//pipelinedefine
#define H_URP


using System;
using HTraceAO.Scripts.Editor;
using HTraceAO.Scripts.Globals;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace HTraceAO.Scripts.Infrastructure.URP
{
#if UNITY_2023_1_OR_NEWER
	[VolumeComponentMenu("Lighting/HTrace: Ambient Occlusion"), SupportedOnRenderPipeline(typeof(UniversalRenderPipelineAsset))]
#else
[VolumeComponentMenuForRenderPipeline("Lighting/HTrace: Ambient Occlusion", typeof(UniversalRenderPipeline))]
#endif
#if UNITY_2023_3_OR_NEWER
	[VolumeRequiresRendererFeatures(typeof(HTraceAORendererFeature))]
#endif
	[HelpURL(HNames.HTRACE_AO_DOCUMENTATION_LINK)]
	public sealed class HTraceAOVolume : VolumeComponent, IPostProcessComponent
	{
		public HTraceAOVolume()
		{
#pragma warning disable CS0618
			displayName = HNames.ASSET_NAME_FULL;
#pragma warning restore CS0618
		}

		/// <summary>
		/// Enable Ambient Occlusion.
		/// </summary>
		[Tooltip("Enable HTrace Ambient Occlusion.")]
		public BoolParameter Enable = new BoolParameter(true, BoolParameter.DisplayType.EnumPopup);
		
		// ----------------------------------- General Settings -----------------------------------
		public AmbientOcclusionModeParameter AmbientOcclusionMode = new(value: Globals.AmbientOcclusionMode.GTAO, overrideState: false);
		[InspectorName("Buffer"), Tooltip("Visualizes the debug mode for different buffers.")]
		public HBufferParameter              HBuffer              = new(value: Globals.HBuffer.Multi, overrideState: false);
		
		[InspectorName("Intensity"), Tooltip("Guides the final intensity of the ambient occlusion, higher values result in darker ambient occlusion.")]
		public ClampedFloatParameter Intensity = new ClampedFloatParameter(1f, 0.1f, 4.0f, false);
		
		
		// ------------------------------------ SSAO Settings -----------------------------------
		[InspectorName("Debug Mode"), Tooltip("Visualizes the debug mode for different rendering components of H-Trace.")]
		public DebugModeSSAOParameter        DebugModeSSAO        = new(value: Globals.DebugModeSSAO.None, overrideState: false);
		/// <summary>
		/// 
		/// </summary>
		[InspectorName("Thickness"), Tooltip("Control the thickness of the surfaces on screen. Because the screen-space algorithms can not distinguish thin objects from thick ones, this property helps trace rays behind objects, treating them uniformly.")]
		public ClampedFloatParameter Thickness = new ClampedFloatParameter(0f, 0.0f, 1.0f, false);

		public ClampedIntParameter Radius = new ClampedIntParameter(2, 1, 4);
		
		
		// ----------------------------------- General Data -----------------------------------
		[InspectorName("Direct Lighting Occlusion"), Tooltip("Defines how visible the effect is in areas exposed to direct lighting. It's recommended to use 0 for maximum physical accuracy.")]
		public ClampedFloatParameter DirectLightingOcclusion = new ClampedFloatParameter(0f, 0.0f, 1.0f, false);

		// ------------------------------------ GTAO Settings -----------------------------------
		[InspectorName("Debug Mode"), Tooltip("Visualizes the debug mode for different rendering components of H-Trace.")]
		public DebugModeGTAOParameter DebugModeGTAO          = new(value: Globals.DebugModeGTAO.None, overrideState: false);

		[InspectorName("Full Resolution"), Tooltip("Determines whether the effect is rendered at full resolution or half resolution.")]
		public BoolParameter FullResolution = new BoolParameter(true);

		[InspectorName("Thickness"), Tooltip("Control the thickness of the surfaces on screen. Because the screen-space algorithms can not distinguish thin objects from thick ones, this property helps trace rays behind objects, treating them uniformly.")]
		public ClampedFloatParameter GTAOThickness = new ClampedFloatParameter(0.5f, 0.0f, 1.0f, false);

		[InspectorName("World Space Radius"), Tooltip("Defines the maximum distance (in meters) for occluder search. RTAO produces darker results with the same distance due to being more physically correct and not needing a falloff.")]
		public ClampedFloatParameter GTAOWorldSpaceRadius = new ClampedFloatParameter(1f, 0.25f, 5.0f, false);

		[InspectorName("Slice Count"), Tooltip("Specifies the number of directions (slices) used to evaluate occlusion. The final sample count is calculated as \"Slice Count × Step Count × 2\". Each increment in the Slice Count significantly increases the number of samples. This parameter has the greatest impact on noise reduction (aside from the denoiser itself) but comes with a substantial performance cost.")]
		public ClampedIntParameter GTAOSliceCount = new ClampedIntParameter(2, 1, 4, false);

		[InspectorName("Step Count"), Tooltip("Specifies the number of steps taken along each direction (slice). This parameter determines the accuracy of occlusion and affects noise levels, although to a lesser extent than the Slice Count.")]
		public ClampedIntParameter GTAOStepCount = new ClampedIntParameter(16, 8, 32, false);

		[InspectorName("Visibility Bitmasks"), Tooltip("This approach provides more accurate results, especially in areas with thin geometry such as bars, fences, grills, and thin pillars, at a moderate performance cost.")]
		public BoolParameter GTAOVisibilityBitmasks = new BoolParameter(false, false);

		[InspectorName("Falloff"), Tooltip("Specifies whether falloff should be applied during the occlusion evaluation. Bitmasks are originally designed to work without falloff, which makes them faster.")]
		public BoolParameter GTAOFalloff = new BoolParameter(true, false);

		[InspectorName("Checkerboarding"), Tooltip("Specifies whether the effect should be rendered using a checkerboard pattern, which processes only half of the pixels per frame. This option is designed to minimize visual impact while improving calculation times by up to 50%. It is recommended to enable this feature whenever possible.")]
		public BoolParameter GTAOCheckerboarding = new BoolParameter(false, false);

		[InspectorName("Sample Count"), Tooltip("Specifies the number of temporally accumulated frames. More samples lead to better noise reduction with no additional performance cost, while fewer samples make occlusion more reactive.")]
		public ClampedIntParameter GTAOSampleCountTemporal = new ClampedIntParameter(8, 0, 12, false);

		[InspectorName("Motion Rejection"), Tooltip("Controls the strictness of temporal history rejection. Lower values accept all history, producing smoother output with less noise, but can cause ghosting and trailing near moving objects or during camera translation/rotation. Higher values reject potentially invalid history samples, but may result in noisier output.")]
		public ClampedFloatParameter GTAOMotionRejection = new ClampedFloatParameter(0.75f, 0.0f, 1.0f, false);

		[InspectorName("Normal Rejection"), Tooltip("Specifies whether the difference in surface normals should be considered during temporal history reprojection. This option can mitigate reprojection artifacts, such as the one in the screenshot where color from the frontal plane of the cube \"leaks\" onto its newly revealed side during camera panning.")]
		public ClampedFloatParameter GTAONormalRejectionTemporal = new ClampedFloatParameter(0f, 0.0f, 1.0f, false);

		[InspectorName("Rejection Strength"), Tooltip("Defines the overall strictness of temporal history rejection. Similar to Normal Rejection, setting this parameter to high values close to 1.0 can cause temporal instability on small and thin details, such as foliage. Using very low values close to 0.0 can lead to ghosting in certain scenarios.")]
		public ClampedFloatParameter GTAORejectionStrengthTemporal = new ClampedFloatParameter(0.25f, 0.0f, 1.0f, false);

		[InspectorName("Reprojection Filter"), Tooltip("Defines a reprojection filter used for temporal history fetch. Bilinear is fast but introduces blur, while Lanczos is approximately three times slower but much sharper. This option affects only the sharpness of the reprojection, not its effectiveness.")]
		public ReprojectionFilterParameter GTAOReprojectionFilter = new ReprojectionFilterParameter(value: Globals.ReprojectionFilter.Linear4Taps, overrideState: false);


		[InspectorName("Pixel Radius"), Tooltip("Controls the spatial denoiser radius in pixels. The wider the radius, the better the noise reduction at the cost of additional blur and performance.")]
		public ClampedIntParameter GTAOPixelRadius = new ClampedIntParameter(1, 0, 4, false);

		[InspectorName("Filter Strength"), Tooltip("Controls the strictness of spatial neighbors' rejection. Higher values better preserve details, while lower values reduce noise more efficiently. Check the first two screenshots in the comparison below to see the difference between the 0 and 1 values of this parameter.")]
		public ClampedFloatParameter GTAOFilterStrength = new ClampedFloatParameter(0.75f, 0.0f, 1.0f, false);

		[InspectorName("Normal Rejection"), Tooltip("Specifies whether the difference in surface normals should be considered for spatial neighbors' rejection. Enabling this option further enhances detail preservation at the cost of performance.")]
		public BoolParameter GTAONormalRejectionSpatial = new BoolParameter(true, false);


		[InspectorName("Upscaling Quality"), Tooltip("Defines the filter used for upscaling the half-resolution occlusion buffer.")]
		public UpscalingQualityParameter GTAOUpscalingQuality = new UpscalingQualityParameter(value: Globals.UpscalingQuality.Linear5Taps, overrideState: false);

		[InspectorName("Normal Rejection"), Tooltip("Specifies whether the difference between surface normals is considered during the upscaling calculation.")]
		public BoolParameter GTAOUpscalingNormalRejection = new BoolParameter(true, false);

		// ------------------------------------ RTAO Settings -----------------------------------
		[InspectorName("Debug Mode"), Tooltip("Visualizes the debug mode for different rendering components of H-Trace.")]
		public DebugModeRTAOParameter DebugModeRTAO = new(value: Globals.DebugModeRTAO.None, overrideState: false);

		[InspectorName("World Space Radius"), Tooltip("Defines the maximum distance (in meters) for occluder search. RTAO produces darker results with the same distance due to being more physically correct and not needing a falloff.")]
		public ClampedFloatParameter RTAOWorldSpaceRadius = new ClampedFloatParameter(1f, 0.25f, 5.0f, false);

		[InspectorName("Max Ray Bias"), Tooltip("Controls the maximum ray bias (offset) from a surface. This parameter allows to avoid self-intersection with geometry and is especially useful when Unity's TAA is active.")]
		public ClampedFloatParameter RTAOMaxRayBias = new ClampedFloatParameter(0.002f, 0.001f, 0.02f, false);

		[InspectorName("Layer Mask"), Tooltip("Use this option to exclude objects from the Ray Tracing Acceleration Structure on a per-layer basis.")]
		public LayerEnumParameter RTAOLayerMask = new(value: ~0, overrideState: false);

		[InspectorName("Ray Count"), Tooltip("Specifies the number of rays launched into the scene to evaluate occlusion. This parameter represents the primary tradeoff between noise level and performance.")]
		public ClampedIntParameter RTAORayCount = new ClampedIntParameter(4, 1, 8, false);

		[InspectorName("Full Resolution"), Tooltip("Determines whether the effect is rendered at full resolution or half resolution.")]
		public BoolParameter RTAOFullResolution = new BoolParameter(true, false);

		[InspectorName("Checkerboarding"), Tooltip("Specifies whether the effect should be rendered using a checkerboard pattern, which processes only half of the pixels per frame. This option is designed to minimize visual impact while improving calculation times by up to 50%. It is recommended to enable this feature whenever possible.")]
		public BoolParameter RTAOCheckerboarding = new BoolParameter(false, false);

		[InspectorName("Sample Count Temporal"), Tooltip("Specifies the number of temporally accumulated frames. More samples lead to better noise reduction with no additional performance cost, while fewer samples make occlusion more reactive.")]
		public ClampedIntParameter RTAOSampleCountTemporal = new ClampedIntParameter(12, 8, 16, false);

		[InspectorName("Motion Rejection"), Tooltip("Controls the strictness of temporal history rejection. Lower values accept all history, producing smoother output with less noise, but can cause ghosting and trailing near moving objects or during camera translation/rotation. Higher values reject potentially invalid history samples, but may result in noisier output.")]
		public ClampedFloatParameter RTAOMotionRejection = new ClampedFloatParameter(0.6f, 0.0f, 1.0f, false);

		[InspectorName("Normal Rejection Temporal"), Tooltip("Specifies whether the difference in surface normals should be considered during temporal history reprojection. This option can mitigate reprojection artifacts, such as the one in the screenshot where color from the frontal plane of the cube \"leaks\" onto its newly revealed side during camera panning.")]
		public ClampedFloatParameter RTAONormalRejectionTemporal = new ClampedFloatParameter(0f, 0.0f, 1.0f, false);

		[InspectorName("Rejection Strength Temporal"), Tooltip("Defines the overall strictness of temporal history rejection. Similar to Normal Rejection, setting this parameter to high values close to 1.0 can cause temporal instability on small and thin details, such as foliage. Using very low values close to 0.0 can lead to ghosting in certain scenarios.")]
		public ClampedFloatParameter RTAORejectionStrengthTemporal = new ClampedFloatParameter(0.25f, 0.0f, 1.0f, false);

		[InspectorName("Reprojection Filter"), Tooltip("Defines a reprojection filter used for temporal history fetch. Bilinear is fast but introduces blur, while Lanczos is approximately three times slower but much sharper. This option affects only the sharpness of the reprojection, not its effectiveness.")]
		public ReprojectionFilterParameter RTAOReprojectionFilter = new ReprojectionFilterParameter(value: Globals.ReprojectionFilter.Linear4Taps, overrideState: false);

		[InspectorName("Pixel Radius"), Tooltip("Controls the spatial denoiser radius in pixels. The wider the radius, the better the noise reduction at the cost of additional blur and performance.")]
		public ClampedIntParameter RTAOPixelRadius = new ClampedIntParameter(1, 1, 4, false);

		[InspectorName("Filter Strength"), Tooltip("Controls the strictness of spatial neighbors' rejection. Higher values better preserve details, while lower values reduce noise more efficiently. Check the first two screenshots in the comparison below to see the difference between the 0 and 1 values of this parameter.")]
		public ClampedFloatParameter RTAOFilterStrength = new ClampedFloatParameter(0.75f, 0.0f, 1.0f, false);

		[InspectorName("Normal Rejection"), Tooltip("Specifies whether the difference in surface normals should be considered for spatial neighbors' rejection. Enabling this option further enhances detail preservation at the cost of performance.")]
		public BoolParameter RTAONormalRejectionSpatial = new BoolParameter(true, false);


		[InspectorName("Upscaling Quality"), Tooltip("Defines the filter used for upscaling the half-resolution occlusion buffer.")]
		public UpscalingQualityParameter RTAOUpscalingQuality = new UpscalingQualityParameter(value: Globals.UpscalingQuality.Linear5Taps, overrideState: false);

		[InspectorName("Normal Rejection"), Tooltip("Specifies whether the difference between surface normals is considered during the upscaling calculation.")]
		public BoolParameter RTAOUpscalingNormalRejection = new BoolParameter(true, false);
		
		public bool IsActive()
		{
			return Enable.value;
		}

#if !UNITY_2023_2_OR_NEWER
		public bool IsTileCompatible() => false;
#endif
	}

	[Serializable]
	public sealed class AmbientOcclusionModeParameter : VolumeParameter<AmbientOcclusionMode>
	{
		public AmbientOcclusionModeParameter(AmbientOcclusionMode value, bool overrideState = false) : base(value, overrideState) { }
	}

	[Serializable]
	public sealed class DebugModeSSAOParameter : VolumeParameter<DebugModeSSAO>
	{
		public DebugModeSSAOParameter(DebugModeSSAO value, bool overrideState = false) : base(value, overrideState) { }
	}

	[Serializable]
	public sealed class HBufferParameter : VolumeParameter<HBuffer>
	{
		public HBufferParameter(HBuffer value, bool overrideState = false) : base(value, overrideState) { }
	}

	[Serializable]
	public sealed class DebugModeGTAOParameter : VolumeParameter<DebugModeGTAO>
	{
		public DebugModeGTAOParameter(DebugModeGTAO value, bool overrideState = false) : base(value, overrideState) { }
	}

	[Serializable]
	public sealed class DebugModeRTAOParameter : VolumeParameter<DebugModeRTAO>
	{
		public DebugModeRTAOParameter(DebugModeRTAO value, bool overrideState = false) : base(value, overrideState) { }
	}

	[Serializable]
	public sealed class LayerEnumParameter : VolumeParameter<LayerMask>
	{
		public LayerEnumParameter(LayerMask value, bool overrideState = false) : base(value, overrideState) { }
	}

	[Serializable]
	public sealed class ReprojectionFilterParameter : VolumeParameter<ReprojectionFilter>
	{
		public ReprojectionFilterParameter(ReprojectionFilter value, bool overrideState = false) : base(value, overrideState) { }
	}

	[Serializable]
	public sealed class UpscalingQualityParameter : VolumeParameter<UpscalingQuality>
	{
		public UpscalingQualityParameter(UpscalingQuality value, bool overrideState = false) : base(value, overrideState) { }
	}
}

