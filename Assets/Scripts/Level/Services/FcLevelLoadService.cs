using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using Cysharp.Threading.Tasks;
using OpenFarCry.Importer;
using OpenFarCry.Importer.Cgf;
using OpenFarCry.Level.Data;
using OpenFarCry.Level.Entities;
using UnityEngine;

namespace OpenFarCry.Level.Services
{
    // Runtime level load orchestrator facade.
    // Phase-1/2 version: mission+brush source loading and entity queue kickoff.
    [DefaultExecutionOrder(-96)]
    public sealed class FcLevelLoadService : MonoBehaviour
    {
        public static FcLevelLoadService Current { get; private set; }

        [SerializeField] string _loadedLevelName;
        [SerializeField] string _loadedMissionName;

        FcLevelCacheService _cacheService;
        FcEntityLoadService _entityLoadService;
        FcLevelLoadReport _report;
        readonly Dictionary<string, FcLevelGeometryAssetHandle> _brushPreloadedByPath =
            new Dictionary<string, FcLevelGeometryAssetHandle>(StringComparer.Ordinal);
        readonly Dictionary<string, FcLevelGeometryAssetHandle> _vegetationPreloadedByPath =
            new Dictionary<string, FcLevelGeometryAssetHandle>(StringComparer.Ordinal);

        public string LoadedLevelName => _loadedLevelName;
        public string LoadedMissionName => _loadedMissionName;

        void Awake()
        {
            Current = this;
            _cacheService = GetComponent<FcLevelCacheService>() ?? GetComponentInParent<FcLevelCacheService>();
            _entityLoadService = GetComponent<FcEntityLoadService>() ?? GetComponentInParent<FcEntityLoadService>();
            _report = FcLevelRuntimeReportRegistry.GetOrCreate(GetScopeId());
        }

        void OnDestroy()
        {
            if (Current == this) Current = null;
        }

        public FcLevelLoadReport GetLoadReport()
        {
            if (_report == null)
                _report = FcLevelRuntimeReportRegistry.GetOrCreate(GetScopeId());
            return _report;
        }

        public async UniTask<FcLevelLoadReport> LoadLevelAsync(
            string levelName,
            string missionName,
            CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(levelName))
                throw new ArgumentException("Level name is null or empty.", nameof(levelName));
            if (string.IsNullOrWhiteSpace(missionName))
                throw new ArgumentException("Mission name is null or empty.", nameof(missionName));

            _loadedLevelName = levelName;
            _loadedMissionName = missionName;

            if (_cacheService != null && string.IsNullOrWhiteSpace(_cacheService.LevelScopeId))
                _cacheService.SetLevelScope(levelName);

            _report = FcLevelRuntimeReportRegistry.GetOrCreate(GetScopeId());
            _report.LevelName = levelName;
            _report.MissionName = missionName;

            var sw = Stopwatch.StartNew();
            var missionSw = Stopwatch.StartNew();
            ct.ThrowIfCancellationRequested();
            var mission = FcLevelLoader.LoadMission(levelName, missionName);
            missionSw.Stop();
            _report.RecordPhase("RuntimeLoadMissionXml", missionSw.Elapsed.TotalMilliseconds);

            var brushSw = Stopwatch.StartNew();
            ct.ThrowIfCancellationRequested();
            var brushes = FcBrushLoader.LoadBrushes(levelName);
            brushSw.Stop();
            _report.RecordPhase("RuntimeLoadBrushList", brushSw.Elapsed.TotalMilliseconds);

            var supplementSw = Stopwatch.StartNew();
            ct.ThrowIfCancellationRequested();
            var supplement = FcLevelSupplementLoader.Load(levelName);
            supplementSw.Stop();
            _report.RecordPhase("RuntimeLoadSupplement", supplementSw.Elapsed.TotalMilliseconds);

            var planner = new FcLevelGeometryPreloadPlanner();
            var brushPlanSw = Stopwatch.StartNew();
            ct.ThrowIfCancellationRequested();
            var brushSceneFallback = FindObjectsByType<FcBrushInstance>(FindObjectsSortMode.None);
            var brushPlan = planner.BuildBrushPlan(brushes, brushSceneFallback);
            brushPlanSw.Stop();
            _report.RecordPhase("RuntimeBuildBrushPreloadPlan", brushPlanSw.Elapsed.TotalMilliseconds);

            var vegetationPlanSw = Stopwatch.StartNew();
            ct.ThrowIfCancellationRequested();
            var vegetationSceneFallback = FindObjectsByType<FcVegetationInstance>(FindObjectsSortMode.None);
            var vegetationPlan = planner.BuildVegetationPlan(supplement, vegetationSceneFallback);
            vegetationPlanSw.Stop();
            _report.RecordPhase("RuntimeBuildVegetationPreloadPlan", vegetationPlanSw.Elapsed.TotalMilliseconds);

