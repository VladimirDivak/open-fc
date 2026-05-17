using System;
using System.Collections.Generic;
using OpenFarCry.Level.Data;
using UnityEngine;

namespace OpenFarCry.Level.Entities
{
    // Lightweight marker for runtime-spawned entity placeholders.
    // Stores identity, model path, and full property payload without loading geometry.
    [DisallowMultipleComponent]
    public sealed class FcEntityStub : MonoBehaviour
    {
        [SerializeField] string _entityClass;
        [SerializeField] int    _entityId;
        [SerializeField] string _layerName;
        [SerializeField] string _modelPath;
        [SerializeField] string[] _propertyKeys   = Array.Empty<string>();
        [SerializeField] string[] _propertyValues = Array.Empty<string>();

        public string EntityClass => _entityClass;
        public int    EntityId    => _entityId;
        public string LayerName   => _layerName;
        public string ModelPath   => _modelPath;

        public bool TryGetProperty(string key, out string value)
        {
            if (_propertyKeys != null)
                for (int i = 0; i < _propertyKeys.Length; i++)
                    if (string.Equals(_propertyKeys[i], key, StringComparison.OrdinalIgnoreCase))
                    { value = _propertyValues[i]; return true; }
            value = null;
            return false;
        }

        // Editor build path (called by FcLevelSceneBuilder).
        public void SetData(FcEntityDesc desc)
        {
            _entityClass = desc.EntityClass;
            _entityId    = desc.Id;
            _layerName   = desc.Layer;
            _modelPath   = desc.GetModelVirtualPath() ?? string.Empty;
            StoreProperties(desc.Properties);
        }

        // Runtime spawn path (called without SerializedObject).
        public void Initialize(FcEntityDesc desc)
        {
            _entityClass = desc.EntityClass;
            _entityId    = desc.Id;
            _layerName   = desc.Layer;
            _modelPath   = desc.GetModelVirtualPath() ?? string.Empty;
            StoreProperties(desc.Properties);
        }

        void StoreProperties(Dictionary<string, string> props)
        {
            if (props == null || props.Count == 0)
            {
                _propertyKeys   = Array.Empty<string>();
                _propertyValues = Array.Empty<string>();
                return;
            }
            _propertyKeys   = new string[props.Count];
            _propertyValues = new string[props.Count];
            int i = 0;
            foreach (var kv in props) { _propertyKeys[i] = kv.Key; _propertyValues[i++] = kv.Value; }
        }
    }
}
