using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using IUTSCalculator.Helpers;
using IUTSCalculator.Models;
using IUTSCalculator.Services;

namespace IUTSCalculator.ViewModels
{
    /// <summary>Un type d'absence proposé dans le sélecteur, avec son libellé.</summary>
    public class TypeAbsenceOption
    {
        public TypeAbsenceOption(TypeAbsence valeur)
        {
            Valeur = valeur;
        }

        public TypeAbsence Valeur { get; private set; }

        public string Libelle
        {
            get { return Absence.Libelle(Valeur); }
        }

        /// <summary>« Rémunérée » ou « Non rémunérée », affiché en regard du choix.</summary>
        public string Traitement
        {
            get { return Absence.EstRemunereParNature(Valeur) ? "Rémunérée" : "Non rémunérée"; }
        }

        /// <summary>
        /// Liste construite UNE fois. Si le getter fabriquait de nouvelles instances à
        /// chaque appel, l'option renvoyée par le ViewModel n'appartiendrait pas à la
        /// liste affichée par le sélecteur, qui ne montrerait alors aucune sélection.
        /// </summary>
        private static readonly List<TypeAbsenceOption> _tous =
            Enum.GetValues(typeof(TypeAbsence))
                .Cast<TypeAbsence>()
                .Select(t => new TypeAbsenceOption(t))
                .ToList();

        public static List<TypeAbsenceOption> Tous
        {
            get { return _tous; }
        }
    }

    /// <summary>
    /// Écran « Congés et absences » (module PAI-04).
    ///
    /// Deux choses s'y jouent. À gauche, les absences de la période : qui, quand, à quel
    /// titre. À droite, le compteur de congés de tout l'effectif — acquis, pris, solde —
    /// qui est ce que réclame un salarié quand il demande « combien me reste-t-il ? ».
    ///
    /// Le champ « congés pris » du bulletin était jusqu'ici saisi à la main, sans rien
    /// derrière. Il vient désormais d'ici.
    /// </summary>
    public class AbsenceViewModel : BaseViewModel
    {
        private readonly AbsenceService _absenceService;
        private readonly DataService _dataService;
        private readonly AbsenceExcelService _absenceExcelService;

        private List<Absence> _toutesLesAbsences = new List<Absence>();

        /// <summary>Libellé sentinelle du groupe cible « tout l'effectif » (attribution groupée).</summary>
        private const string TousLesSalariesLibelle = "Tous les salariés";

        public ObservableCollection<Absence> Absences { get; } = new ObservableCollection<Absence>();
        public ObservableCollection<SoldeConges> Soldes { get; } = new ObservableCollection<SoldeConges>();
        public ObservableCollection<Employee> Salaries { get; } = new ObservableCollection<Employee>();
        public ObservableCollection<string> Avertissements { get; } = new ObservableCollection<string>();
        public ObservableCollection<string> CategoriesFiltre { get; } = new ObservableCollection<string> { TousLesSalariesLibelle };

        /// <summary>Vue filtrée d'Absences (recherche texte + filtres type/rémunérée) pour la grille.</summary>
        public ICollectionView AbsencesView { get; }

        /// <summary>Vue du compteur de congés — pas de filtre propre, juste le même style de liaison que la grille de gauche.</summary>
        public ICollectionView SoldesView { get; }

