using UnityEngine;

namespace Racing
{
    // One traffic car, driven along its lane by TrafficSystem's rules: Intelligent Driver Model
    // car-following against the nearest thing ahead in its lane (traffic, racers, wrecks), lane
    // changes on the highway when held up, curve speed limits on national roads. The body is
    // kinematic, so racers bounce off it; a hard hit hands it to physics as a wreck.
    [RequireComponent(typeof(Rigidbody))]
    public class TrafficCar : MonoBehaviour
    {
        public Transform[] wheels = new Transform[0];
        public float wheelRadius = 0.33f;
        public GameObject lights;
        public float wreckSpeed = 7f;

        [System.NonSerialized] public TrafficSystem system;
        [System.NonSerialized] public TrafficSystem.Lane lane;
        [System.NonSerialized] public float s, speed;
        [System.NonSerialized] public bool wrecked;

        Rigidbody body;
        float desired, offset, offsetFrom, changeT = 1f, changeCooldown, wreckTime, wheelSpin;
        Vector3 velocity;

        public bool Finished { get; private set; }
        public float WreckAge => Time.time - wreckTime;
        public Vector3 Velocity => wrecked ? body.linearVelocity : velocity;

        const float MaxAccel = 1.6f, ComfortBrake = 3f, MinGap = 5f, Headway = 1.3f;

        void Awake() => body = GetComponent<Rigidbody>();

        public void Place(TrafficSystem.Lane l, float at, float v)
        {
            if (!body) body = GetComponent<Rigidbody>();
            if (!body.isKinematic)
            {
                body.linearVelocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;
            }
            lane = l;
            s = at;
            desired = speed = v;
            offset = offsetFrom = l.offset;
            changeT = 1f;
            changeCooldown = Random.Range(2f, 6f);
            wrecked = Finished = false;
            body.isKinematic = true;
            Vector3 p = TrafficSystem.Point(lane, s, offset, out var fwd);
            transform.SetPositionAndRotation(p, Quaternion.LookRotation(fwd));
            velocity = fwd * speed;
            gameObject.SetActive(true);
            body.position = p;
            body.rotation = transform.rotation;
        }

        public void SetLights(bool on)
        {
            if (lights) lights.SetActive(on);
        }

        void FixedUpdate()
        {
            if (wrecked || lane == null) return;
            float dt = Time.fixedDeltaTime;
            var road = lane.road;

            // Curve speed limit on the national roads (look ~40 m ahead).
            float limit = desired;
            if (lane.carriageway == 0)
            {
                int a = Mathf.FloorToInt(s), b = road.Wrap(a + 10 * lane.dir);
                float turn = Vector3.Angle(road.right[road.Wrap(a)], road.right[b]) * Mathf.Deg2Rad;
                if (turn > 0.05f) limit = Mathf.Min(limit, Mathf.Sqrt(0.35f * 9.81f * 40f / turn));
            }

            float gap = Leader(lane, s, out float leaderSpeed);
            float accel = Idm(speed, limit, gap, leaderSpeed);

            // Held up on the highway: move over when the next lane has room.
            changeCooldown -= dt;
            if (lane.carriageway != 0 && changeT >= 1f && changeCooldown <= 0f && gap < 60f && leaderSpeed < desired - 2.5f)
            {
                foreach (int step in new[] { -1, 1 })
                {
                    var other = system.Neighbour(lane, step);
                    if (other == null || !Clear(other)) continue;
                    offsetFrom = offset;
                    lane = other;
                    changeT = 0f;
                    break;
                }
                changeCooldown = 3f;
            }
            // Drift back towards the slow lane now and then.
            else if (lane.carriageway != 0 && changeT >= 1f && changeCooldown <= 0f && Random.value < 0.004f)
            {
                var other = system.Neighbour(lane, 1);
                if (other != null && Clear(other) && desired <= other.speed + 3f)
                {
                    offsetFrom = offset;
                    lane = other;
                    changeT = 0f;
                }
                changeCooldown = 4f;
            }
            if (changeT < 1f)
            {
                changeT = Mathf.Min(1f, changeT + dt / 3f);
                offset = Mathf.Lerp(offsetFrom, lane.offset, Mathf.SmoothStep(0f, 1f, changeT));
            }
            else offset = lane.offset;

            speed = Mathf.Max(0f, speed + accel * dt);
            s += speed * dt / lane.spacing * lane.dir;
            if (road.closed) s = Mathf.Repeat(s, road.Count);
            else if (s < 1f || s > road.Count - 2f)
            {
                s = Mathf.Clamp(s, 1f, road.Count - 2f);
                speed = 0f;
                Finished = true; // end of the road: TrafficSystem recycles it
            }

            Vector3 p = TrafficSystem.Point(lane, s, offset, out var fwd);
            float lateral = (lane.offset - offsetFrom) * (changeT < 1f ? 0.25f : 0f);
            var rot = Quaternion.LookRotation(fwd) * Quaternion.Euler(0f, Mathf.Clamp(lateral * lane.dir, -6f, 6f), 0f);
            velocity = (p - body.position) / dt;
            body.MovePosition(p);
            body.MoveRotation(rot);

            wheelSpin += speed * dt / wheelRadius * Mathf.Rad2Deg;
            foreach (var w in wheels)
                if (w) w.localRotation = Quaternion.Euler(wheelSpin, 0f, 0f);
        }

