using System;
using System.ComponentModel;
using System.Globalization;
using System.Windows.Input;
using IUTSCalculator.Models;
using IUTSCalculator.Services;

namespace IUTSCalculator.ViewModels
{
    public class BaseSalaryEstimatorViewModel : INotifyPropertyChanged
    {
        private readonly TaxCalculatorService _taxCalculatorService;

        public BaseSalaryEstimatorViewModel(TaxCalculatorService taxCalculatorService)
        {
            _taxCalculatorService = taxCalculatorService;
            EstimateCommand = new RelayCommand(EstimateBaseSalary);
        }

        // Propriétés liées à la saisie utilisateur
        private decimal _netSalary;
        public decimal NetSalary
        {
            get => _netSalary;
            set { _netSalary = value; OnPropertyChanged(nameof(NetSalary)); }
        }

        private decimal _housingAllowance;
        public decimal HousingAllowance
        {
            get => _housingAllowance;
            set { _housingAllowance = value; OnPropertyChanged(nameof(HousingAllowance)); }
        }

        private decimal _functionAllowance;
        public decimal FunctionAllowance
        {
            get => _functionAllowance;
            set { _functionAllowance = value; OnPropertyChanged(nameof(FunctionAllowance)); }
        }

        private decimal _transportAllowance;
        public decimal TransportAllowance
        {
            get => _transportAllowance;
            set { _transportAllowance = value; OnPropertyChanged(nameof(TransportAllowance)); }
        }

        private int _familyCharges;
        public int FamilyCharges
        {
            get => _familyCharges;
            set { _familyCharges = value; OnPropertyChanged(nameof(FamilyCharges)); }
        }

        private string _result;
        public string Result
        {
            get => _result;
            set { _result = value; OnPropertyChanged(nameof(Result)); }
        }

        public ICommand EstimateCommand { get; }

        private void EstimateBaseSalary(object obj)
        {
            try
            {
                var employee = new Employee
                {
                    FamilyCharges = this.FamilyCharges,
                    IsHigherCategory = false, // à adapter
                    Category = "B",           // à adapter
                    IsSpouseWorking = false   // à adapter
                };

                decimal baseSalary = _taxCalculatorService.CalculateBaseSalaryFromNet(
                    NetSalary, HousingAllowance, FunctionAllowance, TransportAllowance, employee);

                Result = $"Salaire de base estimé : {baseSalary:N0} FCFA";
            }
            catch (Exception ex)
            {
                Result = $"Erreur : {ex.Message}";
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    // Simple implémentation ICommand
    public class RelayCommand : ICommand
    {
        private readonly Action<object> _execute;
        private readonly Predicate<object> _canExecute;

        public RelayCommand(Action<object> execute, Predicate<object> canExecute = null)
        {
            _execute = execute;
            _canExecute = canExecute;
        }

        public bool CanExecute(object parameter) => _canExecute == null || _canExecute(parameter);
        public void Execute(object parameter) => _execute(parameter);
        public event EventHandler CanExecuteChanged
        {
            add { CommandManager.RequerySuggested += value; }
            remove { CommandManager.RequerySuggested -= value; }
        }
    }
}