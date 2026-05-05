//pipelinedefine
#define H_URP

using System;
using HTraceAO.Scripts.Data.Private;
using HTraceAO.Scripts.Extensions;
using HTraceAO.Scripts.Extensions.CameraHistorySystem;
using HTraceAO.Scripts.Globals;
using HTraceAO.Scripts.Wrappers;
using UnityEngine;
using UnityEngine.Rendering;

namespace HTraceAO.Scripts.Passes.Shared.AO
{
	internal static class GTAO
	{
		private enum HDepthPyramidKernel
		{
			GenerateDepthPyramid_1 = 0,
		}

		private enum HDenoiseGTAOKernel
		{
			TemporalReprojection = 0,
			TemporalAccumulation = 1,
			SpatialFiltering = 2,
		}

		private enum HCheckerboardingKernel
		{
			CheckerboardClassification = 0,
			IndirectArguments = 1,
		}

		private enum HRenderGTAOKernel
		{
			RenderGTAO = 0,
		}

		private enum HInterpolationKernel
		{
			Interpolation = 0,
		}

		// Shader properties
		internal static readonly int _DepthPyramid = Shader.PropertyToID("_DepthPyramid");

		internal static readonly int _DepthIntermediate        = Shader.PropertyToID("_DepthIntermediate");
		internal static readonly int _DepthIntermediate_Output = Shader.PropertyToID("_DepthIntermediate_Output");
		internal static readonly int _DepthPyramid_OutputMIP0  = Shader.PropertyToID("_DepthPyramid_OutputMIP0");
		internal static readonly int _DepthPyramid_OutputMIP1  = Shader.PropertyToID("_DepthPyramid_OutputMIP1");
		internal static readonly int _DepthPyramid_OutputMIP2  = Shader.PropertyToID("_DepthPyramid_OutputMIP2");
		internal static readonly int _DepthPyramid_OutputMIP3  = Shader.PropertyToID("_DepthPyramid_OutputMIP3");
		internal static readonly int _DepthPyramid_OutputMIP4  = Shader.PropertyToID("_DepthPyramid_OutputMIP4");
		internal static readonly int _DepthPyramid_OutputMIP5  = Shader.PropertyToID("_DepthPyramid_OutputMIP5");
		internal static readonly int _DepthPyramid_OutputMIP6  = Shader.PropertyToID("_DepthPyramid_OutputMIP6");
		internal static readonly int _DepthPyramid_OutputMIP7  = Shader.PropertyToID("_DepthPyramid_OutputMIP7");
		internal static readonly int _DepthPyramid_OutputMIP8  = Shader.PropertyToID("_DepthPyramid_OutputMIP8");

		// Local variables
		internal const string _NormalHistory         = "_NormalHistory";
		internal const string _Occlusion             = "_Occlusion";
		internal const string _OcclusionAccumulated  = "_OcclusionAccumulated";
		internal const string _OcclusionReprojected  = "_OcclusionReprojected";
		internal const string _OcclusionHistory      = "_OcclusionHistory";
		internal const string _OcclusionFiltered     = "_OcclusionFiltered";
		internal const string _OcclusionInterpolated = "_OcclusionInterpolated";
		internal const string _DepthPyramid2          = "_DepthPyramid";

		// Keywords
		private const string NORMAL_REJECTION_TEMPORAL = "NORMAL_REJECTION_TEMPORAL";
		private const string LANCZOS_REPROJECTION = "LANCZOS_REPROJECTION";
		private const string CHECKERBOARDING = "CHECKERBOARDING";
		private const string FALLOFF = "FALLOFF";
		private const string VR_COMPATIBILITY = "VR_COMPATIBILITY";
		private const string INTERPOLATION_LINEAR_5 = "INTERPOLATION_LINEAR_5";
		private const string INTERPOLATION_LINEAR_9 = "INTERPOLATION_LINEAR_9";
		private const string INTERPOLATION_LANCZOS_12 = "INTERPOLATION_LANCZOS_12";
		private const string NORMAL_REJECTION_SPATIAL = "NORMAL_REJECTION_SPATIAL";
		private const string NORMAL_REJECTION = "NORMAL_REJECTION";
		private const string FINAL_OUTPUT_ONLY = "FINAL_OUTPUT_ONLY";
		private const string TEMPORAL_ACCUMULATION = "TEMPORAL_ACCUMULATION";
		private const string VISIBILITY_BITMASKS = "VISIBILITY_BITMASKS";
		private const string RADIUS_1 = "RADIUS_1";
		private const string RADIUS_2 = "RADIUS_2";
		private const string RADIUS_3 = "RADIUS_3";
		private const string RADIUS_4 = "RADIUS_4";

