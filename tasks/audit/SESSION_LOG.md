# Audit Remediation — Session Log

**Read this file first when resuming audit work in a new session.** It is the only place that
records *where we actually are*, as opposed to what was planned.

Companions:
- [`AUDIT_2026-08_REGISTER.md`](AUDIT_2026-08_REGISTER.md) — what is broken (82 findings, stable IDs).
- [`AUDIT_2026-08_PLAN.md`](AUDIT_2026-08_PLAN.md) — how to fix it, in 10 stages.

---

## Current Position

```
STAGE:        1 done, compile-verified, committing now
NEXT STAGE:   Stage 2 (editor safety net) — S, independent of 3-9
BRANCH:       refactor/level-builder-fcdata-cache
BASELINE:     267cfe8  chore: ignore vendor Asset Store plugin dirs (history rewritten 2026-08-03)
WORKTREE:     dirty (unrelated pre-existing WIP, untouched — Stage 1 + audit docs now committed)
BLOCKED ON:   nothing
BACKUP:       ~/open-fc-backup-mirror-2026-08-03.git — pre-purge mirror, keep until confident nothing else needs restoring from it
```

**History was rewritten on 2026-08-03.** Old commit SHAs on `refactor/level-builder-fcdata-cache`
(anything referencing `79f7aac` or earlier) no longer exist on origin. If you have another local
clone or a stale fetch, `git fetch` + `git reset --hard origin/refactor/level-builder-fcdata-cache`
(stash first) before doing anything else — do not attempt to merge old and new history.

Update this block at the end of every session that touches audit work. It is four lines; keep it
accurate rather than detailed — detail belongs in the log entries below.

---

## Session protocol

**At session start**
1. Read this file's *Current Position* and the newest log entry.
2. `git status` and `git log --oneline -5` — confirm the baseline matches. If it does not, the
   previous session did not close out; reconstruct from the diff before doing anything else.
3. Read the target stage in the plan. Do not re-derive the findings — the register is the source of
   truth and its line numbers were verified on the baseline commit.
4. If line numbers in the register no longer match the file, **re-locate by symbol name, do not
   guess** — and note the drift in your log entry.

**During**
- Work inside one stage. If a fix wants to escape the stage's file list, write it down under
  *Deferred* below and leave it.
- Any finding you disprove: set `St: wontfix` in the register with a one-line reason. Never delete
  the row.
- Any *new* problem found while fixing: add it to the register with the next free ID in its severity
  band, and mark the discovering stage in the `Stage` column.

**At session end (mandatory, even if nothing landed)**
1. Tick completed tasks in the plan's Stage Board and stage task lists.
2. Update `St` in the register for every finding touched.
3. Update *Current Position* above.
4. Append a log entry using the template below. Empty sessions still get an entry — "investigated
   X, found nothing, do not repeat" is the most valuable kind of note here.

---

## Verification constraints (this workstation)

Facts that repeatedly cost time; do not rediscover them.

- **Unity batch mode is unreliable here.** Licensing or a running Editor instance blocks CLI batch
  runs. `dotnet build` on the generated `.csproj` is not a substitute unless Unity has already
  produced `Temp/obj/.../project.assets.json`.
- **Transform and rendering work needs visual verification in the Editor.** There is no automated
  substitute today (Stage 8 is what changes this).
- **Original Far Cry data is not in the repo.** Tests that need it self-`Assert.Ignore`
  (see `A-M26`). A green suite on a clean machine proves nothing about the parser.
- **`MeshCacheVersionName` (`CgfMeshBuilder.cs:82`) must be bumped after any mesh/basis change**, or
  cached meshes keep the old layout and the change appears to do nothing.
- Generated scenes under `Assets/Scenes/Levels/` are routinely dirty for reasons unrelated to source
  changes — do not treat them as signal.

---

## Deferred / out-of-scope items

Things noticed during remediation that belong to a different stage or a different plan. Move them
into the register or a plan when they are ready to be worked, not before.

*(empty)*

---

## Open questions for the project owner

