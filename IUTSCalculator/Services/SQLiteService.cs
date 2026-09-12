using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Globalization;
using System.IO;

namespace IUTSCalculator.Services
{
    /// <summary>
    /// Accès à la base SQLite d'un dossier : ouverture des connexions et mise à niveau
    /// du schéma.
    ///
    /// Deux principes :
    ///
    ///   • <b>Une connexion par opération.</b> Cette classe ne conserve plus de connexion
    ///     ouverte partagée : <see cref="SQLiteConnection"/> n'est pas thread-safe, et la
    ///     partager entre des méthodes <c>async</c> exposait l'application à des lectures
    ///     entrelacées et à des transactions imbriquées. Chaque appelant ouvre la sienne
    ///     dans un <c>using</c>. L'ouverture d'une connexion SQLite est très peu coûteuse.
    ///
    ///   • <b>Un schéma versionné.</b> La table <c>SchemaVersion</c> mémorise le niveau
    ///     atteint par la base, et les migrations s'appliquent dans l'ordre. Une base
    ///     existante est convertie automatiquement à l'ouverture du dossier ; une copie de
    ///     sauvegarde est déposée à côté du fichier avant toute conversion de données.
    /// </summary>
    public class SQLiteService : IDisposable
    {
        /// <summary>Niveau de schéma attendu par cette version de l'application.</summary>
        private const int SchemaCible = 2;

        private readonly string _dbPath;
        private readonly string _connectionString;

        public SQLiteService(string dbFileName = "iuts_data.db")
        {
            _dbPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "IUTSCalculator", dbFileName);

            Directory.CreateDirectory(Path.GetDirectoryName(_dbPath));

            // BusyTimeout : si une autre opération de l'application écrit au même moment,
            // on patiente au lieu de lever immédiatement SQLITE_BUSY.
            _connectionString = string.Format(
                CultureInfo.InvariantCulture,
                "Data Source={0};Version=3;BusyTimeout=5000;",
                _dbPath);

            EnsureSchema();
        }

        /// <summary>Chemin complet du fichier de base de données du dossier.</summary>
        public string DatabasePath
        {
            get { return _dbPath; }
        }

        /// <summary>
        /// Ouvre une NOUVELLE connexion, déjà ouverte. L'appelant en est propriétaire et
        /// doit la libérer — typiquement avec <c>using (var conn = ...CreateConnection())</c>.
        /// </summary>
        public SQLiteConnection CreateConnection()
        {
            var connection = new SQLiteConnection(_connectionString);
            connection.Open();
            return connection;
        }

        #region Mise à niveau du schéma

        private void EnsureSchema()
        {
            bool baseVierge;
            int version;

            // Premier temps : déterminer l'état de la base, puis refermer la connexion
            // pour pouvoir copier le fichier en toute sécurité si une migration est due.
            using (var connection = CreateConnection())
            {
                CreerTableVersion(connection);
                baseVierge = !TableExiste(connection, "Employees");
                version = LireVersion(connection);
            }

            if (baseVierge)
            {
                using (var connection = CreateConnection())
                {
                    CreerSchemaComplet(connection);
                    EnregistrerVersion(connection, SchemaCible);
                }
                return;
            }

            if (version >= SchemaCible)
                return;

            SauvegarderAvantMigration();

            using (var connection = CreateConnection())
            {
                try
                {
                    if (version < 1)
                    {
                        Migration1_ColonnesManquantes(connection);
                        EnregistrerVersion(connection, 1);
                    }

                    if (version < 2)
                    {
                        Migration2_MontantsExacts(connection);
                        EnregistrerVersion(connection, 2);
                    }
                }
                catch (Exception ex)
                {
                    throw new InvalidOperationException(
                        "La mise à niveau de la base du dossier a échoué : " + _dbPath +
                        ". Aucune donnée n'a été modifiée ; une copie de sauvegarde se trouve " +
                        "à côté du fichier. Détail : " + ex.Message, ex);
                }
            }
        }

        /// <summary>
        /// Dépose une copie du fichier avant une migration qui transforme des données,
        /// afin qu'un incident reste toujours réversible.
        /// </summary>
        private void SauvegarderAvantMigration()
        {
            try
            {
                if (!File.Exists(_dbPath))
                    return;

                string sauvegarde = string.Format(
                    CultureInfo.InvariantCulture,
                    "{0}.avant-v{1}.bak",
                    _dbPath,
                    SchemaCible);

                if (!File.Exists(sauvegarde))
                    File.Copy(_dbPath, sauvegarde);
            }
            catch
            {
                // Une sauvegarde impossible (disque plein, droits) ne doit pas empêcher
                // l'ouverture du dossier : la migration reste transactionnelle.
            }
        }

