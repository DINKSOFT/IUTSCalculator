using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using IUTSCalculator.Helpers;
using IUTSCalculator.Models;
using IUTSCalculator.Services;
using Microsoft.Win32;

namespace IUTSCalculator.ViewModels
{
    /// <summary>Une case du planning : un salarié, un jour.</summary>
    public class CellulePlanning : INotifyPropertyChanged
    {
        private string _code;
        private bool _estModifie;

        public CellulePlanning(DateTime date, string code, decimal? heures, SourceJourPlanning source, bool estFerie)
        {
            Date = date.Date;
            _code = code ?? string.Empty;
            Heures = heures;
            Source = source;
            EstFerie = estFerie;
        }

        public DateTime Date { get; }
        public decimal? Heures { get; set; }
        public SourceJourPlanning Source { get; set; }
        public bool EstFerie { get; }

        /// <summary>Levé quand une saisie est refusée (code inconnu) : le ViewModel l'affiche.</summary>
        public event EventHandler<string> SaisieRefusee;

        public string Code
        {
            get { return _code; }
            set
            {
                string normalise = CodesPlanning.Normaliser(value);
                if (normalise == null)
                {
                    SaisieRefusee?.Invoke(this, value);
                    OnPropertyChanged();
                    return;
                }

                if (normalise == _code) return;
                _code = normalise;
                Heures = null;
                Source = SourceJourPlanning.Saisie;
                EstModifie = true;
                OnPropertyChanged();
                OnPropertyChanged(nameof(Fond));
                OnPropertyChanged(nameof(Infobulle));
            }
        }

        public bool EstModifie
        {
            get { return _estModifie; }
            set { if (_estModifie != value) { _estModifie = value; OnPropertyChanged(); } }
        }

        /// <summary>Couleur de fond (« #FFF4C2 »), convertie en pinceau par WPF.</summary>
        public string Fond
        {
            get
            {
                if (!string.IsNullOrEmpty(_code)) return GestionTempsExcelService.CouleurCode(_code);
                if (EstFerie) return "#FFE9D6";
                return Date.DayOfWeek == DayOfWeek.Sunday ? "#F3F4F2" : "#FFFFFF";
            }
        }

