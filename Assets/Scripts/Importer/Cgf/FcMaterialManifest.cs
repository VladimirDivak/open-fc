using System.Collections.Generic;
using UnityEngine;

namespace OpenFarCry.Importer.Cgf
{
    // Maps (cgfVirtualPath, materialChunkTableIndex) → pre-baked Unity Material asset.
    // Populated at editor build time; used at runtime to skip material creation from scratch.
    [CreateAssetMenu(menuName = "OpenFarCry/Material Manifest", fileName = "FcMaterialManifest")]
    public sealed class FcMaterialManifest : ScriptableObject
    {
        [System.Serializable]
        public struct Entry
        {
            public string VirtualPath;
            public int TableIndex;
            public Material Material;
        }

        [SerializeField] Entry[] _entries;

        Dictionary<string, Material> _lookup;

        void OnEnable() => BuildLookup();

        void BuildLookup()
        {
            _lookup = new Dictionary<string, Material>(System.StringComparer.Ordinal);
            if (_entries == null)
                return;
            for (int i = 0; i < _entries.Length; i++)
            {
                ref var e = ref _entries[i];
                if (!string.IsNullOrEmpty(e.VirtualPath) && e.Material != null)
                    _lookup[MakeKey(e.VirtualPath, e.TableIndex)] = e.Material;
            }
        }

        public bool TryGet(string virtualPath, int tableIndex, out Material material)
        {
            if (_lookup == null)
                BuildLookup();
            return _lookup.TryGetValue(MakeKey(virtualPath, tableIndex), out material);
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

        static string MakeKey(string virtualPath, int tableIndex)
            => string.Concat(virtualPath, "|", tableIndex.ToString());
    }
}
