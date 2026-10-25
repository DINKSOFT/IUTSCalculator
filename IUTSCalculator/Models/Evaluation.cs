using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;

namespace IUTSCalculator.Models
{
    #region Énumérations RH-03

    /// <summary>
    /// Statut d'avancement d'une campagne d'évaluation (module RH-03).
    /// </summary>
    public enum StatutCampagne
    {
        [Description("Planifiée (Brouillon)")]
        Planifiee = 0,

        [Description("En cours")]
        EnCours = 1,

        [Description("Clôturée")]
        Cloturee = 2,

        [Description("Archivée")]
        Archivee = 3
    }

    /// <summary>
    /// Statut du cycle de vie d'un entretien individuel (module RH-03).
    /// </summary>
    public enum StatutEntretien
    {
        [Description("Planifié")]
        Planifie = 0,

        [Description("En cours")]
        EnCours = 1,

        [Description("Réalisé (Noté)")]
        Realise = 2,

        [Description("Validé & Signé")]
        Valide = 3,

        [Description("Clôturé (Archivé)")]
        Cloture = 4
    }

    /// <summary>
    /// Type d'objectif individuel traité dans l'entretien (module RH-03).
    /// </summary>
    public enum TypeObjectifEntretien
    {
        [Description("Objectif évalué (Exercice écoulé N-1)")]
        ObjectifPasse = 0,

        [Description("Nouvel objectif fixé (Exercice à venir N+1)")]
        NouvelObjectif = 1
    }

    /// <summary>
    /// Niveau d'urgence ou de priorité d'un besoin de formation (module RH-03).
    /// </summary>
    public enum PrioriteFormation
    {
        [Description("Normale")]
        Normale = 1,

        [Description("Haute")]
        Haute = 2,

        [Description("Urgente")]
        Urgente = 3
    }

    /// <summary>
    /// Statut de prise en compte d'un besoin de formation (passerelle RH-04).
    /// </summary>
    public enum StatutBesoinFormation
    {
        [Description("Demandé")]
        Demande = 0,

        [Description("Retenu")]
        Retenu = 1,

        [Description("Reporté")]
        Reporte = 2,

        [Description("Rejeté")]
        Rejete = 3
    }

    #endregion

    #region Campagne d'évaluation

    /// <summary>
    /// Campagne périodique ou annuelle d'évaluation des performances (module RH-03).
    /// </summary>
    public class CampagneEvaluation : INotifyPropertyChanged
    {
        private int _id;
        private string _code;
        private string _titre;
        private int _anneeExercice = DateTime.Today.Year;
        private DateTime _dateDebut = new DateTime(DateTime.Today.Year, 1, 1);
        private DateTime _dateFin = new DateTime(DateTime.Today.Year, 12, 31);
        private StatutCampagne _statut = StatutCampagne.Planifiee;
        private string _description;
        private DateTime _dateCreation = DateTime.Now;

        // Métadonnées d'avancement calculées
        private int _totalEntretiens;
        private int _entretiensPlanifies;
        private int _entretiensEnCours;
        private int _entretiensRealises;
        private int _entretiensValides;
        private int _evaluateursAAssigner;
        private double _tauxAvancementPct;
        private double _noteMoyenne;

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

        public StatutCampagne Statut
        {
            get => _statut;
            set
            {
                if (_statut != value)
                {
                    _statut = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(StatutLibelle));
                    OnPropertyChanged(nameof(BadgeStatut));
                    OnPropertyChanged(nameof(CouleurStatut));
                    OnPropertyChanged(nameof(EstActive));
                }
            }
        }

        public string Description
        {
            get => _description;
            set { if (_description != value) { _description = value; OnPropertyChanged(); } }
        }

        public DateTime DateCreation
        {
            get => _dateCreation;
            set { if (_dateCreation != value) { _dateCreation = value; OnPropertyChanged(); } }
        }

        public string DisplayName => string.IsNullOrWhiteSpace(Code) ? Titre : $"{Code} — {Titre} ({AnneeExercice})";

        public string PeriodeLibelle => $"{DateDebut:dd/MM/yyyy} au {DateFin:dd/MM/yyyy}";

        public string StatutLibelle
        {
            get
            {
                switch (_statut)
                {
                    case StatutCampagne.Planifiee: return "Planifiée";
                    case StatutCampagne.EnCours: return "En cours";
                    case StatutCampagne.Cloturee: return "Clôturée";
                    case StatutCampagne.Archivee: return "Archivée";
                    default: return _statut.ToString();
                }
            }
        }

