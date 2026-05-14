using System.Collections.Generic;
using OpenFarCry.Importer.Cgf;
using UnityEngine;
using UnityEngine.Rendering;

namespace OpenFarCry.Level.Services
{
    public static class FcBrushGeometryPostProcessor
    {
        public const int CacheFormatVersion = 2;
        static readonly int PropCull = Shader.PropertyToID("_Cull");

        public readonly struct Artifacts
        {
            public readonly Mesh PhysicsColliderMesh;
            public readonly Mesh VisualFilteredMesh;

            public Artifacts(Mesh physicsColliderMesh, Mesh visualFilteredMesh)
            {
                PhysicsColliderMesh = physicsColliderMesh;
                VisualFilteredMesh = visualFilteredMesh;
            }
        }

        public static Artifacts ApplyRuntimeParity(
            GameObject visualRoot,
            BuildResult buildResult,
            CgfFile parsedFile,
            bool addPhysicsCollider,
            float importScale)
        {
            if (visualRoot == null || buildResult == null || parsedFile == null)
                return default;

            Mesh visualFilteredMesh = StripProxySubmeshesFromVisual(visualRoot, buildResult, parsedFile);
            Mesh physicsColliderMesh = null;

            if (addPhysicsCollider && buildResult.Mesh != null)
            {
                var col = visualRoot.GetComponent<MeshCollider>();
                if (col == null)
                    col = visualRoot.AddComponent<MeshCollider>();

                if (TryBuildPhysicsColliderMesh(parsedFile, importScale, out physicsColliderMesh))
                {
                    col.sharedMesh = physicsColliderMesh;
                }
                else
                {
                    var mf = visualRoot.GetComponent<MeshFilter>();
                    col.sharedMesh = mf != null ? mf.sharedMesh : buildResult.Mesh;
                }
            }

            DisableBackfaceCulling(visualRoot);
            StampCacheMetadata(visualRoot, brushRuntimeParity: true);
            return new Artifacts(physicsColliderMesh, visualFilteredMesh);
        }

        public static void DisableBackfaceCulling(GameObject root)
        {
            var renderers = root != null ? root.GetComponentsInChildren<MeshRenderer>(true) : null;
            if (renderers == null)
                return;

            for (int r = 0; r < renderers.Length; r++)
            {
                var materials = renderers[r].sharedMaterials;
                for (int i = 0; i < materials.Length; i++)
                {
                    var mat = materials[i];
                    if (mat != null && mat.HasProperty(PropCull))
                        mat.SetFloat(PropCull, (float)CullMode.Off);
                }
            }
        }

        public static void StampCacheMetadata(GameObject root, bool brushRuntimeParity)
        {
            if (root == null)
                return;

            var meta = root.GetComponent<FcCachedGeometryMetadata>();
            if (meta == null)
                meta = root.AddComponent<FcCachedGeometryMetadata>();

            meta.SetMetadata(CacheFormatVersion, brushRuntimeParity);
        }

        public static bool IsBrushRuntimeParityCompatible(GameObject prefabRoot)
        {
            if (prefabRoot == null)
                return false;

            var meta = prefabRoot.GetComponent<FcCachedGeometryMetadata>();
            if (meta == null)
                return false;

            return meta.CacheFormatVersion >= CacheFormatVersion && meta.BrushRuntimeParity;
        }