        public AbsenceViewModel(AbsenceService absenceService, DataService dataService, AbsenceExcelService absenceExcelService)
        {
            if (absenceService == null) throw new ArgumentNullException("absenceService");
            if (dataService == null) throw new ArgumentNullException("dataService");
            if (absenceExcelService == null) throw new ArgumentNullException("absenceExcelService");

            _absenceService = absenceService;
            _dataService = dataService;
            _absenceExcelService = absenceExcelService;

            Title = "Congés et absences";

            var reference = DateTime.Today;
            _annee = reference.Year;
            _moisSelectionne = MoisOption.Tous.FirstOrDefault(m => m.Numero == reference.Month);
            _categorieFiltreSelectionnee = TousLesSalariesLibelle;

            AbsencesView = CollectionViewSource.GetDefaultView(Absences);
            AbsencesView.Filter = FiltrerAbsence;
            SoldesView = CollectionViewSource.GetDefaultView(Soldes);

            ChargerCommand = new RelayCommand(async () => await ChargerAsync(), () => !IsBusy);
            NouvelleCommand = new RelayCommand(Nouvelle, () => PeriodeOuverte);
            EnregistrerCommand = new RelayCommand(
                async () => await EnregistrerAsync(),
                () => !IsBusy && PeriodeOuverte && AbsenceEnCours != null);
            SupprimerCommand = new RelayCommand(
                async () => await SupprimerAsync(),
                () => !IsBusy && PeriodeOuverte && AbsenceSelectionnee != null && AbsenceSelectionnee.Id > 0);
            AnnulerCommand = new RelayCommand(Annuler, () => AbsenceEnCours != null);
            CalculerJoursCommand = new RelayCommand(CalculerJours, () => AbsenceEnCours != null);
            ExporterModeleCommand = new RelayCommand(async () => await ExporterModeleAsync(), () => !IsBusy);
            ImporterExcelCommand = new RelayCommand(async () => await ImporterExcelAsync(), () => !IsBusy && PeriodeOuverte);
            ClearSearchCommand = new RelayCommand(() => SearchText = string.Empty);
            ResetFiltersCommand = new RelayCommand(() =>
            {
                SearchText = string.Empty;
                SelectedTypeFilter = null;
                SelectedRemunereeFilter = null;
            });
            ClearFiltreSalarieCommand = new RelayCommand(() => FiltreSalarie = string.Empty);
            ToggleAttributionGroupeeCommand = new RelayCommand(ToggleAttributionGroupee, () => !IsBusy);
            CalculerJoursGroupeeCommand = new RelayCommand(CalculerJoursGroupee,
                () => DateDebutGroupee.HasValue && DateFinGroupee.HasValue);
            AppliquerAttributionGroupeeCommand = new RelayCommand(
                async () => await AppliquerAttributionGroupeeAsync(),
                () => !IsBusy && PeriodeOuverte && NombreSalariesCibles > 0 && JoursGroupee > 0m
                      && DateDebutGroupee.HasValue && DateFinGroupee.HasValue);

            _ = ChargerAsync();
        }

        /// <summary>
        /// Ce ViewModel n'est créé qu'une seule fois (voir MainViewModel) et ne recharge
        /// jamais automatiquement après ce premier chargement : sans ce rafraîchissement,
        /// revenir sur cet écran après avoir modifié une fiche employé ailleurs continuait
        /// d'afficher les anciennes données tant que le dossier n'était pas fermé puis
        /// rouvert (signalé le 16 septembre 2026, voir ETAT-DEVELOPPEMENT.md item 21). Sans
        /// risque pour une saisie en cours : ChargerAsync ne touche pas AbsenceEnCours.
        /// </summary>
        protected override void OnIsActiveChanged()
        {
            if (IsActive)
                _ = ChargerAsync();
        }

        // ── Période ──────────────────────────────────────────────────────────

        public IEnumerable<MoisOption> MoisDisponibles
        {
            get { return MoisOption.Tous; }
        }

        public IEnumerable<int> AnneesDisponibles
        {
            get { return Enumerable.Range(DateTime.Today.Year - 5, 7).ToList(); }
        }

        public IEnumerable<TypeAbsenceOption> TypesAbsence
        {
            get { return TypeAbsenceOption.Tous; }
        }

        private int _annee;
        public int Annee
        {
            get { return _annee; }
            set
            {
                if (SetProperty(ref _annee, value))
                    _ = ChargerAsync();
            }
        }

        private MoisOption _moisSelectionne;
        public MoisOption MoisSelectionne
        {
            get { return _moisSelectionne; }
            set
            {
                if (SetProperty(ref _moisSelectionne, value))
                    _ = ChargerAsync();
            }
        }

        public int Mois
        {
            get { return MoisSelectionne != null ? MoisSelectionne.Numero : DateTime.Today.Month; }
        }

        public string PeriodeLibelle
        {
            get
            {
                var debut = new DateTime(Annee, Mois, 1);
                return debut.ToString("MMMM yyyy", new System.Globalization.CultureInfo("fr-FR"));
            }
        }

        private bool _periodeOuverte = true;
        /// <summary>Faux quand la période est clôturée : l'écran reste consultable, pas modifiable.</summary>
        public bool PeriodeOuverte
        {
            get { return _periodeOuverte; }
            set
            {
                if (SetProperty(ref _periodeOuverte, value))
                {
                    OnPropertyChanged(nameof(PeriodeCloturee));
                    RafraichirCommandes();
                }
            }
        }

