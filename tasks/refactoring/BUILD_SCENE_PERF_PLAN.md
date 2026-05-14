# Build Scene Performance Plan

> **Diagnostic first**: Check `RecordPhase` timings in Console log before implementing.
> Phase with max ms = implement first, regardless of order below.

---

## Confirmed Bottlenecks

| # | Location | Problem |
|---|----------|---------|
| 1 | `PakArchive.GetEntriesInDirectory` line 63 | Linear scan all `_index.Keys` via `StartsWith` per call; 50K–200K keys per PAK |
| 2 | `FcLevelSupplementLoader.Load` | `leveldata.xml` decompressed + decoded + parsed 3× separately |
| 3 | `FcLevelSceneBuilder.BuildVegetationInstances` | `FindSiblingLodPaths` called per unique veg type → `GetEntries` per type; 60 types across 15 dirs = 60 full PAK scans |
| 4 | `FcLevelSupplementLoader` multi-call | `CollectEntries`, `CollectByPrefix` ×3, `CollectMusicXmlFiles` each call `GetEntries` independently |
| 5 | `FcLevelSceneBuilder.SaveLayoutData` | `AssetDatabase.SaveAssets` mid-build flush while 50K veg instances pending |

---

## Phase Board

- [x] Phase 1 — PakArchive directory index
- [x] Phase 2 — `leveldata.xml` single load
- [x] Phase 3 — LOD discovery batch by directory
- [x] Phase 4 — `FcLevelSupplementLoader` single `GetEntries`
- [x] Phase 5 — `AssetDatabase` editing wrapper

---

## Phase 1 — PakArchive Directory Index

**Goal**: `GetEntriesInDirectory` O(N) → O(1).

**Files**: `Assets/Scripts/FileSystem/PakArchive.cs`

**Tasks**:
- [ ] Add field `Dictionary<string, List<string>> _directoryIndex` to `PakArchive`.
- [ ] Populate during existing `_index` build loop: for each key extract parent dir via `key.LastIndexOf('/')`, add to `_directoryIndex[dir]`.
- [ ] Replace body of `GetEntriesInDirectory` (line 63) with `_directoryIndex.TryGetValue(dir, out var list) ? list : Array.Empty<string>()`.
- [ ] Handle root dir edge case (keys with no `/`).

**Success criteria**: `GetEntries("objects/speedtree")` on 100K-key PAK takes <0.1 ms; no change in returned paths.

---

## Phase 2 — `leveldata.xml` Single Load

**Goal**: Decompress + parse `leveldata.xml` once per `Load()`.

**Files**: `Assets/Scripts/Level/Data/FcLevelSupplementLoader.cs`

**Tasks**:
- [ ] In `Load()`, call `LoadXmlIfExists("leveldata.xml")` once, store result as local `XmlDocument leveldataXml`.
- [ ] Add `XmlDocument` parameter to `ParseSurfaceTypes`, `ParseMaterialLibraries`, `ParseVegetationTypes` (or pass via existing struct/context object).
- [ ] Replace internal `LoadXmlIfExists("leveldata.xml")` calls inside those 3 methods with the passed document.
- [ ] If `leveldataXml == null`, early-return all three parse methods unchanged.

**Success criteria**: PAK decompression + `XmlDocument.LoadXml` for `leveldata.xml` happens exactly once per build; Console log shows 1 read event not 3.

---

## Phase 3 — LOD Discovery Batch by Directory

**Goal**: `GetEntries` called once per unique directory, not once per veg type.

**Files**: `Assets/Scripts/Level/Editor/FcLevelSceneBuilder.cs`, `Assets/Scripts/Importer/Cgf/CgfLodImportService.cs`

**Tasks**:
- [ ] Before veg-type loop in `BuildVegetationInstances`, collect unique dirs from all veg type CGF paths:
  ```csharp
  var dirCache = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);
  foreach (var vegType in uniqueVegTypes)
  {
      var dir = VfsPath.GetDirectory(vegType.CgfPath);
      if (!dirCache.ContainsKey(dir))
          dirCache[dir] = _fileSystem.GetEntries(dir);
  }
  ```
- [ ] Add overload or internal helper to `CgfLodImportService.FindSiblingLodPaths` accepting pre-fetched `IReadOnlyList<string> dirEntries` instead of calling `GetEntries` internally.
- [ ] Pass matching `dirCache[dir]` entry into helper per veg type.
- [ ] Keep original `FindSiblingLodPaths(string path)` public signature intact (delegates to new overload).

**Success criteria**: 60 veg types across 15 dirs → 15 `GetEntries` calls instead of 60; build log entry count drops proportionally.

---

## Phase 4 — `FcLevelSupplementLoader` Single `GetEntries`

**Goal**: All `CollectByPrefix` / `CollectEntries` / `CollectMusicXmlFiles` share one directory listing.

**Files**: `Assets/Scripts/Level/Data/FcLevelSupplementLoader.cs`

**Tasks**:
- [ ] In `Load()` (or whichever method dispatches collection), call `_fileSystem.GetEntries(basePath)` once.
- [ ] Store result as `IReadOnlyList<string> baseEntries`.
- [ ] Refactor `CollectEntries`, each `CollectByPrefix` call, and `CollectMusicXmlFiles` to accept `baseEntries` as parameter and filter in-memory instead of calling `GetEntries` independently.
- [ ] Confirm `basePath` is consistent across all 5 callers; if multiple base paths exist, build one list per unique base.

**Success criteria**: 5 `GetEntries` calls → 1 (or 1 per unique base dir); no change in collected file sets.

---

## Phase 5 — `AssetDatabase` Editing Wrapper

**Goal**: Prevent mid-build asset DB flush during `SaveLayoutData`.

**Files**: `Assets/Scripts/Level/Editor/FcLevelSceneBuilder.cs`

**Tasks**:
- [ ] At top of `BuildScene` (or outermost editor entry point), call `AssetDatabase.StartAssetEditing()`.
- [ ] Wrap full build body in `try/finally`; call `AssetDatabase.StopAssetEditing()` in `finally`.
- [ ] Verify `SaveLayoutData` no longer triggers a visible import cycle mid-build.
- [ ] Confirm `AssetDatabase.SaveAssets()` inside `SaveLayoutData` is still needed or can be removed in favour of the outer `StopAssetEditing` flush.

**Success criteria**: Import progress bar appears once (after `StopAssetEditing`), not mid-build; build wall-clock time reduced by import overhead.

---

## Non-Goals

- Runtime path changes (coordinate systems, VFS normalization).
- Changing PAK file layout or index format on disk.
- Merging editor and runtime assemblies.
