# CLAUDE.md

Guidance for Claude Code and other agents working in this repository.

## Project

`open-farcry` is a Unity 6 LTS port/reimplementation for Far Cry 1 assets and runtime behavior. Original Far Cry data is not committed; tools read it from a local game install.

External paths:

- `~/Documents/farcry-game/` - Far Cry 1 install, expected to contain `FCData/*.pak`, `Levels/*/level.pak`, and `.cry` level files.
- `~/Documents/farcry-sources/` - CryEngine 1 C++ source reference for binary formats and runtime behavior.
- `~/Documents/farcry-sources/docs/asset-formats.md` - primary local notes for CGF/CGA/CAL/CAF formats and transform rules.

Generated/imported cache assets live under `Assets/FCData/`.

## Unity Setup

- Unity Editor: `6000.4.3f1`
- Render pipeline: URP `17.4.0`
- Main packages: UniTask, Cinemachine 3, Input System, AI Navigation, Timeline, SharpZipLib, Odin Inspector.
- Runtime settings asset: `Assets/Resources/FcFileSystemSettings.asset`
- Render assets: `Assets/Settings/PC_RPAsset.asset` and `Assets/Settings/PC_Renderer.asset`

Open project in Unity Editor and use Play Mode for runtime checks. Editor tooling is under the `OpenFarCry` Unity menu.

Headless Unity commands are available but may fail in this local environment if Unity licensing or another Editor instance blocks batch mode.

## Git/Workspace Notes

Run `git status` before edits. Unity scene files under `Assets/Scenes/Levels/` are often generated/deleted during testing and may be dirty for reasons unrelated to source changes.

Avoid editing generated Unity folders unless explicitly needed:

- `Library/`
- `Temp/`
- `Logs/`
- `UserSettings/`

Do not commit original Far Cry copyrighted data.

## Implemented Runtime Layers

### File System

Assembly: `OpenFarCry.FileSystem`

Key files:

- `Assets/Scripts/FileSystem/FcFileSystem.cs`
- `Assets/Scripts/FileSystem/PakArchive.cs`
- `Assets/Scripts/FileSystem/FcFileSystemSettings.cs`
- `Assets/Scripts/FileSystem/Editor/FcFileSystemEditorInit.cs`

Current behavior:

- Mounts all `*.pak` files under `<gameInstallPath>/FCData`.
- Paths normalize to lower-case forward-slash virtual paths.
- Lookup uses reverse mount order, matching patch PAK override behavior.
- `ReadAllBytesAsync` offloads decompression to a thread pool with UniTask.
- Editor initialization allows tools to use VFS outside Play Mode.

### CGF Importer

Assembly: `OpenFarCry.Importer`

Key files:

- `Assets/Scripts/Importer/Cgf/CgfData.cs`
- `Assets/Scripts/Importer/Cgf/CgfParser.cs`
- `Assets/Scripts/Importer/Cgf/CgfMeshBuilder.cs`
- `Assets/Scripts/Importer/Cgf/CgfRuntimeImportService.cs`
- `Assets/Scripts/Importer/Cgf/CgfGameObjectBuilder.cs`
- `Assets/Scripts/Importer/Cgf/CgfMaterialImportService.cs`
- `Assets/Scripts/Importer/Cgf/CgfLodImportService.cs`
- `Assets/Scripts/Importer/Cgf/CgfAnimationRuntimeImportService.cs`
- `Assets/Scripts/Importer/Cgf/CgfRagdollBuilder.cs`

Current behavior:

