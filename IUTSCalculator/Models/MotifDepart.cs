using System.ComponentModel;

namespace IUTSCalculator.Models
{
    /// <summary>
    /// Motifs de rupture ou de cessation du contrat de travail en droit burkinabè.
    /// </summary>
    public enum MotifDepart
    {
        [Description("Démission")]
        Demission = 0,

        [Description("Fin de contrat CDD")]
        FinContrat = 1,

        [Description("Licenciement pour motif personnel")]
        LicenciementPersonnel = 2,

        [Description("Licenciement pour motif économique")]
        LicenciementEconomique = 3,

        [Description("Licenciement pour faute lourde")]
        LicenciementFauteLourde = 4,

        [Description("Départ volontaire à la retraite")]
        RetraiteDepart = 5,

        [Description("Mise à la retraite par l'employeur")]
        RetraiteMise = 6,

        [Description("Rupture d'un commun accord")]
        RuptureConventionnelle = 7,

        [Description("Décès du travailleur")]
        Deces = 8,

        [Description("Autre motif")]
        Autre = 9
    }

    /// <summary>
    /// Statut de l'exécution du préavis légal ou conventionnel.
    /// </summary>
    public enum StatutPreavis
    {
        [Description("Préavis effectué (travaillé)")]
        Effectue = 0,

        [Description("Dispensé / Non effectué (indemnité compensatrice due)")]
        DispensePaye = 1,

        [Description("Non applicable / Faute lourde")]
        Inapplicable = 2
    }

    /// <summary>
    /// Statut d'activité du salarié dans l'entreprise.
    /// </summary>
    public enum StatutEmploye
    {
        [Description("Actif")]
        Actif = 0,

        [Description("Sorti (Fin de contrat)")]
        Sorti = 1,

        [Description("Suspendu")]
        Suspendu = 2
    }
}
