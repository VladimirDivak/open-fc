# Level Entity System — Plan

## Концепция

Система строится на двух независимых слоях:

**Editor layer** — парсит XML уровней из PAK-архивов, создаёт Unity-сцену с placeholder-объектами. Сцена сохраняется в репозитории и не содержит данных из оригинальной игры (ни байта из PAK).

**Runtime layer** — слой сервисов и entity-компонентов, который инстанцирует runtime-контент по virtual paths из сцены.

## Актуальный статус (2026-05-09)

- Реализован текущий runtime-путь через `FcLevelResourceService` + `FcLevelCacheService` + `FcEntity`-наследники.
- Загрузка CGF сейчас в основном синхронная (`CgfRuntimeImporter.Import` на main thread), очередь/приоритизация пока не внедрены.
- `FcCharacterEntity` использует `CgfAnimationRuntimeImportService`; для анимаций уже работает многоуровневый runtime cache (CAF/semantic clip/bound clip/animation set).
- Этот документ описывает целевую архитектуру; часть пунктов ниже помечена как target, а не как уже реализованное состояние.

---

## Placeholder-объекты

Placeholder — Unity `MonoBehaviour`, сохранённый в сцене. Хранит только:
- Строки: `EntityClass`, `EntityId`, virtual paths (пути до CGF/звука — не сами файлы)
- Числовые параметры: радиус, цвет, масса, флаги, размеры
- Трансформ из XML уровня (position/rotation/scale)

В сцене хранится **описание**, а не **контент**.

### Типы placeholders

| Класс | Источник в XML | Что хранит | Что загружает в runtime |
|---|---|---|---|
| `FcMeshEntity` | `BasicEntity`, `AnimObject`, `BreakableObject`, pickups, vehicles, AI | virtual path к `.cgf` | `CgfRuntimeImporter.Import` |
| `FcRigidBodyEntity` | `RigidBody`, `SwingingObject`, `fan` | virtual path, mass, density | CGF mesh + `Rigidbody` |
| `FcCharacterEntity` | `Grunt`, `MercCover`, NPC-типы | virtual path к `.cgf`, Properties (здоровье, AI параметры) | CGF mesh + анимации |
| `FcLightEntity` | `DynamicLight` | radius, color, intensity, type, direction | Unity `Light` component (данные уже есть, создаётся немедленно) |
| `FcSoundEntity` | `SoundSpot`, `EAXArea`, `RandomAmbientSound` | virtual path к звуку, volume, min/maxDist, loop | `AudioClip` из PAK (когда будет аудио-импортер) |
| `FcTriggerEntity` | `ProximityTrigger`, `AreaTrigger`, `Shape`, `AreaBox` | dimensions / points, flags | `BoxCollider`/`MeshCollider` trigger (создаётся немедленно) |
| `FcSpawnPoint` | `Object Type=Respawn` | name, angles | ничего (маркер) |
| `FcTagPoint` | `Object Type=TagPoint`, `AIAnchor` | name | ничего (маркер) |

> `FcLightEntity`, `FcTriggerEntity`, `FcSpawnPoint`, `FcTagPoint` не требуют async-загрузки из PAK — все данные для их создания берутся из параметров сцены.

---

## Runtime-загрузка

### FcLevelCacheService

`MonoBehaviour` на корневом объекте уровня. Один экземпляр на загруженный уровень.

```
FcLevelCacheService
  ├── CgfRuntimeAssetCache  — ref-counted кэш CGF (переиспользует парсеры/меши)
  ├── levelScopeId          — string ID для освобождения по завершении уровня
  └── OnDestroy()           — ReleaseLevelScope + TrimUnused
```

Управляет lifecycle level scope (`ReleaseLevelScope`/`TrimUnused`) при выгрузке уровня. Entry-point для импорта ресурсов находится в `FcLevelResourceService`.

### FcLevelResourceService

`MonoBehaviour` на том же корневом объекте.

Текущее состояние:

- даёт единый вход в `CgfRuntimeImporter.Import(...)` и `Release(...)`;
- передаёт `levelScopeId` из `FcLevelCacheService`;
- сам пока не реализует async-очередь.

Target-состояние:

- очередь загрузки и лимит конкурентных задач;
- приоритизация (дистанция до камеры/видимость);
- неблокирующая обработка тяжёлых импортов.

```
FcLevelResourceService
  ├── ImportCgf(request)         — current synchronous path
  ├── ReleaseImportResult(result)
  └── (target) load queue + priority + async workers
```

### Поток загрузки одного placeholder'а

