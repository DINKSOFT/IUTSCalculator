using System;
using System.Collections.Generic;
using System.Linq;
using IUTSCalculator.Models;

namespace IUTSCalculator.Services
{
    /// <summary>
    /// Service pour le calcul de l'Impôt Unique sur les Traitements et Salaires (IUTS)
    /// selon la législation fiscale du Burkina Faso
    /// </summary>
    public class TaxCalculatorService : IDisposable
    {
        private readonly TaxDataService _taxDataService;
        private bool _isDisposed = false;
        private EventHandler<TaxDataUpdatedEventArgs> _taxDataUpdatedHandler;

        public event EventHandler<TaxParametersUpdatedEventArgs> TaxParametersUpdated;

        public TaxCalculatorService(TaxDataService taxDataService)
        {
            _taxDataService = taxDataService ?? throw new ArgumentNullException(nameof(taxDataService));
            _taxDataUpdatedHandler = (sender, e) => OnTaxDataUpdated(e);
            _taxDataService.TaxDataUpdated += _taxDataUpdatedHandler;
        }

        /// <summary>
        /// Estime le salaire de base (et les indemnités associées) nécessaire
        /// pour obtenir <paramref name="netSalary"/> de « salaire net à payer »
        /// après application de <see cref="CalculateMonthlyTax"/>.
        /// La précision visée est ±0 FCFA.
        /// CONTRAINTE : Toutes les indemnités sont arrondies au millier supérieur.
        /// </summary>
        /// <param name="netSalary">Salaire net voulu (FCFA)</param>
        /// <param name="employee">Employé servant de référence (catégorie, charges…)</param>
        /// <returns>
        /// Tuple (BaseSalary, HousingAllowance, FunctionAllowance, TransportAllowance, IUTSNet)
        /// </returns>
        public (decimal baseSalary,
                decimal housing,
                decimal function,
                decimal transport,
                decimal iuts)
        EstimateBaseSalaryWithConstraints(decimal netSalary, Employee employee)
        {
            if (employee == null) throw new ArgumentNullException(nameof(employee));
            if (netSalary <= 0) throw new ArgumentOutOfRangeException(nameof(netSalary));

            // ────────────────── 1. Récupération des constantes métier ──────────────────
            var cst = _taxDataService.ConstantValues;
            decimal maxHousingRate = cst.MaxHousingAllowanceRate;
            decimal maxHousingAmount = cst.MaxHousingAllowanceAmount;
            decimal maxFunctionRate = cst.MaxFunctionAllowanceRate;
            decimal maxFunctionAmount = cst.MaxFunctionAllowanceAmount;
            decimal maxTransportRate = cst.MaxTransportAllowanceRate;
            decimal maxTransportAmount = cst.MaxTransportAllowanceAmount;

            // ────────────────── 2. Fonction d'arrondi au millier supérieur ─────────────
            decimal RoundUpToThousand(decimal value)
            {
                if (value <= 0) return 0;
                return Math.Ceiling(value / 1000m) * 1000m;
            }

            // ────────────────── 3. Fonction locale d'évaluation ─────────────────────────
            (decimal netToPay,
             decimal housing,
             decimal function,
             decimal transport,
             decimal iuts) Evaluate(decimal baseSalary)
            {
                // a. Calcul des indemnités plafonnées PUIS arrondies au millier supérieur
                decimal housingRaw = Math.Min(baseSalary * maxHousingRate, maxHousingAmount);
                decimal functionRaw = Math.Min(baseSalary * maxFunctionRate, maxFunctionAmount);
                decimal transportRaw = Math.Min(baseSalary * maxTransportRate, maxTransportAmount);

                decimal housingA = RoundUpToThousand(housingRaw);
                decimal functionA = RoundUpToThousand(functionRaw);
                decimal transportA = RoundUpToThousand(transportRaw);

                // b. Construction d'un employé « test »
                // IMPORTANT : FamilyCharges = 0 ici, volontairement. Le salaire de base
                // catégoriel ne doit pas dépendre de la situation familiale de l'employé :
                // la réduction d'impôt pour charges de famille est un avantage personnel qui
                // s'applique au moment du calcul du bulletin (salaire net réellement payé),
                // pas au moment de la détermination du salaire de base. On estime donc ici le
                // salaire de base qui atteint le net visé SANS aucune réduction pour charges ;
                // les charges réelles de l'employé sont appliquées ensuite, séparément, lors du
                // calcul du bulletin définitif (CalculateMonthlyTax appelé avec l'employé réel) —
                // ce qui porte alors le net effectivement payé au-dessus du net visé ici.
                var testEmp = new Employee
                {
                    BaseSalary = baseSalary,
                    HousingAllowance = housingA,
                    FunctionAllowance = functionA,
                    TransportAllowance = transportA,
                    SujetionAllowance = employee.SujetionAllowance,
                    Category = employee.Category,
                    FamilyCharges = 0,
                    IsSpouseWorking = employee.IsSpouseWorking
                };

                // c. Calcul fiscal complet
                var tax = CalculateMonthlyTax(testEmp);

                // d. Reconstitution fiable du net à payer
                decimal netCalc = tax.SalaireNetaPayer > 0
                    ? tax.SalaireNetaPayer
                    : baseSalary + housingA + functionA + transportA
                      - tax.IUTSNet - tax.CnssEmployee - tax.RetenueSpeciale;

                return (netCalc, housingA, functionA, transportA, tax.IUTSNet);
            }

            // ────────────────── 4. Bornes initiales pour la recherche ───────────────────
            const decimal TOLERANCE = 0m;                // 0 FCFA
            decimal low = 0m;
            decimal high = netSalary;                    // point de départ raisonnable

            // Si le net obtenu avec 'high' < net désiré, on double jusqu'à dépasser
            var evalHigh = Evaluate(high);
            int safety = 0;
            while (evalHigh.netToPay < netSalary && safety++ < 30)
            {
                low = high;
                high *= 2;
                evalHigh = Evaluate(high);
            }

            // ────────────────── 5. Recherche dichotomique ───────────────────────────────
            // NOTE : les indemnités sont arrondies au millier supérieur (cf. RoundUpToThousand),
            // donc netToPay(baseSalary) est une fonction "en escalier" : deux bases consécutives
            // peuvent encadrer le salaire net cible sans qu'aucune des deux ne le touche
            // exactement. On mémorise donc, tout au long de la recherche, le meilleur candidat
            // trouvé au-dessus de la cible (bestAbove) et le meilleur en-dessous (bestBelow),
            // plutôt que de se fier uniquement aux valeurs finales de low/high : celles-ci
            // peuvent avoir été mises à jour sans jamais être ré-évaluées individuellement,
            // ce qui faisait « perdre » le meilleur candidat (bug d'origine).
            //
            // La contrainte IUTS ≤ 15 % du salaire de base a été retirée de la logique de
            // décision : elle n'est pas satisfiable pour une grande partie des salaires
            // (notamment les hauts revenus), et son utilisation pour orienter la recherche
            // binaire cassait le sens de la comparaison (elle forçait 'high' à baisser même
            // quand le net obtenu était encore trop BAS), ce qui faisait converger l'algorithme
            // vers un salaire de base largement sous-estimé. L'exigence métier prioritaire est
            // de restituer le salaire net exact ; cette contrainte n'est donc plus appliquée.
            (decimal baseSalary, decimal netToPay, decimal housing, decimal function, decimal transport, decimal iuts)? bestAbove = null;
            (decimal baseSalary, decimal netToPay, decimal housing, decimal function, decimal transport, decimal iuts)? bestBelow = null;

            void Consider(decimal baseSalary, (decimal netToPay, decimal housing, decimal function, decimal transport, decimal iuts) eval)
            {
                var candidate = (baseSalary, eval.netToPay, eval.housing, eval.function, eval.transport, eval.iuts);
                if (eval.netToPay >= netSalary)
                {
                    if (bestAbove == null || eval.netToPay < bestAbove.Value.netToPay)
                        bestAbove = candidate;
                }
                else
                {
                    if (bestBelow == null || eval.netToPay > bestBelow.Value.netToPay)
                        bestBelow = candidate;
                }
            }

            Consider(low, Evaluate(low));
            Consider(high, evalHigh);

            while (high - low > 1m)                     // précision à l'unité
            {
                decimal mid = Math.Floor((low + high) / 2m);
                var evalMid = Evaluate(mid);
                Consider(mid, evalMid);

                if (Math.Abs(evalMid.netToPay - netSalary) <= TOLERANCE)
                {
                    return (mid, evalMid.housing, evalMid.function, evalMid.transport, evalMid.iuts);
                }

                // Mise à jour des bornes : uniquement en fonction du sens réel du net obtenu,
                // pour préserver la monotonie de la recherche dichotomique.
                if (evalMid.netToPay > netSalary)
                    high = mid - 1;
                else
                    low = mid + 1;
            }

            // ────────────────── 6. Les bornes finales n'ont pas forcément été ré-évaluées
            // individuellement pendant la boucle (elles peuvent avoir été mises à jour sans
            // être reprises comme 'mid') : on les réévalue explicitement avant de choisir.
            Consider(low, Evaluate(low));
            Consider(high, Evaluate(high));

            // Le meilleur candidat global est celui dont l'écart absolu au net cible est
            // le plus petit, qu'il se situe au-dessus ou en-dessous de celui-ci.
            decimal diffAbove = bestAbove != null ? Math.Abs(bestAbove.Value.netToPay - netSalary) : decimal.MaxValue;
            decimal diffBelow = bestBelow != null ? Math.Abs(bestBelow.Value.netToPay - netSalary) : decimal.MaxValue;

            var chosen = diffAbove <= diffBelow ? bestAbove : bestBelow;

            return (chosen.Value.baseSalary, chosen.Value.housing, chosen.Value.function,
                    chosen.Value.transport, chosen.Value.iuts);
        }


