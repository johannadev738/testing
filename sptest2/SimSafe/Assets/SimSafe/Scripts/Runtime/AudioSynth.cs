using UnityEngine;

namespace SimSafe
{
    /// <summary>
    /// Procedurally generated placeholder audio (no audio assets are bundled).
    /// These are simple synthesised loops meant to be replaced with licensed recordings.
    /// </summary>
    public static class AudioSynth
    {
        const int Rate = 22050;

        delegate float Gen(float t);

        static AudioClip Make(string name, float seconds, Gen gen)
        {
            int n = Mathf.RoundToInt(seconds * Rate);
            var data = new float[n];
            for (int i = 0; i < n; i++) data[i] = Mathf.Clamp(gen(i / (float)Rate), -1f, 1f);
            // Short fade at both ends to avoid clicks at the loop point.
            int fade = Rate / 50;
            for (int i = 0; i < fade && i < n; i++) { float k = i / (float)fade; data[i] *= k; data[n - 1 - i] *= k; }
            var clip = AudioClip.Create(name, n, 1, Rate, false);
            clip.SetData(data, 0);
            return clip;
        }

        public static AudioClip Siren()
        {
            float phase = 0f;
            return Make("siren", 4f, t =>
            {
                float f = 650f + 500f * (0.5f - 0.5f * Mathf.Cos(2f * Mathf.PI * t / 4f));
                phase += 2f * Mathf.PI * f / Rate;
                return 0.45f * Mathf.Sin(phase) + 0.15f * Mathf.Sin(2f * phase);
            });
        }

        public static AudioClip Mower()
        {
            var rng = new System.Random(11);
            float lp = 0f;
            return Make("mower", 3f, t =>
            {
                float engine = (Mathf.Repeat(t * 85f, 1f) * 2f - 1f) * 0.18f;                 // 85 Hz saw (whole cycles per 3 s)
                lp += (((float)rng.NextDouble() * 2f - 1f) - lp) * 0.25f;                        // low-passed noise
                float wobble = 0.85f + 0.15f * Mathf.Sin(2f * Mathf.PI * 7f * t);               // 7 Hz (whole cycles per 3 s)
                return (engine + lp * 0.25f) * wobble;
            });
        }

        public static AudioClip Radio()
        {
            var rng = new System.Random(5);
            float lp = 0f;
            return Make("radio", 6f, t =>
            {
                float gate = Mathf.Sin(t * 3.1f) > 0.2f || Mathf.Sin(t * 1.3f + 1f) > 0.7f ? 1f : 0.05f;
                lp += (((float)rng.NextDouble() * 2f - 1f) - lp) * 0.5f;
                float tone = Mathf.Sin(2f * Mathf.PI * 880f * t) * (Mathf.Sin(t * 5f) > 0.97f ? 0.4f : 0f);
                return (lp * 0.3f + tone) * gate;
            });
        }

        public static AudioClip Ambient()
        {
            var rng = new System.Random(3);
            float lp = 0f;
            // Chirps: start time, base frequency, duration.
            float[,] chirps = { { 1.2f, 3200f, 0.12f }, { 1.45f, 3600f, 0.10f }, { 4.8f, 2800f, 0.16f }, { 7.3f, 3900f, 0.09f }, { 7.5f, 3500f, 0.11f }, { 10.4f, 3000f, 0.14f } };
            return Make("ambient", 12f, t =>
            {
                lp += (((float)rng.NextDouble() * 2f - 1f) - lp) * 0.015f;
                float wind = lp * (0.55f + 0.25f * Mathf.Sin(t * 0.52f));
                float birds = 0f;
                for (int i = 0; i < chirps.GetLength(0); i++)
                {
                    float dt = t - chirps[i, 0];
                    if (dt < 0f || dt > chirps[i, 2]) continue;
                    float env = Mathf.Sin(Mathf.PI * dt / chirps[i, 2]);
                    float f = chirps[i, 1] + 900f * dt / chirps[i, 2];
                    birds += 0.10f * env * Mathf.Sin(2f * Mathf.PI * f * dt);
                }
                return wind + birds;
            });
        }
    }
}
