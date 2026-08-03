# Audit Remediation Plan — 2026-08

Staged remediation of [`AUDIT_2026-08_REGISTER.md`](AUDIT_2026-08_REGISTER.md).
Stages are ordered by impact/effort, **not** by severity — Stage 1 is the cheapest fix with the
widest blast radius.

> **Status: NOT STARTED.** No stage has landed. Update the Stage Board below and the `St` column
> in the register as work completes, and append an entry to [`SESSION_LOG.md`](SESSION_LOG.md).

---

## How to use this document

1. Open [`SESSION_LOG.md`](SESSION_LOG.md) → read the latest entry → that is the current position.
2. Pick the lowest unfinished stage on the board unless the log says otherwise.
3. Work only inside that stage's **Files** list. If a fix wants to escape the stage, note it in the
   log and leave it — cross-stage drift is how these plans die.
4. On completion: tick the stage tasks, set `St` in the register, run the stage's **Verification**,
   append a log entry.

**One stage per commit series.** Commit subject references the finding IDs:
`fix(async): keep failure continuations on main thread [A-H01,A-M01]`.

---

## Stage Board

- [x] **Stage 0** — Repo hygiene / licensing · **S** · `A-M29` · done 2026-08-03
- [x] **Stage 1** — Async main-thread contract · **S** · `A-H01 A-H02 A-H03 A-M01 A-M02 A-M03 A-L11` · done 2026-08-03
- [x] **Stage 2** — Editor safety net · **S** · `A-H10 A-H11 A-H12 A-L17 A-L18` · done 2026-08-03 (A-H12 still `needs-repro`, everything else landed)
- [ ] **Stage 3** — Per-frame work removal · **S** · `A-H06 A-H07 A-M13 A-M14 A-M15 A-M16 A-L13 A-L14 A-L15`
- [ ] **Stage 4** — Water / URP correctness · **S** · `A-M17 A-M18 A-M19 A-M20 A-M21 A-M22 A-L19 A-L20 A-L21 A-L22 A-L23 A-L24`
- [ ] **Stage 5** — Mesh correctness · **M** · `A-H09 A-L25`
- [ ] **Stage 6** — Ownership, teardown, parser hardening · **M/L** · `A-H04 A-H05 A-M04 A-M05 A-M06 A-M07 A-M08 A-M09 A-L01…A-L11`
- [ ] **Stage 7** — Shared model-artifact cache · **M** · `A-H08 A-M10 A-M11 A-M12 A-L16`
- [ ] **Stage 8** — Test and CI foundation · **M** · `A-M24 A-M25 A-M26 A-M27 A-M28 A-L32 A-L35 A-L36 A-L37`
- [ ] **Stage 9** — Architecture cleanups · **L** · `A-M23 A-L12 A-L26…A-L34`

Stages 1–4 are independent of each other and can land in any order. Stage 6 should precede
Stage 7 (both touch cache ownership). Stage 8 should precede Stage 9 (refactors need a net).

---

## Stage 0 — Repo Hygiene / Licensing · DONE 2026-08-03

**Goal**: stop paid third-party assets from being redistributed through the public repo.

**Files**: `.gitignore`, repo settings.

**Tasks**:
- [x] Check the visibility of `github.com/VladimirDivak/open-fc` — **public**.
- [x] Add to `.gitignore`: `/Assets/AmplifyImpostors/`, `/Assets/AmplifyImpostors.meta`, `/Assets/BOXOPHOBIC/`, `/Assets/BOXOPHOBIC.meta`, `/Assets/BOXOPHOBIC+/`, `/Assets/BOXOPHOBIC+.meta`, `/Assets/Plugins/Sirenix/`.
- [x] `Assets/Plugins/Sirenix` (93 files, DLLs, was tracked since the root commit) — purged from history with `git-filter-repo --path Assets/Plugins/Sirenix --invert-paths` on the only public ref, force-pushed.
- [x] Document in `CLAUDE.md` that vendor asset directories are never committed, alongside the existing Far Cry data rule.

