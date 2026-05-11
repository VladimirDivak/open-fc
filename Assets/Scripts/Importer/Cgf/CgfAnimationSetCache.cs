using System;
using System.Collections.Generic;
using UnityEngine;

namespace OpenFarCry.Importer.Cgf
{
    // Runtime LRU caches for animation clips, semantic clip data, and animation sets.
    // Thread-safe. Key builders for all cache key spaces live here alongside the state.
    internal static class CgfAnimationSetCache
    {
        public readonly struct Stats
        {
            public readonly int ClipEntryCount;
            public readonly int AnimationSetEntryCount;
            public readonly int AnimationSetModelLinkCount;
            public readonly int SemanticClipEntryCount;
            public readonly int ClipHitCount;
            public readonly int ClipMissCount;
            public readonly int AnimationSetHitCount;
            public readonly int AnimationSetMissCount;
            public readonly int SemanticClipHitCount;
            public readonly int SemanticClipMissCount;

            public Stats(
                int clipEntryCount, int animationSetEntryCount, int animationSetModelLinkCount, int semanticClipEntryCount,
                int clipHitCount, int clipMissCount,
                int animationSetHitCount, int animationSetMissCount,
                int semanticClipHitCount, int semanticClipMissCount)
            {
                ClipEntryCount = clipEntryCount;
                AnimationSetEntryCount = animationSetEntryCount;
                AnimationSetModelLinkCount = animationSetModelLinkCount;
                SemanticClipEntryCount = semanticClipEntryCount;
                ClipHitCount = clipHitCount;
                ClipMissCount = clipMissCount;
                AnimationSetHitCount = animationSetHitCount;
                AnimationSetMissCount = animationSetMissCount;
                SemanticClipHitCount = semanticClipHitCount;
                SemanticClipMissCount = semanticClipMissCount;
            }
        }

        sealed class CachedClipEntry
        {
            public AnimationClip Clip;
            public long LastAccessTick;
        }

        sealed class CachedModelLayoutLink
        {
            public string AnimationSetCacheKey;
            public long LastAccessTick;
        }

        static readonly object s_sync = new object();
        static readonly Dictionary<string, CachedClipEntry> s_clipByCacheKey =
            new Dictionary<string, CachedClipEntry>(StringComparer.Ordinal);
        static readonly Dictionary<string, CachedAnimationSetEntry> s_animationSetByCacheKey =
            new Dictionary<string, CachedAnimationSetEntry>(StringComparer.Ordinal);
        static readonly Dictionary<string, CachedModelLayoutLink> s_animationSetKeyByModelLayout =
            new Dictionary<string, CachedModelLayoutLink>(StringComparer.Ordinal);
        static readonly Dictionary<string, CachedSemanticClipEntry> s_semanticClipByCacheKey =
            new Dictionary<string, CachedSemanticClipEntry>(StringComparer.Ordinal);
        static long s_tick;
        static int s_clipHitCount;
        static int s_clipMissCount;
        static int s_animationSetHitCount;
        static int s_animationSetMissCount;
        static int s_semanticClipHitCount;
        static int s_semanticClipMissCount;

        // Mercenary animation sets can exceed 1k clips per run. Keep headroom to avoid LRU thrash.
        const int MaxClipEntries = 4096;
        const int MaxAnimationSetEntries = 256;
        const int MaxAnimationSetModelLinks = 2048;
        const int MaxSemanticClipEntries = 4096;
        const string ClipBuildVersion = "clip-v2";
        const string LoopPolicyVersion = "loop-v1";
        const string AnimationSetVersion = "animset-v1";
        const string SemanticClipVersion = "semclip-v1";

        // ── Stats ─────────────────────────────────────────────────────────────

        internal static Stats GetStats()
        {
            lock (s_sync)
            {
                return new Stats(
                    s_clipByCacheKey.Count,
                    s_animationSetByCacheKey.Count,
                    s_animationSetKeyByModelLayout.Count,
                    s_semanticClipByCacheKey.Count,
                    s_clipHitCount, s_clipMissCount,
                    s_animationSetHitCount, s_animationSetMissCount,
                    s_semanticClipHitCount, s_semanticClipMissCount);
            }
        }

        internal static void Clear()
        {
            lock (s_sync)
            {
                foreach (var entry in s_clipByCacheKey.Values)
                    if (entry?.Clip != null)
                        DestroyUnityObject(entry.Clip);
                s_clipByCacheKey.Clear();
                s_animationSetByCacheKey.Clear();
                s_animationSetKeyByModelLayout.Clear();
                s_semanticClipByCacheKey.Clear();
                s_tick = 0;
                s_clipHitCount = 0;
                s_clipMissCount = 0;
                s_animationSetHitCount = 0;
                s_animationSetMissCount = 0;
                s_semanticClipHitCount = 0;
                s_semanticClipMissCount = 0;
            }
        }

