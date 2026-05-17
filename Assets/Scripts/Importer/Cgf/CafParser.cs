using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

namespace OpenFarCry.Importer.Cgf
{
    public sealed class CafControllerTrack : IDisposable
    {
        public uint ControllerID;
        public NativeArray<int> Ticks;
        public NativeArray<Vector3> Positions;
        public NativeArray<Quaternion> Rotations;

        public void Dispose()
        {
            if (Ticks.IsCreated) Ticks.Dispose();
            if (Positions.IsCreated) Positions.Dispose();
            if (Rotations.IsCreated) Rotations.Dispose();
        }
    }

    public sealed class CafFile : IDisposable
    {
        public float SecsPerTick = 1f / 30f;
        public int GlobalStartTick;
        public int GlobalEndTick;
        public readonly List<CafControllerTrack> Tracks = new List<CafControllerTrack>();

        public void Dispose()
        {
            foreach (var t in Tracks) t.Dispose();
        }
    }

    public static unsafe class CafParser
    {
        const int FileHeaderSize = 20;
        const int MaxReasonableEntities = 32768;

        const int CtrlTypeCryBone = 1;

        public static CafFile Parse(byte[] data)
        {
            if (data == null || data.Length < FileHeaderSize)
                throw new InvalidDataException("CAF: file is too small.");

            fixed (byte* ptr = data)
            {
                var r = new BinaryBufferReader(ptr, data.Length);

                string signature = ReadSignature(ptr, data.Length);
                if (!signature.StartsWith(CgfConstants.Magic, StringComparison.Ordinal))
                    throw new InvalidDataException($"CAF: bad signature '{signature}', expected '{CgfConstants.Magic}'.");

                r.Offset = 8;
                int fileType = r.ReadInt32();
                int version = r.ReadInt32();
                int chunkTableOffset = r.ReadInt32();

                const int FileTypeGeom = unchecked((int)0xFFFF0000);
                const int FileTypeAnim = unchecked((int)0xFFFF0001);
                if (fileType != FileTypeGeom && fileType != FileTypeAnim)
                    throw new InvalidDataException($"CAF: unsupported FileType 0x{fileType:X8}.");

                if (version != CgfConstants.FileVersion)
                    throw new InvalidDataException($"CAF: unsupported file version 0x{version:X}, expected 0x{CgfConstants.FileVersion:X}.");

                var headers = ReadChunkTable(ptr, data.Length, chunkTableOffset);
                var caf = new CafFile();
                int unsupportedControllerChunks = 0;

                foreach (var h in headers)
                {
                    switch (h.ChunkType)
                    {
                        case CgfConstants.ChunkTiming:
                            ReadTiming(ptr, data.Length, h, caf);
                            break;
                        case CgfConstants.ChunkController:
                            var track = ReadController(ptr, data.Length, h, 1f);
                            if (track != null)
                                caf.Tracks.Add(track);
                            else
                                unsupportedControllerChunks++;
                            break;
                    }
                }

                if (unsupportedControllerChunks > 0)
                {
                    Debug.LogWarning(
                        $"[CgfImporter] CAF skipped {unsupportedControllerChunks} unsupported controller chunk(s). " +
                        "Animations may be incomplete until packed/TCB controller formats are implemented.");
                }

                return caf;
            }
        }

        static string ReadSignature(byte* data, int length)
        {
            byte[] sigBytes = new byte[7];
            for (int i = 0; i < 7; i++) sigBytes[i] = data[i];
            var sigRaw = Encoding.ASCII.GetString(sigBytes);
            int nullPos = sigRaw.IndexOf('\0');
            return nullPos >= 0 ? sigRaw.Substring(0, nullPos) : sigRaw;
        }

        static ChunkHeader[] ReadChunkTable(byte* data, int length, int tableOffset)
        {
            if (tableOffset <= FileHeaderSize || tableOffset > length - 4)
                throw new InvalidDataException($"CAF: invalid ChunkTableOffset {tableOffset}.");

            uint countU = *(uint*)(data + tableOffset);
            if (countU > int.MaxValue)
                throw new InvalidDataException("CAF: chunk count is too large.");
            int count = (int)countU;

            long tableBytes = 4L + (long)count * CgfConstants.ChunkHeaderSize;
            if (tableOffset + tableBytes > length)
                throw new InvalidDataException("CAF: chunk table exceeds file bounds.");

            var headers = new ChunkHeader[count];
            int p = tableOffset + 4;
            for (int i = 0; i < count; i++)
            {
                headers[i] = *(ChunkHeader*)(data + p);
                p += CgfConstants.ChunkHeaderSize;
            }

            var uniqueOffsets = new SortedSet<int>();
            for (int i = 0; i < headers.Length; i++)
            {
                int off = headers[i].FileOffset;
                if (off < FileHeaderSize || off >= tableOffset)
                    throw new InvalidDataException($"CAF: invalid chunk offset {off} for chunk id {headers[i].ChunkID}.");
                uniqueOffsets.Add(off);
            }

            var offsets = new int[uniqueOffsets.Count];
            uniqueOffsets.CopyTo(offsets);

            for (int i = 0; i < headers.Length; i++)
            {
                int off = headers[i].FileOffset;
                int idx = Array.BinarySearch(offsets, off);
                if (idx < 0) idx = ~idx;
                int next = idx + 1 < offsets.Length ? offsets[idx + 1] : tableOffset;
                int size = next - off;
                if (size <= 0)
                    throw new InvalidDataException($"CAF: invalid chunk size at offset {off}.");
                headers[i].SizeBytes = size;
            }

            return headers;
        }

