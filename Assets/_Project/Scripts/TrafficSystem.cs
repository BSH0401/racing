using System.Collections.Generic;
using UnityEngine;

namespace Racing
{
    // Everyday traffic on the highway (three lanes each way) and the national roads (one lane each
    // way). A small pool of cars is kept around the player: cars appear on lanes out of sight
    // 300-700 m away and are recycled once they fall far behind. Each car follows its lane with a
    // car-following model (gap and closing speed to whatever is ahead - other traffic or racers),
    // overtakes on the highway when held up, and turns into a tumbling wreck when hit hard.
    public class TrafficSystem : MonoBehaviour
    {
        public static TrafficSystem Instance { get; private set; }

        public RaceManager race;
        public ThemeController theme;
        [Tooltip("Inactive car templates (body, wheels, collider, kinematic rigidbody, TrafficCar).")]
        public TrafficCar[] templates = new TrafficCar[0];
        public int highwayCars = 26, nationalCars = 10;
        public float spawnMin = 300f, spawnMax = 700f, despawn = 820f;

        public class Lane
        {
            public WorldLayout.Road road;
            public float offset, speed;
            public int dir;          // +1 along the road's samples, -1 against
            public int carriageway;  // highway: +1 outer, -1 inner; national roads: 0
            public int slot;         // highway lane 0 (median) .. 2 (shoulder)
            public float spacing;
        }

        public readonly List<Lane> lanes = new List<Lane>();
        public readonly List<TrafficCar> cars = new List<TrafficCar>();
        readonly List<TrafficCar> pool = new List<TrafficCar>();
        float tick;
        bool lightsOn, viewsMode;

        public bool Enabled => enabled && !DevFlags.Has("-notraffic");

