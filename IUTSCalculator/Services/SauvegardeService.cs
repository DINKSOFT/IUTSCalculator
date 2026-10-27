using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.Json;
using IUTSCalculator.Helpers;
using Microsoft.Data.Sqlite;

namespace IUTSCalculator.Services
{
    /// <summary>Un dossier (entreprise) trouvé dans une sauvegarde.</summary>
    public class DossierSauvegarde
    {
        public string RaisonSociale { get; set; }
        public string Fichier { get; set; }

        /// <summary>Niveau de schéma de la base (-1 : fichier absent ou illisible).</summary>
        public int Niveau { get; set; }

        public string Libelle
        {
            get
            {
                string nom = string.IsNullOrWhiteSpace(RaisonSociale) ? Path.GetFileName(Fichier ?? "") : RaisonSociale;
                return Niveau >= 0 ? nom + " (base v" + Niveau + ")" : nom + " (base absente de la sauvegarde)";
            }
        }
    }

    /// <summary>Ce que contient un fichier de sauvegarde, lu avant toute restauration.</summary>
    public class ContenuSauvegarde
    {
        public bool EstSauvegardeSigma { get; set; }
        public DateTime? Date { get; set; }
        public string VersionProgramme { get; set; }
        public string Poste { get; set; }
        public int Fichiers { get; set; }
        public List<DossierSauvegarde> Dossiers { get; } = new List<DossierSauvegarde>();

        /// <summary>
        /// Dossiers dont le registre de la sauvegarde désigne la base hors du dossier de données
        /// (chemin absolu, « .. »…) : leur présence interdit la restauration.
        /// </summary>
        public List<string> DossiersInvalides { get; } = new List<string>();

        /// <summary>Niveau de schéma le plus élevé parmi les bases de la sauvegarde.</summary>
        public int NiveauMax { get { return Dossiers.Count == 0 ? 0 : Dossiers.Max(d => d.Niveau); } }
    }

    public class ResultatSauvegarde
    {
        public string Chemin { get; set; }
        public int Fichiers { get; set; }
        public int Bases { get; set; }
        public long Octets { get; set; }
        public List<string> Avertissements { get; } = new List<string>();
    }

    public class ResultatRestauration
    {
        public int Fichiers { get; set; }
        public string SauvegardeDeSecurite { get; set; }
        public ContenuSauvegarde Contenu { get; set; }
    }

    /// <summary>
    /// Sauvegarde et restauration de TOUTES les données du programme sur ce poste : registre
    /// des dossiers, base de chaque entreprise, utilisateurs, réglages, pièces disciplinaires,
    /// CV… (le dossier de données, <c>%APPDATA%\IUTSCalculator</c>). La licence, liée au poste,
    /// n'en fait pas partie.
    ///
    /// Les bases sont copiées par SQLite lui-même (<c>VACUUM INTO</c>) : la copie est cohérente
    /// même si le programme est ouvert. La restauration refuse une sauvegarde faite par une
    /// version plus récente du programme, et sauvegarde d'abord l'état actuel.
    /// </summary>
    public class SauvegardeService
    {
        public const string NomManifeste = "sauvegarde-sigma.txt";
        private const string EnteteManifeste = "Sigma Paie & RH — sauvegarde des données";
        private static readonly string[] ExtensionsExclues = { ".bak", ".tmp" };
        private static readonly string[] SuffixesExclus = { "-journal", "-wal", "-shm" };

        private readonly string _racine;

        public SauvegardeService(string racineDonnees)
        {
            if (string.IsNullOrWhiteSpace(racineDonnees)) throw new ArgumentNullException(nameof(racineDonnees));
            _racine = Path.GetFullPath(racineDonnees);
        }

        public string Racine { get { return _racine; } }

        /// <summary>Dossier proposé pour les sauvegardes : Documents\Sigma Paie &amp; RH\Sauvegardes.</summary>
        public static string DossierParDefaut()
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Sigma Paie & RH", "Sauvegardes");
        }

        public static string NomPropose(DateTime quand, string prefixe = "Sauvegarde")
        {
            return string.Format(CultureInfo.InvariantCulture, "{0}_Sigma_Paie_RH_{1:yyyy-MM-dd_HHmm}.zip", prefixe, quand);
        }

