# Level Loading Refactor Plan

Дата среза: 2026-05-08.

## Цель

Перестроить загрузку уровня так, чтобы:

- entity-компоненты не загружали ресурсы сами;
- загрузка шла через централизованные сервисы;
- тяжёлые этапы максимально использовали async/thread-pool там, где это безопасно;
- кеши mesh/animation/material работали предсказуемо и масштабировались на большие уровни;
- уровень имел прозрачную диагностику по времени загрузки и cache hit/miss поведению.

---

## Проблемы текущего состояния

### 1. Self-loading у entity

Сейчас `FcMeshEntity`, `FcCharacterEntity` и `FcBrushInstance` сами начинают загрузку в `Start()`.

Следствия:

- orchestration размазан по компонентам;
- невозможно централизованно ограничить concurrency;
- нет общей очереди загрузки и приоритетов;
- сложно делать staged loading уровня;
- сложно агрегировать timing/reporting.

### 2. Загрузка в основном синхронная

Сейчас основной runtime path вызывает synchronous import на main thread:

- `FcLevelResourceService.ImportCgf(...)`
- `CgfRuntimeImporter.Import(...)`

Хотя в VFS уже есть `FcFileSystem.ReadAllBytesAsync(...)`, pipeline уровня его почти не использует.

Следствия:

- main-thread spikes;
- плохая масштабируемость на больших уровнях;
- невозможно аккуратно загружать персонажей/brushes/декорации по приоритету.

### 3. Анимационный кеш всё ещё ненадёжен

Нужно окончательно разделить:

- кеш исходных CAF-данных;
- кеш построенных `AnimationClip`;
- сигнатуру совместимости controller mapping;
- semantic clip reuse между разными моделями.

Требование: кеш анимаций должен повторно использовать clip, когда результат действительно идентичен, и не reuse-ить его, когда отличаются значимые кривые/маппинг.

### 4. Нет нормального timing breakdown по уровню

Сейчас в логах есть:

- факт сборки уровня;
- отдельные warning/error сообщения;
- runtime smoke logs для тестового импортера.

Но нет системного breakdown по этапам загрузки реального уровня.

---

## Целевая архитектура

```text
Level Scene / Mission Data
  -> FcLevelLoadService
  -> FcEntityLoadService
      -> FcMeshLoadService
      -> FcAnimationLoadService
      -> FcBrushLoadService
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

### FcBrushLoadService

Сервис для brush-ориентированного runtime path.

Ответственность:

- batching для большого числа brush instances;
- shared mesh reuse;
- proxy/no-draw collider extraction;
- deferred loading для дальних brushes.

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

`FcBrushInstance`:

- `CreateLoadRequest()`
- `ApplyLoadedBrush(LoadedMeshArtifact artifact)`
- `ReleaseLoadedBrush()`

---

## Async Pipeline

## Общий принцип

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

Назначение:

- быстро понять, изменился ли сам CAF;
- не держать устаревший parse только по path.

#### Built clip cache key

Основа:

- semantic CAF content signature
- clip alias
- import scale
- controller mapping signature
- loop policy / import options

Назначение:

- reuse clip между разными моделями, если итоговый результат действительно идентичен;
- не reuse-ить clip, если поменялся mapping или meaningful curve content.

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

В critical batch обычно входят:

- player-near entities;
- gameplay-critical characters;
- важные triggers/spawn points, если для них нужны runtime resources;
- ближайшие visible brushes/props.

### Deferred set

В deferred batch идут:

- дальние props;
- дальние brushes;
- второстепенные characters;
- expensive animation warmups.

---

## Инструментация и метрики

Нужно добавить обязательный timing breakdown.

### Editor level build

Логировать отдельно:

- `MountLevelPak`
- `LoadMissionXml`
- `ParseMissionXml`
- `LoadBrushList`
- `BuildEntities`
- `BuildObjects`
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

### Итоговый report

В конце загрузки уровня нужен агрегированный summary:

- total wall time;
- total main-thread time;
- total background time;
- average per entity class;
- top slowest assets;
- cache hit/miss ratios;
- queue wait time;
- active concurrency peaks.

---

## Порядок внедрения

### Phase 1. Instrumentation first

Сначала добавить тайминги без изменения архитектуры.

Задача:

- понять реальные bottleneck-и;
- зафиксировать baseline;
- иметь чем измерять эффект последующих изменений.

### Phase 2. Remove self-loading

Убрать `Start() -> ImportCgf()` из:

- `FcMeshEntity`
- `FcCharacterEntity`
- `FcBrushInstance`

Заменить на orchestration через `FcEntityLoadService`.

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
- `FcBrushLoadService`

И перевести entity apply на результаты сервисов.

### Phase 5. Stabilize caches

Пересобрать cache policy для:

- parsed CGF;
- parsed CAF;
- built model;
- built clip;
- materials.

Особый фокус: animation semantic reuse.

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

- `Assets/Scripts/Level/Services/FcLevelResourceService.cs`
- `Assets/Scripts/Level/Entities/FcMeshEntity.cs`
- `Assets/Scripts/Level/Entities/FcCharacterEntity.cs`
- `Assets/Scripts/Level/Entities/FcBrushInstance.cs`
- `Assets/Scripts/Importer/Cgf/CgfAnimationRuntimeImportService.cs`

### Нужно добавить

- `Assets/Scripts/Level/Services/FcLevelLoadService.cs`
- `Assets/Scripts/Level/Services/FcEntityLoadService.cs`
- `Assets/Scripts/Level/Services/FcMeshLoadService.cs`
- `Assets/Scripts/Level/Services/FcAnimationLoadService.cs`
- `Assets/Scripts/Level/Services/FcBrushLoadService.cs`
- `Assets/Scripts/Level/Services/FcLevelLoadReport.cs`
- DTO/Request/Artifact types для pipeline.

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

Не пытаться тащить в первый проход:

- полноценную terrain streaming систему;
- vegetation system;
- audio importer;
- full Lua/AI runtime behavior;
- глобальную ECS/Jobs migration.

Первая итерация должна решить именно architecture + loading + caching + timing.

---

## Практический следующий шаг

Самый разумный следующий шаг:

1. добавить timing instrumentation в current pipeline;
2. зафиксировать baseline на уровне `Training`;
3. после этого начинать вынос orchestration в `FcLevelLoadService`.

Без baseline дальнейший рефакторинг будет трудно оценивать объективно.
