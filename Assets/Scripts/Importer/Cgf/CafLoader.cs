using System;
using System.Collections.Generic;
using System.Linq;
using OpenFarCry.FileSystem;
using UnityEngine;

namespace OpenFarCry.Importer.Cgf
{
    // Loads, parses, and caches CafFile instances.
    // Two-level cache: path → bytes hash (fast revalidation) + content hash → parsed CafFile (semantic dedup).
    internal static class CafLoader
    {
        public readonly struct Stats
        {
            public readonly int PathEntryCount;
            public readonly int SemanticEntryCount;
            public readonly int PathHitCount;
            public readonly int PathMissCount;
            public readonly int SemanticHitCount;
            public readonly int SemanticMissCount;

            public int TotalHitCount => PathHitCount + SemanticHitCount;

            public Stats(
                int pathEntries, int semanticEntries,
                int pathHits, int pathMisses,
                int semanticHits, int semanticMisses)
            {
                PathEntryCount = pathEntries;
                SemanticEntryCount = semanticEntries;
                PathHitCount = pathHits;
                PathMissCount = pathMisses;
                SemanticHitCount = semanticHits;
                SemanticMissCount = semanticMisses;
            }
        }

        sealed class CachedPathEntry
        {
            public CafFile Caf;
            public string SourceBytesHash;
            public string ContentHash;
            public long LastAccessTick;
        }

        sealed class CachedSemanticEntry
        {
            public CafFile Caf;
            public long LastAccessTick;
        }

        static readonly object s_sync = new object();
        static readonly Dictionary<string, CachedPathEntry> s_byPath =
            new Dictionary<string, CachedPathEntry>(StringComparer.OrdinalIgnoreCase);
        static readonly Dictionary<string, CachedSemanticEntry> s_bySemantic =
            new Dictionary<string, CachedSemanticEntry>(StringComparer.Ordinal);
        static readonly Dictionary<string, string> s_contentHashBySource =
            new Dictionary<string, string>(StringComparer.Ordinal);

        static long s_tick;
        static int s_pathHits;
        static int s_pathMisses;
        static int s_semanticHits;
        static int s_semanticMisses;

        const int MaxPathEntries = 1024;
        const int MaxSemanticEntries = 256;

        internal static CafFile GetOrParse(string virtualPath, out string contentHash)
        {
            byte[] bytes = FcFileSystem.ReadAllBytes(virtualPath);
            string sourceBytesHash = ComputeBytesHash(bytes);

            lock (s_sync)
            {
                if (s_byPath.TryGetValue(virtualPath, out var cached) &&
                    string.Equals(cached.SourceBytesHash, sourceBytesHash, StringComparison.Ordinal))
                {
                    s_pathHits++;
                    cached.LastAccessTick = ++s_tick;
                    contentHash = cached.ContentHash;
                    return cached.Caf;
                }
                s_pathMisses++;

                if (s_contentHashBySource.TryGetValue(sourceBytesHash, out var knownContentHash) &&
                    !string.IsNullOrEmpty(knownContentHash) &&
                    s_bySemantic.TryGetValue(knownContentHash, out var cachedSemantic) &&
                    cachedSemantic?.Caf != null)
                {
                    s_semanticHits++;
                    cachedSemantic.LastAccessTick = ++s_tick;
                    contentHash = knownContentHash;
                    s_byPath[virtualPath] = new CachedPathEntry
                    {
                        Caf = cachedSemantic.Caf,
                        SourceBytesHash = sourceBytesHash,
                        ContentHash = contentHash,
                        LastAccessTick = s_tick
                    };
                    EvictOldestUnsafe();
                    return cachedSemantic.Caf;
                }
            }

            var parsed = CafParser.Parse(bytes);
            contentHash = ComputeSemanticHash(parsed);

            lock (s_sync)
            {
                if (s_bySemantic.TryGetValue(contentHash, out var existingSemantic) &&
                    existingSemantic?.Caf != null)
                {
                    s_semanticHits++;
                    existingSemantic.LastAccessTick = ++s_tick;
                    s_contentHashBySource[sourceBytesHash] = contentHash;
                    s_byPath[virtualPath] = new CachedPathEntry
                    {
                        Caf = existingSemantic.Caf,
                        SourceBytesHash = sourceBytesHash,
                        ContentHash = contentHash,
                        LastAccessTick = s_tick
                    };
                    EvictOldestUnsafe();
                    return existingSemantic.Caf;
                }

                s_semanticMisses++;
                s_bySemantic[contentHash] = new CachedSemanticEntry
                {
                    Caf = parsed,
                    LastAccessTick = ++s_tick
                };
                s_contentHashBySource[sourceBytesHash] = contentHash;
                s_byPath[virtualPath] = new CachedPathEntry
                {
                    Caf = parsed,
                    SourceBytesHash = sourceBytesHash,
                    ContentHash = contentHash,
                    LastAccessTick = s_tick
                };
                EvictOldestUnsafe();
            }

            return parsed;
        }

