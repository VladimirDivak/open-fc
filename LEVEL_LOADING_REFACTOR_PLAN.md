# Level Loading Refactor Plan

Дата среза: 2026-05-10.

## Цель

Перестроить загрузку уровня так, чтобы:

- entity-компоненты не загружали ресурсы сами;
- загрузка шла через централизованные сервисы;
- тяжёлые этапы максимально использовали async/thread-pool там, где это безопасно;
- кеши mesh/animation/material работали предсказуемо и масштабировались на большие уровни;
- уровень имел прозрачную диагностику по времени загрузки и cache hit/miss поведению.

---

## Состояние на 2026-05-10

### Что уже сделано

| Компонент | Статус |
|-----------|--------|
| `FcBrushLoadService` | **DONE** — async, distance-sorted, concurrency-limited (`_maxConcurrent`, `_loadsPerFrame`) |
| `FcBrushInstance.ApplyLoadResult` | **DONE** — регистрируется в сервисе, не self-loading |
| `FcLevelCacheService` | **DONE** — scope ownership, `ReleaseLevelScope` + `TrimUnused` on destroy |
| `FcLevelResourceService` | есть — тонкая обёртка над sync `CgfRuntimeImporter.Import` |
| `FcLevelLayoutData` | есть — сериализует mission + brush layout в ScriptableObject |

### Что ещё self-loading (нужна Phase 2)

- `FcMeshEntity.Start()` — sync `service.ImportCgf(...)` на main thread
- `FcCharacterEntity.Start()` — sync `service.ImportCgf(...)` + sync `TryAttachAnimations` на main thread

### Что отсутствует полностью

- `FcLevelLoadReport` — timing accumulator, нет нигде
- `FcLevelLoadService` — главный orchestrator уровня
- `FcEntityLoadService` — priority queue + load state tracking
- `FcMeshLoadService` — async CGF pipeline для entity (отдельно от `FcBrushLoadService`)
- `FcAnimationLoadService` — async CAL/CAF pipeline

### Что нужно уточнить по farcry-sources/docs

- `docs/unity-entities.md` — иерархия entity-классов (`FarCryEntity` → NPC/Vehicle/Weapon/…); при создании `FcLevelLoadService` надо учитывать, что загрузка entity должна знать о типах сущностей для приоритетизации.
- `docs/materials.md` — shader/texture mapping (топ-16 шейдеров, структура MTL-чанка); влияет на `FcMeshLoadService.ResolveMaterials` шаг.

---

## Проблемы текущего состояния

### 1. Self-loading у entity

Сейчас `FcMeshEntity` и `FcCharacterEntity` сами начинают загрузку в `Start()`.

Следствия:

- orchestration размазан по компонентам;
- невозможно централизованно ограничить concurrency;
- нет общей очереди загрузки и приоритетов;
- сложно делать staged loading уровня;
- сложно агрегировать timing/reporting.

### 2. Загрузка в основном синхронная

`FcMeshEntity` и `FcCharacterEntity` вызывают `service.ImportCgf(...)` синхронно на main thread.

Хотя `FcBrushLoadService` уже перешёл на async `CgfRuntimeImporter.ImportAsync`, entity-компоненты пока нет.

### 3. Анимационный кеш всё ещё ненадёжен

Нужно окончательно разделить:

- кеш исходных CAF-данных;
- кеш построенных `AnimationClip`;
- сигнатуру совместимости controller mapping;
- semantic clip reuse между разными моделями.

### 4. Нет нормального timing breakdown по уровню

Нет системного breakdown ни по editor build, ни по runtime load.

---

## Целевая архитектура

```text
Level Scene / Mission Data
  -> FcLevelLoadService
  -> FcEntityLoadService
      -> FcMeshLoadService
      -> FcAnimationLoadService
      -> FcBrushLoadService  ← уже реализован
  -> apply loaded state to placeholders
```

### Принцип

Placeholders хранят только данные и состояние применения.

Сервисы:

- решают что загружать;
- когда загружать;
- в каком порядке загружать;
- откуда брать данные;
- как кешировать артефакты;
- как освобождать ресурсы на unload.

---

## Целевые сервисы

### FcLevelLoadService

Главный orchestration service уровня.

Ответственность:

- жизненный цикл загрузки уровня;
- mount level PAK;
- parse mission/brush data;
- разбиение на critical/deferred batches;
- запуск и остановка загрузки;
- финальный timing/report.

Публичное API:

- `LoadLevelAsync(levelName, missionName, cancellationToken)`
- `UnloadLevelAsync()`
- `GetLoadReport()`

### FcEntityLoadService

Сервис, который принимает placeholders и превращает их в `LoadRequest`.

