using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using IUTSCalculator.Helpers;
using IUTSCalculator.Models;
using IUTSCalculator.Services;
using Microsoft.Win32;

namespace IUTSCalculator.ViewModels
{
    /// <summary>
    /// ViewModel principal pour le module RH-04 Formation & TPA.
    /// Gère les plans annuels, le catalogue, les organismes, les sessions, les participants,
    /// la passerelle avec les entretiens RH-03 et les indicateurs fiscaux TPA burkinabè.
    /// </summary>
    public class FormationViewModel : BaseViewModel
    {
        private readonly FormationService _formationService;
        private readonly FormationExportService _exportService;
        private readonly FichePosteService _fichePosteService;
        private readonly DataService _dataService;
        private readonly Dossier _dossier;

        #region Propriétés & Collections

        private int _anneeSelectionnee = DateTime.Now.Year;
        private PlanFormation _planSelectionne;
        private SessionFormation _sessionSelectionnee;
        private SessionInscription _inscriptionSelectionnee;
        private FormationCatalogue _formationCatalogueSelectionnee;
        private OrganismeFormation _organismeSelectionne;
        private EntretienBesoinFormation _besoinRh03Selectionne;
        private TableauBordTpaFormation _tableauBordTpa;

        private Employee _nouvelEmployeSelectionne;
        private decimal _nouveauxFraisImputes;

        private string _filtreStatutSession = "Toutes";
        private string _rechercheSessionTexte = string.Empty;
        private int _ongletActifIndex = 0;

        public ObservableCollection<int> AnneesDisponibles { get; }
        public ObservableCollection<PlanFormation> Plans { get; }
        public ObservableCollection<SessionFormation> Sessions { get; }
        public ObservableCollection<SessionFormation> SessionsFiltrees { get; }
        public ObservableCollection<SessionInscription> Inscriptions { get; }
        public ObservableCollection<FormationCatalogue> CatalogueFormations { get; }
        public ObservableCollection<OrganismeFormation> Organismes { get; }
        public ObservableCollection<EntretienBesoinFormation> BesoinsRh03 { get; }
        public ObservableCollection<Employee> EmployesDisponibles { get; }
        public ObservableCollection<Competence> CompetencesDisponibles { get; }

        public int AnneeSelectionnee
        {
            get => _anneeSelectionnee;
            set
            {
                if (SetProperty(ref _anneeSelectionnee, value))
                {
                    _ = ChargerPlansEtTpaAsync();
                }
            }
        }

        public PlanFormation PlanSelectionne
        {
            get => _planSelectionne;
            set
            {
                if (SetProperty(ref _planSelectionne, value))
                {
                    _ = ChargerSessionsPlanAsync();
                    _ = ChargerTpaDashboardAsync();
                }
            }
        }

        public SessionFormation SessionSelectionnee
        {
            get => _sessionSelectionnee;
            set
            {
                if (SetProperty(ref _sessionSelectionnee, value))
                {
                    _ = ChargerInscriptionsSessionAsync();
                }
            }
        }

        public SessionInscription InscriptionSelectionnee
        {
            get => _inscriptionSelectionnee;
            set => SetProperty(ref _inscriptionSelectionnee, value);
        }

        public FormationCatalogue FormationCatalogueSelectionnee
        {
            get => _formationCatalogueSelectionnee;
            set => SetProperty(ref _formationCatalogueSelectionnee, value);
        }

        public OrganismeFormation OrganismeSelectionne
        {
            get => _organismeSelectionne;
            set => SetProperty(ref _organismeSelectionne, value);
        }

        public EntretienBesoinFormation BesoinRh03Selectionne
        {
            get => _besoinRh03Selectionne;
            set => SetProperty(ref _besoinRh03Selectionne, value);
        }

        public TableauBordTpaFormation TableauBordTpa
        {
            get => _tableauBordTpa;
            set => SetProperty(ref _tableauBordTpa, value);
        }

        public Employee NouvelEmployeSelectionne
        {
            get => _nouvelEmployeSelectionne;
            set => SetProperty(ref _nouvelEmployeSelectionne, value);
        }

        public decimal NouveauxFraisImputes
        {
            get => _nouveauxFraisImputes;
            set => SetProperty(ref _nouveauxFraisImputes, value);
        }

