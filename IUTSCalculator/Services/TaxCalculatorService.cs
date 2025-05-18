using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using IUTSCalculator.Models;

namespace IUTSCalculator.Services
{
    /// <summary>
    /// Service pour le calcul de l'Impôt Unique sur les Traitements et Salaires (IUTS)
    /// selon la législation fiscale du Burkina Faso
    /// </summary>
    public class TaxCalculatorService : IDisposable
    {
        #region Members
        private readonly TaxDataService _taxDataService;
        private bool _isDisposed = false;

        // Event handlers pour les mises à jour des données fiscales
        private EventHandler<TaxDataUpdatedEventArgs> _taxDataUpdatedHandler;
        #endregion

        #region Events
        /// <summary>
        /// Événement déclenché lorsque les paramètres de calcul d'impôt sont mis à jour
        /// </summary>
        public event EventHandler<TaxParametersUpdatedEventArgs> TaxParametersUpdated;
        #endregion

        #region Constructor
        /// <summary>
        /// Constructeur du service de calcul d'impôts
        /// </summary>
        /// <param name="taxDataService">Service de gestion des données fiscales</param>
        public TaxCalculatorService(TaxDataService taxDataService)
        {
            _taxDataService = taxDataService ?? throw new ArgumentNullException(nameof(taxDataService));

            // S'abonner à l'événement de mise à jour des données fiscales
            _taxDataUpdatedHandler = (sender, e) => OnTaxDataUpdated(e);
            _taxDataService.TaxDataUpdated += _taxDataUpdatedHandler;
        }
        #endregion

        #region Public Methods
        /// <summary>
        /// Méthode unifiée pour calculer l'impôt selon différents modes (mensuel, annuel, régularisation)
        /// </summary>
        /// <param name="employee">L'employé pour lequel calculer l'impôt</param>
        /// <param name="isAnnual">Si vrai, calcule l'impôt annuel; sinon calcule l'impôt mensuel</param>
        /// <param name="isRegularization">Si vrai, effectue une régularisation annuelle</param>
        /// <param name="previousTaxPaid">Montant d'impôt déjà payé (pour les régularisations)</param>
        /// <returns>Le résultat du calcul d'impôt</returns>
        public TaxCalculation CalculateTax(Employee employee, bool isAnnual, bool isRegularization, decimal previousTaxPaid = 0)
        {
            if (employee == null)
                throw new ArgumentNullException(nameof(employee));

            if (isRegularization)
            {
                // Pour une régularisation, on simule les calculs mensuels
                var monthlyCalculations = new List<TaxCalculation>
        {
            CalculateMonthlyTax(employee)
        };

                // Si nous voulons simuler 12 mois identiques
                for (int i = 1; i < 12; i++)
                {
                    monthlyCalculations.Add(new TaxCalculation
                    {
                        Employee = employee,
                        CalculationDate = DateTime.Now,
                        SalaireBrut = monthlyCalculations[0].SalaireBrut,
                        BaseIUTSHorsDeduction = monthlyCalculations[0].BaseIUTSHorsDeduction,
                        IUTSNet = previousTaxPaid / 12, // Utiliser l'impôt déjà payé divisé par 12
                        ExemptAmount = monthlyCalculations[0].ExemptAmount,
                        FamilyChargeReduction = monthlyCalculations[0].FamilyChargeReduction
                    });
                }

                // Effectuer la régularisation sur les 12 mois
                return CalculateAnnualRegularization(monthlyCalculations);
            }
            else if (isAnnual)
            {
                return CalculateAnnualTax(employee);
            }
            else
            {
                return CalculateMonthlyTax(employee);
            }
        }

