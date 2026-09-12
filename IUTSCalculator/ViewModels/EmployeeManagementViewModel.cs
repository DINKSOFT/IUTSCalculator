using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using IUTSCalculator.Helpers;
using IUTSCalculator.Models;
using IUTSCalculator.Services;

namespace IUTSCalculator.ViewModels
{
    public class EmployeeManagementViewModel : BaseViewModel
    {
        private readonly DataService _dataService;

        public ObservableCollection<Employee> Employees { get; } = new ObservableCollection<Employee>();

        private Employee _selectedEmployee;
        public Employee SelectedEmployee
        {
            get => _selectedEmployee;
            set
            {
                if (SetProperty(ref _selectedEmployee, value))
                {
                    // Copie complète pour l'édition : voir Employee.Clone(), qui évite
                    // que les champs recopiés à la main ne soient oubliés.
                    EditEmployee = value != null ? value.Clone() : null;
                    OnPropertyChanged(nameof(EstEnEdition));
                    (DeleteEmployeeCommand as RelayCommand)?.RaiseCanExecuteChanged();
                    (EditEmployeeCommand as RelayCommand)?.RaiseCanExecuteChanged();
                }
            }
        }

        private Employee _editEmployee;
        public Employee EditEmployee
        {
            get => _editEmployee;
            set
            {
                if (SetProperty(ref _editEmployee, value))
                    OnPropertyChanged(nameof(FicheCourante));
            }
        }

        private Employee _newEmployee = new Employee();
        public Employee NewEmployee
        {
            get => _newEmployee;
            set
            {
                if (SetProperty(ref _newEmployee, value))
                    OnPropertyChanged(nameof(FicheCourante));
            }
        }

        /// <summary>
        /// Le salarié affiché dans la fiche : celui qui est sélectionné dans la liste,
        /// ou la fiche vierge quand aucun ne l'est. Un seul formulaire sert donc à la
        /// création comme à la modification — seuls les boutons du bas changent.
        /// </summary>
        public Employee FicheCourante => EditEmployee ?? NewEmployee;

        /// <summary>Vrai lorsqu'un salarié existant est sélectionné (mode modification).</summary>
        public bool EstEnEdition => SelectedEmployee != null;

        /// <summary>
        /// Total des salaires de base de l'effectif, affiché en pied de liste : c'est
        /// le contrôle le plus rapide pour repérer une saisie aberrante.
        /// </summary>
        public decimal TotalSalaireBase
        {
            get { return Employees.Sum(e => e.BaseSalary); }
        }

        private void RafraichirTotaux()
        {
            OnPropertyChanged(nameof(TotalSalaireBase));
        }

        public ObservableCollection<string> EmployeeCategories => _dataService.EmployeeCategories;

        /// <summary>Valeurs proposées pour la situation matrimoniale du salarié.</summary>
        public ObservableCollection<string> SituationsMatrimoniales { get; } = new ObservableCollection<string>
        {
            "Célibataire",
            "Marié(e)",
            "Divorcé(e)",
            "Veuf(ve)"
        };

        public ICommand LoadEmployeesCommand { get; }
        public ICommand AddEmployeeCommand { get; }
        public ICommand EditEmployeeCommand { get; }
        public ICommand DeleteEmployeeCommand { get; }
        public ICommand ClearNewEmployeeCommand { get; }
        public ICommand ImportEmployeesCommand { get; }

        /// <summary>Repasse la fiche en création : désélectionne la liste et la vide.</summary>
        public ICommand NouveauSalarieCommand { get; }

        public EmployeeManagementViewModel(DataService dataService)
        {
            _dataService = dataService ?? throw new ArgumentNullException(nameof(dataService));
            Title = "Gestion des employés";

            LoadEmployeesCommand = new RelayCommand(async () => await LoadEmployeesAsync());
            AddEmployeeCommand = new RelayCommand(async () => await AddEmployeeAsync(), CanAddEmployee);
            EditEmployeeCommand = new RelayCommand(async () => await EditEmployeeAsync(), () => SelectedEmployee != null && EditEmployee != null);
            DeleteEmployeeCommand = new RelayCommand(async () => await DeleteEmployeeAsync(), () => SelectedEmployee != null);
            ClearNewEmployeeCommand = new RelayCommand(ClearNewEmployee);
            ImportEmployeesCommand = new RelayCommand(async () => await ImportEmployeesAsync());
            NouveauSalarieCommand = new RelayCommand(NouveauSalarie);

            _ = LoadEmployeesAsync();
        }

