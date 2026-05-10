# CGF/CGA/CAL Importer — план рефакторинга и оптимизации

**Дата оценки:** 2026-05-09  
**Файлов проверено:** 28 (Cgf/), 6 (Texture/), 10 (Editor/), 6 (Tests/)

---

## Общая оценка

Реализация **хорошо структурирована** для своего масштаба: правильный runtime/editor split, ref-counted cache, layered animation cache, coordinate-system bake. Основных архитектурных ошибок нет.

Проблемы — **размер одного класса** (animation service), **аллокации в hot path** mesh builder'а, **отсутствие eviction** в texture cache, и **незавершённые части** (material slots, async pipeline).

---

## Приоритет 1 — Исправить сейчас

### 1.1 `CgfMeshBuilder`: аллокация массива на каждой грани

**Файл:** `CgfMeshBuilder.cs:119`

```csharp
// СЕЙЧАС — аллоцирует int[] на каждой из N граней:
int[] cornerOrder = { 0, 1, 2 };
for (int oi = 0; oi < cornerOrder.Length; oi++)
{
    int c = cornerOrder[oi];
    int pi = c == 0 ? p0 : (c == 1 ? p1 : p2);
    int ti = c == 0 ? t0 : (c == 1 ? t1 : t2);
```

**Исправление:** раскрыть в 3 прохода или использовать `ReadOnlySpan` на стеке:

```csharp
// Вариант A — простейший: раскрыть вручную
ProcessCorner(p0, t0, matID, ...);
ProcessCorner(p1, t1, matID, ...);
ProcessCorner(p2, t2, matID, ...);

// Вариант B — Span на стеке (нет heap-аллокации):
Span<(int pi, int ti)> corners = stackalloc (int, int)[] { (p0, t0), (p1, t1), (p2, t2) };
foreach (var (pi, ti) in corners) { ... }
```

Для модели с 10 000 граней это устраняет 10 000 GC-аллокаций за импорт.

---

### 1.2 `CgfMeshBuilder`: двойная сортировка bone links на вершину

**Файл:** `CgfMeshBuilder.cs:218` и `259`

`links.OrderByDescending(l => l.Blending).ToArray()` вызывается **дважды** для одних и тех же данных:
1. в `TryBuildBindPositionFromLinks` — чтобы взять top-4 для расчёта позиции;
2. в `BuildBoneWeights` — чтобы взять top-4 для Unity `BoneWeight`.

Для модели с 5 000 вершин и 4 link'ами каждая — 10 000 LINQ-сортировок.

**Исправление:** сортировать один раз в `BuildMesh` рядом с `BuildBoneWeights`, передавать уже отсортированный массив в обе функции:

```csharp
// Возвращать заодно sorted links из BuildBoneWeights, переиспользовать
// в TryBuildBindPositionFromLinks при построении позиции.
static CryLink[] SortLinks(CryLink[] links)
    => links.Length <= 1 ? links : links.OrderByDescending(l => l.Blending).ToArray();
```

Или использовать `Array.Sort` in-place (нет аллокации temp array).

---

### 1.3 `TextureRuntimeCache`: нет политики вытеснения

**Файл:** `TextureRuntimeCache.cs`

Кэш растёт неограниченно. При загрузке уровня Far Cry (300–600 уникальных текстур) и последующей загрузке другого уровня кэш не очищается. Объекты `Texture2D` — managed wrapper над нативной памятью GPU; их утечка — реальная проблема.

**Исправление:**
- Добавить `MaxEntries` (например 512) и LRU eviction по `LastAccessTime` — аналогично `CgfAnimationRuntimeImportService`.
- Или подключить кэш к системе scope-release `CgfRuntimeAssetCache` (в идеале — общий интерфейс `IScopedCache`).

Минимум: добавить `TrimToCapacity(int maxEntries)` по аналогии с animation cache.

---

### 1.4 Выделить `CalParser.cs`

**Файл:** `CgfAnimationRuntimeImportService.cs:1173` — метод `ParseCalEntries` (~60 строк)

CAL — отдельный текстовый формат (`.cal` файлы с `$AnimationDir` директивами). Его парсер живёт внутри 1975-строчного сервиса, хотя для `CafParser` уже есть отдельный файл.

**Исправление:** создать `CalParser.cs` в `Importer/Cgf/`:

```csharp
namespace OpenFarCry.Importer.Cgf
{
    public static class CalParser
    {
        public static List<CalAnimEntry> Parse(string calText, string modelDir) { ... }
    }

    public readonly struct CalAnimEntry
    {
        public readonly string Alias;
        public readonly string VirtualPath;
    }
}
```

