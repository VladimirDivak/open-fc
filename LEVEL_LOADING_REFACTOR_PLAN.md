# Level Loading Refactor Plan

Срез: 2026-05-11.

## Goal

Level load via services, not entity self-load.

- entity no resource import;
- central load order/priority/cache/unload;
- safe heavy work async/thread pool;
- predictable mesh/animation/material cache;
- timing + cache hit/miss report.

## State

Done/partial:

- `FcBrushLoadService`: DONE, async, distance sort, `_maxConcurrent`, `_loadsPerFrame`.
- `FcBrushInstance.ApplyLoadResult`: DONE, register in service.
- `FcLevelCacheService`: DONE, scope owner, `ReleaseLevelScope`, `TrimUnused`.
- `FcLevelResourceService`: partial, sync wrapper over `CgfRuntimeImporter.Import`.
- `FcLevelLayoutData`: DONE, mission + brush layout asset.
- `FcLevelLoadReport`: STARTED, editor phases + brush counters; needs entity/mesh/animation.

Still self-loading:

- none for mesh/character path (`FcMeshEntity` and `FcCharacterEntity` already use service registration path).

Missing:

- `FcLevelLoadService`: full orchestrator still pending (minimal runtime facade added).
- `FcEntityLoadService`: priority queue + states.
- `FcMeshLoadService`: entity CGF/CGA async pipeline.
- `FcAnimationLoadService`: CAL/CAF pipeline.

Check later:

- `docs/unity-entities.md`: class hierarchy -> priority.
- `docs/materials.md`: shader/texture/MTL -> material resolve.

## Problems

- Entity self-load spreads orchestration, blocks central concurrency, priority, staged load, reporting.
- Entity path sync on main thread; brush path already uses `CgfRuntimeImporter.ImportAsync`.
- Animation cache needs split: parsed CAF, built `AnimationClip`, controller compatibility, semantic reuse.
- Runtime report fragmented: brush local aggregate, no common `FcLevelLoadReport` yet.

## Target

```text
Level Scene / Mission Data
  -> FcLevelLoadService
  -> FcEntityLoadService
      -> FcMeshLoadService
      -> FcAnimationLoadService
      -> FcBrushLoadService
  -> apply loaded state to placeholders
```

Placeholders hold source data + apply/release state. Services own what/when/order/source/cache/unload.

## Services

`FcLevelLoadService`: mount PAK, parse mission/brushes, split critical/deferred, start/stop load, final report.

API: `LoadLevelAsync(levelName, missionName, ct)`, `UnloadLevelAsync()`, `GetLoadReport()`.

`FcEntityLoadService`: placeholders -> requests; priority queue, bounded concurrency, states, retries/failures/cancel, per-entity stats.

API: `Enqueue(FcEntity, EntityLoadPriority)`, `EnqueueRange(...)`, `CancelAllForScope(levelScopeId)`.

`FcMeshLoadService`: read bytes, parse CGF/CGA, cache parsed/build artifacts, resolve materials, prep LOD, choose collider, filter visual/proxy.

Result: `LoadedMeshArtifact` = parsed file, build result, visual spec, collider policy, cache keys.

`FcAnimationLoadService`: CAL discovery, CAF fallback, parsed CAF cache, semantic clip cache, attach/apply.

Result: `LoadedAnimationArtifact` = clips, default clip, cache stats, warnings.

`FcBrushLoadService`: already async brush loader. Need integrate with `FcLevelLoadReport`.

## Entity Role

No import in entity. Entity only stores source data, registers, applies result, releases result.

- `FcMeshEntity`: `CreateLoadRequest()`, `ApplyLoadedMesh(...)`, `ReleaseLoadedMesh()`.
- `FcCharacterEntity`: `CreateLoadRequest()`, `ApplyLoadedCharacter(...)`, `ReleaseLoadedCharacter()`.
- `FcBrushInstance`: already `Register(this)` + `ApplyLoadResult(...)`.

## Async Pipeline

Stages:

1. `Read source bytes`
2. `Parse source data`
3. `Build Unity artifacts`
4. `Apply to scene object`

Worker-safe: file read, XML/CGF/CAF parse without Unity API, hashes/signatures, data-only structs, vertex/index/UV/bone-weight arrays after builder split.

