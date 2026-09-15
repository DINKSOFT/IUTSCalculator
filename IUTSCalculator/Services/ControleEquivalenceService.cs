using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using IUTSCalculator.Models;

namespace IUTSCalculator.Services
{
    /// <summary>Résultat de la comparaison pour un salarié.</summary>
    public class LigneControleEquivalence
    {
        public string Matricule { get; set; }
        public string NomComplet { get; set; }
        public decimal SalaireBrut { get; set; }

        /// <summary>Brut de l'ancienne formule : base + les quatre indemnités.</summary>
        public decimal BrutHerite { get; set; }

        /// <summary>Brut produit par le moteur après le branchement des variables.</summary>
        public decimal BrutCourant { get; set; }

        /// <summary>Assiette CNSS produite par le moteur, désormais distincte du brut.</summary>
        public decimal AssietteCourante { get; set; }

        public decimal CnssSalarialeHeritee { get; set; }
        public decimal CnssSalarialeCourante { get; set; }

        public decimal CnssPatronaleHeritee { get; set; }
        public decimal CnssPatronaleCourante { get; set; }

        public decimal TaxePatronaleHeritee { get; set; }
        public decimal TaxePatronaleCourante { get; set; }

        /// <summary>Somme des trois branches, qui doit égaler la part patronale.</summary>
        public decimal SommeDesBranches { get; set; }

        public decimal EcartCnssSalariale
        {
            get { return CnssSalarialeCourante - CnssSalarialeHeritee; }
        }

        public decimal EcartCnssPatronale
        {
            get { return CnssPatronaleCourante - CnssPatronaleHeritee; }
        }

        public decimal EcartTaxePatronale
        {
            get { return TaxePatronaleCourante - TaxePatronaleHeritee; }
        }

        public decimal EcartVentilation
        {
            get { return SommeDesBranches - CnssPatronaleCourante; }
        }

        /// <summary>
        /// Écart sur le brut. Le branchement des variables a décomposé un unique
        /// <c>grossIncome</c> en brut imposable, brut cotisable et brut du bulletin ;
        /// sans variable, les trois doivent coïncider avec l'ancien.
        /// </summary>
        public decimal EcartBrut
        {
            get { return BrutCourant - BrutHerite; }
        }

        /// <summary>Écart entre l'assiette CNSS et celle que l'ancienne formule aurait prise.</summary>
        public decimal EcartAssiette
        {
            get { return AssietteCourante - (BrutHerite > 800000m ? 800000m : BrutHerite); }
        }

        /// <summary>
        /// Comparaison à l'identique ABSOLU : pas de tolérance. Le moteur travaille en
        /// <c>decimal</c>, donc à taux égaux le résultat doit être bit à bit le même. Une
        /// tolérance, ici, ne ferait que masquer la seule chose qu'on cherche à voir.
        /// </summary>
        public bool EstIdentique
        {
            get
            {
                return EcartCnssSalariale == 0m
                    && EcartCnssPatronale == 0m
                    && EcartTaxePatronale == 0m
                    && EcartVentilation == 0m
                    && EcartBrut == 0m
                    && EcartAssiette == 0m;
            }
        }

        public string Verdict
        {
            get
            {
                if (EstIdentique) return "Identique";

                var ecarts = new List<string>();
                if (EcartCnssSalariale != 0m) ecarts.Add(string.Format("CNSS salariale {0:+#,##0;-#,##0}", EcartCnssSalariale));
                if (EcartCnssPatronale != 0m) ecarts.Add(string.Format("CNSS patronale {0:+#,##0;-#,##0}", EcartCnssPatronale));
                if (EcartTaxePatronale != 0m) ecarts.Add(string.Format("TPA {0:+#,##0;-#,##0}", EcartTaxePatronale));
                if (EcartVentilation != 0m) ecarts.Add(string.Format("ventilation {0:+#,##0;-#,##0}", EcartVentilation));
                if (EcartBrut != 0m) ecarts.Add(string.Format("brut {0:+#,##0;-#,##0}", EcartBrut));
                if (EcartAssiette != 0m) ecarts.Add(string.Format("assiette {0:+#,##0;-#,##0}", EcartAssiette));

                return string.Join(" ; ", ecarts);
            }
        }
    }

