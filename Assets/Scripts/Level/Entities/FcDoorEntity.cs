using OpenFarCry.Level.Data;
using UnityEngine;

namespace OpenFarCry.Level.Entities
{
    public class FcDoorEntity : FcMeshEntity
    {
        [Header("Door")]
        [SerializeField] bool  _automatic     = false;
        [SerializeField] float _openTime      = 1f;
        [SerializeField] bool  _locked        = false;
        [SerializeField] bool  _initiallyOpen = false;
        [SerializeField] float _travelTime    = 2f;

        public override void SetData(FcEntityDesc desc)
        {
            base.SetData(desc);
            if (TryGetFloat(desc, "fOpenTime",     out float ot)) _openTime   = ot;
            if (TryGetFloat(desc, "fTravelTime",   out float tt)) _travelTime = tt;
            if (desc.Properties.TryGetValue("bAutomatic",     out string a)) _automatic     = a != "0";
            if (desc.Properties.TryGetValue("bLocked",        out string l)) _locked        = l != "0";
            if (desc.Properties.TryGetValue("bInitiallyOpen", out string o)) _initiallyOpen = o != "0";
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
