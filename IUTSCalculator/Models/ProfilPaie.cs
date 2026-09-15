using System;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;

namespace IUTSCalculator.Models
{
    /// <summary>
    /// Population visée par un profil de paie. Ce n'est pas une catégorie fiscale : elle
    /// ne change aucun calcul. Elle sert à nommer la population pour qu'un gestionnaire
    /// retrouve le bon profil sans ouvrir les sept.
    /// </summary>
    public enum PopulationProfil
    {
        Cadre = 0,
        AgentDeMaitrise = 1,
        Employe = 2,
        Stagiaire = 3,
        Apprenti = 4,
        Journalier = 5,
        Expatrie = 6,
        Autre = 7
    }

    /// <summary>
    /// Un profil de paie : un jeu de rubriques préassemblé pour une population (module
    /// SOC-03).
    ///
    /// Le plan de paie (SOC-01) dit quelles rubriques existent ; le profil dit lesquelles
    /// s'appliquent à qui, et avec quel montant par défaut. Sans lui, chaque embauche
    /// oblige à ressaisir les mêmes primes qu'un collègue de même catégorie, et une
    /// convention qui change se reporte à la main sur toutes les fiches concernées.
    ///
    /// <b>Le profil est un lien vivant, pas une recopie.</b> Un salarié porte son profil ;
    /// les rubriques du profil alimentent son bulletin à chaque calcul. Corriger le profil
    /// corrige tous les salariés qui le portent, y compris ceux dont le bulletin du mois
    /// n'est pas encore sorti. C'est l'intérêt du module — et sa contrepartie : une
    /// retouche déplace des nets, et l'écran dit combien de salariés sont concernés avant
    /// d'enregistrer.
    ///
    /// <b>Ce qu'un profil ne porte jamais.</b> Ni le salaire de base, ni les indemnités
    /// plafonnées de l'article 106, ni l'IUTS, ni la CNSS : ces éléments sont lus par le
    /// moteur audité sur la fiche du salarié. Les porter aussi en profil les compterait
    /// deux fois.
    /// </summary>
    public class ProfilPaie : INotifyPropertyChanged
    {
        private int _id;
        private string _code;
        private string _libelle;
        private PopulationProfil _population = PopulationProfil.Employe;
        private string _description;
        private bool _actif = true;
        private int _ordre;
        private DateTime _dateSaisie = DateTime.Now;

        private int _nombreLignes;
        private int _nombreSalaries;

        public int Id
        {
            get { return _id; }
            set { Definir(ref _id, value); }
        }

        /// <summary>Code court et unique du profil (ex. CADRE, JOURNALIER).</summary>
        public string Code
        {
            get { return _code; }
            set
            {
                if (Definir(ref _code, value))
                    OnPropertyChanged("Designation");
            }
        }

        public string Libelle
        {
            get { return _libelle; }
            set
            {
                if (Definir(ref _libelle, value))
                    OnPropertyChanged("Designation");
            }
        }

        public PopulationProfil Population
        {
            get { return _population; }
            set
            {
                if (Definir(ref _population, value))
                    OnPropertyChanged("PopulationLibelle");
            }
        }

        public string Description
        {
            get { return _description; }
            set { Definir(ref _description, value); }
        }

        /// <summary>Alias pour Description.</summary>
        public string Commentaire
        {
            get { return _description; }
            set { Definir(ref _description, value); }
        }

        public bool Actif
        {
            get { return _actif; }
            set
            {
                if (Definir(ref _actif, value))
                    OnPropertyChanged("ActifLibelle");
            }
        }

        /// <summary>Ordre d'affichage dans la liste des profils.</summary>
        public int Ordre
        {
            get { return _ordre; }
            set { Definir(ref _ordre, value); }
        }

        public DateTime DateSaisie
        {
            get { return _dateSaisie; }
            set { Definir(ref _dateSaisie, value); }
        }

        // ── Comptages, renseignés à la lecture ───────────────────────────────

        /// <summary>Nombre de rubriques actives portées par le profil.</summary>
        public int NombreLignes
        {
            get { return _nombreLignes; }
            set
            {
                if (Definir(ref _nombreLignes, value))
                    OnPropertyChanged("Resume");
            }
        }

        /// <summary>
        /// Nombre de salariés qui portent ce profil aujourd'hui. C'est le chiffre que le
        /// gestionnaire doit avoir sous les yeux avant de modifier une ligne : il dit
        /// combien de bulletins la modification va déplacer.
        /// </summary>
        public int NombreSalaries
        {
            get { return _nombreSalaries; }
            set
            {
                if (Definir(ref _nombreSalaries, value))
                    OnPropertyChanged("Resume");
            }
        }

        // ── Dérivés ──────────────────────────────────────────────────────────

        public string Designation
        {
            get
            {
                if (string.IsNullOrWhiteSpace(Code)) return Libelle ?? "";
                return string.Format("{0} — {1}", Code, Libelle);
            }
        }

        public string PopulationLibelle
        {
            get { return Libelle_(Population); }
        }

