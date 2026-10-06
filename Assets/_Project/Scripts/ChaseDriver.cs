using System.Collections.Generic;
using UnityEngine;

namespace Racing
{
    // Pursuit AI for the chase modes: plans a route to the target over the road graph (city streets,
    // national roads and both highway carriageways, WorldLayout.Graph), follows it with corner speed
    // planning, and rams the target directly once it is close and in sight.
    [RequireComponent(typeof(CarController))]
    public class ChaseDriver : MonoBehaviour
    {
        [System.NonSerialized] public Racer target;
        [System.NonSerialized] public float speedScale = 1f;
        public float cornerGrip = 1.3f;
        public float directRange = 45f;

        CarController car;
        readonly List<Vector3> path = new List<Vector3>();
        int pathPos;
        float replan, stuckTime, reverseTime;

        const int SightMask = ~((1 << 2) | (1 << 31)); // ignore cars and minimap-only objects

        void Awake() => car = GetComponent<CarController>();

        void OnEnable()
        {
            if (!car) car = GetComponent<CarController>();
            path.Clear();
            replan = 0f;
        }

        void FixedUpdate()
        {
            if (!target || car.InputLocked) return;
            float dt = Time.fixedDeltaTime;
            Vector3 p = transform.position;
            Vector3 q = target.transform.position;
            float dist = Flat(q - p).magnitude;
            Vector3 qLead = q + target.car.Body.linearVelocity * Mathf.Clamp(dist / 35f, 0f, 1.2f);
            float speed = car.ForwardSpeed;
            float top = car.maxSpeed * speedScale;
            float brake = car.brakeDeceleration * 0.7f;
            float targetSpeed = top;
            Vector3 aim;

            if (dist < directRange && Visible(p, q))
            {
                aim = qLead;
                path.Clear();
            }
            else
            {
                replan -= dt;
                if (replan <= 0f || path.Count == 0 || pathPos >= path.Count)
                {
                    replan = 0.6f;
                    Plan(p, qLead);
                }
                // Advance past waypoints we have reached (or overshot).
                while (pathPos < path.Count - 1)
                {
                    Vector3 a = path[pathPos], b = path[pathPos + 1];
                    Vector3 ab = Flat(b - a);
                    if (Flat(p - a).magnitude < 12f || Vector3.Dot(Flat(p - a), ab) > ab.sqrMagnitude * 0.6f) pathPos++;
                    else break;
                }
                aim = LookAhead(p, 10f + Mathf.Abs(speed) * 0.5f);

                // Corner speed from the heading change at the coming waypoints.
                float along = Flat(path[Mathf.Min(pathPos, path.Count - 1)] - p).magnitude;
                for (int k = pathPos; k < path.Count - 2 && along < 20f + speed * speed / (2f * brake); k++)
                {
                    Vector3 d0 = Flat(path[k + 1] - path[k]), d1 = Flat(path[k + 2] - path[k + 1]);
                    float turn = Vector3.Angle(d0, d1) * Mathf.Deg2Rad;
                    if (turn > 0.15f)
                    {
                        float radius = Mathf.Clamp(Mathf.Min(d0.magnitude, d1.magnitude) * 0.5f / Mathf.Tan(turn * 0.5f), 8f, 400f);
                        float vCorner = Mathf.Sqrt(cornerGrip * 9.81f * radius);
                        targetSpeed = Mathf.Min(targetSpeed, Mathf.Sqrt(vCorner * vCorner + 2f * brake * Mathf.Max(0f, along - 8f)));
                    }
                    along += d0.magnitude;
                }
            }

            Vector3 local = transform.InverseTransformPoint(aim);
            float angle = Mathf.Atan2(local.x, local.z) * Mathf.Rad2Deg;
            if (Mathf.Abs(angle) > 100f) targetSpeed = Mathf.Min(targetSpeed, 7f); // turning round
            car.Steer = Mathf.Clamp(angle / Mathf.Max(car.CurrentMaxSteer, 5f), -1f, 1f);

            float throttle;
            if (speed < targetSpeed - 1f) throttle = 1f;
            else if (speed > targetSpeed + 1.5f) throttle = -Mathf.Clamp01((speed - targetSpeed) / 5f);
            else throttle = 0.35f;
            if (Mathf.Abs(angle) > 50f && speed > 12f) throttle = Mathf.Min(throttle, 0.2f);

            // Wedged against something: back out with opposite lock for a moment.
            stuckTime = throttle > 0.5f && Mathf.Abs(speed) < 1.5f ? stuckTime + dt : 0f;
            if (stuckTime > 1.2f) { reverseTime = 1.1f; stuckTime = 0f; }
            if (reverseTime > 0f)
            {
                reverseTime -= dt;
                car.Throttle = -1f;
                car.Steer = -Mathf.Sign(angle);
                car.Handbrake = false;
                return;
            }
            car.Throttle = throttle;
            car.Handbrake = Mathf.Abs(angle) > 80f && speed > 9f;
        }

        void Plan(Vector3 from, Vector3 to)
        {
            path.Clear();
            pathPos = 0;
            int a = WorldLayout.NearestNode(from, transform.forward);
            int b = WorldLayout.NearestNode(to, Vector3.zero);
            path.AddRange(WorldLayout.FindPath(a, b));
            path.Add(to); // finish at the target itself
        }

        // Point 'ahead' metres further along the path from the closest point on the current leg.
        Vector3 LookAhead(Vector3 p, float ahead)
        {
            if (path.Count == 0) return p + transform.forward * 10f;
            if (pathPos >= path.Count - 1) return path[path.Count - 1];
            Vector3 a = path[pathPos], b = path[pathPos + 1];
            Vector3 ab = b - a;
            float t = Mathf.Clamp01(Vector3.Dot(Flat(p - a), Flat(ab)) / Mathf.Max(Flat(ab).sqrMagnitude, 1e-3f));
            Vector3 cur = a + ab * t;
            float left = ahead;
            for (int k = pathPos; k < path.Count - 1; k++)
            {
                Vector3 s = k == pathPos ? cur : path[k];
                Vector3 e = path[k + 1];
                float len = Flat(e - s).magnitude;
                if (len >= left) return s + (e - s) * (left / Mathf.Max(len, 1e-3f));
                left -= len;
            }
            return path[path.Count - 1];
        }

        static bool Visible(Vector3 from, Vector3 to) =>
            !Physics.Linecast(from + Vector3.up * 1.2f, to + Vector3.up * 1.2f, SightMask, QueryTriggerInteraction.Ignore);

        static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);
    }
}