        public bool PeriodeCloturee
        {
            get { return !PeriodeOuverte; }
        }

        // ── Sélection et édition ─────────────────────────────────────────────

        private Absence _absenceSelectionnee;
        public Absence AbsenceSelectionnee
        {
            get { return _absenceSelectionnee; }
            set
            {
                if (SetProperty(ref _absenceSelectionnee, value))
                {
                    // Sélectionner une ligne l'ouvre à l'édition sur une COPIE : tant que
                    // l'enregistrement n'est pas demandé, la liste affichée reste celle de
                    // la base, et une modification abandonnée ne laisse pas de trace.
                    AbsenceEnCours = value != null ? value.Clone() : null;
                    RafraichirCommandes();
                }
            }
        }

        private Absence _absenceEnCours;
        /// <summary>Absence en cours de saisie ou de modification. Null quand rien n'est ouvert.</summary>
        public Absence AbsenceEnCours
        {
            get { return _absenceEnCours; }
            set
            {
                if (SetProperty(ref _absenceEnCours, value))
                {
                    OnPropertyChanged(nameof(EditionOuverte));
                    OnPropertyChanged(nameof(TypeSelectionne));
                    RafraichirCommandes();
                }
            }
        }

        public bool EditionOuverte
        {
            get { return AbsenceEnCours != null; }
        }

        /// <summary>
        /// Type de l'absence en cours, exposé par son option pour que le sélecteur affiche
        /// un libellé plutôt qu'une valeur d'énumération.
        /// </summary>
        public TypeAbsenceOption TypeSelectionne
        {
            get
            {
                if (AbsenceEnCours == null) return null;
                return TypeAbsenceOption.Tous.FirstOrDefault(t => t.Valeur == AbsenceEnCours.Type);
            }
            set
            {
                if (AbsenceEnCours == null || value == null) return;
                AbsenceEnCours.Type = value.Valeur;
                OnPropertyChanged();
            }
        }

        private Employee _salarieSelectionne;
        public Employee SalarieSelectionne
        {
            get { return _salarieSelectionne; }
            set
            {
                if (SetProperty(ref _salarieSelectionne, value) && AbsenceEnCours != null && value != null)
                {
                    AbsenceEnCours.EmployeeId = value.EmployeeId;
                    AbsenceEnCours.NomSalarie = string.Format("{0} {1}", value.LastName, value.FirstName).Trim();
                }
            }
        }

        public int NombreAbsences
        {
            get { return Absences.Count; }
        }

        public bool ADesAvertissements
        {
            get { return Avertissements.Count > 0; }
        }

        // ── Recherche et filtres sur la liste des absences ──────────────────────

        private string _searchText;
        public string SearchText
        {
            get { return _searchText; }
            set
            {
                if (SetProperty(ref _searchText, value))
                {
                    AbsencesView?.Refresh();
                    OnPropertyChanged(nameof(HasSearchText));
                    OnPropertyChanged(nameof(HasActiveFilters));
                    OnPropertyChanged(nameof(NombreAbsencesAffichees));
                }
            }
        }

        public bool HasSearchText
        {
            get { return !string.IsNullOrWhiteSpace(SearchText); }
        }

        private TypeAbsence? _selectedTypeFilter;
        public TypeAbsence? SelectedTypeFilter
        {
            get { return _selectedTypeFilter; }
            set
            {
                if (SetProperty(ref _selectedTypeFilter, value))
                {
                    AbsencesView?.Refresh();
                    OnPropertyChanged(nameof(HasActiveFilters));
                    OnPropertyChanged(nameof(NombreAbsencesAffichees));
                }
            }
        }

        public List<KeyValuePair<TypeAbsence?, string>> TypesAbsenceFiltreOptions
        {
            get
            {
                var options = new List<KeyValuePair<TypeAbsence?, string>>
                {
                    new KeyValuePair<TypeAbsence?, string>(null, "Tous les types")
                };
                options.AddRange(TypeAbsenceOption.Tous
                    .Select(t => new KeyValuePair<TypeAbsence?, string>(t.Valeur, t.Libelle)));
                return options;
            }
        }

