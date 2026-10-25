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
    /// Écran « Discipline & procédures » (module RH-06) : procédures disciplinaires de la
    /// constatation des faits à la notification, contrôles de procédure en direct,
    /// lettres, pièces, échéancier, registre des sanctions et paramètres du dossier.
    /// </summary>
    public class DisciplineViewModel : BaseViewModel
    {
        private readonly DisciplineService _service;
        private readonly DisciplineExportService _export;
        private readonly DataService _dataService;

        private ParametresDiscipline _parametres = ParametresDiscipline.Defaut();
        private HashSet<DateTime> _feries = new HashSet<DateTime>();
        private List<Employee> _salaries = new List<Employee>();

        private ProcedureDisciplinaire _procedureSelectionnee;
        private ProcedureDisciplinaire _procedureEnCours;
        private bool _modifiee;
        private Employee _salarieChoisi;
        private PieceDisciplinaire _pieceSelectionnee;
        private TypePiece _typeNouvellePiece = TypePiece.ConstatFaits;
        private string _recherche;
        private string _filtreStatut = FiltreToutes;
        private int _ongletActif;
        private EcheanceDiscipline _echeanceSelectionnee;
        private LigneRegistreSanction _ligneRegistreSelectionnee;
        private bool _afficherEffacees = true;

        // Pendant qu'une liste liée est reconstruite, les listes déroulantes et la grille
        // renvoient une sélection vide : elle ne doit pas être prise pour une saisie.
        private bool _synchronisation;

        private ParametresDiscipline _parametresEnCours = ParametresDiscipline.Defaut();
        private TypeSanction _typeCatalogueSelectionne;
        private TypeSanction _typeCatalogueEnCours;

        private MandatRepresentatif _mandatSelectionne;
        private MandatRepresentatif _mandatEnCours;
        private Employee _salarieMandatChoisi;

        private const string FiltreToutes = "Toutes les procédures";
        private const string FiltreEnCours = "En cours";
        private const string FiltreANotifier = "Décision à notifier";
        private const string FiltreNotifiees = "Notifiées";
        private const string FiltreClassees = "Classées sans suite";

        // ── Collections ──────────────────────────────────────────────────

        public ObservableCollection<ProcedureDisciplinaire> Procedures { get; } = new ObservableCollection<ProcedureDisciplinaire>();
        public ObservableCollection<TypeSanction> Types { get; } = new ObservableCollection<TypeSanction>();
        public ObservableCollection<TypeSanction> TypesProposes { get; } = new ObservableCollection<TypeSanction>();
        public ObservableCollection<Employee> SalariesProposes { get; } = new ObservableCollection<Employee>();
        public ObservableCollection<AlerteDiscipline> Alertes { get; } = new ObservableCollection<AlerteDiscipline>();
        public ObservableCollection<PieceDisciplinaire> Pieces { get; } = new ObservableCollection<PieceDisciplinaire>();
        public ObservableCollection<PieceExigee> PiecesExigees { get; } = new ObservableCollection<PieceExigee>();
        public ObservableCollection<EcheanceDiscipline> Echeances { get; } = new ObservableCollection<EcheanceDiscipline>();
        public ObservableCollection<LigneRegistreSanction> Registre { get; } = new ObservableCollection<LigneRegistreSanction>();
        public ObservableCollection<MandatRepresentatif> Mandats { get; } = new ObservableCollection<MandatRepresentatif>();
        public ObservableCollection<Employee> SalariesRegistre { get; } = new ObservableCollection<Employee>();
        public IEnumerable<string> MandatsProposes => MandatRepresentatif.MandatsProposes;

        public ICollectionView VueProcedures { get; }
        public ICollectionView VueRegistre { get; }

        public IEnumerable<string> FiltresStatut { get; } = new[] { FiltreToutes, FiltreEnCours, FiltreANotifier, FiltreNotifiees, FiltreClassees };
        public IEnumerable<string> Categories => LibellesDiscipline.CategoriesFaits;
        public IEnumerable<string> ModesNotification => LibellesDiscipline.ModesNotification;
        public IEnumerable<string> InstancesRecours { get; } = new[] { "Inspection du travail (conciliation)", "Tribunal du travail", "Recours gracieux auprès de l'employeur" };

        public IEnumerable<KeyValuePair<GraviteFaute, string>> Gravites { get; } =
            Enum.GetValues(typeof(GraviteFaute)).Cast<GraviteFaute>()
                .Select(g => new KeyValuePair<GraviteFaute, string>(g, LibellesDiscipline.Gravite(g))).ToList();

        public IEnumerable<KeyValuePair<DecisionInspection, string>> DecisionsInspection { get; } =
            Enum.GetValues(typeof(DecisionInspection)).Cast<DecisionInspection>()
                .Select(d => new KeyValuePair<DecisionInspection, string>(d, LibellesDiscipline.Inspection(d))).ToList();

        public IEnumerable<KeyValuePair<NatureSanction, string>> Natures { get; } =
            Enum.GetValues(typeof(NatureSanction)).Cast<NatureSanction>()
                .Select(n => new KeyValuePair<NatureSanction, string>(n, LibellesDiscipline.Nature(n))).ToList();

        public IEnumerable<KeyValuePair<TypePiece, string>> TypesPiece { get; } =
            Enum.GetValues(typeof(TypePiece)).Cast<TypePiece>()
                .Select(t => new KeyValuePair<TypePiece, string>(t, LibellesDiscipline.Piece(t))).ToList();

        // ── État de l'écran ──────────────────────────────────────────────

        public bool PeutModifier => UserSession.Instance.CanEdit;

        public int OngletActif
        {
            get => _ongletActif;
            set => SetProperty(ref _ongletActif, value);
        }

        public string Recherche
        {
            get => _recherche;
            set { if (SetProperty(ref _recherche, value)) VueProcedures.Refresh(); }
        }

        public string FiltreStatut
        {
            get => _filtreStatut;
            set { if (SetProperty(ref _filtreStatut, value)) VueProcedures.Refresh(); }
        }

        public bool AfficherEffacees
        {
            get => _afficherEffacees;
            set { if (SetProperty(ref _afficherEffacees, value)) VueRegistre.Refresh(); }
        }

        public int NombreEnCours => Procedures.Count(p => !p.EstClose);
        public int NombreANotifier => Procedures.Count(p => p.Statut == StatutProcedure.DecisionPrise);
        public int NombreEcheancesDepassees => Echeances.Count(e => e.EstDepassee);
        public int NombreSanctionsAnnee => Registre.Count(r => r.DateNotification.Year == DateTime.Today.Year);
        public int NombreMisesAPiedAnnee => Registre.Count(r => r.DateNotification.Year == DateTime.Today.Year && r.Nature == NatureSanction.MiseAPied);

        // ── Procédure sélectionnée / éditée ──────────────────────────────

        public ProcedureDisciplinaire ProcedureSelectionnee
        {
            get => _procedureSelectionnee;
            set
            {
                if (_procedureSelectionnee == value || _synchronisation) return;
                ProposerEnregistrement();
                if (SetProperty(ref _procedureSelectionnee, value))
                    _ = OuvrirAsync(value);
            }
        }

        /// <summary>Copie éditée : la liste ne change qu'à l'enregistrement.</summary>
        public ProcedureDisciplinaire ProcedureEnCours
        {
            get => _procedureEnCours;
            private set
            {
                if (_procedureEnCours != null) _procedureEnCours.PropertyChanged -= ProcedureEnCours_PropertyChanged;
                if (SetProperty(ref _procedureEnCours, value))
                {
                    if (_procedureEnCours != null) _procedureEnCours.PropertyChanged += ProcedureEnCours_PropertyChanged;
                    Modifiee = false;
                    _synchronisation = true;
                    try
                    {
                        ReconstruireTypesProposes();
                        ReconstruireSalariesProposes();
                    }
                    finally
                    {
                        _synchronisation = false;
                    }
                    _salarieChoisi = _procedureEnCours == null ? null
                        : _salaries.FirstOrDefault(s => string.Equals(s.EmployeeId, _procedureEnCours.EmployeeId, StringComparison.OrdinalIgnoreCase));
                    OnPropertyChanged(nameof(SalarieChoisi));
                    OnPropertyChanged(nameof(TypeSanctionChoisi));
                    NotifierEtat();
                }
            }
        }

        public bool Modifiee
        {
            get => _modifiee;
            private set { if (SetProperty(ref _modifiee, value)) OnPropertyChanged(nameof(TitreFiche)); }
        }

        public bool ProcedureOuverte => _procedureEnCours != null;
        public bool EstNouvelle => _procedureEnCours != null && _procedureEnCours.EstNouvelle;
        public bool SalarieModifiable => EstNouvelle && PeutModifier;

        public string TitreFiche
        {
            get
            {
                if (_procedureEnCours == null) return "Sélectionnez une procédure ou ouvrez-en une nouvelle.";
                var t = _procedureEnCours.EstNouvelle ? "Nouvelle procédure" : _procedureEnCours.Reference + " — " + _procedureEnCours.StatutLibelle;
                return _modifiee ? t + " (modifiée)" : t;
            }
        }

        public Employee SalarieChoisi
        {
            get => _salarieChoisi;
            set
            {
                if (_synchronisation) return;
                if (!SetProperty(ref _salarieChoisi, value) || _procedureEnCours == null || !_procedureEnCours.EstNouvelle) return;
                _procedureEnCours.EmployeeId = value?.EmployeeId;
                _procedureEnCours.NomSalarie = value?.FullName;
                _procedureEnCours.Fonction = value?.Fonction;

                // Registre des salariés protégés : la qualité se reporte d'elle-même.
                var mandat = ControleDiscipline.MandatApplicable(Mandats, _procedureEnCours, DateTime.Today);
                if (mandat != null)
                {
                    _procedureEnCours.SalarieProtege = true;
                    _procedureEnCours.MandatProtege = mandat.Mandat;
                    StatusMessage = "Salarié protégé (" + mandat.Mandat + ", " + mandat.PeriodeLibelle + ") : un licenciement exigera l'autorisation de l'inspecteur du travail.";
                }
                RecalculerAlertes();
            }
        }

        public TypeSanction TypeSanctionChoisi
        {
            get => _procedureEnCours?.TypeSanction;
            set
            {
                if (_procedureEnCours == null || _synchronisation) return;
                _procedureEnCours.TypeSanction = value;
                _procedureEnCours.TypeSanctionId = value?.Id;
                _procedureEnCours.Notifier(nameof(ProcedureDisciplinaire.SanctionLibelle));
                OnPropertyChanged();
                NotifierEtat();
            }
        }

        public bool EstMiseAPied => _procedureEnCours?.TypeSanction != null && _procedureEnCours.TypeSanction.EstMiseAPied;
        public bool EstLicenciement => _procedureEnCours?.TypeSanction != null && _procedureEnCours.TypeSanction.EstLicenciement;
        public bool AfficherInspection => _procedureEnCours != null && (_procedureEnCours.SalarieProtege || _procedureEnCours.DecisionInspection != DecisionInspection.SansObjet);
        public bool AfficherClassement => _procedureEnCours != null && _procedureEnCours.ClasseeSansSuite;

        public string ExigencesSanction
        {
            get
            {
                var t = _procedureEnCours?.TypeSanction;
                if (t == null) return "Choisissez la sanction envisagée : elle détermine les étapes exigées.";
                var e = new List<string>();
                e.Add(t.ExigeExplication ? "demande d'explication écrite" : "explication écrite facultative");
                if (t.ExigeEntretien) e.Add("entretien préalable");
                if (t.EstMiseAPied && t.DureeMaxJours > 0) e.Add(string.Format("{0} jours au plus", t.DureeMaxJours));
                if (t.EstLicenciement) e.Add(t.FauteLourde ? "faute lourde : sans préavis ni indemnité" : "préavis et indemnité dus");
                return "Exigences : " + string.Join(", ", e) + ".";
            }
        }

        public string DateLimiteReponseLibelle
        {
            get
            {
                var d = ControleDiscipline.DateLimiteReponse(_procedureEnCours, _parametres, _feries);
                return d.HasValue
                    ? string.Format(LibellesDiscipline.Fr, "Réponse attendue au plus tard le {0:dddd dd/MM/yyyy} ({1} j ouvrables).", d.Value, _parametres.DelaiReponseJoursOuvrables)
                    : string.Format("Délai de réponse : {0} jour(s) ouvrable(s) après la remise de la demande.", _parametres.DelaiReponseJoursOuvrables);
            }
        }

        public string PremiereDateEntretienLibelle
        {
            get
            {
                var d = ControleDiscipline.PremiereDateEntretien(_procedureEnCours?.DateConvocation, _parametres, _feries);
                return d.HasValue
                    ? string.Format(LibellesDiscipline.Fr, "Entretien possible à partir du {0:dddd dd/MM/yyyy} ({1} j ouvrables pleins après la convocation).", d.Value, _parametres.DelaiConvocationJoursOuvrables)
                    : string.Format("Au moins {0} jour(s) ouvrable(s) entre la convocation et l'entretien.", _parametres.DelaiConvocationJoursOuvrables);
            }
        }

        public string JoursMiseAPiedProposesLibelle
        {
            get
            {
                var p = _procedureEnCours;
                if (p == null || !p.MiseAPiedDebut.HasValue || !p.MiseAPiedFin.HasValue || p.MiseAPiedFin < p.MiseAPiedDebut) return "";
                return string.Format(LibellesDiscipline.Fr, "{0:0.##} jour(s) dans la période, dimanches exclus.",
                    ControleDiscipline.JoursMiseAPiedProposes(p.MiseAPiedDebut.Value, p.MiseAPiedFin.Value));
            }
        }

        public string EtapeSuivante => _procedureEnCours == null ? "" :
            ControleDiscipline.EtapeSuivante(_procedureEnCours, _procedureEnCours.TypeSanction, _parametres, _feries, DateTime.Today);

        public int NombreBloquants => Alertes.Count(a => a.Niveau == NiveauAlerte.Bloquant);

        // ── Pièces ───────────────────────────────────────────────────────

        public PieceDisciplinaire PieceSelectionnee
        {
            get => _pieceSelectionnee;
            set => SetProperty(ref _pieceSelectionnee, value);
        }

        public TypePiece TypeNouvellePiece
        {
            get => _typeNouvellePiece;
            set => SetProperty(ref _typeNouvellePiece, value);
        }

        // ── Échéancier & registre ────────────────────────────────────────

        public EcheanceDiscipline EcheanceSelectionnee
        {
            get => _echeanceSelectionnee;
            set { if (SetProperty(ref _echeanceSelectionnee, value) && value != null) AllerA(value.ProcedureId); }
        }

        public LigneRegistreSanction LigneRegistreSelectionnee
        {
            get => _ligneRegistreSelectionnee;
            set { if (SetProperty(ref _ligneRegistreSelectionnee, value) && value != null) AllerA(value.ProcedureId); }
        }

        // ── Paramètres ───────────────────────────────────────────────────

        public ParametresDiscipline ParametresEnCours
        {
            get => _parametresEnCours;
            private set => SetProperty(ref _parametresEnCours, value);
        }

        public TypeSanction TypeCatalogueSelectionne
        {
            get => _typeCatalogueSelectionne;
            set
            {
                if (SetProperty(ref _typeCatalogueSelectionne, value))
                    TypeCatalogueEnCours = value?.Clone();
            }
        }

        public TypeSanction TypeCatalogueEnCours
        {
            get => _typeCatalogueEnCours;
            set => SetProperty(ref _typeCatalogueEnCours, value);
        }

        // ── Registre des salariés protégés ───────────────────────────────

        public MandatRepresentatif MandatSelectionne
        {
            get => _mandatSelectionne;
            set
            {
                if (_synchronisation) return;
                if (SetProperty(ref _mandatSelectionne, value))
                    OuvrirMandat(value?.Clone());
            }
        }

        public MandatRepresentatif MandatEnCours
        {
            get => _mandatEnCours;
            private set
            {
                if (SetProperty(ref _mandatEnCours, value))
                    OnPropertyChanged(nameof(MandatOuvert));
            }
        }

        public bool MandatOuvert => _mandatEnCours != null;

        public Employee SalarieMandatChoisi
        {
            get => _salarieMandatChoisi;
            set
            {
                if (_synchronisation) return;
                if (SetProperty(ref _salarieMandatChoisi, value) && _mandatEnCours != null)
                {
                    _mandatEnCours.EmployeeId = value?.EmployeeId;
                    _mandatEnCours.NomSalarie = value?.FullName;
                }
            }
        }

        // ── Commandes ────────────────────────────────────────────────────

        public ICommand ChargerCommand { get; }
        public ICommand NouvelleProcedureCommand { get; }
        public ICommand EnregistrerCommand { get; }
        public ICommand AnnulerCommand { get; }
        public ICommand SupprimerCommand { get; }
        public ICommand ProposerJoursCommand { get; }
        public ICommand AujourdhuiExplicationCommand { get; }
        public ICommand AjouterPieceCommand { get; }
        public ICommand OuvrirPieceCommand { get; }
        public ICommand SupprimerPieceCommand { get; }
        public ICommand OuvrirRepertoireCommand { get; }
        public ICommand LettreExplicationCommand { get; }
        public ICommand LettreConvocationCommand { get; }
        public ICommand LettreConservatoireCommand { get; }
        public ICommand LettreNotificationCommand { get; }
        public ICommand LettreLicenciementCommand { get; }
        public ICommand ExporterExcelCommand { get; }
        public ICommand EnregistrerParametresCommand { get; }
        public ICommand NouveauTypeCommand { get; }
        public ICommand EnregistrerTypeCommand { get; }
        public ICommand SupprimerTypeCommand { get; }
        public ICommand NouveauMandatCommand { get; }
        public ICommand EnregistrerMandatCommand { get; }
        public ICommand SupprimerMandatCommand { get; }

        public DisciplineViewModel(DisciplineService service, DataService dataService)
        {
            _service = service ?? throw new ArgumentNullException(nameof(service));
            _dataService = dataService ?? throw new ArgumentNullException(nameof(dataService));
            _export = new DisciplineExportService();

            Title = "Discipline & procédures";

            VueProcedures = CollectionViewSource.GetDefaultView(Procedures);
            VueProcedures.Filter = FiltrerProcedure;
            VueRegistre = new ListCollectionView(Registre) { Filter = o => _afficherEffacees || !((LigneRegistreSanction)o).EstEffacee };

            Func<bool> libre = () => !IsBusy;
            Func<bool> edition = () => !IsBusy && PeutModifier;
            Func<bool> ouverte = () => edition() && _procedureEnCours != null;
            Func<bool> enregistree = () => edition() && _procedureEnCours != null && !_procedureEnCours.EstNouvelle;

            ChargerCommand = new RelayCommand(async () => await ChargerToutAsync(), () => !IsBusy && !_modifiee);
            NouvelleProcedureCommand = new RelayCommand(NouvelleProcedure, edition);
            EnregistrerCommand = new RelayCommand(async () => await EnregistrerAsync(), ouverte);
            AnnulerCommand = new RelayCommand(Annuler, () => !IsBusy && _procedureEnCours != null && _modifiee);
            SupprimerCommand = new RelayCommand(async () => await SupprimerAsync(),
                () => enregistree() && _procedureSelectionnee != null && _procedureSelectionnee.Statut != StatutProcedure.Notifiee);
            ProposerJoursCommand = new RelayCommand(ProposerJours, () => ouverte() && EstMiseAPied);
            AujourdhuiExplicationCommand = new RelayCommand(() => { if (_procedureEnCours != null) _procedureEnCours.DateDemandeExplication = DateTime.Today; }, ouverte);
            AjouterPieceCommand = new RelayCommand(async () => await AjouterPieceAsync(), enregistree);
            OuvrirPieceCommand = new RelayCommand(OuvrirPiece, () => _pieceSelectionnee != null && _procedureEnCours != null);
            SupprimerPieceCommand = new RelayCommand(async () => await SupprimerPieceAsync(), () => enregistree() && _pieceSelectionnee != null);
            OuvrirRepertoireCommand = new RelayCommand(OuvrirRepertoire, () => _procedureEnCours != null && !_procedureEnCours.EstNouvelle);
            LettreExplicationCommand = new RelayCommand(async () => await LettreAsync(LettreDiscipline.DemandeExplication), ouverte);
            LettreConvocationCommand = new RelayCommand(async () => await LettreAsync(LettreDiscipline.Convocation), ouverte);
            LettreConservatoireCommand = new RelayCommand(async () => await LettreAsync(LettreDiscipline.MiseAPiedConservatoire), ouverte);
            LettreNotificationCommand = new RelayCommand(async () => await LettreAsync(LettreDiscipline.NotificationSanction), ouverte);
            LettreLicenciementCommand = new RelayCommand(async () => await LettreAsync(LettreDiscipline.Licenciement), ouverte);
            ExporterExcelCommand = new RelayCommand(async () => await ExporterAsync(), () => !IsBusy && Procedures.Count > 0);
            EnregistrerParametresCommand = new RelayCommand(async () => await EnregistrerParametresAsync(), edition);
            NouveauTypeCommand = new RelayCommand(() =>
            {
                _typeCatalogueSelectionne = null;
                OnPropertyChanged(nameof(TypeCatalogueSelectionne));
                TypeCatalogueEnCours = new TypeSanction { Ordre = Types.Count == 0 ? 1 : Types.Max(t => t.Ordre) + 1, Nature = NatureSanction.Morale };
            }, edition);
            EnregistrerTypeCommand = new RelayCommand(async () => await EnregistrerTypeAsync(), () => edition() && _typeCatalogueEnCours != null);
            SupprimerTypeCommand = new RelayCommand(async () => await SupprimerTypeAsync(),
                () => edition() && _typeCatalogueEnCours != null && _typeCatalogueEnCours.Id > 0 && !_typeCatalogueEnCours.EstSysteme);
            NouveauMandatCommand = new RelayCommand(() =>
            {
                _synchronisation = true;
                try { _mandatSelectionne = null; OnPropertyChanged(nameof(MandatSelectionne)); }
                finally { _synchronisation = false; }
                OuvrirMandat(new MandatRepresentatif { Mandat = MandatRepresentatif.MandatsProposes[0], DateDebut = DateTime.Today });
            }, edition);
            EnregistrerMandatCommand = new RelayCommand(async () => await EnregistrerMandatAsync(), () => edition() && _mandatEnCours != null);
            SupprimerMandatCommand = new RelayCommand(async () => await SupprimerMandatAsync(),
                () => edition() && _mandatEnCours != null && !_mandatEnCours.EstNouveau);
        }

        /// <summary>
        /// Revenir sur l'écran recharge tout (un salarié a pu sortir, une absence être
        /// modifiée) ; le quitter avec une fiche modifiée propose de l'enregistrer.
        /// </summary>
        protected override void OnIsActiveChanged()
        {
            if (IsActive)
            {
                _ = ChargerToutAsync();
                return;
            }
            ProposerEnregistrement();
        }

        private void ProposerEnregistrement()
        {
            if (!_modifiee || _procedureEnCours == null || !PeutModifier) return;
            var reponse = MessageBox.Show(
                "La procédure « " + _procedureEnCours.Designation + " » comporte des modifications non enregistrées.\n\nLes enregistrer ?",
                "Discipline & procédures", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (reponse == MessageBoxResult.Yes)
                _ = EnregistrerAsync();
            else
                Modifiee = false;
        }

        // ═══════════════════════════════════════════════════════════════════
        //  Chargement
        // ═══════════════════════════════════════════════════════════════════

        public async Task ChargerToutAsync()
        {
            try
            {
                IsBusy = true;
                StatusMessage = "Chargement des procédures disciplinaires…";

                _parametres = await _service.ChargerParametresAsync();
                _feries = await _service.ChargerFeriesAsync();
                _salaries = (await _dataService.LoadEmployeesAsync())
                    .Where(s => s != null && !string.IsNullOrWhiteSpace(s.EmployeeId))
                    .OrderBy(s => s.FullName, StringComparer.CurrentCultureIgnoreCase)
                    .ToList();

                Types.Clear();
                foreach (var t in await _service.ChargerTypesAsync()) Types.Add(t);

                await RechargerMandatsAsync(_mandatEnCours != null && !_mandatEnCours.EstNouveau ? _mandatEnCours.Id : (int?)null);

                ParametresEnCours = _parametres.Clone();
                var typeCatalogueId = _typeCatalogueEnCours?.Id;
                _typeCatalogueSelectionne = typeCatalogueId.HasValue ? Types.FirstOrDefault(t => t.Id == typeCatalogueId.Value) : null;
                OnPropertyChanged(nameof(TypeCatalogueSelectionne));
                TypeCatalogueEnCours = _typeCatalogueSelectionne?.Clone();

                await RechargerProceduresAsync(_procedureEnCours != null && !_modifiee ? _procedureEnCours.Id : (int?)null);

                StatusMessage = Procedures.Count == 0
                    ? "Aucune procédure disciplinaire. « Nouvelle procédure » pour en ouvrir une."
                    : string.Format("{0} procédure(s), dont {1} en cours.", Procedures.Count, NombreEnCours);
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

        private async Task RechargerProceduresAsync(int? aSelectionner)
        {
            var liste = await _service.ChargerProceduresAsync();
            _synchronisation = true;
            try
            {
                Procedures.Clear();
                foreach (var p in liste) Procedures.Add(p);
            }
            finally
            {
                _synchronisation = false;
            }

            Echeances.Clear();
            foreach (var e in ControleDiscipline.Echeances(liste, _parametres, _feries, DateTime.Today)) Echeances.Add(e);
            Registre.Clear();
            foreach (var r in ControleDiscipline.Registre(liste, DateTime.Today)) Registre.Add(r);
            VueProcedures.Refresh();
            VueRegistre.Refresh();

            OnPropertyChanged(nameof(NombreEnCours));
            OnPropertyChanged(nameof(NombreANotifier));
            OnPropertyChanged(nameof(NombreEcheancesDepassees));
            OnPropertyChanged(nameof(NombreSanctionsAnnee));
            OnPropertyChanged(nameof(NombreMisesAPiedAnnee));

            if (aSelectionner.HasValue && aSelectionner.Value > 0)
            {
                var p = Procedures.FirstOrDefault(x => x.Id == aSelectionner.Value);
                _procedureSelectionnee = p;
                OnPropertyChanged(nameof(ProcedureSelectionnee));
                await OuvrirAsync(p);
            }
            else
            {
                // La fiche en cours d'édition est conservée ; la sélection pointe sur sa
                // version rechargée (ou sur rien pour une procédure pas encore créée).
                _procedureSelectionnee = _procedureEnCours != null && _procedureEnCours.Id > 0
                    ? Procedures.FirstOrDefault(x => x.Id == _procedureEnCours.Id)
                    : null;
                OnPropertyChanged(nameof(ProcedureSelectionnee));
            }
        }

        private async Task OuvrirAsync(ProcedureDisciplinaire p)
        {
            ProcedureEnCours = p?.Clone();
            Pieces.Clear();
            PieceSelectionnee = null;
            if (p != null && p.Id > 0)
            {
                try
                {
                    foreach (var piece in await _service.ChargerPiecesAsync(p.Id)) Pieces.Add(piece);
                }
                catch (Exception ex)
                {
                    StatusMessage = "Pièces illisibles : " + ex.Message;
                }
            }
            RecalculerAlertes();
        }

        private void AllerA(int procedureId)
        {
            var p = Procedures.FirstOrDefault(x => x.Id == procedureId);
            if (p == null) return;
            if (!VueProcedures.Cast<object>().Contains(p))
            {
                _filtreStatut = FiltreToutes;
                _recherche = null;
                OnPropertyChanged(nameof(FiltreStatut));
                OnPropertyChanged(nameof(Recherche));
                VueProcedures.Refresh();
            }
            ProcedureSelectionnee = p;
            OngletActif = 0;
        }

        private bool FiltrerProcedure(object o)
        {
            var p = o as ProcedureDisciplinaire;
            if (p == null) return false;

            switch (_filtreStatut)
            {
                case FiltreEnCours: if (p.EstClose) return false; break;
                case FiltreANotifier: if (p.Statut != StatutProcedure.DecisionPrise) return false; break;
                case FiltreNotifiees: if (p.Statut != StatutProcedure.Notifiee) return false; break;
                case FiltreClassees: if (p.Statut != StatutProcedure.ClasseeSansSuite) return false; break;
            }

            if (string.IsNullOrWhiteSpace(_recherche)) return true;
            var r = _recherche.Trim();
            return Contient(p.Reference, r) || Contient(p.EmployeeId, r) || Contient(p.NomSalarie, r)
                   || Contient(p.CategorieFaits, r) || Contient(p.SanctionLibelle, r);
        }

        private static bool Contient(string texte, string recherche)
        {
            return texte != null && texte.IndexOf(recherche, StringComparison.CurrentCultureIgnoreCase) >= 0;
        }

        private void ReconstruireTypesProposes()
        {
            TypesProposes.Clear();
            var actuel = _procedureEnCours?.TypeSanctionId;
            foreach (var t in Types.Where(t => t.Actif || (actuel.HasValue && t.Id == actuel.Value)))
                TypesProposes.Add(t);
            if (_procedureEnCours != null && actuel.HasValue)
                _procedureEnCours.TypeSanction = TypesProposes.FirstOrDefault(t => t.Id == actuel.Value) ?? _procedureEnCours.TypeSanction;
        }

        private void ReconstruireSalariesProposes()
        {
            SalariesProposes.Clear();
            var id = _procedureEnCours?.EmployeeId;
            foreach (var s in _salaries.Where(s => s.EstActif || string.Equals(s.EmployeeId, id, StringComparison.OrdinalIgnoreCase)))
                SalariesProposes.Add(s);
        }

        // ═══════════════════════════════════════════════════════════════════
        //  Contrôles en direct
        // ═══════════════════════════════════════════════════════════════════

        private void ProcedureEnCours_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (!Modifiee) Modifiee = true;
            if (e.PropertyName == nameof(ProcedureDisciplinaire.SalarieProtege) || e.PropertyName == nameof(ProcedureDisciplinaire.DecisionInspection)
                || e.PropertyName == nameof(ProcedureDisciplinaire.ClasseeSansSuite) || e.PropertyName == nameof(ProcedureDisciplinaire.DateDemandeExplication)
                || e.PropertyName == nameof(ProcedureDisciplinaire.DateConvocation) || e.PropertyName == nameof(ProcedureDisciplinaire.MiseAPiedDebut)
                || e.PropertyName == nameof(ProcedureDisciplinaire.MiseAPiedFin) || e.PropertyName == nameof(ProcedureDisciplinaire.Statut))
                NotifierEtat();
            else if (e.PropertyName != nameof(ProcedureDisciplinaire.StatutLibelle) && e.PropertyName != nameof(ProcedureDisciplinaire.EstClose)
                     && e.PropertyName != nameof(ProcedureDisciplinaire.SanctionLibelle) && e.PropertyName != nameof(ProcedureDisciplinaire.DateEngagement))
                RecalculerAlertes();
        }

        private void NotifierEtat()
        {
            OnPropertyChanged(nameof(ProcedureOuverte));
            OnPropertyChanged(nameof(EstNouvelle));
            OnPropertyChanged(nameof(SalarieModifiable));
            OnPropertyChanged(nameof(TitreFiche));
            OnPropertyChanged(nameof(EstMiseAPied));
            OnPropertyChanged(nameof(EstLicenciement));
            OnPropertyChanged(nameof(AfficherInspection));
            OnPropertyChanged(nameof(AfficherClassement));
            OnPropertyChanged(nameof(ExigencesSanction));
            OnPropertyChanged(nameof(DateLimiteReponseLibelle));
            OnPropertyChanged(nameof(PremiereDateEntretienLibelle));
            OnPropertyChanged(nameof(JoursMiseAPiedProposesLibelle));
            RecalculerAlertes();
        }

        private void RecalculerAlertes()
        {
            Alertes.Clear();
            PiecesExigees.Clear();
            if (_procedureEnCours != null)
            {
                var type = _procedureEnCours.TypeSanction;
                foreach (var a in ControleDiscipline.Analyser(_procedureEnCours, type, _parametres, Procedures, Pieces, _feries, DateTime.Today, Mandats))
                    Alertes.Add(a);
                foreach (var x in ControleDiscipline.PiecesExigees(_procedureEnCours, type, Pieces))
                    PiecesExigees.Add(x);
            }
            OnPropertyChanged(nameof(NombreBloquants));
            OnPropertyChanged(nameof(EtapeSuivante));
        }

        // ═══════════════════════════════════════════════════════════════════
        //  Actions sur la procédure
        // ═══════════════════════════════════════════════════════════════════

        private void NouvelleProcedure()
        {
            ProposerEnregistrement();
            _procedureSelectionnee = null;
            OnPropertyChanged(nameof(ProcedureSelectionnee));
            var p = new ProcedureDisciplinaire { DateFaits = DateTime.Today, DateConnaissance = DateTime.Today };
            Pieces.Clear();
            ProcedureEnCours = p;
            Modifiee = true;
            OngletActif = 0;
            StatusMessage = "Nouvelle procédure : choisissez le salarié et décrivez les faits, puis « Enregistrer ».";
        }

        private void Annuler()
        {
            Modifiee = false;
            _ = OuvrirAsync(_procedureSelectionnee);
            StatusMessage = "Modifications abandonnées.";
        }

        private void ProposerJours()
        {
            var p = _procedureEnCours;
            if (p == null || !p.MiseAPiedDebut.HasValue || !p.MiseAPiedFin.HasValue) return;
            p.MiseAPiedJours = ControleDiscipline.JoursMiseAPiedProposes(p.MiseAPiedDebut.Value, p.MiseAPiedFin.Value);
        }

        private async Task<bool> EnregistrerAsync()
        {
            // La fiche est capturée tout de suite : si l'enregistrement a été proposé en
            // quittant la fiche, la sélection change pendant qu'il s'exécute.
            var fiche = _procedureEnCours;
            var origine = _procedureSelectionnee;
            if (fiche == null) return false;
            if (!PeutModifier) { StatusMessage = "Votre profil ne permet pas de modifier les procédures."; return false; }

            var bloquant = Alertes.FirstOrDefault(a => a.Niveau == NiveauAlerte.Bloquant);
            if (bloquant != null)
            {
                StatusMessage = "Enregistrement refusé : " + bloquant.Message;
                MessageBox.Show(bloquant.Message, "Procédure disciplinaire — règle de procédure", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }

            bool premiereNotification = fiche.Statut == StatutProcedure.Notifiee
                                        && (origine == null || origine.Id != fiche.Id || origine.Statut != StatutProcedure.Notifiee);
            if (premiereNotification)
            {
                var t = fiche.TypeSanction;
                string effet = t != null && t.EstMiseAPied
                    ? string.Format(LibellesDiscipline.Fr, "\n\nLa mise à pied de {0:0.##} jour(s) sera transmise aux congés et absences et retenue sur la paie.", fiche.MiseAPiedJours)
                    : "";
                var ok = MessageBox.Show(
                    "Vous enregistrez la NOTIFICATION de la sanction « " + (t?.Libelle ?? "?") + " » à " + _procedureEnCours.NomSalarie +
                    ".\n\nUne sanction notifiée entre au registre : la procédure ne pourra plus être supprimée et ses pièces seront figées." + effet + "\n\nConfirmer ?",
                    "Notification de sanction", MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (ok != MessageBoxResult.Yes) return false;
            }

            try
            {
                IsBusy = true;
                var resultat = await _service.EnregistrerAsync(fiche);
                bool toujoursAffichee = ReferenceEquals(fiche, _procedureEnCours);
                if (toujoursAffichee) Modifiee = false;
                await RechargerProceduresAsync(toujoursAffichee ? fiche.Id : (_procedureEnCours != null && !_modifiee ? _procedureEnCours.Id : (int?)null));
                StatusMessage = resultat.Resume;
                return true;
            }
            catch (Exception ex)
            {
                StatusMessage = "Enregistrement impossible : " + ex.Message;
                MessageBox.Show(ex.Message, "Procédure disciplinaire", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }
            finally
            {
                IsBusy = false;
            }
        }

        private async Task SupprimerAsync()
        {
            var p = _procedureSelectionnee;
            if (p == null) return;
            var ok = MessageBox.Show(
                "Supprimer la procédure " + p.Reference + " (" + p.NomSalarie + ") et ses pièces ?\n\nPréférez « Classée sans suite » pour garder la trace d'une procédure abandonnée.",
                "Discipline & procédures", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (ok != MessageBoxResult.Yes) return;

            try
            {
                IsBusy = true;
                await _service.SupprimerAsync(p);
                Modifiee = false;
                _procedureSelectionnee = null;
                OnPropertyChanged(nameof(ProcedureSelectionnee));
                ProcedureEnCours = null;
                Pieces.Clear();
                await RechargerProceduresAsync(null);
                StatusMessage = "Procédure " + p.Reference + " supprimée.";
            }
            catch (Exception ex)
            {
                StatusMessage = "Suppression impossible : " + ex.Message;
                MessageBox.Show(ex.Message, "Discipline & procédures", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            finally
            {
                IsBusy = false;
            }
        }

        // ═══════════════════════════════════════════════════════════════════
        //  Pièces et lettres
        // ═══════════════════════════════════════════════════════════════════

        private async Task AjouterPieceAsync()
        {
            var p = _procedureEnCours;
            if (p == null || p.EstNouvelle) return;

            var dlg = new OpenFileDialog
            {
                Title = "Verser une pièce au dossier — " + LibellesDiscipline.Piece(_typeNouvellePiece),
                Filter = "Documents (*.pdf;*.jpg;*.jpeg;*.png;*.docx;*.doc;*.xlsx;*.txt)|*.pdf;*.jpg;*.jpeg;*.png;*.docx;*.doc;*.xlsx;*.txt|Tous les fichiers (*.*)|*.*",
                Multiselect = true
            };
            if (dlg.ShowDialog() != true) return;

            try
            {
                IsBusy = true;
                foreach (var f in dlg.FileNames)
                    Pieces.Add(await _service.AjouterPieceAsync(p, _typeNouvellePiece, f, null, false));
                RecalculerAlertes();
                StatusMessage = string.Format("{0} pièce(s) versée(s) au dossier {1}.", dlg.FileNames.Length, p.Reference);
            }
            catch (Exception ex)
            {
                StatusMessage = "Pièce non versée : " + ex.Message;
            }
            finally
            {
                IsBusy = false;
            }
        }

        private void OuvrirPiece()
        {
            if (_pieceSelectionnee == null || _procedureEnCours == null) return;
            Ouvrir(_service.CheminPiece(_procedureEnCours, _pieceSelectionnee));
        }

        private void OuvrirRepertoire()
        {
            if (_procedureEnCours == null) return;
            var rep = _service.RepertoireProcedure(_procedureEnCours);
            Directory.CreateDirectory(rep);
            Ouvrir(rep);
        }

        private void Ouvrir(string chemin)
        {
            try
            {
                if (!File.Exists(chemin) && !Directory.Exists(chemin))
                {
                    StatusMessage = "Fichier introuvable : " + chemin;
                    return;
                }
                Process.Start(new ProcessStartInfo(chemin) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                StatusMessage = "Ouverture impossible : " + ex.Message;
            }
        }

        private async Task SupprimerPieceAsync()
        {
            var piece = _pieceSelectionnee;
            if (piece == null || _procedureEnCours == null) return;
            if (MessageBox.Show("Retirer la pièce « " + piece.Libelle + " » du dossier ? Le fichier copié sera supprimé.",
                    "Discipline & procédures", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
            try
            {
                await _service.SupprimerPieceAsync(_procedureSelectionnee ?? _procedureEnCours, piece);
                Pieces.Remove(piece);
                RecalculerAlertes();
                StatusMessage = "Pièce retirée.";
            }
            catch (Exception ex)
            {
                StatusMessage = ex.Message;
                MessageBox.Show(ex.Message, "Discipline & procédures", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        /// <summary>
        /// Produit la lettre, la verse au dossier comme pièce et l'ouvre. Une fiche modifiée
        /// est d'abord enregistrée : la lettre reflète toujours ce qui est en base.
        /// </summary>
        private async Task LettreAsync(LettreDiscipline lettre)
        {
            if (_procedureEnCours == null) return;
            if (_modifiee && !await EnregistrerAsync()) return;

            var p = _procedureEnCours;
            var manque = DisciplineExportService.Prerequis(lettre, p);
            if (manque != null)
            {
                StatusMessage = DisciplineExportService.Libelle(lettre) + " : " + manque;
                MessageBox.Show(manque, DisciplineExportService.Libelle(lettre), MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            try
            {
                IsBusy = true;
                var chemin = _service.CheminNouvelleLettre(p, DisciplineExportService.Libelle(lettre));
                var salarie = _salaries.FirstOrDefault(s => string.Equals(s.EmployeeId, p.EmployeeId, StringComparison.OrdinalIgnoreCase));
                await Task.Run(() => _export.GenererLettre(lettre, p, salarie, _parametres, _feries, chemin));
                var piece = await _service.AjouterPieceAsync(p, DisciplineExportService.TypePiecePour(lettre), chemin,
                    DisciplineExportService.Libelle(lettre), true);
                Pieces.Add(piece);
                RecalculerAlertes();
                StatusMessage = DisciplineExportService.Libelle(lettre) + " produite et versée au dossier. Faites-la signer, puis versez la décharge.";
                Ouvrir(chemin);
            }
            catch (Exception ex)
            {
                StatusMessage = "Lettre non produite : " + ex.Message;
            }
            finally
            {
                IsBusy = false;
            }
        }

        private async Task ExporterAsync()
        {
            var dlg = new SaveFileDialog
            {
                Title = "Exporter le registre disciplinaire",
                Filter = "Classeur Excel (*.xlsx)|*.xlsx",
                FileName = string.Format(CultureInfo.InvariantCulture, "Registre_disciplinaire_{0:yyyy-MM-dd}.xlsx", DateTime.Today)
            };
            if (dlg.ShowDialog() != true) return;

            try
            {
                IsBusy = true;
                var procedures = Procedures.ToList();
                var registre = Registre.ToList();
                var echeances = Echeances.ToList();
                await Task.Run(() => _export.ExporterRegistre(dlg.FileName, procedures, registre, echeances));
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

        // ═══════════════════════════════════════════════════════════════════
        //  Paramètres et catalogue
        // ═══════════════════════════════════════════════════════════════════

        private async Task EnregistrerParametresAsync()
        {
            try
            {
                IsBusy = true;
                await _service.EnregistrerParametresAsync(_parametresEnCours);
                _parametres = _parametresEnCours.Clone();
                await RechargerProceduresAsync(_procedureEnCours != null && !_modifiee ? _procedureEnCours.Id : (int?)null);
                NotifierEtat();
                StatusMessage = "Délais de procédure enregistrés.";
            }
            catch (Exception ex)
            {
                StatusMessage = "Paramètres non enregistrés : " + ex.Message;
                MessageBox.Show(ex.Message, "Paramètres de la discipline", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            finally
            {
                IsBusy = false;
            }
        }

        private async Task EnregistrerTypeAsync()
        {
            var t = _typeCatalogueEnCours;
            if (t == null) return;
            try
            {
                IsBusy = true;
                await _service.EnregistrerTypeAsync(t);
                await ChargerToutAsync();
                TypeCatalogueSelectionne = Types.FirstOrDefault(x => x.Id == t.Id);
                StatusMessage = "Sanction « " + t.Libelle + " » enregistrée au catalogue.";
            }
            catch (Exception ex)
            {
                StatusMessage = "Sanction non enregistrée : " + ex.Message;
                MessageBox.Show(ex.Message, "Catalogue des sanctions", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            finally
            {
                IsBusy = false;
            }
        }

        private async Task SupprimerTypeAsync()
        {
            var t = _typeCatalogueEnCours;
            if (t == null) return;
            if (MessageBox.Show("Supprimer « " + t.Libelle + " » du catalogue ?", "Catalogue des sanctions",
                    MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
            try
            {
                IsBusy = true;
                await _service.SupprimerTypeAsync(t);
                TypeCatalogueEnCours = null;
                await ChargerToutAsync();
                StatusMessage = "Sanction supprimée du catalogue.";
            }
            catch (Exception ex)
            {
                StatusMessage = ex.Message;
                MessageBox.Show(ex.Message, "Catalogue des sanctions", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            finally
            {
                IsBusy = false;
            }
        }

        // ═══════════════════════════════════════════════════════════════════
        //  Registre des salariés protégés
        // ═══════════════════════════════════════════════════════════════════

        private async Task RechargerMandatsAsync(int? aSelectionner)
        {
            var liste = await _service.ChargerMandatsAsync();
            _synchronisation = true;
            try
            {
                Mandats.Clear();
                foreach (var m in liste) Mandats.Add(m);
                SalariesRegistre.Clear();
                foreach (var e in _salaries.Where(x => x.EstActif || liste.Any(m => string.Equals(m.EmployeeId, x.EmployeeId, StringComparison.OrdinalIgnoreCase))))
                    SalariesRegistre.Add(e);
                _mandatSelectionne = aSelectionner.HasValue ? Mandats.FirstOrDefault(m => m.Id == aSelectionner.Value) : null;
            }
            finally
            {
                _synchronisation = false;
            }
            OnPropertyChanged(nameof(MandatSelectionne));
            if (aSelectionner.HasValue) OuvrirMandat(_mandatSelectionne?.Clone());
        }

        private void OuvrirMandat(MandatRepresentatif m)
        {
            MandatEnCours = m;
            _synchronisation = true;
            try
            {
                _salarieMandatChoisi = m == null ? null
                    : SalariesRegistre.FirstOrDefault(e => string.Equals(e.EmployeeId, m.EmployeeId, StringComparison.OrdinalIgnoreCase));
            }
            finally
            {
                _synchronisation = false;
            }
            OnPropertyChanged(nameof(SalarieMandatChoisi));
        }

        private async Task EnregistrerMandatAsync()
        {
            var m = _mandatEnCours;
            if (m == null) return;
            try
            {
                IsBusy = true;
                await _service.EnregistrerMandatAsync(m);
                await RechargerMandatsAsync(m.Id);
                RecalculerAlertes();
                StatusMessage = "Mandat enregistré : " + m.Mandat + " — " + (m.NomSalarie ?? m.EmployeeId) + ".";
            }
            catch (Exception ex)
            {
                StatusMessage = "Mandat non enregistré : " + ex.Message;
                MessageBox.Show(ex.Message, "Registre des salariés protégés", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            finally
            {
                IsBusy = false;
            }
        }

        private async Task SupprimerMandatAsync()
        {
            var m = _mandatEnCours;
            if (m == null || m.EstNouveau) return;
            if (MessageBox.Show("Retirer ce mandat du registre ?\n\nPour un mandat terminé, renseignez plutôt sa date de fin : l'historique de la protection reste utile en cas de contentieux.",
                    "Registre des salariés protégés", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
            try
            {
                IsBusy = true;
                await _service.SupprimerMandatAsync(m);
                OuvrirMandat(null);
                await RechargerMandatsAsync(null);
                RecalculerAlertes();
                StatusMessage = "Mandat retiré du registre.";
            }
            catch (Exception ex)
            {
                StatusMessage = "Mandat non retiré : " + ex.Message;
            }
            finally
            {
                IsBusy = false;
            }
        }
    }
}
