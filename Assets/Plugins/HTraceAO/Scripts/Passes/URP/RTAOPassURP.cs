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
	internal class RTAOPassURP : ScriptableRenderPass
	{
		private const string RT_IS_NOT_SUPPORTED_MESSAGE = "Realtime RayTracing is not supported!";
		private const string INLINE_RT_IS_NOT_SUPPORTED_MESSAGE = "Inline RayTracing is not supported!";
		private static readonly int CameraNormalsTexture = Shader.PropertyToID("_CameraNormalsTexture");
		
		ProfilingSampler RtaoSampler = new ProfilingSampler(HNames.HTRACE_RTAO_PASS_NAME);

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
			var cmd = CommandBufferPool.Get(HNames.HTRACE_RTAO_PASS_NAME);

			Camera camera = renderingData.cameraData.camera;
			float renderScale = renderingData.cameraData.renderScale;
			int width  = (int)(camera.scaledPixelWidth * renderScale);
			int height = (int)(camera.scaledPixelHeight * renderScale);

			if (Shader.GetGlobalTexture(CameraNormalsTexture) == null)
				return;

			if (HRenderer.SupportsInlineRayTracing == false) // URP has only Inline Raytracing, but we output realtime RT error to avoid confusing users
			{
				HExtensions.DebugPrint(DebugType.Error,RT_IS_NOT_SUPPORTED_MESSAGE);
				cmd.SetGlobalVector(HShaderParams.AmbientOcclusionParam, new Vector4(0.0f, 0.0f, 0.0f, 0.0f));

				context.ExecuteCommandBuffer(cmd);
				cmd.Clear();
				CommandBufferPool.Release(cmd);
				return;
			}

			RTAO.Execute(cmd, camera, width, height);

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

			using (var builder = renderGraph.AddUnsafePass<PassData>(HNames.HTRACE_RTAO_PASS_NAME, out var passData, RtaoSampler))
			{
				UniversalResourceData  resourceData           = frameData.Get<UniversalResourceData>();
				UniversalCameraData    universalCameraData    = frameData.Get<UniversalCameraData>();
				UniversalRenderingData universalRenderingData = frameData.Get<UniversalRenderingData>();
				UniversalLightData     lightData              = frameData.Get<UniversalLightData>();

				builder.AllowGlobalStateModification(true);
				builder.AllowPassCulling(false);

				passData.UniversalCameraData = universalCameraData;

				Camera                  camera      = universalCameraData.camera;
				float                   renderScale = universalCameraData.renderScale;
				RenderTextureDescriptor desc        = universalCameraData.cameraTargetDescriptor;
				
				RTAO.CameraHistorySystem.SyncCamera(universalCameraData.camera.GetHashCode(), Time.frameCount);

				SetupShared(camera, renderScale, desc);

				builder.SetRenderFunc((PassData data, UnsafeGraphContext context) => ExecutePass(data, context));
			}
		}

		private void ExecutePass(PassData data, UnsafeGraphContext rgContext)
		{
			var cmd = CommandBufferHelpers.GetNativeCommandBuffer(rgContext.cmd);

			Camera camera = data.UniversalCameraData.camera;
			float renderScale = data.UniversalCameraData.renderScale;
			int width  = (int)(camera.scaledPixelWidth * renderScale);
			int height = (int)(camera.scaledPixelHeight * renderScale);

			if (HRenderer.SupportsInlineRayTracing == false) // URP has only Inline Raytracing, but we output realtime RT error to avoid confusing users
			{
				HExtensions.DebugPrint(DebugType.Error,RT_IS_NOT_SUPPORTED_MESSAGE);
				cmd.SetGlobalVector(HShaderParams.AmbientOcclusionParam, new Vector4(0.0f, 0.0f, 0.0f, 0.0f));

				return;
			}

			RTAO.Execute(cmd, camera, width, height);
		}
