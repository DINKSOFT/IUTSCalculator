using System;
using System.Collections.ObjectModel;
using System.Windows.Input;
using IUTSCalculator.Helpers;
using IUTSCalculator.Models;
using IUTSCalculator.Services;

namespace IUTSCalculator.ViewModels
{
    /// <summary>
    /// ViewModel pour l'estimation du salaire de base à partir du net
    /// </summary>
    public class BaseSalaryEstimatorViewModel : BaseViewModel
    {
        private readonly DataService _dataService;
        private readonly TaxCalculatorService _taxCalculatorService;
        public bool CanEditData => UserSession.Instance.CanEdit;
        public bool IsAdmin => UserSession.Instance.IsAdmin;
        public bool IsReadOnly => UserSession.Instance.IsReadOnly;

        public BaseSalaryEstimatorViewModel(DataService dataService, TaxCalculatorService taxCalculatorService)
        {
            _dataService = dataService ?? throw new ArgumentNullException(nameof(dataService));
            _taxCalculatorService = taxCalculatorService ?? throw new ArgumentNullException(nameof(taxCalculatorService));
            Title = "Estimation du salaire de base";
            InitializeCommands();
        }

        public ObservableCollection<string> EmployeeCategories => _dataService.EmployeeCategories;

        private decimal _netSalary;
        public decimal NetSalary
        {
            get => _netSalary;
            set => SetProperty(ref _netSalary, value);
        }

        private decimal _housingAllowance;
        public decimal HousingAllowance
        {
            get => _housingAllowance;
            set => SetProperty(ref _housingAllowance, value);
        }

        private decimal _functionAllowance;
        public decimal FunctionAllowance
        {
            get => _functionAllowance;
            set => SetProperty(ref _functionAllowance, value);
        }

        private decimal _transportAllowance;
        public decimal TransportAllowance
        {
            get => _transportAllowance;
            set => SetProperty(ref _transportAllowance, value);
        }

        private int _familyCharges;
        public int FamilyCharges
        {
            get => _familyCharges;
            set => SetProperty(ref _familyCharges, value);
        }

        private string _result;
        public string Result
        {
            get => _result;
            set => SetProperty(ref _result, value);
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

        public ICommand EstimateCommand { get; private set; }

        private void InitializeCommands()
        {
            EstimateCommand = new RelayCommand(
                execute: async () => await EstimateBaseSalaryAsync(),
                canExecute: () => !IsBusy
            );
        }

        private async System.Threading.Tasks.Task EstimateBaseSalaryAsync()
        {
            try
            {
                IsBusy = true;
                StatusMessage = "Estimation en cours...";

                await System.Threading.Tasks.Task.Run(() =>
                {
                    var employee = new Employee
                    {
                        FamilyCharges = this.FamilyCharges,
                        Category = this.Category,
                        IsSpouseWorking = this.IsSpouseWorking
                    };

                    decimal baseSalary = _taxCalculatorService.CalculateBaseSalaryFromNet(
                        NetSalary, HousingAllowance, FunctionAllowance, TransportAllowance, employee);

                    Result = $"Salaire de base estimé : {baseSalary:N0} FCFA";
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
    }
}