        public TaxCalculation CalculateMonthlyTax(Employee employee)
        {
            if (employee == null)
                throw new ArgumentNullException(nameof(employee));

            var calculation = new TaxCalculation
            {
                Employee = employee,
                CalculationDate = DateTime.Now
            };

            // Étape 1: Déterminer le revenu brut total
            decimal grossIncome = employee.BaseSalary;
            grossIncome += employee.HousingAllowance + employee.FunctionAllowance + employee.TransportAllowance;
            // Ajouter d'autres revenus si nécessaire
            decimal otherAllowances = 0m; // À implémenter dans Employee
            decimal benefitsInKind = 0m;  // À implémenter dans Employee
            grossIncome += otherAllowances + benefitsInKind;

            calculation.SalaireBrut = grossIncome;


            // Étape 2: Calculer la cnss employé et affecter la valeur à penssion contribution
            calculation.CnssEmployee = CalculerCotisationSociale(grossIncome, 800000, 0.055m);
            employee.PensionContribution = calculation.CnssEmployee;

            // Étape 3: Calculer la CNSS Part patronal
            calculation.CnssPartPatronale = CalculerCotisationSociale(grossIncome, 800000, 0.16m);

            // Étape 4: Calculer les franchises d'indemnités (exonérations) selon Article 56
            decimal exemptAmount = CalculateExemptAmount(employee);
            calculation.ExemptAmount = exemptAmount;

            // Étape 5: Déduire les cotisations de retraite (max 8% du salaire de base)
            decimal pensionContribution = Math.Min(employee.PensionContribution,
                                                employee.BaseSalary * _taxDataService.ConstantValues.MaxPensionContributionRate);

            // Pour compatibilité avec TaxCalculation
            decimal professionalDeduction = CalculateProfessionalDeduction(employee);

            // Étape 6: Calculer le revenu imposable net
            decimal taxableIncome = grossIncome - exemptAmount - pensionContribution - professionalDeduction;
            calculation.BaseIUTSHorsDeduction = taxableIncome;

            // Étape 7: Calculer l'impôt brut par tranches
            decimal grossTax = CalculateGrossTax(taxableIncome);

            // Étape 8: Appliquer l'abattement pour charges de famille
            int familyCharges = Math.Min(employee.FamilyCharges, 7); // Maximum 7 charges selon Article 62
            decimal familyChargeRate = _taxDataService.FamilyChargeRates.ContainsKey(familyCharges)
                ? _taxDataService.FamilyChargeRates[familyCharges]
                : 0m;

            decimal familyReduction = grossTax * familyChargeRate;
            calculation.FamilyChargeReduction = familyReduction;

            // Étape 9: Calculer l'impôt net à payer
            decimal netTax = grossTax - familyReduction;
            calculation.IUTSNet = netTax;

            // Calcul du taux moyen d'imposition
            calculation.TaxRate = grossIncome > 0 ? netTax / grossIncome * 100 : 0;

            //calcule de la retenue obligatoire sur les salaires
            calculation.RetenueSpeciale=(grossIncome-netTax - calculation.CnssEmployee) * 0.01m;

            // Calcul la taxe professionnel d'apprentissage (TPA)
            calculation.TaxePatronale = grossIncome* 0.03m;

            // Calculer la masse salarial de l'employé
            calculation.MasseSalariale=grossIncome + calculation.TaxePatronale + calculation.CnssPartPatronale;

            // Générer les détails du calcul
            GenerateCalculationDetails(calculation, exemptAmount, pensionContribution, professionalDeduction, grossTax);


            return calculation;
        }

        /// <summary>
        /// Calcule l'IUTS annuel pour un employé
        /// </summary>
        public TaxCalculation CalculateAnnualTax(Employee employee)
        {
            if (employee == null)
                throw new ArgumentNullException(nameof(employee));

            var monthlyCalculation = CalculateMonthlyTax(employee);

            var annualCalculation = new TaxCalculation
            {
                Employee = employee,
                CalculationDate = DateTime.Now,
                SalaireBrut = monthlyCalculation.SalaireBrut * 12,
                ExemptAmount = monthlyCalculation.ExemptAmount * 12,
                BaseIUTSHorsDeduction = monthlyCalculation.BaseIUTSHorsDeduction * 12,
                IUTSNet = monthlyCalculation.IUTSNet * 12,
                TaxRate = monthlyCalculation.TaxRate,
                FamilyChargeReduction = monthlyCalculation.FamilyChargeReduction * 12
            };

            // Générer les détails du calcul annuel
            var details = new System.Text.StringBuilder();
            details.AppendLine("Calcul de l'IUTS annuel :");
            details.AppendLine($"Basé sur le calcul mensuel multiplié par 12");
            details.AppendLine($"1. Revenu brut annuel : {annualCalculation.SalaireBrut:N0} FCFA");
            details.AppendLine($"2. Revenu imposable annuel : {annualCalculation.BaseIUTSHorsDeduction:N0} FCFA");
            details.AppendLine($"3. Impôt annuel à payer : {annualCalculation.IUTSNet:N0} FCFA");

            annualCalculation.CalculationDetails = details.ToString();

            return annualCalculation;
        }