- Runtime-first import path reads CGF/CGA bytes from VFS through `CgfResourceImportService`.
- Runtime cache stores parsed/model artifacts with ref-counting and level-scope release.
- Runtime model cache key includes `CgfMeshBuilder.MeshCacheVersionName`; bump this version after mesh/basis changes.
- Parser keeps `ChunkID` links, multiple mesh chunks, node chunks, material chunks, bone init poses, and bone mesh chunks.
- Static CGF import combines all static `NodeChunks` that reference mesh chunks, matching Cry static object behavior more closely than selecting only the first mesh.
- Static mesh node transforms are accumulated through parent nodes in Cry order: `node.tm * parent.tm * ...`.
- Static node matrices are OLD row-vector `Matrix44`; translation comes from row 3 (`m30/m31/m32`).
- Matrix43 bind matrices remain a separate path; do not reuse node Matrix44 conversion for bind poses.
- Skinned meshes keep the existing skeleton/bind-pose path and reconstruct vertices from `CryLink.offset` in bind pose.
- Material assignment resolves through parsed material chunks and submesh `MatID`, not only Unity submesh order.
- Runtime LOD discovery uses sibling `*_lodX` CGF files.
- Ragdoll/bone physics import exists for `BoneMesh`/`BONE_PHYSICS_COMP` data.

### Texture Runtime

Key files:

- `Assets/Scripts/Importer/Texture/TextureRuntimeImportService.cs`
- `Assets/Scripts/Importer/Texture/TextureRuntimeCache.cs`
- `Assets/Scripts/Importer/Texture/DdsRuntimeDecoder.cs`
- `Assets/Scripts/Importer/Cgf/CgfMaterialImportService.cs`
- `Assets/Scripts/Importer/Cgf/CgfMaterialBuilder.cs`

Current behavior:

- Runtime texture service loads texture bytes from VFS and decodes supported DDS/TGA/BMP/JPG paths.
- Runtime materials can bind diffuse, normal, specular, and opacity maps when parsed material data exposes them.
- Texture cache still needs more robust lifecycle/eviction work; see `TEXTURE_IMPORT_RUNTIME_NEXT_PLAN.md`.

## Coordinate And Transform Rules

This project intentionally has two Unity spaces for Far Cry data:

- CGF asset/importer space: Cry `(x,y,z)` -> Unity `(x,z,-y)`.
- Level scene space: Cry `(x,y,z)` -> Unity `(x,z,y)`.

Static CGF vertices are baked in importer space. Level instances then bridge from importer space to scene space.

Do not replace this with a single global `-Y` or negative root scale shortcut. Brush placement, entity placement, bind poses, animation, and collider generation depend on these paths staying explicit.

Important conversion points:

- `CryTransformConversion.PositionInImporterSpace` handles CGF asset vertices.
- `CryTransformConversion.NodeMatrixInImporterSpace` handles CGF OLD Matrix44 node transforms, including row translation.
- `FcLevelLoader.ConvertPosition` and `ConvertDirection` handle mission/level coordinates as scene space `(x,z,y)`.
- `FcLevelSceneBuilder.ApplyCryMatrix34` converts brush `Matrix34` as `SceneBasis * CryMatrix * Inverse(AssetBasis)`.
- `FcLevelSceneBuilder.ApplyCryRotationXYZ` converts entity/object Cry `CreateRotationXYZ(angles)` through the same basis bridge.
- Brush instances currently use negative `Z` scale at the instance transform to express handedness; brush materials disable backface culling to avoid inside-out visuals.
- `FcBrushInstance` strips proxy/no-draw material submeshes from visual meshes using direct `MatID` matching only. The 1-based material fallback is reserved for collider extraction.

When changing transforms, validate all of these together:

- static brush position/rotation/scale;
- brush MeshCollider alignment;
- CGF pivot/selection position;
- multi-node static CGF geometry;
- entity/object rotations;
- skinned bind poses and animation.

## Level Loading

Assembly: `OpenFarCry.Level`

Key files:

- `Assets/Scripts/Level/Data/FcLevelLoader.cs`
- `Assets/Scripts/Level/Data/FcBrushLoader.cs`
- `Assets/Scripts/Level/Editor/FcLevelSceneBuilder.cs`
- `Assets/Scripts/Level/Services/FcLevelEnvironment.cs`
- `Assets/Scripts/Level/Services/FcLevelResourceService.cs`
- `Assets/Scripts/Level/Services/FcBrushLoadService.cs`
- `Assets/Scripts/Level/Services/FcLevelCacheService.cs`
- `Assets/Scripts/Level/Entities/FcBrushInstance.cs`
- `Assets/Scripts/Level/Entities/FcMeshEntity.cs`
- `Assets/Scripts/Level/Entities/FcCharacterEntity.cs`

