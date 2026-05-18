using OpenFarCry.Importer.Cgf;
using UnityEditor;

namespace OpenFarCry.Importer.Editor
{
    // Toggles CgfMaterialImportService diagnostic logging from the editor menu.
    // When enabled, ResolveSubmeshMaterials / ResolveAndLoadTexture dump per-submesh
    // chunk resolution and per-texture candidate probing to the Console.
    //
    // CgfMaterialImportService.DiagnosticLogging is a plain static field, so a domain
    // reload (which fires on every Play Mode entry) resets it to false. The toggle
    // state is therefore persisted in SessionState and re-applied to the static fields
    // from an [InitializeOnLoadMethod] that runs after each domain reload.
    static class CgfMaterialDiagnosticsMenu
    {
        const string ToggleMenu = "OpenFarCry/Diagnostics/CGF Material Logging";
        const string EnabledKey = "OpenFarCry.CgfMatDiag.Enabled";
        const string FilterKey = "OpenFarCry.CgfMatDiag.Filter";

        [InitializeOnLoadMethod]
        static void ApplyPersistedState()
        {
            bool enabled = SessionState.GetBool(EnabledKey, false);
            CgfMaterialImportService.DiagnosticLogging = enabled;
            CgfMaterialImportService.DiagnosticAssetFilter =
                enabled ? SessionState.GetString(FilterKey, string.Empty) : null;
        }

        [MenuItem(ToggleMenu)]
        static void Toggle()
        {
            bool enabled = !CgfMaterialImportService.DiagnosticLogging;
            SessionState.SetBool(EnabledKey, enabled);
            // Empty filter logs every CGF; set a substring to scope it. Cleared when logging
            // is turned off so a stale filter never silently suppresses a later session.
            CgfMaterialImportService.DiagnosticLogging = enabled;
            CgfMaterialImportService.DiagnosticAssetFilter =
                enabled ? SessionState.GetString(FilterKey, string.Empty) : null;
        }

        [MenuItem(ToggleMenu, validate = true)]
        static bool ToggleValidate()
        {
            Menu.SetChecked(ToggleMenu, SessionState.GetBool(EnabledKey, false));
            return true;
        }
    }
}
