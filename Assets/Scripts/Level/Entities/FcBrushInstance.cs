using System.Collections.Generic;
using System.IO;
using Cysharp.Threading.Tasks;
using OpenFarCry.Importer.Cgf;
using OpenFarCry.Level.Services;
using UnityEngine;

namespace OpenFarCry.Level.Entities
{
    // Static world brush. Position/rotation/scale are baked by FcLevelSceneBuilder.
    // CGF mesh is loaded from PAK at Play Mode start via FcLevelResourceService.
    [DisallowMultipleComponent]
    public sealed class FcBrushInstance : MonoBehaviour
    {
        [SerializeField] string _virtualPath;
        [SerializeField] bool _noPhysics;
        [SerializeField] string _materialOverride;
        [SerializeField] int _materialId = -1;

        public string VirtualPath => _virtualPath;
        public bool NoPhysics => _noPhysics;
        public string MaterialOverride => _materialOverride;
        public int MaterialId => _materialId;

        readonly CgfGameObjectBuilder _goBuilder = new CgfGameObjectBuilder();
        CgfRuntimeImportResult _importResult;
        IReadOnlyList<CgfRuntimeImportResult> _lodResults;
        bool _releaseImportResultsOnDestroy = true;
        FcBrushGeometryPostProcessor.Artifacts _postProcessArtifacts;

        void Start()
        {
            if (string.IsNullOrEmpty(_virtualPath))
                return;

            var svc = FcBrushLoadService.Current;
            if (svc != null)
                svc.Register(this);
            else
                FallbackLoadAsync().Forget();
        }

        // Called by FcBrushLoadService after async import + texture preload complete.
        public void ApplyLoadResult(
            CgfRuntimeImportResult result,
            IReadOnlyList<CgfRuntimeImportResult> lodResults,
            string levelScopeId,
            bool releaseImportResultsOnDestroy = true)
        {
            if (result == null || !result.Success)
                return;

            _importResult = result;
            _lodResults = lodResults;
            _releaseImportResultsOnDestroy = releaseImportResultsOnDestroy;

            string meshName = Path.GetFileNameWithoutExtension(_virtualPath);
            var output = _goBuilder.Build(new CgfGameObjectBuilder.BuildRequest(
                result: result.BuildResult,
                parsedFile: result.ParsedFile,
                rigDefinition: null,
                name: meshName,
                materialService: CgfRuntimeImporter.MaterialService,
                textureScopeId: levelScopeId));

            _postProcessArtifacts = FcBrushGeometryPostProcessor.ApplyRuntimeParity(
                output.Root,
                result.BuildResult,
                result.ParsedFile,
                addPhysicsCollider: !_noPhysics,
                importScale: 0.01f);

            output.Root.transform.SetParent(transform, worldPositionStays: false);

            FcLevelRuntimeLodGroupBuilder.Apply(
                output.Root,
                output.MeshRenderer,
                lodResults,
                levelScopeId);

            ApplyMaterialOverride(output.Root, levelScopeId);

            // LOD children are attached after the base visual, so we ensure cull-off
            // once more after LOD assembly.
            FcBrushGeometryPostProcessor.DisableBackfaceCulling(output.Root);
        }

        // Fallback when FcBrushLoadService is absent (e.g. editor without full scene).
        async UniTaskVoid FallbackLoadAsync()
        {
            var ct = this.GetCancellationTokenOnDestroy();
            var resourceSvc = FcLevelResourceService.Current;
            string scopeId = resourceSvc != null ? resourceSvc.LevelScopeId : string.Empty;

            var result = await FcLevelGeometryImportHelper.ImportStaticGeometryWithTexturePreloadAsync(
                _virtualPath,
                scopeId,
                ct);

            if (ct.IsCancellationRequested)
                return;

            if (result == null || !result.Success)
            {
                Debug.LogWarning($"[FcBrush] '{_virtualPath}': {result?.ErrorMessage ?? "Import failed."}", this);
                return;
            }

            var lodResults = await FcLevelGeometryImportHelper.ImportSiblingLodsWithTexturePreloadAsync(
                _virtualPath,
                scopeId,
                lodService: null,
                ct: ct);

            if (ct.IsCancellationRequested)
                return;

            await UniTask.SwitchToMainThread(ct);
            if (this == null || ct.IsCancellationRequested)
                return;

            ApplyLoadResult(result, lodResults, scopeId, releaseImportResultsOnDestroy: true);
        }

