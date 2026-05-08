using System.IO;
using OpenFarCry.Importer.Cgf;
using OpenFarCry.Level.Data;
using UnityEngine;

namespace OpenFarCry.Level.Entities
{
    public class FcMeshEntity : FcEntity
    {
        [Header("Mesh")]
        [SerializeField] protected string _virtualPath;
        [SerializeField] protected float _importScale = 0.01f;
        [SerializeField] protected bool _importSkeleton;

        public string VirtualPath => _virtualPath;

        protected readonly CgfGameObjectBuilder _goBuilder = new CgfGameObjectBuilder();
        protected readonly CgfLodImportService _lodService = new CgfLodImportService();
        protected CgfRuntimeImportResult _importResult;
        protected CgfGameObjectBuilder.BuildOutput _lastBuildOutput;

        protected virtual void Start()
        {
            if (string.IsNullOrEmpty(_virtualPath)) return;

            var service = ResourceService;
            if (service == null)
            {
                Debug.LogWarning($"[FcMeshEntity] No FcLevelResourceService found for '{name}'", this);
                return;
            }

            var request = new CgfRuntimeImportRequest(
                virtualPath: _virtualPath,
                importSkeleton: _importSkeleton,
                importAnimations: false,
                importScale: _importScale,
                useRuntimeMemoryCache: true);

            var result = service.ImportCgf(request);
            if (!result.Success)
            {
                Debug.LogWarning($"[FcMeshEntity] CGF import failed for '{_virtualPath}': {result.ErrorMessage}", this);
                return;
            }

            _importResult = result;
            ApplyResult(result);
        }

        protected virtual void ApplyResult(CgfRuntimeImportResult result)
        {
            if (result.BuildResult == null || result.Mesh == null) return;

            string meshName = Path.GetFileNameWithoutExtension(_virtualPath);
            _lastBuildOutput = _goBuilder.Build(new CgfGameObjectBuilder.BuildRequest(
                result: result.BuildResult,
                parsedFile: result.ParsedFile,
                rigDefinition: null,
                name: meshName,
                materialService: CgfRuntimeImporter.MaterialService));

            _lastBuildOutput.Root.transform.SetParent(transform, worldPositionStays: false);

            if (!result.BuildResult.HasSkeleton && result.Mesh != null)
            {
                var col = _lastBuildOutput.Root.AddComponent<MeshCollider>();
                col.sharedMesh = result.Mesh;
            }

            var lodPaths = _lodService.FindSiblingLodPaths(result.VirtualPath);
            if (lodPaths.Count > 0)
                _lodService.ConfigureLodGroup(_lastBuildOutput.Root, result.BuildResult.HasSkeleton, _importScale, lodPaths,
                    materialService: CgfRuntimeImporter.MaterialService);
        }

        public override void SetData(FcEntityDesc desc)
        {
            base.SetData(desc);
            string path = desc.GetModelVirtualPath();
            _virtualPath = string.IsNullOrEmpty(path) ? string.Empty : path.ToLowerInvariant().Replace('\\', '/');
        }

        protected virtual void OnDestroy()
        {
            if (_importResult != null)
                ResourceService?.ReleaseImportResult(_importResult);
        }
    }
}
