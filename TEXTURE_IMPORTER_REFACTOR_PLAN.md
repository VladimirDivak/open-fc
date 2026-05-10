# Texture Import & Caching — план рефакторинга и оптимизаций

## Контекст

Текущая реализация текстурного импорта полностью функциональна как pipeline (DDS decoder, cache, material binding),
но имеет несколько критических проблем: CPU-декодирование DXT блоков расходует время и память при загрузке
уровней, кеш не привязан к lifecycle уровня (нет eviction), ряд форматов не поддержан.

Audit report (2026-05-09) по 52 311 ссылкам из 8438 CGF-файлов подтверждает:
95% текстур успешно резолвятся, 58% — DXT1/DXT5 (нативно поддерживаются Unity без CPU decode),
109 ссылок на .jpg молча отбрасываются, 2 457 ссылок ведут к отсутствующим файлам.

Пользователь хочет использовать texture atlases per level и Compute Shader для заполнения атласов.
Все текстуры Far Cry 1 ≤ 1024×1024.

---

## Диагностика текущих проблем

### Критические

| # | Проблема | Место | Влияние |
|---|----------|-------|---------|
| C1 | CPU-декодирование DXT1/DXT5: полный decode Color32[] на каждый блок | DdsRuntimeDecoder.cs | Излишняя нагрузка CPU и GC для 58% текстур |
| C2 | Нет level-scoped eviction: Texture2D накапливаются без ограничений | TextureRuntimeCache.cs | Утечка памяти между уровнями |
| C3 | Дублирование cache: TextureImportService.SharedRuntimeService и CgfRuntimeImporter.SharedTextureService — два независимых кеша | TextureImportService.cs, CgfRuntimeImporter.cs | Бесполезное двойное хранение текстур в памяти |
| C4 | Синхронная загрузка блокирует main thread при decode | TextureRuntimeImportService.TryLoad() | Фреймдропы при loading |

### Средние

| # | Проблема | Место |
|---|----------|-------|
| M1 | JPG не поддержан: 109 ссылок игнорируются | TextureResourceImportService.cs |
| M2 | Normal/Specular/Opacity текстуры не загружаются (данные в CgfMaterialChunk есть) | CgfMaterialImportService.cs, CgfMaterialBuilder.cs |
| M3 | Additive blend mode парсируется но не применяется | CgfMaterialBuilder.cs |
| M4 | OpenFarCry.Importer.asmdef не зависит от UniTask — async невозможен без правки asmdef | asmdef |

---

## Блок 1 — Нативная загрузка DXT1/DXT5 + JPG (быстрая победа)

**Что делать:**

Unity нативно поддерживает TextureFormat.DXT1 и TextureFormat.DXT5. Для этих форматов
вместо CPU-декодирования достаточно `tex.LoadRawTextureData(compressedPayload)` + `tex.Apply()`.
Это устраняет decode для 28 592 текстур (~58%).

**Изменения в DdsRuntimeDecoder.cs:**

- Добавить `TryLoadNative(byte[] ddsBytes, options, out Texture2D, out hasAlpha, out formatTag, out error)`
- Парсить хедер → если DdsFormat.Dxt1 или DdsFormat.Dxt5 → создать Texture2D с нужным TextureFormat,
  вызвать `LoadRawTextureData(bytes, dataOffset, payloadLength)`, затем `Apply(updateMipmaps: options.GenerateMipmaps, makeNoLongerReadable: options.MarkNonReadable)`
- Вертикальный флип НЕ нужен — LoadRawTextureData принимает данные как есть из DDS

**Изменения в TextureRuntimeImportService.cs:**

- В ветке `.dds`: сначала пробовать `DdsRuntimeDecoder.TryLoadNative()`, при неудаче fallback на `TryDecode()`
- Добавить ветку `.jpg/.jpeg`: `ImageConversion.LoadImage(tex, bytes, markNonReadable)` — Unity built-in JPEG

**Изменения в TextureResourceImportService.cs:**

- Добавить `".jpg"` и `".jpeg"` в Extensions

**Изменения в TextureImportService.cs:**

- Обновить SupportedExtensions

**Критерии готовности:**

- DdsRuntimeDecoderTests: тесты нативного DXT1/DXT5 пути проходят
- Профайлер: нет `DdsRuntimeDecoder.DecodeBlock` вызовов для DXT1/DXT5 текстур
- Audit tool: 109 JPG-ссылок переходят из unsupported в resolved

**Сложность:** низкая (4–8 ч). **Риск:** mipChain при создании Texture2D должен совпадать с тем,
сколько mip-уровней реально в DDS payload, иначе LoadRawTextureData бросит exception.

---

## Блок 2 — Level-Scoped Memory + унификация кешей

**Что делать:**