        private bool? _selectedRemunereeFilter;
        public bool? SelectedRemunereeFilter
        {
            get { return _selectedRemunereeFilter; }
            set
            {
                if (SetProperty(ref _selectedRemunereeFilter, value))
                {
                    AbsencesView?.Refresh();
                    OnPropertyChanged(nameof(HasActiveFilters));
                    OnPropertyChanged(nameof(NombreAbsencesAffichees));
                }
            }
        }

        public List<KeyValuePair<bool?, string>> RemunereeFiltreOptions { get; } = new List<KeyValuePair<bool?, string>>
        {
            new KeyValuePair<bool?, string>(null, "Rémunérée ou non"),
            new KeyValuePair<bool?, string>(true, "Rémunérées seulement"),
            new KeyValuePair<bool?, string>(false, "Non rémunérées seulement")
        };

        public bool HasActiveFilters
        {
            get { return HasSearchText || SelectedTypeFilter.HasValue || SelectedRemunereeFilter.HasValue; }
        }

        public int NombreAbsencesAffichees
        {
            get { return AbsencesView != null ? AbsencesView.Cast<Absence>().Count() : Absences.Count; }
        }

        private bool FiltrerAbsence(object obj)
        {
            var absence = obj as Absence;
            if (absence == null) return false;

            if (SelectedTypeFilter.HasValue && absence.Type != SelectedTypeFilter.Value)
                return false;

            if (SelectedRemunereeFilter.HasValue && absence.EstRemuneree != SelectedRemunereeFilter.Value)
                return false;

            if (!string.IsNullOrWhiteSpace(SearchText))
            {
                string q = SearchText.Trim();
                bool match = (absence.NomSalarie != null && absence.NomSalarie.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0)
                          || (absence.TypeLibelle != null && absence.TypeLibelle.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0)
                          || (absence.Commentaire != null && absence.Commentaire.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0)
                          || (absence.EmployeeId != null && absence.EmployeeId.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0);
                if (!match) return false;
            }

            return true;
        }

        // ── Recherche du salarié dans le formulaire d'édition ───────────────────

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

        /// <summary>Droit mensuel appliqué, tel que le dossier l'a réglé.</summary>
        private decimal _droitMensuel = AbsenceService.DroitMensuelLegal;
        public decimal DroitMensuel
        {
            get { return _droitMensuel; }
            private set
            {
                if (SetProperty(ref _droitMensuel, value))
                    OnPropertyChanged(nameof(DroitMensuelLibelle));
            }
        }

        public string DroitMensuelLibelle
        {
            get { return string.Format("{0:0.##} jour(s) ouvrable(s) acquis par mois de service", DroitMensuel); }
        }

        private BaseRetenueAbsence _baseRetenue = BaseRetenueAbsence.TrentiemeMensuel;
        public string BaseRetenueLibelle
        {
            get
            {
                switch (_baseRetenue)
                {
                    case BaseRetenueAbsence.JoursOuvrablesDuMois:
                        return "Retenue au prorata des jours ouvrables du mois";
                    case BaseRetenueAbsence.TauxHoraire:
                        return "Retenue au taux horaire (173,33 h)";
                    default:
                        return "Retenue au 1/30e du salaire mensuel";
                }
            }
        }

        // ── Attribution groupée de congés (panneau rétractable) ─────────────────

        private bool _attributionGroupeeOuverte;
        public bool AttributionGroupeeOuverte
        {
            get { return _attributionGroupeeOuverte; }
            set { SetProperty(ref _attributionGroupeeOuverte, value); }
        }

        private string _categorieFiltreSelectionnee;
        public string CategorieFiltreSelectionnee
        {
            get { return _categorieFiltreSelectionnee; }
            set
            {
                if (SetProperty(ref _categorieFiltreSelectionnee, value))
                {
                    OnPropertyChanged(nameof(NombreSalariesCibles));
                    RafraichirCommandes();
                }
            }
        }

        private DateTime? _dateDebutGroupee;
        public DateTime? DateDebutGroupee
        {
            get { return _dateDebutGroupee; }
            set
            {
                if (SetProperty(ref _dateDebutGroupee, value))
                    RafraichirCommandes();
            }
        }

        private DateTime? _dateFinGroupee;
        public DateTime? DateFinGroupee
        {
            get { return _dateFinGroupee; }
            set
            {
                if (SetProperty(ref _dateFinGroupee, value))
                    RafraichirCommandes();
            }
        }

