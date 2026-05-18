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

            CgfRuntimeImporter.MaterialService.ProjectMaterialLookup =
                CgfMaterialManifestLookup.Build(new[] { _manifest });
        }

        void OnDisable()
        {
            CgfRuntimeImporter.MaterialService.ProjectMaterialLookup = null;
        }
    }
}
