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
    ///
    /// Une seule fiche sert à créer et à modifier un compte : sélectionner une ligne de la
    /// liste remplit la fiche (nom affiché, identifiant, rôle) et la passe en modification ;
    /// « Nouvel utilisateur » la vide pour une création. Jusqu'ici, la sélection ne
    /// remplissait que le petit panneau « Utilisateur sélectionné », placé sous le
    /// formulaire de création et coupé par le bas de la fenêtre : on avait l'impression que
    /// rien ne se passait, et le nom affiché comme l'identifiant ne se modifiaient pas.
    ///
    /// Garde-fous : identifiant unique, mot de passe de 6 caractères au moins et confirmé,
    /// pas de suppression de son propre compte, toujours au moins un administrateur.
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

        /// <summary>Compte affiché dans la fiche ; null = fiche de création.</summary>
        private UserAccount _enModification;

        /// <summary>Vrai pendant un rechargement de la liste : la sélection n'y est pas un choix de l'utilisateur.</summary>
        private bool _rechargementEnCours;

        public UserManagementWindow(UserAccountService userService)
        {
            InitializeComponent();
            _userService = userService ?? throw new ArgumentNullException(nameof(userService));
            Loaded += async (s, e) =>
            {
                await RafraichirAsync(null);
                PasserEnCreation();
            };
        }

        /// <summary>
        /// Recharge la liste depuis le fichier des comptes ; resélectionne le compte
        /// <paramref name="idASelectionner"/> s'il est fourni et existe encore.
        /// </summary>
        private async Task RafraichirAsync(string idASelectionner)
        {
            _utilisateurs = await _userService.LoadUsersAsync();
            var lignes = _utilisateurs
                .OrderBy(u => u.DisplayName ?? u.Username, StringComparer.CurrentCultureIgnoreCase)
                .Select(u => new LigneUtilisateur
                {
                    Nom = u.DisplayName,
                    Identifiant = u.Username,
                    RoleLibelle = LibelleRole(u.Role),
                    Source = u
                })
                .ToList();

            _rechargementEnCours = true;
            try
            {
                GrilleUtilisateurs.ItemsSource = lignes;
                var aSelectionner = idASelectionner == null
                    ? null
                    : lignes.FirstOrDefault(l => string.Equals(l.Source.UserId, idASelectionner, StringComparison.Ordinal));
                GrilleUtilisateurs.SelectedItem = aSelectionner;
                if (aSelectionner != null)
                    GrilleUtilisateurs.ScrollIntoView(aSelectionner);
            }
            finally
            {
                _rechargementEnCours = false;
            }
        }

        private void Grille_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_rechargementEnCours) return;

            var u = (GrilleUtilisateurs.SelectedItem as LigneUtilisateur)?.Source;
            if (u == null)
                PasserEnCreation();
            else
                PasserEnModification(u);
        }

        /// <summary>Remplit la fiche avec le compte choisi et la passe en modification.</summary>
        private void PasserEnModification(UserAccount u)
        {
            _enModification = u;
            TitreFiche.Text = "Modifier l'utilisateur";
            AideFiche.Text = EstCompteCourant(u)
                ? "C'est votre propre compte. Modifiez les champs, puis cliquez sur « Enregistrer les modifications »."
                : "Modifiez les champs, puis cliquez sur « Enregistrer les modifications ».";
            FicheNom.Text = u.DisplayName ?? string.Empty;
            FicheIdentifiant.Text = u.Username ?? string.Empty;
            FicheRole.SelectedIndex = IndexRole(u.Role);
            LibelleMdp.Text = "Nouveau mot de passe (laisser vide pour conserver l'actuel)";
            FicheMdp.Clear();
            FicheMdpConfirmation.Clear();
            BoutonEnregistrer.Content = "Enregistrer les modifications";
            BoutonSupprimer.Visibility = Visibility.Visible;
            MessageEtat.Text = string.Empty;
        }

        /// <summary>Vide la fiche pour la création d'un compte.</summary>
        private void PasserEnCreation()
        {
            _enModification = null;
            TitreFiche.Text = "Nouvel utilisateur";
            AideFiche.Text = "Remplissez la fiche, puis cliquez sur « Ajouter l'utilisateur ».";
            FicheNom.Text = string.Empty;
            FicheIdentifiant.Text = string.Empty;
            FicheRole.SelectedIndex = 1;
            LibelleMdp.Text = "Mot de passe (6 caractères min.)";
            FicheMdp.Clear();
            FicheMdpConfirmation.Clear();
            BoutonEnregistrer.Content = "Ajouter l'utilisateur";
            BoutonSupprimer.Visibility = Visibility.Collapsed;
        }

        private void Nouveau_Click(object sender, RoutedEventArgs e)
        {
            _rechargementEnCours = true;
            try { GrilleUtilisateurs.SelectedItem = null; }
            finally { _rechargementEnCours = false; }
            PasserEnCreation();
            MessageEtat.Text = string.Empty;
            FicheNom.Focus();
        }

        private async void Enregistrer_Click(object sender, RoutedEventArgs e)
        {
            string nom = FicheNom.Text?.Trim() ?? string.Empty;
            string identifiant = FicheIdentifiant.Text?.Trim() ?? string.Empty;
            string mdp = FicheMdp.Password ?? string.Empty;
            UserRole role = RoleDepuisIndex(FicheRole.SelectedIndex);

            if (identifiant.Length == 0) { Info("L'identifiant est obligatoire.", true); return; }
            if (!string.Equals(mdp, FicheMdpConfirmation.Password ?? string.Empty, StringComparison.Ordinal))
            {
                Info("Les deux mots de passe saisis ne sont pas identiques.", true);
                return;
            }

            try
            {
                if (_enModification == null)
                    await CreerAsync(nom, identifiant, role, mdp);
                else
                    await ModifierAsync(_enModification, nom, identifiant, role, mdp);
            }
            catch (Exception ex)
            {
                Info("Erreur : " + ex.Message, true);
            }
        }

        private async Task CreerAsync(string nom, string identifiant, UserRole role, string mdp)
        {
            if (mdp.Length < 6) { Info("Le mot de passe doit comporter au moins 6 caractères.", true); return; }

            var nouvel = new UserAccount
            {
                Username = identifiant,
                DisplayName = nom.Length == 0 ? identifiant : nom,
                Role = role
            };

            bool ok = await _userService.AddUserAsync(nouvel, mdp);
            if (!ok) { Info("Cet identifiant est déjà utilisé.", true); return; }

            await RafraichirAsync(nouvel.UserId);
            PasserEnModification(_utilisateurs.First(u => u.UserId == nouvel.UserId));
            Info("Utilisateur « " + nouvel.Username + " » créé.", false);
        }

        private async Task ModifierAsync(UserAccount u, string nom, string identifiant, UserRole role, string mdp)
        {
            bool soiMeme = EstCompteCourant(u);

            if (soiMeme && u.Role == UserRole.Admin && role != UserRole.Admin)
            {
                // Dernier administrateur : refus immédiat, sans poser d'abord une question
                // de confirmation dont la réponse « Oui » mènerait de toute façon au refus.
                if (await _userService.CompterAdminsAsync() <= 1)
                {
                    Info("Impossible : il doit rester au moins un administrateur.", true);
                    return;
                }

                var reponse = MessageBox.Show(this,
                    "Vous allez retirer le rôle Administrateur à votre propre compte. Une fois cette fenêtre " +
                    "fermée, vous ne pourrez plus gérer les utilisateurs.\n\nContinuer ?",
                    "Votre propre rôle", MessageBoxButton.YesNo, MessageBoxImage.Warning);
                if (reponse != MessageBoxResult.Yes) return;
            }

            string refus = await _userService.ModifierUtilisateurAsync(u.UserId, nom, identifiant, role, mdp);
            if (refus != null) { Info(refus, true); return; }

            // Le compte connecté est modifié : la session suit tout de suite (nom affiché,
            // identifiant, rôle) ; le menu de la fenêtre principale est rafraîchi à la fermeture.
            if (soiMeme)
            {
                var courant = UserSession.Instance.CurrentUser;
                courant.Username = identifiant;
                courant.DisplayName = nom.Length == 0 ? identifiant : nom;
                courant.Role = role;
            }

            await RafraichirAsync(u.UserId);
            var aJour = _utilisateurs.FirstOrDefault(x => x.UserId == u.UserId);
            if (aJour != null) PasserEnModification(aJour);

            string message = "Utilisateur « " + identifiant + " » mis à jour";
            if (mdp.Length > 0) message += ", mot de passe changé";
            message += ".";
            if (!soiMeme && role != u.Role)
                message += " Le nouveau rôle s'applique à sa prochaine connexion.";
            Info(message, false);
        }

        private async void Supprimer_Click(object sender, RoutedEventArgs e)
        {
            var u = _enModification;
            if (u == null) { Info("Sélectionnez d'abord un utilisateur.", true); return; }

            if (EstCompteCourant(u))
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
                await RafraichirAsync(null);
                PasserEnCreation();
                Info("Utilisateur supprimé.", false);
            }
            catch (Exception ex) { Info("Erreur : " + ex.Message, true); }
        }

        private void Fermer_Click(object sender, RoutedEventArgs e) => Close();

        // ───────────────────────────────────────────── utilitaires rôle / messages

        private static bool EstCompteCourant(UserAccount u)
        {
            var courant = UserSession.Instance.CurrentUser;
            return courant != null && u != null && string.Equals(courant.UserId, u.UserId, StringComparison.Ordinal);
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
