using Sirenix.OdinInspector;
using UnityEngine;

namespace OpenFarCry.FileSystem
{
    [CreateAssetMenu(fileName = "FcFileSystemSettings", menuName = "OpenFarCry/File System Settings")]
    public class FcFileSystemSettings : ScriptableObject
    {
        [FolderPath(AbsolutePath = true)]
        [Tooltip("Absolute path to the Far Cry 1 installation directory (containing FCData/).")]
        public string gameInstallPath = "";

#if UNITY_EDITOR
        [ShowInInspector, ReadOnly, HideInEditorMode]
        [BoxGroup("Runtime Debug")]
        public int MountedPakCount => FcFileSystem.MountedCount;

        [ShowInInspector, ReadOnly, HideInEditorMode]
        [BoxGroup("Runtime Debug")]
        public int TotalIndexedFiles => FcFileSystem.TotalFileCount;
#endif
    }
}
