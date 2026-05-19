using System;
using System.Collections.Generic;
using OpenFarCry.Importer.Cgf;
using UnityEngine;
using UnityEngine.Rendering;

namespace OpenFarCry.Level.Services
{
    public static class FcBrushGeometryPostProcessor
    {
        // v5: planar templdecalmodulate submeshes stripped from the visual mesh and
        //     re-spawned as URP DecalProjectors (FcBrushDecalProjectorBuilder).
        // v4: nodraw/proxy faces split into BuildResult.ColliderMesh at mesh-build time.
        public const int CacheFormatVersion = 5;
        static readonly int PropCull = Shader.PropertyToID("_Cull");
        static readonly int PropSrcBlend = Shader.PropertyToID("_SrcBlend");

        // A planar decal submesh extracted from a brush mesh, in visual-root local space.
        // Re-projected onto surrounding geometry by FcBrushDecalProjectorBuilder.
        public readonly struct DecalQuad
        {
            public readonly Vector3 Center;   // rect center, visual-root local space
            public readonly Vector3 Normal;   // mesh face normal (points toward viewer)
            public readonly Vector3 AxisU;    // unit, world direction of increasing texture U
            public readonly Vector3 AxisV;    // unit, world direction of increasing texture V
            public readonly float Width;      // footprint along AxisU
            public readonly float Height;     // footprint along AxisV
            public readonly Material SourceMaterial; // brush submesh material, pre-override
            public readonly int MaterialId;          // CGF submesh MatID, for level override resolution

            public DecalQuad(Vector3 center, Vector3 normal, Vector3 axisU, Vector3 axisV,
                float width, float height, Material sourceMaterial, int materialId)
            {
                Center = center;
                Normal = normal;
                AxisU = axisU;
                AxisV = axisV;
                Width = width;
                Height = height;
                SourceMaterial = sourceMaterial;
                MaterialId = materialId;
            }
        }

        public readonly struct Artifacts
        {
            public readonly Mesh PhysicsColliderMesh;
            public readonly Mesh VisualFilteredMesh;
            public readonly int[] VisualSubmeshMaterialIds;
            public readonly DecalQuad[] DecalQuads;

            public Artifacts(Mesh physicsColliderMesh, Mesh visualFilteredMesh,
                int[] visualSubmeshMaterialIds, DecalQuad[] decalQuads)
            {
                PhysicsColliderMesh = physicsColliderMesh;
                VisualFilteredMesh = visualFilteredMesh;
                VisualSubmeshMaterialIds = visualSubmeshMaterialIds;
                DecalQuads = decalQuads;
            }
        }

