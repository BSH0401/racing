using UnityEngine;

namespace Racing
{
    // Arcade raycast-suspension car. Drivers (player or AI) write Throttle / Steer / Handbrake.
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
        public float suspensionRest = 0.25f;
        public float springStrength = 32000f;
        public float damper = 3000f;

        [Header("Engine")]
        public float maxSpeed = 58f;
        public float reverseMaxSpeed = 14f;
        public float acceleration = 13f;
        public float brakeDeceleration = 24f;
        public float coastDeceleration = 1.2f;

        [Header("Handling")]
        public float maxSteerLow = 32f;
        public float maxSteerHigh = 7f;
        public float steerSpeed = 140f;
        public float frontGrip = 1f;
        public float rearGrip = 0.95f;
        public float handbrakeGrip = 0.3f;
        public float tireFriction = 1.6f;
        public float downforce = 5f;

        [System.NonSerialized] public float Throttle;
        [System.NonSerialized] public float Steer;
        [System.NonSerialized] public bool Handbrake;
        [System.NonSerialized] public bool InputLocked;

        public float ForwardSpeed => Vector3.Dot(rb.linearVelocity, transform.forward);
        public float SpeedKmh => rb.linearVelocity.magnitude * 3.6f;
        public float SteerAngle { get; private set; }
        public int GroundedWheels { get; private set; }
        public bool OffRoad { get; private set; }
        public float CurrentMaxSteer => Mathf.Lerp(maxSteerLow, maxSteerHigh, Mathf.Clamp01(Mathf.Abs(ForwardSpeed) / maxSpeed));
        public Rigidbody Body => rb;

        Rigidbody rb;
        readonly float[] hitDistance = new float[4];
        readonly bool[] grounded = new bool[4];
        float wheelSpin;
        int groundMask;

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
            bool handbrake = InputLocked || Handbrake;

            SteerAngle = Mathf.MoveTowards(SteerAngle, steerIn * CurrentMaxSteer, steerSpeed * dt);

            Vector3 up = transform.up;
            float massPerWheel = rb.mass / 4f;
            float rayLength = suspensionRest + wheelRadius;
            Vector3 groundNormal = Vector3.zero;
            float gripSum = 0f, surfaceDrag = 0f;
            GroundedWheels = 0;
            int offRoadWheels = 0;

            // Suspension.
            for (int i = 0; i < 4; i++)
            {
                Vector3 origin = transform.TransformPoint(wheelAnchors[i]);
                grounded[i] = Physics.Raycast(origin, -up, out RaycastHit hit, rayLength, groundMask, QueryTriggerInteraction.Ignore);
                hitDistance[i] = grounded[i] ? hit.distance : rayLength;
                if (!grounded[i]) continue;

                GroundedWheels++;
                groundNormal += hit.normal;
                var surface = hit.collider.GetComponent<TrackSurface>();
                if (surface)
                {
                    offRoadWheels++;
                    gripSum += surface.gripMultiplier;
                    surfaceDrag += surface.drag;
                }
                else gripSum += 1f;

                float compression = rayLength - hit.distance;
                float pointVel = Vector3.Dot(rb.GetPointVelocity(origin), up);
                float force = Mathf.Max(0f, compression * springStrength - pointVel * damper);
                rb.AddForceAtPosition(up * force, origin);
            }

            OffRoad = offRoadWheels >= 2;

            if (GroundedWheels == 0)
            {
                // Keep the car level in the air.
                Vector3 axis = Vector3.Cross(up, Vector3.up);
                rb.AddTorque(axis * 6f, ForceMode.Acceleration);
                return;
            }

            groundNormal.Normalize();
            float groundedFrac = GroundedWheels / 4f;
            float gripMul = gripSum / GroundedWheels;
            surfaceDrag /= 4f;

            // Longitudinal.
            Vector3 fwd = Vector3.ProjectOnPlane(transform.forward, groundNormal).normalized;
            float v = Vector3.Dot(rb.linearVelocity, fwd);
            float accel;
            if (throttle > 0.01f)
            {
                if (v < -0.5f) accel = brakeDeceleration * throttle;
                else accel = acceleration * throttle * Mathf.Clamp01(1f - Mathf.Pow(Mathf.Max(v, 0f) / maxSpeed, 2f));
            }
            else if (throttle < -0.01f)
            {
                if (v > 0.5f) accel = -brakeDeceleration * -throttle;
                else accel = -acceleration * 0.6f * -throttle * Mathf.Clamp01(1f - Mathf.Abs(v) / reverseMaxSpeed);
            }
            else
            {
                accel = -Mathf.Sign(v) * Mathf.Min(coastDeceleration, Mathf.Abs(v) / dt);
            }

            if (handbrake) accel -= Mathf.Sign(v) * Mathf.Min(InputLocked ? 30f : 7f, Mathf.Abs(v) / dt);
            accel -= v * surfaceDrag;

            // Never let braking flip the direction of travel within one step.
            bool braking = (throttle > 0.01f && v < -0.5f) || (throttle < -0.01f && v > 0.5f);
            if (braking && Mathf.Abs(accel * dt) > Mathf.Abs(v)) accel = -v / dt;

            rb.AddForce(fwd * accel * rb.mass * groundedFrac);

            // Lateral tyre grip per wheel.
            float comY = rb.centerOfMass.y;
            for (int i = 0; i < 4; i++)
            {
                if (!grounded[i]) continue;
                bool front = i < 2;
                Vector3 wheelRight = front ? Quaternion.AngleAxis(SteerAngle, up) * transform.right : transform.right;
                Vector3 wheelPos = transform.TransformPoint(wheelAnchors[i]);
                float lat = Vector3.Dot(rb.GetPointVelocity(wheelPos), wheelRight);
                float grip = front ? frontGrip : (handbrake ? handbrakeGrip : rearGrip);
                grip *= gripMul;
                float desired = -lat * grip * massPerWheel / dt;
                float maxForce = massPerWheel * 9.81f * tireFriction * grip;
                desired = Mathf.Clamp(desired, -maxForce, maxForce);
                Vector3 at = transform.TransformPoint(new Vector3(wheelAnchors[i].x, comY, wheelAnchors[i].z));
                rb.AddForceAtPosition(wheelRight * desired, at);
            }

            float speedFrac = Mathf.Clamp01(Mathf.Abs(v) / maxSpeed);
            rb.AddForce(-up * downforce * speedFrac * speedFrac * rb.mass * groundedFrac);
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
        }

        public void Teleport(Vector3 position, Quaternion rotation)
        {
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            rb.position = position;
            rb.rotation = rotation;
            transform.SetPositionAndRotation(position, rotation);
            SteerAngle = 0f;
            Physics.SyncTransforms();
        }
    }
}