Убрать `ParseCalEntries` из `CgfAnimationRuntimeImportService`.

---

## Приоритет 2 — Следующий шаг

### 2.1 Разбить `CgfAnimationRuntimeImportService` (1 975 строк)

Класс совмещает:
- **7 статических кэшей** + 11 счётчиков + LRU eviction;
- **CAF loading** (чтение байтов + парсинг через `CafParser`);
- **Semantic CAF dedup** (content hash → `CafFile`);
- **CAL discovery** (сейчас `ParseCalEntries` — вынесен в п.1.4);
- **Clip building** (controller tracks → `AnimationClip`);
- **Loop detection heuristics** (~80 строк);
- **Animation set construction** (fingerprint + layout);
- **Diagnostics report generation** (~200 строк).

Предлагаемое разбиение:

| Новый класс | Ответственность | Размер |
|---|---|---|
| `CalParser` | CAL текстовый парсинг | ~60 строк |
| `CafLoader` | path → bytes → `CafFile` (CAF path cache + semantic dedup) | ~200 строк |
| `CgfClipBuilder` | `CafFile` + bone map → `AnimationClip` + loop detection | ~300 строк |
| `CgfAnimationSetCache` | animation set construction, LRU, fingerprint matching | ~400 строк |
| `CgfAnimationDiagnostics` | отчёты, `RuntimeAttachDiagnosticsEntry` | ~250 строк |
| `CgfAnimationRuntimeImportService` | оркестровка, публичный API | ~200 строк |

Переход можно делать **постепенно** — сначала вынести `CalParser` (п.1.4), затем `CgfAnimationDiagnostics`, потом `CafLoader`.

---

### 2.2 `CgfMaterialBuilder`: заполнить оставшиеся material slots

**Файл:** `CgfMaterialBuilder.cs`

Сейчас применяется только `_BaseMap` (diffuse). В `CgfMaterialChunk` есть поля для:
- `BumpMap` → URP `_BumpMap` (нормалмап)
- `GlossMap` / `SpecularMap` → URP `_MetallicGlossMap` или `_SpecGlossMap`
- `OpacityMap` (помечено "MVP деferred" в `CgfMaterialImportService`)

Задача:
1. Расширить `CgfMaterialChunk` (или `CgfTexturePathResolver`) чтобы возвращать пути для bump/specular/opacity.
2. В `CgfMaterialBuilder.Build(...)` биндить найденные текстуры на соответствующие URP Lit свойства.
3. Снять пометку "MVP" с opacity workflow в `CgfMaterialImportService`.

---

### 2.3 `CgfLodImportService`: кэшировать Regex

**Файл:** `CgfLodImportService.cs:23`

```csharp
// Сейчас — new Regex на каждый вызов DiscoverLodSiblings:
string pattern = $"^{Regex.Escape(baseName)}_lod(\\d+)$";
var regex = new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
```

При загрузке уровня с 200 brush-инстансами — 200 allocations. Исправление:

```csharp
static readonly Dictionary<string, Regex> s_lodRegexCache = new(StringComparer.OrdinalIgnoreCase);

static Regex GetLodRegex(string baseName)
{
    if (!s_lodRegexCache.TryGetValue(baseName, out var rx))
        s_lodRegexCache[baseName] = rx = new Regex(
            $"^{Regex.Escape(baseName)}_lod(\\d+)$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);
    return rx;
}
```

---

### 2.4 `CgfRigSnapshotBuilder`: заменить конкатенацию строк на `StringBuilder`

**Файл:** `CgfRigSnapshotBuilder.cs`

Fingerprint строится через pipe-delimited конкатенацию для 60+ костей. Каждая итерация создаёт промежуточный `string`. Заменить на `StringBuilder.Append(...)` + `Hash128.Compute(bytes)` или `ComputeHash(sb.ToString())` без промежуточных строк.

---

### 2.5 Добавить тесты для `CgfMeshBuilder`

**Файл:** `Tests/Editor/` — тестов для mesh builder'а нет.

Приоритетные случаи:
- Нормализация bone weights (сумма = 1, top-4 из N линков).
- Vertex dedup по `(pi, ti)` — одинаковая позиция, разные UV → 2 вершины.
- `TryBuildBoneIndexMaps` с корректным BoneAnim деревом и с `entities.Length != boneCount` (fallback к identity).
- `BuildBindPoses` — проверить, что `bindpose[i] == inverse(globalBone[i])`.

---

## Приоритет 3 — Долгосрочно

### 3.1 Async runtime import path

