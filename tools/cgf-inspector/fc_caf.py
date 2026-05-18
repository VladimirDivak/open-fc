"""CAF binary animation parser.

Mirrors CafParser.cs. Reads Timing and Controller chunks. Per-key data is
summarized (key count, tick range), not dumped.
"""

from fc_chunked import (CHUNK_HEADER_SIZE, Cur, read_chunk_table,
                        read_file_header)


def _looks_like_embedded_header(data, off, ctype, cver):
    if len(data) - off < CHUNK_HEADER_SIZE:
        return False
    import struct
    t, v = struct.unpack_from("<Ii", data, off)
    return t == ctype and v == cver


def parse_caf(data, full=False):
    file_type, version, table_offset = read_file_header(data, "CAF")
    chunks = read_chunk_table(data, table_offset)

    out = {
        "kind": "caf",
        "file_type": f"0x{file_type:08X}",
        "version": f"0x{version:04X}",
        "chunk_count": len(chunks),
        "timing": None,
        "tracks": [],
        "errors": [],
    }

    for c in chunks:
        try:
            tn = c["type_name"]
            if tn == "Timing":
                out["timing"] = _parse_timing(data, c)
            elif tn == "Controller":
                track = _parse_controller(data, c, full)
                out["tracks"].append(track)
        except Exception as ex:
            out["errors"].append(
                f"chunk {c['type_name']} id={c['id']} ver=0x{c['version']:X}: "
                f"{type(ex).__name__}: {ex}")

    out["track_count"] = len(out["tracks"])
    return out


def _parse_timing(data, c):
    off = c["offset"]
    if c["version"] != 0x0918:
        return {"chunk_id": c["id"], "version": f"0x{c['version']:04X}",
                "note": "unsupported timing version"}
    r = Cur(data, off, off + c["size"], base=off)
    if _looks_like_embedded_header(data, off, c["type"], c["version"]):
        r.skip(CHUNK_HEADER_SIZE)
    secs_per_tick = r.f32()
    ticks_per_frame = r.i32()
    r.skip(32)  # RANGE_ENTITY name
    start = r.i32()
    end = r.i32()
    sub_ranges = r.i32()
    fps = round(1.0 / secs_per_tick / ticks_per_frame, 3) if secs_per_tick and ticks_per_frame else None
    # global_start/end are frame indices; one frame is `ticks_per_frame` ticks.
    duration = None
    if secs_per_tick and ticks_per_frame:
        duration = round((end - start) * ticks_per_frame * secs_per_tick, 4)
    return {
        "secs_per_tick": secs_per_tick, "ticks_per_frame": ticks_per_frame,
        "global_start_frame": start, "global_end_frame": end,
        "sub_ranges": sub_ranges,
        "duration_sec": duration,
        "approx_fps": fps,
    }


def _parse_controller(data, c, full):
    off = c["offset"]
    ver = c["version"]
    if ver == 0x0827:
        return _parse_controller_0827(data, c, full)
    if ver == 0x0826:
        return _parse_controller_0826(data, c, full)
    return {"chunk_id": c["id"], "version": f"0x{ver:04X}",
            "supported": False, "note": "unsupported controller version"}


def _parse_controller_0827(data, c, full):
    off = c["offset"]
    r = Cur(data, off, off + c["size"], base=off)
    num_keys = r.u32()
    controller_id = r.u32()
    ticks = []
    for _ in range(num_keys):
        ticks.append(r.i32())
        r.skip(24)  # pos vec3 + rotation-log vec3
    return {
        "chunk_id": c["id"], "version": "0x0827", "format": "CryKeyPQLog",
        "supported": True, "controller_id": controller_id,
        "num_keys": num_keys,
        "tick_range": [min(ticks), max(ticks)] if ticks else None,
        "ticks": ticks if full else None,
    }


def _parse_controller_0826(data, c, full):
    off = c["offset"]
    r = Cur(data, off, off + c["size"], base=off)
    if _looks_like_embedded_header(data, off, c["type"], c["version"]):
        r.skip(16)
    ctrl_type = r.i32()
    num_keys = r.i32()
    r.u32()  # flags
    controller_id = r.u32()
    supported = ctrl_type == 1  # CTRL_CRYBONE
    ticks = []
    if supported:
        for _ in range(num_keys):
            ticks.append(r.i32())
            r.skip(40)  # abspos(12) + relpos(12) + relquat(16)
    return {
        "chunk_id": c["id"], "version": "0x0826",
        "format": "CryKey", "ctrl_type": ctrl_type,
        "supported": supported, "controller_id": controller_id,
        "num_keys": num_keys,
        "tick_range": [min(ticks), max(ticks)] if ticks else None,
        "ticks": ticks if full else None,
    }
