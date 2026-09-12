using System;

namespace IUTSCalculator.Models
{
    /// <summary>
    /// Représente un bulletin de salaire mensuel pour un employé.
    /// </summary>
    public class PayslipMonthly
    {
        public string PayslipId { get; set; }
        public Employee Employee { get; set; }
        public DateTime Period { get; set; } // Mois du bulletin

        // Rubriques principales
        public decimal BaseSalary { get; set; }
        public decimal HousingAllowance { get; set; }
        public decimal FunctionAllowance { get; set; }
        public decimal TransportAllowance { get; set; }
        public decimal GrossSalary { get; set; }

        // Cotisations et taxes
        public decimal CnssEmployee { get; set; }
        public decimal CnssEmployer { get; set; }
        public decimal ProfessionalTax { get; set; }
        public decimal SpecialDeduction { get; set; }

        // Impôt
        public decimal ExemptAmount { get; set; }
        public decimal ProfessionalDeduction { get; set; }
        public decimal NetTaxableIncome { get; set; }
        public decimal IUTS { get; set; }
        public decimal FamilyChargeReduction { get; set; }

        // Résultat
        public decimal NetSalaryAfterTax { get; set; }
        public decimal NetToPay { get; set; }

        // Détail ou commentaire
        public string PayslipDetails { get; set; }
    }
}