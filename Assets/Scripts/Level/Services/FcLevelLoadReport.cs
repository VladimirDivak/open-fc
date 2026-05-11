using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using OpenFarCry.Importer.Cgf;
using UnityEngine;

namespace OpenFarCry.Level.Services
{
    // Timing accumulator for a single level build or load session.
    // Phase 1: editor build timing + brush runtime timing.
    // Phase 2+: extended with entity/mesh/animation per-request stats.
    public sealed class FcLevelLoadReport
    {
        sealed class EntityClassStats
        {
            public int Applied;
            public int Failed;
            public int Cancelled;
            public double TotalLoadMs;
            public double SlowestMs;
        }

        readonly struct SlowEntityEntry
        {
            public readonly string EntityClass;
            public readonly string VirtualPath;
            public readonly double LoadMs;

            public SlowEntityEntry(string entityClass, string virtualPath, double loadMs)
            {
                EntityClass = entityClass;
                VirtualPath = virtualPath;
                LoadMs = loadMs;
            }
        }

        public string LevelName;
        public string MissionName;
        readonly object _sync = new();

        readonly List<(string phase, double ms)> _phases = new();

        // Runtime brush load stats (filled by FcBrushLoadService).
        public int BrushesRegistered;
        public int BrushesLoaded;
        public int BrushesFailed;
        public double BrushTotalMs;
        public double BrushSlowestMs;
        double _brushFastestMs = double.MaxValue;

        // Runtime entity load stats (filled by FcEntityLoadService).
        public int EntitiesQueued;
        public int EntitiesApplied;
        public int EntitiesFailed;
        public int EntitiesCancelled;
        public int EntitiesRetried;
        public int EntityCacheHits;
        public int EntityCacheMisses;
        public int EntityConcurrencyPeak;
        public double EntityQueueWaitTotalMs;
        public double EntityQueueWaitSlowestMs;
        public double EntityLoadTotalMs;
        public double EntityLoadSlowestMs;

        double _entityQueueWaitFastestMs = double.MaxValue;
        double _entityLoadFastestMs = double.MaxValue;
        readonly Dictionary<string, EntityClassStats> _entityStatsByClass = new(StringComparer.OrdinalIgnoreCase);
        readonly List<SlowEntityEntry> _slowEntities = new();
        const int MaxSlowEntries = 8;

        // Runtime animation attach stats (filled by FcAnimationLoadService).
        public int AnimationAttachCount;
        public int AnimationClipTotal;
        public double AnimationAttachTotalMs;
        public double AnimationAttachSlowestMs;
        double _animationAttachFastestMs = double.MaxValue;
        public int AnimationSetCacheHits;
        public int AnimationSetCacheMisses;
        public int AnimationClipCacheHits;
        public int AnimationClipCacheMisses;
        public int AnimationCafHits;
        public int AnimationCafMisses;

        public void RecordPhase(string phase, double ms)
        {
            lock (_sync)
                _phases.Add((phase, ms));
        }

        public void RecordBrushLoad(bool success, double ms)
        {
            lock (_sync)
            {
                if (success)
                {
                    BrushesLoaded++;
                    BrushTotalMs += ms;
                    if (ms < _brushFastestMs) _brushFastestMs = ms;
                    if (ms > BrushSlowestMs) BrushSlowestMs = ms;
                }
                else
                {
                    BrushesFailed++;
                }
            }
        }

        public void RecordEntityQueued()
        {
            lock (_sync)
                EntitiesQueued++;
        }

        public void RecordEntityRetried()
        {
            lock (_sync)
                EntitiesRetried++;
        }

        public void RecordEntityCacheResult(bool cacheHit)
        {
            lock (_sync)
            {
                if (cacheHit)
                    EntityCacheHits++;
                else
                    EntityCacheMisses++;
            }
        }

        public void RecordAnimationAttach(
            int clipCount,
            double attachMs,
            int animationSetHitDelta,
            int animationSetMissDelta,
            int clipHitDelta,
            int clipMissDelta,
            int cafHitDelta,
            int cafMissDelta)
        {
            lock (_sync)
            {
                AnimationAttachCount++;
                AnimationClipTotal += Mathf.Max(0, clipCount);

                if (attachMs >= 0)
                {
                    AnimationAttachTotalMs += attachMs;
                    if (attachMs < _animationAttachFastestMs) _animationAttachFastestMs = attachMs;
                    if (attachMs > AnimationAttachSlowestMs) AnimationAttachSlowestMs = attachMs;
                }

                AnimationSetCacheHits += Mathf.Max(0, animationSetHitDelta);
                AnimationSetCacheMisses += Mathf.Max(0, animationSetMissDelta);
                AnimationClipCacheHits += Mathf.Max(0, clipHitDelta);
                AnimationClipCacheMisses += Mathf.Max(0, clipMissDelta);
                AnimationCafHits += Mathf.Max(0, cafHitDelta);
                AnimationCafMisses += Mathf.Max(0, cafMissDelta);
            }
        }

