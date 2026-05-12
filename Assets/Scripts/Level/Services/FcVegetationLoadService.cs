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
            if (_pending.Count == 0 || _activeCount >= _maxConcurrent)
                return;

            SortByDistance();

            int started = 0;
            for (int i = 0; i < _pending.Count && started < _loadsPerFrame && _activeCount < _maxConcurrent; )
            {
                var veg = _pending[i];
                _pending.RemoveAt(i);
                _pendingSet.Remove(veg);
                if (veg == null) continue;
                _activeCount++;
                LoadOneAsync(veg).Forget();
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

        async UniTaskVoid LoadOneAsync(FcVegetationInstance veg)
        {
            var ct = veg.GetCancellationTokenOnDestroy();
            try
            {
                var result = await CgfRuntimeImporter.ImportAsync(
                    new CgfRuntimeImportRequest(
                        virtualPath: veg.VirtualPath,
                        importSkeleton: false,
                        importAnimations: false,
                        importScale: 0.01f,
                        useRuntimeMemoryCache: true),
                    LevelScopeId,
                    ct);

                if (ct.IsCancellationRequested) return;

                if (!result.Success)
                {
                    Debug.LogWarning($"[FcVegetationLoadService] '{veg.VirtualPath}': {result.ErrorMessage}", veg);
                    return;
                }

                await CgfRuntimeImporter.MaterialService.PreloadTexturesAsync(
                    result.ParsedFile,
                    result.Mesh,
                    result.BuildResult?.SubmeshMaterialIds,
                    LevelScopeId,
                    ct);

                if (ct.IsCancellationRequested) return;

                var lodResults = await LoadLodResultsAsync(veg.VirtualPath, ct);

                if (ct.IsCancellationRequested) return;
                await UniTask.SwitchToMainThread(ct);

                if (veg == null || ct.IsCancellationRequested) return;
                veg.ApplyLoadResult(result, lodResults, LevelScopeId);
            }
            catch (OperationCanceledException) { }
            finally
            {
                _activeCount--;
            }
        }

        async UniTask<List<CgfRuntimeImportResult>> LoadLodResultsAsync(
            string baseVirtualPath, System.Threading.CancellationToken ct)
        {
            var lodPaths = _lodService.FindSiblingLodPaths(baseVirtualPath);
            var lodResults = new List<CgfRuntimeImportResult>(lodPaths.Count);

            for (int i = 0; i < lodPaths.Count; i++)
            {
                if (ct.IsCancellationRequested) break;

                var lodResult = await CgfRuntimeImporter.ImportAsync(
                    new CgfRuntimeImportRequest(
                        virtualPath: lodPaths[i],
                        importSkeleton: false,
                        importAnimations: false,
                        importScale: 0.01f,
                        useRuntimeMemoryCache: true),
                    LevelScopeId,
                    ct);

                if (!lodResult.Success) continue;

                await CgfRuntimeImporter.MaterialService.PreloadTexturesAsync(
                    lodResult.ParsedFile,
                    lodResult.Mesh,
                    lodResult.BuildResult?.SubmeshMaterialIds,
                    LevelScopeId,
                    ct);

                lodResults.Add(lodResult);
            }

            return lodResults;
        }
    }
}
