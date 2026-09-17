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
    /// <summary>Un type de prêt proposé dans le sélecteur, avec son libellé.</summary>
    public class TypePretOption
    {
        public TypePretOption(TypePret valeur)
        {
            Valeur = valeur;
        }

        public TypePret Valeur { get; private set; }

        public string Libelle
        {
            get { return Pret.Libelle(Valeur); }
        }

        /// <summary>Ce que le type change concrètement, affiché en regard du choix.</summary>
        public string Precision
        {
            get
            {
                switch (Valeur)
                {
                    case TypePret.AvanceSurSalaire: return "somme versée d'avance";
                    case TypePret.Acompte: return "fraction du salaire du mois";
                    default: return "remboursé sur plusieurs mois";
                }
            }
        }

        /// <summary>
        /// Liste construite UNE fois. Un getter qui fabriquerait de nouvelles instances à
        /// chaque appel renverrait une option n'appartenant pas à la liste affichée, et le
        /// sélecteur ne montrerait aucune sélection.
        /// </summary>
        private static readonly List<TypePretOption> _tous =
            Enum.GetValues(typeof(TypePret))
                .Cast<TypePret>()
                .Select(t => new TypePretOption(t))
                .ToList();

        public static List<TypePretOption> Tous
        {
            get { return _tous; }
        }
    }

    /// <summary>
    /// Écran « Prêts et avances » (module PAI-05).
    ///
    /// À gauche, l'état des créances : qui doit quoi, combien a déjà été retenu, ce qui
    /// reste dû. À droite, l'échéancier du prêt sélectionné, où se lit d'un coup d'œil ce
    /// qui a été retenu et ce qui reste à retenir.
    ///
    /// La retenue du mois n'est pas saisie : elle vient de l'échéancier, et le bulletin la
    /// porte automatiquement. C'est précisément ce qui manquait — jusqu'ici, un
    /// remboursement était une retenue à ressaisir tous les mois, avec l'oubli d'un mois
    /// et la retenue de trop pour seules issues.
    /// </summary>
    public class PretViewModel : BaseViewModel
    {
        private readonly PretService _pretService;
        private readonly DataService _dataService;

        private List<Pret> _prets = new List<Pret>();

        public ObservableCollection<SituationPret> Situations { get; } = new ObservableCollection<SituationPret>();
        public ObservableCollection<EcheancePret> Echeancier { get; } = new ObservableCollection<EcheancePret>();
        public ObservableCollection<Employee> Salaries { get; } = new ObservableCollection<Employee>();
        public ObservableCollection<string> Avertissements { get; } = new ObservableCollection<string>();

        /// <summary>Vue filtrée de Situations (recherche texte + filtres nature/statut) pour la grille.</summary>
        public ICollectionView SituationsView { get; }

        public PretViewModel(PretService pretService, DataService dataService)
        {
            if (pretService == null) throw new ArgumentNullException("pretService");
            if (dataService == null) throw new ArgumentNullException("dataService");

            _pretService = pretService;
            _dataService = dataService;

            Title = "Prêts et avances";

            var reference = DateTime.Today;
            _annee = reference.Year;
            _moisSelectionne = MoisOption.Tous.FirstOrDefault(m => m.Numero == reference.Month);

            SituationsView = CollectionViewSource.GetDefaultView(Situations);
            SituationsView.Filter = FiltrerSituation;

            ChargerCommand = new RelayCommand(async () => await ChargerAsync(), () => !IsBusy);
            NouveauCommand = new RelayCommand(Nouveau, () => !IsBusy);
            EnregistrerCommand = new RelayCommand(
                async () => await EnregistrerAsync(),
                () => !IsBusy && PretEnCours != null);
            AnnulerSaisieCommand = new RelayCommand(AnnulerSaisie, () => PretEnCours != null);
            SupprimerCommand = new RelayCommand(
                async () => await SupprimerAsync(),
                () => !IsBusy && PretSelectionne != null);
            SuspendreCommand = new RelayCommand(
                async () => await SuspendreAsync(),
                () => !IsBusy && PretSelectionne != null && PretSelectionne.Statut == StatutPret.EnCours);
            ReprendreCommand = new RelayCommand(
                async () => await ReprendreAsync(),
                () => !IsBusy && PretSelectionne != null && PretSelectionne.Statut == StatutPret.Suspendu);
            AnnulerPretCommand = new RelayCommand(
                async () => await AnnulerPretAsync(),
                () => !IsBusy && PretSelectionne != null && PretSelectionne.Statut != StatutPret.Annule);
            CalculerEcheanceCommand = new RelayCommand(CalculerEcheance, () => PretEnCours != null);
            ClearSearchCommand = new RelayCommand(() => SearchText = string.Empty);
            ResetFiltersCommand = new RelayCommand(() =>
            {
                SearchText = string.Empty;
                SelectedTypeFilter = null;
                SelectedStatutFilter = null;
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
        /// risque pour une saisie en cours : ChargerAsync ne touche pas PretEnCours.
        /// </summary>
        protected override void OnIsActiveChanged()
        {
            if (IsActive)
                _ = ChargerAsync();
        }

        // ── Période consultée ────────────────────────────────────────────────

        public IEnumerable<MoisOption> MoisDisponibles
        {
            get { return MoisOption.Tous; }
        }

        public IEnumerable<int> AnneesDisponibles
        {
            get { return Enumerable.Range(DateTime.Today.Year - 5, 7).ToList(); }
        }

        public IEnumerable<TypePretOption> TypesPret
        {
            get { return TypePretOption.Tous; }
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
            get { return Pret.LibellePeriode(Annee, Mois); }
        }

        // ── Totaux de l'écran ────────────────────────────────────────────────

        public int NombrePrets
        {
            get { return Situations.Count; }
        }

        public int NombrePretsActifs
        {
            get { return Situations.Count(s => !s.EstSolde && s.Statut != StatutPret.Annule); }
        }

        /// <summary>Somme retenue sur les bulletins de la période affichée.</summary>
        public decimal TotalRetenueDuMois
        {
            get { return Situations.Sum(s => s.RetenueDuMois); }
        }

        /// <summary>Créance totale de l'entreprise sur son personnel.</summary>
        public decimal TotalRestantDu
        {
            get { return Situations.Where(s => s.Statut != StatutPret.Annule).Sum(s => s.SoldeRestantDu); }
        }

        public bool ADesAvertissements
        {
            get { return Avertissements.Count > 0; }
        }

        // ── Sélection et édition ─────────────────────────────────────────────

        private SituationPret _situationSelectionnee;
        public SituationPret SituationSelectionnee
        {
            get { return _situationSelectionnee; }
            set
            {
                if (SetProperty(ref _situationSelectionnee, value))
                {
                    PretSelectionne = value != null
                        ? _prets.FirstOrDefault(p => p.Id == value.PretId)
                        : null;
                }
            }
        }

        private Pret _pretSelectionne;
        /// <summary>Prêt de la ligne sélectionnée, tel qu'il est en base.</summary>
        public Pret PretSelectionne
        {
            get { return _pretSelectionne; }
            private set
            {
                // ChargerAsync() réaffecte cette propriété après chaque rechargement (y
                // compris le rechargement automatique à la réactivation de l'écran, voir
                // OnIsActiveChanged) avec une NOUVELLE instance représentant le MÊME prêt,
                // pour rafraîchir les totaux affichés. Cloner PretEnCours à chaque fois
                // écraserait alors une saisie en cours dès qu'on revient sur cet écran —
                // signalé le 16 septembre 2026 (voir ETAT-DEVELOPPEMENT.md item 22). On ne
                // clone donc que lorsque le prêt sélectionné a réellement changé.
                bool memePret = _pretSelectionne != null && value != null && _pretSelectionne.Id == value.Id;

                if (SetProperty(ref _pretSelectionne, value))
                {
                    // La ligne s'ouvre à l'édition sur une COPIE : tant que
                    // l'enregistrement n'est pas demandé, la liste affichée reste celle de
                    // la base, et une modification abandonnée ne laisse aucune trace.
                    if (!memePret)
                        PretEnCours = value != null ? value.Clone() : null;

                    SalarieSelectionne = value != null
                        ? Salaries.FirstOrDefault(s => string.Equals(
                              s.EmployeeId, value.EmployeeId, StringComparison.OrdinalIgnoreCase))
                        : null;

                    OnPropertyChanged(nameof(MotifLibelle));
                    OnPropertyChanged(nameof(ADesMotifs));
                    _ = ChargerEcheancierAsync();
                    RafraichirCommandes();
                }
            }
        }

        private Pret _pretEnCours;
        /// <summary>Prêt en cours de saisie ou de modification. Null quand rien n'est ouvert.</summary>
        public Pret PretEnCours
        {
            get { return _pretEnCours; }
            set
            {
                if (SetProperty(ref _pretEnCours, value))
                {
                    OnPropertyChanged(nameof(EditionOuverte));
                    OnPropertyChanged(nameof(TypeSelectionne));
                    OnPropertyChanged(nameof(MoisPremiereEcheanceSelectionne));
                    RafraichirCommandes();
                }
            }
        }

        public bool EditionOuverte
        {
            get { return PretEnCours != null; }
        }

        /// <summary>
        /// Type du prêt en cours, exposé par son option pour que le sélecteur affiche un
        /// libellé plutôt qu'une valeur d'énumération.
        /// </summary>
        public TypePretOption TypeSelectionne
        {
            get
            {
                if (PretEnCours == null) return null;
                return TypePretOption.Tous.FirstOrDefault(t => t.Valeur == PretEnCours.Type);
            }
            set
            {
                if (PretEnCours == null || value == null) return;

                PretEnCours.Type = value.Valeur;

                // Un acompte se récupère sur le mois même : proposer douze échéances
                // n'aurait pas de sens et serait corrigé à la main chaque fois.
                if (value.Valeur == TypePret.Acompte && PretEnCours.Id <= 0)
                {
                    PretEnCours.NombreEcheances = 1;
                    PretEnCours.MontantEcheance = PretEnCours.MontantAccorde;
                }

                OnPropertyChanged();
            }
        }

        /// <summary>Mois de la première échéance, exposé par son option pour le sélecteur.</summary>
        public MoisOption MoisPremiereEcheanceSelectionne
        {
            get
            {
                if (PretEnCours == null) return null;
                return MoisOption.Tous.FirstOrDefault(m => m.Numero == PretEnCours.MoisPremiereEcheance);
            }
            set
            {
                if (PretEnCours == null || value == null) return;
                PretEnCours.MoisPremiereEcheance = value.Numero;
                OnPropertyChanged();
            }
        }

        private Employee _salarieSelectionne;
        public Employee SalarieSelectionne
        {
            get { return _salarieSelectionne; }
            set
            {
                if (SetProperty(ref _salarieSelectionne, value) && PretEnCours != null && value != null)
                {
                    PretEnCours.EmployeeId = value.EmployeeId;
                    PretEnCours.NomSalarie = string.Format("{0} {1}", value.LastName, value.FirstName).Trim();
                }
            }
        }

        /// <summary>Motif de suspension ou d'annulation du prêt sélectionné.</summary>
        public string MotifLibelle
        {
            get { return PretSelectionne != null ? PretSelectionne.Motif : null; }
        }

        public bool ADesMotifs
        {
            get { return !string.IsNullOrWhiteSpace(MotifLibelle); }
        }

        /// <summary>
        /// Motif saisi avant une suspension ou une annulation. Il est conservé sur le
        /// prêt : un remboursement interrompu sans raison écrite est un litige en attente.
        /// </summary>
        private string _motifSaisi;
        public string MotifSaisi
        {
            get { return _motifSaisi; }
            set { SetProperty(ref _motifSaisi, value); }
        }

        // ── Recherche et filtres sur la liste des prêts ─────────────────────────

        private string _searchText;
        public string SearchText
        {
            get { return _searchText; }
            set
            {
                if (SetProperty(ref _searchText, value))
                {
                    SituationsView?.Refresh();
                    OnPropertyChanged(nameof(HasSearchText));
                    OnPropertyChanged(nameof(HasActiveFilters));
                    OnPropertyChanged(nameof(NombrePretsAffiches));
                }
            }
        }

        public bool HasSearchText
        {
            get { return !string.IsNullOrWhiteSpace(SearchText); }
        }

        private TypePret? _selectedTypeFilter;
        public TypePret? SelectedTypeFilter
        {
            get { return _selectedTypeFilter; }
            set
            {
                if (SetProperty(ref _selectedTypeFilter, value))
                {
                    SituationsView?.Refresh();
                    OnPropertyChanged(nameof(HasActiveFilters));
                    OnPropertyChanged(nameof(NombrePretsAffiches));
                }
            }
        }

        public List<KeyValuePair<TypePret?, string>> TypesPretFiltreOptions
        {
            get
            {
                var options = new List<KeyValuePair<TypePret?, string>>
                {
                    new KeyValuePair<TypePret?, string>(null, "Tous les types")
                };
                options.AddRange(TypePretOption.Tous
                    .Select(t => new KeyValuePair<TypePret?, string>(t.Valeur, t.Libelle)));
                return options;
            }
        }

        private string _selectedStatutFilter;
        public string SelectedStatutFilter
        {
            get { return _selectedStatutFilter; }
            set
            {
                if (SetProperty(ref _selectedStatutFilter, value))
                {
                    SituationsView?.Refresh();
                    OnPropertyChanged(nameof(HasActiveFilters));
                    OnPropertyChanged(nameof(NombrePretsAffiches));
                }
            }
        }

        public List<string> StatutsPretFiltreOptions { get; } = new List<string>
        {
            "Tous les statuts",
            "En cours uniquement",
            "Soldés uniquement",
            "Suspendus uniquement",
            "Annulés uniquement"
        };

        public bool HasActiveFilters
        {
            get
            {
                return HasSearchText
                    || SelectedTypeFilter.HasValue
                    || (!string.IsNullOrWhiteSpace(SelectedStatutFilter) && SelectedStatutFilter != "Tous les statuts");
            }
        }

        public int NombrePretsAffiches
        {
            get { return SituationsView != null ? SituationsView.Cast<SituationPret>().Count() : Situations.Count; }
        }

        private bool FiltrerSituation(object obj)
        {
            var situation = obj as SituationPret;
            if (situation == null) return false;

            if (SelectedTypeFilter.HasValue && situation.Type != SelectedTypeFilter.Value)
                return false;

            if (!string.IsNullOrWhiteSpace(SelectedStatutFilter) && SelectedStatutFilter != "Tous les statuts")
            {
                switch (SelectedStatutFilter)
                {
                    case "En cours uniquement":
                        if (situation.EstSolde || situation.Statut != StatutPret.EnCours) return false;
                        break;
                    case "Soldés uniquement":
                        if (!situation.EstSolde) return false;
                        break;
                    case "Suspendus uniquement":
                        if (situation.Statut != StatutPret.Suspendu) return false;
                        break;
                    case "Annulés uniquement":
                        if (situation.Statut != StatutPret.Annule) return false;
                        break;
                }
            }

            if (!string.IsNullOrWhiteSpace(SearchText))
            {
                string q = SearchText.Trim();
                bool match = (situation.NomSalarie != null && situation.NomSalarie.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0)
                          || (situation.EmployeeId != null && situation.EmployeeId.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0)
                          || (situation.Reference != null && situation.Reference.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0)
                          || (situation.TypeLibelle != null && situation.TypeLibelle.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0);
                if (!match) return false;
            }

            return true;
        }

        // ── Recherche rapide du salarié dans le formulaire d'édition ────────────

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
        public ICommand SuspendreCommand { get; }
        public ICommand ReprendreCommand { get; }
        public ICommand AnnulerPretCommand { get; }
        public ICommand CalculerEcheanceCommand { get; }
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
            (SuspendreCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (ReprendreCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (AnnulerPretCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (CalculerEcheanceCommand as RelayCommand)?.RaiseCanExecuteChanged();
        }

        // ── Chargement ───────────────────────────────────────────────────────

        public async Task ChargerAsync()
        {
            try
            {
                IsBusy = true;
                StatusMessage = "Chargement des prêts...";

                Salaries.Clear();
                foreach (var salarie in await _dataService.LoadEmployeesAsync())
                    Salaries.Add(salarie);
                OnPropertyChanged(nameof(SalariesFiltres));

                _prets = await _pretService.ChargerAsync();

                int idRetenu = PretSelectionne != null ? PretSelectionne.Id : 0;

                var situations = await _pretService.SituationsAsync(Annee, Mois);

                Situations.Clear();
                foreach (var situation in situations)
                    Situations.Add(situation);

                Avertissements.Clear();
                foreach (var message in PretService.Avertissements(situations, Salaries))
                    Avertissements.Add(message);

                // La sélection est rendue après le rechargement : sans cela, enregistrer
                // renverrait le gestionnaire en haut de la liste à chaque modification.
                SituationSelectionnee = idRetenu > 0
                    ? Situations.FirstOrDefault(s => s.PretId == idRetenu)
                    : null;

                NotifierTotaux();

                StatusMessage = Situations.Count == 0
                    ? "Aucun prêt enregistré dans ce dossier."
                    : string.Format(
                        "{0} prêt(s), dont {1} en cours — {2:N0} retenus sur {3}.",
                        Situations.Count, NombrePretsActifs, TotalRetenueDuMois, PeriodeLibelle);
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

        private void NotifierTotaux()
        {
            OnPropertyChanged(nameof(NombrePrets));
            OnPropertyChanged(nameof(NombrePretsActifs));
            OnPropertyChanged(nameof(NombrePretsAffiches));
            OnPropertyChanged(nameof(TotalRetenueDuMois));
            OnPropertyChanged(nameof(TotalRestantDu));
            OnPropertyChanged(nameof(ADesAvertissements));
            OnPropertyChanged(nameof(PeriodeLibelle));
        }

        private async Task ChargerEcheancierAsync()
        {
            Echeancier.Clear();

            var pret = PretSelectionne;
            if (pret == null || pret.Id <= 0) return;

            try
            {
                foreach (var echeance in await _pretService.ChargerEcheancesAsync(pret.Id))
                    Echeancier.Add(echeance);
            }
            catch (Exception ex)
            {
                StatusMessage = "Échéancier illisible : " + ex.Message;
            }
        }

        // ── Édition ──────────────────────────────────────────────────────────

        private void Nouveau()
        {
            var debut = new DateTime(Annee, Mois, 1);

            PretSelectionne = null;
            SituationSelectionnee = null;

            PretEnCours = new Pret
            {
                Type = TypePret.Pret,
                DateOctroi = DateTime.Today,
                NombreEcheances = 1,
                AnneePremiereEcheance = debut.Year,
                MoisPremiereEcheance = debut.Month,
                Statut = StatutPret.EnCours
            };

            SalarieSelectionne = null;
            MotifSaisi = null;

            StatusMessage = "Nouveau prêt : choisissez le salarié, le montant et l'échéancier.";
        }

        private void AnnulerSaisie()
        {
            PretEnCours = PretSelectionne != null ? PretSelectionne.Clone() : null;
            StatusMessage = "Saisie abandonnée.";
        }

        /// <summary>
        /// Déduit l'échéance mensuelle du montant et du nombre d'échéances. Proposé et non
        /// imposé : une entreprise peut vouloir une première retenue plus forte, ou un
        /// montant rond que le salarié retient plus facilement.
        /// </summary>
        private void CalculerEcheance()
        {
            var pret = PretEnCours;
            if (pret == null) return;

            if (pret.MontantAccorde <= 0m || pret.NombreEcheances <= 0)
            {
                StatusMessage = "Indiquez d'abord le montant accordé et le nombre d'échéances.";
                return;
            }

            pret.MontantEcheance = Math.Round(
                pret.MontantAccorde / pret.NombreEcheances, 0, MidpointRounding.AwayFromZero);

            StatusMessage = string.Format(
                "{0:N0} sur {1} mois : {2:N0} par mois. La dernière échéance sera ajustée au " +
                "franc près pour ne retenir ni plus ni moins que la somme avancée.",
                pret.MontantAccorde, pret.NombreEcheances, pret.MontantEcheance);
        }

        private async Task EnregistrerAsync()
        {
            var pret = PretEnCours;
            if (pret == null) return;

            var anomalie = PretService.Anomalie(pret);
            if (anomalie != null)
            {
                StatusMessage = anomalie;
                return;
            }

            try
            {
                IsBusy = true;
                StatusMessage = "Enregistrement...";

                await _pretService.EnregistrerAsync(pret);

                int id = pret.Id;
                PretEnCours = null;
                _pretSelectionne = null;

                await ChargerAsync();

                SituationSelectionnee = Situations.FirstOrDefault(s => s.PretId == id);

                StatusMessage = "Prêt enregistré, échéancier mis à jour.";
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
            var pret = PretSelectionne;
            if (pret == null || pret.Id <= 0) return;

            try
            {
                IsBusy = true;
                StatusMessage = "Suppression...";

                await _pretService.SupprimerAsync(pret);

                PretEnCours = null;
                _pretSelectionne = null;
                await ChargerAsync();

                StatusMessage = string.Format("Prêt de {0} supprimé.", pret.NomSalarie);
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

        private async Task SuspendreAsync()
        {
            var pret = PretSelectionne;
            if (pret == null || pret.Id <= 0) return;

            try
            {
                IsBusy = true;
                StatusMessage = "Suspension du remboursement...";

                await _pretService.SuspendreAsync(pret, MotifSaisi);

                MotifSaisi = null;
                await ChargerAsync();

                StatusMessage = string.Format(
                    "Remboursement de {0} suspendu : aucune retenue ne sera opérée tant qu'il " +
                    "n'aura pas été repris.", pret.NomSalarie);
            }
            catch (Exception ex)
            {
                StatusMessage = "Erreur lors de la suspension : " + ex.Message;
            }
            finally
            {
                IsBusy = false;
                RafraichirCommandes();
            }
        }

        private async Task ReprendreAsync()
        {
            var pret = PretSelectionne;
            if (pret == null || pret.Id <= 0) return;

            try
            {
                IsBusy = true;
                StatusMessage = "Reprise du remboursement...";

                await _pretService.ReprendreAsync(pret, Annee, Mois);

                await ChargerAsync();

                StatusMessage = string.Format(
                    "Remboursement de {0} repris à partir de {1}. Le reliquat a été réétalé avec " +
                    "la même échéance mensuelle.", pret.NomSalarie, PeriodeLibelle);
            }
            catch (InvalidOperationException ex)
            {
                StatusMessage = ex.Message;
            }
            catch (Exception ex)
            {
                StatusMessage = "Erreur lors de la reprise : " + ex.Message;
            }
            finally
            {
                IsBusy = false;
                RafraichirCommandes();
            }
        }

        private async Task AnnulerPretAsync()
        {
            var pret = PretSelectionne;
            if (pret == null || pret.Id <= 0) return;

            try
            {
                IsBusy = true;
                StatusMessage = "Annulation du prêt...";

                await _pretService.AnnulerAsync(pret, MotifSaisi);

                MotifSaisi = null;
                await ChargerAsync();

                StatusMessage = string.Format(
                    "Prêt de {0} annulé : les retenues déjà opérées sont conservées, celles à " +
                    "venir ne le seront pas.", pret.NomSalarie);
            }
            catch (Exception ex)
            {
                StatusMessage = "Erreur lors de l'annulation : " + ex.Message;
            }
            finally
            {
                IsBusy = false;
                RafraichirCommandes();
            }
        }
    }
}
