using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Data.SQLite;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using IUTSCalculator.Models;
using Microsoft.Win32; // Pour l'ouverture de fichier

namespace IUTSCalculator.Services
{
    /// <summary>
    /// Service de gestion des données pour l'application IUTSCalculator (version SQLite)
    /// Gère la persistance des employés, des calculs d'impôts et des paramètres de l'application
    /// </summary>
    public class DataService
    {
        public ObservableCollection<string> EmployeeCategories { get; }

        private readonly SQLiteService _sqliteService;

        public DataService() : this("iuts_data.db")
        {
        }

        /// <summary>
        /// Crée un service de données pointant vers la base de données du dossier
        /// (entreprise) spécifié, afin d'isoler complètement ses employés et calculs
        /// de ceux des autres dossiers.
        /// </summary>
        /// <param name="dbFileName">Nom (ou chemin relatif) du fichier SQLite du dossier concerné.</param>
        public DataService(string dbFileName)
        {
            _sqliteService = new SQLiteService(dbFileName);

            EmployeeCategories = new ObservableCollection<string>
            {
                "Cadre Supérieur",
                "Cadre Inférieur"
            };
        }

        #region Gestion des employés

        /// <summary>
        /// Liste des colonnes de la table Employees, partagée par les lectures et les
        /// écritures : c'est l'omission de quatre d'entre elles (indemnité de sujétion,
        /// fonction, date d'entrée, situation matrimoniale) qui faisait silencieusement
        /// perdre ces informations à chaque fermeture de l'application.
        /// </summary>
        private const string ColonnesEmploye =
            "EmployeeId, FirstName, LastName, BaseSalary, HousingAllowance, TransportAllowance, " +
            "FunctionAllowance, SujetionAllowance, FamilyCharges, IsSpouseWorking, PensionContribution, " +
            "Category, IsHigherCategory, Fonction, DateEntree, SituationMatrimoniale";

        /// <summary>
        /// Construit un employé à partir de la ligne courante du lecteur. Point de passage
        /// unique pour toutes les lectures, afin qu'aucune colonne ne puisse être oubliée
        /// dans l'une d'elles seulement.
        /// </summary>
        private static Employee LireEmploye(SQLiteDataReader reader)
        {
            return new Employee
            {
                EmployeeId = DbValue.TextFromDb(reader["EmployeeId"]),
                FirstName = DbValue.TextFromDb(reader["FirstName"]),
                LastName = DbValue.TextFromDb(reader["LastName"]),
                BaseSalary = DbValue.MoneyFromDb(reader["BaseSalary"]),
                HousingAllowance = DbValue.MoneyFromDb(reader["HousingAllowance"]),
                TransportAllowance = DbValue.MoneyFromDb(reader["TransportAllowance"]),
                FunctionAllowance = DbValue.MoneyFromDb(reader["FunctionAllowance"]),
                SujetionAllowance = DbValue.MoneyFromDb(reader["SujetionAllowance"]),
                FamilyCharges = DbValue.IntFromDb(reader["FamilyCharges"]),
                IsSpouseWorking = DbValue.IntFromDb(reader["IsSpouseWorking"]) == 1,
                PensionContribution = DbValue.MoneyFromDb(reader["PensionContribution"]),
                // Category doit être affectée en dernier parmi les champs dont elle dépend :
                // son affectation recalcule IsHigherCategory, qui n'a pas d'accesseur public.
                Category = DbValue.TextFromDb(reader["Category"]),
                Fonction = DbValue.TextFromDb(reader["Fonction"]),
                DateEntree = DbValue.DateFromDb(reader["DateEntree"], default(DateTime)),
                SituationMatrimoniale = DbValue.TextFromDb(reader["SituationMatrimoniale"])
            };
        }

        public async Task<List<Employee>> LoadEmployeesAsync()
        {
            var employees = new List<Employee>();
            using (var conn = _sqliteService.CreateConnection())
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "SELECT " + ColonnesEmploye + " FROM Employees";
                using (var reader = (SQLiteDataReader)await cmd.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                        employees.Add(LireEmploye(reader));
                }
            }
            return employees;
        }

