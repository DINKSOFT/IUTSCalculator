using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace IUTSCalculator.Models
{
    /// <summary>
    /// Représente une ligne de classification professionnelle (Catégorie, Échelon)
    /// et le salaire de base minimum conventionnel garanti associé (module SOC-05).
    /// </summary>
    public class GrilleSalaire : INotifyPropertyChanged
    {
        private int _id;
        private int _conventionId;
        private string _categorie;
        private string _echelon;
        private decimal _salaireMinimum;
        private int _ordre;
        private string _description;

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
        /// Catégorie conventionnelle (ex. "1ère Catégorie", "2ème Catégorie", "Agents de Maîtrise", "Cadres Moyens", "Cadres Supérieurs").
        /// </summary>
        public string Categorie
        {
            get => _categorie;
            set
            {
                if (SetProperty(ref _categorie, value))
                    OnPropertyChanged(nameof(ClassificationComplete));
            }
        }

        /// <summary>
        /// Échelon (ex. "A", "B", "C", "1", "2").
        /// </summary>
        public string Echelon
        {
            get => _echelon;
            set
            {
                if (SetProperty(ref _echelon, value))
                    OnPropertyChanged(nameof(ClassificationComplete));
            }
        }

        /// <summary>
        /// Salaire de base minimum conventionnel garanti en FCFA.
        /// </summary>
        public decimal SalaireMinimum
        {
            get => _salaireMinimum;
            set => SetProperty(ref _salaireMinimum, value);
        }

        public int Ordre
        {
            get => _ordre;
            set => SetProperty(ref _ordre, value);
        }

        public string Description
        {
            get => _description;
            set => SetProperty(ref _description, value);
        }

        /// <summary>
        /// Libellé combiné catégorie + échelon (ex. "1ère Catégorie — Échelon A").
        /// </summary>
        public string ClassificationComplete => $"{Categorie} — Échelon {Echelon}";

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

        public override string ToString() => $"{ClassificationComplete} ({SalaireMinimum:N0} FCFA)";
    }
}