        // ── Key builders ──────────────────────────────────────────────────────

        internal static string BuildCompatibilityCacheKey(string animationFingerprint, string controllerMapKey)
        {
            if (!string.IsNullOrWhiteSpace(animationFingerprint) &&
                !string.Equals(animationFingerprint, "none", StringComparison.OrdinalIgnoreCase))
            {
                return "fp:" + animationFingerprint;
            }

            return "map:" + (controllerMapKey ?? "<empty>");
        }

        internal static string BuildLoopPolicyKey(string alias, bool shouldLoop)
        {
            string aliasKey = string.IsNullOrWhiteSpace(alias) ? "<empty>" : alias.ToLowerInvariant();
            return $"{LoopPolicyVersion}|alias:{aliasKey}|loop:{(shouldLoop ? 1 : 0)}";
        }

        internal static string BuildClipCacheKey(
            string cafContentHash,
            string alias,
            float importScale,
            string compatibilityKey,
            string pathLayoutHash,
            string loopPolicyKey)
        {
            string aliasKey = string.IsNullOrWhiteSpace(alias) ? "<empty>" : alias.ToLowerInvariant();
            return
                $"v:{ClipBuildVersion}|caf:{cafContentHash}|alias:{aliasKey}|scale:{importScale:R}|compat:{compatibilityKey}|layout:{pathLayoutHash}|loop:{loopPolicyKey}";
        }

        internal static string BuildAnimationSetModelLayoutKey(
            string modelVirtualPath,
            string animationFingerprint,
            string pathLayoutHash,
            float importScale)
        {
            string modelKey = string.IsNullOrWhiteSpace(modelVirtualPath) ? "<none>" : modelVirtualPath.ToLowerInvariant();
            string fpKey = string.IsNullOrWhiteSpace(animationFingerprint) ? "none" : animationFingerprint;
            string layoutKey = string.IsNullOrWhiteSpace(pathLayoutHash) ? "none" : pathLayoutHash;
            return $"model:{modelKey}|fp:{fpKey}|layout:{layoutKey}|scale:{importScale:R}";
        }

        internal static string BuildAnimationSetCacheKey(
            string animationFingerprint,
            string pathLayoutHash,
            string animationSetHash,
            float importScale)
        {
            string fpKey = string.IsNullOrWhiteSpace(animationFingerprint) ? "none" : animationFingerprint;
            string layoutKey = string.IsNullOrWhiteSpace(pathLayoutHash) ? "none" : pathLayoutHash;
            string setKey = string.IsNullOrWhiteSpace(animationSetHash) ? "none" : animationSetHash;
            return $"v:{AnimationSetVersion}|fp:{fpKey}|layout:{layoutKey}|set:{setKey}|scale:{importScale:R}|clip:{ClipBuildVersion}";
        }

        internal static string BuildSemanticClipCacheKey(
            string cafContentHash,
            string alias,
            float importScale,
            string loopPolicyKey)
        {
            string aliasKey = string.IsNullOrWhiteSpace(alias) ? "<empty>" : alias.ToLowerInvariant();
            string loopKey = string.IsNullOrWhiteSpace(loopPolicyKey) ? "<none>" : loopPolicyKey;
            return $"v:{SemanticClipVersion}|caf:{cafContentHash}|alias:{aliasKey}|scale:{importScale:R}|loop:{loopKey}";
        }

        internal static string ExtractAnimationSetHashFromCacheKey(string cacheKey)
        {
            if (string.IsNullOrEmpty(cacheKey))
                return "none";

            const string marker = "|set:";
            int start = cacheKey.IndexOf(marker, StringComparison.Ordinal);
            if (start < 0)
                return "none";
            start += marker.Length;
            int end = cacheKey.IndexOf("|scale:", start, StringComparison.Ordinal);
            if (end < 0 || end <= start)
                end = cacheKey.Length;
            return cacheKey.Substring(start, end - start);
        }

        // ── Semantic clip cache ───────────────────────────────────────────────

        internal static bool TryGetCachedSemanticClip(string key, out SemanticClipData data)
        {
            data = null;
            if (string.IsNullOrEmpty(key))
                return false;

            lock (s_sync)
            {
                if (!s_semanticClipByCacheKey.TryGetValue(key, out var cached) || cached?.Data == null)
                {
                    s_semanticClipMissCount++;
                    return false;
                }

                s_semanticClipHitCount++;
                cached.LastAccessTick = ++s_tick;
                data = cached.Data;
                return true;
            }
        }

        internal static void StoreCachedSemanticClip(string key, SemanticClipData data)
        {
            if (string.IsNullOrEmpty(key) || data == null)
                return;

            lock (s_sync)
            {
                s_semanticClipByCacheKey[key] = new CachedSemanticClipEntry
                {
                    Data = data,
                    LastAccessTick = ++s_tick
                };
                EvictOldestSemanticClipEntriesUnsafe();
            }
        }

