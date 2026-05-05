//pipelinedefine
#define H_URP

using System;
using System.Collections.Generic;
using HTraceAO.Scripts.Data.Private;
using HTraceAO.Scripts.Extensions;
using HTraceAO.Scripts.Extensions.CameraHistorySystem;
using HTraceAO.Scripts.Globals;
using HTraceAO.Scripts.Wrappers;
using UnityEngine;
using UnityEngine.Rendering;
#if UNITY_2022
using UnityEngine.Experimental.Rendering;
#endif

namespace HTraceAO.Scripts.Passes.Shared.AO
{
	internal static class RTAO
	{
		private enum HDenoiseRTAOKernel
		{
			TemporalReprojection = 0,
			TemporalAccumulation = 1,
			SpatialFiltering     = 2,
		}

		private enum HCheckerboardingKernel
		{
			CheckerboardClassification = 0,
			IndirectArguments          = 1,
		}

		private enum HRenderRTAOKernel
		{
			RenderRTAO = 0,
		}

		private enum HInterpolationKernel
		{
			Interpolation = 0,
		}

		// Keywords
		private const string NORMAL_REJECTION_TEMPORAL = "NORMAL_REJECTION_TEMPORAL";
		private const string LANCZOS_REPROJECTION = "LANCZOS_REPROJECTION";
		private const string CHECKERBOARDING = "CHECKERBOARDING";
		private const string CULL_BACK_FACES = "CULL_BACK_FACES";
		private const string VR_COMPATIBILITY = "VR_COMPATIBILITY";
		private const string INTERPOLATION_LINEAR_5 = "INTERPOLATION_LINEAR_5";
		private const string INTERPOLATION_LINEAR_9 = "INTERPOLATION_LINEAR_9";
		private const string INTERPOLATION_LANCZOS_12 = "INTERPOLATION_LANCZOS_12";
		private const string NORMAL_REJECTION_SPATIAL = "NORMAL_REJECTION_SPATIAL";
		private const string NORMAL_REJECTION = "NORMAL_REJECTION";
		private const string FINAL_OUTPUT_ONLY = "FINAL_OUTPUT_ONLY";
		private const string RADIUS_1 = "RADIUS_1";
		private const string RADIUS_2 = "RADIUS_2";
		private const string RADIUS_3 = "RADIUS_3";
		private const string RADIUS_4 = "RADIUS_4";

		internal static ComputeShader HRenderRTAO             = null;
		internal static ComputeShader HDenoiseRTAO            = null;
		internal static ComputeShader HInterpolationRTAO      = null;
		internal static ComputeShader HCheckerboardingRTAO    = null;
		internal static RayTracingShader HRayTraceRTAO        = null;

		internal static RayTracingAccelerationStructure RTAS = null;


		// Profiler Samplers
		internal static ProfilingSamplerHTrace CheckerboardingSampler        = new ProfilingSamplerHTrace(HNames.CHECKERBOARDING_SAMPLER,          parentName: HNames.HTRACE_RTAO_PASS_NAME, order: 0);
		internal static ProfilingSamplerHTrace RenderOcclusionSampler        = new ProfilingSamplerHTrace(HNames.RENDER_OCCLUSION_SAMPLER,         parentName: HNames.HTRACE_RTAO_PASS_NAME, order: 1);
		internal static ProfilingSamplerHTrace TemporalAccumulationSampler   = new ProfilingSamplerHTrace(HNames.TEMPORAL_ACCUMULATION_SAMPLER,    parentName: HNames.HTRACE_RTAO_PASS_NAME, order: 2);
		internal static ProfilingSamplerHTrace SpatialFilterSampler          = new ProfilingSamplerHTrace(HNames.SPATIAL_FILTER_SAMPLER,           parentName: HNames.HTRACE_RTAO_PASS_NAME, order: 3);
		internal static ProfilingSamplerHTrace InterpolationSampler          = new ProfilingSamplerHTrace(HNames.INTERPOLATION_SAMPLER,            parentName: HNames.HTRACE_RTAO_PASS_NAME, order: 4);

		internal struct HistoryCameraDataRTAO : ICameraHistoryData, IDisposable
		{
			private int hash;
			public RTWrapper NormalHistory_RTAO;
			public RTWrapper OcclusionHistory_RTAO;

			public HistoryCameraDataRTAO(int hash = 0)
			{
				this.hash = hash;
				NormalHistory_RTAO = new RTWrapper();
				OcclusionHistory_RTAO = new RTWrapper();
			}

