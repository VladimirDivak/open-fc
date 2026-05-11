using OpenFarCry.Level.Data;
using UnityEngine;

namespace OpenFarCry.Level.Entities
{
    public class FcBoidEntity : FcEntity
    {
        [Header("Boid")]
        [SerializeField] int    _count    = 10;
        [SerializeField] float  _radius   = 5f;
        [SerializeField] float  _minSpeed = 1f;
        [SerializeField] float  _maxSpeed = 5f;
        [SerializeField] string _boidType;

        public override void SetData(FcEntityDesc desc)
        {
            base.SetData(desc);
            if (TryGetInt(desc,   "count",    out int c))   _count    = c;
            if (TryGetFloat(desc, "fRadius",  out float r)) _radius   = r;
            if (TryGetFloat(desc, "fMinSpeed",out float mn)) _minSpeed = mn;
            if (TryGetFloat(desc, "fMaxSpeed",out float mx)) _maxSpeed = mx;
            if (desc.Properties.TryGetValue("Boid_Type", out string bt)) _boidType = bt;
        }

        static bool TryGetFloat(FcEntityDesc desc, string key, out float value)
        {
            value = 0f;
            return desc.Properties.TryGetValue(key, out string s) &&
                   float.TryParse(s, System.Globalization.NumberStyles.Float,
                       System.Globalization.CultureInfo.InvariantCulture, out value);
        }

        static bool TryGetInt(FcEntityDesc desc, string key, out int value)
        {
            value = 0;
            return desc.Properties.TryGetValue(key, out string s) && int.TryParse(s, out value);
        }

#if UNITY_EDITOR
        void OnDrawGizmos()
        {
            Gizmos.color = new Color(0.2f, 0.8f, 1f, 0.3f);
            Gizmos.DrawWireSphere(transform.position, _radius);
        }
#endif
    }
}
