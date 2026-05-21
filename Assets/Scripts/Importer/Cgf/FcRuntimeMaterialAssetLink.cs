using System.Collections.Generic;
using UnityEngine;

namespace OpenFarCry.Importer.Cgf
{
    // Editor-support bookkeeping: links a runtime Material instance back to the persistent
    // baked .mat asset it was Instantiated from. An editor tool
    // (FcMaterialEditApplyMenu) reads these links to write Play Mode tweaks back into the
    // source assets. No-op in a player build — the registry stays empty so it never leaks.
    public static class FcRuntimeMaterialAssetLink
    {
        static readonly Dictionary<Material, Material> _instanceToAsset =
            new Dictionary<Material, Material>();

        // Records that runtime `instance` was Instantiated from persistent `asset`.
        public static void Register(Material instance, Material asset)
        {
#if UNITY_EDITOR
            if (instance == null || asset == null)
                return;
            _instanceToAsset[instance] = asset;
#endif
        }

        public static IReadOnlyDictionary<Material, Material> Links => _instanceToAsset;

        public static void Clear() => _instanceToAsset.Clear();
    }
}
