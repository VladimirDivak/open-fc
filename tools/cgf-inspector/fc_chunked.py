"""Shared binary cursor and chunk-table reader for CGF/CGA/CAF containers.

Mirrors BinaryBufferReader.cs and the FILE_HEADER / CHUNK_HEADER layout
documented in farcry-sources/docs/asset-formats.md section 1.
"""

import struct

MAGIC = b"CryTek"
FILE_VERSION = 0x0744
FILE_HEADER_SIZE = 20
CHUNK_HEADER_SIZE = 16

FILE_TYPE_GEOM = 0xFFFF0000
FILE_TYPE_ANIM = 0xFFFF0001

CHUNK_NAMES = {
    0xCCCC0000: "Mesh",
    0xCCCC0001: "Helper",
    0xCCCC0003: "BoneAnim",
    0xCCCC0005: "BoneNameList",
    0xCCCC000B: "Node",
    0xCCCC000C: "Mtl",
    0xCCCC000D: "Controller",
    0xCCCC000E: "Timing",
    0xCCCC000F: "BoneMesh",
    0xCCCC0010: "BoneLightBinding",
    0xCCCC0011: "MeshMorphTarget",
    0xCCCC0012: "BoneInitPos",
}


class Cur:
    """Little-endian binary cursor. `base` anchors align() to a chunk start."""

    def __init__(self, data, offset=0, end=None, base=None):
        self.d = data
        self.o = offset
        self.end = len(data) if end is None else end
        self.base = offset if base is None else base

    def remaining(self):
        return self.end - self.o

    def _need(self, n):
        if self.o + n > self.end:
            raise EOFError(f"read past end (offset {self.o}, need {n}, end {self.end})")

    def u8(self):
        self._need(1)
        v = self.d[self.o]
        self.o += 1
        return v

    def i32(self):
        self._need(4)
        v = struct.unpack_from("<i", self.d, self.o)[0]
        self.o += 4
        return v

    def u32(self):
        self._need(4)
        v = struct.unpack_from("<I", self.d, self.o)[0]
        self.o += 4
        return v

    def f32(self):
        self._need(4)
        v = struct.unpack_from("<f", self.d, self.o)[0]
        self.o += 4
        return v

    def vec3(self):
        return [self.f32(), self.f32(), self.f32()]

    def quat(self):
        return [self.f32(), self.f32(), self.f32(), self.f32()]

    def mat44(self):
        # row-major 4x4
        return [self.f32() for _ in range(16)]

    def mat43(self):
        # row-major 4x3
        return [self.f32() for _ in range(12)]

    def skip(self, n):
        self.o += n

    def align(self, a):
        rel = self.o - self.base
        self.o = self.base + ((rel + a - 1) & ~(a - 1))

    def fixed_str(self, n):
        self._need(n)
        raw = self.d[self.o:self.o + n]
        self.o += n
        z = raw.find(0)
        if z >= 0:
            raw = raw[:z]
        return raw.decode("ascii", "replace").strip()

    def cstr(self):
        z = self.d.find(0, self.o, self.end)
        if z < 0:
            z = self.end
        s = self.d[self.o:z].decode("ascii", "replace")
        self.o = z + 1
        return s


def read_file_header(data, kind):
    """Parse the 20-byte FILE_HEADER. Returns (file_type, version, table_offset)."""
    if data is None or len(data) < FILE_HEADER_SIZE:
        raise ValueError(f"{kind}: file is too small ({0 if data is None else len(data)} bytes).")
    sig = data[:7].split(b"\0", 1)[0]
    if not sig.startswith(MAGIC):
        raise ValueError(f"{kind}: bad signature {sig!r}, expected {MAGIC!r}.")
    file_type, version, table_offset = struct.unpack_from("<III", data, 8)
    if file_type not in (FILE_TYPE_GEOM, FILE_TYPE_ANIM):
        raise ValueError(f"{kind}: unsupported FileType 0x{file_type:08X}.")
    if version != FILE_VERSION:
        raise ValueError(f"{kind}: unsupported file version 0x{version:X}, expected 0x{FILE_VERSION:X}.")
    return file_type, version, table_offset


def read_chunk_table(data, table_offset):
    """Parse the trailing chunk table. Returns list of chunk dicts with
    type/version/offset/id and a computed size (next-offset - this-offset)."""
    length = len(data)
    if table_offset <= FILE_HEADER_SIZE or table_offset > length - 4:
        raise ValueError(f"invalid ChunkTableOffset {table_offset}.")
    count = struct.unpack_from("<I", data, table_offset)[0]
    table_bytes = 4 + count * CHUNK_HEADER_SIZE
    if table_offset + table_bytes > length:
        raise ValueError("chunk table exceeds file bounds.")

    chunks = []
    p = table_offset + 4
    for _ in range(count):
        ctype, cver, coff, cid = struct.unpack_from("<IiiI", data, p)
        p += CHUNK_HEADER_SIZE
        chunks.append({"type": ctype, "version": cver, "offset": coff, "id": cid})

    offsets = sorted({c["offset"] for c in chunks})
    for c in chunks:
        off = c["offset"]
        if off < FILE_HEADER_SIZE or off >= table_offset:
            raise ValueError(f"invalid chunk offset {off} for chunk id {c['id']}.")
        idx = offsets.index(off)
        nxt = offsets[idx + 1] if idx + 1 < len(offsets) else table_offset
        c["size"] = nxt - off
        c["type_name"] = CHUNK_NAMES.get(c["type"], f"0x{c['type']:08X}")
    return chunks
