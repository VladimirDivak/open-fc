using UnityEngine;

namespace OpenFarCry.Level.Volumes
{
    // Placeholder for one CryMovie cutscene sequence (moviedata.xml <Sequence>).
    // No runtime playback — stores metadata only. Position is level origin.
    public sealed class FcMovieSequencePlaceholder : MonoBehaviour
    {
        [SerializeField] public float StartTime;
        [SerializeField] public float EndTime;
        [SerializeField] public int NodeCount;

        public float Duration => EndTime - StartTime;

#if UNITY_EDITOR
        void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(0.85f, 0.3f, 0.85f, 0.7f);
            Gizmos.DrawWireSphere(transform.position, 0.5f);
        }
#endif
    }
}