        private async Task LoadEmployeesAsync()
        {
            try
            {
                IsBusy = true;
                StatusMessage = "Chargement des employés...";
                Employees.Clear();
                var list = await _dataService.LoadEmployeesAsync();
                foreach (var emp in list)
                    Employees.Add(emp);
                RafraichirTotaux();
                StatusMessage = $"{Employees.Count} employés chargés.";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Erreur lors du chargement : {ex.Message}";
            }
            finally
            {
                IsBusy = false;
            }
        }

        private bool CanAddEmployee()
        {
            return !IsBusy
                && !string.IsNullOrWhiteSpace(NewEmployee.FirstName)
                && !string.IsNullOrWhiteSpace(NewEmployee.LastName)
                && !string.IsNullOrWhiteSpace(NewEmployee.Category);
        }

        private async Task AddEmployeeAsync()
        {
            try
            {
                IsBusy = true;
                StatusMessage = "Ajout de l'employé...";
                if (string.IsNullOrEmpty(NewEmployee.EmployeeId))
                    NewEmployee.EmployeeId = Guid.NewGuid().ToString();

                await _dataService.AddEmployeeAsync(NewEmployee);

                Employees.Add(NewEmployee.Clone());
                RafraichirTotaux();

                StatusMessage = "Employé ajouté avec succès.";
                ClearNewEmployee();
            }
            catch (Exception ex)
            {
                StatusMessage = $"Erreur lors de l'ajout : {ex.Message}";
            }
            finally
            {
                IsBusy = false;
            }
        }

        private async Task EditEmployeeAsync()
        {
            if (SelectedEmployee == null || EditEmployee == null)
                return;

            try
            {
                IsBusy = true;
                StatusMessage = "Modification de l'employé...";

                await _dataService.UpdateEmployeeAsync(EditEmployee);

                // Mettre à jour la liste locale
                var index = Employees.IndexOf(SelectedEmployee);
                if (index >= 0)
                    Employees[index] = EditEmployee.Clone();
                RafraichirTotaux();

                StatusMessage = "Employé modifié avec succès.";
                SelectedEmployee = null;
            }
            catch (Exception ex)
            {
                StatusMessage = $"Erreur lors de la modification : {ex.Message}";
            }
            finally
            {
                IsBusy = false;
            }
        }

        private async Task DeleteEmployeeAsync()
        {
            if (SelectedEmployee == null)
                return;

            try
            {
                IsBusy = true;
                StatusMessage = "Suppression de l'employé...";

                await _dataService.DeleteEmployeeAsync(SelectedEmployee.EmployeeId);

                Employees.Remove(SelectedEmployee);
                RafraichirTotaux();
                StatusMessage = "Employé supprimé avec succès.";
                SelectedEmployee = null;
            }
            catch (Exception ex)
            {
                StatusMessage = $"Erreur lors de la suppression : {ex.Message}";
            }
            finally
            {
                IsBusy = false;
            }
        }

        private async Task ImportEmployeesAsync()
        {
            try
            {
                var dialog = new Microsoft.Win32.OpenFileDialog
                {
                    Filter = "Fichiers JSON (*.json)|*.json|Tous les fichiers (*.*)|*.*",
                    Title = "Importer des employés"
                };
                if (dialog.ShowDialog() == true)
                {
                    IsBusy = true;
                    StatusMessage = "Importation en cours...";
                    await _dataService.ImportEmployeesFromFileAsync(dialog.FileName);
                    await LoadEmployeesAsync();
                    StatusMessage = "Importation terminée avec succès.";
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"Erreur lors de l'import : {ex.Message}";
            }
            finally
            {
                IsBusy = false;
            }
        }

        private void ClearNewEmployee()
        {
            NewEmployee = new Employee();
        }

        private void NouveauSalarie()
        {
            SelectedEmployee = null;
            NewEmployee = new Employee();
            StatusMessage = "Nouvelle fiche.";
        }
    }
}