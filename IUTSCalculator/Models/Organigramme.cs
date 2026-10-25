using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace IUTSCalculator.Models
{
    /// <summary>
    /// Type d'unité organisationnelle dans l'arborescence de l'entreprise.
    /// </summary>
    public enum TypeUniteOrganisationnelle
    {
        [Description("Direction Générale")]
        DirectionGenerale = 0,

        [Description("Direction")]
        Direction = 1,

        [Description("Département")]
        Departement = 2,

        [Description("Service")]
        Service = 3,

        [Description("Pôle / Unité")]
        Pole = 4,

        [Description("Autre")]
        Autre = 5
    }

    /// <summary>
    /// Représente une entité organisationnelle (Direction, Département, Service, etc.)
    /// au sein du dossier entreprise (module RH-01).
    /// </summary>
    public class UniteOrganisationnelle : INotifyPropertyChanged
    {
        private int _id;
        private string _code;
        private string _libelle;
        private TypeUniteOrganisationnelle _typeUnite;
        private int? _parentId;
        private string _parentLibelle;
        private string _responsableMatricule;
        private string _responsableNom;
        private string _description;
        private string _couleur = "#14675A";
        private int _ordre;
        private DateTime _dateCreation = DateTime.Now;
        private int _nombrePostes;
        private int _nombreSalariesDirects;
        private int _nombreSalariesTotal;

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

        public TypeUniteOrganisationnelle TypeUnite
        {
            get => _typeUnite;
            set
            {
                if (_typeUnite != value)
                {
                    _typeUnite = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(BadgeTypeLibelle));
                    OnPropertyChanged(nameof(EstDirection));
                    OnPropertyChanged(nameof(EstService));
                }
            }
        }

        public int? ParentId
        {
            get => _parentId;
            set { if (_parentId != value) { _parentId = value; OnPropertyChanged(); } }
        }

        public string ParentLibelle
        {
            get => _parentLibelle;
            set { if (_parentLibelle != value) { _parentLibelle = value; OnPropertyChanged(); } }
        }

        public string ResponsableMatricule
        {
            get => _responsableMatricule;
            set
            {
                if (_responsableMatricule != value)
                {
                    _responsableMatricule = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(AUnResponsable));
                }
            }
        }

        public string ResponsableNom
        {
            get => _responsableNom;
            set { if (_responsableNom != value) { _responsableNom = value; OnPropertyChanged(); } }
        }

        public string Description
        {
            get => _description;
            set { if (_description != value) { _description = value; OnPropertyChanged(); } }
        }

        public string Couleur
        {
            get => _couleur;
            set { if (_couleur != value) { _couleur = value; OnPropertyChanged(); } }
        }

        public int Ordre
        {
            get => _ordre;
            set { if (_ordre != value) { _ordre = value; OnPropertyChanged(); } }
        }

        public DateTime DateCreation
        {
            get => _dateCreation;
            set { if (_dateCreation != value) { _dateCreation = value; OnPropertyChanged(); } }
        }

        public int NombrePostes
        {
            get => _nombrePostes;
            set { if (_nombrePostes != value) { _nombrePostes = value; OnPropertyChanged(); } }
        }

        public int NombreSalariesDirects
        {
            get => _nombreSalariesDirects;
            set { if (_nombreSalariesDirects != value) { _nombreSalariesDirects = value; OnPropertyChanged(); } }
        }

        public int NombreSalariesTotal
        {
            get => _nombreSalariesTotal;
            set { if (_nombreSalariesTotal != value) { _nombreSalariesTotal = value; OnPropertyChanged(); } }
        }

        public bool AUnResponsable => !string.IsNullOrWhiteSpace(ResponsableMatricule);

        public bool EstDirection => _typeUnite == TypeUniteOrganisationnelle.DirectionGenerale || _typeUnite == TypeUniteOrganisationnelle.Direction;

        public bool EstService => _typeUnite == TypeUniteOrganisationnelle.Service;

        public string DisplayName => string.IsNullOrWhiteSpace(Code) ? Libelle : $"{Code} — {Libelle}";

        public string BadgeTypeLibelle
        {
            get
            {
                switch (_typeUnite)
                {
                    case TypeUniteOrganisationnelle.DirectionGenerale: return "Direction Générale";
                    case TypeUniteOrganisationnelle.Direction: return "Direction";
                    case TypeUniteOrganisationnelle.Departement: return "Département";
                    case TypeUniteOrganisationnelle.Service: return "Service";
                    case TypeUniteOrganisationnelle.Pole: return "Pôle";
                    default: return "Unité";
                }
            }
        }

        public List<UniteOrganisationnelle> SousUnites { get; set; } = new List<UniteOrganisationnelle>();
        public List<Poste> Postes { get; set; } = new List<Poste>();

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    /// <summary>
    /// Représente un poste ou une fonction dans la structure organisationnelle (module RH-01).
    /// </summary>
    public class Poste : INotifyPropertyChanged
    {
        private int _id;
        private string _code;
        private string _intitule;
        private int? _uniteId;
        private string _uniteLibelle;
        private int? _superieurPosteId;
        private string _superieurPosteIntitule;
        private string _titulaireMatricule;
        private string _titulaireNom;
        private string _classification;
        private string _missions;
        private int _effectifRequis = 1;
        private bool _actif = true;
        private int _ordre;
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

        public int? UniteId
        {
            get => _uniteId;
            set { if (_uniteId != value) { _uniteId = value; OnPropertyChanged(); } }
        }

        public string UniteLibelle
        {
            get => _uniteLibelle;
            set { if (_uniteLibelle != value) { _uniteLibelle = value; OnPropertyChanged(); } }
        }

        public int? SuperieurPosteId
        {
            get => _superieurPosteId;
            set { if (_superieurPosteId != value) { _superieurPosteId = value; OnPropertyChanged(); } }
        }

        public string SuperieurPosteIntitule
        {
            get => _superieurPosteIntitule;
            set { if (_superieurPosteIntitule != value) { _superieurPosteIntitule = value; OnPropertyChanged(); } }
        }

        public string TitulaireMatricule
        {
            get => _titulaireMatricule;
            set
            {
                if (_titulaireMatricule != value)
                {
                    _titulaireMatricule = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(EstPourvu));
                    OnPropertyChanged(nameof(StatutOccupation));
                    OnPropertyChanged(nameof(InitialesTitulaire));
                }
            }
        }

        public string TitulaireNom
        {
            get => _titulaireNom;
            set
            {
                if (_titulaireNom != value)
                {
                    _titulaireNom = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(InitialesTitulaire));
                }
            }
        }

        public string Classification
        {
            get => _classification;
            set { if (_classification != value) { _classification = value; OnPropertyChanged(); } }
        }

        public string Missions
        {
            get => _missions;
            set { if (_missions != value) { _missions = value; OnPropertyChanged(); } }
        }

        public int EffectifRequis
        {
            get => _effectifRequis;
            set
            {
                if (_effectifRequis != value)
                {
                    _effectifRequis = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(EffectifBudgete));
                    OnPropertyChanged(nameof(EcartEffectif));
                    OnPropertyChanged(nameof(TauxOccupation));
                    OnPropertyChanged(nameof(StatutOccupationGpec));
                    OnPropertyChanged(nameof(BadgeEffectifGpec));
                    OnPropertyChanged(nameof(CouleurStatutGpec));
                }
            }
        }

        // ── Propriétés étendues RH-02 (Fiche de poste & GPEC) ──
        private string _finalitePrincipale;
        private string _activitesCles;
        private string _niveauEtudeRequis;
        private string _diplomeRequis;
        private int _experienceMinimaleAnnees;
        private string _conditionsTravail;
        private int? _conventionId;
        private string _conventionLibelle;
        private string _categorieConventionnelle;
        private string _echelonConventionnel;
        private decimal _salaireMinimumConventionnel;
        private decimal _salaireMinBudgete;
        private decimal _salaireMaxBudgete;
        private int _effectifPourvu;

        public string FinalitePrincipale
        {
            get => _finalitePrincipale;
            set { if (_finalitePrincipale != value) { _finalitePrincipale = value; OnPropertyChanged(); } }
        }

        public string ActivitesCles
        {
            get => _activitesCles;
            set { if (_activitesCles != value) { _activitesCles = value; OnPropertyChanged(); } }
        }

        public string NiveauEtudeRequis
        {
            get => _niveauEtudeRequis;
            set { if (_niveauEtudeRequis != value) { _niveauEtudeRequis = value; OnPropertyChanged(); } }
        }

        public string DiplomeRequis
        {
            get => _diplomeRequis;
            set { if (_diplomeRequis != value) { _diplomeRequis = value; OnPropertyChanged(); } }
        }

        public int ExperienceMinimaleAnnees
        {
            get => _experienceMinimaleAnnees;
            set { if (_experienceMinimaleAnnees != value) { _experienceMinimaleAnnees = value; OnPropertyChanged(); } }
        }

        public string ConditionsTravail
        {
            get => _conditionsTravail;
            set { if (_conditionsTravail != value) { _conditionsTravail = value; OnPropertyChanged(); } }
        }

        public int? ConventionId
        {
            get => _conventionId;
            set { if (_conventionId != value) { _conventionId = value; OnPropertyChanged(); } }
        }

        public string ConventionLibelle
        {
            get => _conventionLibelle;
            set { if (_conventionLibelle != value) { _conventionLibelle = value; OnPropertyChanged(); } }
        }

        public string CategorieConventionnelle
        {
            get => _categorieConventionnelle;
            set { if (_categorieConventionnelle != value) { _categorieConventionnelle = value; OnPropertyChanged(); } }
        }

        public string EchelonConventionnel
        {
            get => _echelonConventionnel;
            set { if (_echelonConventionnel != value) { _echelonConventionnel = value; OnPropertyChanged(); } }
        }

        public decimal SalaireMinimumConventionnel
        {
            get => _salaireMinimumConventionnel;
            set { if (_salaireMinimumConventionnel != value) { _salaireMinimumConventionnel = value; OnPropertyChanged(); } }
        }

        public decimal SalaireMinBudgete
        {
            get => _salaireMinBudgete;
            set { if (_salaireMinBudgete != value) { _salaireMinBudgete = value; OnPropertyChanged(); } }
        }

        public decimal SalaireMaxBudgete
        {
            get => _salaireMaxBudgete;
            set { if (_salaireMaxBudgete != value) { _salaireMaxBudgete = value; OnPropertyChanged(); } }
        }

        public int EffectifBudgete
        {
            get => _effectifRequis;
            set => EffectifRequis = value;
        }

        public int EffectifPourvu
        {
            get => _effectifPourvu;
            set
            {
                if (_effectifPourvu != value)
                {
                    _effectifPourvu = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(EcartEffectif));
                    OnPropertyChanged(nameof(TauxOccupation));
                    OnPropertyChanged(nameof(StatutOccupationGpec));
                    OnPropertyChanged(nameof(BadgeEffectifGpec));
                    OnPropertyChanged(nameof(CouleurStatutGpec));
                    OnPropertyChanged(nameof(EstPourvu));
                    OnPropertyChanged(nameof(StatutOccupation));
                }
            }
        }

        public int EcartEffectif => _effectifRequis - _effectifPourvu;

        public double TauxOccupation => _effectifRequis > 0 ? Math.Round((double)_effectifPourvu / _effectifRequis * 100.0, 1) : 0;

        public string StatutOccupationGpec
        {
            get
            {
                if (_effectifPourvu == 0) return "Vacant";
                if (_effectifPourvu < _effectifRequis) return "Partiel";
                if (_effectifPourvu == _effectifRequis) return "Pourvu";
                return "Sureffectif";
            }
        }

        public string BadgeEffectifGpec => $"{_effectifPourvu} / {_effectifRequis}";

        public string CouleurStatutGpec
        {
            get
            {
                if (_effectifPourvu == 0) return "#C62828";
                if (_effectifPourvu < _effectifRequis) return "#E65100";
                if (_effectifPourvu == _effectifRequis) return "#1B5E20";
                return "#6A1B9A";
            }
        }

        public ObservableCollection<PosteCompetence> CompetencesRequises { get; set; } = new ObservableCollection<PosteCompetence>();
        public ObservableCollection<TitulairePosteLigne> TitulairesActuels { get; set; } = new ObservableCollection<TitulairePosteLigne>();

        public bool Actif
        {
            get => _actif;
            set { if (_actif != value) { _actif = value; OnPropertyChanged(); } }
        }

        public int Ordre
        {
            get => _ordre;
            set { if (_ordre != value) { _ordre = value; OnPropertyChanged(); } }
        }

        public DateTime DateCreation
        {
            get => _dateCreation;
            set { if (_dateCreation != value) { _dateCreation = value; OnPropertyChanged(); } }
        }

        public bool EstPourvu => _effectifPourvu > 0;

        public string StatutOccupation => StatutOccupationGpec;

        public string DisplayName => string.IsNullOrWhiteSpace(Code) ? Intitule : $"{Code} — {Intitule}";

        public string InitialesTitulaire
        {
            get
            {
                if (string.IsNullOrWhiteSpace(TitulaireNom))
                    return "?";

                var parts = TitulaireNom.Trim().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 1)
                    return parts[0].Length >= 2 ? parts[0].Substring(0, 2).ToUpperInvariant() : parts[0].ToUpperInvariant();

                return (parts[0][0].ToString() + parts[parts.Length - 1][0].ToString()).ToUpperInvariant();
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    /// <summary>
    /// Nœud arborescent d'organigramme pour l'affichage visuel et graphique WPF (module RH-01).
    /// </summary>
    public class NoeudOrganigramme : INotifyPropertyChanged
    {
        private int _id;
        private string _code;
        private string _titre;
        private string _sousTitre;
        private string _typeNoeud = "Unite"; // "Unite" ou "Poste"
        private TypeUniteOrganisationnelle? _typeUnite;
        private string _badgeType;
        private string _responsableOuTitulaire;
        private string _matricule;
        private string _initialeAvatar;
        private int _effectifDirect;
        private int _effectifTotal;
        private int _nombrePostes;
        private int _niveau;
        private string _couleurEntete = "#14675A";
        private bool _isExpanded = true;
        private bool _isSelected;
        private bool _isHighlighted;

        public int Id
        {
            get => _id;
            set { if (_id != value) { _id = value; OnPropertyChanged(); } }
        }

        public string Code
        {
            get => _code;
            set { if (_code != value) { _code = value; OnPropertyChanged(); } }
        }

        public string Titre
        {
            get => _titre;
            set { if (_titre != value) { _titre = value; OnPropertyChanged(); } }
        }

        public string SousTitre
        {
            get => _sousTitre;
            set { if (_sousTitre != value) { _sousTitre = value; OnPropertyChanged(); } }
        }

        public string TypeNoeud
        {
            get => _typeNoeud;
            set
            {
                if (_typeNoeud != value)
                {
                    _typeNoeud = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(EstUnite));
                    OnPropertyChanged(nameof(EstPoste));
                }
            }
        }

        public TypeUniteOrganisationnelle? TypeUnite
        {
            get => _typeUnite;
            set { if (_typeUnite != value) { _typeUnite = value; OnPropertyChanged(); } }
        }

        public string BadgeType
        {
            get => _badgeType;
            set { if (_badgeType != value) { _badgeType = value; OnPropertyChanged(); } }
        }

        public string ResponsableOuTitulaire
        {
            get => _responsableOuTitulaire;
            set
            {
                if (_responsableOuTitulaire != value)
                {
                    _responsableOuTitulaire = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(AUnTitulaire));
                }
            }
        }

        public string Matricule
        {
            get => _matricule;
            set { if (_matricule != value) { _matricule = value; OnPropertyChanged(); } }
        }

        public string InitialeAvatar
        {
            get => _initialeAvatar;
            set { if (_initialeAvatar != value) { _initialeAvatar = value; OnPropertyChanged(); } }
        }

        public int EffectifDirect
        {
            get => _effectifDirect;
            set { if (_effectifDirect != value) { _effectifDirect = value; OnPropertyChanged(); } }
        }

        public int EffectifTotal
        {
            get => _effectifTotal;
            set { if (_effectifTotal != value) { _effectifTotal = value; OnPropertyChanged(); } }
        }

        private int _effectifBudgete;
        private int _effectifPourvu;

        public int EffectifBudgete
        {
            get => _effectifBudgete;
            set
            {
                if (_effectifBudgete != value)
                {
                    _effectifBudgete = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(BadgeEffectifGpec));
                    OnPropertyChanged(nameof(CouleurStatutGpec));
                }
            }
        }

        public int EffectifPourvu
        {
            get => _effectifPourvu;
            set
            {
                if (_effectifPourvu != value)
                {
                    _effectifPourvu = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(BadgeEffectifGpec));
                    OnPropertyChanged(nameof(CouleurStatutGpec));
                }
            }
        }

        public string BadgeEffectifGpec => $"{_effectifPourvu} / {_effectifBudgete}";

        public string CouleurStatutGpec
        {
            get
            {
                if (_effectifBudgete <= 0) return "#555555";
                if (_effectifPourvu == 0) return "#C62828";
                if (_effectifPourvu < _effectifBudgete) return "#E65100";
                if (_effectifPourvu == _effectifBudgete) return "#1B5E20";
                return "#6A1B9A";
            }
        }

        public int NombrePostes
        {
            get => _nombrePostes;
            set { if (_nombrePostes != value) { _nombrePostes = value; OnPropertyChanged(); } }
        }

        public int Niveau
        {
            get => _niveau;
            set { if (_niveau != value) { _niveau = value; OnPropertyChanged(); } }
        }

        public string CouleurEntete
        {
            get => _couleurEntete;
            set { if (_couleurEntete != value) { _couleurEntete = value; OnPropertyChanged(); } }
        }

        public bool IsExpanded
        {
            get => _isExpanded;
            set { if (_isExpanded != value) { _isExpanded = value; OnPropertyChanged(); } }
        }

        public bool IsSelected
        {
            get => _isSelected;
            set { if (_isSelected != value) { _isSelected = value; OnPropertyChanged(); } }
        }

        public bool IsHighlighted
        {
            get => _isHighlighted;
            set { if (_isHighlighted != value) { _isHighlighted = value; OnPropertyChanged(); } }
        }

        public bool EstUnite => _typeNoeud == "Unite";
        public bool EstPoste => _typeNoeud == "Poste";
        public bool AUnTitulaire => !string.IsNullOrWhiteSpace(ResponsableOuTitulaire);

        public ObservableCollection<NoeudOrganigramme> Enfants { get; set; } = new ObservableCollection<NoeudOrganigramme>();

        public bool AEnfants => Enfants != null && Enfants.Count > 0;

        public UniteOrganisationnelle UniteAssociee { get; set; }
        public Poste PosteAssocie { get; set; }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    /// <summary>
    /// Ligne récapitulative pour le tableau d'affectation des salariés aux unités et postes (module RH-01).
    /// </summary>
    public class AffectationSalarieLigne : INotifyPropertyChanged
    {
        private string _matricule;
        private string _nomComplet;
        private string _category;
        private string _serviceActuel;
        private int? _posteActuelId;
        private string _posteActuelLibelle;
        private string _managerActuelMatricule;
        private string _managerActuelNom;
        private int _statutSalarie;

        public string Matricule
        {
            get => _matricule;
            set { if (_matricule != value) { _matricule = value; OnPropertyChanged(); } }
        }

        public string NomComplet
        {
            get => _nomComplet;
            set { if (_nomComplet != value) { _nomComplet = value; OnPropertyChanged(); } }
        }

        public string Category
        {
            get => _category;
            set { if (_category != value) { _category = value; OnPropertyChanged(); } }
        }

        public string ServiceActuel
        {
            get => _serviceActuel;
            set { if (_serviceActuel != value) { _serviceActuel = value; OnPropertyChanged(); } }
        }

        public int? PosteActuelId
        {
            get => _posteActuelId;
            set { if (_posteActuelId != value) { _posteActuelId = value; OnPropertyChanged(); } }
        }

        public string PosteActuelLibelle
        {
            get => _posteActuelLibelle;
            set { if (_posteActuelLibelle != value) { _posteActuelLibelle = value; OnPropertyChanged(); } }
        }

        public string ManagerActuelMatricule
        {
            get => _managerActuelMatricule;
            set { if (_managerActuelMatricule != value) { _managerActuelMatricule = value; OnPropertyChanged(); } }
        }

        public string ManagerActuelNom
        {
            get => _managerActuelNom;
            set { if (_managerActuelNom != value) { _managerActuelNom = value; OnPropertyChanged(); } }
        }

        public int StatutSalarie
        {
            get => _statutSalarie;
            set
            {
                if (_statutSalarie != value)
                {
                    _statutSalarie = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(StatutLibelle));
                    OnPropertyChanged(nameof(EstActif));
                }
            }
        }

        public bool EstActif => _statutSalarie == 0;

        public string StatutLibelle
        {
            get
            {
                switch (_statutSalarie)
                {
                    case 0: return "Actif";
                    case 1: return "Sorti";
                    case 2: return "Suspendu";
                    default: return "Inconnu";
                }
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
