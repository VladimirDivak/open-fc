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

        public string EntityClass => _entityClass;
        public int EntityId       => _entityId;
        public string LayerName   => _layerName;
        public bool HiddenInGame  => _hiddenInGame;

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