        public async Task ImportEmployeesFromFileAsync(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
                throw new FileNotFoundException("Fichier d'import introuvable.");

            // Exemple pour un fichier JSON (adaptez pour CSV si besoin)
            var json = await Task.Run(() => File.ReadAllText(filePath));
            var employees = JsonSerializer.Deserialize<List<Employee>>(json);

            if (employees == null)
                throw new InvalidOperationException("Aucun employé trouvé dans le fichier.");

            await SaveEmployeesAsync(employees);
        }

        public async Task SaveEmployeesAsync(List<Employee> employees)
        {
            using (var conn = _sqliteService.CreateConnection())
            using (var tx = conn.BeginTransaction())
            {
                using (var cmd = conn.CreateCommand())
                {
                    cmd.Transaction = tx;
                    cmd.CommandText = "DELETE FROM Employees";
                    await cmd.ExecuteNonQueryAsync();
                }
                foreach (var e in employees)
                {
                    await AddEmployeeAsync(e, conn, tx);
                }
                tx.Commit();
            }
        }

        public async Task AddEmployeeAsync(Employee employee)
        {
            using (var conn = _sqliteService.CreateConnection())
            using (var tx = conn.BeginTransaction())
            {
                await AddEmployeeAsync(employee, conn, tx);
                tx.Commit();
            }
        }

        private async Task AddEmployeeAsync(Employee employee, SQLiteConnection conn, SQLiteTransaction tx)
        {
            if (employee == null)
                throw new ArgumentNullException(nameof(employee));

            if (string.IsNullOrEmpty(employee.EmployeeId))
                employee.EmployeeId = Guid.NewGuid().ToString();

            using (var cmd = conn.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = @"
                    INSERT OR REPLACE INTO Employees
                    (EmployeeId, FirstName, LastName, BaseSalary, HousingAllowance, TransportAllowance, FunctionAllowance, SujetionAllowance, FamilyCharges, IsSpouseWorking, PensionContribution, Category, IsHigherCategory, Fonction, DateEntree, SituationMatrimoniale)
                    VALUES
                    (@EmployeeId, @FirstName, @LastName, @BaseSalary, @HousingAllowance, @TransportAllowance, @FunctionAllowance, @SujetionAllowance, @FamilyCharges, @IsSpouseWorking, @PensionContribution, @Category, @IsHigherCategory, @Fonction, @DateEntree, @SituationMatrimoniale);";

                cmd.Parameters.AddWithValue("@EmployeeId", employee.EmployeeId);
                cmd.Parameters.AddWithValue("@FirstName", employee.FirstName ?? "");
                cmd.Parameters.AddWithValue("@LastName", employee.LastName ?? "");
                cmd.Parameters.AddWithValue("@BaseSalary", DbValue.MoneyToDb(employee.BaseSalary));
                cmd.Parameters.AddWithValue("@HousingAllowance", DbValue.MoneyToDb(employee.HousingAllowance));
                cmd.Parameters.AddWithValue("@TransportAllowance", DbValue.MoneyToDb(employee.TransportAllowance));
                cmd.Parameters.AddWithValue("@FunctionAllowance", DbValue.MoneyToDb(employee.FunctionAllowance));
                cmd.Parameters.AddWithValue("@SujetionAllowance", DbValue.MoneyToDb(employee.SujetionAllowance));
                cmd.Parameters.AddWithValue("@FamilyCharges", employee.FamilyCharges);
                cmd.Parameters.AddWithValue("@IsSpouseWorking", employee.IsSpouseWorking ? 1 : 0);
                cmd.Parameters.AddWithValue("@PensionContribution", DbValue.MoneyToDb(employee.PensionContribution));
                cmd.Parameters.AddWithValue("@Category", employee.Category ?? "");
                cmd.Parameters.AddWithValue("@IsHigherCategory", employee.IsHigherCategory ? 1 : 0);
                cmd.Parameters.AddWithValue("@Fonction", employee.Fonction ?? "");
                cmd.Parameters.AddWithValue("@DateEntree",
                    employee.DateEntree == default(DateTime) ? string.Empty : DbValue.DateToDb(employee.DateEntree));
                cmd.Parameters.AddWithValue("@SituationMatrimoniale", employee.SituationMatrimoniale ?? "");

                await cmd.ExecuteNonQueryAsync();
            }
        }

        public async Task UpdateEmployeeAsync(Employee employee)
        {
            await AddEmployeeAsync(employee);
        }

