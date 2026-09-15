using System;
using System.Windows;
using System.Windows.Input;
using IUTSCalculator.ViewModels;

namespace IUTSCalculator
{
    /// <summary>
    /// Logique d'interaction pour MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private readonly MainViewModel _mainViewModel;
        private bool _isChangingDossier;

        /// <summary>
        /// Constructeur de la fenêtre principale
        /// </summary>
        public MainWindow()
        {
            InitializeComponent();

            // Initialiser le ViewModel principal (nécessite qu'un dossier ait déjà été
            // ouvert via l'écran de sélection de dossier, voir App.xaml.cs)
            _mainViewModel = new MainViewModel();
            _mainViewModel.DossierChangeRequested += MainViewModel_DossierChangeRequested;

            // Définir le DataContext de la fenêtre principale
            DataContext = _mainViewModel;

            // S'abonner à l'événement de fermeture
            Closed += MainWindow_Closed;
        }

        /// <summary>
        /// Raccourci clavier global (Ctrl+F ou Ctrl+K) pour atteindre rapidement la barre de recherche du menu
        /// </summary>
        protected override void OnPreviewKeyDown(KeyEventArgs e)
        {
            base.OnPreviewKeyDown(e);

            if ((Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control &&
                (e.Key == Key.F || e.Key == Key.K))
            {
                if (_mainViewModel != null && !_mainViewModel.IsNavRailVisible)
                {
                    _mainViewModel.IsNavRailVisible = true;
                }

                NavMenuSearchBox?.Focus();
                NavMenuSearchBox?.SelectAll();
                e.Handled = true;
            }
        }

        /// <summary>
        /// Quitte l'application
        /// </summary>
        private void Exit_Click(object sender, RoutedEventArgs e)
        {
            Application.Current.Shutdown();
        }

        /// <summary>
        /// Ouvre l'écran de sélection de dossier pour changer d'entreprise active,
        /// sans avoir à relancer l'application.
        /// </summary>
        private void ChangeDossier_Click(object sender, RoutedEventArgs e)
        {
            RequestChangeDossier();
        }

        private void MainViewModel_DossierChangeRequested(object sender, EventArgs e)
        {
            RequestChangeDossier();
        }

        private void RequestChangeDossier()
        {
            var selectionWindow = new Views.DossierSelectionWindow
            {
                Owner = this
            };
            bool? result = selectionWindow.ShowDialog();

            if (result == true && Services.DossierSession.Instance.HasOpenDossier)
            {
                // Un nouveau dossier a été ouvert : on ouvre une nouvelle fenêtre principale
                // pointant vers ce dossier, puis on referme l'ancienne.
                var newMainWindow = new MainWindow();
                Application.Current.MainWindow = newMainWindow;
                newMainWindow.Show();

                _isChangingDossier = true;
                Close();
            }
            // Si l'utilisateur annule, on conserve le dossier et la fenêtre actuels.
        }

        /// <summary>
        /// Ouvre les préférences d'affichage
        /// </summary>
        private void DisplayPreferences_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show("Fonctionnalité à implémenter", "Information",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }

        /// <summary>
        /// Ouvre la documentation
        /// </summary>
        private void OpenDocumentation_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show("La documentation sera ouverte dans une version future.",
                "Information", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        /// <summary>
        /// Affiche la boîte de dialogue "À propos"
        /// </summary>
        private void About_Click(object sender, RoutedEventArgs e)
        {
            var version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
            var versionString = version != null ? $"{version.Major}.{version.Minor}.{version.Build}" : "1.0.0";
            var aboutMessage = "Calculateur d'Impôts IUTS\n" +
                               $"Version {versionString}\n\n" +
                               "© 2025 IUTSCalculator\n" +
                               "Tous droits réservés.";

            MessageBox.Show(aboutMessage, "À propos",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }

        /// <summary>
        /// Méthode appelée lors de la fermeture de la fenêtre
        /// </summary>
        private void MainWindow_Closed(object sender, EventArgs e)
        {
            Closed -= MainWindow_Closed;
            if (_mainViewModel != null)
                _mainViewModel.DossierChangeRequested -= MainViewModel_DossierChangeRequested;
            _mainViewModel?.Dispose();

            // Si la fermeture ne correspond pas à un changement de dossier (nouvelle fenêtre
            // déjà ouverte), il s'agit d'une fermeture normale de l'application.
            if (!_isChangingDossier)
            {
                Application.Current.Shutdown();
            }
        }
    }
}