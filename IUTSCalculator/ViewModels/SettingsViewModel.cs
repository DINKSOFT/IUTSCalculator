using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows.Input;
using IUTSCalculator.Helpers;
using IUTSCalculator.Models;
using IUTSCalculator.Services;

namespace IUTSCalculator.ViewModels
{
    /// <summary>
    /// ViewModel pour la gestion des paramètres de l'application
    /// </summary>
    public class SettingsViewModel : BaseViewModel
    {
        public bool CanEditData => UserSession.Instance.CanEdit;
        public bool IsAdmin => UserSession.Instance.IsAdmin;
        public bool IsReadOnly => UserSession.Instance.IsReadOnly;
        private readonly DataService _dataService;
        private List<TaxBracket> _taxBrackets;
        private List<FamilyCharge> _familyCharges;
        private bool _isDarkTheme;
        private bool _isAutoSaveEnabled;
        private string _exportDirectory;
        private string _language;

        /// <summary>
        /// Événement déclenché lorsque les paramètres sont sauvegardés
        /// </summary>
        public event EventHandler SettingsSaved;

        /// <summary>
        /// Initialise une nouvelle instance de la classe SettingsViewModel
        /// </summary>
        /// <param name="dataService">Service de données utilisé pour la persistance</param>
        public SettingsViewModel(DataService dataService)
        {
            _dataService = dataService ?? throw new ArgumentNullException(nameof(dataService));

            // Initialisation des commandes
            SaveSettingsCommand = new RelayCommand<object>(async _ => await SaveSettingsAsync(), _ => CanSaveSettings());
            ResetToDefaultsCommand = new RelayCommand<object>(async _ => await ResetToDefaultsAsync());
            ImportSettingsCommand = new RelayCommand<object>(async _ => await ImportSettingsAsync());
            ExportSettingsCommand = new RelayCommand<object>(async _ => await ExportSettingsAsync());

            // Chargement initial des paramètres
            _ = LoadSettingsAsync();
        }

        #region Propriétés

        /// <summary>
        /// Liste des tranches d'imposition
        /// </summary>
        public List<TaxBracket> TaxBrackets
        {
            get => _taxBrackets;
            set => SetProperty(ref _taxBrackets, value);
        }

        /// <summary>
        /// Liste des charges familiales
        /// </summary>
        public List<FamilyCharge> FamilyCharges
        {
            get => _familyCharges;
            set => SetProperty(ref _familyCharges, value);
        }

        /// <summary>
        /// Indique si le thème sombre est activé
        /// </summary>
        public bool IsDarkTheme
        {
            get => _isDarkTheme;
            set
            {
                if (SetProperty(ref _isDarkTheme, value))
                {
                    ApplyTheme();
                }
            }
        }

        /// <summary>
        /// Indique si la sauvegarde automatique est activée
        /// </summary>
        public bool IsAutoSaveEnabled
        {
            get => _isAutoSaveEnabled;
            set => SetProperty(ref _isAutoSaveEnabled, value);
        }

        /// <summary>
        /// Code de langue actuellement utilisé
        /// </summary>
        public string Language
        {
            get => _language;
            set => SetProperty(ref _language, value);
        }

        /// <summary>
        /// Répertoire pour l'exportation des fichiers
        /// </summary>
        public string ExportDirectory
        {
            get => _exportDirectory;
            set => SetProperty(ref _exportDirectory, value);
        }

        #endregion

        #region Commandes

        /// <summary>
        /// Commande pour sauvegarder les paramètres
        /// </summary>
        public ICommand SaveSettingsCommand { get; }

        /// <summary>
        /// Commande pour réinitialiser les paramètres par défaut
        /// </summary>
        public ICommand ResetToDefaultsCommand { get; }

        /// <summary>
        /// Commande pour importer des paramètres
        /// </summary>
        public ICommand ImportSettingsCommand { get; }

        /// <summary>
        /// Commande pour exporter des paramètres
        /// </summary>
        public ICommand ExportSettingsCommand { get; }

