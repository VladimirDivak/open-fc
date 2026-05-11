using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using Cysharp.Threading.Tasks;
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

        public UniTask<FcLevelLoadReport> LoadLevelAsync(
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
            _report.RecordPhase("RuntimeTotalKickoff", sw.Elapsed.TotalMilliseconds);

            // Keep mission/brush data hot in this phase (orchestrator) and start mesh/character queue.
            _ = mission;
            _ = brushes;
            EnqueueSceneEntitiesForCurrentScope();

            return UniTask.FromResult(_report);
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

            _report = FcLevelRuntimeReportRegistry.GetOrCreate(scopeId);
            _loadedLevelName = string.Empty;
            _loadedMissionName = string.Empty;
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
