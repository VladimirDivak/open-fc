using UnityEngine;

namespace OpenFarCry.Rendering.Water
{
    [CreateAssetMenu(fileName = "FcWaterSettings", menuName = "OpenFarCry/Water Settings")]
    public class FcWaterSettings : ScriptableObject
    {
        const string ResourcesPath = "FcWaterSettings";

        [Header("Tier")]
        public FcWaterQualityTier DefaultTier = FcWaterQualityTier.High;
        public bool AutoDetectTier = true;

        [Header("Planar Reflection")]
        public bool EnablePlanarReflection = true;
        public LayerMask ReflectionLayers = ~0;
        [Min(64)] public int ReflectionResolution = 512;
        public bool ReflectionHDR = true;
        [Tooltip("0 = inherit from source camera.")]
        public float ReflectionFarClip = 0f;
        [Range(0f, 1f)] public float ClipPlaneOffset = 0.01f;
        public bool ReflectShadows = false;
        public bool ReflectSkybox = true;

        [Header("Refraction")]
        public bool EnableRefraction = true;
        [Range(0f, 1f)] public float DistortionStrength = 0.05f;
        [Min(0.01f)] public float DepthFadeDistance = 4f;
        [ColorUsage(showAlpha: false, hdr: false)]
        public Color ShallowColor = new Color(0.30f, 0.60f, 0.55f, 1f);
        [ColorUsage(showAlpha: false, hdr: false)]
        public Color DeepColor = new Color(0.02f, 0.08f, 0.14f, 1f);
        [Range(0.1f, 16f)] public float FresnelPower = 5f;

        [Header("Waves")]
        public Texture2D NormalsA;
        public Texture2D NormalsB;
        public Vector2 TilingA = new Vector2(0.25f, 0.25f);
        public Vector2 TilingB = new Vector2(0.13f, 0.13f);
        public Vector2 ScrollA = new Vector2(0.03f, 0.02f);
        public Vector2 ScrollB = new Vector2(-0.02f, 0.04f);
        [Range(0f, 4f)] public float NormalStrength = 1f;

        [Header("Fallback")]
        public bool UseReflectionProbe = true;
        public Cubemap FallbackCubemap;

        [Header("Diagnostics")]
        [Tooltip("Log mirror cam position/water level once per frame to the Console.")]
        public bool VerboseLogging = false;

        static FcWaterSettings s_instance;

        public static FcWaterSettings Instance
        {
            get
            {
                if (s_instance != null)
                    return s_instance;

                s_instance = Resources.Load<FcWaterSettings>(ResourcesPath);
                if (s_instance == null)
                {
                    Debug.LogWarning(
                        $"[FcWater] No FcWaterSettings found at Resources/{ResourcesPath}. Using transient defaults.");
                    s_instance = CreateInstance<FcWaterSettings>();
                    s_instance.name = "FcWaterSettings (Transient)";
                }
                return s_instance;
            }
        }

        public static void ResetCachedInstance() => s_instance = null;
    }
}
