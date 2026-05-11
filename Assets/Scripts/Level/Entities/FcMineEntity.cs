using OpenFarCry.Level.Data;
using UnityEngine;

namespace OpenFarCry.Level.Entities
{
    [RequireComponent(typeof(SphereCollider))]
    public class FcMineEntity : FcMeshEntity
    {
        [Header("Mine")]
        [SerializeField] float _damage          = 200f;
        [SerializeField] float _radius          = 2f;
        [SerializeField] bool  _active          = true;
        [SerializeField] float _detonationDelay = 0f;

        protected override void Awake()
        {
            base.Awake();
            ApplyCollider();
        }

        void ApplyCollider()
        {
            var sc = GetComponent<SphereCollider>();
            sc.isTrigger = true;
            sc.radius    = _radius;
            sc.enabled   = _active;
        }

        public override void SetData(FcEntityDesc desc)
        {
            base.SetData(desc);
            if (TryGetFloat(desc, "fDamage",         out float d))  _damage          = d;
            if (TryGetFloat(desc, "fRadius",          out float r))  _radius          = r;
            if (TryGetFloat(desc, "fDetonationDelay", out float dd)) _detonationDelay = dd;
            if (desc.Properties.TryGetValue("bActive", out string a)) _active = a != "0";
        }

        static bool TryGetFloat(FcEntityDesc desc, string key, out float value)
        {
            value = 0f;
            return desc.Properties.TryGetValue(key, out string s) &&
                   float.TryParse(s, System.Globalization.NumberStyles.Float,
                       System.Globalization.CultureInfo.InvariantCulture, out value);
        }

#if UNITY_EDITOR
        void OnValidate()
        {
            if (GetComponent<SphereCollider>() is { } sc)
            {
                sc.radius  = _radius;
                sc.enabled = _active;
            }
        }
#endif
    }
}
