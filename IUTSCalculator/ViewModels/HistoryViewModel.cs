using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Threading.Tasks;
using IUTSCalculator.Models;
using IUTSCalculator.Helpers;
using IUTSCalculator.Services;

namespace IUTSCalculator.ViewModels
{
    /// <summary>
    /// ViewModel pour la gestion de l'historique des calculs IUTS
    /// </summary>
    public class HistoryViewModel : BaseViewModel
    {
        public bool CanEditData => UserSession.Instance.CanEdit;
        public bool IsAdmin => UserSession.Instance.IsAdmin;
        public bool IsReadOnly => UserSession.Instance.IsReadOnly;
        private readonly DataService _dataService;
        private ObservableCollection<TaxCalculation> _calculations;
        private TaxCalculation _selectedCalculation;
        private string _searchText;
        // Suppression de _isBusy car il est déjà défini dans BaseViewModel
        // Suppression de _statusMessage car il est déjà défini dans BaseViewModel
        private DateTime? _startDate;
        private DateTime? _endDate;
        private string _selectedEmployeeFilter;
        private bool _showAnnualOnly;
        private bool _showMonthlyOnly;

        /// <summary>
        /// Événement déclenché lorsqu'un calcul est sélectionné pour être consulté ou modifié
        /// </summary>
        public event EventHandler<TaxCalculation> CalculationSelected;

        /// <summary>
        /// Collection des calculs dans l'historique
        /// </summary>
        public ObservableCollection<TaxCalculation> Calculations
        {
            get => _calculations;
            set => SetProperty(ref _calculations, value);
        }

        /// <summary>
        /// Calcul actuellement sélectionné dans la liste
        /// </summary>
        public TaxCalculation SelectedCalculation
        {
            get => _selectedCalculation;
            set
            {
                if (SetProperty(ref _selectedCalculation, value))
                {
                    // Invalider les commandes qui dépendent de la sélection
                    (ViewCalculationCommand as RelayCommand)?.RaiseCanExecuteChanged();
                    (DeleteCalculationCommand as RelayCommand)?.RaiseCanExecuteChanged();
                    (ExportCalculationCommand as RelayCommand)?.RaiseCanExecuteChanged();
                }
            }
        }

        /// <summary>
        /// Texte de recherche pour filtrer l'historique
        /// </summary>
        public string SearchText
        {
            get => _searchText;
            set
            {
                if (SetProperty(ref _searchText, value))
                {
                    FilterCalculations();
                }
            }
        }

        // Suppression de la propriété StatusMessage car elle est déjà définie dans BaseViewModel

        /// <summary>
        /// Date de début pour le filtre de période
        /// </summary>
        public DateTime? StartDate
        {
            get => _startDate;
            set
            {
                if (SetProperty(ref _startDate, value))
                {
                    FilterCalculations();
                }
            }
        }

        /// <summary>
        /// Date de fin pour le filtre de période
        /// </summary>
        public DateTime? EndDate
        {
            get => _endDate;
            set
            {
                if (SetProperty(ref _endDate, value))
                {
                    FilterCalculations();
                }
            }
        }

        /// <summary>
        /// Employé sélectionné pour le filtre
        /// </summary>
        public string SelectedEmployeeFilter
        {
            get => _selectedEmployeeFilter;
            set
            {
                if (SetProperty(ref _selectedEmployeeFilter, value))
                {
                    FilterCalculations();
                }
            }
        }

        /// <summary>
        /// Filtre pour n'afficher que les calculs annuels
        /// </summary>
        public bool ShowAnnualOnly
        {
            get => _showAnnualOnly;
            set
            {
                if (SetProperty(ref _showAnnualOnly, value))
                {
                    if (value && _showMonthlyOnly)
                    {
                        _showMonthlyOnly = false;
                        OnPropertyChanged(nameof(ShowMonthlyOnly));
                    }
                    FilterCalculations();
                }
            }
        }

        /// <summary>
        /// Filtre pour n'afficher que les calculs mensuels
        /// </summary>
        public bool ShowMonthlyOnly
        {
            get => _showMonthlyOnly;
            set
            {
                if (SetProperty(ref _showMonthlyOnly, value))
                {
                    if (value && _showAnnualOnly)
                    {
                        _showAnnualOnly = false;
                        OnPropertyChanged(nameof(ShowAnnualOnly));
                    }
                    FilterCalculations();
                }
            }
        }