Current behavior:

- Editor level builder parses mission XML and `brush.lst`, then creates Unity scene placeholders.
- Brushes load through `FcBrushLoadService`, async, distance-sorted and concurrency-limited.
- Brush visuals and colliders are built from runtime CGF data; proxy/no-draw faces prefer collider usage and are stripped from visuals.
- `FcLevelEnvironment.Apply()` is called at editor build time: DirectionalLight (sun) + `RenderSettings` (fog, ambient) placed immediately, not deferred to `Awake`.
- `DynamicLight` entities are built as Unity `Light` (Point or Spot) at editor build; color from `clrDiffuse`, intensity from `DiffuseMultiplier`, range from `OuterRadius`, spot angle from `ProjectorFov`; `bActive=0` disables GO.
- `SoundSpot` entities are built as Unity `AudioSource` (3D, linear rolloff) at editor build; min/max distance from `InnerRadius`/`OuterRadius`, volume from `iVolume` (0–255), `bLoop` sets loop.
- Neither `DynamicLight` nor `SoundSpot` require prefab entries in `FcEntityPrefabRegistry`.
- `SunVector` in mission XML = light travel direction (FROM sun TO scene); stored unconverted in `FcLevelEnvironmentDesc.SunVector`/`FcLevelEnvironment.SunDirection`; applied as `Quaternion.LookRotation(SunDirection)` (no negation). CryEngine itself negates SunVector to derive sun position (`3dEngineLoad.cpp:735`).
- Entity/object placeholders still rely on entity components and `FcLevelResourceService`; further centralization is planned.
- Terrain, vegetation, complex environment systems, full audio pipeline, and gameplay scripting are not complete.

Next level-loading architecture work is documented in `LEVEL_LOADING_REFACTOR_PLAN.md`.

### Water Rendering

Assembly: `OpenFarCry.Rendering.Water` (+ `.Editor`)

Key files:

- `Assets/Scripts/Rendering/Water/FcWaterSettings.cs`
- `Assets/Scripts/Rendering/Water/FcWaterQualityTier.cs`
- `Assets/Scripts/Rendering/Water/FcWaterSurface.cs`
- `Assets/Scripts/Rendering/Water/FcPlanarReflectionRendererFeature.cs`
- `Assets/Scripts/Rendering/Water/FcWaterRuntimeBootstrap.cs`
- `Assets/Shaders/Water/FarCryWater.shader`
- `Assets/Resources/FcWaterSettings.asset` (auto-created on first level build)

Current behavior:

