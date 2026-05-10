using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using OpenFarCry.FileSystem;
using OpenFarCry.Importer.Cgf;
using OpenFarCry.Importer.Texture;
using UnityEditor;
using UnityEngine;

namespace OpenFarCry.Importer.Editor
{
    static class TextureUsageAuditTool
    {
        [MenuItem("OpenFarCry/Texture Usage Audit/Run Audit (All CGF)")]
        public static void RunAll()
        {
            if (FcFileSystem.MountedCount == 0)
            {
                Debug.LogWarning("[TextureUsageAudit] VFS has no mounted PAKs. Check FcFileSystemSettings in Resources/.");
                return;
            }

            var report = BuildReport();
            string markdown = BuildMarkdownReport(report);
            string csv = BuildCsvReport(report);

            string markdownPath = Path.GetFullPath("TextureUsageAuditReport.md");
            string csvPath = Path.GetFullPath("TextureUsageAuditReport.csv");

            File.WriteAllText(markdownPath, markdown, Encoding.UTF8);
            File.WriteAllText(csvPath, csv, Encoding.UTF8);

            Debug.Log($"[TextureUsageAudit] Done. Models={report.ModelCount}, refs={report.TextureReferenceCount}, resolved={report.ResolvedCount}, missing={report.MissingCount}, unsupported={report.UnsupportedCount}.");
            Debug.Log($"[TextureUsageAudit] Reports written:\n- {markdownPath}\n- {csvPath}");
            AssetDatabase.Refresh();
        }

