using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace IUTSCalculator.Models
{
    /// <summary>
    /// Type de rappel ou régularisation de paie.
    /// </summary>
    public enum TypeRappelPaie
    {
        /// <summary>Augmentation rétroactive de salaire de base contractuel ou d'échelon conventionnel.</summary>
        RappelSalaire = 0,

        /// <summary>Rappel de prime, gratification ou indemnité omise.</summary>
        RappelPrime = 1,

        /// <summary>Rattrapage d'heures supplémentaires omises sur un mois clos antérieur.</summary>
        OmissionHeuresSup = 2,

        /// <summary>Trop-perçu sur salaire ou indemnité versée indûment (retenue régularisatrice).</summary>
        TropPercuSalaire = 3,

        /// <summary>Régularisation annuelle d'IUTS (crédit ou débit de fin d'exercice).</summary>
        RegularisationIuts = 4,

        /// <summary>Autre régularisation ponctuelle.</summary>
        Autre = 5
    }

    /// <summary>
    /// Sens de la ligne : Gain (ajoute au salaire) ou Retenue (déduit sur le bulletin).
    /// </summary>
    public enum SensRappel
    {
        /// <summary>Gain versé au salarié (positif).</summary>
        Gain = 0,

        /// <summary>Retenue opérée sur le salarié (déduction).</summary>
        Retenue = 1
    }

    /// <summary>
    /// Statut du rappel dans son cycle de vie.
    /// </summary>
    public enum StatutRappelPaie
    {
        /// <summary>Créé et validé, en attente d'imputation sur la paie du mois cible ouvert.</summary>
        EnAttente = 0,

        /// <summary>Imputé sur un bulletin de paie effectivement généré et enregistré.</summary>
        Impute = 1,

        /// <summary>Annulé avant ou après imputation suite à contestation ou erreur de saisie.</summary>
        Annule = 2
    }

    /// <summary>
    /// Statut d'une régularisation annuelle d'IUTS.
    /// </summary>
    public enum StatutRegulIuts
    {
        /// <summary>Calcul simulé à titre indicatif.</summary>
        Simule = 0,

        /// <summary>Validé par le gestionnaire, prêt à être converti en rappel sur bulletin.</summary>
        Valide = 1,

        /// <summary>Imputé sur le bulletin de fin d'année (décembre ou solde de tout compte).</summary>
        Impute = 2,

        /// <summary>Annulé.</summary>
        Annule = 3
    }

    /// <summary>
    /// Détail mensuel d'un calcul différentiel rétroactif sur une période passée.
    /// </summary>
    public class LigneRappelRetroactifDetail
    {
        public int Annee { get; set; }
        public int Mois { get; set; }
        public string PeriodeLibelle => $"{new DateTime(Annee, Mois, 1):MMMM yyyy}";

        public decimal AncienSalaireDeBase { get; set; }
        public decimal NouveauSalaireDeBase { get; set; }
        public decimal DeltaSalaireDeBase { get; set; }

        public decimal DeltaIndemnites { get; set; }
        public decimal DeltaBrut { get; set; }

        public decimal DeltaAssietteCnss { get; set; }
        public decimal DeltaCnssSalarial { get; set; }
        public decimal DeltaCnssPatronal { get; set; }

        public decimal DeltaTpa { get; set; }
        public decimal DeltaIuts { get; set; }
        public decimal DeltaNet { get; set; }

        public string Observation { get; set; }
    }

    /// <summary>
    /// Entité représentant un rappel de salaire, de prime ou un trop-perçu à imputer sur une paie ouverte.
    /// </summary>
    public class RappelPaie : INotifyPropertyChanged
    {
        private int _id;
        private string _employeeId;
        private string _nomSalarie;
        private string _matricule;
        private TypeRappelPaie _typeRappel = TypeRappelPaie.RappelSalaire;
        private string _libelle = "Rappel de salaire rétroactif";
        private SensRappel _sens = SensRappel.Gain;
        private DateTime? _moisOrigineDebut;
        private DateTime? _moisOrigineFin;
        private int _anneeImputation = DateTime.Now.Year;
        private int _moisImputation = DateTime.Now.Month;
        private decimal _montantBrut;
        private decimal _montantCnssSalarial;
        private decimal _montantCnssPatronal;
        private decimal _montantTPA;
        private decimal _montantIUTS;
        private decimal _montantNet;
        private bool _soumisIUTS = true;
        private bool _soumisCNSS = true;
        private bool _soumisTPA = true;
        private StatutRappelPaie _statut = StatutRappelPaie.EnAttente;
        private string _motif;
        private string _detailsCalculJson;
        private DateTime _dateCreation = DateTime.Now;
        private DateTime? _dateImputation;
        private string _creePar;

        public int Id
        {
            get => _id;
            set => SetProperty(ref _id, value);
        }

        public string EmployeeId
        {
            get => _employeeId;
            set => SetProperty(ref _employeeId, value);
        }

        public string NomSalarie
        {
            get => _nomSalarie;
            set => SetProperty(ref _nomSalarie, value);
        }

        public string Matricule
        {
            get => _matricule;
            set => SetProperty(ref _matricule, value);
        }

        public TypeRappelPaie TypeRappel
        {
            get => _typeRappel;
            set => SetProperty(ref _typeRappel, value);
        }

        public string Libelle
        {
            get => _libelle;
            set => SetProperty(ref _libelle, value);
        }

        public SensRappel Sens
        {
            get => _sens;
            set => SetProperty(ref _sens, value);
        }

        public DateTime? MoisOrigineDebut
        {
            get => _moisOrigineDebut;
            set => SetProperty(ref _moisOrigineDebut, value);
        }

        public DateTime? MoisOrigineFin
        {
            get => _moisOrigineFin;
            set => SetProperty(ref _moisOrigineFin, value);
        }

        public int AnneeImputation
        {
            get => _anneeImputation;
            set => SetProperty(ref _anneeImputation, value);
        }

        public int MoisImputation
        {
            get => _moisImputation;
            set => SetProperty(ref _moisImputation, value);
        }

        public decimal MontantBrut
        {
            get => _montantBrut;
            set => SetProperty(ref _montantBrut, value);
        }

        public decimal MontantCnssSalarial
        {
            get => _montantCnssSalarial;
            set => SetProperty(ref _montantCnssSalarial, value);
        }

        public decimal MontantCnssPatronal
        {
            get => _montantCnssPatronal;
            set => SetProperty(ref _montantCnssPatronal, value);
        }

        public decimal MontantTPA
        {
            get => _montantTPA;
            set => SetProperty(ref _montantTPA, value);
        }

        public decimal MontantIUTS
        {
            get => _montantIUTS;
            set => SetProperty(ref _montantIUTS, value);
        }

        public decimal MontantNet
        {
            get => _montantNet;
            set => SetProperty(ref _montantNet, value);
        }

        public bool SoumisIUTS
        {
            get => _soumisIUTS;
            set => SetProperty(ref _soumisIUTS, value);
        }

        public bool SoumisCNSS
        {
            get => _soumisCNSS;
            set => SetProperty(ref _soumisCNSS, value);
        }

        public bool SoumisTPA
        {
            get => _soumisTPA;
            set => SetProperty(ref _soumisTPA, value);
        }

        public StatutRappelPaie Statut
        {
            get => _statut;
            set => SetProperty(ref _statut, value);
        }

        public string Motif
        {
            get => _motif;
            set => SetProperty(ref _motif, value);
        }

        public string DetailsCalculJson
        {
            get => _detailsCalculJson;
            set => SetProperty(ref _detailsCalculJson, value);
        }

        public DateTime DateCreation
        {
            get => _dateCreation;
            set => SetProperty(ref _dateCreation, value);
        }

        public DateTime? DateImputation
        {
            get => _dateImputation;
            set => SetProperty(ref _dateImputation, value);
        }

        public string CreePar
        {
            get => _creePar;
            set => SetProperty(ref _creePar, value);
        }

        #region Propriétés d'Affichage UI

        public string PeriodeOrigineLibelle
        {
            get
            {
                if (!MoisOrigineDebut.HasValue)
                    return "—";

                if (!MoisOrigineFin.HasValue || (MoisOrigineDebut.Value.Year == MoisOrigineFin.Value.Year && MoisOrigineDebut.Value.Month == MoisOrigineFin.Value.Month))
                    return $"{MoisOrigineDebut.Value:MMMM yyyy}";

                return $"{MoisOrigineDebut.Value:MMM yyyy} à {MoisOrigineFin.Value:MMM yyyy}";
            }
        }

        public string PeriodeImputationLibelle => $"{new DateTime(AnneeImputation, MoisImputation, 1):MMMM yyyy}";

        public string TypeLibelle
        {
            get
            {
                switch (TypeRappel)
                {
                    case TypeRappelPaie.RappelSalaire: return "Rappel de salaire";
                    case TypeRappelPaie.RappelPrime: return "Rappel de prime";
                    case TypeRappelPaie.OmissionHeuresSup: return "Heures sup omises";
                    case TypeRappelPaie.TropPercuSalaire: return "Trop-perçu sur salaire";
                    case TypeRappelPaie.RegularisationIuts: return "Régularisation IUTS";
                    default: return "Autre régularisation";
                }
            }
        }

        public string SensLibelle => Sens == SensRappel.Gain ? "Gain (+)" : "Retenue (-)";

        public string StatutLibelle
        {
            get
            {
                switch (Statut)
                {
                    case StatutRappelPaie.EnAttente: return "En attente";
                    case StatutRappelPaie.Impute: return "Imputé";
                    case StatutRappelPaie.Annule: return "Annulé";
                    default: return Statut.ToString();
                }
            }
        }

        public string MontantAfficheSigne => Sens == SensRappel.Gain
            ? $"+{MontantBrut:N0} FCFA"
            : $"-{MontantBrut:N0} FCFA";

        #endregion

        public event PropertyChangedEventHandler PropertyChanged;
        protected bool SetProperty<T>(ref T storage, T value, [CallerMemberName] string propertyName = null)
        {
            if (EqualityComparer<T>.Default.Equals(storage, value)) return false;
            storage = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
            return true;
        }
    }

    /// <summary>
    /// Entité représentant le résultat de la régularisation annuelle d'IUTS d'un salarié.
    /// </summary>
    public class RegularisationIutsAnnuel : INotifyPropertyChanged
    {
        private int _id;
        private string _employeeId;
        private string _nomSalarie;
        private string _matricule;
        private int _annee = DateTime.Now.Year;
        private int _nombreMoisTravailles = 12;
        private decimal _cumulBrutImposable;
        private decimal _iutsTheoriqueAnnuel;
        private decimal _cumulIutsPreleve;
        private decimal _differenceRegularisation;
        private int _moisImputation = 12;
        private StatutRegulIuts _statut = StatutRegulIuts.Simule;
        private int? _rappelPaieId;
        private string _observations;
        private DateTime _dateCreation = DateTime.Now;
        private DateTime _dateModification = DateTime.Now;

        public int Id
        {
            get => _id;
            set => SetProperty(ref _id, value);
        }

        public string EmployeeId
        {
            get => _employeeId;
            set => SetProperty(ref _employeeId, value);
        }

        public string NomSalarie
        {
            get => _nomSalarie;
            set => SetProperty(ref _nomSalarie, value);
        }

        public string Matricule
        {
            get => _matricule;
            set => SetProperty(ref _matricule, value);
        }

        public int Annee
        {
            get => _annee;
            set => SetProperty(ref _annee, value);
        }

        public int NombreMoisTravailles
        {
            get => _nombreMoisTravailles;
            set => SetProperty(ref _nombreMoisTravailles, value);
        }

        public decimal CumulBrutImposable
        {
            get => _cumulBrutImposable;
            set => SetProperty(ref _cumulBrutImposable, value);
        }

        public decimal IutsTheoriqueAnnuel
        {
            get => _iutsTheoriqueAnnuel;
            set => SetProperty(ref _iutsTheoriqueAnnuel, value);
        }

        public decimal CumulIutsPreleve
        {
            get => _cumulIutsPreleve;
            set => SetProperty(ref _cumulIutsPreleve, value);
        }

        /// <summary>
        /// Écart = IutsTheoriqueAnnuel - CumulIutsPreleve.
        /// Positif : le salarié n'a pas assez payé, complément d'IUTS à retenir.
        /// Négatif : le salarié a trop payé, crédit d'IUTS à lui rembourser.
        /// </summary>
        public decimal DifferenceRegularisation
        {
            get => _differenceRegularisation;
            set => SetProperty(ref _differenceRegularisation, value);
        }

        public int MoisImputation
        {
            get => _moisImputation;
            set => SetProperty(ref _moisImputation, value);
        }

        public StatutRegulIuts Statut
        {
            get => _statut;
            set => SetProperty(ref _statut, value);
        }

        public int? RappelPaieId
        {
            get => _rappelPaieId;
            set => SetProperty(ref _rappelPaieId, value);
        }

        public string Observations
        {
            get => _observations;
            set => SetProperty(ref _observations, value);
        }

        public DateTime DateCreation
        {
            get => _dateCreation;
            set => SetProperty(ref _dateCreation, value);
        }

        public DateTime DateModification
        {
            get => _dateModification;
            set => SetProperty(ref _dateModification, value);
        }

        #region Propriétés d'Affichage UI

        public string ActionRecommandee
        {
            get
            {
                if (DifferenceRegularisation > 0m)
                    return "Complément d'impôt à retenir";
                if (DifferenceRegularisation < 0m)
                    return "Crédit d'impôt à rembourser";
                return "Aucun écart (conforme)";
            }
        }

        public string StatutLibelle
        {
            get
            {
                switch (Statut)
                {
                    case StatutRegulIuts.Simule: return "Simulé";
                    case StatutRegulIuts.Valide: return "Validé";
                    case StatutRegulIuts.Impute: return "Imputé";
                    case StatutRegulIuts.Annule: return "Annulé";
                    default: return Statut.ToString();
                }
            }
        }

        public decimal MontantAbsolu => Math.Abs(DifferenceRegularisation);

        #endregion

        public event PropertyChangedEventHandler PropertyChanged;
        protected bool SetProperty<T>(ref T storage, T value, [CallerMemberName] string propertyName = null)
        {
            if (EqualityComparer<T>.Default.Equals(storage, value)) return false;
            storage = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
            return true;
        }
    }
}
