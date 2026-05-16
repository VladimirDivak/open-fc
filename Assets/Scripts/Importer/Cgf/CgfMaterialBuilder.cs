using UnityEngine;
using UnityEngine.Rendering;
using System.Collections.Generic;

namespace OpenFarCry.Importer.Cgf
{
    // Creates Unity URP Lit materials from parsed CgfMaterialChunk data.
    // Shader routing follows materials.md section 9: shader name extracted from the
    // material's Name field drives surface mode; MTL flags drive cutout/additive/2-sided.
    public static class CgfMaterialBuilder
    {
        static readonly int PropBaseColor    = Shader.PropertyToID("_BaseColor");
        static readonly int PropBaseMap      = Shader.PropertyToID("_BaseMap");
        static readonly int PropBumpMap      = Shader.PropertyToID("_BumpMap");
        static readonly int PropBumpScale    = Shader.PropertyToID("_BumpScale");
        static readonly int PropSpecColor    = Shader.PropertyToID("_SpecColor");
        static readonly int PropSpecGlossMap = Shader.PropertyToID("_SpecGlossMap");
        static readonly int PropWorkflowMode = Shader.PropertyToID("_WorkflowMode");
        static readonly int PropSmoothTexCh  = Shader.PropertyToID("_SmoothnessTextureChannel");
        static readonly int PropBlend        = Shader.PropertyToID("_Blend");
        static readonly int PropEmissionMap  = Shader.PropertyToID("_EmissionMap");
        static readonly int PropEmissionColor = Shader.PropertyToID("_EmissionColor");
        static readonly int PropAlphaClip    = Shader.PropertyToID("_AlphaClip");
        static readonly int PropCutoff       = Shader.PropertyToID("_Cutoff");
        static readonly int PropSmoothness   = Shader.PropertyToID("_Smoothness");
        static readonly int PropCull         = Shader.PropertyToID("_Cull");
        static readonly int PropSurface      = Shader.PropertyToID("_Surface");
        static readonly int PropZWrite       = Shader.PropertyToID("_ZWrite");
        static readonly int PropSrcBlend     = Shader.PropertyToID("_SrcBlend");
        static readonly int PropDstBlend     = Shader.PropertyToID("_DstBlend");
        static readonly int PropSrcBlendA    = Shader.PropertyToID("_SrcBlendAlpha");
        static readonly int PropDstBlendA    = Shader.PropertyToID("_DstBlendAlpha");
        static readonly int PropSpecHighlights = Shader.PropertyToID("_SpecularHighlights");
        static readonly int PropEnvReflections = Shader.PropertyToID("_EnvironmentReflections");
        static readonly Dictionary<int, Texture2D> EmissionMaskByBaseTextureId = new Dictionary<int, Texture2D>();

        static Shader _urpLit;
        static Shader UrpLit => _urpLit != null ? _urpLit : (_urpLit = Shader.Find("Universal Render Pipeline/Lit"));

        public static Material Build(CgfMaterialChunk chunk, CgfResolvedMaterialTextures textures = default)
        {
            var shader = UrpLit;
            if (shader == null)
            {
                Debug.LogWarning("[CgfImporter] URP Lit shader not found; falling back to default.");
                return BuildFallback(chunk?.Name ?? "unknown");
            }

            var classification = CgfMaterialClassifier.Analyze(chunk);
            if (classification.IsNoDraw)
                return BuildNoDraw(chunk.Name);

            var mat = new Material(shader)
            {
                name = string.IsNullOrEmpty(chunk.Name) ? "CgfMaterial" : chunk.Name
            };

            // Diffuse / base map
            var diffuse = chunk.DiffuseColor;
            float baseAlpha = ComputeBaseAlpha(chunk, classification);
            mat.SetColor(PropBaseColor, new Color(
                diffuse.r / 255f,
                diffuse.g / 255f,
                diffuse.b / 255f,
                baseAlpha));
            ApplyResolvedTextures(mat, textures);
            ApplySpecularInputs(mat, chunk, textures, classification);
            ApplyEmissionInputs(mat, textures, classification);
            ApplyReflectionInputs(mat, classification);

            float smoothness = ComputeSmoothness(chunk);
            SetFloatIfProperty(mat, PropSmoothness, smoothness);

            if (classification.IsAdditive)
                ApplyAdditiveState(mat);
            else if (classification.Family == CgfMaterialShaderFamily.ModulateDecal)
                ApplyModulateState(mat);
            else if (classification.Family == CgfMaterialShaderFamily.Glass)
                ApplyGlassState(mat);
            else if (classification.IsTransparentAlphaBlend)
                ApplyAlphaBlendState(mat);
            else if (classification.IsRgbOnlyDecal)
                ApplyOpaqueState(mat);
            else if (classification.Family == CgfMaterialShaderFamily.Plants ||
                     classification.Family == CgfMaterialShaderFamily.Bark)
                ApplyCutoutState(mat, chunk.AlphaTest > 0.01f ? Mathf.Max(chunk.AlphaTest, 0.1f) : 0.3f);
            else if (classification.IsCutout)
                ApplyCutoutState(mat, Mathf.Max(chunk.AlphaTest, 0.1f));
            else
                ApplyOpaqueState(mat);

            if (classification.IsTwoSided)
            {
                mat.SetFloat(PropCull, (float)CullMode.Off);
                mat.doubleSidedGI = true;
            }

            return mat;
        }