        public string FiltreStatutSession
        {
            get => _filtreStatutSession;
            set
            {
                if (SetProperty(ref _filtreStatutSession, value))
                {
                    AppliquerFiltreSessions();
                }
            }
        }

        public string RechercheSessionTexte
        {
            get => _rechercheSessionTexte;
            set
            {
                if (SetProperty(ref _rechercheSessionTexte, value))
                {
                    AppliquerFiltreSessions();
                }
            }
        }

        public int OngletActifIndex
        {
            get => _ongletActifIndex;
            set => SetProperty(ref _ongletActifIndex, value);
        }

        #endregion

        #region Commandes

        public ICommand ChargerToutCommand { get; private set; }
        public ICommand RafraichirCommand { get; private set; }

        // Plan
        public ICommand NouveauPlanCommand { get; private set; }
        public ICommand EnregistrerPlanCommand { get; private set; }
        public ICommand SupprimerPlanCommand { get; private set; }
        public ICommand ValiderPlanCommand { get; private set; }
        public ICommand CloturerPlanCommand { get; private set; }

        // Session
        public ICommand NouvelleSessionCommand { get; private set; }
        public ICommand EnregistrerSessionCommand { get; private set; }
        public ICommand SupprimerSessionCommand { get; private set; }
        public ICommand ConfirmerSessionCommand { get; private set; }
        public ICommand RealiserSessionCommand { get; private set; }
        public ICommand AnnulerSessionCommand { get; private set; }

        // Participants & Inscriptions
        public ICommand AjouterParticipantCommand { get; private set; }
        public ICommand MettreAJourInscriptionCommand { get; private set; }
        public ICommand SupprimerInscriptionCommand { get; private set; }
        public ICommand PointerPresentCommand { get; private set; }
        public ICommand PointerAbsentCommand { get; private set; }
        public ICommand DelivrerAttestationCommand { get; private set; }
        public ICommand GenererAttestationPdfCommand { get; private set; }

        // Catalogue & Organismes
        public ICommand NouvelleFormationCommand { get; private set; }
        public ICommand EnregistrerFormationCommand { get; private set; }
        public ICommand SupprimerFormationCommand { get; private set; }
        public ICommand NouvelOrganismeCommand { get; private set; }
        public ICommand EnregistrerOrganismeCommand { get; private set; }
        public ICommand SupprimerOrganismeCommand { get; private set; }

        // Passerelle RH-03
        public ICommand AssignerBesoinASessionSelectionneeCommand { get; private set; }
        public ICommand DetacherBesoinCommand { get; private set; }

        // Exports
        public ICommand ExporterExcelCommand { get; private set; }
        public ICommand ExporterBilanPdfCommand { get; private set; }

        #endregion

        public FormationViewModel(
            FormationService formationService,
            FormationExportService exportService,
            FichePosteService fichePosteService,
            DataService dataService,
            Dossier dossier)
        {
            _formationService = formationService ?? throw new ArgumentNullException(nameof(formationService));
            _exportService = exportService ?? throw new ArgumentNullException(nameof(exportService));
            _fichePosteService = fichePosteService;
            _dataService = dataService;
            _dossier = dossier ?? throw new ArgumentNullException(nameof(dossier));

            Title = "Formation & TPA";

            AnneesDisponibles = new ObservableCollection<int>();
            int anneeCourante = DateTime.Now.Year;
            for (int y = anneeCourante + 1; y >= anneeCourante - 4; y--)
            {
                AnneesDisponibles.Add(y);
            }

            Plans = new ObservableCollection<PlanFormation>();
            Sessions = new ObservableCollection<SessionFormation>();
            SessionsFiltrees = new ObservableCollection<SessionFormation>();
            Inscriptions = new ObservableCollection<SessionInscription>();
            CatalogueFormations = new ObservableCollection<FormationCatalogue>();
            Organismes = new ObservableCollection<OrganismeFormation>();
            BesoinsRh03 = new ObservableCollection<EntretienBesoinFormation>();
            EmployesDisponibles = new ObservableCollection<Employee>();
            CompetencesDisponibles = new ObservableCollection<Competence>();

            TableauBordTpa = new TableauBordTpaFormation { AnneeExercice = _anneeSelectionnee, TauxTpaLegal = 0.03m };

            InitialiserCommandes();
        }

