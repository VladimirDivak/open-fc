# Animation Cache Reuse Plan

Дата среза: 2026-05-09.

## Цель

Построить поэтапный план для надежного runtime-reuse анимаций между одинаковыми или animation-compatible персонажами, в первую очередь между наёмниками.

План рассчитан на работу агента небольшими итерациями:

- агент делает один этап;
- агент просит проверить результат в Unity/Play Mode;
- только после подтверждения идет дальше.

---

## Проблема текущего состояния

Сейчас в `CgfAnimationRuntimeImportService` уже есть:

- parse cache для `CAF` по `virtualPath`;
- semantic hash содержимого `CAF`;
- clip cache по ключу:
  - `cafContentHash`
  - `alias`
  - `importScale`
  - `controllerMapKey`

Этого недостаточно как целевой архитектуры:

- parse cache привязан к пути, а не к semantic content;
- готовый `AnimationClip` кешируется уже в path-bound виде;
- совместимость между одинаковыми NPC пока не доказывается явно метриками;
- нет отдельного понятия:
  - `pathLayoutHash`
  - `animationSetHash`
  - `clip build version`
  - `semantic clip` vs `bound Unity clip`

---

## Общий принцип внедрения

Каждый этап должен быть:

- маленьким;
- обратимым;
- проверяемым в Play Mode;
- полезным сам по себе.

После каждого этапа агент должен остановиться и попросить проверку результата, а не пытаться протащить весь рефакторинг за один проход.

---

## Этап 1. Добавить диагностику совместимости

### Задача

Сначала собрать доказательства, что mercenary-модели действительно используют один и тот же animation-compatible rig и один и тот же animation set.

### Что менять

- `Assets/Scripts/Importer/Cgf/CgfAnimationRuntimeImportService.cs`
- при необходимости:
  - `Assets/Scripts/Importer/Cgf/CgfRuntimeLoadSmokeTest.cs`
  - небольшие helper-структуры рядом с animation import кодом

### Что добавить

- вычисление и логирование:
  - `animationFingerprint`
  - `pathLayoutHash`
  - `animationSetHash`
  - количества clip cache hits/misses на модель
- отдельный summary для одного runtime smoke run
- режим, в котором удобно сравнить несколько mercenary-моделей подряд

### Ожидаемый результат

Должно стать видно:

- совпадает ли `animationFingerprint` у всех наёмников;
- совпадает ли layout binding paths;
- совпадает ли набор `alias -> cafContentHash`;
- где именно теряется reuse: parse, clip build или attach.

### Что проверить вручную

Запустить smoke/runtime сцену на 2-5 одинаковых или близких mercenary-моделях и проверить:

- одинаковы ли `animationFingerprint`;
- одинаковы ли `pathLayoutHash`;
- одинаков ли `animationSetHash`;
- есть ли уже clip cache hits на повторной загрузке.

### Что агент должен спросить после этапа

`Проверь логи smoke run и подтверди: у mercenary-моделей совпадают ли animationFingerprint/pathLayoutHash/animationSetHash?`

---

## Этап 2. Стабилизировать ключ готового clip cache

### Задача

Сделать текущий clip cache более управляемым и безопасным без смены архитектуры.

### Что менять

- `Assets/Scripts/Importer/Cgf/CgfAnimationRuntimeImportService.cs`

### Что добавить

- явную константу `ClipBuildVersion`
- новый состав ключа для готового `AnimationClip`:
  - `cafContentHash`
  - `alias`
  - `importScale`
  - `animationFingerprint` или fallback на `controllerMapKey`
  - `pathLayoutHash`
  - `loopPolicyKey`
  - `ClipBuildVersion`

### Зачем это нужно

- чтобы изменения в loop heuristics или binding logic корректно инвалидировали старый cache;
- чтобы reuse опирался на более короткие и осмысленные fingerprints;
- чтобы можно было безопасно сравнивать совместимость между моделями.

### Ожидаемый результат

- текущий cache станет детерминированнее;
- появится более надежный reuse между идентичными NPC;
- будет проще диагностировать false miss и false hit.

### Что проверить вручную

Повторить smoke run на одинаковых mercenary-моделях и сравнить:

- вырос ли `clipHitCount`;
- не появилось ли warning-ов про missing controller tracks;
- не сломались ли bind/path назначения.

### Что агент должен спросить после этапа

`Проверь в Unity: одинаковые mercenary теперь получают clip cache hits без визуальной поломки анимации?`

---

## Этап 3. Отвязать parse cache от virtual path

### Задача

Добавить дедупликацию parsed `CAF` между разными путями, если семантическое содержимое одинаково.

### Что менять

- `Assets/Scripts/Importer/Cgf/CgfAnimationRuntimeImportService.cs`

### Что добавить

- индекс:
  - `virtualPath -> sourceBytesHash/contentHash`
- основной parsed cache:
  - `cafContentHash -> CafFile`
- корректную статистику:
  - path hit/miss
  - semantic parsed hit/miss

### Зачем это нужно

Даже если одинаковые анимации лежат в разных `.caf`, parse не должен повторяться без необходимости.

### Ожидаемый результат

- меньше повторного `CafParser.Parse(...)`;
- выше cache hit rate на больших наборах NPC;
- понятнее картина: что не переиспользуется из-за путей, а что из-за реальной несовместимости.

### Что проверить вручную

Прогнать кейс с несколькими NPC, где возможны одинаковые анимации из разных путей, и проверить:

- уменьшается ли число parse misses;
- сохраняется ли корректность импортированных анимаций.

### Что агент должен спросить после этапа

`Проверь логи: semantic CAF cache действительно переиспользуется там, где раньше был повторный parse?`

