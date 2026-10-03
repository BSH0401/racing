using UnityEngine;

namespace Racing
{
    // Raycast-suspension car with a slip-angle tyre model, tuned for a weighty but forgiving
    // GTA-style feel: load transfer from the springs, rear-biased drive, progressive slides,
    // automatic counter-steer and visible body roll/pitch. Drivers write Throttle / Steer / Handbrake.
    [RequireComponent(typeof(Rigidbody))]
    public class CarController : MonoBehaviour
    {
        [Header("Wheels (FL, FR, RL, RR)")]
        public Transform[] wheelVisuals = new Transform[4];
        public Vector3[] wheelAnchors =
        {
            new Vector3(-0.82f, 0f, 1.35f), new Vector3(0.82f, 0f, 1.35f),
            new Vector3(-0.82f, 0f, -1.3f), new Vector3(0.82f, 0f, -1.3f),
        };
        public float wheelRadius = 0.36f;
        public float suspensionRest = 0.28f;
        public float springStrength = 27000f;
        public float damper = 2900f;
        public float antiRoll = 9000f;

        [Header("Engine")]
        public float maxSpeed = 58f;
        public float reverseMaxSpeed = 14f;
        public float acceleration = 12f;
        public float brakeDeceleration = 18f;
        public float coastDeceleration = 1.2f;
        [Range(0f, 1f)] public float rearDriveShare = 0.65f;
        [Tooltip("Traction control: share of a tyre's grip the engine may use.")]
        [Range(0.3f, 1f)] public float tractionLimit = 0.7f;

        [Header("Steering")]
        public float maxSteerLow = 36f;
        public float maxSteerHigh = 13f;
        public float steerSpeed = 160f;
        [Tooltip("Front wheels follow the direction of travel when the rear steps out.")]
        public float counterSteer = 0.8f;
        [Tooltip("Yaw damping applied when the driver is not steering.")]
        public float yawStability = 1.6f;
        [Tooltip("Extra yaw damping once the car is sliding (stability control).")]
        public float slideStability = 3f;

        [Header("Tyres")]
        public float tireGrip = 1.25f;
        public float frontGrip = 1f;
        public float rearGrip = 1.12f;
        public float handbrakeGrip = 0.35f;
        public float handbrakeForce = 9f;
        public float downforce = 4f;

        [Header("Body (visual only)")]
        public Transform bodyVisual;
        public float rollPerG = 4.5f;
        public float pitchPerG = 3f;

        [System.NonSerialized] public float Throttle;
        [System.NonSerialized] public float Steer;
        [System.NonSerialized] public bool Handbrake;
        [System.NonSerialized] public bool InputLocked;

        public float ForwardSpeed => Vector3.Dot(rb.linearVelocity, transform.forward);
        public float SpeedKmh => rb.linearVelocity.magnitude * 3.6f;
        public float SteerAngle { get; private set; }
        public int GroundedWheels { get; private set; }
        public bool OffRoad { get; private set; }
        public float DriftAngle { get; private set; }
        public float CurrentMaxSteer => Mathf.Lerp(maxSteerLow, maxSteerHigh, Mathf.Clamp01(Mathf.Abs(ForwardSpeed) / (maxSpeed * 0.8f)));
        public Rigidbody Body => rb;

        Rigidbody rb;
        readonly float[] hitDistance = new float[4];
        readonly float[] compression = new float[4];
        readonly float[] load = new float[4];
        readonly float[] surfaceGrip = new float[4];
        readonly bool[] grounded = new bool[4];
        readonly RaycastHit[] hits = new RaycastHit[4];
        float wheelSpin;
        int groundMask;
        Vector3 lastVelocity, smoothedAccel;

        void Awake()
        {
            rb = GetComponent<Rigidbody>();
            rb.centerOfMass = new Vector3(0f, -0.3f, 0.05f);
            groundMask = ~((1 << 2) | (1 << 31));
        }

