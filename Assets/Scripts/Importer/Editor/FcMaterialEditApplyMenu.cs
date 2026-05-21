using System.Collections.Generic;
using OpenFarCry.Importer.Cgf;
using UnityEditor;
using UnityEngine;

namespace OpenFarCry.Importer.Editor
{
    // Writes Play Mode material tweaks back into the baked .mat project assets.
    //
    // Runtime materials are Instantiated copies of the baked assets, so editing a material
    // on a scene object in Play Mode never touches the asset. FcRuntimeMaterialAssetLink
    // records instance → source-asset; this menu copies the editable (non-texture) shader
    // properties from each live instance back into its source asset.
    //
    // Run this WHILE Play Mode is still active — instance state is discarded on exit.
    static class FcMaterialEditApplyMenu
    {
        const string ApplyAllPath = "OpenFarCry/Materials/Apply Play Mode Edits to Assets";
        const string ApplySelectionPath = "OpenFarCry/Materials/Apply Play Mode Edits from Selection";

        // Persisted shader knobs — colors and floats only. Texture slots and internal
        // render-state (blend modes, surface type, smoothness channel) are intentionally
        // excluded: textures are runtime-injected, render-state is derived from the chunk.
        static readonly string[] ColorKnobs =
        {
            "_BaseColor", "_Color", "_SpecColor", "_EmissionColor",
        };
        static readonly string[] FloatKnobs =
        {
            "_Smoothness", "_Metallic", "_Cutoff", "_BumpScale", "_OcclusionStrength",
        };

        [MenuItem(ApplyAllPath, priority = 200)]
        static void ApplyAll()
        {
            if (!EditorApplication.isPlaying)
            {
                Debug.LogWarning("[FcMaterialEdit] Not in Play Mode — no runtime instances to apply.");
                return;
            }

            int changed = 0;
            int scanned = 0;
            var dirtied = new HashSet<Material>();

            foreach (var pair in FcRuntimeMaterialAssetLink.Links)
            {
                var instance = pair.Key;
                var asset = pair.Value;
                if (instance == null || asset == null)
                    continue;
                scanned++;
                if (CopyEditableProperties(instance, asset))
                {
                    dirtied.Add(asset);
                    changed++;
                }
            }

            FinishApply(dirtied, changed, scanned);
        }

        [MenuItem(ApplySelectionPath, priority = 201)]
        static void ApplySelection()
        {
            if (!EditorApplication.isPlaying)
            {
                Debug.LogWarning("[FcMaterialEdit] Not in Play Mode — no runtime instances to apply.");
                return;
            }

            var selected = Selection.gameObjects;
            if (selected == null || selected.Length == 0)
            {
                Debug.LogWarning("[FcMaterialEdit] No GameObjects selected.");
                return;
            }

            var links = FcRuntimeMaterialAssetLink.Links;
            int changed = 0;
            int scanned = 0;
            var dirtied = new HashSet<Material>();

            for (int s = 0; s < selected.Length; s++)
            {
                if (selected[s] == null)
                    continue;

                var renderers = selected[s].GetComponentsInChildren<Renderer>(includeInactive: true);
                for (int r = 0; r < renderers.Length; r++)
                {
                    var mats = renderers[r] != null ? renderers[r].sharedMaterials : null;
                    if (mats == null)
                        continue;

                    for (int i = 0; i < mats.Length; i++)
                    {
                        var instance = mats[i];
                        if (instance == null || !links.TryGetValue(instance, out var asset) || asset == null)
                            continue;
                        scanned++;
                        if (CopyEditableProperties(instance, asset))
                        {
                            dirtied.Add(asset);
                            changed++;
                        }
                    }
                }
            }

            FinishApply(dirtied, changed, scanned);
        }

        static void FinishApply(HashSet<Material> dirtied, int changed, int scanned)
        {
            foreach (var asset in dirtied)
                EditorUtility.SetDirty(asset);

            if (dirtied.Count > 0)
                AssetDatabase.SaveAssets();

            Debug.Log(
                $"[FcMaterialEdit] Applied Play Mode edits: {changed} slot(s) changed across " +
                $"{dirtied.Count} asset(s); {scanned} runtime material(s) scanned.");
        }

        // Copies the whitelisted shader knobs (colors + floats) from a runtime instance
        // onto its source asset. Texture slots and render-state internals are excluded.
        // Returns true when at least one knob value differed.
        static bool CopyEditableProperties(Material from, Material to)
        {
            if (from == null || to == null)
                return false;

            var shader = to.shader;
            if (shader == null || from.shader != shader)
                return false;

            bool changed = false;

            for (int i = 0; i < ColorKnobs.Length; i++)
            {
                string prop = ColorKnobs[i];
                if (!from.HasProperty(prop) || !to.HasProperty(prop))
                    continue;
                Color value = from.GetColor(prop);
                if (to.GetColor(prop) != value)
                {
                    to.SetColor(prop, value);
                    changed = true;
                }
            }

            for (int i = 0; i < FloatKnobs.Length; i++)
            {
                string prop = FloatKnobs[i];
                if (!from.HasProperty(prop) || !to.HasProperty(prop))
                    continue;
                float value = from.GetFloat(prop);
                if (!Mathf.Approximately(to.GetFloat(prop), value))
                {
                    to.SetFloat(prop, value);
                    changed = true;
                }
            }

            return changed;
        }
    }
}
