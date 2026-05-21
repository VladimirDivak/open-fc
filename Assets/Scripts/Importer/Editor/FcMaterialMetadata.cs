using UnityEngine;

namespace OpenFarCry.Importer.Editor
{
    // Sidecar ScriptableObject paired with a baked CGF material asset. Stores the content
    // hash of the source chunk so re-bake can detect drift (chunk content changed since
    // the .mat was first written) without clobbering user edits to the .mat itself.
    // Also stores resolved virtual texture paths so the runtime injection step can bind
    // textures without re-parsing the CGF chunk.
    //
    // Path convention: alongside the .mat asset with extension ".metadata.asset".
    public sealed class FcMaterialMetadata : ScriptableObject
    {
        // Source CGF virtual path that produced the canonical bake. A material can be
        // referenced by many CGFs (LOD siblings, shared libraries), but the canonical
        // source is the one that first baked it. Used only for diagnostics.
        public string SourceCgfVirtualPath;

        // ChunkID + TableIndex of the source chunk at the time of bake. Used to verify
        // the manifest entry still matches the underlying CGF.
        public int SourceChunkId;
        public int SourceTableIndex;

        // Raw chunk.Name (before sanitization). Lets the bake registry match a chunk to
        // its existing .mat without round-tripping through the sanitized filename. Two
        // chunks with the same raw name but different content (e.g. different colors)
        // resolve to different .mat assets via collision suffixes.
        public string ChunkName;

        // Sanitized override name (brush.lst / entity XML / vegetation type) or empty
        // for the default material. Determines which manifest slot this material fills.
        public string OverrideName;

        // MD5 (first 8 bytes hex) of chunk content fields at bake time. Re-bake compares
        // the current chunk hash to this; mismatch ⇒ drift warning in Console (no
        // automatic overwrite, user edits stay intact).
        public string ContentHash;

        // Resolved virtual texture paths (output of CgfTexturePathResolver candidate
        // probing). Empty strings mean the chunk had no texture in that slot. Runtime
        // reads these to bind textures into the Instantiated copy of the .mat.
        public string DiffuseTexturePath;
        public string NormalTexturePath;
        public string SpecularTexturePath;
        public string OpacityTexturePath;
        public string GlossTexturePath;
    }
}
