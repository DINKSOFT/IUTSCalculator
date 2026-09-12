using System.ComponentModel;
using System.Runtime.CompilerServices;
using IUTSCalculator.Models;

namespace IUTSCalculator.ViewModels
{
    /// <summary>
    /// Enveloppe légère autour d'un <see cref="Employee"/> pour lui ajouter un état de sélection
    /// (case à cocher) dans les écrans de traitement en lot, sans polluer le modèle de domaine.
    /// </summary>
    public class SelectableEmployee : INotifyPropertyChanged
    {
        private bool _isSelected;

        public Employee Employee { get; }

        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isSelected != value)
                {
                    _isSelected = value;
                    OnPropertyChanged();
                }
            }
        }

        public string NomComplet => $"{Employee?.LastName} {Employee?.FirstName}".Trim();
        public string Fonction => Employee?.Fonction;
        public string Categorie => Employee?.Category;

        public SelectableEmployee(Employee employee)
        {
            Employee = employee;
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
