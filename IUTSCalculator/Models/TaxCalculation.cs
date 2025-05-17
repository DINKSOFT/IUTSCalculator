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
        private decimal _taxepatronale;
        private decimal _retenuespeciale;
        private decimal _massesalariale;
        private decimal _chargespatronales;

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
        /// Cotisation CNSS suportée par l'employé
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
        /// 
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
        /// Salaire net après impôt
        /// </summary>
        public decimal NetSalaryAfterTax => SalaireBrut - IUTSNet;        

        /// <summary>
        /// Salaire net à payer à l'employée
        /// </summary>
        public decimal SalaireNetaPayer => NetSalaryAfterTax-CnssEmployee-RetenueSpeciale;

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