using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace IUTSCalculator.Helpers
{
    /// <summary>
    /// Petite fenêtre de saisie d'un mot de passe (avec confirmation facultative), construite en code
    /// pour les dialogues de sauvegarde. Renvoie null si l'utilisateur annule.
    /// </summary>
    public static class DemandeMotDePasse
    {
        /// <param name="valider">Contrôle du mot de passe saisi : message d'erreur, ou null s'il est accepté.</param>
        public static string Afficher(string titre, string message, bool confirmer, Func<string, string> valider = null)
        {
            var fenetre = new Window
            {
                Title = titre,
                Width = 440,
                SizeToContent = SizeToContent.Height,
                ResizeMode = ResizeMode.NoResize,
                WindowStartupLocation = WindowStartupLocation.CenterScreen,
                ShowInTaskbar = false
            };
            var proprietaire = Application.Current?.Windows.Count > 0 ? Application.Current.MainWindow : null;
            if (proprietaire != null && proprietaire.IsVisible)
            {
                fenetre.Owner = proprietaire;
                fenetre.WindowStartupLocation = WindowStartupLocation.CenterOwner;
            }

            var pile = new StackPanel { Margin = new Thickness(16) };
            pile.Children.Add(new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12) });

            pile.Children.Add(new TextBlock { Text = "Mot de passe :" });
            var saisie = new PasswordBox { Margin = new Thickness(0, 4, 0, 8) };
            pile.Children.Add(saisie);

            PasswordBox confirmation = null;
            if (confirmer)
            {
                pile.Children.Add(new TextBlock { Text = "Confirmation :" });
                confirmation = new PasswordBox { Margin = new Thickness(0, 4, 0, 8) };
                pile.Children.Add(confirmation);
            }

            var erreur = new TextBlock { Foreground = System.Windows.Media.Brushes.Firebrick, TextWrapping = TextWrapping.Wrap, Visibility = Visibility.Collapsed };
            pile.Children.Add(erreur);

            var boutons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
            var ok = new Button { Content = "Valider", IsDefault = true, MinWidth = 90, Margin = new Thickness(0, 0, 8, 0) };
            var annuler = new Button { Content = "Annuler", IsCancel = true, MinWidth = 90 };
            boutons.Children.Add(ok);
            boutons.Children.Add(annuler);
            pile.Children.Add(boutons);
            fenetre.Content = pile;

            string resultat = null;
            ok.Click += (s, e) =>
            {
                string refus = string.IsNullOrEmpty(saisie.Password) ? "Saisissez le mot de passe." : valider?.Invoke(saisie.Password);
                if (refus == null && confirmation != null && confirmation.Password != saisie.Password) refus = "Les deux saisies ne correspondent pas.";
                if (refus != null)
                {
                    erreur.Text = refus;
                    erreur.Visibility = Visibility.Visible;
                    return;
                }
                resultat = saisie.Password;
                fenetre.DialogResult = true;
            };
            fenetre.Loaded += (s, e) => Keyboard.Focus(saisie);

            return fenetre.ShowDialog() == true ? resultat : null;
        }
    }
}
