using OpenFarCry.Importer.Cgf;
using OpenFarCry.Level.Data;
using UnityEngine;

namespace OpenFarCry.Level.Entities
{
    public class FcRigidBodyEntity : FcMeshEntity
    {
        [Header("Physics")]
        [SerializeField] float _mass = 10f;
        [SerializeField] bool _isKinematic;

        public float Mass => _mass;

        protected override void ApplyResult(CgfRuntimeImportResult result)
        {
            base.ApplyResult(result);

            var rb = gameObject.AddComponent<Rigidbody>();
            rb.mass = _mass;
            rb.isKinematic = _isKinematic;
        }

        public override void SetData(FcEntityDesc desc)
        {
            base.SetData(desc);
            _importSkeleton = false;

            if (desc.Properties.TryGetValue("mass", out string massStr) &&
                float.TryParse(massStr, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out float m))
                _mass = m;
        }
    }
}
