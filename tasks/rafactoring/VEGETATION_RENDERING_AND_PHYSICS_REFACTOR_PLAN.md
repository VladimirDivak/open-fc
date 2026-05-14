# Vegetation Rendering And Physics Refactor Plan

Snapshot: 2026-05-14. Scope: runtime vegetation rendering and collision only.

## Goal

Keep the current instanced vegetation direction as the default rendering path, but remove its main scalability limits and add selective runtime collision for blocking vegetation.

Target middle ground:
- keep `FcVegetationTerrainService` as the primary path for mass vegetation rendering
- do not move all vegetation to `GameObject + LODGroup`
- keep mesh LOD support, but drive it from the instanced renderer
- add selective near-player colliders only for blocking vegetation types
- keep authoring/runtime data simple enough to migrate incrementally

## Non-goals

- no global switch of all vegetation to per-instance `GameObject`
- no full GPU-driven renderer in first pass
- no collider generation for every vegetation instance on the map
- no attempt to make bushes/grass physically exact by mesh collision
- no redesign of brush/entity collision in this plan

## Current State

Rendering today:
- `FcLevelSceneBuilder` serializes vegetation type and instance arrays into `FcVegetationTerrainService`
- `FcVegetationTerrainService` imports base + sibling LOD meshes and renders them through `Graphics.DrawMeshInstanced`
- LOD selection is CPU-side and distance-based
- every frame still scans all vegetation instances linearly

Physics today:
- terrain vegetation path has no colliders
- `FcVegetationInstance` visual path also does not add vegetation colliders
- editor import/cache profiles explicitly mark vegetation as `requiresPhysicsCollider: false`

Result:
- rendering architecture is directionally correct
- collision is missing for large blocking vegetation
- CPU visibility/LOD work will become the next bottleneck before raw mesh cost does

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

- Rendering stays instanced by default.
- Collision is opt-in per vegetation type, not implied by instance count alone.
- Presence of sibling LODs is a useful signal for large vegetation, but not the only rule.
- Visual LOD meshes may be reused for collision fallback, but physics proxy or primitive colliders take priority.
- Collider activation is distance-based and capped by budget.
- Data model must support per-type collision policy without hardcoding path heuristics.

## Vegetation Type Policy

Each vegetation type should eventually expose a collision policy:

- `None`
- `PrimitiveCapsule`
- `PrimitiveBox`
- `LowLodMesh`
- `PhysicsProxy`

Recommended default heuristic for migration:
- if type has no sibling LODs: default `None`
- if type has sibling LODs and looks like large tree/palm/trunk asset: default `LowLodMesh`
- if later importer can detect explicit physics proxy: upgrade to `PhysicsProxy`
- bushes remain `None` unless gameplay proves otherwise

Also add per-type tuning fields:
- `CollisionLodIndex`
- `CollisionDistance`
- `MaxActiveCollidersPerType`
- optional `PrimitiveHeight`
- optional `PrimitiveRadius` / `PrimitiveSize`

## Phase Board

- [ ] Phase 1: Runtime inventory and data model extension
- [ ] Phase 2: Renderer structure cleanup without behavior change
- [ ] Phase 3: Spatial partitioning and visible-cell culling
- [ ] Phase 4: Per-type vegetation policy and authoring defaults
- [ ] Phase 5: Near-player collider pool
- [ ] Phase 6: Collision source selection and fallback chain
- [ ] Phase 7: Runtime integration with preload and level lifecycle
- [ ] Phase 8: Validation, profiling, and rollout

## Detailed Plan

### [ ] Phase 1. Runtime inventory and data model extension

Add explicit runtime metadata so rendering and collision decisions are data-driven instead of inferred ad hoc.

Tasks:
- [x] Audit current vegetation type data sources from `leveldata.xml` and scene serialization
- [x] Extend `FcVegetationTerrainService.VegetationTypeEntry` or replace it with a richer serialized type descriptor
- [x] Add collision policy fields per type: mode, lod index, distance, budget
- [x] Preserve backward compatibility with scenes that only contain `TypeIndex + VirtualPath`
- [ ] Decide whether collision metadata lives directly on terrain service or in a separate shared vegetation asset/config

Success criteria:
- [x] Scene data can express which vegetation types are blocking and how their colliders should be built
- [ ] Old scenes still load with safe defaults

### [ ] Phase 2. Renderer structure cleanup without behavior change

Refactor `FcVegetationTerrainService` into clearer subsystems before performance work.

Tasks:
- [ ] Split prototype import/build from per-instance runtime state
- [ ] Introduce explicit runtime structs/classes for prototype, instance, and cell data
- [ ] Isolate LOD threshold generation from frame update flow
- [ ] Isolate draw submission from instance visibility selection
- [ ] Cache immutable per-instance data needed later by both rendering and collision