        private void InitialiserCommandes()
        {
            ChargerToutCommand = new RelayCommand(async () => await ChargerToutAsync());
            RafraichirCommand = new RelayCommand(async () => await ChargerToutAsync());

            // Plan
            NouveauPlanCommand = new RelayCommand(CreerNouveauPlan);
            EnregistrerPlanCommand = new RelayCommand(async () => await EnregistrerPlanAsync(), () => PlanSelectionne != null);
            SupprimerPlanCommand = new RelayCommand(async () => await SupprimerPlanAsync(), () => PlanSelectionne != null);
            ValiderPlanCommand = new RelayCommand(async () => await ValiderPlanAsync(), () => PlanSelectionne != null && PlanSelectionne.Statut == StatutPlanFormation.Elaboration);
            CloturerPlanCommand = new RelayCommand(async () => await CloturerPlanAsync(), () => PlanSelectionne != null && PlanSelectionne.Statut != StatutPlanFormation.Cloture);

            // Session
            NouvelleSessionCommand = new RelayCommand(CreerNouvelleSession, () => PlanSelectionne != null);
            EnregistrerSessionCommand = new RelayCommand(async () => await EnregistrerSessionAsync(), () => SessionSelectionnee != null);
            SupprimerSessionCommand = new RelayCommand(async () => await SupprimerSessionAsync(), () => SessionSelectionnee != null);
            ConfirmerSessionCommand = new RelayCommand(async () => await ChangerStatutSessionAsync(StatutSessionFormation.Confirmee), () => SessionSelectionnee != null);
            RealiserSessionCommand = new RelayCommand(async () => await ChangerStatutSessionAsync(StatutSessionFormation.Realisee), () => SessionSelectionnee != null);
            AnnulerSessionCommand = new RelayCommand(async () => await ChangerStatutSessionAsync(StatutSessionFormation.Annulee), () => SessionSelectionnee != null);

            // Inscriptions
            AjouterParticipantCommand = new RelayCommand(async () => await AjouterParticipantAsync(), () => SessionSelectionnee != null && NouvelEmployeSelectionne != null);
            MettreAJourInscriptionCommand = new RelayCommand(async () => await MettreAJourInscriptionAsync(), () => InscriptionSelectionnee != null);
            SupprimerInscriptionCommand = new RelayCommand(async () => await SupprimerInscriptionAsync(), () => InscriptionSelectionnee != null);
            PointerPresentCommand = new RelayCommand(async () => await PointerPresenceAsync(StatutPresenceSession.Present), () => InscriptionSelectionnee != null);
            PointerAbsentCommand = new RelayCommand(async () => await PointerPresenceAsync(StatutPresenceSession.AbsentJustifie), () => InscriptionSelectionnee != null);
            DelivrerAttestationCommand = new RelayCommand(async () => await DelivrerAttestationAsync(), () => InscriptionSelectionnee != null);
            GenererAttestationPdfCommand = new RelayCommand(async () => await GenererAttestationPdfAsync(), () => InscriptionSelectionnee != null);

            // Catalogue & Organismes
            NouvelleFormationCommand = new RelayCommand(CreerNouvelleFormation);
            EnregistrerFormationCommand = new RelayCommand(async () => await EnregistrerFormationCatalogueAsync(), () => FormationCatalogueSelectionnee != null);
            SupprimerFormationCommand = new RelayCommand(async () => await SupprimerFormationCatalogueAsync(), () => FormationCatalogueSelectionnee != null);

            NouvelOrganismeCommand = new RelayCommand(CreerNouvelOrganisme);
            EnregistrerOrganismeCommand = new RelayCommand(async () => await EnregistrerOrganismeAsync(), () => OrganismeSelectionne != null);
            SupprimerOrganismeCommand = new RelayCommand(async () => await SupprimerOrganismeAsync(), () => OrganismeSelectionne != null);

            // Passerelle RH-03
            AssignerBesoinASessionSelectionneeCommand = new RelayCommand(async () => await AssignerBesoinASessionAsync(), () => BesoinRh03Selectionne != null && SessionSelectionnee != null);
            DetacherBesoinCommand = new RelayCommand(async () => await DetacherBesoinAsync(), () => BesoinRh03Selectionne != null);

            // Exports
            ExporterExcelCommand = new RelayCommand(async () => await ExporterExcelAsync(), () => PlanSelectionne != null);
            ExporterBilanPdfCommand = new RelayCommand(async () => await ExporterBilanPdfAsync(), () => PlanSelectionne != null);
        }

