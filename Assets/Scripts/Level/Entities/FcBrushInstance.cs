using System.IO;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using OpenFarCry.Importer.Cgf;
using OpenFarCry.Level.Services;
using UnityEngine;
using UnityEngine.Rendering;

namespace OpenFarCry.Level.Entities
{
    // Static world brush. Position/rotation/scale are baked by FcLevelSceneBuilder.
    // CGF mesh is loaded from PAK at Play Mode start via FcLevelResourceService.
    [DisallowMultipleComponent]
    public sealed class FcBrushInstance : MonoBehaviour
    {
        [SerializeField] string _virtualPath;
        [SerializeField] bool _noPhysics;

        public string VirtualPath => _virtualPath;
        public bool   NoPhysics   => _noPhysics;

        readonly CgfGameObjectBuilder _goBuilder = new CgfGameObjectBuilder();
        readonly CgfLodImportService _lodService = new CgfLodImportService();
        static readonly int PropCull = Shader.PropertyToID("_Cull");
        CgfRuntimeImportResult _importResult;
        Mesh _physicsColliderMesh;
        Mesh _visualFilteredMesh;

        void Start()
        {
            if (string.IsNullOrEmpty(_virtualPath)) return;
            var svc = FcBrushLoadService.Current;
            if (svc != null)
                svc.Register(this);
            else
                FallbackLoadAsync().Forget();
        }

        // Called by FcBrushLoadService after async import + texture preload complete.
        public void ApplyLoadResult(CgfRuntimeImportResult result, string levelScopeId)
        {
            if (result == null || !result.Success) return;

            _importResult = result;

            string meshName = Path.GetFileNameWithoutExtension(_virtualPath);
            var output = _goBuilder.Build(new CgfGameObjectBuilder.BuildRequest(
                result: result.BuildResult,
                parsedFile: result.ParsedFile,
                rigDefinition: null,
                name: meshName,
                materialService: CgfRuntimeImporter.MaterialService,
                textureScopeId: levelScopeId));

            StripProxySubmeshesFromVisual(output.Root, result);
            DisableBrushBackfaceCulling(output.Root);
            output.Root.transform.SetParent(transform, worldPositionStays: false);

            if (!_noPhysics && result.Mesh != null)
            {
                var col = output.Root.AddComponent<MeshCollider>();
                if (TryBuildPhysicsColliderMesh(result.ParsedFile, 0.01f, out var physicsMesh))
                {
                    _physicsColliderMesh = physicsMesh;
                    col.sharedMesh = physicsMesh;
                }
                else
                {
                    var mf = output.Root.GetComponent<MeshFilter>();
                    col.sharedMesh = mf?.sharedMesh ?? result.Mesh;
                }
            }

            var lods = _lodService.FindSiblingLodPaths(result.VirtualPath);
            if (lods.Count > 0)
            {
                _lodService.ConfigureLodGroup(output.Root, hasSkeleton: false, importScale: 0.01f,
                    siblingLodPaths: lods,
                    materialService: CgfRuntimeImporter.MaterialService,
                    textureScopeId: levelScopeId);
                DisableBrushBackfaceCulling(output.Root);
            }
        }

        static void DisableBrushBackfaceCulling(GameObject root)
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

        // Fallback when FcBrushLoadService is absent (e.g. editor without full scene).
        async UniTaskVoid FallbackLoadAsync()
        {
            var ct = this.GetCancellationTokenOnDestroy();
            var resourceSvc = FcLevelResourceService.Current;
            string scopeId = resourceSvc != null ? resourceSvc.LevelScopeId : string.Empty;

            var result = await CgfRuntimeImporter.ImportAsync(
                new CgfRuntimeImportRequest(
                    virtualPath: _virtualPath,
                    importSkeleton: false,
                    importAnimations: false,
                    importScale: 0.01f,
                    useRuntimeMemoryCache: true),
                scopeId,
                ct);

            if (ct.IsCancellationRequested) return;

            if (!result.Success)
            {
                Debug.LogWarning($"[FcBrush] '{_virtualPath}': {result.ErrorMessage}", this);
                return;
            }

            await CgfRuntimeImporter.MaterialService.PreloadTexturesAsync(
                result.ParsedFile,
                result.Mesh,
                result.BuildResult?.SubmeshMaterialIds,
                scopeId,
                ct);

            if (ct.IsCancellationRequested) return;
            await UniTask.SwitchToMainThread(ct);

            if (this == null || ct.IsCancellationRequested) return;
            ApplyLoadResult(result, scopeId);
        }

