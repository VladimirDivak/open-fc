using System;
using System.Collections.Generic;
using System.Linq;
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
        public const string MeshCacheVersionName = "CGFMesh_NodeMatrixOld_v8";

        public static BuildResult Build(CgfFile cgf, bool importSkeleton = true, float importScale = 1f)
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
                result.Mesh = BuildStaticCombinedMesh(staticParts, result, importScale);
                if (string.IsNullOrEmpty(result.SourceNodeName) && staticParts.Count > 0)
                    result.SourceNodeName = staticParts[0].NodeName;
                return result;
            }

            var unityMesh = BuildMesh(mesh, nodeTransform, cgf.BoneNames, cgf.BoneAnim, cgf.BoneInitPos, result, importSkeleton, importScale);
            result.Mesh   = unityMesh;
            return result;
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

        static Mesh BuildMesh(
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
            int[] boneIdToIndex = null;
            int[] boneIndexToId = null;
            string[] orderedBoneNames = null;
            if (hasBones)
            {
                BuildBoneIndexMapsOrIdentity(boneAnim, boneNames.Names.Length, out boneIdToIndex, out boneIndexToId);
                orderedBoneNames = BuildOrderedBoneNames(boneNames.Names, boneIndexToId);
            }

            // Cry applies the object Node transform only to geometry that is not
            // driven by bone links. Skinned geometry is already in skeleton space.
            // Static Cry geometry is baked by NODE_CHUNK_DESC.tm using OLD Matrix44
            // semantics: row-vector 3x3 plus translation in row 3.
            var unityNodeTransform = hasBones
                ? Matrix4x4.identity
                : CryTransformConversion.NodeMatrixInImporterSpace(nodeTransform, importScale);

            var bindGlobalsByBoneId = hasBones
                ? BuildBindPoseGlobalMatricesByBoneId(boneInitPos, boneNames.Names.Length, importScale)
                : null;

            // --- UV remapping ---
            var vertCache       = new Dictionary<(int pi, int ti), int>();
            var positions       = new List<Vector3>();
            var normals         = new List<Vector3>();
            var uvs             = new List<Vector2>();
            var boneWeightsList = hasBones ? new List<CryLink[]>() : null;

            // Group triangle indices by MatID for submeshes
            var submeshMap = new Dictionary<int, List<int>>();

            var faces    = chunk.Faces;
            var texFaces = chunk.TexFaces;
            var verts    = chunk.Vertices;
            var rawUVs   = chunk.UVs;
            bool hasTexFaces = texFaces != null && texFaces.Length == faces.Length;
            bool hasUvs = rawUVs != null && rawUVs.Length > 0 && hasTexFaces;

            for (int fi = 0; fi < faces.Length; fi++)
            {
                int matID = faces[fi].MatID;
                if (!submeshMap.TryGetValue(matID, out var triList))
                {
                    triList = new List<int>();
                    submeshMap[matID] = triList;
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
                        idx = positions.Count;
                        var v = verts[pi];
                        var rawPos = CryTransformConversion.PositionInImporterSpace(new Vector3(v.PX, v.PY, v.PZ), importScale);
                        var links = chunk.BoneLinks?[pi];
                        var sortedLinks = SortLinksByDescendingWeight(links);
                        var pos = hasBones && TryBuildBindPositionFromLinks(sortedLinks, bindGlobalsByBoneId, importScale, out var linkedBindPos)
                            ? linkedBindPos
                            : rawPos;
                        var nrm = CryTransformConversion.DirectionInImporterSpace(new Vector3(v.NX, v.NY, v.NZ));
                        positions.Add(unityNodeTransform.MultiplyPoint3x4(pos));
                        normals.Add(unityNodeTransform.MultiplyVector(nrm).normalized);
                        if (hasUvs && ti >= 0 && ti < rawUVs.Length)
                        {
                            var uv = rawUVs[ti];
                            uvs.Add(new Vector2(uv.U, 1f - uv.V));
                        }
                        else
                        {
                            uvs.Add(Vector2.zero);
                        }
                        boneWeightsList?.Add(sortedLinks);
                        vertCache[key] = idx;
                    }
                    triangles.Add(idx);
                }
            }

            // --- Build Unity Mesh ---
            var mesh = new Mesh { name = MeshCacheVersionName };

            if (positions.Count > 65535)
                mesh.indexFormat = IndexFormat.UInt32;

            mesh.SetVertices(positions);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);

            var sortedMatIDs = submeshMap.Keys.OrderBy(k => k).ToList();
            mesh.subMeshCount = sortedMatIDs.Count;
            for (int si = 0; si < sortedMatIDs.Count; si++)
                mesh.SetTriangles(submeshMap[sortedMatIDs[si]], si);
            result.SubmeshMaterialIds = sortedMatIDs.ToArray();

            // --- Skeleton ---
            if (hasBones)
            {
                mesh.boneWeights = BuildBoneWeights(boneWeightsList, boneNames.Names.Length, boneIdToIndex);
                result.HasSkeleton = true;
                result.BoneNames   = orderedBoneNames;
                result.BoneIdToIndex = boneIdToIndex;
                result.BoneIndexToId = boneIndexToId;
                result.BindPoses   = BuildBindPoses(boneInitPos, boneIndexToId, importScale);
                mesh.bindposes     = result.BindPoses;
            }

            mesh.RecalculateBounds();
            return mesh;
        }

        static Mesh BuildStaticCombinedMesh(List<StaticMeshPart> parts, BuildResult result, float importScale)
        {
            var positions = new List<Vector3>();
            var normals = new List<Vector3>();
            var uvs = new List<Vector2>();
            var submeshMap = new Dictionary<int, List<int>>();

            for (int partIndex = 0; partIndex < parts.Count; partIndex++)
            {
                var part = parts[partIndex];
                var chunk = part.Chunk;
                var unityNodeTransform = CryTransformConversion.NodeMatrixInImporterSpace(part.NodeTransform, importScale);
                var vertCache = new Dictionary<(int pi, int ti), int>();

                var faces = chunk.Faces;
                var texFaces = chunk.TexFaces;
                var verts = chunk.Vertices;
                var rawUVs = chunk.UVs;
                bool hasTexFaces = texFaces != null && texFaces.Length == faces.Length;
                bool hasUvs = rawUVs != null && rawUVs.Length > 0 && hasTexFaces;

                for (int fi = 0; fi < faces.Length; fi++)
                {
                    var face = faces[fi];
                    if (!submeshMap.TryGetValue(face.MatID, out var triList))
                    {
                        triList = new List<int>();
                        submeshMap[face.MatID] = triList;
                    }

                    int t0 = hasTexFaces ? texFaces[fi].T0 : 0;
                    int t1 = hasTexFaces ? texFaces[fi].T1 : 0;
                    int t2 = hasTexFaces ? texFaces[fi].T2 : 0;

                    ProcessCorner(face.V0, t0, triList);
                    ProcessCorner(face.V1, t1, triList);
                    ProcessCorner(face.V2, t2, triList);
                }

                void ProcessCorner(int pi, int ti, List<int> triangles)
                {
                    if (pi < 0 || pi >= verts.Length)
                        return;

                    var key = (pi, ti);
                    if (!vertCache.TryGetValue(key, out int idx))
                    {
                        idx = positions.Count;
                        var v = verts[pi];
                        var rawPos = CryTransformConversion.PositionInImporterSpace(new Vector3(v.PX, v.PY, v.PZ), importScale);
                        var rawNrm = CryTransformConversion.DirectionInImporterSpace(new Vector3(v.NX, v.NY, v.NZ));
                        positions.Add(unityNodeTransform.MultiplyPoint3x4(rawPos));
                        normals.Add(unityNodeTransform.MultiplyVector(rawNrm).normalized);

                        if (hasUvs && ti >= 0 && ti < rawUVs.Length)
                        {
                            var uv = rawUVs[ti];
                            uvs.Add(new Vector2(uv.U, 1f - uv.V));
                        }
                        else
                        {
                            uvs.Add(Vector2.zero);
                        }

                        vertCache[key] = idx;
                    }

                    triangles.Add(idx);
                }
            }

            var mesh = new Mesh { name = MeshCacheVersionName };
            if (positions.Count > 65535)
                mesh.indexFormat = IndexFormat.UInt32;

            mesh.SetVertices(positions);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);

            var sortedMatIDs = submeshMap.Keys.OrderBy(k => k).ToList();
            mesh.subMeshCount = sortedMatIDs.Count;
            for (int si = 0; si < sortedMatIDs.Count; si++)
                mesh.SetTriangles(submeshMap[sortedMatIDs[si]], si);
            result.SubmeshMaterialIds = sortedMatIDs.ToArray();

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
