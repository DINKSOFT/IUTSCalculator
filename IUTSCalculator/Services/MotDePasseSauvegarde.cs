using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace IUTSCalculator.Services
{
    /// <summary>
    /// Mot de passe des sauvegardes de ce poste : défini une fois par un administrateur, conservé
    /// dans <c>mot-de-passe-sauvegarde.dat</c> protégé par DPAPI (profil Windows courant), puis
    /// utilisé sans le redemander à chaque sauvegarde. Il n'est redemandé qu'à la restauration,
    /// qui peut avoir lieu sur un autre poste. Le fichier n'est jamais sauvegardé ni restauré.
    /// </summary>
    public static class MotDePasseSauvegarde
    {
        public const string NomFichier = "mot-de-passe-sauvegarde.dat";
        public const int LongueurMin = 8;

        private static readonly byte[] Entropie = Encoding.UTF8.GetBytes("Sigma Paie & RH — mot de passe des sauvegardes v1");

        public static string Chemin(string dossierDonnees)
        {
            return Path.Combine(dossierDonnees, NomFichier);
        }

        public static bool Existe(string dossierDonnees)
        {
            return File.Exists(Chemin(dossierDonnees));
        }

        /// <summary>Raison pour laquelle ce mot de passe est refusé, ou null.</summary>
        public static string Valider(string motDePasse)
        {
            if (string.IsNullOrEmpty(motDePasse) || motDePasse.Length < LongueurMin)
                return "Le mot de passe de sauvegarde doit compter au moins " + LongueurMin + " caractères.";
            if (motDePasse.Trim().Length != motDePasse.Length)
                return "Le mot de passe de sauvegarde ne doit pas commencer ni finir par un espace.";
            return null;
        }

        public static void Definir(string dossierDonnees, string motDePasse)
        {
            string erreur = Valider(motDePasse);
            if (erreur != null) throw new ArgumentException(erreur);

            Directory.CreateDirectory(dossierDonnees);
            byte[] clair = Encoding.UTF8.GetBytes(motDePasse);
            try
            {
                string chemin = Chemin(dossierDonnees);
                string temp = chemin + "." + Guid.NewGuid().ToString("N") + ".tmp";
                File.WriteAllBytes(temp, ProtectedData.Protect(clair, Entropie, DataProtectionScope.CurrentUser));
                if (File.Exists(chemin)) File.Replace(temp, chemin, null);
                else File.Move(temp, chemin);
            }
            finally
            {
                Array.Clear(clair, 0, clair.Length);
            }
        }

        /// <summary>Mot de passe enregistré, ou null s'il n'a jamais été défini (ou n'est plus lisible par ce profil).</summary>
        public static string Lire(string dossierDonnees)
        {
            string chemin = Chemin(dossierDonnees);
            if (!File.Exists(chemin)) return null;
            byte[] clair = null;
            try
            {
                clair = ProtectedData.Unprotect(File.ReadAllBytes(chemin), Entropie, DataProtectionScope.CurrentUser);
                return Encoding.UTF8.GetString(clair);
            }
            catch (Exception ex) when (ex is CryptographicException || ex is IOException || ex is UnauthorizedAccessException)
            {
                return null;
            }
            finally
            {
                if (clair != null) Array.Clear(clair, 0, clair.Length);
            }
        }
    }
}
