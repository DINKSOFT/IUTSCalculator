using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;

namespace IUTSCalculator.Models
{
    /// <summary>Rythme de déclaration imposé à l'employeur.</summary>
    public enum PeriodiciteDeclaration
    {
        /// <summary>20 salariés et plus : déclaration chaque mois, dans les 15 jours.</summary>
        Mensuelle = 0,
        /// <summary>Moins de 20 salariés : déclaration chaque trimestre, dans les 30 jours.</summary>
        Trimestrielle = 1
    }

    /// <summary>
    /// Une ligne de la déclaration CNSS : un salarié, son assiette et ses cotisations.
    /// </summary>
    public class LigneDeclarationCnss
    {
        public string NumeroCnss { get; set; }
        public string Matricule { get; set; }
        public string Nom { get; set; }
        public string Prenom { get; set; }

        /// <summary>Salaire brut du mois, avant plafonnement.</summary>
        public decimal SalaireBrut { get; set; }

        /// <summary>Assiette réellement cotisable : le brut, ramené au plafond s'il le dépasse.</summary>
        public decimal AssiettePlafonnee { get; set; }

        /// <summary>Part salariale telle qu'elle a été retenue sur le bulletin.</summary>
        public decimal CotisationSalariale { get; set; }

        /// <summary>Part patronale telle qu'elle a été calculée sur le bulletin.</summary>
        public decimal CotisationPatronale { get; set; }

        /// <summary>Part salariale recalculée depuis l'assiette et le barème de la période.</summary>
        public decimal CotisationSalarialeAttendue { get; set; }

        /// <summary>Part patronale recalculée depuis l'assiette et le barème de la période.</summary>
        public decimal CotisationPatronaleAttendue { get; set; }

        public string NomComplet
        {
            get { return string.Format("{0} {1}", Nom, Prenom).Trim(); }
        }

        public decimal CotisationTotale
        {
            get { return CotisationSalariale + CotisationPatronale; }
        }

        /// <summary>Vrai lorsque l'assiette a été ramenée au plafond.</summary>
        public bool EstPlafonnee
        {
            get { return AssiettePlafonnee < SalaireBrut; }
        }

        /// <summary>Le matricule CNSS manque : la Caisse ne peut pas rattacher la ligne.</summary>
        public bool MatriculeManquant
        {
            get { return string.IsNullOrWhiteSpace(NumeroCnss); }
        }

        /// <summary>
        /// Écart entre la cotisation du bulletin et celle qu'impose le barème de la
        /// période, parts réunies. Une tolérance d'un franc absorbe les arrondis.
        /// </summary>
        public decimal Ecart
        {
            get
            {
                return (CotisationSalariale + CotisationPatronale)
                     - (CotisationSalarialeAttendue + CotisationPatronaleAttendue);
            }
        }

        public bool EcartSignificatif
        {
            get { return Math.Abs(Ecart) > 1m; }
        }

        /// <summary>Ce qui, sur cette ligne, demande une vérification avant l'envoi.</summary>
        public string Anomalie
        {
            get
            {
                if (MatriculeManquant && EcartSignificatif)
                    return "Matricule absent ; cotisation différente du barème";
                if (MatriculeManquant)
                    return "Matricule CNSS absent";
                if (EcartSignificatif)
                    return string.Format("Cotisation différente du barème ({0:N0} FCFA)", Ecart);
                return "";
            }
        }

        public bool AUneAnomalie
        {
            get { return MatriculeManquant || EcartSignificatif; }
        }
    }

    /// <summary>
    /// Une déclaration de cotisations sociales CNSS (module DEC-04).
    ///
    /// Elle se construit à partir de trois éléments déjà en place : une ou plusieurs
    /// PÉRIODES de paie (PAI-01), le BARÈME en vigueur à cette période (SOC-02) et les
    /// bulletins enregistrés. Rien n'est ressaisi — c'est tout l'intérêt : une déclaration
    /// recopiée à la main depuis un état de paie est une déclaration qui finit par
    /// s'écarter de la paie.
    ///
    /// La déclaration ne recalcule pas les bulletins. Elle prend les cotisations telles
    /// qu'elles ont été retenues, et les COMPARE à ce que le barème de la période impose.
    /// Un écart n'est pas corrigé en silence : il est signalé, ligne par ligne. C'est
    /// exactement le contrôle que fait la Caisse.
    ///
    /// Rythme légal : mensuel à partir de 20 salariés, trimestriel en dessous ; l'échéance
    /// tombe 15 jours après le mois pour le premier cas, 30 jours après le trimestre pour
    /// le second.
    /// </summary>
    public class DeclarationCnss : INotifyPropertyChanged
    {
        /// <summary>Seuil légal de bascule entre déclaration mensuelle et trimestrielle.</summary>
        public const int SeuilEffectifMensuel = 20;

