using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;

namespace IUTSCalculator.Models
{
    /// <summary>
    /// Une ligne de la déclaration IUTS/TPA : un salarié, son assiette et l'impôt qui a
    /// été retenu sur son bulletin.
    ///
    /// Identifiée par le matricule interne et le nom, pas par un identifiant fiscal
    /// personnel : au Burkina Faso, un salarié n'a pas de numéro IFU propre — l'IFU
    /// identifie une entreprise ou un contribuable indépendant, pas un employé. Seul le
    /// dossier (l'employeur) porte un IFU, vérifié au niveau de la déclaration.
    ///
    /// La taxe patronale et d'apprentissage n'y figure volontairement pas en colonne :
    /// c'est une charge de l'employeur, pas une retenue individuelle, et le client a
    /// choisi de ne la montrer qu'en un seul total sur le bordereau (module DEC-03).
    /// L'assiette TPA de la ligne est tout de même conservée en interne — elle sert au
    /// total de l'entreprise, salarié par salarié, plutôt qu'un brut global qui ignorerait
    /// les exonérations DEC-02 propres à certaines rubriques.
    /// </summary>
    public class LigneDeclarationIutsTpa
    {
        public string Matricule { get; set; }
        public string Nom { get; set; }
        public string Prenom { get; set; }

        /// <summary>Salaire brut du mois.</summary>
        public decimal SalaireBrut { get; set; }

        /// <summary>Base imposable à l'IUTS, après déductions (Art.112 CGI).</summary>
        public decimal BaseImposable { get; set; }

        /// <summary>IUTS retenu sur le bulletin — la valeur qui fait foi.</summary>
        public decimal IUTSRetenu { get; set; }

        /// <summary>Assiette TPA du salarié (interne : sert au total, non affichée en colonne).</summary>
        public decimal AssietteTPA { get; set; }

        /// <summary>TPA du salarié (interne : sert au total, non affichée en colonne).</summary>
        public decimal TaxePatronale { get; set; }

        public string NomComplet
        {
            get { return string.Format("{0} {1}", Nom, Prenom).Trim(); }
        }
    }

    /// <summary>
    /// La déclaration mensuelle IUTS/TPA (module DEC-03).
    ///
    /// Comme la déclaration CNSS (DEC-04), elle se construit à partir de trois éléments
    /// déjà en place — les périodes de paie (PAI-01), le barème en vigueur (SOC-02) et les
    /// bulletins enregistrés — et ne recopie rien à la main. L'IUTS retenu et la TPA
    /// affichés sont ceux du bulletin, pas une valeur recalculée : c'est ce que l'employeur
    /// a réellement prélevé et doit reverser.
    ///
    /// Seule la TPA se prête à une comparaison avec le barème : c'est un taux unique sur
    /// une assiette (assiette × taux), exactement comme les cotisations CNSS. L'IUTS, lui,
    /// dépend d'un barème par tranches et de la situation propre à chaque salarié
    /// (charges de famille, exonérations) ; le reconstituer ici referait tout le moteur de
    /// paie sans ses données d'entrée. La déclaration ne recalcule donc pas l'IUTS — elle
    /// le rapporte tel qu'il a été retenu, et c'est la bonne réponse : c'est le bulletin
    /// remis au salarié qui fait foi.
    ///
    /// Rythme légal : mensuel, sans seuil d'effectif — à la différence de la CNSS.
    /// </summary>
    public class DeclarationIutsTpa : INotifyPropertyChanged
    {
        // ── Employeur ────────────────────────────────────────────────────────

        public string RaisonSociale { get; set; }
        public string Ifu { get; set; }
        public string RegimeFiscal { get; set; }
        public string Adresse { get; set; }
        public string Ville { get; set; }
        public string Telephone { get; set; }

        // ── Période déclarée ─────────────────────────────────────────────────

        public DateTime DebutPeriode { get; set; }
        public DateTime FinPeriode { get; set; }

        /// <summary>Date limite légale de dépôt et de versement.</summary>
        public DateTime DateEcheance { get; set; }

        /// <summary>Période de paie couverte, pour connaître son état de clôture.</summary>
        public PeriodePaie PeriodeCouverte { get; set; }

