using System;
using System.Collections.Generic;
using OpenFarCry.Importer.Cgf;
using UnityEngine;

namespace OpenFarCry.Level.Services
{
    static class FcLevelRuntimeLodGroupBuilder
    {
        // Returns the brush-owned filtered LOD visual meshes (caller must Destroy them on
        // teardown), or null when none were created.
        public static List<Mesh> Apply(
            GameObject root,
            Renderer lod0Renderer,
            IReadOnlyList<CgfRuntimeImportResult> lodResults,
            string levelScopeId,
            Func<Material[], int[], Material[]> materialOverrider = null,
            bool stripProxySubmeshes = false)
        {
            if (root == null || lod0Renderer == null || lodResults == null || lodResults.Count == 0)
                return null;

            var renderers = new List<Renderer>(lodResults.Count + 1) { lod0Renderer };
            List<Mesh> filteredLodMeshes = null;

            for (int i = 0; i < lodResults.Count; i++)
            {
                var result = lodResults[i];
                if (result?.BuildResult?.Mesh == null)
                    continue;

                var lodGo = new GameObject($"LOD{i + 1}");
                lodGo.transform.SetParent(root.transform, worldPositionStays: false);

                lodGo.AddComponent<MeshFilter>().sharedMesh = result.BuildResult.Mesh;
                var meshRenderer = lodGo.AddComponent<MeshRenderer>();
                var lodMats = CgfRuntimeImporter.MaterialService.ResolveSubmeshMaterials(
                    result.ParsedFile,
                    result.Mesh,
                    result.BuildResult.SubmeshMaterialIds,
                    levelScopeId);
                meshRenderer.sharedMaterials = materialOverrider != null
                    ? materialOverrider(lodMats, result.BuildResult.SubmeshMaterialIds) ?? lodMats
                    : lodMats;

                // Brush LOD0 gets proxy/no-draw submeshes stripped via
                // FcBrushGeometryPostProcessor; mirror that here so LOD1+ do not render
                // collision-only (NoDraw) materials. Vegetation does not strip LOD0, so it
                // leaves stripProxySubmeshes false to keep LODs consistent with LOD0.
                if (stripProxySubmeshes)
                {
                    var lodSubmeshMatIds = result.BuildResult.SubmeshMaterialIds != null
                        ? (int[])result.BuildResult.SubmeshMaterialIds.Clone()
                        : null;
                    Mesh filtered = FcBrushGeometryPostProcessor.StripProxySubmeshesFromVisual(
                        lodGo, result.BuildResult, result.ParsedFile, ref lodSubmeshMatIds);
                    FcBrushGeometryPostProcessor.StampCacheMetadata(
                        lodGo, brushRuntimeParity: true, lodSubmeshMatIds);
                    if (filtered != null)
                        (filteredLodMeshes ??= new List<Mesh>()).Add(filtered);
                }

                renderers.Add(meshRenderer);
            }

            // LOD siblings just got parented under root as new children; a CgfUvScrollRuntime
            // on root (added at build time, before this LOD assembly step) only saw the base
            // LOD0 renderer in its constructor-time scan. Refresh it now instead of relying on
            // a per-frame rescan in Update.
            var uvScroll = root.GetComponent<CgfUvScrollRuntime>();
            if (uvScroll != null)
                uvScroll.Refresh();

            var lodGroup = root.GetComponent<LODGroup>();
            if (lodGroup == null)
                lodGroup = root.AddComponent<LODGroup>();

            lodGroup.animateCrossFading = false;
            lodGroup.SetLODs(BuildLods(renderers));
            lodGroup.RecalculateBounds();
            return filteredLodMeshes;
        }

        static LOD[] BuildLods(List<Renderer> renderers)
        {
            int count = renderers.Count;
            const float maxScreenRelativeTransitionHeight = 0.7f;
            const float minScreenRelativeTransitionHeight = 0.02f;
            var lods = new LOD[count];

            if (count == 1)
            {
                lods[0] = new LOD(maxScreenRelativeTransitionHeight, new[] { renderers[0] });
                return lods;
            }

            float step = (maxScreenRelativeTransitionHeight - minScreenRelativeTransitionHeight) / (count - 1);
            for (int i = 0; i < count; i++)
            {
                float threshold = Mathf.Clamp(
                    maxScreenRelativeTransitionHeight - (step * i),
                    minScreenRelativeTransitionHeight,
                    maxScreenRelativeTransitionHeight);
                lods[i] = new LOD(threshold, new[] { renderers[i] });
            }

            return lods;
        }
    }
}
