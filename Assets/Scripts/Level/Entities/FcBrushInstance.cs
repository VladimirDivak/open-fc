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

        public string VirtualPath => _virtualPath;
        public bool NoPhysics => _noPhysics;

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
