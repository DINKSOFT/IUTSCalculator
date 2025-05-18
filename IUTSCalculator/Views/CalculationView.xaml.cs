using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using IUTSCalculator.ViewModels;
using System.Globalization;
using IUTSCalculator.Services;
using System.Windows.Media; // Ajouté pour accéder aux services

namespace IUTSCalculator.Views
{
    /// <summary>
    /// Logique d'interaction pour CalculationView.xaml
    /// </summary>
    public partial class CalculationView : UserControl
    {
        private readonly CalculationViewModel _viewModel;

        /// <summary>
        /// Constructeur par défaut
        /// </summary>
        public CalculationView()
        {
            InitializeComponent();

            // Créer et initialiser le ViewModel avec les services nécessaires
            // Note: Idéalement, ces services devraient être injectés via un 
            // conteneur d'injection de dépendances. Ceci est une solution simple.

            // Pour l'instant, nous créons une instance directe des services
            // Dans une application réelle, utilisez votre système d'injection de dépendances
            // Définir le répertoire des données fiscales (par exemple, un sous-répertoire "Data" dans le répertoire de l'application)
            string dataDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data");

            // S'assurer que le répertoire existe
            if (!Directory.Exists(dataDirectory))
            {
                Directory.CreateDirectory(dataDirectory);
            }
            var taxDataService = new TaxDataService(dataDirectory);
            var taxCalculatorService = new TaxCalculatorService(taxDataService);
            var dataService = new DataService();

            _viewModel = new CalculationViewModel(taxCalculatorService, dataService);
            DataContext = _viewModel;

            // S'abonner à l'événement de complétion du calcul
            _viewModel.CalculationCompleted += OnCalculationCompleted;

            // S'abonner aux événements de déchargement de la vue
            Unloaded += CalculationView_Unloaded;
        }

        /// <summary>
        /// Constructeur avec injection du ViewModel
        /// </summary>
        /// <param name="viewModel">ViewModel associé à cette vue</param>
        public CalculationView(CalculationViewModel viewModel)
        {
            InitializeComponent();

            _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
            DataContext = _viewModel;

            // S'abonner à l'événement de complétion du calcul
            _viewModel.CalculationCompleted += OnCalculationCompleted;

            // S'abonner aux événements de déchargement de la vue
            Unloaded += CalculationView_Unloaded;
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