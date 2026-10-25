using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using Microsoft.Win32;
using IUTSCalculator.Helpers;
using IUTSCalculator.Models;
using IUTSCalculator.Services;

namespace IUTSCalculator.ViewModels
{
    /// <summary>
    /// ViewModel pour la gestion des campagnes d'évaluation, la conduite des entretiens individuels,
    /// la fixation des objectifs, la notation et l'historique par collaborateur (module RH-03).
    /// </summary>
    public class EntretiensEvaluationsViewModel : BaseViewModel
    {
        private readonly EvaluationService _evaluationService;
        private readonly EvaluationExportService _exportService;
        private readonly OrganigrammeService _organigrammeService;
        private readonly FichePosteService _fichePosteService;
        private readonly DataService _dataService;
        private readonly Dossier _dossier;

        public EntretiensEvaluationsViewModel(
            EvaluationService evaluationService,
            EvaluationExportService exportService,
            OrganigrammeService organigrammeService,
            FichePosteService fichePosteService,
            DataService dataService,
            Dossier dossier)
        {
            _evaluationService = evaluationService ?? throw new ArgumentNullException(nameof(evaluationService));
            _exportService = exportService ?? throw new ArgumentNullException(nameof(exportService));
            _organigrammeService = organigrammeService;
            _fichePosteService = fichePosteService;
            _dataService = dataService;
            _dossier = dossier ?? throw new ArgumentNullException(nameof(dossier));

            Title = "Entretiens & Évaluations";

            Campagnes = new ObservableCollection<CampagneEvaluation>();
            TousEntretiensCampagne = new List<EntretienEvaluation>();
            EntretiensFiltres = new ObservableCollection<EntretienEvaluation>();
            EntretiensSansEvaluateur = new ObservableCollection<EntretienEvaluation>();
            SalariesActifs = new ObservableCollection<Employee>();
            HistoriqueSalarie = new ObservableCollection<HistoriqueEntretienSalarie>();
            CatalogueCompetences = new ObservableCollection<Competence>();
            ServicesFiltre = new ObservableCollection<string>();

            InitialiserCommandes();
        }

        #region Propriétés de Navigation & Onglets

        private int _selectedTabIndex;
        public int SelectedTabIndex
        {
            get => _selectedTabIndex;
            set
            {
                if (SetProperty(ref _selectedTabIndex, value))
                {
                    OnPropertyChanged(nameof(EstOngletCampagne));
                    OnPropertyChanged(nameof(EstOngletEntretien));
                    OnPropertyChanged(nameof(EstOngletHistorique));
                }
            }
        }

        public bool EstOngletCampagne => _selectedTabIndex == 0;
        public bool EstOngletEntretien => _selectedTabIndex == 1;
        public bool EstOngletHistorique => _selectedTabIndex == 2;

        #endregion

        #region Onglet 0 : Campagnes & Tableau de bord

        public ObservableCollection<CampagneEvaluation> Campagnes { get; }
        private List<EntretienEvaluation> TousEntretiensCampagne { get; set; }
        public ObservableCollection<EntretienEvaluation> EntretiensFiltres { get; }
        public ObservableCollection<EntretienEvaluation> EntretiensSansEvaluateur { get; }
        public ObservableCollection<string> ServicesFiltre { get; }

        private CampagneEvaluation _campagneSelectionnee;
        public CampagneEvaluation CampagneSelectionnee
        {
            get => _campagneSelectionnee;
            set
            {
                if (SetProperty(ref _campagneSelectionnee, value))
                {
                    OnPropertyChanged(nameof(CampagneActiveSelectionnee));
                    _ = ChargerEntretiensCampagneAsync();
                }
            }
        }

        public bool CampagneActiveSelectionnee => _campagneSelectionnee != null;

        private EntretienEvaluation _entretienSelectionne;
        public EntretienEvaluation EntretienSelectionne
        {
            get => _entretienSelectionne;
            set => SetProperty(ref _entretienSelectionne, value);
        }

        private StatistiquesCampagne _statsCampagne = new StatistiquesCampagne();
        public StatistiquesCampagne StatsCampagne
        {
            get => _statsCampagne;
            set => SetProperty(ref _statsCampagne, value);
        }

        // Filtres
        private string _rechercheTexte;
        public string RechercheTexte
        {
            get => _rechercheTexte;
            set
            {
                if (SetProperty(ref _rechercheTexte, value))
                {
                    AppliquerFiltreEntretiens();
                }
            }
        }

