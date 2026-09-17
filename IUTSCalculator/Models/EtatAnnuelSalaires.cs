using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace IUTSCalculator.Models
{
    /// <summary>
    /// État annuel des salaires — DADS (module DEC-05).
    ///
    /// Récapitulatif annuel par salarié et par rubrique : cumuls de brut, de base
    /// imposable, d'IUTS retenu et de cotisations. Alimenté DIRECTEMENT par les
    /// périodes clôturées (module PAI-01) — un mois jamais ouvert ou encore ouvert
    /// n'entre pas dans le cumul, exactement comme <see cref="PeriodePaie"/> refuse
    /// de figer les totaux d'un mois non arrêté. Un salarié peut ainsi porter un
    /// cumul « incomplet » si tous les mois de l'année ne sont pas encore clôturés :
    /// <see cref="MoisManquants"/> le dit explicitement, plutôt que de laisser croire
    /// à un cumul annuel complet qui ne l'est pas.
    ///
    /// Même principe que <see cref="LivrePaie"/> (module ETA-01), dont il reprend la
    /// construction (lecture de <c>TaxCalculations</c> et de l'archive du bulletin
    /// pour le détail par rubrique) — étendue sur douze mois au lieu d'un seul, et
    /// bornée aux mois clôturés. Comme le Livre de paie et les totaux de période
    /// (PAI-01), les régularisations (<c>IsRegularization</c>, module PAI-07) et les
    /// calculs annuels (<c>IsAnnual</c>) ne sont pas inclus dans ce cumul.
    /// </summary>
    public class EtatAnnuelSalaires
    {
        // ── En-tête employeur ────────────────────────────────────────────────

        public string RaisonSociale { get; set; }
        public string Ifu { get; set; }
        public string Rccm { get; set; }
        public string Adresse { get; set; }
        public string Ville { get; set; }
        public string Telephone { get; set; }

        // ── Période ──────────────────────────────────────────────────────────

        public int Annee { get; set; }

        public DateTime DateGeneration { get; set; } = DateTime.Now;

        /// <summary>Mois (1-12) effectivement clôturés et inclus dans le cumul.</summary>
        public List<int> MoisClotures { get; set; } = new List<int>();

        /// <summary>Mois (1-12) jamais ouverts ou encore ouverts : absents du cumul.</summary>
        public List<int> MoisManquants { get; set; } = new List<int>();

        /// <summary>Vrai si les douze mois de l'année sont clôturés : le cumul est complet.</summary>
        public bool EstComplet => MoisManquants.Count == 0 && MoisClotures.Count == 12;

        // ── Données ──────────────────────────────────────────────────────────

        public List<LigneEtatAnnuelSalaires> Lignes { get; set; } = new List<LigneEtatAnnuelSalaires>();

        /// <summary>
        /// Rubriques dynamiques rencontrées sur l'année, dans l'ordre du plan de paie —
        /// même mécanique que <see cref="LivrePaie.ColonnesDynamiques"/>.
        /// </summary>
        public List<ColonneRubriqueDynamique> ColonnesDynamiques { get; set; }
            = new List<ColonneRubriqueDynamique>();

        // ── Totaux (calculés) ────────────────────────────────────────────────

        public int Effectif => Lignes.Count;

        public decimal TotalSalaireBrut => Lignes.Sum(l => l.CumulSalaireBrut);
        public decimal TotalBaseImposable => Lignes.Sum(l => l.CumulBaseImposable);
        public decimal TotalIuts => Lignes.Sum(l => l.CumulIuts);
        public decimal TotalCnssSalariale => Lignes.Sum(l => l.CumulCnssSalariale);
        public decimal TotalCnssPatronale => Lignes.Sum(l => l.CumulCnssPatronale);
        public decimal TotalTpa => Lignes.Sum(l => l.CumulTpa);
        public decimal TotalNetAPayer => Lignes.Sum(l => l.CumulNetAPayer);
        public decimal TotalMasseSalariale => Lignes.Sum(l => l.CumulMasseSalariale);

        // ── Affichage ────────────────────────────────────────────────────────

        public string PeriodeLibelle => string.Format("Année {0}", Annee);

        public string NomFichierPropose => string.Format(
            "Etat_annuel_salaires_{0}_{1}.xlsx", (RaisonSociale ?? "").Replace(" ", "_"), Annee);

        private static string NomMois(int mois)
        {
            var culture = CultureInfo.GetCultureInfo("fr-FR");
            string nom = new DateTime(2000, mois, 1).ToString("MMMM", culture);
            return culture.TextInfo.ToTitleCase(nom);
        }

        public string MoisClosLibelle => MoisClotures.Count == 0
            ? "aucun"
            : string.Join(", ", MoisClotures.OrderBy(m => m).Select(NomMois));

        public string MoisManquantsLibelle => MoisManquants.Count == 0
            ? "aucun"
            : string.Join(", ", MoisManquants.OrderBy(m => m).Select(NomMois));

        /// <summary>
        /// Anomalies détectées (mois non clôturés, aucun cumul possible, salarié sans
        /// matricule, etc.) — même esprit que <see cref="LivrePaie.Anomalies"/>.
        /// </summary>
        public List<string> Anomalies()
        {
            var anomalies = new List<string>();

            if (MoisClotures.Count == 0)
            {
                anomalies.Add(string.Format(
                    "Aucun mois clôturé sur {0} : aucun cumul ne peut être établi. Clôturez les périodes " +
                    "du mois (écran « Périodes de paie ») avant d'éditer l'état annuel.",
                    Annee));
                return anomalies;
            }

            if (MoisManquants.Count > 0)
            {
                anomalies.Add(string.Format(
                    "État PARTIEL : {0} mois clôturé(s) sur 12 pour {1}. Mois absents de ce cumul : {2}.",
                    MoisClotures.Count, Annee, MoisManquantsLibelle));
            }

            if (Lignes.Count == 0)
            {
                anomalies.Add("Aucun bulletin enregistré sur les mois clôturés de cette année.");
            }

            anomalies.Add(
                "Les rappels et régularisations (module PAI-07) et les calculs annuels ne sont pas inclus " +
                "dans ce cumul, comme dans le Livre de paie mensuel (module ETA-01).");

            foreach (var ligne in Lignes)
            {
                if (string.IsNullOrWhiteSpace(ligne.Matricule))
                    anomalies.Add(string.Format("{0} : matricule absent.", ligne.NomComplet));
            }

            return anomalies;
        }
    }

    /// <summary>
    /// Une ligne de l'état annuel : un salarié et ses cumuls sur les mois clôturés
    /// de l'année.
    /// </summary>
    public class LigneEtatAnnuelSalaires
    {
        // ── Identité ─────────────────────────────────────────────────────────

        public string Matricule { get; set; }
        public string Nom { get; set; }
        public string Prenom { get; set; }
        public string Categorie { get; set; }
        public string Emploi { get; set; }
        public string NumeroCnss { get; set; }
        public DateTime? DateEntree { get; set; }

        public string NomComplet => string.Format("{0} {1}", Nom, Prenom).Trim();

        // ── Cumuls sur les mois clôturés ─────────────────────────────────────

        /// <summary>Nombre de bulletins entrant dans le cumul (un par mois clôturé où le salarié a été payé).</summary>
        public int NombreBulletins { get; set; }

        public decimal CumulSalaireBrut { get; set; }
        public decimal CumulBaseImposable { get; set; }
        public decimal CumulIuts { get; set; }
        public decimal CumulCnssSalariale { get; set; }
        public decimal CumulCnssPatronale { get; set; }
        public decimal CumulTpa { get; set; }
        public decimal CumulNetAPayer { get; set; }

        /// <summary>
        /// Détail cumulé par rubrique dynamique : code rubrique → montant signé,
        /// additionné mois clôturé après mois clôturé. Un gain est positif, une
        /// retenue est négative.
        /// </summary>
        public Dictionary<string, decimal> MontantsParRubrique { get; set; }
            = new Dictionary<string, decimal>();

        /// <summary>Coût employeur cumulé = brut + charges patronales (CNSS patron + TPA).</summary>
        public decimal CumulMasseSalariale => CumulSalaireBrut + CumulCnssPatronale + CumulTpa;
    }
}
