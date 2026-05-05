using UnityEditor;

namespace OpenFarCry.FileSystem.Editor
{
    // Монтирует PAK-архивы при загрузке домена редактора (перекомпиляция,
    // открытие проекта) — чтобы VFS работал в Edit Mode, не только в Play Mode.
    [InitializeOnLoad]
    static class FcFileSystemEditorInit
    {
        static FcFileSystemEditorInit()
        {
            FcFileSystem.Initialize();
        }
    }
}
