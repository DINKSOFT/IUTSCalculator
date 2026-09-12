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

        private TaxCalculation _lastTaxCalculation;

        private void InitializeCommands()
        {
            EstimateCommand = new RelayCommand(
                execute: async () => await EstimateBaseSalaryAsync(),
                canExecute: () => !IsBusy
            );

            ExportToExcelCommand = new RelayCommand(
                execute: async () => await ExportToExcelAsync(),
                canExecute: () => !IsBusy && _lastTaxCalculation != null
            );
        }

        private async Task EstimateBaseSalaryAsync()
        {
            try
            {
                IsBusy = true;
                StatusMessage = "Estimation en cours...";

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