        // IDM acceleration for speed v, free-road speed v0, gap to the leader and its speed.
        static float Idm(float v, float v0, float gap, float vLead)
        {
            float free = 1f - Mathf.Pow(v / Mathf.Max(v0, 1f), 4f);
            if (gap > 300f) return Mathf.Clamp(MaxAccel * free, -8f, MaxAccel);
            float star = MinGap + v * Headway + v * (v - vLead) / (2f * Mathf.Sqrt(MaxAccel * ComfortBrake));
            float a = MaxAccel * (free - Mathf.Pow(Mathf.Max(star, 0f) / Mathf.Max(gap, 0.5f), 2f));
            return Mathf.Clamp(a, -9f, MaxAccel);
        }

        // Nearest thing ahead in this lane within 300 m: traffic (by lane position), or any racer or
        // wreck physically in the lane's corridor.
        float Leader(TrafficSystem.Lane l, float at, out float leaderSpeed)
        {
            float best = 1000f, lead = desired;
            foreach (var c in system.cars)
            {
                if (c == this || c.wrecked || c.lane != l) continue;
                float g = TrafficSystem.Gap(l, at, c.s) - 4.6f;
                if (g > -2f && g < best) { best = Mathf.Max(g, 0f); lead = c.speed; }
            }
            Vector3 p = transform.position, fwd = transform.forward;
            Vector3 right = Vector3.Cross(Vector3.up, fwd);
            void Obstacle(Vector3 q, Vector3 v)
            {
                Vector3 d = q - p;
                float along = Vector3.Dot(d, fwd), side = Vector3.Dot(d, right);
                if (along < 2f || along > 120f || Mathf.Abs(side - (l.offset - offset) * l.dir) > 2.3f || Mathf.Abs(d.y) > 3f) return;
                float g = along - 4.8f;
                if (g < best) { best = Mathf.Max(g, 0f); lead = Mathf.Max(0f, Vector3.Dot(v, fwd)); }
            }
            var race = system.race;
            if (race)
                foreach (var r in race.racers)
                    if (r && r.gameObject.activeInHierarchy) Obstacle(r.transform.position, r.car.Body.linearVelocity);
            foreach (var c in system.cars)
                if (c.wrecked) Obstacle(c.transform.position, Vector3.zero);
            leaderSpeed = lead;
            return best;
        }

        // Room in another lane: nobody within 25 m behind or 35 m ahead.
        bool Clear(TrafficSystem.Lane other)
        {
            foreach (var c in system.cars)
            {
                if (c == this || c.lane != other) continue;
                float g = TrafficSystem.Gap(other, s, c.s);
                if (g > -25f && g < 35f) return false;
            }
            var race = system.race;
            if (race)
                foreach (var r in race.racers)
                {
                    if (!r || !r.gameObject.activeInHierarchy) continue;
                    Vector3 d = r.transform.position - transform.position;
                    if (d.sqrMagnitude < 35f * 35f && Mathf.Abs(Vector3.Dot(d, Vector3.Cross(Vector3.up, transform.forward)) - (other.offset - offset) * other.dir) < 2.5f) return false;
                }
            return true;
        }

        void OnCollisionEnter(Collision c)
        {
            if (wrecked) return;
            // The player crashing into traffic is a crime in free roam.
            bool byPlayer = c.rigidbody && c.rigidbody.TryGetComponent(out Racer racer) && racer.isPlayer;
            if (byPlayer && ChaseMode.Instance)
            {
                float v = c.relativeVelocity.magnitude;
                if (v >= wreckSpeed) ChaseMode.Instance.Crime(1f, "HIT AND RUN");
                else if (v > 3f) ChaseMode.Instance.Crime(0.3f, "RECKLESS DRIVING");
            }
            if (c.rigidbody && c.rigidbody.GetComponent<TrafficCar>() is TrafficCar t && !t.wrecked) return;
            if (c.relativeVelocity.magnitude < wreckSpeed) return;
            // Hard hit: physics takes over, carrying on with the speed it had.
            wrecked = true;
            wreckTime = Time.time;
            body.isKinematic = false;
            body.linearVelocity = velocity;
            body.AddForceAtPosition(c.impulse * 0.6f, c.GetContact(0).point, ForceMode.Impulse);
        }
    }
}
