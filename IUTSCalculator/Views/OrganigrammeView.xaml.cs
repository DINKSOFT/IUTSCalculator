using System.Windows;
using System.Windows.Controls;
using IUTSCalculator.Models;
using IUTSCalculator.ViewModels;

namespace IUTSCalculator.Views
{
    /// <summary>
    /// Logique d'interaction pour OrganigrammeView.xaml (module RH-01).
    /// </summary>
    public partial class OrganigrammeView : UserControl
    {
        public OrganigrammeView()
        {
            InitializeComponent();
        }

        private void TreeView_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            if (DataContext is OrganigrammeViewModel vm && e.NewValue is NoeudOrganigramme noeud)
            {
                vm.NoeudSelectionne = noeud;
            }
        }
    }
}
