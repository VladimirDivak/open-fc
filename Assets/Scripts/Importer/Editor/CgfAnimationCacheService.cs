using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace OpenFarCry.Importer.Editor
{
    public sealed class CgfAnimationCacheService
    {
        public void RebindAnimationComponentToSharedClips(
            string meshPath,
            GameObject go,
            IReadOnlyList<CgfImportedAnimationClip> clips)
        {
            if (clips == null || clips.Count == 0 || go == null)
                return;

            var animation = go.GetComponent<Animation>();
            if (animation == null)
                return;

            animation.clip = null;
            var existingNames = new List<string>();
            foreach (AnimationState state in animation)
                existingNames.Add(state.name);
            for (int i = 0; i < existingNames.Count; i++)
                animation.RemoveClip(existingNames[i]);

            AnimationClip defaultClip = null;
            for (int i = 0; i < clips.Count; i++)
            {
                var src = clips[i];
                if (src?.Clip == null || string.IsNullOrEmpty(src.Alias))
                    continue;

                string cacheKey = string.IsNullOrWhiteSpace(src.SharedCacheKey)
                    ? BuildSharedAnimationClipContentKey(src.Clip)
                    : src.SharedCacheKey;
                if (string.IsNullOrWhiteSpace(cacheKey))
                    continue;

                string sharedClipPath = ImportAssetPaths.GetSharedAnimationClipPath(cacheKey, src.Alias);
                EnsureDirectory(Path.GetDirectoryName(sharedClipPath));

                var sharedClip = AssetDatabase.LoadAssetAtPath<AnimationClip>(sharedClipPath);
                if (sharedClip == null)
                {
                    AssetDatabase.CreateAsset(src.Clip, sharedClipPath);
                    sharedClip = AssetDatabase.LoadAssetAtPath<AnimationClip>(sharedClipPath);
                }

                if (sharedClip == null)
                    continue;

                animation.AddClip(sharedClip, src.Alias);

                if (defaultClip == null ||
                    string.Equals(src.Alias, "default", StringComparison.OrdinalIgnoreCase))
                {
                    defaultClip = sharedClip;
                }

                string legacyClipPath = GetAnimationAssetPath(meshPath, src.Alias);
                if (!string.Equals(legacyClipPath, sharedClipPath, StringComparison.OrdinalIgnoreCase))
                {
                    var legacyClip = AssetDatabase.LoadAssetAtPath<AnimationClip>(legacyClipPath);
                    if (legacyClip != null)
                        AssetDatabase.DeleteAsset(legacyClipPath);
                }
            }

            if (defaultClip != null)
                animation.clip = defaultClip;
        }

        public string BuildSharedAnimationClipContentKey(AnimationClip clip)
        {
            if (clip == null)
                return Hash128.Compute("anim-clip-v2|null").ToString();

            var sb = new StringBuilder(4096);
            sb.Append("anim-clip-v2|");
            sb.Append(clip.legacy ? "1" : "0").Append('|');
            sb.Append((int)clip.wrapMode).Append('|');

            var curveBindings = AnimationUtility.GetCurveBindings(clip)
                .OrderBy(b => b.path, StringComparer.Ordinal)
                .ThenBy(b => b.type != null ? b.type.FullName : string.Empty, StringComparer.Ordinal)
                .ThenBy(b => b.propertyName, StringComparer.Ordinal)
                .ToArray();

            for (int i = 0; i < curveBindings.Length; i++)
            {
                var binding = curveBindings[i];
                sb.Append((binding.path ?? string.Empty).ToLowerInvariant()).Append('|');
                sb.Append(binding.type != null ? binding.type.FullName : string.Empty).Append('|');
                sb.Append(binding.propertyName ?? string.Empty).Append('|');

                var curve = AnimationUtility.GetEditorCurve(clip, binding);
                AppendCurveSignature(sb, curve);
                sb.Append(';');
            }

            var objectBindings = AnimationUtility.GetObjectReferenceCurveBindings(clip)
                .OrderBy(b => b.path, StringComparer.Ordinal)
                .ThenBy(b => b.type != null ? b.type.FullName : string.Empty, StringComparer.Ordinal)
                .ThenBy(b => b.propertyName, StringComparer.Ordinal)
                .ToArray();

            for (int i = 0; i < objectBindings.Length; i++)
            {
                var binding = objectBindings[i];
                sb.Append("obj|");
                sb.Append((binding.path ?? string.Empty).ToLowerInvariant()).Append('|');
                sb.Append(binding.type != null ? binding.type.FullName : string.Empty).Append('|');
                sb.Append(binding.propertyName ?? string.Empty).Append('|');

                var keys = AnimationUtility.GetObjectReferenceCurve(clip, binding);
                if (keys == null || keys.Length == 0)
                {
                    sb.Append("0;");
                    continue;
                }

                sb.Append(keys.Length).Append('|');
                for (int k = 0; k < keys.Length; k++)
                {
                    var key = keys[k];
                    AppendQuantizedFloat(sb, key.time);
                    sb.Append('=');
                    sb.Append(key.value != null ? key.value.name : "<null>");
                    sb.Append(',');
                }

                sb.Append(';');
            }

            return Hash128.Compute(sb.ToString()).ToString();
        }

        static void AppendCurveSignature(StringBuilder sb, AnimationCurve curve)
        {
            if (curve == null || curve.keys == null || curve.keys.Length == 0)
            {
                sb.Append("0");
                return;
            }

            var keys = curve.keys;
            sb.Append(keys.Length).Append('|');
            for (int i = 0; i < keys.Length; i++)
            {
                var key = keys[i];
                AppendQuantizedFloat(sb, key.time);
                AppendQuantizedFloat(sb, key.value);
                AppendQuantizedFloat(sb, key.inTangent);
                AppendQuantizedFloat(sb, key.outTangent);
                AppendQuantizedFloat(sb, key.inWeight);
                AppendQuantizedFloat(sb, key.outWeight);
                sb.Append((int)key.weightedMode).Append(',');
            }
        }

        static void AppendQuantizedFloat(StringBuilder sb, float value)
        {
            int q = Mathf.RoundToInt(value * 1000000f);
            sb.Append(q).Append(',');
        }

        static bool EnsureDirectory(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath))
                return false;

            string full = Path.GetFullPath(assetPath);
            if (Directory.Exists(full))
                return false;

            Directory.CreateDirectory(full);
            return true;
        }

        static string GetAnimationAssetPath(string meshPath, string alias)
        {
            string noExt = meshPath.EndsWith(".asset", StringComparison.OrdinalIgnoreCase)
                ? meshPath.Substring(0, meshPath.Length - ".asset".Length)
                : meshPath;
            return $"{noExt}@{SanitizeFileName(alias)}.anim";
        }

        static string SanitizeFileName(string value)
        {
            if (string.IsNullOrEmpty(value))
                return "anim";

            char[] invalid = Path.GetInvalidFileNameChars();
            var chars = value.ToCharArray();
            for (int i = 0; i < chars.Length; i++)
            {
                if (Array.IndexOf(invalid, chars[i]) >= 0 ||
                    chars[i] == '/' ||
                    chars[i] == '\\')
                {
                    chars[i] = '_';
                }
            }

            return new string(chars);
        }
    }
}
