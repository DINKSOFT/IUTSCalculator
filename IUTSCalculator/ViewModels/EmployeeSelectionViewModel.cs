using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;
using IUTSCalculator.Models;
using IUTSCalculator.Services;
using IUTSCalculator.Helpers;
using System.Threading.Tasks;

namespace IUTSCalculator.ViewModels
{
    /// <summary>
    /// ViewModel spécialisé dans la recherche et la sélection d'employés
    /// </summary>
    public class EmployeeSelectionViewModel : BaseViewModel
    {
        private readonly DataService _dataService;
        private string _filtreSalarie;
        private Employee _salarieASelectionner;

        public ObservableCollection<Employee> Employees { get; } = new ObservableCollection<Employee>();

        public string FiltreSalarie
        {
            get => _filtreSalarie;
            set
            {
                if (SetProperty(ref _filtreSalarie, value))
                {
                    OnPropertyChanged(nameof(HasFiltreSalarie));
                    OnPropertyChanged(nameof(SalariesFiltres));
                }
            }
        }

        public bool HasFiltreSalarie => !string.IsNullOrWhiteSpace(FiltreSalarie);

        public IList<Employee> SalariesFiltres
        {
            get
            {
                if (string.IsNullOrWhiteSpace(FiltreSalarie)) return Employees;

                string q = FiltreSalarie.Trim();
                return Employees.Where(s =>
                    (s.LastName != null && s.LastName.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0)
                    || (s.FirstName != null && s.FirstName.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0)
                    || (s.Matricule != null && s.Matricule.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0)
                    || (s.Fonction != null && s.Fonction.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0))
                    .ToList();
            }
        }

        public Employee SalarieASelectionner
        {
            get => _salarieASelectionner;
            set => SetProperty(ref _salarieASelectionner, value);
        }

        public ICommand ClearFiltreSalarieCommand { get; private set; }

        public EmployeeSelectionViewModel(DataService dataService)
        {
            _dataService = dataService ?? throw new ArgumentNullException(nameof(dataService));

            ClearFiltreSalarieCommand = new RelayCommand(() => FiltreSalarie = string.Empty);
        }

        /// <summary>
        /// Rafraîchit la liste des employés depuis la base de données
        /// </summary>
        public async Task RefreshEmployeesAsync()
        {
            try
            {
                var employesActuels = await _dataService.LoadEmployeesAsync();
                string idRetenu = SalarieASelectionner?.EmployeeId;

                // Synchronisation intelligente
                var idsActuels = new HashSet<string>(employesActuels.Select(e => e.EmployeeId), StringComparer.OrdinalIgnoreCase);
                for (int i = Employees.Count - 1; i >= 0; i--)
                {
                    if (!idsActuels.Contains(Employees[i].EmployeeId))
                        Employees.RemoveAt(i);
                }

                foreach (var employe in employesActuels)
                {
                    if (!Employees.Any(e => string.Equals(e.EmployeeId, employe.EmployeeId, StringComparison.OrdinalIgnoreCase)))
                    {
                        Employees.Add(employe);
                    }
                }

                SalarieASelectionner = idRetenu != null
                    ? Employees.FirstOrDefault(e => string.Equals(e.EmployeeId, idRetenu, StringComparison.OrdinalIgnoreCase))
                    : null;

                OnPropertyChanged(nameof(SalariesFiltres));
            }
            catch (Exception)
            {
                // La gestion d'erreur est généralement remontée au ViewModel parent
            }
        }
    }
}
