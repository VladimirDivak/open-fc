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
    public static unsafe class CgfParser
    {
        const int FileHeaderSize = 20; // FILE_HEADER with pack(4): char[7] + pad + 3 ints
        const int MaxReasonableEntities = 32768;

        public static CgfFile Parse(byte[] data)
        {
            if (data == null || data.Length < FileHeaderSize)
                throw new InvalidDataException("CGF: file is too small.");

            fixed (byte* ptr = data)
            {
                var r = new BinaryBufferReader(ptr, data.Length);

                string signature = ReadSignature(ptr, data.Length);
                if (!signature.StartsWith(CgfConstants.Magic, StringComparison.Ordinal))
                    throw new InvalidDataException($"CGF: bad signature '{signature}', expected '{CgfConstants.Magic}'.");

                r.Offset = 8;
                int fileType = r.ReadInt32();
                int version = r.ReadInt32();
                int chunkTableOffset = r.ReadInt32();

                // Far Cry 1 geometry path uses these values for both CGF and CGA containers.
                const int FileTypeGeom = unchecked((int)0xFFFF0000);
                const int FileTypeAnim = unchecked((int)0xFFFF0001);
                if (fileType != FileTypeGeom && fileType != FileTypeAnim)
                    throw new InvalidDataException($"CGF: unsupported FileType 0x{fileType:X8}.");

                if (version != CgfConstants.FileVersion)
                    throw new InvalidDataException($"CGF: unsupported file version 0x{version:X}, expected 0x{CgfConstants.FileVersion:X}.");

                var chunks = ReadChunkTable(ptr, data.Length, chunkTableOffset);
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
                                    file.BoneNames = ReadBoneNameList(ptr, data.Length, h);
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
                                var mesh = ReadMesh(ptr, data.Length, h);
                                file.MeshChunks.Add(mesh);
                                file.MeshByChunkID[mesh.ChunkID] = mesh;
                                break;
                            }
                            case CgfConstants.ChunkNode:
                            {
                                var node = ReadNode(ptr, data.Length, h);
                                file.NodeChunks.Add(node);
                                file.NodeByChunkID[node.ChunkID] = node;
                                break;
                            }
                            case CgfConstants.ChunkBoneAnim:
                                if (file.BoneAnim == null)
                                    file.BoneAnim = ReadBoneAnim(ptr, data.Length, h);
                                break;
                            case CgfConstants.ChunkBoneInitPos:
                            {
                                var boneInit = ReadBoneInitPos(ptr, data.Length, h, boneCount);
                                file.BoneInitPosByMeshChunkID[boneInit.MeshChunkID] = boneInit;
                                break;
                            }
                            case CgfConstants.ChunkBoneMesh:
                            {
                                var boneMesh = ReadBoneMesh(ptr, data.Length, h);
                                file.BoneMeshChunks.Add(boneMesh);
                                file.BoneMeshByChunkID[boneMesh.ChunkID] = boneMesh;
                                break;
                            }
                            case CgfConstants.ChunkMtl:
                            {
                                var mtl = ReadMaterialChunk(ptr, data.Length, h);
                                mtl.TableIndex = file.MaterialChunks.Count;
                                file.MaterialChunks.Add(mtl);
                                file.MaterialByChunkID[mtl.ChunkID] = mtl;
                                break;
                            }
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

                foreach (var mtl in file.MaterialChunks)
                    if (mtl.MtlType != CgfMtlType.Multi)
                        file.LeafMaterials.Add(mtl);
                BuildMaterialHierarchy(file);

                return file;
            }
        }

        static string ReadSignature(byte* data, int length)
        {
            // FILE_HEADER.Signature is char[7], usually "CryTek\0".
            byte[] sigBytes = new byte[7];
            for (int i = 0; i < 7; i++) sigBytes[i] = data[i];
            var sigRaw = Encoding.ASCII.GetString(sigBytes);
            int nullPos = sigRaw.IndexOf('\0');
            return nullPos >= 0 ? sigRaw.Substring(0, nullPos) : sigRaw;
        }

        static ChunkHeader[] ReadChunkTable(byte* data, int length, int tableOffset)
        {
            if (tableOffset <= FileHeaderSize || tableOffset > length - 4)
                throw new InvalidDataException($"CGF: invalid ChunkTableOffset {tableOffset}.");

            uint countU = *(uint*)(data + tableOffset);
            if (countU > int.MaxValue)
                throw new InvalidDataException("CGF: chunk count is too large.");
            int count = (int)countU;

            long tableBytes = 4L + (long)count * CgfConstants.ChunkHeaderSize;
            if (tableOffset + tableBytes > length)
                throw new InvalidDataException("CGF: chunk table exceeds file bounds.");

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

        static CgfMeshChunk ReadMesh(byte* data, int length, ChunkHeader h)
        {
            if (h.ChunkVersion != CgfConstants.FileVersion)
                throw new InvalidDataException($"Mesh: unsupported chunk version 0x{h.ChunkVersion:X}.");

            var r = new BinaryBufferReader(data + h.FileOffset, h.SizeBytes);
            r.Skip(CgfConstants.ChunkHeaderSize);

            var mesh = new CgfMeshChunk
            {
                ChunkID = h.ChunkID,
                ChunkVersion = h.ChunkVersion,
                HasBoneInfo = r.ReadByte() != 0,
                HasVertexColor = r.ReadByte() != 0
            };

            r.Align(4);

            int nVerts = r.ReadInt32();
            int nTVerts = r.ReadInt32();
            int nFaces = r.ReadInt32();
            _ = r.ReadInt32(); // VertAnimID

            ValidateCount(nVerts, "Mesh.nVerts");
            ValidateCount(nTVerts, "Mesh.nTVerts");
            ValidateCount(nFaces, "Mesh.nFaces");

            mesh.Vertices = new NativeArray<CryVertex>(nVerts, Allocator.Persistent);
            if (nVerts > 0)
            {
                UnsafeUtility.MemCpy(mesh.Vertices.GetUnsafePtr(), r.ReadBytesPtr(nVerts * sizeof(CryVertex)), nVerts * sizeof(CryVertex));
            }

            mesh.Faces = new NativeArray<CryFace>(nFaces, Allocator.Persistent);
            if (nFaces > 0)
            {
                UnsafeUtility.MemCpy(mesh.Faces.GetUnsafePtr(), r.ReadBytesPtr(nFaces * sizeof(CryFace)), nFaces * sizeof(CryFace));
            }

            mesh.UVs = new NativeArray<CryUV>(nTVerts, Allocator.Persistent);
            if (nTVerts > 0)
            {
                UnsafeUtility.MemCpy(mesh.UVs.GetUnsafePtr(), r.ReadBytesPtr(nTVerts * sizeof(CryUV)), nTVerts * sizeof(CryUV));
            }

            if (nTVerts > 0)
            {
                mesh.TexFaces = new NativeArray<CryTexFace>(nFaces, Allocator.Persistent);
                if (nFaces > 0)
                {
                    UnsafeUtility.MemCpy(mesh.TexFaces.GetUnsafePtr(), r.ReadBytesPtr(nFaces * sizeof(CryTexFace)), nFaces * sizeof(CryTexFace));
                }
            }
            else
            {
                mesh.TexFaces = new NativeArray<CryTexFace>(0, Allocator.Persistent);
            }

            if (mesh.HasBoneInfo)
            {
                mesh.BoneLinkOffsets = new NativeArray<int>(nVerts, Allocator.Persistent);
                mesh.BoneLinkCounts  = new NativeArray<int>(nVerts, Allocator.Persistent);

                // First pass: count links to allocate flattened buffer
                int totalLinks = 0;
                int savedOffset = r.Offset;
                for (int i = 0; i < nVerts; i++)
                {
                    uint numLinksU = r.ReadUInt32();
                    if (numLinksU > 32u)
                        throw new InvalidDataException($"Mesh: invalid number of links ({numLinksU}) for vertex {i}.");
                    totalLinks += (int)numLinksU;
                    r.Skip((int)numLinksU * sizeof(CryLink));
                }

                mesh.BoneLinks = new NativeArray<CryLink>(totalLinks, Allocator.Persistent);
                r.Offset = savedOffset;

                int currentLink = 0;
                for (int i = 0; i < nVerts; i++)
                {
                    int numLinks = (int)r.ReadUInt32();
                    mesh.BoneLinkCounts[i] = numLinks;
                    mesh.BoneLinkOffsets[i] = currentLink;

                    if (numLinks > 0)
                    {
                        UnsafeUtility.MemCpy(
                            (CryLink*)mesh.BoneLinks.GetUnsafePtr() + currentLink,
                            r.ReadBytesPtr(numLinks * sizeof(CryLink)),
                            numLinks * sizeof(CryLink));
                        currentLink += numLinks;
                    }
                }
            }

            // CryIRGB is 3 bytes per vertex.
            if (mesh.HasVertexColor)
            {
                r.Skip(nVerts * 3);
            }

            return mesh;
        }

        static CgfNodeChunk ReadNode(byte* data, int length, ChunkHeader h)
        {
            if (h.ChunkVersion != 0x0823)
                throw new InvalidDataException($"Node: unsupported chunk version 0x{h.ChunkVersion:X}.");

            var r = new BinaryBufferReader(data + h.FileOffset, h.SizeBytes);
            r.Skip(CgfConstants.ChunkHeaderSize);

            var node = new CgfNodeChunk
            {
                ChunkID = h.ChunkID,
                Name = ReadFixedString(ref r, 64),
                ObjectID = r.ReadInt32(),
                ParentID = r.ReadInt32(),
            };

            int nChildren = r.ReadInt32();
            ValidateCount(nChildren, "Node.nChildren");

            node.MatID = r.ReadInt32();
            _ = r.ReadByte(); // IsGroupHead
            _ = r.ReadByte(); // IsGroupMember

            r.Align(4);

            node.Transform = r.ReadMatrix44();
            node.Pos = r.ReadVector3();
            node.Rot = r.ReadQuaternion();
            node.Scale = r.ReadVector3();

            _ = r.ReadInt32(); // pos_cont_id
            _ = r.ReadInt32(); // rot_cont_id
            _ = r.ReadInt32(); // scl_cont_id

            int propLen = r.ReadInt32();
            if (propLen < 0 || propLen > h.SizeBytes)
                throw new InvalidDataException($"Node: invalid property length {propLen}.");

            node.Properties = propLen > 0
                ? Encoding.ASCII.GetString(ReadBytesExact(ref r, propLen))
                : string.Empty;

            node.ChildrenIDs = new int[nChildren];
            for (int i = 0; i < nChildren; i++)
                node.ChildrenIDs[i] = r.ReadInt32();

            return node;
        }

        static CgfBoneAnimChunk ReadBoneAnim(byte* data, int length, ChunkHeader h)
        {
            if (h.ChunkVersion != 0x0290)
                throw new InvalidDataException($"BoneAnim: unsupported chunk version 0x{h.ChunkVersion:X}.");

            var r = new BinaryBufferReader(data + h.FileOffset, h.SizeBytes);
            r.Skip(CgfConstants.ChunkHeaderSize);

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

            for (int i = 0; i < boneCount; i++)
            {
                var entity = new CgfBoneEntity
                {
                    BoneID = r.ReadInt32(),
                    ParentID = r.ReadInt32(),
                    ChildrenCount = r.ReadInt32(),
                    ControllerID = r.ReadUInt32(),
                    Physics = new CgfBonePhysics
                    {
                        PhysGeomChunkID = -1,
                        FrameMatrix = Matrix4x4.identity
                    }
                };

                if (boneEntityTailBytes > 0)
                {
                    var tail = ReadBytesExact(ref r, boneEntityTailBytes);
                    ParseBoneEntityTail(tail, ref entity);
                }

                bones[i] = entity;
            }

            return new CgfBoneAnimChunk
            {
                ChunkID = h.ChunkID,
                Bones = bones
            };
        }

        static CgfBoneMeshChunk ReadBoneMesh(byte* data, int length, ChunkHeader h)
        {
            var mesh = ReadMesh(data, length, h);
            return new CgfBoneMeshChunk
            {
                ChunkID = h.ChunkID,
                Mesh = mesh
            };
        }

        static void ParseBoneEntityTail(byte[] tailBytes, ref CgfBoneEntity entity)
        {
            if (tailBytes == null || tailBytes.Length == 0)
                return;

            fixed (byte* ptr = tailBytes)
            {
                var r = new BinaryBufferReader(ptr, tailBytes.Length);

                // BONE_ENTITY tail in FC 1:
                // prop[32], BONE_PHYSICS_COMP { int nPhysGeom, int flags, ... }.
                if (tailBytes.Length >= 32)
                {
                    entity.Properties = ReadFixedString(ref r, 32);
                }
                else
                {
                    entity.Properties = string.Empty;
                    return;
                }

                if (tailBytes.Length - r.Offset < 8)
                    return;

                var phys = new CgfBonePhysics
                {
                    PhysGeomChunkID = r.ReadInt32(),
                    Flags = r.ReadInt32(),
                    FrameMatrix = Matrix4x4.identity
                };

                if (tailBytes.Length - r.Offset >= 96)
                {
                    phys.MinAngles = r.ReadVector3();
                    phys.MaxAngles = r.ReadVector3();
                    phys.SpringAngle = r.ReadVector3();
                    phys.SpringTension = r.ReadVector3();
                    phys.Damping = r.ReadVector3();
                    phys.FrameMatrix = ReadMatrix33(ref r);
                }

                entity.Physics = phys;
            }
        }

        static CgfBoneNameListChunk ReadBoneNameList(byte* data, int length, ChunkHeader h)
        {
            var r = new BinaryBufferReader(data + h.FileOffset, h.SizeBytes);
            var names = new List<string>();

            switch (h.ChunkVersion)
            {
                case 0x0744:
                {
                    r.Skip(CgfConstants.ChunkHeaderSize);
                    int nEntities = r.ReadInt32();
                    ValidateCount(nEntities, "BoneNameList.nEntities");

                    names.Capacity = nEntities;
                    for (int i = 0; i < nEntities; i++)
                        names.Add(ReadFixedString(ref r, CgfConstants.BoneNameEntitySize0744));
                    break;
                }

                case 0x0745:
                {
                    int nEntities = r.ReadInt32();
                    ValidateCount(nEntities, "BoneNameList.numEntities");

                    names.Capacity = nEntities;
                    for (int i = 0; i < nEntities; i++)
                        names.Add(ReadCString(ref r));
                    break;
                }

                default:
                    throw new InvalidDataException($"BoneNameList: unsupported chunk version 0x{h.ChunkVersion:X}.");
            }

            return new CgfBoneNameListChunk { Names = names.ToArray() };
        }

        static CgfBoneInitPosChunk ReadBoneInitPos(byte* data, int length, ChunkHeader h, int fallbackBoneCount)
        {
            if (h.ChunkVersion != 0x0001)
                throw new InvalidDataException($"BoneInitialPos: unsupported chunk version 0x{h.ChunkVersion:X}.");

            var r = new BinaryBufferReader(data + h.FileOffset, h.SizeBytes);

            uint meshChunkIdU = r.ReadUInt32();
            uint numBonesU = r.ReadUInt32();
            if (numBonesU > MaxReasonableEntities)
                throw new InvalidDataException($"BoneInitialPos: unreasonable bone count {numBonesU}.");

            int numBones = (int)numBonesU;
            int boneCount = fallbackBoneCount > 0 ? fallbackBoneCount : numBones;
            var matrices = new Matrix4x4[boneCount];

            for (int i = 0; i < numBones; i++)
            {
                var m = ReadMatrix43(ref r);
                if (i < boneCount)
                    matrices[i] = m;
            }

            return new CgfBoneInitPosChunk
            {
                MeshChunkID = (int)meshChunkIdU,
                BindMatrices = matrices
            };
        }

        // Texture order in 0x0745/0746: a, d, s, o, b, g, c/fl, rl, subsurf, det.
        static CgfMaterialChunk ReadMaterialChunk(byte* data, int length, ChunkHeader h)
        {
            var r = new BinaryBufferReader(data + h.FileOffset, h.SizeBytes);
            r.Skip(CgfConstants.ChunkHeaderSize);

            var chunk = new CgfMaterialChunk
            {
                ChunkID      = h.ChunkID,
                ChunkVersion = h.ChunkVersion,
                Opacity      = 1f,
            };

            if (h.ChunkVersion == 0x0746)
            {
                chunk.Name       = ReadFixedString(ref r, 64);
                chunk.ShaderName = ExtractShaderName(chunk.Name);
                r.Skip(60);                          // Reserved[60]
                chunk.AlphaTest  = r.ReadSingle();
                chunk.MtlType    = (CgfMtlType)r.ReadInt32();
                if (chunk.MtlType == CgfMtlType.Multi)
                {
                    chunk.ChildCount = r.ReadInt32();
                    return chunk;
                }
                chunk.DiffuseColor  = ReadCryIRGB(ref r);
                chunk.SpecularColor = ReadCryIRGB(ref r);     // col_s[3]
                r.Skip(3);                            // col_a[3]
                r.Skip(3);                            // pack(4) padding: 3×CryIRGB = 9 bytes → float at +12
                chunk.SpecLevel     = r.ReadSingle();
                chunk.SpecShininess = r.ReadSingle();
                r.ReadSingle();                            // selfIllum
                chunk.Opacity = r.ReadSingle();
                // TextureMap3 order: tex_a(0), tex_d(1), tex_s(2), tex_o(3), tex_b(4), tex_g(5), ...
                r.Skip(236);                          // skip tex_a (full 236 bytes)
                chunk.DiffuseTextureName  = NormalizeTextureName(ReadFixedString(ref r, 128));
                r.Skip(108);                          // skip rest of tex_d TextureMap3
                chunk.SpecularTextureName = NormalizeTextureName(ReadFixedString(ref r, 128));
                r.Skip(108);                          // skip rest of tex_s TextureMap3
                chunk.OpacityTextureName  = NormalizeTextureName(ReadFixedString(ref r, 128));
                r.Skip(108);                          // skip rest of tex_o TextureMap3
                chunk.NormalTextureName   = NormalizeTextureName(ReadFixedString(ref r, 128));
                r.Skip(108);                          // skip rest of tex_b TextureMap3
                chunk.GlossTextureName    = NormalizeTextureName(ReadFixedString(ref r, 128));
                r.Skip(108);                          // skip rest of tex_g TextureMap3
                r.Skip(4 * 236);                      // skip tex_fl, tex_rl, tex_subsurf, tex_det
                chunk.Flags = (CgfMtlFlags)r.ReadInt32();
            }
            else if (h.ChunkVersion == 0x0745)
            {
                chunk.Name       = ReadFixedString(ref r, 64);
                chunk.ShaderName = ExtractShaderName(chunk.Name);
                chunk.MtlType    = (CgfMtlType)r.ReadInt32();
                if (chunk.MtlType == CgfMtlType.Multi)
                {
                    chunk.ChildCount = r.ReadInt32();
                    return chunk;
                }
                chunk.DiffuseColor  = ReadCryIRGB(ref r);
                chunk.SpecularColor = ReadCryIRGB(ref r);     // col_s[3]
                r.Skip(3);                            // col_a[3]
                r.Skip(3);                            // pack(4) padding: 3×CryIRGB = 9 bytes → float at +12
                chunk.SpecLevel     = r.ReadSingle();
                chunk.SpecShininess = r.ReadSingle();
                r.ReadSingle();                            // selfIllum
                chunk.Opacity = r.ReadSingle();
                // TextureMap2 order: tex_a(0), tex_d(1), tex_s(2), tex_o(3), tex_b(4), tex_g(5), ...
                r.Skip(108);                          // skip tex_a
                chunk.DiffuseTextureName  = NormalizeTextureName(ReadFixedString(ref r, 32));
                r.Skip(76);                           // skip rest of tex_d TextureMap2
                chunk.SpecularTextureName = NormalizeTextureName(ReadFixedString(ref r, 32));
                r.Skip(76);                           // skip rest of tex_s TextureMap2
                chunk.OpacityTextureName  = NormalizeTextureName(ReadFixedString(ref r, 32));
                r.Skip(76);                           // skip rest of tex_o TextureMap2
                chunk.NormalTextureName   = NormalizeTextureName(ReadFixedString(ref r, 32));
                r.Skip(76);                           // skip rest of tex_b TextureMap2
                chunk.GlossTextureName    = NormalizeTextureName(ReadFixedString(ref r, 32));
                r.Skip(76);                           // skip rest of tex_g TextureMap2
                r.Skip(4 * 108);                      // skip tex_fl, tex_rl, tex_subsurf, tex_det
                chunk.Flags = (CgfMtlFlags)r.ReadInt32();
            }
            else if (h.ChunkVersion == 0x0744)
            {
                chunk.Name       = ReadFixedString(ref r, 64);
                chunk.ShaderName = ExtractShaderName(chunk.Name);
                chunk.MtlType    = (CgfMtlType)r.ReadInt32();
                if (chunk.MtlType == CgfMtlType.Multi)
                {
                    chunk.ChildCount = r.ReadInt32();
                    return chunk;
                }
                chunk.DiffuseColor  = ReadCryIRGB(ref r);
                chunk.SpecularColor = ReadCryIRGB(ref r);     // col_s[3]
                r.Skip(3);                            // col_a[3]
                // TextureMap order: tex_d(0), tex_o(1), tex_b(2); each 92 bytes (name[32]+60)
                chunk.DiffuseTextureName = NormalizeTextureName(ReadFixedString(ref r, 32));
                r.Skip(60);                           // skip rest of tex_d TextureMap
                chunk.OpacityTextureName = NormalizeTextureName(ReadFixedString(ref r, 32));
                r.Skip(60);                           // skip rest of tex_o TextureMap
                chunk.NormalTextureName  = NormalizeTextureName(ReadFixedString(ref r, 32));
                r.Skip(60);                           // skip rest of tex_b TextureMap
            }
            else
            {
                // Unknown version — try to read at least the name.
                if (h.SizeBytes > CgfConstants.ChunkHeaderSize + 64)
                {
                    chunk.Name       = ReadFixedString(ref r, 64);
                    chunk.ShaderName = ExtractShaderName(chunk.Name);
                }
                Debug.LogWarning($"[CgfImporter] MTL chunk {h.ChunkID}: unknown version 0x{h.ChunkVersion:X}, material name only.");
            }

            return chunk;
        }

        static string ExtractShaderName(string mtlName)
        {
            if (string.IsNullOrEmpty(mtlName))
                return string.Empty;
            int open  = mtlName.IndexOf('(');
            int close = mtlName.IndexOf(')');
            if (open >= 0 && close > open)
                return mtlName.Substring(open + 1, close - open - 1).Trim().ToLowerInvariant();
            int slash = mtlName.IndexOf('/');
            string raw = slash > 0 ? mtlName.Substring(0, slash) : mtlName;
            return raw.Trim().ToLowerInvariant();
        }

        static string NormalizeTextureName(string raw)
        {
            if (string.IsNullOrEmpty(raw))
                return string.Empty;

            string normalized = raw.Replace('\\', '/').Replace("\0", string.Empty).Trim().ToLowerInvariant();
            if (normalized.Length == 0)
                return string.Empty;

            int rootedIndex = FindKnownTextureRootIndex(normalized);
            if (rootedIndex > 0)
                normalized = normalized.Substring(rootedIndex);

            while (normalized.Length > 0)
            {
                char c = normalized[0];
                if (char.IsLetterOrDigit(c) || c == '_' || c == '/' || c == '.')
                    break;
                normalized = normalized.Substring(1);
            }

            if (normalized.StartsWith("./"))
                normalized = normalized.Substring(2);
            while (normalized.StartsWith("/"))
                normalized = normalized.Substring(1);

            return normalized;
        }

        static int FindKnownTextureRootIndex(string value)
        {
            int best = -1;
            best = MinPositive(best, value.IndexOf("objects/", StringComparison.Ordinal));
            best = MinPositive(best, value.IndexOf("textures/", StringComparison.Ordinal));
            best = MinPositive(best, value.IndexOf("levels/", StringComparison.Ordinal));
            best = MinPositive(best, value.IndexOf("terrain/", StringComparison.Ordinal));
            best = MinPositive(best, value.IndexOf("characters/", StringComparison.Ordinal));
            return best;
        }

        static int MinPositive(int current, int candidate)
        {
            if (candidate < 0)
                return current;
            if (current < 0)
                return candidate;
            return candidate < current ? candidate : current;
        }

        static void BuildMaterialHierarchy(CgfFile file)
        {
            file.MaterialChildrenByParentChunkID.Clear();
            var chunks = file.MaterialChunks;
            for (int i = 0; i < chunks.Count; i++)
            {
                var root = chunks[i];
                if (root == null || root.MtlType != CgfMtlType.Multi || root.ChildCount <= 0)
                    continue;

                var children = new List<CgfMaterialChunk>(root.ChildCount);
                for (int cursor = i + 1; cursor < chunks.Count && children.Count < root.ChildCount; cursor++)
                {
                    var child = chunks[cursor];
                    if (child == null || child.MtlType == CgfMtlType.Multi)
                        continue;
                    children.Add(child);
                }

                if (children.Count > 0)
                    file.MaterialChildrenByParentChunkID[root.ChunkID] = children;
            }
        }

        static void SelectPrimaryMesh(CgfFile file)
        {
            int selectedMeshId = -1;
            int proxyFallbackId = -1;
            foreach (var node in file.NodeChunks)
            {
                if (!file.MeshByChunkID.ContainsKey(node.ObjectID))
                    continue;
                bool isProxy = node.Name != null &&
                               node.Name.IndexOf("proxy", StringComparison.OrdinalIgnoreCase) >= 0;
                if (!isProxy)
                {
                    selectedMeshId = node.ObjectID;
                    break;
                }
                if (proxyFallbackId == -1)
                    proxyFallbackId = node.ObjectID;
            }

            if (selectedMeshId == -1)
                selectedMeshId = proxyFallbackId;

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

        static Color32 ReadCryIRGB(ref BinaryBufferReader r)
        {
            byte red   = r.ReadByte();
            byte green = r.ReadByte();
            byte blue  = r.ReadByte();
            return new Color32(red, green, blue, 255);
        }

        static string ReadFixedString(ref BinaryBufferReader r, int length)
        {
            byte* ptr = r.ReadBytesPtr(length);
            int len = 0;
            while (len < length && ptr[len] != 0) len++;
            return Encoding.ASCII.GetString(ptr, len);
        }

        static string ReadCString(ref BinaryBufferReader r)
        {
            byte* start = r.ReadBytesPtr(0);
            int len = 0;
            while (!r.IsAtEnd && r.ReadByte() != 0) len++;
            return Encoding.ASCII.GetString(start, len);
        }

        static byte[] ReadBytesExact(ref BinaryBufferReader r, int count)
        {
            byte* src = r.ReadBytesPtr(count);
            byte[] dst = new byte[count];
            fixed (byte* d = dst)
            {
                Unity.Collections.LowLevel.Unsafe.UnsafeUtility.MemCpy(d, src, count);
            }
            return dst;
        }

        static void ValidateCount(int value, string label)
        {
            if (value < 0 || value > MaxReasonableEntities)
                throw new InvalidDataException($"{label} has invalid value {value}.");
        }

        // Row-major 4x3 (12 floats) -> Unity Matrix4x4.
        static Matrix4x4 ReadMatrix43(ref BinaryBufferReader r)
        {
            // Row-major 4x3: row 0, row 1, row 2, row 3
            var m = Matrix4x4.identity;
            m.m00 = r.ReadSingle(); m.m01 = r.ReadSingle(); m.m02 = r.ReadSingle();
            m.m10 = r.ReadSingle(); m.m11 = r.ReadSingle(); m.m12 = r.ReadSingle();
            m.m20 = r.ReadSingle(); m.m21 = r.ReadSingle(); m.m22 = r.ReadSingle();
            m.m30 = r.ReadSingle(); m.m31 = r.ReadSingle(); m.m32 = r.ReadSingle();
            return m;
        }

        static Vector3 ReadVector3(ref BinaryBufferReader r)
        {
            return new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
        }

        static Matrix4x4 ReadMatrix33(ref BinaryBufferReader r)
        {
            // Row-major 3x3
            var m = Matrix4x4.identity;
            m.m00 = r.ReadSingle(); m.m01 = r.ReadSingle(); m.m02 = r.ReadSingle();
            m.m10 = r.ReadSingle(); m.m11 = r.ReadSingle(); m.m12 = r.ReadSingle();
            m.m20 = r.ReadSingle(); m.m21 = r.ReadSingle(); m.m22 = r.ReadSingle();
            return m;
        }
    }
}