			public int GetHash() => hash;
			public void SetHash(int hashIn) => this.hash = hashIn;

			public void Dispose()
			{
				NormalHistory_RTAO?.HRelease();
				OcclusionHistory_RTAO?.HRelease();
			}
		}

		internal static readonly CameraHistorySystem<HistoryCameraDataRTAO> CameraHistorySystem = new CameraHistorySystem<HistoryCameraDataRTAO>();

		// RTAO Buffers
		internal static RTWrapper NormalHistory_RTAO            = new RTWrapper(); //only BIRP
		internal static RTWrapper OcclusionHistory_RTAO         = new RTWrapper(); //only BIRP
		internal static RTWrapper Occlusion_RTAO                = new RTWrapper();
		internal static RTWrapper OcclusionFiltered_RTAO        = new RTWrapper();
		internal static RTWrapper OcclusionInterpolated_RTAO    = new RTWrapper();
		internal static RTWrapper OcclusionAccumulated_RTAO     = new RTWrapper();
		internal static RTWrapper OcclusionReprojected_RTAO     = new RTWrapper();
		internal static RTWrapper DepthPyramid_RTAO             = new RTWrapper();
		internal static RTWrapper VelocityHistory_RTAO          = new RTWrapper();
		internal static RTWrapper VelocityReprojected_RTAO      = new RTWrapper();

		internal static GraphicsBuffer IndirectArguments;
		internal static HDynamicBuffer IndirectCoords;
		internal static ComputeBuffer RayCounter;


		internal const string _DepthPyramid = "_DepthPyramid";
		internal const string _Occlusion = "_Occlusion";
		internal const string _NormalHistory = "_NormalHistory";
		internal const string _VelocityHistory = "_VelocityHistory";
		internal const string _VelocityReprojected = "_VelocityReprojected";
		internal const string _OcclusionAccumulated = "_OcclusionAccumulated";
		internal const string _OcclusionReprojected = "_OcclusionReprojected";
		internal const string _OcclusionHistory = "_OcclusionHistory";
		internal const string _OcclusionFiltered = "_OcclusionFiltered";
		internal const string _OcclusionInterpolated = "_OcclusionInterpolated";
		private static RayTracingInstanceCullingTest[] _instanceTests = new RayTracingInstanceCullingTest[1];
		private static RayTracingInstanceCullingConfig _cullingConfig = new RayTracingInstanceCullingConfig();
		private static RayTracingInstanceCullingTest _instanceTest = new RayTracingInstanceCullingTest();

		internal static void SetupRTAS(Camera camera, int cameraHeight)
		{
#if UNITY_2022_1_OR_NEWER

			_cullingConfig.flags = RayTracingInstanceCullingFlags.EnableLODCulling;

			_cullingConfig.lodParameters.fieldOfView       = camera.fieldOfView;
			_cullingConfig.lodParameters.cameraPixelHeight = cameraHeight;
			_cullingConfig.lodParameters.isOrthographic    = false;
			_cullingConfig.lodParameters.cameraPosition    = camera.transform.position;

			_cullingConfig.subMeshFlagsConfig.opaqueMaterials      = RayTracingSubMeshFlags.Enabled | RayTracingSubMeshFlags.ClosestHitOnly;
			_cullingConfig.subMeshFlagsConfig.transparentMaterials = RayTracingSubMeshFlags.Disabled;
			_cullingConfig.subMeshFlagsConfig.alphaTestedMaterials = RayTracingSubMeshFlags.Disabled;

			_instanceTest.allowOpaqueMaterials      = true;
			_instanceTest.allowAlphaTestedMaterials = true;
			_instanceTest.allowTransparentMaterials = false;
			_instanceTest.layerMask                 = HSettings.RTAOSettings.LayerMask;
			_instanceTest.shadowCastingModeMask     = (1 << (int)ShadowCastingMode.On) | (1 << (int)ShadowCastingMode.TwoSided);
			_instanceTest.instanceMask              = 1 << 0;

			_instanceTests[0] = _instanceTest;

			_cullingConfig.instanceTests = _instanceTests;

			RTAS?.ClearInstances();
			RayTracingInstanceCullingResults? cullingResults = RTAS?.CullInstances(ref _cullingConfig);
#endif
		}

