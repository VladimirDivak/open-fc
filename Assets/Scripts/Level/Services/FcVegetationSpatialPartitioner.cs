using System.Collections.Generic;
using UnityEngine;

namespace OpenFarCry.Level.Services
{
    public static class FcVegetationSpatialPartitioner
    {
        public readonly struct Cell
        {
            public readonly Vector2Int Key;
            public readonly Bounds Bounds;
            public readonly int[] InstanceIndices;

            public Cell(Vector2Int key, Bounds bounds, int[] instanceIndices)
            {
                Key = key;
                Bounds = bounds;
                InstanceIndices = instanceIndices;
            }
        }

        // Partitions positions into fixed-size XZ cells. Returns one Cell per non-empty bucket.
        // Bounds are computed from contained positions and expanded by (1, 4, 1) for frustum stability.
        public static Cell[] Partition(Vector3[] positions, float cellSize)
        {
            if (positions == null || positions.Length == 0)
                return System.Array.Empty<Cell>();

            float size = Mathf.Max(8f, cellSize);
            var buckets = new Dictionary<Vector2Int, List<int>>(capacity: positions.Length / 4 + 1);

            for (int i = 0; i < positions.Length; i++)
            {
                var pos = positions[i];
                int cx = Mathf.FloorToInt(pos.x / size);
                int cz = Mathf.FloorToInt(pos.z / size);
                var key = new Vector2Int(cx, cz);
                if (!buckets.TryGetValue(key, out var list))
                {
                    list = new List<int>(8);
                    buckets[key] = list;
                }
                list.Add(i);
            }

            var cells = new Cell[buckets.Count];
            int cellIndex = 0;
            foreach (var kv in buckets)
            {
                var list = kv.Value;
                var firstPos = positions[list[0]];
                var bounds = new Bounds(firstPos, Vector3.zero);
                for (int i = 1; i < list.Count; i++)
                    bounds.Encapsulate(positions[list[i]]);
                bounds.Expand(new Vector3(1f, 4f, 1f));

                cells[cellIndex++] = new Cell(kv.Key, bounds, list.ToArray());
            }

            return cells;
        }
    }
}