**How it was done** (procedure for the remaining local-only branches, or for future purges):
1. `git ls-remote --heads origin` first — only `refactor/level-builder-fcdata-cache` was actually on GitHub; `main`/`level-transform-fixes`/`level-vegetation-lod` were local-only and out of scope for the public-exposure fix.
2. Snapshotted `git status --porcelain=v1 -uall` to a file before touching anything, to diff against afterward.
3. `git-filter-repo` has no distro package installed and pip is externally-managed on this machine — installed into a throwaway venv (`python3 -m venv` + `pip install git-filter-repo`), not system-wide.
4. Backup: `git clone --mirror` from `origin` to `~/open-fc-backup-mirror-2026-08-03.git` (recovery point, independent of the working tree).
5. Rewrite in a **separate fresh mirror clone** (`git clone --mirror origin <scratch>`, never the working directory) — `git-filter-repo` refuses to run safely on a repo that isn't a fresh clone. Verified zero Sirenix blobs and zero `.dll` objects anywhere in `git rev-list --objects --all` afterward.
6. Force-pushed only the one affected branch: `git push --force <url> refs/heads/<branch>:refs/heads/<branch>`.
7. Back in the working directory: `git fetch`, `git stash push -u` (all tracked+untracked changes), `git reset --hard origin/<branch>`, `git stash pop`. Diffed the pre/post `git status --porcelain=v1 -uall` snapshots — byte-identical, confirming no WIP was lost.
8. `.gitignore` fix committed and pushed normally (fast-forward) on top of the rewritten history.

**Success criteria**: `git status --porcelain -uall` lists no vendor asset files — met. `git ls-files Assets/Plugins/Sirenix` empty — met. `git log --oneline -- Assets/Plugins/Sirenix` on the current branch empty — met. Repo size 12M → 7M on the remote-facing clone.

**Non-goals**: removing the packages from the working tree — they are needed to open the project. Purging the three local-only branches — deferred until/unless they are ever pushed (see register `A-M29`).

---

## Stage 1 — Async Main-Thread Contract · DONE 2026-08-03

**Goal**: restore the invariant "a continuation after `await` always resumes on the main thread",
then stop relying on it implicitly.

**Files**:
- `Assets/Scripts/FileSystem/FcFileSystem.cs`
- `Assets/Scripts/Level/Services/FcBrushLoadService.cs`
- `Assets/Scripts/Level/Services/FcVegetationLoadService.cs`
- `Assets/Scripts/Level/Services/FcEntityLoadService.cs`
- `Assets/Scripts/Level/Services/FcLevelLoadService.cs`
- `Assets/Scripts/Importer/Texture/TextureRuntimeImportService.cs`
- `Assets/Scripts/Importer/Cgf/CgfLodImportService.cs`
- `Assets/Scripts/Importer/Cgf/CgfRuntimeImportService.cs`

