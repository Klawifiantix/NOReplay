using UnityEngine;

namespace NOReplay
{
    /// <summary>
    /// Marks a kinematic-free debris body the first time it touches the ground.
    /// The ghost itself stays kinematic; only the thrown part copy has a body.
    /// </summary>
    internal sealed class ReplayDebrisSink : MonoBehaviour
    {
        public bool Hit;
        public Vector3 Point;

        void OnCollisionEnter(Collision collision)
        {
            if (Hit || collision == null || collision.contactCount == 0) return;
            if (collision.collider != null && collision.collider.isTrigger) return;
            Hit = true;
            Point = collision.GetContact(0).point;
        }
    }

    /// <summary>
    /// Destroys a thrown part from the replay clock. The ghost that spawned it
    /// may already be inactive, so the piece cannot wait for that ghost's tick.
    /// </summary>
    internal sealed class ReplayDebrisLife : MonoBehaviour
    {
        public float Born;
        public float Life = 14f;

        void Update()
        {
            bool playing = Plugin.CurrentDoc != null;
            float now = Plugin.Clock != null ? Plugin.Clock.Time : 0f;
            if (!DebrisLife.Expired(playing, now, Born, Life)) return;
            Destroy(gameObject);
        }
    }
}
