using UnityEngine;

namespace OpenFarCry.Importer.Texture
{
    public static class TextureImportService
    {
        public static readonly string[] SupportedExtensions = { ".dds", ".bmp", ".tga", ".jpg", ".jpeg" };

        // Shared runtime Texture2D cache keyed by normalised virtual path.
        public static readonly TextureRuntimeScopedCache RuntimeCache = new TextureRuntimeScopedCache();
        static readonly TextureRuntimeImportService SharedRuntimeService = new TextureRuntimeImportService(
            resourceService: TextureResourceImportService.Instance,
            runtimeCache: RuntimeCache);

        public static TextureResourceImportService ResourceService => TextureResourceImportService.Instance;
        public static TextureRuntimeImportService RuntimeService => SharedRuntimeService;

        public static bool IsSupportedVirtualPath(string virtualPath)
        {
            return ResourceService.IsSupportedVirtualPath(virtualPath);
        }

        public static string GetTargetAssetPath(string virtualPath)
        {
            return ResourceService.GetPrimaryProjectAssetPath(virtualPath);
        }

        public static byte[] ReadSourceBytes(string virtualPath)
        {
            return ResourceService.LoadProjectAssetSourceBytes(virtualPath);
        }

        public static Texture2D LoadRuntimeTexture(string virtualPath, TextureRuntimeImportOptions options = default)
        {
            return RuntimeService.Load(virtualPath, options);
        }

        public static bool TryLoadRuntimeTexture(string virtualPath, out Texture2D texture, TextureRuntimeImportOptions options = default)
        {
            return RuntimeService.TryLoad(virtualPath, out texture, options);
        }

        public static bool TryReadDdsFormatTag(byte[] bytes, out string formatTag, out string error)
        {
            return DdsRuntimeDecoder.TryReadFormatTag(bytes, out formatTag, out error);
        }

        public static TextureRuntimeScopedCache.Stats GetRuntimeCacheStats()
        {
            return RuntimeService.GetStats();
        }

        public static TextureRuntimeImportService.RuntimeDiagnostics GetRuntimeDiagnostics()
        {
            return RuntimeService.GetRuntimeDiagnostics();
        }

        public static string BuildRuntimeDebugReport()
        {
            return RuntimeService.BuildRuntimeDebugReport();
        }

        public static void ClearRuntimeCache()
        {
            RuntimeService.ClearRuntimeCache();
        }

        public static void ReleaseLevelScope(string levelScopeId)
        {
            RuntimeService.ReleaseLevelScope(levelScopeId);
        }

        public static int TrimUnusedRuntimeCache()
        {
            return RuntimeService.TrimUnused();
        }
    }
}
