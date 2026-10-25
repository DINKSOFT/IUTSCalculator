using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace IUTSCalculator.Models
{
    #region Énumérations RH-05

    /// <summary>
    /// Motif justifiant l'ouverture d'un besoin de recrutement (module RH-05).
    /// </summary>
    public enum MotifRecrutement
    {
        [Description("Création de poste")]
        CreationPoste = 0,

        [Description("Remplacement")]
        Remplacement = 1,

        [Description("Renfort temporaire")]
        RenfortTemporaire = 2,

        [Description("Stage de pré-embauche")]
        StagePreEmbauche = 3
    }

    /// <summary>
    /// Statut du cycle de vie d'un besoin de recrutement.
    /// </summary>
    public enum StatutBesoinRecrutement
    {
        [Description("Brouillon")]
        Brouillon = 0,

        [Description("Ouvert")]
        Ouvert = 1,

        [Description("En cours")]
        EnCours = 2,

        [Description("Pourvu")]
        Pourvu = 3,

        [Description("Annulé")]
        Annule = 4
    }

    /// <summary>
    /// Canal d'acquisition ou source de provenance de la candidature.
    /// </summary>
    public enum CanalSourceCandidature
    {
        [Description("Site Web / Portail")]
        SiteWeb = 0,

        [Description("Réseaux Sociaux / LinkedIn")]
        ReseauxSociaux = 1,

        [Description("Cooptation interne")]
        Cooptation = 2,

        [Description("Cabinet de recrutement")]
        CabinetRecrutement = 3,

        [Description("Candidature spontanée")]
        CandidatureSpontanee = 4,

        [Description("ANPE / Partenaire public")]
        ANPEOuPartenaire = 5,

        [Description("Autre canal")]
        Autre = 6
    }

    /// <summary>
    /// Étape du pipeline de sélection (énumération unifiée de RH-05).
    /// </summary>
    public enum EtapeRecrutement
    {
        [Description("Réception de la candidature")]
        ReceptionCandidature = 0,

        [Description("Tri & Présélection sur dossier")]
        TriEtPreselection = 1,

        [Description("Test technique ou écrit")]
        TestTechniqueOuEcrit = 2,

        [Description("Entretien RH")]
        EntretienRH = 3,

        [Description("Entretien Manager N+1")]
        EntretienManagerN1 = 4,

        [Description("Entretien Direction")]
        EntretienDirection = 5,

        [Description("Décision & Offre formelle")]
        DecisionEtOffre = 6,

        [Description("Finalisée")]
        Finalisee = 7
    }

    /// <summary>
    /// Statut de traitement d'une candidature au sein du pipeline.
    /// </summary>
    public enum StatutCandidature
    {
        [Description("Nouvelle")]
        Nouvelle = 0,

        [Description("En évaluation")]
        EnEvaluation = 1,

        [Description("Retenue")]
        Retenue = 2,

        [Description("Rejetée")]
        Rejetee = 3,

        [Description("Embauchée")]
        Embauchee = 4,

        [Description("Désistée")]
        Desistee = 5
    }

    /// <summary>
    /// Avis formulé par le recruteur lors d'une évaluation ou d'un entretien.
    /// </summary>
    public enum AvisRecruteur
    {
        [Description("Très Favorable")]
        TresFavorable = 0,

        [Description("Favorable")]
        Favorable = 1,

        [Description("Sous Réserve")]
        Reserve = 2,

        [Description("Défavorable")]
        Defavorable = 3
    }

    #endregion

    #region Classes Métier RH-05

    /// <summary>
    /// Expression ou ouverture d'un besoin de recrutement.
    /// </summary>
    public class BesoinRecrutement : INotifyPropertyChanged
    {
        private int _id;
        private string _reference = string.Empty;
        private string _intitulePoste = string.Empty;
        private int? _posteId;
        private string _departement = string.Empty;
        private MotifRecrutement _motif = MotifRecrutement.CreationPoste;
        private int _nombrePostes = 1;
        private int _nombreRecrutes = 0;
        private string _typeContratVise = "CDI";
        private decimal? _remunerationProposeeMin;
        private decimal? _remunerationProposeeMax;
        private DateTime _dateExpression = DateTime.Today;
        private DateTime? _dateEcheanceSouhaitee;
        private DateTime? _dateCloture;
        private StatutBesoinRecrutement _statut = StatutBesoinRecrutement.Brouillon;
        private string _responsableRecrutement = string.Empty;
        private string _descriptionProfil = string.Empty;
        private DateTime _dateCreation = DateTime.Now;

        // Propriétés de commodité (affichage / relations)
        private string _posteLibelle = string.Empty;
        private int _nombreCandidatures;
        private int _nombreEnCours;

        public int Id
        {
            get => _id;
            set { if (_id != value) { _id = value; OnPropertyChanged(); } }
        }

        public string Reference
        {
            get => _reference;
            set { if (_reference != value) { _reference = value; OnPropertyChanged(); } }
        }

        public string IntitulePoste
        {
            get => _intitulePoste;
            set { if (_intitulePoste != value) { _intitulePoste = value; OnPropertyChanged(); } }
        }

        public int? PosteId
        {
            get => _posteId;
            set { if (_posteId != value) { _posteId = value; OnPropertyChanged(); } }
        }

        public string Departement
        {
            get => _departement;
            set { if (_departement != value) { _departement = value; OnPropertyChanged(); } }
        }

        public MotifRecrutement Motif
        {
            get => _motif;
            set { if (_motif != value) { _motif = value; OnPropertyChanged(); OnPropertyChanged(nameof(MotifLibelle)); } }
        }

        public int NombrePostes
        {
            get => _nombrePostes;
            set { if (_nombrePostes != value) { _nombrePostes = value; OnPropertyChanged(); OnPropertyChanged(nameof(PostesRestants)); } }
        }

        public int NombreRecrutes
        {
            get => _nombreRecrutes;
            set { if (_nombreRecrutes != value) { _nombreRecrutes = value; OnPropertyChanged(); OnPropertyChanged(nameof(PostesRestants)); } }
        }

        public int PostesRestants => Math.Max(0, NombrePostes - NombreRecrutes);

        public string TypeContratVise
        {
            get => _typeContratVise;
            set { if (_typeContratVise != value) { _typeContratVise = value; OnPropertyChanged(); } }
        }

        public decimal? RemunerationProposeeMin
        {
            get => _remunerationProposeeMin;
            set { if (_remunerationProposeeMin != value) { _remunerationProposeeMin = value; OnPropertyChanged(); } }
        }

        public decimal? RemunerationProposeeMax
        {
            get => _remunerationProposeeMax;
            set { if (_remunerationProposeeMax != value) { _remunerationProposeeMax = value; OnPropertyChanged(); } }
        }

        public DateTime DateExpression
        {
            get => _dateExpression;
            set { if (_dateExpression != value) { _dateExpression = value; OnPropertyChanged(); } }
        }

        public DateTime? DateEcheanceSouhaitee
        {
            get => _dateEcheanceSouhaitee;
            set { if (_dateEcheanceSouhaitee != value) { _dateEcheanceSouhaitee = value; OnPropertyChanged(); } }
        }

        public DateTime? DateCloture
        {
            get => _dateCloture;
            set { if (_dateCloture != value) { _dateCloture = value; OnPropertyChanged(); } }
        }

        public StatutBesoinRecrutement Statut
        {
            get => _statut;
            set
            {
                if (_statut != value)
                {
                    _statut = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(StatutLibelle));
                    OnPropertyChanged(nameof(PeutCandidater));
                }
            }
        }

        public string ResponsableRecrutement
        {
            get => _responsableRecrutement;
            set { if (_responsableRecrutement != value) { _responsableRecrutement = value; OnPropertyChanged(); } }
        }

        public string DescriptionProfil
        {
            get => _descriptionProfil;
            set { if (_descriptionProfil != value) { _descriptionProfil = value; OnPropertyChanged(); } }
        }

        public DateTime DateCreation
        {
            get => _dateCreation;
            set { if (_dateCreation != value) { _dateCreation = value; OnPropertyChanged(); } }
        }

        public string PosteLibelle
        {
            get => _posteLibelle;
            set { if (_posteLibelle != value) { _posteLibelle = value; OnPropertyChanged(); } }
        }

        public int NombreCandidatures
        {
            get => _nombreCandidatures;
            set { if (_nombreCandidatures != value) { _nombreCandidatures = value; OnPropertyChanged(); } }
        }

        public int NombreEnCours
        {
            get => _nombreEnCours;
            set { if (_nombreEnCours != value) { _nombreEnCours = value; OnPropertyChanged(); } }
        }

        public string MotifLibelle => RecrutementHelper.GetDescription(Motif);
        public string StatutLibelle => RecrutementHelper.GetDescription(Statut);
        public bool PeutCandidater => Statut == StatutBesoinRecrutement.Ouvert || Statut == StatutBesoinRecrutement.EnCours;

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    /// <summary>
    /// Profil unique d'un candidat présent dans le vivier de compétences.
    /// </summary>
    public class Candidat : INotifyPropertyChanged
    {
        private int _id;
        private string _nom = string.Empty;
        private string _prenoms = string.Empty;
        private string _email = string.Empty;
        private string _telephone = string.Empty;
        private string _diplomeLePlusEleve = string.Empty;
        private int _anneesExperience = 0;
        private string _derniereFonction = string.Empty;
        private string _competencesCles = string.Empty;
        private string _cvChemin = string.Empty;
        private bool _vivierActif = true;
        private string _commentaires = string.Empty;
        private DateTime _dateCreation = DateTime.Now;

        // Propriétés dérivées
        private int _nombreCandidatures;

        public int Id
        {
            get => _id;
            set { if (_id != value) { _id = value; OnPropertyChanged(); } }
        }

        public string Nom
        {
            get => _nom;
            set { if (_nom != value) { _nom = value; OnPropertyChanged(); OnPropertyChanged(nameof(NomComplet)); } }
        }

        public string Prenoms
        {
            get => _prenoms;
            set { if (_prenoms != value) { _prenoms = value; OnPropertyChanged(); OnPropertyChanged(nameof(NomComplet)); } }
        }

        public string NomComplet => $"{Nom} {Prenoms}".Trim();

        public string Email
        {
            get => _email;
            set { if (_email != value) { _email = value; OnPropertyChanged(); } }
        }

        public string Telephone
        {
            get => _telephone;
            set { if (_telephone != value) { _telephone = value; OnPropertyChanged(); } }
        }

        public string DiplomeLePlusEleve
        {
            get => _diplomeLePlusEleve;
            set { if (_diplomeLePlusEleve != value) { _diplomeLePlusEleve = value; OnPropertyChanged(); } }
        }

        public int AnneesExperience
        {
            get => _anneesExperience;
            set { if (_anneesExperience != value) { _anneesExperience = value; OnPropertyChanged(); } }
        }

        public string DerniereFonction
        {
            get => _derniereFonction;
            set { if (_derniereFonction != value) { _derniereFonction = value; OnPropertyChanged(); } }
        }

        public string CompetencesCles
        {
            get => _competencesCles;
            set { if (_competencesCles != value) { _competencesCles = value; OnPropertyChanged(); } }
        }

        public string CvChemin
        {
            get => _cvChemin;
            set { if (_cvChemin != value) { _cvChemin = value; OnPropertyChanged(); OnPropertyChanged(nameof(CvNomFichier)); } }
        }

        /// <summary>Nom du fichier de CV à afficher dans l'interface (sans le chemin complet).</summary>
        public string CvNomFichier => string.IsNullOrWhiteSpace(CvChemin) ? "Aucun CV importé" : System.IO.Path.GetFileName(CvChemin);

        public bool VivierActif
        {
            get => _vivierActif;
            set { if (_vivierActif != value) { _vivierActif = value; OnPropertyChanged(); OnPropertyChanged(nameof(VivierActifLibelle)); } }
        }

        public string VivierActifLibelle => VivierActif ? "Actif dans le vivier" : "Archivé";

        public string Commentaires
        {
            get => _commentaires;
            set { if (_commentaires != value) { _commentaires = value; OnPropertyChanged(); } }
        }

        public DateTime DateCreation
        {
            get => _dateCreation;
            set { if (_dateCreation != value) { _dateCreation = value; OnPropertyChanged(); } }
        }

        public int NombreCandidatures
        {
            get => _nombreCandidatures;
            set { if (_nombreCandidatures != value) { _nombreCandidatures = value; OnPropertyChanged(); } }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    /// <summary>
    /// Postulation d'un candidat à un besoin de recrutement spécifique (Pipeline).
    /// </summary>
    public class Candidature : INotifyPropertyChanged
    {
        private int _id;
        private int _besoinId;
        private int _candidatId;
        private DateTime _datePostulation = DateTime.Today;
        private CanalSourceCandidature _canalSource = CanalSourceCandidature.SiteWeb;
        private decimal? _pretentionSalariale;
        private EtapeRecrutement _etapeActuelle = EtapeRecrutement.ReceptionCandidature;
        private StatutCandidature _statut = StatutCandidature.Nouvelle;
        private double? _scoreEvaluation;
        private AvisRecruteur? _avisGlobal;
        private string _commentaireEvaluation = string.Empty;
        private DateTime? _dateEmbauche;
        private decimal? _salaireNegocie;
        private string _salarieCreeMatricule;
        private DateTime _dateCreation = DateTime.Now;

        // Propriétés de jointure (enrichissement UI)
        private string _candidatNomComplet = string.Empty;
        private string _candidatEmail = string.Empty;
        private string _candidatTelephone = string.Empty;
        private string _candidatDiplome = string.Empty;
        private int _candidatExperience;
        private string _besoinReference = string.Empty;
        private string _besoinIntitulePoste = string.Empty;
        private string _besoinDepartement = string.Empty;
        private int? _besoinPosteId;

        public int Id
        {
            get => _id;
            set { if (_id != value) { _id = value; OnPropertyChanged(); } }
        }

        public int BesoinId
        {
            get => _besoinId;
            set { if (_besoinId != value) { _besoinId = value; OnPropertyChanged(); } }
        }

        public int CandidatId
        {
            get => _candidatId;
            set { if (_candidatId != value) { _candidatId = value; OnPropertyChanged(); } }
        }

        public DateTime DatePostulation
        {
            get => _datePostulation;
            set { if (_datePostulation != value) { _datePostulation = value; OnPropertyChanged(); } }
        }

        public CanalSourceCandidature CanalSource
        {
            get => _canalSource;
            set { if (_canalSource != value) { _canalSource = value; OnPropertyChanged(); OnPropertyChanged(nameof(CanalSourceLibelle)); } }
        }

        public decimal? PretentionSalariale
        {
            get => _pretentionSalariale;
            set { if (_pretentionSalariale != value) { _pretentionSalariale = value; OnPropertyChanged(); } }
        }

        public EtapeRecrutement EtapeActuelle
        {
            get => _etapeActuelle;
            set { if (_etapeActuelle != value) { _etapeActuelle = value; OnPropertyChanged(); OnPropertyChanged(nameof(EtapeActuelleLibelle)); } }
        }

        public StatutCandidature Statut
        {
            get => _statut;
            set
            {
                if (_statut != value)
                {
                    _statut = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(StatutLibelle));
                    OnPropertyChanged(nameof(EstEmbauchee));
                    OnPropertyChanged(nameof(EstVerrouillee));
                }
            }
        }

        public double? ScoreEvaluation
        {
            get => _scoreEvaluation;
            set { if (_scoreEvaluation != value) { _scoreEvaluation = value; OnPropertyChanged(); } }
        }

        public AvisRecruteur? AvisGlobal
        {
            get => _avisGlobal;
            set { if (_avisGlobal != value) { _avisGlobal = value; OnPropertyChanged(); OnPropertyChanged(nameof(AvisGlobalLibelle)); } }
        }

        public string CommentaireEvaluation
        {
            get => _commentaireEvaluation;
            set { if (_commentaireEvaluation != value) { _commentaireEvaluation = value; OnPropertyChanged(); } }
        }

        public DateTime? DateEmbauche
        {
            get => _dateEmbauche;
            set { if (_dateEmbauche != value) { _dateEmbauche = value; OnPropertyChanged(); } }
        }

        public decimal? SalaireNegocie
        {
            get => _salaireNegocie;
            set { if (_salaireNegocie != value) { _salaireNegocie = value; OnPropertyChanged(); } }
        }

        public string SalarieCreeMatricule
        {
            get => _salarieCreeMatricule;
            set
            {
                if (_salarieCreeMatricule != value)
                {
                    _salarieCreeMatricule = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(EstEmbauchee));
                    OnPropertyChanged(nameof(EstVerrouillee));
                }
            }
        }

        public DateTime DateCreation
        {
            get => _dateCreation;
            set { if (_dateCreation != value) { _dateCreation = value; OnPropertyChanged(); } }
        }

        // Champs de jointure
        public string CandidatNomComplet
        {
            get => _candidatNomComplet;
            set { if (_candidatNomComplet != value) { _candidatNomComplet = value; OnPropertyChanged(); } }
        }

        public string CandidatEmail
        {
            get => _candidatEmail;
            set { if (_candidatEmail != value) { _candidatEmail = value; OnPropertyChanged(); } }
        }

        public string CandidatTelephone
        {
            get => _candidatTelephone;
            set { if (_candidatTelephone != value) { _candidatTelephone = value; OnPropertyChanged(); } }
        }

        public string CandidatDiplome
        {
            get => _candidatDiplome;
            set { if (_candidatDiplome != value) { _candidatDiplome = value; OnPropertyChanged(); } }
        }

        public int CandidatExperience
        {
            get => _candidatExperience;
            set { if (_candidatExperience != value) { _candidatExperience = value; OnPropertyChanged(); } }
        }

        public string BesoinReference
        {
            get => _besoinReference;
            set { if (_besoinReference != value) { _besoinReference = value; OnPropertyChanged(); } }
        }

        public string BesoinIntitulePoste
        {
            get => _besoinIntitulePoste;
            set { if (_besoinIntitulePoste != value) { _besoinIntitulePoste = value; OnPropertyChanged(); } }
        }

        public string BesoinDepartement
        {
            get => _besoinDepartement;
            set { if (_besoinDepartement != value) { _besoinDepartement = value; OnPropertyChanged(); } }
        }

        public int? BesoinPosteId
        {
            get => _besoinPosteId;
            set { if (_besoinPosteId != value) { _besoinPosteId = value; OnPropertyChanged(); } }
        }

        // Helpers textuels & états
        public string CanalSourceLibelle => RecrutementHelper.GetDescription(CanalSource);
        public string EtapeActuelleLibelle => RecrutementHelper.GetDescription(EtapeActuelle);
        public string StatutLibelle => RecrutementHelper.GetDescription(Statut);
        public string AvisGlobalLibelle => AvisGlobal.HasValue ? RecrutementHelper.GetDescription(AvisGlobal.Value) : "-";

        public bool EstEmbauchee => Statut == StatutCandidature.Embauchee || !string.IsNullOrWhiteSpace(SalarieCreeMatricule);
        public bool EstVerrouillee => EstEmbauchee;

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    /// <summary>
    /// Entretien ou évaluation d'un candidat à une étape donnée du recrutement.
    /// </summary>
    public class RecrutementEntretien : INotifyPropertyChanged
    {
        private int _id;
        private int _candidatureId;
        private EtapeRecrutement _etape = EtapeRecrutement.EntretienRH;
        private DateTime _dateEntretien = DateTime.Today;
        private string _evaluateurNom = string.Empty;
        private string _evaluateurMatricule;
        private double? _noteGlobale;
        private AvisRecruteur? _avis;
        private string _pointsForts = string.Empty;
        private string _axesAmelioration = string.Empty;
        private string _synthese = string.Empty;
        private DateTime _dateCreation = DateTime.Now;

        // Propriétés d'affichage
        private string _candidatNomComplet = string.Empty;
        private string _besoinIntitulePoste = string.Empty;

        public int Id
        {
            get => _id;
            set { if (_id != value) { _id = value; OnPropertyChanged(); } }
        }

        public int CandidatureId
        {
            get => _candidatureId;
            set { if (_candidatureId != value) { _candidatureId = value; OnPropertyChanged(); } }
        }

        public EtapeRecrutement Etape
        {
            get => _etape;
            set { if (_etape != value) { _etape = value; OnPropertyChanged(); OnPropertyChanged(nameof(EtapeLibelle)); } }
        }

        public DateTime DateEntretien
        {
            get => _dateEntretien;
            set { if (_dateEntretien != value) { _dateEntretien = value; OnPropertyChanged(); } }
        }

        public string EvaluateurNom
        {
            get => _evaluateurNom;
            set { if (_evaluateurNom != value) { _evaluateurNom = value; OnPropertyChanged(); } }
        }

        public string EvaluateurMatricule
        {
            get => _evaluateurMatricule;
            set { if (_evaluateurMatricule != value) { _evaluateurMatricule = value; OnPropertyChanged(); } }
        }

        public double? NoteGlobale
        {
            get => _noteGlobale;
            set { if (_noteGlobale != value) { _noteGlobale = value; OnPropertyChanged(); } }
        }

        public AvisRecruteur? Avis
        {
            get => _avis;
            set { if (_avis != value) { _avis = value; OnPropertyChanged(); OnPropertyChanged(nameof(AvisLibelle)); } }
        }

        public string PointsForts
        {
            get => _pointsForts;
            set { if (_pointsForts != value) { _pointsForts = value; OnPropertyChanged(); } }
        }

        public string AxesAmelioration
        {
            get => _axesAmelioration;
            set { if (_axesAmelioration != value) { _axesAmelioration = value; OnPropertyChanged(); } }
        }

        public string Synthese
        {
            get => _synthese;
            set { if (_synthese != value) { _synthese = value; OnPropertyChanged(); } }
        }

        public DateTime DateCreation
        {
            get => _dateCreation;
            set { if (_dateCreation != value) { _dateCreation = value; OnPropertyChanged(); } }
        }

        public string CandidatNomComplet
        {
            get => _candidatNomComplet;
            set { if (_candidatNomComplet != value) { _candidatNomComplet = value; OnPropertyChanged(); } }
        }

        public string BesoinIntitulePoste
        {
            get => _besoinIntitulePoste;
            set { if (_besoinIntitulePoste != value) { _besoinIntitulePoste = value; OnPropertyChanged(); } }
        }

        public string EtapeLibelle => RecrutementHelper.GetDescription(Etape);
        public string AvisLibelle => Avis.HasValue ? RecrutementHelper.GetDescription(Avis.Value) : "-";

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    /// <summary>
    /// Données consolidées pour le tableau de bord RH-05.
    /// </summary>
    public class TableauBordRecrutement
    {
        public int TotalBesoins { get; set; }
        public int BesoinsOuverts { get; set; }
        public int BesoinsEnCours { get; set; }
        public int BesoinsPourvus { get; set; }
        public int PostesAPourvoir { get; set; }
        public int PostesPourvus { get; set; }

        public int TotalCandidatures { get; set; }
        public int CandidaturesNouvelles { get; set; }
        public int CandidaturesEnEvaluation { get; set; }
        public int CandidaturesRetenues { get; set; }
        public int CandidaturesEmbauchees { get; set; }
        public int CandidaturesRejetees { get; set; }

        public int TotalVivier { get; set; }
        public int TotalEntretiens { get; set; }
    }

    #endregion

    #region Helper d'affichage RH-05

    public static class RecrutementHelper
    {
        public static string GetDescription(Enum value)
        {
            if (value == null) return string.Empty;
            var fi = value.GetType().GetField(value.ToString());
            if (fi == null) return value.ToString();
            var attributes = (DescriptionAttribute[])fi.GetCustomAttributes(typeof(DescriptionAttribute), false);
            return attributes.Length > 0 ? attributes[0].Description : value.ToString();
        }
    }

    #endregion
}
