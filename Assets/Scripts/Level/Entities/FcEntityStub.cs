using OpenFarCry.Level.Data;
using UnityEngine;

namespace OpenFarCry.Level.Entities
{
    // Lightweight metadata marker placed on pre-positioned GameObjects.
    // Used when the mesh is loaded at runtime but the transform and entity identity
    // are already baked into the scene at editor build time.
    [DisallowMultipleComponent]
    public sealed class FcEntityStub : MonoBehaviour
    {
        [SerializeField] string _entityClass;
        [SerializeField] int    _entityId;
        [SerializeField] string _layerName;

        public string EntityClass => _entityClass;
        public int    EntityId    => _entityId;
        public string LayerName   => _layerName;

        public void SetData(FcEntityDesc desc)
        {
            _entityClass = desc.EntityClass;
            _entityId    = desc.Id;
            _layerName   = desc.Layer;
        }
    }
}