        public static Artifacts ApplyRuntimeParity(
            GameObject visualRoot,
            BuildResult buildResult,
            CgfFile parsedFile,
            bool addPhysicsCollider,
            float importScale)
        {
            if (visualRoot == null || buildResult == null || parsedFile == null)
                return default;

            var visualSubmeshMaterialIds = buildResult.SubmeshMaterialIds != null
                ? (int[])buildResult.SubmeshMaterialIds.Clone()
                : null;

            // Extract planar decal submeshes from the original mesh before stripping; the
            // same submesh indices are then removed from the visual mesh below.
            var decalQuads = ExtractPlanarDecals(
                visualRoot, buildResult, parsedFile, out var decalStripSubmeshes);

            Mesh visualFilteredMesh = StripProxySubmeshesFromVisual(
                visualRoot,
                buildResult,
                parsedFile,
                ref visualSubmeshMaterialIds,
                decalStripSubmeshes);
            Mesh physicsColliderMesh = null;

            if (addPhysicsCollider && buildResult.Mesh != null)
            {
                // Pick a collision mesh: split-off ColliderMesh (cache-owned), else a
                // parsed-face collider, else the visual mesh itself. physicsColliderMesh is
                // non-null only for the brush-owned middle case so OnDestroy frees just that.
                Mesh colliderCandidate = null;
                if (MeshHasTriangles(buildResult.ColliderMesh))
                {
                    colliderCandidate = buildResult.ColliderMesh;
                }
                else if (TryBuildPhysicsColliderMesh(parsedFile, importScale, out physicsColliderMesh))
                {
                    colliderCandidate = physicsColliderMesh;
                }
                else
                {
                    var mf = visualRoot.GetComponent<MeshFilter>();
                    var visualMesh = mf != null ? mf.sharedMesh : buildResult.Mesh;
                    if (MeshHasTriangles(visualMesh))
                        colliderCandidate = visualMesh;
                }

                // Skip the MeshCollider entirely when there is no usable geometry — a brush
                // with an empty visual mesh and no collision faces must not get a collider
                // (Unity rejects a mesh with no non-degenerate triangle).
                if (MeshHasTriangles(colliderCandidate))
                {
                    var col = visualRoot.GetComponent<MeshCollider>();
                    if (col == null)
                        col = visualRoot.AddComponent<MeshCollider>();
                    col.sharedMesh = colliderCandidate;
                }
            }

            DisableBackfaceCulling(visualRoot);
            StampCacheMetadata(visualRoot, brushRuntimeParity: true, visualSubmeshMaterialIds);
            return new Artifacts(
                physicsColliderMesh,
                visualFilteredMesh,
                visualSubmeshMaterialIds,
                decalQuads != null && decalQuads.Count > 0 ? decalQuads.ToArray() : null);
        }

        public static void DisableBackfaceCulling(GameObject root)
        {
            var renderers = root != null ? root.GetComponentsInChildren<MeshRenderer>(true) : null;
            if (renderers == null)
                return;

            for (int r = 0; r < renderers.Length; r++)
            {
                var materials = renderers[r].sharedMaterials;
                for (int i = 0; i < materials.Length; i++)
                {
                    var mat = materials[i];
                    if (mat == null || !mat.HasProperty(PropCull))
                        continue;
                    // Skip multiply-blend materials (Cry templdecalmodulate): they must
                    // keep Render Face = Front so the decal does not appear through the
                    // back side of its host surface.
                    if (mat.HasProperty(PropSrcBlend) &&
                        (int)mat.GetFloat(PropSrcBlend) == (int)BlendMode.DstColor)
                        continue;
                    mat.SetFloat(PropCull, (float)CullMode.Off);
                }
            }
        }

        public static void StampCacheMetadata(
            GameObject root,
            bool brushRuntimeParity,
            int[] submeshMaterialIds = null)
        {
            if (root == null)
                return;

            var meta = root.GetComponent<FcCachedGeometryMetadata>();
            if (meta == null)
                meta = root.AddComponent<FcCachedGeometryMetadata>();

            meta.SetMetadata(CacheFormatVersion, brushRuntimeParity, submeshMaterialIds);
        }

        public static bool IsBrushRuntimeParityCompatible(GameObject prefabRoot)
        {
            if (prefabRoot == null)
                return false;

            var meta = prefabRoot.GetComponent<FcCachedGeometryMetadata>();
            if (meta == null)
                return false;

            return meta.CacheFormatVersion >= CacheFormatVersion && meta.BrushRuntimeParity;
        }

