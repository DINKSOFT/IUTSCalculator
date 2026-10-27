using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace IUTSCalculator.Helpers
{
    /// <summary>
    /// Contrôle des chemins lus dans <c>dossiers.json</c> (au chargement et dans une sauvegarde
    /// à restaurer). Ce fichier n'est pas chiffré et peut venir d'une archive reçue : ses chemins
    /// ne sont jamais utilisés tels quels.
    /// </summary>
    public static class CheminsDossier
    {
        private static readonly string[] ExtensionsLogo = { ".png", ".jpg", ".jpeg", ".bmp", ".gif" };
        private static readonly Regex LecteurLocal = new Regex(@"^[A-Za-z]:\\", RegexOptions.CultureInvariant);

        /// <summary>
        /// Vrai si <paramref name="dbFileName"/> désigne une base <c>.db</c> rangée DANS
        /// <paramref name="racine"/> : chemin relatif, sans « .. » qui en sorte, sans lecteur ni
        /// flux NTFS (« : »). Un chemin absolu est refusé : sinon une suppression de dossier
        /// effacerait le fichier qu'il désigne, où qu'il soit.
        /// </summary>
        public static bool EstBaseValide(string dbFileName, string racine)
        {
            if (string.IsNullOrWhiteSpace(dbFileName) || string.IsNullOrWhiteSpace(racine)) return false;
            if (dbFileName.IndexOfAny(Path.GetInvalidPathChars()) >= 0 || dbFileName.Contains(":")) return false;
            if (Path.IsPathRooted(dbFileName)) return false;
            if (!string.Equals(Path.GetExtension(dbFileName), ".db", StringComparison.OrdinalIgnoreCase)) return false;
            try
            {
                string r = Path.GetFullPath(racine).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
                return Path.GetFullPath(Path.Combine(r, dbFileName)).StartsWith(r, StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>
        /// Vrai si <paramref name="logoPath"/> est vide (pas de logo) ou désigne une image sur un
        /// lecteur local (« C:\… ») avec une extension d'image. Les chemins réseau (« \\serveur\… »)
        /// sont refusés : afficher le logo y connecterait le poste et lui ferait présenter
        /// l'identité Windows de l'utilisateur.
        /// </summary>
        public static bool EstLogoValide(string logoPath)
        {
            if (string.IsNullOrEmpty(logoPath)) return true;
            if (logoPath.IndexOfAny(Path.GetInvalidPathChars()) >= 0) return false;
            if (!LecteurLocal.IsMatch(logoPath) || logoPath.IndexOf(':', 2) >= 0) return false;
            return ExtensionsLogo.Contains(Path.GetExtension(logoPath), StringComparer.OrdinalIgnoreCase);
        }
    }
}
