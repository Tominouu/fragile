using UnityEngine;

namespace Fragile.Trailer
{
    /// <summary>
    /// Sons du Trailer générés par le code (aucun fichier, aucun droit à vérifier) :
    /// foule du stade (boucle), coup de sifflet, battement de cœur, respiration.
    /// Chaque son peut être remplacé par un vrai fichier dans les champs de TrailerDirector.
    /// </summary>
    public static class TrailerSynth
    {
        const int Rate = 44100;

        static float Rand(System.Random r) => (float)r.NextDouble() * 2f - 1f;

        /// <summary>Rumeur de foule qui boucle sans raccord : bruit filtré, houle lente, quelques cris.</summary>
        public static AudioClip Crowd(float seconds = 6f)
        {
            int n = (int)(Rate * seconds);
            var d = new float[n];
            var r = new System.Random(11);
            float lp1 = 0f, lp2 = 0f, hp = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate;
                float x = Rand(r);
                lp1 += (x - lp1) * 0.12f;
                lp2 += (lp1 - lp2) * 0.12f;
                hp += (lp2 - hp) * 0.004f;
                float band = lp2 - hp;                                   // bande « voix » (~200-1500 Hz)
                // Houle : périodes entières sur la durée -> la boucle se raccorde.
                float swell = 0.75f + 0.15f * Mathf.Sin(2f * Mathf.PI * t * 2f / seconds) + 0.1f * Mathf.Sin(2f * Mathf.PI * t * 5f / seconds + 1f);
                d[i] = band * 3.2f * swell;
            }
            // Cris épars (voyelles grossières : deux formants).
            for (int k = 0; k < 14; k++)
            {
                int start = (int)(r.NextDouble() * (n - Rate));
                float f0 = 160f + (float)r.NextDouble() * 180f, len = 0.25f + (float)r.NextDouble() * 0.5f;
                float amp = 0.03f + (float)r.NextDouble() * 0.05f;
                for (int i = 0; i < Rate * len; i++)
                {
                    float t = i / (float)Rate, env = Mathf.Sin(Mathf.PI * t / len);
                    float s = Mathf.Sin(2f * Mathf.PI * f0 * t) * 0.6f + Mathf.Sin(2f * Mathf.PI * f0 * 3.1f * t) * 0.3f;
                    d[(start + i) % n] += s * env * amp;
                }
            }
            Normalize(d, 0.5f);
            return Make("Foule", d);
        }

        /// <summary>Sifflet d'arbitre : trille aiguë (bille du sifflet). Trois coups = fin du match.</summary>
        public static AudioClip Whistle(int blasts = 3)
        {
            float blast = 0.45f, gap = 0.18f, last = 1.1f;
            float total = (blasts - 1) * (blast + gap) + last;
            int n = (int)(Rate * total);
            var d = new float[n];
            var r = new System.Random(3);
            float phase = 0f;
            for (int b = 0; b < blasts; b++)
            {
                float len = b == blasts - 1 ? last : blast;
                int start = (int)(Rate * b * (blast + gap));
                for (int i = 0; i < Rate * len && start + i < n; i++)
                {
                    float t = i / (float)Rate;
                    float env = Mathf.Clamp01(t / 0.02f) * Mathf.Clamp01((len - t) / 0.06f);
                    float trill = 0.65f + 0.35f * Mathf.Sin(2f * Mathf.PI * 38f * t);
                    float f = 2950f + 90f * Mathf.Sin(2f * Mathf.PI * 38f * t);
                    phase += 2f * Mathf.PI * f / Rate;
                    d[start + i] += (Mathf.Sin(phase) * 0.8f + Rand(r) * 0.08f) * env * trill;
                }
            }
            Normalize(d, 0.6f);
            return Make("Sifflet", d);
        }

        /// <summary>Un battement « poum-poum » sourd (deux chocs graves).</summary>
        public static AudioClip Heartbeat()
        {
            int n = (int)(Rate * 0.7f);
            var d = new float[n];
            void Thump(float at, float freq, float amp)
            {
                int s = (int)(Rate * at);
                for (int i = 0; s + i < n; i++)
                {
                    float t = i / (float)Rate;
                    float env = (1f - Mathf.Exp(-t * 300f)) * Mathf.Exp(-t * 18f);
                    float f = freq * (1f + 0.6f * Mathf.Exp(-t * 40f));   // petite chute de hauteur : effet « coup »
                    d[s + i] += Mathf.Sin(2f * Mathf.PI * f * t) * env * amp;
                }
            }
            Thump(0f, 52f, 1f);
            Thump(0.23f, 44f, 0.75f);
            Normalize(d, 0.95f);
            return Make("Coeur", d);
        }

        /// <summary>Respiration forte, inspiration + expiration (souffle filtré).</summary>
        public static AudioClip Breath()
        {
            float len = 1.6f;
            int n = (int)(Rate * len);
            var d = new float[n];
            var r = new System.Random(5);
            float lp = 0f, hp = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate;
                float x = Rand(r);
                lp += (x - lp) * 0.25f;
                hp += (lp - hp) * 0.02f;
                float inhale = Env(t, 0.05f, 0.55f), exhale = Env(t, 0.75f, 1.5f) * 1.3f;
                d[i] = (lp - hp) * (inhale + exhale);
            }
            Normalize(d, 0.5f);
            return Make("Souffle", d);
        }

        static float Env(float t, float a, float b) => t < a || t > b ? 0f : Mathf.Sin(Mathf.PI * (t - a) / (b - a));

        static void Normalize(float[] d, float peak)
        {
            float m = 1e-5f;
            foreach (var v in d) m = Mathf.Max(m, Mathf.Abs(v));
            for (int i = 0; i < d.Length; i++) d[i] *= peak / m;
        }

        static AudioClip Make(string name, float[] d)
        {
            var c = AudioClip.Create("[Trailer] " + name, d.Length, 1, Rate, false);
            c.SetData(d, 0);
            return c;
        }
    }
}