        // ── Bound clip cache ──────────────────────────────────────────────────

        internal static bool TryGetCachedClip(string key, out AnimationClip clip)
        {
            clip = null;
            lock (s_sync)
            {
                if (!s_clipByCacheKey.TryGetValue(key, out var cached))
                {
                    s_clipMissCount++;
                    return false;
                }

                s_clipHitCount++;
                cached.LastAccessTick = ++s_tick;
                clip = cached.Clip;
                return clip != null;
            }
        }

        internal static void StoreCachedClip(string key, AnimationClip clip)
        {
            if (clip == null)
                return;

            lock (s_sync)
            {
                s_clipByCacheKey[key] = new CachedClipEntry
                {
                    Clip = clip,
                    LastAccessTick = ++s_tick
                };
                EvictOldestClipEntriesUnsafe();
            }
        }

        // ── Animation set cache ───────────────────────────────────────────────

        internal static bool TryGetAnimationSetKeyForModelLayout(string modelLayoutKey, out string animationSetCacheKey)
        {
            animationSetCacheKey = null;
            if (string.IsNullOrEmpty(modelLayoutKey))
                return false;

            lock (s_sync)
            {
                if (!s_animationSetKeyByModelLayout.TryGetValue(modelLayoutKey, out var cached) || cached == null)
                {
                    s_animationSetKeyByModelLayout.Remove(modelLayoutKey);
                    return false;
                }

                if (string.IsNullOrEmpty(cached.AnimationSetCacheKey) ||
                    !s_animationSetByCacheKey.ContainsKey(cached.AnimationSetCacheKey))
                {
                    s_animationSetKeyByModelLayout.Remove(modelLayoutKey);
                    return false;
                }

                cached.LastAccessTick = ++s_tick;
                animationSetCacheKey = cached.AnimationSetCacheKey;
                return true;
            }
        }

        internal static void StoreAnimationSetKeyForModelLayout(string modelLayoutKey, string animationSetCacheKey)
        {
            if (string.IsNullOrEmpty(modelLayoutKey) || string.IsNullOrEmpty(animationSetCacheKey))
                return;

            lock (s_sync)
            {
                s_animationSetKeyByModelLayout[modelLayoutKey] = new CachedModelLayoutLink
                {
                    AnimationSetCacheKey = animationSetCacheKey,
                    LastAccessTick = ++s_tick
                };
                EvictModelLayoutLinksUnsafe();
            }
        }

        internal static void InvalidateAnimationSetKeyForModelLayout(string modelLayoutKey, string expectedAnimationSetCacheKey = null)
        {
            if (string.IsNullOrEmpty(modelLayoutKey))
                return;

            lock (s_sync)
            {
                if (!s_animationSetKeyByModelLayout.TryGetValue(modelLayoutKey, out var cached) || cached == null)
                    return;

                if (!string.IsNullOrEmpty(expectedAnimationSetCacheKey) &&
                    !string.Equals(cached.AnimationSetCacheKey, expectedAnimationSetCacheKey, StringComparison.Ordinal))
                {
                    return;
                }

                s_animationSetKeyByModelLayout.Remove(modelLayoutKey);
            }
        }

        internal static bool TryGetCachedAnimationSet(string key, out CachedAnimationSetEntry entry)
        {
            entry = null;
            if (string.IsNullOrEmpty(key))
                return false;

            lock (s_sync)
            {
                if (!s_animationSetByCacheKey.TryGetValue(key, out var cached))
                {
                    s_animationSetMissCount++;
                    return false;
                }

                if (cached?.Clips == null || cached.Clips.Length == 0 || HasInvalidClipReference(cached.Clips))
                {
                    s_animationSetByCacheKey.Remove(key);
                    s_animationSetMissCount++;
                    return false;
                }

                s_animationSetHitCount++;
                cached.LastAccessTick = ++s_tick;
                entry = cached;
                return true;
            }
        }

        internal static void StoreCachedAnimationSet(
            string key,
            List<CgfRuntimeAnimationClip> clips,
            int existingSourceCount,
            int missingControllerTrackCount)
        {
            if (string.IsNullOrEmpty(key) || clips == null || clips.Count == 0)
                return;

            var snapshot = new CgfRuntimeAnimationClip[clips.Count];
            for (int i = 0; i < clips.Count; i++)
            {
                var src = clips[i];
                if (src == null || src.Clip == null)
                    continue;

                snapshot[i] = new CgfRuntimeAnimationClip
                {
                    Alias = src.Alias,
                    SourceVirtualPath = src.SourceVirtualPath,
                    Clip = src.Clip
                };
            }

            lock (s_sync)
            {
                s_animationSetByCacheKey[key] = new CachedAnimationSetEntry
                {
                    Clips = snapshot,
                    ExistingSourceCount = existingSourceCount,
                    MissingControllerTrackCount = missingControllerTrackCount,
                    LastAccessTick = ++s_tick
                };
                EvictOldestAnimationSetEntriesUnsafe();
            }
        }

