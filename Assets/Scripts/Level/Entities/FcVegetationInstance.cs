using System.Collections.Generic;
using System.IO;
using Cysharp.Threading.Tasks;
using OpenFarCry.Importer.Cgf;
using OpenFarCry.Level.Services;
using UnityEngine;

namespace OpenFarCry.Level.Entities
{
    [DisallowMultipleComponent]
    public sealed class FcVegetationInstance : MonoBehaviour
    {
        [SerializeField] string _virtualPath;
        [SerializeField] int    _typeIndex;
        [SerializeField] float  _instanceScale = 1f;
        [SerializeField] byte   _brightness    = 255;

        public string VirtualPath   => _virtualPath;
        public int    TypeIndex     => _typeIndex;
        public float  InstanceScale => _instanceScale;

        readonly CgfGameObjectBuilder _goBuilder = new CgfGameObjectBuilder();
        CgfRuntimeImportResult              _importResult;
        IReadOnlyList<CgfRuntimeImportResult> _lodResults;

        void Start()
        {
            if (string.IsNullOrEmpty(_virtualPath)) return;
            var svc = FcVegetationLoadService.Current;
            if (svc != null)
                svc.Register(this);
            else
                FallbackLoadAsync().Forget();
        }

        public void ApplyLoadResult(CgfRuntimeImportResult result,
            IReadOnlyList<CgfRuntimeImportResult> lodResults, string levelScopeId)
        {
            if (result == null || !result.Success) return;

            _importResult = result;
            _lodResults   = lodResults;

            string meshName = Path.GetFileNameWithoutExtension(_virtualPath);
            var output = _goBuilder.Build(new CgfGameObjectBuilder.BuildRequest(
                result: result.BuildResult,
                parsedFile: result.ParsedFile,
                rigDefinition: null,
                name: meshName,
                materialService: CgfRuntimeImporter.MaterialService,
                textureScopeId: levelScopeId));

            output.Root.transform.SetParent(transform, worldPositionStays: false);

            if (lodResults != null && lodResults.Count > 0 && output.MeshRenderer != null)
                ApplyLodGroup(output.Root, output.MeshRenderer, lodResults, levelScopeId);
        }

        void ApplyLodGroup(GameObject root, Renderer lod0Renderer,
            IReadOnlyList<CgfRuntimeImportResult> lodResults, string levelScopeId)
        {
            var renderers = new List<Renderer> { lod0Renderer };

            for (int i = 0; i < lodResults.Count; i++)
            {
                var r = lodResults[i];
                if (r?.BuildResult?.Mesh == null) continue;

                var lodGo = new GameObject($"LOD{i + 1}");
                lodGo.transform.SetParent(root.transform, worldPositionStays: false);

                lodGo.AddComponent<MeshFilter>().sharedMesh = r.BuildResult.Mesh;
                var mr = lodGo.AddComponent<MeshRenderer>();
                mr.sharedMaterials = CgfRuntimeImporter.MaterialService.ResolveSubmeshMaterials(
                    r.ParsedFile, r.Mesh, r.BuildResult.SubmeshMaterialIds, levelScopeId);
                renderers.Add(mr);
            }

            var lodGroup = root.GetComponent<LODGroup>();
            if (lodGroup == null) lodGroup = root.AddComponent<LODGroup>();
            lodGroup.animateCrossFading = false;
            lodGroup.SetLODs(BuildLods(renderers));
            lodGroup.RecalculateBounds();
        }

        static LOD[] BuildLods(List<Renderer> renderers)
        {
            int count = renderers.Count;
            const float maxH = 0.7f, minH = 0.02f;
            var lods = new LOD[count];
            if (count == 1)
            {
                lods[0] = new LOD(maxH, new[] { renderers[0] });
                return lods;
            }
            float step = (maxH - minH) / (count - 1);
            for (int i = 0; i < count; i++)
                lods[i] = new LOD(Mathf.Clamp(maxH - step * i, minH, maxH), new[] { renderers[i] });
            return lods;
        }

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
                scopeId, ct);

            if (ct.IsCancellationRequested) return;
            if (!result.Success) return;

            await CgfRuntimeImporter.MaterialService.PreloadTexturesAsync(
                result.ParsedFile, result.Mesh, result.BuildResult?.SubmeshMaterialIds, scopeId, ct);

            if (ct.IsCancellationRequested) return;

            var lodService = new CgfLodImportService();
            var lodPaths = lodService.FindSiblingLodPaths(_virtualPath);
            var lodResults = new List<CgfRuntimeImportResult>(lodPaths.Count);
            foreach (var lodPath in lodPaths)
            {
                if (ct.IsCancellationRequested) break;
                var lodResult = await CgfRuntimeImporter.ImportAsync(
                    new CgfRuntimeImportRequest(
                        virtualPath: lodPath,
                        importSkeleton: false,
                        importAnimations: false,
                        importScale: 0.01f,
                        useRuntimeMemoryCache: true),
                    scopeId, ct);
                if (!lodResult.Success) continue;
                await CgfRuntimeImporter.MaterialService.PreloadTexturesAsync(
                    lodResult.ParsedFile, lodResult.Mesh,
                    lodResult.BuildResult?.SubmeshMaterialIds, scopeId, ct);
                lodResults.Add(lodResult);
            }

            if (ct.IsCancellationRequested) return;
            await UniTask.SwitchToMainThread(ct);
            if (this == null || ct.IsCancellationRequested) return;

            ApplyLoadResult(result, lodResults, scopeId);
        }

        void OnDestroy()
        {
            var releaseSvc = FcLevelResourceService.Current;
            if (_importResult != null)
                releaseSvc?.ReleaseImportResult(_importResult);
            if (_lodResults != null)
                foreach (var r in _lodResults)
                    releaseSvc?.ReleaseImportResult(r);
        }
    }
}