```
FcMeshEntity.Start()
  → FcLevelResourceService.ImportCgf(request)
  → CgfRuntimeImporter.Import(virtualPath, ...)    ← из PAK
  → _gameObjectBuilder.Build(result)
  → Instantiate как дочерний объект placeholder'а
```

### Loading visual (target)

Пока идёт загрузка, placeholder показывает простой индикатор (маленький wireframe-куб или billboard с именем класса). Убирается после успешной загрузки.

---

## Editor workflow

### FcLevelBuilderWindow (`OpenFarCry/Level Builder`)

1. Список уровней из `<installPath>/Levels/` — папки с `level.pak`
2. Dropdown миссий из `leveldata.xml`
3. Кнопка **"Parse → Scene"**:
   - Монтирует `level.pak` (bind root `levels/<name>`)
   - Парсит `mission_<name>.xml`
   - Создаёт Unity-сцену (или добавляет в открытую): root `Level_<Name>`
   - Для каждого `<Entity>` / `<Object>` создаёт соответствующий placeholder GO
   - Позиция/ротация из XML (Cry Z-up → Unity Y-up)
4. Кнопка **"Clear"** — удаляет только spawned root
5. Статистика: количество entity по типам

### Данные в Placeholder (сериализованные поля)

Пример `FcMeshEntity` inspector:
```
[Header("Source")]
string EntityClass       = "BasicEntity"
int    EntityId          = 42
string VirtualPath       = "objects/props/crate.cgf"  // только строка

[Header("Options")]
bool   ImportSkeleton    = false
float  ImportScale       = 0.01
bool   HiddenInGame      = false

[Header("Runtime State")]
bool   IsLoaded          = false  (readonly)
```

---

## Структура файлов

```
Assets/Scripts/Level/
├── OpenFarCry.Level.asmdef              (refs: FileSystem, Importer)
│
├── Data/
│   ├── FcLevelData.cs                   — FcEntityDesc, FcObjectDesc, FcMissionDesc
│   └── FcLevelLoader.cs                 — XML-парсинг из PAK, ListLevels/Missions
│
├── Services/
│   ├── FcLevelCacheService.cs           — MonoBehaviour, CgfRuntimeAssetCache scope
│   └── FcLevelResourceService.cs        — MonoBehaviour, import/release entry-point (queue target)
│
├── Entities/
│   ├── FcEntity.cs                      — base: EntityId, EntityClass, Load()
│   ├── FcMeshEntity.cs
│   ├── FcRigidBodyEntity.cs
│   ├── FcCharacterEntity.cs
│   ├── FcLightEntity.cs
│   ├── FcSoundEntity.cs
│   ├── FcTriggerEntity.cs
│   ├── FcBrushInstance.cs
│   ├── FcSpawnPoint.cs
│   └── FcTagPoint.cs
│
├── Registry/
│   └── FcEntityPrefabRegistry.cs
│
└── Editor/
    ├── OpenFarCry.Level.Editor.asmdef   (refs: Level, Importer.Editor)
    ├── FcLevelBuilderWindow.cs
    └── FcLevelSceneBuilder.cs
```

---

## Координаты

| Тип данных | Масштаб | Преобразование |
|---|---|---|
| Level entity `Pos` | метры | `(x, y, z) → (x, z, -y)` |
| Level entity `Angles` | градусы | требует эмпирической верификации оси |
| CGF вершины | сантиметры | `importScale = 0.01` (как сейчас в CgfRuntimeImporter) |

Позиции уровня **не умножаются** на 0.01. CGF-меш внутри placeholder'а уже содержит 0.01 scale.

---

## Ограничения первой итерации

- Terrain (heightmap) — отдельная задача
- Static brush geometry (`brush.lst`) — базовый импорт уже есть, но система ещё не финализирована
- Vegetation / particles — отдельная задача
- Аудио (`FcSoundEntity`) — загружает только когда будет аудио-импортер; пока placeholder без clip
- AI поведение, Lua-скрипты — `FcCharacterEntity` только CGF visual
- EventTargets wiring — парсить, хранить, не выполнять

---

## Открытые вопросы

1. **Маппинг Angles**: нужна эмпирическая верификация — взять entity с известным поворотом и проверить в сцене
2. **Приоритизация загрузки** в `FcLevelResourceService`: по дистанции к камере, или просто FIFO?
3. **Loading visual**: wireframe cube / billboard / ничего?
4. **Сцена или Prefab**: editor workflow создаёт отдельный `.unity` файл на уровень или добавляет объекты в открытую сцену?
