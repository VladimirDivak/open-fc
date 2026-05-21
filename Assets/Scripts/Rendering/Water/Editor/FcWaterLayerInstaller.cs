using UnityEditor;
using UnityEngine;

namespace OpenFarCry.Rendering.Water.Editor
{
    public static class FcWaterLayerInstaller
    {
        public const string LayerName = "Water";

        public static int EnsureLayer()
        {
            int existing = LayerMask.NameToLayer(LayerName);
            if (existing >= 0) return existing;

            var tagManagerAsset = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset");
            if (tagManagerAsset == null || tagManagerAsset.Length == 0)
            {
                Debug.LogWarning("[FcWater] Could not open ProjectSettings/TagManager.asset to install Water layer.");
                return -1;
            }

            var tagManager = new SerializedObject(tagManagerAsset[0]);
            var layersProp = tagManager.FindProperty("layers");
            if (layersProp == null || !layersProp.isArray)
            {
                Debug.LogWarning("[FcWater] TagManager 'layers' array not found; cannot install Water layer.");
                return -1;
            }

            for (int i = 8; i < layersProp.arraySize; i++)
            {
                var slot = layersProp.GetArrayElementAtIndex(i);
                if (slot != null && string.IsNullOrEmpty(slot.stringValue))
                {
                    slot.stringValue = LayerName;
                    tagManager.ApplyModifiedPropertiesWithoutUndo();
                    AssetDatabase.SaveAssets();
                    Debug.Log($"[FcWater] Installed layer '{LayerName}' at slot {i}.");
                    return i;
                }
            }

            Debug.LogWarning("[FcWater] No free user layer slot to install Water layer. Add it manually in Tags & Layers.");
            return -1;
        }
    }
}
