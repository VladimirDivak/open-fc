using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using OpenFarCry.FileSystem;
using UnityEngine;

namespace OpenFarCry.Level.Data
{
    public static class FcBrushLoader
    {
        const string Signature = "CRY";
        const int FileType = 1;
        const int FileVersion = 3;

        const int MaterialStructSize = 68;   // int32 size + char[64]
        const int GeomStructSize     = 160;  // int32 size + char[128] + int32 flags + 2×Vec3
        const int BrushStructSize    = 76;   // int32 size + 4×int32 + int32 + float[12] + 4×byte

        const int GeomFlagNoPhysics = 0x02;

        public static IReadOnlyList<FcBrushDesc> LoadBrushes(string levelName)
        {
            FcLevelLoader.EnsureLevelMounted(levelName);

            string path = $"levels/{levelName.ToLowerInvariant()}/brush.lst";
            if (!FcFileSystem.Exists(path))
            {
                Debug.LogWarning($"[FcBrushLoader] brush.lst not found at '{path}'");
                return Array.Empty<FcBrushDesc>();
            }

            try
            {
                byte[] bytes = FcFileSystem.ReadAllBytes(path);
                return Parse(bytes, levelName);
            }
            catch (Exception e)
            {
                Debug.LogError($"[FcBrushLoader] Failed to parse brush.lst for '{levelName}': {e.Message}");
                return Array.Empty<FcBrushDesc>();
            }
        }

        static IReadOnlyList<FcBrushDesc> Parse(byte[] bytes, string levelName)
        {
            using var ms = new MemoryStream(bytes);
            using var r  = new BinaryReader(ms, Encoding.ASCII, leaveOpen: true);

            // ── Header (11 bytes) ────────────────────────────────────────────────
            var sig = Encoding.ASCII.GetString(r.ReadBytes(3));
            if (sig != Signature)
                throw new InvalidDataException($"Bad signature: '{sig}' (expected '{Signature}')");

            int fileType = r.ReadInt32();
            int version  = r.ReadInt32();
            if (fileType != FileType || version != FileVersion)
                throw new InvalidDataException($"Unsupported brush.lst version: type={fileType} ver={version}");

            // ── Materials ────────────────────────────────────────────────────────
            int matCount = r.ReadInt32();
            var materials = new string[matCount];
            for (int i = 0; i < matCount; i++)
            {
                r.ReadInt32(); // size field
                materials[i] = ReadFixedString(r, 64);
            }

            // ── Geometries ───────────────────────────────────────────────────────
            int geomCount = r.ReadInt32();
            var geomPaths = new string[geomCount];
            var geomFlags = new int[geomCount];

            for (int i = 0; i < geomCount; i++)
            {
                r.ReadInt32(); // size
                string filename = ReadFixedString(r, 128);
                int flags  = r.ReadInt32();
                r.ReadBytes(24); // min + max bbox (2 × Vec3 = 24 bytes)

                geomPaths[i] = NormalizePath(filename);
                geomFlags[i] = flags;
            }

            // ── Brush instances ──────────────────────────────────────────────────
            int brushCount = r.ReadInt32();
            var result = new List<FcBrushDesc>(brushCount);

            for (int i = 0; i < brushCount; i++)
            {
                r.ReadInt32(); // size
                int id       = r.ReadInt32();
                int geomIdx  = r.ReadInt32();
                int matIdx   = r.ReadInt32();
                r.ReadInt32(); // flags (entity render flags, not geom flags)
                r.ReadInt32(); // mergeId

                var matrix = new float[12];
                for (int j = 0; j < 12; j++)
                    matrix[j] = r.ReadSingle();

                byte lodRatio    = r.ReadByte();
                r.ReadByte();   // ratioViewDist
                r.ReadByte();   // reserved1
                r.ReadByte();   // reserved2

                if (geomIdx < 0 || geomIdx >= geomCount)
                    continue;

                string path = geomPaths[geomIdx];
                if (string.IsNullOrEmpty(path))
                    continue;

                string matOverride = (matIdx >= 0 && matIdx < matCount)
                    ? materials[matIdx]
                    : null;

                result.Add(new FcBrushDesc
                {
                    Id               = id,
                    VirtualPath      = path,
                    MaterialOverride = matOverride,
                    Matrix           = matrix,
                    NoPhysics        = (geomFlags[geomIdx] & GeomFlagNoPhysics) != 0,
                    LodRatio         = lodRatio,
                });
            }

            Debug.Log($"[FcBrushLoader] '{levelName}': {result.Count} brushes " +
                      $"({geomCount} geoms, {matCount} materials)");
            return result;
        }

        static string ReadFixedString(BinaryReader r, int length)
        {
            byte[] buf = r.ReadBytes(length);
            // Find null terminator
            int end = Array.IndexOf(buf, (byte)0);
            int len = end >= 0 ? end : buf.Length;
            return Encoding.ASCII.GetString(buf, 0, len);
        }

        static string NormalizePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return string.Empty;
            return path.Replace('\\', '/').ToLowerInvariant().Trim('/');
        }
    }
}
