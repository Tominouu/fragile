using UnityEngine;

namespace Fragile.Audio
{
    /// <summary>
    /// Petits bruitages générés par le code (rien à importer, pas de droits) : touches, caisse, tiroir, liquides,
    /// pompe, moteur, chute de canette, glaçons, vapeur. Chaque son est fabriqué une fois puis réutilisé.
    /// </summary>
    public static class Sfx
    {
        const int Rate = 44100;
        static AudioClip s_Click, s_Beep, s_Bell, s_Drawer, s_Pour, s_Pump, s_Motor, s_Clunk, s_Clink, s_Steam;

        public static AudioClip Click => s_Click != null ? s_Click : s_Click = Make("Touche", 0.05f, ClickAt);
        public static AudioClip Beep => s_Beep != null ? s_Beep : s_Beep = Make("Bip", 0.09f, BeepAt);
        public static AudioClip Bell => s_Bell != null ? s_Bell : s_Bell = Make("Caisse", 1.3f, BellAt);
        public static AudioClip Drawer => s_Drawer != null ? s_Drawer : s_Drawer = Make("Tiroir", 0.4f, DrawerAt);
        public static AudioClip Pour => s_Pour != null ? s_Pour : s_Pour = Make("Écoulement", 2f, PourAt);
        public static AudioClip Pump => s_Pump != null ? s_Pump : s_Pump = Make("Pompe", 2f, PumpAt);
        public static AudioClip Motor => s_Motor != null ? s_Motor : s_Motor = Make("Moteur", 0.9f, MotorAt);
        public static AudioClip Clunk => s_Clunk != null ? s_Clunk : s_Clunk = Make("Chute canette", 0.45f, ClunkAt);
        public static AudioClip Clink => s_Clink != null ? s_Clink : s_Clink = Make("Glaçon", 0.18f, ClinkAt);
        public static AudioClip Steam => s_Steam != null ? s_Steam : s_Steam = Make("Vapeur", 1.2f, SteamAt);

        /// <summary>Joue un son ponctuel à un endroit (objet temporaire, détruit à la fin).</summary>
        public static void PlayAt(AudioClip clip, Vector3 position, float volume = 1f, float pitch = 1f)
        {
            if (clip == null) return;
            var go = new GameObject("Son " + clip.name);
            go.transform.position = position;
            var src = go.AddComponent<AudioSource>();
            Spatialize(src);
            src.clip = clip; src.volume = volume; src.pitch = pitch;
            src.Play();
            Object.Destroy(go, clip.length / Mathf.Max(0.1f, pitch) + 0.2f);
        }

        /// <summary>Source en boucle attachée à un objet (écoulement, pompe) : on règle son volume, elle joue en continu.</summary>
        public static AudioSource Loop(Transform parent, AudioClip clip)
        {
            var go = new GameObject("Son " + clip.name);
            go.transform.SetParent(parent, false);
            var src = go.AddComponent<AudioSource>();
            Spatialize(src);
            src.clip = clip; src.loop = true; src.volume = 0f; src.playOnAwake = false;
            return src;
        }

        static void Spatialize(AudioSource s)
        {
            s.spatialBlend = 1f; s.minDistance = 0.6f; s.maxDistance = 18f; s.dopplerLevel = 0f;
            s.rolloffMode = AudioRolloffMode.Logarithmic;
        }

        // ------------------------------------------------------------------ synthèse

        delegate float Gen(float t, System.Random rng, ref float state);

        static AudioClip Make(string name, float seconds, Gen gen)
        {
            int n = (int)(Rate * seconds);
            var d = new float[n];
            var rng = new System.Random(name.GetHashCode());
            float state = 0f;
            for (int i = 0; i < n; i++) d[i] = Mathf.Clamp(gen(i / (float)Rate, rng, ref state), -1f, 1f);
            var clip = AudioClip.Create(name, n, 1, Rate, false);
            clip.SetData(d, 0);
            return clip;
        }

        static float Noise(System.Random r) => (float)r.NextDouble() * 2f - 1f;
        static float Sin(float f, float t) => Mathf.Sin(2f * Mathf.PI * f * t);

