"""CGF / CGA static + skinned geometry parser.

Mirrors CgfParser.cs. Reads chunk structure, mesh metadata, node hierarchy,
material chunks and bone data. Geometry (vertex/face arrays) is summarized,
not dumped: this is an inspection tool, not an asset exporter.
"""

from fc_chunked import (Cur, read_chunk_table, read_file_header)

MTL_TYPES = {0: "Unknown", 1: "Standard", 2: "Multi", 3: "TwoSided"}
MTL_FLAGS = {
    0x002: "TwoSided", 0x010: "Additive", 0x020: "Subtractive",
    0x040: "CryShader", 0x080: "Physicalize", 0x100: "AdditiveDecal",
}


def _flag_names(flags):
    return [name for bit, name in MTL_FLAGS.items() if flags & bit]


def parse_cgf(data, full=False):
    """Parse CGF/CGA bytes into a plain dict. `full` keeps per-element detail."""
    file_type, version, table_offset = read_file_header(data, "CGF")
    chunks = read_chunk_table(data, table_offset)

    out = {
        "kind": "cgf",
        "file_type": f"0x{file_type:08X}",
        "version": f"0x{version:04X}",
        "chunk_count": len(chunks),
        "chunk_table": [
            {"type": c["type_name"], "version": f"0x{c['version']:04X}",
             "id": c["id"], "offset": c["offset"], "size": c["size"]}
            for c in chunks
        ],
        "meshes": [], "nodes": [], "materials": [],
        "bone_name_list": None, "bone_anim": None, "bone_init_pos": [],
        "errors": [],
    }

    for c in chunks:
        try:
            tn = c["type_name"]
            if tn == "Mesh":
                out["meshes"].append(_parse_mesh(data, c, full))
            elif tn == "BoneMesh":
                m = _parse_mesh(data, c, full)
                m["is_bone_mesh"] = True
                out["meshes"].append(m)
            elif tn == "Node":
                out["nodes"].append(_parse_node(data, c, full))
            elif tn == "Mtl":
                out["materials"].append(_parse_material(data, c))
            elif tn == "BoneNameList":
                out["bone_name_list"] = _parse_bone_name_list(data, c)
            elif tn == "BoneAnim":
                out["bone_anim"] = _parse_bone_anim(data, c)
            elif tn == "BoneInitPos":
                out["bone_init_pos"].append(_parse_bone_init_pos(data, c, full))
        except Exception as ex:
            out["errors"].append(
                f"chunk {c['type_name']} id={c['id']} ver=0x{c['version']:X} "
                f"off=0x{c['offset']:X}: {type(ex).__name__}: {ex}")

    out["node_tree"] = _build_node_tree(out["nodes"])
    return out


def _parse_mesh(data, c, full):
    off = c["offset"]
    r = Cur(data, off, off + c["size"], base=off)
    r.skip(16)
    has_bone = r.u8() != 0
    has_vcol = r.u8() != 0
    r.align(4)
    n_verts = r.i32()
    n_tverts = r.i32()
    n_faces = r.i32()
    vert_anim_id = r.i32()

    bbox_min = [float("inf")] * 3
    bbox_max = [float("-inf")] * 3
    for _ in range(n_verts):
        px, py, pz = r.f32(), r.f32(), r.f32()
        r.skip(12)  # normal
        for i, v in enumerate((px, py, pz)):
            if v < bbox_min[i]:
                bbox_min[i] = v
            if v > bbox_max[i]:
                bbox_max[i] = v

    mat_ids = {}
    sm_groups = set()
    for _ in range(n_faces):
        r.skip(12)  # v0,v1,v2
        mid = r.i32()
        sg = r.i32()
        mat_ids[mid] = mat_ids.get(mid, 0) + 1
        sm_groups.add(sg)

    r.skip(n_tverts * 8)  # UVs
    if n_tverts > 0:
        r.skip(n_faces * 12)  # TexFaces

    bone_summary = None
    if has_bone:
        bone_ids = set()
        total_links = 0
        max_links = 0
        for _ in range(n_verts):
            num = r.u32()
            if num > 32:
                raise ValueError(f"invalid link count {num}")
            total_links += num
            max_links = max(max_links, num)
            for _ in range(num):
                bone_ids.add(r.i32())
                r.skip(16)  # CryLink tail: offset(3f) + blending(f); CryLink is 20 bytes
        bone_summary = {
            "total_links": total_links, "max_links_per_vertex": max_links,
            "distinct_bone_ids": len(bone_ids),
        }
        if full:
            bone_summary["bone_ids"] = sorted(bone_ids)

    if has_vcol:
        r.skip(n_verts * 3)

    has_geom = all(v != float("inf") for v in bbox_min)
    mesh = {
        "chunk_id": c["id"],
        "n_verts": n_verts, "n_tverts": n_tverts, "n_faces": n_faces,
        "has_bone_info": has_bone, "has_vertex_color": has_vcol,
        "vert_anim_id": vert_anim_id,
        "bbox_min": [round(v, 4) for v in bbox_min] if has_geom else None,
        "bbox_max": [round(v, 4) for v in bbox_max] if has_geom else None,
        "submesh_mat_ids": dict(sorted(mat_ids.items())),
        "smoothing_group_count": len(sm_groups),
        "skinning": bone_summary,
    }
    return mesh


