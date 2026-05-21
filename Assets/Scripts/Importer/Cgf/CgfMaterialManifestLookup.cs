using System;
using System.Collections.Generic;
using UnityEngine;

namespace OpenFarCry.Importer.Cgf
{
    // Builds a (cgfVirtualPath, materialChunkTableIndex[, overrideName]) → manifest entry
    // lookup from one or more FcMaterialManifest assets. Shared by the editor-wide manifest
    // scan and the per-level runtime binding so both produce identical lookup behavior.
    //
    // Lookup priority within each manifest entry:
    //   1. (vpath, tableIndex, overrideName) when overrideName is non-empty
    //   2. (vpath, tableIndex, "") default entry
    //   3. SharedFallback chain (FcMaterialManifest.TryGetEntry handles it internally)
    public static class CgfMaterialManifestLookup
    {
        public static Func<string, int, Material> Build(IEnumerable<FcMaterialManifest> manifests)
        {
            var entryLookup = BuildEntryLookup(manifests);
            if (entryLookup == null)
                return null;

            return (virtualPath, tableIndex) =>
                entryLookup(virtualPath, tableIndex, null) is { } entry ? entry.Material : null;
        }

        // Entry-aware lookup. Returns null when no manifest has a match; otherwise the
        // resolved Entry (which carries Material + Textures + OverrideName).
        public static Func<string, int, string, FcMaterialManifest.Entry?> BuildEntryLookup(
            IEnumerable<FcMaterialManifest> manifests)
        {
            if (manifests == null)
                return null;

            var collected = new List<FcMaterialManifest>();
            foreach (var manifest in manifests)
            {
                if (manifest != null)
                    collected.Add(manifest);
            }

            if (collected.Count == 0)
                return null;

            return (virtualPath, tableIndex, overrideName) =>
            {
                for (int i = 0; i < collected.Count; i++)
                {
                    if (collected[i].TryGetEntry(virtualPath, tableIndex, overrideName, out var entry))
                        return entry;
                }
                return null;
            };
        }
    }
}
