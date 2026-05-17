# Scripts: FileSystem (VFS)

VFS assembly: `OpenFarCry.FileSystem`. Mounts game data.

### Core Logic

- **FcFileSystem.cs**: Main entry. Mounts all `*.pak` from `FCData`. LIFO priority (last mounted wins). `ReadAllBytesAsync` uses ThreadPool.
- **PakArchive.cs**: One PAK file. Uses SharpZipLib. Normalizes paths (lowercase, forward-slash). Lazily builds index.
- **FcFileSystemSettings.cs**: SO asset. Stores game install path. Located in `Resources/`.
- **FcFileSystemEditorInit.cs**: Editor-only. Initializes VFS on load/recompile. Tools work in Edit Mode.

### Rules

- Use `FcFileSystem.ReadAllBytesAsync` for Unity work.
- Use `FcFileSystem.Mount` for level-specific PAKs.
- Paths: no leading slash, lowercase.
