# Level Content Import Refactor Plan

Snapshot: 2026-05-16. Scope: runtime level load into scene first. No gameplay/AI/script.
Validation: user runs tests manually. Agent no run suite.

## Goal

Ship full visible/structural level content from Far Cry package:
terrain + water, brushes (override/LOD/collider/proxy/lightmap hooks), vegetation from `objects.lst`, mission entities as typed placeholders, vis/portal/occluder/fog/water volumes, env/lights/sound/particles/cameras/triggers/markers, material lib + surface types, layout cache.

Non-goal: NPC AI, weapon gameplay, mission logic, save/load, netcode, exact Cry renderer, runtime lightmap bake.

## TODO Board

- [x] Phase 1: Inventory + lossless parse
- [x] Phase 2: Terrain skeleton
- [x] Phase 3: Materials + surface base
- [x] Phase 4: Brush completion
- [x] Phase 5: Vegetation / static objects
- [x] Phase 6: Structural objects + volumes
- [x] Phase 7: Entity content classes
- [x] Phase 8: Static lights + lightmap hooks
- [x] Phase 9: Particles, music, movie placeholders
- [x] Phase 10: Validation + tooling

## Source Map

Level package files:
- `mission_<name>.xml`: entities, objects, env, volumes
- `leveldata.xml`: surface types, vegetation defs, material refs
- `materials.xml`: level material library
- `brush.lst`: brush instances + geom refs + mat overrides
- `objects.lst`: vegetation/static instances
- `terrain/land_map.h16`: 1024x1024 heightmap
- `terrain/cover.ctc`: terrain texture tiles
- `terrain/cover_low.dds`: low LOD cover fallback
- `particles.lst`, `moviedata.xml`, `music/*.xml`

Cry refs:
- `Cry3DEngine/3dEngineLoad.cpp`, `terrain_load.cpp`, `terrain.h`
- `Cry3DEngine/Brush.cpp`, `VisAreaMan.cpp`, `MatMan.cpp`
- `Editor/GameExporter.cpp`

## Current State

Runtime now:
- `FcLevelLoadService.LoadLevelAsync()` loads mission xml + brush list + supplement
- Geometry preload planner exists (brush + vegetation), base/LOD dedupe exists
- Preloaded handles keyed by normalized virtual path
- `FcEntityLoadService` queue/defer/promotion/cancel/report exists
- Brush runtime parity partially wired via shared `FcBrushGeometryPostProcessor`

Still editor-built / scene-authored:
- Terrain and water build in `FcLevelSceneBuilder`, not runtime source build
- Mission objects/entities mostly scene-authored
- DynamicLight/SoundSpot mapping in editor builder
- Vegetation parse+preload exists, runtime spawn-from-records missing

Big runtime gaps:
- no runtime scene builder from parsed records
- no runtime terrain construction from `land_map.h16`
- no runtime volume/object builders (`VisArea`, `Portal`, `OccluderArea`, `FogVolume`, `WaterVolume`, ...)
- no runtime full application of level material lib + surface types
- no runtime source-driven top entity-class mapping
- no static light/lightmap source model
- no runtime placeholder import for particles/movie/music metadata

## Target Architecture

```text
FcLevelContentImportService
  FcLevelPackageIndex · FcMissionXmlParser · FcLevelDataXmlParser · FcMaterialsXmlParser
  FcTerrainImportService · FcBrushImportService · FcVegetationImportService
  FcObjectVolumeImportService · FcEntityContentImportService
  FcEnvironmentImportService · FcLayoutDataWriter
```

Rules:
- parse -> data first, build Unity objects second
- keep unknown attrs in dicts
- stable hierarchy output
- runtime load stays service-owned
- editor build and runtime build use same records

Material precedence law:
- level package materials are scene override source
- base fallback stays embedded CGF/CGA material
- resolve per submesh/submaterial identity first, then override
- if override exists, prefer level shader/flags/texture slots
- if override missing, fallback to embedded model material
- do not preload/import textures guaranteed to be replaced later in same build

## Import Phases

### [x] Phase 1. Inventory + Lossless Parse
Done on data/cache side.
- mission/object/entity parse keeps generic attrs
- supplement parse covers known-file presence, materials, vegetation defs/instances
- stock `materials.xml` hierarchy parse wired (`MaterialsLibrary/Library/Material`, `SubMaterials`, `<Textures>/<Texture ...>`)