        public string BadgeStatut => StatutLibelle.ToUpperInvariant();

        public string CouleurStatut
        {
            get
            {
                switch (_statut)
                {
                    case StatutCampagne.Planifiee: return "#0288D1"; // Bleu
                    case StatutCampagne.EnCours: return "#E65100";   // Ambre
                    case StatutCampagne.Cloturee: return "#14675A";  // Émeraude
                    case StatutCampagne.Archivee: return "#616161";  // Gris
                    default: return "#757575";
                }
            }
        }

        public bool EstActive => _statut == StatutCampagne.EnCours;

        #region Statistiques d'avancement

        public int TotalEntretiens
        {
            get => _totalEntretiens;
            set { if (_totalEntretiens != value) { _totalEntretiens = value; OnPropertyChanged(); } }
        }

        public int EntretiensPlanifies
        {
            get => _entretiensPlanifies;
            set { if (_entretiensPlanifies != value) { _entretiensPlanifies = value; OnPropertyChanged(); } }
        }

        public int EntretiensEnCours
        {
            get => _entretiensEnCours;
            set { if (_entretiensEnCours != value) { _entretiensEnCours = value; OnPropertyChanged(); } }
        }

        public int EntretiensRealises
        {
            get => _entretiensRealises;
            set { if (_entretiensRealises != value) { _entretiensRealises = value; OnPropertyChanged(); } }
        }

        public int EntretiensValides
        {
            get => _entretiensValides;
            set { if (_entretiensValides != value) { _entretiensValides = value; OnPropertyChanged(); } }
        }

        public int EvaluateursAAssigner
        {
            get => _evaluateursAAssigner;
            set { if (_evaluateursAAssigner != value) { _evaluateursAAssigner = value; OnPropertyChanged(); } }
        }

        public double TauxAvancementPct
        {
            get => _tauxAvancementPct;
            set { if (Math.Abs(_tauxAvancementPct - value) > 0.01) { _tauxAvancementPct = value; OnPropertyChanged(); } }
        }

        public double NoteMoyenne
        {
            get => _noteMoyenne;
            set { if (Math.Abs(_noteMoyenne - value) > 0.01) { _noteMoyenne = value; OnPropertyChanged(); } }
        }

        public string NoteMoyenneTexte => _noteMoyenne > 0 ? $"{_noteMoyenne:N2} / 5" : "—";

        #endregion

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    #endregion

    #region Entretien d'évaluation

    /// <summary>
    /// Fiche individuelle d'entretien annuel et d'évaluation (module RH-03).
    /// </summary>
    public class EntretienEvaluation : INotifyPropertyChanged
    {
        private int _id;
        private int _campagneId;
        private string _campagneTitre;
        private int _campagneAnnee;

        private string _employeeId;
        private string _employeeNomComplet;
        private string _employeeService;
        private string _employeePoste;
        private DateTime? _employeeDateEmbauche;
        private string _employeeAnciennete;

        private string _evaluateurMatricule;
        private string _evaluateurNomComplet;
        private string _evaluateurPoste;

        private DateTime? _dateEntretien = DateTime.Today;
        private StatutEntretien _statut = StatutEntretien.Planifie;
        private double _noteGlobale;

        private string _pointsForts;
        private string _axesProgres;
        private string _commentairesSalarie;
        private string _commentairesEvaluateur;
        private string _avisDirectionRH;
        private string _souhaitsEvolution;
        private DateTime? _dateValidation;
        private string _validePar;
        private DateTime _dateCreation = DateTime.Now;

        public EntretienEvaluation()
        {
            Criteres = new ObservableCollection<EntretienCritere>();
            ObjectifsPasses = new ObservableCollection<EntretienObjectif>();
            NouveauxObjectifs = new ObservableCollection<EntretienObjectif>();
            BesoinsFormation = new ObservableCollection<EntretienBesoinFormation>();
        }

        public int Id
        {
            get => _id;
            set { if (_id != value) { _id = value; OnPropertyChanged(); } }
        }

        public int CampagneId
        {
            get => _campagneId;
            set { if (_campagneId != value) { _campagneId = value; OnPropertyChanged(); } }
        }

