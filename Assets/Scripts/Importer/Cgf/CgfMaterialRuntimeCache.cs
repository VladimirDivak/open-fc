using System;
using System.Collections.Generic;
using UnityEngine;

namespace OpenFarCry.Importer.Cgf
{
    // In-memory material cache keyed by material name (lower-case).
    // No ref-counting: materials are lightweight and live until Clear() is called.
    public sealed class CgfMaterialRuntimeCache
    {
        readonly Dictionary<string, Material> _materials = new Dictionary<string, Material>();

        public Material GetOrCreate(string key, Func<Material> factory)
        {
            if (_materials.TryGetValue(key, out var mat) && mat != null)
                return mat;
            mat = factory();
            _materials[key] = mat;
            return mat;
        }

        public void Clear()
        {
            foreach (var mat in _materials.Values)
                if (mat != null)
                    UnityEngine.Object.Destroy(mat);
            _materials.Clear();
        }

        public int Count => _materials.Count;
    }
}