        private decimal _joursGroupee;
        public decimal JoursGroupee
        {
            get { return _joursGroupee; }
            set
            {
                if (SetProperty(ref _joursGroupee, value))
                    RafraichirCommandes();
            }
        }

        private string _commentaireGroupe;
        public string CommentaireGroupe
        {
            get { return _commentaireGroupe; }
            set { SetProperty(ref _commentaireGroupe, value); }
        }

        /// <summary>
        /// Salariés visés par l'attribution groupée : ceux de la catégorie choisie (ou tout
        /// l'effectif), limités aux salariés actifs — un salarié sorti ou suspendu n'a rien à
        /// faire dans une attribution collective de congés, comme pour les écrans de lot
        /// (dette n°20, ETAT-DEVELOPPEMENT.md).
        /// </summary>
        private IEnumerable<Employee> SalariesCibles()
        {
            return Salaries.Where(s =>
                s.EstActif &&
                (CategorieFiltreSelectionnee == TousLesSalariesLibelle
                    || string.Equals(s.Category, CategorieFiltreSelectionnee, StringComparison.OrdinalIgnoreCase)));
        }

        public int NombreSalariesCibles
        {
            get { return SalariesCibles().Count(); }
        }

        // ── Commandes ────────────────────────────────────────────────────────

        public ICommand ChargerCommand { get; }
        public ICommand NouvelleCommand { get; }
        public ICommand EnregistrerCommand { get; }
        public ICommand SupprimerCommand { get; }
        public ICommand AnnulerCommand { get; }
        public ICommand CalculerJoursCommand { get; }
        /// <summary>Génère un fichier Excel de saisie en masse des congés du mois affiché.</summary>
        public ICommand ExporterModeleCommand { get; }
        /// <summary>Relit un fichier Excel rempli et enregistre les congés qu'il contient.</summary>
        public ICommand ImporterExcelCommand { get; }
        public ICommand ClearSearchCommand { get; }
        public ICommand ResetFiltersCommand { get; }
        public ICommand ClearFiltreSalarieCommand { get; }
        public ICommand ToggleAttributionGroupeeCommand { get; }
        public ICommand CalculerJoursGroupeeCommand { get; }
        public ICommand AppliquerAttributionGroupeeCommand { get; }