        public TaxCalculation CalculateTax(Employee employee, bool isAnnual, bool isRegularization, decimal previousTaxPaid = 0)
        {
            if (employee == null)
                throw new ArgumentNullException(nameof(employee));

            if (isRegularization)
            {
                var monthlyCalculations = new List<TaxCalculation>
                {
                    CalculateMonthlyTax(employee)
                };
                for (int i = 1; i < 12; i++)
                {
                    monthlyCalculations.Add(new TaxCalculation
                    {
                        Employee = employee,
                        CalculationDate = DateTime.Now,
                        SalaireBrut = monthlyCalculations[0].SalaireBrut,
                        BaseIUTSHorsDeduction = monthlyCalculations[0].BaseIUTSHorsDeduction,
                        IUTSNet = previousTaxPaid / 12,
                        ExemptAmount = monthlyCalculations[0].ExemptAmount,
                        FamilyChargeReduction = monthlyCalculations[0].FamilyChargeReduction
                    });
                }
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

            // Salaire brut = salaire de base + toutes les indemnités de toute nature
            // (logement, fonction, transport, sujétion, avantages en nature...), cf. Art.109/111 CGI.
            decimal grossIncome = employee.BaseSalary
                                + employee.HousingAllowance
                                + employee.FunctionAllowance
                                + employee.TransportAllowance
                                + employee.SujetionAllowance;

            decimal otherAllowances = 0m;
            decimal benefitsInKind = 0m;
            grossIncome += otherAllowances + benefitsInKind;

            calculation.SalaireBrut = grossIncome;

            calculation.CnssEmployee = CalculerCotisationSociale(grossIncome, 800000, 0.055m);
            employee.PensionContribution = calculation.CnssEmployee;

            calculation.CnssPartPatronale = CalculerCotisationSociale(grossIncome, 800000, 0.16m);

            decimal exemptAmount = CalculateExemptAmount(employee, grossIncome, calculation.CnssEmployee);
            calculation.ExemptAmount = exemptAmount;

            // Art.111 CGI : la déduction pour pension/retraite est plafonnée à 8% du SALAIRE DE
            // BASE uniquement (à l'exclusion des indemnités) — pas du salaire brut total.
            decimal pensionContribution = Math.Min(employee.PensionContribution,
                employee.BaseSalary * _taxDataService.ConstantValues.MaxPensionContributionRate);

            decimal professionalDeduction = CalculateProfessionalDeduction(employee);
            calculation.ProfessionalDeduction = professionalDeduction;

            decimal rawTaxableIncome = grossIncome - exemptAmount - pensionContribution - professionalDeduction;
            if (rawTaxableIncome < 0) rawTaxableIncome = 0;

            // Art.112 CGI : le barème progressif est publié par tranches de 100 FCFA
            // (0-30 000 / 30 100-50 000 / ...) ; la base imposable doit donc être arrondie
            // à la centaine de FCFA inférieure avant application du barème (cf. modèle de
            // bulletin de référence, cellule D34 : ROUNDDOWN(..., -2)).
            decimal taxableIncome = Math.Floor(rawTaxableIncome / 100m) * 100m;
            calculation.BaseIUTSHorsDeduction = taxableIncome;

            decimal grossTax = CalculateGrossTax(taxableIncome);

            int familyCharges = Math.Min(employee.FamilyCharges, 7);
            decimal familyChargeRate = _taxDataService.FamilyChargeRates.ContainsKey(familyCharges)
                ? _taxDataService.FamilyChargeRates[familyCharges]
                : 0m;

            decimal familyReduction = grossTax * familyChargeRate;
            calculation.FamilyChargeReduction = familyReduction;

            decimal netTax = grossTax - familyReduction;
            calculation.IUTSNet = netTax;

            calculation.TaxRate = grossIncome > 0 ? netTax / grossIncome * 100 : 0;

            calculation.RetenueSpeciale = (grossIncome - netTax - calculation.CnssEmployee) * 0.01m;
            calculation.TaxePatronale = grossIncome * 0.03m;
            calculation.MasseSalariale = grossIncome + calculation.TaxePatronale + calculation.CnssPartPatronale;

            GenerateCalculationDetails(calculation, exemptAmount, pensionContribution, professionalDeduction, grossTax);

            return calculation;
        }

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

            var details = new System.Text.StringBuilder();
            details.AppendLine("Calcul de l'IUTS annuel :");
            details.AppendLine($"Basé sur le calcul mensuel multiplié par 12");
            details.AppendLine($"1. Revenu brut annuel : {annualCalculation.SalaireBrut:N0} FCFA");
            details.AppendLine($"2. Revenu imposable annuel : {annualCalculation.BaseIUTSHorsDeduction:N0} FCFA");
            details.AppendLine($"3. Impôt annuel à payer : {annualCalculation.IUTSNet:N0} FCFA");

            annualCalculation.CalculationDetails = details.ToString();

            return annualCalculation;
        }

