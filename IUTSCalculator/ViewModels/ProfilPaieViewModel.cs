using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Data;
using System.Windows.Input;
using IUTSCalculator.Helpers;
using IUTSCalculator.Models;
using IUTSCalculator.Services;

namespace IUTSCalculator.ViewModels
{
    /// <summary>Une population proposée dans le sélecteur, avec son libellé.</summary>
    public class PopulationProfilOption
    {
        public PopulationProfilOption(PopulationProfil valeur)
        {
            Valeur = valeur;
        }

        public PopulationProfil Valeur { get; private set; }

        public string Libelle
        {
            get { return ProfilPaie.Libelle_(Valeur); }
        }

        /// <summary>
        /// Liste construite UNE fois. Un getter qui fabriquerait de nouvelles instances à
        /// chaque appel renverrait une option n'appartenant pas à la liste affichée, et le
        /// sélecteur ne montrerait aucune sélection.
        /// </summary>
        private static readonly List<PopulationProfilOption> _tous =
            Enum.GetValues(typeof(PopulationProfil))
                .Cast<PopulationProfil>()
                .Select(p => new PopulationProfilOption(p))
                .ToList();

        public static List<PopulationProfilOption> Tous
        {
            get { return _tous; }
        }
    }

    /// <summary>
    /// Écran « Profils de paie » (module SOC-03).
    ///
    /// À gauche, les profils du dossier et ce qu'ils coûtent à modifier : pour chacun, le
    /// nombre de rubriques qu'il porte et le nombre de salariés qui le portent. À droite,
    /// les rubriques du profil sélectionné, puis les rattachements — qui l'a, depuis quand.
    ///
    /// Le profil est un lien vivant : corriger une ligne corrige tous les salariés
    /// rattachés. L'écran le dit avant d'enregistrer, parce qu'une retouche déplace des
    /// nets à payer et qu'on ne devrait jamais l'apprendre après coup.
    /// </summary>
    public class ProfilPaieViewModel : BaseViewModel
    {
        private readonly ProfilPaieService _profilService;
        private readonly RubriqueService _rubriqueService;
        private readonly DataService _dataService;

        private List<Rubrique> _plan = new List<Rubrique>();
        private List<AffectationProfil> _toutesAffectations = new List<AffectationProfil>();

        public ObservableCollection<ProfilPaie> Profils { get; } = new ObservableCollection<ProfilPaie>();
        public ObservableCollection<LigneProfil> Lignes { get; } = new ObservableCollection<LigneProfil>();
        public ObservableCollection<Rubrique> RubriquesDisponibles { get; } = new ObservableCollection<Rubrique>();
        public ObservableCollection<AffectationProfil> Affectations { get; } = new ObservableCollection<AffectationProfil>();
        public ObservableCollection<Employee> Salaries { get; } = new ObservableCollection<Employee>();
        public ObservableCollection<string> Avertissements { get; } = new ObservableCollection<string>();

        /// <summary>Vue filtrée de Profils (recherche texte + filtre population) pour la grille.</summary>
        public ICollectionView ProfilsView { get; }

