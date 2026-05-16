using System;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace OpenFarCry.Level.Tests.Editor
{
    public sealed class FcLevelAuthoringVegetationPolicyTests
    {
        [Test]
        public void ConfigureVegetationAuthoringCachedInstance_DisablesCollidersAndLodRuntimeBehavior()
        {
            var root = new GameObject("veg_cached_root");
            var lod0 = GameObject.CreatePrimitive(PrimitiveType.Cube);
            var lod1 = GameObject.CreatePrimitive(PrimitiveType.Cube);
            lod0.name = "lod0";
            lod1.name = "lod1";
            lod0.transform.SetParent(root.transform, worldPositionStays: false);
            lod1.transform.SetParent(root.transform, worldPositionStays: false);

            var lodGroup = root.AddComponent<LODGroup>();
            var lod0Renderer = lod0.GetComponent<Renderer>();
            var lod1Renderer = lod1.GetComponent<Renderer>();
            Assert.That(lod0Renderer, Is.Not.Null);
            Assert.That(lod1Renderer, Is.Not.Null);
            lod0Renderer.enabled = true;
            lod1Renderer.enabled = true;

            lodGroup.SetLODs(new[]
            {
                new LOD(0.6f, new[] { lod0Renderer }),
                new LOD(0.3f, new[] { lod1Renderer }),
            });
            lodGroup.RecalculateBounds();
            lodGroup.enabled = true;

            root.AddComponent<BoxCollider>();
            var childCollider = lod0.AddComponent<SphereCollider>();
            childCollider.enabled = true;

            try
            {
                InvokeConfigureVegetationAuthoringCachedInstance(root);

                var colliders = root.GetComponentsInChildren<Collider>(includeInactive: true);
                for (int i = 0; i < colliders.Length; i++)
                    Assert.That(colliders[i].enabled, Is.False, $"Collider '{colliders[i].name}' must be disabled.");

                Assert.That(lodGroup.enabled, Is.False, "LODGroup runtime switching must be disabled in authoring cache.");
                Assert.That(lod0Renderer.enabled, Is.True, "LOD0 renderer should remain visible.");
                Assert.That(lod1Renderer.enabled, Is.False, "Higher LOD renderers should be hidden.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        static void InvokeConfigureVegetationAuthoringCachedInstance(GameObject cachedChild)
        {
            var type = Type.GetType("OpenFarCry.Level.Editor.FcLevelBuilderWindow, OpenFarCry.Level.Editor");
            Assert.That(type, Is.Not.Null, "Failed to resolve FcLevelBuilderWindow type.");

            var method = type.GetMethod(
                "ConfigureVegetationAuthoringCachedInstance",
                BindingFlags.NonPublic | BindingFlags.Static);
            Assert.That(method, Is.Not.Null, "Failed to resolve ConfigureVegetationAuthoringCachedInstance method.");

            method.Invoke(null, new object[] { cachedChild });
        }
    }
}
