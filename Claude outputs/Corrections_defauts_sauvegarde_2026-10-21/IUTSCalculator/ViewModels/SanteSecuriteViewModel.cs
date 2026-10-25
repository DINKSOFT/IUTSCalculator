using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
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
    /// <summary>
    /// Écran « Santé &amp; sécurité au travail » (module RH-07) : échéancier, accidents du
    /// travail (déclaration CNSS en 48 heures ouvrables, arrêt reporté dans les absences,
    /// indemnités journalières à réclamer), visites médicales, EPI remis aux salariés,
    /// paramètres et catalogue des EPI.
    /// </summary>
    public class SanteSecuriteViewModel : BaseViewModel
    {
        public const int OngletEcheancier = 0;
        public const int OngletAccidents = 1;
        public const int OngletVisites = 2;
        public const int OngletEpi = 3;
        public const int OngletIndemnites = 4;
        public const int OngletParametres = 5;

        private const string Toutes = "Toutes les échéances";
        private const string Tous = "Tous";

        private readonly SanteSecuriteService _service;
        private readonly SanteSecuriteExportService _export;
        private readonly DataService _dataService;

        private ParametresSST _parametres = ParametresSST.Defaut();
        private HashSet<DateTime> _feries = new HashSet<DateTime>();
        private List<Employee> _salaries = new List<Employee>();
        private List<ArretSST> _arrets = new List<ArretSST>();

        // Pendant qu'une liste liée est reconstruite, les listes déroulantes et les grilles
        // renvoient une sélection vide : elle ne doit pas être prise pour une saisie.
        private bool _synchronisation;
        private int _ongletActif;

        private string _filtreCategorie = Toutes;
        private bool _seulementEnRetard;
        private EcheanceSST _echeanceSelectionnee;

        private AccidentTravail _accidentSelectionne;
        private AccidentTravail _accidentEnCours;
        private bool _accidentModifie;
        private Employee _salarieAccident;
        private string _rechercheAccident;

        private VisiteMedicale _visiteSelectionnee;
        private VisiteMedicale _visiteEnCours;
        private Employee _salarieVisite;
        private SituationMedicale _situationSelectionnee;
        private string _rechercheVisite;
        private int _vueVisites;

        private DotationEPI _dotationSelectionnee;
        private DotationEPI _dotationEnCours;
        private Employee _salarieDotation;
        private string _rechercheDotation;
        private bool _seulementEnService = true;

        private int _anneeIndemnites = DateTime.Today.Year;
        private int _moisIndemnites;

        private ParametresSST _parametresEnCours = ParametresSST.Defaut();
        private TypeEPI _typeEpiSelectionne;
        private TypeEPI _typeEpiEnCours;

        // ── Collections ──────────────────────────────────────────────────

        public ObservableCollection<EcheanceSST> Echeances { get; } = new ObservableCollection<EcheanceSST>();
        public ObservableCollection<AccidentTravail> Accidents { get; } = new ObservableCollection<AccidentTravail>();
        public ObservableCollection<AlerteSST> AlertesAccident { get; } = new ObservableCollection<AlerteSST>();
        public ObservableCollection<VisiteMedicale> Visites { get; } = new ObservableCollection<VisiteMedicale>();
        public ObservableCollection<SituationMedicale> Situations { get; } = new ObservableCollection<SituationMedicale>();
        public ObservableCollection<DotationEPI> Dotations { get; } = new ObservableCollection<DotationEPI>();
        public ObservableCollection<TypeEPI> TypesEpi { get; } = new ObservableCollection<TypeEPI>();
        public ObservableCollection<TypeEPI> TypesEpiProposes { get; } = new ObservableCollection<TypeEPI>();
        public ObservableCollection<PeriodicitePoste> Periodicites { get; } = new ObservableCollection<PeriodicitePoste>();
        public ObservableCollection<LigneIndemniteJournaliere> Indemnites { get; } = new ObservableCollection<LigneIndemniteJournaliere>();
        public ObservableCollection<Employee> Salaries { get; } = new ObservableCollection<Employee>();
        public ObservableCollection<int> Annees { get; } = new ObservableCollection<int>();

        public ICollectionView VueEcheances { get; }
        public ICollectionView VueAccidents { get; }
        public ICollectionView VueVisites { get; }
        public ICollectionView VueSituations { get; }
        public ICollectionView VueDotations { get; }

        public IEnumerable<string> Categories { get; } = new[] { Toutes, "Visite médicale", "EPI", "Accident" };
        public IEnumerable<string> SiegesLesion => LibellesSST.SiegesLesion;
        public IEnumerable<string> NaturesLesion => LibellesSST.NaturesLesion;

        public IEnumerable<KeyValuePair<NatureAccident, string>> NaturesAccident { get; } =
            Enum.GetValues(typeof(NatureAccident)).Cast<NatureAccident>()
                .Select(n => new KeyValuePair<NatureAccident, string>(n, LibellesSST.Nature(n))).ToList();

        public IEnumerable<KeyValuePair<TypeVisiteMedicale, string>> TypesVisite { get; } =
            Enum.GetValues(typeof(TypeVisiteMedicale)).Cast<TypeVisiteMedicale>()
                .Select(t => new KeyValuePair<TypeVisiteMedicale, string>(t, LibellesSST.Visite(t))).ToList();

        public IEnumerable<KeyValuePair<AptitudeMedicale, string>> Aptitudes { get; } =
            Enum.GetValues(typeof(AptitudeMedicale)).Cast<AptitudeMedicale>()
                .Select(a => new KeyValuePair<AptitudeMedicale, string>(a, a == AptitudeMedicale.NonRenseignee ? "Non renseigné (visite à venir)" : LibellesSST.Aptitude(a))).ToList();

        public IEnumerable<KeyValuePair<EtatDotation, string>> EtatsDotation { get; } =
            Enum.GetValues(typeof(EtatDotation)).Cast<EtatDotation>()
                .Select(e => new KeyValuePair<EtatDotation, string>(e, LibellesSST.Etat(e))).ToList();

        public IEnumerable<KeyValuePair<int, string>> MoisListe { get; } =
            new[] { new KeyValuePair<int, string>(0, "Toute l'année") }
                .Concat(Enumerable.Range(1, 12).Select(m =>
                {
                    var n = LibellesSST.Fr.DateTimeFormat.GetMonthName(m);
                    return new KeyValuePair<int, string>(m, char.ToUpper(n[0], LibellesSST.Fr) + n.Substring(1));
                })).ToList();

        // ── État général ─────────────────────────────────────────────────

        public bool PeutModifier => UserSession.Instance.CanEdit;

        public int OngletActif
        {
            get => _ongletActif;
            set => SetProperty(ref _ongletActif, value);
        }

        public ParametresSST Parametres => _parametres;

        public int NombreEcheancesEnRetard => Echeances.Count(e => e.EstDepassee);
        public int NombreVisitesAPasser => Situations.Count(s => s.JoursRestants <= _parametres.AlerteAvanceJours);
        public int NombreAvecReserves => Situations.Count(s => s.AvecReserves);
        public int NombreEpiARenouveler => Dotations.Count(d => d.Etat == EtatDotation.EnService && d.DateRenouvellement.HasValue
                                                                && d.DateRenouvellement.Value <= DateTime.Today.AddDays(_parametres.AlerteAvanceJours));
        public int NombreAccidentsAnnee => Accidents.Count(a => a.DateAccident.Year == DateTime.Today.Year);
        public int NombreAccidentsADeclarer => Accidents.Count(a => !a.DateDeclarationCnss.HasValue || !a.DateDeclarationInspection.HasValue);

        public int JoursArretAnnee
        {
            get
            {
                var debut = new DateTime(DateTime.Today.Year, 1, 1);
                var fin = new DateTime(DateTime.Today.Year, 12, 31);
                return Accidents.Sum(a => ControleSanteSecurite.JoursIndemnises(a, debut, fin, DateTime.Today));
            }
        }

        public string RappelLegal =>
            string.Format(LibellesSST.Fr,
                "Déclaration à la CNSS et à l'inspection du travail dans les {0} jours ouvrables (48 heures ouvrables). Jour de l'accident à la charge de l'employeur ; " +
                "à partir du lendemain, indemnité journalière CNSS de {1:0.##} % de la rémunération journalière, chaque jour d'arrêt. Salaire maintenu en entier par l'entreprise : les indemnités sont à réclamer à la CNSS.",
                _parametres.DelaiDeclarationJoursOuvrables, _parametres.TauxIndemniteJournaliere * 100m);

        // ── Échéancier ───────────────────────────────────────────────────

        public string FiltreCategorie
        {
            get => _filtreCategorie;
            set { if (SetProperty(ref _filtreCategorie, value)) VueEcheances.Refresh(); }
        }

        public bool SeulementEnRetard
        {
            get => _seulementEnRetard;
            set { if (SetProperty(ref _seulementEnRetard, value)) VueEcheances.Refresh(); }
        }

        public EcheanceSST EcheanceSelectionnee
        {
            get => _echeanceSelectionnee;
            set => SetProperty(ref _echeanceSelectionnee, value);
        }

        // ── Accidents ────────────────────────────────────────────────────

        public string RechercheAccident
        {
            get => _rechercheAccident;
            set { if (SetProperty(ref _rechercheAccident, value)) VueAccidents.Refresh(); }
        }

        public AccidentTravail AccidentSelectionne
        {
            get => _accidentSelectionne;
            set
            {
                if (_accidentSelectionne == value || _synchronisation) return;
                ProposerEnregistrementAccident();
                if (SetProperty(ref _accidentSelectionne, value))
                    AccidentEnCours = value?.Clone();
            }
        }

        /// <summary>Copie éditée : la liste ne change qu'à l'enregistrement.</summary>
        public AccidentTravail AccidentEnCours
        {
            get => _accidentEnCours;
            private set
            {
                if (_accidentEnCours != null) _accidentEnCours.PropertyChanged -= AccidentEnCours_PropertyChanged;
                if (SetProperty(ref _accidentEnCours, value))
                {
                    if (_accidentEnCours != null) _accidentEnCours.PropertyChanged += AccidentEnCours_PropertyChanged;
                    AccidentModifie = false;
                    _salarieAccident = _accidentEnCours == null ? null : Trouver(_accidentEnCours.EmployeeId);
                    OnPropertyChanged(nameof(SalarieAccident));
                    NotifierAccident();
                }
            }
        }

        public bool AccidentModifie
        {
            get => _accidentModifie;
            private set { if (SetProperty(ref _accidentModifie, value)) OnPropertyChanged(nameof(TitreAccident)); }
        }

        public bool AccidentOuvert => _accidentEnCours != null;
        public bool SalarieAccidentModifiable => _accidentEnCours != null && _accidentEnCours.EstNouveau && PeutModifier;
        public bool ArretSaisissable => _accidentEnCours != null && _accidentEnCours.AvecArret && !_accidentEnCours.Mortel;

        public string TitreAccident
        {
            get
            {
                if (_accidentEnCours == null) return "Sélectionnez un accident ou déclarez-en un nouveau.";
                var t = _accidentEnCours.EstNouveau ? "Nouvel accident" : _accidentEnCours.Reference + " — " + _accidentEnCours.DeclarationLibelle;
                return _accidentModifie ? t + " (modifié)" : t;
            }
        }

        public Employee SalarieAccident
        {
            get => _salarieAccident;
            set
            {
                if (_synchronisation) return;
                if (!SetProperty(ref _salarieAccident, value) || _accidentEnCours == null || !_accidentEnCours.EstNouveau) return;
                _accidentEnCours.EmployeeId = value?.EmployeeId;
                _accidentEnCours.NomSalarie = value?.FullName;
                if (value != null && _accidentEnCours.SalaireJournalier <= 0m)
                    _accidentEnCours.SalaireJournalier = ControleSanteSecurite.SalaireJournalierPropose(value, _parametres);
            }
        }

        public string DateLimiteLibelle
        {
            get
            {
                if (_accidentEnCours == null) return "";
                var d = ControleSanteSecurite.DateLimiteDeclaration(_accidentEnCours, _parametres, _feries);
                return string.Format(LibellesSST.Fr, "À déclarer au plus tard le {0:dddd dd/MM/yyyy} (48 heures ouvrables), à la CNSS et à l'inspection du travail.", d);
            }
        }

        public string SalaireProposeLibelle
        {
            get
            {
                var s = _accidentEnCours == null ? null : Trouver(_accidentEnCours.EmployeeId);
                if (s == null) return "Salaire mensuel ÷ " + _parametres.JoursMoisReference + ".";
                return string.Format(LibellesSST.Fr, "Proposé : {0:N0} FCFA (salaire de base et indemnités ÷ {1}).",
                    ControleSanteSecurite.SalaireJournalierPropose(s, _parametres), _parametres.JoursMoisReference);
            }
        }

        public int NombreBloquantsAccident => AlertesAccident.Count(a => a.Niveau == NiveauAlerte.Bloquant);

        // ── Visites ──────────────────────────────────────────────────────

        public int VueVisitesIndex
        {
            get => _vueVisites;
            set => SetProperty(ref _vueVisites, value);
        }

        public string RechercheVisite
        {
            get => _rechercheVisite;
            set
            {
                if (!SetProperty(ref _rechercheVisite, value)) return;
                VueVisites.Refresh();
                VueSituations.Refresh();
            }
        }

        public SituationMedicale SituationSelectionnee
        {
            get => _situationSelectionnee;
            set => SetProperty(ref _situationSelectionnee, value);
        }

        public VisiteMedicale VisiteSelectionnee
        {
            get => _visiteSelectionnee;
            set
            {
                if (_synchronisation) return;
                if (SetProperty(ref _visiteSelectionnee, value))
                    OuvrirVisite(value?.Clone());
            }
        }

        public VisiteMedicale VisiteEnCours
        {
            get => _visiteEnCours;
            private set
            {
                if (SetProperty(ref _visiteEnCours, value))
                {
                    OnPropertyChanged(nameof(VisiteOuverte));
                    OnPropertyChanged(nameof(TitreVisite));
                }
            }
        }

        public bool VisiteOuverte => _visiteEnCours != null;

        public string TitreVisite => _visiteEnCours == null ? "Sélectionnez une visite, ou programmez-en une."
            : _visiteEnCours.EstNouvelle ? "Nouvelle visite" : _visiteEnCours.TypeLibelle + " — " + (_visiteEnCours.NomSalarie ?? _visiteEnCours.EmployeeId);

        public Employee SalarieVisite
        {
            get => _salarieVisite;
            set
            {
                if (_synchronisation) return;
                if (SetProperty(ref _salarieVisite, value) && _visiteEnCours != null)
                {
                    _visiteEnCours.EmployeeId = value?.EmployeeId;
                    _visiteEnCours.NomSalarie = value?.FullName;
                }
            }
        }

        // ── EPI ──────────────────────────────────────────────────────────

        public string RechercheDotation
        {
            get => _rechercheDotation;
            set { if (SetProperty(ref _rechercheDotation, value)) VueDotations.Refresh(); }
        }

        public bool SeulementEnService
        {
            get => _seulementEnService;
            set { if (SetProperty(ref _seulementEnService, value)) VueDotations.Refresh(); }
        }

        public DotationEPI DotationSelectionnee
        {
            get => _dotationSelectionnee;
            set
            {
                if (_synchronisation) return;
                if (SetProperty(ref _dotationSelectionnee, value))
                    OuvrirDotation(value?.Clone());
            }
        }

        public DotationEPI DotationEnCours
        {
            get => _dotationEnCours;
            private set
            {
                if (SetProperty(ref _dotationEnCours, value))
                {
                    OnPropertyChanged(nameof(DotationOuverte));
                    OnPropertyChanged(nameof(TitreDotation));
                }
            }
        }

        public bool DotationOuverte => _dotationEnCours != null;

        public string TitreDotation => _dotationEnCours == null ? "Sélectionnez une remise, ou enregistrez-en une nouvelle."
            : _dotationEnCours.EstNouvelle ? "Nouvelle remise d'EPI" : _dotationEnCours.TypeEpiLibelle + " — " + (_dotationEnCours.NomSalarie ?? _dotationEnCours.EmployeeId);

        public Employee SalarieDotation
        {
            get => _salarieDotation;
            set
            {
                if (_synchronisation) return;
                if (SetProperty(ref _salarieDotation, value) && _dotationEnCours != null)
                {
                    _dotationEnCours.EmployeeId = value?.EmployeeId;
                    _dotationEnCours.NomSalarie = value?.FullName;
                }
            }
        }

        // ── Indemnités journalières ──────────────────────────────────────

        public int AnneeIndemnites
        {
            get => _anneeIndemnites;
            set { if (SetProperty(ref _anneeIndemnites, value)) RecalculerIndemnites(); }
        }

        public int MoisIndemnites
        {
            get => _moisIndemnites;
            set { if (SetProperty(ref _moisIndemnites, value)) RecalculerIndemnites(); }
        }

        public int TotalJoursIndemnises => Indemnites.Sum(i => i.Jours);
        public decimal TotalIndemnitesCnss => Indemnites.Sum(i => i.MontantCnss);
        public decimal TotalPartEmployeur => Indemnites.Sum(i => i.PartEmployeur);

        // ── Paramètres et catalogue ──────────────────────────────────────

        public ParametresSST ParametresEnCours
        {
            get => _parametresEnCours;
            private set => SetProperty(ref _parametresEnCours, value);
        }

        public TypeEPI TypeEpiSelectionne
        {
            get => _typeEpiSelectionne;
            set
            {
                if (_synchronisation) return;
                if (SetProperty(ref _typeEpiSelectionne, value))
                    TypeEpiEnCours = value?.Clone();
            }
        }

        public TypeEPI TypeEpiEnCours
        {
            get => _typeEpiEnCours;
            set => SetProperty(ref _typeEpiEnCours, value);
        }

        // ── Commandes ────────────────────────────────────────────────────

        public ICommand ChargerCommand { get; }
        public ICommand ExporterRegistreCommand { get; }
        public ICommand TraiterEcheanceCommand { get; }

        public ICommand NouvelAccidentCommand { get; }
        public ICommand EnregistrerAccidentCommand { get; }
        public ICommand AnnulerAccidentCommand { get; }
        public ICommand SupprimerAccidentCommand { get; }
        public ICommand FicheAccidentCommand { get; }
        public ICommand ProposerSalaireCommand { get; }
        public ICommand DeclareCnssAujourdhuiCommand { get; }
        public ICommand DeclareInspectionAujourdhuiCommand { get; }
        public ICommand ProgrammerRepriseCommand { get; }

        public ICommand NouvelleVisiteCommand { get; }
        public ICommand ProgrammerVisiteSituationCommand { get; }
        public ICommand EnregistrerVisiteCommand { get; }
        public ICommand SupprimerVisiteCommand { get; }

        public ICommand NouvelleDotationCommand { get; }
        public ICommand EnregistrerDotationCommand { get; }
        public ICommand SupprimerDotationCommand { get; }
        public ICommand RenouvelerDotationCommand { get; }
        public ICommand FicheDotationCommand { get; }

        public ICommand EnregistrerParametresCommand { get; }
        public ICommand EnregistrerPeriodicitesCommand { get; }
        public ICommand NouveauTypeEpiCommand { get; }
        public ICommand EnregistrerTypeEpiCommand { get; }
        public ICommand SupprimerTypeEpiCommand { get; }

        public SanteSecuriteViewModel(SanteSecuriteService service, DataService dataService)
        {
            _service = service ?? throw new ArgumentNullException(nameof(service));
            _dataService = dataService ?? throw new ArgumentNullException(nameof(dataService));
            _export = new SanteSecuriteExportService();

            Title = "Santé & sécurité au travail";

            VueEcheances = new ListCollectionView(Echeances) { Filter = FiltrerEcheance };
            VueAccidents = new ListCollectionView(Accidents) { Filter = FiltrerAccident };
            VueVisites = new ListCollectionView(Visites) { Filter = o => Correspond(((VisiteMedicale)o).EmployeeId, ((VisiteMedicale)o).NomSalarie, ((VisiteMedicale)o).Medecin, _rechercheVisite) };
            VueSituations = new ListCollectionView(Situations) { Filter = o => Correspond(((SituationMedicale)o).EmployeeId, ((SituationMedicale)o).NomSalarie, ((SituationMedicale)o).Poste, _rechercheVisite) };
            VueDotations = new ListCollectionView(Dotations) { Filter = FiltrerDotation };

            Func<bool> edition = () => !IsBusy && PeutModifier;
            Func<bool> accidentOuvert = () => edition() && _accidentEnCours != null;
            Func<bool> accidentEnregistre = () => edition() && _accidentEnCours != null && !_accidentEnCours.EstNouveau;

            ChargerCommand = new RelayCommand(async () => await ChargerToutAsync(), () => !IsBusy && !_accidentModifie);
            ExporterRegistreCommand = new RelayCommand(async () => await ExporterAsync(), () => !IsBusy);
            TraiterEcheanceCommand = new RelayCommand(TraiterEcheance, () => _echeanceSelectionnee != null);

            NouvelAccidentCommand = new RelayCommand(NouvelAccident, edition);
            EnregistrerAccidentCommand = new RelayCommand(async () => await EnregistrerAccidentAsync(), accidentOuvert);
            AnnulerAccidentCommand = new RelayCommand(AnnulerAccident, () => !IsBusy && _accidentEnCours != null && _accidentModifie);
            SupprimerAccidentCommand = new RelayCommand(async () => await SupprimerAccidentAsync(),
                () => accidentEnregistre() && _accidentSelectionne != null && !_accidentSelectionne.DateDeclarationCnss.HasValue);
            FicheAccidentCommand = new RelayCommand(async () => await FicheAccidentAsync(), () => !IsBusy && _accidentEnCours != null && !_accidentEnCours.EstNouveau);
            ProposerSalaireCommand = new RelayCommand(ProposerSalaire, accidentOuvert);
            DeclareCnssAujourdhuiCommand = new RelayCommand(() => _accidentEnCours.DateDeclarationCnss = DateTime.Today,
                () => accidentOuvert() && !_accidentEnCours.DateDeclarationCnss.HasValue);
            DeclareInspectionAujourdhuiCommand = new RelayCommand(() => _accidentEnCours.DateDeclarationInspection = DateTime.Today,
                () => accidentOuvert() && !_accidentEnCours.DateDeclarationInspection.HasValue);
            ProgrammerRepriseCommand = new RelayCommand(ProgrammerReprise,
                () => accidentEnregistre() && _accidentEnCours.AvecArret && !_accidentEnCours.Mortel && _accidentEnCours.DebutArret.HasValue);

            NouvelleVisiteCommand = new RelayCommand(() => NouvelleVisite(null, TypeVisiteMedicale.Periodique, DateTime.Today, null), edition);
            ProgrammerVisiteSituationCommand = new RelayCommand(ProgrammerVisiteSituation, () => edition() && _situationSelectionnee != null);
            EnregistrerVisiteCommand = new RelayCommand(async () => await EnregistrerVisiteAsync(), () => edition() && _visiteEnCours != null);
            SupprimerVisiteCommand = new RelayCommand(async () => await SupprimerVisiteAsync(), () => edition() && _visiteEnCours != null && !_visiteEnCours.EstNouvelle);

            NouvelleDotationCommand = new RelayCommand(() => NouvelleDotation(_salarieDotation?.EmployeeId, 0), edition);
            EnregistrerDotationCommand = new RelayCommand(async () => await EnregistrerDotationAsync(), () => edition() && _dotationEnCours != null);
            SupprimerDotationCommand = new RelayCommand(async () => await SupprimerDotationAsync(), () => edition() && _dotationEnCours != null && !_dotationEnCours.EstNouvelle);
            RenouvelerDotationCommand = new RelayCommand(async () => await RenouvelerDotationAsync(),
                () => edition() && _dotationEnCours != null && !_dotationEnCours.EstNouvelle && _dotationEnCours.Etat == EtatDotation.EnService);
            FicheDotationCommand = new RelayCommand(async () => await FicheDotationAsync(),
                () => !IsBusy && _dotationEnCours != null && !string.IsNullOrWhiteSpace(_dotationEnCours.EmployeeId));

            EnregistrerParametresCommand = new RelayCommand(async () => await EnregistrerParametresAsync(), edition);
            EnregistrerPeriodicitesCommand = new RelayCommand(async () => await EnregistrerPeriodicitesAsync(), () => edition() && Periodicites.Count > 0);
            NouveauTypeEpiCommand = new RelayCommand(() =>
            {
                _synchronisation = true;
                try { _typeEpiSelectionne = null; OnPropertyChanged(nameof(TypeEpiSelectionne)); }
                finally { _synchronisation = false; }
                TypeEpiEnCours = new TypeEPI { DureeVieMois = 12, Actif = true };
            }, edition);
            EnregistrerTypeEpiCommand = new RelayCommand(async () => await EnregistrerTypeEpiAsync(), () => edition() && _typeEpiEnCours != null);
            SupprimerTypeEpiCommand = new RelayCommand(async () => await SupprimerTypeEpiAsync(), () => edition() && _typeEpiEnCours != null && _typeEpiEnCours.Id > 0);
        }

        /// <summary>
        /// Revenir sur l'écran recharge tout (un salarié a pu entrer ou sortir, une absence
        /// être saisie) ; le quitter avec un accident modifié propose de l'enregistrer.
        /// </summary>
        protected override void OnIsActiveChanged()
        {
            if (IsActive)
            {
                _ = ChargerToutAsync();
                return;
            }
            ProposerEnregistrementAccident();
        }

        private void ProposerEnregistrementAccident()
        {
            if (!_accidentModifie || _accidentEnCours == null || !PeutModifier) return;
            var nom = _accidentEnCours.EstNouveau ? "Le nouvel accident" : "L'accident " + _accidentEnCours.Reference;
            var reponse = MessageBox.Show(nom + " comporte des modifications non enregistrées.\n\nLes enregistrer ?",
                "Santé & sécurité au travail", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (reponse == MessageBoxResult.Yes)
                _ = EnregistrerAccidentAsync();
            else
                AccidentModifie = false;
        }

        // ═══════════════════════════════════════════════════════════════════
        //  Chargement
        // ═══════════════════════════════════════════════════════════════════

        public async Task ChargerToutAsync()
        {
            try
            {
                IsBusy = true;
                StatusMessage = "Chargement de la santé et sécurité au travail…";

                _parametres = await _service.ChargerParametresAsync();
                _feries = await _service.ChargerFeriesAsync();
                _salaries = (await _dataService.LoadEmployeesAsync())
                    .Where(s => s != null && !string.IsNullOrWhiteSpace(s.EmployeeId))
                    .OrderBy(s => s.FullName, StringComparer.CurrentCultureIgnoreCase)
                    .ToList();
                ParametresEnCours = _parametres.Clone();

                var periodicites = await _service.ChargerPeriodicitesAsync();
                Periodicites.Clear();
                foreach (var p in periodicites) Periodicites.Add(p);

                await RechargerTypesEpiAsync();
                await RechargerDonneesAsync();

                OnPropertyChanged(nameof(RappelLegal));
                OnPropertyChanged(nameof(Parametres));
                StatusMessage = Echeances.Count == 0
                    ? "Aucune échéance dans les " + _parametres.AlerteAvanceJours + " prochains jours."
                    : string.Format("{0} échéance(s) dans les {1} prochains jours, dont {2} en retard.", Echeances.Count, _parametres.AlerteAvanceJours, NombreEcheancesEnRetard);
            }
            catch (Exception ex)
            {
                StatusMessage = "Erreur au chargement : " + ex.Message;
            }
            finally
            {
                IsBusy = false;
            }
        }

        /// <summary>Recharge les registres et recalcule l'échéancier, en gardant les fiches ouvertes.</summary>
        private async Task RechargerDonneesAsync(int? accidentId = null, int? visiteId = null, int? dotationId = null)
        {
            var accidents = await _service.ChargerAccidentsAsync();
            var visites = await _service.ChargerVisitesAsync();
            var dotations = await _service.ChargerDotationsAsync();
            _arrets = await _service.ChargerArretsAsync();

            _synchronisation = true;
            try
            {
                // Salariés proposés : les actifs, et ceux qui figurent déjà dans un registre.
                var references = new HashSet<string>(accidents.Select(a => a.EmployeeId)
                    .Concat(visites.Select(v => v.EmployeeId)).Concat(dotations.Select(d => d.EmployeeId))
                    .Where(x => x != null), StringComparer.OrdinalIgnoreCase);
                Salaries.Clear();
                foreach (var s in _salaries.Where(s => s.EstActif || references.Contains(s.EmployeeId))) Salaries.Add(s);

                Accidents.Clear();
                foreach (var a in accidents) Accidents.Add(a);
                Visites.Clear();
                foreach (var v in visites) Visites.Add(v);
                Dotations.Clear();
                foreach (var d in dotations) Dotations.Add(d);

                // Accident : la fiche modifiée est conservée telle quelle.
                int? idAccident = accidentId ?? (_accidentEnCours != null && _accidentEnCours.Id > 0 ? _accidentEnCours.Id : (int?)null);
                _accidentSelectionne = idAccident.HasValue ? Accidents.FirstOrDefault(a => a.Id == idAccident.Value) : null;

                int? idVisite = visiteId ?? (_visiteEnCours != null && _visiteEnCours.Id > 0 ? _visiteEnCours.Id : (int?)null);
                _visiteSelectionnee = idVisite.HasValue ? Visites.FirstOrDefault(v => v.Id == idVisite.Value) : null;

                int? idDotation = dotationId ?? (_dotationEnCours != null && _dotationEnCours.Id > 0 ? _dotationEnCours.Id : (int?)null);
                _dotationSelectionnee = idDotation.HasValue ? Dotations.FirstOrDefault(d => d.Id == idDotation.Value) : null;
            }
            finally
            {
                _synchronisation = false;
            }

            OnPropertyChanged(nameof(AccidentSelectionne));
            OnPropertyChanged(nameof(VisiteSelectionnee));
            OnPropertyChanged(nameof(DotationSelectionnee));

            if (accidentId.HasValue || !_accidentModifie)
                AccidentEnCours = _accidentSelectionne?.Clone() ?? (_accidentEnCours != null && _accidentEnCours.EstNouveau ? _accidentEnCours : null);
            else
                ResynchroniserSalarie(ref _salarieAccident, _accidentEnCours?.EmployeeId, nameof(SalarieAccident));

            if (visiteId.HasValue || (_visiteEnCours != null && !_visiteEnCours.EstNouvelle))
                OuvrirVisite(_visiteSelectionnee?.Clone());
            else
                ResynchroniserSalarie(ref _salarieVisite, _visiteEnCours?.EmployeeId, nameof(SalarieVisite));

            if (dotationId.HasValue || (_dotationEnCours != null && !_dotationEnCours.EstNouvelle))
                OuvrirDotation(_dotationSelectionnee?.Clone());
            else
                ResynchroniserSalarie(ref _salarieDotation, _dotationEnCours?.EmployeeId, nameof(SalarieDotation));

            RecalculerEcheances();

            Annees.Clear();
            var annees = Accidents.Select(a => a.DateAccident.Year).Concat(new[] { DateTime.Today.Year }).Distinct().OrderByDescending(x => x);
            foreach (var an in annees) Annees.Add(an);
            if (!Annees.Contains(_anneeIndemnites)) _anneeIndemnites = DateTime.Today.Year;
            OnPropertyChanged(nameof(AnneeIndemnites));
            RecalculerIndemnites();

            VueAccidents.Refresh();
            VueVisites.Refresh();
            VueDotations.Refresh();
            NotifierIndicateurs();
        }

        private async Task RechargerTypesEpiAsync()
        {
            var types = await _service.ChargerTypesEpiAsync();
            _synchronisation = true;
            try
            {
                TypesEpi.Clear();
                foreach (var t in types) TypesEpi.Add(t);
                var id = _typeEpiEnCours?.Id;
                _typeEpiSelectionne = id.HasValue && id.Value > 0 ? TypesEpi.FirstOrDefault(t => t.Id == id.Value) : null;
                ReconstruireTypesProposes();
            }
            finally
            {
                _synchronisation = false;
            }
            OnPropertyChanged(nameof(TypeEpiSelectionne));
            if (_typeEpiSelectionne != null) TypeEpiEnCours = _typeEpiSelectionne.Clone();
        }

        private void ReconstruireTypesProposes()
        {
            var actuel = _dotationEnCours?.TypeEpiId ?? 0;
            TypesEpiProposes.Clear();
            foreach (var t in TypesEpi.Where(t => t.Actif || t.Id == actuel)) TypesEpiProposes.Add(t);
        }

        private void RecalculerEcheances()
        {
            var actifs = _salaries.Where(s => s.EstActif).ToList();
            var echeances = ControleSanteSecurite.Echeances(actifs, Visites, Periodicites, _arrets, Dotations, Accidents, _parametres, _feries, DateTime.Today);
            Echeances.Clear();
            foreach (var e in echeances) Echeances.Add(e);
            VueEcheances.Refresh();

            Situations.Clear();
            foreach (var s in ControleSanteSecurite.SituationsMedicales(actifs, Visites, Periodicites, _parametres, DateTime.Today)) Situations.Add(s);
            VueSituations.Refresh();
        }

        private void RecalculerIndemnites()
        {
            Indemnites.Clear();
            var mois = _moisIndemnites >= 1 && _moisIndemnites <= 12 ? new[] { _moisIndemnites } : Enumerable.Range(1, 12).ToArray();
            foreach (var m in mois)
                foreach (var l in ControleSanteSecurite.EtatIndemnites(Accidents, _anneeIndemnites, m, _parametres, DateTime.Today))
                    Indemnites.Add(l);
            OnPropertyChanged(nameof(TotalJoursIndemnises));
            OnPropertyChanged(nameof(TotalIndemnitesCnss));
            OnPropertyChanged(nameof(TotalPartEmployeur));
        }

        private void NotifierIndicateurs()
        {
            OnPropertyChanged(nameof(NombreEcheancesEnRetard));
            OnPropertyChanged(nameof(NombreVisitesAPasser));
            OnPropertyChanged(nameof(NombreAvecReserves));
            OnPropertyChanged(nameof(NombreEpiARenouveler));
            OnPropertyChanged(nameof(NombreAccidentsAnnee));
            OnPropertyChanged(nameof(NombreAccidentsADeclarer));
            OnPropertyChanged(nameof(JoursArretAnnee));
        }

        private Employee Trouver(string employeeId)
        {
            if (string.IsNullOrWhiteSpace(employeeId)) return null;
            return Salaries.FirstOrDefault(s => string.Equals(s.EmployeeId, employeeId, StringComparison.OrdinalIgnoreCase))
                   ?? _salaries.FirstOrDefault(s => string.Equals(s.EmployeeId, employeeId, StringComparison.OrdinalIgnoreCase));
        }

        private void ResynchroniserSalarie(ref Employee champ, string employeeId, string propriete)
        {
            champ = Trouver(employeeId);
            OnPropertyChanged(propriete);
        }

        private static bool Correspond(string a, string b, string c, string recherche)
        {
            if (string.IsNullOrWhiteSpace(recherche)) return true;
            var r = recherche.Trim();
            return Contient(a, r) || Contient(b, r) || Contient(c, r);
        }

        private static bool Contient(string texte, string recherche)
        {
            return texte != null && texte.IndexOf(recherche, StringComparison.CurrentCultureIgnoreCase) >= 0;
        }

        private bool FiltrerEcheance(object o)
        {
            var e = o as EcheanceSST;
            if (e == null) return false;
            if (_seulementEnRetard && !e.EstDepassee) return false;
            return _filtreCategorie == Toutes || string.Equals(e.Categorie, _filtreCategorie, StringComparison.Ordinal);
        }

        private bool FiltrerAccident(object o)
        {
            var a = o as AccidentTravail;
            if (a == null) return false;
            if (string.IsNullOrWhiteSpace(_rechercheAccident)) return true;
            var r = _rechercheAccident.Trim();
            return Contient(a.Reference, r) || Contient(a.EmployeeId, r) || Contient(a.NomSalarie, r) || Contient(a.Lieu, r) || Contient(a.NatureLibelle, r);
        }

        private bool FiltrerDotation(object o)
        {
            var d = o as DotationEPI;
            if (d == null) return false;
            if (_seulementEnService && d.Etat != EtatDotation.EnService) return false;
            return Correspond(d.EmployeeId, d.NomSalarie, d.TypeEpiLibelle, _rechercheDotation);
        }

        // ═══════════════════════════════════════════════════════════════════
        //  Échéancier
        // ═══════════════════════════════════════════════════════════════════

        private void TraiterEcheance()
        {
            var e = _echeanceSelectionnee;
            if (e == null) return;

            if (e.VisiteId.HasValue)
            {
                OngletActif = OngletVisites;
                VueVisitesIndex = 1;
                RechercheVisite = null;
                VisiteSelectionnee = Visites.FirstOrDefault(v => v.Id == e.VisiteId.Value);
                return;
            }
            if (e.Categorie == "Visite médicale")
            {
                OngletActif = OngletVisites;
                VueVisitesIndex = 1;
                if (!PeutModifier) return;
                NouvelleVisite(e.EmployeeId, e.TypeVisite ?? TypeVisiteMedicale.Periodique, e.Date < DateTime.Today ? DateTime.Today : e.Date, e.AccidentId);
                return;
            }
            if (e.DotationId.HasValue)
            {
                OngletActif = OngletEpi;
                SeulementEnService = false;
                RechercheDotation = null;
                DotationSelectionnee = Dotations.FirstOrDefault(d => d.Id == e.DotationId.Value);
                return;
            }
            if (e.AccidentId.HasValue)
            {
                OngletActif = OngletAccidents;
                RechercheAccident = null;
                AccidentSelectionne = Accidents.FirstOrDefault(a => a.Id == e.AccidentId.Value);
            }
        }

        // ═══════════════════════════════════════════════════════════════════
        //  Accidents du travail
        // ═══════════════════════════════════════════════════════════════════

        private void AccidentEnCours_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(AccidentTravail.ArretLibelle) || e.PropertyName == nameof(AccidentTravail.DeclarationLibelle)
                || e.PropertyName == nameof(AccidentTravail.NatureLibelle))
                return;
            if (!AccidentModifie) AccidentModifie = true;

            var a = _accidentEnCours;
            if (a != null && e.PropertyName == nameof(AccidentTravail.AvecArret) && a.AvecArret && !a.DebutArret.HasValue)
                a.DebutArret = a.DateAccident.AddDays(1); // en principe, l'arrêt commence le lendemain
            NotifierAccident();
        }

        private void NotifierAccident()
        {
            OnPropertyChanged(nameof(AccidentOuvert));
            OnPropertyChanged(nameof(SalarieAccidentModifiable));
            OnPropertyChanged(nameof(ArretSaisissable));
            OnPropertyChanged(nameof(TitreAccident));
            OnPropertyChanged(nameof(DateLimiteLibelle));
            OnPropertyChanged(nameof(SalaireProposeLibelle));
            RecalculerAlertesAccident();
        }

        private void RecalculerAlertesAccident()
        {
            AlertesAccident.Clear();
            if (_accidentEnCours != null)
                foreach (var al in ControleSanteSecurite.AnalyserAccident(_accidentEnCours, _parametres, _feries, Visites, DateTime.Today))
                    AlertesAccident.Add(al);
            OnPropertyChanged(nameof(NombreBloquantsAccident));
        }

        private void NouvelAccident()
        {
            ProposerEnregistrementAccident();
            _synchronisation = true;
            try { _accidentSelectionne = null; }
            finally { _synchronisation = false; }
            OnPropertyChanged(nameof(AccidentSelectionne));
            AccidentEnCours = new AccidentTravail { DateAccident = DateTime.Today };
            AccidentModifie = true;
            OngletActif = OngletAccidents;
            StatusMessage = "Nouvel accident : choisissez la victime, décrivez l'accident, puis « Enregistrer ». Déclarez-le dans les 48 heures ouvrables.";
        }

        private void AnnulerAccident()
        {
            AccidentModifie = false;
            AccidentEnCours = _accidentSelectionne?.Clone();
            StatusMessage = "Modifications abandonnées.";
        }

        private void ProposerSalaire()
        {
            var a = _accidentEnCours;
            var s = a == null ? null : Trouver(a.EmployeeId);
            if (s == null) { StatusMessage = "Choisissez d'abord la victime."; return; }
            a.SalaireJournalier = ControleSanteSecurite.SalaireJournalierPropose(s, _parametres);
        }

        private async Task<bool> EnregistrerAccidentAsync()
        {
            // La fiche est capturée tout de suite : si l'enregistrement a été proposé en
            // quittant la fiche, la sélection change pendant qu'il s'exécute.
            var fiche = _accidentEnCours;
            if (fiche == null) return false;
            if (!PeutModifier) { StatusMessage = "Votre profil ne permet pas de modifier le registre des accidents."; return false; }

            var bloquant = ControleSanteSecurite.PremierBlocage(ControleSanteSecurite.AnalyserAccident(fiche, _parametres, _feries, Visites, DateTime.Today));
            if (bloquant != null)
            {
                StatusMessage = "Enregistrement refusé : " + bloquant;
                MessageBox.Show(bloquant, "Accident du travail", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }

            try
            {
                IsBusy = true;
                bool nouveau = fiche.EstNouveau;
                var messages = await _service.EnregistrerAccidentAsync(fiche, Visites.ToList());
                bool toujoursAffiche = ReferenceEquals(fiche, _accidentEnCours);
                if (toujoursAffiche) AccidentModifie = false;
                await RechargerDonneesAsync(toujoursAffiche ? fiche.Id : (int?)null);
                var resume = (nouveau ? "Accident enregistré sous la référence " : "Accident ") + fiche.Reference + (nouveau ? "." : " enregistré.");
                if (messages.Count > 0) resume += " " + string.Join(" ", messages);
                StatusMessage = resume;
                return true;
            }
            catch (Exception ex)
            {
                StatusMessage = "Enregistrement impossible : " + ex.Message;
                MessageBox.Show(ex.Message, "Accident du travail", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }
            finally
            {
                IsBusy = false;
            }
        }

        private async Task SupprimerAccidentAsync()
        {
            var a = _accidentSelectionne;
            if (a == null) return;
            var ok = MessageBox.Show(
                "Supprimer l'accident " + a.Reference + " (" + a.NomSalarie + ") ?" +
                (a.AbsenceId.HasValue ? "\n\nL'absence « Accident du travail » qu'il a créée sera retirée des congés et absences." : ""),
                "Santé & sécurité au travail", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (ok != MessageBoxResult.Yes) return;

            try
            {
                IsBusy = true;
                await _service.SupprimerAccidentAsync(a);
                AccidentModifie = false;
                _accidentSelectionne = null;
                OnPropertyChanged(nameof(AccidentSelectionne));
                AccidentEnCours = null;
                await RechargerDonneesAsync();
                StatusMessage = "Accident " + a.Reference + " supprimé.";
            }
            catch (Exception ex)
            {
                StatusMessage = "Suppression impossible : " + ex.Message;
                MessageBox.Show(ex.Message, "Santé & sécurité au travail", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            finally
            {
                IsBusy = false;
            }
        }

        /// <summary>Fiche de déclaration (PDF) ; une fiche modifiée est d'abord enregistrée.</summary>
        private async Task FicheAccidentAsync()
        {
            if (_accidentEnCours == null) return;
            if (_accidentModifie && !await EnregistrerAccidentAsync()) return;
            var a = _accidentEnCours;
            var manque = SanteSecuriteExportService.PrerequisFiche(a);
            if (manque != null) { StatusMessage = manque; return; }

            var dlg = new SaveFileDialog
            {
                Title = "Fiche de déclaration de l'accident " + a.Reference,
                Filter = "Document PDF (*.pdf)|*.pdf",
                FileName = NomFichier("Declaration_" + a.Reference + "_" + (a.NomSalarie ?? a.EmployeeId)) + ".pdf"
            };
            if (dlg.ShowDialog() != true) return;

            try
            {
                IsBusy = true;
                var salarie = Trouver(a.EmployeeId);
                var prm = _parametres;
                var feries = _feries;
                await Task.Run(() => _export.GenererFicheAccident(a, salarie, prm, feries, dlg.FileName));
                StatusMessage = "Fiche produite : reportez ces éléments sur l'imprimé de la CNSS (deux exemplaires : CNSS et inspection du travail), puis saisissez les dates de déclaration.";
                Ouvrir(dlg.FileName);
            }
            catch (Exception ex)
            {
                StatusMessage = "Fiche non produite : " + ex.Message;
            }
            finally
            {
                IsBusy = false;
            }
        }

        /// <summary>Programme la visite de reprise : le lendemain de la fin d'arrêt (ou à la reprise).</summary>
        private void ProgrammerReprise()
        {
            var a = _accidentEnCours;
            if (a == null) return;
            var date = a.DateReprise ?? (a.FinArret.HasValue ? a.FinArret.Value.AddDays(1) : DateTime.Today);
            OngletActif = OngletVisites;
            VueVisitesIndex = 1;
            NouvelleVisite(a.EmployeeId, TypeVisiteMedicale.Reprise, date, a.Id);
        }

        // ═══════════════════════════════════════════════════════════════════
        //  Visites médicales
        // ═══════════════════════════════════════════════════════════════════

        private void OuvrirVisite(VisiteMedicale v)
        {
            VisiteEnCours = v;
            ResynchroniserSalarie(ref _salarieVisite, v?.EmployeeId, nameof(SalarieVisite));
        }

        private void NouvelleVisite(string employeeId, TypeVisiteMedicale type, DateTime datePrevue, int? accidentId)
        {
            _synchronisation = true;
            try { _visiteSelectionnee = null; }
            finally { _synchronisation = false; }
            OnPropertyChanged(nameof(VisiteSelectionnee));
            var s = Trouver(employeeId);
            OuvrirVisite(new VisiteMedicale
            {
                EmployeeId = s?.EmployeeId,
                NomSalarie = s?.FullName,
                Type = type,
                DatePrevue = datePrevue.Date,
                AccidentId = accidentId
            });
            StatusMessage = s == null
                ? "Nouvelle visite : choisissez le salarié et la date de convocation ; après la visite, saisissez sa date et l'avis du médecin."
                : LibellesSST.Visite(type) + " de " + s.FullName + " : vérifiez la date de convocation, puis « Enregistrer ».";
        }

        private void ProgrammerVisiteSituation()
        {
            var s = _situationSelectionnee;
            if (s == null) return;
            var programmee = Visites.Where(v => !v.DateVisite.HasValue && v.DatePrevue.HasValue
                                                && string.Equals(v.EmployeeId, s.EmployeeId, StringComparison.OrdinalIgnoreCase))
                                    .OrderBy(v => v.DatePrevue.Value).FirstOrDefault();
            VueVisitesIndex = 1;
            if (programmee != null)
            {
                RechercheVisite = null;
                VisiteSelectionnee = programmee;
                StatusMessage = "Visite déjà programmée : après la visite, saisissez sa date et l'avis du médecin.";
                return;
            }
            NouvelleVisite(s.EmployeeId, s.TypeProchaine, s.ProchaineEcheance < DateTime.Today ? DateTime.Today : s.ProchaineEcheance, null);
        }

        private async Task EnregistrerVisiteAsync()
        {
            var v = _visiteEnCours;
            if (v == null) return;
            try
            {
                IsBusy = true;
                await _service.EnregistrerVisiteAsync(v);
                await RechargerDonneesAsync(visiteId: v.Id);
                StatusMessage = v.TypeLibelle + " de " + (v.NomSalarie ?? v.EmployeeId) + (v.EstRealisee ? " enregistrée." : " programmée le " + LibellesSST.Date(v.DatePrevue) + ".");
            }
            catch (Exception ex)
            {
                StatusMessage = "Visite non enregistrée : " + ex.Message;
                MessageBox.Show(ex.Message, "Visite médicale", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            finally
            {
                IsBusy = false;
            }
        }

        private async Task SupprimerVisiteAsync()
        {
            var v = _visiteEnCours;
            if (v == null || v.EstNouvelle) return;
            if (MessageBox.Show("Supprimer cette " + v.TypeLibelle.ToLower(LibellesSST.Fr) + " de " + (v.NomSalarie ?? v.EmployeeId) + " ?",
                    "Visite médicale", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
            try
            {
                IsBusy = true;
                await _service.SupprimerVisiteAsync(v);
                OuvrirVisite(null);
                await RechargerDonneesAsync();
                StatusMessage = "Visite supprimée.";
            }
            catch (Exception ex)
            {
                StatusMessage = "Visite non supprimée : " + ex.Message;
            }
            finally
            {
                IsBusy = false;
            }
        }

        // ═══════════════════════════════════════════════════════════════════
        //  EPI
        // ═══════════════════════════════════════════════════════════════════

        private void OuvrirDotation(DotationEPI d)
        {
            DotationEnCours = d;
            ReconstruireTypesProposes();
            ResynchroniserSalarie(ref _salarieDotation, d?.EmployeeId, nameof(SalarieDotation));
        }

        private void NouvelleDotation(string employeeId, int typeEpiId, string taille = null, int quantite = 1)
        {
            _synchronisation = true;
            try { _dotationSelectionnee = null; }
            finally { _synchronisation = false; }
            OnPropertyChanged(nameof(DotationSelectionnee));
            var s = Trouver(employeeId);
            OuvrirDotation(new DotationEPI
            {
                EmployeeId = s?.EmployeeId,
                NomSalarie = s?.FullName,
                TypeEpiId = typeEpiId,
                DateRemise = DateTime.Today,
                Quantite = quantite > 0 ? quantite : 1,
                Taille = taille
            });
            StatusMessage = "Nouvelle remise : choisissez le salarié et l'équipement ; la date de renouvellement est calculée d'après la durée de vie de l'EPI.";
        }

        private async Task EnregistrerDotationAsync()
        {
            var d = _dotationEnCours;
            if (d == null) return;
            try
            {
                IsBusy = true;
                await _service.EnregistrerDotationAsync(d);
                await RechargerDonneesAsync(dotationId: d.Id);
                StatusMessage = string.Format("{0} × {1} remis à {2}{3}.", d.Quantite, d.TypeEpiLibelle, d.NomSalarie ?? d.EmployeeId,
                    d.Etat == EtatDotation.EnService && d.DateRenouvellement.HasValue ? ", à renouveler le " + LibellesSST.Date(d.DateRenouvellement) : "");
            }
            catch (Exception ex)
            {
                StatusMessage = "Remise non enregistrée : " + ex.Message;
                MessageBox.Show(ex.Message, "Équipements de protection", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            finally
            {
                IsBusy = false;
            }
        }

        private async Task SupprimerDotationAsync()
        {
            var d = _dotationEnCours;
            if (d == null || d.EstNouvelle) return;
            if (MessageBox.Show("Supprimer la remise de « " + d.TypeEpiLibelle + " » à " + (d.NomSalarie ?? d.EmployeeId) + " ?\n\n" +
                                "Pour un équipement rendu, perdu ou usé, changez plutôt son état : l'historique des remises reste utile.",
                    "Équipements de protection", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
            try
            {
                IsBusy = true;
                await _service.SupprimerDotationAsync(d);
                OuvrirDotation(null);
                await RechargerDonneesAsync();
                StatusMessage = "Remise supprimée.";
            }
            catch (Exception ex)
            {
                StatusMessage = "Remise non supprimée : " + ex.Message;
            }
            finally
            {
                IsBusy = false;
            }
        }

        /// <summary>Renouvellement : l'équipement en service est rendu aujourd'hui, un neuf est remis.</summary>
        private async Task RenouvelerDotationAsync()
        {
            var ancienne = _dotationEnCours;
            if (ancienne == null || ancienne.EstNouvelle || ancienne.Etat != EtatDotation.EnService) return;
            if (MessageBox.Show("Renouveler « " + ancienne.TypeEpiLibelle + " » de " + (ancienne.NomSalarie ?? ancienne.EmployeeId) + " ?\n\n" +
                                "L'équipement actuel sera noté « Rendu » aujourd'hui et une nouvelle remise sera préparée.",
                    "Équipements de protection", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
            try
            {
                IsBusy = true;
                var rendu = ancienne.Clone();
                rendu.Etat = EtatDotation.Rendu;
                rendu.DateRetour = DateTime.Today < rendu.DateRemise ? rendu.DateRemise : DateTime.Today;
                await _service.EnregistrerDotationAsync(rendu);
                await RechargerDonneesAsync(dotationId: rendu.Id);
                NouvelleDotation(ancienne.EmployeeId, ancienne.TypeEpiId, ancienne.Taille, ancienne.Quantite);
                StatusMessage = "Ancien équipement noté « Rendu ». Vérifiez la nouvelle remise, puis « Enregistrer ».";
            }
            catch (Exception ex)
            {
                StatusMessage = "Renouvellement impossible : " + ex.Message;
            }
            finally
            {
                IsBusy = false;
            }
        }

        /// <summary>Fiche de dotation à faire signer : tous les EPI en service du salarié.</summary>
        private async Task FicheDotationAsync()
        {
            var d = _dotationEnCours;
            if (d == null || string.IsNullOrWhiteSpace(d.EmployeeId)) return;
            var enService = Dotations.Where(x => x.Etat == EtatDotation.EnService
                                                 && string.Equals(x.EmployeeId, d.EmployeeId, StringComparison.OrdinalIgnoreCase)).ToList();
            if (enService.Count == 0)
            {
                StatusMessage = "Aucun équipement en service enregistré pour ce salarié : enregistrez d'abord la remise.";
                return;
            }

            var dlg = new SaveFileDialog
            {
                Title = "Fiche de dotation en EPI",
                Filter = "Document PDF (*.pdf)|*.pdf",
                FileName = NomFichier("Dotation_EPI_" + (d.NomSalarie ?? d.EmployeeId) + "_" + DateTime.Today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)) + ".pdf"
            };
            if (dlg.ShowDialog() != true) return;

            try
            {
                IsBusy = true;
                var salarie = Trouver(d.EmployeeId);
                await Task.Run(() => _export.GenererDechargeEpi(salarie, d.EmployeeId, d.NomSalarie, enService, dlg.FileName));
                StatusMessage = "Fiche de dotation produite (" + enService.Count + " équipement(s)) : faites-la signer par le salarié et classez-la dans son dossier.";
                Ouvrir(dlg.FileName);
            }
            catch (Exception ex)
            {
                StatusMessage = "Fiche non produite : " + ex.Message;
            }
            finally
            {
                IsBusy = false;
            }
        }

        // ═══════════════════════════════════════════════════════════════════
        //  Export
        // ═══════════════════════════════════════════════════════════════════

        private async Task ExporterAsync()
        {
            var dlg = new SaveFileDialog
            {
                Title = "Exporter le registre santé & sécurité",
                Filter = "Classeur Excel (*.xlsx)|*.xlsx",
                FileName = string.Format(CultureInfo.InvariantCulture, "Registre_SST_{0:yyyy-MM-dd}.xlsx", DateTime.Today)
            };
            if (dlg.ShowDialog() != true) return;

            try
            {
                IsBusy = true;
                var accidents = Accidents.ToList();
                var annee = _anneeIndemnites;
                var indemnites = Enumerable.Range(1, 12)
                    .SelectMany(m => ControleSanteSecurite.EtatIndemnites(accidents, annee, m, _parametres, DateTime.Today)).ToList();
                var visites = Visites.ToList();
                var dotations = Dotations.ToList();
                var echeances = Echeances.ToList();
                await Task.Run(() => _export.ExporterRegistre(dlg.FileName, accidents, indemnites, annee, visites, dotations, echeances));
                StatusMessage = "Registre exporté : " + dlg.FileName;
                Ouvrir(dlg.FileName);
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

        private static string NomFichier(string nom)
        {
            foreach (var c in Path.GetInvalidFileNameChars()) nom = nom.Replace(c, '_');
            return nom.Replace(' ', '_');
        }

        private void Ouvrir(string chemin)
        {
            try
            {
                if (File.Exists(chemin)) Process.Start(new ProcessStartInfo(chemin) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                StatusMessage = "Ouverture impossible : " + ex.Message;
            }
        }

        // ═══════════════════════════════════════════════════════════════════
        //  Paramètres, périodicités, catalogue
        // ═══════════════════════════════════════════════════════════════════

        private async Task EnregistrerParametresAsync()
        {
            try
            {
                IsBusy = true;
                await _service.EnregistrerParametresAsync(_parametresEnCours);
                _parametres = _parametresEnCours.Clone();
                RecalculerEcheances();
                RecalculerIndemnites();
                NotifierAccident();
                NotifierIndicateurs();
                OnPropertyChanged(nameof(RappelLegal));
                OnPropertyChanged(nameof(Parametres));
                StatusMessage = "Paramètres de santé et sécurité enregistrés.";
            }
            catch (Exception ex)
            {
                StatusMessage = "Paramètres non enregistrés : " + ex.Message;
                MessageBox.Show(ex.Message, "Paramètres santé & sécurité", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            finally
            {
                IsBusy = false;
            }
        }

        private async Task EnregistrerPeriodicitesAsync()
        {
            try
            {
                IsBusy = true;
                await _service.EnregistrerPeriodicitesAsync(Periodicites);
                RecalculerEcheances();
                NotifierIndicateurs();
                StatusMessage = "Périodicités par poste enregistrées (" + Periodicites.Count(p => p.Mois.HasValue) + " poste(s) à périodicité propre).";
            }
            catch (Exception ex)
            {
                StatusMessage = "Périodicités non enregistrées : " + ex.Message;
                MessageBox.Show(ex.Message, "Visites médicales par poste", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            finally
            {
                IsBusy = false;
            }
        }

        private async Task EnregistrerTypeEpiAsync()
        {
            var t = _typeEpiEnCours;
            if (t == null) return;
            try
            {
                IsBusy = true;
                await _service.EnregistrerTypeEpiAsync(t);
                await RechargerTypesEpiAsync();
                _synchronisation = true;
                try { _typeEpiSelectionne = TypesEpi.FirstOrDefault(x => x.Id == t.Id); }
                finally { _synchronisation = false; }
                OnPropertyChanged(nameof(TypeEpiSelectionne));
                await RechargerDonneesAsync();
                StatusMessage = "« " + t.Libelle + " » enregistré au catalogue des EPI.";
            }
            catch (Exception ex)
            {
                StatusMessage = "Équipement non enregistré : " + ex.Message;
                MessageBox.Show(ex.Message, "Catalogue des EPI", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            finally
            {
                IsBusy = false;
            }
        }

        private async Task SupprimerTypeEpiAsync()
        {
            var t = _typeEpiEnCours;
            if (t == null || t.Id <= 0) return;
            if (MessageBox.Show("Supprimer « " + t.Libelle + " » du catalogue ?", "Catalogue des EPI",
                    MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
            try
            {
                IsBusy = true;
                await _service.SupprimerTypeEpiAsync(t);
                TypeEpiEnCours = null;
                await RechargerTypesEpiAsync();
                StatusMessage = "Équipement retiré du catalogue.";
            }
            catch (Exception ex)
            {
                StatusMessage = ex.Message;
                MessageBox.Show(ex.Message, "Catalogue des EPI", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            finally
            {
                IsBusy = false;
            }
        }
    }
}
