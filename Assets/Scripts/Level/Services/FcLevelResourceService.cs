using OpenFarCry.Importer.Cgf;
using UnityEngine;

namespace OpenFarCry.Level.Services
{
    // Singleton-per-scene service. Entity components look it up via Current.
    public sealed class FcLevelResourceService : MonoBehaviour
    {
        public static FcLevelResourceService Current { get; private set; }

        [Tooltip("Template material cloned for every static brush DecalProjector. " +
                 "Use a Shader Graph with Decal target; Texture2D property reference must be _BaseMap.")]
        [SerializeField] Material _decalMaterialTemplate;

        FcLevelCacheService _cacheService;

        public string LevelScopeId => _cacheService != null ? _cacheService.LevelScopeId : string.Empty;
        public Material DecalMaterialTemplate => _decalMaterialTemplate;

        void Awake()
        {
            Current = this;
            _cacheService = GetComponent<FcLevelCacheService>();
            if (_cacheService == null)
                _cacheService = GetComponentInParent<FcLevelCacheService>();
        }

        void OnDestroy()
        {
            if (Current == this) Current = null;
            FcBrushDecalProjectorBuilder.ReleaseAll();
        }

        // Synchronous CGF import on the calling thread (main thread).
        // Heavy for large levels; see FcMeshEntity for usage pattern.
        public CgfRuntimeImportResult ImportCgf(CgfRuntimeImportRequest request)
        {
            return CgfRuntimeImporter.Import(request, LevelScopeId);
        }

        public void ReleaseImportResult(CgfRuntimeImportResult result)
        {
            CgfRuntimeImporter.Release(result);
        }
    }
}