    /// <summary>
    /// Contrôle d'équivalence du moteur de calcul (module DEC-01).
    ///
    /// Deux interventions ont touché le moteur audité :
    ///
    ///   • DEC-01 a remplacé des taux écrits en dur — <c>800000</c>, <c>0.055m</c>,
    ///     <c>0.16m</c>, <c>0.03m</c> — par des paramètres issus des barèmes datés ;
    ///   • le branchement de PAI-02 a décomposé un unique <c>grossIncome</c> en brut
    ///     imposable, brut cotisable et brut du bulletin.
    ///
    /// Promettre que « rien ne change » ne suffit pas sur un moteur audité : ce service le
    /// VÉRIFIE, salarié par salarié, en recalculant chaque bulletin avec les deux formules.
    ///
    /// La référence n'est pas une copie du moteur mais la formule d'origine, réécrite ici
    /// telle qu'elle était, avec ses littéraux. Si un jour un barème modifie réellement un
    /// taux, l'écran cesse d'annoncer « identique » — et c'est normal : il dira alors
    /// exactement de combien et sur quelle ligne.
    /// </summary>
    public class ControleEquivalenceService
    {
        private readonly DataService _dataService;
        private readonly TaxCalculatorService _taxCalculatorService;

        // Les valeurs telles qu'elles étaient écrites dans TaxCalculatorService avant
        // DEC-01. Ne pas les modifier : elles sont le témoin de la comparaison.
        private const decimal PlafondHerite = 800000m;
        private const decimal TauxSalarialHerite = 0.055m;
        private const decimal TauxPatronalHerite = 0.16m;
        private const decimal TauxTpaHerite = 0.03m;

        public ControleEquivalenceService(
            DataService dataService, TaxCalculatorService taxCalculatorService)
        {
            if (dataService == null) throw new ArgumentNullException("dataService");
            if (taxCalculatorService == null) throw new ArgumentNullException("taxCalculatorService");

            _dataService = dataService;
            _taxCalculatorService = taxCalculatorService;
        }

        /// <summary>
        /// Rejoue le calcul de tous les salariés du dossier avec les deux formules.
        /// </summary>
        public async Task<List<LigneControleEquivalence>> ControlerAsync()
        {
            // Le moteur est un service partagé : un autre écran (Bulletin, Calcul d'IUTS...)
            // peut avoir laissé un barème daté appliqué (module DEC-02) juste avant ce
            // contrôle. Ce contrôle compare le moteur à ses valeurs HISTORIQUES, pas à un
            // barème quelconque — sans cette remise à zéro, un écart signalé ici pourrait
            // n'être que l'effet d'un barème resté actif, pas une vraie régression.
            _taxCalculatorService.AppliquerBareme(null);

            var resultats = new List<LigneControleEquivalence>();
            var employes = await _dataService.LoadEmployeesAsync();

            foreach (var employe in employes.OrderBy(e => e.LastName).ThenBy(e => e.FirstName))
            {
                // Le calcul écrit dans employee.PensionContribution : on travaille sur une
                // copie pour ne pas modifier la fiche du salarié pendant un contrôle.
                var copie = employe.Clone();
                var calcul = _taxCalculatorService.CalculateMonthlyTax(copie);

                decimal brut = calcul.SalaireBrut;
                decimal assietteHeritee = brut > PlafondHerite ? PlafondHerite : brut;

                resultats.Add(new LigneControleEquivalence
                {
                    Matricule = employe.EmployeeId,
                    NomComplet = string.Format("{0} {1}", employe.LastName, employe.FirstName).Trim(),
                    SalaireBrut = brut,

                    CnssSalarialeHeritee = assietteHeritee * TauxSalarialHerite,
                    CnssSalarialeCourante = calcul.CnssEmployee,

                    CnssPatronaleHeritee = assietteHeritee * TauxPatronalHerite,
                    CnssPatronaleCourante = calcul.CnssPartPatronale,

                    TaxePatronaleHeritee = brut * TauxTpaHerite,
                    TaxePatronaleCourante = calcul.TaxePatronale,

                    BrutHerite = employe.BaseSalary
                               + employe.HousingAllowance
                               + employe.FunctionAllowance
                               + employe.TransportAllowance
                               + employe.SujetionAllowance,
                    BrutCourant = calcul.SalaireBrut,
                    AssietteCourante = calcul.AssietteCnss,

                    SommeDesBranches = calcul.CnssPrestationsFamiliales
                                     + calcul.CnssRisquesProfessionnels
                                     + calcul.CnssPensionsPatronal
                });
            }

            return resultats;
        }

        /// <summary>Paramètres effectivement appliqués par le moteur au moment du contrôle.</summary>
        public ParametresSociaux ParametresAppliques
        {
            get { return _taxCalculatorService.ParametresAppliques; }
        }
    }
}
