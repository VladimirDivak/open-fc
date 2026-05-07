# CGF Importer Implementation Map

Дата среза: 2026-05-07.

Этот документ фиксирует, что уже сделано по рефакторингу CGF/CAF импорта. Это не план работ, а карта текущей реализации: какие слои появились, какие файлы за что отвечают, где проходит граница runtime/editor, как сейчас устроены кеши и что уже покрыто тестами.

## Главный Принцип

CGF импорт теперь строится вокруг runtime-first подхода:

```text
PAK / FcFileSystem virtual path
  -> CgfResourceImportService
  -> CgfRuntimeImportService
  -> CgfParser / CgfMeshBuilder / runtime builders
  -> runtime GameObject / Mesh / skeleton / animation / LOD / ragdoll
  -> optional editor wrapper
  -> optional Assets/FCData project cache
  -> optional CgfImporterWindow UI
```

`Assets/FCData` рассматривается как удобный editor cache, а не как обязательный источник данных. Runtime/build путь должен уметь читать исходные ресурсы из `FcFileSystem`/PAK напрямую.

## Текущий Размер Окна

`Assets/Scripts/Importer/Editor/CgfImporterWindow.cs` сокращен до примерно 531 строки.

Окно теперь отвечает за:

- UI и состояние выбранного файла.
- Отрисовку списка, настроек и информационной панели.
- Сбор `CgfImportRequest`.
- Вызов `CgfImportEditorService`.
- `Undo`, `Selection`, `SceneView` и другие editor UI действия.

Окно больше не содержит:

- Парсинг CAL/CAF и построение animation curves.
- Skeleton hierarchy logic.
- LOD discovery/build loops.
- Ragdoll joint/collider construction.
- Shared `.anim` cache logic.
- Prefab/mesh persistence implementation.
- Runtime cache policy.

## Runtime Слой

### Resource Contract

Файлы:

- `Assets/Scripts/Importer/IResourceImportService.cs`
- `Assets/Scripts/Importer/ResourceProjectAssetPath.cs`
- `Assets/Scripts/Importer/Cgf/CgfResourceImportService.cs`
- `Assets/Scripts/Importer/Texture/TextureResourceImportService.cs`

Что сделано:

- Введен общий контракт ресурсных import-сервисов.
- CGF/CGA и texture сервисы реализуют загрузку runtime bytes и source bytes для project asset creation.
- `CgfResourceImportService` централизует поддержку `.cgf/.cga`, нормализацию virtual path через `ImportAssetPaths` и чтение из `FcFileSystem`.

### Runtime Import Entry

Файлы:

- `Assets/Scripts/Importer/Cgf/CgfRuntimeImportRequest.cs`
- `Assets/Scripts/Importer/Cgf/CgfRuntimeImportResult.cs`
- `Assets/Scripts/Importer/Cgf/CgfRuntimeImportService.cs`
- `Assets/Scripts/Importer/Cgf/CgfRuntimeImporter.cs`

Что сделано:

- Добавлен runtime request/result API.
- `CgfRuntimeImportService.Import(...)` читает CGF/CGA из `CgfResourceImportService`, парсит через `CgfParser`, выбирает mesh chunk и строит `BuildResult` через `CgfMeshBuilder`.
- `CgfRuntimeImporter` дает статический фасад для будущих scene scripts.
- Добавлены release/cache операции: `Release`, `ReleaseModel`, `ReleaseLevelScope`, `TrimUnused`, `ClearRuntimeCache`, `GetCacheStats`.

Ограничение текущего состояния:

- Runtime service пока в основном возвращает parsed/build result. Полная сборка GameObject/animations/LOD/ragdoll все еще проходит через editor wrapper delegates при импорте из окна.

### Runtime Cache

Файл:

- `Assets/Scripts/Importer/Cgf/CgfRuntimeAssetCache.cs`

Что сделано:

- Кеш разделен на parsed entries и model entries.
- Есть ref-counting для parsed/model entries.
- Есть scope ownership через `scopeId`, чтобы уровень мог освободить удерживаемые ресурсы.
- `ReleaseLevelScope` снимает ссылки для уровня.
- `TrimUnused` удаляет entries с нулевыми ref-count.
- При удалении model entry уничтожается Unity `Mesh`.
- `GetStats` возвращает счетчики entries/ref-count/scope keys.

Зачем это важно:

- Runtime кеш не должен бесконечно расти при прохождении уровней.
- Уровневый код в будущем сможет удерживать модели на время активной сцены и освобождать их на transition.

### Source Browser

Файл:

- `Assets/Scripts/Importer/Cgf/CgfSourceBrowser.cs`

Что сделано:

- Вынесены загрузка списка `.cgf/.cga`, фильтрация, группировка по директориям.
- Вынесен parse selected file для UI preview.
- Возвращаются `ParseSelectionResult`, parse note, sibling LOD paths, стартовый mesh list index.

Используется:

- `CgfImporterWindow.RefreshFileList`
- `CgfImporterWindow.ApplyFilter`
- `CgfImporterWindow.OnFileSelected`

## Mesh / GameObject / Skeleton

### Parser And Mesh Builder

Файлы:

- `Assets/Scripts/Importer/Cgf/CgfParser.cs`
- `Assets/Scripts/Importer/Cgf/CgfData.cs`
- `Assets/Scripts/Importer/Cgf/CgfMeshBuilder.cs`

Статус:

- Binary parsing не переписывался.
- Mesh build math сохранен.
- `CgfMeshBuilder.MeshCacheVersionName` используется editor cache compatibility проверкой.

### GameObject Builder

Файл:

- `Assets/Scripts/Importer/Cgf/CgfGameObjectBuilder.cs`

Что сделано:

- Вынесено создание root `GameObject`.
- Вынесено создание static renderer или skinned renderer.
- Вынесена привязка skeleton transforms к `SkinnedMeshRenderer`.
- Builder не зависит от `UnityEditor`.

### Skeleton Builder

Файл:

- `Assets/Scripts/Importer/Cgf/CgfSkeletonBuilder.cs`

Что сделано:

- Вынесена логика построения bone transforms.
- Вынесена priority hierarchy:
  - `BoneAnim`
  - compatible `CgfRigDefinition`
  - `Node` hierarchy fallback
- Вынесены helper-методы controller id / bone id mapping.
- Вынесено применение local matrix из bind poses.

## Animation

### Runtime Animation Import

Файлы:

- `Assets/Scripts/Importer/Cgf/CafParser.cs`
- `Assets/Scripts/Importer/Cgf/CgfAnimationRuntimeImportService.cs`
- `Assets/Scripts/Importer/Cgf/CgfRuntimeAnimationClip.cs`

Что сделано:

- Вынесен CAL discovery.
- Вынесен fallback CAF discovery по `<model>_*.caf`.
- Вынесен CAF parsing через `CafParser`.
- Вынесено построение legacy `AnimationClip` curves.
- Вынесен controller-to-transform path mapping.
- Вынесены loop heuristics, которые работают без `UnityEditor`.
- Runtime service прикрепляет clips к `Animation` component.

### Editor Animation Adapter

Файлы:

- `Assets/Scripts/Importer/Editor/CgfAnimationImportEditorService.cs`
- `Assets/Scripts/Importer/Editor/CgfImportedAnimationClip.cs`

Что сделано:

- Editor wrapper вызывает runtime animation importer.
- Применяет `AnimationUtility` loop settings.
- Рассчитывает shared cache key через `CgfAnimationCacheService`.

### Shared Animation Cache

Файл:

- `Assets/Scripts/Importer/Editor/CgfAnimationCacheService.cs`

Что сделано:

- Вынесен content hash для `AnimationClip`.
- Shared clips сохраняются через `ImportAssetPaths.GetSharedAnimationClipPath(...)`.
- `Animation` component на prefab перебинживается на shared `.anim`.
- Legacy per-character duplicate clip path удаляется при наличии.

## LOD

Файл:

- `Assets/Scripts/Importer/Cgf/CgfLodImportService.cs`

Что сделано:

- Вынесен sibling LOD discovery.
- Вынесено построение LOD meshes.
- Вынесено создание LOD child renderers.
- Вынесена настройка `LODGroup`.
- Editor persistence LOD mesh делегируется через callback `persistMesh`.

Используется:

- `CgfSourceBrowser` для подсчета sibling LOD preview.
- `CgfImporterWindow.ConfigureLodGroup` через `_lodImportService`.

## Ragdoll / Physics

### Ragdoll Builder

Файлы:

- `Assets/Scripts/Importer/Cgf/CgfRagdollBuilder.cs`
- `Assets/Scripts/Importer/Cgf/FcRagdollController.cs`

Что сделано:

- Вынесено создание `BoxCollider` из `BoneMesh`.
- Вынесено создание `Rigidbody`.
- Вынесено создание/configuration `ConfigurableJoint`.
- Вынесена Cry physics limits conversion.
- Вынесена axis mapping и frame matrix логика.
- Возвращается `RagdollBuildResult` с counts, physics map и joint diagnostics.

### Ragdoll Diagnostics

Файл:

- `Assets/Scripts/Importer/Cgf/CgfRagdollDiagnostics.cs`

Что сделано:

- Вынесена `[CgfImporter][Diag]` диагностика bind pose и physics frame alignment.
- Окно теперь только вызывает `CgfRagdollDiagnostics.LogImportDiagnostics(...)`.
- `[CgfImporter][JointDiag]` строки приходят из `CgfRagdollBuilder.RagdollBuildResult`.

## Rig Cache

Файлы:

- `Assets/Scripts/Importer/Cgf/CgfRigDefinition.cs`
- `Assets/Scripts/Importer/Cgf/CgfRigSnapshotBuilder.cs`
- `Assets/Scripts/Importer/Cgf/CgfRigCacheSettings.cs`
- `Assets/Scripts/Importer/Editor/CgfRigRegistry.cs`

Что сделано:

- Runtime-safe rig definition/snapshot types остаются вне `Editor`.
- Editor-only registry отвечает за создание/поиск rig snapshots в проекте.
- `CgfImportEditorService` решает rig через `CgfRigRegistry.ResolveOrCreate(...)`.
- `CgfImporterWindow` только показывает mode/note.

## Editor Слой

### Editor Import Service

Файлы:

- `Assets/Scripts/Importer/Editor/CgfImportRequest.cs`
- `Assets/Scripts/Importer/Editor/CgfImportResult.cs`
- `Assets/Scripts/Importer/Editor/CgfImportEditorService.cs`

Что сделано:

- `CgfImportEditorService.ImportToScene(...)` стал editor orchestration layer.
- Окно передает delegates для editor-only действий:
  - instantiate cached prefab
  - build GameObject
  - configure LOD
  - attach animations
  - apply post transform
  - save assets
- Service вызывает `CgfRuntimeImportService` как основной import path.
- Rig resolve делается здесь, а не в окне.

### Asset Cache

Файл:

- `Assets/Scripts/Importer/Editor/CgfAssetCacheService.cs`

Что сделано:

- Вынесены mesh/prefab cache paths.
- Вынесена cached prefab compatibility проверка.
- Вынесены mesh asset create/replace.
- Вынесен prefab save.
- Вынесен LOD mesh persistence entry point.
- Shared animation persistence делегируется в `CgfAnimationCacheService`.

Editor-only APIs теперь сконцентрированы здесь:

- `AssetDatabase`
- `PrefabUtility`
- `Undo` для cached prefab instantiate

## Texture Importer Side Work

Файлы:

- `Assets/Scripts/Importer/Texture/TextureResourceImportService.cs`
- `Assets/Scripts/Importer/Texture/TextureImportService.cs`
- `Assets/Scripts/Importer/Editor/TextureImportEditorService.cs`

Что сделано:

- Texture importer также подключен к resource service pattern.
- Source bytes для project asset import идут через `TextureResourceImportService`.
- Это подтверждает общий контракт для будущих sound/other resource importers.

## Tests

Файлы:

- `Assets/Scripts/Importer/Tests/Editor/OpenFarCry.Importer.Tests.asmdef`
- `Assets/Scripts/Importer/Tests/Editor/CgfSourceBrowserTests.cs`
- `Assets/Scripts/Importer/Tests/Editor/CgfRuntimeAssetCacheTests.cs`

Что покрыто:

- `CgfSourceBrowser.ApplyFilter`
  - фильтрация
  - группировка
  - suggested expanded dirs
  - контроль существующего/отсутствующего selected path
- `CgfSourceBrowser.ParseSelection`
  - missing path возвращает structured error вместо исключения
- `CgfRuntimeAssetCache`
  - store/retain/release/trim для parsed entries
  - `ReleaseLevelScope + TrimUnused`
  - store/retain/release/trim/stats для model entries

Проверка в текущей среде:

- `dotnet build OpenFarCry.Importer.csproj --no-restore` проходит.
- `OpenFarCry.Importer.Editor.csproj` в CLI падает без диагностик, как и раньше.
- EditMode тесты нужно прогонять через Unity Test Runner.

