# Vegetation Rendering + Physics Refactor Plan

Snapshot: 2026-05-14. Scope: runtime vegetation rendering + collision.

## Goal

Keep instanced vegetation as default. Remove scalability limits. Add selective runtime collision for blocking vegetation.

Target middle ground:
- `FcVegetationTerrainService` primary for mass vegetation.
- No move to `GameObject + LODGroup`.
- Keep mesh LOD support, driven from instanced renderer.
- Selective near-player colliders for blocking types.
- Simple authoring/runtime data for incremental migration.

## Non-goals

- No global switch to per-instance `GameObject`.
- No full GPU renderer in first pass.
- No collider generation for every instance.
- No physically exact bushes/grass collision.
- No brush/entity collision redesign.

## Current State

Rendering:
- `FcLevelSceneBuilder` serialize vegetation types + instances to `FcVegetationTerrainService`.
- `FcVegetationTerrainService` import base + LOD meshes. Render via `Graphics.DrawMeshInstanced`.
- LOD selection CPU-side, distance-based.
- Frame scan all instances linearly (bottleneck).

Physics:
- Terrain vegetation has no colliders.
- `FcVegetationInstance` path no colliders.
- Editor import profiles mark `requiresPhysicsCollider: false`.

Result:
- Rendering architecture correct.
- Collision missing for large blocking vegetation.
- CPU visibility/LOD next bottleneck.

## Target Architecture

```text
FcLevelSceneBuilder
  -> serializes vegetation types + instances + collision metadata

FcVegetationTerrainService
  1. build runtime prototypes from preloaded handles
  2. partition instances into terrain cells
  3. render visible cells only
  4. choose LOD per visible instance
  5. draw batched instanced meshes
  6. manage near-player collider pool for blocking vegetation

FcVegetationCollisionService or internal collider subsystem
  - activate collider hosts only near player
  - use type-driven collider policy
  - recycle collider GameObjects from a pool
```

## Design Rules

- Rendering stays instanced.
- Collision opt-in per type.
- Sibling LODs signal large vegetation.
- Visual LOD fallback for collision, but physics proxy/primitive priority.
- Collider activation distance-based + capped.
- Policy-driven data model.

## Vegetation Type Policy

Collision policy:
- `None`
- `PrimitiveCapsule`
- `PrimitiveBox`
- `LowLodMesh`
- `PhysicsProxy`

Migration heuristic:
- No sibling LODs: `None`.
- Sibling LODs + large asset (tree/palm/trunk): `LowLodMesh`.
- Importer detect proxy: `PhysicsProxy`.
- Bushes: `None` unless needed.

Tuning fields:
- `CollisionLodIndex`
- `CollisionDistance`
- `MaxActiveCollidersPerType`
- `PrimitiveHeight`
- `PrimitiveRadius` / `PrimitiveSize`

## Phase Board

- [x] Phase 1: Runtime inventory + data model extension
- [x] Phase 2: Renderer cleanup
- [x] Phase 3: Spatial partitioning + visible-cell culling
- [x] Phase 4: Per-type policy + authoring defaults
- [x] Phase 5: Near-player collider pool
- [x] Phase 6: Collision source selection + fallback chain
- [x] Phase 7: Runtime integration + level lifecycle
- [ ] Phase 8: Validation, profiling, rollout

## Detailed Plan

### [x] Phase 1. Runtime inventory + data model extension

Add explicit runtime metadata. Decisions data-driven.

Tasks:
- [x] Audit data sources from `leveldata.xml` + scene.
- [x] Extend `FcVegetationTerrainService.VegetationTypeEntry` with descriptor.
- [x] Add collision policy fields: mode, LOD index, distance, budget.
- [x] Backward compat for `TypeIndex + VirtualPath`.
- [x] Collision metadata on terrain service or shared config.

Success:
- [x] Scene express blocking types + collider build rules.
- [x] Old scenes load with safe defaults.

### [x] Phase 2. Renderer cleanup

Refactor `FcVegetationTerrainService` into subsystems.

Tasks:
- [x] Split prototype import/build from instance state.
- [x] Add explicit runtime structs for prototype, instance, cell data.
- [x] Isolate LOD threshold generation from frame update.
- [x] Isolate draw submission from visibility selection.
- [x] Cache immutable per-instance data.

Success:
- [x] Rendering matches current behavior.
- [x] Clear extension points for cells + colliders.

### [x] Phase 3. Spatial partitioning + visible-cell culling

Remove linear scanning.

Tasks:
- [x] Partition instances into fixed terrain cells.
- [x] Compute cell bounds.
- [x] Cell frustum culling.
- [x] Cell distance culling.
- [x] Iterate instances from visible cells.
- [x] Benchmark cell sizes.

Success:
- [x] Frame work scale with nearby cells.
- [x] No popping beyond boundaries.

### [x] Phase 4. Per-type vegetation policy + authoring defaults

Decide blocking vs non-blocking.

Tasks:
- [x] Classification rules for tree/palm/trunk/bush/grass.
- [x] Auto default policy for legacy data.
- [x] Manual override path.
- [x] Document render-only classes.
- [x] Debug view for resolved policy.

Success:
- [x] Large blocking vegetation enabled without hand-authoring.
- [x] Small vegetation collision-free.

### [x] Phase 5. Near-player collider pool

Tasks:
- [x] Pooled collider hosts reused at runtime.
- [x] Activate near player/camera.
- [x] Global/per-type cap.
- [x] Nearest-first priority.
- [x] Recycle hosts when out of range.
- [x] Clean teardown.

Success:
- [x] Bounded local colliders.
- [x] Nearby trees block player, no map-wide overhead.

### [x] Phase 6. Collision source selection + fallback chain

Cheapest representation per type.

