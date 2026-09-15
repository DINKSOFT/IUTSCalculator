using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace IUTSCalculator.Models
{
    /// <summary>
    /// Un élément variable saisi pour un salarié sur une période de paie (module PAI-02).
    ///
    /// C'est le chaînon qui manquait entre le plan de paie (SOC-01) et le mois de paie
    /// (PAI-01). Jusqu'ici, tout ce qui varie d'un mois à l'autre — heures
    /// supplémentaires, prime exceptionnelle, absence, retenue ponctuelle — n'avait
    /// nulle part où se poser : il fallait modifier la fiche du salarié, ce qui écrase la
    /// situation permanente avec une circonstance passagère et ne laisse aucune trace du
    /// mois suivant.
    ///
    /// Une variable porte la rubrique qu'elle alimente, une quantité et un montant. Selon
    /// la rubrique, l'un ou l'autre fait foi : 12 heures supplémentaires se saisissent en
    /// quantité, une prime de 50 000 en montant.
    /// </summary>
    public class VariablePaie : INotifyPropertyChanged
    {
        private int _id;
        private int _annee;
        private int _mois;
        private string _employeeId;
        private string _nomSalarie;
        private string _codeRubrique;
        private string _libelleRubrique;
        private SensRubrique _sens;
        private decimal _quantite;
        private decimal _montant;
        private string _commentaire;
        private DateTime _dateSaisie;

        public int Id
        {
            get { return _id; }
            set { if (_id != value) { _id = value; OnPropertyChanged(); } }
        }

        public int Annee
        {
            get { return _annee; }
            set { if (_annee != value) { _annee = value; OnPropertyChanged(); } }
        }

        /// <summary>Mois de 1 à 12.</summary>
        public int Mois
        {
            get { return _mois; }
            set { if (_mois != value) { _mois = value; OnPropertyChanged(); } }
        }

        public string EmployeeId
        {
            get { return _employeeId; }
            set { if (_employeeId != value) { _employeeId = value; OnPropertyChanged(); } }
        }

        /// <summary>
        /// Nom du salarié au moment de la lecture. Il n'est PAS stocké : il est joint
        /// depuis la fiche, pour que l'écran reste lisible sans dupliquer une donnée qui
        /// vit ailleurs.
        /// </summary>
        public string NomSalarie
        {
            get { return _nomSalarie; }
            set { if (_nomSalarie != value) { _nomSalarie = value; OnPropertyChanged(); } }
        }

        /// <summary>Code de la rubrique alimentée, tel qu'il figure au plan de paie.</summary>
        public string CodeRubrique
        {
            get { return _codeRubrique; }
            set
            {
                if (_codeRubrique != value)
                {
                    _codeRubrique = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(Designation));
                }
            }
        }

        /// <summary>Libellé de la rubrique, joint depuis le plan de paie.</summary>
        public string LibelleRubrique
        {
            get { return _libelleRubrique; }
            set
            {
                if (_libelleRubrique != value)
                {
                    _libelleRubrique = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(Designation));
                }
            }
        }

        /// <summary>Sens de la rubrique, joint depuis le plan de paie.</summary>
        public SensRubrique Sens
        {
            get { return _sens; }
            set
            {
                if (_sens != value)
                {
                    _sens = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(SensLibelle));
                    OnPropertyChanged(nameof(MontantSigne));
                }
            }
        }

        /// <summary>Quantité : heures, jours, unités — selon la rubrique.</summary>
        public decimal Quantite
        {
            get { return _quantite; }
            set { if (_quantite != value) { _quantite = value; OnPropertyChanged(); } }
        }

        public decimal Montant
        {
            get { return _montant; }
            set
            {
                if (_montant != value)
                {
                    _montant = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(MontantSigne));
                }
            }
        }

        public string Commentaire
        {
            get { return _commentaire; }
            set { if (_commentaire != value) { _commentaire = value; OnPropertyChanged(); } }
        }

        public DateTime DateSaisie
        {
            get { return _dateSaisie; }
            set { if (_dateSaisie != value) { _dateSaisie = value; OnPropertyChanged(); } }
        }

        public VariablePaie()
        {
            _dateSaisie = DateTime.Now;
            _sens = SensRubrique.Gain;
        }

        // ── Dérivés ──────────────────────────────────────────────────────────

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

        /// <summary>
        /// Montant signé : négatif pour une retenue, nul pour une ligne d'information.
        /// C'est ce qui permet de totaliser une colonne sans se tromper de sens.
        /// </summary>
        public decimal MontantSigne
        {
            get
            {
                switch (Sens)
                {
                    case SensRubrique.Retenue: return -Montant;
                    case SensRubrique.Information: return 0m;
                    default: return Montant;
                }
            }
        }

        public string Designation
        {
            get
            {
                return string.IsNullOrWhiteSpace(LibelleRubrique)
                    ? (CodeRubrique ?? "")
                    : string.Format("{0} — {1}", CodeRubrique, LibelleRubrique);
            }
        }

        public string Periode
        {
            get { return string.Format("{0:D4}-{1:D2}", Annee, Mois); }
        }

        /// <summary>Une variable sans rubrique ni montant ne veut rien dire.</summary>
        public bool EstValide
        {
            get
            {
                return !string.IsNullOrWhiteSpace(EmployeeId)
                    && !string.IsNullOrWhiteSpace(CodeRubrique)
                    && (Montant != 0m || Quantite != 0m);
            }
        }

        public VariablePaie Clone()
        {
            return new VariablePaie
            {
                Id = Id,
                Annee = Annee,
                Mois = Mois,
                EmployeeId = EmployeeId,
                NomSalarie = NomSalarie,
                CodeRubrique = CodeRubrique,
                LibelleRubrique = LibelleRubrique,
                Sens = Sens,
                Quantite = Quantite,
                Montant = Montant,
                Commentaire = Commentaire,
                DateSaisie = DateSaisie
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
