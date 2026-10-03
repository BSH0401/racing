using UnityEngine;

namespace Racing
{
    // Marks a collider as off-road: less grip and extra rolling drag.
    public class TrackSurface : MonoBehaviour
    {
        public float gripMultiplier = 0.75f;
        public float drag = 0.9f;
    }
}
