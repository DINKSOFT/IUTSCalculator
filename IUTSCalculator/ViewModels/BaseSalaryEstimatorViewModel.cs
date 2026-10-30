using System;
using System.Collections.ObjectModel;
using System.Windows.Input;
using IUTSCalculator.Helpers;
using IUTSCalculator.Models;
using IUTSCalculator.Services;
using System.Threading.Tasks;

namespace IUTSCalculator.ViewModels
{
    /// <summary>
    /// ViewModel pour l'estimation du salaire de base à partir du net et l'export du calcul d'impôt
    /// </summary>
    public class BaseSalaryEstimatorViewModel : BaseViewModel
    {
        private readonly DataService _dataService;
        private readonly TaxCalculatorService _taxCalculatorService;
        public static bool CanEditData => UserSession.Instance.CanEdit;
        public static bool IsAdmin => UserSession.Instance.IsAdmin;
        public static bool IsReadOnly => UserSession.Instance.IsReadOnly;

        public BaseSalaryEstimatorViewModel(DataService dataService, TaxCalculatorService taxCalculatorService)
        {
            _dataService = dataService ?? throw new ArgumentNullException(nameof(dataService));
            _taxCalculatorService = taxCalculatorService ?? throw new ArgumentNullException(nameof(taxCalculatorService));
            Title = "Estimation du salaire de base";
            InitializeCommands();
        }

        public ObservableCollection<string> EmployeeCategories => _dataService.EmployeeCategories;

        // Entrées
        private decimal _netSalary;
        public decimal NetSalary
        {
            get => _netSalary;
            set => SetProperty(ref _netSalary, value);
        }

        private int _familyCharges;
        public int FamilyCharges
        {
            get => _familyCharges;
            set => SetProperty(ref _familyCharges, value);
        }

        private string _category;
        public string Category
        {
            get => _category;
            set => SetProperty(ref _category, value);
        }

        private bool _isSpouseWorking;
        public bool IsSpouseWorking
        {
            get => _isSpouseWorking;
            set => SetProperty(ref _isSpouseWorking, value);
        }

        // Sorties
        private decimal _estimatedBaseSalary;
        public decimal EstimatedBaseSalary
        {
            get => _estimatedBaseSalary;
            set => SetProperty(ref _estimatedBaseSalary, value);
        }

        private decimal _estimatedHousing;
        public decimal EstimatedHousing
        {
            get => _estimatedHousing;
            set => SetProperty(ref _estimatedHousing, value);
        }

        private decimal _estimatedFunction;
        public decimal EstimatedFunction
        {
            get => _estimatedFunction;
            set => SetProperty(ref _estimatedFunction, value);
        }

        private decimal _estimatedTransport;
        public decimal EstimatedTransport
        {
            get => _estimatedTransport;
            set => SetProperty(ref _estimatedTransport, value);
        }

        private decimal _estimatedIUTS;
        public decimal EstimatedIUTS
        {
            get => _estimatedIUTS;
            set => SetProperty(ref _estimatedIUTS, value);
        }

        private string _result;
        public string Result
        {
            get => _result;
            set => SetProperty(ref _result, value);
        }

        public ICommand EstimateCommand { get; private set; }
        public ICommand ExportToExcelCommand { get; private set; }

        /// <summary>
        /// Ouvre une fiche de création d'employé reprenant le résultat de l'estimation.
        /// Active seulement après une estimation réussie.
        /// </summary>
        public ICommand CreerFicheEmployeCommand { get; private set; }

        /// <summary>
        /// Levé avec la fiche pré-remplie : l'écran principal y répond en ouvrant la
        /// gestion des employés, qui reste seule à enregistrer (voir MainViewModel).
        /// </summary>
        public event EventHandler<Employee> CreationFicheDemandee;

        private TaxCalculation _lastTaxCalculation;

        /// <summary>
        /// Hypothèses et résultats de la dernière estimation réussie, figés à cet
        /// instant : modifier ensuite les champs sans relancer « Estimer » ne doit
        /// pas produire une fiche incohérente avec le résultat affiché.
        /// </summary>
        private Employee _derniereEstimation;

        public bool EstimationDisponible => _derniereEstimation != null;

