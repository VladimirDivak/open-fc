using UnityEngine;

namespace OpenFarCry.Level.Services
{
    [DisallowMultipleComponent]
    public sealed class FcCachedGeometryMetadata : MonoBehaviour
    {
        [SerializeField] int _cacheFormatVersion = 1;
        [SerializeField] bool _brushRuntimeParity;

        public int CacheFormatVersion => _cacheFormatVersion;
        public bool BrushRuntimeParity => _brushRuntimeParity;

        public void SetMetadata(int cacheFormatVersion, bool brushRuntimeParity)
        {
            _cacheFormatVersion = cacheFormatVersion;
            _brushRuntimeParity = brushRuntimeParity;
        }
    }
}