        public TaxCalculation CalculateAnnualRegularization(List<TaxCalculation> monthlyCalculations)
        {
            if (monthlyCalculations == null || !monthlyCalculations.Any())
                throw new ArgumentException("Les calculs mensuels doivent être fournis pour la régularisation");

            var employee = monthlyCalculations.First().Employee;

            decimal totalGrossSalary = monthlyCalculations.Sum(c => c.SalaireBrut);
            decimal totalExemptAmount = monthlyCalculations.Sum(c => c.ExemptAmount);
            decimal totalNetTaxableIncome = monthlyCalculations.Sum(c => c.BaseIUTSHorsDeduction);
            decimal totalTaxPaid = monthlyCalculations.Sum(c => c.IUTSNet);

            decimal annualGrossTax = CalculateGrossTax(totalNetTaxableIncome);

            int familyCharges = Math.Min(employee.FamilyCharges, 7);
            decimal familyChargeRate = _taxDataService.FamilyChargeRates.ContainsKey(familyCharges)
                ? _taxDataService.FamilyChargeRates[familyCharges]
                : 0m;

            decimal annualFamilyReduction = annualGrossTax * familyChargeRate;
            decimal annualNetTax = annualGrossTax - annualFamilyReduction;
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

        public decimal CalculerCotisationSociale(decimal salaireBrut, decimal brutPlafond, decimal taux)
        {
            if (salaireBrut > brutPlafond)
                return brutPlafond * taux;
            else
                return salaireBrut * taux;
        }

        private decimal CalculateProfessionalDeduction(Employee employee)
        {
            bool isHigherCategory = employee.IsHigherCategory;
            decimal professionalDeductionRate = isHigherCategory
                ? _taxDataService.ConstantValues.ProfessionalDeductionHigherCategories
                : _taxDataService.ConstantValues.ProfessionalDeductionOtherCategories;

            return employee.BaseSalary * professionalDeductionRate;
        }

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
        /// Calcule le montant exonéré des indemnités de logement, fonction et transport,
        /// conformément à l'Art.106 §1 du CGI.
        /// </summary>
        /// <param name="employee">Employé (indemnités accordées)</param>
        /// <param name="grossIncome">Salaire brut total (salaire de base + toutes indemnités)</param>
        /// <param name="cnssEmployee">Retenue CNSS part employée effectivement opérée (non utilisée
        /// ici, conservée pour référence : voir note ci-dessous)</param>
        private decimal CalculateExemptAmount(Employee employee, decimal grossIncome, decimal cnssEmployee)
        {
            // Art.106 CGI : « Le salaire brut s'entend du salaire de base ... augmenté des
            // indemnités de toute nature ... et diminué des retenues pour cotisation sociale. »
            // Lu à la lettre, la base des plafonds de 20 %/5 %/5 % serait donc le salaire brut
            // diminué de la retenue CNSS salariale. En pratique toutefois, le bulletin de paie
            // de référence (onglet "Option 4" du modèle fourni, cellules C29/C30/C31 = MIN(plafond,
            // D22*taux) où D22 est le salaire brut total AVANT retenue CNSS) n'applique PAS cette
            // diminution, et c'est cette convention qui a été retenue ici afin de coller exactement
            // aux montants du bulletin de référence (validé au FCFA près sur "Option 4" et "Option 3").
            decimal salaireBrutArt106 = grossIncome;

            decimal exemptHousingAllowance = Math.Min(
                employee.HousingAllowance,
                Math.Min(
                    salaireBrutArt106 * _taxDataService.ConstantValues.MaxHousingAllowanceRate,
                    _taxDataService.ConstantValues.MaxHousingAllowanceAmount)
            );

            decimal exemptFunctionAllowance = Math.Min(
                employee.FunctionAllowance,
                Math.Min(
                    salaireBrutArt106 * _taxDataService.ConstantValues.MaxFunctionAllowanceRate,
                    _taxDataService.ConstantValues.MaxFunctionAllowanceAmount)
            );

            decimal exemptTransportAllowance = Math.Min(
                employee.TransportAllowance,
                Math.Min(
                    salaireBrutArt106 * _taxDataService.ConstantValues.MaxTransportAllowanceRate,
                    _taxDataService.ConstantValues.MaxTransportAllowanceAmount)
            );

            return exemptHousingAllowance + exemptFunctionAllowance + exemptTransportAllowance;
        }

        private decimal CalculateGrossTax(decimal taxableIncome)
        {
            decimal tax = 0m;

            foreach (var bracket in _taxDataService.TaxBrackets)
            {
                if (taxableIncome < bracket.LowerBound)
                    break;

                decimal amountInBracket = Math.Min(
                    taxableIncome - bracket.LowerBound,
                    bracket.UpperBound - bracket.LowerBound
                );

                if (amountInBracket > 0)
                    tax += amountInBracket * bracket.Rate / 100m;

                if (taxableIncome <= bracket.UpperBound)
                    break;
            }

            return tax;
        }

        private void OnTaxDataUpdated(TaxDataUpdatedEventArgs e)
        {
            TaxParametersUpdated?.Invoke(this, new TaxParametersUpdatedEventArgs
            {
                TaxBracketsUpdated = e.TaxBracketsUpdated,
                TaxConstantsUpdated = e.TaxConstantsUpdated,
                UpdateTime = e.UpdateTime
            });
        }

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
                    if (_taxDataService != null && _taxDataUpdatedHandler != null)
                    {
                        _taxDataService.TaxDataUpdated -= _taxDataUpdatedHandler;
                        _taxDataUpdatedHandler = null;
                    }
                }
                _isDisposed = true;
            }
        }
    }

    public class TaxParametersUpdatedEventArgs : EventArgs
    {
        public bool TaxBracketsUpdated { get; set; }
        public bool TaxConstantsUpdated { get; set; }
        public DateTime UpdateTime { get; set; }
    }
}
