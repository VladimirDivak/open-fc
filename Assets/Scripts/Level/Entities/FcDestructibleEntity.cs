using OpenFarCry.Level.Data;
using UnityEngine;

namespace OpenFarCry.Level.Entities
{
    public class FcDestructibleEntity : FcMeshEntity
    {
        [Header("Destructible")]
        [SerializeField] float _health = 100f;
        [SerializeField] float _damage = 0f;
        [SerializeField] bool  _active = true;

        public override void SetData(FcEntityDesc desc)
        {
            base.SetData(desc);
            if (TryGetFloat(desc, "fHealth", out float h)) _health = h;
            if (TryGetFloat(desc, "fDamage", out float d)) _damage = d;
            if (desc.Properties.TryGetValue("bActive", out string a)) _active = a != "0";
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
