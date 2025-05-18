using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using System.Windows.Input;
using IUTSCalculator.Models;
using IUTSCalculator.Helpers;
using IUTSCalculator.Services;

namespace IUTSCalculator.ViewModels
{
    /// <summary>
    /// ViewModel principal qui coordonne l'ensemble de l'application
    /// </summary>
    public class MainViewModel : BaseViewModel
    {
        public bool CanEditData => UserSession.Instance.CanEdit;
        public bool IsAdmin => UserSession.Instance.IsAdmin;
        public bool IsReadOnly => UserSession.Instance.IsReadOnly;
        private BaseViewModel _currentViewModel;
        private readonly CalculationViewModel _calculationViewModel;
        private readonly HistoryViewModel _historyViewModel;
        private readonly SettingsViewModel _settingsViewModel;
        private readonly BaseSalaryEstimatorViewModel _baseSalaryEstimatorViewModel; // Ajout du ViewModel pour l'estimation du salaire
        private readonly DataService _dataService;
        private readonly TaxDataService _taxDataService;
        private readonly TaxCalculatorService _taxCalculatorService;
        private readonly PDFExportService _pdfExportService;
        private string _applicationTitle;

        /// <summary>
        /// ViewModel actuellement affiché
        /// </summary>
        public BaseViewModel CurrentViewModel
        {
            get => _currentViewModel;
            set
            {
                if (SetProperty(ref _currentViewModel, value))
                {
                    // Désactiver l'ancien ViewModel (déjà géré par SetProperty)
                    if (_currentViewModel != null)
                        _currentViewModel.IsActive = true;
                    else if (value != null)
                        value.IsActive = false;
                }
            }
        }

        /// <summary>
        /// Titre de l'application
        /// </summary>
        public string ApplicationTitle
        {
            get => _applicationTitle;
            set => SetProperty(ref _applicationTitle, value);
        }

        /// <summary>
        /// Commande pour naviguer vers la vue de calcul
        /// </summary>
        public ICommand NavigateToCalculationCommand { get; private set; }

        /// <summary>
        /// Commande pour naviguer vers la vue d'historique
        /// </summary>
        public ICommand NavigateToHistoryCommand { get; private set; }

        /// <summary>
        /// Commande pour naviguer vers la vue des paramètres
        /// </summary>
        public ICommand NavigateToSettingsCommand { get; private set; }

        /// <summary>
        /// Commande pour naviguer vers l'estimateur de salaire de base
        /// </summary>
        public ICommand NavigateToBaseSalaryEstimatorCommand { get; private set; }

        /// <summary>
        /// Commande pour exporter le calcul actuel en PDF
        /// </summary>
        public ICommand ExportToPdfCommand { get; private set; }

        /// <summary>
        /// Commande pour sauvegarder les données actuelles
        /// </summary>
        public ICommand SaveDataCommand { get; private set; }

        /// <summary>
        /// Commande pour créer un nouveau calcul d'IUTS
        /// </summary>
        public ICommand NewCalculationCommand { get; private set; }

        /// <summary>
        /// Constructeur 
        /// </summary>
        public MainViewModel()
        {
            ApplicationTitle = "IUTS Calculator - Calculatrice d'Impôt Unifié sur les Traitements et Salaires";

            // Initialiser les services
            _dataService = new DataService();
            _taxDataService = new TaxDataService("path/to/data");  // Ajout du chemin de données
            _taxCalculatorService = new TaxCalculatorService(_taxDataService);
            _pdfExportService = new PDFExportService();

            // Initialiser les ViewModels
            _calculationViewModel = new CalculationViewModel(_taxCalculatorService, _dataService);
            _historyViewModel = new HistoryViewModel(_dataService, _taxCalculatorService);
            _settingsViewModel = new SettingsViewModel(_dataService);
            _baseSalaryEstimatorViewModel = new BaseSalaryEstimatorViewModel(_dataService,_taxCalculatorService);

            // Configurer la navigation par défaut
            CurrentViewModel = _calculationViewModel;

            // Initialiser les commandes
            InitializeCommands();

            // S'abonner aux événements des ViewModels
            SubscribeToViewModelEvents();

            // Charger les données initiales
            _ = LoadInitialDataAsync();
        }

        /// <summary>
        /// Initialise les commandes de l'application
        /// </summary>
        private void InitializeCommands()
        {
            NavigateToCalculationCommand = new RelayCommand(
                execute: () => NavigateTo(_calculationViewModel),
                canExecute: () => !IsBusy);

            NavigateToHistoryCommand = new RelayCommand(
                execute: () => NavigateTo(_historyViewModel),
                canExecute: () => !IsBusy);

            NavigateToSettingsCommand = new RelayCommand(
                execute: () => NavigateTo(_settingsViewModel),
                canExecute: () => !IsBusy);

            NavigateToBaseSalaryEstimatorCommand = new RelayCommand(
                execute: () => NavigateTo(_baseSalaryEstimatorViewModel),
                canExecute: () => !IsBusy);

            ExportToPdfCommand = new RelayCommand(
                execute: async () => await ExportCurrentCalculationToPdfAsync(),
                canExecute: () => !IsBusy && _calculationViewModel.HasCalculation);

            SaveDataCommand = new RelayCommand(
                execute: async () => await SaveAllDataAsync(),
                canExecute: () => !IsBusy);

            NewCalculationCommand = new RelayCommand(
                execute: CreateNewCalculation,
                canExecute: () => !IsBusy);
        }

