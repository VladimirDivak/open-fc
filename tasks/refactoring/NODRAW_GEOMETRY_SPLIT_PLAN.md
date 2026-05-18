# NoDraw Geometry Split Refactor Plan

Date: 2026-05-17

Scope: separate `nodraw`/proxy collision geometry from the visual mesh at geometry-build time.

## Problem

`nodraw` (FC1 collision-only shader, ~7900 chunks) still renders. Symptoms on `merc_cover.cgf`
(a character): a nodraw submesh stays in the visual mesh with the `BuildNoDraw` "invisible"
transparent material, which is fragile and shows up.

Root cause: nodraw stripping lives only in `FcBrushGeometryPostProcessor.ApplyRuntimeParity`,
called **only** by `FcBrushInstance`. Non-brush entities (`FcMeshEntity` / `FcCharacterEntity`)
never strip -> nodraw submesh + material reach the renderer. Stripping is also post-hoc: the
full mesh is built and cached, then rebuilt per instance.

## Decision

Move the split into `CgfMeshBuilder` (geometry creation). The built+cached mesh is then clean
for every consumer (brush, entity, character, LOD) with no per-consumer code. nodraw faces go
into a separate collider mesh; nodraw MatIDs never enter the visual `SubmeshMaterialIds`.

## Design

- `BuildResult` gains `Mesh ColliderMesh` (nodraw/proxy faces, single submesh; null when none).
- `CgfMeshBuilder.PrepareBuild` computes a collision-only MatID set from the parsed file.
- `CreateUnityMesh` partitions `sortedMatIDs`: visual submeshes for renderable MatIDs;
  collision MatIDs' triangles concatenated into `ColliderMesh` (full vertex buffer + tris).
- `MeshCacheVersionName` bumped (mesh output changes).
- `CgfRuntimeAssetCache.DisposeModelEntryUnsafe` also destroys `ColliderMesh`.
- Consumer side: visual is fixed everywhere for free. Only `FcBrushGeometryPostProcessor`
  switched to consume `BuildResult.ColliderMesh` for its `MeshCollider`; proxy-node and
  BoneMesh collider strategies kept as fallbacks. `CgfGameObjectBuilder` unchanged
  (no auto-collider); skinned characters strip-only, ignore `ColliderMesh`.

## Reliability rule

Face MatID -> material chunk mapping is fuzzy across CGFs (see the `+1` shift hack in
`TryBuildFromNoDrawFaces`). A misclassification that strips a *visible* submesh is worse than
the current bug. So the collision-only MatID set is **conservative**: a MatID is excluded only
when it confidently resolves to a collision-only material. Ambiguous MatIDs stay visible, and
`BuildNoDraw` remains as the invisible-material safety net for anything not excluded.

- MTL_MULTI root: face MatID == child index -> classify each child (covers `merc_cover`).
- single / table-index layout: classify each non-Multi material chunk by table index.

## Collision-only classification

Single authority `CgfMaterialClassifier`:

- `IsCollisionOnly(chunk)` = `Classification.IsNoDraw` OR `NameMarksCollisionOnly(chunk.Name)`.
- `NameMarksCollisionOnly` mirrors the old `IsNoDrawProxyMaterial`: name contains
  `nodraw` / `no_draw` / `physics_proxy` / `phys_proxy` / `$physics_proxy` / `proxy`.
- `FcBrushGeometryPostProcessor.IsNoDrawProxyMaterial` delegates to it.

## Plan

- [x] Phase 1: `CgfMaterialClassifier.IsCollisionOnly` + `NameMarksCollisionOnly`.
- [x] Phase 2: new `CgfNoDrawFaceClassifier.CollectCollisionOnlyMatIds(CgfFile)`.
- [x] Phase 3: `BuildResult.ColliderMesh`; `CgfMeshBuilder` split logic; `MeshCacheVersionName` -> v11.
- [x] Phase 4: `CgfRuntimeAssetCache` destroys `ColliderMesh`.
- [x] Phase 5: `FcBrushGeometryPostProcessor` consumes `ColliderMesh`; `IsNoDrawProxyMaterial` delegates; `CacheFormatVersion` 3 -> 4.
- [ ] Phase 6: verify in Unity — brush collider intact; merc_cover renders no nodraw; EditMode tests.

## Execution status (2026-05-17)

Phases 1-5 done. Static review: no compile errors; collider-mesh ownership correct
(`BuildResult.ColliderMesh` cache-owned, `FcBrushGeometryPostProcessor` leaves
`Artifacts.PhysicsColliderMesh` null in that path so `FcBrushInstance.OnDestroy` never
double-frees it). `CgfMeshBuilderTests` fixtures carry no material chunks -> collision set
empty -> submesh counts unchanged. Phase 6 needs Unity (batch blocked locally).

## Non-goals

- proxy-*node* (separate node named `*proxy*`) and BoneMesh collider strategies — kept as-is.
- skinned-character hit colliders — separate task; characters only strip visually here.
- vertex compaction of the collider mesh — keep full vertex buffer for now.
