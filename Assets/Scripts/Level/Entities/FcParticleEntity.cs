using OpenFarCry.Level.Data;
using UnityEngine;

namespace OpenFarCry.Level.Entities
{
    [RequireComponent(typeof(ParticleSystem))]
    public class FcParticleEntity : FcEntity
    {
        [Header("Particle")]
        [SerializeField] string _effectName;
        [SerializeField] bool   _loop   = true;
        [SerializeField] float  _scale  = 1f;
        [SerializeField] bool   _active = true;

        protected override void Awake()
        {
            base.Awake();
            GetComponent<ParticleSystem>().Stop(
                withChildren: true,
                stopBehavior: ParticleSystemStopBehavior.StopEmittingAndClear);
        }

        public override void SetData(FcEntityDesc desc)
        {
            base.SetData(desc);

            if (desc.Properties.TryGetValue("effectName", out string en) && !string.IsNullOrEmpty(en))
                _effectName = en;
            else if (desc.Properties.TryGetValue("ParticleEffect", out string pe) && !string.IsNullOrEmpty(pe))
                _effectName = pe;

            if (TryGetFloat(desc, "fScale", out float s)) _scale = s;
            if (desc.Properties.TryGetValue("bLoop", out string l))
                _loop = l == "1" || l.Equals("true", System.StringComparison.OrdinalIgnoreCase);
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
