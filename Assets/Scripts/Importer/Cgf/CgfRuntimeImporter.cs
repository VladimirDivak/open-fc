using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using OpenFarCry.Importer.Texture;

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
            new CgfMaterialImportService(
                cache: SharedMaterialCache,
                textureRuntimeService: TextureImportService.RuntimeService);

        public static CgfRuntimeImportService Service => SharedService;
        public static TextureRuntimeImportService TextureService => TextureImportService.RuntimeService;

        // Use when creating a BuildRequest to get per-submesh materials from parsed chunks.
        public static CgfMaterialImportService MaterialService => SharedMaterialService;

        public static void ClearMaterialCache() => SharedMaterialService.ClearCache();
        public static int MaterialCacheCount => SharedMaterialService.CachedCount;

        public static CgfRuntimeImportResult Import(CgfRuntimeImportRequest request, string levelScopeId = null)
        {
            return SharedService.Import(request, levelScopeId);
        }

        public static UniTask<CgfRuntimeImportResult> ImportAsync(
            CgfRuntimeImportRequest request,
            string levelScopeId = null,
            CancellationToken ct = default)
        {
            return SharedService.ImportAsync(request, levelScopeId, ct);
        }

        public static UniTask<IReadOnlyDictionary<string, CgfRuntimeImportResult>> PreloadAsync(
            IReadOnlyList<CgfRuntimeImportRequest> requests,
            string levelScopeId = null,
            CancellationToken ct = default)
        {
            return SharedService.PreloadAsync(requests, levelScopeId, ct);
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
            TextureImportService.ReleaseLevelScope(levelScopeId);
            SharedMaterialService.ReleaseLevelScope(levelScopeId);
        }

        public static int TrimUnused()
        {
            int removed = SharedService.TrimUnused();
            removed += TextureImportService.TrimUnusedRuntimeCache();
            removed += SharedMaterialService.TrimUnused();
            return removed;
        }

        public static TextureRuntimeImportService.RuntimeDiagnostics GetTextureRuntimeDiagnostics()
        {
            return TextureImportService.RuntimeService.GetRuntimeDiagnostics();
        }

        public static string GetTextureRuntimeDebugReport()
        {
            return TextureImportService.RuntimeService.BuildRuntimeDebugReport();
        }

        public static void ClearRuntimeCache()
        {
            SharedService.ClearRuntimeCache();
            SharedMaterialService.ClearCache();
            TextureImportService.RuntimeService.ClearRuntimeCache();
        }

        // Frees Allocator.Persistent native data held by cached parsed CGF files without
        // destroying built meshes. Call before an editor domain reload to avoid leaking
        // the parsed-geometry NativeArrays.
        public static void DisposeParsedNativeData()
        {
            SharedService.DisposeParsedNativeData();
        }

        public static CgfRuntimeAssetCache.Stats GetCacheStats()
        {
            return SharedService.GetCacheStats();
        }
    }
}
