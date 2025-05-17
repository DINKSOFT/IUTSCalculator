using System;

namespace IUTSCalculator.Models
{
    /// <summary>
    /// Représente un abattement pour charge de famille
    /// </summary>
    public class FamilyCharge
    {
        /// <summary>
        /// Nombre de charges
        /// </summary>
        public int ChargeCount { get; set; }

        /// <summary>
        /// Pourcentage d'abattement correspondant
        /// </summary>
        public decimal AbatementPercentage { get; set; }

        /// <summary>
        /// Constructeur par défaut
        /// </summary>
        public FamilyCharge()
        {
        }

        /// <summary>
        /// Constructeur avec paramètres
        /// </summary>
        /// <param name="chargeCount">Nombre de charges</param>
        /// <param name="abatementPercentage">Pourcentage d'abattement</param>
        public FamilyCharge(int chargeCount, decimal abatementPercentage)
        {
            ChargeCount = chargeCount;
            AbatementPercentage = abatementPercentage;
        }

        /// <summary>
        /// Retourne une représentation textuelle de la charge familiale
        /// </summary>
        /// <returns>Représentation textuelle</returns>
        public override string ToString()
        {
            return $"{ChargeCount} charge(s) : {AbatementPercentage}% d'abattement";
        }
    }
}