        void Awake()
        {
            Instance = this;
            float m = WorldLayout.MedianHalf, lw = WorldLayout.LaneWidth;
            var hw = WorldLayout.Highway;
            for (int l = 0; l < WorldLayout.HighwayLanes; l++)
            {
                float speed = 33f - 4f * l; // fast lane by the median
                lanes.Add(new Lane { road = hw, offset = m + lw * (l + 0.5f), dir = 1, carriageway = 1, slot = l, speed = speed });
                lanes.Add(new Lane { road = hw, offset = -(m + lw * (l + 0.5f)), dir = -1, carriageway = -1, slot = l, speed = speed });
            }
            foreach (var road in WorldLayout.National)
            {
                lanes.Add(new Lane { road = road, offset = 1.75f, dir = 1, speed = 20f });
                lanes.Add(new Lane { road = road, offset = -1.75f, dir = -1, speed = 20f });
            }
            foreach (var lane in lanes) lane.spacing = lane.road.dist[1];

            if (templates.Length == 0) { enabled = false; return; }
            if (DevFlags.Has("-views")) { viewsMode = true; spawnMin = 25f; spawnMax = 260f; }
            int total = highwayCars + nationalCars;
            for (int i = 0; i < total; i++)
            {
                var t = templates[i % templates.Length];
                var car = Instantiate(t, transform);
                car.name = "Traffic_" + i + "_" + t.name;
                car.system = this;
                car.gameObject.SetActive(false);
                pool.Add(car);
            }
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        // Neighbouring lane on the same carriageway (highway only), or null.
        public Lane Neighbour(Lane lane, int step)
        {
            if (lane.carriageway == 0) return null;
            foreach (var l in lanes)
                if (l.road == lane.road && l.carriageway == lane.carriageway && l.slot == lane.slot + step) return l;
            return null;
        }

        Vector3 Focus(out Vector3 viewPos, out Vector3 viewDir)
        {
            var cam = Camera.main;
            viewPos = cam ? cam.transform.position : Vector3.zero;
            viewDir = cam ? cam.transform.forward : Vector3.forward;
            var p = race ? race.Player : null;
            return p && p.gameObject.activeInHierarchy && !viewsMode ? p.transform.position : viewPos;
        }

        void Update()
        {
            if (DevFlags.Has("-notraffic")) { enabled = false; return; }
            bool night = theme && theme.Current == RaceTheme.Night;
            if (night != lightsOn)
            {
                lightsOn = night;
                foreach (var c in pool) c.SetLights(night);
            }

            tick -= Time.deltaTime;
            if (tick > 0f) return;
            tick = 0.25f;
            Vector3 focus = Focus(out var viewPos, out var viewDir);

            for (int i = cars.Count - 1; i >= 0; i--)
            {
                var c = cars[i];
                float d = Flat(c.transform.position - focus).magnitude;
                bool gone = d > despawn || c.Finished || (c.wrecked && c.WreckAge > 12f && !Visible(c.transform.position, viewPos, viewDir, 250f));
                if (gone) Retire(c);
            }

            int hwy = 0, nat = 0;
            foreach (var c in cars)
                if (c.lane.carriageway != 0) hwy++; else nat++;
            for (int attempt = 0; attempt < (viewsMode ? 60 : 12) && (hwy < highwayCars || nat < nationalCars) && pool.Count > 0; attempt++)
            {
                bool wantHighway = hwy < highwayCars && (nat >= nationalCars || Random.value < 0.7f);
                if (TrySpawn(wantHighway, focus, viewPos, viewDir))
                {
                    if (wantHighway) hwy++; else nat++;
                }
            }
        }

        static bool Visible(Vector3 p, Vector3 viewPos, Vector3 viewDir, float within)
        {
            Vector3 d = p - viewPos;
            return d.magnitude < within || Vector3.Dot(d.normalized, viewDir) > 0.35f && d.magnitude < 900f;
        }

        bool TrySpawn(bool highway, Vector3 focus, Vector3 viewPos, Vector3 viewDir)
        {
            var candidates = new List<Lane>();
            foreach (var l in lanes)
                if ((l.carriageway != 0) == highway) candidates.Add(l);
            var lane = candidates[Random.Range(0, candidates.Count)];
            var road = lane.road;
            int near = NearestSample(road, focus);
            float ahead = Random.Range(spawnMin, spawnMax) * (Random.value < 0.5f ? 1f : -1f);
            float s = near + ahead / lane.spacing;
            if (road.closed) s = Mathf.Repeat(s, road.Count);
            else if (s < 2f || s > road.Count - 3f) return false;
            Vector3 pos = Point(lane, s, lane.offset, out _);
            float dist = Flat(pos - focus).magnitude;
            if (dist < spawnMin * 0.9f || dist > spawnMax * 1.1f) return false;
            if (!viewsMode && Visible(pos, viewPos, viewDir, 200f)) return false;
            // Room to slot in.
            foreach (var c in cars)
                if (c.lane == lane && Mathf.Abs(Gap(lane, c.s, s)) < 35f) return false;
            if (race)
                foreach (var r in race.racers)
                    if (r && r.gameObject.activeInHierarchy && (r.transform.position - pos).sqrMagnitude < 40f * 40f) return false;

            var car = pool[pool.Count - 1];
            pool.RemoveAt(pool.Count - 1);
            float speed = lane.speed * Random.Range(0.85f, 1.05f);
            car.Place(lane, s, speed);
            car.SetLights(lightsOn);
            cars.Add(car);
            return true;
        }

        void Retire(TrafficCar c)
        {
            cars.Remove(c);
            c.gameObject.SetActive(false);
            pool.Add(c);
        }

        static int NearestSample(WorldLayout.Road road, Vector3 p)
        {
            int best = 0;
            float bd = float.MaxValue;
            for (int i = 0; i < road.Count; i += 4)
            {
                float d = (road.pts[i].x - p.x) * (road.pts[i].x - p.x) + (road.pts[i].z - p.z) * (road.pts[i].z - p.z);
                if (d < bd) { bd = d; best = i; }
            }
            return best;
        }

        // Point on a lane at fractional sample s and lateral offset; 'forward' follows the travel direction.
        public static Vector3 Point(Lane lane, float s, float offset, out Vector3 forward)
        {
            var road = lane.road;
            int a = Mathf.FloorToInt(s);
            float t = s - a;
            int i0 = road.Wrap(a), i1 = road.Wrap(a + 1);
            Vector3 p = Vector3.Lerp(road.pts[i0], road.pts[i1], t);
            Vector3 r = Vector3.Lerp(road.right[i0], road.right[i1], t);
            forward = (road.pts[road.Wrap(a + 2)] - road.pts[road.Wrap(a - 1)]).normalized * lane.dir;
            return p + r * offset + Vector3.up * road.Lift;
        }

        // Metres from sample s0 forward (in the lane's travel direction) to sample s1; wraps on the ring.
        public static float Gap(Lane lane, float s0, float s1)
        {
            float d = (s1 - s0) * lane.dir;
            if (lane.road.closed)
            {
                float n = lane.road.Count;
                d = Mathf.Repeat(d + n * 0.5f, n) - n * 0.5f;
            }
            return d * lane.spacing;
        }

        static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);
    }
}
