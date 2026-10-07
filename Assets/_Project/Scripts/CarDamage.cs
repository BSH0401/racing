using System.Collections.Generic;
using UnityEngine;

namespace Racing
{
    // Crash damage: hard hits dent the body mesh around the impact (vertices pushed in towards the
    // middle of the car), wear down Health and with it engine power, and a battered car smokes from
    // under the bonnet. Everything is repaired at the start of each race (RepairAll).
    [RequireComponent(typeof(CarController))]
    public class CarDamage : MonoBehaviour
    {
        public Material smokeMaterial;
        [Tooltip("Velocity change (m/s) below which a hit leaves no mark.")]
        public float threshold = 2.5f;

        [System.NonSerialized] public float Health = 1f;
        // Damage multiplier on top of tuning (police cars are reinforced).
        [System.NonSerialized] public float toughness = 1f;

        CarController car;
        ParticleSystem smoke;
        float cooldown;

        // Deformed copies of the body meshes, keyed by their renderer, with the original to put back.
        class Dent
        {
            public Mesh original, copy;
            public Vector3[] start, now;
        }
        static readonly Dictionary<MeshFilter, Dent> dents = new Dictionary<MeshFilter, Dent>();
        static readonly List<CarDamage> all = new List<CarDamage>();
        public static int Dented => dents.Count;
        public static int Unreadable { get; private set; }
        public string SmokeDebug()
        {
            if (!smoke) return "none";
            var ps = new ParticleSystem.Particle[4];
            int n = smoke.GetParticles(ps);
            var sb = new System.Text.StringBuilder($"count={smoke.particleCount} emitter={smoke.transform.position} car={transform.position}");
            for (int i = 0; i < n; i++) sb.Append($" p{i}={ps[i].position} size={ps[i].GetCurrentSize(smoke):F2} col={ps[i].GetCurrentColor(smoke)}");
            var r = smoke.GetComponent<ParticleSystemRenderer>();
            sb.Append($" mat={(r.sharedMaterial ? r.sharedMaterial.name + "/" + r.sharedMaterial.shader.name : "null")} enabled={r.enabled}");
            return sb.ToString();
        }

        void Awake() => car = GetComponent<CarController>();
        void OnEnable() => all.Add(this);
        void OnDisable() => all.Remove(this);

        public static void RepairAll()
        {
            foreach (var kv in dents)
            {
                if (kv.Key) kv.Key.sharedMesh = kv.Value.original;
                if (kv.Value.copy) Destroy(kv.Value.copy);
            }
            dents.Clear();
            foreach (var d in all) d.Repair();
        }

        public void Repair()
        {
            Health = 1f;
            if (car && car.bodyVisual)
                foreach (var mf in car.bodyVisual.GetComponentsInChildren<MeshFilter>())
                    if (dents.TryGetValue(mf, out var dent))
                    {
                        mf.sharedMesh = dent.original;
                        Destroy(dent.copy);
                        dents.Remove(mf);
                    }
            if (car) car.enginePower = 1f;
            if (smoke) smoke.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        }

        void Update()
        {
            cooldown -= Time.deltaTime;
            float wear = 1f - Health;
            if (wear > 0.45f && !smoke) BuildSmoke();
            if (!smoke) return;
            var emission = smoke.emission;
            emission.rateOverTime = wear > 0.45f ? Mathf.Lerp(8f, 34f, (wear - 0.45f) / 0.55f) : 0f;
            var main = smoke.main;
            main.startColor = Color.Lerp(new Color(0.8f, 0.8f, 0.8f, 0.35f), new Color(0.15f, 0.15f, 0.15f, 0.55f), (wear - 0.45f) / 0.55f);
            if (!smoke.isPlaying && wear > 0.45f) smoke.Play();
        }

        void OnCollisionEnter(Collision c)
        {
            var rm = RaceManager.Instance;
            if (rm && rm.State == RaceState.Menu || DevFlags.Has("-nodamage")) return;
            if (c.contactCount == 0 || cooldown > 0f) return;
            float dv = c.impulse.magnitude / Mathf.Max(1f, car.Body.mass);
            if (dv < threshold) return;
            // Landing on the road after a jump isn't a crash.
            if (!c.rigidbody && Mathf.Abs(c.GetContact(0).normal.y) > 0.7f) return;
            cooldown = 0.15f;
            float hit = (dv - threshold) * car.damageTaken * toughness;
            Health = Mathf.Max(0f, Health - hit * 0.018f);
            car.enginePower = Mathf.Lerp(0.6f, 1f, Health);
            Deform(c.GetContact(0).point, Mathf.Min(0.045f * hit, 0.28f), Mathf.Clamp(0.6f + 0.04f * dv, 0.6f, 1.2f));
        }

