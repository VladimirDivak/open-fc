//pipelinedefine
#define H_URP


using HTraceAO.Scripts.Data.Private;
using HTraceAO.Scripts.Extensions;
using HTraceAO.Scripts.Globals;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.ShortcutManagement;
#endif

namespace HTraceAO.Scripts.Editor.WindowsAndMenu
{

#if UNITY_EDITOR
	
	public class HMenuAndFilesManager : EditorWindow
	{

		[MenuItem("Window/HTrace/Add HTrace AO Render Feature to active RendererData", false, priority: 32)]
		private static void AddRenderFeature()
		{
			HRendererURP.AddHTraceRendererFeatureToUniversalRendererData();
		}

		[MenuItem("Window/HTrace/Open HTrace AO documentation", false, priority: 32)]
		private static void OpenDocumentation()
		{
			Application.OpenURL(HNames.HTRACE_AO_DOCUMENTATION_LINK);
		}
		
		[ClutchShortcut("HTrace/ChangeDebugMode", null, KeyCode.Z, ShortcutModifiers.Shift)]
		private static void ChangeDebugMode( ShortcutArguments args)
		{
			if (args.stage == ShortcutStage.Begin)
			{
				HSettings.SSAOSettings.DebugModeSSAO.NextEnum();
				HSettings.GTAOSettings.DebugMode.NextEnum();
				HSettings.RTAOSettings.DebugMode.NextEnum();
			}
		}
	}
	
#endif
}
