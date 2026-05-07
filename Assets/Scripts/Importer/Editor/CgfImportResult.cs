using OpenFarCry.Importer.Cgf;
using UnityEngine;

namespace OpenFarCry.Importer.Editor
{
    public sealed class CgfImportResult
    {
        public readonly bool Success;
        public readonly string ErrorMessage;
        public readonly GameObject GameObject;
        public readonly Mesh Mesh;
        public readonly string RigCacheNote;
        public readonly bool UsedCachedPrefab;
        public readonly CgfRuntimeImportResult RuntimeResult;

        CgfImportResult(
            bool success,
            string errorMessage,
            GameObject gameObject,
            Mesh mesh,
            string rigCacheNote,
            bool usedCachedPrefab,
            CgfRuntimeImportResult runtimeResult)
        {
            Success = success;
            ErrorMessage = errorMessage;
            GameObject = gameObject;
            Mesh = mesh;
            RigCacheNote = rigCacheNote;
            UsedCachedPrefab = usedCachedPrefab;
            RuntimeResult = runtimeResult;
        }

        public static CgfImportResult Failed(string errorMessage, CgfRuntimeImportResult runtimeResult = null)
        {
            return new CgfImportResult(
                success: false,
                errorMessage: errorMessage,
                gameObject: null,
                mesh: null,
                rigCacheNote: null,
                usedCachedPrefab: false,
                runtimeResult: runtimeResult);
        }

        public static CgfImportResult Completed(
            GameObject gameObject,
            Mesh mesh,
            string rigCacheNote,
            bool usedCachedPrefab,
            CgfRuntimeImportResult runtimeResult)
        {
            return new CgfImportResult(
                success: true,
                errorMessage: null,
                gameObject: gameObject,
                mesh: mesh,
                rigCacheNote: rigCacheNote,
                usedCachedPrefab: usedCachedPrefab,
                runtimeResult: runtimeResult);
        }
    }
}