        internal static Stats GetStats()
        {
            lock (s_sync)
                return new Stats(s_byPath.Count, s_bySemantic.Count,
                    s_pathHits, s_pathMisses, s_semanticHits, s_semanticMisses);
        }

        internal static void Clear()
        {
            lock (s_sync)
            {
                s_byPath.Clear();
                s_bySemantic.Clear();
                s_contentHashBySource.Clear();
                s_tick = 0;
                s_pathHits = 0;
                s_pathMisses = 0;
                s_semanticHits = 0;
                s_semanticMisses = 0;
            }
        }

        // Must be called under s_sync.
        static void EvictOldestUnsafe()
        {
            while (s_byPath.Count > MaxPathEntries)
            {
                string oldest = null;
                long oldestTick = long.MaxValue;
                foreach (var kv in s_byPath)
                {
                    if (kv.Value.LastAccessTick < oldestTick)
                    {
                        oldestTick = kv.Value.LastAccessTick;
                        oldest = kv.Key;
                    }
                }
                if (oldest == null) break;
                s_byPath.Remove(oldest);
            }

            while (s_bySemantic.Count > MaxSemanticEntries)
            {
                string oldest = null;
                long oldestTick = long.MaxValue;
                foreach (var kv in s_bySemantic)
                {
                    if (kv.Value.LastAccessTick < oldestTick)
                    {
                        oldestTick = kv.Value.LastAccessTick;
                        oldest = kv.Key;
                    }
                }
                if (oldest == null) break;
                s_bySemantic.Remove(oldest);
            }
        }

        // FNV-1a 64-bit over normalized CAF content — reuse is possible even when
        // byte-level packaging differs but track data is semantically identical.
        static string ComputeSemanticHash(CafFile caf)
        {
            if (caf == null)
                return "0";

            unchecked
            {
                const ulong fnvOffset = 14695981039346656037UL;
                ulong hash = fnvOffset;

                HashInt(ref hash, Quantize(caf.SecsPerTick, 1000000f));
                HashInt(ref hash, caf.GlobalStartTick);
                HashInt(ref hash, caf.GlobalEndTick);
                int trackCount = caf.Tracks != null ? caf.Tracks.Count : 0;
                HashInt(ref hash, trackCount);

                if (trackCount <= 0)
                    return hash.ToString("X16");

                var ordered = caf.Tracks
                    .Where(t => t != null)
                    .OrderBy(t => t.ControllerID)
                    .ToArray();
                HashInt(ref hash, ordered.Length);

                for (int ti = 0; ti < ordered.Length; ti++)
                {
                    var track = ordered[ti];
                    HashInt(ref hash, unchecked((int)track.ControllerID));

                    int keyCount = 0;
                    if (track.Ticks != null && track.Positions != null && track.Rotations != null)
                        keyCount = Mathf.Min(track.Ticks.Length, track.Positions.Length, track.Rotations.Length);
                    HashInt(ref hash, keyCount);

                    for (int k = 0; k < keyCount; k++)
                    {
                        HashInt(ref hash, track.Ticks[k]);
                        var p = track.Positions[k];
                        HashInt(ref hash, Quantize(p.x, 100000f));
                        HashInt(ref hash, Quantize(p.y, 100000f));
                        HashInt(ref hash, Quantize(p.z, 100000f));
                        var r = track.Rotations[k];
                        HashInt(ref hash, Quantize(r.x, 100000f));
                        HashInt(ref hash, Quantize(r.y, 100000f));
                        HashInt(ref hash, Quantize(r.z, 100000f));
                        HashInt(ref hash, Quantize(r.w, 100000f));
                    }
                }

                return hash.ToString("X16");

                static int Quantize(float value, float scale) =>
                    Mathf.RoundToInt(value * scale);

                static void HashInt(ref ulong h, int value)
                {
                    const ulong prime = 1099511628211UL;
                    for (int b = 0; b < 4; b++)
                    {
                        h ^= (byte)((value >> (8 * b)) & 0xFF);
                        h *= prime;
                    }
                }
            }
        }

        static string ComputeBytesHash(byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0)
                return "0";

            unchecked
            {
                const ulong fnvOffset = 14695981039346656037UL;
                const ulong fnvPrime = 1099511628211UL;
                ulong hash = fnvOffset;
                for (int i = 0; i < bytes.Length; i++)
                {
                    hash ^= bytes[i];
                    hash *= fnvPrime;
                }
                return hash.ToString("X16");
            }
        }
    }
}