        // Strips collision-only (proxy/no-draw) submeshes from a visual GameObject's
        // MeshFilter/MeshRenderer. Public so LOD children get the same treatment as LOD0.
        // Returns the brush-owned filtered Mesh (caller must Destroy it), or null when no
        // stripping was needed.
        public static Mesh StripProxySubmeshesFromVisual(
            GameObject visualRoot,
            BuildResult buildResult,
            CgfFile parsedFile,
            ref int[] visualSubmeshMaterialIds,
            HashSet<int> extraStripSubmeshIndices = null)
        {
            if (visualRoot == null || buildResult?.SubmeshMaterialIds == null)
                return null;

            if (!TryResolveRootMaterial(parsedFile, out var rootMat) || rootMat == null)
                return null;

            var proxyMatIds = BuildProxyMaterialIds(parsedFile, rootMat);
            if (proxyMatIds == null)
            {
                var noDrawMr = visualRoot.GetComponent<MeshRenderer>();
                if (noDrawMr != null)
                    noDrawMr.enabled = false;
                return null;
            }

            bool hasExtraStrip = extraStripSubmeshIndices != null && extraStripSubmeshIndices.Count > 0;
            if (proxyMatIds.Count == 0 && !hasExtraStrip)
                return null;

            var mf = visualRoot.GetComponent<MeshFilter>();
            var mr = visualRoot.GetComponent<MeshRenderer>();
            var sourceMesh = mf != null ? mf.sharedMesh : null;
            if (mf == null || mr == null || sourceMesh == null)
                return null;

            int subCount = sourceMesh.subMeshCount;
            if (subCount <= 0)
                return null;

            int[] matIds = buildResult.SubmeshMaterialIds;
            if (matIds.Length != subCount)
                return null;

            var keep = new List<int>(subCount);
            for (int i = 0; i < subCount; i++)
            {
                if (proxyMatIds.Contains(matIds[i]))
                    continue;
                if (hasExtraStrip && extraStripSubmeshIndices.Contains(i))
                    continue;
                keep.Add(i);
            }

            if (keep.Count == subCount)
                return null;

            if (keep.Count == 0)
            {
                mr.enabled = false;
                visualSubmeshMaterialIds = Array.Empty<int>();
                return null;
            }

            var filtered = BuildCompactedMesh(sourceMesh, keep);

            mf.sharedMesh = filtered;

            var mats = mr.sharedMaterials;
            var newMats = new Material[keep.Count];
            var filteredMatIds = new int[keep.Count];
            for (int i = 0; i < keep.Count; i++)
            {
                int src = keep[i];
                newMats[i] = src >= 0 && src < mats.Length ? mats[src] : null;
                filteredMatIds[i] = matIds[src];
            }
            mr.sharedMaterials = newMats;
            visualSubmeshMaterialIds = filteredMatIds;
            return filtered;
        }

        // Builds a new mesh containing only the submeshes in `keepSubmeshIndices`, with the vertex
        // buffer compacted to exclude vertices that are only referenced by the stripped submeshes.
        static Mesh BuildCompactedMesh(Mesh source, List<int> keepSubmeshIndices)
        {
            // Gather triangles for kept submeshes and mark which source vertices are needed.
            var keptTris = new int[keepSubmeshIndices.Count][];
            var usedVertex = new bool[source.vertexCount];
            for (int i = 0; i < keepSubmeshIndices.Count; i++)
            {
                var tris = source.GetTriangles(keepSubmeshIndices[i]);
                keptTris[i] = tris;
                for (int t = 0; t < tris.Length; t++)
                    usedVertex[tris[t]] = true;
            }

            // Build old→new vertex index mapping.
            var oldToNew = new int[source.vertexCount];
            int newVertCount = 0;
            for (int i = 0; i < oldToNew.Length; i++)
                oldToNew[i] = usedVertex[i] ? newVertCount++ : -1;

            // Extract only the used vertex data.
            var srcPositions = source.vertices;
            var srcNormals   = source.normals;
            var srcUV0       = source.uv;

            var newPositions = new Vector3[newVertCount];
            var newNormals   = srcNormals.Length == source.vertexCount ? new Vector3[newVertCount] : null;
            var newUV0       = srcUV0.Length    == source.vertexCount ? new Vector2[newVertCount] : null;

            for (int i = 0; i < source.vertexCount; i++)
            {
                int n = oldToNew[i];
                if (n < 0) continue;
                newPositions[n] = srcPositions[i];
                if (newNormals != null) newNormals[n] = srcNormals[i];
                if (newUV0    != null) newUV0[n]     = srcUV0[i];
            }

            // Remap triangle indices and assemble the filtered mesh.
            var filtered = new Mesh { name = source.name + "_NoProxyVisual" };
            if (newVertCount > 65535) filtered.indexFormat = IndexFormat.UInt32;
            filtered.SetVertices(newPositions);
            if (newNormals != null) filtered.SetNormals(newNormals);
            if (newUV0    != null) filtered.SetUVs(0, newUV0);

            filtered.subMeshCount = keepSubmeshIndices.Count;
            for (int i = 0; i < keepSubmeshIndices.Count; i++)
            {
                var orig     = keptTris[i];
                var remapped = new int[orig.Length];
                for (int j = 0; j < orig.Length; j++)
                    remapped[j] = oldToNew[orig[j]];
                filtered.SetTriangles(remapped, i);
            }
            filtered.RecalculateBounds();
            return filtered;
        }

