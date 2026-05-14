using System;

namespace OpenFarCry.Importer.Cgf
{
    public enum CgfMaterialShaderFamily
    {
        Unknown = 0,
        NoDraw,
        Diffuse,
        BumpDiffuse,
        BumpSpec,
        BumpSpecGlossAlpha,
        Plants,
        Bark,
        Glass,
        AlphaBlend,
        GlowDecal,
        ModulateDecal,
        UvScroll
    }

    public readonly struct CgfMaterialClassification
    {
        public readonly CgfMaterialShaderFamily Family;
        public readonly bool IsNoDraw;
        public readonly bool IsCutout;
        public readonly bool IsTransparentAlphaBlend;
        public readonly bool IsAdditive;
        public readonly bool IsTwoSided;
        public readonly bool UsesVertexColors;
        public readonly bool UsesGlossFromDiffuseAlpha;
        public readonly bool UsesGlowFromDiffuseAlpha;
        public readonly bool UsesTransparencyFromDiffuseAlpha;
        public readonly bool UsesGlossTexture;
        public readonly bool UsesReflection;
        public readonly bool UsesUvScroll;

        public CgfMaterialClassification(
            CgfMaterialShaderFamily family,
            bool isNoDraw,
            bool isCutout,
            bool isTransparentAlphaBlend,
            bool isAdditive,
            bool isTwoSided,
            bool usesVertexColors,
            bool usesGlossFromDiffuseAlpha,
            bool usesGlowFromDiffuseAlpha,
            bool usesTransparencyFromDiffuseAlpha,
            bool usesGlossTexture,
            bool usesReflection,
            bool usesUvScroll)
        {
            Family = family;
            IsNoDraw = isNoDraw;
            IsCutout = isCutout;
            IsTransparentAlphaBlend = isTransparentAlphaBlend;
            IsAdditive = isAdditive;
            IsTwoSided = isTwoSided;
            UsesVertexColors = usesVertexColors;
            UsesGlossFromDiffuseAlpha = usesGlossFromDiffuseAlpha;
            UsesGlowFromDiffuseAlpha = usesGlowFromDiffuseAlpha;
            UsesTransparencyFromDiffuseAlpha = usesTransparencyFromDiffuseAlpha;
            UsesGlossTexture = usesGlossTexture;
            UsesReflection = usesReflection;
            UsesUvScroll = usesUvScroll;
        }
    }

    public static class CgfMaterialClassifier
    {
        public static CgfMaterialClassification Analyze(CgfMaterialChunk chunk)
        {
            if (chunk == null)
                return default;

            string shader = (chunk.ShaderName ?? string.Empty).ToLowerInvariant();
            bool hasAlphaTest = chunk.AlphaTest > 0.01f;
            bool isTwoSided = chunk.MtlType == CgfMtlType.TwoSided ||
                              (chunk.Flags & CgfMtlFlags.TwoSided) != 0;
            bool isAdditive = (chunk.Flags & CgfMtlFlags.Additive) != 0;

            bool hasNormal = !string.IsNullOrWhiteSpace(chunk.NormalTextureName);
            bool hasSpecular = !string.IsNullOrWhiteSpace(chunk.SpecularTextureName);
            bool hasGloss = !string.IsNullOrWhiteSpace(chunk.GlossTextureName);

            bool isNoDraw = shader == "nodraw" || shader == "no_draw";
            bool isPlants = ContainsOrdinal(shader, "plants");
            bool isBark = ContainsOrdinal(shader, "bark");
            bool isGlass = ContainsOrdinal(shader, "glass") || ContainsOrdinal(shader, "refr");
            bool isAlphaBlend = ContainsOrdinal(shader, "alphablend");
            bool isGlowDecal = ContainsOrdinal(shader, "glow") || ContainsOrdinal(shader, "selfillum");
            bool isModulateDecal = ContainsOrdinal(shader, "decalmodulate");
            bool isUvScroll = ContainsOrdinal(shader, "textureshiftt05");
            bool isBumpSpecFamily = ContainsOrdinal(shader, "bumpspec");
            bool isBumpDiffuseFamily = ContainsOrdinal(shader, "bumpdiffuse");

            var family = CgfMaterialShaderFamily.Unknown;
            if (isNoDraw)
                family = CgfMaterialShaderFamily.NoDraw;
            else if (isPlants && !isBark)
                family = CgfMaterialShaderFamily.Plants;
            else if (isBark)
                family = CgfMaterialShaderFamily.Bark;
            else if (isGlass)
                family = CgfMaterialShaderFamily.Glass;
            else if (isAlphaBlend)
                family = CgfMaterialShaderFamily.AlphaBlend;
            else if (isGlowDecal)
                family = CgfMaterialShaderFamily.GlowDecal;
            else if (isModulateDecal)
                family = CgfMaterialShaderFamily.ModulateDecal;
            else if (isUvScroll)
                family = CgfMaterialShaderFamily.UvScroll;
            else if (isBumpSpecFamily && (hasGloss || ContainsOrdinal(shader, "glossalpha")))
                family = CgfMaterialShaderFamily.BumpSpecGlossAlpha;
            else if (isBumpSpecFamily)
                family = CgfMaterialShaderFamily.BumpSpec;
            else if (isBumpDiffuseFamily)
                family = CgfMaterialShaderFamily.BumpDiffuse;
            else if (hasNormal && hasGloss)
                family = CgfMaterialShaderFamily.BumpSpecGlossAlpha;
            else if (hasNormal && hasSpecular)
                family = CgfMaterialShaderFamily.BumpSpec;
            else if (hasNormal)
                family = CgfMaterialShaderFamily.BumpDiffuse;
            else
                family = CgfMaterialShaderFamily.Diffuse;

            bool usesGlossFromDiffuseAlpha = family == CgfMaterialShaderFamily.BumpSpecGlossAlpha &&
                                             (!hasGloss || ContainsOrdinal(shader, "glossalpha"));
            bool usesGlowFromDiffuseAlpha = isGlowDecal;
            bool usesTransparencyFromDiffuseAlpha = isAlphaBlend || isGlass;

            return new CgfMaterialClassification(
                family: family,
                isNoDraw: isNoDraw,
                isCutout: hasAlphaTest,
                isTransparentAlphaBlend: isAlphaBlend || isGlass,
                isAdditive: isAdditive,
                isTwoSided: isTwoSided || isPlants,
                usesVertexColors: isPlants || isBark,
                usesGlossFromDiffuseAlpha: usesGlossFromDiffuseAlpha,
                usesGlowFromDiffuseAlpha: usesGlowFromDiffuseAlpha,
                usesTransparencyFromDiffuseAlpha: usesTransparencyFromDiffuseAlpha,
                usesGlossTexture: hasGloss,
                usesReflection: isGlass,
                usesUvScroll: isUvScroll);
        }

        static bool ContainsOrdinal(string value, string needle)
        {
            return value != null && needle != null && value.IndexOf(needle, StringComparison.Ordinal) >= 0;
        }
    }
}
