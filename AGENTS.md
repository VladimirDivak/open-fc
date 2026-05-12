# Repository Guidelines

## Structure

Unity 6 LTS Far Cry 1 data layer. Code: `Assets/Scripts/`.

- `FileSystem/`: mount/read Far Cry `*.pak`.
- `Importer/`: runtime CGF/CGA/CAL/CAF + texture import.
- `Importer/Editor/`, `Level/Editor/`: Editor-only.
- `Importer/Tests/Editor/`: EditMode tests.
- `Level/`: mission/brush parsing, services, entities.

Scenes: `Assets/Scenes/`. Generated/import cache: `Assets/FCData/`. No original game data in git.

## Commands

Unity Editor `6000.4.3f1`. Use Play Mode for runtime load/transform checks.

```sh
git status --short --branch
rg "SearchTerm" Assets/Scripts
Unity -batchmode -projectPath . -runTests -testPlatform EditMode -quit
```

Prefer Unity EditMode tests. `dotnet build` unreliable unless Unity generated/restored project files.

## Style

C#, four-space indent, Unity conventions. Runtime namespaces match asmdefs: `OpenFarCry.FileSystem`, `OpenFarCry.Importer.Cgf`, `OpenFarCry.Level`.

Keep runtime/editor split via `Editor/` + asmdefs. Use UniTask for Unity async. Prefer `BinaryReader`; document unclear offsets/format guesses briefly.

VFS paths: lower-case, forward slashes, no leading/trailing slash.

## Tests

Unity Test Framework. Tests: `Assets/Scripts/Importer/Tests/Editor/`. Name by behavior, e.g. `CgfMeshBuilderTests`.

Transform/level/animation/collider/material changes need focused EditMode tests + visual Unity check. Coordinate conversion needs joint validation: brushes, entity rotations, CGF pivots, colliders, bind poses, animation.

## Commits / PRs

Commit subject: short imperative, e.g. `Fix brush instance matrix basis conversion`.

PR: behavior changed, tests/Unity validation, screenshots/error captures for visual fixes, generated scene/asset churn esp. `Assets/Scenes/Levels/`.

Avoid unrelated Unity serialization churn.

## Config / Data

Data paths in `CLAUDE.md`: `~/Documents/farcry-game/`, `~/Documents/farcry-sources/`. Runtime settings: `Assets/Resources/FcFileSystemSettings.asset`.

Do not edit `Library/`, `Temp/`, `Logs/`, `UserSettings/`. Dirty Unity scenes = user/generated unless task needs them.