#endif

		#endregion --------------------------- Render Graph ---------------------------

		#region ------------------------------------ Share ------------------------------------


		private static void SetupShared(Camera camera, float renderScale, RenderTextureDescriptor desc)
		{
#if UNITY_2023_2_OR_NEWER
			if (RTAO.HRenderRTAO == null) RTAO.HRenderRTAO             = HExtensions.LoadComputeShader("HRenderRTAO");
#endif

			if (RTAO.HDenoiseRTAO == null) RTAO.HDenoiseRTAO                 = HExtensions.LoadComputeShader("HDenoiserRTAO");
			if (RTAO.HInterpolationRTAO == null) RTAO.HInterpolationRTAO     = HExtensions.LoadComputeShader("HInterpolation");
			if (RTAO.HCheckerboardingRTAO == null) RTAO.HCheckerboardingRTAO = HExtensions.LoadComputeShader("HCheckerboarding");

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

			ref var cameraData = ref RTAO.CameraHistorySystem.GetCameraData();
			if (cameraData.NormalHistory_RTAO == null)
				cameraData.NormalHistory_RTAO = new RTWrapper();
			if (cameraData.OcclusionHistory_RTAO == null)
				cameraData.OcclusionHistory_RTAO = new RTWrapper();

			RTAO.DepthPyramid_RTAO.ReAllocateIfNeeded(RTAO._DepthPyramid, ref desc, graphicsFormat: GraphicsFormat.R16_SFloat, useMipMap: true);
			RTAO.Occlusion_RTAO.ReAllocateIfNeeded(RTAO._Occlusion, ref desc, graphicsFormat: GraphicsFormat.R16_UInt);
			RTAO.VelocityHistory_RTAO.ReAllocateIfNeeded(RTAO._VelocityHistory, ref desc, graphicsFormat: GraphicsFormat.R16_SFloat); //useMipMap: false, autoGenerateMips: false
			RTAO.VelocityReprojected_RTAO.ReAllocateIfNeeded(RTAO._VelocityReprojected, ref desc, graphicsFormat: GraphicsFormat.R16_SFloat);
			RTAO.OcclusionAccumulated_RTAO.ReAllocateIfNeeded(RTAO._OcclusionAccumulated, ref desc, graphicsFormat: GraphicsFormat.R32_UInt);
			RTAO.OcclusionReprojected_RTAO.ReAllocateIfNeeded(RTAO._OcclusionReprojected, ref desc, graphicsFormat: GraphicsFormat.R32_UInt);
			RTAO.OcclusionFiltered_RTAO.ReAllocateIfNeeded(RTAO._OcclusionFiltered, ref desc, graphicsFormat: GraphicsFormat.R32_UInt);
			RTAO.OcclusionInterpolated_RTAO.ReAllocateIfNeeded(RTAO._OcclusionInterpolated, ref desc, graphicsFormat: GraphicsFormat.R8_UNorm);

			cameraData.NormalHistory_RTAO.ReAllocateIfNeeded(RTAO._NormalHistory,  ref desc, graphicsFormat: GraphicsFormat.R8G8B8A8_UNorm);
			cameraData.OcclusionHistory_RTAO.ReAllocateIfNeeded(RTAO._OcclusionHistory, ref desc, graphicsFormat: GraphicsFormat.R32_UInt);

			if (RTAO.RayCounter == null)
			{
				RTAO.RayCounter = new ComputeBuffer(2 * HRenderer.TextureXrSlices, sizeof(uint));
				uint[] zeroArray = new uint[2 * HRenderer.TextureXrSlices];
				RTAO.RayCounter.SetData(zeroArray);
			}

			if (RTAO.IndirectCoords == null)
				RTAO.IndirectCoords = new HDynamicBuffer(BufferType.ComputeBuffer, 2 * sizeof(uint), HRenderer.TextureXrSlices, avoidDownscale: false);
			RTAO.IndirectCoords?.ReAllocIfNeeded(new Vector2Int(width, height));

			if (RTAO.IndirectArguments == null)
				RTAO.IndirectArguments = new GraphicsBuffer(GraphicsBuffer.Target.IndirectArguments, 3 * HRenderer.TextureXrSlices, sizeof(uint));

			if (RTAO.RTAS == null)
#if UNITY_2023_2_OR_NEWER
			{
				RayTracingAccelerationStructure.Settings settings = new RayTracingAccelerationStructure.Settings();
				settings.rayTracingModeMask = RayTracingAccelerationStructure.RayTracingModeMask.Everything;
				settings.managementMode     = RayTracingAccelerationStructure.ManagementMode.Manual;
				settings.layerMask          = 255;
				RTAO.RTAS                        = new RayTracingAccelerationStructure(settings);
			}
#else
				RTAO.RTAS = new RayTracingAccelerationStructure();
#endif

			RTAO.SetupRTAS(camera, height);
		}

		protected internal void Dispose()
		{
			RTAO.CameraHistorySystem.Cleanup();

			RTAO.Occlusion_RTAO?.HRelease();
			RTAO.OcclusionFiltered_RTAO?.HRelease();
			RTAO.OcclusionInterpolated_RTAO?.HRelease();
			RTAO.OcclusionAccumulated_RTAO?.HRelease();
			RTAO.OcclusionReprojected_RTAO?.HRelease();
			RTAO.DepthPyramid_RTAO?.HRelease();
			RTAO.VelocityHistory_RTAO?.HRelease();
			RTAO.VelocityReprojected_RTAO?.HRelease();

			RTAO.IndirectArguments?.HRelease();
			RTAO.IndirectCoords?.HRelease();
			RTAO.RayCounter?.HRelease();

			RTAO.IndirectArguments = null;
			RTAO.IndirectCoords    = null;
			RTAO.RayCounter        = null;

			RTAO.RTAS.HRelease();
			RTAO.RTAS = null;
		}

		#endregion
	}
}
