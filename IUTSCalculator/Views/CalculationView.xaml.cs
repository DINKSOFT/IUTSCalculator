using System;
using System.Windows;
using System.Windows.Controls;
using IUTSCalculator.ViewModels;
using System.Globalization;
using System.Windows.Media;

namespace IUTSCalculator.Views
{
    /// <summary>
    /// Logique d'interaction pour CalculationView.xaml
    /// </summary>
    public partial class CalculationView : UserControl
    {
        private CalculationViewModel _viewModel;

        /// <summary>
        /// Constructeur par défaut.
        /// Le DataContext (CalculationViewModel) est fourni par le DataTemplate de
        /// MainWindow.xaml, construit avec le DataService du dossier actif : on ne le
        /// recrée pas ici, sous peine de charger employés/calculs depuis la mauvaise base
        /// de données. On s'abonne à CalculationCompleted dès que le DataContext est connu.
        /// </summary>
        public CalculationView()
        {
            InitializeComponent();

            DataContextChanged += CalculationView_DataContextChanged;
            Unloaded += CalculationView_Unloaded;
        }

        private void CalculationView_DataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (_viewModel != null)
                _viewModel.CalculationCompleted -= OnCalculationCompleted;

            _viewModel = DataContext as CalculationViewModel;

            if (_viewModel != null)
                _viewModel.CalculationCompleted += OnCalculationCompleted;
        }

        /// <summary>
        /// Constructeur avec injection du ViewModel
        /// </summary>
        /// <param name="viewModel">ViewModel associé à cette vue</param>
        public CalculationView(CalculationViewModel viewModel)
        {
            InitializeComponent();

            DataContextChanged += CalculationView_DataContextChanged;
            Unloaded += CalculationView_Unloaded;

            if (viewModel == null)
                throw new ArgumentNullException(nameof(viewModel));
            DataContext = viewModel;
        }

        /// <summary>
        /// Méthode appelée lorsque le calcul est terminé
        /// </summary>
        private void OnCalculationCompleted(object sender, EventArgs e)
        {
                       // Faire défiler jusqu'à la fin du ScrollViewer principal
            var scrollViewer = FindScrollViewer(this);
            if (scrollViewer != null)
            {
                scrollViewer.ScrollToEnd();
            }
        }

        /// <summary>
        /// Recherche récursivement le premier ScrollViewer dans l'arborescence visuelle
        /// </summary>
        private ScrollViewer FindScrollViewer(DependencyObject parent)
        {
            if (parent == null) return null;
            if (parent is ScrollViewer sv) return sv;

            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                var result = FindScrollViewer(child);
                if (result != null)
                    return result;
            }
            return null;
        }

        private void TextBox_GotFocus_ClearIfZero(object sender, RoutedEventArgs e)
        {
            if (sender is TextBox tb)
            {
                var text = tb.Text?.Trim();
                if (text == "0" || text == "0,00" || text == "0.00")
                {
                    tb.Clear();
                }
                else if (decimal.TryParse(text, NumberStyles.Any, CultureInfo.CurrentCulture, out decimal value) && value == 0)
                {
                    tb.Clear();
                }
            }
        }


        /// <summary>
        /// Méthode appelée lors du déchargement de la vue
        /// </summary>
        private void CalculationView_Unloaded(object sender, RoutedEventArgs e)
        {
            // Se désabonner des événements pour éviter les fuites de mémoire
            if (_viewModel != null)
            {
                _viewModel.CalculationCompleted -= OnCalculationCompleted;
            }

            Unloaded -= CalculationView_Unloaded;
        }

    }
}