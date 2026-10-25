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
    /// Les 4 catégories légales de majoration (Code du Travail du Burkina Faso & CCIP) :
    ///   • 15% (taux 115%) : 8 premières heures supplémentaires de jour en semaine (41e à 48e heure hebdo).
    ///   • 30% (taux 130%) : heures supplémentaires de jour en semaine au-delà de la 48e heure.
    ///   • 50% (taux 150%) : heures supplémentaires de nuit en semaine (22h à 5h) OU de jour dimanches/jours fériés.
    ///   • 100% (taux 200%) : heures supplémentaires de nuit dimanches/jours fériés.
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

        private decimal _tauxHoraireBase;
        private decimal _montant15;
        private decimal _montant30;
        private decimal _montant50;
        private decimal _montant100;
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

        public decimal TotalHeures => _heures15 + _heures30 + _heures50 + _heures100;

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
        /// Recalcule le taux horaire de base et la valorisation monétaire des 4 tranches d'heures supplémentaires.
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

            _montant15 = Math.Round(_heures15 * _tauxHoraireBase * 1.15m, 0);
            _montant30 = Math.Round(_heures30 * _tauxHoraireBase * 1.30m, 0);
            _montant50 = Math.Round(_heures50 * _tauxHoraireBase * 1.50m, 0);
            _montant100 = Math.Round(_heures100 * _tauxHoraireBase * 2.00m, 0);

            _montantTotal = _montant15 + _montant30 + _montant50 + _montant100;

            OnPropertyChanged(nameof(TauxHoraireBase));
            OnPropertyChanged(nameof(Montant15));
            OnPropertyChanged(nameof(Montant30));
            OnPropertyChanged(nameof(Montant50));
            OnPropertyChanged(nameof(Montant100));
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