        public string CampagneTitre
        {
            get => _campagneTitre;
            set { if (_campagneTitre != value) { _campagneTitre = value; OnPropertyChanged(); } }
        }

        public int CampagneAnnee
        {
            get => _campagneAnnee;
            set { if (_campagneAnnee != value) { _campagneAnnee = value; OnPropertyChanged(); } }
        }

        public string EmployeeId
        {
            get => _employeeId;
            set { if (_employeeId != value) { _employeeId = value; OnPropertyChanged(); } }
        }

        public string EmployeeNomComplet
        {
            get => _employeeNomComplet;
            set { if (_employeeNomComplet != value) { _employeeNomComplet = value; OnPropertyChanged(); } }
        }

        public string EmployeeService
        {
            get => _employeeService;
            set { if (_employeeService != value) { _employeeService = value; OnPropertyChanged(); } }
        }

        public string EmployeePoste
        {
            get => _employeePoste;
            set { if (_employeePoste != value) { _employeePoste = value; OnPropertyChanged(); } }
        }

        public DateTime? EmployeeDateEmbauche
        {
            get => _employeeDateEmbauche;
            set { if (_employeeDateEmbauche != value) { _employeeDateEmbauche = value; OnPropertyChanged(); } }
        }

        public string EmployeeAnciennete
        {
            get => _employeeAnciennete;
            set { if (_employeeAnciennete != value) { _employeeAnciennete = value; OnPropertyChanged(); } }
        }

        public string EvaluateurMatricule
        {
            get => _evaluateurMatricule;
            set
            {
                if (_evaluateurMatricule != value)
                {
                    _evaluateurMatricule = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(EvaluateurAAssigner));
                }
            }
        }

        public string EvaluateurNomComplet
        {
            get => _evaluateurNomComplet;
            set { if (_evaluateurNomComplet != value) { _evaluateurNomComplet = value; OnPropertyChanged(); } }
        }

        public string EvaluateurPoste
        {
            get => _evaluateurPoste;
            set { if (_evaluateurPoste != value) { _evaluateurPoste = value; OnPropertyChanged(); } }
        }

        public bool EvaluateurAAssigner => string.IsNullOrWhiteSpace(_evaluateurMatricule);

