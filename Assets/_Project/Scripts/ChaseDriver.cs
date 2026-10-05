using UnityEngine;

namespace Racing
{
    // Pursuit AI for the chase modes: drives the city street grid towards a target car, turning at
    // the intersections that lead to it (the grid is complete, so going straight then turning always
    // works), and rams it directly once it is close and in sight.
    [RequireComponent(typeof(CarController))]
    public class ChaseDriver : MonoBehaviour
    {
        [System.NonSerialized] public Racer target;
        [System.NonSerialized] public float speedScale = 1f;
        public float cornerSpeed = 13f;
        public float directRange = 45f;

        CarController car;
        bool alongZ; // driving a north-south street (x = const)
        float stuckTime, reverseTime;

        const int SightMask = ~((1 << 2) | (1 << 31)); // ignore cars and minimap-only objects

        void Awake() => car = GetComponent<CarController>();

        void OnEnable()
        {
            if (!car) car = GetComponent<CarController>();
            alongZ = Mathf.Abs(transform.forward.z) >= Mathf.Abs(transform.forward.x);
        }

        static float Line(float v) => Mathf.Clamp(Mathf.Round(v / CityLayout.Pitch), 0, CityLayout.Lines - 1) * CityLayout.Pitch;

        void FixedUpdate()
        {
            if (!target || car.InputLocked) return;
            Vector3 p = transform.position;
            Vector3 q = target.transform.position;
            float dist = Flat(q - p).magnitude;
            Vector3 qLead = q + target.car.Body.linearVelocity * Mathf.Clamp(dist / 35f, 0f, 1.2f);
            float speed = car.ForwardSpeed;
            float top = car.maxSpeed * speedScale;
            float brake = car.brakeDeceleration * 0.7f;
            float targetSpeed = top;
            Vector3 aim;

            float lx = Line(p.x), lz = Line(p.z);
            bool onX = Mathf.Abs(p.x - lx) < CityLayout.RoadHalf + 3f;
            bool onZ = Mathf.Abs(p.z - lz) < CityLayout.RoadHalf + 3f;

            if (dist < directRange && Visible(p, q))
            {
                aim = qLead;
            }
            else if (!onX && !onZ)
            {
                // Off the streets (park, sidewalk): get back onto the nearest one.
                aim = Mathf.Abs(p.x - lx) < Mathf.Abs(p.z - lz)
                    ? new Vector3(lx, p.y, p.z + Mathf.Sign(qLead.z - p.z) * 15f)
                    : new Vector3(p.x + Mathf.Sign(qLead.x - p.x) * 15f, p.y, lz);
                targetSpeed = Mathf.Min(top, 12f);
            }
            else
            {
                if (onX && !onZ) alongZ = true;
                else if (onZ && !onX) alongZ = false;
                float tx = Line(qLead.x), tz = Line(qLead.z);

                // At an intersection: turn onto the street that leads to the target.
                if (onX && onZ)
                {
                    if (alongZ && Mathf.Abs(p.z - tz) < 6f && Mathf.Abs(lx - tx) > 1f) alongZ = false;
                    else if (!alongZ && Mathf.Abs(p.x - tx) < 6f && Mathf.Abs(lz - tz) > 1f) alongZ = true;
                }

                bool turning;
                float turnDist;
                if (alongZ)
                {
                    turning = Mathf.Abs(lx - tx) >= 1f;
                    float goal = turning ? tz : qLead.z;
                    float d = goal - p.z;
                    turnDist = Mathf.Abs(d);
                    aim = new Vector3(lx, p.y, p.z + Mathf.Sign(d) * Mathf.Min(turnDist, 10f + Mathf.Abs(speed) * 0.5f));
                    if (turning && turnDist < 14f) aim = new Vector3(lx + Mathf.Sign(tx - lx) * 14f, p.y, tz);
                }
                else
                {
                    turning = Mathf.Abs(lz - tz) >= 1f;
                    float goal = turning ? tx : qLead.x;
                    float d = goal - p.x;
                    turnDist = Mathf.Abs(d);
                    aim = new Vector3(p.x + Mathf.Sign(d) * Mathf.Min(turnDist, 10f + Mathf.Abs(speed) * 0.5f), p.y, lz);
                    if (turning && turnDist < 14f) aim = new Vector3(tx, p.y, lz + Mathf.Sign(tz - lz) * 14f);
                }
                if (turning)
                {
                    float allowed = Mathf.Sqrt(cornerSpeed * cornerSpeed + 2f * brake * Mathf.Max(0f, turnDist - 10f));
                    targetSpeed = Mathf.Min(targetSpeed, allowed);
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
            stuckTime = throttle > 0.5f && Mathf.Abs(speed) < 1.5f ? stuckTime + Time.fixedDeltaTime : 0f;
            if (stuckTime > 1.2f) { reverseTime = 1.1f; stuckTime = 0f; }
            if (reverseTime > 0f)
            {
                reverseTime -= Time.fixedDeltaTime;
                car.Throttle = -1f;
                car.Steer = -Mathf.Sign(angle);
                car.Handbrake = false;
                return;
            }
            car.Throttle = throttle;
            car.Handbrake = Mathf.Abs(angle) > 80f && speed > 9f;
        }

        static bool Visible(Vector3 from, Vector3 to) =>
            !Physics.Linecast(from + Vector3.up * 1.2f, to + Vector3.up * 1.2f, SightMask, QueryTriggerInteraction.Ignore);

        static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);
    }
}
