# CGF/CAF Loading Refactor Plan

Snapshot: 2026-05-17. Scope: CGF/CAF runtime load + cache layer in `OpenFarCry.Importer`.
Validation: user runs EditMode tests manually. Agent no run suite.

## Verdict

Loading correct, hot inner loops well optimized (Burst jobs, zero-alloc binary
parse, in-flight coalescing). Problem = orchestration/cache layer around it:
bloated, inconsistent, dead weight. Four divergent cache subsystems,
near-duplicated sync/async import path, ~150 lines dead code, 265-line
animation-attach method, ~12 cache keys built per character spawn, full-file
hash recomputed every animation load. Works, but costs maintenance and not free
at runtime.

## Findings

### Dead code (~180 lines)

- `CgfMeshBuilder`: `BuildOneBoneWeight`, `BuildBoneWeights`,
  `TryBuildBindPositionFromLinks`, `SortLinksByDescendingWeight` +
  `CompareLinkWeightDescending`, `MergeSubmeshMap` — defined, zero callers.
  Superseded by `BoneWeightBuildJob` / inlined combined-mesh path.
- `CafParser`: `EnsureQuaternionContinuity`, `QuaternionFromRotationLog` —
  zero callers; continuity now `CafEnsureContinuityJob`, log→quat
  `CafRotationJob`.
- `CgfMeshBuilder.BuildMeshData:228` — `hasTexFaces` local assigned, never read.

### Bottlenecks

- `CafLoader.ComputeBytesHash` runs FNV over **entire file** every
  `GetOrParse`, including path-cache hits. PAK data immutable for session;
  full-buffer scan per animation source. Character with ~30 CAFs re-hashes ~30
  files **every** spawn.
- `CgfRuntimeImportService.PreloadAsync` = sequential `await` loop — no
  parallelism. In-flight coalescing infrastructure exists but preload never
  drives concurrent requests through it.
- `CgfRuntimeAssetCache.RemoveParsedKeyFromAllScopesUnsafe` /
  `RemoveModelKeyFromAllScopesUnsafe` scan **all** scopes per removed key
  (O(scopes × removed)) while `ParsedEntry.Scopes` / `ModelEntry.Scopes`
  already hold exact scope set — populated but unused for removal.
- `TryAttachAnimations` builds ~12 string cache keys per call, several via
  `Hash128.Compute` over fresh `StringBuilder`. Alloc-heavy on per-character
  spawn path.
- `BuildStaticCombinedMeshData` "append to existing NativeArray" branch
  reallocates + copies index buffer per extra submesh per part
  (O(parts × submeshes) reallocs). Comment already flags it "slow".

### Bloat

- `CgfRuntimeImportService.Import` (sync) and `ImportAsync` = ~80-line
  near-duplicates. Sync lacks in-flight coalescing — silent behavioral drift,
  latent bug source. Sync path still live (`FcVegetationTerrainService`,
  `FcLevelResourceService`).
- Diagnostics plumbing dominates animation path: 19-field `RuntimeCacheStats`,
  8 before/after stat-delta pairs interleaved with build logic inside 265-line
  `TryAttachAnimations`.

### Maintainability

- Four cache subsystems, three lifecycle models, no shared abstraction:
  - `CgfRuntimeAssetCache` — instance, refcount + level-scope, no LRU.
  - `CafLoader` — static, LRU + content-hash semantic dedup, no scope.
  - `CgfAnimationSetCache` — static, LRU, no scope/refcount.
  - `TextureRuntimeScopedCache` — yet another model.
  Fix in one not fix in others.
- Scope refcount semantics fragile: `TryRetain*` increments refcount **and**
  attaches scope; `ReleaseLevelScope` decrements once per key. Same key
  retained N times in one scope (N brush instances of one CGF) → refcount = N
  but scope-release subtracts 1. Bulk scope release not reliable safety net —
  works only if per-instance `Release` always perfectly balanced. Underflow
  guarded; leak not.
- Cache-key construction scattered: CGF keys private statics in
  `CgfRuntimeImportService`; CAF/anim keys = 12 builders in
  `CgfAnimationSetCache`. No single key contract.