        public ProfilPaieViewModel(
            ProfilPaieService profilService,
            RubriqueService rubriqueService,
            DataService dataService)
        {
            if (profilService == null) throw new ArgumentNullException("profilService");
            if (rubriqueService == null) throw new ArgumentNullException("rubriqueService");
            if (dataService == null) throw new ArgumentNullException("dataService");

            _profilService = profilService;
            _rubriqueService = rubriqueService;
            _dataService = dataService;

            Title = "Profils de paie";

            _dateEffet = DateTime.Today;

            ProfilsView = CollectionViewSource.GetDefaultView(Profils);
            ProfilsView.Filter = FiltrerProfil;

            ChargerCommand = new RelayCommand(async () => await ChargerAsync(), () => !IsBusy);
            NouveauCommand = new RelayCommand(Nouveau, () => !IsBusy);
            EnregistrerCommand = new RelayCommand(
                async () => await EnregistrerAsync(),
                () => !IsBusy && ProfilEnCours != null);
            AnnulerSaisieCommand = new RelayCommand(AnnulerSaisie, () => ProfilEnCours != null);
            SupprimerCommand = new RelayCommand(
                async () => await SupprimerAsync(),
                () => !IsBusy && ProfilSelectionne != null);

            AjouterRubriqueCommand = new RelayCommand(
                AjouterRubrique,
                () => ProfilSelectionne != null && RubriqueAAjouter != null);
            RetirerRubriqueCommand = new RelayCommand(
                RetirerRubrique,
                () => LigneSelectionnee != null);
            EnregistrerRubriquesCommand = new RelayCommand(
                async () => await EnregistrerRubriquesAsync(),
                () => !IsBusy && ProfilSelectionne != null);

            AffecterCommand = new RelayCommand(
                async () => await AffecterAsync(),
                () => !IsBusy && ProfilSelectionne != null && SalarieAAffecter != null);
            RetirerProfilCommand = new RelayCommand(
                async () => await RetirerProfilAsync(),
                () => !IsBusy && SalarieAAffecter != null);
            SupprimerAffectationCommand = new RelayCommand(
                async () => await SupprimerAffectationAsync(),
                () => !IsBusy && AffectationSelectionnee != null);

            ClearSearchCommand = new RelayCommand(() => SearchText = string.Empty);
            ResetFiltersCommand = new RelayCommand(() =>
            {
                SearchText = string.Empty;
                SelectedPopulationFilter = null;
            });
            ClearFiltreSalarieCommand = new RelayCommand(() => FiltreSalarie = string.Empty);

            _ = ChargerAsync();
        }

        /// <summary>
        /// Ce ViewModel n'est créé qu'une seule fois (voir MainViewModel) et ne recharge
        /// jamais automatiquement après ce premier chargement : sans ce rafraîchissement,
        /// revenir sur cet écran après avoir modifié une fiche employé ailleurs continuait
        /// d'afficher les anciennes données tant que le dossier n'était pas fermé puis
        /// rouvert (signalé le 16 septembre 2026, voir ETAT-DEVELOPPEMENT.md item 21). Sans
        /// risque pour une saisie en cours : ChargerAsync ne touche pas ProfilEnCours.
        /// </summary>
        protected override void OnIsActiveChanged()
        {
            if (IsActive)
                _ = ChargerAsync();
        }

        // ── Sélection et édition du profil ───────────────────────────────────

        public IEnumerable<PopulationProfilOption> Populations
        {
            get { return PopulationProfilOption.Tous; }
        }

        private ProfilPaie _profilSelectionne;
        public ProfilPaie ProfilSelectionne
        {
            get { return _profilSelectionne; }
            set
            {
                // ChargerAsync() réaffecte cette propriété après chaque rechargement (y
                // compris le rechargement automatique à la réactivation de l'écran, voir
                // OnIsActiveChanged) avec une NOUVELLE instance représentant le MÊME profil,
                // pour rafraîchir l'affichage. Cloner ProfilEnCours à chaque fois écraserait
                // alors une saisie en cours dès qu'on revient sur cet écran — signalé le 16
                // septembre 2026 (voir ETAT-DEVELOPPEMENT.md item 22). On ne clone donc que
                // lorsque le profil sélectionné a réellement changé.
                bool memeProfil = _profilSelectionne != null && value != null && _profilSelectionne.Id == value.Id;

                if (SetProperty(ref _profilSelectionne, value))
                {
                    // La ligne s'ouvre à l'édition sur une COPIE : tant que
                    // l'enregistrement n'est pas demandé, la liste affichée reste celle de
                    // la base, et une modification abandonnée ne laisse aucune trace.
                    if (!memeProfil)
                        ProfilEnCours = value != null ? value.Clone() : null;

                    OnPropertyChanged(nameof(PorteeLibelle));
                    _ = ChargerDetailAsync();
                    RafraichirCommandes();
                }
            }
        }

        private ProfilPaie _profilEnCours;
        public ProfilPaie ProfilEnCours
        {
            get { return _profilEnCours; }
            set
            {
                if (SetProperty(ref _profilEnCours, value))
                {
                    OnPropertyChanged(nameof(EditionOuverte));
                    OnPropertyChanged(nameof(PopulationSelectionnee));
                    RafraichirCommandes();
                }
            }
        }

