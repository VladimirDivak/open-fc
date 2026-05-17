using System.Collections.Generic;
using OpenFarCry.Level.Data;
using OpenFarCry.Level.Services;
using UnityEngine;

namespace OpenFarCry.Level.Entities
{
    // Base for all Far Cry level entity placeholders.
    // Stores non-copyrighted metadata (IDs, class names, paths as strings).
    // Subclasses load actual resources from PAK at runtime via Start().
    public class FcEntity : MonoBehaviour
    {
        [Header("Level Source")]
        [SerializeField] string _entityClass;
        [SerializeField] int _entityId;
        [SerializeField] string _layerName;
        [SerializeField] bool _hiddenInGame;
        [Header("Source Properties")]
        [SerializeField] string[] _propertyKeys   = System.Array.Empty<string>();
        [SerializeField] string[] _propertyValues = System.Array.Empty<string>();

        public string EntityClass => _entityClass;
        public int EntityId       => _entityId;
        public string LayerName   => _layerName;
        public bool HiddenInGame  => _hiddenInGame;

        public bool TryGetProperty(string key, out string value)
        {
            if (_propertyKeys != null)
                for (int i = 0; i < _propertyKeys.Length; i++)
                    if (string.Equals(_propertyKeys[i], key, System.StringComparison.OrdinalIgnoreCase))
                    { value = _propertyValues[i]; return true; }
            value = null;
            return false;
        }

        protected FcLevelResourceService ResourceService => FcLevelResourceService.Current;

        protected virtual void Awake()
        {
            if (_hiddenInGame)
                gameObject.SetActive(false);
        }

        // Called by FcLevelSceneBuilder (editor-side) to populate serialized fields.
        public virtual void SetData(FcEntityDesc desc)
        {
            _entityClass  = desc.EntityClass;
            _entityId     = desc.Id;
            _layerName    = desc.Layer;
            _hiddenInGame = desc.HiddenInGame;
            StoreProperties(desc.Properties);
        }

        void StoreProperties(Dictionary<string, string> props)
        {
            if (props == null || props.Count == 0)
            {
                _propertyKeys   = System.Array.Empty<string>();
                _propertyValues = System.Array.Empty<string>();
                return;
            }
            _propertyKeys   = new string[props.Count];
            _propertyValues = new string[props.Count];
            int i = 0;
            foreach (var kv in props) { _propertyKeys[i] = kv.Key; _propertyValues[i++] = kv.Value; }
        }

        // Also used for <Object> entries (TagPoint, Respawn, etc.).
        public virtual void SetData(FcObjectDesc desc)
        {
            _entityClass = desc.Type;
            _entityId    = 0;
            _layerName   = string.Empty;
        }
    }
}
