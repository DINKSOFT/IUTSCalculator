using System;
using System.Windows;
using IUTSCalculator.Models;
using IUTSCalculator.Services;

namespace IUTSCalculator.Views
{
    /// <summary>
    /// Écran affiché au tout premier démarrage, lorsqu'aucun utilisateur n'existe encore.
    /// Crée le compte administrateur initial. <see cref="AdminCree"/> indique le succès.
    /// </summary>
    public partial class CreerAdminWindow : Window
    {
        private readonly UserAccountService _userService;

        public bool AdminCree { get; private set; }

        public CreerAdminWindow(UserAccountService userService)
        {
            InitializeComponent();
            _userService = userService ?? throw new ArgumentNullException(nameof(userService));
        }

        private async void Creer_Click(object sender, RoutedEventArgs e)
        {
            string nom = NomAffiche.Text?.Trim();
            string identifiant = Identifiant.Text?.Trim();
            string mdp = MotDePasse.Password;
            string confirm = Confirmation.Password;

            if (string.IsNullOrWhiteSpace(identifiant))
            {
                Erreur("L'identifiant de connexion est obligatoire.");
                return;
            }
            if (mdp.Length < 6)
            {
                Erreur("Le mot de passe doit comporter au moins 6 caractères.");
                return;
            }
            if (mdp != confirm)
            {
                Erreur("Les deux mots de passe ne correspondent pas.");
                return;
            }

            BoutonCreer.IsEnabled = false;
            try
            {
                var admin = new UserAccount
                {
                    Username = identifiant,
                    DisplayName = string.IsNullOrWhiteSpace(nom) ? identifiant : nom,
                    Role = UserRole.Admin
                };
                bool ok = await _userService.AddUserAsync(admin, mdp);
                if (!ok)
                {
                    Erreur("Impossible de créer ce compte (identifiant déjà utilisé ?).");
                    BoutonCreer.IsEnabled = true;
                    return;
                }
                AdminCree = true;
                DialogResult = true;
                Close();
            }
            catch (Exception ex)
            {
                Erreur("Erreur lors de la création : " + ex.Message);
                BoutonCreer.IsEnabled = true;
            }
        }

        private void Quitter_Click(object sender, RoutedEventArgs e)
        {
            AdminCree = false;
            DialogResult = false;
            Close();
        }

        private void Erreur(string message)
        {
            MessageEtat.Text = message;
        }
    }
}
