using UnityEngine;

namespace Racing
{
    // Procedurally generated placeholder sounds.
    public static class SynthAudio
    {
        const int Rate = 44100;

        public static AudioClip Tone(float freq, float seconds)
        {
            int n = Mathf.RoundToInt(Rate * seconds);
            var data = new float[n];
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate;
                float env = Mathf.Clamp01(t / 0.01f) * Mathf.Clamp01((seconds - t) / 0.08f);
                data[i] = Mathf.Sin(2f * Mathf.PI * freq * t) * 0.5f * env;
            }
            var clip = AudioClip.Create("Tone" + freq, n, 1, Rate, false);
            clip.SetData(data, 0);
            return clip;
        }

        // One-second seamless engine loop: integer-Hz harmonics so the loop point has no click.
        public static AudioClip Engine()
        {
            int n = Rate;
            var data = new float[n];
            const float f = 56f;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate;
                float saw = 2f * (f * t - Mathf.Floor(f * t + 0.5f));
                float body = Mathf.Sin(2f * Mathf.PI * f * t) * 0.6f + Mathf.Sin(2f * Mathf.PI * f * 2f * t) * 0.3f + saw * 0.25f;
                float pulse = 0.65f + 0.35f * Mathf.Sin(2f * Mathf.PI * (f * 0.5f) * t);
                data[i] = body * pulse * 0.35f;
            }
            var clip = AudioClip.Create("Engine", n, 1, Rate, false);
            clip.SetData(data, 0);
            return clip;
        }
    }
}
