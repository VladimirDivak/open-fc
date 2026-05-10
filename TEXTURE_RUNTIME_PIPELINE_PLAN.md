# Texture Runtime Pipeline Plan

## Цель

Достроить отсутствующий runtime pipeline для текстур и встроить автоматическое подключение текстур в runtime-материалы CGF.

Основной результат:

- runtime умеет загружать `Texture2D` из ресурсов `PAK/VFS`;
- runtime переиспользует текстуры через общий cache;
- `CgfMaterialImportService` автоматически назначает текстуры в `Material`;
- MVP покрывает хотя бы `diffuse/base color` путь;
- архитектура не смешивает editor import cache и runtime on-demand decode.

## Текущее состояние

### Что уже есть

- `TextureResourceImportService` умеет читать исходные bytes текстур из `FcFileSystem`.
- `TextureRuntimeCache` существует как in-memory cache по `virtualPath`.
- `CgfParser` уже извлекает из material chunk:
  - `DiffuseTextureName`
  - `OpacityTextureName`
- `CgfMaterialImportService` уже централизует сборку `Material[]` для submesh-слотов.
- `CgfMaterialRuntimeCache` уже переиспользует runtime-материалы.

### Чего не хватает

- нет `TextureRuntimeImportService`, который реально декодирует `.dds/.tga/.bmp` в `Texture2D`;
- `TextureRuntimeCache` нигде не наполняется в runtime;
- `CgfMaterialBuilder` не назначает текстуры в Unity material properties;
- runtime-path вообще не использует texture names из `CgfMaterialChunk`;
- lifecycle текстур не описан: кто держит ссылки, кто очищает cache, как измеряется память;
- нет тестов и диагностики для runtime texture pipeline.

### Важное архитектурное ограничение

Editor pipeline и runtime pipeline сейчас решают разные задачи:

- editor import:
  - копирует source bytes в `Assets/FCData/...`;
  - гоняет `AssetDatabase` и `TextureImporter`;
  - годится для authoring/prebake.
- runtime:
  - должен работать без `AssetDatabase`;
  - должен читать из `VFS`;
  - должен строить `Texture2D` on-demand.

Их нельзя смешивать в один путь.

## Наблюдения по текущему коду

### Runtime texture слой

- `Assets/Scripts/Importer/Texture/TextureImportService.cs`
  - cache объявлен, но используется как заглушка.
- `Assets/Scripts/Importer/Texture/TextureRuntimeCache.cs`
  - только `TryGet/Store/Clear`, без статистики и без ref-count.
- `Assets/Scripts/Importer/Texture/TextureResourceImportService.cs`
  - runtime bytes уже доступны.

### Material слой

- `Assets/Scripts/Importer/Cgf/CgfMaterialImportService.cs`
  - правильная orchestration-point для texture hookup.
- `Assets/Scripts/Importer/Cgf/CgfMaterialBuilder.cs`
  - пока заполняет только:
    - `_BaseColor`
    - alpha clip state
    - culling
- `Assets/Scripts/Importer/Cgf/CgfMaterialRuntimeCache.cs`
  - global material cache без ref-count.

### CGF parser слой

- `Assets/Scripts/Importer/Cgf/CgfData.cs`
  - `CgfMaterialChunk` хранит только `DiffuseTextureName` и `OpacityTextureName`.
- `Assets/Scripts/Importer/Cgf/CgfParser.cs`
  - normal/spec/emissive пути сейчас в runtime model не вытаскиваются.

## Главный вывод

Сейчас нет смысла "оптимизировать runtime import текстур" как hot path, потому что рабочего runtime import path ещё нет.

Правильная последовательность такая:

1. собрать минимальный рабочий runtime texture pipeline;
2. встроить автоматическое подключение текстур в runtime materials;
3. только потом оптимизировать memory/caching/lifecycle.

## Целевая архитектура

### Поток данных

`PAK/VFS bytes`
-> `TextureRuntimeImportService`
-> `Texture2D`
-> `TextureRuntimeCache`
-> `CgfMaterialImportService`
-> `CgfMaterialBuilder`
-> `Material` с назначенными texture slots

### Слои ответственности

- `TextureResourceImportService`
  - только чтение bytes по `virtualPath`.
- `TextureRuntimeImportService`
  - decode bytes в `Texture2D`;
  - normalize/import flags;
  - cache hits/misses;
  - fallback behavior.
- `TextureRuntimeCache`
  - хранение runtime texture instances;
  - clear/stats.
- `CgfMaterialImportService`
  - разрешение texture paths из `CgfMaterialChunk`;
  - вызов texture runtime service;
  - передача resolved textures в builder.
- `CgfMaterialBuilder`
  - фактическое назначение `_BaseMap` и других shader properties.

