# Terrain Import Notes (Far Cry 1 -> Unity)

Snapshot: 2026-05-12

## Source Truth

- Cry runtime height decode:
  - `height = (sample & ~INFO_BITS_MASK) * TERRAIN_Z_RATIO`
  - `INFO_BITS_MASK = 31` (low 5 bits are flags)
  - `TERRAIN_Z_RATIO = 1 / 256`
- Source refs:
  - `Cry3DEngine/terrain.h`
  - `Cry3DEngine/terrain_hmap.cpp`
  - `Cry3DEngine/terrain_load.cpp`
  - `Editor/GameExporter.cpp`
  - `Editor/Heightmap.cpp`

## Export/Runtime Facts

- `terrain/land_map.h16` exported from editor as `ftoi(height * 256)`.
- `LevelInfo` provides:
  - `HeightmapSize`
  - `HeightmapUnitSize`
  - `WaterLevel`
- Training level values:
  - `HeightmapSize=1024`
  - `HeightmapUnitSize=2`
  - `WaterLevel=16`

## OpenFarCry Implementation (Current)

- Terrain built as chunked mesh (63x63 quads/chunk).
- Height sampling uses Cry-compatible rules:
  - transpose mapping for world `x/y` vs stored hmap layout;
  - border handling for index-0 row/column;
  - low 5 bits masked out before height conversion.
- Height conversion:
  - `worldY = (raw & 0xFFE0) * (1f / 256f)`
- Horizontal step:
  - `metersPerSample = HeightmapUnitSize` from `leveldata.xml`
- Water plane Y:
  - `WaterLevel` from `leveldata.xml`

## Material Fallback

- `terrain/cover_low.dds` used as first fallback texture.
- If texture load fails, plain color fallback material used.
- UV mapping kept aligned with transposed terrain sampling to avoid rotated look.

## Validation Criteria

- Terrain height matches entities/brush anchors in key points.
- Terrain XY footprint matches mission object ranges (no x100 drift).
- Water plane aligns with coast transitions at expected level.
- Fallback texture orientation matches world orientation.
