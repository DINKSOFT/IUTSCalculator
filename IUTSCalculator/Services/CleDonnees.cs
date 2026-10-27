using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace IUTSCalculator.Services
{
    /// <summary>
    /// Clé de chiffrement des bases des dossiers, propre à ce poste.
    ///
    /// La clé (256 bits tirés au hasard) est conservée dans <c>cle-donnees.dat</c>, protégée par
    /// DPAPI pour le profil Windows courant, comme <c>users.json</c> : le fichier copié sur un
    /// autre poste ou lu par un autre compte ne révèle rien. Elle ne fait donc PAS partie des
    /// sauvegardes : celles-ci sont protégées par le mot de passe de sauvegarde, et la
    /// restauration rechiffre les bases avec la clé du poste qui les reçoit.
    ///
    /// Une clé présente mais illisible n'est JAMAIS remplacée : une nouvelle clé rendrait les
    /// bases existantes définitivement inaccessibles. L'appelant reçoit alors
    /// <see cref="CleDonneesIllisibleException"/>, et le recours est la restauration d'une sauvegarde.
    /// </summary>
    public static class CleDonnees
    {
        public const string NomFichier = "cle-donnees.dat";

        private const int TailleCle = 32;
        private static readonly byte[] Signature = Encoding.ASCII.GetBytes("SIGK1");
        private static readonly byte[] Entropie = Encoding.UTF8.GetBytes("Sigma Paie & RH — clé des bases v1");
        private static readonly object Verrou = new object();

        public static string Chemin(string dossierDonnees)
        {
            return Path.Combine(dossierDonnees, NomFichier);
        }

        public static bool Existe(string dossierDonnees)
        {
            return File.Exists(Chemin(dossierDonnees));
        }

        /// <summary>Clé du poste en hexadécimal (64 caractères), créée au premier appel.</summary>
        public static string ObtenirOuCreer(string dossierDonnees)
        {
            lock (Verrou)
            {
                if (!Existe(dossierDonnees))
                    Creer(dossierDonnees);
                return Lire(dossierDonnees);
            }
        }

        /// <summary>Clé du poste en hexadécimal ; lève <see cref="CleDonneesIllisibleException"/> si elle ne peut être lue.</summary>
        public static string Lire(string dossierDonnees)
        {
            string chemin = Chemin(dossierDonnees);
            byte[] clair = null;
            try
            {
                clair = ProtectedData.Unprotect(File.ReadAllBytes(chemin), Entropie, DataProtectionScope.CurrentUser);
                if (clair.Length != Signature.Length + TailleCle || !clair.Take(Signature.Length).SequenceEqual(Signature))
                    throw new CryptographicException("contenu inattendu");
                var sb = new StringBuilder(TailleCle * 2);
                for (int i = Signature.Length; i < clair.Length; i++)
                    sb.Append(clair[i].ToString("x2"));
                return sb.ToString();
            }
            catch (Exception ex) when (ex is CryptographicException || ex is IOException || ex is UnauthorizedAccessException)
            {
                throw new CleDonneesIllisibleException(chemin, ex);
            }
            finally
            {
                if (clair != null) Array.Clear(clair, 0, clair.Length);
            }
        }

        private static void Creer(string dossierDonnees)
        {
            Directory.CreateDirectory(dossierDonnees);
            var clair = new byte[Signature.Length + TailleCle];
            try
            {
                Buffer.BlockCopy(Signature, 0, clair, 0, Signature.Length);
                using (var rng = RandomNumberGenerator.Create())
                {
                    var cle = new byte[TailleCle];
                    rng.GetBytes(cle);
                    Buffer.BlockCopy(cle, 0, clair, Signature.Length, TailleCle);
                    Array.Clear(cle, 0, cle.Length);
                }

                // Écriture dans un fichier temporaire puis déplacement : jamais de clé à moitié écrite,
                // et File.Move refuse d'écraser une clé créée entre-temps.
                string chemin = Chemin(dossierDonnees);
                string temp = chemin + "." + Guid.NewGuid().ToString("N") + ".tmp";
                File.WriteAllBytes(temp, ProtectedData.Protect(clair, Entropie, DataProtectionScope.CurrentUser));
                try { File.Move(temp, chemin); }
                catch (IOException) when (File.Exists(chemin)) { File.Delete(temp); }
            }
            finally
            {
                Array.Clear(clair, 0, clair.Length);
            }
        }
    }

    /// <summary>La clé des bases existe mais ne peut être lue (autre profil Windows, autre poste, fichier abîmé).</summary>
    public class CleDonneesIllisibleException : InvalidOperationException
    {
        public CleDonneesIllisibleException(string chemin, Exception cause)
            : base("La clé de chiffrement des données de ce poste est illisible (" + chemin + "). " +
                   "Elle n'est utilisable que par le compte Windows qui l'a créée, sur ce poste. " +
                   "Ouvrez la session Windows habituelle, ou restaurez une sauvegarde avec son mot de passe. " +
                   "Le fichier n'a pas été modifié.", cause)
        {
            Chemin = chemin;
        }

        public string Chemin { get; }
    }
}