        void FixedUpdate()
        {
            float dt = Time.fixedDeltaTime;
            float throttle = InputLocked ? 0f : Mathf.Clamp(Throttle, -1f, 1f);
            float steerIn = InputLocked ? 0f : Mathf.Clamp(Steer, -1f, 1f);
            bool handbrake = !InputLocked && Handbrake;

            Vector3 up = transform.up;
            Vector3 vel = rb.linearVelocity;
            float fwdSpeed = Vector3.Dot(vel, transform.forward);

            Suspension(up, dt);

            // Body slip: angle between heading and direction of travel (positive = sliding to the right).
            Vector3 flatVel = Vector3.ProjectOnPlane(vel, up);
            DriftAngle = flatVel.magnitude > 4f && fwdSpeed > 1f ? Vector3.SignedAngle(transform.forward, flatVel, up) : 0f;

            // Steering: speed-sensitive lock plus automatic counter-steer into a slide.
            float target = steerIn * CurrentMaxSteer;
            // Normal cornering already has a few degrees of body slip, so only assist beyond that.
            float slide = Mathf.Sign(DriftAngle) * Mathf.Max(0f, Mathf.Abs(DriftAngle) - 4f);
            if (GroundedWheels > 0) target += counterSteer * Mathf.Clamp(slide, -30f, 30f);
            target = Mathf.Clamp(target, -maxSteerLow, maxSteerLow);
            SteerAngle = Mathf.MoveTowards(SteerAngle, target, steerSpeed * dt);

            TrackAcceleration(dt);

            if (GroundedWheels == 0)
            {
                // Keep the car level in the air.
                rb.AddTorque(Vector3.Cross(up, Vector3.up) * 6f, ForceMode.Acceleration);
                return;
            }

            // Total longitudinal demand (N).
            float mass = rb.mass;
            float drive = 0f, brake = 0f;
            if (InputLocked)
            {
                brake = 30f * mass;
            }
            else if (throttle > 0.01f)
            {
                if (fwdSpeed < -0.5f) brake = brakeDeceleration * throttle * mass;
                else drive = acceleration * throttle * Mathf.Clamp01(1f - Mathf.Pow(Mathf.Max(fwdSpeed, 0f) / maxSpeed, 2f)) * mass;
            }
            else if (throttle < -0.01f)
            {
                if (fwdSpeed > 0.5f) brake = brakeDeceleration * -throttle * mass;
                else drive = -acceleration * 0.6f * -throttle * Mathf.Clamp01(1f - Mathf.Abs(fwdSpeed) / reverseMaxSpeed) * mass;
            }
            else
            {
                brake = coastDeceleration * mass;
            }

            float massPerWheel = mass / 4f;
            float comY = rb.centerOfMass.y;
            int offRoad = 0;

            for (int i = 0; i < 4; i++)
            {
                if (!grounded[i]) continue;
                bool front = i < 2;
                if (surfaceGrip[i] < 1f) offRoad++;

                Vector3 normal = hits[i].normal;
                Vector3 heading = front ? Quaternion.AngleAxis(SteerAngle, up) * transform.forward : transform.forward;
                Vector3 wheelFwd = Vector3.ProjectOnPlane(heading, normal).normalized;
                Vector3 wheelRight = Vector3.Cross(normal, wheelFwd).normalized;
                Vector3 contact = hits[i].point;
                Vector3 pv = rb.GetPointVelocity(contact);
                float vLong = Vector3.Dot(pv, wheelFwd);
                float vLat = Vector3.Dot(pv, wheelRight);

                float n = load[i];
                float mu = tireGrip * surfaceGrip[i];
                float maxGrip = mu * n;

                // Longitudinal: drive split front/rear, brakes 60/40, handbrake on the rear.
                float fx = drive * (front ? (1f - rearDriveShare) : rearDriveShare) * 0.5f;
                fx = Mathf.Clamp(fx, -maxGrip * tractionLimit, maxGrip * tractionLimit);
                float wheelBrake = brake * (front ? 0.3f : 0.2f);
                if (handbrake && !front) wheelBrake += handbrakeForce * massPerWheel;
                fx -= Mathf.Sign(vLong) * Mathf.Min(wheelBrake, Mathf.Abs(vLong) * massPerWheel / dt);
                fx = Mathf.Clamp(fx, -maxGrip, maxGrip);

                // Lateral: slip-angle curve with a soft peak; grip left after longitudinal use (friction ellipse).
                float slip = Mathf.Atan2(vLat, Mathf.Max(Mathf.Abs(vLong), 0.5f));
                float latMu = mu * (front ? frontGrip : (handbrake ? handbrakeGrip : rearGrip));
                float used = maxGrip > 1f ? fx / maxGrip : 0f;
                float latMax = latMu * n * Mathf.Sqrt(Mathf.Max(0.05f, 1f - 0.5f * used * used));
                float fy = -TyreCurve(slip) * latMax;
                // Never push harder than needed to stop the sideways motion this step (low-speed stability).
                float cancel = Mathf.Abs(vLat) * massPerWheel / dt;
                fy = Mathf.Clamp(fy, -cancel, cancel);

                Vector3 at = transform.TransformPoint(new Vector3(wheelAnchors[i].x, comY - 0.15f, wheelAnchors[i].z));
                rb.AddForceAtPosition(wheelFwd * fx + wheelRight * fy, at);
            }

            OffRoad = offRoad >= 2;

            // Settle the yaw when the driver lets go of the wheel.
            float yawRate = Vector3.Dot(rb.angularVelocity, up);
            float release = 1f - Mathf.Abs(steerIn);
            rb.AddTorque(-up * yawRate * yawStability * release, ForceMode.Acceleration);
            // Stability control: resist rotation that makes a slide worse, so slides stay catchable.
            float slideAmount = Mathf.Clamp01((Mathf.Abs(DriftAngle) - 8f) / 30f);
            if (slideAmount > 0f && yawRate * DriftAngle < 0f)
                rb.AddTorque(-up * yawRate * slideStability * slideAmount, ForceMode.Acceleration);

            float speedFrac = Mathf.Clamp01(Mathf.Abs(fwdSpeed) / maxSpeed);
            rb.AddForce(-up * downforce * speedFrac * speedFrac * mass * GroundedWheels / 4f);
        }