        public void RecordEntityStarted(double queueWaitMs, int activeCount)
        {
            lock (_sync)
            {
                if (queueWaitMs >= 0)
                {
                    EntityQueueWaitTotalMs += queueWaitMs;
                    if (queueWaitMs < _entityQueueWaitFastestMs) _entityQueueWaitFastestMs = queueWaitMs;
                    if (queueWaitMs > EntityQueueWaitSlowestMs) EntityQueueWaitSlowestMs = queueWaitMs;
                }

                if (activeCount > EntityConcurrencyPeak)
                    EntityConcurrencyPeak = activeCount;
            }
        }

        public void RecordEntityCompleted(
            string entityClass,
            string virtualPath,
            EntityLoadState state,
            double loadMs)
        {
            lock (_sync)
            {
                if (state == EntityLoadState.Applied)
                {
                    EntitiesApplied++;
                    if (loadMs >= 0)
                    {
                        EntityLoadTotalMs += loadMs;
                        if (loadMs < _entityLoadFastestMs) _entityLoadFastestMs = loadMs;
                        if (loadMs > EntityLoadSlowestMs) EntityLoadSlowestMs = loadMs;
                    }

                    AddSlowEntity(entityClass, virtualPath, loadMs);
                }
                else if (state == EntityLoadState.Cancelled)
                {
                    EntitiesCancelled++;
                }
                else if (state == EntityLoadState.Failed)
                {
                    EntitiesFailed++;
                }

                string cls = string.IsNullOrWhiteSpace(entityClass) ? "<unknown>" : entityClass;
                if (!_entityStatsByClass.TryGetValue(cls, out var classStats))
                {
                    classStats = new EntityClassStats();
                    _entityStatsByClass[cls] = classStats;
                }

                if (state == EntityLoadState.Applied)
                {
                    classStats.Applied++;
                    classStats.TotalLoadMs += Mathf.Max(0f, (float)loadMs);
                    if (loadMs > classStats.SlowestMs) classStats.SlowestMs = loadMs;
                }
                else if (state == EntityLoadState.Cancelled)
                {
                    classStats.Cancelled++;
                }
                else if (state == EntityLoadState.Failed)
                {
                    classStats.Failed++;
                }
            }
        }

        public void LogEditorBuild()
        {
            lock (_sync)
            {
                var sb = new StringBuilder();
                sb.AppendLine($"[FcLevel] Editor build — {LevelName}/{MissionName}");
                double total = 0;
                foreach (var (phase, ms) in _phases)
                {
                    sb.AppendLine($"  {phase,-36} {ms,8:F1} ms");
                    total += ms;
                }
                sb.AppendLine($"  {"TotalEditorBuild",-36} {total,8:F1} ms");
                Debug.Log(sb.ToString());
            }
        }

        public void LogRuntimeBrushes()
        {
            lock (_sync)
            {
                if (BrushesLoaded == 0 && BrushesFailed == 0)
                    return;

                double avg = BrushesLoaded > 0 ? BrushTotalMs / BrushesLoaded : 0;
                double fastest = BrushesLoaded > 0 ? _brushFastestMs : 0;
                Debug.Log(
                    $"[FcLevel] Brush load — {LevelName}: " +
                    $"registered={BrushesRegistered} ok={BrushesLoaded} fail={BrushesFailed} " +
                    $"total={BrushTotalMs:F0}ms avg={avg:F1}ms " +
                    $"min={fastest:F1}ms max={BrushSlowestMs:F1}ms");
            }
        }

