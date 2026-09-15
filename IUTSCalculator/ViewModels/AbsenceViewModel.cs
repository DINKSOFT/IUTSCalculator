using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Data;
using System.Windows.Input;
using Microsoft.Win32;
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
    /// ViewModel de l'écran « Congés et absences » (module PAI-04).
    /// </summary>
    public class AbsenceViewModel : BaseViewModel
    {
        private readonly AbsenceService _absenceService;
        private readonly DataService _dataService;
        private readonly AbsenceExcelService _absenceExcelService;

        private List<Absence> _toutesLesAbsences = new List<Absence>();

        public ObservableCollection<Absence> Absences { get; } = new ObservableCollection<Absence>();
        public ObservableCollection<SoldeConges> Soldes { get; } = new ObservableCollection<SoldeConges>();
        public ObservableCollection<Employee> Salaries { get; } = new ObservableCollection<Employee>();
        public ObservableCollection<Employee> SalariesFiltres { get; } = new ObservableCollection<Employee>();
        public ObservableCollection<string> Avertissements { get; } = new ObservableCollection<string>();

        public ICollectionView AbsencesView { get; }
        public ICollectionView SoldesView { get; }

        private string _searchText;
        public string SearchText
        {
            get { return _searchText; }
            set
            {
                if (SetProperty(ref _searchText, value))
                {
                    AbsencesView?.Refresh();
                    SoldesView?.Refresh();
                    OnPropertyChanged(nameof(HasSearchText));
                    OnPropertyChanged(nameof(HasActiveFilters));
                    OnPropertyChanged(nameof(NombreAbsencesAffichees));
                }
            }
        }

        public bool HasSearchText => !string.IsNullOrWhiteSpace(SearchText);

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

        public bool HasActiveFilters => HasSearchText || SelectedTypeFilter.HasValue || SelectedRemunereeFilter.HasValue;

        public List<KeyValuePair<TypeAbsence?, string>> TypesAbsenceFiltreOptions { get; } =
            new List<KeyValuePair<TypeAbsence?, string>> { new KeyValuePair<TypeAbsence?, string>(null, "Tous les types") };

        public List<KeyValuePair<bool?, string>> RemunereeFiltreOptions { get; } =
            new List<KeyValuePair<bool?, string>>
            {
                new KeyValuePair<bool?, string>(null, "Toutes"),
                new KeyValuePair<bool?, string>(true, "Payées"),
                new KeyValuePair<bool?, string>(false, "Non payées")
            };

        public int NombreAbsencesAffichees => AbsencesView?.Cast<Absence>().Count() ?? Absences.Count;

        public ICommand ClearSearchCommand { get; }
        public ICommand ResetFiltersCommand { get; }

        private string _filtreSalarie;
        public string FiltreSalarie
        {
            get { return _filtreSalarie; }
            set
            {
                if (SetProperty(ref _filtreSalarie, value))
                {
                    AppliquerFiltreSalaries();
                    OnPropertyChanged(nameof(HasFiltreSalarie));
                }
            }
        }

        public bool HasFiltreSalarie => !string.IsNullOrWhiteSpace(FiltreSalarie);
        public ICommand ClearFiltreSalarieCommand { get; }

        public void AppliquerFiltreSalaries()
        {
            SalariesFiltres.Clear();
            var q = string.IsNullOrWhiteSpace(FiltreSalarie)
                ? (IEnumerable<Employee>)Salaries
                : Salaries.Where(e =>
                    (!string.IsNullOrEmpty(e.LastName) && e.LastName.IndexOf(FiltreSalarie, StringComparison.OrdinalIgnoreCase) >= 0) ||
                    (!string.IsNullOrEmpty(e.FirstName) && e.FirstName.IndexOf(FiltreSalarie, StringComparison.OrdinalIgnoreCase) >= 0) ||
                    (!string.IsNullOrEmpty(e.EmployeeId) && e.EmployeeId.IndexOf(FiltreSalarie, StringComparison.OrdinalIgnoreCase) >= 0) ||
                    (!string.IsNullOrEmpty(e.Fonction) && e.Fonction.IndexOf(FiltreSalarie, StringComparison.OrdinalIgnoreCase) >= 0));

            foreach (var e in q)
                SalariesFiltres.Add(e);

            if (SalariesFiltres.Count == 1 && SalarieSelectionne == null)
            {
                SalarieSelectionne = SalariesFiltres[0];
            }
        }

        public AbsenceViewModel(AbsenceService absenceService, DataService dataService)
            : this(absenceService, dataService, new AbsenceExcelService())
        {
        }

        public AbsenceViewModel(AbsenceService absenceService, DataService dataService, AbsenceExcelService absenceExcelService)
        {
            if (absenceService == null) throw new ArgumentNullException("absenceService");
            if (dataService == null) throw new ArgumentNullException("dataService");

            _absenceService = absenceService;
            _dataService = dataService;
            _absenceExcelService = absenceExcelService ?? new AbsenceExcelService();

            Title = "Congés et absences";

            foreach (TypeAbsence t in Enum.GetValues(typeof(TypeAbsence)))
            {
                TypesAbsenceFiltreOptions.Add(new KeyValuePair<TypeAbsence?, string>(t, Absence.Libelle(t)));
            }

            AbsencesView = CollectionViewSource.GetDefaultView(Absences);
            AbsencesView.Filter = FiltrerAbsence;

            SoldesView = CollectionViewSource.GetDefaultView(Soldes);
            SoldesView.Filter = FiltrerSolde;

            ClearSearchCommand = new RelayCommand(() => SearchText = string.Empty);
            ResetFiltersCommand = new RelayCommand(() =>
            {
                SearchText = string.Empty;
                SelectedTypeFilter = null;
                SelectedRemunereeFilter = null;
            });

            ClearFiltreSalarieCommand = new RelayCommand(() => FiltreSalarie = string.Empty);

            var reference = DateTime.Today;
            _annee = reference.Year;
            _moisSelectionne = MoisOption.Tous.FirstOrDefault(m => m.Numero == reference.Month);

            _dateDebutGroupee = new DateTime(_annee, Mois, 1);
            _dateFinGroupee = _dateDebutGroupee.AddMonths(1).AddDays(-1);
            _joursGroupee = 1m;

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

            // Commandes de traitement en masse et Excel
            ExporterModeleCommand = new RelayCommand(async () => await ExporterModeleAsync(), () => !IsBusy && Salaries.Count > 0);
            ImporterExcelCommand = new RelayCommand(async () => await ImporterExcelAsync(), () => !IsBusy && PeriodeOuverte);
            ToggleAttributionGroupeeCommand = new RelayCommand(ToggleAttributionGroupee, () => PeriodeOuverte);
            AppliquerAttributionGroupeeCommand = new RelayCommand(async () => await AppliquerAttributionGroupeeAsync(), () => !IsBusy && PeriodeOuverte && NombreSalariesCibles > 0);
            CalculerJoursGroupeeCommand = new RelayCommand(CalculerJoursGroupee);

            _ = ChargerAsync();
        }

        private bool FiltrerAbsence(object obj)
        {
            var item = obj as Absence;
            if (item == null) return false;

            if (SelectedTypeFilter.HasValue && item.Type != SelectedTypeFilter.Value)
                return false;

            if (SelectedRemunereeFilter.HasValue && item.EstRemuneree != SelectedRemunereeFilter.Value)
                return false;

            if (!string.IsNullOrWhiteSpace(SearchText))
            {
                string term = SearchText.Trim();
                bool match = (item.NomSalarie != null && item.NomSalarie.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0)
                          || (item.EmployeeId != null && item.EmployeeId.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0)
                          || (item.Commentaire != null && item.Commentaire.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0)
                          || (item.TypeLibelle != null && item.TypeLibelle.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0);
                if (!match) return false;
            }

            return true;
        }

        private bool FiltrerSolde(object obj)
        {
            var item = obj as SoldeConges;
            if (item == null) return false;

            if (!string.IsNullOrWhiteSpace(SearchText))
            {
                string term = SearchText.Trim();
                bool match = (item.NomSalarie != null && item.NomSalarie.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0)
                          || (item.EmployeeId != null && item.EmployeeId.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0);
                if (!match) return false;
            }

            return true;
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
                    if (value != null && !string.IsNullOrEmpty(value.EmployeeId))
                    {
                        SalarieSelectionne = Salaries.FirstOrDefault(s => s.EmployeeId == value.EmployeeId);
                    }
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

        // ── Propriétés pour l'attribution groupée ──────────────────────────────

        public ObservableCollection<string> CategoriesFiltre { get; } = new ObservableCollection<string>();

        private string _categorieFiltreSelectionnee = "Toutes les catégories";
        public string CategorieFiltreSelectionnee
        {
            get { return _categorieFiltreSelectionnee; }
            set
            {
                if (SetProperty(ref _categorieFiltreSelectionnee, value))
                {
                    OnPropertyChanged(nameof(NombreSalariesCibles));
                    (AppliquerAttributionGroupeeCommand as RelayCommand)?.RaiseCanExecuteChanged();
                }
            }
        }

        private bool _attributionGroupeeOuverte;
        public bool AttributionGroupeeOuverte
        {
            get { return _attributionGroupeeOuverte; }
            set
            {
                if (SetProperty(ref _attributionGroupeeOuverte, value))
                {
                    if (value)
                    {
                        AbsenceEnCours = null;
                        DateDebutGroupee = new DateTime(Annee, Mois, 1);
                        DateFinGroupee = DateDebutGroupee.AddMonths(1).AddDays(-1);
                        CalculerJoursGroupee();
                    }
                    RafraichirCommandes();
                }
            }
        }

        private DateTime _dateDebutGroupee;
        public DateTime DateDebutGroupee
        {
            get { return _dateDebutGroupee; }
            set
            {
                if (SetProperty(ref _dateDebutGroupee, value))
                {
                    CalculerJoursGroupee();
                }
            }
        }

        private DateTime _dateFinGroupee;
        public DateTime DateFinGroupee
        {
            get { return _dateFinGroupee; }
            set
            {
                if (SetProperty(ref _dateFinGroupee, value))
                {
                    CalculerJoursGroupee();
                }
            }
        }

        private decimal _joursGroupee = 1m;
        public decimal JoursGroupee
        {
            get { return _joursGroupee; }
            set { SetProperty(ref _joursGroupee, value); }
        }

        private string _commentaireGroupe = "Congé annuel collectif";
        public string CommentaireGroupe
        {
            get { return _commentaireGroupe; }
            set { SetProperty(ref _commentaireGroupe, value); }
        }

        public int NombreSalariesCibles
        {
            get
            {
                if (string.IsNullOrWhiteSpace(CategorieFiltreSelectionnee) || CategorieFiltreSelectionnee == "Toutes les catégories")
                    return Salaries.Count;
                return Salaries.Count(s => string.Equals(s.Category, CategorieFiltreSelectionnee, StringComparison.OrdinalIgnoreCase));
            }
        }

        // ── Commandes ────────────────────────────────────────────────────────

        public ICommand ChargerCommand { get; }
        public ICommand NouvelleCommand { get; }
        public ICommand EnregistrerCommand { get; }
        public ICommand SupprimerCommand { get; }
        public ICommand AnnulerCommand { get; }
        public ICommand CalculerJoursCommand { get; }
        public ICommand ExporterModeleCommand { get; }
        public ICommand ImporterExcelCommand { get; }
        public ICommand ToggleAttributionGroupeeCommand { get; }
        public ICommand AppliquerAttributionGroupeeCommand { get; }
        public ICommand CalculerJoursGroupeeCommand { get; }

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
                AppliquerFiltreSalaries();

                CategoriesFiltre.Clear();
                CategoriesFiltre.Add("Toutes les catégories");
                var cats = Salaries
                    .Select(s => s.Category)
                    .Where(c => !string.IsNullOrWhiteSpace(c))
                    .Distinct()
                    .OrderBy(c => c);
                foreach (var c in cats) CategoriesFiltre.Add(c);
                if (string.IsNullOrEmpty(CategorieFiltreSelectionnee) || !CategoriesFiltre.Contains(CategorieFiltreSelectionnee))
                {
                    CategorieFiltreSelectionnee = "Toutes les catégories";
                }
                OnPropertyChanged(nameof(NombreSalariesCibles));

                _toutesLesAbsences = await _absenceService.ChargerToutAsync();

                var periode = await _absenceService.ChargerPeriodeAsync(Annee, Mois);
                Absences.Clear();
                foreach (var absence in periode)
                    Absences.Add(absence);

                PeriodeOuverte = await PeriodeOuverteAsync();

                RafraichirSoldes();

                AbsencesView?.Refresh();
                SoldesView?.Refresh();
                OnPropertyChanged(nameof(NombreAbsencesAffichees));
                OnPropertyChanged(nameof(NombreAbsences));
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

            FiltreSalarie = string.Empty;
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

        // ── Saisie groupée et Import/Export Excel ──────────────────────────────

        private void ToggleAttributionGroupee()
        {
            AttributionGroupeeOuverte = !AttributionGroupeeOuverte;
            if (AttributionGroupeeOuverte)
            {
                AbsenceEnCours = null;
                DateDebutGroupee = new DateTime(Annee, Mois, 1);
                DateFinGroupee = DateDebutGroupee.AddMonths(1).AddDays(-1);
                CalculerJoursGroupee();
            }
        }

        private void CalculerJoursGroupee()
        {
            JoursGroupee = Absence.JoursEntre(DateDebutGroupee, DateFinGroupee, true);
        }

        private async Task AppliquerAttributionGroupeeAsync()
        {
            if (!PeriodeOuverte) return;

            var cibles = (string.IsNullOrWhiteSpace(CategorieFiltreSelectionnee) || CategorieFiltreSelectionnee == "Toutes les catégories")
                ? Salaries.ToList()
                : Salaries.Where(s => string.Equals(s.Category, CategorieFiltreSelectionnee, StringComparison.OrdinalIgnoreCase)).ToList();

            if (cibles.Count == 0)
            {
                StatusMessage = "Aucun salarié ne correspond aux critères sélectionnés.";
                return;
            }

            if (JoursGroupee <= 0m)
            {
                StatusMessage = "Veuillez spécifier un nombre de jours supérieur à zéro.";
                return;
            }

            try
            {
                IsBusy = true;
                StatusMessage = string.Format("Attribution en cours pour {0} salarié(s)...", cibles.Count);

                var absences = cibles.Select(s => new Absence
                {
                    EmployeeId = s.EmployeeId,
                    NomSalarie = string.Format("{0} {1}", s.LastName, s.FirstName).Trim(),
                    Type = TypeAbsence.CongePaye,
                    DateDebut = DateDebutGroupee,
                    DateFin = DateFinGroupee,
                    NombreJours = JoursGroupee,
                    NombreHeures = 0m,
                    Commentaire = string.IsNullOrWhiteSpace(CommentaireGroupe)
                        ? string.Format("Congé collectif ({0:D2}/{1})", Mois, Annee)
                        : CommentaireGroupe,
                    DateSaisie = DateTime.Now
                }).ToList();

                int count = await _absenceService.EnregistrerEnMasseAsync(absences);

                AttributionGroupeeOuverte = false;
                await ChargerAsync();

                StatusMessage = string.Format(
                    "Attribution groupée réussie : {0} congé(s) enregistré(s) pour la période de {1}.",
                    count, PeriodeLibelle);
            }
            catch (Exception ex)
            {
                StatusMessage = "Erreur lors de l'attribution groupée : " + ex.Message;
            }
            finally
            {
                IsBusy = false;
                RafraichirCommandes();
            }
        }

        private async Task ExporterModeleAsync()
        {
            if (Salaries.Count == 0)
            {
                StatusMessage = "Aucun salarié dans le dossier.";
                return;
            }

            var dialog = new SaveFileDialog
            {
                Filter = "Classeur Excel (*.xlsx)|*.xlsx",
                FileName = string.Format("Modele_Conges_{0}_{1:D2}.xlsx", Annee, Mois),
                Title = "Exporter le modèle de saisie des congés"
            };

            if (dialog.ShowDialog() == true)
            {
                try
                {
                    IsBusy = true;
                    StatusMessage = "Exportation du modèle Excel en cours...";

                    var dossier = DossierSession.Instance.CurrentDossier;
                    await _absenceExcelService.ExporterModeleAsync(Salaries, Soldes, dossier, Annee, Mois, dialog.FileName);

                    StatusMessage = string.Format("Modèle Excel exporté avec succès : {0}", Path.GetFileName(dialog.FileName));
                }
                catch (Exception ex)
                {
                    StatusMessage = "Erreur lors de l'export du modèle : " + ex.Message;
                }
                finally
                {
                    IsBusy = false;
                }
            }
        }

        private async Task ImporterExcelAsync()
        {
            if (!PeriodeOuverte)
            {
                StatusMessage = "La période est clôturée : import impossible.";
                return;
            }

            var dialog = new OpenFileDialog
            {
                Filter = "Classeur Excel (*.xlsx)|*.xlsx|Tous les fichiers (*.*)|*.*",
                Title = "Importer les congés depuis un fichier Excel"
            };

            if (dialog.ShowDialog() == true)
            {
                try
                {
                    IsBusy = true;
                    StatusMessage = "Lecture et validation du fichier Excel...";

                    var resultat = await _absenceExcelService.ImporterCongesAsync(dialog.FileName, Annee, Mois, Salaries);

                    if (resultat.Erreurs.Count > 0 && resultat.CongesImportes == 0)
                    {
                        StatusMessage = "Échec de l'import : " + string.Join(" | ", resultat.Erreurs.Take(3));
                        return;
                    }

                    if (resultat.AbsencesValides.Count > 0)
                    {
                        StatusMessage = string.Format("Enregistrement de {0} congés en base...", resultat.AbsencesValides.Count);
                        int count = await _absenceService.EnregistrerEnMasseAsync(resultat.AbsencesValides);

                        await ChargerAsync();

                        string msg = string.Format("Import terminé : {0} congé(s) importé(s) avec succès pour {1}.", count, PeriodeLibelle);
                        if (resultat.LignesIgnorees > 0)
                        {
                            msg += string.Format(" ({0} ligne(s) vide(s) ignorée(s))", resultat.LignesIgnorees);
                        }
                        if (resultat.Erreurs.Count > 0)
                        {
                            msg += string.Format(" — {0} avertissement(s) : {1}", resultat.Erreurs.Count, resultat.Erreurs.First());
                        }
                        StatusMessage = msg;
                    }
                    else
                    {
                        StatusMessage = string.Format("Aucun jour de congé à importer dans ce fichier ({0} lignes ignorées).", resultat.LignesIgnorees);
                    }
                }
                catch (Exception ex)
                {
                    StatusMessage = "Erreur lors de l'import Excel : " + ex.Message;
                }
                finally
                {
                    IsBusy = false;
                    RafraichirCommandes();
                }
            }
        }
    }
}
