using System.Windows.Controls;
using IUTSCalculator.Services;
using IUTSCalculator.ViewModels;

namespace IUTSCalculator.Views
{
    /// <summary>
    /// Logique d'interaction pour RubriqueView.xaml — écran « Plan de paie » (SOC-01).
    /// </summary>
    public partial class RubriqueView : UserControl
    {
        // Constructeur sans paramètre requis par WPF/XAML. Le DataContext est fourni par
        // le DataTemplate de MainWindow.xaml, lié au dossier actif.
        public RubriqueView()
        {
            InitializeComponent();
        }

        // Constructeur d'injection, pour un usage hors navigation principale.
        public RubriqueView(RubriqueService rubriqueService)
            : this()
        {
            DataContext = new RubriqueViewModel(rubriqueService);
        }
    }
}
