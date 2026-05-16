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

            public PreparedBuild(BuildResult result, MeshBuildData data)
            {
                Result = result;
                Data = data;
            }
        }

        internal sealed class MeshBuildData : IDisposable
        {
            public readonly List<Vector3> Positions = new List<Vector3>();
            public readonly List<Vector3> Normals = new List<Vector3>();
            public readonly List<Vector2> Uvs = new List<Vector2>();
            public readonly Dictionary<int, List<int>> SubmeshMap = new Dictionary<int, List<int>>();
            public BoneWeight[] BoneWeights;
            public Matrix4x4[] BindPoses;

            public bool HasNativeArrays;
            public NativeArray<float3> NativePositions;
            public NativeArray<float3> NativeNormals;
            public NativeArray<float2> NativeUvs;

            public void Dispose()
            {
                if (!HasNativeArrays) return;
                if (NativePositions.IsCreated) NativePositions.Dispose();
                if (NativeNormals.IsCreated)   NativeNormals.Dispose();
                if (NativeUvs.IsCreated)       NativeUvs.Dispose();
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

        public const string MeshCacheVersionName = "CGFMesh_NodeMatrixOld_v9";

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

            if (!selectedHasBones && TryBuildStaticMeshParts(cgf, out var staticParts))
            {
                var staticData = BuildStaticCombinedMeshData(staticParts, importScale);
                if (string.IsNullOrEmpty(result.SourceNodeName) && staticParts.Count > 0)
                    result.SourceNodeName = staticParts[0].NodeName;
                return new PreparedBuild(result, staticData);
            }

            var meshData = BuildMeshData(mesh, nodeTransform, cgf.BoneNames, cgf.BoneAnim, cgf.BoneInitPos, result, importSkeleton, importScale);
            return new PreparedBuild(result, meshData);
        }

        internal static BuildResult UploadPrepared(PreparedBuild prepared)
        {
            if (prepared == null)
                throw new ArgumentNullException(nameof(prepared));
            if (prepared.Result == null)
                throw new ArgumentException("Prepared build result is null.", nameof(prepared));
            if (prepared.Data == null)
                throw new ArgumentException("Prepared build data is null.", nameof(prepared));

            prepared.Result.Mesh = CreateUnityMesh(prepared.Data, out var submeshMaterialIds);
            prepared.Result.SubmeshMaterialIds = submeshMaterialIds;
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
                if (chunk == null || chunk.HasBoneInfo || chunk.Vertices == null || chunk.Faces == null)
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
            float importScale)
        {
            bool hasBones = importSkeleton && chunk.HasBoneInfo && boneNames != null && boneNames.Names.Length > 0;

            if (!hasBones)
                return BuildStaticMeshData(chunk, nodeTransform, importScale);

            int[] boneIdToIndex = null;
            int[] boneIndexToId = null;
            string[] orderedBoneNames = null;
            BuildBoneIndexMapsOrIdentity(boneAnim, boneNames.Names.Length, out boneIdToIndex, out boneIndexToId);
            orderedBoneNames = BuildOrderedBoneNames(boneNames.Names, boneIndexToId);

            var bindGlobalsByBoneId = BuildBindPoseGlobalMatricesByBoneId(boneInitPos, boneNames.Names.Length, importScale);

            var vertCache       = new Dictionary<(int pi, int ti), int>();
            var data = new MeshBuildData();
            var boneWeightsList = new List<CryLink[]>();

            var faces    = chunk.Faces;
            var texFaces = chunk.TexFaces;
            var verts    = chunk.Vertices;
            var rawUVs   = chunk.UVs;
            bool hasTexFaces = texFaces != null && texFaces.Length == faces.Length;
            bool hasUvs = rawUVs != null && rawUVs.Length > 0 && hasTexFaces;

            for (int fi = 0; fi < faces.Length; fi++)
            {
                int matID = faces[fi].MatID;
                if (!data.SubmeshMap.TryGetValue(matID, out var triList))
                {
                    triList = new List<int>();
                    data.SubmeshMap[matID] = triList;
                }

                int p0 = faces[fi].V0;
                int p1 = faces[fi].V1;
                int p2 = faces[fi].V2;
                int t0 = hasTexFaces ? texFaces[fi].T0 : 0;
                int t1 = hasTexFaces ? texFaces[fi].T1 : 0;
                int t2 = hasTexFaces ? texFaces[fi].T2 : 0;

                ProcessCorner(p0, t0, triList);
                ProcessCorner(p1, t1, triList);
                ProcessCorner(p2, t2, triList);

                void ProcessCorner(int pi, int ti, List<int> triangles)
                {
                    var key = (pi, ti);
                    if (!vertCache.TryGetValue(key, out int idx))
                    {
                        idx = data.Positions.Count;
                        var v = verts[pi];
                        var rawPos = CryTransformConversion.PositionInImporterSpace(new Vector3(v.PX, v.PY, v.PZ), importScale);
                        var links = chunk.BoneLinks?[pi];
                        var sortedLinks = SortLinksByDescendingWeight(links);
                        var pos = TryBuildBindPositionFromLinks(sortedLinks, bindGlobalsByBoneId, importScale, out var linkedBindPos)
                            ? linkedBindPos
                            : rawPos;
                        var nrm = CryTransformConversion.DirectionInImporterSpace(new Vector3(v.NX, v.NY, v.NZ));
                        data.Positions.Add(pos);
                        data.Normals.Add(nrm.normalized);
                        if (hasUvs && ti >= 0 && ti < rawUVs.Length)
                        {
                            var uv = rawUVs[ti];
                            data.Uvs.Add(new Vector2(uv.U, 1f - uv.V));
                        }
                        else
                        {
                            data.Uvs.Add(Vector2.zero);
                        }
                        boneWeightsList.Add(sortedLinks);
                        vertCache[key] = idx;
                    }
                    triangles.Add(idx);
                }
            }

            data.BoneWeights = BuildBoneWeights(boneWeightsList, boneNames.Names.Length, boneIdToIndex);
            result.HasSkeleton = true;
            result.BoneNames   = orderedBoneNames;
            result.BoneIdToIndex = boneIdToIndex;
            result.BoneIndexToId = boneIndexToId;
            result.BindPoses   = BuildBindPoses(boneInitPos, boneIndexToId, importScale);
            data.BindPoses = result.BindPoses;

            return data;
        }

        static MeshBuildData BuildStaticMeshData(CgfMeshChunk chunk, Matrix4x4 nodeTransform, float importScale)
        {
            var data = new MeshBuildData();
            var unityNodeTransform = CryTransformConversion.NodeMatrixInImporterSpace(nodeTransform, importScale);

            bool hasUvs = chunk.UVs != null && chunk.UVs.Length > 0 &&
                          chunk.TexFaces != null && chunk.TexFaces.Length == chunk.Faces.Length;
            var remap = BuildVertexRemapping(chunk.Faces, chunk.TexFaces, chunk.Vertices.Length,
                hasUvs ? chunk.UVs.Length : 0);
            int n = remap.UniquePosIdx.Length;

            data.NativePositions = new NativeArray<float3>(n, Allocator.Persistent);
            data.NativeNormals   = new NativeArray<float3>(n, Allocator.Persistent);
            data.NativeUvs       = new NativeArray<float2>(n, Allocator.Persistent);
            data.HasNativeArrays = true;

            RunStaticVertexTransformJob(remap, chunk.Vertices, hasUvs ? chunk.UVs : null,
                unityNodeTransform, importScale,
                data.NativePositions, data.NativeNormals, data.NativeUvs);

            MergeSubmeshMap(data.SubmeshMap, remap.SubmeshMap, 0);
            return data;
        }

        static MeshBuildData BuildStaticCombinedMeshData(List<StaticMeshPart> parts, float importScale)
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
                bool hasUvs = chunk.UVs != null && chunk.UVs.Length > 0 &&
                              chunk.TexFaces != null && chunk.TexFaces.Length == chunk.Faces.Length;
                partHasUvs[i] = hasUvs;
                remaps[i] = BuildVertexRemapping(chunk.Faces, chunk.TexFaces, chunk.Vertices.Length,
                    hasUvs ? chunk.UVs.Length : 0);
                totalVerts += remaps[i].UniquePosIdx.Length;
            }

            data.NativePositions = new NativeArray<float3>(totalVerts, Allocator.Persistent);
            data.NativeNormals   = new NativeArray<float3>(totalVerts, Allocator.Persistent);
            data.NativeUvs       = new NativeArray<float2>(totalVerts, Allocator.Persistent);
            data.HasNativeArrays = true;

            // Pass 2: per-part Burst jobs into output slices; merge submesh maps with offset
            int vertexOffset = 0;
            for (int i = 0; i < parts.Count; i++)
            {
                var chunk = parts[i].Chunk;
                var remap = remaps[i];
                int partVerts = remap.UniquePosIdx.Length;

                RunStaticVertexTransformJob(remap, chunk.Vertices, partHasUvs[i] ? chunk.UVs : null,
                    unityTransforms[i], importScale,
                    data.NativePositions.GetSubArray(vertexOffset, partVerts),
                    data.NativeNormals.GetSubArray(vertexOffset, partVerts),
                    data.NativeUvs.GetSubArray(vertexOffset, partVerts));

                MergeSubmeshMap(data.SubmeshMap, remap.SubmeshMap, vertexOffset);
                vertexOffset += partVerts;
            }

            return data;
        }

        internal static VertexRemappingResult BuildVertexRemapping(
            CryFace[] faces, CryTexFace[] texFaces, int vertCount, int uvCount)
        {
            var submeshMap   = new Dictionary<int, List<int>>();
            var uniquePosIdx = new List<int>();
            var uniqueUvIdx  = new List<int>();
            var vertCache    = new Dictionary<(int pi, int ti), int>();

            bool hasTexFaces = texFaces != null && texFaces.Length == faces.Length;

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

        static void MergeSubmeshMap(Dictionary<int, List<int>> target, Dictionary<int, List<int>> source, int vertexOffset)
        {
            foreach (var kv in source)
            {
                if (!target.TryGetValue(kv.Key, out var targetList))
                {
                    targetList = new List<int>(kv.Value.Count);
                    target[kv.Key] = targetList;
                }
                foreach (int idx in kv.Value)
                    targetList.Add(idx + vertexOffset);
            }
        }

        static unsafe void RunStaticVertexTransformJob(
            VertexRemappingResult remap,
            CryVertex[] verts, CryUV[] rawUVs,
            Matrix4x4 unityNodeTransform, float importScale,
            NativeArray<float3> outPositions,
            NativeArray<float3> outNormals,
            NativeArray<float2> outUvs)
        {
            int uvCount = rawUVs?.Length ?? 0;

            var nativePosIdx = new NativeArray<int>(remap.UniquePosIdx, Allocator.TempJob);
            var nativeUvIdx  = new NativeArray<int>(remap.UniqueUvIdx,  Allocator.TempJob);

            var nativeVerts = new NativeArray<CryVertexBlittable>(verts.Length, Allocator.TempJob,
                NativeArrayOptions.UninitializedMemory);
            fixed (CryVertex* src = verts)
                UnsafeUtility.MemCpy(nativeVerts.GetUnsafePtr(), src,
                    verts.Length * UnsafeUtility.SizeOf<CryVertexBlittable>());

            NativeArray<CryUvBlittable> nativeUvs;
            if (uvCount > 0)
            {
                nativeUvs = new NativeArray<CryUvBlittable>(uvCount, Allocator.TempJob,
                    NativeArrayOptions.UninitializedMemory);
                fixed (CryUV* src = rawUVs)
                    UnsafeUtility.MemCpy(nativeUvs.GetUnsafePtr(), src,
                        uvCount * UnsafeUtility.SizeOf<CryUvBlittable>());
            }
            else
            {
                nativeUvs = new NativeArray<CryUvBlittable>(0, Allocator.TempJob);
            }

            new StaticVertexTransformJob
            {
                UniquePosIdx = nativePosIdx,
                UniqueUvIdx  = nativeUvIdx,
                RawVertices  = nativeVerts,
                RawUVs       = nativeUvs,
                NodeMatrix   = (float4x4)unityNodeTransform,
                ImportScale  = importScale,
                UvCount      = uvCount,
                OutPositions = outPositions,
                OutNormals   = outNormals,
                OutUvs       = outUvs,
            }.Schedule(remap.UniquePosIdx.Length, 64).Complete();

            nativePosIdx.Dispose();
            nativeUvIdx.Dispose();
            nativeVerts.Dispose();
            nativeUvs.Dispose();
        }

        static Mesh CreateUnityMesh(MeshBuildData data, out int[] submeshMaterialIds)
        {
            var mesh = new Mesh { name = MeshCacheVersionName };

            int vertCount = data.HasNativeArrays ? data.NativePositions.Length : data.Positions.Count;
            if (vertCount > 65535)
                mesh.indexFormat = IndexFormat.UInt32;

            if (data.HasNativeArrays)
            {
                mesh.SetVertices(data.NativePositions.Reinterpret<Vector3>(UnsafeUtility.SizeOf<float3>()));
                mesh.SetNormals(data.NativeNormals.Reinterpret<Vector3>(UnsafeUtility.SizeOf<float3>()));
                mesh.SetUVs(0, data.NativeUvs.Reinterpret<Vector2>(UnsafeUtility.SizeOf<float2>()));
            }
            else
            {
                mesh.SetVertices(data.Positions);
                mesh.SetNormals(data.Normals);
                mesh.SetUVs(0, data.Uvs);
            }

            var sortedMatIDs = data.SubmeshMap.Keys.OrderBy(k => k).ToList();
            mesh.subMeshCount = sortedMatIDs.Count;
            for (int si = 0; si < sortedMatIDs.Count; si++)
                mesh.SetTriangles(data.SubmeshMap[sortedMatIDs[si]], si);
            submeshMaterialIds = sortedMatIDs.ToArray();

            if (data.BoneWeights != null)
                mesh.boneWeights = data.BoneWeights;
            if (data.BindPoses != null)
                mesh.bindposes = data.BindPoses;

            mesh.RecalculateBounds();
            return mesh;
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
            var globals = new Matrix4x4[boneCount];
            for (int boneId = 0; boneId < boneCount; boneId++)
            {
                var defaultGlobal = initPos != null && boneId >= 0 && boneId < initPos.BindMatrices.Length
                    ? initPos.BindMatrices[boneId]
                    : Matrix4x4.identity;

                globals[boneId] = CryTransformConversion.RemoveScale(
                    CryTransformConversion.MatrixInImporterSpace(defaultGlobal, scaleFactor));
            }

            return globals;
        }

        static bool TryBuildBindPositionFromLinks(
            CryLink[] links,
            Matrix4x4[] bindGlobalsByBoneId,
            float importScale,
            out Vector3 position)
        {
            position = Vector3.zero;
            if (links == null || links.Length == 0 || bindGlobalsByBoneId == null || bindGlobalsByBoneId.Length == 0)
                return false;

            // CryEngine skins from per-link bone-local offsets:
            //   vertex = sum(boneGlobal.TransformPointOLD(link.offset) * link.Blending)
            // Unity stores one bind vertex plus up to four BoneWeight entries, so build
            // the imported vertex from the same top-four normalized influences we assign.
            int used = Mathf.Min(4, links.Length);

            float topTotal = 0f;
            for (int i = 0; i < used; i++)
            {
                int boneId = links[i].BoneID;
                if (boneId >= 0 && boneId < bindGlobalsByBoneId.Length)
                    topTotal += links[i].Blending;
            }

            if (topTotal < float.Epsilon)
                return false;

            float norm = 1f / topTotal;
            for (int i = 0; i < used; i++)
            {
                var link = links[i];
                int boneId = link.BoneID;
                if (boneId < 0 || boneId >= bindGlobalsByBoneId.Length)
                    continue;

                var offset = CryTransformConversion.PositionInImporterSpace(new Vector3(link.OX, link.OY, link.OZ), importScale);
                position += bindGlobalsByBoneId[boneId].MultiplyPoint3x4(offset) * (link.Blending * norm);
            }

            return true;
        }

        static BoneWeight[] BuildBoneWeights(List<CryLink[]> weightsList, int boneCount, int[] boneIdToIndex)
        {
            var boneWeights = new BoneWeight[weightsList.Count];
            for (int vi = 0; vi < weightsList.Count; vi++)
            {
                var links = weightsList[vi];
                if (links == null || links.Length == 0)
                {
                    boneWeights[vi] = new BoneWeight { boneIndex0 = 0, weight0 = 1f };
                    continue;
                }

                int used = Mathf.Min(4, links.Length);

                float topTotal = 0f;
                for (int i = 0; i < used; i++)
                    topTotal += links[i].Blending;
                if (topTotal < float.Epsilon) topTotal = 1f;
                float norm = 1f / topTotal;

                var bw = new BoneWeight();

                if (used > 0) { bw.boneIndex0 = RemapBone(links[0].BoneID, boneCount, boneIdToIndex); bw.weight0 = links[0].Blending * norm; }
                if (used > 1) { bw.boneIndex1 = RemapBone(links[1].BoneID, boneCount, boneIdToIndex); bw.weight1 = links[1].Blending * norm; }
                if (used > 2) { bw.boneIndex2 = RemapBone(links[2].BoneID, boneCount, boneIdToIndex); bw.weight2 = links[2].Blending * norm; }
                if (used > 3) { bw.boneIndex3 = RemapBone(links[3].BoneID, boneCount, boneIdToIndex); bw.weight3 = links[3].Blending * norm; }

                boneWeights[vi] = bw;
            }
            return boneWeights;
        }

        static CryLink[] SortLinksByDescendingWeight(CryLink[] links)
        {
            if (links == null || links.Length <= 1)
                return links;

            var sorted = new CryLink[links.Length];
            Array.Copy(links, sorted, links.Length);
            Array.Sort(sorted, CompareLinkWeightDescending);
            return sorted;
        }

        static int CompareLinkWeightDescending(CryLink x, CryLink y)
        {
            return y.Blending.CompareTo(x.Blending);
        }

        static int RemapBone(int boneId, int count, int[] boneIdToIndex)
        {
            if (boneIdToIndex != null && boneId >= 0 && boneId < boneIdToIndex.Length && boneIdToIndex[boneId] >= 0)
                return boneIdToIndex[boneId];

            return Mathf.Clamp(boneId, 0, count - 1);
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
            var idToIndex = new int[boneCount];
            var indexToId = new int[boneCount];
            for (int i = 0; i < boneCount; i++)
            {
                idToIndex[i] = -1;
                indexToId[i] = -1;
            }

            boneIdToIndex = idToIndex;
            boneIndexToId = indexToId;

            var entities = boneAnim?.Bones;
            if (entities == null || entities.Length != boneCount || boneCount == 0)
                return false;

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
                if (boneId < 0 || boneId >= boneCount)
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
            return rootIndex == 0 && LoadSubtree(rootIndex) && cursor == entities.Length;
        }
    }
}
