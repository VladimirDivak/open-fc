//pipelinedefine
#define H_URP

using HTraceAO.Scripts.Extensions;
using HTraceAO.Scripts.Globals;
using HTraceAO.Scripts.Passes.Shared.AO;
using HTraceAO.Scripts.Wrappers;
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
	internal class GTAOPassURP : ScriptableRenderPass
	{
		private static readonly int CameraNormalsTexture = Shader.PropertyToID("_CameraNormalsTexture");
		
		ProfilingSampler GtaoSampler = new ProfilingSampler(HNames.HTRACE_GTAO_PASS_NAME);

		#region --------------------------- Non Render Graph ---------------------------

#if !UNITY_6000_4_OR_NEWER
		private ScriptableRenderer _renderer;

		protected internal void Initialize(ScriptableRenderer renderer)
		{
			_renderer = renderer;
		}

#if UNITY_2023_3_OR_NEWER
		[System.Obsolete]
#endif
		public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
		{
			GTAO.CameraHistorySystem.SyncCamera(renderingData.cameraData.camera.GetHashCode(), Time.frameCount);

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
			var cmd = CommandBufferPool.Get(HNames.HTRACE_GTAO_PASS_NAME);

			Camera camera = renderingData.cameraData.camera;
			float renderScale = renderingData.cameraData.renderScale;
			int width  = (int)(camera.scaledPixelWidth * renderScale);
			int height = (int)(camera.scaledPixelHeight * renderScale);

			if (Shader.GetGlobalTexture(CameraNormalsTexture) == null)
				return;

			GTAO.Execute(cmd, camera, width, height);

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

			using (var builder = renderGraph.AddUnsafePass<PassData>(HNames.HTRACE_GTAO_PASS_NAME, out var passData, GtaoSampler))
			{
				UniversalResourceData  resourceData           = frameData.Get<UniversalResourceData>();
				UniversalCameraData    universalCameraData    = frameData.Get<UniversalCameraData>();
				UniversalRenderingData universalRenderingData = frameData.Get<UniversalRenderingData>();
				UniversalLightData     lightData              = frameData.Get<UniversalLightData>();

				builder.AllowGlobalStateModification(true);
				builder.AllowPassCulling(false);

				passData.UniversalCameraData = universalCameraData;

				GTAO.CameraHistorySystem.SyncCamera(universalCameraData.camera.GetHashCode(), Time.frameCount);

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

			GTAO.Execute(cmd, camera, width, height);
		}
#endif

		#endregion --------------------------- Render Graph ---------------------------

		#region ------------------------------------ Shared ------------------------------------


		private static void SetupShared(Camera camera, float renderScale, RenderTextureDescriptor desc)
		{
			if (GTAO.HDepthPyramid == null) GTAO.HDepthPyramid               = HExtensions.LoadComputeShader("HDepthPyramidGTAO");
			if (GTAO.HRenderGTAO == null) GTAO.HRenderGTAO                   = HExtensions.LoadComputeShader("HRenderGTAO");
			if (GTAO.HDenoiseGTAO == null) GTAO.HDenoiseGTAO                 = HExtensions.LoadComputeShader("HDenoiserGTAO");
			if (GTAO.HInterpolationGTAO == null) GTAO.HInterpolationGTAO     = HExtensions.LoadComputeShader("HInterpolation");
			if (GTAO.HCheckerboardingGTAO == null) GTAO.HCheckerboardingGTAO = HExtensions.LoadComputeShader("HCheckerboarding");

			int width  = (int)(camera.scaledPixelWidth * renderScale);
			int height = (int)(camera.scaledPixelHeight * renderScale);
			if (desc.width != width || desc.height != height)
				desc = new RenderTextureDescriptor(width, height);

			// Debug.Log($"All params in cameraTargetDescriptor:   width: {desc.width},  height:{desc.height},  volumeDepth: {desc.volumeDepth},  depthBufferBits: {desc.depthBufferBits},  \n" +
			//           $"graphicsFormat: {desc.graphicsFormat},  colorFormat: {desc.colorFormat},  stencilFormat: {desc.stencilFormat},  msaaSamples: {desc.msaaSamples},  \n" +
			//           $"useMipMap: {desc.useMipMap},  autoGenerateMips: {desc.autoGenerateMips},  mipCount: {desc.mipCount},  \n" +
			//           $"enableRandomWrite: {desc.enableRandomWrite},  useDynamicScale: {desc.useDynamicScale}, ");

			desc.depthBufferBits    = 0; // Color and depth cannot be combined in RTHandles
			desc.stencilFormat      = GraphicsFormat.None;
			desc.depthStencilFormat = GraphicsFormat.None;
			desc.msaaSamples        = 1;
			desc.bindMS             = false;
			desc.enableRandomWrite  = true;

			ref var cameraData = ref GTAO.CameraHistorySystem.GetCameraData();
			if (cameraData.NormalHistory_GTAO == null)
				cameraData.NormalHistory_GTAO = new RTWrapper();
			if (cameraData.OcclusionHistory_GTAO == null)
				cameraData.OcclusionHistory_GTAO = new RTWrapper();

			GTAO.DepthPyramidRT.ReAllocateIfNeeded(GTAO._DepthPyramid2, ref desc, graphicsFormat: GraphicsFormat.R16_SFloat, useMipMap: true);
			GTAO.Occlusion_GTAO.ReAllocateIfNeeded(GTAO._Occlusion, ref desc, graphicsFormat: GraphicsFormat.R8_UInt);
			GTAO.OcclusionAccumulated_GTAO.ReAllocateIfNeeded(GTAO._OcclusionAccumulated, ref desc, graphicsFormat: GraphicsFormat.R32_UInt);
			GTAO.OcclusionReprojected_GTAO.ReAllocateIfNeeded(GTAO._OcclusionReprojected,ref desc, graphicsFormat: GraphicsFormat.R32_UInt);
			GTAO.OcclusionFiltered_GTAO.ReAllocateIfNeeded(GTAO._OcclusionFiltered, ref desc, graphicsFormat: GraphicsFormat.R32_UInt);
			GTAO.OcclusionInterpolated_GTAO.ReAllocateIfNeeded(GTAO._OcclusionInterpolated, ref desc, graphicsFormat: GraphicsFormat.R8_UNorm);

			cameraData.NormalHistory_GTAO.ReAllocateIfNeeded(GTAO._NormalHistory,  ref desc, graphicsFormat: GraphicsFormat.R8G8B8A8_UNorm);
			cameraData.OcclusionHistory_GTAO.ReAllocateIfNeeded(GTAO._OcclusionHistory, ref desc, graphicsFormat: GraphicsFormat.R32_UInt);

			if (GTAO.RayCounter == null)
			{
				GTAO.RayCounter = new ComputeBuffer(2 * HRenderer.TextureXrSlices, sizeof(uint));
				uint[] zeroArray = new uint[2 * HRenderer.TextureXrSlices];
				GTAO.RayCounter.SetData(zeroArray);
			}

			if (GTAO.IndirectCoords == null) GTAO.IndirectCoords       = new HDynamicBuffer(BufferType.ComputeBuffer, 2 * sizeof(uint), HRenderer.TextureXrSlices, avoidDownscale: false);
			if (GTAO.IndirectArguments == null) GTAO.IndirectArguments = new ComputeBuffer(3 * HRenderer.TextureXrSlices, sizeof(int), ComputeBufferType.IndirectArguments);
		}

		protected internal void Dispose()
		{
			GTAO.CameraHistorySystem.Cleanup();

			GTAO.DepthPyramidRT?.HRelease();
			GTAO.Occlusion_GTAO?.HRelease();
			GTAO.OcclusionAccumulated_GTAO?.HRelease();
			GTAO.OcclusionReprojected_GTAO?.HRelease();
			GTAO.OcclusionFiltered_GTAO?.HRelease();
			GTAO.OcclusionInterpolated_GTAO?.HRelease();

			GTAO.RayCounter.HRelease();
			GTAO.IndirectCoords.HRelease();
			GTAO.IndirectArguments.HRelease();
			GTAO.RayCounter = null;
			GTAO.IndirectCoords = null;
			GTAO.IndirectArguments = null;
		}

		#endregion
	}
}