        static Mesh StripProxySubmeshesFromVisual(
            GameObject visualRoot,
            BuildResult buildResult,
            CgfFile parsedFile)
        {
            if (visualRoot == null || buildResult?.SubmeshMaterialIds == null)
                return null;

            if (!TryResolveRootMaterial(parsedFile, out var rootMat) || rootMat == null)
                return null;

            var proxyMatIds = BuildProxyMaterialIds(parsedFile, rootMat);
            if (proxyMatIds == null)
            {
                var noDrawMr = visualRoot.GetComponent<MeshRenderer>();
                if (noDrawMr != null)
                    noDrawMr.enabled = false;
                return null;
            }

            if (proxyMatIds.Count == 0)
                return null;

            var mf = visualRoot.GetComponent<MeshFilter>();
            var mr = visualRoot.GetComponent<MeshRenderer>();
            var sourceMesh = mf != null ? mf.sharedMesh : null;
            if (mf == null || mr == null || sourceMesh == null)
                return null;

            int subCount = sourceMesh.subMeshCount;
            if (subCount <= 0)
                return null;

            int[] matIds = buildResult.SubmeshMaterialIds;
            if (matIds.Length != subCount)
                return null;

            var keep = new List<int>(subCount);
            for (int i = 0; i < subCount; i++)
            {
                if (!proxyMatIds.Contains(matIds[i]))
                    keep.Add(i);
            }

            if (keep.Count == subCount)
                return null;

            if (keep.Count == 0)
            {
                mr.enabled = false;
                return null;
            }

            var filtered = Object.Instantiate(sourceMesh);
            filtered.name = sourceMesh.name + "_NoProxyVisual";
            filtered.subMeshCount = keep.Count;
            for (int i = 0; i < keep.Count; i++)
            {
                int src = keep[i];
                filtered.SetTriangles(sourceMesh.GetTriangles(src), i, true);
            }
            filtered.RecalculateBounds();

            mf.sharedMesh = filtered;

            var mats = mr.sharedMaterials;
            var newMats = new Material[keep.Count];
            for (int i = 0; i < keep.Count; i++)
            {
                int src = keep[i];
                newMats[i] = src >= 0 && src < mats.Length ? mats[src] : null;
            }
            mr.sharedMaterials = newMats;
            return filtered;
        }

        static bool TryBuildPhysicsColliderMesh(CgfFile parsedFile, float importScale, out Mesh mesh)
        {
            if (TryBuildFromNoDrawFaces(parsedFile, importScale, out mesh))
                return true;

            if (TryBuildFromBoneMesh(parsedFile, importScale, out mesh))
                return true;

            mesh = null;
            return false;
        }

        static bool TryBuildFromNoDrawFaces(CgfFile parsedFile, float importScale, out Mesh mesh)
        {
            mesh = null;
            var meshChunk = parsedFile?.MeshChunk;
            if (meshChunk?.Vertices == null || meshChunk.Faces == null || meshChunk.Faces.Length == 0)
                return false;

            if (!TryResolveRootMaterial(parsedFile, out var rootMat) || rootMat == null)
                return false;

            var proxyMatIds = BuildProxyMaterialIds(parsedFile, rootMat);
            if (proxyMatIds == null)
            {
                return BuildColliderMeshFromFaces(parsedFile, meshChunk, importScale, allowedMatIds: null, "BrushNoDrawCollider", out mesh);
            }

            if (proxyMatIds.Count == 0)
                return false;

            if (BuildColliderMeshFromFaces(parsedFile, meshChunk, importScale, proxyMatIds, "BrushNoDrawCollider", out mesh))
                return true;

            var shifted = new HashSet<int>();
            foreach (int id in proxyMatIds)
                shifted.Add(id + 1);
            return BuildColliderMeshFromFaces(parsedFile, meshChunk, importScale, shifted, "BrushNoDrawCollider", out mesh);
        }

        static bool TryBuildFromBoneMesh(CgfFile parsedFile, float importScale, out Mesh mesh)
        {
            mesh = null;
            var chunks = parsedFile?.BoneMeshChunks;
            if (chunks == null || chunks.Count == 0)
                return false;

            CgfMeshChunk phys = null;
            for (int i = 0; i < chunks.Count; i++)
            {
                var candidate = chunks[i]?.Mesh;
                if (candidate?.Vertices != null &&
                    candidate.Vertices.Length > 0 &&
                    candidate.Faces != null &&
                    candidate.Faces.Length > 0)
                {
                    phys = candidate;
                    break;
                }
            }

            if (phys == null)
                return false;

            return BuildColliderMeshFromFaces(null, phys, importScale, allowedMatIds: null, "BrushPhysicsCollider", out mesh);
        }

