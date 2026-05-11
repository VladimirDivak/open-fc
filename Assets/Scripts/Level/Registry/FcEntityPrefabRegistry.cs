using System;
using System.Collections.Generic;
using UnityEngine;

namespace OpenFarCry.Level.Registry
{
    public enum FcEntityPrefabType
    {
        Unknown,
        Mesh,
        RigidBody,
        Character,
        Light,
        Sound,
        Trigger,
        SpawnPoint,
        TagPoint,
        Vehicle,
        Door,
        Pickup,
        Mine,
        Particle,
        Environment,
        Boid,
        FcCamera,
        Destructible,
    }

    [Serializable]
    public struct EntityClassMapping
    {
        [Tooltip("Entity class name or substring (case-insensitive).")]
        public string Pattern;
        public FcEntityPrefabType PrefabType;
    }

    [CreateAssetMenu(fileName = "FcEntityPrefabRegistry",
        menuName = "OpenFarCry/Entity Prefab Registry")]
    public sealed class FcEntityPrefabRegistry : ScriptableObject
    {
        [Header("Prefabs")]
        [SerializeField] GameObject _meshEntityPrefab;
        [SerializeField] GameObject _rigidBodyEntityPrefab;
        [SerializeField] GameObject _characterEntityPrefab;
        [SerializeField] GameObject _lightEntityPrefab;
        [SerializeField] GameObject _soundEntityPrefab;
        [SerializeField] GameObject _triggerEntityPrefab;
        [SerializeField] GameObject _spawnPointPrefab;
        [SerializeField] GameObject _tagPointPrefab;
        [SerializeField] GameObject _vehicleEntityPrefab;
        [SerializeField] GameObject _doorEntityPrefab;
        [SerializeField] GameObject _pickupEntityPrefab;
        [SerializeField] GameObject _mineEntityPrefab;
        [SerializeField] GameObject _particleEntityPrefab;
        [SerializeField] GameObject _environmentEntityPrefab;
        [SerializeField] GameObject _boidEntityPrefab;
        [SerializeField] GameObject _cameraEntityPrefab;
        [SerializeField] GameObject _destructibleEntityPrefab;

        [Header("Class Mappings (override built-in)")]
        [SerializeField] EntityClassMapping[] _classMappings = Array.Empty<EntityClassMapping>();

        // Returns the appropriate prefab for an EntityClass string, or null if unknown.
        public GameObject GetPrefabForClass(string entityClass)
        {
            var type = ResolveType(entityClass);
            return GetPrefabForType(type);
        }

        // Returns the prefab for <Object Type="..."> entries.
        public GameObject GetPrefabForObjectType(string objectType)
        {
            if (string.IsNullOrEmpty(objectType)) return _tagPointPrefab;

            if (objectType.Equals("Respawn", StringComparison.OrdinalIgnoreCase))
                return _spawnPointPrefab;

            if (objectType.Equals("Shape", StringComparison.OrdinalIgnoreCase) ||
                objectType.Equals("AreaBox", StringComparison.OrdinalIgnoreCase))
                return _triggerEntityPrefab;

            // TagPoint, AIAnchor, Waypoint, Group → tag point
            return _tagPointPrefab;
        }

        public GameObject GetPrefabForType(FcEntityPrefabType type)
        {
            return type switch
            {
                FcEntityPrefabType.Mesh        => _meshEntityPrefab,
                FcEntityPrefabType.RigidBody   => _rigidBodyEntityPrefab,
                FcEntityPrefabType.Character   => _characterEntityPrefab,
                FcEntityPrefabType.Light       => _lightEntityPrefab,
                FcEntityPrefabType.Sound       => _soundEntityPrefab,
                FcEntityPrefabType.Trigger     => _triggerEntityPrefab,
                FcEntityPrefabType.SpawnPoint  => _spawnPointPrefab,
                FcEntityPrefabType.TagPoint    => _tagPointPrefab,
                FcEntityPrefabType.Vehicle     => _vehicleEntityPrefab,
                FcEntityPrefabType.Door        => _doorEntityPrefab,
                FcEntityPrefabType.Pickup      => _pickupEntityPrefab,
                FcEntityPrefabType.Mine        => _mineEntityPrefab,
                FcEntityPrefabType.Particle    => _particleEntityPrefab,
                FcEntityPrefabType.Environment => _environmentEntityPrefab,
                FcEntityPrefabType.Boid        => _boidEntityPrefab,
                FcEntityPrefabType.FcCamera    => _cameraEntityPrefab,
                FcEntityPrefabType.Destructible => _destructibleEntityPrefab,
                _ => null,
            };
        }

