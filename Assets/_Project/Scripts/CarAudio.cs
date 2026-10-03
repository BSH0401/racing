using UnityEngine;

namespace Racing
{
    // Engine loop whose pitch follows a fake 5-speed gearbox.
    [RequireComponent(typeof(CarController), typeof(AudioSource))]
    public class CarAudio : MonoBehaviour
    {
        public bool listenerCar;

        static AudioClip engineClip;
        CarController car;
        AudioSource source;

        void Start()
        {
            car = GetComponent<CarController>();
            source = GetComponent<AudioSource>();
            if (!engineClip) engineClip = SynthAudio.Engine();
            source.clip = engineClip;
            source.loop = true;
            source.playOnAwake = false;
            source.spatialBlend = listenerCar ? 0f : 1f;
            source.minDistance = 6f;
            source.maxDistance = 90f;
            source.rolloffMode = AudioRolloffMode.Linear;
            source.dopplerLevel = 0.3f;
            source.Play();
        }

        void Update()
        {
            const int gears = 5;
            float v = Mathf.Clamp01(Mathf.Abs(car.ForwardSpeed) / car.maxSpeed) * 0.999f;
            float g = Mathf.Floor(v * gears);
            float inGear = v * gears - g;
            float rpm = Mathf.Lerp(g == 0f ? 0.15f : 0.45f, 1f, inGear);
            float load = Mathf.Abs(car.Throttle);
            source.pitch = 0.65f + rpm * 1.25f + load * 0.05f;
            source.volume = (listenerCar ? 0.32f : 0.55f) * (0.55f + 0.45f * load);
        }
    }
}
