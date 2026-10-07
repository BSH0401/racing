using System.Collections.Generic;
using UnityEngine;

namespace Racing
{
    // Street furniture that cars can knock flying. It stands as a kinematic trigger (so a car is never
    // stopped dead by a bin); when a car drives into it at speed it turns into a loose rigid body,
    // launched with the car's velocity, and the car loses a little speed. Hydrants leave a water
    // fountain behind. Everything is put back at the start of each race (ResetAll).
    [RequireComponent(typeof(Rigidbody), typeof(BoxCollider))]
    public class Breakable : MonoBehaviour
    {
        public enum Kind { Hydrant, Bin, Barrier }

        public Kind kind;
        public float breakSpeed = 3f;

        public static Material particleMaterial;
        public static AudioClip hitClip;
        static readonly List<Breakable> all = new List<Breakable>();

        Rigidbody body;
        BoxCollider box;
        Vector3 homePos;
        Quaternion homeRot;
        bool broken;
        ParticleSystem fountain;

        void Awake()
        {
            body = GetComponent<Rigidbody>();
            box = GetComponent<BoxCollider>();
            homePos = transform.position;
            homeRot = transform.rotation;
            all.Add(this);
            Stand();
        }

        void OnDestroy() => all.Remove(this);

        public static void ResetAll()
        {
            foreach (var b in all) if (b.broken) b.Stand();
        }

        void Stand()
        {
            broken = false;
            if (!body.isKinematic)
            {
                body.linearVelocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;
            }
            body.isKinematic = true;
            box.isTrigger = true;
            transform.SetPositionAndRotation(homePos, homeRot);
            if (fountain) fountain.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        }

        void OnTriggerEnter(Collider other)
        {
            if (broken || DevFlags.Has("-noprops")) return;
            var rb = other.attachedRigidbody;
            if (!rb || rb.isKinematic) return;
            var car = rb.GetComponent<CarController>();
            Vector3 v = rb.linearVelocity;
            if (!car && !rb.GetComponent<TrafficCar>()) return;
            if (v.magnitude < breakSpeed) return;
            Break(rb, v);
            var racer = car ? car.GetComponent<Racer>() : null;
            if (racer && racer.isPlayer && ChaseMode.Instance) ChaseMode.Instance.Crime(kind == Kind.Barrier ? 0.2f : 0.12f, "VANDALISM");
        }

        // Dev (-smash): knocks over everything within 'radius' of a point, as if hit by a car going by.
        public static void SmashNear(Vector3 p, float radius, Vector3 v)
        {
            foreach (var b in all)
                if (!b.broken && (b.transform.position - p).sqrMagnitude < radius * radius)
                {
                    b.broken = true;
                    b.box.isTrigger = false;
                    b.body.isKinematic = false;
                    b.body.linearVelocity = v + Vector3.up * 4f;
                    b.body.angularVelocity = Random.insideUnitSphere * 8f;
                    if (b.kind == Kind.Hydrant) b.Fountain();
                }
        }

        void Break(Rigidbody hitter, Vector3 v)
        {
            broken = true;
            box.isTrigger = false;
            body.isKinematic = false;
            body.linearVelocity = v * 1.1f + Vector3.up * Mathf.Min(v.magnitude * 0.25f, 6f);
            body.angularVelocity = Random.insideUnitSphere * 8f;
            // Momentum shared with the car: light things barely slow it, a concrete block does.
            float share = hitter.mass / (hitter.mass + body.mass * 0.6f);
            hitter.linearVelocity = Vector3.Scale(hitter.linearVelocity, new Vector3(share, 1f, share));
            if (hitClip) AudioSource.PlayClipAtPoint(hitClip, transform.position, kind == Kind.Barrier ? 0.9f : 0.6f);
            if (kind == Kind.Hydrant) Fountain();
        }

        // A burst main: a column of spray at the hydrant's old spot for half a minute.
        void Fountain()
        {
            if (!particleMaterial) return;
            if (!fountain)
            {
                var go = new GameObject("Fountain");
                go.transform.SetParent(transform.parent, false);
                go.layer = 2;
                fountain = go.AddComponent<ParticleSystem>();
                fountain.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                var main = fountain.main;
                main.duration = 30f;
                main.loop = false;
                main.startLifetime = new ParticleSystem.MinMaxCurve(1.2f, 1.8f);
                main.startSpeed = new ParticleSystem.MinMaxCurve(9f, 12f);
                main.startSize = new ParticleSystem.MinMaxCurve(0.35f, 0.8f);
                main.startColor = new Color(0.85f, 0.92f, 1f, 0.55f);
                main.gravityModifier = 1f;
                main.simulationSpace = ParticleSystemSimulationSpace.World;
                main.maxParticles = 400;
                var emission = fountain.emission;
                emission.rateOverTime = 70f;
                var shape = fountain.shape;
                shape.shapeType = ParticleSystemShapeType.Cone;
                shape.angle = 7f;
                shape.radius = 0.1f;
                var size = fountain.sizeOverLifetime;
                size.enabled = true;
                size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.5f, 1f, 1.8f));
                var rend = go.GetComponent<ParticleSystemRenderer>();
                rend.sharedMaterial = particleMaterial;
                rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
            fountain.transform.SetPositionAndRotation(homePos + Vector3.up * 0.4f, Quaternion.Euler(-90f, 0f, 0f));
            fountain.Play();
        }
    }
}
