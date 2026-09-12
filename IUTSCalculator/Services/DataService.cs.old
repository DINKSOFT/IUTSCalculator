using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using IUTSCalculator.Models;
using System.IO.Compression;
using System.Globalization;
using IUTSCalculator.ViewModels;
using static System.Windows.Forms.VisualStyles.VisualStyleElement.Window;
using System.Collections.ObjectModel;


namespace IUTSCalculator.Services
{
    /// <summary>
    /// Service de gestion des données pour l'application IUTSCalculator
    /// Permet la persistance des données d'employés et d'historique des calculs d'impôt 
    /// </summary>
    
    public class DataService
    {
        #region Constantes et champs privés
        public ObservableCollection<string> EmployeeCategories { get; }
        private readonly string _appDataFolder;
        private readonly string _employeesFilePath;
        private readonly string _calculationsFilePath;
        private readonly string _settingsFilePath;

        private List<Employee> _cachedEmployees;
        private List<TaxCalculation> _cachedCalculations;
        private AppSettings _cachedSettings;

        private readonly JsonSerializerOptions _jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        #endregion

        #region Constructeur

        public DataService()
        {
            // Création du dossier d'application dans AppData
            _appDataFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "IUTSCalculator");

            Directory.CreateDirectory(_appDataFolder);

            _employeesFilePath = Path.Combine(_appDataFolder, "employees.json");
            _calculationsFilePath = Path.Combine(_appDataFolder, "calculations.json");
            _settingsFilePath = Path.Combine(_appDataFolder, "settings.json");

            // Initialisation des caches
            _cachedEmployees = new List<Employee>();
            _cachedCalculations = new List<TaxCalculation>();
            _cachedSettings = new AppSettings();

            EmployeeCategories = new ObservableCollection<string>
        {
            "Cadre Supérieur",
            "Cadre Inférieur"
        };
        }

        #endregion

        #region Gestion des employés



        /// <summary>
        /// Charge la liste des employés depuis le stockage persistant
        /// </summary>
        public async Task<List<Employee>> LoadEmployeesAsync()
        {
            try
            {
                if (File.Exists(_employeesFilePath))
                {
                    // Utilisation de Task.Run pour exécuter la méthode synchrone File.ReadAllText de manière asynchrone
                    string json = await Task.Run(() => File.ReadAllText(_employeesFilePath));
                    _cachedEmployees = JsonSerializer.Deserialize<List<Employee>>(json, _jsonOptions) ?? new List<Employee>();
                }

                return _cachedEmployees;
            }
            catch (Exception ex)
            {
                // En cas d'erreur, on retourne la liste en cache ou une nouvelle liste
                System.Diagnostics.Debug.WriteLine($"Erreur lors du chargement des employés: {ex.Message}");
                return _cachedEmployees ?? new List<Employee>();
            }
        }

        /// <summary>
        /// Sauvegarde la liste des employés dans le stockage persistant
        /// </summary>
        public async Task SaveEmployeesAsync(List<Employee> employees)
        {
            try
            {
                _cachedEmployees = employees ?? new List<Employee>();
                string json = JsonSerializer.Serialize(_cachedEmployees, _jsonOptions);

                // Utilisation de Task.Run pour exécuter la méthode synchrone File.WriteAllText de manière asynchrone
                await Task.Run(() => File.WriteAllText(_employeesFilePath, json));
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Erreur lors de la sauvegarde des employés: {ex.Message}");
                throw; // Remonter l'exception pour informer l'appelant
            }
        }

        /// <summary>
        /// Ajoute un nouvel employé et sauvegarde la liste mise à jour
        /// </summary>
        public async Task AddEmployeeAsync(Employee employee)
        {
            if (employee == null)
                throw new ArgumentNullException(nameof(employee));

            // Génération d'un ID s'il n'existe pas déjà
            if (string.IsNullOrEmpty(employee.EmployeeId))
                employee.EmployeeId = Guid.NewGuid().ToString();

            // Chargement de la liste actuelle
            await LoadEmployeesAsync();

            // Ajout du nouvel employé
            _cachedEmployees.Add(employee);

            // Sauvegarde de la liste mise à jour
            await SaveEmployeesAsync(_cachedEmployees);
        }

