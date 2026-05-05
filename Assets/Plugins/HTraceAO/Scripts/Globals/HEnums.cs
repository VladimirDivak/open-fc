//pipelinedefine
#define H_URP

using UnityEngine;
using UnityEngine.Rendering;

namespace HTraceAO.Scripts.Globals
{
	public enum UpscalingQuality
	{
		[InspectorName("Linear 5 Taps"), Tooltip("Uses 4 neighbors (in a cross pattern) and the center pixel to reconstruct the full-resolution output.")]
		Linear5Taps = 0,
		[InspectorName("Linear 9 Taps"), Tooltip("Uses 8 neighbors surrounding the center pixel, along with the center pixel itself, to reconstruct the full-resolution output. Marginally better than 5 taps, it provides slightly more accurate reconstruction for very small details.")]
		Linear9Taps = 1,
		[InspectorName("Lanczos 12 Taps"), Tooltip("Employs an FSR 1.0-inspired approach with an adaptive Lanczos filter. Delivers the best and sharpest reconstruction at the cost of performance.")]
		Lanczos12Taps = 2,
	}

	public enum AmbientOcclusionMode
	{
		[InspectorName("SSAO"), Tooltip("Screen Space Ambient Occlusion")]
		SSAO = 0,
		[InspectorName("GTAO"), Tooltip("Ground Truth Ambient Occlusion")]
		GTAO = 1,
		[InspectorName("RTAO"), Tooltip("Ray Traced Ambient Occlusion")]
		RTAO = 2,
	}
	public enum ReprojectionFilter
	{
		Linear4Taps = 0,
		Lanczos12Taps  = 1,
	}
	public enum AlphaCutout
	{
		[InspectorName("Evaluate"), Tooltip("Materials will accurately evaluate alpha cutout on hit.")]
		Evaluate = 0,
		[InspectorName("DepthTest"), Tooltip("Alpha cutout will be evaluated in screen space against the depth buffer.")]
		DepthTest = 1,
	}

	public enum DebugModeSSAO
	{
		[InspectorName("None")]
		None = 0,
		[InspectorName("Main Buffers")]
		MainBuffers,
		[InspectorName("Ambient Occlusion")]
		AmbientOcclusion,
	}

	public enum DebugModeGTAO
	{
		[InspectorName("None")]
		None = 0,
		[InspectorName("Main Buffers")]
		MainBuffers,
		[InspectorName("Ambient Occlusion")]
		AmbientOcclusion,
		[InspectorName("Temporal Disocclusion")]
		TemporalDisocclusion,
	}

	public enum DebugModeRTAO
	{
		None = 0,
		MainBuffers,
		AmbientOcclusion,
		TemporalDisocclusion,
		MotionRejectionMask,
	}
	
	public enum HBuffer
	{
		[InspectorName("Multi")]
		Multi = 0,
		[InspectorName("Depth")]
		Depth = 1,
		//Diffuse = 2,
		[InspectorName("Normal")]
		Normal        = 3,
		[InspectorName("Motion Mask")]
		MotionMask    = 4,
		[InspectorName("Motion Vectors")]
		MotionVectors = 5,
	}

	public enum DebugType
	{
		Log,
		Warning,
		Error,
	}

	public enum HInjectionPoint
	{
	}
}
