using System.Collections.Generic;
using OpenFarCry.Importer.Cgf;
using UnityEngine;

namespace OpenFarCry.Level.Services
{
    static class FcLevelRuntimeLodGroupBuilder
    {
        public static void Apply(
            GameObject root,
            Renderer lod0Renderer,
            IReadOnlyList<CgfRuntimeImportResult> lodResults,
            string levelScopeId)
        {
            if (root == null || lod0Renderer == null || lodResults == null || lodResults.Count == 0)
                return;

            var renderers = new List<Renderer>(lodResults.Count + 1) { lod0Renderer };

            for (int i = 0; i < lodResults.Count; i++)
            {
                var result = lodResults[i];
                if (result?.BuildResult?.Mesh == null)
                    continue;

                var lodGo = new GameObject($"LOD{i + 1}");
                lodGo.transform.SetParent(root.transform, worldPositionStays: false);

                lodGo.AddComponent<MeshFilter>().sharedMesh = result.BuildResult.Mesh;
                var meshRenderer = lodGo.AddComponent<MeshRenderer>();
                meshRenderer.sharedMaterials = CgfRuntimeImporter.MaterialService.ResolveSubmeshMaterials(
                    result.ParsedFile,
                    result.Mesh,
                    result.BuildResult.SubmeshMaterialIds,
                    levelScopeId);
                renderers.Add(meshRenderer);
            }

            var lodGroup = root.GetComponent<LODGroup>();
            if (lodGroup == null)
                lodGroup = root.AddComponent<LODGroup>();

            lodGroup.animateCrossFading = false;
            lodGroup.SetLODs(BuildLods(renderers));
            lodGroup.RecalculateBounds();
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
