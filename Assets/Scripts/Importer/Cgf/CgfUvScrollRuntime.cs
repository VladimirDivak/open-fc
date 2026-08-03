using System.Collections.Generic;
using UnityEngine;

namespace OpenFarCry.Importer.Cgf
{
    [DisallowMultipleComponent]
    public sealed class CgfUvScrollRuntime : MonoBehaviour
    {
        static readonly int s_baseMapId = Shader.PropertyToID("_BaseMap");

        [SerializeField] Vector2 _scrollPerSecond = new Vector2(0.05f, 0f);
        // Every cloned material (for OnDestroy cleanup).
        readonly List<Material> _instancedMaterials = new List<Material>();
        // Subset of _instancedMaterials that actually has _BaseMap, checked once at clone
        // time instead of every frame — this is what Update() animates.
        readonly List<Material> _scrollableMaterials = new List<Material>();
        readonly HashSet<int> _processedRendererIds = new HashSet<int>();

        void Awake()
        {
            ProcessCurrentRenderers();
        }

        // Rescans for renderers not seen yet (already-processed ones are skipped via
        // _processedRendererIds). Call after anything reparents new renderers under this
        // GameObject post-Awake — LOD siblings are added after the component's own Awake
        // runs, so they're otherwise invisible to it. See CgfLodImportService.ConfigureLodGroup
        // and FcLevelRuntimeLodGroupBuilder.Apply.
        public void Refresh()
        {
            ProcessCurrentRenderers();
        }

        void Update()
        {
            if (_scrollableMaterials.Count == 0)
                return;

            Vector2 delta = _scrollPerSecond * Time.deltaTime;
            for (int i = 0; i < _scrollableMaterials.Count; i++)
            {
                var mat = _scrollableMaterials[i];
                if (mat == null)
                    continue;
                mat.mainTextureOffset += delta;
            }
        }

        void ProcessCurrentRenderers()
        {
            var renderers = GetComponentsInChildren<Renderer>(includeInactive: true);
            if (renderers == null || renderers.Length == 0)
                return;

            for (int r = 0; r < renderers.Length; r++)
            {
                var renderer = renderers[r];
                if (renderer == null)
                    continue;
                int rendererId = renderer.GetInstanceID();
                if (_processedRendererIds.Contains(rendererId))
                    continue;

                var shared = renderer.sharedMaterials;
                if (shared == null || shared.Length == 0)
                    continue;

                var unique = new Material[shared.Length];
                for (int i = 0; i < shared.Length; i++)
                {
                    var source = shared[i];
                    if (source == null)
                        continue;

                    var clone = new Material(source)
                    {
                        name = $"{source.name}_uvscroll"
                    };
                    unique[i] = clone;
                    _instancedMaterials.Add(clone);
                    if (clone.HasProperty(s_baseMapId))
                        _scrollableMaterials.Add(clone);
                }

                renderer.sharedMaterials = unique;
                _processedRendererIds.Add(rendererId);
            }
        }

        void OnDestroy()
        {
            for (int i = 0; i < _instancedMaterials.Count; i++)
            {
                var mat = _instancedMaterials[i];
                if (mat == null)
                    continue;
                if (Application.isPlaying)
                    Destroy(mat);
                else
                    DestroyImmediate(mat);
            }
            _instancedMaterials.Clear();
            _scrollableMaterials.Clear();
            _processedRendererIds.Clear();
        }
    }
}
