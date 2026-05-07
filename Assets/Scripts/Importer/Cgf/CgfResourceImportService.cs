using System;
using System.Collections.Generic;
using System.IO;
using OpenFarCry.FileSystem;

namespace OpenFarCry.Importer.Cgf
{
    public sealed class CgfResourceImportService : IResourceImportService
    {
        public static readonly CgfResourceImportService Instance = new CgfResourceImportService();

        static readonly string[] Extensions = { ".cgf", ".cga" };

        public string ResourceKind => "CGF";
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
            if (!IsSupportedVirtualPath(virtualPath))
                throw new NotSupportedException($"Unsupported CGF/CGA extension: '{virtualPath}'");

            var paths = ImportAssetPaths.GetCgfCachePaths(virtualPath);
            return new[]
            {
                new ResourceProjectAssetPath("Mesh", paths.meshPath),
                new ResourceProjectAssetPath("Prefab", paths.prefabPath)
            };
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
                throw new NotSupportedException($"Unsupported CGF/CGA extension: '{virtualPath}'");

            string normalized = ImportAssetPaths.NormalizeVirtualPath(virtualPath);
            return FcFileSystem.ReadAllBytes(normalized);
        }
    }
}