        FcEntityPrefabType ResolveType(string entityClass)
        {
            if (string.IsNullOrEmpty(entityClass)) return FcEntityPrefabType.Unknown;

            // User-defined overrides first
            foreach (var mapping in _classMappings)
            {
                if (!string.IsNullOrEmpty(mapping.Pattern) &&
                    entityClass.IndexOf(mapping.Pattern, StringComparison.OrdinalIgnoreCase) >= 0)
                    return mapping.PrefabType;
            }

            // Built-in table
            return BuiltInResolve(entityClass);
        }

        static FcEntityPrefabType BuiltInResolve(string cls)
        {
            // Marker / navigation types (check before Character to avoid mismatches)
            foreach (string pat in s_tagPointPatterns)
                if (cls.Equals(pat, StringComparison.OrdinalIgnoreCase))
                    return FcEntityPrefabType.TagPoint;

            // Vehicles (check before Character — gunship/cargochopper are vehicles)
            foreach (string pat in s_vehiclePatterns)
                if (cls.IndexOf(pat, StringComparison.OrdinalIgnoreCase) >= 0)
                    return FcEntityPrefabType.Vehicle;

            // AI / character types
            foreach (string pat in s_characterPatterns)
                if (cls.IndexOf(pat, StringComparison.OrdinalIgnoreCase) >= 0)
                    return FcEntityPrefabType.Character;

            // Lights
            if (cls.IndexOf("Light", StringComparison.OrdinalIgnoreCase) >= 0)
                return FcEntityPrefabType.Light;

            // Sounds
            foreach (string pat in s_soundPatterns)
                if (cls.IndexOf(pat, StringComparison.OrdinalIgnoreCase) >= 0)
                    return FcEntityPrefabType.Sound;

            // Triggers
            foreach (string pat in s_triggerPatterns)
                if (cls.IndexOf(pat, StringComparison.OrdinalIgnoreCase) >= 0)
                    return FcEntityPrefabType.Trigger;

            // Doors
            foreach (string pat in s_doorPatterns)
                if (cls.IndexOf(pat, StringComparison.OrdinalIgnoreCase) >= 0)
                    return FcEntityPrefabType.Door;

            // Pickups (substring — covers Pickup*, Ammo*, health, Armor, KeyCard*, Checkpoint)
            foreach (string pat in s_pickupPatterns)
                if (cls.IndexOf(pat, StringComparison.OrdinalIgnoreCase) >= 0)
                    return FcEntityPrefabType.Pickup;

            // Mines (exact match to avoid false positives)
            foreach (string pat in s_minePatterns)
                if (cls.Equals(pat, StringComparison.OrdinalIgnoreCase))
                    return FcEntityPrefabType.Mine;

            // Particle effects
            foreach (string pat in s_particlePatterns)
                if (cls.IndexOf(pat, StringComparison.OrdinalIgnoreCase) >= 0)
                    return FcEntityPrefabType.Particle;

            // Environment volumes
            foreach (string pat in s_environmentPatterns)
                if (cls.IndexOf(pat, StringComparison.OrdinalIgnoreCase) >= 0)
                    return FcEntityPrefabType.Environment;

            // Boids (exact match — short names like "Bugs" could be substrings)
            foreach (string pat in s_boidPatterns)
                if (cls.Equals(pat, StringComparison.OrdinalIgnoreCase))
                    return FcEntityPrefabType.Boid;

            // Destructibles
            foreach (string pat in s_destructiblePatterns)
                if (cls.IndexOf(pat, StringComparison.OrdinalIgnoreCase) >= 0)
                    return FcEntityPrefabType.Destructible;

            // Camera entities (exact match)
            foreach (string pat in s_cameraPatterns)
                if (cls.Equals(pat, StringComparison.OrdinalIgnoreCase))
                    return FcEntityPrefabType.FcCamera;

            // RigidBody types
            foreach (string pat in s_rigidBodyPatterns)
                if (cls.Equals(pat, StringComparison.OrdinalIgnoreCase))
                    return FcEntityPrefabType.RigidBody;

            // Spawn points
            if (cls.Equals("Respawn", StringComparison.OrdinalIgnoreCase) ||
                cls.Equals("SpawnPoint", StringComparison.OrdinalIgnoreCase))
                return FcEntityPrefabType.SpawnPoint;

            // Mesh / default
            return FcEntityPrefabType.Mesh;
        }