        void ApplyMaterialOverride(GameObject visualRoot, string levelScopeId)
        {
            if (visualRoot == null)
                return;

            var service = FcLevelMaterialOverrideService.Current;
            if (service == null)
                return;

            bool hasMetadata = service.TryResolveBrushMaterialMetadata(
                _materialOverride,
                _materialId,
                out var metadata);

            if (hasMetadata)
                ApplyMaterialMetadata(visualRoot, metadata);

            if (string.IsNullOrWhiteSpace(_materialOverride) && _materialId < 0)
                return;

            var renderers = visualRoot.GetComponentsInChildren<Renderer>(includeInactive: true);
            List<string> slotDiagnostics = null;
            int unresolvedDiagnostics = 0;
            int targetedMisses = 0;
            for (int r = 0; r < renderers.Length; r++)
            {
                var renderer = renderers[r];
                if (renderer == null)
                    continue;

                var mats = renderer.sharedMaterials;
                if (mats == null || mats.Length == 0)
                    continue;

                int[] submeshMaterialIds = ResolveRendererSubmeshMaterialIds(renderer, visualRoot);
                if (!service.TryApplyBrushOverrideToRendererSlots(
                        _materialOverride,
                        _materialId,
                        levelScopeId,
                        mats,
                        submeshMaterialIds,
                        out var diagnostics))
                {
                    AppendSlotDiagnostics(
                        ref slotDiagnostics,
                        renderer,
                        diagnostics);
                    CountResolutionIssues(diagnostics, ref unresolvedDiagnostics, ref targetedMisses);
                    continue;
                }

                renderer.sharedMaterials = mats;
                AppendSlotDiagnostics(
                    ref slotDiagnostics,
                    renderer,
                    diagnostics);
                CountResolutionIssues(diagnostics, ref unresolvedDiagnostics, ref targetedMisses);
            }

            ApplySlotDiagnostics(visualRoot, slotDiagnostics);
            if (unresolvedDiagnostics > 0)
            {
                Debug.LogWarning(
                    $"[FcBrushMaterialOverride] '{_virtualPath}': unresolved slot resolutions={unresolvedDiagnostics}, targetedMisses={targetedMisses}, override='{_materialOverride}', materialId={_materialId}.",
                    this);
            }
        }

        int[] ResolveRendererSubmeshMaterialIds(Renderer renderer, GameObject visualRoot)
        {
            if (renderer == null || visualRoot == null)
                return null;

            var rootMeta = visualRoot.GetComponent<FcCachedGeometryMetadata>();
            if (rootMeta != null &&
                renderer.transform == visualRoot.transform &&
                rootMeta.SubmeshMaterialIds != null)
            {
                return rootMeta.SubmeshMaterialIds;
            }

            if (renderer.transform == visualRoot.transform)
                return _importResult?.BuildResult?.SubmeshMaterialIds;

            if (_lodResults == null || _lodResults.Count == 0)
                return null;

            string rendererName = renderer.gameObject != null ? renderer.gameObject.name : string.Empty;
            if (!TryParseLodRendererIndex(rendererName, out int lodIndex))
                return null;

            if (lodIndex < 0 || lodIndex >= _lodResults.Count)
                return null;

            return _lodResults[lodIndex]?.BuildResult?.SubmeshMaterialIds;
        }