        /// <summary>
        /// Liste des noms d'employés disponibles pour le filtre
        /// </summary>
        public ObservableCollection<string> EmployeeNames { get; private set; }

        // Commandes
        public ICommand ViewCalculationCommand { get; private set; }
        public ICommand DeleteCalculationCommand { get; private set; }
        public ICommand ExportCalculationCommand { get; private set; }
        public ICommand RefreshHistoryCommand { get; private set; }
        public ICommand ClearFiltersCommand { get; private set; }

        /// <summary>
        /// Constructeur
        /// </summary>
        /// <param name="dataService">Service de données</param>
        /// <param name="taxCalculatorService">Service de calcul d'impôt</param>
        public HistoryViewModel(DataService dataService, TaxCalculatorService taxCalculatorService)
        {
            _dataService = dataService ?? throw new ArgumentNullException(nameof(dataService));

            Title = "Historique";

            // Initialiser les collections
            Calculations = new ObservableCollection<TaxCalculation>();
            EmployeeNames = new ObservableCollection<string>();

            // Initialiser les commandes
            InitializeCommands();
        }

        /// <summary>
        /// Initialise les commandes du ViewModel
        /// </summary>
        private void InitializeCommands()
        {
            ViewCalculationCommand = new RelayCommand(
                execute: ExecuteViewCalculation,
                canExecute: () => !IsBusy && SelectedCalculation != null);

            DeleteCalculationCommand = new RelayCommand(
                execute: ExecuteDeleteCalculation,
                canExecute: () => !IsBusy && SelectedCalculation != null);

            ExportCalculationCommand = new RelayCommand(
                execute: ExecuteExportCalculation,
                canExecute: () => !IsBusy && SelectedCalculation != null);

            RefreshHistoryCommand = new RelayCommand(
                execute: ExecuteRefreshHistory,
                canExecute: () => !IsBusy);

            ClearFiltersCommand = new RelayCommand(
                execute: ExecuteClearFilters,
                canExecute: () => !IsBusy);
        }

        /// <summary>
        /// Charge l'historique des calculs de manière asynchrone
        /// </summary>
        public async Task LoadCalculationHistoryAsync()
        {
            try
            {
                IsBusy = true;
                StatusMessage = "Chargement de l'historique...";

                // Récupérer l'historique via le service de données (méthode asynchrone)
                var allCalculations = await _dataService.LoadCalculationsHistoryAsync();

                // Mettre à jour la collection
                Calculations.Clear();
                foreach (var calculation in allCalculations)
                {
                    Calculations.Add(calculation);
                }

                // Mettre à jour la liste des employés
                UpdateEmployeeNamesList();

                StatusMessage = $"{Calculations.Count} calculs chargés";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Erreur lors du chargement de l'historique: {ex.Message}";
            }
            finally
            {
                IsBusy = false;
            }
        }

        /// <summary>
        /// Charge l'historique des calculs (wrapper synchrone pour la méthode asynchrone)
        /// </summary>
        public void LoadCalculationHistory()
        {
            // Appeler la méthode asynchrone et attendre le résultat
            // Note: Dans une application WPF réelle, il serait préférable d'utiliser
            // une approche complètement asynchrone avec async/await
            Task.Run(async () => await LoadCalculationHistoryAsync()).Wait();
        }

        /// <summary>
        /// Met à jour la liste des noms d'employés pour le filtre
        /// </summary>
        private void UpdateEmployeeNamesList()
        {
            EmployeeNames.Clear();

            // Ajouter une option pour "Tous les employés"
            EmployeeNames.Add("Tous les employés");

            // Récupérer la liste unique des noms d'employés
            var uniqueEmployees = Calculations
                .Select(c => $"{c.Employee.LastName} {c.Employee.FirstName}")
                .Distinct()
                .OrderBy(n => n);

            foreach (var name in uniqueEmployees)
            {
                EmployeeNames.Add(name);
            }

            // Sélectionner "Tous les employés" par défaut
            SelectedEmployeeFilter = "Tous les employés";
        }

