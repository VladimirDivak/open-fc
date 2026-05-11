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

        readonly List<FcBrushInstance> _pending = new List<FcBrushInstance>();
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
            if (brush != null && !_pending.Contains(brush))
            {
                _pending.Add(brush);
                _totalRegistered++;
                _report.BrushesRegistered = _totalRegistered;
            }
        }

        public void Unregister(FcBrushInstance brush)
        {
            _pending.Remove(brush);
        }

        void Update()
        {
            if (_pending.Count == 0 || _activeCount >= _maxConcurrent)
                return;

            SortByDistance();

            int started = 0;
            for (int i = 0; i < _pending.Count && started < _loadsPerFrame && _activeCount < _maxConcurrent; )
            {
                var brush = _pending[i];
                if (brush == null) { _pending.RemoveAt(i); continue; }
                _pending.RemoveAt(i);
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
                var result = await CgfRuntimeImporter.ImportAsync(
                    new CgfRuntimeImportRequest(
                        virtualPath: brush.VirtualPath,
                        importSkeleton: false,
                        importAnimations: false,
                        importScale: 0.01f,
                        useRuntimeMemoryCache: true),
                    LevelScopeId,
                    ct);

                if (ct.IsCancellationRequested) return;

                if (!result.Success)
                {
                    Debug.LogWarning($"[FcBrushLoadService] '{brush.VirtualPath}': {result.ErrorMessage}", brush);
                    return;
                }

                await CgfRuntimeImporter.MaterialService.PreloadTexturesAsync(
                    result.ParsedFile,
                    result.Mesh,
                    result.BuildResult?.SubmeshMaterialIds,
                    LevelScopeId,
                    ct);

                if (ct.IsCancellationRequested) return;
                await UniTask.SwitchToMainThread(ct);

                if (brush == null || ct.IsCancellationRequested) return;
                brush.ApplyLoadResult(result, LevelScopeId);
                success = true;
            }
            catch (OperationCanceledException) { }
            finally
            {
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
    }
}
