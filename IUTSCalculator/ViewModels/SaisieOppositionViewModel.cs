using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
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
    /// <summary>
    /// ViewModel pour la gestion des saisies-arrêts, cessions de salaire et oppositions (module PAI-06).
    /// Gère le contentieux des retenues, la hiérarchie des privilèges, le barème progressif et les bordereaux judiciaires.
    /// </summary>
    public class SaisieOppositionViewModel : BaseViewModel
    {
        private readonly SaisieOppositionService _saisieService;
        private readonly SaisieOppositionExportService _exportService;
        private readonly DataService _dataService;

        public ObservableCollection<SaisieOpposition> Saisies { get; } = new ObservableCollection<SaisieOpposition>();
        public ObservableCollection<Employee> Salaries { get; } = new ObservableCollection<Employee>();
        public ObservableCollection<Employee> SalariesFiltres { get; } = new ObservableCollection<Employee>();
        public ObservableCollection<RetenueSaisieMensuelle> HistoriqueRetenues { get; } = new ObservableCollection<RetenueSaisieMensuelle>();
        public ObservableCollection<DetailTrancheQuotite> SimulateurTranches { get; } = new ObservableCollection<DetailTrancheQuotite>();
        public ObservableCollection<LigneRepartitionSaisie> SimulateurRepartitions { get; } = new ObservableCollection<LigneRepartitionSaisie>();

        public ICollectionView SaisiesView { get; }

        #region Filtres et Recherche

        private string _searchText;
        public string SearchText
        {
            get => _searchText;
            set
            {
                if (SetProperty(ref _searchText, value))
                {
                    SaisiesView?.Refresh();
                    OnPropertyChanged(nameof(HasSearchText));
                    OnPropertyChanged(nameof(HasActiveFilters));
                    OnPropertyChanged(nameof(NombreSaisiesAffichees));
                }
            }
        }

        public bool HasSearchText => !string.IsNullOrWhiteSpace(SearchText);

        private TypeSaisieOpposition? _selectedTypeFilter;
        public TypeSaisieOpposition? SelectedTypeFilter
        {
            get => _selectedTypeFilter;
            set
            {
                if (SetProperty(ref _selectedTypeFilter, value))
                {
                    SaisiesView?.Refresh();
                    OnPropertyChanged(nameof(HasActiveFilters));
                    OnPropertyChanged(nameof(NombreSaisiesAffichees));
                }
            }
        }

        private StatutSaisieOpposition? _selectedStatutFilter;
        public StatutSaisieOpposition? SelectedStatutFilter
        {
            get => _selectedStatutFilter;
            set
            {
                if (SetProperty(ref _selectedStatutFilter, value))
                {
                    SaisiesView?.Refresh();
                    OnPropertyChanged(nameof(HasActiveFilters));
                    OnPropertyChanged(nameof(NombreSaisiesAffichees));
                }
            }
        }

        public bool HasActiveFilters => HasSearchText || SelectedTypeFilter.HasValue || SelectedStatutFilter.HasValue;

        public List<KeyValuePair<TypeSaisieOpposition?, string>> TypesSaisieFiltreOptions { get; } =
            new List<KeyValuePair<TypeSaisieOpposition?, string>>
            {
                new KeyValuePair<TypeSaisieOpposition?, string>(null, "Tous types de créances"),
                new KeyValuePair<TypeSaisieOpposition?, string>(TypeSaisieOpposition.PensionAlimentaire, "Pension alimentaire (super-privilège)"),
                new KeyValuePair<TypeSaisieOpposition?, string>(TypeSaisieOpposition.AvisATiersDetenteur, "Avis à Tiers Détenteur (ATD)"),
                new KeyValuePair<TypeSaisieOpposition?, string>(TypeSaisieOpposition.SaisieArretJudiciaire, "Saisie-arrêt judiciaire"),
                new KeyValuePair<TypeSaisieOpposition?, string>(TypeSaisieOpposition.CessionVolontaire, "Cession volontaire"),
                new KeyValuePair<TypeSaisieOpposition?, string>(TypeSaisieOpposition.AutreOpposition, "Autre opposition")
            };

        public List<KeyValuePair<StatutSaisieOpposition?, string>> StatutsSaisieFiltreOptions { get; } =
            new List<KeyValuePair<StatutSaisieOpposition?, string>>
            {
                new KeyValuePair<StatutSaisieOpposition?, string>(null, "Tous les statuts"),
                new KeyValuePair<StatutSaisieOpposition?, string>(StatutSaisieOpposition.EnCours, "En cours"),
                new KeyValuePair<StatutSaisieOpposition?, string>(StatutSaisieOpposition.Suspendue, "Suspendue"),
                new KeyValuePair<StatutSaisieOpposition?, string>(StatutSaisieOpposition.Soldee, "Soldée"),
                new KeyValuePair<StatutSaisieOpposition?, string>(StatutSaisieOpposition.Mainlevee, "Mainlevée")
            };

        public int NombreSaisiesAffichees => SaisiesView?.Cast<SaisieOpposition>().Count() ?? Saisies.Count;

        private string _filtreSalarie;
        public string FiltreSalarie
        {
            get => _filtreSalarie;
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

        public void AppliquerFiltreSalaries()
        {
            SalariesFiltres.Clear();
            var q = string.IsNullOrWhiteSpace(FiltreSalarie)
                ? (IEnumerable<Employee>)Salaries
                : Salaries.Where(e =>
                    (!string.IsNullOrEmpty(e.LastName) && e.LastName.IndexOf(FiltreSalarie, StringComparison.OrdinalIgnoreCase) >= 0) ||
                    (!string.IsNullOrEmpty(e.FirstName) && e.FirstName.IndexOf(FiltreSalarie, StringComparison.OrdinalIgnoreCase) >= 0) ||
                    (!string.IsNullOrEmpty(e.EmployeeId) && e.EmployeeId.IndexOf(FiltreSalarie, StringComparison.OrdinalIgnoreCase) >= 0));

            foreach (var e in q)
                SalariesFiltres.Add(e);
        }

        #endregion

        #region Sélection & Formulaire

        private SaisieOpposition _selectedSaisie;
        public SaisieOpposition SelectedSaisie
        {
            get => _selectedSaisie;
            set
            {
                if (SetProperty(ref _selectedSaisie, value))
                {
                    OnSelectedSaisieChanged();
                }
            }
        }

        private bool _isEditing;
        public bool IsEditing
        {
            get => _isEditing;
            set => SetProperty(ref _isEditing, value);
        }

        private bool _isNew;
        public bool IsNew
        {
            get => _isNew;
            set => SetProperty(ref _isNew, value);
        }

        // Propriétés du formulaire
        private Employee _formEmployee;
        public Employee FormEmployee
        {
            get => _formEmployee;
            set
            {
                if (SetProperty(ref _formEmployee, value))
                {
                    if (_formEmployee != null && SimulateurNetReference == 0m)
                    {
                        SimulateurNetReference = _formEmployee.BaseSalary;
                    }
                    RecalculerSimulateur();
                }
            }
        }

        private TypeSaisieOpposition _formTypeSaisie = TypeSaisieOpposition.SaisieArretJudiciaire;
        public TypeSaisieOpposition FormTypeSaisie
        {
            get => _formTypeSaisie;
            set
            {
                if (SetProperty(ref _formTypeSaisie, value))
                {
                    OnPropertyChanged(nameof(EstPensionAlimentaire));
                    RecalculerSimulateur();
                }
            }
        }

        public bool EstPensionAlimentaire => FormTypeSaisie == TypeSaisieOpposition.PensionAlimentaire;

        private string _formCreancier;
        public string FormCreancier
        {
            get => _formCreancier;
            set => SetProperty(ref _formCreancier, value);
        }

        private string _formBeneficiaire;
        public string FormBeneficiaire
        {
            get => _formBeneficiaire;
            set => SetProperty(ref _formBeneficiaire, value);
        }

        private string _formNumeroDossier;
        public string FormNumeroDossier
        {
            get => _formNumeroDossier;
            set => SetProperty(ref _formNumeroDossier, value);
        }

        private decimal _formMontantTotalDu;
        public decimal FormMontantTotalDu
        {
            get => _formMontantTotalDu;
            set => SetProperty(ref _formMontantTotalDu, value);
        }

        private decimal _formRetenueMensuelleFixee;
        public decimal FormRetenueMensuelleFixee
        {
            get => _formRetenueMensuelleFixee;
            set
            {
                if (SetProperty(ref _formRetenueMensuelleFixee, value))
                {
                    RecalculerSimulateur();
                }
            }
        }

        private DateTime? _formDateDebut;
        public DateTime? FormDateDebut
        {
            get => _formDateDebut;
            set => SetProperty(ref _formDateDebut, value);
        }

        public bool HasSelectedSaisie => SelectedSaisie != null;
        public string BasculerStatutLibelle => SelectedSaisie?.Statut == StatutSaisieOpposition.EnCours ? "Suspendre" : "Réactiver";

        private DateTime? _formDateFin;
        public DateTime? FormDateFin
        {
            get => _formDateFin;
            set => SetProperty(ref _formDateFin, value);
        }

        private DateTime? _formDateNotification;
        public DateTime? FormDateNotification
        {
            get => _formDateNotification;
            set => SetProperty(ref _formDateNotification, value);
        }

        private string _formTribunalOuAutorite;
        public string FormTribunalOuAutorite
        {
            get => _formTribunalOuAutorite;
            set => SetProperty(ref _formTribunalOuAutorite, value);
        }

        private string _formReferenceJugement;
        public string FormReferenceJugement
        {
            get => _formReferenceJugement;
            set => SetProperty(ref _formReferenceJugement, value);
        }

        private StatutSaisieOpposition _formStatut = StatutSaisieOpposition.EnCours;
        public StatutSaisieOpposition FormStatut
        {
            get => _formStatut;
            set => SetProperty(ref _formStatut, value);
        }

        private int _formPrioriteOrdre;
        public int FormPrioriteOrdre
        {
            get => _formPrioriteOrdre;
            set
            {
                if (SetProperty(ref _formPrioriteOrdre, value))
                    RecalculerSimulateur();
            }
        }

        private string _formCommentaire;
        public string FormCommentaire
        {
            get => _formCommentaire;
            set => SetProperty(ref _formCommentaire, value);
        }

        #endregion

        #region Simulateur & Diagnostic Légal

        private decimal _simulateurNetReference = 250000m;
        public decimal SimulateurNetReference
        {
            get => _simulateurNetReference;
            set
            {
                if (SetProperty(ref _simulateurNetReference, value))
                    RecalculerSimulateur();
            }
        }

        private decimal _simulateurQuotiteLegale;
        public decimal SimulateurQuotiteLegale
        {
            get => _simulateurQuotiteLegale;
            set => SetProperty(ref _simulateurQuotiteLegale, value);
        }

        private decimal _simulateurFractionInsaisissable;
        public decimal SimulateurFractionInsaisissable
        {
            get => _simulateurFractionInsaisissable;
            set => SetProperty(ref _simulateurFractionInsaisissable, value);
        }

        private decimal _simulateurRetenuePension;
        public decimal SimulateurRetenuePension
        {
            get => _simulateurRetenuePension;
            set => SetProperty(ref _simulateurRetenuePension, value);
        }

        private decimal _simulateurRetenueOrdinaire;
        public decimal SimulateurRetenueOrdinaire
        {
            get => _simulateurRetenueOrdinaire;
            set => SetProperty(ref _simulateurRetenueOrdinaire, value);
        }

        private decimal _simulateurTotalRetenu;
        public decimal SimulateurTotalRetenu
        {
            get => _simulateurTotalRetenu;
            set => SetProperty(ref _simulateurTotalRetenu, value);
        }

        private decimal _simulateurNetRestant;
        public decimal SimulateurNetRestant
        {
            get => _simulateurNetRestant;
            set => SetProperty(ref _simulateurNetRestant, value);
        }

        #endregion

        #region Commandes

        public ICommand ClearSearchCommand { get; }
        public ICommand ResetFiltersCommand { get; }
        public ICommand ClearFiltreSalarieCommand { get; }
        public ICommand NouveauCommand { get; }
        public ICommand EditerCommand { get; }
        public ICommand EnregistrerCommand { get; }
        public ICommand AnnulerCommand { get; }
        public ICommand SupprimerCommand { get; }
        public ICommand PrononcerMainleveeCommand { get; }
        public ICommand BasculerStatutCommand { get; }
        public ICommand ExporterRegistreCommand { get; }
        public ICommand ExporterBordereauCommand { get; }
        public ICommand ActualiserCommand { get; }

        #endregion

        public SaisieOppositionViewModel(
            SaisieOppositionService saisieService,
            SaisieOppositionExportService exportService,
            DataService dataService)
        {
            _saisieService = saisieService ?? throw new ArgumentNullException(nameof(saisieService));
            _exportService = exportService ?? throw new ArgumentNullException(nameof(exportService));
            _dataService = dataService ?? throw new ArgumentNullException(nameof(dataService));

            Title = "Saisies-arrêts et cessions sur salaire";

            SaisiesView = CollectionViewSource.GetDefaultView(Saisies);
            SaisiesView.Filter = FiltrerSaisie;

            ClearSearchCommand = new RelayCommand(() => SearchText = string.Empty);
            ResetFiltersCommand = new RelayCommand(ReinitialiserFiltres);
            ClearFiltreSalarieCommand = new RelayCommand(() => FiltreSalarie = string.Empty);

            NouveauCommand = new RelayCommand(Nouveau);
            EditerCommand = new RelayCommand(Editer, () => SelectedSaisie != null && !IsEditing);
            EnregistrerCommand = new RelayCommand(async () => await EnregistrerAsync(), () => IsEditing);
            AnnulerCommand = new RelayCommand(Annuler, () => IsEditing);
            SupprimerCommand = new RelayCommand(async () => await SupprimerAsync(), () => SelectedSaisie != null && !IsEditing);
            PrononcerMainleveeCommand = new RelayCommand(async () => await PrononcerMainleveeAsync(), () => SelectedSaisie != null && SelectedSaisie.Statut != StatutSaisieOpposition.Mainlevee);
            BasculerStatutCommand = new RelayCommand(async () => await BasculerStatutAsync(), () => SelectedSaisie != null);
            ExporterRegistreCommand = new RelayCommand(async () => await ExporterRegistreAsync());
            ExporterBordereauCommand = new RelayCommand(async () => await ExporterBordereauAsync());
            ActualiserCommand = new RelayCommand(async () => await ChargerDonneesAsync());

            _ = InitialiserAsync();
        }

        private async Task InitialiserAsync()
        {
            await ChargerSalariesAsync();
            await ChargerDonneesAsync();
            RecalculerSimulateur();
        }

        private async Task ChargerSalariesAsync()
        {
            try
            {
                var list = await _dataService.LoadEmployeesAsync();
                Salaries.Clear();
                foreach (var emp in list.OrderBy(e => e.LastName).ThenBy(e => e.FirstName))
                    Salaries.Add(emp);
                AppliquerFiltreSalaries();
            }
            catch (Exception ex)
            {
                StatusMessage = "Erreur chargement des salariés : " + ex.Message;
            }
        }

        public async Task ChargerDonneesAsync()
        {
            IsBusy = true;
            try
            {
                var liste = await _saisieService.ChargerToutAsync();
                Saisies.Clear();
                foreach (var s in liste)
                    Saisies.Add(s);

                SaisiesView.Refresh();
                OnPropertyChanged(nameof(NombreSaisiesAffichees));

                if (SelectedSaisie != null)
                {
                    SelectedSaisie = Saisies.FirstOrDefault(s => s.Id == SelectedSaisie.Id);
                }
                else if (Saisies.Count > 0)
                {
                    SelectedSaisie = Saisies[0];
                }
            }
            catch (Exception ex)
            {
                StatusMessage = "Erreur chargement des saisies : " + ex.Message;
            }
            finally
            {
                IsBusy = false;
            }
        }

        private bool FiltrerSaisie(object obj)
        {
            if (!(obj is SaisieOpposition s)) return false;

            if (SelectedTypeFilter.HasValue && s.TypeSaisie != SelectedTypeFilter.Value)
                return false;

            if (SelectedStatutFilter.HasValue && s.Statut != SelectedStatutFilter.Value)
                return false;

            if (!string.IsNullOrWhiteSpace(SearchText))
            {
                string st = SearchText.Trim();
                bool matchEmp = !string.IsNullOrEmpty(s.NomSalarie) && s.NomSalarie.IndexOf(st, StringComparison.OrdinalIgnoreCase) >= 0;
                bool matchId = !string.IsNullOrEmpty(s.EmployeeId) && s.EmployeeId.IndexOf(st, StringComparison.OrdinalIgnoreCase) >= 0;
                bool matchCreancier = !string.IsNullOrEmpty(s.Creancier) && s.Creancier.IndexOf(st, StringComparison.OrdinalIgnoreCase) >= 0;
                bool matchDossier = !string.IsNullOrEmpty(s.NumeroDossier) && s.NumeroDossier.IndexOf(st, StringComparison.OrdinalIgnoreCase) >= 0;
                bool matchTribunal = !string.IsNullOrEmpty(s.TribunalOuAutorite) && s.TribunalOuAutorite.IndexOf(st, StringComparison.OrdinalIgnoreCase) >= 0;

                if (!matchEmp && !matchId && !matchCreancier && !matchDossier && !matchTribunal)
                    return false;
            }

            return true;
        }

        private void ReinitialiserFiltres()
        {
            SearchText = string.Empty;
            SelectedTypeFilter = null;
            SelectedStatutFilter = null;
        }

        private async void OnSelectedSaisieChanged()
        {
            // ChargerDonneesAsync() réaffecte SelectedSaisie après chaque rechargement (y
            // compris le rechargement automatique à la réactivation de l'écran) pour
            // retrouver la même ligne, ce qui invoque ce gestionnaire. Si une édition est en
            // cours (IsEditing), réécraser les champs Form* depuis la base effacerait alors
            // la saisie de l'utilisateur en plein milieu d'une modification — signalé le 16
            // septembre 2026 (voir ETAT-DEVELOPPEMENT.md item 22). Annuler() reste le seul
            // chemin qui doit resynchroniser Form* pendant une édition, et il le fait après
            // avoir remis IsEditing à false, donc sans passer par ce garde-fou.
            if (!IsEditing)
            {
                if (SelectedSaisie != null)
                {
                    FormEmployee = Salaries.FirstOrDefault(e => e.EmployeeId == SelectedSaisie.EmployeeId);
                    FormTypeSaisie = SelectedSaisie.TypeSaisie;
                    FormCreancier = SelectedSaisie.Creancier;
                    FormBeneficiaire = SelectedSaisie.Beneficiaire;
                    FormNumeroDossier = SelectedSaisie.NumeroDossier;
                    FormMontantTotalDu = SelectedSaisie.MontantTotalDu;
                    FormRetenueMensuelleFixee = SelectedSaisie.RetenueMensuelleFixee;
                    FormDateDebut = SelectedSaisie.DateDebut;
                    FormDateFin = SelectedSaisie.DateFin;
                    FormDateNotification = SelectedSaisie.DateNotification;
                    FormTribunalOuAutorite = SelectedSaisie.TribunalOuAutorite;
                    FormReferenceJugement = SelectedSaisie.ReferenceJugement;
                    FormStatut = SelectedSaisie.Statut;
                    FormPrioriteOrdre = SelectedSaisie.PrioriteOrdre;
                    FormCommentaire = SelectedSaisie.Commentaire;

                    await ChargerHistoriqueRetenuesAsync(SelectedSaisie.Id);
                }
                else
                {
                    FormEmployee = null;
                    FormCreancier = string.Empty;
                    FormBeneficiaire = string.Empty;
                    FormNumeroDossier = string.Empty;
                    FormMontantTotalDu = 0m;
                    FormRetenueMensuelleFixee = 0m;
                    FormDateDebut = null;
                    FormDateFin = null;
                    FormDateNotification = null;
                    FormTribunalOuAutorite = string.Empty;
                    FormReferenceJugement = string.Empty;
                    FormCommentaire = string.Empty;
                    HistoriqueRetenues.Clear();
                }
            }

            OnPropertyChanged(nameof(BasculerStatutLibelle));
            OnPropertyChanged(nameof(HasSelectedSaisie));
            RecalculerSimulateur();
            (EditerCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (SupprimerCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (PrononcerMainleveeCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (BasculerStatutCommand as RelayCommand)?.RaiseCanExecuteChanged();
        }

        private async Task ChargerHistoriqueRetenuesAsync(int saisieId)
        {
            try
            {
                var h = await _saisieService.ChargerHistoriqueRetenuesParSaisieAsync(saisieId);
                HistoriqueRetenues.Clear();
                foreach (var item in h)
                    HistoriqueRetenues.Add(item);
            }
            catch (Exception ex)
            {
                StatusMessage = "Erreur historique : " + ex.Message;
            }
        }

        private void Nouveau()
        {
            IsNew = true;
            IsEditing = true;

            FormEmployee = Salaries.FirstOrDefault();
            FormTypeSaisie = TypeSaisieOpposition.SaisieArretJudiciaire;
            FormCreancier = string.Empty;
            FormBeneficiaire = string.Empty;
            FormNumeroDossier = string.Empty;
            FormMontantTotalDu = 0m;
            FormRetenueMensuelleFixee = 25000m;
            FormDateDebut = DateTime.Today;
            FormDateFin = null;
            FormDateNotification = DateTime.Today;
            FormTribunalOuAutorite = "Tribunal du Travail de Ouagadougou";
            FormReferenceJugement = string.Empty;
            FormStatut = StatutSaisieOpposition.EnCours;
            FormPrioriteOrdre = 0;
            FormCommentaire = string.Empty;

            RecalculerSimulateur();
            ActualiserCommandesEdition();
        }

        private void Editer()
        {
            if (SelectedSaisie == null) return;
            IsNew = false;
            IsEditing = true;
            ActualiserCommandesEdition();
        }

        private void Annuler()
        {
            IsEditing = false;
            IsNew = false;
            OnSelectedSaisieChanged();
            ActualiserCommandesEdition();
        }

        private void ActualiserCommandesEdition()
        {
            (NouveauCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (EditerCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (EnregistrerCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (AnnulerCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (SupprimerCommand as RelayCommand)?.RaiseCanExecuteChanged();
        }

        private async Task EnregistrerAsync()
        {
            if (FormEmployee == null)
            {
                StatusMessage = "Veuillez sélectionner un travailleur.";
                return;
            }
            if (string.IsNullOrWhiteSpace(FormCreancier))
            {
                StatusMessage = "La désignation du créancier est obligatoire.";
                return;
            }
            if (FormRetenueMensuelleFixee <= 0m)
            {
                StatusMessage = "La retenue mensuelle fixée doit être strictement positive.";
                return;
            }

            try
            {
                var s = IsNew ? new SaisieOpposition() : SelectedSaisie;
                s.EmployeeId = FormEmployee.EmployeeId;
                s.NomSalarie = string.Format("{0} {1}", FormEmployee.LastName, FormEmployee.FirstName).Trim();
                s.TypeSaisie = FormTypeSaisie;
                s.Creancier = FormCreancier.Trim();
                s.Beneficiaire = FormBeneficiaire?.Trim();
                s.NumeroDossier = FormNumeroDossier?.Trim();
                s.MontantTotalDu = FormMontantTotalDu;
                s.RetenueMensuelleFixee = FormRetenueMensuelleFixee;
                s.DateDebut = FormDateDebut ?? DateTime.Today;
                s.DateFin = FormDateFin;
                s.DateNotification = FormDateNotification;
                s.TribunalOuAutorite = FormTribunalOuAutorite?.Trim();
                s.ReferenceJugement = FormReferenceJugement?.Trim();
                s.Statut = FormStatut;
                s.PrioriteOrdre = FormPrioriteOrdre;
                s.Commentaire = FormCommentaire?.Trim();

                await _saisieService.EnregistrerAsync(s);

                IsEditing = false;
                IsNew = false;
                ActualiserCommandesEdition();

                await ChargerDonneesAsync();
                SelectedSaisie = Saisies.FirstOrDefault(x => x.Id == s.Id);

                StatusMessage = "Dossier de saisie/cession enregistré avec succès.";
            }
            catch (Exception ex)
            {
                StatusMessage = "Erreur d'enregistrement : " + ex.Message;
            }
        }

        private async Task SupprimerAsync()
        {
            if (SelectedSaisie == null) return;

            try
            {
                await _saisieService.SupprimerAsync(SelectedSaisie.Id);
                StatusMessage = "Dossier supprimé.";
                await ChargerDonneesAsync();
            }
            catch (Exception ex)
            {
                StatusMessage = "Erreur suppression : " + ex.Message;
            }
        }

        private async Task PrononcerMainleveeAsync()
        {
            if (SelectedSaisie == null) return;

            try
            {
                await _saisieService.PrononcerMainleveeAsync(SelectedSaisie.Id, DateTime.Today, "Acte de mainlevée notifié");
                StatusMessage = "Mainlevée prononcée pour ce dossier.";
                await ChargerDonneesAsync();
            }
            catch (Exception ex)
            {
                StatusMessage = "Erreur mainlevée : " + ex.Message;
            }
        }

        private async Task BasculerStatutAsync()
        {
            if (SelectedSaisie == null) return;

            var nouveauStatut = SelectedSaisie.Statut == StatutSaisieOpposition.EnCours
                ? StatutSaisieOpposition.Suspendue
                : StatutSaisieOpposition.EnCours;

            try
            {
                await _saisieService.ChangerStatutAsync(SelectedSaisie.Id, nouveauStatut);
                StatusMessage = "Statut mis à jour : " + nouveauStatut;
                await ChargerDonneesAsync();
            }
            catch (Exception ex)
            {
                StatusMessage = "Erreur statut : " + ex.Message;
            }
        }

        private void RecalculerSimulateur()
        {
            decimal net = SimulateurNetReference;
            var calcul = SaisieOppositionService.CalculerQuotiteSaisissable(net);

            SimulateurQuotiteLegale = calcul.QuotiteSaisissableLegale;
            SimulateurFractionInsaisissable = calcul.FractionInsaisissable;

            SimulateurTranches.Clear();
            foreach (var t in calcul.Tranches)
                SimulateurTranches.Add(t);

            // Création d'une liste de saisies pour le salarié sélectionné ou le formulaire
            var saisiesAtester = new List<SaisieOpposition>();

            string empId = FormEmployee?.EmployeeId ?? SelectedSaisie?.EmployeeId;
            if (!string.IsNullOrWhiteSpace(empId))
            {
                saisiesAtester.AddRange(Saisies.Where(s => s.EmployeeId == empId && s.EstActive));
            }

            if (IsEditing)
            {
                // Remplacer ou ajouter la saisie du formulaire
                int idForm = IsNew ? -1 : (SelectedSaisie?.Id ?? -1);
                saisiesAtester.RemoveAll(s => s.Id == idForm);
                saisiesAtester.Add(new SaisieOpposition
                {
                    Id = idForm,
                    EmployeeId = empId ?? "SIMUL",
                    TypeSaisie = FormTypeSaisie,
                    Creancier = string.IsNullOrWhiteSpace(FormCreancier) ? "Nouveau créancier (Simulation)" : FormCreancier,
                    MontantTotalDu = FormMontantTotalDu,
                    RetenueMensuelleFixee = FormRetenueMensuelleFixee,
                    PrioriteOrdre = FormPrioriteOrdre,
                    Statut = StatutSaisieOpposition.EnCours
                });
            }

            var rep = SaisieOppositionService.RepartirSaisies(net, saisiesAtester);

            SimulateurRetenuePension = rep.RetenuePensionAlimentaire;
            SimulateurRetenueOrdinaire = rep.RetenuesOrdinairesTotal;
            SimulateurTotalRetenu = rep.RetenueGlobaleEffectuee;
            SimulateurNetRestant = rep.NetRestantApresSaisies;

            SimulateurRepartitions.Clear();
            foreach (var r in rep.Repartitions)
                SimulateurRepartitions.Add(r);
        }

        private async Task ExporterRegistreAsync()
        {
            try
            {
                var dialog = new SaveFileDialog
                {
                    Filter = "Fichier Excel (*.xlsx)|*.xlsx",
                    FileName = string.Format("Registre_Saisies_Cessions_{0:yyyyMMdd}.xlsx", DateTime.Today),
                    Title = "Exporter le registre des créances"
                };

                if (dialog.ShowDialog() != true) return;

                var dossier = DossierSession.Instance.CurrentDossier;
                await _exportService.ExporterRegistreCreancesExcelAsync(Saisies, dossier, dialog.FileName);
                StatusMessage = "Registre exporté avec succès : " + dialog.FileName;
            }
            catch (Exception ex)
            {
                StatusMessage = "Erreur lors de l'export du registre : " + ex.Message;
            }
        }

        private async Task ExporterBordereauAsync()
        {
            try
            {
                var dialog = new SaveFileDialog
                {
                    Filter = "Fichier Excel (*.xlsx)|*.xlsx",
                    FileName = string.Format("Bordereau_Retenues_Saisies_{0:yyyyMM}.xlsx", DateTime.Today),
                    Title = "Exporter le bordereau mensuel des reversements"
                };

                if (dialog.ShowDialog() != true) return;

                var retenues = await _saisieService.ChargerRetenuesMoisAsync(DateTime.Today.Year, DateTime.Today.Month);
                var dossier = DossierSession.Instance.CurrentDossier;
                await _exportService.ExporterRecapitulatifMensuelExcelAsync(
                    retenues, dossier, DateTime.Today.Year, DateTime.Today.Month, dialog.FileName);
                StatusMessage = "Bordereau mensuel exporté avec succès : " + dialog.FileName;
            }
            catch (Exception ex)
            {
                StatusMessage = "Erreur lors de l'export du bordereau : " + ex.Message;
            }
        }
    }
}
