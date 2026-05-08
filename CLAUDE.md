# CLAUDE.md

Guidance for Claude Code when working in this repository.

## Project

`open-farcry` is a Unity 6 LTS port/reimplementation effort for Far Cry 1 assets and runtime behavior. Original Far Cry data is not committed to the project; tools read it from a local game installation.

External paths used by the developer:

- `~/Documents/farcry-game/` - Far Cry 1 installation, expected to contain `FCData/*.pak`, `Levels/*/level.pak`, and `.cry` level files.
- `~/Documents/farcry-sources/` - CryEngine 1 C++ source reference for binary formats and runtime behavior.

Generated/imported project-side cache assets should live under `Assets/FCData/`.

## Unity Setup

- Unity Editor: `6000.4.3f1`
- Render pipeline: URP `17.4.0`
- Main packages: UniTask, Cinemachine 3, Input System, AI Navigation, Timeline, SharpZipLib, Odin Inspector.
- Runtime settings asset: `Assets/Resources/FcFileSystemSettings.asset`
- Render assets: `Assets/Settings/PC_RPAsset.asset` and `Assets/Settings/PC_Renderer.asset`

Open the project in Unity Editor and use Play Mode for runtime checks. Editor tooling is under the `OpenFarCry` Unity menu.

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

This checkout currently behaves as a normal Git worktree. Use `git status`/`git diff` before edits and before commits because Unity can still introduce unrelated asset churn.

Avoid editing generated Unity folders unless explicitly needed:

- `Library/`
- `Temp/`
- `Logs/`
- `UserSettings/`

Prefer source edits in `Assets/Scripts/`, package changes in `Packages/`, and Unity settings changes in `ProjectSettings/` or `Assets/Settings/`.

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
- It mounts all `*.pak` files under `<gameInstallPath>/FCData`.
- Mount order is alphabetical, and lookup is reverse order (last mounted wins), matching patch PAK override behavior.
- Paths are normalized to lower-case forward-slash virtual paths.
- `PakArchive` uses `Unity.SharpZipLib.Zip.ZipFile`.
- `ReadAllBytesAsync` offloads decompression to the thread pool with UniTask, then returns to the main thread.
- In the editor, `[InitializeOnLoad]` initializes the VFS after domain reloads so editor tools can use it outside Play Mode.

When adding file-system features, preserve thread-safety around mount/index state and do not block the Unity main thread for heavy decompression or parsing.

Resource-loading services must support both project-side cached assets under `Assets/FCData/` and original files read on demand from mounted PAK archives through `FcFileSystem`. Prefer cached project assets when they exist and the active file-system/import settings allow project cache usage; fall back to the original PAK-backed virtual path when running in builds or when settings explicitly request live PAK loading. Do not make importer or runtime resource code depend on editor-only cache assets as the only source of truth.

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

Current refactor snapshot (2026-05-08):

- Runtime-first service split is in place:
  - `CgfResourceImportService` for virtual-path source loading.
  - `CgfRuntimeImportRequest` / `CgfRuntimeImportResult` runtime contracts.
  - `CgfRuntimeImportService` + `CgfRuntimeImporter` as runtime import entry/facade.
  - `CgfRuntimeAssetCache` with parsed/model entries, ref-counting, scope release, and trim.
- Runtime-safe builders/services extracted from editor window:
  - `CgfGameObjectBuilder`, `CgfSkeletonBuilder`, `CgfLodImportService`,
    `CgfAnimationRuntimeImportService`, `CgfRagdollBuilder`, `CgfRagdollDiagnostics`.
- Runtime material path is partially split out:
  - `CgfMaterialBuilder`, `CgfMaterialImportService`, `CgfMaterialRuntimeCache`.
  - `CgfParser` now preserves material data needed for runtime material/color/texture-name resolution.
  - Multi-material resolution is keyed by material IDs, not only Unity submesh order.
- Editor-only orchestration split is in place:
  - `CgfImportEditorService`, `CgfImportRequest`, `CgfImportResult`.
  - `CgfAssetCacheService` for mesh/prefab persistence.
  - `CgfAnimationImportEditorService` + `CgfAnimationCacheService` for shared `.anim` cache.
- Source browsing/parsing UI support is extracted to `CgfSourceBrowser`.
- `CgfImporterWindow` is now a thin UI/controller (~531 lines), not the core importer implementation.
- EditMode tests were added for:
  - `CgfSourceBrowser` filtering/selection behavior.
  - `CgfRuntimeAssetCache` retain/release/scope/trim behavior.
- Runtime smoke test script exists:
  - `Assets/Scripts/Importer/Cgf/CgfRuntimeLoadSmokeTest.cs`
  - It can run scene-level import timing, optional animation/LOD/physics setup, and cache scope release/trim checks.
- Brush/runtime collider handling now prefers proxy/no-draw geometry:
  - `FcBrushInstance` can derive collider faces from no-draw/proxy material slots first.
  - Proxy/no-draw submeshes are stripped from the visual mesh so collider geometry is not rendered.
