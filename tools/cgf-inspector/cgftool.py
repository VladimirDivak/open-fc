#!/usr/bin/env python3
"""CGF inspector CLI for the open-farcry project.

Parses Far Cry CGF/CGA/CAF/CAL assets straight from PAK archives (or disk)
and prints a structured summary. Intended as an independent ground-truth
reference for debugging the Unity C# importer.

Usage:
  cgftool.py find   <pattern>          search asset names inside paks
  cgftool.py info   <path>             full parse + summary (cgf/cga/caf/cal)
  cgftool.py chunks <path>             chunk table only
  cgftool.py extract <path> -o FILE    write raw bytes to disk (debug)

Options:
  --game PATH   Far Cry install root (default ~/Documents/farcry-game)
  --json        emit JSON instead of text summary
  --full        keep per-element detail (matrices, ticks, bone-id lists)

<path> is a virtual asset path inside a pak (e.g. objects/.../merc_cover.cgf)
or an absolute disk path. Pak lookup uses reverse mount order (patch override).
"""

import argparse
import json
import os
import sys

import fc_pak
from fc_caf import parse_caf
from fc_cal import parse_cal
from fc_cgf import parse_cgf
from fc_chunked import read_chunk_table, read_file_header


def _detect_kind(path, data):
    ext = os.path.splitext(path)[1].lower()
    if ext in (".cgf", ".cga"):
        return "cgf"
    if ext == ".caf":
        return "caf"
    if ext == ".cal":
        return "cal"
    if data[:6] == b"CryTek":
        return "cgf"
    return "cal"


def cmd_find(args):
    hits = fc_pak.find_in_paks(args.pattern, args.game)
    if args.json:
        print(json.dumps([{"pak": os.path.basename(p), "entry": n}
                          for p, n in hits], indent=2))
        return 0
    if not hits:
        print(f"no matches for '{args.pattern}'")
        return 1
    for pak, name in hits:
        print(f"{os.path.basename(pak):20} {name}")
    print(f"\n{len(hits)} match(es)")
    return 0


def cmd_info(args):
    data, source = fc_pak.read_file(args.path, args.game)
    kind = _detect_kind(args.path, data)
    if kind == "cgf":
        result = parse_cgf(data, full=args.full)
    elif kind == "caf":
        result = parse_caf(data, full=args.full)
    else:
        result = parse_cal(data)
    result["source"] = source
    result["size_bytes"] = len(data)

    if args.json:
        print(json.dumps(result, indent=2, default=str))
    else:
        _print_summary(result)
    return 0


def cmd_chunks(args):
    data, source = fc_pak.read_file(args.path, args.game)
    _, version, table_offset = read_file_header(data, "FILE")
    chunks = read_chunk_table(data, table_offset)
    if args.json:
        print(json.dumps(chunks, indent=2))
        return 0
    print(f"{source}  ({len(data)} bytes, version 0x{version:04X})")
    print(f"{'type':20} {'version':9} {'id':>8} {'offset':>10} {'size':>10}")
    for c in chunks:
        print(f"{c['type_name']:20} 0x{c['version']:04X}    {c['id']:>8} "
              f"{c['offset']:>10} {c['size']:>10}")
    return 0


def cmd_extract(args):
    data, source = fc_pak.read_file(args.path, args.game)
    with open(args.out, "wb") as f:
        f.write(data)
    print(f"wrote {len(data)} bytes from {source} -> {args.out}")
    return 0