Ответственность:

- priority queue;
- bounded concurrency;
- load state tracking;
- retries/failures/cancellation;
- агрегирование per-entity stats.

Публичное API:

- `Enqueue(FcEntity entity, EntityLoadPriority priority)`
- `EnqueueRange(...)`
- `CancelAllForScope(levelScopeId)`

### FcMeshLoadService

Сервис CGF/CGA mesh/model pipeline.

Ответственность:

- чтение исходных bytes;
- parse CGF/CGA;
- cache parsed/build artifacts;
- material resolution;
- LOD preparation;
- collider source selection;
- visual/proxy filtering.

Результат:

- `LoadedMeshArtifact`
  - parsed file
  - build result
  - root visual spec
  - collider mesh or collider source policy
  - model cache keys

### FcAnimationLoadService

Сервис CAL/CAF/clip pipeline.

Ответственность:

- CAL discovery;
- CAF discovery fallback;
- CAF parsed cache;
- semantic clip cache;
- attach/apply to runtime target.

Результат:

- `LoadedAnimationArtifact`
  - clip list
  - default clip
  - cache stats
  - warning summary

### FcBrushLoadService ← уже реализован

Async loader для brush-ориентированного runtime path.

Реализовано:

- batching, distance-sort, shared mesh cache (через `CgfRuntimeImporter`);
- async import + texture preload;
- concurrency limits (`_maxConcurrent`, `_loadsPerFrame`).

Нужно добавить:

- timing instrumentation (Phase 1);
- интеграция с `FcLevelLoadReport`.

---

## Что должно остаться в entity-компонентах

Entity-компоненты больше не должны делать import сами.

Они должны:

- хранить source data;
- регистрироваться в `FcEntityLoadService`;
- уметь применить результат;
- уметь снять результат при unload.

### Пример целевого API

`FcMeshEntity`:

- `CreateLoadRequest()`
- `ApplyLoadedMesh(LoadedMeshArtifact artifact)`
- `ReleaseLoadedMesh()`

`FcCharacterEntity`:

- `CreateLoadRequest()`
- `ApplyLoadedCharacter(LoadedMeshArtifact mesh, LoadedAnimationArtifact anim)`
- `ReleaseLoadedCharacter()`

`FcBrushInstance` ← уже реализован:

- `Register(this)` в `Start()`
- `ApplyLoadResult(CgfRuntimeImportResult result, string levelScopeId)`

---

## Async Pipeline

### Общий принцип

Каждый load request должен быть разложен на этапы:

1. `Read source bytes`
2. `Parse source data`
3. `Build Unity artifacts`
4. `Apply to scene object`

### Что можно делать off-main-thread

- `FcFileSystem.ReadAllBytesAsync(...)`
- XML/CGF/CAF binary parsing, если код не трогает Unity API
- вычисление хешей/сигнатур
- сборка промежуточных data-only structures

### Что должно оставаться на main thread

- создание `GameObject`
- создание/назначение `Mesh`, `Material`, `AnimationClip`
- `MeshCollider`, `Renderer`, `Animation` component apply
- любые Unity object lifecycle операции

### Concurrency policy

Нужны отдельные лимиты:

- `MaxConcurrentReads`
- `MaxConcurrentParses`
- `MaxConcurrentApplies`

Также нужен priority scheduling:

- `Critical`
- `NearCamera`
- `Characters`
- `GameplayRelevant`
- `Background`
- `FarBrushes`

---

## Кеши

### Разделение кешей

Нужно хранить отдельно:

- `SourceBytesCache`
- `ParsedCgfCache`
- `BuiltModelCache`
- `ParsedCafCache`
- `BuiltAnimationClipCache`
- `MaterialCache`

### Animation cache policy

Анимационный кеш должен быть двухуровневым.

#### CAF parsed cache key

Основа:

- `virtualPath`
- fingerprint исходных bytes или source version

#### Built clip cache key

Основа:

- semantic CAF content signature
- clip alias
- import scale
- controller mapping signature
- loop policy / import options

### Level scope ownership

Все кеши, которые удерживаются уровнем, должны поддерживать:

- retain/release;
- scope ownership;
- `ReleaseLevelScope(levelScopeId)`;
- trim unused.

---

## Level Loading Flow

Целевой flow:

```text
LoadLevelAsync
  -> mount level pak
  -> parse mission xml
  -> parse brush data
  -> create load requests
  -> classify requests by priority
  -> warm critical set
  -> stream deferred set
  -> emit load report
```

### Critical set

- player-near entities;
- gameplay-critical characters;
- важные triggers/spawn points;
- ближайшие visible brushes/props.

### Deferred set

