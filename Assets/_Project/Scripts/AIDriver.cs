using UnityEngine;

namespace Racing
{
    // Follows the track centre line with a lane offset, plans corner speeds from curvature
    // and dodges cars directly ahead.
    [RequireComponent(typeof(CarController), typeof(Racer))]
    public class AIDriver : MonoBehaviour
    {
        [Range(0.7f, 1f)] public float skill = 0.94f;
        public float cornerGrip = 1.45f;
        const float PadSpeed = 11f;
        [System.NonSerialized] public float speedScale = 1f;
        [System.NonSerialized] public float difficulty = 1f;

        CarController car;
        Racer racer;
        float laneOffset, laneTarget, laneTimer, trafficCap = float.MaxValue;

        void Awake()
        {
            car = GetComponent<CarController>();
            racer = GetComponent<Racer>();
        }

        void FixedUpdate()
        {
            var rm = RaceManager.Instance;
            if (!rm || car.InputLocked) return;
            var track = rm.track;
            float dt = Time.fixedDeltaTime;
            float speed = car.ForwardSpeed;

            UpdateLane(rm, track, dt);

            // Steering towards a look-ahead point.
            float look = 9f + Mathf.Abs(speed) * 0.5f;
            int ti = track.IndexAhead(racer.index, look);
            Vector3 target = track.Point(ti) + track.Right(ti) * laneOffset;
            Vector3 local = transform.InverseTransformPoint(target);
            float angle = Mathf.Atan2(local.x, local.z) * Mathf.Rad2Deg;
            car.Steer = Mathf.Clamp(angle / Mathf.Max(car.CurrentMaxSteer, 5f), -1f, 1f);

            // Speed planning: v_corner = sqrt(mu * g * R), then check braking distance to reach it.
            float top = car.maxSpeed * skill * speedScale * difficulty;
            float targetSpeed = top;
            float brake = car.brakeDeceleration * 0.75f;
            float horizon = 20f + speed * speed / (2f * brake);
            for (float d = 0f; d <= horizon; d += 8f)
            {
                // Very narrow route width marks a tight junction (ramp terminal pads): take it slowly.
                if (track.HalfWidth(track.IndexAhead(racer.index, d)) < 2.6f)
                    targetSpeed = Mathf.Min(targetSpeed, Mathf.Sqrt(PadSpeed * PadSpeed + 2f * brake * d));
                float turn = track.TurnAngle(racer.index, d, d + 24f) * Mathf.Deg2Rad;
                if (turn < 0.02f) continue;
                float radius = 24f / turn;
                float vCorner = Mathf.Sqrt(cornerGrip * difficulty * 9.81f * radius);
                float allowed = Mathf.Sqrt(vCorner * vCorner + 2f * brake * d);
                targetSpeed = Mathf.Min(targetSpeed, allowed);
            }
            if (car.OffRoad) targetSpeed = Mathf.Min(targetSpeed, top * 0.7f);
            targetSpeed = Mathf.Min(targetSpeed, trafficCap);

            float throttle;
            if (speed < targetSpeed - 1f) throttle = 1f;
            else if (speed > targetSpeed + 1.5f) throttle = -Mathf.Clamp01((speed - targetSpeed) / 5f);
            else throttle = 0.35f;
            if (Mathf.Abs(angle) > 50f && speed > 12f) throttle = Mathf.Min(throttle, 0.2f);

            car.Throttle = throttle;
            car.Handbrake = false;
        }

        void UpdateLane(RaceManager rm, TrackPath track, float dt)
        {
            // Narrowest road over the next stretch, so we line up before a ramp or a pad.
            float half = track.HalfWidth(racer.index);
            float ahead = 25f + Mathf.Max(car.ForwardSpeed, 0f) * 1.5f;
            for (float d = 10f; d <= ahead; d += 10f) half = Mathf.Min(half, track.HalfWidth(track.IndexAhead(racer.index, d)));
            float maxLane = Mathf.Max(0.5f, half - 2f);
            laneTimer -= dt;
            if (laneTimer <= 0f)
            {
                laneTimer = Random.Range(3f, 7f);
                laneTarget = Random.Range(-maxLane * 0.6f, maxLane * 0.6f);
                // Two-lane country road with traffic about: keep to the right-hand lane.
                if (maxLane < 3f && TrafficSystem.Instance) laneTarget = Random.Range(0.4f, maxLane * 0.7f);
            }

            foreach (var other in rm.racers)
            {
                if (other == racer || !other.gameObject.activeSelf) continue;
                Vector3 local = transform.InverseTransformPoint(other.transform.position);
                if (local.z < 0f || local.z > 16f || Mathf.Abs(local.x) > 2.6f) continue;
                if (other.car.ForwardSpeed > car.ForwardSpeed + 2f) continue;
                float otherLane = track.LateralOffset(other.transform.position, other.index);
                laneTarget = otherLane > 0f ? otherLane - 4f : otherLane + 4f;
                laneTimer = 2f;
                break;
            }

            DodgeTraffic(track, maxLane);

            laneTarget = Mathf.Clamp(laneTarget, -maxLane, maxLane);
            // Back inside the road quickly when it narrows, gently otherwise.
            laneOffset = Mathf.MoveTowards(laneOffset, laneTarget, (Mathf.Abs(laneOffset) > maxLane ? 5f : 2.5f) * dt);
        }

        // Traffic ahead in our path: swerve to whichever side has room, or slow to its speed.
        void DodgeTraffic(TrackPath track, float maxLane)
        {
            trafficCap = float.MaxValue;
            var traffic = TrafficSystem.Instance;
            if (!traffic) return;
            float speed = car.ForwardSpeed;
            foreach (var t in traffic.cars)
            {
                Vector3 local = transform.InverseTransformPoint(t.transform.position);
                float theirs = Vector3.Dot(t.Velocity, transform.forward);
                float closing = Mathf.Max(0f, speed - theirs);
                float reach = 14f + closing * 1.7f;
                if (local.z < 0f || local.z > reach || Mathf.Abs(local.x) > 2.7f || Mathf.Abs(local.y) > 3f) continue;
                float tLane = track.LateralOffset(t.transform.position, racer.index);
                float left = tLane - 3.9f, right = tLane + 3.9f;
                bool leftOk = left >= -maxLane, rightOk = right <= maxLane;
                if (leftOk && (!rightOk || Mathf.Abs(left - laneOffset) < Mathf.Abs(right - laneOffset))) laneTarget = left;
                else if (rightOk) laneTarget = right;
                else trafficCap = Mathf.Min(trafficCap, Mathf.Max(theirs + 2f, 4f));
                laneTimer = 2f;
                if (local.z < 14f && Mathf.Abs(local.x) < 1.9f) trafficCap = Mathf.Min(trafficCap, Mathf.Max(theirs + 1f, 4f));
            }
        }

        public void ResetLane()
        {
            laneOffset = laneTarget = 0f;
            laneTimer = 0f;
        }
    }
}