Success criteria:
- [ ] Rendering output matches current behavior
- [ ] Service code has clear extension points for cells and collider activation

### [ ] Phase 3. Spatial partitioning and visible-cell culling

Remove full-map linear scanning as the steady-state runtime path.

Tasks:
- [ ] Partition vegetation instances into fixed terrain cells at startup
- [ ] Compute bounds per cell
- [ ] Add frustum culling per cell before per-instance LOD work
- [ ] Add distance culling per cell to skip far-away buckets early
- [ ] Iterate instances only from visible candidate cells
- [ ] Benchmark cell sizes and choose a simple default first

Success criteria:
- [ ] Per-frame vegetation work scales with nearby cells, not total map instance count
- [ ] No visible popping beyond expected cell/frustum boundaries

### [ ] Phase 4. Per-type vegetation policy and authoring defaults

Decide which vegetation participates in collision and which does not.

Tasks:
- [x] Define classification rules for tree / palm / trunk / bush / grass style assets
- [x] Implement automatic default policy assignment for legacy data
- [x] Keep manual override path for false positives/negatives
- [ ] Document which vegetation classes should stay render-only
- [x] Add debug view/logging to inspect resolved collision policy per type

Success criteria:
- [ ] Large blocking vegetation can be enabled without hand-authoring every instance
- [ ] Small vegetation remains collision-free by default

### [ ] Phase 5. Near-player collider pool

Add collision without exploding object count.

Tasks:
- [x] Create pooled collider host GameObjects reused at runtime
- [x] Activate colliders only around player/camera within per-type collision distance
- [x] Cap active colliders globally and/or per type
- [x] Add deterministic priority rule for choosing nearest blocking instances
- [x] Release/recycle collider hosts when instances leave range
- [x] Ensure unload destroys or returns pooled hosts cleanly

Success criteria:
- [x] Only a bounded local set of vegetation colliders exists at runtime
- [ ] Player can collide with nearby blocking trees without map-wide physics overhead

### [ ] Phase 6. Collision source selection and fallback chain

Choose the cheapest acceptable collision representation per type.

Fallback order:
1. explicit physics proxy if available
2. configured low LOD mesh
3. primitive collider derived from bounds/trunk heuristics
4. no collider

Tasks:
- [x] Investigate whether vegetation CGF data exposes usable proxy/physics chunks
- [x] If proxy data exists, add extraction path for vegetation collision
- [x] Add `LowLodMesh` collider path using configured LOD index rather than blindly last LOD
- [x] Add primitive capsule/box builders for trunk-like assets
- [ ] Validate that collider source meshes are readable and stable for pooled reuse
- [x] Avoid per-instance duplicated mesh allocations for shared collider geometry

Success criteria:
- [x] Each blocking vegetation type resolves to one stable collider source strategy
- [ ] Collision stays cheaper than visual rendering for the same vegetation set

### [ ] Phase 7. Runtime integration with preload and level lifecycle

Wire rendering and collision into existing level load ownership rules.

Tasks:
- [x] Reuse `FcLevelLoadService` preloaded vegetation handles for collision source resolution where possible
- [x] Decide whether collider meshes should share imported LOD meshes or own separate cached copies
- [x] Ensure level scope release does not break active collider hosts during unload order
- [x] Add explicit teardown for cell data, collider pool, and imported collision references
- [x] Keep direct-import fallback behavior working for standalone editor play mode

Success criteria:
- [x] Rendering, collision, preload, and unload all use compatible ownership rules
- [ ] No leaked meshes/materials/collider hosts after level unload

### [ ] Phase 8. Validation, profiling, and rollout

Prove the middle-ground design before expanding it.

Tasks:
- [ ] Add EditMode tests for cell partitioning and collision policy resolution
- [ ] Add EditMode tests for collider fallback selection logic
- [x] Add runtime debug counters: total instances, visible cells, visible instances, active colliders
- [x] Add a simple scene gizmo/debug mode for collider activation radius and cell bounds
- [ ] Validate on at least one dense jungle level and one sparse level
- [ ] Compare before/after CPU frame cost for vegetation update
- [ ] Verify player collision against representative large trees and palms

Success criteria:
- [ ] Rendering cost decreases or stays flat despite added collision support
- [ ] Collision works only where intended
- [ ] No regressions in visual LOD selection or unload behavior

## Implementation Breakdown

### [x] Slice A. Data model and scene serialization

Files:
- `Assets/Scripts/Level/Services/FcVegetationTerrainService.cs`
- `Assets/Scripts/Level/Editor/FcLevelSceneBuilder.cs`
- optional new file: `Assets/Scripts/Level/Services/FcVegetationCollisionMode.cs`

