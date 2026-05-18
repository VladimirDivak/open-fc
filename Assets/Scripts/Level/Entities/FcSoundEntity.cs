using OpenFarCry.Level.Data;
using UnityEngine;

namespace OpenFarCry.Level.Entities
{
    // SoundSpot / EAXArea / RandomAmbientSound entity.
    // Stores virtual path for future audio importer; clip is null until then.
    [RequireComponent(typeof(AudioSource))]
    public class FcSoundEntity : FcEntity
    {
        [Header("Sound")]
        [SerializeField] string _soundVirtualPath;
        [SerializeField] float _volume = 1f;
        [SerializeField] float _minDistance = 5f;
        [SerializeField] float _maxDistance = 30f;
        [SerializeField] bool _loop = true;

        public string SoundVirtualPath => _soundVirtualPath;

        public override void SetData(FcEntityDesc desc)
        {
            base.SetData(desc);

            if (desc.Properties.TryGetValue("soundName", out string snd) && !string.IsNullOrEmpty(snd))
                _soundVirtualPath = snd.ToLowerInvariant().Replace('\\', '/');
            else if (desc.Properties.TryGetValue("SoundLibrary", out string lib) && !string.IsNullOrEmpty(lib))
                _soundVirtualPath = lib.ToLowerInvariant().Replace('\\', '/');

            if (TryGetFloat(desc, "fRadius", out float r)) { _minDistance = r * 0.1f; _maxDistance = r; }
            if (TryGetFloat(desc, "fMaxRadius", out float maxR)) _maxDistance = maxR;
            if (TryGetFloat(desc, "fVolume", out float vol)) _volume = vol;

            if (desc.Properties.TryGetValue("bLoop", out string loopStr))
                _loop = loopStr == "1" || loopStr.Equals("true", System.StringComparison.OrdinalIgnoreCase);
        }

        static bool TryGetFloat(FcEntityDesc desc, string key, out float value)
        {
            value = 0f;
            return desc.Properties.TryGetValue(key, out string s) &&
                   float.TryParse(s, System.Globalization.NumberStyles.Float,
                       System.Globalization.CultureInfo.InvariantCulture, out value);
        }
    }
}
