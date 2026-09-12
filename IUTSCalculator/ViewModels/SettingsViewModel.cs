using System;
using System.Collections.Generic;
using System.IO;
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

        public SettingsViewModel(DataService dataService)
        {
            _dataService = dataService ?? throw new ArgumentNullException(nameof(dataService));

            // Le titre alimente le message de la barre d'état et signale l'écran
            // actif dans le rail de navigation : sans lui, ni l'un ni l'autre.
            Title = "Paramètres";

            SaveSettingsCommand = new RelayCommand<object>(async _ => await SaveSettingsAsync(), _ => CanSaveSettings());
            ResetToDefaultsCommand = new RelayCommand<object>(async _ => await ResetToDefaultsAsync());
            ImportSettingsCommand = new RelayCommand<object>(async _ => await ImportSettingsAsync());
            ExportSettingsCommand = new RelayCommand<object>(async _ => await ExportSettingsAsync());

            // Chargement initial des paramètres
            _ = LoadSettingsAsync();
        }

        #region Propriétés

        public List<TaxBracket> TaxBrackets
        {
            get => _taxBrackets;
            set => SetProperty(ref _taxBrackets, value);
        }

        public List<FamilyCharge> FamilyCharges
        {
            get => _familyCharges;
            set => SetProperty(ref _familyCharges, value);
        }

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

        public bool IsAutoSaveEnabled
        {
            get => _isAutoSaveEnabled;
            set => SetProperty(ref _isAutoSaveEnabled, value);
        }

        public string Language
        {
            get => _language;
            set => SetProperty(ref _language, value);
        }

        public string ExportDirectory
        {
            get => _exportDirectory;
            set => SetProperty(ref _exportDirectory, value);
        }

        #endregion

        #region Commandes

        public ICommand SaveSettingsCommand { get; }
        public ICommand ResetToDefaultsCommand { get; }
        public ICommand ImportSettingsCommand { get; }
        public ICommand ExportSettingsCommand { get; }

        #endregion

        #region Méthodes privées

        private async Task LoadSettingsAsync()
        {
            IsBusy = true;
            StatusMessage = "Chargement des paramètres...";

            try
            {
                var appSettings = await _dataService.LoadSettingsAsync();

                IsDarkTheme = appSettings.Theme == "Dark";
                IsAutoSaveEnabled = appSettings.AutoSave;
                Language = appSettings.Language;

                // Charger les propriétés persistées ou valeurs par défaut
                ExportDirectory = !string.IsNullOrWhiteSpace(appSettings.ExportDirectory)
                    ? appSettings.ExportDirectory
                    : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);

                TaxBrackets = appSettings.TaxBrackets != null && appSettings.TaxBrackets.Count > 0
                    ? appSettings.TaxBrackets
                    : GetDefaultTaxBrackets();

                FamilyCharges = appSettings.FamilyCharges != null && appSettings.FamilyCharges.Count > 0
                    ? appSettings.FamilyCharges
                    : GetDefaultFamilyCharges();

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

        private async Task SaveSettingsAsync()
        {
            IsBusy = true;
            StatusMessage = "Enregistrement des paramètres...";

            try
            {
                var appSettings = new AppSettings
                {
                    Theme = IsDarkTheme ? "Dark" : "Light",
                    Language = Language,
                    AutoSave = IsAutoSaveEnabled,
                    LastUpdated = DateTime.Now,
                    ExportDirectory = ExportDirectory,
                    TaxBrackets = TaxBrackets,
                    FamilyCharges = FamilyCharges
                };

                await _dataService.SaveSettingsAsync(appSettings);

                StatusMessage = "Paramètres enregistrés avec succès";
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

        private bool CanSaveSettings()
        {
            return !IsBusy && TaxBrackets != null && TaxBrackets.Count > 0 && FamilyCharges != null;
        }

        private async Task ResetToDefaultsAsync()
        {
            IsBusy = true;
            StatusMessage = "Réinitialisation des paramètres...";

            try
            {
                TaxBrackets = GetDefaultTaxBrackets();
                FamilyCharges = GetDefaultFamilyCharges();
                IsDarkTheme = false;
                IsAutoSaveEnabled = true;
                Language = "fr-FR";
                ExportDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);

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

        private void ApplyTheme()
        {
            try
            {
                var appResources = System.Windows.Application.Current.Resources;
                var themeUri = IsDarkTheme
                    ? new Uri("/Resources/DarkTheme.xaml", UriKind.Relative)
                    : new Uri("/Resources/LightTheme.xaml", UriKind.Relative);

                appResources.MergedDictionaries.Clear();
                appResources.MergedDictionaries.Add(new System.Windows.ResourceDictionary { Source = themeUri });
                appResources.MergedDictionaries.Add(new System.Windows.ResourceDictionary { Source = new Uri("/Resources/Styles.xaml", UriKind.Relative) });
            }
            catch (Exception ex)
            {
                StatusMessage = $"Erreur lors de l'application du thème: {ex.Message}";
            }
        }

        private async Task ImportSettingsAsync()
        {
            try
            {
                var dialog = new Microsoft.Win32.OpenFileDialog
                {
                    DefaultExt = ".json",
                    Filter = "Fichiers JSON (*.json)|*.json"
                };

                if (dialog.ShowDialog() == true)
                {
                    StatusMessage = "Importation des paramètres...";
                    IsBusy = true;

                    var jsonData = File.ReadAllText(dialog.FileName);
                    await _dataService.ImportAllDataAsync(jsonData);

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

        private async Task ExportSettingsAsync()
        {
            try
            {
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

                    var jsonData = await _dataService.ExportAllDataAsync();
                    File.WriteAllText(dialog.FileName, jsonData);

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

        private List<TaxBracket> GetDefaultTaxBrackets()
        {
            return new List<TaxBracket>
            {
                new TaxBracket { LowerBound = 0, UpperBound = 30000, Rate = 0.0m },
                new TaxBracket { LowerBound = 30100, UpperBound = 50000, Rate = 0.121m },
                new TaxBracket { LowerBound = 50100, UpperBound = 80000, Rate = 0.139m },
                new TaxBracket { LowerBound = 80100, UpperBound = 120000, Rate = 0.157m },
                new TaxBracket { LowerBound = 120100, UpperBound = 170000, Rate = 0.184m },
                new TaxBracket { LowerBound = 170100, UpperBound = 250000, Rate = 0.217m },
                new TaxBracket { LowerBound = 250100, UpperBound = decimal.MaxValue, Rate = 0.25m }
            };
        }

        private List<FamilyCharge> GetDefaultFamilyCharges()
        {
            return new List<FamilyCharge>
            {
                new FamilyCharge { ChargeCount = 0, AbatementPercentage = 0.0m },
                new FamilyCharge { ChargeCount = 1, AbatementPercentage = 0.05m },
                new FamilyCharge { ChargeCount = 2, AbatementPercentage = 0.10m },
                new FamilyCharge { ChargeCount = 3, AbatementPercentage = 0.15m },
                new FamilyCharge { ChargeCount = 4, AbatementPercentage = 0.20m },
                new FamilyCharge { ChargeCount = 5, AbatementPercentage = 0.25m }
            };
        }

        #endregion
    }
}