        void Suspension(Vector3 up, float dt)
        {
            float rayLength = suspensionRest + wheelRadius;
            GroundedWheels = 0;
            for (int i = 0; i < 4; i++)
            {
                Vector3 origin = transform.TransformPoint(wheelAnchors[i]);
                grounded[i] = Physics.Raycast(origin, -up, out hits[i], rayLength, groundMask, QueryTriggerInteraction.Ignore);
                hitDistance[i] = grounded[i] ? hits[i].distance : rayLength;
                compression[i] = rayLength - hitDistance[i];
                load[i] = 0f;
                if (!grounded[i]) continue;

                GroundedWheels++;
                var surface = hits[i].collider.GetComponent<TrackSurface>();
                surfaceGrip[i] = surface ? surface.gripMultiplier : 1f;

                float pointVel = Vector3.Dot(rb.GetPointVelocity(origin), up);
                float force = Mathf.Max(0f, compression[i] * springStrength - pointVel * damper);
                load[i] = force;
                rb.AddForceAtPosition(up * force, origin);
            }

            // Anti-roll bars keep the body flat enough to stay planted.
            for (int axle = 0; axle < 4; axle += 2)
            {
                int l = axle, r = axle + 1;
                float f = (compression[l] - compression[r]) * antiRoll;
                if (grounded[l]) { rb.AddForceAtPosition(-up * f, transform.TransformPoint(wheelAnchors[l])); load[l] = Mathf.Max(0f, load[l] - f); }
                if (grounded[r]) { rb.AddForceAtPosition(up * f, transform.TransformPoint(wheelAnchors[r])); load[r] = Mathf.Max(0f, load[r] + f); }
            }
        }

        // Normalised lateral force vs slip angle (Pacejka-style): peaks near 11 degrees, keeps ~75% when sliding.
        static float TyreCurve(float slip)
        {
            const float B = 9f, C = 1.45f, E = 0.2f;
            float bx = B * slip;
            return Mathf.Sin(C * Mathf.Atan(bx - E * (bx - Mathf.Atan(bx))));
        }

        void TrackAcceleration(float dt)
        {
            Vector3 v = rb.linearVelocity;
            Vector3 a = (v - lastVelocity) / dt;
            lastVelocity = v;
            smoothedAccel = Vector3.Lerp(smoothedAccel, transform.InverseTransformDirection(a), 1f - Mathf.Exp(-8f * dt));
        }

        void LateUpdate()
        {
            if (!rb) return;
            wheelSpin += ForwardSpeed / wheelRadius * Mathf.Rad2Deg * Time.deltaTime;
            wheelSpin %= 360f;
            float rayLength = suspensionRest + wheelRadius;
            for (int i = 0; i < 4; i++)
            {
                var w = wheelVisuals[i];
                if (!w) continue;
                float drop = Mathf.Min(hitDistance[i], rayLength) - wheelRadius;
                w.localPosition = wheelAnchors[i] - Vector3.up * drop;
                float yaw = i < 2 ? SteerAngle : 0f;
                w.localRotation = Quaternion.Euler(wheelSpin, yaw, 0f);
            }

            if (bodyVisual)
            {
                // Lean out of corners, dive under braking, squat under power.
                const float g = 9.81f;
                float roll = Mathf.Clamp(smoothedAccel.x / g * rollPerG, -6f, 6f);
                float pitch = Mathf.Clamp(-smoothedAccel.z / g * pitchPerG, -4f, 4f);
                bodyVisual.localRotation = Quaternion.Euler(pitch, 0f, roll);
            }
        }

        public void Teleport(Vector3 position, Quaternion rotation)
        {
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            rb.position = position;
            rb.rotation = rotation;
            transform.SetPositionAndRotation(position, rotation);
            SteerAngle = 0f;
            lastVelocity = Vector3.zero;
            smoothedAccel = Vector3.zero;
            Physics.SyncTransforms();
        }
    }
}
