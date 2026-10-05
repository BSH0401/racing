using UnityEngine;

namespace Racing
{
    // Forwards a car's collisions to the chase mode (ramming damage).
    [RequireComponent(typeof(Racer))]
    public class CarImpacts : MonoBehaviour
    {
        Racer racer;

        void Awake() => racer = GetComponent<Racer>();

        void OnCollisionEnter(Collision c)
        {
            if (ChaseMode.Instance) ChaseMode.Instance.OnImpact(racer, c);
        }
    }
}
