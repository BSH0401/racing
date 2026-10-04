using UnityEngine;
using UnityEngine.InputSystem;

namespace Racing
{
    // Smoothed third-person chase camera; C / gamepad right shoulder toggles near/far.
    // In cinematic mode (main menu) it cuts between TV-style shots of random cars.
    [RequireComponent(typeof(Camera))]
    public class ChaseCamera : MonoBehaviour
    {
        public Rigidbody target;
        public float distance = 7f;
        public float height = 2.6f;
        public float lookAhead = 5f;
        public float follow = 7f;

        [Header("Free look (mouse / right stick)")]
        public float mouseSensitivity = 0.15f;
        public float stickSpeed = 140f;
        [Tooltip("Seconds without look input before the camera swings back behind the car.")]
        public float recenterDelay = 1.2f;
        public float recenterSpeed = 3f;
        [System.NonSerialized] public bool cinematic;
        [Tooltip("While set (garage screen), the cinematic camera slowly circles this car.")]
        [System.NonSerialized] public Transform showcase;

        enum Shot { Trackside, Aerial, FrontLow, Orbit, Chase }

        Camera cam;
        bool near;
        float yaw;
        float shotTimer;
        Shot shot;
        Racer subject;
        Vector3 fixedPos;
        float orbitAngle;
        float lookYaw, lookPitch, lookIdle;

        void Awake()
        {
            cam = GetComponent<Camera>();
            // Dev: -lookyaw N holds the free-look camera N degrees round (screenshots of the orbit).
            if (DevFlags.Has("-lookyaw")) { devLookYaw = DevFlags.GetFloat("-lookyaw", 0f); recenterDelay = float.MaxValue; }
        }

        float devLookYaw;

        void Update()
        {
            var rm = RaceManager.Instance;
            // Mouse look while driving: hide and lock the cursor; menus, pause and results get it back.
            bool driving = !cinematic && rm && (rm.State == RaceState.Countdown || rm.State == RaceState.Racing) && !rm.Paused && !rm.Transitioning;
            var wantLock = driving ? CursorLockMode.Locked : CursorLockMode.None;
            if (Cursor.lockState != wantLock) { Cursor.lockState = wantLock; Cursor.visible = !driving; }
            if (cinematic) return;

            var kb = Keyboard.current;
            var gp = Gamepad.current;
            if ((kb != null && kb.cKey.wasPressedThisFrame) || (gp != null && gp.rightShoulder.wasPressedThisFrame)) near = !near;

            Vector2 look = Vector2.zero;
            if (driving && Mouse.current != null) look += Mouse.current.delta.ReadValue() * mouseSensitivity;
            if (gp != null) look += gp.rightStick.ReadValue() * stickSpeed * Time.unscaledDeltaTime;
            if (look.sqrMagnitude > 0.0001f)
            {
                lookYaw = Mathf.Repeat(lookYaw + look.x + 180f, 360f) - 180f;
                lookPitch = Mathf.Clamp(lookPitch - look.y, -12f, 55f);
                lookIdle = 0f;
            }
            else
            {
                lookIdle += Time.unscaledDeltaTime;
                if (lookIdle > recenterDelay)
                {
                    float k = 1f - Mathf.Exp(-recenterSpeed * Time.unscaledDeltaTime);
                    lookYaw = Mathf.LerpAngle(lookYaw, 0f, k);
                    lookPitch = Mathf.Lerp(lookPitch, 0f, k);
                }
            }
        }

        void LateUpdate()
        {
            if (cinematic) { Cinematic(); return; }
            if (!target) return;
            Place(1f - Mathf.Exp(-follow * Time.deltaTime));
        }

        public void Snap()
        {
            shotTimer = 0f;
            lookYaw = devLookYaw;
            lookPitch = 0f;
            if (target && !cinematic) Place(1f, true);
        }

        void Place(float t, bool snap = false)
        {
            Transform tr = target.transform;
            Vector3 fwd = tr.forward;
            fwd.y = 0f;
            if (fwd.sqrMagnitude < 0.001f) fwd = Vector3.forward;
            float targetYaw = Mathf.Atan2(fwd.x, fwd.z) * Mathf.Rad2Deg;
            yaw = snap ? targetYaw : Mathf.LerpAngle(yaw, targetYaw, t);
            Vector3 dir = Quaternion.Euler(0f, yaw + lookYaw, 0f) * Vector3.forward;

            float speed = target.linearVelocity.magnitude;
            float dist = (near ? distance * 0.65f : distance) + speed * 0.03f;
            float h = near ? height * 0.7f : height;
            // Free look orbits the car; the further round, the more the camera looks at the car itself.
            float pitch = lookPitch * Mathf.Deg2Rad;
            Vector3 desired = tr.position - dir * dist * Mathf.Cos(pitch) + Vector3.up * (h + dist * Mathf.Sin(pitch));
            // Follow rigidly while looking around so the orbit doesn't lag behind the mouse.
            float rigid = Mathf.Clamp01(Mathf.Max(Mathf.Abs(lookYaw), Mathf.Abs(lookPitch)) / 10f);
            transform.position = snap ? desired : Vector3.Lerp(transform.position, desired, Mathf.Lerp(Mathf.Clamp01(t * 2f), 1f, rigid));
            float ahead = lookAhead * Mathf.Clamp01(1f - Mathf.Abs(lookYaw) / 45f);
            Vector3 look = tr.position + dir * ahead + Vector3.up * 0.9f;
            transform.rotation = Quaternion.LookRotation(look - transform.position);

            cam.fieldOfView = Mathf.Lerp(cam.fieldOfView, 60f + Mathf.Clamp01(speed / 55f) * 14f, snap ? 1f : t);
        }