def _print_summary(r):
    print(f"source : {r['source']}")
    print(f"size   : {r['size_bytes']} bytes")
    kind = r["kind"]

    if kind == "cgf":
        print(f"kind   : CGF/CGA  file_type {r['file_type']}  version {r['version']}")
        print(f"chunks : {r['chunk_count']}")
        ctab = {}
        for c in r["chunk_table"]:
            ctab[c["type"]] = ctab.get(c["type"], 0) + 1
        print("         " + ", ".join(f"{k}x{v}" for k, v in sorted(ctab.items())))

        print(f"\nMESHES ({len(r['meshes'])}):")
        for m in r["meshes"]:
            tag = " [bone-mesh]" if m.get("is_bone_mesh") else ""
            skin = ""
            if m["skinning"]:
                s = m["skinning"]
                skin = (f"  skin: {s['total_links']} links, "
                        f"{s['distinct_bone_ids']} bones")
            print(f"  #{m['chunk_id']}{tag}: {m['n_verts']}v {m['n_faces']}f "
                  f"{m['n_tverts']}uv  vcol={m['has_vertex_color']}{skin}")
            print(f"    bbox {m['bbox_min']} .. {m['bbox_max']}")
            print(f"    submesh MatIDs (face count): {m['submesh_mat_ids']}")

        print(f"\nMATERIALS ({len(r['materials'])}):")
        for m in r["materials"]:
            extra = ""
            if m["mtl_type"] == "Multi":
                extra = f"  children={m.get('child_count')}"
            else:
                tx = m.get("textures", {})
                tex = [f"{k}={v}" for k, v in tx.items() if v]
                extra = "  " + ("; ".join(tex) if tex else "(no textures)")
            flags = m.get("flags")
            fl = f"  flags={flags}" if flags else ""
            print(f"  #{m['chunk_id']} {m['version']} [{m['mtl_type']}] "
                  f"'{m['name']}' shader={m['shader']}{fl}")
            if extra.strip():
                print(f"    {extra.strip()}")

        print(f"\nNODE TREE ({len(r['nodes'])} nodes):")
        for n in r["node_tree"]:
            pad = "  " + "  " * n["depth"]
            proxy = " [proxy]" if n["is_proxy"] else ""
            print(f"{pad}{n['name']}{proxy}  (chunk #{n['chunk_id']}, "
                  f"object #{n['object_id']}, pos {n['pos']})")

        bnl = r.get("bone_name_list")
        if bnl:
            print(f"\nBONE NAMES ({bnl['count']}):")
            print("  " + ", ".join(bnl["names"][:40])
                  + (" ..." if bnl["count"] > 40 else ""))
        ba = r.get("bone_anim")
        if ba:
            print(f"\nBONE ANIM: {ba['bone_count']} bones (BONE_ENTITY hierarchy)")
        for bip in r.get("bone_init_pos", []):
            print(f"BONE INIT POS: mesh #{bip['mesh_chunk_id']}, "
                  f"{bip['num_bones']} bind matrices")

    elif kind == "caf":
        print(f"kind   : CAF  file_type {r['file_type']}  version {r['version']}")
        t = r.get("timing")
        if t and "secs_per_tick" in t:
            print(f"timing : {t['secs_per_tick']:.6f}s/tick, {t['ticks_per_frame']} ticks/frame, "
                  f"frames {t['global_start_frame']}..{t['global_end_frame']}, "
                  f"{t['duration_sec']}s, ~{t['approx_fps']}fps")
        print(f"\nTRACKS ({r['track_count']}):")
        for tr in r["tracks"]:
            sup = "" if tr.get("supported") else "  [UNSUPPORTED]"
            print(f"  controller {tr.get('controller_id')}  {tr['version']} "
                  f"{tr.get('format', '')}  keys={tr.get('num_keys')}  "
                  f"ticks={tr.get('tick_range')}{sup}")

    elif kind == "cal":
        print("kind   : CAL (text animation script)")
        if r["directives"]:
            print("\nDIRECTIVES:")
            for k, v in r["directives"].items():
                print(f"  {k} = {v}")
        print(f"\nANIMATIONS ({r['animation_count']}):")
        for a in r["animations"]:
            print(f"  {a['alias']:28} {a['path']}")
        if r["dummy_animations"]:
            print(f"\nDUMMY ANIMATIONS: {', '.join(r['dummy_animations'])}")
        if r["unparsed_lines"]:
            print(f"\nUNPARSED LINES ({len(r['unparsed_lines'])}):")
            for l in r["unparsed_lines"]:
                print(f"  {l}")

    errs = r.get("errors")
    if errs:
        print(f"\nERRORS ({len(errs)}):")
        for e in errs:
            print(f"  ! {e}")


def main(argv):
    p = argparse.ArgumentParser(prog="cgftool", description=__doc__,
                                formatter_class=argparse.RawDescriptionHelpFormatter)
    # Shared options, inherited by every subcommand so they may appear either
    # before or after the subcommand name.
    common = argparse.ArgumentParser(add_help=False)
    common.add_argument("--game", default=fc_pak.DEFAULT_GAME, help="Far Cry install root")
    common.add_argument("--json", action="store_true", help="emit JSON")
    common.add_argument("--full", action="store_true", help="keep per-element detail")

    sub = p.add_subparsers(dest="cmd", required=True)

    sp = sub.add_parser("find", parents=[common], help="search asset names in paks")
    sp.add_argument("pattern")
    sp.set_defaults(func=cmd_find)

    sp = sub.add_parser("info", parents=[common], help="parse + summary")
    sp.add_argument("path")
    sp.set_defaults(func=cmd_info)

    sp = sub.add_parser("chunks", parents=[common], help="chunk table only")
    sp.add_argument("path")
    sp.set_defaults(func=cmd_chunks)

    sp = sub.add_parser("extract", parents=[common], help="write raw bytes to disk")
    sp.add_argument("path")
    sp.add_argument("-o", "--out", required=True)
    sp.set_defaults(func=cmd_extract)

    args = p.parse_args(argv)
    try:
        return args.func(args)
    except FileNotFoundError as ex:
        print(f"error: {ex}", file=sys.stderr)
        return 2
    except (ValueError, EOFError) as ex:
        print(f"parse error: {ex}", file=sys.stderr)
        return 3


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
