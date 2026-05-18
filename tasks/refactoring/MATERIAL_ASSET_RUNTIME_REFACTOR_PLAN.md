# Material Asset/Runtime Refactor Plan

Date: 2026-05-17

Scope: material **asset creation** (editor bake) + **runtime parsing/build**.

Sibling doc: `MATERIAL_PIPELINE_REFACTOR_PLAN.md` (shader-family parity, Phase 5). This doc = pipeline plumbing audit, not parity.

## Pipelines Today

### A. Asset creation (editor)

- `CgfMaterialEditorBakeService` — bakes `CgfMaterialChunk` -> `.mat` at `Assets/FCData/Materials/{cgf_path}/{TableIndex}.mat`, no textures embedded.
- `BakeOrUpdateLevelManifest` — parses CGFs, bakes each non-Multi chunk, writes `FcMaterialManifest` SO `{level}_manifest.asset`.
- `CgfMaterialProjectCacheInit` (`InitializeOnLoad`) — loads all manifests, builds combined dict, sets `CgfMaterialImportService.ProjectMaterialLookup`.

### B. Runtime parse/build

- `CgfParser.ReadMaterialChunk` (0x0744/0745/0746) -> `CgfMaterialChunk`.
- `CgfMaterialImportService.ResolveSubmeshMaterials` -> per-submesh chunk resolve (Multi / table-index / single).
- `GetOrBuild` -> build string cache key -> `CgfMaterialRuntimeCache.GetOrCreate` -> factory: `Instantiate(projectMat)` if lookup hit, else `CgfMaterialBuilder.Build` -> `ApplyResolvedTextures`.
- `CgfMaterialBuilder.Build` — classifier-driven URP Lit state.

## Phase 0 result (2026-05-17): premise corrected

P1 below was **wrong** — bake path is live. `FcLevelBuilderWindow` uses it: `BakeOrUpdateLevelManifest` (line 237, `BakeLevelMaterials`), `GetOrBakeMaterial` (line 544, pre-bake menu), `PostProcessGameObjectMaterials` (line 1193, `CacheSceneGeometryToProject`).

Bake path has **two roles**:

- **(a) editor prefab-cache** — `GetOrBakeMaterial` + `PostProcessGameObjectMaterials`. Cached project prefab cannot reference a non-persistent runtime `Material`; swap to `.mat` is mandatory. Keep.
- **(b) manifest -> runtime lookup** — `FcMaterialManifest` + `CgfMaterialProjectCacheInit` + `ProjectMaterialLookup`. `CgfMaterialProjectCacheInit` is `[InitializeOnLoad]` = **editor-only**; in a player build `ProjectMaterialLookup` is always null, so (b) currently pays off nowhere.

Decision (user 2026-05-17): keep (a); **fix (b) properly** so it works in builds.

## Problems

