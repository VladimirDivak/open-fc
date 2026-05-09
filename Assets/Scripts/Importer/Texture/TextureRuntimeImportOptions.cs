namespace OpenFarCry.Importer.Texture
{
    public readonly struct TextureRuntimeImportOptions
    {
        public readonly bool UseRuntimeMemoryCache;
        public readonly bool MarkNonReadable;
        public readonly bool LinearColorSpace;
        public readonly bool GenerateMipmaps;
        readonly bool _configured;

        public TextureRuntimeImportOptions(
            bool useRuntimeMemoryCache,
            bool markNonReadable,
            bool linearColorSpace,
            bool generateMipmaps)
        {
            UseRuntimeMemoryCache = useRuntimeMemoryCache;
            MarkNonReadable = markNonReadable;
            LinearColorSpace = linearColorSpace;
            GenerateMipmaps = generateMipmaps;
            _configured = true;
        }

        internal bool IsConfigured => _configured;

        public static TextureRuntimeImportOptions Default =>
            new TextureRuntimeImportOptions(
                useRuntimeMemoryCache: true,
                markNonReadable: true,
                linearColorSpace: false,
                generateMipmaps: true);
    }
}
