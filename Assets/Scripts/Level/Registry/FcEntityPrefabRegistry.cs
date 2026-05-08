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
                FcEntityPrefabType.Mesh => _meshEntityPrefab,
                FcEntityPrefabType.RigidBody => _rigidBodyEntityPrefab,
                FcEntityPrefabType.Character => _characterEntityPrefab,
                FcEntityPrefabType.Light => _lightEntityPrefab,
                FcEntityPrefabType.Sound => _soundEntityPrefab,
                FcEntityPrefabType.Trigger => _triggerEntityPrefab,
                FcEntityPrefabType.SpawnPoint => _spawnPointPrefab,
                FcEntityPrefabType.TagPoint => _tagPointPrefab,
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

        static readonly string[] s_characterPatterns =
        {
            "Grunt", "MercCover", "MercScout", "MercSniper", "MercRear",
            "Mutant", "Pig", "Shark", "Worm", "gunship", "cargochopper",
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

        static readonly string[] s_tagPointPatterns =
        {
            "AIAnchor", "Waypoint", "TagPoint", "Group",
            "NavigationSeed", "Anchor", "AIPoint", "VisualScriptHook",
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
            if (_meshEntityPrefab == null)      yield return nameof(_meshEntityPrefab);
            if (_rigidBodyEntityPrefab == null) yield return nameof(_rigidBodyEntityPrefab);
            if (_characterEntityPrefab == null) yield return nameof(_characterEntityPrefab);
            if (_lightEntityPrefab == null)     yield return nameof(_lightEntityPrefab);
            if (_soundEntityPrefab == null)     yield return nameof(_soundEntityPrefab);
            if (_triggerEntityPrefab == null)   yield return nameof(_triggerEntityPrefab);
            if (_spawnPointPrefab == null)      yield return nameof(_spawnPointPrefab);
            if (_tagPointPrefab == null)        yield return nameof(_tagPointPrefab);
        }
#endif
    }
}
