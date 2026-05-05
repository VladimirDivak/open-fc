using UnityEngine;

namespace HTraceAO.Scripts.Globals
{
	public static class HShaderParams
	{
		public static readonly string _SCREEN_SPACE_OCCLUSION = "_SCREEN_SPACE_OCCLUSION";
		// Globals, "g_" prefix
		public static readonly int g_HTraceGBuffer0         = Shader.PropertyToID("g_HTraceGBuffer0");
		public static readonly int g_HTraceGBuffer1         = Shader.PropertyToID("g_HTraceGBuffer1");
		public static readonly int g_HTraceGBuffer2         = Shader.PropertyToID("g_HTraceGBuffer2");
		public static readonly int g_HTraceGBuffer3         = Shader.PropertyToID("g_HTraceGBuffer3");
		public static readonly int g_HTraceColor            = Shader.PropertyToID("g_HTraceColor");
		public static readonly int g_HTraceDepth            = Shader.PropertyToID("g_HTraceDepth");
		public static readonly int g_HTraceDepthPyramidWSGI = Shader.PropertyToID("g_HTraceDepthPyramidWSGI");
		public static readonly int g_HTraceStencilBuffer    = Shader.PropertyToID("g_HTraceStencilBuffer");
		public static readonly int g_HTraceMotionVectors    = Shader.PropertyToID("g_HTraceMotionVectors");
		public static readonly int g_HTraceMotionMask       = Shader.PropertyToID("g_HTraceMotionMask");
		public static readonly int g_DepthPyramidTexture    = Shader.PropertyToID("g_DepthPyramidTexture");
		
		//Matrix
		public static readonly int H_MATRIX_VP        = Shader.PropertyToID("_H_MATRIX_VP");
		public static readonly int H_MATRIX_I_VP      = Shader.PropertyToID("_H_MATRIX_I_VP");
		public static readonly int H_MATRIX_PREV_VP   = Shader.PropertyToID("_H_MATRIX_PREV_VP");
		public static readonly int H_MATRIX_PREV_I_VP = Shader.PropertyToID("_H_MATRIX_PREV_I_VP");


		// Additional
		public static int HRenderScale         = Shader.PropertyToID("_HRenderScale");
		public static int HRenderScalePrevious = Shader.PropertyToID("_HRenderScalePrevious");
		public static int FrameCount           = Shader.PropertyToID("_FrameCount");
		public static int ScreenSize           = Shader.PropertyToID("_ScreenSize");
		
		// Globals, "g_" prefix
		public static readonly int g_HTraceBufferAO              = Shader.PropertyToID("_HTraceBufferAO");
		public static readonly int g_ScreenSpaceOcclusionTexture = Shader.PropertyToID("_ScreenSpaceOcclusionTexture");
		public static readonly int g_CameraDepthTexture = Shader.PropertyToID("_CameraDepthTexture");
		public static readonly int g_CameraNormalsTexture = Shader.PropertyToID("_CameraNormalsTexture");

		// Shared Params
		public static readonly int SliceXR					= Shader.PropertyToID("_SliceXR");
		public static readonly int IndexXR					= Shader.PropertyToID("_IndexXR");
		public static readonly int Intensity				= Shader.PropertyToID("_Intensity");
		public static readonly int FilterStrength 			= Shader.PropertyToID("_FilterStrength");
		public static readonly int NormalRejection 			= Shader.PropertyToID("_NormalRejection");
		public static readonly int MotionRejection 			= Shader.PropertyToID("_MotionRejection");
		public static readonly int RejectionStrength		= Shader.PropertyToID("_RejectionStrength");
		public static readonly int TemporalSamplecount 		= Shader.PropertyToID("_TemporalSamplecount");
		public static readonly int Checkerboarding 			= Shader.PropertyToID("_Checkerboarding");
		public static readonly int DepthToViewParams 		= Shader.PropertyToID("_DepthToViewParams");
		
		public static readonly int DepthPyramid_OutputMIP0 	= Shader.PropertyToID("_DepthPyramid_OutputMIP0");
		public static readonly int DepthPyramid_OutputMIP1 	= Shader.PropertyToID("_DepthPyramid_OutputMIP1");
		public static readonly int DepthPyramid_OutputMIP2 	= Shader.PropertyToID("_DepthPyramid_OutputMIP2");
		public static readonly int DepthPyramid_OutputMIP3 	= Shader.PropertyToID("_DepthPyramid_OutputMIP3");
		public static readonly int DepthPyramid_OutputMIP4 	= Shader.PropertyToID("_DepthPyramid_OutputMIP4");
		public static readonly int DepthIntermediate_Output = Shader.PropertyToID("_DepthIntermediate_Output");
		public static readonly int DepthIntermediate        = Shader.PropertyToID("_DepthIntermediate");
		public static readonly int Occlusion				= Shader.PropertyToID("_Occlusion");
		public static readonly int Occlusion_Output         = Shader.PropertyToID("_Occlusion_Output");
		public static readonly int Occlusion_History 		= Shader.PropertyToID("_Occlusion_History");
		public static readonly int OcclusionHistory_Output	= Shader.PropertyToID("_OcclusionHistory_Output");
		public static readonly int OcclusionReprojected		= Shader.PropertyToID("_OcclusionReprojected");
		public static readonly int Normal_History    		= Shader.PropertyToID("_Normal_History");
		public static readonly int NormalHistory_Output		= Shader.PropertyToID("_NormalHistory_Output");

