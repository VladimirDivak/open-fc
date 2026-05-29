using System.Collections.Generic;
using UnityEngine;

namespace OpenFarCry.Rendering.Water
{
    /// <summary>
    /// Per-source-camera planar reflection state (P3 pool). Each game/scene camera gets its own
    /// mirror camera + render texture so Scene view, Game view and split-screen never share state.
    /// Also holds the last-rendered view/projection for temporal reuse (P2).
    /// </summary>
    sealed class FcReflectionView
    {
        public Camera MirrorCam;
        public RenderTexture Rt;
        public int RtWidth;
        public int RtHeight;
        public bool RtHDR;

        public Matrix4x4 LastView;
        public Matrix4x4 LastProj;
        public bool HasRendered;

        public void Release()
        {
            if (Rt != null)
            {
                Rt.Release();
                if (Application.isPlaying) Object.Destroy(Rt); else Object.DestroyImmediate(Rt);
                Rt = null;
            }
            if (MirrorCam != null)
            {
                if (Application.isPlaying) Object.Destroy(MirrorCam.gameObject);
                else Object.DestroyImmediate(MirrorCam.gameObject);
                MirrorCam = null;
            }
        }
    }

    sealed class FcReflectionCache
    {
        readonly Dictionary<Camera, FcReflectionView> _views = new Dictionary<Camera, FcReflectionView>(4);
        readonly List<Camera> _dead = new List<Camera>(4);

        public FcReflectionView GetOrCreate(Camera src)
        {
            if (_views.TryGetValue(src, out var v))
                return v;
            v = new FcReflectionView();
            _views.Add(src, v);
            return v;
        }

        /// <summary>Drop views whose source camera was destroyed.</summary>
        public void CollectGarbage()
        {
            _dead.Clear();
            foreach (var kv in _views)
                if (kv.Key == null)
                    _dead.Add(kv.Key);
            for (int i = 0; i < _dead.Count; i++)
            {
                if (_views.TryGetValue(_dead[i], out var v))
                    v.Release();
                _views.Remove(_dead[i]);
            }
        }

        public void ReleaseAll()
        {
            foreach (var kv in _views)
                kv.Value?.Release();
            _views.Clear();
            _dead.Clear();
        }
    }
}
