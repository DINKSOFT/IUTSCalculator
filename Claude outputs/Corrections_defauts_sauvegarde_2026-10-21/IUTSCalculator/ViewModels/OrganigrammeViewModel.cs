using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
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
    /// ViewModel de l'organigramme d'entreprise (module RH-01).
    /// Gère l'affichage graphique interactif, la nomenclature des postes,
    /// la hiérarchie des directions/services et les rattachements des salariés.
    /// </summary>
    public class OrganigrammeViewModel : BaseViewModel
    {
        private readonly OrganigrammeService _organigrammeService;
        private readonly OrganigrammeExportService _exportService;
        private readonly DataService _dataService;
        private readonly Dossier _dossier;
        private readonly FichePosteService _fichePosteService;
        private readonly FichePosteExportService _fichePosteExportService;

        public OrganigrammeViewModel(
            OrganigrammeService organigrammeService,
            OrganigrammeExportService exportService,
            DataService dataService,
            Dossier dossier,
            FichePosteService fichePosteService = null,
            FichePosteExportService fichePosteExportService = null)
        {
            _organigrammeService = organigrammeService ?? throw new ArgumentNullException(nameof(organigrammeService));
            _exportService = exportService ?? throw new ArgumentNullException(nameof(exportService));
            _dataService = dataService ?? throw new ArgumentNullException(nameof(dataService));
            _dossier = dossier ?? throw new ArgumentNullException(nameof(dossier));
            _fichePosteService = fichePosteService ?? new FichePosteService(new SQLiteService(_dossier.DbFileName));
            _fichePosteExportService = fichePosteExportService ?? new FichePosteExportService();

            Title = "Organigramme";

            // Initialisation des commandes
            RechargerCommand = new RelayCommand(async () => await ChargerToutAsync(), () => !IsBusy);
            InitialiserDefautCommand = new RelayCommand(async () => await InitialiserStructureDefautAsync(), () => !IsBusy);

            ZoomInCommand = new RelayCommand(() => ZoomLevel = Math.Min(2.0, ZoomLevel + 0.1));
            ZoomOutCommand = new RelayCommand(() => ZoomLevel = Math.Max(0.5, ZoomLevel - 0.1));
            ResetZoomCommand = new RelayCommand(() => ZoomLevel = 1.0);

            DeplierToutCommand = new RelayCommand(() => BasculerDepliage(true));
            ReplierToutCommand = new RelayCommand(() => BasculerDepliage(false));

            // Commandes Unités
            NouvelleUniteCommand = new RelayCommand(NouvelleUnite);
            EditerUniteCommand = new RelayCommand(EditerUnite, () => UniteSelectionnee != null);
            EnregistrerUniteCommand = new RelayCommand(async () => await EnregistrerUniteAsync(), () => UniteEnEdition != null && !IsBusy);
            SupprimerUniteCommand = new RelayCommand(async () => await SupprimerUniteAsync(), () => UniteSelectionnee != null && !IsBusy);
            AnnulerEditionUniteCommand = new RelayCommand(() => EstEnEditionUnite = false);

            // Commandes Postes
            NouveauPosteCommand = new RelayCommand(NouveauPoste);
            EditerPosteCommand = new RelayCommand(EditerPoste, () => PosteSelectionne != null);
            EnregistrerPosteCommand = new RelayCommand(async () => await EnregistrerPosteAsync(), () => PosteEnEdition != null && !IsBusy);
            SupprimerPosteCommand = new RelayCommand(async () => await SupprimerPosteAsync(), () => PosteSelectionne != null && !IsBusy);
            AnnulerEditionPosteCommand = new RelayCommand(() => EstEnEditionPoste = false);

            // Commandes Fiche de poste (RH-02)
            AjouterCompetencePosteCommand = new RelayCommand(async () => await AjouterCompetencePosteAsync(), () => PosteEnEdition != null && PosteEnEdition.Id > 0 && CompetenceSelectionneePourAjout != null && !IsBusy);
            SupprimerCompetencePosteCommand = new RelayCommand(async () => await SupprimerCompetencePosteAsync(), () => CompetencePosteSelectionnee != null && !IsBusy);
            ExporterFichePostePdfCommand = new RelayCommand(ExporterFichePostePdf, () => (PosteSelectionne != null || PosteEnEdition != null) && !IsBusy);
            ActualiserSalaireConventionnelCommand = new RelayCommand(async () => await ActualiserSalaireConventionnelAsync());

            // Commandes Affectations
            EnregistrerAffectationCommand = new RelayCommand(async () => await EnregistrerAffectationAsync(), () => AffectationSelectionnee != null && !IsBusy);

            // Exports
            ExporterExcelCommand = new RelayCommand(ExporterExcel, () => !IsBusy);
            ExporterPdfCommand = new RelayCommand(ExporterPdf, () => !IsBusy);
        }

        #region Propriétés d'état & Navigation

        private int _selectedTabIndex;
        public int SelectedTabIndex
        {
            get => _selectedTabIndex;
            set
            {
                if (_selectedTabIndex != value)
                {
                    _selectedTabIndex = value;
                    OnPropertyChanged();
                }
            }
        }

        private string _rechercheTexte;
        public string RechercheTexte
        {
            get => _rechercheTexte;
            set
            {
                if (_rechercheTexte != value)
                {
                    _rechercheTexte = value;
                    OnPropertyChanged();
                    AppliquerFiltreRecherche();
                }
            }
        }

        private double _zoomLevel = 1.0;
        public double ZoomLevel
        {
            get => _zoomLevel;
            set
            {
                if (Math.Abs(_zoomLevel - value) > 0.001)
                {
                    _zoomLevel = Math.Max(0.5, Math.Min(2.0, value));
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(ZoomLevelPourcentage));
                }
            }
        }

        public string ZoomLevelPourcentage => $"{Math.Round(_zoomLevel * 100)} %";

        private bool _inclurePostesDansArbre = true;
        public bool InclurePostesDansArbre
        {
            get => _inclurePostesDansArbre;
            set
            {
                if (_inclurePostesDansArbre != value)
                {
                    _inclurePostesDansArbre = value;
                    OnPropertyChanged();
                    _ = ChargerArbreAsync();
                }
            }
        }

        #endregion

        #region Données de Synthèse & Compteurs

        private int _totalUnites;
        public int TotalUnites { get => _totalUnites; set { _totalUnites = value; OnPropertyChanged(); } }

        private int _totalDirections;
        public int TotalDirections { get => _totalDirections; set { _totalDirections = value; OnPropertyChanged(); } }

        private int _totalServices;
        public int TotalServices { get => _totalServices; set { _totalServices = value; OnPropertyChanged(); } }

        private int _totalPostes;
        public int TotalPostes { get => _totalPostes; set { _totalPostes = value; OnPropertyChanged(); } }

        private int _totalPostesPourvus;
        public int TotalPostesPourvus { get => _totalPostesPourvus; set { _totalPostesPourvus = value; OnPropertyChanged(); } }

        private int _totalPostesVacants;
        public int TotalPostesVacants { get => _totalPostesVacants; set { _totalPostesVacants = value; OnPropertyChanged(); } }

        private int _totalSalariesActifs;
        public int TotalSalariesActifs { get => _totalSalariesActifs; set { _totalSalariesActifs = value; OnPropertyChanged(); } }

        #endregion

        #region Arbre Graphique

        public ObservableCollection<NoeudOrganigramme> ArbreRacines { get; } = new ObservableCollection<NoeudOrganigramme>();

        private NoeudOrganigramme _noeudSelectionne;
        public NoeudOrganigramme NoeudSelectionne
        {
            get => _noeudSelectionne;
            set
            {
                if (_noeudSelectionne != value)
                {
                    if (_noeudSelectionne != null) _noeudSelectionne.IsSelected = false;
                    _noeudSelectionne = value;
                    if (_noeudSelectionne != null) _noeudSelectionne.IsSelected = true;
                    OnPropertyChanged();
                }
            }
        }

        #endregion

        #region Directions & Services

        private List<UniteOrganisationnelle> _toutesUnites = new List<UniteOrganisationnelle>();
        public ObservableCollection<UniteOrganisationnelle> Unites { get; } = new ObservableCollection<UniteOrganisationnelle>();

        private UniteOrganisationnelle _uniteSelectionnee;
        public UniteOrganisationnelle UniteSelectionnee
        {
            get => _uniteSelectionnee;
            set
            {
                if (_uniteSelectionnee != value)
                {
                    _uniteSelectionnee = value;
                    OnPropertyChanged();
                    EditerUnite();
                }
            }
        }

        private UniteOrganisationnelle _uniteEnEdition;
        public UniteOrganisationnelle UniteEnEdition
        {
            get => _uniteEnEdition;
            set { _uniteEnEdition = value; OnPropertyChanged(); }
        }

        private bool _estEnEditionUnite;
        public bool EstEnEditionUnite
        {
            get => _estEnEditionUnite;
            set { _estEnEditionUnite = value; OnPropertyChanged(); }
        }

        public ObservableCollection<UniteOrganisationnelle> ParentsDisponibles { get; } = new ObservableCollection<UniteOrganisationnelle>();
        public ObservableCollection<Employee> ResponsablesDisponibles { get; } = new ObservableCollection<Employee>();

        public IEnumerable<TypeUniteOrganisationnelle> TypesUniteDisponibles =>
            Enum.GetValues(typeof(TypeUniteOrganisationnelle)).Cast<TypeUniteOrganisationnelle>();

        #endregion

        #region Postes

        private List<Poste> _tousPostes = new List<Poste>();
        public ObservableCollection<Poste> Postes { get; } = new ObservableCollection<Poste>();

        private Poste _posteSelectionne;
        public Poste PosteSelectionne
        {
            get => _posteSelectionne;
            set
            {
                if (_posteSelectionne != value)
                {
                    _posteSelectionne = value;
                    OnPropertyChanged();
                    EditerPoste();
                }
            }
        }

        private Poste _posteEnEdition;
        public Poste PosteEnEdition
        {
            get => _posteEnEdition;
            set { _posteEnEdition = value; OnPropertyChanged(); }
        }

        private bool _estEnEditionPoste;
        public bool EstEnEditionPoste
        {
            get => _estEnEditionPoste;
            set { _estEnEditionPoste = value; OnPropertyChanged(); }
        }

        private string _filtreStatutPoste = "Tous";
        public string FiltreStatutPoste
        {
            get => _filtreStatutPoste;
            set
            {
                if (_filtreStatutPoste != value)
                {
                    _filtreStatutPoste = value;
                    OnPropertyChanged();
                    FiltrerPostes();
                }
            }
        }

        public ObservableCollection<Poste> SuperieursDisponibles { get; } = new ObservableCollection<Poste>();
        public ObservableCollection<Employee> TitulairesDisponibles { get; } = new ObservableCollection<Employee>();
        public ObservableCollection<UniteOrganisationnelle> UnitesRattachementDisponibles { get; } = new ObservableCollection<UniteOrganisationnelle>();

        // RH-02 : Compétences requises et Titulaires multiples du poste en cours d'édition
        public ObservableCollection<PosteCompetence> CompetencesPosteEnEdition { get; } = new ObservableCollection<PosteCompetence>();
        public ObservableCollection<TitulairePosteLigne> TitulairesPosteEnEdition { get; } = new ObservableCollection<TitulairePosteLigne>();
        public ObservableCollection<Competence> CompetencesDisponibles { get; } = new ObservableCollection<Competence>();

        private Competence _competenceSelectionneePourAjout;
        public Competence CompetenceSelectionneePourAjout
        {
            get => _competenceSelectionneePourAjout;
            set => SetProperty(ref _competenceSelectionneePourAjout, value);
        }

        private int _niveauMaitrisePourAjout = 2;
        public int NiveauMaitrisePourAjout
        {
            get => _niveauMaitrisePourAjout;
            set => SetProperty(ref _niveauMaitrisePourAjout, value);
        }

        private bool _estObligatoirePourAjout = true;
        public bool EstObligatoirePourAjout
        {
            get => _estObligatoirePourAjout;
            set => SetProperty(ref _estObligatoirePourAjout, value);
        }

        private PosteCompetence _competencePosteSelectionnee;
        public PosteCompetence CompetencePosteSelectionnee
        {
            get => _competencePosteSelectionnee;
            set => SetProperty(ref _competencePosteSelectionnee, value);
        }

        #endregion

        #region Salariés & Affectations

        private List<AffectationSalarieLigne> _toutesAffectations = new List<AffectationSalarieLigne>();
        public ObservableCollection<AffectationSalarieLigne> Affectations { get; } = new ObservableCollection<AffectationSalarieLigne>();

        private AffectationSalarieLigne _affectationSelectionnee;
        public AffectationSalarieLigne AffectationSelectionnee
        {
            get => _affectationSelectionnee;
            set
            {
                if (_affectationSelectionnee != value)
                {
                    _affectationSelectionnee = value;
                    OnPropertyChanged();
                    ChargerDetailAffectation();
                }
            }
        }

        private string _serviceAffectationChoisi;
        public string ServiceAffectationChoisi
        {
            get => _serviceAffectationChoisi;
            set { _serviceAffectationChoisi = value; OnPropertyChanged(); }
        }

        private Poste _posteAffectationChoisi;
        public Poste PosteAffectationChoisi
        {
            get => _posteAffectationChoisi;
            set { _posteAffectationChoisi = value; OnPropertyChanged(); }
        }

        private Employee _managerAffectationChoisi;
        public Employee ManagerAffectationChoisi
        {
            get => _managerAffectationChoisi;
            set { _managerAffectationChoisi = value; OnPropertyChanged(); }
        }

        public ObservableCollection<string> ServicesDisponibles { get; } = new ObservableCollection<string>();

        #endregion

        #region Commandes

        public ICommand RechargerCommand { get; }
        public ICommand InitialiserDefautCommand { get; }

        public ICommand ZoomInCommand { get; }
        public ICommand ZoomOutCommand { get; }
        public ICommand ResetZoomCommand { get; }
        public ICommand DeplierToutCommand { get; }
        public ICommand ReplierToutCommand { get; }

        public ICommand NouvelleUniteCommand { get; }
        public ICommand EditerUniteCommand { get; }
        public ICommand EnregistrerUniteCommand { get; }
        public ICommand SupprimerUniteCommand { get; }
        public ICommand AnnulerEditionUniteCommand { get; }

        public ICommand NouveauPosteCommand { get; }
        public ICommand EditerPosteCommand { get; }
        public ICommand EnregistrerPosteCommand { get; }
        public ICommand SupprimerPosteCommand { get; }
        public ICommand AnnulerEditionPosteCommand { get; }

        // Commandes Fiche de poste (RH-02)
        public ICommand AjouterCompetencePosteCommand { get; }
        public ICommand SupprimerCompetencePosteCommand { get; }
        public ICommand ExporterFichePostePdfCommand { get; }
        public ICommand ActualiserSalaireConventionnelCommand { get; }

        public ICommand EnregistrerAffectationCommand { get; }

        public ICommand ExporterExcelCommand { get; }
        public ICommand ExporterPdfCommand { get; }

        #endregion

        #region Chargement & Synchronisation

        protected override void OnIsActiveChanged()
        {
            base.OnIsActiveChanged();
            if (IsActive)
            {
                _ = ChargerToutAsync();
            }
        }

        public async Task ChargerToutAsync()
        {
            try
            {
                IsBusy = true;
                StatusMessage = "Chargement de l'organigramme...";

                // 1. Charger salariés pour les listes déroulantes
                var tousEmployes = await _dataService.LoadEmployeesAsync();
                var employesActifs = tousEmployes.Where(e => e.EstActif).OrderBy(e => e.LastName).ThenBy(e => e.FirstName).ToList();

                ResponsablesDisponibles.Clear();
                ResponsablesDisponibles.Add(new Employee { EmployeeId = "", FirstName = "—", LastName = "Aucun responsable —" });
                foreach (var e in employesActifs) ResponsablesDisponibles.Add(e);

                TitulairesDisponibles.Clear();
                TitulairesDisponibles.Add(new Employee { EmployeeId = "", FirstName = "—", LastName = "Poste vacant —" });
                foreach (var e in employesActifs) TitulairesDisponibles.Add(e);

                TotalSalariesActifs = employesActifs.Count;

                // 2. Charger Unités
                _toutesUnites = await _organigrammeService.ChargerUnitesAsync();
                UnitesRattachementDisponibles.Clear();
                UnitesRattachementDisponibles.Add(new UniteOrganisationnelle { Id = 0, Code = "", Libelle = "— Aucune unité rattachée —" });
                foreach (var u in _toutesUnites) UnitesRattachementDisponibles.Add(u);

                FiltrerUnites();

                TotalUnites = _toutesUnites.Count;
                TotalDirections = _toutesUnites.Count(u => u.EstDirection);
                TotalServices = _toutesUnites.Count(u => u.EstService);

                // 3. Charger Postes
                _tousPostes = await _organigrammeService.ChargerPostesAsync();
                FiltrerPostes();

                TotalPostes = _tousPostes.Count;
                TotalPostesPourvus = _tousPostes.Count(p => p.EstPourvu);
                TotalPostesVacants = _tousPostes.Count(p => !p.EstPourvu);

                // 4. Charger Affectations
                _toutesAffectations = await _organigrammeService.ChargerAffectationsSalariesAsync();
                FiltrerAffectations();

                // Services uniques pour le formulaire d'affectation
                ServicesDisponibles.Clear();
                foreach (var u in _toutesUnites.Select(u => u.Libelle).Distinct().OrderBy(s => s))
                {
                    ServicesDisponibles.Add(u);
                }

                // 5. Charger Arbre Graphique
                await ChargerArbreAsync();

                StatusMessage = "Organigramme prêt.";
            }
            catch (Exception ex)
            {
                StatusMessage = "Erreur de chargement : " + ex.Message;
            }
            finally
            {
                IsBusy = false;
            }
        }

        private async Task ChargerArbreAsync()
        {
            var racines = await _organigrammeService.ChargerArbreOrganigrammeAsync(_inclurePostesDansArbre);
            ArbreRacines.Clear();
            foreach (var r in racines) ArbreRacines.Add(r);
        }

        private async Task InitialiserStructureDefautAsync()
        {
            if (_toutesUnites.Count > 0)
            {
                var rep = MessageBox.Show(
                    "L'organigramme contient déjà des unités. L'initialisation par défaut est réservée aux dossiers sans structure définie.\n\nSouhaitez-vous quand même réinitialiser ?",
                    "Initialisation de l'organigramme",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);
                if (rep != MessageBoxResult.Yes) return;
            }

            try
            {
                IsBusy = true;
                StatusMessage = "Initialisation de la structure d'entreprise...";
                await _organigrammeService.InitialiserStructureDefautAsync();
                await ChargerToutAsync();
                StatusMessage = "Structure d'organigramme initialisée avec succès.";
            }
            catch (Exception ex)
            {
                StatusMessage = "Erreur d'initialisation : " + ex.Message;
            }
            finally
            {
                IsBusy = false;
            }
        }

        #endregion

        #region Logique Unités

        private void FiltrerUnites()
        {
            Unites.Clear();
            var q = string.IsNullOrWhiteSpace(_rechercheTexte) ? _toutesUnites :
                _toutesUnites.Where(u =>
                    (!string.IsNullOrEmpty(u.Code) && u.Code.IndexOf(_rechercheTexte, StringComparison.OrdinalIgnoreCase) >= 0) ||
                    (!string.IsNullOrEmpty(u.Libelle) && u.Libelle.IndexOf(_rechercheTexte, StringComparison.OrdinalIgnoreCase) >= 0) ||
                    (!string.IsNullOrEmpty(u.ResponsableNom) && u.ResponsableNom.IndexOf(_rechercheTexte, StringComparison.OrdinalIgnoreCase) >= 0));

            foreach (var u in q) Unites.Add(u);
        }

        private void NouvelleUnite()
        {
            UniteEnEdition = new UniteOrganisationnelle
            {
                Id = 0,
                Code = "SRV-" + (_toutesUnites.Count + 1).ToString("D2"),
                Libelle = "Nouveau service",
                TypeUnite = TypeUniteOrganisationnelle.Service,
                ParentId = _toutesUnites.FirstOrDefault(u => u.EstDirection)?.Id,
                Couleur = "#14675A",
                Ordre = _toutesUnites.Count + 1
            };
            MettreAJourParentsDisponibles(0);
            EstEnEditionUnite = true;
        }

        private void EditerUnite()
        {
            if (UniteSelectionnee == null) return;

            UniteEnEdition = new UniteOrganisationnelle
            {
                Id = UniteSelectionnee.Id,
                Code = UniteSelectionnee.Code,
                Libelle = UniteSelectionnee.Libelle,
                TypeUnite = UniteSelectionnee.TypeUnite,
                ParentId = UniteSelectionnee.ParentId,
                ResponsableMatricule = UniteSelectionnee.ResponsableMatricule,
                Description = UniteSelectionnee.Description,
                Couleur = UniteSelectionnee.Couleur,
                Ordre = UniteSelectionnee.Ordre
            };

            MettreAJourParentsDisponibles(UniteSelectionnee.Id);
            EstEnEditionUnite = true;
        }

        private void MettreAJourParentsDisponibles(int idExclu)
        {
            ParentsDisponibles.Clear();
            ParentsDisponibles.Add(new UniteOrganisationnelle { Id = 0, Code = "", Libelle = "— Niveau racine (Aucun parent) —" });
            foreach (var u in _toutesUnites.Where(u => u.Id != idExclu))
            {
                ParentsDisponibles.Add(u);
            }
        }

        private async Task EnregistrerUniteAsync()
        {
            if (UniteEnEdition == null) return;

            try
            {
                IsBusy = true;
                if (UniteEnEdition.ParentId == 0) UniteEnEdition.ParentId = null;
                if (string.IsNullOrWhiteSpace(UniteEnEdition.ResponsableMatricule)) UniteEnEdition.ResponsableMatricule = null;

                await _organigrammeService.EnregistrerUniteAsync(UniteEnEdition);
                EstEnEditionUnite = false;
                await ChargerToutAsync();
                StatusMessage = "Unité organisationnelle enregistrée.";
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Validation unité", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            finally
            {
                IsBusy = false;
            }
        }

        private async Task SupprimerUniteAsync()
        {
            if (UniteSelectionnee == null) return;

            var res = MessageBox.Show(
                $"Êtes-vous sûr de vouloir supprimer l'unité '{UniteSelectionnee.Libelle}' ?\nSes éventuelles sous-unités seront rattachées au niveau supérieur.",
                "Confirmation de suppression",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (res != MessageBoxResult.Yes) return;

            try
            {
                IsBusy = true;
                await _organigrammeService.SupprimerUniteAsync(UniteSelectionnee.Id);
                EstEnEditionUnite = false;
                await ChargerToutAsync();
                StatusMessage = "Unité supprimée avec succès.";
            }
            catch (Exception ex)
            {
                MessageBox.Show("Erreur lors de la suppression : " + ex.Message, "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                IsBusy = false;
            }
        }

        #endregion

        #region Logique Postes

        private void FiltrerPostes()
        {
            Postes.Clear();
            var q = _tousPostes.AsEnumerable();

            if (_filtreStatutPoste == "Pourvus") q = q.Where(p => p.EstPourvu);
            else if (_filtreStatutPoste == "Vacants") q = q.Where(p => !p.EstPourvu);

            if (!string.IsNullOrWhiteSpace(_rechercheTexte))
            {
                q = q.Where(p =>
                    (!string.IsNullOrEmpty(p.Code) && p.Code.IndexOf(_rechercheTexte, StringComparison.OrdinalIgnoreCase) >= 0) ||
                    (!string.IsNullOrEmpty(p.Intitule) && p.Intitule.IndexOf(_rechercheTexte, StringComparison.OrdinalIgnoreCase) >= 0) ||
                    (!string.IsNullOrEmpty(p.TitulaireNom) && p.TitulaireNom.IndexOf(_rechercheTexte, StringComparison.OrdinalIgnoreCase) >= 0) ||
                    (!string.IsNullOrEmpty(p.UniteLibelle) && p.UniteLibelle.IndexOf(_rechercheTexte, StringComparison.OrdinalIgnoreCase) >= 0));
            }

            foreach (var p in q) Postes.Add(p);
        }

        private void NouveauPoste()
        {
            PosteEnEdition = new Poste
            {
                Id = 0,
                Code = "PST-" + (_tousPostes.Count + 1).ToString("D2"),
                Intitule = "Nouveau poste",
                UniteId = _toutesUnites.FirstOrDefault()?.Id,
                EffectifRequis = 1,
                Actif = true,
                Ordre = _tousPostes.Count + 1
            };
            CompetencesPosteEnEdition.Clear();
            TitulairesPosteEnEdition.Clear();
            _ = ChargerCompetencesCatalogueAsync();
            MettreAJourSuperieursDisponibles(0);
            EstEnEditionPoste = true;
        }

        private void EditerPoste()
        {
            if (PosteSelectionne == null) return;

            PosteEnEdition = new Poste
            {
                Id = PosteSelectionne.Id,
                Code = PosteSelectionne.Code,
                Intitule = PosteSelectionne.Intitule,
                UniteId = PosteSelectionne.UniteId,
                SuperieurPosteId = PosteSelectionne.SuperieurPosteId,
                TitulaireMatricule = PosteSelectionne.TitulaireMatricule,
                Classification = PosteSelectionne.Classification,
                Missions = PosteSelectionne.Missions,
                EffectifRequis = PosteSelectionne.EffectifRequis,
                Actif = PosteSelectionne.Actif,
                Ordre = PosteSelectionne.Ordre,
                // RH-02 : Attributs étendus de la fiche de poste
                FinalitePrincipale = PosteSelectionne.FinalitePrincipale,
                ActivitesCles = PosteSelectionne.ActivitesCles,
                NiveauEtudeRequis = PosteSelectionne.NiveauEtudeRequis,
                DiplomeRequis = PosteSelectionne.DiplomeRequis,
                ExperienceMinimaleAnnees = PosteSelectionne.ExperienceMinimaleAnnees,
                ConditionsTravail = PosteSelectionne.ConditionsTravail,
                ConventionId = PosteSelectionne.ConventionId,
                ConventionLibelle = PosteSelectionne.ConventionLibelle,
                CategorieConventionnelle = PosteSelectionne.CategorieConventionnelle,
                EchelonConventionnel = PosteSelectionne.EchelonConventionnel,
                SalaireMinimumConventionnel = PosteSelectionne.SalaireMinimumConventionnel,
                SalaireMinBudgete = PosteSelectionne.SalaireMinBudgete,
                SalaireMaxBudgete = PosteSelectionne.SalaireMaxBudgete
            };

            MettreAJourSuperieursDisponibles(PosteSelectionne.Id);
            EstEnEditionPoste = true;
            _ = ChargerDetailsPosteEnEditionAsync(PosteSelectionne.Id);
        }

        private async Task ChargerCompetencesCatalogueAsync()
        {
            try
            {
                var compList = await _fichePosteService.ChargerToutesCompetencesAsync();
                CompetencesDisponibles.Clear();
                foreach (var c in compList) CompetencesDisponibles.Add(c);
                if (CompetenceSelectionneePourAjout == null && CompetencesDisponibles.Count > 0)
                {
                    CompetenceSelectionneePourAjout = CompetencesDisponibles[0];
                }
            }
            catch { }
        }

        private async Task ChargerDetailsPosteEnEditionAsync(int posteId)
        {
            try
            {
                await ChargerCompetencesCatalogueAsync();

                var compPoste = await _organigrammeService.ChargerCompetencesPosteAsync(posteId);
                CompetencesPosteEnEdition.Clear();
                foreach (var cp in compPoste) CompetencesPosteEnEdition.Add(cp);

                var tituPoste = await _organigrammeService.ChargerTitulairesPosteAsync(posteId);
                TitulairesPosteEnEdition.Clear();
                foreach (var tp in tituPoste) TitulairesPosteEnEdition.Add(tp);
            }
            catch { }
        }

        private async Task AjouterCompetencePosteAsync()
        {
            if (PosteEnEdition == null || PosteEnEdition.Id <= 0 || CompetenceSelectionneePourAjout == null) return;

            try
            {
                var pc = new PosteCompetence
                {
                    PosteId = PosteEnEdition.Id,
                    CompetenceId = CompetenceSelectionneePourAjout.Id,
                    NiveauRequis = (NiveauMaitrise)Math.Max(1, Math.Min(4, NiveauMaitrisePourAjout)),
                    EstObligatoire = EstObligatoirePourAjout
                };

                await _organigrammeService.EnregistrerCompetencePosteAsync(pc);
                await ChargerDetailsPosteEnEditionAsync(PosteEnEdition.Id);
                StatusMessage = $"Compétence '{CompetenceSelectionneePourAjout.Libelle}' ajoutée au poste.";
            }
            catch (Exception ex)
            {
                MessageBox.Show("Erreur lors de l'ajout de la compétence : " + ex.Message, "Erreur", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private async Task SupprimerCompetencePosteAsync()
        {
            if (CompetencePosteSelectionnee == null || PosteEnEdition == null) return;

            try
            {
                await _organigrammeService.SupprimerCompetencePosteAsync(CompetencePosteSelectionnee.Id);
                await ChargerDetailsPosteEnEditionAsync(PosteEnEdition.Id);
                StatusMessage = "Compétence retirée du poste.";
            }
            catch (Exception ex)
            {
                MessageBox.Show("Erreur lors du retrait de la compétence : " + ex.Message, "Erreur", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private async Task ActualiserSalaireConventionnelAsync()
        {
            if (PosteEnEdition == null || !PosteEnEdition.ConventionId.HasValue || string.IsNullOrWhiteSpace(PosteEnEdition.CategorieConventionnelle))
            {
                return;
            }

            try
            {
                var salMin = await _fichePosteService.ObtenirSalaireMinConventionnelAsync(
                    PosteEnEdition.ConventionId.Value,
                    PosteEnEdition.CategorieConventionnelle,
                    PosteEnEdition.EchelonConventionnel);

                if (salMin > 0m)
                {
                    PosteEnEdition.SalaireMinimumConventionnel = salMin;
                    StatusMessage = $"Salaire minimum conventionnel mis à jour : {salMin:N0} FCFA.";
                }
            }
            catch { }
        }

        private void ExporterFichePostePdf()
        {
            var poste = PosteEnEdition ?? PosteSelectionne;
            if (poste == null || poste.Id <= 0)
            {
                MessageBox.Show("Veuillez d'abord sélectionner ou enregistrer un poste pour exporter sa fiche.", "Export Fiche de poste", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            try
            {
                var sfd = new SaveFileDialog
                {
                    Title = "Exporter la fiche de poste officielle en PDF",
                    Filter = "Document PDF (*.pdf)|*.pdf",
                    FileName = string.Format("Fiche_Poste_{0}_{1:yyyyMMdd}.pdf",
                        string.IsNullOrWhiteSpace(poste.Code) ? "PST" : poste.Code.Trim(),
                        DateTime.Now)
                };

                if (sfd.ShowDialog() != true) return;

                IsBusy = true;
                StatusMessage = "Génération de la fiche de poste PDF...";

                // Charger compétences et titulaires à jour
                var comp = _organigrammeService.ChargerCompetencesPosteAsync(poste.Id).GetAwaiter().GetResult();
                var titu = _organigrammeService.ChargerTitulairesPosteAsync(poste.Id).GetAwaiter().GetResult();
                poste.CompetencesRequises = new ObservableCollection<PosteCompetence>(comp ?? new List<PosteCompetence>());
                poste.TitulairesActuels = new ObservableCollection<TitulairePosteLigne>(titu ?? new List<TitulairePosteLigne>());

                _fichePosteExportService.GenererFichePostePdf(poste, _dossier, sfd.FileName);

                StatusMessage = "Fiche de poste exportée avec succès.";
                try { Process.Start(new ProcessStartInfo(sfd.FileName) { UseShellExecute = true }); } catch { }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Erreur lors de l'export PDF : " + ex.Message, "Export Fiche de poste", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                IsBusy = false;
            }
        }

        private void MettreAJourSuperieursDisponibles(int idExclu)
        {
            SuperieursDisponibles.Clear();
            SuperieursDisponibles.Add(new Poste { Id = 0, Code = "", Intitule = "— Direction Générale (Pas de N+1) —" });
            foreach (var p in _tousPostes.Where(p => p.Id != idExclu))
            {
                SuperieursDisponibles.Add(p);
            }
        }

        private async Task EnregistrerPosteAsync()
        {
            if (PosteEnEdition == null) return;

            try
            {
                IsBusy = true;
                if (PosteEnEdition.UniteId == 0) PosteEnEdition.UniteId = null;
                if (PosteEnEdition.SuperieurPosteId == 0) PosteEnEdition.SuperieurPosteId = null;
                if (string.IsNullOrWhiteSpace(PosteEnEdition.TitulaireMatricule)) PosteEnEdition.TitulaireMatricule = null;

                await _organigrammeService.EnregistrerPosteAsync(PosteEnEdition);
                EstEnEditionPoste = false;
                await ChargerToutAsync();
                StatusMessage = "Poste de travail enregistré.";
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Validation poste", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            finally
            {
                IsBusy = false;
            }
        }

        private async Task SupprimerPosteAsync()
        {
            if (PosteSelectionne == null) return;

            var res = MessageBox.Show(
                $"Êtes-vous sûr de vouloir supprimer le poste '{PosteSelectionne.Intitule}' ?",
                "Suppression poste",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (res != MessageBoxResult.Yes) return;

            try
            {
                IsBusy = true;
                await _organigrammeService.SupprimerPosteAsync(PosteSelectionne.Id);
                EstEnEditionPoste = false;
                await ChargerToutAsync();
                StatusMessage = "Poste supprimé.";
            }
            catch (Exception ex)
            {
                MessageBox.Show("Erreur lors de la suppression : " + ex.Message, "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                IsBusy = false;
            }
        }

        #endregion

        #region Logique Affectations

        private void FiltrerAffectations()
        {
            Affectations.Clear();
            var q = string.IsNullOrWhiteSpace(_rechercheTexte) ? _toutesAffectations :
                _toutesAffectations.Where(a =>
                    (!string.IsNullOrEmpty(a.Matricule) && a.Matricule.IndexOf(_rechercheTexte, StringComparison.OrdinalIgnoreCase) >= 0) ||
                    (!string.IsNullOrEmpty(a.NomComplet) && a.NomComplet.IndexOf(_rechercheTexte, StringComparison.OrdinalIgnoreCase) >= 0) ||
                    (!string.IsNullOrEmpty(a.ServiceActuel) && a.ServiceActuel.IndexOf(_rechercheTexte, StringComparison.OrdinalIgnoreCase) >= 0) ||
                    (!string.IsNullOrEmpty(a.PosteActuelLibelle) && a.PosteActuelLibelle.IndexOf(_rechercheTexte, StringComparison.OrdinalIgnoreCase) >= 0));

            foreach (var a in q) Affectations.Add(a);
        }

        private void ChargerDetailAffectation()
        {
            if (AffectationSelectionnee == null) return;

            ServiceAffectationChoisi = AffectationSelectionnee.ServiceActuel;
            PosteAffectationChoisi = _tousPostes.FirstOrDefault(p => p.Id == AffectationSelectionnee.PosteActuelId);
            ManagerAffectationChoisi = ResponsablesDisponibles.FirstOrDefault(r => r.EmployeeId == AffectationSelectionnee.ManagerActuelMatricule);
        }

        private async Task EnregistrerAffectationAsync()
        {
            if (AffectationSelectionnee == null) return;

            try
            {
                IsBusy = true;
                await _organigrammeService.AffecterSalarieAsync(
                    AffectationSelectionnee.Matricule,
                    ServiceAffectationChoisi,
                    PosteAffectationChoisi?.Id,
                    ManagerAffectationChoisi?.EmployeeId);

                await ChargerToutAsync();
                StatusMessage = $"Affectation mise à jour pour {AffectationSelectionnee.NomComplet}.";
            }
            catch (Exception ex)
            {
                MessageBox.Show("Erreur lors de l'enregistrement de l'affectation : " + ex.Message, "Affectation", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            finally
            {
                IsBusy = false;
            }
        }

        #endregion

        #region Recherche & Dépliage

        private void AppliquerFiltreRecherche()
        {
            FiltrerUnites();
            FiltrerPostes();
            FiltrerAffectations();

            // Mettre en surbrillance dans l'arbre graphique
            if (!string.IsNullOrWhiteSpace(_rechercheTexte))
            {
                SurbrillanceArbre(ArbreRacines, _rechercheTexte);
            }
            else
            {
                EffacerSurbrillance(ArbreRacines);
            }
        }

        private bool SurbrillanceArbre(IEnumerable<NoeudOrganigramme> noeuds, string filtre)
        {
            bool aUnMatch = false;
            foreach (var n in noeuds)
            {
                bool matchDirect =
                    (!string.IsNullOrEmpty(n.Titre) && n.Titre.IndexOf(filtre, StringComparison.OrdinalIgnoreCase) >= 0) ||
                    (!string.IsNullOrEmpty(n.Code) && n.Code.IndexOf(filtre, StringComparison.OrdinalIgnoreCase) >= 0) ||
                    (!string.IsNullOrEmpty(n.ResponsableOuTitulaire) && n.ResponsableOuTitulaire.IndexOf(filtre, StringComparison.OrdinalIgnoreCase) >= 0);

                bool matchEnfant = SurbrillanceArbre(n.Enfants, filtre);

                n.IsHighlighted = matchDirect;
                if (matchDirect || matchEnfant)
                {
                    n.IsExpanded = true;
                    aUnMatch = true;
                }
            }
            return aUnMatch;
        }

        private void EffacerSurbrillance(IEnumerable<NoeudOrganigramme> noeuds)
        {
            foreach (var n in noeuds)
            {
                n.IsHighlighted = false;
                EffacerSurbrillance(n.Enfants);
            }
        }

        private void BasculerDepliage(bool deployer)
        {
            void Parcourir(IEnumerable<NoeudOrganigramme> noeuds)
            {
                foreach (var n in noeuds)
                {
                    n.IsExpanded = deployer;
                    Parcourir(n.Enfants);
                }
            }
            Parcourir(ArbreRacines);
        }

        #endregion

        #region Exports

        private void ExporterExcel()
        {
            try
            {
                var sfd = new SaveFileDialog
                {
                    Title = "Exporter l'organigramme vers Excel",
                    Filter = "Classeur Excel (*.xlsx)|*.xlsx",
                    FileName = string.Format("Organigramme_{0}_{1:yyyyMMdd}.xlsx",
                        string.IsNullOrWhiteSpace(_dossier.Sigle) ? "Entreprise" : _dossier.Sigle.Trim(),
                        DateTime.Now)
                };

                if (sfd.ShowDialog() != true) return;

                IsBusy = true;
                StatusMessage = "Génération du classeur Excel...";

                _exportService.ExporterExcel(
                    _toutesUnites,
                    _tousPostes,
                    _toutesAffectations,
                    _dossier.RaisonSociale,
                    _dossier.IFU,
                    sfd.FileName);

                StatusMessage = "Export Excel terminé avec succès.";
                try { Process.Start(new ProcessStartInfo(sfd.FileName) { UseShellExecute = true }); } catch { }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Erreur lors de l'export Excel : " + ex.Message, "Export Excel", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                IsBusy = false;
            }
        }

        private void ExporterPdf()
        {
            try
            {
                var sfd = new SaveFileDialog
                {
                    Title = "Exporter l'organigramme en PDF",
                    Filter = "Document PDF (*.pdf)|*.pdf",
                    FileName = string.Format("Organigramme_{0}_{1:yyyyMMdd}.pdf",
                        string.IsNullOrWhiteSpace(_dossier.Sigle) ? "Entreprise" : _dossier.Sigle.Trim(),
                        DateTime.Now)
                };

                if (sfd.ShowDialog() != true) return;

                IsBusy = true;
                StatusMessage = "Génération du document PDF...";

                _exportService.ExporterPdf(
                    _toutesUnites,
                    _tousPostes,
                    _dossier.RaisonSociale,
                    _dossier.IFU,
                    _dossier.RCCM,
                    sfd.FileName);

                StatusMessage = "Export PDF terminé avec succès.";
                try { Process.Start(new ProcessStartInfo(sfd.FileName) { UseShellExecute = true }); } catch { }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Erreur lors de l'export PDF : " + ex.Message, "Export PDF", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                IsBusy = false;
            }
        }

        #endregion
    }
}