- `CafLoader`'s 3-dictionary path→bytesHash→contentHash→parsed dedup with
  manual refcounted handles clever but over-engineered for immutable PAK files
  — keying by normalized virtual path alone covers real case.

## Phase Board

- [x] Phase 1: Delete dead code
- [x] Phase 2: Collapse sync/async import duplication
- [x] Phase 3: Fix CafLoader per-load full-file hash
- [x] Phase 4: Cache-layer cleanup (scope removal, key contract)
- [x] Phase 5: Slim the animation-attach path
- [x] Phase 6: Parallel preload (optional)

## Detailed Plan

### Phase 1 — Delete dead code

Lowest risk, highest signal. No behavior change.

- Remove 5 dead `CgfMeshBuilder` helpers + `CompareLinkWeightDescending`.
- Remove `CafParser.EnsureQuaternionContinuity` and `QuaternionFromRotationLog`.
- Remove unused `hasTexFaces` local in `BuildMeshData`.
- Validate: EditMode `CgfMeshBuilderTests` still green.

### Phase 2 — Collapse sync/async import duplication

`Import` and `ImportAsync` share path validation, key building, cache lookup,
selected-mesh view, artifact construction. Extract shared steps; keep sync
entry point thin wrapper (cache-lookup + synchronous build, no thread offload)
so callers (`FcVegetationTerrainService`, `FcLevelResourceService`)
unaffected. Goal: one definition of lookup→parse→build→store sequence so two
paths cannot drift.

### Phase 3 — Fix CafLoader per-load full-file hash

PAK data immutable per session. Options, simplest first:

- A: drop `SourceBytesHash` revalidation; trust path entry for session. Key
  `s_byPath` by normalized virtual path, return cached `CafFile` directly. Keep
  semantic dedup only if it measurably saves memory.
- B: if revalidation must stay, validate with file length + small fixed-size
  sample, not full FNV scan.

Either removes per-spawn full-buffer scan. Prefer A unless semantic dedup shown
to dedup meaningfully across distinct paths.

### Phase 4 — Cache-layer cleanup

- Use `ParsedEntry.Scopes` / `ModelEntry.Scopes` to drive scope removal in
  `CgfRuntimeAssetCache` instead of scanning all scopes.
- Decide scope semantics explicitly: either (a) scope tracks per-scope retain
  count per key and `ReleaseLevelScope` releases exactly that many, or (b)
  document scope release as best-effort and make per-instance `Release` sole
  authority. Pick one; current half-and-half leaks.
- Centralize cache-key construction into one `CgfCacheKeys` type so CGF and
  CAF/anim keys follow single documented contract.
- Stretch: define shared `IRuntimeAssetCache` abstraction (retain/release/
  trim/scope/stats) and converge four caches onto it. Large; do last or defer
  to own task.

### Phase 5 — Slim the animation-attach path

- Extract 8 before/after stat-delta pairs in `TryAttachAnimations` into
  `using`-scoped diagnostics helper (`CgfAnimAttachDiagnosticsScope`) so method
  body = build logic only.
- Split `TryAttachAnimations` into: resolve sources → build/fetch set → apply
  to `Animation` component. Target < 80 lines per method.
- Audit 12 keys: collapse any always derived together (e.g. `compatibilityKey`,
  `pathLayoutHash`, `loopPolicyKey` feed only `clipCacheKey`).

### Phase 6 — Parallel preload (optional)

`PreloadAsync` could fan out unique requests with concurrency cap and
`UniTask.WhenAll`, letting in-flight coalescing do its job. Verify no conflict
with `FcBrushLoadService`'s own distance-sorted concurrency limit before doing
it — if brush loading already governs concurrency, leave preload sequential and
delete unused expectation instead.

## Non-goals

- No coordinate/basis/bind-pose/material behavior change.
- No parser format changes (covered by `IMPORTER_OPTIMIZATION_PLAN.md`).
- No texture pipeline changes.
- No editor importer window/browser refactor.

## Risk Notes

- Phase 1 mechanical and safe.
- Phase 3 option A changes cache identity from content to path — confirm no two
  distinct virtual paths expected to share one parsed CAF.
- Phase 4 scope-semantics change touches level teardown; validate with
  load → unload → reload cycle and `GetCacheStats` (refcounts return to 0).
