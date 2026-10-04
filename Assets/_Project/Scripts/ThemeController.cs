using System.Collections.Generic;
using UnityEngine;

namespace Racing
{
    public enum RaceTheme { Day, Night }

    [System.Serializable]
    public class ThemeSettings
    {
        public Material skybox;
        [Tooltip("HDRI cubemap used for reflections (and, with skybox ambient, for lighting).")]
        public Cubemap reflection;
        public float ambientIntensity = 1f;
        public float reflectionIntensity = 1f;
        public Color sunColor = Color.white;
        public float sunIntensity = 1f;
        public Vector3 sunEuler = new Vector3(45f, -40f, 0f);
        public Color ambientSky, ambientEquator, ambientGround;
        public Color fogColor;
        public float fogStart = 200f, fogEnd = 1100f;
        public Color windowGlow = Color.black;
        public Color lampGlow = Color.black;
        public bool lightsOn;
    }

    // Switches lighting, sky, fog, emissive windows/lamps and real street/head lights between day and night.
    public class ThemeController : MonoBehaviour
    {
        const string PrefKey = "theme";

        public Light sun;
        public TrackBuilder track;
        public Racer[] cars;
        public Material[] windowMaterials = new Material[0];
        public Material lampHeadMaterial;
        public ThemeSettings day = new ThemeSettings();
        public ThemeSettings night = new ThemeSettings();

        public RaceTheme Current { get; private set; }

        readonly List<Light> streetLights = new List<Light>();
        readonly List<Light> headLights = new List<Light>();

        void Start()
        {
            var lightRoot = new GameObject("StreetLights").transform;
            lightRoot.SetParent(transform, false);
            foreach (var p in track.LampLightPositions)
            {
                var l = new GameObject("StreetLight").AddComponent<Light>();
                l.transform.SetParent(lightRoot, false);
                l.transform.SetPositionAndRotation(p, Quaternion.Euler(90f, 0f, 0f));
                l.type = LightType.Spot;
                l.spotAngle = 120f;
                l.innerSpotAngle = 60f;
                l.range = 34f;
                l.intensity = 140f;
                l.color = new Color(1f, 0.82f, 0.55f);
                l.shadows = LightShadows.None;
                streetLights.Add(l);
            }

            foreach (var car in cars)
            {
                var l = new GameObject("Headlights").AddComponent<Light>();
                l.transform.SetParent(car.transform, false);
                l.transform.localPosition = new Vector3(0f, 0.3f, 2.2f);
                l.transform.localRotation = Quaternion.Euler(7f, 0f, 0f);
                l.type = LightType.Spot;
                l.spotAngle = 70f;
                l.innerSpotAngle = 35f;
                l.range = 55f;
                l.intensity = car.isPlayer ? 120f : 70f;
                l.color = new Color(1f, 0.96f, 0.85f);
                l.shadows = LightShadows.None;
                headLights.Add(l);
            }

            RaceTheme start = (RaceTheme)PlayerPrefs.GetInt(PrefKey, (int)RaceTheme.Day);
            if (DevFlags.Has("-night")) start = RaceTheme.Night;
            if (DevFlags.Has("-day")) start = RaceTheme.Day;
            Apply(start);
        }

        public void Toggle() => Apply(Current == RaceTheme.Day ? RaceTheme.Night : RaceTheme.Day);

        public void Apply(RaceTheme theme)
        {
            Current = theme;
            PlayerPrefs.SetInt(PrefKey, (int)theme);
            var s = theme == RaceTheme.Night ? night : day;

            if (s.skybox) RenderSettings.skybox = s.skybox;
            if (s.reflection)
            {
                RenderSettings.defaultReflectionMode = UnityEngine.Rendering.DefaultReflectionMode.Custom;
                RenderSettings.customReflectionTexture = s.reflection;
            }
            sun.color = s.sunColor;
            sun.intensity = s.sunIntensity;
            sun.transform.rotation = Quaternion.Euler(s.sunEuler);
            RenderSettings.ambientIntensity = s.ambientIntensity;
            RenderSettings.reflectionIntensity = s.reflectionIntensity;
            RenderSettings.ambientSkyColor = s.ambientSky;
            RenderSettings.ambientEquatorColor = s.ambientEquator;
            RenderSettings.ambientGroundColor = s.ambientGround;
            RenderSettings.fogColor = s.fogColor;
            RenderSettings.fogStartDistance = s.fogStart;
            RenderSettings.fogEndDistance = s.fogEnd;

            SetMaterialGlow(s);
            foreach (var l in streetLights) l.enabled = s.lightsOn;
            foreach (var l in headLights) l.enabled = s.lightsOn;
            DynamicGI.UpdateEnvironment();
        }

        void SetMaterialGlow(ThemeSettings s)
        {
            foreach (var m in windowMaterials) m.SetColor("_EmissionColor", s.windowGlow);
            if (lampHeadMaterial) lampHeadMaterial.SetColor("_EmissionColor", s.lampGlow);
        }

#if UNITY_EDITOR
        // Materials are shared assets: put them back to the day look when play mode ends.
        void OnDestroy() => SetMaterialGlow(day);
#endif
    }
}