        /// <summary>
        /// Filtre les calculs selon les critères actuels
        /// </summary>
        private async void FilterCalculations()
        {
            try
            {
                IsBusy = true;
                StatusMessage = "Application des filtres...";

                // Récupérer tous les calculs
                var allCalculations = await _dataService.LoadCalculationsHistoryAsync();

                // Appliquer les filtres
                var filteredCalculations = allCalculations.AsEnumerable();

                // Filtre par texte de recherche
                if (!string.IsNullOrWhiteSpace(SearchText))
                {
                    string searchLower = SearchText.ToLower();
                    filteredCalculations = filteredCalculations.Where(c =>
                        c.Employee.LastName.ToLower().Contains(searchLower) ||
                        c.Employee.FirstName.ToLower().Contains(searchLower) ||
                        c.Employee.EmployeeId.ToLower().Contains(searchLower));
                }

                // Filtre par dates
                if (StartDate.HasValue)
                {
                    filteredCalculations = filteredCalculations.Where(c => c.CalculationDate >= StartDate.Value);
                }

                if (EndDate.HasValue)
                {
                    filteredCalculations = filteredCalculations.Where(c => c.CalculationDate <= EndDate.Value.AddDays(1));
                }

                // Filtre par employé
                if (!string.IsNullOrEmpty(SelectedEmployeeFilter) && SelectedEmployeeFilter != "Tous les employés")
                {
                    filteredCalculations = filteredCalculations.Where(c =>
                        $"{c.Employee.LastName} {c.Employee.FirstName}" == SelectedEmployeeFilter);
                }

                // Filtre par type de calcul (annuel/mensuel)
                if (ShowAnnualOnly)
                {
                    filteredCalculations = filteredCalculations.Where(c => c.IsAnnual);
                }
                else if (ShowMonthlyOnly)
                {
                    filteredCalculations = filteredCalculations.Where(c => !c.IsAnnual);
                }

                // Mettre à jour la collection
                Calculations.Clear();
                foreach (var calculation in filteredCalculations)
                {
                    Calculations.Add(calculation);
                }

                StatusMessage = $"{Calculations.Count} calculs trouvés";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Erreur lors du filtrage: {ex.Message}";
            }
            finally
            {
                IsBusy = false;
            }
        }

        /// <summary>
        /// Exécute l'action de consultation d'un calcul
        /// </summary>
        private void ExecuteViewCalculation()
        {
            if (SelectedCalculation != null)
            {
                // Déclencher l'événement pour informer le MainViewModel
                CalculationSelected?.Invoke(this, SelectedCalculation);
                StatusMessage = $"Calcul sélectionné: {SelectedCalculation.Employee.LastName} {SelectedCalculation.Employee.FirstName}";
            }
        }

        /// <summary>
        /// Exécute l'action de suppression d'un calcul
        /// </summary>
        private async void ExecuteDeleteCalculation()
        {
            if (SelectedCalculation != null)
            {
                var confirmation = MessageBox.Show(
                    $"Supprimer définitivement le calcul de {SelectedCalculation.Employee.LastName} {SelectedCalculation.Employee.FirstName} du {SelectedCalculation.CalculationDate:dd/MM/yyyy} ?",
                    "Confirmation de suppression", MessageBoxButton.YesNo, MessageBoxImage.Warning);
                if (confirmation != MessageBoxResult.Yes) return;

                try
                {
                    IsBusy = true;
                    StatusMessage = "Suppression du calcul...";


                    // Supprimer le calcul via le service de données
                    await _dataService.DeleteCalculationFromHistoryAsync(SelectedCalculation.Id);

                    // Mettre à jour la liste
                    Calculations.Remove(SelectedCalculation);
                    SelectedCalculation = null;

                    StatusMessage = "Calcul supprimé avec succès";
                }
                catch (Exception ex)
                {
                    StatusMessage = $"Erreur lors de la suppression: {ex.Message}";
                }
                finally
                {
                    IsBusy = false;
                }
            }
        }