| # | Question | Blocks | Asked | Answered |
|---|----------|--------|-------|----------|
| Q1 | Is `github.com/VladimirDivak/open-fc` public? Determines whether the committed Sirenix DLLs need a history purge (`A-M29`). | Stage 0 | 2026-08-03 | 2026-08-03 — **public**, full history purge done |
| Q2 | Is additive / multi-level loading on the roadmap? If yes, Stage 6 ownership work becomes mandatory and `A-L30` (static `Current` locators) is a real blocker rather than a cleanup. | Stage 6, Stage 9 | 2026-08-03 | — |
| Q3 | Is SSPR intended to ship, or is it exploratory? Determines whether Stage 4 hardens it (`A-L20`, `A-L21`, `A-M22`) or the tier is removed. | Stage 4 | 2026-08-03 | — |

---

## Log

Newest first. Template:

```markdown
### YYYY-MM-DD — Stage N — <one-line outcome>

**Landed**: A-Hxx, A-Mxx (commit abc1234)
**Status changes**: A-Hxx open → done; A-Myy open → wontfix (reason)
**Measured**: before/after numbers, if the stage's Verification asked for them
**Surprises**: what the code actually did versus what the register claimed
**Next**: concrete first action for the next session
```

---

### 2026-08-03 — Stage 1 — Async main-thread contract fixed, NOT YET VERIFIED

**Landed**: `A-H01`, `A-H02`, `A-H03`, `A-M01`, `A-M02`, `A-M03`, `A-L11` — code changes only,
**not committed**. 9 files touched: `FcFileSystem.cs`, `FcBrushLoadService.cs`,
`FcVegetationLoadService.cs`, `FcEntityLoadService.cs`, `FcLevelLoadService.cs`,
`TextureRuntimeImportService.cs`, `CgfLodImportService.cs`, `CgfRuntimeImportService.cs`,
`CgfRuntimeImporter.cs`.

**Status changes**: `A-H01`, `A-H02`, `A-H03`, `A-M01`, `A-M02`, `A-M03`, `A-L11` open → done (in
the register; treat as tentative until verified — see below).

**What happened**: fixed the root cause (`A-H01`) first — `FcFileSystem.ReadAllBytesAsync` now
wraps the pool-side read in `try/finally` so `SwitchToMainThread` runs even when `ReadAllBytes`
throws. Then, rather than trusting that fix to propagate everywhere, added defense-in-depth at
every loader the register flagged (`A-H02`, `A-H03`): each hops back to the main thread as the
*first statement* of its `catch`/`finally` blocks, not just after a specific `await` line — a hop
placed only after the awaited call would be skipped entirely if that call is what threw, control
jumps straight to `catch`. This is a deliberate deviation from the register's literal wording
("SwitchToMainThread immediately after the awaits at :X/:Y"); the intent (state only touched from
main thread) is the same, the placement is more robust. Diagnostics races (`A-M01`) got one
`lock` covering both `HashSet`s, all plain counters, and the two format `Dictionary`s — deliberately
not split into `lock`-for-collections / `Interlocked`-for-ints, since these are report-only fields
and the simpler single-lock version is harder to get wrong. `A-M02`'s regex cache is now a
`ConcurrentDictionary`, and got a bonus `ClearSiblingRegexCache()` wired into
`CgfRuntimeImporter.TrimUnused()` (register asked for this, wasn't in the file list, added it
anyway since it's a one-line hook into an existing trim path — see `AUDIT_2026-08_PLAN.md` Stage 1
for the exact reasoning per task).

**Surprises**: `A-M03`'s fix required splitting `LoadLevelAsync` into a thin wrapper +
`LoadLevelInnerAsync` rather than a simple wrap, because the original method intentionally clears
`IsLoadInProgress` early (before spawning brushes/vegetation/entities) — that looked like a
deliberate handoff point for other systems polling the flag, so it was left untouched and the
`finally`-based reset is purely a safety net for the exception paths the register flagged.

**Compile verified 2026-08-03, same session**: the Sirenix incident below blocked the Editor
compile until fixed; once restored, `Library/ScriptAssemblies` shows all 20 expected DLLs
(`OpenFarCry.FileSystem[.Editor]`, `.Importer[.Editor][.Tests]`, `.Level[.Editor][.Tests]`,
`.Rendering.Water[.Editor]`), zero `error CS` lines in `~/.config/unity3d/Editor.log` after the
fix. **Compile-level verification only** — the three *runtime* checks in the plan's Stage 1
Verification section (missing-normal-map load completing, main-thread assertion in the loader
`finally` blocks, cancel-mid-flight resetting `IsLoadInProgress`) require actually loading a level
in Play Mode, which this session could not drive. Do those on the next level load and update this
entry if anything surfaces.

