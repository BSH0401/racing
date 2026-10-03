using UnityEngine;

namespace Racing
{
    // Top-down orthographic camera drawn into a square in the top-right corner.
    [RequireComponent(typeof(Camera))]
    public class MinimapCamera : MonoBehaviour
    {
        public TrackPath track;
        public float sizeFraction = 0.3f;
        public float margin = 16f;

        Camera cam;

        void Start()
        {
            cam = GetComponent<Camera>();
            var b = track.GetBounds();
            transform.position = new Vector3(b.center.x, b.max.y + 300f, b.center.z);
            transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            cam.orthographic = true;
            cam.orthographicSize = Mathf.Max(b.extents.x, b.extents.z) + 30f;
            cam.nearClipPlane = 1f;
            cam.farClipPlane = 600f;
        }

        void LateUpdate()
        {
            float px = Screen.height * sizeFraction;
            cam.rect = new Rect(1f - (px + margin) / Screen.width, 1f - (px + margin) / Screen.height, px / Screen.width, px / Screen.height);
        }
    }
}
