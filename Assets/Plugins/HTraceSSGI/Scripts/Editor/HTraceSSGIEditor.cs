//pipelinedefine
#define H_URP

#if UNITY_EDITOR

using System.IO;
using HTraceSSGI.Scripts.Data.Private;
using HTraceSSGI.Scripts.Data.Public;
using UnityEditor;
using UnityEngine;

using HTraceSSGI.Scripts.Infrastructure.URP;

namespace HTraceSSGI.Scripts.Editor
{
    public enum HTraceSSGIPreset
    {
        Performance = 1,
        Optimized = 2,
        Balanced = 3,
        Quality = 4,
    }
    
    [CustomEditor(typeof(HTraceSSGI))]
    public class HTraceSSGIEditor : UnityEditor.Editor
    {
        private SerializedProperty _profile;
        
        HTraceSSGIProfile _cachedProfile;
        UnityEditor.Editor _cachedProfileEditor;

        static GUIStyle _boxStyle;
        
        private void OnEnable()
        {
            _profile = serializedObject.FindProperty("Profile");
        }

        public override void OnInspectorGUI()
        {
            if (_boxStyle == null) 
            {
                _boxStyle = new GUIStyle(GUI.skin.box);
                _boxStyle.padding = new RectOffset(15, 10, 5, 5);
            }

            if (HTraceSSGIRendererFeature.IsUseVolumes == true)
            {
                EditorGUILayout.HelpBox("\"Use Volumes\" checkbox in the HTrace SSGI Renderer feature is enabled, use the HTraceSSGI volume override in your scenes.", MessageType.Warning, wide: true);
                return;
            }
            
            EditorGUILayout.PropertyField(_profile);
            
            EditorGUILayout.Space(5);
            
            if (_profile.objectReferenceValue != null) 
            {
                if (_cachedProfile != _profile.objectReferenceValue) 
                {
                    _cachedProfile = null;
                }
                if (_cachedProfile == null) 
                {
                    _cachedProfile = (HTraceSSGIProfile)_profile.objectReferenceValue;
                    _cachedProfileEditor = CreateEditor(_profile.objectReferenceValue);
                }

                EditorGUILayout.BeginVertical();
                _cachedProfileEditor.OnInspectorGUI();

                EditorGUILayout.Separator();

                if (GUILayout.Button("Save As New Profile")) 
                {
                    ExportProfile();
                }
                EditorGUILayout.EndVertical();
            }
            else 
            {
                EditorGUILayout.HelpBox("Create or assign a profile.", MessageType.Info);
                if (GUILayout.Button("New Profile")) 
                {
                    CreateProfile();
                }
            }

            serializedObject.ApplyModifiedProperties();
        }
        
        void CreateProfile() {

            var fp = CreateInstance<HTraceSSGIProfile>();
            fp.name = "New HTrace SSGI Profile";

            string path = "Assets";
            foreach (Object obj in Selection.GetFiltered(typeof(Object), SelectionMode.Assets)) {
                path = AssetDatabase.GetAssetPath(obj);
                if (File.Exists(path)) {
                    path = Path.GetDirectoryName(path);
                }
                break;
            }

            string fullPath = path + "/" + fp.name + ".asset";
            fullPath = AssetDatabase.GenerateUniqueAssetPath(fullPath);

            AssetDatabase.CreateAsset(fp, fullPath);
            AssetDatabase.SaveAssets();
            _profile.objectReferenceValue = fp;
            EditorGUIUtility.PingObject(fp);
        }

        void ExportProfile() {
            var fp = (HTraceSSGIProfile)_profile.objectReferenceValue;
            var newProfile = Instantiate(fp);

            string path = AssetDatabase.GetAssetPath(fp);
            string fullPath = path;
            if (string.IsNullOrEmpty(path)) {
                path = "Assets";
                foreach (Object obj in Selection.GetFiltered(typeof(Object), SelectionMode.Assets)) {
                    path = AssetDatabase.GetAssetPath(obj);
                    if (File.Exists(path)) {
                        path = Path.GetDirectoryName(path);
                    }
                    break;
                }
                fullPath = path + "/" + fp.name + ".asset";
            }
            fullPath = AssetDatabase.GenerateUniqueAssetPath(fullPath);
            AssetDatabase.CreateAsset(newProfile, fullPath);
            AssetDatabase.SaveAssets();
            _profile.objectReferenceValue = newProfile;
            EditorGUIUtility.PingObject(fp);
        }
    }
}
#endif
