using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using System.Windows.Input;
using IUTSCalculator.Models;
using IUTSCalculator.Helpers;
using IUTSCalculator.Services;
using System.Windows;
using System.IO;
using System.Web.Script.Serialization;


namespace IUTSCalculator.ViewModels
{
    /// <summary>
    /// ViewModel pour la vue de calcul d'impôt IUTS
    /// </summary>
    public class CalculationViewModel : BaseViewModel
    {
        public bool CanEditData => UserSession.Instance.CanEdit;
        public bool IsAdmin => UserSession.Instance.IsAdmin;
        public bool IsReadOnly => UserSession.Instance.IsReadOnly;
        private readonly TaxCalculatorService _taxCalculatorService;
        public ObservableCollection<string> EmployeeCategories => _dataService.EmployeeCategories;
        private readonly DataService _dataService;
        private Employee _currentEmployee;
        private TaxCalculation _currentCalculation;
        private string _resultMessage;
        private bool _isNewEmployee;        
        private bool _isAnnual;
        private bool _isRegularization;
        private decimal _previousTaxPaid;

        /// <summary>
        /// Événement déclenché lorsqu'un calcul est terminé
        /// </summary>
        public event EventHandler CalculationCompleted;

        /// <summary>
        /// Employé actuellement sélectionné ou créé
        /// </summary>
        public Employee CurrentEmployee
        {
            get => _currentEmployee;
            set => SetProperty(ref _currentEmployee, value);
        }

        /// <summary>
        /// Calcul d'impôt actuel
        /// </summary>
        public TaxCalculation CurrentCalculation
        {
            get => _currentCalculation;
            set => SetProperty(ref _currentCalculation, value);
        }

        /// <summary>
        /// Message de résultat du calcul
        /// </summary>
        public string ResultMessage
        {
            get => _resultMessage;
            set => SetProperty(ref _resultMessage, value);
        }

        /// <summary>
        /// Indique si l'employé courant est nouveau
        /// </summary>
        public bool IsNewEmployee
        {
            get => _isNewEmployee;
            set => SetProperty(ref _isNewEmployee, value);
        }

        /// <summary>
        /// Liste des catégories d'employés disponibles
        /// </summary>
        //public ObservableCollection<string> EmployeeCategories
        //{
        //    get => _employeeCategories;
        //    set => SetProperty(ref _employeeCategories, value);
        //}

