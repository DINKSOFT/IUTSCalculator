using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace IUTSCalculator.Models
{
    /// <summary>Lien de la personne à charge avec le salarié.</summary>
    public enum LienParente
    {
        Enfant = 0,
        Conjoint = 1,
        Ascendant = 2,
        Autre = 3
    }

    /// <summary>
    /// Une personne à charge, nommément désignée (module SAL-01).
    ///
    /// Jusqu'ici le nombre de charges de famille était un simple entier saisi à la main
    /// sur la fiche. Deux ennuis, l'un administratif et l'autre fiscal :
    ///
    ///   • Devant un contrôle, rien ne justifiait le chiffre. « 4 charges » n'est pas une
    ///     pièce ; quatre noms avec leurs dates de naissance, si.
    ///
    ///   • Un enfant qui atteint 18 ans sort de l'assiette des charges, et personne ne
    ///     s'en aperçoit. La réduction d'impôt continue d'être appliquée, et c'est
    ///     l'employeur qui répond du trop-perçu.
    ///
    /// Cette table règle les deux : chaque charge porte un nom et une date de naissance,
    /// et le décompte applicable à une période se calcule au lieu de se déclarer.
    ///
    /// <b>Le décompte reste proposé, jamais imposé.</b> Le champ <c>FamilyCharges</c> de
    /// la fiche continue de commander le calcul de l'IUTS — un salarié peut avoir une
    /// situation que la règle d'âge ne décrit pas. L'écran signale l'écart et propose de
    /// reprendre le décompte ; il ne le fait pas dans le dos de l'utilisateur.
    /// </summary>
    public class PersonneACharge : INotifyPropertyChanged
    {
        /// <summary>Âge au-delà duquel un enfant cesse d'ouvrir droit à réduction.</summary>
        public const int AgeLimiteEnfant = 18;

        /// <summary>Âge limite prolongé pour un enfant qui poursuit des études.</summary>
        public const int AgeLimiteEnfantEtudiant = 25;

        private int _id;
        private string _employeeId;
        private string _nom;
        private string _prenom;
        private LienParente _lien;
        private DateTime? _dateNaissance;
        private bool _poursuitDesEtudes;
        private bool _situationDeHandicap;
        private bool _compteCommeCharge;
        private string _commentaire;

        public int Id
        {
            get { return _id; }
            set { if (_id != value) { _id = value; OnPropertyChanged(); } }
        }

        public string EmployeeId
        {
            get { return _employeeId; }
            set { if (_employeeId != value) { _employeeId = value; OnPropertyChanged(); } }
        }

        public string Nom
        {
            get { return _nom; }
            set
            {
                if (_nom != value)
                {
                    _nom = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(NomComplet));
                }
            }
        }

        public string Prenom
        {
            get { return _prenom; }
            set
            {
                if (_prenom != value)
                {
                    _prenom = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(NomComplet));
                }
            }
        }

        public LienParente Lien
        {
            get { return _lien; }
            set
            {
                if (_lien != value)
                {
                    _lien = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(LienLibelle));
                    OnPropertyChanged(nameof(Situation));
                }
            }
        }

        /// <summary>Date de naissance. Nulle tant qu'elle n'est pas connue.</summary>
        public DateTime? DateNaissance
        {
            get { return _dateNaissance; }
            set
            {
                if (_dateNaissance != value)
                {
                    _dateNaissance = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(Age));
                    OnPropertyChanged(nameof(AgeLibelle));
                    OnPropertyChanged(nameof(Situation));
                }
            }
        }

        /// <summary>Prolonge la limite d'âge d'un enfant jusqu'à 25 ans.</summary>
        public bool PoursuitDesEtudes
        {
            get { return _poursuitDesEtudes; }
            set
            {
                if (_poursuitDesEtudes != value)
                {
                    _poursuitDesEtudes = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(Situation));
                }
            }
        }

        /// <summary>Supprime toute limite d'âge.</summary>
        public bool SituationDeHandicap
        {
            get { return _situationDeHandicap; }
            set
            {
                if (_situationDeHandicap != value)
                {
                    _situationDeHandicap = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(Situation));
                }
            }
        }

        /// <summary>
        /// Retenue dans le décompte proposé. Décochée, la personne reste au dossier mais
        /// n'entre pas dans le calcul — un enfant dont l'autre parent porte la charge, par
        /// exemple.
        /// </summary>
        public bool CompteCommeCharge
        {
            get { return _compteCommeCharge; }
            set
            {
                if (_compteCommeCharge != value)
                {
                    _compteCommeCharge = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(Situation));
                }
            }
        }

        public string Commentaire
        {
            get { return _commentaire; }
            set { if (_commentaire != value) { _commentaire = value; OnPropertyChanged(); } }
        }

        public PersonneACharge()
        {
            _lien = LienParente.Enfant;
            _compteCommeCharge = true;
        }

        // ── Dérivés ──────────────────────────────────────────────────────────

        public string NomComplet
        {
            get { return string.Format("{0} {1}", Nom, Prenom).Trim(); }
        }

        public string LienLibelle
        {
            get
            {
                switch (Lien)
                {
                    case LienParente.Conjoint: return "Conjoint";
                    case LienParente.Ascendant: return "Ascendant";
                    case LienParente.Autre: return "Autre";
                    default: return "Enfant";
                }
            }
        }

        /// <summary>Âge révolu aujourd'hui, ou null si la date de naissance est inconnue.</summary>
        public int? Age
        {
            get { return AgeA(DateTime.Today); }
        }

        /// <summary>Âge révolu à une date donnée — celle de la période de paie.</summary>
        public int? AgeA(DateTime date)
        {
            if (!DateNaissance.HasValue) return null;

            var naissance = DateNaissance.Value.Date;
            if (naissance > date.Date) return null;

            int age = date.Year - naissance.Year;
            if (naissance.Date > date.Date.AddYears(-age)) age--;
            return age;
        }

        public string AgeLibelle
        {
            get
            {
                int? age = Age;
                return age.HasValue ? string.Format("{0} ans", age.Value) : "—";
            }
        }

        /// <summary>
        /// Cette personne ouvre-t-elle droit à réduction à la date donnée ?
        ///
        /// Règle appliquée : un conjoint ou un ascendant sans condition d'âge ; un enfant
        /// jusqu'à 18 ans, 25 s'il poursuit des études, sans limite en situation de
        /// handicap. Une date de naissance inconnue est traitée comme ouvrant droit —
        /// refuser silencieusement une charge faute d'information serait pire que la
        /// signaler.
        /// </summary>
        public bool OuvreDroitA(DateTime date)
        {
            if (!CompteCommeCharge) return false;
            if (SituationDeHandicap) return true;
            if (Lien != LienParente.Enfant) return true;

            int? age = AgeA(date);
            if (!age.HasValue) return true;

            int limite = PoursuitDesEtudes ? AgeLimiteEnfantEtudiant : AgeLimiteEnfant;
            return age.Value < limite;
        }

        /// <summary>Situation lisible, telle qu'elle s'affiche en bout de ligne.</summary>
        public string Situation
        {
            get
            {
                if (!CompteCommeCharge) return "Hors décompte";
                if (SituationDeHandicap) return "Sans limite d'âge";

                if (Lien != LienParente.Enfant) return "Compte";

                int? age = Age;
                if (!age.HasValue) return "Date de naissance à renseigner";

                int limite = PoursuitDesEtudes ? AgeLimiteEnfantEtudiant : AgeLimiteEnfant;

                if (age.Value >= limite)
                    return string.Format("Ne compte plus depuis {0} ans", limite);

                int reste = limite - age.Value;
                if (reste <= 1)
                    return string.Format("Sort du décompte dans moins d'un an");

                return string.Format("Compte encore {0} an(s)", reste);
            }
        }

        /// <summary>Vrai si la ligne mérite l'attention de l'utilisateur.</summary>
        public bool ASignaler
        {
            get
            {
                if (!CompteCommeCharge) return false;
                if (Lien != LienParente.Enfant || SituationDeHandicap) return false;

                int? age = Age;
                if (!age.HasValue) return true;

                int limite = PoursuitDesEtudes ? AgeLimiteEnfantEtudiant : AgeLimiteEnfant;
                return age.Value >= limite - 1;
            }
        }

        public PersonneACharge Clone()
        {
            return new PersonneACharge
            {
                Id = Id,
                EmployeeId = EmployeeId,
                Nom = Nom,
                Prenom = Prenom,
                Lien = Lien,
                DateNaissance = DateNaissance,
                PoursuitDesEtudes = PoursuitDesEtudes,
                SituationDeHandicap = SituationDeHandicap,
                CompteCommeCharge = CompteCommeCharge,
                Commentaire = Commentaire
            };
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            var handler = PropertyChanged;
            if (handler != null)
                handler(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
