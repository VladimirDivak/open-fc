# Level Authoring Bake Prep Refactor Plan

Snapshot: 2026-05-15. Scope: editor authoring scene for lighting bake, NavMesh bake, visual polish.

## Goal

Build Unity scene that contain:
- persisted `Terrain` with textures
- persisted static level geometry: brushes + vegetation
- persisted materials + textures
- no runtime-only VFS dependency for bake workflow

Keep temp import/cache under `Assets/FCData` during build. Add later step to detach scene from temp cache, so `Assets/FCData` can die after promotion/finalization.

## Non-goals

- No gameplay/runtime streaming rewrite in first pass
- No full entity prefab authoring pass
- No terrain shader redesign first
- No automatic GI/navmesh bake in same refactor first
- No delete-`FCData` magic without explicit scene rewire step

## Problem

Current `Build Scene + Cache Geometry (EditMode)` not enough.

- Terrain data persist, but terrain textures runtime-only via `FcTerrainTextureService`
- Vegetation scene path runtime-only via `FcVegetationTerrainService`
- Brush cache path miss level material override parity
- Scene reference `Assets/FCData` assets directly, so deleting `FCData` break scene

Result: current button build hybrid scene. Good for preview. Bad for stable bake authoring.

## Target Architecture

```text
Level Builder Window
  -> Build Authoring Scene
      1. load level source data
      2. import/persist authoring assets into Assets/FCData
      3. assemble scene from persisted assets only
      4. disable runtime loader components for authoring objects
      5. optional finalize/promote used assets to permanent level folder

Runtime path stay separate:
  Build Scene
    -> placeholders + runtime loaders + VFS/preload flow
```

Two scene modes:
- Runtime scene: current placeholder/runtime-loader flow
- Authoring scene: baked editor geometry/material/terrain flow

## Design Rules

- Editor authoring path must not depend on `Start()` loaders
- Scene must contain real renderable geometry before Play Mode
- Terrain layers must hold real texture assets in edit mode
- Vegetation authoring path must create real scene geometry or prefab instances
- Brush authoring path must apply same material override rules as runtime path
- Temp cache folder allowed during import
- Safe `FCData` deletion require explicit finalize/promote step

## Phase Board

- [x] Phase 1: Split runtime scene build from authoring scene build
- [x] Phase 2: Terrain authoring asset pipeline
- [ ] Phase 3: Brush authoring geometry parity (in progress)
- [ ] Phase 4: Vegetation authoring geometry pipeline (in progress)
- [ ] Phase 5: Scene assembly rules for bake workflow (in progress)
- [ ] Phase 6: Finalize/promote scene dependencies out of `FCData` (in progress)
- [ ] Phase 7: Validation, bake checks, rollout (in progress)

## Detailed Plan

### [x] Phase 1. Split runtime scene build from authoring scene build

Need hard separation. Current button mix two worlds.

Tasks:
- [x] Add explicit authoring build entry in `FcLevelBuilderWindow`
- [x] Keep current `Build Scene` for runtime placeholder flow
- [x] Replace current cache button with `Build Authoring Scene`
- [x] Extract shared source-load phase from scene assembly phase
- Define clear build products:
  - runtime scene
  - authoring scene
  - temp cache
- [x] Stop attaching authoring cache as hidden child under runtime placeholders

Success:
- [x] One button build runtime scene
- [x] One button build authoring scene
- [x] No hybrid scene state

### [x] Phase 2. Terrain authoring asset pipeline

Terrain height already persist. Texture side not done.

Tasks:
- [x] Add editor terrain texture import path
- [x] Persist `cover_low.dds` result as real asset under `Assets/FCData/Levels/<Level>/TerrainTextures/`
- [x] Persist detail terrain textures as real assets
- [x] Build real `TerrainLayer` assets with `diffuseTexture` assigned in edit mode
- [ ] If needed, persist cover atlas built from `cover.ctc`
- [x] Keep runtime `FcTerrainTextureService` off authoring terrain path
- [x] Decide authoring material policy:
  - use existing terrain material
  - assign persisted cover texture/material params in edit mode
  - clone level-local authoring terrain material to avoid mutating shared asset

Success:
- [x] Open scene in editor, terrain look correct before Play
- [x] No terrain texture load required from VFS for bake

### [ ] Phase 3. Brush authoring geometry parity

Brush cache import base already good. Need full parity.

Tasks:
- [x] Build/import brush prefabs through existing CGF cache path
- [x] Apply `FcBrushGeometryPostProcessor` in authoring import path
- [x] Configure level material override data in editor build
- [x] Apply same override material resolution as runtime brush path
- [x] Persist brush override materials/textures into `Assets/FCData/Levels/<Level>/MaterialOverrides`
- Instantiate authoring brush prefabs directly in scene
- Remove/avoid `FcBrushInstance` on authoring visuals unless kept as metadata-only marker

Success:
- Brush visuals in authoring scene match runtime import path
- Brush colliders usable for NavMesh bake
- No missing override materials

### [ ] Phase 4. Vegetation authoring geometry pipeline

Main missing piece.

Tasks:
- [x] Add editor vegetation authoring mode separate from `FcVegetationTerrainService` (placeholder `FcVegetationInstance` path for authoring mode)
- [x] Reuse cached vegetation prefab assets from `Assets/FCData` (attach cached prefab to authoring placeholders)
- [x] Build actual scene objects for vegetation instances
- Decide batching shape:
  - first pass: per-instance prefab/GameObject for correctness
  - later pass: grouped authoring containers if scene too heavy
- Support LODGroup on authoring vegetation if cached prefab has LODs
- Decide collider policy for authoring vegetation:
  - none by default for grass/small plants
  - enabled for blocking trees if needed for navmesh
- Preserve transform/scale parity with runtime vegetation placement

