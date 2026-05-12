# Level Content Import Refactor Plan

Snapshot: 2026-05-12. Scope: level filling only. No gameplay logic, no AI behavior, no scripting runtime.

## Goal

Import full visible/structural level content from original Far Cry level data.

Target scene contains:

- terrain height/texture/water;
- brushes with material overrides, LOD, collider/proxy split, optional lightmap hooks;
- vegetation/static object instances from `objects.lst`;
- mission XML entities as typed placeholders/components;
- vis/portal/occluder/fog/water volumes;
- environment, lights, sound emitters, particles, cameras, triggers, markers;
- materials library and surface types enough for visuals/colliders/audio tags;
- layout data cache preserving all source records.

Non-goal:

- NPC AI, weapon gameplay, mission script logic, save/load logic, network;
- exact Cry renderer backend;
- runtime lightmap baking.

## TODO Board

- [x] Phase 1: Inventory + lossless parse (`FcLevelLayoutDataV2`, supplement payload, validation/import report).
- [x] Phase 2: Terrain skeleton (`land_map.h16`, collider, water level, `cover_low.dds` fallback, alignment pass).
- [ ] Phase 3: Materials + surface base.
- [ ] Phase 4: Brush completion.
- [ ] Phase 5: Vegetation / static objects.
- [ ] Phase 6: Structural objects + volumes.
- [ ] Phase 7: Entity content classes.
- [ ] Phase 8: Static lights + lightmap hooks.
- [ ] Phase 9: Particles, music, movie placeholders.
- [ ] Phase 10: Validation + tooling.

## Source Map

Level PAK common files:

- `mission_<name>.xml`: entities, objects, environment, volumes.
- `leveldata.xml`: surface types, vegetation type definitions, materials refs.
- `materials.xml`: level material library.
- `brush.lst`: static brush instances + geom refs + material overrides.
- `objects.lst`: vegetation/static object instances.
- `terrain/land_map.h16`: 1024x1024 ushort heightmap in stock levels.
- `terrain/cover.ctc`: terrain texture tiles.
- `terrain/cover_low.dds`: low LOD terrain cover texture.
- `particles.lst`: exported particle library/list.
- `moviedata.xml`: TrackView/camera/sound/event sequences.
- `music/*.xml`: music pattern data.
- `net*.bai`, `hide*.bai`: AI/nav/hide data. Keep parsed later, not content-first.

Cry source refs:

- `Cry3DEngine/3dEngineLoad.cpp`: load order: terrain, materials, env, static lights, brushes, vegetation.
- `Cry3DEngine/terrain_load.cpp`: `objects.lst` load into vegetation instances.
- `Cry3DEngine/terrain.h`: `CStatObjInstForLoading` layout.
- `Cry3DEngine/Brush.cpp`: `brush.lst` runtime load, materials, flags, lightmaps.
- `Cry3DEngine/VisAreaMan.cpp`: `VisArea`, `Portal`, `OccluderArea` XML load.
- `Cry3DEngine/MatMan.cpp`: `materials.xml` / `MaterialsLibrary`.
- `Editor/GameExporter.cpp`: exported level package list.
- `docs/unity-architecture.md`, `docs/unity-entities.md`, `docs/Scripts.md`: Unity target split.

## Current State

Done/partial:

- VFS mounts FCData + level PAK.
- Mission XML parses `Entity`, basic `Object`.
- Environment parses sun/fog/ambient subset.
- `brush.lst` parses enough for brush placeholders.
- Brush runtime CGF load works via services.
- DynamicLight -> Unity `Light`.
- SoundSpot -> Unity `AudioSource`.
- Entity prefab registry/stubs exist.
- Layout asset stores mission + brush subset.

Big gaps:

- no terrain mesh/import;
- no terrain texture/surface import;
- no vegetation from `objects.lst`;
- no `leveldata.xml` vegetation type/surface/material import;
- no vis/portal/occluder/fog/water volume builders;
- no `materials.xml` level material library pipeline;
- no `particles.lst` import;
- no generic entity property-preserving typed placeholders;
- no full object type coverage;
- no static light/lightmap data model;
- no `moviedata.xml` camera/sequence placeholder import;
- layout cache drops many object attributes.

## Coverage Numbers

Across stock level missions, object types found:

- `TagPoint` 2372
- `AIAnchor` 2304
- `VisArea` 989
- `Group` 891
- `Shape` 716
- `ForbiddenArea` 669
- `OccluderArea` 406
- `AreaBox` 329
- `Portal` 213
- `Respawn` 152
- `AINavigationModifier` 131
- `FogVolume` 73
- `AIPath` 46
- `WaterVolume` 22
- `AIHorizontalOcclusionPlane` 12
- `AreaSphere` 6

Top entity classes found:

- `BasicEntity` 2408
- `SoundSpot` 1280
- `DestroyableObject` 612
- `MercCover` 605
- `ProximityTrigger` 601
- `DynamicLight` 471
- `AreaTrigger` 433
- `DelayTrigger` 259
- `ParticleEffect` 231
- `MercScout` 226
- `RandomAmbientSoundPreset` 216
- `AutomaticDoor` 185
- `Grunt` 174
- `MissionHint` 169
- `Health` 160

Meaning: content import cannot be registry stubs only. Need preserve all XML and build structural groups first.

## Target Architecture

```text
FcLevelContentImportService
  -> FcLevelPackageIndex
  -> FcMissionXmlParser
  -> FcLevelDataXmlParser
  -> FcMaterialsXmlParser
  -> FcTerrainImportService
  -> FcBrushImportService
  -> FcVegetationImportService
  -> FcObjectVolumeImportService
  -> FcEntityContentImportService
  -> FcEnvironmentImportService
  -> FcLayoutDataWriter
```

Rules:

- Parse source -> data records first. Build Unity objects second.
- Preserve unknown attributes in dictionaries, not lost.
- No special-case exact classes unless behavior differs structurally.
- Builder emits stable hierarchy and labels.
- Runtime loading stays service-owned.
- Editor build and layout rebuild use same data records.
- Detailed logging/audit reports are deferred to late validation phases or enabled only when diagnosing issues.

## Data Model

Add/expand:

- `FcLevelContentData`: full level package manifest + parsed records.
- `FcTerrainDesc`: heightmap path, size, height scale, water level, cover texture refs, surface ids.
- `FcVegetationTypeDesc`: id, model path, material, sprite/brightness/bending/collision flags.
- `FcVegetationInstanceDesc`: type id, position, scale, brightness.
- `FcMaterialLibraryDesc`: material names, shader names, texture refs, flags.
- `FcLevelObjectDesc`: generic object attrs + typed extras, not only `Shape`/`AreaBox`.
- `FcVolumeDesc`: shape/box/polygon, height, colors, flags.
- `FcEntityDesc`: keep all root attrs, `Properties`, `Properties2`, nested property path attrs.
- `FcStaticLightDesc`: from `bUsedInRealTime=0` DynamicLight and optional `StatLights.dat` if present.
- `FcMovieSequenceDesc`: sequence metadata placeholders from `moviedata.xml`.

## Import Phases

### [x] Phase 1. Inventory + Lossless Parse

Implemented package index + parsers (data-first, no mandatory early logs).

- List known level files from mounted PAK.
- Parse mission XML with generic root attrs.
- Parse all `Object` types into `FcLevelObjectDesc`.
- Parse `leveldata.xml` surface types + vegetation object definitions.
- Parse `materials.xml` enough to name/link materials.
- Parse `objects.lst` using `CStatObjInstForLoading`: `ushort x/y/z`, `byte type`, `byte brightness`, `float scale`.
- Added `FcLevelLayoutDataV2` preserving full records.

Success:

- per-level file/entity/object/attr counts are preserved in layout data (no mandatory early log output).
- mission XML entities/objects are preserved with generic attrs (`FcLevelObjectDesc` + legacy projection).
- `leveldata.xml`/`materials.xml`/`objects.lst` data is captured into V2 supplement payload.
- coverage + validation metrics are embedded in V2 `ImportReport`/`Validation` (no console-report dependency).
- scene rebuild path switched to V2.

### [x] Phase 2. Terrain Skeleton

Build Unity terrain/mesh from `terrain/land_map.h16`.

- Decode 1024x1024 ushort heights.
- Convert Cry level coords `(x,y,z)` -> Unity `(x,z,y)`.
- Create chunked terrain meshes or Unity `TerrainData`.
- Apply water level from environment/leveldata.
- Attach collider.
- Use `cover_low.dds` as first visual fallback.

Defer:

- exact `cover.ctc` tile decode;
- detail material splats.

Success:

- terrain aligns with brushes/entities.
- water plane at correct height.
- height samples match Cry positions from vegetation/objects.

### [ ] Phase 3. Materials + Surface Base

Make level material resolver.

- Load `materials.xml` + mission/level `MaterialsLibrary`.
- Map Cry material names to Unity materials.
- Resolve terrain/brush/entity material overrides.
- Preserve surface type id/name for colliders/audio later.

Success:

- brush material overrides work by material name/id.
- unknown shader/material diagnostics are deferred to validation/audit stage.

### [ ] Phase 4. Brush Completion

Finish brush parity.

- Preserve all `brush.lst` fields: id, matrix, flags, view ratio, LOD ratio, merge id, material id.
- Add lightmap flag model: `ERF_USELIGHTMAPS` marker, optional future `LM_EXPORT_FILE_NAME`.
- Add merge group metadata but do not merge until validated.
- Keep proxy/no-draw visual strip + collider extraction.

Success:

- brush count/model refs/material refs match Cry load.
- visual/collider alignment validated with terrain.

### [ ] Phase 5. Vegetation / Static Objects

Import `objects.lst` + `leveldata.xml` vegetation definitions.

- Build `VegetationTypes` from `<Vegetation><Object ... Index=... FileName=...>`.
- Instantiate type refs from `objects.lst`.
- Apply position, scale, brightness.
- Use batching/LOD/instancing path, not one heavy GO per final version.
- Start simple: placeholders with mesh loading service; then batch.

Success:

- instance count per type matches `objects.lst`.
- missing model refs are tracked for validation/audit stage (not mandatory early logs).
- terrain z fallback for `z == 0` matches Cry.

### [ ] Phase 6. Structural Objects + Volumes

Build all mission `Object` types.

- Markers: `TagPoint`, `AIAnchor`, `AIPath`, `Respawn`, `Group`.
- Areas: `Shape`, `AreaBox`, `AreaSphere`, `ForbiddenArea`, `AINavigationModifier`.
- Visibility: `VisArea`, `Portal`, `OccluderArea`.
- Atmosphere: `FogVolume`, `WaterVolume`.
- Debug geometry optional; components store data always.

Success:

- object type counts match source.
- volumes visible in editor gizmos.
- portals/vis areas have connectivity-ready data.

### [ ] Phase 7. Entity Content Classes

No gameplay. Content-only components.

- Mesh-bearing: `BasicEntity`, `DestroyableObject`, `BreakableObject`, `RigidBody`, doors, pickups, vehicles, weapons.
- Character placeholders: NPC class, model, equipment props, no AI.
- Lights: `DynamicLight`, classify runtime/static/fake/projector.
- Audio: all sound classes as emitters/presets/placeholders.
- Particles: `ParticleEffect`, `ParticleSpray`, `BFly`, `Grasshopper`.
- Camera: `CameraSource`, `CameraTargetPoint`.
- Triggers: components with shape/radius/links, no action execution.

Success:

- top 100 entity classes resolve to typed content component or explicit stub class.
- no missing prefab warning for known content classes.
- all properties preserved.

### [ ] Phase 8. Static Lights + Lightmap Hooks