        static bool TryBuildPhysicsColliderMesh(CgfFile parsedFile, float importScale, out Mesh mesh)
        {
            if (TryBuildFromProxyNodeMesh(parsedFile, importScale, out mesh))
                return true;

            if (TryBuildFromNoDrawFaces(parsedFile, importScale, out mesh))
                return true;

            if (TryBuildFromBoneMesh(parsedFile, importScale, out mesh))
                return true;

            mesh = null;
            return false;
        }

        static bool TryBuildFromProxyNodeMesh(CgfFile parsedFile, float importScale, out Mesh mesh)
        {
            mesh = null;
            if (parsedFile?.NodeChunks == null) return false;

            for (int i = 0; i < parsedFile.NodeChunks.Count; i++)
            {
                var node = parsedFile.NodeChunks[i];
                if (node.Name == null || node.Name.IndexOf("proxy", StringComparison.OrdinalIgnoreCase) < 0)
                    continue;
                if (!parsedFile.MeshByChunkID.TryGetValue(node.ObjectID, out var proxyMesh))
                    continue;
                if (!proxyMesh.Vertices.IsCreated || proxyMesh.Vertices.Length == 0 ||
                    !proxyMesh.Faces.IsCreated || proxyMesh.Faces.Length == 0)
                    continue;
                return BuildColliderMeshFromFaces(null, proxyMesh, importScale, null, "BrushProxyNodeCollider", out mesh);
            }
            return false;
        }

        static bool TryBuildFromNoDrawFaces(CgfFile parsedFile, float importScale, out Mesh mesh)
        {
            mesh = null;
            var meshChunk = parsedFile?.MeshChunk;
            if (meshChunk == null || !meshChunk.Vertices.IsCreated || !meshChunk.Faces.IsCreated || meshChunk.Faces.Length == 0)
                return false;

            if (!TryResolveRootMaterial(parsedFile, out var rootMat) || rootMat == null)
                return false;

            var proxyMatIds = BuildProxyMaterialIds(parsedFile, rootMat);
            if (proxyMatIds == null)
            {
                return BuildColliderMeshFromFaces(parsedFile, meshChunk, importScale, allowedMatIds: null, "BrushNoDrawCollider", out mesh);
            }

            if (proxyMatIds.Count == 0)
                return false;

            if (BuildColliderMeshFromFaces(parsedFile, meshChunk, importScale, proxyMatIds, "BrushNoDrawCollider", out mesh))
                return true;

            var shifted = new HashSet<int>();
            foreach (int id in proxyMatIds)
                shifted.Add(id + 1);
            return BuildColliderMeshFromFaces(parsedFile, meshChunk, importScale, shifted, "BrushNoDrawCollider", out mesh);
        }

