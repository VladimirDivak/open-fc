# Level Content Import Refactor Plan

Snapshot: 2026-05-13. Scope: runtime level loading into scene first. No gameplay/AI/scripting.

## Goal

Full visible/structural level content from Far Cry level data:
terrain h/tex/water · brushes (mat overrides, LOD, collider/proxy, lightmap hooks) · vegetation/static from `objects.lst` · mission XML entities as typed placeholders · vis/portal/occluder/fog/water volumes · environment/lights/sound/particles/cameras/triggers/markers · material lib + surface types · layout data cache.

Non-goal: NPC AI, weapon gameplay, mission scripts, save/load, network, exact Cry renderer, runtime lightmap baking.

## TODO Board

- [x] Phase 1: Inventory + lossless parse
- [~] Phase 2: Terrain skeleton
- [~] Phase 3: Materials + surface base
- [~] Phase 4: Brush completion
- [~] Phase 5: Vegetation / static objects
- [ ] Phase 6: Structural objects + volumes
- [ ] Phase 7: Entity content classes
- [ ] Phase 8: Static lights + lightmap hooks
- [ ] Phase 9: Particles, music, movie placeholders
- [~] Phase 10: Validation + tooling

## Source Map

Level PAK files:
- `mission_<name>.xml` — entities, objects, environment, volumes
- `leveldata.xml` — surface types, vegetation type defs, material refs
- `materials.xml` — level material library
- `brush.lst` — static brush instances + geom refs + mat overrides
- `objects.lst` — vegetation/static object instances
- `terrain/land_map.h16` — 1024×1024 ushort heightmap
- `terrain/cover.ctc` — terrain texture tiles
- `terrain/cover_low.dds` — low LOD terrain cover
- `particles.lst` — exported particle library
- `moviedata.xml` — TrackView/camera/sound/event sequences
- `music/*.xml` — music pattern data
- `net*.bai`, `hide*.bai` — AI/nav/hide data (deferred)

Cry source refs:
- `Cry3DEngine/3dEngineLoad.cpp` — load order: terrain→mats→env→static lights→brushes→vegetation
- `Cry3DEngine/terrain_load.cpp` — objects.lst → vegetation instances
- `Cry3DEngine/terrain.h` — `CStatObjInstForLoading` layout
- `Cry3DEngine/Brush.cpp` — brush.lst runtime load, mats, flags, lightmaps
- `Cry3DEngine/VisAreaMan.cpp` — VisArea/Portal/OccluderArea XML
- `Cry3DEngine/MatMan.cpp` — materials.xml / MaterialsLibrary
- `Editor/GameExporter.cpp` — exported level package list

## Current State

Runtime path that actually exists now:
- `FcLevelLoadService.LoadLevelAsync()` loads `mission_<name>.xml`, `brush.lst`, and `FcLevelSupplementLoader` payload at runtime.
- Runtime builds a geometry preload plan for brushes + vegetation, dedupes base/LOD requests, preloads CGF models, and preloads their textures before live component loads start.
- Runtime keeps preloaded geometry handles keyed by normalized virtual path so `FcBrushLoadService` and `FcVegetationLoadService` can reuse already-imported assets.
- `FcEntityLoadService` already has queueing, deferred/background split, near-camera promotion, scoped cancellation, unload draining, and runtime reporting.
- Brush runtime parity work is in progress and already partially wired through shared `FcBrushGeometryPostProcessor`.

What is still editor-built or scene-authored rather than runtime-built-from-source:
- Terrain mesh/water plane generation exists in `FcLevelSceneBuilder`, not in `FcLevelLoadService`.
- Mission objects/entities are still instantiated by editor scene build / prefab placement; runtime currently enqueues already-existing `FcMeshEntity` components from the scene instead of constructing scene content from parsed source data.
- `DynamicLight` and `SoundSpot` source-to-Unity mapping exists in `FcLevelSceneBuilder`, not in runtime source build.
- Vegetation source parsing exists, and runtime can preload/reuse vegetation geometry, but scene instance creation still comes from editor-built `FcVegetationInstance` components.

