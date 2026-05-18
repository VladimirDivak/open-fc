using OpenFarCry.Level.Data;
using UnityEngine;

namespace OpenFarCry.Level.Entities
{
    // DynamicLight entity. All data is serialized; no PAK resource loading needed.
    [RequireComponent(typeof(Light))]
    public class FcLightEntity : FcEntity
    {
        [Header("Light")]
        [SerializeField] LightType _lightType = LightType.Point;
        [SerializeField] Color _lightColor = Color.white;
        [SerializeField] float _range = 5f;
        [SerializeField] float _intensity = 1f;
        [SerializeField] float _spotAngle = 30f;

        void ApplyLight()
        {
            var l = GetComponent<Light>();
            l.type = _lightType;
            l.color = _lightColor;
            l.range = _range;
            l.intensity = _intensity;
            l.spotAngle = _spotAngle;
        }

        public override void SetData(FcEntityDesc desc)
        {
            base.SetData(desc);

            if (TryGetFloat(desc, "Radius", out float radius)) _range = radius;
            if (TryGetFloat(desc, "fRadius", out float fradius)) _range = fradius;
            if (TryGetFloat(desc, "fDiffuseMult", out float dm)) _intensity = dm;

            if (TryGetColor(desc, "clrDiffuse", out Color c)) _lightColor = c;
            else if (TryGetColor(desc, "Color", out Color c2)) _lightColor = c2;

            if (TryGetFloat(desc, "fHDRDynamic", out float hdr) && hdr > 0)
                _intensity *= (1f + hdr);
        }

        static bool TryGetFloat(FcEntityDesc desc, string key, out float value)
        {
            value = 0f;
            return desc.Properties.TryGetValue(key, out string s) &&
                   float.TryParse(s, System.Globalization.NumberStyles.Float,
                       System.Globalization.CultureInfo.InvariantCulture, out value);
        }

        static bool TryGetColor(FcEntityDesc desc, string key, out Color color)
        {
            color = Color.white;
            if (!desc.Properties.TryGetValue(key, out string s)) return false;

            // "R,G,B" 0-255 format
            var parts = s.Split(',');
            if (parts.Length >= 3 &&
                float.TryParse(parts[0].Trim(), System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out float r) &&
                float.TryParse(parts[1].Trim(), System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out float g) &&
                float.TryParse(parts[2].Trim(), System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out float b))
            {
                float scale = r > 1f || g > 1f || b > 1f ? 1f / 255f : 1f;
                color = new Color(r * scale, g * scale, b * scale);
                return true;
            }
            return false;
        }

#if UNITY_EDITOR
        void OnValidate() => ApplyLight();
#endif
    }
}
