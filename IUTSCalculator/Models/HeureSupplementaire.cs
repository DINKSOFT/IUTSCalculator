using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using IUTSCalculator.Services;

namespace IUTSCalculator.Models
{
    /// <summary>
    /// Décompte et valorisation des heures supplémentaires d'un salarié pour un mois donné (module PAI-03).
    ///
    /// Cinq tranches de majoration, dont les TAUX viennent du barème daté
    /// <see cref="BaremeHeuresSup"/> applicable au mois (RH-08, octobre 2026) :
    ///   • <see cref="Heures15"/>  : tranche 1 — 8 premières heures supplémentaires de la semaine (41e à 48e) ;
    ///   • <see cref="Heures30"/>  : tranche 2 — heures supplémentaires au-delà de la 48e heure ;
    ///   • <see cref="Heures50"/>  : heures supplémentaires de nuit d'un jour ouvrable ;
    ///   • <see cref="Heures60"/>  : heures supplémentaires de jour, dimanche ou jour férié ;
    ///   • <see cref="Heures100"/> : heures supplémentaires de nuit, dimanche ou jour férié.
    ///
    /// Les noms des propriétés (et des colonnes SQLite) sont ceux de PAI-03 et ne disent
    /// plus le taux : Heures30 est majoré au taux de la tranche 2 du barème (35 % depuis
    /// octobre 2026), Heures100 au taux férié de nuit (120 %). Ils sont conservés pour ne
    /// pas migrer les données saisies. Sans barème appliqué, les taux par défaut sont
    /// exactement les anciens littéraux (15/30/50/100, dimanche de jour à 50 %) : un mois
    /// antérieur recalculé reste identique au centime.
    /// </summary>
    public class HeureSupplementaire : INotifyPropertyChanged
    {
        public const decimal HeuresMensuellesReference = 173.33333333333333333333333333m;

        private long _id;
        private string _employeeId;
        private int _annee;
        private int _mois;

        private string _matricule;
        private string _nomComplet;
        private decimal _salaireDeBase;

        private decimal _heures15;
        private decimal _heures30;
        private decimal _heures50;
        private decimal _heures100;
        private decimal _heures60;

        // Majorations en proportion. Valeurs par défaut = anciens littéraux de PAI-03.
        private decimal _tauxTranche1 = 0.15m;
        private decimal _tauxTranche2 = 0.30m;
        private decimal _tauxNuit = 0.50m;
        private decimal _tauxFerieJour = 0.50m;
        private decimal _tauxFerieNuit = 1.00m;

        private decimal _tauxHoraireBase;
        private decimal _montant15;
        private decimal _montant30;
        private decimal _montant50;
        private decimal _montant100;
        private decimal _montant60;
        private decimal _montantTotal;

        private string _commentaire;
        private DateTime _dateCreation;
        private DateTime _dateModification;
        private bool _estModifie;

        public long Id
        {
            get => _id;
            set { _id = value; OnPropertyChanged(); }
        }

        public string EmployeeId
        {
            get => _employeeId;
            set { _employeeId = value; OnPropertyChanged(); }
        }

        public int Annee
        {
            get => _annee;
            set { _annee = value; OnPropertyChanged(); }
        }

        public int Mois
        {
            get => _mois;
            set { _mois = value; OnPropertyChanged(); }
        }

        public string Matricule
        {
            get => _matricule;
            set { _matricule = value; OnPropertyChanged(); }
        }

        public string NomComplet
        {
            get => _nomComplet;
            set { _nomComplet = value; OnPropertyChanged(); }
        }

        public decimal SalaireDeBase
        {
            get => _salaireDeBase;
            set
            {
                if (_salaireDeBase != value)
                {
                    _salaireDeBase = value;
                    OnPropertyChanged();
                    RecalculerMontants();
                }
            }
        }

        public decimal Heures15
        {
            get => _heures15;
            set
            {
                if (_heures15 != value)
                {
                    _heures15 = value < 0m ? 0m : value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(TotalHeures));
                    RecalculerMontants();
                    EstModifie = true;
                }
            }
        }

