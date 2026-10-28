using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using IUTSCalculator.Services;
using Microsoft.Win32;

namespace IUTSCalculator.Helpers
{
    /// <summary>
    /// Dialogues de sauvegarde et de restauration des données, communs à l'écran de
    /// sélection du dossier et au menu de la fenêtre principale.
    /// </summary>
    public static class SauvegardeInteractive
    {
        private const string TitreFenetre = "Sauvegarde des données";

        private static SauvegardeService Service()
        {
            return new SauvegardeService(Constants.AppDataFolder);
        }

        /// <summary>Demande l'emplacement, sauvegarde tout, et renvoie un message d'état (null si annulé).</summary>
        public static async Task<string> SauvegarderAsync()
        {
            string dossier = SauvegardeService.DossierParDefaut();
            try { Directory.CreateDirectory(dossier); } catch { }
            var dlg = new SaveFileDialog
            {
                Title = "Sauvegarder toutes les données de Sigma Paie & RH",
                Filter = "Sauvegarde Sigma Paie & RH (*.zip)|*.zip",
                InitialDirectory = dossier,
                FileName = SauvegardeService.NomPropose(DateTime.Now)
            };
            if (dlg.ShowDialog() != true) return null;

            string motDePasse = MotDePasseDuPoste();
            if (motDePasse == null) return "Sauvegarde non faite : aucun mot de passe de sauvegarde n'est défini sur ce poste.";

            try
            {
                var service = Service();
                string chemin = dlg.FileName;
                var r = await Task.Run(() => service.Sauvegarder(chemin, motDePasse, VersionApplication.Libelle));
                var msg = new StringBuilder();
                msg.AppendFormat(CultureInfo.CurrentCulture, "Sauvegarde terminée : {0} fichier(s), dont {1} base(s) de dossier, {2:N0} Ko.\n\n{3}",
                    r.Fichiers, r.Bases, Math.Max(1, r.Octets / 1024), r.Chemin);
                if (r.Avertissements.Count > 0)
                    msg.Append("\n\nÀ noter :\n• " + string.Join("\n• ", r.Avertissements));
                msg.Append("\n\nElle est chiffrée par le mot de passe de sauvegarde, qui sera demandé pour la restaurer.");
                msg.Append("\n\nConservez une copie hors de cet ordinateur (clé USB, disque externe).");
                MessageBox.Show(msg.ToString(), TitreFenetre, MessageBoxButton.OK,
                    r.Avertissements.Count > 0 ? MessageBoxImage.Warning : MessageBoxImage.Information);
                return "Données sauvegardées : " + r.Chemin;
            }
            catch (Exception ex)
            {
                NotificationService.Instance.Error("La sauvegarde a échoué : " + ex.Message);
                return "Sauvegarde non faite : " + ex.Message;
            }
        }