        /// <summary>Barème appliqué à la période déclarée.</summary>
        public BaremeLegal Bareme { get; set; }

        // ── Lignes ───────────────────────────────────────────────────────────

        public ObservableCollection<LigneDeclarationIutsTpa> Lignes { get; private set; }

        public DeclarationIutsTpa()
        {
            Lignes = new ObservableCollection<LigneDeclarationIutsTpa>();
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

        public decimal TotalBaseImposable
        {
            get { return Lignes.Sum(l => l.BaseImposable); }
        }

        /// <summary>Total de l'IUTS retenu sur le mois — à reverser à la DGI.</summary>
        public decimal TotalIUTSRetenu
        {
            get { return Lignes.Sum(l => l.IUTSRetenu); }
        }

        /// <summary>Assiette TPA de l'entreprise : somme des assiettes individuelles.</summary>
        public decimal TotalAssietteTPA
        {
            get { return Lignes.Sum(l => l.AssietteTPA); }
        }

        /// <summary>TPA telle que retenue sur les bulletins — la valeur qui fait foi.</summary>
        public decimal TotalTaxePatronale
        {
            get { return Lignes.Sum(l => l.TaxePatronale); }
        }

        /// <summary>TPA recalculée depuis l'assiette et le taux du barème de la période.</summary>
        public decimal TotalTaxePatronaleAttendue
        {
            get
            {
                if (Bareme == null) return 0m;
                return decimal.Round(TotalAssietteTPA * Bareme.TauxTaxePatronaleApprentissage, 0, MidpointRounding.AwayFromZero);
            }
        }

        /// <summary>Écart entre la TPA retenue et celle qu'impose le barème de la période.</summary>
        public decimal EcartTPA
        {
            get { return TotalTaxePatronale - TotalTaxePatronaleAttendue; }
        }

        public bool EcartTPASignificatif
        {
            get { return Math.Abs(EcartTPA) > 1m; }
        }

        /// <summary>Total à verser à la DGI : IUTS retenu + TPA, sur le même bordereau mensuel.</summary>
        public decimal TotalAVerser
        {
            get { return TotalIUTSRetenu + TotalTaxePatronale; }
        }

        // ── Contrôles ────────────────────────────────────────────────────────

        /// <summary>
        /// Anomalies bloquantes ou à vérifier avant dépôt, dans l'ordre où elles comptent.
        /// </summary>
        public List<string> Anomalies()
        {
            var anomalies = new List<string>();

            if (string.IsNullOrWhiteSpace(Ifu))
            {
                anomalies.Add(
                    "L'IFU du dossier n'est pas renseigné : la DGI rejette une déclaration " +
                    "qui ne le porte pas.");
            }

            if (Lignes.Count == 0)
            {
                anomalies.Add("Aucun bulletin sur la période : il n'y a rien à déclarer.");
                return anomalies;
            }

            if (EcartTPASignificatif)
            {
                anomalies.Add(string.Format(
                    "La TPA retenue diffère de {0:N0} FCFA par rapport au barème de la " +
                    "période. Un bulletin calculé avant un changement de taux en est la " +
                    "cause la plus fréquente ; vérifiez avant d'envoyer.", EcartTPA));
            }

            if (PeriodeCouverte != null && PeriodeCouverte.EstOuverte)
            {
                anomalies.Add(string.Format(
                    "Période non clôturée : {0}. Une déclaration produite sur un mois " +
                    "encore ouvert peut ne plus correspondre aux bulletins de demain.",
                    PeriodeCouverte.Designation));
            }

            if (Bareme == null)
            {
                anomalies.Add(
                    "Aucun barème n'a été trouvé pour cette période : le taux de TPA n'a pas " +
                    "pu être vérifié.");
            }

            return anomalies;
        }

        // ── Libellés ─────────────────────────────────────────────────────────

        public string PeriodeLibelle
        {
            get
            {
                return PeriodeCouverte != null
                    ? PeriodeCouverte.Designation
                    : string.Format("{0:MMMM yyyy}", DebutPeriode);
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

                return string.Format("IUTS-TPA_{0}_{1:yyyy-MM}.xlsx", societe, DebutPeriode);
            }
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
