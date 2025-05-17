using System;
using System.Windows;
using IUTSCalculator.ViewModels;
using IUTSCalculator.Views;
using IUTSCalculator.Models;
using IUTSCalculator.Services;
using System.IO;

namespace IUTSCalculator
{
    /// <summary>
    /// Logique d'interaction pour MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private readonly CalculationViewModel _calculationViewModel;
        private readonly MainViewModel _mainViewModel;
        private Views.CalculationView _calculationView;        

        /// <summary>
        /// Constructeur de la fenêtre principale
        /// </summary>
        public MainWindow()
        {
            InitializeComponent();

            // Définir le répertoire des données fiscales (par exemple, un sous-répertoire "Data" dans le répertoire de l'application)
            string dataDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data");

            // S'assurer que le répertoire existe
            if (!Directory.Exists(dataDirectory))
            {
                Directory.CreateDirectory(dataDirectory);
            }

            // Initialiser les services
            var taxDataService = new TaxDataService(dataDirectory);
            var taxCalculatorService = new TaxCalculatorService(taxDataService);
            var dataService = new DataService();

            // Initialiser les ViewModels
            _mainViewModel = new MainViewModel();
            _calculationViewModel = new CalculationViewModel(taxCalculatorService, dataService);

            // Créer et configurer la vue de calcul
            _calculationView = new Views.CalculationView(_calculationViewModel);
            MainContent.Content = _calculationView;

            // Définir le DataContext de la fenêtre principale
            DataContext = _mainViewModel;

            // Mettre à jour le statut et la version
            UpdateStatus("Application prête");
            VersionText.Text = $"Version {GetApplicationVersion()}";

            // S'abonner aux événements de déchargement
            Closed += MainWindow_Closed;
        }

        /// <summary>
        /// Récupère la version actuelle de l'application
        /// </summary>
        private string GetApplicationVersion()
        {
            var version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
            return version != null ? $"{version.Major}.{version.Minor}.{version.Build}" : "1.0.0";
        }

        /// <summary>
        /// Met à jour le texte de statut dans la barre d'état
        /// </summary>
        /// <param name="message">Message à afficher</param>
        public void UpdateStatus(string message)
        {
            StatusText.Text = message;
        }

        #region Gestionnaires d'événements du menu

        /// <summary>
        /// Crée un nouveau calcul
        /// </summary>
        private void NewCalculation_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Réinitialiser le ViewModel de calcul
                _calculationViewModel.CreateNewCalculation();

                // Afficher la vue de calcul
                MainContent.Content = _calculationView;

