using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using OpenFarCry.Level.Data;
using OpenFarCry.Level.Entities;
using OpenFarCry.Level.Registry;
using OpenFarCry.Level.Services;
using OpenFarCry.Level.Volumes;
using OpenFarCry.FileSystem;
using OpenFarCry.Importer.Texture;
using OpenFarCry.Rendering.Water;
using OpenFarCry.Rendering.Water.Editor;
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
            var volumeRoot = new GameObject("Volumes");
            volumeRoot.transform.SetParent(levelRoot.transform, worldPositionStays: false);

            sw.Restart();
            var stats = BuildMission(mission, registry, entityRoot, objectRoot, volumeRoot, skipHidden);
            report.RecordPhase("BuildEntities", sw.Elapsed.TotalMilliseconds);

            // Editor-XML static lights (from <LevelName>.cry) — bake-only sources not in mission XML.
            sw.Restart();
            stats.Lights += BuildEditorXmlLights(levelName, mission, entityRoot);
            report.RecordPhase("BuildEditorXmlLights", sw.Elapsed.TotalMilliseconds);

            // Movie sequence placeholders from moviedata.xml.
            sw.Restart();
            stats.Sequences = BuildMovieSequencePlaceholders(levelName, levelRoot);
            report.RecordPhase("BuildMovieSequences", sw.Elapsed.TotalMilliseconds);

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

        // Places per-instance vegetation wrappers into an already-built scene as a temporary
        // bake-only root. Used by the APV bake pipeline so vegetation occludes probes (the
        // production runtime path uses FcVegetationTerrainService with no MeshRenderers).
        public static int PlaceVegetationAuthoringWrappersInExistingScene(
            string levelName,
            GameObject levelRoot,
            Terrain terrain,
            string rootName = "Vegetation_AuthoringForBake",
            HashSet<string> excludedCategories = null)
        {
            if (levelRoot == null)
                return 0;

            var supplement = FcLevelSupplementLoader.Load(levelName);
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

            return PlaceVegetationAsAuthoringInstances(
                supplement.VegetationInstances,
                typeByIndex,
                levelRoot,
                terrain,
                rootName,
                excludedCategories);
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
            var volumeRoot = new GameObject("Volumes");
            volumeRoot.transform.SetParent(levelRoot.transform, worldPositionStays: false);

            var stats = BuildMission(mission, registry, entityRoot, objectRoot, volumeRoot, skipHidden);

            stats.Vegetation = BuildVegetationInstancesFromLayout(
                layoutData,
                terrainRes,
                terrainUnit,
                levelRoot,
                levelTerrain,
                buildMode);

            stats.Brushes = BuildBrushesFromList(brushList, levelRoot, buildMode);

            stats.Sequences = BuildMovieSequencePlaceholders(layoutData.LevelName, levelRoot);

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
            => PlaceVegetationAsAuthoringInstances(instances, typeByIndex, levelRoot, terrain, "Vegetation_Authoring", excludedCategories: null);

        static int PlaceVegetationAsAuthoringInstances(
            FcLevelSupplementData.VegetationInstanceDesc[] instances,
            Dictionary<int, FcLevelSupplementData.VegetationTypeDesc> typeByIndex,
            GameObject levelRoot,
            Terrain terrain,
            string rootName,
            HashSet<string> excludedCategories)
        {
            if (instances == null || instances.Length == 0 || levelRoot == null)
                return 0;

            var root = new GameObject(rootName);
            root.transform.SetParent(levelRoot.transform, worldPositionStays: false);

            Vector3 terrainOrigin = terrain != null ? terrain.transform.position : Vector3.zero;
            float terrainSizeX = terrain != null && terrain.terrainData != null ? terrain.terrainData.size.x : 1f;
            float terrainSizeZ = terrain != null && terrain.terrainData != null ? terrain.terrainData.size.z : 1f;

            int placed = 0;
            int skippedByCategory = 0;
            for (int i = 0; i < instances.Length; i++)
            {
                var inst = instances[i];
                if (!typeByIndex.TryGetValue(inst.Type, out var typeDef))
                    continue;
                if (string.IsNullOrWhiteSpace(typeDef.FileName))
                    continue;

                if (excludedCategories != null && excludedCategories.Count > 0 &&
                    TryGetVegetationCategory(typeDef, out var category) &&
                    excludedCategories.Contains(category))
                {
                    skippedByCategory++;
                    continue;
                }

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
                so.FindProperty("_materialOverride").stringValue = typeDef.Material ?? string.Empty;
                so.ApplyModifiedPropertiesWithoutUndo();
                placed++;
            }

            if (skippedByCategory > 0)
                Debug.Log($"[FcLevelSceneBuilder] Vegetation authoring placement: placed={placed}, skippedByCategory={skippedByCategory}.");
            return placed;
        }

        static bool TryGetVegetationCategory(
            FcLevelSupplementData.VegetationTypeDesc typeDef,
            out string category)
        {
            category = null;
            if (typeDef.Attributes == null) return false;
            for (int i = 0; i < typeDef.Attributes.Length; i++)
            {
                var pair = typeDef.Attributes[i];
                if (pair.Key != null &&
                    string.Equals(pair.Key, "Category", System.StringComparison.OrdinalIgnoreCase))
                {
                    category = pair.Value != null ? pair.Value.Trim().ToLowerInvariant() : string.Empty;
                    return true;
                }
            }
            return false;
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

            Debug.Log($"[FcLevelSceneBuilder] Building terrain for '{levelName}'. h16Path='{h16Path}', layers={surfaceLayers?.Count ?? 0}");

            if (!FcFileSystem.Exists(h16Path))
            {
                Debug.LogWarning($"[FcLevelSceneBuilder] Terrain heightmap not found in VFS: '{h16Path}'");
                return null;
            }

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

            // ── Bake albedo + build triplanar detail inputs ──────────────────────
            var megaAlbedo  = BakeTerrainAlbedo(levelName, samples, resolution, heightmapUnitSize, levelDir);
            var detailArray = BuildDetailArray(surfaceLayers, levelDir, out var detailScaleA, out var detailScaleB);
            BuildSplatTextures(samples, resolution, levelDir, out var splatA, out var splatB);
            terrainData.terrainLayers = new TerrainLayer[0];
            ApplyTerrainHoles(terrainData, samples, resolution);

            AssetDatabase.SaveAssets();

            // ── Create Terrain GameObject ────────────────────────────────────────
            var terrainGo = Terrain.CreateTerrainGameObject(terrainData);
            terrainGo.name = "Terrain";
            terrainGo.transform.SetParent(levelRoot.transform, worldPositionStays: false);

            var terrain = terrainGo.GetComponent<Terrain>();
            // The FarCryTerrain shader graph multiplies the baked albedo over a
            // triplanar detail layer blended per-texel by the surface-type splat.
            terrain.materialTemplate = ResolveTerrainMaterial(
                levelDir, levelName, megaAlbedo, splatA, splatB, detailArray,
                detailScaleA, detailScaleB, buildMode);

            if (environment != null)
                BuildWaterPlane(terrainGo.transform, resolution, heightmapUnitSize,
                    environment.WaterLevel * terrainData.size.y / FcTerrainHeightmapDecoder.MaxWorldHeight);

            return terrain;
        }

        // Bakes the clean albedo megatexture from the level .cry paint layers and
        // persists it. Returns the persisted texture, or null on failure.
        static Texture2D BakeTerrainAlbedo(
            string levelName, ushort[] samples, int resolution, int heightmapUnitSize, string levelDir)
        {
            string levelKey = levelName.ToLowerInvariant();
            if (!FcTerrainLayerSet.TryLoad(levelKey, out var layerSet))
            {
                Debug.LogWarning(
                    $"[FcLevelSceneBuilder] No .cry terrain layers for '{levelName}'; albedo not baked.");
                return null;
            }

            if (!FcTerrainAlbedoBaker.TryBake(
                    layerSet, samples, resolution, heightmapUnitSize, out var albedo, out int outRes))
            {
                Debug.LogWarning($"[FcLevelSceneBuilder] Terrain albedo bake failed for '{levelName}'.");
                return null;
            }

            var albedoTex = new Texture2D(outRes, outRes, TextureFormat.RGBA32, mipChain: true)
            {
                name = "TerrainAlbedo",
                wrapMode = TextureWrapMode.Clamp,
            };
            albedoTex.SetPixels32(albedo);
            albedoTex.Apply(updateMipmaps: true);
            // Terrain albedo carries no alpha; DXT1 is ~8x smaller than RGBA32.
            EditorUtility.CompressTexture(albedoTex, TextureFormat.DXT1, TextureCompressionQuality.Best);

            string albedoPath = $"{levelDir}/TerrainAlbedo.asset";
            if (AssetDatabase.LoadAssetAtPath<Texture2D>(albedoPath) != null)
                AssetDatabase.DeleteAsset(albedoPath);
            AssetDatabase.CreateAsset(albedoTex, albedoPath);
            AssetDatabase.SaveAssets();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(albedoPath) ?? albedoTex;
        }

        // Marks terrain cells whose h16 surface type is STYPE_HOLE (7) as holes,
        // cutting geometry where Far Cry has cave / tunnel openings.
        static void ApplyTerrainHoles(TerrainData terrainData, ushort[] samples, int resolution)
        {
            int hr = terrainData.holesResolution;
            if (hr <= 0)
                return;

            byte[] ids = FcTerrainHeightmapDecoder.DecodeSurfaceTypes(samples);
            var holes = new bool[hr, hr];
            int holeCount = 0;
            for (int z = 0; z < hr; z++)
                for (int x = 0; x < hr; x++)
                {
                    int sx = x * resolution / hr;
                    int sz = z * resolution / hr;
                    bool isHole = ids[sz * resolution + sx] == 7;
                    holes[z, x] = !isHole; // Unity convention: false = hole, true = solid
                    if (isHole) holeCount++;
                }

            terrainData.SetHoles(0, 0, holes);
            if (holeCount > 0)
                Debug.Log($"[FcLevelSceneBuilder] Terrain holes: {holeCount} cell(s) cut.");
        }

        // Builds two RGBA splat-weight textures from the h16 surface type ids:
        // SplatA channels = surface types 0-3, SplatB channels = types 4-7.
        // The hard per-texel id field is blurred so detail blends smoothly
        // between types instead of switching in hard squares.
        static void BuildSplatTextures(
            ushort[] samples, int resolution, string levelDir,
            out Texture2D splatA, out Texture2D splatB)
        {
            byte[] ids = FcTerrainHeightmapDecoder.DecodeSurfaceTypes(samples);
            int n = resolution * resolution;
            var a = new Color[n];
            var b = new Color[n];
            for (int i = 0; i < n; i++)
            {
                Color ca = default, cb = default;
                switch (ids[i])
                {
                    case 0: ca.r = 1f; break;
                    case 1: ca.g = 1f; break;
                    case 2: ca.b = 1f; break;
                    case 3: ca.a = 1f; break;
                    case 4: cb.r = 1f; break;
                    case 5: cb.g = 1f; break;
                    case 6: cb.b = 1f; break;
                    default: cb.a = 1f; break; // 7
                }
                a[i] = ca;
                b[i] = cb;
            }

            const int blurIterations = 4; // softens ~2 m id texels into a smooth fade
            for (int k = 0; k < blurIterations; k++)
            {
                BoxBlurColors(a, resolution);
                BoxBlurColors(b, resolution);
            }

            splatA = CreateSplatTexture(a, resolution, "TerrainSplatA", $"{levelDir}/TerrainSplatA.asset");
            splatB = CreateSplatTexture(b, resolution, "TerrainSplatB", $"{levelDir}/TerrainSplatB.asset");
        }

        static Texture2D CreateSplatTexture(Color[] pixels, int res, string name, string path)
        {
            var tex = new Texture2D(res, res, TextureFormat.RGBA32, mipChain: true, linear: true)
            {
                name = name,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
            };
            tex.SetPixels(pixels);
            tex.Apply(updateMipmaps: true);
            if (AssetDatabase.LoadAssetAtPath<Texture2D>(path) != null)
                AssetDatabase.DeleteAsset(path);
            AssetDatabase.CreateAsset(tex, path);
            AssetDatabase.SaveAssets();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path) ?? tex;
        }

        // In-place 3x3 box blur over an RGBA weight field.
        static void BoxBlurColors(Color[] buf, int res)
        {
            var src = (Color[])buf.Clone();
            Parallel.For(0, res, z =>
            {
                int z0 = Mathf.Max(0, z - 1), z1 = Mathf.Min(res - 1, z + 1);
                for (int x = 0; x < res; x++)
                {
                    int x0 = Mathf.Max(0, x - 1), x1 = Mathf.Min(res - 1, x + 1);
                    Color sum = default;
                    int cnt = 0;
                    for (int zz = z0; zz <= z1; zz++)
                        for (int xx = x0; xx <= x1; xx++)
                        {
                            sum += src[zz * res + xx];
                            cnt++;
                        }
                    buf[z * res + x] = sum * (1f / cnt);
                }
            });
        }

        // Builds a Texture2DArray of detail textures, one slice per surface type id
        // (0-7), loaded from the leveldata.xml detail paths. The per-type tiling
        // (tiles per metre, from each layer DetailScaleX) is packed into two
        // Vector4s: scaleA = types 0-3, scaleB = types 4-7.
        static Texture2DArray BuildDetailArray(
            List<FcTerrainLayerDesc> surfaceLayers, string levelDir,
            out Vector4 scaleA, out Vector4 scaleB)
        {
            const int sliceCount = 8; // surface ids 0-7
            const float defaultScale = 0.2f; // tiles per metre
            var scales = new float[sliceCount];
            for (int i = 0; i < sliceCount; i++)
                scales[i] = defaultScale;
            scaleA = scaleB = Vector4.zero;

            if (surfaceLayers == null || surfaceLayers.Count == 0)
            {
                Debug.LogWarning("[FcLevelSceneBuilder] No surface-type detail layers; terrain detail skipped.");
                return null;
            }

            var slices = new Texture2D[sliceCount];
            int maxW = 0, maxH = 0;

            string detailDir = $"{levelDir}/TerrainDetail";
            EnsureDir(detailDir);

            foreach (var ld in surfaceLayers)
            {
                if (ld == null || ld.SurfaceTypeId >= sliceCount)
                    continue;
                if (ld.ScaleX > 0f)
                    scales[ld.SurfaceTypeId] = ld.ScaleX;
                if (TryLoadAndPersistTerrainTexture(
                        ld.DetailTexturePath, $"{detailDir}/Detail_{ld.SurfaceTypeId}.asset",
                        transpose: false, out var tex) && tex != null)
                {
                    slices[ld.SurfaceTypeId] = tex;
                    maxW = Mathf.Max(maxW, tex.width);
                    maxH = Mathf.Max(maxH, tex.height);
                }
            }

            scaleA = new Vector4(scales[0], scales[1], scales[2], scales[3]);
            scaleB = new Vector4(scales[4], scales[5], scales[6], scales[7]);

            if (maxW == 0)
            {
                Debug.LogWarning("[FcLevelSceneBuilder] No detail textures resolved; terrain detail skipped.");
                return null;
            }

            var array = new Texture2DArray(maxW, maxH, sliceCount, TextureFormat.RGBA32, mipChain: true)
            {
                name = "TerrainDetailArray",
                wrapMode = TextureWrapMode.Repeat,
                filterMode = FilterMode.Bilinear,
            };
            for (int i = 0; i < sliceCount; i++)
            {
                Color32[] pixels;
                if (slices[i] != null)
                {
                    pixels = ReadTextureRgba(slices[i], maxW, maxH);
                }
                else
                {
                    pixels = new Color32[maxW * maxH];
                    for (int p = 0; p < pixels.Length; p++)
                        pixels[p] = new Color32(128, 128, 128, 255); // neutral grey
                }
                array.SetPixels32(pixels, i);
            }
            array.Apply(updateMipmaps: true);

            string path = $"{levelDir}/TerrainDetailArray.asset";
            if (AssetDatabase.LoadAssetAtPath<Texture2DArray>(path) != null)
                AssetDatabase.DeleteAsset(path);
            AssetDatabase.CreateAsset(array, path);
            AssetDatabase.SaveAssets();
            return AssetDatabase.LoadAssetAtPath<Texture2DArray>(path) ?? array;
        }

        // Reads any texture (incl. compressed) to RGBA32 pixels at w x h via a blit.
        static Color32[] ReadTextureRgba(Texture src, int w, int h)
        {
            var rt = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32);
            Graphics.Blit(src, rt);
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tmp = new Texture2D(w, h, TextureFormat.RGBA32, mipChain: false);
            tmp.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            tmp.Apply(updateMipmaps: false);
            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(rt);
            var pixels = tmp.GetPixels32();
            UnityEngine.Object.DestroyImmediate(tmp);
            return pixels;
        }

        // Resolves the terrain material: instances the FarCryTerrain shader-graph
        // material and binds the baked albedo, detail array and surface index.
        // Falls back to the stock URP terrain material when the graph is missing.
        static Material ResolveTerrainMaterial(
            string levelDir, string levelName, Texture2D megaAlbedo,
            Texture2D splatA, Texture2D splatB, Texture2DArray detailArray,
            Vector4 detailScaleA, Vector4 detailScaleB, SceneBuildMode buildMode)
        {
            const string graphMatPath = "Assets/Materials/FarCryTerrain.mat";
            var graphMat = AssetDatabase.LoadAssetAtPath<Material>(graphMatPath);
            if (graphMat == null || graphMat.shader == null)
            {
                Debug.LogWarning(
                    $"[FcLevelSceneBuilder] '{graphMatPath}' missing; using stock URP terrain material. " +
                    "Build the FarCryTerrain shader graph for albedo+detail blending.");
                return CreateFallbackTerrainMaterial(levelDir, levelName, buildMode);
            }

            var mat = new Material(graphMat) { name = $"FarCryTerrain_{levelName}" };
            if (megaAlbedo != null)  mat.SetTexture("_MegaAlbedo", megaAlbedo);
            if (splatA != null)      mat.SetTexture("_SplatA", splatA);
            if (splatB != null)      mat.SetTexture("_SplatB", splatB);
            if (detailArray != null) mat.SetTexture("_DetailArray", detailArray);
            mat.SetVector("_DetailScaleA", detailScaleA);
            mat.SetVector("_DetailScaleB", detailScaleB);

            string matPath = $"{levelDir}/TerrainMaterial.mat";
            if (AssetDatabase.LoadAssetAtPath<Material>(matPath) != null)
                AssetDatabase.DeleteAsset(matPath);
            AssetDatabase.CreateAsset(mat, matPath);
            AssetDatabase.SaveAssets();
            return AssetDatabase.LoadAssetAtPath<Material>(matPath) ?? mat;
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

        const string WaterMaterialPath = "Assets/Shaders/Water/FarCryWater.mat";
        const string WaterShaderName = "FarCry/Water";

        static void BuildWaterPlane(Transform terrainRoot, int resolution, float metersPerSample, float waterLevel)
        {
            float size = (resolution - 1) * metersPerSample;
            var settings = FcWaterSettings.Instance;
            int cells = settings != null ? Mathf.Clamp(settings.WaveGridResolution, 2, 254) : 128;

            // Subdivided grid (P11): primitive Plane is too coarse for Gerstner vertex displacement.
            // Mesh spans 0..size in local XZ; placed so world coverage matches the terrain footprint.
            var water = new GameObject("Water");
            water.transform.SetParent(terrainRoot, worldPositionStays: false);
            water.transform.localPosition = new Vector3(0f, waterLevel, 0f);
            water.transform.localScale = Vector3.one;

            var mf = water.AddComponent<MeshFilter>();
            mf.sharedMesh = BuildWaterGridMesh(size, cells);
            var mr = water.AddComponent<MeshRenderer>();

            int waterLayer = FcWaterLayerInstaller.EnsureLayer();
            if (waterLayer >= 0)
                water.layer = waterLayer;

            FcWaterSettingsEditor.LoadOrCreateSettings();

            mr.sharedMaterial = LoadOrCreateWaterMaterial();

            var surface = water.AddComponent<FcWaterSurface>();
            var so = new SerializedObject(surface);
            so.FindProperty("waterMaterial").objectReferenceValue = mr.sharedMaterial;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        // Builds an upward-facing (cells×cells) tessellated grid in local space [0..size] on XZ.
        // Winding faces +Y under Cull Back. Index format stays 16-bit (cells clamped to 254).
        static Mesh BuildWaterGridMesh(float size, int cells)
        {
            int vpr = cells + 1;
            int vcount = vpr * vpr;
            var verts = new Vector3[vcount];
            var normals = new Vector3[vcount];
            var uvs = new Vector2[vcount];
            var tangents = new Vector4[vcount];
            float step = size / cells;

            for (int z = 0; z < vpr; z++)
            {
                for (int x = 0; x < vpr; x++)
                {
                    int idx = z * vpr + x;
                    verts[idx] = new Vector3(x * step, 0f, z * step);
                    normals[idx] = Vector3.up;
                    uvs[idx] = new Vector2((float)x / cells, (float)z / cells);
                    tangents[idx] = new Vector4(1f, 0f, 0f, -1f);
                }
            }

            var tris = new int[cells * cells * 6];
            int t = 0;
            for (int z = 0; z < cells; z++)
            {
                for (int x = 0; x < cells; x++)
                {
                    int bl = z * vpr + x;
                    int br = bl + 1;
                    int tl = bl + vpr;
                    int tr = tl + 1;
                    tris[t++] = bl; tris[t++] = tl; tris[t++] = br;
                    tris[t++] = br; tris[t++] = tl; tris[t++] = tr;
                }
            }

            var mesh = new Mesh { name = "FcWaterGrid" };
            mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt16;
            mesh.vertices = verts;
            mesh.normals = normals;
            mesh.uv = uvs;
            mesh.tangents = tangents;
            mesh.triangles = tris;
            mesh.RecalculateBounds();
            return mesh;
        }

        static Material LoadOrCreateWaterMaterial()
        {
            var existing = AssetDatabase.LoadAssetAtPath<Material>(WaterMaterialPath);
            if (existing != null) return existing;

            var shader = Shader.Find(WaterShaderName);
            if (shader == null)
            {
                Debug.LogWarning($"[FcLevelSceneBuilder] Shader '{WaterShaderName}' not found; falling back to URP/Lit.");
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

        static void ApplyCryMatrix34(Transform t, float[] m)
            => FcLevelLoader.ApplyBrushMatrix34(t, m);

        // ── Shared entity/object build ────────────────────────────────────────────

        static BuildStats BuildMission(
            FcMissionDesc mission,
            FcEntityPrefabRegistry registry,
            GameObject entityRoot,
            GameObject objectRoot,
            GameObject volumeRoot,
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

            // Pass 2: volume objects (VisArea / Portal / OccluderArea / FogVolume / WaterVolume)
            if (mission.LevelObjects != null)
            {
                foreach (var levelObject in mission.LevelObjects)
                {
                    if (!TryBuildVolume(levelObject, volumeRoot)) continue;
                    stats.Volumes++;
                }
            }

            // Pass 3: generic objects (generic LevelObjects first; legacy fallback kept for safety)
            if (mission.LevelObjects != null && mission.LevelObjects.Count > 0)
            {
                foreach (var levelObject in mission.LevelObjects)
                {
                    if (IsVolumeType(levelObject.Type)) continue;

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

        static bool IsVolumeType(string type) =>
            type != null && (
            type.Equals("VisArea",      StringComparison.OrdinalIgnoreCase) ||
            type.Equals("Portal",       StringComparison.OrdinalIgnoreCase) ||
            type.Equals("OccluderArea", StringComparison.OrdinalIgnoreCase) ||
            type.Equals("FogVolume",    StringComparison.OrdinalIgnoreCase) ||
            type.Equals("WaterVolume",  StringComparison.OrdinalIgnoreCase));

        // Returns true if the object was handled as a volume; false if caller should skip.
        static bool TryBuildVolume(FcLevelObjectDesc src, GameObject volumeRoot)
        {
            if (!IsVolumeType(src.Type)) return false;

            var go = new GameObject(string.IsNullOrEmpty(src.Name) ? src.Type : src.Name);
            go.transform.SetParent(volumeRoot.transform, worldPositionStays: false);
            go.transform.position = src.Pos;

            var pts = src.ShapePoints.Count > 0 ? src.ShapePoints.ToArray() : null;
            var attr = src.Attributes;

            if (src.Type.Equals("VisArea", StringComparison.OrdinalIgnoreCase))
            {
                var v = go.AddComponent<FcVisAreaVolume>();
                v.Points          = pts;
                v.Height          = ParseAttrFloat(attr, "Height", 3f);
                v.AmbientColor    = ParseAttrColor(attr, "AmbientColor", Color.gray);
                v.DynAmbientColor = ParseAttrColor(attr, "DynAmbientColor", Color.gray);
                v.AffectedBySun   = ParseAttrBool(attr, "AffectedBySun");
                v.SkyOnly         = ParseAttrBool(attr, "SkyOnly");
                v.ViewDistRatio   = (int)ParseAttrFloat(attr, "ViewDistRatio", 100f);
                v.Closed          = ParseAttrBool(attr, "Closed", defaultTrue: true);
            }
            else if (src.Type.Equals("Portal", StringComparison.OrdinalIgnoreCase))
            {
                var v = go.AddComponent<FcPortalVolume>();
                v.Points        = pts;
                v.Height        = ParseAttrFloat(attr, "Height", 2f);
                v.DoubleSide    = ParseAttrBool(attr, "DoubleSide", defaultTrue: true);
                v.AmbientColor  = ParseAttrColor(attr, "AmbientColor", Color.gray);
            }
            else if (src.Type.Equals("OccluderArea", StringComparison.OrdinalIgnoreCase))
            {
                var v = go.AddComponent<FcOccluderAreaVolume>();
                v.Points        = pts;
                v.Height        = ParseAttrFloat(attr, "Height", 5f);
                v.UseInIndoors  = ParseAttrBool(attr, "UseInIndoors");
            }
            else if (src.Type.Equals("FogVolume", StringComparison.OrdinalIgnoreCase))
            {
                var v = go.AddComponent<FcFogVolume>();
                v.FogColor      = ParseAttrColor(attr, "Color", Color.white);
                v.ViewDistance  = ParseAttrFloat(attr, "ViewDistance", 50f);
                v.Width         = ParseAttrFloat(attr, "Width", 1f);
                v.Height        = ParseAttrFloat(attr, "Height", 1f);
                v.Length        = ParseAttrFloat(attr, "Length", 1f);
            }
            else if (src.Type.Equals("WaterVolume", StringComparison.OrdinalIgnoreCase))
            {
                var v = go.AddComponent<FcWaterVolume>();
                v.Points        = pts;
                v.Height        = ParseAttrFloat(attr, "Height", -1f);
                attr.TryGetValue("Material",    out v.Material);
                attr.TryGetValue("WaterShader", out v.WaterShader);
                v.WaterSpeed    = ParseAttrFloat(attr, "WaterSpeed", 0f);
            }

            return true;
        }

        static float ParseAttrFloat(
            System.Collections.Generic.Dictionary<string, string> attrs,
            string key, float fallback)
        {
            if (attrs.TryGetValue(key, out string s) &&
                float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out float v))
                return v;
            return fallback;
        }

        static bool ParseAttrBool(
            System.Collections.Generic.Dictionary<string, string> attrs,
            string key, bool defaultTrue = false)
        {
            if (!attrs.TryGetValue(key, out string s)) return defaultTrue;
            return s == "1" || s.Equals("true", StringComparison.OrdinalIgnoreCase);
        }

        static Color ParseAttrColor(
            System.Collections.Generic.Dictionary<string, string> attrs,
            string key, Color fallback)
        {
            if (!attrs.TryGetValue(key, out string s)) return fallback;
            var parts = s.Split(',');
            if (parts.Length < 3) return fallback;
            if (float.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out float r) &&
                float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float g) &&
                float.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out float b))
                return new Color(r, g, b);
            return fallback;
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
            => FcLevelLoader.ApplyEntityTransform(t, t.position, cryAngles, scale);

        static int BuildMovieSequencePlaceholders(string levelName, GameObject levelRoot)
        {
            var sequences = FcLevelLoader.LoadMovieSequences(levelName);
            if (sequences == null || sequences.Count == 0)
                return 0;

            var seqRoot = new GameObject("Sequences");
            seqRoot.transform.SetParent(levelRoot.transform, worldPositionStays: false);

            foreach (var seq in sequences)
            {
                var go = new GameObject(seq.Name);
                go.transform.SetParent(seqRoot.transform, worldPositionStays: false);
                var placeholder = go.AddComponent<FcMovieSequencePlaceholder>();
                placeholder.StartTime = seq.StartTime;
                placeholder.EndTime   = seq.EndTime;
                placeholder.NodeCount = seq.NodeCount;
            }

            Debug.Log($"[FcLevelSceneBuilder] Movie sequences: {sequences.Count} placeholders built for '{levelName}'.");
            return sequences.Count;
        }

        static int BuildEditorXmlLights(string levelName, FcMissionDesc mission, GameObject entityRoot)
        {
            var editorLights = FcLevelLoader.LoadEditorXmlDynamicLights(levelName);
            if (editorLights == null || editorLights.Count == 0)
                return 0;

            // Skip lights already built from mission XML (same EntityId).
            var missionLightIds = new HashSet<int>();
            if (mission?.Entities != null)
            {
                foreach (var e in mission.Entities)
                    if (e.EntityClass == "DynamicLight" && e.Id > 0)
                        missionLightIds.Add(e.Id);
            }

            int built = 0;
            foreach (var desc in editorLights)
            {
                if (desc.Id > 0 && missionLightIds.Contains(desc.Id))
                    continue;
                BuildDynamicLightEntity(desc, entityRoot);
                built++;
            }

            Debug.Log($"[FcLevelSceneBuilder] Editor XML lights: {built} built from {editorLights.Count} total (skipped {editorLights.Count - built} already in mission).");
            return built;
        }

        static GameObject BuildDynamicLightEntity(FcEntityDesc desc, GameObject entityRoot)
        {
            var go = new GameObject(string.IsNullOrEmpty(desc.Name) ? $"DynamicLight_{desc.Id}" : desc.Name);
            go.transform.SetParent(entityRoot.transform, false);
            SetTransform(go.transform, desc.Pos, desc.Angles, desc.Scale);

            var p = desc.Properties;

            bool active  = !p.TryGetValue("bActive",    out var act)  || act  == "1";
            bool isFake  =  p.TryGetValue("bFakeLight", out var fake) && fake == "1";

            go.SetActive(active);

            if (isFake)
                return go;

            var light = go.AddComponent<Light>();

            bool hasProjectorTex = p.TryGetValue("texture_ProjectorTexture", out var tex) && !string.IsNullOrEmpty(tex);
            bool projectAll      = p.TryGetValue("bProjectInAllDirs", out var piad) && piad == "1";
            bool isSpot          = hasProjectorTex && !projectAll;
            light.type = isSpot ? LightType.Spot : LightType.Point;

            if (p.TryGetValue("clrDiffuse", out var clrStr) && TryParseFloats(clrStr, out float r, out float g, out float b))
            {
                float mult = p.TryGetValue("DiffuseMultiplier", out var dm) && TryParseF(dm, out float dmf) ? dmf : 1f;
                light.color     = new Color(r, g, b);
                light.intensity = mult;
            }

            if (p.TryGetValue("OuterRadius", out var rad) && TryParseF(rad, out float radius))
                light.range = radius;

            if (isSpot && p.TryGetValue("ProjectorFov", out var fov) && TryParseF(fov, out float fovF))
                light.spotAngle = fovF;

            bool castShadowMaps = desc.RootAttributes.TryGetValue("CastShadowMaps", out var csm) && csm == "1";
            light.shadows = (desc.CastShadows || castShadowMaps) ? LightShadows.Soft : LightShadows.None;

            bool usedInRealTime = !p.TryGetValue("bUsedInRealTime", out var rt) || rt == "1";
            light.lightmapBakeType = usedInRealTime ? LightmapBakeType.Mixed : LightmapBakeType.Baked;

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
            public int Volumes;
            public int Brushes;
            public int Vegetation;
            public int Lights;
            public int Sequences;
            public int Skipped;
            public int Unknown;

            public override string ToString() =>
                $"Entities: {Entities}  Objects: {Objects}  Volumes: {Volumes}  Brushes: {Brushes}  Vegetation: {Vegetation}  Lights: {Lights}  Sequences: {Sequences}  Skipped: {Skipped}  Unknown: {Unknown}";
        }
    }
}
