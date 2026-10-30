using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using IUTSCalculator.Helpers;

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

        /// <summary>Format du manifeste : 1 (ancien, en clair), 2 (protégé par mot de passe).</summary>
        public int Format { get; set; } = 1;

        /// <summary>Sauvegarde protégée par un mot de passe (format 2).</summary>
        public bool Protegee { get { return Format >= 2; } }

        /// <summary>Sauvegarde protégée lue sans son mot de passe : seuls la date, la version et le poste sont connus.</summary>
        public bool MotDePasseRequis { get; set; }

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
    /// n'en fait pas partie, ni la clé des bases ni le mot de passe de sauvegarde du poste.
    ///
    /// Une sauvegarde est <b>portable et protégée par le mot de passe de sauvegarde</b> (format 2) :
    /// les bases y sont chiffrées par une clé dérivée du mot de passe (et non par la clé du poste),
    /// tous les autres fichiers — dont <c>users.json</c>, sorti de sa protection DPAPI propre au
    /// poste — sont rangés dans un coffre chiffré et authentifié (<see cref="CoffreSauvegarde"/>).
    /// La restauration, sur ce poste ou un autre, redemande le mot de passe, rechiffre les bases
    /// avec la clé du poste qui les reçoit et <c>users.json</c> avec son DPAPI.
    ///
    /// Les bases sont copiées par SQLite lui-même (<c>VACUUM INTO</c>) : la copie est cohérente
    /// même si le programme est ouvert. La restauration refuse une sauvegarde faite par une
    /// version plus récente du programme, et sauvegarde d'abord l'état actuel. Les anciennes
    /// sauvegardes (format 1, en clair) restent restaurables.
    /// </summary>
    public class SauvegardeService
    {
        public const string NomManifeste = "sauvegarde-sigma.txt";

        /// <summary>Coffre chiffré des fichiers autres que les bases.</summary>
        public const string NomCoffre = "donnees-protegees.sigma";

        private const string EnteteManifeste = "Sigma Paie & RH — sauvegarde des données";
        private const string NomComptes = "users.json";
        private static readonly string[] ExtensionsExclues = { ".bak", ".tmp" };
        private static readonly string[] SuffixesExclus = { "-journal", "-wal", "-shm" };

        private readonly string _racine;
        private readonly Func<string> _cleBases;

        /// <param name="cleBases">Clé des bases de ce poste ; par défaut la clé active (<see cref="SQLiteService.CleActive"/>).</param>
        public SauvegardeService(string racineDonnees, Func<string> cleBases = null)
        {
            if (string.IsNullOrWhiteSpace(racineDonnees)) throw new ArgumentNullException(nameof(racineDonnees));
            _racine = Path.GetFullPath(racineDonnees);
            _cleBases = cleBases ?? SQLiteService.CleActive;
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

        public ResultatSauvegarde Sauvegarder(string cheminZip, string motDePasse, string versionProgramme)
        {
            if (string.IsNullOrWhiteSpace(cheminZip)) throw new ArgumentNullException(nameof(cheminZip));
            string refus = MotDePasseSauvegarde.Valider(motDePasse);
            if (refus != null) throw new ArgumentException(refus, nameof(motDePasse));
            cheminZip = Path.GetFullPath(cheminZip);
            if (EstSous(cheminZip, _racine))
                throw new InvalidOperationException("Choisissez un emplacement hors du dossier de données du programme (" + _racine + ").");
            if (!Directory.Exists(_racine))
                throw new InvalidOperationException("Aucune donnée à sauvegarder : le dossier " + _racine + " n'existe pas.");

            var resultat = new ResultatSauvegarde { Chemin = cheminZip };
            string temp = DossierTemporaire();
            string coffreTemp = DossierTemporaire();
            string contenuCoffre = Path.Combine(coffreTemp, "contenu");
            string zipTemp = cheminZip + ".partiel";
            byte[] sel = CoffreSauvegarde.NouveauSel();
            int iterations = CoffreSauvegarde.IterationsParDefaut;
            try
            {
                using (var cles = CoffreSauvegarde.Deriver(motDePasse, sel, iterations))
                {
                    Directory.CreateDirectory(contenuCoffre);
                    var bases = new List<DossierSauvegarde>();
                    string cleLocale = _cleBases();
                    foreach (var fichier in Directory.GetFiles(_racine, "*", SearchOption.AllDirectories))
                    {
                        if (EstExclu(fichier)) continue;
                        string relatif = Relatif(fichier);
                        resultat.Fichiers++;

                        if (string.Equals(Path.GetExtension(fichier), ".db", StringComparison.OrdinalIgnoreCase))
                        {
                            string cible = Path.Combine(temp, relatif);
                            Directory.CreateDirectory(Path.GetDirectoryName(cible));
                            string erreur;
                            if (CopierBase(fichier, cible, cleLocale, cles.CleBases, out erreur))
                            {
                                resultat.Bases++;
                                bases.Add(new DossierSauvegarde { Fichier = relatif, Niveau = SQLiteService.LireVersionFichier(cible, cles.CleBases) });
                                continue;
                            }
                            // Fichier .db qui n'est pas une base lisible : gardé tel quel, mais dans le coffre.
                            resultat.Avertissements.Add("Copie directe de " + Path.GetFileName(fichier) + " (" + erreur + ").");
                        }

                        string dansCoffre = Path.Combine(contenuCoffre, relatif);
                        Directory.CreateDirectory(Path.GetDirectoryName(dansCoffre));
                        if (EstFichierComptes(relatif))
                        {
                            if (!CopierComptesPortables(fichier, dansCoffre))
                                resultat.Avertissements.Add("Les comptes utilisateurs ne sont pas lisibles par ce profil Windows : ils sont sauvegardés tels quels et ne pourront pas être restaurés sur un autre poste.");
                        }
                        else
                        {
                            CopierFichier(fichier, dansCoffre);
                        }
                    }

                    // Bases rangées hors du dossier de données : signalées, non sauvegardées.
                    foreach (var d in LireRegistre(Path.Combine(_racine, "dossiers.json")))
                    {
                        if (d.Fichier != null && Path.IsPathRooted(d.Fichier) && !EstSous(Path.GetFullPath(d.Fichier), _racine))
                            resultat.Avertissements.Add("La base du dossier « " + d.RaisonSociale + " » est rangée hors du dossier de données (" + d.Fichier + ") : copiez-la à part.");
                    }

                    string zipCoffre = Path.Combine(coffreTemp, "contenu.zip");
                    ZipFile.CreateFromDirectory(contenuCoffre, zipCoffre, CompressionLevel.Optimal, false);
                    CoffreSauvegarde.Chiffrer(zipCoffre, Path.Combine(temp, NomCoffre), cles);
                    SupprimerDossier(coffreTemp);

                    File.WriteAllText(Path.Combine(temp, NomManifeste), Manifeste(versionProgramme, bases, sel, iterations), new UTF8Encoding(true));

                    Directory.CreateDirectory(Path.GetDirectoryName(cheminZip));
                    SupprimerSiPresent(zipTemp);
                    ZipFile.CreateFromDirectory(temp, zipTemp, CompressionLevel.Optimal, false);

                    // Relecture : une sauvegarde illisible ne doit jamais remplacer une bonne.
                    using (var zip = ZipFile.OpenRead(zipTemp))
                    {
                        if (zip.GetEntry(NomManifeste) == null || zip.GetEntry(NomCoffre) == null || zip.Entries.Count < resultat.Bases + 2)
                            throw new InvalidOperationException("la relecture de l'archive a échoué");
                    }
                    string controle = DossierTemporaire();
                    try
                    {
                        CoffreSauvegarde.Dechiffrer(Path.Combine(temp, NomCoffre), Path.Combine(controle, "contenu.zip"), cles);
                    }
                    finally
                    {
                        SupprimerDossier(controle);
                    }
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
                SupprimerDossier(coffreTemp);
            }
        }

        /// <summary>
        /// Copie cohérente d'une base, faite par SQLite (le programme peut être ouvert) : ouverte avec
        /// la clé du poste si elle est chiffrée, copiée chiffrée par <paramref name="cleCible"/>.
        /// </summary>
        private static bool CopierBase(string source, string cible, string cleSource, string cleCible, out string erreur)
        {
            try
            {
                SupprimerSiPresent(cible);
                if (ChiffrementBases.EstEnClair(source)) cleSource = null;
                // Tout format : une sauvegarde faite avant le passage à la clé brute reste restaurable.
                using (var connection = SQLiteService.OuvrirConnexionToutFormat(SQLiteService.ChaineConnexion(source), cleSource))
                using (var cmd = connection.CreateCommand())
                {
                    cmd.CommandText = "VACUUM INTO " + SQLiteService.CibleVacuum(cible, cleCible) + ";";
                    cmd.ExecuteNonQuery();
                }
                // Sans clé de cible, SQLite3MC chiffre la copie avec la clé de la source : on la lui retire
                // (seulement quand ce poste n'a pas de clé, c'est-à-dire hors de l'application).
                if (cleCible == null && cleSource != null)
                {
                    using (var connection = SQLiteService.OuvrirConnexionToutFormat(SQLiteService.ChaineConnexion(cible), cleSource))
                    using (var cmd = connection.CreateCommand())
                    {
                        cmd.CommandText = "PRAGMA rekey = '';";
                        cmd.ExecuteNonQuery();
                    }
                }
                // Garde-fou : une copie restée en clair ne doit jamais sortir du poste.
                if (cleCible != null && ChiffrementBases.EstEnClair(cible))
                    throw new InvalidOperationException("la copie n'a pas été chiffrée");
                if (cleCible == null && !ChiffrementBases.EstEnClair(cible))
                    throw new InvalidOperationException("la copie n'a pas pu être déchiffrée");
                erreur = null;
                return true;
            }
            catch (Exception ex)
            {
                SupprimerSiPresent(cible);
                erreur = ex.Message;
                return false;
            }
        }

        private static bool EstFichierComptes(string relatif)
        {
            return string.Equals(Normaliser(relatif), NomComptes, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// <c>users.json</c> est protégé par DPAPI, donc illisible ailleurs : il est rangé dans le coffre
        /// une fois déprotégé. Renvoie faux s'il a fallu le copier tel quel (ni DPAPI de ce profil, ni JSON).
        /// </summary>
        private static bool CopierComptesPortables(string source, string cible)
        {
            byte[] donnees = File.ReadAllBytes(source);
            try
            {
                donnees = UserAccountService.DeprotegerPourCePoste(donnees);
            }
            catch (CryptographicException)
            {
                // Ancien format en clair (lisible partout) ou fichier d'un autre profil : copié tel quel.
                File.WriteAllBytes(cible, donnees);
                return EstJson(donnees);
            }
            try { File.WriteAllBytes(cible, donnees); }
            finally { Array.Clear(donnees, 0, donnees.Length); }
            return true;
        }

        private static bool EstJson(byte[] donnees)
        {
            foreach (byte b in donnees)
            {
                if (b == 0xEF || b == 0xBB || b == 0xBF || b == (byte)' ' || b == (byte)'\t' || b == (byte)'\r' || b == (byte)'\n') continue;
                return b == (byte)'[' || b == (byte)'{';
            }
            return false;
        }

        private static void CopierFichier(string source, string cible)
        {
            using (var entree = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            using (var sortie = new FileStream(cible, FileMode.Create, FileAccess.Write, FileShare.None))
                entree.CopyTo(sortie);
        }

        /// <summary>
        /// Manifeste en clair : de quoi reconnaître la sauvegarde et dériver ses clés. Les noms des
        /// entreprises n'y figurent pas (ils sont dans le registre, au coffre).
        /// </summary>
        private static string Manifeste(string versionProgramme, List<DossierSauvegarde> bases, byte[] sel, int iterations)
        {
            var sb = new StringBuilder();
            sb.AppendLine(EnteteManifeste);
            sb.AppendLine("Format=2");
            sb.AppendLine("Date=" + DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture));
            sb.AppendLine("Version=" + (versionProgramme ?? ""));
            sb.AppendLine("NiveauProgramme=" + SQLiteService.SchemaCible.ToString(CultureInfo.InvariantCulture));
            sb.AppendLine("Poste=" + Environment.MachineName);
            sb.AppendLine("Utilisateur=" + Environment.UserName);
            sb.AppendLine("Protection=PBKDF2-SHA256/SQLCipher4/AES-256-CBC+HMAC-SHA256");
            sb.AppendLine("Iterations=" + iterations.ToString(CultureInfo.InvariantCulture));
            sb.AppendLine("Sel=" + Convert.ToBase64String(sel));
            foreach (var b in bases)
                sb.AppendLine("Base=|" + Normaliser(b.Fichier) + "|" + b.Niveau.ToString(CultureInfo.InvariantCulture));
            return sb.ToString();
        }

        // ═══════════════════════════════════════════════════════════════════
        //  Lecture d'une sauvegarde
        // ═══════════════════════════════════════════════════════════════════

        /// <summary>
        /// Lit le contenu d'une sauvegarde sans rien modifier. Une sauvegarde protégée lue sans
        /// <paramref name="motDePasse"/> revient avec <see cref="ContenuSauvegarde.MotDePasseRequis"/> ;
        /// un mot de passe faux lève <see cref="MotDePasseSauvegardeIncorrectException"/>.
        /// </summary>
        public ContenuSauvegarde Analyser(string cheminZip, string motDePasse = null)
        {
            string temp = DossierTemporaire();
            try
            {
                string cleSauvegarde;
                return Extraire(cheminZip, temp, motDePasse, out cleSauvegarde);
            }
            finally
            {
                SupprimerDossier(temp);
            }
        }

        private ContenuSauvegarde Extraire(string cheminZip, string temp, string motDePasse, out string cleSauvegarde)
        {
            cleSauvegarde = null;
            if (!File.Exists(cheminZip)) throw new InvalidOperationException("Fichier introuvable : " + cheminZip);
            var contenu = new ContenuSauvegarde();
            try
            {
                using (var zip = ZipFile.OpenRead(cheminZip))
                    ExtraireArchive(zip, temp);
            }
            catch (InvalidDataException)
            {
                throw new InvalidOperationException("Ce fichier n'est pas une archive de sauvegarde lisible.");
            }

            string manifeste = Path.Combine(temp, NomManifeste);
            if (!File.Exists(manifeste)) return contenu;   // pas une sauvegarde Sigma
            var lignes = File.ReadAllLines(manifeste, Encoding.UTF8);
            contenu.EstSauvegardeSigma = lignes.Length > 0 && lignes[0].Trim().TrimStart('﻿') == EnteteManifeste;
            byte[] sel = null;
            int iterations = 0;
            foreach (var ligne in lignes.Skip(1))
            {
                int i = ligne.IndexOf('=');
                if (i <= 0) continue;
                string cle = ligne.Substring(0, i), valeur = ligne.Substring(i + 1);
                DateTime date;
                int nombre;
                if (cle == "Date" && DateTime.TryParseExact(valeur, "yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out date)) contenu.Date = date;
                else if (cle == "Version") contenu.VersionProgramme = valeur;
                else if (cle == "Poste") contenu.Poste = valeur;
                else if (cle == "Format" && int.TryParse(valeur, NumberStyles.None, CultureInfo.InvariantCulture, out nombre)) contenu.Format = nombre;
                else if (cle == "Iterations" && int.TryParse(valeur, NumberStyles.None, CultureInfo.InvariantCulture, out nombre)) iterations = nombre;
                else if (cle == "Sel") { try { sel = Convert.FromBase64String(valeur); } catch (FormatException) { sel = null; } }
            }
            if (!contenu.EstSauvegardeSigma) return contenu;

            if (contenu.Protegee)
            {
                if (motDePasse == null)
                {
                    contenu.MotDePasseRequis = true;
                    return contenu;
                }
                string coffre = Path.Combine(temp, NomCoffre);
                if (!File.Exists(coffre) || sel == null || iterations < CoffreSauvegarde.IterationsMin || iterations > CoffreSauvegarde.IterationsMax)
                    throw new InvalidOperationException("Cette sauvegarde est incomplète ou abîmée : restauration impossible.");

                string zipCoffre = Path.Combine(temp, NomCoffre + ".zip");
                using (var cles = CoffreSauvegarde.Deriver(motDePasse, sel, iterations))
                {
                    CoffreSauvegarde.Dechiffrer(coffre, zipCoffre, cles);
                    cleSauvegarde = cles.CleBases;
                }
                File.Delete(coffre);
                using (var zip = ZipFile.OpenRead(zipCoffre))
                    ExtraireArchive(zip, temp, refuserDoublons: true);
                File.Delete(zipCoffre);
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
            // Format 2 : bases chiffrées par la clé de la sauvegarde ; format 1 : en clair, ou par la clé de ce poste.
            string cleBases = contenu.Protegee ? cleSauvegarde : _cleBases();
            foreach (var db in Directory.GetFiles(temp, "*.db", SearchOption.AllDirectories))
            {
                string relatif = Normaliser(db.Substring(temp.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                string nom;
                noms.TryGetValue(relatif, out nom);
                contenu.Dossiers.Add(new DossierSauvegarde { RaisonSociale = nom, Fichier = relatif, Niveau = SQLiteService.LireVersionFichier(db, cleBases) });
            }
            contenu.Fichiers = Directory.GetFiles(temp, "*", SearchOption.AllDirectories).Count(f => !string.Equals(Path.GetFileName(f), NomManifeste, StringComparison.OrdinalIgnoreCase));
            contenu.Dossiers.Sort((a, b) => string.Compare(a.Libelle, b.Libelle, StringComparison.CurrentCultureIgnoreCase));
            return contenu;
        }

        private static void ExtraireArchive(ZipArchive zip, string temp, bool refuserDoublons = false)
        {
            foreach (var entree in zip.Entries)
            {
                if (string.IsNullOrEmpty(entree.Name)) continue;   // répertoire
                string cible = Path.GetFullPath(Path.Combine(temp, entree.FullName));
                if (!EstSous(cible, temp)) throw new InvalidOperationException("chemin non autorisé dans l'archive : " + entree.FullName);
                if (refuserDoublons && File.Exists(cible)) throw new InvalidOperationException("Cette sauvegarde est incohérente : " + entree.FullName + " y figure deux fois.");
                Directory.CreateDirectory(Path.GetDirectoryName(cible));
                entree.ExtractToFile(cible, true);
            }
        }

        // ═══════════════════════════════════════════════════════════════════
        //  Restauration
        // ═══════════════════════════════════════════════════════════════════

        /// <summary>Raison pour laquelle cette sauvegarde ne peut pas être restaurée, ou null.</summary>
        public static string Obstacle(ContenuSauvegarde c)
        {
            if (c == null || !c.EstSauvegardeSigma) return "Ce fichier n'est pas une sauvegarde de Sigma Paie & RH.";
            if (c.MotDePasseRequis) return "Cette sauvegarde est protégée par un mot de passe : saisissez-le pour la lire.";
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
        /// Remet les données dans l'état de la sauvegarde, ouverte par <paramref name="motDePasse"/>
        /// (ignoré pour une ancienne sauvegarde en clair). L'état actuel est d'abord sauvegardé dans
        /// <paramref name="cheminSauvegardeSecurite"/>, protégé par <paramref name="motDePasseSecurite"/>
        /// (à défaut, par <paramref name="motDePasse"/>) : sans elle, rien n'est touché. Les bases sont
        /// rechiffrées avec la clé de ce poste et <c>users.json</c> avec son DPAPI. Les fichiers créés
        /// depuis la sauvegarde ne sont pas supprimés (ils ne sont plus référencés, et restent dans la
        /// sauvegarde de sécurité). À faire sans dossier ouvert, puis relancer le programme.
        /// </summary>
        public ResultatRestauration Restaurer(string cheminZip, string motDePasse, string cheminSauvegardeSecurite, string motDePasseSecurite, string versionProgramme)
        {
            string temp = DossierTemporaire();
            try
            {
                string cleSauvegarde;
                var contenu = Extraire(cheminZip, temp, motDePasse ?? "", out cleSauvegarde);
                var obstacle = Obstacle(contenu);
                if (obstacle != null) throw new InvalidOperationException(obstacle);

                var resultat = new ResultatRestauration { Contenu = contenu };
                if (Directory.Exists(_racine) && Directory.EnumerateFiles(_racine, "*", SearchOption.AllDirectories).Any(f => !EstExclu(f)))
                {
                    Sauvegarder(cheminSauvegardeSecurite, motDePasseSecurite ?? motDePasse, versionProgramme);
                    resultat.SauvegardeDeSecurite = cheminSauvegardeSecurite;
                }

                string cleLocale = _cleBases();
                foreach (var fichier in Directory.GetFiles(temp, "*", SearchOption.AllDirectories))
                {
                    string relatif = fichier.Substring(temp.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                    if (string.Equals(relatif, NomManifeste, StringComparison.OrdinalIgnoreCase)) continue;
                    if (EstExclu(fichier)) continue;   // dont la clé du poste : une archive ne la remplace jamais
                    string cible = Path.GetFullPath(Path.Combine(_racine, relatif));
                    if (!EstSous(cible, _racine)) continue;
                    Directory.CreateDirectory(Path.GetDirectoryName(cible));
                    string source = fichier;
                    if (string.Equals(Path.GetExtension(cible), ".db", StringComparison.OrdinalIgnoreCase))
                    {
                        SupprimerSiPresent(cible + "-journal");
                        SupprimerSiPresent(cible + SQLiteService.SuffixeCopieMigration);
                        SupprimerSiPresent(cible + ChiffrementBases.SuffixeCopie);
                        // Les copies « avant mise à niveau » de la base remplacée ne correspondent
                        // plus à la base restaurée : elles sont renommées, pour qu'une prochaine
                        // mise à niveau dépose une copie de la base restaurée.
                        if (Directory.Exists(Path.GetDirectoryName(cible)))
                            foreach (var bak in Directory.GetFiles(Path.GetDirectoryName(cible), Path.GetFileName(cible) + ".avant-v*.bak"))
                                if (!bak.Contains(".remplace-"))
                                    try { File.Move(bak, bak.Substring(0, bak.Length - 4) + ".remplace-" + DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + ".bak"); }
                                    catch { }

                        // Base chiffrée par la clé de la sauvegarde : rechiffrée avec celle de ce poste.
                        if (cleSauvegarde != null && SQLiteService.LireVersionFichier(fichier, cleSauvegarde) >= 0)
                        {
                            source = fichier + ".poste";
                            string erreur;
                            if (!CopierBase(fichier, source, cleSauvegarde, cleLocale, out erreur))
                                throw new InvalidOperationException("Le rechiffrement de " + relatif + " pour ce poste a échoué : " + erreur);
                        }
                    }
                    else if (contenu.Protegee && EstFichierComptes(relatif))
                    {
                        // Comptes rangés en clair dans le coffre : protégés par le DPAPI de ce poste.
                        byte[] donnees = File.ReadAllBytes(fichier);
                        if (EstJson(donnees))
                        {
                            source = fichier + ".poste";
                            File.WriteAllBytes(source, UserAccountService.ProtegerPourCePoste(donnees));
                        }
                        Array.Clear(donnees, 0, donnees.Length);
                    }
                    File.Copy(source, cible, true);
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
            // La clé des bases et le mot de passe de sauvegarde sont propres au poste (DPAPI) :
            // jamais sauvegardés ni restaurés.
            if (nom.StartsWith(CleDonnees.NomFichier, StringComparison.OrdinalIgnoreCase)) return true;
            if (nom.StartsWith(MotDePasseSauvegarde.NomFichier, StringComparison.OrdinalIgnoreCase)) return true;
            if (ExtensionsExclues.Any(e => nom.EndsWith(e, StringComparison.OrdinalIgnoreCase))) return true;
            if (SuffixesExclus.Any(s => nom.EndsWith(s, StringComparison.OrdinalIgnoreCase))) return true;
            if (nom.EndsWith(".poste", StringComparison.OrdinalIgnoreCase)) return true;
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
