"""CAL text animation-script parser.

CAL is a plain-text alias-to-CAF binding list, not a binary container.
See farcry-sources/docs/asset-formats.md section 4. Mirrors
CryModelLoader.cpp loadAnimationsWithCAL().
"""

DIRECTIVES = {
    "$animationdir", "$animdir", "$animationdirectory", "$animdirectory",
    "$modeloffsetx", "$modeloffsety", "$modeloffsetz",
    "$autounload", "$delayload",
}


def parse_cal(data):
    text = data.decode("ascii", "replace") if isinstance(data, (bytes, bytearray)) else data
    out = {
        "kind": "cal",
        "directives": {}, "animations": [], "dummy_animations": [],
        "unparsed_lines": [], "line_count": 0,
    }
    for raw in text.splitlines():
        out["line_count"] += 1
        line = raw.split(";", 1)[0].split("//", 1)[0].strip()
        if not line:
            continue
        if "=" not in line:
            out["unparsed_lines"].append(raw.strip())
            continue
        key, _, value = line.partition("=")
        key = key.strip()
        value = value.strip()
        low = key.lower()
        if low in DIRECTIVES:
            out["directives"][low] = value
        elif value == "?":
            out["dummy_animations"].append(key)
        else:
            out["animations"].append({"alias": key, "path": value})
    out["animation_count"] = len(out["animations"])
    return out