        public decimal Heures30
        {
            get => _heures30;
            set
            {
                if (_heures30 != value)
                {
                    _heures30 = value < 0m ? 0m : value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(TotalHeures));
                    RecalculerMontants();
                    EstModifie = true;
                }
            }
        }

        public decimal Heures50
        {
            get => _heures50;
            set
            {
                if (_heures50 != value)
                {
                    _heures50 = value < 0m ? 0m : value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(TotalHeures));
                    RecalculerMontants();
                    EstModifie = true;
                }
            }
        }

        public decimal Heures100
        {
            get => _heures100;
            set
            {
                if (_heures100 != value)
                {
                    _heures100 = value < 0m ? 0m : value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(TotalHeures));
                    RecalculerMontants();
                    EstModifie = true;
                }
            }
        }

        /// <summary>Heures supplémentaires de jour, dimanche ou jour férié (tranche ajoutée par RH-08).</summary>
        public decimal Heures60
        {
            get => _heures60;
            set
            {
                if (_heures60 != value)
                {
                    _heures60 = value < 0m ? 0m : value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(TotalHeures));
                    RecalculerMontants();
                    EstModifie = true;
                }
            }
        }

        public decimal TotalHeures => _heures15 + _heures30 + _heures50 + _heures60 + _heures100;

        // ── Taux du barème applicable ────────────────────────────────────────

        public decimal TauxTranche1 => _tauxTranche1;
        public decimal TauxTranche2 => _tauxTranche2;
        public decimal TauxNuit => _tauxNuit;
        public decimal TauxFerieJour => _tauxFerieJour;
        public decimal TauxFerieNuit => _tauxFerieNuit;

        /// <summary>
        /// Applique les majorations d'un barème daté puis revalorise. Un barème nul laisse
        /// les taux en place (par défaut ceux de PAI-03). <paramref name="revaloriser"/> à
        /// faux garde les montants tels qu'enregistrés (lecture d'une ligne en base).
        /// </summary>
        public void AppliquerBareme(BaremeHeuresSup bareme, bool revaloriser = true)
        {
            if (bareme != null)
            {
                _tauxTranche1 = bareme.TauxTranche1;
                _tauxTranche2 = bareme.TauxTranche2;
                _tauxNuit = bareme.TauxNuit;
                _tauxFerieJour = bareme.TauxFerieJour;
                _tauxFerieNuit = bareme.TauxFerieNuit;

                OnPropertyChanged(nameof(TauxTranche1));
                OnPropertyChanged(nameof(TauxTranche2));
                OnPropertyChanged(nameof(TauxNuit));
                OnPropertyChanged(nameof(TauxFerieJour));
                OnPropertyChanged(nameof(TauxFerieNuit));
            }

            if (revaloriser)
                RecalculerMontants();
        }

        public decimal TauxHoraireBase
        {
            get => _tauxHoraireBase;
            set { _tauxHoraireBase = value; OnPropertyChanged(); }
        }

        public decimal Montant15
        {
            get => _montant15;
            set { _montant15 = value; OnPropertyChanged(); }
        }

        public decimal Montant30
        {
            get => _montant30;
            set { _montant30 = value; OnPropertyChanged(); }
        }

        public decimal Montant50
        {
            get => _montant50;
            set { _montant50 = value; OnPropertyChanged(); }
        }

        public decimal Montant100
        {
            get => _montant100;
            set { _montant100 = value; OnPropertyChanged(); }
        }

        public decimal Montant60
        {
            get => _montant60;
            set { _montant60 = value; OnPropertyChanged(); }
        }

        public decimal MontantTotal
        {
            get => _montantTotal;
            set { _montantTotal = value; OnPropertyChanged(); }
        }

        public string Commentaire
        {
            get => _commentaire;
            set
            {
                if (_commentaire != value)
                {
                    _commentaire = value;
                    OnPropertyChanged();
                    EstModifie = true;
                }
            }
        }

        public DateTime DateCreation
        {
            get => _dateCreation;
            set { _dateCreation = value; OnPropertyChanged(); }
        }

        public DateTime DateModification
        {
            get => _dateModification;
            set { _dateModification = value; OnPropertyChanged(); }
        }

        public bool EstModifie
        {
            get => _estModifie;
            set { _estModifie = value; OnPropertyChanged(); }
        }

        /// <summary>
        /// Recalcule le taux horaire de base et la valorisation monétaire des 5 tranches d'heures supplémentaires.
        /// </summary>
        public void RecalculerMontants()
        {
            if (_salaireDeBase > 0m)
            {
                _tauxHoraireBase = Math.Round(_salaireDeBase / HeuresMensuellesReference, 2);
            }
            else
            {
                _tauxHoraireBase = 0m;
            }

            _montant15 = Math.Round(_heures15 * _tauxHoraireBase * (1m + _tauxTranche1), 0);
            _montant30 = Math.Round(_heures30 * _tauxHoraireBase * (1m + _tauxTranche2), 0);
            _montant50 = Math.Round(_heures50 * _tauxHoraireBase * (1m + _tauxNuit), 0);
            _montant60 = Math.Round(_heures60 * _tauxHoraireBase * (1m + _tauxFerieJour), 0);
            _montant100 = Math.Round(_heures100 * _tauxHoraireBase * (1m + _tauxFerieNuit), 0);

            _montantTotal = _montant15 + _montant30 + _montant50 + _montant60 + _montant100;

            OnPropertyChanged(nameof(TauxHoraireBase));
            OnPropertyChanged(nameof(Montant15));
            OnPropertyChanged(nameof(Montant30));
            OnPropertyChanged(nameof(Montant50));
            OnPropertyChanged(nameof(Montant100));
            OnPropertyChanged(nameof(Montant60));
            OnPropertyChanged(nameof(MontantTotal));
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    /// <summary>
    /// Ce que les heures supplémentaires du mois apportent au bulletin d'un salarié (module PAI-03).
    /// </summary>
    public sealed class ApportHeuresSupplementaires
    {
        public static readonly ApportHeuresSupplementaires Vide = new ApportHeuresSupplementaires(new List<LigneApport>(), 0m, 0m);

        public IReadOnlyList<LigneApport> Lignes { get; }
        public decimal MontantTotal { get; }
        public decimal TotalHeures { get; }

        public bool EstVide => Lignes == null || Lignes.Count == 0 || MontantTotal == 0m;

        public ApportHeuresSupplementaires(List<LigneApport> lignes, decimal montantTotal, decimal totalHeures)
        {
            Lignes = lignes ?? new List<LigneApport>();
            MontantTotal = montantTotal;
            TotalHeures = totalHeures;
        }
    }
}
