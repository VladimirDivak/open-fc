using System;
using System.IO;
using OpenFarCry.Importer.Cgf;

namespace OpenFarCry.Importer.Editor
{
    public sealed class CgfImportEditorService
    {
        readonly CgfRuntimeImportService _runtimeImportService;

        public CgfImportEditorService(CgfRuntimeImportService runtimeImportService = null)
        {
            _runtimeImportService = runtimeImportService ?? CgfRuntimeImporter.Service;
        }

        public CgfImportResult ImportToScene(CgfImportRequest request)
        {
            if (request == null)
                throw new ArgumentNullException(nameof(request));
            if (request.ParsedFile == null)
                return CgfImportResult.Failed("Parsed file is null.");
            if (string.IsNullOrWhiteSpace(request.ParsedPath))
                return CgfImportResult.Failed("Parsed path is null or empty.");

            if (!request.ImportAnimations &&
                !request.ImportPhysicsBoxColliders &&
                (request.SiblingLodPaths == null || request.SiblingLodPaths.Count == 0) &&
                request.TryInstantiateCachedPrefab != null &&
                request.TryInstantiateCachedPrefab(out var cached))
            {
                return CgfImportResult.Completed(
                    gameObject: cached,
                    mesh: null,
                    rigCacheNote: null,
                    usedCachedPrefab: true,
                    runtimeResult: null);
            }

            BuildResult buildResult;
            CgfFile parsedForImport;
            CgfRuntimeImportResult runtimeResult = null;

            if (request.UseRuntimeImportService)
            {
                runtimeResult = _runtimeImportService.Import(
                    new CgfRuntimeImportRequest(
                        virtualPath: request.ParsedPath,
                        selectedMeshChunkId: request.ParsedFile.SelectedMeshChunkID,
                        importSkeleton: request.ImportSkeleton,
                        importAnimations: request.ImportAnimations,
                        importPhysicsBoxColliders: request.ImportPhysicsBoxColliders,
                        importRagdollBodies: request.ImportRagdollBodies,
                        importScale: request.ImportScale,
                        rigCachePolicy: request.RigCachePolicy,
                        useRuntimeMemoryCache: request.UseRuntimeMemoryCache,
                        preferProjectCache: request.PreferProjectCache),
                    request.LevelScopeId);

                if (runtimeResult.Success)
                {
                    buildResult = runtimeResult.BuildResult;
                    parsedForImport = runtimeResult.ParsedFile;
                }
                else
                {
                    try
                    {
                        parsedForImport = request.ParsedFile;
                        buildResult = CgfMeshBuilder.Build(parsedForImport, request.ImportSkeleton, request.ImportScale);
                    }
                    catch (Exception)
                    {
                        return CgfImportResult.Failed(runtimeResult.ErrorMessage, runtimeResult);
                    }
                }
            }
            else
            {
                try
                {
                    parsedForImport = request.ParsedFile;
                    buildResult = CgfMeshBuilder.Build(parsedForImport, request.ImportSkeleton, request.ImportScale);
                }
                catch (Exception e)
                {
                    return CgfImportResult.Failed(e.Message);
                }
            }

            CgfRigDefinition rigDefinition = null;
            string rigCacheNote = null;
            if (buildResult.HasSkeleton &&
                CgfRigSnapshotBuilder.TryBuild(parsedForImport, buildResult, request.ParsedPath, out var snapshot))
            {
                rigDefinition = CgfRigRegistry.ResolveOrCreate(
                    snapshot,
                    request.RigCachePolicy,
                    allowProjectWrite: request.SaveToProject,
                    out bool createdNow,
                    out var matchMode);

                if (rigDefinition != null)
                    rigCacheNote = BuildRigCacheNote(snapshot.RigFingerprint, rigDefinition.RigFingerprint, matchMode, createdNow);
            }

            if (request.BuildGameObject == null ||
                request.ConfigureLodGroup == null ||
                request.ApplyPostTransform == null)
            {
                return CgfImportResult.Failed("Import request delegates are not fully configured.", runtimeResult);
            }

            string baseName = Path.GetFileNameWithoutExtension(request.ParsedPath);
            var go = request.BuildGameObject(
                buildResult,
                parsedForImport,
                rigDefinition,
                baseName,
                request.ImportPhysicsBoxColliders,
                request.ImportRagdollBodies,
                request.ImportScale);

            request.ConfigureLodGroup(go, buildResult.HasSkeleton, request.ImportScale, request.SaveToProject);

            System.Collections.Generic.List<CgfImportedAnimationClip> importedClips = null;
            if (request.ImportAnimations && request.AttachAnimations != null)
            {
                importedClips = request.AttachAnimations(
                    go,
                    parsedForImport,
                    rigDefinition,
                    request.ParsedPath,
                    request.ImportScale);
            }

            request.ApplyPostTransform(go);

            if (request.SaveToProject && request.SaveAssets != null)
                request.SaveAssets(buildResult.Mesh, go, importedClips);

            return CgfImportResult.Completed(
                gameObject: go,
                mesh: buildResult.Mesh,
                rigCacheNote: rigCacheNote,
                usedCachedPrefab: false,
                runtimeResult: runtimeResult);
        }

        static string BuildRigCacheNote(
            string snapshotFingerprint,
            string resolvedRigFingerprint,
            CgfRigRegistry.ResolveMatchMode matchMode,
            bool createdNow)
        {
            switch (matchMode)
            {
                case CgfRigRegistry.ResolveMatchMode.Created:
                    return $"Rig snapshot создан: {snapshotFingerprint}";
                case CgfRigRegistry.ResolveMatchMode.AnimationCompatible:
                    return $"Rig snapshot переиспользован по animation-compatible fingerprint: {resolvedRigFingerprint}";
                default:
                    return createdNow
                        ? $"Rig snapshot создан: {snapshotFingerprint}"
                        : $"Rig snapshot переиспользован: {snapshotFingerprint}";
            }
        }
    }
}
