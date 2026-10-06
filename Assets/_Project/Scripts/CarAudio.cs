using UnityEngine;

namespace Racing
{
    // Car sound: two engine loops (low / high rpm) cross-faded by a fake 5-speed gearbox, a throttle
    // low-pass (muffled off-throttle), tyre squeal while sliding, wind at speed and impact thumps.
    // The player's car is 2D; other cars are quieter, positional and slightly detuned from each other.
    [RequireComponent(typeof(CarController), typeof(AudioSource))]
    public class CarAudio : MonoBehaviour
    {
        public bool listenerCar;

        const float LowFiring = 40f, HighFiring = 100f;
        const float IdleFiring = 28f, RedlineFiring = 165f;

        static AudioClip lowClip, highClip, squealClip, windClip, impactClip;

        CarController car;
        AudioSource low, high, squeal, wind, fx;
        AudioLowPassFilter engineFilter;
        float detune, rpm, shiftDip, squealVol;
        int lastGear;

        void Start()
        {
            car = GetComponent<CarController>();
            if (!lowClip)
            {
                lowClip = SynthAudio.Engine(LowFiring, 11);
                highClip = SynthAudio.Engine(HighFiring, 23);
                squealClip = SynthAudio.Noise("Squeal", SynthAudio.Biquad.Kind.BandPass, 1150f, 3.5f, 0.6f, 5);
                windClip = SynthAudio.Noise("Wind", SynthAudio.Biquad.Kind.LowPass, 420f, 0.7f, 0.3f, 9);
                impactClip = SynthAudio.Impact(3);
            }
            detune = listenerCar ? 1f : Random.Range(0.9f, 1.1f);

            // Engine loops live on a child with their own low-pass (filters affect every source on a GameObject).
            var engineGo = new GameObject("EngineAudio");
            engineGo.transform.SetParent(transform, false);
            low = Setup(engineGo.AddComponent<AudioSource>(), lowClip, true);
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
            float target = Mathf.Lerp(gear == 0 ? 0.08f : 0.42f, 1f, inGear);
            if (car.GroundedWheels == 0) target = Mathf.Max(target, load); // revving in the air
            if (gear != lastGear && gear > lastGear) shiftDip = 1f;       // brief lift on up-shift
            lastGear = gear;
            shiftDip = Mathf.MoveTowards(shiftDip, 0f, Time.deltaTime * 6f);
            rpm = Mathf.Lerp(rpm, target, 1f - Mathf.Exp(-12f * Time.deltaTime));

            float firing = Mathf.Lerp(IdleFiring, RedlineFiring, rpm) * detune;
            float blend = Mathf.InverseLerp(55f, 95f, firing);
            low.pitch = firing / LowFiring;
            high.pitch = firing / HighFiring;

            var rm = RaceManager.Instance;
            bool menu = rm && rm.State == RaceState.Menu;
            float master = listenerCar ? 0.5f : 0.38f;
            if (menu) master *= listenerCar ? 0.25f : 0.5f;
            if (rm && rm.Paused) master = 0f;
            float engine = master * (0.5f + 0.5f * load) * (1f - shiftDip * 0.45f);
            low.volume = engine * (1f - blend);
            high.volume = engine * blend * 0.9f;
            // Open throttle sounds bright, lifting off sounds muffled.
            engineFilter.cutoffFrequency = Mathf.Lerp(900f, 5500f, Mathf.Max(load, 0.15f)) * (0.8f + 0.4f * rpm);

            // Tyre squeal from body slip or handbrake, only on the ground and at speed.
            float slip = Mathf.Clamp01((Mathf.Abs(car.DriftAngle) - 7f) / 22f);
            if (car.Handbrake && speed > 6f) slip = Mathf.Max(slip, 0.6f);
            if (car.GroundedWheels < 2 || speed < 5f) slip = 0f;
            squealVol = Mathf.MoveTowards(squealVol, slip, Time.deltaTime * 4f);
            squeal.volume = squealVol * (listenerCar ? 0.16f : 0.12f) * (menu ? 0.5f : 1f) * (rm && rm.Paused ? 0f : 1f);
            squeal.pitch = 0.9f + 0.2f * Mathf.Clamp01(speed / 40f);

            if (wind)
            {
                float s = Mathf.Clamp01(speed / 60f);
                wind.volume = s * s * 0.2f * (menu || (rm && rm.Paused) ? 0f : 1f);
                wind.pitch = 0.8f + 0.4f * s;
            }
        }

        void OnCollisionEnter(Collision c)
        {
            if (!fx) return;
            float dv = c.impulse.magnitude / Mathf.Max(1f, car.Body.mass);
            if (dv < 0.8f) return;
            fx.pitch = Random.Range(0.85f, 1.1f);
            fx.PlayOneShot(impactClip, Mathf.Clamp01(dv / 8f) * (listenerCar ? 0.9f : 0.7f));
        }
    }
}