| # | Sev | Problem |
| --- | --- | --- |
| P1 | RETRACTED | Bake path is live (see Phase 0 result). |
| P2 | med | `(b)` is editor-only dead weight: `CgfMaterialProjectCacheInit` is `[InitializeOnLoad]`, never runs in a build, so `ProjectMaterialLookup` is null at runtime. Also iterates manifest via `SerializedObject` (no public iteration on `FcMaterialManifest`). Baked `.mat` reuse never reaches the shipped game. |
| P3 | med | `GetOrBuild` builds ~10-field interpolated string key every call (alloc churn). Key includes resolved texture virtual paths -> `ResolveTextures` (loads textures) runs **before** cache lookup, so every call re-resolves textures even on cache hit. |
| P4 | med | `CgfMaterialClassifier.Analyze` recomputed many times per chunk (`Build`, `ResolveTextures`, `RequiresUvScroll`, `PreloadTextures`, `CollectUniqueTexturePreloadRequests` twice/iter). Pure but allocs struct + `ToLowerInvariant` each call. |
| P5 | med | Two-pass texture load: `PreloadTexturesAsync` loads, then `ResolveTextures` loads again. Both iterate `BuildTexturePathCandidates` which allocs HashSet+List per slot per submesh. Heavy alloc during level load. |
| P6 | med | Emission-mask leak: `CgfMaterialBuilder.EmissionMaskByBaseTextureId` static dict, keyed by texture InstanceID, never cleared. Generated `Texture2D` never destroyed on level-scope release. InstanceID reuse risk after destroy+recreate. |
| P7 | med | Fallback/missing materials uncached. `BuildFallbackArray`, `ResolveMultiMaterialChild` null path, `BuildNoDraw` -> new `Material` per submesh, not registered in `CgfMaterialRuntimeCache` scope -> leak, never released. |
| P8 | low | `ApplyResolvedTextures` called twice on fresh build (`Build` line 66 + `GetOrBuild` line 433). |
| P9 | low | `0x0746` parser padding (`Skip(3)` "pack(4)") undocumented vs `CryHeaders.h MTL_CHUNK_DESC_0746`. Fragile. `selfIllum` float read+discarded — could drive `glowselfillum` emission instead of guessed `white*0.5`. |
| P10 | low | `CgfResolvedMaterialTextures` = 15-field struct passed by value; large copies. |
| P11 | info | Alpha-channel role (`GLOSS_DIFFUSEALPHA`/`ALPHAGLOW`/`DIFFUSEALPHA`/`OFFSETBUMPMAPPING`) lives in `Shaders/Illumination.ext` shader-flag bitfield, **not** in MTL chunk `Flags`. Classifier infers from shader-name string only — known approximation, acceptable unless `.ext` parsing added. |
| P12 | info | `BuildNoDraw` returns invisible material but comment says nodraw stripped at mesh-build level -> double-handled. Confirm no orphan invisible submeshes render. |

## Decision: keep (a), fix (b)

`asset-pipeline.md` arch = "Runtime-first with persistent Editor cache". Baked `.mat` is the persistent cache for project prefabs (a) — required. The manifest -> runtime bridge (b) is sound in intent but never reaches builds. Fix (b) by giving the runtime a way to load the level manifest (scene reference), so a player build reuses baked `.mat` instead of rebuilding every material.

`.mat` keeps no textures (runtime-injected, copyright); only shader state + colours — derived numbers, committable under `Assets/FCData/` cache policy.

## Plan

### Phase 1 — fix (b): manifest reaches builds

- [ ] `FcMaterialManifest`: add public `IReadOnlyList<Entry> Entries` accessor; drop the need for `SerializedObject` iteration.
- [ ] `CgfMaterialProjectCacheInit`: iterate via `Entries`, not `SerializedObject`.
- [ ] new runtime component `FcLevelMaterialManifestBinding` (MonoBehaviour) holding a serialized `FcMaterialManifest` ref; in `Awake`/`OnEnable` sets `CgfRuntimeImporter.MaterialService.ProjectMaterialLookup`, clears on destroy. Runs in builds.
- [ ] `FcLevelBuilderWindow.BakeLevelMaterials` / authoring build: attach `FcLevelMaterialManifestBinding` to the level root with the baked manifest ref so the saved scene carries it.
- [ ] keep `GetOrBuild` `Instantiate(projectMat)` once-per-cache-key (correct, SRP-batch friendly); no per-renderer churn. Just ensure lookup-hit path still runs after Phase 3 reorder.

### Phase 2 — classification once

- [ ] cache `CgfMaterialClassification` on `CgfMaterialChunk` (lazy field or compute-in-parser) — single source.
- [ ] update `Build`/`ResolveTextures`/`RequiresUvScroll`/`Preload*`/`Collect*` to read cached classification.

### Phase 3 — cache key + texture-load order

- [ ] cache key from **chunk identity** only: `SourceVirtualPath + ChunkID` (or `TableIndex`). Drop texture-path key fields.
- [ ] `CgfMaterialRuntimeCache.GetOrCreate`: lookup **before** `ResolveTextures`. Resolve+`ApplyResolvedTextures` only on miss (or when injected textures differ).
- [ ] precompute key as int/struct, avoid per-call string interpolation.

### Phase 4 — fallback + nodraw lifecycle

- [ ] route fallback/missing/nodraw materials through `CgfMaterialRuntimeCache` (shared keys: `__fallback`, `__nodraw`) so they release with level scope.
- [ ] one shared magenta + one shared nodraw material per cache, not per submesh.
- [ ] confirm P12: if mesh-build strips nodraw submeshes, `BuildNoDraw` only needed for collider path — document or remove.

