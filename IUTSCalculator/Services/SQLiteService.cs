using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Globalization;
using System.IO;
using IUTSCalculator.Models;

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
    ///     La conversion est faite <b>tout ou rien</b> : sur une copie de travail, contrôlée
    ///     puis substituée à l'original. Une base mise à niveau par une version plus récente
    ///     du programme est refusée (<see cref="BaseTropRecenteException"/>).
    /// </summary>
    public class SQLiteService : IDisposable
    {
        /// <summary>Niveau de schéma attendu par cette version de l'application.</summary>
        public const int SchemaCible = 38;

        private readonly string _dbPath;
        private readonly string _connectionString;

        public SQLiteService(string dbFileName = "iuts_data.db")
        {
            if (Path.IsPathRooted(dbFileName))
            {
                _dbPath = dbFileName;
            }
            else
            {
                _dbPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "IUTSCalculator", dbFileName);
            }

            Directory.CreateDirectory(Path.GetDirectoryName(_dbPath));

            // BusyTimeout : si une autre opération de l'application écrit au même moment,
            // on patiente au lieu de lever immédiatement SQLITE_BUSY.
            _connectionString = ChaineConnexion(_dbPath);

            EnsureSchema();
            _schemaPret = true;
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

            // Garde du profil « Lecture seule » (défaut n° 10) : posée par l'application,
            // une fois le schéma prêt — la mise à niveau doit toujours pouvoir écrire.
            var garde = GardeConnexion;
            if (_schemaPret && garde != null)
                garde(connection);

            connection.Open();
            return connection;
        }

        /// <summary>
        /// Contrôle appliqué à chaque connexion ouverte par <see cref="CreateConnection"/> une
        /// fois le schéma du dossier prêt. L'application WPF y branche la garde du profil
        /// « Lecture seule » (GardeLectureSeule) ; nul dans les outils et les tests.
        /// </summary>
        public static Action<SQLiteConnection> GardeConnexion;

        /// <summary>Vrai une fois la vérification / mise à niveau du schéma terminée.</summary>
        private bool _schemaPret;

        #region Mise à niveau du schéma

        private void EnsureSchema()
        {
            bool baseVierge;
            int version;

            // Premier temps : déterminer l'état de la base, puis refermer la connexion
            // pour pouvoir copier le fichier en toute sécurité si une migration est due.
            // (Ouvrir la base rejoue aussi un éventuel journal laissé par une coupure.)
            using (var connection = CreateConnection())
            {
                CreerTableVersion(connection);
                baseVierge = !TableExiste(connection, "Employees");
                version = LireVersion(connection);
            }

            // Une base déjà mise à niveau par une version PLUS RÉCENTE du programme ne doit
            // pas être utilisée par celle-ci : elle ignorerait les tables qu'elle ne connaît
            // pas et pourrait enregistrer des données incomplètes.
            if (!baseVierge && version > SchemaCible)
                throw new BaseTropRecenteException(_dbPath, version, SchemaCible);

            if (!baseVierge && version == SchemaCible)
                return;

            if (!baseVierge)
                SauvegarderAvantMigration();

            // Tout ou rien : la création ou la mise à niveau se fait sur une COPIE du
            // fichier, contrôlée, puis substituée à l'original en une seule opération.
            // Une erreur, une coupure de courant ou un arrêt brutal en cours de route
            // laissent le fichier du dossier exactement tel qu'il était.
            string copie = _dbPath + SuffixeCopieMigration;
            try
            {
                SupprimerSiPresent(copie);
                SupprimerSiPresent(copie + "-journal");
                File.Copy(_dbPath, copie);

                using (var connection = OuvrirConnexion(copie))
                {
                    if (baseVierge)
                    {
                        CreerSchemaComplet(connection);
                        EnregistrerVersion(connection, SchemaCible);
                    }
                    else
                    {
                        bool saineAvant = EstIntegre(connection);
                        AppliquerMigrations(connection, version);
                        if (saineAvant && !EstIntegre(connection))
                            throw new InvalidOperationException("le contrôle d'intégrité de la base mise à niveau a échoué");
                    }

                    if (LireVersion(connection) != SchemaCible || !TableExiste(connection, "Employees"))
                        throw new InvalidOperationException("la base obtenue n'est pas au niveau attendu (v" + SchemaCible + ")");
                }

                RemplacerParCopie(copie);
            }
            catch (Exception ex)
            {
                SupprimerSiPresent(copie);
                SupprimerSiPresent(copie + "-journal");

                if (baseVierge)
                    throw new InvalidOperationException(
                        "La création de la base du dossier a échoué : " + _dbPath + ". Détail : " + ex.Message, ex);

                string sauvegarde = CheminSauvegardeMigration();
                throw new InvalidOperationException(
                    "La mise à niveau de la base du dossier a échoué : " + _dbPath +
                    ". Le fichier du dossier n'a pas été modifié : la mise à niveau se fait sur une copie, " +
                    "qui ne remplace l'original qu'une fois entièrement réussie et contrôlée" +
                    (File.Exists(sauvegarde) ? " ; une sauvegarde se trouve en outre à côté (" + Path.GetFileName(sauvegarde) + ")" : "") +
                    ". Détail : " + ex.Message, ex);
            }
        }

        /// <summary>Chaîne des migrations, de la version lue jusqu'à <see cref="SchemaCible"/>.</summary>
        private void AppliquerMigrations(SQLiteConnection connection, int version)
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

            if (version < 3)
            {
                Migration3_PlanDePaie(connection);
                EnregistrerVersion(connection, 3);
            }

            if (version < 4)
            {
                Migration4_BaremesDates(connection);
                EnregistrerVersion(connection, 4);
            }

            if (version < 5)
            {
                Migration5_PeriodesPaie(connection);
                EnregistrerVersion(connection, 5);
            }

            if (version < 6)
            {
                Migration6_MatriculeCnss(connection);
                EnregistrerVersion(connection, 6);
            }

            if (version < 7)
            {
                Migration7_VentilationCnss(connection);
                EnregistrerVersion(connection, 7);
            }

            if (version < 8)
            {
                Migration8_DossierSalarie(connection);
                EnregistrerVersion(connection, 8);
            }

            if (version < 9)
            {
                Migration9_VariablesPaie(connection);
                EnregistrerVersion(connection, 9);
            }

            if (version < 10)
            {
                Migration10_ApportVariables(connection);
                EnregistrerVersion(connection, 10);
            }

            if (version < 11)
            {
                Migration11_RubriquesDeSaisie(connection);
                EnregistrerVersion(connection, 11);
            }

            if (version < 12)
            {
                Migration12_AffichageDevise(connection);
                EnregistrerVersion(connection, 12);
            }

            if (version < 13)
            {
                Migration13_Absences(connection);
                EnregistrerVersion(connection, 13);
            }

            if (version < 14)
            {
                Migration14_NetsManquants(connection);
                EnregistrerVersion(connection, 14);
            }

            if (version < 15)
            {
                Migration15_ArchiveBulletin(connection);
                EnregistrerVersion(connection, 15);
            }

            if (version < 16)
            {
                Migration16_Prets(connection);
                EnregistrerVersion(connection, 16);
            }

            if (version < 17)
            {
                Migration17_ProfilsPaie(connection);
                EnregistrerVersion(connection, 17);
            }

            if (version < 18)
            {
                Migration18_TaxePatronale(connection);
                EnregistrerVersion(connection, 18);
            }

            if (version < 19)
            {
                Migration19_AssietteTPA(connection);
                EnregistrerVersion(connection, 19);
            }

            if (version < 20)
            {
                Migration20_JournalAudit(connection);
                EnregistrerVersion(connection, 20);
            }

            if (version < 21)
            {
                Migration21_HeuresSupplementaires(connection);
                EnregistrerVersion(connection, 21);
            }

            if (version < 22)
            {
                Migration22_ConventionsEtGrilles(connection);
                EnregistrerVersion(connection, 22);
            }

            if (version < 23)
            {
                Migration23_SoldeToutCompteEtSortie(connection);
                EnregistrerVersion(connection, 23);
            }

            if (version < 24)
            {
                Migration24_SaisiesOppositionsEtCessions(connection);
                EnregistrerVersion(connection, 24);
            }

            if (version < 25)
            {
                Migration25_RappelsEtRegularisations(connection);
                EnregistrerVersion(connection, 25);
            }

            if (version < 26)
            {
                Migration26_AvantagesEnNature(connection);
                EnregistrerVersion(connection, 26);
            }

            if (version < 27)
            {
                Migration27_RubriqueAvance(connection);
                EnregistrerVersion(connection, 27);
            }

            if (version < 28)
            {
                Migration28_ServiceEtColonnesManquantes(connection);
                EnregistrerVersion(connection, 28);
            }

            if (version < 29)
            {
                Migration29_OrganigrammeEtPostes(connection);
                EnregistrerVersion(connection, 29);
            }

            if (version < 30)
            {
                Migration30_FichesDePostesEtCompetences(connection);
                EnregistrerVersion(connection, 30);
            }

            if (version < 31)
            {
                Migration31_EntretiensEtEvaluations(connection);
                EnregistrerVersion(connection, 31);
            }

            if (version < 32)
            {
                Migration32_FormationEtTPA(connection);
                EnregistrerVersion(connection, 32);
            }

            if (version < 33)
            {
                Migration33_Recrutement(connection);
                EnregistrerVersion(connection, 33);
            }

            if (version < 34)
            {
                Migration34_GestionDesTemps(connection);
                Migration34_RubriquePanier(connection);
                EnregistrerVersion(connection, 34);
            }

            if (version < 35)
            {
                Migration35_Discipline(connection);
                EnregistrerVersion(connection, 35);
            }

            if (version < 36)
            {
                Migration36_PlanDePaieComplet(connection);
                Migration36_MandatsRepresentatifs(connection);
                EnregistrerVersion(connection, 36);
            }

            if (version < 37)
            {
                Migration37_SanteSecurite(connection);
                EnregistrerVersion(connection, 37);
            }

            if (version < 38)
            {
                Migration38_CorrectionsPaie(connection);
                // Migrations v38 des modules RH, des écritures et de l'audit
                Migration38_SanteSecurite(connection);
                Migration38_Entretiens(connection);
                Migration38_Recrutement(connection);
                Migration38_FichesDePostes(connection);
                Migration38_ParametresComptables(connection);
                Migration38_AuditScelle(connection);
                EnregistrerVersion(connection, 38);
            }
        }

        /// <summary>Suffixe de la copie de travail d'une mise à niveau (exclue des sauvegardes).</summary>
        public const string SuffixeCopieMigration = ".migration.tmp";

        private static string ChaineConnexion(string chemin)
        {
            return string.Format(CultureInfo.InvariantCulture, "Data Source={0};Version=3;BusyTimeout=5000;", chemin);
        }

        private static SQLiteConnection OuvrirConnexion(string chemin)
        {
            var connection = new SQLiteConnection(ChaineConnexion(chemin));
            connection.Open();
            return connection;
        }

        private static bool EstIntegre(SQLiteConnection connection)
        {
            using (var cmd = connection.CreateCommand())
            {
                cmd.CommandText = "PRAGMA quick_check;";
                return string.Equals(Convert.ToString(cmd.ExecuteScalar(), CultureInfo.InvariantCulture), "ok", StringComparison.OrdinalIgnoreCase);
            }
        }

        /// <summary>
        /// Substitue la copie mise à niveau au fichier du dossier, en une opération du
        /// système de fichiers. Un antivirus ou une connexion pas encore libérée peuvent
        /// retenir brièvement le fichier : on réessaie quelques instants.
        /// </summary>
        private void RemplacerParCopie(string copie)
        {
            for (int essai = 1; essai <= 20; essai++)
            {
                try
                {
                    File.Replace(copie, _dbPath, null);
                    return;
                }
                catch (IOException) when (essai < 20)
                {
                }
                catch (UnauthorizedAccessException) when (essai < 20)
                {
                }
                GC.Collect();
                GC.WaitForPendingFinalizers();
                System.Threading.Thread.Sleep(200);
            }
            throw new IOException($"Impossible de remplacer le fichier de base de données après 20 tentatives ({_dbPath}). Le fichier est peut-être verrouillé par un autre processus.");
        }

        private static void SupprimerSiPresent(string chemin)
        {
            try
            {
                if (File.Exists(chemin)) File.Delete(chemin);
            }
            catch
            {
                // Un reste de copie de travail est sans conséquence : il est écrasé au prochain essai.
            }
        }

        private string CheminSauvegardeMigration()
        {
            return string.Format(CultureInfo.InvariantCulture, "{0}.avant-v{1}.bak", _dbPath, SchemaCible);
        }

        /// <summary>
        /// Version du schéma d'un fichier de base, lue sans rien modifier ni migrer
        /// (0 : base sans table de version, -1 : fichier illisible).
        /// </summary>
        public static int LireVersionFichier(string chemin)
        {
            try
            {
                using (var connection = new SQLiteConnection(ChaineConnexion(chemin) + "Read Only=True;"))
                {
                    connection.Open();
                    using (var cmd = connection.CreateCommand())
                    {
                        cmd.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'SchemaVersion';";
                        if (DbValue.IntFromDb(cmd.ExecuteScalar()) == 0) return 0;
                        cmd.CommandText = "SELECT COALESCE(MAX(Version), 0) FROM SchemaVersion;";
                        return DbValue.IntFromDb(cmd.ExecuteScalar());
                    }
                }
            }
            catch
            {
                return -1;
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

                string sauvegarde = CheminSauvegardeMigration();

                if (!File.Exists(sauvegarde))
                    File.Copy(_dbPath, sauvegarde);
            }
            catch
            {
                // Une sauvegarde impossible (disque plein, droits) ne doit pas empêcher
                // l'ouverture du dossier : la mise à niveau se fait de toute façon sur une copie.
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
                    Service               TEXT,
                    IsHigherCategory      INTEGER NOT NULL DEFAULT 0,
                    Fonction              TEXT,
                    DateEntree            TEXT,
                    SituationMatrimoniale TEXT,
                    NumeroCnss            TEXT,
                    DateNaissance         TEXT,
                    LieuNaissance         TEXT,
                    Sexe                  TEXT,
                    Nationalite           TEXT,
                    NumeroCnib            TEXT,
                    DateDelivranceCnib    TEXT,
                    LieuDelivranceCnib    TEXT,
                    Adresse               TEXT,
                    Telephone             TEXT,
                    Email                 TEXT,
                    NumeroIfu             TEXT,
                    Banque                TEXT,
                    NumeroCompte          TEXT,
                    ModeReglement         TEXT,
                    ConventionId          INTEGER,
                    Echelon               TEXT,
                    DateSortie            TEXT,
                    MotifDepart           TEXT,
                    StatutSalarie         INTEGER NOT NULL DEFAULT 0,
                    PosteId               INTEGER,
                    ManagerMatricule      TEXT
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
                    AssietteCnss          INTEGER NOT NULL DEFAULT 0,
                    CnssPrestationsFamiliales INTEGER NOT NULL DEFAULT 0,
                    CnssRisquesProfessionnels INTEGER NOT NULL DEFAULT 0,
                    CnssPensionsPatronal  INTEGER NOT NULL DEFAULT 0,
                    RetenuesDiverses      INTEGER NOT NULL DEFAULT 0,
                    GainsVariables        INTEGER NOT NULL DEFAULT 0,
                    AssietteTPA           INTEGER NOT NULL DEFAULT 0,
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

            CreerTableRubriques(connection);
            CreerTablesBaremes(connection);
            CreerTablePeriodes(connection);
            CreerTablePersonnesACharge(connection);
            CreerTableVariablesPaie(connection);

            // Les migrations qui CRÉENT quelque chose sont rejouées ici, sur une base
            // neuve où elles ne tourneront jamais autrement : un dossier créé aujourd'hui
            // est enregistré directement au niveau cible et saute toute la chaîne. Sans
            // ces appels, une base vierge naissait sans la table des absences ni la
            // colonne d'archivage des bulletins — l'écran « Congés et absences » échouait
            // dès l'ouverture d'un dossier neuf, alors qu'il fonctionnait dans les
            // dossiers migrés. Toutes sont idempotentes (CREATE TABLE IF NOT EXISTS,
            // ajout de colonne si absente) : les rejouer ne coûte rien.
            Migration12_AffichageDevise(connection);
            Migration13_Absences(connection);
            Migration15_ArchiveBulletin(connection);
            Migration16_Prets(connection);
            Migration17_ProfilsPaie(connection);
            Migration18_TaxePatronale(connection);
            Migration19_AssietteTPA(connection);
            Migration20_JournalAudit(connection);
            Migration21_HeuresSupplementaires(connection);
            Migration22_ConventionsEtGrilles(connection);
            Migration23_SoldeToutCompteEtSortie(connection);
            Migration24_SaisiesOppositionsEtCessions(connection);
            Migration25_RappelsEtRegularisations(connection);
            Migration26_AvantagesEnNature(connection);
            Migration29_OrganigrammeEtPostes(connection);
            Migration30_FichesDePostesEtCompetences(connection);
            Migration31_EntretiensEtEvaluations(connection);
            Migration32_FormationEtTPA(connection);
            Migration33_Recrutement(connection);
            Migration34_GestionDesTemps(connection);
            // Le plan d'un dossier neuf n'est jamais vide à ce stade (rubriques des
            // migrations 25 et 26) : PANIER y est ajouté de la même façon.
            Migration34_RubriquePanier(connection);
            Migration35_Discipline(connection);
            Migration36_MandatsRepresentatifs(connection);
            Migration37_SanteSecurite(connection);

            // En DERNIER : les migrations 25, 26 et 34 ont déjà déposé quelques rubriques,
            // ce qui empêchait RubriqueService d'amorcer le plan (il ne le fait que sur un
            // plan vide). Le plan initial complet est donc posé ici, sans rien écraser.
            Migration36_PlanDePaieComplet(connection);

            // Puis les corrections v38, qui portent aussi sur le plan qu'on vient de poser.
            Migration38_CorrectionsPaie(connection);
            // Migrations v38 des modules RH, des écritures et de l'audit
            Migration38_SanteSecurite(connection);
            Migration38_Entretiens(connection);
            Migration38_Recrutement(connection);
            Migration38_FichesDePostes(connection);
            Migration38_ParametresComptables(connection);
            Migration38_AuditScelle(connection);
        }

        /// <summary>
        /// Variables du mois (module PAI-02).
        ///
        /// L'index unique sur (Annee, Mois, EmployeeId, CodeRubrique) n'est PAS posé : un
        /// salarié peut légitimement porter deux lignes de la même rubrique — deux primes
        /// distinctes, deux retenues d'origines différentes. C'est l'écran qui signale le
        /// cas comme suspect, pas la base qui l'interdit.
        ///
        /// Conventions : montants en INTEGER de centimes, quantités en TEXT invariant
        /// (une quantité porte des décimales significatives : 7,5 heures).
        /// </summary>
        private void CreerTableVariablesPaie(SQLiteConnection connection)
        {
            Executer(connection, @"
                CREATE TABLE IF NOT EXISTS VariablesPaie (
                    Id           INTEGER PRIMARY KEY AUTOINCREMENT,
                    Annee        INTEGER NOT NULL,
                    Mois         INTEGER NOT NULL,
                    EmployeeId   TEXT NOT NULL,
                    CodeRubrique TEXT NOT NULL,
                    Quantite     TEXT,
                    Montant      INTEGER NOT NULL DEFAULT 0,
                    Commentaire  TEXT,
                    DateSaisie   TEXT
                );");

            Executer(connection,
                "CREATE INDEX IF NOT EXISTS IX_VariablesPaie_Periode ON VariablesPaie(Annee, Mois);");
            Executer(connection,
                "CREATE INDEX IF NOT EXISTS IX_VariablesPaie_Employe ON VariablesPaie(EmployeeId);");
        }

        /// <summary>
        /// Personnes à charge nominatives (module SAL-01).
        ///
        /// La suppression en cascade est voulue : les charges d'un salarié retiré du
        /// dossier n'ont plus d'objet, et les laisser orphelines fausserait tout décompte
        /// ultérieur.
        /// </summary>
        private void CreerTablePersonnesACharge(SQLiteConnection connection)
        {
            Executer(connection, @"
                CREATE TABLE IF NOT EXISTS PersonnesACharge (
                    Id                  INTEGER PRIMARY KEY AUTOINCREMENT,
                    EmployeeId          TEXT NOT NULL,
                    Nom                 TEXT,
                    Prenom              TEXT,
                    Lien                INTEGER NOT NULL DEFAULT 0,
                    DateNaissance       TEXT,
                    PoursuitDesEtudes   INTEGER NOT NULL DEFAULT 0,
                    SituationDeHandicap INTEGER NOT NULL DEFAULT 0,
                    CompteCommeCharge   INTEGER NOT NULL DEFAULT 1,
                    Commentaire         TEXT,
                    FOREIGN KEY(EmployeeId) REFERENCES Employees(EmployeeId) ON DELETE CASCADE
                );");

            Executer(connection,
                "CREATE INDEX IF NOT EXISTS IX_PersonnesACharge_Employe ON PersonnesACharge(EmployeeId);");
        }

        /// <summary>
        /// Périodes de paie (module PAI-01). La contrainte d'unicité sur (Annee, Mois) est
        /// ce qui garantit qu'un mois ne peut pas être ouvert deux fois — sans elle, deux
        /// lignes concurrentes porteraient des totaux différents pour le même mois.
        ///
        /// Les totaux sont des INTEGER en centimes, comme partout ailleurs.
        /// </summary>
        private void CreerTablePeriodes(SQLiteConnection connection)
        {
            Executer(connection, @"
                CREATE TABLE IF NOT EXISTS PeriodesPaie (
                    Id                  INTEGER PRIMARY KEY AUTOINCREMENT,
                    Annee               INTEGER NOT NULL,
                    Mois                INTEGER NOT NULL,
                    Statut              INTEGER NOT NULL DEFAULT 0,
                    DateOuverture       TEXT,
                    DateCloture         TEXT,
                    ClotureePar         TEXT,
                    Commentaire         TEXT,
                    NombreBulletins     INTEGER NOT NULL DEFAULT 0,
                    TotalBrut           INTEGER NOT NULL DEFAULT 0,
                    TotalNetAPayer      INTEGER NOT NULL DEFAULT 0,
                    TotalIuts           INTEGER NOT NULL DEFAULT 0,
                    TotalCnssSalarie    INTEGER NOT NULL DEFAULT 0,
                    TotalCnssPatronal   INTEGER NOT NULL DEFAULT 0,
                    TotalTaxePatronale  INTEGER NOT NULL DEFAULT 0,
                    TotalMasseSalariale INTEGER NOT NULL DEFAULT 0
                );");

            Executer(connection,
                "CREATE UNIQUE INDEX IF NOT EXISTS IX_PeriodesPaie_AnneeMois ON PeriodesPaie(Annee, Mois);");
        }

        /// <summary>
        /// Migration 5 — périodes de paie et clôture.
        ///
        /// Ajoute la table aux dossiers existants. Aucune période n'est créée : les mois
        /// déjà calculés restent modifiables tant que l'utilisateur ne les ouvre puis ne
        /// les clôture pas explicitement. Le comportement de l'application ne change donc
        /// pas tant qu'elle ne s'est pas servie du module.
        /// </summary>
        private void Migration5_PeriodesPaie(SQLiteConnection connection)
        {
            CreerTablePeriodes(connection);
        }

        /// <summary>
        /// Migration 6 — matricule CNSS du salarié.
        ///
        /// La déclaration CNSS (module DEC-04) identifie chaque assuré par son matricule,
        /// pas par son nom. Sans cette colonne, la déclaration ne peut pas être produite.
        /// La colonne est ajoutée vide : les matricules se saisissent ensuite sur la fiche
        /// du salarié, et l'écran de déclaration signale les lignes encore incomplètes.
        /// </summary>
        private void Migration6_MatriculeCnss(SQLiteConnection connection)
        {
            AjouterColonneSiAbsente(connection, "Employees", "NumeroCnss", "TEXT");
        }

        /// <summary>
        /// Migration 7 — ventilation des cotisations CNSS par branche (module DEC-01).
        ///
        /// Quatre colonnes : l'assiette réellement cotisée (le brut ramené au plafond) et
        /// les trois branches patronales. Elles étaient jusqu'ici implicites — seul le
        /// total patronal était conservé — ce qui obligeait le bordereau CNSS à
        /// reconstituer la ventilation au lieu de la lire.
        ///
        /// Les calculs déjà enregistrés gardent des colonnes à zéro : on ne reconstitue
        /// PAS une ventilation qui n'a jamais été calculée, car il faudrait pour cela
        /// deviner quel plafond et quels taux étaient en vigueur à l'époque. Le bordereau
        /// sait déjà déduire l'assiette du brut ; pour ces lignes-là, il continue.
        /// </summary>
        private void Migration7_VentilationCnss(SQLiteConnection connection)
        {
            AjouterColonneSiAbsente(connection, "TaxCalculations", "AssietteCnss", "INTEGER NOT NULL DEFAULT 0");
            AjouterColonneSiAbsente(connection, "TaxCalculations", "CnssPrestationsFamiliales", "INTEGER NOT NULL DEFAULT 0");
            AjouterColonneSiAbsente(connection, "TaxCalculations", "CnssRisquesProfessionnels", "INTEGER NOT NULL DEFAULT 0");
            AjouterColonneSiAbsente(connection, "TaxCalculations", "CnssPensionsPatronal", "INTEGER NOT NULL DEFAULT 0");
        }

        /// <summary>
        /// Migration 8 — dossier salarié (module SAL-01).
        ///
        /// Quatorze colonnes d'état civil, de coordonnées et de règlement sur la fiche du
        /// salarié, plus la table des personnes à charge nominatives.
        ///
        /// Aucune donnée existante n'est transformée. En particulier, le champ
        /// <c>FamilyCharges</c> — celui qui commande la réduction d'IUTS — n'est PAS
        /// recalculé depuis la nouvelle table : celle-ci est vide au départ, et déduire
        /// un décompte de rien mettrait toutes les charges à zéro. Les deux coexistent,
        /// et l'écran signale l'écart quand il apparaît.
        /// </summary>
        private void Migration8_DossierSalarie(SQLiteConnection connection)
        {
            foreach (var colonne in new[]
            {
                "DateNaissance", "LieuNaissance", "Sexe", "Nationalite",
                "NumeroCnib", "DateDelivranceCnib", "LieuDelivranceCnib",
                "Adresse", "Telephone", "Email", "NumeroIfu",
                "Banque", "NumeroCompte", "ModeReglement"
            })
            {
                AjouterColonneSiAbsente(connection, "Employees", colonne, "TEXT");
            }

            CreerTablePersonnesACharge(connection);
        }

        /// <summary>
        /// Migration 9 — variables du mois (module PAI-02).
        ///
        /// Ajoute la table aux dossiers existants. Rien n'est repris des bulletins déjà
        /// calculés : une prime passée n'était pas une variable, elle était noyée dans le
        /// salaire de base ou dans une indemnité, et la retrouver après coup relèverait de
        /// la divination.
        /// </summary>
        private void Migration9_VariablesPaie(SQLiteConnection connection)
        {
            CreerTableVariablesPaie(connection);
        }

        /// <summary>
        /// Migration 10 — apport des variables sur le bulletin.
        ///
        /// Deux colonnes : les retenues diverses, qui se déduisent du net après impôt et
        /// cotisations, et le total des gains variables, conservé pour que le bulletin
        /// distingue le salaire permanent de ce que le mois a ajouté.
        ///
        /// Les calculs déjà enregistrés gardent zéro : ils ont été établis avant que les
        /// variables n'existent, et leur net à payer reste donc exact.
        /// </summary>
        private void Migration10_ApportVariables(SQLiteConnection connection)
        {
            AjouterColonneSiAbsente(connection, "TaxCalculations", "RetenuesDiverses", "INTEGER NOT NULL DEFAULT 0");
            AjouterColonneSiAbsente(connection, "TaxCalculations", "GainsVariables", "INTEGER NOT NULL DEFAULT 0");
        }

        /// <summary>
        /// Migration 11 — rubriques de saisie mensuelle (module PAI-02).
        ///
        /// Le plan de paie livré ne contenait que de la rémunération PERMANENTE : salaire
        /// de base et quatre indemnités, qui se saisissent sur la fiche du salarié. L'écran
        /// « Variables du mois » n'avait donc rien de pertinent à proposer.
        ///
        /// Ces douze rubriques couvrent les trois traitements fiscaux que le moteur sait
        /// distinguer — soumis aux deux, soumis à l'IUTS seul, soumis à aucun — plus les
        /// retenues sur le net.
        ///
        /// <c>INSERT OR IGNORE</c> : un dossier qui porte déjà l'un de ces codes le garde
        /// tel qu'il l'a. Et comme la migration ne s'exécute qu'une fois, une rubrique que
        /// l'utilisateur supprime ensuite ne réapparaît pas à la prochaine ouverture.
        ///
        /// ATTENTION : cette liste double celle de
        /// <c>RubriqueService.RubriquesDeSaisie()</c>, qui sert aux dossiers NEUFS.
        /// Toute modification doit être reportée dans les deux.
        /// </summary>
        /// <summary>
        /// Migration 12 — affichage de la devise sur les documents de paie.
        ///
        /// Un seul réglage, par dossier : faut-il écrire « FCFA » derrière chaque montant
        /// des bulletins ? La valeur par défaut 1 reproduit le comportement antérieur, si
        /// bien qu'un dossier déjà en service ne voit aucun changement tant que personne
        /// ne décoche la case.
        /// </summary>
        /// <summary>
        /// Migration 13 — congés et absences (PAI-04).
        ///
        /// Une absence est datée de bout en bout, et non rattachée à un mois : un congé
        /// du 28 mars au 5 avril concerne deux périodes de paie, et le stocker sous un
        /// seul mois le ferait disparaître de l'autre bulletin.
        ///
        /// Deux réglages accompagnent la table : le droit mensuel à congé (2,5 jours
        /// ouvrables par défaut, conformément au Code du travail, mais une convention
        /// collective peut être plus favorable) et la base de retenue d'une absence non
        /// rémunérée, que la loi ne prescrit pas et que chaque entreprise arrête.
        /// </summary>
        /// <summary>
        /// Migration 14 — nets à payer restés à zéro.
        ///
        /// Le net après impôt et le net à payer sont des colonnes distinctes des
        /// propriétés calculées du modèle, et personne ne les alimentait : tous les
        /// bulletins enregistrés jusqu'ici portent un net nul. Les totaux de période, le
        /// livre de paie et les futurs ordres de virement en héritaient.
        ///
        /// Les deux valeurs se reconstituent exactement à partir des colonnes déjà
        /// présentes, avec la formule du modèle :
        ///     net après impôt = brut − IUTS
        ///     net à payer     = net après impôt − CNSS salariale − retenue obligatoire
        ///                       − retenues diverses
        ///
        /// Seules les lignes à zéro sont touchées : un net déjà enregistré fait foi et
        /// n'est pas recalculé.
        /// </summary>
        /// <summary>
        /// Migration 15 — archivage du bulletin édité.
        ///
        /// Le calcul enregistré porte les totaux, pas le document : ni le détail des
        /// indemnités du moment, ni les lignes de variables, ni le numéro de contrôle.
        /// Rééditer un bulletin de septembre en novembre le recalculait donc depuis la
        /// fiche du salarié telle qu'elle est en novembre — augmentation comprise.
        ///
        /// Un bulletin de paie est un instantané opposable au salarié : il doit se
        /// réafficher des années plus tard exactement comme il a été remis. Le document
        /// est donc archivé en clair à côté de son calcul.
        /// </summary>
        private void Migration15_ArchiveBulletin(SQLiteConnection connection)
        {
            if (!TableExiste(connection, "TaxCalculations"))
                return;

            AjouterColonneSiAbsente(connection, "TaxCalculations", "BulletinArchive", "TEXT");
        }

        /// <summary>
        /// Migration 16 — prêts, avances et acomptes (module PAI-05).
        ///
        /// Deux tables : l'accord, et son échéancier. Les séparer n'est pas une élégance
        /// — c'est ce qui permet de suspendre un remboursement, de le reprendre trois
        /// mois plus tard et de garder la trace de ce décalage. Un prêt qui ne porterait
        /// qu'un montant et une durée obligerait à recalculer à chaque lecture un
        /// calendrier que les évènements du dossier ont déjà démenti.
        /// </summary>
        private void Migration16_Prets(SQLiteConnection connection)
        {
            CreerTablesPrets(connection);
        }

        /// <summary>
        /// Migration 17 — profils de paie (module SOC-03).
        ///
        /// Trois tables : le profil, ses rubriques, et le rattachement des salariés. Le
        /// rattachement est une table à part et non une colonne de <c>Employees</c>, parce
        /// qu'il est DATÉ et conservé : un salarié promu cadre en avril garde son profil
        /// d'employé sur ses bulletins de janvier à mars. Une colonne n'aurait retenu que
        /// la situation du jour, et aurait fait mentir tout recalcul antérieur.
        /// </summary>
        private void Migration17_ProfilsPaie(SQLiteConnection connection)
        {
            CreerTablesProfils(connection);
        }

        /// <summary>
        /// Profils de paie (module SOC-03).
        ///
        /// Conventions : montants en INTEGER de centimes, quantités en TEXT invariant. Les
        /// rubriques d'un profil sont supprimées en cascade avec lui ; les affectations ne
        /// le sont pas, et le service refuse d'ailleurs de supprimer un profil qui a été
        /// affecté — c'est la pièce qui explique un bulletin passé.
        /// </summary>
        private void CreerTablesProfils(SQLiteConnection connection)
        {
            Executer(connection, @"
                CREATE TABLE IF NOT EXISTS ProfilsPaie (
                    Id          INTEGER PRIMARY KEY AUTOINCREMENT,
                    Code        TEXT    NOT NULL,
                    Libelle     TEXT,
                    Population  INTEGER NOT NULL DEFAULT 2,
                    Description TEXT,
                    Actif       INTEGER NOT NULL DEFAULT 1,
                    Ordre       INTEGER NOT NULL DEFAULT 0,
                    DateSaisie  TEXT    NOT NULL
                );");

            Executer(connection,
                "CREATE UNIQUE INDEX IF NOT EXISTS IX_ProfilsPaie_Code ON ProfilsPaie (Code);");

            Executer(connection, @"
                CREATE TABLE IF NOT EXISTS LignesProfil (
                    Id           INTEGER PRIMARY KEY AUTOINCREMENT,
                    ProfilId     INTEGER NOT NULL,
                    CodeRubrique TEXT    NOT NULL,
                    Montant      INTEGER NOT NULL DEFAULT 0,
                    Quantite     TEXT,
                    Ordre        INTEGER NOT NULL DEFAULT 0,
                    Active       INTEGER NOT NULL DEFAULT 1,
                    Commentaire  TEXT,
                    FOREIGN KEY(ProfilId) REFERENCES ProfilsPaie(Id) ON DELETE CASCADE
                );");

            Executer(connection,
                "CREATE INDEX IF NOT EXISTS IX_LignesProfil_Profil ON LignesProfil (ProfilId);");

            Executer(connection, @"
                CREATE TABLE IF NOT EXISTS AffectationsProfil (
                    Id          INTEGER PRIMARY KEY AUTOINCREMENT,
                    EmployeeId  TEXT    NOT NULL,
                    ProfilId    INTEGER NOT NULL DEFAULT 0,
                    DateEffet   TEXT    NOT NULL,
                    Commentaire TEXT
                );");

            Executer(connection,
                "CREATE INDEX IF NOT EXISTS IX_AffectationsProfil_Salarie " +
                "ON AffectationsProfil (EmployeeId, DateEffet);");
        }

        /// <summary>
        /// Prêts, avances et acomptes (module PAI-05).
        ///
        /// Conventions : montants en INTEGER de centimes, comme partout ailleurs. La
        /// période d'une échéance est stockée en (Annee, Mois) et non en date : une
        /// échéance appartient à un MOIS DE PAIE, pas à un jour, et c'est sur ce couple
        /// que se fait la jointure avec les périodes (PAI-01).
        ///
        /// L'échéancier est supprimé en cascade avec son prêt : un calendrier sans accord
        /// ne se rattache à rien et fausserait tout décompte de solde.
        /// </summary>
        private void CreerTablesPrets(SQLiteConnection connection)
        {
            Executer(connection, @"
                CREATE TABLE IF NOT EXISTS Prets (
                    Id                    INTEGER PRIMARY KEY AUTOINCREMENT,
                    EmployeeId            TEXT    NOT NULL,
                    Type                  INTEGER NOT NULL DEFAULT 0,
                    Reference             TEXT,
                    DateOctroi            TEXT    NOT NULL,
                    MontantAccorde        INTEGER NOT NULL DEFAULT 0,
                    MontantEcheance       INTEGER NOT NULL DEFAULT 0,
                    NombreEcheances       INTEGER NOT NULL DEFAULT 1,
                    AnneePremiereEcheance INTEGER NOT NULL,
                    MoisPremiereEcheance  INTEGER NOT NULL,
                    Statut                INTEGER NOT NULL DEFAULT 0,
                    Motif                 TEXT,
                    Commentaire           TEXT,
                    DateSaisie            TEXT    NOT NULL
                );");

            Executer(connection,
                "CREATE INDEX IF NOT EXISTS IX_Prets_Salarie ON Prets (EmployeeId);");

            Executer(connection, @"
                CREATE TABLE IF NOT EXISTS EcheancesPret (
                    Id           INTEGER PRIMARY KEY AUTOINCREMENT,
                    PretId       INTEGER NOT NULL,
                    Rang         INTEGER NOT NULL DEFAULT 1,
                    Annee        INTEGER NOT NULL,
                    Mois         INTEGER NOT NULL,
                    MontantPrevu INTEGER NOT NULL DEFAULT 0,
                    Statut       INTEGER NOT NULL DEFAULT 0,
                    Commentaire  TEXT,
                    FOREIGN KEY(PretId) REFERENCES Prets(Id) ON DELETE CASCADE
                );");

            Executer(connection,
                "CREATE INDEX IF NOT EXISTS IX_EcheancesPret_Pret ON EcheancesPret (PretId);");
            Executer(connection,
                "CREATE INDEX IF NOT EXISTS IX_EcheancesPret_Periode ON EcheancesPret (Annee, Mois);");
        }

        private void Migration14_NetsManquants(SQLiteConnection connection)
        {
            if (!TableExiste(connection, "TaxCalculations"))
                return;

            Executer(connection, @"
                UPDATE TaxCalculations
                   SET NetSalaryAfterTax = COALESCE(SalaireBrut,0) - COALESCE(IUTSNet,0)
                 WHERE COALESCE(NetSalaryAfterTax,0) = 0
                   AND COALESCE(SalaireBrut,0) <> 0;");

            Executer(connection, @"
                UPDATE TaxCalculations
                   SET SalaireNetaPayer = COALESCE(SalaireBrut,0)
                                        - COALESCE(IUTSNet,0)
                                        - COALESCE(CnssEmployee,0)
                                        - COALESCE(RetenueSpeciale,0)
                                        - COALESCE(RetenuesDiverses,0)
                 WHERE COALESCE(SalaireNetaPayer,0) = 0
                   AND COALESCE(SalaireBrut,0) <> 0;");

            // Les totaux déjà figés des périodes OUVERTES sont remis à zéro : ils seront
            // recalculés à la prochaine lecture, depuis les bulletins corrigés ci-dessus.
            // Les périodes clôturées ne sont pas touchées — un arrêté ne se réécrit pas
            // sans décision explicite.
            if (TableExiste(connection, "PeriodesPaie"))
            {
                Executer(connection, @"
                    UPDATE PeriodesPaie
                       SET TotalNetAPayer = 0
                     WHERE Statut = 0;");
            }
        }

        private void Migration13_Absences(SQLiteConnection connection)
        {
            Executer(connection, @"
                CREATE TABLE IF NOT EXISTS Absences (
                    Id            INTEGER PRIMARY KEY AUTOINCREMENT,
                    EmployeeId    TEXT    NOT NULL,
                    Type          INTEGER NOT NULL DEFAULT 0,
                    DateDebut     TEXT    NOT NULL,
                    DateFin       TEXT    NOT NULL,
                    NombreJours   TEXT    NOT NULL DEFAULT '0',
                    NombreHeures  TEXT    NOT NULL DEFAULT '0',
                    Commentaire   TEXT,
                    DateSaisie    TEXT    NOT NULL
                );");

            Executer(connection,
                "CREATE INDEX IF NOT EXISTS IX_Absences_Salarie ON Absences (EmployeeId);");
            Executer(connection,
                "CREATE INDEX IF NOT EXISTS IX_Absences_Periode ON Absences (DateDebut, DateFin);");

            if (TableExiste(connection, "AppSettings"))
            {
                AjouterColonneSiAbsente(connection, "AppSettings", "JoursCongesParMois", "TEXT NOT NULL DEFAULT '2.5'");
                AjouterColonneSiAbsente(connection, "AppSettings", "BaseRetenueAbsence", "INTEGER NOT NULL DEFAULT 0");
            }
        }

        private void Migration12_AffichageDevise(SQLiteConnection connection)
        {
            if (!TableExiste(connection, "AppSettings"))
                return;

            AjouterColonneSiAbsente(connection, "AppSettings", "AfficherDevise", "INTEGER NOT NULL DEFAULT 1");
        }

        /// <summary>
        /// Migration 18 — taxe patronale et d'apprentissage (module DEC-02).
        ///
        /// Ajoute la colonne qui permet d'exonérer une rubrique de la TPA, comme
        /// <c>SoumisIUTS</c> et <c>SoumisCNSS</c> le font déjà pour l'impôt et les
        /// cotisations. Le défaut à 1 (soumis) laisse inchangée l'assiette de tout
        /// dossier existant : la TPA portait jusqu'ici sur l'intégralité du salaire
        /// brut, exactement ce que reproduit une colonne où toute rubrique reste
        /// soumise tant que personne ne l'en exonère explicitement.
        /// </summary>
        private void Migration18_TaxePatronale(SQLiteConnection connection)
        {
            AjouterColonneSiAbsente(connection, "Rubriques", "SoumisTPA", "INTEGER NOT NULL DEFAULT 1");
        }

        /// <summary>
        /// Migration 19 — assiette TPA (module DEC-03).
        ///
        /// Jusqu'ici, seul le MONTANT de la taxe patronale et d'apprentissage
        /// (<c>TaxePatronale</c>) était enregistré ; l'assiette qui l'a produite ne
        /// l'était pas. La déclaration IUTS/TPA a besoin de cette assiette telle qu'elle
        /// a réellement servi au calcul — pas reconstituée après coup à partir du brut,
        /// ce qui ignorerait les exonérations DEC-02 propres à chaque dossier.
        ///
        /// Les calculs déjà enregistrés gardent une colonne à zéro : on ne devine pas
        /// une assiette qui n'a jamais été conservée. Pour ces lignes-là, la déclaration
        /// la reconstitue depuis le brut, comme elle le fait déjà pour l'assiette CNSS
        /// des bulletins antérieurs à la migration 7.
        /// </summary>
        private void Migration19_AssietteTPA(SQLiteConnection connection)
        {
            AjouterColonneSiAbsente(connection, "TaxCalculations", "AssietteTPA", "INTEGER NOT NULL DEFAULT 0");
        }

        /// <summary>
        /// Migration 20 — journal d'audit et traçabilité des opérations (module ADM-02).
        ///
        /// Crée la table JournalAudit et ses index pour tracer les modifications sensibles
        /// de paie : qui a changé quel salaire, quand, et quelle était la valeur précédente.
        /// </summary>
        private void Migration20_JournalAudit(SQLiteConnection connection)
        {
            Executer(connection, @"
                CREATE TABLE IF NOT EXISTS JournalAudit (
                    Id              INTEGER PRIMARY KEY AUTOINCREMENT,
                    DateAction      TEXT    NOT NULL,
                    Utilisateur     TEXT    NOT NULL,
                    Action          TEXT    NOT NULL,
                    Entite          TEXT    NOT NULL,
                    EntiteId        TEXT,
                    ChampModifie    TEXT,
                    AncienneValeur  TEXT,
                    NouvelleValeur  TEXT,
                    Details         TEXT,
                    AdresseMachine  TEXT
                );");

            Executer(connection,
                "CREATE INDEX IF NOT EXISTS IX_JournalAudit_Date ON JournalAudit (DateAction);");
            Executer(connection,
                "CREATE INDEX IF NOT EXISTS IX_JournalAudit_Entite ON JournalAudit (Entite, EntiteId);");
            Executer(connection,
                "CREATE INDEX IF NOT EXISTS IX_JournalAudit_Utilisateur ON JournalAudit (Utilisateur);");
        }

        /// <summary>
        /// Migration 21 — Heures supplémentaires et majorations légales (module PAI-03).
        ///
        /// Crée la table HeuresSupplementaires et ses index pour décompter et valoriser
        /// les heures supplémentaires mensuelles par tranche de majoration (115%, 130%, 150%, 200%).
        /// </summary>
        private void Migration21_HeuresSupplementaires(SQLiteConnection connection)
        {
            Executer(connection, @"
                CREATE TABLE IF NOT EXISTS HeuresSupplementaires (
                    Id                INTEGER PRIMARY KEY AUTOINCREMENT,
                    EmployeeId        TEXT    NOT NULL,
                    Annee             INTEGER NOT NULL,
                    Mois              INTEGER NOT NULL,
                    Heures15          TEXT    NOT NULL DEFAULT '0',
                    Heures30          TEXT    NOT NULL DEFAULT '0',
                    Heures50          TEXT    NOT NULL DEFAULT '0',
                    Heures100         TEXT    NOT NULL DEFAULT '0',
                    TauxHoraireBase   INTEGER NOT NULL DEFAULT 0,
                    Montant15         INTEGER NOT NULL DEFAULT 0,
                    Montant30         INTEGER NOT NULL DEFAULT 0,
                    Montant50         INTEGER NOT NULL DEFAULT 0,
                    Montant100        INTEGER NOT NULL DEFAULT 0,
                    MontantTotal      INTEGER NOT NULL DEFAULT 0,
                    Commentaire       TEXT,
                    DateCreation      TEXT    NOT NULL,
                    DateModification  TEXT    NOT NULL,
                    FOREIGN KEY(EmployeeId) REFERENCES Employees(EmployeeId)
                );");

            Executer(connection,
                "CREATE UNIQUE INDEX IF NOT EXISTS UX_HeuresSupplementaires_Employe_Periode " +
                "ON HeuresSupplementaires (EmployeeId, Annee, Mois);");
            Executer(connection,
                "CREATE INDEX IF NOT EXISTS IX_HeuresSupplementaires_Periode " +
                "ON HeuresSupplementaires (Annee, Mois);");
        }

        /// <summary>
        /// Migration 22 — Grilles de salaires et conventions collectives (module SOC-05).
        ///
        /// Crée les tables ConventionsCollectives, GrillesSalaires et TranchesAnciennete.
        /// Ajoute ConventionId et Echelon à la table Employees.
        /// Initialise la Convention Collective Interprofessionnelle du Burkina Faso (CCI-BF)
        /// avec sa grille de salaires minima et son barème légal de prime d'ancienneté.
        /// </summary>
        private void Migration22_ConventionsEtGrilles(SQLiteConnection connection)
        {
            Executer(connection, @"
                CREATE TABLE IF NOT EXISTS ConventionsCollectives (
                    Id                   INTEGER PRIMARY KEY AUTOINCREMENT,
                    Code                 TEXT    NOT NULL UNIQUE,
                    Libelle              TEXT    NOT NULL,
                    Description          TEXT,
                    BaseCalculAnciennete INTEGER NOT NULL DEFAULT 0,
                    EstActive            INTEGER NOT NULL DEFAULT 1,
                    DateCreation         TEXT    NOT NULL,
                    DateModification     TEXT    NOT NULL
                );");

            Executer(connection, @"
                CREATE TABLE IF NOT EXISTS GrillesSalaires (
                    Id              INTEGER PRIMARY KEY AUTOINCREMENT,
                    ConventionId    INTEGER NOT NULL,
                    Categorie       TEXT    NOT NULL,
                    Echelon         TEXT    NOT NULL,
                    SalaireMinimum  INTEGER NOT NULL DEFAULT 0,
                    Ordre           INTEGER NOT NULL DEFAULT 0,
                    Description     TEXT,
                    FOREIGN KEY(ConventionId) REFERENCES ConventionsCollectives(Id) ON DELETE CASCADE
                );");

            Executer(connection,
                "CREATE UNIQUE INDEX IF NOT EXISTS UX_GrillesSalaires_Conv_Cat_Ech " +
                "ON GrillesSalaires (ConventionId, Categorie, Echelon);");
            Executer(connection,
                "CREATE INDEX IF NOT EXISTS IX_GrillesSalaires_Convention " +
                "ON GrillesSalaires (ConventionId);");

            Executer(connection, @"
                CREATE TABLE IF NOT EXISTS TranchesAnciennete (
                    Id              INTEGER PRIMARY KEY AUTOINCREMENT,
                    ConventionId    INTEGER NOT NULL,
                    AnneesMin       INTEGER NOT NULL,
                    AnneesMax       INTEGER,
                    TauxPourcentage TEXT    NOT NULL DEFAULT '0',
                    Ordre           INTEGER NOT NULL DEFAULT 0,
                    FOREIGN KEY(ConventionId) REFERENCES ConventionsCollectives(Id) ON DELETE CASCADE
                );");

            Executer(connection,
                "CREATE INDEX IF NOT EXISTS IX_TranchesAnciennete_Conv " +
                "ON TranchesAnciennete (ConventionId, AnneesMin);");

            AjouterColonneSiAbsente(connection, "Employees", "ConventionId", "INTEGER");
            AjouterColonneSiAbsente(connection, "Employees", "Echelon", "TEXT");

            // Initialiser les données de référence CCI-BF si aucune convention n'existe
            InitialiserConventionCciBfSiAbsente(connection);
        }

        private void InitialiserConventionCciBfSiAbsente(SQLiteConnection connection)
        {
            long convId = 0;
            using (var cmd = connection.CreateCommand())
            {
                cmd.CommandText = "SELECT Id FROM ConventionsCollectives WHERE Code = 'CCI-BF';";
                var res = cmd.ExecuteScalar();
                if (res != null && res != DBNull.Value)
                {
                    convId = Convert.ToInt64(res);
                }
            }

            string now = DbValue.DateToDb(DateTime.Now);

            if (convId == 0)
            {
                using (var cmd = connection.CreateCommand())
                {
                    cmd.CommandText = @"
                        INSERT INTO ConventionsCollectives (
                            Code, Libelle, Description, BaseCalculAnciennete, EstActive, DateCreation, DateModification
                        ) VALUES (
                            'CCI-BF',
                            'Convention Collective Interprofessionnelle du Burkina Faso',
                            'Convention collective interprofessionnelle régissant les rapports de travail entre employeurs et travailleurs du Burkina Faso.',
                            0, 1, @now, @now
                        );";
                    cmd.Parameters.AddWithValue("@now", now);
                    cmd.ExecuteNonQuery();
                    convId = connection.LastInsertRowId;
                }
            }

            // Grille des salaires minima (catégories 1 à 9 et échelons A, B, C)
            // Les montants sont exprimés en centimes : 34 664 FCFA = 3 466 400 centimes.
            var categories = new[]
            {
                new { Cat = "1ère Catégorie", Ech = "A", SalMin = 34664m, Ordre = 10 },
                new { Cat = "1ère Catégorie", Ech = "B", SalMin = 35500m, Ordre = 11 },
                new { Cat = "1ère Catégorie", Ech = "C", SalMin = 36500m, Ordre = 12 },

                new { Cat = "2ème Catégorie", Ech = "A", SalMin = 37500m, Ordre = 20 },
                new { Cat = "2ème Catégorie", Ech = "B", SalMin = 39000m, Ordre = 21 },
                new { Cat = "2ème Catégorie", Ech = "C", SalMin = 40500m, Ordre = 22 },

                new { Cat = "3ème Catégorie", Ech = "A", SalMin = 42000m, Ordre = 30 },
                new { Cat = "3ème Catégorie", Ech = "B", SalMin = 44000m, Ordre = 31 },
                new { Cat = "3ème Catégorie", Ech = "C", SalMin = 46500m, Ordre = 32 },

                new { Cat = "4ème Catégorie", Ech = "A", SalMin = 48500m, Ordre = 40 },
                new { Cat = "4ème Catégorie", Ech = "B", SalMin = 51000m, Ordre = 41 },
                new { Cat = "4ème Catégorie", Ech = "C", SalMin = 54000m, Ordre = 42 },

                new { Cat = "5ème Catégorie", Ech = "A", SalMin = 57000m, Ordre = 50 },
                new { Cat = "5ème Catégorie", Ech = "B", SalMin = 61000m, Ordre = 51 },
                new { Cat = "5ème Catégorie", Ech = "C", SalMin = 65500m, Ordre = 52 },

                new { Cat = "6ème Catégorie", Ech = "A", SalMin = 71000m, Ordre = 60 },
                new { Cat = "6ème Catégorie", Ech = "B", SalMin = 76500m, Ordre = 61 },
                new { Cat = "6ème Catégorie", Ech = "C", SalMin = 82500m, Ordre = 62 },

                new { Cat = "Agents de Maîtrise", Ech = "A", SalMin = 90000m, Ordre = 70 },
                new { Cat = "Agents de Maîtrise", Ech = "B", SalMin = 98000m, Ordre = 71 },
                new { Cat = "Agents de Maîtrise", Ech = "C", SalMin = 106000m, Ordre = 72 },

                new { Cat = "Cadres Moyens", Ech = "A", SalMin = 120000m, Ordre = 80 },
                new { Cat = "Cadres Moyens", Ech = "B", SalMin = 132000m, Ordre = 81 },
                new { Cat = "Cadres Moyens", Ech = "C", SalMin = 145000m, Ordre = 82 },

                new { Cat = "Cadres Supérieurs", Ech = "A", SalMin = 165000m, Ordre = 90 },
                new { Cat = "Cadres Supérieurs", Ech = "B", SalMin = 185000m, Ordre = 91 },
                new { Cat = "Cadres Supérieurs", Ech = "C", SalMin = 210000m, Ordre = 92 }
            };

            foreach (var item in categories)
            {
                using (var cmd = connection.CreateCommand())
                {
                    cmd.CommandText = @"
                        INSERT OR IGNORE INTO GrillesSalaires (
                            ConventionId, Categorie, Echelon, SalaireMinimum, Ordre, Description
                        ) VALUES (
                            @conv, @cat, @ech, @sal, @ord, ''
                        );";
                    cmd.Parameters.AddWithValue("@conv", convId);
                    cmd.Parameters.AddWithValue("@cat", item.Cat);
                    cmd.Parameters.AddWithValue("@ech", item.Ech);
                    cmd.Parameters.AddWithValue("@sal", DbValue.MoneyToDb(item.SalMin));
                    cmd.Parameters.AddWithValue("@ord", item.Ordre);
                    cmd.ExecuteNonQuery();
                }
            }

            // Tranches d'ancienneté CCI-BF (Art. 37) :
            // 2 à 3 ans : 5%
            // 3 à 4 ans : 6%
            // ... jusqu'à 25 ans et plus (28%)
            for (int annee = 2; annee <= 25; annee++)
            {
                decimal taux = 5m + (annee - 2);
                int min = annee;
                int? max = annee == 25 ? (int?)null : annee + 1;

                using (var cmd = connection.CreateCommand())
                {
                    cmd.CommandText = @"
                        INSERT OR IGNORE INTO TranchesAnciennete (
                            ConventionId, AnneesMin, AnneesMax, TauxPourcentage, Ordre
                        ) VALUES (
                            @conv, @min, @max, @taux, @ord
                        );";
                    cmd.Parameters.AddWithValue("@conv", convId);
                    cmd.Parameters.AddWithValue("@min", min);
                    cmd.Parameters.AddWithValue("@max", max.HasValue ? (object)max.Value : DBNull.Value);
                    cmd.Parameters.AddWithValue("@taux", DbValue.DecimalToDb(taux));
                    cmd.Parameters.AddWithValue("@ord", annee);
                    cmd.ExecuteNonQuery();
                }
            }
        }

        /// <summary>
        /// Migration 23 — Solde de tout compte et gestion des départs de salariés (module SAL-05).
        ///
        /// Ajoute les champs de départ et statut dans la table Employees :
        /// - DateSortie (TEXT)
        /// - MotifDepart (TEXT)
        /// - StatutSalarie (INTEGER, 0 = Actif, 1 = Sorti/Inactif, 2 = Suspendu)
        ///
        /// Crée la table SoldesToutCompte pour conserver l'historique opposable de chaque
        /// solde de tout compte calculé, validé et remis au salarié.
        /// </summary>
        private void Migration23_SoldeToutCompteEtSortie(SQLiteConnection connection)
        {
            AjouterColonneSiAbsente(connection, "Employees", "DateSortie", "TEXT");
            AjouterColonneSiAbsente(connection, "Employees", "MotifDepart", "TEXT");
            AjouterColonneSiAbsente(connection, "Employees", "StatutSalarie", "INTEGER NOT NULL DEFAULT 0");

            Executer(connection, @"
                CREATE TABLE IF NOT EXISTS SoldesToutCompte (
                    Id                          INTEGER PRIMARY KEY AUTOINCREMENT,
                    EmployeeId                  TEXT    NOT NULL,
                    NomSalarie                  TEXT,
                    DateSortie                  TEXT    NOT NULL,
                    Motif                       INTEGER NOT NULL DEFAULT 0,
                    DateEntree                  TEXT    NOT NULL,
                    AncienneteAnnees            INTEGER NOT NULL DEFAULT 0,
                    AncienneteMois              INTEGER NOT NULL DEFAULT 0,
                    Categorie                   TEXT,
                    Echelon                     TEXT,
                    Fonction                    TEXT,
                    SalaireReference            INTEGER NOT NULL DEFAULT 0,
                    SalairePresence             INTEGER NOT NULL DEFAULT 0,
                    JoursPresence               TEXT    NOT NULL DEFAULT '0',
                    JoursCongesDus              TEXT    NOT NULL DEFAULT '0',
                    IndemniteConges             INTEGER NOT NULL DEFAULT 0,
                    DureePreavisMois            INTEGER NOT NULL DEFAULT 0,
                    StatutPreavis               INTEGER NOT NULL DEFAULT 0,
                    IndemnitePreavis            INTEGER NOT NULL DEFAULT 0,
                    IndemniteLicenciement       INTEGER NOT NULL DEFAULT 0,
                    TauxLicenciementApplique    TEXT    NOT NULL DEFAULT '0',
                    Indemnite13emeMois          INTEGER NOT NULL DEFAULT 0,
                    Mois13emeMois               TEXT    NOT NULL DEFAULT '0',
                    IndemnitesDiverses          INTEGER NOT NULL DEFAULT 0,
                    LibelleIndemnitesDiverses   TEXT,
                    DeductionsPrets             INTEGER NOT NULL DEFAULT 0,
                    DeductionsAutres            INTEGER NOT NULL DEFAULT 0,
                    LibelleDeductionsAutres     TEXT,
                    EstimationIUTS              INTEGER NOT NULL DEFAULT 0,
                    EstimationCNSS              INTEGER NOT NULL DEFAULT 0,
                    TotalBrut                   INTEGER NOT NULL DEFAULT 0,
                    TotalExonere                INTEGER NOT NULL DEFAULT 0,
                    TotalImposable              INTEGER NOT NULL DEFAULT 0,
                    TotalRetenues               INTEGER NOT NULL DEFAULT 0,
                    TotalNet                    INTEGER NOT NULL DEFAULT 0,
                    Commentaire                 TEXT,
                    DateCreation                TEXT    NOT NULL,
                    DateModification            TEXT    NOT NULL,
                    FOREIGN KEY(EmployeeId) REFERENCES Employees(EmployeeId)
                );");

            Executer(connection,
                "CREATE INDEX IF NOT EXISTS IX_SoldesToutCompte_EmployeeId ON SoldesToutCompte (EmployeeId);");
            Executer(connection,
                "CREATE INDEX IF NOT EXISTS IX_SoldesToutCompte_DateSortie ON SoldesToutCompte (DateSortie);");
        }

        private void Migration24_SaisiesOppositionsEtCessions(SQLiteConnection connection)
        {
            Executer(connection, @"
                CREATE TABLE IF NOT EXISTS SaisiesOppositions (
                    Id                          INTEGER PRIMARY KEY AUTOINCREMENT,
                    EmployeeId                  TEXT    NOT NULL,
                    TypeSaisie                  INTEGER NOT NULL DEFAULT 2,
                    NumeroDossier               TEXT,
                    Creancier                   TEXT    NOT NULL,
                    Beneficiaire                TEXT,
                    MontantTotalDu              INTEGER NOT NULL DEFAULT 0,
                    RetenueMensuelleFixee       INTEGER NOT NULL DEFAULT 0,
                    TotalAmorti                 INTEGER NOT NULL DEFAULT 0,
                    DateDebut                   TEXT    NOT NULL,
                    DateFin                     TEXT,
                    DateNotification            TEXT,
                    TribunalOuAutorite          TEXT,
                    ReferenceJugement           TEXT,
                    Statut                      INTEGER NOT NULL DEFAULT 0,
                    PrioriteOrdre               INTEGER NOT NULL DEFAULT 0,
                    DateMainlevee               TEXT,
                    ReferenceMainlevee          TEXT,
                    Commentaire                 TEXT,
                    DateCreation                TEXT    NOT NULL,
                    DateModification            TEXT    NOT NULL,
                    FOREIGN KEY(EmployeeId) REFERENCES Employees(EmployeeId)
                );");

            Executer(connection,
                "CREATE INDEX IF NOT EXISTS IX_SaisiesOppositions_EmployeeId ON SaisiesOppositions (EmployeeId);");
            Executer(connection,
                "CREATE INDEX IF NOT EXISTS IX_SaisiesOppositions_Statut ON SaisiesOppositions (Statut);");

            Executer(connection, @"
                CREATE TABLE IF NOT EXISTS RetenuesSaisiesMensuelles (
                    Id                          INTEGER PRIMARY KEY AUTOINCREMENT,
                    SaisieOppositionId          INTEGER NOT NULL,
                    EmployeeId                  TEXT    NOT NULL,
                    Annee                       INTEGER NOT NULL,
                    Mois                        INTEGER NOT NULL,
                    MontantRetenu               INTEGER NOT NULL DEFAULT 0,
                    BaseNetCalcul               INTEGER NOT NULL DEFAULT 0,
                    QuotiteDisponible           INTEGER NOT NULL DEFAULT 0,
                    RangPriorite                INTEGER NOT NULL DEFAULT 0,
                    DatePrelevement             TEXT    NOT NULL,
                    NumeroChequeOuVirement      TEXT,
                    StatutPaiement              INTEGER NOT NULL DEFAULT 0,
                    DateReversement             TEXT,
                    Commentaire                 TEXT,
                    FOREIGN KEY(SaisieOppositionId) REFERENCES SaisiesOppositions(Id) ON DELETE CASCADE,
                    FOREIGN KEY(EmployeeId) REFERENCES Employees(EmployeeId)
                );");

            Executer(connection,
                "CREATE INDEX IF NOT EXISTS IX_RetenuesSaisies_Periode ON RetenuesSaisiesMensuelles (Annee, Mois, EmployeeId);");
            Executer(connection,
                "CREATE INDEX IF NOT EXISTS IX_RetenuesSaisies_SaisieId ON RetenuesSaisiesMensuelles (SaisieOppositionId);");
        }

        private void Migration25_RappelsEtRegularisations(SQLiteConnection connection)
        {
            Executer(connection, @"
                CREATE TABLE IF NOT EXISTS RappelsPaie (
                    Id                          INTEGER PRIMARY KEY AUTOINCREMENT,
                    EmployeeId                  TEXT    NOT NULL,
                    TypeRappel                  INTEGER NOT NULL DEFAULT 0,
                    Libelle                     TEXT    NOT NULL,
                    Sens                        INTEGER NOT NULL DEFAULT 0,
                    MoisOrigineDebut            TEXT,
                    MoisOrigineFin              TEXT,
                    AnneeImputation             INTEGER NOT NULL,
                    MoisImputation              INTEGER NOT NULL,
                    MontantBrut                 INTEGER NOT NULL DEFAULT 0,
                    MontantCnssSalarial         INTEGER NOT NULL DEFAULT 0,
                    MontantCnssPatronal         INTEGER NOT NULL DEFAULT 0,
                    MontantTPA                  INTEGER NOT NULL DEFAULT 0,
                    MontantIUTS                 INTEGER NOT NULL DEFAULT 0,
                    MontantNet                  INTEGER NOT NULL DEFAULT 0,
                    SoumisIUTS                  INTEGER NOT NULL DEFAULT 1,
                    SoumisCNSS                  INTEGER NOT NULL DEFAULT 1,
                    SoumisTPA                   INTEGER NOT NULL DEFAULT 1,
                    Statut                      INTEGER NOT NULL DEFAULT 0,
                    Motif                       TEXT,
                    DetailsCalculJson           TEXT,
                    DateCreation                TEXT    NOT NULL,
                    DateImputation              TEXT,
                    CreePar                     TEXT,
                    FOREIGN KEY(EmployeeId) REFERENCES Employees(EmployeeId)
                );");

            Executer(connection,
                "CREATE INDEX IF NOT EXISTS IX_RappelsPaie_EmployeeId ON RappelsPaie (EmployeeId);");
            Executer(connection,
                "CREATE INDEX IF NOT EXISTS IX_RappelsPaie_Imputation ON RappelsPaie (AnneeImputation, MoisImputation);");
            Executer(connection,
                "CREATE INDEX IF NOT EXISTS IX_RappelsPaie_Statut ON RappelsPaie (Statut);");

            Executer(connection, @"
                CREATE TABLE IF NOT EXISTS RegularisationsIUTSAnnuel (
                    Id                          INTEGER PRIMARY KEY AUTOINCREMENT,
                    EmployeeId                  TEXT    NOT NULL,
                    Annee                       INTEGER NOT NULL,
                    NombreMoisTravailles        INTEGER NOT NULL DEFAULT 12,
                    CumulBrutImposable          INTEGER NOT NULL DEFAULT 0,
                    IutsTheoriqueAnnuel         INTEGER NOT NULL DEFAULT 0,
                    CumulIutsPreleve            INTEGER NOT NULL DEFAULT 0,
                    DifferenceRegularisation    INTEGER NOT NULL DEFAULT 0,
                    MoisImputation              INTEGER NOT NULL DEFAULT 12,
                    Statut                      INTEGER NOT NULL DEFAULT 0,
                    RappelPaieId                INTEGER,
                    Observations                TEXT,
                    DateCreation                TEXT    NOT NULL,
                    DateModification            TEXT    NOT NULL,
                    FOREIGN KEY(EmployeeId) REFERENCES Employees(EmployeeId),
                    FOREIGN KEY(RappelPaieId) REFERENCES RappelsPaie(Id) ON DELETE SET NULL
                );");

            Executer(connection,
                "CREATE INDEX IF NOT EXISTS IX_RegulIuts_Annee_Employee ON RegularisationsIUTSAnnuel (Annee, EmployeeId);");

            // Rubriques par défaut pour les rappels et régularisations
            if (TableExiste(connection, "Rubriques"))
            {
                Executer(connection, @"
                    INSERT OR IGNORE INTO Rubriques
                    (Code, Libelle, Sens, ModeCalcul, MontantFixe, TauxSalarial, TauxPatronal,
                     CodeBase, SoumisIUTS, SoumisCNSS, Plafonne, Plafond, PourcentagePlafond,
                     Ordre, Active, EstSysteme, CompteComptable, CompteContrepartie, Commentaire)
                    VALUES ('RAPPELSAL', 'Rappel de salaire rétroactif', 0, 0, 0, '0', '0', '', 1, 1, 0, 0, '0',
                            65, 1, 0, '6611', '4221', 'Rappels de salaire et revalorisations sur périodes closes.');");

                Executer(connection, @"
                    INSERT OR IGNORE INTO Rubriques
                    (Code, Libelle, Sens, ModeCalcul, MontantFixe, TauxSalarial, TauxPatronal,
                     CodeBase, SoumisIUTS, SoumisCNSS, Plafonne, Plafond, PourcentagePlafond,
                     Ordre, Active, EstSysteme, CompteComptable, CompteContrepartie, Commentaire)
                    VALUES ('RAPPELPRIME', 'Rappel de primes / indemnités', 0, 0, 0, '0', '0', '', 1, 1, 0, 0, '0',
                            66, 1, 0, '6617', '4221', 'Rattrapage d''omissions ou régularisations de primes.');");

                Executer(connection, @"
                    INSERT OR IGNORE INTO Rubriques
                    (Code, Libelle, Sens, ModeCalcul, MontantFixe, TauxSalarial, TauxPatronal,
                     CodeBase, SoumisIUTS, SoumisCNSS, Plafonne, Plafond, PourcentagePlafond,
                     Ordre, Active, EstSysteme, CompteComptable, CompteContrepartie, Commentaire)
                    VALUES ('TROPPERCU', 'Trop-perçu sur salaire antérieur', 1, 0, 0, '0', '0', '', 0, 0, 0, 0, '0',
                            124, 1, 0, '4221', '6611', 'Récupération de trop-perçu sur période close.');");

                Executer(connection, @"
                    INSERT OR IGNORE INTO Rubriques
                    (Code, Libelle, Sens, ModeCalcul, MontantFixe, TauxSalarial, TauxPatronal,
                     CodeBase, SoumisIUTS, SoumisCNSS, Plafonne, Plafond, PourcentagePlafond,
                     Ordre, Active, EstSysteme, CompteComptable, CompteContrepartie, Commentaire)
                    VALUES ('REGULIUTS', 'Régularisation IUTS fin d''exercice', 1, 0, 0, '0', '0', '', 0, 0, 0, 0, '0',
                            125, 1, 0, '4472', '4221', 'Régularisation annuelle de fin d''année (CGI Art. 104-124).');");
            }
        }

        private void Migration26_AvantagesEnNature(SQLiteConnection connection)
        {
            Executer(connection, @"
                CREATE TABLE IF NOT EXISTS AvantagesEnNature (
                    Id                      INTEGER PRIMARY KEY AUTOINCREMENT,
                    EmployeeId              TEXT    NOT NULL,
                    TypeAvantage            INTEGER NOT NULL DEFAULT 0,
                    Libelle                 TEXT    NOT NULL,
                    ModeEvaluation          INTEGER NOT NULL DEFAULT 0,
                    MontantAvantage         INTEGER NOT NULL DEFAULT 0,
                    PartSalariale           INTEGER NOT NULL DEFAULT 0,
                    ValeurNetteImposable    INTEGER NOT NULL DEFAULT 0,
                    DateDebut               TEXT    NOT NULL,
                    DateFin                 TEXT,
                    Actif                   INTEGER NOT NULL DEFAULT 1,
                    SoumisIUTS              INTEGER NOT NULL DEFAULT 1,
                    SoumisCNSS              INTEGER NOT NULL DEFAULT 1,
                    SoumisTPA               INTEGER NOT NULL DEFAULT 1,
                    CodeRubriqueGain        TEXT    NOT NULL DEFAULT 'AVNAT_LOG',
                    CodeRubriqueRetenue     TEXT    NOT NULL DEFAULT 'RET_AVNAT',
                    Commentaire             TEXT,
                    DateCreation            TEXT    NOT NULL,
                    DateModification        TEXT    NOT NULL,
                    FOREIGN KEY(EmployeeId) REFERENCES Employees(EmployeeId)
                );");

            Executer(connection,
                "CREATE INDEX IF NOT EXISTS IX_AvantagesEnNature_EmployeeId ON AvantagesEnNature (EmployeeId);");
            Executer(connection,
                "CREATE INDEX IF NOT EXISTS IX_AvantagesEnNature_Actif ON AvantagesEnNature (Actif);");

            if (TableExiste(connection, "Rubriques"))
            {
                // Rubriques de gain (réintégration au brut imposable/cotisable)
                Executer(connection, @"
                    INSERT OR IGNORE INTO Rubriques
                    (Code, Libelle, Sens, ModeCalcul, MontantFixe, TauxSalarial, TauxPatronal,
                     CodeBase, SoumisIUTS, SoumisCNSS, Plafonne, Plafond, PourcentagePlafond,
                     Ordre, Active, EstSysteme, CompteComptable, CompteContrepartie, Commentaire)
                    VALUES ('AVNAT_LOG', 'Avantage en nature logement', 0, 0, 0, '0', '0', '', 1, 1, 0, 0, '0',
                            40, 1, 0, '6614', '4221', 'Évaluation forfaitaire (20% base plafonné à 75k) ou réelle du logement de fonction.');");

                Executer(connection, @"
                    INSERT OR IGNORE INTO Rubriques
                    (Code, Libelle, Sens, ModeCalcul, MontantFixe, TauxSalarial, TauxPatronal,
                     CodeBase, SoumisIUTS, SoumisCNSS, Plafonne, Plafond, PourcentagePlafond,
                     Ordre, Active, EstSysteme, CompteComptable, CompteContrepartie, Commentaire)
                    VALUES ('AVNAT_VEH', 'Avantage en nature véhicule', 0, 0, 0, '0', '0', '', 1, 1, 0, 0, '0',
                            41, 1, 0, '6614', '4221', 'Mise à disposition d''un véhicule de fonction pour usage mixte personnel/professionnel.');");

                Executer(connection, @"
                    INSERT OR IGNORE INTO Rubriques
                    (Code, Libelle, Sens, ModeCalcul, MontantFixe, TauxSalarial, TauxPatronal,
                     CodeBase, SoumisIUTS, SoumisCNSS, Plafonne, Plafond, PourcentagePlafond,
                     Ordre, Active, EstSysteme, CompteComptable, CompteContrepartie, Commentaire)
                    VALUES ('AVNAT_ENG', 'Avantage en nature eau / électricité', 0, 0, 0, '0', '0', '', 1, 1, 0, 0, '0',
                            42, 1, 0, '6614', '4221', 'Prise en charge des factures ONEA et SONABEL par l''employeur.');");

                Executer(connection, @"
                    INSERT OR IGNORE INTO Rubriques
                    (Code, Libelle, Sens, ModeCalcul, MontantFixe, TauxSalarial, TauxPatronal,
                     CodeBase, SoumisIUTS, SoumisCNSS, Plafonne, Plafond, PourcentagePlafond,
                     Ordre, Active, EstSysteme, CompteComptable, CompteContrepartie, Commentaire)
                    VALUES ('AVNAT_DOM', 'Avantage en nature domesticité', 0, 0, 0, '0', '0', '', 1, 1, 0, 0, '0',
                            43, 1, 0, '6614', '4221', 'Prise en charge des gens de maison (gardien, cuisinier, chauffeur, jardinier).');");

                Executer(connection, @"
                    INSERT OR IGNORE INTO Rubriques
                    (Code, Libelle, Sens, ModeCalcul, MontantFixe, TauxSalarial, TauxPatronal,
                     CodeBase, SoumisIUTS, SoumisCNSS, Plafonne, Plafond, PourcentagePlafond,
                     Ordre, Active, EstSysteme, CompteComptable, CompteContrepartie, Commentaire)
                    VALUES ('AVNAT_DIV', 'Avantage en nature nourriture / divers', 0, 0, 0, '0', '0', '', 1, 1, 0, 0, '0',
                            44, 1, 0, '6614', '4221', 'Nourriture ou autres prestations en nature fournies au salarié.');");

                // Rubrique de retenue (déduction en bas de bulletin du net à payer)
                Executer(connection, @"
                    INSERT OR IGNORE INTO Rubriques
                    (Code, Libelle, Sens, ModeCalcul, MontantFixe, TauxSalarial, TauxPatronal,
                     CodeBase, SoumisIUTS, SoumisCNSS, Plafonne, Plafond, PourcentagePlafond,
                     Ordre, Active, EstSysteme, CompteComptable, CompteContrepartie, Commentaire)
                    VALUES ('RET_AVNAT', 'Reprise des avantages en nature', 1, 0, 0, '0', '0', '', 0, 0, 0, 0, '0',
                            130, 1, 0, '4221', '6614', 'Déduction en bas de bulletin de la valeur des avantages fournis en nature.');");
            }
        }

        private void Migration11_RubriquesDeSaisie(SQLiteConnection connection)
        {
            if (!TableExiste(connection, "Rubriques"))
                return;

            Executer(connection, @"
                INSERT OR IGNORE INTO Rubriques
                (Code, Libelle, Sens, ModeCalcul, MontantFixe, TauxSalarial, TauxPatronal,
                 CodeBase, SoumisIUTS, SoumisCNSS, Plafonne, Plafond, PourcentagePlafond,
                 Ordre, Active, EstSysteme, CompteComptable, CompteContrepartie, Commentaire)
                VALUES ('HSUP', 'Heures supplémentaires', 0, 0, 0, '0', '0', '', 1, 1, 0, 0, '0',
                        60, 1, 0, '6612', '4221', 'Saisir le nombre d''heures en quantité et leur valeur en montant.');");

            Executer(connection, @"
                INSERT OR IGNORE INTO Rubriques
                (Code, Libelle, Sens, ModeCalcul, MontantFixe, TauxSalarial, TauxPatronal,
                 CodeBase, SoumisIUTS, SoumisCNSS, Plafonne, Plafond, PourcentagePlafond,
                 Ordre, Active, EstSysteme, CompteComptable, CompteContrepartie, Commentaire)
                VALUES ('PRIME', 'Prime exceptionnelle', 0, 0, 0, '0', '0', '', 1, 1, 0, 0, '0',
                        61, 1, 0, '6617', '4221', 'Prime ponctuelle, entièrement soumise à l''impôt et aux cotisations.');");

            Executer(connection, @"
                INSERT OR IGNORE INTO Rubriques
                (Code, Libelle, Sens, ModeCalcul, MontantFixe, TauxSalarial, TauxPatronal,
                 CodeBase, SoumisIUTS, SoumisCNSS, Plafonne, Plafond, PourcentagePlafond,
                 Ordre, Active, EstSysteme, CompteComptable, CompteContrepartie, Commentaire)
                VALUES ('PRIMEREND', 'Prime de rendement', 0, 0, 0, '0', '0', '', 1, 1, 0, 0, '0',
                        62, 1, 0, '6617', '4221', 'Prime liée aux résultats, variable d''un mois à l''autre.');");

            Executer(connection, @"
                INSERT OR IGNORE INTO Rubriques
                (Code, Libelle, Sens, ModeCalcul, MontantFixe, TauxSalarial, TauxPatronal,
                 CodeBase, SoumisIUTS, SoumisCNSS, Plafonne, Plafond, PourcentagePlafond,
                 Ordre, Active, EstSysteme, CompteComptable, CompteContrepartie, Commentaire)
                VALUES ('PRIMEANC', 'Prime d''ancienneté', 0, 0, 0, '0', '0', '', 1, 1, 0, 0, '0',
                        63, 1, 0, '6613', '4221', 'Le calcul par tranche d''ancienneté relève du module SOC-05.');");

            Executer(connection, @"
                INSERT OR IGNORE INTO Rubriques
                (Code, Libelle, Sens, ModeCalcul, MontantFixe, TauxSalarial, TauxPatronal,
                 CodeBase, SoumisIUTS, SoumisCNSS, Plafonne, Plafond, PourcentagePlafond,
                 Ordre, Active, EstSysteme, CompteComptable, CompteContrepartie, Commentaire)
                VALUES ('GRATIF', 'Gratification / 13e mois', 0, 0, 0, '0', '0', '', 1, 1, 0, 0, '0',
                        64, 1, 0, '6618', '4221', 'Le prorata selon le temps de présence relève du module PAI-10.');");

            Executer(connection, @"
                INSERT OR IGNORE INTO Rubriques
                (Code, Libelle, Sens, ModeCalcul, MontantFixe, TauxSalarial, TauxPatronal,
                 CodeBase, SoumisIUTS, SoumisCNSS, Plafonne, Plafond, PourcentagePlafond,
                 Ordre, Active, EstSysteme, CompteComptable, CompteContrepartie, Commentaire)
                VALUES ('INDPREAVIS', 'Indemnité compensatrice de préavis', 0, 0, 0, '0', '0', '', 1, 0, 0, 0, '0',
                        70, 1, 0, '6635', '4221', 'Revenu imposable, mais hors assiette CNSS.');");

            Executer(connection, @"
                INSERT OR IGNORE INTO Rubriques
                (Code, Libelle, Sens, ModeCalcul, MontantFixe, TauxSalarial, TauxPatronal,
                 CodeBase, SoumisIUTS, SoumisCNSS, Plafonne, Plafond, PourcentagePlafond,
                 Ordre, Active, EstSysteme, CompteComptable, CompteContrepartie, Commentaire)
                VALUES ('REMBFRAIS', 'Remboursement de frais', 0, 0, 0, '0', '0', '', 0, 0, 0, 0, '0',
                        75, 1, 0, '6181', '4221', 'Remboursement d''une dépense avancée par le salarié : ce n''est pas un revenu.');");

            Executer(connection, @"
                INSERT OR IGNORE INTO Rubriques
                (Code, Libelle, Sens, ModeCalcul, MontantFixe, TauxSalarial, TauxPatronal,
                 CodeBase, SoumisIUTS, SoumisCNSS, Plafonne, Plafond, PourcentagePlafond,
                 Ordre, Active, EstSysteme, CompteComptable, CompteContrepartie, Commentaire)
                VALUES ('FRAISMISS', 'Frais de mission', 0, 0, 0, '0', '0', '', 0, 0, 0, 0, '0',
                        76, 1, 0, '6185', '4221', 'Indemnité de déplacement couvrant une dépense réelle.');");

            Executer(connection, @"
                INSERT OR IGNORE INTO Rubriques
                (Code, Libelle, Sens, ModeCalcul, MontantFixe, TauxSalarial, TauxPatronal,
                 CodeBase, SoumisIUTS, SoumisCNSS, Plafonne, Plafond, PourcentagePlafond,
                 Ordre, Active, EstSysteme, CompteComptable, CompteContrepartie, Commentaire)
                VALUES ('ACOMPTE', 'Acompte sur salaire', 1, 0, 0, '0', '0', '', 0, 0, 0, 0, '0',
                        120, 1, 0, '4221', '4271', 'Avance déjà versée, récupérée sur le net.');");

            Executer(connection, @"
                INSERT OR IGNORE INTO Rubriques
                (Code, Libelle, Sens, ModeCalcul, MontantFixe, TauxSalarial, TauxPatronal,
                 CodeBase, SoumisIUTS, SoumisCNSS, Plafonne, Plafond, PourcentagePlafond,
                 Ordre, Active, EstSysteme, CompteComptable, CompteContrepartie, Commentaire)
                VALUES ('PRET', 'Remboursement de prêt', 1, 0, 0, '0', '0', '', 0, 0, 0, 0, '0',
                        121, 1, 0, '4221', '4272', 'Échéance de prêt au personnel. L''échéancier relève du module PAI-05.');");

            Executer(connection, @"
                INSERT OR IGNORE INTO Rubriques
                (Code, Libelle, Sens, ModeCalcul, MontantFixe, TauxSalarial, TauxPatronal,
                 CodeBase, SoumisIUTS, SoumisCNSS, Plafonne, Plafond, PourcentagePlafond,
                 Ordre, Active, EstSysteme, CompteComptable, CompteContrepartie, Commentaire)
                VALUES ('OPPOSITION', 'Saisie-arrêt / opposition', 1, 0, 0, '0', '0', '', 0, 0, 0, 0, '0',
                        122, 1, 0, '4221', '4273', 'Retenue judiciaire ou pension alimentaire.');");

            Executer(connection, @"
                INSERT OR IGNORE INTO Rubriques
                (Code, Libelle, Sens, ModeCalcul, MontantFixe, TauxSalarial, TauxPatronal,
                 CodeBase, SoumisIUTS, SoumisCNSS, Plafonne, Plafond, PourcentagePlafond,
                 Ordre, Active, EstSysteme, CompteComptable, CompteContrepartie, Commentaire)
                VALUES ('RETSYND', 'Cotisation syndicale', 1, 0, 0, '0', '0', '', 0, 0, 0, 0, '0',
                        123, 1, 0, '4221', '4274', 'Précomptée sur le net, reversée au syndicat.');");
        }

        /// <summary>
        /// Migration 27 — rubrique « avance sur salaire » (dette n°7).
        ///
        /// Le module PAI-05 (Prêts) sait déjà retenir une avance sur salaire, avec son
        /// propre code de retenue (<c>AVANCE</c>) distinct de l'acompte — mais aucune
        /// rubrique de ce code n'existait dans le plan de paie : l'écriture comptable
        /// (CPT-01) la classait donc dans le compte générique 422100, mêlée à tout ce qui
        /// n'a pas de compte configuré, alors que l'acompte (4271) et le prêt (4272) ont
        /// chacun le leur depuis la migration 11.
        ///
        /// Un dossier créé après ce module reçoit déjà <c>AVANCE</c> par
        /// <see cref="RubriqueService.RubriquesDeSaisie"/> (appelée dès que le plan de paie
        /// est vide) ; celle-ci rattrape les dossiers déjà en service, dont le plan de paie
        /// n'est justement plus vide.
        ///
        /// Le compte de contrepartie (4270) est une proposition, pas un compte imposé : il
        /// se corrige comme tout autre, depuis l'écran « Plan de paie ».
        /// </summary>
        private void Migration27_RubriqueAvance(SQLiteConnection connection)
        {
            if (!TableExiste(connection, "Rubriques"))
                return;

            Executer(connection, @"
                INSERT OR IGNORE INTO Rubriques
                (Code, Libelle, Sens, ModeCalcul, MontantFixe, TauxSalarial, TauxPatronal,
                 CodeBase, SoumisIUTS, SoumisCNSS, Plafonne, Plafond, PourcentagePlafond,
                 Ordre, Active, EstSysteme, CompteComptable, CompteContrepartie, Commentaire)
                VALUES ('AVANCE', 'Avance sur salaire', 1, 0, 0, '0', '0', '', 0, 0, 0, 0, '0',
                        119, 1, 0, '4221', '4270',
                        'Somme versée avant l''échéance, récupérée sur le net. Échéancier automatique : module PAI-05.');");
        }

        /// <summary>
        /// Migration 28 — service du salarié (module PIL-01) et rattrapage d'un oubli
        /// de <see cref="CreerSchemaComplet"/>.
        ///
        /// Ajoute la colonne <c>Service</c> à la fiche salarié : le tableau de bord masse
        /// salariale (PIL-01) en a besoin comme second axe de ventilation, à côté de la
        /// catégorie qui existait déjà. Purement déclaratif, saisi à la main sur la fiche.
        ///
        /// Au passage, corrige un oubli distinct : <c>DateSortie</c>, <c>MotifDepart</c> et
        /// <c>StatutSalarie</c> avaient été ajoutés par la migration 23 pour les dossiers
        /// déjà existants, mais jamais reportés dans <see cref="CreerSchemaComplet"/> — un
        /// dossier tout neuf se retrouvait donc sans ces trois colonnes, et la première
        /// lecture de la liste des salariés (qui les sélectionne toutes) aurait échoué avec
        /// une erreur SQL « no such column ». <c>AjouterColonneSiAbsente</c> ne fait rien
        /// sur un dossier migré où elles existent déjà.
        /// </summary>
        private void Migration28_ServiceEtColonnesManquantes(SQLiteConnection connection)
        {
            AjouterColonneSiAbsente(connection, "Employees", "Service", "TEXT");
            AjouterColonneSiAbsente(connection, "Employees", "DateSortie", "TEXT");
            AjouterColonneSiAbsente(connection, "Employees", "MotifDepart", "TEXT");
            AjouterColonneSiAbsente(connection, "Employees", "StatutSalarie", "INTEGER NOT NULL DEFAULT 0");
        }

        /// <summary>
        /// Migration 29 — Organigramme d'entreprise : Directions, services, postes et rattachements hiérarchiques (module RH-01).
        /// </summary>
        private void Migration29_OrganigrammeEtPostes(SQLiteConnection connection)
        {
            Executer(connection, @"
                CREATE TABLE IF NOT EXISTS UnitesOrganisationnelles (
                    Id                   INTEGER PRIMARY KEY AUTOINCREMENT,
                    Code                 TEXT NOT NULL UNIQUE,
                    Libelle              TEXT NOT NULL,
                    TypeUnite            INTEGER NOT NULL DEFAULT 0,
                    ParentId             INTEGER,
                    ResponsableMatricule TEXT,
                    Description          TEXT,
                    Couleur              TEXT,
                    Ordre                INTEGER NOT NULL DEFAULT 0,
                    DateCreation         TEXT NOT NULL,
                    FOREIGN KEY(ParentId) REFERENCES UnitesOrganisationnelles(Id) ON DELETE SET NULL,
                    FOREIGN KEY(ResponsableMatricule) REFERENCES Employees(EmployeeId) ON DELETE SET NULL
                );");

            Executer(connection, @"
                CREATE TABLE IF NOT EXISTS Postes (
                    Id                   INTEGER PRIMARY KEY AUTOINCREMENT,
                    Code                 TEXT NOT NULL UNIQUE,
                    Intitule             TEXT NOT NULL,
                    UniteId              INTEGER,
                    SuperieurPosteId     INTEGER,
                    TitulaireMatricule   TEXT,
                    Classification       TEXT,
                    Missions             TEXT,
                    EffectifRequis       INTEGER NOT NULL DEFAULT 1,
                    Actif                INTEGER NOT NULL DEFAULT 1,
                    Ordre                INTEGER NOT NULL DEFAULT 0,
                    DateCreation         TEXT NOT NULL,
                    FOREIGN KEY(UniteId) REFERENCES UnitesOrganisationnelles(Id) ON DELETE SET NULL,
                    FOREIGN KEY(SuperieurPosteId) REFERENCES Postes(Id) ON DELETE SET NULL,
                    FOREIGN KEY(TitulaireMatricule) REFERENCES Employees(EmployeeId) ON DELETE SET NULL
                );");

            AjouterColonneSiAbsente(connection, "Employees", "PosteId", "INTEGER");
            AjouterColonneSiAbsente(connection, "Employees", "ManagerMatricule", "TEXT");

            Executer(connection,
                "CREATE INDEX IF NOT EXISTS IX_UnitesOrganisationnelles_Code ON UnitesOrganisationnelles(Code);");
            Executer(connection,
                "CREATE INDEX IF NOT EXISTS IX_UnitesOrganisationnelles_ParentId ON UnitesOrganisationnelles(ParentId);");
            Executer(connection,
                "CREATE INDEX IF NOT EXISTS IX_Postes_Code ON Postes(Code);");
            Executer(connection,
                "CREATE INDEX IF NOT EXISTS IX_Postes_UniteId ON Postes(UniteId);");
            Executer(connection,
                "CREATE INDEX IF NOT EXISTS IX_Postes_Superieur ON Postes(SuperieurPosteId);");
            Executer(connection,
                "CREATE INDEX IF NOT EXISTS IX_Postes_Titulaire ON Postes(TitulaireMatricule);");
        }

        /// <summary>
        /// Migration 30 — Postes & fiches de poste : missions, compétences requises, classification conventionnelle, effectif budgété vs pourvu (module RH-02).
        /// </summary>
        private void Migration30_FichesDePostesEtCompetences(SQLiteConnection connection)
        {
            Executer(connection, @"
                CREATE TABLE IF NOT EXISTS Competences (
                    Id           INTEGER PRIMARY KEY AUTOINCREMENT,
                    Code         TEXT NOT NULL UNIQUE,
                    Libelle      TEXT NOT NULL,
                    Domaine      INTEGER NOT NULL DEFAULT 0,
                    Description  TEXT,
                    DateCreation TEXT NOT NULL
                );");

            Executer(connection, @"
                CREATE TABLE IF NOT EXISTS PosteCompetences (
                    Id             INTEGER PRIMARY KEY AUTOINCREMENT,
                    PosteId        INTEGER NOT NULL,
                    CompetenceId   INTEGER NOT NULL,
                    NiveauRequis   INTEGER NOT NULL DEFAULT 2,
                    EstObligatoire INTEGER NOT NULL DEFAULT 1,
                    Commentaire    TEXT,
                    FOREIGN KEY(PosteId) REFERENCES Postes(Id) ON DELETE CASCADE,
                    FOREIGN KEY(CompetenceId) REFERENCES Competences(Id) ON DELETE CASCADE
                );");

            // Extension de la table Postes pour la fiche de poste détaillée et la classification conventionnelle
            AjouterColonneSiAbsente(connection, "Postes", "FinalitePrincipale", "TEXT");
            AjouterColonneSiAbsente(connection, "Postes", "ActivitesCles", "TEXT");
            AjouterColonneSiAbsente(connection, "Postes", "NiveauEtudeRequis", "TEXT");
            AjouterColonneSiAbsente(connection, "Postes", "DiplomeRequis", "TEXT");
            AjouterColonneSiAbsente(connection, "Postes", "ExperienceMinimaleAnnees", "INTEGER NOT NULL DEFAULT 0");
            AjouterColonneSiAbsente(connection, "Postes", "ConditionsTravail", "TEXT");
            AjouterColonneSiAbsente(connection, "Postes", "ConventionId", "INTEGER");
            AjouterColonneSiAbsente(connection, "Postes", "CategorieConventionnelle", "TEXT");
            AjouterColonneSiAbsente(connection, "Postes", "EchelonConventionnel", "TEXT");
            AjouterColonneSiAbsente(connection, "Postes", "SalaireMinimumConventionnel", "INTEGER NOT NULL DEFAULT 0");
            AjouterColonneSiAbsente(connection, "Postes", "SalaireMinBudgete", "INTEGER NOT NULL DEFAULT 0");
            AjouterColonneSiAbsente(connection, "Postes", "SalaireMaxBudgete", "INTEGER NOT NULL DEFAULT 0");

            Executer(connection, "CREATE INDEX IF NOT EXISTS IX_Competences_Code ON Competences(Code);");
            Executer(connection, "CREATE INDEX IF NOT EXISTS IX_Competences_Domaine ON Competences(Domaine);");
            Executer(connection, "CREATE INDEX IF NOT EXISTS IX_PosteCompetences_Poste ON PosteCompetences(PosteId);");
            Executer(connection, "CREATE INDEX IF NOT EXISTS IX_PosteCompetences_Competence ON PosteCompetences(CompetenceId);");

            // Pré-remplissage du catalogue initial des compétences courantes
            InsererCompetenceInitiale(connection, "COMPTA-OHADA", "Comptabilité générale & SYSCOHADA", 0, "Maîtrise des écritures, règles et états financiers SYSCOHADA révisé.");
            InsererCompetenceInitiale(connection, "PAIE-BF", "Gestion de la paie burkinabè", 0, "Calcul du brut, des cotisations CNSS et de la retenue IUTS selon le CGI.");
            InsererCompetenceInitiale(connection, "DROIT-TRAVAIL", "Droit du travail burkinabè", 3, "Application du Code du travail, conventions collectives et gestion des contrats.");
            InsererCompetenceInitiale(connection, "FISCALITE-RH", "Fiscalité des rémunérations & TPA", 3, "Déclarations fiscales IUTS, TPA et retenues à la source.");
            InsererCompetenceInitiale(connection, "EXCEL-AVANCE", "Bureautique & Excel avancé", 0, "Tableaux croisés dynamiques, formules complexes, modélisation financière et RH.");
            InsererCompetenceInitiale(connection, "MANAGEMENT", "Management d'équipe & Leadership", 1, "Encadrement, animation des équipes, fixation des objectifs et entretiens annuels.");
            InsererCompetenceInitiale(connection, "COMMUNICATION", "Communication professionnelle", 2, "Aisance relationnelle, écoute active et communication écrite/orale claire.");
            InsererCompetenceInitiale(connection, "NEGOCIATION", "Négociation & Relation partenaires", 0, "Techniques de négociation commerciale, fournisseurs et partenaires sociaux.");
            InsererCompetenceInitiale(connection, "ANALYSE-FIN", "Analyse financière & Budgétaire", 0, "Élaboration, suivi budgétaire et tableaux de bord financiers.");
            InsererCompetenceInitiale(connection, "RIGUEUR", "Rigueur, Discrétion & Confidentialité", 2, "Respect scrupuleux du secret professionnel et fiabilité dans l'exécution.");
        }

        private void InsererCompetenceInitiale(SQLiteConnection connection, string code, string libelle, int domaine, string description)
        {
            using (var cmd = connection.CreateCommand())
            {
                cmd.CommandText = @"
                    INSERT OR IGNORE INTO Competences (Code, Libelle, Domaine, Description, DateCreation)
                    VALUES (@code, @libelle, @domaine, @desc, @date);";
                cmd.Parameters.AddWithValue("@code", code);
                cmd.Parameters.AddWithValue("@libelle", libelle);
                cmd.Parameters.AddWithValue("@domaine", domaine);
                cmd.Parameters.AddWithValue("@desc", description);
                cmd.Parameters.AddWithValue("@date", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                cmd.ExecuteNonQuery();
            }
        }

        /// <summary>
        /// Migration 31 — Entretiens & évaluations : campagnes annuelles, grilles d'évaluation, objectifs, notation et suivi des entretiens individuels (module RH-03).
        /// </summary>
        private void Migration31_EntretiensEtEvaluations(SQLiteConnection connection)
        {
            Executer(connection, @"
                CREATE TABLE IF NOT EXISTS CampagnesEvaluation (
                    Id            INTEGER PRIMARY KEY AUTOINCREMENT,
                    Code          TEXT NOT NULL UNIQUE,
                    Titre         TEXT NOT NULL,
                    AnneeExercice INTEGER NOT NULL,
                    DateDebut     TEXT NOT NULL,
                    DateFin       TEXT NOT NULL,
                    Statut        INTEGER NOT NULL DEFAULT 0,
                    Description   TEXT,
                    DateCreation  TEXT NOT NULL
                );");

            Executer(connection, @"
                CREATE TABLE IF NOT EXISTS EntretiensEvaluation (
                    Id                     INTEGER PRIMARY KEY AUTOINCREMENT,
                    CampagneId             INTEGER NOT NULL,
                    EmployeeId             TEXT NOT NULL,
                    EvaluateurMatricule    TEXT,
                    DateEntretien          TEXT,
                    Statut                 INTEGER NOT NULL DEFAULT 0,
                    NoteGlobale            TEXT NOT NULL DEFAULT '0.00',
                    PointsForts            TEXT,
                    AxesProgres            TEXT,
                    CommentairesSalarie    TEXT,
                    CommentairesEvaluateur TEXT,
                    AvisDirectionRH        TEXT,
                    SouhaitsEvolution      TEXT,
                    DateValidation         TEXT,
                    DateCreation           TEXT NOT NULL,
                    FOREIGN KEY(CampagneId) REFERENCES CampagnesEvaluation(Id) ON DELETE CASCADE,
                    FOREIGN KEY(EmployeeId) REFERENCES Employees(EmployeeId),
                    FOREIGN KEY(EvaluateurMatricule) REFERENCES Employees(EmployeeId) ON DELETE SET NULL
                );");

            Executer(connection, @"
                CREATE TABLE IF NOT EXISTS EntretienCriteres (
                    Id             INTEGER PRIMARY KEY AUTOINCREMENT,
                    EntretienId    INTEGER NOT NULL,
                    CompetenceId   INTEGER,
                    Categorie      TEXT NOT NULL,
                    Libelle        TEXT NOT NULL,
                    Note           INTEGER NOT NULL DEFAULT 3,
                    Poids          INTEGER NOT NULL DEFAULT 1,
                    Commentaire    TEXT,
                    FOREIGN KEY(EntretienId) REFERENCES EntretiensEvaluation(Id) ON DELETE CASCADE,
                    FOREIGN KEY(CompetenceId) REFERENCES Competences(Id) ON DELETE SET NULL
                );");

            Executer(connection, @"
                CREATE TABLE IF NOT EXISTS EntretienObjectifs (
                    Id               INTEGER PRIMARY KEY AUTOINCREMENT,
                    EntretienId      INTEGER NOT NULL,
                    TypeObjectif     INTEGER NOT NULL DEFAULT 0,
                    Intitule         TEXT NOT NULL,
                    IndicateurMesure TEXT,
                    CibleAttendue    TEXT,
                    ResultatObtenu   TEXT,
                    TauxAtteintePct  INTEGER NOT NULL DEFAULT 0,
                    Appreciation     TEXT,
                    Echeance         TEXT,
                    Poids            INTEGER NOT NULL DEFAULT 1,
                    FOREIGN KEY(EntretienId) REFERENCES EntretiensEvaluation(Id) ON DELETE CASCADE
                );");

            Executer(connection, @"
                CREATE TABLE IF NOT EXISTS EntretienBesoinsFormation (
                    Id               INTEGER PRIMARY KEY AUTOINCREMENT,
                    EntretienId      INTEGER NOT NULL,
                    CompetenceId     INTEGER,
                    ThemeFormation   TEXT NOT NULL,
                    CompetenceVisee  TEXT,
                    Priorite         INTEGER NOT NULL DEFAULT 1,
                    AvisEvaluateur   TEXT,
                    Statut           INTEGER NOT NULL DEFAULT 0,
                    FOREIGN KEY(EntretienId) REFERENCES EntretiensEvaluation(Id) ON DELETE CASCADE,
                    FOREIGN KEY(CompetenceId) REFERENCES Competences(Id) ON DELETE SET NULL
                );");

            Executer(connection,
                "CREATE UNIQUE INDEX IF NOT EXISTS UX_EntretiensEvaluation_Campagne_Employe ON EntretiensEvaluation(CampagneId, EmployeeId);");
            Executer(connection,
                "CREATE INDEX IF NOT EXISTS IX_CampagnesEvaluation_Code ON CampagnesEvaluation(Code);");
            Executer(connection,
                "CREATE INDEX IF NOT EXISTS IX_CampagnesEvaluation_Annee ON CampagnesEvaluation(AnneeExercice);");
            Executer(connection,
                "CREATE INDEX IF NOT EXISTS IX_EntretiensEvaluation_Campagne ON EntretiensEvaluation(CampagneId);");
            Executer(connection,
                "CREATE INDEX IF NOT EXISTS IX_EntretiensEvaluation_Employe ON EntretiensEvaluation(EmployeeId);");
            Executer(connection,
                "CREATE INDEX IF NOT EXISTS IX_EntretiensEvaluation_Evaluateur ON EntretiensEvaluation(EvaluateurMatricule);");
            Executer(connection,
                "CREATE INDEX IF NOT EXISTS IX_EntretiensEvaluation_Statut ON EntretiensEvaluation(Statut);");
            Executer(connection,
                "CREATE INDEX IF NOT EXISTS IX_EntretienCriteres_Entretien ON EntretienCriteres(EntretienId);");
            Executer(connection,
                "CREATE INDEX IF NOT EXISTS IX_EntretienCriteres_Competence ON EntretienCriteres(CompetenceId);");
            Executer(connection,
                "CREATE INDEX IF NOT EXISTS IX_EntretienObjectifs_Entretien ON EntretienObjectifs(EntretienId);");
            Executer(connection,
                "CREATE INDEX IF NOT EXISTS IX_EntretienBesoinsFormation_Entretien ON EntretienBesoinsFormation(EntretienId);");
            Executer(connection,
                "CREATE INDEX IF NOT EXISTS IX_EntretienBesoinsFormation_Competence ON EntretienBesoinsFormation(CompetenceId);");
        }

        /// <summary>
        /// Migration 32 — Gestion de la formation : plan annuel, sessions, participants, coûts et organismes,
        /// avec liaison naturelle vers la TPA (Taxe Patronale et d'Apprentissage) et passerelle RH-03 (module RH-04).
        /// </summary>
        private void Migration32_FormationEtTPA(SQLiteConnection connection)
        {
            Executer(connection, @"
                CREATE TABLE IF NOT EXISTS PlansFormation (
                    Id                    INTEGER PRIMARY KEY AUTOINCREMENT,
                    Code                  TEXT NOT NULL UNIQUE,
                    Titre                 TEXT NOT NULL,
                    AnneeExercice         INTEGER NOT NULL,
                    BudgetPrevisionnel    INTEGER NOT NULL DEFAULT 0,
                    Statut                INTEGER NOT NULL DEFAULT 0,
                    ObjectifsStrategiques TEXT,
                    DateValidation        TEXT,
                    DateCreation          TEXT NOT NULL
                );");

            Executer(connection, @"
                CREATE TABLE IF NOT EXISTS OrganismesFormation (
                    Id             INTEGER PRIMARY KEY AUTOINCREMENT,
                    RaisonSociale  TEXT NOT NULL,
                    ContactNom     TEXT,
                    Telephone      TEXT,
                    Email          TEXT,
                    Adresse        TEXT,
                    NumeroAgrement TEXT,
                    Specialites    TEXT,
                    Actif          INTEGER NOT NULL DEFAULT 1
                );");

            Executer(connection, @"
                CREATE TABLE IF NOT EXISTS FormationsCatalogue (
                    Id                       INTEGER PRIMARY KEY AUTOINCREMENT,
                    Code                     TEXT NOT NULL UNIQUE,
                    Intitule                 TEXT NOT NULL,
                    Domaine                  TEXT,
                    CompetenceViseeId        INTEGER,
                    TypeFormation            INTEGER NOT NULL DEFAULT 0,
                    DureeHeures              INTEGER NOT NULL DEFAULT 0,
                    CoutEstimeParParticipant INTEGER NOT NULL DEFAULT 0,
                    ObjectifsPedagogiques    TEXT,
                    Prerequis                TEXT,
                    Actif                    INTEGER NOT NULL DEFAULT 1,
                    FOREIGN KEY(CompetenceViseeId) REFERENCES Competences(Id) ON DELETE SET NULL
                );");

            Executer(connection, @"
                CREATE TABLE IF NOT EXISTS SessionsFormation (
                    Id               INTEGER PRIMARY KEY AUTOINCREMENT,
                    FormationId      INTEGER NOT NULL,
                    PlanFormationId  INTEGER NOT NULL,
                    OrganismeId      INTEGER,
                    CodeSession      TEXT NOT NULL UNIQUE,
                    IntituleSession  TEXT,
                    DateDebut        TEXT NOT NULL,
                    DateFin          TEXT NOT NULL,
                    Lieu             TEXT,
                    FormateurNom     TEXT,
                    Statut           INTEGER NOT NULL DEFAULT 0,
                    CoutPedagogique  INTEGER NOT NULL DEFAULT 0,
                    CoutLogistique   INTEGER NOT NULL DEFAULT 0,
                    CoutAutre        INTEGER NOT NULL DEFAULT 0,
                    CoutTotalReel    INTEGER NOT NULL DEFAULT 0,
                    Commentaire      TEXT,
                    FOREIGN KEY(FormationId) REFERENCES FormationsCatalogue(Id),
                    FOREIGN KEY(PlanFormationId) REFERENCES PlansFormation(Id) ON DELETE CASCADE,
                    FOREIGN KEY(OrganismeId) REFERENCES OrganismesFormation(Id) ON DELETE SET NULL
                );");

            Executer(connection, @"
                CREATE TABLE IF NOT EXISTS SessionInscriptions (
                    Id                 INTEGER PRIMARY KEY AUTOINCREMENT,
                    SessionId          INTEGER NOT NULL,
                    EmployeeId         TEXT NOT NULL,
                    DateInscription    TEXT NOT NULL,
                    StatutPresence     INTEGER NOT NULL DEFAULT 0,
                    NoteEvaluation     TEXT,
                    AttestationObtenue INTEGER NOT NULL DEFAULT 0,
                    FraisImputes       INTEGER NOT NULL DEFAULT 0,
                    Commentaire        TEXT,
                    FOREIGN KEY(SessionId) REFERENCES SessionsFormation(Id) ON DELETE CASCADE,
                    FOREIGN KEY(EmployeeId) REFERENCES Employees(EmployeeId)
                );");

            // Passerelle avec le recueil des besoins RH-03 via helper idempotent
            AjouterColonneSiAbsente(connection, "EntretienBesoinsFormation", "SessionFormationId", "INTEGER REFERENCES SessionsFormation(Id)");

            // Index de performance et d'intégrité
            Executer(connection, "CREATE INDEX IF NOT EXISTS IX_PlansFormation_Annee ON PlansFormation(AnneeExercice);");
            Executer(connection, "CREATE INDEX IF NOT EXISTS IX_FormationsCatalogue_Competence ON FormationsCatalogue(CompetenceViseeId);");
            Executer(connection, "CREATE INDEX IF NOT EXISTS IX_SessionsFormation_Plan ON SessionsFormation(PlanFormationId);");
            Executer(connection, "CREATE INDEX IF NOT EXISTS IX_SessionsFormation_Formation ON SessionsFormation(FormationId);");
            Executer(connection, "CREATE INDEX IF NOT EXISTS IX_SessionsFormation_Organisme ON SessionsFormation(OrganismeId);");
            Executer(connection, "CREATE INDEX IF NOT EXISTS IX_SessionsFormation_Statut ON SessionsFormation(Statut);");
            Executer(connection, "CREATE UNIQUE INDEX IF NOT EXISTS UX_SessionInscriptions_Session_Employe ON SessionInscriptions(SessionId, EmployeeId);");
            Executer(connection, "CREATE INDEX IF NOT EXISTS IX_SessionInscriptions_Employe ON SessionInscriptions(EmployeeId);");
            Executer(connection, "CREATE INDEX IF NOT EXISTS IX_EntretienBesoinsFormation_Session ON EntretienBesoinsFormation(SessionFormationId);");

            // Amorçage : 2 organismes de référence
            Executer(connection, @"
                INSERT OR IGNORE INTO OrganismesFormation (RaisonSociale, ContactNom, Telephone, Email, NumeroAgrement, Specialites, Actif)
                VALUES ('Direction des Ressources Humaines (Interne)', 'Responsable Formation', '+226 25 00 00 00', 'formation@sigma-rh.bf', 'INTERNE', 'Accueil, Intégration, Procédures internes, Sécurité', 1);");

            Executer(connection, @"
                INSERT OR IGNORE INTO OrganismesFormation (RaisonSociale, ContactNom, Telephone, Email, NumeroAgrement, Specialites, Actif)
                VALUES ('Institut Burkinabè de Management & Ingénierie (IBMI)', 'Dr. Ouedraogo', '+226 25 30 12 34', 'contact@ibmi-bf.com', 'AGR-FAFPA/2024/082', 'Management, Droit social, Fiscalité OHADA, Comptabilité', 1);");

            // Amorçage : 3 actions de formation au catalogue résolues par code de compétence
            Executer(connection, @"
                INSERT OR IGNORE INTO FormationsCatalogue (Code, Intitule, Domaine, CompetenceViseeId, TypeFormation, DureeHeures, CoutEstimeParParticipant, ObjectifsPedagogiques, Actif)
                VALUES (
                    'MGT-01',
                    'Management opérationnel d''équipe & Conduite des entretiens',
                    'Management & RH',
                    (SELECT Id FROM Competences WHERE Code = 'MANAGEMENT'),
                    1, 24, 15000000,
                    'Maîtriser les leviers de motivation, fixer des objectifs SMART et conduire les entretiens annuels d''évaluation.',
                    1
                );");

            Executer(connection, @"
                INSERT OR IGNORE INTO FormationsCatalogue (Code, Intitule, Domaine, CompetenceViseeId, TypeFormation, DureeHeures, CoutEstimeParParticipant, ObjectifsPedagogiques, Actif)
                VALUES (
                    'FISC-01',
                    'Fiscalité des rémunérations, Déclarations IUTS & TPA',
                    'Fiscalité & Paie',
                    (SELECT Id FROM Competences WHERE Code = 'FISCALITE-RH'),
                    1, 16, 12000000,
                    'Maîtriser les règles d''assiette IUTS et TPA, le calendrier déclaratif DGI et les déductions pour charges familiales.',
                    1
                );");

            Executer(connection, @"
                INSERT OR IGNORE INTO FormationsCatalogue (Code, Intitule, Domaine, CompetenceViseeId, TypeFormation, DureeHeures, CoutEstimeParParticipant, ObjectifsPedagogiques, Actif)
                VALUES (
                    'PAIE-01',
                    'Pratique de la paie burkinabè et déclarations sociales CNSS',
                    'Paie & Social',
                    (SELECT Id FROM Competences WHERE Code = 'PAIE-BF'),
                    0, 20, 8000000,
                    'Calcul des éléments de paie, application des plafonds de cotisations CNSS et édition des états nominatifs.',
                    1
                );");
        }

        /// <summary>
        /// Migration 33 — Recrutement & gestion des candidatures (module RH-05).
        /// Crée les tables RecrutementBesoins, RecrutementCandidats, RecrutementCandidatures,
        /// RecrutementEntretiens, les index de performance et d'unicité, ainsi qu'un jeu
        /// d'amorçage initial.
        /// </summary>
        private void Migration33_Recrutement(SQLiteConnection connection)
        {
            Executer(connection, @"
                CREATE TABLE IF NOT EXISTS RecrutementBesoins (
                    Id                     INTEGER PRIMARY KEY AUTOINCREMENT,
                    Reference              TEXT NOT NULL UNIQUE,
                    IntitulePoste          TEXT NOT NULL,
                    PosteId                INTEGER,
                    Departement            TEXT,
                    Motif                  INTEGER NOT NULL DEFAULT 0,
                    NombrePostes           INTEGER NOT NULL DEFAULT 1,
                    NombreRecrutes         INTEGER NOT NULL DEFAULT 0,
                    TypeContratVise        TEXT NOT NULL DEFAULT 'CDI',
                    RemunerationProposeeMin INTEGER,
                    RemunerationProposeeMax INTEGER,
                    DateExpression         TEXT NOT NULL,
                    DateEcheanceSouhaitee  TEXT,
                    DateCloture            TEXT,
                    Statut                 INTEGER NOT NULL DEFAULT 0,
                    ResponsableRecrutement TEXT,
                    DescriptionProfil      TEXT,
                    DateCreation           TEXT NOT NULL,
                    FOREIGN KEY(PosteId) REFERENCES Postes(Id) ON DELETE SET NULL
                );");

            Executer(connection, @"
                CREATE TABLE IF NOT EXISTS RecrutementCandidats (
                    Id                  INTEGER PRIMARY KEY AUTOINCREMENT,
                    Nom                 TEXT NOT NULL,
                    Prenoms             TEXT NOT NULL,
                    Email               TEXT,
                    Telephone           TEXT,
                    DiplomeLePlusEleve  TEXT,
                    AnneesExperience    INTEGER NOT NULL DEFAULT 0,
                    DerniereFonction    TEXT,
                    CompetencesCles     TEXT,
                    CvChemin            TEXT,
                    VivierActif         INTEGER NOT NULL DEFAULT 1,
                    Commentaires        TEXT,
                    DateCreation        TEXT NOT NULL
                );");

            Executer(connection, @"
                CREATE TABLE IF NOT EXISTS RecrutementCandidatures (
                    Id                    INTEGER PRIMARY KEY AUTOINCREMENT,
                    BesoinId              INTEGER NOT NULL,
                    CandidatId            INTEGER NOT NULL,
                    DatePostulation       TEXT NOT NULL,
                    CanalSource           INTEGER NOT NULL DEFAULT 0,
                    PretentionSalariale   INTEGER,
                    EtapeActuelle         INTEGER NOT NULL DEFAULT 0,
                    Statut                INTEGER NOT NULL DEFAULT 0,
                    ScoreEvaluation       REAL,
                    AvisGlobal            INTEGER,
                    CommentaireEvaluation TEXT,
                    DateEmbauche          TEXT,
                    SalaireNegocie        INTEGER,
                    SalarieCreeMatricule  TEXT,
                    DateCreation          TEXT NOT NULL,
                    FOREIGN KEY(BesoinId) REFERENCES RecrutementBesoins(Id) ON DELETE CASCADE,
                    FOREIGN KEY(CandidatId) REFERENCES RecrutementCandidats(Id) ON DELETE CASCADE,
                    FOREIGN KEY(SalarieCreeMatricule) REFERENCES Employees(EmployeeId) ON DELETE SET NULL
                );");

            Executer(connection, @"
                CREATE TABLE IF NOT EXISTS RecrutementEntretiens (
                    Id                  INTEGER PRIMARY KEY AUTOINCREMENT,
                    CandidatureId       INTEGER NOT NULL,
                    Etape               INTEGER NOT NULL DEFAULT 3,
                    DateEntretien       TEXT NOT NULL,
                    EvaluateurNom       TEXT NOT NULL,
                    EvaluateurMatricule TEXT,
                    NoteGlobale         REAL,
                    Avis                INTEGER,
                    PointsForts         TEXT,
                    AxesAmelioration    TEXT,
                    Synthese            TEXT,
                    DateCreation        TEXT NOT NULL,
                    FOREIGN KEY(CandidatureId) REFERENCES RecrutementCandidatures(Id) ON DELETE CASCADE,
                    FOREIGN KEY(EvaluateurMatricule) REFERENCES Employees(EmployeeId) ON DELETE SET NULL
                );");

            // Index de performance et d'intégrité
            Executer(connection, "CREATE UNIQUE INDEX IF NOT EXISTS UX_RecrutementCandidatures_Besoin_Candidat ON RecrutementCandidatures(BesoinId, CandidatId);");
            Executer(connection, "CREATE INDEX IF NOT EXISTS IX_RecrutementBesoins_Statut ON RecrutementBesoins(Statut);");
            Executer(connection, "CREATE INDEX IF NOT EXISTS IX_RecrutementBesoins_Poste ON RecrutementBesoins(PosteId);");
            Executer(connection, "CREATE INDEX IF NOT EXISTS IX_RecrutementCandidats_Email ON RecrutementCandidats(Email);");
            Executer(connection, "CREATE INDEX IF NOT EXISTS IX_RecrutementCandidats_Vivier ON RecrutementCandidats(VivierActif);");
            Executer(connection, "CREATE INDEX IF NOT EXISTS IX_RecrutementCandidatures_Besoin ON RecrutementCandidatures(BesoinId);");
            Executer(connection, "CREATE INDEX IF NOT EXISTS IX_RecrutementCandidatures_Candidat ON RecrutementCandidatures(CandidatId);");
            Executer(connection, "CREATE INDEX IF NOT EXISTS IX_RecrutementCandidatures_Etape ON RecrutementCandidatures(EtapeActuelle);");
            Executer(connection, "CREATE INDEX IF NOT EXISTS IX_RecrutementCandidatures_Statut ON RecrutementCandidatures(Statut);");
            Executer(connection, "CREATE INDEX IF NOT EXISTS IX_RecrutementEntretiens_Candidature ON RecrutementEntretiens(CandidatureId);");

            // Amorçage initial si les tables sont neuves
            Executer(connection, @"
                INSERT OR IGNORE INTO RecrutementBesoins (
                    Reference, IntitulePoste, Departement, Motif, NombrePostes, NombreRecrutes,
                    TypeContratVise, RemunerationProposeeMin, RemunerationProposeeMax,
                    DateExpression, DateEcheanceSouhaitee, Statut, ResponsableRecrutement,
                    DescriptionProfil, DateCreation
                ) VALUES (
                    'REC-2026-001', 'Gestionnaire Paie & Administration RH', 'Ressources Humaines', 0, 1, 0,
                    'CDI', 35000000, 45000000,
                    date('now'), date('now', '+30 days'), 1, 'Direction RH',
                    'Bac+3 en Gestion/RH, 3 ans d''expérience en paie burkinabè (IUTS/CNSS) et maîtrise de la réglementation du travail.',
                    datetime('now')
                );");

            Executer(connection, @"
                INSERT OR IGNORE INTO RecrutementBesoins (
                    Reference, IntitulePoste, Departement, Motif, NombrePostes, NombreRecrutes,
                    TypeContratVise, RemunerationProposeeMin, RemunerationProposeeMax,
                    DateExpression, DateEcheanceSouhaitee, Statut, ResponsableRecrutement,
                    DescriptionProfil, DateCreation
                ) VALUES (
                    'REC-2026-002', 'Comptable Trésorerie & Fiscalité', 'Finances & Comptabilité', 1, 1, 0,
                    'CDI', 40000000, 50000000,
                    date('now'), date('now', '+45 days'), 1, 'Direction Financière',
                    'Licence ou Master CCA, pratique confirmée des états financiers OHADA et déclarations fiscales mensuelles.',
                    datetime('now')
                );");

            Executer(connection, @"
                INSERT OR IGNORE INTO RecrutementCandidats (
                    Nom, Prenoms, Email, Telephone, DiplomeLePlusEleve, AnneesExperience,
                    DerniereFonction, CompetencesCles, VivierActif, Commentaires, DateCreation
                ) VALUES (
                    'TRAORE', 'Aminata', 'aminata.traore@candidat.bf', '+226 70 12 34 56',
                    'Licence Gestion des Ressources Humaines', 4,
                    'Assistante RH & Paie', 'Paie burkinabè, IUTS, CNSS, Déclarations sociales, Recrutement',
                    1, 'Excellente maîtrise des logiciels de paie et bon sens relationnel.', datetime('now')
                );");

            Executer(connection, @"
                INSERT OR IGNORE INTO RecrutementCandidats (
                    Nom, Prenoms, Email, Telephone, DiplomeLePlusEleve, AnneesExperience,
                    DerniereFonction, CompetencesCles, VivierActif, Commentaires, DateCreation
                ) VALUES (
                    'SOME', 'Arnaud', 'arnaud.some@candidat.bf', '+226 76 98 76 54',
                    'Master Comptabilité Contrôle Audit', 5,
                    'Comptable Général', 'SYSCOHADA révisé, Rapprochements bancaires, Fiscalité DGI, Excel avancé',
                    1, 'Expérience de 3 ans en cabinet d''expertise comptable.', datetime('now')
                );");
        }

        /// <summary>
        /// Plan de paie (module SOC-01). Les montants et plafonds suivent la convention
        /// de la base : INTEGER en centimes. Les taux, eux, sont des proportions à
        /// nombreuses décimales et sont donc stockés en TEXT, comme TaxRate.
        /// </summary>
        private void CreerTableRubriques(SQLiteConnection connection)
        {
            Executer(connection, @"
                CREATE TABLE IF NOT EXISTS Rubriques (
                    Code                TEXT PRIMARY KEY,
                    Libelle             TEXT,
                    Sens                INTEGER NOT NULL DEFAULT 0,
                    ModeCalcul          INTEGER NOT NULL DEFAULT 0,
                    MontantFixe         INTEGER NOT NULL DEFAULT 0,
                    TauxSalarial        TEXT,
                    TauxPatronal        TEXT,
                    CodeBase            TEXT,
                    SoumisIUTS          INTEGER NOT NULL DEFAULT 0,
                    SoumisCNSS          INTEGER NOT NULL DEFAULT 0,
                    SoumisTPA           INTEGER NOT NULL DEFAULT 1,
                    Plafonne            INTEGER NOT NULL DEFAULT 0,
                    Plafond             INTEGER NOT NULL DEFAULT 0,
                    PourcentagePlafond  TEXT,
                    Ordre               INTEGER NOT NULL DEFAULT 0,
                    Active              INTEGER NOT NULL DEFAULT 1,
                    EstSysteme          INTEGER NOT NULL DEFAULT 0,
                    CompteComptable     TEXT,
                    CompteContrepartie  TEXT,
                    Commentaire         TEXT
                );");

            Executer(connection,
                "CREATE INDEX IF NOT EXISTS IX_Rubriques_Ordre ON Rubriques(Ordre);");
        }

        /// <summary>
        /// Migration 34 — Gestion des temps et rotations (module RH-08) et barème daté des
        /// heures supplémentaires.
        ///
        ///   • <c>BaremesHeuresSup</c> : majorations datées. Deux barèmes livrés : les taux
        ///     historiques de PAI-03 (15/30/50/100, dimanche de jour à 50 %) au 01/01/2000,
        ///     pour que tout mois antérieur recalculé reste identique au centime, puis les
        ///     cinq tranches 15/35/50/60/120 à partir de la paie d'octobre 2026 ;
        ///   • <c>HeuresSupplementaires</c> : cinquième tranche (dimanche/férié de jour),
        ///     colonnes Heures60/Montant60 à zéro pour l'existant ;
        ///   • <c>Absences.PeriodeImputation</c> : mois de paie (« yyyy-MM ») auquel une
        ///     absence relevée au planning est imputée en entier quand la période de
        ///     pointage ne suit pas le mois civil (arrêté au 20, par exemple). Vide pour
        ///     toute absence existante : son traitement ne change pas ;
        ///   • équipes et cycles de rotation, affectations, planning jour par jour, jours
        ///     fériés et paramètres du relevé des temps.
        ///
        /// Idempotente (CREATE IF NOT EXISTS, ajout de colonne si absente, amorçage si
        /// vide) : elle est aussi rejouée par <see cref="CreerSchemaComplet"/>.
        /// </summary>
        private void Migration34_GestionDesTemps(SQLiteConnection connection)
        {
            Executer(connection, @"
                CREATE TABLE IF NOT EXISTS BaremesHeuresSup (
                    Id                INTEGER PRIMARY KEY AUTOINCREMENT,
                    DateEffet         TEXT    NOT NULL,
                    Libelle           TEXT,
                    Reference         TEXT,
                    EstSysteme        INTEGER NOT NULL DEFAULT 0,
                    TauxTranche1      TEXT    NOT NULL DEFAULT '0.15',
                    TauxTranche2      TEXT    NOT NULL DEFAULT '0.35',
                    TauxNuit          TEXT    NOT NULL DEFAULT '0.50',
                    TauxFerieJour     TEXT    NOT NULL DEFAULT '0.60',
                    TauxFerieNuit     TEXT    NOT NULL DEFAULT '1.20',
                    SeuilTranche1     TEXT    NOT NULL DEFAULT '8',
                    DureeHebdoLegale  TEXT    NOT NULL DEFAULT '40'
                );");
            Executer(connection,
                "CREATE UNIQUE INDEX IF NOT EXISTS UX_BaremesHeuresSup_DateEffet ON BaremesHeuresSup (DateEffet);");

            int baremes;
            using (var cmd = connection.CreateCommand())
            {
                cmd.CommandText = "SELECT COUNT(*) FROM BaremesHeuresSup;";
                baremes = DbValue.IntFromDb(cmd.ExecuteScalar());
            }

            if (baremes == 0)
            {
                foreach (var b in new[] { BaremeHeuresSup.Historique(), BaremeHeuresSup.CinqTranches2026() })
                {
                    using (var cmd = connection.CreateCommand())
                    {
                        cmd.CommandText =
                            "INSERT INTO BaremesHeuresSup (DateEffet, Libelle, Reference, EstSysteme, TauxTranche1, " +
                            "TauxTranche2, TauxNuit, TauxFerieJour, TauxFerieNuit, SeuilTranche1, DureeHebdoLegale) " +
                            "VALUES (@d, @l, @r, 1, @t1, @t2, @tn, @tfj, @tfn, @s1, @dh);";
                        cmd.Parameters.AddWithValue("@d", DbValue.DateToDb(b.DateEffet));
                        cmd.Parameters.AddWithValue("@l", b.Libelle);
                        cmd.Parameters.AddWithValue("@r", b.Reference);
                        cmd.Parameters.AddWithValue("@t1", DbValue.DecimalToDb(b.TauxTranche1));
                        cmd.Parameters.AddWithValue("@t2", DbValue.DecimalToDb(b.TauxTranche2));
                        cmd.Parameters.AddWithValue("@tn", DbValue.DecimalToDb(b.TauxNuit));
                        cmd.Parameters.AddWithValue("@tfj", DbValue.DecimalToDb(b.TauxFerieJour));
                        cmd.Parameters.AddWithValue("@tfn", DbValue.DecimalToDb(b.TauxFerieNuit));
                        cmd.Parameters.AddWithValue("@s1", DbValue.DecimalToDb(b.SeuilTranche1));
                        cmd.Parameters.AddWithValue("@dh", DbValue.DecimalToDb(b.DureeHebdoLegale));
                        cmd.ExecuteNonQuery();
                    }
                }
            }

            AjouterColonneSiAbsente(connection, "HeuresSupplementaires", "Heures60", "TEXT NOT NULL DEFAULT '0'");
            AjouterColonneSiAbsente(connection, "HeuresSupplementaires", "Montant60", "INTEGER NOT NULL DEFAULT 0");
            AjouterColonneSiAbsente(connection, "Absences", "PeriodeImputation", "TEXT");

            Executer(connection, @"
                CREATE TABLE IF NOT EXISTS EquipesTravail (
                    Id                      INTEGER PRIMARY KEY AUTOINCREMENT,
                    Code                    TEXT    NOT NULL UNIQUE,
                    Libelle                 TEXT,
                    Site                    TEXT,
                    Motif                   TEXT    NOT NULL DEFAULT '',
                    DateDebutCycle          TEXT    NOT NULL,
                    DureePosteJour          TEXT    NOT NULL DEFAULT '12',
                    DureePosteNuit          TEXT    NOT NULL DEFAULT '12',
                    HeuresNormalesParPoste  TEXT    NOT NULL DEFAULT '8',
                    ModeSemaine             INTEGER NOT NULL DEFAULT 0,
                    DimancheMajore          INTEGER NOT NULL DEFAULT 0,
                    PanierMode              INTEGER NOT NULL DEFAULT 0,
                    PanierValeur            TEXT    NOT NULL DEFAULT '0',
                    Active                  INTEGER NOT NULL DEFAULT 1,
                    Commentaire             TEXT,
                    DateCreation            TEXT    NOT NULL
                );");

            Executer(connection, @"
                CREATE TABLE IF NOT EXISTS EquipesMembres (
                    EmployeeId       TEXT    PRIMARY KEY,
                    EquipeId         INTEGER NOT NULL,
                    DateAffectation  TEXT,
                    FOREIGN KEY(EmployeeId) REFERENCES Employees(EmployeeId) ON DELETE CASCADE,
                    FOREIGN KEY(EquipeId) REFERENCES EquipesTravail(Id) ON DELETE CASCADE
                );");
            Executer(connection,
                "CREATE INDEX IF NOT EXISTS IX_EquipesMembres_Equipe ON EquipesMembres (EquipeId);");

            Executer(connection, @"
                CREATE TABLE IF NOT EXISTS PlanningJours (
                    Id                INTEGER PRIMARY KEY AUTOINCREMENT,
                    EmployeeId        TEXT    NOT NULL,
                    DateJour          TEXT    NOT NULL,
                    Code              TEXT    NOT NULL,
                    Heures            TEXT,
                    EquipeId          INTEGER,
                    Source            INTEGER NOT NULL DEFAULT 0,
                    Commentaire       TEXT,
                    DateModification  TEXT,
                    FOREIGN KEY(EmployeeId) REFERENCES Employees(EmployeeId) ON DELETE CASCADE
                );");
            Executer(connection,
                "CREATE UNIQUE INDEX IF NOT EXISTS UX_PlanningJours_Employe_Date ON PlanningJours (EmployeeId, DateJour);");
            Executer(connection,
                "CREATE INDEX IF NOT EXISTS IX_PlanningJours_Date ON PlanningJours (DateJour);");

            Executer(connection, @"
                CREATE TABLE IF NOT EXISTS JoursFeries (
                    Id       INTEGER PRIMARY KEY AUTOINCREMENT,
                    DateJour TEXT    NOT NULL UNIQUE,
                    Libelle  TEXT
                );");

            Executer(connection, @"
                CREATE TABLE IF NOT EXISTS ParametresTemps (
                    Id                 INTEGER PRIMARY KEY CHECK (Id = 1),
                    JourArrete         INTEGER NOT NULL DEFAULT 0,
                    AJCompteDansSeuil  INTEGER NOT NULL DEFAULT 0
                );");
            Executer(connection,
                "INSERT OR IGNORE INTO ParametresTemps (Id, JourArrete, AJCompteDansSeuil) VALUES (1, 0, 0);");
        }

        /// <summary>
        /// Migration 34 (suite) — rubrique « Prime de panier » alimentée par le relevé des
        /// temps (RH-08). Elle figure aussi dans <c>RubriqueService.RubriquesDeSaisie()</c>.
        /// Insérée seulement dans un plan déjà alimenté, pour ne jamais empêcher l'amorçage
        /// d'un plan vide ; en pratique les migrations 25 et 26 alimentent déjà tout plan,
        /// y compris celui d'un dossier neuf (voir ETAT-DEVELOPPEMENT.md, dette RH-08).
        /// Soumise à l'IUTS et à la CNSS par prudence : se corrige depuis « Plan de paie ».
        /// </summary>
        private void Migration34_RubriquePanier(SQLiteConnection connection)
        {
            if (!TableExiste(connection, "Rubriques"))
                return;

            using (var cmd = connection.CreateCommand())
            {
                cmd.CommandText = "SELECT COUNT(*) FROM Rubriques;";
                if (DbValue.IntFromDb(cmd.ExecuteScalar()) == 0)
                    return;
            }

            Executer(connection, @"
                INSERT OR IGNORE INTO Rubriques
                (Code, Libelle, Sens, ModeCalcul, MontantFixe, TauxSalarial, TauxPatronal,
                 CodeBase, SoumisIUTS, SoumisCNSS, Plafonne, Plafond, PourcentagePlafond,
                 Ordre, Active, EstSysteme, CompteComptable, CompteContrepartie, Commentaire)
                VALUES ('PANIER', 'Prime de panier', 0, 0, 0, '0', '0', '', 1, 1, 0, 0, '0',
                        62, 1, 0, '6612', '4221',
                        'Nombre de paniers (postes travaillés) en quantité. Alimentée par la Gestion des temps (RH-08).');");
        }

        /// <summary>
        /// Migration 35 — Discipline & procédures (module RH-06).
        ///
        /// Quatre tables : les paramètres de délai du dossier (une seule ligne), le catalogue
        /// des sanctions du règlement intérieur (livré avec cinq sanctions modifiables), les
        /// procédures disciplinaires et leurs pièces. Les dates nullables sont stockées en
        /// TEXT vide, comme ailleurs ; le nombre de jours de mise à pied en TEXT invariant.
        /// Aucune donnée existante n'est transformée.
        /// </summary>
        private void Migration35_Discipline(SQLiteConnection connection)
        {
            Executer(connection, @"
                CREATE TABLE IF NOT EXISTS ParametresDiscipline (
                    Id                              INTEGER PRIMARY KEY CHECK (Id = 1),
                    DelaiEngagementJours            INTEGER NOT NULL DEFAULT 60,
                    DelaiReponseJoursOuvrables      INTEGER NOT NULL DEFAULT 3,
                    DelaiConvocationJoursOuvrables  INTEGER NOT NULL DEFAULT 5,
                    DelaiDecisionJours              INTEGER NOT NULL DEFAULT 30,
                    ReferenceReglementInterieur     TEXT,
                    Signataire                      TEXT,
                    FonctionSignataire              TEXT
                );");
            Executer(connection, "INSERT OR IGNORE INTO ParametresDiscipline (Id) VALUES (1);");

            Executer(connection, @"
                CREATE TABLE IF NOT EXISTS TypesSanction (
                    Id                INTEGER PRIMARY KEY AUTOINCREMENT,
                    Code              TEXT    NOT NULL UNIQUE,
                    Libelle           TEXT    NOT NULL,
                    Nature            INTEGER NOT NULL DEFAULT 0,
                    Ordre             INTEGER NOT NULL DEFAULT 0,
                    ExigeExplication  INTEGER NOT NULL DEFAULT 1,
                    ExigeEntretien    INTEGER NOT NULL DEFAULT 0,
                    DureeMaxJours     INTEGER NOT NULL DEFAULT 0,
                    FauteLourde       INTEGER NOT NULL DEFAULT 0,
                    EffacementMois    INTEGER NOT NULL DEFAULT 0,
                    Actif             INTEGER NOT NULL DEFAULT 1,
                    EstSysteme        INTEGER NOT NULL DEFAULT 0
                );");

            int types;
            using (var cmd = connection.CreateCommand())
            {
                cmd.CommandText = "SELECT COUNT(*) FROM TypesSanction;";
                types = DbValue.IntFromDb(cmd.ExecuteScalar());
            }

            if (types == 0)
            {
                foreach (var t in TypeSanction.CatalogueParDefaut())
                {
                    using (var cmd = connection.CreateCommand())
                    {
                        cmd.CommandText =
                            "INSERT OR IGNORE INTO TypesSanction (Code, Libelle, Nature, Ordre, ExigeExplication, " +
                            "ExigeEntretien, DureeMaxJours, FauteLourde, EffacementMois, Actif, EstSysteme) " +
                            "VALUES (@c, @l, @n, @o, @ex, @en, @d, @fl, @ef, 1, 1);";
                        cmd.Parameters.AddWithValue("@c", t.Code);
                        cmd.Parameters.AddWithValue("@l", t.Libelle);
                        cmd.Parameters.AddWithValue("@n", (int)t.Nature);
                        cmd.Parameters.AddWithValue("@o", t.Ordre);
                        cmd.Parameters.AddWithValue("@ex", t.ExigeExplication ? 1 : 0);
                        cmd.Parameters.AddWithValue("@en", t.ExigeEntretien ? 1 : 0);
                        cmd.Parameters.AddWithValue("@d", t.DureeMaxJours);
                        cmd.Parameters.AddWithValue("@fl", t.FauteLourde ? 1 : 0);
                        cmd.Parameters.AddWithValue("@ef", t.EffacementMois);
                        cmd.ExecuteNonQuery();
                    }
                }
            }

            Executer(connection, @"
                CREATE TABLE IF NOT EXISTS ProceduresDisciplinaires (
                    Id                       INTEGER PRIMARY KEY AUTOINCREMENT,
                    Reference                TEXT    NOT NULL UNIQUE,
                    EmployeeId               TEXT    NOT NULL,
                    DateFaits                TEXT    NOT NULL,
                    DateConnaissance         TEXT    NOT NULL,
                    LieuFaits                TEXT,
                    CategorieFaits           TEXT,
                    DescriptionFaits         TEXT,
                    Temoins                  TEXT,
                    Gravite                  INTEGER NOT NULL DEFAULT 0,
                    SalarieProtege           INTEGER NOT NULL DEFAULT 0,
                    MandatProtege            TEXT,
                    DateDemandeExplication   TEXT,
                    DateReponse              TEXT,
                    ResumeReponse            TEXT,
                    ConservatoireDebut       TEXT,
                    ConservatoireFin         TEXT,
                    DateConvocation          TEXT,
                    DateEntretien            TEXT,
                    HeureLieuEntretien       TEXT,
                    AssistantEntretien       TEXT,
                    CompteRenduEntretien     TEXT,
                    DecisionInspection       INTEGER NOT NULL DEFAULT 0,
                    DateDemandeInspection    TEXT,
                    DateDecisionInspection   TEXT,
                    ReferenceInspection      TEXT,
                    TypeSanctionId           INTEGER,
                    DateDecision             TEXT,
                    MotifsDecision           TEXT,
                    MiseAPiedDebut           TEXT,
                    MiseAPiedFin             TEXT,
                    MiseAPiedJours           TEXT    NOT NULL DEFAULT '0',
                    DateEffetLicenciement    TEXT,
                    ClasseeSansSuite         INTEGER NOT NULL DEFAULT 0,
                    MotifClassement          TEXT,
                    DateNotification         TEXT,
                    ModeNotification         TEXT,
                    DateAccuseReception      TEXT,
                    DateRecours              TEXT,
                    InstanceRecours          TEXT,
                    IssueRecours             TEXT,
                    AbsenceMiseAPiedId       INTEGER,
                    AbsenceConservatoireId   INTEGER,
                    Commentaire              TEXT,
                    DateCreation             TEXT    NOT NULL,
                    DateModification         TEXT    NOT NULL,
                    CreePar                  TEXT
                );");
            Executer(connection,
                "CREATE INDEX IF NOT EXISTS IX_ProceduresDisciplinaires_Employe ON ProceduresDisciplinaires (EmployeeId);");

            Executer(connection, @"
                CREATE TABLE IF NOT EXISTS PiecesDisciplinaires (
                    Id            INTEGER PRIMARY KEY AUTOINCREMENT,
                    ProcedureId   INTEGER NOT NULL REFERENCES ProceduresDisciplinaires(Id) ON DELETE CASCADE,
                    Type          INTEGER NOT NULL DEFAULT 10,
                    Libelle       TEXT,
                    NomFichier    TEXT    NOT NULL,
                    NomOriginal   TEXT,
                    DateAjout     TEXT    NOT NULL,
                    EstGeneree    INTEGER NOT NULL DEFAULT 0
                );");
            Executer(connection,
                "CREATE INDEX IF NOT EXISTS IX_PiecesDisciplinaires_Procedure ON PiecesDisciplinaires (ProcedureId);");
        }

        /// <summary>
        /// Migration 36 (1/2) — plan de paie jamais amorcé.
        ///
        /// <c>RubriqueService.ChargerAsync</c> n'amorce le plan initial (salaire de base,
        /// indemnités, CNSS, IUTS, rubriques de saisie) que si la table est VIDE. Or les
        /// migrations 11, 25, 26, 27 et 34 y insèrent leurs propres rubriques sans regarder
        /// si le plan a été amorcé : un dossier créé après l'arrivée de PAI-07 (ou dont le
        /// plan n'avait jamais été ouvert avant ces migrations) se retrouvait avec RAPPEL*,
        /// AVNAT_*, PANIER… mais sans SALBASE ni HSUP, PRIME, ACOMPTE, PRET, etc. Les
        /// variables saisies sur ces codes étaient alors ignorées du bulletin.
        ///
        /// Signature sûre du défaut : l'absence de SALBASE. Une rubrique système ne se
        /// supprime jamais (elle se désactive), donc un plan amorcé contient toujours
        /// SALBASE et n'est pas touché. Dans un plan touché, le plan initial complet est
        /// inséré en INSERT OR IGNORE : une rubrique déjà présente, éventuellement modifiée
        /// par l'utilisateur, est conservée telle quelle.
        /// </summary>
        private void Migration36_PlanDePaieComplet(SQLiteConnection connection)
        {
            if (!TableExiste(connection, "Rubriques"))
                return;

            using (var cmd = connection.CreateCommand())
            {
                cmd.CommandText = "SELECT COUNT(*) FROM Rubriques WHERE Code = @c;";
                cmd.Parameters.AddWithValue("@c", RubriqueService.CodeSalaireBase);
                if (DbValue.IntFromDb(cmd.ExecuteScalar()) > 0)
                    return;
            }

            // Un plan entièrement vide reste à la charge de RubriqueService, comme avant :
            // on ne change pas le comportement des dossiers qui n'ont rien.
            using (var cmd = connection.CreateCommand())
            {
                cmd.CommandText = "SELECT COUNT(*) FROM Rubriques;";
                if (DbValue.IntFromDb(cmd.ExecuteScalar()) == 0)
                    return;
            }

            AjouterColonneSiAbsente(connection, "Rubriques", "SoumisTPA", "INTEGER NOT NULL DEFAULT 1");

            using (var tx = connection.BeginTransaction())
            {
                foreach (var r in RubriqueService.PlanDePaieInitial())
                {
                    using (var cmd = connection.CreateCommand())
                    {
                        cmd.Transaction = tx;
                        cmd.CommandText = @"
                            INSERT OR IGNORE INTO Rubriques
                            (Code, Libelle, Sens, ModeCalcul, MontantFixe, TauxSalarial, TauxPatronal, CodeBase,
                             SoumisIUTS, SoumisCNSS, SoumisTPA, Plafonne, Plafond, PourcentagePlafond, Ordre, Active,
                             EstSysteme, CompteComptable, CompteContrepartie, Commentaire)
                            VALUES
                            (@Code, @Libelle, @Sens, @ModeCalcul, @MontantFixe, @TauxSalarial, @TauxPatronal, @CodeBase,
                             @SoumisIUTS, @SoumisCNSS, @SoumisTPA, @Plafonne, @Plafond, @PourcentagePlafond, @Ordre, @Active,
                             @EstSysteme, @CompteComptable, @CompteContrepartie, @Commentaire);";
                        cmd.Parameters.AddWithValue("@Code", (r.Code ?? "").Trim().ToUpperInvariant());
                        cmd.Parameters.AddWithValue("@Libelle", r.Libelle ?? "");
                        cmd.Parameters.AddWithValue("@Sens", (int)r.Sens);
                        cmd.Parameters.AddWithValue("@ModeCalcul", (int)r.ModeCalcul);
                        cmd.Parameters.AddWithValue("@MontantFixe", DbValue.MoneyToDb(r.MontantFixe));
                        cmd.Parameters.AddWithValue("@TauxSalarial", DbValue.DecimalToDb(r.TauxSalarial));
                        cmd.Parameters.AddWithValue("@TauxPatronal", DbValue.DecimalToDb(r.TauxPatronal));
                        cmd.Parameters.AddWithValue("@CodeBase", r.CodeBase ?? "");
                        cmd.Parameters.AddWithValue("@SoumisIUTS", r.SoumisIUTS ? 1 : 0);
                        cmd.Parameters.AddWithValue("@SoumisCNSS", r.SoumisCNSS ? 1 : 0);
                        cmd.Parameters.AddWithValue("@SoumisTPA", r.SoumisTPA ? 1 : 0);
                        cmd.Parameters.AddWithValue("@Plafonne", r.Plafonne ? 1 : 0);
                        cmd.Parameters.AddWithValue("@Plafond", DbValue.MoneyToDb(r.Plafond));
                        cmd.Parameters.AddWithValue("@PourcentagePlafond", DbValue.DecimalToDb(r.PourcentagePlafond));
                        cmd.Parameters.AddWithValue("@Ordre", r.Ordre);
                        cmd.Parameters.AddWithValue("@Active", r.Active ? 1 : 0);
                        cmd.Parameters.AddWithValue("@EstSysteme", r.EstSysteme ? 1 : 0);
                        cmd.Parameters.AddWithValue("@CompteComptable", r.CompteComptable ?? "");
                        cmd.Parameters.AddWithValue("@CompteContrepartie", r.CompteContrepartie ?? "");
                        cmd.Parameters.AddWithValue("@Commentaire", r.Commentaire ?? "");
                        cmd.ExecuteNonQuery();
                    }
                }
                tx.Commit();
            }
        }

        /// <summary>
        /// Migration 36 (2/2) — registre des mandats représentatifs (RH-06).
        ///
        /// Un délégué du personnel ou un délégué syndical ne se licencie qu'avec
        /// l'autorisation de l'inspecteur du travail. Jusqu'ici, la qualité de salarié
        /// protégé se cochait à la main sur chaque procédure — un oubli suffisait à laisser
        /// passer un licenciement irrégulier. Le registre la rend automatique.
        /// </summary>
        private void Migration36_MandatsRepresentatifs(SQLiteConnection connection)
        {
            Executer(connection, @"
                CREATE TABLE IF NOT EXISTS MandatsRepresentatifs (
                    Id              INTEGER PRIMARY KEY AUTOINCREMENT,
                    EmployeeId      TEXT    NOT NULL,
                    Mandat          TEXT    NOT NULL,
                    DateDebut       TEXT    NOT NULL,
                    DateFin         TEXT,
                    FinProtection   TEXT,
                    Commentaire     TEXT
                );");
            Executer(connection,
                "CREATE INDEX IF NOT EXISTS IX_MandatsRepresentatifs_Employe ON MandatsRepresentatifs (EmployeeId);");
        }

        /// <summary>
        /// Migration 37 — Santé & sécurité au travail (module RH-07).
        ///
        /// Paramètres (une ligne), périodicité des visites propre à un poste, visites
        /// médicales, catalogue des EPI (amorcé avec les équipements courants, durée de vie
        /// 12 mois à ajuster), remises d'EPI, accidents du travail. Aucune donnée existante
        /// n'est transformée. La rémunération journalière d'un accident est un montant :
        /// INTEGER en centimes, comme ailleurs.
        /// </summary>
        private void Migration37_SanteSecurite(SQLiteConnection connection)
        {
            Executer(connection, @"
                CREATE TABLE IF NOT EXISTS ParametresSST (
                    Id                              INTEGER PRIMARY KEY CHECK (Id = 1),
                    PeriodiciteVisiteMois           INTEGER NOT NULL DEFAULT 12,
                    RepriseApresArretJours          INTEGER NOT NULL DEFAULT 21,
                    DelaiDeclarationJoursOuvrables  INTEGER NOT NULL DEFAULT 2,
                    TauxIndemniteJournaliere        TEXT    NOT NULL DEFAULT '0.6666666666666666666666666667',
                    JoursMoisReference              INTEGER NOT NULL DEFAULT 30,
                    AlerteAvanceJours               INTEGER NOT NULL DEFAULT 30
                );");
            Executer(connection, "INSERT OR IGNORE INTO ParametresSST (Id) VALUES (1);");

            Executer(connection, @"
                CREATE TABLE IF NOT EXISTS PeriodicitesVisitePoste (
                    PosteId  INTEGER PRIMARY KEY,
                    Mois     INTEGER NOT NULL
                );");

            Executer(connection, @"
                CREATE TABLE IF NOT EXISTS VisitesMedicales (
                    Id               INTEGER PRIMARY KEY AUTOINCREMENT,
                    EmployeeId       TEXT    NOT NULL,
                    Type             INTEGER NOT NULL DEFAULT 1,
                    DatePrevue       TEXT,
                    DateVisite       TEXT,
                    Medecin          TEXT,
                    Aptitude         INTEGER NOT NULL DEFAULT 0,
                    Restrictions     TEXT,
                    ProchaineVisite  TEXT,
                    AccidentId       INTEGER,
                    Commentaire      TEXT,
                    DateSaisie       TEXT    NOT NULL
                );");
            Executer(connection, "CREATE INDEX IF NOT EXISTS IX_VisitesMedicales_Employe ON VisitesMedicales (EmployeeId);");

            Executer(connection, @"
                CREATE TABLE IF NOT EXISTS TypesEPI (
                    Id            INTEGER PRIMARY KEY AUTOINCREMENT,
                    Libelle       TEXT    NOT NULL UNIQUE,
                    DureeVieMois  INTEGER NOT NULL DEFAULT 12,
                    Norme         TEXT,
                    Actif         INTEGER NOT NULL DEFAULT 1
                );");
            int nbEpi;
            using (var cmd = connection.CreateCommand())
            {
                cmd.CommandText = "SELECT COUNT(*) FROM TypesEPI;";
                nbEpi = DbValue.IntFromDb(cmd.ExecuteScalar());
            }
            if (nbEpi == 0)
            {
                foreach (var libelle in LibellesSST.EpiCourants)
                {
                    using (var cmd = connection.CreateCommand())
                    {
                        cmd.CommandText = "INSERT OR IGNORE INTO TypesEPI (Libelle, DureeVieMois, Actif) VALUES (@l, 12, 1);";
                        cmd.Parameters.AddWithValue("@l", libelle);
                        cmd.ExecuteNonQuery();
                    }
                }
            }

            Executer(connection, @"
                CREATE TABLE IF NOT EXISTS DotationsEPI (
                    Id                  INTEGER PRIMARY KEY AUTOINCREMENT,
                    EmployeeId          TEXT    NOT NULL,
                    TypeEpiId           INTEGER NOT NULL,
                    DateRemise          TEXT    NOT NULL,
                    Quantite            INTEGER NOT NULL DEFAULT 1,
                    Taille              TEXT,
                    DateRenouvellement  TEXT,
                    Etat                INTEGER NOT NULL DEFAULT 0,
                    DateRetour          TEXT,
                    Commentaire         TEXT
                );");
            Executer(connection, "CREATE INDEX IF NOT EXISTS IX_DotationsEPI_Employe ON DotationsEPI (EmployeeId);");

            Executer(connection, @"
                CREATE TABLE IF NOT EXISTS AccidentsTravail (
                    Id                         INTEGER PRIMARY KEY AUTOINCREMENT,
                    Reference                  TEXT    NOT NULL UNIQUE,
                    EmployeeId                 TEXT    NOT NULL,
                    Nature                     INTEGER NOT NULL DEFAULT 0,
                    DateAccident               TEXT    NOT NULL,
                    Heure                      TEXT,
                    Lieu                       TEXT,
                    Circonstances              TEXT,
                    SiegeLesion                TEXT,
                    NatureLesion               TEXT,
                    Temoins                    TEXT,
                    PremiersSoins              TEXT,
                    AvecArret                  INTEGER NOT NULL DEFAULT 0,
                    DebutArret                 TEXT,
                    FinArret                   TEXT,
                    DateReprise                TEXT,
                    Mortel                     INTEGER NOT NULL DEFAULT 0,
                    DateDeclarationCnss        TEXT,
                    DateDeclarationInspection  TEXT,
                    ReferenceCnss              TEXT,
                    SalaireJournalier          INTEGER NOT NULL DEFAULT 0,
                    TauxIpp                    TEXT    NOT NULL DEFAULT '0',
                    AbsenceId                  INTEGER,
                    Commentaire                TEXT,
                    DateCreation               TEXT    NOT NULL,
                    DateModification           TEXT    NOT NULL
                );");
            Executer(connection, "CREATE INDEX IF NOT EXISTS IX_AccidentsTravail_Employe ON AccidentsTravail (EmployeeId);");
        }

        /// <summary>
        /// Migration 38 — corrections de la paie relevées en préparant le guide de formation
        /// (version 1.4.37.1). Idempotente : la rejouer ne change rien.
        /// </summary>
        private void Migration38_CorrectionsPaie(SQLiteConnection connection)
        {
            // Défaut 7 — reprise du solde de congés à l'ouverture du dossier.
            if (TableExiste(connection, "Employees"))
            {
                AjouterColonneSiAbsente(connection, "Employees", "SoldeCongesReprise", "TEXT NOT NULL DEFAULT '0'");
                AjouterColonneSiAbsente(connection, "Employees", "DateRepriseConges", "TEXT");
            }

            // Défaut 25 — remboursements de frais et frais de mission : ni salaire ni
            // avantage, donc hors assiette de la TPA. La case TPA restait cochée d'office.
            // Seules les rubriques encore dans leur réglage d'origine (ni IUTS ni CNSS) sont
            // corrigées : un dossier qui a rendu ses frais imposables garde son choix.
            // N'agit que sur les calculs futurs : les bulletins archivés gardent leur TPA.
            if (TableExiste(connection, "Rubriques"))
            {
                AjouterColonneSiAbsente(connection, "Rubriques", "SoumisTPA", "INTEGER NOT NULL DEFAULT 1");
                Executer(connection, @"
                    UPDATE Rubriques SET SoumisTPA = 0
                    WHERE Code IN ('REMBFRAIS', 'FRAISMISS')
                      AND SoumisTPA = 1 AND SoumisIUTS = 0 AND SoumisCNSS = 0;");

                // Défaut 1 — rubriques du bulletin de sortie (solde de tout compte). Ajoutées
                // sans rien écraser : un dossier qui les a déjà créées garde son réglage. Un
                // plan entièrement vide reste amorcé par RubriqueService (il les contient).
                bool planExiste;
                using (var cmd = connection.CreateCommand())
                {
                    cmd.CommandText = "SELECT COUNT(*) FROM Rubriques;";
                    planExiste = DbValue.IntFromDb(cmd.ExecuteScalar()) > 0;
                }

                if (planExiste)
                {
                    var codes = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "INDCONGES", "INDDEPART", "INDDIVERS", "RETSORTIE" };
                    using (var tx = connection.BeginTransaction())
                    {
                        foreach (var r in RubriqueService.PlanDePaieInitial())
                        {
                            if (r == null || !codes.Contains(r.Code ?? "")) continue;
                            using (var cmd = connection.CreateCommand())
                            {
                                cmd.Transaction = tx;
                                cmd.CommandText = @"
                                    INSERT OR IGNORE INTO Rubriques
                                    (Code, Libelle, Sens, ModeCalcul, MontantFixe, TauxSalarial, TauxPatronal, CodeBase,
                                     SoumisIUTS, SoumisCNSS, SoumisTPA, Plafonne, Plafond, PourcentagePlafond, Ordre, Active,
                                     EstSysteme, CompteComptable, CompteContrepartie, Commentaire)
                                    VALUES
                                    (@Code, @Libelle, @Sens, @ModeCalcul, 0, '0', '0', '',
                                     @SoumisIUTS, @SoumisCNSS, @SoumisTPA, 0, 0, '0', @Ordre, 1,
                                     0, @CompteComptable, @CompteContrepartie, @Commentaire);";
                                cmd.Parameters.AddWithValue("@Code", r.Code.Trim().ToUpperInvariant());
                                cmd.Parameters.AddWithValue("@Libelle", r.Libelle ?? "");
                                cmd.Parameters.AddWithValue("@Sens", (int)r.Sens);
                                cmd.Parameters.AddWithValue("@ModeCalcul", (int)r.ModeCalcul);
                                cmd.Parameters.AddWithValue("@SoumisIUTS", r.SoumisIUTS ? 1 : 0);
                                cmd.Parameters.AddWithValue("@SoumisCNSS", r.SoumisCNSS ? 1 : 0);
                                cmd.Parameters.AddWithValue("@SoumisTPA", r.SoumisTPA ? 1 : 0);
                                cmd.Parameters.AddWithValue("@Ordre", r.Ordre);
                                cmd.Parameters.AddWithValue("@CompteComptable", r.CompteComptable ?? "");
                                cmd.Parameters.AddWithValue("@CompteContrepartie", r.CompteContrepartie ?? "");
                                cmd.Parameters.AddWithValue("@Commentaire", r.Commentaire ?? "");
                                cmd.ExecuteNonQuery();
                            }
                        }
                        tx.Commit();
                    }
                }
            }
        }

        /// <summary>
        /// Migration 3 — plan de paie.
        ///
        /// Ajoute la table des rubriques aux dossiers existants. Aucune donnée n'est
        /// transformée : le plan de paie initial est alimenté à la première ouverture de
        /// l'écran, par <c>RubriqueService</c>, à partir des éléments déjà gérés par le
        /// moteur de calcul.
        /// </summary>
        private void Migration3_PlanDePaie(SQLiteConnection connection)
        {
            CreerTableRubriques(connection);
        }

        /// <summary>
        /// Barèmes légaux datés (module SOC-02).
        ///
        /// Trois tables : l'en-tête du barème avec sa date d'effet et ses taux scalaires,
        /// ses tranches d'IUTS, et ses réductions pour charges de famille. Les deux tables
        /// filles sont rattachées par <c>BaremeId</c> et supprimées en cascade.
        ///
        /// Conventions de stockage, identiques au reste de la base : les MONTANTS et les
        /// BORNES sont des INTEGER en centimes ; les TAUX, qui portent des décimales
        /// significatives, sont des TEXT en culture invariante.
        /// </summary>
        private void CreerTablesBaremes(SQLiteConnection connection)
        {
            Executer(connection, @"
                CREATE TABLE IF NOT EXISTS Baremes (
                    Id                                  INTEGER PRIMARY KEY AUTOINCREMENT,
                    DateEffet                           TEXT NOT NULL,
                    Libelle                             TEXT,
                    Reference                           TEXT,
                    Commentaire                         TEXT,
                    EstSysteme                          INTEGER NOT NULL DEFAULT 0,
                    TauxCnssSalarie                     TEXT,
                    TauxCnssPrestationsFamiliales       TEXT,
                    TauxCnssRisquesProfessionnels       TEXT,
                    TauxCnssPensionsPatronal            TEXT,
                    PlafondCnssMensuel                  INTEGER NOT NULL DEFAULT 0,
                    TauxTaxePatronaleApprentissage      TEXT,
                    TauxMaxIndemniteLogement            TEXT,
                    PlafondIndemniteLogement            INTEGER NOT NULL DEFAULT 0,
                    TauxMaxIndemniteTransport           TEXT,
                    PlafondIndemniteTransport           INTEGER NOT NULL DEFAULT 0,
                    TauxMaxIndemniteFonction            TEXT,
                    PlafondIndemniteFonction            INTEGER NOT NULL DEFAULT 0,
                    TauxMaxCotisationRetraiteDeductible TEXT,
                    AbattementCategoriesSuperieures     TEXT,
                    AbattementAutresCategories          TEXT
                );");

            Executer(connection,
                "CREATE INDEX IF NOT EXISTS IX_Baremes_DateEffet ON Baremes(DateEffet);");

            Executer(connection, @"
                CREATE TABLE IF NOT EXISTS BaremeTranches (
                    Id              INTEGER PRIMARY KEY AUTOINCREMENT,
                    BaremeId        INTEGER NOT NULL,
                    Ordre           INTEGER NOT NULL DEFAULT 0,
                    BorneInferieure INTEGER NOT NULL DEFAULT 0,
                    BorneSuperieure INTEGER NOT NULL DEFAULT 0,
                    Taux            TEXT,
                    FOREIGN KEY(BaremeId) REFERENCES Baremes(Id) ON DELETE CASCADE
                );");

            Executer(connection,
                "CREATE INDEX IF NOT EXISTS IX_BaremeTranches_Bareme ON BaremeTranches(BaremeId, Ordre);");

            Executer(connection, @"
                CREATE TABLE IF NOT EXISTS BaremeChargesFamille (
                    Id            INTEGER PRIMARY KEY AUTOINCREMENT,
                    BaremeId      INTEGER NOT NULL,
                    NombreCharges INTEGER NOT NULL DEFAULT 0,
                    Taux          TEXT,
                    FOREIGN KEY(BaremeId) REFERENCES Baremes(Id) ON DELETE CASCADE
                );");

            Executer(connection,
                "CREATE INDEX IF NOT EXISTS IX_BaremeChargesFamille_Bareme ON BaremeChargesFamille(BaremeId, NombreCharges);");
        }

        /// <summary>
        /// Migration 4 — barèmes légaux datés.
        ///
        /// Ajoute les trois tables aux dossiers existants. Aucune donnée n'est transformée
        /// et aucun calcul déjà enregistré n'est retouché : le barème en vigueur est
        /// alimenté à la première ouverture de l'écran, par <c>BaremeService</c>, avec
        /// exactement les valeurs que le moteur applique aujourd'hui.
        /// </summary>
        private void Migration4_BaremesDates(SQLiteConnection connection)
        {
            CreerTablesBaremes(connection);
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

        /// <summary>
        /// Migration 38 (RH-07, défaut n° 13) — suivi du remboursement des indemnités journalières
        /// par la CNSS : dépôt du dossier, montant et date du remboursement, sur la fiche de
        /// l'accident. Idempotente ; base neuve : la table vient d'être créée par la migration 37.
        /// </summary>
        private void Migration38_SanteSecurite(SQLiteConnection connection)
        {
            AjouterColonneSiAbsente(connection, "AccidentsTravail", "DateDepotIJ", "TEXT");
            AjouterColonneSiAbsente(connection, "AccidentsTravail", "MontantRembourseIJ", "INTEGER NOT NULL DEFAULT 0");
            AjouterColonneSiAbsente(connection, "AccidentsTravail", "DateRemboursementIJ", "TEXT");
        }

        /// <summary>
        /// Migration 38 — RH-03 Entretiens : auteur de la validation d'un entretien
        /// (« Valider &amp; Sceller »), à côté de la date de validation déjà existante.
        /// Idempotente. Les entretiens validés avant cette version gardent une date et un auteur
        /// vides : aucune source fiable ne permet de les reconstituer, rien n'est réécrit.
        /// </summary>
        private void Migration38_Entretiens(SQLiteConnection connection)
        {
            AjouterColonneSiAbsente(connection, "EntretiensEvaluation", "ValidePar", "TEXT");
        }

        /// <summary>
        /// Migration 38 — RH-05 Recrutement : conditions d'embauche reprises sur la fiche salarié
        /// (type de contrat, indemnités proposées par l'offre, en centimes comme tous les montants).
        /// Idempotente ; aucune donnée existante n'est modifiée.
        /// </summary>
        private void Migration38_Recrutement(SQLiteConnection connection)
        {
            AjouterColonneSiAbsente(connection, "Employees", "TypeContrat", "TEXT");
            AjouterColonneSiAbsente(connection, "RecrutementCandidatures", "TypeContratEmbauche", "TEXT");
            AjouterColonneSiAbsente(connection, "RecrutementBesoins", "IndemniteLogement", "INTEGER NOT NULL DEFAULT 0");
            AjouterColonneSiAbsente(connection, "RecrutementBesoins", "IndemniteTransport", "INTEGER NOT NULL DEFAULT 0");
            AjouterColonneSiAbsente(connection, "RecrutementBesoins", "IndemniteFonction", "INTEGER NOT NULL DEFAULT 0");
            AjouterColonneSiAbsente(connection, "RecrutementBesoins", "IndemniteSujetion", "INTEGER NOT NULL DEFAULT 0");
        }

        /// <summary>
        /// Migration 38 — fiches de poste (RH-02, défaut 15) : signale les niveaux de compétence
        /// enregistrés avant la correction du sélecteur, SANS les réécrire (une valeur 1 pouvait
        /// venir de « Niv 1 » comme de « Niv 2 »). Idempotente.
        /// </summary>
        private void Migration38_FichesDePostes(SQLiteConnection connection)
        {
            if (!TableExiste(connection, "PosteCompetences"))
                return;

            // Déjà passée : rien n'est refait (sinon des niveaux confirmés depuis redeviendraient « à vérifier »).
            foreach (var colonne in ColonnesDe(connection, "PosteCompetences"))
            {
                if (string.Equals(colonne, "NiveauAVerifier", StringComparison.OrdinalIgnoreCase))
                    return;
            }

            AjouterColonneSiAbsente(connection, "PosteCompetences", "NiveauAVerifier", "INTEGER NOT NULL DEFAULT 0");

            // Les exigences existantes ont été saisies avec l'ancien sélecteur (seul chemin d'écriture de la table).
            Executer(connection, "UPDATE PosteCompetences SET NiveauAVerifier = 1;");
        }

        /// <summary>
        /// Migration 38 — écritures de paie (défaut 20) : comptes et paramètres d'export réglables
        /// par dossier. Valeurs initiales = anciennes valeurs codées en dur (aucun résultat ne
        /// change tant que l'utilisateur ne modifie rien). Idempotente.
        /// </summary>
        private void Migration38_ParametresComptables(SQLiteConnection connection)
        {
            Executer(connection, @"
                CREATE TABLE IF NOT EXISTS ParametresComptables (
                    Cle     TEXT PRIMARY KEY,
                    Valeur  TEXT NOT NULL DEFAULT ''
                );");
            foreach (var valeur in ParametresComptables.ValeursInitiales())
            {
                using (var cmd = connection.CreateCommand())
                {
                    cmd.CommandText = "INSERT OR IGNORE INTO ParametresComptables (Cle, Valeur) VALUES (@cle, @valeur);";
                    cmd.Parameters.AddWithValue("@cle", valeur.Key);
                    cmd.Parameters.AddWithValue("@valeur", valeur.Value);
                    cmd.ExecuteNonQuery();
                }
            }
        }

        /// <summary>
        /// Migration 38 — journal d'audit scellé (défaut 21) : chaînage SHA-256 des entrées
        /// postérieures à la migration, borne des entrées antérieures (« non scellées »), et
        /// déclencheurs interdisant la modification ou la suppression d'une entrée. Idempotente.
        /// </summary>
        private void Migration38_AuditScelle(SQLiteConnection connection)
        {
            if (!TableExiste(connection, "JournalAudit"))
                Migration20_JournalAudit(connection);

            Executer(connection, @"
                CREATE TABLE IF NOT EXISTS JournalAuditScellement (
                    Id                  INTEGER PRIMARY KEY CHECK (Id = 1),
                    DateScellement      TEXT    NOT NULL,
                    DernierIdNonScelle  INTEGER NOT NULL
                );");
            Executer(connection, @"
                INSERT OR IGNORE INTO JournalAuditScellement (Id, DateScellement, DernierIdNonScelle)
                VALUES (1,
                        strftime('%Y-%m-%d %H:%M:%S', 'now', 'localtime'),
                        MAX(COALESCE((SELECT MAX(Id) FROM JournalAudit), 0),
                            COALESCE((SELECT seq FROM sqlite_sequence WHERE name = 'JournalAudit'), 0)));");

            AjouterColonneSiAbsente(connection, "JournalAudit", "Empreinte", "TEXT");
            AjouterColonneSiAbsente(connection, "JournalAudit", "EmpreintePrecedente", "TEXT");

            Executer(connection, @"
                CREATE TRIGGER IF NOT EXISTS TR_JournalAudit_SansModification
                BEFORE UPDATE ON JournalAudit
                BEGIN
                    SELECT RAISE(ABORT, 'Journal d''audit scellé : une entrée enregistrée ne peut pas être modifiée.');
                END;");
            Executer(connection, @"
                CREATE TRIGGER IF NOT EXISTS TR_JournalAudit_SansSuppression
                BEFORE DELETE ON JournalAudit
                BEGIN
                    SELECT RAISE(ABORT, 'Journal d''audit scellé : une entrée enregistrée ne peut pas être supprimée.');
                END;");
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

    /// <summary>
    /// La base du dossier a été mise à niveau par une version plus récente du programme.
    /// L'ouvrir avec celle-ci risquerait d'enregistrer des données incomplètes : il faut
    /// installer la version la plus récente (ou restaurer une sauvegarde antérieure).
    /// </summary>
    public class BaseTropRecenteException : InvalidOperationException
    {
        public BaseTropRecenteException(string chemin, int versionBase, int versionProgramme)
            : base(string.Format(CultureInfo.InvariantCulture,
                "Ce dossier a été mis à jour par une version plus récente de Sigma Paie & RH (base de données v{0} ; " +
                "ce programme gère jusqu'à la v{1}). Pour ne rien perdre, il ne sera pas ouvert avec cette version : " +
                "installez la dernière version du programme. Fichier : {2}", versionBase, versionProgramme, chemin))
        {
            Chemin = chemin;
            VersionBase = versionBase;
            VersionProgramme = versionProgramme;
        }

        public string Chemin { get; }
        public int VersionBase { get; }
        public int VersionProgramme { get; }
    }
}