        public string ActifLibelle
        {
            get { return Actif ? "Actif" : "Inactif"; }
        }

        public string Resume
        {
            get
            {
                return string.Format(
                    "{0} rubrique(s) — {1} salarié(s)", NombreLignes, NombreSalaries);
            }
        }

        /// <summary>
        /// Libellé d'une population. La méthode ne s'appelle pas <c>Libelle</c> : ce nom
        /// est déjà celui d'une propriété de la classe.
        /// </summary>
        public static string Libelle_(PopulationProfil population)
        {
            switch (population)
            {
                case PopulationProfil.Cadre: return "Cadre";
                case PopulationProfil.AgentDeMaitrise: return "Agent de maîtrise";
                case PopulationProfil.Employe: return "Employé";
                case PopulationProfil.Stagiaire: return "Stagiaire";
                case PopulationProfil.Apprenti: return "Apprenti";
                case PopulationProfil.Journalier: return "Journalier";
                case PopulationProfil.Expatrie: return "Expatrié";
                default: return "Autre";
            }
        }

        public ProfilPaie Clone()
        {
            return new ProfilPaie
            {
                Id = Id,
                Code = Code,
                Libelle = Libelle,
                Population = Population,
                Description = Description,
                Actif = Actif,
                Ordre = Ordre,
                DateSaisie = DateSaisie,
                NombreLignes = NombreLignes,
                NombreSalaries = NombreSalaries
            };
        }

        public event PropertyChangedEventHandler PropertyChanged;

        private bool Definir<T>(ref T champ, T valeur, [CallerMemberName] string nom = null)
        {
            if (Equals(champ, valeur)) return false;
            champ = valeur;
            OnPropertyChanged(nom);
            return true;
        }

        private void OnPropertyChanged(string nom)
        {
            var handler = PropertyChanged;
            if (handler != null) handler(this, new PropertyChangedEventArgs(nom));
        }
    }

    /// <summary>
    /// Une rubrique portée par un profil, avec le montant qu'elle vaut par défaut pour
    /// cette population.
    ///
    /// Le montant de la ligne n'est pas obligatoire : laissé à zéro, la rubrique est
    /// valorisée par son propre paramétrage (montant fixe du plan de paie, ou pourcentage
    /// du salaire de base). Une ligne qu'aucune des deux sources ne valorise ne produit
    /// rien, et l'écran la signale plutôt que de la laisser disparaître en silence.
    /// </summary>
    public class LigneProfil : INotifyPropertyChanged
    {
        private int _id;
        private int _profilId;
        private string _codeRubrique;
        private string _libelleRubrique;
        private SensRubrique _sens;
        private ModeCalculRubrique _modeCalcul;
        private decimal _montant;
        private decimal _quantite;
        private int _ordre;
        private bool _active = true;
        private string _commentaire;

        public int Id
        {
            get { return _id; }
            set { Definir(ref _id, value); }
        }

        public int ProfilId
        {
            get { return _profilId; }
            set { Definir(ref _profilId, value); }
        }

        /// <summary>Code de la rubrique du plan de paie alimentée par cette ligne.</summary>
        public string CodeRubrique
        {
            get { return _codeRubrique; }
            set
            {
                if (Definir(ref _codeRubrique, value))
                    OnPropertyChanged("Designation");
            }
        }

        /// <summary>Libellé joint depuis le plan de paie. Il n'est pas stocké.</summary>
        public string LibelleRubrique
        {
            get { return _libelleRubrique; }
            set
            {
                if (Definir(ref _libelleRubrique, value))
                    OnPropertyChanged("Designation");
            }
        }

        /// <summary>Sens joint depuis le plan de paie.</summary>
        public SensRubrique Sens
        {
            get { return _sens; }
            set
            {
                if (Definir(ref _sens, value))
                    OnPropertyChanged("SensLibelle");
            }
        }

        /// <summary>Mode de calcul joint depuis le plan de paie.</summary>
        public ModeCalculRubrique ModeCalcul
        {
            get { return _modeCalcul; }
            set
            {
                if (Definir(ref _modeCalcul, value))
                    OnPropertyChanged("ValorisationLibelle");
            }
        }

        /// <summary>Montant par défaut pour cette population. Zéro = valoriser par la rubrique.</summary>
        public decimal Montant
        {
            get { return _montant; }
            set
            {
                if (Definir(ref _montant, value))
                    OnPropertyChanged("ValorisationLibelle");
            }
        }

        /// <summary>Quantité par défaut (heures, unités). Reportée telle quelle sur le bulletin.</summary>
        public decimal Quantite
        {
            get { return _quantite; }
            set { Definir(ref _quantite, value); }
        }

        public int Ordre
        {
            get { return _ordre; }
            set { Definir(ref _ordre, value); }
        }

        public bool Active
        {
            get { return _active; }
            set { Definir(ref _active, value); }
        }

        public string Commentaire
        {
            get { return _commentaire; }
            set { Definir(ref _commentaire, value); }
        }

        // ── Dérivés ──────────────────────────────────────────────────────────