        private void CreerTableVersion(SQLiteConnection connection)
        {
            Executer(connection, @"
                CREATE TABLE IF NOT EXISTS SchemaVersion (
                    Version   INTEGER NOT NULL,
                    AppliedOn TEXT    NOT NULL
                );");
        }

        private int LireVersion(SQLiteConnection connection)
        {
            using (var cmd = connection.CreateCommand())
            {
                cmd.CommandText = "SELECT COALESCE(MAX(Version), 0) FROM SchemaVersion;";
                object resultat = cmd.ExecuteScalar();
                return DbValue.IntFromDb(resultat);
            }
        }

        private void EnregistrerVersion(SQLiteConnection connection, int version)
        {
            using (var cmd = connection.CreateCommand())
            {
                cmd.CommandText = "INSERT INTO SchemaVersion (Version, AppliedOn) VALUES (@v, @d);";
                cmd.Parameters.AddWithValue("@v", version);
                cmd.Parameters.AddWithValue("@d", DbValue.DateToDb(DateTime.Now));
                cmd.ExecuteNonQuery();
            }
        }

        /// <summary>
        /// Schéma complet, tel qu'attendu par la version courante de l'application.
        ///
        /// ATTENTION : toutes les colonnes de MONTANT sont des INTEGER exprimés en
        /// CENTIMES (voir <see cref="DbValue"/>). Un salaire de base de 250 000 FCFA y
        /// est stocké sous la forme 25 000 000.
        /// </summary>
        private void CreerSchemaComplet(SQLiteConnection connection)
        {
            Executer(connection, @"
                CREATE TABLE IF NOT EXISTS Employees (
                    EmployeeId            TEXT PRIMARY KEY,
                    FirstName             TEXT,
                    LastName              TEXT,
                    BaseSalary            INTEGER NOT NULL DEFAULT 0,
                    HousingAllowance      INTEGER NOT NULL DEFAULT 0,
                    TransportAllowance    INTEGER NOT NULL DEFAULT 0,
                    FunctionAllowance     INTEGER NOT NULL DEFAULT 0,
                    SujetionAllowance     INTEGER NOT NULL DEFAULT 0,
                    FamilyCharges         INTEGER NOT NULL DEFAULT 0,
                    IsSpouseWorking       INTEGER NOT NULL DEFAULT 0,
                    PensionContribution   INTEGER NOT NULL DEFAULT 0,
                    Category              TEXT,
                    IsHigherCategory      INTEGER NOT NULL DEFAULT 0,
                    Fonction              TEXT,
                    DateEntree            TEXT,
                    SituationMatrimoniale TEXT
                );");

            Executer(connection, @"
                CREATE TABLE IF NOT EXISTS TaxCalculations (
                    Id                    TEXT PRIMARY KEY,
                    EmployeeId            TEXT,
                    CalculationDate       TEXT,
                    SalaireBrut           INTEGER NOT NULL DEFAULT 0,
                    BaseIUTSHorsDeduction INTEGER NOT NULL DEFAULT 0,
                    IUTSNet               INTEGER NOT NULL DEFAULT 0,
                    TaxRate               TEXT,
                    FamilyChargeReduction INTEGER NOT NULL DEFAULT 0,
                    ExemptAmount          INTEGER NOT NULL DEFAULT 0,
                    CalculationDetails    TEXT,
                    IsAnnual              INTEGER NOT NULL DEFAULT 0,
                    IsRegularization      INTEGER NOT NULL DEFAULT 0,
                    TaxDifference         INTEGER NOT NULL DEFAULT 0,
                    CnssEmployee          INTEGER NOT NULL DEFAULT 0,
                    CnssPartPatronale     INTEGER NOT NULL DEFAULT 0,
                    TaxePatronale         INTEGER NOT NULL DEFAULT 0,
                    RetenueSpeciale       INTEGER NOT NULL DEFAULT 0,
                    MasseSalariale        INTEGER NOT NULL DEFAULT 0,
                    ChargesPatronales     INTEGER NOT NULL DEFAULT 0,
                    NetSalaryAfterTax     INTEGER NOT NULL DEFAULT 0,
                    SalaireNetaPayer      INTEGER NOT NULL DEFAULT 0,
                    FOREIGN KEY(EmployeeId) REFERENCES Employees(EmployeeId)
                );");

            Executer(connection,
                "CREATE INDEX IF NOT EXISTS IX_TaxCalculations_EmployeeId ON TaxCalculations(EmployeeId);");
            Executer(connection,
                "CREATE INDEX IF NOT EXISTS IX_TaxCalculations_Date ON TaxCalculations(CalculationDate);");

            Executer(connection, @"
                CREATE TABLE IF NOT EXISTS AppSettings (
                    Theme           TEXT,
                    Language        TEXT,
                    AutoSave        INTEGER,
                    LastUpdated     TEXT,
                    ExportDirectory TEXT,
                    TaxBrackets     TEXT,
                    FamilyCharges   TEXT
                );");
        }

        /// <summary>
        /// Migration 1 — colonnes manquantes.
        ///
        /// Quatre propriétés du modèle <c>Employee</c> n'étaient ni écrites ni relues :
        /// l'indemnité de sujétion, la fonction occupée, la date d'entrée et la situation
        /// matrimoniale. Elles étaient donc silencieusement perdues à chaque fermeture de
        /// l'application — l'indemnité de sujétion entre pourtant dans le salaire brut
        /// imposable, et la fonction comme la date d'entrée s'impriment sur le bulletin.
        ///
        /// Les colonnes sont créées ici avec la convention de la version 1 (montants en
        /// REAL) ; la migration 2 les convertit ensuite comme toutes les autres.
        /// </summary>
        private void Migration1_ColonnesManquantes(SQLiteConnection connection)
        {
            AjouterColonneSiAbsente(connection, "Employees", "SujetionAllowance", "REAL");
            AjouterColonneSiAbsente(connection, "Employees", "Fonction", "TEXT");
            AjouterColonneSiAbsente(connection, "Employees", "DateEntree", "TEXT");
            AjouterColonneSiAbsente(connection, "Employees", "SituationMatrimoniale", "TEXT");

            // Reprise du rattrapage historique sur les paramètres.
            AjouterColonneSiAbsente(connection, "AppSettings", "ExportDirectory", "TEXT");
            AjouterColonneSiAbsente(connection, "AppSettings", "TaxBrackets", "TEXT");
            AjouterColonneSiAbsente(connection, "AppSettings", "FamilyCharges", "TEXT");
        }

        /// <summary>
        /// Migration 2 — stockage exact des montants.
        ///
        /// Les colonnes monétaires passent de REAL (flottant, donc inexact) à INTEGER en
        /// centimes. SQLite ne sait pas changer le type d'une colonne en place : chaque
        /// table est reconstruite, alimentée depuis l'ancienne avec la conversion, puis
        /// substituée. Le tout dans une transaction — en cas d'incident, rien n'est perdu.
        ///
        /// <c>TaxRate</c> échappe à la conversion : c'est un pourcentage à nombreuses
        /// décimales, pas un montant. Il passe en TEXT, qui préserve sa valeur exacte.
        /// </summary>
        private void Migration2_MontantsExacts(SQLiteConnection connection)
        {
            // Ces deux PRAGMA doivent être posés HORS transaction.
            //
            // legacy_alter_table : depuis SQLite 3.25, ALTER TABLE ... RENAME relit tout le
            // schéma et tente de réécrire les références croisées. Or la procédure de
            // reconstruction consiste précisément à supprimer Employees avant de renommer
            // sa remplaçante — pendant ce court instant, la clé étrangère de
            // TaxCalculations pointe vers une table absente, ce qui ferait échouer le
            // renommage. Le mode hérité, recommandé par la documentation SQLite pour cette
            // procédure, désactive cette relecture.
            // Ces deux réglages sont propres à la connexion et disparaissent avec elle :
            // il n'y a donc rien à restaurer, y compris si la migration échoue.
            Executer(connection, "PRAGMA foreign_keys = OFF;");
            Executer(connection, "PRAGMA legacy_alter_table = ON;");

            using (var tx = connection.BeginTransaction())
            {
                // --- Employees ---
                Executer(connection, tx, "DROP TABLE IF EXISTS Employees_v2;");

                Executer(connection, tx, @"
                    CREATE TABLE Employees_v2 (
                        EmployeeId            TEXT PRIMARY KEY,
                        FirstName             TEXT,
                        LastName              TEXT,
                        BaseSalary            INTEGER NOT NULL DEFAULT 0,
                        HousingAllowance      INTEGER NOT NULL DEFAULT 0,
                        TransportAllowance    INTEGER NOT NULL DEFAULT 0,
                        FunctionAllowance     INTEGER NOT NULL DEFAULT 0,
                        SujetionAllowance     INTEGER NOT NULL DEFAULT 0,
                        FamilyCharges         INTEGER NOT NULL DEFAULT 0,
                        IsSpouseWorking       INTEGER NOT NULL DEFAULT 0,
                        PensionContribution   INTEGER NOT NULL DEFAULT 0,
                        Category              TEXT,
                        IsHigherCategory      INTEGER NOT NULL DEFAULT 0,
                        Fonction              TEXT,
                        DateEntree            TEXT,
                        SituationMatrimoniale TEXT
                    );");

                Executer(connection, tx, @"
                    INSERT INTO Employees_v2 (
                        EmployeeId, FirstName, LastName,
                        BaseSalary, HousingAllowance, TransportAllowance, FunctionAllowance,
                        SujetionAllowance, FamilyCharges, IsSpouseWorking, PensionContribution,
                        Category, IsHigherCategory, Fonction, DateEntree, SituationMatrimoniale)
                    SELECT
                        EmployeeId, FirstName, LastName,
                        CAST(ROUND(COALESCE(BaseSalary,          0) * 100) AS INTEGER),
                        CAST(ROUND(COALESCE(HousingAllowance,    0) * 100) AS INTEGER),
                        CAST(ROUND(COALESCE(TransportAllowance,  0) * 100) AS INTEGER),
                        CAST(ROUND(COALESCE(FunctionAllowance,   0) * 100) AS INTEGER),
                        CAST(ROUND(COALESCE(SujetionAllowance,   0) * 100) AS INTEGER),
                        COALESCE(FamilyCharges,   0),
                        COALESCE(IsSpouseWorking, 0),
                        CAST(ROUND(COALESCE(PensionContribution, 0) * 100) AS INTEGER),
                        Category,
                        COALESCE(IsHigherCategory, 0),
                        Fonction, DateEntree, SituationMatrimoniale
                    FROM Employees;");

                Executer(connection, tx, "DROP TABLE Employees;");
                Executer(connection, tx, "ALTER TABLE Employees_v2 RENAME TO Employees;");

                // --- TaxCalculations ---
                Executer(connection, tx, "DROP TABLE IF EXISTS TaxCalculations_v2;");

                Executer(connection, tx, @"
                    CREATE TABLE TaxCalculations_v2 (
                        Id                    TEXT PRIMARY KEY,
                        EmployeeId            TEXT,
                        CalculationDate       TEXT,
                        SalaireBrut           INTEGER NOT NULL DEFAULT 0,
                        BaseIUTSHorsDeduction INTEGER NOT NULL DEFAULT 0,
                        IUTSNet               INTEGER NOT NULL DEFAULT 0,
                        TaxRate               TEXT,
                        FamilyChargeReduction INTEGER NOT NULL DEFAULT 0,
                        ExemptAmount          INTEGER NOT NULL DEFAULT 0,
                        CalculationDetails    TEXT,
                        IsAnnual              INTEGER NOT NULL DEFAULT 0,
                        IsRegularization      INTEGER NOT NULL DEFAULT 0,
                        TaxDifference         INTEGER NOT NULL DEFAULT 0,
                        CnssEmployee          INTEGER NOT NULL DEFAULT 0,
                        CnssPartPatronale     INTEGER NOT NULL DEFAULT 0,
                        TaxePatronale         INTEGER NOT NULL DEFAULT 0,
                        RetenueSpeciale       INTEGER NOT NULL DEFAULT 0,
                        MasseSalariale        INTEGER NOT NULL DEFAULT 0,
                        ChargesPatronales     INTEGER NOT NULL DEFAULT 0,
                        NetSalaryAfterTax     INTEGER NOT NULL DEFAULT 0,
                        SalaireNetaPayer      INTEGER NOT NULL DEFAULT 0,
                        FOREIGN KEY(EmployeeId) REFERENCES Employees(EmployeeId)
                    );");

                Executer(connection, tx, @"
                    INSERT INTO TaxCalculations_v2 (
                        Id, EmployeeId, CalculationDate,
                        SalaireBrut, BaseIUTSHorsDeduction, IUTSNet, TaxRate,
                        FamilyChargeReduction, ExemptAmount, CalculationDetails,
                        IsAnnual, IsRegularization, TaxDifference,
                        CnssEmployee, CnssPartPatronale, TaxePatronale, RetenueSpeciale,
                        MasseSalariale, ChargesPatronales, NetSalaryAfterTax, SalaireNetaPayer)
                    SELECT
                        Id, EmployeeId, CalculationDate,
                        CAST(ROUND(COALESCE(SalaireBrut,           0) * 100) AS INTEGER),
                        CAST(ROUND(COALESCE(BaseIUTSHorsDeduction, 0) * 100) AS INTEGER),
                        CAST(ROUND(COALESCE(IUTSNet,               0) * 100) AS INTEGER),
                        CAST(TaxRate AS TEXT),
                        CAST(ROUND(COALESCE(FamilyChargeReduction, 0) * 100) AS INTEGER),
                        CAST(ROUND(COALESCE(ExemptAmount,          0) * 100) AS INTEGER),
                        CalculationDetails,
                        COALESCE(IsAnnual,         0),
                        COALESCE(IsRegularization, 0),
                        CAST(ROUND(COALESCE(TaxDifference,      0) * 100) AS INTEGER),
                        CAST(ROUND(COALESCE(CnssEmployee,       0) * 100) AS INTEGER),
                        CAST(ROUND(COALESCE(CnssPartPatronale,  0) * 100) AS INTEGER),
                        CAST(ROUND(COALESCE(TaxePatronale,      0) * 100) AS INTEGER),
                        CAST(ROUND(COALESCE(RetenueSpeciale,    0) * 100) AS INTEGER),
                        CAST(ROUND(COALESCE(MasseSalariale,     0) * 100) AS INTEGER),
                        CAST(ROUND(COALESCE(ChargesPatronales,  0) * 100) AS INTEGER),
                        CAST(ROUND(COALESCE(NetSalaryAfterTax,  0) * 100) AS INTEGER),
                        CAST(ROUND(COALESCE(SalaireNetaPayer,   0) * 100) AS INTEGER)
                    FROM TaxCalculations;");

                Executer(connection, tx, "DROP TABLE TaxCalculations;");
                Executer(connection, tx, "ALTER TABLE TaxCalculations_v2 RENAME TO TaxCalculations;");

                Executer(connection, tx,
                    "CREATE INDEX IF NOT EXISTS IX_TaxCalculations_EmployeeId ON TaxCalculations(EmployeeId);");
                Executer(connection, tx,
                    "CREATE INDEX IF NOT EXISTS IX_TaxCalculations_Date ON TaxCalculations(CalculationDate);");

                tx.Commit();
            }
        }

        #endregion

        #region Utilitaires de schéma

        private void Executer(SQLiteConnection connection, string sql)
        {
            Executer(connection, null, sql);
        }

        private void Executer(SQLiteConnection connection, SQLiteTransaction tx, string sql)
        {
            using (var cmd = connection.CreateCommand())
            {
                if (tx != null)
                    cmd.Transaction = tx;
                cmd.CommandText = sql;
                cmd.ExecuteNonQuery();
            }
        }

        private bool TableExiste(SQLiteConnection connection, string nomTable)
        {
            using (var cmd = connection.CreateCommand())
            {
                cmd.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = @n;";
                cmd.Parameters.AddWithValue("@n", nomTable);
                return DbValue.IntFromDb(cmd.ExecuteScalar()) > 0;
            }
        }

        private List<string> ColonnesDe(SQLiteConnection connection, string nomTable)
        {
            var colonnes = new List<string>();
            using (var cmd = connection.CreateCommand())
            {
                // PRAGMA n'accepte pas de paramètre ; le nom de table provient uniquement
                // de constantes internes à cette classe.
                cmd.CommandText = "PRAGMA table_info(" + nomTable + ");";
                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                        colonnes.Add(Convert.ToString(reader["name"]));
                }
            }
            return colonnes;
        }

        private void AjouterColonneSiAbsente(SQLiteConnection connection, string nomTable, string nomColonne, string type)
        {
            if (!TableExiste(connection, nomTable))
                return;

            foreach (var colonne in ColonnesDe(connection, nomTable))
            {
                if (string.Equals(colonne, nomColonne, StringComparison.OrdinalIgnoreCase))
                    return;
            }

            Executer(connection, "ALTER TABLE " + nomTable + " ADD COLUMN " + nomColonne + " " + type + ";");
        }

        #endregion

        /// <summary>
        /// Conservé pour compatibilité : cette classe ne détient plus de ressource non
        /// managée, chaque connexion étant libérée par son propre appelant.
        /// </summary>
        public void Dispose()
        {
            GC.SuppressFinalize(this);
        }
    }
}
