using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Threading;

namespace IUTSCalculator
{
    public partial class App : Application
    {

        public App()
        {
            // Définir la culture française pour toute l'application (affichage, formats, etc.)
            var culture = new CultureInfo("fr-FR");
            Thread.CurrentThread.CurrentCulture = culture;
            Thread.CurrentThread.CurrentUICulture = culture;
            FrameworkElement.LanguageProperty.OverrideMetadata(
                typeof(FrameworkElement),
                new FrameworkPropertyMetadata(
                    System.Windows.Markup.XmlLanguage.GetLanguage("fr-FR")));

            // Attention : ne PAS tenter le même OverrideMetadata pour
            // FrameworkContentElement. Language y est déjà inscrite avec ses propres
            // métadonnées, et l'appel lève « PropertyMetadata est déjà inscrit pour le
            // type 'FrameworkContentElement' » dès le constructeur — l'application ne
            // démarre plus.
            //
            // Conséquence à connaître : un <Run> est un FrameworkContentElement et reste
            // donc en culture en-US. Un montant mis en forme dans un Run s'affiche
            // « 2,120,686 » au lieu de « 2 120 686 ». La parade est locale : ajouter
            // ConverterCulture=fr-FR à la liaison concernée.
        }
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // Avant toute chose, l'utilisateur doit ouvrir un dossier (une entreprise) :
            // c'est ce dossier qui détermine quelle base de données (employés, calculs)
            // sera utilisée pendant toute la session, afin d'éviter tout mélange entre
            // les salariés de différentes entreprises.
            var selectionWindow = new Views.DossierSelectionWindow();
            bool? result = selectionWindow.ShowDialog();

            if (result == true && Services.DossierSession.Instance.HasOpenDossier)
            {
                var mainWindow = new MainWindow();
                this.MainWindow = mainWindow;
                mainWindow.Show();
            }
            else
            {
                Shutdown();
            }
        }

        // Ancien flux de connexion (identification utilisateur), conservé pour référence :
        // il n'est plus utilisé, l'ouverture de dossier ayant pris la place de l'écran
        // de démarrage. Pour le réactiver, enchaîner cet écran après OnStartup ci-dessus.
        //
        // string appDataFolder = Path.Combine(
        //     System.Environment.GetFolderPath(System.Environment.SpecialFolder.ApplicationData),
        //     "IUTSCalculator");
        // Directory.CreateDirectory(appDataFolder);
        // var userService = new UserAccountService(appDataFolder);
        // var users = userService.LoadUsersAsync().Result;
        // if (!users.Any())
        // {
        //     userService.AddUserAsync(new Models.UserAccount
        //     {
        //         Username = "admin",
        //         DisplayName = "Administrateur",
        //         Role = Models.UserRole.Admin
        //     }, "admin123").Wait();
        // }
        // var loginWindow = new LoginWindow(userService);
        // var loginResult = loginWindow.ShowDialog();
        // if (loginResult == true && UserSession.Instance.IsAuthenticated) { loginWindow.Close(); }
        // else { Shutdown(); }

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