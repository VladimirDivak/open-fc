//pipelinedefine
#define H_URP

using System;
using HTraceAO.Scripts.Data.Private;
using HTraceAO.Scripts.Data.Public;
using HTraceAO.Scripts.Globals;
using HTraceAO.Scripts.Passes.URP;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace HTraceAO.Scripts.Infrastructure.URP
{
    [DisallowMultipleRendererFeature]
	[ExecuteAlways]
	[HelpURL(HNames.HTRACE_AO_DOCUMENTATION_LINK)]
	public class HTraceAORendererFeature :  
#if UNITY_2022
		ScriptableRendererFeature
#else
		ScreenSpaceAmbientOcclusion
#endif
	{
		// #region UI Part
		//
		// [FormerlySerializedAs("AmbientOcclusionMode")]
		// [Header("Global Settings")]
		// [Tooltip("Ambient Occlusion Mode")]
		// [SerializeField] private AmbientOcclusionMode ambientOcclusionMode = AmbientOcclusionMode.GTAO;
		//
		// public AmbientOcclusionMode AmbientOcclusionMode
		// {
		// 	get => ambientOcclusionMode;
		// 	set => ambientOcclusionMode = value;
		// }
		//
		// #endregion
		
		private PrePassURP       _prePass;
		private MotionVectorsURP _motionVectors;
		private SSAOPassURP      _ssaoPass;
		private GTAOPassURP      _gtaoPass;
		private RTAOPassURP      _rtaoPass;
		private FinalPassURP     _finalPass;
		
		private bool _initialized = false;

		private AmbientOcclusionMode _previousAmbientOcclusionMode;

		/// <summary>
		/// Initializes this feature's resources. This is called every time serialization happens.
		/// </summary>
		public override void Create()
		{
			name = HNames.HTRACE_RENDERER_FEATURE_NAME;
			Dispose();

			if (VolumeManager.instance == null)
				return;

			var stack = VolumeManager.instance.stack;
			if (stack == null)
				return;
			
			HTraceAOVolume hTraceAOVolume = stack.GetComponent<HTraceAOVolume>();
			if (hTraceAOVolume == null)
				return;

			SettingsBuild(hTraceAOVolume);
			_previousAmbientOcclusionMode = hTraceAOVolume.AmbientOcclusionMode.value;
			
			_prePass                       = new PrePassURP();
			_prePass.renderPassEvent       = RenderPassEvent.BeforeRenderingDeferredLights;
			switch (hTraceAOVolume.AmbientOcclusionMode.value)
			{
				case AmbientOcclusionMode.SSAO:
					_ssaoPass                      = new SSAOPassURP();
					_ssaoPass.renderPassEvent      = RenderPassEvent.BeforeRenderingDeferredLights;
					break;
				case AmbientOcclusionMode.GTAO:
					_motionVectors                 = new MotionVectorsURP();
					_motionVectors.renderPassEvent = RenderPassEvent.BeforeRenderingDeferredLights;
					_gtaoPass                      = new GTAOPassURP();
					_gtaoPass.renderPassEvent      = RenderPassEvent.BeforeRenderingDeferredLights;
					break;
				case AmbientOcclusionMode.RTAO:
					_motionVectors                 = new MotionVectorsURP();
					_motionVectors.renderPassEvent = RenderPassEvent.BeforeRenderingDeferredLights;
					_rtaoPass                      = new RTAOPassURP();
					_rtaoPass.renderPassEvent      = RenderPassEvent.BeforeRenderingDeferredLights;
					break;
			}
			_finalPass                     = new FinalPassURP();
			_finalPass.renderPassEvent     = RenderPassEvent.AfterRenderingPostProcessing;

			_initialized = true;
		}
		
#if !UNITY_6000_4_OR_NEWER
		/// <summary>
		/// Called when render targets are allocated and ready to be used.
		/// </summary>
		/// <param name="renderer"></param>
		/// <param name="renderingData"></param>
		/// <!--https://docs.unity3d.com/Packages/com.unity.render-pipelines.universal@13.1/manual/upgrade-guide-2022-1.html-->
#if UNITY_6000_3_OR_NEWER
		[System.Obsolete]
#endif
		public override void SetupRenderPasses(ScriptableRenderer renderer, in RenderingData renderingData)
		{
			if (_initialized == false)
				return;
			
			if (HRenderer.RenderGraphEnabled == false)
			{
				if (VolumeManager.instance == null)
					return;

				var stack = VolumeManager.instance.stack;
				if (stack == null)
					return;
				
				HTraceAOVolume hTraceAOVolume = stack.GetComponent<HTraceAOVolume>();
				if (hTraceAOVolume == null)
					return;

				if (_previousAmbientOcclusionMode != hTraceAOVolume.AmbientOcclusionMode.value || _initialized == false)
				{
					Create();
				}

				SettingsBuild(hTraceAOVolume);
				_prePass.Initialize(renderer);
				switch (hTraceAOVolume.AmbientOcclusionMode.value)
				{
					case AmbientOcclusionMode.SSAO:
						_ssaoPass.Initialize(renderer);
						break;
					case AmbientOcclusionMode.GTAO:
						_motionVectors.Initialize(renderer);
						_gtaoPass.Initialize(renderer);
						break;
					case AmbientOcclusionMode.RTAO:
						_motionVectors.Initialize(renderer);
						_rtaoPass.Initialize(renderer);
						break;
				}
				_finalPass.Initialize(renderer);	
			}
		}
#endif

		/// <summary>
		/// Injects one or multiple ScriptableRenderPass in the renderer.
		/// </summary>
		/// <param name="renderer"></param>
		/// <param name="renderingData"></param>
		public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
		{
			Shader.DisableKeyword(HShaderParams._SCREEN_SPACE_OCCLUSION);
			
			if (renderingData.cameraData.cameraType == CameraType.Reflection || renderingData.cameraData.cameraType == CameraType.Preview)
				return;
			
			if (VolumeManager.instance == null)
				return;

			var stack = VolumeManager.instance.stack;
			if (stack == null)
				return;
			
			HTraceAOVolume hTraceAOVolume = stack.GetComponent<HTraceAOVolume>();
			bool           isActive       = hTraceAOVolume != null && hTraceAOVolume.IsActive();
			
			if (!isActive)
				return;
			
			Shader.EnableKeyword(HShaderParams._SCREEN_SPACE_OCCLUSION); //HTraceAO specific

			SettingsBuild(hTraceAOVolume);
			
#if !UNITY_2022_1_OR_NEWER
			_prePass.Initialize(renderer);
			switch (hTraceAOVolume.AmbientOcclusionMode.value)
			{
				case AmbientOcclusionMode.SSAO:
					_ssaoPass.Initialize(renderer);
					break;
				case AmbientOcclusionMode.GTAO:
			_motionVectors.Initialize(renderer);
					_gtaoPass.Initialize(renderer);
					break;
				case AmbientOcclusionMode.RTAO:
			_motionVectors.Initialize(renderer);
					_rtaoPass.Initialize(renderer);
					break;
			}
			_finalPass.Initialize(renderer);
#endif
			if (_previousAmbientOcclusionMode != hTraceAOVolume.AmbientOcclusionMode.value || _initialized == false)
			{
				Create();
			}

			renderer.EnqueuePass(_prePass);
			switch (hTraceAOVolume.AmbientOcclusionMode.value)
			{
				case AmbientOcclusionMode.SSAO:
					renderer.EnqueuePass(_ssaoPass);
					break;
				case AmbientOcclusionMode.GTAO:
					if (HSettings.GTAOSettings.SampleCountTemporal > 1)
						renderer.EnqueuePass(_motionVectors);
					renderer.EnqueuePass(_gtaoPass);
					break;
				case AmbientOcclusionMode.RTAO:
					if (HSettings.RTAOSettings.SampleCountTemporal > 1)
						renderer.EnqueuePass(_motionVectors);
					renderer.EnqueuePass(_rtaoPass);
					break;
			}
			renderer.EnqueuePass(_finalPass);

			_previousAmbientOcclusionMode = hTraceAOVolume.AmbientOcclusionMode.value;
		}

		private void SettingsBuild(HTraceAOVolume hTraceAOVolume)
		{
			if (HSettings.GeneralSettings == null) HSettings.GeneralSettings = new GeneralSettings();
			if (HSettings.SSAOSettings == null) HSettings.SSAOSettings       = new SSAOSettings();
			if (HSettings.GTAOSettings == null) HSettings.GTAOSettings       = new GTAOSettings();
			if (HSettings.RTAOSettings == null) HSettings.RTAOSettings       = new RTAOSettings();
			if (HSettings.DebugSettings == null) HSettings.DebugSettings     = new DebugSettings();

			if (hTraceAOVolume == null)
				return;

			// HRendererURP.UrpAsset.volumeProfile.TryGet(out HTraceAOVolume hTraceAOVolumeProfileDefault);
			// if (hTraceAOVolumeProfileDefault == null)
			// 	return;

			// General Settings
			HSettings.GeneralSettings.AmbientOcclusionMode = hTraceAOVolume.AmbientOcclusionMode.value;
			HSettings.GeneralSettings.HBuffer = hTraceAOVolume.HBuffer.value;
			HSettings.GeneralSettings.Intensity = hTraceAOVolume.Intensity.value;
			HSettings.GeneralSettings.DirectLightOcclusion = hTraceAOVolume.DirectLightingOcclusion.value;

			// SSAO Settings
			HSettings.SSAOSettings.DebugModeSSAO = hTraceAOVolume.DebugModeSSAO.value;
			HSettings.SSAOSettings.Thickness = hTraceAOVolume.Thickness.value;
			HSettings.SSAOSettings.Radius = hTraceAOVolume.Radius.value;

			// GTAO Settings
			HSettings.GTAOSettings.DebugMode = hTraceAOVolume.DebugModeGTAO.value;
			HSettings.GTAOSettings.FullResolution = hTraceAOVolume.FullResolution.value;
			HSettings.GTAOSettings.Thickness = hTraceAOVolume.GTAOThickness.value;
			HSettings.GTAOSettings.WorldSpaceRadius = hTraceAOVolume.GTAOWorldSpaceRadius.value;
			HSettings.GTAOSettings.SliceCount = hTraceAOVolume.GTAOSliceCount.value;
			HSettings.GTAOSettings.StepCount = hTraceAOVolume.GTAOStepCount.value;
			HSettings.GTAOSettings.VisibilityBitmasks = hTraceAOVolume.GTAOVisibilityBitmasks.value;
			HSettings.GTAOSettings.Falloff = hTraceAOVolume.GTAOFalloff.value;
			HSettings.GTAOSettings.Checkerboarding = hTraceAOVolume.GTAOCheckerboarding.value;
			HSettings.GTAOSettings.SampleCountTemporal = hTraceAOVolume.GTAOSampleCountTemporal.value;
			HSettings.GTAOSettings.MotionRejection = hTraceAOVolume.GTAOMotionRejection.value;
			HSettings.GTAOSettings.NormalRejectionTemporal = hTraceAOVolume.GTAONormalRejectionTemporal.value;
			HSettings.GTAOSettings.RejectionStrengthTemporal = hTraceAOVolume.GTAORejectionStrengthTemporal.value;
			HSettings.GTAOSettings.ReprojectionFilter = hTraceAOVolume.GTAOReprojectionFilter.value;
			HSettings.GTAOSettings.PixelRadius = hTraceAOVolume.GTAOPixelRadius.value;
			HSettings.GTAOSettings.FilterStrength = hTraceAOVolume.GTAOFilterStrength.value;
			HSettings.GTAOSettings.NormalRejectionSpatial = hTraceAOVolume.GTAONormalRejectionSpatial.value;
			HSettings.GTAOSettings.UpscalingQuality = hTraceAOVolume.GTAOUpscalingQuality.value;
			HSettings.GTAOSettings.UpscalingNormalRejection = hTraceAOVolume.GTAOUpscalingNormalRejection.value;

			// RTAO Settings
			HSettings.RTAOSettings.DebugMode = hTraceAOVolume.DebugModeRTAO.value;
			HSettings.RTAOSettings.WorldSpaceRadius = hTraceAOVolume.RTAOWorldSpaceRadius.value;
			HSettings.RTAOSettings.MaxRayBias = hTraceAOVolume.RTAOMaxRayBias.value;
			HSettings.RTAOSettings.LayerMask = hTraceAOVolume.RTAOLayerMask.value;
			HSettings.RTAOSettings.RayCount = hTraceAOVolume.RTAORayCount.value;
			HSettings.RTAOSettings.FullResolution = hTraceAOVolume.RTAOFullResolution.value;
			HSettings.RTAOSettings.Checkerboarding = hTraceAOVolume.RTAOCheckerboarding.value;
			HSettings.RTAOSettings.SampleCountTemporal = hTraceAOVolume.RTAOSampleCountTemporal.value;
			HSettings.RTAOSettings.MotionRejection = hTraceAOVolume.RTAOMotionRejection.value;
			HSettings.RTAOSettings.NormalRejectionTemporal = hTraceAOVolume.RTAONormalRejectionTemporal.value;
			HSettings.RTAOSettings.RejectionStrengthTemporal = hTraceAOVolume.RTAORejectionStrengthTemporal.value;
			HSettings.RTAOSettings.ReprojectionFilter = hTraceAOVolume.RTAOReprojectionFilter.value;
			HSettings.RTAOSettings.PixelRadius = hTraceAOVolume.RTAOPixelRadius.value;
			HSettings.RTAOSettings.FilterStrength = hTraceAOVolume.RTAOFilterStrength.value;
			HSettings.RTAOSettings.NormalRejectionSpatial = hTraceAOVolume.RTAONormalRejectionSpatial.value;
			HSettings.RTAOSettings.UpscalingQuality = hTraceAOVolume.RTAOUpscalingQuality.value;
			HSettings.RTAOSettings.UpscalingNormalRejection = hTraceAOVolume.RTAOUpscalingNormalRejection.value;
		}

		protected override void Dispose(bool disposing)
		{
			_prePass?.Dispose();
			_motionVectors?.Dispose();
			_ssaoPass?.Dispose();
			_gtaoPass?.Dispose();
			_rtaoPass?.Dispose();
			_finalPass?.Dispose();
			
			_prePass       = null;
			_motionVectors = null;
			_ssaoPass      = null;
			_gtaoPass      = null;
			_rtaoPass      = null;
			_finalPass     = null;

			_initialized = false;
			
			Shader.DisableKeyword(HShaderParams._SCREEN_SPACE_OCCLUSION);
		}
	}
}