Data/model work already in place:
- `FcLevelLayoutDataV2` is the active cache format.
- `FcLevelSupplementLoader` indexes package entries and parses known-file presence, surface types, material library refs, `materials.xml`, vegetation types, and `objects.lst` vegetation instances.
- Mission parsing preserves generic root/object attributes and carries `LevelObjects`.
- Brush parsing preserves `MaterialId`, `Flags`, `MergeId`, `ViewDistRatio`.
- Import/validation counters are stored in V2 data instead of being emitted as early logs.

Big runtime gaps:
- no runtime scene builder that instantiates terrain, brushes, vegetation, objects, and entities directly from parsed records
- no runtime terrain construction from `land_map.h16` / water level / cover fallback
- no runtime object/volume builders for `VisArea`, `Portal`, `OccluderArea`, `FogVolume`, `WaterVolume`, `Shape`, `AreaBox`, etc.
- no runtime application of parsed level material library / surface types to spawned content
- no runtime source-driven creation for top entity classes beyond whatever was baked into the scene
- no static light/lightmap source model in runtime path
- no runtime placeholder import for `particles.lst`, `moviedata.xml`, `music/*.xml`

## Coverage Numbers

Object types (stock missions):
`TagPoint` 2372 · `AIAnchor` 2304 · `VisArea` 989 · `Group` 891 · `Shape` 716 · `ForbiddenArea` 669 · `OccluderArea` 406 · `AreaBox` 329 · `Portal` 213 · `Respawn` 152 · `AINavigationModifier` 131 · `FogVolume` 73 · `AIPath` 46 · `WaterVolume` 22 · `AIHorizontalOcclusionPlane` 12 · `AreaSphere` 6

Top entity classes:
`BasicEntity` 2408 · `SoundSpot` 1280 · `DestroyableObject` 612 · `MercCover` 605 · `ProximityTrigger` 601 · `DynamicLight` 471 · `AreaTrigger` 433 · `DelayTrigger` 259 · `ParticleEffect` 231 · `MercScout` 226 · `RandomAmbientSoundPreset` 216 · `AutomaticDoor` 185 · `Grunt` 174 · `MissionHint` 169 · `Health` 160

→ Content import cannot be registry stubs only. Need data-preserving stubs before behavior.

## Target Architecture

```
FcLevelContentImportService
  FcLevelPackageIndex · FcMissionXmlParser · FcLevelDataXmlParser · FcMaterialsXmlParser
  FcTerrainImportService · FcBrushImportService · FcVegetationImportService
  FcObjectVolumeImportService · FcEntityContentImportService
  FcEnvironmentImportService · FcLayoutDataWriter
```

Rules: parse→data records first, build Unity objects second. Preserve unknown attrs in dicts. No special-case exact classes unless behavior differs structurally. Builder emits stable hierarchy. Runtime loading stays service-owned. Editor build + layout rebuild use same data records.

## Data Model

Add/expand:
- `FcLevelContentData` — full level package manifest + parsed records
- `FcTerrainDesc` — heightmap path/size/scale, water level, cover tex refs, surface ids
- `FcVegetationTypeDesc` — id, model path, material, sprite/brightness/bending/collision flags
- `FcVegetationInstanceDesc` — type id, position, scale, brightness
- `FcMaterialLibraryDesc` — material names, shader names, tex refs, flags
- `FcLevelObjectDesc` — generic object attrs + typed extras
- `FcVolumeDesc` — shape/box/polygon, height, colors, flags
- `FcEntityDesc` — all root attrs, Properties, Properties2, nested property path attrs
- `FcStaticLightDesc` — from bUsedInRealTime=0 DynamicLight + StatLights.dat if present
- `FcMovieSequenceDesc` — sequence metadata placeholders from moviedata.xml

## Import Phases

