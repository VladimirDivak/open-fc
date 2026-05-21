using System.IO;
using UnityEditor;
using UnityEngine;

namespace OpenFarCry.Rendering.Water.Editor
{
    [CustomEditor(typeof(FcWaterSettings))]
    public class FcWaterSettingsEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("Runtime", EditorStyles.boldLabel);
            var settings = (FcWaterSettings)target;
            var tier = FcWaterQualityTierResolver.Resolve(settings);
            EditorGUILayout.LabelField("Resolved Tier", tier.ToString());
            EditorGUILayout.LabelField("Uses Planar", FcWaterQualityTierResolver.UsesPlanar(tier).ToString());
            EditorGUILayout.LabelField("Uses Refraction", FcWaterQualityTierResolver.UsesRefraction(tier).ToString());
            EditorGUILayout.LabelField("Uses Probe Fallback", FcWaterQualityTierResolver.UsesProbeFallback(tier).ToString());

            if (GUILayout.Button("Re-apply Globals (push keywords)"))
                FcWaterRuntimeBootstrap.Apply();
        }

        [MenuItem("OpenFarCry/Rendering/Water Settings")]
        public static void OpenSettings()
        {
            var asset = LoadOrCreateSettings();
            Selection.activeObject = asset;
            EditorGUIUtility.PingObject(asset);
        }

        public static FcWaterSettings LoadOrCreateSettings()
        {
            const string dir = "Assets/Resources";
            const string path = "Assets/Resources/FcWaterSettings.asset";

            var existing = AssetDatabase.LoadAssetAtPath<FcWaterSettings>(path);
            if (existing != null) return existing;

            if (!Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            var instance = ScriptableObject.CreateInstance<FcWaterSettings>();
            AssetDatabase.CreateAsset(instance, path);
            AssetDatabase.SaveAssets();
            return instance;
        }
    }
}