        static bool TryParseLodRendererIndex(string name, out int lodIndex)
        {
            lodIndex = -1;
            if (string.IsNullOrEmpty(name))
                return false;

            if (!name.StartsWith("LOD", System.StringComparison.OrdinalIgnoreCase))
                return false;

            var suffix = name.Substring(3);
            if (!int.TryParse(suffix, out int parsedLodNumber))
                return false;

            lodIndex = parsedLodNumber - 1;
            return lodIndex >= 0;
        }

        static void ApplyMaterialMetadata(
            GameObject visualRoot,
            FcLevelMaterialOverrideService.BrushMaterialMetadata metadata)
        {
            if (visualRoot == null)
                return;

            var rootMeta = visualRoot.GetComponent<FcLevelMaterialMetadata>();
            if (rootMeta == null)
                rootMeta = visualRoot.AddComponent<FcLevelMaterialMetadata>();
            rootMeta.SetMetadata(metadata);

            var colliders = visualRoot.GetComponentsInChildren<Collider>(includeInactive: true);
            for (int i = 0; i < colliders.Length; i++)
            {
                var col = colliders[i];
                if (col == null)
                    continue;

                var colGo = col.gameObject;
                var colMeta = colGo.GetComponent<FcLevelMaterialMetadata>();
                if (colMeta == null)
                    colMeta = colGo.AddComponent<FcLevelMaterialMetadata>();
                colMeta.SetMetadata(metadata);
            }
        }

        static void AppendSlotDiagnostics(
            ref List<string> destination,
            Renderer renderer,
            FcLevelMaterialOverrideService.BrushSlotResolutionInfo[] diagnostics)
        {
            if (diagnostics == null || diagnostics.Length == 0)
                return;

            if (destination == null)
                destination = new List<string>(diagnostics.Length);

            string rendererName = renderer != null && renderer.gameObject != null
                ? renderer.gameObject.name
                : "<null-renderer>";

            for (int i = 0; i < diagnostics.Length; i++)
            {
                var d = diagnostics[i];
                destination.Add(
                    $"renderer={rendererName};slot={d.SlotIndex};submeshMatId={d.SubmeshMaterialId};" +
                    $"targeted={d.Targeted};applied={d.Applied};outcome={d.Outcome};" +
                    $"in={d.InputMaterialName};out={d.OutputMaterialName};detail={d.Detail}");
            }
        }

        static void ApplySlotDiagnostics(GameObject visualRoot, List<string> diagnostics)
        {
            if (visualRoot == null || diagnostics == null || diagnostics.Count == 0)
                return;

            var rootMeta = visualRoot.GetComponent<FcLevelMaterialMetadata>();
            if (rootMeta == null)
                rootMeta = visualRoot.AddComponent<FcLevelMaterialMetadata>();

            rootMeta.SetSlotResolutionDiagnostics(diagnostics.ToArray());
        }

        static void CountResolutionIssues(
            FcLevelMaterialOverrideService.BrushSlotResolutionInfo[] diagnostics,
            ref int unresolvedCount,
            ref int targetedMissCount)
        {
            if (diagnostics == null || diagnostics.Length == 0)
                return;

            for (int i = 0; i < diagnostics.Length; i++)
            {
                var d = diagnostics[i];
                if (d.Targeted && !d.Applied)
                    targetedMissCount++;

                if (string.Equals(d.Outcome, "unresolved", System.StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(d.Outcome, "fallback-failed", System.StringComparison.OrdinalIgnoreCase))
                {
                    unresolvedCount++;
                }
            }
        }

        void OnDestroy()
        {
            FcLevelGeometryResultOwnershipHelper.ReleaseOwnedResults(
                _releaseImportResultsOnDestroy,
                FcLevelResourceService.Current,
                _importResult,
                _lodResults);

            if (_postProcessArtifacts.PhysicsColliderMesh != null)
                Destroy(_postProcessArtifacts.PhysicsColliderMesh);
            if (_postProcessArtifacts.VisualFilteredMesh != null)
                Destroy(_postProcessArtifacts.VisualFilteredMesh);
        }
    }
}
