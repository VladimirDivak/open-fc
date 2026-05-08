using System;
using System.Collections.Generic;
using Stopwatch = System.Diagnostics.Stopwatch;
using UnityEngine;

namespace OpenFarCry.Importer.Cgf
{
    public sealed class CgfRuntimeLoadSmokeTest : MonoBehaviour
    {
        [Header("Input")]
        [SerializeField] string[] _virtualPaths = Array.Empty<string>();
        [SerializeField] bool _runOnStart = false;
        [SerializeField] bool _clearPreviousBeforeRun = true;

        [Header("Import Options")]
        [SerializeField] bool _importSkeleton = true;
        [SerializeField] bool _importAnimations = true;
        [SerializeField] bool _importPhysicsBoxColliders = false;
        [SerializeField] bool _importRagdollBodies = false;
        [SerializeField] float _importScale = 0.01f;
        [SerializeField] bool _configureLods = true;

        [Header("Layout")]
        [SerializeField] Vector3 _spawnOrigin = Vector3.zero;
        [SerializeField] Vector3 _spawnStep = new Vector3(3f, 0f, 0f);

        [Header("Cache")]
        [SerializeField] string _levelScopeId = "runtime_smoke_level";
        [SerializeField] bool _useRuntimeMemoryCache = true;
        [SerializeField] bool _clearAnimationRuntimeCacheBeforeRun = false;

        [Header("Timing")]
        [SerializeField] bool _logPerAssetTiming = true;

        [Header("Animation Diagnostics")]
        [SerializeField] bool _clearAnimationAttachDiagnosticsBeforeRun = true;
        [SerializeField] bool _logAnimationCompatibilityDiagnostics = true;
        [SerializeField] int _maxAnimationDiagnosticsInReport = 64;

        readonly List<GameObject> _spawnedRoots = new List<GameObject>();
        readonly List<CgfRuntimeImportResult> _importResults = new List<CgfRuntimeImportResult>();

        readonly CgfGameObjectBuilder _gameObjectBuilder = new CgfGameObjectBuilder();
        readonly CgfAnimationRuntimeImportService _animationImportService = new CgfAnimationRuntimeImportService();
        readonly CgfLodImportService _lodImportService = new CgfLodImportService();
        readonly CgfRagdollBuilder _ragdollBuilder = new CgfRagdollBuilder();

        void Start()
        {
            if (_runOnStart)
                RunSmokeTest();
        }

