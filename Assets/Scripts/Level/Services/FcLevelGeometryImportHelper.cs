using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using OpenFarCry.Importer.Cgf;

namespace OpenFarCry.Level.Services
{
    static class FcLevelGeometryImportHelper
    {
        public static async UniTask<CgfRuntimeImportResult> ImportStaticGeometryWithTexturePreloadAsync(
            string virtualPath,
            string levelScopeId,
            CancellationToken ct,
            bool skipTexturePreload = false)
        {
            var result = await CgfRuntimeImporter.ImportAsync(
                CreateStaticGeometryRequest(virtualPath),
                levelScopeId,
                ct);

            if (ct.IsCancellationRequested || result == null || !result.Success)
                return result;

            if (!skipTexturePreload)
                await CgfRuntimeImporter.MaterialService.PreloadTexturesAsync(
                    result.ParsedFile,
                    result.Mesh,
                    result.BuildResult?.SubmeshMaterialIds,
                    levelScopeId,
                    ct);

            return result;
        }

        public static async UniTask<List<CgfRuntimeImportResult>> ImportSiblingLodsWithTexturePreloadAsync(
            string baseVirtualPath,
            string levelScopeId,
            CgfLodImportService lodService,
            CancellationToken ct,
            bool skipTexturePreload = false)
        {
            var service = lodService ?? new CgfLodImportService();
            var lodPaths = service.FindSiblingLodPaths(baseVirtualPath);
            if (lodPaths == null || lodPaths.Count == 0)
                return new List<CgfRuntimeImportResult>(0);

            var lodResults = new List<CgfRuntimeImportResult>(lodPaths.Count);

            for (int i = 0; i < lodPaths.Count; i++)
            {
                if (ct.IsCancellationRequested)
                    break;

                var lodResult = await ImportStaticGeometryWithTexturePreloadAsync(
                    lodPaths[i],
                    levelScopeId,
                    ct,
                    skipTexturePreload);

                if (lodResult == null || !lodResult.Success)
                    continue;

                lodResults.Add(lodResult);
            }

            return lodResults;
        }

        static CgfRuntimeImportRequest CreateStaticGeometryRequest(string virtualPath)
        {
            return new CgfRuntimeImportRequest(
                virtualPath: virtualPath,
                importSkeleton: false,
                importAnimations: false,
                importScale: 0.01f,
                useRuntimeMemoryCache: true);
        }
    }
}