        static TextureUsageAuditReport BuildReport()
        {
            var report = new TextureUsageAuditReport();

            var cgfPaths = new List<string>();
            foreach (var path in FcFileSystem.GetEntries(string.Empty))
            {
                if (CgfResourceImportService.Instance.IsSupportedVirtualPath(path))
                    cgfPaths.Add(ImportAssetPaths.NormalizeVirtualPath(path));
            }
            cgfPaths.Sort(StringComparer.OrdinalIgnoreCase);

            report.ModelCount = cgfPaths.Count;

            try
            {
                for (int i = 0; i < cgfPaths.Count; i++)
                {
                    string modelPath = cgfPaths[i];
                    EditorUtility.DisplayProgressBar(
                        "Texture Usage Audit",
                        modelPath,
                        cgfPaths.Count == 0 ? 1f : (float)i / cgfPaths.Count);

                    AuditModel(report, modelPath);
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            return report;
        }

        static void AuditModel(TextureUsageAuditReport report, string modelPath)
        {
            byte[] bytes;
            try
            {
                bytes = CgfResourceImportService.Instance.LoadProjectAssetSourceBytes(modelPath);
            }
            catch (Exception e)
            {
                report.ParseFailureCount++;
                report.AddParseFailure(modelPath, e.Message);
                return;
            }

            CgfFile file;
            try
            {
                file = CgfParser.Parse(bytes);
                file.SourceVirtualPath = modelPath;
                report.ParsedModelCount++;
            }
            catch (Exception e)
            {
                report.ParseFailureCount++;
                report.AddParseFailure(modelPath, e.Message);
                return;
            }

            if (file.LeafMaterials == null || file.LeafMaterials.Count == 0)
                return;

            for (int i = 0; i < file.LeafMaterials.Count; i++)
            {
                var mat = file.LeafMaterials[i];
                report.MaterialCount++;

                CollectTextureReference(report, file, modelPath, mat, "diffuse", mat.DiffuseTextureName);
                CollectTextureReference(report, file, modelPath, mat, "normal", mat.NormalTextureName);
                CollectTextureReference(report, file, modelPath, mat, "specular", mat.SpecularTextureName);
                CollectTextureReference(report, file, modelPath, mat, "opacity", mat.OpacityTextureName);
            }
        }

        static void CollectTextureReference(
            TextureUsageAuditReport report,
            CgfFile file,
            string modelPath,
            CgfMaterialChunk material,
            string slot,
            string textureName)
        {
            string normalizedTextureName = CgfTexturePathResolver.NormalizeTextureName(textureName);
            if (string.IsNullOrEmpty(normalizedTextureName))
                return;

            report.TextureReferenceCount++;

            var supportedCandidates = new List<string>();
            foreach (var candidate in CgfTexturePathResolver.BuildTexturePathCandidates(file, normalizedTextureName))
            {
                if (TextureImportService.IsSupportedVirtualPath(candidate))
                    supportedCandidates.Add(candidate);
            }

            if (supportedCandidates.Count == 0)
            {
                report.UnsupportedCount++;
                report.AddRecord(new TextureUsageAuditRecord(
                    modelPath,
                    materialName: material?.Name ?? "",
                    materialChunkId: material?.ChunkID ?? -1,
                    slot: slot,
                    sourceTextureName: normalizedTextureName,
                    resolvedVirtualPath: string.Empty,
                    extension: string.Empty,
                    ddsFormatTag: string.Empty,
                    status: "unsupported_ref",
                    details: "no supported extension candidate (.dds/.bmp/.tga)"));
                return;
            }

            string resolvedVirtualPath = null;
            for (int i = 0; i < supportedCandidates.Count; i++)
            {
                string candidate = supportedCandidates[i];
                if (FcFileSystem.Exists(candidate))
                {
                    resolvedVirtualPath = candidate;
                    break;
                }
            }

            if (string.IsNullOrEmpty(resolvedVirtualPath))
            {
                report.MissingCount++;
                report.AddRecord(new TextureUsageAuditRecord(
                    modelPath,
                    materialName: material?.Name ?? "",
                    materialChunkId: material?.ChunkID ?? -1,
                    slot: slot,
                    sourceTextureName: normalizedTextureName,
                    resolvedVirtualPath: supportedCandidates[0],
                    extension: Path.GetExtension(supportedCandidates[0]).ToLowerInvariant(),
                    ddsFormatTag: string.Empty,
                    status: "missing",
                    details: "all supported candidates are missing in VFS"));
                return;
            }

            string ext = Path.GetExtension(resolvedVirtualPath).ToLowerInvariant();
            report.ResolvedCount++;
            report.IncrementExtensionCount(ext);

            string ddsFormatTag = string.Empty;
            string details = string.Empty;
            if (ext == ".dds")
            {
                try
                {
                    byte[] textureBytes = TextureImportService.ReadSourceBytes(resolvedVirtualPath);
                    if (TextureImportService.TryReadDdsFormatTag(textureBytes, out var tag, out var error))
                    {
                        ddsFormatTag = tag;
                        report.IncrementDdsTagCount(tag);
                    }
                    else
                    {
                        ddsFormatTag = "parse_error";
                        details = error ?? "failed to parse DDS header";
                        report.IncrementDdsTagCount(ddsFormatTag);
                    }
                }
                catch (Exception e)
                {
                    ddsFormatTag = "read_error";
                    details = e.Message;
                    report.IncrementDdsTagCount(ddsFormatTag);
                }
            }

            report.AddRecord(new TextureUsageAuditRecord(
                modelPath,
                materialName: material?.Name ?? "",
                materialChunkId: material?.ChunkID ?? -1,
                slot: slot,
                sourceTextureName: normalizedTextureName,
                resolvedVirtualPath: resolvedVirtualPath,
                extension: ext,
                ddsFormatTag: ddsFormatTag,
                status: "resolved",
                details: details));
        }

        static string BuildMarkdownReport(TextureUsageAuditReport report)
        {
            var sb = new StringBuilder(16 * 1024);
            sb.AppendLine("# Texture Usage Audit Report");
            sb.AppendLine();
            sb.AppendLine($"Generated at: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine();
            sb.AppendLine("## Summary");
            sb.AppendLine($"- Models found: {report.ModelCount}");
            sb.AppendLine($"- Models parsed: {report.ParsedModelCount}");
            sb.AppendLine($"- Parse failures: {report.ParseFailureCount}");
            sb.AppendLine($"- Leaf materials scanned: {report.MaterialCount}");
            sb.AppendLine($"- Texture refs scanned: {report.TextureReferenceCount}");
            sb.AppendLine($"- Resolved refs: {report.ResolvedCount}");
            sb.AppendLine($"- Missing refs: {report.MissingCount}");
            sb.AppendLine($"- Unsupported refs: {report.UnsupportedCount}");
            sb.AppendLine();

            sb.AppendLine("## Resolved By Extension");
            if (report.ResolvedByExtension.Count == 0)
            {
                sb.AppendLine("- (none)");
            }
            else
            {
                foreach (var pair in report.ResolvedByExtension.OrderByDescending(p => p.Value).ThenBy(p => p.Key, StringComparer.OrdinalIgnoreCase))
                    sb.AppendLine($"- {pair.Key}: {pair.Value}");
            }
            sb.AppendLine();

            sb.AppendLine("## DDS Tags");
            if (report.ResolvedDdsByTag.Count == 0)
            {
                sb.AppendLine("- (none)");
            }
            else
            {
                foreach (var pair in report.ResolvedDdsByTag.OrderByDescending(p => p.Value).ThenBy(p => p.Key, StringComparer.OrdinalIgnoreCase))
                    sb.AppendLine($"- {pair.Key}: {pair.Value}");
            }
            sb.AppendLine();

            sb.AppendLine("## Parse Failures (Top 30)");
            if (report.ParseFailures.Count == 0)
            {
                sb.AppendLine("- (none)");
            }
            else
            {
                for (int i = 0; i < Mathf.Min(30, report.ParseFailures.Count); i++)
                    sb.AppendLine($"- {report.ParseFailures[i]}");
            }
            sb.AppendLine();

            sb.AppendLine("## Missing References (Top 100)");
            AppendRecords(sb, report.Records, status: "missing", max: 100);
            sb.AppendLine();

            sb.AppendLine("## Unsupported References (Top 100)");
            AppendRecords(sb, report.Records, status: "unsupported_ref", max: 100);
            sb.AppendLine();

            return sb.ToString();
        }

        static void AppendRecords(StringBuilder sb, List<TextureUsageAuditRecord> records, string status, int max)
        {
            int written = 0;
            for (int i = 0; i < records.Count && written < max; i++)
            {
                var row = records[i];
                if (!string.Equals(row.Status, status, StringComparison.Ordinal))
                    continue;

                sb.AppendLine($"- model={row.ModelPath}; material={row.MaterialName}#{row.MaterialChunkId}; slot={row.Slot}; source={row.SourceTextureName}; resolved={row.ResolvedVirtualPath}; ext={row.Extension}; dds={row.DdsFormatTag}; details={row.Details}");
                written++;
            }

            if (written == 0)
                sb.AppendLine("- (none)");
        }

        static string BuildCsvReport(TextureUsageAuditReport report)
        {
            var sb = new StringBuilder(64 * 1024);
            sb.AppendLine("model_path,material_name,material_chunk_id,slot,source_texture_name,resolved_virtual_path,extension,dds_format_tag,status,details");
            for (int i = 0; i < report.Records.Count; i++)
            {
                var r = report.Records[i];
                sb.AppendLine(string.Join(",",
                    Csv(r.ModelPath),
                    Csv(r.MaterialName),
                    Csv(r.MaterialChunkId.ToString()),
                    Csv(r.Slot),
                    Csv(r.SourceTextureName),
                    Csv(r.ResolvedVirtualPath),
                    Csv(r.Extension),
                    Csv(r.DdsFormatTag),
                    Csv(r.Status),
                    Csv(r.Details)));
            }

            return sb.ToString();
        }

        static string Csv(string value)
        {
            string v = value ?? string.Empty;
            bool hasSpecial = v.IndexOfAny(new[] { ',', '"', '\n', '\r' }) >= 0;
            if (!hasSpecial)
                return v;

            return '"' + v.Replace("\"", "\"\"") + '"';
        }

        sealed class TextureUsageAuditReport
        {
            public int ModelCount;
            public int ParsedModelCount;
            public int ParseFailureCount;
            public int MaterialCount;
            public int TextureReferenceCount;
            public int ResolvedCount;
            public int MissingCount;
            public int UnsupportedCount;

            public readonly Dictionary<string, int> ResolvedByExtension = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            public readonly Dictionary<string, int> ResolvedDdsByTag = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            public readonly List<string> ParseFailures = new List<string>(128);
            public readonly List<TextureUsageAuditRecord> Records = new List<TextureUsageAuditRecord>(4096);

            public void IncrementExtensionCount(string extension)
            {
                string key = string.IsNullOrWhiteSpace(extension) ? "(none)" : extension.ToLowerInvariant();
                if (ResolvedByExtension.TryGetValue(key, out int count))
                    ResolvedByExtension[key] = count + 1;
                else
                    ResolvedByExtension[key] = 1;
            }

            public void IncrementDdsTagCount(string tag)
            {
                string key = string.IsNullOrWhiteSpace(tag) ? "unknown" : tag.ToLowerInvariant();
                if (ResolvedDdsByTag.TryGetValue(key, out int count))
                    ResolvedDdsByTag[key] = count + 1;
                else
                    ResolvedDdsByTag[key] = 1;
            }

            public void AddParseFailure(string modelPath, string reason)
            {
                ParseFailures.Add($"{modelPath}: {reason}");
            }

            public void AddRecord(TextureUsageAuditRecord record)
            {
                Records.Add(record);
            }
        }

        readonly struct TextureUsageAuditRecord
        {
            public readonly string ModelPath;
            public readonly string MaterialName;
            public readonly int MaterialChunkId;
            public readonly string Slot;
            public readonly string SourceTextureName;
            public readonly string ResolvedVirtualPath;
            public readonly string Extension;
            public readonly string DdsFormatTag;
            public readonly string Status;
            public readonly string Details;

            public TextureUsageAuditRecord(
                string modelPath,
                string materialName,
                int materialChunkId,
                string slot,
                string sourceTextureName,
                string resolvedVirtualPath,
                string extension,
                string ddsFormatTag,
                string status,
                string details)
            {
                ModelPath = modelPath;
                MaterialName = materialName;
                MaterialChunkId = materialChunkId;
                Slot = slot;
                SourceTextureName = sourceTextureName;
                ResolvedVirtualPath = resolvedVirtualPath;
                Extension = extension;
                DdsFormatTag = ddsFormatTag;
                Status = status;
                Details = details;
            }
        }
    }
}
