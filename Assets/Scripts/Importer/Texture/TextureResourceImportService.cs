using System;
using System.Collections.Generic;
using System.IO;
using OpenFarCry.FileSystem;

namespace OpenFarCry.Importer.Texture
{
    public sealed class TextureResourceImportService : IResourceImportService
    {
        public static readonly TextureResourceImportService Instance = new TextureResourceImportService();

        static readonly string[] Extensions = { ".dds", ".bmp", ".tga" };

        public string ResourceKind => "Texture";
        public IReadOnlyList<string> SupportedExtensions => Extensions;

        public bool IsSupportedVirtualPath(string virtualPath)
        {
            if (string.IsNullOrWhiteSpace(virtualPath))
                return false;

            string ext = Path.GetExtension(virtualPath);
            if (string.IsNullOrEmpty(ext))
                return false;

            for (int i = 0; i < Extensions.Length; i++)
            {
                if (ext.Equals(Extensions[i], StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        public IReadOnlyList<ResourceProjectAssetPath> GetProjectAssetPaths(string virtualPath)
        {
            return new[]
            {
                new ResourceProjectAssetPath("Texture", GetPrimaryProjectAssetPath(virtualPath))
            };
        }

        public string GetPrimaryProjectAssetPath(string virtualPath)
        {
            if (!IsSupportedVirtualPath(virtualPath))
                throw new NotSupportedException($"Unsupported texture extension: '{virtualPath}'");

            return ImportAssetPaths.GetAssetPathWithOriginalExtension(virtualPath);
        }

        public byte[] LoadRuntimeResourceBytes(string virtualPath)
        {
            return ReadPakSourceBytes(virtualPath);
        }

        public byte[] LoadProjectAssetSourceBytes(string virtualPath)
        {
            return ReadPakSourceBytes(virtualPath);
        }

        byte[] ReadPakSourceBytes(string virtualPath)
        {
            if (!IsSupportedVirtualPath(virtualPath))
                throw new NotSupportedException($"Unsupported texture extension: '{virtualPath}'");

            string normalized = ImportAssetPaths.NormalizeVirtualPath(virtualPath);
            return FcFileSystem.ReadAllBytes(normalized);
        }
    }
}
