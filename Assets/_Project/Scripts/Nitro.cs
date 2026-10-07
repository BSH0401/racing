using System.Collections.Generic;
using UnityEngine;

namespace Racing
{
    // Nitrous boost: drivers set Request, the tank burns down while CarController.Boosting pushes the
    // car past its normal top speed. The tank refills slowly on its own and quickly from drifting,
    // air time and (player only) near misses with traffic. Exhaust flames and a blue glow show it.
    [RequireComponent(typeof(CarController))]
    public class Nitro : MonoBehaviour
    {
        public Material flameMaterial;
        [Tooltip("Tank fraction burned per second (a full tank lasts ~3.5 s).")]
        public float drain = 0.28f;
        public float refill = 0.012f;

        [System.NonSerialized] public bool Request;
        [System.NonSerialized] public float Amount = 0.5f;
        public bool Active { get; private set; }
        public int Combo { get; private set; }
        public int Uses { get; private set; }
        public int NearMissCount { get; private set; }

        CarController car;
        Racer racer;
        Transform[] flames;
        Light glow;
        AudioSource whoosh;
        static AudioClip whooshClip;
        float lastMiss = -10f, flameLevel;

        // Near misses: traffic cars currently alongside, and ones we touched (no credit for those).
        readonly HashSet<TrafficCar> alongside = new HashSet<TrafficCar>();
        readonly HashSet<TrafficCar> touched = new HashSet<TrafficCar>();

        static readonly bool noNitro = DevFlags.Has("-nonitro");

        void Awake()
        {
            car = GetComponent<CarController>();
            racer = GetComponent<Racer>();
        }

        void Start()
        {
            if (racer && racer.isPlayer)
            {
                if (!whooshClip) whooshClip = SynthAudio.Noise("Nitro", SynthAudio.Biquad.Kind.LowPass, 700f, 0.7f, 0.4f, 17);
                whoosh = gameObject.AddComponent<AudioSource>();
                whoosh.clip = whooshClip;
                whoosh.loop = true;
                whoosh.playOnAwake = false;
                whoosh.spatialBlend = 0f;
                whoosh.volume = 0f;
                whoosh.Play();
            }
        }

        public void Refill(float amount) => Amount = Mathf.Clamp01(amount);

        void FixedUpdate()
        {
            float dt = Time.fixedDeltaTime;
            float speed = car.ForwardSpeed;
            bool want = Request && !noNitro && !car.InputLocked && speed > 3f && car.GroundedWheels > 0;
            // Needs a little in the tank to light up, then burns until empty or released.
            bool was = Active;
            Active = want && (Active ? Amount > 0f : Amount > 0.08f);
            if (Active && !was) Uses++;
            car.Boosting = Active;

            if (Active) Amount = Mathf.Max(0f, Amount - drain / car.nitroCapacity * dt);
            else if (!car.InputLocked)
            {
                float gain = refill;
                if (Mathf.Abs(car.DriftAngle) > 12f && speed > 12f && car.GroundedWheels >= 2) gain += 0.1f;
                if (car.GroundedWheels == 0 && Mathf.Abs(speed) > 10f) gain += 0.12f;
                Amount = Mathf.Min(1f, Amount + gain / car.nitroCapacity * dt);
            }

            if (racer && racer.isPlayer) NearMisses();
        }

        // A traffic car passed at speed within about a metre and a half, without contact.
        void NearMisses()
        {
            var traffic = TrafficSystem.Instance;
            var rm = RaceManager.Instance;
            if (!traffic || !rm || rm.State != RaceState.Racing) return;
            Vector3 v = car.Body.linearVelocity;
            foreach (var t in traffic.cars)
            {
                Vector3 d = t.transform.position - transform.position;
                if (d.sqrMagnitude > 30f * 30f || t.wrecked) { alongside.Remove(t); touched.Remove(t); continue; }
                Vector3 local = transform.InverseTransformDirection(d);
                float rel = (v - t.Velocity).magnitude;
                if (Mathf.Abs(local.z) < 4.5f && Mathf.Abs(local.x) < 3.6f && Mathf.Abs(local.y) < 2f && rel > 8f && v.magnitude > 15f)
                    alongside.Add(t);
                else if (Mathf.Abs(local.z) > 6f && alongside.Remove(t))
                {
                    if (!touched.Remove(t)) Award(rm);
                }
            }
        }

        void Award(RaceManager rm)
        {
            Combo = Time.time - lastMiss < 4f ? Combo + 1 : 1;
            NearMissCount++;
            lastMiss = Time.time;
            Amount = Mathf.Min(1f, Amount + 0.12f);
            rm.hud.Toast(Combo > 1 ? $"NEAR MISS  x{Combo}" : "NEAR MISS");
            if (ChaseMode.Instance) ChaseMode.Instance.OnNearMiss(Combo);
        }

        void OnCollisionEnter(Collision c)
        {
            if (c.rigidbody && c.rigidbody.TryGetComponent(out TrafficCar t)) touched.Add(t);
        }

        void LateUpdate()
        {
            flameLevel = Mathf.MoveTowards(flameLevel, Active ? 1f : 0f, Time.deltaTime * 8f);
            if (flameLevel > 0f && flames == null) Build();
            if (flames == null) return;
            bool show = flameLevel > 0.01f;
            for (int i = 0; i < flames.Length; i++)
            {
                flames[i].gameObject.SetActive(show);
                if (!show) continue;
                float len = flameLevel * Random.Range(0.75f, 1.25f);
                flames[i].localScale = new Vector3(0.17f, 0.6f * len, 0.17f) * (0.6f + 0.4f * flameLevel);
            }
            glow.enabled = show;
            glow.intensity = flameLevel * Random.Range(5f, 8f);
            if (whoosh) whoosh.volume = flameLevel * 0.35f * (RaceManager.Instance && RaceManager.Instance.Paused ? 0f : 1f);
        }

        // Two flame cones at the back of the body (fitted to the current collider: bodies get swapped).
        void Build()
        {
            var root = new GameObject("NitroFlames").transform;
            root.SetParent(transform, false);
            root.gameObject.layer = 2;
            flames = new Transform[2];
            for (int i = 0; i < 2; i++)
            {
                var f = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                Destroy(f.GetComponent<Collider>());
                f.name = "Flame";
                f.layer = 2;
                f.transform.SetParent(root, false);
                f.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
                var r = f.GetComponent<MeshRenderer>();
                if (flameMaterial) r.sharedMaterial = flameMaterial;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                flames[i] = f.transform;
            }
            glow = new GameObject("NitroGlow").AddComponent<Light>();
            glow.transform.SetParent(root, false);
            glow.type = LightType.Point;
            glow.color = new Color(0.35f, 0.55f, 1f);
            glow.range = 7f;
            glow.shadows = LightShadows.None;
            Fit();
        }

        public void Fit()
        {
            if (flames == null) return;
            var box = GetComponent<BoxCollider>();
            Vector3 c = box ? box.center : Vector3.zero, s = box ? box.size : new Vector3(1.8f, 1.3f, 4.4f);
            float z = c.z - s.z * 0.5f - 0.5f, y = c.y - s.y * 0.32f;
            flames[0].localPosition = new Vector3(-s.x * 0.22f, y, z);
            flames[1].localPosition = new Vector3(s.x * 0.22f, y, z);
            glow.transform.localPosition = new Vector3(0f, y, z - 0.8f);
        }
    }
}