            _brushPreloadedByPath.Clear();
            _vegetationPreloadedByPath.Clear();

            int uniqueBaseRequestCount = CountUniqueModelKeys(
                brushPlan.UniqueBaseRequests,
                vegetationPlan.UniqueBaseRequests);
            int uniqueLodRequestCount = CountUniqueModelKeys(
                brushPlan.UniqueLodRequests,
                vegetationPlan.UniqueLodRequests);
            int cachedModelReuseCount = 0;
            int uniqueTexturePreloadCount = 0;

            if (brushPlan.UniqueRequestCount > 0 || vegetationPlan.UniqueRequestCount > 0)
            {
                var requestBuildSw = Stopwatch.StartNew();
                var orderedUniqueRequests = new List<FcLevelGeometryRequest>(
                    brushPlan.UniqueAllRequests.Count + vegetationPlan.UniqueAllRequests.Count);
                var seenModelKeys = new HashSet<string>(StringComparer.Ordinal);
                CollectUniqueGeometryRequests(brushPlan.UniqueAllRequests, seenModelKeys, orderedUniqueRequests);
                CollectUniqueGeometryRequests(vegetationPlan.UniqueAllRequests, seenModelKeys, orderedUniqueRequests);

                var importRequests = new List<CgfRuntimeImportRequest>(orderedUniqueRequests.Count);
                for (int i = 0; i < orderedUniqueRequests.Count; i++)
                    importRequests.Add(orderedUniqueRequests[i].ToRuntimeImportRequest(useRuntimeMemoryCache: true));
                requestBuildSw.Stop();
                _report.RecordPhase("RuntimeBuildGeometryPreloadRequests", requestBuildSw.Elapsed.TotalMilliseconds);

                var preloadGeometrySw = Stopwatch.StartNew();
                ct.ThrowIfCancellationRequested();
                var preloadResultsByModelKey = await CgfRuntimeImporter.PreloadAsync(importRequests, GetScopeId(), ct);
                preloadGeometrySw.Stop();
                _report.RecordPhase("RuntimePreloadGeometry", preloadGeometrySw.Elapsed.TotalMilliseconds);

                var preloadTextureSw = Stopwatch.StartNew();
                ct.ThrowIfCancellationRequested();
                var successfulResults = new List<CgfRuntimeImportResult>(preloadResultsByModelKey.Count);
                for (int i = 0; i < orderedUniqueRequests.Count; i++)
                {
                    var req = orderedUniqueRequests[i];
                    if (!preloadResultsByModelKey.TryGetValue(req.ModelCacheKey, out var result))
                        continue;
                    if (result == null || !result.Success)
                        continue;
                    if (result.UsedModelRuntimeMemoryCache)
                        cachedModelReuseCount++;

                    successfulResults.Add(result);
                }

                uniqueTexturePreloadCount =
                    CgfRuntimeImporter.MaterialService.CountUniqueTexturePreloadRequests(successfulResults);

                await CgfRuntimeImporter.MaterialService.PreloadTexturesForResultsAsync(
                    successfulResults,
                    GetScopeId(),
                    ct);
                preloadTextureSw.Stop();
                _report.RecordPhase("RuntimePreloadGeometryTextures", preloadTextureSw.Elapsed.TotalMilliseconds);

                var buildBrushHandlesSw = Stopwatch.StartNew();
                var brushHandles = brushPlan.BuildBrushHandles(preloadResultsByModelKey);
                foreach (var kv in brushHandles)
                    _brushPreloadedByPath[kv.Key] = kv.Value;
                buildBrushHandlesSw.Stop();
                _report.RecordPhase("RuntimeBuildBrushAssetHandles", buildBrushHandlesSw.Elapsed.TotalMilliseconds);

                var buildVegetationHandlesSw = Stopwatch.StartNew();
                var vegetationHandles = vegetationPlan.BuildVegetationHandles(preloadResultsByModelKey);
                foreach (var kv in vegetationHandles)
                    _vegetationPreloadedByPath[kv.Key] = kv.Value;
                buildVegetationHandlesSw.Stop();
                _report.RecordPhase("RuntimeBuildVegetationAssetHandles", buildVegetationHandlesSw.Elapsed.TotalMilliseconds);
            }

            _report.RecordGeometryPreloadStats(
                uniqueBaseRequestCount: uniqueBaseRequestCount,
                uniqueLodRequestCount: uniqueLodRequestCount,
                cachedModelReuseCount: cachedModelReuseCount,
                uniqueTexturePreloadCount: uniqueTexturePreloadCount);

            _report.RecordPhase("RuntimeTotalKickoff", sw.Elapsed.TotalMilliseconds);