- Level-loading runtime code lives under `Assets/Scripts/Level/`, but the orchestration is still transitional:
  - entity components still contain self-loading behavior in places;
  - the current level runtime path is still mostly synchronous and main-thread-heavy;
  - the planned refactor is documented in `LEVEL_LOADING_REFACTOR_PLAN.md`.

Known runtime performance issue (current state):

- Character animation stage still dominates load time in runtime smoke runs (`CgfAnimationRuntimeImportService`).
- Model cache hits are working; cross-model clip reuse is still not confirmed for all character variants even with current CAF/clip cache logic.
- Do not assume raw CAF file bytes are a stable semantic cache key across equivalent clips; treat animation cache identity/compatibility as an active design problem.
- Keep this as an active optimization/debug area before treating runtime animation performance as closed.

Work status (2026-05-06):

- Added animation import pipeline for character models:
  - `CAL` parsing (`$AnimDir`/`$AnimationDir` directives, dummy `?` entries, fallback to `<model>_*.caf` when needed).
  - `CAF` parsing for controller/timing chunks (`0x0827`, `0x0826` where available).
  - Legacy Unity `Animation` clip generation (`localPosition`/`localRotation`) and attach to imported prefab.
- Fixed duplicate/empty clips in Unity `Animation` component by rebuilding clip list before attach/save.
- Added rig snapshot/reuse pipeline for skinned characters:
  - `CgfRigDefinition` ScriptableObject stores rig fingerprint, animation fingerprint, bone names, parent indices, remap arrays, controller mapping, bind poses.
  - `CgfRigSnapshotBuilder` builds strict rig fingerprint and animation-compatible fingerprint.
  - `CgfRigRegistry` resolves rigs from memory/project cache with exact and animation-compatible matching.
- Added dev/prod rig cache policy:
  - `CgfRigCacheSettings` (`Resources/CgfRigCacheSettings`) supports `ProjectOnly`, `MemoryOnly`, `Hybrid`.
  - Player runtime always resolves to memory-only behavior; editor can combine project + memory.
- Added shared animation cache:
  - Imported clips are saved under `Assets/FCData/AnimationCache/...` and reused across characters/LODs.
  - Cache key is content-based from generated `AnimationClip` curves/bindings (not source filename), so identical clips dedupe to one shared asset.
  - File naming preserves readable alias prefix (`<alias>_<hash>.anim`).
- Added automatic loop detection for imported legacy clips:
  - Uses CAF start/end transform continuity heuristics plus alias hints (`idle/walk/run/...`) and one-shot hints (`jump/reload/death/...`).
  - Applies both `clip.wrapMode` and editor clip setting `loopTime`.
- Added `BoneAnim` parsing (`0x0290`) and controllerID-to-bone-path mapping for clip curve binding.
- Added ragdoll import path from CGF `BONE_PHYSICS_COMP`:
  - `BoneMesh` chunks are parsed and can generate per-bone `BoxCollider`s.
  - Optional ragdoll creation adds `Rigidbody` + `ConfigurableJoint` on physics bones.
  - `FcRagdollController` toggles animated/physics states at runtime (`SetAnimated`/`SetRagdoll`).
- Added LOD discovery/configuration in importer:
  - Sibling `*_lodX` files are discovered and imported as LOD children.
  - `LODGroup` is configured automatically from available levels.
- Fixed ragdoll bone hierarchy correctness:
  - Skeleton hierarchy creation now prefers current-file `BoneAnim` parent links.
  - Bind-pose diagnostics (`[CgfImporter][Diag]`) are available and showed zero bind errors after fix.
- Fixed CGF physics-angle sentinel handling:
  - Extreme values (e.g. `±1e10`) are treated as unconstrained axes, not real limits.
  - Axis mapping now prefers constrained axes for `AngularX`, improving hinge-like joints (knees/elbows).
  - Added joint diagnostics (`[CgfImporter][JointDiag]`) for axis/limit verification.
- Updated skeleton/bind-pose handling:
  - `BoneInitialPos` matrix parsing corrected for translation row in `SBoneInitPosMatrix`.
  - Bindposes now built as inverse of default global pose, with scale removal (`NoScale`-style).
  - Bone local scales forced to `Vector3.one` when reconstructing transforms from bind matrices.
- Fixed major character skinning/animation mismatch:
  - `BoneNameList 0x0744` uses `NAME_ENTITY.name[64]`; reading 32 bytes corrupts bone names.
  - Cry runtime remaps `CryLink.BoneID` into hierarchy/runtime bone indices; Unity import now mirrors this for bone weights, bone names, bind poses, and hierarchy reconstruction.
  - Skinned mesh vertex positions must be reconstructed from `CryLink.offset` in bind pose (`boneDefaultGlobal.TransformPointOLD(offset) * weight`). Some models store raw mesh vertices in an offset space, so using raw `CryVertex.P*` with Unity bindposes makes pivots drift and animated vertices explode.
  - Cry OLD row-vector matrices (`TransformPointOLD`, `SetTranslationOLD`) must be converted into Unity column-vector matrices before basis conversion.
  - Coordinate conversion is now baked into mesh/bindpose/bone/animation data with a proper Z-up to Unity Y-up rotation `(x, y, z) -> (x, z, -y)`.
  - Imported prefab roots should remain ergonomic: position zero, identity rotation, scale one.