        internal static List<CgfRuntimeAnimationClip> CloneCachedAnimationSet(CachedAnimationSetEntry entry)
        {
            var list = new List<CgfRuntimeAnimationClip>(entry?.Clips?.Length ?? 0);
            if (entry?.Clips == null)
                return list;

            for (int i = 0; i < entry.Clips.Length; i++)
            {
                var src = entry.Clips[i];
                if (src?.Clip == null)
                    continue;

                list.Add(new CgfRuntimeAnimationClip
                {
                    Alias = src.Alias,
                    SourceVirtualPath = src.SourceVirtualPath,
                    Clip = src.Clip
                });
            }

            return list;
        }

        // ── LRU eviction (must be called under s_sync) ────────────────────────

        static void EvictOldestAnimationSetEntriesUnsafe()
        {
            while (s_animationSetByCacheKey.Count > MaxAnimationSetEntries)
            {
                string oldest = null;
                long oldestTick = long.MaxValue;
                foreach (var kv in s_animationSetByCacheKey)
                {
                    if (kv.Value.LastAccessTick < oldestTick)
                    {
                        oldestTick = kv.Value.LastAccessTick;
                        oldest = kv.Key;
                    }
                }

                if (oldest == null)
                    break;
                s_animationSetByCacheKey.Remove(oldest);
            }

            EvictModelLayoutLinksUnsafe();
        }

        static void EvictModelLayoutLinksUnsafe()
        {
            if (s_animationSetKeyByModelLayout.Count == 0)
                return;

            // First remove stale links to missing animation-set entries.
            var stale = new List<string>();
            foreach (var kv in s_animationSetKeyByModelLayout)
            {
                var link = kv.Value;
                if (link == null ||
                    string.IsNullOrEmpty(link.AnimationSetCacheKey) ||
                    !s_animationSetByCacheKey.ContainsKey(link.AnimationSetCacheKey))
                {
                    stale.Add(kv.Key);
                }
            }

            for (int i = 0; i < stale.Count; i++)
                s_animationSetKeyByModelLayout.Remove(stale[i]);

            while (s_animationSetKeyByModelLayout.Count > MaxAnimationSetModelLinks)
            {
                string oldest = null;
                long oldestTick = long.MaxValue;
                foreach (var kv in s_animationSetKeyByModelLayout)
                {
                    long tick = kv.Value != null ? kv.Value.LastAccessTick : long.MinValue;
                    if (tick < oldestTick)
                    {
                        oldestTick = tick;
                        oldest = kv.Key;
                    }
                }

                if (oldest == null)
                    break;
                s_animationSetKeyByModelLayout.Remove(oldest);
            }
        }

        static void EvictOldestSemanticClipEntriesUnsafe()
        {
            while (s_semanticClipByCacheKey.Count > MaxSemanticClipEntries)
            {
                string oldest = null;
                long oldestTick = long.MaxValue;
                foreach (var kv in s_semanticClipByCacheKey)
                {
                    if (kv.Value.LastAccessTick < oldestTick)
                    {
                        oldestTick = kv.Value.LastAccessTick;
                        oldest = kv.Key;
                    }
                }

                if (oldest == null)
                    break;
                s_semanticClipByCacheKey.Remove(oldest);
            }
        }

        static void EvictOldestClipEntriesUnsafe()
        {
            while (s_clipByCacheKey.Count > MaxClipEntries)
            {
                string oldest = null;
                long oldestTick = long.MaxValue;
                foreach (var kv in s_clipByCacheKey)
                {
                    if (kv.Value.LastAccessTick < oldestTick)
                    {
                        oldestTick = kv.Value.LastAccessTick;
                        oldest = kv.Key;
                    }
                }

                if (oldest == null)
                    break;

                if (s_clipByCacheKey.TryGetValue(oldest, out var entry) && entry?.Clip != null)
                    DestroyUnityObject(entry.Clip);
                s_clipByCacheKey.Remove(oldest);
            }
        }

        static bool HasInvalidClipReference(CgfRuntimeAnimationClip[] clips)
        {
            for (int i = 0; i < clips.Length; i++)
            {
                if (clips[i] == null || clips[i].Clip == null)
                    return true;
            }

            return false;
        }

        static void DestroyUnityObject(UnityEngine.Object obj)
        {
            if (obj == null)
                return;

            if (Application.isPlaying)
                UnityEngine.Object.Destroy(obj);
            else
                UnityEngine.Object.DestroyImmediate(obj);
        }
    }
}
