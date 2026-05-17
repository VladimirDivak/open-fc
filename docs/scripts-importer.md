# Scripts: Importer

Importer assembly: `OpenFarCry.Importer`. Binary assets to Unity objects.

### CGF / CGA (Geometry)

- **CgfParser.cs**: Reads chunks (Mesh, Node, Mtl, BoneAnim, BoneInitPos).
- **CgfMeshBuilder.cs**: Builds `Mesh`. Combines nodes. Vertex deduplication. High performance with Burst/Jobs.
- **CgfRuntimeImportService.cs**: Runtime import orchestrator. Shared sync/async path. In-flight request coalescing. Bounded-concurrency `PreloadAsync` fan-out.
- **CryTransformConversion.cs**: Asset basis: Cry `(x,y,z)` -> Unity Importer `(x,z,-y)`.
- **CgfSkeletonBuilder.cs**: Rebuilds bone hierarchy. Maps ControllerID to Unity `Transform`.
- **CgfLodImportService.cs**: Finds sibling `_lodN.cgf`. Configures `LODGroup`.

### Texture & Material

- **DdsRuntimeDecoder.cs**: Fast DDS reader. BC1/BC3/BC4/BC5. Normal map support.
- **CgfMaterialClassifier.cs**: Logic core. Shader name + Flags -> Material family.
- **CgfMaterialBuilder.cs**: URP Lit material factory. Sets Blend/Cutout/Spec.
- **TextureRuntimeImportService.cs**: Decodes DDS/TGA/BMP/JPG. Runtime memory cache.

### Animation & Physics

- **CgfAnimationRuntimeImportService.cs**: CAF attachment. Semantic clip cache. Dedup across rigs.
- **CafParser.cs**: Reads CAF binary. Tracks positions/rotations.
- **CafLoader.cs**: Two-level CAF cache (path + content-hash semantic dedup). Path hits skip I/O and hashing.
- **CgfRagdollBuilder.cs**: Auto-builds `BoxCollider` and `ConfigurableJoint` from BoneMesh data.
- **FcRagdollController.cs**: Runtime toggle: Animated <-> Ragdoll.

### Cache

- **CgfRuntimeAssetCache.cs**: Ref-counted CGF files and Meshes. Level scope holds one ref per key; `ReleaseLevelScope` force-frees all scoped keys.
- **CgfCacheKeys.cs**: Single source for all CGF/CAF/animation cache keys.
- **CgfMaterialRuntimeCache.cs**: Reuses materials with same keys (shader + textures).