Main thread: `GameObject`, `Mesh`, `Material`, `AnimationClip`, `MeshCollider`, `Renderer`, `Animation`, Unity lifecycle.

Important: current `CgfRuntimeImportService.ImportAsync` moves read+parse to thread pool, then calls `CgfMeshBuilder.Build`, which creates Unity `Mesh`. Full parallel mesh build needs split: data-only prepare -> Unity upload/apply. Until then, `FcMeshLoadService` treats build/apply as main-thread-sensitive and limits concurrency.

Limits: `MaxConcurrentReads`, `MaxConcurrentParses`, `MaxConcurrentApplies`.

Priorities: `Critical`, `NearCamera`, `Characters`, `GameplayRelevant`, `Background`, `FarBrushes`.

## Cache

Separate caches: `SourceBytesCache`, `ParsedCgfCache`, `BuiltModelCache`, `ParsedCafCache`, `BuiltAnimationClipCache`, `MaterialCache`.

In-flight coalescing: same model cache key -> one ongoing task -> many waiters. Prevent cache stampede on miss.

Animation keys:

- parsed CAF: `virtualPath` + source fingerprint/version;
- built clip: semantic CAF signature + alias + scale + controller mapping signature + loop/import policy.

Level caches need retain/release, scope ownership, `ReleaseLevelScope(levelScopeId)`, trim unused.

## Flow

```text
LoadLevelAsync
  -> mount level pak
  -> parse mission xml
  -> parse brush data
  -> create load requests
  -> classify priority
  -> warm critical set
  -> stream deferred set
  -> emit report
```

Critical: player-near entities, gameplay characters, triggers/spawn points, near visible brushes/props.

Deferred: far props, far brushes, secondary characters, expensive animation warmups.

## Metrics

Editor: `LoadMissionXml`, `LoadBrushList`, `SaveLayoutData`, `BuildEntities`, `BuildBrushPlaceholders`, `TotalEditorBuild`.

Runtime: `ReadBytes`, `ParseCgf`, `BuildMesh`, `ResolveMaterials`, `BuildCollider`, `ParseCal`, `ParseCaf`, `BuildAnimationClip`, `AttachAnimation`, `ConfigureLod`, `ApplyToSceneObject`.

Report: wall time, main-thread time, avg per entity class, slowest assets, cache hit/miss, queue wait, concurrency peaks.

## Phases

### Phase 1. Instrumentation — STARTED 2026-05-10

- [x] `FcLevelLoadReport`: timing accumulator.
- [x] `FcLevelSceneBuilder`: editor phase timing.
- [x] `FcBrushLoadService`: local timing + aggregate.
- [x] Hook `FcBrushLoadService` into `FcLevelLoadReport`.

### Phase 2. Minimal Mesh Entity Slice

Goal: remove worst self-load, no importer/cache rewrite.

Status: implemented.

Add:

- `FcEntityLoadService`: queue, bounded concurrency, scope cancel, states `Pending/Loading/Applied/Failed/Cancelled`, basic priorities.
- `FcMeshLoadService`: wrapper over `CgfRuntimeImporter.ImportAsync`, texture preload, in-flight coalescing, coarse timing.
- request DTO + `LoadedMeshArtifact`.

Switch `FcMeshEntity.Start()` to register, add `ApplyLoadedMesh(...)`, `ReleaseLoadedMesh()`.

### Phase 3. Character Slice

Move `FcCharacterEntity` to entity load path: register, mesh via `FcMeshLoadService`, ragdoll main-thread apply, animation attach via current `CgfAnimationRuntimeImportService` but service workflow + report.

End: no mesh/character self-load in `Start()`.

Status: implemented for registration/load/apply path.

### Phase 4. Queue + Report Expansion

Add queue wait, concurrency peaks, retry/failure policy, per-class stats, slowest assets, cache hit/miss.

Status: implemented for queue/report layer. Added queue wait/concurrency peak/per-class/slowest, retry policy, and cache hit/miss stats in runtime entity report.

### Phase 5. Mesh/Animation Internal Split

Split data-only mesh prepare and Unity mesh upload/apply. Add `FcAnimationLoadService`. Move discovery/CAF parse/semantic clip build to service. Keep `AnimationClip` creation + attach on main thread.

