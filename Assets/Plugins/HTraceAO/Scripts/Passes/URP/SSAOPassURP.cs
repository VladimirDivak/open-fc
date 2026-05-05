//pipelinedefine
#define H_URP

using HTraceAO.Scripts.Extensions;
using HTraceAO.Scripts.Globals;
using HTraceAO.Scripts.Passes.Shared.AO;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
#if UNITY_2023_3_OR_NEWER
using UnityEngine.Rendering.RenderGraphModule;
#else
using UnityEngine.Experimental.Rendering.RenderGraphModule;
#endif


namespace HTraceAO.Scripts.Passes.URP
{
    internal class SSAOPassURP : ScriptableRenderPass
    {
	    ProfilingSampler SsaoSampler = new ProfilingSampler(HNames.HTRACE_SSAO_PASS_NAME);
	    
	    private static readonly int CameraNormalsTexture = Shader.PropertyToID("_CameraNormalsTexture");

	    #region --------------------------- Non Render Graph ---------------------------
       
#if !UNITY_6000_4_OR_NEWER
	    private ScriptableRenderer _renderer;

        protected internal void Initialize(ScriptableRenderer renderer)
        {
            _renderer    = renderer;
        }

#if UNITY_2023_3_OR_NEWER
        [System.Obsolete]
#endif
        public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
        {
	        SetupShared(renderingData.cameraData.camera, renderingData.cameraData.renderScale, renderingData.cameraData.cameraTargetDescriptor);
        }

#if UNITY_2023_3_OR_NEWER
        [System.Obsolete]
#endif
        public override void Configure(CommandBuffer cmd, RenderTextureDescriptor cameraTextureDescriptor)
        {
        }

#if UNITY_2023_3_OR_NEWER
        [System.Obsolete]
#endif
        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
	        var cmd = CommandBufferPool.Get(HNames.HTRACE_SSAO_PASS_NAME);

            Camera camera = renderingData.cameraData.camera;
            float renderScale = renderingData.cameraData.renderScale;
            int width  = (int)(camera.scaledPixelWidth * renderScale);
            int height = (int)(camera.scaledPixelHeight * renderScale);

            if (Shader.GetGlobalTexture(CameraNormalsTexture) == null)
	            return;

            SSAO.Execute(cmd, camera, width, height);

            context.ExecuteCommandBuffer(cmd);
            cmd.Clear();
            CommandBufferPool.Release(cmd);
            return;
        }
#endif

        #endregion --------------------------- Non Render Graph ---------------------------

		#region --------------------------- Render Graph ---------------------------

		#if UNITY_2023_3_OR_NEWER
	    private class PassData
	    {
		    public UniversalCameraData UniversalCameraData;
	    }

		public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
		{
			using (var builder = renderGraph.AddUnsafePass<PassData>(HNames.HTRACE_SSAO_PASS_NAME, out var passData, SsaoSampler))
			{
				UniversalResourceData  resourceData           = frameData.Get<UniversalResourceData>();
				UniversalCameraData    universalCameraData    = frameData.Get<UniversalCameraData>();
				UniversalRenderingData universalRenderingData = frameData.Get<UniversalRenderingData>();
				UniversalLightData     lightData              = frameData.Get<UniversalLightData>();

				builder.AllowGlobalStateModification(true);
				builder.AllowPassCulling(false);

				passData.UniversalCameraData = universalCameraData;

				SetupShared(universalCameraData.camera, universalCameraData.renderScale, universalCameraData.cameraTargetDescriptor);

				builder.SetRenderFunc((PassData data, UnsafeGraphContext context) => ExecutePass(data, context));
			}
		}
		private static void ExecutePass(PassData data, UnsafeGraphContext rgContext)
		{
		    var cmd = CommandBufferHelpers.GetNativeCommandBuffer(rgContext.cmd);

		    Camera camera = data.UniversalCameraData.camera;
		    float renderScale = data.UniversalCameraData.renderScale;
		    int width  = (int)(camera.scaledPixelWidth * renderScale);
		    int height = (int)(camera.scaledPixelHeight * renderScale);

		    SSAO.Execute(cmd, camera, width, height);

		}
		#endif

		#endregion --------------------------- Render Graph ---------------------------

		
		#region --------------------------- Shared ---------------------------
		
