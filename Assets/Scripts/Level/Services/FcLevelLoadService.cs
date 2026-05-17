using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Threading;
using Cysharp.Threading.Tasks;
using OpenFarCry.Importer;
using OpenFarCry.Importer.Cgf;
using OpenFarCry.Level.Data;
using OpenFarCry.Level.Entities;
using UnityEngine;
using Debug = UnityEngine.Debug;

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
        FcLevelMaterialOverrideService _materialOverrideService;
        FcLevelLoadReport _report;
        readonly Dictionary<string, FcLevelGeometryAssetHandle> _brushPreloadedByPath =
            new Dictionary<string, FcLevelGeometryAssetHandle>(StringComparer.Ordinal);
        readonly Dictionary<string, FcLevelGeometryAssetHandle> _vegetationPreloadedByPath =
            new Dictionary<string, FcLevelGeometryAssetHandle>(StringComparer.Ordinal);

        public string LoadedLevelName => _loadedLevelName;
        public string LoadedMissionName => _loadedMissionName;

        // True while LoadLevelAsync is in progress (set at start, cleared when done or on unload).
        public bool IsLoadInProgress { get; private set; }
        // True after LoadLevelAsync has populated preloaded handles. Reset on UnloadLevelAsync.
        public bool IsPreloadComplete { get; private set; }

        void Awake()
        {
            Current = this;
            _cacheService = GetComponent<FcLevelCacheService>() ?? GetComponentInParent<FcLevelCacheService>();
            _entityLoadService = GetComponent<FcEntityLoadService>() ?? GetComponentInParent<FcEntityLoadService>();
            _materialOverrideService = GetComponent<FcLevelMaterialOverrideService>() ?? GetComponentInParent<FcLevelMaterialOverrideService>();
            _report = FcLevelRuntimeReportRegistry.GetOrCreate(GetScopeId());

            if (!string.IsNullOrWhiteSpace(_loadedLevelName))
                FcLevelLoader.EnsureLevelMounted(_loadedLevelName);
        }

        void Start()
        {
            if (!string.IsNullOrWhiteSpace(_loadedLevelName) && _materialOverrideService != null)
            {
                var supplement = FcLevelSupplementLoader.Load(_loadedLevelName);
                _materialOverrideService.Configure(_loadedLevelName, supplement);
            }
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

            IsLoadInProgress = true;
            IsPreloadComplete = false;

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
            _materialOverrideService?.Configure(levelName, supplement);

            var planner = new FcLevelGeometryPreloadPlanner();
            var brushPlanSw = Stopwatch.StartNew();
            ct.ThrowIfCancellationRequested();
            var brushSceneFallback = FindObjectsByType<FcBrushInstance>(FindObjectsInactive.Exclude);
            var brushPlan = planner.BuildBrushPlan(brushes, brushSceneFallback);
            brushPlanSw.Stop();
            _report.RecordPhase("RuntimeBuildBrushPreloadPlan", brushPlanSw.Elapsed.TotalMilliseconds);

            var vegetationPlanSw = Stopwatch.StartNew();
            ct.ThrowIfCancellationRequested();
            var vegetationSceneFallback = FindObjectsByType<FcVegetationInstance>(FindObjectsInactive.Exclude);
            var terrainVegService = FindAnyObjectByType<FcVegetationTerrainService>();
            var terrainServicePaths = terrainVegService != null ? terrainVegService.GetVegetationVirtualPaths() : null;
            var vegetationPlan = planner.BuildVegetationPlan(supplement, vegetationSceneFallback, terrainServicePaths);
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

            IsPreloadComplete = true;
            IsLoadInProgress = false;
            _report.RecordPhase("RuntimeTotalKickoff", sw.Elapsed.TotalMilliseconds);

            var spawnBrushSw = Stopwatch.StartNew();
            int spawnedBrushCount = SpawnBrushesFromList(brushes);
            spawnBrushSw.Stop();
            _report.RecordPhase("RuntimeSpawnBrushes", spawnBrushSw.Elapsed.TotalMilliseconds);

            var spawnVegSw = Stopwatch.StartNew();
            int spawnedVegCount = SpawnVegetationFromSupplement(supplement);
            spawnVegSw.Stop();
            _report.RecordPhase("RuntimeSpawnVegetation", spawnVegSw.Elapsed.TotalMilliseconds);

            var spawnEntitySw = Stopwatch.StartNew();
            (int spawnedEntityCount, int spawnedEntityMeshCount) = SpawnEntitiesFromMission(mission);
            spawnEntitySw.Stop();
            _report.RecordPhase("RuntimeSpawnEntities", spawnEntitySw.Elapsed.TotalMilliseconds);

            EnqueueSceneEntitiesForCurrentScope();

            _report.SpawnedBrushes    = spawnedBrushCount;
            _report.SpawnedVegetation = spawnedVegCount;
            _report.SpawnedEntities   = spawnedEntityCount;
            _report.SpawnedEntitiesMesh = spawnedEntityMeshCount;
            return _report;
        }

        public async UniTask UnloadLevelAsync(int drainTimeoutMs = 2000)
        {
            string scopeId = GetScopeId();
            if (!string.IsNullOrWhiteSpace(scopeId))
            {
                // Cancel queued (not yet active) loads so they don't start after scope release.
                if (_entityLoadService != null)
                    _entityLoadService.CancelQueuedForScope(scopeId);

                // Wait for in-flight loads to complete before releasing assets.
                if (_entityLoadService != null)
                    await _entityLoadService.WaitForIdleAsync(drainTimeoutMs);

                // Cancel any remaining active loads (timeout path or no drain service).
                if (_entityLoadService != null)
                    _entityLoadService.CancelAllForScope(scopeId);

                CgfRuntimeImporter.ReleaseLevelScope(scopeId);
                CgfRuntimeImporter.TrimUnused();
                FcLevelRuntimeReportRegistry.Release(scopeId);
            }

            IsLoadInProgress = false;
            IsPreloadComplete = false;
            _brushPreloadedByPath.Clear();
            _vegetationPreloadedByPath.Clear();
            if (_materialOverrideService != null)
                _materialOverrideService.Clear();
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

        // Spawns FcBrushInstance GOs from parsed brush list when none exist in scene yet
        // (pure runtime path without editor-pre-built scene).
        // Returns count of spawned instances (0 if editor scene already has brushes).
        int SpawnBrushesFromList(IReadOnlyList<FcBrushDesc> brushes)
        {
            if (brushes == null || brushes.Count == 0)
                return 0;

            // If editor-built scene has brush instances, skip runtime spawn to avoid duplicates.
            var existing = FindObjectsByType<FcBrushInstance>(FindObjectsInactive.Exclude);
            if (existing != null && existing.Length > 0)
            {
                Debug.Log($"[FcLevelLoadService] SpawnBrushes: skipped — {existing.Length} FcBrushInstance already in scene.");
                return 0;
            }

            var levelRoot = transform.parent != null ? transform.parent.gameObject : gameObject;
            var brushRoot = new GameObject("Brushes");
            brushRoot.transform.SetParent(levelRoot.transform, worldPositionStays: false);

            int spawned = 0;
            for (int i = 0; i < brushes.Count; i++)
            {
                var desc = brushes[i];
                if (string.IsNullOrWhiteSpace(desc.VirtualPath) || desc.Matrix == null || desc.Matrix.Length < 12)
                    continue;

                var go = new GameObject($"Brush_{desc.Id}");
                go.transform.SetParent(brushRoot.transform, worldPositionStays: false);
                FcLevelLoader.ApplyBrushMatrix34(go.transform, desc.Matrix);

                var bi = go.AddComponent<FcBrushInstance>();
                bi.Initialize(desc.VirtualPath, desc.NoPhysics, desc.MaterialOverride, desc.MaterialId);
                spawned++;
            }

            Debug.Log($"[FcLevelLoadService] SpawnBrushes: spawned={spawned} of {brushes.Count} from brush.lst.");
            return spawned;
        }

        // Spawns FcVegetationInstance GOs from supplement when none exist in scene yet.
        // Positions are resolved against active Terrain using normalized ushort coordinates.
        // Returns count of spawned instances (0 if editor scene already has vegetation).
        int SpawnVegetationFromSupplement(FcLevelSupplementData supplement)
        {
            if (supplement == null)
            {
                Debug.LogWarning("[FcLevelLoadService] SpawnVegetation: supplement is null.");
                return 0;
            }
            if (supplement.VegetationInstances == null || supplement.VegetationInstances.Length == 0)
            {
                int instanceCount = supplement.VegetationInstances != null ? supplement.VegetationInstances.Length : -1;
                Debug.Log($"[FcLevelLoadService] SpawnVegetation: no instances in supplement (VegetationInstances={instanceCount}).");
                return 0;
            }
            if (supplement.VegetationTypes == null || supplement.VegetationTypes.Length == 0)
            {
                Debug.Log($"[FcLevelLoadService] SpawnVegetation: no types in supplement.");
                return 0;
            }

            // If editor-built scene already handles vegetation (FcVegetationInstance or FcVegetationTerrainService), skip.
            var existingInstances = FindObjectsByType<FcVegetationInstance>(FindObjectsInactive.Exclude);
            if (existingInstances != null && existingInstances.Length > 0)
            {
                Debug.Log($"[FcLevelLoadService] SpawnVegetation: skipped — {existingInstances.Length} FcVegetationInstance already in scene.");
                return 0;
            }
            var existingService = FindAnyObjectByType<FcVegetationTerrainService>();
            if (existingService != null)
            {
                Debug.Log("[FcLevelLoadService] SpawnVegetation: skipped — FcVegetationTerrainService already in scene.");
                return 0;
            }

            var typeByIndex = new Dictionary<int, FcLevelSupplementData.VegetationTypeDesc>();
            for (int i = 0; i < supplement.VegetationTypes.Length; i++)
            {
                var t = supplement.VegetationTypes[i];
                if (t.Index >= 0 && !string.IsNullOrWhiteSpace(t.FileName))
                    typeByIndex[t.Index] = t;
            }
            if (typeByIndex.Count == 0)
            {
                Debug.Log("[FcLevelLoadService] SpawnVegetation: typeByIndex is empty after filtering.");
                return 0;
            }

            var terrain = FindAnyObjectByType<Terrain>();
            Vector3 terrainOrigin = terrain != null ? terrain.transform.position : Vector3.zero;
            float terrainSizeX = terrain != null && terrain.terrainData != null ? terrain.terrainData.size.x : 1f;
            float terrainSizeZ = terrain != null && terrain.terrainData != null ? terrain.terrainData.size.z : 1f;

            var levelRoot = transform.parent != null ? transform.parent.gameObject : gameObject;
            var vegRoot = new GameObject("Vegetation");
            vegRoot.transform.SetParent(levelRoot.transform, worldPositionStays: false);

            int spawned = 0;
            int skippedNoType = 0;
            var instances = supplement.VegetationInstances;
            for (int i = 0; i < instances.Length; i++)
            {
                var inst = instances[i];
                if (!typeByIndex.TryGetValue(inst.Type, out var typeDef))
                {
                    skippedNoType++;
                    continue;
                }

                float nx = inst.X / 65535f;
                float nz = inst.Y / 65535f;
                float wx = terrainOrigin.x + nx * terrainSizeX;
                float wz = terrainOrigin.z + nz * terrainSizeZ;
                float wy = terrain != null ? terrain.SampleHeight(new Vector3(wx, 0f, wz)) : 0f;
                float scale = inst.Scale > 0f ? inst.Scale : 1f;

                var go = new GameObject($"Vegetation_{i:D6}");
                go.transform.SetParent(vegRoot.transform, worldPositionStays: false);
                go.transform.position = new Vector3(wx, wy, wz);
                go.transform.localScale = new Vector3(scale, scale, scale);

                var vi = go.AddComponent<FcVegetationInstance>();
                vi.Initialize(typeDef.FileName, inst.Type, scale);
                spawned++;
            }

            Debug.Log($"[FcLevelLoadService] SpawnVegetation: spawned={spawned}, skipped_no_type={skippedNoType}, types={typeByIndex.Count}, total_instances={instances.Length}, terrain={(terrain != null ? "found" : "null")}");
            return spawned;
        }

        // Spawns FcEntityStub (and FcMeshEntity for model-bearing entities) GOs from
        // parsed mission when no editor-built entity GOs exist in scene.
        // Returns (totalSpawned, meshEntityCount).
        (int total, int mesh) SpawnEntitiesFromMission(FcMissionDesc mission)
        {
            if (mission == null || mission.Entities == null || mission.Entities.Count == 0)
                return (0, 0);

            var existing = FindObjectsByType<FcEntity>(FindObjectsInactive.Exclude);
            if (existing != null && existing.Length > 0)
            {
                Debug.Log($"[FcLevelLoadService] SpawnEntities: skipped — {existing.Length} FcEntity already in scene.");
                return (0, 0);
            }

            var levelRoot = transform.parent != null ? transform.parent.gameObject : gameObject;
            var entityRoot = new GameObject("Entities");
            entityRoot.transform.SetParent(levelRoot.transform, worldPositionStays: false);

            int total = 0, mesh = 0;
            foreach (var desc in mission.Entities)
            {
                if (string.IsNullOrEmpty(desc.EntityClass)) continue;

                if (desc.EntityClass.Equals("DynamicLight", StringComparison.Ordinal))
                {
                    SpawnRuntimeDynamicLight(desc, entityRoot);
                    continue;
                }

                // SoundSpot runtime spawn not implemented yet.
                if (desc.EntityClass.Equals("SoundSpot", StringComparison.Ordinal))
                    continue;

                string goName = string.IsNullOrEmpty(desc.Name)
                    ? $"{desc.EntityClass}_{desc.Id}"
                    : desc.Name;

                var go = new GameObject(goName);
                go.transform.SetParent(entityRoot.transform, worldPositionStays: false);
                FcLevelLoader.ApplyEntityTransform(go.transform, desc.Pos, desc.Angles, desc.Scale);

                if (desc.HiddenInGame)
                    go.SetActive(false);

                string modelPath = desc.GetModelVirtualPath();
                if (!string.IsNullOrEmpty(modelPath))
                {
                    var me = go.AddComponent<FcMeshEntity>();
                    me.Initialize(modelPath);
                    mesh++;
                }

                var stub = go.AddComponent<FcEntityStub>();
                stub.Initialize(desc);

                total++;
            }

            Debug.Log($"[FcLevelLoadService] SpawnEntities: spawned={total} (mesh={mesh}, lights={_report.SpawnedLights}) from mission.Entities={mission.Entities.Count}.");
            return (total, mesh);
        }

        void SpawnRuntimeDynamicLight(FcEntityDesc desc, GameObject entityRoot)
        {
            var p = desc.Properties;

            bool active = !p.TryGetValue("bActive",    out var act)  || act  == "1";
            bool isFake =  p.TryGetValue("bFakeLight", out var fake) && fake == "1";

            if (isFake) return;

            var go = new GameObject(string.IsNullOrEmpty(desc.Name) ? $"DynamicLight_{desc.Id}" : desc.Name);
            go.transform.SetParent(entityRoot.transform, worldPositionStays: false);
            FcLevelLoader.ApplyEntityTransform(go.transform, desc.Pos, desc.Angles, desc.Scale);
            go.SetActive(active);

            var light = go.AddComponent<Light>();

            bool hasProjectorTex = p.TryGetValue("texture_ProjectorTexture", out var tex) && !string.IsNullOrEmpty(tex);
            bool projectAll      = p.TryGetValue("bProjectInAllDirs", out var piad) && piad == "1";
            light.type = (hasProjectorTex && !projectAll) ? LightType.Spot : LightType.Point;

            if (p.TryGetValue("clrDiffuse", out var clrStr))
            {
                var parts = clrStr.Split(',');
                if (parts.Length >= 3
                    && float.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out float r)
                    && float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float g)
                    && float.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out float b))
                {
                    float mult = p.TryGetValue("DiffuseMultiplier", out var dm)
                        && float.TryParse(dm, NumberStyles.Float, CultureInfo.InvariantCulture, out float dmf) ? dmf : 1f;
                    light.color     = new Color(r, g, b);
                    light.intensity = mult;
                }
            }

            if (p.TryGetValue("OuterRadius", out var rad)
                && float.TryParse(rad, NumberStyles.Float, CultureInfo.InvariantCulture, out float radius))
                light.range = radius;

            if (light.type == LightType.Spot
                && p.TryGetValue("ProjectorFov", out var fov)
                && float.TryParse(fov, NumberStyles.Float, CultureInfo.InvariantCulture, out float fovF))
                light.spotAngle = fovF;

            bool castShadowMaps = desc.RootAttributes.TryGetValue("CastShadowMaps", out var csm) && csm == "1";
            light.shadows = (desc.CastShadows || castShadowMaps) ? LightShadows.Soft : LightShadows.None;

            _report.SpawnedLights++;
        }

        void EnqueueSceneEntitiesForCurrentScope()
        {
            if (_entityLoadService == null)
                return;

            string scopeId = GetScopeId();
            var entities = FindObjectsByType<FcMeshEntity>(FindObjectsInactive.Exclude);
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
