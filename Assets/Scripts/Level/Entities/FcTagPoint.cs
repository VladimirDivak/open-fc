using OpenFarCry.Level.Data;
using UnityEngine;

namespace OpenFarCry.Level.Entities
{
    // Object Type="TagPoint" / "AIAnchor" / "Waypoint" / "Group" — named transform marker.
    public class FcTagPoint : FcEntity
    {
        [Header("Tag")]
        [SerializeField] string _tagType;

        public string TagType => _tagType;

        public override void SetData(FcObjectDesc desc)
        {
            base.SetData(desc);
            _tagType = desc.Type ?? string.Empty;
        }

#if UNITY_EDITOR
        void OnDrawGizmos()
        {
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(transform.position, 0.2f);
        }
#endif
    }
}
