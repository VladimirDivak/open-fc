using System.Collections.Generic;
using OpenFarCry.Importer.Cgf;
using UnityEditor;

namespace OpenFarCry.Importer.Editor
{
    // Wires pre-baked project material manifests into CgfRuntimeImporter.MaterialService.
    // At editor startup and after asset imports, loads all FcMaterialManifest assets and
    // builds a combined lookup. Runtime CGF imports reuse project .mat assets and only
    // inject textures instead of building materials from scratch.
    //
    // Editor-only: in a player build the per-level FcLevelMaterialManifestBinding component
    // populates ProjectMaterialLookup instead.
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
            var service = CgfRuntimeImporter.MaterialService;

            // Scan both the new Assets/Materials root and the legacy Assets/FCData/Materials
            // root so unbaked-yet-in-new-layout levels keep working during the migration.
            var searchRoots = CgfMaterialEditorBakeService.GetManifestSearchRoots();
            if (searchRoots.Length == 0)
            {
                service.ProjectMaterialLookup = null;
                service.ProjectMaterialEntryLookup = null;
                return;
            }

            var guids = AssetDatabase.FindAssets("t:FcMaterialManifest", searchRoots);
            if (guids.Length == 0)
            {
                service.ProjectMaterialLookup = null;
                service.ProjectMaterialEntryLookup = null;
                return;
            }

            var manifests = new List<FcMaterialManifest>(guids.Length);
            for (int i = 0; i < guids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                var manifest = AssetDatabase.LoadAssetAtPath<FcMaterialManifest>(path);
                if (manifest != null)
                    manifests.Add(manifest);
            }

            service.ProjectMaterialEntryLookup = CgfMaterialManifestLookup.BuildEntryLookup(manifests);
            service.ProjectMaterialLookup = CgfMaterialManifestLookup.Build(manifests);
        }
    }
}