        /// <summary>
        /// Indique si le calcul est annuel ou mensuel
        /// </summary>
        public bool IsAnnual
        {
            get => _isAnnual;
            set
            {
                if (SetProperty(ref _isAnnual, value))
                {
                    // Mettre à jour la période de calcul dans le calcul courant
                    if (CurrentCalculation != null)
                    {
                        CurrentCalculation.IsAnnual = value;
                    }
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
                if (SetProperty(ref _isRegularization, value))
                {
                    // Mettre à jour le mode de régularisation dans le calcul courant
                    if (CurrentCalculation != null)
                    {
                        CurrentCalculation.IsRegularization = value;
                    }
                }
            }
        }

        /// <summary>
        /// Montant d'impôt déjà payé (pour les régularisations)
        /// </summary>
        public decimal PreviousTaxPaid
        {
            get => _previousTaxPaid;
            set => SetProperty(ref _previousTaxPaid, value);
        }

        /// <summary>
        /// Indique si un calcul est actuellement chargé
        /// </summary>
        public bool HasCalculation => CurrentCalculation != null;

        // Commandes
        public ICommand CalculateTaxCommand { get; private set; }
        public ICommand SaveCalculationCommand { get; private set; }
        public ICommand CreateNewEmployeeCommand { get; private set; }
        public ICommand ClearFormCommand { get; private set; }
        public ICommand LoadEmployeeCommand { get; private set; }
        public ICommand ExportExcelCommand { get; private set; }

        /// <summary>
        /// Constructeur
        /// </summary>
        /// <param name="taxCalculatorService">Service de calcul d'impôt</param>
        /// <param name="dataService">Service de données</param>
        public CalculationViewModel(TaxCalculatorService taxCalculatorService, DataService dataService)
        {
            _taxCalculatorService = taxCalculatorService ?? throw new ArgumentNullException(nameof(taxCalculatorService));
            _dataService = dataService ?? throw new ArgumentNullException(nameof(dataService));

            Title = "Calcul d'IUTS";

            // Initialiser les collections
            //"P", "A", "B", "C", "D", "E", "F", "G", "1", "2", "3", "4", "5", "6" valeurs initiales
            //EmployeeCategories = new ObservableCollection<string>
            //{
            //    "Cadre Supérieur", "Cadre Inférieur"
            //};

            // Initialiser les commandes
            InitializeCommands();

            // Créer un nouveau calcul par défaut
            CreateNewCalculation();

            // Charger les paramètres asynchrone
            _ = LoadSettingsAsync();
        }

        /// <summary>
        /// Initialise les commandes du ViewModel
        /// </summary>
        private void InitializeCommands()
        {
            CalculateTaxCommand = new RelayCommand(
                execute: async () => await ExecuteCalculateTaxAsync(),
                canExecute: () => !IsBusy && CurrentEmployee != null);

            SaveCalculationCommand = new RelayCommand(
                execute: async () => await ExecuteSaveCalculationAsync(),
                canExecute: () => !IsBusy && CurrentCalculation != null && CurrentCalculation.SalaireBrut > 0);

            CreateNewEmployeeCommand = new RelayCommand(
                execute: ExecuteCreateNewEmployee,
                canExecute: () => !IsBusy);

            ClearFormCommand = new RelayCommand(
                execute: ExecuteClearForm,
                canExecute: () => !IsBusy);

            LoadEmployeeCommand = new RelayCommand(
                execute: async () => await ExecuteLoadEmployeeAsync(),
                canExecute: () => !IsBusy);

            ExportExcelCommand = new RelayCommand(
                execute: async () => await _dataService.ExportToExcelAsync(CurrentCalculation),
                canExecute: () => !IsBusy && CurrentCalculation != null && CurrentCalculation.SalaireBrut > 0);
        }

        /// <summary>
        /// Crée un nouveau calcul d'impôt
        /// </summary>
        public void CreateNewCalculation()
        {
            // Créer un nouvel employé
            CurrentEmployee = new Employee
            {
                EmployeeId = GenerateEmployeeId(),
                Category = "Cadre Inférieur" // Catégorie par défaut
            };

            // Créer un nouveau calcul
            CurrentCalculation = new TaxCalculation
            {
                Id = Guid.NewGuid().ToString(),
                Employee = CurrentEmployee,
                CalculationDate = DateTime.Now,
                IsAnnual = IsAnnual,
                IsRegularization = IsRegularization
            };

            IsNewEmployee = true;
            ResultMessage = "Nouveau calcul créé. Veuillez remplir les informations de l'employé.";
        }

        /// <summary>
        /// Réinitialise le calcul actuel - cette méthode est ajoutée pour
        /// assurer la compatibilité avec MainWindow.xaml.cs
        /// </summary>
        public void ResetCalculation()
        {
            // Réutilise simplement la méthode CreateNewCalculation()
            CreateNewCalculation();
        }

        /// <summary>
        /// Charge un calcul existant
        /// </summary>
        /// <param name="calculation">Calcul à charger</param>
        public void LoadExistingCalculation(TaxCalculation calculation)
        {
            if (calculation == null)
                throw new ArgumentNullException(nameof(calculation));

            CurrentCalculation = calculation;
            CurrentEmployee = calculation.Employee;
            IsAnnual = calculation.IsAnnual;
            IsRegularization = calculation.IsRegularization;
            IsNewEmployee = false;
            ResultMessage = $"Calcul chargé pour {CurrentEmployee.FirstName} {CurrentEmployee.LastName}";
        }

        /// <summary>
        /// Charge un calcul depuis l'historique - méthode ajoutée pour
        /// assurer la compatibilité avec MainWindow.xaml.cs
        /// </summary>
        /// <param name="calculation">Calcul à charger</param>
        public void LoadFromHistory(TaxCalculation calculation)
        {
            // Réutilise la méthode LoadExistingCalculation
            LoadExistingCalculation(calculation);
        }

        /// <summary>
        /// Vérifie si un calcul peut être exporté
        /// </summary>
        /// <returns>true si un calcul valide existe</returns>
        public bool CanExport()
        {
            return CurrentCalculation != null && CurrentCalculation.SalaireBrut > 0;
        }

        /// <summary>
        /// Exporte le détail du calcul actuel au format JSON
        /// </summary>
        /// <returns>true si l'exportation a réussi</returns>
        public bool ExportCalculation()
        {
            try
            {
                if (CurrentCalculation == null || string.IsNullOrWhiteSpace(CurrentCalculation.CalculationDetails))
                    return false;

                // Préparer l'objet à exporter
                var exportObject = new
                {
                    CurrentCalculation.CalculationDetails
                };

                // Sérialiser en JSON
                var serializer = new JavaScriptSerializer();
                string json = serializer.Serialize(exportObject);

                // Demander à l'utilisateur où enregistrer le fichier
                var dialog = new Microsoft.Win32.SaveFileDialog
                {
                    Title = "Exporter les détails du calcul",
                    Filter = "Fichier JSON (*.json)|*.json",
                    FileName = "CalculationDetails.json"
                };

                if (dialog.ShowDialog() == true)
                {
                    File.WriteAllText(dialog.FileName, json);
                    MessageBox.Show("Exportation réussie.", "Export", MessageBoxButton.OK, MessageBoxImage.Information);
                    return true;
                }

                return false;
            }
            catch (Exception ex)
            {
                MessageBox.Show("Erreur lors de l'exportation : " + ex.Message, "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }
        }

        /// <summary>
        /// Rafraîchit les paramètres de calcul
        /// </summary>
        public async Task RefreshSettingsAsync()
        {
            try
            {
                // Recharger les catégories d'employés et autres paramètres depuis le service de données
                var settings = await _dataService.LoadSettingsAsync();

                // Mettre à jour les catégories si nécessaire
                if (settings != null && settings.EmployeeCategories != null && settings.EmployeeCategories.Count > 0)
                {
                    EmployeeCategories.Clear();
                    foreach (var category in settings.EmployeeCategories)
                    {
                        EmployeeCategories.Add(category);
                    }
                }
            }
            catch (Exception ex)
            {
                ResultMessage = $"Erreur lors du chargement des paramètres: {ex.Message}";
            }
        }

        /// <summary>
        /// Charge les paramètres d'application
        /// </summary>
        private async Task LoadSettingsAsync()
        {
            try
            {
                await RefreshSettingsAsync();
            }
            catch (Exception ex)
            {
                ResultMessage = $"Erreur lors du chargement des paramètres: {ex.Message}";
            }
        }

        /// <summary>
        /// Exécute le calcul d'impôt de façon asynchrone
        /// </summary>
        private async Task ExecuteCalculateTaxAsync()
        {
            try
            {
                IsBusy = true;
                ResultMessage = "Calcul en cours...";

                // Calculer l'impôt via le service
                var result = _taxCalculatorService.CalculateTax(CurrentEmployee, IsAnnual, IsRegularization, PreviousTaxPaid);

                // Mettre à jour le calcul courant avec les résultats
                CurrentCalculation.SalaireBrut = result.SalaireBrut;
                CurrentCalculation.BaseIUTSHorsDeduction = result.BaseIUTSHorsDeduction;
                CurrentCalculation.IUTSNet = result.IUTSNet;
                CurrentCalculation.TaxRate = result.TaxRate;
                CurrentCalculation.FamilyChargeReduction = result.FamilyChargeReduction;
                CurrentCalculation.ExemptAmount = result.ExemptAmount;
                CurrentCalculation.CalculationDetails = result.CalculationDetails;

                if (IsRegularization)
                {
                    CurrentCalculation.TaxDifference = result.IUTSNet - PreviousTaxPaid;
                }

                ResultMessage = $"Impôt calculé: {result.IUTSNet:N0} FCFA";

                // Déclencher l'événement de calcul terminé
                CalculationCompleted?.Invoke(this, EventArgs.Empty);

                // Pour simuler un traitement asynchrone (si l'opération est réellement synchrone)
                await Task.CompletedTask;
            }
            catch (Exception ex)
            {
                ResultMessage = $"Erreur lors du calcul: {ex.Message}";
            }
            finally
            {
                IsBusy = false;
            }
        }

        /// <summary>
        /// Sauvegarde le calcul dans l'historique de façon asynchrone
        /// </summary>
        private async Task ExecuteSaveCalculationAsync()
        {
            try
            {
                IsBusy = true;
                ResultMessage = "Sauvegarde du calcul...";

                // Mettre à jour la date de calcul
                CurrentCalculation.CalculationDate = DateTime.Now;

                // Sauvegarder via le service de données
                await _dataService.AddCalculationToHistoryAsync(CurrentCalculation);

                ResultMessage = "Calcul sauvegardé avec succès";
            }
            catch (Exception ex)
            {
                ResultMessage = $"Erreur lors de la sauvegarde: {ex.Message}";
            }
            finally
            {
                IsBusy = false;
            }
        }

        /// <summary>
        /// Crée un nouvel employé
        /// </summary>
        private void ExecuteCreateNewEmployee()
        {
            CreateNewCalculation();
        }

        /// <summary>
        /// Réinitialise le formulaire
        /// </summary>
        private void ExecuteClearForm()
        {
            if (CurrentEmployee != null)
            {
                // Réinitialiser les propriétés mais garder l'employé
                CurrentEmployee.BaseSalary = 0;
                CurrentEmployee.HousingAllowance = 0;
                CurrentEmployee.TransportAllowance = 0;
                CurrentEmployee.FunctionAllowance = 0;
                CurrentEmployee.FamilyCharges = 0;
                CurrentEmployee.IsSpouseWorking = false;
                CurrentEmployee.PensionContribution = 0;

                // Réinitialiser le calcul
                CurrentCalculation = new TaxCalculation
                {
                    Id = Guid.NewGuid().ToString(),
                    Employee = CurrentEmployee,
                    CalculationDate = DateTime.Now,
                    IsAnnual = IsAnnual,
                    IsRegularization = IsRegularization
                };

                ResultMessage = "Formulaire réinitialisé";
            }
        }
        /// <summary>
        /// Charge un employé existant de façon asynchrone
        /// </summary>
        private async Task ExecuteLoadEmployeeAsync()
        {
            try
            {
                IsBusy = true;

                // Ouvrir une boîte de dialogue pour sélectionner un employé
                // Ici, on simule l'opération en chargeant un employé existant

                var employees = await _dataService.LoadEmployeesAsync();
                if (employees.Count > 0)
                {
                    // Prendre le premier employé comme exemple
                    var employee = employees[0];
                    CurrentEmployee = employee;

                    // Créer un nouveau calcul pour cet employé
                    CurrentCalculation = new TaxCalculation
                    {
                        Id = Guid.NewGuid().ToString(),
                        Employee = CurrentEmployee,
                        CalculationDate = DateTime.Now,
                        IsAnnual = IsAnnual,
                        IsRegularization = IsRegularization
                    };

                    IsNewEmployee = false;
                    ResultMessage = $"Employé chargé: {employee.FirstName} {employee.LastName}";
                }
                else
                {
                    ResultMessage = "Aucun employé trouvé";
                }
            }
            catch (Exception ex)
            {
                ResultMessage = $"Erreur lors du chargement: {ex.Message}";
            }
            finally
            {
                IsBusy = false;
            }
        }

        /// <summary>
        /// Génère un nouvel identifiant d'employé
        /// </summary>
        /// <returns>Identifiant unique</returns>
        private string GenerateEmployeeId()
        {
            // Format: E-YYYYMMDD-XXXX (où XXXX est un nombre aléatoire)
            return $"E-{DateTime.Now:yyyyMMdd}-{new Random().Next(1000, 9999)}";
        }

        /// <summary>
        /// Méthode appelée quand le ViewModel devient actif ou inactif
        /// </summary>
        protected override void OnIsActiveChanged()
        {
            if (IsActive)
            {
                // Actions à effectuer quand le ViewModel devient actif
                _ = RefreshSettingsAsync();
            }
        }

        /// <summary>
        /// Méthode synchrone pour la compatibilité avec les implémentations qui n'utilisent pas async/await
        /// </summary>
        public void RefreshSettings()
        {
            _ = RefreshSettingsAsync();
        }

        /// <summary>
        /// Libère les ressources utilisées par le ViewModel
        /// </summary>
        public void Dispose()
        {
            // Libérer les ressources si nécessaire
        }
    }
}