- дальние props;
- дальние brushes;
- второстепенные characters;
- expensive animation warmups.

---

## Инструментация и метрики

### Editor level build

Логировать отдельно:

- `LoadMissionXml`
- `LoadBrushList`
- `SaveLayoutData`
- `BuildEntities`
- `BuildBrushPlaceholders`
- `TotalEditorBuild`

### Runtime level loading

Логировать отдельно:

- `ReadBytes`
- `ParseCgf`
- `BuildMesh`
- `ResolveMaterials`
- `BuildCollider`
- `ParseCal`
- `ParseCaf`
- `BuildAnimationClip`
- `AttachAnimation`
- `ConfigureLod`
- `ApplyToSceneObject`

### Итоговый report (runtime)

- total wall time;
- total main-thread time;
- average per entity class;
- top slowest assets;
- cache hit/miss ratios;
- queue wait time;
- active concurrency peaks.

---

## Порядок внедрения

### Phase 1. Instrumentation — **STARTED 2026-05-10**

Добавить timing без изменения архитектуры:

- [x] `FcLevelLoadReport` — accumulator для timing entries
- [x] `FcLevelSceneBuilder` — timing по editor build phases
- [x] `FcBrushLoadService` — per-load timing + aggregate report

### Phase 2. Remove self-loading

Убрать `Start() -> ImportCgf()` из:

- `FcMeshEntity` — заменить на регистрацию в `FcEntityLoadService`
- `FcCharacterEntity` — заменить на регистрацию в `FcEntityLoadService`

Потребует создания `FcEntityLoadService` + `FcMeshLoadService`.

### Phase 3. Introduce async queue

Ввести:

- request queue;
- concurrency limits;
- cancellation;
- priority scheduling.

### Phase 4. Split mesh/animation services

Выделить:

- `FcMeshLoadService`
- `FcAnimationLoadService`

И перевести entity apply на результаты сервисов.

### Phase 5. Stabilize caches

Пересобрать cache policy для:

- parsed CGF;
- parsed CAF;
- built model;
- built clip;
- materials.

### Phase 6. Streaming and polish

Добавить:

- deferred/background loading;
- near-camera prioritization;
- warmup policies;
- unload transitions;
- stress diagnostics.

---

## Изменения по существующим файлам

### Нужно переработать

- `Assets/Scripts/Level/Services/FcLevelResourceService.cs` — сейчас только sync wrapper
- `Assets/Scripts/Level/Entities/FcMeshEntity.cs` — убрать self-loading из `Start()`
- `Assets/Scripts/Level/Entities/FcCharacterEntity.cs` — убрать self-loading из `Start()`
- `Assets/Scripts/Importer/Cgf/CgfAnimationRuntimeImportService.cs` — стабилизировать cache policy

### Нужно добавить

- `Assets/Scripts/Level/Services/FcLevelLoadReport.cs` ← Phase 1 ✓
- `Assets/Scripts/Level/Services/FcLevelLoadService.cs`
- `Assets/Scripts/Level/Services/FcEntityLoadService.cs`
- `Assets/Scripts/Level/Services/FcMeshLoadService.cs`
- `Assets/Scripts/Level/Services/FcAnimationLoadService.cs`
- DTO/Request/Artifact types для pipeline.

### Уже есть (не добавлять повторно)

- `Assets/Scripts/Level/Services/FcBrushLoadService.cs` ← готов
- `Assets/Scripts/Level/Services/FcLevelCacheService.cs` ← готов
- `Assets/Scripts/Level/Services/FcLevelResourceService.cs` ← частично
- `Assets/Scripts/Level/Services/FcLevelEnvironment.cs` ← готов

---

## Критерии успеха

Рефакторинг считается успешным, когда:

- ни один entity-компонент не делает self-loading в `Start()`;
- main-thread sync import path для большинства ресурсов устранён;
- есть внятный timing breakdown по уровню;
- загрузка уровня использует bounded async concurrency;
- animation cache повторно использует clips только при реальной semantic совместимости;
- unload уровня корректно освобождает cached artifacts по scope;
- большие уровни грузятся заметно ровнее, без крупных main-thread spikes.

---

## Что не входит в первую итерацию

- полноценную terrain streaming систему;
- vegetation system;
- audio importer;
- full Lua/AI runtime behavior;
- глобальную ECS/Jobs migration.

---

## Практический следующий шаг (после Phase 1)

После фиксации baseline Phase 1:

1. создать `FcEntityLoadService` с priority queue;
2. создать `FcMeshLoadService` как async версию текущего sync path в `FcMeshEntity`;
3. переключить `FcMeshEntity` и `FcCharacterEntity` на регистрацию вместо self-loading.
