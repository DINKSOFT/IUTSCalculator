using System.Windows.Controls;
using IUTSCalculator.Services;
using IUTSCalculator.ViewModels;

namespace IUTSCalculator.Views
{
    /// <summary>
    /// Logique d'interaction pour BaremeView.xaml — écran « Barèmes légaux » (SOC-02).
    /// </summary>
    public partial class BaremeView : UserControl
    {
        // Constructeur sans paramètre requis par WPF/XAML. Le DataContext est fourni par
        // le DataTemplate de MainWindow.xaml, lié au dossier actif.
        public BaremeView()
        {
            InitializeComponent();
        }

        // Constructeur d'injection, pour un usage hors navigation principale.
        public BaremeView(BaremeService baremeService)
            : this()
        {
            DataContext = new BaremeViewModel(baremeService);
        }
    }
}
