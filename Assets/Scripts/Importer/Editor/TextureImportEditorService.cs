using System;
using System.IO;
using OpenFarCry.Importer.Texture;
using UnityEditor;
using UnityEngine;

namespace OpenFarCry.Importer.Editor
{
    public enum TextureImportStatus
    {
        Imported,
        SkippedAlreadyExists,
        UnsupportedPath,
        Failed
    }

    public readonly struct TextureImportOptions
    {
        public readonly bool OverwriteExisting;
        public readonly bool Readable;
        public readonly bool AutoDetectNormalMap;

        public TextureImportOptions(bool overwriteExisting, bool readable, bool autoDetectNormalMap)
        {
            OverwriteExisting = overwriteExisting;
            Readable = readable;
            AutoDetectNormalMap = autoDetectNormalMap;
        }
    }

    public readonly struct TextureImportResult
    {
        public readonly TextureImportStatus Status;
        public readonly string VirtualPath;
        public readonly string AssetPath;
        public readonly string Message;
        public readonly Texture2D Texture;

        public bool Success => Status == TextureImportStatus.Imported;

        public TextureImportResult(
            TextureImportStatus status,
            string virtualPath,
            string assetPath,
            string message,
            Texture2D texture = null)
        {
            Status = status;
            VirtualPath = virtualPath;
            AssetPath = assetPath;
            Message = message;
            Texture = texture;
        }
    }

    public static class TextureImportEditorService
    {
        public static TextureImportResult ImportTexture(string virtualPath, TextureImportOptions options)
        {
            if (!TextureImportService.IsSupportedVirtualPath(virtualPath))
            {
                return new TextureImportResult(
                    TextureImportStatus.UnsupportedPath,
                    virtualPath,
                    null,
                    "Unsupported texture path.");
            }

            string normalizedVirtualPath = OpenFarCry.Importer.ImportAssetPaths.NormalizeVirtualPath(virtualPath);
            string assetPath = TextureImportService.GetTargetAssetPath(normalizedVirtualPath);
            string fullPath = Path.GetFullPath(assetPath);

            try
            {
                string dir = Path.GetDirectoryName(fullPath);
                if (!string.IsNullOrEmpty(dir))
                    Directory.CreateDirectory(dir);

                if (!options.OverwriteExisting && File.Exists(fullPath))
                {
                    return new TextureImportResult(
                        TextureImportStatus.SkippedAlreadyExists,
                        normalizedVirtualPath,
                        assetPath,
                        "Target asset already exists.");
                }

                byte[] bytes = TextureImportService.ReadSourceBytes(normalizedVirtualPath);
                File.WriteAllBytes(fullPath, bytes);

                AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
                ApplyTextureImporterSettings(assetPath, normalizedVirtualPath, options);

                var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
                return new TextureImportResult(
                    TextureImportStatus.Imported,
                    normalizedVirtualPath,
                    assetPath,
                    "Imported.",
                    texture);
            }
            catch (Exception e)
            {
                return new TextureImportResult(
                    TextureImportStatus.Failed,
                    normalizedVirtualPath,
                    assetPath,
                    e.Message);
            }
        }

        static void ApplyTextureImporterSettings(string assetPath, string virtualPath, TextureImportOptions options)
        {
            var importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
            if (importer == null)
                return;

            bool wantsNormalMap = options.AutoDetectNormalMap && LooksLikeNormalMap(virtualPath);
            bool changed = false;

            TextureImporterType targetType = wantsNormalMap
                ? TextureImporterType.NormalMap
                : TextureImporterType.Default;
            if (importer.textureType != targetType)
            {
                importer.textureType = targetType;
                changed = true;
            }

            bool targetSrgb = !wantsNormalMap;
            if (importer.sRGBTexture != targetSrgb)
            {
                importer.sRGBTexture = targetSrgb;
                changed = true;
            }

            if (importer.isReadable != options.Readable)
            {
                importer.isReadable = options.Readable;
                changed = true;
            }

            if (changed)
                importer.SaveAndReimport();
        }

        static bool LooksLikeNormalMap(string virtualPath)
        {
            string lower = virtualPath.ToLowerInvariant();
            return lower.Contains("_ddn") ||
                   lower.Contains("_ddna") ||
                   lower.Contains("_ddna2") ||
                   lower.Contains("_bump") ||
                   lower.EndsWith("_normal.dds", StringComparison.Ordinal) ||
                   lower.EndsWith("_normal.tga", StringComparison.Ordinal) ||
                   lower.EndsWith("_normal.bmp", StringComparison.Ordinal);
        }
    }
}
