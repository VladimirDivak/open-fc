using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using Cysharp.Threading.Tasks;
using OpenFarCry.Level.Entities;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace OpenFarCry.Level.Services
{
    public enum EntityLoadPriority
    {
        Critical = 0,
        NearCamera = 10,
        Characters = 20,
        GameplayRelevant = 30,
        Background = 100
    }

    public enum EntityLoadState
    {
        Pending,
        Loading,
        Applied,
        Failed,
        Cancelled
    }

    [DefaultExecutionOrder(-98)]
    public sealed class FcEntityLoadService : MonoBehaviour
    {
        sealed class QueuedEntityLoad
        {
            public FcMeshEntity Entity;
            public EntityLoadPriority Priority;
            public string ScopeId;
            public long Sequence;
            public long EnqueuedAtTicks;
            public int Attempt;
        }

        public static FcEntityLoadService Current { get; private set; }

        [SerializeField] int _maxConcurrent = 4;
        [SerializeField] int _loadsPerFrame = 2;
        [SerializeField] int _maxRetries = 1;

        // Deferred queue: items with priority >= _deferredThreshold only start when _pending is empty.
        [SerializeField] EntityLoadPriority _deferredThreshold = EntityLoadPriority.Background;
        [SerializeField] int _maxDeferredConcurrent = 1;

        // Near-camera promotion: deferred items inside this radius move to _pending each frame.
        [SerializeField] float _promoteRadius = 50f;

        readonly List<QueuedEntityLoad> _pending = new List<QueuedEntityLoad>();
        readonly List<QueuedEntityLoad> _deferred = new List<QueuedEntityLoad>();
        readonly HashSet<FcMeshEntity> _active = new HashSet<FcMeshEntity>();
        readonly Dictionary<FcMeshEntity, EntityLoadState> _stateByEntity = new Dictionary<FcMeshEntity, EntityLoadState>();
        readonly Dictionary<string, CancellationTokenSource> _scopeCtsById = new Dictionary<string, CancellationTokenSource>(StringComparer.Ordinal);

        IFcMeshLoadService _meshLoadService;
        FcLevelLoadReport _report;
        long _sequence;
        int _activeCount;

        void Awake()
        {
            Current = this;
            var cache = GetComponent<FcLevelCacheService>() ?? GetComponentInParent<FcLevelCacheService>();
            string scopeId = cache != null ? cache.LevelScopeId : string.Empty;
            _report = FcLevelRuntimeReportRegistry.GetOrCreate(scopeId);
            EnsureMeshLoadService();
            if (_meshLoadService == null)
                Debug.LogWarning("[FcEntityLoadService] FcMeshLoadService not found. Requests will stay unprocessed.", this);
        }

        bool EnsureMeshLoadService()
        {
            if (_meshLoadService != null)
                return true;

            _meshLoadService = ResolveMeshLoadService();
            return _meshLoadService != null;
        }

        void EnsureReport(string preferredScopeId = null)
        {
            if (_report != null)
                return;

            string scopeId = preferredScopeId;
            if (string.IsNullOrWhiteSpace(scopeId))
            {
                var cache = GetComponent<FcLevelCacheService>() ?? GetComponentInParent<FcLevelCacheService>();
                scopeId = cache != null ? cache.LevelScopeId : string.Empty;
            }

            _report = FcLevelRuntimeReportRegistry.GetOrCreate(scopeId);
        }

        IFcMeshLoadService ResolveMeshLoadService()
        {
            var localBehaviours = GetComponents<MonoBehaviour>();
            for (int i = 0; i < localBehaviours.Length; i++)
            {
                if (localBehaviours[i] is IFcMeshLoadService local)
                    return local;
            }

            var parentBehaviours = GetComponentsInParent<MonoBehaviour>(includeInactive: true);
            for (int i = 0; i < parentBehaviours.Length; i++)
            {
                if (parentBehaviours[i] is IFcMeshLoadService parent)
                    return parent;
            }

            return null;
        }

        void OnDestroy()
        {
            if (Current == this) Current = null;
            foreach (var cts in _scopeCtsById.Values)
            {
                cts.Cancel();
                cts.Dispose();
            }
            _scopeCtsById.Clear();
        }

        public void Enqueue(FcMeshEntity entity, EntityLoadPriority priority)
        {
            if (entity == null || !EnsureMeshLoadService())
                return;
            if (_active.Contains(entity))
                return;

            // Check if already in pending — update priority if improved.
            for (int i = 0; i < _pending.Count; i++)
            {
                if (_pending[i].Entity != entity)
                    continue;
                if (priority < _pending[i].Priority)
                    _pending[i].Priority = priority;
                _stateByEntity[entity] = EntityLoadState.Pending;
                return;
            }

            // Check if already in deferred — update priority or migrate to pending if now critical.
            for (int i = 0; i < _deferred.Count; i++)
            {
                if (_deferred[i].Entity != entity)
                    continue;
                if (priority < _deferred[i].Priority)
                {
                    _deferred[i].Priority = priority;
                    if (priority < _deferredThreshold)
                    {
                        var migrated = _deferred[i];
                        _deferred.RemoveAt(i);
                        _pending.Add(migrated);
                    }
                }
                _stateByEntity[entity] = EntityLoadState.Pending;
                return;
            }

            string scopeId = entity.GetLevelScopeId();
            EnsureReport(scopeId);

            var entry = new QueuedEntityLoad
            {
                Entity = entity,
                Priority = priority,
                ScopeId = scopeId,
                Sequence = ++_sequence,
                EnqueuedAtTicks = Stopwatch.GetTimestamp(),
                Attempt = 0
            };

            if (priority >= _deferredThreshold)
                _deferred.Add(entry);
            else
                _pending.Add(entry);

            _stateByEntity[entity] = EntityLoadState.Pending;
            _report.RecordEntityQueued();
        }

        public void EnqueueRange(IEnumerable<FcMeshEntity> entities, EntityLoadPriority priority)
        {
            if (entities == null)
                return;

            foreach (var entity in entities)
                Enqueue(entity, priority);
        }

        public void CancelAllForScope(string levelScopeId)
        {
            if (string.IsNullOrWhiteSpace(levelScopeId))
                return;
            EnsureReport(levelScopeId);

            if (_scopeCtsById.TryGetValue(levelScopeId, out var cts))
            {
                cts.Cancel();
                cts.Dispose();
                _scopeCtsById.Remove(levelScopeId);
            }

            CancelListForScope(_pending, levelScopeId);
            CancelListForScope(_deferred, levelScopeId);
        }

        // Cancels queued (not active) loads for the scope without triggering the scope CTS.
        // Use before WaitForIdleAsync so active loads can complete before scope release.
        public void CancelQueuedForScope(string levelScopeId)
        {
            if (string.IsNullOrWhiteSpace(levelScopeId))
                return;
            EnsureReport(levelScopeId);
            CancelListForScope(_pending, levelScopeId);
            CancelListForScope(_deferred, levelScopeId);
        }

        void CancelListForScope(List<QueuedEntityLoad> list, string levelScopeId)
        {
            for (int i = list.Count - 1; i >= 0; i--)
            {
                var item = list[i];
                if (!string.Equals(item.ScopeId, levelScopeId, StringComparison.Ordinal))
                    continue;

                if (item.Entity != null)
                {
                    _stateByEntity[item.Entity] = EntityLoadState.Cancelled;
                    _report.RecordEntityCompleted(item.Entity.EntityClass, item.Entity.VirtualPath, EntityLoadState.Cancelled, 0);
                }
                list.RemoveAt(i);
            }
        }

        // Returns when no active loads remain or timeout expires. Main thread only.
        public async UniTask<bool> WaitForIdleAsync(int timeoutMs = 2000, CancellationToken ct = default)
        {
            float deadline = Time.realtimeSinceStartup + timeoutMs / 1000f;
            while (Time.realtimeSinceStartup < deadline && !ct.IsCancellationRequested)
            {
                if (_activeCount <= 0)
                    return true;
                await UniTask.NextFrame(ct);
            }
            return _activeCount <= 0;
        }

        public bool TryGetState(FcMeshEntity entity, out EntityLoadState state)
        {
            if (entity != null && _stateByEntity.TryGetValue(entity, out state))
                return true;

            state = default;
            return false;
        }

        void Update()
        {
            if (!EnsureMeshLoadService())
                return;
            EnsureReport();

            if (_meshLoadService == null || _activeCount >= _maxConcurrent)
                return;

            // Promote nearby deferred items to pending (Slice B).
            UpdateDeferredPromotions();

            // Critical queue drains first — deferred waits until it empties.
            if (_pending.Count > 0)
            {
                SortList(_pending);
                DrainList(_pending, _loadsPerFrame, _maxConcurrent);
                return;
            }

            // Deferred drains only when pending is empty and under deferred concurrency cap.
            if (_deferred.Count > 0 && _activeCount < _maxDeferredConcurrent)
            {
                SortList(_deferred);
                DrainList(_deferred, _loadsPerFrame, _maxDeferredConcurrent);
            }
        }

        void UpdateDeferredPromotions()
        {
            if (_deferred.Count == 0 || _promoteRadius <= 0f)
                return;

            var cam = Camera.main;
            if (cam == null)
                return;

            Vector3 camPos = cam.transform.position;
            float radiusSq = _promoteRadius * _promoteRadius;

            for (int i = _deferred.Count - 1; i >= 0; i--)
            {
                var item = _deferred[i];
                if (item.Entity == null)
                    continue;

                float distSq = Vector3.SqrMagnitude(item.Entity.transform.position - camPos);
                if (distSq <= radiusSq)
                {
                    item.Priority = EntityLoadPriority.NearCamera;
                    _deferred.RemoveAt(i);
                    _pending.Add(item);
                }
            }
        }

        void SortList(List<QueuedEntityLoad> list)
        {
            var cam = Camera.main;
            Vector3 camPos = cam != null ? cam.transform.position : Vector3.zero;
            bool hasCamera = cam != null;

            list.Sort((a, b) =>
            {
                int priorityCompare = a.Priority.CompareTo(b.Priority);
                if (priorityCompare != 0)
                    return priorityCompare;

                if (hasCamera)
                {
                    float da = a.Entity != null ? Vector3.SqrMagnitude(a.Entity.transform.position - camPos) : float.MaxValue;
                    float db = b.Entity != null ? Vector3.SqrMagnitude(b.Entity.transform.position - camPos) : float.MaxValue;
                    int distanceCompare = da.CompareTo(db);
                    if (distanceCompare != 0)
                        return distanceCompare;
                }

                return a.Sequence.CompareTo(b.Sequence);
            });
        }

        void DrainList(List<QueuedEntityLoad> list, int maxStartsThisFrame, int concurrencyLimit)
        {
            int started = 0;
            for (int i = 0; i < list.Count && started < maxStartsThisFrame && _activeCount < concurrencyLimit;)
            {
                var item = list[i];
                list.RemoveAt(i);
                if (item.Entity == null) continue;
                if (_active.Contains(item.Entity)) continue;

                _active.Add(item.Entity);
                _activeCount++;
                _stateByEntity[item.Entity] = EntityLoadState.Loading;
                double waitMs = item.EnqueuedAtTicks > 0
                    ? (Stopwatch.GetTimestamp() - item.EnqueuedAtTicks) * 1000.0 / Stopwatch.Frequency
                    : 0;
                _report.RecordEntityStarted(waitMs, _activeCount);
                LoadOneAsync(item).Forget();
                started++;
            }
        }

        async UniTaskVoid LoadOneAsync(QueuedEntityLoad item)
        {
            var entity = item.Entity;
            string scopeId = item.ScopeId;
            var scopeToken = GetScopeToken(scopeId);
            var sw = Stopwatch.StartNew();
            bool completionRecorded = false;
            EnsureReport(scopeId);
            try
            {
                if (entity == null || scopeToken.IsCancellationRequested)
                {
                    if (entity != null)
                    {
                        _stateByEntity[entity] = EntityLoadState.Cancelled;
                        _report.RecordEntityCompleted(entity.EntityClass, entity.VirtualPath, EntityLoadState.Cancelled, sw.Elapsed.TotalMilliseconds);
                        completionRecorded = true;
                    }
                    return;
                }

                var request = entity.CreateLoadRequest();
                var artifact = await _meshLoadService.LoadAsync(request, scopeToken);
                if (scopeToken.IsCancellationRequested || entity == null)
                {
                    if (entity != null)
                    {
                        _stateByEntity[entity] = EntityLoadState.Cancelled;
                        _report.RecordEntityCompleted(entity.EntityClass, entity.VirtualPath, EntityLoadState.Cancelled, sw.Elapsed.TotalMilliseconds);
                        completionRecorded = true;
                    }
                    return;
                }

                if (artifact == null || !artifact.Success)
                {
                    if (TryEnqueueRetry(item, scopeToken, artifact?.ErrorMessage ?? "unknown error"))
                        return;

                    _stateByEntity[entity] = EntityLoadState.Failed;
                    _report.RecordEntityCompleted(entity.EntityClass, entity.VirtualPath, EntityLoadState.Failed, sw.Elapsed.TotalMilliseconds);
                    completionRecorded = true;
                    Debug.LogWarning($"[FcEntityLoadService] '{entity.name}': mesh load failed: {artifact?.ErrorMessage ?? "unknown error"}", entity);
                    return;
                }

                _report.RecordEntityCacheResult(artifact.ImportResult?.UsedRuntimeMemoryCache ?? false);

                if (!string.IsNullOrWhiteSpace(entity.MaterialOverride))
                {
                    var overrideSvc = FcLevelMaterialOverrideService.Current;
                    if (overrideSvc != null)
                        await overrideSvc.PreloadOverrideTexturesAsync(entity.MaterialOverride, -1, scopeId, scopeToken);
                }

                await UniTask.SwitchToMainThread(scopeToken);
                if (scopeToken.IsCancellationRequested || entity == null)
                {
                    if (entity != null)
                    {
                        _stateByEntity[entity] = EntityLoadState.Cancelled;
                        _report.RecordEntityCompleted(entity.EntityClass, entity.VirtualPath, EntityLoadState.Cancelled, sw.Elapsed.TotalMilliseconds);
                        completionRecorded = true;
                    }
                    return;
                }

                entity.ApplyLoadedMesh(artifact);
                _stateByEntity[entity] = EntityLoadState.Applied;
                _report.RecordEntityCompleted(
                    entity.EntityClass,
                    entity.VirtualPath,
                    EntityLoadState.Applied,
                    artifact.TotalMs > 0 ? artifact.TotalMs : sw.Elapsed.TotalMilliseconds);
                completionRecorded = true;
            }
            catch (OperationCanceledException)
            {
                if (entity != null)
                {
                    _stateByEntity[entity] = EntityLoadState.Cancelled;
                    if (!completionRecorded)
                        _report.RecordEntityCompleted(entity.EntityClass, entity.VirtualPath, EntityLoadState.Cancelled, sw.Elapsed.TotalMilliseconds);
                }
            }
            catch (Exception e)
            {
                if (TryEnqueueRetry(item, scopeToken, e.Message))
                    return;

                if (entity != null)
                {
                    _stateByEntity[entity] = EntityLoadState.Failed;
                    if (!completionRecorded)
                        _report.RecordEntityCompleted(entity.EntityClass, entity.VirtualPath, EntityLoadState.Failed, sw.Elapsed.TotalMilliseconds);
                }
                Debug.LogWarning($"[FcEntityLoadService] '{entity?.name ?? "<null>"}': {e.Message}");
            }
            finally
            {
                if (entity != null)
                    _active.Remove(entity);
                _activeCount = Mathf.Max(0, _activeCount - 1);
                if (_pending.Count == 0 && _deferred.Count == 0 && _activeCount == 0)
                {
                    _report.LogRuntimeEntities();
                    _report.LogRuntimeAnimations();
                }
            }
        }

        bool TryEnqueueRetry(QueuedEntityLoad item, CancellationToken scopeToken, string reason)
        {
            if (scopeToken.IsCancellationRequested)
                return false;
            if (item.Entity == null)
                return false;
            if (item.Attempt >= _maxRetries)
                return false;

            var retry = new QueuedEntityLoad
            {
                Entity = item.Entity,
                Priority = item.Priority,
                ScopeId = item.ScopeId,
                Sequence = ++_sequence,
                EnqueuedAtTicks = Stopwatch.GetTimestamp(),
                Attempt = item.Attempt + 1
            };

            if (retry.Priority >= _deferredThreshold)
                _deferred.Add(retry);
            else
                _pending.Add(retry);

            _stateByEntity[item.Entity] = EntityLoadState.Pending;
            _report.RecordEntityRetried();
            Debug.LogWarning(
                $"[FcEntityLoadService] Retry {retry.Attempt}/{_maxRetries} for '{item.Entity.name}' after error: {reason}",
                item.Entity);
            return true;
        }

        CancellationToken GetScopeToken(string scopeId)
        {
            if (string.IsNullOrWhiteSpace(scopeId))
                return this.GetCancellationTokenOnDestroy();

            if (!_scopeCtsById.TryGetValue(scopeId, out var cts))
            {
                cts = CancellationTokenSource.CreateLinkedTokenSource(this.GetCancellationTokenOnDestroy());
                _scopeCtsById[scopeId] = cts;
            }

            return cts.Token;
        }
    }
}
