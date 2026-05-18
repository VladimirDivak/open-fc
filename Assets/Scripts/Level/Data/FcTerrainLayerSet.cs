using System;
using System.Collections.Generic;
using System.Globalization;
using System.Xml;
using OpenFarCry.FileSystem;
using UnityEngine;

namespace OpenFarCry.Level.Data
{
    // Parsed Far Cry terrain paint layers for one level, read from the mounted
    // <level>.cry editor archive (bindRoot "levels/<key>/cry").
    public sealed class FcTerrainLayerSet
    {
        // Surface megatexture resolution the editor used (Heightmap TextureSize).
        public int TextureSize;

        // Paint layers in paint order: index 0 is the bottom layer.
        public List<FcTerrainPaintLayer> Layers = new List<FcTerrainPaintLayer>();

        // Loads the terrain layer set for a level key (lower-case, e.g. "training").
        public static bool TryLoad(string levelKey, out FcTerrainLayerSet set)
        {
            set = null;
            if (string.IsNullOrEmpty(levelKey))
                return false;

            string cryRoot = $"levels/{levelKey}/cry";
            string xmlPath = $"{cryRoot}/level.editor_xml";
            if (!FcFileSystem.Exists(xmlPath))
            {
                Debug.LogWarning($"[FcTerrainLayerSet] '{xmlPath}' not found; is the .cry mounted?");
                return false;
            }

            XmlDocument doc;
            try
            {
                doc = new XmlDocument();
                using var stream = new System.IO.MemoryStream(FcFileSystem.ReadAllBytes(xmlPath));
                doc.Load(stream);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[FcTerrainLayerSet] Failed to parse '{xmlPath}': {e.Message}");
                return false;
            }

            var result = new FcTerrainLayerSet();

            var heightmapNode = doc.GetElementsByTagName("Heightmap").Count > 0
                ? doc.GetElementsByTagName("Heightmap")[0]
                : null;
            result.TextureSize = ParseInt(heightmapNode?.Attributes?["TextureSize"]?.Value, 4096);

            foreach (XmlNode node in doc.GetElementsByTagName("Layer"))
            {
                var attrs = node.Attributes;
                // Terrain paint layers carry AutoGenMask; object layers carry GUID.
                if (attrs?["AutoGenMask"] == null)
                    continue;
                if (ParseInt(attrs["InUse"]?.Value, 1) == 0)
                    continue;

                string name = attrs["Name"]?.Value;
                if (string.IsNullOrEmpty(name))
                    continue;

                var layer = ParseLayer(cryRoot, name, attrs);
                if (layer != null)
                    result.Layers.Add(layer);
            }

            if (result.Layers.Count == 0)
            {
                Debug.LogWarning($"[FcTerrainLayerSet] No terrain paint layers found for '{levelKey}'.");
                return false;
            }

            Debug.Log($"[FcTerrainLayerSet] '{levelKey}': {result.Layers.Count} layers, TextureSize={result.TextureSize}");
            foreach (var l in result.Layers)
            {
                string maskInfo = l.AutoGenMask
                    ? $"autogen alt[{l.AltStart}..{l.AltEnd}] slope[{l.MinSlope}..{l.MaxSlope}]"
                    : (l.Mask != null ? $"manual mask {l.MaskResolution}²" : "manual NO-MASK");
                Debug.Log($"[FcTerrainLayerSet]   - {l.Name} ({maskInfo}) tex {l.TextureWidth}x{l.TextureHeight}");
            }

            set = result;
            return true;
        }

        static FcTerrainPaintLayer ParseLayer(string cryRoot, string name, XmlAttributeCollection attrs)
        {
            int texW = ParseInt(attrs["TextureWidth"]?.Value, 0);
            int texH = ParseInt(attrs["TextureHeight"]?.Value, 0);
            if (texW <= 0 || texH <= 0)
            {
                Debug.LogWarning($"[FcTerrainLayerSet] Layer '{name}' has invalid texture size {texW}x{texH}; skipping.");
                return null;
            }

            string key = name.ToLowerInvariant();
            string texPath = $"{cryRoot}/layer_{key}.editor_data";
            if (!FcFileSystem.Exists(texPath))
            {
                Debug.LogWarning($"[FcTerrainLayerSet] Layer texture '{texPath}' missing; skipping layer '{name}'.");
                return null;
            }

            byte[] rgba = FcFileSystem.ReadAllBytes(texPath);
            int expected = texW * texH * 4;
            if (rgba.Length != expected)
            {
                Debug.LogWarning(
                    $"[FcTerrainLayerSet] Layer '{name}' texture size mismatch: {rgba.Length} bytes, expected {expected}; skipping.");
                return null;
            }

            var layer = new FcTerrainPaintLayer
            {
                Name          = name,
                SurfaceType   = attrs["SurfaceType"]?.Value ?? string.Empty,
                TextureRgba   = rgba,
                TextureWidth  = texW,
                TextureHeight = texH,
                AutoGenMask   = ParseInt(attrs["AutoGenMask"]?.Value, 1) != 0,
                AltStart      = ParseInt(attrs["AltStart"]?.Value, 0),
                AltEnd        = ParseInt(attrs["AltEnd"]?.Value, 255),
                MinSlope      = ParseInt(attrs["MinSlope"]?.Value, 0),
                MaxSlope      = ParseInt(attrs["MaxSlope"]?.Value, 255),
                Smooth        = ParseInt(attrs["Smooth"]?.Value, 0) != 0,
            };

            if (!layer.AutoGenMask)
            {
                string maskPath = $"{cryRoot}/layermask_{key}.editor_datac";
                if (FcFileSystem.Exists(maskPath) &&
                    FcTerrainLayerMaskDecoder.TryDecode(FcFileSystem.ReadAllBytes(maskPath), out var mask, out int maskRes))
                {
                    layer.Mask = mask;
                    layer.MaskResolution = maskRes;
                }
                else
                {
                    Debug.LogWarning(
                        $"[FcTerrainLayerSet] Manual layer '{name}' has no usable mask; treating as fully opaque.");
                }
            }

            return layer;
        }

        static int ParseInt(string s, int fallback)
            => int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out int v) ? v : fallback;
    }
}
