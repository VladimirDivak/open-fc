using System.Collections.Generic;
using UnityEngine;

namespace OpenFarCry.Importer.Cgf
{
    // Maps (cgfVirtualPath, materialChunkTableIndex, overrideName?) → pre-baked Unity Material asset.
    // Populated at editor build time; used at runtime to skip material creation from scratch.
    //
    // SharedFallback lets a per-level manifest delegate to a global shared manifest when the
    // lookup misses locally — runtime first checks the level entries, then the fallback chain.
    //
    // TextureNames carries the resolved virtual texture paths so runtime injection can run
    // without re-parsing the CGF chunk on a hit.
    [CreateAssetMenu(menuName = "OpenFarCry/Material Manifest", fileName = "FcMaterialManifest")]
    public sealed class FcMaterialManifest : ScriptableObject
    {
        [System.Serializable]
        public struct TextureSet
        {
            public string Diffuse;
            public string Normal;
            public string Specular;
            public string Opacity;
            public string Gloss;
        }

        [System.Serializable]
        public struct Entry
        {
            public string VirtualPath;
            public int TableIndex;
            // Empty (or null) = default material with no override. Otherwise = sanitized
            // override name from brush.lst / entity XML / vegetation type. Lookup with a
            // non-empty overrideName first tries to match an override entry, then falls
            // back to the default entry (same vpath+tableIndex with empty OverrideName).
            public string OverrideName;
            // Raw CGF material chunk name. Lets a chunk-name-keyed consumer (e.g. the
            // runtime FcLevelMaterialOverrideService, which works off Material.name rather
            // than vpath/tableIndex) find the baked override material.
            public string ChunkName;
            public Material Material;
            public TextureSet Textures;
        }

        [SerializeField] Entry[] _entries;

        // Optional chain — when a key isn't in _entries, the lookup falls through to this
        // manifest. Typically points from a Levels/{lvl}/manifest.asset to the global
        // shared_manifest.asset.
        [SerializeField] FcMaterialManifest _sharedFallback;

        Dictionary<string, Entry> _lookup;

        public FcMaterialManifest SharedFallback => _sharedFallback;

        public void SetSharedFallback(FcMaterialManifest fallback) => _sharedFallback = fallback;

        void OnEnable() => BuildLookup();

        void BuildLookup()
        {
            _lookup = new Dictionary<string, Entry>(System.StringComparer.Ordinal);
            if (_entries == null)
                return;
            for (int i = 0; i < _entries.Length; i++)
            {
                var e = _entries[i];
                if (!string.IsNullOrEmpty(e.VirtualPath) && e.Material != null)
                    _lookup[MakeKey(e.VirtualPath, e.TableIndex, e.OverrideName)] = e;
            }
        }

        // Tries the override entry first (if overrideName is non-empty), then the default
        // entry, then the SharedFallback chain. Returns false only when no manifest in the
        // chain has a match.
        public bool TryGetEntry(string virtualPath, int tableIndex, string overrideName, out Entry entry)
        {
            if (_lookup == null)
                BuildLookup();

            if (!string.IsNullOrEmpty(overrideName) &&
                _lookup.TryGetValue(MakeKey(virtualPath, tableIndex, overrideName), out entry))
                return true;

            if (_lookup.TryGetValue(MakeKey(virtualPath, tableIndex, null), out entry))
                return true;

            if (_sharedFallback != null)
                return _sharedFallback.TryGetEntry(virtualPath, tableIndex, overrideName, out entry);

            entry = default;
            return false;
        }

        public bool TryGet(string virtualPath, int tableIndex, out Material material)
        {
            if (TryGetEntry(virtualPath, tableIndex, null, out var entry))
            {
                material = entry.Material;
                return true;
            }
            material = null;
            return false;
        }

        public Material Get(string virtualPath, int tableIndex)
            => TryGet(virtualPath, tableIndex, out var m) ? m : null;

        // Called by editor bake service to populate entries.
        public void SetEntries(Entry[] entries)
        {
            _entries = entries;
            BuildLookup();
        }

        public int EntryCount => _entries?.Length ?? 0;

        // Read-only view of baked entries. Lets callers iterate without SerializedObject.
        public IReadOnlyList<Entry> Entries => _entries ?? System.Array.Empty<Entry>();

        // Normalized lookup key. OverrideName is lower-cased so brush.lst / entity XML
        // casing differences don't shatter the lookup; empty/null collapse to "".
        static string MakeKey(string virtualPath, int tableIndex, string overrideName)
        {
            string ov = string.IsNullOrEmpty(overrideName)
                ? string.Empty
                : overrideName.ToLowerInvariant();
            return string.Concat(virtualPath, "|", tableIndex.ToString(), "|", ov);
        }
    }
}
