using System.Collections.Generic;
using UnityEngine;

namespace Fragile.Audio
{
    /// <summary>
    /// Bruitages enregistrés (Assets/Bar/Resources/Sons/&lt;Famille&gt;/) : chaque famille regroupe plusieurs variantes,
    /// tirées au hasard sans rejouer deux fois de suite la même. Une famille absente renvoie null : l'appelant garde
    /// alors son son généré par le code (<see cref="Sfx"/>).
    /// </summary>
    public static class SfxBank
    {
        // Familles (= noms des dossiers).
        public const string TirRevolver = "Tir_Revolver", TirRifle = "Tir_Rifle", TirShotgun = "Tir_Shotgun";
        public const string ImpactLeger = "Impact_Leger", ImpactLourd = "Impact_Lourd", ObjetTombe = "ObjetTombe";
        public const string MetalTordu = "MetalTordu", TireuseFrappee = "Tireuse_Frappee", TireuseArrachee = "Tireuse_Arrachee";
        public const string ChaiseCassee = "Chaise_Cassee", ChaiseTombe = "Chaise_Tombe";
        public const string TableCasse = "Table_Casse", TableRenversee = "Table_Renversee";
        public const string EtagereBouteille = "EtagereBouteille", VerreVinCasse = "Verre_Vin_Casse", ShotCasse = "ShotCasse";
        public const string MiroirFissure = "Miroir_Fissure", BouteilleRoulement = "Bouteille_Roulement";
        public const string CannetteEcrasee = "Cannette_Ecrasee", CannettePercee = "Cannette_Percee";
        public const string GobeletEcrase = "GobeletEcrase", PlastiqueCraque = "PlastiqueCraque", PasDebris = "Pas_Debris", PasParquet = "Pas_Parquet";
        public const string DouillePistolet = "Douille_Pistolet", DouilleCarabine = "Douille_Carabine", DouillePlastique = "Douille_Plastique";
        public const string PompeArriere = "Pompe_Arriere", PompeAvant = "Pompe_Avant";
        public const string VerrouReleve = "Verrou_Releve", VerrouTire = "Verrou_Tire", VerrouPousse = "Verrou_Pousse", VerrouRabattu = "Verrou_Rabattu";
        // Familles à remplir (dossiers créés vides) : tant qu'elles sont vides, l'ancien son reste.
        public const string MarbreCoup = "Marbre_Coup", Glacon = "Glacon", PorcelaineCasse = "Porcelaine_Casse", BiereEcoulement = "Biere_Ecoulement";

        static readonly Dictionary<string, AudioClip[]> s_Clips = new Dictionary<string, AudioClip[]>();
        static readonly Dictionary<string, int> s_Last = new Dictionary<string, int>();

        /// <summary>
        /// Lancement du jeu : on oublie ce qui a été chargé avant. Le projet ne recharge pas les scripts à chaque Play,
        /// donc sans ça une famille ajoutée pendant une partie resterait vide aux parties suivantes.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetCache() { s_Clips.Clear(); s_Last.Clear(); }

        /// <summary>Toutes les variantes d'une famille (chargées une fois, puis gardées).</summary>
        public static AudioClip[] Clips(string family)
        {
            if (string.IsNullOrEmpty(family)) return System.Array.Empty<AudioClip>();
            if (!s_Clips.TryGetValue(family, out var clips))
            {
                clips = Resources.LoadAll<AudioClip>("Sons/" + family);
                System.Array.Sort(clips, (a, b) => string.CompareOrdinal(a.name, b.name));
                // Une famille vide n'est pas retenue : si ses sons arrivent plus tard, ils seront trouvés.
                if (clips.Length > 0) s_Clips[family] = clips;
            }
            return clips;
        }

        /// <summary>Charge d'avance (pas d'à-coup au premier coup).</summary>
        public static void Preload(params string[] families)
        {
            foreach (var f in families) foreach (var c in Clips(f)) if (c.loadState == AudioDataLoadState.Unloaded) c.LoadAudioData();
        }

        /// <summary>Une variante au hasard, jamais deux fois de suite la même ; null si la famille est vide.</summary>
        public static AudioClip Pick(string family)
        {
            var clips = Clips(family);
            if (clips.Length == 0) return null;
            if (clips.Length == 1) return clips[0];
            s_Last.TryGetValue(family, out int last);
            int i = Random.Range(0, clips.Length - 1);
            if (i >= last) i++;
            s_Last[family] = i;
            return clips[i];
        }

        /// <summary>Joue une variante à un endroit. Renvoie false si la famille est vide (l'appelant peut jouer autre chose).</summary>
        public static bool Play(string family, Vector3 position, float volume = 1f, float pitch = 1f)
        {
            var clip = Pick(family);
            if (clip == null) return false;
            Sfx.PlayAt(clip, position, volume, pitch);
            return true;
        }

        /// <summary>Joue une variante qui suit un objet (jet d'une canette percée qui vole).</summary>
        public static AudioSource PlayAttached(string family, Transform parent, float volume = 1f, float pitch = 1f)
        {
            var clip = Pick(family);
            if (clip == null || parent == null) return null;
            var go = new GameObject("Son " + clip.name);
            go.transform.SetParent(parent, false);
            var src = go.AddComponent<AudioSource>();
            Sfx.Spatialize(src);
            src.clip = clip; src.volume = volume; src.pitch = pitch;
            src.Play();
            Object.Destroy(go, clip.length / Mathf.Max(0.1f, pitch) + 0.2f);
            return src;
        }
    }
}