        [ContextMenu("Run Smoke Test")]
        public void RunSmokeTest()
        {
            if (_clearPreviousBeforeRun)
                ClearSpawnedObjects();

            int successCount = 0;
            int failCount = 0;
            int cacheHitCount = 0;

            double importMsSum = 0d;
            double buildMsSum = 0d;
            double physicsMsSum = 0d;
            double animationMsSum = 0d;
            double lodMsSum = 0d;
            double totalMsSum = 0d;
            double totalMsMin = double.MaxValue;
            double totalMsMax = 0d;

            var runTimer = Stopwatch.StartNew();

            if (_clearAnimationRuntimeCacheBeforeRun)
                CgfAnimationRuntimeImportService.ClearRuntimeCache();
            else if (_clearAnimationAttachDiagnosticsBeforeRun)
                CgfAnimationRuntimeImportService.ClearRuntimeAttachDiagnostics();

            bool prevCompatibilityLogging = CgfAnimationRuntimeImportService.EnableCompatibilityDiagnosticsLogging;
            CgfAnimationRuntimeImportService.EnableCompatibilityDiagnosticsLogging = _logAnimationCompatibilityDiagnostics;

            try
            {
                var before = CgfRuntimeImporter.GetCacheStats();
                var animationCacheBefore = CgfAnimationRuntimeImportService.GetRuntimeCacheStats();
                Debug.Log(
                    $"[CgfRuntimeSmoke] Start. Cache before: parsed={before.ParsedEntryCount}, models={before.ModelEntryCount}, " +
                    $"parsedRefs={before.ParsedTotalRefCount}, modelRefs={before.ModelTotalRefCount}. " +
                    $"AnimCache before: caf={animationCacheBefore.CafEntryCount}, cafPath={animationCacheBefore.CafPathEntryCount}, clips={animationCacheBefore.ClipEntryCount}, " +
                    $"cafHit/miss={animationCacheBefore.CafHitCount}/{animationCacheBefore.CafMissCount}, " +
                    $"cafPathHit/miss={animationCacheBefore.CafPathHitCount}/{animationCacheBefore.CafPathMissCount}, " +
                    $"cafSemanticHit/miss={animationCacheBefore.CafSemanticHitCount}/{animationCacheBefore.CafSemanticMissCount}, " +
                    $"clipHit/miss={animationCacheBefore.ClipHitCount}/{animationCacheBefore.ClipMissCount}, " +
                    $"setEntries={animationCacheBefore.AnimationSetEntryCount}, setHit/miss={animationCacheBefore.AnimationSetHitCount}/{animationCacheBefore.AnimationSetMissCount}, " +
                    $"semClipEntries={animationCacheBefore.SemanticClipEntryCount}, semClipHit/miss={animationCacheBefore.SemanticClipHitCount}/{animationCacheBefore.SemanticClipMissCount}.");

                int pathCount = _virtualPaths != null ? _virtualPaths.Length : 0;
                for (int i = 0; i < pathCount; i++)
                {
                    string path = _virtualPaths[i];
                    if (string.IsNullOrWhiteSpace(path))
                        continue;

                    var assetTimer = Stopwatch.StartNew();
                    var stageTimer = Stopwatch.StartNew();

                    var request = new CgfRuntimeImportRequest(
                        virtualPath: path,
                        selectedMeshChunkId: -1,
                        importSkeleton: _importSkeleton,
                        importAnimations: _importAnimations,
                        importPhysicsBoxColliders: _importPhysicsBoxColliders,
                        importRagdollBodies: _importRagdollBodies,
                        importScale: _importScale,
                        useRuntimeMemoryCache: _useRuntimeMemoryCache,
                        preferProjectCache: false);

                    var result = CgfRuntimeImporter.Import(request, _levelScopeId);
                    double importMs = stageTimer.Elapsed.TotalMilliseconds;
                    _importResults.Add(result);
                    if (result != null && result.UsedRuntimeMemoryCache)
                        cacheHitCount++;

                    if (!result.Success || result.BuildResult?.Mesh == null || result.ParsedFile == null)
                    {
                        failCount++;
                        Debug.LogError(
                            $"[CgfRuntimeSmoke] Import failed for '{path}' in {importMs:F1} ms: " +
                            $"{result?.ErrorMessage ?? "Unknown error"}");
                        continue;
                    }

                    stageTimer.Restart();
                    string name = System.IO.Path.GetFileNameWithoutExtension(path);
                    var built = _gameObjectBuilder.Build(new CgfGameObjectBuilder.BuildRequest(
                        result.BuildResult,
                        result.ParsedFile,
                        rigDefinition: null,
                        name: name,
                        materialService: CgfRuntimeImporter.MaterialService));
                    double buildMs = stageTimer.Elapsed.TotalMilliseconds;

                    var root = built.Root;
                    root.transform.position = _spawnOrigin + _spawnStep * successCount;
                    root.transform.rotation = Quaternion.identity;
                    root.transform.localScale = Vector3.one;
                    _spawnedRoots.Add(root);

                    stageTimer.Restart();
                    if (result.BuildResult.HasSkeleton && built.BoneTransforms != null)
                    {
                        if (_importPhysicsBoxColliders)
                            _ragdollBuilder.AddBonePhysicsBoxColliders(result.ParsedFile, result.BuildResult, built.BoneTransforms, _importScale);

                        if (_importPhysicsBoxColliders && _importRagdollBodies)
                        {
                            var ragdoll = _ragdollBuilder.AddRagdollBodiesAndJoints(root, built.BoneTransforms, result.ParsedFile, result.BuildResult);
                            CgfRagdollDiagnostics.LogImportDiagnostics(root, built.BoneTransforms, result.BuildResult, ragdoll.PhysicsByRuntimeIndex);
                            if (ragdoll.JointDiagnostics != null && ragdoll.JointDiagnostics.Count > 0)
                                Debug.Log("[CgfRuntimeSmoke][JointDiag]\n" + string.Join("\n", ragdoll.JointDiagnostics));
                        }
                    }
                    double physicsMs = stageTimer.Elapsed.TotalMilliseconds;

                    stageTimer.Restart();
                    if (_importAnimations)
                    {
                        _animationImportService.TryAttachAnimations(
                            root,
                            result.ParsedFile,
                            rigDefinition: null,
                            modelVirtualPath: result.VirtualPath,
                            importScale: _importScale,
                            out var animationWarning);
                        if (!string.IsNullOrEmpty(animationWarning))
                            Debug.LogWarning($"[CgfRuntimeSmoke] '{path}': {animationWarning}");
                    }
                    double animationMs = stageTimer.Elapsed.TotalMilliseconds;

                    stageTimer.Restart();
                    if (_configureLods)
                    {
                        var siblingLods = _lodImportService.FindSiblingLodPaths(result.VirtualPath);
                        _lodImportService.ConfigureLodGroup(
                            root,
                            hasSkeleton: result.BuildResult.HasSkeleton,
                            importScale: _importScale,
                            siblingLodPaths: siblingLods,
                            materialService: CgfRuntimeImporter.MaterialService);
                    }
                    double lodMs = stageTimer.Elapsed.TotalMilliseconds;

                    double totalMs = assetTimer.Elapsed.TotalMilliseconds;
                    importMsSum += importMs;
                    buildMsSum += buildMs;
                    physicsMsSum += physicsMs;
                    animationMsSum += animationMs;
                    lodMsSum += lodMs;
                    totalMsSum += totalMs;
                    if (totalMs < totalMsMin)
                        totalMsMin = totalMs;
                    if (totalMs > totalMsMax)
                        totalMsMax = totalMs;

                    if (_logPerAssetTiming)
                    {
                        Debug.Log(
                            $"[CgfRuntimeSmoke][Timing] '{path}': total={totalMs:F1} ms " +
                            $"(import={importMs:F1}, build={buildMs:F1}, physics={physicsMs:F1}, anim={animationMs:F1}, lod={lodMs:F1}), " +
                            $"cacheHit={(result.UsedRuntimeMemoryCache ? 1 : 0)}");
                    }

                    successCount++;
                }

                runTimer.Stop();
                var after = CgfRuntimeImporter.GetCacheStats();
                var animationCacheAfter = CgfAnimationRuntimeImportService.GetRuntimeCacheStats();
                double avgTotalMs = successCount > 0 ? totalMsSum / successCount : 0d;
                if (totalMsMin == double.MaxValue)
                    totalMsMin = 0d;
                Debug.Log(
                    $"[CgfRuntimeSmoke] Done. success={successCount}, fail={failCount}. " +
                    $"Wall={runTimer.Elapsed.TotalMilliseconds:F1} ms, " +
                    $"avgTotal={avgTotalMs:F1} ms, minTotal={totalMsMin:F1} ms, maxTotal={totalMsMax:F1} ms, " +
                    $"sum(import/build/physics/anim/lod)=({importMsSum:F1}/{buildMsSum:F1}/{physicsMsSum:F1}/{animationMsSum:F1}/{lodMsSum:F1}) ms, " +
                    $"cacheHits={cacheHitCount}. " +
                    $"AnimCache after: caf={animationCacheAfter.CafEntryCount}, cafPath={animationCacheAfter.CafPathEntryCount}, clips={animationCacheAfter.ClipEntryCount}, " +
                    $"cafHit/miss={animationCacheAfter.CafHitCount}/{animationCacheAfter.CafMissCount}, " +
                    $"cafPathHit/miss={animationCacheAfter.CafPathHitCount}/{animationCacheAfter.CafPathMissCount}, " +
                    $"cafSemanticHit/miss={animationCacheAfter.CafSemanticHitCount}/{animationCacheAfter.CafSemanticMissCount}, " +
                    $"clipHit/miss={animationCacheAfter.ClipHitCount}/{animationCacheAfter.ClipMissCount}, " +
                    $"setEntries={animationCacheAfter.AnimationSetEntryCount}, setHit/miss={animationCacheAfter.AnimationSetHitCount}/{animationCacheAfter.AnimationSetMissCount}. " +
                    $"semClipEntries={animationCacheAfter.SemanticClipEntryCount}, semClipHit/miss={animationCacheAfter.SemanticClipHitCount}/{animationCacheAfter.SemanticClipMissCount}. " +
                    $"Cache after: parsed={after.ParsedEntryCount}, models={after.ModelEntryCount}, " +
                    $"parsedRefs={after.ParsedTotalRefCount}, modelRefs={after.ModelTotalRefCount}.");

                if (_logAnimationCompatibilityDiagnostics)
                {
                    Debug.Log(
                        CgfAnimationRuntimeImportService.BuildRuntimeAttachDiagnosticsReport(
                            maxEntries: Mathf.Max(1, _maxAnimationDiagnosticsInReport)));
                }
            }
            finally
            {
                CgfAnimationRuntimeImportService.EnableCompatibilityDiagnosticsLogging = prevCompatibilityLogging;
            }
        }