        public static void ApplyResolvedTextures(Material mat, CgfResolvedMaterialTextures textures)
        {
            if (mat == null)
                return;

            if (textures.BaseMap != null)
                mat.SetTexture(PropBaseMap, textures.BaseMap);
            else if (textures.OpacityMap != null && mat.GetTexture(PropBaseMap) == null)
                mat.SetTexture(PropBaseMap, textures.OpacityMap);

            if (textures.NormalMap != null)
            {
                mat.SetTexture(PropBumpMap, textures.NormalMap);
                mat.SetFloat(PropBumpScale, 1f);
                mat.EnableKeyword("_NORMALMAP");
            }

            if (textures.SpecularMap != null)
                SetTextureIfProperty(mat, PropSpecGlossMap, textures.SpecularMap);
            else if (textures.GlossMap != null)
                SetTextureIfProperty(mat, PropSpecGlossMap, textures.GlossMap);
        }

        public static Material BuildFallback(string name)
        {
            var shader = UrpLit;
            if (shader == null)
                shader = Shader.Find("Standard") ?? Shader.Find("Hidden/InternalErrorShader");

            var mat = new Material(shader)
            {
                name  = string.IsNullOrEmpty(name) ? "CgfFallback" : $"{name}_fallback",
                color = Color.magenta
            };
            if (shader == UrpLit && UrpLit != null)
            {
                mat.SetColor(PropBaseColor, Color.magenta);
                ApplyOpaqueState(mat);
            }
            return mat;
        }

        // Returns a fully invisible material for collision-only (nodraw) submeshes.
        // The renderer still submits geometry; proper removal happens at mesh-build level.
        public static Material BuildNoDraw(string name)
        {
            var shader = UrpLit;
            if (shader == null)
                return BuildFallback(name);

            var mat = new Material(shader)
            {
                name = string.IsNullOrEmpty(name) ? "CgfNoDraw" : name
            };
            mat.SetColor(PropBaseColor, Color.clear);
            mat.SetFloat(PropSurface, 1f);
            mat.SetFloat(PropAlphaClip, 0f);
            mat.SetFloat(PropZWrite, 0f);
            mat.SetFloat(PropSrcBlend,  (float)BlendMode.Zero);
            mat.SetFloat(PropDstBlend,  (float)BlendMode.One);
            mat.SetFloat(PropSrcBlendA, (float)BlendMode.Zero);
            mat.SetFloat(PropDstBlendA, (float)BlendMode.One);
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.SetOverrideTag("RenderType", "Transparent");
            mat.renderQueue = (int)RenderQueue.Transparent + 1;
            return mat;
        }

        public static bool IsNoDraw(CgfMaterialChunk chunk)
        {
            return CgfMaterialClassifier.Analyze(chunk).IsNoDraw;
        }

        static float ComputeSmoothness(CgfMaterialChunk chunk)
        {
            if (chunk.SpecShininess > 0.0001f)
                return Mathf.Clamp01(Mathf.Sqrt(Mathf.Clamp01(chunk.SpecShininess)));
            return 0.2f;
        }

        static float ComputeBaseAlpha(CgfMaterialChunk chunk, CgfMaterialClassification classification)
        {
            if (chunk == null)
                return 1f;

            if (!classification.UsesTransparencyFromDiffuseAlpha)
                return 1f;

            float opacity = chunk.Opacity > 0.0001f
                ? Mathf.Clamp01(chunk.Opacity)
                : Mathf.Clamp01(chunk.DiffuseColor.a / 255f);
            if (opacity > 0.0001f)
                return opacity;

            // Keep glass visible even when source opacity is omitted.
            if (classification.Family == CgfMaterialShaderFamily.Glass)
                return 0.35f;

            return 1f;
        }