        /// <summary>
        /// Met à jour un employé existant
        /// </summary>
        public async Task UpdateEmployeeAsync(Employee employee)
        {
            if (employee == null || string.IsNullOrEmpty(employee.EmployeeId))
                throw new ArgumentException("L'employé doit avoir un ID valide", nameof(employee));

            // Chargement de la liste actuelle
            await LoadEmployeesAsync();

            // Recherche de l'employé à mettre à jour
            int index = _cachedEmployees.FindIndex(e => e.EmployeeId == employee.EmployeeId);
            if (index == -1)
                throw new KeyNotFoundException($"Employé avec ID {employee.EmployeeId} non trouvé");

            // Mise à jour de l'employé
            _cachedEmployees[index] = employee;

            // Sauvegarde de la liste mise à jour
            await SaveEmployeesAsync(_cachedEmployees);
        }

        /// <summary>
        /// Supprime un employé par son ID
        /// </summary>
        public async Task DeleteEmployeeAsync(string employeeId)
        {
            if (string.IsNullOrEmpty(employeeId))
                throw new ArgumentException("L'ID de l'employé est requis", nameof(employeeId));

            // Chargement de la liste actuelle
            await LoadEmployeesAsync();

            // Recherche et suppression de l'employé
            Employee employeeToRemove = _cachedEmployees.FirstOrDefault(e => e.EmployeeId == employeeId);
            if (employeeToRemove != null)
            {
                _cachedEmployees.Remove(employeeToRemove);
                await SaveEmployeesAsync(_cachedEmployees);
            }
        }

        /// <summary>
        /// Récupère un employé par son ID
        /// </summary>
        public async Task<Employee> GetEmployeeByIdAsync(string employeeId)
        {
            if (string.IsNullOrEmpty(employeeId))
                throw new ArgumentException("L'ID de l'employé est requis", nameof(employeeId));

            // Chargement de la liste actuelle
            await LoadEmployeesAsync();

            // Recherche et retour de l'employé
            return _cachedEmployees.FirstOrDefault(e => e.EmployeeId == employeeId);
        }

        #endregion

        #region Gestion de l'historique des calculs

        /// <summary>
        /// Charge l'historique des calculs depuis le stockage persistant
        /// </summary>
        public async Task<List<TaxCalculation>> LoadCalculationsHistoryAsync()
        {
            try
            {
                if (File.Exists(_calculationsFilePath))
                {
                    // Utilisation de Task.Run pour exécuter la méthode synchrone File.ReadAllText de manière asynchrone
                    string json = await Task.Run(() => File.ReadAllText(_calculationsFilePath));
                    _cachedCalculations = JsonSerializer.Deserialize<List<TaxCalculation>>(json, _jsonOptions) ?? new List<TaxCalculation>();
                }

                return _cachedCalculations;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Erreur lors du chargement de l'historique des calculs: {ex.Message}");
                return _cachedCalculations ?? new List<TaxCalculation>();
            }
        }

