using System;
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

            // Profil « Lecture seule » : toute écriture en base est refusée (défaut n° 10).
            Services.GardeLectureSeule.Activer();

            var cmdArgs = System.Environment.GetCommandLineArgs();
            if (cmdArgs != null && cmdArgs.Length > 1 && cmdArgs[1] == "--run-tests-rh02")
            {
                int exitCode = 0;
                string resultFile = System.IO.Path.Combine(System.AppDomain.CurrentDomain.BaseDirectory, "test_rh02_result.txt");
                try
                {
                    string testsDll = System.IO.Path.Combine(System.AppDomain.CurrentDomain.BaseDirectory, "IUTSCalculator_Tests.dll");
                    if (!System.IO.File.Exists(testsDll))
                    {
                        testsDll = System.IO.Path.GetFullPath(System.IO.Path.Combine(System.AppDomain.CurrentDomain.BaseDirectory, @"..\..\..\IUTSCalculator_Tests\bin\Debug\IUTSCalculator_Tests.dll"));
                    }

                    var testAsm = System.Reflection.Assembly.LoadFrom(testsDll);
                    var testClass = testAsm.GetType("IUTSCalculator.Tests.FichePosteTests");
                    var instance = System.Activator.CreateInstance(testClass);
                    var log = new System.Text.StringBuilder();

                    foreach (var m in testClass.GetMethods())
                    {
                        if (m.Name.Contains("Calculer") || m.Name.Contains("Competences") || m.Name.Contains("Obtenir") || m.Name.Contains("Exports"))
                        {
                            var res = m.Invoke(instance, null);
                            if (res is System.Threading.Tasks.Task t)
                            {
                                t.GetAwaiter().GetResult();
                            }
                            log.AppendLine("PASSED: " + m.Name);
                        }
                    }

                    log.AppendLine("ALL_TESTS_PASSED");
                    System.IO.File.WriteAllText(resultFile, log.ToString());
                }
                catch (System.Exception ex)
                {
                    exitCode = 1;
                    var inner = ex.InnerException ?? ex;
                    System.IO.File.WriteAllText(resultFile, "FAILED: " + inner.Message + "\n" + inner.StackTrace);
                }
                finally
                {
                    Shutdown(exitCode);
                }
                return;
            }

            if (!ActiverChiffrementDesBases())
            {
                Shutdown();
                return;
            }

            // ─────────────────────────────────────────────────────────────────────
            //  CONTRÔLE DE LICENCE (module d'activation)
            //  L'application ne démarre que si elle est activée ou en période
            //  d'évaluation valide. Sinon, la fenêtre d'activation est présentée.
            // ─────────────────────────────────────────────────────────────────────
            if (!VerifierLicenceAuDemarrage())
            {
                Shutdown();
                return;
            }

            // La garde d'intégrité ferme proprement l'application via le Dispatcher WPF
            // si un contrôle de licence échoue en cours d'exécution (défense anti-altération).
            Licensing.GardeIntegrite.Fermeture = () =>
            {
                try { Dispatcher.Invoke(() => Shutdown()); }
                catch { Environment.Exit(0); }
            };

            // ─────────────────────────────────────────────────────────────────────
            //  AUTHENTIFICATION DES UTILISATEURS
            //  Au premier démarrage (aucun compte), on crée le compte administrateur.
            //  Ensuite, l'utilisateur doit se connecter avant d'ouvrir un dossier.
            // ─────────────────────────────────────────────────────────────────────
            var userService = new Services.UserAccountService(Helpers.Constants.AppDataFolder);
            var comptes = userService.LoadUsersAsync().GetAwaiter().GetResult();
            if (comptes == null || comptes.Count == 0)
            {
                // Un fichier de comptes qui EXISTE mais ne se lit pas n'est pas un premier lancement :
                // créer un administrateur réécrirait le fichier. On prévient, copie à l'appui.
                if (userService.FichierIllisible)
                {
                    string copie = userService.SauvegardeFichierIllisible;
                    var choix = MessageBox.Show(
                        "Le fichier des comptes utilisateurs existe mais ne peut pas être lu par ce profil Windows " +
                        "(fichier abîmé, ou créé sous un autre profil ou une autre machine).\n\n" +
                        (copie != null ? "Une copie a été conservée : " + copie + "\n\n" : "") +
                        "Créer maintenant un nouvel administrateur remplacerait ce fichier. " +
                        "Voulez-vous quand même continuer ?",
                        "Comptes utilisateurs illisibles", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
                    if (choix != MessageBoxResult.Yes)
                    {
                        Shutdown();
                        return;
                    }
                }

                var creation = new Views.CreerAdminWindow(userService);
                if (creation.ShowDialog() != true)
                {
                    Shutdown();
                    return;
                }
            }

            var loginWindow = new Views.LoginWindow(userService);
            if (loginWindow.ShowDialog() != true || !Services.UserSession.Instance.IsAuthenticated)
            {
                Shutdown();
                return;
            }

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

        /// <summary>
        /// Chiffre les bases encore en clair avec la clé du poste, puis la branche pour toutes les
        /// connexions. Renvoie false si la clé est illisible ou absente : aucune clé n'est alors
        /// régénérée, car elle rendrait les bases chiffrées définitivement inaccessibles.
        /// </summary>
        private bool ActiverChiffrementDesBases()
        {
            string dossier = Helpers.Constants.AppDataFolder;
            string cle;
            try
            {
                cle = Services.ChiffrementBases.ObtenirCleDuPoste(dossier);
            }
            catch (Exception ex) when (ex is Services.CleDonneesIllisibleException || ex is Services.CleDonneesAbsenteException)
            {
                MessageBox.Show(ex.Message, "Clé de chiffrement des données", MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }

            var resultat = Services.ChiffrementBases.ChiffrerDossierDonnees(dossier, cle);
            Services.SQLiteService.FournisseurCle = () => cle;

            // Une base restée en clair n'est pas perdue : son ouverture retentera le chiffrement.
            if (resultat.Echecs.Count > 0)
                MessageBox.Show(
                    "Certaines bases n'ont pas pu être chiffrées ; elles restent intactes, en clair :\n\n• " +
                    string.Join("\n• ", resultat.Echecs),
                    "Chiffrement des données", MessageBoxButton.OK, MessageBoxImage.Warning);
            return true;
        }

        /// <summary>
        /// Évalue la licence au démarrage. Renvoie true si l'accès est permis (licence
        /// active ou essai valide). Sinon ouvre la fenêtre d'activation et renvoie son verdict.
        /// </summary>
        private bool VerifierLicenceAuDemarrage()
        {
            try
            {
                var etat = Licensing.LicenceService.Instance.Evaluer();

                // Licence pleinement active : démarrage direct, sans fenêtre.
                if (etat.Etat == Licensing.EtatActivation.Activee)
                    return true;

                // Tout autre cas (évaluation en cours, expirée, non activée, invalide) :
                // on présente la fenêtre d'activation. En période d'évaluation valide, elle
                // propose « Continuer en évaluation » ; l'activation y est donc toujours visible.
                var fenetre = new Views.ActivationWindow(etat);
                fenetre.ShowDialog();
                return fenetre.Autorise;
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "Le contrôle de licence a rencontré une erreur : " + ex.Message,
                    "Sigma Paie & RH", MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
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