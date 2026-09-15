using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace IUTSCalculator.Models
{
    /// <summary>Sens d'une rubrique sur le bulletin.</summary>
    public enum SensRubrique
    {
        /// <summary>S'ajoute au salaire brut (salaire de base, indemnités, primes).</summary>
        Gain = 0,
        /// <summary>Se déduit du net à payer (cotisations, impôt, prêts, oppositions).</summary>
        Retenue = 1,
        /// <summary>N'entre dans aucun total : ligne d'information ou base intermédiaire.</summary>
        Information = 2
    }

    /// <summary>Manière dont le montant d'une rubrique est obtenu.</summary>
    public enum ModeCalculRubrique
    {
        /// <summary>Montant saisi tel quel sur la fiche du salarié ou en variable du mois.</summary>
        MontantFixe = 0,
        /// <summary>Base × taux, la base étant une autre rubrique ou le salaire brut.</summary>
        BaseParTaux = 1,
        /// <summary>Pourcentage du salaire de base.</summary>
        PourcentageSalaireBase = 2,
        /// <summary>Calculée par le moteur de paie (IUTS, CNSS) : non modifiable ici.</summary>
        Moteur = 3
    }

    /// <summary>
    /// Une ligne du plan de paie.
    ///
    /// C'est le module SOC-01 de la feuille de route, et le verrou dont presque tout le
    /// reste dépend. Jusqu'ici les éléments de paie étaient des colonnes figées du modèle
    /// <see cref="Employee"/> : ajouter une prime de rendement ou une retenue de prêt
    /// imposait de modifier le modèle, la base, le calcul, les cinq modèles de bulletin
    /// et les exports. Une rubrique se crée désormais depuis l'écran « Plan de paie »,
    /// sans livraison de code.
    ///
    /// Les rubriques marquées <see cref="EstSysteme"/> reprennent les éléments déjà gérés
    /// par le moteur de calcul audité (salaire de base, indemnités plafonnées, CNSS,
    /// IUTS) : leur code et leur sens sont figés pour que le calcul continue de les
    /// reconnaître, mais leur libellé, leur ordre d'impression et leur compte comptable
    /// restent modifiables.
    /// </summary>
    public class Rubrique : INotifyPropertyChanged
    {
        private string _code;
        private string _libelle;
        private SensRubrique _sens;
        private ModeCalculRubrique _modeCalcul;
        private decimal _montantFixe;
        private decimal _tauxSalarial;
        private decimal _tauxPatronal;
        private string _codeBase;
        private bool _soumisIUTS;
        private bool _soumisCNSS;
        private bool _soumisTPA = true;
        private bool _plafonne;
        private decimal _plafond;
        private decimal _pourcentagePlafond;
        private int _ordre;
        private bool _active = true;
        private bool _estSysteme;
        private string _compteComptable;
        private string _compteContrepartie;
        private string _commentaire;

        /// <summary>Code court et unique, servant de référence dans les calculs (ex. SALBASE).</summary>
        public string Code
        {
            get { return _code; }
            set { if (_code != value) { _code = value; OnPropertyChanged(); OnPropertyChanged(nameof(Designation)); } }
        }

        /// <summary>Libellé imprimé sur le bulletin.</summary>
        public string Libelle
        {
            get { return _libelle; }
            set { if (_libelle != value) { _libelle = value; OnPropertyChanged(); OnPropertyChanged(nameof(Designation)); } }
        }

        public SensRubrique Sens
        {
            get { return _sens; }
            set { if (_sens != value) { _sens = value; OnPropertyChanged(); OnPropertyChanged(nameof(SensLibelle)); } }
        }

        public ModeCalculRubrique ModeCalcul
        {
            get { return _modeCalcul; }
            set { if (_modeCalcul != value) { _modeCalcul = value; OnPropertyChanged(); OnPropertyChanged(nameof(ModeCalculLibelle)); } }
        }

        /// <summary>Montant par défaut, pour une rubrique à montant fixe.</summary>
        public decimal MontantFixe
        {
            get { return _montantFixe; }
            set { if (_montantFixe != value) { _montantFixe = value; OnPropertyChanged(); } }
        }

        /// <summary>Taux appliqué à la base, part salariale (0,055 pour 5,5 %).</summary>
        public decimal TauxSalarial
        {
            get { return _tauxSalarial; }
            set { if (_tauxSalarial != value) { _tauxSalarial = value; OnPropertyChanged(); } }
        }

        /// <summary>Taux appliqué à la base, part patronale.</summary>
        public decimal TauxPatronal
        {
            get { return _tauxPatronal; }
            set { if (_tauxPatronal != value) { _tauxPatronal = value; OnPropertyChanged(); } }
        }

        /// <summary>Code de la rubrique servant de base de calcul (vide = salaire brut).</summary>
        public string CodeBase
        {
            get { return _codeBase; }
            set { if (_codeBase != value) { _codeBase = value; OnPropertyChanged(); } }
        }

        /// <summary>La rubrique entre dans la base imposable à l'IUTS.</summary>
        public bool SoumisIUTS
        {
            get { return _soumisIUTS; }
            set { if (_soumisIUTS != value) { _soumisIUTS = value; OnPropertyChanged(); } }
        }

        /// <summary>La rubrique entre dans l'assiette des cotisations CNSS.</summary>
        public bool SoumisCNSS
        {
            get { return _soumisCNSS; }
            set { if (_soumisCNSS != value) { _soumisCNSS = value; OnPropertyChanged(); } }
        }

        /// <summary>
        /// La rubrique entre dans l'assiette de la taxe patronale d'apprentissage
        /// (module DEC-02). Vraie par défaut : une rubrique existante ou nouvellement
        /// créée reste soumise à la TPA tant que personne ne l'en exonère explicitement,
        /// ce qui laisse inchangée l'assiette de tout dossier qui n'a jamais touché ce
        /// réglage.
        /// </summary>
        public bool SoumisTPA
        {
            get { return _soumisTPA; }
            set { if (_soumisTPA != value) { _soumisTPA = value; OnPropertyChanged(); } }
        }

        /// <summary>La rubrique bénéficie d'une exonération plafonnée.</summary>
        public bool Plafonne
        {
            get { return _plafonne; }
            set { if (_plafonne != value) { _plafonne = value; OnPropertyChanged(); OnPropertyChanged(nameof(PlafondLibelle)); } }
        }

        /// <summary>Plafond d'exonération en valeur absolue (FCFA).</summary>
        public decimal Plafond
        {
            get { return _plafond; }
            set { if (_plafond != value) { _plafond = value; OnPropertyChanged(); OnPropertyChanged(nameof(PlafondLibelle)); } }
        }

        /// <summary>
        /// Plafond exprimé en pourcentage du salaire brut (0,20 pour 20 %). Le plafond
        /// effectif est le plus petit des deux, comme le prévoit l'article 106 du CGI.
        /// </summary>
        public decimal PourcentagePlafond
        {
            get { return _pourcentagePlafond; }
            set { if (_pourcentagePlafond != value) { _pourcentagePlafond = value; OnPropertyChanged(); OnPropertyChanged(nameof(PlafondLibelle)); } }
        }

        /// <summary>Ordre d'impression sur le bulletin.</summary>
        public int Ordre
        {
            get { return _ordre; }
            set { if (_ordre != value) { _ordre = value; OnPropertyChanged(); } }
        }

        public bool Active
        {
            get { return _active; }
            set { if (_active != value) { _active = value; OnPropertyChanged(); } }
        }

        /// <summary>Rubrique livrée avec l'application et reconnue par le moteur de calcul.</summary>
        public bool EstSysteme
        {
            get { return _estSysteme; }
            set { if (_estSysteme != value) { _estSysteme = value; OnPropertyChanged(); OnPropertyChanged(nameof(CodeModifiable)); } }
        }

        /// <summary>Compte général SYSCOHADA mouvementé par cette rubrique (module CPT-01).</summary>
        public string CompteComptable
        {
            get { return _compteComptable; }
            set { if (_compteComptable != value) { _compteComptable = value; OnPropertyChanged(); } }
        }

        /// <summary>Compte de contrepartie (tiers ou organisme social).</summary>
        public string CompteContrepartie
        {
            get { return _compteContrepartie; }
            set { if (_compteContrepartie != value) { _compteContrepartie = value; OnPropertyChanged(); } }
        }

        public string Commentaire
        {
            get { return _commentaire; }
            set { if (_commentaire != value) { _commentaire = value; OnPropertyChanged(); } }
        }

        // ── Affichage ────────────────────────────────────────────────────────

        /// <summary>Le code d'une rubrique système est figé : le moteur s'y réfère.</summary>
        public bool CodeModifiable
        {
            get { return !EstSysteme; }
        }

        public string Designation
        {
            get { return string.Format("{0} — {1}", Code, Libelle); }
        }

        public string SensLibelle
        {
            get
            {
                switch (Sens)
                {
                    case SensRubrique.Gain: return "Gain";
                    case SensRubrique.Retenue: return "Retenue";
                    default: return "Information";
                }
            }
        }

        public string ModeCalculLibelle
        {
            get
            {
                switch (ModeCalcul)
                {
                    case ModeCalculRubrique.MontantFixe: return "Montant fixe";
                    case ModeCalculRubrique.BaseParTaux: return "Base × taux";
                    case ModeCalculRubrique.PourcentageSalaireBase: return "% du salaire de base";
                    default: return "Calculée par le moteur";
                }
            }
        }

        /// <summary>Plafond lisible, tel qu'il s'affiche en face de la rubrique.</summary>
        public string PlafondLibelle
        {
            get
            {
                if (!Plafonne) return "—";
                if (PourcentagePlafond > 0 && Plafond > 0)
                    return string.Format("{0:P0} du brut, max {1:N0}", PourcentagePlafond, Plafond);
                if (PourcentagePlafond > 0)
                    return string.Format("{0:P0} du brut", PourcentagePlafond);
                return string.Format("max {0:N0}", Plafond);
            }
        }

        public Rubrique Clone()
        {
            return new Rubrique
            {
                Code = Code,
                Libelle = Libelle,
                Sens = Sens,
                ModeCalcul = ModeCalcul,
                MontantFixe = MontantFixe,
                TauxSalarial = TauxSalarial,
                TauxPatronal = TauxPatronal,
                CodeBase = CodeBase,
                SoumisIUTS = SoumisIUTS,
                SoumisCNSS = SoumisCNSS,
                SoumisTPA = SoumisTPA,
                Plafonne = Plafonne,
                Plafond = Plafond,
                PourcentagePlafond = PourcentagePlafond,
                Ordre = Ordre,
                Active = Active,
                EstSysteme = EstSysteme,
                CompteComptable = CompteComptable,
                CompteContrepartie = CompteContrepartie,
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
