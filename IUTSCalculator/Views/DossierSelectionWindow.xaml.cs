using System;
using System.Windows;
using System.Windows.Input;
using IUTSCalculator.ViewModels;

namespace IUTSCalculator.Views
{
    /// <summary>
    /// Logique d'interaction pour DossierSelectionWindow.xaml.
    /// Fenêtre modale permettant d'ouvrir un dossier existant, ou d'en créer,
    /// modifier ou supprimer un.
    /// </summary>
    public partial class DossierSelectionWindow : Window
    {
        private readonly DossierSelectionViewModel _viewModel;

        public DossierSelectionWindow()
        {
            InitializeComponent();

            _viewModel = new DossierSelectionViewModel();
            _viewModel.DossierOpened += ViewModel_DossierOpened;
            DataContext = _viewModel;

            Closed += DossierSelectionWindow_Closed;
        }

        private void ViewModel_DossierOpened(object sender, EventArgs e)
        {
            DialogResult = true;
            Close();
        }

        private void DossiersGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (_viewModel.OpenDossierCommand.CanExecute(null))
                _viewModel.OpenDossierCommand.Execute(null);
        }

        private void DossierSelectionWindow_Closed(object sender, EventArgs e)
        {
            Closed -= DossierSelectionWindow_Closed;
            _viewModel.DossierOpened -= ViewModel_DossierOpened;
        }
    }
}