            // Keep mission/brush data hot in this phase (orchestrator) and start mesh/character queue.
            _ = mission;
            _ = brushes;
            _ = supplement;
            EnqueueSceneEntitiesForCurrentScope();

            return _report;
        }

        public async UniTask UnloadLevelAsync(int drainTimeoutMs = 2000)
        {
            string scopeId = GetScopeId();
            if (!string.IsNullOrWhiteSpace(scopeId))
            {
                // Cancel queued (not yet active) loads so they don't start after scope release.
                _entityLoadService?.CancelQueuedForScope(scopeId);

                // Wait for in-flight loads to complete before releasing assets.
                if (_entityLoadService != null)
                    await _entityLoadService.WaitForIdleAsync(drainTimeoutMs);

                // Cancel any remaining active loads (timeout path or no drain service).
                _entityLoadService?.CancelAllForScope(scopeId);

                CgfRuntimeImporter.ReleaseLevelScope(scopeId);
                CgfRuntimeImporter.TrimUnused();
                FcLevelRuntimeReportRegistry.Release(scopeId);
            }

            _brushPreloadedByPath.Clear();
            _vegetationPreloadedByPath.Clear();
            _report = FcLevelRuntimeReportRegistry.GetOrCreate(scopeId);
            _loadedLevelName = string.Empty;
            _loadedMissionName = string.Empty;
        }

        public bool TryGetBrushPreloadedHandle(string virtualPath, out FcLevelGeometryAssetHandle handle)
        {
            return TryGetPreloadedHandle(_brushPreloadedByPath, virtualPath, out handle);
        }

        public bool TryGetVegetationPreloadedHandle(string virtualPath, out FcLevelGeometryAssetHandle handle)
        {
            return TryGetPreloadedHandle(_vegetationPreloadedByPath, virtualPath, out handle);
        }

        static bool TryGetPreloadedHandle(
            IReadOnlyDictionary<string, FcLevelGeometryAssetHandle> handlesByPath,
            string virtualPath,
            out FcLevelGeometryAssetHandle handle)
        {
            handle = null;
            if (string.IsNullOrWhiteSpace(virtualPath))
                return false;

            string normalizedPath;
            try
            {
                normalizedPath = ImportAssetPaths.NormalizeVirtualPath(virtualPath);
            }
            catch
            {
                return false;
            }

            return handlesByPath.TryGetValue(normalizedPath, out handle) && handle != null && handle.IsValid;
        }

        static void CollectUniqueGeometryRequests(
            IReadOnlyList<FcLevelGeometryRequest> source,
            HashSet<string> seenModelKeys,
            List<FcLevelGeometryRequest> destination)
        {
            if (source == null || source.Count == 0)
                return;

            for (int i = 0; i < source.Count; i++)
            {
                var request = source[i];
                if (request == null || string.IsNullOrWhiteSpace(request.ModelCacheKey))
                    continue;
                if (!seenModelKeys.Add(request.ModelCacheKey))
                    continue;

                destination.Add(request);
            }
        }

        static int CountUniqueModelKeys(
            IReadOnlyList<FcLevelGeometryRequest> a,
            IReadOnlyList<FcLevelGeometryRequest> b)
        {
            var keys = new HashSet<string>(StringComparer.Ordinal);
            AddModelKeys(a, keys);
            AddModelKeys(b, keys);
            return keys.Count;
        }

        static void AddModelKeys(IReadOnlyList<FcLevelGeometryRequest> source, HashSet<string> keys)
        {
            if (source == null || source.Count == 0)
                return;

            for (int i = 0; i < source.Count; i++)
            {
                var request = source[i];
                if (request == null || string.IsNullOrWhiteSpace(request.ModelCacheKey))
                    continue;

                keys.Add(request.ModelCacheKey);
            }
        }

        void EnqueueSceneEntitiesForCurrentScope()
        {
            if (_entityLoadService == null)
                return;

            string scopeId = GetScopeId();
            var entities = FindObjectsByType<FcMeshEntity>(FindObjectsSortMode.None);
            if (entities == null || entities.Length == 0)
                return;

            var toEnqueue = new List<FcMeshEntity>(entities.Length);
            for (int i = 0; i < entities.Length; i++)
            {
                var entity = entities[i];
                if (entity == null)
                    continue;

                string entityScope = entity.GetLevelScopeId();
                if (!string.Equals(entityScope, scopeId, StringComparison.Ordinal))
                    continue;
                if (string.IsNullOrWhiteSpace(entity.VirtualPath))
                    continue;

                toEnqueue.Add(entity);
            }

            for (int i = 0; i < toEnqueue.Count; i++)
            {
                var entity = toEnqueue[i];
                _entityLoadService.Enqueue(entity, entity.GetDefaultLoadPriority());
            }
        }

        string GetScopeId()
        {
            return _cacheService != null ? _cacheService.LevelScopeId : string.Empty;
        }
    }
}