        #region Chargement des Données

        public async Task ChargerToutAsync()
        {
            try
            {
                IsBusy = true;
                StatusMessage = "Chargement du module Formation...";

                // 1. Charger le référentiel des employés
                if (_dataService != null)
                {
                    var emps = await _dataService.LoadEmployeesAsync();
                    EmployesDisponibles.Clear();
                    foreach (var e in emps.OrderBy(x => x.LastName).ThenBy(x => x.FirstName))
                    {
                        EmployesDisponibles.Add(e);
                    }
                }

                // 2. Charger les compétences RH-02
                if (_fichePosteService != null)
                {
                    var comps = await _fichePosteService.ChargerToutesCompetencesAsync();
                    CompetencesDisponibles.Clear();
                    foreach (var c in comps)
                    {
                        CompetencesDisponibles.Add(c);
                    }
                }

                // 3. Charger le catalogue et les organismes
                await ChargerCatalogueEtOrganismesAsync();

                // 4. Charger les plans de l'exercice et le tableau de bord TPA
                await ChargerPlansEtTpaAsync();

                // 5. Charger les besoins d'évaluation RH-03
                await ChargerBesoinsRh03Async();

                StatusMessage = "Données de formation chargées.";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Erreur lors du chargement : {ex.Message}";
            }
            finally
            {
                IsBusy = false;
            }
        }

        public async Task ChargerPlansEtTpaAsync()
        {
            var plans = await _formationService.ObtenirPlansAsync(AnneeSelectionnee);
            Plans.Clear();
            foreach (var p in plans)
            {
                Plans.Add(p);
            }

            PlanSelectionne = Plans.FirstOrDefault();
            await ChargerTpaDashboardAsync();
        }

        public async Task ChargerSessionsPlanAsync()
        {
            Sessions.Clear();
            if (PlanSelectionne != null)
            {
                var sessions = await _formationService.ObtenirSessionsAsync(PlanSelectionne.Id);
                foreach (var s in sessions)
                {
                    Sessions.Add(s);
                }
            }

            AppliquerFiltreSessions();
            SessionSelectionnee = SessionsFiltrees.FirstOrDefault();
        }

        public async Task ChargerInscriptionsSessionAsync()
        {
            Inscriptions.Clear();
            if (SessionSelectionnee != null)
            {
                var insc = await _formationService.ObtenirInscriptionsSessionAsync(SessionSelectionnee.Id);
                foreach (var i in insc)
                {
                    Inscriptions.Add(i);
                }
            }

            InscriptionSelectionnee = Inscriptions.FirstOrDefault();
        }

        public async Task ChargerCatalogueEtOrganismesAsync()
        {
            var cats = await _formationService.ObtenirCatalogueFormationsAsync();
            CatalogueFormations.Clear();
            foreach (var c in cats)
            {
                CatalogueFormations.Add(c);
            }
            FormationCatalogueSelectionnee = CatalogueFormations.FirstOrDefault();

            var orgs = await _formationService.ObtenirOrganismesAsync();
            Organismes.Clear();
            foreach (var o in orgs)
            {
                Organismes.Add(o);
            }
            OrganismeSelectionne = Organismes.FirstOrDefault();
        }

        public async Task ChargerBesoinsRh03Async()
        {
            var besoins = await _formationService.ObtenirBesoinsFormationRh03Async(AnneeSelectionnee);
            BesoinsRh03.Clear();
            foreach (var b in besoins)
            {
                BesoinsRh03.Add(b);
            }
            BesoinRh03Selectionne = BesoinsRh03.FirstOrDefault();
        }

        public async Task ChargerTpaDashboardAsync()
        {
            int? planId = PlanSelectionne?.Id;
            TableauBordTpa = await _formationService.ObtenirTableauBordTpaAsync(AnneeSelectionnee, planId);
        }

