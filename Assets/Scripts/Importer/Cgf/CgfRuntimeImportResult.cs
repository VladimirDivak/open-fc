using System.Collections.Generic;
using UnityEngine;

namespace OpenFarCry.Importer.Cgf
{
    public sealed class CgfRuntimeImportResult
    {
        public readonly bool Success;
        public readonly string VirtualPath;
        public readonly string ErrorMessage;
        public readonly CgfFile ParsedFile;
        public readonly BuildResult BuildResult;
        public readonly bool UsedRuntimeMemoryCache;
        public readonly string ParsedCacheKey;
        public readonly string ModelCacheKey;
        public readonly IReadOnlyList<string> Warnings;

        public Mesh Mesh => BuildResult?.Mesh;

        CgfRuntimeImportResult(
            bool success,
            string virtualPath,
            string errorMessage,
            CgfFile parsedFile,
            BuildResult buildResult,
            bool usedRuntimeMemoryCache,
            string parsedCacheKey,
            string modelCacheKey,
            IReadOnlyList<string> warnings)
        {
            Success = success;
            VirtualPath = virtualPath;
            ErrorMessage = errorMessage;
            ParsedFile = parsedFile;
            BuildResult = buildResult;
            UsedRuntimeMemoryCache = usedRuntimeMemoryCache;
            ParsedCacheKey = parsedCacheKey;
            ModelCacheKey = modelCacheKey;
            Warnings = warnings;
        }

        public static CgfRuntimeImportResult Failed(string virtualPath, string errorMessage)
        {
            return new CgfRuntimeImportResult(
                success: false,
                virtualPath: virtualPath,
                errorMessage: errorMessage,
                parsedFile: null,
                buildResult: null,
                usedRuntimeMemoryCache: false,
                parsedCacheKey: null,
                modelCacheKey: null,
                warnings: null);
        }

        public static CgfRuntimeImportResult Completed(
            string virtualPath,
            CgfFile parsedFile,
            BuildResult buildResult,
            bool usedRuntimeMemoryCache,
            string parsedCacheKey,
            string modelCacheKey,
            IReadOnlyList<string> warnings = null)
        {
            return new CgfRuntimeImportResult(
                success: true,
                virtualPath: virtualPath,
                errorMessage: null,
                parsedFile: parsedFile,
                buildResult: buildResult,
                usedRuntimeMemoryCache: usedRuntimeMemoryCache,
                parsedCacheKey: parsedCacheKey,
                modelCacheKey: modelCacheKey,
                warnings: warnings);
        }
    }
}