### [x] Phase 1. Inventory + Lossless Parse
List known level files from mounted PAK. Parse mission XML + generic root attrs. Parse all Object types → FcLevelObjectDesc. Parse leveldata.xml surface types + vegetation defs. Parse materials.xml (name/link). Parse objects.lst (CStatObjInstForLoading: ushort x/y/z, byte type/brightness, float scale). FcLevelLayoutDataV2 preserving full records. Per-level counts preserved. V2 supplement payload captures leveldata/materials/objects.lst. Coverage+validation metrics in V2 ImportReport.

Status:
- Done for data/cache side.
- Runtime scene consumption not started.

### [~] Phase 2. Terrain Skeleton
Decode 1024×1024 ushort heights from land_map.h16. Cry level coords (x,y,z)→Unity (x,z,y). Chunked terrain meshes or Unity TerrainData. Water level from environment/leveldata. Collider attached. cover_low.dds as first visual fallback.
Deferred: exact cover.ctc tile decode; detail material splats.
Success: terrain aligns with brushes/entities; water plane at correct height; height samples match Cry vegetation positions.

Status:
- Editor scene builder has terrain skeleton, collider, fallback material, and water plane.
- Runtime source load only parses terrain settings/supplement; it does not build terrain into the scene.

### [~] Phase 3. Materials + Surface Base
Load materials.xml + mission/level MaterialsLibrary. Map Cry mat names → Unity materials. Resolve terrain/brush/entity mat overrides. Preserve surface type id/name for colliders/audio.
Success: brush mat overrides work by name/id; unknown shader diagnostics deferred to audit stage.

Status:
- Parsed/material data exists in supplement + V2 report.
- Runtime texture preload for CGF results exists.
- Missing: runtime application of level material library / surface types to spawned terrain, brushes, and entities.

### [~] Phase 4. Brush Completion
Preserve all brush.lst fields: id, matrix, flags, view ratio, LOD ratio, merge id, material id. Add lightmap flag model (ERF_USELIGHTMAPS marker, optional future LM_EXPORT_FILE_NAME). Add merge group metadata (no actual merge until validated). Keep proxy/no-draw visual strip + collider extraction.
Success: brush count/model refs/mat refs match Cry load; visual/collider alignment validated with terrain.

Status:
- Metadata preservation is largely in place.
- Runtime brush loading, runtime LOD preload, texture preload reuse, and shared post-processing exist.
- Missing: runtime construction of brush instances directly from parsed `brush.lst`; current runtime path still expects scene-authored `FcBrushInstance` components.

### [~] Phase 5. Vegetation / Static Objects
- FcVegetationInstance (path, typeIndex, instanceScale, brightness)
- FcVegetationLoadService (bounded concurrency _maxConcurrent=8, distance sort, HashSet O(1) dedup)
- BuildVegetationInstances() in FcLevelSceneBuilder; called from BuildScene() + RebuildFromLayoutData()
- Coords: X/Y (0..65535) → worldX/Z = val × terrainWorldSize / 65535; Z → worldY = val / 256 (TERRAIN_Z_RATIO)
- LOD: FindSiblingLodPaths → ImportAsync per LOD (cached via CgfRuntimeImporter) → LODGroup (0.70→0.02 thresholds)
- Textures preloaded per LOD before ApplyLoadResult
- Results ref-counted; OnDestroy releases base + all LOD results
- BuildStats.Vegetation counter + BuildVegetation phase in load report

Deferred: batching/GPU instancing.

Status:
- Parsing/model side is done.
- Runtime preload/reuse side is done.
- Runtime spawning from source records is not done; editor scene builder still creates the actual `FcVegetationInstance` GameObjects.

### [ ] Phase 6. Structural Objects + Volumes
Markers: TagPoint, AIAnchor, AIPath, Respawn, Group. Areas: Shape, AreaBox, AreaSphere, ForbiddenArea, AINavigationModifier. Visibility: VisArea, Portal, OccluderArea. Atmosphere: FogVolume, WaterVolume. Debug geometry optional; components always store data.
Success: object type counts match source; volumes visible in editor gizmos; portals/vis areas have connectivity-ready data.