        private void AppliquerFiltreSessions()
        {
            SessionsFiltrees.Clear();
            IEnumerable<SessionFormation> query = Sessions;

            if (_filtreStatutSession != "Toutes")
            {
                query = query.Where(s => s.StatutLibelle.Equals(_filtreStatutSession, StringComparison.OrdinalIgnoreCase));
            }

            if (!string.IsNullOrWhiteSpace(_rechercheSessionTexte))
            {
                string search = _rechercheSessionTexte.Trim().ToLowerInvariant();
                query = query.Where(s =>
                    (s.CodeSession != null && s.CodeSession.ToLowerInvariant().Contains(search)) ||
                    (s.IntituleSession != null && s.IntituleSession.ToLowerInvariant().Contains(search)) ||
                    (s.FormationIntitule != null && s.FormationIntitule.ToLowerInvariant().Contains(search)) ||
                    (s.FormateurNom != null && s.FormateurNom.ToLowerInvariant().Contains(search)) ||
                    (s.OrganismeNom != null && s.OrganismeNom.ToLowerInvariant().Contains(search)));
            }

            foreach (var s in query)
            {
                SessionsFiltrees.Add(s);
            }
        }

        #endregion

        #region Actions Plan de Formation

        private void CreerNouveauPlan()
        {
            var plan = new PlanFormation
            {
                Code = $"PLAN-{AnneeSelectionnee}-{(Plans.Count + 1):D2}",
                Titre = $"Plan Annuel de Formation {AnneeSelectionnee}",
                AnneeExercice = AnneeSelectionnee,
                BudgetPrevisionnel = 5000000m,
                Statut = StatutPlanFormation.Elaboration,
                ObjectifsStrategiques = "Développement des compétences métiers et managériales."
            };
            Plans.Add(plan);
            PlanSelectionne = plan;
        }

        private async Task EnregistrerPlanAsync()
        {
            if (PlanSelectionne == null) return;
            try
            {
                IsBusy = true;
                await _formationService.EnregistrerPlanAsync(PlanSelectionne);
                StatusMessage = $"Plan '{PlanSelectionne.Code}' enregistré avec succès.";
                await ChargerPlansEtTpaAsync();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erreur lors de l'enregistrement du plan : {ex.Message}", "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                IsBusy = false;
            }
        }