Текущий `CgfRuntimeImportService.Import(...)` — синхронный. При загрузке уровня с 200 brush-инстансами каждый `FcFileSystem.ReadAllBytes` + `CgfParser.Parse` + `CgfMeshBuilder.Build` блокирует main thread.

`FcFileSystem.ReadAllBytesAsync(...)` уже существует (UniTask). Цель — перевести `CgfRuntimeImportService` и `CgfAnimationRuntimeImportService` на `async UniTask<T>` контракт:

```csharp
public UniTask<CgfRuntimeImportResult> ImportAsync(CgfRuntimeImportRequest request,
    CancellationToken ct = default);
```

Парсинг (`CgfParser.Parse`, `CafParser.Parse`) вынести в `UniTask.RunOnThreadPool(...)`, возвращаясь на main thread только для Unity API (Mesh, AnimationClip, Texture2D).

**Замечание:** изменение затрагивает все caller'ы — делать в отдельной ветке, не смешивать с текущими изменениями.

---

### 3.2 Общий интерфейс `IScopedCache<T>`

Сейчас `CgfRuntimeAssetCache` и `TextureRuntimeCache` — независимые классы с разной семантикой eviction. Текстурный кэш не интегрирован в систему scope-release level loading.

```csharp
public interface IScopedCache<T>
{
    T GetOrCreate(string key, Func<T> factory);
    void Retain(string key, string scopeTag);
    void ReleaseScope(string scopeTag);
    void TrimUnused();
    CacheStats GetStats();
}
```

Реализовать для `TextureRuntimeCache` и опционально для `CgfMaterialRuntimeCache`.

---

### 3.3 `.anm` pipeline (CGA)

В CLAUDE.md: «`.cga` treated as geometry preview; `.anm` animation pipeline not implemented».

По аналогии с CAF/CAL:
- `AnmParser.cs` — бинарный парсер `.anm` (controller chunks, аналогичные CAF).
- `CgaAnimationRuntimeImportService` или расширение существующего.

Это отдельная задача, но отметить что `CgfAnimationRuntimeImportService` предполагает наличие CAF — при добавлении ANM нужно будет расширить интерфейс источника анимации.

---

## Что НЕ трогать

| Файл/область | Причина |
|---|---|
| `DdsRuntimeDecoder.cs` (1 372 строки) | Монолитный, но корректный и покрытый тестами. Каждый блок-декодер — изолированный метод. Рефактор ухудшит читаемость без выгоды. |
| `CgfParser.cs` — versioned chunk branches | Format-driven complexity. Ветки для 0x0744/0x0745/0x0746 — не code smell, а требование формата. |
| `CafParser.cs` | Адекватный размер, логика чистая. |
| `TryBuildBoneIndexMaps` — local functions с closures | Выполняется один раз на импорт, не hot path. Closures здесь читаемее, чем out-параметры. |
| Static facades (`CgfRuntimeImporter`, `TextureImportService`) | Правильны для Unity single-instance контекста. DI избыточен без тестируемости как требования. |
| Coordinate conversion (`CryTransformConversion`) | Чувствительная область. CLAUDE.md: «если меняете — валидируйте mesh, brush, bind pose, animation вместе». Не трогать без необходимости. |

---

## Сводная таблица

| № | Файл | Проблема | Сложность | Приоритет |
|---|---|---|---|---|
| 1.1 | `CgfMeshBuilder.cs:119` | `int[] cornerOrder` per face | XS | P1 |
| 1.2 | `CgfMeshBuilder.cs:218,259` | Двойная сортировка links per vertex | S | P1 |
| 1.3 | `TextureRuntimeCache.cs` | Нет LRU eviction | S | P1 |
| 1.4 | `CgfAnimationRuntimeImportService.cs:1173` | Выделить `CalParser.cs` | S | P1 |
| 2.1 | `CgfAnimationRuntimeImportService.cs` | 1975 строк → разбить | L | P2 |
| 2.2 | `CgfMaterialBuilder.cs` | Только diffuse; normal/specular отсутствуют | M | P2 |
| 2.3 | `CgfLodImportService.cs:23` | Regex allocation per call | XS | P2 |
| 2.4 | `CgfRigSnapshotBuilder.cs` | String concat в fingerprint | XS | P2 |
| 2.5 | `Tests/Editor/` | Нет тестов для mesh builder | M | P2 |
| 3.1 | `CgfRuntimeImportService.cs` | Sync import блокирует main thread | XL | P3 |
| 3.2 | `TextureRuntimeCache` / `CgfMaterialRuntimeCache` | Нет `IScopedCache` интеграции | M | P3 |
| 3.3 | — | `.anm` pipeline (CGA) | XL | P3 |