Заменить плоский TextureRuntimeCache на TextureRuntimeScopedCache с ref-counting по аналогии
с CgfRuntimeAssetCache. Устранить дублирование двух независимых кешей.

**Новый файл** `Assets/Scripts/Importer/Texture/TextureRuntimeScopedCache.cs`:

```csharp
public sealed class TextureRuntimeScopedCache
{
    sealed class Entry
    {
        public Texture2D Texture;
        public LoadedTextureInfo Info;
        public int RefCount;
        public readonly HashSet<string> Scopes = new();
    }

    readonly Dictionary<string, Entry> _byPath = new();
    readonly object _sync = new();

    public bool TryRetain(string path, string scopeId, out Texture2D tex, out LoadedTextureInfo info);
    public void Store(string path, string scopeId, Texture2D tex, LoadedTextureInfo info);
    public void ReleaseLevelScope(string scopeId);  // decrements refcount для скопа
    public void TrimUnused();                        // Destroys записи с RefCount <= 0
    public TextureCacheStats GetStats();
}
```

**Изменения в TextureRuntimeImportService.cs:**

- Заменить TextureRuntimeCache на TextureRuntimeScopedCache
- Добавить `string scopeId` в TryLoad / TryLoadWithInfo

**Изменения в CgfRuntimeImporter.cs:**

- Убрать собственный SharedTextureService — использовать `TextureImportService.RuntimeService` (единый singleton)
- В `ReleaseLevelScope(string levelScopeId)` — дополнительно вызвать `TextureImportService.RuntimeService.ReleaseLevelScope(levelScopeId)`

**Изменения в FcLevelCacheService.cs:**

- В `OnDestroy()` — добавить `TextureImportService.ReleaseLevelScope(_levelScopeId)`

**Критерии готовности:**

- Memory Profiler: после анлоада уровня нет живых Texture2D от предыдущего уровня
- Нет NullRef при последовательной загрузке нескольких уровней

**Сложность:** средняя (1–2 дня).

---

## Блок 3 — Async Pipeline

**Что делать:**

CPU decode тяжёлых форматов (maskedRGB, DXT3, BC4/BC5) перенести в thread pool.
Создание Texture2D и Apply() остаются на main thread.

**Изменения в OpenFarCry.Importer.asmdef:**

- Добавить `"UniTask"` в references

**Изменения в TextureRuntimeImportService.cs:**

- Добавить `TryLoadWithInfoAsync(string path, string scopeId, CancellationToken ct)`
- Pipeline:
  - a. Cache check (main thread, sync)
  - b. Load bytes через `FcFileSystem.ReadAllBytesAsync()` (уже async/UniTask)
  - c. `await UniTask.SwitchToThreadPool()` — CPU decode (только для форматов требующих decode)
  - d. `await UniTask.SwitchToMainThread()` — `new Texture2D()` + `Apply()`
  - e. Cache store

**Изменения в FcMeshEntity.cs, FcBrushInstance.cs:**

- `Start()` → `async UniTaskVoid StartAsync()` с использованием async texture/CGF pipeline

**Критерии готовности:**

- Профайлер: CPU decode не блокирует main thread; работает в Unity Job System thread
- Загрузка уровня без фреймдропов >16ms во время decode

**Сложность:** средняя (2–3 дня). Строгое правило: Unity объекты нельзя создавать вне main thread.

---

## Блок 4 — Full Material Pipeline (Normal, Specular, Opacity, Additive)

**Что делать:**

Подключить три нереализованных текстурных слота и Additive blend mode.

**Изменения в CgfResolvedMaterialTextures** (struct в CgfMaterialImportService.cs):

```csharp
public readonly struct CgfResolvedMaterialTextures
{
    public readonly Texture2D BaseMap;
    public readonly Texture2D NormalMap;    // новое
    public readonly Texture2D SpecularMap;  // новое
    public readonly Texture2D OpacityMap;   // было null; теперь загружается
    // + соответствующие VirtualPath поля
}
```

**Изменения в CgfMaterialImportService.cs:**

- В `ResolveTextures()` загружать NormalTextureName с `LinearColorSpace = true`
- Загружать SpecularTextureName
- Загружать OpacityTextureName (было помечено "deferred MVP")
- Обновить cache key — включить пути всех четырёх слотов

**Изменения в CgfMaterialBuilder.cs:**

```csharp
static readonly int PropBumpMap   = Shader.PropertyToID("_BumpMap");
static readonly int PropBumpScale = Shader.PropertyToID("_BumpScale");

// В Build():
if (textures.NormalMap != null)
{
    mat.SetTexture(PropBumpMap, textures.NormalMap);
    mat.EnableKeyword("_NORMALMAP");
}
// Для Specular: включить _SPECULAR_SETUP keyword, установить _SpecGlossMap
// Для Additive:
if ((chunk.Flags & CgfMtlFlags.Additive) != 0)
    ApplyAdditiveBlendState(mat);
```