        /// <summary>
        /// Calcule la régularisation annuelle des impôts
        /// selon l'Article 69 du code des impôts
        /// </summary>
        public TaxCalculation CalculateAnnualRegularization(List<TaxCalculation> monthlyCalculations)
        {
            if (monthlyCalculations == null || !monthlyCalculations.Any())
                throw new ArgumentException("Les calculs mensuels doivent être fournis pour la régularisation");

            var employee = monthlyCalculations.First().Employee;

            // Somme des éléments des calculs mensuels
            decimal totalGrossSalary = monthlyCalculations.Sum(c => c.SalaireBrut);
            decimal totalExemptAmount = monthlyCalculations.Sum(c => c.ExemptAmount);
            decimal totalNetTaxableIncome = monthlyCalculations.Sum(c => c.BaseIUTSHorsDeduction);
            decimal totalTaxPaid = monthlyCalculations.Sum(c => c.IUTSNet);

            // Recalcul de l'impôt annuel de façon correcte
            decimal annualGrossTax = CalculateGrossTax(totalNetTaxableIncome);

            int familyCharges = Math.Min(employee.FamilyCharges, 7);
            decimal familyChargeRate = _taxDataService.FamilyChargeRates.ContainsKey(familyCharges)
                ? _taxDataService.FamilyChargeRates[familyCharges]
                : 0m;

            decimal annualFamilyReduction = annualGrossTax * familyChargeRate;

            decimal annualNetTax = annualGrossTax - annualFamilyReduction;

            // Différence entre ce qui a été payé et ce qui aurait dû être payé
            decimal taxDifference = annualNetTax - totalTaxPaid;

            var regularization = new TaxCalculation
            {
                Employee = employee,
                CalculationDate = DateTime.Now,
                SalaireBrut = totalGrossSalary,
                ExemptAmount = totalExemptAmount,
                BaseIUTSHorsDeduction = totalNetTaxableIncome,
                IUTSNet = annualNetTax,
                FamilyChargeReduction = annualFamilyReduction,
                TaxRate = totalGrossSalary > 0 ? annualNetTax / totalGrossSalary * 100 : 0
            };

            // Générer les détails de la régularisation
            var details = new System.Text.StringBuilder();
            details.AppendLine("Régularisation annuelle de l'IUTS :");
            details.AppendLine($"1. Total des revenus bruts : {totalGrossSalary:N0} FCFA");
            details.AppendLine($"2. Total des revenus imposables : {totalNetTaxableIncome:N0} FCFA");
            details.AppendLine($"3. Impôt brut recalculé : {annualGrossTax:N0} FCFA");
            details.AppendLine($"4. Réduction pour charges familiales : {annualFamilyReduction:N0} FCFA");
            details.AppendLine($"5. Impôt net recalculé : {annualNetTax:N0} FCFA");
            details.AppendLine($"6. Total des impôts déjà payés : {totalTaxPaid:N0} FCFA");
            details.AppendLine($"7. Différence à régulariser : {taxDifference:N0} FCFA");

            if (taxDifference > 0)
                details.AppendLine("   → Complément d'impôt à payer");
            else if (taxDifference < 0)
                details.AppendLine("   → Trop-perçu à rembourser");
            else
                details.AppendLine("   → Aucune régularisation nécessaire");

            regularization.CalculationDetails = details.ToString();

            return regularization;
        }

        /// <summary>
        /// Force le rechargement des données fiscales
        /// </summary>
        public async Task ReloadTaxDataAsync()
        {
            await _taxDataService.ReloadTaxDataAsync();
        }
        #endregion

        #region Private Methods
        /// <summary>
        /// Calcule l'abattement forfaitaire pour frais professionnels
        /// </summary>
        private decimal CalculateProfessionalDeduction(Employee employee)
        {
            bool isHigherCategory = employee.IsHigherCategory;
            decimal professionalDeductionRate = isHigherCategory
                ? _taxDataService.ConstantValues.ProfessionalDeductionHigherCategories
                : _taxDataService.ConstantValues.ProfessionalDeductionOtherCategories;

            return employee.BaseSalary * professionalDeductionRate;
        }

