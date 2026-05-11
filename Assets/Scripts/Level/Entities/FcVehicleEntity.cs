using OpenFarCry.Importer.Cgf;
using OpenFarCry.Level.Data;
using UnityEngine;

namespace OpenFarCry.Level.Entities
{
    public class FcVehicleEntity : FcMeshEntity
    {
        [Header("Vehicle")]
        [SerializeField] float _health    = 500f;
        [SerializeField] float _maxSpeed  = 15f;
        [SerializeField] bool  _enterable = true;
        [SerializeField] float _maxForce  = 1000f;
        [SerializeField] float _mass      = 1200f;

        protected override void ApplyResult(CgfRuntimeImportResult result)
        {
            base.ApplyResult(result);
            var rb = gameObject.AddComponent<Rigidbody>();
            rb.mass = _mass;
            // Non-convex MeshCollider + dynamic Rigidbody is unsupported in Unity.
            foreach (var col in GetComponentsInChildren<MeshCollider>())
                col.convex = true;
        }

        public override void SetData(FcEntityDesc desc)
        {
            base.SetData(desc);
            if (TryGetFloat(desc, "fHealth",   out float h)) _health   = h;
            if (TryGetFloat(desc, "fMaxSpeed", out float s)) _maxSpeed = s;
            if (TryGetFloat(desc, "fMaxForce", out float f)) _maxForce = f;
            if (TryGetFloat(desc, "mass",      out float m)) _mass     = m;
            if (desc.Properties.TryGetValue("bEnterable", out string e))
                _enterable = e != "0";
        }

        static bool TryGetFloat(FcEntityDesc desc, string key, out float value)
        {
            value = 0f;
            return desc.Properties.TryGetValue(key, out string s) &&
                   float.TryParse(s, System.Globalization.NumberStyles.Float,
                       System.Globalization.CultureInfo.InvariantCulture, out value);
        }
    }
}