        /// <summary>
        /// Exécute l'action d'exportation d'un calcul en PDF
        /// </summary>
        private async void ExecuteExportCalculation()
        {
            if (SelectedCalculation != null)
            {
                try
                {
                    IsBusy = true;
                    StatusMessage = "Exportation du calcul en PDF...";

                    // Créer et utiliser un service PDF pour l'export
                    var pdfService = new PDFExportService();

                    // Obtenir un chemin pour le fichier PDF
                    string fileName = $"IUTS_{SelectedCalculation.Employee.LastName}_{SelectedCalculation.Employee.FirstName}_{DateTime.Now:yyyyMMdd}.pdf";
                    string filePath = GetSaveFilePath(fileName);

                    if (!string.IsNullOrEmpty(filePath))
                    {
                        // Utiliser la méthode asynchrone pour exporter le calcul
                        bool success = await pdfService.ExportTaxCalculationToPdfAsync(SelectedCalculation, filePath);

                        if (success)
                        {
                            StatusMessage = "Calcul exporté avec succès";
                        }
                        else
                        {
                            StatusMessage = "Échec lors de l'exportation du calcul";
                        }
                    }
                    else
                    {
                        StatusMessage = "Exportation annulée";
                    }
                }
                catch (Exception ex)
                {
                    StatusMessage = $"Erreur lors de l'exportation: {ex.Message}";
                }
                finally
                {
                    IsBusy = false;
                }
            }
        }

        /// <summary>
        /// Obtient le chemin pour enregistrer un fichier via une boîte de dialogue
        /// </summary>
        /// <param name="defaultFileName">Nom de fichier par défaut</param>
        /// <returns>Chemin sélectionné ou null si annulé</returns>
        private string GetSaveFilePath(string defaultFileName)
        {
            // Note: Dans une implémentation réelle, vous utiliseriez 
            // Microsoft.Win32.SaveFileDialog ou équivalent

            // Exemple simple pour simulation:
            // Nous supposons ici que l'utilisateur a choisi d'enregistrer dans son dossier Documents
            string documentsPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            return System.IO.Path.Combine(documentsPath, defaultFileName);

            // Dans une application réelle, remplacer par:
            /*
            var saveFileDialog = new Microsoft.Win32.SaveFileDialog
            {
                FileName = defaultFileName,
                DefaultExt = ".pdf",
                Filter = "Documents PDF (*.pdf)|*.pdf"
            };
            
            bool? result = saveFileDialog.ShowDialog();
            return result == true ? saveFileDialog.FileName : null;
            */
        }

        /// <summary>
        /// Exécute l'action de rafraîchissement de l'historique.
        ///
        /// Appelle directement la version asynchrone plutôt que le wrapper synchrone
        /// LoadCalculationHistory() : celui-ci bloque le thread appelant — ici le thread
        /// d'interface — via Task.Run(...).Wait() jusqu'à la fin du chargement, ce qui
        /// gelait toute la fenêtre (y compris le rail de navigation) le temps du chargement,
        /// sans le moindre indicateur visuel malgré IsBusy — signalé le 16 septembre 2026
        /// (voir ETAT-DEVELOPPEMENT.md item 22).
        /// </summary>
        private async void ExecuteRefreshHistory()
        {
            await LoadCalculationHistoryAsync();
        }

        /// <summary>
        /// Exécute l'action de réinitialisation des filtres
        /// </summary>
        private async void ExecuteClearFilters()
        {
            SearchText = string.Empty;
            StartDate = null;
            EndDate = null;
            SelectedEmployeeFilter = "Tous les employés";
            ShowAnnualOnly = false;
            ShowMonthlyOnly = false;

            // Recharger tous les calculs (voir ExecuteRefreshHistory pour le choix de la
            // version asynchrone).
            await LoadCalculationHistoryAsync();
        }

        /// <summary>
        /// Rafraîchit les paramètres si nécessaire
        /// </summary>
        public void RefreshSettings()
        {
            // Si nécessaire, mettre à jour des paramètres qui dépendent des settings
        }

        /// <summary>
        /// Méthode appelée quand le ViewModel devient actif ou inactif
        /// </summary>
        protected override void OnIsActiveChanged()
        {
            if (IsActive)
            {
                // Recharger l'historique quand le ViewModel devient actif (voir
                // ExecuteRefreshHistory pour le choix de la version asynchrone).
                _ = LoadCalculationHistoryAsync();
            }
        }
    }
}