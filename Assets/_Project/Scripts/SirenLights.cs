using UnityEngine;

namespace Racing
{
    // Red/blue flashing light bar for the chase modes, built on demand and fitted to the roof of
    // whichever car body the racer currently has.
    public class SirenLights : MonoBehaviour
    {
        public Material red, blue;

        Transform bar;
        Renderer left, right;
        Light glow;
        AudioSource wail;
        static AudioClip sirenClip;
        bool on;

        // showBar = false for bodies with their own roof lights: only the flashing glow is added.
        public void Set(bool value, bool showBar = true)
        {
            on = value;
            if (value && !bar) Build();
            if (!bar) return;
            bar.gameObject.SetActive(value);
            if (!value) { if (wail) wail.Stop(); return; }
            // The player's own siren (pursuit) stays in the background; cops are louder and positional.
            var racer = GetComponent<Racer>();
            bool mine = racer && racer.isPlayer;
            wail.volume = mine ? 0.07f : 0.3f;
            wail.time = Random.Range(0f, sirenClip.length);
            wail.Play();
            Fit();
            barVisible = showBar;
        }

        bool barVisible = true;

        void Build()
        {
            bar = new GameObject("SirenBar").transform;
            bar.SetParent(transform, false);
            left = Lamp("Red", red, -0.27f);
            right = Lamp("Blue", blue, 0.27f);
            glow = new GameObject("SirenGlow").AddComponent<Light>();
            glow.transform.SetParent(bar, false);
            glow.type = LightType.Point;
            glow.range = 16f;
            glow.intensity = 30f;
            glow.shadows = LightShadows.None;
            foreach (var t in bar.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = 2;

            if (!sirenClip) sirenClip = SynthAudio.Siren();
            wail = bar.gameObject.AddComponent<AudioSource>();
            wail.clip = sirenClip;
            wail.loop = true;
            wail.playOnAwake = false;
            wail.spatialBlend = 1f;
            wail.rolloffMode = AudioRolloffMode.Logarithmic;
            wail.minDistance = 10f;
            wail.maxDistance = 200f;
            wail.dopplerLevel = 0.6f;
        }

        Renderer Lamp(string name, Material mat, float x)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            Destroy(go.GetComponent<Collider>());
            go.transform.SetParent(bar, false);
            go.transform.localPosition = new Vector3(x, 0f, 0f);
            go.transform.localScale = new Vector3(0.5f, 0.12f, 0.22f);
            var r = go.GetComponent<Renderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return r;
        }

        // Sit on the highest point of the body, a little behind its centre.
        void Fit()
        {
            var car = GetComponent<CarController>();
            if (!car || !car.bodyVisual) return;
            var renderers = car.bodyVisual.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) return;
            Bounds b = renderers[0].bounds;
            foreach (var r in renderers) b.Encapsulate(r.bounds);
            Vector3 top = transform.InverseTransformPoint(new Vector3(b.center.x, b.max.y, b.center.z));
            bar.localPosition = new Vector3(0f, top.y + 0.04f, top.z - 0.3f);
            bar.localRotation = Quaternion.identity;
        }

        void Update()
        {
            if (!on || !bar) return;
            bool phase = Mathf.Repeat(Time.time * 2.5f, 1f) < 0.5f;
            left.enabled = barVisible && phase;
            right.enabled = barVisible && !phase;
            glow.color = phase ? new Color(1f, 0.1f, 0.1f) : new Color(0.15f, 0.3f, 1f);
            glow.transform.localPosition = new Vector3(phase ? -0.3f : 0.3f, 0.3f, 0f);
        }
    }
}
