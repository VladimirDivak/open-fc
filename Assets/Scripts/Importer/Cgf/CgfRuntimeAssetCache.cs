using System;
using System.Collections.Generic;
using UnityEngine;

namespace OpenFarCry.Importer.Cgf
{
    public sealed class CgfRuntimeAssetCache
    {
        public readonly struct RuntimeModelArtifact
        {
            public readonly CgfFile ParsedFile;
            public readonly BuildResult BuildResult;
            public readonly string ParsedCacheKey;

            public RuntimeModelArtifact(CgfFile parsedFile, BuildResult buildResult, string parsedCacheKey)
            {
                ParsedFile = parsedFile;
                BuildResult = buildResult;
                ParsedCacheKey = parsedCacheKey;
            }
        }

        public readonly struct Stats
        {
            public readonly int ParsedEntryCount;
            public readonly int ModelEntryCount;
            public readonly int ParsedTotalRefCount;
            public readonly int ModelTotalRefCount;
            public readonly int ScopedParsedKeyCount;
            public readonly int ScopedModelKeyCount;

            public Stats(
                int parsedEntryCount,
                int modelEntryCount,
                int parsedTotalRefCount,
                int modelTotalRefCount,
                int scopedParsedKeyCount,
                int scopedModelKeyCount)
            {
                ParsedEntryCount = parsedEntryCount;
                ModelEntryCount = modelEntryCount;
                ParsedTotalRefCount = parsedTotalRefCount;
                ModelTotalRefCount = modelTotalRefCount;
                ScopedParsedKeyCount = scopedParsedKeyCount;
                ScopedModelKeyCount = scopedModelKeyCount;
            }
        }

        sealed class ParsedEntry
        {
            public readonly CgfFile ParsedFile;
            public readonly HashSet<string> Scopes = new HashSet<string>(StringComparer.Ordinal);
            public int RefCount;

            public ParsedEntry(CgfFile parsedFile)
            {
                ParsedFile = parsedFile;
                RefCount = 1;
            }
        }

        sealed class ModelEntry
        {
            public readonly RuntimeModelArtifact Artifact;
            public readonly HashSet<string> Scopes = new HashSet<string>(StringComparer.Ordinal);
            public int RefCount;

            public ModelEntry(RuntimeModelArtifact artifact)
            {
                Artifact = artifact;
                RefCount = 1;
            }
        }

        readonly object _sync = new object();
        readonly Dictionary<string, ParsedEntry> _parsedByKey = new Dictionary<string, ParsedEntry>(StringComparer.Ordinal);
        readonly Dictionary<string, ModelEntry> _modelsByKey = new Dictionary<string, ModelEntry>(StringComparer.Ordinal);
        readonly Dictionary<string, HashSet<string>> _parsedKeysByScope = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        readonly Dictionary<string, HashSet<string>> _modelKeysByScope = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);

        public int ParsedEntryCount
        {
            get
            {
                lock (_sync) return _parsedByKey.Count;
            }
        }

        public int ModelEntryCount
        {
            get
            {
                lock (_sync) return _modelsByKey.Count;
            }
        }

        public bool TryRetainParsed(string key, string scopeId, out CgfFile parsedFile)
        {
            parsedFile = null;
            if (string.IsNullOrEmpty(key))
                return false;

            lock (_sync)
            {
                if (!_parsedByKey.TryGetValue(key, out var entry))
                    return false;

                entry.RefCount++;
                AttachParsedScopeUnsafe(scopeId, key, entry);
                parsedFile = entry.ParsedFile;
                return true;
            }
        }

        public void StoreParsed(string key, CgfFile parsedFile, string scopeId = null)
        {
            if (string.IsNullOrEmpty(key))
                throw new ArgumentException("Cache key is null or empty.", nameof(key));
            if (parsedFile == null)
                throw new ArgumentNullException(nameof(parsedFile));

            lock (_sync)
            {
                if (_parsedByKey.TryGetValue(key, out var existing))
                {
                    existing.RefCount++;
                    AttachParsedScopeUnsafe(scopeId, key, existing);
                    return;
                }

                var created = new ParsedEntry(parsedFile);
                _parsedByKey[key] = created;
                AttachParsedScopeUnsafe(scopeId, key, created);
            }
        }

        public bool TryRetainModel(string key, string scopeId, out RuntimeModelArtifact artifact)
        {
            artifact = default;
            if (string.IsNullOrEmpty(key))
                return false;

            lock (_sync)
            {
                if (!_modelsByKey.TryGetValue(key, out var entry))
                    return false;

                entry.RefCount++;
                AttachModelScopeUnsafe(scopeId, key, entry);
                artifact = entry.Artifact;
                return true;
            }
        }

