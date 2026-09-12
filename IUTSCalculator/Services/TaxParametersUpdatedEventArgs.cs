using System;

namespace IUTSCalculator.Models
{
    /// <summary>
    /// Arguments pour l'événement de mise à jour des paramètres fiscaux.
    /// </summary>
    public class TaxParametersUpdatedEventArgs : EventArgs
    {
        /// <summary>
        /// Nouveau taux d'imposition.
        /// </summary>
        public decimal NewTaxRate { get; }

        /// <summary>
        /// Constructeur pour initialiser les arguments de l'événement.
        /// </summary>
        /// <param name="newTaxRate">Le nouveau taux d'imposition.</param>
        public TaxParametersUpdatedEventArgs(decimal newTaxRate)
        {
            NewTaxRate = newTaxRate;
        }
    }
}