        // ── Employeur ────────────────────────────────────────────────────────

        public string RaisonSociale { get; set; }
        public string NumeroEmployeurCnss { get; set; }
        public string Ifu { get; set; }
        public string Adresse { get; set; }
        public string Ville { get; set; }
        public string Telephone { get; set; }

        // ── Période déclarée ─────────────────────────────────────────────────

        public PeriodiciteDeclaration Periodicite { get; set; }

        /// <summary>Périodes de paie couvertes : une seule au mois, jusqu'à trois au trimestre.</summary>
        public ObservableCollection<PeriodePaie> PeriodesCouvertes { get; private set; }

        public DateTime DebutPeriode { get; set; }
        public DateTime FinPeriode { get; set; }

        /// <summary>Date limite légale de dépôt.</summary>
        public DateTime DateEcheance { get; set; }

        /// <summary>Barème appliqué à la période déclarée.</summary>
        public BaremeLegal Bareme { get; set; }

        // ── Lignes ───────────────────────────────────────────────────────────

        public ObservableCollection<LigneDeclarationCnss> Lignes { get; private set; }

        public DeclarationCnss()
        {
            Lignes = new ObservableCollection<LigneDeclarationCnss>();
            PeriodesCouvertes = new ObservableCollection<PeriodePaie>();
            DateGeneration = DateTime.Now;
        }

        public DateTime DateGeneration { get; set; }

        // ── Totaux ───────────────────────────────────────────────────────────

        public int Effectif
        {
            get { return Lignes.Select(l => l.Matricule).Distinct().Count(); }
        }

        public decimal TotalSalaireBrut
        {
            get { return Lignes.Sum(l => l.SalaireBrut); }
        }

        /// <summary>Assiette déclarée : somme des bruts plafonnés individuellement.</summary>
        public decimal TotalAssiette
        {
            get { return Lignes.Sum(l => l.AssiettePlafonnee); }
        }

        public decimal TotalCotisationSalariale
        {
            get { return Lignes.Sum(l => l.CotisationSalariale); }
        }

        public decimal TotalCotisationPatronale
        {
            get { return Lignes.Sum(l => l.CotisationPatronale); }
        }

        public decimal TotalCotisations
        {
            get { return TotalCotisationSalariale + TotalCotisationPatronale; }
        }

        // ── Ventilation patronale par branche ────────────────────────────────
        //
        // La Caisse attend le détail par branche, pas seulement le total : c'est ce qui
        // permet de vérifier la répartition 6 % / 1,5 % / 8,5 %.

        public decimal PrestationsFamiliales
        {
            get { return Bareme == null ? 0m : Arrondir(TotalAssiette * Bareme.TauxCnssPrestationsFamiliales); }
        }

        public decimal RisquesProfessionnels
        {
            get { return Bareme == null ? 0m : Arrondir(TotalAssiette * Bareme.TauxCnssRisquesProfessionnels); }
        }

        public decimal PensionsPatronal
        {
            get { return Bareme == null ? 0m : Arrondir(TotalAssiette * Bareme.TauxCnssPensionsPatronal); }
        }

        public decimal PensionsSalarial
        {
            get { return Bareme == null ? 0m : Arrondir(TotalAssiette * Bareme.TauxCnssSalarie); }
        }

        /// <summary>Total des cotisations recalculé depuis l'assiette et le barème.</summary>
        public decimal TotalCotisationsAttendu
        {
            get { return PrestationsFamiliales + RisquesProfessionnels + PensionsPatronal + PensionsSalarial; }
        }

        /// <summary>
        /// Écart global entre ce que les bulletins ont retenu et ce que le barème impose.
        /// C'est le chiffre à regarder avant d'envoyer : s'il n'est pas nul, la déclaration
        /// et la paie ne racontent pas la même histoire.
        /// </summary>
        public decimal EcartGlobal
        {
            get { return TotalCotisations - TotalCotisationsAttendu; }
        }

        public bool EcartGlobalSignificatif
        {
            get { return Math.Abs(EcartGlobal) > Lignes.Count + 1m; }
        }

        // ── Contrôles ────────────────────────────────────────────────────────

