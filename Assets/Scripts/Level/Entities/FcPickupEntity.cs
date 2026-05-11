using OpenFarCry.Level.Data;
using UnityEngine;

namespace OpenFarCry.Level.Entities
{
    public enum PickupCategory
    {
        Unknown,
        Weapon,
        Ammo,
        Health,
        Armor,
        KeyCard,
        Checkpoint,
    }

    [RequireComponent(typeof(SphereCollider))]
    public class FcPickupEntity : FcMeshEntity
    {
        [Header("Pickup")]
        [SerializeField] PickupCategory _category;
        [SerializeField] float          _respawnTime = 30f;
        [SerializeField] bool           _respawns    = false;
        [SerializeField] int            _amount      = 1;

        protected override void Awake()
        {
            base.Awake();
            var sc = GetComponent<SphereCollider>();
            sc.isTrigger = true;
            sc.radius    = 0.5f;
        }

        public override void SetData(FcEntityDesc desc)
        {
            base.SetData(desc);
            _category = InferCategory(desc.EntityClass);
            if (TryGetFloat(desc, "fRespawnTime", out float rt)) _respawnTime = rt;
            if (desc.Properties.TryGetValue("bRespawn", out string r)) _respawns = r != "0";
            if (TryGetInt(desc, "amount", out int amt)) _amount = amt;
        }

        static PickupCategory InferCategory(string cls)
        {
            if (string.IsNullOrEmpty(cls)) return PickupCategory.Unknown;
            if (cls.IndexOf("Ammo",       System.StringComparison.OrdinalIgnoreCase) >= 0) return PickupCategory.Ammo;
            if (cls.IndexOf("Pickup",     System.StringComparison.OrdinalIgnoreCase) >= 0) return PickupCategory.Weapon;
            if (cls.IndexOf("health",     System.StringComparison.OrdinalIgnoreCase) >= 0) return PickupCategory.Health;
            if (cls.IndexOf("Armor",      System.StringComparison.OrdinalIgnoreCase) >= 0) return PickupCategory.Armor;
            if (cls.IndexOf("KeyCard",    System.StringComparison.OrdinalIgnoreCase) >= 0) return PickupCategory.KeyCard;
            if (cls.IndexOf("Checkpoint", System.StringComparison.OrdinalIgnoreCase) >= 0) return PickupCategory.Checkpoint;
            return PickupCategory.Unknown;
        }

        static bool TryGetFloat(FcEntityDesc desc, string key, out float value)
        {
            value = 0f;
            return desc.Properties.TryGetValue(key, out string s) &&
                   float.TryParse(s, System.Globalization.NumberStyles.Float,
                       System.Globalization.CultureInfo.InvariantCulture, out value);
        }

        static bool TryGetInt(FcEntityDesc desc, string key, out int value)
        {
            value = 0;
            return desc.Properties.TryGetValue(key, out string s) && int.TryParse(s, out value);
        }
    }
}