        public string Infobulle
        {
            get
            {
                var fr = CultureInfo.GetCultureInfo("fr-FR");
                string texte = string.Format(fr, "{0:dddd dd/MM/yyyy} — {1}", Date, CodesPlanning.Libelle(_code));
                if (Heures.HasValue) texte += string.Format(fr, " — {0:0.##} h pointées", Heures.Value);
                if (EstFerie) texte += " — jour férié";
                switch (Source)
                {
                    case SourceJourPlanning.Cycle: texte += " (cycle)"; break;
                    case SourceJourPlanning.Pointage: texte += " (pointage)"; break;
                    case SourceJourPlanning.Absence: texte += " (congés et absences)"; break;
                    default: if (!string.IsNullOrEmpty(_code)) texte += " (saisie)"; break;
                }
                return texte;
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string nom = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nom));
        }
    }

    /// <summary>Une ligne du planning : un salarié et ses jours de la période.</summary>
    public class LignePlanning : INotifyPropertyChanged
    {
        public LignePlanning(string employeeId, string nomComplet, EquipeTravail equipe)
        {
            EmployeeId = employeeId;
            NomComplet = nomComplet;
            EquipeId = equipe?.Id;
            EquipeCode = equipe?.Code ?? "—";
            Jours = new List<CellulePlanning>();
        }

        public string EmployeeId { get; }
        public string NomComplet { get; }
        public int? EquipeId { get; }
        public string EquipeCode { get; }

        /// <summary>Une case par jour de la période, dans l'ordre (colonnes générées par la vue).</summary>
        public List<CellulePlanning> Jours { get; }

        public int JoursTravailles
        {
            get { return Jours.Count(j => CodesPlanning.EstTravaille(j.Code)); }
        }

        public void Attacher()
        {
            foreach (var j in Jours)
                j.PropertyChanged += (s, e) =>
                {
                    if (e.PropertyName == nameof(CellulePlanning.Code))
                        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(JoursTravailles)));
                };
        }

        public event PropertyChangedEventHandler PropertyChanged;
    }

    /// <summary>Salarié proposé à l'affectation dans une équipe.</summary>
    public class SalarieAffectable : INotifyPropertyChanged
    {
        private bool _estCoche;

        public SalarieAffectable(Employee employe, string equipeCode)
        {
            Employe = employe;
            EquipeCode = equipeCode;
        }

        public Employee Employe { get; }
        public string Matricule => Employe?.EmployeeId;
        public string NomComplet => Employe?.FullName;
        public string Fonction => Employe?.Fonction;
        public string EquipeCode { get; }

        public bool EstCoche
        {
            get { return _estCoche; }
            set { if (_estCoche != value) { _estCoche = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(EstCoche))); } }
        }

        public event PropertyChangedEventHandler PropertyChanged;
    }

    /// <summary>
    /// Écran « Gestion des temps &amp; rotations » (module RH-08) : planning des équipes
    /// postées, décompte de la période de pointage, transferts vers la paie (heures
    /// supplémentaires, absences, paniers), équipes et cycles, jours fériés, barème daté
    /// des heures supplémentaires.
    /// </summary>
    public class GestionTempsViewModel : BaseViewModel
    {
        private readonly GestionTempsService _service;
        private readonly GestionTempsExcelService _excel;
        private readonly BaremeHeuresSupService _baremeService;
        private readonly DataService _dataService;

        private int _annee;
        private MoisOption _moisSelectionne;
        private PeriodePointage _periode;
        private EquipeTravail _equipeFiltre;
        private string _rechercheTexte;
        private int _ongletActif;
        private bool _chargementEnCours;

        private RecapTempsSalarie _recapSelectionne;
        private EquipeTravail _equipeSelectionnee;
        private EquipeTravail _equipeEnCours;
        private string _rechercheSalaries;

        private int _jourArrete;
        private bool _ajCompteDansSeuil;
        private DateTime _nouveauFerieDate = DateTime.Today;
        private string _nouveauFerieLibelle;
        private JourFerie _ferieSelectionne;

        private BaremeHeuresSup _baremeSelectionne;
        private BaremeHeuresSup _baremeEnCours;
        private string _codeRemplissage = CodesPlanning.Jour;
        private string _baremeApplicableLibelle;

        private List<Employee> _salaries = new List<Employee>();
        private Dictionary<string, int> _affectations = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        public static readonly EquipeTravail ToutesLesEquipes = new EquipeTravail { Id = 0, Code = "Toutes les équipes" };

        // ── Collections ──────────────────────────────────────────────────

        public ObservableCollection<EquipeTravail> Equipes { get; } = new ObservableCollection<EquipeTravail>();
        public ObservableCollection<EquipeTravail> EquipesFiltre { get; } = new ObservableCollection<EquipeTravail>();
        public ObservableCollection<LignePlanning> LignesPlanning { get; } = new ObservableCollection<LignePlanning>();
        public ObservableCollection<RecapTempsSalarie> Recaps { get; } = new ObservableCollection<RecapTempsSalarie>();
        public ObservableCollection<SalarieAffectable> Salaries { get; } = new ObservableCollection<SalarieAffectable>();
        public ObservableCollection<JourFerie> Feries { get; } = new ObservableCollection<JourFerie>();
        public ObservableCollection<BaremeHeuresSup> Baremes { get; } = new ObservableCollection<BaremeHeuresSup>();

        /// <summary>Dates de la période affichée : la vue en tire une colonne par jour.</summary>
        public List<DateTime> DatesPeriode { get; private set; } = new List<DateTime>();

        /// <summary>Levé quand les dates de la période changent : la vue reconstruit ses colonnes.</summary>
        public event EventHandler ColonnesPlanningChangees;

        public ICollectionView VuePlanning { get; }
        public ICollectionView VueSalaries { get; }

        public IEnumerable<MoisOption> MoisDisponibles => MoisOption.Tous;
        public IEnumerable<int> AnneesDisponibles => Enumerable.Range(DateTime.Today.Year - 3, 5).ToList();
        public IEnumerable<int> JoursArreteDisponibles => Enumerable.Range(0, 28).ToList();

        public IEnumerable<KeyValuePair<ModeSemaineDecompte, string>> ModesSemaine { get; } = new[]
        {
            new KeyValuePair<ModeSemaineDecompte, string>(ModeSemaineDecompte.Civile, "Semaine civile (lundi → dimanche)"),
            new KeyValuePair<ModeSemaineDecompte, string>(ModeSemaineDecompte.Rotation, "Semaine de rotation (jour de début du cycle)")
        };

        public IEnumerable<KeyValuePair<ModePanier, string>> ModesPanier { get; } = new[]
        {
            new KeyValuePair<ModePanier, string>(ModePanier.Aucun, "Pas de prime de panier"),
            new KeyValuePair<ModePanier, string>(ModePanier.MontantFixe, "Montant fixe par poste travaillé"),
            new KeyValuePair<ModePanier, string>(ModePanier.FractionTauxHoraire, "Fraction du taux horaire par poste")
        };

        public string Legende => CodesPlanning.Legende();

        /// <summary>Vrai tant qu'aucun salarié n'apparaît au planning (message d'aide sur la grille).</summary>
        public bool PlanningVide => LignesPlanning.Count == 0;

        /// <summary>Codes proposés pour remplir une plage de cases ; la clé vide vide les cases.</summary>
        public IEnumerable<KeyValuePair<string, string>> CodesRemplissage { get; } =
            new[] { new KeyValuePair<string, string>(string.Empty, "(vider les cases)") }
                .Concat(CodesPlanning.Tous.Select(c => new KeyValuePair<string, string>(c, c + " — " + CodesPlanning.Libelle(c))))
                .ToList();

        public string CodeRemplissage
        {
            get => _codeRemplissage;
            set => SetProperty(ref _codeRemplissage, value ?? string.Empty);
        }

        /// <summary>
        /// Applique un même code à plusieurs cases (sélection de la grille) : une semaine
        /// de congé, un arrêt maladie, un poste de nuit à ajouter sur toute une équipe.
        /// Les cases changent comme une saisie au clavier ; rien n'est écrit en base
        /// avant « Enregistrer ».
        /// </summary>
        public int AppliquerCode(IEnumerable<CellulePlanning> cellules, string code)
        {
            if (!PeutModifier)
            {
                StatusMessage = "Votre profil ne permet pas de modifier le planning.";
                return 0;
            }
            if (IsBusy) return 0;

            string normalise = CodesPlanning.Normaliser(code);
            if (normalise == null)
            {
                StatusMessage = string.Format("Code « {0} » inconnu. Codes possibles : {1}.", code, string.Join(", ", CodesPlanning.Tous));
                return 0;
            }

            int n = 0;
            foreach (var cellule in (cellules ?? Enumerable.Empty<CellulePlanning>()).Where(c => c != null).Distinct())
            {
                if (cellule.Code == normalise) continue;
                cellule.Code = normalise;
                n++;
            }

            StatusMessage = n == 0
                ? "Aucune case à modifier dans la sélection."
                : string.Format("{0} case(s) {1}. « Enregistrer » pour valider.", n,
                    normalise.Length == 0 ? "vidée(s)" : "passée(s) à " + normalise);
            return n;
        }

        // ── Période ──────────────────────────────────────────────────────

        public int Annee
        {
            get => _annee;
            set { if (SetProperty(ref _annee, value)) _ = ChangerPeriodeAsync(); }
        }

        public MoisOption MoisSelectionne
        {
            get => _moisSelectionne;
            set { if (SetProperty(ref _moisSelectionne, value)) _ = ChangerPeriodeAsync(); }
        }

        public PeriodePointage Periode
        {
            get => _periode;
            private set
            {
                if (SetProperty(ref _periode, value))
                    OnPropertyChanged(nameof(PeriodeLibelle));
            }
        }

        public string PeriodeLibelle => _periode == null
            ? string.Empty
            : string.Format("Période de pointage : {0} ({1} jours)", _periode.Libelle, _periode.NombreJours);

        public EquipeTravail EquipeFiltre
        {
            get => _equipeFiltre;
            set { if (SetProperty(ref _equipeFiltre, value)) _ = ChangerPeriodeAsync(); }
        }

        private int? FiltreId => _equipeFiltre != null && _equipeFiltre.Id > 0 ? _equipeFiltre.Id : (int?)null;

        public string RechercheTexte
        {
            get => _rechercheTexte;
            set { if (SetProperty(ref _rechercheTexte, value)) VuePlanning.Refresh(); }
        }

        public int OngletActif
        {
            get => _ongletActif;
            set => SetProperty(ref _ongletActif, value);
        }

        public bool PeutModifier => UserSession.Instance.CanEdit;

        // ── Récapitulatif ────────────────────────────────────────────────

        public RecapTempsSalarie RecapSelectionne
        {
            get => _recapSelectionne;
            set => SetProperty(ref _recapSelectionne, value);
        }

        public decimal TotalHeuresSup => Recaps.Sum(r => r.HeuresSup);
        public decimal TotalTranche1 => Recaps.Sum(r => r.Tranche1);
        public decimal TotalTranche2 => Recaps.Sum(r => r.Tranche2);
        public decimal TotalNuit => Recaps.Sum(r => r.Nuit);
        public decimal TotalFerie => Recaps.Sum(r => r.FerieJour + r.FerieNuit);
        public int TotalPaniers => Recaps.Sum(r => r.Paniers);
        public int TotalAbsences => Recaps.Sum(r => r.JoursAbsenceNonJustifiee);
        public bool RecapDisponible => Recaps.Count > 0;

        public string BaremeApplicableLibelle
        {
            get => _baremeApplicableLibelle;
            private set => SetProperty(ref _baremeApplicableLibelle, value);
        }

        // ── Équipes ──────────────────────────────────────────────────────

        public EquipeTravail EquipeSelectionnee
        {
            get => _equipeSelectionnee;
            set
            {
                if (SetProperty(ref _equipeSelectionnee, value))
                    EquipeEnCours = value != null ? value.Clone() : null;
            }
        }

        /// <summary>Copie éditée de l'équipe sélectionnée (rien n'est modifié avant « Enregistrer »).</summary>
        public EquipeTravail EquipeEnCours
        {
            get => _equipeEnCours;
            set
            {
                if (SetProperty(ref _equipeEnCours, value))
                {
                    OnPropertyChanged(nameof(EquipeEnregistree));
                    VueSalaries.Refresh();
                }
            }
        }

        public bool EquipeEnregistree => _equipeEnCours != null && _equipeEnCours.Id > 0;

        public string RechercheSalaries
        {
            get => _rechercheSalaries;
            set { if (SetProperty(ref _rechercheSalaries, value)) VueSalaries.Refresh(); }
        }

        // ── Paramètres ───────────────────────────────────────────────────

        public int JourArrete
        {
            get => _jourArrete;
            set { if (SetProperty(ref _jourArrete, value)) OnPropertyChanged(nameof(ApercuArrete)); }
        }

        public bool AJCompteDansSeuil
        {
            get => _ajCompteDansSeuil;
            set => SetProperty(ref _ajCompteDansSeuil, value);
        }

        public string ApercuArrete
        {
            get
            {
                var mois = _moisSelectionne != null ? _moisSelectionne.Numero : DateTime.Today.Month;
                var p = PeriodePointage.Pour(_annee > 0 ? _annee : DateTime.Today.Year, mois, _jourArrete);
                return string.Format(CultureInfo.GetCultureInfo("fr-FR"), "Paie de {0:MMMM yyyy} : pointage {1}.",
                    new DateTime(p.Annee, p.Mois, 1), p.Libelle);
            }
        }

        public DateTime NouveauFerieDate
        {
            get => _nouveauFerieDate;
            set => SetProperty(ref _nouveauFerieDate, value);
        }

        public string NouveauFerieLibelle
        {
            get => _nouveauFerieLibelle;
            set => SetProperty(ref _nouveauFerieLibelle, value);
        }

        public JourFerie FerieSelectionne
        {
            get => _ferieSelectionne;
            set => SetProperty(ref _ferieSelectionne, value);
        }

        public BaremeHeuresSup BaremeSelectionne
        {
            get => _baremeSelectionne;
            set
            {
                if (SetProperty(ref _baremeSelectionne, value))
                    BaremeEnCours = value != null ? value.Clone() : null;
            }
        }

        public BaremeHeuresSup BaremeEnCours
        {
            get => _baremeEnCours;
            set => SetProperty(ref _baremeEnCours, value);
        }

        // ── Commandes ────────────────────────────────────────────────────

        public ICommand ChargerCommand { get; }
        public ICommand GenererPlanningCommand { get; }
        public ICommand RegenererPlanningCommand { get; }
        public ICommand EnregistrerPlanningCommand { get; }
        public ICommand ImporterCommand { get; }
        public ICommand ExporterModeleCommand { get; }
        public ICommand ExporterExcelCommand { get; }
        public ICommand CalculerCommand { get; }
        public ICommand TransfererHeuresSupCommand { get; }
        public ICommand TransfererAbsencesCommand { get; }
        public ICommand TransfererPaniersCommand { get; }
        public ICommand TransfererToutCommand { get; }
        public ICommand NouvelleEquipeCommand { get; }
        public ICommand EnregistrerEquipeCommand { get; }
        public ICommand SupprimerEquipeCommand { get; }
        public ICommand AffecterCommand { get; }
        public ICommand RetirerCommand { get; }
        public ICommand EnregistrerParametresCommand { get; }
        public ICommand AjouterFerieCommand { get; }
        public ICommand SupprimerFerieCommand { get; }
        public ICommand NouveauBaremeCommand { get; }
        public ICommand EnregistrerBaremeCommand { get; }
        public ICommand SupprimerBaremeCommand { get; }

        public GestionTempsViewModel(GestionTempsService service, DataService dataService, SQLiteService sqliteService)
        {
            _service = service ?? throw new ArgumentNullException(nameof(service));
            _dataService = dataService ?? throw new ArgumentNullException(nameof(dataService));
            _baremeService = new BaremeHeuresSupService(sqliteService ?? throw new ArgumentNullException(nameof(sqliteService)));
            _excel = new GestionTempsExcelService();

            Title = "Gestion des temps & rotations";

            var aujourdHui = DateTime.Today;
            _annee = aujourdHui.Year;
            _moisSelectionne = MoisOption.Tous.FirstOrDefault(m => m.Numero == aujourdHui.Month) ?? MoisOption.Tous.First();
            _equipeFiltre = ToutesLesEquipes;

            VuePlanning = CollectionViewSource.GetDefaultView(LignesPlanning);
            LignesPlanning.CollectionChanged += (s, e) => OnPropertyChanged(nameof(PlanningVide));
            VuePlanning.Filter = FiltrerLigne;
            VueSalaries = new ListCollectionView(Salaries) { Filter = FiltrerSalarie };

            Func<bool> libre = () => !IsBusy;
            Func<bool> edition = () => !IsBusy && PeutModifier;

            ChargerCommand = new RelayCommand(async () => await ChargerToutAsync(), libre);
            GenererPlanningCommand = new RelayCommand(async () => await GenererAsync(false), edition);
            RegenererPlanningCommand = new RelayCommand(async () => await GenererAsync(true), edition);
            EnregistrerPlanningCommand = new RelayCommand(async () => await EnregistrerPlanningAsync(), edition);
            ImporterCommand = new RelayCommand(async () => await ImporterAsync(), edition);
            ExporterModeleCommand = new RelayCommand(async () => await ExporterModeleAsync(), libre);
            ExporterExcelCommand = new RelayCommand(async () => await ExporterExcelAsync(), () => !IsBusy && LignesPlanning.Count > 0);
            CalculerCommand = new RelayCommand(async () => await CalculerAsync(true), libre);
            TransfererHeuresSupCommand = new RelayCommand(async () => await TransfererAsync(true, false, false), () => edition() && RecapDisponible);
            TransfererAbsencesCommand = new RelayCommand(async () => await TransfererAsync(false, true, false), () => edition() && RecapDisponible);
            TransfererPaniersCommand = new RelayCommand(async () => await TransfererAsync(false, false, true), () => edition() && RecapDisponible);
            TransfererToutCommand = new RelayCommand(async () => await TransfererAsync(true, true, true), () => edition() && RecapDisponible);
            NouvelleEquipeCommand = new RelayCommand(NouvelleEquipe, edition);
            EnregistrerEquipeCommand = new RelayCommand(async () => await EnregistrerEquipeAsync(), () => edition() && _equipeEnCours != null);
            SupprimerEquipeCommand = new RelayCommand(async () => await SupprimerEquipeAsync(), () => edition() && EquipeEnregistree);
            AffecterCommand = new RelayCommand(async () => await AffecterAsync(), () => edition() && EquipeEnregistree);
            RetirerCommand = new RelayCommand(async () => await RetirerAsync(), edition);
            EnregistrerParametresCommand = new RelayCommand(async () => await EnregistrerParametresAsync(), edition);
            AjouterFerieCommand = new RelayCommand(async () => await AjouterFerieAsync(), edition);
            SupprimerFerieCommand = new RelayCommand(async () => await SupprimerFerieAsync(), () => edition() && _ferieSelectionne != null);
            NouveauBaremeCommand = new RelayCommand(NouveauBareme, edition);
            EnregistrerBaremeCommand = new RelayCommand(async () => await EnregistrerBaremeAsync(), () => edition() && _baremeEnCours != null);
            SupprimerBaremeCommand = new RelayCommand(async () => await SupprimerBaremeAsync(),
                () => edition() && _baremeEnCours != null && _baremeEnCours.Id > 0 && !_baremeEnCours.EstSysteme);
        }

        /// <summary>
        /// Revenir sur l'écran recharge tout : équipes, salariés et planning ont pu changer
        /// ailleurs. Le quitter avec des cases modifiées propose de les enregistrer — sans
        /// quoi elles seraient perdues au prochain rechargement.
        /// </summary>
        protected override void OnIsActiveChanged()
        {
            if (IsActive)
            {
                _ = ChargerToutAsync();
                return;
            }

            if (CellulesModifiees().Count > 0 && PeutModifier)
            {
                var reponse = MessageBox.Show(
                    "Le planning de la gestion des temps comporte des modifications non enregistrées.\n\nLes enregistrer ?",
                    "Gestion des temps", MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (reponse == MessageBoxResult.Yes)
                    _ = EnregistrerPlanningAsync();
            }
        }

        // ═══════════════════════════════════════════════════════════════════
        //  Chargement
        // ═══════════════════════════════════════════════════════════════════

        public async Task ChargerToutAsync()
        {
            if (_chargementEnCours) return;
            try
            {
                _chargementEnCours = true;
                IsBusy = true;
                StatusMessage = "Chargement de la gestion des temps…";

                await ChargerReferentielsAsync();
                await ChargerPlanningAsync();
                Recaps.Clear();
                NotifierTotaux();
            }
            catch (Exception ex)
            {
                StatusMessage = "Erreur au chargement : " + ex.Message;
            }
            finally
            {
                IsBusy = false;
                _chargementEnCours = false;
            }
        }

        private async Task ChargerReferentielsAsync()
        {
            var parametres = await _service.ChargerParametresAsync();
            _jourArrete = parametres.JourArrete;
            _ajCompteDansSeuil = parametres.AJCompteDansSeuil;
            OnPropertyChanged(nameof(JourArrete));
            OnPropertyChanged(nameof(AJCompteDansSeuil));
            OnPropertyChanged(nameof(ApercuArrete));

            var equipes = await _service.ChargerEquipesAsync();
            int? idSelection = _equipeSelectionnee?.Id;
            int filtre = _equipeFiltre?.Id ?? 0;

            Equipes.Clear();
            foreach (var e in equipes) Equipes.Add(e);

            EquipesFiltre.Clear();
            EquipesFiltre.Add(ToutesLesEquipes);
            foreach (var e in equipes.Where(x => x.Active)) EquipesFiltre.Add(e);
            _equipeFiltre = EquipesFiltre.FirstOrDefault(e => e.Id == filtre) ?? ToutesLesEquipes;
            OnPropertyChanged(nameof(EquipeFiltre));

            if (idSelection.HasValue && idSelection.Value > 0)
            {
                _equipeSelectionnee = Equipes.FirstOrDefault(e => e.Id == idSelection.Value);
                OnPropertyChanged(nameof(EquipeSelectionnee));
                // La copie en cours d'édition n'est pas écrasée si elle porte des modifications non enregistrées.
                if (_equipeEnCours == null || _equipeEnCours.Id == idSelection.Value)
                    EquipeEnCours = _equipeSelectionnee?.Clone();
            }

            _salaries = (await _dataService.LoadEmployeesAsync()).Where(s => s != null && !string.IsNullOrWhiteSpace(s.EmployeeId)).ToList();
            _affectations = await _service.ChargerAffectationsAsync();
            var codes = equipes.ToDictionary(e => e.Id, e => e.Code);

            Salaries.Clear();
            foreach (var s in _salaries.Where(x => x.EstActif).OrderBy(x => x.FullName))
            {
                int id;
                string code = _affectations.TryGetValue(s.EmployeeId, out id) && codes.ContainsKey(id) ? codes[id] : string.Empty;
                Salaries.Add(new SalarieAffectable(s, code));
            }

            Feries.Clear();
            foreach (var f in await _service.ChargerFeriesAsync()) Feries.Add(f);

            int? baremeSel = _baremeSelectionne?.Id;
            Baremes.Clear();
            foreach (var b in await _baremeService.ChargerToutAsync()) Baremes.Add(b);
            if (baremeSel.HasValue)
            {
                _baremeSelectionne = Baremes.FirstOrDefault(b => b.Id == baremeSel.Value);
                OnPropertyChanged(nameof(BaremeSelectionne));
            }

            await MettreAJourBaremeApplicableAsync();
        }

        private async Task MettreAJourBaremeApplicableAsync()
        {
            if (_moisSelectionne == null) return;
            var b = await _baremeService.ApplicableAsync(_annee, _moisSelectionne.Numero);
            BaremeApplicableLibelle = string.Format(
                "Majorations HS du mois : 41e-48e h {0} · au-delà {1} · nuit {2} · dim./férié jour {3} · dim./férié nuit {4} (barème du {5:dd/MM/yyyy}) — seuil {6:0.##} h/semaine",
                BaremeHeuresSup.Pourcent(b.TauxTranche1), BaremeHeuresSup.Pourcent(b.TauxTranche2), BaremeHeuresSup.Pourcent(b.TauxNuit),
                BaremeHeuresSup.Pourcent(b.TauxFerieJour), BaremeHeuresSup.Pourcent(b.TauxFerieNuit), b.DateEffet, b.DureeHebdoLegale);
        }

        private async Task ChangerPeriodeAsync()
        {
            if (_chargementEnCours || _moisSelectionne == null) return;

            if (!await ProposerEnregistrementAsync()) return;

            try
            {
                IsBusy = true;
                await ChargerPlanningAsync();
                await MettreAJourBaremeApplicableAsync();
                Recaps.Clear();
                NotifierTotaux();
                OnPropertyChanged(nameof(ApercuArrete));
            }
            catch (Exception ex)
            {
                StatusMessage = "Erreur au chargement du planning : " + ex.Message;
            }
            finally
            {
                IsBusy = false;
            }
        }

        private async Task ChargerPlanningAsync()
        {
            if (_moisSelectionne == null) return;

            var periode = await _service.PeriodeAsync(_annee, _moisSelectionne.Numero);
            Periode = periode;

            var equipes = Equipes.ToDictionary(e => e.Id);
            var jours = await _service.ChargerPlanningAsync(periode.Debut, periode.Fin);
            var parSalarie = jours.GroupBy(j => j.EmployeeId, StringComparer.OrdinalIgnoreCase)
                                  .ToDictionary(g => g.Key, g => g.ToDictionary(j => j.Date.Date), StringComparer.OrdinalIgnoreCase);
            var feries = new HashSet<DateTime>(Feries.Select(f => f.Date.Date));

            var dates = new List<DateTime>();
            for (var d = periode.Debut; d <= periode.Fin; d = d.AddDays(1)) dates.Add(d);

            DatesPeriode = dates;

            LignesPlanning.Clear();
            foreach (var s in _salaries.OrderBy(x => x.FullName))
            {
                int idEquipe;
                EquipeTravail equipe = null;
                if (_affectations.TryGetValue(s.EmployeeId, out idEquipe)) equipes.TryGetValue(idEquipe, out equipe);

                Dictionary<DateTime, PlanningJour> siens;
                parSalarie.TryGetValue(s.EmployeeId, out siens);
                bool aDesJours = siens != null && siens.Count > 0;

                if (equipe == null && !aDesJours) continue;
                if (FiltreId.HasValue && (equipe == null || equipe.Id != FiltreId.Value)) continue;
                if (!s.EstActif && !aDesJours) continue;

                var ligne = new LignePlanning(s.EmployeeId, s.FullName, equipe);
                foreach (var d in dates)
                {
                    PlanningJour jour = null;
                    if (siens != null) siens.TryGetValue(d, out jour);
                    var cellule = new CellulePlanning(d, jour?.Code, jour?.Heures, jour?.Source ?? SourceJourPlanning.Saisie, feries.Contains(d));
                    cellule.SaisieRefusee += (o, valeur) =>
                        StatusMessage = string.Format("Code « {0} » refusé. Codes possibles : {1}.", valeur, string.Join(", ", CodesPlanning.Tous));
                    ligne.Jours.Add(cellule);
                }
                ligne.Attacher();
                LignesPlanning.Add(ligne);
            }

            // Toujours : la période, mais aussi les jours fériés de l'en-tête, ont pu changer.
            ColonnesPlanningChangees?.Invoke(this, EventArgs.Empty);

            int nbJours = LignesPlanning.Sum(l => l.Jours.Count(j => !string.IsNullOrEmpty(j.Code)));
            StatusMessage = LignesPlanning.Count == 0
                ? "Aucun salarié affecté à une équipe : créez une équipe et affectez-lui des salariés (onglet « Équipes & cycles »)."
                : string.Format("{0} salarié(s), {1} jour(s) planifiés sur la {2}.", LignesPlanning.Count, nbJours,
                    PeriodeLibelle.Replace("Période de pointage : ", "période "));
        }

        private bool FiltrerLigne(object o)
        {
            if (string.IsNullOrWhiteSpace(_rechercheTexte)) return true;
            var l = o as LignePlanning;
            if (l == null) return true;
            string r = _rechercheTexte.Trim();
            return (l.NomComplet ?? "").IndexOf(r, StringComparison.OrdinalIgnoreCase) >= 0
                || (l.EmployeeId ?? "").IndexOf(r, StringComparison.OrdinalIgnoreCase) >= 0
                || (l.EquipeCode ?? "").IndexOf(r, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private bool FiltrerSalarie(object o)
        {
            var s = o as SalarieAffectable;
            if (s == null) return true;
            if (string.IsNullOrWhiteSpace(_rechercheSalaries)) return true;
            string r = _rechercheSalaries.Trim();
            return (s.NomComplet ?? "").IndexOf(r, StringComparison.OrdinalIgnoreCase) >= 0
                || (s.Matricule ?? "").IndexOf(r, StringComparison.OrdinalIgnoreCase) >= 0
                || (s.EquipeCode ?? "").IndexOf(r, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        // ═══════════════════════════════════════════════════════════════════
        //  Planning
        // ═══════════════════════════════════════════════════════════════════

        private List<CellulePlanning> CellulesModifiees()
        {
            return LignesPlanning.SelectMany(l => l.Jours).Where(c => c.EstModifie).ToList();
        }

        /// <summary>
        /// Avant de quitter la période ou de recalculer : propose d'enregistrer les cases
        /// modifiées. Faux si l'utilisateur annule.
        /// </summary>
        private async Task<bool> ProposerEnregistrementAsync()
        {
            if (CellulesModifiees().Count == 0) return true;

            var reponse = MessageBox.Show(
                "Le planning affiché comporte des modifications non enregistrées.\n\nLes enregistrer maintenant ?",
                "Gestion des temps", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);

            if (reponse == MessageBoxResult.Cancel) return false;
            if (reponse == MessageBoxResult.Yes) return await EnregistrerPlanningAsync();
            return true;
        }

        public async Task<bool> EnregistrerPlanningAsync()
        {
            var modifiees = new List<PlanningJour>();
            foreach (var ligne in LignesPlanning)
            {
                foreach (var c in ligne.Jours.Where(x => x.EstModifie))
                {
                    modifiees.Add(new PlanningJour
                    {
                        EmployeeId = ligne.EmployeeId,
                        Date = c.Date,
                        Code = c.Code,
                        Heures = c.Heures,
                        EquipeId = ligne.EquipeId,
                        Source = SourceJourPlanning.Saisie,
                        Commentaire = "Saisie à l'écran"
                    });
                }
            }

            if (modifiees.Count == 0)
            {
                StatusMessage = "Aucune modification à enregistrer.";
                return true;
            }

            try
            {
                IsBusy = true;
                int n = await _service.EnregistrerPlanningAsync(modifiees);
                foreach (var ligne in LignesPlanning)
                    foreach (var c in ligne.Jours) c.EstModifie = false;
                StatusMessage = string.Format("{0} jour(s) de planning enregistré(s).", n);
                return true;
            }
            catch (Exception ex)
            {
                StatusMessage = "Enregistrement impossible : " + ex.Message;
                MessageBox.Show(ex.Message, "Planning non enregistré", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }
            finally
            {
                IsBusy = false;
            }
        }

        private async Task GenererAsync(bool ecraser)
        {
            if (_periode == null) return;
            if (!await ProposerEnregistrementAsync()) return;

            if (ecraser)
            {
                var r = MessageBox.Show(
                    "Régénérer remplace aussi les jours saisis à la main et les pointages importés de la période " +
                    "par le cycle de chaque équipe.\n\nContinuer ?",
                    "Régénérer le planning", MessageBoxButton.YesNo, MessageBoxImage.Warning);
                if (r != MessageBoxResult.Yes) return;
            }

            try
            {
                IsBusy = true;
                StatusMessage = "Génération du planning depuis les cycles…";
                int n = await _service.GenererDepuisCyclesAsync(_periode, FiltreId, ecraser);
                await ChargerPlanningAsync();
                Recaps.Clear();
                NotifierTotaux();
                StatusMessage = n == 0
                    ? "Planning déjà à jour : aucun jour à générer (vérifiez les équipes, leurs cycles et les affectations)."
                    : string.Format("{0} jour(s) générés depuis les cycles.", n);
            }
            catch (Exception ex)
            {
                StatusMessage = "Génération impossible : " + ex.Message;
            }
            finally
            {
                IsBusy = false;
            }
        }

        private async Task ImporterAsync()
        {
            var dialog = new OpenFileDialog
            {
                Filter = "Fichiers de pointage (*.xlsx;*.csv)|*.xlsx;*.csv;*.txt|Classeur Excel (*.xlsx)|*.xlsx|Fichier CSV (*.csv)|*.csv",
                Title = "Importer un planning ou des pointages"
            };
            if (dialog.ShowDialog() != true) return;

            try
            {
                IsBusy = true;
                StatusMessage = "Lecture du fichier…";
                var matricules = new HashSet<string>(_salaries.Select(s => s.EmployeeId), StringComparer.OrdinalIgnoreCase);
                var resultat = await _excel.ImporterAsync(dialog.FileName, matricules);

                // Le matricule lu dans le fichier reprend la casse de la fiche salarié.
                var casse = _salaries.ToDictionary(s => s.EmployeeId, s => s.EmployeeId, StringComparer.OrdinalIgnoreCase);
                foreach (var j in resultat.Jours)
                {
                    string m;
                    if (casse.TryGetValue(j.EmployeeId, out m)) j.EmployeeId = m;
                    int idEquipe;
                    if (_affectations.TryGetValue(j.EmployeeId, out idEquipe)) j.EquipeId = idEquipe;
                }

                string message = string.Format("{0} : {1} ligne(s) lue(s), {2} jour(s) à importer.",
                    resultat.Format, resultat.LignesLues, resultat.Jours.Count);
                if (resultat.Erreurs.Count > 0)
                {
                    message += string.Format("\n\n{0} anomalie(s) ignorée(s) :\n{1}", resultat.Erreurs.Count,
                        string.Join("\n", resultat.Erreurs.Take(12)) + (resultat.Erreurs.Count > 12 ? "\n…" : ""));
                }

                if (resultat.Jours.Count == 0)
                {
                    IsBusy = false;
                    MessageBox.Show(message, "Rien à importer", MessageBoxButton.OK, MessageBoxImage.Information);
                    StatusMessage = "Import : aucun jour exploitable.";
                    return;
                }

                IsBusy = false;
                var reponse = MessageBox.Show(message + "\n\nLes jours importés remplacent ceux du planning aux mêmes dates. Importer ?",
                    "Import du planning", MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (reponse != MessageBoxResult.Yes)
                {
                    StatusMessage = "Import annulé.";
                    return;
                }

                IsBusy = true;
                int n = await _service.EnregistrerPlanningAsync(resultat.Jours);
                await ChargerPlanningAsync();
                Recaps.Clear();
                NotifierTotaux();
                StatusMessage = string.Format("{0} jour(s) importé(s).", n);
            }
            catch (Exception ex)
            {
                StatusMessage = "Import interrompu : " + ex.Message;
                MessageBox.Show(ex.Message, "Import interrompu — rien n'a été enregistré", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            finally
            {
                IsBusy = false;
            }
        }

        private async Task ExporterModeleAsync()
        {
            var dialog = new SaveFileDialog
            {
                Filter = "Classeur Excel (*.xlsx)|*.xlsx",
                FileName = "Modele_pointages.xlsx",
                Title = "Modèle de fichier de pointages"
            };
            if (dialog.ShowDialog() != true) return;

            try
            {
                IsBusy = true;
                await _excel.ExporterModelePointagesAsync(dialog.FileName);
                StatusMessage = "Modèle enregistré : " + Path.GetFileName(dialog.FileName);
            }
            catch (Exception ex)
            {
                StatusMessage = "Export du modèle impossible : " + ex.Message;
            }
            finally
            {
                IsBusy = false;
            }
        }

        private async Task ExporterExcelAsync()
        {
            if (_periode == null) return;

            var dialog = new SaveFileDialog
            {
                Filter = "Classeur Excel (*.xlsx)|*.xlsx",
                FileName = string.Format("Planning_rotations_{0}_{1:D2}.xlsx", _periode.Annee, _periode.Mois),
                Title = "Exporter le planning et le récapitulatif"
            };
            if (dialog.ShowDialog() != true) return;

            try
            {
                IsBusy = true;
                var lignes = LignesPlanning.Select(l =>
                {
                    var g = new LigneGrillePlanning { EmployeeId = l.EmployeeId, NomComplet = l.NomComplet, EquipeCode = l.EquipeCode };
                    foreach (var c in l.Jours) g.Codes[c.Date] = c.Code;
                    return g;
                }).ToList();

                await _excel.ExporterPlanningAsync(lignes, Recaps, _periode, DossierSession.Instance.CurrentDossier, dialog.FileName);
                StatusMessage = "Planning exporté : " + Path.GetFileName(dialog.FileName);
            }
            catch (Exception ex)
            {
                StatusMessage = "Export impossible : " + ex.Message;
            }
            finally
            {
                IsBusy = false;
            }
        }

        // ═══════════════════════════════════════════════════════════════════
        //  Décompte et transferts
        // ═══════════════════════════════════════════════════════════════════

        private async Task CalculerAsync(bool basculer)
        {
            if (_moisSelectionne == null) return;
            if (!await ProposerEnregistrementAsync()) return;

            try
            {
                IsBusy = true;
                StatusMessage = "Décompte des heures de la période…";
                var recaps = await _service.CalculerAsync(_annee, _moisSelectionne.Numero, FiltreId);

                Recaps.Clear();
                foreach (var r in recaps) Recaps.Add(r);
                RecapSelectionne = Recaps.FirstOrDefault();
                NotifierTotaux();

                if (basculer) OngletActif = 1;
                StatusMessage = string.Format(
                    "{0} salarié(s) décompté(s) : {1:0.##} h supplémentaires, {2} panier(s), {3} jour(s) d'absence non justifiée.",
                    Recaps.Count, TotalHeuresSup, TotalPaniers, TotalAbsences);
            }
            catch (Exception ex)
            {
                StatusMessage = "Décompte impossible : " + ex.Message;
            }
            finally
            {
                IsBusy = false;
            }
        }

        private async Task TransfererAsync(bool heuresSup, bool absences, bool paniers)
        {
            if (_periode == null || Recaps.Count == 0) return;

            var objets = new List<string>();
            if (heuresSup) objets.Add("les heures supplémentaires (écran « Heures supplémentaires ») — la saisie existante des salariés listés est remplacée");
            if (absences) objets.Add("les absences non justifiées (écran « Congés et absences »), imputées à la paie du mois — les relevés précédents sont remplacés, les saisies manuelles conservées");
            if (paniers) objets.Add("les primes de panier (« Variables du mois », rubrique PANIER) — les lignes d'un transfert précédent sont remplacées");

            var reponse = MessageBox.Show(
                string.Format("Transférer vers la paie de {0:D2}/{1}, pour {2} salarié(s) :\n\n• {3}\n\nContinuer ?",
                    _periode.Mois, _periode.Annee, Recaps.Count, string.Join("\n• ", objets)),
                "Transfert vers la paie", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (reponse != MessageBoxResult.Yes) return;

            var comptesRendus = new List<string>();
            try
            {
                IsBusy = true;
                var liste = Recaps.ToList();

                if (heuresSup)
                {
                    var r = await _service.TransfererHeuresSupAsync(_annee, _moisSelectionne.Numero, liste, _periode);
                    comptesRendus.Add("Heures supplémentaires — " + r.Resume("heures"));
                }
                if (absences)
                {
                    var r = await _service.TransfererAbsencesAsync(_annee, _moisSelectionne.Numero, liste, _periode);
                    comptesRendus.Add("Absences — " + r.Resume("absences"));
                }
                if (paniers)
                {
                    var r = await _service.TransfererPaniersAsync(_annee, _moisSelectionne.Numero, liste, _periode);
                    comptesRendus.Add("Paniers — " + r.Resume("paniers"));
                }

                StatusMessage = string.Join("  |  ", comptesRendus);
                IsBusy = false;
                MessageBox.Show(string.Join("\n\n", comptesRendus), "Transfert terminé", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                comptesRendus.Add("Interrompu : " + ex.Message);
                StatusMessage = string.Join("  |  ", comptesRendus);
                IsBusy = false;
                MessageBox.Show(string.Join("\n\n", comptesRendus), "Transfert interrompu", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            finally
            {
                IsBusy = false;
            }
        }

        private void NotifierTotaux()
        {
            OnPropertyChanged(nameof(TotalHeuresSup));
            OnPropertyChanged(nameof(TotalTranche1));
            OnPropertyChanged(nameof(TotalTranche2));
            OnPropertyChanged(nameof(TotalNuit));
            OnPropertyChanged(nameof(TotalFerie));
            OnPropertyChanged(nameof(TotalPaniers));
            OnPropertyChanged(nameof(TotalAbsences));
            OnPropertyChanged(nameof(RecapDisponible));
            CommandManager.InvalidateRequerySuggested();
        }

        // ═══════════════════════════════════════════════════════════════════
        //  Équipes
        // ═══════════════════════════════════════════════════════════════════

        private void NouvelleEquipe()
        {
            _equipeSelectionnee = null;
            OnPropertyChanged(nameof(EquipeSelectionnee));
            EquipeEnCours = new EquipeTravail
            {
                Code = "EQUIPE " + (Equipes.Count + 1),
                Motif = "7J 7N 7R",
                DateDebutCycle = _periode?.Debut ?? DateTime.Today,
                ModeSemaine = ModeSemaineDecompte.Rotation
            };
            StatusMessage = "Nouvelle équipe : renseignez le code, le cycle et sa date de début, puis enregistrez.";
        }

        private async Task EnregistrerEquipeAsync()
        {
            if (_equipeEnCours == null) return;
            try
            {
                IsBusy = true;
                await _service.EnregistrerEquipeAsync(_equipeEnCours);
                int id = _equipeEnCours.Id;
                _equipeSelectionnee = null;
                await ChargerReferentielsAsync();
                EquipeSelectionnee = Equipes.FirstOrDefault(e => e.Id == id);
                await ChargerPlanningAsync();
                StatusMessage = string.Format("Équipe « {0} » enregistrée. {1}", _equipeEnCours?.Code, _equipeEnCours?.ResumeCycle);
            }
            catch (Exception ex)
            {
                StatusMessage = "Équipe non enregistrée : " + ex.Message;
                MessageBox.Show(ex.Message, "Équipe non enregistrée", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            finally
            {
                IsBusy = false;
            }
        }

        private async Task SupprimerEquipeAsync()
        {
            if (!EquipeEnregistree) return;
            var r = MessageBox.Show(
                string.Format("Supprimer l'équipe « {0} » ? Ses {1} salarié(s) n'auront plus d'équipe. Le planning déjà établi est conservé.",
                    _equipeEnCours.Code, _equipeEnCours.NombreMembres),
                "Supprimer l'équipe", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (r != MessageBoxResult.Yes) return;

            try
            {
                IsBusy = true;
                await _service.SupprimerEquipeAsync(_equipeEnCours);
                _equipeSelectionnee = null;
                EquipeEnCours = null;
                await ChargerReferentielsAsync();
                await ChargerPlanningAsync();
                StatusMessage = "Équipe supprimée.";
            }
            catch (Exception ex)
            {
                StatusMessage = "Suppression impossible : " + ex.Message;
            }
            finally
            {
                IsBusy = false;
            }
        }

        private async Task AffecterAsync()
        {
            if (!EquipeEnregistree) return;
            var coches = Salaries.Where(s => s.EstCoche).Select(s => s.Matricule).ToList();
            if (coches.Count == 0)
            {
                StatusMessage = "Cochez les salariés à affecter à l'équipe.";
                return;
            }

            try
            {
                IsBusy = true;
                int id = _equipeEnCours.Id;
                await _service.AffecterAsync(coches, id);
                await ChargerReferentielsAsync();
                await ChargerPlanningAsync();
                StatusMessage = string.Format("{0} salarié(s) affecté(s) à l'équipe « {1} ». Générez ensuite le planning depuis le cycle.",
                    coches.Count, _equipeEnCours?.Code);
            }
            catch (Exception ex)
            {
                StatusMessage = "Affectation impossible : " + ex.Message;
            }
            finally
            {
                IsBusy = false;
            }
        }

        private async Task RetirerAsync()
        {
            var coches = Salaries.Where(s => s.EstCoche).Select(s => s.Matricule).ToList();
            if (coches.Count == 0)
            {
                StatusMessage = "Cochez les salariés à retirer de leur équipe.";
                return;
            }

            try
            {
                IsBusy = true;
                await _service.RetirerAsync(coches);
                await ChargerReferentielsAsync();
                await ChargerPlanningAsync();
                StatusMessage = string.Format("{0} salarié(s) retiré(s) de leur équipe.", coches.Count);
            }
            catch (Exception ex)
            {
                StatusMessage = "Retrait impossible : " + ex.Message;
            }
            finally
            {
                IsBusy = false;
            }
        }

        // ═══════════════════════════════════════════════════════════════════
        //  Paramètres, jours fériés, barème HS
        // ═══════════════════════════════════════════════════════════════════

        private async Task EnregistrerParametresAsync()
        {
            try
            {
                IsBusy = true;
                await _service.EnregistrerParametresAsync(new ParametresTemps
                {
                    JourArrete = _jourArrete,
                    AJCompteDansSeuil = _ajCompteDansSeuil
                });
                await ChargerPlanningAsync();
                Recaps.Clear();
                NotifierTotaux();
                StatusMessage = "Paramètres enregistrés. " + ApercuArrete;
            }
            catch (Exception ex)
            {
                StatusMessage = "Paramètres non enregistrés : " + ex.Message;
            }
            finally
            {
                IsBusy = false;
            }
        }

        private async Task AjouterFerieAsync()
        {
            try
            {
                IsBusy = true;
                await _service.AjouterFerieAsync(_nouveauFerieDate, _nouveauFerieLibelle);
                NouveauFerieLibelle = string.Empty;
                Feries.Clear();
                foreach (var f in await _service.ChargerFeriesAsync()) Feries.Add(f);
                await ChargerPlanningAsync();
                StatusMessage = "Jour férié enregistré : le travail de ce jour est majoré comme férié (FJ/FN).";
            }
            catch (Exception ex)
            {
                StatusMessage = "Jour férié non enregistré : " + ex.Message;
            }
            finally
            {
                IsBusy = false;
            }
        }

        private async Task SupprimerFerieAsync()
        {
            if (_ferieSelectionne == null) return;
            try
            {
                IsBusy = true;
                await _service.SupprimerFerieAsync(_ferieSelectionne);
                Feries.Clear();
                foreach (var f in await _service.ChargerFeriesAsync()) Feries.Add(f);
                await ChargerPlanningAsync();
                StatusMessage = "Jour férié supprimé.";
            }
            catch (Exception ex)
            {
                StatusMessage = "Suppression impossible : " + ex.Message;
            }
            finally
            {
                IsBusy = false;
            }
        }

        private void NouveauBareme()
        {
            var modele = (_baremeSelectionne ?? Baremes.FirstOrDefault() ?? BaremeHeuresSup.CinqTranches2026()).Clone();
            modele.Id = 0;
            modele.EstSysteme = false;
            modele.Libelle = "Nouveau barème HS";
            modele.Reference = string.Empty;
            modele.DateEffet = new DateTime(_annee, _moisSelectionne?.Numero ?? 1, 1);
            _baremeSelectionne = null;
            OnPropertyChanged(nameof(BaremeSelectionne));
            BaremeEnCours = modele;
            StatusMessage = "Nouveau barème : ajustez la date d'effet et les taux (en %), puis enregistrez. Les mois antérieurs gardent leur barème.";
        }

        private async Task EnregistrerBaremeAsync()
        {
            if (_baremeEnCours == null) return;
            try
            {
                IsBusy = true;
                await _baremeService.EnregistrerAsync(_baremeEnCours);
                int id = _baremeEnCours.Id;
                Baremes.Clear();
                foreach (var b in await _baremeService.ChargerToutAsync()) Baremes.Add(b);
                BaremeSelectionne = Baremes.FirstOrDefault(b => b.Id == id);
                await MettreAJourBaremeApplicableAsync();
                StatusMessage = "Barème des heures supplémentaires enregistré.";
            }
            catch (Exception ex)
            {
                StatusMessage = "Barème non enregistré : " + ex.Message;
                MessageBox.Show(ex.Message, "Barème non enregistré", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            finally
            {
                IsBusy = false;
            }
        }

        private async Task SupprimerBaremeAsync()
        {
            if (_baremeEnCours == null || _baremeEnCours.Id <= 0) return;
            var r = MessageBox.Show("Supprimer ce barème ? Les mois concernés reprendront le barème précédent.",
                "Supprimer le barème", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (r != MessageBoxResult.Yes) return;

            try
            {
                IsBusy = true;
                await _baremeService.SupprimerAsync(_baremeEnCours);
                BaremeSelectionne = null;
                Baremes.Clear();
                foreach (var b in await _baremeService.ChargerToutAsync()) Baremes.Add(b);
                await MettreAJourBaremeApplicableAsync();
                StatusMessage = "Barème supprimé.";
            }
            catch (Exception ex)
            {
                StatusMessage = "Suppression impossible : " + ex.Message;
            }
            finally
            {
                IsBusy = false;
            }
        }
    }
}
