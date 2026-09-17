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
        private const int SchemaCible = 29;

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