        private string _filtreStatut = "Tous";
        public string FiltreStatut
        {
            get => _filtreStatut;
            set
            {
                if (SetProperty(ref _filtreStatut, value))
                {
                    AppliquerFiltreEntretiens();
                }
            }
        }

        private string _filtreService = "Tous";
        public string FiltreService
        {
            get => _filtreService;
            set
            {
                if (SetProperty(ref _filtreService, value))
                {
                    AppliquerFiltreEntretiens();
                }
            }
        }

        // Mode édition d'une campagne
        private bool _estEnEditionCampagne;
        public bool EstEnEditionCampagne
        {
            get => _estEnEditionCampagne;
            set => SetProperty(ref _estEnEditionCampagne, value);
        }

        private CampagneEvaluation _campagneEnEdition;
        public CampagneEvaluation CampagneEnEdition
        {
            get => _campagneEnEdition;
            set => SetProperty(ref _campagneEnEdition, value);
        }

        #endregion

        #region Onglet 1 : Conduite & Saisie de l'Entretien

        private EntretienEvaluation _entretienEnCours;
        public EntretienEvaluation EntretienEnCours
        {
            get => _entretienEnCours;
            set
            {
                if (SetProperty(ref _entretienEnCours, value))
                {
                    OnPropertyChanged(nameof(EntretienEnCoursValide));
                    OnPropertyChanged(nameof(EstVerrouille));
                    OnPropertyChanged(nameof(EstModifiable));
                }
            }
        }

        public bool EntretienEnCoursValide => _entretienEnCours != null;
        public bool EstVerrouille => _entretienEnCours?.EstVerrouille ?? false;
        public bool EstModifiable => _entretienEnCours != null && !EstVerrouille;

        public ObservableCollection<Competence> CatalogueCompetences { get; }

        private Competence _competenceAAjouter;
        public Competence CompetenceAAjouter
        {
            get => _competenceAAjouter;
            set => SetProperty(ref _competenceAAjouter, value);
        }

        #endregion

        #region Onglet 2 : Historique par Salarié

        public ObservableCollection<Employee> SalariesActifs { get; }
        public ObservableCollection<HistoriqueEntretienSalarie> HistoriqueSalarie { get; }

        private Employee _salarieSelectionne;
        public Employee SalarieSelectionne
        {
            get => _salarieSelectionne;
            set
            {
                if (SetProperty(ref _salarieSelectionne, value))
                {
                    _ = ChargerHistoriqueSalarieAsync();
                }
            }
        }

        private HistoriqueEntretienSalarie _historiqueSelectionne;
        public HistoriqueEntretienSalarie HistoriqueSelectionne
        {
            get => _historiqueSelectionne;
            set => SetProperty(ref _historiqueSelectionne, value);
        }

        #endregion

        #region Commandes

        public ICommand RechargerToutCommand { get; private set; }

        // Commandes Campagne
        public ICommand NouvelleCampagneCommand { get; private set; }
        public ICommand EditerCampagneCommand { get; private set; }
        public ICommand EnregistrerCampagneCommand { get; private set; }
        public ICommand AnnulerEditionCampagneCommand { get; private set; }
        public ICommand SupprimerCampagneCommand { get; private set; }
        public ICommand CloturerCampagneCommand { get; private set; }
        public ICommand InitialiserCampagneCommand { get; private set; }
        public ICommand ExporterExcelCampagneCommand { get; private set; }
        public ICommand ExporterPdfEntretienCommand { get; private set; }
        public ICommand OuvrirEntretienCommand { get; private set; }

        // Commandes Entretien
        public ICommand EnregistrerEntretienCommand { get; private set; }
        public ICommand MarquerRealiseCommand { get; private set; }
        public ICommand ValiderEntretienCommand { get; private set; }
        public ICommand ImprimerPdfEntretienEnCoursCommand { get; private set; }
        public ICommand RetourAuxCampagnesCommand { get; private set; }

        // Commandes sous-éléments Entretien
        public ICommand AjouterCritereCommand { get; private set; }
        public ICommand AjouterCritereDepuisCatalogueCommand { get; private set; }
        public ICommand SupprimerCritereCommand { get; private set; }
        public ICommand AjouterObjectifPasseCommand { get; private set; }
        public ICommand SupprimerObjectifPasseCommand { get; private set; }
        public ICommand AjouterNouvelObjectifCommand { get; private set; }
        public ICommand SupprimerNouvelObjectifCommand { get; private set; }
        public ICommand AjouterBesoinFormationCommand { get; private set; }
        public ICommand SupprimerBesoinFormationCommand { get; private set; }
        public ICommand RecalculerNoteGlobaleCommand { get; private set; }