        static readonly string[] s_tagPointPatterns =
        {
            "AIAnchor", "Waypoint", "TagPoint", "Group",
            "NavigationSeed", "Anchor", "AIPoint", "VisualScriptHook",
        };

        static readonly string[] s_vehiclePatterns =
        {
            "fwdvehicle", "Buggy", "Bigtrack", "Forklift",
            "boat", "Paraglider", "gunship", "cargochopper",
        };

        static readonly string[] s_characterPatterns =
        {
            "Grunt", "MercCover", "MercScout", "MercSniper", "MercRear",
            "Mutant", "Pig", "Shark", "Worm",
            "BasicAI", "NPC", "CreatureGenerator",
        };

        static readonly string[] s_soundPatterns =
        {
            "SoundSpot", "EAXArea", "EAXPresetArea", "RandomAmbientSound",
            "SoundExclusive", "SoundExclusivePreset", "MusicMoodSelector",
            "MusicThemeSelector", "MissionHint",
        };

        static readonly string[] s_triggerPatterns =
        {
            "ProximityTrigger", "AreaTrigger", "AITrigger", "VisibilityTrigger",
            "BoatTrampolineTrigger", "DelayTrigger", "MultipleTrigger",
            "SmartProximityTrigger", "DamageArea", "ProximityDamage",
            "ProximityKeyTrigger", "ImpulseTrigger",
        };

        static readonly string[] s_doorPatterns =
        {
            "Door", "AutomaticElevator", "FlyingFox", "ladder",
        };

        static readonly string[] s_pickupPatterns =
        {
            "Pickup", "Ammo", "health", "Armor", "KeyCard", "Checkpoint",
        };

        static readonly string[] s_minePatterns =
        {
            "AreaMine", "FrogMine", "ProximityMine", "PlaceableExplo", "PlaceableGeneric",
        };

        static readonly string[] s_particlePatterns =
        {
            "ParticleEffect", "ParticleSpray", "BFly", "Grasshopper",
        };

        static readonly string[] s_environmentPatterns =
        {
            "Fog", "Storm", "EnvColor", "ViewDist", "RaisingWater",
        };

        static readonly string[] s_boidPatterns =
        {
            "Birds", "Fish", "Bugs",
        };

        static readonly string[] s_destructiblePatterns =
        {
            "BreakableObject", "DestroyableObject", "BuildableObject",
            "DeadBody", "AnimObject", "AICrate", "AIObject", "SwivilChair",
        };

        static readonly string[] s_cameraPatterns =
        {
            "CameraSource", "CameraTargetPoint",
        };

        static readonly string[] s_rigidBodyPatterns =
        {
            "RigidBody", "SwingingObject", "fan", "fan2", "pusher",
            "piece", "rope", "chain", "cloth",
        };

#if UNITY_EDITOR
        // Validation helper for editor — returns which prefabs are unassigned.
        public IEnumerable<string> GetUnassignedPrefabNames()
        {
            if (_meshEntityPrefab == null)        yield return nameof(_meshEntityPrefab);
            if (_rigidBodyEntityPrefab == null)   yield return nameof(_rigidBodyEntityPrefab);
            if (_characterEntityPrefab == null)   yield return nameof(_characterEntityPrefab);
            if (_lightEntityPrefab == null)       yield return nameof(_lightEntityPrefab);
            if (_soundEntityPrefab == null)       yield return nameof(_soundEntityPrefab);
            if (_triggerEntityPrefab == null)     yield return nameof(_triggerEntityPrefab);
            if (_spawnPointPrefab == null)        yield return nameof(_spawnPointPrefab);
            if (_tagPointPrefab == null)          yield return nameof(_tagPointPrefab);
            if (_vehicleEntityPrefab == null)     yield return nameof(_vehicleEntityPrefab);
            if (_doorEntityPrefab == null)        yield return nameof(_doorEntityPrefab);
            if (_pickupEntityPrefab == null)      yield return nameof(_pickupEntityPrefab);
            if (_mineEntityPrefab == null)        yield return nameof(_mineEntityPrefab);
            if (_particleEntityPrefab == null)    yield return nameof(_particleEntityPrefab);
            if (_environmentEntityPrefab == null) yield return nameof(_environmentEntityPrefab);
            if (_boidEntityPrefab == null)        yield return nameof(_boidEntityPrefab);
            if (_cameraEntityPrefab == null)      yield return nameof(_cameraEntityPrefab);
            if (_destructibleEntityPrefab == null) yield return nameof(_destructibleEntityPrefab);
        }
#endif
    }
}
