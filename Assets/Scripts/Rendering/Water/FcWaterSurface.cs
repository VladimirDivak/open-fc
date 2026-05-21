using System.Collections.Generic;
using UnityEngine;

namespace OpenFarCry.Rendering.Water
{
    [DisallowMultipleComponent]
    public sealed class FcWaterSurface : MonoBehaviour
    {
        [SerializeField] Material waterMaterial;
        [SerializeField] FcWaterSettings settingsOverride;

        public Material WaterMaterial => waterMaterial;
        public FcWaterSettings Settings => settingsOverride != null ? settingsOverride : FcWaterSettings.Instance;
        public Plane WaterPlane => new Plane(Vector3.up, transform.position);
        public float WaterLevelY => transform.position.y;

        void OnEnable() => FcWaterRegistry.Register(this);
        void OnDisable() => FcWaterRegistry.Unregister(this);
    }

    public static class FcWaterRegistry
    {
        static readonly List<FcWaterSurface> s_surfaces = new List<FcWaterSurface>(4);

        public static IReadOnlyList<FcWaterSurface> All => s_surfaces;
        public static int Count => s_surfaces.Count;

        public static void Register(FcWaterSurface surface)
        {
            if (surface != null && !s_surfaces.Contains(surface))
                s_surfaces.Add(surface);
        }

        public static void Unregister(FcWaterSurface surface)
        {
            if (surface != null)
                s_surfaces.Remove(surface);
        }

        public static FcWaterSurface FindClosest(Vector3 worldPosition)
        {
            FcWaterSurface best = null;
            float bestSqr = float.MaxValue;
            for (int i = 0; i < s_surfaces.Count; i++)
            {
                var s = s_surfaces[i];
                if (s == null) continue;
                float d = (s.transform.position - worldPosition).sqrMagnitude;
                if (d < bestSqr)
                {
                    bestSqr = d;
                    best = s;
                }
            }
            return best;
        }
    }

    [DisallowMultipleComponent, AddComponentMenu("")]
    public sealed class FcReflectionCameraTag : MonoBehaviour
    {
    }
}