**Tasks**:
- [x] `A-H01` — `FcFileSystem.ReadAllBytesAsync`: pool-side read wrapped in `try/finally` with `await UniTask.SwitchToMainThread()` in the `finally`; `ThrowIfCancellationRequested` moved before the `SwitchToThreadPool` hop.
- [x] `A-H02` — `FcBrushLoadService`/`FcVegetationLoadService`: implemented as `await UniTask.SwitchToMainThread(CancellationToken.None)` as the **first line of `finally`**, not a per-callsite hop — this covers state mutation regardless of which awaited call threw, which a hop placed only after specific await lines would not (control skips straight past it to `catch`/`finally` on exception). `_activeCount--` stays a plain decrement since it's now guaranteed main-thread-only, matching `Update()`'s plain `++`; `Interlocked` was not needed once the thread is pinned.
- [x] `A-H03` — `FcEntityLoadService`: same reasoning — hop added right after the `LoadAsync` await (:392, covers the untouched failure branch at :404-414 that ran before any hop existed) and as the first line of both `catch` blocks and `finally`.
- [x] `A-M03` — `FcLevelLoadService.LoadLevelAsync`: split into a thin wrapper (sets the flag, `try/finally` resets it) and `LoadLevelInnerAsync` (the original body, unchanged). The original early `IsLoadInProgress = false` before spawn logic is left in place — that timing (other systems may react before spawn completes) looked intentional, not touched.
- [x] `A-M01` — `TextureRuntimeImportService`: one `_diagnosticsLock` guards both `HashSet`s + all 7 counters + the 2 format `Dictionary`s, including `GetRuntimeDiagnostics`'s read (enumerating `Dictionary` while another thread writes throws) and `ClearRuntimeCache`. Chose a single lock over lock+Interlocked mix — these are diagnostics-only, not hot-path, so the extra rigor of picking apart which field needs which primitive wasn't worth it.
- [x] `A-M02` — `CgfLodImportService`: `ConcurrentDictionary` + `GetOrAdd`; `RegexOptions.Compiled` dropped only from the per-basename entries (kept on the two fixed static patterns — those aren't the unbounded-growth concern). Added `ClearSiblingRegexCache()`, wired into `CgfRuntimeImporter.TrimUnused()` alongside the other cache trims.
- [x] `A-L11` — `CgfRuntimeImportService:350,465`: both coalescing joins now `await tcs.Task.AttachExternalCancellation(ct)`; the now-redundant explicit `ThrowIfCancellationRequested` after each was removed.

**Verification** — not run (see below), plan for next session or manual QA pass:
- Build a level whose CGF materials reference at least one missing normal map. Loading must complete and `LogReport` must fire.
- Add a temporary `Debug.Assert(UnityEngine.Object.CurrentThreadIsMainThread)`-equivalent (or a main-thread-id check) in the brush/entity `finally` blocks and confirm it never trips over a full level load.
- Cancel a load mid-flight (destroy the level root) and confirm `IsLoadInProgress` returns to `false`.
- **Not done this session**: Unity Editor was already running locally (batch mode unreliable per `SESSION_LOG.md`), no `.sln`/`Temp/obj` generated so `dotnet build` wasn't viable either. Verified instead by: full manual re-read of every changed region, brace-balance check across all 9 touched files, and confirming `git diff --stat` touched only the intended files. **Compile and the above runtime checks still need to happen in the Editor before this is trusted.**

**Non-goals**: redesigning the loader concurrency model; the `_loadsPerFrame` budget stays as is.

---

## Stage 2 — Editor Safety Net · DONE 2026-08-03 (A-H12 needs-repro)

**Goal**: editor tooling must not destroy user work or lock the Editor with no way out.

**Files**:
- `Assets/Scripts/Level/Editor/FcVegetationCatalogBuilder.cs`
- `Assets/Scripts/Importer/Editor/CgfAssetCacheService.cs`
- `Assets/Scripts/Level/Editor/FcLevelBuilderWindow.cs`
- `Assets/Scripts/Level/Editor/FcLevelSceneBuilder.cs`
- (touched, not originally listed) `Assets/Scripts/Importer/Editor/CgfImporterWindow.cs`,
  `Assets/Scripts/Level/Services/FcVegetationTerrainService.cs`

**Tasks**:
- [x] `A-H10` — `SaveCurrentModifiedScenesIfUserWantsTo()` guard at the top of `BuildCatalogScene`; dialog text now names scene closure, not just asset overwrite.
- [x] `A-H11` — `EnsureCachedPrefabsForProfiles` (:1148-1209) wrapped in `AssetDatabase.StartAssetEditing()/StopAssetEditing()`; per-item `SaveAssets`/`Refresh` removed from `CgfAssetCacheService.SaveAssets`; one `SaveAssets`/`Refresh` after the loop instead.
- [x] `A-H11` — bar switched to `EditorUtility.DisplayCancelableProgressBar`, `break` + warning log on cancel (partial cache kept, rest picked up next build). Audited all `DisplayProgressBar` call sites in this file (3 total): the geometry-cache loop (fixed) and `PreBakeLevelMaterials`'s per-CGF material-bake loop (same shape, also fixed — `StartAssetEditing` + cancelable bar). The third, `BakeOrUpdateLevelManifests`'s two bars (:253,:268), is not a per-item loop — two fixed-progress calls around opaque batch operations, nothing to cancel mid-item — left as is.
- [ ] `A-H12` — **not reproduced this session**: needs an interactive Editor pass (build a level, reimport an entity prefab, inspect fields) that no CLI session can drive. Still `needs-repro` in the register.
- [x] `A-L17` — traced to the actual calling loop: `CacheSceneGeometryToProject`'s `if (attachCachedPrefabsToScene) { ... }` block (the three `AttachCachedPrefabsToScene` calls whose per-instance override-material resolution is what reaches `PersistMaterialAsset`/`PersistTextureAsset` at :1896/:1953) — wrapped in `StartAssetEditing`/`StopAssetEditing`; left the leaf persist helpers untouched.
- [x] `A-L18` — added `FcVegetationTerrainService.SetAuthoringData(VegetationTypeEntry[], VegetationInstanceData[])`, a **public** method (not `internal` as originally proposed — matches the existing `Initialize()` convention `FcBrushInstance`/`FcVegetationInstance` already use for editor-time population, no `InternalsVisibleTo` plumbing needed). Assigns both the type-entry and instance arrays in one call; `FcLevelSceneBuilder` calls it once + `EditorUtility.SetDirty`, replacing the SerializedProperty loop for *both* arrays (the register only flagged `_instances`, but `_vegetationTypes` had the identical smaller-N problem right next to it).

**Side effect found while fixing `A-H11`**: removing the per-item flush from `CgfAssetCacheService.SaveAssets` would have silently broken `CgfImporterWindow` (the standalone "import one CGF" tool) — it was the *only* other caller and is a single-shot, non-batched invocation with no `StartAssetEditing` wrapper of its own. Re-added the flush there specifically (`AssetDatabase.SaveAssets()`/`Refresh()` in `CgfImporterWindow.SaveAssets`), verified via `grep` that these two call sites (`FcLevelBuilderWindow`'s batch loop, `CgfImporterWindow`'s single-shot) are the *only* callers of `CgfAssetCacheService.SaveAssets`.

**Verification**:
- With a dirty untitled scene open, run `OpenFarCry → Build Vegetation Catalog`: a save prompt must appear and Cancel must abort. **Not run** — needs the Editor.
- Build a level with a cold FCData cache: the import progress bar appears once after the loop, is cancelable, and cancelling leaves no half-written cache (or logs that it did). **Not run.**
- Compare build wall-clock before/after on the same level; record the numbers in the log. **Not run.**
- Compile-level check only, same constraints as Stage 1 (batch mode unreliable, no `Temp/obj`): manual re-read of every changed region + brace-balance check across all 6 touched files. **The above three checks need an actual Editor session — do them before trusting this stage in Play Mode / a real build.**

**Non-goals**: splitting `FcLevelBuilderWindow` (that is Stage 9).

---

## Stage 3 — Per-Frame Work Removal

**Goal**: stop recomputing immutable data every frame. Everything here derives from state the
scene builder baked and that never changes at runtime.

**Files**:
- `Assets/Scripts/Level/Services/FcBrushLoadService.cs`
- `Assets/Scripts/Level/Services/FcEntityLoadService.cs`
- `Assets/Scripts/Level/Services/FcVegetationTerrainService.cs`
- `Assets/Scripts/Level/Services/FcVegetationColliderSelector.cs`
- `Assets/Scripts/Importer/Cgf/CgfUvScrollRuntime.cs`

**Tasks**:
- [ ] `A-H07` — split the `_visibleCellScratch.Count == 0` guard from the legacy `_runtimeCells == null` guard; empty visible set → `return`. Same at :601 for `CollectColliderCandidates`. **Do this first — one line, largest single win.**
- [ ] `A-H06` — `FcBrushLoadService`: snapshot positions to `Vector3[]` at `Register`; `HashSet` for dedup; cached static `Comparison<T>`; re-sort only past a camera-movement threshold; add a `_distanceSortMaxPending`-style guard. Mirror the existing `FcVegetationLoadService` implementation rather than inventing a new one.
- [ ] `A-M15` — same treatment for `FcEntityLoadService:329` (cache the world position into `QueuedEntityLoad` at enqueue).
- [ ] `A-M14` — `Matrix4x4[] _instanceMatrices` built in `BuildRuntimeInstances` next to `_positions`/`_scales`; :575 becomes an array read.
- [ ] `A-M13` — `CgfUvScrollRuntime`: public `Refresh()` called from `Awake` and after LOD assembly (`FcBrushInstance.ApplyLoadResult`, `FcMeshEntity.ConfigureLodGroup`); remove discovery from `Update`; `Shader.PropertyToID` into `static readonly`; `HasProperty` once at clone time.
- [ ] `A-M16` — build the vegetation visible set **per camera** inside `RenderPipelineManager.beginCameraRendering` (cache keyed by `Camera`, mirroring `FcReflectionCache`) and pass the camera to `DrawMeshInstanced`. Removes the `Camera.main` dependency and fixes vegetation in the water reflection.
- [ ] `A-L13` — cached `Plane[6]` field for `CalculateFrustumPlanes`.
- [ ] `A-L14` — fill `_batchBuf` once per 1023-instance page, then issue one `DrawMeshInstanced` per submesh against the filled buffer.
- [ ] `A-L15` — `FcVegetationColliderSelector.Select` takes a caller-owned destination `HashSet` and `Clear()`s it.

**Verification**:
- Profiler, Play Mode, a dense level: capture main-thread ms and GC alloc/frame before and after, camera pointed at vegetation **and** turned away. Record both numbers in the log — turning away must be cheaper, not more expensive.
- Confirm vegetation appears in the water reflection after `A-M16`.
- Confirm brush registration no longer produces a multi-hundred-ms hitch on level start (Profiler timeline).

**Non-goals**: converting vegetation culling to Jobs/Burst; that is a separate future plan.

---

## Stage 4 — Water / URP Correctness

**Goal**: the water tier does what its settings claim, and stops leaking GPU resources.

**Files**:
- `Assets/Scripts/Rendering/Water/FcPlanarReflectionRendererFeature.cs`
- `Assets/Scripts/Rendering/Water/FcReflectionCache.cs`
- `Assets/Scripts/Rendering/Water/FcSsprRendererFeature.cs`
- `Assets/Scripts/Rendering/Water/FcWaterQualityTier.cs`
- `Assets/Scripts/Rendering/Water/FcWaterRuntimeBootstrap.cs`
- `Assets/Shaders/Water/FarCryWater.shader`, `Assets/Shaders/Water/FcSsprCompute.compute`

**Tasks**:
- [ ] `A-M19` — move `_cache.CollectGarbage()` above all early returns, right after the `srcCam` checks; `ReleaseAll()` when `FcWaterRegistry.Count == 0`.
- [ ] `A-M17` — `view.RtHDR = hdr` at :189; `view.HasRendered = false` in the reallocation branch of `EnsureRT`.
- [ ] `A-M18` — staleness budget on temporal reuse: force a re-render after N=4-6 frames or a `Time.time` threshold.
- [ ] `A-M20` — early-return from `OnBeginCameraRendering` when `!isActive`.
- [ ] `A-M21` — treat `ReflectionMaxShadowDistance <= 0` as "inherit source camera shadow distance"; `renderShadows = ReflectShadows && effectiveDistance > 0`.
- [ ] `A-M22` — drop `useMipMap`/`autoGenerateMips` from the SSPR result; clamp the shader sample to LOD 0 for the SSPR source while keeping mips on the mirror-cam path.
- [ ] `A-L19` — decouple the refraction toggle from the tier clamp.
- [ ] `A-L20` — startup validation: resolved tier → required renderer feature registered in the active URP asset → otherwise `LogError` and degrade. This is the single validation that ties `FcWaterSettings`, `FcWaterRuntimeBootstrap` and `PC_Renderer.asset` together.
- [ ] `A-L21` — gate `FcSsprRendererFeature` on `SystemInfo.supportsComputeShaders` + `HasKernel` for `KClear`/`KProject`/`KResolve`.
- [ ] `A-L22` — `#if UNITY_REVERSED_Z` around sky rejection and the depth key in the compute shader.
- [ ] `A-L23` — delete the dead `_MAIN_LIGHT_SHADOWS` `multi_compile`, or wire it up properly.
- [ ] `A-L24` — queue dead reflection views and drain from `endContextRendering`/`delayCall` instead of `DestroyImmediate` inside the pipeline callback.

**Verification**:
- Load a level with water, then a level without: RenderDoc / Memory Profiler must show no surviving mirror camera or reflection RT.
- Stand still on the shore with a moving object in view — the reflection must keep updating.
- Untick the renderer feature in `PC_Renderer.asset` — the second scene pass must disappear from the frame debugger.
- Set `ReflectShadows` on and confirm shadows appear in the reflection.

**Non-goals**: shipping SSPR — it stays unregistered in `PC_Renderer.asset` until `A-L20`/`A-L21` land.

---

## Stage 5 — Mesh Correctness

**Goal**: normal maps actually affect shading; generated meshes do not bloat scene files.

**Files**: `Assets/Scripts/Importer/Cgf/CgfMeshBuilder.cs`, `Assets/Scripts/Importer/Cgf/CgfMeshJobs.cs`,
`Assets/Scripts/Importer/Cgf/CgfMaterialBuilder.cs`, `Assets/Scripts/Level/Editor/FcLevelSceneBuilder.cs`

**Tasks**:
- [ ] `A-H09` — add `VertexAttribute.Tangent` to the vertex layout at :575-588 and compute tangents in the existing Burst job. Stop-gap if the job change is too large: `RecalculateTangents()` before :617, measured.
- [ ] `A-H09` — **bump `CgfMeshBuilder.MeshCacheVersionName` (:82)**; without it every cached mesh keeps the old layout.
- [ ] `A-H09` — if tangents are deferred instead, remove the `_NORMALMAP` enable block in `CgfMaterialBuilder.cs:113-118` so the cost is not paid for nothing. Do not leave both states half-done.
- [ ] `A-L25` — persist the procedural water grid to `Assets/FCData/Levels/<level>/WaterGrid.asset`, reusing the existing asset when size and cell count match; assign the loaded asset instead of the in-memory mesh.

**Verification**:
- Visual A/B on a brush with a strong normal map: lighting must respond to the map before/after.
- Re-run the CGF EditMode tests after the cache version bump.
- Level `.unity` file size before/after `A-L25`.
- Re-validate the transform checklist from CLAUDE.md (brush position/rotation/scale, MeshCollider alignment, CGF pivot, multi-node static CGF, entity rotation, skinned bind poses) — the vertex layout change touches the shared mesh path.

**Non-goals**: changing the coordinate-space rules.

---

## Stage 6 — Ownership, Teardown, Parser Hardening

**Goal**: no dangling native memory, no silent success on corrupt input, no unsynchronized index
publication. This stage is a prerequisite for any additive level loading.

**Files**:
- `Assets/Scripts/Level/Services/FcLevelCacheService.cs`
- `Assets/Scripts/FileSystem/PakArchive.cs`
- `Assets/Scripts/Importer/Cgf/CgfParser.cs`, `CafParser.cs`, `CgfRuntimeImportService.cs`, `CgfRuntimeAssetCache.cs`, `CgfMaterialRuntimeCache.cs`, `CgfScopedTextureCache.cs`, `CgfAnimationSetCache.cs`
- `Assets/Scripts/Level/Services/FcMeshLoadService.cs`
- `Assets/Scripts/Level/Data/FcBrushLoader.cs`, `FcLevelLoader.cs`, `FcLevelSupplementLoader.cs`, `FcTerrainLayerMaskDecoder.cs`
- `Assets/Scripts/Importer/Texture/DdsRuntimeDecoder.cs`

**Tasks**:
- [ ] `A-H04` — replace `CancellationToken.None` at `CgfRuntimeImportService:368,440` with a linked "scope alive" token; make `ReleaseLevelScope` skip keys present in `_inFlightParsed`/`_inFlightModel`; give brush and vegetation loaders a scope cancel + drain in `FcLevelCacheService.OnDestroy`.
- [ ] `A-H05` — `PakArchive`: build both indexes into locals inside the lock, publish via `Volatile.Write` behind an `_indexed` flag; take the lock in `Exists`/`FileCount`/`GetEntriesInDirectory`, materializing the latter into a list.
- [ ] `A-M04` — `TryRead` returns `false` and logs when `offset != data.Length`.
- [ ] `A-M05` — `CgfParser`: validate `(long)count * stride` against the remaining chunk window; `try/catch` + Dispose in `ReadMesh`; wrap both `Parse` chunk loops so `file.Dispose()` runs before the exception escapes.
- [ ] `A-M06` — same three fixes in `CafParser` (:205-216, :260-270, `Parse` :78-93).
- [ ] `A-M08` — `PreparedBuild` becomes `IDisposable`; `Dispose` in a `finally` on every exit path including the synchronous `Import`.
- [ ] `A-M07` — `StoreParsed` returns the canonical `CgfFile` so the caller disposes the loser. *(reproduce first)*
- [ ] `A-M09` — non-destructive clip eviction in `CgfAnimationSetCache`. *(reproduce first)*
- [ ] `A-L09` — `if (!entry.Scopes.Remove(scopeId)) continue;` before the decrement, in `CgfMaterialRuntimeCache` and `CgfScopedTextureCache`.
- [ ] `A-L10` — `FcMeshLoadService` coalescing retains once per consumer.
- [ ] `A-L08` — swap the order in `FcVegetationCatalogBuilder.ClearRuntimeCaches`.
- [ ] `A-L01`…`A-L07` — bound every file-supplied count against the remaining window before allocating (`FcTerrainLayerMaskDecoder`, `FcBrushLoader`, `DdsRuntimeDecoder`, `PakArchive` entry size), stream-based XML load with BOM detection, `DtdProcessing.Prohibit`, XML recursion depth cap.

**Verification**:
- Enable the Unity native leak detector (`Jobs → Leak Detection → Enabled With Stack Trace`); load and unload a level three times; the log must stay clean.
- Feed the parser a deliberately truncated PAK entry (copy a `.pak`, truncate one entry) — expect a logged error, not an empty GameObject.
- Enter and exit Play Mode ten times on a water level and check that native memory returns to baseline.

**Non-goals**: unifying the five caches behind one abstraction — that is the larger follow-up in
Stage 9 / the ownership item in the systemic list. This stage only makes each cache correct.

---

## Stage 7 — Shared Model-Artifact Cache

**Goal**: introduce the missing layer — "artifact per model", not per instance. Everything produced
*after* `CgfRuntimeAssetCache` is currently recomputed per instance or per frame.

**Files**:
- `Assets/Scripts/Level/Services/FcBrushGeometryPostProcessor.cs`
- `Assets/Scripts/Level/Entities/FcBrushInstance.cs`
- `Assets/Scripts/Importer/Cgf/CgfRuntimeAssetCache.cs`
- `Assets/Scripts/Importer/Cgf/CgfLodImportService.cs`
- `Assets/Scripts/Level/Entities/FcMeshEntity.cs`, `Assets/Scripts/Level/Services/FcLevelGeometryImportHelper.cs`
- `Assets/Scripts/FileSystem/PakArchive.cs`

**Tasks**:
- [ ] Design the key first: `(model cache key, strip signature)` → `{ physics collider mesh, filtered visual mesh, decal quads }`, level-scoped, refcounted, released on scope release. Write the shape into this document before implementing.
- [ ] `A-H08` — route `TryBuildFromProxyNodeMesh`/`BuildColliderMeshFromFaces` through the cache; one mesh and one PhysX cook per unique CGF instead of per instance.
- [ ] `A-M10` — route `BuildCompactedMesh` through the same cache so `sharedMesh` is genuinely shared again.
- [ ] `A-M11` — stop copying the shared cached mesh per instance at :327.
- [ ] `FcBrushInstance.OnDestroy` (:427-430) must not destroy shared meshes — release the refcount instead.
- [ ] `A-M12` — static memo in `CgfLodImportService` (`dir → string[]`, `modelPath → List<string>`), invalidated on mount change; make `PakArchive.GetEntriesInDirectory` an O(1) non-recursive lookup. Closes the unfinished Phase 1 in [`../refactoring/BUILD_SCENE_PERF_PLAN.md`](../refactoring/BUILD_SCENE_PERF_PLAN.md).
- [ ] `A-L16` — `HashSet<int>` in `TryBuildDecalQuad`, early bail on `tris.Length > 64`.

**Verification**:
- Frame Debugger: brushes sharing a CGF must batch again (static batching / GPU instancing restored).
- Memory Profiler: mesh count on a dense level must drop from ~instances to ~unique models. Record before/after in the log.
- Physics cook time on level load (Profiler `Physics.Bake`) before/after.
- Unload the level and confirm no shared mesh is destroyed while another instance still references it.

**Non-goals**: touching the `CgfRuntimeAssetCache` key format — the new cache sits beside it.

---

## Stage 8 — Test and CI Foundation

**Goal**: a green suite must mean something. Today it does not distinguish "everything works" from
"nothing ran".

**Files**:
- `Assets/Scripts/Level/Tests/Editor/OpenFarCry.Level.Tests.asmdef` (+ new test files)
- `Assets/Scripts/Importer/Tests/Editor/` (+ new test files, + new PlayMode asmdef)
- `Assets/Scripts/Importer/Cgf/CgfRuntimeLoadSmokeTest.cs`
- `.github/workflows/` (new)

**Tasks**:
- [ ] `A-M27` — add `OpenFarCry.Level.Editor` and `OpenFarCry.FileSystem` to the Level tests asmdef; promote the three reflected methods to `internal` + `InternalsVisibleTo`; delete the `Type.GetType`/`GetMethod`/`Invoke` calls in the three test files.
- [ ] `A-M26` — `CgfParserTests` with in-memory `byte[]` fixtures: valid header + MESH + NODE + MTL, truncated chunk, bad magic, chunk offset past EOF. Parser coverage must not depend on a local game install.
- [ ] `A-M24` — `FcLevelTransformRegressionTests`: pin the basis bridge (identity-rotation `Matrix34` → `(m03, m23, m13)` and `localScale.z < 0`; 90° Cry-Z rotation → expected Unity rotation).
- [ ] `A-M25` — `FcBrushGeometryPostProcessorTests`: synthetic `CgfFile` with three MatIDs (one proxy/nodraw-named), assert exactly the proxy submesh is stripped from the visual and retained for the collider, and that the 1-based fallback stays collider-only.
- [ ] `A-M28` — PlayMode test assembly (`includePlatforms: []`, `optionalUnityReferences: ["TestAssemblies"]`): load a scope with a stub resource service, assert entry counts > 0, `ReleaseLevelScope` + `TrimUnused`, assert counts return to zero and Unity objects read as fake-null.
- [ ] `A-M28` — move `CgfRuntimeLoadSmokeTest.cs` out of the runtime assembly.
- [ ] `A-L35` — assert destruction, not just refcounts, in `TextureRuntimeCacheTests`.
- [ ] `A-L36` — non-ignorable `UrpLitShaderIsAvailable` guard test.
- [ ] `A-L37` — use a non-zero vertex in a static-mesh test to pin the inlined job swizzle.
- [ ] CI workflow: EditMode + PlayMode run, **fail the build if the Ignored count exceeds a pinned threshold**. Without this the fixtures rot back to `Assert.Ignore`.

**Verification**: clone the repo into a directory with no `~/Documents/farcry-game`, run the suite —
parser, transform and stripping tests must execute and pass; the Ignored count must be below the
threshold.

**Non-goals**: full coverage. Pin the invariants CLAUDE.md already declares fragile, nothing more.

---

## Stage 9 — Architecture Cleanups

**Goal**: remove the duplication and the runtime/editor bleed the audit surfaced. Do this **after**
Stage 8 — these are refactors and need a net.

**Files**: see individual IDs in the register.

**Tasks**:
- [ ] `A-M23` — single source of proxy-material classification: `CgfNoDrawFaceClassifier.Classify`, or consume `BuildResult.ColliderMesh` in the vegetation service; delete the "+1" retry. *(reproduce with multi-MULTI content first)*
- [ ] `A-L29`, `A-L27`, `A-L26` — one path-normalization layer and one supported-extensions array; collapse the dead members on `IResourceImportService`.
- [ ] `A-L28` — hoist `EnsureDir`/`NormalizeLevelAssetKey`/level-cache path into one `FcLevelAssetPaths`.
- [ ] `A-L34`, `A-L33` — move `CgfSourceBrowser` and the `FcLevelLayoutDataV2` audit block into Editor assemblies.
- [ ] `A-L31` — extract the terrain block from `FcLevelSceneBuilder` into `FcTerrainSceneBuilder`.
- [ ] `A-L30` — replace the static `Current` locator on the eight services with level-root resolution, **if** additive level loading is on the roadmap. Otherwise mark `wontfix` with the reason.
- [ ] `A-L12` — clamp-extend the terrain heightmap's extra (+1) row and column.

**Verification**: full EditMode + PlayMode suite green; build a level and diff the generated scene
against a pre-refactor build for unintended changes.

**Non-goals**: splitting `FcLevelBuilderWindow` (3442 lines) in this pass — schedule separately once
it has test coverage.

---

## Systemic conclusions (why the stages are grouped this way)

1. **One hole in the async contract produces half the races.** `ReadAllBytesAsync` is the only place
   where "a continuation always resumes on the main thread" is violated, and A-H02, A-H03, A-M01,
   A-M02 all descend from it. Nowhere in the codebase is service state explicitly protected after an
   `await` — the whole model rests on an implicit guarantee. → Stage 1.
2. **There is no "artifact per model" layer.** `CgfRuntimeAssetCache` caches parsed data and the built
   mesh, but everything produced *after* it — physics collider mesh, compacted visual mesh, decal
   quads, sibling-LOD list, renderer discovery — is recomputed per instance or per frame. Five private
   workarounds instead of one cache. → Stage 7.
3. **Update loops recompute immutable data.** Queue sorts, `Matrix4x4.TRS`, `CalculateFrustumPlanes`,
   `GetComponentsInChildren`, `HasProperty(string)` — all derived from state the scene builder baked.
   The discipline is snapshot-on-register + dirty flag, not "recompute, it's cheap". → Stage 3.
4. **Binary parsers allocate before validating and have no `try/finally`.** The same pattern in
   `CgfParser`, `CafParser`, `DdsRuntimeDecoder`, `FcBrushLoader`, `FcTerrainLayerMaskDecoder`: a count
   from the file → `new T[count]`/`NativeArray` → read. None compares `count * stride` to the
   remaining chunk window; none disposes on the exception path. A separate defect class: truncation
   and corruption produce **silent success with an empty result** rather than an error — the worst
   possible failure mode when debugging levels. → Stage 6.
5. **Ownership semantics are spread across five caches with diverging rules.**
   `CgfRuntimeAssetCache`, `TextureRuntimeScopedCache`, `CgfMaterialRuntimeCache`,
   `CgfScopedTextureCache`, `CgfAnimationSetCache` each implement their own scope/refcount variant;
   only `TextureRuntimeScopedCache` checks `Scopes.Remove` before decrementing, and
   `CgfAnimationSetCache` evicts by LRU with no refcount at all. Coalescing gives N consumers one
   retain and all N release. This does not fire today only because there is always exactly one level
   and `TrimUnused` runs only at teardown — **any** attempt at additive loading or periodic trimming
   turns it into a dangling mesh. → Stage 6, then revisit.
6. **The water layer is configured by three unlinked sources of truth**: the tier in
   `FcWaterSettings`, the keyword set in `FcWaterRuntimeBootstrap`, and the renderer feature
   registration in `PC_Renderer.asset`. No pair is validated against another. → Stage 4, task `A-L20`.
7. **Editor tooling has no Unity-editor checklist.** No `StartAssetEditing` (except one call site),
   zero `DisplayCancelableProgressBar`, zero `SaveCurrentModifiedScenesIfUserWantsTo`, zero
   `RecordPrefabInstancePropertyModifications`. These are not separate bugs but one missing
   checklist. → Stage 2.
8. **The test strategy yields a false green.** EditMode-only + reflection binding to editor code +
   `Assert.Ignore` when game data is absent + no CI means a suite run cannot distinguish "works" from
   "did not execute". → Stage 8.
