using System;
using System.Collections.Generic;
using System.Text.Json;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Timers;
using IUTSCalculator.Models;

namespace IUTSCalculator.Services
{
    /// <summary>
    /// Service pour le chargement et la gestion des données fiscales
    /// </summary>
    public class TaxDataService : IDisposable
    {
        #region Constants
        private const string TAX_BRACKETS_FILE = "taxBrackets.json";
        private const string TAX_CONSTANTS_FILE = "taxConstants.json";
        private const int DEFAULT_CACHE_DURATION_MINUTES = 60; // 1 heure par défaut
        #endregion

        #region Members
        private readonly string _dataDirectory;
        private readonly TimeSpan _cacheDuration;
        private readonly System.Threading.Timer _cacheRefreshTimer;
        private readonly SemaphoreSlim _cacheLock = new SemaphoreSlim(1, 1);
        private DateTime _lastRefreshTime = DateTime.MinValue;
        private bool _isDisposed = false;
        #endregion

        #region Properties
        /// <summary>
        /// Tranches d'imposition actuellement en cache
        /// </summary>
        public List<TaxBracket> TaxBrackets { get; private set; }

        /// <summary>
        /// Taux d'abattement pour charges de famille actuellement en cache
        /// </summary>
        public Dictionary<int, decimal> FamilyChargeRates { get; private set; }

        /// <summary>
        /// Valeurs constantes utilisées dans les calculs fiscaux
        /// </summary>
        public TaxConstants ConstantValues { get; private set; }
        #endregion

        #region Events
        /// <summary>
        /// Événement déclenché lorsque les données fiscales sont mises à jour
        /// </summary>
        public event EventHandler<TaxDataUpdatedEventArgs> TaxDataUpdated;
        #endregion

        #region Constructor
        /// <summary>
        /// Constructeur du service de données fiscales
        /// </summary>
        /// <param name="dataDirectory">Répertoire contenant les fichiers de données fiscales</param>
        /// <param name="cacheDurationMinutes">Durée de mise en cache en minutes (60 minutes par défaut)</param>
        public TaxDataService(string dataDirectory, int cacheDurationMinutes = DEFAULT_CACHE_DURATION_MINUTES)
        {
            _dataDirectory = string.IsNullOrEmpty(dataDirectory)
                ? AppDomain.CurrentDomain.BaseDirectory
                : dataDirectory;

            _cacheDuration = TimeSpan.FromMinutes(cacheDurationMinutes);

            // Initialisation avec les données par défaut (cas de fallback)
            InitializeDefaultData();

            // Charger les données au démarrage
            LoadTaxData().Wait();

            // Configurer le timer pour le rechargement périodique
            _cacheRefreshTimer = new System.Threading.Timer(
                async _ => await RefreshCacheIfNeeded(),
                null,
                _cacheDuration,
                _cacheDuration);
        }
        #endregion

        #region Public Methods
        /// <summary>
        /// Recharge les données fiscales de manière forcée
        /// </summary>
        public async Task ReloadTaxDataAsync()
        {
            await LoadTaxData(forceReload: true);
        }

        /// <summary>
        /// Vérifie si les données sont périmées et doivent être rechargées
        /// </summary>
        /// <returns>True si les données doivent être rechargées, False sinon</returns>
        public bool ShouldReloadData()
        {
            return DateTime.Now - _lastRefreshTime > _cacheDuration;
        }
        #endregion

        #region Private Methods
        /// <summary>
        /// Initialise les données par défaut (en cas d'échec de chargement)
        /// </summary>
        private void InitializeDefaultData()
        {
            // Valeurs par défaut pour les constantes fiscales
            ConstantValues = new TaxConstants
            {
                MaxHousingAllowanceRate = 0.20m,
                MaxHousingAllowanceAmount = 50000m,
                MaxFunctionAllowanceRate = 0.05m,
                MaxFunctionAllowanceAmount = 30000m,
                MaxTransportAllowanceRate = 0.05m,
                MaxTransportAllowanceAmount = 20000m,
                MaxPensionContributionRate = 0.08m,
                ProfessionalDeductionHigherCategories = 0.20m,
                ProfessionalDeductionOtherCategories = 0.25m
            };

            // Valeurs par défaut pour les tranches d'imposition
            TaxBrackets = new List<TaxBracket>
            {
                new TaxBracket(0m, 30000m, 0m),
                new TaxBracket(30100m, 50000m, 12.10m),
                new TaxBracket(50100m, 80000m, 13.90m),
                new TaxBracket(80100m, 120000m, 15.70m),
                new TaxBracket(120100m, 170000m, 18.40m),
                new TaxBracket(170100m, 250000m, 21.70m),
                new TaxBracket(250100m, decimal.MaxValue, 25.00m)
            };

            // Valeurs par défaut pour les abattements pour charges de famille
            FamilyChargeRates = new Dictionary<int, decimal>
            {
                { 0, 0m },
                { 1, 0.08m },
                { 2, 0.10m },
                { 3, 0.12m },
                { 4, 0.14m },
                { 5, 0.16m },
                { 6, 0.18m },
                { 7, 0.20m }
            };
        }

