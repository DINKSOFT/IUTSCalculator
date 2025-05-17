using System;

namespace IUTSCalculator.Models
{
    /// <summary>
    /// Représente une tranche d'imposition pour le calcul de l'IUTS
    /// </summary>
    public class TaxBracket
    {
        /// <summary>
        /// Limite inférieure de la tranche d'imposition (incluse)
        /// </summary>
        public decimal LowerBound { get; set; }

        /// <summary>
        /// Limite supérieure de la tranche d'imposition (incluse)
        /// </summary>
        public decimal UpperBound { get; set; }

        /// <summary>
        /// Taux d'imposition pour cette tranche en pourcentage
        /// </summary>
        public decimal Rate { get; set; }

        /// <summary>
        /// Constructeur par défaut
        /// </summary>
        public TaxBracket()
        {
        }

        /// <summary>
        /// Constructeur avec paramètres
        /// </summary>
        /// <param name="lowerBound">Limite inférieure de la tranche</param>
        /// <param name="upperBound">Limite supérieure de la tranche</param>
        /// <param name="rate">Taux d'imposition</param>
        public TaxBracket(decimal lowerBound, decimal upperBound, decimal rate)
        {
            LowerBound = lowerBound;
            UpperBound = upperBound;
            Rate = rate;
        }

        /// <summary>
        /// Vérifie si un montant donné est dans cette tranche
        /// </summary>
        /// <param name="amount">Montant à vérifier</param>
        /// <returns>True si le montant est dans cette tranche</returns>
        public bool IsInBracket(decimal amount)
        {
            return amount >= LowerBound && (amount <= UpperBound || UpperBound == decimal.MaxValue);
        }

        /// <summary>
        /// Calcule l'impôt pour cette tranche
        /// </summary>
        /// <param name="amount">Montant total</param>
        /// <returns>Montant d'impôt pour cette tranche</returns>
        public decimal CalculateTaxForBracket(decimal amount)
        {
            if (amount <= LowerBound)
                return 0;

            decimal taxableInBracket = Math.Min(amount, UpperBound) - LowerBound;
            return taxableInBracket * (Rate / 100m);
        }

        /// <summary>
        /// Retourne une représentation textuelle de la tranche d'imposition
        /// </summary>
        /// <returns>Représentation textuelle</returns>
        public override string ToString()
        {
            string upperBoundStr = UpperBound == decimal.MaxValue ? "et au-dessus" : UpperBound.ToString("N0");
            return $"{LowerBound:N0} à {upperBoundStr} FCFA : {Rate}%";
        }
    }
}