        public bool EditionOuverte
        {
            get { return ProfilEnCours != null; }
        }

        public PopulationProfilOption PopulationSelectionnee
        {
            get
            {
                if (ProfilEnCours == null) return null;
                return PopulationProfilOption.Tous.FirstOrDefault(p => p.Valeur == ProfilEnCours.Population);
            }
            set
            {
                if (ProfilEnCours == null || value == null) return;
                ProfilEnCours.Population = value.Valeur;
                OnPropertyChanged();
            }
        }

        /// <summary>
        /// Ce qu'une modification du profil va déplacer. Affiché en permanence près du
        /// bouton d'enregistrement : c'est la seule information qui manque au moment
        /// exact où le geste devient risqué.
        /// </summary>
        public string PorteeLibelle
        {
            get
            {
                if (ProfilSelectionne == null)
                    return "Nouveau profil : il ne concerne encore personne.";

                int n = ProfilSelectionne.NombreSalaries;

                if (n == 0)
                    return "Aucun salarié ne porte ce profil : le modifier ne change aucun bulletin.";

                return string.Format(
                    "{0} salarié(s) portent ce profil : toute modification changera leur bulletin " +
                    "dès le prochain calcul d'une période ouverte.", n);
            }
        }

        // ── Rubriques du profil ──────────────────────────────────────────────

        private LigneProfil _ligneSelectionnee;
        public LigneProfil LigneSelectionnee
        {
            get { return _ligneSelectionnee; }
            set
            {
                if (SetProperty(ref _ligneSelectionnee, value))
                    RafraichirCommandes();
            }
        }

        private Rubrique _rubriqueAAjouter;
        public Rubrique RubriqueAAjouter
        {
            get { return _rubriqueAAjouter; }
            set
            {
                if (SetProperty(ref _rubriqueAAjouter, value))
                    RafraichirCommandes();
            }
        }

        public int NombreLignes
        {
            get { return Lignes.Count; }
        }

        // ── Rattachements ────────────────────────────────────────────────────

        private Employee _salarieAAffecter;
        public Employee SalarieAAffecter
        {
            get { return _salarieAAffecter; }
            set
            {
                if (SetProperty(ref _salarieAAffecter, value))
                    RafraichirCommandes();
            }
        }

        private DateTime _dateEffet;
        /// <summary>
        /// Date à partir de laquelle le rattachement s'applique. Elle compte : un salarié
        /// promu en avril garde son ancien profil sur ses bulletins de janvier à mars.
        /// </summary>
        public DateTime DateEffet
        {
            get { return _dateEffet; }
            set { SetProperty(ref _dateEffet, value); }
        }

        private string _motifAffectation;
        public string MotifAffectation
        {
            get { return _motifAffectation; }
            set { SetProperty(ref _motifAffectation, value); }
        }

        private AffectationProfil _affectationSelectionnee;
        public AffectationProfil AffectationSelectionnee
        {
            get { return _affectationSelectionnee; }
            set
            {
                if (SetProperty(ref _affectationSelectionnee, value))
                    RafraichirCommandes();
            }
        }

        public int NombreProfils
        {
            get { return Profils.Count; }
        }

        public bool ADesAvertissements
        {
            get { return Avertissements.Count > 0; }
        }

        // ── Recherche et filtres sur la liste des profils ───────────────────────

        private string _searchText;
        public string SearchText
        {
            get { return _searchText; }
            set
            {
                if (SetProperty(ref _searchText, value))
                {
                    ProfilsView?.Refresh();
                    OnPropertyChanged(nameof(HasSearchText));
                    OnPropertyChanged(nameof(HasActiveFilters));
                    OnPropertyChanged(nameof(NombreProfilsAffiches));
                }
            }
        }

        public bool HasSearchText
        {
            get { return !string.IsNullOrWhiteSpace(SearchText); }
        }

