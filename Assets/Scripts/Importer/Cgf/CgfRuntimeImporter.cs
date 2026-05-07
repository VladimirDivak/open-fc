namespace OpenFarCry.Importer.Cgf
{
    public static class CgfRuntimeImporter
    {
        static readonly CgfRuntimeAssetCache SharedCache = new CgfRuntimeAssetCache();
        static readonly CgfRuntimeImportService SharedService = new CgfRuntimeImportService(
            resourceService: CgfResourceImportService.Instance,
            runtimeCache: SharedCache);

        public static CgfRuntimeImportService Service => SharedService;

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
        }

        public static CgfRuntimeAssetCache.Stats GetCacheStats()
        {
            return SharedService.GetCacheStats();
        }
    }
}
