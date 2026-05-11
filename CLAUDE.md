# CLAUDE.md

Guidance for Claude Code when working in this repository.

## Project

`open-farcry` is Unity 6 LTS port/reimplementation for Far Cry 1 assets and runtime behavior. Original Far Cry data not committed; tools read from local game install.

External paths:

- `~/Documents/farcry-game/` - Far Cry 1 install, must contain `FCData/*.pak`, `Levels/*/level.pak`, `.cry` level files.
- `~/Documents/farcry-sources/` - CryEngine 1 C++ source reference for binary formats and runtime behavior.

Generated/imported cache assets live under `Assets/FCData/`.

## Unity Setup

- Unity Editor: `6000.4.3f1`
- Render pipeline: URP `17.4.0`
- Main packages: UniTask, Cinemachine 3, Input System, AI Navigation, Timeline, SharpZipLib, Odin Inspector.
- Runtime settings asset: `Assets/Resources/FcFileSystemSettings.asset`
- Render assets: `Assets/Settings/PC_RPAsset.asset` and `Assets/Settings/PC_Renderer.asset`

Open project in Unity Editor, use Play Mode for runtime checks. Editor tooling under `OpenFarCry` Unity menu.

Headless Linux build:

```bash
unity -batchmode -projectPath "/home/vladimir/Unity Projects/open-farcry" \
  -buildTarget Linux64 -buildLinux64Player ./Build/OpenFarCry
```

EditMode tests, when present:

```bash
unity -batchmode -projectPath "/home/vladimir/Unity Projects/open-farcry" \
  -runTests -testPlatform EditMode
```

## Repository Notes

Checkout behaves as normal Git worktree. Run `git status`/`git diff` before edits and commits — Unity can introduce unrelated asset churn.

Avoid editing generated Unity folders unless explicitly needed:

- `Library/`
- `Temp/`
- `Logs/`
- `UserSettings/`

Prefer source edits in `Assets/Scripts/`, package changes in `Packages/`, Unity settings changes in `ProjectSettings/` or `Assets/Settings/`.

## Implemented Code

### File System

Assembly: `OpenFarCry.FileSystem`

Key files:

- `Assets/Scripts/FileSystem/FcFileSystem.cs`
- `Assets/Scripts/FileSystem/PakArchive.cs`
- `Assets/Scripts/FileSystem/FcFileSystemSettings.cs`
- `Assets/Scripts/FileSystem/Editor/FcFileSystemEditorInit.cs`

Current behavior:

- `FcFileSystem.Initialize()` loads `FcFileSystemSettings` from `Resources/FcFileSystemSettings`.
- Mounts all `*.pak` files under `<gameInstallPath>/FCData`.
- Mount order alphabetical; lookup reverse order (last mounted wins), matches patch PAK override behavior.
- Paths normalized to lower-case forward-slash virtual paths.
- `PakArchive` uses `Unity.SharpZipLib.Zip.ZipFile`.
- `ReadAllBytesAsync` offloads decompression to thread pool with UniTask, returns to main thread.
- In editor, `[InitializeOnLoad]` initializes VFS after domain reloads so editor tools work outside Play Mode.

Adding file-system features: preserve thread-safety around mount/index state, don't block Unity main thread for heavy decompression or parsing.

Resource-loading services must support both project-side cached assets under `Assets/FCData/` and original files read on demand from mounted PAK archives through `FcFileSystem`. Prefer cached project assets when they exist and settings allow; fall back to PAK-backed virtual path in builds or when settings request live PAK loading. Don't make importer or runtime resource code depend on editor-only cache assets as sole source of truth.

### CGF Importer

Assembly: `OpenFarCry.Importer`

Key files:

- `Assets/Scripts/Importer/Cgf/CgfData.cs`
- `Assets/Scripts/Importer/Cgf/CgfParser.cs`
- `Assets/Scripts/Importer/Cgf/CgfMeshBuilder.cs`
- `Assets/Scripts/Importer/Cgf/FcRagdollController.cs`
- `Assets/Scripts/Importer/Cgf/CgfRigDefinition.cs`
- `Assets/Scripts/Importer/Cgf/CgfRigSnapshotBuilder.cs`
- `Assets/Scripts/Importer/Cgf/CgfRigCacheSettings.cs`
- `Assets/Scripts/Importer/Editor/CgfImporterWindow.cs`
- `Assets/Scripts/Importer/Editor/CgfRigRegistry.cs`
- `Assets/Scripts/Importer/ImportAssetPaths.cs`

