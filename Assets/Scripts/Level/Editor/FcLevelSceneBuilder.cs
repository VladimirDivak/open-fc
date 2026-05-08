using System.Collections.Generic;
using OpenFarCry.Level.Data;
using OpenFarCry.Level.Entities;
using OpenFarCry.Level.Registry;
using OpenFarCry.Level.Services;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace OpenFarCry.Level.Editor
{
    public static class FcLevelSceneBuilder
    {
        // ── Build ────────────────────────────────────────────────────────────────

        public static BuildStats BuildScene(
            string levelName,
            string missionName,
            FcEntityPrefabRegistry registry,
            Scene targetScene,
            bool skipHidden = true,
            bool buildBrushes = true)
        {
            var mission = FcLevelLoader.LoadMission(levelName, missionName);
            if (mission == null)
            {
                Debug.LogError($"[FcLevelSceneBuilder] Failed to load mission '{missionName}' for level '{levelName}'.");
                return default;
            }

            // Root GO
            var levelRoot = new GameObject($"Level_{levelName}");
            SceneManager.MoveGameObjectToScene(levelRoot, targetScene);

            // Services
            InstantiateServices(levelRoot, levelName, mission.Environment);

            // Entity container
            var entityRoot = new GameObject("Entities");
            entityRoot.transform.SetParent(levelRoot.transform, worldPositionStays: false);

            // Objects container
            var objectRoot = new GameObject("Objects");
            objectRoot.transform.SetParent(levelRoot.transform, worldPositionStays: false);

            var stats = new BuildStats();
            var entityById = new Dictionary<int, Transform>();

            // ── Pass 1: entities ────────────────────────────────────────────────
            foreach (var desc in mission.Entities)
            {
                if (skipHidden && desc.HiddenInGame) { stats.Skipped++; continue; }

                var prefab = registry.GetPrefabForClass(desc.EntityClass);
                if (prefab == null)
                {
                    Debug.LogWarning($"[FcLevelSceneBuilder] No prefab for EntityClass='{desc.EntityClass}' (id={desc.Id})");
                    stats.Unknown++;
                    continue;
                }

                var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                go.name = string.IsNullOrEmpty(desc.Name) ? $"{desc.EntityClass}_{desc.Id}" : desc.Name;
                go.transform.SetParent(entityRoot.transform, worldPositionStays: false);

                SetTransform(go.transform, desc.Pos, desc.Angles, desc.Scale);

                var entity = go.GetComponent<FcEntity>();
                entity?.SetData(desc);

                if (desc.Id > 0) entityById[desc.Id] = go.transform;
                stats.Entities++;
            }

            // ── Pass 2: objects ─────────────────────────────────────────────────
            foreach (var desc in mission.Objects)
            {
                var prefab = registry.GetPrefabForObjectType(desc.Type);
                if (prefab == null) { stats.Unknown++; continue; }

                var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                go.name = string.IsNullOrEmpty(desc.Name) ? $"{desc.Type}_obj" : desc.Name;
                go.transform.SetParent(objectRoot.transform, worldPositionStays: false);

                SetTransform(go.transform, desc.Pos, desc.Angles, 1f);

                var entity = go.GetComponent<FcEntity>();
                entity?.SetData(desc);

                stats.Objects++;
            }

            // ── Pass 3: parent remap ────────────────────────────────────────────
            foreach (var desc in mission.Entities)
            {
                if (desc.ParentId <= 0) continue;
                if (!entityById.TryGetValue(desc.Id, out var child)) continue;
                if (!entityById.TryGetValue(desc.ParentId, out var parent)) continue;
                child.SetParent(parent, worldPositionStays: true);
            }

            // ── Pass 4: brush geometry ──────────────────────────────────────────
            if (buildBrushes)
                stats.Brushes = BuildBrushes(levelName, levelRoot);

            EditorSceneManager.MarkSceneDirty(targetScene);
            return stats;
        }

        // ── Brushes ──────────────────────────────────────────────────────────────

        static int BuildBrushes(string levelName, GameObject levelRoot)
        {
            var brushes = FcBrushLoader.LoadBrushes(levelName);
            if (brushes.Count == 0) return 0;

            var brushRoot = new GameObject("Brushes");
            brushRoot.transform.SetParent(levelRoot.transform, worldPositionStays: false);

            foreach (var desc in brushes)
            {
                var go = new GameObject($"Brush_{desc.Id}");
                go.transform.SetParent(brushRoot.transform, worldPositionStays: false);
                ApplyCryMatrix34(go.transform, desc.Matrix);

                var bi = go.AddComponent<FcBrushInstance>();
                var so = new SerializedObject(bi);
                so.FindProperty("_virtualPath").stringValue = desc.VirtualPath;
                so.FindProperty("_noPhysics").boolValue     = desc.NoPhysics;
                so.ApplyModifiedPropertiesWithoutUndo();
            }

            return brushes.Count;
        }

        // CryEngine Matrix34 (row-major, Z-up right-handed) → Unity Transform (Y-up left-handed).
        // Coordinate map M: Cry(x,y,z) → Unity(x,z,−y).
        // Columns: colX=(m00,m20,−m10), colY=(m02,m22,−m12), colZ=(−m01,−m21,m11).
        // Translation: pos=(m03, m23, −m13).
        static void ApplyCryMatrix34(Transform t, float[] m)
        {
            float m00=m[0],  m01=m[1],  m02=m[2],  m03=m[3];
            float m10=m[4],  m11=m[5],  m12=m[6],  m13=m[7];
            float m20=m[8],  m21=m[9],  m22=m[10], m23=m[11];

            t.position = new Vector3(m03, m23, -m13);

            var colX = new Vector3( m00,  m20, -m10);
            var colY = new Vector3( m02,  m22, -m12);
            var colZ = new Vector3(-m01, -m21,  m11);

            float sX = colX.magnitude;
            float sY = colY.magnitude;
            float sZ = colZ.magnitude;

            if (sZ > 1e-5f && sY > 1e-5f)
                t.rotation = Quaternion.LookRotation(colZ / sZ, colY / sY);

            t.localScale = new Vector3(
                sX > 1e-5f ? sX : 1f,
                sY > 1e-5f ? sY : 1f,
                sZ > 1e-5f ? sZ : 1f);
        }

        // ── Clear ────────────────────────────────────────────────────────────────

        public static void ClearScene(Scene targetScene)
        {
            var roots = targetScene.GetRootGameObjects();
            foreach (var go in roots)
            {
                if (go.name.StartsWith("Level_"))
                    Object.DestroyImmediate(go);
            }
            EditorSceneManager.MarkSceneDirty(targetScene);
        }

        // ── Helpers ──────────────────────────────────────────────────────────────

        static void InstantiateServices(GameObject parent, string levelName, FcLevelEnvironmentDesc env)
        {
            var servicesGo = new GameObject("FcLevelServices");
            servicesGo.transform.SetParent(parent.transform, worldPositionStays: false);

            var cache = servicesGo.AddComponent<FcLevelCacheService>();
            cache.SetLevelScope(levelName);
            servicesGo.AddComponent<FcLevelResourceService>();

            var environment = servicesGo.AddComponent<FcLevelEnvironment>();
            if (env != null)
            {
                environment.SunColor = env.SunColor;
                environment.SunMultiplier = env.SunMultiplier;
                environment.AmbientColor = env.AmbientColor;
                environment.FogColor = env.FogColor;
                environment.FogStart = env.FogStart;
                environment.FogEnd = env.FogEnd;

                if (env.SunVector != Vector3.zero)
                    environment.SunDirection = env.SunVector;
            }
        }

        static void SetTransform(Transform t, Vector3 pos, Vector3 cryAngles, float scale)
        {
            t.position = pos;
            t.rotation = CryAnglesToUnity(cryAngles);
            t.localScale = Vector3.one * scale;
        }

        // Cry XYZ Euler (degrees, Z-up right-handed) → Unity Quaternion (Y-up left-handed).
        // Keep this consistent with historic level authoring assumptions for mission XML angles.
        static Quaternion CryAnglesToUnity(Vector3 cryAngles)
        {
            return Quaternion.AngleAxis(cryAngles.x, Vector3.right)
                 * Quaternion.AngleAxis(-cryAngles.y, Vector3.forward)
                 * Quaternion.AngleAxis(cryAngles.z, Vector3.up);
        }

        public struct BuildStats
        {
            public int Entities;
            public int Objects;
            public int Brushes;
            public int Skipped;
            public int Unknown;

            public override string ToString() =>
                $"Entities: {Entities}  Objects: {Objects}  Brushes: {Brushes}  Skipped: {Skipped}  Unknown: {Unknown}";
        }
    }
}
