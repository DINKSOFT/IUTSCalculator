using System;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;

namespace IUTSCalculator.Models
{
    /// <summary>État d'une période de paie.</summary>
    public enum StatutPeriode
    {
        /// <summary>Les bulletins de la période peuvent être calculés et recalculés.</summary>
        Ouverte = 0,
        /// <summary>Les totaux sont arrêtés. Plus aucun calcul ne peut être enregistré.</summary>
        Cloturee = 1
    }

    /// <summary>
    /// Un mois de paie (module PAI-01).
    ///
    /// Jusqu'ici un calcul était simplement daté : rien ne disait qu'un mois était
    /// terminé, rien n'empêchait de recalculer en novembre un bulletin de mars déjà
    /// déclaré à la CNSS et à la DGI, et rien ne conservait les totaux qui avaient servi
    /// à ces déclarations. La période de paie est la notion qui manquait — et c'est aussi
    /// celle dont dépendent les déclarations (DEC-01 à DEC-05), le journal comptable
    /// (CPT-01) et les états annuels.
    ///
    /// La clôture arrête les totaux et les fige dans la ligne de la période. C'est cette
    /// photographie, et non un recalcul ultérieur, qui fait foi : elle reste exacte même
    /// si un salaire est corrigé l'année suivante.
    /// </summary>
    public class PeriodePaie : INotifyPropertyChanged
    {
        private int _id;
        private int _annee;
        private int _mois;
        private StatutPeriode _statut;
        private DateTime _dateOuverture;
        private DateTime? _dateCloture;
        private string _clotureePar;
        private string _commentaire;

        private int _nombreBulletins;
        private decimal _totalBrut;
        private decimal _totalNetAPayer;
        private decimal _totalIuts;
        private decimal _totalCnssSalarie;
        private decimal _totalCnssPatronal;
        private decimal _totalTaxePatronale;
        private decimal _totalMasseSalariale;

        // ── Identité ─────────────────────────────────────────────────────────

        public int Id
        {
            get { return _id; }
            set { if (_id != value) { _id = value; OnPropertyChanged(); } }
        }

        public int Annee
        {
            get { return _annee; }
            set
            {
                if (_annee != value)
                {
                    _annee = value;
                    OnPropertyChanged();
                    NotifierLibelles();
                }
            }
        }

        /// <summary>Mois de 1 à 12.</summary>
        public int Mois
        {
            get { return _mois; }
            set
            {
                if (_mois != value)
                {
                    _mois = value;
                    OnPropertyChanged();
                    NotifierLibelles();
                }
            }
        }

        public StatutPeriode Statut
        {
            get { return _statut; }
            set
            {
                if (_statut != value)
                {
                    _statut = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(StatutLibelle));
                    OnPropertyChanged(nameof(EstOuverte));
                    OnPropertyChanged(nameof(EstCloturee));
                }
            }
        }

        public DateTime DateOuverture
        {
            get { return _dateOuverture; }
            set { if (_dateOuverture != value) { _dateOuverture = value; OnPropertyChanged(); } }
        }