        // ═══════════════════════════════════════════════════════════════════
        //  Sauvegarde
        // ═══════════════════════════════════════════════════════════════════

        public ResultatSauvegarde Sauvegarder(string cheminZip, string versionProgramme)
        {
            if (string.IsNullOrWhiteSpace(cheminZip)) throw new ArgumentNullException(nameof(cheminZip));
            cheminZip = Path.GetFullPath(cheminZip);
            if (EstSous(cheminZip, _racine))
                throw new InvalidOperationException("Choisissez un emplacement hors du dossier de données du programme (" + _racine + ").");
            if (!Directory.Exists(_racine))
                throw new InvalidOperationException("Aucune donnée à sauvegarder : le dossier " + _racine + " n'existe pas.");

            var resultat = new ResultatSauvegarde { Chemin = cheminZip };
            string temp = DossierTemporaire();
            string zipTemp = cheminZip + ".partiel";
            try
            {
                var bases = new List<DossierSauvegarde>();
                foreach (var fichier in Directory.GetFiles(_racine, "*", SearchOption.AllDirectories))
                {
                    if (EstExclu(fichier)) continue;
                    string relatif = Relatif(fichier);
                    string cible = Path.Combine(temp, relatif);
                    Directory.CreateDirectory(Path.GetDirectoryName(cible));

                    if (string.Equals(Path.GetExtension(fichier), ".db", StringComparison.OrdinalIgnoreCase))
                    {
                        CopierBase(fichier, cible, resultat);
                        resultat.Bases++;
                        bases.Add(new DossierSauvegarde { Fichier = relatif, Niveau = SQLiteService.LireVersionFichier(cible) });
                    }
                    else
                    {
                        CopierFichier(fichier, cible);
                    }
                    resultat.Fichiers++;
                }

                // Bases rangées hors du dossier de données : signalées, non sauvegardées.
                foreach (var d in LireRegistre(Path.Combine(_racine, "dossiers.json")))
                {
                    if (d.Fichier != null && Path.IsPathRooted(d.Fichier) && !EstSous(Path.GetFullPath(d.Fichier), _racine))
                        resultat.Avertissements.Add("La base du dossier « " + d.RaisonSociale + " » est rangée hors du dossier de données (" + d.Fichier + ") : copiez-la à part.");
                }

                File.WriteAllText(Path.Combine(temp, NomManifeste), Manifeste(versionProgramme, bases, Path.Combine(temp, "dossiers.json")), new UTF8Encoding(true));

                Directory.CreateDirectory(Path.GetDirectoryName(cheminZip));
                SupprimerSiPresent(zipTemp);
                ZipFile.CreateFromDirectory(temp, zipTemp, CompressionLevel.Optimal, false);

                // Relecture : une sauvegarde illisible ne doit jamais remplacer une bonne.
                using (var zip = ZipFile.OpenRead(zipTemp))
                {
                    if (zip.GetEntry(NomManifeste) == null || zip.Entries.Count < resultat.Fichiers + 1)
                        throw new InvalidOperationException("la relecture de l'archive a échoué");
                }
                SupprimerSiPresent(cheminZip);
                File.Move(zipTemp, cheminZip);
                resultat.Octets = new FileInfo(cheminZip).Length;
                return resultat;
            }
            finally
            {
                SupprimerSiPresent(zipTemp);
                SupprimerDossier(temp);
            }
        }

        /// <summary>Copie cohérente d'une base, faite par SQLite (le programme peut être ouvert).</summary>
        private static void CopierBase(string source, string cible, ResultatSauvegarde resultat)
        {
            try
            {
                SupprimerSiPresent(cible);
                SqliteNatif.Initialiser();
                var chaine = new SqliteConnectionStringBuilder { DataSource = source, Pooling = false, DefaultTimeout = 10 };
                using (var connection = new SqliteConnection(chaine.ToString()))
                {
                    connection.Open();
                    using (var cmd = connection.CreateCommand())
                    {
                        cmd.CommandText = "VACUUM INTO '" + cible.Replace("'", "''") + "';";
                        cmd.ExecuteNonQuery();
                    }
                }
            }
            catch (Exception ex)
            {
                // Fichier .db qui n'est pas une base SQLite, ou SQLite trop ancien : copie brute.
                CopierFichier(source, cible);
                resultat.Avertissements.Add("Copie directe de " + Path.GetFileName(source) + " (" + ex.Message + ").");
            }
        }