---

## Этап 4. Ввести Animation Set cache для одинаковых NPC

### Задача

Научиться переиспользовать не только отдельные клипы, но и весь набор анимаций как единый пакет для одинаковых mercenary rigs.

### Что менять

- `Assets/Scripts/Importer/Cgf/CgfAnimationRuntimeImportService.cs`
- возможно новый файл:
  - `Assets/Scripts/Importer/Cgf/CgfAnimationSetCache.cs`

### Что добавить

- `animationSetHash`:
  - hash от отсортированного списка `alias -> cafContentHash`
- cache набора:
  - ключ:
    - `animationFingerprint`
    - `pathLayoutHash`
    - `animationSetHash`
    - `importScale`
    - `ClipBuildVersion`
- reuse уже готового пакета `alias -> AnimationClip`

### Зачем это нужно

Для наёмников важен именно массовый reuse полного комплекта анимаций, а не только отдельных клипов.

### Ожидаемый результат

- первый NPC строит комплект;
- следующие NPC почти только attach-ят уже готовые клипы;
- время на массовую загрузку персонажей заметно падает.

### Что проверить вручную

Запустить сцену с несколькими наёмниками и проверить:

- строится ли animation set только один раз;
- уменьшается ли `animationMs` на последующих персонажах;
- сохраняется ли корректная проигровка анимаций.

### Что агент должен спросить после этапа

`Проверь массовую загрузку mercenary NPC: видно ли, что полный animation set reuse-ится, а не собирается заново на каждом персонаже?`

---

## Этап 5. Разделить semantic clip data и bound Unity clip

### Задача

Сделать целевую архитектуру, где path-agnostic анимационные данные кешируются отдельно от Unity-bound `AnimationClip`.

### Что менять

- `Assets/Scripts/Importer/Cgf/CgfAnimationRuntimeImportService.cs`
- новые файлы, вероятно:
  - `Assets/Scripts/Importer/Cgf/CgfSemanticClipData.cs`
  - `Assets/Scripts/Importer/Cgf/CgfBoundClipCache.cs`

### Что добавить

- промежуточный DTO:
  - normalized tracks по `controllerId`
  - timing
  - loop decision
  - semantic clip hash
- два уровня кеша:
  - `semanticClipKey -> SemanticClipData`
  - `semanticClipKey + pathLayoutHash -> AnimationClip`

### Зачем это нужно

Это убирает смешение двух разных задач:

- reuse motion data;
- binding motion data к конкретному skeleton layout в Unity.

### Ожидаемый результат

- архитектура станет чище;
- reuse станет безопаснее и расширяемее;
- будет проще поддержать эквивалентные риги с разными path layouts.

### Что проверить вручную

Проверить минимум два кейса:

- identical rig/layout: reuse готового bound clip;
- compatible motion, но иной layout: reuse semantic clip data, но отдельная bind/build стадия.

### Что агент должен спросить после этапа

`Проверь два сценария: identical layout reuse-ит готовый clip, а differing layout reuse-ит только semantic data без поломки анимации.`

---

## Этап 6. Добить тесты и метрики

### Задача

Закрепить поведение автоматическими проверками и сделать диагностику достаточно подробной, чтобы следующий рефакторинг не сломал reuse незаметно.

### Что менять

- EditMode tests для importer/runtime cache логики
- при необходимости smoke test helpers

### Что добавить

- тесты на:
  - одинаковый semantic `CAF` при разных путях;
  - invalidation по `ClipBuildVersion`;
  - reuse для identical mercenary rigs;
  - отсутствие reuse для несовместимого `pathLayoutHash`;
  - корректную работу `animationSetHash`
- расширенные runtime stats:
  - `semanticClipHit/miss`
  - `boundClipHit/miss`
  - `animationSetHit/miss`

### Ожидаемый результат

- поведение можно безопасно менять дальше;
- регрессии по reuse будут видны сразу;
- smoke logs станут пригодны для сравнения до/после.

### Что проверить вручную

- тесты проходят;
- smoke report читается и показывает все нужные hit/miss категории;
- в сцене нет визуальных регрессий.

### Что агент должен спросить после этапа

`Проверь тесты и smoke report: хватает ли метрик, чтобы дальше уверенно оптимизировать animation pipeline?`

---

## Рекомендуемый порядок работы агента

Агент должен идти строго так:

1. Сделать Этап 1.
2. Остановиться и попросить ручную проверку.
3. После подтверждения сделать Этап 2.
4. Снова остановиться и попросить проверку.
5. И так далее без прыжков сразу к Этапу 5-6.

Причина:

- без фактической диагностики легко начать оптимизировать ложную гипотезу;
- без ручной проверки в Unity можно получить формально высокий hit rate, но сломанные анимации;
- mercenary case надо сначала доказать метриками, а потом превращать в shared-cache policy.

---

## Минимально полезная первая итерация

Если нужен самый практичный короткий маршрут, сначала делать только:

1. Этап 1. Диагностика совместимости.
2. Этап 2. Новый clip cache key.
3. Этап 4. Animation set cache для одинаковых NPC.

Это наиболее вероятно даст быстрый выигрыш именно для наёмников, даже до полного архитектурного разделения semantic/bound cache.

---

## Критерии успеха

Работу можно считать успешной, когда:

- одинаковые mercenary NPC разделяют один animation-compatible fingerprint;
- одинаковый animation set не пересобирается на каждом NPC;
- повторные clip build операции заметно сокращены;
- reuse не вызывает визуальных ошибок в Play Mode;
- smoke/test метрики явно показывают, где именно произошел выигрыш.