Success:
- Vegetation visible in editor without Play Mode
- Enough static geometry present for lighting/navmesh decisions

### [ ] Phase 5. Scene assembly rules for bake workflow

Need stable authoring scene structure.

Tasks:
- Define root hierarchy:
  - [x] `Level_<name>`
  - [x] `Terrain`
  - [x] `Brushes_Authoring`
  - [x] `Vegetation_Authoring`
  - `Entities`
  - `BakeServices` or none
- [x] Mark authoring renderers/static objects for lightmap/navmesh workflow (static flags on cached authoring geometry + terrain/water)
- [x] Disable/remove runtime-only loader components in authoring scene
- Keep optional metadata components only if harmless in edit mode
- Ensure scene can reopen without rebuilding transient data
- Keep generated asset churn localized under `Assets/FCData/Levels/<Level>/`

Success:
- Scene reopen stable
- Bake tools see real static geometry
- No Play Mode bootstrap needed

### [ ] Phase 6. Finalize/promote scene dependencies out of `FCData`

This phase make delete-temp-cache story true.

Tasks:
- [x] Add dependency collector for active authoring scene
- [x] Enumerate used meshes, prefabs, materials, textures, terrain assets
- [x] Copy/promote only used assets to permanent level-owned folder
  - candidate: `Assets/Scenes/Levels/<Level>_Data/`
  - or `Assets/Levels/<Level>/`
- [x] Rewire scene references from `Assets/FCData/...` to promoted assets
- [x] Validate no remaining scene dependency on `Assets/FCData`
- [x] Add explicit command:
  - `Finalize Authoring Scene`
  - optional `Purge Temp FCData For Level`

Success:
- Scene survive delete of `Assets/FCData`
- Temp cache stay disposable

### [ ] Phase 7. Validation, bake checks, rollout

Tasks:
- [x] EditMode tests for terrain authoring asset generation (terrain texture transpose/duplicate pipeline)
- [x] EditMode tests for brush override parity in authoring path
- [x] EditMode tests for vegetation authoring transform/scale placement
- [x] EditMode test for vegetation authoring collider/LOD policy (colliders off, LODGroup off, LOD0 visible)
- [x] Dependency audit test: finalized scene no `Assets/FCData` refs
- [x] Add `Validate Authoring Scene` report in Level Builder to pre-check bake readiness
- Manual validation in Unity:
  - open scene after domain reload
  - bake lighting
  - build NavMesh
  - delete temp cache after finalize
  - reopen scene
- Compare runtime scene vs authoring scene visually on 1-2 known levels

Success:
- Authoring scene stable
- Lighting bake work
- NavMesh bake work
- Finalized scene independent from temp cache

## Implementation Breakdown

### Slice A. Builder window + mode split

Files:
- `Assets/Scripts/Level/Editor/FcLevelBuilderWindow.cs`
- `Assets/Scripts/Level/Editor/FcLevelSceneBuilder.cs`

Tasks:
- add authoring button
- split runtime vs authoring build options
- remove hybrid attach-cache behavior from primary authoring path

### Slice B. Terrain authoring import

Files:
- `Assets/Scripts/Level/Editor/FcLevelSceneBuilder.cs`
- `Assets/Scripts/Level/Services/FcTerrainTextureService.cs`
- maybe new editor helper: `Assets/Scripts/Level/Editor/FcTerrainAuthoringAssetBuilder.cs`

Tasks:
- editor texture persistence
- real `TerrainLayer` build
- no runtime terrain loader in authoring scene

### Slice C. Brush authoring import

Files:
- `Assets/Scripts/Level/Editor/FcLevelBuilderWindow.cs`
- `Assets/Scripts/Importer/Editor/CgfAssetCacheService.cs`
- `Assets/Scripts/Level/Services/FcLevelMaterialOverrideService.cs`
- maybe new editor helper: `Assets/Scripts/Level/Editor/FcBrushAuthoringBuilder.cs`

Tasks:
- configure override service in editor
- import/persist final brush prefab/material state
- instantiate authoring brush scene objects

### Slice D. Vegetation authoring import

Files:
- `Assets/Scripts/Level/Editor/FcLevelSceneBuilder.cs`
- `Assets/Scripts/Level/Services/FcVegetationTerrainService.cs`
- maybe new editor helper: `Assets/Scripts/Level/Editor/FcVegetationAuthoringBuilder.cs`

Tasks:
- add editor vegetation scene generation
- keep runtime instanced path separate
- support collider/LOD policy for bake scene

### Slice E. Finalize/promote pipeline

Files:
- maybe new:
  - `Assets/Scripts/Level/Editor/FcAuthoringSceneFinalizer.cs`
  - `Assets/Scripts/Level/Editor/FcSceneDependencyCollector.cs`

Tasks:
- collect used assets
- copy/promote
- rewire refs
- validate no temp refs remain

## Risks

- Vegetation per-instance GameObject path may explode scene size on dense levels
- Terrain cover atlas persistence may cost memory/disk
- Brush override parity may expose gaps in current level material resolver
- Finalize/promote step may miss hidden dependencies first pass
- Unity prefab/material duplication churn may get noisy without strict folder rules

## Recommended Delivery Order

1. Phase 1
2. Phase 2
3. Phase 3
4. Phase 4 first-pass correctness over perf
5. Phase 5
6. Phase 7 partial validation
7. Phase 6 after authoring scene proven useful

Reason:
- first make bake scene real
- then make temp cache disposable

## Exit Criteria

- `Build Authoring Scene` create editor-ready level scene
- terrain textured before Play Mode
- brushes visible with correct materials/colliders
- vegetation visible in authoring scene
- light bake + NavMesh bake possible
- after `Finalize Authoring Scene`, scene no longer depend on `Assets/FCData`