## MVP scope

Первый этап должен быть узким.

### Входит в MVP

- runtime loader для текстур;
- cache reuse по `virtualPath`;
- diffuse/base texture hookup:
  - `DiffuseTextureName` -> `_BaseMap`
- сохранение текущего color tint через `_BaseColor`;
- диагностика hit/miss и missing texture paths;
- безопасный fallback при ошибках decode.

### Не входит в MVP

- полная поддержка всех cry material texture slots;
- separate opacity texture workflow;
- нормали, specular, gloss, emissive;
- disk-backed runtime texture cache;
- background async texture import;
- aggressive ref-count lifecycle.

## Этапы внедрения

## Этап 1. Ввести runtime texture import service

### Цель

Добавить отдельный runtime сервис, который умеет по `virtualPath`:

- проверить cache;
- прочитать bytes из `TextureResourceImportService`;
- декодировать bytes в `Texture2D`;
- сохранить в cache;
- вернуть готовую текстуру.

### Новые файлы

- `Assets/Scripts/Importer/Texture/TextureRuntimeImportService.cs`

### Изменения

- `Assets/Scripts/Importer/Texture/TextureImportService.cs`
  - добавить shared singleton/service entrypoint по аналогии с CGF runtime service;
  - не смешивать с editor API.

### Интерфейс

Примерный API:

```csharp
public sealed class TextureRuntimeImportService
{
    public Texture2D Load(string virtualPath, TextureRuntimeImportOptions options = default);
    public bool TryLoad(string virtualPath, out Texture2D texture, TextureRuntimeImportOptions options = default);
    public void ClearRuntimeCache();
    public TextureRuntimeCache.Stats GetStats();
}
```

### Решения по формату

- стартовать с форматов, которые реально можем декодировать безопасно;
- если `.dds` нужен первым, нужен отдельный runtime decoder;
- если `TGA/BMP` можно загрузить через Unity runtime APIs проще, их можно пустить раньше, но только если они реально встречаются в данных.

### Риск

Если полноценный `.dds` decode не реализован, MVP texture hookup останется ограниченным.

## Этап 2. Усилить TextureRuntimeCache

### Цель

Сделать cache пригодным для измеримой runtime эксплуатации.

### Изменения

- `Assets/Scripts/Importer/Texture/TextureRuntimeCache.cs`

### Что добавить

- `Stats`:
  - entry count
  - hit count
  - miss count
- возможность централизованного `Clear()`;
- optional future-proof hooks под ref-count или LRU, но без переусложнения MVP.

### Важное решение

На MVP ref-count можно не вводить.

Причина:

- сейчас material cache тоже глобальный и без ref-count;
- сначала надо доказать полезность reuse;
- lifecycle можно ужесточить отдельно вторым этапом.

## Этап 3. Подключить runtime texture service к material pipeline

### Цель

Сделать так, чтобы материалы автоматически подтягивали текстуры из parsed material metadata.

### Основная точка интеграции

- `Assets/Scripts/Importer/Cgf/CgfMaterialImportService.cs`

### Изменения

- внедрить dependency на texture runtime service;
- при сборке material key учитывать наличие relevant texture paths;
- при build передавать resolved texture set в material builder.

### Пример структуры

```csharp
public readonly struct CgfResolvedMaterialTextures
{
    public readonly Texture2D BaseMap;
    public readonly Texture2D OpacityMap;
}
```

### Важное замечание

Не нужно заставлять `CgfMaterialBuilder` читать VFS или лазить в cache напрямую.

Builder должен получать уже разрешённые texture references, а не заниматься IO.

## Этап 4. Научить CgfMaterialBuilder назначать текстуры

### Цель

Собрать первый полезный visual win без распухания shader logic.

### Изменения

- `Assets/Scripts/Importer/Cgf/CgfMaterialBuilder.cs`

### MVP поведение

- если есть `DiffuseTextureName` и texture успешно загружена:
  - назначить её в `_BaseMap`;
- `DiffuseColor` сохранить как tint multiplier в `_BaseColor`;
- alpha clip/cull behavior оставить как сейчас.

### Почему именно так

- это минимальный рабочий и визуально полезный шаг;
- не ломает текущую alpha logic;
- не требует сразу разруливать cry opacity maps.

## Этап 5. Протянуть shared texture service в CgfRuntimeImporter

### Цель

Подключить texture runtime pipeline к общему runtime importer composition root.

### Изменения

- `Assets/Scripts/Importer/Cgf/CgfRuntimeImporter.cs`

### Что сделать

- создать shared `TextureRuntimeCache`;
- создать shared `TextureRuntimeImportService`;
- передать его в `CgfMaterialImportService`;
- расширить `ClearRuntimeCache()` так, чтобы он очищал:
  - mesh/model cache
  - material cache
  - texture cache