        static bool TryBuildFromBoneMesh(CgfFile parsedFile, float importScale, out Mesh mesh)
        {
            mesh = null;
            var chunks = parsedFile?.BoneMeshChunks;
            if (chunks == null || chunks.Count == 0)
                return false;

            CgfMeshChunk phys = null;
            for (int i = 0; i < chunks.Count; i++)
            {
                var candidate = chunks[i]?.Mesh;
                if (candidate != null &&
                    candidate.Vertices.IsCreated &&
                    candidate.Vertices.Length > 0 &&
                    candidate.Faces.IsCreated &&
                    candidate.Faces.Length > 0)
                {
                    phys = candidate;
                    break;
                }
            }

            if (phys == null)
                return false;

            return BuildColliderMeshFromFaces(null, phys, importScale, allowedMatIds: null, "BrushPhysicsCollider", out mesh);
        }

        static bool BuildColliderMeshFromFaces(
            CgfFile parsedFile,
            CgfMeshChunk source,
            float importScale,
            HashSet<int> allowedMatIds,
            string meshName,
            out Mesh mesh)
        {
            mesh = null;
            if (source == null || !source.Vertices.IsCreated || !source.Faces.IsCreated || source.Faces.Length == 0)
                return false;

            var nodeTransform = Matrix4x4.identity;
            if (parsedFile != null && source == parsedFile.MeshChunk)
            {
                var rawNodeTransform = CgfMeshBuilder.BuildStaticNodeTransform(parsedFile, source.ChunkID);
                nodeTransform = CryTransformConversion.NodeMatrixInImporterSpace(rawNodeTransform, importScale);
            }

            var vertices = new List<Vector3>(source.Vertices.Length);
            for (int i = 0; i < source.Vertices.Length; i++)
            {
                var v = source.Vertices[i];
                var pos = CryTransformConversion.PositionInImporterSpace(
                    new Vector3(v.PX, v.PY, v.PZ),
                    importScale);
                vertices.Add(nodeTransform.MultiplyPoint3x4(pos));
            }

            var triangles = new List<int>(source.Faces.Length * 3);
            for (int i = 0; i < source.Faces.Length; i++)
            {
                var f = source.Faces[i];
                if (allowedMatIds != null && !allowedMatIds.Contains(f.MatID))
                    continue;
                if (f.V0 < 0 || f.V1 < 0 || f.V2 < 0 ||
                    f.V0 >= vertices.Count || f.V1 >= vertices.Count || f.V2 >= vertices.Count)
                    continue;
                triangles.Add(f.V0);
                triangles.Add(f.V1);
                triangles.Add(f.V2);
            }

            if (triangles.Count < 3)
                return false;

            mesh = new Mesh { name = meshName };
            if (vertices.Count > 65535)
                mesh.indexFormat = IndexFormat.UInt32;
            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0, true);
            mesh.RecalculateBounds();
            return true;
        }

        static bool TryResolveRootMaterial(CgfFile parsedFile, out CgfMaterialChunk rootMat)
        {
            rootMat = null;
            if (parsedFile == null)
                return false;

            CgfNodeChunk primaryNode = null;
            var nodes = parsedFile.NodeChunks;
            if (nodes != null)
            {
                for (int i = 0; i < nodes.Count; i++)
                {
                    var node = nodes[i];
                    if (node.ObjectID == parsedFile.SelectedMeshChunkID)
                    {
                        primaryNode = node;
                        break;
                    }
                }
            }

            int matChunkId = primaryNode?.MatID ?? -1;
            return matChunkId >= 0 && parsedFile.MaterialByChunkID.TryGetValue(matChunkId, out rootMat);
        }

        static HashSet<int> BuildProxyMaterialIds(CgfFile parsedFile, CgfMaterialChunk rootMat)
        {
            if (rootMat == null)
                return new HashSet<int>();

            if (rootMat.MtlType != CgfMtlType.Multi)
            {
                var singleMaterialIds = new HashSet<int>();
                if (IsNoDrawProxyMaterial(rootMat.Name))
                    singleMaterialIds.Add(rootMat.TableIndex);
                return singleMaterialIds;
            }

            var ids = new HashSet<int>();
            if (!parsedFile.MaterialChildrenByParentChunkID.TryGetValue(rootMat.ChunkID, out var children) || children == null)
                return ids;

            for (int i = 0; i < children.Count; i++)
            {
                if (IsNoDrawProxyMaterial(children[i]?.Name))
                    ids.Add(i);
            }

            return ids;
        }