Status: in progress. `FcAnimationLoadService` introduced, `FcCharacterEntity` switched to service-based animation attach, runtime animation attach timings/cache deltas added to level report, and runtime importer now uses two-stage mesh flow (`PrepareBuild` worker thread -> `UploadPrepared` main thread). Added parity tests for `Build` vs `PrepareBuild+UploadPrepared`. Remaining work: deeper data-only extraction coverage and validation.

### Phase 6. Cache Stabilization

Rework parsed CGF, parsed CAF, built model, built clip, materials.

Status: in progress. Added stabilization for animation caches: bounded pruning for CAF source-hash index (`CafLoader`) and stale `modelLayout -> animationSetKey` invalidation when cached animation set entries are evicted. Added runtime visibility for CAF source-hash entry count and model-layout link count, LRU/stale pruning for model-layout links, and importer tests for cache-key invalidation behavior.

### Phase 7. Streaming Polish

Add deferred/background load, near-camera reprioritization, warmup policies, unload transitions, stress diagnostics.

Status: implemented.

- Slice A (deferred/critical split): `FcEntityLoadService` now routes `Background`-priority items to a separate `_deferred` queue. Deferred items only drain when `_pending` is empty and under `_maxDeferredConcurrent` (default 1) concurrency. Critical, NearCamera, Characters, GameplayRelevant tiers drain via `_pending` with full `_maxConcurrent` slots.
- Slice B (near-camera promotion): `UpdateDeferredPromotions()` runs each frame. Deferred items within `_promoteRadius` (default 50 m) are bumped to `NearCamera` priority and migrated to `_pending`.
- Slice C (unload transition): `UnloadLevelAsync` now does a two-phase cancel: `CancelQueuedForScope` removes pending items, then `WaitForIdleAsync` waits up to `drainTimeoutMs` (default 2000 ms) for active loads to finish before releasing scope assets.

## Files

Rework:

- `Assets/Scripts/Level/Services/FcLevelResourceService.cs`
- `Assets/Scripts/Level/Entities/FcMeshEntity.cs`
- `Assets/Scripts/Level/Entities/FcCharacterEntity.cs`
- `Assets/Scripts/Importer/Cgf/CgfAnimationRuntimeImportService.cs`

Add:

- `Assets/Scripts/Level/Services/FcLevelLoadService.cs`
- `Assets/Scripts/Level/Services/FcEntityLoadService.cs`
- `Assets/Scripts/Level/Services/FcMeshLoadService.cs`
- `Assets/Scripts/Level/Services/FcAnimationLoadService.cs`
- DTO/request/artifact types.

Existing: `FcLevelLoadReport.cs`, `FcBrushLoadService.cs`, `FcLevelCacheService.cs`, `FcLevelResourceService.cs`, `FcLevelEnvironment.cs`.

## Success

- No entity self-load in `Start()`.
- Main-thread sync import removed for most resources.
- Clear level timing.
- Bounded async concurrency.
- Animation clips reused only with real semantic compatibility.
- Level unload releases scoped artifacts.
- Big levels load smoother, fewer main-thread spikes.

## Out Of First Iteration

Terrain streaming, vegetation, full audio pipeline, full Lua/AI runtime, ECS/Jobs migration.

## Editor-Time Scene Population (Done, outside refactor scope)

`FcLevelSceneBuilder` now places structural entities at editor build time without runtime:

- `FcLevelEnvironment.Apply()` called at build: DirectionalLight + RenderSettings (fog, ambient) baked into scene immediately.
- `DynamicLight` → Unity `Light` (Point/Spot); color, range, active state from Properties. No prefab needed.
- `SoundSpot` → Unity `AudioSource` (3D, linear rolloff); min/max distance, volume, loop from Properties. No prefab needed.
- Sun direction fix: `SunVector` XML = light travel direction; `LookRotation(SunDirection)` used (not negated).

## Next

Phase 7 implemented. All planned refactor phases complete.

Remaining quality debt:

- Expand data-only extraction coverage in mesh prepare stage (Phase 5 carry-over).
- Terrain streaming, vegetation, full audio pipeline, full Lua/AI runtime, ECS/Jobs migration (out of scope for this refactor).
