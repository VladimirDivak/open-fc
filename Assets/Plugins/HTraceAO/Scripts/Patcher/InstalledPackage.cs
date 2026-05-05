//pipelinedefine
#define H_URP

#if UNITY_EDITOR
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEngine;
using UnityEngine.Rendering;
using HTraceAO.Scripts.Globals;
using HTraceAO.Scripts.PipelelinesConfigurator;
using UnityEngine.Rendering.Universal;

namespace HTraceAO.Scripts.Patcher
{
	public class InstalledPackage// : AssetPostprocessor
	{
		[InitializeOnLoadMethod]
		private static void InitializeOnLoad()
		{
#if UNITY_2022
			string filePath_hRenderRTAO = Path.Combine(ConfiguratorUtils.GetHTraceFolderPath(), "Resources", "HTraceAO", "Computes", "HRenderRTAO.compute");
			
			if (File.Exists(filePath_hRenderRTAO) && File.ReadAllLines(filePath_hRenderRTAO).Length > 10)
			{
				string[] hRenderRTAO2022 = new string[]
				{
					"#pragma kernel RenderRTAO",
					"[numthreads(8, 8, 1)]",
					"void RenderRTAO(uint3 pixCoord : SV_DispatchThreadID, uint groupIndex : SV_GroupIndex, uint groupID : SV_GroupID)",
					"{",
					"}",
				};
			
				File.WriteAllLines(filePath_hRenderRTAO, hRenderRTAO2022);
			}

// 			string   filePath_hMainHSLS = Path.Combine(ConfiguratorUtils.GetHTraceFolderPath(), "Resources", "HTraceAO", "Headers", "HMain.hlsl");
// 			if (File.Exists(filePath_hMainHSLS))
// 			{
// 				string[] allLines_hMainHSLS = File.ReadAllLines(filePath_hMainHSLS);
// 				if (string.IsNullOrEmpty(allLines_hMainHSLS[3]))
// 				{
// 					allLines_hMainHSLS[3] = "#define _RTHandleScale float4(1.0f, 1.0f, 1.0f, 1.0f)";
// 					File.WriteAllLines(filePath_hMainHSLS, allLines_hMainHSLS);
// 				}
// 			}

			AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);	
#endif
		}
	}
}
#endif
