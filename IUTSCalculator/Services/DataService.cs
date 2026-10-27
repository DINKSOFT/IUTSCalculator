using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Microsoft.Data.Sqlite;
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

        /// <summary>
        /// Recharge la liste des catégories proposées sur les fiches : les deux valeurs
        /// historiques, puis les catégories des grilles conventionnelles du dossier (dans
        /// l'ordre des grilles), puis toute catégorie déjà portée par un salarié.
        ///
        /// La liste se limitait aux deux valeurs historiques : un salarié classé avec un
        /// libellé de grille (« Agents de Maîtrise »…) — indispensable pour la prime
        /// d'ancienneté et le contrôle du minimum — ne pouvait ni être choisi ni être
        /// affiché, et sa catégorie risquait d'être perdue à l'enregistrement.
        /// La collection est modifiée après l'attente : appeler depuis le thread de l'écran.
        /// </summary>
        public async Task RafraichirCategoriesAsync()
        {
            var valeurs = new List<string> { "Cadre Supérieur", "Cadre Inférieur" };
            try
            {
                using (var conn = _sqliteService.CreerConnexion())
                {
                    if (TableExiste(conn, "GrillesSalaires"))
                    {
                        using (var cmd = conn.CreateCommand())
                        {
                            cmd.CommandText = "SELECT Categorie, MIN(Ordre) AS O FROM GrillesSalaires " +
                                              "WHERE TRIM(IFNULL(Categorie,'')) <> '' GROUP BY Categorie ORDER BY O, Categorie;";
                            using (var reader = (SqliteDataReader)await cmd.ExecuteReaderAsync())
                            {
                                while (await reader.ReadAsync())
                                    valeurs.Add(Convert.ToString(reader["Categorie"]).Trim());
                            }
                        }
                    }
                    using (var cmd = conn.CreateCommand())
                    {
                        cmd.CommandText = "SELECT DISTINCT TRIM(Category) FROM Employees WHERE TRIM(IFNULL(Category,'')) <> '' ORDER BY 1;";
                        using (var reader = (SqliteDataReader)await cmd.ExecuteReaderAsync())
                        {
                            while (await reader.ReadAsync())
                                valeurs.Add(Convert.ToString(reader[0]).Trim());
                        }
                    }
                }
            }
            catch (SqliteException)
            {
                // Base en cours de création : on garde au moins les valeurs historiques.
            }

            var distinctes = new List<string>();
            foreach (var v in valeurs)
            {
                if (!distinctes.Exists(d => string.Equals(d, v, StringComparison.OrdinalIgnoreCase)))
                    distinctes.Add(v);
            }

            // Mise à jour en place : les écrans liés gardent la même collection.
            for (int i = EmployeeCategories.Count - 1; i >= 0; i--)
            {
                if (!distinctes.Contains(EmployeeCategories[i]))
                    EmployeeCategories.RemoveAt(i);
            }
            for (int i = 0; i < distinctes.Count; i++)
            {
                if (!EmployeeCategories.Contains(distinctes[i]))
                    EmployeeCategories.Insert(Math.Min(i, EmployeeCategories.Count), distinctes[i]);
            }
        }

        private static bool TableExiste(SqliteConnection conn, string table)
        {
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name=@n;";
                cmd.Parameters.AjouterValeur("@n", table);
                return Convert.ToInt32(cmd.ExecuteScalar()) > 0;
            }
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
            "Category, Service, IsHigherCategory, Fonction, DateEntree, SituationMatrimoniale, NumeroCnss, " +
            "DateNaissance, LieuNaissance, Sexe, Nationalite, NumeroCnib, DateDelivranceCnib, LieuDelivranceCnib, Adresse, Telephone, Email, NumeroIfu, Banque, NumeroCompte, ModeReglement, ConventionId, Echelon, DateSortie, MotifDepart, StatutSalarie, PosteId, ManagerMatricule, SoldeCongesReprise, DateRepriseConges, TypeContrat";

        /// <summary>
        /// Construit un employé à partir de la ligne courante du lecteur. Point de passage
        /// unique pour toutes les lectures, afin qu'aucune colonne ne puisse être oubliée
        /// dans l'une d'elles seulement.
        /// </summary>
        private static Employee LireEmploye(SqliteDataReader reader)
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
                Service = DbValue.TextFromDb(reader["Service"]),
                Fonction = DbValue.TextFromDb(reader["Fonction"]),
                DateEntree = DbValue.DateFromDb(reader["DateEntree"], default(DateTime)),
                SituationMatrimoniale = DbValue.TextFromDb(reader["SituationMatrimoniale"]),
                NumeroCnss = DbValue.TextFromDb(reader["NumeroCnss"]),
                DateNaissance = DbValue.DateNullableFromDb(reader["DateNaissance"]),
                LieuNaissance = DbValue.TextFromDb(reader["LieuNaissance"]),
                Sexe = DbValue.TextFromDb(reader["Sexe"]),
                Nationalite = DbValue.TextFromDb(reader["Nationalite"]),
                NumeroCnib = DbValue.TextFromDb(reader["NumeroCnib"]),
                DateDelivranceCnib = DbValue.DateNullableFromDb(reader["DateDelivranceCnib"]),
                LieuDelivranceCnib = DbValue.TextFromDb(reader["LieuDelivranceCnib"]),
                Adresse = DbValue.TextFromDb(reader["Adresse"]),
                Telephone = DbValue.TextFromDb(reader["Telephone"]),
                Email = DbValue.TextFromDb(reader["Email"]),
                NumeroIfu = DbValue.TextFromDb(reader["NumeroIfu"]),
                Banque = DbValue.TextFromDb(reader["Banque"]),
                NumeroCompte = DbValue.TextFromDb(reader["NumeroCompte"]),
                ModeReglement = DbValue.TextFromDb(reader["ModeReglement"]),
                ConventionId = reader["ConventionId"] != DBNull.Value && reader["ConventionId"] != null ? Convert.ToInt32(reader["ConventionId"]) : (int?)null,
                Echelon = DbValue.TextFromDb(reader["Echelon"]),
                DateSortie = DbValue.DateNullableFromDb(reader["DateSortie"]),
                MotifDepart = DbValue.TextFromDb(reader["MotifDepart"]),
                StatutSalarie = DbValue.IntFromDb(reader["StatutSalarie"]),
                PosteId = DbValue.IntNullableFromDb(reader["PosteId"]),
                ManagerMatricule = DbValue.TextFromDb(reader["ManagerMatricule"]),
                SoldeCongesReprise = DbValue.DecimalFromDb(reader["SoldeCongesReprise"]),
                DateRepriseConges = DbValue.DateNullableFromDb(reader["DateRepriseConges"]),
                TypeContrat = DbValue.TextFromDb(reader["TypeContrat"])
            };
        }

        public async Task<List<Employee>> LoadEmployeesAsync()
        {
            var employees = new List<Employee>();
            using (var conn = _sqliteService.CreerConnexion())
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "SELECT " + ColonnesEmploye + " FROM Employees";
                using (var reader = (SqliteDataReader)await cmd.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                        employees.Add(LireEmploye(reader));
                }
            }
            return employees;
        }

        /// <summary>
        /// Importe des employés depuis un fichier .csv ou .json.
        ///
        /// Les deux formats n'ont PAS le même comportement, et c'est volontaire :
        ///   • .json — remplacement complet de la liste des employés (DELETE puis
        ///     réinsertion), pensé pour restaurer une sauvegarde entière.
        ///   • .csv — ajout / mise à jour PAR MATRICULE, sans jamais supprimer un
        ///     employé absent du fichier. Une ligne dont le Matricule correspond à un
        ///     employé déjà présent remplace intégralement sa fiche (comme le ferait
        ///     l'écran salarié) ; une ligne sans Matricule (ou à un Matricule inconnu)
        ///     crée un nouvel employé. C'est le format attendu pour ajouter un lot de
        ///     nouvelles recrues à un dossier déjà en service.
        ///
        /// <paramref name="conventionsDisponibles"/> permet de résoudre la colonne CSV
        /// « ConventionCollective » (saisie par code ou libellé) vers son Id ; à défaut
        /// (null ou vide), la convention n'est pas affectée et un avertissement est
        /// ajouté au rapport.
        /// </summary>
        public async Task<RapportImportEmployes> ImportEmployeesFromFileAsync(
            string filePath, IEnumerable<ConventionCollective> conventionsDisponibles = null)
        {
            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
                throw new FileNotFoundException("Fichier d'import introuvable.");

            var rapport = new RapportImportEmployes();
            string extension = Path.GetExtension(filePath);

            if (string.Equals(extension, ".csv", StringComparison.OrdinalIgnoreCase))
            {
                var existants = await LoadEmployeesAsync();

                var lecture = EmployeeCsvService.Lire(filePath, existants, conventionsDisponibles);
                rapport.Erreurs.AddRange(lecture.ErreursGlobales);
                rapport.Avertissements.AddRange(lecture.AvertissementsGlobaux);

                var lignesValides = lecture.Lignes.Where(l => l.EstValide).ToList();
                foreach (var l in lecture.Lignes.Where(l => !l.EstValide))
                {
                    rapport.LignesIgnorees++;
                    foreach (var e in l.Erreurs)
                        rapport.Erreurs.Add($"Ligne {l.NumeroLigne} : {e}");
                }
                foreach (var l in lecture.Lignes)
                    foreach (var a in l.Avertissements)
                        rapport.Avertissements.Add($"Ligne {l.NumeroLigne} : {a}");

                if (lignesValides.Count > 0)
                {
                    // Une ligne de mise à jour remplace la fiche — mais seulement pour ce que le
                    // modèle CSV contient. Le poste et le manager (organigramme), et la reprise
                    // du solde de congés n'en font pas partie : réimporter les effaçait
                    // (défaut n° 22 du guide). Ils sont repris de la fiche existante.
                    var parMatricule = existants
                        .Where(e => !string.IsNullOrWhiteSpace(e.EmployeeId))
                        .GroupBy(e => e.EmployeeId, StringComparer.OrdinalIgnoreCase)
                        .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

                    using (var conn = _sqliteService.CreerConnexion())
                    using (var tx = conn.BeginTransaction())
                    {
                        foreach (var l in lignesValides)
                        {
                            Employee existant;
                            if (l.EstMiseAJour && l.Employe != null && !string.IsNullOrWhiteSpace(l.Employe.EmployeeId)
                                && parMatricule.TryGetValue(l.Employe.EmployeeId, out existant))
                            {
                                if (!l.Employe.PosteId.HasValue) l.Employe.PosteId = existant.PosteId;
                                if (string.IsNullOrWhiteSpace(l.Employe.ManagerMatricule)) l.Employe.ManagerMatricule = existant.ManagerMatricule;
                                if (string.IsNullOrWhiteSpace(l.Employe.TypeContrat)) l.Employe.TypeContrat = existant.TypeContrat;
                                if (!l.Employe.DateRepriseConges.HasValue)
                                {
                                    l.Employe.DateRepriseConges = existant.DateRepriseConges;
                                    l.Employe.SoldeCongesReprise = existant.SoldeCongesReprise;
                                }
                            }
                            await AddEmployeeAsync(l.Employe, conn, tx);
                            if (l.EstMiseAJour) rapport.MisAJour++; else rapport.Ajoutes++;
                        }
                        tx.Commit();
                    }
                }
            }
            else
            {
                // .json (ou toute autre extension) : comportement historique, remplacement complet —
                // conservé pour la restauration d'une sauvegarde JSON existante.
                var json = await Task.Run(() => File.ReadAllText(filePath));
                var employees = JsonSerializer.Deserialize<List<Employee>>(json);

                if (employees == null)
                    throw new InvalidOperationException("Aucun employé trouvé dans le fichier.");

                await SaveEmployeesAsync(employees);
                rapport.Ajoutes = employees.Count;
                rapport.Avertissements.Add(
                    "Import JSON : la liste complète des employés a été remplacée par le contenu du fichier.");
            }

            return rapport;
        }

        public async Task SaveEmployeesAsync(List<Employee> employees)
        {
            using (var conn = _sqliteService.CreerConnexion())
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
            using (var conn = _sqliteService.CreerConnexion())
            using (var tx = conn.BeginTransaction())
            {
                await AddEmployeeAsync(employee, conn, tx);
                tx.Commit();
            }
        }

        internal async Task AddEmployeeAsync(Employee employee, SqliteConnection conn, SqliteTransaction tx)
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
                    (EmployeeId, FirstName, LastName, BaseSalary, HousingAllowance, TransportAllowance, FunctionAllowance, SujetionAllowance, FamilyCharges, IsSpouseWorking, PensionContribution, Category, Service, IsHigherCategory, Fonction, DateEntree, SituationMatrimoniale, NumeroCnss, DateNaissance, LieuNaissance, Sexe, Nationalite, NumeroCnib, DateDelivranceCnib, LieuDelivranceCnib, Adresse, Telephone, Email, NumeroIfu, Banque, NumeroCompte, ModeReglement, ConventionId, Echelon, DateSortie, MotifDepart, StatutSalarie, PosteId, ManagerMatricule, SoldeCongesReprise, DateRepriseConges, TypeContrat)
                    VALUES
                    (@EmployeeId, @FirstName, @LastName, @BaseSalary, @HousingAllowance, @TransportAllowance, @FunctionAllowance, @SujetionAllowance, @FamilyCharges, @IsSpouseWorking, @PensionContribution, @Category, @Service, @IsHigherCategory, @Fonction, @DateEntree, @SituationMatrimoniale, @NumeroCnss, @DateNaissance, @LieuNaissance, @Sexe, @Nationalite, @NumeroCnib, @DateDelivranceCnib, @LieuDelivranceCnib, @Adresse, @Telephone, @Email, @NumeroIfu, @Banque, @NumeroCompte, @ModeReglement, @ConventionId, @Echelon, @DateSortie, @MotifDepart, @StatutSalarie, @PosteId, @ManagerMatricule, @SoldeCongesReprise, @DateRepriseConges, @TypeContrat);";

                cmd.Parameters.AjouterValeur("@EmployeeId", employee.EmployeeId);
                cmd.Parameters.AjouterValeur("@FirstName", employee.FirstName ?? "");
                cmd.Parameters.AjouterValeur("@LastName", employee.LastName ?? "");
                cmd.Parameters.AjouterValeur("@BaseSalary", DbValue.MoneyToDb(employee.BaseSalary));
                cmd.Parameters.AjouterValeur("@HousingAllowance", DbValue.MoneyToDb(employee.HousingAllowance));
                cmd.Parameters.AjouterValeur("@TransportAllowance", DbValue.MoneyToDb(employee.TransportAllowance));
                cmd.Parameters.AjouterValeur("@FunctionAllowance", DbValue.MoneyToDb(employee.FunctionAllowance));
                cmd.Parameters.AjouterValeur("@SujetionAllowance", DbValue.MoneyToDb(employee.SujetionAllowance));
                cmd.Parameters.AjouterValeur("@FamilyCharges", employee.FamilyCharges);
                cmd.Parameters.AjouterValeur("@IsSpouseWorking", employee.IsSpouseWorking ? 1 : 0);
                cmd.Parameters.AjouterValeur("@PensionContribution", DbValue.MoneyToDb(employee.PensionContribution));
                cmd.Parameters.AjouterValeur("@Category", employee.Category ?? "");
                cmd.Parameters.AjouterValeur("@Service", employee.Service ?? "");
                cmd.Parameters.AjouterValeur("@IsHigherCategory", employee.IsHigherCategory ? 1 : 0);
                cmd.Parameters.AjouterValeur("@Fonction", employee.Fonction ?? "");
                cmd.Parameters.AjouterValeur("@DateEntree",
                    employee.DateEntree == default(DateTime) ? string.Empty : DbValue.DateToDb(employee.DateEntree));
                cmd.Parameters.AjouterValeur("@SituationMatrimoniale", employee.SituationMatrimoniale ?? "");
                cmd.Parameters.AjouterValeur("@NumeroCnss", employee.NumeroCnss ?? "");
                cmd.Parameters.AjouterValeur("@DateNaissance", DbValue.DateNullableToDb(employee.DateNaissance));
                cmd.Parameters.AjouterValeur("@LieuNaissance", employee.LieuNaissance ?? "");
                cmd.Parameters.AjouterValeur("@Sexe", employee.Sexe ?? "");
                cmd.Parameters.AjouterValeur("@Nationalite", employee.Nationalite ?? "");
                cmd.Parameters.AjouterValeur("@NumeroCnib", employee.NumeroCnib ?? "");
                cmd.Parameters.AjouterValeur("@DateDelivranceCnib", DbValue.DateNullableToDb(employee.DateDelivranceCnib));
                cmd.Parameters.AjouterValeur("@LieuDelivranceCnib", employee.LieuDelivranceCnib ?? "");
                cmd.Parameters.AjouterValeur("@Adresse", employee.Adresse ?? "");
                cmd.Parameters.AjouterValeur("@Telephone", employee.Telephone ?? "");
                cmd.Parameters.AjouterValeur("@Email", employee.Email ?? "");
                cmd.Parameters.AjouterValeur("@NumeroIfu", employee.NumeroIfu ?? "");
                cmd.Parameters.AjouterValeur("@Banque", employee.Banque ?? "");
                cmd.Parameters.AjouterValeur("@NumeroCompte", employee.NumeroCompte ?? "");
                cmd.Parameters.AjouterValeur("@ModeReglement", employee.ModeReglement ?? "");
                cmd.Parameters.AjouterValeur("@ConventionId", employee.ConventionId.HasValue ? (object)employee.ConventionId.Value : DBNull.Value);
                cmd.Parameters.AjouterValeur("@Echelon", employee.Echelon ?? "");
                cmd.Parameters.AjouterValeur("@DateSortie", DbValue.DateNullableToDb(employee.DateSortie));
                cmd.Parameters.AjouterValeur("@MotifDepart", employee.MotifDepart ?? "");
                cmd.Parameters.AjouterValeur("@StatutSalarie", employee.StatutSalarie);
                cmd.Parameters.AjouterValeur("@PosteId", employee.PosteId.HasValue ? (object)employee.PosteId.Value : DBNull.Value);
                cmd.Parameters.AjouterValeur("@ManagerMatricule", employee.ManagerMatricule ?? "");
                cmd.Parameters.AjouterValeur("@SoldeCongesReprise", DbValue.DecimalToDb(employee.SoldeCongesReprise));
                cmd.Parameters.AjouterValeur("@DateRepriseConges", DbValue.DateNullableToDb(employee.DateRepriseConges));
                cmd.Parameters.AjouterValeur("@TypeContrat", employee.TypeContrat ?? "");

                await cmd.ExecuteNonQueryAsync();
            }
        }

        public async Task UpdateEmployeeAsync(Employee employee)
        {
            await AddEmployeeAsync(employee);
        }

        public async Task DeleteEmployeeAsync(string employeeId)
        {
            using (var conn = _sqliteService.CreerConnexion())
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "DELETE FROM Employees WHERE EmployeeId = @EmployeeId";
                cmd.Parameters.AjouterValeur("@EmployeeId", employeeId);
                await cmd.ExecuteNonQueryAsync();
            }
        }

        public async Task<Employee> GetEmployeeByIdAsync(string employeeId)
        {
            using (var conn = _sqliteService.CreerConnexion())
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "SELECT " + ColonnesEmploye + " FROM Employees WHERE EmployeeId = @EmployeeId";
                cmd.Parameters.AjouterValeur("@EmployeeId", employeeId);
                using (var reader = (SqliteDataReader)await cmd.ExecuteReaderAsync())
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

            using (var conn = _sqliteService.CreerConnexion())
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "SELECT * FROM TaxCalculations";
                using (var reader = (SqliteDataReader)await cmd.ExecuteReaderAsync())
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
                            SalaireNetaPayerPersisted = DbValue.MoneyFromDb(reader["SalaireNetaPayer"]),
                            AssietteCnss = DbValue.MoneyFromDb(reader["AssietteCnss"]),
                            CnssPrestationsFamiliales = DbValue.MoneyFromDb(reader["CnssPrestationsFamiliales"]),
                            CnssRisquesProfessionnels = DbValue.MoneyFromDb(reader["CnssRisquesProfessionnels"]),
                            CnssPensionsPatronal = DbValue.MoneyFromDb(reader["CnssPensionsPatronal"]),
                            RetenuesDiverses = DbValue.MoneyFromDb(reader["RetenuesDiverses"]),
                            GainsVariables = DbValue.MoneyFromDb(reader["GainsVariables"]),
                            AssietteTPA = DbValue.MoneyFromDb(reader["AssietteTPA"])
                        });
                    }
                }
            }
            return calculations;
        }

        /// <summary>
        /// Restaure en masse l'historique des calculs — utilisé par la restauration d'une
        /// sauvegarde JSON complète (voir <see cref="ImportAllDataAsync"/>) et par le
        /// « Enregistrer toutes les données » de l'écran principal, qui relit puis
        /// réenregistre tel quel l'historique courant.
        ///
        /// N'insère QUE la ligne TaxCalculations elle-même, SANS passer par
        /// <see cref="AddCalculationToHistoryAsync(TaxCalculation, SqliteConnection, SqliteTransaction)"/> :
        /// deux effets de bord de cette dernière n'ont pas de sens ici, et corrompaient ou
        /// bloquaient la restauration — signalé le 16 septembre 2026 (voir
        /// ETAT-DEVELOPPEMENT.md item 22).
        ///
        ///   • Fusion sur la fiche employé : chaque TaxCalculation d'une sauvegarde JSON
        ///     porte un instantané HISTORIQUE de l'employé (son état CE mois-là), pas son
        ///     état actuel. Les fusionner un par un, dans l'ordre où la sauvegarde les
        ///     liste (non garanti chronologique), écrasait la fiche employé actuelle —
        ///     déjà restaurée séparément par <see cref="SaveEmployeesAsync"/> — par la
        ///     valeur d'un ancien bulletin pris arbitrairement.
        ///   • Verrou de clôture : une sauvegarde reflète des bulletins déjà émis, y
        ///     compris dans des mois clôturés depuis ; le verrou existe pour empêcher une
        ///     NOUVELLE saisie sur un mois clos, pas pour interdire de restaurer ce qui a
        ///     déjà été déclaré. Avec le verrou, la restauration échouait entièrement (la
        ///     transaction entière annulée) dès que la sauvegarde contenait ne serait-ce
        ///     qu'un bulletin d'un mois clôturé depuis — le cas normal dès que le dossier a
        ///     un peu vécu.
        /// </summary>
        public async Task SaveCalculationsHistoryAsync(List<TaxCalculation> calculations)
        {
            using (var conn = _sqliteService.CreerConnexion())
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
                    if (c == null) continue;

                    if (string.IsNullOrEmpty(c.Id))
                        c.Id = Guid.NewGuid().ToString();
                    if (c.CalculationDate == default)
                        c.CalculationDate = DateTime.Now;

                    await InsererLigneCalculAsync(c, conn, tx);
                }
                tx.Commit();
            }
        }

        /// <summary>
        /// Enregistre le bulletin d'un salarié pour un mois donné, en REMPLAÇANT celui
        /// qui existerait déjà pour ce salarié et ce mois.
        ///
        /// C'est ce remplacement qui fait la différence entre un historique et un
        /// empilement : sans lui, régénérer trois fois le bulletin de septembre laisserait
        /// trois lignes, et la déclaration CNSS déclarerait trois fois le même salaire.
        /// Un salarié n'a qu'un bulletin par mois.
        ///
        /// Le verrou de clôture s'applique : un mois arrêté n'accepte plus de bulletin.
        /// </summary>
        public async Task EnregistrerBulletinDuMoisAsync(
            TaxCalculation calculation, DateTime periode, Bulletin bulletin = null)
        {
            if (calculation == null) throw new ArgumentNullException(nameof(calculation));

            var debut = new DateTime(periode.Year, periode.Month, 1);
            var fin = debut.AddMonths(1).AddDays(-1);

            // La date du calcul EST celle de la période : c'est elle que lisent les
            // déclarations, et c'est sur elle que porte le verrou de clôture.
            calculation.CalculationDate = fin;

            string employeeId = calculation.Employee != null ? calculation.Employee.EmployeeId : null;
            int remplaces = 0;

            using (var conn = _sqliteService.CreerConnexion())
            using (var tx = conn.BeginTransaction())
            {
                try
                {
                    if (!string.IsNullOrWhiteSpace(employeeId))
                    {
                        using (var cmd = conn.CreateCommand())
                        {
                            cmd.Transaction = tx;
                            cmd.CommandText =
                                "DELETE FROM TaxCalculations " +
                                "WHERE EmployeeId = @employe " +
                                "  AND CalculationDate >= @debut AND CalculationDate <= @fin " +
                                "  AND IsAnnual = 0 AND IsRegularization = 0;";
                            cmd.Parameters.AjouterValeur("@employe", employeeId);
                            cmd.Parameters.AjouterValeur("@debut", DbValue.DateToDb(debut));
                            cmd.Parameters.AjouterValeur("@fin", DbValue.DateToDb(fin.AddDays(1).AddTicks(-1)));
                            remplaces = await cmd.ExecuteNonQueryAsync();
                        }
                    }

                    // Un identifiant neuf : l'ancien vient d'être supprimé, le réutiliser
                    // ferait dépendre le résultat de l'ordre des opérations.
                    calculation.Id = Guid.NewGuid().ToString();

                    await AddCalculationToHistoryAsync(calculation, conn, tx);

                    // Le document lui-même est archivé à côté de son calcul : c'est lui
                    // qu'on réaffichera, sans rien recalculer, si le bulletin est rappelé.
                    if (bulletin != null)
                        await ArchiverBulletinAsync(conn, tx, calculation.Id, bulletin);

                    // Journal d'audit (entité « Calcul »), dans la même transaction.
                    await AuditService.AuditerEnregistrementCalculAsync(conn, tx, calculation, periode, bulletin != null, remplaces);

                    tx.Commit();
                }
                catch
                {
                    try { tx.Rollback(); }
                    catch { /* connexion perdue */ }
                    throw;
                }
            }
        }

        /// <summary>
        /// Écrit le bulletin édité en clair à côté de son calcul. Un échec de
        /// sérialisation ne doit pas faire perdre la paie : le calcul reste enregistré,
        /// seul le rappel à l'identique sera indisponible pour ce mois.
        /// </summary>
        private static async Task ArchiverBulletinAsync(
            SqliteConnection conn, SqliteTransaction tx, string calculationId, Bulletin bulletin)
        {
            if (string.IsNullOrEmpty(calculationId) || bulletin == null) return;

            string archive;
            try
            {
                archive = JsonSerializer.Serialize(bulletin);
            }
            catch
            {
                return;
            }

            using (var cmd = conn.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = "UPDATE TaxCalculations SET BulletinArchive = @archive WHERE Id = @id;";
                cmd.Parameters.AjouterValeur("@archive", archive);
                cmd.Parameters.AjouterValeur("@id", calculationId);
                await cmd.ExecuteNonQueryAsync();
            }
        }

        /// <summary>
        /// Rappelle le bulletin archivé d'un salarié pour un mois : le document tel qu'il
        /// a été édité, sans aucun recalcul. Renvoie null si ce mois n'a pas de bulletin
        /// enregistré, ou s'il a été enregistré avant l'archivage (bulletins antérieurs à
        /// la migration 15) — l'appelant propose alors de le régénérer.
        /// </summary>
        public async Task<Bulletin> RappelerBulletinAsync(string employeeId, DateTime periode)
        {
            if (string.IsNullOrWhiteSpace(employeeId)) return null;

            var debut = new DateTime(periode.Year, periode.Month, 1);
            var fin = debut.AddMonths(1).AddDays(-1);

            string archive = null;

            using (var conn = _sqliteService.CreerConnexion())
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText =
                    "SELECT BulletinArchive FROM TaxCalculations " +
                    "WHERE EmployeeId = @employe " +
                    "  AND CalculationDate >= @debut AND CalculationDate <= @fin " +
                    "  AND IsAnnual = 0 AND IsRegularization = 0 " +
                    "ORDER BY CalculationDate DESC LIMIT 1;";
                cmd.Parameters.AjouterValeur("@employe", employeeId);
                cmd.Parameters.AjouterValeur("@debut", DbValue.DateToDb(debut));
                cmd.Parameters.AjouterValeur("@fin", DbValue.DateToDb(fin.AddDays(1).AddTicks(-1)));

                using (var reader = (SqliteDataReader)await cmd.ExecuteReaderAsync())
                {
                    if (await reader.ReadAsync())
                        archive = DbValue.TextFromDb(reader["BulletinArchive"]);
                }
            }

            if (string.IsNullOrWhiteSpace(archive)) return null;

            try
            {
                return JsonSerializer.Deserialize<Bulletin>(archive);
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Numéro du solde de tout compte validé dont la date de sortie tombe dans ce mois
        /// pour ce salarié, ou null. Le bulletin de ce mois est alors son bulletin de sortie :
        /// les écrans de calcul ne doivent pas le remplacer par une paie ordinaire.
        /// </summary>
        public async Task<int?> SoldeToutCompteDuMoisAsync(string employeeId, DateTime periode)
        {
            if (string.IsNullOrWhiteSpace(employeeId)) return null;
            var debut = new DateTime(periode.Year, periode.Month, 1);
            var fin = debut.AddMonths(1).AddDays(-1);
            using (var conn = _sqliteService.CreerConnexion())
            {
                if (!TableExiste(conn, "SoldesToutCompte")) return null;
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT Id FROM SoldesToutCompte WHERE EmployeeId = @e " +
                                      "AND DateSortie >= @d AND DateSortie <= @f ORDER BY Id DESC LIMIT 1;";
                    cmd.Parameters.AjouterValeur("@e", employeeId);
                    cmd.Parameters.AjouterValeur("@d", DbValue.DateToDb(debut));
                    cmd.Parameters.AjouterValeur("@f", DbValue.DateToDb(fin.AddDays(1).AddTicks(-1)));
                    object o = await cmd.ExecuteScalarAsync();
                    if (o == null || o == DBNull.Value) return null;
                    return Convert.ToInt32(o);
                }
            }
        }

        /// <summary>Vrai si un bulletin est enregistré pour ce salarié et ce mois.</summary>
        public async Task<bool> BulletinEnregistreAsync(string employeeId, DateTime periode)
        {
            if (string.IsNullOrWhiteSpace(employeeId)) return false;

            var debut = new DateTime(periode.Year, periode.Month, 1);
            var fin = debut.AddMonths(1).AddDays(-1);

            using (var conn = _sqliteService.CreerConnexion())
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText =
                    "SELECT COUNT(*) FROM TaxCalculations " +
                    "WHERE EmployeeId = @employe " +
                    "  AND CalculationDate >= @debut AND CalculationDate <= @fin " +
                    "  AND IsAnnual = 0 AND IsRegularization = 0;";
                cmd.Parameters.AjouterValeur("@employe", employeeId);
                cmd.Parameters.AjouterValeur("@debut", DbValue.DateToDb(debut));
                cmd.Parameters.AjouterValeur("@fin", DbValue.DateToDb(fin.AddDays(1).AddTicks(-1)));

                object brut = await cmd.ExecuteScalarAsync();
                int nombre;
                return brut != null && int.TryParse(brut.ToString(), out nombre) && nombre > 0;
            }
        }

        public async Task AddCalculationToHistoryAsync(TaxCalculation calculation)
        {
            using (var conn = _sqliteService.CreerConnexion())
            using (var tx = conn.BeginTransaction())
            {
                await AddCalculationToHistoryAsync(calculation, conn, tx);
                await AuditService.AuditerEnregistrementCalculAsync(conn, tx, calculation, calculation.CalculationDate, false);
                tx.Commit();
            }
        }

        /// <summary>
        /// Refuse l'écriture si le calcul tombe dans une période de paie clôturée
        /// (module PAI-01).
        ///
        /// C'est ici, et nulle part ailleurs, que le verrou a un sens : tous les
        /// enregistrements d'un calcul passent par <c>AddCalculationToHistoryAsync</c>.
        /// Sans ce contrôle, la clôture ne serait qu'une étiquette — on pourrait écraser
        /// en novembre un bulletin de mars déjà déclaré à la CNSS et à la DGI.
        ///
        /// Tant qu'aucune période n'a été clôturée, ce contrôle ne change rien : une
        /// période jamais ouverte n'est pas close.
        /// </summary>
        private static async Task VerifierPeriodeOuverteAsync(
            SqliteConnection conn, SqliteTransaction tx, TaxCalculation calculation, string action = "enregistré")
        {
            if (calculation == null) return;

            if (await PeriodePaieService.EstClotureeAsync(conn, calculation.CalculationDate, tx))
            {
                throw new InvalidOperationException(string.Format(
                    "La période de {0:MM/yyyy} est clôturée : ce bulletin ne peut plus être " +
                    "{1}. Rouvrez la période depuis l'écran « Périodes de paie » si la " +
                    "correction est justifiée.",
                    calculation.CalculationDate, action));
            }
        }

        /// <summary>
        /// Relit une fiche employé par matricule DANS la transaction en cours (contrairement
        /// à <see cref="GetEmployeeByIdAsync"/>, qui ouvre sa propre connexion). Utilisé pour
        /// fusionner en toute sécurité, voir <see cref="AddCalculationToHistoryAsync(TaxCalculation, SqliteConnection, SqliteTransaction)"/>.
        /// </summary>
        private async Task<Employee> LireEmployeParIdAsync(string employeeId, SqliteConnection conn, SqliteTransaction tx)
        {
            using (var cmd = conn.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = "SELECT " + ColonnesEmploye + " FROM Employees WHERE EmployeeId = @EmployeeId";
                cmd.Parameters.AjouterValeur("@EmployeeId", employeeId);
                using (var reader = (SqliteDataReader)await cmd.ExecuteReaderAsync())
                {
                    if (await reader.ReadAsync())
                        return LireEmploye(reader);
                }
            }
            return null;
        }

        private async Task AddCalculationToHistoryAsync(TaxCalculation calculation, SqliteConnection conn, SqliteTransaction tx)
        {
            if (calculation == null)
                throw new ArgumentNullException(nameof(calculation));

            if (string.IsNullOrEmpty(calculation.Id))
                calculation.Id = Guid.NewGuid().ToString();

            if (calculation.CalculationDate == default)
                calculation.CalculationDate = DateTime.Now;

            // Verrou de clôture : la date est désormais connue, le contrôle a un sens.
            await VerifierPeriodeOuverteAsync(conn, tx, calculation);

            // L'employé peut être absent (calcul libre, sans fiche associée) : la colonne
            // EmployeeId accepte alors une chaîne vide, comme plus bas.
            //
            // Filet de sécurité contre la fiche périmée (signalé les 16 septembre 2026, voir
            // ETAT-DEVELOPPEMENT.md items 18 et 19) : l'objet Employee attaché à un calcul
            // peut avoir été chargé en mémoire depuis un moment (écran resté ouvert, calcul
            // rechargé depuis l'historique...) et ne plus refléter une modification faite
            // depuis via la fiche salarié ou un import CSV. Enregistrer un calcul ne doit
            // jamais réécrire une fiche existante avec une copie potentiellement périmée :
            //   - l'employé n'existe pas encore en base → calcul libre sur un employé tout
            //     juste créé dans un écran de calcul, on l'insère tel quel ;
            //   - l'employé existe déjà → seuls les champs qu'un écran de calcul a
            //     effectivement vocation à modifier (identité et éléments de paie) sont
            //     recopiés sur la fiche ACTUELLE en base ; tout le reste (état civil,
            //     coordonnées, convention, date de sortie...) vient de cette fiche actuelle,
            //     jamais de la copie en mémoire du calcul.
            if (calculation.Employee != null && !string.IsNullOrWhiteSpace(calculation.Employee.EmployeeId))
            {
                var employeActuel = await LireEmployeParIdAsync(calculation.Employee.EmployeeId, conn, tx);
                if (employeActuel == null)
                {
                    await AddEmployeeAsync(calculation.Employee, conn, tx);
                }
                else
                {
                    employeActuel.FirstName = calculation.Employee.FirstName;
                    employeActuel.LastName = calculation.Employee.LastName;
                    // IsHigherCategory n'est pas recopiée : son accesseur set est privé, et
                    // elle se recalcule automatiquement dès l'affectation de Category
                    // ci-dessous (voir Employee.Category et Employee.DetermineIfHigherCategory).
                    employeActuel.Category = calculation.Employee.Category;
                    employeActuel.BaseSalary = calculation.Employee.BaseSalary;
                    employeActuel.HousingAllowance = calculation.Employee.HousingAllowance;
                    employeActuel.TransportAllowance = calculation.Employee.TransportAllowance;
                    employeActuel.FunctionAllowance = calculation.Employee.FunctionAllowance;
                    employeActuel.SujetionAllowance = calculation.Employee.SujetionAllowance;
                    employeActuel.FamilyCharges = calculation.Employee.FamilyCharges;
                    employeActuel.IsSpouseWorking = calculation.Employee.IsSpouseWorking;
                    employeActuel.PensionContribution = calculation.Employee.PensionContribution;

                    await AddEmployeeAsync(employeActuel, conn, tx);

                    // Le calcul enregistré référence désormais la fiche à jour, pas la copie
                    // périmée : la colonne EmployeeId ci-dessous et tout appelant qui relit
                    // calculation.Employee après cet appel voient l'état réel.
                    calculation.Employee = employeActuel;
                }
            }

            await InsererLigneCalculAsync(calculation, conn, tx);
        }

        /// <summary>
        /// Insère (ou remplace) la seule ligne TaxCalculations, sans verrou de clôture ni
        /// fusion sur la fiche employé — ces deux contrôles sont la responsabilité de
        /// l'appelant. Utilisée par <see cref="AddCalculationToHistoryAsync(TaxCalculation, SqliteConnection, SqliteTransaction)"/>
        /// (qui les fait avant d'appeler ceci) et directement par
        /// <see cref="SaveCalculationsHistoryAsync"/> (qui ne doit faire ni l'un ni l'autre).
        /// </summary>
        private static async Task InsererLigneCalculAsync(TaxCalculation calculation, SqliteConnection conn, SqliteTransaction tx)
        {
            using (var cmd = conn.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = @"
                    INSERT OR REPLACE INTO TaxCalculations
                    (Id, EmployeeId, CalculationDate, SalaireBrut, BaseIUTSHorsDeduction, IUTSNet, TaxRate, FamilyChargeReduction, ExemptAmount, CalculationDetails, IsAnnual, IsRegularization, TaxDifference, CnssEmployee, CnssPartPatronale, TaxePatronale, RetenueSpeciale, MasseSalariale, ChargesPatronales, NetSalaryAfterTax, SalaireNetaPayer, AssietteCnss, CnssPrestationsFamiliales, CnssRisquesProfessionnels, CnssPensionsPatronal, RetenuesDiverses, GainsVariables, AssietteTPA)
                    VALUES
                    (@Id, @EmployeeId, @CalculationDate, @SalaireBrut, @BaseIUTSHorsDeduction, @IUTSNet, @TaxRate, @FamilyChargeReduction, @ExemptAmount, @CalculationDetails, @IsAnnual, @IsRegularization, @TaxDifference, @CnssEmployee, @CnssPartPatronale, @TaxePatronale, @RetenueSpeciale, @MasseSalariale, @ChargesPatronales, @NetSalaryAfterTax, @SalaireNetaPayer, @AssietteCnss, @CnssPrestationsFamiliales, @CnssRisquesProfessionnels, @CnssPensionsPatronal, @RetenuesDiverses, @GainsVariables, @AssietteTPA);";

                cmd.Parameters.AjouterValeur("@Id", calculation.Id);
                cmd.Parameters.AjouterValeur("@EmployeeId", calculation.Employee?.EmployeeId ?? "");
                cmd.Parameters.AjouterValeur("@CalculationDate", DbValue.DateToDb(calculation.CalculationDate));
                cmd.Parameters.AjouterValeur("@SalaireBrut", DbValue.MoneyToDb(calculation.SalaireBrut));
                cmd.Parameters.AjouterValeur("@BaseIUTSHorsDeduction", DbValue.MoneyToDb(calculation.BaseIUTSHorsDeduction));
                cmd.Parameters.AjouterValeur("@IUTSNet", DbValue.MoneyToDb(calculation.IUTSNet));
                // TaxRate est un pourcentage à nombreuses décimales : il ne doit surtout pas
                // être arrondi au centime comme un montant.
                cmd.Parameters.AjouterValeur("@TaxRate", DbValue.DecimalToDb(calculation.TaxRate));
                cmd.Parameters.AjouterValeur("@FamilyChargeReduction", DbValue.MoneyToDb(calculation.FamilyChargeReduction));
                cmd.Parameters.AjouterValeur("@ExemptAmount", DbValue.MoneyToDb(calculation.ExemptAmount));
                cmd.Parameters.AjouterValeur("@CalculationDetails", calculation.CalculationDetails ?? "");
                cmd.Parameters.AjouterValeur("@IsAnnual", calculation.IsAnnual ? 1 : 0);
                cmd.Parameters.AjouterValeur("@IsRegularization", calculation.IsRegularization ? 1 : 0);
                cmd.Parameters.AjouterValeur("@TaxDifference", DbValue.MoneyToDb(calculation.TaxDifference));
                cmd.Parameters.AjouterValeur("@CnssEmployee", DbValue.MoneyToDb(calculation.CnssEmployee));
                cmd.Parameters.AjouterValeur("@CnssPartPatronale", DbValue.MoneyToDb(calculation.CnssPartPatronale));
                cmd.Parameters.AjouterValeur("@TaxePatronale", DbValue.MoneyToDb(calculation.TaxePatronale));
                cmd.Parameters.AjouterValeur("@RetenueSpeciale", DbValue.MoneyToDb(calculation.RetenueSpeciale));
                cmd.Parameters.AjouterValeur("@MasseSalariale", DbValue.MoneyToDb(calculation.MasseSalariale));
                cmd.Parameters.AjouterValeur("@ChargesPatronales", DbValue.MoneyToDb(calculation.ChargesPatronales));
                // Les deux nets sont des champs de persistance distincts des propriétés
                // calculées. Si l'appelant ne les a pas alimentés, on écrit la valeur
                // calculée plutôt qu'un zéro : un net à payer nul en base rend faux tout
                // ce qui s'appuie dessus — totaux de période, livre de paie, virements.
                decimal netApresImpot = calculation.NetSalaryAfterTaxPersisted != 0m
                    ? calculation.NetSalaryAfterTaxPersisted
                    : calculation.NetSalaryAfterTax;

                decimal netAPayer = calculation.SalaireNetaPayerPersisted != 0m
                    ? calculation.SalaireNetaPayerPersisted
                    : calculation.SalaireNetaPayer;

                cmd.Parameters.AjouterValeur("@NetSalaryAfterTax", DbValue.MoneyToDb(netApresImpot));
                cmd.Parameters.AjouterValeur("@SalaireNetaPayer", DbValue.MoneyToDb(netAPayer));
                cmd.Parameters.AjouterValeur("@AssietteCnss", DbValue.MoneyToDb(calculation.AssietteCnss));
                cmd.Parameters.AjouterValeur("@CnssPrestationsFamiliales", DbValue.MoneyToDb(calculation.CnssPrestationsFamiliales));
                cmd.Parameters.AjouterValeur("@CnssRisquesProfessionnels", DbValue.MoneyToDb(calculation.CnssRisquesProfessionnels));
                cmd.Parameters.AjouterValeur("@CnssPensionsPatronal", DbValue.MoneyToDb(calculation.CnssPensionsPatronal));
                cmd.Parameters.AjouterValeur("@RetenuesDiverses", DbValue.MoneyToDb(calculation.RetenuesDiverses));
                cmd.Parameters.AjouterValeur("@GainsVariables", DbValue.MoneyToDb(calculation.GainsVariables));
                cmd.Parameters.AjouterValeur("@AssietteTPA", DbValue.MoneyToDb(calculation.AssietteTPA));

                await cmd.ExecuteNonQueryAsync();
            }
        }

        /// <summary>
        /// Supprime un bulletin de l'historique.
        ///
        /// Contrairement à tous les autres chemins d'écriture sur TaxCalculations, cette
        /// méthode ne passait par AUCUN verrou de clôture : un bulletin d'un mois déjà
        /// clôturé (et donc, normalement, déjà déclaré à la CNSS/DGI) pouvait être
        /// silencieusement supprimé, ce qui est pire qu'une simple réécriture puisque la
        /// clôture est censée garantir que le bulletin reste consultable tel qu'il a été
        /// déclaré — signalé le 16 septembre 2026 (voir ETAT-DEVELOPPEMENT.md item 22).
        /// </summary>
        public async Task DeleteCalculationFromHistoryAsync(string calculationId)
        {
            using (var conn = _sqliteService.CreerConnexion())
            using (var tx = conn.BeginTransaction())
            {
                try
                {
                    DateTime dateCalcul = DateTime.MinValue;
                    bool trouve = false;
                    string employeId = null;

                    using (var cmdLecture = conn.CreateCommand())
                    {
                        cmdLecture.Transaction = tx;
                        cmdLecture.CommandText = "SELECT CalculationDate, EmployeeId FROM TaxCalculations WHERE Id = @Id";
                        cmdLecture.Parameters.AjouterValeur("@Id", calculationId);
                        using (var reader = (SqliteDataReader)await cmdLecture.ExecuteReaderAsync())
                        {
                            if (await reader.ReadAsync())
                            {
                                dateCalcul = DbValue.DateFromDb(reader["CalculationDate"], DateTime.Now);
                                employeId = DbValue.TextFromDb(reader["EmployeeId"]);
                                trouve = true;
                            }
                        }
                    }

                    // Introuvable : rien à supprimer, et donc rien à protéger — pas d'erreur,
                    // pour que rappeler cette méthode sur un Id déjà supprimé reste sans effet.
                    if (trouve)
                    {
                        await VerifierPeriodeOuverteAsync(
                            conn, tx, new TaxCalculation { CalculationDate = dateCalcul }, "supprimé");
                    }

                    using (var cmd = conn.CreateCommand())
                    {
                        cmd.Transaction = tx;
                        cmd.CommandText = "DELETE FROM TaxCalculations WHERE Id = @Id";
                        cmd.Parameters.AjouterValeur("@Id", calculationId);
                        await cmd.ExecuteNonQueryAsync();
                    }

                    if (trouve)
                        await AuditService.AuditerSuppressionCalculAsync(conn, tx, calculationId, dateCalcul, employeId);

                    tx.Commit();
                }
                catch
                {
                    try { tx.Rollback(); }
                    catch { /* connexion perdue */ }
                    throw;
                }
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
            using (var conn = _sqliteService.CreerConnexion())
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "SELECT * FROM AppSettings LIMIT 1";
                using (var reader = (SqliteDataReader)await cmd.ExecuteReaderAsync())
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

                        // Colonnes ajoutées par la migration 13 (congés et absences).
                        if (ColumnExists(reader, "JoursCongesParMois"))
                        {
                            decimal jours = DbValue.DecimalFromDb(reader["JoursCongesParMois"]);
                            if (jours > 0m) settings.JoursCongesParMois = jours;
                        }

                        if (ColumnExists(reader, "BaseRetenueAbsence"))
                        {
                            int baseRetenue;
                            if (int.TryParse((reader["BaseRetenueAbsence"] ?? "").ToString(), out baseRetenue)
                                && Enum.IsDefined(typeof(BaseRetenueAbsence), baseRetenue))
                            {
                                settings.BaseRetenueAbsence = (BaseRetenueAbsence)baseRetenue;
                            }
                        }

                        // Colonne ajoutée par la migration 12 : une base plus ancienne
                        // ouverte avant la mise à niveau garde la valeur par défaut.
                        if (ColumnExists(reader, "AfficherDevise"))
                        {
                            var brut = reader["AfficherDevise"];
                            int valeur;
                            settings.AfficherDeviseSurBulletin =
                                brut == null || brut == DBNull.Value
                                || !int.TryParse(brut.ToString(), out valeur) || valeur != 0;
                        }

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
            using (var conn = _sqliteService.CreerConnexion())
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
                        (Theme, Language, AutoSave, LastUpdated, ExportDirectory, TaxBrackets, FamilyCharges, AfficherDevise, JoursCongesParMois, BaseRetenueAbsence)
                        VALUES 
                        (@Theme, @Language, @AutoSave, @LastUpdated, @ExportDirectory, @TaxBrackets, @FamilyCharges, @AfficherDevise, @JoursCongesParMois, @BaseRetenueAbsence);";
                    cmd.Parameters.AjouterValeur("@Theme", settings.Theme ?? "Light");
                    cmd.Parameters.AjouterValeur("@Language", settings.Language ?? "fr-FR");
                    cmd.Parameters.AjouterValeur("@AutoSave", settings.AutoSave ? 1 : 0);
                    cmd.Parameters.AjouterValeur("@LastUpdated", DbValue.DateToDb(settings.LastUpdated));
                    cmd.Parameters.AjouterValeur("@ExportDirectory", settings.ExportDirectory ?? string.Empty);
                    cmd.Parameters.AjouterValeur("@TaxBrackets", settings.TaxBrackets != null ? JsonSerializer.Serialize(settings.TaxBrackets) : string.Empty);
                    cmd.Parameters.AjouterValeur("@FamilyCharges", settings.FamilyCharges != null ? JsonSerializer.Serialize(settings.FamilyCharges) : string.Empty);
                    cmd.Parameters.AjouterValeur("@AfficherDevise", settings.AfficherDeviseSurBulletin ? 1 : 0);
                    cmd.Parameters.AjouterValeur("@JoursCongesParMois", DbValue.DecimalToDb(
                        settings.JoursCongesParMois > 0m ? settings.JoursCongesParMois : 2.5m));
                    cmd.Parameters.AjouterValeur("@BaseRetenueAbsence", (int)settings.BaseRetenueAbsence);
                    await cmd.ExecuteNonQueryAsync();
                }
                tx.Commit();
            }
        }

        private bool ColumnExists(SqliteDataReader reader, string columnName)
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