        private async Task SupprimerPlanAsync()
        {
            if (PlanSelectionne == null) return;

            var res = MessageBox.Show(
                $"Êtes-vous certain de vouloir supprimer le plan '{PlanSelectionne.Code}' ?\nCette opération supprimera toutes les sessions et inscriptions associées.",
                "Confirmation", MessageBoxButton.YesNo, MessageBoxImage.Warning);

            if (res != MessageBoxResult.Yes) return;

            try
            {
                IsBusy = true;
                await _formationService.SupprimerPlanFormationAsync(PlanSelectionne.Id);
                StatusMessage = "Plan de formation supprimé.";
                await ChargerPlansEtTpaAsync();
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Suppression impossible", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            finally
            {
                IsBusy = false;
            }
        }

        private async Task ValiderPlanAsync()
        {
            if (PlanSelectionne == null) return;
            try
            {
                await _formationService.ChangerStatutPlanAsync(PlanSelectionne.Id, StatutPlanFormation.Valide);
                PlanSelectionne.Statut = StatutPlanFormation.Valide;
                PlanSelectionne.DateValidation = DateTime.Now;
                StatusMessage = "Plan validé avec succès.";
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async Task CloturerPlanAsync()
        {
            if (PlanSelectionne == null) return;
            try
            {
                await _formationService.ChangerStatutPlanAsync(PlanSelectionne.Id, StatutPlanFormation.Cloture);
                PlanSelectionne.Statut = StatutPlanFormation.Cloture;
                StatusMessage = "Plan clôturé avec succès.";
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        #endregion

        #region Actions Session de Formation

        private void CreerNouvelleSession()
        {
            if (PlanSelectionne == null) return;

            var sess = new SessionFormation
            {
                CodeSession = $"SESS-{AnneeSelectionnee}-{(Sessions.Count + 1):D3}",
                PlanFormationId = PlanSelectionne.Id,
                FormationId = CatalogueFormations.FirstOrDefault()?.Id ?? 1,
                OrganismeId = Organismes.FirstOrDefault()?.Id,
                DateDebut = DateTime.Today,
                DateFin = DateTime.Today.AddDays(2),
                Statut = StatutSessionFormation.Planifiee,
                Lieu = "Salle de formation",
                CoutPedagogique = 500000m,
                CoutLogistique = 100000m,
                CoutAutre = 0m
            };
            sess.RecalculerCoutTotal();

            Sessions.Add(sess);
            SessionsFiltrees.Add(sess);
            SessionSelectionnee = sess;
        }

        private async Task EnregistrerSessionAsync()
        {
            if (SessionSelectionnee == null) return;
            try
            {
                IsBusy = true;
                await _formationService.EnregistrerSessionAsync(SessionSelectionnee);
                StatusMessage = $"Session '{SessionSelectionnee.CodeSession}' enregistrée.";
                await ChargerSessionsPlanAsync();
                await ChargerTpaDashboardAsync();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erreur lors de l'enregistrement de la session : {ex.Message}", "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                IsBusy = false;
            }
        }

        private async Task SupprimerSessionAsync()
        {
            if (SessionSelectionnee == null) return;

            var res = MessageBox.Show(
                $"Supprimer la session '{SessionSelectionnee.CodeSession}' et toutes ses inscriptions ?",
                "Confirmation", MessageBoxButton.YesNo, MessageBoxImage.Warning);

            if (res != MessageBoxResult.Yes) return;

            try
            {
                IsBusy = true;
                await _formationService.SupprimerSessionFormationAsync(SessionSelectionnee.Id);
                StatusMessage = "Session supprimée.";
                await ChargerSessionsPlanAsync();
                await ChargerTpaDashboardAsync();
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Suppression impossible", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            finally
            {
                IsBusy = false;
            }
        }

        private async Task ChangerStatutSessionAsync(StatutSessionFormation nouveauStatut)
        {
            if (SessionSelectionnee == null) return;
            try
            {
                await _formationService.ChangerStatutSessionAsync(SessionSelectionnee.Id, nouveauStatut);
                SessionSelectionnee.Statut = nouveauStatut;
                StatusMessage = $"Statut de la session mis à jour vers : {SessionSelectionnee.StatutLibelle}";
                await ChargerTpaDashboardAsync();
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        #endregion

        #region Actions Inscriptions & Participants

        private async Task AjouterParticipantAsync()
        {
            if (SessionSelectionnee == null || NouvelEmployeSelectionne == null) return;

            try
            {
                IsBusy = true;
                await _formationService.InscrireParticipantAsync(SessionSelectionnee.Id, NouvelEmployeSelectionne.EmployeeId, NouveauxFraisImputes);
                StatusMessage = $"Salarié {NouvelEmployeSelectionne.FirstName} {NouvelEmployeSelectionne.LastName} inscrit.";
                await ChargerInscriptionsSessionAsync();
                await ChargerSessionsPlanAsync();
                await ChargerTpaDashboardAsync();
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Inscription refusée", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            finally
            {
                IsBusy = false;
            }
        }

        private async Task MettreAJourInscriptionAsync()
        {
            if (InscriptionSelectionnee == null) return;
            try
            {
                await _formationService.MettreAJourInscriptionAsync(InscriptionSelectionnee);
                StatusMessage = "Inscription mise à jour.";
                await ChargerSessionsPlanAsync();
                await ChargerTpaDashboardAsync();
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async Task SupprimerInscriptionAsync()
        {
            if (InscriptionSelectionnee == null) return;

            try
            {
                await _formationService.SupprimerInscriptionAsync(InscriptionSelectionnee.Id);
                StatusMessage = "Inscription retirée.";
                await ChargerInscriptionsSessionAsync();
                await ChargerSessionsPlanAsync();
                await ChargerTpaDashboardAsync();
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Action refusée", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private async Task PointerPresenceAsync(StatutPresenceSession presence)
        {
            if (InscriptionSelectionnee == null) return;
            try
            {
                bool attestation = presence == StatutPresenceSession.Present;
                await _formationService.PointerPresenceAsync(InscriptionSelectionnee.Id, presence, attestation);
                InscriptionSelectionnee.StatutPresence = presence;
                InscriptionSelectionnee.AttestationObtenue = attestation;
                StatusMessage = $"Présence pointée : {InscriptionSelectionnee.StatutPresenceLibelle}";
                await ChargerSessionsPlanAsync();
                await ChargerTpaDashboardAsync();
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async Task DelivrerAttestationAsync()
        {
            if (InscriptionSelectionnee == null) return;
            try
            {
                bool nouvelEtat = !InscriptionSelectionnee.AttestationObtenue;
                await _formationService.PointerPresenceAsync(InscriptionSelectionnee.Id, InscriptionSelectionnee.StatutPresence, nouvelEtat);
                InscriptionSelectionnee.AttestationObtenue = nouvelEtat;
                StatusMessage = nouvelEtat ? "Attestation officielle délivrée." : "Délivrance de l'attestation annulée.";
                await ChargerSessionsPlanAsync();
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async Task GenererAttestationPdfAsync()
        {
            if (SessionSelectionnee == null || InscriptionSelectionnee == null) return;

            var sfd = new SaveFileDialog
            {
                Filter = "Fichier PDF (*.pdf)|*.pdf",
                FileName = $"Attestation_{InscriptionSelectionnee.EmployeeMatricule}_{SessionSelectionnee.CodeSession}.pdf",
                Title = "Enregistrer l'attestation individuelle de formation"
            };

            if (sfd.ShowDialog() != true) return;

            try
            {
                IsBusy = true;
                bool success = await Task.Run(() => _exportService.GenererAttestationFormationPdf(SessionSelectionnee, InscriptionSelectionnee, _dossier, sfd.FileName));
                if (success)
                {
                    StatusMessage = "Attestation PDF générée avec succès.";
                    var res = MessageBox.Show("Attestation PDF générée avec succès. Souhaitez-vous l'ouvrir ?", "Succès", MessageBoxButton.YesNo, MessageBoxImage.Information);
                    if (res == MessageBoxResult.Yes)
                    {
                        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(sfd.FileName) { UseShellExecute = true });
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erreur lors de la génération PDF : {ex.Message}", "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                IsBusy = false;
            }
        }

        #endregion

        #region Actions Catalogue & Organismes

        private void CreerNouvelleFormation()
        {
            var f = new FormationCatalogue
            {
                Code = $"FORM-{(CatalogueFormations.Count + 1):D3}",
                Intitule = "Nouvelle formation",
                Domaine = "Général",
                DureeHeures = 16,
                CoutEstimeParParticipant = 100000m,
                TypeFormation = TypeActionFormation.Interne,
                Actif = true
            };
            CatalogueFormations.Add(f);
            FormationCatalogueSelectionnee = f;
        }

        private async Task EnregistrerFormationCatalogueAsync()
        {
            if (FormationCatalogueSelectionnee == null) return;
            try
            {
                IsBusy = true;
                await _formationService.EnregistrerFormationCatalogueAsync(FormationCatalogueSelectionnee);
                StatusMessage = "Formation enregistrée au catalogue.";
                await ChargerCatalogueEtOrganismesAsync();
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                IsBusy = false;
            }
        }

        private async Task SupprimerFormationCatalogueAsync()
        {
            if (FormationCatalogueSelectionnee == null) return;

            try
            {
                await _formationService.SupprimerFormationCatalogueAsync(FormationCatalogueSelectionnee.Id);
                StatusMessage = "Formation supprimée du catalogue.";
                await ChargerCatalogueEtOrganismesAsync();
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Suppression impossible", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void CreerNouvelOrganisme()
        {
            var o = new OrganismeFormation
            {
                RaisonSociale = "Nouvel organisme de formation",
                Actif = true
            };
            Organismes.Add(o);
            OrganismeSelectionne = o;
        }

        private async Task EnregistrerOrganismeAsync()
        {
            if (OrganismeSelectionne == null) return;
            try
            {
                IsBusy = true;
                await _formationService.EnregistrerOrganismeAsync(OrganismeSelectionne);
                StatusMessage = "Organisme de formation enregistré.";
                await ChargerCatalogueEtOrganismesAsync();
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                IsBusy = false;
            }
        }

        private async Task SupprimerOrganismeAsync()
        {
            if (OrganismeSelectionne == null) return;

            try
            {
                await _formationService.SupprimerOrganismeAsync(OrganismeSelectionne.Id);
                StatusMessage = "Organisme supprimé.";
                await ChargerCatalogueEtOrganismesAsync();
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Suppression impossible", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        #endregion

        #region Actions Passerelle RH-03

        private async Task AssignerBesoinASessionAsync()
        {
            if (BesoinRh03Selectionne == null || SessionSelectionnee == null) return;

            try
            {
                IsBusy = true;
                await _formationService.AssignerBesoinASessionAsync(BesoinRh03Selectionne.Id, SessionSelectionnee.Id);
                BesoinRh03Selectionne.Statut = StatutBesoinFormation.Retenu;
                StatusMessage = $"Besoin '{BesoinRh03Selectionne.ThemeFormation}' rattaché à la session '{SessionSelectionnee.CodeSession}'.";
                await ChargerBesoinsRh03Async();
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                IsBusy = false;
            }
        }

        private async Task DetacherBesoinAsync()
        {
            if (BesoinRh03Selectionne == null) return;

            try
            {
                await _formationService.DetacherBesoinSessionAsync(BesoinRh03Selectionne.Id);
                BesoinRh03Selectionne.Statut = StatutBesoinFormation.Demande;
                StatusMessage = "Besoin détaché de la session.";
                await ChargerBesoinsRh03Async();
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        #endregion

        #region Exports Excel & Bilan PDF

        private async Task ExporterExcelAsync()
        {
            if (PlanSelectionne == null) return;

            var sfd = new SaveFileDialog
            {
                Filter = "Classeur Excel (*.xlsx)|*.xlsx",
                FileName = $"Synthese_Formation_TPA_{PlanSelectionne.Code}_{AnneeSelectionnee}.xlsx",
                Title = "Exporter la synthèse de la formation professionnelle"
            };

            if (sfd.ShowDialog() != true) return;

            try
            {
                IsBusy = true;
                var sessions = await _formationService.ObtenirSessionsAsync(PlanSelectionne.Id);
                var participants = await _formationService.ObtenirToutesInscriptionsPlanAsync(PlanSelectionne.Id);
                var besoins = await _formationService.ObtenirBesoinsFormationRh03Async(AnneeSelectionnee);

                bool ok = await Task.Run(() => _exportService.GenererSyntheseFormationExcel(
                    PlanSelectionne, TableauBordTpa, sessions, participants, besoins, _dossier, sfd.FileName));

                if (ok)
                {
                    StatusMessage = "Classeur Excel exporté avec succès.";
                    var res = MessageBox.Show("Synthèse Excel générée avec succès. Souhaitez-vous l'ouvrir ?", "Succès", MessageBoxButton.YesNo, MessageBoxImage.Information);
                    if (res == MessageBoxResult.Yes)
                    {
                        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(sfd.FileName) { UseShellExecute = true });
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erreur lors de l'exportation Excel : {ex.Message}", "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                IsBusy = false;
            }
        }

        private async Task ExporterBilanPdfAsync()
        {
            if (PlanSelectionne == null) return;

            var sfd = new SaveFileDialog
            {
                Filter = "Fichier PDF (*.pdf)|*.pdf",
                FileName = $"Bilan_Formation_TPA_{PlanSelectionne.Code}_{AnneeSelectionnee}.pdf",
                Title = "Exporter le bilan annuel du plan de formation"
            };

            if (sfd.ShowDialog() != true) return;

            try
            {
                IsBusy = true;
                var sessions = await _formationService.ObtenirSessionsAsync(PlanSelectionne.Id);

                bool ok = await Task.Run(() => _exportService.GenererBilanAnnuelPdf(
                    PlanSelectionne, TableauBordTpa, sessions, _dossier, sfd.FileName));

                if (ok)
                {
                    StatusMessage = "Bilan PDF exporté avec succès.";
                    var res = MessageBox.Show("Bilan PDF généré avec succès. Souhaitez-vous l'ouvrir ?", "Succès", MessageBoxButton.YesNo, MessageBoxImage.Information);
                    if (res == MessageBoxResult.Yes)
                    {
                        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(sfd.FileName) { UseShellExecute = true });
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erreur lors de l'exportation PDF : {ex.Message}", "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                IsBusy = false;
            }
        }

        #endregion
    }
}
