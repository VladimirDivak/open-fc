namespace OpenFarCry.Importer.Cgf
{
    public static class CgfRuntimeImporter
    {
        static readonly CgfRuntimeAssetCache SharedCache = new CgfRuntimeAssetCache();
        static readonly CgfRuntimeImportService SharedService = new CgfRuntimeImportService(
            resourceService: CgfResourceImportService.Instance,
            runtimeCache: SharedCache);

        static readonly CgfMaterialRuntimeCache SharedMaterialCache = new CgfMaterialRuntimeCache();
        static readonly CgfMaterialImportService SharedMaterialService =
            new CgfMaterialImportService(SharedMaterialCache);

        public static CgfRuntimeImportService Service => SharedService;

        // Use when creating a BuildRequest to get per-submesh materials from parsed chunks.
        public static CgfMaterialImportService MaterialService => SharedMaterialService;

        public static void ClearMaterialCache() => SharedMaterialService.ClearCache();
        public static int MaterialCacheCount => SharedMaterialService.CachedCount;

        public static CgfRuntimeImportResult Import(CgfRuntimeImportRequest request, string levelScopeId = null)
        {
            return SharedService.Import(request, levelScopeId);
        }

        public static void Release(string parsedCacheKey)
        {
            SharedService.Release(parsedCacheKey);
        }

        public static void Release(CgfRuntimeImportResult importResult)
        {
            SharedService.Release(importResult);
        }

        public static void ReleaseModel(string modelCacheKey)
        {
            SharedService.ReleaseModel(modelCacheKey);
        }

        public static void ReleaseLevelScope(string levelScopeId)
        {
            SharedService.ReleaseLevelScope(levelScopeId);
        }

        public static int TrimUnused()
        {
            return SharedService.TrimUnused();
        }

        public static void ClearRuntimeCache()
        {
            SharedService.ClearRuntimeCache();
            SharedMaterialService.ClearCache();
        }

        public static CgfRuntimeAssetCache.Stats GetCacheStats()
        {
            return SharedService.GetCacheStats();
        }
    }
}