        /// <summary>
        /// Choisit une sauvegarde, l'analyse, demande confirmation, sauvegarde l'état actuel,
        /// restaure puis relance le programme. Renvoie un message d'état si rien n'a été fait.
        /// </summary>
        public static async Task<string> RestaurerAsync()
        {
            if (DossierSession.Instance.HasOpenDossier)
            {
                NotificationService.Instance.Info("Un dossier est ouvert. Pour restaurer une sauvegarde, quittez le programme, relancez-le, " +
                                "puis choisissez « Restaurer une sauvegarde » avant d'ouvrir un dossier.");
                return null;
            }

            string dossier = SauvegardeService.DossierParDefaut();
            var dlg = new OpenFileDialog
            {
                Title = "Restaurer une sauvegarde de Sigma Paie & RH",
                Filter = "Sauvegarde Sigma Paie & RH (*.zip)|*.zip",
                InitialDirectory = Directory.Exists(dossier) ? dossier : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
            };
            if (dlg.ShowDialog() != true) return null;

            var service = Service();
            string chemin = dlg.FileName;
            ContenuSauvegarde contenu;
            try
            {
                contenu = await Task.Run(() => service.Analyser(chemin));
            }
            catch (Exception ex)
            {
                NotificationService.Instance.Warning(ex.Message);
                return "Restauration impossible : " + ex.Message;
            }

            // Sauvegarde protégée : le mot de passe est demandé ici, même sur le poste qui l'a faite.
            string motDePasse = null;
            for (int essai = 1; contenu.MotDePasseRequis; essai++)
            {
                motDePasse = DemandeMotDePasse.Afficher("Mot de passe de la sauvegarde",
                    "Cette sauvegarde est protégée. Saisissez le mot de passe de sauvegarde défini par l'administrateur du poste qui l'a faite" +
                    (string.IsNullOrWhiteSpace(contenu.Poste) ? "" : " (" + contenu.Poste + ")") + ".", false);
                if (motDePasse == null) return null;
                try
                {
                    string saisi = motDePasse;
                    contenu = await Task.Run(() => service.Analyser(chemin, saisi));
                }
                catch (MotDePasseSauvegardeIncorrectException ex)
                {
                    if (essai >= 3)
                    {
                        NotificationService.Instance.Warning(ex.Message);
                        return "Restauration refusée : mot de passe incorrect.";
                    }
                    MessageBox.Show(ex.Message, "Mot de passe de la sauvegarde", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
                catch (Exception ex)
                {
                    NotificationService.Instance.Warning(ex.Message);
                    return "Restauration impossible : " + ex.Message;
                }
            }

            var obstacle = SauvegardeService.Obstacle(contenu);
            if (obstacle != null)
            {
                NotificationService.Instance.Warning(obstacle);
                return "Restauration refusée : " + obstacle;
            }

            var resume = new StringBuilder();
            resume.AppendLine("Sauvegarde du " + (contenu.Date.HasValue ? contenu.Date.Value.ToString("dd/MM/yyyy à HH:mm", CultureInfo.CurrentCulture) : "?")
                              + (string.IsNullOrWhiteSpace(contenu.Poste) ? "" : " (poste " + contenu.Poste + ")")
                              + ", version " + (string.IsNullOrWhiteSpace(contenu.VersionProgramme) ? "?" : contenu.VersionProgramme) + ".");
            resume.AppendLine();
            resume.AppendLine(contenu.Dossiers.Count + " dossier(s) :");
            foreach (var d in contenu.Dossiers.Take(15)) resume.AppendLine("• " + d.Libelle);
            if (contenu.Dossiers.Count > 15) resume.AppendLine("• …");
            resume.AppendLine();
            resume.AppendLine("TOUTES les données actuelles de ce poste (dossiers, utilisateurs, réglages) seront remplacées par celles de la sauvegarde. " +
                              "Ce qui a été saisi depuis sera perdu, sauf dans la sauvegarde de sécurité de l'état actuel, faite automatiquement juste avant.");
            resume.AppendLine();
            resume.Append("Le programme redémarrera ensuite. Restaurer ?");
            if (MessageBox.Show(resume.ToString(), "Restaurer une sauvegarde", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) != MessageBoxResult.Yes)
                return null;

            // La sauvegarde de sécurité de l'état actuel est protégée par le mot de passe du poste ;
            // à défaut (poste neuf), par celui de la sauvegarde restaurée.
            string motDePasseSecurite = MotDePasseSauvegarde.Lire(Constants.AppDataFolder) ?? motDePasse ?? MotDePasseDuPoste();
            if (motDePasseSecurite == null) return "Restauration non faite : aucun mot de passe pour la sauvegarde de sécurité.";

            try
            {
                Directory.CreateDirectory(dossier);
                string securite = Path.Combine(dossier, SauvegardeService.NomPropose(DateTime.Now, "Avant_restauration"));
                string saisi = motDePasse;
                var r = await Task.Run(() => service.Restaurer(chemin, saisi, securite, motDePasseSecurite, VersionApplication.Libelle));

                // Poste sans mot de passe de sauvegarde : celui de la sauvegarde restaurée le devient.
                if (motDePasse != null && !MotDePasseSauvegarde.Existe(Constants.AppDataFolder))
                    try { MotDePasseSauvegarde.Definir(Constants.AppDataFolder, motDePasse); } catch { }
                MessageBox.Show("Restauration terminée (" + r.Fichiers + " fichier(s))." +
                                (r.SauvegardeDeSecurite != null ? "\n\nL'état précédent a été sauvegardé dans :\n" + r.SauvegardeDeSecurite : "") +
                                "\n\nLe programme va redémarrer : reconnectez-vous avec un compte de la sauvegarde.",
                    "Restauration", MessageBoxButton.OK, MessageBoxImage.Information);
                Redemarrer();
                return "Restauration terminée.";
            }
            catch (Exception ex)
            {
                MessageBox.Show("La restauration a échoué : " + ex.Message +
                                "\n\nSi la sauvegarde de sécurité a été faite, elle se trouve dans " + dossier + ".",
                    "Restauration", MessageBoxButton.OK, MessageBoxImage.Error);
                return "Restauration non faite : " + ex.Message;
            }
        }

        /// <summary>
        /// Mot de passe de sauvegarde du poste. S'il n'est pas encore défini (ou plus lisible par ce
        /// profil Windows), un administrateur le définit maintenant ; sinon null.
        /// </summary>
        private static string MotDePasseDuPoste()
        {
            string motDePasse = MotDePasseSauvegarde.Lire(Constants.AppDataFolder);
            if (motDePasse != null) return motDePasse;

            bool illisible = MotDePasseSauvegarde.Existe(Constants.AppDataFolder);
            if (!UserSession.Instance.IsAdmin)
            {
                MessageBox.Show((illisible
                        ? "Le mot de passe de sauvegarde de ce poste n'est plus lisible par ce profil Windows."
                        : "Aucun mot de passe de sauvegarde n'est encore défini sur ce poste.") +
                    "\n\nUn administrateur doit le définir en faisant une première sauvegarde.",
                    TitreFenetre, MessageBoxButton.OK, MessageBoxImage.Information);
                return null;
            }

            motDePasse = DemandeMotDePasse.Afficher("Mot de passe des sauvegardes",
                (illisible ? "Le mot de passe de sauvegarde enregistré n'est plus lisible par ce profil Windows : définissez-en un nouveau. " +
                             "Les sauvegardes déjà faites gardent leur ancien mot de passe.\n\n"
                           : "") +
                "Ce mot de passe protège toutes les sauvegardes de ce poste. Il ne sera plus demandé pour sauvegarder, " +
                "mais il le sera pour toute restauration, ici ou sur un autre poste.\n\n" +
                "Notez-le en lieu sûr : sans lui, une sauvegarde est irrécupérable.",
                true, MotDePasseSauvegarde.Valider);
            if (motDePasse == null) return null;

            try
            {
                MotDePasseSauvegarde.Definir(Constants.AppDataFolder, motDePasse);
            }
            catch (Exception ex)
            {
                NotificationService.Instance.Error("Le mot de passe de sauvegarde n'a pas pu être enregistré : " + ex.Message);
                return null;
            }
            return motDePasse;
        }

        private static void Redemarrer()
        {
            try
            {
                string exe = Process.GetCurrentProcess().MainModule.FileName;
                Process.Start(exe);
            }
            catch { /* si le relancement échoue, l'utilisateur relance lui-même */ }
            Application.Current.Shutdown();
        }
    }
}