        /// <summary>
        /// Sauvegarde l'historique des calculs dans le stockage persistant
        /// </summary>
        public async Task SaveCalculationsHistoryAsync(List<TaxCalculation> calculations)
        {
            try
            {
                _cachedCalculations = calculations ?? new List<TaxCalculation>();
                string json = JsonSerializer.Serialize(_cachedCalculations, _jsonOptions);

                // Utilisation de Task.Run pour exécuter la méthode synchrone File.WriteAllText de manière asynchrone
                await Task.Run(() => File.WriteAllText(_calculationsFilePath, json));
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Erreur lors de la sauvegarde de l'historique des calculs: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Ajoute un nouveau calcul à l'historique
        /// </summary>
        public async Task AddCalculationToHistoryAsync(TaxCalculation calculation)
        {
            if (calculation == null)
                throw new ArgumentNullException(nameof(calculation));

            // Génération d'un ID s'il n'existe pas déjà
            if (string.IsNullOrEmpty(calculation.Id))
                calculation.Id = Guid.NewGuid().ToString();

            // Vérification que la date de calcul est définie
            if (calculation.CalculationDate == default)
                calculation.CalculationDate = DateTime.Now;

            // Chargement de l'historique actuel
            await LoadCalculationsHistoryAsync();

            // Ajout du nouveau calcul
            _cachedCalculations.Add(calculation);

            // Sauvegarde de l'historique mis à jour
            await SaveCalculationsHistoryAsync(_cachedCalculations);
        }

        /// <summary>
        /// Supprime un calcul de l'historique par son ID
        /// </summary>
        public async Task DeleteCalculationFromHistoryAsync(string calculationId)
        {
            if (string.IsNullOrEmpty(calculationId))
                throw new ArgumentException("L'ID du calcul est requis", nameof(calculationId));

            // Chargement de l'historique actuel
            await LoadCalculationsHistoryAsync();

            // Recherche et suppression du calcul
            TaxCalculation calculationToRemove = _cachedCalculations.FirstOrDefault(c => c.Id == calculationId);
            if (calculationToRemove != null)
            {
                _cachedCalculations.Remove(calculationToRemove);
                await SaveCalculationsHistoryAsync(_cachedCalculations);
            }
            else
            {
                throw new KeyNotFoundException($"Calcul avec ID {calculationId} non trouvé");
            }
        }

        /// <summary>
        /// Supprime un calcul de l'historique par son ID
        /// </summary>
        public async Task ExportToExcelAsync(TaxCalculation calculation)
        {
            if (calculation == null)
                throw new ArgumentNullException(nameof(calculation));

            // Récupérer le dossier Documents de l'utilisateur
            string documentsFolder = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);

            // Générer un nom de fichier avec date et heure
            string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            string fileName = $"DetailsCalcul_{timestamp}.xlsx";
            string filePath = Path.Combine(documentsFolder, fileName);

            // Appeler le service de données pour exporter le détail des calculs
            await Task.Run(() => ExcelExportHelper.ExportCalculationDetailsToExcel(calculation, filePath));
        }

        /// <summary>
        /// Récupère l'historique des calculs pour un employé spécifique
        /// </summary>
        public async Task<List<TaxCalculation>> GetCalculationHistoryForEmployeeAsync(string employeeId)
        {
            if (string.IsNullOrEmpty(employeeId))
                throw new ArgumentException("L'ID de l'employé est requis", nameof(employeeId));

            // Chargement de l'historique actuel
            await LoadCalculationsHistoryAsync();

            // Filtrage et retour des calculs pour l'employé spécifié
            return _cachedCalculations
                .Where(c => c.Employee?.EmployeeId == employeeId)
                .OrderByDescending(c => c.CalculationDate)
                .ToList();
        }

        /// <summary>
        /// Récupère l'historique des calculs pour une période spécifique
        /// </summary>
        public async Task<List<TaxCalculation>> GetCalculationHistoryForPeriodAsync(DateTime startDate, DateTime endDate)
        {
            // Chargement de l'historique actuel
            await LoadCalculationsHistoryAsync();

            // Filtrage et retour des calculs pour la période spécifiée
            return _cachedCalculations
                .Where(c => c.CalculationDate >= startDate && c.CalculationDate <= endDate)
                .OrderByDescending(c => c.CalculationDate)
                .ToList();
        }

        #endregion

        #region Gestion des paramètres de l'application

        /// <summary>
        /// Charge les paramètres de l'application depuis le stockage persistant
        /// </summary>
        public async Task<AppSettings> LoadSettingsAsync()
        {
            try
            {
                if (File.Exists(_settingsFilePath))
                {
                    // Utilisation de Task.Run pour exécuter la méthode synchrone File.ReadAllText de manière asynchrone
                    string json = await Task.Run(() => File.ReadAllText(_settingsFilePath));
                    _cachedSettings = JsonSerializer.Deserialize<AppSettings>(json, _jsonOptions) ?? new AppSettings();
                }
                else
                {
                    // Création des paramètres par défaut si le fichier n'existe pas
                    _cachedSettings = new AppSettings
                    {
                        Theme = "Light",
                        Language = "fr-FR",
                        AutoSave = true,
                        LastUpdated = DateTime.Now
                    };
                    await SaveSettingsAsync(_cachedSettings);
                }

                return _cachedSettings;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Erreur lors du chargement des paramètres: {ex.Message}");
                return _cachedSettings ?? new AppSettings();
            }
        }

        /// <summary>
        /// Sauvegarde les paramètres de l'application dans le stockage persistant
        /// </summary>
        public async Task SaveSettingsAsync(AppSettings settings)
        {
            try
            {
                _cachedSettings = settings ?? new AppSettings();
                _cachedSettings.LastUpdated = DateTime.Now;

                string json = JsonSerializer.Serialize(_cachedSettings, _jsonOptions);

                // Utilisation de Task.Run pour exécuter la méthode synchrone File.WriteAllText de manière asynchrone
                await Task.Run(() => File.WriteAllText(_settingsFilePath, json));
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Erreur lors de la sauvegarde des paramètres: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Met à jour un paramètre spécifique de l'application
        /// </summary>
        public async Task UpdateSettingAsync<T>(string settingName, T value)
        {
            // Chargement des paramètres actuels
            await LoadSettingsAsync();

            // Utilisation de la réflexion pour mettre à jour le paramètre
            var property = typeof(AppSettings).GetProperty(settingName);
            if (property != null && property.CanWrite)
            {
                property.SetValue(_cachedSettings, value);
                await SaveSettingsAsync(_cachedSettings);
            }
            else
            {
                throw new ArgumentException($"Le paramètre '{settingName}' n'existe pas ou ne peut pas être modifié");
            }
        }

        #endregion

        #region Exportation des données

        /// <summary>
        /// Exporte toutes les données de l'application en JSON
        /// </summary>
        public async Task<string> ExportAllDataAsync()
        {
            try
            {
                // Chargement de toutes les données
                await LoadEmployeesAsync();
                await LoadCalculationsHistoryAsync();
                await LoadSettingsAsync();

                // Création d'un objet de données pour l'export
                var exportData = new
                {
                    ExportDate = DateTime.Now,
                    Version = "1.0",
                    Employees = _cachedEmployees,
                    Calculations = _cachedCalculations,
                    Settings = _cachedSettings
                };

                // Conversion en JSON et retour
                return JsonSerializer.Serialize(exportData, _jsonOptions);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Erreur lors de l'exportation des données: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Importe toutes les données de l'application depuis un JSON
        /// </summary>
        public async Task ImportAllDataAsync(string jsonData)
        {
            try
            {
                using (JsonDocument document = JsonDocument.Parse(jsonData))
                {
                    JsonElement root = document.RootElement;

                    // Extraction et sauvegarde des employés
                    if (root.TryGetProperty("Employees", out JsonElement employeesElement))
                    {
                        var employees = JsonSerializer.Deserialize<List<Employee>>(
                            employeesElement.GetRawText(), _jsonOptions);

                        if (employees != null)
                            await SaveEmployeesAsync(employees);
                    }

                    // Extraction et sauvegarde de l'historique des calculs
                    if (root.TryGetProperty("Calculations", out JsonElement calculationsElement))
                    {
                        var calculations = JsonSerializer.Deserialize<List<TaxCalculation>>(
                            calculationsElement.GetRawText(), _jsonOptions);

                        if (calculations != null)
                            await SaveCalculationsHistoryAsync(calculations);
                    }

                    // Extraction et sauvegarde des paramètres
                    if (root.TryGetProperty("Settings", out JsonElement settingsElement))
                    {
                        var settings = JsonSerializer.Deserialize<AppSettings>(
                            settingsElement.GetRawText(), _jsonOptions);

                        if (settings != null)
                            await SaveSettingsAsync(settings);
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Erreur lors de l'importation des données: {ex.Message}");
                throw;
            }
        }

        #endregion

        #region Maintenance des données

        /// <summary>
        /// Effectue une sauvegarde des données dans un fichier ZIP
        /// </summary>
        public async Task<string> BackupDataAsync()
        {
            try
            {
                // Génération du nom de fichier de sauvegarde
                string backupFileName = $"IUTSCalculator_Backup_{DateTime.Now:yyyyMMdd_HHmmss}.zip";
                string backupFilePath = Path.Combine(_appDataFolder, backupFileName);

                // Création d'un fichier temporaire pour l'export JSON
                string exportJson = await ExportAllDataAsync();
                string tempJsonPath = Path.Combine(_appDataFolder, "export_temp.json");

                // Utilisation de Task.Run pour exécuter la méthode synchrone File.WriteAllText de manière asynchrone
                await Task.Run(() => File.WriteAllText(tempJsonPath, exportJson));

                // Assurez-vous que les fichiers de données existent avant de créer l'archive
                List<string> filesToArchive = new List<string>();
                if (File.Exists(tempJsonPath)) filesToArchive.Add(tempJsonPath);
                if (File.Exists(_employeesFilePath)) filesToArchive.Add(_employeesFilePath);
                if (File.Exists(_calculationsFilePath)) filesToArchive.Add(_calculationsFilePath);
                if (File.Exists(_settingsFilePath)) filesToArchive.Add(_settingsFilePath);

                // Création de l'archive ZIP à partir des fichiers existants
                if (filesToArchive.Count > 0)
                {
                    // Si un fichier de backup existe déjà, le supprimer
                    if (File.Exists(backupFilePath))
                        File.Delete(backupFilePath);

                    // Utilisation de la méthode CreateFromDirectory pour créer l'archive
                    // au lieu de ZipFile.Open qui peut poser des problèmes
                    string tempFolder = Path.Combine(_appDataFolder, "temp_backup");
                    Directory.CreateDirectory(tempFolder);

                    foreach (var file in filesToArchive)
                    {
                        File.Copy(file, Path.Combine(tempFolder, Path.GetFileName(file)), true);
                    }

                    // Envelopper l'opération ZipFile.CreateFromDirectory dans une tâche asynchrone
                    await Task.Run(() => ZipFile.CreateFromDirectory(tempFolder, backupFilePath));

                    // Nettoyage du dossier temporaire
                    if (Directory.Exists(tempFolder))
                    {
                        foreach (var file in Directory.GetFiles(tempFolder))
                        {
                            File.Delete(file);
                        }
                        Directory.Delete(tempFolder);
                    }
                }

                // Suppression du fichier temporaire
                if (File.Exists(tempJsonPath))
                    File.Delete(tempJsonPath);

                return backupFilePath;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Erreur lors de la sauvegarde des données: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Efface toutes les données de l'application
        /// </summary>
        public async Task ClearAllDataAsync()
        {
            try
            {
                // Réinitialisation des données en mémoire
                _cachedEmployees = new List<Employee>();
                _cachedCalculations = new List<TaxCalculation>();
                _cachedSettings = new AppSettings
                {
                    Theme = "Light",
                    Language = "fr-FR",
                    AutoSave = true,
                    LastUpdated = DateTime.Now
                };

                // Sauvegarde des données vides
                await SaveEmployeesAsync(_cachedEmployees);
                await SaveCalculationsHistoryAsync(_cachedCalculations);
                await SaveSettingsAsync(_cachedSettings);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Erreur lors de l'effacement des données: {ex.Message}");
                throw;
            }
        }

        #endregion
        #region Gestion des tranches d'impôt et charges familiales

        /// <summary>
        /// Récupère la liste des tranches d'imposition
        /// </summary>
        /// <returns>Liste des tranches d'imposition</returns>
        public List<TaxBracket> GetTaxBrackets()
        {
            try
            {
                string filePath = Path.Combine(_appDataFolder, "taxBrackets.json");

                if (File.Exists(filePath))
                {
                    string json = File.ReadAllText(filePath);
                    return JsonSerializer.Deserialize<List<TaxBracket>>(json, _jsonOptions) ?? CreateDefaultTaxBrackets();
                }

                // Si le fichier n'existe pas, créer et retourner les tranches par défaut
                var defaultBrackets = CreateDefaultTaxBrackets();
                SaveTaxBrackets(defaultBrackets);
                return defaultBrackets;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Erreur lors du chargement des tranches d'imposition: {ex.Message}");
                return CreateDefaultTaxBrackets();
            }
        }

        /// <summary>
        /// Sauvegarde la liste des tranches d'imposition
        /// </summary>
        /// <param name="brackets">Liste des tranches d'imposition à sauvegarder</param>
        public void SaveTaxBrackets(List<TaxBracket> brackets)
        {
            try
            {
                string filePath = Path.Combine(_appDataFolder, "taxBrackets.json");
                string json = JsonSerializer.Serialize(brackets ?? CreateDefaultTaxBrackets(), _jsonOptions);
                File.WriteAllText(filePath, json);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Erreur lors de la sauvegarde des tranches d'imposition: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Crée une liste de tranches d'imposition par défaut
        /// </summary>
        /// <returns>Liste des tranches d'imposition par défaut</returns>
        private List<TaxBracket> CreateDefaultTaxBrackets()
        {
            return new List<TaxBracket>
    {
        new TaxBracket { LowerBound = 0, UpperBound = 25000, Rate = 0.0m },
        new TaxBracket { LowerBound = 25001, UpperBound = 50000, Rate = 0.05m },
        new TaxBracket { LowerBound = 50001, UpperBound = 150000, Rate = 0.10m },
        new TaxBracket { LowerBound = 150001, UpperBound = 300000, Rate = 0.15m },
        new TaxBracket { LowerBound = 300001, UpperBound = decimal.MaxValue, Rate = 0.20m }
    };
        }

        /// <summary>
        /// Récupère la liste des charges familiales
        /// </summary>
        /// <returns>Liste des charges familiales</returns>
        public List<FamilyCharge> GetFamilyCharges()
        {
            try
            {
                string filePath = Path.Combine(_appDataFolder, "familyCharges.json");

                if (File.Exists(filePath))
                {
                    string json = File.ReadAllText(filePath);
                    return JsonSerializer.Deserialize<List<FamilyCharge>>(json, _jsonOptions) ?? CreateDefaultFamilyCharges();
                }

                // Si le fichier n'existe pas, créer et retourner les charges par défaut
                var defaultCharges = CreateDefaultFamilyCharges();
                SaveFamilyCharges(defaultCharges);
                return defaultCharges;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Erreur lors du chargement des charges familiales: {ex.Message}");
                return CreateDefaultFamilyCharges();
            }
        }

        /// <summary>
        /// Sauvegarde la liste des charges familiales
        /// </summary>
        /// <param name="charges">Liste des charges familiales à sauvegarder</param>
        public void SaveFamilyCharges(List<FamilyCharge> charges)
        {
            try
            {
                string filePath = Path.Combine(_appDataFolder, "familyCharges.json");
                string json = JsonSerializer.Serialize(charges ?? CreateDefaultFamilyCharges(), _jsonOptions);
                File.WriteAllText(filePath, json);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Erreur lors de la sauvegarde des charges familiales: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Crée une liste de charges familiales par défaut
        /// </summary>
        /// <returns>Liste des charges familiales par défaut</returns>
        private List<FamilyCharge> CreateDefaultFamilyCharges()
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

        #region Gestion des paramètres spécifiques de l'application

        /// <summary>
        /// Récupère un paramètre spécifique de l'application
        /// </summary>
        /// <typeparam name="T">Type du paramètre à récupérer</typeparam>
        /// <param name="settingName">Nom du paramètre</param>
        /// <returns>Valeur du paramètre ou valeur par défaut si le paramètre n'existe pas</returns>
        public T GetAppSetting<T>(string settingName)
        {
            try
            {
                string filePath = Path.Combine(_appDataFolder, "appSettings.json");

                if (File.Exists(filePath))
                {
                    string json = File.ReadAllText(filePath);
                    using (JsonDocument document = JsonDocument.Parse(json))
                    {
                        if (document.RootElement.TryGetProperty(settingName, out JsonElement element))
                        {
                            // Convertir la valeur JSON en type T
                            return JsonSerializer.Deserialize<T>(element.GetRawText(), _jsonOptions);
                        }
                    }
                }

                // Si le paramètre n'existe pas, retourner la valeur par défaut du type
                return default;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Erreur lors de la récupération du paramètre {settingName}: {ex.Message}");
                return default;
            }
        }

        /// <summary>
        /// Sauvegarde un paramètre spécifique de l'application
        /// </summary>
        /// <typeparam name="T">Type du paramètre à sauvegarder</typeparam>
        /// <param name="settingName">Nom du paramètre</param>
        /// <param name="value">Valeur du paramètre</param>
        public void SaveAppSetting<T>(string settingName, T value)
        {
            try
            {
                string filePath = Path.Combine(_appDataFolder, "appSettings.json");
                Dictionary<string, object> settings;

                // Charger les paramètres existants ou créer un nouveau dictionnaire
                if (File.Exists(filePath))
                {
                    string json = File.ReadAllText(filePath);
                    settings = JsonSerializer.Deserialize<Dictionary<string, object>>(json, _jsonOptions)
                        ?? new Dictionary<string, object>();
                }
                else
                {
                    settings = new Dictionary<string, object>();
                }

                // Mettre à jour ou ajouter le paramètre
                settings[settingName] = value;

                // Sauvegarder les paramètres
                string updatedJson = JsonSerializer.Serialize(settings, _jsonOptions);
                File.WriteAllText(filePath, updatedJson);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Erreur lors de la sauvegarde du paramètre {settingName}: {ex.Message}");
                throw;
            }
        }

        #endregion

        #region Importation et exportation des paramètres

        /// <summary>
        /// Exporte les paramètres de l'application vers un fichier JSON
        /// </summary>
        /// <param name="filePath">Chemin du fichier de destination</param>
        public void ExportSettings(string filePath)
        {
            try
            {
                // Création d'un objet contenant tous les paramètres à exporter
                var exportData = new
                {
                    ExportDate = DateTime.Now,
                    Version = "1.0",
                    Settings = _cachedSettings ?? LoadSettingsAsync().Result,
                    TaxBrackets = GetTaxBrackets(),
                    FamilyCharges = GetFamilyCharges(),
                    CustomSettings = LoadCustomSettingsAsync().Result
                };

                // Conversion en JSON et écriture dans le fichier
                string json = JsonSerializer.Serialize(exportData, _jsonOptions);
                File.WriteAllText(filePath, json);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Erreur lors de l'exportation des paramètres: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Importe les paramètres de l'application depuis un fichier JSON
        /// </summary>
        /// <param name="filePath">Chemin du fichier source</param>
        public void ImportSettings(string filePath)
        {
            try
            {
                // Lecture du contenu du fichier
                string json = File.ReadAllText(filePath);

                using (JsonDocument document = JsonDocument.Parse(json))
                {
                    JsonElement root = document.RootElement;

                    // Importation des paramètres généraux
                    if (root.TryGetProperty("Settings", out JsonElement settingsElement))
                    {
                        var settings = JsonSerializer.Deserialize<AppSettings>(
                            settingsElement.GetRawText(), _jsonOptions);

                        if (settings != null)
                            SaveSettingsAsync(settings).Wait();
                    }

                    // Importation des tranches d'imposition
                    if (root.TryGetProperty("TaxBrackets", out JsonElement bracketsElement))
                    {
                        var brackets = JsonSerializer.Deserialize<List<TaxBracket>>(
                            bracketsElement.GetRawText(), _jsonOptions);

                        if (brackets != null)
                            SaveTaxBrackets(brackets);
                    }

                    // Importation des charges familiales
                    if (root.TryGetProperty("FamilyCharges", out JsonElement chargesElement))
                    {
                        var charges = JsonSerializer.Deserialize<List<FamilyCharge>>(
                            chargesElement.GetRawText(), _jsonOptions);

                        if (charges != null)
                            SaveFamilyCharges(charges);
                    }

                    // Importation des paramètres personnalisés
                    if (root.TryGetProperty("CustomSettings", out JsonElement customSettingsElement))
                    {
                        var customSettings = JsonSerializer.Deserialize<Dictionary<string, object>>(
                            customSettingsElement.GetRawText(), _jsonOptions);

                        if (customSettings != null)
                            SaveCustomSettingsAsync(customSettings).Wait();
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Erreur lors de l'importation des paramètres: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Charge les paramètres personnalisés de l'application
        /// </summary>
        /// <returns>Dictionnaire des paramètres personnalisés</returns>
        private async Task<Dictionary<string, object>> LoadCustomSettingsAsync()
        {
            try
            {
                string filePath = Path.Combine(_appDataFolder, "appSettings.json");

                if (File.Exists(filePath))
                {
                    // Utilisation de Task.Run pour exécuter la méthode synchrone File.ReadAllText de manière asynchrone
                    string json = await Task.Run(() => File.ReadAllText(filePath));
                    return JsonSerializer.Deserialize<Dictionary<string, object>>(json, _jsonOptions)
                        ?? new Dictionary<string, object>();
                }

                return new Dictionary<string, object>();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Erreur lors du chargement des paramètres personnalisés: {ex.Message}");
                return new Dictionary<string, object>();
            }
        }

        /// <summary>
        /// Sauvegarde les paramètres personnalisés de l'application
        /// </summary>
        /// <param name="settings">Dictionnaire des paramètres personnalisés</param>
        private async Task SaveCustomSettingsAsync(Dictionary<string, object> settings)
        {
            try
            {
                string filePath = Path.Combine(_appDataFolder, "appSettings.json");
                string json = JsonSerializer.Serialize(settings ?? new Dictionary<string, object>(), _jsonOptions);

                // Utilisation de Task.Run pour exécuter la méthode synchrone File.WriteAllText de manière asynchrone
                await Task.Run(() => File.WriteAllText(filePath, json));
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Erreur lors de la sauvegarde des paramètres personnalisés: {ex.Message}");
                throw;
            }
        }

        #endregion
       

       
}
}