        // Finds planar templdecalmodulate submeshes. Each returned DecalQuad is later
        // re-spawned as a URP DecalProjector; its submesh index is added to
        // `stripSubmeshIndices` so StripProxySubmeshesFromVisual removes it from the
        // visual mesh. Non-planar decal submeshes are left untouched (rendered as-is).
        static List<DecalQuad> ExtractPlanarDecals(
            GameObject visualRoot,
            BuildResult buildResult,
            CgfFile parsedFile,
            out HashSet<int> stripSubmeshIndices)
        {
            stripSubmeshIndices = null;
            var quads = new List<DecalQuad>();
            if (visualRoot == null || buildResult?.SubmeshMaterialIds == null)
                return quads;
            if (!TryResolveRootMaterial(parsedFile, out var rootMat) || rootMat == null)
                return quads;

            var decalMatIds = BuildDecalMaterialIds(parsedFile, rootMat);
            if (decalMatIds.Count == 0)
                return quads;

            var mf = visualRoot.GetComponent<MeshFilter>();
            var mr = visualRoot.GetComponent<MeshRenderer>();
            var mesh = mf != null ? mf.sharedMesh : null;
            if (mesh == null || mr == null)
                return quads;

            int subCount = mesh.subMeshCount;
            int[] matIds = buildResult.SubmeshMaterialIds;
            if (matIds.Length != subCount)
                return quads;

            var verts = mesh.vertices;
            var uvs = mesh.uv;
            if (uvs == null || uvs.Length != verts.Length)
                return quads;

            var mats = mr.sharedMaterials;
            for (int s = 0; s < subCount; s++)
            {
                if (!decalMatIds.Contains(matIds[s]))
                    continue;

                var tris = mesh.GetTriangles(s);
                if (tris.Length < 6)
                    continue;

                var srcMat = mats != null && s < mats.Length ? mats[s] : null;
                if (!TryBuildDecalQuad(verts, uvs, tris, srcMat, matIds[s], out var quad))
                    continue; // non-planar / degenerate -> keep as overlay geometry

                quads.Add(quad);
                (stripSubmeshIndices ??= new HashSet<int>()).Add(s);
            }

            return quads;
        }

        // Two templdecalmodulate cases need different handling:
        //   * Whole-brush decal -- the CGF is a single-material decal quad (objects/decals/
        //     GDE_*.cgf, moss07.cgf, ...). The root material is itself ModulateDecal and
        //     MtlType is not Multi. These get stripped and rendered through a
        //     DecalProjector (FcBrushDecalProjectorBuilder).
        //   * MatID-in-mesh decal -- the CGF is a multi-material brush where one (or more)
        //     submeshes carry a templdecalmodulate child (decals_hull.cgf, WW2_..._DECAL).
        //     Those submeshes stay in the visual mesh and rely on
        //     CgfMaterialBuilder.ApplyModulateState's URP Lit Multiply blend to reproduce
        //     Cry's surface*decal blending directly on the host geometry.
        static HashSet<int> BuildDecalMaterialIds(CgfFile parsedFile, CgfMaterialChunk rootMat)
        {
            var ids = new HashSet<int>();
            if (rootMat == null)
                return ids;

            if (rootMat.MtlType != CgfMtlType.Multi &&
                rootMat.Classification.Family == CgfMaterialShaderFamily.ModulateDecal)
                ids.Add(rootMat.TableIndex);

            return ids;
        }

