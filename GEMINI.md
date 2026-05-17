# OpenFarCry - Unity 6 Far Cry 1 Reimplementation

A Unity 6 LTS port and runtime reimplementation for Far Cry 1 assets and game behavior. This project imports original game data (PAK, CGF, CGA, XML) from a local Far Cry 1 installation and renders it using Unity's Universal Render Pipeline (URP).

## Project Overview

- **Goal:** Recreate the Far Cry 1 experience in Unity 6, focusing on high-fidelity asset importing and accurate runtime behavior.
- **Target Platform:** PC (Unity 6000.4.3f1).
- **Architecture:** 
    - **Runtime-first Import:** Assets are parsed and built into Unity objects at runtime or editor-time through a specialized VFS and importer layer.
    - **Asynchronous Orchestration:** Extensive use of `UniTask` for non-blocking I/O, parsing, and mesh building.
    - **Data-Driven:** Level construction is driven by original Far Cry mission XMLs, brush lists, and supplement data.

## Tech Stack

- **Engine:** Unity 6 LTS (`6000.4.3f1`)
- **Render Pipeline:** Universal Render Pipeline (URP) `17.4.0`
- **Key Packages:**
    - **UniTask:** Main asynchronous programming framework.
    - **SharpZipLib:** Used for reading and decompressing Far Cry `.pak` and `.cry` files.
    - **Odin Inspector:** Enhanced editor tooling and serialization.
    - **Cinemachine 3:** Camera systems.
    - **Input System:** Modern Unity input handling.
    - **AI Navigation:** For pathfinding and agent movement.

## Implemented Architecture

### Virtual File System (VFS)
- **Assembly:** `OpenFarCry.FileSystem`
- **Logic:** Mounts `*.pak` files from the game installation. Paths are normalized to lower-case, forward-slashes. Lookup uses reverse mount order (patch override behavior). Supports `.cry` (ZIP) files via `PakArchive`.

### CGF Importer
- **Assembly:** `OpenFarCry.Importer`
- **Static Meshes:** Combines all static `NodeChunks` into single Unity meshes. Static transforms use OLD Matrix44 row-vector row-translation.
- **Skinned Meshes:** Bind pose reconstruction from `CryLink.offset` and bone weights.
- **LODs:** Automatic discovery and setup of `LODGroup` using sibling files (e.g., `_lod1.cgf`).
- **Physics:** Bone physics/ragdoll import from `BoneMesh`/`BONE_PHYSICS_COMP`.
- **Lightmaps:** UV2 generation using `Unwrapping.GenerateSecondaryUVSet`. Cache version: `v10_lmuv`.

### Texture & Material Pipeline
- **Texture Runtime:** Decodes DDS (DXT1/3/5), TGA, BMP, JPG. Supports normal/spec/opacity/gloss slots with linear/sRGB split.
- **Material Builder:** Classifier-driven mapping of Cry shader families (`modelcommon`, `bumpdiffuse`, `plants`, `glass`, etc.) to URP shaders.
- **Level Materials:** Resolves `materials.xml` overrides for brushes and entities with strict precedence (level override -> embedded fallback).

### Level Loading & Scene Building
- **Assembly:** `OpenFarCry.Level`
- **Loader:** Parses mission XML, `brush.lst`, `objects.lst`, and supplement data.
- **Runtime Spawning:** Asynchronous spawning of brushes (`SpawnBrushesFromList`) and vegetation (`SpawnVegetationFromSupplement`) with distance-based preloading.
- **Volumes:** `VisArea`, `Portal`, `OccluderArea`, `FogVolume`, and `WaterVolume` implemented as MonoBehaviours with custom gizmos.
- **Entities:** `FcEntityStub` (generic property storage), `FcMeshEntity` (static models), `FcCharacterEntity` (skinned).
- **Environment:** `DynamicLight` mapped to Unity `Light` (Mixed mode support); `SoundSpot` mapped to `AudioSource`. Movie sequences represented as placeholders.

## Coordinate Systems

- **Importer Space (CGF Assets):** Cry `(x, y, z)` → Unity `(x, z, -y)`.
- **Scene Space (Levels):** Cry `(x, y, z)` → Unity `(x, z, y)`.
- **Note:** Basis conversions are explicit in importer and level builder paths. Do not use negative root scale shortcuts.

## Development Workflow

### Prerequisites
- **Local Far Cry 1 Installation:** Expected at `~/Documents/farcry-game/`. Specify in `Assets/Resources/FcFileSystemSettings.asset`.
- **CryEngine 1 Source:** Reference C++ source at `~/Documents/farcry-sources/`.

### Conventions
- **Namespaces:** Match assembly names (e.g., `OpenFarCry.FileSystem`).
- **Editor/Runtime Split:** Keep editor-only code in `Editor/` folders with separate asmdefs.
- **Async:** Always prefer `UniTask`.
- **VFS Paths:** Lower-case, forward-slash normalization. Trim leading/trailing slashes.

### Key Commands
- **Run Level:** Open `Assets/Scenes/SampleScene.unity` or a level in `Assets/Scenes/Levels/`.
- **Tests:** Window > General > Test Framework. EditMode tests under `Assets/Scripts/Importer/Tests/Editor/`.
- **CLI Tests:**
  ```bash
  Unity -batchmode -projectPath . -runTests -testPlatform EditMode -quit
  ```
- **Audit:** Use "Source Parity Audit" in `FcLevelBuilderWindow` for SRC/SCENE/DELTA reporting.

## Documentation Index

- `CLAUDE.md`: Implementation status and response style.
- `AGENTS.md`: Repository guidelines and coding standards.
- `docs/cgf-import-pipeline.md`: Technical breakdown of CGF logic.
- `tasks/refactoring/`: Active architectural plans (e.g., `LEVEL_CONTENT_IMPORT_REFACTOR_PLAN.md`, `MATERIAL_PIPELINE_REFACTOR_PLAN.md`).
