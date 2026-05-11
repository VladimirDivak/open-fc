using OpenFarCry.Importer.Cgf;
using OpenFarCry.Level.Data;
using OpenFarCry.Level.Services;
using UnityEngine;

namespace OpenFarCry.Level.Entities
{
    // NPC / AI character entity. Imports skeletal mesh with animations, ragdoll, and LODs.
    public class FcCharacterEntity : FcMeshEntity
    {
        [Header("Character")]
        [SerializeField] bool _importAnimations = true;
        [SerializeField] bool _importRagdoll = true;

        readonly CgfRagdollBuilder _ragdollBuilder = new CgfRagdollBuilder();
        readonly CgfAnimationRuntimeImportService _animFallbackService = new CgfAnimationRuntimeImportService();

        public override EntityLoadPriority GetDefaultLoadPriority() => EntityLoadPriority.Characters;

        public override FcMeshLoadRequest CreateLoadRequest()
        {
            return new FcMeshLoadRequest(
                virtualPath: _virtualPath,
                levelScopeId: GetLevelScopeId(),
                selectedMeshChunkId: -1,
                importSkeleton: true,
                importScale: _importScale,
                useRuntimeMemoryCache: true,
                preloadTextures: true);
        }

        protected override void ApplyResult(CgfRuntimeImportResult result)
        {
            // Build mesh GO + LODs (via base)
            base.ApplyResult(result);

            var meshRoot = _lastBuildOutput.Root;
            if (meshRoot == null) return;

            var boneTransforms = _lastBuildOutput.BoneTransforms;

            // Ragdoll
            if (_importRagdoll && result.BuildResult.HasSkeleton && boneTransforms != null)
            {
                _ragdollBuilder.AddBonePhysicsBoxColliders(
                    result.ParsedFile, result.BuildResult, boneTransforms, _importScale);
                _ragdollBuilder.AddRagdollBodiesAndJoints(
                    meshRoot, boneTransforms, result.ParsedFile, result.BuildResult);
            }

            // Animations
            if (_importAnimations)
            {
                string animWarning = null;
                var animLoadService = FcAnimationLoadService.Current;
                if (animLoadService != null)
                {
                    var artifact = animLoadService.LoadAndAttach(
                        new FcAnimationLoadRequest(
                            targetRoot: meshRoot,
                            parsedFile: result.ParsedFile,
                            modelVirtualPath: result.VirtualPath,
                            importScale: _importScale));
                    animWarning = artifact.WarningMessage;
                }
                else
                {
                    _animFallbackService.TryAttachAnimations(
                        meshRoot,
                        result.ParsedFile,
                        rigDefinition: null,
                        modelVirtualPath: result.VirtualPath,
                        importScale: _importScale,
                        out animWarning);
                }

                if (!string.IsNullOrEmpty(animWarning))
                    Debug.LogWarning($"[FcCharacterEntity] '{_virtualPath}': {animWarning}", this);
            }
        }

        public override void SetData(FcEntityDesc desc)
        {
            base.SetData(desc);
            _importSkeleton = true;
        }
    }
}
