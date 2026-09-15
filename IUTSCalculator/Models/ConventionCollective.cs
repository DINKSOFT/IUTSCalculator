using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace IUTSCalculator.Models
{
    /// <summary>
    /// Base de calcul légale de la prime d'ancienneté conventionnelle (module SOC-05).
    /// </summary>
    public enum BaseCalculAnciennete
    {
        /// <summary>
        /// Calculée sur le salaire minimum garanti de la catégorie du travailleur (règle standard CCI-BF).
        /// </summary>
        [Description("Salaire minimum conventionnel de la catégorie")]
        MinimumConventionnel = 0,

        /// <summary>
        /// Calculée sur le salaire de base contractuel réel du travailleur (usage ou convention plus favorable).
        /// </summary>
        [Description("Salaire de base contractuel réel")]
        SalaireDeBaseReel = 1
    }

    /// <summary>
    /// Représente une convention collective applicable à l'entreprise ou à un groupe d'employés.
    /// </summary>
    public class ConventionCollective : INotifyPropertyChanged
    {
        private int _id;
        private string _code;
        private string _libelle;
        private string _description;
        private BaseCalculAnciennete _baseCalculAnciennete;
        private bool _estActive = true;
        private DateTime _dateCreation = DateTime.Now;
        private DateTime _dateModification = DateTime.Now;

        public int Id
        {
            get => _id;
            set => SetProperty(ref _id, value);
        }

        /// <summary>
        /// Code unique d'identification (ex. "CCI-BF", "BANQUES", "COMMERCE", "MINES").
        /// </summary>
        public string Code
        {
            get => _code;
            set => SetProperty(ref _code, value);
        }

        /// <summary>
        /// Libellé complet de la convention.
        /// </summary>
        public string Libelle
        {
            get => _libelle;
            set => SetProperty(ref _libelle, value);
        }

        public string Description
        {
            get => _description;
            set => SetProperty(ref _description, value);
        }

        /// <summary>
        /// Base légale ou conventionnelle de calcul de la prime d'ancienneté.
        /// </summary>
        public BaseCalculAnciennete BaseCalculAnciennete
        {
            get => _baseCalculAnciennete;
            set => SetProperty(ref _baseCalculAnciennete, value);
        }

        public bool EstActive
        {
            get => _estActive;
            set => SetProperty(ref _estActive, value);
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

        /// <summary>
        /// Grille salariale (catégories et échelons) rattachée à cette convention.
        /// </summary>
        public List<GrilleSalaire> Grilles { get; set; } = new List<GrilleSalaire>();

        /// <summary>
        /// Tranches de majoration d'ancienneté rattachées à cette convention.
        /// </summary>
        public List<TrancheAnciennete> TranchesAnciennete { get; set; } = new List<TrancheAnciennete>();

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

        public override string ToString() => string.IsNullOrWhiteSpace(Code) ? Libelle : $"{Code} — {Libelle}";
    }
}