        /// <summary>
        /// S'abonne aux événements déclenchés par les ViewModels
        /// </summary>
        private void SubscribeToViewModelEvents()
        {
            _calculationViewModel.CalculationCompleted += (sender, e) =>
            {
                StatusMessage = $"Calcul effectué le {DateTime.Now:g}";
                CommandManager.InvalidateRequerySuggested();
            };

            _historyViewModel.CalculationSelected += (sender, calculation) =>
            {
                _calculationViewModel.LoadExistingCalculation(calculation);
                NavigateTo(_calculationViewModel);
                StatusMessage = $"Calcul chargé: {calculation.Employee.LastName} {calculation.Employee.FirstName}";
            };

            _settingsViewModel.SettingsSaved += (sender, e) =>
            {
                StatusMessage = $"Paramètres enregistrés le {DateTime.Now:g}";
                // Rafraîchir les données des autres viewmodels si nécessaire
                _calculationViewModel.RefreshSettings();
                _historyViewModel.RefreshSettings();
            };

            // Si besoin d'écouter des événements du BaseSalaryEstimatorViewModel, ajoutez-les ici
        }

        /// <summary>
        /// Charge les données initiales pour l'application de manière asynchrone
        /// </summary>
        private async Task LoadInitialDataAsync()
        {
            try
            {
                IsBusy = true;
                StatusMessage = "Chargement des données...";

                // Charger les tranches d'imposition et autres données de référence
                await Task.Run(() => {
                    // Remplacer l'appel à LoadTaxBrackets() et LoadFamilyCharges() 
                    // par un appel à la méthode disponible dans TaxDataService
                    _taxDataService.ReloadTaxDataAsync().Wait();
                });

                // Charger l'historique des calculs
                await _dataService.LoadCalculationsHistoryAsync();

                StatusMessage = "Données chargées avec succès";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Erreur lors du chargement des données: {ex.Message}";
            }
            finally
            {
                IsBusy = false;
            }
        }

        /// <summary>
        /// Navigue vers le ViewModel spécifié
        /// </summary>
        /// <param name="viewModel">ViewModel de destination</param>
        private void NavigateTo(BaseViewModel viewModel)
        {
            if (viewModel == null)
                throw new ArgumentNullException(nameof(viewModel));

            // Désactiver l'ancien ViewModel s'il existe
            if (CurrentViewModel != null)
                CurrentViewModel.IsActive = false;

            CurrentViewModel = viewModel;
            viewModel.IsActive = true;
            StatusMessage = $"Module {viewModel.Title} chargé";
        }

        /// <summary>
        /// Exporte le calcul actuel au format PDF de manière asynchrone
        /// </summary>
        private async Task ExportCurrentCalculationToPdfAsync()
        {
            try
            {
                IsBusy = true;
                StatusMessage = "Exportation en PDF...";

                var currentCalculation = _calculationViewModel.CurrentCalculation;
                if (currentCalculation != null)
                {
                    // Utiliser la méthode ExportTaxCalculationToPdfAsync au lieu d'ExportCalculation
                    string fileName = $"IUTS_{currentCalculation.Employee.LastName}_{DateTime.Now:yyyyMMdd}.pdf";
                    string filePath = GetSaveFilePath(fileName);

                    if (!string.IsNullOrEmpty(filePath))
                    {
                        bool success = await _pdfExportService.ExportTaxCalculationToPdfAsync(currentCalculation, filePath);
                        StatusMessage = success ? "PDF exporté avec succès" : "Échec de l'exportation PDF";
                    }
                    else
                    {
                        StatusMessage = "Exportation annulée";
                    }
                }
                else
                {
                    StatusMessage = "Aucun calcul à exporter";
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

        /// <summary>
        /// Obtient le chemin pour sauvegarder le fichier PDF
        /// </summary>
        /// <param name="defaultFileName">Nom de fichier par défaut</param>
        /// <returns>Chemin complet du fichier ou null si annulé</returns>
        private string GetSaveFilePath(string defaultFileName)
        {
            // Dans une application réelle, ceci utiliserait un dialog de sauvegarde de fichier
            // Ici on retourne simplement un chemin par défaut pour l'exemple
            return System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                defaultFileName);
        }

        /// <summary>
        /// Sauvegarde toutes les données de l'application de manière asynchrone
        /// </summary>
        private async Task SaveAllDataAsync()
        {
            try
            {
                IsBusy = true;
                StatusMessage = "Sauvegarde des données...";

                await Task.Run(() => {
                    // Utilisez les méthodes asynchrones correctes du DataService
                    _dataService.SaveCalculationsHistoryAsync(_dataService.LoadCalculationsHistoryAsync().Result).Wait();
                    _dataService.SaveSettingsAsync(_dataService.LoadSettingsAsync().Result).Wait();
                });

                StatusMessage = $"Données sauvegardées avec succès le {DateTime.Now:g}";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Erreur lors de la sauvegarde: {ex.Message}";
            }
            finally
            {
                IsBusy = false;
            }
        }

        /// <summary>
        /// Crée un nouveau calcul d'IUTS
        /// </summary>
        private void CreateNewCalculation()
        {
            _calculationViewModel.CreateNewCalculation();
            NavigateTo(_calculationViewModel);
            StatusMessage = "Nouveau calcul créé";
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