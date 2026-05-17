# Scripts: Importer

Importer assembly: `OpenFarCry.Importer`. Binary assets to Unity objects.

### CGF / CGA (Geometry)

- **CgfParser.cs**: Reads chunks (Mesh, Node, Mtl, BoneAnim, BoneInitPos).
- **CgfMeshBuilder.cs**: Builds `Mesh`. Combines nodes. Vertex deduplication. High performance with Burst/Jobs.
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
- **CafParser.cs / CafLoader.cs**: Reads CAF binary. Tracks positions/rotations.
- **CgfRagdollBuilder.cs**: Auto-builds `BoxCollider` and `ConfigurableJoint` from BoneMesh data.
- **FcRagdollController.cs**: Runtime toggle: Animated <-> Ragdoll.

### Cache

- **CgfRuntimeAssetCache.cs**: Ref-counted CGF files and Meshes. Release per level scope.
- **CgfMaterialRuntimeCache.cs**: Reuses materials with same keys (shader + textures).
