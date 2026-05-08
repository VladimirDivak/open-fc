using System.Collections.Generic;
using UnityEngine;

namespace OpenFarCry.Importer.Texture
{
    // In-memory Texture2D cache keyed by normalised virtual path (lower-case, forward slashes).
    // Populated by a DDS parser when one is available; empty until then.
    public sealed class TextureRuntimeCache
    {
        readonly Dictionary<string, Texture2D> _textures = new Dictionary<string, Texture2D>();

        public bool TryGet(string normalizedVirtualPath, out Texture2D texture)
        {
            return _textures.TryGetValue(normalizedVirtualPath, out texture) && texture != null;
        }

        public void Store(string normalizedVirtualPath, Texture2D texture)
        {
            _textures[normalizedVirtualPath] = texture;
        }

        public void Clear()
        {
            foreach (var tex in _textures.Values)
                if (tex != null)
                    Object.Destroy(tex);
            _textures.Clear();
        }

        public int Count => _textures.Count;
    }
}
