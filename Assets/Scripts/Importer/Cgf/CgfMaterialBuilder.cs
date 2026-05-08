using UnityEngine;
using UnityEngine.Rendering;

namespace OpenFarCry.Importer.Cgf
{
    // Creates Unity URP Lit materials from parsed CgfMaterialChunk data.
    // All materials are Surface = Opaque; alpha clipping is toggled based on
    // the chunk's opacity / alphaTest fields.
    public static class CgfMaterialBuilder
    {
        static readonly int PropBaseColor = Shader.PropertyToID("_BaseColor");
        static readonly int PropAlphaClip = Shader.PropertyToID("_AlphaClip");
        static readonly int PropCutoff    = Shader.PropertyToID("_Cutoff");
        static readonly int PropCull      = Shader.PropertyToID("_Cull");
        static readonly int PropSurface   = Shader.PropertyToID("_Surface");
        static readonly int PropZWrite    = Shader.PropertyToID("_ZWrite");
        static readonly int PropSrcBlend  = Shader.PropertyToID("_SrcBlend");
        static readonly int PropDstBlend  = Shader.PropertyToID("_DstBlend");
        static readonly int PropSrcBlendA = Shader.PropertyToID("_SrcBlendAlpha");
        static readonly int PropDstBlendA = Shader.PropertyToID("_DstBlendAlpha");

        static Shader _urpLit;
        static Shader UrpLit => _urpLit != null ? _urpLit : (_urpLit = Shader.Find("Universal Render Pipeline/Lit"));

        public static Material Build(CgfMaterialChunk chunk)
        {
            var shader = UrpLit;
            if (shader == null)
            {
                Debug.LogWarning("[CgfImporter] URP Lit shader not found; falling back to default.");
                return BuildFallback(chunk?.Name ?? "unknown");
            }

            var mat = new Material(shader)
            {
                name = string.IsNullOrEmpty(chunk.Name) ? "CgfMaterial" : chunk.Name
            };

            var diffuse = chunk.DiffuseColor;
            mat.SetColor(PropBaseColor, new Color(diffuse.r / 255f, diffuse.g / 255f, diffuse.b / 255f, 1f));

            if (NeedsAlphaClip(chunk, out float cutoff))
                ApplyAlphaClipState(mat, cutoff);
            else
                ApplyOpaqueState(mat);

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

        static bool NeedsAlphaClip(CgfMaterialChunk chunk, out float cutoff)
        {
            if (chunk.ChunkVersion >= 0x0746 && chunk.AlphaTest > 0f)
            {
                cutoff = chunk.AlphaTest;
                return true;
            }
            if (chunk.ChunkVersion >= 0x0745 && chunk.Opacity < 0.99f)
            {
                cutoff = 0.5f;
                return true;
            }
            cutoff = 0.5f;
            return false;
        }

        static bool IsTwoSided(CgfMaterialChunk chunk)
        {
            return chunk.MtlType == CgfMtlType.TwoSided ||
                   (chunk.Flags & CgfMtlFlags.TwoSided) != 0;
        }

        static void ApplyOpaqueState(Material m)
        {
            m.SetFloat(PropSurface, 0f);
            m.SetFloat(PropAlphaClip, 0f);
            m.SetFloat(PropZWrite, 1f);
            m.SetFloat(PropSrcBlend, (float)BlendMode.One);
            m.SetFloat(PropDstBlend, (float)BlendMode.Zero);
            m.SetFloat(PropSrcBlendA, (float)BlendMode.One);
            m.SetFloat(PropDstBlendA, (float)BlendMode.Zero);
            m.DisableKeyword("_ALPHATEST_ON");
            m.DisableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.SetOverrideTag("RenderType", "Opaque");
            m.renderQueue = (int)RenderQueue.Geometry;
        }

        static void ApplyAlphaClipState(Material m, float cutoff)
        {
            m.SetFloat(PropSurface, 0f);
            m.SetFloat(PropAlphaClip, 1f);
            m.SetFloat(PropCutoff, cutoff);
            m.SetFloat(PropZWrite, 1f);
            m.SetFloat(PropSrcBlend, (float)BlendMode.One);
            m.SetFloat(PropDstBlend, (float)BlendMode.Zero);
            m.SetFloat(PropSrcBlendA, (float)BlendMode.One);
            m.SetFloat(PropDstBlendA, (float)BlendMode.Zero);
            m.EnableKeyword("_ALPHATEST_ON");
            m.DisableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.SetOverrideTag("RenderType", "TransparentCutout");
            m.renderQueue = (int)RenderQueue.AlphaTest;
        }
    }
}
