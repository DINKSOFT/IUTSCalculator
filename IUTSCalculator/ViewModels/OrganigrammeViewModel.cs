using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
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
        private ConventionCollectiveService _conventionService;

        public OrganigrammeViewModel(
            OrganigrammeService organigrammeService,
            OrganigrammeExportService exportService,
            DataService dataService,
            Dossier dossier,
            FichePosteService fichePosteService = null,
            FichePosteExportService fichePosteExportService = null,
            ConventionCollectiveService conventionService = null)
        {
            _organigrammeService = organigrammeService ?? throw new ArgumentNullException(nameof(organigrammeService));
            _exportService = exportService ?? throw new ArgumentNullException(nameof(exportService));
            _dataService = dataService ?? throw new ArgumentNullException(nameof(dataService));
            _dossier = dossier ?? throw new ArgumentNullException(nameof(dossier));
            _fichePosteService = fichePosteService ?? new FichePosteService(new SQLiteService(_dossier.DbFileName));
            _fichePosteExportService = fichePosteExportService ?? new FichePosteExportService();
            _conventionService = conventionService;

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
            ActualiserSalaireConventionnelCommand = new RelayCommand(async () => await ActualiserSalaireConventionnelAsync(),
                () => PosteEnEdition != null && PosteEnEdition.ConventionId.HasValue && PosteEnEdition.ConventionId.Value > 0 && !IsBusy);

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
            set
            {
                if (_posteEnEdition != null) _posteEnEdition.PropertyChanged -= PosteEnEdition_PropertyChanged;
                _posteEnEdition = value;
                if (_posteEnEdition != null) _posteEnEdition.PropertyChanged += PosteEnEdition_PropertyChanged;
                OnPropertyChanged();
                InfoSalaireConventionnel = null;
                InfoCompetences = null;
                // Grille de la convention du poste ouvert, sans rien proposer : les valeurs
                // enregistrées sont affichées telles quelles.
                _ = ChargerGrilleConventionAsync(false);
            }
        }

        private bool _estEnEditionPoste;
        public bool EstEnEditionPoste
        {
            get => _estEnEditionPoste;
            set { _estEnEditionPoste = value; OnPropertyChanged(); }
        }

        /// <summary>
        /// Filtre du répertoire des postes : « Tous », « Pourvus » (au moins un titulaire
        /// actif) ou « Vacants » (aucun). La liste déroulante est liée par SelectedValue sur
        /// le contenu des éléments : liée par SelectedItem, elle renvoyait un ComboBoxItem
        /// (« System.Windows.Controls.ComboBoxItem: Pourvus ») et le filtre restait sans effet.
        /// </summary>
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

        /// <summary>
        /// Tous les postes, pour le panneau d'affectation : il ne doit pas dépendre du filtre
        /// ni de la recherche du répertoire, sinon le poste actuel d'un salarié pourrait
        /// manquer à la liste et l'affectation être enregistrée sans poste.
        /// </summary>
        public ObservableCollection<Poste> PostesAffectationDisponibles { get; } = new ObservableCollection<Poste>();
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

        /// <summary>
        /// Niveaux proposés à l'ajout d'une compétence : la clé EST le niveau enregistré
        /// (1 à 4). Le sélecteur était lié par SelectedIndex (0 à 3) puis converti par
        /// Math.Max(1, …) : Niv 1 et Niv 2 étaient enregistrés 1, Niv 3 → 2, Niv 4 → 3, et le
        /// niveau 4 restait inatteignable.
        /// </summary>
        public IEnumerable<KeyValuePair<int, string>> NiveauxMaitriseDisponibles { get; } = new List<KeyValuePair<int, string>>
        {
            new KeyValuePair<int, string>((int)NiveauMaitrise.Notions, "Niv 1 (★)"),
            new KeyValuePair<int, string>((int)NiveauMaitrise.Pratique, "Niv 2 (★★)"),
            new KeyValuePair<int, string>((int)NiveauMaitrise.Maitrise, "Niv 3 (★★★)"),
            new KeyValuePair<int, string>((int)NiveauMaitrise.Expert, "Niv 4 (★★★★)")
        };

        /// <summary>Niveau requis (1 à 4) choisi pour l'ajout ; « Niv 3 » par défaut, comme l'écran l'affichait.</summary>
        private int _niveauMaitrisePourAjout = (int)NiveauMaitrise.Maitrise;
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
            set
            {
                if (SetProperty(ref _competencePosteSelectionnee, value) && value != null)
                {
                    // La ligne choisie est reportée dans la barre d'ajout avec SON niveau :
                    // « Ajouter » la met alors à jour (et confirme un niveau à vérifier).
                    var comp = CompetencesDisponibles.FirstOrDefault(c => c.Id == value.CompetenceId);
                    if (comp != null) CompetenceSelectionneePourAjout = comp;
                    NiveauMaitrisePourAjout = (int)value.NiveauRequis;
                    EstObligatoirePourAjout = value.EstObligatoire;
                }
            }
        }

        /// <summary>Vrai si une compétence du poste ouvert porte un niveau à vérifier (enregistré avant la v38).</summary>
        public bool AUnNiveauAVerifier => CompetencesPosteEnEdition.Any(c => c.NiveauAVerifier);

        // RH-02 : Classification conventionnelle (bloc 4 de la fiche de poste)
        public ObservableCollection<ConventionCollective> ConventionsDisponibles { get; } = new ObservableCollection<ConventionCollective>();
        public ObservableCollection<GrilleSalaire> GrilleConventionEnEdition { get; } = new ObservableCollection<GrilleSalaire>();

        private GrilleSalaire _ligneGrilleSelectionnee;
        /// <summary>
        /// Ligne de la grille choisie dans la liste : reporte catégorie, échelon et minimum
        /// sur la fiche (le minimum reste modifiable).
        /// </summary>
        public GrilleSalaire LigneGrilleSelectionnee
        {
            get => _ligneGrilleSelectionnee;
            set
            {
                if (SetProperty(ref _ligneGrilleSelectionnee, value) && value != null && PosteEnEdition != null && _synchroListes == 0)
                {
                    _synchroListes++;
                    try
                    {
                        PosteEnEdition.CategorieConventionnelle = value.Categorie;
                        PosteEnEdition.EchelonConventionnel = value.Echelon;
                        PosteEnEdition.SalaireMinimumConventionnel = value.SalaireMinimum;
                    }
                    finally
                    {
                        _synchroListes--;
                    }
                    _sequenceMinimum++; // une proposition encore en cours ne doit pas écraser ce choix
                    InfoSalaireConventionnel = MessageMinimumPropose(value.SalaireMinimum);
                }
            }
        }

        private string _infoSalaireConventionnel;
        /// <summary>Information affichée sous le minimum conventionnel (proposé, introuvable…).</summary>
        public string InfoSalaireConventionnel
        {
            get => _infoSalaireConventionnel;
            set => SetProperty(ref _infoSalaireConventionnel, value);
        }

        private string _infoCompetences;
        /// <summary>Retour de la dernière opération sur les compétences du poste (affiché sous la barre d'ajout).</summary>
        public string InfoCompetences
        {
            get => _infoCompetences;
            set => SetProperty(ref _infoCompetences, value);
        }

        // Pendant la reconstruction d'une liste liée, les listes déroulantes renvoient une
        // sélection vide : elle ne doit être prise ni pour une saisie, ni pour un changement.
        // Compteur (et non booléen) : plusieurs reconstructions peuvent se chevaucher.
        private int _synchroListes;
        private int _sequenceMinimum;

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
            // Reconstruire une liste liée vide un instant la sélection des listes déroulantes
            // de la fiche de poste ouverte, qui la recopieraient dans le poste (unité, N+1,
            // convention). Ces valeurs sont donc mises de côté puis rétablies.
            var posteOuvert = PosteEnEdition;
            int? uniteOuverte = posteOuvert?.UniteId;
            int? superieurOuvert = posteOuvert?.SuperieurPosteId;
            int? conventionOuverte = posteOuvert?.ConventionId;

            try
            {
                IsBusy = true;
                _synchroListes++;
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
                PostesAffectationDisponibles.Clear();
                foreach (var p in _tousPostes) PostesAffectationDisponibles.Add(p);

                TotalPostes = _tousPostes.Count;
                TotalPostesPourvus = _tousPostes.Count(p => p.EstPourvu);
                TotalPostesVacants = _tousPostes.Count(p => !p.EstPourvu);

                // Conventions collectives du dossier (bloc 4 de la fiche de poste)
                await ChargerConventionsAsync();

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
                if (posteOuvert != null && ReferenceEquals(posteOuvert, PosteEnEdition))
                {
                    posteOuvert.UniteId = uniteOuverte;
                    posteOuvert.SuperieurPosteId = superieurOuvert;
                    posteOuvert.ConventionId = conventionOuverte;
                }
                _synchroListes--;
                IsBusy = false;
            }
        }

        private ConventionCollectiveService ServiceConventions
        {
            get
            {
                // Lecture seule (liste des conventions et grilles SOC-05).
                if (_conventionService == null)
                    _conventionService = new ConventionCollectiveService(new SQLiteService(_dossier.DbFileName));
                return _conventionService;
            }
        }

        private async Task ChargerConventionsAsync()
        {
            List<ConventionCollective> liste;
            try
            {
                liste = await ServiceConventions.ObtenirToutesConventionsAsync();
            }
            catch
            {
                liste = new List<ConventionCollective>();
            }

            ConventionsDisponibles.Clear();
            ConventionsDisponibles.Add(new ConventionCollective { Id = 0, Code = "", Libelle = "— Aucune convention —" });
            foreach (var c in liste) ConventionsDisponibles.Add(c);
        }

        // ── Classification conventionnelle de la fiche de poste (RH-02) ──

        /// <summary>
        /// Convention, catégorie ou échelon modifiés sur la fiche ouverte : le minimum de la
        /// grille est proposé (il reste modifiable). Rien n'est proposé à l'ouverture d'un
        /// poste : les valeurs enregistrées sont affichées telles quelles.
        /// </summary>
        private void PosteEnEdition_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (_synchroListes > 0 || !ReferenceEquals(sender, _posteEnEdition)) return;

            switch (e.PropertyName)
            {
                case nameof(Poste.ConventionId):
                    var conv = _posteEnEdition.ConventionId.HasValue && _posteEnEdition.ConventionId.Value > 0
                        ? ConventionsDisponibles.FirstOrDefault(c => c.Id == _posteEnEdition.ConventionId.Value)
                        : null;
                    _posteEnEdition.ConventionLibelle = conv?.Libelle;
                    _ = ChargerGrilleConventionAsync(true);
                    break;

                case nameof(Poste.CategorieConventionnelle):
                case nameof(Poste.EchelonConventionnel):
                    SelectionnerLigneGrille();
                    _ = ProposerSalaireMinimumAsync(false);
                    break;
            }
        }

        /// <summary>Charge la grille de la convention du poste ouvert (liste « Ligne de la grille »).</summary>
        private async Task ChargerGrilleConventionAsync(bool proposer)
        {
            var poste = PosteEnEdition;
            var conventionId = poste?.ConventionId;
            var lignes = new List<GrilleSalaire>();

            if (conventionId.HasValue && conventionId.Value > 0)
            {
                try
                {
                    var conv = await ServiceConventions.ObtenirParIdAsync(conventionId.Value);
                    if (conv != null && conv.Grilles != null) lignes = conv.Grilles;
                }
                catch (Exception ex)
                {
                    InfoSalaireConventionnel = "Grille salariale illisible : " + ex.Message;
                }
            }

            // Un autre poste (ou une autre convention) a pu être choisi pendant la lecture.
            if (!ReferenceEquals(poste, PosteEnEdition) || (poste != null && poste.ConventionId != conventionId)) return;

            _synchroListes++;
            try
            {
                GrilleConventionEnEdition.Clear();
                foreach (var g in lignes) GrilleConventionEnEdition.Add(g);
            }
            finally
            {
                _synchroListes--;
            }
            SelectionnerLigneGrille();

            if (proposer) await ProposerSalaireMinimumAsync(false);
        }

        /// <summary>Met en regard, dans la liste de la grille, la ligne correspondant à la catégorie et à l'échelon saisis.</summary>
        private void SelectionnerLigneGrille()
        {
            var poste = PosteEnEdition;
            var ligne = poste == null ? null : GrilleConventionEnEdition.FirstOrDefault(g =>
                string.Equals((g.Categorie ?? "").Trim(), (poste.CategorieConventionnelle ?? "").Trim(), StringComparison.OrdinalIgnoreCase) &&
                string.Equals((g.Echelon ?? "").Trim(), (poste.EchelonConventionnel ?? "").Trim(), StringComparison.OrdinalIgnoreCase));

            _synchroListes++;
            try
            {
                LigneGrilleSelectionnee = ligne;
            }
            finally
            {
                _synchroListes--;
            }
        }

        /// <summary>
        /// Propose le minimum de la grille (SOC-05) pour la convention, la catégorie et
        /// l'échelon de la fiche ouverte. Seule la saisie la plus récente est appliquée.
        /// </summary>
        private async Task ProposerSalaireMinimumAsync(bool demandeExplicite)
        {
            var poste = PosteEnEdition;
            int sequence = ++_sequenceMinimum;
            if (poste == null) return;

            bool aConvention = poste.ConventionId.HasValue && poste.ConventionId.Value > 0;
            if (!aConvention || string.IsNullOrWhiteSpace(poste.CategorieConventionnelle) || string.IsNullOrWhiteSpace(poste.EchelonConventionnel))
            {
                InfoSalaireConventionnel = aConvention || demandeExplicite
                    ? "Choisissez la convention, puis une ligne de la grille (ou saisissez catégorie et échelon) : le minimum conventionnel est alors repris de la grille."
                    : null;
                return;
            }

            try
            {
                var salMin = await _fichePosteService.ObtenirSalaireMinConventionnelAsync(
                    poste.ConventionId.Value,
                    poste.CategorieConventionnelle,
                    poste.EchelonConventionnel);

                // Une saisie plus récente (ou un autre poste) a pris le relais.
                if (sequence != _sequenceMinimum || !ReferenceEquals(poste, PosteEnEdition)) return;

                if (salMin > 0m)
                {
                    poste.SalaireMinimumConventionnel = salMin;
                    InfoSalaireConventionnel = MessageMinimumPropose(salMin);
                    StatusMessage = $"Salaire minimum conventionnel mis à jour : {salMin:N0} FCFA.";
                }
                else
                {
                    InfoSalaireConventionnel = string.Format(
                        "Aucune ligne de la grille pour la catégorie « {0} », échelon « {1} » : saisissez le minimum à la main.",
                        poste.CategorieConventionnelle.Trim(), poste.EchelonConventionnel.Trim());
                }
            }
            catch (Exception ex)
            {
                if (sequence == _sequenceMinimum)
                    InfoSalaireConventionnel = "Grille salariale illisible : " + ex.Message;
            }
        }

        private static string MessageMinimumPropose(decimal minimum)
        {
            return $"Minimum de la grille : {minimum:N0} FCFA (repris automatiquement, modifiable).";
        }

        private async Task ChargerArbreAsync()
        {
            var racines = await _organigrammeService.ChargerArbreOrganigrammeAsync(_inclurePostesDansArbre);
            ArbreRacines.Clear();
            foreach (var r in racines) ArbreRacines.Add(r);
        }

        private async Task InitialiserStructureDefautAsync()
        {
            bool complement = _toutesUnites.Count > 0;
            if (complement)
            {
                // L'organigramme existant n'est jamais effacé : l'initialisation se borne à
                // ajouter les services des fiches salariés qui n'ont pas encore d'unité (ce
                // qui complète aussi une structure restée inachevée).
                var rep = MessageBox.Show(
                    "L'organigramme contient déjà des unités : il ne sera ni effacé ni modifié.\n\n" +
                    "Seuls les services saisis sur les fiches salariés qui n'ont pas encore d'unité seront ajoutés, rattachés à la Direction Générale.\n\nContinuer ?",
                    "Initialisation de l'organigramme",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);
                if (rep != MessageBoxResult.Yes) return;
            }

            try
            {
                IsBusy = true;
                StatusMessage = "Initialisation de la structure d'entreprise...";
                int creees = await _organigrammeService.InitialiserStructureDefautAsync();
                await ChargerToutAsync();
                if (!complement)
                    StatusMessage = $"Structure d'organigramme initialisée avec succès ({creees} unité(s) créée(s)).";
                else if (creees > 0)
                    StatusMessage = $"{creees} service(s) des fiches salariés ajouté(s) à l'organigramme.";
                else
                    StatusMessage = "Aucun service à ajouter : chaque service des fiches salariés a déjà son unité.";

                // Le message d'état de cet écran n'est pas affiché par la fenêtre principale :
                // le bilan est donc présenté dans une boîte de dialogue.
                MessageBox.Show(StatusMessage, "Initialisation de l'organigramme", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                // Rien n'a été enregistré (création en une seule transaction).
                StatusMessage = "Erreur d'initialisation (aucune unité créée) : " + ex.Message;
                MessageBox.Show("L'initialisation de l'organigramme a échoué ; aucune unité n'a été créée.\n\n" + ex.Message,
                    "Initialisation de l'organigramme", MessageBoxButton.OK, MessageBoxImage.Warning);
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
            // Une seule convention active dans le dossier : elle est proposée d'office.
            var conventionsActives = ConventionsDisponibles.Where(c => c.Id > 0 && c.EstActive).ToList();
            var conventionParDefaut = conventionsActives.Count == 1 ? conventionsActives[0] : null;

            PosteEnEdition = new Poste
            {
                Id = 0,
                Code = "PST-" + (_tousPostes.Count + 1).ToString("D2"),
                Intitule = "Nouveau poste",
                UniteId = _toutesUnites.FirstOrDefault()?.Id,
                EffectifRequis = 1,
                Actif = true,
                Ordre = _tousPostes.Count + 1,
                ConventionId = conventionParDefaut?.Id,
                ConventionLibelle = conventionParDefaut?.Libelle
            };
            CompetencesPosteEnEdition.Clear();
            OnPropertyChanged(nameof(AUnNiveauAVerifier));
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
                OnPropertyChanged(nameof(AUnNiveauAVerifier));

                var tituPoste = await _organigrammeService.ChargerTitulairesPosteAsync(posteId);
                TitulairesPosteEnEdition.Clear();
                foreach (var tp in tituPoste) TitulairesPosteEnEdition.Add(tp);
            }
            catch { }
        }

        private async Task AjouterCompetencePosteAsync()
        {
            if (PosteEnEdition == null || PosteEnEdition.Id <= 0 || CompetenceSelectionneePourAjout == null) return;

            int niveau = NiveauMaitrisePourAjout;
            if (niveau < (int)NiveauMaitrise.Notions || niveau > (int)NiveauMaitrise.Expert)
            {
                MessageBox.Show("Choisissez le niveau requis (Niv 1 à Niv 4).", "Compétences du poste", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            try
            {
                var competence = CompetenceSelectionneePourAjout;
                // Une compétence ne figure qu'une fois sur un poste : la rajouter met à jour
                // son niveau et sa criticité.
                var existante = CompetencesPosteEnEdition.FirstOrDefault(c => c.CompetenceId == competence.Id);
                var pc = new PosteCompetence
                {
                    Id = existante?.Id ?? 0,
                    PosteId = PosteEnEdition.Id,
                    CompetenceId = competence.Id,
                    NiveauRequis = (NiveauMaitrise)niveau,
                    EstObligatoire = EstObligatoirePourAjout,
                    Commentaire = existante?.Commentaire
                };

                await _organigrammeService.EnregistrerCompetencePosteAsync(pc);
                await ChargerDetailsPosteEnEditionAsync(PosteEnEdition.Id);

                string etoiles = new string('★', niveau);
                InfoCompetences = existante == null
                    ? $"Compétence « {competence.Libelle} » ajoutée au poste (Niv {niveau} {etoiles})."
                    : $"Compétence « {competence.Libelle} » mise à jour (Niv {niveau} {etoiles})" + (existante.NiveauAVerifier ? " : niveau confirmé." : ".");
                StatusMessage = InfoCompetences;
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
                InfoCompetences = StatusMessage;
            }
            catch (Exception ex)
            {
                MessageBox.Show("Erreur lors du retrait de la compétence : " + ex.Message, "Erreur", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        /// <summary>Bouton « Grille » : reprend le minimum de la grille après une saisie manuelle.</summary>
        private async Task ActualiserSalaireConventionnelAsync()
        {
            await ProposerSalaireMinimumAsync(true);
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
                _synchroListes++;
                try
                {
                    if (PosteEnEdition.UniteId == 0) PosteEnEdition.UniteId = null;
                    if (PosteEnEdition.SuperieurPosteId == 0) PosteEnEdition.SuperieurPosteId = null;
                    // « — Aucune convention — » : pas de convention (et pas d'identifiant 0 en base).
                    if (PosteEnEdition.ConventionId.HasValue && PosteEnEdition.ConventionId.Value <= 0)
                    {
                        PosteEnEdition.ConventionId = null;
                        PosteEnEdition.ConventionLibelle = null;
                    }
                    if (string.IsNullOrWhiteSpace(PosteEnEdition.TitulaireMatricule)) PosteEnEdition.TitulaireMatricule = null;
                }
                finally
                {
                    _synchroListes--;
                }

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
