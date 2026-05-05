using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using OpenFarCry.Importer.Cgf;

namespace OpenFarCry.Importer.Editor
{
    public static class CgfRigRegistry
    {
        static readonly Dictionary<string, CgfRigDefinition> MemoryCache =
            new Dictionary<string, CgfRigDefinition>(StringComparer.OrdinalIgnoreCase);
        static readonly Dictionary<string, CgfRigDefinition> MemoryAnimationCache =
            new Dictionary<string, CgfRigDefinition>(StringComparer.OrdinalIgnoreCase);

        public enum ResolveMatchMode
        {
            Created,
            Exact,
            AnimationCompatible
        }

        public static CgfRigDefinition ResolveOrCreate(
            in CgfRigSnapshot snapshot,
            in CgfRigCachePolicy policy,
            bool allowProjectWrite,
            out bool createdNow,
            out ResolveMatchMode matchMode)
        {
            createdNow = false;
            matchMode = ResolveMatchMode.Exact;
            if (!snapshot.IsValid)
                return null;

            if (policy.UseMemoryCache &&
                MemoryCache.TryGetValue(snapshot.RigFingerprint, out var memoryDef) &&
                memoryDef != null)
            {
                matchMode = ResolveMatchMode.Exact;
                return memoryDef;
            }

            string assetPath = GetRigAssetPath(policy.ProjectAssetRoot, snapshot.RigFingerprint);
            if (policy.UseProjectCache)
            {
                var assetDef = AssetDatabase.LoadAssetAtPath<CgfRigDefinition>(assetPath);
                if (assetDef != null)
                {
                    if (policy.UseMemoryCache)
                        RegisterInMemory(assetDef);
                    matchMode = ResolveMatchMode.Exact;
                    return assetDef;
                }
            }

            if (!string.IsNullOrEmpty(snapshot.AnimationFingerprint))
            {
                if (policy.UseMemoryCache &&
                    MemoryAnimationCache.TryGetValue(snapshot.AnimationFingerprint, out var memByAnim) &&
                    memByAnim != null)
                {
                    matchMode = ResolveMatchMode.AnimationCompatible;
                    return memByAnim;
                }

                if (policy.UseProjectCache &&
                    TryLoadByAnimationFingerprint(policy.ProjectAssetRoot, snapshot.AnimationFingerprint, out var byAnim))
                {
                    if (policy.UseMemoryCache)
                        RegisterInMemory(byAnim);
                    matchMode = ResolveMatchMode.AnimationCompatible;
                    return byAnim;
                }
            }

            var created = ScriptableObject.CreateInstance<CgfRigDefinition>();
            created.ApplySnapshot(snapshot);
            createdNow = true;
            matchMode = ResolveMatchMode.Created;

            if (policy.UseProjectCache && policy.PersistProjectAssets && allowProjectWrite)
            {
                EnsureAssetFolder(policy.ProjectAssetRoot);
                AssetDatabase.CreateAsset(created, assetPath);
                AssetDatabase.SaveAssets();
                AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceSynchronousImport);
            }

            if (policy.UseMemoryCache)
                RegisterInMemory(created);

            return created;
        }

        static void RegisterInMemory(CgfRigDefinition definition)
        {
            if (definition == null || !definition.IsValid || string.IsNullOrEmpty(definition.RigFingerprint))
                return;

            MemoryCache[definition.RigFingerprint] = definition;
            string animationFingerprint = GetAnimationFingerprint(definition);
            if (!string.IsNullOrEmpty(animationFingerprint))
                MemoryAnimationCache[animationFingerprint] = definition;
        }

        static bool TryLoadByAnimationFingerprint(string root, string animationFingerprint, out CgfRigDefinition definition)
        {
            definition = null;
            if (string.IsNullOrEmpty(animationFingerprint))
                return false;

            string assetRoot = NormalizeAssetPath(root);
            var guids = AssetDatabase.FindAssets("t:CgfRigDefinition", new[] { assetRoot });
            for (int i = 0; i < guids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                if (string.IsNullOrEmpty(path))
                    continue;

                var loaded = AssetDatabase.LoadAssetAtPath<CgfRigDefinition>(path);
                if (loaded == null || !loaded.IsValid)
                    continue;
                string loadedAnimationFingerprint = GetAnimationFingerprint(loaded);
                if (!string.Equals(loadedAnimationFingerprint, animationFingerprint, StringComparison.OrdinalIgnoreCase))
                    continue;

                definition = loaded;
                return true;
            }

            return false;
        }

        static string GetAnimationFingerprint(CgfRigDefinition definition)
        {
            if (definition == null)
                return string.Empty;
            if (!string.IsNullOrEmpty(definition.AnimationFingerprint))
                return definition.AnimationFingerprint;

            return CgfRigSnapshotBuilder.BuildAnimationFingerprintFromRigData(
                definition.BoneNames,
                definition.ControllerIdsByBoneIndex);
        }

        static string GetRigAssetPath(string root, string fingerprint)
        {
            string assetRoot = NormalizeAssetPath(root);
            return $"{assetRoot}/{fingerprint}.asset";
        }

        static string NormalizeAssetPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return "Assets/FCData/RigCache";

            string normalized = path.Replace('\\', '/').Trim();
            if (normalized.Equals("Assets", StringComparison.OrdinalIgnoreCase))
                return "Assets";

            if (!normalized.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
                normalized = "Assets/" + normalized.TrimStart('/');

            return normalized.TrimEnd('/');
        }

        static void EnsureAssetFolder(string path)
        {
            string normalized = NormalizeAssetPath(path);
            if (AssetDatabase.IsValidFolder(normalized))
                return;

            var parts = normalized.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0 || !parts[0].Equals("Assets", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"Rig cache root must be under Assets/: '{path}'.");

            string cursor = "Assets";
            for (int i = 1; i < parts.Length; i++)
            {
                string next = $"{cursor}/{parts[i]}";
                if (!AssetDatabase.IsValidFolder(next))
                    AssetDatabase.CreateFolder(cursor, parts[i]);
                cursor = next;
            }
        }
    }
}