Represent baked light sources.

- `DynamicLight.Properties.bUsedInRealTime=0` -> `FcStaticLightMarker` + optional disabled Unity preview light.
- `bFakeLight`, `bFakeRadiosity`, projector texture, shader, style preserved.
- Search/load `StatLights.dat` if present in non-stock/custom levels.
- Add future hook for brush lightmap metadata.

Success:

- static vs realtime light diagnostics deferred to validation/audit stage.
- baked lights visible as gizmos/optional preview.

### [ ] Phase 9. Particles, Music, Movie Placeholders

Data-first import.

- `particles.lst`: parse or at least index file presence/version; map to particle entity names.
- `moviedata.xml`: import sequence/camera/event placeholders.
- `music/*.xml`: import music region/theme metadata, no playback.

Success:

- scene can show cameras and sequence anchors.
- missing runtime implementation explicit, not silent.

### [ ] Phase 10. Validation + Tooling

Add audit tools.

- `OpenFarCry/Level/Audit Current Level`.
- Reports: counts by file, object type, entity class, missing assets, unknown attrs, parser errors.
- Golden tests using small synthetic XML/binary fixtures.
- Visual checklist per level: terrain, brushes, vegetation, volumes, lights, sounds, particles.

Success:

- `Training`, `Fort`, `Pier`, one indoor-heavy level, one MP level pass count audit.

## Refactor Files

Add:

- `Assets/Scripts/Level/Data/FcLevelContentData.cs`
- `Assets/Scripts/Level/Data/FcLevelPackageIndex.cs`
- `Assets/Scripts/Level/Data/FcLevelDataXmlParser.cs`
- `Assets/Scripts/Level/Data/FcMaterialsXmlParser.cs`
- `Assets/Scripts/Level/Data/FcVegetationLoader.cs`
- `Assets/Scripts/Level/Data/FcTerrainDataLoader.cs`
- `Assets/Scripts/Level/Editor/FcLevelContentSceneBuilder.cs`
- `Assets/Scripts/Level/Editor/FcLevelAuditWindow.cs`
- `Assets/Scripts/Level/Entities/FcLevelVolume.cs`
- `Assets/Scripts/Level/Entities/FcVegetationInstance.cs`
- `Assets/Scripts/Level/Entities/FcStaticLightMarker.cs`

Modify:

- `FcLevelLoader`: become mission parser facade or split into parsers.
- `FcLevelSceneBuilder`: delegate to content builders.
- `FcLevelLayoutData`: v2/full records or replacement.
- `FcEntityPrefabRegistry`: stop exact special-case leak; map content families.
- `FcLevelEnvironment`: add water/ocean/fog extras.

## Priority

- [x] Lossless parse + audit.
- [x] Terrain skeleton.
- [ ] Vegetation.
- [ ] Volumes.
- [ ] Entity content mapping.
- [ ] Materials refinement.
- [ ] Static light markers.
- [ ] Movie/music/particle metadata.

Reason: terrain + vegetation + volumes are biggest missing level filling, and validate coordinate basis fastest.

## Risks

- `cover.ctc` unknown/complex. Start with `cover_low.dds`.
- Unity `Terrain` coordinate/scale may fight existing basis. Prefer chunked mesh if alignment easier.
- Vegetation count high. Need batching soon.
- Existing generated scenes dirty. Avoid scene churn until importer stable.
- Entity classes huge. Need data-preserving stubs before behavior.
- `StatLights.dat` absent in stock PAKs checked. Static light source may mainly be `DynamicLight bUsedInRealTime=0`.

## Done Definition

Full content import v1 done when:

- [x] all known level package files indexed;
- [x] terrain visible/collidable;
- [ ] brushes and vegetation visible;
- [ ] all mission objects represented;
- [ ] top stock entity classes represented as content components/stubs;
- [ ] lights/sounds/particles/cameras represented;
- [ ] audit counts match source for selected levels;
- [ ] no source attrs dropped without report.