def _parse_node(data, c, full):
    off = c["offset"]
    if c["version"] != 0x0823:
        raise ValueError(f"unsupported Node version 0x{c['version']:X}")
    r = Cur(data, off, off + c["size"], base=off)
    r.skip(16)
    name = r.fixed_str(64)
    object_id = r.i32()
    parent_id = r.i32()
    n_children = r.i32()
    mat_id = r.i32()
    r.u8()  # IsGroupHead
    r.u8()  # IsGroupMember
    r.align(4)
    transform = r.mat44()
    pos = r.vec3()
    rot = r.quat()
    scale = r.vec3()
    r.i32(); r.i32(); r.i32()  # pos/rot/scl controller ids
    prop_len = r.i32()
    properties = ""
    if 0 < prop_len <= c["size"]:
        properties = bytes(data[r.o:r.o + prop_len]).split(b"\0", 1)[0].decode("ascii", "replace")
        r.skip(prop_len)
    children = [r.i32() for _ in range(n_children)]

    return {
        "chunk_id": c["id"], "name": name,
        "object_id": object_id, "parent_id": parent_id,
        "mat_id": mat_id, "children_ids": children,
        "pos": [round(v, 5) for v in pos],
        "rot": [round(v, 6) for v in rot],
        "scale": [round(v, 5) for v in scale],
        "transform_rowmajor_4x4": [round(v, 6) for v in transform],
        "properties": properties,
        "is_proxy": "proxy" in name.lower(),
    }


def _parse_material(data, c):
    off = c["offset"]
    ver = c["version"]
    r = Cur(data, off, off + c["size"], base=off)
    r.skip(16)
    out = {"chunk_id": c["id"], "version": f"0x{ver:04X}"}

    name = r.fixed_str(64)
    out["name"] = name
    shader = ""
    if "(" in name and ")" in name:
        shader = name[name.index("(") + 1:name.index(")")].strip().lower()
    elif "/" in name:
        shader = name.split("/")[0].strip().lower()
    else:
        shader = name.strip().lower()
    out["shader"] = shader

    if ver == 0x0746:
        r.skip(60)
        out["alpha_test"] = round(r.f32(), 4)
    mtl_type = r.i32() if ver in (0x0744, 0x0745, 0x0746) else 1
    out["mtl_type"] = MTL_TYPES.get(mtl_type, str(mtl_type))
    if mtl_type == 2:  # Multi
        out["child_count"] = r.i32()
        return out

    if ver in (0x0744, 0x0745, 0x0746):
        out["diffuse_color"] = [r.u8(), r.u8(), r.u8()]
        out["specular_color"] = [r.u8(), r.u8(), r.u8()]
        r.skip(3)  # col_a
    if ver in (0x0745, 0x0746):
        r.skip(3)  # pack(4) padding
        out["spec_level"] = round(r.f32(), 4)
        out["spec_shininess"] = round(r.f32(), 4)
        out["self_illum"] = round(r.f32(), 4)
        out["opacity"] = round(r.f32(), 4)

    if ver == 0x0746:
        r.skip(236)
        tex = _read_tex_block(r, 128, 108)
        out["flags"] = _flag_names(_read_flags_0746(r))
        out["textures"] = tex
    elif ver == 0x0745:
        r.skip(108)
        tex = _read_tex_block(r, 32, 76)
        out["flags"] = _flag_names(_read_flags_0745(r))
        out["textures"] = tex
    elif ver == 0x0744:
        out["textures"] = {
            "diffuse": _norm_tex(r.fixed_str(32)),
        }
        r.skip(60)
        out["textures"]["opacity"] = _norm_tex(r.fixed_str(32))
        r.skip(60)
        out["textures"]["normal"] = _norm_tex(r.fixed_str(32))
    return out


