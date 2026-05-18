using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace OpenFarCry.Importer.Cgf
{
    public class BuildResult
    {
        public Mesh        Mesh;
        public Mesh        ColliderMesh; // nodraw/proxy faces split off the visual mesh; null when none
        public int         MeshChunkID;
        public string      SourceNodeName;
        public int[]       SubmeshMaterialIds; // submesh index -> original Cry face MatID
        public bool        HasSkeleton;
        public string[]    BoneNames;   // null if !HasSkeleton
        public Matrix4x4[] BindPoses;   // Unity-space inverse bind matrices
        public int[]       BoneIdToIndex;
        public int[]       BoneIndexToId;
        public Vector3     NodeLocalOffset; // node transform translation in Unity space; zero for skeletal meshes
    }

    public static class CgfMeshBuilder
    {
        internal sealed class PreparedBuild
        {
            public readonly BuildResult Result;
            public readonly MeshBuildData Data;
            public readonly string MeshName; // CGF-derived display name

            public PreparedBuild(BuildResult result, MeshBuildData data, string meshName)
            {
                Result = result;
                Data = data;
                MeshName = meshName;
            }
        }

        internal sealed class MeshBuildData : IDisposable
        {
            public NativeArray<float3> Positions;
            public NativeArray<float3> Normals;
            public NativeArray<float2> Uvs;
            public NativeArray<BoneWeight> BoneWeights;
            public Matrix4x4[] BindPoses;

            // visual submesh: face MatID -> triangle indices (collision faces excluded)
            public readonly Dictionary<int, NativeArray<int>> SubmeshTriangles = new Dictionary<int, NativeArray<int>>();
            // nodraw/proxy faces routed out of the visual mesh, indices into the shared vertex buffer
            public readonly List<int> ColliderTriangles = new List<int>();

            public void Dispose()
            {
                if (Positions.IsCreated) Positions.Dispose();
                if (Normals.IsCreated)   Normals.Dispose();
                if (Uvs.IsCreated)       Uvs.Dispose();
                if (BoneWeights.IsCreated) BoneWeights.Dispose();
                foreach (var na in SubmeshTriangles.Values)
                    if (na.IsCreated) na.Dispose();
            }
        }

        internal readonly struct VertexRemappingResult
        {
            public readonly Dictionary<int, List<int>> SubmeshMap;
            public readonly int[] UniquePosIdx;
            public readonly int[] UniqueUvIdx;

            public VertexRemappingResult(Dictionary<int, List<int>> submeshMap, int[] uniquePosIdx, int[] uniqueUvIdx)
            {
                SubmeshMap   = submeshMap;
                UniquePosIdx = uniquePosIdx;
                UniqueUvIdx  = uniqueUvIdx;
            }
        }

        public const string MeshCacheVersionName = "CGFMesh_NodeMatrixOld_v16_nodrawsplit";

        public static BuildResult Build(CgfFile cgf, bool importSkeleton = true, float importScale = 1f)
        {
            var prepared = PrepareBuild(cgf, importSkeleton, importScale);
            return UploadPrepared(prepared);
        }

        internal static PreparedBuild PrepareBuild(CgfFile cgf, bool importSkeleton = true, float importScale = 1f)
        {
            if (cgf.MeshChunk == null)
                throw new InvalidOperationException("CGF file has no Mesh chunk.");

            var mesh = cgf.MeshChunk;
            bool selectedHasBones = importSkeleton &&
                                    mesh.HasBoneInfo &&
                                    cgf.BoneNames != null &&
                                    cgf.BoneNames.Names.Length > 0;
            var nodeTransform = BuildStaticNodeTransform(cgf, mesh.ChunkID);
            string sourceNodeName = null;

            for (int i = 0; i < cgf.NodeChunks.Count; i++)
            {
                var node = cgf.NodeChunks[i];
                if (node.ObjectID == mesh.ChunkID)
                {
                    sourceNodeName = node.Name;
                    break;
                }
            }

            var result = new BuildResult
            {
                MeshChunkID = mesh.ChunkID,
                SourceNodeName = sourceNodeName,
            };

            string meshName = ResolveMeshName(cgf, sourceNodeName);

            // Global leaf-material indices that are collision-only; uniform across all nodes.
            var collisionMatIds = CgfNoDrawFaceClassifier.Classify(cgf);

            if (!selectedHasBones && TryBuildStaticMeshParts(cgf, out var staticParts))
            {
                var staticData = BuildStaticCombinedMeshData(staticParts, importScale, collisionMatIds);
                if (string.IsNullOrEmpty(result.SourceNodeName) && staticParts.Count > 0)
                    result.SourceNodeName = staticParts[0].NodeName;
                return new PreparedBuild(result, staticData, meshName);
            }

            var meshData = BuildMeshData(mesh, nodeTransform, cgf.BoneNames, cgf.BoneAnim, cgf.BoneInitPos, result, importSkeleton, importScale, collisionMatIds);
            return new PreparedBuild(result, meshData, meshName);
        }

        // Original-derived mesh name for readable errors/assets: CGF file name without
        // extension, falling back to the source node name, then a generic constant.
        static string ResolveMeshName(CgfFile cgf, string sourceNodeName)
        {
            if (!string.IsNullOrEmpty(cgf?.SourceVirtualPath))
            {
                string fileName = System.IO.Path.GetFileNameWithoutExtension(cgf.SourceVirtualPath);
                if (!string.IsNullOrEmpty(fileName))
                    return fileName;
            }

            return !string.IsNullOrEmpty(sourceNodeName) ? sourceNodeName : "CgfMesh";
        }

        internal static BuildResult UploadPrepared(PreparedBuild prepared)
        {
            if (prepared == null)
                throw new ArgumentNullException(nameof(prepared));
            if (prepared.Result == null)
                throw new ArgumentException("Prepared build result is null.", nameof(prepared));
            if (prepared.Data == null)
                throw new ArgumentException("Prepared build data is null.", nameof(prepared));

            prepared.Result.Mesh = CreateUnityMesh(
                prepared.Data,
                out var submeshMaterialIds,
                out var colliderMesh);
            prepared.Result.SubmeshMaterialIds = submeshMaterialIds;
            prepared.Result.ColliderMesh = colliderMesh;

            // Name meshes after the original CGF so runtime errors are distinguishable.
            // The CgfMeshBuilder version is tracked separately (cache key + CgfMeshBuildStamp).
            string meshName = string.IsNullOrEmpty(prepared.MeshName) ? "CgfMesh" : prepared.MeshName;
            prepared.Result.Mesh.name = meshName;
            if (colliderMesh != null)
                colliderMesh.name = meshName + "_collider";

            prepared.Data.Dispose();
            return prepared.Result;
        }

        public static Matrix4x4 BuildStaticNodeTransform(CgfFile cgf, int meshChunkId)
        {
            if (cgf?.NodeChunks == null)
                return Matrix4x4.identity;

            for (int i = 0; i < cgf.NodeChunks.Count; i++)
            {
                var node = cgf.NodeChunks[i];
                if (node.ObjectID == meshChunkId)
                    return BuildAccumulatedNodeTransform(cgf, node);
            }

            return Matrix4x4.identity;
        }

        readonly struct StaticMeshPart
        {
            public readonly CgfMeshChunk Chunk;
            public readonly Matrix4x4 NodeTransform;
            public readonly string NodeName;

            public StaticMeshPart(CgfMeshChunk chunk, Matrix4x4 nodeTransform, string nodeName)
            {
                Chunk = chunk;
                NodeTransform = nodeTransform;
                NodeName = nodeName;
            }
        }

        static bool TryBuildStaticMeshParts(CgfFile cgf, out List<StaticMeshPart> parts)
        {
            parts = null;
            if (cgf?.NodeChunks == null || cgf.MeshByChunkID == null)
                return false;

            var collected = new List<StaticMeshPart>();
            for (int i = 0; i < cgf.NodeChunks.Count; i++)
            {
                var node = cgf.NodeChunks[i];
                // Skip physics-proxy nodes — they are excluded from visual mesh and handled
                // separately by TryBuildFromProxyNodeMesh for collider generation.
                if (node.Name != null &&
                    node.Name.IndexOf("proxy", StringComparison.OrdinalIgnoreCase) >= 0)
                    continue;
                if (!cgf.MeshByChunkID.TryGetValue(node.ObjectID, out var chunk))
                    continue;
                if (chunk == null || chunk.HasBoneInfo || !chunk.Vertices.IsCreated || !chunk.Faces.IsCreated)
                    continue;

                collected.Add(new StaticMeshPart(
                    chunk,
                    BuildAccumulatedNodeTransform(cgf, node),
                    node.Name));
            }

            if (collected.Count == 0)
                return false;

            parts = collected;
            return true;
        }

        static MeshBuildData BuildMeshData(
            CgfMeshChunk chunk,
            Matrix4x4 nodeTransform,
            CgfBoneNameListChunk boneNames,
            CgfBoneAnimChunk boneAnim,
            CgfBoneInitPosChunk  boneInitPos,
            BuildResult result,
            bool importSkeleton,
            float importScale,
            HashSet<int> collisionMatIds)
        {
            bool hasBones = importSkeleton && chunk.HasBoneInfo && boneNames != null && boneNames.Names.Length > 0;

            if (!hasBones)
                return BuildStaticMeshData(chunk, nodeTransform, importScale, collisionMatIds);

            int[] boneIdToIndex = null;
            int[] boneIndexToId = null;
            string[] orderedBoneNames = null;
            BuildBoneIndexMapsOrIdentity(boneAnim, boneNames.Names.Length, out boneIdToIndex, out boneIndexToId);
            orderedBoneNames = BuildOrderedBoneNames(boneNames.Names, boneIndexToId);

            var bindGlobalsByBoneId = BuildBindPoseGlobalMatricesByBoneId(boneInitPos, boneNames.Names.Length, importScale);
            var nativeBindGlobals = new NativeArray<float4x4>(bindGlobalsByBoneId.Length, Allocator.TempJob);
            for (int i = 0; i < bindGlobalsByBoneId.Length; i++) nativeBindGlobals[i] = (float4x4)bindGlobalsByBoneId[i];

            var remap = BuildVertexRemapping(chunk.Faces, chunk.TexFaces, chunk.Vertices.Length,
                chunk.UVs.IsCreated ? chunk.UVs.Length : 0);
            int n = remap.UniquePosIdx.Length;

            var data = new MeshBuildData();
            data.Positions = new NativeArray<float3>(n, Allocator.Persistent);
            data.Normals   = new NativeArray<float3>(n, Allocator.Persistent);
            data.Uvs       = new NativeArray<float2>(n, Allocator.Persistent);
            data.BoneWeights = new NativeArray<BoneWeight>(n, Allocator.Persistent);

            var nativePosIdx = new NativeArray<int>(remap.UniquePosIdx, Allocator.TempJob);
            var nativeUvIdx  = new NativeArray<int>(remap.UniqueUvIdx,  Allocator.TempJob);
            var nativeBoneIdToIndex = new NativeArray<int>(boneIdToIndex, Allocator.TempJob);

            var safeLinks       = chunk.BoneLinks.IsCreated ? chunk.BoneLinks : new NativeArray<CryLink>(1, Allocator.TempJob);
            var safeLinkOffsets = chunk.BoneLinkOffsets.IsCreated ? chunk.BoneLinkOffsets : new NativeArray<int>(1, Allocator.TempJob);
            var safeLinkCounts  = chunk.BoneLinkCounts.IsCreated ? chunk.BoneLinkCounts : new NativeArray<int>(1, Allocator.TempJob);

            var transformJob = new SkinnedVertexTransformJob
            {
                UniquePosIdx = nativePosIdx,
                RawVertices = chunk.Vertices,
                RawLinks = safeLinks,
                LinkOffsets = safeLinkOffsets,
                LinkCounts = safeLinkCounts,
                BindGlobalsByBoneId = nativeBindGlobals,
                ImportScale = importScale,
                OutPositions = data.Positions,
                OutNormals = data.Normals
            }.Schedule(n, 64);

            var weightJob = new BoneWeightBuildJob
            {
                UniquePosIdx = nativePosIdx,
                RawLinks = safeLinks,
                LinkOffsets = safeLinkOffsets,
                LinkCounts = safeLinkCounts,
                BoneIdToIndex = nativeBoneIdToIndex,
                BoneCount = boneNames.Names.Length,
                OutWeights = data.BoneWeights
            }.Schedule(n, 64);

            var dummyPos = new NativeArray<float3>(n, Allocator.TempJob);
            var dummyNrm = new NativeArray<float3>(n, Allocator.TempJob);

            var safeUvs = chunk.UVs.IsCreated ? chunk.UVs : new NativeArray<CryUV>(1, Allocator.TempJob);

            // UVs and Index remap
            new StaticVertexTransformJob
            {
                UniquePosIdx = nativePosIdx,
                UniqueUvIdx = nativeUvIdx,
                RawVertices = chunk.Vertices,
                RawUVs = safeUvs,
                NodeMatrix = float4x4.identity, // Already baked into positions for skinned
                ImportScale = 1f,
                UvCount = chunk.UVs.IsCreated ? chunk.UVs.Length : 0,
                OutPositions = dummyPos,
                OutNormals = dummyNrm,
                OutUvs = data.Uvs
            }.Schedule(n, 64).Complete();

            JobHandle.CombineDependencies(transformJob, weightJob).Complete();

            if (!chunk.UVs.IsCreated) safeUvs.Dispose();
            if (!chunk.BoneLinks.IsCreated) safeLinks.Dispose();
            if (!chunk.BoneLinkOffsets.IsCreated) safeLinkOffsets.Dispose();
            if (!chunk.BoneLinkCounts.IsCreated) safeLinkCounts.Dispose();

            foreach (var kv in remap.SubmeshMap)
            {
                if (collisionMatIds != null && collisionMatIds.Contains(kv.Key))
                    data.ColliderTriangles.AddRange(kv.Value);
                else
                    data.SubmeshTriangles[kv.Key] = new NativeArray<int>(kv.Value.ToArray(), Allocator.Persistent);
            }

            nativePosIdx.Dispose();
            nativeUvIdx.Dispose();
            nativeBindGlobals.Dispose();
            nativeBoneIdToIndex.Dispose();
            dummyPos.Dispose();
            dummyNrm.Dispose();

            result.HasSkeleton = true;
            result.BoneNames   = orderedBoneNames;
            result.BoneIdToIndex = boneIdToIndex;
            result.BoneIndexToId = boneIndexToId;
            result.BindPoses   = BuildBindPoses(boneInitPos, boneIndexToId, importScale);
            data.BindPoses = result.BindPoses;

            return data;
        }

        static MeshBuildData BuildStaticMeshData(
            CgfMeshChunk chunk,
            Matrix4x4 nodeTransform,
            float importScale,
            HashSet<int> collisionMatIds)
        {
            var data = new MeshBuildData();
            var unityNodeTransform = CryTransformConversion.NodeMatrixInImporterSpace(nodeTransform, importScale);

            bool hasUvs = chunk.UVs.IsCreated && chunk.UVs.Length > 0 &&
                          chunk.TexFaces.IsCreated && chunk.TexFaces.Length == chunk.Faces.Length;
            var remap = BuildVertexRemapping(chunk.Faces, chunk.TexFaces, chunk.Vertices.Length,
                hasUvs ? chunk.UVs.Length : 0);
            int n = remap.UniquePosIdx.Length;

            data.Positions = new NativeArray<float3>(n, Allocator.Persistent);
            data.Normals   = new NativeArray<float3>(n, Allocator.Persistent);
            data.Uvs       = new NativeArray<float2>(n, Allocator.Persistent);

            RunStaticVertexTransformJob(remap, chunk.Vertices, hasUvs ? chunk.UVs : default,
                unityNodeTransform, importScale,
                data.Positions, data.Normals, data.Uvs);

            foreach (var kv in remap.SubmeshMap)
            {
                if (collisionMatIds != null && collisionMatIds.Contains(kv.Key))
                    data.ColliderTriangles.AddRange(kv.Value);
                else
                    data.SubmeshTriangles[kv.Key] = new NativeArray<int>(kv.Value.ToArray(), Allocator.Persistent);
            }
            return data;
        }

        static MeshBuildData BuildStaticCombinedMeshData(
            List<StaticMeshPart> parts,
            float importScale,
            HashSet<int> collisionMatIds)
        {
            var data = new MeshBuildData();

            // Pass 1: compute remapping for every part and total vert count
            var remaps          = new VertexRemappingResult[parts.Count];
            var unityTransforms = new Matrix4x4[parts.Count];
            var partHasUvs      = new bool[parts.Count];
            int totalVerts      = 0;

            for (int i = 0; i < parts.Count; i++)
            {
                var chunk = parts[i].Chunk;
                unityTransforms[i] = CryTransformConversion.NodeMatrixInImporterSpace(parts[i].NodeTransform, importScale);
                bool hasUvs = chunk.UVs.IsCreated && chunk.UVs.Length > 0 &&
                              chunk.TexFaces.IsCreated && chunk.TexFaces.Length == chunk.Faces.Length;
                partHasUvs[i] = hasUvs;
                remaps[i] = BuildVertexRemapping(chunk.Faces, chunk.TexFaces, chunk.Vertices.Length,
                    hasUvs ? chunk.UVs.Length : 0);
                totalVerts += remaps[i].UniquePosIdx.Length;
            }

            data.Positions = new NativeArray<float3>(totalVerts, Allocator.Persistent);
            data.Normals   = new NativeArray<float3>(totalVerts, Allocator.Persistent);
            data.Uvs       = new NativeArray<float2>(totalVerts, Allocator.Persistent);

            // Pass 2: per-part Burst jobs into output slices; merge submesh maps with offset
            int vertexOffset = 0;
            for (int i = 0; i < parts.Count; i++)
            {
                var chunk = parts[i].Chunk;
                var remap = remaps[i];
                int partVerts = remap.UniquePosIdx.Length;

                RunStaticVertexTransformJob(remap, chunk.Vertices, partHasUvs[i] ? chunk.UVs : default,
                    unityTransforms[i], importScale,
                    data.Positions.GetSubArray(vertexOffset, partVerts),
                    data.Normals.GetSubArray(vertexOffset, partVerts),
                    data.Uvs.GetSubArray(vertexOffset, partVerts));

                foreach (var kv in remap.SubmeshMap)
                {
                    // Collision-only faces go to the collider mesh, never the visual mesh.
                    // Face MatID is a global leaf-material index uniform across all nodes.
                    if (collisionMatIds != null && collisionMatIds.Contains(kv.Key))
                    {
                        var collisionTris = kv.Value;
                        for (int t = 0; t < collisionTris.Count; t++)
                            data.ColliderTriangles.Add(collisionTris[t] + vertexOffset);
                        continue;
                    }

                    if (!data.SubmeshTriangles.TryGetValue(kv.Key, out var existing))
                    {
                        var arr = new NativeArray<int>(kv.Value.Count, Allocator.Persistent);
                        var srcIndices = new NativeArray<int>(kv.Value.ToArray(), Allocator.TempJob);
                        new StaticIndexRemapJob
                        {
                            SourceIndices = srcIndices,
                            OutIndices = arr,
                            VertexOffset = vertexOffset
                        }.Run();
                        srcIndices.Dispose();
                        data.SubmeshTriangles[kv.Key] = arr;
                    }
                    else
                    {
                        // Append to existing NativeArray (slow, but combined meshes usually have few submeshes)
                        var old = existing;
                        var combined = new NativeArray<int>(old.Length + kv.Value.Count, Allocator.Persistent);
                        NativeArray<int>.Copy(old, combined, old.Length);
                        var srcIndices = new NativeArray<int>(kv.Value.ToArray(), Allocator.TempJob);
                        new StaticIndexRemapJob
                        {
                            SourceIndices = srcIndices,
                            OutIndices = combined.GetSubArray(old.Length, kv.Value.Count),
                            VertexOffset = vertexOffset
                        }.Run();
                        srcIndices.Dispose();
                        data.SubmeshTriangles[kv.Key] = combined;
                        old.Dispose();
                    }
                }
                vertexOffset += partVerts;
            }

            return data;
        }

        internal static VertexRemappingResult BuildVertexRemapping(
            NativeArray<CryFace> faces, NativeArray<CryTexFace> texFaces, int vertCount, int uvCount)
        {
            var submeshMap   = new Dictionary<int, List<int>>();
            var uniquePosIdx = new List<int>();
            var uniqueUvIdx  = new List<int>();
            var vertCache    = new Dictionary<(int pi, int ti), int>();

            bool hasTexFaces = texFaces.IsCreated && texFaces.Length == faces.Length;

            for (int fi = 0; fi < faces.Length; fi++)
            {
                int matID = faces[fi].MatID;
                if (!submeshMap.TryGetValue(matID, out var triList))
                {
                    triList = new List<int>();
                    submeshMap[matID] = triList;
                }

                int t0 = hasTexFaces ? texFaces[fi].T0 : 0;
                int t1 = hasTexFaces ? texFaces[fi].T1 : 0;
                int t2 = hasTexFaces ? texFaces[fi].T2 : 0;

                Remap(faces[fi].V0, t0, triList);
                Remap(faces[fi].V1, t1, triList);
                Remap(faces[fi].V2, t2, triList);
            }

            void Remap(int pi, int ti, List<int> tris)
            {
                if (pi < 0 || pi >= vertCount) return;
                var key = (pi, ti);
                if (!vertCache.TryGetValue(key, out int idx))
                {
                    idx = uniquePosIdx.Count;
                    uniquePosIdx.Add(pi);
                    uniqueUvIdx.Add(uvCount > 0 && ti >= 0 && ti < uvCount ? ti : -1);
                    vertCache[key] = idx;
                }
                tris.Add(idx);
            }

            return new VertexRemappingResult(submeshMap, uniquePosIdx.ToArray(), uniqueUvIdx.ToArray());
        }

        static void RunStaticVertexTransformJob(
            VertexRemappingResult remap,
            NativeArray<CryVertex> verts, NativeArray<CryUV> rawUVs,
            Matrix4x4 unityNodeTransform, float importScale,
            NativeArray<float3> outPositions,
            NativeArray<float3> outNormals,
            NativeArray<float2> outUvs)
        {
            int uvCount = rawUVs.IsCreated ? rawUVs.Length : 0;

            var nativePosIdx = new NativeArray<int>(remap.UniquePosIdx, Allocator.TempJob);
            var nativeUvIdx  = new NativeArray<int>(remap.UniqueUvIdx,  Allocator.TempJob);
            var safeUvs      = rawUVs.IsCreated ? rawUVs : new NativeArray<CryUV>(1, Allocator.TempJob);

            new StaticVertexTransformJob
            {
                UniquePosIdx = nativePosIdx,
                UniqueUvIdx  = nativeUvIdx,
                RawVertices  = verts,
                RawUVs       = safeUvs,
                NodeMatrix   = (float4x4)unityNodeTransform,
                ImportScale  = importScale,
                UvCount      = uvCount,
                OutPositions = outPositions,
                OutNormals   = outNormals,
                OutUvs       = outUvs,
            }.Schedule(remap.UniquePosIdx.Length, 64).Complete();

            nativePosIdx.Dispose();
            nativeUvIdx.Dispose();
            if (!rawUVs.IsCreated) safeUvs.Dispose();
        }

        static Mesh CreateUnityMesh(
            MeshBuildData data,
            out int[] submeshMaterialIds,
            out Mesh colliderMesh)
        {
            var mesh = new Mesh { name = MeshCacheVersionName };

            int vertCount = data.Positions.Length;
            // Since we use NativeArray<int> for indices, we always use UInt32 format.
            // Small meshes could use UInt16 but it requires casting NativeArrays.
            mesh.indexFormat = IndexFormat.UInt32;

            // Direct buffer upload
            var layout = new[]
            {
                new VertexAttributeDescriptor(VertexAttribute.Position, VertexAttributeFormat.Float32, 3, stream: 0),
                new VertexAttributeDescriptor(VertexAttribute.Normal,   VertexAttributeFormat.Float32, 3, stream: 1),
                new VertexAttributeDescriptor(VertexAttribute.TexCoord0, VertexAttributeFormat.Float32, 2, stream: 2),
            };

            mesh.SetVertexBufferParams(vertCount, layout);
            
            // SetVertexBufferData expects NativeArray<T> where T is the vertex struct.
            // Since we use separate arrays, we have to upload them one by one or combine into one struct.
            // URP supports separate streams, but SetVertexBufferData with stream index is safer.
            mesh.SetVertexBufferData(data.Positions, 0, 0, vertCount, 0, MeshUpdateFlags.DontRecalculateBounds);
            mesh.SetVertexBufferData(data.Normals,   0, 0, vertCount, 1, MeshUpdateFlags.DontRecalculateBounds);
            mesh.SetVertexBufferData(data.Uvs,       0, 0, vertCount, 2, MeshUpdateFlags.DontRecalculateBounds);

            // SubmeshTriangles already holds visual faces only — collision-only faces were
            // routed into data.ColliderTriangles per node during the build.
            var sortedMatIDs = data.SubmeshTriangles.Keys.OrderBy(k => k).ToList();
            mesh.subMeshCount = sortedMatIDs.Count;

            int totalIndices = 0;
            for (int i = 0; i < sortedMatIDs.Count; i++)
                totalIndices += data.SubmeshTriangles[sortedMatIDs[i]].Length;
            mesh.SetIndexBufferParams(totalIndices, mesh.indexFormat);

            int baseIndex = 0;
            for (int si = 0; si < sortedMatIDs.Count; si++)
            {
                var tris = data.SubmeshTriangles[sortedMatIDs[si]];
                mesh.SetIndexBufferData(tris, 0, baseIndex, tris.Length, MeshUpdateFlags.DontRecalculateBounds);
                mesh.SetSubMesh(si, new SubMeshDescriptor(baseIndex, tris.Length), MeshUpdateFlags.DontRecalculateBounds);
                baseIndex += tris.Length;
            }
            submeshMaterialIds = sortedMatIDs.ToArray();

            if (data.BoneWeights.IsCreated)
                mesh.boneWeights = data.BoneWeights.ToArray(); // TODO: Use BoneWeight1 with SetBoneWeights for zero-alloc

            if (data.BindPoses != null)
                mesh.bindposes = data.BindPoses;

            mesh.RecalculateBounds();

            colliderMesh = BuildColliderMesh(data);
            return mesh;
        }

        // Builds a single-submesh collider mesh from the nodraw/proxy faces routed out of
        // the visual mesh. Shares the full vertex buffer (positions only); MeshCollider
        // cooking ignores unreferenced vertices. Returns null when there are no such faces.
        static Mesh BuildColliderMesh(MeshBuildData data)
        {
            if (data.ColliderTriangles.Count < 3)
                return null;

            int vertCount = data.Positions.Length;
            var positions = new Vector3[vertCount];
            for (int i = 0; i < vertCount; i++)
            {
                var p = data.Positions[i];
                positions[i] = new Vector3(p.x, p.y, p.z);
            }

            var colliderMesh = new Mesh
            {
                name = MeshCacheVersionName + "_Collider",
                indexFormat = IndexFormat.UInt32,
            };
            colliderMesh.SetVertices(positions);
            colliderMesh.SetTriangles(data.ColliderTriangles, 0, calculateBounds: true);
            return colliderMesh;
        }

        static Matrix4x4 BuildAccumulatedNodeTransform(CgfFile cgf, CgfNodeChunk node)
        {
            // CryStaticModel.cpp applies the mesh node first, then walks parents:
            // matNodeMatrix = matNodeMatrix * parent.tm; vertices use TransformPointOLD.
            var accumulated = node.Transform;
            var current = node;
            var visited = new HashSet<int> { node.ChunkID };

            while (current.ParentID >= 0 &&
                   cgf.NodeByChunkID != null &&
                   cgf.NodeByChunkID.TryGetValue(current.ParentID, out var parent) &&
                   visited.Add(parent.ChunkID))
            {
                accumulated = accumulated * parent.Transform;
                current = parent;
            }

            return accumulated;
        }

        // ------------------------------------------------------------------ bone weights

        static Matrix4x4[] BuildBindPoseGlobalMatricesByBoneId(CgfBoneInitPosChunk initPos, int boneCount, float scaleFactor)
        {
            int matrixCount = initPos?.BindMatrices?.Length ?? 0;
            int totalBones = Math.Max(boneCount, matrixCount);
            var globals = new Matrix4x4[totalBones];

            for (int boneId = 0; boneId < totalBones; boneId++)
            {
                var defaultGlobal = (initPos != null && boneId >= 0 && boneId < initPos.BindMatrices.Length)
                    ? initPos.BindMatrices[boneId]
                    : Matrix4x4.identity;

                globals[boneId] = CryTransformConversion.RemoveScale(
                    CryTransformConversion.MatrixInImporterSpace(defaultGlobal, scaleFactor));
            }

            return globals;
        }

        // ------------------------------------------------------------------ bind poses

        static Matrix4x4[] BuildBindPoses(CgfBoneInitPosChunk initPos, int[] boneIndexToId, float scaleFactor)
        {
            int boneCount = boneIndexToId?.Length ?? 0;
            var poses = new Matrix4x4[boneCount];

            for (int boneIndex = 0; boneIndex < boneCount; boneIndex++)
            {
                int boneId = boneIndexToId[boneIndex];
                var defaultGlobal = (initPos != null && boneId >= 0 && boneId < initPos.BindMatrices.Length)
                    ? initPos.BindMatrices[boneId]
                    : Matrix4x4.identity;

                defaultGlobal = CryTransformConversion.MatrixInImporterSpace(defaultGlobal, scaleFactor);
                defaultGlobal = CryTransformConversion.RemoveScale(defaultGlobal);
                poses[boneIndex] = defaultGlobal.inverse;
            }
            return poses;
        }

        static string[] BuildOrderedBoneNames(string[] namesByBoneId, int[] boneIndexToId)
        {
            var ordered = new string[boneIndexToId.Length];
            for (int boneIndex = 0; boneIndex < ordered.Length; boneIndex++)
            {
                int boneId = boneIndexToId[boneIndex];
                ordered[boneIndex] = boneId >= 0 && boneId < namesByBoneId.Length
                    ? namesByBoneId[boneId]
                    : $"bone_{boneIndex}";
            }

            return ordered;
        }

        static void BuildBoneIndexMapsOrIdentity(
            CgfBoneAnimChunk boneAnim,
            int boneCount,
            out int[] boneIdToIndex,
            out int[] boneIndexToId)
        {
            if (TryBuildBoneIndexMaps(boneAnim, boneCount, out boneIdToIndex, out boneIndexToId))
                return;

            boneIdToIndex = new int[boneCount];
            boneIndexToId = new int[boneCount];
            for (int i = 0; i < boneCount; i++)
            {
                boneIdToIndex[i] = i;
                boneIndexToId[i] = i;
            }
        }

        public static bool TryBuildBoneIndexMaps(
            CgfBoneAnimChunk boneAnim,
            int boneCount,
            out int[] boneIdToIndex,
            out int[] boneIndexToId)
        {
            var entities = boneAnim?.Bones;
            
            // Determine required map size based on max BoneID
            int maxBoneId = -1;
            if (entities != null)
            {
                for (int i = 0; i < entities.Length; i++)
                    if (entities[i].BoneID > maxBoneId) maxBoneId = entities[i].BoneID;
            }
            int idMapSize = Math.Max(boneCount, maxBoneId + 1);

            var idToIndex = new int[idMapSize];
            var indexToId = new int[boneCount];
            for (int i = 0; i < idMapSize; i++) idToIndex[i] = -1;
            for (int i = 0; i < boneCount; i++) indexToId[i] = -1;

            boneIdToIndex = idToIndex;
            boneIndexToId = indexToId;

            if (entities == null || entities.Length == 0 || boneCount == 0)
            {
                FallbackToIdentity(boneCount, idMapSize, idToIndex, indexToId);
                return false;
            }

            int cursor = 0;
            int nextBoneIndex = 0;

            int Allocate(int count)
            {
                if (count < 0 || nextBoneIndex + count > boneCount)
                    return -1;

                int result = nextBoneIndex;
                nextBoneIndex += count;
                return result;
            }

            bool LoadSubtree(int boneIndex)
            {
                if (cursor < 0 || cursor >= entities.Length || boneIndex < 0 || boneIndex >= boneCount)
                    return false;

                var entity = entities[cursor++];
                int boneId = entity.BoneID;
                if (boneId < 0 || boneId >= idMapSize)
                    return false;

                idToIndex[boneId] = boneIndex;
                indexToId[boneIndex] = boneId;

                int children = Mathf.Max(0, entity.ChildrenCount);
                if (children == 0)
                    return true;

                int childrenBase = Allocate(children);
                if (childrenBase < 0)
                    return false;

                for (int i = 0; i < children; i++)
                {
                    if (!LoadSubtree(childrenBase + i))
                        return false;
                }

                return true;
            }

            int rootIndex = Allocate(1);
            if (rootIndex == 0 && LoadSubtree(rootIndex) && cursor == entities.Length)
            {
                return true;
            }

            // Fallback if hierarchical map fails
            FallbackToIdentity(boneCount, idMapSize, idToIndex, indexToId);
            return false;
        }

        static void FallbackToIdentity(int boneCount, int idMapSize, int[] idToIndex, int[] indexToId)
        {
            for (int i = 0; i < idMapSize; i++) idToIndex[i] = -1;
            for (int i = 0; i < boneCount; i++) indexToId[i] = -1;

            for (int i = 0; i < boneCount; i++)
            {
                if (i < idMapSize)
                    idToIndex[i] = i;
                indexToId[i] = i;
            }
        }
    }
}