Current refactor snapshot (2026-05-09):

- Runtime-first service split in place:
  - `CgfResourceImportService` for virtual-path source loading.
  - `CgfRuntimeImportRequest` / `CgfRuntimeImportResult` runtime contracts.
  - `CgfRuntimeImportService` + `CgfRuntimeImporter` as runtime import entry/facade.
  - `CgfRuntimeAssetCache` with parsed/model entries, ref-counting, scope release, trim.
- Runtime-safe builders/services extracted from editor window:
  - `CgfGameObjectBuilder`, `CgfSkeletonBuilder`, `CgfLodImportService`,
    `CgfAnimationRuntimeImportService`, `CgfRagdollBuilder`, `CgfRagdollDiagnostics`.
- Runtime material path partially split:
  - `CgfMaterialBuilder`, `CgfMaterialImportService`, `CgfMaterialRuntimeCache`.
  - `CgfParser` now preserves material data for runtime material/color/texture-name resolution.
  - Multi-material resolution keyed by material IDs, not only Unity submesh order.
- Editor-only orchestration split in place:
  - `CgfImportEditorService`, `CgfImportRequest`, `CgfImportResult`.
  - `CgfAssetCacheService` for mesh/prefab persistence.
  - `CgfAnimationImportEditorService` + `CgfAnimationCacheService` for shared `.anim` cache.
- Source browsing/parsing UI support extracted to `CgfSourceBrowser`.
- `CgfImporterWindow` now thin UI/controller (~531 lines), not core importer.
- EditMode tests added for:
  - `CgfSourceBrowser` filtering/selection behavior.
  - `CgfRuntimeAssetCache` retain/release/scope/trim behavior.
- Runtime smoke test script:
  - `Assets/Scripts/Importer/Cgf/CgfRuntimeLoadSmokeTest.cs`
  - Runs scene-level import timing, optional animation/LOD/physics setup, cache scope release/trim checks, animation cache compatibility diagnostics (`[CgfAnimDiag]`).
- Brush/runtime collider handling prefers proxy/no-draw geometry:
  - `FcBrushInstance` derives collider faces from no-draw/proxy material slots first.
  - Proxy/no-draw submeshes stripped from visual mesh so collider geometry not rendered.
- Level-loading runtime code under `Assets/Scripts/Level/`, orchestration still transitional:
  - entity components still contain self-loading behavior in places;
  - current level runtime path still mostly synchronous and main-thread-heavy;
  - planned refactor documented in `LEVEL_LOADING_REFACTOR_PLAN.md`.

Animation runtime cache status (2026-05-09):

- `CgfAnimationRuntimeImportService` uses layered runtime cache:
  - path cache (`virtualPath -> source/content hash`) + semantic CAF cache (`contentHash -> CafFile`);
  - semantic clip cache (`semanticClipKey -> normalized track data`);
  - bound Unity clip cache (`clipKey -> AnimationClip`);
  - animation-set cache (`animFp + layout + setHash + scale + version -> alias->clip map`) with model-layout link reuse.
- Clip keys versioned (`ClipBuildVersion`, `LoopPolicyVersion`), include animation-compatibility + layout identity to avoid unsafe reuse.
- Runtime diagnostics first-class:
  - cache counters include `cafPath`, `cafSemantic`, `clip`, `set`, `semClip` hit/miss;
  - per-model diagnostics include `animFp`, `layout`, `set`, `clipBuild(semHit/semMiss)`, `missingTracks`.
- Expected behavior on mercenary variants:
  - identical model/layout gets animation-set reuse;
  - animation-compatible but different layout reuses semantic CAF/clip data, then rebuilds bound clips.
- Known limitation:
  - large `missingTracks` warnings still present for some mercenary assets (unmapped controller IDs); needs skeleton/controller mapping cleanup, not cache-key tuning.

Work status (2026-05-09):

