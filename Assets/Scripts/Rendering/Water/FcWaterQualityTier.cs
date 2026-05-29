using UnityEngine;

namespace OpenFarCry.Rendering.Water
{
    public enum FcWaterQualityTier
    {
        Off = 0,
        Low = 1,
        Medium = 2,
        High = 3,
        Ultra = 4,
        // Screen-space planar reflection (P6). Logically sits between Medium and High (no mirror cam),
        // but appended to keep existing serialized tier ints stable.
        Sspr = 5
    }

    public static class FcWaterQualityTierResolver
    {
        public static FcWaterQualityTier Resolve(FcWaterSettings settings)
        {
            if (settings == null)
                return FcWaterQualityTier.Off;

            if (!settings.EnableRefraction && !settings.EnablePlanarReflection)
                return FcWaterQualityTier.Off;

            if (!settings.AutoDetectTier)
                return ApplyHardOverrides(settings, settings.DefaultTier);

            var detected = AutoDetect();
            return ApplyHardOverrides(settings, detected);
        }

        static FcWaterQualityTier AutoDetect()
        {
            var mem = SystemInfo.graphicsMemorySize;
            bool mobile = Application.isMobilePlatform;

            if (mobile)
                return FcWaterQualityTier.Low;
            if (mem >= 6000)
                return FcWaterQualityTier.Ultra;
            if (mem >= 3000)
                return FcWaterQualityTier.High;
            if (mem >= 1500)
                return FcWaterQualityTier.Medium;
            return FcWaterQualityTier.Low;
        }

        static FcWaterQualityTier ApplyHardOverrides(FcWaterSettings settings, FcWaterQualityTier tier)
        {
            if (!settings.EnablePlanarReflection && tier > FcWaterQualityTier.Medium)
                tier = FcWaterQualityTier.Medium;
            if (!settings.EnableRefraction && tier > FcWaterQualityTier.Off)
                tier = FcWaterQualityTier.Off;
            return tier;
        }

        // Mirror-camera planar reflection path (FcPlanarReflectionRendererFeature).
        public static bool UsesPlanar(FcWaterQualityTier tier) =>
            tier == FcWaterQualityTier.High || tier == FcWaterQualityTier.Ultra;

        // Screen-space planar reflection path (FcSsprRendererFeature).
        public static bool UsesSspr(FcWaterQualityTier tier) =>
            tier == FcWaterQualityTier.Sspr;

        // Tiers that produce a screen-space _FcWaterReflectionTex (mirror planar OR SSPR). Drives the
        // shader sampling keyword and Gerstner waves.
        public static bool UsesReflectionTexture(FcWaterQualityTier tier) =>
            UsesPlanar(tier) || UsesSspr(tier);

        public static bool UsesRefraction(FcWaterQualityTier tier) =>
            tier >= FcWaterQualityTier.Low || tier == FcWaterQualityTier.Sspr;

        public static bool UsesProbeFallback(FcWaterQualityTier tier) =>
            tier == FcWaterQualityTier.Medium;

        public static int ReflectionResolution(FcWaterQualityTier tier, FcWaterSettings settings)
        {
            if (tier == FcWaterQualityTier.Ultra)
                return Mathf.Max(settings.ReflectionResolution, 1024);
            if (tier == FcWaterQualityTier.High)
                return Mathf.Clamp(settings.ReflectionResolution, 256, 512);
            if (tier == FcWaterQualityTier.Sspr)
                return Mathf.Clamp(settings.ReflectionResolution, 256, 768);
            return 0;
        }
    }
}