		// Buffers & etc
		internal static ComputeShader HRenderGTAO          = null;
		internal static ComputeShader HDenoiseGTAO         = null;
		internal static ComputeShader HInterpolationGTAO   = null;
		internal static ComputeShader HCheckerboardingGTAO = null;
		internal static ComputeShader HDepthPyramid        = null;

		// Profiler Samplers
		internal static ProfilingSamplerHTrace DepthPyramidGenerationSampler            = new ProfilingSamplerHTrace(HNames.DEPTH_PYRAMID_GENERATION_SAMPLER, parentName: HNames.HTRACE_GTAO_PASS_NAME, order: 0);
		internal static ProfilingSamplerHTrace CheckerboardingSampler                   = new ProfilingSamplerHTrace(HNames.CHECKERBOARDING_SAMPLER,          parentName: HNames.HTRACE_GTAO_PASS_NAME, order: 1);
		internal static ProfilingSamplerHTrace RenderOcclusionSampler                   = new ProfilingSamplerHTrace(HNames.RENDER_OCCLUSION_SAMPLER,         parentName: HNames.HTRACE_GTAO_PASS_NAME, order: 2);
		internal static ProfilingSamplerHTrace TemporalAccumulationSampler              = new ProfilingSamplerHTrace(HNames.TEMPORAL_ACCUMULATION_SAMPLER,    parentName: HNames.HTRACE_GTAO_PASS_NAME, order: 3);
		internal static ProfilingSamplerHTrace SpatialFilterSampler                     = new ProfilingSamplerHTrace(HNames.SPATIAL_FILTER_SAMPLER,           parentName: HNames.HTRACE_GTAO_PASS_NAME, order: 4);
		internal static ProfilingSamplerHTrace InterpolationSampler                     = new ProfilingSamplerHTrace(HNames.INTERPOLATION_SAMPLER,            parentName: HNames.HTRACE_GTAO_PASS_NAME, order: 5);


		internal struct HistoryCameraDataGTAO : ICameraHistoryData, IDisposable
		{
			private int hash;
			public RTWrapper NormalHistory_GTAO;
			public RTWrapper OcclusionHistory_GTAO;

			public HistoryCameraDataGTAO(int hash = 0)
			{
				this.hash = hash;
				NormalHistory_GTAO = new RTWrapper();
				OcclusionHistory_GTAO = new RTWrapper();
			}

			public int GetHash() => hash;
			public void SetHash(int hashIn) => this.hash = hashIn;

			public void Dispose()
			{
				NormalHistory_GTAO?.HRelease();
				OcclusionHistory_GTAO?.HRelease();
			}
		}

		internal static readonly CameraHistorySystem<HistoryCameraDataGTAO> CameraHistorySystem = new CameraHistorySystem<HistoryCameraDataGTAO>();


		// GTAO Buffers
		internal static RTWrapper Occlusion_GTAO  = new RTWrapper();
		internal static RTWrapper OcclusionFiltered_GTAO = new RTWrapper();
		internal static RTWrapper OcclusionInterpolated_GTAO  = new RTWrapper();
		internal static RTWrapper OcclusionAccumulated_GTAO  = new RTWrapper();
		internal static RTWrapper OcclusionReprojected_GTAO  = new RTWrapper();
		internal static RTWrapper NormalHistory_GTAO_BIRP  = new RTWrapper(); //for BIRP
		internal static RTWrapper OcclusionHistory_GTAO_BIRP  = new RTWrapper();  //for BIRP

		internal static RTWrapper DepthPyramidRT            = new();
		internal static RTWrapper DepthIntermediate_Pyramid = new();

		internal static ComputeBuffer IndirectArguments;
		internal static HDynamicBuffer IndirectCoords;
		internal static ComputeBuffer RayCounter;

