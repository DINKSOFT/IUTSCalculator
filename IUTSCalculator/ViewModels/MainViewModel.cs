using System;
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
        private readonly BaseSalaryEstimatorViewModel _baseSalaryEstimatorViewModel;
        private readonly BatchSalaryCalculationViewModel _batchSalaryCalculationViewModel;
        private readonly BatchSalaryEstimationViewModel _batchSalaryEstimationViewModel;
        private readonly DataService _dataService;
        private readonly TaxDataService _taxDataService;
        private readonly TaxCalculatorService _taxCalculatorService;
        private readonly PDFExportService _pdfExportService;
        private readonly Dossier _currentDossier;
        private string _applicationTitle;

        // Bulletin de paie (écran de consultation + export PDF/Excel)
        private readonly BulletinViewModel _bulletinViewModel;
        // Bulletins de paie en masse (sélection multi-employés + export PDF en lot)
        private readonly BulletinBatchViewModel _bulletinBatchViewModel;

        public BaseViewModel CurrentViewModel
        {
            get => _currentViewModel;
            set
            {
                if (SetProperty(ref _currentViewModel, value))
                {
                    if (_currentViewModel != null)
                        _currentViewModel.IsActive = true;
                    else if (value != null)
                        value.IsActive = false;
                }
            }
        }

        public string ApplicationTitle
        {
            get => _applicationTitle;
            set => SetProperty(ref _applicationTitle, value);
        }

        /// <summary>
        /// Raison sociale du dossier (entreprise) actuellement ouvert
        /// </summary>
        public string CurrentDossierName => _currentDossier?.RaisonSociale;

        /// <summary>
        /// Déclenché lorsque l'utilisateur demande à changer de dossier (entreprise) actif
        /// </summary>
        public event EventHandler DossierChangeRequested;

        public ICommand NavigateToCalculationCommand { get; private set; }
        public ICommand NavigateToHistoryCommand { get; private set; }
        public ICommand NavigateToSettingsCommand { get; private set; }
        public ICommand NavigateToBaseSalaryEstimatorCommand { get; private set; }
        public ICommand NavigateToBatchSalaryCalculationCommand { get; private set; }
        public ICommand NavigateToBatchSalaryEstimationCommand { get; private set; }
        public ICommand ExportToPdfCommand { get; private set; }
        public ICommand SaveDataCommand { get; private set; }
        public ICommand NewCalculationCommand { get; private set; }
        public ICommand NavigateToEmployeeManagementCommand { get; private set; }
        public ICommand NavigateToBulletinCommand { get; private set; }
        public ICommand NavigateToBulletinBatchCommand { get; private set; }
        public ICommand ChangeDossierCommand { get; private set; }

        public MainViewModel()
        {
            // Le dossier (entreprise) à ouvrir doit avoir été sélectionné au préalable
            // via l'écran de sélection de dossier (voir App.xaml.cs).
            _currentDossier = DossierSession.Instance.CurrentDossier
                ?? throw new InvalidOperationException("Aucun dossier n'est ouvert. Sélectionnez un dossier avant de démarrer l'application.");

            ApplicationTitle = $"IUTS Calculator - Calculatrice d'Impôt Unifié sur les Traitements et Salaires — Dossier : {_currentDossier.RaisonSociale}";

            // Initialisation des services (la base de données utilisée est celle du dossier ouvert,
            // ce qui garantit qu'aucun employé n'est mélangé entre deux entreprises différentes)
            _dataService = new DataService(_currentDossier.DbFileName);
            _taxDataService = new TaxDataService("path/to/data");
            _taxCalculatorService = new TaxCalculatorService(_taxDataService);
            _pdfExportService = new PDFExportService();

            // Initialisation des ViewModels
            _calculationViewModel = new CalculationViewModel(_taxCalculatorService, _dataService);
            _historyViewModel = new HistoryViewModel(_dataService, _taxCalculatorService);
            _settingsViewModel = new SettingsViewModel(_dataService);
            _baseSalaryEstimatorViewModel = new BaseSalaryEstimatorViewModel(_dataService, _taxCalculatorService);
            _batchSalaryCalculationViewModel = new BatchSalaryCalculationViewModel(_dataService, _taxCalculatorService);
            _batchSalaryEstimationViewModel = new BatchSalaryEstimationViewModel(_taxCalculatorService);

            // Initialisation du ViewModel pour le bulletin de paie
            _bulletinViewModel = new BulletinViewModel(
                _dataService,
                new BulletinService(_taxCalculatorService),
                new BulletinExportService());

            // Initialisation du ViewModel pour les bulletins de paie en masse
            _bulletinBatchViewModel = new BulletinBatchViewModel(
                _dataService,
                new BulletinService(_taxCalculatorService),
                new BulletinExportService());

            // Navigation par défaut
            CurrentViewModel = _calculationViewModel;

            InitializeCommands();
            SubscribeToViewModelEvents();

            // Chargement initial des données
            _ = LoadInitialDataAsync();
        }

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

            NavigateToBatchSalaryCalculationCommand = new RelayCommand(
                execute: () => NavigateTo(_batchSalaryCalculationViewModel),
                canExecute: () => !IsBusy);

            NavigateToBatchSalaryEstimationCommand = new RelayCommand(
                execute: () => NavigateTo(_batchSalaryEstimationViewModel),
                canExecute: () => !IsBusy);

            ExportToPdfCommand = new RelayCommand(
                async () => await ExportCurrentCalculationToPdfAsync(),
                () => !IsBusy && _calculationViewModel.CurrentCalculation != null);

            SaveDataCommand = new RelayCommand(
                async () => await SaveAllDataAsync(),
                () => !IsBusy);

            NewCalculationCommand = new RelayCommand(
                execute: CreateNewCalculation,
                canExecute: () => !IsBusy);

            NavigateToEmployeeManagementCommand = new RelayCommand(NavigateToEmployeeManagement);

            NavigateToBulletinCommand = new RelayCommand(
                execute: () => NavigateTo(_bulletinViewModel),
                canExecute: () => !IsBusy);

            NavigateToBulletinBatchCommand = new RelayCommand(
                execute: () => NavigateTo(_bulletinBatchViewModel),
                canExecute: () => !IsBusy);

            ChangeDossierCommand = new RelayCommand(
                execute: () => DossierChangeRequested?.Invoke(this, EventArgs.Empty),
                canExecute: () => !IsBusy);
        }

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
                _calculationViewModel.RefreshSettings();
                _historyViewModel.RefreshSettings();
            };
        }

        private async Task LoadInitialDataAsync()
        {
            try
            {
                IsBusy = true;
                StatusMessage = "Chargement des données...";

                await _taxDataService.ReloadTaxDataAsync();
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

        private void NavigateTo(BaseViewModel viewModel)
        {
            if (viewModel == null)
                throw new ArgumentNullException(nameof(viewModel));

            if (CurrentViewModel != null)
                CurrentViewModel.IsActive = false;

            CurrentViewModel = viewModel;
            viewModel.IsActive = true;
            StatusMessage = $"Module {viewModel.Title} chargé";
        }

        private async Task ExportCurrentCalculationToPdfAsync()
        {
            try
            {
                IsBusy = true;
                StatusMessage = "Exportation en PDF...";

                var currentCalculation = _calculationViewModel.CurrentCalculation;
                if (currentCalculation != null)
                {
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

        private string GetSaveFilePath(string defaultFileName)
        {
            // Dans une application réelle, utilisez un SaveFileDialog
            return System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                defaultFileName);
        }

        private async Task SaveAllDataAsync()
        {
            try
            {
                IsBusy = true;
                StatusMessage = "Sauvegarde des données...";

                var calculations = await _dataService.LoadCalculationsHistoryAsync();
                await _dataService.SaveCalculationsHistoryAsync(calculations);

                var settings = await _dataService.LoadSettingsAsync();
                await _dataService.SaveSettingsAsync(settings);

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

        private void CreateNewCalculation()
        {
            _calculationViewModel.CreateNewCalculation();
            NavigateTo(_calculationViewModel);
            StatusMessage = "Nouveau calcul créé";
        }

        private void NavigateToEmployeeManagement()
        {
            CurrentViewModel = new EmployeeManagementViewModel(_dataService);
        }

        public void Dispose()
        {
            // Libérer les ressources si nécessaire
        }
    }
}
