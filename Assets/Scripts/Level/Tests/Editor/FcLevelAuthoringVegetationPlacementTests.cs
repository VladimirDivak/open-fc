using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using OpenFarCry.Level.Data;
using OpenFarCry.Level.Entities;
using UnityEditor;
using UnityEngine;

namespace OpenFarCry.Level.Tests.Editor
{
    public sealed class FcLevelAuthoringVegetationPlacementTests
    {
        [Test]
        public void PlaceVegetationAsAuthoringInstances_SetsTransformAndSerializedFields()
        {
            var levelRoot = new GameObject("Level_Test");
            var terrainData = new TerrainData
            {
                heightmapResolution = 33,
                size = new Vector3(100f, 20f, 100f)
            };
            terrainData.SetHeights(0, 0, new float[33, 33]);

            var terrainGo = Terrain.CreateTerrainGameObject(terrainData);
            var terrain = terrainGo.GetComponent<Terrain>();
            terrainGo.transform.position = new Vector3(10f, 0f, 20f);

            try
            {
                var typeByIndex = new Dictionary<int, FcLevelSupplementData.VegetationTypeDesc>
                {
                    [7] = new FcLevelSupplementData.VegetationTypeDesc
                    {
                        Index = 7,
                        FileName = "objects/forest/tree.cgf"
                    }
                };

                var instances = new[]
                {
                    new FcLevelSupplementData.VegetationInstanceDesc { Type = 7, X = 0, Y = 0, Scale = 2f },
                    new FcLevelSupplementData.VegetationInstanceDesc { Type = 7, X = 65535, Y = 65535, Scale = 0f },
                };

                int placed = InvokePlaceVegetationAsAuthoringInstances(instances, typeByIndex, levelRoot, terrain);
                Assert.That(placed, Is.EqualTo(2));

                var vegRoot = levelRoot.transform.Find("Vegetation_Authoring");
                Assert.That(vegRoot, Is.Not.Null);
                Assert.That(vegRoot.childCount, Is.EqualTo(2));

                var a = vegRoot.GetChild(0);
                var b = vegRoot.GetChild(1);
                Assert.That(a.name, Is.EqualTo("Vegetation_000000"));
                Assert.That(b.name, Is.EqualTo("Vegetation_000001"));

                AssertVec3(a.position, new Vector3(10f, 0f, 20f));
                AssertVec3(b.position, new Vector3(110f, 0f, 120f));
                AssertVec3(a.localScale, new Vector3(2f, 2f, 2f));
                AssertVec3(b.localScale, new Vector3(1f, 1f, 1f));

                AssertVegetationFields(a.GetComponent<FcVegetationInstance>(), "objects/forest/tree.cgf", 7, 2f);
                AssertVegetationFields(b.GetComponent<FcVegetationInstance>(), "objects/forest/tree.cgf", 7, 1f);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(levelRoot);
                UnityEngine.Object.DestroyImmediate(terrainGo);
                UnityEngine.Object.DestroyImmediate(terrainData);
            }
        }

        static int InvokePlaceVegetationAsAuthoringInstances(
            FcLevelSupplementData.VegetationInstanceDesc[] instances,
            Dictionary<int, FcLevelSupplementData.VegetationTypeDesc> typeByIndex,
            GameObject levelRoot,
            Terrain terrain)
        {
            var type = Type.GetType("OpenFarCry.Level.Editor.FcLevelSceneBuilder, OpenFarCry.Level.Editor");
            Assert.That(type, Is.Not.Null, "Failed to resolve FcLevelSceneBuilder type.");

            var method = type.GetMethod(
                "PlaceVegetationAsAuthoringInstances",
                BindingFlags.NonPublic | BindingFlags.Static);
            Assert.That(method, Is.Not.Null, "Failed to resolve PlaceVegetationAsAuthoringInstances method.");

            var result = method.Invoke(null, new object[] { instances, typeByIndex, levelRoot, terrain });
            return result is int placed ? placed : 0;
        }

        static void AssertVegetationFields(FcVegetationInstance instance, string expectedPath, int expectedType, float expectedScale)
        {
            Assert.That(instance, Is.Not.Null);
            var so = new SerializedObject(instance);
            Assert.That(so.FindProperty("_virtualPath").stringValue, Is.EqualTo(expectedPath));
            Assert.That(so.FindProperty("_typeIndex").intValue, Is.EqualTo(expectedType));
            Assert.That(so.FindProperty("_instanceScale").floatValue, Is.EqualTo(expectedScale).Within(0.0001f));
        }

        static void AssertVec3(Vector3 actual, Vector3 expected)
        {
            Assert.That(actual.x, Is.EqualTo(expected.x).Within(0.0001f));
            Assert.That(actual.y, Is.EqualTo(expected.y).Within(0.0001f));
            Assert.That(actual.z, Is.EqualTo(expected.z).Within(0.0001f));
        }
    }
}