                UpdateStatus("Nouveau calcul initialisé");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erreur lors de la création d'un nouveau calcul: {ex.Message}",
                                "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
                UpdateStatus("Erreur: Impossible de créer un nouveau calcul");
            }
        }

        /// <summary>
        /// Ouvre la fenêtre d'historique
        /// </summary>
        private void OpenHistory_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var dataService = new DataService();
                // Définir le même répertoire de données fiscales que précédemment
                string dataDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data");
                var taxDataService = new TaxDataService(dataDirectory);
                var taxCalculatorService = new TaxCalculatorService(taxDataService);
                var historyViewModel = new HistoryViewModel(dataService, taxCalculatorService);
                var historyView = new HistoryView(historyViewModel);

                // S'abonner à l'événement de sélection d'un calcul
                historyView.CalculationSelected += HistoryView_CalculationSelected;

                // Afficher la fenêtre d'historique
                historyView.Owner = this;
                historyView.ShowDialog();

                UpdateStatus("Historique consulté");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erreur lors de l'ouverture de l'historique: {ex.Message}",
                                "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
                UpdateStatus("Erreur: Impossible d'ouvrir l'historique");
            }
        }

        /// <summary>
        /// Gestionnaire d'événement pour la sélection d'un calcul depuis l'historique
        /// </summary>
        private void HistoryView_CalculationSelected(object sender, TaxCalculation calculation)
        {
            try
            {
                // Charger le calcul sélectionné dans le ViewModel
                _calculationViewModel.LoadExistingCalculation(calculation);

                // Afficher la vue de calcul
                MainContent.Content = _calculationView;

                UpdateStatus($"Calcul chargé depuis l'historique: {calculation.CalculationDate:dd/MM/yyyy HH:mm}");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erreur lors du chargement du calcul: {ex.Message}",
                                "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
                UpdateStatus("Erreur: Impossible de charger le calcul");
            }
        }

        /// <summary>
        /// Exporte le calcul actuel
        /// </summary>
        private void Export_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Vérifier si un calcul est en cours
                if (_calculationViewModel.CanExport())
                {
                    bool success = _calculationViewModel.ExportCalculation();
                    

                    if (success)
                    {
                        UpdateStatus("Calcul exporté avec succès");
                    }
                    else
                    {
                        UpdateStatus("L'exportation a été annulée");
                    }
                }
                else
                {
                    MessageBox.Show("Aucun calcul à exporter. Veuillez d'abord effectuer un calcul.",
                                    "Information", MessageBoxButton.OK, MessageBoxImage.Information);
                    UpdateStatus("Aucun calcul à exporter");
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erreur lors de l'exportation: {ex.Message}",
                                "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
                UpdateStatus("Erreur: Échec de l'exportation");
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
        /// Ouvre la fenêtre des paramètres
        /// </summary>
        private void OpenSettings_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var configService = new DataService();
                var settingsViewModel = new SettingsViewModel(configService);
                var settingsView = new SettingsView(settingsViewModel);

                // Afficher la fenêtre des paramètres
                settingsView.Owner = this;
                var result = settingsView.ShowDialog();

                if (result == true)
                {
                    // Mettre à jour les paramètres si nécessaire
                    _calculationViewModel.RefreshSettings();
                    UpdateStatus("Paramètres mis à jour");
                }
                else
                {
                    UpdateStatus("Modification des paramètres annulée");
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erreur lors de l'ouverture des paramètres: {ex.Message}",
                                "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
                UpdateStatus("Erreur: Impossible d'ouvrir les paramètres");
            }
        }

        /// <summary>
        /// Ouvre les préférences d'affichage
        /// </summary>
        private void DisplayPreferences_Click(object sender, RoutedEventArgs e)
        {
            // À implémenter: fenêtre des préférences d'affichage
            MessageBox.Show("Fonctionnalité à implémenter", "Information",
                          MessageBoxButton.OK, MessageBoxImage.Information);
            UpdateStatus("Préférences d'affichage: fonctionnalité à implémenter");
        }

        /// <summary>
        /// Ouvre la documentation
        /// </summary>
        private void OpenDocumentation_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // À implémenter: ouverture de la documentation
                // Exemple: System.Diagnostics.Process.Start("Documentation.pdf");
                MessageBox.Show("La documentation sera ouverte dans une version future.",
                              "Information", MessageBoxButton.OK, MessageBoxImage.Information);
                UpdateStatus("Documentation: fonctionnalité à implémenter");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erreur lors de l'ouverture de la documentation: {ex.Message}",
                                "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
                UpdateStatus("Erreur: Impossible d'ouvrir la documentation");
            }
        }

        /// <summary>
        /// Affiche la boîte de dialogue "À propos"
        /// </summary>
        private void About_Click(object sender, RoutedEventArgs e)
        {
            var aboutMessage = "Calculateur d'Impôts IUTS\n" +
                              $"Version {GetApplicationVersion()}\n\n" +
                              "© 2025 IUTSCalculator\n" +
                              "Tous droits réservés.";

            MessageBox.Show(aboutMessage, "À propos",
                          MessageBoxButton.OK, MessageBoxImage.Information);
            UpdateStatus("Informations sur l'application affichées");
        }


        #endregion

        /// <summary>
        /// Méthode appelée lors de la fermeture de la fenêtre
        /// </summary>
        private void MainWindow_Closed(object sender, EventArgs e)
        {
            // Se désabonner des événements pour éviter les fuites de mémoire
            Closed -= MainWindow_Closed;

            // Nettoyer les ressources
            _calculationViewModel?.Dispose();
            _mainViewModel?.Dispose();
        }
    }
}