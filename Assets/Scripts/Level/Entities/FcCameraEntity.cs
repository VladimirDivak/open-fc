using OpenFarCry.Level.Data;
using UnityEngine;

namespace OpenFarCry.Level.Entities
{
    [RequireComponent(typeof(Camera))]
    public class FcCameraEntity : FcEntity
    {
        [Header("Camera")]
        [SerializeField] float _fov      = 60f;
        [SerializeField] float _nearClip = 0.1f;
        [SerializeField] float _farClip  = 1000f;

        protected override void Awake()
        {
            base.Awake();
            ApplyCamera();
        }

        void ApplyCamera()
        {
            var cam = GetComponent<Camera>();
            cam.enabled       = false;
            cam.fieldOfView   = _fov;
            cam.nearClipPlane = _nearClip;
            cam.farClipPlane  = _farClip;
        }

        public override void SetData(FcEntityDesc desc)
        {
            base.SetData(desc);
            if (TryGetFloat(desc, "fov",       out float f))  _fov      = f;
            if (TryGetFloat(desc, "fNearClip", out float nc)) _nearClip = nc;
            if (TryGetFloat(desc, "fFarClip",  out float fc)) _farClip  = fc;
        }

        static bool TryGetFloat(FcEntityDesc desc, string key, out float value)
        {
            value = 0f;
            return desc.Properties.TryGetValue(key, out string s) &&
                   float.TryParse(s, System.Globalization.NumberStyles.Float,
                       System.Globalization.CultureInfo.InvariantCulture, out value);
        }

#if UNITY_EDITOR
        void OnValidate()
        {
            if (!Application.isPlaying && GetComponent<Camera>() is { } cam)
            {
                cam.fieldOfView   = _fov;
                cam.nearClipPlane = _nearClip;
                cam.farClipPlane  = _farClip;
            }
        }
#endif
    }
}
