# CGF Import Pipeline

## Entry points

| Path | Entry |
|------|-------|
| Runtime (game) | `CgfRuntimeImporter.ImportAsync(request, scopeId, ct)` |
| Runtime (sync fallback) | `CgfRuntimeImporter.Import(request, scopeId)` |
| Editor (smoke test / browser) | `CgfRuntimeLoadSmokeTest`, `CgfSourceBrowser` |

`CgfRuntimeImporter` — static facade over `CgfRuntimeImportService` singleton.

---

## Runtime import pipeline

```
CgfRuntimeImporter.ImportAsync
  └─ CgfRuntimeImportService.ImportAsync
       1. model cache hit? → return CgfRuntimeImportResult (refcount++)
       2. parsed cache hit? → skip I/O, go to step 5
       3. parsed in-flight coalescing (UniTaskCompletionSource per path)
            owner: ReadAllBytes + CgfParser.Parse on thread pool
            waiter: await same TCS
       4. store parsed in CgfRuntimeAssetCache (parsed slot)
       5. CreateSelectedMeshView (filter by SelectedMeshChunkId if set)
       6. model in-flight coalescing (UniTaskCompletionSource per modelCacheKey)
            owner: PrepareBuild + FinalizeBuild
            waiter: await same TCS
       7. CgfMeshBuilder.PrepareBuild on thread pool → PreparedBuild
       8. CgfMeshBuilder.FinalizeBuild on main thread → BuildResult (Mesh)
       9. store model in CgfRuntimeAssetCache (model slot)
      10. return CgfRuntimeImportResult { ParsedFile, BuildResult, Mesh }
```

### CgfRuntimeImportRequest fields

| Field | Default | Notes |
|-------|---------|-------|
| `VirtualPath` | — | lower-case VFS path, `.cgf`/`.cga` |
| `ImportSkeleton` | false | true → skinned path, extracts bones/bind poses |
| `ImportAnimations` | false | reserved, not yet used |
| `ImportScale` | 0.01f | standard Cry→Unity cm→m |
| `UseRuntimeMemoryCache` | true | false = always re-parse (editor tools only) |
| `SelectedMeshChunkId` | -1 | -1 = all mesh chunks combined |

---

## VFS read

`CgfResourceImportService.LoadRuntimeResourceBytes(virtualPath)`
  → `FcFileSystem.ReadAllBytes(virtualPath)` — reads from mounted `.pak`, decompresses on thread pool.

Supported extensions: `.cgf`, `.cga`.

---

## Parse layer (`CgfParser`)

`CgfParser.Parse(bytes)` → `CgfFile`

Reads chunk table, dispatches by `ChunkType`:
- `Mesh` → vertex/UV/normal/face arrays, face `MatID` per face
- `Node` → `Matrix44` (OLD row-vector), parent links, mesh ref
- `MtlName` → material name + sub-material list
- `BoneInitialPos` → bind-pose `Matrix43` per bone
- `BoneMesh` / `BONE_PHYSICS_COMP` → ragdoll data

`CgfFile.SourceVirtualPath` set after parse.

---

## Mesh build layer (`CgfMeshBuilder`)

### Static (ImportSkeleton = false)

1. Collect all `NodeChunks` that ref a `MeshChunk`.
2. Accumulate node transforms: `node.tm * parent.tm * ...` (row-vector multiplication, read row 3 for translation).
3. Transform vertices: `CryTransformConversion.NodeMatrixInImporterSpace` → Unity importer space `(x,z,-y)`.
4. Merge all node meshes into single `Mesh`, split by `MatID` → submeshes.
5. `BuildResult.SubmeshMaterialIds[submeshIndex]` = original Cry `MatID`.

### Skinned (ImportSkeleton = true)

1. Read bone hierarchy + `BoneInitialPos` bind matrices (`Matrix43`, separate path — do NOT reuse `Matrix44` conversion).
2. Reconstruct vertices from `CryLink.offset` in bind pose.
3. Build `SkinnedMeshRenderer`-compatible mesh with bone weights.

### Two-stage split (async)

- `PrepareBuild` — pure data work, runs on thread pool.
- `FinalizeBuild` — creates Unity `Mesh` object, must run on main thread.

---

## Material layer (`CgfMaterialImportService`)

Per-asset preload API (still used in migration fallback paths):

```csharp
await CgfRuntimeImporter.MaterialService.PreloadTexturesAsync(
    result.ParsedFile, result.Mesh, result.BuildResult?.SubmeshMaterialIds,
    levelScopeId, ct);
```

`PreloadTexturesAsync` — runs per-material texture loads with `UniTask.WhenAll` (parallel).
Texture types: diffuse, normal, specular, opacity.

Level-driven bulk preload API (primary runtime path):

```csharp
await CgfRuntimeImporter.MaterialService.PreloadTexturesForResultsAsync(
    successfulResults,
    levelScopeId,
    ct);
```

`PreloadTexturesForResultsAsync` dedupes by normalized texture path + linear/sRGB mode across all base+LOD results.