        // Commandes Historique Salarié
        public ICommand ImprimerHistoriquePdfCommand { get; private set; }

        private void InitialiserCommandes()
        {
            RechargerToutCommand = new RelayCommand(async () => await ChargerToutAsync(), () => !IsBusy);

            // Campagne
            NouvelleCampagneCommand = new RelayCommand(NouvelleCampagne);
            EditerCampagneCommand = new RelayCommand(EditerCampagne, () => CampagneSelectionnee != null);
            EnregistrerCampagneCommand = new RelayCommand(async () => await EnregistrerCampagneAsync(), () => CampagneEnEdition != null && !IsBusy);
            AnnulerEditionCampagneCommand = new RelayCommand(() => EstEnEditionCampagne = false);
            SupprimerCampagneCommand = new RelayCommand(async () => await SupprimerCampagneAsync(), () => CampagneSelectionnee != null && !IsBusy);
            CloturerCampagneCommand = new RelayCommand(async () => await CloturerCampagneAsync(), () => CampagneSelectionnee != null && CampagneSelectionnee.Statut != StatutCampagne.Cloturee && !IsBusy);
            InitialiserCampagneCommand = new RelayCommand(async () => await InitialiserCampagneAsync(), () => CampagneSelectionnee != null && !IsBusy);
            ExporterExcelCampagneCommand = new RelayCommand(ExporterExcelCampagne, () => CampagneSelectionnee != null && !IsBusy);
            ExporterPdfEntretienCommand = new RelayCommand(ExporterPdfEntretienSelectionne, () => EntretienSelectionne != null && !IsBusy);
            OuvrirEntretienCommand = new RelayCommand(async () => await OuvrirEntretienSelectionneAsync(), () => EntretienSelectionne != null);

            // Entretien
            EnregistrerEntretienCommand = new RelayCommand(async () => await EnregistrerEntretienEnCoursAsync(), () => EntretienEnCours != null && EstModifiable && !IsBusy);
            MarquerRealiseCommand = new RelayCommand(async () => await MarquerEntretienRealiseAsync(), () => EntretienEnCours != null && EntretienEnCours.Statut == StatutEntretien.Planifie && !IsBusy);
            ValiderEntretienCommand = new RelayCommand(async () => await ValiderEntretienEnCoursAsync(), () => EntretienEnCours != null && EntretienEnCours.Statut != StatutEntretien.Valide && !IsBusy);
            ImprimerPdfEntretienEnCoursCommand = new RelayCommand(ImprimerPdfEntretienEnCours, () => EntretienEnCours != null && !IsBusy);
            RetourAuxCampagnesCommand = new RelayCommand(() => SelectedTabIndex = 0);

            // Sous-éléments
            AjouterCritereCommand = new RelayCommand(AjouterCritere, () => EstModifiable);
            AjouterCritereDepuisCatalogueCommand = new RelayCommand(AjouterCritereDepuisCatalogue, () => EstModifiable && CompetenceAAjouter != null);
            SupprimerCritereCommand = new RelayCommand<EntretienCritere>(SupprimerCritere, c => EstModifiable);
            AjouterObjectifPasseCommand = new RelayCommand(AjouterObjectifPasse, () => EstModifiable);
            SupprimerObjectifPasseCommand = new RelayCommand<EntretienObjectif>(SupprimerObjectifPasse, o => EstModifiable);
            AjouterNouvelObjectifCommand = new RelayCommand(AjouterNouvelObjectif, () => EstModifiable);
            SupprimerNouvelObjectifCommand = new RelayCommand<EntretienObjectif>(SupprimerNouvelObjectif, o => EstModifiable);
            AjouterBesoinFormationCommand = new RelayCommand(AjouterBesoinFormation, () => EstModifiable);
            SupprimerBesoinFormationCommand = new RelayCommand<EntretienBesoinFormation>(SupprimerBesoinFormation, b => EstModifiable);
            RecalculerNoteGlobaleCommand = new RelayCommand(RecalculerNoteGlobale, () => EstModifiable);

            // Historique
            ImprimerHistoriquePdfCommand = new RelayCommand(async () => await ImprimerHistoriqueSelectionnePdfAsync(), () => HistoriqueSelectionne != null && !IsBusy);
        }

