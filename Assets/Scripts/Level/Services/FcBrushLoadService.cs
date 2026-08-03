using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using Cysharp.Threading.Tasks;
using OpenFarCry.Importer.Cgf;
using OpenFarCry.Level.Entities;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace OpenFarCry.Level.Services
{
    // Centralized async loader for FcBrushInstance components.
    // Brushes register here in Start() instead of loading themselves.
    // Loads are sorted by camera distance and capped by _maxConcurrent/_loadsPerFrame.
    [DefaultExecutionOrder(-100)]
    public sealed class FcBrushLoadService : MonoBehaviour
    {
        public static FcBrushLoadService Current { get; private set; }

        [SerializeField] int _maxConcurrent = 8;
        [SerializeField] int _loadsPerFrame  = 8;
        [SerializeField] int _distanceSortMaxPending = 5000;

        readonly HashSet<FcBrushInstance> _pendingSet = new HashSet<FcBrushInstance>();
        readonly List<FcBrushInstance> _pending = new List<FcBrushInstance>();
        readonly CgfLodImportService _lodService = new CgfLodImportService();
        int _activeCount;
        int _totalRegistered;
        FcLevelLoadReport _report;

        public string LevelScopeId { get; private set; }

        void Awake()
        {
            Current = this;
            var cache = GetComponent<FcLevelCacheService>() ?? GetComponentInParent<FcLevelCacheService>();
            LevelScopeId = cache != null ? cache.LevelScopeId : string.Empty;
            _report = FcLevelRuntimeReportRegistry.GetOrCreate(LevelScopeId);
        }

        void OnDestroy()
        {
            if (Current == this) Current = null;
        }

        public void Register(FcBrushInstance brush)
        {
            if (brush != null && _pendingSet.Add(brush))
            {
                _pending.Add(brush);
                _totalRegistered++;
                _report.BrushesRegistered = _totalRegistered;
            }
        }

        public void Unregister(FcBrushInstance brush)
        {
            if (_pendingSet.Remove(brush))
                _pending.Remove(brush);
        }

        void Update()
        {
            if (_pending.Count == 0 || _activeCount >= _maxConcurrent)
                return;

            // Skip the sort once the queue is large — a full level's worth of brushes
            // registering in one burst turned this into an O(n log n) hitch on the very
            // frame everything else is also starting up. Distance ordering only matters
            // for the near-camera-first UX, not correctness.
            if (_pending.Count <= Mathf.Max(0, _distanceSortMaxPending))
                SortByDistance();

            int started = 0;
            for (int i = 0; i < _pending.Count && started < _loadsPerFrame && _activeCount < _maxConcurrent; )
            {
                var brush = _pending[i];
                if (brush == null)
                {
                    _pending.RemoveAt(i);
                    _pendingSet.Remove(brush);
                    continue;
                }
                _pending.RemoveAt(i);
                _pendingSet.Remove(brush);
                _activeCount++;
                LoadOneAsync(brush).Forget();
                started++;
            }
        }

        void SortByDistance()
        {
            var cam = Camera.main;
            if (cam == null) return;
            var camPos = cam.transform.position;
            _pending.Sort((a, b) =>
            {
                float da = a != null ? Vector3.SqrMagnitude(a.transform.position - camPos) : float.MaxValue;
                float db = b != null ? Vector3.SqrMagnitude(b.transform.position - camPos) : float.MaxValue;
                return da.CompareTo(db);
            });
        }

        async UniTaskVoid LoadOneAsync(FcBrushInstance brush)
        {
            var ct = brush.GetCancellationTokenOnDestroy();
            var sw = Stopwatch.StartNew();
            bool success = false;
            try
            {
                if (TryGetPreloadedHandle(brush, out var preloadedHandle))
                {
                    if (!string.IsNullOrWhiteSpace(brush.MaterialOverride) || brush.MaterialId >= 0)
                    {
                        var overrideSvc = FcLevelMaterialOverrideService.Current;
                        if (overrideSvc != null)
                            await overrideSvc.PreloadOverrideTexturesAsync(brush.MaterialOverride, brush.MaterialId, LevelScopeId, ct);
                    }
                    if (ct.IsCancellationRequested) return;
                    await UniTask.SwitchToMainThread(ct);
                    if (brush == null || ct.IsCancellationRequested) return;
                    brush.ApplyLoadResult(
                        preloadedHandle.BaseResult,
                        preloadedHandle.LodResults,
                        LevelScopeId,
                        releaseImportResultsOnDestroy: false);
                    success = true;
                    return;
                }

                bool skipPreload = brush.MaterialId < 0 &&
                    !string.IsNullOrWhiteSpace(brush.MaterialOverride) &&
                    (FcLevelMaterialOverrideService.Current?.HasResolvableOverride(
                        brush.MaterialOverride, brush.MaterialId) ?? false);

                var result = await FcLevelGeometryImportHelper.ImportStaticGeometryWithTexturePreloadAsync(
                    brush.VirtualPath,
                    LevelScopeId,
                    ct,
                    skipTexturePreload: skipPreload);

                if (ct.IsCancellationRequested) return;

                if (result == null || !result.Success)
                {
                    Debug.LogWarning(
                        $"[FcBrushLoadService] '{brush.VirtualPath}': {result?.ErrorMessage ?? "Import failed."}",
                        brush);
                    return;
                }

                var lodResults = await FcLevelGeometryImportHelper.ImportSiblingLodsWithTexturePreloadAsync(
                    brush.VirtualPath,
                    LevelScopeId,
                    _lodService,
                    ct,
                    skipTexturePreload: skipPreload);

                if (ct.IsCancellationRequested) return;

                if (!string.IsNullOrWhiteSpace(brush.MaterialOverride) || brush.MaterialId >= 0)
                {
                    var overrideSvc = FcLevelMaterialOverrideService.Current;
                    if (overrideSvc != null)
                        await overrideSvc.PreloadOverrideTexturesAsync(brush.MaterialOverride, brush.MaterialId, LevelScopeId, ct);
                }

                if (ct.IsCancellationRequested) return;
                await UniTask.SwitchToMainThread(ct);

                if (brush == null || ct.IsCancellationRequested) return;
                brush.ApplyLoadResult(
                    result,
                    lodResults,
                    LevelScopeId,
                    releaseImportResultsOnDestroy: true);
                success = true;
            }
            catch (OperationCanceledException) { }
            finally
            {
                // Guarantee the state below is only ever touched from the main thread,
                // regardless of which thread the try block's exception/cancellation
                // surfaced on (imports can throw off a pool thread).
                await UniTask.SwitchToMainThread(CancellationToken.None);
                _activeCount--;
                double elapsed = sw.Elapsed.TotalMilliseconds;
                _report.RecordBrushLoad(success, elapsed);

                if (_pending.Count == 0 && _activeCount == 0)
                    LogReport();
            }
        }

        void LogReport()
        {
            _report.LogRuntimeBrushes();
        }

        static bool TryGetPreloadedHandle(FcBrushInstance brush, out FcLevelGeometryAssetHandle handle)
        {
            handle = null;
            if (brush == null || string.IsNullOrWhiteSpace(brush.VirtualPath))
                return false;

            var levelLoadService = FcLevelLoadService.Current;
            if (levelLoadService == null)
                return false;

            return levelLoadService.TryGetBrushPreloadedHandle(brush.VirtualPath, out handle);
        }
    }
}
