# Level Content Import Refactor Plan

Snapshot: 2026-05-16. Scope: runtime level load into scene first. No gameplay/AI/script.
Validation: user runs tests manually. Agent no run suite.

## Goal

Ship full visible/structural level content from Far Cry package:
terrain + water, brushes (override/LOD/collider/proxy/lightmap hooks), vegetation from `objects.lst`, mission entities as typed placeholders, vis/portal/occluder/fog/water volumes, env/lights/sound/particles/cameras/triggers/markers, material lib + surface types, layout cache.

Non-goal: NPC AI, weapon gameplay, mission logic, save/load, netcode, exact Cry renderer, runtime lightmap bake.

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

### [~] Phase 2. Terrain Skeleton
Status:
- editor path has terrain skeleton + collider + fallback visual + water plane
- runtime source path parses settings only, no terrain build

### [~] Phase 3. Materials + Surface Base
Status:
- parsed material/surface data present in supplement + V2 report
- runtime CGF texture preload path exists
- P3.1 done (stock `materials.xml` parser fix)
- P3.2 partial (structured slot semantics in supplement/resolver)
- P3.3 partial (submesh MatID-aware slot targeting)
- P3.4 partial (slot + instance diagnostics persisted into import report)

Missing:
- runtime-wide level material/surface application for terrain/brush/entity
- strict precedence pipeline `level override -> embedded fallback`

Planned work:
- [x] P3.1 Parse stock `materials.xml` structure + texture slot extraction
- [~] P3.2 Expand supplement material model (name/fullname/shader/alpha/flags/slots/hierarchy)
- [ ] P3.3 Build-path resolver finalization:
  - decide keep/replace per CGF submaterial + override token/id
  - finish CGF chunk-name-aware submaterial fallback when MatID/index mismatch
- [~] P3.4 Diagnostics:
  - per-instance override/fallback origin
  - unresolved names/ids
  - slot misses / unsupported shader family markers
- [ ] P3.5 Override-aware texture preload:
  - evaluate targeted slot replacement before preload
  - skip CGF texture preload for fully replaced chunks/slots
  - keep preload for fallback/unresolved paths
- [ ] P3.6 Alpha/decal policy:
  - explicit rule for RGB-only decal textures (no alpha)
  - map to correct opacity/cutout/blend behavior
  - mark diagnostics when alpha synthesized/fallback path used

### [~] Phase 4. Brush Completion
Status:
- brush metadata preservation mostly in place
- runtime brush load + LOD preload + texture preload + parity postprocess exists

Missing:
- runtime spawn of brush instances directly from `brush.lst`
- strict NoDraw contract end-to-end: NoDraw MatID must not leak into final visual submesh/material layout

### [~] Phase 5. Vegetation / Static Objects
Status:
- parse/model side done
- runtime preload/reuse done
- runtime spawn-from-records missing (still editor-created `FcVegetationInstance`)

### [ ] Phase 6. Structural Objects + Volumes
Need typed/stub components + gizmo/debug for markers/areas/vis/occluders/fog/water volumes.

### [ ] Phase 7. Entity Content Classes
Need top entity classes mapped to content components/stubs; preserve full property payload.

### [ ] Phase 8. Static Lights + Lightmap Hooks
Need static-light derivation from DynamicLight flags + optional `StatLights.dat`, plus brush lightmap metadata hook.

### [ ] Phase 9. Particles, Music, Movie Placeholders
Need metadata parse/index + placeholders in scene graph.

### [~] Phase 10. Validation + Tooling
Status:
- V2 import report counters exist
- targeted EditMode tests exist for terrain decode and preload dedupe/planning

Missing:
- runtime content audit workflow + source-vs-runtime parity checks per level
- focused regression fixtures for known problematic assets/materials:
  - `overhanging_rock`
  - `coa_streetlight`
  - `camo_net`
  - `ww2_gk_cbe02_x200y400z200_decal`

## Priority

- [x] Lossless parse + supplement model
- [x] Runtime geometry preload base (brush + vegetation)
- [~] Stock `materials.xml` parse + material precedence pipeline
- [ ] Runtime scene builder from parsed records
- [ ] Runtime terrain construction
- [ ] Runtime brush instantiation from `brush.lst`
- [ ] Runtime vegetation instantiation from supplement records
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
  - remaining P3.3 gap: stronger chunk-name-aware mapping fallback
  - remaining P3.4 gap: raw per-instance/per-renderer lines still scene metadata only, not full layout payload

## Done Definition

Full runtime content import v1 complete when:
- [x] all known level package files indexed
- [ ] runtime/editor build resolves stock `materials.xml` correctly with strict override->fallback precedence
- [ ] runtime builds terrain visible/collidable from source
- [ ] runtime spawns vegetation from parsed records
- [ ] runtime spawns brushes from parsed `brush.lst` with override/LOD/collider parity
- [ ] runtime spawns mission objects/volumes from parsed records
- [ ] runtime maps top stock entity classes to content components/stubs
- [ ] runtime represents lights/sounds/particles/cameras from source data
- [ ] runtime audit counts match source on selected reference levels
- [x] parse/cache layer drops no source attrs without report
