using UnityEngine;

namespace Racing
{
    // Car sound from recorded CC0 engine loops (idle / mid / high rpm, see ThirdParty/Freesound),
    // cross-faded and pitched by a fake 5-speed gearbox; a throttle low-pass (muffled off-throttle),
    // tyre squeal while sliding, wind at speed and crash sounds. Falls back to synthesized sounds when
    // the clips are missing. The player's car is 2D; other cars are quieter, positional and detuned.
    [RequireComponent(typeof(CarController), typeof(AudioSource))]
    public class CarAudio : MonoBehaviour
    {
        public bool listenerCar;

        [Header("Recorded clips (base firing frequency in Hz of each engine loop)")]
        public AudioClip idleClip;
        public float idleFiring = 46.5f;
        public AudioClip midClip;
        public float midFiring = 67f;
        public AudioClip highClip;
        public float highFiring = 169f;
        public AudioClip squealClip, crashClip;

        // Engine firing frequency range of the fake gearbox (4-cylinder: ~1100 to ~5900 rpm).
        const float IdleFiring = 38f, RedlineFiring = 196f;

        static AudioClip synthLow, synthHigh, synthSqueal, windClip, thumpClip;

        CarController car;
        AudioSource idle, mid, high, squeal, wind, fx;
        AudioLowPassFilter engineFilter;
        float detune, rpm, shiftDip, squealVol;
        int lastGear;

        void Start()
        {
            car = GetComponent<CarController>();
            if (!windClip)
            {
                windClip = SynthAudio.Noise("Wind", SynthAudio.Biquad.Kind.LowPass, 420f, 0.7f, 0.3f, 9);
                thumpClip = SynthAudio.Impact(3);
            }
            if (!idleClip || !midClip || !highClip)
            {
                // No recordings: two synthesized loops stand in for the three recorded ones.
                if (!synthLow) { synthLow = SynthAudio.Engine(40f, 11); synthHigh = SynthAudio.Engine(100f, 23); }
                idleClip = synthLow; idleFiring = 40f;
                midClip = synthLow; midFiring = 40f;
                highClip = synthHigh; highFiring = 100f;
            }
            if (!squealClip)
            {
                if (!synthSqueal) synthSqueal = SynthAudio.Noise("Squeal", SynthAudio.Biquad.Kind.BandPass, 1150f, 3.5f, 0.6f, 5);
                squealClip = synthSqueal;
            }
            detune = listenerCar ? 1f : Random.Range(0.9f, 1.1f);

            // Engine loops live on a child with their own low-pass (filters affect every source on a GameObject).
            var engineGo = new GameObject("EngineAudio");
            engineGo.transform.SetParent(transform, false);
            idle = Setup(engineGo.AddComponent<AudioSource>(), idleClip, true);
            mid = Setup(engineGo.AddComponent<AudioSource>(), midClip, true);
            high = Setup(engineGo.AddComponent<AudioSource>(), highClip, true);
            engineFilter = engineGo.AddComponent<AudioLowPassFilter>();
            squeal = Setup(gameObject.AddComponent<AudioSource>(), squealClip, true);
            if (listenerCar) wind = Setup(gameObject.AddComponent<AudioSource>(), windClip, true);
            fx = Setup(GetComponent<AudioSource>(), null, false);
        }

        AudioSource Setup(AudioSource s, AudioClip clip, bool loop)
        {
            s.clip = clip;
            s.loop = loop;
            s.playOnAwake = false;
            s.spatialBlend = listenerCar ? 0f : 1f;
            s.rolloffMode = AudioRolloffMode.Logarithmic;
            s.minDistance = 7f;
            s.maxDistance = 120f;
            s.dopplerLevel = 0.15f;
            s.volume = 0f;
            if (loop && clip)
            {
                s.time = Random.Range(0f, clip.length * 0.9f);
                s.Play();
            }
            return s;
        }