        private PopulationProfil? _selectedPopulationFilter;
        public PopulationProfil? SelectedPopulationFilter
        {
            get { return _selectedPopulationFilter; }
            set
            {
                if (SetProperty(ref _selectedPopulationFilter, value))
                {
                    ProfilsView?.Refresh();
                    OnPropertyChanged(nameof(HasActiveFilters));
                    OnPropertyChanged(nameof(NombreProfilsAffiches));
                }
            }
        }

        public List<KeyValuePair<PopulationProfil?, string>> PopulationFiltreOptions
        {
            get
            {
                var options = new List<KeyValuePair<PopulationProfil?, string>>
                {
                    new KeyValuePair<PopulationProfil?, string>(null, "Toutes les populations")
                };
                options.AddRange(PopulationProfilOption.Tous
                    .Select(p => new KeyValuePair<PopulationProfil?, string>(p.Valeur, p.Libelle)));
                return options;
            }
        }

        public bool HasActiveFilters
        {
            get { return HasSearchText || SelectedPopulationFilter.HasValue; }
        }

        public int NombreProfilsAffiches
        {
            get { return ProfilsView != null ? ProfilsView.Cast<ProfilPaie>().Count() : Profils.Count; }
        }

        private bool FiltrerProfil(object obj)
        {
            var profil = obj as ProfilPaie;
            if (profil == null) return false;

            if (SelectedPopulationFilter.HasValue && profil.Population != SelectedPopulationFilter.Value)
                return false;

            if (!string.IsNullOrWhiteSpace(SearchText))
            {
                string q = SearchText.Trim();
                bool match = (profil.Code != null && profil.Code.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0)
                          || (profil.Libelle != null && profil.Libelle.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0);
                if (!match) return false;
            }

            return true;
        }

        // ── Recherche rapide du salarié pour le rattachement ────────────────────

        private string _filtreSalarie;
        public string FiltreSalarie
        {
            get { return _filtreSalarie; }
            set
            {
                if (SetProperty(ref _filtreSalarie, value))
                {
                    OnPropertyChanged(nameof(HasFiltreSalarie));
                    OnPropertyChanged(nameof(SalariesFiltres));
                }
            }
        }

        public bool HasFiltreSalarie
        {
            get { return !string.IsNullOrWhiteSpace(FiltreSalarie); }
        }

        public IList<Employee> SalariesFiltres
        {
            get
            {
                if (string.IsNullOrWhiteSpace(FiltreSalarie)) return Salaries;

                string q = FiltreSalarie.Trim();
                return Salaries.Where(s =>
                    (s.LastName != null && s.LastName.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0)
                    || (s.FirstName != null && s.FirstName.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0)
                    || (s.Matricule != null && s.Matricule.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0)
                    || (s.Fonction != null && s.Fonction.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0))
                    .ToList();
            }
        }

        // ── Commandes ────────────────────────────────────────────────────────

        public ICommand ChargerCommand { get; }
        public ICommand NouveauCommand { get; }
        public ICommand EnregistrerCommand { get; }
        public ICommand AnnulerSaisieCommand { get; }
        public ICommand SupprimerCommand { get; }
        public ICommand AjouterRubriqueCommand { get; }
        public ICommand RetirerRubriqueCommand { get; }
        public ICommand EnregistrerRubriquesCommand { get; }
        public ICommand AffecterCommand { get; }
        public ICommand RetirerProfilCommand { get; }
        public ICommand SupprimerAffectationCommand { get; }
        public ICommand ClearSearchCommand { get; }
        public ICommand ResetFiltersCommand { get; }
        public ICommand ClearFiltreSalarieCommand { get; }

