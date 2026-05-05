using System;
using System.IO;
using System.Text;

namespace OpenFarCry.Importer
{
    public static class ImportAssetPaths
    {
        public const string CacheRoot = "Assets/FCData";
        public const string AnimationCacheRoot = CacheRoot + "/AnimationCache";

        public static string NormalizeVirtualPath(string virtualPath)
        {
            if (string.IsNullOrWhiteSpace(virtualPath))
                throw new ArgumentException("Virtual path is null or empty.", nameof(virtualPath));

            return virtualPath
                .Replace('\\', '/')
                .TrimStart('/')
                .ToLowerInvariant();
        }

        public static string GetBaseAssetPathNoExtension(string virtualPath)
        {
            string normalized = NormalizeVirtualPath(virtualPath);
            int ext = normalized.LastIndexOf('.');
            string noExt = ext > 0 ? normalized.Substring(0, ext) : normalized;
            return $"{CacheRoot}/{noExt}";
        }

        public static string GetAssetPathWithOriginalExtension(string virtualPath)
        {
            string normalized = NormalizeVirtualPath(virtualPath);
            return $"{CacheRoot}/{normalized}";
        }

        public static (string meshPath, string prefabPath) GetCgfCachePaths(string virtualPath)
        {
            string basePath = GetBaseAssetPathNoExtension(virtualPath);
            return (basePath + ".asset", basePath + ".prefab");
        }

        public static string GetSharedAnimationClipPath(string clipCacheKey, string alias)
        {
            if (string.IsNullOrWhiteSpace(clipCacheKey))
                throw new ArgumentException("Clip cache key is null or empty.", nameof(clipCacheKey));

            string normalizedKey = clipCacheKey.Trim().ToLowerInvariant();
            string keyPrefix = normalizedKey.Length > 2 ? normalizedKey.Substring(0, 2) : normalizedKey;
            if (string.IsNullOrEmpty(keyPrefix))
                keyPrefix = "00";

            string safeAlias = SanitizePathSegment(string.IsNullOrWhiteSpace(alias) ? "anim" : alias);
            return $"{AnimationCacheRoot}/{keyPrefix}/{safeAlias}_{normalizedKey}.anim";
        }

        static string SanitizePathSegment(string value)
        {
            var invalid = Path.GetInvalidFileNameChars();
            var sb = new StringBuilder(value.Length);
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                if (c == '/' || c == '\\' || Array.IndexOf(invalid, c) >= 0)
                    sb.Append('_');
                else
                    sb.Append(c);
            }
            return sb.ToString();
        }
    }
}
