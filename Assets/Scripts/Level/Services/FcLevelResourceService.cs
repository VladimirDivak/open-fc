using OpenFarCry.Importer.Cgf;
using UnityEngine;

namespace OpenFarCry.Level.Services
{
    // Singleton-per-scene service. Entity components look it up via Current.
    public sealed class FcLevelResourceService : MonoBehaviour
    {
        public static FcLevelResourceService Current { get; private set; }

        FcLevelCacheService _cacheService;

        public string LevelScopeId => _cacheService != null ? _cacheService.LevelScopeId : string.Empty;

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