def _read_tex_block(r, name_len, tail):
    """Read diffuse/specular/opacity/normal/gloss texture names (0745/0746 layout)."""
    diffuse = _norm_tex(r.fixed_str(name_len)); r.skip(tail)
    specular = _norm_tex(r.fixed_str(name_len)); r.skip(tail)
    opacity = _norm_tex(r.fixed_str(name_len)); r.skip(tail)
    normal = _norm_tex(r.fixed_str(name_len)); r.skip(tail)
    gloss = _norm_tex(r.fixed_str(name_len)); r.skip(tail)
    block = 236 if name_len == 128 else 108
    r.skip(4 * block)  # tex_fl, tex_rl, tex_subsurf, tex_det
    return {"diffuse": diffuse, "specular": specular, "opacity": opacity,
            "normal": normal, "gloss": gloss}


def _read_flags_0746(r):
    return r.i32()


def _read_flags_0745(r):
    return r.i32()


def _norm_tex(raw):
    if not raw:
        return ""
    return raw.replace("\\", "/").replace("\0", "").strip().lower()


def _parse_bone_name_list(data, c):
    off = c["offset"]
    r = Cur(data, off, off + c["size"], base=off)
    names = []
    if c["version"] == 0x0744:
        r.skip(16)
        n = r.i32()
        names = [r.fixed_str(64) for _ in range(n)]
    elif c["version"] == 0x0745:
        n = r.i32()
        names = [r.cstr() for _ in range(n)]
    else:
        raise ValueError(f"unsupported BoneNameList version 0x{c['version']:X}")
    return {"chunk_id": c["id"], "count": len(names), "names": names}


def _parse_bone_anim(data, c):
    off = c["offset"]
    if c["version"] != 0x0290:
        raise ValueError(f"unsupported BoneAnim version 0x{c['version']:X}")
    r = Cur(data, off, off + c["size"], base=off)
    r.skip(16)
    bone_count = r.i32()
    head = 16  # BoneID, ParentID, ChildrenCount, ControllerID
    payload = c["size"] - 16 - 4
    bones = []
    if bone_count > 0:
        if payload % bone_count != 0:
            raise ValueError(f"payload {payload} not divisible by {bone_count} bones")
        entity_size = payload // bone_count
        tail = entity_size - head
        for _ in range(bone_count):
            bone = {
                "bone_id": r.i32(), "parent_id": r.i32(),
                "children_count": r.i32(), "controller_id": r.u32(),
            }
            tail_start = r.o
            if tail >= 32:
                bone["properties"] = r.fixed_str(32)
            if tail >= 40:
                bone["phys_geom_chunk_id"] = r.i32()
                bone["phys_flags"] = r.i32()
            r.o = tail_start + tail
            bones.append(bone)
    return {"chunk_id": c["id"], "bone_count": bone_count, "bones": bones}


def _parse_bone_init_pos(data, c, full):
    off = c["offset"]
    if c["version"] != 0x0001:
        raise ValueError(f"unsupported BoneInitPos version 0x{c['version']:X}")
    r = Cur(data, off, off + c["size"], base=off)
    mesh_chunk_id = r.u32()
    num_bones = r.u32()
    matrices = []
    for _ in range(num_bones):
        m = r.mat43()
        if full:
            matrices.append([round(v, 6) for v in m])
    return {
        "chunk_id": c["id"], "mesh_chunk_id": mesh_chunk_id,
        "num_bones": num_bones,
        "bind_matrices_4x3": matrices if full else "<{} matrices, use --full>".format(num_bones),
    }


def _build_node_tree(nodes):
    by_id = {n["chunk_id"]: n for n in nodes}
    roots = []

    def fmt(n, depth):
        line = {
            "name": n["name"], "chunk_id": n["chunk_id"],
            "object_id": n["object_id"], "depth": depth,
            "pos": n["pos"], "is_proxy": n["is_proxy"],
        }
        return line

    flat = []

    def walk(n, depth):
        flat.append(fmt(n, depth))
        for cid in n["children_ids"]:
            child = by_id.get(cid)
            if child:
                walk(child, depth + 1)

    for n in nodes:
        if n["parent_id"] not in by_id or n["parent_id"] == n["chunk_id"]:
            roots.append(n)
    for r in roots:
        walk(r, 0)
    # nodes never reached (broken parent links) appended flat
    seen = {f["chunk_id"] for f in flat}
    for n in nodes:
        if n["chunk_id"] not in seen:
            flat.append(fmt(n, 0))
    return flat
