using OpenFarCry.Level.Data;
using UnityEngine;

namespace OpenFarCry.Level.Entities
{
    public class FcEnvironmentEntity : FcEntity
    {
        [Header("Fog")]
        [SerializeField] Color _fogColor = Color.grey;
        [SerializeField] float _fogStart = 200f;
        [SerializeField] float _fogEnd   = 900f;

        [Header("View Distance")]
        [SerializeField] float _viewDistance = 900f;

        [Header("Rising Water")]
        [SerializeField] float _targetHeight = 0f;
        [SerializeField] float _riseSpeed    = 0f;

        [Header("Storm")]
        [SerializeField] float _stormIntensity = 0f;

        public override void SetData(FcEntityDesc desc)
        {
            base.SetData(desc);

            TryGetColor(desc, "fogColor", out _fogColor);
            if (TryGetFloat(desc, "fogStart",       out float fs)) _fogStart      = fs;
            if (TryGetFloat(desc, "fogEnd",         out float fe)) _fogEnd        = fe;
            if (TryGetFloat(desc, "viewDistance",   out float vd)) _viewDistance  = vd;
            if (TryGetFloat(desc, "targetHeight",   out float th)) _targetHeight  = th;
            if (TryGetFloat(desc, "riseSpeed",      out float rs)) _riseSpeed     = rs;
            if (TryGetFloat(desc, "stormIntensity", out float si)) _stormIntensity = si;
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
    }
}
