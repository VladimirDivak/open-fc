# Texture Import Runtime Next Plan

## Цель

Довести runtime import текстур до стабильного состояния на реальных ассетах Far Cry, без расширения material workflow.

## Область работ (только texture pipeline)

- декодирование `DDS` (включая часто встречающиеся DX10/BC* кейсы);
- корректные `mipmap` при runtime decode;
- измеримая диагностика покрытия форматов и ошибок;
- тесты декодера и регрессий.

## Текущее состояние

- runtime pipeline работает end-to-end;
- есть decode для `DDS/TGA/BMP`;
- есть cache + hit/miss + runtime debug report;
- есть базовая DDS форматная телеметрия;
- mipmap generation включена через `TextureRuntimeImportOptions.GenerateMipmaps`.

## Этап 1. Расширить DDS coverage

### Цель

Снизить `unsupported DDS pixel format` на реальных данных.

### Что сделать

1. Поддержать DX10 header (`DXGI`) для наиболее частых форматов:
   - BC1/BC2/BC3
   - BC4/BC5 (min viable decode)
   - R8G8B8A8/B8G8R8A8
2. Поддержать legacy `ATI2/BC5` path.
3. Улучшить error-tagging (`formatTag`) для точной диагностики.

### Критерий готовности

- заметно меньше decode-fail в smoke;
- в debug report видно, какие именно форматы ещё не покрыты.

## Этап 2. Mipmap policy

### Цель

Сделать поведение mipmap управляемым и предсказуемым.

### Что сделать

1. Проверить все decode paths на использование `GenerateMipmaps`.
2. Зафиксировать default policy:
   - runtime: mipmaps on
   - debug/диагностика: возможность выключить через options
3. Убедиться, что `makeNoLongerReadable` не ломает рабочие сценарии.

### Критерий готовности

- mipmap поведение одинаково для DDS/BMP/TGA;
- нет лишних CPU copy при стандартных runtime options.

## Этап 3. Диагностика coverage

### Цель

Иметь измеримый отчёт по форматам и ошибкам.

### Что сделать

1. Поддерживать счётчики success/fail по DDS formatTag.
2. Добавить компактный отчёт в `BuildRuntimeDebugReport()`.
3. При необходимости добавить top-N fail tags с suppression.

### Критерий готовности

- после smoke можно понять, какой следующий формат декодировать.

## Этап 4. Тесты texture decoder

### Цель

Защититься от регрессий при расширении форматов.

### Что сделать

1. Unit tests для ключевых decode branches:
   - DXT1/DXT5
   - uncompressed masked RGB
   - DX10+BC5 (минимальный кейс)
2. Тесты на безопасный fail (битые headers/unsupported formats).

### Критерий готовности

- тесты фиксируют базовые контрактные гарантии decode.

## Этап 5. Аудит фактического использования форматов

### Цель

Получать список `model/material/slot -> texture/format` автоматически, без ручного отбора объектов.

### Что сделать

1. Добавить `Editor`-утилиту, которая сканирует все `CGF/CGA` из `VFS`.
2. Для каждой ссылки на текстуру резолвить реальный `virtual path` по той же логике, что runtime loader.
3. Строить отчёт:
   - summary по расширениям;
   - summary по `DDS formatTag`;
   - missing/unsupported references;
   - полный CSV для точечного отбора.

### Критерий готовности

- можно одним запуском получить полный список того, какие форматы реально используются ассетами.

## Что не входит в этот план

- opacity workflow / material clip logic;
- normal/spec/emissive hookup в шейдер;
- полная реконструкция Cry material model.

## Практический порядок

1. Этап 1 (DDS coverage)
2. Этап 3 (диагностика coverage, если нужно донастроить)
3. Этап 2 (дофикс mipmap policy)
4. Этап 4 (тесты)
5. Этап 5 (авто-аудит usage)
