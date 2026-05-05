using System;
using System.IO;
using OpenFarCry.FileSystem;

namespace OpenFarCry.Importer.Texture
{
    public static class TextureImportService
    {
        public static readonly string[] SupportedExtensions = { ".dds", ".bmp", ".tga" };

        public static bool IsSupportedVirtualPath(string virtualPath)
        {
            if (string.IsNullOrWhiteSpace(virtualPath))
                return false;

            string ext = Path.GetExtension(virtualPath);
            if (string.IsNullOrEmpty(ext))
                return false;

            for (int i = 0; i < SupportedExtensions.Length; i++)
            {
                if (ext.Equals(SupportedExtensions[i], StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        public static string GetTargetAssetPath(string virtualPath)
        {
            if (!IsSupportedVirtualPath(virtualPath))
                throw new NotSupportedException($"Unsupported texture extension: '{virtualPath}'");

            return OpenFarCry.Importer.ImportAssetPaths.GetAssetPathWithOriginalExtension(virtualPath);
        }

        public static byte[] ReadSourceBytes(string virtualPath)
        {
            if (!IsSupportedVirtualPath(virtualPath))
                throw new NotSupportedException($"Unsupported texture extension: '{virtualPath}'");

            string normalized = OpenFarCry.Importer.ImportAssetPaths.NormalizeVirtualPath(virtualPath);
            return FcFileSystem.ReadAllBytes(normalized);
        }
    }
}
