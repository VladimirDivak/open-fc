using System;
using System.Collections.Generic;
using System.IO;
using OpenFarCry.Importer.Texture;

namespace OpenFarCry.Importer.Cgf
{
    public static class CgfTexturePathResolver
    {
        public static IEnumerable<string> BuildTexturePathCandidates(CgfFile parsedFile, string normalizedTextureName)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            string sourceDir = GetSourceDirectory(parsedFile?.SourceVirtualPath);
            bool hasExtension = !string.IsNullOrEmpty(Path.GetExtension(normalizedTextureName));
            bool isRootedVirtualPath = IsRootedVirtualPath(normalizedTextureName);

            var baseCandidates = new List<string>();
            AddCandidate(baseCandidates, seen, normalizedTextureName);
            if (!isRootedVirtualPath && !string.IsNullOrEmpty(sourceDir))
                AddCandidate(baseCandidates, seen, $"{sourceDir}/{normalizedTextureName}");

            if (hasExtension)
            {
                for (int i = 0; i < baseCandidates.Count; i++)
                    yield return baseCandidates[i];
                yield break;
            }

            for (int i = 0; i < baseCandidates.Count; i++)
            {
                string stem = baseCandidates[i];
                for (int e = 0; e < TextureImportService.SupportedExtensions.Length; e++)
                    yield return $"{stem}{TextureImportService.SupportedExtensions[e]}";
            }
        }

        public static string NormalizeTextureName(string textureName)
        {
            if (string.IsNullOrWhiteSpace(textureName))
                return string.Empty;

            string normalized = textureName.Replace("\0", string.Empty).Trim().Replace('\\', '/').ToLowerInvariant();
            if (normalized.Length == 0)
                return string.Empty;

            int rootedIndex = FindKnownTextureRootIndex(normalized);
            if (rootedIndex > 0)
                normalized = normalized.Substring(rootedIndex);

            while (normalized.Length > 0)
            {
                char c = normalized[0];
                if (char.IsLetterOrDigit(c) || c == '_' || c == '/' || c == '.')
                    break;
                normalized = normalized.Substring(1);
            }

            if (normalized.StartsWith("./"))
                normalized = normalized.Substring(2);
            while (normalized.StartsWith("/"))
                normalized = normalized.Substring(1);
            return normalized;
        }

        static bool IsRootedVirtualPath(string normalizedTextureName)
        {
            if (string.IsNullOrEmpty(normalizedTextureName))
                return false;

            if (normalizedTextureName.StartsWith("/"))
                return true;

            return normalizedTextureName.StartsWith("objects/", StringComparison.Ordinal) ||
                   normalizedTextureName.StartsWith("textures/", StringComparison.Ordinal) ||
                   normalizedTextureName.StartsWith("levels/", StringComparison.Ordinal) ||
                   normalizedTextureName.StartsWith("terrain/", StringComparison.Ordinal) ||
                   normalizedTextureName.StartsWith("characters/", StringComparison.Ordinal);
        }

        static void AddCandidate(List<string> list, HashSet<string> seen, string value)
        {
            string normalized = NormalizeTextureName(value);
            if (string.IsNullOrEmpty(normalized))
                return;
            if (!seen.Add(normalized))
                return;
            list.Add(normalized);
        }

        static string GetSourceDirectory(string sourceVirtualPath)
        {
            if (string.IsNullOrWhiteSpace(sourceVirtualPath))
                return string.Empty;

            string normalizedSource = ImportAssetPaths.NormalizeVirtualPath(sourceVirtualPath);
            int slash = normalizedSource.LastIndexOf('/');
            if (slash <= 0)
                return string.Empty;
            return normalizedSource.Substring(0, slash);
        }

        static int FindKnownTextureRootIndex(string value)
        {
            int best = -1;
            best = MinPositive(best, value.IndexOf("objects/", StringComparison.Ordinal));
            best = MinPositive(best, value.IndexOf("textures/", StringComparison.Ordinal));
            best = MinPositive(best, value.IndexOf("levels/", StringComparison.Ordinal));
            best = MinPositive(best, value.IndexOf("terrain/", StringComparison.Ordinal));
            best = MinPositive(best, value.IndexOf("characters/", StringComparison.Ordinal));
            return best;
        }

        static int MinPositive(int current, int candidate)
        {
            if (candidate < 0)
                return current;
            if (current < 0)
                return candidate;
            return candidate < current ? candidate : current;
        }
    }
}
