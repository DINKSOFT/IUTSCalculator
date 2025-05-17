using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using IUTSCalculator.Models;
using IUTSCalculator.ViewModels;
using IUTSCalculator.Converters;

namespace IUTSCalculator.Views
{
    /// <summary>
    /// Logique d'interaction pour HistoryView.xaml
    /// </summary>
    public partial class HistoryView : Window
    {
        /// <summary>
        /// Événement déclenché lorsqu'un calcul est sélectionné depuis l'historique
        /// </summary>
        public event EventHandler<TaxCalculation> CalculationSelected;

        /// <summary>
        /// Obtient ou définit le ViewModel associé à cette vue
        /// </summary>
        public HistoryViewModel ViewModel => DataContext as HistoryViewModel;

        /// <summary>
        /// Constructeur par défaut
        /// </summary>
        public HistoryView()
        {
            InitializeComponent();
        }

        /// <summary>
        /// Initialise la vue avec un ViewModel spécifique
        /// </summary>
        /// <param name="viewModel">ViewModel à associer à cette vue</param>
        /// <exception cref="ArgumentNullException">Lancée si viewModel est null</exception>
        public HistoryView(HistoryViewModel viewModel) : this()
        {
            // Définir le DataContext
            DataContext = viewModel ?? throw new ArgumentNullException(nameof(viewModel));

            // S'abonner à l'événement de sélection du calcul
            viewModel.CalculationSelected += ViewModel_CalculationSelected;

            // Charger l'historique au démarrage (si ce n'est pas déjà fait)
            if (viewModel.Calculations == null || viewModel.Calculations.Count == 0)
            {
                viewModel.LoadCalculationHistory();
            }

            // S'abonner à l'événement de fermeture pour nettoyer les abonnements
            Closed += (s, e) =>
            {
                viewModel.CalculationSelected -= ViewModel_CalculationSelected;
            };
        }

        /// <summary>
        /// Gestionnaire d'événement pour la sélection d'un calcul dans le ViewModel
        /// </summary>
        private void ViewModel_CalculationSelected(object sender, TaxCalculation calculation)
        {
            // Relayer l'événement vers le code qui utilise cette vue
            CalculationSelected?.Invoke(this, calculation);
        }
    }
}