        private static void CopierFichier(string source, string cible)
        {
            using (var entree = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            using (var sortie = new FileStream(cible, FileMode.Create, FileAccess.Write, FileShare.None))
                entree.CopyTo(sortie);
        }

        private string Manifeste(string versionProgramme, List<DossierSauvegarde> bases, string registre)
        {
            var noms = LireRegistre(registre).Where(d => d.Fichier != null)
                .ToDictionary(d => Normaliser(d.Fichier), d => d.RaisonSociale, StringComparer.OrdinalIgnoreCase);
            var sb = new StringBuilder();
            sb.AppendLine(EnteteManifeste);
            sb.AppendLine("Format=1");
            sb.AppendLine("Date=" + DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture));
            sb.AppendLine("Version=" + (versionProgramme ?? ""));
            sb.AppendLine("NiveauProgramme=" + SQLiteService.SchemaCible.ToString(CultureInfo.InvariantCulture));
            sb.AppendLine("Poste=" + Environment.MachineName);
            sb.AppendLine("Utilisateur=" + Environment.UserName);
            foreach (var b in bases)
            {
                string nom;
                noms.TryGetValue(Normaliser(b.Fichier), out nom);
                sb.AppendLine("Base=" + (nom ?? "").Replace("|", "/") + "|" + Normaliser(b.Fichier) + "|" + b.Niveau.ToString(CultureInfo.InvariantCulture));
            }
            return sb.ToString();
        }

        // ═══════════════════════════════════════════════════════════════════
        //  Lecture d'une sauvegarde
        // ═══════════════════════════════════════════════════════════════════

        /// <summary>Lit le contenu d'une sauvegarde sans rien modifier.</summary>
        public ContenuSauvegarde Analyser(string cheminZip)
        {
            string temp = DossierTemporaire();
            try
            {
                return Extraire(cheminZip, temp);
            }
            finally
            {
                SupprimerDossier(temp);
            }
        }

