using System;
using System.Windows;
using System.Windows.Input;
using IUTSCalculator.ViewModels;
using IUTSCalculator.Services;
using IUTSCalculator.Models;

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
            Title = "Sigma Paie & RH " + IUTSCalculator.Helpers.VersionApplication.Court;

            // Initialiser le ViewModel principal (nécessite qu'un dossier ait déjà été
            // ouvert via l'écran de sélection de dossier, voir App.xaml.cs)
            _mainViewModel = new MainViewModel();
            _mainViewModel.DossierChangeRequested += MainViewModel_DossierChangeRequested;

            // Définir le DataContext de la fenêtre principale
            DataContext = _mainViewModel;

            // S'abonner à l'événement de fermeture
            Closed += MainWindow_Closed;

            ConfigurerMenuUtilisateur();
        }

        /// <summary>
        /// Prépare le menu lié à l'utilisateur connecté : affiche son nom et son rôle, et
        /// masque la gestion des utilisateurs pour les non-administrateurs.
        /// </summary>
        private void ConfigurerMenuUtilisateur()
        {
            var u = UserSession.Instance.CurrentUser;
            if (u != null)
            {
                MenuUtilisateurCourant.Header = "Connecté : " + (u.DisplayName ?? u.Username)
                                                + "  (" + LibelleRole(u.Role) + ")";
            }
            MenuGestionUtilisateurs.Visibility = UserSession.Instance.IsAdmin
                ? Visibility.Visible : Visibility.Collapsed;
        }

        private static string LibelleRole(UserRole r)
        {
            switch (r)
            {
                case UserRole.ReadOnly: return "Lecture seule";
                case UserRole.Editor: return "Éditeur";
                case UserRole.Admin: return "Administrateur";
                default: return r.ToString();
            }
        }

        private void GererUtilisateurs_Click(object sender, RoutedEventArgs e)
        {
            if (!UserSession.Instance.IsAdmin)
            {
                MessageBox.Show(this, "Seul un administrateur peut gérer les utilisateurs.",
                    "Accès refusé", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            var service = new UserAccountService(IUTSCalculator.Helpers.Constants.AppDataFolder);
            var fenetre = new Views.UserManagementWindow(service) { Owner = this };
            fenetre.ShowDialog();

            // Le compte connecté a pu être modifié (nom affiché, rôle) : le menu suit.
            ConfigurerMenuUtilisateur();
        }

        private void Deconnexion_Click(object sender, RoutedEventArgs e)
        {
            var confirmation = MessageBox.Show(this,
                "Se déconnecter et revenir à l'écran de connexion ?",
                "Déconnexion", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (confirmation != MessageBoxResult.Yes) return;

            UserSession.Instance.Logout();
            try
            {
                string exe = System.Diagnostics.Process.GetCurrentProcess().MainModule.FileName;
                System.Diagnostics.Process.Start(exe);
            }
            catch { /* si le relancement échoue, on ferme quand même */ }
            Application.Current.Shutdown();
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
            NotificationService.Instance.Info("Fonctionnalité à implémenter");
        }

        /// <summary>
        /// Ouvre la documentation
        /// </summary>
        private void OpenDocumentation_Click(object sender, RoutedEventArgs e)
        {
            NotificationService.Instance.Info("La documentation sera ouverte dans une version future.");
        }

        /// <summary>
        /// Affiche la boîte de dialogue "À propos"
        /// </summary>
        private void About_Click(object sender, RoutedEventArgs e)
        {
            var aboutMessage = "Sigma Paie & RH\n" +
                               IUTSCalculator.Helpers.VersionApplication.Complet + "\n\n" +
                               "Données de ce poste : " + IUTSCalculator.Helpers.Constants.AppDataFolder + "\n\n" +
                               "© 2025-2026 — Tous droits réservés.";

            NotificationService.Instance.Info(aboutMessage);
        }

        /// <summary>
        /// Sauvegarde de toutes les données du poste (le dossier ouvert compris : la copie
        /// des bases est faite par SQLite et reste cohérente). La restauration, elle, se fait
        /// au démarrage, depuis l'écran de sélection du dossier.
        /// </summary>
        private async void Sauvegarder_Click(object sender, RoutedEventArgs e)
        {
            var message = await IUTSCalculator.Helpers.SauvegardeInteractive.SauvegarderAsync();
            if (message != null) _mainViewModel.StatusMessage = message;
        }

        /// <summary>
        /// Ouvre la fenêtre d'activation / gestion de licence depuis le menu. Permet
        /// d'activer pendant la période d'évaluation ou de renouveler une licence.
        /// </summary>
        private void ActiverLicence_Click(object sender, RoutedEventArgs e)
        {
            var etat = Licensing.LicenceService.Instance.Evaluer();
            var fenetre = new Views.ActivationWindow(etat) { Owner = this };
            fenetre.ShowDialog();
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