Current behavior:

- `CgfParser` validates `FILE_HEADER` and chunk table bounds, computes per-chunk sizes from offsets, and rejects invalid offsets.
- Implemented chunk support includes `Mesh`, `Node`, `BoneNameList` (`0x0744` and `0x0745` variants), and `BoneInitialPos` (`0x0001`).
- Parser handles alignment/padding-sensitive layouts for mesh/node chunks (bool fields before ints/matrices).
- Parsed data keeps `ChunkID` links and multiple mesh chunks (`MeshChunks`, `MeshByChunkID`, `NodeByChunkID`), not only a single mesh.
- Importer selects a primary mesh via `Node.ObjectID -> MeshChunkID` (fallback: first mesh chunk).
- Importer can discover and build sibling LODs via `_lodX` filename suffix and apply a Unity `LODGroup`.
- Mesh build keeps UV V-flip (`1 - v`) and groups faces into Unity submeshes by `MatID` (material count is tied to resulting submesh count).
- Runtime material assignment is now resolved through parsed material chunks and submesh `MatID` mapping, not only by submesh index.
- `Import Skeleton` toggle exists in the importer window:
  - ON: creates `SkinnedMeshRenderer`, applies mesh bone weights/bindposes, and builds a bone hierarchy from node data + bind-pose-derived local transforms.
  - OFF: imports as plain `MeshFilter` + `MeshRenderer` for geometry debugging.
- Additional importer toggles:
  - `Import Physics Box Colliders` (from `BoneMesh`/bone physics data)
  - `Import Ragdoll Bodies/Joints` (requires physics collider import)
- Rig reuse behavior:
  - Exact reuse path requires strict bone/index compatibility.
  - Animation-compatible reuse can share controller-to-bone mapping and clip cache even if raw bone order differs across source files.
  - Hierarchy from cached rig is applied only when structural compatibility checks pass; otherwise hierarchy is rebuilt from current source (`BoneAnim`/`Node`) to avoid pose corruption.
- Coordinate-system/scale conversion is baked into imported data. Do not reintroduce a negative root scale or final root rotation as a shortcut; it makes prefabs hard to use and can hide bind/animation-space mismatches.
- Current coordinate conversion remains sensitive. If changing matrix/transform conversion, validate mesh placement, brush placement, yaw rotation, bind poses, and animation together rather than patching only one stage.
- Caching/saving is deterministic by source virtual path:
  - mesh: `Assets/FCData/<virtual_path_without_ext>.asset`
  - prefab: `Assets/FCData/<virtual_path_without_ext>.prefab`
- Shared animation cache path:
  - clips: `Assets/FCData/AnimationCache/<hash_prefix>/<alias>_<content_hash>.anim`
- On import with saving enabled, existing cached prefab can be reused instead of rebuilding if compatibility checks pass (currently UV0 presence + submesh/material count).
- Cache compatibility also checks generated mesh name/version (`CgfMeshBuilder.MeshCacheVersionName`) so older meshes are rebuilt after importer-space or skinning changes.

Treat the CGF importer as incremental and format-sensitive. When changing binary parsing, cross-check against CryEngine source in `~/Documents/farcry-sources/`, especially ResourceCompiler and CryChunkedFile code.

Detailed importer notes are in `~/Documents/farcry-sources/docs/OpenFarCry_Unity_CGF_CAF_Importer.md`. Read that before changing CGF/CAF transform, bind pose, bone mapping, or animation code.

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

- Mission/entity/brush parsing exists and can assemble a basic scene from Far Cry level data.
- Terrain, vegetation, and more complex environment systems are still outside the implemented level path.
- `brush.lst` parsing and brush scene assembly are in place, including material-table parsing needed for proxy/no-draw handling.
- Level runtime loading is not fully service-driven yet:
  - some entity types still trigger resource loading from their own lifecycle methods;
  - the main runtime path is still mostly synchronous;
  - timing/instrumentation for real level loads is still incomplete.
- `FcFileSystem.ReadAllBytesAsync(...)` exists and should be preferred for future level-load refactors, but the current level pipeline does not yet use async systematically.

When changing level loading, prefer moving logic toward centralized services and explicit load requests rather than expanding self-loading MonoBehaviour code.

Known limitations (current state):

- `.cga` is currently treated as geometry preview; controller/timing/`*.anm` animation pipeline is not implemented.
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
- Use UniTask for async Unity-facing work already following project patterns.
- Prefer explicit binary parsing with `BinaryReader`; document offsets and chunk assumptions where the format is ambiguous.
- Preserve path normalization semantics in VFS code: lower-case, forward slashes, trimmed leading/trailing slashes.
- Do not commit or generate original Far Cry copyrighted data into the repository.
- Avoid broad asset churn from Unity serialization unless the task requires changing those assets.

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
