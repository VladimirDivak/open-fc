using OpenFarCry.Importer.Cgf;
using OpenFarCry.Level.Data;
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
        readonly CgfAnimationRuntimeImportService _animService = new CgfAnimationRuntimeImportService();

        protected override void Start()
        {
            if (string.IsNullOrEmpty(_virtualPath)) return;

            var service = ResourceService;
            if (service == null)
            {
                Debug.LogWarning($"[FcCharacterEntity] No FcLevelResourceService found for '{name}'", this);
                return;
            }

            var request = new CgfRuntimeImportRequest(
                virtualPath: _virtualPath,
                importSkeleton: true,
                importAnimations: false, // handled manually below
                importScale: _importScale,
                useRuntimeMemoryCache: true);

            var result = service.ImportCgf(request);
            if (!result.Success)
            {
                Debug.LogWarning($"[FcCharacterEntity] CGF import failed for '{_virtualPath}': {result.ErrorMessage}", this);
                return;
            }

            _importResult = result;
            ApplyResult(result);
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
                _animService.TryAttachAnimations(
                    meshRoot,
                    result.ParsedFile,
                    rigDefinition: null,
                    modelVirtualPath: result.VirtualPath,
                    importScale: _importScale,
                    out string animWarning);

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