**Next**: open the project in the Editor, let it compile, fix anything that doesn't (the manual
review was careful but is not a substitute for the compiler). Then run the three Stage 1
verification checks from `AUDIT_2026-08_PLAN.md`. Only after that passes: commit (subject should
cite all seven IDs) and move to Stage 2.

**INCIDENT, same session, fixed**: the Stage 0 history purge's local reconciliation step
(`git reset --hard origin/<branch>` after the force-push, see the Stage 0 log entry) deleted the
real `Assets/Plugins/Sirenix` DLL/asset files from disk — not just from git. Root cause:
`git reset --hard` removes any file that was tracked in the pre-reset index but is absent from the
target commit's tree; Sirenix was tracked before (part of the purge) and absent from the rewritten
history, so the reset deleted all 93 files' worth of *actual content* it once tracked, not merely
its git metadata. Unity's Burst compiler surfaced this next session as
`Failed to resolve assembly 'OpenFarCry.FileSystem.Editor'` — a red herring; the real errors in
`~/.config/unity3d/Editor.log` were `CS0246: 'Sirenix'/'FolderPathAttribute'/... could not be
found`, i.e. every script using Odin Inspector attributes failed, which cascaded and left
`OpenFarCry.FileSystem`/`.Importer`/`.Level` (but not `.Rendering.Water`, which doesn't touch Odin)
without output DLLs in `Library/ScriptAssemblies`.

**The Stage 0 "byte-identical" verification was insufficient** — it diffed
`git status --porcelain=v1 -uall` before/after, but a file that is tracked-and-clean before and
gitignored-after is invisible to `git status` in *both* snapshots, so its deletion produced no
diff. The correct check (used to confirm the fix below) is: list every path `git ls-tree -r` on
the *pre-rewrite* ref, and confirm each one still exists on disk — a leaf `find -e` check per path,
independent of git status. Do this for the whole repo, not just the directory you think you
touched, if this procedure is ever repeated.

**Fix applied**: restored the full `Assets/Plugins/Sirenix` tree (93 files: 11 DLLs +
xml/asset/shader/config data) via
`git --git-dir=~/open-fc-backup-mirror-2026-08-03.git archive refs/heads/refactor/level-builder-fcdata-cache -- Assets/Plugins/Sirenix | tar -x -C "<repo>"`
— extracts historical file *content* only, touches neither the current repo's index nor its
`.gitignore` status, so the restored files land back as untracked/ignored exactly as intended.
Verified two ways: (1) full sorted-filename diff of the old tracked list vs. the restored disk
state — zero missing; (2) same `ls-tree` + existence check repeated across **all 3432** paths ever
tracked pre-rewrite (not just Sirenix) — zero missing anywhere in the repo, confirming the damage
was isolated to Sirenix (the only path the rewrite actually removed) and is now fully repaired.
Restored files are identical git blobs to the originals (same content ⇒ same `.meta` GUIDs), so no
Unity asset-reference breakage expected from the round-trip.

**Lesson for any future history rewrite + local reconciliation**: if the rewrite's `--invert-paths`
target was ever tracked, `git reset --hard` onto the new history **will delete it from disk**, not
just from git. Either restore it immediately from the mirror backup afterward (as done here), or —
better — do the restore-to-untracked step *before* running `reset --hard` isn't possible (reset is
what causes it), so: immediately after `reset --hard`, proactively `git archive` the purged path(s)
back out of the backup mirror as the very next step, before assuming the working tree is intact.

---

### 2026-08-03 — Stage 0 — Sirenix purged from public history, force-pushed

**Landed**: `A-M29` (commits `267cfe8` gitignore fix on top of rewritten history; history rewrite itself has no single commit — it replaced the whole ref).

**Status changes**: `A-M29` open → done.

**What happened**: repo confirmed public (Q1 answered). Verified only one branch
(`refactor/level-builder-fcdata-cache`) was ever pushed to `origin`; `main`,
`level-transform-fixes`, `level-vegetation-lod` are local-only and were left untouched — they still
contain the old Sirenix blobs in local history, harmless unless pushed later.