		private static void KeywordSwitch(ComputeShader Compute, bool State, string Keyword)
		{
			if (State) Compute.EnableKeyword(Keyword);
			else Compute.DisableKeyword(Keyword);
		}

		public static void Execute(CommandBuffer cmd, Camera camera, int cameraWidth, int cameraHeight)
		{
			bool InlineRaytracing = true;

			bool Checkerboarding = HSettings.RTAOSettings.Checkerboarding;
			if (IndirectCoords.ComputeBuffer == null || camera.cameraType == CameraType.SceneView)
				Checkerboarding = false;

			float ScaleFactor = HSettings.RTAOSettings.FullResolution ? 1.0f : 2.0f;
			int TemporalSampleCount = (int)HSettings.RTAOSettings.SampleCountTemporal;
			float MaxRayBias = 0.001f;

			MaxRayBias = HSettings.RTAOSettings.MaxRayBias;

			float   FovRadians        = camera.fieldOfView * Mathf.Deg2Rad;
			float   TanHalfFOVY       = Mathf.Tan(FovRadians * 0.5f);
			float   invHalfTanFov     = 1 / TanHalfFOVY;
			Vector2 focalLen          = new Vector2(invHalfTanFov * ((float)cameraHeight  / (float)cameraWidth), invHalfTanFov);
			Vector2 invFocalLen       = new Vector2(1 / focalLen.x, 1 / focalLen.y);
			Vector4 DepthToViewParams = new Vector4(2 * invFocalLen.x, 2 * invFocalLen.y, -1 * invFocalLen.x, -1 * invFocalLen.y);

			var threadGroupsXBy8 = Mathf.CeilToInt(cameraWidth / ScaleFactor / 8.0f);
			var threadGroupsYBy8 = Mathf.CeilToInt(cameraHeight / ScaleFactor / 8.0f);

			// ---------------------------------------- PARAMETERS SET ---------------------------------------- //
			cmd.SetGlobalFloat(HShaderParams.HScaleFactorAO, ScaleFactor);

			cmd.SetComputeFloatParam(HDenoiseRTAO, HShaderParams.FilterStrength,  HSettings.RTAOSettings.FilterStrength);
			cmd.SetComputeFloatParam(HDenoiseRTAO, HShaderParams.MotionRejection, HSettings.RTAOSettings.MotionRejection);
			cmd.SetComputeFloatParam(HDenoiseRTAO, HShaderParams.NormalRejection, HSettings.RTAOSettings.NormalRejectionTemporal);
			cmd.SetComputeFloatParam(HDenoiseRTAO, HShaderParams.RejectionStrength, HMath.Remap(HSettings.RTAOSettings.RejectionStrengthTemporal, 0.0f, 1.0f, 0.1f, 0.9f));
			cmd.SetComputeIntParam(HDenoiseRTAO, HShaderParams.TemporalSamplecount, TemporalSampleCount);
			cmd.SetComputeIntParam(HDenoiseRTAO,  HShaderParams.Checkerboarding, Checkerboarding ? 1 : 0);
			cmd.SetComputeVectorParam(HDenoiseRTAO, HShaderParams.DepthToViewParams, DepthToViewParams);


			cmd.SetComputeFloatParam(HRenderRTAO, HShaderParams.MaxRayBias, MaxRayBias);
			cmd.SetComputeFloatParam(HRenderRTAO, HShaderParams.MaxRayDistance, HSettings.RTAOSettings.WorldSpaceRadius);
			cmd.SetComputeIntParam(HRenderRTAO, HShaderParams.RaySampleCount, HSettings.RTAOSettings.RayCount);
			cmd.SetComputeIntParam(HRenderRTAO, HShaderParams.TemporalSamplecount, TemporalSampleCount);

			cmd.SetComputeFloatParam(HInterpolationRTAO, HShaderParams.Intensity, HSettings.GeneralSettings.Intensity);


			// ---------------------------------------- TEMPORAL REPROJECTION ---------------------------------------- //
			using (new HTraceProfilingScope(cmd, TemporalAccumulationSampler))
			{
				KeywordSwitch(HDenoiseRTAO, HSettings.RTAOSettings.NormalRejectionTemporal > Mathf.Epsilon, NORMAL_REJECTION_TEMPORAL);
				KeywordSwitch(HDenoiseRTAO, (int)HSettings.RTAOSettings.ReprojectionFilter == 1 ,LANCZOS_REPROJECTION);

				cmd.SetComputeTextureParam(HDenoiseRTAO, (int)HDenoiseRTAOKernel.TemporalReprojection, HShaderParams.Velocity_History, VelocityHistory_RTAO.rt);
				cmd.SetComputeTextureParam(HDenoiseRTAO, (int)HDenoiseRTAOKernel.TemporalReprojection, HShaderParams.Velocity_Output, VelocityReprojected_RTAO.rt);
				cmd.SetComputeTextureParam(HDenoiseRTAO, (int)HDenoiseRTAOKernel.TemporalReprojection, HShaderParams.Normal_History, CameraHistorySystem.GetCameraData().NormalHistory_RTAO.rt);
				cmd.SetComputeTextureParam(HDenoiseRTAO, (int)HDenoiseRTAOKernel.TemporalReprojection, HShaderParams.Occlusion_History, CameraHistorySystem.GetCameraData().OcclusionHistory_RTAO.rt);
				cmd.SetComputeTextureParam(HDenoiseRTAO, (int)HDenoiseRTAOKernel.TemporalReprojection, HShaderParams.Occlusion_Output, OcclusionReprojected_RTAO.rt);
				cmd.DispatchCompute(HDenoiseRTAO, (int)HDenoiseRTAOKernel.TemporalReprojection, threadGroupsXBy8, threadGroupsYBy8, HRenderer.TextureXrSlices);
			}


			// ---------------------------------------- CHECKERBOARDING ---------------------------------------- //
			if (Checkerboarding)
			{
				using (new HTraceProfilingScope(cmd, CheckerboardingSampler))
				{
					cmd.SetComputeTextureParam(HCheckerboardingRTAO, (int)HCheckerboardingKernel.CheckerboardClassification, HShaderParams.OcclusionReprojected, OcclusionReprojected_RTAO.rt);
					cmd.SetComputeBufferParam(HCheckerboardingRTAO, (int)HCheckerboardingKernel.CheckerboardClassification, HShaderParams.RayCounter_Output, RayCounter);
					cmd.SetComputeBufferParam(HCheckerboardingRTAO, (int)HCheckerboardingKernel.CheckerboardClassification, HShaderParams.IndirectCoords_Output, IndirectCoords.ComputeBuffer);
					cmd.DispatchCompute(HCheckerboardingRTAO, (int)HCheckerboardingKernel.CheckerboardClassification, threadGroupsXBy8, threadGroupsYBy8, HRenderer.TextureXrSlices);

					cmd.SetComputeIntParam(HCheckerboardingRTAO, HShaderParams.RayTracedCounter, !InlineRaytracing ? 1 : 0);
					cmd.SetComputeBufferParam(HCheckerboardingRTAO, (int)HCheckerboardingKernel.IndirectArguments, HShaderParams.RayCounter, RayCounter);
					cmd.SetComputeBufferParam(HCheckerboardingRTAO, (int)HCheckerboardingKernel.IndirectArguments, HShaderParams.IndirectArguments_Output, IndirectArguments);
					cmd.DispatchCompute(HCheckerboardingRTAO, (int)HCheckerboardingKernel.IndirectArguments, 1, 1, HRenderer.TextureXrSlices);
				}
			}


			// ---------------------------------------- OCCLUSION TRACING ---------------------------------------- //
			using (new HTraceProfilingScope(cmd, RenderOcclusionSampler))
			{
				if (!InlineRaytracing)
				{
				}
				else
				{
				#if UNITY_2023_2_OR_NEWER
					SetupRTAS(camera, cameraHeight);
					cmd.BuildRayTracingAccelerationStructure(RTAS);

					KeywordSwitch(HRenderRTAO, Checkerboarding, CHECKERBOARDING);
					KeywordSwitch(HRenderRTAO, HSettings.RTAOSettings.CullBackfaces, CULL_BACK_FACES);

					cmd.SetComputeTextureParam(HRenderRTAO, (int)HRenderRTAOKernel.RenderRTAO, HShaderParams.DepthPyramid, DepthPyramid_RTAO.rt);
					cmd.SetComputeTextureParam(HRenderRTAO, (int)HRenderRTAOKernel.RenderRTAO, HShaderParams.Occlusion_Output, Occlusion_RTAO.rt);
					cmd.SetComputeBufferParam(HRenderRTAO, (int)HRenderRTAOKernel.RenderRTAO, HShaderParams.RayCounter, RayCounter);
					cmd.SetComputeBufferParam(HRenderRTAO, (int)HRenderRTAOKernel.RenderRTAO, HShaderParams.TracingCoords, IndirectCoords.ComputeBuffer);
					cmd.SetRayTracingAccelerationStructure(HRenderRTAO, (int)HRenderRTAOKernel.RenderRTAO, HShaderParams.RTAS, RTAS);

					if (Checkerboarding)
					{
						cmd.SetComputeIntParam(HRenderRTAO, HShaderParams.IndexXR, 0);
						cmd.DispatchCompute(HRenderRTAO, (int)HRenderRTAOKernel.RenderRTAO, IndirectArguments, 0);

						if (HRenderer.TextureXrSlices > 1)
						{
							cmd.SetComputeIntParam(HRenderRTAO, HShaderParams.IndexXR, 1);
							cmd.DispatchCompute(HRenderRTAO, (int)HRenderRTAOKernel.RenderRTAO, IndirectArguments, sizeof(uint) * 3);
						}
					}
					else cmd.DispatchCompute(HRenderRTAO, (int)HRenderRTAOKernel.RenderRTAO, threadGroupsXBy8, threadGroupsYBy8, HRenderer.TextureXrSlices);
				#endif
				}
			}


			// ---------------------------------------- TEMPORAL ACCUMULATION ---------------------------------------- //
			using (new HTraceProfilingScope(cmd, TemporalAccumulationSampler))
			{
				cmd.SetComputeTextureParam(HDenoiseRTAO, (int)HDenoiseRTAOKernel.TemporalAccumulation, HShaderParams.Occlusion, Occlusion_RTAO.rt);
				cmd.SetComputeTextureParam(HDenoiseRTAO, (int)HDenoiseRTAOKernel.TemporalAccumulation, HShaderParams.VelocityReprojected, VelocityReprojected_RTAO.rt);
				cmd.SetComputeTextureParam(HDenoiseRTAO, (int)HDenoiseRTAOKernel.TemporalAccumulation, HShaderParams.OcclusionReprojected, OcclusionReprojected_RTAO.rt);
				cmd.SetComputeTextureParam(HDenoiseRTAO, (int)HDenoiseRTAOKernel.TemporalAccumulation, HShaderParams.Occlusion_Output, OcclusionAccumulated_RTAO.rt);
				cmd.SetComputeTextureParam(HDenoiseRTAO, (int)HDenoiseRTAOKernel.TemporalAccumulation, HShaderParams.NormalHistory_Output, CameraHistorySystem.GetCameraData().NormalHistory_RTAO.rt);
				cmd.SetComputeTextureParam(HDenoiseRTAO, (int)HDenoiseRTAOKernel.TemporalAccumulation, HShaderParams.OcclusionHistory_Output, CameraHistorySystem.GetCameraData().OcclusionHistory_RTAO.rt);
				cmd.SetComputeTextureParam(HDenoiseRTAO, (int)HDenoiseRTAOKernel.TemporalAccumulation, HShaderParams.VelocityHistory_Output, VelocityHistory_RTAO.rt);
				cmd.DispatchCompute(HDenoiseRTAO, (int)HDenoiseRTAOKernel.TemporalAccumulation, threadGroupsXBy8, threadGroupsYBy8, HRenderer.TextureXrSlices);
			}

			
			// ---------------------------------------- SPATIAL FILTER ---------------------------------------- //
			using (new HTraceProfilingScope(cmd, SpatialFilterSampler))
			{	
				if ((int)HSettings.RTAOSettings.PixelRadius <= 1) {HDenoiseRTAO.EnableKeyword(RADIUS_1); HDenoiseRTAO.DisableKeyword(RADIUS_2); HDenoiseRTAO.DisableKeyword(RADIUS_3); HDenoiseRTAO.DisableKeyword(RADIUS_4);}
				if ((int)HSettings.RTAOSettings.PixelRadius == 2) {HDenoiseRTAO.EnableKeyword(RADIUS_2); HDenoiseRTAO.DisableKeyword(RADIUS_1); HDenoiseRTAO.DisableKeyword(RADIUS_3); HDenoiseRTAO.DisableKeyword(RADIUS_4);}
				if ((int)HSettings.RTAOSettings.PixelRadius == 3) {HDenoiseRTAO.EnableKeyword(RADIUS_3); HDenoiseRTAO.DisableKeyword(RADIUS_1); HDenoiseRTAO.DisableKeyword(RADIUS_2); HDenoiseRTAO.DisableKeyword(RADIUS_4);}
				if ((int)HSettings.RTAOSettings.PixelRadius == 4) {HDenoiseRTAO.EnableKeyword(RADIUS_4); HDenoiseRTAO.DisableKeyword(RADIUS_1); HDenoiseRTAO.DisableKeyword(RADIUS_2); HDenoiseRTAO.DisableKeyword(RADIUS_3);}
				
				KeywordSwitch(HDenoiseRTAO, HRenderer.TextureXrSlices > 1, VR_COMPATIBILITY);
				KeywordSwitch(HDenoiseRTAO, HSettings.RTAOSettings.NormalRejectionSpatial, NORMAL_REJECTION_SPATIAL);

				cmd.SetComputeTextureParam(HDenoiseRTAO, (int)HDenoiseRTAOKernel.SpatialFiltering, HShaderParams.Occlusion, OcclusionAccumulated_RTAO.rt);
				cmd.SetComputeTextureParam(HDenoiseRTAO, (int)HDenoiseRTAOKernel.SpatialFiltering, HShaderParams.Occlusion_Output, OcclusionFiltered_RTAO.rt);
				cmd.DispatchCompute(HDenoiseRTAO, (int)HDenoiseRTAOKernel.SpatialFiltering, threadGroupsXBy8, threadGroupsYBy8, HRenderer.TextureXrSlices);
			}
			
			
			// ---------------------------------------- INTERPOLATION ---------------------------------------- //
			using (new HTraceProfilingScope(cmd, InterpolationSampler))
			{		
				if ((int)HSettings.RTAOSettings.UpscalingQuality == 0) {HInterpolationRTAO.EnableKeyword(INTERPOLATION_LINEAR_5); HInterpolationRTAO.DisableKeyword(INTERPOLATION_LINEAR_9); HInterpolationRTAO.DisableKeyword(INTERPOLATION_LANCZOS_12);}
				if ((int)HSettings.RTAOSettings.UpscalingQuality == 1) {HInterpolationRTAO.EnableKeyword(INTERPOLATION_LINEAR_9); HInterpolationRTAO.DisableKeyword(INTERPOLATION_LINEAR_5); HInterpolationRTAO.DisableKeyword(INTERPOLATION_LANCZOS_12);}
				if ((int)HSettings.RTAOSettings.UpscalingQuality == 2) {HInterpolationRTAO.EnableKeyword(INTERPOLATION_LANCZOS_12); HInterpolationRTAO.DisableKeyword(INTERPOLATION_LINEAR_5); HInterpolationRTAO.DisableKeyword(INTERPOLATION_LINEAR_9);}
				
				KeywordSwitch(HInterpolationRTAO, HRenderer.TextureXrSlices > 1, VR_COMPATIBILITY);
				KeywordSwitch(HInterpolationRTAO, HSettings.RTAOSettings.UpscalingNormalRejection, NORMAL_REJECTION);
				KeywordSwitch(HInterpolationRTAO, HSettings.RTAOSettings.FullResolution, FINAL_OUTPUT_ONLY);

				cmd.SetComputeTextureParam(HInterpolationRTAO, (int)HInterpolationKernel.Interpolation, HShaderParams.Occlusion, OcclusionFiltered_RTAO.rt);
				cmd.SetComputeTextureParam(HInterpolationRTAO, (int)HInterpolationKernel.Interpolation, HShaderParams.Occlusion_Output, OcclusionInterpolated_RTAO.rt);
				cmd.DispatchCompute(HInterpolationRTAO, (int)HInterpolationKernel.Interpolation, Mathf.CeilToInt(cameraWidth / 8.0f), Mathf.CeilToInt(cameraHeight / 8.0f), HRenderer.TextureXrSlices);
			}

			var FinalOutput = OcclusionInterpolated_RTAO.rt;
			if (HSettings.RTAOSettings.DebugMode == DebugModeRTAO.TemporalDisocclusion) FinalOutput = OcclusionReprojected_RTAO.rt;
			if (HSettings.RTAOSettings.DebugMode == DebugModeRTAO.MotionRejectionMask) FinalOutput = VelocityReprojected_RTAO.rt;

			cmd.SetGlobalTexture(HShaderParams.g_HTraceBufferAO, FinalOutput);
			cmd.SetGlobalTexture(HShaderParams.g_ScreenSpaceOcclusionTexture, OcclusionInterpolated_RTAO.rt);
		}
	}
}