		public static readonly int RayCounter               = Shader.PropertyToID("_RayCounter");
		public static readonly int RayCounter_Output        = Shader.PropertyToID("_RayCounter_Output");
		public static readonly int IndirectCoords_Output    = Shader.PropertyToID("_IndirectCoords_Output");
		public static readonly int IndirectArguments_Output = Shader.PropertyToID("_IndirectArguments_Output");
		public static readonly int TracingCoords            = Shader.PropertyToID("_TracingCoords");
		
		
		// SSAO Params
		public static readonly int RejectFadeoff       		= Shader.PropertyToID("_RejectFadeoff");
		public static readonly int DepthScale          		= Shader.PropertyToID("_DepthScale");
		public static readonly int InvThicknessTable   		= Shader.PropertyToID("_InvThicknessTable");
		public static readonly int SampleWeightTable   		= Shader.PropertyToID("_SampleWeightTable");
		public static readonly int PassNumber          		= Shader.PropertyToID("_PassNumber");
		public static readonly int NoiseFilterStrength 		= Shader.PropertyToID("_NoiseFilterStrength");
		public static readonly int UpsampleTolerance   		= Shader.PropertyToID("_UpsampleTolerance");
		public static readonly int StepSize            		= Shader.PropertyToID("_StepSize");
		public static readonly int BlurTolerance       		= Shader.PropertyToID("_BlurTolerance");

		public static readonly int DepthTiled_OutputMIP0   	= Shader.PropertyToID("_DepthTiled_OutputMIP0");
		public static readonly int DepthTiled_OutputMIP1   	= Shader.PropertyToID("_DepthTiled_OutputMIP1");
		public static readonly int DepthTiled_OutputMIP2   	= Shader.PropertyToID("_DepthTiled_OutputMIP2");
		public static readonly int DepthTiled_OutputMIP3   	= Shader.PropertyToID("_DepthTiled_OutputMIP3");
		public static readonly int DepthTiled              	= Shader.PropertyToID("_DepthTiled");
		public static readonly int DepthPyramid            	= Shader.PropertyToID("_DepthPyramid");
		public static readonly int OcclusionLowRes         	= Shader.PropertyToID("_OcclusionLowRes");
		public static readonly int OcclusionHighRes        	= Shader.PropertyToID("_OcclusionHighRes");
		
		
		// GTAO Params
		public static readonly int HScaleFactorAO 			= Shader.PropertyToID("_HScaleFactorAO");
		public static readonly int Radius               	= Shader.PropertyToID("_Radius");
		public static readonly int ScreenSpaceRadiusScale   = Shader.PropertyToID("_ScreenSpaceRadiusScale");
		public static readonly int Thickness 				= Shader.PropertyToID("_Thickness");
		public static readonly int StepCount 				= Shader.PropertyToID("_StepCount");
		public static readonly int SliceCount 				= Shader.PropertyToID("_SliceCount");
		public static readonly int RayTracedCounter			= Shader.PropertyToID("_RayTracedCounter");

		
		// RTAO Params
		public static readonly int MaxRayDistance			= Shader.PropertyToID("_MaxRayDistance");
		public static readonly int MaxRayBias				= Shader.PropertyToID("_MaxRayBias");
		public static readonly int RaySampleCount			= Shader.PropertyToID("_RaySampleCount");
		public static readonly int VelocityEvaluationXR		= Shader.PropertyToID("_VelocityEvaluationXR");
		
		public static readonly int Velocity_History			= Shader.PropertyToID("_Velocity_History");
		public static readonly int VelocityHistory_Output   = Shader.PropertyToID("_VelocityHistory_Output");
		public static readonly int Velocity_Output			= Shader.PropertyToID("_Velocity_Output");
		public static readonly int VelocityReprojected      = Shader.PropertyToID("_VelocityReprojected");
		
		public static readonly int RTAS                   	= Shader.PropertyToID("_RTAS");
		
		
		// Final Params
		public static readonly int AmbientOcclusionParam = Shader.PropertyToID("_AmbientOcclusionParam");
		public static readonly int DebugSwitch           = Shader.PropertyToID("_DebugSwitch");
		public static readonly int BuffersSwitch         = Shader.PropertyToID("_BuffersSwitch");
		public static readonly int Debug_Output          = Shader.PropertyToID("_Debug_Output");
		
	}		
}