        /// <summary>
        /// Charge les données fiscales depuis les fichiers JSON
        /// </summary>
        /// <param name="forceReload">Force le rechargement même si le cache est valide</param>
        private async Task LoadTaxData(bool forceReload = false)
        {
            if (!forceReload && !ShouldReloadData())
                return;

            await _cacheLock.WaitAsync();
            try
            {
                // Vérifier une seconde fois après avoir acquis le verrou
                if (!forceReload && !ShouldReloadData())
                    return;

                // Charge les tranches d'imposition
                var taxBrackets = await LoadTaxBracketsAsync();

                // Charge les constantes fiscales et les taux d'abattement
                var taxConstantsResult = await LoadTaxConstantsAsync();

                // Si les deux chargements ont réussi, mettre à jour les données
                if (taxBrackets != null && taxConstantsResult.ConstantValues != null && taxConstantsResult.FamilyChargeRates != null)
                {
                    bool taxBracketsChanged = !AreTaxBracketsEqual(TaxBrackets, taxBrackets);
                    bool taxConstantsChanged = !AreTaxConstantsEqual(ConstantValues, taxConstantsResult.ConstantValues) ||
                                            !AreFamilyChargeRatesEqual(FamilyChargeRates, taxConstantsResult.FamilyChargeRates);

                    TaxBrackets = taxBrackets;
                    ConstantValues = taxConstantsResult.ConstantValues;
                    FamilyChargeRates = taxConstantsResult.FamilyChargeRates;
                    _lastRefreshTime = DateTime.Now;

                    // Déclencher l'événement si des données ont changé
                    if (taxBracketsChanged || taxConstantsChanged)
                    {
                        OnTaxDataUpdated(new TaxDataUpdatedEventArgs
                        {
                            TaxBracketsUpdated = taxBracketsChanged,
                            TaxConstantsUpdated = taxConstantsChanged,
                            UpdateTime = _lastRefreshTime
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                // Logger l'erreur (Utiliser ILogger si disponible)
                Console.WriteLine($"Erreur lors du chargement des données fiscales: {ex.Message}");

                // En cas d'erreur, on garde les données existantes ou les valeurs par défaut
                if (TaxBrackets == null || ConstantValues == null || FamilyChargeRates == null)
                {
                    InitializeDefaultData();
                }
            }
            finally
            {
                _cacheLock.Release();
            }
        }

        /// <summary>
        /// Rafraîchit le cache si nécessaire (appelé par le timer)
        /// </summary>
        private async Task RefreshCacheIfNeeded()
        {
            if (ShouldReloadData())
            {
                await LoadTaxData();
            }
        }

        /// <summary>
        /// Méthode appelée par le Timer de rafraîchissement du cache
        /// </summary>
        private async void OnCacheRefreshTimerElapsed(object state)
        {
            await RefreshCacheIfNeeded();
        }

        /// <summary>
        /// Charge les tranches d'imposition depuis le fichier JSON
        /// </summary>
        private async Task<List<TaxBracket>> LoadTaxBracketsAsync()
        {
            try
            {
                string filePath = Path.Combine(_dataDirectory, TAX_BRACKETS_FILE);
                if (!File.Exists(filePath))
                {
                    Console.WriteLine($"Fichier non trouvé: {filePath}");
                    return null;
                }

                // Remplacer ReadAllTextAsync par sa version compatible
                string jsonContent = await Task.Run(() => File.ReadAllText(filePath));
                var document = JsonDocument.Parse(jsonContent);

                if (document.RootElement.TryGetProperty("taxBrackets", out var bracketsElement))
                {
                    var brackets = new List<TaxBracket>();
                    foreach (var element in bracketsElement.EnumerateArray())
                    {
                        if (element.TryGetProperty("lowerBound", out var lowerBoundElement) &&
                            element.TryGetProperty("upperBound", out var upperBoundElement) &&
                            element.TryGetProperty("rate", out var rateElement))
                        {
                            decimal lowerBound = lowerBoundElement.GetDecimal();
                            decimal upperBound = upperBoundElement.GetDecimal();
                            decimal rate = rateElement.GetDecimal();

                            brackets.Add(new TaxBracket(lowerBound, upperBound, rate));
                        }
                    }

                    return brackets;
                }

                return null;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Erreur lors du chargement des tranches d'imposition: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Charge les constantes fiscales et les taux d'abattement depuis le fichier JSON
        /// </summary>
        private async Task<(TaxConstants ConstantValues, Dictionary<int, decimal> FamilyChargeRates)> LoadTaxConstantsAsync()
        {
            try
            {
                string filePath = Path.Combine(_dataDirectory, TAX_CONSTANTS_FILE);
                if (!File.Exists(filePath))
                {
                    Console.WriteLine($"Fichier non trouvé: {filePath}");
                    return (null, null);
                }

                // Remplacer ReadAllTextAsync par sa version compatible
                string jsonContent = await Task.Run(() => File.ReadAllText(filePath));
                var document = JsonDocument.Parse(jsonContent);

                var constants = new TaxConstants();
                var familyRates = new Dictionary<int, decimal>();

                // Charger les constantes
                if (document.RootElement.TryGetProperty("constantValues", out var constantsElement))
                {
                    if (constantsElement.TryGetProperty("maxHousingAllowanceRate", out var prop))
                        constants.MaxHousingAllowanceRate = prop.GetDecimal();

                    if (constantsElement.TryGetProperty("maxHousingAllowanceAmount", out prop))
                        constants.MaxHousingAllowanceAmount = prop.GetDecimal();

                    if (constantsElement.TryGetProperty("maxFunctionAllowanceRate", out prop))
                        constants.MaxFunctionAllowanceRate = prop.GetDecimal();

                    if (constantsElement.TryGetProperty("maxFunctionAllowanceAmount", out prop))
                        constants.MaxFunctionAllowanceAmount = prop.GetDecimal();

                    if (constantsElement.TryGetProperty("maxTransportAllowanceRate", out prop))
                        constants.MaxTransportAllowanceRate = prop.GetDecimal();

                    if (constantsElement.TryGetProperty("maxTransportAllowanceAmount", out prop))
                        constants.MaxTransportAllowanceAmount = prop.GetDecimal();

                    if (constantsElement.TryGetProperty("maxPensionContributionRate", out prop))
                        constants.MaxPensionContributionRate = prop.GetDecimal();

                    if (constantsElement.TryGetProperty("professionalDeductionHigherCategories", out prop))
                        constants.ProfessionalDeductionHigherCategories = prop.GetDecimal();

                    if (constantsElement.TryGetProperty("professionalDeductionOtherCategories", out prop))
                        constants.ProfessionalDeductionOtherCategories = prop.GetDecimal();
                }

                // Charger les taux d'abattement pour charges de famille
                if (document.RootElement.TryGetProperty("familyChargeRates", out var ratesElement))
                {
                    foreach (var property in ratesElement.EnumerateObject())
                    {
                        if (int.TryParse(property.Name, out int charges))
                        {
                            familyRates[charges] = property.Value.GetDecimal();
                        }
                    }
                }

                return (constants, familyRates);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Erreur lors du chargement des constantes fiscales: {ex.Message}");
                return (null, null);
            }
        }

        /// <summary>
        /// Compare deux listes de tranches d'imposition
        /// </summary>
        private bool AreTaxBracketsEqual(List<TaxBracket> list1, List<TaxBracket> list2)
        {
            if (list1 == null && list2 == null) return true;
            if (list1 == null || list2 == null) return false;
            if (list1.Count != list2.Count) return false;

            for (int i = 0; i < list1.Count; i++)
            {
                if (list1[i].LowerBound != list2[i].LowerBound ||
                    list1[i].UpperBound != list2[i].UpperBound ||
                    list1[i].Rate != list2[i].Rate)
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Compare deux ensembles de constantes fiscales
        /// </summary>
        private bool AreTaxConstantsEqual(TaxConstants constants1, TaxConstants constants2)
        {
            if (constants1 == null && constants2 == null) return true;
            if (constants1 == null || constants2 == null) return false;

            return constants1.MaxHousingAllowanceRate == constants2.MaxHousingAllowanceRate &&
                   constants1.MaxHousingAllowanceAmount == constants2.MaxHousingAllowanceAmount &&
                   constants1.MaxFunctionAllowanceRate == constants2.MaxFunctionAllowanceRate &&
                   constants1.MaxFunctionAllowanceAmount == constants2.MaxFunctionAllowanceAmount &&
                   constants1.MaxTransportAllowanceRate == constants2.MaxTransportAllowanceRate &&
                   constants1.MaxTransportAllowanceAmount == constants2.MaxTransportAllowanceAmount &&
                   constants1.MaxPensionContributionRate == constants2.MaxPensionContributionRate &&
                   constants1.ProfessionalDeductionHigherCategories == constants2.ProfessionalDeductionHigherCategories &&
                   constants1.ProfessionalDeductionOtherCategories == constants2.ProfessionalDeductionOtherCategories;
        }

        /// <summary>
        /// Compare deux dictionnaires de taux d'abattement pour charges de famille
        /// </summary>
        private bool AreFamilyChargeRatesEqual(Dictionary<int, decimal> dict1, Dictionary<int, decimal> dict2)
        {
            if (dict1 == null && dict2 == null) return true;
            if (dict1 == null || dict2 == null) return false;
            if (dict1.Count != dict2.Count) return false;

            foreach (var key in dict1.Keys)
            {
                if (!dict2.TryGetValue(key, out decimal value) || value != dict1[key])
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Déclenche l'événement de mise à jour des données fiscales
        /// </summary>
        protected virtual void OnTaxDataUpdated(TaxDataUpdatedEventArgs e)
        {
            TaxDataUpdated?.Invoke(this, e);
        }
        #endregion

        #region IDisposable Implementation
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_isDisposed)
            {
                if (disposing)
                {
                    _cacheRefreshTimer?.Dispose();
                    _cacheLock?.Dispose();
                }

                _isDisposed = true;
            }
        }
        #endregion
    }

    /// <summary>
    /// Arguments de l'événement de mise à jour des données fiscales
    /// </summary>
    public class TaxDataUpdatedEventArgs : EventArgs
    {
        /// <summary>
        /// Indique si les tranches d'imposition ont été mises à jour
        /// </summary>
        public bool TaxBracketsUpdated { get; set; }

        /// <summary>
        /// Indique si les constantes fiscales ont été mises à jour
        /// </summary>
        public bool TaxConstantsUpdated { get; set; }

        /// <summary>
        /// Date et heure de la mise à jour
        /// </summary>
        public DateTime UpdateTime { get; set; }
    }

    /// <summary>
    /// Constantes fiscales utilisées dans les calculs
    /// </summary>
    public class TaxConstants
    {
        /// <summary>
        /// Taux maximal pour l'indemnité de logement (pourcentage du salaire brut)
        /// </summary>
        public decimal MaxHousingAllowanceRate { get; set; }

        /// <summary>
        /// Montant maximal pour l'indemnité de logement (en FCFA)
        /// </summary>
        public decimal MaxHousingAllowanceAmount { get; set; }

        /// <summary>
        /// Taux maximal pour l'indemnité de fonction (pourcentage du salaire brut)
        /// </summary>
        public decimal MaxFunctionAllowanceRate { get; set; }

        /// <summary>
        /// Montant maximal pour l'indemnité de fonction (en FCFA)
        /// </summary>
        public decimal MaxFunctionAllowanceAmount { get; set; }

        /// <summary>
        /// Taux maximal pour l'indemnité de transport (pourcentage du salaire brut)
        /// </summary>
        public decimal MaxTransportAllowanceRate { get; set; }

        /// <summary>
        /// Montant maximal pour l'indemnité de transport (en FCFA)
        /// </summary>
        public decimal MaxTransportAllowanceAmount { get; set; }

        /// <summary>
        /// Taux maximal pour les cotisations de pension/retraite (pourcentage du salaire de base)
        /// </summary>
        public decimal MaxPensionContributionRate { get; set; }

        /// <summary>
        /// Taux d'abattement forfaitaire pour les catégories supérieures
        /// </summary>
        public decimal ProfessionalDeductionHigherCategories { get; set; }

        /// <summary>
        /// Taux d'abattement forfaitaire pour les autres catégories
        /// </summary>
        public decimal ProfessionalDeductionOtherCategories { get; set; }
    }
}