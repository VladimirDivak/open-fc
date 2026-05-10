using System;
using System.Collections.Generic;

namespace OpenFarCry.Importer.Cgf
{
    public static class CalParser
    {
        public readonly struct CalAnimEntry
        {
            public readonly string Alias;
            public readonly string VirtualPath;

            public CalAnimEntry(string alias, string virtualPath)
            {
                Alias = alias;
                VirtualPath = virtualPath;
            }
        }

        public static List<CalAnimEntry> Parse(string calText, string modelDir)
        {
            var output = new List<CalAnimEntry>();
            if (string.IsNullOrEmpty(calText))
                return output;

            string animDir = GetDefaultAnimDirectory(modelDir);
            var lines = calText.Replace('\r', '\n').Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                var line = lines[i]?.Trim();
                if (string.IsNullOrEmpty(line) || line.StartsWith("//", StringComparison.Ordinal))
                    continue;

                int eq = line.IndexOf('=');
                if (eq <= 0 || eq >= line.Length - 1)
                    continue;

                string left = line.Substring(0, eq).Trim();
                string right = line.Substring(eq + 1).Trim();
                if (string.IsNullOrEmpty(left) || string.IsNullOrEmpty(right))
                    continue;

                if (right[0] == '?')
                    continue;

                right = right.Replace('\\', '/').TrimStart('/', '\\');

                if (left.StartsWith("$", StringComparison.Ordinal))
                {
                    string directive = left.Substring(1);
                    if (directive.Equals("AnimationDir", StringComparison.OrdinalIgnoreCase) ||
                        directive.Equals("AnimDir", StringComparison.OrdinalIgnoreCase) ||
                        directive.Equals("AnimationDirectory", StringComparison.OrdinalIgnoreCase) ||
                        directive.Equals("AnimDirectory", StringComparison.OrdinalIgnoreCase))
                    {
                        animDir = $"{modelDir}/{right}".Replace('\\', '/').TrimEnd('/');
                    }

                    continue;
                }

                string resolved = $"{animDir}/{right}".Replace('\\', '/');
                output.Add(new CalAnimEntry(left, resolved));
            }

            return output;
        }

        static string GetDefaultAnimDirectory(string modelDir)
        {
            string dir = modelDir.Replace('\\', '/').Trim('/');
            if (string.IsNullOrEmpty(dir))
                return "animations";

            int cut = dir.LastIndexOf('/');
            if (cut < 0)
                return "animations";
            string oneUp = dir.Substring(0, cut);
            cut = oneUp.LastIndexOf('/');
            if (cut < 0)
                return "animations";
            string twoUp = oneUp.Substring(0, cut);
            return $"{twoUp}/animations";
        }
    }
}
