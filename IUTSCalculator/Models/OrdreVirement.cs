using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace IUTSCalculator.Models
{
    /// <summary>
    /// Représente l'ordre de virement bancaire et l'état des paiements de salaires
    /// pour une période mensuelle donnée (module CPT-02).
    /// </summary>
    public class OrdreVirement
    {
        // ── En-tête et métadonnées ───────────────────────────────────────────

        public string NumeroOrdre { get; set; }
        public DateTime DateEmission { get; set; } = DateTime.Now;
        public DateTime DebutPeriode { get; set; }
        public DateTime FinPeriode { get; set; }

        public string RaisonSociale { get; set; }
        public string Sigle { get; set; }
        public string Ifu { get; set; }
        public string Rccm { get; set; }
        public string Adresse { get; set; }
        public string Ville { get; set; }
        public string Telephone { get; set; }

        /// <summary>Banque émettrice / compte employeur (optionnel).</summary>
        public string BanqueEmettrice { get; set; }
        public string CompteEmetteur { get; set; }

        /// <summary>Banque cible pour laquelle l'ordre est filtré (null ou "Toutes les banques" si global).</summary>
        public string BanqueCible { get; set; }

        /// <summary>Vrai si la période de paie n'est pas encore clôturée.</summary>
        public bool EstBrouillon { get; set; }

        // ── Lignes de paiement ───────────────────────────────────────────────

        public List<LigneOrdreVirement> Lignes { get; set; } = new List<LigneOrdreVirement>();

        // ── Totaux et statistiques ───────────────────────────────────────────

        public decimal TotalNetGeneral
        {
            get { return Lignes.Sum(l => l.MontantNet); }
        }

        public decimal TotalVirements
        {
            get { return Lignes.Where(l => l.EstVirement).Sum(l => l.MontantNet); }
        }

        public decimal TotalCheques
        {
            get { return Lignes.Where(l => l.EstCheque).Sum(l => l.MontantNet); }
        }

        public decimal TotalEspeces
        {
            get { return Lignes.Where(l => l.EstEspeces).Sum(l => l.MontantNet); }
        }

        public decimal TotalMobileMoney
        {
            get { return Lignes.Where(l => l.EstMobileMoney).Sum(l => l.MontantNet); }
        }

        public decimal TotalAutres
        {
            get { return Lignes.Where(l => !l.EstVirement && !l.EstCheque && !l.EstEspeces && !l.EstMobileMoney).Sum(l => l.MontantNet); }
        }

        public int NombreTotalSalaries
        {
            get { return Lignes.Count; }
        }

        public int NombreVirements
        {
            get { return Lignes.Count(l => l.EstVirement); }
        }

        public int NombreCheques
        {
            get { return Lignes.Count(l => l.EstCheque); }
        }

        public int NombreEspeces
        {
            get { return Lignes.Count(l => l.EstEspeces); }
        }

        public int NombreMobileMoney
        {
            get { return Lignes.Count(l => l.EstMobileMoney); }
        }

        public int NombreAnomalies
        {
            get { return Lignes.Count(l => l.AAnomalie); }
        }

        public bool AAnomalies
        {
            get { return NombreAnomalies > 0; }
        }

        /// <summary>Récapitulatif ventilé par mode de règlement.</summary>
        public List<RecapModeReglement> Recapitulatifs
        {
            get
            {
                decimal total = TotalNetGeneral;
                var list = new List<RecapModeReglement>();

                if (NombreVirements > 0)
                {
                    list.Add(new RecapModeReglement
                    {
                        Mode = "Virement bancaire",
                        Nombre = NombreVirements,
                        Montant = TotalVirements,
                        Pourcentage = total > 0m ? Math.Round(TotalVirements / total * 100m, 1) : 0m
                    });
                }

                if (NombreCheques > 0)
                {
                    list.Add(new RecapModeReglement
                    {
                        Mode = "Chèque",
                        Nombre = NombreCheques,
                        Montant = TotalCheques,
                        Pourcentage = total > 0m ? Math.Round(TotalCheques / total * 100m, 1) : 0m
                    });
                }

                if (NombreEspeces > 0)
                {
                    list.Add(new RecapModeReglement
                    {
                        Mode = "Espèces",
                        Nombre = NombreEspeces,
                        Montant = TotalEspeces,
                        Pourcentage = total > 0m ? Math.Round(TotalEspeces / total * 100m, 1) : 0m
                    });
                }

                if (NombreMobileMoney > 0)
                {
                    list.Add(new RecapModeReglement
                    {
                        Mode = "Mobile Money",
                        Nombre = NombreMobileMoney,
                        Montant = TotalMobileMoney,
                        Pourcentage = total > 0m ? Math.Round(TotalMobileMoney / total * 100m, 1) : 0m
                    });
                }

                int nbAutres = Lignes.Count(l => !l.EstVirement && !l.EstCheque && !l.EstEspeces && !l.EstMobileMoney);
                if (nbAutres > 0)
                {
                    list.Add(new RecapModeReglement
                    {
                        Mode = "Autre mode",
                        Nombre = nbAutres,
                        Montant = TotalAutres,
                        Pourcentage = total > 0m ? Math.Round(TotalAutres / total * 100m, 1) : 0m
                    });
                }

                return list;
            }
        }

        // ── Libellés et noms de fichiers ─────────────────────────────────────

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
                string banque = string.IsNullOrWhiteSpace(BanqueCible) || BanqueCible == "Toutes les banques"
                    ? "ToutesBanques"
                    : BanqueCible.Replace(" ", "_");
                return string.Format("Ordre_Virement_{0}_{1}_{2:yyyy_MM}.xlsx", entreprise, banque, FinPeriode);
            }
        }

        public string NomFichierCsv
        {
            get
            {
                string entreprise = (RaisonSociale ?? "Entreprise").Replace(" ", "_");
                string banque = string.IsNullOrWhiteSpace(BanqueCible) || BanqueCible == "Toutes les banques"
                    ? "ToutesBanques"
                    : BanqueCible.Replace(" ", "_");
                return string.Format("Virement_Salaires_{0}_{1}_{2:yyyy_MM}.csv", entreprise, banque, FinPeriode);
            }
        }

        public List<string> Anomalies()
        {
            var liste = new List<string>();

            if (EstBrouillon)
            {
                liste.Add(string.Format(
                    "La période {0} n'est pas encore clôturée : cet état est un brouillon.",
                    PeriodeLibelle));
            }

            if (Lignes.Count == 0)
            {
                liste.Add("Aucun paiement généré : aucun bulletin enregistré sur cette période.");
            }

            foreach (var l in Lignes)
            {
                if (l.AAnomalie)
                {
                    liste.Add(string.Format(
                        "Salarié {0} ({1}) : {2}",
                        l.NomComplet, l.EmployeeId, l.AnomalieLibelle));
                }
            }

            return liste;
        }
    }

    /// <summary>
    /// Une ligne individuelle de l'état des paiements / ordre de virement.
    /// </summary>
    public class LigneOrdreVirement
    {
        public int Ordre { get; set; }
        public string EmployeeId { get; set; }
        public string Nom { get; set; }
        public string Prenom { get; set; }
        public string Fonction { get; set; }
        public string ModeReglement { get; set; }
        public string Banque { get; set; }
        public string NumeroCompte { get; set; }
        public decimal MontantNet { get; set; }

        public string NomComplet
        {
            get { return string.Format("{0} {1}", Nom ?? "", Prenom ?? "").Trim(); }
        }

        public bool EstVirement
        {
            get
            {
                if (string.IsNullOrWhiteSpace(ModeReglement)) return true; // Virement par défaut
                return ModeReglement.IndexOf("virement", StringComparison.OrdinalIgnoreCase) >= 0;
            }
        }

        public bool EstCheque
        {
            get
            {
                if (string.IsNullOrWhiteSpace(ModeReglement)) return false;
                return ModeReglement.IndexOf("chèque", StringComparison.OrdinalIgnoreCase) >= 0 ||
                       ModeReglement.IndexOf("cheque", StringComparison.OrdinalIgnoreCase) >= 0;
            }
        }

        public bool EstEspeces
        {
            get
            {
                if (string.IsNullOrWhiteSpace(ModeReglement)) return false;
                return ModeReglement.IndexOf("espèce", StringComparison.OrdinalIgnoreCase) >= 0 ||
                       ModeReglement.IndexOf("espece", StringComparison.OrdinalIgnoreCase) >= 0 ||
                       ModeReglement.IndexOf("caisse", StringComparison.OrdinalIgnoreCase) >= 0;
            }
        }

        public bool EstMobileMoney
        {
            get
            {
                if (string.IsNullOrWhiteSpace(ModeReglement)) return false;
                return ModeReglement.IndexOf("mobile", StringComparison.OrdinalIgnoreCase) >= 0 ||
                       ModeReglement.IndexOf("orange", StringComparison.OrdinalIgnoreCase) >= 0 ||
                       ModeReglement.IndexOf("moov", StringComparison.OrdinalIgnoreCase) >= 0;
            }
        }

        public bool EstCompteValide
        {
            get
            {
                if (!EstVirement) return true;
                return !string.IsNullOrWhiteSpace(NumeroCompte);
            }
        }

        public bool AAnomalie
        {
            get
            {
                if (EstVirement && string.IsNullOrWhiteSpace(NumeroCompte)) return true;
                if (MontantNet <= 0m) return true;
                return false;
            }
        }

        public string AnomalieLibelle
        {
            get
            {
                if (EstVirement && string.IsNullOrWhiteSpace(NumeroCompte))
                    return "Numéro de compte / RIB manquant pour un virement bancaire";
                if (MontantNet <= 0m)
                    return "Net à payer nul ou négatif";
                return string.Empty;
            }
        }
    }

    /// <summary>
    /// Ligne synthétique du récapitulatif par mode de règlement.
    /// </summary>
    public class RecapModeReglement
    {
        public string Mode { get; set; }
        public int Nombre { get; set; }
        public decimal Montant { get; set; }
        public decimal Pourcentage { get; set; }
    }
}
