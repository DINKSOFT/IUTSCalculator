using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;

namespace IUTSCalculator.Models
{
    #region Énumérations RH-04

    /// <summary>
    /// Statut du cycle de vie du Plan annuel de formation (module RH-04).
    /// </summary>
    public enum StatutPlanFormation
    {
        [Description("Élaboration (Brouillon)")]
        Elaboration = 0,

        [Description("Validé")]
        Valide = 1,

        [Description("En cours d'exécution")]
        EnCours = 2,

        [Description("Clôturé")]
        Cloture = 3
    }

    /// <summary>
    /// Modalité d'exécution de l'action de formation.
    /// </summary>
    public enum TypeActionFormation
    {
        [Description("Formation interne")]
        Interne = 0,

        [Description("Formation externe")]
        Externe = 1,

        [Description("Mixte (Interne / Externe)")]
        Mixte = 2,

        [Description("E-Learning / Distanciel")]
        ELearning = 3
    }

    /// <summary>
    /// Statut d'avancement d'une session de formation.
    /// </summary>
    public enum StatutSessionFormation
    {
        [Description("Planifiée")]
        Planifiee = 0,

        [Description("Confirmée")]
        Confirmee = 1,

        [Description("En cours")]
        EnCours = 2,

        [Description("Réalisée")]
        Realisee = 3,

        [Description("Annulée")]
        Annulee = 4
    }

    /// <summary>
    /// Statut de présence d'un salarié inscrit à une session.
    /// </summary>
    public enum StatutPresenceSession
    {
        [Description("Inscrit")]
        Inscrit = 0,

        [Description("Présent")]
        Present = 1,

        [Description("Absent justifié")]
        AbsentJustifie = 2,

        [Description("Abandon")]
        Abandon = 3
    }

    #endregion

    #region Plan Annuel de Formation

    /// <summary>
    /// Plan annuel de formation de l'entreprise rattaché à un exercice financier (module RH-04).
    /// </summary>
    public class PlanFormation : INotifyPropertyChanged
    {
        private int _id;
        private string _code;
        private string _titre;
        private int _anneeExercice = DateTime.Now.Year;
        private decimal _budgetPrevisionnel; // Stocké en centimes
        private StatutPlanFormation _statut = StatutPlanFormation.Elaboration;
        private string _objectifsStrategiques;
        private DateTime? _dateValidation;
        private DateTime _dateCreation = DateTime.Now;

        // Propriétés calculées / agrégées
        private int _nombreSessions;
        private int _totalHeures;
        private decimal _depensesReelles; // en FCFA
        private int _nombreSalariesFormes;

        public int Id
        {
            get => _id;
            set { if (_id != value) { _id = value; OnPropertyChanged(); } }
        }

        public string Code
        {
            get => _code;
            set
            {
                if (_code != value)
                {
                    _code = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(DisplayName));
                }
            }
        }

