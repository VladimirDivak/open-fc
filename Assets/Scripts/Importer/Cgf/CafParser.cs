using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace OpenFarCry.Importer.Cgf
{
    public sealed class CafControllerTrack
    {
        public uint ControllerID;
        public int[] Ticks;
        public Vector3[] Positions;
        public Quaternion[] Rotations;
    }

    public sealed class CafFile
    {
        public float SecsPerTick = 1f / 30f;
        public int GlobalStartTick;
        public int GlobalEndTick;
        public readonly List<CafControllerTrack> Tracks = new List<CafControllerTrack>();
    }

    public static class CafParser
    {
        const int FileHeaderSize = 20;
        const int MaxReasonableEntities = 32768;

        const int CtrlTypeCryBone = 1;

        public static CafFile Parse(byte[] data)
        {
            if (data == null || data.Length < FileHeaderSize)
                throw new InvalidDataException("CAF: file is too small.");

            string signature = ReadSignature(data);
            if (!signature.StartsWith(CgfConstants.Magic, StringComparison.Ordinal))
                throw new InvalidDataException($"CAF: bad signature '{signature}', expected '{CgfConstants.Magic}'.");

            int fileType = BitConverter.ToInt32(data, 8);
            int version = BitConverter.ToInt32(data, 12);
            int chunkTableOffset = BitConverter.ToInt32(data, 16);

            const int FileTypeGeom = unchecked((int)0xFFFF0000);
            const int FileTypeAnim = unchecked((int)0xFFFF0001);
            if (fileType != FileTypeGeom && fileType != FileTypeAnim)
                throw new InvalidDataException($"CAF: unsupported FileType 0x{fileType:X8}.");

            if (version != CgfConstants.FileVersion)
                throw new InvalidDataException($"CAF: unsupported file version 0x{version:X}, expected 0x{CgfConstants.FileVersion:X}.");

            var headers = ReadChunkTable(data, chunkTableOffset);
            var caf = new CafFile();
            int unsupportedControllerChunks = 0;

            foreach (var h in headers)
            {
                switch (h.ChunkType)
                {
                    case CgfConstants.ChunkTiming:
                        ReadTiming(data, h, caf);
                        break;
                    case CgfConstants.ChunkController:
                        var track = ReadController(data, h, 1f);
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

        static string ReadSignature(byte[] data)
        {
            var sigRaw = System.Text.Encoding.ASCII.GetString(data, 0, 7);
            int nullPos = sigRaw.IndexOf('\0');
            return nullPos >= 0 ? sigRaw.Substring(0, nullPos) : sigRaw;
        }

        static ChunkHeader[] ReadChunkTable(byte[] data, int tableOffset)
        {
            if (tableOffset <= FileHeaderSize || tableOffset > data.Length - 4)
                throw new InvalidDataException($"CAF: invalid ChunkTableOffset {tableOffset}.");

            uint countU = BitConverter.ToUInt32(data, tableOffset);
            if (countU > int.MaxValue)
                throw new InvalidDataException("CAF: chunk count is too large.");
            int count = (int)countU;

            long tableBytes = 4L + (long)count * CgfConstants.ChunkHeaderSize;
            if (tableOffset + tableBytes > data.Length)
                throw new InvalidDataException("CAF: chunk table exceeds file bounds.");

            var headers = new ChunkHeader[count];
            int p = tableOffset + 4;
            for (int i = 0; i < count; i++)
            {
                headers[i] = new ChunkHeader
                {
                    ChunkType = BitConverter.ToUInt32(data, p + 0),
                    ChunkVersion = BitConverter.ToInt32(data, p + 4),
                    FileOffset = BitConverter.ToInt32(data, p + 8),
                    ChunkID = BitConverter.ToInt32(data, p + 12),
                };
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

        static void ReadTiming(byte[] data, ChunkHeader h, CafFile caf)
        {
            if (h.ChunkVersion != 0x0918)
                return;

            using var r = OpenChunkReader(data, h);
            if (LooksLikeEmbeddedChunkHeader(r, h))
                SkipEmbeddedChunkHeader(r);

            caf.SecsPerTick = r.ReadSingle();
            _ = r.ReadInt32(); // TicksPerFrame

            ReadBytesExact(r, 32); // RANGE_ENTITY name
            caf.GlobalStartTick = r.ReadInt32();
            caf.GlobalEndTick = r.ReadInt32();
            _ = r.ReadInt32(); // nSubRanges
        }

        static CafControllerTrack ReadController(byte[] data, ChunkHeader h, float scale)
        {
            switch (h.ChunkVersion)
            {
                case 0x0827:
                    return ReadController0827(data, h, scale);
                case 0x0826:
                    return ReadController0826(data, h, scale);
                default:
                    return null;
            }
        }

        static CafControllerTrack ReadController0827(byte[] data, ChunkHeader h, float scale)
        {
            using var r = OpenChunkReader(data, h);

            uint numKeysU = r.ReadUInt32();
            if (numKeysU > MaxReasonableEntities)
                throw new InvalidDataException($"CAF Controller0827: unreasonable key count {numKeysU}.");
            int numKeys = (int)numKeysU;
            uint controllerId = r.ReadUInt32();

            var ticks = new int[numKeys];
            var positions = new Vector3[numKeys];
            var rotations = new Quaternion[numKeys];

            for (int i = 0; i < numKeys; i++)
            {
                ticks[i] = r.ReadInt32();
                var pos = new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
                var rotLog = new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());

                positions[i] = pos * scale;
                rotations[i] = QuaternionFromRotationLog(rotLog);
            }

            EnsureQuaternionContinuity(rotations);

            return new CafControllerTrack
            {
                ControllerID = controllerId,
                Ticks = ticks,
                Positions = positions,
                Rotations = rotations
            };
        }

        static CafControllerTrack ReadController0826(byte[] data, ChunkHeader h, float scale)
        {
            using var r = OpenChunkReader(data, h);
            if (LooksLikeEmbeddedChunkHeader(r, h))
                SkipEmbeddedChunkHeader(r);

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

            var ticks = new int[numKeys];
            var positions = new Vector3[numKeys];
            var rotations = new Quaternion[numKeys];

            Quaternion last = Quaternion.identity;
            bool haveLast = false;
            for (int i = 0; i < numKeys; i++)
            {
                ticks[i] = r.ReadInt32();

                // abspos is present in file but not required for local pose tracks.
                _ = r.ReadSingle();
                _ = r.ReadSingle();
                _ = r.ReadSingle();

                var relPos = new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
                var relRot = new Quaternion(r.ReadSingle(), r.ReadSingle(), r.ReadSingle(), r.ReadSingle());

                if (haveLast && Quaternion.Dot(last, relRot) < 0f)
                    relRot = new Quaternion(-relRot.x, -relRot.y, -relRot.z, -relRot.w);

                positions[i] = relPos * scale;
                rotations[i] = relRot.normalized;
                last = rotations[i];
                haveLast = true;
            }

            return new CafControllerTrack
            {
                ControllerID = controllerId,
                Ticks = ticks,
                Positions = positions,
                Rotations = rotations
            };
        }

        static void EnsureQuaternionContinuity(Quaternion[] rotations)
        {
            if (rotations == null || rotations.Length < 2)
                return;

            for (int i = 1; i < rotations.Length; i++)
            {
                if (Quaternion.Dot(rotations[i - 1], rotations[i]) < 0f)
                    rotations[i] = new Quaternion(-rotations[i].x, -rotations[i].y, -rotations[i].z, -rotations[i].w);
            }
        }

        static Quaternion QuaternionFromRotationLog(Vector3 logVec)
        {
            double d = Math.Sqrt(logVec.x * logVec.x + logVec.y * logVec.y + logVec.z * logVec.z);
            if (d > 1e-4)
            {
                double m = Math.Sin(d) / d;
                var q = new Quaternion(
                    (float)(logVec.x * m),
                    (float)(logVec.y * m),
                    (float)(logVec.z * m),
                    (float)Math.Cos(d));
                return q.normalized;
            }

            return new Quaternion(logVec.x, logVec.y, logVec.z, (float)(1.0 - d * d)).normalized;
        }

        static BinaryReader OpenChunkReader(byte[] data, ChunkHeader h)
        {
            if (h.FileOffset < 0 || h.SizeBytes <= 0 || h.FileOffset + h.SizeBytes > data.Length)
                throw new InvalidDataException($"Chunk {h.ChunkID}: out-of-bounds range offset={h.FileOffset}, size={h.SizeBytes}.");

            var ms = new MemoryStream(data, h.FileOffset, h.SizeBytes, writable: false);
            return new BinaryReader(ms, System.Text.Encoding.ASCII, leaveOpen: false);
        }

        static bool LooksLikeEmbeddedChunkHeader(BinaryReader r, ChunkHeader expected)
        {
            long start = r.BaseStream.Position;
            if (r.BaseStream.Length - start < CgfConstants.ChunkHeaderSize)
                return false;

            uint type = r.ReadUInt32();
            int version = r.ReadInt32();
            _ = r.ReadInt32();
            _ = r.ReadInt32();
            r.BaseStream.Position = start;

            return type == expected.ChunkType && version == expected.ChunkVersion;
        }

        static void SkipEmbeddedChunkHeader(BinaryReader r)
        {
            ReadBytesExact(r, CgfConstants.ChunkHeaderSize);
        }

        static byte[] ReadBytesExact(BinaryReader r, int count)
        {
            var bytes = r.ReadBytes(count);
            if (bytes.Length != count)
                throw new EndOfStreamException($"Truncated chunk data: expected {count} bytes, got {bytes.Length}.");
            return bytes;
        }
    }
}
