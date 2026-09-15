using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace IUTSCalculator.Models
{
    /// <summary>
    /// Représente le décompte complet du Solde de Tout Compte (STC) lors du départ d'un travailleur,
    /// incluant le salaire de présence, les indemnités légales/conventionnelles, les déductions
    /// et le traitement fiscal/social conforme au droit burkinabè.
    /// </summary>
    public class SoldeToutCompte : INotifyPropertyChanged
    {
        private int _id;
        private string _employeeId;
        private string _nomSalarie;
        private DateTime _dateSortie = DateTime.Today;
        private MotifDepart _motif = MotifDepart.Demission;
        private DateTime _dateEntree;
        private int _ancienneteAnnees;
        private int _ancienneteMois;
        private string _categorie;
        private string _echelon;
        private string _fonction;

        private decimal _salaireReference;
        private decimal _salairePresence;
        private decimal _joursPresence;
        private decimal _joursCongesDus;
        private decimal _indemniteConges;
        private int _dureePreavisMois;
        private StatutPreavis _statutPreavis = StatutPreavis.Effectue;
        private decimal _indemnitePreavis;
        private decimal _indemniteLicenciement;
        private decimal _tauxLicenciementApplique;
        private decimal _indemnite13emeMois;
        private decimal _mois13emeMois;
        private decimal _indemnitesDiverses;
        private string _libelleIndemnitesDiverses;
        private decimal _deductionsPrets;
        private decimal _deductionsAutres;
        private string _libelleDeductionsAutres;

        private decimal _estimationIuts;
        private decimal _estimationCnss;
        private string _commentaire;
        private DateTime _dateCreation = DateTime.Now;
        private DateTime _dateModification = DateTime.Now;

        public int Id
        {
            get => _id;
            set { if (_id != value) { _id = value; OnPropertyChanged(); } }
        }

        public string EmployeeId
        {
            get => _employeeId;
            set { if (_employeeId != value) { _employeeId = value; OnPropertyChanged(); } }
        }

        public string NomSalarie
        {
            get => _nomSalarie;
            set { if (_nomSalarie != value) { _nomSalarie = value; OnPropertyChanged(); } }
        }

        public DateTime DateSortie
        {
            get => _dateSortie;
            set { if (_dateSortie != value) { _dateSortie = value; OnPropertyChanged(); } }
        }

        public MotifDepart Motif
        {
            get => _motif;
            set
            {
                if (_motif != value)
                {
                    _motif = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(MotifLibelle));
                }
            }
        }

        public string MotifLibelle
        {
            get
            {
                switch (Motif)
                {
                    case MotifDepart.Demission: return "Démission";
                    case MotifDepart.FinContrat: return "Fin de contrat CDD";
                    case MotifDepart.LicenciementPersonnel: return "Licenciement pour motif personnel";
                    case MotifDepart.LicenciementEconomique: return "Licenciement pour motif économique";
                    case MotifDepart.LicenciementFauteLourde: return "Licenciement pour faute lourde";
                    case MotifDepart.RetraiteDepart: return "Départ volontaire à la retraite";
                    case MotifDepart.RetraiteMise: return "Mise à la retraite par l'employeur";
                    case MotifDepart.RuptureConventionnelle: return "Rupture d'un commun accord";
                    case MotifDepart.Deces: return "Décès du travailleur";
                    default: return "Autre motif";
                }
            }
        }

        public DateTime DateEntree
        {
            get => _dateEntree;
            set { if (_dateEntree != value) { _dateEntree = value; OnPropertyChanged(); } }
        }

        public int AncienneteAnnees
        {
            get => _ancienneteAnnees;
            set
            {
                if (_ancienneteAnnees != value)
                {
                    _ancienneteAnnees = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(AncienneteTexte));
                }
            }
        }

        public int AncienneteMois
        {
            get => _ancienneteMois;
            set
            {
                if (_ancienneteMois != value)
                {
                    _ancienneteMois = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(AncienneteTexte));
                }
            }
        }

        public string AncienneteTexte
        {
            get
            {
                if (AncienneteAnnees > 0 && AncienneteMois > 0)
                    return $"{AncienneteAnnees} an(s) et {AncienneteMois} mois";
                if (AncienneteAnnees > 0)
                    return $"{AncienneteAnnees} an(s)";
                return $"{AncienneteMois} mois";
            }
        }

        public string Categorie
        {
            get => _categorie;
            set { if (_categorie != value) { _categorie = value; OnPropertyChanged(); } }
        }

        public string Echelon
        {
            get => _echelon;
            set { if (_echelon != value) { _echelon = value; OnPropertyChanged(); } }
        }

        public string Fonction
        {
            get => _fonction;
            set { if (_fonction != value) { _fonction = value; OnPropertyChanged(); } }
        }

        public decimal SalaireReference
        {
            get => _salaireReference;
            set { if (_salaireReference != value) { _salaireReference = value; OnPropertyChanged(); RecalculerTotaux(); } }
        }

        public decimal SalairePresence
        {
            get => _salairePresence;
            set { if (_salairePresence != value) { _salairePresence = value; OnPropertyChanged(); RecalculerTotaux(); } }
        }

        public decimal JoursPresence
        {
            get => _joursPresence;
            set { if (_joursPresence != value) { _joursPresence = value; OnPropertyChanged(); } }
        }

        public decimal JoursCongesDus
        {
            get => _joursCongesDus;
            set { if (_joursCongesDus != value) { _joursCongesDus = value; OnPropertyChanged(); } }
        }

        public decimal IndemniteConges
        {
            get => _indemniteConges;
            set { if (_indemniteConges != value) { _indemniteConges = value; OnPropertyChanged(); RecalculerTotaux(); } }
        }

        public int DureePreavisMois
        {
            get => _dureePreavisMois;
            set { if (_dureePreavisMois != value) { _dureePreavisMois = value; OnPropertyChanged(); } }
        }

        public StatutPreavis StatutPreavis
        {
            get => _statutPreavis;
            set
            {
                if (_statutPreavis != value)
                {
                    _statutPreavis = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(StatutPreavisLibelle));
                }
            }
        }

        public string StatutPreavisLibelle
        {
            get
            {
                switch (StatutPreavis)
                {
                    case StatutPreavis.Effectue: return "Préavis effectué";
                    case StatutPreavis.DispensePaye: return "Dispensé et rémunéré";
                    case StatutPreavis.Inapplicable: return "Non applicable / Sans préavis";
                    default: return "Inconnu";
                }
            }
        }

        public decimal IndemnitePreavis
        {
            get => _indemnitePreavis;
            set { if (_indemnitePreavis != value) { _indemnitePreavis = value; OnPropertyChanged(); RecalculerTotaux(); } }
        }

        public decimal IndemniteLicenciement
        {
            get => _indemniteLicenciement;
            set { if (_indemniteLicenciement != value) { _indemniteLicenciement = value; OnPropertyChanged(); RecalculerTotaux(); } }
        }

        public decimal TauxLicenciementApplique
        {
            get => _tauxLicenciementApplique;
            set { if (_tauxLicenciementApplique != value) { _tauxLicenciementApplique = value; OnPropertyChanged(); } }
        }

        public decimal Indemnite13emeMois
        {
            get => _indemnite13emeMois;
            set { if (_indemnite13emeMois != value) { _indemnite13emeMois = value; OnPropertyChanged(); RecalculerTotaux(); } }
        }

        public decimal Mois13emeMois
        {
            get => _mois13emeMois;
            set { if (_mois13emeMois != value) { _mois13emeMois = value; OnPropertyChanged(); } }
        }

        public decimal IndemnitesDiverses
        {
            get => _indemnitesDiverses;
            set { if (_indemnitesDiverses != value) { _indemnitesDiverses = value; OnPropertyChanged(); RecalculerTotaux(); } }
        }

        public string LibelleIndemnitesDiverses
        {
            get => _libelleIndemnitesDiverses;
            set { if (_libelleIndemnitesDiverses != value) { _libelleIndemnitesDiverses = value; OnPropertyChanged(); } }
        }

        public decimal DeductionsPrets
        {
            get => _deductionsPrets;
            set { if (_deductionsPrets != value) { _deductionsPrets = value; OnPropertyChanged(); RecalculerTotaux(); } }
        }

        public decimal DeductionsAutres
        {
            get => _deductionsAutres;
            set { if (_deductionsAutres != value) { _deductionsAutres = value; OnPropertyChanged(); RecalculerTotaux(); } }
        }

        public string LibelleDeductionsAutres
        {
            get => _libelleDeductionsAutres;
            set { if (_libelleDeductionsAutres != value) { _libelleDeductionsAutres = value; OnPropertyChanged(); } }
        }

        public decimal EstimationIUTS
        {
            get => _estimationIuts;
            set { if (_estimationIuts != value) { _estimationIuts = value; OnPropertyChanged(); RecalculerTotaux(); } }
        }

        public decimal EstimationCNSS
        {
            get => _estimationCnss;
            set { if (_estimationCnss != value) { _estimationCnss = value; OnPropertyChanged(); RecalculerTotaux(); } }
        }

        public string Commentaire
        {
            get => _commentaire;
            set { if (_commentaire != value) { _commentaire = value; OnPropertyChanged(); } }
        }

        public DateTime DateCreation
        {
            get => _dateCreation;
            set { if (_dateCreation != value) { _dateCreation = value; OnPropertyChanged(); } }
        }

        public DateTime DateModification
        {
            get => _dateModification;
            set { if (_dateModification != value) { _dateModification = value; OnPropertyChanged(); } }
        }

        // --- Totaux calculés ---

        /// <summary>
        /// Total brut versé au titre de la rupture du contrat de travail.
        /// </summary>
        public decimal TotalBrut =>
            SalairePresence + IndemniteConges + IndemnitePreavis +
            IndemniteLicenciement + Indemnite13emeMois + IndemnitesDiverses;

        /// <summary>
        /// Part exonérée d'impôt (IUTS) et de cotisations CNSS selon l'Art. 106 CGI (Indemnité de licenciement/retraite légale).
        /// </summary>
        public decimal TotalExonere => IndemniteLicenciement;

        /// <summary>
        /// Assiette brute soumise aux retenues fiscales et sociales (Salaire de présence, Congés, Préavis, Gratification).
        /// </summary>
        public decimal TotalImposable => Math.Max(0m, TotalBrut - TotalExonere);

        /// <summary>
        /// Total des retenues et déductions (fiscales, sociales et dettes de prêts).
        /// </summary>
        public decimal TotalRetenues =>
            EstimationIUTS + EstimationCNSS + DeductionsPrets + DeductionsAutres;

        /// <summary>
        /// Montant Net à payer au salarié pour solde de tout compte.
        /// </summary>
        public decimal TotalNet => Math.Max(0m, TotalBrut - TotalRetenues);

        private void RecalculerTotaux()
        {
            OnPropertyChanged(nameof(TotalBrut));
            OnPropertyChanged(nameof(TotalExonere));
            OnPropertyChanged(nameof(TotalImposable));
            OnPropertyChanged(nameof(TotalRetenues));
            OnPropertyChanged(nameof(TotalNet));
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
