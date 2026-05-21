using System;
using OpenFarCry.Level.Entities;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace OpenFarCry.Level.Editor
{
    // Editor helpers for the APV-only lighting workflow. The actual bake (Window >
    // Rendering > Lighting > Generate Lighting, or AdaptiveProbeVolumes.BakeAsync) is
    // triggered manually by the user — this class only prepares the scene for it and
    // strips the temporary geometry once the user has confirmed the bake is done.
    //
    // Expected caller order:
    //   1. FcLevelSceneBuilder.BuildScene(... Runtime ...)
    //   2. FcLevelSceneBuilder.PlaceVegetationAuthoringWrappersInExistingScene(...)
    //   3. FcLevelBuilderWindow.CacheSceneGeometryToProject(... attach=true, disable=false ...)
    //   4. PrepareSceneForApvBake(scene, levelName, useGpuLightmapper) — this file
    //   5. User triggers APV bake in the Unity editor
    //   6. StripCachedGeometry(scene) — this file
    //
    // After step 6 the scene is commit-safe: only terrain, services, placeholders,
    // ProbeVolume, and the BakingSet asset remain. URP samples APV per-pixel from
    // world position, so runtime-spawned brushes/vegetation are lit automatically.
    internal static class FcLevelApvBakePipeline
    {
        const string TempVegetationRootName = "Vegetation_AuthoringForBake";
        const string ProbeVolumeGoName = "ProbeVolume_Level";

        public static bool PrepareSceneForApvBake(
            Scene scene,
            string levelName,
            bool useGpuLightmapper)
        {
            if (!scene.IsValid())
            {
                Debug.LogError("[FcLevelApvBakePipeline] Scene is invalid.");
                return false;
            }

            GameObject levelRoot = FindLevelRoot(scene, levelName);
            if (levelRoot == null)
            {
                Debug.LogError($"[FcLevelApvBakePipeline] Level root 'Level_{levelName}' not found in scene.");
                return false;
            }

            ConfigureSunForMixedBake(scene);
            ConfigureLightingSettings(useGpuLightmapper);
            SetCachedRenderersReceiveGiLightProbes(scene);

            Bounds bbox = ComputeLevelBounds(scene, levelRoot);
            PlaceProbeVolume(levelRoot, bbox);

            ProbeVolumeBakingSet bakingSet = EnsureBakingSet(levelName, scene);
            if (bakingSet == null)
            {
                Debug.LogError("[FcLevelApvBakePipeline] Failed to create or load ProbeVolumeBakingSet.");
                return false;
            }

            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene))
            {
                Debug.LogError("[FcLevelApvBakePipeline] Failed to save scene after preparation.");
                return false;
            }

            Debug.Log($"[FcLevelApvBakePipeline] Scene '{levelName}' prepared for APV bake. " +
                      $"ProbeVolume bounds size={bbox.size}, center={bbox.center}. " +
                      "Trigger the bake from Window > Rendering > Lighting > Generate Lighting, " +
                      "then run the strip step to drop cached geometry.");
            return true;
        }

        public static int StripCachedGeometry(Scene scene)
        {
            if (!scene.IsValid())
            {
                Debug.LogError("[FcLevelApvBakePipeline] Scene is invalid.");
                return 0;
            }

            int removed = StripCachedChildren(scene);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log($"[FcLevelApvBakePipeline] Stripped {removed} cached geometry root(s); scene saved.");
            return removed;
        }

        static GameObject FindLevelRoot(Scene scene, string levelName)
        {
            string expected = $"Level_{levelName}";
            var roots = scene.GetRootGameObjects();
            for (int i = 0; i < roots.Length; i++)
            {
                if (roots[i] != null && roots[i].name == expected)
                    return roots[i];
            }
            return null;
        }

        static void ConfigureSunForMixedBake(Scene scene)
        {
            var roots = scene.GetRootGameObjects();
            for (int i = 0; i < roots.Length; i++)
            {
                if (roots[i] == null)
                    continue;

                var lights = roots[i].GetComponentsInChildren<Light>(includeInactive: true);
                for (int li = 0; li < lights.Length; li++)
                {
                    var l = lights[li];
                    if (l == null || l.type != LightType.Directional)
                        continue;

                    l.lightmapBakeType = LightmapBakeType.Mixed;
                    EditorUtility.SetDirty(l);
                }
            }
        }

        static void ConfigureLightingSettings(bool useGpuLightmapper)
        {
            LightingSettings settings;
            try
            {
                settings = Lightmapping.lightingSettings;
            }
            catch
            {
                settings = null;
            }

            if (settings == null)
            {
                settings = new LightingSettings { name = "FcLevelApvSettings" };
                Lightmapping.lightingSettings = settings;
            }

            settings.mixedBakeMode = MixedLightingMode.IndirectOnly;
            settings.lightmapper = useGpuLightmapper
                ? LightingSettings.Lightmapper.ProgressiveGPU
                : LightingSettings.Lightmapper.ProgressiveCPU;

            EditorUtility.SetDirty(settings);
        }

        static void SetCachedRenderersReceiveGiLightProbes(Scene scene)
        {
            var roots = scene.GetRootGameObjects();
            for (int i = 0; i < roots.Length; i++)
            {
                if (roots[i] == null)
                    continue;

                var renderers = roots[i].GetComponentsInChildren<MeshRenderer>(includeInactive: true);
                for (int r = 0; r < renderers.Length; r++)
                {
                    var mr = renderers[r];
                    if (mr == null)
                        continue;

                    mr.receiveGI = ReceiveGI.LightProbes;
                    EditorUtility.SetDirty(mr);
                }
            }
        }

        static Bounds ComputeLevelBounds(Scene scene, GameObject levelRoot)
        {
            bool hasBounds = false;
            Bounds bounds = new Bounds(Vector3.zero, Vector3.zero);

            var terrains = levelRoot.GetComponentsInChildren<Terrain>(includeInactive: true);
            for (int i = 0; i < terrains.Length; i++)
            {
                var t = terrains[i];
                if (t == null || t.terrainData == null)
                    continue;

                Vector3 origin = t.transform.position;
                Vector3 size = t.terrainData.size;
                var tb = new Bounds(origin + size * 0.5f, size);
                if (!hasBounds) { bounds = tb; hasBounds = true; }
                else bounds.Encapsulate(tb);
            }

            var renderers = levelRoot.GetComponentsInChildren<MeshRenderer>(includeInactive: true);
            for (int i = 0; i < renderers.Length; i++)
            {
                var mr = renderers[i];
                if (mr == null || !mr.enabled)
                    continue;

                if (!hasBounds) { bounds = mr.bounds; hasBounds = true; }
                else bounds.Encapsulate(mr.bounds);
            }

            if (!hasBounds)
            {
                bounds = new Bounds(levelRoot.transform.position, new Vector3(512f, 256f, 512f));
            }

            // Vertical padding so brushes above terrain and indoor ceilings stay inside the volume.
            Vector3 padded = bounds.size + new Vector3(0f, 20f, 0f);
            bounds = new Bounds(bounds.center, padded);
            return bounds;
        }

        static ProbeVolume PlaceProbeVolume(GameObject levelRoot, Bounds bbox)
        {
            Transform existing = levelRoot.transform.Find(ProbeVolumeGoName);
            GameObject go = existing != null ? existing.gameObject : null;
            if (go == null)
            {
                go = new GameObject(ProbeVolumeGoName);
                go.transform.SetParent(levelRoot.transform, worldPositionStays: false);
            }

            go.transform.position = bbox.center;
            go.transform.rotation = Quaternion.identity;
            go.transform.localScale = Vector3.one;

            var pv = go.GetComponent<ProbeVolume>();
            if (pv == null)
                pv = go.AddComponent<ProbeVolume>();

            pv.mode = ProbeVolume.Mode.Local;
            pv.size = bbox.size;
            EditorUtility.SetDirty(pv);
            return pv;
        }

        static ProbeVolumeBakingSet EnsureBakingSet(string levelName, Scene scene)
        {
            string levelDir = FcLevelBuilderWindow.GetLevelAssetCacheDir(levelName);
            string lightingDir = $"{levelDir}/Lighting";
            FcLevelBuilderWindow.EnsureDir(lightingDir);

            string assetPath = $"{lightingDir}/{levelName}_APV_BakingSet.asset";
            var bakingSet = AssetDatabase.LoadAssetAtPath<ProbeVolumeBakingSet>(assetPath);
            if (bakingSet == null)
            {
                bakingSet = ScriptableObject.CreateInstance<ProbeVolumeBakingSet>();
                bakingSet.name = $"{levelName}_APV_BakingSet";
                AssetDatabase.CreateAsset(bakingSet, assetPath);
                AssetDatabase.SaveAssets();
            }

            string sceneGuid = AssetDatabase.AssetPathToGUID(scene.path);
            if (string.IsNullOrEmpty(sceneGuid))
            {
                Debug.LogError("[FcLevelApvBakePipeline] Scene has no asset GUID; save the scene before baking.");
                return bakingSet;
            }

            bakingSet.TryAddScene(sceneGuid);
            EditorUtility.SetDirty(bakingSet);
            return bakingSet;
        }

        static int StripCachedChildren(Scene scene)
        {
            int removed = 0;

            // Drop temporary vegetation root entirely.
            var roots = scene.GetRootGameObjects();
            for (int i = 0; i < roots.Length; i++)
            {
                if (roots[i] == null)
                    continue;

                var tempVeg = FindDescendantByName(roots[i].transform, TempVegetationRootName);
                if (tempVeg != null)
                {
                    Object.DestroyImmediate(tempVeg.gameObject);
                    removed++;
                }
            }

            // Drop __FCDataCached children under every brush / vegetation wrapper.
            var brushes = FcLevelBuilderWindow.CollectComponentsInScene<FcBrushInstance>(scene);
            for (int i = 0; i < brushes.Count; i++)
            {
                if (brushes[i] == null) continue;
                int before = brushes[i].transform.childCount;
                FcLevelBuilderWindow.RemoveExistingCachedChildren(brushes[i].transform);
                removed += before - brushes[i].transform.childCount;
            }

            var veg = FcLevelBuilderWindow.CollectComponentsInScene<FcVegetationInstance>(scene);
            for (int i = 0; i < veg.Count; i++)
            {
                if (veg[i] == null) continue;
                int before = veg[i].transform.childCount;
                FcLevelBuilderWindow.RemoveExistingCachedChildren(veg[i].transform);
                removed += before - veg[i].transform.childCount;
            }

            return removed;
        }

        static Transform FindDescendantByName(Transform root, string name)
        {
            if (root == null)
                return null;
            if (string.Equals(root.name, name, StringComparison.Ordinal))
                return root;
            for (int i = 0; i < root.childCount; i++)
            {
                var hit = FindDescendantByName(root.GetChild(i), name);
                if (hit != null)
                    return hit;
            }
            return null;
        }
    }
}
