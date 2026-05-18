# CGF Inspector

Independent Python parser for Far Cry 1 binary assets (CGF/CGA geometry, CAF
animations, CAL animation scripts), reading straight from the game PAK
archives. Serves as a ground-truth reference for debugging the Unity C#
importer in `open-farcry`.

It is intentionally scoped to **inspection**, not asset export: it reports
counts, bounding boxes, hierarchies, transforms and metadata, and never dumps
full geometry or animation curves. Keep it that way — see "Scope" below.

## Requirements

- Python 3.8+ (stdlib only — `zipfile`, `struct`, `argparse`).
- A local Far Cry install at `~/Documents/farcry-game` (overridable with
  `--game`). PAK files are read directly; nothing is extracted or copied.

## Usage

Run from the repo root:

```sh
# locate an asset by substring
python3 tools/cgf-inspector/cgftool.py find merc_cover

# full parse + human summary
python3 tools/cgf-inspector/cgftool.py info objects/.../merc_cover.cgf

# machine-readable output / extra detail
python3 tools/cgf-inspector/cgftool.py info <path> --json
python3 tools/cgf-inspector/cgftool.py info <path> --full

# chunk table only
python3 tools/cgf-inspector/cgftool.py chunks <path>

# raw bytes to disk (debug only)
python3 tools/cgf-inspector/cgftool.py extract <path> -o /tmp/asset.cgf
```

`<path>` is a virtual path inside a pak (e.g.
`objects/characters/mercenaries/Merc_cover/merc_cover.cgf`) or an absolute disk
path. Pak lookup uses reverse mount order, so a patch pak overrides the base
pak; the tool reports which pak it used.

`info` auto-detects the asset kind by extension (`.cgf`/`.cga` → CGF,
`.caf` → CAF, `.cal` → CAL).

## Files

| File            | Role                                            |
|-----------------|-------------------------------------------------|
| `cgftool.py`    | CLI entry point and text/JSON summary printer.  |
| `fc_pak.py`     | PAK (zip) lookup and byte reading.              |
| `fc_chunked.py` | Shared binary cursor + FILE_HEADER/chunk table. |
| `fc_cgf.py`     | CGF/CGA geometry parser.                        |
| `fc_caf.py`     | CAF animation parser.                           |
| `fc_cal.py`     | CAL text animation-script parser.               |

## Source of truth

The binary layout mirrors:

- `~/Documents/farcry-sources/docs/asset-formats.md` — format documentation.
- `Assets/Scripts/Importer/Cgf/CgfParser.cs` and `CafParser.cs` — the C#
  importer this tool cross-checks.

Known layout note: `CryLink` is **20 bytes** (`int BoneID` + 3 float offset +
1 float blending), despite a stale `// 16 bytes` comment in `CgfData.cs`; the
C# code is correct because it uses `sizeof(CryLink)`.

## Scope

This is a debugging companion, kept deliberately small to avoid drifting from
the C# importer:

- Inspection and cross-checking only — no mesh/material/animation export.
- No full geometry or animation-curve dumps. Far Cry geometry is copyrighted
  and must not be reproduced in project files.
- When in doubt, the C# importer behaviour is what ships; this tool exists to
  tell you what the *file* actually contains so you can find the mismatch.

The `cgf-inspector` subagent (`.claude/agents/cgf-inspector.md`) wraps this
tool so the main Claude Code thread can ask for asset facts without spending
context on raw output.
