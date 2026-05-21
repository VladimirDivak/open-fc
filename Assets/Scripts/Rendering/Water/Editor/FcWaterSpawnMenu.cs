using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace OpenFarCry.Rendering.Water.Editor
{
    public static class FcWaterSpawnMenu
    {
        const string WaterMaterialPath = "Assets/Shaders/Water/FarCryWater.mat";
        const string WaterShaderName = "FarCry/Water";

        [MenuItem("OpenFarCry/Rendering/Spawn Water Plane Here %&w")]
        public static void SpawnWaterPlane()
        {
            int layer = FcWaterLayerInstaller.EnsureLayer();
            var settings = FcWaterSettingsEditor.LoadOrCreateSettings();
            var material = LoadOrCreateWaterMaterial();

            var go = GameObject.CreatePrimitive(PrimitiveType.Plane);
            go.name = "Water";
            Object.DestroyImmediate(go.GetComponent<Collider>());

            if (layer >= 0)
                go.layer = layer;

            go.transform.position = PickScenePivot();
            go.transform.localScale = new Vector3(50f, 1f, 50f); // 500m × 500m

            var mr = go.GetComponent<MeshRenderer>();
            mr.sharedMaterial = material;

            var surface = go.AddComponent<FcWaterSurface>();
            var so = new SerializedObject(surface);
            so.FindProperty("waterMaterial").objectReferenceValue = material;
            so.ApplyModifiedPropertiesWithoutUndo();

            EditorSceneManager.MarkSceneDirty(go.scene);
            Selection.activeGameObject = go;
            EditorGUIUtility.PingObject(go);

            if (layer < 0)
                Debug.LogWarning("[FcWater] Water layer not installed; reflection self-recursion guard will fall back to surface layer.");
            if (settings == null)
                Debug.LogWarning("[FcWater] FcWaterSettings missing; runtime keywords will not push.");
        }

        static Vector3 PickScenePivot()
        {
            var view = SceneView.lastActiveSceneView;
            if (view == null || view.camera == null)
                return Vector3.zero;
            var pivot = view.pivot;
            pivot.y = Mathf.Round(pivot.y * 10f) / 10f;
            return pivot;
        }

        static Material LoadOrCreateWaterMaterial()
        {
            var existing = AssetDatabase.LoadAssetAtPath<Material>(WaterMaterialPath);
            if (existing != null) return existing;

            var shader = Shader.Find(WaterShaderName);
            if (shader == null)
            {
                Debug.LogWarning($"[FcWater] Shader '{WaterShaderName}' not found; falling back to URP/Lit.");
                shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            }

            var dir = Path.GetDirectoryName(WaterMaterialPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            var mat = new Material(shader) { name = "FarCryWater" };
            AssetDatabase.CreateAsset(mat, WaterMaterialPath);
            AssetDatabase.SaveAssets();
            return mat;
        }
    }
}