        #endregion

        #region Chargement des Données

        public async Task ChargerToutAsync()
        {
            try
            {
                IsBusy = true;
                StatusMessage = "Chargement des données d'évaluation...";

                // 1. Charger les campagnes
                var listeCampagnes = await _evaluationService.ObtenirCampagnesAsync();
                Campagnes.Clear();
                foreach (var c in listeCampagnes)
                {
                    Campagnes.Add(c);
                }

                if (CampagneSelectionnee == null || !Campagnes.Any(c => c.Id == CampagneSelectionnee.Id))
                {
                    CampagneSelectionnee = Campagnes.FirstOrDefault(c => c.Statut == StatutCampagne.EnCours) ?? Campagnes.FirstOrDefault();
                }

                // 2. Charger le catalogue des compétences si service disponible
                if (_fichePosteService != null)
                {
                    var competences = await _fichePosteService.ChargerToutesCompetencesAsync();
                    CatalogueCompetences.Clear();
                    foreach (var comp in competences)
                    {
                        CatalogueCompetences.Add(comp);
                    }
                    CompetenceAAjouter = CatalogueCompetences.FirstOrDefault();
                }

                // 3. Charger les salariés actifs
                if (_dataService != null)
                {
                    var allEmployees = await _dataService.LoadEmployeesAsync();
                    SalariesActifs.Clear();
                    foreach (var emp in allEmployees.Where(e => e.EstActif).OrderBy(e => e.LastName).ThenBy(e => e.FirstName))
                    {
                        SalariesActifs.Add(emp);
                    }

                    if (SalarieSelectionne == null && SalariesActifs.Count > 0)
                    {
                        SalarieSelectionne = SalariesActifs.First();
                    }
                }

                StatusMessage = "Données d'évaluation chargées.";
            }
            catch (Exception ex)
            {
                StatusMessage = "Erreur lors du chargement : " + ex.Message;
                MessageBox.Show("Erreur de chargement des évaluations : " + ex.Message, "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                IsBusy = false;
            }
        }

        private async Task ChargerEntretiensCampagneAsync()
        {
            if (CampagneSelectionnee == null)
            {
                TousEntretiensCampagne.Clear();
                EntretiensFiltres.Clear();
                EntretiensSansEvaluateur.Clear();
                StatsCampagne = new StatistiquesCampagne();
                return;
            }

            try
            {
                IsBusy = true;
                StatusMessage = $"Chargement de la campagne {CampagneSelectionnee.Code}...";

                TousEntretiensCampagne = await _evaluationService.ObtenirEntretiensCampagneAsync(CampagneSelectionnee.Id);
                StatsCampagne = await _evaluationService.CalculerStatistiquesCampagneAsync(CampagneSelectionnee.Id);

                // Mettre à jour la liste des services disponibles pour le filtrage
                var services = TousEntretiensCampagne
                    .Select(e => e.EmployeeService)
                    .Where(s => !string.IsNullOrWhiteSpace(s))
                    .Distinct()
                    .OrderBy(s => s)
                    .ToList();

                ServicesFiltre.Clear();
                ServicesFiltre.Add("Tous");
                foreach (var s in services) ServicesFiltre.Add(s);

                // Extraire les salariés dont l'évaluateur est à assigner
                EntretiensSansEvaluateur.Clear();
                foreach (var e in TousEntretiensCampagne.Where(e => e.EvaluateurAAssigner))
                {
                    EntretiensSansEvaluateur.Add(e);
                }

                AppliquerFiltreEntretiens();
                StatusMessage = $"{TousEntretiensCampagne.Count} entretien(s) chargé(s).";
            }
            catch (Exception ex)
            {
                StatusMessage = "Erreur : " + ex.Message;
            }
            finally
            {
                IsBusy = false;
            }
        }

        private void AppliquerFiltreEntretiens()
        {
            EntretiensFiltres.Clear();

            var query = TousEntretiensCampagne.AsEnumerable();

            // Filtre par statut
            if (!string.IsNullOrWhiteSpace(FiltreStatut) && FiltreStatut != "Tous")
            {
                query = query.Where(e => string.Equals(e.StatutLibelle, FiltreStatut, StringComparison.OrdinalIgnoreCase));
            }

            // Filtre par service
            if (!string.IsNullOrWhiteSpace(FiltreService) && FiltreService != "Tous")
            {
                query = query.Where(e => string.Equals(e.EmployeeService, FiltreService, StringComparison.OrdinalIgnoreCase));
            }

            // Recherche texte
            if (!string.IsNullOrWhiteSpace(RechercheTexte))
            {
                string search = RechercheTexte.Trim().ToLowerInvariant();
                query = query.Where(e =>
                    (e.EmployeeId != null && e.EmployeeId.ToLowerInvariant().Contains(search)) ||
                    (e.EmployeeNomComplet != null && e.EmployeeNomComplet.ToLowerInvariant().Contains(search)) ||
                    (e.EmployeePoste != null && e.EmployeePoste.ToLowerInvariant().Contains(search)) ||
                    (e.EvaluateurNomComplet != null && e.EvaluateurNomComplet.ToLowerInvariant().Contains(search)));
            }

            foreach (var ent in query)
            {
                EntretiensFiltres.Add(ent);
            }
        }

        #endregion

        #region Gestion des Campagnes

        private void NouvelleCampagne()
        {
            CampagneEnEdition = new CampagneEvaluation
            {
                Code = $"CAMP-{DateTime.Today.Year}",
                Titre = $"Campagne d'Évaluation Annuelle {DateTime.Today.Year}",
                AnneeExercice = DateTime.Today.Year,
                DateDebut = new DateTime(DateTime.Today.Year, 1, 1),
                DateFin = new DateTime(DateTime.Today.Year, 12, 31),
                Statut = StatutCampagne.Planifiee,
                Description = "Campagne annuelle d'évaluation des compétences et de fixation des objectifs."
            };
            EstEnEditionCampagne = true;
        }

        private void EditerCampagne()
        {
            if (CampagneSelectionnee == null) return;

            CampagneEnEdition = new CampagneEvaluation
            {
                Id = CampagneSelectionnee.Id,
                Code = CampagneSelectionnee.Code,
                Titre = CampagneSelectionnee.Titre,
                AnneeExercice = CampagneSelectionnee.AnneeExercice,
                DateDebut = CampagneSelectionnee.DateDebut,
                DateFin = CampagneSelectionnee.DateFin,
                Statut = CampagneSelectionnee.Statut,
                Description = CampagneSelectionnee.Description,
                DateCreation = CampagneSelectionnee.DateCreation
            };
            EstEnEditionCampagne = true;
        }

        private async Task EnregistrerCampagneAsync()
        {
            if (CampagneEnEdition == null) return;

            try
            {
                IsBusy = true;
                StatusMessage = "Enregistrement de la campagne...";

                await _evaluationService.EnregistrerCampagneAsync(CampagneEnEdition);
                EstEnEditionCampagne = false;
                await ChargerToutAsync();

                CampagneSelectionnee = Campagnes.FirstOrDefault(c => c.Code == CampagneEnEdition.Code);
                StatusMessage = "Campagne enregistrée avec succès.";
            }
            catch (Exception ex)
            {
                StatusMessage = "Erreur : " + ex.Message;
                MessageBox.Show("Erreur lors de l'enregistrement de la campagne : " + ex.Message, "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            finally
            {
                IsBusy = false;
            }
        }

        private async Task SupprimerCampagneAsync()
        {
            if (CampagneSelectionnee == null) return;

            var result = MessageBox.Show(
                $"Êtes-vous sûr de vouloir supprimer la campagne '{CampagneSelectionnee.Titre}' ?\nCette action supprimera également tous les entretiens associés.",
                "Confirmation de suppression",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result != MessageBoxResult.Yes) return;

            try
            {
                IsBusy = true;
                await _evaluationService.SupprimerCampagneAsync(CampagneSelectionnee.Id);
                CampagneSelectionnee = null;
                await ChargerToutAsync();
                StatusMessage = "Campagne supprimée.";
            }
            catch (Exception ex)
            {
                StatusMessage = "Erreur : " + ex.Message;
                MessageBox.Show("Erreur de suppression : " + ex.Message, "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                IsBusy = false;
            }
        }

        private async Task CloturerCampagneAsync()
        {
            if (CampagneSelectionnee == null) return;

            var result = MessageBox.Show(
                $"Souhaitez-vous clôturer la campagne '{CampagneSelectionnee.Titre}' ?\nLes statistiques de complétion seront figées.",
                "Clôture de campagne",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result != MessageBoxResult.Yes) return;

            try
            {
                IsBusy = true;
                await _evaluationService.CloturerCampagneAsync(CampagneSelectionnee.Id);
                await ChargerToutAsync();
                StatusMessage = "Campagne clôturée avec succès.";
            }
            catch (Exception ex)
            {
                StatusMessage = "Erreur : " + ex.Message;
            }
            finally
            {
                IsBusy = false;
            }
        }

        private async Task InitialiserCampagneAsync()
        {
            if (CampagneSelectionnee == null) return;

            try
            {
                IsBusy = true;
                StatusMessage = "Initialisation des entretiens pour l'effectif actif...";

                var rapport = await _evaluationService.InitialiserEntretiensCampagneAsync(CampagneSelectionnee.Id);
                await ChargerEntretiensCampagneAsync();

                string msg = $"Initialisation terminée pour la campagne '{CampagneSelectionnee.Titre}' :\n\n" +
                             $"• Salariés actifs éligibles : {rapport.TotalSalariesActifs}\n" +
                             $"• Nouveaux entretiens créés : {rapport.EntretiensCrees}\n" +
                             $"• Entretiens existant déjà (inchangés) : {rapport.EntretiensExistantDeja}\n" +
                             $"• Supérieurs N+1 assignés automatiquement : {rapport.EvaluateursAssignes}\n" +
                             $"• Évaluateurs à assigner manuellement : {rapport.EvaluateursAAssigner}";

                if (rapport.SalariesSansEvaluateur.Count > 0)
                {
                    msg += $"\n\nSalariés en attente d'attribution de responsable N+1 ({rapport.SalariesSansEvaluateur.Count}) :\n" +
                           string.Join(", ", rapport.SalariesSansEvaluateur.Take(5)) +
                           (rapport.SalariesSansEvaluateur.Count > 5 ? "..." : "");
                }

                MessageBox.Show(msg, "Bilan de l'initialisation", MessageBoxButton.OK, MessageBoxImage.Information);
                StatusMessage = $"{rapport.EntretiensCrees} nouvel(s) entretien(s) créé(s).";
            }
            catch (Exception ex)
            {
                StatusMessage = "Erreur lors de l'initialisation : " + ex.Message;
                MessageBox.Show("Erreur : " + ex.Message, "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                IsBusy = false;
            }
        }

        #endregion

        #region Conduite de l'Entretien

        private async Task OuvrirEntretienSelectionneAsync()
        {
            if (EntretienSelectionne == null) return;

            try
            {
                IsBusy = true;
                StatusMessage = "Chargement complet de l'entretien...";

                EntretienEnCours = await _evaluationService.ObtenirEntretienCompletAsync(EntretienSelectionne.Id);
                SelectedTabIndex = 1;
                StatusMessage = $"Entretien de {EntretienEnCours.EmployeeNomComplet} ouvert.";
            }
            catch (Exception ex)
            {
                StatusMessage = "Erreur : " + ex.Message;
                MessageBox.Show("Erreur lors de l'ouverture de l'entretien : " + ex.Message, "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                IsBusy = false;
            }
        }

        private async Task EnregistrerEntretienEnCoursAsync()
        {
            if (EntretienEnCours == null) return;

            try
            {
                IsBusy = true;
                StatusMessage = "Enregistrement de l'entretien...";

                await _evaluationService.EnregistrerEntretienAsync(EntretienEnCours);
                await ChargerEntretiensCampagneAsync();

                StatusMessage = $"Entretien de {EntretienEnCours.EmployeeNomComplet} enregistré avec succès (Note : {EntretienEnCours.NoteGlobaleFormatee}).";
                MessageBox.Show("L'entretien a été enregistré avec succès.", "Succès", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                StatusMessage = "Erreur d'enregistrement : " + ex.Message;
                MessageBox.Show("Erreur lors de l'enregistrement de l'entretien :\n" + ex.Message, "Avertissement", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            finally
            {
                IsBusy = false;
            }
        }

        private async Task MarquerEntretienRealiseAsync()
        {
            if (EntretienEnCours == null) return;

            try
            {
                IsBusy = true;
                EntretienEnCours.Statut = StatutEntretien.Realise;
                await _evaluationService.EnregistrerEntretienAsync(EntretienEnCours);
                await _evaluationService.ChangerStatutEntretienAsync(EntretienEnCours.Id, StatutEntretien.Realise);
                await ChargerEntretiensCampagneAsync();

                OnPropertyChanged(nameof(EstVerrouille));
                OnPropertyChanged(nameof(EstModifiable));
                StatusMessage = "Entretien marqué comme Réalisé.";
            }
            catch (Exception ex)
            {
                StatusMessage = "Erreur : " + ex.Message;
                MessageBox.Show("Erreur : " + ex.Message, "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                IsBusy = false;
            }
        }

        private async Task ValiderEntretienEnCoursAsync()
        {
            if (EntretienEnCours == null) return;

            var result = MessageBox.Show(
                "ATTENTION : Vous êtes sur le point de valider et sceller cet entretien individuel.\n\n" +
                "Conformément à la règle de force probante et d'opposabilité, un entretien validé " +
                "devient strictement IMMUTABLE (il ne pourra plus être modifié, ni ses critères, ni ses notes).\n\n" +
                "Confirmez-vous la validation définitive ?",
                "Validation officielle & Verrouillage",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result != MessageBoxResult.Yes) return;

            try
            {
                IsBusy = true;
                StatusMessage = "Validation et scellement de l'entretien...";

                EntretienEnCours.Statut = StatutEntretien.Valide;
                EntretienEnCours.DateValidation = DateTime.Now;

                await _evaluationService.EnregistrerEntretienAsync(EntretienEnCours);
                await _evaluationService.ChangerStatutEntretienAsync(EntretienEnCours.Id, StatutEntretien.Valide);
                await ChargerEntretiensCampagneAsync();

                OnPropertyChanged(nameof(EstVerrouille));
                OnPropertyChanged(nameof(EstModifiable));
                StatusMessage = "Entretien validé et scellé avec succès.";
                MessageBox.Show("L'entretien a été validé et scellé avec succès.", "Validation scellée", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                StatusMessage = "Erreur : " + ex.Message;
                MessageBox.Show("Erreur de validation : " + ex.Message, "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                IsBusy = false;
            }
        }

        private void RecalculerNoteGlobale()
        {
            if (EntretienEnCours == null || EstVerrouille) return;
            EntretienEnCours.RecalculerNoteGlobale();
        }

        private void AjouterCritere()
        {
            if (EntretienEnCours == null || EstVerrouille) return;

            EntretienEnCours.Criteres.Add(new EntretienCritere
            {
                EntretienId = EntretienEnCours.Id,
                Categorie = "Compétences Métier",
                Libelle = "Nouveau critère d'évaluation",
                Note = 3,
                Poids = 1
            });
            EntretienEnCours.RecalculerNoteGlobale();
        }

        private void AjouterCritereDepuisCatalogue()
        {
            if (EntretienEnCours == null || EstVerrouille || CompetenceAAjouter == null) return;

            EntretienEnCours.Criteres.Add(new EntretienCritere
            {
                EntretienId = EntretienEnCours.Id,
                CompetenceId = CompetenceAAjouter.Id,
                Categorie = CompetenceAAjouter.DomaineLibelle,
                Libelle = CompetenceAAjouter.Libelle,
                Note = 3,
                Poids = 1
            });
            EntretienEnCours.RecalculerNoteGlobale();
        }

        private void SupprimerCritere(EntretienCritere crit)
        {
            if (EntretienEnCours == null || EstVerrouille || crit == null) return;

            EntretienEnCours.Criteres.Remove(crit);
            EntretienEnCours.RecalculerNoteGlobale();
        }

        private void AjouterObjectifPasse()
        {
            if (EntretienEnCours == null || EstVerrouille) return;

            EntretienEnCours.ObjectifsPasses.Add(new EntretienObjectif
            {
                EntretienId = EntretienEnCours.Id,
                TypeObjectif = TypeObjectifEntretien.ObjectifPasse,
                Intitule = "Objectif évalué N-1",
                TauxAtteintePct = 100,
                Poids = 1
            });
            EntretienEnCours.RecalculerNoteGlobale();
        }

        private void SupprimerObjectifPasse(EntretienObjectif obj)
        {
            if (EntretienEnCours == null || EstVerrouille || obj == null) return;

            EntretienEnCours.ObjectifsPasses.Remove(obj);
            EntretienEnCours.RecalculerNoteGlobale();
        }

        private void AjouterNouvelObjectif()
        {
            if (EntretienEnCours == null || EstVerrouille) return;

            EntretienEnCours.NouveauxObjectifs.Add(new EntretienObjectif
            {
                EntretienId = EntretienEnCours.Id,
                TypeObjectif = TypeObjectifEntretien.NouvelObjectif,
                Intitule = "Nouvel objectif opérationnel N+1",
                Echeance = "Fin exercice",
                Poids = 1
            });
        }

        private void SupprimerNouvelObjectif(EntretienObjectif obj)
        {
            if (EntretienEnCours == null || EstVerrouille || obj == null) return;

            EntretienEnCours.NouveauxObjectifs.Remove(obj);
        }

        private void AjouterBesoinFormation()
        {
            if (EntretienEnCours == null || EstVerrouille) return;

            EntretienEnCours.BesoinsFormation.Add(new EntretienBesoinFormation
            {
                EntretienId = EntretienEnCours.Id,
                ThemeFormation = "Thème de perfectionnement",
                Priorite = PrioriteFormation.Normale,
                Statut = StatutBesoinFormation.Demande
            });
        }

        private void SupprimerBesoinFormation(EntretienBesoinFormation form)
        {
            if (EntretienEnCours == null || EstVerrouille || form == null) return;

            EntretienEnCours.BesoinsFormation.Remove(form);
        }

        #endregion

        #region Historique Salarié

        private async Task ChargerHistoriqueSalarieAsync()
        {
            if (SalarieSelectionne == null)
            {
                HistoriqueSalarie.Clear();
                return;
            }

            try
            {
                IsBusy = true;
                StatusMessage = $"Chargement de l'historique de {SalarieSelectionne.FullName}...";

                var hist = await _evaluationService.ObtenirHistoriqueEntretiensSalarieAsync(SalarieSelectionne.EmployeeId);
                HistoriqueSalarie.Clear();
                foreach (var h in hist)
                {
                    HistoriqueSalarie.Add(h);
                }

                StatusMessage = $"{HistoriqueSalarie.Count} entretien(s) historique(s) trouvé(s).";
            }
            catch (Exception ex)
            {
                StatusMessage = "Erreur : " + ex.Message;
            }
            finally
            {
                IsBusy = false;
            }
        }

        #endregion

        #region Exports PDF & Excel

        private void ExporterExcelCampagne()
        {
            if (CampagneSelectionnee == null) return;

            var sfd = new SaveFileDialog
            {
                Filter = "Classeur Excel (*.xlsx)|*.xlsx",
                FileName = $"Synthese_Campagne_{CampagneSelectionnee.Code}_{DateTime.Now:yyyyMMdd}.xlsx",
                Title = "Exporter la synthèse de la campagne d'évaluation"
            };

            if (sfd.ShowDialog() == true)
            {
                try
                {
                    _exportService.GenererSyntheseCampagneExcel(
                        CampagneSelectionnee,
                        TousEntretiensCampagne,
                        StatsCampagne,
                        _dossier,
                        sfd.FileName);

                    MessageBox.Show("La synthèse Excel a été générée avec succès.", "Export Réussi", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Erreur lors de l'export Excel : " + ex.Message, "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private async void ExporterPdfEntretienSelectionne()
        {
            if (EntretienSelectionne == null) return;

            try
            {
                var entComplet = await _evaluationService.ObtenirEntretienCompletAsync(EntretienSelectionne.Id);
                GenererPdfEntretien(entComplet);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Erreur lors de la génération PDF : " + ex.Message, "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ImprimerPdfEntretienEnCours()
        {
            if (EntretienEnCours == null) return;
            GenererPdfEntretien(EntretienEnCours);
        }

        private async Task ImprimerHistoriqueSelectionnePdfAsync()
        {
            if (HistoriqueSelectionne == null) return;

            try
            {
                var entComplet = await _evaluationService.ObtenirEntretienCompletAsync(HistoriqueSelectionne.EntretienId);
                GenererPdfEntretien(entComplet);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Erreur lors de la réimpression PDF : " + ex.Message, "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void GenererPdfEntretien(EntretienEvaluation ent)
        {
            if (ent == null) return;

            var sfd = new SaveFileDialog
            {
                Filter = "Document PDF (*.pdf)|*.pdf",
                FileName = $"Fiche_Entretien_{ent.EmployeeId}_{ent.CampagneAnnee}_{DateTime.Now:yyyyMMdd}.pdf",
                Title = "Exporter la fiche individuelle d'évaluation"
            };

            if (sfd.ShowDialog() == true)
            {
                try
                {
                    _exportService.GenererFicheEntretienPdf(ent, _dossier, sfd.FileName);
                    MessageBox.Show("La fiche PDF d'évaluation a été générée avec succès.", "Export PDF", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Erreur lors de la génération PDF : " + ex.Message, "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        #endregion
    }
}