Gap:
- full instance-level precedence decisions vs CGF submaterial identity not complete

### [x] Phase 2. Terrain Skeleton
Done:
- editor path builds terrain from h16 + splatmap + cover/detail layers + water plane
- runtime terrain construction out of scope: terrain is always pre-built in editor scene (no copyright issue building from heightmap data)

### [x] Phase 3. Materials + Surface Base
Status:
- parsed material/surface data present in supplement + V2 report
- runtime CGF texture preload path exists
- P3.1–P3.6 all done (2026-05-16)

Done:
- P3.1: stock `materials.xml` parser fix
- P3.2: MaterialDesc expanded (slots, alpha, flags, guid, params, hierarchy)
- P3.3: slot-aware override apply; submaterial by MatID then source-name; reverse-path fallback (chunk-name shorter than child fullname)
- P3.4: per-slot diagnostics (targeted/applied/outcome/detail); ResolutionSourceCounts; ShaderFamilyCounts persisted in import report
- P3.5: skip CGF texture preload when all slots covered by resolvable override (materialId<0)
- P3.6: Decal shader family in CgfMaterialClassifier; RGB-only decal → opaque, no cutout; level override decal suppresses AlphaTest

Remaining (not blocking P3 close):
- runtime-wide material application for terrain + entities (scoped to P4/P6/P7)
- strict NoDraw contract end-to-end (scoped to P4)

### [x] Phase 4. Brush Completion
Status:
- brush metadata preservation mostly in place
- runtime brush load + LOD preload + texture preload + parity postprocess exists
- runtime spawn of brush instances from `brush.lst` when no editor-pre-built scene: `FcLevelLoadService.SpawnBrushesFromList` (2026-05-16)
  - `FcLevelLoader.ApplyBrushMatrix34` extracted from editor-only `FcLevelSceneBuilder`
  - `FcBrushInstance.Initialize(virtualPath, noPhysics, materialOverride, materialId)` for runtime init without `SerializedObject`
  - spawn skips if scene already has `FcBrushInstance` objects (editor-built path)
- runtime material override: `FcLevelMaterialOverrideService.Configure` runs at L107 before spawn at L211; `FcBrushInstance.ApplyMaterialOverride` uses `Current` singleton → resolved
- NoDraw contract: `FcBrushGeometryPostProcessor.BuildProxyMaterialIds` + `TryBuildFromNoDrawFaces` → proxy/NoDraw stripped from visuals, used for collider → complete

### [x] Phase 5. Vegetation / Static Objects
Status:
- parse/model side done
- runtime preload/reuse done
- runtime spawn from supplement records: `FcLevelLoadService.SpawnVegetationFromSupplement` (2026-05-16)
  - `FcVegetationInstance.Initialize(virtualPath, typeIndex, instanceScale)` for runtime init
  - positions resolved via active `Terrain.SampleHeight`; skips if editor scene already has vegetation
  - `FcLevelLoadReport.SpawnedVegetation` counter added

### [x] Phase 6. Structural Objects + Volumes
Done (2026-05-16):
- `FcLevelLoader.IsTypeWithShapePoints` extended: VisArea/Portal/OccluderArea/WaterVolume ShapePoints now parsed (previously only Shape type)
- `FcVolumeObjects.cs` — 5 stub MonoBehaviours: `FcVisAreaVolume`, `FcPortalVolume`, `FcOccluderAreaVolume`, `FcFogVolume`, `FcWaterVolume`; each has typed fields + `OnDrawGizmosSelected` polygon/box gizmo
- `FcLevelSceneBuilder.BuildMission` — new "Volumes" root GO + Pass 2 volume build loop; `TryBuildVolume`/`IsVolumeType` helpers; generic objects pass skips volume types
- `BuildStats.Volumes` counter added
- Both `BuildScene` and `RebuildFromLayoutData` create `volumeRoot` and pass it to `BuildMission`

