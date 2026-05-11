using System;
using System.Collections.Generic;

namespace OpenFarCry.Level.Services
{
    // Shared runtime report store by level scope.
    // Allows multiple services (brush/entity) to write to one report instance.
    public static class FcLevelRuntimeReportRegistry
    {
        static readonly object Sync = new object();
        static readonly Dictionary<string, FcLevelLoadReport> ByScope =
            new Dictionary<string, FcLevelLoadReport>(StringComparer.Ordinal);

        public static FcLevelLoadReport GetOrCreate(string levelScopeId)
        {
            string key = string.IsNullOrWhiteSpace(levelScopeId) ? "<default>" : levelScopeId;
            lock (Sync)
            {
                if (!ByScope.TryGetValue(key, out var report))
                {
                    report = new FcLevelLoadReport
                    {
                        LevelName = key,
                        MissionName = "runtime"
                    };
                    ByScope[key] = report;
                }

                return report;
            }
        }

        public static void Release(string levelScopeId)
        {
            string key = string.IsNullOrWhiteSpace(levelScopeId) ? "<default>" : levelScopeId;
            lock (Sync)
                ByScope.Remove(key);
        }
    }
}
