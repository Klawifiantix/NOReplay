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
}
