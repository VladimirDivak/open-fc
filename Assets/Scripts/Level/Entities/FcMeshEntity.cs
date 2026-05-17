using System.IO;
using System.Diagnostics;
using System.Threading;
using Cysharp.Threading.Tasks;
using OpenFarCry.Importer.Cgf;
using OpenFarCry.Level.Data;
using OpenFarCry.Level.Services;
using UnityEngine;
using Debug = UnityEngine.Debug;
using System;

namespace OpenFarCry.Level.Entities
{
    public class FcMeshEntity : FcEntity
    {
        [Header("Mesh")]
        [SerializeField] protected string _virtualPath;
        [SerializeField] protected float _importScale = 0.01f;
        [SerializeField] protected bool _importSkeleton;
        [SerializeField] protected string _materialOverride;

        public string VirtualPath => _virtualPath;
        public string MaterialOverride => _materialOverride;

        // Runtime-only init: set virtualPath before Start() fires (no SerializedObject).
        public void Initialize(string virtualPath)
        {
            _virtualPath = string.IsNullOrEmpty(virtualPath)
                ? string.Empty
                : virtualPath.ToLowerInvariant().Replace('\\', '/');
        }

        protected readonly CgfGameObjectBuilder _goBuilder = new CgfGameObjectBuilder();
        protected readonly CgfLodImportService _lodService = new CgfLodImportService();
        protected CgfRuntimeImportResult _importResult;
        protected CgfGameObjectBuilder.BuildOutput _lastBuildOutput;

        protected virtual void Start()
        {
            if (string.IsNullOrEmpty(_virtualPath)) return;
            var loadService = FcEntityLoadService.Current;
            if (loadService != null && FcMeshLoadService.Current != null)
            {
                loadService.Enqueue(this, GetDefaultLoadPriority());
                return;
            }

            FallbackLoadAsync().Forget();
        }

        async UniTaskVoid FallbackLoadAsync()
        {
            var ct = this.GetCancellationTokenOnDestroy();
            var request = CreateLoadRequest();
            var sw = Stopwatch.StartNew();
            try
            {
                var result = await CgfRuntimeImporter.ImportAsync(
                    new CgfRuntimeImportRequest(
                        virtualPath: request.VirtualPath,
                        selectedMeshChunkId: request.SelectedMeshChunkId,
                        importSkeleton: request.ImportSkeleton,
                        importAnimations: false,
                        importScale: request.ImportScale,
                        useRuntimeMemoryCache: request.UseRuntimeMemoryCache),
                    request.LevelScopeId,
                    ct);

                if (ct.IsCancellationRequested) return;
                if (!result.Success)
                {
                    Debug.LogWarning($"[FcMeshEntity] CGF import failed for '{_virtualPath}': {result.ErrorMessage}", this);
                    return;
                }

                if (request.PreloadTextures)
                {
                    await CgfRuntimeImporter.MaterialService.PreloadTexturesAsync(
                        result.ParsedFile,
                        result.Mesh,
                        result.BuildResult?.SubmeshMaterialIds,
                        request.LevelScopeId,
                        ct);
                }

                if (ct.IsCancellationRequested) return;

                if (!string.IsNullOrWhiteSpace(_materialOverride))
                {
                    var overrideSvc = FcLevelMaterialOverrideService.Current;
                    if (overrideSvc != null)
                        await overrideSvc.PreloadOverrideTexturesAsync(_materialOverride, -1, request.LevelScopeId, ct);
                }

                if (ct.IsCancellationRequested) return;
                await UniTask.SwitchToMainThread(ct);
                if (this == null || ct.IsCancellationRequested) return;
                ApplyLoadedMesh(LoadedMeshArtifact.Completed(result, request.LevelScopeId, sw.Elapsed.TotalMilliseconds));
            }
            catch (OperationCanceledException) { }
        }

        public virtual EntityLoadPriority GetDefaultLoadPriority() => EntityLoadPriority.Background;

        public virtual string GetLevelScopeId()
        {
            var service = ResourceService;
            return service != null ? service.LevelScopeId : string.Empty;
        }

        public virtual FcMeshLoadRequest CreateLoadRequest()
        {
            return new FcMeshLoadRequest(
                virtualPath: _virtualPath,
                levelScopeId: GetLevelScopeId(),
                selectedMeshChunkId: -1,
                importSkeleton: _importSkeleton,
                importScale: _importScale,
                useRuntimeMemoryCache: true,
                preloadTextures: true);
        }

        public virtual void ApplyLoadedMesh(LoadedMeshArtifact artifact)
        {
            if (artifact == null || !artifact.Success || artifact.ImportResult == null)
                return;

            _importResult = artifact.ImportResult;
            ApplyResult(artifact.ImportResult);
        }

        public virtual void ReleaseLoadedMesh()
        {
            if (_importResult != null)
            {
                ResourceService?.ReleaseImportResult(_importResult);
                _importResult = null;
            }
        }

        protected virtual void ApplyResult(CgfRuntimeImportResult result)
        {
            if (result.BuildResult == null || result.Mesh == null) return;

            string meshName = Path.GetFileNameWithoutExtension(_virtualPath);
            string scopeId = ResourceService != null ? ResourceService.LevelScopeId : null;

            Func<Material[], int[], Material[]> materialOverrider = null;
            if (!string.IsNullOrWhiteSpace(_materialOverride))
            {
                var overrideSvc = FcLevelMaterialOverrideService.Current;
                string overrideName = _materialOverride;
                if (overrideSvc != null)
                {
                    materialOverrider = (mats, submeshIds) =>
                    {
                        overrideSvc.TryApplyBrushOverrideToRendererSlots(overrideName, -1, scopeId, mats, submeshIds, out _);
                        return mats;
                    };
                }
            }

            _lastBuildOutput = _goBuilder.Build(new CgfGameObjectBuilder.BuildRequest(
                result: result.BuildResult,
                parsedFile: result.ParsedFile,
                rigDefinition: null,
                name: meshName,
                materialService: CgfRuntimeImporter.MaterialService,
                textureScopeId: scopeId,
                materialOverrider: materialOverrider));

            _lastBuildOutput.Root.transform.SetParent(transform, worldPositionStays: false);

            if (!result.BuildResult.HasSkeleton && result.Mesh != null)
            {
                var col = _lastBuildOutput.Root.AddComponent<MeshCollider>();
                col.sharedMesh = result.Mesh;
            }

            var lodPaths = _lodService.FindSiblingLodPaths(result.VirtualPath);
            if (lodPaths.Count > 0)
                _lodService.ConfigureLodGroup(_lastBuildOutput.Root, result.BuildResult.HasSkeleton, _importScale, lodPaths,
                    materialService: CgfRuntimeImporter.MaterialService,
                    textureScopeId: scopeId,
                    materialOverrider: materialOverrider);
        }

        public override void SetData(FcEntityDesc desc)
        {
            base.SetData(desc);
            string path = desc.GetModelVirtualPath();
            _virtualPath = string.IsNullOrEmpty(path) ? string.Empty : path.ToLowerInvariant().Replace('\\', '/');
            _materialOverride = desc.GetMaterialOverride() ?? string.Empty;
        }

        protected virtual void OnDestroy()
        {
            ReleaseLoadedMesh();
        }
    }
}