- Added animation import pipeline for character models:
  - `CAL` parsing (`$AnimDir`/`$AnimationDir` directives, dummy `?` entries, fallback to `<model>_*.caf`).
  - `CAF` parsing for controller/timing chunks (`0x0827`, `0x0826` where available).
  - Legacy Unity `Animation` clip generation (`localPosition`/`localRotation`) and attach to imported prefab.
- Fixed duplicate/empty clips in Unity `Animation` component by rebuilding clip list before attach/save.
- Added rig snapshot/reuse pipeline for skinned characters:
  - `CgfRigDefinition` ScriptableObject stores rig fingerprint, animation fingerprint, bone names, parent indices, remap arrays, controller mapping, bind poses.
  - `CgfRigSnapshotBuilder` builds strict rig fingerprint and animation-compatible fingerprint.
  - `CgfRigRegistry` resolves rigs from memory/project cache with exact and animation-compatible matching.
- Added dev/prod rig cache policy:
  - `CgfRigCacheSettings` (`Resources/CgfRigCacheSettings`) supports `ProjectOnly`, `MemoryOnly`, `Hybrid`.
  - Player runtime always resolves to memory-only; editor can combine project + memory.
- Added shared animation cache:
  - Clips saved under `Assets/FCData/AnimationCache/...`, reused across characters/LODs.
  - Cache key content-based from generated `AnimationClip` curves/bindings (not source filename) — identical clips dedupe to one shared asset.
  - File naming preserves readable alias prefix (`<alias>_<hash>.anim`).
- Added runtime animation cache reuse pipeline for mercenary-heavy loads:
  - compatibility diagnostics (`animationFingerprint`, `pathLayoutHash`, `animationSetHash`) and summary report (`[CgfAnimDiag]`);
  - semantic CAF dedup across paths (`cafPathHit/miss`, `cafSemanticHit/miss`);
  - semantic clip cache + bound clip cache split;
  - animation-set cache and model-layout linking for fast repeated attach on identical rigs/layouts.
- Added automatic loop detection for imported legacy clips:
  - CAF start/end transform continuity heuristics + alias hints (`idle/walk/run/...`) + one-shot hints (`jump/reload/death/...`).
  - Applies both `clip.wrapMode` and editor clip setting `loopTime`.
- Added `BoneAnim` parsing (`0x0290`) and controllerID-to-bone-path mapping for clip curve binding.
- Added ragdoll import path from CGF `BONE_PHYSICS_COMP`:
  - `BoneMesh` chunks parsed, can generate per-bone `BoxCollider`s.
  - Optional ragdoll creation adds `Rigidbody` + `ConfigurableJoint` on physics bones.
  - `FcRagdollController` toggles animated/physics states at runtime (`SetAnimated`/`SetRagdoll`).
- Added LOD discovery/configuration in importer:
  - Sibling `*_lodX` files discovered and imported as LOD children.
  - `LODGroup` configured automatically from available levels.
- Fixed ragdoll bone hierarchy correctness:
  - Skeleton hierarchy creation prefers current-file `BoneAnim` parent links.
  - Bind-pose diagnostics (`[CgfImporter][Diag]`) available; showed zero bind errors after fix.
- Fixed CGF physics-angle sentinel handling:
  - Extreme values (e.g. `±1e10`) treated as unconstrained axes, not real limits.
  - Axis mapping prefers constrained axes for `AngularX`, improving hinge-like joints (knees/elbows).
  - Added joint diagnostics (`[CgfImporter][JointDiag]`) for axis/limit verification.
- Updated skeleton/bind-pose handling:
  - `BoneInitialPos` matrix parsing corrected for translation row in `SBoneInitPosMatrix`.
  - Bindposes built as inverse of default global pose, with scale removal (`NoScale`-style).
  - Bone local scales forced to `Vector3.one` when reconstructing transforms from bind matrices.
- Fixed major character skinning/animation mismatch:
  - `BoneNameList 0x0744` uses `NAME_ENTITY.name[64]`; reading 32 bytes corrupts bone names.
  - Cry runtime remaps `CryLink.BoneID` into hierarchy/runtime bone indices; Unity import now mirrors this for bone weights, bone names, bind poses, hierarchy reconstruction.
  - Skinned mesh vertex positions must be reconstructed from `CryLink.offset` in bind pose (`boneDefaultGlobal.TransformPointOLD(offset) * weight`). Some models store raw mesh vertices in offset space — using raw `CryVertex.P*` with Unity bindposes makes pivots drift and animated vertices explode.
  - Cry OLD row-vector matrices (`TransformPointOLD`, `SetTranslationOLD`) must be converted into Unity column-vector matrices before basis conversion.
  - Coordinate conversion baked into mesh/bindpose/bone/animation data with Z-up to Unity Y-up rotation `(x, y, z) -> (x, z, -y)`.
  - Imported prefab roots must stay ergonomic: position zero, identity rotation, scale one.