        public async Task DeleteEmployeeAsync(string employeeId)
        {
            using (var conn = _sqliteService.CreateConnection())
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "DELETE FROM Employees WHERE EmployeeId = @EmployeeId";
                cmd.Parameters.AddWithValue("@EmployeeId", employeeId);
                await cmd.ExecuteNonQueryAsync();
            }
        }

        public async Task<Employee> GetEmployeeByIdAsync(string employeeId)
        {
            using (var conn = _sqliteService.CreateConnection())
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "SELECT " + ColonnesEmploye + " FROM Employees WHERE EmployeeId = @EmployeeId";
                cmd.Parameters.AddWithValue("@EmployeeId", employeeId);
                using (var reader = (SQLiteDataReader)await cmd.ExecuteReaderAsync())
                {
                    if (await reader.ReadAsync())
                        return LireEmploye(reader);
                }
            }
            return null;
        }

        #endregion

        #region Gestion de l'historique des calculs

        public async Task<List<TaxCalculation>> LoadCalculationsHistoryAsync()
        {
            var calculations = new List<TaxCalculation>();
            var employees = (await LoadEmployeesAsync()).ToDictionary(e => e.EmployeeId);

            using (var conn = _sqliteService.CreateConnection())
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "SELECT * FROM TaxCalculations";
                using (var reader = (SQLiteDataReader)await cmd.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                    {
                        var employeeId = DbValue.TextFromDb(reader["EmployeeId"]);
                        Employee employee = null;
                        if (!string.IsNullOrEmpty(employeeId))
                            employees.TryGetValue(employeeId, out employee);

                        calculations.Add(new TaxCalculation
                        {
                            Id = DbValue.TextFromDb(reader["Id"]),
                            Employee = employee,
                            CalculationDate = DbValue.DateFromDb(reader["CalculationDate"], DateTime.Now),
                            SalaireBrut = DbValue.MoneyFromDb(reader["SalaireBrut"]),
                            BaseIUTSHorsDeduction = DbValue.MoneyFromDb(reader["BaseIUTSHorsDeduction"]),
                            IUTSNet = DbValue.MoneyFromDb(reader["IUTSNet"]),
                            // TaxRate est un pourcentage, pas un montant : il est stocké tel quel.
                            TaxRate = DbValue.DecimalFromDb(reader["TaxRate"]),
                            FamilyChargeReduction = DbValue.MoneyFromDb(reader["FamilyChargeReduction"]),
                            ExemptAmount = DbValue.MoneyFromDb(reader["ExemptAmount"]),
                            CalculationDetails = DbValue.TextFromDb(reader["CalculationDetails"]),
                            IsAnnual = DbValue.IntFromDb(reader["IsAnnual"]) == 1,
                            IsRegularization = DbValue.IntFromDb(reader["IsRegularization"]) == 1,
                            TaxDifference = DbValue.MoneyFromDb(reader["TaxDifference"]),
                            CnssEmployee = DbValue.MoneyFromDb(reader["CnssEmployee"]),
                            CnssPartPatronale = DbValue.MoneyFromDb(reader["CnssPartPatronale"]),
                            TaxePatronale = DbValue.MoneyFromDb(reader["TaxePatronale"]),
                            RetenueSpeciale = DbValue.MoneyFromDb(reader["RetenueSpeciale"]),
                            MasseSalariale = DbValue.MoneyFromDb(reader["MasseSalariale"]),
                            ChargesPatronales = DbValue.MoneyFromDb(reader["ChargesPatronales"]),
                            NetSalaryAfterTaxPersisted = DbValue.MoneyFromDb(reader["NetSalaryAfterTax"]),
                            SalaireNetaPayerPersisted = DbValue.MoneyFromDb(reader["SalaireNetaPayer"])
                        });
                    }
                }
            }
            return calculations;
        }

        public async Task SaveCalculationsHistoryAsync(List<TaxCalculation> calculations)
        {
            using (var conn = _sqliteService.CreateConnection())
            using (var tx = conn.BeginTransaction())
            {
                using (var cmd = conn.CreateCommand())
                {
                    cmd.Transaction = tx;
                    cmd.CommandText = "DELETE FROM TaxCalculations";
                    await cmd.ExecuteNonQueryAsync();
                }
                foreach (var c in calculations)
                {
                    await AddCalculationToHistoryAsync(c, conn, tx);
                }
                tx.Commit();
            }
        }

        public async Task AddCalculationToHistoryAsync(TaxCalculation calculation)
        {
            using (var conn = _sqliteService.CreateConnection())
            using (var tx = conn.BeginTransaction())
            {
                await AddCalculationToHistoryAsync(calculation, conn, tx);
                tx.Commit();
            }
        }

        private async Task AddCalculationToHistoryAsync(TaxCalculation calculation, SQLiteConnection conn, SQLiteTransaction tx)
        {
            if (calculation == null)
                throw new ArgumentNullException(nameof(calculation));

            if (string.IsNullOrEmpty(calculation.Id))
                calculation.Id = Guid.NewGuid().ToString();

            if (calculation.CalculationDate == default)
                calculation.CalculationDate = DateTime.Now;

            // L'employé peut être absent (calcul libre, sans fiche associée) : la colonne
            // EmployeeId accepte alors une chaîne vide, comme plus bas.
            if (calculation.Employee != null)
                await AddEmployeeAsync(calculation.Employee, conn, tx);

            using (var cmd = conn.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = @"
                    INSERT OR REPLACE INTO TaxCalculations
                    (Id, EmployeeId, CalculationDate, SalaireBrut, BaseIUTSHorsDeduction, IUTSNet, TaxRate, FamilyChargeReduction, ExemptAmount, CalculationDetails, IsAnnual, IsRegularization, TaxDifference, CnssEmployee, CnssPartPatronale, TaxePatronale, RetenueSpeciale, MasseSalariale, ChargesPatronales, NetSalaryAfterTax, SalaireNetaPayer)
                    VALUES
                    (@Id, @EmployeeId, @CalculationDate, @SalaireBrut, @BaseIUTSHorsDeduction, @IUTSNet, @TaxRate, @FamilyChargeReduction, @ExemptAmount, @CalculationDetails, @IsAnnual, @IsRegularization, @TaxDifference, @CnssEmployee, @CnssPartPatronale, @TaxePatronale, @RetenueSpeciale, @MasseSalariale, @ChargesPatronales, @NetSalaryAfterTax, @SalaireNetaPayer);";

                cmd.Parameters.AddWithValue("@Id", calculation.Id);
                cmd.Parameters.AddWithValue("@EmployeeId", calculation.Employee?.EmployeeId ?? "");
                cmd.Parameters.AddWithValue("@CalculationDate", DbValue.DateToDb(calculation.CalculationDate));
                cmd.Parameters.AddWithValue("@SalaireBrut", DbValue.MoneyToDb(calculation.SalaireBrut));
                cmd.Parameters.AddWithValue("@BaseIUTSHorsDeduction", DbValue.MoneyToDb(calculation.BaseIUTSHorsDeduction));
                cmd.Parameters.AddWithValue("@IUTSNet", DbValue.MoneyToDb(calculation.IUTSNet));
                // TaxRate est un pourcentage à nombreuses décimales : il ne doit surtout pas
                // être arrondi au centime comme un montant.
                cmd.Parameters.AddWithValue("@TaxRate", DbValue.DecimalToDb(calculation.TaxRate));
                cmd.Parameters.AddWithValue("@FamilyChargeReduction", DbValue.MoneyToDb(calculation.FamilyChargeReduction));
                cmd.Parameters.AddWithValue("@ExemptAmount", DbValue.MoneyToDb(calculation.ExemptAmount));
                cmd.Parameters.AddWithValue("@CalculationDetails", calculation.CalculationDetails ?? "");
                cmd.Parameters.AddWithValue("@IsAnnual", calculation.IsAnnual ? 1 : 0);
                cmd.Parameters.AddWithValue("@IsRegularization", calculation.IsRegularization ? 1 : 0);
                cmd.Parameters.AddWithValue("@TaxDifference", DbValue.MoneyToDb(calculation.TaxDifference));
                cmd.Parameters.AddWithValue("@CnssEmployee", DbValue.MoneyToDb(calculation.CnssEmployee));
                cmd.Parameters.AddWithValue("@CnssPartPatronale", DbValue.MoneyToDb(calculation.CnssPartPatronale));
                cmd.Parameters.AddWithValue("@TaxePatronale", DbValue.MoneyToDb(calculation.TaxePatronale));
                cmd.Parameters.AddWithValue("@RetenueSpeciale", DbValue.MoneyToDb(calculation.RetenueSpeciale));
                cmd.Parameters.AddWithValue("@MasseSalariale", DbValue.MoneyToDb(calculation.MasseSalariale));
                cmd.Parameters.AddWithValue("@ChargesPatronales", DbValue.MoneyToDb(calculation.ChargesPatronales));
                cmd.Parameters.AddWithValue("@NetSalaryAfterTax", DbValue.MoneyToDb(calculation.NetSalaryAfterTaxPersisted));
                cmd.Parameters.AddWithValue("@SalaireNetaPayer", DbValue.MoneyToDb(calculation.SalaireNetaPayerPersisted));

                await cmd.ExecuteNonQueryAsync();
            }
        }

        public async Task DeleteCalculationFromHistoryAsync(string calculationId)
        {
            using (var conn = _sqliteService.CreateConnection())
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "DELETE FROM TaxCalculations WHERE Id = @Id";
                cmd.Parameters.AddWithValue("@Id", calculationId);
                await cmd.ExecuteNonQueryAsync();
            }
        }

        /// <summary>
        /// Exporte la fiche de paie de l'employé concerné par le calcul au format Excel (XLSX).
        /// </summary>
        /// <param name="calculation">Le calcul d'impôt à exporter (pour récupérer l'employé)</param>
        public async Task ExportToExcelAsync(TaxCalculation calculation)
        {
            if (calculation == null)
                throw new ArgumentNullException(nameof(calculation));

                     
            var employees = new List<Employee>();
            if (calculation.Employee != null)
                employees.Add(calculation.Employee);

            await Task.Run(() =>
            {
                var helper = new ExcelExportHelper();
                helper.ExportPayrollToExcel(employees);
            });
        }

        public async Task<List<TaxCalculation>> GetCalculationHistoryForEmployeeAsync(string employeeId)
        {
            var all = await LoadCalculationsHistoryAsync();
            return all.Where(c => c.Employee?.EmployeeId == employeeId)
                      .OrderByDescending(c => c.CalculationDate)
                      .ToList();
        }

        public async Task<List<TaxCalculation>> GetCalculationHistoryForPeriodAsync(DateTime startDate, DateTime endDate)
        {
            var all = await LoadCalculationsHistoryAsync();
            return all.Where(c => c.CalculationDate >= startDate && c.CalculationDate <= endDate)
                      .OrderByDescending(c => c.CalculationDate)
                      .ToList();
        }

        #endregion

        #region Gestion des paramètres de l'application

        public async Task<AppSettings> LoadSettingsAsync()
        {
            using (var conn = _sqliteService.CreateConnection())
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "SELECT * FROM AppSettings LIMIT 1";
                using (var reader = (SQLiteDataReader)await cmd.ExecuteReaderAsync())
                {
                    if (await reader.ReadAsync())
                    {
                        var settings = new AppSettings
                        {
                            Theme = reader["Theme"].ToString(),
                            Language = reader["Language"].ToString(),
                            AutoSave = reader.GetInt32(reader.GetOrdinal("AutoSave")) == 1,
                            LastUpdated = DbValue.DateFromDb(reader["LastUpdated"], DateTime.Now)
                        };

                        if (ColumnExists(reader, "ExportDirectory"))
                            settings.ExportDirectory = reader["ExportDirectory"]?.ToString();

                        if (ColumnExists(reader, "TaxBrackets"))
                        {
                            var json = reader["TaxBrackets"]?.ToString();
                            if (!string.IsNullOrWhiteSpace(json))
                            {
                                try
                                {
                                    settings.TaxBrackets = JsonSerializer.Deserialize<List<TaxBracket>>(json);
                                }
                                catch { settings.TaxBrackets = null; }
                            }
                        }

                        if (ColumnExists(reader, "FamilyCharges"))
                        {
                            var json = reader["FamilyCharges"]?.ToString();
                            if (!string.IsNullOrWhiteSpace(json))
                            {
                                try
                                {
                                    settings.FamilyCharges = JsonSerializer.Deserialize<List<FamilyCharge>>(json);
                                }
                                catch { settings.FamilyCharges = null; }
                            }
                        }

                        return settings;
                    }
                }
            }
            var defaultSettings = new AppSettings
            {
                Theme = "Light",
                Language = "fr-FR",
                AutoSave = true,
                LastUpdated = DateTime.Now
            };
            await SaveSettingsAsync(defaultSettings);
            return defaultSettings;
        }

        public async Task SaveSettingsAsync(AppSettings settings)
        {
            using (var conn = _sqliteService.CreateConnection())
            using (var tx = conn.BeginTransaction())
            {
                using (var cmd = conn.CreateCommand())
                {
                    cmd.Transaction = tx;
                    cmd.CommandText = "DELETE FROM AppSettings";
                    await cmd.ExecuteNonQueryAsync();
                }
                using (var cmd = conn.CreateCommand())
                {
                    cmd.Transaction = tx;
                    cmd.CommandText = @"
                        INSERT INTO AppSettings 
                        (Theme, Language, AutoSave, LastUpdated, ExportDirectory, TaxBrackets, FamilyCharges)
                        VALUES 
                        (@Theme, @Language, @AutoSave, @LastUpdated, @ExportDirectory, @TaxBrackets, @FamilyCharges);";
                    cmd.Parameters.AddWithValue("@Theme", settings.Theme ?? "Light");
                    cmd.Parameters.AddWithValue("@Language", settings.Language ?? "fr-FR");
                    cmd.Parameters.AddWithValue("@AutoSave", settings.AutoSave ? 1 : 0);
                    cmd.Parameters.AddWithValue("@LastUpdated", DbValue.DateToDb(settings.LastUpdated));
                    cmd.Parameters.AddWithValue("@ExportDirectory", settings.ExportDirectory ?? string.Empty);
                    cmd.Parameters.AddWithValue("@TaxBrackets", settings.TaxBrackets != null ? JsonSerializer.Serialize(settings.TaxBrackets) : string.Empty);
                    cmd.Parameters.AddWithValue("@FamilyCharges", settings.FamilyCharges != null ? JsonSerializer.Serialize(settings.FamilyCharges) : string.Empty);
                    await cmd.ExecuteNonQueryAsync();
                }
                tx.Commit();
            }
        }

        private bool ColumnExists(SQLiteDataReader reader, string columnName)
        {
            for (int i = 0; i < reader.FieldCount; i++)
            {
                if (reader.GetName(i).Equals(columnName, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        public async Task UpdateSettingAsync<T>(string settingName, T value)
        {
            var settings = await LoadSettingsAsync();

            var prop = typeof(AppSettings).GetProperty(settingName);
            if (prop != null && prop.CanWrite)
            {
                prop.SetValue(settings, value);
                await SaveSettingsAsync(settings);
            }
            else
            {
                throw new ArgumentException($"Le paramètre '{settingName}' n'existe pas ou ne peut pas être modifié");
            }
        }

        #endregion

        #region Export/Import JSON (compatibilité)

        public async Task<string> ExportAllDataAsync()
        {
            var employees = await LoadEmployeesAsync();
            var calculations = await LoadCalculationsHistoryAsync();
            var settings = await LoadSettingsAsync();

            var exportData = new
            {
                ExportDate = DateTime.Now,
                Version = "1.0",
                Employees = employees,
                Calculations = calculations,
                Settings = settings
            };

            return JsonSerializer.Serialize(exportData, new JsonSerializerOptions { WriteIndented = true });
        }

        public async Task ImportAllDataAsync(string jsonData)
        {
            using (var doc = JsonDocument.Parse(jsonData))
            {
                var root = doc.RootElement;

                if (root.TryGetProperty("Employees", out var employeesElement))
                {
                    var employees = JsonSerializer.Deserialize<List<Employee>>(employeesElement.GetRawText());
                    if (employees != null)
                        await SaveEmployeesAsync(employees);
                }

                if (root.TryGetProperty("Calculations", out var calculationsElement))
                {
                    var calculations = JsonSerializer.Deserialize<List<TaxCalculation>>(calculationsElement.GetRawText());
                    if (calculations != null)
                        await SaveCalculationsHistoryAsync(calculations);
                }

                if (root.TryGetProperty("Settings", out var settingsElement))
                {
                    var settings = JsonSerializer.Deserialize<AppSettings>(settingsElement.GetRawText());
                    if (settings != null)
                        await SaveSettingsAsync(settings);
                }
            }
        }

        #endregion
    }
}
