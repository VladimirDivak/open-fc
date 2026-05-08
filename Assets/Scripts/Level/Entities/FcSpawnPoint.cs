using OpenFarCry.Level.Data;
using UnityEngine;

namespace OpenFarCry.Level.Entities
{
    // Object Type="Respawn" — player / NPC spawn marker.
    public class FcSpawnPoint : FcEntity
    {
        [Header("Spawn")]
        [SerializeField] string _teamName;

        public string TeamName => _teamName;

        public override void SetData(FcObjectDesc desc)
        {
            base.SetData(desc);
            if (desc.Type != null)
                _teamName = desc.Name ?? string.Empty;
        }

#if UNITY_EDITOR
        void OnDrawGizmos()
        {
            Gizmos.color = Color.green;
            Gizmos.DrawWireSphere(transform.position, 0.4f);
            Gizmos.DrawRay(transform.position, transform.forward * 0.8f);
        }
#endif
    }
}