        void Update()
        {
            const int gears = 5;
            float speed = Mathf.Abs(car.ForwardSpeed);
            float v = Mathf.Clamp01(speed / car.maxSpeed) * 0.999f;
            int gear = Mathf.FloorToInt(v * gears);
            float inGear = v * gears - gear;
            float load = Mathf.Clamp01(Mathf.Abs(car.Throttle));
            float target = Mathf.Lerp(gear == 0 ? 0.05f : 0.45f, 1f, inGear);
            if (car.GroundedWheels == 0) target = Mathf.Max(target, load); // revving in the air
            if (gear > lastGear) shiftDip = 1f;                              // brief lift on up-shift
            lastGear = gear;
            shiftDip = Mathf.MoveTowards(shiftDip, 0f, Time.deltaTime * 6f);
            rpm = Mathf.Lerp(rpm, target, 1f - Mathf.Exp(-12f * Time.deltaTime));

            // Three overlapping bands, equal-power cross-fades: idle < 60 Hz < mid < 125 Hz < high.
            float firing = Mathf.Lerp(IdleFiring, RedlineFiring, rpm) * detune;
            float toMid = Mathf.InverseLerp(48f, 70f, firing);
            float toHigh = Mathf.InverseLerp(105f, 145f, firing);
            float wIdle = Mathf.Cos(toMid * Mathf.PI * 0.5f);
            float wMid = Mathf.Sin(toMid * Mathf.PI * 0.5f) * Mathf.Cos(toHigh * Mathf.PI * 0.5f);
            float wHigh = Mathf.Sin(toHigh * Mathf.PI * 0.5f);
            idle.pitch = Mathf.Clamp(firing / idleFiring, 0.5f, 2.5f);
            mid.pitch = Mathf.Clamp(firing / midFiring, 0.5f, 2.5f);
            high.pitch = Mathf.Clamp(firing / highFiring, 0.5f, 2.5f);

            var rm = RaceManager.Instance;
            bool menu = rm && rm.State == RaceState.Menu;
            bool paused = rm && rm.Paused;
            float master = listenerCar ? 0.7f : 0.5f;
            if (menu) master *= listenerCar ? 0.25f : 0.5f;
            if (paused) master = 0f;
            float engine = master * (0.45f + 0.55f * load) * (1f - shiftDip * 0.4f);
            idle.volume = engine * wIdle;
            mid.volume = engine * wMid;
            high.volume = engine * wHigh;
            // Open throttle sounds bright, lifting off sounds muffled.
            engineFilter.cutoffFrequency = Mathf.Lerp(1200f, 9000f, Mathf.Max(load, 0.12f)) * (0.8f + 0.4f * rpm);

            // Tyre squeal from body slip or handbrake, only on the ground and at speed.
            float slip = Mathf.Clamp01((Mathf.Abs(car.DriftAngle) - 7f) / 22f);
            if (car.Handbrake && speed > 6f) slip = Mathf.Max(slip, 0.6f);
            if (car.GroundedWheels < 2 || speed < 5f) slip = 0f;
            squealVol = Mathf.MoveTowards(squealVol, slip, Time.deltaTime * 4f);
            squeal.volume = squealVol * (listenerCar ? 0.35f : 0.28f) * (menu ? 0.5f : 1f) * (paused ? 0f : 1f);
            squeal.pitch = 0.92f + 0.16f * Mathf.Clamp01(speed / 40f);

            if (wind)
            {
                float s = Mathf.Clamp01(speed / 60f);
                wind.volume = s * s * 0.18f * (menu || paused ? 0f : 1f);
                wind.pitch = 0.8f + 0.4f * s;
            }
        }

        void OnCollisionEnter(Collision c)
        {
            if (!fx) return;
            float dv = c.impulse.magnitude / Mathf.Max(1f, car.Body.mass);
            if (dv < 0.8f) return;
            float vol = Mathf.Clamp01(dv / 8f) * (listenerCar ? 0.9f : 0.7f);
            fx.pitch = Random.Range(0.85f, 1.1f);
            fx.PlayOneShot(thumpClip, vol * 0.7f);
            // Real crash recording for the harder hits.
            if (crashClip && dv > 2.5f) fx.PlayOneShot(crashClip, vol);
        }
    }
}
