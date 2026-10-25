using System;
using System.IO;
using System.Windows;
using Microsoft.Win32;
using IUTSCalculator.Licensing;

namespace IUTSCalculator.Views
{
    /// <summary>
    /// Fenêtre d'activation. Affiche le code de demande du poste, permet d'importer une
    /// licence (.lic) et d'activer. Tant que l'essai est valide, l'utilisateur peut aussi
    /// poursuivre en évaluation. <see cref="Autorise"/> indique si l'application peut démarrer.
    /// </summary>
    public partial class ActivationWindow : Window
    {
        private readonly ResultatLicence _etatInitial;

        /// <summary>Vrai si, à la fermeture, l'accès à l'application est permis (activée ou essai accepté).</summary>
        public bool Autorise { get; private set; }

        public ActivationWindow(ResultatLicence etatInitial)
        {
            InitializeComponent();
            _etatInitial = etatInitial ?? LicenceService.Instance.Evaluer();

            try { ZoneCode.Text = LicenceService.Instance.CodeDemande(); }
            catch { ZoneCode.Text = "(impossible de calculer le code sur ce poste)"; }

            ConfigurerSelonEtat();
        }

        private void ConfigurerSelonEtat()
        {
            switch (_etatInitial.Etat)
            {
                case EtatActivation.Essai:
                    int jours = _etatInitial.JoursRestants ?? 0;
                    SousTitre.Text = "Vous utilisez actuellement la version d'évaluation ("
                        + Math.Max(0, jours) + " jour(s) restant(s)). "
                        + "Activez le logiciel avec une licence, ou continuez l'évaluation.";
                    BoutonEssai.Visibility = Visibility.Visible;
                    break;

                case EtatActivation.Expiree:
                    SousTitre.Text = "La période d'évaluation ou la licence a expiré. "
                        + "Une licence valide est nécessaire pour continuer.";
                    BoutonEssai.Visibility = Visibility.Collapsed;
                    AfficherMessage(_etatInitial.Message, erreur: true);
                    break;

                case EtatActivation.Invalide:
                    SousTitre.Text = "La licence installée n'est pas valide sur ce poste. "
                        + "Importez une licence correcte pour continuer.";
                    BoutonEssai.Visibility = Visibility.Collapsed;
                    AfficherMessage(_etatInitial.Message, erreur: true);
                    break;

                case EtatActivation.Activee:
                    string exp = _etatInitial.ExpirationEffective.HasValue
                        ? "valable jusqu'au " + _etatInitial.ExpirationEffective.Value.ToLocalTime().ToString("dd/MM/yyyy")
                        : "licence perpétuelle";
                    SousTitre.Text = "Ce poste est activé.";
                    BoutonEssai.Visibility = Visibility.Collapsed;
                    BoutonActiver.Content = "Remplacer la licence";
                    AfficherMessage("Logiciel activé — Titulaire : "
                        + (_etatInitial.Titulaire ?? "(non précisé)") + " — " + exp
                        + ". Pour renouveler ou changer de licence, importez un nouveau fichier .lic ci-dessous.",
                        erreur: false);
                    break;

                default: // état non activé (cas théorique)
                    SousTitre.Text = "Ce poste n'est pas encore activé. Suivez les deux étapes ci-dessous.";
                    BoutonEssai.Visibility = Visibility.Collapsed;
                    break;
            }

            if (_etatInitial.HorlogeSuspecte)
            {
                AfficherMessage("Attention : l'horloge de cet ordinateur semble avoir été reculée. "
                    + "Réglez la date et l'heure correctes avant d'activer.", erreur: true);
            }
        }

        private void Copier_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Clipboard.SetText(ZoneCode.Text);
                AfficherMessage("Code copié. Collez-le dans votre message à l'éditeur.", erreur: false);
            }
            catch
            {
                AfficherMessage("Impossible de copier automatiquement — sélectionnez le code à la main.", erreur: true);
            }
        }

        private void Importer_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                Title = "Choisir le fichier de licence",
                Filter = "Licence Sigma (*.lic)|*.lic|Tous les fichiers (*.*)|*.*"
            };
            if (dlg.ShowDialog(this) == true)
            {
                try
                {
                    ZoneLicence.Text = File.ReadAllText(dlg.FileName);
                    AfficherMessage("Fichier chargé. Cliquez sur « Activer » pour terminer.", erreur: false);
                }
                catch (Exception ex)
                {
                    AfficherMessage("Lecture impossible : " + ex.Message, erreur: true);
                }
            }
        }

        private void Activer_Click(object sender, RoutedEventArgs e)
        {
            string texte = ZoneLicence.Text?.Trim();
            if (string.IsNullOrWhiteSpace(texte))
            {
                AfficherMessage("Collez d'abord votre licence, ou importez le fichier .lic.", erreur: true);
                return;
            }

            ResultatLicence r = LicenceService.Instance.InstallerLicence(texte);
            if (r.Etat == EtatActivation.Activee)
            {
                string expire = r.ExpirationEffective.HasValue
                    ? "\nValable jusqu'au " + r.ExpirationEffective.Value.ToLocalTime().ToString("dd/MM/yyyy") + "."
                    : "\nLicence perpétuelle.";
                MessageBox.Show(this,
                    "Activation réussie.\nTitulaire : " + r.Titulaire + expire,
                    "Sigma Paie & RH", MessageBoxButton.OK, MessageBoxImage.Information);
                Autorise = true;
                DialogResult = true;
                Close();
            }
            else
            {
                AfficherMessage(r.Message ?? "La licence fournie n'a pas pu être validée.", erreur: true);
            }
        }

        private void Essai_Click(object sender, RoutedEventArgs e)
        {
            // Autorisé uniquement si l'essai est réellement encore valide (recalcul, pas de confiance au bouton).
            ResultatLicence r = LicenceService.Instance.Evaluer();
            if (r.Etat == EtatActivation.Essai)
            {
                Autorise = true;
                DialogResult = true;
                Close();
            }
            else
            {
                AfficherMessage("La période d'évaluation n'est plus disponible.", erreur: true);
                BoutonEssai.Visibility = Visibility.Collapsed;
            }
        }

        private void Quitter_Click(object sender, RoutedEventArgs e)
        {
            Autorise = false;
            DialogResult = false;
            Close();
        }

        private void AfficherMessage(string message, bool erreur)
        {
            MessageEtat.Text = message;
            MessageEtat.Foreground = erreur
                ? (System.Windows.Media.Brush)FindResource("RedBrush")
                : (System.Windows.Media.Brush)FindResource("GreenBrush");
        }
    }
}
