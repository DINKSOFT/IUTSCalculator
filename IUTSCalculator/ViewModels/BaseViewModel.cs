using IUTSCalculator.Services;
using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;

namespace IUTSCalculator.ViewModels
{
    /// <summary>
    /// Classe de base pour tous les ViewModels, implémentant INotifyPropertyChanged
    /// </summary>
    public abstract class BaseViewModel : INotifyPropertyChanged
    {
        private bool _isActive;
        private string _title;
        private bool _isBusy;
        private string _statusMessage;
        //public bool CanEditData => UserSession.Instance.CanEdit;
        //public bool IsAdmin => UserSession.Instance.IsAdmin;
        //public bool IsReadOnly => UserSession.Instance.IsReadOnly;

        /// <summary>
        /// Indique si ce ViewModel est actuellement actif/affiché
        /// </summary>
        public bool IsActive
        {
            get => _isActive;
            set
            {
                if (SetProperty(ref _isActive, value))
                {
                    OnIsActiveChanged();
                }
            }
        }

        /// <summary>
        /// Titre descriptif de la vue
        /// </summary>
        public string Title
        {
            get => _title;
            set => SetProperty(ref _title, value);
        }

        /// <summary>
        /// Indique si le ViewModel est occupé à traiter une opération
        /// </summary>
        public bool IsBusy
        {
            get => _isBusy;
            set
            {
                if (SetProperty(ref _isBusy, value))
                {
                    // Mettre à jour les commandes qui dépendent de IsBusy
                    CommandManager.InvalidateRequerySuggested();
                }
            }
        }

        /// <summary>
        /// Message d'état affiché à l'utilisateur
        /// </summary>
        public string StatusMessage
        {
            get => _statusMessage;
            set
            {
                string translated = Services.GardeLectureSeule.Traduire(value);
                if (translated != value)
                {
                    NotificationService.Instance.Warning(translated);
                }
                SetProperty(ref _statusMessage, translated);
            }
        }

        /// <summary>
        /// Constructeur
        /// </summary>
        protected BaseViewModel()
        {
            // Par défaut, le titre est basé sur le nom de la classe
            Title = GetType().Name.Replace("ViewModel", "");
            StatusMessage = "Prêt";
        }

        /// <summary>
        /// Méthode appelée quand la propriété IsActive change
        /// Peut être surchargée par les classes dérivées pour effectuer des opérations
        /// lorsque le ViewModel devient actif ou inactif
        /// </summary>
        protected virtual void OnIsActiveChanged()
        {
            // Par défaut, ne fait rien
            // Les classes dérivées peuvent surcharger cette méthode pour réagir 
            // lorsque le ViewModel devient actif/inactif
        }

        /// <summary>
        /// Événement déclenché lorsqu'une propriété change
        /// </summary>
        public event PropertyChangedEventHandler PropertyChanged;

        /// <summary>
        /// Déclenche l'événement PropertyChanged
        /// </summary>
        /// <param name="propertyName">Nom de la propriété modifiée</param>
        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        /// <summary>
        /// Méthode utilitaire pour mettre à jour un champ et déclencher PropertyChanged si nécessaire
        /// </summary>
        /// <typeparam name="T">Type du champ</typeparam>
        /// <param name="field">Référence du champ à mettre à jour</param>
        /// <param name="value">Nouvelle valeur</param>
        /// <param name="propertyName">Nom de la propriété</param>
        /// <returns>True si la valeur a été modifiée, False sinon</returns>
        protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string propertyName = null)
        {
            // Si l'ancienne valeur est égale à la nouvelle, ne rien faire
            if (Equals(field, value)) return false;

            // Mettre à jour la valeur
            field = value;

            // Déclencher l'événement PropertyChanged
            OnPropertyChanged(propertyName);

            return true;
        }
    }
}