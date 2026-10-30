using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.Data.Sqlite;

namespace IUTSCalculator.Services
{
    /// <summary>Bilan du chiffrement des bases au démarrage.</summary>
    public class ResultatChiffrement
    {
        /// <summary>Bases en clair désormais chiffrées.</summary>
        public List<string> Chiffrees { get; } = new List<string>();

        /// <summary>Bases restées en clair, avec la raison de l'échec (le fichier n'a pas été modifié).</summary>
        public List<string> Echecs { get; } = new List<string>();
    }

    /// <summary>Avancement du chiffrement : bases déjà traitées sur le total à chiffrer, et base en cours.</summary>
    public class AvancementChiffrement
    {
        public AvancementChiffrement(int traitees, int total, string baseEnCours)
        {
            Traitees = traitees;
            Total = total;
            Base = baseEnCours;
        }

        public int Traitees { get; }
        public int Total { get; }

        /// <summary>Base en cours de chiffrement ; null une fois toutes les bases traitées.</summary>
        public string Base { get; }
    }

    /// <summary>
    /// Chiffrement des bases existantes (créées en clair par les versions précédentes) avec la
    /// clé du poste (<see cref="CleDonnees"/>).
    ///
    /// Même principe que la mise à niveau du schéma : le chiffrement se fait sur une COPIE,
    /// contrôlée (intégrité, contenu identique, lecture avec la clé, refus sans clé), puis
    /// substituée à l'original en une opération du système de fichiers. Une erreur laisse la
    /// base exactement telle qu'elle était. Une base déjà chiffrée n'est pas touchée.
    ///
    /// Les copies « avant mise à niveau » (<c>.avant-vNN.bak</c>) sont des bases complètes : elles
    /// sont chiffrées de la même façon, sans quoi elles laisseraient les données en clair à côté.
    /// </summary>
    public static class ChiffrementBases
    {
        /// <summary>Suffixe de la copie de travail du chiffrement (exclue des sauvegardes).</summary>
        public const string SuffixeCopie = ".chiffrement.tmp";

        /// <summary>Suffixe de la copie de sécurité, retirée une fois la base chiffrée contrôlée.</summary>
        public const string SuffixeSecurite = ".avant-chiffrement.bak";

        private static readonly byte[] EnteteSqlite = Encoding.ASCII.GetBytes("SQLite format 3\0");

        /// <summary>
        /// Clé du poste pour ce dossier de données, créée au premier lancement. Refuse d'en créer
        /// une nouvelle si des bases sont déjà chiffrées (fichier de clé supprimé ou déplacé) : elle
        /// ne les ouvrirait pas, et masquerait la perte de l'ancienne.
        /// </summary>
        public static string ObtenirCleDuPoste(string dossierDonnees)
        {
            if (!CleDonnees.Existe(dossierDonnees))
            {
                string chiffree = BasesDuDossier(dossierDonnees).FirstOrDefault(f => !EstEnClair(f) && new FileInfo(f).Length > 0);
                if (chiffree != null)
                    throw new CleDonneesAbsenteException(CleDonnees.Chemin(dossierDonnees), chiffree);
            }
            return CleDonnees.ObtenirOuCreer(dossierDonnees);
        }

        private static IEnumerable<string> BasesDuDossier(string dossierDonnees)
        {
            if (!Directory.Exists(dossierDonnees)) return Enumerable.Empty<string>();
            return Directory.GetFiles(dossierDonnees, "*", SearchOption.AllDirectories).Where(EstFichierDeBase);
        }

        /// <summary>
        /// Chiffre toutes les bases en clair du dossier de données ; une base en échec n'empêche pas les autres.
        /// <paramref name="avancement"/> est prévenu avant chaque base, puis une dernière fois à la fin.
        /// </summary>
        public static ResultatChiffrement ChiffrerDossierDonnees(string dossierDonnees, string cle,
            IProgress<AvancementChiffrement> avancement = null)
        {
            var resultat = new ResultatChiffrement();
            // Les bases déjà chiffrées sont écartées d'emblée : la barre ne compte que le vrai travail.
            var aChiffrer = BasesDuDossier(dossierDonnees).Where(EstEnClair).ToList();
            for (int i = 0; i < aChiffrer.Count; i++)
            {
                string fichier = aChiffrer[i];
                avancement?.Report(new AvancementChiffrement(i, aChiffrer.Count, fichier));
                try
                {
                    if (ChiffrerSiEnClair(fichier, cle))
                        resultat.Chiffrees.Add(fichier);
                }
                catch (Exception ex)
                {
                    resultat.Echecs.Add(fichier + " : " + ex.Message);
                }
            }
            avancement?.Report(new AvancementChiffrement(aChiffrer.Count, aChiffrer.Count, null));
            return resultat;
        }

