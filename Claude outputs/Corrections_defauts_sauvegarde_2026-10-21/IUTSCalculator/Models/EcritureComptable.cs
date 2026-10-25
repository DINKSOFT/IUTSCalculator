using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace IUTSCalculator.Models
{
    /// <summary>
    /// Représente la pièce comptable mensuelle d'Opérations Diverses (OD de paie)
    /// selon le plan comptable SYSCOHADA révisé (module CPT-01).
    /// </summary>
    public class PieceComptable
    {
        // ── En-tête de la pièce ──────────────────────────────────────────────

        public string NumeroPiece { get; set; }
        public string CodeJournal { get; set; } = "OD";
        public string LibelleJournal { get; set; } = "Opérations Diverses";
        public DateTime DatePiece { get; set; }

        public string RaisonSociale { get; set; }
        public string Ifu { get; set; }

        // ── Période couverte ─────────────────────────────────────────────────

        public DateTime DebutPeriode { get; set; }
        public DateTime FinPeriode { get; set; }

        /// <summary>Vrai si la période n'est pas encore clôturée dans les périodes de paie.</summary>
        public bool EstBrouillon { get; set; }

        public DateTime DateGeneration { get; set; } = DateTime.Now;

        // ── Lignes d'écriture ────────────────────────────────────────────────

        public List<LigneEcritureComptable> Lignes { get; set; } = new List<LigneEcritureComptable>();

        // ── Totaux et équilibre ──────────────────────────────────────────────

        public decimal TotalDebit
        {
            get { return Lignes.Sum(l => l.Debit); }
        }

        public decimal TotalCredit
        {
            get { return Lignes.Sum(l => l.Credit); }
        }

        public decimal Ecart
        {
            get { return TotalDebit - TotalCredit; }
        }

        public bool EstEquilibree
        {
            get { return Math.Abs(Ecart) < 0.005m; }
        }

        public int NombreLignes
        {
            get { return Lignes.Count; }
        }

        // ── Libellés et export ───────────────────────────────────────────────

        public string PeriodeLibelle
        {
            get
            {
                var culture = CultureInfo.GetCultureInfo("fr-FR");
                string texte = FinPeriode.ToString("MMMM yyyy", culture);
                return culture.TextInfo.ToTitleCase(texte);
            }
        }

        public string NomFichierExcel
        {
            get
            {
                string entreprise = (RaisonSociale ?? "Entreprise").Replace(" ", "_");
                return string.Format("OD_Paie_{0}_{1:yyyy_MM}.xlsx", entreprise, FinPeriode);
            }
        }

        public string NomFichierTxt
        {
            get
            {
                string entreprise = (RaisonSociale ?? "Entreprise").Replace(" ", "_");
                return string.Format("OD_Paie_{0}_{1:yyyy_MM}.txt", entreprise, FinPeriode);
            }
        }

        public List<string> Anomalies()
        {
            var liste = new List<string>();

            if (EstBrouillon)
            {
                liste.Add(string.Format(
                    "La période {0} n'est pas encore clôturée : cette écriture est un brouillon.",
                    PeriodeLibelle));
            }

            if (Lignes.Count == 0)
            {
                liste.Add("Aucune écriture générée : aucun bulletin enregistré sur cette période.");
            }
            else if (!EstEquilibree)
            {
                liste.Add(string.Format(
                    "Déséquilibre comptable détecté : Total Débit ({0:N0}) != Total Crédit ({1:N0}), Écart : {2:N0} FCFA.",
                    TotalDebit, TotalCredit, Ecart));
            }

            return liste;
        }
    }

    /// <summary>
    /// Une ligne d'écriture comptable dans la pièce d'OD de paie.
    /// </summary>
    public class LigneEcritureComptable
    {
        public int Ordre { get; set; }
        public DateTime DateEcriture { get; set; }
        public string CodeJournal { get; set; } = "OD";
        public string ReferencePiece { get; set; }
        public string CompteGeneral { get; set; }
        public string IntituleCompte { get; set; }
        public string LibelleOperation { get; set; }
        public decimal Debit { get; set; }
        public decimal Credit { get; set; }
        public string CodeAnalytique { get; set; }
        public string Matricule { get; set; }

        public bool EstDebit
        {
            get { return Debit > 0m; }
        }

        public bool EstCredit
        {
            get { return Credit > 0m; }
        }
    }
}
