using System.Windows.Controls;
using IUTSCalculator.Services;
using IUTSCalculator.ViewModels;

namespace IUTSCalculator.Views
{
    /// <summary>
    /// Logique d'interaction pour PeriodePaieView.xaml — écran « Périodes de paie » (PAI-01).
    /// </summary>
    public partial class PeriodePaieView : UserControl
    {
        // Constructeur sans paramètre requis par WPF/XAML. Le DataContext est fourni par
        // le DataTemplate de MainWindow.xaml, lié au dossier actif.
        public PeriodePaieView()
        {
            InitializeComponent();
        }

        // Constructeur d'injection, pour un usage hors navigation principale.
        public PeriodePaieView(PeriodePaieService periodeService)
            : this()
        {
            DataContext = new PeriodePaieViewModel(periodeService);
        }
    }
}