        private void RafraichirCommandes()
        {
            (ChargerCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (NouveauCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (EnregistrerCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (AnnulerSaisieCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (SupprimerCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (AjouterRubriqueCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (RetirerRubriqueCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (EnregistrerRubriquesCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (AffecterCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (RetirerProfilCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (SupprimerAffectationCommand as RelayCommand)?.RaiseCanExecuteChanged();
        }

        // ── Chargement ───────────────────────────────────────────────────────

        public async Task ChargerAsync()
        {
            try
            {
                IsBusy = true;
                StatusMessage = "Chargement des profils...";

                _plan = await _rubriqueService.ChargerAsync();

                RubriquesDisponibles.Clear();
                foreach (var rubrique in _plan
                             .Where(ProfilPaieService.EstApplicable)
                             .OrderBy(r => r.Ordre).ThenBy(r => r.Code))
                {
                    RubriquesDisponibles.Add(rubrique);
                }

                Salaries.Clear();
                foreach (var salarie in await _dataService.LoadEmployeesAsync())
                    Salaries.Add(salarie);
                OnPropertyChanged(nameof(SalariesFiltres));

                _toutesAffectations = await _profilService.ChargerAffectationsAsync();

                int idRetenu = ProfilSelectionne != null ? ProfilSelectionne.Id : 0;

                Profils.Clear();
                foreach (var profil in await _profilService.ChargerAsync())
                    Profils.Add(profil);

                _profilSelectionne = null;
                ProfilSelectionne = idRetenu > 0
                    ? Profils.FirstOrDefault(p => p.Id == idRetenu)
                    : null;

                OnPropertyChanged(nameof(NombreProfils));
                OnPropertyChanged(nameof(NombreProfilsAffiches));

                StatusMessage = Profils.Count == 0
                    ? "Aucun profil : créez-en un par population — cadre, employé, journalier…"
                    : string.Format(
                        "{0} profil(s), {1} rubrique(s) saisissables disponibles au plan de paie.",
                        Profils.Count, RubriquesDisponibles.Count);
            }
            catch (Exception ex)
            {
                StatusMessage = "Erreur lors du chargement : " + ex.Message;
            }
            finally
            {
                IsBusy = false;
                RafraichirCommandes();
            }
        }

        /// <summary>Rubriques et rattachements du profil sélectionné.</summary>
        private async Task ChargerDetailAsync()
        {
            Lignes.Clear();
            Affectations.Clear();
            Avertissements.Clear();

            var profil = ProfilSelectionne;

            if (profil == null || profil.Id <= 0)
            {
                NotifierDetail();
                return;
            }

            try
            {
                foreach (var ligne in await _profilService.ChargerLignesAsync(profil.Id, _plan))
                    Lignes.Add(ligne);

                // L'historique montre TOUS les mouvements des salariés passés par ce profil,
                // pas seulement leur rattachement : un salarié qui l'a quitté pour un autre
                // profil doit se lire ici, sans quoi on chercherait longtemps pourquoi son
                // bulletin a changé.
                var concernes = new HashSet<string>(
                    _toutesAffectations
                        .Where(a => a.ProfilId == profil.Id && !string.IsNullOrWhiteSpace(a.EmployeeId))
                        .Select(a => a.EmployeeId),
                    StringComparer.OrdinalIgnoreCase);

                foreach (var affectation in _toutesAffectations
                             .Where(a => a.EmployeeId != null && concernes.Contains(a.EmployeeId))
                             .OrderBy(a => a.NomSalarie)
                             .ThenByDescending(a => a.DateEffet))
                {
                    Affectations.Add(affectation);
                }

                foreach (var message in ProfilPaieService.Anomalies(Lignes, _plan))
                    Avertissements.Add(message);
            }
            catch (Exception ex)
            {
                StatusMessage = "Détail du profil illisible : " + ex.Message;
            }

            NotifierDetail();
        }

        private void NotifierDetail()
        {
            OnPropertyChanged(nameof(NombreLignes));
            OnPropertyChanged(nameof(ADesAvertissements));
            OnPropertyChanged(nameof(PorteeLibelle));
        }

        // ── Édition du profil ────────────────────────────────────────────────

        private void Nouveau()
        {
            ProfilSelectionne = null;

            ProfilEnCours = new ProfilPaie
            {
                Population = PopulationProfil.Employe,
                Actif = true,
                Ordre = (Profils.Count + 1) * 10
            };

            StatusMessage = "Nouveau profil : donnez-lui un code, un libellé et une population.";
        }

        private void AnnulerSaisie()
        {
            ProfilEnCours = ProfilSelectionne != null ? ProfilSelectionne.Clone() : null;
            StatusMessage = "Saisie abandonnée.";
        }

        private async Task EnregistrerAsync()
        {
            var profil = ProfilEnCours;
            if (profil == null) return;

            var anomalie = ProfilPaieService.Anomalie(profil);
            if (anomalie != null)
            {
                StatusMessage = anomalie;
                return;
            }

            try
            {
                IsBusy = true;
                StatusMessage = "Enregistrement...";

                int salariesTouches = ProfilSelectionne != null ? ProfilSelectionne.NombreSalaries : 0;

                await _profilService.EnregistrerAsync(profil);

                int id = profil.Id;
                ProfilEnCours = null;
                _profilSelectionne = null;

                await ChargerAsync();
                ProfilSelectionne = Profils.FirstOrDefault(p => p.Id == id);

                StatusMessage = salariesTouches > 0
                    ? string.Format(
                        "Profil enregistré. {0} salarié(s) le portent : leurs bulletins des périodes " +
                        "ouvertes changeront au prochain calcul.", salariesTouches)
                    : "Profil enregistré.";
            }
            catch (InvalidOperationException ex)
            {
                StatusMessage = ex.Message;
            }
            catch (Exception ex)
            {
                StatusMessage = "Erreur lors de l'enregistrement : " + ex.Message;
            }
            finally
            {
                IsBusy = false;
                RafraichirCommandes();
            }
        }

        private async Task SupprimerAsync()
        {
            var profil = ProfilSelectionne;
            if (profil == null || profil.Id <= 0) return;

            try
            {
                IsBusy = true;
                StatusMessage = "Suppression...";

                await _profilService.SupprimerAsync(profil);

                ProfilEnCours = null;
                _profilSelectionne = null;
                await ChargerAsync();

                StatusMessage = string.Format("Profil {0} supprimé.", profil.Code);
            }
            catch (InvalidOperationException ex)
            {
                StatusMessage = ex.Message;
            }
            catch (Exception ex)
            {
                StatusMessage = "Erreur lors de la suppression : " + ex.Message;
            }
            finally
            {
                IsBusy = false;
                RafraichirCommandes();
            }
        }

        // ── Rubriques ────────────────────────────────────────────────────────

        private void AjouterRubrique()
        {
            var rubrique = RubriqueAAjouter;
            if (rubrique == null || ProfilSelectionne == null) return;

            string code = (rubrique.Code ?? "").Trim().ToUpperInvariant();

            if (Lignes.Any(l => string.Equals((l.CodeRubrique ?? "").Trim(), code,
                                              StringComparison.OrdinalIgnoreCase)))
            {
                StatusMessage = string.Format("{0} figure déjà dans ce profil.", code);
                return;
            }

            var ligne = new LigneProfil
            {
                ProfilId = ProfilSelectionne.Id,
                CodeRubrique = code,
                LibelleRubrique = rubrique.Libelle,
                Sens = rubrique.Sens,
                ModeCalcul = rubrique.ModeCalcul,
                Ordre = (Lignes.Count + 1) * 10,
                Active = true
            };

            Lignes.Add(ligne);
            LigneSelectionnee = ligne;

            NotifierDetail();
            StatusMessage = string.Format(
                "{0} ajoutée. Renseignez le montant par défaut, puis enregistrez les rubriques.",
                ligne.Designation);
        }

        private void RetirerRubrique()
        {
            var ligne = LigneSelectionnee;
            if (ligne == null) return;

            Lignes.Remove(ligne);
            LigneSelectionnee = null;

            NotifierDetail();
            StatusMessage = string.Format(
                "{0} retirée de la liste. Enregistrez les rubriques pour appliquer.", ligne.Designation);
        }

        private async Task EnregistrerRubriquesAsync()
        {
            var profil = ProfilSelectionne;
            if (profil == null || profil.Id <= 0) return;

            try
            {
                IsBusy = true;
                StatusMessage = "Enregistrement des rubriques...";

                await _profilService.EnregistrerLignesAsync(profil.Id, Lignes);

                int id = profil.Id;
                int salariesTouches = profil.NombreSalaries;

                _profilSelectionne = null;
                await ChargerAsync();
                ProfilSelectionne = Profils.FirstOrDefault(p => p.Id == id);

                StatusMessage = salariesTouches > 0
                    ? string.Format(
                        "Rubriques enregistrées. {0} salarié(s) portent ce profil : leurs bulletins " +
                        "des périodes ouvertes changeront au prochain calcul.", salariesTouches)
                    : "Rubriques enregistrées.";
            }
            catch (InvalidOperationException ex)
            {
                StatusMessage = ex.Message;
            }
            catch (Exception ex)
            {
                StatusMessage = "Erreur lors de l'enregistrement des rubriques : " + ex.Message;
            }
            finally
            {
                IsBusy = false;
                RafraichirCommandes();
            }
        }

        // ── Rattachements ────────────────────────────────────────────────────

        private async Task AffecterAsync()
        {
            var profil = ProfilSelectionne;
            var salarie = SalarieAAffecter;

            if (profil == null || profil.Id <= 0 || salarie == null) return;

            try
            {
                IsBusy = true;
                StatusMessage = "Rattachement...";

                await _profilService.AffecterAsync(
                    salarie.EmployeeId, profil.Id, DateEffet, MotifAffectation);

                MotifAffectation = null;

                int id = profil.Id;
                _profilSelectionne = null;
                await ChargerAsync();
                ProfilSelectionne = Profils.FirstOrDefault(p => p.Id == id);

                StatusMessage = string.Format(
                    "{0} {1} porte le profil {2} à partir du {3:dd/MM/yyyy}.",
                    salarie.LastName, salarie.FirstName, profil.Code, DateEffet);
            }
            catch (InvalidOperationException ex)
            {
                StatusMessage = ex.Message;
            }
            catch (Exception ex)
            {
                StatusMessage = "Erreur lors du rattachement : " + ex.Message;
            }
            finally
            {
                IsBusy = false;
                RafraichirCommandes();
            }
        }

        /// <summary>
        /// Retire son profil à un salarié à partir d'une date. Le retrait est lui-même
        /// daté et conservé : les bulletins antérieurs gardent le profil qui les a
        /// calculés.
        /// </summary>
        private async Task RetirerProfilAsync()
        {
            var salarie = SalarieAAffecter;
            if (salarie == null) return;

            try
            {
                IsBusy = true;
                StatusMessage = "Retrait du profil...";

                await _profilService.AffecterAsync(salarie.EmployeeId, 0, DateEffet, MotifAffectation);

                MotifAffectation = null;

                int id = ProfilSelectionne != null ? ProfilSelectionne.Id : 0;
                _profilSelectionne = null;
                await ChargerAsync();
                ProfilSelectionne = id > 0 ? Profils.FirstOrDefault(p => p.Id == id) : null;

                StatusMessage = string.Format(
                    "{0} {1} n'a plus de profil à partir du {2:dd/MM/yyyy}. Ses bulletins antérieurs " +
                    "gardent celui qui les a calculés.",
                    salarie.LastName, salarie.FirstName, DateEffet);
            }
            catch (Exception ex)
            {
                StatusMessage = "Erreur lors du retrait : " + ex.Message;
            }
            finally
            {
                IsBusy = false;
                RafraichirCommandes();
            }
        }

        private async Task SupprimerAffectationAsync()
        {
            var affectation = AffectationSelectionnee;
            if (affectation == null || affectation.Id <= 0) return;

            try
            {
                IsBusy = true;
                StatusMessage = "Suppression du rattachement...";

                await _profilService.SupprimerAffectationAsync(affectation);

                int id = ProfilSelectionne != null ? ProfilSelectionne.Id : 0;
                _profilSelectionne = null;
                await ChargerAsync();
                ProfilSelectionne = id > 0 ? Profils.FirstOrDefault(p => p.Id == id) : null;

                StatusMessage = string.Format(
                    "Rattachement du {0} supprimé de l'historique de {1}.",
                    affectation.DateEffetLibelle, affectation.NomSalarie);
            }
            catch (Exception ex)
            {
                StatusMessage = "Erreur lors de la suppression : " + ex.Message;
            }
            finally
            {
                IsBusy = false;
                RafraichirCommandes();
            }
        }
    }
}
