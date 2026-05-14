using NUnit.Framework;
using OpenFarCry.Importer.Cgf;

namespace OpenFarCry.Importer.Tests.Editor
{
    public sealed class CgfMaterialClassifierTests
    {
        [Test]
        public void Analyze_NoDrawShader_ReturnsNoDrawFamily()
        {
            var chunk = new CgfMaterialChunk
            {
                ShaderName = "nodraw",
                MtlType = CgfMtlType.Standard
            };

            var c = CgfMaterialClassifier.Analyze(chunk);

            Assert.That(c.Family, Is.EqualTo(CgfMaterialShaderFamily.NoDraw));
            Assert.That(c.IsNoDraw, Is.True);
        }

        [Test]
        public void Analyze_AdditiveFlag_SetsAdditive()
        {
            var chunk = new CgfMaterialChunk
            {
                ShaderName = "templmodelcommon",
                MtlType = CgfMtlType.Standard,
                Flags = CgfMtlFlags.Additive
            };

            var c = CgfMaterialClassifier.Analyze(chunk);

            Assert.That(c.IsAdditive, Is.True);
        }

        [Test]
        public void Analyze_PlantShader_ForcesTwoSidedAndVertexColors()
        {
            var chunk = new CgfMaterialChunk
            {
                ShaderName = "templplants1",
                MtlType = CgfMtlType.Standard
            };

            var c = CgfMaterialClassifier.Analyze(chunk);

            Assert.That(c.Family, Is.EqualTo(CgfMaterialShaderFamily.Plants));
            Assert.That(c.IsTwoSided, Is.True);
            Assert.That(c.UsesVertexColors, Is.True);
        }

        [Test]
        public void Analyze_BumpSpecShaderName_UsesBumpSpecFamily()
        {
            var chunk = new CgfMaterialChunk
            {
                ShaderName = "templbumpspec_hp",
                MtlType = CgfMtlType.Standard
            };

            var c = CgfMaterialClassifier.Analyze(chunk);

            Assert.That(c.Family, Is.EqualTo(CgfMaterialShaderFamily.BumpSpec));
        }
    }
}
