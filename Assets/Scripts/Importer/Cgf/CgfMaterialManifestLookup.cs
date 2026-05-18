using System;
using System.Collections.Generic;
using UnityEngine;

namespace OpenFarCry.Importer.Cgf
{
    // Builds a (cgfVirtualPath, materialChunkTableIndex) -> baked Material lookup from
    // one or more FcMaterialManifest assets. Shared by the editor-wide manifest scan
    // and the per-level runtime binding so both produce identical ProjectMaterialLookup
    // delegates.
    public static class CgfMaterialManifestLookup
    {
        public static Func<string, int, Material> Build(IEnumerable<FcMaterialManifest> manifests)
        {
            if (manifests == null)
                return null;

            var combined = new Dictionary<string, Material>(StringComparer.Ordinal);
            foreach (var manifest in manifests)
            {
                if (manifest == null)
                    continue;

                var entries = manifest.Entries;
                for (int i = 0; i < entries.Count; i++)
                {
                    var e = entries[i];
                    if (string.IsNullOrEmpty(e.VirtualPath) || e.TableIndex < 0 || e.Material == null)
                        continue;
                    combined[MakeKey(e.VirtualPath, e.TableIndex)] = e.Material;
                }
            }

            if (combined.Count == 0)
                return null;

            return (virtualPath, tableIndex) =>
                combined.TryGetValue(MakeKey(virtualPath, tableIndex), out var mat) ? mat : null;
        }

        static string MakeKey(string virtualPath, int tableIndex)
            => string.Concat(virtualPath, "|", tableIndex.ToString());
    }
}
