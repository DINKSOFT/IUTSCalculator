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

        /// <summary>
        /// Taux et plafonds sociaux appliqués par le moteur (module DEC-01).
        ///
        /// Initialisés aux VALEURS HISTORIQUES — celles qui étaient écrites en dur dans
        /// ce fichier — pour que rien ne change tant qu'un barème daté n'est pas appliqué
        /// explicitement via <see cref="AppliquerBareme"/>. C'est la garantie
        /// d'équivalence : un appelant qui ne connaît pas ce module obtient exactement les
        /// mêmes bulletins qu'avant.
        /// </summary>
        public ParametresSociaux ParametresAppliques { get; private set; }

        public TaxCalculatorService(TaxDataService taxDataService)
        {
            _taxDataService = taxDataService ?? throw new ArgumentNullException(nameof(taxDataService));
            ParametresAppliques = ParametresSociaux.Historiques();
            _taxDataUpdatedHandler = (sender, e) => OnTaxDataUpdated(e);
            _taxDataService.TaxDataUpdated += _taxDataUpdatedHandler;
        }

        /// <summary>
        /// Applique les taux d'un barème daté (SOC-02) aux calculs suivants. Passer
        /// <c>null</c> revient aux valeurs historiques.
        /// </summary>
        public void AppliquerBareme(BaremeLegal bareme)
        {
            ParametresAppliques = bareme == null
                ? ParametresSociaux.Historiques()
                : ParametresSociaux.DepuisBareme(bareme);
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

        /// <summary>
        /// Calcul mensuel sans variable : comportement inchangé depuis toujours.
        /// </summary>
        public TaxCalculation CalculateMonthlyTax(Employee employee)
        {
            return CalculateMonthlyTax(employee, ApportVariables.Aucun());
        }

        /// <summary>
        /// Calcul mensuel tenant compte des variables du mois (branchement de PAI-02).
        ///
        /// <b>Équivalence stricte.</b> Avec <see cref="ApportVariables.Aucun"/>, cette
        /// méthode donne au franc près ce que donnait l'ancienne : tous les apports sont
        /// nuls, et chaque terme ajouté ci-dessous s'annule.
        ///
        /// Trois assiettes sont désormais distinguées là où il n'y en avait qu'une :
        ///
        ///   • <c>brutImposable</c> — ce qui entre dans la base IUTS ;
        ///   • <c>brutCotisable</c> — ce qui entre dans l'assiette CNSS ;
        ///   • <c>SalaireBrut</c>  — le brut complet qui s'imprime sur le bulletin.
        ///
        /// Les confondre était sans conséquence tant que tous les éléments de paie étaient
        /// soumis aux deux. Ce n'est plus vrai dès qu'une rubrique porte des drapeaux
        /// différents, et c'est précisément ce que SOC-01 rend possible.
        /// </summary>
        public TaxCalculation CalculateMonthlyTax(Employee employee, ApportVariables apport)
        {
            if (employee == null)
                throw new ArgumentNullException(nameof(employee));

            if (apport == null)
                apport = ApportVariables.Aucun();

            var calculation = new TaxCalculation
            {
                Employee = employee,
                CalculationDate = DateTime.Now
            };

            // Rémunération permanente : salaire de base + indemnités de toute nature
            // (logement, fonction, transport, sujétion), cf. Art.109/111 CGI.
            decimal remunerationPermanente = employee.BaseSalary
                                + employee.HousingAllowance
                                + employee.FunctionAllowance
                                + employee.TransportAllowance
                                + employee.SujetionAllowance;

            decimal otherAllowances = 0m;
            decimal benefitsInKind = 0m;
            remunerationPermanente += otherAllowances + benefitsInKind;

            // Absences non rémunérées (module PAI-04) : la retenue s'impute sur la
            // rémunération permanente AVANT tout calcul. Une journée non travaillée ne
            // réduit pas seulement le net, elle réduit l'assiette de l'impôt et des
            // cotisations — retenir après coup ferait payer au salarié l'IUTS et la CNSS
            // sur un salaire qu'il n'a pas perçu.
            //
            // Sans absence saisie, la retenue vaut zéro et la ligne ci-dessous ne change
            // rien : c'est la garantie d'équivalence stricte, vérifiable sur l'écran de
            // contrôle du moteur.
            decimal retenueAbsences = apport.RetenueSurBrut;
            if (retenueAbsences > remunerationPermanente)
                retenueAbsences = remunerationPermanente;
            if (retenueAbsences < 0m)
                retenueAbsences = 0m;

            remunerationPermanente -= retenueAbsences;
            calculation.RetenueAbsences = retenueAbsences;

            // Base imposable à l'IUTS : le permanent, plus les gains variables soumis.
            decimal grossIncome = remunerationPermanente + apport.GainsImposables;

            // Assiette des cotisations : le permanent, plus les gains variables cotisables.
            decimal brutCotisable = remunerationPermanente + apport.GainsCotisables;

            calculation.GainsVariables = apport.TousLesGains;
            calculation.RetenuesDiverses = apport.Retenues;

            // Le brut du bulletin comprend TOUT ce qui est versé, y compris les gains ni
            // imposables ni cotisables — qui rejoignent donc le net sans passer par le
            // calcul, puisque le net se déduit du brut.
            calculation.SalaireBrut = grossIncome + apport.GainsNonSoumis;

            // ── Cotisations CNSS (module DEC-01) ────────────────────────────────
            //
            // Les taux et le plafond viennent désormais de ParametresSociaux, initialisés
            // aux valeurs historiques (5,5 % / 16 % / 800 000) : à paramètres inchangés,
            // les trois lignes ci-dessous donnent exactement ce que donnaient les
            // littéraux qu'elles remplacent.
            var parametres = ParametresAppliques;
            // L'assiette porte sur le brut COTISABLE, pas sur le brut imposable :
            // sans variable les deux sont égaux, avec variables ils divergent.
            decimal assietteCnss = parametres.Assiette(brutCotisable);
            calculation.AssietteCnss = assietteCnss;

            calculation.CnssEmployee = assietteCnss * parametres.TauxCnssSalarie;

            // La retenue CNSS sert aussi de base au plafond de déduction retraite de
            // l'article 111 (voir plus bas) — une variable locale suffit : rien d'autre ne
            // lit ce nombre entre-temps. Avant dette n°1, ce report passait par
            // employee.PensionContribution, un champ de la fiche que le moteur écrasait au
            // passage — invisible sur tout écran (aucun XAML n'y touche), et sans risque de
            // fuite vers une fiche enregistrée puisque chaque écran charge sa propre copie
            // du salarié, mais un effet de bord resté sans raison dès lors qu'une variable
            // locale donne exactement le même résultat.
            decimal cnssEmployeePourDeductionRetraite = calculation.CnssEmployee;

            // Ventilation par branche, exigée sur le bordereau CNSS. Aucun arrondi n'est
            // appliqué par branche : la somme des trois est donc exactement égale à la
            // part patronale globale, en arithmétique décimale.
            calculation.CnssPrestationsFamiliales = assietteCnss * parametres.TauxPrestationsFamiliales;
            calculation.CnssRisquesProfessionnels = assietteCnss * parametres.TauxRisquesProfessionnels;
            calculation.CnssPensionsPatronal = assietteCnss * parametres.TauxPensionsPatronal;

            calculation.CnssPartPatronale = assietteCnss * parametres.TauxPatronalTotal;

            decimal exemptAmount = CalculateExemptAmount(employee, grossIncome, calculation.CnssEmployee);
            calculation.ExemptAmount = exemptAmount;

            // Art.111 CGI : la déduction pour pension/retraite est plafonnée à 8% du SALAIRE DE
            // BASE uniquement (à l'exclusion des indemnités) — pas du salaire brut total.
            // Un mois d'entrée ou de sortie, le plafond suit lui aussi la base effectivement due.
            decimal pensionContribution = Math.Min(cnssEmployeePourDeductionRetraite,
                employee.BaseSalary * apport.FractionContrat * _taxDataService.ConstantValues.MaxPensionContributionRate);

            // Abattement sur la base effectivement due : un mois d'entrée ou de sortie, il
            // suit le prorata du contrat (sinon une entrée le 21 gardait l'abattement d'un
            // mois entier sur un brut d'un tiers de mois). Les absences ordinaires ne changent
            // pas l'abattement (comportement existant).
            decimal professionalDeduction = CalculateProfessionalDeduction(employee) * apport.FractionContrat;
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
            decimal familyChargeRate = parametres.ChargesFamille.ContainsKey(familyCharges)
                ? parametres.ChargesFamille[familyCharges]
                : 0m;

            decimal familyReduction = grossTax * familyChargeRate;
            calculation.FamilyChargeReduction = familyReduction;

            decimal netTax = grossTax - familyReduction;
            calculation.IUTSNet = netTax;

            calculation.TaxRate = grossIncome > 0 ? netTax / grossIncome * 100 : 0;

            calculation.RetenueSpeciale = (grossIncome - netTax - calculation.CnssEmployee) * 0.01m;

            // ── Taxe patronale et d'apprentissage (module DEC-02) ───────────────
            //
            // Assiette : le salaire brut, diminué des rubriques que le plan de paie en
            // exonère explicitement (apport.GainsExoneresTPA). Par défaut aucune rubrique
            // n'est exonérée — l'assiette reste donc le brut intégral, exactement ce que
            // calculait l'acompte livré avec DEC-01.
            //
            // Taux : tiré des paramètres, eux-mêmes issus du barème daté applicable à la
            // période (SOC-02) quand l'appelant l'a résolu via AppliquerBareme ; sinon la
            // valeur historique de 3 %, inchangée.
            //
            // Pas de plafonnement : à la différence de la CNSS, la TPA n'est pas assise sur
            // une assiette plafonnée — aucun texte ne le prévoit.
            decimal assietteTPA = calculation.SalaireBrut - apport.GainsExoneresTPA;
            if (assietteTPA < 0m) assietteTPA = 0m;

            calculation.AssietteTPA = assietteTPA;
            calculation.TaxePatronale = assietteTPA * parametres.TauxTaxePatronale;
            calculation.MasseSalariale = calculation.SalaireBrut + calculation.TaxePatronale + calculation.CnssPartPatronale;

            // Net après impôt et net à payer : le modèle les expose en propriétés CALCULÉES,
            // mais la base stocke deux champs distincts, qui restaient à zéro faute d'être
            // alimentés. Un bulletin enregistré partait donc avec un net à payer nul, et
            // les totaux d'une période affichaient 0 pour tout l'effectif.
            //
            // Ces deux lignes ne changent aucun montant calculé : elles recopient, à la
            // toute fin, ce que le calcul vient d'établir.
            calculation.NetSalaryAfterTaxPersisted = calculation.NetSalaryAfterTax;
            calculation.SalaireNetaPayerPersisted = calculation.SalaireNetaPayer;

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
            decimal familyChargeRate = ParametresAppliques.ChargesFamille.ContainsKey(familyCharges)
                ? ParametresAppliques.ChargesFamille[familyCharges]
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
            // Suit le barème daté appliqué à la période (SOC-02), comme les taux CNSS/TPA
            // ci-dessus — dette connue n°2. Un dossier qui n'a jamais touché ce réglage du
            // barème obtient les mêmes valeurs qu'avant (ParametresSociaux.Historiques()).
            bool isHigherCategory = employee.IsHigherCategory;
            decimal professionalDeductionRate = isHigherCategory
                ? ParametresAppliques.AbattementCategoriesSuperieures
                : ParametresAppliques.AbattementAutresCategories;

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
            details.AppendLine($"13. TPA : {calculation.TaxePatronale:N0} FCFA (assiette {calculation.AssietteTPA:N0} FCFA)");
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

            // Plafonds d'exonération suivant le barème daté appliqué à la période (SOC-02),
            // comme les taux CNSS/TPA — dette connue n°2. Un dossier qui n'a jamais touché
            // ce réglage du barème obtient exactement les plafonds d'avant ce module
            // (ParametresSociaux.Historiques() : 20 %/75 000, 5 %/50 000, 5 %/30 000).
            decimal exemptHousingAllowance = Math.Min(
                employee.HousingAllowance,
                Math.Min(
                    salaireBrutArt106 * ParametresAppliques.TauxMaxIndemniteLogement,
                    ParametresAppliques.PlafondIndemniteLogement)
            );

            decimal exemptFunctionAllowance = Math.Min(
                employee.FunctionAllowance,
                Math.Min(
                    salaireBrutArt106 * ParametresAppliques.TauxMaxIndemniteFonction,
                    ParametresAppliques.PlafondIndemniteFonction)
            );

            decimal exemptTransportAllowance = Math.Min(
                employee.TransportAllowance,
                Math.Min(
                    salaireBrutArt106 * ParametresAppliques.TauxMaxIndemniteTransport,
                    ParametresAppliques.PlafondIndemniteTransport)
            );

            return exemptHousingAllowance + exemptFunctionAllowance + exemptTransportAllowance;
        }

        /// <summary>
        /// Applique le barème progressif de l'IUTS à la base imposable.
        ///
        /// Les tranches viennent de <see cref="ParametresAppliques"/> — le barème daté
        /// applicable à la période quand l'appelant l'a résolu via
        /// <see cref="AppliquerBareme"/>, sinon la table historique de l'Art.112 CGI,
        /// inchangée. C'était le dernier maillon du référentiel daté (module SOC-02) resté
        /// volontairement débranché du moteur, pour ne pas le modifier en même temps que
        /// le stockage de ses paramètres (voir la note d'architecture de BaremeLegal.cs) :
        /// il est désormais branché, sur le même modèle que les taux CNSS/TPA (DEC-01) et
        /// les plafonds d'exonération/abattements (dette n°2).
        /// </summary>
        public decimal CalculateGrossTax(decimal taxableIncome)
        {
            decimal tax = 0m;

            foreach (var bracket in ParametresAppliques.Tranches)
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