        #endregion

        #region Méthodes privées

        /// <summary>
        /// Charge les paramètres de l'application
        /// </summary>
        private async Task LoadSettingsAsync()
        {
            IsBusy = true;
            StatusMessage = "Chargement des paramètres...";

            try
            {
                // Charger les paramètres de l'application
                var appSettings = await _dataService.LoadSettingsAsync();

                // Configurer les propriétés à partir des paramètres chargés
                IsDarkTheme = appSettings.Theme == "Dark";
                IsAutoSaveEnabled = appSettings.AutoSave;
                Language = appSettings.Language;

                // Charger le répertoire d'exportation depuis les paramètres de l'application
                ExportDirectory = await Task.Run(() => _dataService.GetAppSetting<string>("ExportDirectory")
                    ?? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments));

                // Charger les tranches d'impôts
                TaxBrackets = await Task.Run(() => _dataService.GetTaxBrackets());

                // Charger les charges familiales
                FamilyCharges = await Task.Run(() => _dataService.GetFamilyCharges());

                StatusMessage = "Paramètres chargés avec succès";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Erreur lors du chargement des paramètres: {ex.Message}";
            }
            finally
            {
                IsBusy = false;
            }
        }

        /// <summary>
        /// Sauvegarde les paramètres de l'application
        /// </summary>
        private async Task SaveSettingsAsync()
        {
            IsBusy = true;
            StatusMessage = "Enregistrement des paramètres...";

            try
            {
                // Créer un objet AppSettings avec les valeurs actuelles
                var appSettings = new AppSettings
                {
                    Theme = IsDarkTheme ? "Dark" : "Light",
                    Language = Language,
                    AutoSave = IsAutoSaveEnabled,
                    LastUpdated = DateTime.Now
                };

                // Sauvegarder les paramètres de l'application
                await _dataService.SaveSettingsAsync(appSettings);

                // Sauvegarder les tranches d'impôts et charges familiales
                await Task.Run(() => {
                    _dataService.SaveTaxBrackets(TaxBrackets);
                    _dataService.SaveFamilyCharges(FamilyCharges);
                    _dataService.SaveAppSetting("ExportDirectory", ExportDirectory);
                });

                StatusMessage = "Paramètres enregistrés avec succès";

                // Déclencher l'événement SettingsSaved
                SettingsSaved?.Invoke(this, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                StatusMessage = $"Erreur lors de l'enregistrement des paramètres: {ex.Message}";
            }
            finally
            {
                IsBusy = false;
            }
        }

        /// <summary>
        /// Vérifie si les paramètres peuvent être sauvegardés
        /// </summary>
        private bool CanSaveSettings()
        {
            // Vérifier si les données sont valides avant de permettre l'enregistrement
            return !IsBusy && TaxBrackets != null && TaxBrackets.Count > 0 && FamilyCharges != null;
        }

        /// <summary>
        /// Réinitialise tous les paramètres à leurs valeurs par défaut
        /// </summary>
        private async Task ResetToDefaultsAsync()
        {
            IsBusy = true;
            StatusMessage = "Réinitialisation des paramètres...";

            try
            {
                // Réinitialiser les tranches d'impôt par défaut
                TaxBrackets = new List<TaxBracket>
                {
                    new TaxBracket { LowerBound = 0, UpperBound = 25000, Rate = 0.0m },
                    new TaxBracket { LowerBound = 25001, UpperBound = 50000, Rate = 0.05m },
                    new TaxBracket { LowerBound = 50001, UpperBound = 150000, Rate = 0.10m },
                    new TaxBracket { LowerBound = 150001, UpperBound = 300000, Rate = 0.15m },
                    new TaxBracket { LowerBound = 300001, UpperBound = decimal.MaxValue, Rate = 0.20m }
                };

                // Réinitialiser les charges familiales par défaut
                FamilyCharges = new List<FamilyCharge>
                {
                    new FamilyCharge { ChargeCount = 0, AbatementPercentage = 0.0m },
                    new FamilyCharge { ChargeCount = 1, AbatementPercentage = 0.05m },
                    new FamilyCharge { ChargeCount = 2, AbatementPercentage = 0.10m },
                    new FamilyCharge { ChargeCount = 3, AbatementPercentage = 0.15m },
                    new FamilyCharge { ChargeCount = 4, AbatementPercentage = 0.20m },
                    new FamilyCharge { ChargeCount = 5, AbatementPercentage = 0.25m }
                };

                // Réinitialiser les préférences par défaut
                IsDarkTheme = false;
                IsAutoSaveEnabled = true;
                Language = "fr-FR";
                ExportDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);

                // Sauvegarder les paramètres réinitialisés
                await SaveSettingsAsync();

                StatusMessage = "Paramètres réinitialisés avec succès";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Erreur lors de la réinitialisation des paramètres: {ex.Message}";
            }
            finally
            {
                IsBusy = false;
            }
        }

