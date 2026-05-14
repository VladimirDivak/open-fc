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

        // Returns the set of instance indices that should have active colliders.
        // sortedCandidates must be sorted by SqrDistance ascending (nearest first).
        // perTypeBudgets[protoIndex] = max colliders for that prototype type.
        // globalBudget = total cap across all types.
        public static HashSet<int> Select(
            IReadOnlyList<Candidate> sortedCandidates,
            int[] perTypeBudgets,
            int globalBudget)
        {
            var result = new HashSet<int>();
            var perTypeCounts = new Dictionary<int, int>();

            int maxGlobal = Mathf.Max(1, globalBudget);
            for (int i = 0; i < sortedCandidates.Count; i++)
            {
                if (result.Count >= maxGlobal)
                    break;

                var candidate = sortedCandidates[i];
                int pi = candidate.ProtoIndex;

                int perBudget = pi >= 0 && perTypeBudgets != null && pi < perTypeBudgets.Length
                    ? Mathf.Max(1, perTypeBudgets[pi])
                    : 1;

                perTypeCounts.TryGetValue(pi, out int current);
                if (current >= perBudget)
                    continue;

                if (result.Add(candidate.InstanceIndex))
                    perTypeCounts[pi] = current + 1;
            }

            return result;
        }
    }
}