        void Cinematic()
        {
            var rm = RaceManager.Instance;
            if (!rm || rm.racers.Length == 0) return;
            var track = rm.track;
            float dt = Time.unscaledDeltaTime;

            if (showcase)
            {
                // Garage: circle the player's car from the right-hand side so the menu panel doesn't hide it.
                orbitAngle += dt * 18f;
                Vector3 c = showcase.position + Vector3.up * 0.6f;
                Vector3 dir = Quaternion.AngleAxis(orbitAngle, Vector3.up) * showcase.forward;
                transform.position = c + dir * 6.5f + Vector3.up * 1.4f;
                transform.rotation = Quaternion.LookRotation(c - transform.position);
                // Shift the car into the free right part of the screen.
                transform.position -= transform.right * 1.8f;
                cam.fieldOfView = 45f;
                shotTimer = 0f;
                return;
            }

            shotTimer -= dt;
            if (shotTimer <= 0f || !subject)
            {
                shotTimer = Random.Range(5f, 8f);
                subject = rm.racers[Random.Range(0, rm.racers.Length)];
                shot = (Shot)Random.Range(0, 5);
                if (shot == Shot.Trackside)
                {
                    // Stand on the sidewalk ahead of the car and watch it go by.
                    int idx = track.IndexAhead(subject.index, 45f + subject.car.ForwardSpeed * 1.5f);
                    float side = Random.value < 0.5f ? -1f : 1f;
                    fixedPos = track.Point(idx) + track.Right(idx) * side * (track.roadHalfWidth + 2.5f) + Vector3.up * Random.Range(1.6f, 4f);
                }
                orbitAngle = Random.Range(0f, 360f);
                InstantCut();
            }

            Vector3 subjPos = subject.transform.position;
            Vector3 fwd = subject.transform.forward;
            fwd.y = 0f;
            fwd.Normalize();
            float k = 1f - Mathf.Exp(-4f * dt);

            switch (shot)
            {
                case Shot.Trackside:
                    transform.position = fixedPos;
                    LookAt(subjPos + Vector3.up * 0.6f, 32f, k);
                    break;
                case Shot.Aerial:
                    transform.position = subjPos - fwd * 22f + Vector3.up * 26f;
                    LookAt(subjPos + fwd * 10f, 50f, k);
                    break;
                case Shot.FrontLow:
                    // Rigidly attached so a fast car can't catch up with the camera.
                    transform.position = subjPos + fwd * 10f + Vector3.up * 0.9f + subject.transform.right * 1.2f;
                    LookAt(subjPos + Vector3.up * 0.6f, 48f, 1f);
                    break;
                case Shot.Orbit:
                {
                    // Slow helicopter pass around the city centre.
                    orbitAngle += dt * 4f;
                    float half = CityLayout.Size * 0.5f;
                    Vector3 c = new Vector3(half, CityLayout.Height(half, half), half);
                    transform.position = c + Quaternion.Euler(0f, orbitAngle, 0f) * Vector3.forward * (half + 120f) + Vector3.up * 170f;
                    LookAt(c, 50f, 1f);
                    break;
                }
                default:
                    transform.position = subjPos - fwd * 7.5f + Vector3.up * 2.5f;
                    LookAt(subjPos + fwd * 5f + Vector3.up * 0.9f, 62f, 1f);
                    break;
            }
        }

        void InstantCut()
        {
            if (!subject) return;
            Vector3 p = subject.transform.position;
            Vector3 fwd = subject.transform.forward;
            fwd.y = 0f;
            fwd.Normalize();
            transform.position = shot switch
            {
                Shot.Aerial => p - fwd * 22f + Vector3.up * 26f,
                Shot.FrontLow => p + fwd * 10f + Vector3.up * 0.9f,
                Shot.Chase => p - fwd * 7.5f + Vector3.up * 2.5f,
                _ => transform.position,
            };
            transform.rotation = Quaternion.LookRotation((p - transform.position).sqrMagnitude > 0.01f ? p - transform.position : fwd);
        }

        void LookAt(Vector3 point, float fov, float k)
        {
            var rot = Quaternion.LookRotation(point - transform.position);
            transform.rotation = k >= 1f ? rot : Quaternion.Slerp(transform.rotation, rot, k * 3f);
            cam.fieldOfView = fov;
        }
    }
}