		private static void SetupShared(Camera camera, float renderScale, RenderTextureDescriptor desc)
		{
		    if (SSAO.HRenderSSAO == null) SSAO.HRenderSSAO             = HExtensions.LoadComputeShader("HRenderSSAO");
		    if (SSAO.HDenoiseSSAO == null) SSAO.HDenoiseSSAO           = HExtensions.LoadComputeShader("HDenoiseSSAO");
		    if (SSAO.HDepthPyramidSSAO == null) SSAO.HDepthPyramidSSAO = HExtensions.LoadComputeShader("HDepthPyramidSSAO");

		    int width  = (int)(camera.scaledPixelWidth * renderScale);
		    int height = (int)(camera.scaledPixelHeight * renderScale);

		    if (desc.width != width || desc.height != height)
				desc = new RenderTextureDescriptor(width, height);

		    desc.depthBufferBits    = 0; // Color and depth cannot be combined in RTHandles
		    desc.stencilFormat      = GraphicsFormat.None;
		    desc.depthStencilFormat = GraphicsFormat.None;
		    desc.msaaSamples        = 1;
		    desc.bindMS             = false;
		    desc.enableRandomWrite  = true;

		    RenderTextureDescriptor depthDesc = desc;
		    //depthDesc.depthBufferBits = 32;
		    depthDesc.graphicsFormat  = GraphicsFormat.R32_SFloat;

		    // Depth textures
		    SSAO.DepthTiled_SSAO.ReAllocateIfNeeded(SSAO._DepthTiled, ref desc, width: (int)(HMath.DepthResolutionFunc(width) / 8.0f), height: (int)(HMath.DepthResolutionFunc(height) / 8.0f),
			    depth: TextureXR.slices * 16, graphicsFormat: GraphicsFormat.R16_SFloat, dimension: TextureDimension.Tex2DArray, useMipMap: true);
		    SSAO.DepthPyramid_SSAO.ReAllocateIfNeeded(SSAO._DepthPyramid_SSAO, ref depthDesc, width: HMath.DepthResolutionFunc(width) / 2, height: HMath.DepthResolutionFunc(height) / 2,
			    graphicsFormat: GraphicsFormat.R32_SFloat, useMipMap: true);
		    SSAO.DepthIntermediatePyramid_SSAO.ReAllocateIfNeeded(SSAO._DepthIntermediatePyramid_SSAO, ref depthDesc, width: HMath.DepthResolutionFunc(width) / 4, height: HMath.DepthResolutionFunc(height) / 4,
			    graphicsFormat: GraphicsFormat.R32_SFloat, useMipMap: true);

		    // SSAO textures

		    SSAO.Occlusion_SSAO_1.ReAllocateIfNeeded(SSAO._Occlusion_1, ref desc, width: width / 2, height: height / 2, graphicsFormat: GraphicsFormat.R8_UNorm, dimension: TextureDimension.Tex2D);
		    SSAO.Occlusion_SSAO_2.ReAllocateIfNeeded(SSAO._Occlusion_2, ref desc, width: width / 4, height: height / 4, graphicsFormat: GraphicsFormat.R8_UNorm, dimension: TextureDimension.Tex2D);
		    SSAO.Occlusion_SSAO_3.ReAllocateIfNeeded(SSAO._Occlusion_3, ref desc, width: width / 8, height: height / 8, graphicsFormat: GraphicsFormat.R8_UNorm, dimension: TextureDimension.Tex2D);
		    SSAO.Occlusion_SSAO_4.ReAllocateIfNeeded(SSAO._Occlusion_4, ref desc, width: width / 16, height: height / 16, graphicsFormat: GraphicsFormat.R8_UNorm, dimension: TextureDimension.Tex2D);
		    SSAO.OcclusionCombined_SSAO_0.ReAllocateIfNeeded(SSAO._OcclusionCombined_0, ref desc, width: width, height: height, graphicsFormat: GraphicsFormat.R8_UNorm, dimension: TextureDimension.Tex2D);
		    SSAO.OcclusionCombined_SSAO_1.ReAllocateIfNeeded(SSAO._OcclusionCombined_1, ref desc, width: width / 2, height: height / 2, graphicsFormat: GraphicsFormat.R8_UNorm, dimension: TextureDimension.Tex2D);
		    SSAO.OcclusionCombined_SSAO_2.ReAllocateIfNeeded(SSAO._OcclusionCombined_2, ref desc, width: width / 4, height: height / 4, graphicsFormat: GraphicsFormat.R8_UNorm, dimension: TextureDimension.Tex2D);
		    SSAO.OcclusionCombined_SSAO_3.ReAllocateIfNeeded(SSAO._OcclusionCombined_3, ref desc, width: width / 8, height: height / 8, graphicsFormat: GraphicsFormat.R8_UNorm, dimension: TextureDimension.Tex2D);
		}

		protected internal void Dispose()
		{
			SSAO.Occlusion_SSAO_1.HRelease();
			SSAO.Occlusion_SSAO_2.HRelease();
			SSAO.Occlusion_SSAO_3.HRelease();
			SSAO.Occlusion_SSAO_4.HRelease();
			SSAO.OcclusionCombined_SSAO_0.HRelease();
			SSAO.OcclusionCombined_SSAO_1.HRelease();
			SSAO.OcclusionCombined_SSAO_2.HRelease();
			SSAO.OcclusionCombined_SSAO_3.HRelease();

			SSAO.DepthTiled_SSAO.HRelease();
			SSAO.DepthPyramid_SSAO.HRelease();
			SSAO.DepthIntermediatePyramid_SSAO.HRelease();
		}

		#endregion --------------------------- Shared ---------------------------

    }
}
