using System;
using System.Collections.Generic;
using UnityEngine;

namespace OpenFarCry.Importer.Texture
{
    public sealed class TextureRuntimeScopedCache
    {
        public readonly struct Stats
        {
            public readonly int EntryCount;
            public readonly int TotalRefCount;
            public readonly int ScopedKeyCount;
            public readonly int HitCount;
            public readonly int MissCount;

            public Stats(
                int entryCount,
                int totalRefCount,
                int scopedKeyCount,
                int hitCount,
                int missCount)
            {
                EntryCount = entryCount;
                TotalRefCount = totalRefCount;
                ScopedKeyCount = scopedKeyCount;
                HitCount = hitCount;
                MissCount = missCount;
            }
        }

        sealed class Entry
        {
            public Texture2D Texture;
            public TextureRuntimeImportService.LoadedTextureInfo Info;
            public int RefCount;
            public long LastAccessTick;
            public readonly HashSet<string> Scopes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        const int DefaultMaxEntries = 512;
        readonly Dictionary<string, Entry> _byPath = new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);
        readonly Dictionary<string, HashSet<string>> _keysByScope = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        readonly object _sync = new object();
        int _hitCount;
        int _missCount;
        int _maxEntries = DefaultMaxEntries;
        long _accessTick;

        public bool TryRetain(
            string normalizedVirtualPath,
            string scopeId,
            out Texture2D texture,
            out TextureRuntimeImportService.LoadedTextureInfo info)
        {
            texture = null;
            info = default;
            if (string.IsNullOrWhiteSpace(normalizedVirtualPath))
                return false;

            lock (_sync)
            {
                if (!_byPath.TryGetValue(normalizedVirtualPath, out var entry) || entry.Texture == null)
                {
                    _missCount++;
                    if (entry != null && entry.Texture == null)
                        _byPath.Remove(normalizedVirtualPath);
                    return false;
                }

                entry.RefCount++;
                entry.LastAccessTick = NextAccessTickUnsafe();
                AttachScopeUnsafe(scopeId, normalizedVirtualPath, entry);
                texture = entry.Texture;
                info = entry.Info;
                _hitCount++;
                return true;
            }
        }

        public void Store(
            string normalizedVirtualPath,
            string scopeId,
            Texture2D texture,
            TextureRuntimeImportService.LoadedTextureInfo info)
        {
            if (string.IsNullOrWhiteSpace(normalizedVirtualPath) || texture == null)
                return;

            lock (_sync)
            {
                if (_byPath.TryGetValue(normalizedVirtualPath, out var existing))
                {
                    if (existing.Texture != null && existing.Texture != texture)
                        DestroyTexture(existing.Texture);

                    existing.Texture = texture;
                    existing.Info = info;
                    existing.RefCount++;
                    existing.LastAccessTick = NextAccessTickUnsafe();
                    AttachScopeUnsafe(scopeId, normalizedVirtualPath, existing);
                    TrimToCapacityUnsafe(_maxEntries);
                    return;
                }

                var created = new Entry
                {
                    Texture = texture,
                    Info = info,
                    RefCount = 1,
                    LastAccessTick = NextAccessTickUnsafe()
                };

                _byPath[normalizedVirtualPath] = created;
                AttachScopeUnsafe(scopeId, normalizedVirtualPath, created);
                TrimToCapacityUnsafe(_maxEntries);
            }
        }

        public void Release(string normalizedVirtualPath)
        {
            if (string.IsNullOrWhiteSpace(normalizedVirtualPath))
                return;

            lock (_sync)
            {
                if (_byPath.TryGetValue(normalizedVirtualPath, out var entry) && entry.RefCount > 0)
                    entry.RefCount--;
            }
        }

        public void ReleaseLevelScope(string scopeId)
        {
            if (string.IsNullOrWhiteSpace(scopeId))
                return;

            lock (_sync)
            {
                if (!_keysByScope.TryGetValue(scopeId, out var keys))
                    return;

                foreach (string key in keys)
                {
                    if (!_byPath.TryGetValue(key, out var entry))
                        continue;

                    if (!entry.Scopes.Remove(scopeId))
                        continue;

                    if (entry.RefCount > 0)
                        entry.RefCount--;
                }

                _keysByScope.Remove(scopeId);
            }
        }

