//pipelinedefine
#define H_URP

#if UNITY_EDITOR
using HTraceAO.Scripts.Infrastructure.URP;
using UnityEditor;

namespace HTraceAO.Scripts.Editor
{
	[CustomEditor(typeof(HTraceAORendererFeature), true)]
	public class HTraceAORendererFeatureEditorURP : UnityEditor.Editor
	{
		private void OnEnable()
		{
		}
		
		public override void OnInspectorGUI()
		{
			serializedObject.Update();

			serializedObject.ApplyModifiedProperties();
		}
	}
}
#endif
