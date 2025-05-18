using System.Linq;
using System.Windows;
using IUTSCalculator.Services;

namespace IUTSCalculator.Views
{
    public partial class PasswordResetWindow : Window
    {
        private readonly UserAccountService _userService;

        public PasswordResetWindow(UserAccountService userService, string username)
        {
            InitializeComponent();
            _userService = userService;
            UsernameBox.Text = username ?? "";
        }

        private async void ResetButton_Click(object sender, RoutedEventArgs e)
        {
            var username = UsernameBox.Text.Trim();
            var newPassword = NewPasswordBox.Password;
            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(newPassword))
            {
                StatusText.Text = "Veuillez remplir tous les champs.";
                return;
            }

            var users = await _userService.LoadUsersAsync();
            var user = users.FirstOrDefault(u => u.Username == username);
            if (user == null)
            {
                StatusText.Text = "Utilisateur introuvable.";
                return;
            }

            user.PasswordHash = typeof(UserAccountService)
                .GetMethod("HashPassword", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                .Invoke(_userService, new object[] { newPassword }) as string;

            await _userService.SaveUsersAsync(users);
            StatusText.Text = "Mot de passe réinitialisé.";
        }
    }
}