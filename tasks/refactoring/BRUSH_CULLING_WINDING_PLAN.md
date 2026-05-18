# Brush/Entity Backface Culling — Winding Plan

Date: 2026-05-18
Status: not started — deferred from the nodraw/material debugging session.

## Problem

Almost every brush/entity material renders two-sided (`Render Face: Both`), not front-only.

`FcBrushGeometryPostProcessor.DisableBackfaceCulling` force-sets `_Cull = Off` on every
brush material. Reason: brush/entity instance transforms carry a reflection (negative-Z
scale), which inverts triangle winding; with normal back-face culling the geometry would
render inside-out, so culling is disabled wholesale.

## Why the reflection exists

CGF meshes are built in importer space `Cry(x,y,z) -> Unity(x,z,-y)`. Level placement is
scene space `Cry(x,y,z) -> Unity(x,z,y)`. The bridge `SceneBasis * Cry * Inverse(AssetBasis)`
flips Y -> one reflection -> `localScale.z` negative.

Placement paths (verified):

| Path | Transform | Reflects? |
| --- | --- | --- |
| Brush — `FcLevelLoader.ApplyBrushMatrix34` | `localScale.z = -sZ` | yes |
| Entity/Object — `FcLevelLoader.ApplyEntityTransform` | `localScale.z = -sZ` | yes |
| Vegetation — `FcVegetationTerrainService` | `localScale = Vector3.one * scale` (positive) | **no** |

Brush + entity reflect. **Vegetation does not** — it places the importer-space mesh with
identity rotation + positive scale, skipping the basis bridge. The CGF mesh is shared
(one `CgfRuntimeImporter` cache entry per file), so a single global winding flip cannot
serve both reflecting and non-reflecting consumers.

## Why this was deferred

A blind change risks breaking brush position/rotation, MeshCollider alignment, CGF pivot,
multi-node static geometry, entity rotations and skinned bind poses across the whole level.
`CLAUDE.md` ("Coordinate And Transform Rules") explicitly requires validating all of these
together, visually, in the Unity editor. That validation could not be done in the debugging
session.

## Options

### Option A — winding-flipped mesh variant (low risk, no basis math)

- `BuildResult` carries `Mesh` (importer winding) + `MeshFlipped` (reversed triangle winding).
- Brush + entity consumers use `MeshFlipped`; vegetation keeps `Mesh`.
- Remove `DisableBackfaceCulling`; let `_Cull` stay default (Back). Genuine two-sided
  materials (`2SIDED` flag, plants/bark family) keep `_Cull = Off` via `CgfMaterialBuilder`.
- Cost: a second mesh per CGF used by both reflecting and vegetation paths.
- No transform-basis math touched.

### Option B — unify the basis (correct, higher risk)

- Route vegetation through the same basis bridge as brush/entity so everything reflects.
- Then a single global winding flip in `CgfMeshBuilder.BuildVertexRemapping` (emit
  `V0, V2, V1`) is correct for all consumers; remove `DisableBackfaceCulling`.
- Vegetation orientation changes — it may currently be slightly wrong (trees ~symmetric,
  unnoticed). Needs visual confirmation.
- This is a transform-basis change — full multi-case visual validation required.

## Files

- `Assets/Scripts/Importer/Cgf/CgfMeshBuilder.cs` — winding (`BuildVertexRemapping` /
  `CreateUnityMesh`), `BuildResult`, `MeshCacheVersionName` bump.
- `Assets/Scripts/Level/Services/FcBrushGeometryPostProcessor.cs` — `DisableBackfaceCulling`
  removal; consumer mesh selection.
- `Assets/Scripts/Level/Data/FcLevelLoader.cs` — `ApplyBrushMatrix34` / `ApplyEntityTransform`
  (Option B only).
- `Assets/Scripts/Level/Services/FcVegetationTerrainService.cs` — Option B only.
- `Assets/Scripts/Importer/Cgf/CgfMaterialBuilder.cs` — confirm `2SIDED`/plants stay `_Cull=Off`.

## Verification

- Build a representative level. Brushes, entities, vegetation, skinned characters all
  render front faces only, none inside-out.
- MeshCollider alignment, brush pivot, multi-node static geometry unchanged.
- Genuine two-sided materials (plants, bark, `2SIDED` flag) still draw both faces.

## Recommendation

Option A — contained, no basis math, reversible. Revisit Option B only if the second mesh
per CGF proves a real memory problem.
