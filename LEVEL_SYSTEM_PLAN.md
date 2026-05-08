# Level Entity System — Plan

## Концепция

Система строится на двух независимых слоях:

**Editor layer** — парсит XML уровней из PAK-архивов, создаёт Unity-сцену с placeholder-объектами. Сцена сохраняется в репозитории и не содержит данных из оригинальной игры (ни байта из PAK).

**Runtime layer** — при старте сцены каждый placeholder начинает async-загрузку своего ресурса из PAK. Уровень-скоупные сервисы кэша управляют жизненным циклом загруженных ресурсов.

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
| `FcSoundEntity` | `SoundSpot`, `EAXArea`, `RandomAmbientSound` | virtual path к звуку, volume, min/maxDist, loop | `AudioClip` из PAK (когда будет аудио-имопортер) |
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

Выступает entry-point для placeholders: они не обращаются к `CgfRuntimeImporter` напрямую, а запрашивают через сервис.

### FcLevelResourceService

`MonoBehaviour` на том же корневом объекте. Управляет очередью и приоритетом загрузки.

```
FcLevelResourceService
  ├── _loadQueue       — Priority Queue<FcLoadRequest> (приоритет = dist к камере)
  ├── _maxConcurrent   — int (напр. 4 одновременных загрузки)
  ├── RequestLoad(placeholder, priority) → UniTask<>
  └── Update()         — продвигает очередь
```

Placeholders регистрируют запрос при `Start()`, получают результат через `await`.

### Поток загрузки одного placeholder'а

```
FcMeshEntity.Start()
  → FcLevelResourceService.RequestLoad(this)
  → (ожидание в очереди)
  → CgfRuntimeImporter.Import(virtualPath, ...)    ← из PAK
  → _gameObjectBuilder.Build(result)
  → Instantiate как дочерний объект placeholder'а
  → ClearLoadingVisual()
```

### Loading visual

Пока идёт загрузка, placeholder показывает простой индикатор (маленький wireframe-куб или billboard с именем класса). Убирается после успешной загрузки.

---

## Editor workflow

### FcLevelImporterWindow (`OpenFarCry/Level Importer`)

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
│   └── FcLevelResourceService.cs        — MonoBehaviour, load queue
│
├── Placeholders/
│   ├── FcEntity.cs                      — base: EntityId, EntityClass, Load()
│   ├── FcMeshEntity.cs
│   ├── FcRigidBodyEntity.cs
│   ├── FcCharacterEntity.cs
│   ├── FcLightEntity.cs
│   ├── FcSoundEntity.cs
│   ├── FcTriggerEntity.cs
│   ├── FcSpawnPoint.cs
│   └── FcTagPoint.cs
│
└── Editor/
    ├── OpenFarCry.Level.Editor.asmdef   (refs: Level, Importer.Editor)
    ├── FcLevelImporterWindow.cs
    └── FcEntityPlaceholderDrawers.cs    — кастомные Inspector GUI для placeholders
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
- Static brush geometry (`brush.lst`) — отдельная задача
- Vegetation / particles — отдельная задача
- Аудио (`FcSoundEntity`) — загружает только когда будет аудио-имопортер; пока placeholder без clip
- AI поведение, Lua-скрипты — `FcCharacterEntity` только CGF visual
- EventTargets wiring — парсить, хранить, не выполнять

---

## Открытые вопросы

1. **Маппинг Angles**: нужна эмпирическая верификация — взять entity с известным поворотом и проверить в сцене
2. **Приоритизация загрузки** в `FcLevelResourceService`: по дистанции к камере, или просто FIFO?
3. **Loading visual**: wireframe cube / billboard / ничего?
4. **Сцена или Prefab**: editor workflow создаёт отдельный `.unity` файл на уровень или добавляет объекты в открытую сцену?
