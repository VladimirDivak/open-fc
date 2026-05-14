# CGF Level Geometry Preload Refactor Plan

## Goal

Level know all brush + vegetation before runtime CGF load. Use this.

Shift:
- from instance-driven import
- to level-driven plan -> unique preload -> instance build

Apply to:
- brush geometry
- vegetation geometry

## Non-goals

- no coord/basis/bind-pose/material behavior change
- no editor browser/smoke-test refactor first pass
- no early skinned/entity expansion

## Dedup unit

Not scene object. Not path alone.

Use normalized import request key:

`{virtualPath, importScale, importSkeleton, selectedMeshChunkId, meshCacheVersion}`

Must match runtime `modelCacheKey` semantics.

## Target runtime flow

```text
FcLevelLoadService.LoadLevelAsync
  1. load mission / brush list / vegetation data
  2. collect geometry users
  3. build preload plan
       - unique base CGF reqs
       - unique sibling LOD reqs
       - unique material/texture deps
  4. preload unique CGF assets
       - parsed/model cache fill
       - in-flight coalescing by model key
  5. preload unique textures/material inputs
  6. expose ready asset handles
  7. instance services build GameObjects from ready assets
  8. release by level scope as now
```

## New planning layer

Add under `Assets/Scripts/Level/Services/`:

- `FcLevelGeometryPreloadPlanner`
- `FcLevelGeometryPreloadPlan`
- `FcLevelGeometryRequest`
- `FcLevelGeometryAssetHandle`

### `FcLevelGeometryRequest`

One normalized CGF build request.

Fields:
- virtual path
- import scale
- import skeleton
- selected mesh chunk id
- source kind: brush / vegetation / brush_lod / vegetation_lod

### `FcLevelGeometryPreloadPlan`

Hold:
- unique base reqs
- unique LOD reqs
- scene object -> base req key map
- base req key -> sibling LOD req keys map
- unique material/texture preload inputs

### `FcLevelGeometryAssetHandle`

Wrap imported `CgfRuntimeImportResult`.
Expose:
- base mesh
- parsed file
- build result
- sibling LOD results

Owned by level scope. Reused by many instances.

## Collection rules

### Brushes

Source:
- `FcBrushLoader.LoadBrushes(levelName)`
- fallback: existing `FcBrushInstance` comps

Per unique base path:
- make base req
- discover sibling LOD paths once
- add LOD reqs

### Vegetation

Source:
- `FcLevelSupplementData.VegetationTypes`
- `FcLevelSupplementData.VegetationInstances`
- fallback: existing `FcVegetationInstance` comps

Per instance:
- resolve type -> path
- make base req
- discover sibling LOD paths once per unique base path
- add LOD reqs

## Importer refactor

### 1. Model-level in-flight coalescing

Now `CgfRuntimeImportService` coalesce parsed load per path only.

Add second in-flight table by `modelCacheKey`.
Need: same mesh reqs share one `PrepareBuild + UploadPrepared` path.

Reason:
- still useful after planner
- protects partial migration
- protects future on-demand loads

### 2. Bulk geometry preload API

Add API above raw `CgfRuntimeImporter.ImportAsync`:

```csharp
UniTask<IReadOnlyDictionary<FcLevelGeometryRequest, CgfRuntimeImportResult>>
    PreloadAsync(IReadOnlyList<FcLevelGeometryRequest> requests, string levelScopeId, CancellationToken ct)
```

Behavior:
- import all unique reqs
- reuse parsed/model cache
- allow partial fail
- deterministic result order

### 3. Bulk texture preload + dedupe

After geometry preload:
- inspect imported results
- collect unique textures
- preload once per normalized path + color space mode

Replace repeated per-instance calls from:
- `FcBrushLoadService.LoadOneAsync`
- `FcVegetationLoadService.LoadOneAsync`
- `FcVegetationLoadService.LoadLodResultsAsync`

Suggested API:

```csharp
UniTask PreloadTexturesForResultsAsync(
    IReadOnlyList<CgfRuntimeImportResult> results,
    string textureScopeId,
    CancellationToken ct)
```

Dedup by:
- normalized texture virtual path
- linear/sRGB flag

Include base + LOD in one pass.

## Level service refactor

### 1. Move planning into `FcLevelLoadService`

Kickoff become:
1. load mission
2. load brush list
3. load vegetation source
4. build brush + vegetation preload plan
5. preload unique geometry
6. preload textures/material deps
7. publish ready handles to instance services

Reason: full scene knowledge already here.

### 2. Change instance services: importer -> builder

`FcBrushLoadService`
- may keep distance scheduling
- stop steady-state `ImportAsync`
- request preloaded handle by key
- only build GO, collider, LODGroup

`FcVegetationLoadService`
- same
- consume preloaded base + LOD handles
- only build renderers / `LODGroup`

Keep if wanted:
- `_maxConcurrent`
- `_loadsPerFrame`
- camera-distance sorting

But concurrency now control GO assembly, not parse/import.

### 3. Keep fallback paths for migration

Keep temporarily:
- `FcBrushInstance.FallbackLoadAsync`
- `FcVegetationInstance.FallbackLoadAsync`