        static void ApplySpecularInputs(
            Material mat,
            CgfMaterialChunk chunk,
            CgfResolvedMaterialTextures textures,
            CgfMaterialClassification classification)
        {
            if (classification.Family == CgfMaterialShaderFamily.BumpSpec ||
                classification.Family == CgfMaterialShaderFamily.BumpSpecGlossAlpha ||
                !string.IsNullOrWhiteSpace(chunk.SpecularTextureName) ||
                !string.IsNullOrWhiteSpace(chunk.GlossTextureName))
            {
                SetFloatIfProperty(mat, PropWorkflowMode, 0f); // URP Lit specular workflow
                float specLevel = chunk.SpecLevel > 0.0001f ? Mathf.Clamp01(chunk.SpecLevel) : 0.5f;
                var sc = chunk.SpecularColor;
                var specColor = new Color(
                    (sc.r / 255f) * specLevel,
                    (sc.g / 255f) * specLevel,
                    (sc.b / 255f) * specLevel,
                    1f);
                SetColorIfProperty(mat, PropSpecColor, specColor);
            }

            if (classification.UsesGlossFromDiffuseAlpha)
            {
                // URP Lit: 1 = smoothness from albedo alpha.
                SetFloatIfProperty(mat, PropSmoothTexCh, 1f);
            }
            else if (textures.GlossMap != null || textures.SpecularMap != null)
            {
                // URP Lit: 0 = smoothness from metallic/spec alpha.
                SetFloatIfProperty(mat, PropSmoothTexCh, 0f);
            }
        }

        static void ApplyEmissionInputs(
            Material mat,
            CgfResolvedMaterialTextures textures,
            CgfMaterialClassification classification)
        {
            if (!classification.UsesGlowFromDiffuseAlpha)
                return;

            UnityEngine.Texture emissionSource = textures.BaseMap;
            if (textures.BaseMap != null &&
                TryGetOrCreateEmissionMaskFromBaseAlpha(textures.BaseMap, out var mask) &&
                mask != null)
            {
                emissionSource = mask;
            }

            if (emissionSource != null)
                SetTextureIfProperty(mat, PropEmissionMap, emissionSource);

            SetColorIfProperty(mat, PropEmissionColor, Color.white * 0.5f);
            mat.EnableKeyword("_EMISSION");
        }

        static void ApplyReflectionInputs(Material mat, CgfMaterialClassification classification)
        {
            if (!classification.UsesReflection)
                return;

            SetFloatIfProperty(mat, PropSpecHighlights, 1f);
            SetFloatIfProperty(mat, PropEnvReflections, 1f);
        }

        static bool TryGetOrCreateEmissionMaskFromBaseAlpha(Texture2D baseMap, out Texture2D emissionMask)
        {
            emissionMask = null;
            if (baseMap == null || !baseMap.isReadable)
                return false;

            int key = baseMap.GetInstanceID();
            if (EmissionMaskByBaseTextureId.TryGetValue(key, out emissionMask) && emissionMask != null)
                return true;

            Color32[] src;
            try
            {
                src = baseMap.GetPixels32();
            }
            catch
            {
                return false;
            }

            if (src == null || src.Length == 0)
                return false;

            var dst = new Color32[src.Length];
            for (int i = 0; i < src.Length; i++)
            {
                byte a = src[i].a;
                dst[i] = new Color32(a, a, a, 255);
            }

            var tex = new Texture2D(baseMap.width, baseMap.height, TextureFormat.RGBA32, mipChain: false, linear: true)
            {
                name = $"{baseMap.name}_emission_mask"
            };
            tex.SetPixels32(dst);
            tex.Apply(updateMipmaps: false, makeNoLongerReadable: true);

            EmissionMaskByBaseTextureId[key] = tex;
            emissionMask = tex;
            return true;
        }

