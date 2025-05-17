using System;
using System.Windows;
using System.Windows.Controls;
using IUTSCalculator.ViewModels;
using System.Windows.Forms;
using MessageBox = System.Windows.MessageBox;

namespace IUTSCalculator.Views
{
    /// <summary>
    /// Logique d'interaction pour SettingsView.xaml
    /// </summary>
    public partial class SettingsView : Window
    {
        private readonly SettingsViewModel _viewModel;

        /// <summary>
        /// Initialise une nouvelle instance de la fenêtre des paramètres
        /// </summary>
        /// <param name="viewModel">ViewModel à utiliser</param>
        public SettingsView(SettingsViewModel viewModel)
        {
            InitializeComponent();

            _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
            DataContext = _viewModel;

            // S'abonner à l'événement de sauvegarde des paramètres
            _viewModel.SettingsSaved += OnSettingsSaved;

            // S'abonner à l'événement de fermeture de la fenêtre
            Closing += (s, e) => _viewModel.SettingsSaved -= OnSettingsSaved;
        }

        /// <summary>
        /// Méthode appelée lorsque les paramètres sont sauvegardés
        /// </summary>
        private void OnSettingsSaved(object sender, EventArgs e)
        {
            // Fermer la fenêtre si la sauvegarde est réussie
            var result = MessageBox.Show(
                "Les paramètres ont été enregistrés avec succès. Voulez-vous fermer cette fenêtre ?",
                "Paramètres enregistrés",
                MessageBoxButton.YesNo,
                MessageBoxImage.Information);

            if (result == MessageBoxResult.Yes)
            {
                DialogResult = true;
            }
        }

        /// <summary>
        /// Ouvre une boîte de dialogue pour sélectionner le répertoire d'exportation
        /// </summary>
        private void BrowseExportDirectory_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new System.Windows.Forms.FolderBrowserDialog
            {
                Description = "Sélectionner le répertoire d'exportation",
                ShowNewFolderButton = true
            };

            // Définir le dossier initial si disponible
            if (!string.IsNullOrEmpty(_viewModel.ExportDirectory))
            {
                dialog.SelectedPath = _viewModel.ExportDirectory;
            }

            // Afficher la boîte de dialogue
            var result = dialog.ShowDialog();

            // Mettre à jour le répertoire si un dossier a été sélectionné
            if (result == System.Windows.Forms.DialogResult.OK && !string.IsNullOrWhiteSpace(dialog.SelectedPath))
            {
                _viewModel.ExportDirectory = dialog.SelectedPath;
            }
        }
    }
}