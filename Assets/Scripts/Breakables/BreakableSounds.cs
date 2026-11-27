using System.Globalization;
using System.Text;
using Fragile.Audio;

namespace Fragile.Breakables
{
    /// <summary>
    /// Sons d'un objet cassable, choisis d'après son nom (tabouret, table, tireuse, bouteille…) puis sa matière.
    /// Une famille vide ou absente laisse place au son généré par le code.
    /// </summary>
    public sealed class BreakableSounds
    {
        public struct Layer
        {
            public string family; public float volume;
            public Layer(string f, float v) { family = f; volume = v; }
        }

        /// <summary>Coup encaissé sans casser.</summary>
        public string[] hit = System.Array.Empty<string>();
        /// <summary>Casse : couches jouées ensemble (ex. métal tordu + jet de bière).</summary>
        public Layer[] brk = System.Array.Empty<Layer>();
        /// <summary>Chute ou renversement (choc trop faible pour abîmer).</summary>
        public string fall;
        /// <summary>Roule par terre (bouteilles).</summary>
        public string roll;
        /// <summary>Objet en verre : petits chocs = tintement généré.</summary>
        public bool glass;

        public static BreakableSounds For(string objectName, Breakable.Matter matter)
        {
            string n = Simplify(objectName);
            var s = new BreakableSounds();

            if (Has(n, "verre_"))
            {
                // Verres à boire (GlassesBuilder) : petit fracas de verre ; le shooter, épais, sonne comme un shot cassé.
                s.brk = Has(n, "shot") ? new[] { new Layer(SfxBank.ShotCasse, 1f) }
                                       : new[] { new Layer(SfxBank.VerreVinCasse, 0.85f), new Layer(SfxBank.ShotCasse, 0.5f) };
                s.glass = true;
                return s;
            }
            if (!Has(n, "etagere") && Has(n, "bouteille", "flasque"))
            {
                s.brk = GlassBreak;
                s.roll = SfxBank.BouteilleRoulement;
                s.glass = true;
                return s;
            }
            if (Has(n, "miroir"))
            {
                // Miroir : il se fend au premier coup, puis éclate (fracas de verre + crissement du tain).
                s.hit = new[] { SfxBank.MiroirFissure };
                s.brk = new[] { new Layer(SfxBank.VerreVinCasse, 1f), new Layer(SfxBank.MiroirFissure, 0.7f), new Layer(SfxBank.ShotCasse, 0.5f) };
                s.glass = true;
                return s;
            }
            if (Has(n, "puff"))
            {
                s.brk = GlassBreak;
                s.glass = true;
                return s;
            }
            if (Has(n, "plante", "pot de fleur", "potted"))
            {
                // Pot de fleurs : même fracas que les bouteilles.
                s.hit = new[] { SfxBank.ImpactLeger };
                s.brk = GlassBreak;
                s.fall = SfxBank.ObjetTombe;
                return s;
            }
            if (Has(n, "tireuse"))
            {
                s.hit = new[] { SfxBank.TireuseFrappee };
                s.brk = new[] { new Layer(SfxBank.MetalTordu, 1f), new Layer(SfxBank.TireuseArrachee, 0.8f) };
                s.fall = SfxBank.ObjetTombe;
                return s;
            }
            if (Has(n, "tabouret", "chaise", "fauteuil", "banc", "pouf", "canape", "chair", "stool"))
            {
                s.hit = new[] { SfxBank.ImpactLourd };
                s.brk = new[] { new Layer(SfxBank.ChaiseCassee, 1f) };
                s.fall = SfxBank.ChaiseTombe;
                return s;
            }
            if (!Has(n, "tableau", "plante") && Has(n, "table", "gueridon", "mange-debout"))
            {
                s.hit = new[] { SfxBank.ImpactLourd };
                s.brk = new[] { new Layer(SfxBank.TableCasse, 1f) };
                s.fall = SfxBank.TableRenversee;
                return s;
            }

            switch (matter)
            {
                case Breakable.Matter.Wood:
                    s.hit = new[] { SfxBank.ImpactLourd };
                    s.brk = new[] { new Layer(SfxBank.TableCasse, 1f) };
                    s.fall = SfxBank.ObjetTombe;
                    break;
                case Breakable.Matter.Metal:
                    s.hit = new[] { SfxBank.TireuseFrappee, SfxBank.MetalTordu };
                    s.brk = new[] { new Layer(SfxBank.MetalTordu, 1f), new Layer(SfxBank.ImpactLourd, 0.8f) };
                    s.fall = SfxBank.ObjetTombe;
                    break;
                case Breakable.Matter.Plastic:
                    s.hit = new[] { SfxBank.ImpactLeger };
                    s.brk = new[] { new Layer(SfxBank.GobeletEcrase, 1f), new Layer(SfxBank.PlastiqueCraque, 0.5f) };
                    s.fall = SfxBank.ObjetTombe;
                    break;
                case Breakable.Matter.Glass:
                    s.hit = new[] { SfxBank.ImpactLeger };
                    s.brk = new[] { new Layer(SfxBank.VerreVinCasse, 1f), new Layer(SfxBank.ShotCasse, 0.6f) };
                    s.glass = true;
                    break;
                default: // céramique (trophée…) : casse de porcelaine enregistrée, sinon le fracas des bouteilles
                    s.hit = new[] { SfxBank.ImpactLeger };
                    s.brk = SfxBank.Clips(SfxBank.PorcelaineCasse).Length > 0 ? new[] { new Layer(SfxBank.PorcelaineCasse, 1f) } : GlassBreak;
                    s.fall = SfxBank.ObjetTombe;
                    break;
            }
            return s;
        }

        /// <summary>Casse de verre (bouteilles, puff, pots de fleurs, écrans de télé).</summary>
        public static readonly Layer[] GlassBreak = { new Layer(SfxBank.VerreVinCasse, 1f), new Layer(SfxBank.ShotCasse, 0.55f) };

        /// <summary>Joue la casse de verre des bouteilles ; false si la banque est absente.</summary>
        public static bool PlayGlassBreak(UnityEngine.Vector3 point, float volume, float pitch)
        {
            bool played = false;
            foreach (var l in GlassBreak) played |= SfxBank.Play(l.family, point, volume * l.volume, pitch);
            return played;
        }

        /// <summary>Une famille de coup au hasard parmi celles de l'objet (null s'il n'y en a pas).</summary>
        public string PickHit() => hit.Length == 0 ? null : hit[UnityEngine.Random.Range(0, hit.Length)];

        public void Preload()
        {
            SfxBank.Preload(hit);
            foreach (var l in brk) SfxBank.Preload(l.family);
            if (fall != null) SfxBank.Preload(fall);
        }

        static bool Has(string n, params string[] keys)
        {
            foreach (var k in keys) if (n.Contains(k)) return true;
            return false;
        }

        /// <summary>Minuscules, sans accents : « Étagère à bouteilles » -> « etagere a bouteilles ».</summary>
        static string Simplify(string s)
        {
            var d = (s ?? "").ToLowerInvariant().Normalize(NormalizationForm.FormD);
            var b = new StringBuilder(d.Length);
            foreach (var ch in d) if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark) b.Append(ch);
            return b.ToString();
        }
    }
}
