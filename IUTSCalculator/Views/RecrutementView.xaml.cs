using System.Windows;
using System.Windows.Controls;
using IUTSCalculator.ViewModels;

namespace IUTSCalculator.Views
{
    /// <summary>
    /// Logique d'interaction pour RecrutementView.xaml (module RH-05 Recrutement & Candidatures).
    /// </summary>
    public partial class RecrutementView : UserControl
    {
        public RecrutementView()
        {
            InitializeComponent();
        }

        private void BasculerVersOngletEmbauche_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is RecrutementViewModel vm)
            {
                vm.OngletActifIndex = 3; // Basculer vers l'onglet "Entretiens & Embauche"
            }
        }
    }
}