        /// <summary>
        /// Construit une fiche employé vierge portant les éléments de la dernière
        /// estimation. Nulle tant qu'aucune estimation n'a abouti. Chaque appel rend
        /// une nouvelle instance.
        /// </summary>
        public Employee ConstruireFicheEmploye()
        {
            var source = _derniereEstimation;
            if (source == null) return null;

            return new Employee
            {
                Category = source.Category,
                FamilyCharges = source.FamilyCharges,
                IsSpouseWorking = source.IsSpouseWorking,
                // Un conjoint salarié suppose un mariage ; sinon la situation reste
                // à renseigner plutôt que d'être devinée.
                SituationMatrimoniale = source.IsSpouseWorking ? "Marié(e)" : null,
                BaseSalary = source.BaseSalary,
                HousingAllowance = source.HousingAllowance,
                FunctionAllowance = source.FunctionAllowance,
                TransportAllowance = source.TransportAllowance
            };
        }

        private void CreerFicheEmploye()
        {
            var fiche = ConstruireFicheEmploye();
            if (fiche == null) return;

            CreationFicheDemandee?.Invoke(this, fiche);
        }

        private void InitializeCommands()
        {
            CreerFicheEmployeCommand = new RelayCommand(
                execute: CreerFicheEmploye,
                canExecute: () => !IsBusy && EstimationDisponible && CanEditData
            );

            EstimateCommand = new RelayCommand(
                execute: async () => await EstimateBaseSalaryAsync(),
                canExecute: () => !IsBusy
            );

            ExportToExcelCommand = new RelayCommand(
                execute: async () => await ExportToExcelAsync(),
                canExecute: () => !IsBusy && _lastTaxCalculation != null
            );
        }

        /// <summary>Lance l'estimation (EstimateCommand) ; publique pour pouvoir l'attendre dans les tests.</summary>
        public async Task EstimateBaseSalaryAsync()
        {
            try
            {
                IsBusy = true;
                StatusMessage = "Estimation en cours...";
                _derniereEstimation = null;
                OnPropertyChanged(nameof(EstimationDisponible));

                await Task.Run(() =>
                {
                    // Création de l'objet Employee à partir des entrées du ViewModel
                    var employee = new Employee
                    {
                        Category = Category,
                        FamilyCharges = FamilyCharges,
                        IsSpouseWorking = IsSpouseWorking
                    };

                    // Appel de la méthode d'estimation avec les bons paramètres
                    var result = _taxCalculatorService.EstimateBaseSalaryWithConstraints(
                        NetSalary,
                        employee
                    );

                    EstimatedBaseSalary = result.baseSalary;
                    EstimatedHousing = result.housing;
                    EstimatedFunction = result.function;
                    EstimatedTransport = result.transport;
                    EstimatedIUTS = result.iuts;

                    // Pour affichage détaillé
                    employee.BaseSalary = EstimatedBaseSalary;
                    employee.HousingAllowance = EstimatedHousing;
                    employee.FunctionAllowance = EstimatedFunction;
                    employee.TransportAllowance = EstimatedTransport;

                    var taxCalculation = _taxCalculatorService.CalculateMonthlyTax(employee);
                    _lastTaxCalculation = taxCalculation;
                    _derniereEstimation = employee;

                    Result = $"Salaire de base estimé : {EstimatedBaseSalary:N0} FCFA\n" +
                             $"Indemnité logement : {EstimatedHousing:N0} FCFA\n" +
                             $"Indemnité fonction : {EstimatedFunction:N0} FCFA\n" +
                             $"Indemnité transport : {EstimatedTransport:N0} FCFA\n" +
                             $"IUTS mensuel : {EstimatedIUTS:N0} FCFA\n" +
                             $"Salaire net à payer : {taxCalculation.SalaireNetaPayer:N0} FCFA";
                });

                StatusMessage = "Estimation terminée";
            }
            catch (Exception ex)
            {
                Result = $"Erreur : {ex.Message}";
                StatusMessage = $"Erreur : {ex.Message}";
            }
            finally
            {
                IsBusy = false;
                OnPropertyChanged(nameof(EstimationDisponible));
                CommandManager.InvalidateRequerySuggested();
            }
        }

        private async Task ExportToExcelAsync()
        {
            if (_lastTaxCalculation == null)
            {
                StatusMessage = "Aucun calcul à exporter. Veuillez d'abord estimer le salaire de base.";
                return;
            }

            try
            {
                IsBusy = true;
                StatusMessage = "Exportation Excel en cours...";

                await _dataService.ExportToExcelAsync(_lastTaxCalculation);

                StatusMessage = "Exportation Excel terminée.";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Erreur lors de l'export : {ex.Message}";
            }
            finally
            {
                IsBusy = false;
            }
        }
    }
}
