using UnityEngine;

namespace Fragile.Breakables
{
    /// <summary>Son de céramique qui éclate, généré par le code (aucun fichier audio) : craquement + tintements.</summary>
    static class SmashSynth
    {
        public static AudioClip Create()
        {
            const int rate = 44100;
            float length = 1.1f;
            int n = (int)(rate * length);
            var data = new float[n];
            var rng = new System.Random(7);
            float Rand() => (float)rng.NextDouble() * 2f - 1f;

            // 1) Impact : bruit large bande très court + corps grave.
            float lp = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)rate;
                float crack = Rand() * Mathf.Exp(-t * 38f);
                lp += (Rand() - lp) * 0.08f;                     // bruit filtré (grave)
                float thud = lp * 2.2f * Mathf.Exp(-t * 22f);
                data[i] = crack * 0.9f + thud * 0.6f;
            }
            // 2) Craquements secondaires (fragments qui se séparent).
            for (int k = 0; k < 9; k++)
            {
                int start = (int)(rate * (0.01f + (float)rng.NextDouble() * 0.12f));
                float amp = 0.25f + (float)rng.NextDouble() * 0.35f;
                for (int i = 0; i < rate * 0.03f && start + i < n; i++)
                    data[start + i] += Rand() * amp * Mathf.Exp(-i / (rate * 0.006f));
            }
            // 3) Tintements de céramique : sinus aigus amortis, décalés dans le temps.
            for (int k = 0; k < 16; k++)
            {
                float f = 2200f + (float)rng.NextDouble() * 5200f;
                int start = (int)(rate * (0.02f + (float)rng.NextDouble() * 0.6f));
                float amp = 0.05f + (float)rng.NextDouble() * 0.12f;
                float decay = 18f + (float)rng.NextDouble() * 30f;
                for (int i = start; i < n; i++)
                {
                    float t = (i - start) / (float)rate;
                    float e = Mathf.Exp(-t * decay);
                    if (e < 0.001f) break;
                    data[i] += Mathf.Sin(2f * Mathf.PI * f * t) * amp * e
                             + Mathf.Sin(2f * Mathf.PI * f * 1.51f * t) * amp * 0.4f * e;
                }
            }
            // Normalisation douce.
            float peak = 0f; foreach (var s in data) peak = Mathf.Max(peak, Mathf.Abs(s));
            float g = peak > 0f ? 0.95f / peak : 1f;
            for (int i = 0; i < n; i++) data[i] = (float)System.Math.Tanh(data[i] * g * 1.2f);

            var clip = AudioClip.Create("Casse céramique", n, 1, rate, false);
            clip.SetData(data, 0);
            return clip;
        }
    }
}
