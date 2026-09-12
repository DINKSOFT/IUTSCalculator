using IUTSCalculator.ViewModels;
using System.Windows.Controls;
using IUTSCalculator.Services;

namespace IUTSCalculator.Views
{
    /// <summary>  
    /// Logique d'interaction pour EmployeeManagementView.xaml  
    /// </summary>  
    public partial class EmployeeManagementView : UserControl
    {
        // Constructeur sans paramètre requis par WPF/XAML.
        // Le DataContext (EmployeeManagementViewModel) est fourni par le DataTemplate de
        // MainWindow.xaml, lié au DataService du dossier actif : on ne le recrée pas ici,
        // sous peine de recharger les employés depuis la mauvaise base de données.
        public EmployeeManagementView()
        {
            InitializeComponent();
        }

        // Constructeur personnalisé pour l'injection de dépendance
        public EmployeeManagementView(DataService dataService)
            : this()
        {
            this.DataContext = new EmployeeManagementViewModel(dataService);
        }

        // Il y avait ici un gestionnaire qui faisait défiler l'écran jusqu'en bas à la
        // sélection d'une ligne, pour amener à l'écran le bloc « Modifier l'employé »
        // qui se trouvait sous la liste. La fiche est désormais affichée en permanence
        // à droite de la liste : il n'y a plus rien à faire défiler.
    }
}