        // Pushes body vertices within 'radius' of the world point in towards the car's centre line.
        void Deform(Vector3 point, float depth, float radius)
        {
            if (depth < 0.005f || !car.bodyVisual) return;
            Vector3 centre = transform.TransformPoint(GetComponent<BoxCollider>().center);
            foreach (var mf in car.bodyVisual.GetComponentsInChildren<MeshFilter>())
            {
                var mesh = mf.sharedMesh;
                if (!mesh) continue;
                Vector3 p = mf.transform.InverseTransformPoint(point);
                Vector3 c = mf.transform.InverseTransformPoint(centre);
                float r = radius / Mathf.Max(mf.transform.lossyScale.x, 1e-4f);
                var b = mesh.bounds;
                b.Expand(r * 2f);
                if (!b.Contains(p)) continue;
                if (!dents.TryGetValue(mf, out var dent))
                {
                    if (!mesh.isReadable) { Unreadable++; continue; }
                    var copy = Instantiate(mesh);
                    copy.name = mesh.name + "_dented";
                    var v = copy.vertices;
                    dent = new Dent { original = mesh, copy = copy, start = v, now = (Vector3[])v.Clone() };
                    dents[mf] = dent;
                    mf.sharedMesh = copy;
                }
                float d = depth / Mathf.Max(mf.transform.lossyScale.x, 1e-4f);
                float limit = 0.35f / Mathf.Max(mf.transform.lossyScale.x, 1e-4f);
                bool changed = false;
                var now = dent.now;
                for (int i = 0; i < now.Length; i++)
                {
                    float dist = (now[i] - p).magnitude;
                    if (dist > r) continue;
                    float k = 1f - dist / r;
                    Vector3 dir = c - now[i];
                    dir.y *= 0.3f;
                    Vector3 moved = now[i] + dir.normalized * d * k * k;
                    // Never crumple more than 35 cm from the original shape.
                    Vector3 off = moved - dent.start[i];
                    if (off.magnitude > limit) moved = dent.start[i] + off.normalized * limit;
                    now[i] = moved;
                    changed = true;
                }
                if (!changed) continue;
                dent.copy.vertices = now;
                dent.copy.RecalculateNormals();
            }
        }

        // Dev (-batter): a few heavy knocks all round, to look at dents and smoke.
        public void Batter()
        {
            var box = GetComponent<BoxCollider>();
            Vector3 c = box.center, s = box.size * 0.5f;
            Vector3[] spots = { new Vector3(0f, 0f, s.z), new Vector3(-s.x, 0.1f, s.z * 0.4f), new Vector3(s.x, 0.1f, -s.z * 0.3f), new Vector3(-s.x * 0.6f, 0f, -s.z) };
            foreach (var p in spots) Deform(transform.TransformPoint(c + p), 0.28f, 1f);
            Health = 0.2f;
            car.enginePower = Mathf.Lerp(0.6f, 1f, Health);
        }

        void BuildSmoke()
        {
            var go = new GameObject("DamageSmoke");
            go.transform.SetParent(transform, false);
            var box = GetComponent<BoxCollider>();
            go.transform.localPosition = box.center + new Vector3(0f, box.size.y * 0.3f, box.size.z * 0.3f);
            go.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);
            go.layer = 2;
            smoke = go.AddComponent<ParticleSystem>();
            smoke.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = smoke.main;
            main.duration = 1f;
            main.loop = true;
            main.startLifetime = new ParticleSystem.MinMaxCurve(1.2f, 2.2f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.8f, 2f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.6f, 1.1f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.gravityModifier = -0.05f;
            main.maxParticles = 120;
            var shape = smoke.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 12f;
            shape.radius = 0.25f;
            var size = smoke.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.6f, 1f, 2.8f));
            var col = smoke.colorOverLifetime;
            col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.15f), new GradientAlphaKey(0f, 1f) });
            col.color = g;
            var rend = go.GetComponent<ParticleSystemRenderer>();
            rend.sharedMaterial = smokeMaterial;
            rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }
    }
}