        public void StoreModel(string key, RuntimeModelArtifact artifact, string scopeId = null)
        {
            if (string.IsNullOrEmpty(key))
                throw new ArgumentException("Cache key is null or empty.", nameof(key));
            if (artifact.BuildResult == null)
                throw new ArgumentNullException(nameof(artifact.BuildResult));

            lock (_sync)
            {
                if (_modelsByKey.TryGetValue(key, out var existing))
                {
                    existing.RefCount++;
                    AttachModelScopeUnsafe(scopeId, key, existing);
                    return;
                }

                var created = new ModelEntry(artifact);
                _modelsByKey[key] = created;
                AttachModelScopeUnsafe(scopeId, key, created);
            }
        }

        public void ReleaseParsed(string key)
        {
            if (string.IsNullOrEmpty(key))
                return;

            lock (_sync)
            {
                if (!_parsedByKey.TryGetValue(key, out var entry))
                    return;

                if (entry.RefCount > 0)
                    entry.RefCount--;
            }
        }

        public void ReleaseModel(string key)
        {
            if (string.IsNullOrEmpty(key))
                return;

            lock (_sync)
            {
                if (!_modelsByKey.TryGetValue(key, out var entry))
                    return;

                if (entry.RefCount > 0)
                    entry.RefCount--;
            }
        }

        // Releases every asset a level scope owns. The scope holds one ref per
        // key; releasing it drops that ref, and once a key has no scope left it
        // is freed immediately — regardless of any leaked per-instance refs.
        // Level unload always reclaims everything that level loaded.
        public void ReleaseLevelScope(string scopeId)
        {
            if (string.IsNullOrWhiteSpace(scopeId))
                return;

            lock (_sync)
            {
                if (_parsedKeysByScope.TryGetValue(scopeId, out var parsedKeys))
                {
                    foreach (var key in parsedKeys)
                    {
                        if (!_parsedByKey.TryGetValue(key, out var entry))
                            continue;

                        entry.Scopes.Remove(scopeId);
                        if (entry.RefCount > 0)
                            entry.RefCount--;

                        if (entry.Scopes.Count == 0)
                        {
                            entry.ParsedFile?.Dispose();
                            _parsedByKey.Remove(key);
                        }
                    }

                    _parsedKeysByScope.Remove(scopeId);
                }

                if (_modelKeysByScope.TryGetValue(scopeId, out var modelKeys))
                {
                    foreach (var key in modelKeys)
                    {
                        if (!_modelsByKey.TryGetValue(key, out var entry))
                            continue;

                        entry.Scopes.Remove(scopeId);
                        if (entry.RefCount > 0)
                            entry.RefCount--;

                        if (entry.Scopes.Count == 0)
                        {
                            DisposeModelEntryUnsafe(entry);
                            _modelsByKey.Remove(key);
                        }
                    }

                    _modelKeysByScope.Remove(scopeId);
                }
            }
        }

        public int TrimUnused()
        {
            lock (_sync)
            {
                int removed = 0;
                var deleteParsedKeys = new List<string>();
                foreach (var kv in _parsedByKey)
                {
                    if (kv.Value.RefCount <= 0)
                        deleteParsedKeys.Add(kv.Key);
                }

                for (int i = 0; i < deleteParsedKeys.Count; i++)
                {
                    string key = deleteParsedKeys[i];
                    if (_parsedByKey.TryGetValue(key, out var entry))
                    {
                        entry.ParsedFile?.Dispose();
                        RemoveKeyFromScopesUnsafe(key, entry.Scopes, _parsedKeysByScope);
                    }

                    _parsedByKey.Remove(key);
                    removed++;
                }

                var deleteModelKeys = new List<string>();
                foreach (var kv in _modelsByKey)
                {
                    if (kv.Value.RefCount <= 0)
                        deleteModelKeys.Add(kv.Key);
                }

                for (int i = 0; i < deleteModelKeys.Count; i++)
                {
                    string key = deleteModelKeys[i];
                    if (_modelsByKey.TryGetValue(key, out var modelEntry))
                    {
                        DisposeModelEntryUnsafe(modelEntry);
                        RemoveKeyFromScopesUnsafe(key, modelEntry.Scopes, _modelKeysByScope);
                    }

                    _modelsByKey.Remove(key);
                    removed++;
                }

                return removed;
            }
        }