Fallback:
1. Physics proxy
2. Low LOD mesh
3. Primitive collider
4. No collider

Tasks:
- [x] Investigate CGF proxy/physics chunks.
- [x] Extraction path for vegetation collision.
- [x] `LowLodMesh` path via `CollisionLodIndex`.
- [x] Primitive capsule/box builders.
- [x] Source meshes readable/stable for reuse.
- [x] Shared mesh reused, no per-instance allocations.

Success:
- [x] Stable source strategy per type.
- [x] Collision cheaper than rendering.

### [x] Phase 7. Runtime integration + level lifecycle

Tasks:
- [x] Reuse preloaded handles from `FcLevelLoadService`.
- [x] Collider meshes share LOD meshes or own cached copies.
- [x] Level release no break active hosts.
- [x] Teardown cell data, pool, references.
- [x] Fallback for standalone editor.

Success:
- [x] Unified ownership rules.
- [x] No leaks after unload.

### [ ] Phase 8. Validation, profiling, rollout

Tasks:
- [x] EditMode tests: partitioning, policy resolution.
- [x] EditMode tests: fallback logic.
- [x] Runtime counters: instances, cells, colliders.
- [x] Gizmo/debug mode for radius + bounds.
- [ ] Validate dense jungle + sparse levels.
- [ ] Benchmark CPU cost update.
- [ ] Verify player collision vs large trees.

Success:
- [ ] Rendering cost flat/down with collision.
- [ ] Collision works as intended.
- [ ] No regressions.

## Implementation Breakdown

### [x] Slice A. Data model + scene serialization

Files:
- `Assets/Scripts/Level/Services/FcVegetationTerrainService.cs`
- `Assets/Scripts/Level/Editor/FcLevelSceneBuilder.cs`
- `Assets/Scripts/Level/Services/FcVegetationCollisionMode.cs`

Tasks:
- [x] Add `CollisionMode` enum.
- [x] Extend type data: mode, LOD index, distance, budget, primitives.
- [x] Legacy data compat.
- [x] `FcLevelSceneBuilder` assign defaults.
- [x] Heuristic for default mode from path/name/LOD.

Success:
- [x] Metadata stored for activation.
- [x] Legacy scenes enter Play Mode safely.

### [x] Slice B. `FcVegetationTerrainService` internal split

Tasks:
- [x] Split `RuntimeType` into pieces (render, collision, instance, cell).
- [x] Startup methods: `BuildRuntimeTypes`, `BuildRuntimeInstances`, `BuildSpatialCells`, `InitializeScratchBuffers`.
- [x] Frame methods: `CollectVisibleCells`, `CollectVisibleInstances`, `SubmitInstancedDraws`, `UpdateNearbyColliders`.
- [x] Keep render output same.

Success:
- [x] Readable service subsystems.
- [x] No behavior drift.

### [x] Slice C. Spatial partitioning

Files:
- `Assets/Scripts/Level/Services/FcVegetationTerrainService.cs`
- `Assets/Scripts/Level/Services/FcVegetationSpatialCell.cs`

Tasks:
- [x] Cell size constant/field.
- [x] Bucket instances at startup.
- [x] Compute cell `Bounds`.
- [x] Cache index list.
- [x] Frustum test + distance reject.
- [x] Visible cell iteration.
- [x] Debug counters.

Success:
- [x] Update loop scale with local cells.
- [x] Lower CPU cost.

### [x] Slice D. Type policy resolution

Tasks:
- [x] Resolve policy once at startup.
- [x] Mark blocking tree-like assets.
- [x] Bushes/grass default `None`.
- [x] Clamp invalid config values.
- [x] Debug log resolved policy.

Success:
- [x] Stable policy object per type.
- [x] No policy logic in update.

### [x] Slice E. Collider pool runtime

Tasks:
- [x] Pooled collider hosts under service root.
- [x] Track instance -> host assignment.
- [x] Nearest-first selection + cap enforcement.
- [x] Recycle + clean teardown.

Success:
- [x] Bounded collider count.
- [x] Selective near-player collision.

### [x] Slice F. Primitive collider path first

Tasks:
- [x] `PrimitiveCapsule` + `PrimitiveBox` builders.
- [x] Size from mesh bounds + scale.
- [x] Per-type overrides.
- [x] Trunk trees first.
- [x] Allocation-lean updates.

Success:
- [x] Trees block player without mesh colliders.
- [x] Cheap rollout.

### [x] Slice G. Low LOD mesh collider path

Tasks:
- [x] Reuse imported LOD meshes.
- [x] Fallback for missing LOD.
- [x] Shared mesh reuse.
- [x] Convex `MeshCollider` check.
- [x] Restrict mesh-mode to needed types.

Success:
- [x] Mesh path for selected types.
- [x] Bounded cost.

### [x] Slice H. Physics proxy investigation

Tasks:
- [x] Inspect CGF for proxy data.
- [x] Reuse brush proxy logic.
- [x] Shared helper for extraction.
- [x] Proxy mode enable.

Success:
- [x] Proxy path proven/implemented.

### [x] Slice I. Lifecycle + ownership

Tasks:
- [x] Define mesh ownership.
- [x] Teardown pool before scope release.
- [x] Direct-import fallback valid.
- [x] Reset path for service restart.

Success:
- [x] No leaks.
- [x] No unload errors.

### [ ] Slice J. Tests + validation

Tasks:
- [x] Policy resolution tests.
- [x] Cell bucketing/bounds tests.
- [ ] Visible-cell filtering tests. (requires camera/frustum → PlayMode)
- [x] Collider prioritization tests.
- [x] LOD fallback tests.
- [ ] Dense level checklist. (manual runtime)

Success:
- [ ] Core logic covered by tests.
- [ ] Repeatable validation.
