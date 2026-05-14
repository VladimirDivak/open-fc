using UnityEngine;

namespace OpenFarCry.Level.Services
{
    public static class FcVegetationLodMath
    {
        public static float BuildCullDistanceSqr(float lod0Distance, float cullDistance)
        {
            float lod0 = Mathf.Max(1f, lod0Distance);
            float cull = Mathf.Max(lod0 + 0.01f, cullDistance);
            return cull * cull;
        }

        public static float[] BuildLodThresholdsSqr(int lodCount, float lod0Distance, float cullDistance)
        {
            var thresholds = new float[Mathf.Max(1, lodCount)];
            if (lodCount <= 1)
            {
                thresholds[0] = BuildCullDistanceSqr(lod0Distance, cullDistance);
                return thresholds;
            }

            float lod0 = Mathf.Max(1f, lod0Distance);
            float cull = Mathf.Max(lod0 + 0.01f, cullDistance);
            float ratio = Mathf.Pow(cull / lod0, 1f / (lodCount - 1));

            for (int i = 0; i < lodCount; i++)
            {
                float dist = i == lodCount - 1
                    ? cull
                    : lod0 * Mathf.Pow(ratio, i);
                thresholds[i] = dist * dist;
            }

            return thresholds;
        }

        // Returns the LOD index for the given squared distance, -1 if beyond cull distance.
        public static int FindLod(float[] lodThresholdsSqr, float cullDistanceSqr, int lodMeshCount, float sqrDist)
        {
            if (lodMeshCount <= 0 || lodThresholdsSqr == null || lodThresholdsSqr.Length == 0)
                return -1;
            if (sqrDist >= cullDistanceSqr)
                return -1;

            int max = Mathf.Min(lodMeshCount, lodThresholdsSqr.Length);
            for (int li = 0; li < max; li++)
            {
                if (sqrDist < lodThresholdsSqr[li])
                    return li;
            }

            return lodMeshCount - 1;
        }
    }
}