        public void LogRuntimeEntities()
        {
            lock (_sync)
            {
                if (EntitiesQueued == 0 &&
                    EntitiesApplied == 0 &&
                    EntitiesFailed == 0 &&
                    EntitiesCancelled == 0)
                {
                    return;
                }

                double loadAvg = EntitiesApplied > 0 ? EntityLoadTotalMs / EntitiesApplied : 0;
                double loadFastest = EntitiesApplied > 0 ? _entityLoadFastestMs : 0;
                int started = EntitiesApplied + EntitiesFailed + EntitiesCancelled;
                double queueAvg = started > 0 ? EntityQueueWaitTotalMs / started : 0;
                double queueFastest = started > 0 ? _entityQueueWaitFastestMs : 0;
                int cacheTotal = EntityCacheHits + EntityCacheMisses;
                double cacheHitRate = cacheTotal > 0 ? (double)EntityCacheHits / cacheTotal * 100.0 : 0;

                Debug.Log(
                    $"[FcLevel] Entity load — {LevelName}: " +
                    $"queued={EntitiesQueued} applied={EntitiesApplied} failed={EntitiesFailed} cancelled={EntitiesCancelled} retried={EntitiesRetried} " +
                    $"queue_avg={queueAvg:F1}ms queue_min={queueFastest:F1}ms queue_max={EntityQueueWaitSlowestMs:F1}ms " +
                    $"load_total={EntityLoadTotalMs:F0}ms load_avg={loadAvg:F1}ms load_min={loadFastest:F1}ms load_max={EntityLoadSlowestMs:F1}ms " +
                    $"cache_hit={EntityCacheHits} cache_miss={EntityCacheMisses} cache_hit_rate={cacheHitRate:F1}% " +
                    $"active_peak={EntityConcurrencyPeak}");

                if (_entityStatsByClass.Count > 0)
                {
                    var classSb = new StringBuilder();
                    classSb.Append($"[FcLevel] Entity by class — {LevelName}: ");
                    bool first = true;
                    foreach (var kv in _entityStatsByClass.OrderByDescending(x => x.Value.Applied + x.Value.Failed + x.Value.Cancelled))
                    {
                        if (!first) classSb.Append(" | ");
                        first = false;

                        var stats = kv.Value;
                        double avg = stats.Applied > 0 ? stats.TotalLoadMs / stats.Applied : 0;
                        classSb.Append(
                            $"{kv.Key}: ok={stats.Applied} fail={stats.Failed} cancel={stats.Cancelled} avg={avg:F1}ms max={stats.SlowestMs:F1}ms");
                    }
                    Debug.Log(classSb.ToString());
                }

                if (_slowEntities.Count > 0)
                {
                    var slowSb = new StringBuilder();
                    slowSb.Append($"[FcLevel] Entity slowest — {LevelName}: ");
                    for (int i = 0; i < _slowEntities.Count; i++)
                    {
                        if (i > 0) slowSb.Append(" | ");
                        var e = _slowEntities[i];
                        slowSb.Append($"#{i + 1} {e.LoadMs:F1}ms {e.EntityClass} {e.VirtualPath}");
                    }
                    Debug.Log(slowSb.ToString());
                }
            }
        }

        public void LogRuntimeAnimations()
        {
            lock (_sync)
            {
                if (AnimationAttachCount <= 0)
                    return;

                double attachAvg = AnimationAttachTotalMs / AnimationAttachCount;
                double attachFastest = _animationAttachFastestMs == double.MaxValue ? 0 : _animationAttachFastestMs;
                double clipsPerAttach = (double)AnimationClipTotal / AnimationAttachCount;

                int setTotal = AnimationSetCacheHits + AnimationSetCacheMisses;
                int clipTotal = AnimationClipCacheHits + AnimationClipCacheMisses;
                int cafTotal = AnimationCafHits + AnimationCafMisses;
                double setHitRate = setTotal > 0 ? (double)AnimationSetCacheHits / setTotal * 100.0 : 0;
                double clipHitRate = clipTotal > 0 ? (double)AnimationClipCacheHits / clipTotal * 100.0 : 0;
                double cafHitRate = cafTotal > 0 ? (double)AnimationCafHits / cafTotal * 100.0 : 0;

                Debug.Log(
                    $"[FcLevel] Animation attach — {LevelName}: " +
                    $"calls={AnimationAttachCount} clips={AnimationClipTotal} clips_per_call={clipsPerAttach:F1} " +
                    $"attach_total={AnimationAttachTotalMs:F0}ms attach_avg={attachAvg:F1}ms attach_min={attachFastest:F1}ms attach_max={AnimationAttachSlowestMs:F1}ms " +
                    $"set_hit={AnimationSetCacheHits} set_miss={AnimationSetCacheMisses} set_hit_rate={setHitRate:F1}% " +
                    $"clip_hit={AnimationClipCacheHits} clip_miss={AnimationClipCacheMisses} clip_hit_rate={clipHitRate:F1}% " +
                    $"caf_hit={AnimationCafHits} caf_miss={AnimationCafMisses} caf_hit_rate={cafHitRate:F1}%");

                var cafStats = CafLoader.GetStats();
                var animSetStats = CgfAnimationSetCache.GetStats();
                Debug.Log(
                    $"[FcLevel] Anim cache sizes — {LevelName}: " +
                    $"caf_path={cafStats.PathEntryCount} caf_semantic={cafStats.SemanticEntryCount} caf_src_hash={cafStats.SourceHashEntryCount} " +
                    $"anim_sets={animSetStats.AnimationSetEntryCount} anim_links={animSetStats.AnimationSetModelLinkCount} " +
                    $"sem_clips={animSetStats.SemanticClipEntryCount} clips={animSetStats.ClipEntryCount}");
            }
        }

        void AddSlowEntity(string entityClass, string virtualPath, double loadMs)
        {
            if (loadMs < 0)
                return;

            _slowEntities.Add(new SlowEntityEntry(
                string.IsNullOrWhiteSpace(entityClass) ? "<unknown>" : entityClass,
                string.IsNullOrWhiteSpace(virtualPath) ? "<none>" : virtualPath,
                loadMs));

            _slowEntities.Sort((a, b) => b.LoadMs.CompareTo(a.LoadMs));
            if (_slowEntities.Count > MaxSlowEntries)
                _slowEntities.RemoveRange(MaxSlowEntries, _slowEntities.Count - MaxSlowEntries);
        }
    }
}
