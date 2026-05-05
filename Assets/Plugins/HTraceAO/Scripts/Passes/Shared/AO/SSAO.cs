//pipelinedefine
#define H_URP

using HTraceAO.Scripts.Data.Private;
using HTraceAO.Scripts.Extensions;
using HTraceAO.Scripts.Globals;
using HTraceAO.Scripts.Wrappers;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace HTraceAO.Scripts.Passes.Shared.AO
{
	internal static class SSAO
	{
		private enum HDepthPyramidSSAOKernel
		{
			DepthDownsample_1 = 0,
			DepthDownsample_2 = 1,
		}

		private enum HRenderSSAOKernel
		{
			RenderOcclusion = 0,
		}

		private enum HDenoiseSSAOKernel
		{
			DenoiseOcclusion_A = 0,
			DenoiseOcclusion_B = 1,
		}

		internal static ComputeShader HRenderSSAO   = null;
		internal static ComputeShader HDenoiseSSAO  = null;
		internal static ComputeShader HDepthPyramidSSAO = null;


		// Profiler Samplers
		internal static ProfilingSamplerHTrace DepthPyramidGenerationSampler = new ProfilingSamplerHTrace(HNames.DEPTH_PYRAMID_GENERATION_SAMPLER, parentName: HNames.HTRACE_SSAO_PASS_NAME, order: 0);
		internal static ProfilingSamplerHTrace RenderOcclusionSampler        = new ProfilingSamplerHTrace(HNames.RENDER_OCCLUSION_SAMPLER,         parentName: HNames.HTRACE_SSAO_PASS_NAME, order: 1);
		internal static ProfilingSamplerHTrace InterpolationSampler          = new ProfilingSamplerHTrace(HNames.INTERPOLATION_SAMPLER,            parentName: HNames.HTRACE_SSAO_PASS_NAME, order: 2);

		public static void MaterialsShadersSetup()
		{
			HRenderSSAO       = HExtensions.LoadComputeShader("HRenderSSAO");
			HDenoiseSSAO      = HExtensions.LoadComputeShader("HDenoiseSSAO");
			HDepthPyramidSSAO = HExtensions.LoadComputeShader("HDepthPyramidSSAO");
		}

		// SSAO Buffers
		internal static RTWrapper DepthTiled_SSAO                = new RTWrapper();
		internal static RTWrapper DepthPyramid_SSAO              = new RTWrapper();
		internal static RTWrapper DepthIntermediatePyramid_SSAO  = new RTWrapper();
		internal static RTWrapper Occlusion_SSAO_1               = new RTWrapper();
		internal static RTWrapper Occlusion_SSAO_2               = new RTWrapper();
		internal static RTWrapper Occlusion_SSAO_3               = new RTWrapper();
		internal static RTWrapper Occlusion_SSAO_4               = new RTWrapper();
		internal static RTWrapper OcclusionCombined_SSAO_0       = new RTWrapper();
		internal static RTWrapper OcclusionCombined_SSAO_1       = new RTWrapper();
		internal static RTWrapper OcclusionCombined_SSAO_2       = new RTWrapper();
		internal static RTWrapper OcclusionCombined_SSAO_3       = new RTWrapper();
		
		
		internal static RTWrapper[] Occlusion_SSAO_Array    = new RTWrapper[4];
		internal static RTWrapper[] Occlusion_LowRes_Array  = new RTWrapper[4];
		internal static RTWrapper[] Occlusion_HighRes_Array = new RTWrapper[4];
		internal static RTWrapper[] Occlusion_Output_Array  = new RTWrapper[4];

		// Local variables
		internal const string _DepthTiled                    = "_DepthTiled";
		internal const string _DepthPyramid_SSAO             = "_DepthPyramid_SSAO";
		internal const string _DepthIntermediatePyramid_SSAO = "_DepthIntermediatePyramid_SSAO";
		internal const string _Occlusion_1                   = "_Occlusion_1";
		internal const string _Occlusion_2                   = "_Occlusion_2";
		internal const string _Occlusion_3                   = "_Occlusion_3";
		internal const string _Occlusion_4                   = "_Occlusion_4";
		internal const string _OcclusionCombined_0      = "_OcclusionCombined_0";
		internal const string _OcclusionCombined_1      = "_OcclusionCombined_1";
		internal const string _OcclusionCombined_2      = "_OcclusionCombined_2";
		internal const string _OcclusionCombined_3      = "_OcclusionCombined_3";
		
		static readonly float [] SampleThickness = {
			Mathf.Sqrt(1 - 0.2f * 0.2f),
			Mathf.Sqrt(1 - 0.4f * 0.4f),
			Mathf.Sqrt(1 - 0.6f * 0.6f),
			Mathf.Sqrt(1 - 0.8f * 0.8f),
			Mathf.Sqrt(1 - 0.2f * 0.2f - 0.2f * 0.2f),
			Mathf.Sqrt(1 - 0.2f * 0.2f - 0.4f * 0.4f),
			Mathf.Sqrt(1 - 0.2f * 0.2f - 0.6f * 0.6f),
			Mathf.Sqrt(1 - 0.2f * 0.2f - 0.8f * 0.8f),
			Mathf.Sqrt(1 - 0.4f * 0.4f - 0.4f * 0.4f),
			Mathf.Sqrt(1 - 0.4f * 0.4f - 0.6f * 0.6f),
			Mathf.Sqrt(1 - 0.4f * 0.4f - 0.8f * 0.8f),
			Mathf.Sqrt(1 - 0.6f * 0.6f - 0.6f * 0.6f)
		};
		
		static readonly float [] InvThicknessTable = new float [12];
		static readonly float [] SampleWeightTable = new float [12];
		
		
		private static void UpdateTables(Vector2 depthRes, float tanHalfFovH, float screenspaceDiameter)
		{
			var thicknessMultiplier = 2.0f * tanHalfFovH * screenspaceDiameter / depthRes.x;
			var inverseRangeFactor  = 1.0f / thicknessMultiplier;
			
			for (var i = 0; i < 12; i++)
				InvThicknessTable[i] = inverseRangeFactor / SampleThickness[i];

			SampleWeightTable[0]  = 4 * SampleThickness[0];  // Axial
			SampleWeightTable[1]  = 4 * SampleThickness[1];  // Axial
			SampleWeightTable[2]  = 4 * SampleThickness[2];  // Axial
			SampleWeightTable[3]  = 4 * SampleThickness[3];  // Axial
			SampleWeightTable[4]  = 4 * SampleThickness[4];  // Diagonal
			SampleWeightTable[5]  = 8 * SampleThickness[5];  // L-shaped
			SampleWeightTable[6]  = 8 * SampleThickness[6];  // L-shaped
			SampleWeightTable[7]  = 8 * SampleThickness[7];  // L-shaped
			SampleWeightTable[8]  = 4 * SampleThickness[8];  // Diagonal
			SampleWeightTable[9]  = 8 * SampleThickness[9];  // L-shaped
			SampleWeightTable[10] = 8 * SampleThickness[10]; // L-shaped
			SampleWeightTable[11] = 4 * SampleThickness[11]; // Diagonal

			// Zero out the unused samples.
			// SampleWeightTable[0] = 0;
			// SampleWeightTable[2] = 0;
			// SampleWeightTable[5] = 0;
			// SampleWeightTable[7] = 0;
			// SampleWeightTable[9] = 0;

			var WeightTotal = 0.0f;

			foreach (var w in SampleWeightTable)
				WeightTotal += w;

			for (var i = 0; i < SampleWeightTable.Length; i++)
				SampleWeightTable[i] /= WeightTotal;
		}

		
		public static void Execute(CommandBuffer cmd, Camera camera, int cameraWidth, int cameraHeight)
		{	
			cmd.SetGlobalFloat(HShaderParams.HScaleFactorAO, 1.0f);
			
			using (new HTraceProfilingScope(cmd, DepthPyramidGenerationSampler))
			{	
				Vector2Int depthPyramidResolution = HMath.CalculateDepthPyramidResolution(new Vector2Int((int)cameraWidth, (int)cameraHeight), 5);

				cmd.SetComputeTextureParam(HDepthPyramidSSAO, (int)HDepthPyramidSSAOKernel.DepthDownsample_1, HShaderParams.DepthTiled_OutputMIP0,   DepthTiled_SSAO.rt,   0);
				cmd.SetComputeTextureParam(HDepthPyramidSSAO, (int)HDepthPyramidSSAOKernel.DepthDownsample_1, HShaderParams.DepthTiled_OutputMIP1,   DepthTiled_SSAO.rt,   1);
				cmd.SetComputeTextureParam(HDepthPyramidSSAO, (int)HDepthPyramidSSAOKernel.DepthDownsample_1, HShaderParams.DepthPyramid_OutputMIP0, DepthPyramid_SSAO.rt, 0);
				cmd.SetComputeTextureParam(HDepthPyramidSSAO, (int)HDepthPyramidSSAOKernel.DepthDownsample_1, HShaderParams.DepthPyramid_OutputMIP1, DepthPyramid_SSAO.rt, 1);
				cmd.SetComputeTextureParam(HDepthPyramidSSAO, (int)HDepthPyramidSSAOKernel.DepthDownsample_1, HShaderParams.DepthIntermediate_Output,                DepthIntermediatePyramid_SSAO.rt);
				cmd.DispatchCompute(HDepthPyramidSSAO, (int)HDepthPyramidSSAOKernel.DepthDownsample_1, Mathf.CeilToInt(depthPyramidResolution.x / 2.0f / 8.0f), Mathf.CeilToInt(depthPyramidResolution.y / 2.0f / 8.0f), HRenderer.TextureXrSlices);

				if (HSettings.SSAOSettings.Radius > 2)
				{
					cmd.SetComputeTextureParam(HDepthPyramidSSAO, (int)HDepthPyramidSSAOKernel.DepthDownsample_2, HShaderParams.DepthTiled_OutputMIP2,   DepthTiled_SSAO.rt,   2);
					cmd.SetComputeTextureParam(HDepthPyramidSSAO, (int)HDepthPyramidSSAOKernel.DepthDownsample_2, HShaderParams.DepthTiled_OutputMIP3,   DepthTiled_SSAO.rt,   3);
					cmd.SetComputeTextureParam(HDepthPyramidSSAO, (int)HDepthPyramidSSAOKernel.DepthDownsample_2, HShaderParams.DepthPyramid_OutputMIP2, DepthPyramid_SSAO.rt, 2);
					cmd.SetComputeTextureParam(HDepthPyramidSSAO, (int)HDepthPyramidSSAOKernel.DepthDownsample_2, HShaderParams.DepthPyramid_OutputMIP3, DepthPyramid_SSAO.rt, 3);
					cmd.SetComputeTextureParam(HDepthPyramidSSAO, (int)HDepthPyramidSSAOKernel.DepthDownsample_2, HShaderParams.DepthIntermediate,       DepthIntermediatePyramid_SSAO.rt);
					cmd.DispatchCompute(HDepthPyramidSSAO, (int)HDepthPyramidSSAOKernel.DepthDownsample_2, Mathf.CeilToInt(depthPyramidResolution.x / 8.0f / 8.0f), Mathf.CeilToInt(depthPyramidResolution.y / 8.0f / 8.0f), HRenderer.TextureXrSlices);
				}
			}
			
			using (new HTraceProfilingScope(cmd, RenderOcclusionSampler))
			{
				const float screenSpaceDiameter = 10;
				float       tanHalfFovH         = 1 / camera.projectionMatrix[0, 0];
				cmd.SetComputeFloatParam(HRenderSSAO, HShaderParams.RejectFadeoff, -1.0f / (Mathf.Clamp(HSettings.SSAOSettings.Thickness, 0.1f, 1.0f) * 10));
				cmd.SetComputeFloatParam(HRenderSSAO, HShaderParams.Intensity, HSettings.GeneralSettings.Intensity);
				cmd.SetComputeTextureParam(HRenderSSAO, (int)HRenderSSAOKernel.RenderOcclusion, HShaderParams.DepthTiled, DepthTiled_SSAO.rt);
				
				Occlusion_SSAO_Array[0] = Occlusion_SSAO_1;
				Occlusion_SSAO_Array[1] = Occlusion_SSAO_2;
				Occlusion_SSAO_Array[2] = Occlusion_SSAO_3;
				Occlusion_SSAO_Array[3] = Occlusion_SSAO_4;

				for (int passIndex = 0; passIndex < HSettings.SSAOSettings.Radius; passIndex++)
				{
					float scaleFactor = 8.0f * Mathf.Pow(2, passIndex);
					Vector2 depthRes = new Vector2(cameraWidth / scaleFactor, cameraHeight / scaleFactor);
					UpdateTables(depthRes, tanHalfFovH, screenSpaceDiameter);

					cmd.SetComputeTextureParam(HRenderSSAO, (int)HRenderSSAOKernel.RenderOcclusion, HShaderParams.Occlusion_Output, Occlusion_SSAO_Array[passIndex].rt);
					cmd.SetComputeVectorParam(HRenderSSAO, HShaderParams.DepthScale, new Vector2(depthRes.x, depthRes.y));
					cmd.SetComputeFloatParams(HRenderSSAO, HShaderParams.InvThicknessTable, InvThicknessTable);
					cmd.SetComputeFloatParams(HRenderSSAO, HShaderParams.SampleWeightTable, SampleWeightTable);
					cmd.SetComputeIntParam(HRenderSSAO, HShaderParams.PassNumber, passIndex);
					cmd.SetComputeIntParam(HRenderSSAO, HShaderParams.SliceXR, HRenderer.TextureXrSlices > 1 ? 1 : 0);
					cmd.DispatchCompute(HRenderSSAO, (int)HRenderSSAOKernel.RenderOcclusion, Mathf.CeilToInt(cameraWidth / scaleFactor / 8.0f), Mathf.CeilToInt(cameraHeight / scaleFactor / 8.0f), HRenderer.TextureXrSlices * 16);
				}
			}
			
				
			using (new HTraceProfilingScope(cmd, InterpolationSampler))
			{
				var upsampleTolerance = Mathf.Pow(10, -12.0f);
				var noiseFilterWeight = 1 / (Mathf.Pow(10, 0) + upsampleTolerance);
				
				int denosie_ssao_Kernel = (int)HDenoiseSSAOKernel.DenoiseOcclusion_A;
				cmd.SetComputeFloatParam(HDenoiseSSAO, HShaderParams.NoiseFilterStrength, noiseFilterWeight);
				cmd.SetComputeFloatParam(HDenoiseSSAO, HShaderParams.UpsampleTolerance, upsampleTolerance);

				Occlusion_LowRes_Array[0] = Occlusion_SSAO_1;
				Occlusion_LowRes_Array[1] = Occlusion_SSAO_2;
				Occlusion_LowRes_Array[2] = Occlusion_SSAO_3;
				Occlusion_LowRes_Array[3] = Occlusion_SSAO_4;
				
				if (HSettings.SSAOSettings.Radius == 2)
					Occlusion_LowRes_Array[0] = OcclusionCombined_SSAO_1;
				if (HSettings.SSAOSettings.Radius == 3)
				{
					Occlusion_LowRes_Array[0] = OcclusionCombined_SSAO_1;
					Occlusion_LowRes_Array[1] = OcclusionCombined_SSAO_2;
				}

				if (HSettings.SSAOSettings.Radius == 4)
				{
					Occlusion_LowRes_Array[0] = OcclusionCombined_SSAO_1;
					Occlusion_LowRes_Array[1] = OcclusionCombined_SSAO_2;
					Occlusion_LowRes_Array[2] = OcclusionCombined_SSAO_3;
				}
				
				Occlusion_HighRes_Array[0] = Occlusion_SSAO_1 /* null */;
				Occlusion_HighRes_Array[1] = Occlusion_SSAO_1;
				Occlusion_HighRes_Array[2] = Occlusion_SSAO_2;
				Occlusion_HighRes_Array[3] = Occlusion_SSAO_3;
				Occlusion_Output_Array[0] = OcclusionCombined_SSAO_0;
				Occlusion_Output_Array[1] = OcclusionCombined_SSAO_1;
				Occlusion_Output_Array[2] = OcclusionCombined_SSAO_2;
				Occlusion_Output_Array[3] = OcclusionCombined_SSAO_3;
				
				for (int passIndex = (HSettings.SSAOSettings.Radius - 1); passIndex >= 0; passIndex--)
				{
					if (passIndex == 0)
					{	
						denosie_ssao_Kernel = (int)HDenoiseSSAOKernel.DenoiseOcclusion_B;
					}
					
					var stepSize = Mathf.Pow(2, passIndex + 1);
					var blurTolerance = 1 - Mathf.Pow(10, -4.6f) * stepSize;
					blurTolerance *= blurTolerance;
					
					cmd.SetComputeIntParam(HDenoiseSSAO, HShaderParams.PassNumber, passIndex);
					cmd.SetComputeFloatParam(HDenoiseSSAO, HShaderParams.StepSize, stepSize);
					cmd.SetComputeFloatParam(HDenoiseSSAO, HShaderParams.BlurTolerance, blurTolerance);
					cmd.SetComputeTextureParam(HDenoiseSSAO, denosie_ssao_Kernel, HShaderParams.DepthPyramid, DepthPyramid_SSAO.rt);
					cmd.SetComputeTextureParam(HDenoiseSSAO, denosie_ssao_Kernel, HShaderParams.OcclusionLowRes, Occlusion_LowRes_Array[passIndex].rt);	// inverted textures order
					cmd.SetComputeTextureParam(HDenoiseSSAO, denosie_ssao_Kernel, HShaderParams.OcclusionHighRes, Occlusion_HighRes_Array[passIndex].rt);	// inverted textures order
					cmd.SetComputeTextureParam(HDenoiseSSAO, denosie_ssao_Kernel, HShaderParams.Occlusion_Output, Occlusion_Output_Array[passIndex].rt);	// inverted textures order
					cmd.DispatchCompute(HDenoiseSSAO, denosie_ssao_Kernel, ((int)cameraWidth * 2 / (int)stepSize + 17) / 16, ((int)cameraHeight * 2 / (int)stepSize + 17) / 16, HRenderer.TextureXrSlices);
				}
			}
			
			cmd.SetGlobalTexture(HShaderParams.g_HTraceBufferAO, OcclusionCombined_SSAO_0.rt);
			cmd.SetGlobalTexture(HShaderParams.g_ScreenSpaceOcclusionTexture, OcclusionCombined_SSAO_0.rt);
		}
	}
}
