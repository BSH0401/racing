using UnityEngine;

namespace Racing
{
    // GTA-style radar: an orthographic camera above the player that turns with the car,
    // drawn into a square in the top-right corner.
    [RequireComponent(typeof(Camera))]
    public class MinimapCamera : MonoBehaviour
    {
        public Transform follow;
        public float range = 140f;
        public float sizeFraction = 0.3f;
        public float margin = 16f;

        Camera cam;
        float yaw;

        void Start()
        {
            cam = GetComponent<Camera>();
            cam.orthographic = true;
            cam.orthographicSize = range;
            cam.nearClipPlane = 1f;
            cam.farClipPlane = 600f;
        }

        void LateUpdate()
        {
            float px = Screen.height * sizeFraction;
            cam.rect = new Rect(1f - (px + margin) / Screen.width, 1f - (px + margin) / Screen.height, px / Screen.width, px / Screen.height);
            if (!follow) return;

            Vector3 f = follow.forward;
            f.y = 0f;
            if (f.sqrMagnitude > 0.001f) yaw = Mathf.LerpAngle(yaw, Mathf.Atan2(f.x, f.z) * Mathf.Rad2Deg, 1f - Mathf.Exp(-6f * Time.unscaledDeltaTime));
            transform.SetPositionAndRotation(follow.position + Vector3.up * 250f, Quaternion.Euler(90f, yaw, 0f));
            // Zoom out at speed (highway) so the road ahead stays on the radar.
            var body = follow.GetComponent<Rigidbody>();
            float speed = body ? body.linearVelocity.magnitude : 0f;
            cam.orthographicSize = Mathf.Lerp(cam.orthographicSize, range * Mathf.Lerp(1f, 2f, Mathf.InverseLerp(20f, 55f, speed)), 1f - Mathf.Exp(-2f * Time.unscaledDeltaTime));
        }
    }
}
