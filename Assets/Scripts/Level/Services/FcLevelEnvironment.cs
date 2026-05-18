using UnityEngine;

namespace OpenFarCry.Level.Services
{
    // Applies level-wide environment settings from the parsed mission XML.
    // FcLevelSceneBuilder populates the fields and calls Apply() at build time;
    // the sun GameObject and RenderSettings are then baked into the saved scene,
    // so no runtime re-application is needed.
    public sealed class FcLevelEnvironment : MonoBehaviour
    {
        [Header("Sun")]
        [SerializeField] Color _sunColor = Color.white;
        [SerializeField] Vector3 _sunDirection = new Vector3(0.25f, -0.65f, 0.72f);
        [SerializeField] float _sunMultiplier = 1.2f;

        [Header("Ambient")]
        [SerializeField] Color _ambientColor = new Color(0.3f, 0.35f, 0.45f);

        [Header("Fog")]
        [SerializeField] Color _fogColor = new Color(0.55f, 0.65f, 0.75f);
        [SerializeField] float _fogStart = 200f;
        [SerializeField] float _fogEnd = 900f;

        public Color SunColor   { get => _sunColor;   set => _sunColor = value; }
        public Vector3 SunDirection { get => _sunDirection; set => _sunDirection = value; }
        public float SunMultiplier  { get => _sunMultiplier; set => _sunMultiplier = value; }
        public Color AmbientColor   { get => _ambientColor; set => _ambientColor = value; }
        public Color FogColor       { get => _fogColor; set => _fogColor = value; }
        public float FogStart       { get => _fogStart; set => _fogStart = value; }
        public float FogEnd         { get => _fogEnd;   set => _fogEnd = value; }

        public void Apply()
        {
            SetupSun();

            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = _ambientColor;

            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = _fogColor;
            RenderSettings.fogStartDistance = _fogStart;
            RenderSettings.fogEndDistance = _fogEnd;
        }

        void SetupSun()
        {
            Light sun = GetSunLight();
            if (sun == null)
            {
                var sunGo = new GameObject("Sun");
                sunGo.transform.SetParent(transform, worldPositionStays: false);
                sun = sunGo.AddComponent<Light>();
                sun.type = LightType.Directional;
            }

            sun.color = _sunColor;
            sun.intensity = _sunMultiplier;
            sun.shadows = LightShadows.Soft;

            if (_sunDirection != Vector3.zero)
                sun.transform.rotation = Quaternion.LookRotation(_sunDirection);
        }

        Light GetSunLight()
        {
            foreach (Transform child in transform)
            {
                var l = child.GetComponent<Light>();
                if (l != null && l.type == LightType.Directional)
                    return l;
            }
            return null;
        }

// #if UNITY_EDITOR
//         void OnValidate()
//         {
//             if (Application.isPlaying) Apply();
//         }
// #endif
    }
}
