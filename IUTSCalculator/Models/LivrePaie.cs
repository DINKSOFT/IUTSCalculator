using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace IUTSCalculator.Models
{
    /// <summary>
    /// Livre de paie mensuel (module ETA-01).
    ///
    /// État récapitulatif obligatoire : tous les salariés en lignes, toutes les
    /// rubriques en colonnes, totaux de contrôle. Construit à partir des bulletins
    /// enregistrés — il ne recalcule rien, il restitue.
    /// </summary>
    public class LivrePaie
    {
        // ── En-tête employeur ────────────────────────────────────────────────

        public string RaisonSociale { get; set; }
        public string Ifu { get; set; }
        public string Rccm { get; set; }
        public string Adresse { get; set; }
        public string Ville { get; set; }
        public string Telephone { get; set; }

        // ── Période ──────────────────────────────────────────────────────────

        public DateTime DebutPeriode { get; set; }
        public DateTime FinPeriode { get; set; }

        /// <summary>Vrai tant que la période n'est pas clôturée : les montants peuvent encore bouger.</summary>
        public bool EstBrouillon { get; set; }

        public DateTime DateGeneration { get; set; } = DateTime.Now;

        // ── Données ──────────────────────────────────────────────────────────

        public List<LigneLivrePaie> Lignes { get; set; } = new List<LigneLivrePaie>();

        /// <summary>
        /// Rubriques dynamiques qui apparaissent dans les bulletins du mois,
        /// dans l'ordre du plan de paie. Chaque entrée est un couple
        /// (code rubrique, libellé) : l'export et l'écran s'en servent pour
        /// nommer les colonnes.
        /// </summary>
        public List<ColonneRubriqueDynamique> ColonnesDynamiques { get; set; }
            = new List<ColonneRubriqueDynamique>();

        // ── Totaux (calculés) ────────────────────────────────────────────────

        public int Effectif { get { return Lignes.Count; } }

        public decimal TotalSalaireBase { get { return Lignes.Sum(l => l.SalaireDeBase); } }
        public decimal TotalIndemniteLogement { get { return Lignes.Sum(l => l.IndemniteLogement); } }
        public decimal TotalIndemniteTransport { get { return Lignes.Sum(l => l.IndemniteTransport); } }
        public decimal TotalIndemniteFonction { get { return Lignes.Sum(l => l.IndemniteFonction); } }
        public decimal TotalIndemniteSujetion { get { return Lignes.Sum(l => l.IndemniteSujetion); } }
        public decimal TotalGainsVariables { get { return Lignes.Sum(l => l.GainsVariables); } }
        public decimal TotalRetenueAbsences { get { return Lignes.Sum(l => l.RetenueAbsences); } }
        public decimal TotalBrut { get { return Lignes.Sum(l => l.SalaireBrut); } }
        public decimal TotalCnssSalariale { get { return Lignes.Sum(l => l.CnssSalariale); } }
        public decimal TotalCnssPatronale { get { return Lignes.Sum(l => l.CnssPatronale); } }
        public decimal TotalIuts { get { return Lignes.Sum(l => l.Iuts); } }
        public decimal TotalRetenueSpeciale { get { return Lignes.Sum(l => l.RetenueSpeciale); } }
        public decimal TotalRetenuesDiverses { get { return Lignes.Sum(l => l.RetenuesDiverses); } }
        public decimal TotalNetAPayer { get { return Lignes.Sum(l => l.NetAPayer); } }
        public decimal TotalTpa { get { return Lignes.Sum(l => l.Tpa); } }
        public decimal TotalMasseSalariale { get { return Lignes.Sum(l => l.MasseSalariale); } }

        // ── Affichage ────────────────────────────────────────────────────────

        public string PeriodeLibelle
        {
            get
            {
                var culture = CultureInfo.GetCultureInfo("fr-FR");
                string moisAnnee = FinPeriode.ToString("MMMM yyyy", culture);
                moisAnnee = culture.TextInfo.ToTitleCase(moisAnnee);
                return moisAnnee;
            }
        }

        public string NomFichierPropose
        {
            get
            {
                return string.Format("Livre_paie_{0}_{1:yyyy_MM}.xlsx",
                    (RaisonSociale ?? "").Replace(" ", "_"), FinPeriode);
            }
        }

        /// <summary>
        /// Anomalies détectées (mois non clôturé, salarié sans bulletin, etc.).
        /// </summary>
        public List<string> Anomalies()
        {
            var anomalies = new List<string>();

            if (EstBrouillon)
                anomalies.Add(string.Format(
                    "La période {0} n'est pas clôturée : ce livre de paie est un brouillon, les montants peuvent encore changer.",
                    PeriodeLibelle));

            if (Lignes.Count == 0)
                anomalies.Add("Aucun bulletin enregistré sur ce mois.");

            foreach (var ligne in Lignes)
            {
                if (string.IsNullOrWhiteSpace(ligne.Matricule))
                    anomalies.Add(string.Format("{0} : matricule absent.", ligne.NomComplet));
            }

            return anomalies;
        }
    }

    /// <summary>
    /// Une ligne du livre de paie : un salarié et ses montants du mois.
    /// </summary>
    public class LigneLivrePaie
    {
        // ── Identité ─────────────────────────────────────────────────────────

        public string Matricule { get; set; }
        public string Nom { get; set; }
        public string Prenom { get; set; }
        public string Categorie { get; set; }
        public string Emploi { get; set; }
        public DateTime? DateEntree { get; set; }

        public string NomComplet
        {
            get { return string.Format("{0} {1}", Nom, Prenom).Trim(); }
        }

        // ── Rémunération permanente ──────────────────────────────────────────

        public decimal SalaireDeBase { get; set; }
        public decimal IndemniteLogement { get; set; }
        public decimal IndemniteTransport { get; set; }
        public decimal IndemniteFonction { get; set; }
        public decimal IndemniteSujetion { get; set; }

        // ── Variables du mois ────────────────────────────────────────────────

        /// <summary>Total des gains variables (compris dans le brut).</summary>
        public decimal GainsVariables { get; set; }

        /// <summary>Retenue pour absences non rémunérées (déjà déduite du brut).</summary>
        public decimal RetenueAbsences { get; set; }

        /// <summary>
        /// Détail par rubrique dynamique : code rubrique → montant signé.
        /// Un gain est positif, une retenue est négative.
        /// </summary>
        public Dictionary<string, decimal> MontantsParRubrique { get; set; }
            = new Dictionary<string, decimal>();

        // ── Synthèse ─────────────────────────────────────────────────────────

        public decimal SalaireBrut { get; set; }
        public decimal CnssSalariale { get; set; }
        public decimal CnssPatronale { get; set; }
        public decimal Iuts { get; set; }
        public decimal RetenueSpeciale { get; set; }
        public decimal RetenuesDiverses { get; set; }
        public decimal NetAPayer { get; set; }
        public decimal Tpa { get; set; }

        /// <summary>Coût employeur = brut + charges patronales (CNSS patron + TPA).</summary>
        public decimal MasseSalariale
        {
            get { return SalaireBrut + CnssPatronale + Tpa; }
        }
    }

    /// <summary>
    /// Colonne dynamique du livre de paie : une rubrique variable qui apparaît
    /// dans au moins un bulletin du mois.
    /// </summary>
    public class ColonneRubriqueDynamique
    {
        public string Code { get; set; }
        public string Libelle { get; set; }
        public int Ordre { get; set; }

        /// <summary>Vrai pour une retenue, faux pour un gain.</summary>
        public bool EstRetenue { get; set; }
    }
}