Tasks:
- [x] Add collision mode enum: `None`, `PrimitiveCapsule`, `PrimitiveBox`, `LowLodMesh`, `PhysicsProxy`
- [x] Extend serialized vegetation type data with:
  - [x] `CollisionMode`
  - [x] `CollisionLodIndex`
  - [x] `CollisionDistance`
  - [x] `MaxActiveCollidersPerType`
  - [x] optional primitive tuning fields
- [x] Keep old scene data valid when new fields absent
- [x] In `FcLevelSceneBuilder`, assign safe defaults while serializing vegetation types
- [x] Add temporary heuristic for default collision mode from path/name/LOD presence

Done when:
- [x] Terrain scene stores enough metadata for later collision activation
- [ ] Existing built scenes still enter Play mode without null/ref regressions

### [ ] Slice B. `FcVegetationTerrainService` internal split

Files:
- `Assets/Scripts/Level/Services/FcVegetationTerrainService.cs`

Tasks:
- [ ] Split `RuntimeType` into clearer runtime pieces:
  - [ ] prototype render data
  - [ ] prototype collision data
  - [ ] flat instance runtime data
  - [ ] cell runtime data
- [ ] Move startup flow into separate methods:
  - [x] `BuildRuntimeTypes`
  - [x] `BuildRuntimeInstances`
  - [x] `BuildSpatialCells`
  - [x] `InitializeScratchBuffers`
- [ ] Move frame flow into separate methods:
  - [x] `CollectVisibleCells`
  - [x] `CollectVisibleInstances`
  - [x] `SubmitInstancedDraws`
  - [x] `UpdateNearbyColliders`
- [ ] Keep render output same before culling changes

Done when:
- [ ] Service readable by subsystem
- [ ] No behavior drift before optimization phase

### [ ] Slice C. Spatial partitioning

Files:
- `Assets/Scripts/Level/Services/FcVegetationTerrainService.cs`
- optional new file: `Assets/Scripts/Level/Services/FcVegetationSpatialCell.cs`

Tasks:
- [x] Choose first-pass cell size constant/serialized field
- [x] Bucket all instances into cells at startup
- [x] Compute `Bounds` per cell
- [x] Cache cell -> instance index list
- [x] Add frustum planes test per cell
- [x] Add cell distance reject before per-instance LOD selection
- [x] Iterate only visible candidate cells during render update
- [x] Add debug counters:
  - [x] total cells
  - [x] visible cells
  - [x] visible instances

Done when:
- [x] Update loop no longer scans full vegetation map each frame
- [ ] Dense level shows lower CPU cost in vegetation update

### [ ] Slice D. Type policy resolution

Files:
- `Assets/Scripts/Level/Services/FcVegetationTerrainService.cs`
- optional new file: `Assets/Scripts/Level/Services/FcVegetationTypePolicyResolver.cs`

Tasks:
- [x] Resolve per-type collision policy once at startup
- [x] Mark likely blocking tree-like assets
- [x] Keep bushes/grass default `None`
- [x] Clamp invalid `CollisionLodIndex`
- [x] Clamp invalid `CollisionDistance` and budget values
- [x] Add debug log/report for resolved policy per vegetation type

Done when:
- [x] Runtime has stable resolved policy object for each type
- [x] No policy logic buried in frame update

### [x] Slice E. Collider pool runtime

Files:
- `Assets/Scripts/Level/Services/FcVegetationTerrainService.cs`
- optional new file: `Assets/Scripts/Level/Services/FcVegetationColliderPool.cs`
- optional new file: `Assets/Scripts/Level/Services/FcVegetationColliderHost.cs`

Tasks:
- [x] Add pooled collider host GameObjects under service-owned root
- [x] Track active collider assignment: instance index -> host
- [x] Track free host list
- [x] Add nearest-first selection for blocking instances inside collision radius
- [x] Respect global/per-type active collider cap
- [x] Recycle hosts when instance leaves range or type no longer eligible
- [x] Clear pool cleanly on unload/destroy

Done when:
- [x] Active collider count bounded
- [x] Near player collision exists only for selected vegetation

### [ ] Slice F. Primitive collider path first

Files:
- `Assets/Scripts/Level/Services/FcVegetationTerrainService.cs`
- optional new file: `Assets/Scripts/Level/Services/FcVegetationColliderBuilder.cs`

Tasks:
- [x] Implement `PrimitiveCapsule` builder
- [x] Implement `PrimitiveBox` builder
- [x] Derive primitive size from prototype mesh bounds with scale compensation
- [x] Allow per-type override for height/radius/size
- [ ] Validate trunk-like trees first
- [x] Keep this path preferred for first rollout
- [x] Keep primitive collider setup allocation-lean during steady updates

Done when:
- [ ] Large trees can block player without mesh collider path
- [ ] First collision rollout stays cheap