        /// <summary>
        /// Applique le thème sélectionné à l'application
        /// </summary>
        private void ApplyTheme()
        {
            // Appliquer le thème clair ou sombre à l'application
            try
            {
                var appResources = System.Windows.Application.Current.Resources;
                var themeUri = IsDarkTheme
                    ? new Uri("/Resources/DarkTheme.xaml", UriKind.Relative)
                    : new Uri("/Resources/LightTheme.xaml", UriKind.Relative);

                // Mettre à jour les dictionnaires de ressources
                appResources.MergedDictionaries.Clear();
                appResources.MergedDictionaries.Add(new System.Windows.ResourceDictionary { Source = themeUri });
                appResources.MergedDictionaries.Add(new System.Windows.ResourceDictionary { Source = new Uri("/Resources/Styles.xaml", UriKind.Relative) });
            }
            catch (Exception ex)
            {
                StatusMessage = $"Erreur lors de l'application du thème: {ex.Message}";
            }
        }

        /// <summary>
        /// Importe les paramètres depuis un fichier JSON
        /// </summary>
        private async Task ImportSettingsAsync()
        {
            try
            {
                // Utiliser un dialogue pour sélectionner un fichier de configuration à importer
                var dialog = new Microsoft.Win32.OpenFileDialog
                {
                    DefaultExt = ".json",
                    Filter = "Fichiers JSON (*.json)|*.json"
                };

                if (dialog.ShowDialog() == true)
                {
                    StatusMessage = "Importation des paramètres...";
                    IsBusy = true;

                    // Appeler le service de données pour importer les paramètres
                    await Task.Run(() => _dataService.ImportSettings(dialog.FileName));

                    // Recharger les paramètres
                    await LoadSettingsAsync();

                    StatusMessage = "Paramètres importés avec succès";
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"Erreur lors de l'importation des paramètres: {ex.Message}";
            }
            finally
            {
                IsBusy = false;
            }
        }

        /// <summary>
        /// Exporte les paramètres vers un fichier JSON
        /// </summary>
        private async Task ExportSettingsAsync()
        {
            try
            {
                // Utiliser un dialogue pour choisir où enregistrer le fichier de configuration
                var dialog = new Microsoft.Win32.SaveFileDialog
                {
                    DefaultExt = ".json",
                    Filter = "Fichiers JSON (*.json)|*.json",
                    FileName = "IUTSCalculator_Settings.json"
                };

                if (dialog.ShowDialog() == true)
                {
                    StatusMessage = "Exportation des paramètres...";
                    IsBusy = true;

                    // Appeler le service de données pour exporter les paramètres
                    await Task.Run(() => _dataService.ExportSettings(dialog.FileName));

                    StatusMessage = "Paramètres exportés avec succès";
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"Erreur lors de l'exportation des paramètres: {ex.Message}";
            }
            finally
            {
                IsBusy = false;
            }
        }

        #endregion
    }
}