        public DateTime? DateCloture
        {
            get { return _dateCloture; }
            set
            {
                if (_dateCloture != value)
                {
                    _dateCloture = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(DateClotureLibelle));
                }
            }
        }

        /// <summary>Utilisateur ayant prononcé la clôture.</summary>
        public string ClotureePar
        {
            get { return _clotureePar; }
            set { if (_clotureePar != value) { _clotureePar = value; OnPropertyChanged(); } }
        }

        public string Commentaire
        {
            get { return _commentaire; }
            set { if (_commentaire != value) { _commentaire = value; OnPropertyChanged(); } }
        }

        // ── Totaux arrêtés à la clôture ──────────────────────────────────────

        public int NombreBulletins
        {
            get { return _nombreBulletins; }
            set { if (_nombreBulletins != value) { _nombreBulletins = value; OnPropertyChanged(); } }
        }

        public decimal TotalBrut
        {
            get { return _totalBrut; }
            set
            {
                if (_totalBrut != value)
                {
                    _totalBrut = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(TotalChargesPatronales));
                    OnPropertyChanged(nameof(CoutTotalEmployeur));
                }
            }
        }

        public decimal TotalNetAPayer
        {
            get { return _totalNetAPayer; }
            set { if (_totalNetAPayer != value) { _totalNetAPayer = value; OnPropertyChanged(); } }
        }

        public decimal TotalIuts
        {
            get { return _totalIuts; }
            set { if (_totalIuts != value) { _totalIuts = value; OnPropertyChanged(); } }
        }

        /// <summary>
        /// Cotisation CNSS totale de la période : part salariale ET part patronale.
        /// C'est le montant versé à la Caisse, celui du bordereau — la part patronale
        /// seule ne dit qu'une moitié de ce qui est dû.
        /// </summary>
        public decimal TotalCnss
        {
            get { return TotalCnssSalarie + TotalCnssPatronal; }
        }

        public decimal TotalCnssSalarie
        {
            get { return _totalCnssSalarie; }
            set
            {
                if (_totalCnssSalarie != value)
                {
                    _totalCnssSalarie = value;
                    OnPropertyChanged();
                    OnPropertyChanged("TotalCnss");
                }
            }
        }

        public decimal TotalCnssPatronal
        {
            get { return _totalCnssPatronal; }
            set
            {
                if (_totalCnssPatronal != value)
                {
                    _totalCnssPatronal = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(TotalCnss));
                    OnPropertyChanged(nameof(TotalChargesPatronales));
                    OnPropertyChanged(nameof(CoutTotalEmployeur));
                }
            }
        }

        public decimal TotalTaxePatronale
        {
            get { return _totalTaxePatronale; }
            set
            {
                if (_totalTaxePatronale != value)
                {
                    _totalTaxePatronale = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(TotalChargesPatronales));
                    OnPropertyChanged(nameof(CoutTotalEmployeur));
                }
            }
        }

        public decimal TotalMasseSalariale
        {
            get { return _totalMasseSalariale; }
            set { if (_totalMasseSalariale != value) { _totalMasseSalariale = value; OnPropertyChanged(); } }
        }

        public decimal TotalChargesPatronales
        {
            get { return TotalCnssPatronal + TotalTaxePatronale; }
        }

        /// <summary>Brut versé plus charges supportées par l'employeur.</summary>
        public decimal CoutTotalEmployeur
        {
            get { return TotalBrut + TotalChargesPatronales; }
        }

        // ── Dérivés ──────────────────────────────────────────────────────────

        public bool EstOuverte
        {
            get { return Statut == StatutPeriode.Ouverte; }
        }

        public bool EstCloturee
        {
            get { return Statut == StatutPeriode.Cloturee; }
        }

        public string StatutLibelle
        {
            get { return EstCloturee ? "Clôturée" : "Ouverte"; }
        }

        /// <summary>Premier jour de la période.</summary>
        public DateTime PremierJour
        {
            get { return new DateTime(Annee, Mois, 1); }
        }

        /// <summary>Dernier instant de la période, borne haute des requêtes de calcul.</summary>
        public DateTime DernierJour
        {
            get { return PremierJour.AddMonths(1).AddTicks(-1); }
        }

        /// <summary>« Mars 2026 », avec la majuscule initiale attendue en français.</summary>
        public string Designation
        {
            get
            {
                if (Mois < 1 || Mois > 12) return string.Format("{0}", Annee);

                var culture = CultureInfo.GetCultureInfo("fr-FR");
                string nom = culture.DateTimeFormat.GetMonthName(Mois);
                if (nom.Length > 0)
                    nom = char.ToUpper(nom[0], culture) + nom.Substring(1);

                return string.Format("{0} {1}", nom, Annee);
            }
        }

        /// <summary>« 2026-03 », pour les tris et les noms de fichiers d'export.</summary>
        public string Code
        {
            get { return string.Format(CultureInfo.InvariantCulture, "{0:D4}-{1:D2}", Annee, Mois); }
        }

        public string DateClotureLibelle
        {
            get
            {
                return DateCloture.HasValue
                    ? string.Format("Clôturée le {0:dd/MM/yyyy}", DateCloture.Value)
                    : "—";
            }
        }

        public PeriodePaie()
        {
            _dateOuverture = DateTime.Now;
            _statut = StatutPeriode.Ouverte;
        }

        public PeriodePaie Clone()
        {
            return new PeriodePaie
            {
                Id = Id,
                Annee = Annee,
                Mois = Mois,
                Statut = Statut,
                DateOuverture = DateOuverture,
                DateCloture = DateCloture,
                ClotureePar = ClotureePar,
                Commentaire = Commentaire,
                NombreBulletins = NombreBulletins,
                TotalBrut = TotalBrut,
                TotalNetAPayer = TotalNetAPayer,
                TotalIuts = TotalIuts,
                TotalCnssSalarie = TotalCnssSalarie,
                TotalCnssPatronal = TotalCnssPatronal,
                TotalTaxePatronale = TotalTaxePatronale,
                TotalMasseSalariale = TotalMasseSalariale
            };
        }

        private void NotifierLibelles()
        {
            OnPropertyChanged(nameof(Designation));
            OnPropertyChanged(nameof(Code));
            OnPropertyChanged(nameof(PremierJour));
            OnPropertyChanged(nameof(DernierJour));
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
