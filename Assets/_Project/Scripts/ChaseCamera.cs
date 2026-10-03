using UnityEngine;
using UnityEngine.InputSystem;

namespace Racing
{
    // Smoothed third-person chase camera; C / gamepad right shoulder toggles near/far.
    [RequireComponent(typeof(Camera))]
    public class ChaseCamera : MonoBehaviour
    {
        public Rigidbody target;
        public float distance = 7f;
        public float height = 2.6f;
        public float lookAhead = 5f;
        public float follow = 7f;

        Camera cam;
        bool near;
        float yaw;

        void Awake() => cam = GetComponent<Camera>();

        void Update()
        {
            var kb = Keyboard.current;
            var gp = Gamepad.current;
            if ((kb != null && kb.cKey.wasPressedThisFrame) || (gp != null && gp.rightShoulder.wasPressedThisFrame)) near = !near;
        }

        void LateUpdate()
        {
            if (!target) return;
            Place(1f - Mathf.Exp(-follow * Time.deltaTime));
        }

        public void Snap() => Place(1f, true);

        void Place(float t, bool snap = false)
        {
            Transform tr = target.transform;
            Vector3 fwd = tr.forward;
            fwd.y = 0f;
            if (fwd.sqrMagnitude < 0.001f) fwd = Vector3.forward;
            float targetYaw = Mathf.Atan2(fwd.x, fwd.z) * Mathf.Rad2Deg;
            yaw = snap ? targetYaw : Mathf.LerpAngle(yaw, targetYaw, t);
            Vector3 dir = Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;

            float speed = target.linearVelocity.magnitude;
            float dist = (near ? distance * 0.65f : distance) + speed * 0.03f;
            float h = near ? height * 0.7f : height;
            Vector3 desired = tr.position - dir * dist + Vector3.up * h;
            transform.position = snap ? desired : Vector3.Lerp(transform.position, desired, Mathf.Clamp01(t * 2f));
            Vector3 look = tr.position + dir * lookAhead + Vector3.up * 0.9f;
            transform.rotation = Quaternion.LookRotation(look - transform.position);

            cam.fieldOfView = Mathf.Lerp(cam.fieldOfView, 60f + Mathf.Clamp01(speed / 55f) * 14f, snap ? 1f : t);
        }
    }
}