## Этап 6. Диагностика и логирование

### Цель

Измерять эффект и находить missing assets.

### Что добавить

- счетчики texture cache hit/miss;
- количество textures в cache;
- лог missing texture path с ограничением по spam;
- optional debug report по аналогии с animation diagnostics.

### Где хранить

- внутри `TextureRuntimeImportService` или `TextureRuntimeCache`.

## Этап 7. Расширить parser для normal/spec textures

### Цель

После MVP diffuse hookup перейти к реально важным material slots.

### Изменения

- `Assets/Scripts/Importer/Cgf/CgfData.cs`
  - добавить новые поля:
    - `NormalTextureName`
    - `SpecularTextureName`
    - при необходимости `EmissiveTextureName`
- `Assets/Scripts/Importer/Cgf/CgfParser.cs`
  - перестать пропускать соответствующие texture name slots внутри `TextureMap2/TextureMap3`.

### Важно

Делать это после MVP, чтобы не смешивать:

- задачу "pipeline вообще работает"
- и задачу "полный fidelity material import"

## Этап 8. Отдельно решить opacity workflow

### Проблема

`OpacityTextureName` в URP Lit нельзя просто корректно использовать как самостоятельную opacity map без дополнительной логики.

### Возможные варианты

1. Игнорировать `OpacityTextureName` в MVP.
2. Собирать alpha в base texture во время runtime decode.
3. Ввести custom shader/workflow для cutout materials.

### Рекомендация

Для первого прохода оставить:

- `AlphaTest` и `Opacity` как есть;
- `OpacityTextureName` пока не подключать.

Иначе задача сильно расползётся.

## Этап 9. Тесты

### EditMode/unit tests

Добавить тесты на:

- normalize/resolve virtual path;
- texture cache hit/miss behavior;
- material builder with and without texture;
- fallback behavior при missing texture;
- stable material cache key при одинаковом texture set.

### Smoke tests

Добавить runtime smoke test:

- импорт CGF с diffuse texture;
- проверка, что `Renderer.sharedMaterials[i].GetTexture("_BaseMap") != null`.

## Конкретный план по файлам

### Новые файлы

- `Assets/Scripts/Importer/Texture/TextureRuntimeImportService.cs`
- `Assets/Scripts/Importer/Texture/TextureRuntimeImportOptions.cs` или вложенный options struct
- `Assets/Scripts/Importer/Tests/Editor/TextureRuntimeCacheTests.cs`
- `Assets/Scripts/Importer/Tests/Editor/CgfMaterialTextureBindingTests.cs`

### Изменяемые файлы

- `Assets/Scripts/Importer/Texture/TextureImportService.cs`
- `Assets/Scripts/Importer/Texture/TextureRuntimeCache.cs`
- `Assets/Scripts/Importer/Texture/TextureResourceImportService.cs`
  - возможно без функциональных изменений, только переиспользование API
- `Assets/Scripts/Importer/Cgf/CgfMaterialImportService.cs`
- `Assets/Scripts/Importer/Cgf/CgfMaterialBuilder.cs`
- `Assets/Scripts/Importer/Cgf/CgfRuntimeImporter.cs`
- `Assets/Scripts/Importer/Cgf/CgfData.cs`
- `Assets/Scripts/Importer/Cgf/CgfParser.cs`

## Порядок реализации

1. `TextureRuntimeImportService`
2. `TextureRuntimeCache` stats/clear API
3. wire-up в `CgfRuntimeImporter`
4. diffuse texture hookup в `CgfMaterialImportService` + `CgfMaterialBuilder`
5. smoke test на material binding
6. parser extension for normal/spec
7. opacity workflow separately

## Критерии готовности MVP

- runtime способен загрузить текстуру по `virtualPath` без `AssetDatabase`;
- повторная загрузка той же текстуры даёт cache hit;
- `CgfMaterialImportService` назначает `_BaseMap`, если `DiffuseTextureName` найден и успешно декодирован;
- при missing/failed texture материал остаётся валидным и не ломает импорт;
- `ClearRuntimeCache()` освобождает текстуры вместе с остальными runtime caches;
- есть хотя бы один smoke test на textured CGF.

## Что не делать сейчас

- не вводить сразу disk-backed binary cache для runtime textures;
- не завязывать texture lifecycle на level-scope до появления метрик;
- не смешивать editor import path с runtime load path;
- не пытаться сразу полностью воспроизвести cry material model в URP Lit.

## Рекомендация по следующему шагу

Следующий практический шаг:

1. реализовать `TextureRuntimeImportService` для MVP формата;
2. подключить `DiffuseTextureName -> _BaseMap`;
3. проверить результат на одном реальном уровне и снять hit/miss stats.