        public int NombreMatriculesManquants
        {
            get { return Lignes.Count(l => l.MatriculeManquant); }
        }

        public int NombreLignesEnEcart
        {
            get { return Lignes.Count(l => l.EcartSignificatif); }
        }

        public int NombreLignesPlafonnees
        {
            get { return Lignes.Count(l => l.EstPlafonnee); }
        }

        /// <summary>
        /// Anomalies bloquantes ou à vérifier avant dépôt, dans l'ordre où elles comptent.
        /// </summary>
        public List<string> Anomalies()
        {
            var anomalies = new List<string>();

            if (string.IsNullOrWhiteSpace(NumeroEmployeurCnss))
            {
                anomalies.Add(
                    "Le numéro d'employeur CNSS n'est pas renseigné sur le dossier : la Caisse " +
                    "rejette une déclaration qui ne le porte pas.");
            }

            if (Lignes.Count == 0)
            {
                anomalies.Add("Aucun bulletin sur la période : il n'y a rien à déclarer.");
                return anomalies;
            }

            if (NombreMatriculesManquants > 0)
            {
                anomalies.Add(string.Format(
                    "{0} salarié(s) sans matricule CNSS : ces lignes ne pourront pas être " +
                    "rattachées à un assuré.", NombreMatriculesManquants));
            }

            if (NombreLignesEnEcart > 0)
            {
                anomalies.Add(string.Format(
                    "{0} ligne(s) dont la cotisation retenue diffère du barème de la période. " +
                    "Un bulletin calculé avant un changement de taux en est la cause la plus " +
                    "fréquente ; vérifiez avant d'envoyer.", NombreLignesEnEcart));
            }

            var ouvertes = PeriodesCouvertes.Where(p => p.EstOuverte).ToList();
            if (ouvertes.Count > 0)
            {
                anomalies.Add(string.Format(
                    "Période(s) non clôturée(s) : {0}. Une déclaration produite sur un mois " +
                    "encore ouvert peut ne plus correspondre aux bulletins de demain.",
                    string.Join(", ", ouvertes.Select(p => p.Designation))));
            }

            if (Bareme == null)
            {
                anomalies.Add(
                    "Aucun barème n'a été trouvé pour cette période : les cotisations attendues " +
                    "n'ont pas pu être vérifiées.");
            }

            return anomalies;
        }

        // ── Libellés ─────────────────────────────────────────────────────────

        public string PeriodiciteLibelle
        {
            get { return Periodicite == PeriodiciteDeclaration.Mensuelle ? "Mensuelle" : "Trimestrielle"; }
        }

        public string PeriodeLibelle
        {
            get
            {
                if (PeriodesCouvertes.Count == 1)
                    return PeriodesCouvertes[0].Designation;

                if (PeriodesCouvertes.Count > 1)
                {
                    var ordonnees = PeriodesCouvertes.OrderBy(p => p.Annee).ThenBy(p => p.Mois).ToList();
                    return string.Format("{0} à {1}",
                        ordonnees[0].Designation, ordonnees[ordonnees.Count - 1].Designation);
                }

                return string.Format("{0:dd/MM/yyyy} – {1:dd/MM/yyyy}", DebutPeriode, FinPeriode);
            }
        }

        public string EcheanceLibelle
        {
            get
            {
                int jours = (DateEcheance.Date - DateTime.Today).Days;

                if (jours < 0)
                    return string.Format("Échéance dépassée depuis le {0:dd/MM/yyyy}", DateEcheance);
                if (jours == 0)
                    return "Échéance aujourd'hui";

                return string.Format("À déposer avant le {0:dd/MM/yyyy} — {1} jour(s)", DateEcheance, jours);
            }
        }

        public bool EcheanceDepassee
        {
            get { return DateEcheance.Date < DateTime.Today; }
        }

        /// <summary>Nom de fichier proposé pour l'export.</summary>
        public string NomFichierPropose
        {
            get
            {
                string societe = (RaisonSociale ?? "Declaration")
                    .Replace(" ", "-").Replace("/", "-").Replace("\\", "-");

                return string.Format("CNSS_{0}_{1:yyyy-MM}_{2:yyyy-MM}.xlsx",
                    societe, DebutPeriode, FinPeriode);
            }
        }

        private static decimal Arrondir(decimal montant)
        {
            return decimal.Round(montant, 0, MidpointRounding.AwayFromZero);
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            var handler = PropertyChanged;
            if (handler != null)
                handler(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
