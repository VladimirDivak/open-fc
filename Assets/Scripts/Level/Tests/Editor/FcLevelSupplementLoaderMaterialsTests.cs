using System;
using System.Linq;
using System.Reflection;
using System.Xml;
using NUnit.Framework;
using OpenFarCry.Level.Data;

namespace OpenFarCry.Level.Tests.Editor
{
    public sealed class FcLevelSupplementLoaderMaterialsTests
    {
        [Test]
        public void ParseMaterialsFromDocument_ParsesLibraryAndSubMaterialsTextureNodes()
        {
            const string xml = @"
<MaterialsLibrary>
  <Library Name='Level'>
    <Material Name='watch_tower0' Shader='templmodelcommon' AlphaTest='0.1' Opacity='0.9' MtlFlags='16' Id='{ABC}'>
      <Textures>
        <Texture File='Objects\Outdoor\islanders_structures\wood73.dds' Map='Diffuse' />
        <Texture File='Objects\Outdoor\islanders_structures\wood74_ddn.dds' Map='Bumpmap' />
      </Textures>
      <PublicParams specularexp='32' />
      <SubMaterials>
        <Material Name='[1] s_nodraw' Shader='TemplDecal'>
          <Textures />
        </Material>
        <Material Name='[2] Material #230' Shader='templmodelcommon'>
          <Textures>
            <Texture File='Textures\ww2style\concrOld_893.dds' Map='Diffuse' />
          </Textures>
        </Material>
      </SubMaterials>
    </Material>
  </Library>
</MaterialsLibrary>";

            var doc = new XmlDocument();
            doc.LoadXml(xml);

            var materials = InvokeParseMaterialsFromDocument(doc);
            Assert.That(materials, Is.Not.Null);
            Assert.That(materials.Length, Is.EqualTo(3));

            var root = materials.FirstOrDefault(m => m.Name == "watch_tower0");
            Assert.That(root.Name, Is.EqualTo("watch_tower0"));
            Assert.That(root.FullName, Is.EqualTo("watch_tower0"));
            Assert.That(root.Depth, Is.EqualTo(0));
            Assert.That(root.AlphaTest, Is.EqualTo(0.1f).Within(0.0001f));
            Assert.That(root.Opacity, Is.EqualTo(0.9f).Within(0.0001f));
            Assert.That(root.MtlFlags, Is.EqualTo(16));
            Assert.That(root.MaterialGuid, Is.EqualTo("{ABC}"));
            Assert.That(root.PublicParams, Is.Not.Null);
            Assert.That(root.PublicParams.Length, Is.EqualTo(1));
            Assert.That(root.PublicParams[0].Key, Is.EqualTo("specularexp"));
            Assert.That(root.PublicParams[0].Value, Is.EqualTo("32"));
            Assert.That(root.TextureSlots, Is.Not.Null);
            Assert.That(root.TextureSlots.Length, Is.EqualTo(2));
            Assert.That(root.TextureSlots[0].Map, Is.EqualTo("Diffuse"));
            Assert.That(root.TextureSlots[0].File, Is.EqualTo("objects/outdoor/islanders_structures/wood73.dds"));
            Assert.That(root.TextureSlots[1].Map, Is.EqualTo("Bumpmap"));
            Assert.That(root.TextureSlots[1].File, Is.EqualTo("objects/outdoor/islanders_structures/wood74_ddn.dds"));
            Assert.That(root.TextureRefs, Does.Contain("objects/outdoor/islanders_structures/wood73.dds"));
            Assert.That(root.TextureRefs, Does.Contain("objects/outdoor/islanders_structures/wood74_ddn.dds"));
            Assert.That(root.TextureRefs, Does.Not.Contain("diffuse"), "Map semantic must not be treated as a texture path.");
            Assert.That(root.TextureRefs, Does.Not.Contain("0.5,0.5,0.5"), "Color values must not be treated as texture paths.");

            var sub = materials.FirstOrDefault(m => m.Name == "[2] Material #230");
            Assert.That(sub.FullName, Is.EqualTo("watch_tower0/[2] Material #230"));
            Assert.That(sub.Depth, Is.EqualTo(1));
            Assert.That(sub.TextureSlots, Is.Not.Null);
            Assert.That(sub.TextureSlots.Length, Is.EqualTo(1));
            Assert.That(sub.TextureSlots[0].Map, Is.EqualTo("Diffuse"));
            Assert.That(sub.TextureRefs, Does.Contain("textures/ww2style/concrold_893.dds"));
        }

        [Test]
        public void ParseMaterialsFromDocument_FallbackParsesFlatMaterialChildren()
        {
            const string xml = @"
<Root>
  <Material Name='flat_mat' Shader='templmodelcommon'>
    <Texture File='Objects\Indoor\boxes\box.dds' />
  </Material>
</Root>";

            var doc = new XmlDocument();
            doc.LoadXml(xml);

            var materials = InvokeParseMaterialsFromDocument(doc);
            Assert.That(materials.Length, Is.EqualTo(1));
            Assert.That(materials[0].Name, Is.EqualTo("flat_mat"));
            Assert.That(materials[0].TextureRefs, Does.Contain("objects/indoor/boxes/box.dds"));
        }

        static FcLevelSupplementData.MaterialDesc[] InvokeParseMaterialsFromDocument(XmlDocument doc)
        {
            var method = typeof(FcLevelSupplementLoader).GetMethod(
                "ParseMaterialsFromDocument",
                BindingFlags.NonPublic | BindingFlags.Static);
            Assert.That(method, Is.Not.Null, "Failed to resolve ParseMaterialsFromDocument method.");

            return method.Invoke(null, new object[] { doc }) as FcLevelSupplementData.MaterialDesc[];
        }
    }
}