`ResolveSubmeshMaterials(parsedFile, mesh, submeshMaterialIds, scopeId)` — assigns loaded textures to URP materials per submesh. Called at GO build time.

---

## GO build layer (`CgfGameObjectBuilder`)

`CgfGameObjectBuilder.Build(BuildRequest)` → `BuildOutput { Root, MeshRenderer, SkinnedMeshRenderer }`

Static: `Root` GO gets `MeshFilter` (sharedMesh) + `MeshRenderer` (sharedMaterials).
Skinned: `Root` GO gets `SkinnedMeshRenderer`.

---

## LOD layer (`CgfLodImportService`)

`FindSiblingLodPaths(baseVirtualPath)` → scans VFS dir for `<base>_lod1.cgf`, `_lod2.cgf`, etc.

**Runtime import path (vegetation/brushes):** LOD paths fed back into `ImportAsync` → cached same as base.
Runtime `LODGroup` assembly is now shared via `FcLevelRuntimeLodGroupBuilder`.

**Editor path (smoke test / source browser):** `ConfigureLodGroup` reads bytes directly (no cache) — do not use for runtime; each call re-parses from VFS.

LOD thresholds: linear interpolation 0.70 → 0.02 screen height across LOD count.

---

## Cache architecture (`CgfRuntimeAssetCache`)

Two slots per asset:

| Slot | Key | Holds |
|------|-----|-------|
| parsed | `{path}|parsed` | `CgfFile` (reused across mesh configs) |
| model | `{path}|{scale}|{skeleton}|{meshChunkId}|{versionName}|mesh:…` | `BuildResult` + `Mesh` |

Ref-counted per `levelScopeId`. `ReleaseLevelScope(id)` drops all refs for that level. `TrimUnused()` removes zero-ref entries.

`MeshCacheVersionName` in `CgfMeshBuilder` — bump after mesh/basis algorithm changes to invalidate model cache.

---

## Coordinate spaces

| Layer | Cry `(x,y,z)` → Unity |
|-------|----------------------|
| CGF vertices / importer | `(x, z, -y)` — **importer space** |
| Level scene | `(x, z, y)` — **scene space** |

Brush placement bridges importer→scene via `FcLevelSceneBuilder.ApplyCryMatrix34`:
`SceneBasis * CryMatrix * Inverse(AssetBasis)`.

Negative `Z` scale at brush instance expresses handedness; brush materials disable backface culling.

---

## Key files

```
Assets/Scripts/Importer/Cgf/
  CgfRuntimeImporter.cs          — static facade
  CgfRuntimeImportService.cs     — cache + in-flight coalescing logic
  CgfRuntimeImportResult.cs      — result record
  CgfRuntimeImportRequest.cs     — request record
  CgfRuntimeAssetCache.cs        — ref-counted two-slot cache
  CgfResourceImportService.cs    — VFS read + path validation
  CgfParser.cs                   — binary chunk parser → CgfFile
  CgfData.cs                     — CgfFile + chunk POCOs
  CgfMeshBuilder.cs              — PrepareBuild / FinalizeBuild, BuildResult
  CgfGameObjectBuilder.cs        — Unity GO assembly
  CgfMaterialImportService.cs    — material + texture resolve/preload
  CgfLodImportService.cs         — sibling LOD discovery + LODGroup setup
  CgfAnimationRuntimeImportService.cs
  CgfRagdollBuilder.cs

Assets/Scripts/Level/Services/
  FcLevelLoadService.cs                — level-driven preload orchestration
  FcLevelGeometryPreloadPlanner.cs     — unique base/LOD request planning
  FcLevelGeometryPreloadPlan.cs        — request/result mapping and handle build
  FcLevelGeometryAssetHandle.cs        — shared base+LOD runtime handle
  FcLevelGeometryImportHelper.cs       — migration fallback import helper
  FcLevelGeometryResultOwnershipHelper.cs — shared release helper for owned import results
  FcLevelRuntimeLodGroupBuilder.cs     — shared runtime LODGroup assembly
```

---

## Current level runtime usage

Primary runtime path:

```
FcLevelLoadService.LoadLevelAsync
  1. load mission + brush list + supplement
  2. build brush and vegetation preload plans
  3. merge unique base+LOD requests by model key
  4. CgfRuntimeImporter.PreloadAsync(unique requests)
  5. CgfMaterialImportService.PreloadTexturesForResultsAsync(successful results)
  6. publish preloaded handles:
       - _brushPreloadedByPath
       - _vegetationPreloadedByPath

FcBrushLoadService / FcVegetationLoadService
  1. keep distance-based queueing
  2. try handle from FcLevelLoadService
  3. if found: only GO build/collider/LOD assembly on main thread
  4. if missing: migration fallback import path via FcLevelGeometryImportHelper
```

Current properties:
- dependency discovery and preload are level-driven
- geometry preload is deduped by normalized model cache key
- texture preload is deduped across all successful base+LOD results
- brush and vegetation runtime LOD assembly use the same builder
- fallback paths remain for compatibility when preloaded handles are absent
