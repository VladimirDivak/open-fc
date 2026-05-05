using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace OpenFarCry.Importer.Cgf
{
    public static class CgfParser
    {
        const int FileHeaderSize = 20; // FILE_HEADER with pack(4): char[7] + pad + 3 ints
        const int MaxReasonableEntities = 32768;

        public static CgfFile Parse(byte[] data)
        {
            if (data == null || data.Length < FileHeaderSize)
                throw new InvalidDataException("CGF: file is too small.");

            string signature = ReadSignature(data);
            if (!signature.StartsWith(CgfConstants.Magic, StringComparison.Ordinal))
                throw new InvalidDataException($"CGF: bad signature '{signature}', expected '{CgfConstants.Magic}'.");

            int fileType = BitConverter.ToInt32(data, 8);
            int version = BitConverter.ToInt32(data, 12);
            int chunkTableOffset = BitConverter.ToInt32(data, 16);

            // Far Cry 1 geometry path uses these values for both CGF and CGA containers.
            const int FileTypeGeom = unchecked((int)0xFFFF0000);
            const int FileTypeAnim = unchecked((int)0xFFFF0001);
            if (fileType != FileTypeGeom && fileType != FileTypeAnim)
                throw new InvalidDataException($"CGF: unsupported FileType 0x{fileType:X8}.");

            if (version != CgfConstants.FileVersion)
                throw new InvalidDataException($"CGF: unsupported file version 0x{version:X}, expected 0x{CgfConstants.FileVersion:X}.");

            var chunks = ReadChunkTable(data, chunkTableOffset);
            var file = new CgfFile
            {
                FileType = fileType,
                Version = version
            };

            foreach (var h in chunks)
            {
                try
                {
                    switch (h.ChunkType)
                    {
                        case CgfConstants.ChunkBoneNameList:
                            if (file.BoneNames == null)
                                file.BoneNames = ReadBoneNameList(data, h);
                            break;
                    }
                }
                catch (Exception ex)
                {
                    throw new InvalidDataException(
                        $"Error in chunk type=0x{h.ChunkType:X} ver=0x{h.ChunkVersion:X} offset=0x{h.FileOffset:X}: {ex.Message}", ex);
                }
            }

            int boneCount = file.BoneNames?.Names.Length ?? 0;
            var unsupportedChunks = new Dictionary<uint, List<int>>();
            foreach (var h in chunks)
            {
                try
                {
                    switch (h.ChunkType)
                    {
                        case CgfConstants.ChunkMesh:
                        {
                            var mesh = ReadMesh(data, h);
                            file.MeshChunks.Add(mesh);
                            file.MeshByChunkID[mesh.ChunkID] = mesh;
                            break;
                        }
                        case CgfConstants.ChunkNode:
                        {
                            var node = ReadNode(data, h);
                            file.NodeChunks.Add(node);
                            file.NodeByChunkID[node.ChunkID] = node;
                            break;
                        }
                        case CgfConstants.ChunkBoneAnim:
                            if (file.BoneAnim == null)
                                file.BoneAnim = ReadBoneAnim(data, h);
                            break;
                        case CgfConstants.ChunkBoneInitPos:
                        {
                            var boneInit = ReadBoneInitPos(data, h, boneCount);
                            file.BoneInitPosByMeshChunkID[boneInit.MeshChunkID] = boneInit;
                            break;
                        }
                        case CgfConstants.ChunkBoneMesh:
                        case CgfConstants.ChunkBoneLightBinding:
                        case CgfConstants.ChunkMeshMorphTarget:
                            RecordUnsupportedChunk(unsupportedChunks, h);
                            break;
                    }
                }
                catch (Exception ex)
                {
                    throw new InvalidDataException(
                        $"Error in chunk type=0x{h.ChunkType:X} ver=0x{h.ChunkVersion:X} offset=0x{h.FileOffset:X}: {ex.Message}", ex);
                }
            }

            LogUnsupportedChunks(unsupportedChunks);
            SelectPrimaryMesh(file);
            return file;
        }

        static string ReadSignature(byte[] data)
        {
            // FILE_HEADER.Signature is char[7], usually "CryTek\0".
            var sigRaw = Encoding.ASCII.GetString(data, 0, 7);
            int nullPos = sigRaw.IndexOf('\0');
            return nullPos >= 0 ? sigRaw.Substring(0, nullPos) : sigRaw;
        }

        static ChunkHeader[] ReadChunkTable(byte[] data, int tableOffset)
        {
            if (tableOffset <= FileHeaderSize || tableOffset > data.Length - 4)
                throw new InvalidDataException($"CGF: invalid ChunkTableOffset {tableOffset}.");

            uint countU = BitConverter.ToUInt32(data, tableOffset);
            if (countU > int.MaxValue)
                throw new InvalidDataException("CGF: chunk count is too large.");
            int count = (int)countU;

            long tableBytes = 4L + (long)count * CgfConstants.ChunkHeaderSize;
            if (tableOffset + tableBytes > data.Length)
                throw new InvalidDataException("CGF: chunk table exceeds file bounds.");

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
                    throw new InvalidDataException($"CGF: invalid chunk offset {off} for chunk id {headers[i].ChunkID}.");
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
                    throw new InvalidDataException($"CGF: invalid chunk size at offset {off}.");
                headers[i].SizeBytes = size;
            }

            return headers;
        }

        static CgfMeshChunk ReadMesh(byte[] data, ChunkHeader h)
        {
            if (h.ChunkVersion != CgfConstants.FileVersion)
                throw new InvalidDataException($"Mesh: unsupported chunk version 0x{h.ChunkVersion:X}.");

            using var r = OpenChunkReader(data, h);
            SkipEmbeddedChunkHeader(r);

            var mesh = new CgfMeshChunk
            {
                ChunkID = h.ChunkID,
                ChunkVersion = h.ChunkVersion,
                HasBoneInfo = r.ReadByte() != 0,
                HasVertexColor = r.ReadByte() != 0
            };

            Align4(r);

            int nVerts = r.ReadInt32();
            int nTVerts = r.ReadInt32();
            int nFaces = r.ReadInt32();
            _ = r.ReadInt32(); // VertAnimID

            ValidateCount(nVerts, "Mesh.nVerts");
            ValidateCount(nTVerts, "Mesh.nTVerts");
            ValidateCount(nFaces, "Mesh.nFaces");

            mesh.Vertices = new CryVertex[nVerts];
            for (int i = 0; i < nVerts; i++)
            {
                mesh.Vertices[i] = new CryVertex
                {
                    PX = r.ReadSingle(), PY = r.ReadSingle(), PZ = r.ReadSingle(),
                    NX = r.ReadSingle(), NY = r.ReadSingle(), NZ = r.ReadSingle(),
                };
            }

            mesh.Faces = new CryFace[nFaces];
            for (int i = 0; i < nFaces; i++)
            {
                mesh.Faces[i] = new CryFace
                {
                    V0 = r.ReadInt32(), V1 = r.ReadInt32(), V2 = r.ReadInt32(),
                    MatID = r.ReadInt32(),
                    SmGroup = r.ReadInt32(),
                };
            }

            mesh.UVs = new CryUV[nTVerts];
            for (int i = 0; i < nTVerts; i++)
                mesh.UVs[i] = new CryUV { U = r.ReadSingle(), V = r.ReadSingle() };

            mesh.TexFaces = new CryTexFace[nFaces];
            for (int i = 0; i < nFaces; i++)
            {
                mesh.TexFaces[i] = new CryTexFace
                {
                    T0 = r.ReadInt32(), T1 = r.ReadInt32(), T2 = r.ReadInt32()
                };
            }

            if (mesh.HasBoneInfo)
            {
                mesh.BoneLinks = new CryLink[nVerts][];
                for (int i = 0; i < nVerts; i++)
                {
                    uint numLinksU = r.ReadUInt32();
                    if (numLinksU > 32u)
                        throw new InvalidDataException($"Mesh: invalid number of links ({numLinksU}) for vertex {i}.");
                    int numLinks = (int)numLinksU;
                    mesh.BoneLinks[i] = new CryLink[numLinks];

                    for (int j = 0; j < numLinks; j++)
                    {
                        mesh.BoneLinks[i][j] = new CryLink
                        {
                            BoneID = r.ReadInt32(),
                            OX = r.ReadSingle(),
                            OY = r.ReadSingle(),
                            OZ = r.ReadSingle(),
                            Blending = r.ReadSingle(),
                        };
                    }
                }
            }

            // CryIRGB is 3 bytes per vertex.
            if (mesh.HasVertexColor)
            {
                int bytes = checked(nVerts * 3);
                ReadBytesExact(r, bytes);
            }

            return mesh;
        }

        static CgfNodeChunk ReadNode(byte[] data, ChunkHeader h)
        {
            if (h.ChunkVersion != 0x0823)
                throw new InvalidDataException($"Node: unsupported chunk version 0x{h.ChunkVersion:X}.");

            using var r = OpenChunkReader(data, h);
            SkipEmbeddedChunkHeader(r);

            var node = new CgfNodeChunk
            {
                ChunkID = h.ChunkID,
                Name = ReadFixedString(r, 64),
                ObjectID = r.ReadInt32(),
                ParentID = r.ReadInt32(),
            };

            int nChildren = r.ReadInt32();
            ValidateCount(nChildren, "Node.nChildren");

            node.MatID = r.ReadInt32();
            _ = r.ReadByte(); // IsGroupHead
            _ = r.ReadByte(); // IsGroupMember

            Align4(r);

            node.Transform = ReadMatrix44(r);
            node.Pos = new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
            node.Rot = new Quaternion(r.ReadSingle(), r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
            node.Scale = new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());

            _ = r.ReadInt32(); // pos_cont_id
            _ = r.ReadInt32(); // rot_cont_id
            _ = r.ReadInt32(); // scl_cont_id

            int propLen = r.ReadInt32();
            if (propLen < 0 || propLen > h.SizeBytes)
                throw new InvalidDataException($"Node: invalid property length {propLen}.");

            node.Properties = propLen > 0
                ? Encoding.ASCII.GetString(ReadBytesExact(r, propLen))
                : string.Empty;

            node.ChildrenIDs = new int[nChildren];
            for (int i = 0; i < nChildren; i++)
                node.ChildrenIDs[i] = r.ReadInt32();

            return node;
        }

        static CgfBoneAnimChunk ReadBoneAnim(byte[] data, ChunkHeader h)
        {
            if (h.ChunkVersion != 0x0290)
                throw new InvalidDataException($"BoneAnim: unsupported chunk version 0x{h.ChunkVersion:X}.");

            using var r = OpenChunkReader(data, h);
            SkipEmbeddedChunkHeader(r);

            int boneCount = r.ReadInt32();
            ValidateCount(boneCount, "BoneAnim.nBones");

            var bones = new CgfBoneEntity[boneCount];
            const int BoneEntityHeadBytes = 4 * sizeof(int); // BoneID, ParentID, ChildrenCount, ControllerID
            int payloadBytes = h.SizeBytes - CgfConstants.ChunkHeaderSize - sizeof(int);
            if (payloadBytes < 0)
                throw new InvalidDataException("BoneAnim: negative payload size.");
            if (boneCount == 0)
                return new CgfBoneAnimChunk { ChunkID = h.ChunkID, Bones = bones };
            if (payloadBytes % boneCount != 0)
                throw new InvalidDataException($"BoneAnim: payload size {payloadBytes} is not divisible by nBones {boneCount}.");

            int boneEntitySize = payloadBytes / boneCount;
            if (boneEntitySize < BoneEntityHeadBytes)
                throw new InvalidDataException($"BoneAnim: invalid BONE_ENTITY size {boneEntitySize}.");
            int boneEntityTailBytes = boneEntitySize - BoneEntityHeadBytes;
            Debug.Log($"[CgfImporter] BoneAnim chunk {h.ChunkID}: nBones={boneCount}, BONE_ENTITY size={boneEntitySize} bytes.");

            for (int i = 0; i < boneCount; i++)
            {
                bones[i] = new CgfBoneEntity
                {
                    BoneID = r.ReadInt32(),
                    ParentID = r.ReadInt32(),
                    ChildrenCount = r.ReadInt32(),
                    ControllerID = r.ReadUInt32()
                };

                if (boneEntityTailBytes > 0)
                    ReadBytesExact(r, boneEntityTailBytes);
            }

            return new CgfBoneAnimChunk
            {
                ChunkID = h.ChunkID,
                Bones = bones
            };
        }

        static CgfBoneNameListChunk ReadBoneNameList(byte[] data, ChunkHeader h)
        {
            using var r = OpenChunkReader(data, h);
            var names = new List<string>();

            switch (h.ChunkVersion)
            {
                case 0x0744:
                {
                    SkipEmbeddedChunkHeader(r);
                    int nEntities = r.ReadInt32();
                    ValidateCount(nEntities, "BoneNameList.nEntities");

                    names.Capacity = nEntities;
                    for (int i = 0; i < nEntities; i++)
                        names.Add(ReadFixedString(r, CgfConstants.BoneNameEntitySize0744));
                    break;
                }

                case 0x0745:
                {
                    int nEntities = r.ReadInt32();
                    ValidateCount(nEntities, "BoneNameList.numEntities");

                    names.Capacity = nEntities;
                    for (int i = 0; i < nEntities; i++)
                        names.Add(ReadCString(r));
                    break;
                }

                default:
                    throw new InvalidDataException($"BoneNameList: unsupported chunk version 0x{h.ChunkVersion:X}.");
            }

            return new CgfBoneNameListChunk { Names = names.ToArray() };
        }

        static CgfBoneInitPosChunk ReadBoneInitPos(byte[] data, ChunkHeader h, int fallbackBoneCount)
        {
            if (h.ChunkVersion != 0x0001)
                throw new InvalidDataException($"BoneInitialPos: unsupported chunk version 0x{h.ChunkVersion:X}.");

            using var r = OpenChunkReader(data, h);

            uint meshChunkIdU = r.ReadUInt32();
            uint numBonesU = r.ReadUInt32();
            if (numBonesU > MaxReasonableEntities)
                throw new InvalidDataException($"BoneInitialPos: unreasonable bone count {numBonesU}.");

            int numBones = (int)numBonesU;
            int boneCount = fallbackBoneCount > 0 ? fallbackBoneCount : numBones;
            var matrices = new Matrix4x4[boneCount];

            for (int i = 0; i < numBones; i++)
            {
                var m = ReadMatrix43(r);
                if (i < boneCount)
                    matrices[i] = m;
            }

            return new CgfBoneInitPosChunk
            {
                MeshChunkID = (int)meshChunkIdU,
                BindMatrices = matrices
            };
        }

        static void SelectPrimaryMesh(CgfFile file)
        {
            int selectedMeshId = -1;
            foreach (var node in file.NodeChunks)
            {
                if (file.MeshByChunkID.ContainsKey(node.ObjectID))
                {
                    selectedMeshId = node.ObjectID;
                    break;
                }
            }

            if (selectedMeshId == -1 && file.MeshChunks.Count > 0)
                selectedMeshId = file.MeshChunks[0].ChunkID;

            file.SelectedMeshChunkID = selectedMeshId;
            if (selectedMeshId != -1 && file.MeshByChunkID.TryGetValue(selectedMeshId, out var mesh))
                file.MeshChunk = mesh;
            else
                file.MeshChunk = null;

            if (selectedMeshId != -1 && file.BoneInitPosByMeshChunkID.TryGetValue(selectedMeshId, out var selectedBindPoses))
            {
                file.BoneInitPos = selectedBindPoses;
                return;
            }

            foreach (var kv in file.BoneInitPosByMeshChunkID)
            {
                file.BoneInitPos = kv.Value;
                return;
            }
        }

        static void RecordUnsupportedChunk(Dictionary<uint, List<int>> chunksByType, ChunkHeader h)
        {
            if (!chunksByType.TryGetValue(h.ChunkType, out var ids))
            {
                ids = new List<int>();
                chunksByType[h.ChunkType] = ids;
            }

            ids.Add(h.ChunkID);
        }

        static void LogUnsupportedChunks(Dictionary<uint, List<int>> chunksByType)
        {
            foreach (var kv in chunksByType)
            {
                var ids = kv.Value;
                if (ids == null || ids.Count == 0)
                    continue;

                ids.Sort();
                Debug.LogWarning(
                    $"[CgfImporter] CGF contains {ids.Count} unsupported {GetChunkTypeName(kv.Key)} chunk(s) " +
                    $"({FormatChunkIdList(ids)}); {GetUnsupportedChunkImpact(kv.Key)}");
            }
        }

        static string GetChunkTypeName(uint chunkType)
        {
            switch (chunkType)
            {
                case CgfConstants.ChunkBoneMesh:
                    return "BoneMesh";
                case CgfConstants.ChunkBoneLightBinding:
                    return "BoneLightBinding";
                case CgfConstants.ChunkMeshMorphTarget:
                    return "MeshMorphTarget";
                default:
                    return $"0x{chunkType:X8}";
            }
        }

        static string GetUnsupportedChunkImpact(uint chunkType)
        {
            switch (chunkType)
            {
                case CgfConstants.ChunkBoneMesh:
                    return "bone collision/physics geometry is ignored.";
                case CgfConstants.ChunkBoneLightBinding:
                    return "bone-attached lights are ignored.";
                case CgfConstants.ChunkMeshMorphTarget:
                    return "morph targets are ignored.";
                default:
                    return "related data is ignored.";
            }
        }

        static string FormatChunkIdList(List<int> ids)
        {
            if (ids.Count == 1)
                return $"id {ids[0]}";

            bool contiguous = true;
            for (int i = 1; i < ids.Count; i++)
            {
                if (ids[i] != ids[i - 1] + 1)
                {
                    contiguous = false;
                    break;
                }
            }

            if (contiguous)
                return $"ids {ids[0]}-{ids[ids.Count - 1]}";

            var sb = new StringBuilder("ids ");
            for (int i = 0; i < ids.Count; i++)
            {
                if (i > 0)
                    sb.Append(", ");
                sb.Append(ids[i]);
            }

            return sb.ToString();
        }

        static BinaryReader OpenChunkReader(byte[] data, ChunkHeader h)
        {
            if (h.FileOffset < 0 || h.SizeBytes <= 0 || h.FileOffset + h.SizeBytes > data.Length)
                throw new InvalidDataException($"Chunk {h.ChunkID}: out-of-bounds range offset={h.FileOffset}, size={h.SizeBytes}.");

            var ms = new MemoryStream(data, h.FileOffset, h.SizeBytes, writable: false);
            return new BinaryReader(ms, Encoding.ASCII, leaveOpen: false);
        }

        static void SkipEmbeddedChunkHeader(BinaryReader r)
        {
            ReadBytesExact(r, CgfConstants.ChunkHeaderSize);
        }

        static void Align4(BinaryReader r)
        {
            long pos = r.BaseStream.Position;
            long aligned = (pos + 3L) & ~3L;
            if (aligned > r.BaseStream.Length)
                throw new EndOfStreamException("Alignment moved past chunk end.");
            r.BaseStream.Position = aligned;
        }

        static string ReadFixedString(BinaryReader r, int length)
        {
            var bytes = ReadBytesExact(r, length);
            int nullPos = Array.IndexOf(bytes, (byte)0);
            int strLen = nullPos >= 0 ? nullPos : bytes.Length;
            return Encoding.ASCII.GetString(bytes, 0, strLen);
        }

        static string ReadCString(BinaryReader r)
        {
            using var ms = new MemoryStream(64);
            while (true)
            {
                byte b = r.ReadByte();
                if (b == 0)
                    break;
                ms.WriteByte(b);
                if (ms.Length > 4096)
                    throw new InvalidDataException("CString is too long.");
            }

            return Encoding.ASCII.GetString(ms.ToArray());
        }

        static byte[] ReadBytesExact(BinaryReader r, int count)
        {
            var bytes = r.ReadBytes(count);
            if (bytes.Length != count)
                throw new EndOfStreamException($"Truncated chunk data: expected {count} bytes, got {bytes.Length}.");
            return bytes;
        }

        static void ValidateCount(int value, string label)
        {
            if (value < 0 || value > MaxReasonableEntities)
                throw new InvalidDataException($"{label} has invalid value {value}.");
        }

        // Row-major 4x4 -> Unity Matrix4x4 (column-major constructor).
        static Matrix4x4 ReadMatrix44(BinaryReader r)
        {
            float[,] m = new float[4, 4];
            for (int row = 0; row < 4; row++)
                for (int col = 0; col < 4; col++)
                    m[row, col] = r.ReadSingle();

            return new Matrix4x4(
                new Vector4(m[0, 0], m[1, 0], m[2, 0], m[3, 0]),
                new Vector4(m[0, 1], m[1, 1], m[2, 1], m[3, 1]),
                new Vector4(m[0, 2], m[1, 2], m[2, 2], m[3, 2]),
                new Vector4(m[0, 3], m[1, 3], m[2, 3], m[3, 3])
            );
        }

        // Row-major 4x3 (12 floats) -> Unity Matrix4x4.
        static Matrix4x4 ReadMatrix43(BinaryReader r)
        {
            float[,] m = new float[4, 3];
            for (int row = 0; row < 4; row++)
                for (int col = 0; col < 3; col++)
                    m[row, col] = r.ReadSingle();

            // SBoneInitPosMatrix stores a 3x3 basis plus translation in the 4th row:
            // [ r00 r01 r02 ]
            // [ r10 r11 r12 ]
            // [ r20 r21 r22 ]
            // [ tx  ty  tz  ]
            return new Matrix4x4(
                new Vector4(m[0, 0], m[1, 0], m[2, 0], 0f),
                new Vector4(m[0, 1], m[1, 1], m[2, 1], 0f),
                new Vector4(m[0, 2], m[1, 2], m[2, 2], 0f),
                new Vector4(m[3, 0], m[3, 1], m[3, 2], 1f)
            );
        }
    }
}
