using UnityEngine;

namespace OpenFarCry.Level.Services
{
    [DisallowMultipleComponent]
    public sealed class FcCachedGeometryMetadata : MonoBehaviour
    {
        [SerializeField] int _cacheFormatVersion = 1;
        [SerializeField] bool _brushRuntimeParity;
        [SerializeField] int[] _submeshMaterialIds;

        public int CacheFormatVersion => _cacheFormatVersion;
        public bool BrushRuntimeParity => _brushRuntimeParity;
        public int[] SubmeshMaterialIds => _submeshMaterialIds;

        public void SetMetadata(int cacheFormatVersion, bool brushRuntimeParity, int[] submeshMaterialIds = null)
        {
            _cacheFormatVersion = cacheFormatVersion;
            _brushRuntimeParity = brushRuntimeParity;
            _submeshMaterialIds = submeshMaterialIds != null
                ? (int[])submeshMaterialIds.Clone()
                : null;
        }
    }
}
