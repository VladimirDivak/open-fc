# Importer Zero-Allocation & Jobs Refactor Plan

Snapshot: 2026-05-17. Scope: `OpenFarCry.Importer` assembly.

## Problem

Current importer uses `BinaryReader` and `BitConverter` in tight loops.
Reads values one-by-one. Creates millions of managed objects (`CryVertex`, `CryFace`, `Vector3`, `Quaternion`).
Garbage Collector gets angry. CPU stalls on memory allocation. Main thread blocks during CAF/CGF parse.

## Goal

Zero-allocation binary parsing using `unsafe` Reinterpret Casts.
Move heavy math (matrices, quaternions, geometry transformation) to `IJobParallelFor` + `[BurstCompile]`.
Offload texture DXT software-decoding to Burst Jobs.

## Target Architecture

1. **VFS Read**: `byte[]` -> wrap in `NativeArray<byte>`.
2. **Chunk Header Scan**: Fast pass over `NativeArray`, store offsets.
3. **Zero-Alloc Structs**: Replace `class` structures with `struct` and `[StructLayout(LayoutKind.Sequential, Pack = 1)]`.
4. **Reinterpret**: `UnsafeUtility.AsRef<T>` or `Reinterpret<T>` to view chunk bytes directly as structs. No `new`.
5. **Burst Jobs**: Pass NativeArrays to Jobs. Jobs do scale/basis math. Outputs direct to `Mesh.SetVertexBufferData` / `Mesh.SetIndexBufferData`.

## Phase Board

- [ ] Phase 1: Struct Blittability + Unsafe Wrapper
- [ ] Phase 2: CGF Geometry Jobs (Vertices / Faces / UVs)
- [ ] Phase 3: CAF Animation Jobs (Position / Rotation Log)
- [ ] Phase 4: Texture DXT Decoder Jobs
- [ ] Phase 5: Testing & Cleanup

## Detailed Plan

### Phase 1. Struct Blittability

Need data layout parity with CryEngine binary chunks.

Tasks:
- [ ] Convert `CryVertex`, `CryFace`, `CryUV`, `CryTexFace` in `CgfData.cs` from `class`/managed to strict `struct`.
- [ ] Add `[StructLayout(LayoutKind.Sequential, Pack = 1)]` (or Pack=4 depending on CryEngine padding).
- [ ] Create `BinaryBufferReader` struct wrapping `NativeArray<byte>` or `byte*` for safe offset-based reading without allocations.
- [ ] Update `CgfParser.cs` to use the new memory view approach for simple chunks (Nodes, Headers).

### Phase 2. CGF Geometry Jobs

Move mesh building off main thread, zero allocation.

Tasks:
- [ ] In `CgfParser.ReadMesh`, instead of `r.ReadSingle()` loop, use `UnsafeUtility` to cast byte slice to `NativeArray<CryVertex>`.
- [ ] Update `CgfMeshBuilder.cs`. Skip intermediate `List<Vector3>`.
- [ ] Create `CgfGeometryTransformJob`: takes `NativeArray<CryVertex>`, applies `NodeMatrixInImporterSpace`, writes to `NativeArray<float3>`.
- [ ] Direct upload: use `Mesh.SetVertexBufferParams` and `Mesh.SetVertexBufferData`. Skip `Mesh.SetVertices(List)`.

### Phase 3. CAF Animation Jobs

CAF parsing is CPU heavy due to `Math.Sin`/`Sqrt` per frame.

Tasks:
- [ ] `CafParser.cs`: read Controller track payloads as `NativeArray<byte>`.
- [ ] Create `CafRotationJob`: parses RotationLog `Vector3` -> `Quaternion` (Burst-compiled math).
- [ ] Create `CafPositionJob`: handles scale and basis conversion.
- [ ] Change `CafControllerTrack` to hold `NativeArray<float3>` / `NativeArray<quaternion>` instead of managed arrays.
- [ ] Ensure `CgfClipBuilder` can read from NativeArrays to build `AnimationClip` curves.

### Phase 4. Texture DXT Decoder Jobs

Software decoding of DXT1/3/5 is currently on ThreadPool but not Burst compiled.

Tasks:
- [ ] `DdsRuntimeDecoder.cs`: Extract `TryDecodeBc1`, `TryDecodeBc2` (DXT3), `TryDecodeBc3` (DXT5) loops.
- [ ] Write `Bc1DecompressJob`, `Bc2DecompressJob`, `Bc3DecompressJob` using `IJobParallelFor`.
- [ ] (Reference existing `Bc4DecompressJob` and `Bc5DecompressJob` in `DdsDecompressJobs.cs`).
- [ ] Ensure output is `NativeArray<Color32>` mapped directly to `Texture2D.SetPixelData`.

### Phase 5. Testing & Validation

Tasks:
- [ ] Run `CgfMeshBuilderTests` to ensure geometry identical.
- [ ] Run `DdsRuntimeDecoderTests` to verify texture colors.
- [ ] Load dense level (e.g. Training/Fort) and check RAM usage + CPU Profiler.

## Risks

- `Pack = 1` vs `Pack = 4`. CryEngine structs often have implicit compiler padding. Raw memory casts will break if padding is wrong. Must compare `sizeof(struct)` with chunk payload sizes.
- Endianness. x86 is little-endian. Unity NativeArrays are native endian. Safe for PC/Android, but must document.
- Memory Leaks. `NativeArray` must be disposed. Need strict ownership rules in `CgfFile` / `CafFile` lifecycle (or immediate conversion/disposal during parse).
