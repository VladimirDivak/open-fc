using System;
using OpenFarCry.Level.Entities;
using OpenFarCry.Level.Registry;
using UnityEditor;
using UnityEngine;

namespace OpenFarCry.Level.Editor
{
    public static class FcEntityPrefabFactory
    {
        const string PrefabDir    = "Assets/Prefabs/Level/Entities";
        const string RegistryPath = "Assets/Resources/FcEntityPrefabRegistry.asset";

        [MenuItem("OpenFarCry/Level/Create Entity Prefabs")]
        static void CreateAllPrefabs()
        {
            var registry = AssetDatabase.LoadAssetAtPath<FcEntityPrefabRegistry>(RegistryPath);
            if (registry == null)
            {
                Debug.LogError($"[FcEntityPrefabFactory] Registry not found at '{RegistryPath}'.");
                return;
            }

            EnsureDir(PrefabDir);

            var so = new SerializedObject(registry);

            AssetDatabase.StartAssetEditing();
            try
            {
                // Existing types — wire up if missing but do not recreate
                WireExisting(so, "FcMeshEntity",      "_meshEntityPrefab");
                WireExisting(so, "FcRigidBodyEntity", "_rigidBodyEntityPrefab");
                WireExisting(so, "FcCharacterEntity", "_characterEntityPrefab");
                WireExisting(so, "FcLightEntity",     "_lightEntityPrefab");
                WireExisting(so, "FcSoundEntity",     "_soundEntityPrefab");
                WireExisting(so, "FcTriggerEntity",   "_triggerEntityPrefab");
                WireExisting(so, "FcSpawnPoint",      "_spawnPointPrefab");
                WireExisting(so, "FcTagPoint",        "_tagPointPrefab");

                // New types — create if missing, then wire
                CreateAndWire(so, "FcVehicleEntity",     typeof(FcVehicleEntity),     "_vehicleEntityPrefab");
                CreateAndWire(so, "FcDoorEntity",        typeof(FcDoorEntity),        "_doorEntityPrefab");
                CreateAndWire(so, "FcPickupEntity",      typeof(FcPickupEntity),      "_pickupEntityPrefab",
                    typeof(SphereCollider));
                CreateAndWire(so, "FcMineEntity",        typeof(FcMineEntity),        "_mineEntityPrefab",
                    typeof(SphereCollider));
                CreateAndWire(so, "FcParticleEntity",    typeof(FcParticleEntity),    "_particleEntityPrefab",
                    typeof(ParticleSystem));
                CreateAndWire(so, "FcEnvironmentEntity", typeof(FcEnvironmentEntity), "_environmentEntityPrefab");
                CreateAndWire(so, "FcBoidEntity",        typeof(FcBoidEntity),        "_boidEntityPrefab");
                CreateAndWire(so, "FcCameraEntity",      typeof(FcCameraEntity),      "_cameraEntityPrefab",
                    typeof(Camera));
                CreateAndWire(so, "FcDestructibleEntity",typeof(FcDestructibleEntity),"_destructibleEntityPrefab");
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
            }

            so.ApplyModifiedProperties();
            EditorUtility.SetDirty(registry);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log("[FcEntityPrefabFactory] Done. Check FcEntityPrefabRegistry for assignments.");
        }

        // ── Helpers ─────────────────────────────────────────────────────────────

        static void CreateAndWire(SerializedObject registrySO, string prefabName, Type scriptType,
            string fieldName, params Type[] extraComponents)
        {
            string path = $"{PrefabDir}/{prefabName}.prefab";

            if (AssetDatabase.LoadAssetAtPath<GameObject>(path) == null)
            {
                var go = new GameObject(prefabName);

                foreach (var t in extraComponents)
                    go.AddComponent(t);

                go.AddComponent(scriptType);
                PostConfigure(go);

                PrefabUtility.SaveAsPrefabAsset(go, path);
                UnityEngine.Object.DestroyImmediate(go);
                Debug.Log($"[FcEntityPrefabFactory] Created: {path}");
            }
            else
            {
                Debug.Log($"[FcEntityPrefabFactory] Skipped (exists): {path}");
            }

            WireToRegistry(registrySO, fieldName, path);
        }

        // Only wires the registry field if the prefab already exists on disk.
        static void WireExisting(SerializedObject registrySO, string prefabName, string fieldName)
        {
            string path = $"{PrefabDir}/{prefabName}.prefab";
            WireToRegistry(registrySO, fieldName, path);
        }

        static void PostConfigure(GameObject go)
        {
            if (go.TryGetComponent<SphereCollider>(out var sc))
            {
                sc.isTrigger = true;
                sc.radius    = 0.5f;
            }

            if (go.TryGetComponent<Camera>(out var cam))
                cam.enabled = false;

            if (go.TryGetComponent<ParticleSystem>(out var ps))
                ps.Stop(withChildren: true, stopBehavior: ParticleSystemStopBehavior.StopEmittingAndClear);
        }

        static void WireToRegistry(SerializedObject so, string fieldName, string prefabPath)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (prefab == null) return;

            var prop = so.FindProperty(fieldName);
            if (prop == null)
            {
                Debug.LogWarning($"[FcEntityPrefabFactory] Field '{fieldName}' not found in registry.");
                return;
            }

            prop.objectReferenceValue = prefab;
        }

        static void EnsureDir(string dir)
        {
            if (!AssetDatabase.IsValidFolder(dir))
            {
                string parent = System.IO.Path.GetDirectoryName(dir)?.Replace('\\', '/');
                string leaf   = System.IO.Path.GetFileName(dir);
                AssetDatabase.CreateFolder(parent, leaf);
            }
        }
    }
}