        // Fits a planar oriented rectangle to a decal submesh. Returns false when the
        // submesh is non-planar, degenerate, or has no usable UV gradient.
        static bool TryBuildDecalQuad(
            Vector3[] verts,
            Vector2[] uvs,
            int[] tris,
            Material srcMat,
            int materialId,
            out DecalQuad quad)
        {
            quad = default;

            var idx = new List<int>(4);
            for (int i = 0; i < tris.Length; i++)
            {
                if (!idx.Contains(tris[i]))
                    idx.Add(tris[i]);
            }
            if (idx.Count < 3)
                return false;

            Vector3 p0 = verts[tris[0]], p1 = verts[tris[1]], p2 = verts[tris[2]];
            Vector3 normal = Vector3.Cross(p1 - p0, p2 - p0);
            if (normal.sqrMagnitude < 1e-12f)
                return false;
            normal.Normalize();

            Vector3 centroid = Vector3.zero;
            for (int i = 0; i < idx.Count; i++)
                centroid += verts[idx[i]];
            centroid /= idx.Count;

            // Tangent / bitangent: world-space directions of increasing texture U / V.
            Vector2 uv0 = uvs[tris[0]], uv1 = uvs[tris[1]], uv2 = uvs[tris[2]];
            Vector3 e1 = p1 - p0, e2 = p2 - p0;
            Vector2 d1 = uv1 - uv0, d2 = uv2 - uv0;
            float det = d1.x * d2.y - d2.x * d1.y;
            if (Mathf.Abs(det) < 1e-12f)
                return false;
            float inv = 1f / det;
            Vector3 tangent = (e1 * d2.y - e2 * d1.y) * inv;
            Vector3 bitangent = (e2 * d1.x - e1 * d2.x) * inv;
            if (tangent.sqrMagnitude < 1e-12f || bitangent.sqrMagnitude < 1e-12f)
                return false;

            Vector3 axisU = tangent.normalized;
            Vector3 axisV = bitangent.normalized;

            // Planarity gate: max out-of-plane deviation relative to footprint.
            float footprint = Mathf.Max(tangent.magnitude, bitangent.magnitude);
            float maxDev = 0f;
            float minU = float.MaxValue, maxU = float.MinValue;
            float minV = float.MaxValue, maxV = float.MinValue;
            for (int i = 0; i < idx.Count; i++)
            {
                Vector3 d = verts[idx[i]] - centroid;
                maxDev = Mathf.Max(maxDev, Mathf.Abs(Vector3.Dot(d, normal)));
                float cu = Vector3.Dot(d, axisU);
                float cv = Vector3.Dot(d, axisV);
                minU = Mathf.Min(minU, cu); maxU = Mathf.Max(maxU, cu);
                minV = Mathf.Min(minV, cv); maxV = Mathf.Max(maxV, cv);
            }
            if (footprint <= 1e-6f || maxDev > footprint * 0.05f)
                return false;

            float width = maxU - minU;
            float height = maxV - minV;
            if (width < 1e-4f || height < 1e-4f)
                return false;

            Vector3 center = centroid +
                             axisU * ((minU + maxU) * 0.5f) +
                             axisV * ((minV + maxV) * 0.5f);

            quad = new DecalQuad(center, normal, axisU, axisV, width, height, srcMat, materialId);
            return true;
        }

        // Collision-only material detection lives in CgfMaterialClassifier so the brush
        // collider builder and the mesh-build nodraw split agree.
        static bool IsNoDrawProxyMaterial(string materialName)
            => CgfMaterialClassifier.NameMarksCollisionOnly(materialName);

        // True when the mesh carries at least one triangle. A MeshCollider rejects a mesh
        // with no non-degenerate triangle, so empty meshes must never be assigned to one.
        static bool MeshHasTriangles(Mesh mesh)
        {
            if (mesh == null || mesh.vertexCount < 3)
                return false;

            for (int i = 0; i < mesh.subMeshCount; i++)
                if (mesh.GetIndexCount(i) >= 3)
                    return true;

            return false;
        }
    }
}
