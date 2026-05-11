using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace OpenFarCry.Level.Services
{
    // Timing accumulator for a single level build or load session.
    // Phase 1: editor build timing + brush runtime timing.
    // Phase 2+: extended with entity/mesh/animation per-request stats.
    public sealed class FcLevelLoadReport
    {
        public string LevelName;
        public string MissionName;

        readonly List<(string phase, double ms)> _phases = new();

        // Runtime brush load stats (filled by FcBrushLoadService).
        public int BrushesLoaded;
        public int BrushesFailed;
        public double BrushTotalMs;
        public double BrushSlowestMs;
        double _brushFastestMs = double.MaxValue;

        public void RecordPhase(string phase, double ms) =>
            _phases.Add((phase, ms));

        public void RecordBrushLoad(bool success, double ms)
        {
            if (success)
            {
                BrushesLoaded++;
                BrushTotalMs += ms;
                if (ms < _brushFastestMs) _brushFastestMs = ms;
                if (ms > BrushSlowestMs) BrushSlowestMs = ms;
            }
            else
            {
                BrushesFailed++;
            }
        }

        public void LogEditorBuild()
        {
            var sb = new StringBuilder();
            sb.AppendLine($"[FcLevel] Editor build — {LevelName}/{MissionName}");
            double total = 0;
            foreach (var (phase, ms) in _phases)
            {
                sb.AppendLine($"  {phase,-36} {ms,8:F1} ms");
                total += ms;
            }
            sb.AppendLine($"  {"TotalEditorBuild",-36} {total,8:F1} ms");
            Debug.Log(sb.ToString());
        }

        public void LogRuntimeBrushes()
        {
            if (BrushesLoaded == 0 && BrushesFailed == 0)
                return;

            double avg = BrushesLoaded > 0 ? BrushTotalMs / BrushesLoaded : 0;
            double fastest = BrushesLoaded > 0 ? _brushFastestMs : 0;
            Debug.Log(
                $"[FcLevel] Brush load — {LevelName}: " +
                $"ok={BrushesLoaded} fail={BrushesFailed} " +
                $"total={BrushTotalMs:F0}ms avg={avg:F1}ms " +
                $"min={fastest:F1}ms max={BrushSlowestMs:F1}ms");
        }
    }
}
