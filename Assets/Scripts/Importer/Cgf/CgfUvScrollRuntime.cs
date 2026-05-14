using System.Collections.Generic;
using UnityEngine;

namespace OpenFarCry.Importer.Cgf
{
    [DisallowMultipleComponent]
    public sealed class CgfUvScrollRuntime : MonoBehaviour
    {
        [SerializeField] Vector2 _scrollPerSecond = new Vector2(0.05f, 0f);
        readonly List<Material> _instancedMaterials = new List<Material>();
        readonly HashSet<int> _processedRendererIds = new HashSet<int>();

        void Awake()
        {
            ProcessCurrentRenderers();
        }

        void Update()
        {
            ProcessCurrentRenderers();

            if (_instancedMaterials.Count == 0)
                return;

            Vector2 delta = _scrollPerSecond * Time.deltaTime;
            for (int i = 0; i < _instancedMaterials.Count; i++)
            {
                var mat = _instancedMaterials[i];
                if (mat == null || !mat.HasProperty("_BaseMap"))
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
            _processedRendererIds.Clear();
        }
    }
}
