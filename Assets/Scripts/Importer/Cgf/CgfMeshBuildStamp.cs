using UnityEngine;

namespace OpenFarCry.Importer.Cgf
{
    // Records the CgfMeshBuilder version a cached prefab's geometry was built with.
    // Lets CgfAssetCacheService validate cache freshness without overloading mesh.name
    // as a version stamp, so meshes can keep their original (CGF-derived) names.
    [DisallowMultipleComponent]
    public sealed class CgfMeshBuildStamp : MonoBehaviour
    {
        [SerializeField] string _meshBuilderVersion;

        public string MeshBuilderVersion => _meshBuilderVersion;

        public void SetVersion(string version) => _meshBuilderVersion = version;
    }
}
