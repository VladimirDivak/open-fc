using System;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace OpenFarCry.Level.Tests.Editor
{
    public sealed class FcLevelAuthoringTerrainTextureTests
    {
        [Test]
        public void TransposeTexture_TransposesPixelsAndDimensions()
        {
            var source = new Texture2D(2, 3, TextureFormat.RGBA32, mipChain: false, linear: false);
            source.SetPixel(0, 0, Color.red);
            source.SetPixel(1, 0, Color.green);
            source.SetPixel(0, 1, Color.blue);
            source.SetPixel(1, 1, Color.yellow);
            source.SetPixel(0, 2, Color.cyan);
            source.SetPixel(1, 2, Color.magenta);
            source.Apply(updateMipmaps: false, makeNoLongerReadable: false);

            try
            {
                var transposed = InvokeTransposeTexture(source);
                Assert.That(transposed, Is.Not.Null);
                Assert.That(transposed.width, Is.EqualTo(3));
                Assert.That(transposed.height, Is.EqualTo(2));

                for (int y = 0; y < source.height; y++)
                for (int x = 0; x < source.width; x++)
                    AssertColorEq(source.GetPixel(x, y), transposed.GetPixel(y, x));

                UnityEngine.Object.DestroyImmediate(transposed);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(source);
            }
        }

        [Test]
        public void DuplicateTexture_ReadableSource_CreatesIndependentCopy()
        {
            var source = new Texture2D(2, 2, TextureFormat.RGBA32, mipChain: false, linear: false);
            source.SetPixel(0, 0, Color.red);
            source.SetPixel(1, 0, Color.green);
            source.SetPixel(0, 1, Color.blue);
            source.SetPixel(1, 1, Color.white);
            source.Apply(updateMipmaps: false, makeNoLongerReadable: false);

            try
            {
                var copy = InvokeDuplicateTexture(source);
                Assert.That(copy, Is.Not.Null);
                Assert.That(copy, Is.Not.SameAs(source));
                Assert.That(copy.width, Is.EqualTo(source.width));
                Assert.That(copy.height, Is.EqualTo(source.height));
                AssertColorEq(source.GetPixel(0, 0), copy.GetPixel(0, 0));
                AssertColorEq(source.GetPixel(1, 1), copy.GetPixel(1, 1));

                UnityEngine.Object.DestroyImmediate(copy);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(source);
            }
        }

        static Texture2D InvokeTransposeTexture(Texture2D source)
        {
            var type = Type.GetType("OpenFarCry.Level.Editor.FcLevelSceneBuilder, OpenFarCry.Level.Editor");
            Assert.That(type, Is.Not.Null, "Failed to resolve FcLevelSceneBuilder type.");
            var method = type.GetMethod("TransposeTexture", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.That(method, Is.Not.Null, "Failed to resolve TransposeTexture method.");

            return method.Invoke(null, new object[] { source }) as Texture2D;
        }

        static Texture2D InvokeDuplicateTexture(Texture2D source)
        {
            var type = Type.GetType("OpenFarCry.Level.Editor.FcLevelSceneBuilder, OpenFarCry.Level.Editor");
            Assert.That(type, Is.Not.Null, "Failed to resolve FcLevelSceneBuilder type.");
            var method = type.GetMethod("DuplicateTexture", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.That(method, Is.Not.Null, "Failed to resolve DuplicateTexture method.");

            return method.Invoke(null, new object[] { source }) as Texture2D;
        }

        static void AssertColorEq(Color expected, Color actual)
        {
            Assert.That(actual.r, Is.EqualTo(expected.r).Within(0.001f));
            Assert.That(actual.g, Is.EqualTo(expected.g).Within(0.001f));
            Assert.That(actual.b, Is.EqualTo(expected.b).Within(0.001f));
            Assert.That(actual.a, Is.EqualTo(expected.a).Within(0.001f));
        }
    }
}