        [ContextMenu("Clear Spawned Objects")]
        public void ClearSpawnedObjects()
        {
            for (int i = _spawnedRoots.Count - 1; i >= 0; i--)
            {
                var root = _spawnedRoots[i];
                if (root == null)
                    continue;

                if (Application.isPlaying)
                    Destroy(root);
                else
                    DestroyImmediate(root);
            }

            _spawnedRoots.Clear();
        }

        [ContextMenu("Release Runtime Scope")]
        public void ReleaseRuntimeScope()
        {
            for (int i = 0; i < _importResults.Count; i++)
                CgfRuntimeImporter.Release(_importResults[i]);
            _importResults.Clear();

            CgfRuntimeImporter.ReleaseLevelScope(_levelScopeId);
            int removed = CgfRuntimeImporter.TrimUnused();
            var stats = CgfRuntimeImporter.GetCacheStats();
            Debug.Log(
                $"[CgfRuntimeSmoke] Released scope '{_levelScopeId}', trimmed={removed}. " +
                $"Cache now: parsed={stats.ParsedEntryCount}, models={stats.ModelEntryCount}, " +
                $"parsedRefs={stats.ParsedTotalRefCount}, modelRefs={stats.ModelTotalRefCount}.");
        }

        void OnDestroy()
        {
            if (!Application.isPlaying)
                return;

            ReleaseRuntimeScope();
        }
    }
}