        private ContenuSauvegarde Extraire(string cheminZip, string temp)
        {
            if (!File.Exists(cheminZip)) throw new InvalidOperationException("Fichier introuvable : " + cheminZip);
            var contenu = new ContenuSauvegarde();
            try
            {
                using (var zip = ZipFile.OpenRead(cheminZip))
                {
                    foreach (var entree in zip.Entries)
                    {
                        if (string.IsNullOrEmpty(entree.Name)) continue;   // répertoire
                        string cible = Path.GetFullPath(Path.Combine(temp, entree.FullName));
                        if (!EstSous(cible, temp)) throw new InvalidOperationException("chemin non autorisé dans l'archive : " + entree.FullName);
                        Directory.CreateDirectory(Path.GetDirectoryName(cible));
                        entree.ExtractToFile(cible, true);
                        if (!string.Equals(entree.FullName, NomManifeste, StringComparison.OrdinalIgnoreCase)) contenu.Fichiers++;
                    }
                }
            }
            catch (InvalidDataException)
            {
                throw new InvalidOperationException("Ce fichier n'est pas une archive de sauvegarde lisible.");
            }

            string manifeste = Path.Combine(temp, NomManifeste);
            if (!File.Exists(manifeste)) return contenu;   // pas une sauvegarde Sigma
            var lignes = File.ReadAllLines(manifeste, Encoding.UTF8);
            contenu.EstSauvegardeSigma = lignes.Length > 0 && lignes[0].Trim().TrimStart('﻿') == EnteteManifeste;
            foreach (var ligne in lignes.Skip(1))
            {
                int i = ligne.IndexOf('=');
                if (i <= 0) continue;
                string cle = ligne.Substring(0, i), valeur = ligne.Substring(i + 1);
                DateTime date;
                if (cle == "Date" && DateTime.TryParseExact(valeur, "yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out date)) contenu.Date = date;
                else if (cle == "Version") contenu.VersionProgramme = valeur;
                else if (cle == "Poste") contenu.Poste = valeur;
            }

            // Les niveaux sont relus dans les bases elles-mêmes, pas dans le manifeste.
            var registre = LireRegistre(Path.Combine(temp, "dossiers.json"));
            // Même contrôle qu'au chargement du registre : une base désignée hors du dossier de
            // données (archive fabriquée ou modifiée) bloque la restauration.
            foreach (var d in registre.Where(d => !CheminsDossier.EstBaseValide(d.Fichier, temp)))
                contenu.DossiersInvalides.Add((string.IsNullOrWhiteSpace(d.RaisonSociale) ? "(sans nom)" : d.RaisonSociale) + " → " + (d.Fichier ?? "(aucun fichier)"));
            var noms = registre.Where(d => d.Fichier != null)
                .GroupBy(d => Normaliser(d.Fichier), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First().RaisonSociale, StringComparer.OrdinalIgnoreCase);
            foreach (var db in Directory.GetFiles(temp, "*.db", SearchOption.AllDirectories))
            {
                string relatif = Normaliser(db.Substring(temp.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                string nom;
                noms.TryGetValue(relatif, out nom);
                contenu.Dossiers.Add(new DossierSauvegarde { RaisonSociale = nom, Fichier = relatif, Niveau = SQLiteService.LireVersionFichier(db) });
            }
            contenu.Dossiers.Sort((a, b) => string.Compare(a.Libelle, b.Libelle, StringComparison.CurrentCultureIgnoreCase));
            return contenu;
        }

        // ═══════════════════════════════════════════════════════════════════
        //  Restauration
        // ═══════════════════════════════════════════════════════════════════

        /// <summary>Raison pour laquelle cette sauvegarde ne peut pas être restaurée, ou null.</summary>
        public static string Obstacle(ContenuSauvegarde c)
        {
            if (c == null || !c.EstSauvegardeSigma) return "Ce fichier n'est pas une sauvegarde de Sigma Paie & RH.";
            if (c.DossiersInvalides.Count > 0)
                return "Le registre des dossiers de cette sauvegarde désigne des bases hors du dossier de données du programme : restauration refusée par sécurité.\n• "
                       + string.Join("\n• ", c.DossiersInvalides);
            if (c.Dossiers.Any(d => d.Niveau < 0)) return "Une base de la sauvegarde est illisible : restauration refusée.";
            if (c.NiveauMax > SQLiteService.SchemaCible)
                return string.Format(CultureInfo.InvariantCulture,
                    "Cette sauvegarde a été faite avec une version plus récente du programme (bases jusqu'à la v{0} ; ce programme gère la v{1}). " +
                    "Installez d'abord la dernière version.", c.NiveauMax, SQLiteService.SchemaCible);
            return null;
        }

        /// <summary>
        /// Remet les données dans l'état de la sauvegarde. L'état actuel est d'abord
        /// sauvegardé dans <paramref name="cheminSauvegardeSecurite"/> : sans elle, rien
        /// n'est touché. Les fichiers créés depuis la sauvegarde ne sont pas supprimés (ils
        /// ne sont plus référencés, et restent dans la sauvegarde de sécurité).
        /// À faire sans dossier ouvert, puis relancer le programme.
        /// </summary>
        public ResultatRestauration Restaurer(string cheminZip, string cheminSauvegardeSecurite, string versionProgramme)
        {
            string temp = DossierTemporaire();
            try
            {
                var contenu = Extraire(cheminZip, temp);
                var obstacle = Obstacle(contenu);
                if (obstacle != null) throw new InvalidOperationException(obstacle);

                var resultat = new ResultatRestauration { Contenu = contenu };
                if (Directory.Exists(_racine) && Directory.EnumerateFiles(_racine, "*", SearchOption.AllDirectories).Any(f => !EstExclu(f)))
                {
                    Sauvegarder(cheminSauvegardeSecurite, versionProgramme);
                    resultat.SauvegardeDeSecurite = cheminSauvegardeSecurite;
                }

                foreach (var fichier in Directory.GetFiles(temp, "*", SearchOption.AllDirectories))
                {
                    string relatif = fichier.Substring(temp.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                    if (string.Equals(relatif, NomManifeste, StringComparison.OrdinalIgnoreCase)) continue;
                    if (EstExclu(fichier)) continue;   // dont la clé du poste : une archive ne la remplace jamais
                    string cible = Path.GetFullPath(Path.Combine(_racine, relatif));
                    if (!EstSous(cible, _racine)) continue;
                    Directory.CreateDirectory(Path.GetDirectoryName(cible));
                    if (string.Equals(Path.GetExtension(cible), ".db", StringComparison.OrdinalIgnoreCase))
                    {
                        SupprimerSiPresent(cible + "-journal");
                        SupprimerSiPresent(cible + SQLiteService.SuffixeCopieMigration);
                        // Les copies « avant mise à niveau » de la base remplacée ne correspondent
                        // plus à la base restaurée : elles sont renommées, pour qu'une prochaine
                        // mise à niveau dépose une copie de la base restaurée.
                        if (Directory.Exists(Path.GetDirectoryName(cible)))
                            foreach (var bak in Directory.GetFiles(Path.GetDirectoryName(cible), Path.GetFileName(cible) + ".avant-v*.bak"))
                                if (!bak.Contains(".remplace-"))
                                    try { File.Move(bak, bak.Substring(0, bak.Length - 4) + ".remplace-" + DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + ".bak"); }
                                    catch { }
                    }
                    File.Copy(fichier, cible, true);
                    resultat.Fichiers++;
                }
                return resultat;
            }
            finally
            {
                SupprimerDossier(temp);
            }
        }

        // ═══════════════════════════════════════════════════════════════════
        //  Utilitaires
        // ═══════════════════════════════════════════════════════════════════

        private bool EstExclu(string fichier)
        {
            string nom = Path.GetFileName(fichier);
            // La clé des bases est propre au poste (DPAPI) : jamais sauvegardée ni restaurée.
            if (nom.StartsWith(CleDonnees.NomFichier, StringComparison.OrdinalIgnoreCase)) return true;
            if (ExtensionsExclues.Any(e => nom.EndsWith(e, StringComparison.OrdinalIgnoreCase))) return true;
            if (SuffixesExclus.Any(s => nom.EndsWith(s, StringComparison.OrdinalIgnoreCase))) return true;
            return nom.EndsWith(".partiel", StringComparison.OrdinalIgnoreCase);
        }

        private string Relatif(string fichier)
        {
            return Path.GetFullPath(fichier).Substring(_racine.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }

        private static string Normaliser(string chemin)
        {
            return (chemin ?? "").Replace('\\', '/').TrimStart('/');
        }

        private static bool EstSous(string chemin, string racine)
        {
            string r = Path.GetFullPath(racine).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            return Path.GetFullPath(chemin).StartsWith(r, StringComparison.OrdinalIgnoreCase);
        }

        private static List<DossierSauvegarde> LireRegistre(string chemin)
        {
            var liste = new List<DossierSauvegarde>();
            try
            {
                if (!File.Exists(chemin)) return liste;
                using (var doc = JsonDocument.Parse(File.ReadAllText(chemin)))
                {
                    if (doc.RootElement.ValueKind != JsonValueKind.Array) return liste;
                    foreach (var e in doc.RootElement.EnumerateArray())
                    {
                        JsonElement nom, fichier;
                        liste.Add(new DossierSauvegarde
                        {
                            RaisonSociale = e.TryGetProperty("RaisonSociale", out nom) && nom.ValueKind == JsonValueKind.String ? nom.GetString() : null,
                            Fichier = e.TryGetProperty("DbFileName", out fichier) && fichier.ValueKind == JsonValueKind.String ? fichier.GetString() : null
                        });
                    }
                }
            }
            catch
            {
                // Registre illisible : les bases restent sauvegardées, seuls les noms manquent.
            }
            return liste;
        }

        private static string DossierTemporaire()
        {
            string d = Path.Combine(Path.GetTempPath(), "SigmaSauvegarde_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(d);
            return d;
        }

        private static void SupprimerSiPresent(string chemin)
        {
            try { if (File.Exists(chemin)) File.Delete(chemin); } catch { }
        }

        private static void SupprimerDossier(string chemin)
        {
            try { if (Directory.Exists(chemin)) Directory.Delete(chemin, true); } catch { }
        }
    }
}
