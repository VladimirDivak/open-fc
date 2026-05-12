using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using OpenFarCry.Importer.Cgf;
using OpenFarCry.Level.Entities;
using UnityEngine;

namespace OpenFarCry.Level.Services
{
    [DefaultExecutionOrder(-100)]
    public sealed class FcVegetationLoadService : MonoBehaviour
    {
        public static FcVegetationLoadService Current { get; private set; }

        [SerializeField] int _maxConcurrent = 8;
        [SerializeField] int _loadsPerFrame  = 8;
        [SerializeField] int _preloadedBuildsPerFrame = 512;
        [SerializeField] int _distanceSortMaxPending = 5000;

        readonly HashSet<FcVegetationInstance> _pendingSet = new HashSet<FcVegetationInstance>();
        readonly List<FcVegetationInstance>    _pending    = new List<FcVegetationInstance>();
        readonly CgfLodImportService           _lodService = new CgfLodImportService();
        int _activeCount;

        public string LevelScopeId { get; private set; }

        void Awake()
        {
            Current = this;
            var cache = GetComponent<FcLevelCacheService>() ?? GetComponentInParent<FcLevelCacheService>();
            LevelScopeId = cache != null ? cache.LevelScopeId : string.Empty;
        }

        void OnDestroy()
        {
            if (Current == this) Current = null;
        }

        public void Register(FcVegetationInstance veg)
        {
            if (veg != null && _pendingSet.Add(veg))
                _pending.Add(veg);
        }

        public void Unregister(FcVegetationInstance veg)
        {
            if (_pendingSet.Remove(veg))
                _pending.Remove(veg);
        }

        void Update()
        {
            if (_pending.Count == 0)
                return;

            if (_pending.Count <= Mathf.Max(0, _distanceSortMaxPending))
                SortByDistance();

            int preloadedBuilt = 0;
            int importedStarted = 0;
            int importBudgetPerFrame = Mathf.Max(0, _loadsPerFrame);
            int preloadedBudgetPerFrame = Mathf.Max(0, _preloadedBuildsPerFrame);

            for (int i = 0; i < _pending.Count; )
            {
                if (preloadedBuilt >= preloadedBudgetPerFrame &&
                    (importedStarted >= importBudgetPerFrame || _activeCount >= _maxConcurrent))
                {
                    break;
                }

                var veg = _pending[i];
                if (veg == null)
                {
                    _pending.RemoveAt(i);
                    _pendingSet.Remove(veg);
                    continue;
                }

                if (TryGetPreloadedHandle(veg, out var preloadedHandle))
                {
                    _pending.RemoveAt(i);
                    _pendingSet.Remove(veg);
                    veg.ApplyLoadResult(
                        preloadedHandle.BaseResult,
                        preloadedHandle.LodResults,
                        LevelScopeId,
                        releaseImportResultsOnDestroy: false);
                    preloadedBuilt++;
                    continue;
                }

                if (importedStarted >= importBudgetPerFrame || _activeCount >= _maxConcurrent)
                {
                    i++;
                    continue;
                }

                _pending.RemoveAt(i);
                _pendingSet.Remove(veg);
                _activeCount++;
                LoadOneAsync(veg).Forget();
                importedStarted++;
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

        async UniTaskVoid LoadOneAsync(FcVegetationInstance veg)
        {
            var ct = veg.GetCancellationTokenOnDestroy();
            try
            {
                if (TryGetPreloadedHandle(veg, out var preloadedHandle))
                {
                    await UniTask.SwitchToMainThread(ct);
                    if (veg == null || ct.IsCancellationRequested) return;
                    veg.ApplyLoadResult(
                        preloadedHandle.BaseResult,
                        preloadedHandle.LodResults,
                        LevelScopeId,
                        releaseImportResultsOnDestroy: false);
                    return;
                }

                var result = await FcLevelGeometryImportHelper.ImportStaticGeometryWithTexturePreloadAsync(
                    veg.VirtualPath,
                    LevelScopeId,
                    ct);

                if (ct.IsCancellationRequested) return;

                if (result == null || !result.Success)
                {
                    Debug.LogWarning(
                        $"[FcVegetationLoadService] '{veg.VirtualPath}': {result?.ErrorMessage ?? "Import failed."}",
                        veg);
                    return;
                }

                if (ct.IsCancellationRequested) return;

                var lodResults = await FcLevelGeometryImportHelper.ImportSiblingLodsWithTexturePreloadAsync(
                    veg.VirtualPath,
                    LevelScopeId,
                    _lodService,
                    ct);

                if (ct.IsCancellationRequested) return;
                await UniTask.SwitchToMainThread(ct);

                if (veg == null || ct.IsCancellationRequested) return;
                veg.ApplyLoadResult(result, lodResults, LevelScopeId, releaseImportResultsOnDestroy: true);
            }
            catch (OperationCanceledException) { }
            finally
            {
                _activeCount--;
            }
        }

        static bool TryGetPreloadedHandle(FcVegetationInstance veg, out FcLevelGeometryAssetHandle handle)
        {
            handle = null;
            if (veg == null || string.IsNullOrWhiteSpace(veg.VirtualPath))
                return false;

            var levelLoadService = FcLevelLoadService.Current;
            if (levelLoadService == null)
                return false;

            return levelLoadService.TryGetVegetationPreloadedHandle(veg.VirtualPath, out handle);
        }
    }
}
