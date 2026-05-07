namespace OpenFarCry.Importer.Texture
{
    public static class TextureImportService
    {
        public static readonly string[] SupportedExtensions = { ".dds", ".bmp", ".tga" };

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
