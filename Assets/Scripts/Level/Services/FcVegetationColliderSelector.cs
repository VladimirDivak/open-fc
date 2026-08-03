using System.Collections.Generic;
using UnityEngine;

namespace OpenFarCry.Level.Services
{
    public static class FcVegetationColliderSelector
    {
        public readonly struct Candidate
        {
            public readonly int InstanceIndex;
            public readonly int ProtoIndex;
            public readonly float SqrDistance;

            public Candidate(int instanceIndex, int protoIndex, float sqrDistance)
            {
                InstanceIndex = instanceIndex;
                ProtoIndex = protoIndex;
                SqrDistance = sqrDistance;
            }
        }

        // Convenience overload for callers (tests, one-off tools) that don't own a
        // reusable HashSet. Allocates; the hot path below does not.
        public static HashSet<int> Select(
            IReadOnlyList<Candidate> sortedCandidates,
            int[] perTypeBudgets,
            int globalBudget)
        {
            var result = new HashSet<int>();
            Select(sortedCandidates, perTypeBudgets, globalBudget, result);
            return result;
        }

        // Fills `destination` with the set of instance indices that should have active
        // colliders (Clear()'d first) instead of allocating a HashSet and a Dictionary
        // every call. This runs on a fixed interval (_colliderUpdateInterval, default 5x/s)
        // against potentially large candidate lists, so the per-tick allocation adds up.
        // sortedCandidates must be sorted by SqrDistance ascending (nearest first).
        // perTypeBudgets[protoIndex] = max colliders for that prototype type.
        // globalBudget = total cap across all types.
        public static void Select(
            IReadOnlyList<Candidate> sortedCandidates,
            int[] perTypeBudgets,
            int globalBudget,
            HashSet<int> destination)
        {
            destination.Clear();
            s_perTypeCountsScratch.Clear();

            int maxGlobal = Mathf.Max(1, globalBudget);
            for (int i = 0; i < sortedCandidates.Count; i++)
            {
                if (destination.Count >= maxGlobal)
                    break;

                var candidate = sortedCandidates[i];
                int pi = candidate.ProtoIndex;

                int perBudget = pi >= 0 && perTypeBudgets != null && pi < perTypeBudgets.Length
                    ? Mathf.Max(1, perTypeBudgets[pi])
                    : 1;

                s_perTypeCountsScratch.TryGetValue(pi, out int current);
                if (current >= perBudget)
                    continue;

                if (destination.Add(candidate.InstanceIndex))
                    s_perTypeCountsScratch[pi] = current + 1;
            }
        }

        // Reused across calls on the main thread only (Unity's Update/collider-refresh
        // path is single-threaded) — cleared at the top of every Select call.
        static readonly Dictionary<int, int> s_perTypeCountsScratch = new Dictionary<int, int>();
    }
}