        static void ReadTiming(byte* data, int length, ChunkHeader h, CafFile caf)
        {
            if (h.ChunkVersion != 0x0918)
                return;

            var r = new BinaryBufferReader(data + h.FileOffset, h.SizeBytes);
            if (LooksLikeEmbeddedChunkHeader(ref r, h))
                r.Skip(CgfConstants.ChunkHeaderSize);

            caf.SecsPerTick = r.ReadSingle();
            _ = r.ReadInt32(); // TicksPerFrame

            r.Skip(32); // RANGE_ENTITY name
            caf.GlobalStartTick = r.ReadInt32();
            caf.GlobalEndTick = r.ReadInt32();
            _ = r.ReadInt32(); // nSubRanges
        }

        static CafControllerTrack ReadController(byte* data, int length, ChunkHeader h, float scale)
        {
            switch (h.ChunkVersion)
            {
                case 0x0827:
                    return ReadController0827(data, length, h, scale);
                case 0x0826:
                    return ReadController0826(data, length, h, scale);
                default:
                    return null;
            }
        }

        static CafControllerTrack ReadController0827(byte* data, int length, ChunkHeader h, float scale)
        {
            var r = new BinaryBufferReader(data + h.FileOffset, h.SizeBytes);

            uint numKeysU = r.ReadUInt32();
            if (numKeysU > MaxReasonableEntities)
                throw new InvalidDataException($"CAF Controller0827: unreasonable key count {numKeysU}.");
            int numKeys = (int)numKeysU;
            uint controllerId = r.ReadUInt32();

            var ticks = new NativeArray<int>(numKeys, Allocator.Persistent);
            var positions = new NativeArray<Vector3>(numKeys, Allocator.Persistent);
            var rotations = new NativeArray<Quaternion>(numKeys, Allocator.Persistent);

            var rawRotLogs = new NativeArray<float3>(numKeys, Allocator.TempJob);

            for (int i = 0; i < numKeys; i++)
            {
                ticks[i] = r.ReadInt32();
                positions[i] = r.ReadVector3() * scale;
                rawRotLogs[i] = r.ReadVector3();
            }

            var rotJob = new CafRotationJob
            {
                RawRotationLogs = rawRotLogs,
                OutRotations = rotations.Reinterpret<quaternion>(UnsafeUtility.SizeOf<Quaternion>())
            }.Schedule(numKeys, 64);

            new CafEnsureContinuityJob
            {
                Rotations = rotations.Reinterpret<quaternion>(UnsafeUtility.SizeOf<Quaternion>())
            }.Schedule(rotJob).Complete();

            rawRotLogs.Dispose();

            return new CafControllerTrack
            {
                ControllerID = controllerId,
                Ticks = ticks,
                Positions = positions,
                Rotations = rotations
            };
        }

        static CafControllerTrack ReadController0826(byte* data, int length, ChunkHeader h, float scale)
        {
            var r = new BinaryBufferReader(data + h.FileOffset, h.SizeBytes);
            if (LooksLikeEmbeddedChunkHeader(ref r, h))
                r.Skip(CgfConstants.ChunkHeaderSize);

            int ctrlType = r.ReadInt32();
            int numKeys = r.ReadInt32();
            _ = r.ReadUInt32(); // flags
            uint controllerId = r.ReadUInt32();

            if (numKeys < 0 || numKeys > MaxReasonableEntities)
                throw new InvalidDataException($"CAF Controller0826: invalid key count {numKeys}.");

            if (ctrlType != CtrlTypeCryBone)
            {
                Debug.LogWarning($"[CgfImporter] CAF controller chunk {h.ChunkID} uses unsupported 0x0826 ctrl type {ctrlType}.");
                return null;
            }

            var ticks = new NativeArray<int>(numKeys, Allocator.Persistent);
            var positions = new NativeArray<Vector3>(numKeys, Allocator.Persistent);
            var rotations = new NativeArray<Quaternion>(numKeys, Allocator.Persistent);

            for (int i = 0; i < numKeys; i++)
            {
                ticks[i] = r.ReadInt32();
                r.Skip(12); // abspos
                positions[i] = r.ReadVector3() * scale;
                rotations[i] = r.ReadQuaternion().normalized;
            }

            new CafEnsureContinuityJob
            {
                Rotations = rotations.Reinterpret<quaternion>(UnsafeUtility.SizeOf<Quaternion>())
            }.Run();

            return new CafControllerTrack
            {
                ControllerID = controllerId,
                Ticks = ticks,
                Positions = positions,
                Rotations = rotations
            };
        }

        static bool LooksLikeEmbeddedChunkHeader(ref BinaryBufferReader r, ChunkHeader expected)
        {
            int start = r.Offset;
            if (r.Length - start < CgfConstants.ChunkHeaderSize)
                return false;

            uint type = r.ReadUInt32();
            int version = r.ReadInt32();
            r.Offset = start;

            return type == expected.ChunkType && version == expected.ChunkVersion;
        }
    }
}
