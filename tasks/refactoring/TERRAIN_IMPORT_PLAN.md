# Terrain Import Plan

## Цель

Воспроизвести двухслойный рендер террейна Far Cry 1 в Unity 6.4 URP:
- **cover_low.dds** — глобальная подложка (CLAMP, тайлится 1×1 по всему острову)
- **detail layers** (0-6) — детальные текстуры по surface type из `land_map.h16`, тайлятся по `DetailScaleX/Y` из `<SurfaceTypes>` в `leveldata.xml`

## Текущее состояние

`BuildUnityTerrain` в `FcLevelSceneBuilder.cs`:
- ✅ Читает `land_map.h16` → высоты → `TerrainData.SetHeights`
- ✅ Грузит `cover_low.dds` → единственный `TerrainLayer` (tileSize = worldSize)
- ❌ Не читает surface type из h16 (биты 0-2)
- ❌ Не парсит `<SurfaceTypes>` из `leveldata.xml`
- ❌ Нет сплатмапов → unity terrain рисует только cover_low без detail layers

## Формат h16 (из terrain.h)

```
uint16 per cell:
  bits [0-2] = STYPE_BIT_MASK  → surface type ID (0-6; 7 = STYPE_HOLE = дыра)
  bit  [3]   = LIGHT_BIT_MASK  → shadow bit
  bit  [4]   = reserved
  bits [5-15] = height         → (value & 0xFFE0) / 65536f = normalized height
```

## Формат leveldata.xml (из 3dEngineLoad.cpp)

```xml
<SurfaceTypes>
  <SurfaceType Name="mat_default"
               DetailTexture="terrain/detail0.dds"
               DetailScaleX="8" DetailScaleY="8"
               ProjAxis="Z" />
  <!-- до 7 записей, индекс = порядковый номер от 0 -->
</SurfaceTypes>
```

## Архитектура рендера оригинала

1. **Pass 1** — весь террейн с `cover_low.dds` (UV = worldXZ / terrainSize, CLAMP)
2. **Pass 2** — per-cell detail texture (UV = worldXZ * scaleX/Y, REPEAT) с vertex-alpha blend на границах

В Unity воспроизводим через стандартную систему сплатмапов:
- `TerrainLayer[0]` = cover_low (tileSize = worldSize×worldSize) — вес 0..1 (base fallback)
- `TerrainLayer[1..7]` = detail types 0-6 (tileSize = worldSize/scaleX × worldSize/scaleY) — вес из surface type map
- Alphamap: на каждом пикселе сумма весов = 1; detail weight = 0.85, cover = 0.15

---

## Plan

### Phase 1 — Data pipeline

**Step 1.1** — `FcTerrainLayerDesc` + extend `TryLoadTerrainSettings`

Файлы:
- `Assets/Scripts/Level/Data/FcLevelData.cs` — добавить `FcTerrainLayerDesc`
- `Assets/Scripts/Level/Data/FcLevelLoader.cs` — расширить `TryLoadTerrainSettings` → возвращает `List<FcTerrainLayerDesc>`

```csharp
public sealed class FcTerrainLayerDesc
{
    public byte   SurfaceTypeId;      // 0-6
    public string DetailTexturePath;  // VFS: "terrain/detail0.dds"
    public float  ScaleX;             // from DetailScaleX
    public float  ScaleY;             // from DetailScaleY
    public char   ProjAxis;           // 'Z' (usually)
}
```

Сигнатура:
```csharp
public static bool TryLoadTerrainSettings(
    string levelName,
    out int heightmapSize,
    out int heightmapUnitSize,
    out List<FcTerrainLayerDesc> surfaceLayers)
```

**Step 1.2** — `FcTerrainHeightmapDecoder.DecodeSurfaceTypes`

Файл: `Assets/Scripts/Level/Data/FcTerrainHeightmapDecoder.cs`

```csharp
// Extracts lower 3 bits (STYPE_BIT_MASK) from each h16 sample.
// Returns byte[] length = resolution*resolution; value 0-6 (7 = hole).
public static byte[] DecodeSurfaceTypes(ushort[] samples)
```

**Step 1.3** — `FcTerrainSplatmapBuilder` (новый файл)

Файл: `Assets/Scripts/Level/Data/FcTerrainSplatmapBuilder.cs`

Входные данные:
- `byte[] surfaceTypeIds` (resolution×resolution, row-major [z*res+x])
- `int resolution` (1024)
- `int layerCount` (= surfaceLayers.Count, ≤7)

Выход: `float[,,] alphamap` shape [splatRes, splatRes, layerCount+1]

Алгоритм:
```
splatRes = resolution (или 512 для экономии VRAM)
alphamap[row, col, 0]      = 0.15f        // cover_low base weight
alphamap[row, col, id+1]   = 0.85f        // detail weight for this surface type
// 2px box blur по границам (optional, воспроизводит vertex-alpha blend оригинала)
```

**Step 1.4** — Extend `BuildUnityTerrain` in `FcLevelSceneBuilder.cs`

- `TryLoadTerrainSettings` → also `surfaceLayers`
- Для каждого `FcTerrainLayerDesc`: загрузить detail texture, создать `TerrainLayer` asset с `tileSize = (worldSize/scaleX, worldSize/scaleY)`
- `terrainData.terrainLayers` = [cover] + [layers 0..N]
- Декодировать surface types из h16 raw bytes → `FcTerrainSplatmapBuilder.Build` → `terrainData.SetAlphamaps`
- Сохранить TerrainLayer assets в `Assets/FCData/Levels/{levelName}/`

---

### Phase 2 — Custom Terrain Shader Graph (опционально)

Если стандартный URP terrain shader даёт неприемлемое смешивание, добавить кастомный Terrain Shader Graph:

- `Assets/Shaders/FcTerrainLit.shadergraph` — Custom Terrain Shader (URP)
- Логика: sample cover_low @ global UV (всегда), lerp с detail @ tiled UV, distance fade
- Использует те же TerrainLayer и alphamap данные

Откладываем до визуальной проверки Phase 1.

---

## Files changed / created

| File | Action |
|------|--------|
| `Assets/Scripts/Level/Data/FcLevelData.cs` | add `FcTerrainLayerDesc` |
| `Assets/Scripts/Level/Data/FcLevelLoader.cs` | extend `TryLoadTerrainSettings` |
| `Assets/Scripts/Level/Data/FcTerrainHeightmapDecoder.cs` | add `DecodeSurfaceTypes` |
| `Assets/Scripts/Level/Data/FcTerrainSplatmapBuilder.cs` | new |
| `Assets/Scripts/Level/Editor/FcLevelSceneBuilder.cs` | extend `BuildUnityTerrain` |

---

## Status

- [x] 1.1 FcTerrainLayerDesc + TryLoadTerrainSettings
- [x] 1.2 DecodeSurfaceTypes
- [x] 1.3 FcTerrainSplatmapBuilder
- [x] 1.4 BuildUnityTerrain extension
- [ ] 2.1 Custom Shader Graph (Phase 2)
