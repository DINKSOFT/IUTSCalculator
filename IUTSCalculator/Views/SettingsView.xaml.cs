using System;
using System.Windows;
using System.Windows.Controls;
using IUTSCalculator.ViewModels;
using System.Windows.Forms;

namespace IUTSCalculator.Views
{
    /// <summary>  
    /// Logique d'interaction pour SettingsView.xaml (UserControl)  
    /// </summary>  
    public partial class SettingsView : System.Windows.Controls.UserControl // Fully qualify UserControl to resolve ambiguity  
    {
        /// <summary>  
        /// Constructeur sans paramètre pour l'instanciation via XAML  
        /// </summary>  
        public SettingsView()
        {
            InitializeComponent();
        }

        /// <summary>  
        /// Ouvre une boîte de dialogue pour sélectionner le répertoire d'exportation  
        /// (utilisé uniquement si le bouton n'est pas lié à une ICommand)  
        /// </summary>  
        private void BrowseExportDirectory_Click(object sender, RoutedEventArgs e)
        {
            var viewModel = DataContext as SettingsViewModel;
            using (var dialog = new FolderBrowserDialog
            {
                Description = "Sélectionner le répertoire d'exportation",
                ShowNewFolderButton = true
            })
            {
                if (viewModel != null && !string.IsNullOrEmpty(viewModel.ExportDirectory))
                {
                    dialog.SelectedPath = viewModel.ExportDirectory;
                }

                var result = dialog.ShowDialog();

                if (result == DialogResult.OK && !string.IsNullOrWhiteSpace(dialog.SelectedPath))
                {
                    if (viewModel != null)
                        viewModel.ExportDirectory = dialog.SelectedPath;
                }
            }
        }
    }
}