        private static bool EstFichierDeBase(string fichier)
        {
            string nom = Path.GetFileName(fichier);
            if (nom.EndsWith(".db", StringComparison.OrdinalIgnoreCase)) return true;
            // Copies complètes d'une base : avant mise à niveau, ou copie de sécurité laissée par
            // un arrêt brutal entre la substitution et son retrait.
            return (nom.IndexOf(".db.avant-v", StringComparison.OrdinalIgnoreCase) > 0
                    || nom.EndsWith(".db" + SuffixeSecurite, StringComparison.OrdinalIgnoreCase))
                   && nom.EndsWith(".bak", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>Vrai si le fichier commence par l'en-tête d'une base SQLite non chiffrée.</summary>
        public static bool EstEnClair(string chemin)
        {
            try
            {
                var entete = new byte[EnteteSqlite.Length];
                using (var f = new FileStream(chemin, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                {
                    if (f.Read(entete, 0, entete.Length) != entete.Length) return false;
                }
                return entete.SequenceEqual(EnteteSqlite);
            }
            catch (IOException)
            {
                return false;
            }
            catch (UnauthorizedAccessException)
            {
                return false;
            }
        }

        /// <summary>
        /// Chiffre la base si elle est en clair. Renvoie faux si elle était déjà chiffrée, vide ou
        /// absente ; lève une exception (fichier inchangé) si le chiffrement échoue.
        /// </summary>
        public static bool ChiffrerSiEnClair(string chemin, string cle)
        {
            if (cle == null) throw new ArgumentNullException(nameof(cle));
            if (!File.Exists(chemin) || !EstEnClair(chemin)) return false;

            string copie = chemin + SuffixeCopie;
            string securite = chemin + SuffixeSecurite;
            try
            {
                // Ouvrir la base en clair rejoue un éventuel journal laissé par une coupure : la copie
                // faite ensuite est cohérente. On relève au passage le contenu, pour le contrôle.
                Dictionary<string, long> contenuAvant;
                using (var connection = SQLiteService.OuvrirConnexion(SQLiteService.ChaineConnexion(chemin), null))
                {
                    if (!EstIntegre(connection))
                        throw new InvalidOperationException("la base est endommagée (contrôle d'intégrité en échec)");
                    contenuAvant = CompterLignes(connection);
                }

                SupprimerSiPresent(copie);
                SupprimerSiPresent(copie + "-journal");
                File.Copy(chemin, copie);

                using (var connection = SQLiteService.OuvrirConnexion(SQLiteService.ChaineConnexion(copie), null))
                using (var cmd = connection.CreateCommand())
                {
                    // La clé n'est faite que de chiffres hexadécimaux (vérifiée) : aucune injection possible.
                    cmd.CommandText = "PRAGMA cipher = 'sqlcipher'; PRAGMA legacy = 4; PRAGMA hexrekey = '" + SQLiteService.VerifierCle(cle) + "';";
                    cmd.ExecuteNonQuery();
                }

                Controler(copie, cle, contenuAvant);

                // Substitution : l'original devient la copie de sécurité le temps du contrôle final.
                SupprimerSiPresent(securite);
                Remplacer(copie, chemin, securite);
                try
                {
                    Controler(chemin, cle, contenuAvant);
                }
                catch
                {
                    // Contrôle final en échec : l'original revient à sa place.
                    Remplacer(securite, chemin, null);
                    throw;
                }

                // La copie de sécurité est en clair : la conserver annulerait le chiffrement.
                File.Delete(securite);
                return true;
            }
            catch (Exception ex)
            {
                SupprimerSiPresent(copie);
                SupprimerSiPresent(copie + "-journal");
                throw new InvalidOperationException(
                    "Le chiffrement de la base " + chemin + " a échoué ; elle reste en clair et n'a pas été modifiée. Détail : " + ex.Message, ex);
            }
        }

        /// <summary>La base est chiffrée, intègre, lisible avec la clé, illisible sans, et contient les mêmes lignes.</summary>
        private static void Controler(string chemin, string cle, Dictionary<string, long> contenuAvant)
        {
            if (EstEnClair(chemin))
                throw new InvalidOperationException("la base est restée en clair");

            using (var connection = SQLiteService.OuvrirConnexion(SQLiteService.ChaineConnexion(chemin, lectureSeule: true), cle))
            {
                if (!EstIntegre(connection))
                    throw new InvalidOperationException("le contrôle d'intégrité de la base chiffrée a échoué");
                var contenuApres = CompterLignes(connection);
                if (contenuApres.Count != contenuAvant.Count || contenuAvant.Any(t => !contenuApres.TryGetValue(t.Key, out long n) || n != t.Value))
                    throw new InvalidOperationException("le contenu de la base chiffrée diffère de l'original");
            }

            if (EstLisibleSansCle(chemin))
                throw new InvalidOperationException("la base chiffrée reste lisible sans la clé");
        }

        private static bool EstLisibleSansCle(string chemin)
        {
            try
            {
                using (var connection = SQLiteService.OuvrirConnexion(SQLiteService.ChaineConnexion(chemin, lectureSeule: true), null))
                using (var cmd = connection.CreateCommand())
                {
                    cmd.CommandText = "SELECT COUNT(*) FROM sqlite_master;";
                    cmd.ExecuteScalar();
                    return true;
                }
            }
            catch (SqliteException)
            {
                return false;
            }
        }

        private static bool EstIntegre(SqliteConnection connection)
        {
            using (var cmd = connection.CreateCommand())
            {
                cmd.CommandText = "PRAGMA quick_check;";
                return string.Equals(Convert.ToString(cmd.ExecuteScalar(), CultureInfo.InvariantCulture), "ok", StringComparison.OrdinalIgnoreCase);
            }
        }

        /// <summary>Nombre de lignes de chaque table (hors tables internes de SQLite).</summary>
        private static Dictionary<string, long> CompterLignes(SqliteConnection connection)
        {
            var tables = new List<string>();
            using (var cmd = connection.CreateCommand())
            {
                cmd.CommandText = "SELECT name FROM sqlite_master WHERE type = 'table' AND name NOT LIKE 'sqlite_%';";
                using (var r = cmd.ExecuteReader())
                    while (r.Read()) tables.Add(r.GetString(0));
            }

            var comptes = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
            foreach (var table in tables)
            {
                using (var cmd = connection.CreateCommand())
                {
                    cmd.CommandText = "SELECT COUNT(*) FROM \"" + table.Replace("\"", "\"\"") + "\";";
                    comptes[table] = Convert.ToInt64(cmd.ExecuteScalar(), CultureInfo.InvariantCulture);
                }
            }
            return comptes;
        }

        /// <summary>
        /// <see cref="File.Replace(string, string, string)"/> avec quelques essais : un antivirus ou une
        /// connexion pas encore libérée peuvent retenir brièvement le fichier.
        /// </summary>
        private static void Remplacer(string source, string destination, string sauvegarde)
        {
            for (int essai = 1; ; essai++)
            {
                try
                {
                    File.Replace(source, destination, sauvegarde);
                    return;
                }
                catch (Exception ex) when ((ex is IOException || ex is UnauthorizedAccessException) && essai < 20)
                {
                    GC.Collect();
                    GC.WaitForPendingFinalizers();
                    System.Threading.Thread.Sleep(200);
                }
            }
        }

        private static void SupprimerSiPresent(string chemin)
        {
            try { if (File.Exists(chemin)) File.Delete(chemin); } catch { }
        }
    }

    /// <summary>La clé du poste a disparu alors que des bases sont déjà chiffrées.</summary>
    public class CleDonneesAbsenteException : InvalidOperationException
    {
        public CleDonneesAbsenteException(string chemin, string baseChiffree)
            : base("La clé de chiffrement des données de ce poste est introuvable (" + chemin + "), " +
                   "alors que des bases sont chiffrées (par exemple " + baseChiffree + "). " +
                   "Remettez le fichier de clé à sa place, ou restaurez une sauvegarde avec son mot de passe. " +
                   "Aucune nouvelle clé n'a été créée et aucune base n'a été modifiée.")
        {
            Chemin = chemin;
        }

        public string Chemin { get; }
    }
}
