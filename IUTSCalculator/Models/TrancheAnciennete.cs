using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace IUTSCalculator.Models
{
    /// <summary>
    /// Représente une tranche de majoration d'ancienneté conventionnelle (module SOC-05).
    /// </summary>
    public class TrancheAnciennete : INotifyPropertyChanged
    {
        private int _id;
        private int _conventionId;
        private int _anneesMin;
        private int? _anneesMax;
        private decimal _tauxPourcentage;
        private int _ordre;

        public int Id
        {
            get => _id;
            set => SetProperty(ref _id, value);
        }

        public int ConventionId
        {
            get => _conventionId;
            set => SetProperty(ref _conventionId, value);
        }

        /// <summary>
        /// Seuil minimum d'années d'ancienneté révolues (inclus).
        /// </summary>
        public int AnneesMin
        {
            get => _anneesMin;
            set
            {
                if (SetProperty(ref _anneesMin, value))
                    OnPropertyChanged(nameof(LibelleTranche));
            }
        }

        /// <summary>
        /// Seuil maximum d'années d'ancienneté (exclus), ou null si pas de limite supérieure.
        /// </summary>
        public int? AnneesMax
        {
            get => _anneesMax;
            set
            {
                if (SetProperty(ref _anneesMax, value))
                    OnPropertyChanged(nameof(LibelleTranche));
            }
        }

        /// <summary>
        /// Taux de majoration en pourcentage (ex. 5.0 pour 5%).
        /// </summary>
        public decimal TauxPourcentage
        {
            get => _tauxPourcentage;
            set => SetProperty(ref _tauxPourcentage, value);
        }

        public int Ordre
        {
            get => _ordre;
            set => SetProperty(ref _ordre, value);
        }

        /// <summary>
        /// Libellé affichable de la tranche (ex. "De 2 à 3 ans", "25 ans et plus").
        /// </summary>
        public string LibelleTranche
        {
            get
            {
                if (!AnneesMax.HasValue)
                    return $"{AnneesMin} ans et plus";
                if (AnneesMax.Value == AnneesMin + 1)
                    return $"{AnneesMin} an(s) révolu(s)";
                return $"De {AnneesMin} à {AnneesMax.Value} ans";
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string propertyName = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value)) return false;
            field = value;
            OnPropertyChanged(propertyName);
            return true;
        }

        public override string ToString() => $"{LibelleTranche} : {TauxPourcentage:0.##}%";
    }
}
