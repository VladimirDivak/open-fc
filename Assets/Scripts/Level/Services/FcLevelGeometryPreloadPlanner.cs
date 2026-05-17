using System;
using System.Collections.Generic;
using OpenFarCry.Importer;
using OpenFarCry.Importer.Cgf;
using OpenFarCry.Level.Data;
using OpenFarCry.Level.Entities;

namespace OpenFarCry.Level.Services
{
    public sealed class FcLevelGeometryPreloadPlanner
    {
        readonly CgfLodImportService _lodService = new CgfLodImportService();
        readonly Func<string, IReadOnlyList<string>> _findLodPaths;

        public FcLevelGeometryPreloadPlanner(Func<string, IReadOnlyList<string>> findLodPaths = null)
        {
            _findLodPaths = findLodPaths ?? FindLodPathsFromService;
        }

        public FcLevelGeometryPreloadPlan BuildVegetationPlan(
            FcLevelSupplementData supplement,
            IReadOnlyList<FcVegetationInstance> sceneVegetationFallback,
            IReadOnlyList<string> terrainServicePaths = null)
        {
            var uniqueBasePaths = new HashSet<string>(StringComparer.Ordinal);

            CollectVegetationPathsFromSupplement(supplement, uniqueBasePaths);
            CollectPathsFromSceneFallback(
                sceneVegetationFallback,
                uniqueBasePaths,
                static instance => instance.VirtualPath);
            CollectRawPaths(terrainServicePaths, uniqueBasePaths);

            return BuildPlanForPaths(
                uniqueBasePaths,
                FcLevelGeometrySourceKind.Vegetation,
                FcLevelGeometrySourceKind.VegetationLod);
        }

        public FcLevelGeometryPreloadPlan BuildBrushPlan(
            IReadOnlyList<FcBrushDesc> brushes,
            IReadOnlyList<FcBrushInstance> sceneBrushFallback)
        {
            var uniqueBasePaths = new HashSet<string>(StringComparer.Ordinal);

            CollectBrushPathsFromBrushList(brushes, uniqueBasePaths);
            CollectPathsFromSceneFallback(
                sceneBrushFallback,
                uniqueBasePaths,
                static instance => instance.VirtualPath);

            return BuildPlanForPaths(
                uniqueBasePaths,
                FcLevelGeometrySourceKind.Brush,
                FcLevelGeometrySourceKind.BrushLod);
        }

        void CollectVegetationPathsFromSupplement(
            FcLevelSupplementData supplement,
            HashSet<string> uniquePaths)
        {
            if (supplement?.VegetationTypes == null || supplement.VegetationTypes.Length == 0 ||
                supplement.VegetationInstances == null || supplement.VegetationInstances.Length == 0)
                return;

            var typePathByIndex = new Dictionary<int, string>();
            for (int i = 0; i < supplement.VegetationTypes.Length; i++)
            {
                var type = supplement.VegetationTypes[i];
                if (type.Index < 0 || string.IsNullOrWhiteSpace(type.FileName))
                    continue;

                string normalizedPath = ImportAssetPaths.NormalizeVirtualPath(type.FileName);
                typePathByIndex[type.Index] = normalizedPath;
            }

            for (int i = 0; i < supplement.VegetationInstances.Length; i++)
            {
                int typeIndex = supplement.VegetationInstances[i].Type;
                if (!typePathByIndex.TryGetValue(typeIndex, out var path) || string.IsNullOrWhiteSpace(path))
                    continue;

                uniquePaths.Add(path);
            }
        }

        static void CollectPathsFromSceneFallback<TInstance>(
            IReadOnlyList<TInstance> sceneFallback,
            HashSet<string> uniquePaths,
            Func<TInstance, string> getVirtualPath)
            where TInstance : class
        {
            if (sceneFallback == null || sceneFallback.Count == 0 || getVirtualPath == null)
                return;

            for (int i = 0; i < sceneFallback.Count; i++)
            {
                var instance = sceneFallback[i];
                if (instance == null)
                    continue;

                var virtualPath = getVirtualPath(instance);
                if (string.IsNullOrWhiteSpace(virtualPath))
                    continue;

                uniquePaths.Add(ImportAssetPaths.NormalizeVirtualPath(virtualPath));
            }
        }

        static void CollectBrushPathsFromBrushList(
            IReadOnlyList<FcBrushDesc> brushes,
            HashSet<string> uniquePaths)
        {
            if (brushes == null || brushes.Count == 0)
                return;

            for (int i = 0; i < brushes.Count; i++)
            {
                var brush = brushes[i];
                if (brush == null || string.IsNullOrWhiteSpace(brush.VirtualPath))
                    continue;

                uniquePaths.Add(ImportAssetPaths.NormalizeVirtualPath(brush.VirtualPath));
            }
        }

        FcLevelGeometryPreloadPlan BuildPlanForPaths(
            IEnumerable<string> basePaths,
            FcLevelGeometrySourceKind baseSourceKind,
            FcLevelGeometrySourceKind lodSourceKind)
        {
            var plan = new FcLevelGeometryPreloadPlan();
            if (basePaths == null)
                return plan;

            foreach (string basePath in basePaths)
            {
                if (string.IsNullOrWhiteSpace(basePath))
                    continue;

                var baseRequest = CreateGeometryRequest(basePath, baseSourceKind);
                plan.AddBaseRequest(baseRequest);

                var lodPaths = _findLodPaths(basePath);
                if (lodPaths == null || lodPaths.Count == 0)
                    continue;

                for (int i = 0; i < lodPaths.Count; i++)
                {
                    if (string.IsNullOrWhiteSpace(lodPaths[i]))
                        continue;

                    var lodRequest = CreateGeometryRequest(lodPaths[i], lodSourceKind);
                    plan.AddLodRequest(baseRequest, lodRequest);
                }
            }

            return plan;
        }

        static FcLevelGeometryRequest CreateGeometryRequest(
            string virtualPath,
            FcLevelGeometrySourceKind sourceKind)
        {
            return new FcLevelGeometryRequest(
                virtualPath: virtualPath,
                importScale: 0.01f,
                importSkeleton: false,
                selectedMeshChunkId: -1,
                sourceKind: sourceKind);
        }

        static void CollectRawPaths(IReadOnlyList<string> paths, HashSet<string> uniquePaths)
        {
            if (paths == null || paths.Count == 0)
                return;

            for (int i = 0; i < paths.Count; i++)
            {
                var vp = paths[i];
                if (string.IsNullOrWhiteSpace(vp))
                    continue;

                try { uniquePaths.Add(ImportAssetPaths.NormalizeVirtualPath(vp)); }
                catch { /* ignore invalid path */ }
            }
        }

        IReadOnlyList<string> FindLodPathsFromService(string basePath)
        {
            return _lodService.FindSiblingLodPaths(basePath);
        }
    }
}
