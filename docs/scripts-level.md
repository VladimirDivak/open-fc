# Scripts: Level

Level assembly: `OpenFarCry.Level`. Scene construction and streaming.

### Loading

- **FcLevelLoader.cs**: Parses `mission_*.xml`, `leveldata.xml`, `.cry` editor lights. Coordinate basis bridge.
- **FcBrushLoader.cs**: Binary `brush.lst` reader.
- **FcLevelLoadService.cs**: Main facade. Preload planner. Async level kickoff. Spawns scene root.
- **FcLevelGeometryPreloadPlanner.cs**: Analyzes brush/veg lists. Groups requests for IO efficiency.

### Services (Runtime)

- **FcBrushLoadService.cs**: Distance-sorted brush loading. Limits concurrency.
- **FcEntityLoadService.cs**: Priority entity queue (Characters > Critical > Background).
- **FcVegetationTerrainService.cs**: High-perf GPU instancing. Spatial partitioning (Cells). Runtime colliders.
- **FcLevelMaterialOverrideService.cs**: `materials.xml` resolver. Replaces CGF materials with level-specific ones.
- **FcLevelEnvironment.cs**: Sun/Fog/Ambient controller. Syncs with Mission XML.

### Entities

- **FcEntity.cs**: Base. Stores XML properties.
- **FcMeshEntity.cs**: Basic static model with LOD support.
- **FcCharacterEntity.cs**: Skeleton, Anim, Ragdoll support.
- **FcBrushInstance.cs**: Static world geometry placeholder.
- **FcLightEntity / FcSoundEntity**: Dynamic light / SoundSpot wrappers.

### Volumes

- **FcVolumeObjects.cs**: `VisArea`, `Portal`, `OccluderArea`, `FogVolume`, `WaterVolume`. Polygon stubs with editor gizmos.
- **FcMovieSequencePlaceholder.cs**: `moviedata.xml` metadata marker.