Current behavior:

- `CgfParser` validates `FILE_HEADER` and chunk table bounds, computes per-chunk sizes from offsets, rejects invalid offsets.
- Implemented chunk support: `Mesh`, `Node`, `BoneNameList` (`0x0744` and `0x0745` variants), `BoneInitialPos` (`0x0001`).
- Parser handles alignment/padding-sensitive layouts for mesh/node chunks (bool fields before ints/matrices).
- Parsed data keeps `ChunkID` links and multiple mesh chunks (`MeshChunks`, `MeshByChunkID`, `NodeByChunkID`), not only single mesh.
- Importer selects primary mesh via `Node.ObjectID -> MeshChunkID` (fallback: first mesh chunk).
- Importer discovers and builds sibling LODs via `_lodX` filename suffix, applies Unity `LODGroup`.
- Mesh build keeps UV V-flip (`1 - v`), groups faces into Unity submeshes by `MatID` (material count tied to submesh count).
- Runtime material assignment resolved through parsed material chunks and submesh `MatID` mapping, not only submesh index.
- `Import Skeleton` toggle in importer window:
  - ON: creates `SkinnedMeshRenderer`, applies mesh bone weights/bindposes, builds bone hierarchy from node data + bind-pose-derived local transforms.
  - OFF: imports as plain `MeshFilter` + `MeshRenderer` for geometry debugging.
- Additional importer toggles:
  - `Import Physics Box Colliders` (from `BoneMesh`/bone physics data)
  - `Import Ragdoll Bodies/Joints` (requires physics collider import)
- Rig reuse behavior:
  - Exact reuse requires strict bone/index compatibility.
  - Animation-compatible reuse can share controller-to-bone mapping and clip cache even if raw bone order differs.
  - Hierarchy from cached rig applied only when structural compatibility checks pass; otherwise rebuilt from current source (`BoneAnim`/`Node`) to avoid pose corruption.
- Coordinate-system/scale conversion baked into imported data. Don't reintroduce negative root scale or final root rotation as shortcut — makes prefabs hard to use and hides bind/animation-space mismatches.
- Current coordinate conversion remains sensitive. Changing matrix/transform conversion: validate mesh placement, brush placement, yaw rotation, bind poses, and animation together — don't patch only one stage.
- Caching/saving deterministic by source virtual path:
  - mesh: `Assets/FCData/<virtual_path_without_ext>.asset`
  - prefab: `Assets/FCData/<virtual_path_without_ext>.prefab`
- Shared animation cache path:
  - clips: `Assets/FCData/AnimationCache/<hash_prefix>/<alias>_<content_hash>.anim`
- On import with saving enabled, existing cached prefab can be reused instead of rebuilding if compatibility checks pass (currently UV0 presence + submesh/material count).
- Cache compatibility also checks generated mesh name/version (`CgfMeshBuilder.MeshCacheVersionName`) so older meshes rebuild after importer-space or skinning changes.

Treat CGF importer as incremental and format-sensitive. Changing binary parsing: cross-check against CryEngine source in `~/Documents/farcry-sources/`, especially ResourceCompiler and CryChunkedFile code.

Detailed importer notes in `~/Documents/farcry-sources/docs/asset-formats.md`. Read before changing CGF/CAF transform, bind pose, bone mapping, or animation code.

### Level Loading

Assembly: `OpenFarCry.Level`

Key files:

- `Assets/Scripts/Level/Data/FcLevelLoader.cs`
- `Assets/Scripts/Level/Editor/FcLevelSceneBuilder.cs`
- `Assets/Scripts/Level/Services/FcLevelResourceService.cs`
- `Assets/Scripts/Level/Entities/FcMeshEntity.cs`
- `Assets/Scripts/Level/Entities/FcCharacterEntity.cs`
- `Assets/Scripts/Level/Entities/FcBrushInstance.cs`
- `LEVEL_LOADING_REFACTOR_PLAN.md`