        private void RafraichirCommandes()
        {
            (ChargerCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (NouvelleCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (EnregistrerCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (SupprimerCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (AnnulerCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (CalculerJoursCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (ExporterModeleCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (ImporterExcelCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (ToggleAttributionGroupeeCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (CalculerJoursGroupeeCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (AppliquerAttributionGroupeeCommand as RelayCommand)?.RaiseCanExecuteChanged();
        }

        // ── Chargement ───────────────────────────────────────────────────────

        public async Task ChargerAsync()
        {
            try
            {
                IsBusy = true;
                StatusMessage = "Chargement des absences...";

                var reglages = await _dataService.LoadSettingsAsync();
                if (reglages != null)
                {
                    DroitMensuel = reglages.JoursCongesParMois > 0m
                        ? reglages.JoursCongesParMois
                        : AbsenceService.DroitMensuelLegal;
                    _baseRetenue = reglages.BaseRetenueAbsence;
                    OnPropertyChanged(nameof(BaseRetenueLibelle));
                }

                Salaries.Clear();
                foreach (var salarie in await _dataService.LoadEmployeesAsync())
                    Salaries.Add(salarie);
                OnPropertyChanged(nameof(SalariesFiltres));

                CategoriesFiltre.Clear();
                CategoriesFiltre.Add(TousLesSalariesLibelle);
                foreach (var categorie in Salaries
                    .Select(s => s.Category)
                    .Where(c => !string.IsNullOrWhiteSpace(c))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(c => c))
                {
                    CategoriesFiltre.Add(categorie);
                }
                if (string.IsNullOrEmpty(CategorieFiltreSelectionnee) || !CategoriesFiltre.Contains(CategorieFiltreSelectionnee))
                    CategorieFiltreSelectionnee = TousLesSalariesLibelle;
                else
                    OnPropertyChanged(nameof(NombreSalariesCibles));

                _toutesLesAbsences = await _absenceService.ChargerToutAsync();

                var periode = await _absenceService.ChargerPeriodeAsync(Annee, Mois);
                Absences.Clear();
                foreach (var absence in periode)
                    Absences.Add(absence);

                PeriodeOuverte = await PeriodeOuverteAsync();

                RafraichirSoldes();

                OnPropertyChanged(nameof(NombreAbsences));
                OnPropertyChanged(nameof(NombreAbsencesAffichees));
                OnPropertyChanged(nameof(PeriodeLibelle));

                StatusMessage = PeriodeCloturee
                    ? string.Format("Période {0} clôturée — consultation seule.", PeriodeLibelle)
                    : string.Format("{0} absence(s) sur {1}.", Absences.Count, PeriodeLibelle);
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

        private async Task<bool> PeriodeOuverteAsync()
        {
            try
            {
                // On pose la question au lieu d'attendre l'échec au moment d'enregistrer :
                // un écran grisé se comprend mieux qu'un message d'erreur après coup.
                return await _absenceService.PeriodeOuverteAsync(Annee, Mois);
            }
            catch
            {
                return true;
            }
        }

        private void RafraichirSoldes()
        {
            var arrete = new DateTime(Annee, Mois, 1).AddMonths(1).AddDays(-1);

            var soldes = AbsenceService.Soldes(
                Salaries, _toutesLesAbsences, arrete, DroitMensuel, Annee, Mois);

            Soldes.Clear();
            foreach (var solde in soldes)
                Soldes.Add(solde);

            Avertissements.Clear();
            foreach (var message in AbsenceService.Avertissements(soldes))
                Avertissements.Add(message);

            OnPropertyChanged(nameof(ADesAvertissements));
        }

        // ── Édition ──────────────────────────────────────────────────────────

        private void Nouvelle()
        {
            var debut = new DateTime(Annee, Mois, 1);

            AbsenceEnCours = new Absence
            {
                DateDebut = debut,
                DateFin = debut,
                NombreJours = 1m,
                Type = TypeAbsence.CongePaye
            };

            SalarieSelectionne = null;
            AbsenceSelectionnee = null;
            StatusMessage = "Nouvelle absence : choisissez le salarié, le type et les dates.";
        }

        private void Annuler()
        {
            AbsenceEnCours = null;
            StatusMessage = "Saisie abandonnée.";
        }

        /// <summary>
        /// Recalcule le nombre de jours d'après les dates, dimanches exclus. Proposé et
        /// non imposé : c'est le gestionnaire qui arrête le décompte, l'usage variant
        /// d'une entreprise à l'autre sur les jours fériés et les demi-journées.
        /// </summary>
        private void CalculerJours()
        {
            if (AbsenceEnCours == null) return;

            AbsenceEnCours.NombreJours = Absence.JoursEntre(
                AbsenceEnCours.DateDebut, AbsenceEnCours.DateFin, true);

            // Un dimanche seul : s'il est saisi en absence, c'est qu'il devait être travaillé.
            if (AbsenceEnCours.NombreJours == 0m
                && AbsenceEnCours.DateDebut.Date == AbsenceEnCours.DateFin.Date
                && AbsenceEnCours.DateDebut.DayOfWeek == DayOfWeek.Sunday)
            {
                AbsenceEnCours.NombreJours = 1m;
                StatusMessage = string.Format(
                    "Absence d'un dimanche ({0:dd/MM}) : 1 jour proposé. Si ce dimanche n'était pas travaillé, il n'y a pas d'absence à saisir.",
                    AbsenceEnCours.DateDebut);
                return;
            }

            StatusMessage = string.Format(
                "{0:0.##} jour(s) ouvrable(s) entre le {1:dd/MM} et le {2:dd/MM}, dimanches exclus.",
                AbsenceEnCours.NombreJours, AbsenceEnCours.DateDebut, AbsenceEnCours.DateFin);
        }

        private async Task EnregistrerAsync()
        {
            if (AbsenceEnCours == null) return;

            var anomalie = AbsenceService.Anomalie(AbsenceEnCours);
            if (anomalie != null)
            {
                StatusMessage = anomalie;
                return;
            }

            try
            {
                IsBusy = true;
                StatusMessage = "Enregistrement...";

                await _absenceService.EnregistrerAsync(AbsenceEnCours);

                AbsenceEnCours = null;
                await ChargerAsync();

                StatusMessage = "Absence enregistrée.";
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
            var cible = AbsenceSelectionnee;
            if (cible == null || cible.Id <= 0) return;

            try
            {
                IsBusy = true;
                StatusMessage = "Suppression...";

                await _absenceService.SupprimerAsync(cible);

                AbsenceEnCours = null;
                await ChargerAsync();

                StatusMessage = string.Format("Absence de {0} supprimée.", cible.NomSalarie);
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

        // ── Import / export Excel (saisie en masse des congés) ─────────────────

        /// <summary>
        /// Génère, pour la période affichée, un classeur Excel pré-rempli avec l'effectif et
        /// son solde de congés, prêt à être complété par un gestionnaire RH puis réimporté.
        /// </summary>
        private async Task ExporterModeleAsync()
        {
            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "Fichiers Excel (*.xlsx)|*.xlsx",
                DefaultExt = "xlsx",
                FileName = string.Format("Modele_Conges_{0:D4}_{1:D2}.xlsx", Annee, Mois)
            };
            if (dialog.ShowDialog() != true)
            {
                StatusMessage = "Export annulé.";
                return;
            }

            try
            {
                IsBusy = true;
                StatusMessage = "Génération du modèle Excel...";

                await _absenceExcelService.ExporterModeleAsync(
                    Salaries, Soldes, DossierSession.Instance.CurrentDossier, Annee, Mois, dialog.FileName);

                StatusMessage = "Modèle Excel généré : " + dialog.FileName;
            }
            catch (Exception ex)
            {
                StatusMessage = "Erreur lors de la génération du modèle : " + ex.Message;
            }
            finally
            {
                IsBusy = false;
                RafraichirCommandes();
            }
        }

        /// <summary>
        /// Relit un classeur Excel rempli (voir ExporterModeleAsync) et enregistre en masse les
        /// congés qu'il contient, via AbsenceService.EnregistrerEnMasseAsync — qui valide et
        /// rejette tout le lot (rien n'est écrit) si une seule ligne pose problème, y compris un
        /// chevauchement avec une absence déjà enregistrée.
        /// </summary>
        private async Task ImporterExcelAsync()
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "Fichiers Excel (*.xlsx)|*.xlsx",
                Title = "Importer les congés saisis en masse"
            };
            if (dialog.ShowDialog() != true) return;

            try
            {
                IsBusy = true;
                StatusMessage = "Lecture du fichier...";

                var resultat = await _absenceExcelService.ImporterCongesAsync(dialog.FileName, Annee, Mois, Salaries);

                if (resultat.AbsencesValides.Count == 0)
                {
                    StatusMessage = resultat.Erreurs.Count > 0
                        ? string.Format("Import annulé : {0} erreur(s), aucune ligne valide.", resultat.Erreurs.Count)
                        : "Aucun congé à importer (toutes les lignes étaient vides ou à 0 jour).";
                    if (resultat.Erreurs.Count > 0)
                    {
                        MessageBox.Show(string.Join(Environment.NewLine, resultat.Erreurs),
                            "Erreurs d'import", MessageBoxButton.OK, MessageBoxImage.Warning);
                    }
                    return;
                }

                int enregistres = await _absenceService.EnregistrerEnMasseAsync(resultat.AbsencesValides);
                await ChargerAsync();

                StatusMessage = string.Format(
                    "{0} congé(s) importé(s) sur {1} ligne(s) lue(s){2}.",
                    enregistres, resultat.TotalLignesLues,
                    resultat.LignesIgnorees > 0
                        ? string.Format(", {0} ligne(s) ignorée(s)", resultat.LignesIgnorees)
                        : "");

                if (resultat.Erreurs.Count > 0)
                {
                    MessageBox.Show(string.Join(Environment.NewLine, resultat.Erreurs),
                        "Lignes ignorées à l'import", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (InvalidOperationException ex)
            {
                // EnregistrerEnMasseAsync valide tout le lot avant d'écrire quoi que ce soit :
                // rien n'est enregistré tant que cette erreur n'est pas corrigée dans le fichier.
                StatusMessage = "Import interrompu : " + ex.Message;
                MessageBox.Show(ex.Message, "Import interrompu — rien n'a été enregistré",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            catch (Exception ex)
            {
                StatusMessage = "Erreur lors de l'import : " + ex.Message;
            }
            finally
            {
                IsBusy = false;
                RafraichirCommandes();
            }
        }

        // ── Attribution groupée de congés ───────────────────────────────────────

        private void ToggleAttributionGroupee()
        {
            AttributionGroupeeOuverte = !AttributionGroupeeOuverte;
            if (!AttributionGroupeeOuverte) return;

            // Valeurs par défaut à l'ouverture : la période affichée, rien de saisi.
            var debut = new DateTime(Annee, Mois, 1);
            DateDebutGroupee = debut;
            DateFinGroupee = debut;
            JoursGroupee = 0m;
            CommentaireGroupe = string.Empty;
            if (string.IsNullOrEmpty(CategorieFiltreSelectionnee) || !CategoriesFiltre.Contains(CategorieFiltreSelectionnee))
                CategorieFiltreSelectionnee = TousLesSalariesLibelle;
            else
                OnPropertyChanged(nameof(NombreSalariesCibles));
        }

        /// <summary>Même règle de décompte que CalculerJours, appliquée à la période du groupe.</summary>
        private void CalculerJoursGroupee()
        {
            if (!DateDebutGroupee.HasValue || !DateFinGroupee.HasValue) return;

            JoursGroupee = Absence.JoursEntre(DateDebutGroupee.Value, DateFinGroupee.Value, true);

            StatusMessage = string.Format(
                "{0:0.##} jour(s) ouvrable(s) calculé(s) pour le groupe, dimanches exclus.", JoursGroupee);
        }

        /// <summary>
        /// Attribue le même congé à tous les salariés actifs de la cible choisie, en un seul
        /// lot via AbsenceService.EnregistrerEnMasseAsync — qui valide tout avant d'écrire quoi
        /// que ce soit : un seul salarié en anomalie (chevauchement, période close...) annule
        /// l'attribution pour tout le groupe plutôt que d'en enregistrer une partie.
        /// </summary>
        private async Task AppliquerAttributionGroupeeAsync()
        {
            if (!DateDebutGroupee.HasValue || !DateFinGroupee.HasValue)
            {
                StatusMessage = "Renseignez les dates « Du » et « Au » du groupe.";
                return;
            }
            if (JoursGroupee <= 0m)
            {
                StatusMessage = "Le nombre de jours ouvrables doit être supérieur à 0.";
                return;
            }

            var cibles = SalariesCibles().ToList();
            if (cibles.Count == 0)
            {
                StatusMessage = "Aucun salarié actif ne correspond au groupe sélectionné.";
                return;
            }

            try
            {
                IsBusy = true;
                StatusMessage = string.Format("Attribution en cours pour {0} salarié(s)...", cibles.Count);

                string commentaire = string.IsNullOrWhiteSpace(CommentaireGroupe)
                    ? string.Format("Congé collectif attribué le {0:dd/MM/yyyy}", DateTime.Now)
                    : CommentaireGroupe;

                var absences = cibles.Select(s => new Absence
                {
                    EmployeeId = s.EmployeeId,
                    NomSalarie = string.Format("{0} {1}", s.LastName, s.FirstName).Trim(),
                    Type = TypeAbsence.CongePaye,
                    DateDebut = DateDebutGroupee.Value,
                    DateFin = DateFinGroupee.Value,
                    NombreJours = JoursGroupee,
                    NombreHeures = 0m,
                    Commentaire = commentaire,
                    DateSaisie = DateTime.Now
                }).ToList();

                int enregistres = await _absenceService.EnregistrerEnMasseAsync(absences);
                await ChargerAsync();

                AttributionGroupeeOuverte = false;
                StatusMessage = string.Format(
                    "{0} congé(s) attribué(s) à {1}.",
                    enregistres,
                    CategorieFiltreSelectionnee == TousLesSalariesLibelle
                        ? "tout l'effectif actif"
                        : "la catégorie « " + CategorieFiltreSelectionnee + " »");
            }
            catch (InvalidOperationException ex)
            {
                // Comme pour l'import Excel : rien n'a été écrit tant que cette erreur n'est
                // pas résolue (salarié en anomalie, chevauchement, ou période close).
                StatusMessage = "Attribution interrompue : " + ex.Message;
                MessageBox.Show(ex.Message, "Attribution interrompue — rien n'a été enregistré",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            catch (Exception ex)
            {
                StatusMessage = "Erreur lors de l'attribution : " + ex.Message;
            }
            finally
            {
                IsBusy = false;
                RafraichirCommandes();
            }
        }
    }
}
