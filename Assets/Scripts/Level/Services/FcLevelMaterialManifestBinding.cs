using OpenFarCry.Importer.Cgf;
using UnityEngine;

namespace OpenFarCry.Level.Services
{
    // Carries a baked FcMaterialManifest reference inside a built level scene so that
    // a player build can reuse pre-baked .mat assets. On enable it installs a
    // ProjectMaterialLookup on the shared runtime material service; on disable it
    // clears it. The editor-only CgfMaterialProjectCacheInit covers non-play imports.
    [DisallowMultipleComponent]
    public sealed class FcLevelMaterialManifestBinding : MonoBehaviour
    {
        [SerializeField] FcMaterialManifest _manifest;

        public FcMaterialManifest Manifest => _manifest;

        public void SetManifest(FcMaterialManifest manifest) => _manifest = manifest;

        void OnEnable()
        {
            if (_manifest == null)
                return;

            var service = CgfRuntimeImporter.MaterialService;
            var manifests = new[] { _manifest };
            service.ProjectMaterialEntryLookup = CgfMaterialManifestLookup.BuildEntryLookup(manifests);
            service.ProjectMaterialLookup = CgfMaterialManifestLookup.Build(manifests);

            // Feed the manifest to the override service so it can overlay baked,
            // user-editable shader knobs onto resolved override materials.
            if (FcLevelMaterialOverrideService.Current != null)
                FcLevelMaterialOverrideService.Current.SetBakedOverrideManifest(_manifest);
        }

        void OnDisable()
        {
            var service = CgfRuntimeImporter.MaterialService;
            service.ProjectMaterialLookup = null;
            service.ProjectMaterialEntryLookup = null;
        }
    }
}
