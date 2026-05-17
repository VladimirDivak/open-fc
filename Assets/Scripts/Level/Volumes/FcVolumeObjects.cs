using UnityEngine;

namespace OpenFarCry.Level.Volumes
{
    // Stub for CryEngine VisArea — defines a convex polygon room for PVS culling.
    // Points are world-space polygon vertices at the base plane; Height extrudes upward.
    public sealed class FcVisAreaVolume : MonoBehaviour
    {
        [SerializeField] public Vector3[] Points;
        [SerializeField] public float Height;
        [SerializeField] public Color AmbientColor = Color.gray;
        [SerializeField] public Color DynAmbientColor = Color.gray;
        [SerializeField] public bool AffectedBySun;
        [SerializeField] public bool SkyOnly;
        [SerializeField] public int ViewDistRatio = 100;
        [SerializeField] public bool Closed = true;

#if UNITY_EDITOR
        void OnDrawGizmosSelected()
        {
            if (Points == null || Points.Length < 2) return;
            Gizmos.color = new Color(0.2f, 0.6f, 1f, 0.5f);
            DrawPolygonExtruded(Points, Height);
        }

        internal static void DrawPolygonExtruded(Vector3[] pts, float height)
        {
            int n = pts.Length;
            Vector3 up = Vector3.up * height;
            for (int i = 0; i < n; i++)
            {
                Vector3 a = pts[i], b = pts[(i + 1) % n];
                Gizmos.DrawLine(a, b);
                Gizmos.DrawLine(a + up, b + up);
                Gizmos.DrawLine(a, a + up);
            }
        }
#endif
    }

    // Stub for CryEngine Portal — a planar quadrilateral connecting two VisAreas.
    public sealed class FcPortalVolume : MonoBehaviour
    {
        [SerializeField] public Vector3[] Points;
        [SerializeField] public float Height;
        [SerializeField] public bool DoubleSide = true;
        [SerializeField] public Color AmbientColor = Color.gray;

#if UNITY_EDITOR
        void OnDrawGizmosSelected()
        {
            if (Points == null || Points.Length < 2) return;
            Gizmos.color = new Color(1f, 0.8f, 0.1f, 0.6f);
            FcVisAreaVolume.DrawPolygonExtruded(Points, Height);
        }
#endif
    }

    // Stub for CryEngine OccluderArea — polygon occluder plane for CPU occlusion culling.
    public sealed class FcOccluderAreaVolume : MonoBehaviour
    {
        [SerializeField] public Vector3[] Points;
        [SerializeField] public float Height;
        [SerializeField] public bool UseInIndoors;

#if UNITY_EDITOR
        void OnDrawGizmosSelected()
        {
            if (Points == null || Points.Length < 2) return;
            Gizmos.color = new Color(1f, 0.3f, 0.3f, 0.5f);
            FcVisAreaVolume.DrawPolygonExtruded(Points, Height);
        }
#endif
    }

    // Stub for CryEngine FogVolume — AABB-bounded local fog.
    public sealed class FcFogVolume : MonoBehaviour
    {
        [SerializeField] public Color FogColor = Color.white;
        [SerializeField] public float ViewDistance = 50f;
        [SerializeField] public float Width;
        [SerializeField] public float Height;
        [SerializeField] public float Length;

#if UNITY_EDITOR
        void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(FogColor.r, FogColor.g, FogColor.b, 0.3f);
            Gizmos.DrawWireCube(transform.position, new Vector3(Width, Height, Length));
        }
#endif
    }

    // Stub for CryEngine WaterVolume — polygon water body.
    public sealed class FcWaterVolume : MonoBehaviour
    {
        [SerializeField] public Vector3[] Points;
        [SerializeField] public float Height;
        [SerializeField] public string Material;
        [SerializeField] public string WaterShader;
        [SerializeField] public float WaterSpeed;

#if UNITY_EDITOR
        void OnDrawGizmosSelected()
        {
            if (Points == null || Points.Length < 2) return;
            Gizmos.color = new Color(0.1f, 0.5f, 1f, 0.4f);
            FcVisAreaVolume.DrawPolygonExtruded(Points, Height);
        }
#endif
    }
}
