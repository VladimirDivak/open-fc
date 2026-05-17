using NUnit.Framework;
using OpenFarCry.FileSystem;
using OpenFarCry.Importer.Cgf;

namespace OpenFarCry.Importer.Tests.Editor
{
    // Integration smoke tests for known-problematic CGF assets.
    // Skip gracefully if VFS / game data not available.
    public sealed class FcKnownAssetRegressionTests
    {
        static CgfFile LoadAsset(string virtualPath)
        {
            if (!FcFileSystem.Exists(virtualPath))
                Assert.Ignore($"Asset not found in VFS (game data absent?): {virtualPath}");

            byte[] data = FcFileSystem.ReadAllBytes(virtualPath);
            Assert.That(data, Is.Not.Null.And.Not.Empty, "Read zero bytes from VFS.");
            return CgfParser.Parse(data);
        }

        // ── overhanging_rock ─────────────────────────────────────────────────────

        [Test]
        public void OverhangingRock_ParsesWithVerticesAndFaces()
        {
            var file = LoadAsset("objects/natural/rocks/cliffs/overhanging_rock.cgf");

            Assert.That(file, Is.Not.Null);
            Assert.That(file.MeshChunk, Is.Not.Null, "No MeshChunk — static CGF should have one.");
            Assert.That(file.MeshChunk.Vertices, Is.Not.Null.And.Length.GreaterThan(0));
            Assert.That(file.MeshChunk.Faces,    Is.Not.Null.And.Length.GreaterThan(0));
            Assert.That(file.MeshChunk.HasBoneInfo, Is.False, "overhanging_rock should be static.");
        }

        [Test]
        public void OverhangingRock_MaterialNotNoDraw()
        {
            var file = LoadAsset("objects/natural/rocks/cliffs/overhanging_rock.cgf");

            Assert.That(file.MaterialChunks, Is.Not.Null);
            foreach (var mat in file.MaterialChunks)
            {
                var cls = CgfMaterialClassifier.Analyze(mat);
                Assert.That(cls.Family, Is.Not.EqualTo(CgfMaterialShaderFamily.NoDraw),
                    $"Material '{mat.Name}' classified as NoDraw — would produce invisible rock.");
            }
        }

        // ── coa_streetlight ──────────────────────────────────────────────────────

        [Test]
        public void CoaStreetlight_ParsesWithMultipleSubmeshes()
        {
            var file = LoadAsset("objects/buildings/m03/compound_area/coa_streetlight.cgf");

            Assert.That(file, Is.Not.Null);
            Assert.That(file.MeshChunk, Is.Not.Null);
            Assert.That(file.MeshChunk.Faces, Is.Not.Null.And.Length.GreaterThan(0));

            // Streetlight has lamp + pole — expect at least 2 distinct material IDs.
            var matIds = new System.Collections.Generic.HashSet<int>();
            foreach (var f in file.MeshChunk.Faces)
                matIds.Add(f.MatID);
            Assert.That(matIds.Count, Is.GreaterThanOrEqualTo(1));
        }

        [Test]
        public void CoaStreetlight_LodSiblingExists()
        {
            const string basePath = "objects/buildings/m03/compound_area/coa_streetlight";
            if (!FcFileSystem.Exists(basePath + ".cgf"))
                Assert.Ignore("Base CGF not in VFS.");

            bool hasLod = FcFileSystem.Exists(basePath + "_lod1.cgf") ||
                          FcFileSystem.Exists(basePath + "_lod2.cgf");
            Assert.That(hasLod, Is.True,
                "Expected at least one _lod1/_lod2 sibling for coa_streetlight.");
        }

        // ── camo_net ─────────────────────────────────────────────────────────────

        [Test]
        public void CamoNet_MaterialClassifiedAsPlants()
        {
            var file = LoadAsset("objects/outdoor/human_camp/camo_net.cgf");

            Assert.That(file, Is.Not.Null);
            Assert.That(file.MaterialChunks, Is.Not.Null.And.Count.GreaterThan(0),
                "camo_net must have at least one material chunk.");

            bool hasAlphaMat = false;
            foreach (var mat in file.MaterialChunks)
            {
                var cls = CgfMaterialClassifier.Analyze(mat);
                if (cls.Family == CgfMaterialShaderFamily.Plants ||
                    cls.Family == CgfMaterialShaderFamily.AlphaBlend ||
                    cls.IsCutout)
                    hasAlphaMat = true;
            }
            Assert.That(hasAlphaMat, Is.True,
                "camo_net should have at least one alpha/plants material — it's a transparency mesh.");
        }

        // ── WW2 decal ────────────────────────────────────────────────────────────

        [Test]
        public void Ww2DecalCbe02_MaterialClassifiedAsDecal()
        {
            var file = LoadAsset("objects/glm/ww2style/ceiling/ww2_gk_cbe02_x200y100z200_decal.cgf");

            Assert.That(file, Is.Not.Null);
            Assert.That(file.MaterialChunks, Is.Not.Null.And.Count.GreaterThan(0));

            bool hasDecal = false;
            foreach (var mat in file.MaterialChunks)
            {
                var cls = CgfMaterialClassifier.Analyze(mat);
                if (cls.Family == CgfMaterialShaderFamily.Decal ||
                    cls.Family == CgfMaterialShaderFamily.GlowDecal ||
                    cls.Family == CgfMaterialShaderFamily.ModulateDecal)
                    hasDecal = true;
            }
            Assert.That(hasDecal, Is.True,
                "Decal CGF should have at least one Decal-family material.");
        }

        [Test]
        public void Ww2DecalCbe02_HasGeometry()
        {
            var file = LoadAsset("objects/glm/ww2style/ceiling/ww2_gk_cbe02_x200y100z200_decal.cgf");

            Assert.That(file.MeshChunk, Is.Not.Null);
            Assert.That(file.MeshChunk.Vertices, Is.Not.Null.And.Length.GreaterThan(0));
        }
    }
}