- Ocean spawns from `FcLevelSceneBuilder.BuildWaterPlane` (editor build time) on `Water` Unity layer (auto-installed via `FcWaterLayerInstaller`), with `FcWaterSurface` MonoBehaviour and shared `FarCryWater.mat`.
- `FcPlanarReflectionRendererFeature` subscribes to `RenderPipelineManager.beginCameraRendering`, renders a mirrored camera (oblique near plane = water surface, `GL.invertCulling`) into `_FcWaterReflectionTex` via `RenderPipeline.SubmitRenderRequest(UniversalRenderPipeline.SingleCameraRequest)`.
- Quality tiers (Off/Low/Medium/High/Ultra) gate planar/refraction/fallback. Auto-detect from `SystemInfo.graphicsMemorySize` and `Application.isMobilePlatform`. Manual override in `FcWaterSettings`.
- Shader keywords pushed at startup by `FcWaterRuntimeBootstrap`: `_FC_WATER_PLANAR_ON`, `_FC_WATER_REFRACTION_ON`, `_FC_WATER_FALLBACK_PROBE_ON`.
- Refraction relies on URP `_CameraOpaqueTexture` (already enabled in `PC_RPAsset.asset`).
- Per-source-camera reflection pool (`FcReflectionCache`): each Game camera gets its own mirror cam + RT (no shared global state); frustum-culled against `FcWaterSurface.WorldBounds`; temporal reuse skips re-render when source view/projection is unchanged (`FcWaterSettings.TemporalReuse`); reflection cam renders with reduced `QualitySettings.lodBias`/`maximumLODLevel` and optional shadow-distance cut.
- **Stencil bit 16 is reserved** for the water surface mask. `FarCryWater.shader` ForwardLit pass writes `Stencil { Ref 16 Comp Always Pass Replace }`; do not reuse this stencil ref for other systems.
- Visual look is material-driven (`FarCryWater.mat`), NOT synced from `FcWaterSettings`: Gerstner vertex waves (`_FC_WATER_GERSTNER_ON`, enabled on reflection-texture tiers), Beer-Lambert depth absorption (`_ExtinctionRGB`), GGX sun glint (`_Roughness`/`_SunGlintStrength`), shoreline+crest foam (`_Foam*`). Only `FcWaterSettings.WaveGridResolution` (ocean mesh subdivision, default 128, 16-bit index cap 254) is C#-side, consumed by `FcLevelSceneBuilder.BuildWaterPlane` which now builds a tessellated grid mesh instead of a primitive Plane.
- Two reflection paths share `_FcWaterReflectionTex` (shader samples it via `_FC_WATER_PLANAR_ON`): mirror-camera planar (`FcPlanarReflectionRendererFeature`, tiers High/Ultra) and screen-space SSPR (`FcSsprRendererFeature` + `FcSsprCompute.compute`, tier `Sspr`). Tier helpers: `UsesPlanar` (mirror cam), `UsesSspr`, `UsesReflectionTexture` (either → drives sampling + Gerstner keywords). SSPR needs the feature added to `PC_Renderer.asset` with the compute shader assigned; it has no-roll/offscreen limits and is the low-end/mobile alternative.

**Manual setup required after first compile:** add `FcPlanarReflectionRendererFeature` to `Assets/Settings/PC_Renderer.asset` via the Renderer asset inspector (Add Renderer Feature). Open `OpenFarCry → Rendering → Water Settings` to edit knobs.

## Tests And Verification

EditMode tests exist under `Assets/Scripts/Importer/Tests/Editor/`.

Unity batch tests are the preferred verification path, but in this workstation CLI batch mode may be blocked by licensing or another running Editor. `dotnet build` on Unity-generated `.csproj` is not a reliable substitute unless Unity has generated/restored `Temp/obj/.../project.assets.json`.

For transform work, visual Unity checks are currently required.

## Response Style

- Respond in English only.
- Be maximally short, concise, and to the point. No filler, no pleasantries.
- Process commentary and reasoning: one sentence max per step. State results, not narration.

## Coding Conventions

- Use namespaces matching assemblies, for example `OpenFarCry.FileSystem`, `OpenFarCry.Importer.Cgf`, `OpenFarCry.Level`.
- Keep runtime assemblies separate from editor-only code via `Editor/` folders and editor asmdefs.
- Use UniTask for async Unity-facing work following project patterns.
- Prefer explicit binary parsing with `BinaryReader`; document offsets and chunk assumptions where format is ambiguous.
- Preserve VFS path normalization: lower-case, forward slashes, trimmed leading/trailing slashes.
- Avoid broad Unity serialization churn unless the task requires changing assets.

## Useful References

CryEngine source reference path:

- `~/Documents/farcry-sources/CryCommon/`
- `~/Documents/farcry-sources/Cry3DEngine/`
- `~/Documents/farcry-sources/CryEntitySystem/`
- `~/Documents/farcry-sources/ResourceCompilerPC/`

Source documentation in `~/Documents/farcry-sources/docs/`:

- `docs/README.md` - map of all docs.
- `docs/asset-formats.md` - CGF/CGA/CAL/CAF binary formats and Unity import rules.
- `docs/unity-architecture.md` - C# vs Lua split strategy.
- `docs/unity-ai.md` and `docs/AI.md` - AI porting strategy and CryEngine AI internals.
- `docs/CryGame.md`, `docs/Scripts.md`, `docs/modules.md` - source/module maps.