        public DateTime? DateEntretien
        {
            get => _dateEntretien;
            set
            {
                if (_dateEntretien != value)
                {
                    _dateEntretien = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(DateEntretienTexte));
                }
            }
        }

        public string DateEntretienTexte => _dateEntretien.HasValue ? _dateEntretien.Value.ToString("dd/MM/yyyy") : "Non planifié";

        public StatutEntretien Statut
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
                    OnPropertyChanged(nameof(BadgeStatut));
                    OnPropertyChanged(nameof(EstVerrouille));
                    OnPropertyChanged(nameof(EstModifiable));
                }
            }
        }

        /// <summary>
        /// Règle d'immutabilité stricte : un entretien est verrouillé dès qu'il est validé ou clôturé.
        /// </summary>
        public bool EstVerrouille => _statut >= StatutEntretien.Valide;

        /// <summary>
        /// Indique si l'entretien peut encore être modifié.
        /// </summary>
        public bool EstModifiable => !EstVerrouille;

        public double NoteGlobale
        {
            get => _noteGlobale;
            set
            {
                if (Math.Abs(_noteGlobale - value) > 0.001)
                {
                    _noteGlobale = Math.Round(value, 2);
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(NoteGlobaleFormatee));
                    OnPropertyChanged(nameof(NoteEtoiles));
                    OnPropertyChanged(nameof(MentionGlobale));
                    OnPropertyChanged(nameof(CouleurMention));
                }
            }
        }

        public string NoteGlobaleFormatee => _noteGlobale > 0 ? $"{_noteGlobale:F2} / 5" : "—";

        public string NoteEtoiles
        {
            get
            {
                if (_noteGlobale <= 0) return "☆☆☆☆☆";
                int arrondis = (int)Math.Round(_noteGlobale);
                arrondis = Math.Max(1, Math.Min(5, arrondis));
                return new string('★', arrondis) + new string('☆', 5 - arrondis);
            }
        }

        public string MentionGlobale
        {
            get
            {
                if (_noteGlobale <= 0) return "Non noté";
                if (_noteGlobale < 2.0) return "Insuffisant";
                if (_noteGlobale < 3.0) return "À développer";
                if (_noteGlobale < 4.0) return "Conforme aux attentes";
                if (_noteGlobale < 4.5) return "Dépasse les attentes";
                return "Remarquable / Exceptionnel";
            }
        }

        public string CouleurMention
        {
            get
            {
                if (_noteGlobale <= 0) return "#757575";
                if (_noteGlobale < 2.0) return "#C62828"; // Rouge
                if (_noteGlobale < 3.0) return "#E65100"; // Ambre
                if (_noteGlobale < 4.0) return "#14675A"; // Vert émeraude
                if (_noteGlobale < 4.5) return "#1B5E20"; // Vert foncé
                return "#6A1B9A"; // Violet exceptionnel
            }
        }

        public string StatutLibelle
        {
            get
            {
                switch (_statut)
                {
                    case StatutEntretien.Planifie: return "Planifié";
                    case StatutEntretien.EnCours: return "En cours";
                    case StatutEntretien.Realise: return "Réalisé";
                    case StatutEntretien.Valide: return "Validé & Signé";
                    case StatutEntretien.Cloture: return "Clôturé";
                    default: return _statut.ToString();
                }
            }
        }

        public string BadgeStatut => StatutLibelle.ToUpperInvariant();

        public string CouleurStatut
        {
            get
            {
                switch (_statut)
                {
                    case StatutEntretien.Planifie: return "#0288D1"; // Bleu
                    case StatutEntretien.EnCours: return "#E65100";  // Ambre
                    case StatutEntretien.Realise: return "#14675A";  // Vert émeraude
                    case StatutEntretien.Valide: return "#1B5E20";   // Vert validation
                    case StatutEntretien.Cloture: return "#616161";  // Gris clôture
                    default: return "#757575";
                }
            }
        }

        public string PointsForts
        {
            get => _pointsForts;
            set { if (_pointsForts != value) { _pointsForts = value; OnPropertyChanged(); } }
        }

        public string AxesProgres
        {
            get => _axesProgres;
            set { if (_axesProgres != value) { _axesProgres = value; OnPropertyChanged(); } }
        }

        public string CommentairesSalarie
        {
            get => _commentairesSalarie;
            set { if (_commentairesSalarie != value) { _commentairesSalarie = value; OnPropertyChanged(); } }
        }

        public string CommentairesEvaluateur
        {
            get => _commentairesEvaluateur;
            set { if (_commentairesEvaluateur != value) { _commentairesEvaluateur = value; OnPropertyChanged(); } }
        }

        public string AvisDirectionRH
        {
            get => _avisDirectionRH;
            set { if (_avisDirectionRH != value) { _avisDirectionRH = value; OnPropertyChanged(); } }
        }

        public string SouhaitsEvolution
        {
            get => _souhaitsEvolution;
            set { if (_souhaitsEvolution != value) { _souhaitsEvolution = value; OnPropertyChanged(); } }
        }

        /// <summary>
        /// Date et heure de la validation (« Valider &amp; Sceller »), scellée par le service
        /// au moment du passage au statut Validé.
        /// </summary>
        public DateTime? DateValidation
        {
            get => _dateValidation;
            set { if (_dateValidation != value) { _dateValidation = value; OnPropertyChanged(); OnPropertyChanged(nameof(ValidationLibelle)); } }
        }

        /// <summary>
        /// Utilisateur de l'application connecté lors de la validation (nom affiché, sinon identifiant).
        /// </summary>
        public string ValidePar
        {
            get => _validePar;
            set { if (_validePar != value) { _validePar = value; OnPropertyChanged(); OnPropertyChanged(nameof(ValidationLibelle)); } }
        }

        /// <summary>
        /// Mention de scellement affichée à l'écran et sur la fiche PDF
        /// (ex. « Validé et scellé le 20/10/2026 à 14:32 par Awa SANOGO »).
        /// Vide tant que l'entretien n'a pas de date de validation.
        /// </summary>
        public string ValidationLibelle
        {
            get
            {
                if (!_dateValidation.HasValue) return string.Empty;
                string texte = $"Validé et scellé le {_dateValidation.Value:dd/MM/yyyy} à {_dateValidation.Value:HH:mm}";
                if (!string.IsNullOrWhiteSpace(_validePar)) texte += $" par {_validePar}";
                return texte;
            }
        }

        public DateTime DateCreation
        {
            get => _dateCreation;
            set { if (_dateCreation != value) { _dateCreation = value; OnPropertyChanged(); } }
        }

        public ObservableCollection<EntretienCritere> Criteres { get; }
        public ObservableCollection<EntretienObjectif> ObjectifsPasses { get; }
        public ObservableCollection<EntretienObjectif> NouveauxObjectifs { get; }
        public ObservableCollection<EntretienBesoinFormation> BesoinsFormation { get; }

        public double TauxAtteinteMoyenObjectifsPasses
        {
            get
            {
                if (ObjectifsPasses == null || ObjectifsPasses.Count == 0) return 0.0;
                int totalPoids = ObjectifsPasses.Sum(o => o.Poids > 0 ? o.Poids : 1);
                double sommePonderee = ObjectifsPasses.Sum(o => o.TauxAtteintePct * (o.Poids > 0 ? o.Poids : 1));
                return Math.Round(sommePonderee / totalPoids, 1);
            }
        }

        public string TauxAtteinteMoyenTexte => ObjectifsPasses != null && ObjectifsPasses.Count > 0
            ? $"{TauxAtteinteMoyenObjectifsPasses:N1} %"
            : "—";

        /// <summary>
        /// Recalcule la note globale moyenne pondérée des critères.
        /// Uniquement si l'entretien n'est pas verrouillé.
        /// </summary>
        public void RecalculerNoteGlobale()
        {
            if (EstVerrouille) return;

            if (Criteres == null || Criteres.Count == 0)
            {
                NoteGlobale = 0.0;
                return;
            }

            int totalPoids = Criteres.Sum(c => c.Poids > 0 ? c.Poids : 1);
            double somme = Criteres.Sum(c => c.Note * (c.Poids > 0 ? c.Poids : 1));
            NoteGlobale = Math.Round(somme / totalPoids, 2);
            OnPropertyChanged(nameof(TauxAtteinteMoyenObjectifsPasses));
            OnPropertyChanged(nameof(TauxAtteinteMoyenTexte));
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    #endregion

    #region Critères & Compétences évalués

    /// <summary>
    /// Critère unitaire ou compétence évaluée au cours de l'entretien (module RH-03).
    /// </summary>
    public class EntretienCritere : INotifyPropertyChanged
    {
        private int _id;
        private int _entretienId;
        private int? _competenceId;
        private string _categorie = "Compétences Métier";
        private string _libelle;
        private int _note = 3;
        private int _poids = 1;
        private string _commentaire;

        public int Id
        {
            get => _id;
            set { if (_id != value) { _id = value; OnPropertyChanged(); } }
        }

        public int EntretienId
        {
            get => _entretienId;
            set { if (_entretienId != value) { _entretienId = value; OnPropertyChanged(); } }
        }

        public int? CompetenceId
        {
            get => _competenceId;
            set { if (_competenceId != value) { _competenceId = value; OnPropertyChanged(); } }
        }

        public string Categorie
        {
            get => _categorie;
            set { if (_categorie != value) { _categorie = value; OnPropertyChanged(); } }
        }

        public string Libelle
        {
            get => _libelle;
            set { if (_libelle != value) { _libelle = value; OnPropertyChanged(); } }
        }

        public int Note
        {
            get => _note;
            set
            {
                int val = Math.Max(1, Math.Min(5, value));
                if (_note != val)
                {
                    _note = val;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(NoteEtoiles));
                    OnPropertyChanged(nameof(NiveauLibelle));
                }
            }
        }

        public int Poids
        {
            get => _poids;
            set { if (_poids != value) { _poids = Math.Max(1, value); OnPropertyChanged(); } }
        }

        public string Commentaire
        {
            get => _commentaire;
            set { if (_commentaire != value) { _commentaire = value; OnPropertyChanged(); } }
        }

        public string NoteEtoiles => new string('★', _note) + new string('☆', 5 - _note);

        public string NiveauLibelle
        {
            get
            {
                switch (_note)
                {
                    case 1: return "1 - Insuffisant";
                    case 2: return "2 - À développer";
                    case 3: return "3 - Conforme aux attentes";
                    case 4: return "4 - Dépasse les attentes";
                    case 5: return "5 - Remarquable";
                    default: return _note.ToString();
                }
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    #endregion

    #region Objectifs individuels

    /// <summary>
    /// Objectif individuel (passé évalué ou nouveau fixé) (module RH-03).
    /// </summary>
    public class EntretienObjectif : INotifyPropertyChanged
    {
        private int _id;
        private int _entretienId;
        private TypeObjectifEntretien _typeObjectif = TypeObjectifEntretien.NouvelObjectif;
        private string _intitule;
        private string _indicateurMesure;
        private string _cibleAttendue;
        private string _resultatObtenu;
        private int _tauxAtteintePct;
        private string _appreciation;
        private string _echeance;
        private int _poids = 1;

        public int Id
        {
            get => _id;
            set { if (_id != value) { _id = value; OnPropertyChanged(); } }
        }

        public int EntretienId
        {
            get => _entretienId;
            set { if (_entretienId != value) { _entretienId = value; OnPropertyChanged(); } }
        }

        public TypeObjectifEntretien TypeObjectif
        {
            get => _typeObjectif;
            set
            {
                if (_typeObjectif != value)
                {
                    _typeObjectif = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(TypeObjectifLibelle));
                }
            }
        }

        public string TypeObjectifLibelle => _typeObjectif == TypeObjectifEntretien.ObjectifPasse ? "Évalué (N-1)" : "Fixé (N+1)";

        public string Intitule
        {
            get => _intitule;
            set { if (_intitule != value) { _intitule = value; OnPropertyChanged(); } }
        }

        public string IndicateurMesure
        {
            get => _indicateurMesure;
            set { if (_indicateurMesure != value) { _indicateurMesure = value; OnPropertyChanged(); } }
        }

        public string CibleAttendue
        {
            get => _cibleAttendue;
            set { if (_cibleAttendue != value) { _cibleAttendue = value; OnPropertyChanged(); } }
        }

        public string ResultatObtenu
        {
            get => _resultatObtenu;
            set { if (_resultatObtenu != value) { _resultatObtenu = value; OnPropertyChanged(); } }
        }

        public int TauxAtteintePct
        {
            get => _tauxAtteintePct;
            set
            {
                int val = Math.Max(0, value);
                if (_tauxAtteintePct != val)
                {
                    _tauxAtteintePct = val;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(TauxAtteinteTexte));
                    OnPropertyChanged(nameof(CouleurTaux));
                }
            }
        }

        public string TauxAtteinteTexte => $"{_tauxAtteintePct} %";

        public string CouleurTaux
        {
            get
            {
                if (_tauxAtteintePct >= 100) return "#1B5E20"; // Vert
                if (_tauxAtteintePct >= 75) return "#14675A";  // Vert émeraude
                if (_tauxAtteintePct >= 50) return "#E65100";  // Ambre
                return "#C62828";                             // Rouge
            }
        }

        public string Appreciation
        {
            get => _appreciation;
            set { if (_appreciation != value) { _appreciation = value; OnPropertyChanged(); } }
        }

        public string Echeance
        {
            get => _echeance;
            set { if (_echeance != value) { _echeance = value; OnPropertyChanged(); } }
        }

        public int Poids
        {
            get => _poids;
            set { if (_poids != value) { _poids = Math.Max(1, value); OnPropertyChanged(); } }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    #endregion

    #region Besoins en formation

    /// <summary>
    /// Besoin en formation identifié lors de l'entretien (module RH-03, passerelle RH-04).
    /// </summary>
    public class EntretienBesoinFormation : INotifyPropertyChanged
    {
        private int _id;
        private int _entretienId;
        private int? _competenceId;
        private string _themeFormation;
        private string _competenceVisee;
        private PrioriteFormation _priorite = PrioriteFormation.Normale;
        private string _avisEvaluateur;
        private StatutBesoinFormation _statut = StatutBesoinFormation.Demande;

        public int Id
        {
            get => _id;
            set { if (_id != value) { _id = value; OnPropertyChanged(); } }
        }

        public int EntretienId
        {
            get => _entretienId;
            set { if (_entretienId != value) { _entretienId = value; OnPropertyChanged(); } }
        }

        public int? CompetenceId
        {
            get => _competenceId;
            set { if (_competenceId != value) { _competenceId = value; OnPropertyChanged(); } }
        }

        public string ThemeFormation
        {
            get => _themeFormation;
            set { if (_themeFormation != value) { _themeFormation = value; OnPropertyChanged(); } }
        }

        public string CompetenceVisee
        {
            get => _competenceVisee;
            set { if (_competenceVisee != value) { _competenceVisee = value; OnPropertyChanged(); } }
        }

        public PrioriteFormation Priorite
        {
            get => _priorite;
            set
            {
                if (_priorite != value)
                {
                    _priorite = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(PrioriteLibelle));
                    OnPropertyChanged(nameof(CouleurPriorite));
                }
            }
        }

        public string PrioriteLibelle
        {
            get
            {
                switch (_priorite)
                {
                    case PrioriteFormation.Normale: return "Normale";
                    case PrioriteFormation.Haute: return "Haute";
                    case PrioriteFormation.Urgente: return "Urgente";
                    default: return _priorite.ToString();
                }
            }
        }

        public string CouleurPriorite
        {
            get
            {
                switch (_priorite)
                {
                    case PrioriteFormation.Normale: return "#0288D1"; // Bleu
                    case PrioriteFormation.Haute: return "#E65100";   // Ambre
                    case PrioriteFormation.Urgente: return "#C62828"; // Rouge
                    default: return "#757575";
                }
            }
        }

        public string AvisEvaluateur
        {
            get => _avisEvaluateur;
            set { if (_avisEvaluateur != value) { _avisEvaluateur = value; OnPropertyChanged(); } }
        }

        public StatutBesoinFormation Statut
        {
            get => _statut;
            set
            {
                if (_statut != value)
                {
                    _statut = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(StatutLibelle));
                }
            }
        }

        public string StatutLibelle
        {
            get
            {
                switch (_statut)
                {
                    case StatutBesoinFormation.Demande: return "Demandé";
                    case StatutBesoinFormation.Retenu: return "Retenu";
                    case StatutBesoinFormation.Reporte: return "Reporté";
                    case StatutBesoinFormation.Rejete: return "Rejeté";
                    default: return _statut.ToString();
                }
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    #endregion

    #region DTOs Statistiques & Historique

    /// <summary>
    /// Statistiques consolidées pour les cartes KPIs de la campagne d'évaluation.
    /// </summary>
    public class StatistiquesCampagne
    {
        public int TotalSalariesCibles { get; set; }
        public int EntretiensPlanifies { get; set; }
        public int EntretiensEnCours { get; set; }
        public int EntretiensRealises { get; set; }
        public int EntretiensValides { get; set; }
        public int EntretiensClotures { get; set; }
        public int EvaluateursAAssignerCount { get; set; }

        public double TauxAvancementPct => TotalSalariesCibles > 0
            ? Math.Round((double)(EntretiensRealises + EntretiensValides + EntretiensClotures) / TotalSalariesCibles * 100.0, 1)
            : 0.0;

        public double TauxValidationPct => TotalSalariesCibles > 0
            ? Math.Round((double)(EntretiensValides + EntretiensClotures) / TotalSalariesCibles * 100.0, 1)
            : 0.0;

        public double NoteMoyenneGlobale { get; set; }

        public int CountInsuffisant { get; set; }
        public int CountADevelopper { get; set; }
        public int CountConforme { get; set; }
        public int CountDepasse { get; set; }
        public int CountRemarquable { get; set; }
    }

    /// <summary>
    /// Ligne récapitulative pour la vue d'historique de carrière d'un collaborateur.
    /// </summary>
    public class HistoriqueEntretienSalarie
    {
        public int EntretienId { get; set; }
        public int CampagneId { get; set; }
        public string CampagneCode { get; set; }
        public string CampagneTitre { get; set; }
        public int AnneeExercice { get; set; }
        public DateTime? DateEntretien { get; set; }
        public string DateEntretienTexte => DateEntretien.HasValue ? DateEntretien.Value.ToString("dd/MM/yyyy") : "—";
        public string EvaluateurNom { get; set; }
        public string EvaluateurPoste { get; set; }
        public double NoteGlobale { get; set; }
        public string NoteFormatee => NoteGlobale > 0 ? $"{NoteGlobale:F2} / 5" : "—";
        public string MentionGlobale { get; set; }
        public StatutEntretien Statut { get; set; }
        public string StatutLibelle { get; set; }
        public string CouleurStatut { get; set; }
        public int ObjectifsPassesCount { get; set; }
        public double TauxAtteinteMoyen { get; set; }
        public int NouveauxObjectifsCount { get; set; }
        public int FormationsCount { get; set; }
    }

    #endregion
}