### [ ] Slice G. Low LOD mesh collider path

Files:
- `Assets/Scripts/Level/Services/FcVegetationTerrainService.cs`
- `Assets/Scripts/Level/Services/FcLevelLoadService.cs`
- optional new file: `Assets/Scripts/Level/Services/FcVegetationColliderBuilder.cs`

Tasks:
- [x] Reuse imported LOD meshes from preloaded vegetation handle when available
- [x] Resolve collider mesh from configured `CollisionLodIndex`
- [x] Fall back safely when requested LOD mesh missing
- [x] Ensure shared mesh reused across pooled hosts, no duplicate instantiate
- [ ] Validate whether non-convex `MeshCollider` acceptable for static pooled hosts
- [x] Restrict mesh-collider mode to types that fail primitive fidelity

Done when:
- [x] Mesh collider path exists for selected types
- [x] Memory and CPU cost still bounded by pool

### [ ] Slice H. Physics proxy investigation

Files:
- `Assets/Scripts/Importer/Cgf/`
- `Assets/Scripts/Level/Services/FcVegetationTerrainService.cs`
- possible shared helper near `FcBrushGeometryPostProcessor`

Tasks:
- [x] Inspect CGF chunks/material naming for vegetation proxy data
- [x] Check whether brush proxy extraction logic reusable
- [x] If proxy data exists, define shared helper for vegetation collider extraction
- [ ] Add `PhysicsProxy` collision mode only after source proven reliable

Done when:
- [ ] Project knows whether vegetation proxy path real or fake hope

### [ ] Slice I. Lifecycle and ownership

Files:
- `Assets/Scripts/Level/Services/FcVegetationTerrainService.cs`
- `Assets/Scripts/Level/Services/FcLevelLoadService.cs`
- possibly `Assets/Scripts/Level/Services/FcLevelGeometryAssetHandle.cs`

Tasks:
- [x] Define ownership of collider source meshes
- [x] Ensure collider pool teardown happens before level scope release
- [x] Keep direct-import fallback path valid for editor standalone play
- [x] Avoid releasing preloaded shared mesh while pooled collider still references it
- [x] Add explicit destroy/reset path for service restart/domain reload edge cases

Done when:
- [x] No leaked references
- [ ] No unload-time missing mesh/collider errors

### [ ] Slice J. Tests and validation

Files:
- `Assets/Scripts/Level/Tests/Editor/`

Tasks:
- [ ] Add test for vegetation type policy resolution defaults
- [ ] Add test for cell bucketing and bounds generation
- [ ] Add test for visible-cell filtering with synthetic positions
- [ ] Add test for collider candidate prioritization and cap enforcement
- [ ] Add test for collision LOD fallback selection
- [ ] Add runtime validation checklist for dense level

Done when:
- [ ] Core logic covered by EditMode tests
- [ ] Manual validation path repeatable

## Implementation Order

Recommended execution order:

1. Phase 1
2. Phase 2
3. Phase 3
4. Phase 4
5. Phase 5
6. Phase 6
7. Phase 7
8. Phase 8

Rationale:
- do not add physics first to a renderer that still scans the whole map every frame
- do not hardcode collision rules before type policy exists
- do not wire preload ownership until collision source strategy is clear

## Concrete First Slice

The smallest useful first implementation slice:
- [ ] extend vegetation type data with collision policy fields
- [ ] refactor `FcVegetationTerrainService` into prototype/instance/cell stages
- [ ] add startup cell partitioning
- [ ] add visible-cell iteration
- [ ] add pooled primitive colliders for nearest tree-like vegetation only

This first slice should intentionally avoid mesh colliders. It gives the project a cheap proof that selective local vegetation collision works before adding low-LOD mesh collision complexity.

## Follow-up Options

After this plan is stable, optional upgrades:
- [ ] mesh-collider support from selected low LOD for trunks that need better shape fidelity
- [ ] importer support for explicit vegetation physics proxies
- [ ] `DrawMeshInstancedIndirect` or GPU-driven culling
- [ ] impostors/billboards for very far vegetation
- [ ] authoring/debug tooling for vegetation type classification in editor

## Risks

- low visual LOD meshes may be poor collision sources for some trees
- collider activation tied to `Camera.main` may be wrong if player and camera diverge
- too-small cells increase bookkeeping; too-large cells reduce culling benefit
- pooled mesh colliders may still be expensive if too many are active at once
- legacy vegetation data may not cleanly reveal which assets are actually blocking

## Decisions Captured Here

Chosen middle ground:
- keep instanced rendering
- do not use `LODGroup` for terrain vegetation
- keep mesh LODs for large vegetation
- add collision only for selected blocking types
- activate colliders only near player
- prefer primitive collision first, mesh collision second