		public static void Execute(CommandBuffer cmd, Camera camera, int cameraWidth, int cameraHeight)
		{
			using (new HTraceProfilingScope(cmd, DepthPyramidGenerationSampler))
			{
				// Vector2Int depthPyramidResolution = HMath.DepthResolutionFunc(RuntimeData.CameraResolution);
				// int        depthPyramidResX       = depthPyramidResolution.x / 16;
				// int        depthPyramidResY       = depthPyramidResolution.y / 16;

				// Generate 0-4 mip levels
				cmd.SetComputeTextureParam(HDepthPyramid, (int)HDepthPyramidKernel.GenerateDepthPyramid_1, _DepthPyramid_OutputMIP0, DepthPyramidRT.rt, 0);
				cmd.SetComputeTextureParam(HDepthPyramid, (int)HDepthPyramidKernel.GenerateDepthPyramid_1, _DepthPyramid_OutputMIP1, DepthPyramidRT.rt, 1);
				cmd.SetComputeTextureParam(HDepthPyramid, (int)HDepthPyramidKernel.GenerateDepthPyramid_1, _DepthPyramid_OutputMIP2, DepthPyramidRT.rt, 2);
				cmd.SetComputeTextureParam(HDepthPyramid, (int)HDepthPyramidKernel.GenerateDepthPyramid_1, _DepthPyramid_OutputMIP3, DepthPyramidRT.rt, 3);
				cmd.SetComputeTextureParam(HDepthPyramid, (int)HDepthPyramidKernel.GenerateDepthPyramid_1, _DepthPyramid_OutputMIP4, DepthPyramidRT.rt, 4);
				//cmd.SetComputeTextureParam(HDepthPyramid, (int)HDepthPyramidKernel.GenerateDepthPyramid_1, _DepthIntermediate_Output, DepthIntermediate_Pyramid.rt);
				cmd.DispatchCompute(HDepthPyramid, (int)HDepthPyramidKernel.GenerateDepthPyramid_1, Mathf.CeilToInt(cameraWidth / 16.0f), Mathf.CeilToInt(cameraHeight / 16.0f), TextureXR.slices);

				// if (false) // Not required for AO
				// {
				// 	// Generate 5-7 mip levels
				// 	int generateDepthPyramid_2_Kernel = HDepthPyramid.FindKernel("GenerateDepthPyramid_2");
				// 	cmd.SetComputeTextureParam(HDepthPyramid, generateDepthPyramid_2_Kernel, _DepthIntermediate, DepthIntermediate_Pyramid.rt);
				// 	cmd.SetComputeTextureParam(HDepthPyramid, generateDepthPyramid_2_Kernel, _DepthPyramid_OutputMIP5, DepthPyramidRT.rt, 5);
				// 	cmd.SetComputeTextureParam(HDepthPyramid, generateDepthPyramid_2_Kernel, _DepthPyramid_OutputMIP6, DepthPyramidRT.rt, 6);
				// 	cmd.SetComputeTextureParam(HDepthPyramid, generateDepthPyramid_2_Kernel, _DepthPyramid_OutputMIP7, DepthPyramidRT.rt, 7);
				// 	cmd.SetComputeTextureParam(HDepthPyramid, generateDepthPyramid_2_Kernel, _DepthPyramid_OutputMIP8, DepthPyramidRT.rt, 8);
				// 	cmd.DispatchCompute(HDepthPyramid, generateDepthPyramid_2_Kernel, depthPyramidResX / 8, depthPyramidResY / 8, TextureXR.slices);
				// }

				cmd.SetGlobalTexture(HShaderParams.g_DepthPyramidTexture, DepthPyramidRT.rt);
			}

			bool TemporalAccumulationEnabled = HSettings.GTAOSettings.SampleCountTemporal > 1;
			bool SpatialFilterEnabled = HSettings.GTAOSettings.PixelRadius > 0;
			bool Checkerboarding = HSettings.GTAOSettings.Checkerboarding;

			if (IndirectCoords.ComputeBuffer == null || camera.cameraType == CameraType.SceneView)
				Checkerboarding = false;

			float ScaleFactor = HSettings.GTAOSettings.FullResolution ? 1.0f : 2.0f;
			float Thickness = HSettings.GTAOSettings.VisibilityBitmasks ? HMath.Remap(HSettings.GTAOSettings.Thickness, 0.0f, 1.0f, 0.1f, 0.35f) : HSettings.GTAOSettings.Thickness;

			float   FovRadians               = camera.fieldOfView * Mathf.Deg2Rad;
			float   TanHalfFOVY              = Mathf.Tan(FovRadians * 0.5f);
			float   TanHalfFOVX              = TanHalfFOVY * ((float)cameraWidth / (float)cameraHeight);
			Vector2 CameraTanHalfFOV         = new Vector2(TanHalfFOVX, TanHalfFOVY);
			Vector2 NDCToViewMul             = new Vector2(CameraTanHalfFOV.x * 2.0f, CameraTanHalfFOV.y * -2.0f);
			Vector2 NDCToViewMul_X_PixelSize = new Vector2(NDCToViewMul.x * (1.0f / (float)cameraWidth), NDCToViewMul.y * (1.0f / cameraHeight));

			float   invHalfTanFov     = 1 / TanHalfFOVY;
			Vector2 focalLen          = new Vector2(invHalfTanFov * ((float)cameraHeight / (float)cameraWidth), invHalfTanFov);
			Vector2 invFocalLen       = new Vector2(1 / focalLen.x, 1 / focalLen.y);
			Vector4 DepthToViewParams = new Vector4(2 * invFocalLen.x, 2 * invFocalLen.y, -1 * invFocalLen.x, -1 * invFocalLen.y);

			var threadGroupsXBy8 = Mathf.CeilToInt(cameraWidth / ScaleFactor / 8.0f);
			var threadGroupsYBy8 = Mathf.CeilToInt(cameraHeight / ScaleFactor / 8.0f);

			// ---------------------------------------- PARAMETERS SET ---------------------------------------- //
			cmd.SetGlobalFloat(HShaderParams.HScaleFactorAO, ScaleFactor);

			cmd.SetComputeFloatParam(HRenderGTAO, HShaderParams.Radius, HSettings.GTAOSettings.WorldSpaceRadius);
			cmd.SetComputeFloatParam(HRenderGTAO, HShaderParams.ScreenSpaceRadiusScale, NDCToViewMul_X_PixelSize.x);
			cmd.SetComputeFloatParam(HRenderGTAO, HShaderParams.Thickness, Thickness);
			cmd.SetComputeIntParam(HRenderGTAO, HShaderParams.StepCount, HSettings.GTAOSettings.StepCount);
			cmd.SetComputeIntParam(HRenderGTAO, HShaderParams.SliceCount, HSettings.GTAOSettings.SliceCount);
			cmd.SetComputeIntParam(HRenderGTAO, HShaderParams.TemporalSamplecount, HSettings.GTAOSettings.SampleCountTemporal);
			cmd.SetComputeIntParam(HRenderGTAO, HShaderParams.Checkerboarding, Checkerboarding ? 1 : 0);
			cmd.SetComputeVectorParam(HRenderGTAO, HShaderParams.DepthToViewParams, DepthToViewParams);

			cmd.SetComputeFloatParam(HDenoiseGTAO, HShaderParams.FilterStrength, HSettings.GTAOSettings.FilterStrength);
			cmd.SetComputeFloatParam(HDenoiseGTAO, HShaderParams.NormalRejection, HSettings.GTAOSettings.NormalRejectionTemporal);
			cmd.SetComputeFloatParam(HDenoiseGTAO, HShaderParams.MotionRejection, HSettings.GTAOSettings.MotionRejection);
			cmd.SetComputeFloatParam(HDenoiseGTAO, HShaderParams.RejectionStrength, HMath.Remap(HSettings.GTAOSettings.RejectionStrengthTemporal, 0.0f, 1.0f, 0.1f, 0.9f));
			cmd.SetComputeIntParam(HDenoiseGTAO, HShaderParams.TemporalSamplecount, HSettings.GTAOSettings.SampleCountTemporal);
			cmd.SetComputeIntParam(HDenoiseGTAO, HShaderParams.Checkerboarding, Checkerboarding ? 1 : 0);

			cmd.SetComputeFloatParam(HInterpolationGTAO, HShaderParams.Intensity, HSettings.GeneralSettings.Intensity);

			// ---------------------------------------- TEMPORAL REPROJECTION ---------------------------------------- //
			if (TemporalAccumulationEnabled)
			{
				using (new HTraceProfilingScope(cmd, TemporalAccumulationSampler))
				{
					KeywordSwitch(HDenoiseGTAO, HSettings.GTAOSettings.NormalRejectionTemporal > Mathf.Epsilon, NORMAL_REJECTION_TEMPORAL);
					KeywordSwitch(HDenoiseGTAO, (int)HSettings.GTAOSettings.ReprojectionFilter == 1, LANCZOS_REPROJECTION);

					cmd.SetComputeTextureParam(HDenoiseGTAO, (int)HDenoiseGTAOKernel.TemporalReprojection, HShaderParams.Normal_History, CameraHistorySystem.GetCameraData().NormalHistory_GTAO.rt);
					cmd.SetComputeTextureParam(HDenoiseGTAO, (int)HDenoiseGTAOKernel.TemporalReprojection, HShaderParams.Occlusion_History, CameraHistorySystem.GetCameraData().OcclusionHistory_GTAO.rt);
					cmd.SetComputeTextureParam(HDenoiseGTAO, (int)HDenoiseGTAOKernel.TemporalReprojection, HShaderParams.Occlusion_Output, OcclusionReprojected_GTAO.rt);
					cmd.DispatchCompute(HDenoiseGTAO, (int)HDenoiseGTAOKernel.TemporalReprojection, threadGroupsXBy8, threadGroupsYBy8, HRenderer.TextureXrSlices);
				}
			}


			// ---------------------------------------- CHECKERBOARDING ---------------------------------------- //
			if (Checkerboarding)
			{
				using (new HTraceProfilingScope(cmd, CheckerboardingSampler))
				{
					cmd.SetComputeTextureParam(HCheckerboardingGTAO, (int)HCheckerboardingKernel.CheckerboardClassification, HShaderParams.OcclusionReprojected, OcclusionReprojected_GTAO.rt);
					cmd.SetComputeBufferParam(HCheckerboardingGTAO, (int)HCheckerboardingKernel.CheckerboardClassification, HShaderParams.RayCounter_Output, RayCounter);
					cmd.SetComputeBufferParam(HCheckerboardingGTAO, (int)HCheckerboardingKernel.CheckerboardClassification, HShaderParams.IndirectCoords_Output, IndirectCoords.ComputeBuffer);
					cmd.DispatchCompute(HCheckerboardingGTAO, (int)HCheckerboardingKernel.CheckerboardClassification, threadGroupsXBy8, threadGroupsYBy8, HRenderer.TextureXrSlices);

					cmd.SetComputeIntParam(HCheckerboardingGTAO, HShaderParams.RayTracedCounter, 0);
					cmd.SetComputeBufferParam(HCheckerboardingGTAO, (int)HCheckerboardingKernel.IndirectArguments, HShaderParams.RayCounter, RayCounter);
					cmd.SetComputeBufferParam(HCheckerboardingGTAO, (int)HCheckerboardingKernel.IndirectArguments, HShaderParams.IndirectArguments_Output, IndirectArguments);
					cmd.DispatchCompute(HCheckerboardingGTAO, (int)HCheckerboardingKernel.IndirectArguments, 1, 1, HRenderer.TextureXrSlices);
				}
			}


			// ---------------------------------------- OCCLUSION RENDER ---------------------------------------- //
			using (new HTraceProfilingScope(cmd, RenderOcclusionSampler))
			{

				KeywordSwitch(HRenderGTAO, Checkerboarding, CHECKERBOARDING);
				KeywordSwitch(HRenderGTAO, HSettings.GTAOSettings.Falloff, FALLOFF);
				KeywordSwitch(HRenderGTAO, HRenderer.TextureXrSlices > 1, VR_COMPATIBILITY);
				KeywordSwitch(HRenderGTAO, TemporalAccumulationEnabled, TEMPORAL_ACCUMULATION);
				KeywordSwitch(HRenderGTAO, HSettings.GTAOSettings.VisibilityBitmasks, VISIBILITY_BITMASKS);

				RenderTexture Output = TemporalAccumulationEnabled ?
					Occlusion_GTAO.rt.rt : OcclusionAccumulated_GTAO.rt.rt;

				cmd.SetComputeTextureParam(HRenderGTAO, (int)HRenderGTAOKernel.RenderGTAO, HShaderParams.Occlusion_Output, Output);
				cmd.SetComputeBufferParam(HRenderGTAO, (int)HRenderGTAOKernel.RenderGTAO, HShaderParams.RayCounter, RayCounter);
				cmd.SetComputeBufferParam(HRenderGTAO, (int)HRenderGTAOKernel.RenderGTAO, HShaderParams.TracingCoords, IndirectCoords.ComputeBuffer);

				if (Checkerboarding)
				{
					cmd.SetComputeIntParam(HRenderGTAO, HShaderParams.IndexXR, 0);
					cmd.DispatchCompute(HRenderGTAO, (int)HRenderGTAOKernel.RenderGTAO, IndirectArguments, 0);

					if (HRenderer.TextureXrSlices > 1)
					{
						cmd.SetComputeIntParam(HRenderGTAO, HShaderParams.IndexXR, 1);
						cmd.DispatchCompute(HRenderGTAO, (int)HRenderGTAOKernel.RenderGTAO, IndirectArguments, sizeof(uint) * 3);
					}
				}
				else cmd.DispatchCompute(HRenderGTAO, (int)HRenderGTAOKernel.RenderGTAO, threadGroupsXBy8, threadGroupsYBy8, HRenderer.TextureXrSlices);
			}


			// ---------------------------------------- TEMPORAL ACCUMULATION ---------------------------------------- //
			if (TemporalAccumulationEnabled)
			{
				using (new HTraceProfilingScope(cmd, TemporalAccumulationSampler))
				{
					cmd.SetComputeTextureParam(HDenoiseGTAO, (int)HDenoiseGTAOKernel.TemporalAccumulation, HShaderParams.Occlusion, Occlusion_GTAO.rt);
					cmd.SetComputeTextureParam(HDenoiseGTAO, (int)HDenoiseGTAOKernel.TemporalAccumulation, HShaderParams.OcclusionReprojected, OcclusionReprojected_GTAO.rt);
					cmd.SetComputeTextureParam(HDenoiseGTAO, (int)HDenoiseGTAOKernel.TemporalAccumulation, HShaderParams.Occlusion_Output, OcclusionAccumulated_GTAO.rt);
					cmd.SetComputeTextureParam(HDenoiseGTAO, (int)HDenoiseGTAOKernel.TemporalAccumulation, HShaderParams.NormalHistory_Output, CameraHistorySystem.GetCameraData().NormalHistory_GTAO.rt);
					cmd.SetComputeTextureParam(HDenoiseGTAO, (int)HDenoiseGTAOKernel.TemporalAccumulation, HShaderParams.OcclusionHistory_Output, CameraHistorySystem.GetCameraData().OcclusionHistory_GTAO.rt);
					cmd.DispatchCompute(HDenoiseGTAO, (int)HDenoiseGTAOKernel.TemporalAccumulation, threadGroupsXBy8, threadGroupsYBy8, HRenderer.TextureXrSlices);
				}
			}

			// ---------------------------------------- SPATIAL FILTER ---------------------------------------- //
			if ((int)HSettings.GTAOSettings.PixelRadius <= 1) {HDenoiseGTAO.EnableKeyword(RADIUS_1); HDenoiseGTAO.DisableKeyword(RADIUS_2); HDenoiseGTAO.DisableKeyword(RADIUS_3); HDenoiseGTAO.DisableKeyword(RADIUS_4);}
			if ((int)HSettings.GTAOSettings.PixelRadius == 2) {HDenoiseGTAO.EnableKeyword(RADIUS_2); HDenoiseGTAO.DisableKeyword(RADIUS_1); HDenoiseGTAO.DisableKeyword(RADIUS_3); HDenoiseGTAO.DisableKeyword(RADIUS_4);}
			if ((int)HSettings.GTAOSettings.PixelRadius == 3) {HDenoiseGTAO.EnableKeyword(RADIUS_3); HDenoiseGTAO.DisableKeyword(RADIUS_1); HDenoiseGTAO.DisableKeyword(RADIUS_2); HDenoiseGTAO.DisableKeyword(RADIUS_4);}
			if ((int)HSettings.GTAOSettings.PixelRadius == 4) {HDenoiseGTAO.EnableKeyword(RADIUS_4); HDenoiseGTAO.DisableKeyword(RADIUS_1); HDenoiseGTAO.DisableKeyword(RADIUS_2); HDenoiseGTAO.DisableKeyword(RADIUS_3);}

			if (SpatialFilterEnabled)
			{
				using (new HTraceProfilingScope(cmd, SpatialFilterSampler))
				{
					KeywordSwitch(HDenoiseGTAO, HRenderer.TextureXrSlices > 1, VR_COMPATIBILITY);
					KeywordSwitch(HDenoiseGTAO, HSettings.GTAOSettings.NormalRejectionSpatial, NORMAL_REJECTION_SPATIAL);

					cmd.SetComputeTextureParam(HDenoiseGTAO, (int)HDenoiseGTAOKernel.SpatialFiltering, HShaderParams.Occlusion, OcclusionAccumulated_GTAO.rt);
					cmd.SetComputeTextureParam(HDenoiseGTAO, (int)HDenoiseGTAOKernel.SpatialFiltering, HShaderParams.Occlusion_Output, OcclusionFiltered_GTAO.rt);
					cmd.DispatchCompute(HDenoiseGTAO, (int)HDenoiseGTAOKernel.SpatialFiltering, threadGroupsXBy8, threadGroupsYBy8, HRenderer.TextureXrSlices);
				}
			}


			// ---------------------------------------- INTERPOLATION ---------------------------------------- //
			using (new HTraceProfilingScope(cmd, InterpolationSampler))
			{
				if ((int)HSettings.GTAOSettings.UpscalingQuality == 0) {HInterpolationGTAO.EnableKeyword(INTERPOLATION_LINEAR_5); HInterpolationGTAO.DisableKeyword(INTERPOLATION_LINEAR_9); HInterpolationGTAO.DisableKeyword(INTERPOLATION_LANCZOS_12);}
				if ((int)HSettings.GTAOSettings.UpscalingQuality == 1) {HInterpolationGTAO.EnableKeyword(INTERPOLATION_LINEAR_9); HInterpolationGTAO.DisableKeyword(INTERPOLATION_LINEAR_5); HInterpolationGTAO.DisableKeyword(INTERPOLATION_LANCZOS_12);}
				if ((int)HSettings.GTAOSettings.UpscalingQuality == 2) {HInterpolationGTAO.EnableKeyword(INTERPOLATION_LANCZOS_12); HInterpolationGTAO.DisableKeyword(INTERPOLATION_LINEAR_5); HInterpolationGTAO.DisableKeyword(INTERPOLATION_LINEAR_9);}

				RenderTexture Input = SpatialFilterEnabled ?
					OcclusionFiltered_GTAO.rt.rt : OcclusionAccumulated_GTAO.rt.rt;
				
				KeywordSwitch(HInterpolationGTAO, HRenderer.TextureXrSlices > 1, VR_COMPATIBILITY);
				KeywordSwitch(HInterpolationGTAO, HSettings.GTAOSettings.UpscalingNormalRejection, NORMAL_REJECTION);
				KeywordSwitch(HInterpolationGTAO, HSettings.GTAOSettings.FullResolution, FINAL_OUTPUT_ONLY);

				cmd.SetComputeTextureParam(HInterpolationGTAO, (int)HInterpolationKernel.Interpolation, HShaderParams.Occlusion, Input);
				cmd.SetComputeTextureParam(HInterpolationGTAO, (int)HInterpolationKernel.Interpolation, HShaderParams.Occlusion_Output, OcclusionInterpolated_GTAO.rt);
				cmd.DispatchCompute(HInterpolationGTAO, (int)HInterpolationKernel.Interpolation, Mathf.CeilToInt(cameraWidth / 8.0f), Mathf.CeilToInt(cameraHeight / 8.0f), HRenderer.TextureXrSlices);
			}

			var FinalOutput = OcclusionInterpolated_GTAO.rt;
			if (HSettings.GTAOSettings.DebugMode == DebugModeGTAO.TemporalDisocclusion) FinalOutput = OcclusionReprojected_GTAO.rt;

			cmd.SetGlobalTexture(HShaderParams.g_HTraceBufferAO, FinalOutput);
			cmd.SetGlobalTexture(HShaderParams.g_ScreenSpaceOcclusionTexture, OcclusionInterpolated_GTAO.rt);
		}

		private static void KeywordSwitch(ComputeShader compute, bool state, string keyword)
		{
			if (state) compute.EnableKeyword(keyword);
			else compute.DisableKeyword(keyword);
		}
	}
}
