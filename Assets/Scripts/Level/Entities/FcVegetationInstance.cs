using System;
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
        [SerializeField] string _materialOverride;

        public string VirtualPath   => _virtualPath;
        public int    TypeIndex     => _typeIndex;
        public float  InstanceScale => _instanceScale;

        public void Initialize(string virtualPath, int typeIndex, float instanceScale, string materialOverride = null)
        {
            _virtualPath      = virtualPath;
            _typeIndex        = typeIndex;
            _instanceScale    = instanceScale > 0f ? instanceScale : 1f;
            _materialOverride = materialOverride ?? string.Empty;
        }

        readonly CgfGameObjectBuilder _goBuilder = new CgfGameObjectBuilder();
        CgfRuntimeImportResult              _importResult;
        IReadOnlyList<CgfRuntimeImportResult> _lodResults;
        bool _releaseImportResultsOnDestroy = true;

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
            IReadOnlyList<CgfRuntimeImportResult> lodResults,
            string levelScopeId,
            bool releaseImportResultsOnDestroy = true)
        {
            if (result == null || !result.Success) return;

            _importResult = result;
            _lodResults   = lodResults;
            _releaseImportResultsOnDestroy = releaseImportResultsOnDestroy;

            Func<Material[], int[], Material[]> materialOverrider = null;
            if (!string.IsNullOrWhiteSpace(_materialOverride))
            {
                var overrideSvc = FcLevelMaterialOverrideService.Current;
                string overrideName = _materialOverride;
                if (overrideSvc != null)
                {
                    materialOverrider = (mats, submeshIds) =>
                    {
                        overrideSvc.TryApplyBrushOverrideToRendererSlots(overrideName, -1, levelScopeId, mats, submeshIds, out _);
                        return mats;
                    };
                }
            }

            string meshName = Path.GetFileNameWithoutExtension(_virtualPath);
            var output = _goBuilder.Build(new CgfGameObjectBuilder.BuildRequest(
                result: result.BuildResult,
                parsedFile: result.ParsedFile,
                rigDefinition: null,
                name: meshName,
                materialService: CgfRuntimeImporter.MaterialService,
                textureScopeId: levelScopeId,
                materialOverrider: materialOverrider));

            output.Root.transform.SetParent(transform, worldPositionStays: false);

            FcLevelRuntimeLodGroupBuilder.Apply(
                output.Root,
                output.MeshRenderer,
                lodResults,
                levelScopeId,
                materialOverrider);
        }

        async UniTaskVoid FallbackLoadAsync()
        {
            var ct = this.GetCancellationTokenOnDestroy();
            var resourceSvc = FcLevelResourceService.Current;
            string scopeId = resourceSvc != null ? resourceSvc.LevelScopeId : string.Empty;

            var result = await FcLevelGeometryImportHelper.ImportStaticGeometryWithTexturePreloadAsync(
                _virtualPath,
                scopeId,
                ct);

            if (ct.IsCancellationRequested) return;
            if (result == null || !result.Success) return;

            if (ct.IsCancellationRequested) return;
            var lodResults = await FcLevelGeometryImportHelper.ImportSiblingLodsWithTexturePreloadAsync(
                _virtualPath,
                scopeId,
                lodService: null,
                ct: ct);

            if (ct.IsCancellationRequested) return;

            if (!string.IsNullOrWhiteSpace(_materialOverride))
            {
                var overrideSvc = FcLevelMaterialOverrideService.Current;
                if (overrideSvc != null)
                    await overrideSvc.PreloadOverrideTexturesAsync(_materialOverride, -1, scopeId, ct);
            }

            if (ct.IsCancellationRequested) return;
            await UniTask.SwitchToMainThread(ct);
            if (this == null || ct.IsCancellationRequested) return;

            ApplyLoadResult(result, lodResults, scopeId);
        }

        void OnDestroy()
        {
            FcLevelGeometryResultOwnershipHelper.ReleaseOwnedResults(
                _releaseImportResultsOnDestroy,
                FcLevelResourceService.Current,
                _importResult,
                _lodResults);
        }
    }
}