`ApplyAdditiveBlendState`: Surface=Transparent, ZWrite=Off, SrcBlend=SrcAlpha, DstBlend=One,
`_SURFACE_TYPE_TRANSPARENT` keyword, RenderType=Transparent, renderQueue=Transparent.

**Критерии готовности:**

- CgfMaterialTextureBindingTests: тест проверяет `_BumpMap`
- Additive материалы (стёкла, дым) визуально корректны в Play Mode

**Сложность:** низкая (1 день).

---

## Блок 5 — Texture Atlas System (per-level)

### Архитектура

Перед загрузкой уровня сканировать все модели (brush.lst + entities), собирать уникальные пути
текстур, паковать в атласы по формату. При анлоуде уровня — уничтожать атлас одной операцией.

**Разделение атласов по формату:**

| Атлас | TextureFormat Unity | Исходные форматы DDS |
|-------|---------------------|----------------------|
| DXT1Atlas | TextureFormat.DXT1 | dxt1 |
| DXT5Atlas | TextureFormat.DXT5 | dxt5, dxt3 (CPU decode → RGBA32 в RGBA-атлас на MVP) |
| RGBAAtlas | TextureFormat.RGBA32 | maskedRGB_24/32, dxt3, bc4, bc5, bmp, tga, jpg |

DXT1 и DXT5 нельзя смешивать в одном атласе (разная структура блоков).
Сжатые атласы заполняются через `Graphics.CopyTexture` (GPU-GPU copy без декодирования).
RGBA32 атлас заполняется через Compute Shader (см. Блок 6) или CPU fallback.

**Ограничение:** тайловые текстуры исключать из атласа.
Текстуры с путём `terrain/` или размером ≤128px — оставлять как отдельные Texture2D с Repeat wrap mode.

### UV Remap — рекомендуемый подход (MVP: модификация UV меша)

Для статичных брашей — после импорта CGF бейкать UV remap в меш:

```
uvFinal = uvOriginal * uvScale + uvOffset
```

где `uvScale = (texWidth/atlasWidth, texHeight/atlasHeight)`, `uvOffset` — позиция слота.
Меш становится atlas-specific, но это нормально для статичной геометрии уровня.

Финальный вариант (после MVP) — MaterialPropertyBlock с кастомным URP ShaderGraph
(`FcAtlasLit.shadergraph`), читающим `_AtlasUVOffset` (Vector4: xy=offset, zw=scale).

### Bin packing алгоритм — Skyline

Сортировка по убыванию высоты → Skyline (массив высот по x-позициям, для каждой текстуры
ищем позицию с минимальным y-fit). Блочное выравнивание для compressed: позиции кратны 4px.
Multi-atlas fallback: если текущий атлас заполнен — создавать следующий (DXT1Atlas_0, DXT1Atlas_1...).

### Новые файлы

| Файл | Описание |
|------|----------|
| `Assets/Scripts/Importer/Texture/Atlas/TextureAtlasBinPacker.cs` | Skyline packing, возвращает `Dictionary<string, AtlasSlot>` |
| `Assets/Scripts/Importer/Texture/Atlas/LevelTextureAtlas.cs` | Data class: DXT1Atlases[], DXT5Atlases[], RGBAAtlases[], EntryByPath dict, Dispose() |
| `Assets/Scripts/Importer/Texture/Atlas/LevelTexturePrescanner.cs` | Сканирование всех CGF в уровне, сбор уникальных texture paths |
| `Assets/Scripts/Importer/Texture/Atlas/LevelTextureAtlasBuilder.cs` | Orchestrator: pre-scan → bin pack → load → CopyTexture/CS dispatch |
| `Assets/Scripts/Level/Services/FcLevelTextureAtlasService.cs` | MonoBehaviour: lifecycle атласа, BuildAtlasAsync(), OnDestroy → Dispose |

### Интеграция с материальным pipeline

`CgfMaterialBuilder.Build()` получает опциональный `LevelTextureAtlas atlas`.
Если путь текстуры найден в атласе — использовать atlas texture + bake UV offset/scale в меш (MVP).

Cache key в CgfMaterialImportService должен включать `atlasId` чтобы не смешивать
атласные и нон-атласные материалы.

**Критерии готовности:**

- Уровень загружается с 2–4 atlas Texture2D вместо тысяч отдельных
- Memory Profiler: Texture Memory снижается на 15–30% (устранение per-texture Unity overhead)
- Unload уровня: 3–5 Destroy() вызовов вместо тысяч

**Сложность:** высокая (1–1.5 недели). Реализовывать после Блоков 1–3.

---

## Блок 6 — Compute Shader (atlas population + format conversion)