Current behavior:

- Mission/entity/brush parsing exists, can assemble basic scene from Far Cry level data.
- Terrain, vegetation, complex environment systems still outside implemented level path.
- `brush.lst` parsing and brush scene assembly in place, including material-table parsing for proxy/no-draw handling.
- Level runtime loading not fully service-driven:
  - some entity types still trigger resource loading from own lifecycle methods;
  - main runtime path still mostly synchronous;
  - timing/instrumentation for real level loads still incomplete.
- `FcFileSystem.ReadAllBytesAsync(...)` exists and should be preferred for future level-load refactors, but current pipeline doesn't use async systematically.

Changing level loading: prefer moving logic toward centralized services and explicit load requests rather than expanding self-loading MonoBehaviour code.

Known limitations:

- `.cga` currently treated as geometry preview; controller/timing/`*.anm` animation pipeline not implemented.
- No automatic Unity `Avatar` generation or Mecanim retarget setup.

## Target Architecture

```text
FCData/*.pak
  -> FcFileSystem / PakArchive
  -> format importers
  -> Unity runtime objects or cached assets under Assets/FCData/
```

Planned/importer areas:

- `.cgf` / `.cga` static and animated meshes
- `.dds` textures
- `.cry` level files
- `.lua` scripts and mission/entity behavior
- level PAK mounting with bind roots such as `levels/<level-name>`

## Coding Conventions

- Use C# namespaces matching assemblies, e.g. `OpenFarCry.FileSystem` and `OpenFarCry.Importer.Cgf`.
- Keep runtime assemblies separate from editor-only code via `Editor/` folders and editor asmdefs.
- Use UniTask for async Unity-facing work following project patterns.
- Prefer explicit binary parsing with `BinaryReader`; document offsets and chunk assumptions where format is ambiguous.
- Preserve path normalization semantics in VFS code: lower-case, forward slashes, trimmed leading/trailing slashes.
- Don't commit or generate original Far Cry copyrighted data into repo.
- Avoid broad asset churn from Unity serialization unless task requires changing those assets.

## Useful References

CryEngine source reference path:

- `~/Documents/farcry-sources/CryCommon/`
- `~/Documents/farcry-sources/ResourceCompilerPC/ChunkFileReader.cpp`
- `~/Documents/farcry-sources/ResourceCompilerPC/CryChunkedFile.cpp`
- `~/Documents/farcry-sources/Cry3DEngine/`

Far Cry install reference path:

- `~/Documents/farcry-game/FCData/*.pak`
- `~/Documents/farcry-game/Levels/*/level.pak`
- `~/Documents/farcry-game/Levels/*/*.cry`

Source documentation (`~/Documents/farcry-sources/docs/`):

- `docs/README.md` - map of all docs and where to look for specific topics
- `docs/asset-formats.md` - CGF/CGA/CAL/CAF binary formats + Unity import rules (OLD matrices, BoneID remap, skinned vertex reconstruction, CAF controller chunks); read before any parsing/skinning/animation change
- `docs/unity-architecture.md` - C# vs Lua split strategy: what goes into C# (engine/runtime), what stays in Lua (AI behaviors, weapon params, game rules, balance)
- `docs/unity-ai.md` - AI porting strategy: CryEngine Goal Pipes approach vs GOAP, Unity improvements (NavMesh, UniTask coroutines, Burst boids)
- `docs/AI.md` - CryEngine AI internals: CAIHandler, GoalPipe execution, AIMind perception loop, CXPuppetProxy, Boids/flocks, Lua AI API
- `docs/CryGame.md` - CryGame.dll map: 15 subsystems (player, vehicles, weapons, network, UI, script objects, entities)
- `docs/Scripts.md` - Scripts.pak map: ~770 Lua files (AI behaviors, entity scripts, game rules, HUD, menus, sound presets, physics materials)
- `docs/modules.md` - all 20 CryEngine module overviews (Cry3DEngine, CryAnimation, CryPhysics, ResourceCompiler, etc.)
- `docs/third-party.md` - third-party libraries: STLPORT, BinkSDK, PunkBuster, curl