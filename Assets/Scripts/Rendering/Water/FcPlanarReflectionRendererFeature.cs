using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace OpenFarCry.Rendering.Water
{
    public sealed class FcPlanarReflectionRendererFeature : ScriptableRendererFeature
    {
        static readonly int s_ReflectionTexId = Shader.PropertyToID("_FcWaterReflectionTex");

        Camera _reflectionCam;
        RenderTexture _rt;
        int _rtResolution;
        bool _rtHDR;
        bool _subscribed;

        public override void Create()
        {
            Subscribe();
        }

        protected override void Dispose(bool disposing)
        {
            Unsubscribe();
            if (_reflectionCam != null)
            {
                if (Application.isPlaying)
                    Destroy(_reflectionCam.gameObject);
                else
                    DestroyImmediate(_reflectionCam.gameObject);
                _reflectionCam = null;
            }
            if (_rt != null)
            {
                _rt.Release();
                if (Application.isPlaying)
                    Destroy(_rt);
                else
                    DestroyImmediate(_rt);
                _rt = null;
            }
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            // Reflection render runs in beginCameraRendering; nothing to inject into the main render graph.
        }

        void Subscribe()
        {
            if (_subscribed) return;
            RenderPipelineManager.beginCameraRendering += OnBeginCameraRendering;
            _subscribed = true;
        }

        void Unsubscribe()
        {
            if (!_subscribed) return;
            RenderPipelineManager.beginCameraRendering -= OnBeginCameraRendering;
            _subscribed = false;
        }

        void OnBeginCameraRendering(ScriptableRenderContext ctx, Camera srcCam)
        {
            if (srcCam == null) return;
            if (srcCam.GetComponent<FcReflectionCameraTag>() != null) return;
            if (srcCam.cameraType != CameraType.Game) return;
            if (FcWaterRegistry.Count == 0) return;

            var settings = FcWaterSettings.Instance;
            if (settings == null) return;

            var tier = FcWaterQualityTierResolver.Resolve(settings);
            if (!FcWaterQualityTierResolver.UsesPlanar(tier)) return;

            var surface = FcWaterRegistry.FindClosest(srcCam.transform.position);
            if (surface == null) return;

            if (srcCam.transform.position.y < surface.WaterLevelY + 0.001f)
                return; // camera below water: skip planar reflection (would mirror upward)

            int res = FcWaterQualityTierResolver.ReflectionResolution(tier, settings);
            float aspect = srcCam.aspect > 0f ? srcCam.aspect : 16f / 9f;
            EnsureRT(res, aspect, settings.ReflectionHDR);
            EnsureReflectionCamera(srcCam, settings);

            CopyCameraData(srcCam, _reflectionCam, settings);
            // Exclude the "Water" layer to prevent recursive self-reflection. Only effective if the layer exists
            // AND the water GO is on it (BuildWaterPlane auto-installs and assigns). Layer 0 (Default) is never
            // excluded here because that would mask out most of the scene.
            int waterLayer = LayerMask.NameToLayer("Water");
            if (waterLayer > 0)
                _reflectionCam.cullingMask &= ~(1 << waterLayer);
            else if (surface.gameObject.layer > 0)
                _reflectionCam.cullingMask &= ~(1 << surface.gameObject.layer);
            SetupMirrorMatrices(srcCam, surface, settings);

            if (settings.VerboseLogging)
            {
                float expectedMirrorY = 2f * surface.WaterLevelY - srcCam.transform.position.y;
                Debug.Log(
                    $"[FcWater] srcCam=({srcCam.transform.position}) waterY={surface.WaterLevelY:F2} " +
                    $"mirrorCamPos=({_reflectionCam.transform.position}) expectedMirrorY={expectedMirrorY:F2} " +
                    $"srcCamFwd={srcCam.transform.forward} mirrorFwd={_reflectionCam.transform.forward} " +
                    $"RT={_rt.width}x{_rt.height} aspect={srcCam.aspect:F2}");
            }

            var request = new UniversalRenderPipeline.SingleCameraRequest { destination = _rt };
            if (!RenderPipeline.SupportsRenderRequest(_reflectionCam, request))
                return;

            bool prevInvert = GL.invertCulling;
            GL.invertCulling = true;
            try
            {
                RenderPipeline.SubmitRenderRequest(_reflectionCam, request);
            }
            finally
            {
                GL.invertCulling = prevInvert;
            }

            Shader.SetGlobalTexture(s_ReflectionTexId, _rt);
        }

        void EnsureRT(int resolution, float aspect, bool hdr)
        {
            int height = Mathf.Max(64, Mathf.RoundToInt(resolution / aspect));
            if (_rt != null && _rt.width == resolution && _rt.height == height && _rtHDR == hdr)
                return;

            if (_rt != null)
            {
                _rt.Release();
                if (Application.isPlaying) Destroy(_rt); else DestroyImmediate(_rt);
            }

            var format = hdr ? RenderTextureFormat.RGB111110Float : RenderTextureFormat.ARGB32;
            _rt = new RenderTexture(resolution, height, 24, format, RenderTextureReadWrite.Default)
            {
                name = "FcWater_PlanarReflection",
                useMipMap = true,
                autoGenerateMips = true,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Trilinear,
                antiAliasing = 1,
                hideFlags = HideFlags.HideAndDontSave
            };
            _rt.Create();
            _rtResolution = resolution;
            _rtHDR = hdr;
        }

        void EnsureReflectionCamera(Camera src, FcWaterSettings settings)
        {
            if (_reflectionCam != null) return;

            var go = new GameObject("FcWater_ReflectionCam")
            {
                hideFlags = HideFlags.HideAndDontSave
            };
            _reflectionCam = go.AddComponent<Camera>();
            go.AddComponent<FcReflectionCameraTag>();

            var data = _reflectionCam.GetUniversalAdditionalCameraData();
            _reflectionCam.enabled = false;
            data.renderShadows = settings.ReflectShadows;
            data.renderPostProcessing = false;
            data.requiresColorOption = CameraOverrideOption.Off;
            data.requiresDepthOption = CameraOverrideOption.Off;
            data.renderType = CameraRenderType.Base;
        }

        void CopyCameraData(Camera src, Camera dst, FcWaterSettings settings)
        {
            dst.CopyFrom(src);
            dst.enabled = false;
            dst.targetTexture = null;
            dst.cullingMask = settings.ReflectionLayers.value;
            dst.allowHDR = settings.ReflectionHDR;
            dst.allowMSAA = false;
            dst.clearFlags = settings.ReflectSkybox ? CameraClearFlags.Skybox : CameraClearFlags.SolidColor;
            if (settings.ReflectionFarClip > 0f)
                dst.farClipPlane = settings.ReflectionFarClip;

            var dstData = dst.GetUniversalAdditionalCameraData();
            dstData.renderShadows = settings.ReflectShadows;
            dstData.renderPostProcessing = false;
            dstData.renderType = CameraRenderType.Base;
            dstData.requiresColorOption = CameraOverrideOption.Off;
            dstData.requiresDepthOption = CameraOverrideOption.Off;
        }

        void SetupMirrorMatrices(Camera src, FcWaterSurface surface, FcWaterSettings settings)
        {
            var planeNormal = Vector3.up;
            float planeY = surface.WaterLevelY;
            float offset = settings.ClipPlaneOffset;

            // World-space plane equation: dot(N, P) + d = 0, with N = up.
            // We bias the plane slightly upward so that near-surface geometry does not flicker.
            Vector3 planePoint = new Vector3(0f, planeY + offset, 0f);
            float d = -Vector3.Dot(planeNormal, planePoint);
            var reflectionMatrix = CalculateReflectionMatrix(planeNormal, d);

            // Approximate transform (for editor display + culling); the exact view comes from worldToCameraMatrix.
            Vector3 reflectedPos = reflectionMatrix.MultiplyPoint(src.transform.position);
            Vector3 reflectedForward = Vector3.Reflect(src.transform.forward, planeNormal);
            _reflectionCam.transform.position = reflectedPos;
            _reflectionCam.transform.rotation = Quaternion.LookRotation(reflectedForward, Vector3.up);

            // Exact reflected view matrix overrides Unity's transform-derived computation.
            // Quaternions cannot represent the left-handed reflected basis, so we must override.
            _reflectionCam.worldToCameraMatrix = src.worldToCameraMatrix * reflectionMatrix;

            // Oblique near-plane clip in CAMERA space (use the overridden view matrix).
            // For a mirror cam below the water plane, world-up under mirror.worldToCameraMatrix maps to
            // cam-space -Y; CalculateObliqueMatrix keeps the half-space the normal points to, which is the
            // hemisphere containing above-water world geometry. No sign flip needed.
            Vector3 camSpacePos = _reflectionCam.worldToCameraMatrix.MultiplyPoint(planePoint);
            Vector3 camSpaceNormal = _reflectionCam.worldToCameraMatrix.MultiplyVector(planeNormal).normalized;
            var clipPlane = new Vector4(camSpaceNormal.x, camSpaceNormal.y, camSpaceNormal.z,
                -Vector3.Dot(camSpacePos, camSpaceNormal));

            // Build base projection from src (CopyFrom already copied fov/near/far/aspect), then make oblique.
            _reflectionCam.ResetProjectionMatrix();
            _reflectionCam.projectionMatrix = _reflectionCam.CalculateObliqueMatrix(clipPlane);

            // Frustum culling normally derives from the transform; since our view matrix is overridden
            // (and quaternions cannot represent the reflected basis), we must override cullingMatrix too
            // so URP renders everything the reflected camera actually sees, not the transform's view.
            _reflectionCam.cullingMatrix = _reflectionCam.projectionMatrix * _reflectionCam.worldToCameraMatrix;
        }

        static Matrix4x4 CalculateReflectionMatrix(Vector3 n, float d)
        {
            var m = new Matrix4x4();
            m.m00 = 1f - 2f * n.x * n.x;
            m.m01 = -2f * n.x * n.y;
            m.m02 = -2f * n.x * n.z;
            m.m03 = -2f * n.x * d;

            m.m10 = -2f * n.y * n.x;
            m.m11 = 1f - 2f * n.y * n.y;
            m.m12 = -2f * n.y * n.z;
            m.m13 = -2f * n.y * d;

            m.m20 = -2f * n.z * n.x;
            m.m21 = -2f * n.z * n.y;
            m.m22 = 1f - 2f * n.z * n.z;
            m.m23 = -2f * n.z * d;

            m.m30 = 0f;
            m.m31 = 0f;
            m.m32 = 0f;
            m.m33 = 1f;
            return m;
        }
    }
}
