namespace OpenFarCry.Importer.Texture
{
    public static class TextureImportService
    {
        public static readonly string[] SupportedExtensions = { ".dds", ".bmp", ".tga" };

        // Shared Texture2D cache; populated by a DDS parser once one is implemented.
        public static readonly TextureRuntimeCache RuntimeCache = new TextureRuntimeCache();

        public static TextureResourceImportService ResourceService => TextureResourceImportService.Instance;

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
    }
}
