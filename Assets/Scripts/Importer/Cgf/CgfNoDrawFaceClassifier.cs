using System.Collections.Generic;

namespace OpenFarCry.Importer.Cgf
{
    // Determines which face MatID values map to collision-only (nodraw / physics-proxy)
    // materials, so CgfMeshBuilder can route those faces into a collider mesh instead of
    // the visual mesh.
    //
    // A CGF face MatID is a GLOBAL leaf-material index: the position of the material among
    // all non-MULTI material chunks, in chunk-table order. This index space is uniform
    // across every node of the file — verified against merc_cover, fence_collision,
    // gunboatdamaged and hut_medium_grassroof_splitted — so one set covers the whole
    // combined mesh regardless of how many nodes or MULTI materials it has.
    public static class CgfNoDrawFaceClassifier
    {
        public static HashSet<int> Classify(CgfFile parsedFile)
        {
            var collisionMatIds = new HashSet<int>();
            var chunks = parsedFile?.MaterialChunks;
            if (chunks == null)
                return collisionMatIds;

            int leafIndex = 0;
            for (int i = 0; i < chunks.Count; i++)
            {
                var chunk = chunks[i];
                if (chunk == null || chunk.MtlType == CgfMtlType.Multi)
                    continue;

                if (CgfMaterialClassifier.IsCollisionOnly(chunk))
                    collisionMatIds.Add(leafIndex);
                leafIndex++;
            }

            return collisionMatIds;
        }
    }
}