        static void ApplyOpaqueState(Material m)
        {
            m.SetFloat(PropSurface, 0f);
            m.SetFloat(PropAlphaClip, 0f);
            m.SetFloat(PropZWrite, 1f);
            m.SetFloat(PropSrcBlend,  (float)BlendMode.One);
            m.SetFloat(PropDstBlend,  (float)BlendMode.Zero);
            m.SetFloat(PropSrcBlendA, (float)BlendMode.One);
            m.SetFloat(PropDstBlendA, (float)BlendMode.Zero);
            m.DisableKeyword("_ALPHATEST_ON");
            m.DisableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.SetOverrideTag("RenderType", "Opaque");
            m.renderQueue = (int)RenderQueue.Geometry;
        }

        static void ApplyCutoutState(Material m, float cutoff)
        {
            m.SetFloat(PropSurface, 0f);
            m.SetFloat(PropAlphaClip, 1f);
            m.SetFloat(PropCutoff, cutoff);
            m.SetFloat(PropZWrite, 1f);
            m.SetFloat(PropSrcBlend,  (float)BlendMode.One);
            m.SetFloat(PropDstBlend,  (float)BlendMode.Zero);
            m.SetFloat(PropSrcBlendA, (float)BlendMode.One);
            m.SetFloat(PropDstBlendA, (float)BlendMode.Zero);
            m.EnableKeyword("_ALPHATEST_ON");
            m.DisableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.SetOverrideTag("RenderType", "TransparentCutout");
            m.renderQueue = (int)RenderQueue.AlphaTest;
        }

        static void ApplyAlphaBlendState(Material m)
        {
            m.SetFloat(PropSurface, 1f);
            SetFloatIfProperty(m, PropBlend, 0f);
            m.SetFloat(PropAlphaClip, 0f);
            m.SetFloat(PropZWrite, 0f);
            m.SetFloat(PropSrcBlend,  (float)BlendMode.SrcAlpha);
            m.SetFloat(PropDstBlend,  (float)BlendMode.OneMinusSrcAlpha);
            m.SetFloat(PropSrcBlendA, (float)BlendMode.One);
            m.SetFloat(PropDstBlendA, (float)BlendMode.OneMinusSrcAlpha);
            m.DisableKeyword("_ALPHATEST_ON");
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.SetOverrideTag("RenderType", "Transparent");
            m.renderQueue = (int)RenderQueue.Transparent;
        }

        static void ApplyModulateState(Material m)
        {
            m.SetFloat(PropSurface, 1f);
            m.SetFloat(PropAlphaClip, 0f);
            m.SetFloat(PropZWrite, 0f);
            m.SetFloat(PropSrcBlend,  (float)BlendMode.DstColor);
            m.SetFloat(PropDstBlend,  (float)BlendMode.Zero);
            m.SetFloat(PropSrcBlendA, (float)BlendMode.One);
            m.SetFloat(PropDstBlendA, (float)BlendMode.Zero);
            m.DisableKeyword("_ALPHATEST_ON");
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.SetOverrideTag("RenderType", "Transparent");
            m.renderQueue = (int)RenderQueue.Transparent;
        }

        static void ApplyAdditiveState(Material m)
        {
            m.SetFloat(PropSurface, 1f);
            m.SetFloat(PropAlphaClip, 0f);
            m.SetFloat(PropZWrite, 0f);
            m.SetFloat(PropSrcBlend,  (float)BlendMode.One);
            m.SetFloat(PropDstBlend,  (float)BlendMode.One);
            m.SetFloat(PropSrcBlendA, (float)BlendMode.One);
            m.SetFloat(PropDstBlendA, (float)BlendMode.One);
            m.DisableKeyword("_ALPHATEST_ON");
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.SetOverrideTag("RenderType", "Transparent");
            m.renderQueue = (int)RenderQueue.Transparent;
        }

        static void ApplyGlassState(Material m)
        {
            ApplyAlphaBlendState(m);
            SetFloatIfProperty(m, PropSmoothness, 1f);
            SetFloatIfProperty(m, PropSpecHighlights, 1f);
            SetFloatIfProperty(m, PropEnvReflections, 1f);
        }

        static void SetFloatIfProperty(Material mat, int propertyId, float value)
        {
            if (mat.HasProperty(propertyId))
                mat.SetFloat(propertyId, value);
        }

        static void SetColorIfProperty(Material mat, int propertyId, Color value)
        {
            if (mat.HasProperty(propertyId))
                mat.SetColor(propertyId, value);
        }

        static void SetTextureIfProperty(Material mat, int propertyId, UnityEngine.Texture value)
        {
            if (mat.HasProperty(propertyId))
                mat.SetTexture(propertyId, value);
        }
    }
}
