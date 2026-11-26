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

        // Coups et casse par matière (décor cassable).
        static AudioClip s_WoodHit, s_WoodBreak, s_MetalHit, s_MetalBreak, s_PlasticHit, s_PlasticBreak, s_CeramicHit;
        public static AudioClip WoodHit => s_WoodHit != null ? s_WoodHit : s_WoodHit = Make("Coup bois", 0.3f, WoodHitAt);
        public static AudioClip WoodBreak => s_WoodBreak != null ? s_WoodBreak : s_WoodBreak = Make("Casse bois", 1f, WoodBreakAt);
        public static AudioClip MetalHit => s_MetalHit != null ? s_MetalHit : s_MetalHit = Make("Coup métal", 1f, MetalHitAt);
        public static AudioClip MetalBreak => s_MetalBreak != null ? s_MetalBreak : s_MetalBreak = Make("Casse métal", 1.5f, MetalBreakAt);
        public static AudioClip PlasticHit => s_PlasticHit != null ? s_PlasticHit : s_PlasticHit = Make("Coup plastique", 0.18f, PlasticHitAt);
        public static AudioClip PlasticBreak => s_PlasticBreak != null ? s_PlasticBreak : s_PlasticBreak = Make("Casse plastique", 0.6f, PlasticBreakAt);
        public static AudioClip CeramicHit => s_CeramicHit != null ? s_CeramicHit : s_CeramicHit = Make("Coup céramique", 0.22f, CeramicHitAt);

        // Armes : détonations (claquement + souffle + grondement + écho de la pièce), mécanismes, impacts, douilles.
        static AudioClip s_PistolShot, s_RifleShot, s_ShotgunShot, s_PumpRack, s_BoltCycle, s_SlideClack, s_BulletImpact, s_ShellDrop;
        public static AudioClip PistolShot => s_PistolShot != null ? s_PistolShot : s_PistolShot = Make("Tir pistolet", 0.9f, Shot(0.07f, 1.0f, 95f, 0.6f, 0.35f));
        public static AudioClip RifleShot => s_RifleShot != null ? s_RifleShot : s_RifleShot = Make("Tir carabine", 1.4f, Shot(0.12f, 1.0f, 70f, 0.85f, 0.55f));
        public static AudioClip ShotgunShot => s_ShotgunShot != null ? s_ShotgunShot : s_ShotgunShot = Make("Tir fusil à pompe", 1.6f, Shot(0.16f, 0.75f, 55f, 1.0f, 0.7f));
        public static AudioClip PumpRack => s_PumpRack != null ? s_PumpRack : s_PumpRack = Make("Pompe armée", 0.42f, Clacks(new[] { 0f, 0.2f }, 1700f, 1f));
        public static AudioClip BoltCycle => s_BoltCycle != null ? s_BoltCycle : s_BoltCycle = Make("Culasse", 0.75f, Clacks(new[] { 0f, 0.14f, 0.42f, 0.58f }, 2300f, 0.8f));
        public static AudioClip SlideClack => s_SlideClack != null ? s_SlideClack : s_SlideClack = Make("Culasse pistolet", 0.12f, Clacks(new[] { 0f, 0.045f }, 3100f, 0.5f));
        // Souris : couinements, cri de mort, « splotch » mouillé.
        static AudioClip s_Squeak, s_MouseDeath, s_Splat;
        public static AudioClip Squeak => s_Squeak != null ? s_Squeak : s_Squeak = Make("Couinement", 0.32f, Chirps(new[] { 0f, 0.09f, 0.2f }, 0.06f, 5200f, 7400f));
        public static AudioClip MouseDeath => s_MouseDeath != null ? s_MouseDeath : s_MouseDeath = Make("Cri de souris", 0.5f, Chirps(new[] { 0f }, 0.42f, 8200f, 3600f));
        public static AudioClip Splat => s_Splat != null ? s_Splat : s_Splat = Make("Splotch", 0.45f, SplatAt);

        /// <summary>Couinements : sifflements aigus qui glissent d'une fréquence à l'autre, avec un léger vibrato.</summary>
        static Gen Chirps(float[] starts, float length, float f0, float f1)
        {
            float phase = 0f;
            return (float t, System.Random r, ref float s) =>
            {
                float v = 0f;
                foreach (var t0 in starts)
                {
                    float d = t - t0;
                    if (d < 0f || d > length) continue;
                    float k = d / length;
                    float f = Mathf.Lerp(f0, f1, k) * (1f + 0.04f * Mathf.Sin(d * 2f * Mathf.PI * 38f));
                    phase += 2f * Mathf.PI * f / Rate;
                    float env = Mathf.Sin(Mathf.PI * k) * Mathf.Clamp01(d / 0.004f);
                    v += (Mathf.Sin(phase) + 0.3f * Mathf.Sin(phase * 2f)) * env * 0.35f;
                }
                return v;
            };
        }

        static float SplatAt(float t, System.Random r, ref float lp)
        {
            // Choc mou et mouillé : bruit sourd filtré + petits clapotis.
            lp += (Noise(r) - lp) * 0.08f;
            float thud = lp * 3f * Mathf.Exp(-t * 18f) + Sin(90f, t) * Mathf.Exp(-t * 25f) * 0.6f;
            float drips = 0f;
            for (int k = 0; k < 5; k++)
            {
                float d = t - (0.06f + k * 0.055f + (k * 31 % 7) * 0.006f);
                if (d > 0f) drips += Sin(900f + k * 230f, d) * Mathf.Exp(-d * 90f) * 0.18f;
            }
            return thud + drips;
        }

        static AudioClip s_ActionOpen, s_ActionClose, s_DryFire, s_LoadRound;
        public static AudioClip ActionOpen => s_ActionOpen != null ? s_ActionOpen : s_ActionOpen = Make("Mécanisme ouvert", 0.12f, Clacks(new[] { 0f }, 1900f, 0.9f));
        public static AudioClip ActionClose => s_ActionClose != null ? s_ActionClose : s_ActionClose = Make("Mécanisme fermé", 0.14f, Clacks(new[] { 0f, 0.012f }, 1500f, 1f));
        public static AudioClip DryFire => s_DryFire != null ? s_DryFire : s_DryFire = Make("Clic à vide", 0.06f, Clacks(new[] { 0f }, 3800f, 0.45f));
        public static AudioClip LoadRound => s_LoadRound != null ? s_LoadRound : s_LoadRound = Make("Cartouche introduite", 0.16f, Clacks(new[] { 0f, 0.05f }, 2600f, 0.55f));
        public static AudioClip BulletImpact => s_BulletImpact != null ? s_BulletImpact : s_BulletImpact = Make("Impact balle", 0.25f, ImpactAt);
        public static AudioClip ShellDrop => s_ShellDrop != null ? s_ShellDrop : s_ShellDrop = Make("Douille", 0.45f, ShellAt);

        /// <summary>Détonation. crack : claquement initial ; boomDecay : durée du souffle ; low : grondement (Hz) ; room : écho de la pièce.</summary>
        static Gen Shot(float boomDecay, float crack, float low, float body, float room)
        {
            float lp1 = 0f, lp2 = 0f, echo = 0f;
            return (float t, System.Random r, ref float s) =>
            {
                float n = Noise(r);
                lp1 += (n - lp1) * 0.35f;              // souffle (bruit adouci)
                lp2 += (n - lp2) * 0.04f;              // grondement de la pièce
                float attack = Mathf.Clamp01(t / 0.0008f);
                float c = n * Mathf.Exp(-t * 260f) * crack;                                       // claquement
                float boom = lp1 * Mathf.Exp(-t / boomDecay) * 1.6f * body;                       // souffle
                float thump = Mathf.Sin(2f * Mathf.PI * low * t * (1f - t * 0.6f)) * Mathf.Exp(-t * 9f) * 0.9f * body;
                echo += (lp2 * 6f - echo) * 0.02f;
                float tail = echo * Mathf.Exp(-t * 4.5f) * room * Mathf.Clamp01((t - 0.02f) / 0.05f);
                return Mathf.Clamp((c + boom + thump + tail) * attack, -1f, 1f) * 0.95f;
            };
        }

        /// <summary>Claquements métalliques (mécanisme) aux instants donnés.</summary>
        static Gen Clacks(float[] times, float ring, float gain)
        {
            return (float t, System.Random r, ref float hp) =>
            {
                float n = Noise(r); hp = n - hp * 0.5f;
                float v = 0f;
                for (int i = 0; i < times.Length; i++)
                {
                    float d = t - times[i];
                    if (d < 0f || d > 0.1f) continue;
                    v += (hp * 0.7f * Mathf.Exp(-d * 120f) + (Sin(ring * (1f + 0.08f * i), d) * 0.5f + Sin(ring * 1.73f, d) * 0.25f) * Mathf.Exp(-d * 55f)) * gain;
                }
                // Frottement entre les claquements (glissière).
                if (times.Length > 1 && t > times[0] && t < times[times.Length - 1]) v += hp * 0.05f * gain;
                return v;
            };
        }

        static float ImpactAt(float t, System.Random r, ref float lp)
        {
            lp += (Noise(r) - lp) * 0.3f;
            return (lp * 1.4f + Sin(180f, t) * 0.5f) * Mathf.Exp(-t * 28f) * 0.8f;
        }

        static float ShellAt(float t, System.Random r, ref float s)
        {
            // Douille qui rebondit : trois tintements de plus en plus faibles.
            float v = 0f;
            float[] at = { 0f, 0.13f, 0.22f, 0.28f };
            for (int i = 0; i < at.Length; i++)
            {
                float d = t - at[i]; if (d < 0f) continue;
                v += (Sin(4200f + i * 310f, d) + Sin(6900f, d) * 0.4f) * Mathf.Exp(-d * 45f) * Mathf.Pow(0.55f, i) * 0.4f;
            }
            return v;
        }

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

        public static void Spatialize(AudioSource s)
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

        static float WoodHitAt(float t, System.Random r, ref float lp)
        {
            // Coup sourd sur du bois : « toc » grave, petit claquement d'attaque.
            lp += (Noise(r) - lp) * 0.35f;
            return (Sin(150f, t) * 0.7f + Sin(330f, t) * 0.35f + Sin(710f, t) * 0.15f) * Mathf.Exp(-t * 22f)
                   + lp * Mathf.Exp(-t * 90f) * 0.8f;
        }

        static float WoodBreakAt(float t, System.Random r, ref float lp)
        {
            // Bois qui éclate : série de craquements secs (échardes) puis morceaux qui retombent.
            lp += (Noise(r) - lp) * 0.5f;
            float snaps = 0f;
            for (int k = 0; k < 7; k++)
            {
                float t0 = k * 0.055f + (k * 37 % 11) * 0.004f, d = t - t0;
                if (d > 0f) snaps += Noise(r) * Mathf.Exp(-d * 70f) * (1f - k * 0.09f);
            }
            float thud = Sin(110f, t) * Mathf.Exp(-t * 12f) * 0.6f;
            float fall = t > 0.45f ? (Sin(240f, t) + Sin(520f, t) * 0.5f) * Mathf.Exp(-(t - 0.45f) * 18f) * 0.3f : 0f;
            return snaps * 0.7f + thud + fall + lp * Mathf.Exp(-t * 6f) * 0.2f;
        }

        static float MetalHitAt(float t, System.Random r, ref float s)
        {
            // « Bong » métallique : partiels inharmoniques, longue résonance.
            return (Sin(431f, t) * Mathf.Exp(-t * 4f) + Sin(1187f, t) * 0.6f * Mathf.Exp(-t * 6f)
                    + Sin(2063f, t) * 0.4f * Mathf.Exp(-t * 9f) + Sin(3311f, t) * 0.25f * Mathf.Exp(-t * 14f)) * 0.4f
                   + Noise(r) * Mathf.Exp(-t * 120f) * 0.5f;
        }

        static readonly float[] k_Clangs = { 0f, 0.09f, 0.21f, 0.38f, 0.6f };

        static float MetalBreakAt(float t, System.Random r, ref float lp)
        {
            // Fracas : plusieurs chocs métalliques qui se chevauchent + grondement.
            lp += (Noise(r) - lp) * 0.08f;
            float clangs = 0f;
            for (int k = 0; k < k_Clangs.Length; k++)
            {
                float d = t - k_Clangs[k]; if (d <= 0f) continue;
                float f = 380f + k * 170f;
                clangs += (Sin(f, d) + Sin(f * 2.76f, d) * 0.5f + Sin(f * 5.4f, d) * 0.25f) * Mathf.Exp(-d * (5f + k)) * (1f - k * 0.15f);
            }
            return clangs * 0.35f + lp * Mathf.Exp(-t * 3f) * 0.9f + Noise(r) * Mathf.Exp(-t * 40f) * 0.4f;
        }

        static float PlasticHitAt(float t, System.Random r, ref float s)
        {
            return (Sin(620f, t) * 0.6f + Sin(1430f, t) * 0.3f) * Mathf.Exp(-t * 45f) + Noise(r) * Mathf.Exp(-t * 150f) * 0.5f;
        }

        static float PlasticBreakAt(float t, System.Random r, ref float s)
        {
            float snap = Noise(r) * (Mathf.Exp(-t * 60f) + (t > 0.12f ? Mathf.Exp(-(t - 0.12f) * 70f) * 0.6f : 0f));
            float rattle = 0f; float ph = (t * 23f) % 1f;
            if (t > 0.2f) rattle = Sin(900f + (int)(t * 23f) * 130 % 600, ph / 23f) * Mathf.Exp(-ph * 12f) * Mathf.Exp(-(t - 0.2f) * 5f) * 0.3f;
            return snap * 0.7f + rattle;
        }

        static float CeramicHitAt(float t, System.Random r, ref float s)
        {
            return (Sin(2480f, t) + Sin(3970f, t) * 0.6f + Sin(6120f, t) * 0.3f) * Mathf.Exp(-t * 30f) * 0.35f + Noise(r) * Mathf.Exp(-t * 200f) * 0.3f;
        }

        static float SteamAt(float t, System.Random r, ref float prev)
        {
            float n = Noise(r); float hp = n - prev; prev = n;
            return hp * 0.35f * Mathf.Clamp01(t / 0.08f) * Mathf.Clamp01((1.2f - t) / 0.4f);
        }
    }
}
