using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using OpenFarCry.Importer.Cgf;
using OpenFarCry.Level.Entities;
using OpenFarCry.Level.Services;
using UnityEngine;
using UnityEngine.TestTools;

namespace OpenFarCry.Level.Tests.Editor
{
    public sealed class FcEntityLoadServiceTests
    {
        [UnityTest]
        public IEnumerator Enqueue_ProcessesHigherPriorityFirst_WhenLoadsPerFrameIsOne()
        {
            string scope = "scope_priority_" + Guid.NewGuid().ToString("N");
            var env = new TestEnv(scope);
            try
            {
                env.SetConcurrent(maxConcurrent: 1, loadsPerFrame: 1);
                env.SetMaxRetries(0);
                env.Loader.DefaultResult = MakeSuccess(scope, "objects/default.cgf", cacheHit: false);

                var low = TestMeshEntity.Create("low", "objects/low.cgf", scope, Vector3.zero);
                var high = TestMeshEntity.Create("high", "objects/high.cgf", scope, Vector3.zero);

                env.Service.Enqueue(low, EntityLoadPriority.Background);
                env.Service.Enqueue(high, EntityLoadPriority.Critical);

                env.Tick();
                yield return null;
                env.Tick();
                yield return null;

                Assert.That(env.Loader.RequestPaths.Count, Is.EqualTo(2));
                Assert.That(env.Loader.RequestPaths[0], Is.EqualTo("objects/high.cgf"));
                Assert.That(env.Loader.RequestPaths[1], Is.EqualTo("objects/low.cgf"));
                Assert.That(low.ApplyCount, Is.EqualTo(1));
                Assert.That(high.ApplyCount, Is.EqualTo(1));
            }
            finally
            {
                env.Dispose();
            }
        }

        [UnityTest]
        public IEnumerator LoadFail_RetriesOnce_ThenApplies()
        {
            string scope = "scope_retry_" + Guid.NewGuid().ToString("N");
            var env = new TestEnv(scope);
            try
            {
                env.SetConcurrent(maxConcurrent: 1, loadsPerFrame: 1);
                env.SetMaxRetries(1);

                var entity = TestMeshEntity.Create("retry_entity", "objects/retry.cgf", scope, Vector3.zero);
                env.Loader.SetScript(
                    "objects/retry.cgf",
                    new Queue<LoadedMeshArtifact>(new[]
                    {
                        LoadedMeshArtifact.Failed("first_fail"),
                        MakeSuccess(scope, "objects/retry.cgf", cacheHit: false)
                    }));

                env.Service.Enqueue(entity, EntityLoadPriority.Background);

                env.Tick();
                yield return null;
                env.Tick();
                yield return null;
                env.Tick();
                yield return null;

                Assert.That(env.Loader.GetRequestCount("objects/retry.cgf"), Is.EqualTo(2));
                Assert.That(entity.ApplyCount, Is.EqualTo(1));
                Assert.That(env.Service.TryGetState(entity, out var state), Is.True);
                Assert.That(state, Is.EqualTo(EntityLoadState.Applied));

                var report = FcLevelRuntimeReportRegistry.GetOrCreate(scope);
                Assert.That(report.EntitiesRetried, Is.EqualTo(1));
                Assert.That(report.EntitiesApplied, Is.EqualTo(1));
                Assert.That(report.EntitiesFailed, Is.EqualTo(0));
            }
            finally
            {
                env.Dispose();
            }
        }

        [UnityTest]
        public IEnumerator CancelAllForScope_CancelsPending()
        {
            string scope = "scope_cancel_" + Guid.NewGuid().ToString("N");
            var env = new TestEnv(scope);
            try
            {
                env.SetConcurrent(maxConcurrent: 1, loadsPerFrame: 1);

                var e1 = TestMeshEntity.Create("e1", "objects/e1.cgf", scope, Vector3.zero);
                var e2 = TestMeshEntity.Create("e2", "objects/e2.cgf", scope, Vector3.zero);

                env.Loader.DefaultResult = MakeSuccess(scope, "objects/default.cgf", cacheHit: true);
                env.Service.Enqueue(e1, EntityLoadPriority.Background);
                env.Service.Enqueue(e2, EntityLoadPriority.Background);

                env.Service.CancelAllForScope(scope);
                env.Tick();
                yield return null;

                Assert.That(env.Loader.RequestPaths.Count, Is.EqualTo(0));
                Assert.That(env.Service.TryGetState(e1, out var s1), Is.True);
                Assert.That(env.Service.TryGetState(e2, out var s2), Is.True);
                Assert.That(s1, Is.EqualTo(EntityLoadState.Cancelled));
                Assert.That(s2, Is.EqualTo(EntityLoadState.Cancelled));

                var report = FcLevelRuntimeReportRegistry.GetOrCreate(scope);
                Assert.That(report.EntitiesCancelled, Is.EqualTo(2));
            }
            finally
            {
                env.Dispose();
            }
        }

