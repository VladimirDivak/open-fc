using UnityEngine;

namespace OpenFarCry.Importer.Cgf
{
    [DisallowMultipleComponent]
    public sealed class FcRagdollController : MonoBehaviour
    {
        [SerializeField] bool startInRagdoll;

        Rigidbody[] _bodies;
        Animation _animation;

        void Awake()
        {
            RebuildCache();
            if (startInRagdoll)
                SetRagdoll();
            else
                SetAnimated();
        }

        public void RebuildCache()
        {
            _bodies = GetComponentsInChildren<Rigidbody>(includeInactive: true);
            _animation = GetComponent<Animation>();
        }

        [ContextMenu("Set Animated")]
        public void SetAnimated()
        {
            EnsureCache();
            if (_animation != null)
                _animation.enabled = true;

            for (int i = 0; i < _bodies.Length; i++)
            {
                var rb = _bodies[i];
                if (rb == null)
                    continue;

                if (!rb.isKinematic)
                {
                    rb.linearVelocity = Vector3.zero;
                    rb.angularVelocity = Vector3.zero;
                }
                rb.isKinematic = true;
                rb.useGravity = false;
                rb.detectCollisions = true;
            }
        }

        [ContextMenu("Set Ragdoll")]
        public void SetRagdoll()
        {
            EnsureCache();
            if (_animation != null)
                _animation.enabled = false;

            for (int i = 0; i < _bodies.Length; i++)
            {
                var rb = _bodies[i];
                if (rb == null)
                    continue;

                rb.isKinematic = false;
                rb.useGravity = true;
                rb.detectCollisions = true;
            }
        }

        void EnsureCache()
        {
            if (_bodies == null)
                RebuildCache();
        }
    }
}
