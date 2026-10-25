using System;
using System.Collections.Generic;
using System.Linq;
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
        private bool _isNavRailVisible = true;

        // Bulletin de paie (écran de consultation + export PDF/Excel)
        private readonly BulletinViewModel _bulletinViewModel;
        // Bulletins de paie en masse (sélection multi-employés + export PDF en lot)
        private readonly BulletinBatchViewModel _bulletinBatchViewModel;
        // Plan de paie : table des rubriques paramétrables (module SOC-01)
        private readonly RubriqueViewModel _rubriqueViewModel;
        private readonly BaremeViewModel _baremeViewModel;
        private readonly PeriodePaieViewModel _periodePaieViewModel;
        private readonly DeclarationCnssViewModel _declarationCnssViewModel;
        private readonly DeclarationIutsTpaViewModel _declarationIutsTpaViewModel;
        private readonly ControleEquivalenceViewModel _controleEquivalenceViewModel;
        private readonly VariablePaieViewModel _variablePaieViewModel;
        private readonly AbsenceViewModel _absenceViewModel;
        // Prêts, avances et acomptes (module PAI-05)
        private readonly PretViewModel _pretViewModel;
        // Profils de paie (module SOC-03)
        private readonly ProfilPaieViewModel _profilPaieViewModel;
        // Livre de paie (module ETA-01)
        private readonly LivrePaieViewModel _livrePaieViewModel;
        // État annuel des salaires — DADS (module DEC-05)
        private readonly EtatAnnuelSalairesViewModel _etatAnnuelSalairesViewModel;
        private readonly TableauBordMasseSalarialeViewModel _tableauBordMasseSalarialeViewModel;

        // Écritures de paie SYSCOHADA (module CPT-01)
        private readonly EcriturePaieViewModel _ecriturePaieViewModel;
        // Ordres de virement bancaire (module CPT-02)
        private readonly OrdreVirementViewModel _ordreVirementViewModel;

        // Journal d'audit et traçabilité des opérations (module ADM-02)
        private readonly AuditService _auditService;
        private readonly AuditExportService _auditExportService;
        private readonly AuditViewModel _auditViewModel;

        // Heures supplémentaires et majorations légales (module PAI-03)
        private readonly HeureSupplementaireService _heureSupplementaireService;
        private readonly HeuresSupExportService _heuresSupExportService;
        private readonly HeuresSupplementairesViewModel _heuresSupplementairesViewModel;

        // Conventions collectives et grilles salariales (module SOC-05)
        private readonly ConventionCollectiveService _conventionCollectiveService;
        private readonly ConventionCollectiveViewModel _conventionCollectiveViewModel;

        // Solde de tout compte et gestion des départs (module SAL-05)
        private readonly SoldeToutCompteService _soldeToutCompteService;
        private readonly SoldeToutCompteExportService _soldeToutCompteExportService;
        private readonly SoldeToutCompteViewModel _soldeToutCompteViewModel;

        // Saisies-arrêts, cessions et oppositions (module PAI-06)
        private readonly SaisieOppositionService _saisieOppositionService;
        private readonly SaisieOppositionExportService _saisieOppositionExportService;
        private readonly SaisieOppositionViewModel _saisieOppositionViewModel;

        // Rappels et régularisations (module PAI-07)
        private readonly RappelPaieService _rappelPaieService;
        private readonly RappelPaieExportService _rappelPaieExportService;
        private readonly RappelPaieViewModel _rappelPaieViewModel;

        // Avantages en nature (module PAI-08)
        private readonly AvantageNatureService _avantageNatureService;
        private readonly AvantageNatureExportService _avantageNatureExportService;
        private readonly AvantageNatureViewModel _avantageNatureViewModel;

        // Organigramme d'entreprise et rattachements hiérarchiques (module RH-01)
        private readonly OrganigrammeService _organigrammeService;
        private readonly OrganigrammeExportService _organigrammeExportService;
        private readonly OrganigrammeViewModel _organigrammeViewModel;

        // Suivi budgétaire des postes & compétences (module RH-02)
        private readonly FichePosteService _fichePosteService;
        private readonly FichePosteExportService _fichePosteExportService;
        private readonly SuiviPostesBudgetViewModel _suiviPostesBudgetViewModel;

        // Entretiens & évaluations (module RH-03)
        private readonly EvaluationService _evaluationService;
        private readonly EvaluationExportService _evaluationExportService;
        private readonly EntretiensEvaluationsViewModel _entretiensEvaluationsViewModel;

        // Formation & TPA (module RH-04)
        private readonly FormationService _formationService;
        private readonly FormationExportService _formationExportService;
        private readonly FormationViewModel _formationViewModel;

        // Recrutement & Candidatures (module RH-05)
        private readonly RecrutementService _recrutementService;
        private readonly RecrutementExportService _recrutementExportService;
        private readonly RecrutementViewModel _recrutementViewModel;

        // Discipline & procédures (module RH-06)
        private readonly DisciplineService _disciplineService;
        private readonly DisciplineViewModel _disciplineViewModel;

        // Santé & sécurité au travail (module RH-07)
        private readonly SanteSecuriteService _santeSecuriteService;
        private readonly SanteSecuriteViewModel _santeSecuriteViewModel;

        // Gestion des temps & rotations (module RH-08)
        private readonly GestionTempsService _gestionTempsService;
        private readonly GestionTempsViewModel _gestionTempsViewModel;

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
        public ICommand NavigateToRubriqueCommand { get; private set; }
        public ICommand NavigateToBaremeCommand { get; private set; }
        public ICommand NavigateToPeriodePaieCommand { get; private set; }
        public ICommand NavigateToDeclarationCnssCommand { get; private set; }
        public ICommand NavigateToDeclarationIutsTpaCommand { get; private set; }
        public ICommand NavigateToLivrePaieCommand { get; private set; }
        public ICommand NavigateToEtatAnnuelSalairesCommand { get; private set; }
        public ICommand NavigateToTableauBordMasseSalarialeCommand { get; private set; }
        public ICommand NavigateToEcriturePaieCommand { get; private set; }
        public ICommand NavigateToOrdreVirementCommand { get; private set; }
        public ICommand NavigateToControleMoteurCommand { get; private set; }
        public ICommand NavigateToVariablesCommand { get; private set; }
        public ICommand NavigateToAbsencesCommand { get; private set; }
        public ICommand NavigateToPretsCommand { get; private set; }
        public ICommand NavigateToProfilsCommand { get; private set; }
        public ICommand NavigateToAuditCommand { get; private set; }
        public ICommand NavigateToHeuresSupCommand { get; private set; }
        public ICommand NavigateToConventionsCommand { get; private set; }
        public ICommand NavigateToSoldeToutCompteCommand { get; private set; }
        public ICommand NavigateToSaisiesCommand { get; private set; }
        public ICommand NavigateToRappelsCommand { get; private set; }
        public ICommand NavigateToAvantagesNatureCommand { get; private set; }
        public ICommand NavigateToOrganigrammeCommand { get; private set; }
        public ICommand NavigateToSuiviPostesCommand { get; private set; }
        public ICommand NavigateToEntretiensCommand { get; private set; }
        public ICommand NavigateToFormationCommand { get; private set; }
        public ICommand NavigateToRecrutementCommand { get; private set; }
        public ICommand NavigateToGestionTempsCommand { get; private set; }
        public ICommand NavigateToDisciplineCommand { get; private set; }
        public ICommand NavigateToSanteSecuriteCommand { get; private set; }

        /// <summary>
        /// Indique si la barre de menu latéral (rail de navigation) est affichée.
        /// Permet de la masquer pour libérer de l'espace sur les petits écrans.
        /// </summary>
        public bool IsNavRailVisible
        {
            get => _isNavRailVisible;
            set => SetProperty(ref _isNavRailVisible, value);
        }

        public ICommand ToggleNavRailCommand { get; private set; }
        public ICommand ChangeDossierCommand { get; private set; }

        #region Recherche et Filtrage du Menu Latéral

        private string _navMenuSearchText = string.Empty;
        private readonly List<NavMenuItemDefinition> _navMenuItems = new List<NavMenuItemDefinition>();
        private readonly HashSet<string> _visibleNavItems = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _visibleNavGroups = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Texte saisi dans la barre de recherche du menu latéral
        /// </summary>
        public string NavMenuSearchText
        {
            get => _navMenuSearchText;
            set
            {
                if (SetProperty(ref _navMenuSearchText, value))
                {
                    ApplyNavMenuFilter();
                }
            }
        }

        public bool HasNavMenuSearchText => !string.IsNullOrWhiteSpace(_navMenuSearchText);
        public bool HasNavMenuMatches => _visibleNavItems.Count > 0;
        public bool ShowNavNoMatches => HasNavMenuSearchText && !HasNavMenuMatches;
        public string NavMenuMatchCountText => _visibleNavItems.Count == 1 ? "1 module trouvé" : $"{_visibleNavItems.Count} modules trouvés";

        public ICommand ClearNavMenuSearchCommand { get; private set; }
        public ICommand NavigateToFirstNavMatchCommand { get; private set; }

        // Visibilité des éléments individuels de navigation
        public bool IsNavBulletinVisible => IsItemVisible("Bulletin");
        public bool IsNavBulletinBatchVisible => IsItemVisible("BulletinBatch");
        public bool IsNavSoldeToutCompteVisible => IsItemVisible("SoldeToutCompte");

        public bool IsNavCalculVisible => IsItemVisible("CalculIuts");
        public bool IsNavCalculBatchVisible => IsItemVisible("CalculBatch");
        public bool IsNavEstimationVisible => IsItemVisible("EstimationSalaire");
        public bool IsNavEstimationBatchVisible => IsItemVisible("EstimationBatch");

        public bool IsNavEmployesVisible => IsItemVisible("Employes");
        public bool IsNavHistoriqueVisible => IsItemVisible("Historique");

        public bool IsNavCnssVisible => IsItemVisible("DeclarationCnss");
        public bool IsNavIutsTpaVisible => IsItemVisible("DeclarationIutsTpa");
        public bool IsNavLivrePaieVisible => IsItemVisible("LivrePaie");
        public bool IsNavEtatAnnuelSalairesVisible => IsItemVisible("EtatAnnuelSalaires");
        public bool IsNavTableauBordMasseSalarialeVisible => IsItemVisible("TableauBordMasseSalariale");
        public bool IsNavEcriturePaieVisible => IsItemVisible("EcriturePaie");
        public bool IsNavOrdreVirementVisible => IsItemVisible("OrdreVirement");

        public bool IsNavRubriqueVisible => IsItemVisible("Rubrique");
        public bool IsNavProfilsVisible => IsItemVisible("Profils");
        public bool IsNavVariablesVisible => IsItemVisible("Variables");
        public bool IsNavHeuresSupVisible => IsItemVisible("HeuresSup");
        public bool IsNavAbsencesVisible => IsItemVisible("Absences");
        public bool IsNavPretsVisible => IsItemVisible("Prets");
        public bool IsNavSaisiesVisible => IsItemVisible("Saisies");
        public bool IsNavRappelsVisible => IsItemVisible("Rappels");
        public bool IsNavAvantagesNatureVisible => IsItemVisible("AvantagesNature");
        public bool IsNavPeriodePaieVisible => IsItemVisible("PeriodePaie");
        public bool IsNavControleMoteurVisible => IsItemVisible("ControleMoteur");
        public bool IsNavBaremeVisible => IsItemVisible("Bareme");
        public bool IsNavConventionsVisible => IsItemVisible("Conventions");
        public bool IsNavOrganigrammeVisible => IsItemVisible("Organigramme");
        public bool IsNavSuiviPostesVisible => IsItemVisible("SuiviPostes");
        public bool IsNavEntretiensVisible => IsItemVisible("Entretiens");
        public bool IsNavFormationVisible => IsItemVisible("Formation");
        public bool IsNavRecrutementVisible => IsItemVisible("Recrutement");
        public bool IsNavGestionTempsVisible => IsItemVisible("GestionTemps");
        public bool IsNavDisciplineVisible => IsItemVisible("Discipline");
        public bool IsNavSanteSecuriteVisible => IsItemVisible("SanteSecurite");

        public bool IsNavAuditVisible => IsItemVisible("Audit");
        public bool IsNavSettingsVisible => IsItemVisible("Parametres");

        // Visibilité des en-têtes de domaine (groupes)
        public bool IsNavGroupPaieVisible => IsGroupVisible("PAIE");
        public bool IsNavGroupCalculVisible => IsGroupVisible("CALCUL");
        public bool IsNavGroupDonneesVisible => IsGroupVisible("DONNEES");
        public bool IsNavGroupDeclarationsVisible => IsGroupVisible("DECLARATIONS");
        public bool IsNavGroupParametrageVisible => IsGroupVisible("PARAMETRAGE");
        public bool IsNavGroupAuditVisible => IsGroupVisible("AUDIT");
        public bool IsNavGroupPilotageVisible => IsGroupVisible("PILOTAGE");
        public bool IsNavGroupRhVisible => IsGroupVisible("RH");

        private bool IsItemVisible(string key) => _visibleNavItems.Contains(key);
        private bool IsGroupVisible(string groupKey) => _visibleNavGroups.Contains(groupKey);

        #endregion

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
            // Base partagée par les écrans de calcul : variables du mois, plan de paie et
            // absences. Les mêmes entrées que le bulletin — sans quoi deux écrans donnent
            // deux résultats pour le même salarié et le même mois.
            var sqliteCalculs = new SQLiteService(_currentDossier.DbFileName);

            // _calculationViewModel et _batchSalaryCalculationViewModel sont construits plus
            // bas (après les services PAI-03/SOC-05/PAI-07/PAI-08), pour recevoir les mêmes
            // quatre modules que les écrans de bulletin — voir ce bloc pour le détail.
            _historyViewModel = new HistoryViewModel(_dataService, _taxCalculatorService);

            // Paramètres : édite désormais le même barème IUTS daté que l'écran « Barèmes
            // légaux » (voir ETAT-DEVELOPPEMENT.md item 23), d'où sa propre instance de
            // BaremeService — même schéma que RubriqueViewModel/BaremeViewModel ci-dessous :
            // chaque écran ouvre sa propre connexion, l'ouverture porte le schéma à niveau.
            //
            // RattrapageCnssService (dette connue n°3) : branché ici sur un panneau
            // temporaire de cet écran, seul point d'entrée de l'application vers ce
            // service — voir ETAT-DEVELOPPEMENT.md item 3 et la documentation du service
            // lui-même pour ce qu'il corrige et pourquoi ce n'est PAS automatique.
            _settingsViewModel = new SettingsViewModel(
                _dataService,
                new BaremeService(new SQLiteService(_currentDossier.DbFileName)),
                new RattrapageCnssService(new SQLiteService(_currentDossier.DbFileName)));
            _baseSalaryEstimatorViewModel = new BaseSalaryEstimatorViewModel(_dataService, _taxCalculatorService);
            _batchSalaryEstimationViewModel = new BatchSalaryEstimationViewModel(_taxCalculatorService);

            // Journal d'audit et traçabilité des opérations (module ADM-02).
            _auditService = new AuditService(new SQLiteService(_currentDossier.DbFileName));
            _auditExportService = new AuditExportService();
            _auditViewModel = new AuditViewModel(_auditService, _auditExportService, _currentDossier);

            // Heures supplémentaires et majorations légales (module PAI-03).
            var sqliteHeuresSup = new SQLiteService(_currentDossier.DbFileName);
            _heureSupplementaireService = new HeureSupplementaireService(
                sqliteHeuresSup,
                _auditService,
                new PeriodePaieService(sqliteHeuresSup));
            _heuresSupExportService = new HeuresSupExportService();
            _heuresSupplementairesViewModel = new HeuresSupplementairesViewModel(
                _heureSupplementaireService,
                _heuresSupExportService,
                new PeriodePaieService(sqliteHeuresSup));

            // Conventions collectives et grilles salariales (module SOC-05).
            // Doit être instancié AVANT les viewmodels de bulletins pour valoriser la prime d'ancienneté.
            var sqliteConventions = new SQLiteService(_currentDossier.DbFileName);
            _conventionCollectiveService = new ConventionCollectiveService(sqliteConventions, _auditService);
            _conventionCollectiveViewModel = new ConventionCollectiveViewModel(_conventionCollectiveService, _dataService);

            // Base commune aux écrans de bulletin, pour les variables du mois et le plan de
            // paie : un bulletin qui ignorerait les variables saisies afficherait un brut
            // faux, et le salarié n'aurait aucun moyen de s'en apercevoir.
            var sqliteBulletins = new SQLiteService(_currentDossier.DbFileName);

            // Saisies-arrêts, cessions et oppositions (module PAI-06)
            var sqliteSaisies = new SQLiteService(_currentDossier.DbFileName);
            _saisieOppositionService = new SaisieOppositionService(sqliteSaisies);
            _saisieOppositionExportService = new SaisieOppositionExportService();
            _saisieOppositionViewModel = new SaisieOppositionViewModel(
                _saisieOppositionService,
                _saisieOppositionExportService,
                _dataService);

            // Rappels et régularisations (module PAI-07)
            var sqliteRappels = new SQLiteService(_currentDossier.DbFileName);
            _rappelPaieService = new RappelPaieService(sqliteRappels, _taxCalculatorService, _auditService);
            _rappelPaieExportService = new RappelPaieExportService();
            _rappelPaieViewModel = new RappelPaieViewModel(_rappelPaieService, _rappelPaieExportService, _dataService);

            // Avantages en nature (module PAI-08)
            var sqliteAvantages = new SQLiteService(_currentDossier.DbFileName);
            _avantageNatureService = new AvantageNatureService(sqliteAvantages, _auditService);
            _avantageNatureExportService = new AvantageNatureExportService();
            _avantageNatureViewModel = new AvantageNatureViewModel(
                _avantageNatureService,
                _avantageNatureExportService,
                _dataService);

            // « Calcul d'IUTS » et « Calcul en lot » : mêmes quatre modules que les écrans
            // de bulletin (heures sup, convention collective, rappels, avantages en nature),
            // pour que les quatre écrans qui produisent la paie restent alignés (§6).
            _calculationViewModel = new CalculationViewModel(
                _taxCalculatorService,
                _dataService,
                new VariablePaieService(sqliteCalculs),
                new RubriqueService(sqliteCalculs),
                new AbsenceService(sqliteCalculs),
                new PretService(sqliteCalculs),
                new ProfilPaieService(sqliteCalculs),
                new BaremeService(sqliteCalculs),
                _heureSupplementaireService,
                _conventionCollectiveService,
                _rappelPaieService,
                _avantageNatureService,
                new PeriodePaieService(sqliteCalculs));
            _batchSalaryCalculationViewModel = new BatchSalaryCalculationViewModel(
                _dataService,
                _taxCalculatorService,
                new VariablePaieService(sqliteCalculs),
                new RubriqueService(sqliteCalculs),
                new AbsenceService(sqliteCalculs),
                new PretService(sqliteCalculs),
                new ProfilPaieService(sqliteCalculs),
                new BaremeService(sqliteCalculs),
                _heureSupplementaireService,
                _conventionCollectiveService,
                _rappelPaieService,
                _avantageNatureService,
                new PeriodePaieService(sqliteCalculs));

            // Initialisation du ViewModel pour le bulletin de paie
            _bulletinViewModel = new BulletinViewModel(
                _dataService,
                new BulletinService(_taxCalculatorService),
                new BulletinExportService(),
                new VariablePaieService(sqliteBulletins),
                new RubriqueService(sqliteBulletins),
                new AbsenceService(sqliteBulletins),
                new PretService(sqliteBulletins),
                new ProfilPaieService(sqliteBulletins),
                new BaremeService(sqliteBulletins),
                new PeriodePaieService(sqliteBulletins),
                _heureSupplementaireService,
                _conventionCollectiveService,
                _saisieOppositionService,
                _rappelPaieService,
                _avantageNatureService);

            // Initialisation du ViewModel pour les bulletins de paie en masse
            _bulletinBatchViewModel = new BulletinBatchViewModel(
                _dataService,
                new BulletinService(_taxCalculatorService),
                new BulletinExportService(),
                new VariablePaieService(sqliteBulletins),
                new RubriqueService(sqliteBulletins),
                new AbsenceService(sqliteBulletins),
                new PretService(sqliteBulletins),
                new ProfilPaieService(sqliteBulletins),
                new BaremeService(sqliteBulletins),
                new PeriodePaieService(sqliteBulletins),
                _heureSupplementaireService,
                _conventionCollectiveService,
                _saisieOppositionService,
                _rappelPaieService,
                _avantageNatureService);

            // Plan de paie. Le service ouvre la base du dossier courant : le constructeur
            // de SQLiteService met le schéma à niveau au passage, ce qui crée la table des
            // rubriques dans les dossiers antérieurs à ce module.
            _rubriqueViewModel = new RubriqueViewModel(
                new RubriqueService(new SQLiteService(_currentDossier.DbFileName)));

            // Barèmes légaux datés. Même remarque : l'ouverture de la base porte le schéma
            // à la version qui contient les tables de barèmes.
            _baremeViewModel = new BaremeViewModel(
                new BaremeService(new SQLiteService(_currentDossier.DbFileName)));

            // Périodes de paie et clôture.
            _periodePaieViewModel = new PeriodePaieViewModel(
                new PeriodePaieService(new SQLiteService(_currentDossier.DbFileName)),
                _auditService);

            // Déclaration CNSS. Elle s'appuie sur les trois modules du socle : les périodes
            // pour savoir ce qui est arrêté, les barèmes pour savoir quels taux étaient en
            // vigueur, et les bulletins enregistrés pour l'assiette.
            var sqliteDeclaration = new SQLiteService(_currentDossier.DbFileName);
            _declarationCnssViewModel = new DeclarationCnssViewModel(
                new DeclarationCnssService(
                    sqliteDeclaration,
                    new BaremeService(sqliteDeclaration),
                    new PeriodePaieService(sqliteDeclaration)),
                new DeclarationCnssExportService(),
                _currentDossier);

            // Déclaration IUTS/TPA (DEC-03) : même socle que la déclaration CNSS, mais
            // mensuelle sans seuil d'effectif — l'IUTS et la TPA se déclarent chaque mois.
            var sqliteDeclarationIutsTpa = new SQLiteService(_currentDossier.DbFileName);
            _declarationIutsTpaViewModel = new DeclarationIutsTpaViewModel(
                new DeclarationIutsTpaService(
                    sqliteDeclarationIutsTpa,
                    new BaremeService(sqliteDeclarationIutsTpa),
                    new PeriodePaieService(sqliteDeclarationIutsTpa)),
                new DeclarationIutsTpaExportService(),
                _currentDossier);

            // Livre de paie (module ETA-01) : état récapitulatif mensuel obligatoire
            // salariés × rubriques avec totaux de contrôle.
            var sqliteLivre = new SQLiteService(_currentDossier.DbFileName);
            _livrePaieViewModel = new LivrePaieViewModel(
                new LivrePaieService(
                    sqliteLivre,
                    new PeriodePaieService(sqliteLivre),
                    new RubriqueService(sqliteLivre)),
                new LivrePaieExportService(),
                _currentDossier);

            // État annuel des salaires — DADS (module DEC-05) : même construction que
            // le Livre de paie, mais cumulée sur les mois clôturés de l'année plutôt
            // qu'un seul mois — voir ETAT-DEVELOPPEMENT.md.
            var sqliteEtatAnnuel = new SQLiteService(_currentDossier.DbFileName);
            _etatAnnuelSalairesViewModel = new EtatAnnuelSalairesViewModel(
                new EtatAnnuelSalairesService(
                    sqliteEtatAnnuel,
                    new PeriodePaieService(sqliteEtatAnnuel),
                    new RubriqueService(sqliteEtatAnnuel)),
                new EtatAnnuelSalairesExportService(),
                _currentDossier);

            // Tableau de bord masse salariale (module PIL-01) : évolution mensuelle du
            // brut, du net et du coût employeur, ventilée par catégorie et par service —
            // même construction que l'état annuel des salaires, cumulée sur l'année.
            var sqliteTableauBord = new SQLiteService(_currentDossier.DbFileName);
            _tableauBordMasseSalarialeViewModel = new TableauBordMasseSalarialeViewModel(
                new TableauBordMasseSalarialeService(
                    sqliteTableauBord,
                    new PeriodePaieService(sqliteTableauBord)),
                new TableauBordMasseSalarialeExportService(),
                _currentDossier);

            // Écritures de paie SYSCOHADA (module CPT-01) : journal mensuel d'OD
            _ecriturePaieViewModel = new EcriturePaieViewModel(
                new EcriturePaieService(
                    sqliteLivre,
                    new PeriodePaieService(sqliteLivre),
                    new RubriqueService(sqliteLivre)),
                new EcriturePaieExportService(),
                _currentDossier);

            // Ordres de virement bancaire (module CPT-02) : état des paiements de salaires
            _ordreVirementViewModel = new OrdreVirementViewModel(
                new OrdreVirementService(
                    sqliteLivre,
                    new PeriodePaieService(sqliteLivre)),
                new OrdreVirementExportService(),
                _currentDossier);

            // Contrôle d'équivalence du moteur (DEC-01) : le moteur audité ayant été
            // paramétré, l'écran rejoue chaque bulletin avec l'ancienne formule et la
            // nouvelle. C'est la preuve, pas la promesse.
            _controleEquivalenceViewModel = new ControleEquivalenceViewModel(
                new ControleEquivalenceService(_dataService, _taxCalculatorService));

            // Variables du mois (PAI-02) : elles s'appuient sur le plan de paie pour les
            // rubriques saisissables et sur les périodes pour le verrou de clôture.
            var sqliteVariables = new SQLiteService(_currentDossier.DbFileName);
            _variablePaieViewModel = new VariablePaieViewModel(
                new VariablePaieService(sqliteVariables),
                new RubriqueService(sqliteVariables),
                _dataService,
                _auditService);

            // Congés et absences (PAI-04) : le compteur de congés et la retenue pour
            // absence non rémunérée alimentent le bulletin, qui cesse ainsi de dépendre
            // d'un nombre de jours saisi à la main.
            _absenceViewModel = new AbsenceViewModel(
                new AbsenceService(sqliteVariables),
                _dataService,
                new AbsenceExcelService());

            // Prêts, avances et acomptes (PAI-05) : échéancier et retenue sur le net.
            _pretViewModel = new PretViewModel(
                new PretService(new SQLiteService(_currentDossier.DbFileName)),
                _dataService);

            // Profils de paie (SOC-03) : rubriques par population, affectées et datées.
            // S'appuie sur le plan de paie pour savoir quelles rubriques sont proposables.
            _profilPaieViewModel = new ProfilPaieViewModel(
                new ProfilPaieService(new SQLiteService(_currentDossier.DbFileName)),
                new RubriqueService(new SQLiteService(_currentDossier.DbFileName)),
                _dataService);

            // Solde de tout compte et gestion des départs (module SAL-05)
            var sqliteSolde = new SQLiteService(_currentDossier.DbFileName);
            _soldeToutCompteService = new SoldeToutCompteService(
                sqliteSolde,
                _auditService,
                new AbsenceService(sqliteSolde),
                new PretService(sqliteSolde),
                _taxCalculatorService);
            _soldeToutCompteExportService = new SoldeToutCompteExportService();
            _soldeToutCompteViewModel = new SoldeToutCompteViewModel(
                sqliteSolde,
                _dataService,
                _soldeToutCompteService,
                _soldeToutCompteExportService,
                _currentDossier);

            // Organigramme d'entreprise et rattachements hiérarchiques (module RH-01)
            var sqliteOrganigramme = new SQLiteService(_currentDossier.DbFileName);
            _organigrammeService = new OrganigrammeService(sqliteOrganigramme);
            _organigrammeExportService = new OrganigrammeExportService();

            // Fiches de poste et référentiel GPEC (module RH-02)
            var sqliteFichePoste = new SQLiteService(_currentDossier.DbFileName);
            _fichePosteService = new FichePosteService(sqliteFichePoste);
            _fichePosteExportService = new FichePosteExportService();
            _suiviPostesBudgetViewModel = new SuiviPostesBudgetViewModel(
                _fichePosteService,
                _fichePosteExportService,
                _organigrammeService,
                _currentDossier);

            _organigrammeViewModel = new OrganigrammeViewModel(
                _organigrammeService,
                _organigrammeExportService,
                _dataService,
                _currentDossier,
                _fichePosteService,
                _fichePosteExportService);

            // Entretiens & évaluations des performances (module RH-03)
            var sqliteEvaluation = new SQLiteService(_currentDossier.DbFileName);
            _evaluationService = new EvaluationService(sqliteEvaluation, _organigrammeService);
            _evaluationExportService = new EvaluationExportService();
            _entretiensEvaluationsViewModel = new EntretiensEvaluationsViewModel(
                _evaluationService,
                _evaluationExportService,
                _organigrammeService,
                _fichePosteService,
                _dataService,
                _currentDossier);

            // Formation & TPA (module RH-04)
            var sqliteFormation = new SQLiteService(_currentDossier.DbFileName);
            _formationService = new FormationService(sqliteFormation);
            _formationExportService = new FormationExportService();
            _formationViewModel = new FormationViewModel(
                _formationService,
                _formationExportService,
                _fichePosteService,
                _dataService,
                _currentDossier);

            // Recrutement & Candidatures (module RH-05)
            var sqliteRecrutement = new SQLiteService(_currentDossier.DbFileName);
            _recrutementService = new RecrutementService(sqliteRecrutement, _dataService);
            _recrutementExportService = new RecrutementExportService();
            _recrutementViewModel = new RecrutementViewModel(
                _recrutementService,
                _recrutementExportService,
                _organigrammeService,
                _dataService);

            // Gestion des temps & rotations (module RH-08). Le chargement se fait à
            // l'affichage de l'écran (OnIsActiveChanged) : rien n'est lu au démarrage.
            var sqliteTemps = new SQLiteService(_currentDossier.DbFileName);
            _gestionTempsService = new GestionTempsService(sqliteTemps, _dataService);
            _gestionTempsViewModel = new GestionTempsViewModel(_gestionTempsService, _dataService, sqliteTemps);

            // Discipline & procédures (module RH-06). Chargement à l'affichage de l'écran.
            _disciplineService = new DisciplineService(new SQLiteService(_currentDossier.DbFileName));
            _disciplineViewModel = new DisciplineViewModel(_disciplineService, _dataService);

            // Santé & sécurité au travail (module RH-07). Chargement à l'affichage de l'écran.
            _santeSecuriteService = new SanteSecuriteService(new SQLiteService(_currentDossier.DbFileName));
            _santeSecuriteViewModel = new SanteSecuriteViewModel(_santeSecuriteService, _dataService);

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
                () => !IsBusy && CanEditData);

            NewCalculationCommand = new RelayCommand(
                execute: CreateNewCalculation,
                canExecute: () => !IsBusy && CanEditData);

            NavigateToEmployeeManagementCommand = new RelayCommand(NavigateToEmployeeManagement);

            NavigateToBulletinCommand = new RelayCommand(
                execute: () => NavigateTo(_bulletinViewModel),
                canExecute: () => !IsBusy);

            NavigateToRubriqueCommand = new RelayCommand(
                execute: () => NavigateTo(_rubriqueViewModel),
                canExecute: () => true);

            NavigateToBaremeCommand = new RelayCommand(
                execute: () => NavigateTo(_baremeViewModel),
                canExecute: () => true);

            NavigateToPeriodePaieCommand = new RelayCommand(
                execute: () => NavigateTo(_periodePaieViewModel),
                canExecute: () => true);

            NavigateToDeclarationCnssCommand = new RelayCommand(
                execute: () => NavigateTo(_declarationCnssViewModel),
                canExecute: () => true);

            NavigateToDeclarationIutsTpaCommand = new RelayCommand(
                execute: () => NavigateTo(_declarationIutsTpaViewModel),
                canExecute: () => true);

            NavigateToLivrePaieCommand = new RelayCommand(
                execute: () => NavigateTo(_livrePaieViewModel),
                canExecute: () => true);

            NavigateToEtatAnnuelSalairesCommand = new RelayCommand(
                execute: () => NavigateTo(_etatAnnuelSalairesViewModel),
                canExecute: () => true);

            NavigateToTableauBordMasseSalarialeCommand = new RelayCommand(
                execute: () => NavigateTo(_tableauBordMasseSalarialeViewModel),
                canExecute: () => true);

            NavigateToEcriturePaieCommand = new RelayCommand(
                execute: () => NavigateTo(_ecriturePaieViewModel),
                canExecute: () => true);

            NavigateToOrdreVirementCommand = new RelayCommand(
                execute: () => NavigateTo(_ordreVirementViewModel),
                canExecute: () => true);

            NavigateToControleMoteurCommand = new RelayCommand(
                execute: () => NavigateTo(_controleEquivalenceViewModel),
                canExecute: () => true);

            NavigateToVariablesCommand = new RelayCommand(
                execute: () => NavigateTo(_variablePaieViewModel),
                canExecute: () => true);

            NavigateToAbsencesCommand = new RelayCommand(
                execute: () => NavigateTo(_absenceViewModel),
                canExecute: () => true);

            NavigateToPretsCommand = new RelayCommand(
                execute: () => NavigateTo(_pretViewModel),
                canExecute: () => true);

            NavigateToProfilsCommand = new RelayCommand(
                execute: () => NavigateTo(_profilPaieViewModel),
                canExecute: () => true);

            NavigateToBulletinBatchCommand = new RelayCommand(
                execute: () => NavigateTo(_bulletinBatchViewModel),
                canExecute: () => !IsBusy);

            NavigateToAuditCommand = new RelayCommand(
                execute: () => NavigateTo(_auditViewModel),
                canExecute: () => true);

            NavigateToHeuresSupCommand = new RelayCommand(
                execute: () => NavigateTo(_heuresSupplementairesViewModel),
                canExecute: () => true);

            NavigateToConventionsCommand = new RelayCommand(
                execute: () =>
                {
                    _ = _conventionCollectiveViewModel.ChargerToutAsync();
                    NavigateTo(_conventionCollectiveViewModel);
                },
                canExecute: () => true);

            NavigateToSoldeToutCompteCommand = new RelayCommand(
                execute: () =>
                {
                    _ = _soldeToutCompteViewModel.ChargerDonneesAsync();
                    NavigateTo(_soldeToutCompteViewModel);
                },
                canExecute: () => true);

            NavigateToSaisiesCommand = new RelayCommand(
                execute: () =>
                {
                    _ = _saisieOppositionViewModel.ChargerDonneesAsync();
                    NavigateTo(_saisieOppositionViewModel);
                },
                canExecute: () => true);

            NavigateToRappelsCommand = new RelayCommand(
                execute: () =>
                {
                    _ = _rappelPaieViewModel.ChargerDonneesAsync();
                    NavigateTo(_rappelPaieViewModel);
                },
                canExecute: () => true);

            NavigateToAvantagesNatureCommand = new RelayCommand(
                execute: () =>
                {
                    _ = _avantageNatureViewModel.ChargerDonneesAsync();
                    NavigateTo(_avantageNatureViewModel);
                },
                canExecute: () => true);

            NavigateToOrganigrammeCommand = new RelayCommand(
                execute: () =>
                {
                    _ = _organigrammeViewModel.ChargerToutAsync();
                    NavigateTo(_organigrammeViewModel);
                },
                canExecute: () => true);

            NavigateToSuiviPostesCommand = new RelayCommand(
                execute: () =>
                {
                    _ = _suiviPostesBudgetViewModel.ChargerToutAsync();
                    NavigateTo(_suiviPostesBudgetViewModel);
                },
                canExecute: () => true);

            NavigateToEntretiensCommand = new RelayCommand(
                execute: () =>
                {
                    _ = _entretiensEvaluationsViewModel.ChargerToutAsync();
                    NavigateTo(_entretiensEvaluationsViewModel);
                },
                canExecute: () => true);

            NavigateToFormationCommand = new RelayCommand(
                execute: () =>
                {
                    _ = _formationViewModel.ChargerToutAsync();
                    NavigateTo(_formationViewModel);
                },
                canExecute: () => true);

            NavigateToRecrutementCommand = new RelayCommand(
                execute: () =>
                {
                    _ = _recrutementViewModel.ChargerToutesDonneesAsync();
                    NavigateTo(_recrutementViewModel);
                },
                canExecute: () => true);

            NavigateToGestionTempsCommand = new RelayCommand(
                execute: () => NavigateTo(_gestionTempsViewModel),
                canExecute: () => true);

            NavigateToDisciplineCommand = new RelayCommand(
                execute: () => NavigateTo(_disciplineViewModel),
                canExecute: () => true);

            NavigateToSanteSecuriteCommand = new RelayCommand(
                execute: () => NavigateTo(_santeSecuriteViewModel),
                canExecute: () => true);

            ToggleNavRailCommand = new RelayCommand(
                execute: () => IsNavRailVisible = !IsNavRailVisible);

            ChangeDossierCommand = new RelayCommand(
                execute: () => DossierChangeRequested?.Invoke(this, EventArgs.Empty),
                canExecute: () => !IsBusy);

            InitializeNavigationSearch();
        }

        private void SubscribeToViewModelEvents()
        {
            _calculationViewModel.CalculationCompleted += (sender, e) =>
            {
                StatusMessage = $"Calcul effectué le {DateTime.Now:g}";
                CommandManager.InvalidateRequerySuggested();
            };

            _historyViewModel.CalculationSelected += async (sender, calculation) =>
            {
                await _calculationViewModel.LoadExistingCalculationAsync(calculation);
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

            // Verrouillage par licence : un écran dont le module n'est pas couvert reste
            // inaccessible. En période d'évaluation, tous les modules sont ouverts.
            if (!Licensing.LicenceService.Instance.EstModuleAutorise(viewModel.GetType().Name))
            {
                Licensing.GardeIntegrite.Sonder();
                StatusMessage = "Module non inclus dans votre licence.";
                System.Windows.MessageBox.Show(
                    "Le module « " + viewModel.Title + " » n'est pas inclus dans votre licence.\n\n" +
                    "Contactez l'éditeur pour l'activer.",
                    "Sigma Paie & RH", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
                return;
            }

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
            if (!Licensing.LicenceService.Instance.EstModuleAutorise("EmployeeManagementViewModel"))
            {
                Licensing.GardeIntegrite.Sonder();
                StatusMessage = "Module non inclus dans votre licence.";
                return;
            }

            // Contrairement aux autres écrans, celui-ci est reconstruit à chaque visite (et
            // non réutilisé comme les VM singletons) : voir le commentaire du constructeur.
            // Mais comme NavigateTo(), il doit d'abord désactiver l'écran quitté — sans quoi
            // son IsActive reste bloqué à true, et y revenir plus tard ne redéclenche jamais
            // OnIsActiveChanged (pas de transition false→true), donc plus aucun
            // rafraîchissement automatique sur cet écran-là — signalé le 16 septembre 2026
            // (voir ETAT-DEVELOPPEMENT.md item 22).
            if (CurrentViewModel != null)
                CurrentViewModel.IsActive = false;

            // Le service des personnes à charge (SAL-01) ouvre la base du dossier :
            // le constructeur de SQLiteService porte le schéma à la version qui contient
            // la table, y compris pour un dossier antérieur au module.
            CurrentViewModel = new EmployeeManagementViewModel(
                _dataService,
                new PersonneAChargeService(new SQLiteService(_currentDossier.DbFileName)),
                _auditService,
                _conventionCollectiveService);
        }

        #region Logique de Recherche et Navigation

        private void InitializeNavigationSearch()
        {
            _navMenuItems.Clear();

            // ───── PAIE ─────
            _navMenuItems.Add(new NavMenuItemDefinition("Bulletin", "PAIE", "Bulletin de paie",
                "bulletin salaire paie paye fiche pdf brut net employe consultation decompte", NavigateToBulletinCommand));
            _navMenuItems.Add(new NavMenuItemDefinition("BulletinBatch", "PAIE", "Bulletins en masse",
                "bulletins en masse lot multi tous salaires generation export pdf impression regroupee", NavigateToBulletinBatchCommand));
            _navMenuItems.Add(new NavMenuItemDefinition("Rappels", "PAIE", "Rappels & régularisations",
                "rappels regularisations revalorisation retroactif trop percu omission heures sup iuts annuel regularisation fiscale fin exercice", NavigateToRappelsCommand));
            _navMenuItems.Add(new NavMenuItemDefinition("AvantagesNature", "PAIE", "Avantages en nature",
                "avantages en nature logement vehicule electricite eau domesticite nourriture forfait cgi art 106 reprise", NavigateToAvantagesNatureCommand));
            _navMenuItems.Add(new NavMenuItemDefinition("SoldeToutCompte", "PAIE", "Solde de tout compte",
                "solde tout compte stc depart indemnites preavis licenciement demission certificat recu fin de contrat rupture", NavigateToSoldeToutCompteCommand));

            // ───── CALCUL ─────
            _navMenuItems.Add(new NavMenuItemDefinition("CalculIuts", "CALCUL", "Calcul d'IUTS",
                "calcul iuts impot salaire brut net deduction tranches simulation charges fiscales", NavigateToCalculationCommand));
            _navMenuItems.Add(new NavMenuItemDefinition("CalculBatch", "CALCUL", "Calcul en lot",
                "calcul en lot masse fichier excel plusieurs salaires traitement import automatique", NavigateToBatchSalaryCalculationCommand));
            _navMenuItems.Add(new NavMenuItemDefinition("EstimationSalaire", "CALCUL", "Estimation du salaire",
                "estimation du salaire inverse net brut negociation simulation pretention salariale", NavigateToBaseSalaryEstimatorCommand));
            _navMenuItems.Add(new NavMenuItemDefinition("EstimationBatch", "CALCUL", "Estimation en lot",
                "estimation en lot masse fichier salaires simulation inverse", NavigateToBatchSalaryEstimationCommand));

            // ───── DONNÉES ─────
            _navMenuItems.Add(new NavMenuItemDefinition("Employes", "DONNEES", "Employés",
                "employes employe salaries salarie personnel agents collaborateurs fiches matricule enfants charge famille embauche contrat", NavigateToEmployeeManagementCommand));
            _navMenuItems.Add(new NavMenuItemDefinition("Historique", "DONNEES", "Historique",
                "historique calculs archives anciens bulletins consultations traces etats passes", NavigateToHistoryCommand));

            // ───── DÉCLARATIONS ─────
            _navMenuItems.Add(new NavMenuItemDefinition("DeclarationCnss", "DECLARATIONS", "Déclaration CNSS",
                "declaration cnss securite sociale caisse cotisations bordereau trimestriel mensuel employeur salarie", NavigateToDeclarationCnssCommand));
            _navMenuItems.Add(new NavMenuItemDefinition("DeclarationIutsTpa", "DECLARATIONS", "Déclaration IUTS/TPA",
                "declaration iuts tpa impots etat fiscal impôt versements tresor retenue source", NavigateToDeclarationIutsTpaCommand));
            _navMenuItems.Add(new NavMenuItemDefinition("LivrePaie", "DECLARATIONS", "Livre de paie",
                "livre de paie journal registre mensuel recapitulatif etats salaire total masse salariale", NavigateToLivrePaieCommand));
            _navMenuItems.Add(new NavMenuItemDefinition("EtatAnnuelSalaires", "DECLARATIONS", "État annuel des salaires (DADS)",
                "etat annuel des salaires dads recapitulatif annuel cumul brut base imposable iuts cotisations cloture", NavigateToEtatAnnuelSalairesCommand));

            // ───── PILOTAGE ─────
            _navMenuItems.Add(new NavMenuItemDefinition("TableauBordMasseSalariale", "PILOTAGE", "Tableau de bord masse salariale",
                "tableau de bord masse salariale evolution mensuelle brut net cout employeur service categorie graphique dashboard pilotage", NavigateToTableauBordMasseSalarialeCommand));

            // ───── RESSOURCES HUMAINES ─────
            _navMenuItems.Add(new NavMenuItemDefinition("Organigramme", "RH", "Organigramme",
                "organigramme directions services postes fonctions rattachements hierarchiques n+1 structure organisation ressources humaines rh", NavigateToOrganigrammeCommand));
            _navMenuItems.Add(new NavMenuItemDefinition("SuiviPostes", "RH", "Suivi budgétaire des postes",
                "suivi budgetaire des postes budget pourvu effectifs vacants sureffectif gpec fiches de poste competences referentiel ressources humaines rh", NavigateToSuiviPostesCommand));
            _navMenuItems.Add(new NavMenuItemDefinition("Entretiens", "RH", "Entretiens & évaluations",
                "entretiens evaluations evaluation performance notation objectifs n-1 n+1 grilles competences campagnes annuelles rh ressources humaines", NavigateToEntretiensCommand));
            _navMenuItems.Add(new NavMenuItemDefinition("Formation", "RH", "Formation & TPA",
                "formation tpa fafpa sessions participants plan de formation budget competences acquis attestation rh ressources humaines apprentissage", NavigateToFormationCommand));
            _navMenuItems.Add(new NavMenuItemDefinition("Recrutement", "RH", "Recrutement & vivier",
                "recrutement candidatures vivier talents cv entretiens embauche offre poste sal-01 rh ressources humaines", NavigateToRecrutementCommand));
            _navMenuItems.Add(new NavMenuItemDefinition("Discipline", "RH", "Discipline & procédures",
                "discipline disciplinaire procedure sanction sanctions avertissement blame mise a pied conservatoire licenciement faute lourde grave demande d explication entretien prealable notification registre rh-06 rh ressources humaines", NavigateToDisciplineCommand));
            _navMenuItems.Add(new NavMenuItemDefinition("SanteSecurite", "RH", "Santé & sécurité au travail",
                "sante securite sst hygiene accident accidents du travail at trajet maladie professionnelle declaration cnss inspection 48 heures arret indemnite journaliere ij visite medicale medecin aptitude embauche reprise epi equipement protection individuelle casque chaussures gants dotation rh-07 rh ressources humaines", NavigateToSanteSecuriteCommand));
            _navMenuItems.Add(new NavMenuItemDefinition("GestionTemps", "RH", "Gestion des temps & rotations",
                "gestion des temps rotations rotation planning equipes equipe poste quart jour nuit roulement pointage badgeuse heures supplementaires paniers panier cycle 7j 7n rh-08 rh ressources humaines", NavigateToGestionTempsCommand));
            _navMenuItems.Add(new NavMenuItemDefinition("EcriturePaie", "DECLARATIONS", "Écritures de paie",
                "ecritures de paie comptabilite compta syscohada od pieces debits credits 66 42 43 44 journal comptable", NavigateToEcriturePaieCommand));
            _navMenuItems.Add(new NavMenuItemDefinition("OrdreVirement", "DECLARATIONS", "Ordres de virement",
                "ordres de virement banque bancaire fichier virement rib paiements salaires transfert teletransmission", NavigateToOrdreVirementCommand));

            // ───── PARAMÉTRAGE ─────
            _navMenuItems.Add(new NavMenuItemDefinition("Rubrique", "PARAMETRAGE", "Plan de paie",
                "plan de paie rubriques primes indemnites retenues cotisations formule calcul regles de paie", NavigateToRubriqueCommand));
            _navMenuItems.Add(new NavMenuItemDefinition("Profils", "PARAMETRAGE", "Profils de paie",
                "profils de paie modeles cadre ouvrier employe categorie structure type contrat", NavigateToProfilsCommand));
            _navMenuItems.Add(new NavMenuItemDefinition("Variables", "PARAMETRAGE", "Variables du mois",
                "variables du mois saisie primes ponctuelles acomptes retenues grille mensuelle elements variables evp", NavigateToVariablesCommand));
            _navMenuItems.Add(new NavMenuItemDefinition("HeuresSup", "PARAMETRAGE", "Heures supplémentaires",
                "heures supplementaires sup 115 120 135 150 repos de nuit dimanche ferie majoration hs temps de travail", NavigateToHeuresSupCommand));
            _navMenuItems.Add(new NavMenuItemDefinition("Absences", "PARAMETRAGE", "Congés et absences",
                "conges et absences conge annuel paye maladie maternite permission deduit non paye solde jours repos", NavigateToAbsencesCommand));
            _navMenuItems.Add(new NavMenuItemDefinition("Prets", "PARAMETRAGE", "Prêts et avances",
                "prets et avances pret avance acompte echeancier remboursement mensualite retenue pret patronal", NavigateToPretsCommand));
            _navMenuItems.Add(new NavMenuItemDefinition("Saisies", "PARAMETRAGE", "Saisies & cessions",
                "saisies cessions oppositions salaire huissier tribunal pension alimentaire creance quotite saisissable retenue sur salaire", NavigateToSaisiesCommand));
            _navMenuItems.Add(new NavMenuItemDefinition("PeriodePaie", "PARAMETRAGE", "Périodes de paie",
                "periodes de paie mois cloture validation exercice ouverture calendrier date verrouillage", NavigateToPeriodePaieCommand));
            _navMenuItems.Add(new NavMenuItemDefinition("ControleMoteur", "PARAMETRAGE", "Contrôle du moteur",
                "controle du moteur test equivalence coherence verification formule bareme diagnostique", NavigateToControleMoteurCommand));
            _navMenuItems.Add(new NavMenuItemDefinition("Bareme", "PARAMETRAGE", "Barèmes légaux",
                "baremes legaux tranches taux plafonds lois cnss iuts fiscalites regles smig seuils", NavigateToBaremeCommand));
            _navMenuItems.Add(new NavMenuItemDefinition("Conventions", "PARAMETRAGE", "Conventions & Grilles",
                "conventions collectives grilles salariales echelons categories classification salaires minima conventionnels primes anciennete", NavigateToConventionsCommand));

            // ───── SÉCURITÉ & AUDIT ─────
            _navMenuItems.Add(new NavMenuItemDefinition("Audit", "AUDIT", "Journal d'audit",
                "journal d'audit securite tracabilite logs historique modifications suppressions activites utilisateur evenements", NavigateToAuditCommand));

            // ───── PARAMÈTRES (DOCK DU BAS) ─────
            _navMenuItems.Add(new NavMenuItemDefinition("Parametres", "PARAMETRES", "Paramètres",
                "parametres configuration preferences options societe entreprise general reglage", NavigateToSettingsCommand));

            ClearNavMenuSearchCommand = new RelayCommand(
                execute: () => NavMenuSearchText = string.Empty,
                canExecute: () => !string.IsNullOrEmpty(NavMenuSearchText));

            NavigateToFirstNavMatchCommand = new RelayCommand(
                execute: NavigateToFirstNavMatch,
                canExecute: () => HasNavMenuMatches);

            ApplyNavMenuFilter();
        }

        private void ApplyNavMenuFilter()
        {
            _visibleNavItems.Clear();
            _visibleNavGroups.Clear();

            string rawQuery = _navMenuSearchText?.Trim();
            if (string.IsNullOrEmpty(rawQuery))
            {
                foreach (var item in _navMenuItems)
                {
                    _visibleNavItems.Add(item.Key);
                    _visibleNavGroups.Add(item.GroupKey);
                }
            }
            else
            {
                string normalizedQuery = RemoveDiacritics(rawQuery).ToLowerInvariant();
                var tokens = normalizedQuery.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);

                foreach (var item in _navMenuItems)
                {
                    string searchableText = item.SearchableText;
                    bool isMatch = tokens.All(token => searchableText.Contains(token));
                    if (isMatch)
                    {
                        _visibleNavItems.Add(item.Key);
                        _visibleNavGroups.Add(item.GroupKey);
                    }
                }
            }

            OnPropertyChanged(nameof(NavMenuSearchText));
            OnPropertyChanged(nameof(HasNavMenuSearchText));
            OnPropertyChanged(nameof(HasNavMenuMatches));
            OnPropertyChanged(nameof(ShowNavNoMatches));
            OnPropertyChanged(nameof(NavMenuMatchCountText));
            OnPropertyChanged(string.Empty);
            CommandManager.InvalidateRequerySuggested();
        }

        private void NavigateToFirstNavMatch()
        {
            var firstMatch = _navMenuItems.Find(item => _visibleNavItems.Contains(item.Key));
            if (firstMatch != null && firstMatch.Command != null && firstMatch.Command.CanExecute(null))
            {
                firstMatch.Command.Execute(null);
            }
        }

        private static string RemoveDiacritics(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return string.Empty;

            string normalizedString = text.Normalize(System.Text.NormalizationForm.FormD);
            var sb = new System.Text.StringBuilder();

            foreach (char c in normalizedString)
            {
                var unicodeCategory = System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c);
                if (unicodeCategory != System.Globalization.UnicodeCategory.NonSpacingMark)
                {
                    sb.Append(c);
                }
            }

            return sb.ToString().Normalize(System.Text.NormalizationForm.FormC);
        }

        private class NavMenuItemDefinition
        {
            public string Key { get; }
            public string GroupKey { get; }
            public string Title { get; }
            public string Keywords { get; }
            public ICommand Command { get; }
            public string SearchableText { get; }

            public NavMenuItemDefinition(string key, string groupKey, string title, string keywords, ICommand command)
            {
                Key = key;
                GroupKey = groupKey;
                Title = title;
                Keywords = keywords;
                Command = command;
                SearchableText = RemoveDiacritics($"{title} {groupKey} {keywords}").ToLowerInvariant();
            }
        }

        #endregion

        public void Dispose()
        {
            // Libérer les ressources si nécessaire
        }
    }
}