        static float ClickAt(float t, System.Random r, ref float hp)
        {
            float n = Noise(r); hp = n - hp * 0.6f;
            return (hp * 0.5f + Sin(2900f, t) * 0.5f) * Mathf.Exp(-t * 160f) * 0.8f;
        }

        static float BeepAt(float t, System.Random r, ref float s)
        {
            float env = Mathf.Clamp01(t / 0.005f) * Mathf.Clamp01((0.09f - t) / 0.02f);
            return (Sin(2200f, t) + Sin(4400f, t) * 0.25f) * env * 0.35f;
        }

        static float BellAt(float t, System.Random r, ref float s)
        {
            // « Ka » : claquement mécanique ; « ching » : cloche (partiels inharmoniques).
            float clack = Noise(r) * Mathf.Exp(-t * 90f) * 0.7f;
            float b = t - 0.07f;
            float bell = 0f;
            if (b > 0f)
            {
                float vib = 1f + 0.003f * Sin(6f, b);
                bell = (Sin(2093f * vib, b) + Sin(2637f * vib, b) * 0.6f + Sin(3951f, b) * 0.35f + Sin(5274f, b) * 0.2f)
                       * Mathf.Exp(-b * 2.6f) * Mathf.Clamp01(b / 0.003f) * 0.32f;
            }
            return clack + bell;
        }

        static float DrawerAt(float t, System.Random r, ref float lp)
        {
            lp += (Noise(r) - lp) * 0.05f;
            float roll = lp * 2.2f * Mathf.Clamp01(t / 0.03f) * Mathf.Clamp01((0.3f - t) / 0.1f);
            float stop = t > 0.28f ? Sin(85f, t - 0.28f) * Mathf.Exp(-(t - 0.28f) * 30f) * 0.8f : 0f;
            return roll + stop;
        }

        static float PourAt(float t, System.Random r, ref float lp)
        {
            // Liquide : bruit sourd + bulles (glouglou). Boucle de 2 s.
            lp += (Noise(r) - lp) * 0.12f;
            float bubble = 0f;
            float phase = (t * 9f) % 1f; int k = (int)(t * 9f);
            float f = 380f + (k * 137 % 400);
            bubble = Sin(f * (1f + phase * 0.8f), phase / 9f) * Mathf.Exp(-phase * 7f) * 0.35f;
            return lp * 1.6f + bubble;
        }

        static float PumpAt(float t, System.Random r, ref float lp)
        {
            lp += (Noise(r) - lp) * 0.2f;
            return (Sin(50f, t) * 0.35f + Sin(100f, t) * 0.25f + Sin(150f, t) * 0.1f) * (0.8f + 0.2f * Sin(5f, t)) + lp * 0.25f;
        }

        static float MotorAt(float t, System.Random r, ref float lp)
        {
            lp += (Noise(r) - lp) * 0.3f;
            float f = 95f + 60f * Mathf.Clamp01(t / 0.3f);
            float saw = ((t * f) % 1f) * 2f - 1f;
            float env = Mathf.Clamp01(t / 0.05f) * Mathf.Clamp01((0.9f - t) / 0.15f);
            return (saw * 0.3f + lp * 0.25f) * env;
        }

        static float ClunkAt(float t, System.Random r, ref float s)
        {
            return Sin(92f, t) * Mathf.Exp(-t * 14f) * 0.8f + (Sin(640f, t) + Sin(1530f, t) * 0.5f) * Mathf.Exp(-t * 18f) * 0.25f
                   + Noise(r) * Mathf.Exp(-t * 80f) * 0.4f;
        }

        static float ClinkAt(float t, System.Random r, ref float s)
        {
            return (Sin(3520f, t) + Sin(5240f, t) * 0.6f + Sin(7900f, t) * 0.3f) * Mathf.Exp(-t * 32f) * 0.4f;
        }

        static float SteamAt(float t, System.Random r, ref float prev)
        {
            float n = Noise(r); float hp = n - prev; prev = n;
            return hp * 0.35f * Mathf.Clamp01(t / 0.08f) * Mathf.Clamp01((1.2f - t) / 0.4f);
        }
    }
}
