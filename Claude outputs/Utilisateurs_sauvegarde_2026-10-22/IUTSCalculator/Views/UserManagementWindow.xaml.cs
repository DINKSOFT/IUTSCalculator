using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using IUTSCalculator.Models;
using IUTSCalculator.Services;

namespace IUTSCalculator.Views
{
    /// <summary>
    /// Écran d'administration des comptes utilisateurs (réservé au rôle Administrateur).
    /// Permet de créer des comptes, changer leur rôle, réinitialiser un mot de passe et
    /// supprimer un compte, avec des garde-fous (pas de suppression de soi-même ni du
    /// dernier administrateur).
    /// </summary>
    public partial class UserManagementWindow : Window
    {
        private sealed class LigneUtilisateur
        {
            public string Nom { get; set; }
            public string Identifiant { get; set; }
            public string RoleLibelle { get; set; }
            public UserAccount Source { get; set; }
        }

        private readonly UserAccountService _userService;
        private List<UserAccount> _utilisateurs = new List<UserAccount>();

        public UserManagementWindow(UserAccountService userService)
        {
            InitializeComponent();
            _userService = userService ?? throw new ArgumentNullException(nameof(userService));
            Loaded += async (s, e) => await RafraichirAsync();
        }

        private async Task RafraichirAsync()
        {
            _utilisateurs = await _userService.LoadUsersAsync();
            GrilleUtilisateurs.ItemsSource = _utilisateurs
                .OrderBy(u => u.DisplayName ?? u.Username)
                .Select(u => new LigneUtilisateur
                {
                    Nom = u.DisplayName,
                    Identifiant = u.Username,
                    RoleLibelle = LibelleRole(u.Role),
                    Source = u
                })
                .ToList();
            SelInfo.Text = "Aucun utilisateur sélectionné.";
        }

        private UserAccount Selectionne()
        {
            return (GrilleUtilisateurs.SelectedItem as LigneUtilisateur)?.Source;
        }

        private void Grille_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var u = Selectionne();
            if (u == null)
            {
                SelInfo.Text = "Aucun utilisateur sélectionné.";
                return;
            }
            SelInfo.Text = (u.DisplayName ?? u.Username) + " — identifiant « " + u.Username + " ».";
            SelRole.SelectedIndex = IndexRole(u.Role);
        }

        private async void Ajouter_Click(object sender, RoutedEventArgs e)
        {
            string nom = NewNom.Text?.Trim();
            string identifiant = NewIdentifiant.Text?.Trim();
            string mdp = NewMdp.Password;

            if (string.IsNullOrWhiteSpace(identifiant)) { Info("L'identifiant est obligatoire.", true); return; }
            if (mdp.Length < 6) { Info("Le mot de passe doit comporter au moins 6 caractères.", true); return; }

            var nouvel = new UserAccount
            {
                Username = identifiant,
                DisplayName = string.IsNullOrWhiteSpace(nom) ? identifiant : nom,
                Role = RoleDepuisIndex(NewRole.SelectedIndex)
            };

            try
            {
                bool ok = await _userService.AddUserAsync(nouvel, mdp);
                if (!ok) { Info("Cet identifiant est déjà utilisé.", true); return; }
                NewNom.Text = NewIdentifiant.Text = string.Empty;
                NewMdp.Clear();
                await RafraichirAsync();
                Info("Utilisateur « " + nouvel.Username + " » créé.", false);
            }
            catch (Exception ex)
            {
                Info("Erreur : " + ex.Message, true);
            }
        }

        private async void AppliquerRole_Click(object sender, RoutedEventArgs e)
        {
            var u = Selectionne();
            if (u == null) { Info("Sélectionnez d'abord un utilisateur.", true); return; }

            UserRole nouveau = RoleDepuisIndex(SelRole.SelectedIndex);
            if (nouveau == u.Role) { Info("Le rôle est déjà celui-là.", false); return; }

            // Empêche de retirer le dernier administrateur.
            if (u.Role == UserRole.Admin && nouveau != UserRole.Admin)
            {
                int admins = await _userService.CompterAdminsAsync();
                if (admins <= 1) { Info("Impossible : il doit rester au moins un administrateur.", true); return; }
            }

            try
            {
                await _userService.UpdateUserRoleAsync(u.UserId, nouveau);
                await RafraichirAsync();
                Info("Rôle mis à jour.", false);
            }
            catch (Exception ex) { Info("Erreur : " + ex.Message, true); }
        }

        private async void Reinitialiser_Click(object sender, RoutedEventArgs e)
        {
            var u = Selectionne();
            if (u == null) { Info("Sélectionnez d'abord un utilisateur.", true); return; }
            string mdp = SelMdp.Password;
            if (mdp.Length < 6) { Info("Le nouveau mot de passe doit comporter au moins 6 caractères.", true); return; }

            try
            {
                await _userService.ReinitialiserMotDePasseAsync(u.UserId, mdp);
                SelMdp.Clear();
                Info("Mot de passe réinitialisé pour « " + u.Username + " ».", false);
            }
            catch (Exception ex) { Info("Erreur : " + ex.Message, true); }
        }

        private async void Supprimer_Click(object sender, RoutedEventArgs e)
        {
            var u = Selectionne();
            if (u == null) { Info("Sélectionnez d'abord un utilisateur.", true); return; }

            var courant = UserSession.Instance.CurrentUser;
            if (courant != null && string.Equals(courant.UserId, u.UserId, StringComparison.Ordinal))
            {
                Info("Vous ne pouvez pas supprimer votre propre compte.", true);
                return;
            }
            if (u.Role == UserRole.Admin)
            {
                int admins = await _userService.CompterAdminsAsync();
                if (admins <= 1) { Info("Impossible : il doit rester au moins un administrateur.", true); return; }
            }

            var confirmation = MessageBox.Show(this,
                "Supprimer définitivement l'utilisateur « " + (u.DisplayName ?? u.Username) + " » ?",
                "Confirmation", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (confirmation != MessageBoxResult.Yes) return;

            try
            {
                await _userService.RemoveUserAsync(u.UserId);
                await RafraichirAsync();
                Info("Utilisateur supprimé.", false);
            }
            catch (Exception ex) { Info("Erreur : " + ex.Message, true); }
        }

        private void Fermer_Click(object sender, RoutedEventArgs e) => Close();

        // ───────────────────────────────────────────── utilitaires rôle / messages

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

        private static int IndexRole(UserRole r)
        {
            switch (r)
            {
                case UserRole.ReadOnly: return 0;
                case UserRole.Editor: return 1;
                case UserRole.Admin: return 2;
                default: return 1;
            }
        }

        private static UserRole RoleDepuisIndex(int index)
        {
            switch (index)
            {
                case 0: return UserRole.ReadOnly;
                case 2: return UserRole.Admin;
                default: return UserRole.Editor;
            }
        }

        private void Info(string message, bool erreur)
        {
            MessageEtat.Text = message;
            MessageEtat.Foreground = erreur
                ? (Brush)FindResource("RedBrush")
                : (Brush)FindResource("GreenBrush");
        }
    }
}
