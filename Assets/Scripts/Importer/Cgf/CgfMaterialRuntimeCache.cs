using System;
using System.Collections.Generic;
using UnityEngine;

namespace OpenFarCry.Importer.Cgf
{
    // In-memory material cache keyed by material name (lower-case).
    // Supports level-scope tracking: materials can be tagged with a scopeId at creation/lookup,
    // and ReleaseLevelScope decrements refcounts for all materials in that scope.
    // TrimUnused destroys materials with refcount <= 0. Thread-unsafe: call from main thread only.
    public sealed class CgfMaterialRuntimeCache
    {
        sealed class Entry
        {
            public Material Material;
            public int RefCount;
            public readonly HashSet<string> Scopes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        readonly Dictionary<string, Entry> _materials =
            new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);
        readonly Dictionary<string, HashSet<string>> _keysByScope =
            new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);

        public Material GetOrCreate(string key, string scopeId, Func<Material> factory)
        {
            // RefCount tracks the number of distinct scopes holding the material, so it
            // stays balanced against ReleaseLevelScope (one decrement per scope). Repeated
            // GetOrCreate calls in the same scope must not inflate it.
            if (_materials.TryGetValue(key, out var entry) && entry.Material != null)
            {
                if (AttachScope(scopeId, key, entry))
                    entry.RefCount++;
                return entry.Material;
            }

            var mat = factory();
            if (mat == null)
                return null;

            // RefCount starts at 1 for the creating scope (or as a persistent global
            // material when scopeId is null). AttachScope must not add another count.
            var created = new Entry { Material = mat, RefCount = 1 };
            _materials[key] = created;
            AttachScope(scopeId, key, created);
            return mat;
        }

        public Material GetOrCreate(string key, Func<Material> factory)
            => GetOrCreate(key, null, factory);

        public void ReleaseLevelScope(string scopeId)
        {
            if (string.IsNullOrWhiteSpace(scopeId))
                return;

            if (!_keysByScope.TryGetValue(scopeId, out var keys))
                return;

            foreach (string key in keys)
            {
                if (!_materials.TryGetValue(key, out var entry))
                    continue;
                entry.Scopes.Remove(scopeId);
                if (entry.RefCount > 0)
                    entry.RefCount--;
            }

            _keysByScope.Remove(scopeId);
        }

        public int TrimUnused()
        {
            var toRemove = new List<string>();
            foreach (var pair in _materials)
            {
                if (pair.Value == null || pair.Value.RefCount <= 0 || pair.Value.Material == null)
                    toRemove.Add(pair.Key);
            }

            for (int i = 0; i < toRemove.Count; i++)
            {
                if (_materials.TryGetValue(toRemove[i], out var entry))
                    RemoveEntry(toRemove[i], entry);
            }

            return toRemove.Count;
        }

        public void Clear()
        {
            foreach (var entry in _materials.Values)
                if (entry?.Material != null)
                    DestroyMaterial(entry.Material);
            _materials.Clear();
            _keysByScope.Clear();
        }

        public int Count => _materials.Count;

        // Returns true when scopeId was newly attached to the entry (caller should bump RefCount).
        bool AttachScope(string scopeId, string key, Entry entry)
        {
            if (string.IsNullOrWhiteSpace(scopeId) || entry == null)
                return false;

            if (!entry.Scopes.Add(scopeId))
                return false;

            if (!_keysByScope.TryGetValue(scopeId, out var keys))
            {
                keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                _keysByScope[scopeId] = keys;
            }
            keys.Add(key);
            return true;
        }

        void RemoveEntry(string key, Entry entry)
        {
            if (entry.Material != null)
                DestroyMaterial(entry.Material);

            foreach (string scopeId in entry.Scopes)
            {
                if (_keysByScope.TryGetValue(scopeId, out var keys))
                {
                    keys.Remove(key);
                    if (keys.Count == 0)
                        _keysByScope.Remove(scopeId);
                }
            }

            _materials.Remove(key);
        }

        static void DestroyMaterial(Material mat)
        {
            if (mat == null)
                return;
            if (Application.isPlaying)
                UnityEngine.Object.Destroy(mat);
            else
                UnityEngine.Object.DestroyImmediate(mat);
        }
    }
}
