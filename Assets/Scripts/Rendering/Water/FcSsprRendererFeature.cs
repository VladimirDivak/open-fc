using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace OpenFarCry.Rendering.Water
{
    /// <summary>
    /// Screen-space planar reflection (P6). Fills the same global `_FcWaterReflectionTex` the mirror-camera
    /// planar path uses, so FarCryWater.shader needs no SSPR-specific branch. Active only on the `Sspr`
    /// quality tier; the mirror-cam feature stays the path for High/Ultra.
    ///
    /// Manual setup: add this feature to `PC_Renderer.asset` and assign `FcSsprCompute.compute`.
    /// </summary>
    public sealed class FcSsprRendererFeature : ScriptableRendererFeature
    {
        [SerializeField] ComputeShader ssprCompute;

        SsprPass _pass;

        public override void Create()
        {
            _pass = new SsprPass(ssprCompute)
            {
                renderPassEvent = RenderPassEvent.BeforeRenderingTransparents
            };
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            if (ssprCompute == null || _pass == null) return;
            if (FcWaterRegistry.Count == 0) return;

            var cam = renderingData.cameraData.camera;
            if (cam.cameraType != CameraType.Game && cam.cameraType != CameraType.SceneView) return;

            var settings = FcWaterSettings.Instance;
            if (settings == null) return;
            var tier = FcWaterQualityTierResolver.Resolve(settings);
            if (!FcWaterQualityTierResolver.UsesSspr(tier)) return;

            renderer.EnqueuePass(_pass);
        }

        sealed class SsprPass : ScriptableRenderPass
        {
            static readonly int s_ReflectionTexId = Shader.PropertyToID("_FcWaterReflectionTex");
            static readonly int s_HashId = Shader.PropertyToID("_HashBuffer");
            static readonly int s_ResultId = Shader.PropertyToID("_ReflectionResult");
            static readonly int s_DepthId = Shader.PropertyToID("_SsprDepth");
            static readonly int s_ColorId = Shader.PropertyToID("_SsprColor");
            static readonly int s_RTSizeId = Shader.PropertyToID("_SsprRTSize");
            static readonly int s_WaterHeightId = Shader.PropertyToID("_SsprWaterHeight");
            static readonly int s_InvVPId = Shader.PropertyToID("_SsprInvVP");
            static readonly int s_VPId = Shader.PropertyToID("_SsprVP");

            readonly ComputeShader _cs;
            readonly int _kClear, _kProject, _kResolve;
            readonly bool _valid;

            public SsprPass(ComputeShader cs)
            {
                _cs = cs;
                if (cs != null)
                {
                    _kClear = cs.FindKernel("KClear");
                    _kProject = cs.FindKernel("KProject");
                    _kResolve = cs.FindKernel("KResolve");
                    _valid = true;
                }
            }

            class PassData
            {
                public ComputeShader cs;
                public int kClear, kProject, kResolve;
                public int width, height, groupsX, groupsY;
                public Vector4 rtSize;
                public Matrix4x4 invVP, vp;
                public float waterHeight;
                public TextureHandle depth, color, result;
                public BufferHandle hash;
            }

            public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
            {
                if (!_valid) return;

                var resources = frameData.Get<UniversalResourceData>();
                var camData = frameData.Get<UniversalCameraData>();

                var surface = FcWaterRegistry.FindClosest(camData.camera.transform.position);
                if (surface == null) return;

                var settings = FcWaterSettings.Instance;
                var tier = FcWaterQualityTierResolver.Resolve(settings);
                int width = Mathf.Max(64, FcWaterQualityTierResolver.ReflectionResolution(tier, settings));
                float aspect = camData.camera.aspect > 0f ? camData.camera.aspect : 16f / 9f;
                int height = Mathf.Max(64, Mathf.RoundToInt(width / aspect));

                var desc = new TextureDesc(width, height)
                {
                    format = GraphicsFormat.B10G11R11_UFloatPack32,
                    enableRandomWrite = true,
                    clearBuffer = false,
                    filterMode = FilterMode.Trilinear,
                    useMipMap = true,
                    autoGenerateMips = true,
                    name = "FcSsprReflection"
                };
                var result = renderGraph.CreateTexture(desc);
                var hash = renderGraph.CreateBuffer(new BufferDesc(width * height, sizeof(uint))
                {
                    name = "FcSsprHash"
                });

                Matrix4x4 view = camData.camera.worldToCameraMatrix;
                Matrix4x4 proj = GL.GetGPUProjectionMatrix(camData.camera.projectionMatrix, true);
                Matrix4x4 vp = proj * view;

                using (var builder = renderGraph.AddComputePass<PassData>("FcWater.SSPR", out var data))
                {
                    data.cs = _cs;
                    data.kClear = _kClear;
                    data.kProject = _kProject;
                    data.kResolve = _kResolve;
                    data.width = width;
                    data.height = height;
                    data.groupsX = Mathf.CeilToInt(width / 8f);
                    data.groupsY = Mathf.CeilToInt(height / 8f);
                    data.rtSize = new Vector4(width, height, 1f / width, 1f / height);
                    data.vp = vp;
                    data.invVP = vp.inverse;
                    data.waterHeight = surface.WaterLevelY;
                    data.depth = resources.cameraDepthTexture;
                    data.color = resources.cameraOpaqueTexture;
                    data.result = result;
                    data.hash = hash;

                    builder.UseTexture(data.depth);
                    builder.UseTexture(data.color);
                    builder.UseTexture(data.result, AccessFlags.Write);
                    builder.UseBuffer(data.hash, AccessFlags.ReadWrite);
                    builder.AllowPassCulling(false);
                    builder.SetGlobalTextureAfterPass(data.result, s_ReflectionTexId);

                    builder.SetRenderFunc((PassData d, ComputeGraphContext ctx) =>
                    {
                        var cmd = ctx.cmd;
                        cmd.SetComputeVectorParam(d.cs, s_RTSizeId, d.rtSize);
                        cmd.SetComputeFloatParam(d.cs, s_WaterHeightId, d.waterHeight);
                        cmd.SetComputeMatrixParam(d.cs, s_InvVPId, d.invVP);
                        cmd.SetComputeMatrixParam(d.cs, s_VPId, d.vp);

                        cmd.SetComputeBufferParam(d.cs, d.kClear, s_HashId, d.hash);
                        cmd.SetComputeTextureParam(d.cs, d.kClear, s_ResultId, d.result);
                        cmd.DispatchCompute(d.cs, d.kClear, d.groupsX, d.groupsY, 1);

                        cmd.SetComputeBufferParam(d.cs, d.kProject, s_HashId, d.hash);
                        cmd.SetComputeTextureParam(d.cs, d.kProject, s_DepthId, d.depth);
                        cmd.DispatchCompute(d.cs, d.kProject, d.groupsX, d.groupsY, 1);

                        cmd.SetComputeBufferParam(d.cs, d.kResolve, s_HashId, d.hash);
                        cmd.SetComputeTextureParam(d.cs, d.kResolve, s_ColorId, d.color);
                        cmd.SetComputeTextureParam(d.cs, d.kResolve, s_ResultId, d.result);
                        cmd.DispatchCompute(d.cs, d.kResolve, d.groupsX, d.groupsY, 1);
                    });
                }
            }
        }
    }
}