        static bool BuildColliderMeshFromFaces(
            CgfFile parsedFile,
            CgfMeshChunk source,
            float importScale,
            HashSet<int> allowedMatIds,
            string meshName,
            out Mesh mesh)
        {
            mesh = null;
            if (source?.Vertices == null || source.Faces == null || source.Faces.Length == 0)
                return false;

            var nodeTransform = Matrix4x4.identity;
            if (parsedFile != null && source == parsedFile.MeshChunk)
            {
                var rawNodeTransform = CgfMeshBuilder.BuildStaticNodeTransform(parsedFile, source.ChunkID);
                nodeTransform = CryTransformConversion.NodeMatrixInImporterSpace(rawNodeTransform, importScale);
            }

            var vertices = new List<Vector3>(source.Vertices.Length);
            for (int i = 0; i < source.Vertices.Length; i++)
            {
                var v = source.Vertices[i];
                var pos = CryTransformConversion.PositionInImporterSpace(
                    new Vector3(v.PX, v.PY, v.PZ),
                    importScale);
                vertices.Add(nodeTransform.MultiplyPoint3x4(pos));
            }

            var triangles = new List<int>(source.Faces.Length * 3);
            for (int i = 0; i < source.Faces.Length; i++)
            {
                var f = source.Faces[i];
                if (allowedMatIds != null && !allowedMatIds.Contains(f.MatID))
                    continue;
                if (f.V0 < 0 || f.V1 < 0 || f.V2 < 0 ||
                    f.V0 >= vertices.Count || f.V1 >= vertices.Count || f.V2 >= vertices.Count)
                    continue;
                triangles.Add(f.V0);
                triangles.Add(f.V1);
                triangles.Add(f.V2);
            }

            if (triangles.Count < 3)
                return false;

            mesh = new Mesh { name = meshName };
            if (vertices.Count > 65535)
                mesh.indexFormat = IndexFormat.UInt32;
            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0, true);
            mesh.RecalculateBounds();
            return true;
        }

        static bool TryResolveRootMaterial(CgfFile parsedFile, out CgfMaterialChunk rootMat)
        {
            rootMat = null;
            if (parsedFile == null)
                return false;

            CgfNodeChunk primaryNode = null;
            var nodes = parsedFile.NodeChunks;
            if (nodes != null)
            {
                for (int i = 0; i < nodes.Count; i++)
                {
                    var node = nodes[i];
                    if (node.ObjectID == parsedFile.SelectedMeshChunkID)
                    {
                        primaryNode = node;
                        break;
                    }
                }
            }

            int matChunkId = primaryNode?.MatID ?? -1;
            return matChunkId >= 0 && parsedFile.MaterialByChunkID.TryGetValue(matChunkId, out rootMat);
        }

        static HashSet<int> BuildProxyMaterialIds(CgfFile parsedFile, CgfMaterialChunk rootMat)
        {
            if (rootMat == null)
                return new HashSet<int>();

            if (rootMat.MtlType != CgfMtlType.Multi)
            {
                var singleMaterialIds = new HashSet<int>();
                if (IsNoDrawProxyMaterial(rootMat.Name))
                    singleMaterialIds.Add(rootMat.TableIndex);
                return singleMaterialIds;
            }

            var ids = new HashSet<int>();
            if (!parsedFile.MaterialChildrenByParentChunkID.TryGetValue(rootMat.ChunkID, out var children) || children == null)
                return ids;

            for (int i = 0; i < children.Count; i++)
            {
                if (IsNoDrawProxyMaterial(children[i]?.Name))
                    ids.Add(i);
            }

            return ids;
        }

        static bool IsNoDrawProxyMaterial(string materialName)
        {
            if (string.IsNullOrWhiteSpace(materialName))
                return false;

            string n = materialName.ToLowerInvariant();
            return n.Contains("nodraw") ||
                   n.Contains("no_draw") ||
                   n.Contains("physics_proxy") ||
                   n.Contains("phys_proxy") ||
                   n.Contains("$physics_proxy") ||
                   n.Contains("proxy");
        }
    }
}
