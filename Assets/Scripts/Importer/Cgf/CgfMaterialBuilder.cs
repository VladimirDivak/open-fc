using UnityEngine;
using UnityEngine.Rendering;

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
        static readonly int PropSpecGlossMap = Shader.PropertyToID("_SpecGlossMap");
        static readonly int PropMetallic     = Shader.PropertyToID("_Metallic");
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

            if (chunk == null)
                return BuildFallback("null");

            var classification = chunk.Classification;
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
            ApplyEmissionInputs(mat, textures, classification, chunk.SelfIllum);
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

            // When textures are injected after a texture-free bake, PropSmoothTexCh may not
            // have been set because the spec/gloss maps were absent at bake time.
            // Don't override if already set to 1 (smoothness-from-albedo-alpha mode).
            if (textures.SpecularMap != null || textures.GlossMap != null)
            {
                if (mat.HasProperty(PropSmoothTexCh) && mat.GetFloat(PropSmoothTexCh) < 0.5f)
                    mat.SetFloat(PropSmoothTexCh, 0f);
            }
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
            return chunk != null && chunk.Classification.IsNoDraw;
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
            bool isSpecFamily =
                classification.Family == CgfMaterialShaderFamily.BumpSpec ||
                classification.Family == CgfMaterialShaderFamily.BumpSpecGlossAlpha ||
                !string.IsNullOrWhiteSpace(chunk.SpecularTextureName) ||
                !string.IsNullOrWhiteSpace(chunk.GlossTextureName);

            if (isSpecFamily)
            {
                // CryEngine 1 is Phong, not PBR: col_s * specLevel is a highlight
                // intensity, not a per-pixel reflectance. Feeding it into URP's specular
                // workflow as _SpecColor turns whole surfaces mirror-white, because col_s
                // is near-white for almost every FC1 material. FC1 geometry is
                // overwhelmingly dielectric (cloth, skin, leather, concrete, wood), so map
                // to the metallic workflow with metallic 0: URP then applies the correct
                // ~4% dielectric specular. Highlight shape comes from smoothness
                // (gloss-alpha / shininess), not from a white spec color. Genuine metals
                // are rare in FC1 and undetectable without parsing Illumination.ext flags.
                SetFloatIfProperty(mat, PropWorkflowMode, 1f); // URP Lit metallic workflow
                SetFloatIfProperty(mat, PropMetallic, 0f);
            }

            if (classification.UsesGlossFromDiffuseAlpha)
            {
                // Gloss is stored in the diffuse alpha; URP reads smoothness from albedo alpha.
                SetFloatIfProperty(mat, PropSmoothTexCh, 1f);
            }
            else if (textures.GlossMap != null || textures.SpecularMap != null)
            {
                // Dedicated gloss/spec map: smoothness from the metallic-gloss map alpha.
                SetFloatIfProperty(mat, PropSmoothTexCh, 0f);
            }
        }

        static void ApplyEmissionInputs(
            Material mat,
            CgfResolvedMaterialTextures textures,
            CgfMaterialClassification classification,
            float selfIllum)
        {
            if (!classification.UsesGlowFromDiffuseAlpha)
                return;

            // Bind the base map as a provisional emission source. CgfMaterialImportService
            // upgrades _EmissionMap to a scoped alpha-derived mask when the base texture is
            // readable; otherwise the base map itself stays as the emission source.
            if (textures.BaseMap != null)
                SetTextureIfProperty(mat, PropEmissionMap, textures.BaseMap);

            // Cry selfIllum scalar drives glow strength; fall back to 0.5 when absent.
            float strength = selfIllum > 0.0001f ? Mathf.Clamp01(selfIllum) : 0.5f;
            SetColorIfProperty(mat, PropEmissionColor, Color.white * strength);
            mat.EnableKeyword("_EMISSION");
        }

        // Builds a grayscale emission mask from a base texture's alpha channel.
        // Pure: returns a fresh Texture2D (caller owns lifetime) or null when unreadable.
        public static Texture2D CreateEmissionMaskFromBaseAlpha(Texture2D baseMap)
        {
            if (baseMap == null || !baseMap.isReadable)
                return null;

            Color32[] src;
            try
            {
                src = baseMap.GetPixels32();
            }
            catch
            {
                return null;
            }

            if (src == null || src.Length == 0)
                return null;

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
            return tex;
        }

        static void ApplyReflectionInputs(Material mat, CgfMaterialClassification classification)
        {
            if (!classification.UsesReflection)
                return;

            SetFloatIfProperty(mat, PropSpecHighlights, 1f);
            SetFloatIfProperty(mat, PropEnvReflections, 1f);
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