### [ ] Phase 7. Entity Content Classes
Mesh-bearing: BasicEntity, DestroyableObject, BreakableObject, RigidBody, doors, pickups, vehicles, weapons. Character placeholders: NPC class/model/equipment, no AI. Lights: DynamicLight, classify runtime/static/fake/projector. Audio: all sound classes as emitters/presets/placeholders. Particles: ParticleEffect, ParticleSpray, BFly, Grasshopper. Camera: CameraSource, CameraTargetPoint. Triggers: shape/radius/links, no action execution.
Success: top 100 entity classes → typed content component or explicit stub; no missing prefab warning; all properties preserved.

### [ ] Phase 8. Static Lights + Lightmap Hooks
DynamicLight.Properties.bUsedInRealTime=0 → FcStaticLightMarker + optional disabled preview light. bFakeLight, bFakeRadiosity, projector tex, shader, style preserved. Search/load StatLights.dat if present. Add brush lightmap metadata hook.
Success: static vs realtime diagnostics deferred to audit; baked lights visible as gizmos.

### [ ] Phase 9. Particles, Music, Movie Placeholders
particles.lst: parse/index, map to particle entity names. moviedata.xml: sequence/camera/event placeholders. music/*.xml: music region/theme metadata, no playback.
Success: scene shows cameras + sequence anchors; missing runtime impl explicit.

### [~] Phase 10. Validation + Tooling
OpenFarCry/Level/Audit Current Level menu item. Reports: counts by file/object type/entity class/missing assets/unknown attrs/parser errors. Golden tests using small synthetic XML/binary fixtures. Visual checklist per level.
Success: Training, Fort, Pier, one indoor-heavy, one MP level pass count audit.

Status:
- V2 import report and validation counters exist.
- Targeted EditMode tests exist for terrain height decode and geometry preload dedupe/planning.
- Missing: dedicated runtime content audit workflow and level-by-level source-vs-runtime verification.

## Refactor Files

Add: FcLevelContentData · FcLevelPackageIndex · FcLevelDataXmlParser · FcMaterialsXmlParser · FcVegetationLoader · FcTerrainDataLoader · FcLevelContentSceneBuilder · FcLevelAuditWindow · FcLevelVolume · FcVegetationInstance ✓ · FcStaticLightMarker

Modify: FcLevelLoader (→ mission parser facade or split) · FcLevelSceneBuilder (delegate to content builders) · FcLevelLayoutData (v2/full records) · FcEntityPrefabRegistry (stop exact special-case leak; map content families) · FcLevelEnvironment (add water/ocean/fog extras)

## Priority

- [x] Lossless parse + supplement model
- [x] Runtime geometry preload foundation (brushes + vegetation)
- [ ] Runtime scene builder from parsed records
- [ ] Runtime terrain construction
- [ ] Runtime brush instantiation from `brush.lst`
- [ ] Runtime vegetation instantiation from supplement records
- [ ] Runtime objects/volumes instantiation
- [ ] Runtime entity content mapping
- [ ] Materials/surface application
- [ ] Static light markers
- [ ] Movie/music/particle metadata

## Risks

- `cover.ctc` unknown/complex → start with cover_low.dds
- Unity Terrain coordinate/scale may fight existing basis → prefer chunked mesh if alignment easier
- Vegetation count high → need batching soon
- Existing generated scenes dirty → avoid scene churn until importer stable
- Entity classes huge → need data-preserving stubs before behavior
- StatLights.dat absent in stock PAKs → static light source mainly DynamicLight bUsedInRealTime=0

## Done Definition

Full runtime content import v1:
- [x] all known level package files indexed
- [ ] runtime can build terrain visible/collidable from source data
- [ ] runtime can instantiate vegetation from parsed records (not only from prebuilt scene)
- [ ] runtime can instantiate brushes from parsed records with material overrides, LOD, collider, and parity post-process
- [ ] runtime can instantiate mission objects/volumes from parsed records
- [ ] runtime can instantiate top stock entity classes as content components/stubs from parsed records
- [ ] runtime represents lights/sounds/particles/cameras from source data
- [ ] runtime audit counts match source for selected levels
- [x] no source attrs dropped without report in the parse/cache layer
