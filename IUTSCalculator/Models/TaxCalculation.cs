using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace IUTSCalculator.Models
{
    /// <summary>
    /// Représente un calcul d'impôt sur les traitements et salaires
    /// </summary>
    public class TaxCalculation : INotifyPropertyChanged
    {
        private string _id;
        private Employee _employee;
        private DateTime _calculationDate;
        private decimal _salairebrut;
        private decimal _baseIUTShorsdeduction;
        private decimal _iutsnet;
        private decimal _taxRate;
        private decimal _familyChargeReduction;
        private decimal _exemptAmount;
        private string _calculationDetails;
        private bool _isAnnual;
        private bool _isRegularization;
        private decimal _taxDifference;
        private decimal _cnssemployee;
        private decimal _cnsspartpatronale;
        private decimal _retenuesDiverses;
        private decimal _gainsVariables;
        private decimal _retenueAbsences;
        private decimal _assietteCnss;
        private decimal _cnssPrestationsFamiliales;
        private decimal _cnssRisquesProfessionnels;
        private decimal _cnssPensionsPatronal;
        private decimal _taxepatronale;
        private decimal _assietteTPA;
        private decimal _retenuespeciale;
        private decimal _massesalariale;
        private decimal _chargespatronales;
        private decimal _professionalDeduction;

        // Champs pour la persistance (base de données, JSON)
        private decimal _netSalaryAfterTax;
        private decimal _salaireNetaPayer;

        /// <summary>
        /// Identifiant unique du calcul
        /// </summary>
        public string Id
        {
            get => _id;
            set
            {
                if (_id != value)
                {
                    _id = value;
                    OnPropertyChanged();
                }
            }
        }

        /// <summary>
        /// Employé concerné par le calcul
        /// </summary>
        public Employee Employee
        {
            get => _employee;
            set
            {
                if (_employee != value)
                {
                    _employee = value;
                    OnPropertyChanged();
                }
            }
        }

        /// <summary>
        /// Date du calcul
        /// </summary>
        public DateTime CalculationDate
        {
            get => _calculationDate;
            set
            {
                if (_calculationDate != value)
                {
                    _calculationDate = value;
                    OnPropertyChanged();
                }
            }
        }

        /// <summary>
        /// Salaire brut (mensuel ou annuel selon le contexte)
        /// </summary>
        public decimal SalaireBrut
        {
            get => _salairebrut;
            set
            {
                if (_salairebrut != value)
                {
                    _salairebrut = value;
                    OnPropertyChanged();
                }
            }
        }

        /// <summary>
        /// Cotisation CNSS suportée par l'employé
        /// </summary>
        public decimal CnssEmployee
        {
            get => _cnssemployee;
            set
            {
                if (_cnssemployee != value)
                {
                    _cnssemployee = value;
                    OnPropertyChanged();
                }
            }
        }

        /// <summary>
        /// Taxe profesionnelle d'apprentissage (TPA)
        /// </summary>
        public decimal TaxePatronale
        {
            get => _taxepatronale;
            set
            {
                if (_taxepatronale != value)
                {
                    _taxepatronale = value;
                    OnPropertyChanged();
                }
            }
        }

        /// <summary>
        /// Assiette réellement soumise à la TPA (module DEC-02) : le salaire brut, diminué
        /// des rubriques que le plan de paie en exonère explicitement. Stockée pour que la
        /// déclaration (module DEC-03) reprenne l'assiette qui a réellement servi, au lieu
        /// de la reconstituer et de risquer un écart.
        /// </summary>
        public decimal AssietteTPA
        {
            get => _assietteTPA;
            set
            {
                if (_assietteTPA != value)
                {
                    _assietteTPA = value;
                    OnPropertyChanged();
                }
            }
        }

        /// <summary>
        /// Charge salarial de l'employé (salaire brut + charges patronales)
        /// </summary>
        public decimal MasseSalariale
        {
            get => _massesalariale;
            set
            {
                if (_massesalariale != value)
                {
                    _massesalariale = value;
                    OnPropertyChanged();
                }
            }
        }

        public decimal TotalRetenue => CnssEmployee + IUTSNet + RetenueSpeciale + RetenuesDiverses;


        /// <summary>
        /// Charges patronales (cotisations sociales, TPA, etc.)
        /// </summary>
        public decimal ChargesPatronales
        {
            get => _chargespatronales;
            set
            {
                if (_chargespatronales != value)
                {
                    _chargespatronales = value;
                    OnPropertyChanged();
                }
            }
        }

        /// <summary>
        /// Cotisation CNSS suportée par l'employeur (part patronale)
        /// </summary>
        public decimal CnssPartPatronale
        {
            get => _cnsspartpatronale;
            set
            {
                if (_cnsspartpatronale != value)
                {
                    _cnsspartpatronale = value;
                    OnPropertyChanged();
                }
            }
        }

        /// <summary>
        /// Retenues diverses issues des variables du mois (branchement PAI-02).
        ///
        /// Elles se déduisent du net à payer APRÈS l'impôt et les cotisations : une
        /// retenue de prêt ou une opposition ne diminue ni la base imposable ni
        /// l'assiette CNSS. Zéro par défaut, donc sans effet sur un calcul sans variables.
        /// </summary>
        public decimal RetenuesDiverses
        {
            get => _retenuesDiverses;
            set
            {
                if (_retenuesDiverses != value)
                {
                    _retenuesDiverses = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(SalaireNetaPayer));
                    OnPropertyChanged(nameof(TotalRetenue));
                }
            }
        }

        /// <summary>
        /// Total des gains apportés par les variables du mois, tous traitements confondus.
        /// Conservé pour que le bulletin puisse distinguer le salaire permanent de ce que
        /// le mois a ajouté — sans quoi un brut inhabituel resterait inexpliqué.
        /// </summary>
        public decimal GainsVariables
        {
            get => _gainsVariables;
            set
            {
                if (_gainsVariables != value)
                {
                    _gainsVariables = value;
                    OnPropertyChanged();
                }
            }
        }

        /// <summary>
        /// Retenue opérée sur le brut au titre des absences non rémunérées (module PAI-04).
        ///
        /// À la différence des retenues diverses, celle-ci s'impute AVANT l'impôt et les
        /// cotisations : le salarié n'a pas perçu ce salaire, il n'a donc pas à en être
        /// imposé. Conservée ici pour que le bulletin puisse afficher la ligne qui
        /// explique un brut inférieur au salaire contractuel.
        /// </summary>
        public decimal RetenueAbsences
        {
            get => _retenueAbsences;
            set
            {
                if (_retenueAbsences != value)
                {
                    _retenueAbsences = value;
                    OnPropertyChanged();
                }
            }
        }

        /// <summary>
        /// Assiette réellement soumise à cotisation : le salaire brut, ramené au plafond
        /// mensuel de la CNSS s'il le dépasse (module DEC-01).
        ///
        /// Elle était jusqu'ici implicite, recalculée à chaque besoin. La stocker permet à
        /// la déclaration de reprendre l'assiette qui a réellement servi, au lieu de la
        /// reconstituer et de risquer un écart.
        /// </summary>
        public decimal AssietteCnss
        {
            get => _assietteCnss;
            set
            {
                if (_assietteCnss != value)
                {
                    _assietteCnss = value;
                    OnPropertyChanged();
                }
            }
        }

        /// <summary>Part patronale — branche prestations familiales (module DEC-01).</summary>
        public decimal CnssPrestationsFamiliales
        {
            get => _cnssPrestationsFamiliales;
            set
            {
                if (_cnssPrestationsFamiliales != value)
                {
                    _cnssPrestationsFamiliales = value;
                    OnPropertyChanged();
                }
            }
        }

        /// <summary>Part patronale — branche risques professionnels (module DEC-01).</summary>
        public decimal CnssRisquesProfessionnels
        {
            get => _cnssRisquesProfessionnels;
            set
            {
                if (_cnssRisquesProfessionnels != value)
                {
                    _cnssRisquesProfessionnels = value;
                    OnPropertyChanged();
                }
            }
        }

        /// <summary>Part patronale — branche pensions (module DEC-01).</summary>
        public decimal CnssPensionsPatronal
        {
            get => _cnssPensionsPatronal;
            set
            {
                if (_cnssPensionsPatronal != value)
                {
                    _cnssPensionsPatronal = value;
                    OnPropertyChanged();
                }
            }
        }

        /// <summary>
        /// Retenue spéciale
        /// </summary>
        public decimal RetenueSpeciale
        {
            get => _retenuespeciale;
            set
            {
                if (_retenuespeciale != value)
                {
                    _retenuespeciale = value;
                    OnPropertyChanged();
                }
            }
        }

        /// <summary>
        /// Revenu net imposable après déductions
        /// </summary>
        public decimal BaseIUTSHorsDeduction
        {
            get => _baseIUTShorsdeduction;
            set
            {
                if (_baseIUTShorsdeduction != value)
                {
                    _baseIUTShorsdeduction = value;
                    OnPropertyChanged();
                }
            }
        }

        /// <summary>
        /// Montant de l'impôt calculé
        /// </summary>
        public decimal IUTSNet
        {
            get => _iutsnet;
            set
            {
                if (_iutsnet != value)
                {
                    _iutsnet = value;
                    OnPropertyChanged();
                }
            }
        }

        /// <summary>
        /// Taux moyen d'imposition en pourcentage
        /// </summary>
        public decimal TaxRate
        {
            get => _taxRate;
            set
            {
                if (_taxRate != value)
                {
                    _taxRate = value;
                    OnPropertyChanged();
                }
            }
        }

        /// <summary>
        /// Réduction pour charges familiales
        /// </summary>
        public decimal FamilyChargeReduction
        {
            get => _familyChargeReduction;
            set
            {
                if (_familyChargeReduction != value)
                {
                    _familyChargeReduction = value;
                    OnPropertyChanged();
                }
            }
        }

        /// <summary>
        /// Montant exonéré (indemnités dans les limites légales)
        /// </summary>
        public decimal ExemptAmount
        {
            get => _exemptAmount;
            set
            {
                if (_exemptAmount != value)
                {
                    _exemptAmount = value;
                    OnPropertyChanged();
                }
            }
        }

        /// <summary>
        /// Abattement forfaitaire pour frais et charges professionnels
        /// (20% ou 25% du salaire de base selon la catégorie, Art.111 CGI)
        /// </summary>
        public decimal ProfessionalDeduction
        {
            get => _professionalDeduction;
            set
            {
                if (_professionalDeduction != value)
                {
                    _professionalDeduction = value;
                    OnPropertyChanged();
                }
            }
        }

        /// <summary>
        /// Détails du calcul étape par étape
        /// </summary>
        public string CalculationDetails
        {
            get => _calculationDetails;
            set
            {
                if (_calculationDetails != value)
                {
                    _calculationDetails = value;
                    OnPropertyChanged();
                }
            }
        }

        /// <summary>
        /// Indique si le calcul est annuel (true) ou mensuel (false)
        /// </summary>
        public bool IsAnnual
        {
            get => _isAnnual;
            set
            {
                if (_isAnnual != value)
                {
                    _isAnnual = value;
                    OnPropertyChanged();
                }
            }
        }

        /// <summary>
        /// Indique si le calcul est une régularisation annuelle
        /// </summary>
        public bool IsRegularization
        {
            get => _isRegularization;
            set
            {
                if (_isRegularization != value)
                {
                    _isRegularization = value;
                    OnPropertyChanged();
                }
            }
        }

        /// <summary>
        /// Différence d'impôt lors d'une régularisation (positif = à payer, négatif = à rembourser)
        /// </summary>
        public decimal TaxDifference
        {
            get => _taxDifference;
            set
            {
                if (_taxDifference != value)
                {
                    _taxDifference = value;
                    OnPropertyChanged();
                }
            }
        }

        /// <summary>
        /// Salaire net après impôt (pour la persistance)
        /// </summary>
        public decimal NetSalaryAfterTaxPersisted
        {
            get => _netSalaryAfterTax;
            set
            {
                if (_netSalaryAfterTax != value)
                {
                    _netSalaryAfterTax = value;
                    OnPropertyChanged();
                }
            }
        }

        /// <summary>
        /// Salaire net à payer à l'employé (pour la persistance)
        /// </summary>
        public decimal SalaireNetaPayerPersisted
        {
            get => _salaireNetaPayer;
            set
            {
                if (_salaireNetaPayer != value)
                {
                    _salaireNetaPayer = value;
                    OnPropertyChanged();
                }
            }
        }

        /// <summary>
        /// Salaire net après impôt (calculé dynamiquement)
        /// </summary>
        public decimal NetSalaryAfterTax => SalaireBrut - IUTSNet;

        /// <summary>
        /// Salaire net à payer à l'employé (calculé dynamiquement)
        /// </summary>
        public decimal SalaireNetaPayer => NetSalaryAfterTax - CnssEmployee - RetenueSpeciale - RetenuesDiverses;

        /// <summary>
        /// Constructeur par défaut
        /// </summary>
        public TaxCalculation()
        {
            CalculationDate = DateTime.Now;
        }

        /// <summary>
        /// Constructeur avec un employé
        /// </summary>
        /// <param name="employee">Employé pour lequel calculer l'impôt</param>
        public TaxCalculation(Employee employee) : this()
        {
            Employee = employee;
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}