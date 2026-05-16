using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace OpenFarCry.Level.Tests.Editor
{
    public sealed class FcLevelAuthoringFinalizeTests
    {
        [Test]
        public void FinalizeSceneDependencies_PromotesFcDataAssets_AndRewiresSceneReferences()
        {
            const string levelName = "__FinalizeTestLevel";
            string fcLevelRoot = $"Assets/FCData/Levels/{levelName.ToLowerInvariant()}";
            string sourceDir = $"{fcLevelRoot}/FinalizeTest";
            string promotedRoot = $"Assets/Scenes/Levels/{levelName}_Data";
            string token = Guid.NewGuid().ToString("N").Substring(0, 8);
            string texturePath = $"{sourceDir}/tex_{token}.asset";
            string materialPath = $"{sourceDir}/mat_{token}.mat";

            Scene scene = default;
            GameObject go = null;

            try
            {
                EnsureDir(sourceDir);
                EnsureDir("Assets/Scenes/Levels");

                var texture = new Texture2D(4, 4, TextureFormat.RGBA32, mipChain: false);
                texture.SetPixel(0, 0, Color.green);
                texture.Apply(updateMipmaps: false, makeNoLongerReadable: false);
                AssetDatabase.CreateAsset(texture, texturePath);

                var shader = Shader.Find("Unlit/Texture");
                if (shader == null)
                    shader = Shader.Find("Standard");
                Assert.That(shader, Is.Not.Null, "Expected fallback shader for test material.");

                var material = new Material(shader) { name = $"FinalizeMat_{token}" };
                material.mainTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);
                AssetDatabase.CreateAsset(material, materialPath);
                AssetDatabase.SaveAssets();

                scene = CreateIsolatedTestScene();
                go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                SceneManager.MoveGameObjectToScene(go, scene);

                var renderer = go.GetComponent<Renderer>();
                Assert.That(renderer, Is.Not.Null);
                renderer.sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>(materialPath);

                var fcDataBefore = CollectSceneFcDataPaths(scene);
                Assert.That(fcDataBefore.Count, Is.GreaterThan(0), "Scene should depend on FCData before finalize.");
                Assert.That(fcDataBefore.Contains(materialPath), Is.True);
                Assert.That(fcDataBefore.Contains(texturePath), Is.True);

                string report = InvokeFinalize(scene, levelName);
                Assert.That(report, Does.Contain("Scene FCData refs after: 0"));

                var fcDataAfter = CollectSceneFcDataPaths(scene);
                Assert.That(fcDataAfter.Count, Is.EqualTo(0), "Scene should have no FCData refs after finalize.");

                string promotedMaterialPath = AssetDatabase.GetAssetPath(renderer.sharedMaterial);
                Assert.That(promotedMaterialPath.StartsWith(promotedRoot, StringComparison.Ordinal), Is.True);

                var promotedTexture = renderer.sharedMaterial != null ? renderer.sharedMaterial.mainTexture : null;
                string promotedTexturePath = AssetDatabase.GetAssetPath(promotedTexture);
                Assert.That(promotedTexturePath.StartsWith(promotedRoot, StringComparison.Ordinal), Is.True);
            }
            finally
            {
                if (go != null)
                    UnityEngine.Object.DestroyImmediate(go);

                if (scene.IsValid() && scene.isLoaded)
                    TryCloseScene(scene);

                if (AssetDatabase.IsValidFolder(fcLevelRoot))
                    AssetDatabase.DeleteAsset(fcLevelRoot);

                if (AssetDatabase.IsValidFolder(promotedRoot))
                    AssetDatabase.DeleteAsset(promotedRoot);

                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
            }
        }

        static string InvokeFinalize(Scene scene, string levelName)
        {
            var type = Type.GetType("OpenFarCry.Level.Editor.FcLevelBuilderWindow, OpenFarCry.Level.Editor");
            Assert.That(type, Is.Not.Null, "Failed to resolve FcLevelBuilderWindow type.");

            var method = type.GetMethod("FinalizeSceneDependencies", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.That(method, Is.Not.Null, "Failed to resolve FinalizeSceneDependencies method.");

            var report = method.Invoke(null, new object[] { scene, levelName }) as string;
            Assert.That(report, Is.Not.Null);
            return report;
        }

        static Scene CreateIsolatedTestScene()
        {
            try
            {
                return EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            }
            catch (InvalidOperationException)
            {
                return EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            }
        }

        static void TryCloseScene(Scene scene)
        {
            if (!scene.IsValid() || !scene.isLoaded)
                return;

            if (EditorSceneManager.CloseScene(scene, removeScene: true))
                return;

            SceneManager.UnloadSceneAsync(scene);
        }

        static HashSet<string> CollectSceneFcDataPaths(Scene scene)
        {
            var roots = scene.GetRootGameObjects();
            var deps = EditorUtility.CollectDependencies(roots);
            var result = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < deps.Length; i++)
            {
                var dep = deps[i];
                if (dep == null)
                    continue;

                string path = AssetDatabase.GetAssetPath(dep);
                if (string.IsNullOrWhiteSpace(path))
                    continue;

                if (path.StartsWith("Assets/FCData/", StringComparison.Ordinal))
                    result.Add(path);
            }

            return result;
        }

        static void EnsureDir(string dir)
        {
            if (AssetDatabase.IsValidFolder(dir))
                return;

            string parent = System.IO.Path.GetDirectoryName(dir)?.Replace('\\', '/');
            string leaf = System.IO.Path.GetFileName(dir);
            if (!string.IsNullOrEmpty(parent) && !AssetDatabase.IsValidFolder(parent))
                EnsureDir(parent);

            AssetDatabase.CreateFolder(parent, leaf);
        }
    }
}
