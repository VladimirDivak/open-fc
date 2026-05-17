using OpenFarCry.Importer.Cgf;
using UnityEditor;
using UnityEngine;

namespace OpenFarCry.Importer.Editor
{
    // Wires pre-baked project material manifests into CgfRuntimeImporter.MaterialService.
    // At editor startup and after asset imports, loads all FcMaterialManifest assets and
    // builds a combined lookup. Runtime CGF imports reuse project .mat assets and only
    // inject textures instead of building materials from scratch.
    [InitializeOnLoad]
    static class CgfMaterialProjectCacheInit
    {
        static CgfMaterialProjectCacheInit()
        {
            RefreshFromManifests();
            AssetDatabase.importPackageCompleted += _ => RefreshFromManifests();
        }

        [InitializeOnLoadMethod]
        static void RegisterPostProcessorHook()
        {
            // Re-register after domain reload caused by asset import/recompile.
            RefreshFromManifests();
        }

        static void RefreshFromManifests()
        {
            const string searchFolder = "Assets/FCData/Materials";
            if (!AssetDatabase.IsValidFolder(searchFolder))
            {
                CgfRuntimeImporter.MaterialService.ProjectMaterialLookup = null;
                return;
            }

            var guids = AssetDatabase.FindAssets("t:FcMaterialManifest", new[] { searchFolder });
            if (guids.Length == 0)
            {
                CgfRuntimeImporter.MaterialService.ProjectMaterialLookup = null;
                return;
            }

            var combined = new System.Collections.Generic.Dictionary<string, Material>(
                System.StringComparer.Ordinal);

            for (int i = 0; i < guids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                var manifest = AssetDatabase.LoadAssetAtPath<Cgf.FcMaterialManifest>(path);
                if (manifest == null)
                    continue;

                // Enumerate entries via TryGet: iterate by loading entries indirectly.
                // FcMaterialManifest exposes TryGet(path, index) but not iteration,
                // so we load entries via SerializedObject to populate our combined dict.
                var so = new SerializedObject(manifest);
                var arr = so.FindProperty("_entries");
                if (arr == null || !arr.isArray)
                    continue;

                for (int j = 0; j < arr.arraySize; j++)
                {
                    var elem = arr.GetArrayElementAtIndex(j);
                    string virtualPath = elem.FindPropertyRelative("VirtualPath")?.stringValue;
                    int tableIndex = elem.FindPropertyRelative("TableIndex")?.intValue ?? -1;
                    var matProp = elem.FindPropertyRelative("Material");
                    var mat = matProp?.objectReferenceValue as Material;

                    if (string.IsNullOrEmpty(virtualPath) || tableIndex < 0 || mat == null)
                        continue;

                    string key = string.Concat(virtualPath, "|", tableIndex.ToString());
                    combined[key] = mat;
                }
            }

            if (combined.Count == 0)
            {
                CgfRuntimeImporter.MaterialService.ProjectMaterialLookup = null;
                return;
            }

            CgfRuntimeImporter.MaterialService.ProjectMaterialLookup = (virtualPath, tableIndex) =>
            {
                string key = string.Concat(virtualPath, "|", tableIndex.ToString());
                return combined.TryGetValue(key, out var mat) ? mat : null;
            };
        }
    }
}
