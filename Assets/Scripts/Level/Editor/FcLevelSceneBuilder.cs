using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using OpenFarCry.Level.Data;
using OpenFarCry.Level.Entities;
using OpenFarCry.Level.Registry;
using OpenFarCry.Level.Services;
using OpenFarCry.FileSystem;
using OpenFarCry.Importer.Texture;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Debug = UnityEngine.Debug;
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
            var sw = new Stopwatch();
            var report = new FcLevelLoadReport { LevelName = levelName, MissionName = missionName };

            sw.Restart();
            var mission = FcLevelLoader.LoadMission(levelName, missionName);
            report.RecordPhase("LoadMissionXml", sw.Elapsed.TotalMilliseconds);

            if (mission == null)
            {
                Debug.LogError($"[FcLevelSceneBuilder] Failed to load mission '{missionName}' for level '{levelName}'.");
                return default;
            }

            // Load brushes once; used both for layout save and scene build.
            sw.Restart();
            IReadOnlyList<FcBrushDesc> brushList = buildBrushes
                ? FcBrushLoader.LoadBrushes(levelName)
                : System.Array.Empty<FcBrushDesc>();
            report.RecordPhase("LoadBrushList", sw.Elapsed.TotalMilliseconds);

            sw.Restart();
            var supplement = FcLevelSupplementLoader.Load(levelName);
            SaveLayoutData(levelName, missionName, mission, brushList, supplement);
            report.RecordPhase("SaveLayoutData", sw.Elapsed.TotalMilliseconds);

            // Root GO
            var levelRoot = new GameObject($"Level_{levelName}");
            SceneManager.MoveGameObjectToScene(levelRoot, targetScene);

            // Services
            InstantiateServices(levelRoot, levelName, mission.Environment);
            FcLevelLoader.TryLoadTerrainSettings(levelName, out int terrainRes, out int terrainUnit);
            var levelTerrain = BuildUnityTerrain(levelRoot, levelName, mission.Environment, terrainRes, terrainUnit);

            // Entity + object containers
            var entityRoot = new GameObject("Entities");
            entityRoot.transform.SetParent(levelRoot.transform, worldPositionStays: false);
            var objectRoot = new GameObject("Objects");
            objectRoot.transform.SetParent(levelRoot.transform, worldPositionStays: false);

            sw.Restart();
            var stats = BuildMission(mission, registry, entityRoot, objectRoot, skipHidden);
            report.RecordPhase("BuildEntities", sw.Elapsed.TotalMilliseconds);

            // Vegetation
            sw.Restart();
            stats.Vegetation = BuildVegetationInstances(supplement, levelName, terrainRes, terrainUnit, levelRoot, levelTerrain);
            report.RecordPhase("BuildVegetation", sw.Elapsed.TotalMilliseconds);

            // ── Brush geometry ──────────────────────────────────────────────────
            if (buildBrushes)
            {
                sw.Restart();
                stats.Brushes = BuildBrushesFromList(brushList, levelRoot);
                report.RecordPhase("BuildBrushPlaceholders", sw.Elapsed.TotalMilliseconds);
            }

            report.LogEditorBuild();
            EditorSceneManager.MarkSceneDirty(targetScene);
            return stats;
        }

        // V2 adapter: keeps current scene build path while layout format evolves.
        public static BuildStats RebuildFromLayoutData(
            FcLevelLayoutDataV2 layoutData,
            FcEntityPrefabRegistry registry,
            Scene targetScene,
            bool skipHidden = true)
        {
            if (layoutData == null)
            {
                Debug.LogError("[FcLevelSceneBuilder] layoutData V2 is null.");
                return default;
            }

            var mission = layoutData.ToMission();
            var brushList = layoutData.ToBrushList();

            var levelRoot = new GameObject($"Level_{layoutData.LevelName}");
            SceneManager.MoveGameObjectToScene(levelRoot, targetScene);

            InstantiateServices(levelRoot, layoutData.LevelName, mission.Environment);
            FcLevelLoader.TryLoadTerrainSettings(layoutData.LevelName, out int terrainRes, out int terrainUnit);
            var levelTerrain = BuildUnityTerrain(levelRoot, layoutData.LevelName, mission.Environment, terrainRes, terrainUnit);

            var entityRoot = new GameObject("Entities");
            entityRoot.transform.SetParent(levelRoot.transform, worldPositionStays: false);
            var objectRoot = new GameObject("Objects");
            objectRoot.transform.SetParent(levelRoot.transform, worldPositionStays: false);

            var stats = BuildMission(mission, registry, entityRoot, objectRoot, skipHidden);

            stats.Vegetation = BuildVegetationInstancesFromLayout(layoutData, terrainRes, terrainUnit, levelRoot, levelTerrain);

            stats.Brushes = BuildBrushesFromList(brushList, levelRoot);

            EditorSceneManager.MarkSceneDirty(targetScene);
            return stats;
        }

        // ── Brushes ──────────────────────────────────────────────────────────────

        static int BuildBrushesFromList(IReadOnlyList<FcBrushDesc> brushes, GameObject levelRoot)
        {
            if (brushes == null || brushes.Count == 0) return 0;

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

        // ── Vegetation ───────────────────────────────────────────────────────────

        static int BuildVegetationInstances(
            FcLevelSupplementData supplement,
            string levelName,
            int terrainResolution,
            int heightmapUnitSize,
            GameObject levelRoot,
            Terrain terrain)
        {
            if (supplement == null ||
                supplement.VegetationInstances == null || supplement.VegetationInstances.Length == 0 ||
                supplement.VegetationTypes == null || supplement.VegetationTypes.Length == 0)
                return 0;

            var typeByIndex = new Dictionary<int, FcLevelSupplementData.VegetationTypeDesc>();
            foreach (var t in supplement.VegetationTypes)
            {
                if (t.Index >= 0 && !string.IsNullOrEmpty(t.FileName))
                    typeByIndex[t.Index] = t;
            }

            return PlaceVegetationAsTreeInstances(supplement.VegetationInstances, typeByIndex, terrain);
        }

        static int BuildVegetationInstancesFromLayout(
            FcLevelLayoutDataV2 layoutData,
            int terrainResolution,
            int heightmapUnitSize,
            GameObject levelRoot,
            Terrain terrain)
        {
            if (layoutData.VegetationInstances == null || layoutData.VegetationInstances.Length == 0 ||
                layoutData.VegetationTypes == null || layoutData.VegetationTypes.Length == 0)
                return 0;

            var typeByIndex = new Dictionary<int, FcLevelSupplementData.VegetationTypeDesc>();
            foreach (var t in layoutData.VegetationTypes)
            {
                if (t.Index >= 0 && !string.IsNullOrEmpty(t.FileName))
                    typeByIndex[t.Index] = t;
            }

            return PlaceVegetationAsTreeInstances(layoutData.VegetationInstances, typeByIndex, terrain);
        }

        // Attaches FcVegetationTerrainService to the terrain GO with serialized VirtualPaths + instance data.
        // No geometry is written to disk — runtime component loads CGF from VFS at Play mode Start.
        static int PlaceVegetationAsTreeInstances(
            FcLevelSupplementData.VegetationInstanceDesc[] instances,
            Dictionary<int, FcLevelSupplementData.VegetationTypeDesc> typeByIndex,
            Terrain terrain)
        {
            if (terrain == null) return 0;
            if (instances == null || instances.Length == 0) return 0;

            var usedTypes = new HashSet<int>();
            foreach (var inst in instances)
                usedTypes.Add(inst.Type);

            // Collect VirtualPath per used vegetation type — no import, no disk write.
            var typeEntries = new List<FcVegetationTerrainService.VegetationTypeEntry>();
            foreach (var kv in typeByIndex)
            {
                if (!usedTypes.Contains(kv.Key)) continue;
                var typeDef = kv.Value;
                if (string.IsNullOrEmpty(typeDef.FileName)) continue;

                typeEntries.Add(new FcVegetationTerrainService.VegetationTypeEntry
                {
                    TypeIndex   = kv.Key,
                    VirtualPath = typeDef.FileName,
                });
            }

            // Pre-normalize instance positions (float) to avoid ushort arithmetic at runtime.
            var instanceData = new FcVegetationTerrainService.VegetationInstanceData[instances.Length];
            for (int i = 0; i < instances.Length; i++)
            {
                var inst = instances[i];
                instanceData[i] = new FcVegetationTerrainService.VegetationInstanceData
                {
                    TypeIndex = inst.Type,
                    PosX      = inst.X / 65535f,
                    PosZ      = inst.Y / 65535f,
                    Scale     = inst.Scale > 0f ? inst.Scale : 1f,
                };
            }

            // Attach the runtime service; populate via SerializedObject so data persists in scene.
            var vegService = terrain.gameObject.AddComponent<FcVegetationTerrainService>();
            var so = new SerializedObject(vegService);

            var typesProp = so.FindProperty("_vegetationTypes");
            typesProp.arraySize = typeEntries.Count;
            for (int i = 0; i < typeEntries.Count; i++)
            {
                var elem = typesProp.GetArrayElementAtIndex(i);
                elem.FindPropertyRelative("TypeIndex").intValue    = typeEntries[i].TypeIndex;
                elem.FindPropertyRelative("VirtualPath").stringValue = typeEntries[i].VirtualPath;
            }

            var instProp = so.FindProperty("_instances");
            instProp.arraySize = instanceData.Length;
            for (int i = 0; i < instanceData.Length; i++)
            {
                var elem = instProp.GetArrayElementAtIndex(i);
                elem.FindPropertyRelative("TypeIndex").intValue  = instanceData[i].TypeIndex;
                elem.FindPropertyRelative("PosX").floatValue     = instanceData[i].PosX;
                elem.FindPropertyRelative("PosZ").floatValue     = instanceData[i].PosZ;
                elem.FindPropertyRelative("Scale").floatValue    = instanceData[i].Scale;
            }

            so.ApplyModifiedPropertiesWithoutUndo();

            return instances.Length;
        }

        static Terrain BuildUnityTerrain(
            GameObject levelRoot,
            string levelName,
            FcLevelEnvironmentDesc environment,
            int resolution,
            int heightmapUnitSize)
        {
            string basePath = $"levels/{levelName.ToLowerInvariant()}";
            string h16Path = $"{basePath}/terrain/land_map.h16";
            if (!FcFileSystem.Exists(h16Path))
                return null;

            byte[] bytes = FcFileSystem.ReadAllBytes(h16Path);
            if (!FcTerrainHeightmapDecoder.TryDecodeToUnityHeights(bytes, resolution, out var heights))
            {
                Debug.LogWarning($"[FcLevelSceneBuilder] Could not decode terrain heightmap '{h16Path}'.");
                return null;
            }

            int hmRes = resolution + 1; // e.g. 1025
            float worldSize = (resolution - 1) * heightmapUnitSize;

            var terrainData = new TerrainData();
            terrainData.heightmapResolution = hmRes;
            terrainData.size = new Vector3(worldSize, FcTerrainHeightmapDecoder.MaxWorldHeight, worldSize);
            terrainData.SetHeights(0, 0, heights);

            // Persist TerrainData as Unity asset so scene retains heightmap after reload.
            const string fcDataDir = "Assets/FCData/Levels";
            string levelDir = $"{fcDataDir}/{levelName}";
            EnsureDir(fcDataDir);
            EnsureDir(levelDir);
            string tdPath = $"{levelDir}/TerrainData.asset";
            if (AssetDatabase.LoadAssetAtPath<TerrainData>(tdPath) != null)
                AssetDatabase.DeleteAsset(tdPath);
            AssetDatabase.CreateAsset(terrainData, tdPath);
            AssetDatabase.SaveAssets();
            terrainData = AssetDatabase.LoadAssetAtPath<TerrainData>(tdPath);

            // Apply cover_low.dds as megatexture TerrainLayer.
            string coverPath = $"{basePath}/terrain/cover_low.dds";
            if (TextureImportService.TryLoadRuntimeTexture(coverPath, out var coverTex))
            {
                coverTex.wrapMode = TextureWrapMode.Clamp;
                var layer = new TerrainLayer
                {
                    diffuseTexture = coverTex,
                    tileSize = new Vector2(worldSize, worldSize),
                    tileOffset = Vector2.zero,
                };
                string layerPath = $"{levelDir}/TerrainLayer_Cover.terrainlayer";
                if (AssetDatabase.LoadAssetAtPath<TerrainLayer>(layerPath) != null)
                    AssetDatabase.DeleteAsset(layerPath);
                AssetDatabase.CreateAsset(layer, layerPath);
                AssetDatabase.SaveAssets();
                terrainData.terrainLayers = new[] { AssetDatabase.LoadAssetAtPath<TerrainLayer>(layerPath) };
            }

            var terrainGo = Terrain.CreateTerrainGameObject(terrainData);
            terrainGo.name = "Terrain";
            terrainGo.transform.SetParent(levelRoot.transform, worldPositionStays: false);

            var terrain = terrainGo.GetComponent<Terrain>();

            if (environment != null)
                BuildWaterPlane(terrainGo.transform, resolution, heightmapUnitSize,
                    environment.WaterLevel * terrainData.size.y / FcTerrainHeightmapDecoder.MaxWorldHeight);

            return terrain;
        }

        static void BuildWaterPlane(Transform terrainRoot, int resolution, float metersPerSample, float waterLevel)
        {
            float size = (resolution - 1) * metersPerSample;
            var water = GameObject.CreatePrimitive(PrimitiveType.Plane);
            water.name = "Water";
            water.transform.SetParent(terrainRoot, worldPositionStays: false);
            water.transform.localPosition = new Vector3(size * 0.5f, waterLevel, size * 0.5f);
            water.transform.localScale = new Vector3(size / 10f, 1f, size / 10f);
            Object.DestroyImmediate(water.GetComponent<Collider>());

            var mr = water.GetComponent<MeshRenderer>();
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null)
                shader = Shader.Find("Standard");

            var mat = new Material(shader) { name = "WaterFallback" };
            mat.color = new Color(0.15f, 0.3f, 0.45f, 0.65f);
            mr.sharedMaterial = mat;
        }

        // ── Layout data ───────────────────────────────────────────────────────────

        static void SaveLayoutData(string levelName, string missionName,
            FcMissionDesc mission, IReadOnlyList<FcBrushDesc> brushes,
            FcLevelSupplementData supplement)
        {
            const string dir = "Assets/FCData/Levels";
            EnsureDir(dir);

            string path = $"{dir}/{levelName}_{missionName}_v2.asset";
            var data = FcLevelLayoutDataV2.FromMission(mission, brushes, supplement);

            var existing = AssetDatabase.LoadAssetAtPath<FcLevelLayoutDataV2>(path);
            if (existing != null)
            {
                EditorUtility.CopySerialized(data, existing);
                EditorUtility.SetDirty(existing);
            }
            else
            {
                AssetDatabase.CreateAsset(data, path);
            }
            AssetDatabase.SaveAssets();
        }

        static void EnsureDir(string dir)
        {
            if (AssetDatabase.IsValidFolder(dir)) return;
            string parent = Path.GetDirectoryName(dir)?.Replace('\\', '/');
            string leaf   = Path.GetFileName(dir);
            if (!string.IsNullOrEmpty(parent) && !AssetDatabase.IsValidFolder(parent))
                EnsureDir(parent);
            AssetDatabase.CreateFolder(parent, leaf);
        }

        // CryEngine Matrix34 (row-major, Z-up) -> Unity scene transform.
        // Static CGF vertices are imported as Cry(x,y,z) -> Unity asset(x,z,-y),
        // while level placement uses Cry(x,y,z) -> Unity scene(x,z,y). Therefore
        // instance linear transform is SceneBasis * CryMatrix * Inverse(AssetBasis).
        static void ApplyCryMatrix34(Transform t, float[] m)
        {
            float m00=m[0],  m01=m[1],  m02=m[2],  m03=m[3];
            float m10=m[4],  m11=m[5],  m12=m[6],  m13=m[7];
            float m20=m[8],  m21=m[9],  m22=m[10], m23=m[11];

            t.position = new Vector3(m03, m23, m13);

            var colX = new Vector3( m00,  m20,  m10);
            var colY = new Vector3( m02,  m22,  m12);
            var colZ = new Vector3(-m01, -m21, -m11);

            float sX = colX.magnitude;
            float sY = colY.magnitude;
            float sZ = colZ.magnitude;

            // SceneBasis has opposite handedness from AssetBasis, so the composed
            // instance matrix contains one reflection. Keep it explicit as -Z scale.
            if (sZ > 1e-5f && sY > 1e-5f)
                t.rotation = Quaternion.LookRotation(-colZ / sZ, colY / sY);

            t.localScale = new Vector3(
                sX > 1e-5f ? sX : 1f,
                sY > 1e-5f ? sY : 1f,
                sZ > 1e-5f ? -sZ : -1f);
        }

        // ── Shared entity/object build ────────────────────────────────────────────

        static BuildStats BuildMission(
            FcMissionDesc mission,
            FcEntityPrefabRegistry registry,
            GameObject entityRoot,
            GameObject objectRoot,
            bool skipHidden)
        {
            var stats = new BuildStats();
            var entityById = new Dictionary<int, Transform>();

            // Pass 1: entities
            foreach (var desc in mission.Entities)
            {
                if (skipHidden && desc.HiddenInGame) { stats.Skipped++; continue; }

                if (desc.EntityClass == "DynamicLight")
                {
                    var builtGo = BuildDynamicLightEntity(desc, entityRoot);
                    if (desc.Id > 0) entityById[desc.Id] = builtGo.transform;
                    stats.Entities++;
                    continue;
                }

                if (desc.EntityClass == "SoundSpot")
                {
                    var builtGo = BuildSoundSpotEntity(desc, entityRoot);
                    if (desc.Id > 0) entityById[desc.Id] = builtGo.transform;
                    stats.Entities++;
                    continue;
                }

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

            // Pass 2: objects (generic LevelObjects first; legacy fallback kept for safety)
            if (mission.LevelObjects != null && mission.LevelObjects.Count > 0)
            {
                foreach (var levelObject in mission.LevelObjects)
                {
                    var prefab = registry.GetPrefabForObjectType(levelObject.Type);
                    if (prefab == null) { stats.Unknown++; continue; }

                    var desc = ToSceneObjectDesc(levelObject);
                    var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                    go.name = string.IsNullOrEmpty(desc.Name) ? $"{desc.Type}_obj" : desc.Name;
                    go.transform.SetParent(objectRoot.transform, worldPositionStays: false);

                    SetTransform(go.transform, desc.Pos, desc.Angles, 1f);

                    var entity = go.GetComponent<FcEntity>();
                    entity?.SetData(desc);

                    stats.Objects++;
                }
            }
            else
            {
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
            }

            // Pass 3: parent remap
            foreach (var desc in mission.Entities)
            {
                if (desc.ParentId <= 0) continue;
                if (!entityById.TryGetValue(desc.Id, out var child)) continue;
                if (!entityById.TryGetValue(desc.ParentId, out var parent)) continue;
                child.SetParent(parent, worldPositionStays: true);
            }

            return stats;
        }

        static FcObjectDesc ToSceneObjectDesc(FcLevelObjectDesc src)
        {
            var desc = new FcObjectDesc
            {
                Type = src.Type,
                Name = src.Name,
                Pos = src.Pos,
                Angles = src.Angles,
                AreaId = src.AreaId,
                ShapePoints = src.ShapePoints.Count > 0 ? src.ShapePoints.ToArray() : null,
            };

            if (src.Attributes.TryGetValue("Width", out var wText) &&
                src.Attributes.TryGetValue("Height", out var hText) &&
                src.Attributes.TryGetValue("Length", out var lText))
            {
                desc.AreaBoxDims = new Vector3(
                    ParseInvariantFloat(wText, 5f),
                    ParseInvariantFloat(hText, 5f),
                    ParseInvariantFloat(lText, 5f));
            }

            foreach (var kv in src.Attributes)
                desc.Attributes[kv.Key] = kv.Value;

            return desc;
        }

        static float ParseInvariantFloat(string value, float fallback)
        {
            if (float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float v))
                return v;
            return fallback;
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
            servicesGo.AddComponent<FcBrushLoadService>();
            servicesGo.AddComponent<FcVegetationLoadService>();
            servicesGo.AddComponent<FcMeshLoadService>();
            servicesGo.AddComponent<FcEntityLoadService>();
            servicesGo.AddComponent<FcAnimationLoadService>();
            servicesGo.AddComponent<FcLevelLoadService>();

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

                environment.Apply();
            }
        }

        static void SetTransform(Transform t, Vector3 pos, Vector3 cryAngles, float scale)
        {
            t.position = pos;
            ApplyCryRotationXYZ(t, cryAngles, scale);
        }

        // Cry entities render with Matrix34::CreateRotationXYZ(Deg2Rad(angles)).
        // Convert that linear part through the same scene/asset basis bridge as brushes.
        static void ApplyCryRotationXYZ(Transform t, Vector3 cryAngles, float scale)
        {
            float sx = Mathf.Sin(cryAngles.x * Mathf.Deg2Rad);
            float cx = Mathf.Cos(cryAngles.x * Mathf.Deg2Rad);
            float sy = Mathf.Sin(cryAngles.y * Mathf.Deg2Rad);
            float cy = Mathf.Cos(cryAngles.y * Mathf.Deg2Rad);
            float sz = Mathf.Sin(cryAngles.z * Mathf.Deg2Rad);
            float cz = Mathf.Cos(cryAngles.z * Mathf.Deg2Rad);

            float sycz = sy * cz;
            float sysz = sy * sz;

            float m00 = cy * cz;
            float m01 = sycz * sx - cx * sz;
            float m02 = sycz * cx + sx * sz;
            float m10 = cy * sz;
            float m11 = sysz * sx + cx * cz;
            float m12 = sysz * cx - sx * cz;
            float m20 = -sy;
            float m21 = cy * sx;
            float m22 = cy * cx;

            var colX = new Vector3( m00,  m20,  m10);
            var colY = new Vector3( m02,  m22,  m12);
            var colZ = new Vector3(-m01, -m21, -m11);

            float sX = colX.magnitude;
            float sY = colY.magnitude;
            float sZ = colZ.magnitude;

            if (sZ > 1e-5f && sY > 1e-5f)
                t.rotation = Quaternion.LookRotation(-colZ / sZ, colY / sY);

            t.localScale = new Vector3(
                (sX > 1e-5f ? sX : 1f) * scale,
                (sY > 1e-5f ? sY : 1f) * scale,
                (sZ > 1e-5f ? -sZ : -1f) * scale);
        }

        static GameObject BuildDynamicLightEntity(FcEntityDesc desc, GameObject entityRoot)
        {
            var go = new GameObject(string.IsNullOrEmpty(desc.Name) ? $"DynamicLight_{desc.Id}" : desc.Name);
            go.transform.SetParent(entityRoot.transform, false);
            SetTransform(go.transform, desc.Pos, desc.Angles, desc.Scale);

            var light = go.AddComponent<Light>();
            var p = desc.Properties;

            bool projectAll = p.TryGetValue("bProjectInAllDirs", out var piad) && piad == "1";
            int lighttype   = p.TryGetValue("lighttype", out var lt) && int.TryParse(lt, out int lti) ? lti : 0;
            light.type = lighttype == 2 && !projectAll ? LightType.Spot : LightType.Point;

            if (p.TryGetValue("clrDiffuse", out var clrStr) && TryParseFloats(clrStr, out float r, out float g, out float b))
            {
                float mult = p.TryGetValue("DiffuseMultiplier", out var dm) && TryParseF(dm, out float dmf) ? dmf : 1f;
                light.color     = new Color(r, g, b);
                light.intensity = mult;
            }

            if (p.TryGetValue("OuterRadius", out var rad) && TryParseF(rad, out float radius))
                light.range = radius;

            if (light.type == LightType.Spot && p.TryGetValue("ProjectorFov", out var fov) && TryParseF(fov, out float fovF))
                light.spotAngle = fovF;

            bool active = !p.TryGetValue("bActive", out var act) || act == "1";
            go.SetActive(active);

            return go;
        }

        static GameObject BuildSoundSpotEntity(FcEntityDesc desc, GameObject entityRoot)
        {
            var go = new GameObject(string.IsNullOrEmpty(desc.Name) ? $"SoundSpot_{desc.Id}" : desc.Name);
            go.transform.SetParent(entityRoot.transform, false);
            go.transform.position = desc.Pos;

            var audio = go.AddComponent<AudioSource>();
            var p = desc.Properties;

            audio.spatialBlend  = 1f;
            audio.rolloffMode   = AudioRolloffMode.Linear;
            audio.playOnAwake   = false;

            if (p.TryGetValue("InnerRadius", out var ir) && TryParseF(ir, out float innerR))
                audio.minDistance = innerR;
            if (p.TryGetValue("OuterRadius", out var or2) && TryParseF(or2, out float outerR))
                audio.maxDistance = outerR;
            if (p.TryGetValue("iVolume", out var vol) && TryParseF(vol, out float volF))
                audio.volume = Mathf.Clamp01(volF / 255f);

            audio.loop = p.TryGetValue("bLoop", out var lp) && lp == "1";

            bool enabled = !p.TryGetValue("bEnabled", out var en) || en == "1";
            go.SetActive(enabled);

            return go;
        }

        static bool TryParseF(string s, out float result) =>
            float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out result);

        static bool TryParseFloats(string s, out float a, out float b, out float c)
        {
            a = b = c = 0f;
            var parts = s.Split(',');
            if (parts.Length < 3) return false;
            return TryParseF(parts[0], out a) && TryParseF(parts[1], out b) && TryParseF(parts[2], out c);
        }

        public struct BuildStats
        {
            public int Entities;
            public int Objects;
            public int Brushes;
            public int Vegetation;
            public int Skipped;
            public int Unknown;

            public override string ToString() =>
                $"Entities: {Entities}  Objects: {Objects}  Brushes: {Brushes}  Vegetation: {Vegetation}  Skipped: {Skipped}  Unknown: {Unknown}";
        }
    }
}
