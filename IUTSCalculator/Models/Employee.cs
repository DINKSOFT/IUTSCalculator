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
        private decimal _sujetionAllowance;
        private int _familyCharges;
        private bool _isSpouseWorking;
        private decimal _pensionContribution;
        private string _category;
        private bool _isHigherCategory;
        private string _situationMatrimoniale;

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
        /// Fonction de l'employé (poste ou titre)
        /// </summary>
        public string Fonction { get; set; }

        /// <summary>
        /// Date d'entrée de l'employé dans l'entreprise
        /// </summary>
        public DateTime DateEntree { get; set; }

        /// <summary>
        /// Date d'entrée sous forme nullable, pour la liaison avec un DatePicker : une date
        /// non renseignée doit laisser le contrôle vide plutôt qu'afficher 01/01/0001.
        /// C'est la même convention que celle déjà retenue par le service des bulletins.
        /// </summary>
        public DateTime? DateEntreeNullable
        {
            get { return DateEntree == default(DateTime) ? (DateTime?)null : DateEntree; }
            set { DateEntree = value ?? default(DateTime); }
        }


        public decimal TotalIndemnites
        {
            get
            {
                return
                    (HousingAllowance > 0 ? HousingAllowance : 0) +
                    (FunctionAllowance > 0 ? FunctionAllowance : 0) +
                    (TransportAllowance > 0 ? TransportAllowance : 0) +
                    (SujetionAllowance > 0 ? SujetionAllowance : 0);
            }
        }


        /// <summary>
        /// Indemnité de fonction
        /// </summary>
        public decimal SujetionAllowance
        {
            get => _sujetionAllowance;
            set
            {
                if (_sujetionAllowance != value)
                {
                    _sujetionAllowance = value;
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


        public string SituationMatrimoniale
        {
            get => _situationMatrimoniale;
            set
            {
                if (_situationMatrimoniale != value)
                {
                    _situationMatrimoniale = value;
                    OnPropertyChanged(nameof(SituationMatrimoniale));
                }
            }
        }



        /// <summary>
        /// Plafond de l'indemnité de logement (20% du salaire brut, max 75 000 FCFA - Art.106 CGI)
        /// </summary>
        public decimal MaxHousingAllowance => Math.Min(BaseSalary * 0.2m, 75000m);

        /// <summary>
        /// Plafond de l'indemnité de fonction (5% du salaire brut, max 50 000 FCFA - Art.106 CGI)
        /// </summary>
        public decimal MaxFunctionAllowance => Math.Min(BaseSalary * 0.05m, 50000m);

        /// <summary>
        /// Plafond de l'indemnité de transport (5% du salaire brut, max 30 000 FCFA - Art.106 CGI)
        /// </summary>
        public decimal MaxTransportAllowance => Math.Min(BaseSalary * 0.05m, 30000m);

        /// <summary>
        /// Détermine si l'employé fait partie des catégories supérieures
        /// </summary>
        private void DetermineIfHigherCategory()
        {
            // Les catégories supérieures sont P, A, B et les classes 6, 1 et 2 du secteur public
            string[] higherCategories = { "Cadre Supérieur" }; //{ "P", "A", "B", "6", "1", "2" }
            IsHigherCategory = Array.Exists(higherCategories, c => c.Equals(Category, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Copie complète de l'employé.
        ///
        /// Les recopies champ par champ écrites à la main dans les vues omettaient
        /// systématiquement les propriétés ajoutées après coup — indemnité de sujétion,
        /// fonction, date d'entrée, situation matrimoniale — si bien qu'une simple
        /// modification de fiche les remettait à zéro. Passer par cette méthode garantit
        /// qu'aucune propriété ne sera plus perdue, y compris celles ajoutées plus tard.
        ///
        /// <see cref="IsHigherCategory"/> n'est pas recopiée : elle est recalculée
        /// automatiquement par l'affectation de <see cref="Category"/>.
        /// </summary>
        public Employee Clone()
        {
            return new Employee
            {
                EmployeeId = EmployeeId,
                FirstName = FirstName,
                LastName = LastName,
                BaseSalary = BaseSalary,
                HousingAllowance = HousingAllowance,
                TransportAllowance = TransportAllowance,
                FunctionAllowance = FunctionAllowance,
                SujetionAllowance = SujetionAllowance,
                FamilyCharges = FamilyCharges,
                IsSpouseWorking = IsSpouseWorking,
                PensionContribution = PensionContribution,
                Category = Category,
                Fonction = Fonction,
                DateEntree = DateEntree,
                SituationMatrimoniale = SituationMatrimoniale
            };
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}