        public void ClearRuntimeCache()
        {
            lock (_sync)
            {
                foreach (var parsedEntry in _parsedByKey.Values)
                    parsedEntry.ParsedFile?.Dispose();

                foreach (var modelEntry in _modelsByKey.Values)
                    DisposeModelEntryUnsafe(modelEntry);

                _parsedByKey.Clear();
                _modelsByKey.Clear();
                _parsedKeysByScope.Clear();
                _modelKeysByScope.Clear();
            }
        }

        // Disposes the Allocator.Persistent NativeArrays held by every cached parsed
        // CgfFile, without destroying built meshes. Built meshes are UnityEngine.Objects
        // referenced by the live scene; only the native parsed data must be freed before a
        // domain reload, which would otherwise orphan it and trip the leak detector.
        public void DisposeParsedNativeData()
        {
            lock (_sync)
            {
                foreach (var parsedEntry in _parsedByKey.Values)
                    parsedEntry.ParsedFile?.Dispose();

                foreach (var modelEntry in _modelsByKey.Values)
                    modelEntry.Artifact.ParsedFile?.Dispose();

                _parsedByKey.Clear();
                _modelsByKey.Clear();
                _parsedKeysByScope.Clear();
                _modelKeysByScope.Clear();
            }
        }

        public Stats GetStats()
        {
            lock (_sync)
            {
                int parsedRefs = 0;
                foreach (var entry in _parsedByKey.Values)
                    parsedRefs += entry.RefCount;

                int modelRefs = 0;
                foreach (var entry in _modelsByKey.Values)
                    modelRefs += entry.RefCount;

                int scopedParsedKeys = 0;
                foreach (var set in _parsedKeysByScope.Values)
                    scopedParsedKeys += set.Count;

                int scopedModelKeys = 0;
                foreach (var set in _modelKeysByScope.Values)
                    scopedModelKeys += set.Count;

                return new Stats(
                    parsedEntryCount: _parsedByKey.Count,
                    modelEntryCount: _modelsByKey.Count,
                    parsedTotalRefCount: parsedRefs,
                    modelTotalRefCount: modelRefs,
                    scopedParsedKeyCount: scopedParsedKeys,
                    scopedModelKeyCount: scopedModelKeys);
            }
        }

        void AttachParsedScopeUnsafe(string scopeId, string key, ParsedEntry entry)
        {
            if (string.IsNullOrWhiteSpace(scopeId))
                return;

            // The scope holds exactly one ref per key. Bump only on first attach
            // so repeated retains within one scope don't inflate its share — and
            // ReleaseLevelScope's single decrement always matches.
            if (entry.Scopes.Add(scopeId))
                entry.RefCount++;

            if (!_parsedKeysByScope.TryGetValue(scopeId, out var keys))
            {
                keys = new HashSet<string>(StringComparer.Ordinal);
                _parsedKeysByScope[scopeId] = keys;
            }

            keys.Add(key);
        }

        void AttachModelScopeUnsafe(string scopeId, string key, ModelEntry entry)
        {
            if (string.IsNullOrWhiteSpace(scopeId))
                return;

            // The scope holds exactly one ref per key. See AttachParsedScopeUnsafe.
            if (entry.Scopes.Add(scopeId))
                entry.RefCount++;

            if (!_modelKeysByScope.TryGetValue(scopeId, out var keys))
            {
                keys = new HashSet<string>(StringComparer.Ordinal);
                _modelKeysByScope[scopeId] = keys;
            }

            keys.Add(key);
        }

        // Removes a key from exactly the scopes the entry recorded, instead of
        // scanning every scope. The entry's own Scopes set is the authoritative
        // index, so this is O(scopes-of-entry), not O(all-scopes).
        static void RemoveKeyFromScopesUnsafe(
            string key,
            HashSet<string> entryScopes,
            Dictionary<string, HashSet<string>> keysByScope)
        {
            if (entryScopes == null || entryScopes.Count == 0)
                return;

            foreach (var scopeId in entryScopes)
            {
                if (!keysByScope.TryGetValue(scopeId, out var keys))
                    continue;

                keys.Remove(key);
                if (keys.Count == 0)
                    keysByScope.Remove(scopeId);
            }
        }

        static void DisposeModelEntryUnsafe(ModelEntry modelEntry)
        {
            var buildResult = modelEntry.Artifact.BuildResult;
            if (buildResult == null)
                return;

            DestroyMesh(buildResult.Mesh);
            DestroyMesh(buildResult.ColliderMesh);
        }

        static void DestroyMesh(Mesh mesh)
        {
            if (mesh == null)
                return;

            if (Application.isPlaying)
                UnityEngine.Object.Destroy(mesh);
            else
                UnityEngine.Object.DestroyImmediate(mesh);
        }
    }
}
