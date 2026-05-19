using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering.Universal;

namespace OpenFarCry.Level.Services
{
    // Re-spawns planar templdecalmodulate brush submeshes (extracted by
    // FcBrushGeometryPostProcessor) as URP DecalProjectors so they project onto the
    // surrounding geometry instead of z-fighting as overlay quads.
    //
    // The decal material template is assigned in the scene on
    // FcLevelResourceService.DecalMaterialTemplate. Its Shader Graph drives opacity/colour
    // from the supplied diffuse map; the Texture2D property reference name must be
    // "_BaseMap". The builder clones the template per unique source texture and binds
    // the original (untouched) decal texture into _BaseMap -- no texture synthesis is
    // performed.
    public static class FcBrushDecalProjectorBuilder
    {
        const float ProjectionDepth = 0.1f; // world metres straddled around the decal plane
        static readonly int PropDecalBaseMap = Shader.PropertyToID("_BaseMap");
        static readonly int PropDecalHasAlpha = Shader.PropertyToID("_HasAlpha");

        static readonly Dictionary<Texture, Material> DecalMaterialCache = new();
        static bool _templateMissingLogged;

        // Builds one DecalProjector GameObject per quad. Returned objects are parented to
        // `parent`; the caller owns their lifetime (destroy them when the brush unloads).
        // `baseTextures` is a parallel array of already level-override-resolved decal
        // textures; entry i drives quad i (null entry skips that quad).
        public static List<GameObject> Build(
            Transform visualRoot,
            Transform parent,
            FcBrushGeometryPostProcessor.DecalQuad[] quads,
            Texture[] baseTextures)
        {
            if (visualRoot == null || quads == null || quads.Length == 0)
                return null;

            var template = ResolveTemplateMaterial();
            if (template == null)
                return null;

            List<GameObject> spawned = null;
            for (int i = 0; i < quads.Length; i++)
            {
                var q = quads[i];
                var baseTexture = baseTextures != null && i < baseTextures.Length
                    ? baseTextures[i]
                    : null;
                if (baseTexture == null)
                    continue;

                Vector3 worldCenter = visualRoot.TransformPoint(q.Center);
                Vector3 worldU = visualRoot.TransformPoint(q.Center + q.AxisU) - worldCenter;
                Vector3 worldV = visualRoot.TransformPoint(q.Center + q.AxisV) - worldCenter;
                Vector3 worldN = visualRoot.TransformPoint(q.Center + q.Normal) - worldCenter;
                if (worldU.sqrMagnitude < 1e-10f ||
                    worldV.sqrMagnitude < 1e-10f ||
                    worldN.sqrMagnitude < 1e-10f)
                    continue;

                float worldWidth = q.Width * worldU.magnitude;
                float worldHeight = q.Height * worldV.magnitude;
                worldU.Normalize();
                worldV.Normalize();
                worldN.Normalize();

                var go = new GameObject($"Decal_{baseTexture.name}");
                go.transform.SetParent(parent, worldPositionStays: false);
                go.transform.SetPositionAndRotation(
                    worldCenter,
                    // Projector +Z projects into the surface, i.e. away from the viewer.
                    Quaternion.LookRotation(-worldN, worldV));

                var projector = go.AddComponent<DecalProjector>();
                projector.pivot = Vector3.zero; // projection box straddles the decal plane
                projector.size = new Vector3(worldWidth, worldHeight, ProjectionDepth);
                projector.material = GetOrCreateMaterial(baseTexture, template);

                (spawned ??= new List<GameObject>(quads.Length)).Add(go);
            }

            return spawned;
        }

        // Drops cached decal material instances. Call on level teardown to avoid leaks
        // across level reloads. The template asset itself is not destroyed.
        public static void ReleaseAll()
        {
            foreach (var mat in DecalMaterialCache.Values)
            {
                if (mat != null)
                    Object.Destroy(mat);
            }
            DecalMaterialCache.Clear();
        }

        static Material ResolveTemplateMaterial()
        {
            var svc = FcLevelResourceService.Current;
            var template = svc != null ? svc.DecalMaterialTemplate : null;
            if (template == null && !_templateMissingLogged)
            {
                Debug.LogWarning(
                    "[FcDecal] FcLevelResourceService.DecalMaterialTemplate is unassigned; decal projection skipped.");
                _templateMissingLogged = true;
            }
            return template;
        }

        static Material GetOrCreateMaterial(Texture baseTexture, Material template)
        {
            if (DecalMaterialCache.TryGetValue(baseTexture, out var cached) && cached != null)
                return cached;

            var mat = new Material(template) { name = $"FcDecal_{baseTexture.name}" };
            mat.SetTexture(PropDecalBaseMap, baseTexture);
            mat.SetFloat(
                PropDecalHasAlpha,
                GraphicsFormatUtility.HasAlphaChannel(baseTexture.graphicsFormat) ? 1f : 0f);
            DecalMaterialCache[baseTexture] = mat;
            return mat;
        }
    }
}