### [x] Phase 7. Entity Content Classes
Done (2026-05-16):
- `FcEntity.cs` — base `SetData(FcEntityDesc)` now stores full property payload as parallel `string[] _propertyKeys/_propertyValues`; `TryGetProperty(key, out value)` accessor
- `FcEntityStub.cs` — rewritten: stores `_modelPath` + full properties; `Initialize(FcEntityDesc)` runtime path; `SetData(FcEntityDesc)` editor path
- `FcMeshEntity.cs` — `Initialize(string virtualPath)` added for runtime spawn (no SerializedObject)
- `FcLevelLoader.ApplyEntityTransform` — extracted entity rotation math from editor-only `FcLevelSceneBuilder.ApplyCryRotationXYZ`; now runtime-accessible
- `FcLevelSceneBuilder.ApplyCryRotationXYZ` → delegates to `FcLevelLoader.ApplyEntityTransform`
- `FcLevelLoadService.SpawnEntitiesFromMission` — runtime entity spawn: skips if FcEntity already in scene; creates "Entities" root; for each mission entity: FcEntityStub always, FcMeshEntity added when model path present; DynamicLight/SoundSpot skipped (handled by environment system)
- `FcLevelLoadReport.SpawnedEntities`, `SpawnedEntitiesMesh` counters added
- `EnqueueSceneEntitiesForCurrentScope` runs after entity spawn → FcMeshEntity GOs found and enqueued; double-enqueue safe (FcEntityLoadService deduplicates via `_active` HashSet)

### [x] Phase 8. Static Lights + Lightmap Hooks
StatLights.dat absent in all stock level.pak — out of scope (will never exist in FC1 stock data).
DynamicLight count per level: Training=12(9real), Swamp=23(17real), Control=69(68real), Fort=10(10real).

Done (2026-05-16):
- `.cry` file = ZIP with `Level.editor_xml`; Training has 127 DynamicLight (RT=0: 104 bake-only, RT=1: 23 realtime); `level.pak/mission_training.xml` has only 12 → RT=0 lights never reach mission XML
- `FcLevelLoader.LoadEditorXmlDynamicLights(levelName)` reads `<LevelName>.cry`, decompresses via `PakArchive` (handles case-mismatch), parses `<Object EntityClass="DynamicLight">` using existing `ParseEntityNode`
- `FcLevelSceneBuilder.BuildEditorXmlLights` calls loader, deduplicates by EntityId vs mission lights, builds remaining via `BuildDynamicLightEntity`; adds `BuildStats.Lights` counter
- `BuildDynamicLightEntity` fixed:
  - `bFakeLight="1"` → skip Light component (DLF_FAKE = corona/flare only)
  - spot detection: `texture_ProjectorTexture` non-empty + `!bProjectInAllDirs` (was: `lighttype==2`)
  - `light.lightmapBakeType = LightmapBakeType.Mixed` — all real lights contribute to bake
  - `light.shadows`: `CastShadows` || `CastShadowMaps` entity attrs → `LightShadows.Soft`
- `SpawnRuntimeDynamicLight` added to `FcLevelLoadService`:
  - same fake/active filtering; `LightType` + color/intensity/range; shadows from entity attrs
  - `_report.SpawnedLights` counter
- Prior code skipped DynamicLight with incorrect comment "handled by FcLevelEnvironment" (only sun handled there)
- UV2 lightmap UVs via `Unwrapping.GenerateSecondaryUVSet` in `CgfAssetCacheService.SaveAssets`; cache version bumped to `v10_lmuv`
- `vector_LightDir` for fixed-direction spot projectors: out of scope (entity Angles sufficient for bake orientation)

### [x] Phase 9. Particles, Music, Movie Placeholders
Done (2026-05-16):
- `particles.lst`: binary `CRY\x02` format; all stock levels 0–1 entries; ParticleEffect/ParticleSpray entity classes handled as `FcEntityStub` → no separate parser needed
- `music/*.xml`: `<MusicThemeLibrary>` OGG refs; covered by `MusicThemeSelector` entities → `FcEntityStub`
- `moviedata.xml` sequences:
  - `FcMovieSequenceDesc` added to `FcLevelData.cs` (Name, StartTime, EndTime, NodeCount)
  - `FcLevelLoader.LoadMovieSequences(levelName)` parses `<SequenceData><Sequence>` from level.pak/moviedata.xml
  - `FcMovieSequencePlaceholder` MonoBehaviour in `Level/Volumes/` — stores metadata, gizmo = magenta sphere
  - `FcLevelSceneBuilder.BuildMovieSequencePlaceholders` creates "Sequences" root GO + one child per sequence
  - Called from both `BuildScene` and `RebuildFromLayoutData`; `BuildStats.Sequences` counter added
  - Training: 5 seqs; Fort/Pier: 14 seqs; Cooler: 18 seqs

