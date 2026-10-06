using UnityEngine;

namespace Racing
{
    // Procedurally generated sounds (no audio assets). Engines are modelled as cylinder firing pulses
    // through exhaust resonances rather than raw oscillators, which keeps them warm instead of buzzy.
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
                data[i] = (Mathf.Sin(2f * Mathf.PI * freq * t) + 0.15f * Mathf.Sin(4f * Mathf.PI * freq * t)) * 0.4f * env;
            }
            return Make("Tone" + freq, data);
        }

        // Seamless 2 s engine loop at the given firing frequency (Hz). Each firing is a short noisy
        // pulse with slight timing/strength variation (a 4-cylinder pattern), shaped by two exhaust
        // resonances and a low-pass. Played back with AudioSource.pitch = firing / baseFiring.
        public static AudioClip Engine(float firing, int seed)
        {
            const float seconds = 2f;
            int n = Mathf.RoundToInt(Rate * seconds);
            var rnd = new System.Random(seed);
            float R() => (float)rnd.NextDouble() * 2f - 1f;
            int events = Mathf.RoundToInt(firing * seconds);
            float period = n / (float)events;
            float[] cylinder = { 1f, 0.82f, 0.94f, 0.76f };
            var excite = new float[n];
            for (int k = 0; k < events; k++)
            {
                int start = Mathf.RoundToInt(k * period + R() * period * 0.06f);
                float amp = cylinder[k % 4] * (1f + R() * 0.08f);
                int len = Mathf.RoundToInt(period * 0.9f);
                for (int j = 0; j < len; j++)
                {
                    float env = Mathf.Exp(-j / (period * 0.18f));
                    excite[((start + j) % n + n) % n] += amp * env * (0.55f + 0.45f * R());
                }
            }

            // Run the filters over the loop twice so the output wraps without a click.
            var a = new Biquad(Biquad.Kind.BandPass, firing * 2f, 1.2f);
            var b = new Biquad(Biquad.Kind.BandPass, 420f, 0.9f);
            var lp = new Biquad(Biquad.Kind.LowPass, 2200f, 0.7f);
            var data = new float[n];
            for (int pass = 0; pass < 2; pass++)
                for (int i = 0; i < n; i++)
                {
                    float x = excite[i];
                    float y = lp.Process(a.Process(x) * 1.4f + b.Process(x) * 0.5f + x * 0.08f);
                    if (pass == 1) data[i] = y;
                }
            Normalize(data, 0.55f);
            return Make("Engine" + firing, data);
        }

        // Filtered noise loop (tyre squeal, wind).
        public static AudioClip Noise(string name, Biquad.Kind kind, float freq, float q, float wobble, int seed)
        {
            int n = Rate * 2;
            var rnd = new System.Random(seed);
            var f1 = new Biquad(kind, freq, q);
            var f2 = new Biquad(kind, freq, q);
            var data = new float[n];
            for (int pass = 0; pass < 2; pass++)
                for (int i = 0; i < n; i++)
                {
                    float x = (float)rnd.NextDouble() * 2f - 1f;
                    float y = f2.Process(f1.Process(x));
                    // Slow amplitude wobble at an integer rate so the loop stays seamless.
                    float t = i / (float)Rate;
                    if (pass == 1) data[i] = y * (1f - wobble * 0.5f + wobble * 0.5f * Mathf.Sin(2f * Mathf.PI * 3f * t));
                }
            // Fade the seam a little: noise doesn't repeat, so cross-fade the last 20 ms into the start.
            int fade = Rate / 50;
            for (int i = 0; i < fade; i++)
            {
                float w = i / (float)fade;
                data[i] = data[i] * w + data[n - fade + i] * (1f - w);
            }
            System.Array.Resize(ref data, n - fade);
            Normalize(data, 0.5f);
            return Make(name, data);
        }

        // Body impact: a low thump plus a short crunch.
        public static AudioClip Impact(int seed)
        {
            int n = Mathf.RoundToInt(Rate * 0.45f);
            var rnd = new System.Random(seed);
            var lp = new Biquad(Biquad.Kind.LowPass, 260f, 0.8f);
            var bp = new Biquad(Biquad.Kind.BandPass, 1800f, 1.1f);
            var data = new float[n];
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate;
                float x = (float)rnd.NextDouble() * 2f - 1f;
                float thump = lp.Process(x) * 3f * Mathf.Exp(-t / 0.09f) + Mathf.Sin(2f * Mathf.PI * 62f * t) * Mathf.Exp(-t / 0.07f) * 0.7f;
                float crunch = bp.Process(x) * Mathf.Exp(-t / 0.035f) * 0.8f;
                data[i] = (thump + crunch) * Mathf.Clamp01(t / 0.002f);
            }
            Normalize(data, 0.8f);
            return Make("Impact", data);
        }

        // Police siren: smooth up/down wail over 4 s.
        public static AudioClip Siren()
        {
            int n = Rate * 4;
            var data = new float[n];
            double phase = 0;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate;
                float sweep = 0.5f - 0.5f * Mathf.Cos(2f * Mathf.PI * t / 4f);
                float f = Mathf.Lerp(640f, 1250f, sweep);
                // Mean frequency 945 Hz over 4 s = a whole number of cycles, so the loop joins cleanly.
                float p = (float)(phase % (2.0 * System.Math.PI));
                data[i] = Mathf.Sin(p) * 0.8f + Mathf.Sin(2f * p) * 0.12f + Mathf.Sin(3f * p) * 0.06f;
                phase += 2.0 * System.Math.PI * f / Rate;
            }
            Normalize(data, 0.45f);
            return Make("Siren", data);
        }

        // Dev (-dumpaudio dir): write the generated clips as 16-bit WAV files for inspection.
        public static void Dump(string dir)
        {
            System.IO.Directory.CreateDirectory(dir);
            var clips = new[]
            {
                Engine(40f, 11), Engine(100f, 23), Noise("Squeal", Biquad.Kind.BandPass, 1150f, 3.5f, 0.6f, 5),
                Noise("Wind", Biquad.Kind.LowPass, 420f, 0.7f, 0.3f, 9), Impact(3), Siren(), Tone(660f, 0.18f),
            };
            foreach (var c in clips)
            {
                var d = new float[c.samples];
                c.GetData(d, 0);
                using var w = new System.IO.BinaryWriter(System.IO.File.Create(System.IO.Path.Combine(dir, c.name + ".wav")));
                w.Write(System.Text.Encoding.ASCII.GetBytes("RIFF")); w.Write(36 + d.Length * 2);
                w.Write(System.Text.Encoding.ASCII.GetBytes("WAVEfmt ")); w.Write(16); w.Write((short)1); w.Write((short)1);
                w.Write(Rate); w.Write(Rate * 2); w.Write((short)2); w.Write((short)16);
                w.Write(System.Text.Encoding.ASCII.GetBytes("data")); w.Write(d.Length * 2);
                foreach (var v in d) w.Write((short)Mathf.Clamp(v * 32767f, -32768f, 32767f));
            }
        }

        static void Normalize(float[] data, float peak)
        {
            float max = 1e-6f;
            foreach (var v in data) max = Mathf.Max(max, Mathf.Abs(v));
            float k = peak / max;
            for (int i = 0; i < data.Length; i++) data[i] *= k;
        }

        static AudioClip Make(string name, float[] data)
        {
            var clip = AudioClip.Create(name, data.Length, 1, Rate, false);
            clip.SetData(data, 0);
            return clip;
        }

        // RBJ cookbook biquad.
        public class Biquad
        {
            public enum Kind { LowPass, BandPass, HighPass }
            readonly float b0, b1, b2, a1, a2;
            float x1, x2, y1, y2;

            public Biquad(Kind kind, float freq, float q)
            {
                float w = 2f * Mathf.PI * freq / Rate;
                float cos = Mathf.Cos(w), alpha = Mathf.Sin(w) / (2f * q);
                float a0;
                switch (kind)
                {
                    case Kind.LowPass:
                        b0 = (1f - cos) / 2f; b1 = 1f - cos; b2 = b0; break;
                    case Kind.HighPass:
                        b0 = (1f + cos) / 2f; b1 = -(1f + cos); b2 = b0; break;
                    default:
                        b0 = alpha; b1 = 0f; b2 = -alpha; break;
                }
                a0 = 1f + alpha;
                a1 = -2f * cos;
                a2 = 1f - alpha;
                b0 /= a0; b1 /= a0; b2 /= a0; a1 /= a0; a2 /= a0;
            }

            public float Process(float x)
            {
                float y = b0 * x + b1 * x1 + b2 * x2 - a1 * y1 - a2 * y2;
                x2 = x1; x1 = x; y2 = y1; y1 = y;
                return y;
            }
        }
    }
}
