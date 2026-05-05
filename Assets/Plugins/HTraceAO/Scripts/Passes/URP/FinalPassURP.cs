//pipelinedefine
#define H_URP

using HTraceAO.Scripts.Data.Private;
using HTraceAO.Scripts.Extensions;
using HTraceAO.Scripts.Globals;
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
	internal class FinalPassURP : ScriptableRenderPass
	{
		const   string   _OutputTarget = "_OutputTarget";
		private static string s_motionVectorsKeyword = "MOTION_VECTORS";
		
		ProfilingSampler FinalPassSampler = new ProfilingSampler(HNames.HTRACE_FINAL_PASS_NAME);

		// Buffers & etc
		internal static ComputeShader HDebug = null;

		// Textures
		internal static RTHandle OutputTarget;

		#region --------------------------- Non Render Graph ---------------------------

#if !UNITY_6000_4_OR_NEWER
		private ScriptableRenderer _renderer;

		protected internal void Initialize(ScriptableRenderer renderer)
		{
			_renderer       = renderer;
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
			Camera camera = renderingData.cameraData.camera;

			var cmd = CommandBufferPool.Get(HNames.HTRACE_FINAL_PASS_NAME);

			int width  = (int)(camera.scaledPixelWidth * renderingData.cameraData.renderScale);
			int height = (int)(camera.scaledPixelHeight * renderingData.cameraData.renderScale);

			 if (DebugModule(cmd, width, height, OutputTarget))
			     return;

			Blitter.BlitCameraTexture(cmd, OutputTarget, _renderer.cameraColorTargetHandle);


			context.ExecuteCommandBuffer(cmd);
			cmd.Clear();
			CommandBufferPool.Release(cmd);
		}
#endif

		#endregion --------------------------- Non Render Graph ---------------------------

		#region --------------------------- Render Graph ---------------------------
#if UNITY_2023_3_OR_NEWER
		private class PassData
		{
			public TextureHandle       ColorTexture;
			public UniversalCameraData UniversalCameraData;
			public TextureHandle       OutputTarget;
		}

	    public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
	    {

			using (var builder = renderGraph.AddUnsafePass<PassData>(HNames.HTRACE_FINAL_PASS_NAME, out var passData, FinalPassSampler))
			{
				UniversalResourceData  resourceData           = frameData.Get<UniversalResourceData>();
				UniversalCameraData    universalCameraData    = frameData.Get<UniversalCameraData>();
				UniversalRenderingData universalRenderingData = frameData.Get<UniversalRenderingData>();
				UniversalLightData     lightData              = frameData.Get<UniversalLightData>();

				builder.AllowGlobalStateModification(true);
				builder.AllowPassCulling(false);

				TextureHandle colorTexture = universalRenderingData.renderingMode == RenderingMode.Deferred ? resourceData.activeColorTexture : resourceData.cameraColor;

				passData.ColorTexture        = colorTexture;
				passData.UniversalCameraData = universalCameraData;

				SetupShared(universalCameraData.camera, universalCameraData.renderScale, universalCameraData.cameraTargetDescriptor);
				ExtensionsURP.UseTexture(builder, renderGraph, OutputTarget, ref passData.OutputTarget, AccessFlags.ReadWrite);

				builder.SetRenderFunc((PassData data, UnsafeGraphContext context) => ExecutePass(data, context));
			}
	    }

	    private static void ExecutePass(PassData data, UnsafeGraphContext rgContext)
	    {
		    var cmd = CommandBufferHelpers.GetNativeCommandBuffer(rgContext.cmd);

		    int width  = (int)(data.UniversalCameraData.camera.scaledPixelWidth * data.UniversalCameraData.renderScale);
		    int height = (int)(data.UniversalCameraData.camera.scaledPixelHeight * data.UniversalCameraData.renderScale);

			if (DebugModule(cmd, width, height, data.OutputTarget))
				return;

			Blitter.BlitCameraTexture(cmd, OutputTarget, data.ColorTexture);
	    }

#endif

		#endregion --------------------------- Render Graph ---------------------------

		#region --------------------------- Shared ---------------------------

		private static void SetupShared(Camera camera, float renderScale, RenderTextureDescriptor desc)
		{
			if (HDebug == null) HDebug = HExtensions.LoadComputeShader("HDebug");

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

			ExtensionsURP.ReAllocateIfNeeded(_OutputTarget, ref OutputTarget, ref desc);
		}

		private static bool DebugModule(CommandBuffer cmd, int width, int height, RTHandle outputTarget)
	    {
		    if (HSettings.GeneralSettings.AmbientOcclusionMode == AmbientOcclusionMode.SSAO && HSettings.SSAOSettings.DebugModeSSAO == DebugModeSSAO.None ||
		        HSettings.GeneralSettings.AmbientOcclusionMode == AmbientOcclusionMode.GTAO && HSettings.GTAOSettings.DebugMode == DebugModeGTAO.None ||
		        HSettings.GeneralSettings.AmbientOcclusionMode == AmbientOcclusionMode.RTAO && HSettings.RTAOSettings.DebugMode == DebugModeRTAO.None)
		    {
// #if UNITY_EDITOR
// #endif
			    return true;
		    }

		    using (new HTraceProfilingScope(cmd, new ProfilingSamplerHTrace("Debug")))
		    {
			    cmd.SetComputeIntParams(HDebug, HShaderParams.DebugSwitch, 0);
			    cmd.SetComputeIntParams(HDebug, HShaderParams.BuffersSwitch, (int)HSettings.GeneralSettings.HBuffer);
			    HDebug.DisableKeyword(s_motionVectorsKeyword);

			    if (HSettings.GeneralSettings.AmbientOcclusionMode == AmbientOcclusionMode.SSAO)
			    {
				    cmd.SetGlobalTexture(HShaderParams.g_HTraceMotionVectors, HRenderer.EmptyTexture);
				    cmd.SetGlobalTexture(HShaderParams.g_HTraceMotionMask, HRenderer.EmptyTexture);
				    cmd.SetComputeIntParams(HDebug, HShaderParams.DebugSwitch, (int)HSettings.SSAOSettings.DebugModeSSAO);
			    }

			    if (HSettings.GeneralSettings.AmbientOcclusionMode == AmbientOcclusionMode.GTAO)
			    {
				    cmd.SetComputeIntParams(HDebug, HShaderParams.DebugSwitch, (int)HSettings.GTAOSettings.DebugMode);
				    if (HSettings.GTAOSettings.DebugMode == DebugModeGTAO.TemporalDisocclusion)
				    {
					    cmd.SetComputeIntParams(HDebug, HShaderParams.TemporalSamplecount, HSettings.GTAOSettings.SampleCountTemporal);
				    }
				    if (HSettings.GTAOSettings.SampleCountTemporal > 1)
					    HDebug.EnableKeyword(s_motionVectorsKeyword);
			    }

			    if (HSettings.GeneralSettings.AmbientOcclusionMode == AmbientOcclusionMode.RTAO)
			    {
				    cmd.SetComputeIntParams(HDebug, HShaderParams.DebugSwitch, (int)HSettings.RTAOSettings.DebugMode);
				    if (HSettings.RTAOSettings.DebugMode == DebugModeRTAO.TemporalDisocclusion)
				    {
					    cmd.SetComputeIntParams(HDebug, HShaderParams.TemporalSamplecount, HSettings.RTAOSettings.SampleCountTemporal);
				    }
				    if (HSettings.RTAOSettings.SampleCountTemporal > 1)
					    HDebug.EnableKeyword(s_motionVectorsKeyword);
			    }

			    int debug_kernel = HDebug.FindKernel("Debug");
			    cmd.SetComputeTextureParam(HDebug, debug_kernel, HShaderParams.Debug_Output, outputTarget);
			    cmd.DispatchCompute(HDebug, debug_kernel, Mathf.CeilToInt(width / 8.0f), Mathf.CeilToInt(height / 8.0f), 1);
		    }

		    return false;
	    }

		protected internal void Dispose()
		{

		}

		#endregion --------------------------- Shared ---------------------------
	}
}
