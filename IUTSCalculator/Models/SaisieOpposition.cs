using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace IUTSCalculator.Models
{
    /// <summary>
    /// Type de saisie, cession ou opposition selon les qualifications légales burkinabè et OHADA.
    /// </summary>
    public enum TypeSaisieOpposition
    {
        /// <summary>Super-privilège légal : prime sur toutes les créances ordinaires et peut mordre sur la quotité insaisissable au-delà du SMIG.</summary>
        PensionAlimentaire = 0,

        /// <summary>Avis à tiers détenteur (Trésor Public, DGI, CNSS).</summary>
        AvisATiersDetenteur = 1,

        /// <summary>Saisie-arrêt sur salaire ordonnée par décision de justice exécutoire.</summary>
        SaisieArretJudiciaire = 2,

        /// <summary>Cession volontaire consentie par le travailleur (acte sous seing privé ou devant greffier).</summary>
        CessionVolontaire = 3,

        /// <summary>Opposition conservatoire ou diverse.</summary>
        AutreOpposition = 4
    }

    /// <summary>
    /// Statut du dossier de saisie ou cession.
    /// </summary>
    public enum StatutSaisieOpposition
    {
        /// <summary>Dossier actif, des retenues doivent être effectuées chaque mois.</summary>
        EnCours = 0,

        /// <summary>Dossier suspendu temporairement (accord amiable, instance de contestation).</summary>
        Suspendue = 1,

        /// <summary>Dette entièrement amortie et remboursée (arrêt automatique des retenues).</summary>
        Soldee = 2,

        /// <summary>Mainlevée judiciaire ou désistement du créancier notifié à l'employeur.</summary>
        Mainlevee = 3
    }

    /// <summary>
    /// Statut de versement de la retenue mensuelle.
    /// </summary>
    public enum StatutPaiementSaisie
    {
        /// <summary>Montant retenu sur le bulletin mais pas encore transféré.</summary>
        Retenu = 0,

        /// <summary>Montant effectivement reversé au greffe ou au créancier.</summary>
        Reverse = 1,

        /// <summary>Retenue annulée ou remboursée au salarié suite à régularisation.</summary>
        Annule = 2
    }

    /// <summary>
    /// Représente un titre de saisie-arrêt, une cession de salaire ou une opposition notifiée à l'employeur.
    /// </summary>
    public class SaisieOpposition : INotifyPropertyChanged
    {
        private int _id;
        private string _employeeId;
        private string _nomSalarie;
        private TypeSaisieOpposition _typeSaisie = TypeSaisieOpposition.SaisieArretJudiciaire;
        private string _numeroDossier;
        private string _creancier;
        private string _beneficiaire;
        private decimal _montantTotalDu;
        private decimal _retenueMensuelleFixee;
        private decimal _totalAmorti;
        private DateTime _dateDebut = DateTime.Today;
        private DateTime? _dateFin;
        private DateTime? _dateNotification;
        private string _tribunalOuAutorite;
        private string _referenceJugement;
        private StatutSaisieOpposition _statut = StatutSaisieOpposition.EnCours;
        private int _prioriteOrdre;
        private DateTime? _dateMainlevee;
        private string _referenceMainlevee;
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

        public TypeSaisieOpposition TypeSaisie
        {
            get => _typeSaisie;
            set
            {
                if (_typeSaisie != value)
                {
                    _typeSaisie = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(TypeLibelle));
                    OnPropertyChanged(nameof(PrioriteEffective));
                }
            }
        }

        public string NumeroDossier
        {
            get => _numeroDossier;
            set { if (_numeroDossier != value) { _numeroDossier = value; OnPropertyChanged(); } }
        }

        public string Creancier
        {
            get => _creancier;
            set { if (_creancier != value) { _creancier = value; OnPropertyChanged(); } }
        }

        public string Beneficiaire
        {
            get => _beneficiaire;
            set { if (_beneficiaire != value) { _beneficiaire = value; OnPropertyChanged(); } }
        }

        /// <summary>
        /// Montant total de la dette en FCFA. 0 si la créance est périodique et illimitée (ex: pension alimentaire mensuelle).
        /// </summary>
        public decimal MontantTotalDu
        {
            get => _montantTotalDu;
            set
            {
                if (_montantTotalDu != value)
                {
                    _montantTotalDu = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(SoldeRestantDu));
                    OnPropertyChanged(nameof(EstSolde));
                    OnPropertyChanged(nameof(EstIllimitee));
                    OnPropertyChanged(nameof(TauxProgression));
                }
            }
        }

        /// <summary>
        /// Montant mensuel ordonné ou convenu en FCFA.
        /// </summary>
        public decimal RetenueMensuelleFixee
        {
            get => _retenueMensuelleFixee;
            set { if (_retenueMensuelleFixee != value) { _retenueMensuelleFixee = value; OnPropertyChanged(); } }
        }

        /// <summary>
        /// Cumul des retenues déjà opérées et enregistrées en FCFA.
        /// </summary>
        public decimal TotalAmorti
        {
            get => _totalAmorti;
            set
            {
                if (_totalAmorti != value)
                {
                    _totalAmorti = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(SoldeRestantDu));
                    OnPropertyChanged(nameof(EstSolde));
                    OnPropertyChanged(nameof(TauxProgression));
                }
            }
        }

        public DateTime DateDebut
        {
            get => _dateDebut;
            set { if (_dateDebut != value) { _dateDebut = value; OnPropertyChanged(); } }
        }

        public DateTime? DateFin
        {
            get => _dateFin;
            set { if (_dateFin != value) { _dateFin = value; OnPropertyChanged(); } }
        }

        public DateTime? DateNotification
        {
            get => _dateNotification;
            set { if (_dateNotification != value) { _dateNotification = value; OnPropertyChanged(); } }
        }

        public string TribunalOuAutorite
        {
            get => _tribunalOuAutorite;
            set { if (_tribunalOuAutorite != value) { _tribunalOuAutorite = value; OnPropertyChanged(); } }
        }

        public string ReferenceJugement
        {
            get => _referenceJugement;
            set { if (_referenceJugement != value) { _referenceJugement = value; OnPropertyChanged(); } }
        }

        public StatutSaisieOpposition Statut
        {
            get => _statut;
            set
            {
                if (_statut != value)
                {
                    _statut = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(StatutLibelle));
                    OnPropertyChanged(nameof(EstActive));
                }
            }
        }

        /// <summary>
        /// Ordre explicite de priorité (1 = prioritaire). Si 0, la priorité par défaut du TypeSaisie s'applique.
        /// </summary>
        public int PrioriteOrdre
        {
            get => _prioriteOrdre;
            set
            {
                if (_prioriteOrdre != value)
                {
                    _prioriteOrdre = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(PrioriteEffective));
                }
            }
        }

        public DateTime? DateMainlevee
        {
            get => _dateMainlevee;
            set { if (_dateMainlevee != value) { _dateMainlevee = value; OnPropertyChanged(); } }
        }

        public string ReferenceMainlevee
        {
            get => _referenceMainlevee;
            set { if (_referenceMainlevee != value) { _referenceMainlevee = value; OnPropertyChanged(); } }
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

        #region Propriétés Dérivées et Métier

        /// <summary>
        /// Montant restant dû sur le principal de la dette.
        /// </summary>
        public decimal SoldeRestantDu => _montantTotalDu > 0 ? Math.Max(0m, _montantTotalDu - _totalAmorti) : 0m;

        /// <summary>
        /// Indique si la créance à terme a été totalement remboursée.
        /// </summary>
        public bool EstSolde => _montantTotalDu > 0 && _totalAmorti >= _montantTotalDu;

        /// <summary>
        /// Indique si la créance est de nature récurrente sans terme prédéfini (ex: pension alimentaire).
        /// </summary>
        public bool EstIllimitee => _montantTotalDu <= 0;

        /// <summary>
        /// Indique si la saisie est active et recevable pour une retenue mensuelle.
        /// </summary>
        public bool EstActive => _statut == StatutSaisieOpposition.EnCours && (!EstSolde || EstIllimitee);

        /// <summary>
        /// Rang effectif d'imputation légale.
        /// </summary>
        public int PrioriteEffective => _prioriteOrdre > 0 ? _prioriteOrdre : ((int)_typeSaisie + 1);

        public double TauxProgression => _montantTotalDu > 0 ? Math.Min(100.0, (double)(_totalAmorti / _montantTotalDu * 100m)) : 0.0;

        public string TypeLibelle
        {
            get
            {
                switch (_typeSaisie)
                {
                    case TypeSaisieOpposition.PensionAlimentaire:
                        return "Pension alimentaire (super-privilège)";
                    case TypeSaisieOpposition.AvisATiersDetenteur:
                        return "Avis à Tiers Détenteur (ATD / Impôts)";
                    case TypeSaisieOpposition.SaisieArretJudiciaire:
                        return "Saisie-arrêt judiciaire";
                    case TypeSaisieOpposition.CessionVolontaire:
                        return "Cession volontaire de salaire";
                    case TypeSaisieOpposition.AutreOpposition:
                        return "Opposition diverse";
                    default:
                        return _typeSaisie.ToString();
                }
            }
        }

        public string StatutLibelle
        {
            get
            {
                switch (_statut)
                {
                    case StatutSaisieOpposition.EnCours:
                        return "En cours";
                    case StatutSaisieOpposition.Suspendue:
                        return "Suspendue";
                    case StatutSaisieOpposition.Soldee:
                        return "Soldée";
                    case StatutSaisieOpposition.Mainlevee:
                        return "Mainlevée";
                    default:
                        return _statut.ToString();
                }
            }
        }

        #endregion

        public event PropertyChangedEventHandler PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    /// <summary>
    /// Enregistrement de la retenue effective d'une saisie sur la paie d'un mois donné.
    /// </summary>
    public class RetenueSaisieMensuelle : INotifyPropertyChanged
    {
        private int _id;
        private int _saisieOppositionId;
        private string _employeeId;
        private string _nomSalarie;
        private string _creancier;
        private TypeSaisieOpposition _typeSaisie;
        private int _annee;
        private int _mois;
        private decimal _montantRetenu;
        private decimal _baseNetCalcul;
        private decimal _quotiteDisponible;
        private int _rangPriorite;
        private DateTime _datePrelevement = DateTime.Today;
        private string _numeroChequeOuVirement;
        private StatutPaiementSaisie _statutPaiement = StatutPaiementSaisie.Retenu;
        private DateTime? _dateReversement;
        private string _commentaire;

        public int Id
        {
            get => _id;
            set { if (_id != value) { _id = value; OnPropertyChanged(); } }
        }

        public int SaisieOppositionId
        {
            get => _saisieOppositionId;
            set { if (_saisieOppositionId != value) { _saisieOppositionId = value; OnPropertyChanged(); } }
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

        public string Creancier
        {
            get => _creancier;
            set { if (_creancier != value) { _creancier = value; OnPropertyChanged(); } }
        }

        public TypeSaisieOpposition TypeSaisie
        {
            get => _typeSaisie;
            set { if (_typeSaisie != value) { _typeSaisie = value; OnPropertyChanged(); } }
        }

        public int Annee
        {
            get => _annee;
            set { if (_annee != value) { _annee = value; OnPropertyChanged(); } }
        }

        public int Mois
        {
            get => _mois;
            set { if (_mois != value) { _mois = value; OnPropertyChanged(); } }
        }

        public decimal MontantRetenu
        {
            get => _montantRetenu;
            set { if (_montantRetenu != value) { _montantRetenu = value; OnPropertyChanged(); } }
        }

        public decimal BaseNetCalcul
        {
            get => _baseNetCalcul;
            set { if (_baseNetCalcul != value) { _baseNetCalcul = value; OnPropertyChanged(); } }
        }

        public decimal QuotiteDisponible
        {
            get => _quotiteDisponible;
            set { if (_quotiteDisponible != value) { _quotiteDisponible = value; OnPropertyChanged(); } }
        }

        public int RangPriorite
        {
            get => _rangPriorite;
            set { if (_rangPriorite != value) { _rangPriorite = value; OnPropertyChanged(); } }
        }

        public DateTime DatePrelevement
        {
            get => _datePrelevement;
            set { if (_datePrelevement != value) { _datePrelevement = value; OnPropertyChanged(); } }
        }

        public string NumeroChequeOuVirement
        {
            get => _numeroChequeOuVirement;
            set { if (_numeroChequeOuVirement != value) { _numeroChequeOuVirement = value; OnPropertyChanged(); } }
        }

        public StatutPaiementSaisie StatutPaiement
        {
            get => _statutPaiement;
            set { if (_statutPaiement != value) { _statutPaiement = value; OnPropertyChanged(); } }
        }

        public DateTime? DateReversement
        {
            get => _dateReversement;
            set { if (_dateReversement != value) { _dateReversement = value; OnPropertyChanged(); } }
        }

        public string Commentaire
        {
            get => _commentaire;
            set { if (_commentaire != value) { _commentaire = value; OnPropertyChanged(); } }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    /// <summary>
    /// Détail d'une tranche de calcul de la quotité saisissable.
    /// </summary>
    public class DetailTrancheQuotite
    {
        public int NumeroTranche { get; set; }
        public string LibelleTranche { get; set; }
        public decimal PlafondBas { get; set; }
        public decimal PlafondHaut { get; set; }
        public decimal FractionQuotite { get; set; }
        public string RatioAffichage { get; set; }
        public decimal AssietteTranche { get; set; }
        public decimal MontantSaisissableTranche { get; set; }
    }

    /// <summary>
    /// Ligne de répartition d'une saisie dans le calcul mensuel de la paie.
    /// </summary>
    public class LigneRepartitionSaisie
    {
        public int SaisieOppositionId { get; set; }
        public string Creancier { get; set; }
        public string NumeroDossier { get; set; }
        public TypeSaisieOpposition TypeSaisie { get; set; }
        public int RangPriorite { get; set; }
        public decimal MontantReclameMensuel { get; set; }
        public decimal MontantRetenu { get; set; }
        public decimal SoldeRestantAvant { get; set; }
        public decimal SoldeRestantApres { get; set; }
        public bool EstCompletementAmortie { get; set; }
        public string MotifPlafonnementOuNote { get; set; }
    }

    /// <summary>
    /// Résultat consolidé du calcul légal de la quotité saisissable et de la répartition entre créanciers.
    /// </summary>
    public class ResultatCalculQuotite
    {
        public decimal SalaireNetReference { get; set; }
        public decimal SMIG { get; set; } = 45000m;

        /// <summary>
        /// Fraction du salaire insaisissable (avec respect du SMIG insaisissable).
        /// </summary>
        public decimal FractionInsaisissable { get; set; }

        /// <summary>
        /// Quotité maximale globale légalement saisissable selon le barème par tranches (Code du travail Burkinabè).
        /// </summary>
        public decimal QuotiteSaisissableLegale { get; set; }

        /// <summary>
        /// Montant maximal disponible pour une pension alimentaire (Salaire Net - SMIG).
        /// </summary>
        public decimal MontantDisponiblePensionAlimentaire { get; set; }

        /// <summary>
        /// Montant total retenu pour la ou les pensions alimentaires (super-privilège).
        /// </summary>
        public decimal RetenuePensionAlimentaire { get; set; }

        /// <summary>
        /// Quotité légale restant utilisable pour les créances ordinaires (ATD, saisies, cessions)
        /// après déduction de la retenue pour pension alimentaire.
        /// </summary>
        public decimal QuotiteOrdinaireRestante { get; set; }

        /// <summary>
        /// Montant total des retenues ordinaires (ATD + saisies + cessions).
        /// </summary>
        public decimal RetenuesOrdinairesTotal { get; set; }

        /// <summary>
        /// Montant total retenu toutes saisies confondues.
        /// </summary>
        public decimal RetenueGlobaleEffectuee => RetenuePensionAlimentaire + RetenuesOrdinairesTotal;

        /// <summary>
        /// Salaire net final restant à payer au salarié après toutes les saisies.
        /// </summary>
        public decimal NetRestantApresSaisies => Math.Max(0m, SalaireNetReference - RetenueGlobaleEffectuee);

        /// <summary>
        /// Détail du calcul par tranches légales.
        /// </summary>
        public List<DetailTrancheQuotite> Tranches { get; set; } = new List<DetailTrancheQuotite>();

        /// <summary>
        /// Répartition individuelle par créance/dossier.
        /// </summary>
        public List<LigneRepartitionSaisie> Repartitions { get; set; } = new List<LigneRepartitionSaisie>();
    }
}
