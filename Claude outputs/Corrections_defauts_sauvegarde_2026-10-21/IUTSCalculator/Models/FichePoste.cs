using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace IUTSCalculator.Models
{
    /// <summary>
    /// Domaine ou famille de compétence (module RH-02).
    /// </summary>
    public enum DomaineCompetence
    {
        [Description("Technique / Métier")]
        Technique = 0,

        [Description("Managériale / Leadership")]
        Manageriale = 1,
        Management = 1,

        [Description("Comportementale / Savoir-Être")]
        Comportementale = 2,

        [Description("Réglementaire / Juridique")]
        Reglementaire = 3
    }

    /// <summary>
    /// Niveau de maîtrise exigé pour une compétence (échelle de 1 à 4).
    /// </summary>
    public enum NiveauMaitrise
    {
        [Description("1 - Notions / Débutant")]
        Notions = 1,

        [Description("2 - Pratique / Intermédiaire")]
        Pratique = 2,

        [Description("3 - Maîtrise / Avancé")]
        Maitrise = 3,
        Confirme = 3,

        [Description("4 - Expert / Référent")]
        Expert = 4
    }

    /// <summary>
    /// Compétence unitaire du catalogue d'entreprise (module RH-02).
    /// </summary>
    public class Competence : INotifyPropertyChanged
    {
        private int _id;
        private string _code;
        private string _libelle;
        private DomaineCompetence _domaine = DomaineCompetence.Technique;
        private string _description;
        private DateTime _dateCreation = DateTime.Now;

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

        public string Libelle
        {
            get => _libelle;
            set
            {
                if (_libelle != value)
                {
                    _libelle = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(DisplayName));
                }
            }
        }

        public DomaineCompetence Domaine
        {
            get => _domaine;
            set
            {
                if (_domaine != value)
                {
                    _domaine = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(DomaineLibelle));
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

        public string DisplayName => string.IsNullOrWhiteSpace(Code) ? Libelle : $"{Code} — {Libelle}";

        public string DomaineLibelle
        {
            get
            {
                switch (_domaine)
                {
                    case DomaineCompetence.Technique: return "Technique / Métier";
                    case DomaineCompetence.Manageriale: return "Managériale / Leadership";
                    case DomaineCompetence.Comportementale: return "Comportementale / Savoir-Être";
                    case DomaineCompetence.Reglementaire: return "Réglementaire / Juridique";
                    default: return "Générale";
                }
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    /// <summary>
    /// Association entre un poste et une compétence exigée avec niveau requis (module RH-02).
    /// </summary>
    public class PosteCompetence : INotifyPropertyChanged
    {
        private int _id;
        private int _posteId;
        private int _competenceId;
        private string _competenceCode;
        private string _competenceLibelle;
        private DomaineCompetence _domaine = DomaineCompetence.Technique;
        private NiveauMaitrise _niveauRequis = NiveauMaitrise.Pratique;
        private bool _estObligatoire = true;
        private string _commentaire;

        public int Id
        {
            get => _id;
            set { if (_id != value) { _id = value; OnPropertyChanged(); } }
        }

        public int PosteId
        {
            get => _posteId;
            set { if (_posteId != value) { _posteId = value; OnPropertyChanged(); } }
        }

        public int CompetenceId
        {
            get => _competenceId;
            set { if (_competenceId != value) { _competenceId = value; OnPropertyChanged(); } }
        }

        public string CompetenceCode
        {
            get => _competenceCode;
            set { if (_competenceCode != value) { _competenceCode = value; OnPropertyChanged(); } }
        }

        public string CompetenceLibelle
        {
            get => _competenceLibelle;
            set { if (_competenceLibelle != value) { _competenceLibelle = value; OnPropertyChanged(); } }
        }

        public DomaineCompetence Domaine
        {
            get => _domaine;
            set { if (_domaine != value) { _domaine = value; OnPropertyChanged(); OnPropertyChanged(nameof(DomaineLibelle)); } }
        }

        public string DomaineLibelle
        {
            get
            {
                switch (_domaine)
                {
                    case DomaineCompetence.Technique: return "Technique / Métier";
                    case DomaineCompetence.Manageriale: return "Managériale / Leadership";
                    case DomaineCompetence.Comportementale: return "Comportementale / Savoir-Être";
                    case DomaineCompetence.Reglementaire: return "Réglementaire / Juridique";
                    default: return "Générale";
                }
            }
        }

        public NiveauMaitrise NiveauRequis
        {
            get => _niveauRequis;
            set
            {
                if (_niveauRequis != value)
                {
                    _niveauRequis = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(NiveauValeur));
                    OnPropertyChanged(nameof(NiveauLibelle));
                    OnPropertyChanged(nameof(NiveauEtoiles));
                }
            }
        }

        public NiveauMaitrise NiveauMaitriseRequis
        {
            get => _niveauRequis;
            set => NiveauRequis = value;
        }

        public int NiveauValeur
        {
            get => (int)_niveauRequis;
            set
            {
                if (value >= 1 && value <= 4)
                    NiveauRequis = (NiveauMaitrise)value;
            }
        }

        public bool EstObligatoire
        {
            get => _estObligatoire;
            set
            {
                if (_estObligatoire != value)
                {
                    _estObligatoire = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(CriticiteLibelle));
                }
            }
        }

        public string Commentaire
        {
            get => _commentaire;
            set { if (_commentaire != value) { _commentaire = value; OnPropertyChanged(); } }
        }

        public string CriticiteLibelle => _estObligatoire ? "Indispensable (Requis)" : "Souhaité (Atout)";

        public string NiveauLibelle
        {
            get
            {
                switch (_niveauRequis)
                {
                    case NiveauMaitrise.Notions: return "1 - Notions";
                    case NiveauMaitrise.Pratique: return "2 - Pratique";
                    case NiveauMaitrise.Maitrise: return "3 - Maîtrise";
                    case NiveauMaitrise.Expert: return "4 - Expert";
                    default: return ((int)_niveauRequis).ToString();
                }
            }
        }

        public string NiveauEtoiles
        {
            get
            {
                switch (_niveauRequis)
                {
                    case NiveauMaitrise.Notions: return "★☆☆☆";
                    case NiveauMaitrise.Pratique: return "★★☆☆";
                    case NiveauMaitrise.Maitrise: return "★★★☆";
                    case NiveauMaitrise.Expert: return "★★★★";
                    default: return "★★☆☆";
                }
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    /// <summary>
    /// Représente un salarié actuellement titulaire du poste (module RH-02 GPEC).
    /// </summary>
    public class TitulairePosteLigne
    {
        public string EmployeeId { get; set; }
        public string Matricule
        {
            get => EmployeeId;
            set => EmployeeId = value;
        }
        public string NomComplet { get; set; }
        public string Department { get; set; }
        public string Categorie { get; set; }
        public string Echelon { get; set; }
        public DateTime? DateEmbauche { get; set; }
        public string DateEmbaucheTexte { get; set; }
        public string TypeContrat { get; set; } = "CDI";
        public string AncienneteTexte { get; set; }
        public int StatutSalarie { get; set; }

        public string StatutTexte => StatutSalarie == 0 ? "Actif" : (StatutSalarie == 2 ? "Suspendu" : "Sorti");
    }

    /// <summary>
    /// Ligne récapitulative de synthèse budgétaire pour un poste (module RH-02).
    /// </summary>
    public class SynthesePosteBudget
    {
        public int PosteId { get; set; }
        public string Code { get; set; }
        public string Intitule { get; set; }
        public string UniteLibelle { get; set; }
        public string SuperieurIntitule { get; set; }
        public int EffectifBudgete { get; set; }
        public int EffectifPourvu { get; set; }
        public int Ecart => EffectifBudgete - EffectifPourvu;
        public int EcartEffectif => Ecart;
        public double TauxOccupation => EffectifBudgete > 0 ? Math.Round((double)EffectifPourvu / EffectifBudgete * 100.0, 1) : 0;

        public string StatutOccupation
        {
            get
            {
                if (EffectifPourvu == 0) return "Vacant";
                if (EffectifPourvu < EffectifBudgete) return "Partiel";
                if (EffectifPourvu == EffectifBudgete) return "Pourvu";
                return "Sureffectif";
            }
        }

        public string StatutOccupationGpec => StatutOccupation;

        public string BadgeEffectif => $"{EffectifPourvu} / {EffectifBudgete}";

        public string CouleurStatut
        {
            get
            {
                if (EffectifPourvu == 0) return "#C62828"; // Rouge (Vacant)
                if (EffectifPourvu < EffectifBudgete) return "#E65100"; // Ambre (Partiel)
                if (EffectifPourvu == EffectifBudgete) return "#1B5E20"; // Vert (Pourvu)
                return "#6A1B9A"; // Violet (Sureffectif)
            }
        }

        public decimal SalaireMinBudgete { get; set; }
        public decimal SalaireMaxBudgete { get; set; }
        public decimal SalaireMinConventionnel { get; set; }
        public string CategorieConventionnelle { get; set; }
        public string EchelonConventionnel { get; set; }
        public List<TitulairePosteLigne> Titulaires { get; set; } = new List<TitulairePosteLigne>();
    }
}
