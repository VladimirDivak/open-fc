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

            if (IsNoDraw(chunk))
                return BuildNoDraw(chunk.Name);

            var mat = new Material(shader)
            {
                name = string.IsNullOrEmpty(chunk.Name) ? "CgfMaterial" : chunk.Name
            };

            // Diffuse / base map
            var diffuse = chunk.DiffuseColor;
            mat.SetColor(PropBaseColor, new Color(diffuse.r / 255f, diffuse.g / 255f, diffuse.b / 255f, 1f));
            if (textures.BaseMap != null)
                mat.SetTexture(PropBaseMap, textures.BaseMap);
            else if (textures.OpacityMap != null)
                mat.SetTexture(PropBaseMap, textures.OpacityMap);

            // Normal map
            if (textures.NormalMap != null)
            {
                mat.SetTexture(PropBumpMap, textures.NormalMap);
                mat.SetFloat(PropBumpScale, 1f);
                mat.EnableKeyword("_NORMALMAP");
            }

            // Smoothness: low default until specular workflow is revisited
            mat.SetFloat(PropSmoothness, 0.2f);

            // Surface mode: additive > alpha-test > opaque.
            // MTLFLAG_CRYSHADER (0x040) means "uses CryShader system" and is set on ~68% of all
            // materials — it does NOT indicate alpha-test. Alpha-test is driven solely by the
            // alpharef float field (chunk.AlphaTest), which is non-zero only for vegetation, glass,
            // and similar cut-out geometry. 0x0744/0x0745 chunks have no alpharef; treat as opaque.
            bool hasAlphaTest = chunk.AlphaTest > 0.01f;
            bool isAdditive   = (chunk.Flags & CgfMtlFlags.Additive) != 0;

            if (isAdditive)
                ApplyAdditiveState(mat);
            else if (hasAlphaTest)
                ApplyCutoutState(mat, Mathf.Max(chunk.AlphaTest, 0.1f));
            else
                ApplyOpaqueState(mat);

            // Two-sided
            if (IsTwoSided(chunk))
            {
                mat.SetFloat(PropCull, (float)CullMode.Off);
                mat.doubleSidedGI = true;
            }

            return mat;
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
            if (chunk == null || string.IsNullOrEmpty(chunk.ShaderName))
                return false;
            return chunk.ShaderName == "nodraw" || chunk.ShaderName == "no_draw";
        }

        static bool IsTwoSided(CgfMaterialChunk chunk) =>
            chunk.MtlType == CgfMtlType.TwoSided ||
            (chunk.Flags & CgfMtlFlags.TwoSided) != 0;

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
    }
}
