using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace IUTSCalculator.Models
{
    /// <summary>
    /// Représente un employé avec ses informations de base et fiscales
    /// </summary>
    public class Employee : INotifyPropertyChanged
    {
        private string _firstName;
        private string _lastName;
        private string _employeeId;
        private decimal _baseSalary;
        private decimal _housingAllowance;
        private decimal _transportAllowance;
        private decimal _functionAllowance;
        private int _familyCharges;
        private bool _isSpouseWorking;
        private decimal _pensionContribution;
        private string _category;
        private bool _isHigherCategory;

        /// <summary>
        /// Prénom de l'employé
        /// </summary>
        public string FirstName
        {
            get => _firstName;
            set
            {
                if (_firstName != value)
                {
                    _firstName = value;
                    OnPropertyChanged();
                }
            }
        }

        /// <summary>
        /// Nom de l'employé
        /// </summary>
        public string LastName
        {
            get => _lastName;
            set
            {
                if (_lastName != value)
                {
                    _lastName = value;
                    OnPropertyChanged();
                }
            }
        }

        /// <summary>
        /// Identificateur unique de l'employé
        /// </summary>
        public string EmployeeId
        {
            get => _employeeId;
            set
            {
                if (_employeeId != value)
                {
                    _employeeId = value;
                    OnPropertyChanged();
                }
            }
        }

        /// <summary>
        /// Salaire de base de l'employé
        /// </summary>
        public decimal BaseSalary
        {
            get => _baseSalary;
            set
            {
                if (_baseSalary != value)
                {
                    _baseSalary = value;
                    OnPropertyChanged();
                    // Recalculer les plafonds d'indemnités
                    OnPropertyChanged(nameof(MaxHousingAllowance));
                    OnPropertyChanged(nameof(MaxFunctionAllowance));
                    OnPropertyChanged(nameof(MaxTransportAllowance));
                }
            }
        }

        /// <summary>
        /// Indemnité de logement
        /// </summary>
        public decimal HousingAllowance
        {
            get => _housingAllowance;
            set
            {
                if (_housingAllowance != value)
                {
                    _housingAllowance = value;
                    OnPropertyChanged();
                }
            }
        }

        /// <summary>
        /// Indemnité de transport
        /// </summary>
        public decimal TransportAllowance
        {
            get => _transportAllowance;
            set
            {
                if (_transportAllowance != value)
                {
                    _transportAllowance = value;
                    OnPropertyChanged();
                }
            }
        }

        /// <summary>
        /// Indemnité de fonction
        /// </summary>
        public decimal FunctionAllowance
        {
            get => _functionAllowance;
            set
            {
                if (_functionAllowance != value)
                {
                    _functionAllowance = value;
                    OnPropertyChanged();
                }
            }
        }

        /// <summary>
        /// Nombre de charges familiales
        /// </summary>
        public int FamilyCharges
        {
            get => _familyCharges;
            set
            {
                if (_familyCharges != value)
                {
                    _familyCharges = value;
                    OnPropertyChanged();
                }
            }
        }

        /// <summary>
        /// Indique si le conjoint travaille
        /// </summary>
        public bool IsSpouseWorking
        {
            get => _isSpouseWorking;
            set
            {
                if (_isSpouseWorking != value)
                {
                    _isSpouseWorking = value;
                    OnPropertyChanged();
                }
            }
        }

        /// <summary>
        /// Cotisation de pension/retraite
        /// </summary>
        public decimal PensionContribution
        {
            get => _pensionContribution;
            set
            {
                if (_pensionContribution != value)
                {
                    _pensionContribution = value;
                    OnPropertyChanged();
                }
            }
        }

        /// <summary>
        /// Catégorie du salarié (P, A, B, etc.)
        /// </summary>
        public string Category
        {
            get => _category;
            set
            {
                if (_category != value)
                {
                    _category = value;
                    DetermineIfHigherCategory();
                    OnPropertyChanged();
                }
            }
        }

        /// <summary>
        /// Indique si l'employé appartient à une catégorie supérieure
        /// </summary>
        public bool IsHigherCategory
        {
            get => _isHigherCategory;
            private set
            {
                if (_isHigherCategory != value)
                {
                    _isHigherCategory = value;
                    OnPropertyChanged();
                }
            }
        }

        /// <summary>
        /// Plafond de l'indemnité de logement (20% du salaire brut, max 50 000 FCFA)
        /// </summary>
        public decimal MaxHousingAllowance => Math.Min(BaseSalary * 0.2m, 50000m);

        /// <summary>
        /// Plafond de l'indemnité de fonction (5% du salaire brut, max 30 000 FCFA)
        /// </summary>
        public decimal MaxFunctionAllowance => Math.Min(BaseSalary * 0.05m, 30000m);

        /// <summary>
        /// Plafond de l'indemnité de transport (5% du salaire brut, max 20 000 FCFA)
        /// </summary>
        public decimal MaxTransportAllowance => Math.Min(BaseSalary * 0.05m, 20000m);

        /// <summary>
        /// Détermine si l'employé fait partie des catégories supérieures
        /// </summary>
        private void DetermineIfHigherCategory()
        {
            // Les catégories supérieures sont P, A, B et les classes 6, 1 et 2 du secteur public
            string[] higherCategories = { "Cadre Supérieur" };//{ "P", "A", "B", "6", "1", "2" };
            IsHigherCategory = Array.Exists(higherCategories, c => c.Equals(Category, StringComparison.OrdinalIgnoreCase));
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}