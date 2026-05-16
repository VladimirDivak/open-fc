using System;
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
        public enum SceneBuildMode
        {
            Runtime = 0,
            Authoring = 1,
        }

        static readonly int FcCoverTexId = Shader.PropertyToID("_FcCoverTex");
        static readonly int FcCoverScaleId = Shader.PropertyToID("_FcCoverScale");
        static readonly int FcCoverOffsetId = Shader.PropertyToID("_FcCoverOffset");

        // ── Build ────────────────────────────────────────────────────────────────

        public static BuildStats BuildScene(
            string levelName,
            string missionName,
            FcEntityPrefabRegistry registry,
            Scene targetScene,
            bool skipHidden = true,
            bool buildBrushes = true,
            SceneBuildMode buildMode = SceneBuildMode.Runtime)
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
            FcLevelLoader.TryLoadTerrainSettings(levelName, out int terrainRes, out int terrainUnit, out var surfaceLayers);
            Terrain levelTerrain = null;
            try
            {
                levelTerrain = BuildUnityTerrain(
                    levelRoot,
                    levelName,
                    mission.Environment,
                    terrainRes,
                    terrainUnit,
                    surfaceLayers,
                    buildMode);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[FcLevelSceneBuilder] Terrain build failed, continuing without terrain: {ex.Message}\n{ex.StackTrace}");
            }

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
            stats.Vegetation = BuildVegetationInstances(
                supplement,
                levelName,
                terrainRes,
                terrainUnit,
                levelRoot,
                levelTerrain,
                buildMode);
            report.RecordPhase("BuildVegetation", sw.Elapsed.TotalMilliseconds);

            // ── Brush geometry ──────────────────────────────────────────────────
            if (buildBrushes)
            {
                sw.Restart();
                stats.Brushes = BuildBrushesFromList(brushList, levelRoot, buildMode);
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
            bool skipHidden = true,
            SceneBuildMode buildMode = SceneBuildMode.Runtime)
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
            FcLevelLoader.TryLoadTerrainSettings(layoutData.LevelName, out int terrainRes, out int terrainUnit, out var surfaceLayers);
            Terrain levelTerrain = null;
            try
            {
                levelTerrain = BuildUnityTerrain(
                    levelRoot,
                    layoutData.LevelName,
                    mission.Environment,
                    terrainRes,
                    terrainUnit,
                    surfaceLayers,
                    buildMode);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[FcLevelSceneBuilder] Terrain build failed, continuing without terrain: {ex.Message}\n{ex.StackTrace}");
            }

            var entityRoot = new GameObject("Entities");
            entityRoot.transform.SetParent(levelRoot.transform, worldPositionStays: false);
            var objectRoot = new GameObject("Objects");
            objectRoot.transform.SetParent(levelRoot.transform, worldPositionStays: false);

            var stats = BuildMission(mission, registry, entityRoot, objectRoot, skipHidden);

            stats.Vegetation = BuildVegetationInstancesFromLayout(
                layoutData,
                terrainRes,
                terrainUnit,
                levelRoot,
                levelTerrain,
                buildMode);

            stats.Brushes = BuildBrushesFromList(brushList, levelRoot, buildMode);

            EditorSceneManager.MarkSceneDirty(targetScene);
            return stats;
        }

        // ── Brushes ──────────────────────────────────────────────────────────────

        static int BuildBrushesFromList(
            IReadOnlyList<FcBrushDesc> brushes,
            GameObject levelRoot,
            SceneBuildMode buildMode)
        {
            if (brushes == null || brushes.Count == 0) return 0;

            string rootName = buildMode == SceneBuildMode.Authoring ? "Brushes_Authoring" : "Brushes";
            var brushRoot = new GameObject(rootName);
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
                so.FindProperty("_materialOverride").stringValue = desc.MaterialOverride ?? string.Empty;
                so.FindProperty("_materialId").intValue          = desc.MaterialId;
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
            Terrain terrain,
            SceneBuildMode buildMode)
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

            if (buildMode == SceneBuildMode.Authoring)
            {
                return PlaceVegetationAsAuthoringInstances(
                    supplement.VegetationInstances,
                    typeByIndex,
                    levelRoot,
                    terrain);
            }

            return PlaceVegetationAsTreeInstances(supplement.VegetationInstances, typeByIndex, terrain);
        }

        static int BuildVegetationInstancesFromLayout(
            FcLevelLayoutDataV2 layoutData,
            int terrainResolution,
            int heightmapUnitSize,
            GameObject levelRoot,
            Terrain terrain,
            SceneBuildMode buildMode)
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

            if (buildMode == SceneBuildMode.Authoring)
            {
                return PlaceVegetationAsAuthoringInstances(
                    layoutData.VegetationInstances,
                    typeByIndex,
                    levelRoot,
                    terrain);
            }

            return PlaceVegetationAsTreeInstances(layoutData.VegetationInstances, typeByIndex, terrain);
        }

        static int PlaceVegetationAsAuthoringInstances(
            FcLevelSupplementData.VegetationInstanceDesc[] instances,
            Dictionary<int, FcLevelSupplementData.VegetationTypeDesc> typeByIndex,
            GameObject levelRoot,
            Terrain terrain)
        {
            if (instances == null || instances.Length == 0 || levelRoot == null)
                return 0;

            var root = new GameObject("Vegetation_Authoring");
            root.transform.SetParent(levelRoot.transform, worldPositionStays: false);

            Vector3 terrainOrigin = terrain != null ? terrain.transform.position : Vector3.zero;
            float terrainSizeX = terrain != null && terrain.terrainData != null ? terrain.terrainData.size.x : 1f;
            float terrainSizeZ = terrain != null && terrain.terrainData != null ? terrain.terrainData.size.z : 1f;

            int placed = 0;
            for (int i = 0; i < instances.Length; i++)
            {
                var inst = instances[i];
                if (!typeByIndex.TryGetValue(inst.Type, out var typeDef))
                    continue;
                if (string.IsNullOrWhiteSpace(typeDef.FileName))
                    continue;

                float nx = inst.X / 65535f;
                float nz = inst.Y / 65535f;
                float wx = terrainOrigin.x + nx * terrainSizeX;
                float wz = terrainOrigin.z + nz * terrainSizeZ;
                float wy = terrain != null ? terrain.SampleHeight(new Vector3(wx, 0f, wz)) : 0f;

                var go = new GameObject($"Vegetation_{i:D6}");
                go.transform.SetParent(root.transform, worldPositionStays: false);
                go.transform.position = new Vector3(wx, wy, wz);
                float scale = inst.Scale > 0f ? inst.Scale : 1f;
                go.transform.localScale = new Vector3(scale, scale, scale);

                var vegetation = go.AddComponent<FcVegetationInstance>();
                var so = new SerializedObject(vegetation);
                so.FindProperty("_virtualPath").stringValue = typeDef.FileName;
                so.FindProperty("_typeIndex").intValue = inst.Type;
                so.FindProperty("_instanceScale").floatValue = scale;
                so.ApplyModifiedPropertiesWithoutUndo();
                placed++;
            }

            return placed;
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
            // CollisionMode is resolved at runtime by FcVegetationTerrainService.ResolveCollisionPolicies.
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
                elem.FindPropertyRelative("TypeIndex").intValue      = typeEntries[i].TypeIndex;
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
            int heightmapUnitSize,
            List<FcTerrainLayerDesc> surfaceLayers,
            SceneBuildMode buildMode)
        {
            string basePath = $"levels/{levelName.ToLowerInvariant()}";
            string h16Path = $"{basePath}/terrain/land_map.h16";
            if (!FcFileSystem.Exists(h16Path))
                return null;

            byte[] h16Bytes = FcFileSystem.ReadAllBytes(h16Path);
            if (!FcTerrainHeightmapDecoder.TryDecodeH16(h16Bytes, resolution, out var samples))
            {
                Debug.LogWarning($"[FcLevelSceneBuilder] Could not decode terrain heightmap '{h16Path}'.");
                return null;
            }

            if (!FcTerrainHeightmapDecoder.TryDecodeToUnityHeights(samples, resolution, out var heights))
                return null;

            int hmRes = resolution + 1; // e.g. 1025
            float worldSize = (resolution - 1) * heightmapUnitSize;
            surfaceLayers ??= new List<FcTerrainLayerDesc>();

            var terrainData = new TerrainData();
            terrainData.heightmapResolution = hmRes;
            terrainData.size = new Vector3(worldSize, FcTerrainHeightmapDecoder.MaxWorldHeight, worldSize);
            terrainData.SetHeights(0, 0, heights);
            var inMemoryTerrainData = terrainData;

            // ── Persist TerrainData ──────────────────────────────────────────────
            const string fcDataDir = "Assets/FCData/Levels";
            string levelDir = $"{fcDataDir}/{NormalizeLevelAssetKey(levelName)}";
            EnsureDir(fcDataDir);
            EnsureDir(levelDir);
            string tdPath = $"{levelDir}/TerrainData.asset";
            if (AssetDatabase.LoadAssetAtPath<TerrainData>(tdPath) != null)
                AssetDatabase.DeleteAsset(tdPath);
            AssetDatabase.CreateAsset(terrainData, tdPath);
            AssetDatabase.SaveAssets();
            terrainData = AssetDatabase.LoadAssetAtPath<TerrainData>(tdPath);
            if (terrainData == null)
            {
                Debug.LogWarning(
                    $"[FcLevelSceneBuilder] Failed to reload TerrainData asset '{tdPath}', using in-memory instance.");
                terrainData = inMemoryTerrainData;
            }

            // ── Build TerrainLayers ──────────────────────────────────────────────
            // Layer 0: cover_low.dds — global megatexture (CLAMP, tiled once over worldSize).
            // Layers 1-N: detail textures per surface type from <SurfaceTypes> in leveldata.xml.
            var terrainLayers = new List<TerrainLayer>();
            bool authoringMode = buildMode == SceneBuildMode.Authoring;
            string terrainTexturesDir = $"{levelDir}/TerrainTextures";
            if (authoringMode)
                EnsureDir(terrainTexturesDir);

            string coverPath = $"{basePath}/terrain/cover_low.dds";
            {
                var layer = new TerrainLayer { tileSize = new Vector2(worldSize, worldSize) };
                if (authoringMode)
                {
                    TryLoadAndPersistTerrainTexture(
                        coverPath,
                        $"{terrainTexturesDir}/Cover.asset",
                        transpose: true,
                        out var coverTexture);
                    layer.diffuseTexture = coverTexture;
                }

                string layerPath = $"{levelDir}/TerrainLayer_Cover.terrainlayer";
                terrainLayers.Add(SaveAndLoadTerrainLayerAsset(layer, layerPath));
            }

            var validSurfaceLayers = new List<FcTerrainLayerDesc>(surfaceLayers.Count);
            for (int i = 0; i < surfaceLayers.Count; i++)
            {
                var layerDesc = surfaceLayers[i];
                if (layerDesc != null)
                    validSurfaceLayers.Add(layerDesc);
            }

            foreach (var layerDesc in validSurfaceLayers)
            {
                // DetailScaleX/Y in leveldata.xml is a UV scale: 1/scale = world meters per tile.
                float tileSizeX = layerDesc.ScaleX > 0f ? 1f / layerDesc.ScaleX : 1f;
                float tileSizeY = layerDesc.ScaleY > 0f ? 1f / layerDesc.ScaleY : 1f;
                var layer = new TerrainLayer { tileSize = new Vector2(tileSizeX, tileSizeY) };
                if (authoringMode)
                {
                    TryLoadAndPersistTerrainTexture(
                        layerDesc.DetailTexturePath,
                        $"{terrainTexturesDir}/SurfaceType_{layerDesc.SurfaceTypeId}.asset",
                        transpose: false,
                        out var detailTexture);
                    layer.diffuseTexture = detailTexture;
                }

                string layerPath = $"{levelDir}/TerrainLayer_Type{layerDesc.SurfaceTypeId}.terrainlayer";
                terrainLayers.Add(SaveAndLoadTerrainLayerAsset(layer, layerPath));
            }

            terrainLayers.RemoveAll(l => l == null);
            terrainData.terrainLayers = terrainLayers.ToArray();

            // ── Build and apply alphamap (splatmap) ──────────────────────────────
            if (validSurfaceLayers.Count > 0)
            {
                byte[] surfaceTypeIds = FcTerrainHeightmapDecoder.DecodeSurfaceTypes(samples);
                terrainData.alphamapResolution = resolution;
                float[,,] alphamap = FcTerrainSplatmapBuilder.Build(surfaceTypeIds, resolution, validSurfaceLayers);
                terrainData.SetAlphamaps(0, 0, alphamap);
            }

            AssetDatabase.SaveAssets();

            // ── Create Terrain GameObject ────────────────────────────────────────
            var terrainGo = Terrain.CreateTerrainGameObject(terrainData);
            terrainGo.name = "Terrain";
            terrainGo.transform.SetParent(levelRoot.transform, worldPositionStays: false);

            var terrain = terrainGo.GetComponent<Terrain>();
            const string terrainMaterialPath = "Assets/Materials/FarCry Terrain Material.mat";
            var terrainMaterial = AssetDatabase.LoadAssetAtPath<Material>(terrainMaterialPath);
            Material assignedTerrainMaterial = null;
            if (IsUsableTerrainMaterial(terrainMaterial))
            {
                if (buildMode == SceneBuildMode.Authoring)
                {
                    string authoringMaterialPath = $"{levelDir}/TerrainMaterial_Authoring.mat";
                    if (AssetDatabase.LoadAssetAtPath<Material>(authoringMaterialPath) != null)
                        AssetDatabase.DeleteAsset(authoringMaterialPath);

                    var authoringMaterial = new Material(terrainMaterial)
                    {
                        name = $"{terrainMaterial.name}_{levelName}_Authoring"
                    };
                    AssetDatabase.CreateAsset(authoringMaterial, authoringMaterialPath);
                    AssetDatabase.SaveAssets();
                    assignedTerrainMaterial = AssetDatabase.LoadAssetAtPath<Material>(authoringMaterialPath);
                    if (!IsUsableTerrainMaterial(assignedTerrainMaterial))
                        assignedTerrainMaterial = authoringMaterial;
                }
                else
                {
                    assignedTerrainMaterial = terrainMaterial;
                }
            }
            else
            {
                Debug.LogWarning(
                    $"[FcLevelSceneBuilder] Terrain material missing/invalid at '{terrainMaterialPath}'. " +
                    "Trying terrain material fallback.");
            }

            if (!IsUsableTerrainMaterial(assignedTerrainMaterial))
            {
                assignedTerrainMaterial = CreateFallbackTerrainMaterial(levelDir, levelName, buildMode);
            }

            if (IsUsableTerrainMaterial(assignedTerrainMaterial))
            {
                terrain.materialType = Terrain.MaterialType.Custom;
                terrain.materialTemplate = assignedTerrainMaterial;
            }
            else
            {
                terrain.materialTemplate = null;
                terrain.materialType = Terrain.MaterialType.BuiltInStandard;
                Debug.LogWarning(
                    "[FcLevelSceneBuilder] No usable custom terrain material found. " +
                    "Using built-in terrain material mode.");
            }

            if (buildMode == SceneBuildMode.Runtime)
            {
                // Runtime scene keeps lazy terrain texture load from VFS.
                var texService = terrainGo.AddComponent<FcTerrainTextureService>();
                texService.CoverLayer = new FcTerrainTextureService.LayerDef
                {
                    VfsPath = coverPath,
                    TileSizeX = worldSize,
                    TileSizeY = worldSize,
                };
                texService.CoverCtcPath = $"{basePath}/terrain/cover.ctc";
                texService.CoverSectorCount = Mathf.Max(1, Mathf.RoundToInt((resolution * heightmapUnitSize) / 64f));
                texService.DetailLayers = new FcTerrainTextureService.LayerDef[surfaceLayers.Count];
                for (int i = 0; i < surfaceLayers.Count; i++)
                {
                    var ld = surfaceLayers[i];
                    texService.DetailLayers[i] = new FcTerrainTextureService.LayerDef
                    {
                        VfsPath = ld.DetailTexturePath,
                        TileSizeX = ld.ScaleX > 0f ? 1f / ld.ScaleX : 1f,
                        TileSizeY = ld.ScaleY > 0f ? 1f / ld.ScaleY : 1f,
                    };
                }
            }
            else
            {
                ApplyAuthoringCoverMaterialProperties(terrain);
            }

            if (environment != null)
                BuildWaterPlane(terrainGo.transform, resolution, heightmapUnitSize,
                    environment.WaterLevel * terrainData.size.y / FcTerrainHeightmapDecoder.MaxWorldHeight);

            return terrain;
        }

        static bool IsUsableTerrainMaterial(Material material)
        {
            if (material == null)
                return false;
            if (material.shader == null)
                return false;
            return !string.Equals(
                material.shader.name,
                "Hidden/InternalErrorShader",
                System.StringComparison.Ordinal);
        }

        static Material CreateFallbackTerrainMaterial(string levelDir, string levelName, SceneBuildMode buildMode)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Terrain/Lit");
            if (shader == null)
                shader = Shader.Find("Nature/Terrain/Standard");
            if (shader == null)
                return null;

            var fallback = new Material(shader)
            {
                name = $"TerrainFallback_{levelName}"
            };

            if (buildMode != SceneBuildMode.Authoring)
                return fallback;

            try
            {
                string path = $"{levelDir}/TerrainMaterial_Fallback.mat";
                if (AssetDatabase.LoadAssetAtPath<Material>(path) != null)
                    AssetDatabase.DeleteAsset(path);
                AssetDatabase.CreateAsset(fallback, path);
                AssetDatabase.SaveAssets();
                var persisted = AssetDatabase.LoadAssetAtPath<Material>(path);
                return persisted != null ? persisted : fallback;
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[FcLevelSceneBuilder] Failed to persist fallback terrain material: {e.Message}");
                return fallback;
            }
        }

        static void SaveTerrainLayerAsset(TerrainLayer layer, string assetPath)
        {
            if (AssetDatabase.LoadAssetAtPath<TerrainLayer>(assetPath) != null)
                AssetDatabase.DeleteAsset(assetPath);
            AssetDatabase.CreateAsset(layer, assetPath);
            AssetDatabase.SaveAssets();
        }

        static TerrainLayer SaveAndLoadTerrainLayerAsset(TerrainLayer layer, string assetPath)
        {
            if (layer == null)
                return null;

            SaveTerrainLayerAsset(layer, assetPath);
            var loaded = AssetDatabase.LoadAssetAtPath<TerrainLayer>(assetPath);
            if (loaded == null)
            {
                Debug.LogWarning($"[FcLevelSceneBuilder] Failed to load TerrainLayer asset '{assetPath}', using in-memory layer.");
                return layer;
            }

            return loaded;
        }

        static void ApplyAuthoringCoverMaterialProperties(Terrain terrain)
        {
            if (terrain == null || terrain.terrainData == null)
                return;

            var layers = terrain.terrainData.terrainLayers;
            if (layers == null || layers.Length == 0 || layers[0] == null)
                return;

            var coverLayer = layers[0];
            var material = terrain.materialTemplate;
            if (material == null)
                return;
            if (!material.HasProperty(FcCoverTexId))
                return;

            material.SetTexture(FcCoverTexId, coverLayer.diffuseTexture);

            Vector2 tileSize = coverLayer.tileSize;
            float scaleX = tileSize.x > 0f ? 1f / tileSize.x : 1f;
            float scaleY = tileSize.y > 0f ? 1f / tileSize.y : 1f;
            if (material.HasProperty(FcCoverScaleId))
                material.SetVector(FcCoverScaleId, new Vector4(scaleX, scaleY, 0f, 0f));
            if (material.HasProperty(FcCoverOffsetId))
                material.SetVector(FcCoverOffsetId, Vector4.zero);
        }

        static bool TryLoadAndPersistTerrainTexture(
            string virtualPath,
            string assetPath,
            bool transpose,
            out Texture2D textureAsset)
        {
            textureAsset = null;
            if (string.IsNullOrWhiteSpace(virtualPath))
                return false;
            if (!TextureImportService.IsSupportedVirtualPath(virtualPath))
                return false;
            if (!TextureImportService.RuntimeService.TryLoad(virtualPath, out var loadedTexture))
            {
                Debug.LogWarning($"[FcLevelSceneBuilder] Failed to load terrain texture '{virtualPath}'.");
                return false;
            }
            if (loadedTexture == null)
                return false;

            Texture2D assetTexture = transpose
                ? TransposeTexture(loadedTexture)
                : DuplicateTexture(loadedTexture);
            if (assetTexture == null)
                return false;

            if (AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath) != null)
                AssetDatabase.DeleteAsset(assetPath);

            AssetDatabase.CreateAsset(assetTexture, assetPath);
            textureAsset = AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
            return textureAsset != null;
        }

        static Texture2D DuplicateTexture(Texture2D source)
        {
            if (source == null)
                return null;
            if (source.isReadable)
                return Object.Instantiate(source);

            return CopyTextureViaRenderTarget(source);
        }

        static Texture2D TransposeTexture(Texture2D source)
        {
            if (source == null)
                return null;

            var readable = source.isReadable ? source : CopyTextureViaRenderTarget(source);
            if (readable == null)
                return null;

            int w = readable.width;
            int h = readable.height;
            var srcPixels = readable.GetPixels32();
            if (!source.isReadable)
                Object.DestroyImmediate(readable);

            var transposed = new Texture2D(h, w, TextureFormat.RGBA32, mipChain: false, linear: false);
            var dstPixels = new Color32[srcPixels.Length];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                    dstPixels[x * h + y] = srcPixels[y * w + x];
            transposed.SetPixels32(dstPixels);
            transposed.Apply(updateMipmaps: false, makeNoLongerReadable: false);
            transposed.wrapMode = source.wrapMode;
            transposed.wrapModeU = source.wrapModeU;
            transposed.wrapModeV = source.wrapModeV;
            transposed.wrapModeW = source.wrapModeW;
            transposed.filterMode = source.filterMode;
            transposed.anisoLevel = source.anisoLevel;
            transposed.mipMapBias = source.mipMapBias;
            return transposed;
        }

        static Texture2D CopyTextureViaRenderTarget(Texture2D source)
        {
            RenderTexture rt = null;
            var previous = RenderTexture.active;
            try
            {
                rt = RenderTexture.GetTemporary(
                    source.width,
                    source.height,
                    0,
                    RenderTextureFormat.ARGB32,
                    RenderTextureReadWrite.Default);

                Graphics.Blit(source, rt);
                RenderTexture.active = rt;
                var copy = new Texture2D(
                    source.width,
                    source.height,
                    TextureFormat.RGBA32,
                    mipChain: source.mipmapCount > 1,
                    linear: false);
                copy.ReadPixels(new Rect(0, 0, source.width, source.height), 0, 0);
                copy.Apply(updateMipmaps: source.mipmapCount > 1, makeNoLongerReadable: false);
                copy.wrapMode = source.wrapMode;
                copy.wrapModeU = source.wrapModeU;
                copy.wrapModeV = source.wrapModeV;
                copy.wrapModeW = source.wrapModeW;
                copy.filterMode = source.filterMode;
                copy.anisoLevel = source.anisoLevel;
                copy.mipMapBias = source.mipMapBias;
                return copy;
            }
            finally
            {
                RenderTexture.active = previous;
                if (rt != null)
                    RenderTexture.ReleaseTemporary(rt);
            }
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

        static string NormalizeLevelAssetKey(string levelName)
        {
            if (string.IsNullOrWhiteSpace(levelName))
                return "level";

            var trimmed = levelName.Trim().ToLowerInvariant();
            var chars = trimmed.ToCharArray();
            for (int i = 0; i < chars.Length; i++)
            {
                char c = chars[i];
                if (c == '/' || c == '\\' || char.IsControl(c) || char.IsWhiteSpace(c))
                    chars[i] = '_';
            }

            string key = new string(chars).Trim('_', '.');
            return string.IsNullOrEmpty(key) ? "level" : key;
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
            servicesGo.AddComponent<FcLevelMaterialOverrideService>();
            var loadService = servicesGo.AddComponent<FcLevelLoadService>();
            var loadSo = new SerializedObject(loadService);
            loadSo.FindProperty("_loadedLevelName").stringValue = levelName;
            loadSo.ApplyModifiedPropertiesWithoutUndo();

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