### Phase 5 — emission-mask lifecycle

- [ ] move `EmissionMaskByBaseTextureId` out of static `CgfMaterialBuilder` into a scoped cache (level-scope keyed) or `CgfMaterialRuntimeCache`.
- [ ] destroy generated emission `Texture2D` on level-scope release.
- [ ] key by texture virtual path, not `InstanceID`.

### Phase 6 — texture preload/resolve dedup

- [ ] single texture-candidate resolution: resolve virtual paths once per chunk, reuse for preload + `ResolveTextures`.
- [ ] cache resolved candidate list on chunk or in a per-import map; stop re-allocating HashSet/List in `BuildTexturePathCandidates` per slot.

### Phase 7 — parser hardening

- [ ] cross-check `0x0746`/`0x0745`/`0x0744` offsets vs `CryHeaders.h MTL_CHUNK_DESC_*`; document each `Skip` with struct field.
- [ ] capture `selfIllum` into `CgfMaterialChunk.SelfIllum`; feed `glowselfillum` emission strength from it instead of `white*0.5`.

### Phase 8 — verify

- [ ] EditMode: classifier tests, texture-binding tests green.
- [ ] EditMode: new cache test — same chunk twice -> 1 cache entry, level-scope release destroys it.
- [ ] manual Unity: load representative level, check no magenta, no leaked materials/textures after 2x level reload.

## Execution status (2026-05-17)

- Phase 0: done — premise corrected (bake path live).
- Phase 1: done — `FcMaterialManifest.Entries` accessor; `CgfMaterialManifestLookup` shared builder; `CgfMaterialProjectCacheInit` drops `SerializedObject`; new `FcLevelMaterialManifestBinding` runtime component; `FcLevelBuilderWindow.AttachMaterialManifestBinding` wires it onto the level root.
- Phase 2: done — `CgfMaterialChunk.Classification` lazy-cached; all callers read it.
- Phase 3: done — cache key = `{sourceVirtualPath}#{chunkID}`; texture resolve moved inside the cache-miss factory; double `ApplyResolvedTextures` removed (P8).
- Phase 4: done — `CgfMaterialRuntimeCache` RefCount now counts distinct scopes (was inflated per call -> leak); shared `__shared_fallback` material, level-scoped; nodraw already cached via `Build`.
- Phase 5: done — emission mask moved to `CgfScopedTextureCache` (level-scoped, destroyed on release); keyed by base virtual path, not InstanceID; `CgfMaterialBuilder` static dict removed.
- Phase 6: partial — the worst offender (`Analyze` recomputed per texture pass) fixed via Phase 2. Remaining `BuildTexturePathCandidates` HashSet/List alloc dedup deferred: pure GC micro-opt, needs a Unity profiler run to justify the regression risk to texture path resolution.
- Phase 7: done — `selfIllum` captured into `CgfMaterialChunk.SelfIllum`, drives glow emission strength; `0x0746` offsets cross-checked against `CryHeaders.h MTL_CHUNK_DESC_0746` — parser confirmed correct (`Skip(3)` = `col_a` CryIRGB→float pad).
- Phase 8: pending — needs Unity (EditMode tests + manual scene). Static review done; no public API removed; material tests use distinct ChunkIDs + SourceVirtualPath so the new key holds.

Still open: P10 (`CgfResolvedMaterialTextures` 15-field struct by value) — left as a separate cleanup.

## Non-goals

- shader-family parity (covered by `MATERIAL_PIPELINE_REFACTOR_PLAN.md`).
- `Illumination.ext` shader-flag parsing.
- override service (`FcLevelMaterialOverrideService`, 1113 lines) — separate refactor; only touch where it calls importer material code.
- reviving material baking — deferred to full Bake&Strip / `CryAssetLink`.

## Files

- delete: `CgfMaterialEditorBakeService.cs`, `CgfMaterialProjectCacheInit.cs`, `FcMaterialManifest.cs`
- edit: `CgfMaterialImportService.cs`, `CgfMaterialBuilder.cs`, `CgfMaterialClassifier.cs`, `CgfMaterialChunk` in `CgfData.cs`, `CgfParser.cs`, `CgfMaterialRuntimeCache.cs`, `CgfTexturePathResolver.cs`, `FcLevelBuilderWindow.cs`