        /// <summary>
        /// Génère les détails du calcul pour affichage
        /// </summary>
        private void GenerateCalculationDetails(
            TaxCalculation calculation,
            decimal exemptAmount,
            decimal pensionContribution,
            decimal professionalDeduction,
            decimal grossTax)
        {
            var details = new System.Text.StringBuilder();

            details.AppendLine("Détails du calcul de l'IUTS :");
            details.AppendLine($"1. Revenu brut total : {calculation.SalaireBrut:N0} FCFA");
            details.AppendLine($"2. Montants exonérés : {exemptAmount:N0} FCFA");
            details.AppendLine($"3. Cotisation retraite : {pensionContribution:N0} FCFA");
            details.AppendLine($"4. Abattement forfaitaire : {professionalDeduction:N0} FCFA");
            details.AppendLine($"5. Revenu imposable : {calculation.BaseIUTSHorsDeduction:N0} FCFA");
            details.AppendLine($"6. Impôt brut : {grossTax:N0} FCFA");
            details.AppendLine($"7. Réduction pour charges familiales ({calculation.Employee.FamilyCharges} charges) : {calculation.FamilyChargeReduction:N0} FCFA");
            details.AppendLine($"8. IUTS net à payer : {calculation.IUTSNet:N0} FCFA");            
            details.AppendLine($"9. Salaire net après impôt : {calculation.NetSalaryAfterTax:N0} FCFA");
            details.AppendLine($"10. Rosalaire : {calculation.RetenueSpeciale:N0} FCFA");
            details.AppendLine($"11. Salaire net à payer : {calculation.SalaireNetaPayer:N0} FCFA");
            details.AppendLine($"12. CNSS Part Patronal  : {calculation.CnssPartPatronale:N0} FCFA");
            details.AppendLine($"13. TPA : {calculation.TaxePatronale:N0} FCFA");
            details.AppendLine($"14. Masse Salarial : {calculation.MasseSalariale:N0} FCFA");

            calculation.CalculationDetails = details.ToString();
        }

        /// <summary>
        /// Calcule le montant des exonérations d'indemnités selon l'Article 56
        /// </summary>
        private decimal CalculateExemptAmount(Employee employee)
        {
            decimal grossSalaryForCalculation = employee.BaseSalary;

            // Si la cotisation de pension est spécifiée, l'utiliser pour le calcul du salaire brut
            if (employee.PensionContribution > 0)
            {
                grossSalaryForCalculation = employee.BaseSalary -
                    Math.Min(employee.PensionContribution,
                         employee.BaseSalary * _taxDataService.ConstantValues.MaxPensionContributionRate);
            }

            // Calculer les montants exonérés pour chaque type d'indemnité
            decimal exemptHousingAllowance = Math.Min(
                employee.HousingAllowance,
                Math.Min(
                    grossSalaryForCalculation * _taxDataService.ConstantValues.MaxHousingAllowanceRate,
                    _taxDataService.ConstantValues.MaxHousingAllowanceAmount)
            );

            decimal exemptFunctionAllowance = Math.Min(
                employee.FunctionAllowance,
                Math.Min(
                    grossSalaryForCalculation * _taxDataService.ConstantValues.MaxFunctionAllowanceRate,
                    _taxDataService.ConstantValues.MaxFunctionAllowanceAmount)
            );

            decimal exemptTransportAllowance = Math.Min(
                employee.TransportAllowance,
                Math.Min(
                    grossSalaryForCalculation * _taxDataService.ConstantValues.MaxTransportAllowanceRate,
                    _taxDataService.ConstantValues.MaxTransportAllowanceAmount)
            );

            // La somme des exonérations
            return exemptHousingAllowance + exemptFunctionAllowance + exemptTransportAllowance;
        }

        /// <summary>
        /// Calcule l'impôt brut par tranches selon le barème fiscal
        /// </summary>
        private decimal CalculateGrossTax(decimal taxableIncome)
        {
            decimal tax = 0m;

            foreach (var bracket in _taxDataService.TaxBrackets)
            {
                // Si le revenu est inférieur à la borne inférieure, sortir de la boucle
                if (taxableIncome < bracket.LowerBound)
                    break;

                // Calculer le montant imposable dans cette tranche
                decimal amountInBracket = Math.Min(
                    taxableIncome - bracket.LowerBound,
                    bracket.UpperBound - bracket.LowerBound
                );

                // Appliquer le taux d'imposition sur le montant de cette tranche
                if (amountInBracket > 0)
                    tax += amountInBracket * bracket.Rate / 100m; // Convertir le taux en pourcentage

                // Si le revenu ne dépasse pas la borne supérieure, sortir de la boucle
                if (taxableIncome <= bracket.UpperBound)
                    break;
            }

            return tax;
        }

        /// <summary>
        /// Calcule la cnss en fonction du salaire brut, d'un plafond et d'un taux.
        /// Si le salaire brut dépasse le plafond, on applique le taux au plafond, sinon au salaire brut.
        /// </summary>
        /// <param name="salaireBrut">Salaire brut de l'employé</param>
        /// <param name="brutPlafond">Plafond à appliquer</param>
        /// <param name="taux">Taux à appliquer (ex: 0.05 pour 5%)</param>
        /// <returns>Montant calculé</returns>
        public decimal CalculerCotisationSociale(decimal salaireBrut, decimal brutPlafond, decimal taux)
        {
            if (salaireBrut > brutPlafond)
                return brutPlafond * taux;
            else
                return salaireBrut * taux;
        }

