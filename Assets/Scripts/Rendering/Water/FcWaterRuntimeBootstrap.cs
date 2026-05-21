using UnityEngine;

namespace OpenFarCry.Rendering.Water
{
    public static class FcWaterRuntimeBootstrap
    {
        public const string KeywordPlanar = "_FC_WATER_PLANAR_ON";
        public const string KeywordRefraction = "_FC_WATER_REFRACTION_ON";
        public const string KeywordFallbackProbe = "_FC_WATER_FALLBACK_PROBE_ON";

        static readonly int s_FallbackCubemapId = Shader.PropertyToID("_FcWaterFallbackCubemap");

        public static FcWaterQualityTier ActiveTier { get; private set; }

#if UNITY_EDITOR
        [UnityEditor.InitializeOnLoadMethod]
        static void EditorInit() => Apply();
#endif

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void RuntimeInit() => Apply();

        public static void Apply()
        {
            FcWaterSettings.ResetCachedInstance();
            var settings = FcWaterSettings.Instance;
            ActiveTier = FcWaterQualityTierResolver.Resolve(settings);

            SetKeyword(KeywordPlanar, FcWaterQualityTierResolver.UsesPlanar(ActiveTier));
            SetKeyword(KeywordRefraction, FcWaterQualityTierResolver.UsesRefraction(ActiveTier));
            SetKeyword(KeywordFallbackProbe,
                FcWaterQualityTierResolver.UsesProbeFallback(ActiveTier) && settings.UseReflectionProbe);

            if (settings.FallbackCubemap != null)
                Shader.SetGlobalTexture(s_FallbackCubemapId, settings.FallbackCubemap);
        }

        static void SetKeyword(string keyword, bool on)
        {
            if (on) Shader.EnableKeyword(keyword);
            else Shader.DisableKeyword(keyword);
        }
    }
}
