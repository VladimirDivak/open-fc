# Level Content Import Refactor Plan

Snapshot: 2026-05-12. Scope: level filling only. No gameplay/AI/scripting.

## Goal

Full visible/structural level content from Far Cry level data:
terrain h/tex/water · brushes (mat overrides, LOD, collider/proxy, lightmap hooks) · vegetation/static from `objects.lst` · mission XML entities as typed placeholders · vis/portal/occluder/fog/water volumes · environment/lights/sound/particles/cameras/triggers/markers · material lib + surface types · layout data cache.

Non-goal: NPC AI, weapon gameplay, mission scripts, save/load, network, exact Cry renderer, runtime lightmap baking.

## TODO Board

- [x] Phase 1: Inventory + lossless parse (FcLevelLayoutDataV2, supplement payload, validation/import report)
- [x] Phase 2: Terrain skeleton (land_map.h16, collider, water level, cover_low.dds fallback, alignment)
- [ ] Phase 3: Materials + surface base
- [ ] Phase 4: Brush completion
- [x] Phase 5: Vegetation / static objects
- [ ] Phase 6: Structural objects + volumes
- [ ] Phase 7: Entity content classes
- [ ] Phase 8: Static lights + lightmap hooks
- [ ] Phase 9: Particles, music, movie placeholders
- [ ] Phase 10: Validation + tooling

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

Done:
- VFS mounts FCData + level PAK
- Mission XML parses Entity + basic Object
- Environment parses sun/fog/ambient subset
- brush.lst parses enough for placeholders; runtime CGF load works
- DynamicLight → Unity Light; SoundSpot → Unity AudioSource (editor build time)
- Entity prefab registry/stubs exist
- Layout asset stores mission + brush subset
- Vegetation: FcVegetationInstance + FcVegetationLoadService (bounded concurrency, distance sort, O(1) dedup via HashSet)
- Vegetation LOD: FindSiblingLodPaths → ImportAsync (cached) per LOD → LODGroup built on ApplyLoadResult

Big gaps:
- no terrain mesh/import; no terrain tex/surface
- no leveldata.xml vegetation type/surface/material import
- no vis/portal/occluder/fog/water volume builders
- no materials.xml level material library pipeline
- no particles.lst import
- no generic entity property-preserving typed placeholders
- no full object type coverage
- no static light/lightmap data model
- no moviedata.xml camera/sequence placeholder import
- layout cache drops many object attributes

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

### [x] Phase 2. Terrain Skeleton
Decode 1024×1024 ushort heights from land_map.h16. Cry level coords (x,y,z)→Unity (x,z,y). Chunked terrain meshes or Unity TerrainData. Water level from environment/leveldata. Collider attached. cover_low.dds as first visual fallback.
Deferred: exact cover.ctc tile decode; detail material splats.
Success: terrain aligns with brushes/entities; water plane at correct height; height samples match Cry vegetation positions.

### [ ] Phase 3. Materials + Surface Base
Load materials.xml + mission/level MaterialsLibrary. Map Cry mat names → Unity materials. Resolve terrain/brush/entity mat overrides. Preserve surface type id/name for colliders/audio.
Success: brush mat overrides work by name/id; unknown shader diagnostics deferred to audit stage.

### [ ] Phase 4. Brush Completion
Preserve all brush.lst fields: id, matrix, flags, view ratio, LOD ratio, merge id, material id. Add lightmap flag model (ERF_USELIGHTMAPS marker, optional future LM_EXPORT_FILE_NAME). Add merge group metadata (no actual merge until validated). Keep proxy/no-draw visual strip + collider extraction.
Success: brush count/model refs/mat refs match Cry load; visual/collider alignment validated with terrain.

### [x] Phase 5. Vegetation / Static Objects
- FcVegetationInstance (path, typeIndex, instanceScale, brightness)
- FcVegetationLoadService (bounded concurrency _maxConcurrent=8, distance sort, HashSet O(1) dedup)
- BuildVegetationInstances() in FcLevelSceneBuilder; called from BuildScene() + RebuildFromLayoutData()
- Coords: X/Y (0..65535) → worldX/Z = val × terrainWorldSize / 65535; Z → worldY = val / 256 (TERRAIN_Z_RATIO)
- LOD: FindSiblingLodPaths → ImportAsync per LOD (cached via CgfRuntimeImporter) → LODGroup (0.70→0.02 thresholds)
- Textures preloaded per LOD before ApplyLoadResult
- Results ref-counted; OnDestroy releases base + all LOD results
- BuildStats.Vegetation counter + BuildVegetation phase in load report

Deferred: batching/GPU instancing.

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

### [ ] Phase 10. Validation + Tooling
OpenFarCry/Level/Audit Current Level menu item. Reports: counts by file/object type/entity class/missing assets/unknown attrs/parser errors. Golden tests using small synthetic XML/binary fixtures. Visual checklist per level.
Success: Training, Fort, Pier, one indoor-heavy, one MP level pass count audit.

## Refactor Files

Add: FcLevelContentData · FcLevelPackageIndex · FcLevelDataXmlParser · FcMaterialsXmlParser · FcVegetationLoader · FcTerrainDataLoader · FcLevelContentSceneBuilder · FcLevelAuditWindow · FcLevelVolume · FcVegetationInstance ✓ · FcStaticLightMarker

Modify: FcLevelLoader (→ mission parser facade or split) · FcLevelSceneBuilder (delegate to content builders) · FcLevelLayoutData (v2/full records) · FcEntityPrefabRegistry (stop exact special-case leak; map content families) · FcLevelEnvironment (add water/ocean/fog extras)

## Priority

- [x] Lossless parse + audit
- [x] Terrain skeleton
- [x] Vegetation (base load + LOD)
- [ ] Volumes
- [ ] Entity content mapping
- [ ] Materials refinement
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

Full content import v1:
- [x] all known level package files indexed
- [x] terrain visible/collidable
- [x] vegetation visible (LOD, cached, bounded async load)
- [ ] brushes visible (material overrides, LOD, collider)
- [ ] all mission objects represented
- [ ] top stock entity classes → content components/stubs
- [ ] lights/sounds/particles/cameras represented
- [ ] audit counts match source for selected levels
- [ ] no source attrs dropped without report