        /// <summary>
        /// Estime le salaire de base à partir du salaire net et des indemnités.
        /// </summary>
        /// <param name="netSalary">Salaire net à payer</param>
        /// <param name="housingAllowance">Indemnité de logement</param>
        /// <param name="functionAllowance">Indemnité de fonction</param>
        /// <param name="transportAllowance">Indemnité de transport</param>
        /// <param name="employee">Employé (pour charges familiales, catégorie, etc.)</param>
        /// <param name="tolerance">Tolérance d'arrêt (par défaut 100 FCFA)</param>
        /// <param name="maxIterations">Nombre max d'itérations</param>
        /// <returns>Salaire de base estimé</returns>
        public decimal CalculateBaseSalaryFromNet(
    decimal netSalary,
    decimal housingAllowance,
    decimal functionAllowance,
    decimal transportAllowance,
    Employee employee,
    decimal tolerance = 0m,
    int maxIterations = 50)
        {
            if (employee == null)
                throw new ArgumentNullException(nameof(employee));

            // Estimation initiale
            decimal estimatedBase = netSalary;
            decimal step = 1000m;
            decimal lastDifference = decimal.MaxValue;
            int iteration = 0;

            while (iteration < maxIterations)
            {
                // On clone l'employé pour ne pas modifier l'original
                var tempEmployee = new Employee
                {
                    BaseSalary = estimatedBase,
                    HousingAllowance = housingAllowance,
                    FunctionAllowance = functionAllowance,
                    TransportAllowance = transportAllowance,
                    FamilyCharges = employee.FamilyCharges,
                    Category = employee.Category, // Déclenche le calcul de IsHigherCategory
                    IsSpouseWorking = employee.IsSpouseWorking
                };

                var calc = CalculateMonthlyTax(tempEmployee);

                // On considère le salaire net à payer (salaire brut - impôts - cotisations)
                decimal calculatedNet = calc.SalaireNetaPayer > 0 ? calc.SalaireNetaPayer : calc.NetSalaryAfterTax;

                decimal difference = calculatedNet - netSalary;

                if (Math.Abs(difference) <= tolerance)
                    return estimatedBase;

                // Ajustement : si le net calculé est trop élevé, on baisse le brut, sinon on l'augmente
                if (difference > 0)
                    estimatedBase -= step;
                else
                    estimatedBase += step;

                // Réduire le pas si on change de signe (pour accélérer la convergence)
                if (Math.Sign(difference) != Math.Sign(lastDifference))
                    step /= 2;

                lastDifference = difference;
                iteration++;
            }

            // Retourne la dernière estimation si la convergence n'est pas atteinte
            return estimatedBase;
        }



        /// <summary>
        /// Traite les mises à jour des données fiscales
        /// </summary>
        private void OnTaxDataUpdated(TaxDataUpdatedEventArgs e)
        {
            // Notifier les composants qui dépendent de ce service
            TaxParametersUpdated?.Invoke(this, new TaxParametersUpdatedEventArgs
            {
                TaxBracketsUpdated = e.TaxBracketsUpdated,
                TaxConstantsUpdated = e.TaxConstantsUpdated,
                UpdateTime = e.UpdateTime
            });
        }
        #endregion

        #region IDisposable Implementation
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_isDisposed)
            {
                if (disposing)
                {
                    // Se désabonner de l'événement 
                    if (_taxDataService != null && _taxDataUpdatedHandler != null)
                    {
                        _taxDataService.TaxDataUpdated -= _taxDataUpdatedHandler;
                        _taxDataUpdatedHandler = null;
                    }
                }

                _isDisposed = true;
            }
        }
        #endregion
    }



    /// <summary>
    /// Arguments de l'événement de mise à jour des paramètres de calcul d'impôt
    /// </summary>
    public class TaxParametersUpdatedEventArgs : EventArgs
    {
        /// <summary>
        /// Indique si les tranches d'imposition ont été mises à jour
        /// </summary>
        public bool TaxBracketsUpdated { get; set; }

        /// <summary>
        /// Indique si les constantes fiscales ont été mises à jour
        /// </summary>
        public bool TaxConstantsUpdated { get; set; }

        /// <summary>
        /// Date et heure de la mise à jour
        /// </summary>
        public DateTime UpdateTime { get; set; }
    }
}