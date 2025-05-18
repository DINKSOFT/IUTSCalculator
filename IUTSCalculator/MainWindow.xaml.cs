using System;
using System.Windows;
using IUTSCalculator.ViewModels;

namespace IUTSCalculator
{
    /// <summary>
    /// Logique d'interaction pour MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private readonly MainViewModel _mainViewModel;

        /// <summary>
        /// Constructeur de la fenêtre principale
        /// </summary>
        public MainWindow()
        {
            InitializeComponent();

            // Initialiser le ViewModel principal
            _mainViewModel = new MainViewModel();

            // Définir le DataContext de la fenêtre principale
            DataContext = _mainViewModel;

            // S'abonner à l'événement de fermeture
            Closed += MainWindow_Closed;
        }

        /// <summary>
        /// Quitte l'application
        /// </summary>
        private void Exit_Click(object sender, RoutedEventArgs e)
        {
            Application.Current.Shutdown();
        }

        /// <summary>
        /// Ouvre les préférences d'affichage
        /// </summary>
        private void DisplayPreferences_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show("Fonctionnalité à implémenter", "Information",
                MessageBoxButton.OK, MessageBoxImage.Information);
            // Le ViewModel peut exposer une propriété StatusMessage si besoin
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
            _mainViewModel?.Dispose();
        }
    }
}