        public string Titre
        {
            get => _titre;
            set
            {
                if (_titre != value)
                {
                    _titre = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(DisplayName));
                }
            }
        }

        public int AnneeExercice
        {
            get => _anneeExercice;
            set { if (_anneeExercice != value) { _anneeExercice = value; OnPropertyChanged(); } }
        }

        public decimal BudgetPrevisionnel
        {
            get => _budgetPrevisionnel;
            set
            {
                if (_budgetPrevisionnel != value)
                {
                    _budgetPrevisionnel = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(BudgetPrevisionnelTexte));
                    OnPropertyChanged(nameof(EcartBudget));
                    OnPropertyChanged(nameof(EcartBudgetTexte));
                    OnPropertyChanged(nameof(TauxConsommationPct));
                    OnPropertyChanged(nameof(TauxConsommationTexte));
                }
            }
        }

        public string BudgetPrevisionnelTexte => string.Format(CultureInfo.InvariantCulture, "{0:N0} FCFA", _budgetPrevisionnel);

        public StatutPlanFormation Statut
        {
            get => _statut;
            set
            {
                if (_statut != value)
                {
                    _statut = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(StatutLibelle));
                    OnPropertyChanged(nameof(CouleurStatut));
                    OnPropertyChanged(nameof(EstModifiable));
                    OnPropertyChanged(nameof(EstCloture));
                }
            }
        }

        public string StatutLibelle
        {
            get
            {
                switch (_statut)
                {
                    case StatutPlanFormation.Elaboration: return "Élaboration";
                    case StatutPlanFormation.Valide: return "Validé";
                    case StatutPlanFormation.EnCours: return "En cours";
                    case StatutPlanFormation.Cloture: return "Clôturé";
                    default: return _statut.ToString();
                }
            }
        }

        public string CouleurStatut
        {
            get
            {
                switch (_statut)
                {
                    case StatutPlanFormation.Elaboration: return "#5C6BC0"; // Indigo
                    case StatutPlanFormation.Valide: return "#0288D1";      // Bleu
                    case StatutPlanFormation.EnCours: return "#14675A";     // Vert émeraude
                    case StatutPlanFormation.Cloture: return "#757575";     // Gris neutre
                    default: return "#757575";
                }
            }
        }

        public bool EstModifiable => _statut != StatutPlanFormation.Cloture;
        public bool EstCloture => _statut == StatutPlanFormation.Cloture;

        public string ObjectifsStrategiques
        {
            get => _objectifsStrategiques;
            set { if (_objectifsStrategiques != value) { _objectifsStrategiques = value; OnPropertyChanged(); } }
        }

        public DateTime? DateValidation
        {
            get => _dateValidation;
            set { if (_dateValidation != value) { _dateValidation = value; OnPropertyChanged(); } }
        }

        public DateTime DateCreation
        {
            get => _dateCreation;
            set { if (_dateCreation != value) { _dateCreation = value; OnPropertyChanged(); } }
        }

        public int NombreSessions
        {
            get => _nombreSessions;
            set { if (_nombreSessions != value) { _nombreSessions = value; OnPropertyChanged(); } }
        }

        public int TotalHeures
        {
            get => _totalHeures;
            set { if (_totalHeures != value) { _totalHeures = value; OnPropertyChanged(); } }
        }

        public decimal DepensesReelles
        {
            get => _depensesReelles;
            set
            {
                if (_depensesReelles != value)
                {
                    _depensesReelles = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(DepensesReellesTexte));
                    OnPropertyChanged(nameof(EcartBudget));
                    OnPropertyChanged(nameof(EcartBudgetTexte));
                    OnPropertyChanged(nameof(TauxConsommationPct));
                    OnPropertyChanged(nameof(TauxConsommationTexte));
                }
            }
        }

        public string DepensesReellesTexte => string.Format(CultureInfo.InvariantCulture, "{0:N0} FCFA", _depensesReelles);

        public int NombreSalariesFormes
        {
            get => _nombreSalariesFormes;
            set { if (_nombreSalariesFormes != value) { _nombreSalariesFormes = value; OnPropertyChanged(); } }
        }

        public decimal EcartBudget => _budgetPrevisionnel - _depensesReelles;

        public string EcartBudgetTexte => string.Format(CultureInfo.InvariantCulture, "{0:N0} FCFA", EcartBudget);

        public double TauxConsommationPct
        {
            get
            {
                if (_budgetPrevisionnel <= 0m) return 0.0;
                double ratio = (double)(_depensesReelles / _budgetPrevisionnel) * 100.0;
                return Math.Round(ratio, 1);
            }
        }

        public string TauxConsommationTexte => $"{TauxConsommationPct:F1} %";

        public string DisplayName => string.IsNullOrEmpty(_code) ? _titre : $"[{_code}] {_titre} ({_anneeExercice})";

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    #endregion

    #region Organisme de Formation

    /// <summary>
    /// Organisme formateur prestataire ou structure interne (module RH-04).
    /// </summary>
    public class OrganismeFormation : INotifyPropertyChanged
    {
        private int _id;
        private string _raisonSociale;
        private string _contactNom;
        private string _telephone;
        private string _email;
        private string _adresse;
        private string _numeroAgrement; // N° agrément FAFPA / Ministère
        private string _specialites;
        private bool _actif = true;

        public int Id
        {
            get => _id;
            set { if (_id != value) { _id = value; OnPropertyChanged(); } }
        }

        public string RaisonSociale
        {
            get => _raisonSociale;
            set
            {
                if (_raisonSociale != value)
                {
                    _raisonSociale = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(DisplayName));
                }
            }
        }

        public string ContactNom
        {
            get => _contactNom;
            set { if (_contactNom != value) { _contactNom = value; OnPropertyChanged(); } }
        }

        public string Telephone
        {
            get => _telephone;
            set { if (_telephone != value) { _telephone = value; OnPropertyChanged(); } }
        }

        public string Email
        {
            get => _email;
            set { if (_email != value) { _email = value; OnPropertyChanged(); } }
        }

        public string Adresse
        {
            get => _adresse;
            set { if (_adresse != value) { _adresse = value; OnPropertyChanged(); } }
        }

        public string NumeroAgrement
        {
            get => _numeroAgrement;
            set { if (_numeroAgrement != value) { _numeroAgrement = value; OnPropertyChanged(); } }
        }

        public string Specialites
        {
            get => _specialites;
            set { if (_specialites != value) { _specialites = value; OnPropertyChanged(); } }
        }

        public bool Actif
        {
            get => _actif;
            set
            {
                if (_actif != value)
                {
                    _actif = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(StatutActifLibelle));
                }
            }
        }

        public string StatutActifLibelle => _actif ? "Actif" : "Inactif";

        public string DisplayName => string.IsNullOrEmpty(_numeroAgrement)
            ? _raisonSociale
            : $"{_raisonSociale} (Agrément: {_numeroAgrement})";

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    #endregion

    #region Catalogue des Formations

    /// <summary>
    /// Module ou action de formation répertoriée au catalogue interne (module RH-04).
    /// </summary>
    public class FormationCatalogue : INotifyPropertyChanged
    {
        private int _id;
        private string _code;
        private string _intitule;
        private string _domaine;
        private int? _competenceViseeId;
        private string _competenceViseeLibelle;
        private TypeActionFormation _typeFormation = TypeActionFormation.Interne;
        private int _dureeHeures;
        private decimal _coutEstimeParParticipant; // en FCFA
        private string _objectifsPedagogiques;
        private string _prerequis;
        private bool _actif = true;

        public int Id
        {
            get => _id;
            set { if (_id != value) { _id = value; OnPropertyChanged(); } }
        }

        public string Code
        {
            get => _code;
            set
            {
                if (_code != value)
                {
                    _code = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(DisplayName));
                }
            }
        }

        public string Intitule
        {
            get => _intitule;
            set
            {
                if (_intitule != value)
                {
                    _intitule = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(DisplayName));
                }
            }
        }

        public string Domaine
        {
            get => _domaine;
            set { if (_domaine != value) { _domaine = value; OnPropertyChanged(); } }
        }

        public int? CompetenceViseeId
        {
            get => _competenceViseeId;
            set { if (_competenceViseeId != value) { _competenceViseeId = value; OnPropertyChanged(); } }
        }

        public string CompetenceViseeLibelle
        {
            get => _competenceViseeLibelle;
            set { if (_competenceViseeLibelle != value) { _competenceViseeLibelle = value; OnPropertyChanged(); } }
        }

        public TypeActionFormation TypeFormation
        {
            get => _typeFormation;
            set
            {
                if (_typeFormation != value)
                {
                    _typeFormation = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(TypeFormationLibelle));
                }
            }
        }

        public string TypeFormationLibelle
        {
            get
            {
                switch (_typeFormation)
                {
                    case TypeActionFormation.Interne: return "Interne";
                    case TypeActionFormation.Externe: return "Externe";
                    case TypeActionFormation.Mixte: return "Mixte";
                    case TypeActionFormation.ELearning: return "E-Learning";
                    default: return _typeFormation.ToString();
                }
            }
        }

        public int DureeHeures
        {
            get => _dureeHeures;
            set
            {
                if (_dureeHeures != value)
                {
                    _dureeHeures = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(DureeLibelle));
                }
            }
        }

        public string DureeLibelle => $"{_dureeHeures} h";

        public decimal CoutEstimeParParticipant
        {
            get => _coutEstimeParParticipant;
            set
            {
                if (_coutEstimeParParticipant != value)
                {
                    _coutEstimeParParticipant = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(CoutEstimeTexte));
                }
            }
        }

        public string CoutEstimeTexte => string.Format(CultureInfo.InvariantCulture, "{0:N0} FCFA", _coutEstimeParParticipant);

        public string ObjectifsPedagogiques
        {
            get => _objectifsPedagogiques;
            set { if (_objectifsPedagogiques != value) { _objectifsPedagogiques = value; OnPropertyChanged(); } }
        }

        public string Prerequis
        {
            get => _prerequis;
            set { if (_prerequis != value) { _prerequis = value; OnPropertyChanged(); } }
        }

        public bool Actif
        {
            get => _actif;
            set
            {
                if (_actif != value)
                {
                    _actif = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(StatutActifLibelle));
                }
            }
        }

        public string StatutActifLibelle => _actif ? "Actif" : "Inactif";

        public string DisplayName => string.IsNullOrEmpty(_code) ? _intitule : $"[{_code}] {_intitule}";

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    #endregion

    #region Session de Formation

    /// <summary>
    /// Occurrence concrète d'une formation rattachée à un plan annuel (module RH-04).
    /// </summary>
    public class SessionFormation : INotifyPropertyChanged
    {
        private int _id;
        private int _formationId;
        private int _planFormationId;
        private int? _organismeId;
        private string _codeSession;
        private string _intituleSession;
        private DateTime _dateDebut = DateTime.Today;
        private DateTime _dateFin = DateTime.Today;
        private string _lieu;
        private string _formateurNom;
        private StatutSessionFormation _statut = StatutSessionFormation.Planifiee;
        private decimal _coutPedagogique; // FCFA
        private decimal _coutLogistique;  // FCFA
        private decimal _coutAutre;       // FCFA
        private decimal _coutTotalReel;   // FCFA
        private string _commentaire;

        // Métadonnées d'affichage et jointures
        private string _formationCode;
        private string _formationIntitule;
        private int _dureeHeures;
        private string _organismeNom;
        private string _organismeNumeroAgrement;
        private string _planCode;
        private string _planTitre;
        private int _nombreInscrits;
        private int _nombrePresents;
        private int _nombreAttestations;

        public int Id
        {
            get => _id;
            set { if (_id != value) { _id = value; OnPropertyChanged(); } }
        }

        public int FormationId
        {
            get => _formationId;
            set { if (_formationId != value) { _formationId = value; OnPropertyChanged(); } }
        }

        public int PlanFormationId
        {
            get => _planFormationId;
            set { if (_planFormationId != value) { _planFormationId = value; OnPropertyChanged(); } }
        }

        public int? OrganismeId
        {
            get => _organismeId;
            set { if (_organismeId != value) { _organismeId = value; OnPropertyChanged(); } }
        }

        public string CodeSession
        {
            get => _codeSession;
            set
            {
                if (_codeSession != value)
                {
                    _codeSession = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(DisplayName));
                }
            }
        }

        public string IntituleSession
        {
            get => _intituleSession;
            set
            {
                if (_intituleSession != value)
                {
                    _intituleSession = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(DisplayName));
                }
            }
        }

        public DateTime DateDebut
        {
            get => _dateDebut;
            set
            {
                if (_dateDebut != value)
                {
                    _dateDebut = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(PeriodeLibelle));
                }
            }
        }

        public DateTime DateFin
        {
            get => _dateFin;
            set
            {
                if (_dateFin != value)
                {
                    _dateFin = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(PeriodeLibelle));
                }
            }
        }

        public string PeriodeLibelle
        {
            get
            {
                if (_dateDebut.Date == _dateFin.Date)
                    return _dateDebut.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);
                return $"{_dateDebut:dd/MM/yyyy} au {_dateFin:dd/MM/yyyy}";
            }
        }

        public string Lieu
        {
            get => _lieu;
            set { if (_lieu != value) { _lieu = value; OnPropertyChanged(); } }
        }

        public string FormateurNom
        {
            get => _formateurNom;
            set { if (_formateurNom != value) { _formateurNom = value; OnPropertyChanged(); } }
        }

        public StatutSessionFormation Statut
        {
            get => _statut;
            set
            {
                if (_statut != value)
                {
                    _statut = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(StatutLibelle));
                    OnPropertyChanged(nameof(CouleurStatut));
                    OnPropertyChanged(nameof(EstRealisee));
                    OnPropertyChanged(nameof(EstAnnulee));
                }
            }
        }

        public string StatutLibelle
        {
            get
            {
                switch (_statut)
                {
                    case StatutSessionFormation.Planifiee: return "Planifiée";
                    case StatutSessionFormation.Confirmee: return "Confirmée";
                    case StatutSessionFormation.EnCours: return "En cours";
                    case StatutSessionFormation.Realisee: return "Réalisée";
                    case StatutSessionFormation.Annulee: return "Annulée";
                    default: return _statut.ToString();
                }
            }
        }

        public string CouleurStatut
        {
            get
            {
                switch (_statut)
                {
                    case StatutSessionFormation.Planifiee: return "#5C6BC0"; // Indigo
                    case StatutSessionFormation.Confirmee: return "#0288D1"; // Bleu
                    case StatutSessionFormation.EnCours: return "#E65100";   // Ambre
                    case StatutSessionFormation.Realisee: return "#14675A";  // Vert émeraude
                    case StatutSessionFormation.Annulee: return "#C62828";   // Rouge
                    default: return "#757575";
                }
            }
        }

        public bool EstRealisee => _statut == StatutSessionFormation.Realisee;
        public bool EstAnnulee => _statut == StatutSessionFormation.Annulee;

        public decimal CoutPedagogique
        {
            get => _coutPedagogique;
            set
            {
                if (_coutPedagogique != value)
                {
                    _coutPedagogique = value;
                    OnPropertyChanged();
                    RecalculerCoutTotal();
                }
            }
        }

        public decimal CoutLogistique
        {
            get => _coutLogistique;
            set
            {
                if (_coutLogistique != value)
                {
                    _coutLogistique = value;
                    OnPropertyChanged();
                    RecalculerCoutTotal();
                }
            }
        }

        public decimal CoutAutre
        {
            get => _coutAutre;
            set
            {
                if (_coutAutre != value)
                {
                    _coutAutre = value;
                    OnPropertyChanged();
                    RecalculerCoutTotal();
                }
            }
        }

        public decimal CoutTotalReel
        {
            get => _coutTotalReel;
            set
            {
                if (_coutTotalReel != value)
                {
                    _coutTotalReel = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(CoutTotalTexte));
                }
            }
        }

        public string CoutTotalTexte => string.Format(CultureInfo.InvariantCulture, "{0:N0} FCFA", _coutTotalReel);

        public void RecalculerCoutTotal()
        {
            CoutTotalReel = _coutPedagogique + _coutLogistique + _coutAutre;
        }

        public string Commentaire
        {
            get => _commentaire;
            set { if (_commentaire != value) { _commentaire = value; OnPropertyChanged(); } }
        }

        public string FormationCode
        {
            get => _formationCode;
            set { if (_formationCode != value) { _formationCode = value; OnPropertyChanged(); } }
        }

        public string FormationIntitule
        {
            get => _formationIntitule;
            set { if (_formationIntitule != value) { _formationIntitule = value; OnPropertyChanged(); } }
        }

        public int DureeHeures
        {
            get => _dureeHeures;
            set { if (_dureeHeures != value) { _dureeHeures = value; OnPropertyChanged(); } }
        }

        public string OrganismeNom
        {
            get => _organismeNom;
            set { if (_organismeNom != value) { _organismeNom = value; OnPropertyChanged(); } }
        }

        public string OrganismeNumeroAgrement
        {
            get => _organismeNumeroAgrement;
            set { if (_organismeNumeroAgrement != value) { _organismeNumeroAgrement = value; OnPropertyChanged(); } }
        }

        public string PlanCode
        {
            get => _planCode;
            set { if (_planCode != value) { _planCode = value; OnPropertyChanged(); } }
        }

        public string PlanTitre
        {
            get => _planTitre;
            set { if (_planTitre != value) { _planTitre = value; OnPropertyChanged(); } }
        }

        public int NombreInscrits
        {
            get => _nombreInscrits;
            set
            {
                if (_nombreInscrits != value)
                {
                    _nombreInscrits = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(TauxPresencePct));
                    OnPropertyChanged(nameof(TauxPresenceTexte));
                }
            }
        }

        public int NombrePresents
        {
            get => _nombrePresents;
            set
            {
                if (_nombrePresents != value)
                {
                    _nombrePresents = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(TauxPresencePct));
                    OnPropertyChanged(nameof(TauxPresenceTexte));
                }
            }
        }

        public int NombreAttestations
        {
            get => _nombreAttestations;
            set { if (_nombreAttestations != value) { _nombreAttestations = value; OnPropertyChanged(); } }
        }

        public double TauxPresencePct
        {
            get
            {
                if (_nombreInscrits <= 0) return 0.0;
                return Math.Round((double)_nombrePresents / _nombreInscrits * 100.0, 1);
            }
        }

        public string TauxPresenceTexte => $"{TauxPresencePct:F1} %";

        public string DisplayName => string.IsNullOrEmpty(_codeSession)
            ? _intituleSession
            : $"[{_codeSession}] {_intituleSession ?? _formationIntitule}";

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    #endregion

    #region Inscription / Participant

    /// <summary>
    /// Inscription nominative d'un salarié à une session de formation (module RH-04).
    /// </summary>
    public class SessionInscription : INotifyPropertyChanged
    {
        private int _id;
        private int _sessionId;
        private string _employeeId;
        private DateTime _dateInscription = DateTime.Today;
        private StatutPresenceSession _statutPresence = StatutPresenceSession.Inscrit;
        private string _noteEvaluation; // Ex: "17/20", "Acquis", "Très bien"
        private bool _attestationObtenue;
        private decimal _fraisImputes; // en FCFA
        private string _commentaire;

        // Métadonnées dénormalisées
        private string _employeeMatricule;
        private string _employeeNomComplet;
        private string _department;
        private string _posteTitre;
        private string _sessionCode;
        private string _sessionIntitule;
        private string _formationIntitule;
        private int _dureeHeures;
        private DateTime _dateDebut;
        private DateTime _dateFin;

        public int Id
        {
            get => _id;
            set { if (_id != value) { _id = value; OnPropertyChanged(); } }
        }

        public int SessionId
        {
            get => _sessionId;
            set { if (_sessionId != value) { _sessionId = value; OnPropertyChanged(); } }
        }

        public string EmployeeId
        {
            get => _employeeId;
            set { if (_employeeId != value) { _employeeId = value; OnPropertyChanged(); } }
        }

        public DateTime DateInscription
        {
            get => _dateInscription;
            set { if (_dateInscription != value) { _dateInscription = value; OnPropertyChanged(); } }
        }

        public StatutPresenceSession StatutPresence
        {
            get => _statutPresence;
            set
            {
                if (_statutPresence != value)
                {
                    _statutPresence = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(StatutPresenceLibelle));
                    OnPropertyChanged(nameof(StatutPresenceCouleur));
                    OnPropertyChanged(nameof(EstPresent));
                }
            }
        }

        public string StatutPresenceLibelle
        {
            get
            {
                switch (_statutPresence)
                {
                    case StatutPresenceSession.Inscrit: return "Inscrit";
                    case StatutPresenceSession.Present: return "Présent";
                    case StatutPresenceSession.AbsentJustifie: return "Absent justifié";
                    case StatutPresenceSession.Abandon: return "Abandon";
                    default: return _statutPresence.ToString();
                }
            }
        }

        public string StatutPresenceCouleur
        {
            get
            {
                switch (_statutPresence)
                {
                    case StatutPresenceSession.Inscrit: return "#5C6BC0";        // Indigo
                    case StatutPresenceSession.Present: return "#14675A";        // Vert émeraude
                    case StatutPresenceSession.AbsentJustifie: return "#E65100"; // Ambre
                    case StatutPresenceSession.Abandon: return "#C62828";        // Rouge
                    default: return "#757575";
                }
            }
        }

        public bool EstPresent => _statutPresence == StatutPresenceSession.Present;

        public string NoteEvaluation
        {
            get => _noteEvaluation;
            set { if (_noteEvaluation != value) { _noteEvaluation = value; OnPropertyChanged(); } }
        }

        public bool AttestationObtenue
        {
            get => _attestationObtenue;
            set
            {
                if (_attestationObtenue != value)
                {
                    _attestationObtenue = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(AttestationLibelle));
                    OnPropertyChanged(nameof(AttestationCouleur));
                }
            }
        }

        public string AttestationLibelle => _attestationObtenue ? "Délivrée" : "Non délivrée";
        public string AttestationCouleur => _attestationObtenue ? "#14675A" : "#757575";

        public decimal FraisImputes
        {
            get => _fraisImputes;
            set
            {
                if (_fraisImputes != value)
                {
                    _fraisImputes = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(FraisImputesTexte));
                }
            }
        }

        public string FraisImputesTexte => string.Format(CultureInfo.InvariantCulture, "{0:N0} FCFA", _fraisImputes);

        public string Commentaire
        {
            get => _commentaire;
            set { if (_commentaire != value) { _commentaire = value; OnPropertyChanged(); } }
        }

        public string EmployeeMatricule
        {
            get => _employeeMatricule;
            set { if (_employeeMatricule != value) { _employeeMatricule = value; OnPropertyChanged(); } }
        }

        public string EmployeeNomComplet
        {
            get => _employeeNomComplet;
            set { if (_employeeNomComplet != value) { _employeeNomComplet = value; OnPropertyChanged(); } }
        }

        public string Department
        {
            get => _department;
            set { if (_department != value) { _department = value; OnPropertyChanged(); } }
        }

        public string PosteTitre
        {
            get => _posteTitre;
            set { if (_posteTitre != value) { _posteTitre = value; OnPropertyChanged(); } }
        }

        public string SessionCode
        {
            get => _sessionCode;
            set { if (_sessionCode != value) { _sessionCode = value; OnPropertyChanged(); } }
        }

        public string SessionIntitule
        {
            get => _sessionIntitule;
            set { if (_sessionIntitule != value) { _sessionIntitule = value; OnPropertyChanged(); } }
        }

        public string FormationIntitule
        {
            get => _formationIntitule;
            set { if (_formationIntitule != value) { _formationIntitule = value; OnPropertyChanged(); } }
        }

        public int DureeHeures
        {
            get => _dureeHeures;
            set { if (_dureeHeures != value) { _dureeHeures = value; OnPropertyChanged(); } }
        }

        public DateTime DateDebut
        {
            get => _dateDebut;
            set { if (_dateDebut != value) { _dateDebut = value; OnPropertyChanged(); } }
        }

        public DateTime DateFin
        {
            get => _dateFin;
            set { if (_dateFin != value) { _dateFin = value; OnPropertyChanged(); } }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    #endregion

    #region Tableau de Bord TPA & Finances

    /// <summary>
    /// Tableau de bord annuel récapitulant les indicateurs de formation et la contrepartie TPA (module RH-04).
    /// </summary>
    public class TableauBordTpaFormation : INotifyPropertyChanged
    {
        private int _anneeExercice;
        private decimal _masseSalarialeSoumiseTpa; // Cumul AssietteTPA (FCFA)
        private decimal _tauxTpaLegal;             // Ex: 0.03 (3%)
        private decimal _tpaTheoriqueDue;          // Cumul TaxePatronale réelle (FCFA)
        private decimal _budgetPlanFormation;      // FCFA
        private decimal _totalDepensesFormation;   // FCFA
        private int _nombreSalariesFormes;
        private int _nombreHeuresTotal;
        private int _nombreSessionsRealisees;

        public int AnneeExercice
        {
            get => _anneeExercice;
            set { if (_anneeExercice != value) { _anneeExercice = value; OnPropertyChanged(); } }
        }

        public decimal MasseSalarialeSoumiseTpa
        {
            get => _masseSalarialeSoumiseTpa;
            set
            {
                if (_masseSalarialeSoumiseTpa != value)
                {
                    _masseSalarialeSoumiseTpa = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(MasseSalarialeTexte));
                    OnPropertyChanged(nameof(RatioDepensesMassePct));
                    OnPropertyChanged(nameof(RatioDepensesMasseTexte));
                }
            }
        }

        public string MasseSalarialeTexte => string.Format(CultureInfo.InvariantCulture, "{0:N0} FCFA", _masseSalarialeSoumiseTpa);

        public decimal TauxTpaLegal
        {
            get => _tauxTpaLegal;
            set
            {
                if (_tauxTpaLegal != value)
                {
                    _tauxTpaLegal = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(TauxTpaTexte));
                }
            }
        }

        public string TauxTpaTexte => string.Format(CultureInfo.InvariantCulture, "{0:P1}", _tauxTpaLegal);

        public decimal TpaTheoriqueDue
        {
            get => _tpaTheoriqueDue;
            set
            {
                if (_tpaTheoriqueDue != value)
                {
                    _tpaTheoriqueDue = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(TpaDueTexte));
                    OnPropertyChanged(nameof(TauxCouvertureTpaPct));
                    OnPropertyChanged(nameof(TauxCouvertureTpaTexte));
                }
            }
        }

        public string TpaDueTexte => string.Format(CultureInfo.InvariantCulture, "{0:N0} FCFA", _tpaTheoriqueDue);

        public decimal BudgetPlanFormation
        {
            get => _budgetPlanFormation;
            set
            {
                if (_budgetPlanFormation != value)
                {
                    _budgetPlanFormation = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(BudgetPlanTexte));
                }
            }
        }

        public string BudgetPlanTexte => string.Format(CultureInfo.InvariantCulture, "{0:N0} FCFA", _budgetPlanFormation);

        public decimal TotalDepensesFormation
        {
            get => _totalDepensesFormation;
            set
            {
                if (_totalDepensesFormation != value)
                {
                    _totalDepensesFormation = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(TotalDepensesTexte));
                    OnPropertyChanged(nameof(RatioDepensesMassePct));
                    OnPropertyChanged(nameof(RatioDepensesMasseTexte));
                    OnPropertyChanged(nameof(TauxCouvertureTpaPct));
                    OnPropertyChanged(nameof(TauxCouvertureTpaTexte));
                }
            }
        }

        public string TotalDepensesTexte => string.Format(CultureInfo.InvariantCulture, "{0:N0} FCFA", _totalDepensesFormation);

        /// <summary>
        /// Ratio Dépenses réelles / Masse salariale soumise TPA (en %).
        /// Représente l'effort de formation continue de l'employeur.
        /// </summary>
        public double RatioDepensesMassePct
        {
            get
            {
                if (_masseSalarialeSoumiseTpa <= 0m) return 0.0;
                double ratio = (double)(_totalDepensesFormation / _masseSalarialeSoumiseTpa) * 100.0;
                return Math.Round(ratio, 2);
            }
        }

        public string RatioDepensesMasseTexte => $"{RatioDepensesMassePct:F2} %";

        /// <summary>
        /// Taux de couverture de la TPA : Dépenses de formation / TPA due (en %).
        /// Mesure la proportion de la contribution patronale réinvestie en compétences internes.
        /// </summary>
        public double TauxCouvertureTpaPct
        {
            get
            {
                if (_tpaTheoriqueDue <= 0m) return 0.0;
                double ratio = (double)(_totalDepensesFormation / _tpaTheoriqueDue) * 100.0;
                return Math.Round(ratio, 1);
            }
        }

        public string TauxCouvertureTpaTexte => $"{TauxCouvertureTpaPct:F1} %";

        public int NombreSalariesFormes
        {
            get => _nombreSalariesFormes;
            set { if (_nombreSalariesFormes != value) { _nombreSalariesFormes = value; OnPropertyChanged(); } }
        }

        public int NombreHeuresTotal
        {
            get => _nombreHeuresTotal;
            set { if (_nombreHeuresTotal != value) { _nombreHeuresTotal = value; OnPropertyChanged(); } }
        }

        public int NombreSessionsRealisees
        {
            get => _nombreSessionsRealisees;
            set { if (_nombreSessionsRealisees != value) { _nombreSessionsRealisees = value; OnPropertyChanged(); } }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    #endregion
}
