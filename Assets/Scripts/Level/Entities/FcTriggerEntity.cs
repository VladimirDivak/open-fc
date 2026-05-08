using OpenFarCry.Level.Data;
using UnityEngine;

namespace OpenFarCry.Level.Entities
{
    // ProximityTrigger / AreaTrigger / Shape / AreaBox entity.
    // BoxCollider (isTrigger=true) is pre-added on the prefab.
    // For Shape/AreaBox with custom geometry the collider dimensions are set in Awake.
    [RequireComponent(typeof(BoxCollider))]
    public class FcTriggerEntity : FcEntity
    {
        [Header("Trigger")]
        [SerializeField] Vector3 _dimensions = Vector3.one;
        [SerializeField] Vector3[] _shapePoints;
        [SerializeField] bool _enabled = true;

        protected override void Awake()
        {
            base.Awake();
            ApplyCollider();
        }

        void ApplyCollider()
        {
            var box = GetComponent<BoxCollider>();
            box.isTrigger = true;
            box.enabled = _enabled;
            box.size = _dimensions;
        }

        public override void SetData(FcEntityDesc desc)
        {
            base.SetData(desc);

            if (TryGetFloat(desc, "DimX", out float dx) &&
                TryGetFloat(desc, "DimY", out float dy) &&
                TryGetFloat(desc, "DimZ", out float dz))
            {
                // Cry Z-up → Unity Y-up dimension swap
                _dimensions = new Vector3(dx, dz, dy);
            }
            else if (TryGetFloat(desc, "Width", out float w) &&
                     TryGetFloat(desc, "Height", out float h) &&
                     TryGetFloat(desc, "Length", out float l))
            {
                _dimensions = new Vector3(w, h, l);
            }

            if (desc.Properties.TryGetValue("bActive", out string active))
                _enabled = active != "0";
        }

        public override void SetData(FcObjectDesc desc)
        {
            base.SetData(desc);

            if (desc.ShapePoints != null && desc.ShapePoints.Length > 0)
                _shapePoints = desc.ShapePoints;

            if (desc.AreaBoxDims != Vector3.zero)
                _dimensions = desc.AreaBoxDims;
        }

        static bool TryGetFloat(FcEntityDesc desc, string key, out float value)
        {
            value = 0f;
            return desc.Properties.TryGetValue(key, out string s) &&
                   float.TryParse(s, System.Globalization.NumberStyles.Float,
                       System.Globalization.CultureInfo.InvariantCulture, out value);
        }

#if UNITY_EDITOR
        void OnValidate() => ApplyCollider();
#endif
    }
}