## Public Entry Points For Future Scene Scripts

Основной будущий runtime API:

- `CgfRuntimeImporter.Import(...)`
- `CgfRuntimeImporter.Release(...)`
- `CgfRuntimeImporter.ReleaseModel(...)`
- `CgfRuntimeImporter.ReleaseLevelScope(...)`
- `CgfRuntimeImporter.TrimUnused()`
- `CgfRuntimeImporter.ClearRuntimeCache()`
- `CgfRuntimeImporter.GetCacheStats()`

Смысл для будущей загрузки уровней:

```text
Level data / entity prefab request
  -> CgfRuntimeImporter.Import(modelVirtualPath, levelScopeId)
  -> instantiate/configure entity
  -> on entity unload: Release(result) or ReleaseModel(modelKey)
  -> on level transition: ReleaseLevelScope(levelScopeId)
  -> after transition / memory pressure: TrimUnused()
```

## Что Осталось Важным

### Runtime Import Still Needs More Direct GameObject Path

Сейчас `CgfRuntimeImportService` умеет load/parse/build mesh data и работает с runtime cache, но полная сборка GameObject/animations/LOD/ragdoll еще фактически завершается через editor wrapper delegates при импорте из окна.

Следующий runtime-focused шаг:

- дать `CgfRuntimeImportService` прямой путь сборки `GameObject` без editor delegates;
- явно включить animation/LOD/ragdoll options в runtime result;
- сделать это callable из scene scripts без `CgfImporterWindow`.

### Cache Policy Needs Animation Entries

`CgfRuntimeAssetCache` уже имеет parsed/model entries и scoped lifetime. Для долгих игровых сессий дальше нужно расширить это на:

- parsed CAL/CAF data;
- generated runtime `AnimationClip`;
- optional byte buffer policy;
- memory diagnostics/approximate cost.

### Tests Need Wider Coverage

Следующие полезные тесты:

- `CgfRuntimeImportService`: unsupported path, missing path, cache hit/miss behavior.
- `CgfLodImportService`: deterministic LOD suffix parsing/discovery.
- `CgfAnimationRuntimeImportService`: CAL directives, dummy `?`, fallback CAF naming, loop hints.
- `CgfSkeletonBuilder`: controller id priority and hierarchy fallback order.
- `CgfAssetCacheService`: cached prefab compatibility checks.

### Unity Manual Validation Still Required

Нужно вручную проверить в Unity:

- static CGF;
- skinned CGF;
- animated character with CAL/CAF;
- LOD model;
- ragdoll/physics import;
- save to project cache;
- reuse cached prefab;
- live PAK import without relying on `Assets/FCData` assets.

## Короткая Карта Файлов

Runtime-safe CGF layer:

- `CgfResourceImportService` - source bytes and supported path contract.
- `CgfSourceBrowser` - source listing/filtering/preview parse.
- `CgfRuntimeImportService` - runtime load/parse/build and cache use.
- `CgfRuntimeAssetCache` - scoped runtime cache lifetime.
- `CgfRuntimeImporter` - static runtime facade.
- `CgfParser`, `CgfData`, `CafParser` - binary parsing/data.
- `CgfMeshBuilder` - mesh build.
- `CgfGameObjectBuilder` - Unity object/renderers/skeleton attach.
- `CgfSkeletonBuilder` - skeleton hierarchy and bind pose transforms.
- `CgfAnimationRuntimeImportService` - CAL/CAF to runtime legacy clips.
- `CgfLodImportService` - LOD discovery/build/configure.
- `CgfRagdollBuilder` - physics colliders/bodies/joints.
- `CgfRagdollDiagnostics` - bind/joint diagnostic logging.
- `CgfRigDefinition`, `CgfRigSnapshotBuilder`, `CgfRigCacheSettings` - rig data/policy.

Editor-only CGF layer:

- `CgfImporterWindow` - UI controller.
- `CgfImportEditorService` - editor import orchestration.
- `CgfImportRequest`, `CgfImportResult` - editor service DTOs.
- `CgfAssetCacheService` - mesh/prefab project cache.
- `CgfAnimationImportEditorService` - editor animation post-processing.
- `CgfAnimationCacheService` - shared `.anim` project cache.
- `CgfRigRegistry` - project-backed rig registry.
- `CgfImportedAnimationClip` - editor clip record.