        void OnDestroy()
        {
            if (_importResult != null)
                FcLevelResourceService.Current?.ReleaseImportResult(_importResult);

            if (_physicsColliderMesh != null)
            {
                Destroy(_physicsColliderMesh);
                _physicsColliderMesh = null;
            }

            if (_visualFilteredMesh != null)
            {
                Destroy(_visualFilteredMesh);
                _visualFilteredMesh = null;
            }
        }

        void StripProxySubmeshesFromVisual(GameObject visualRoot, CgfRuntimeImportResult result)
        {
            if (visualRoot == null || result?.BuildResult?.SubmeshMaterialIds == null)
                return;

            if (!TryResolveRootMaterial(result.ParsedFile, out var rootMat) || rootMat == null)
                return;

            var proxyMatIds = BuildProxyMaterialIds(result.ParsedFile, rootMat);
            if (proxyMatIds == null)
            {
                // Entire mesh is NoDraw/proxy: suppress rendering.
                var noDrawMr = visualRoot.GetComponent<MeshRenderer>();
                if (noDrawMr != null) noDrawMr.enabled = false;
                return;
            }
            if (proxyMatIds.Count == 0)
                return;

            var mf = visualRoot.GetComponent<MeshFilter>();
            var mr = visualRoot.GetComponent<MeshRenderer>();
            var sourceMesh = mf != null ? mf.sharedMesh : null;
            if (mf == null || mr == null || sourceMesh == null)
                return;

            int subCount = sourceMesh.subMeshCount;
            if (subCount <= 0)
                return;

            int[] matIds = result.BuildResult.SubmeshMaterialIds;
            if (matIds.Length != subCount)
                return;

            var keep = new List<int>(subCount);
            for (int i = 0; i < subCount; i++)
            {
                if (!proxyMatIds.Contains(matIds[i]))
                    keep.Add(i);
            }

            if (keep.Count == subCount)
                return;

            if (keep.Count == 0)
            {
                mr.enabled = false;
                return;
            }

            var filtered = Object.Instantiate(sourceMesh);
            filtered.name = $"{sourceMesh.name}_NoProxyVisual";
            filtered.subMeshCount = keep.Count;
            for (int i = 0; i < keep.Count; i++)
            {
                int src = keep[i];
                filtered.SetTriangles(sourceMesh.GetTriangles(src), i, true);
            }
            filtered.RecalculateBounds();

            var oldFiltered = _visualFilteredMesh;
            _visualFilteredMesh = filtered;
            if (oldFiltered != null)
                Destroy(oldFiltered);

            mf.sharedMesh = filtered;

            var mats = mr.sharedMaterials;
            var newMats = new Material[keep.Count];
            for (int i = 0; i < keep.Count; i++)
            {
                int src = keep[i];
                newMats[i] = src >= 0 && src < mats.Length ? mats[src] : null;
            }
            mr.sharedMaterials = newMats;
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
                // Single proxy-only material: use all faces.
                return BuildColliderMeshFromFaces(parsedFile, meshChunk, importScale, allowedMatIds: null, "BrushNoDrawCollider", out mesh);
            }

            if (proxyMatIds.Count == 0)
                return false;

            if (BuildColliderMeshFromFaces(parsedFile, meshChunk, importScale, proxyMatIds, "BrushNoDrawCollider", out mesh))
                return true;

            // Some exporters write face MatID as 1-based sub-material index.
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