        public int TrimUnused()
        {
            lock (_sync)
            {
                return TrimUnusedUnsafe();
            }
        }

        public int TrimToCapacity(int maxEntries)
        {
            maxEntries = Math.Max(1, maxEntries);
            lock (_sync)
            {
                _maxEntries = maxEntries;
                return TrimToCapacityUnsafe(maxEntries);
            }
        }

        public void Clear()
        {
            lock (_sync)
            {
                foreach (var pair in _byPath)
                    if (pair.Value?.Texture != null)
                        DestroyTexture(pair.Value.Texture);

                _byPath.Clear();
                _keysByScope.Clear();
                _hitCount = 0;
                _missCount = 0;
                _accessTick = 0;
            }
        }

        public Stats GetStats()
        {
            lock (_sync)
            {
                int totalRefs = 0;
                foreach (var entry in _byPath.Values)
                    totalRefs += Math.Max(0, entry?.RefCount ?? 0);

                int scopedKeyCount = 0;
                foreach (var keys in _keysByScope.Values)
                    scopedKeyCount += keys.Count;

                return new Stats(
                    entryCount: _byPath.Count,
                    totalRefCount: totalRefs,
                    scopedKeyCount: scopedKeyCount,
                    hitCount: _hitCount,
                    missCount: _missCount);
            }
        }

        void AttachScopeUnsafe(string scopeId, string key, Entry entry)
        {
            if (string.IsNullOrWhiteSpace(scopeId) || entry == null)
                return;

            if (!entry.Scopes.Add(scopeId))
                return;

            if (!_keysByScope.TryGetValue(scopeId, out var keys))
            {
                keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                _keysByScope[scopeId] = keys;
            }

            keys.Add(key);
        }

        int TrimUnusedUnsafe()
        {
            if (_byPath.Count == 0)
                return 0;

            var toRemove = new List<string>();
            foreach (var pair in _byPath)
            {
                if (pair.Value == null || pair.Value.RefCount <= 0 || pair.Value.Texture == null)
                    toRemove.Add(pair.Key);
            }

            for (int i = 0; i < toRemove.Count; i++)
            {
                string key = toRemove[i];
                if (!_byPath.TryGetValue(key, out var entry))
                    continue;

                RemoveEntryUnsafe(key, entry);
            }

            return toRemove.Count;
        }

        int TrimToCapacityUnsafe(int maxEntries)
        {
            int removed = TrimUnusedUnsafe();
            if (_byPath.Count <= maxEntries)
                return removed;

            var candidates = new List<KeyValuePair<string, Entry>>();
            foreach (var pair in _byPath)
            {
                var entry = pair.Value;
                if (entry == null || entry.Texture == null || entry.RefCount > 0)
                    continue;

                candidates.Add(pair);
            }

            candidates.Sort((a, b) => a.Value.LastAccessTick.CompareTo(b.Value.LastAccessTick));

            for (int i = 0; i < candidates.Count && _byPath.Count > maxEntries; i++)
            {
                string key = candidates[i].Key;
                if (!_byPath.TryGetValue(key, out var entry) || entry == null || entry.RefCount > 0)
                    continue;

                RemoveEntryUnsafe(key, entry);
                removed++;
            }

            return removed;
        }

        void RemoveEntryUnsafe(string key, Entry entry)
        {
            if (entry.Texture != null)
                DestroyTexture(entry.Texture);

            foreach (string scopeId in entry.Scopes)
            {
                if (_keysByScope.TryGetValue(scopeId, out var keys))
                {
                    keys.Remove(key);
                    if (keys.Count == 0)
                        _keysByScope.Remove(scopeId);
                }
            }

            _byPath.Remove(key);
        }

        long NextAccessTickUnsafe()
        {
            if (_accessTick == long.MaxValue)
                _accessTick = 0;

            _accessTick++;
            return _accessTick;
        }

        static void DestroyTexture(Texture2D texture)
        {
            if (texture == null)
                return;

            if (Application.isPlaying)
                UnityEngine.Object.Destroy(texture);
            else
                UnityEngine.Object.DestroyImmediate(texture);
        }
    }
}