### Где нужен CS, а где нет

`Graphics.CopyTexture` (не CS) — идеален для DXT1→DXT1Atlas и DXT5→DXT5Atlas:
работает без декодирования, GPU-GPU copy, быстрее CS.

**Compute Shader нужен только для:**

1. RGB24 → RGBA32Atlas conversion (uncompressed, UAV запись в RWTexture2D\<float4\> поддерживается)
2. DXT3 → DXT5 block re-encode (BC форматы не поддерживают UAV, поэтому выход — decode DXT3 блоков → RGBA32Atlas)

**Важное ограничение:** Compressed texture forms (DXT1, DXT5) не поддерживают UAV.
CS не может писать напрямую в DXT1/DXT5 atlas. Только через `Graphics.CopyTexture`.

### Новые файлы

**`Assets/Resources/Shaders/AtlasRgbConvert.compute`:**

```hlsl
#pragma kernel RgbToRgba

Texture2D<float3> _SourceRGB;
RWTexture2D<float4> _TargetAtlas;
int2 _TargetOffset;
int2 _SourceSize;

[numthreads(8, 8, 1)]
void RgbToRgba(uint3 id : SV_DispatchThreadID)
{
    if (id.x >= (uint)_SourceSize.x || id.y >= (uint)_SourceSize.y) return;
    _TargetAtlas[id.xy + _TargetOffset] = float4(_SourceRGB[id.xy], 1.0);
}
```

**`Assets/Scripts/Importer/Texture/Atlas/TextureAtlasComputeHelper.cs`:**

- Фасад с проверкой `SystemInfo.supportsComputeShaders`
- CPU fallback: Color32[] копия блоками при отсутствии CS поддержки
- `FillRgbaSlot(ComputeShader cs, Texture2D src, Texture2D dstAtlas, Vector2Int offset)`

### Полная схема заполнения атласа

```
DXT1 текстура → LoadRawTextureData (DXT1) → Graphics.CopyTexture → DXT1Atlas[slotX, slotY]
DXT5 текстура → LoadRawTextureData (DXT5) → Graphics.CopyTexture → DXT5Atlas[slotX, slotY]
RGB24 текстура → LoadRawTextureData (RGB24) → CS dispatch RgbToRgba → RGBAAtlas[offset]
DXT3 текстура → DdsRuntimeDecoder.TryDecode() (CPU) → SetPixels32 → RGBAAtlas[offset]
```

**Критерии готовности:**

- GPU profiler: видны CS dispatches для RGB24 текстур
- Визуальный результат: атлас корректно отображает текстуры в Play Mode
- Fallback на CPU работает при `!SystemInfo.supportsComputeShaders`

**Сложность:** высокая (5–7 дней). Реализовывать строго после Блока 5.

---

## Порядок реализации

| Блок | Описание | Срок | Условие |
|------|----------|------|---------|
| 1 | DXT nativeload + JPG | ~1 день | сейчас |
| 4 | Normal/Spec/Opacity/Add | ~1 день | параллельно с 1 |
| 3 | Async pipeline | ~2–3 дня | после 1, asmdef правка |
| 2 | Level-scoped cache | ~1–2 дня | параллельно с 3 |
| 5 | Texture Atlas | ~1–1.5 недели | после 1–3 |
| 6 | Compute Shader | ~1 неделя | после 5 |

---

## Открытые вопросы перед реализацией

1. **DXT3 стратегия:** CPU decode → RGBA32 atlas (просто, работает сразу) или CS decode → DXT5 re-encode
   → DXT5 atlas (быстрее, меньше памяти)? Рекомендую CPU→RGBA32 для MVP.
2. **UV remap для Блока 5:** бейкать в меш (MVP, только static brushes, без кастомного шейдера) или
   MaterialPropertyBlock + ShaderGraph FcAtlasLit (финальный вариант)?
3. **Atlas size:** 4096×4096 с multi-atlas fallback или фиксированный размер под максимальное
   число текстур уровня? Нужна статистика сколько уникальных текстур в самом тяжёлом уровне.

---

## Затронутые файлы

**Модифицировать:**

- `Assets/Scripts/Importer/Texture/DdsRuntimeDecoder.cs`
- `Assets/Scripts/Importer/Texture/TextureRuntimeImportService.cs`
- `Assets/Scripts/Importer/Texture/TextureRuntimeCache.cs` (→ заменить на ScopedCache)
- `Assets/Scripts/Importer/Texture/TextureImportService.cs`
- `Assets/Scripts/Importer/Cgf/CgfMaterialBuilder.cs`
- `Assets/Scripts/Importer/Cgf/CgfMaterialImportService.cs`
- `Assets/Scripts/Importer/Cgf/CgfRuntimeImporter.cs`
