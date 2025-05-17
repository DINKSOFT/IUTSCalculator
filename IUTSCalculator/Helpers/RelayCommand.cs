using System;
using System.Windows.Input;

namespace IUTSCalculator.Helpers
{
    /// <summary>
    /// Une implémentation de l'interface ICommand qui permet de lier facilement
    /// des méthodes aux commandes dans le modèle MVVM (Model-View-ViewModel).
    /// </summary>
    public class RelayCommand : ICommand
    {
        private readonly Action _execute;
        private readonly Func<bool> _canExecute;

        /// <summary>
        /// Initialise une nouvelle instance de la classe RelayCommand qui peut toujours être exécutée.
        /// </summary>
        /// <param name="execute">La méthode à exécuter lorsque la commande est invoquée.</param>
        public RelayCommand(Action execute) : this(execute, null)
        {
        }

        /// <summary>
        /// Initialise une nouvelle instance de la classe RelayCommand.
        /// </summary>
        /// <param name="execute">La méthode à exécuter lorsque la commande est invoquée.</param>
        /// <param name="canExecute">La méthode qui détermine si la commande peut être exécutée.</param>
        public RelayCommand(Action execute, Func<bool> canExecute)
        {
            _execute = execute ?? throw new ArgumentNullException(nameof(execute));
            _canExecute = canExecute;
        }

        /// <summary>
        /// Détermine si la commande peut être exécutée dans son état actuel.
        /// </summary>
        /// <param name="parameter">Données utilisées par la commande. Si la commande ne nécessite pas de données,
        /// ce paramètre peut être défini à null.</param>
        /// <returns>true si cette commande peut être exécutée; sinon, false.</returns>
        public bool CanExecute(object parameter)
        {
            return _canExecute == null || _canExecute();
        }

        /// <summary>
        /// Événement déclenché lorsque les conditions pouvant affecter si la commande peut s'exécuter ou non changent.
        /// </summary>
        public event EventHandler CanExecuteChanged
        {
            add { CommandManager.RequerySuggested += value; }
            remove { CommandManager.RequerySuggested -= value; }
        }

        /// <summary>
        /// Exécute la méthode associée à cette commande.
        /// </summary>
        /// <param name="parameter">Données utilisées par la commande. Si la commande ne nécessite pas de données,
        /// ce paramètre peut être défini à null.</param>
        public void Execute(object parameter)
        {
            _execute();
        }

        /// <summary>
        /// Déclenche manuellement l'événement CanExecuteChanged pour forcer la réévaluation
        /// des conditions de CanExecute.
        /// </summary>
        public void RaiseCanExecuteChanged()
        {
            CommandManager.InvalidateRequerySuggested();
        }
    }

    /// <summary>
    /// Une implémentation de l'interface ICommand qui permet de lier facilement
    /// des méthodes aux commandes dans le modèle MVVM (Model-View-ViewModel) avec un paramètre.
    /// </summary>
    /// <typeparam name="T">Le type du paramètre de la commande.</typeparam>
    public class RelayCommand<T> : ICommand
    {
        private readonly Action<T> _execute;
        private readonly Predicate<T> _canExecute;

        /// <summary>
        /// Initialise une nouvelle instance de la classe RelayCommand qui peut toujours être exécutée.
        /// </summary>
        /// <param name="execute">La méthode à exécuter lorsque la commande est invoquée.</param>
        public RelayCommand(Action<T> execute) : this(execute, null)
        {
        }

        /// <summary>
        /// Initialise une nouvelle instance de la classe RelayCommand.
        /// </summary>
        /// <param name="execute">La méthode à exécuter lorsque la commande est invoquée.</param>
        /// <param name="canExecute">La méthode qui détermine si la commande peut être exécutée.</param>
        public RelayCommand(Action<T> execute, Predicate<T> canExecute)
        {
            _execute = execute ?? throw new ArgumentNullException(nameof(execute));
            _canExecute = canExecute;
        }

        /// <summary>
        /// Détermine si la commande peut être exécutée dans son état actuel.
        /// </summary>
        /// <param name="parameter">Données utilisées par la commande. Si la commande ne nécessite pas de données,
        /// ce paramètre peut être défini à null.</param>
        /// <returns>true si cette commande peut être exécutée; sinon, false.</returns>
        public bool CanExecute(object parameter)
        {
            return _canExecute == null || _canExecute((T)parameter);
        }

        /// <summary>
        /// Événement déclenché lorsque les conditions pouvant affecter si la commande peut s'exécuter ou non changent.
        /// </summary>
        public event EventHandler CanExecuteChanged
        {
            add { CommandManager.RequerySuggested += value; }
            remove { CommandManager.RequerySuggested -= value; }
        }

        /// <summary>
        /// Exécute la méthode associée à cette commande.
        /// </summary>
        /// <param name="parameter">Données utilisées par la commande. Si la commande ne nécessite pas de données,
        /// ce paramètre peut être défini à null.</param>
        public void Execute(object parameter)
        {
            _execute((T)parameter);
        }

        /// <summary>
        /// Déclenche manuellement l'événement CanExecuteChanged pour forcer la réévaluation
        /// des conditions de CanExecute.
        /// </summary>
        public void RaiseCanExecuteChanged()
        {
            CommandManager.InvalidateRequerySuggested();
        }
    }
}