        static LoadedMeshArtifact MakeSuccess(string scope, string virtualPath, bool cacheHit)
        {
            var importResult = CgfRuntimeImportResult.Completed(
                virtualPath: virtualPath,
                parsedFile: null,
                buildResult: null,
                usedRuntimeMemoryCache: cacheHit,
                parsedCacheKey: null,
                modelCacheKey: null);
            return LoadedMeshArtifact.Completed(importResult, scope, totalMs: 1);
        }

        sealed class TestEnv : IDisposable
        {
            readonly MethodInfo _updateMethod;
            readonly List<GameObject> _owned = new();

            public readonly FcEntityLoadService Service;
            public readonly FakeMeshLoadService Loader;

            public TestEnv(string scope)
            {
                var root = new GameObject("test_level_services");
                root.SetActive(false);
                _owned.Add(root);

                var cache = root.AddComponent<FcLevelCacheService>();
                cache.SetLevelScope(scope);
                Loader = root.AddComponent<FakeMeshLoadService>();
                Service = root.AddComponent<FcEntityLoadService>();

                root.SetActive(true);
                _updateMethod = typeof(FcEntityLoadService).GetMethod("Update", BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.That(_updateMethod, Is.Not.Null, "Failed to find FcEntityLoadService.Update via reflection.");
            }

            public void Tick()
            {
                _updateMethod.Invoke(Service, null);
            }

            public void SetConcurrent(int maxConcurrent, int loadsPerFrame)
            {
                SetField("_maxConcurrent", maxConcurrent);
                SetField("_loadsPerFrame", loadsPerFrame);
            }

            public void SetMaxRetries(int maxRetries)
            {
                SetField("_maxRetries", maxRetries);
            }

            void SetField(string name, int value)
            {
                var field = typeof(FcEntityLoadService).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.That(field, Is.Not.Null, $"Field {name} not found.");
                field.SetValue(Service, value);
            }

            public void Dispose()
            {
                foreach (var go in _owned)
                {
                    if (go != null)
                        UnityEngine.Object.DestroyImmediate(go);
                }
                _owned.Clear();
                TestMeshEntity.DestroyAll();
            }
        }

        sealed class FakeMeshLoadService : MonoBehaviour, IFcMeshLoadService
        {
            readonly Dictionary<string, Queue<LoadedMeshArtifact>> _scriptByPath =
                new(StringComparer.Ordinal);
            readonly Dictionary<string, int> _countByPath = new(StringComparer.Ordinal);

            public readonly List<string> RequestPaths = new();
            public LoadedMeshArtifact DefaultResult;

            public void SetScript(string virtualPath, Queue<LoadedMeshArtifact> artifacts)
            {
                _scriptByPath[virtualPath] = artifacts;
            }

            public int GetRequestCount(string virtualPath)
            {
                return _countByPath.TryGetValue(virtualPath, out var n) ? n : 0;
            }

            public UniTask<LoadedMeshArtifact> LoadAsync(FcMeshLoadRequest request, CancellationToken ct = default)
            {
                RequestPaths.Add(request.VirtualPath);
                _countByPath.TryGetValue(request.VirtualPath, out int count);
                _countByPath[request.VirtualPath] = count + 1;

                if (_scriptByPath.TryGetValue(request.VirtualPath, out var scripted) && scripted.Count > 0)
                    return UniTask.FromResult(scripted.Dequeue());

                if (DefaultResult != null)
                    return UniTask.FromResult(DefaultResult);

                return UniTask.FromResult(LoadedMeshArtifact.Failed("no_scripted_result", request.LevelScopeId));
            }
        }

        sealed class TestMeshEntity : FcMeshEntity
        {
            static readonly List<GameObject> Created = new();

            string _scope;
            string _path;
            public int ApplyCount { get; private set; }

            public static TestMeshEntity Create(string name, string virtualPath, string scope, Vector3 position)
            {
                var go = new GameObject(name);
                go.transform.position = position;
                Created.Add(go);

                var entity = go.AddComponent<TestMeshEntity>();
                entity._scope = scope;
                entity._path = virtualPath;
                return entity;
            }

            public static void DestroyAll()
            {
                for (int i = 0; i < Created.Count; i++)
                {
                    var go = Created[i];
                    if (go != null)
                        UnityEngine.Object.DestroyImmediate(go);
                }
                Created.Clear();
            }

            public override string GetLevelScopeId() => _scope;

            public override FcMeshLoadRequest CreateLoadRequest()
            {
                return new FcMeshLoadRequest(
                    virtualPath: _path,
                    levelScopeId: _scope,
                    selectedMeshChunkId: -1,
                    importSkeleton: false,
                    importScale: 0.01f,
                    useRuntimeMemoryCache: true,
                    preloadTextures: false);
            }

            public override void ApplyLoadedMesh(LoadedMeshArtifact artifact)
            {
                ApplyCount++;
            }
        }
    }
}
