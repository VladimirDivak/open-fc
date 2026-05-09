using System.Collections.Generic;
using UnityEngine;

namespace OpenFarCry.Importer.Texture
{
    // In-memory Texture2D cache keyed by normalised virtual path (lower-case, forward slashes).
    // Populated by a DDS parser when one is available; empty until then.
    public sealed class TextureRuntimeCache
    {
        public readonly struct Stats
        {
            public readonly int EntryCount;
            public readonly int HitCount;
            public readonly int MissCount;

            public Stats(int entryCount, int hitCount, int missCount)
            {
                EntryCount = entryCount;
                HitCount = hitCount;
                MissCount = missCount;
            }
        }

        sealed class Entry
        {
            public Texture2D Texture;
            public long LastAccessTick;
        }

        readonly Dictionary<string, Entry> _textures = new Dictionary<string, Entry>();
        readonly int _maxEntries;
        int _hitCount;
        int _missCount;
        long _accessTick;

        public TextureRuntimeCache(int maxEntries = 512)
        {
            _maxEntries = Mathf.Max(1, maxEntries);
        }

        public bool TryGet(string normalizedVirtualPath, out Texture2D texture)
        {
            texture = null;
            if (_textures.TryGetValue(normalizedVirtualPath, out var entry) && entry?.Texture != null)
            {
                entry.LastAccessTick = NextAccessTick();
                texture = entry.Texture;
                _hitCount++;
                return true;
            }

            _missCount++;
            if (entry?.Texture == null)
                _textures.Remove(normalizedVirtualPath);
            return false;
        }

        public void Store(string normalizedVirtualPath, Texture2D texture)
        {
            if (texture == null)
                return;

            if (_textures.TryGetValue(normalizedVirtualPath, out var existing))
            {
                if (existing.Texture != null && existing.Texture != texture)
                    DestroyTexture(existing.Texture);

                existing.Texture = texture;
                existing.LastAccessTick = NextAccessTick();
                TrimToCapacity(_maxEntries);
                return;
            }

            _textures[normalizedVirtualPath] = new Entry
            {
                Texture = texture,
                LastAccessTick = NextAccessTick()
            };
            TrimToCapacity(_maxEntries);
        }

        public int TrimToCapacity(int maxEntries)
        {
            maxEntries = Mathf.Max(1, maxEntries);
            if (_textures.Count <= maxEntries)
                return 0;

            var candidates = new List<KeyValuePair<string, Entry>>(_textures.Count);
            foreach (var pair in _textures)
            {
                if (pair.Value?.Texture == null)
                    continue;
                candidates.Add(pair);
            }

            candidates.Sort((a, b) => a.Value.LastAccessTick.CompareTo(b.Value.LastAccessTick));

            int removed = 0;
            for (int i = 0; i < candidates.Count && _textures.Count > maxEntries; i++)
            {
                string key = candidates[i].Key;
                if (!_textures.TryGetValue(key, out var entry) || entry?.Texture == null)
                    continue;

                DestroyTexture(entry.Texture);
                _textures.Remove(key);
                removed++;
            }

            return removed;
        }

        public void Clear()
        {
            foreach (var entry in _textures.Values)
                if (entry?.Texture != null)
                    DestroyTexture(entry.Texture);
            _textures.Clear();
            _hitCount = 0;
            _missCount = 0;
            _accessTick = 0;
        }

        public int Count => _textures.Count;
        public int HitCount => _hitCount;
        public int MissCount => _missCount;

        public Stats GetStats()
        {
            return new Stats(
                entryCount: _textures.Count,
                hitCount: _hitCount,
                missCount: _missCount);
        }

        long NextAccessTick()
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
            {
                Object.Destroy(texture);
                return;
            }

            Object.DestroyImmediate(texture);
        }
    }
}