Compatibility only. Not primary runtime path.

## LOD refactor

Need one runtime LOD path for brush + vegetation.

Change:
- stop runtime brush use of `CgfLodImportService.ConfigureLodGroup`
- keep `ConfigureLodGroup` for editor only, or split:
  - `BuildRuntimeLodGroup(root, preloadedLodResults, materialService, scopeId)`
  - `ConfigureEditorLodGroup(...)`

Reason:
- brush runtime path still direct VFS read + parse/build per call
- vegetation runtime path already use `ImportAsync`
- runtime should use same cached asset path everywhere

## Rollout order

### Phase 1. Importer base

1. add model-level in-flight coalescing
2. add bulk geometry preload helper
3. add bulk texture preload helper + dedupe
4. add tests for duplicate req coalescing + texture dedupe

### Phase 2. Vegetation first

1. build vegetation base + LOD preload plan
2. preload from `FcLevelLoadService`
3. convert `FcVegetationLoadService` to ready handles
4. verify runtime LOD visuals unchanged

Why first: vegetation already use `ImportAsync` for LOD path.

### Phase 3. Brushes

1. build brush base + LOD preload plan
2. replace runtime `ConfigureLodGroup` use
3. convert `FcBrushLoadService` to ready handles
4. keep collider/proxy stripping behavior same

Why second: brush LOD path mismatch bigger.

### Phase 4. Cleanup

1. merge shared brush/vegetation planning logic
2. reduce duplicated fallback code where safe
3. add runtime report:
   - unique base geometry req count
   - unique LOD req count
   - reused cached model count
   - preloaded unique texture count

## Implementation status (2026-05-12)

### Phase 1. Importer base

Status: done.

- model-level in-flight coalescing added in `CgfRuntimeImportService`
- bulk preload API added: `CgfRuntimeImporter.PreloadAsync(...)`
- bulk texture preload + dedupe added: `PreloadTexturesForResultsAsync(...)`
- tests added: `CgfGeometryPreloadDedupeTests`

### Phase 2. Vegetation first

Status: done.

- vegetation preload plan built in `FcLevelGeometryPreloadPlanner`
- preload orchestration moved into `FcLevelLoadService`
- `FcVegetationLoadService` now consumes preloaded handles first
- runtime LOD assembly for vegetation unchanged by thresholds

### Phase 3. Brushes

Status: done.

- brush preload plan built in `FcLevelGeometryPreloadPlanner`
- runtime brush no longer uses `CgfLodImportService.ConfigureLodGroup`
- `FcBrushLoadService` now consumes preloaded handles first
- brush collider/proxy stripping behavior preserved in `FcBrushInstance`

### Phase 4. Cleanup

Status: done for planned scope.

- shared brush/vegetation planning logic merged in planner
- duplicated fallback/runtime import path reduced via `FcLevelGeometryImportHelper`
- shared runtime LODGroup builder extracted: `FcLevelRuntimeLodGroupBuilder`
- runtime preload metrics added in `FcLevelLoadReport`

### Validation status

Local batch test execution is blocked in this CLI environment when Unity project is already open in another instance.
Interactive Unity test runs reported by user passed after each major refactor step.

### Remaining optional follow-up

- remove temporary fallback paths after confidence window:
  - `FcBrushInstance.FallbackLoadAsync`
  - `FcVegetationInstance.FallbackLoadAsync`
- add focused EditMode tests for fallback helper path (`FcLevelGeometryImportHelper`)
- run play-mode visual regression pass for LOD transitions and proxy/no-draw brush cases

## Validation

Must verify:
- repeated brushes share one mesh per model key
- repeated vegetation share one mesh per model key
- sibling LOD discovered once per unique base path
- base + LOD textures preload once per unique normalized path
- brush proxy/no-draw stripping still work
- brush collider build unchanged
- vegetation `LODGroup` thresholds unchanged
- level scope release still free unused meshes/textures

## Expected file changes

Importer:
- `Assets/Scripts/Importer/Cgf/CgfRuntimeImportService.cs`
- `Assets/Scripts/Importer/Cgf/CgfMaterialImportService.cs`
- maybe `Assets/Scripts/Importer/Cgf/CgfLodImportService.cs`
- optional new importer helpers if logic truly importer-specific

Level:
- `Assets/Scripts/Level/Services/FcLevelLoadService.cs`
- `Assets/Scripts/Level/Services/FcBrushLoadService.cs`
- `Assets/Scripts/Level/Services/FcVegetationLoadService.cs`
- new planner/handle types under `Assets/Scripts/Level/Services/`
- small integration updates in:
  - `Assets/Scripts/Level/Entities/FcBrushInstance.cs`
  - `Assets/Scripts/Level/Entities/FcVegetationInstance.cs`

Tests:
- importer EditMode tests for coalescing/cache behavior
- level/runtime-oriented EditMode tests for brush/vegetation dedupe plan

## Summary

Do not replace CGF runtime importer.
Change when/how use it.

Target:
- level collect unique deps once
- preload base geometry once
- preload sibling LOD geometry once
- preload material/texture deps once
- let brush + vegetation build many instances from same ready assets
