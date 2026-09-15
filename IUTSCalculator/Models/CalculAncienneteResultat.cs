using System;

namespace IUTSCalculator.Models
{
    /// <summary>
    /// Résultat du calcul de l'ancienneté et de la prime conventionnelle pour un salarié (module SOC-05).
    /// </summary>
    public class CalculAncienneteResultat
    {
        public static readonly CalculAncienneteResultat Vide = new CalculAncienneteResultat();

        public int Annees { get; set; }
        public int Mois { get; set; }
        public decimal TauxPourcentage { get; set; }
        public decimal BaseCalcul { get; set; }
        public decimal Montant { get; set; }
        public string LibelleTranche { get; set; }
        public string BaseUtilisee { get; set; }

        public decimal SalaireMinimumConventionnel { get; set; }
        public decimal SalaireContractuel { get; set; }

        /// <summary>
        /// Vrai si le salarié a droit à une prime d'ancienneté (taux > 0 et montant > 0).
        /// </summary>
        public bool EstEligible => Montant > 0m && TauxPourcentage > 0m;

        /// <summary>
        /// Vrai si le salaire contractuel du salarié est inférieur au salaire minimum conventionnel garanti.
        /// </summary>
        public bool EstSousLeMinimum => SalaireMinimumConventionnel > 0m && SalaireContractuel < SalaireMinimumConventionnel;

        /// <summary>
        /// Écart financier en FCFA à combler si le salaire est inférieur au minimum conventionnel.
        /// </summary>
        public decimal EcartMinimum => EstSousLeMinimum ? (SalaireMinimumConventionnel - SalaireContractuel) : 0m;

        public string DescriptionAnciennete => Annees > 0
            ? $"{Annees} an(s) et {Mois} mois"
            : $"{Mois} mois";

        public override string ToString()
        {
            return EstEligible
                ? $"{DescriptionAnciennete} → {TauxPourcentage:0.##}% sur {BaseUtilisee} ({BaseCalcul:N0} FCFA) = {Montant:N0} FCFA"
                : $"{DescriptionAnciennete} → Non éligible à la prime d'ancienneté";
        }
    }
}