Backed up `origin` to `~/open-fc-backup-mirror-2026-08-03.git` (mirror clone, recovery point).
Installed `git-filter-repo` into a throwaway venv (no distro package installed, system pip is
externally-managed). Ran the rewrite in a **separate fresh mirror clone**, not the working
directory — `git-filter-repo` refuses non-fresh repos by design, and this also meant the working
directory's uncommitted state was never at risk from the rewrite step itself.

`--path Assets/Plugins/Sirenix --invert-paths` rewrote all 94 commits (Sirenix was added in the
root commit, so every descendant commit's tree changed → every SHA changed). Root
`834fa59` → `822475c`. Verified zero Sirenix files and zero `.dll` objects anywhere in
`git rev-list --objects --all` post-rewrite. Repo size 12M → 7M. Force-pushed the single branch.

Reconciled the working directory: snapshotted `git status --porcelain=v1 -uall` before touching
anything, `git stash push -u` (everything, tracked and untracked), `fetch` + `reset --hard` onto
the new tip, `stash pop`. Diffed the before/after status snapshots — **byte-identical**, confirming
none of the standing WIP (audit docs from the previous session, water/terrain edits, vendor asset
imports) was lost.

`.gitignore` updated for all four vendor dirs (`Sirenix`, `AmplifyImpostors`, `BOXOPHOBIC`,
`BOXOPHOBIC+`) and committed/pushed normally on top of the rewritten history.

**Surprises**: `git log --oneline --reverse | head -1` intermittently printed the wrong commit
(some non-root commit) both before and after the rewrite — a display/pipe quirk, not a data issue.
`git rev-list --max-parents=0 HEAD` is the reliable way to get the true root; used that for all
verification instead.

**Next**: Stage 1 — `FcFileSystem.ReadAllBytesAsync:144`, `try/finally` with `SwitchToMainThread`
(`A-H01`), root cause of `A-H02`/`A-H03`/`A-M01`/`A-M02`. If anyone else has an old clone of this
repo, they need to re-clone or hard-reset onto the new history — old SHAs are gone from origin.

---

### 2026-08-03 — Stage — — Audit performed, tracking docs created

**Landed**: nothing — audit and documentation only.

**What happened**: full-project audit across 8 dimensions (perf/GC, async/UniTask, memory lifecycle,
URP rendering, binary parsing, architecture, Unity correctness, tests/tooling), each dimension's
findings then re-checked by an adversarial verifier that read the cited code and tried to refute the
claim. 94 raw claims → **82 survived, 12 refuted**. Severity after re-grading: 0 critical, 12 high,
32 medium (29 after merging duplicates), 38 low.

**Register line numbers are valid as of `79f7aac`.** Nothing in the working tree at audit time
modified the audited `.cs` files, so the dirty worktree does not invalidate them.

**Findings that need reproduction before any code is written** (marked `needs-repro` in the
register, all verdict `PLAUSIBLE`):
- `A-H12` — prefab property modifications. Follows from the `PrefabUtility` contract, never observed.
- `A-M07` — duplicate `CgfFile` dropped by `StoreParsed`. Depends on whether the same key is really
  requested by both the sync and coalesced paths — content-dependent.
- `A-M09` — animation clip LRU destroying live clips. Needs >4096 distinct rig keys.
- `A-M23` — vegetation proxy MatID index space. Needs a CGF with more than one MULTI chunk; with a
  single MULTI the buggy filter is a no-op.

**Surprises worth remembering**:
- `BUILD_SCENE_PERF_PLAN.md` marks Phase 1 (PAK directory index O(1)) as done, but
  `PakArchive.GetEntriesInDirectory:67-84` still walks every key with `StartsWith`. Verified banners
  in the older plan docs are not reliable — re-check before trusting one.
- `FcVegetationLoadService` already implements the exact queue pattern `FcBrushLoadService` is
  missing (`HashSet` + `_distanceSortMaxPending`). Stage 3 should copy it rather than design one.
- Repo hygiene issue found outside the code audit: 93 tracked Sirenix files including DLLs, plus
  2234 untracked files from three other paid Asset Store packages that are absent from `.gitignore`.
  Filed as `A-M29`, Stage 0.

**Next**: answer Q1 (repo visibility) → Stage 0 if public, otherwise start Stage 1. Stage 1 task 1 is
`FcFileSystem.ReadAllBytesAsync:144` — `try/finally` with `SwitchToMainThread`.
