using System.Collections.Generic;
using OpenFarCry.Importer.Cgf;
using UnityEditor;
using UnityEngine;

namespace OpenFarCry.Importer.Editor
{
    public sealed class CgfAnimationImportEditorService
    {
        readonly CgfAnimationRuntimeImportService _runtimeService;
        readonly CgfAnimationCacheService _animationCacheService;

        public CgfAnimationImportEditorService(
            CgfAnimationRuntimeImportService runtimeService = null,
            CgfAnimationCacheService animationCacheService = null)
        {
            _runtimeService = runtimeService ?? new CgfAnimationRuntimeImportService();
            _animationCacheService = animationCacheService ?? new CgfAnimationCacheService();
        }

        public List<CgfImportedAnimationClip> TryAttachAnimations(
            GameObject go,
            CgfFile parsedFile,
            CgfRigDefinition rigDefinition,
            string modelVirtualPath,
            float importScale,
            out string warningMessage)
        {
            warningMessage = null;

            var runtimeClips = _runtimeService.TryAttachAnimations(
                go,
                parsedFile,
                rigDefinition,
                modelVirtualPath,
                importScale,
                out warningMessage);

            if (runtimeClips == null || runtimeClips.Count == 0)
                return null;

            var imported = new List<CgfImportedAnimationClip>(runtimeClips.Count);
            for (int i = 0; i < runtimeClips.Count; i++)
            {
                var runtimeClip = runtimeClips[i];
                if (runtimeClip == null || runtimeClip.Clip == null || string.IsNullOrWhiteSpace(runtimeClip.Alias))
                    continue;

                ApplyEditorLoopSettings(runtimeClip.Clip);

                imported.Add(new CgfImportedAnimationClip
                {
                    Alias = runtimeClip.Alias,
                    Clip = runtimeClip.Clip,
                    SharedCacheKey = _animationCacheService.BuildSharedAnimationClipContentKey(runtimeClip.Clip)
                });
            }

            return imported.Count > 0 ? imported : null;
        }

        static void ApplyEditorLoopSettings(AnimationClip clip)
        {
            if (clip == null)
                return;

            try
            {
                var settings = AnimationUtility.GetAnimationClipSettings(clip);
                settings.loopTime = clip.wrapMode == WrapMode.Loop;
                AnimationUtility.SetAnimationClipSettings(clip, settings);
            }
            catch
            {
                // Keep importer resilient across Unity API variants.
            }
        }
    }
}