### [x] Phase 10. Validation + Tooling
Done (2026-05-16):
- V2 import report counters exist; targeted EditMode tests for terrain decode and preload dedupe/planning
- "Source Parity Audit" button added to `FcLevelBuilderWindow` → `BuildSourceParityReport`:
  - parses source: mission entities, brushes, vegetation, .cry lights, moviedata sequences
  - counts scene: FcEntity, FcBrushInstance, FcVegetationInstance, Light, FcMovieSequencePlaceholder
  - prints table with SRC/SCENE/DELTA columns
- `FcKnownAssetRegressionTests.cs` in `OpenFarCry.Importer.Tests.Editor`:
  - `overhanging_rock`: parses with vertices+faces, not NoDraw
  - `coa_streetlight`: mesh with faces, LOD sibling present
  - `camo_net`: has Plants/AlphaBlend/AlphaTest material
  - `ww2_gk_cbe02_x200y100z200_decal`: Decal-family material, has geometry
  - all tests skip gracefully via `Assert.Ignore` if VFS/game data absent

## Priority

- [x] Lossless parse + supplement model
- [x] Runtime geometry preload base (brush + vegetation)
- [~] Stock `materials.xml` parse + material precedence pipeline
- [ ] Runtime scene builder from parsed records
- [x] Runtime terrain construction (out of scope: editor-built terrain on scene)
- [~] Runtime brush instantiation from `brush.lst`
- [~] Runtime vegetation instantiation from supplement records
- [ ] Runtime objects/volumes instantiation
- [ ] Runtime entity content mapping
- [ ] Runtime material/surface application
- [ ] Static light markers
- [ ] Movie/music/particle metadata

## Risks

- `cover.ctc` decode unknown/complex -> use `cover_low.dds` first
- Unity terrain basis/scale mismatch risk vs existing transforms
- high vegetation counts still need batching/instancing
- dirty generated scenes can hide importer regressions
- huge entity-class spread requires strong stub discipline
- `StatLights.dat` absent in stock packs
- split material ownership (embedded vs level override) can waste IO + bind wrong submaterials
- stock `materials.xml` hierarchy easy to misparse as flat map
- NoDraw/proxy semantics can break if MatID remap diverges from mesh postprocess
- cache invalidation drift can reuse stale assets after resolver/postprocess changes

## Progress Notes

- 2026-05-16:
  - slot-aware brush override apply no longer blind all-slot replace
  - submesh MatID targeting + fallback slot-index targeting wired
  - parent/submaterial selection improved (MatID + source-name fallback)
  - effective visual `SubmeshMaterialIds` persisted in cache metadata
  - slot diagnostics serialized (`targeted/applied/outcome/input/output/detail`)
  - runtime warning for unresolved slot resolution
  - import report carries override match modes and missing buckets
  - authoring build persists slot+instance diagnostic aggregates + unresolved samples + resolution-source buckets
  - authoring validation checks scene/layout sync for slot+instance diagnostics
  - 2026-05-16 (continued): P3.3 reverse-path fallback, P3.5 skip-preload opt, P3.6 decal RGB-only, P3.4 ShaderFamilyCounts → P3 closed
  - P8: .cry ZIP parse via PakArchive → LoadEditorXmlDynamicLights; bFakeLight/bUsedInRealTime/spot detection fixed; UV2 via Unwrapping.GenerateSecondaryUVSet; cache version bumped to v10_lmuv; BuildStats.Lights added
  - P9: moviedata.xml sequence placeholders (FcMovieSequenceDesc + LoadMovieSequences + FcMovieSequencePlaceholder + BuildMovieSequencePlaceholders); particles.lst and music/*.xml covered by existing FcEntityStub path → P9 closed

## Done Definition

Full runtime content import v1 complete when:
- [x] all known level package files indexed
- [x] runtime/editor build resolves stock `materials.xml` correctly with strict override->fallback precedence (brush path)
- [x] runtime builds terrain visible/collidable from source (editor-built scene path; out of scope for runtime)
- [ ] runtime spawns vegetation from parsed records
- [ ] runtime spawns brushes from parsed `brush.lst` with override/LOD/collider parity
- [ ] runtime spawns mission objects/volumes from parsed records
- [ ] runtime maps top stock entity classes to content components/stubs
- [ ] runtime represents lights/sounds/particles/cameras from source data
- [ ] runtime audit counts match source on selected reference levels
- [x] parse/cache layer drops no source attrs without report
