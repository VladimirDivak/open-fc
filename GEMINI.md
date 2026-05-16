# OpenFarCry - Unity 6 Far Cry 1 Reimplementation

A Unity 6 LTS port and runtime reimplementation for Far Cry 1 assets and game behavior. This project imports original game data (PAK, CGF, CGA, XML) from a local Far Cry 1 installation and renders it using Unity's Universal Render Pipeline (URP).

## Project Overview

- **Core Goal:** Recreate the Far Cry 1 experience in Unity 6, focusing on high-fidelity asset importing and accurate runtime behavior.
- **Target Platform:** PC (Unity 6000.4.3f1).
- **Architecture:** 
    - **Runtime-first Import:** Assets are parsed and built into Unity objects at runtime or editor-time through a specialized VFS and importer layer.
    - **Asynchronous Orchestration:** Extensive use of `UniTask` for non-blocking I/O, parsing, and mesh building.
    - **Data-Driven:** Level construction is driven by original Far Cry mission XMLs and brush lists.

## Tech Stack

- **Engine:** Unity 6 LTS (`6000.4.3f1`)
- **Render Pipeline:** Universal Render Pipeline (URP) `17.4.0`
- **Key Packages:**
    - **UniTask:** Main asynchronous programming framework.
    - **SharpZipLib:** Used for reading and decompressing Far Cry `.pak` files.
    - **Odin Inspector:** Enhanced editor tooling and serialization.
    - **Cinemachine 3:** Camera systems.
    - **Input System:** Modern Unity input handling.
    - **AI Navigation:** For pathfinding and agent movement.

## Core Systems

### File System (VFS)
- **Assembly:** `OpenFarCry.FileSystem`
- **Logic:** Mounts `*.pak` files from the game installation. Paths are normalized to lower-case, forward-slashes (e.g., `scripts/common.lua`).
- **Key Files:** `FcFileSystem.cs`, `PakArchive.cs`.

### CGF Importer
- **Assembly:** `OpenFarCry.Importer`
- **Logic:** Parses CryEngine `.cgf`/`.cga` binary files. Supports:
    - **Static Meshes:** Merges node hierarchies into single Unity meshes.
    - **Skinned Meshes:** Reconstructs bind poses and bone weights.
    - **LODs:** Automatically discovers and sets up `LODGroup` components using sibling files (e.g., `_lod1.cgf`).
    - **Materials:** Resolves texture paths (diffuse, normal, specular, opacity) from CGF chunks.
- **Cache:** Implements a ref-counted two-slot cache (parsed data and built Unity models).

### Level Loading & Scene Building
- **Assembly:** `OpenFarCry.Level`
- **Logic:** Parses mission XML, `brush.lst`, and entity data.
- **Workflow:** 
    1. Parse mission data.
    2. Build preload plans for geometry and textures.
    3. Distance-based asynchronous loading of brushes and vegetation.
    4. Coordinate conversion from CryEngine space to Unity scene space.

### Coordinate Systems
- **Importer Space (CGF Assets):** Cry `(x, y, z)` → Unity `(x, z, -y)`.
- **Scene Space (Levels):** Cry `(x, y, z)` → Unity `(x, z, y)`.
- **Note:** Do not use global scale shortcuts. Conversions must be explicit in importer and level builder paths.

## Building and Running

### Prerequisites
- **Unity Hub & Unity 6000.4.3f1.**
- **Local Far Cry 1 Installation:** Specify the path in `Assets/Resources/FcFileSystemSettings.asset`.
- **CryEngine 1 Source (Optional):** Reference C++ source is expected at `~/Documents/farcry-sources/` for development.

### Key Commands
- **Open Project:** Open the root directory in Unity Hub.
- **Run Level:** Open `Assets/Scenes/SampleScene.unity` or a generated level scene in `Assets/Scenes/Levels/` and enter Play Mode.
- **Tests:** Use the Unity Test Framework window (Window > General > Test Framework) to run EditMode tests.
- **CLI Tests:**
  ```bash
  Unity -batchmode -projectPath . -runTests -testPlatform EditMode -quit
  ```

## Development Conventions

- **Namespaces:** Match assembly names (e.g., `OpenFarCry.FileSystem`).
- **Editor/Runtime Split:** Keep editor-only code in `Editor/` subfolders with separate asmdefs.
- **Async:** Always prefer `UniTask` over standard Tasks or Coroutines for Unity-integrated async work.
- **VFS Paths:** Use lower-case, forward-slash normalization. Trim leading/trailing slashes.
- **Binary Parsing:** Use `BinaryReader`. Explicitly document offsets and chunk types in comments.
- **Coordinate Basis:** Be extremely careful with basis conversions. Validate static brushes, entity rotations, and skinned bind poses together when changing transform logic.
- **Git:** Avoid committing original game data. Unity scene files in `Assets/Scenes/Levels/` are often transient/generated.

## Documentation Pointers

- `CLAUDE.md`: High-level guidance for agents and workspace setup.
- `AGENTS.md`: Repository-specific guidelines and style rules.
- `docs/cgf-import-pipeline.md`: Detailed breakdown of the CGF import logic.
- `tasks/rafactoring/`: Active architectural refactoring plans.
- `~/Documents/farcry-sources/docs/asset-formats.md`: Primary reference for binary formats.
