using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using IUTSCalculator.Services;
using IUTSCalculator.Views;
using System.Windows.Controls;

namespace IUTSCalculator
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // Initialisation du service utilisateur
            string appDataFolder = Path.Combine(
                System.Environment.GetFolderPath(System.Environment.SpecialFolder.ApplicationData),
                "IUTSCalculator");
            Directory.CreateDirectory(appDataFolder);
            var userService = new UserAccountService(appDataFolder);

            // Création d'un admin par défaut si aucun utilisateur
            var users = userService.LoadUsersAsync().Result;
            if (!users.Any())
            {
                userService.AddUserAsync(new Models.UserAccount
                {
                    Username = "admin",
                    DisplayName = "Administrateur",
                    Role = Models.UserRole.Admin
                }, "admin123").Wait();
            }

            // Affichage de la fenêtre de connexion
            var loginWindow = new LoginWindow(userService);
            var result = loginWindow.ShowDialog();

            if (result == true && UserSession.Instance.IsAuthenticated)
            {
                // Fermer la fenêtre de login avant d'ouvrir la fenêtre principale
                loginWindow.Close();

                var mainWindow = new MainWindow();
                this.MainWindow = mainWindow; // Définir la fenêtre principale
                mainWindow.Show();
            }
            else
            {
                Shutdown();
            }
        }

        private void TextBox_GotFocus_ClearIfZero(object sender, RoutedEventArgs e)
        {
            if (sender is TextBox tb)
            {
                var text = tb.Text?.Trim();
                if (text == "0" || text == "0,00" || text == "0.00")
                {
                    tb.Clear();
                }
                else if (decimal.TryParse(text, NumberStyles.Any, CultureInfo.CurrentCulture, out decimal value) && value == 0)
                {
                    tb.Clear();
                }
            }
        }
    }
}