        public string Designation
        {
            get
            {
                return string.IsNullOrWhiteSpace(LibelleRubrique)
                    ? (CodeRubrique ?? "")
                    : string.Format("{0} — {1}", CodeRubrique, LibelleRubrique);
            }
        }

        public string SensLibelle
        {
            get
            {
                switch (Sens)
                {
                    case SensRubrique.Retenue: return "Retenue";
                    case SensRubrique.Information: return "Information";
                    default: return "Gain";
                }
            }
        }

        /// <summary>D'où viendra le montant, en clair, pour la colonne de contrôle de l'écran.</summary>
        public string ValorisationLibelle
        {
            get
            {
                if (Montant != 0m)
                    return string.Format(CultureInfo.CurrentCulture, "{0:N0} par défaut", Montant);

                if (ModeCalcul == ModeCalculRubrique.PourcentageSalaireBase)
                    return "% du salaire de base";

                if (ModeCalcul == ModeCalculRubrique.MontantFixe)
                    return "montant de la rubrique";

                return "non valorisée";
            }
        }

        public LigneProfil Clone()
        {
            return new LigneProfil
            {
                Id = Id,
                ProfilId = ProfilId,
                CodeRubrique = CodeRubrique,
                LibelleRubrique = LibelleRubrique,
                Sens = Sens,
                ModeCalcul = ModeCalcul,
                Montant = Montant,
                Quantite = Quantite,
                Ordre = Ordre,
                Active = Active,
                Commentaire = Commentaire
            };
        }

        public event PropertyChangedEventHandler PropertyChanged;

        private bool Definir<T>(ref T champ, T valeur, [CallerMemberName] string nom = null)
        {
            if (Equals(champ, valeur)) return false;
            champ = valeur;
            OnPropertyChanged(nom);
            return true;
        }

        private void OnPropertyChanged(string nom)
        {
            var handler = PropertyChanged;
            if (handler != null) handler(this, new PropertyChangedEventArgs(nom));
        }
    }

    /// <summary>
    /// Rattachement d'un salarié à un profil, à partir d'une date.
    ///
    /// L'affectation est DATÉE et conservée : un salarié promu cadre en avril doit garder
    /// le profil « employé » sur ses bulletins de janvier à mars. Écraser l'affectation à
    /// chaque changement ferait mentir tout recalcul antérieur, et un contrôle fiscal
    /// demande précisément de justifier le calcul du mois, pas celui d'aujourd'hui.
    ///
    /// Un <see cref="ProfilId"/> à zéro est une affectation « aucun profil » : c'est ainsi
    /// qu'un retrait se date lui aussi, au lieu d'effacer l'histoire.
    /// </summary>
    public class AffectationProfil : INotifyPropertyChanged
    {
        private int _id;
        private string _employeeId;
        private string _nomSalarie;
        private int _profilId;
        private string _profilDesignation;
        private DateTime _dateEffet = DateTime.Today;
        private string _commentaire;

        public int Id
        {
            get { return _id; }
            set { Definir(ref _id, value); }
        }

        public string EmployeeId
        {
            get { return _employeeId; }
            set { Definir(ref _employeeId, value); }
        }

        public string NomSalarie
        {
            get { return _nomSalarie; }
            set { Definir(ref _nomSalarie, value); }
        }

        /// <summary>Profil rattaché. Zéro signifie « aucun profil à partir de cette date ».</summary>
        public int ProfilId
        {
            get { return _profilId; }
            set
            {
                if (Definir(ref _profilId, value))
                    OnPropertyChanged("EstRetrait");
            }
        }

        /// <summary>Désignation du profil, jointe pour la lisibilité des listes.</summary>
        public string ProfilDesignation
        {
            get { return _profilDesignation; }
            set { Definir(ref _profilDesignation, value); }
        }

        /// <summary>Date à partir de laquelle le rattachement s'applique.</summary>
        public DateTime DateEffet
        {
            get { return _dateEffet; }
            set
            {
                if (Definir(ref _dateEffet, value))
                    OnPropertyChanged("DateEffetLibelle");
            }
        }

        public string Commentaire
        {
            get { return _commentaire; }
            set { Definir(ref _commentaire, value); }
        }

        public bool EstRetrait
        {
            get { return ProfilId <= 0; }
        }

        public string DateEffetLibelle
        {
            get { return DateEffet.ToString("dd/MM/yyyy", CultureInfo.CurrentCulture); }
        }

        public string ProfilLibelle
        {
            get
            {
                return EstRetrait
                    ? "Aucun profil"
                    : (string.IsNullOrWhiteSpace(ProfilDesignation) ? "Profil supprimé" : ProfilDesignation);
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        private bool Definir<T>(ref T champ, T valeur, [CallerMemberName] string nom = null)
        {
            if (Equals(champ, valeur)) return false;
            champ = valeur;
            OnPropertyChanged(nom);
            return true;
        }

        private void OnPropertyChanged(string nom)
        {
            var handler = PropertyChanged;
            if (handler != null) handler(this, new PropertyChangedEventArgs(nom));
        }
    }
}
