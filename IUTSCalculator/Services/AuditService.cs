using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using IUTSCalculator.Models;

namespace IUTSCalculator.Services
{
    /// <summary>
    /// Service de journalisation et d'audit des opérations (module ADM-02).
    ///
    /// Trace qui a changé quel salaire, quand, et quelle était la valeur précédente.
    ///
    /// Scellement (défaut 21 du guide de formation 1.4.37.1) : l'écran annonçait un
    /// journal « scellé » qui ne l'était pas. Chaque entrée écrite porte désormais
    /// l'empreinte SHA-256 de son contenu ET de l'empreinte de l'entrée précédente
    /// (colonnes Empreinte et EmpreintePrecedente, migration 38). Modifier, supprimer ou
    /// insérer une entrée après coup rompt la chaîne, ce que <see cref="VerifierIntegriteAsync"/>
    /// détecte. Les entrées antérieures à la migration n'ont pas d'empreinte : elles sont
    /// présentées comme « non scellées ». Tant que la base n'a pas les colonnes, les
    /// écritures continuent sans empreinte, comme avant.
    /// </summary>
    public class AuditService
    {
        // ── Entités journalisées (valeurs de la colonne Entite, filtre « Entité / Module ») ─

        public const string EntiteSalarie = "Salarié";
        public const string EntitePeriode = "Période";
        public const string EntiteVariables = "Variables";
        public const string EntiteBaremes = "Barèmes";
        public const string EntiteCalcul = "Calcul";
        public const string EntiteEcrituresPaie = "Écritures de paie";

        /// <summary>Empreinte « précédente » de la toute première entrée scellée.</summary>
        public const string EmpreinteOrigine = "0000000000000000000000000000000000000000000000000000000000000000";

        /// <summary>Préfixe versionné du contenu haché : changer le calcul imposerait un « v2 ».</summary>
        private const string PrefixeEmpreinte = "SIGMA-AUDIT-1";

        private readonly SQLiteService _sqliteService;

        public AuditService(SQLiteService sqliteService)
        {
            if (sqliteService == null) throw new ArgumentNullException(nameof(sqliteService));
            _sqliteService = sqliteService;
        }

        private static string UtilisateurCourant(string repli = null)
        {
            if (!string.IsNullOrWhiteSpace(repli)) return repli;

            try
            {
                if (UserSession.Instance.IsAuthenticated && UserSession.Instance.CurrentUser != null)
                {
                    return UserSession.Instance.CurrentUser.Username ??
                           UserSession.Instance.CurrentUser.DisplayName ??
                           Environment.UserName;
                }
            }
            catch
            {
                // Contexte hors WPF ou session non initialisée
            }

            return Environment.UserName ?? "Système";
        }

        private static string MachineCourante()
        {
            try
            {
                return Environment.MachineName ?? "Localhost";
            }
            catch
            {
                return "Inconnue";
            }
        }

        // ── Écriture dans le journal ─────────────────────────────────────────

        /// <summary>
        /// Enregistre un événement élémentaire dans le journal d'audit.
        /// </summary>
        public async Task EnregistrerAsync(
            string action,
            string entite,
            string entiteId,
            string champ = null,
            string ancienneValeur = null,
            string nouvelleValeur = null,
            string details = null,
            string utilisateur = null)
        {
            if (string.IsNullOrWhiteSpace(action)) throw new ArgumentNullException(nameof(action));
            if (string.IsNullOrWhiteSpace(entite)) throw new ArgumentNullException(nameof(entite));

            // Transaction immédiate : lire la dernière empreinte et insérer la suivante doivent
            // se faire sous le même verrou d'écriture, sinon deux écritures simultanées
            // (deux écrans, deux postes sur le même dossier) chaîneraient sur la même entrée.
            using (var conn = _sqliteService.CreateConnection())
            using (var tx = conn.BeginTransaction())
            {
                try
                {
                    await EnregistrerDansTransactionAsync(conn, tx, action, entite, entiteId,
                        champ, ancienneValeur, nouvelleValeur, details, utilisateur);
                    tx.Commit();
                }
                catch
                {
                    try { tx.Rollback(); }
                    catch { /* connexion perdue */ }
                    throw;
                }
            }
        }

        /// <summary>
        /// Même enregistrement, dans la transaction de l'appelant : l'entrée d'audit est
        /// validée — ou annulée — avec l'opération qu'elle décrit (barème, bulletin,
        /// paramètres comptables). Ouvrir une seconde connexion pendant que l'appelant
        /// tient le verrou d'écriture bloquerait jusqu'au délai d'attente.
        /// </summary>
        public static async Task EnregistrerDansTransactionAsync(
            SQLiteConnection conn,
            SQLiteTransaction tx,
            string action,
            string entite,
            string entiteId,
            string champ = null,
            string ancienneValeur = null,
            string nouvelleValeur = null,
            string details = null,
            string utilisateur = null)
        {
            if (conn == null) throw new ArgumentNullException(nameof(conn));
            if (string.IsNullOrWhiteSpace(action)) throw new ArgumentNullException(nameof(action));
            if (string.IsNullOrWhiteSpace(entite)) throw new ArgumentNullException(nameof(entite));

            string user = UtilisateurCourant(utilisateur);
            string machine = MachineCourante();
            string dateStr = DbValue.DateToDb(DateTime.Now);

            string vEntiteId = entiteId ?? "";
            string vChamp = champ ?? "";
            string vAncienne = ancienneValeur ?? "";
            string vNouvelle = nouvelleValeur ?? "";
            string vDetails = details ?? "";

            bool scelle = ScellementActif(conn, tx);
            string precedente = null;
            string empreinte = null;

            if (scelle)
            {
                precedente = await DerniereEmpreinteAsync(conn, tx);
                empreinte = CalculerEmpreinte(dateStr, user, action, entite, vEntiteId, vChamp,
                    vAncienne, vNouvelle, vDetails, machine, precedente);
            }

            using (var cmd = conn.CreateCommand())
            {
                if (tx != null) cmd.Transaction = tx;

                cmd.CommandText = scelle
                    ? @"
                    INSERT INTO JournalAudit
                    (DateAction, Utilisateur, Action, Entite, EntiteId, ChampModifie, AncienneValeur, NouvelleValeur, Details, AdresseMachine, Empreinte, EmpreintePrecedente)
                    VALUES
                    (@date, @user, @action, @entite, @entiteId, @champ, @ancienne, @nouvelle, @details, @machine, @empreinte, @precedente);"
                    : @"
                    INSERT INTO JournalAudit
                    (DateAction, Utilisateur, Action, Entite, EntiteId, ChampModifie, AncienneValeur, NouvelleValeur, Details, AdresseMachine)
                    VALUES
                    (@date, @user, @action, @entite, @entiteId, @champ, @ancienne, @nouvelle, @details, @machine);";

                cmd.Parameters.AddWithValue("@date", dateStr);
                cmd.Parameters.AddWithValue("@user", user);
                cmd.Parameters.AddWithValue("@action", action);
                cmd.Parameters.AddWithValue("@entite", entite);
                cmd.Parameters.AddWithValue("@entiteId", vEntiteId);
                cmd.Parameters.AddWithValue("@champ", vChamp);
                cmd.Parameters.AddWithValue("@ancienne", vAncienne);
                cmd.Parameters.AddWithValue("@nouvelle", vNouvelle);
                cmd.Parameters.AddWithValue("@details", vDetails);
                cmd.Parameters.AddWithValue("@machine", machine);
                if (scelle)
                {
                    cmd.Parameters.AddWithValue("@empreinte", empreinte);
                    cmd.Parameters.AddWithValue("@precedente", precedente);
                }

                await cmd.ExecuteNonQueryAsync();
            }
        }

        // ── Scellement : empreintes et vérification ──────────────────────────

        /// <summary>
        /// Empreinte SHA-256 (hexadécimal minuscule) d'une entrée : chaque champ, tel qu'il
        /// est stocké, est préfixé de sa longueur — aucun découpage ambigu possible —, puis
        /// l'empreinte de l'entrée précédente est ajoutée. Un seul caractère changé dans un
        /// champ, ou une entrée retirée de la chaîne, donne une empreinte différente.
        /// </summary>
        public static string CalculerEmpreinte(
            string dateAction, string utilisateur, string action, string entite, string entiteId,
            string champ, string ancienneValeur, string nouvelleValeur, string details, string machine,
            string empreintePrecedente)
        {
            var sb = new StringBuilder(PrefixeEmpreinte);
            foreach (string valeur in new[]
            {
                dateAction, utilisateur, action, entite, entiteId,
                champ, ancienneValeur, nouvelleValeur, details, machine
            })
            {
                string v = valeur ?? "";
                sb.Append('|').Append(v.Length.ToString(CultureInfo.InvariantCulture)).Append(':').Append(v);
            }
            sb.Append('|').Append(empreintePrecedente ?? "");

            using (var sha = SHA256.Create())
            {
                byte[] octets = sha.ComputeHash(Encoding.UTF8.GetBytes(sb.ToString()));
                var hex = new StringBuilder(octets.Length * 2);
                foreach (byte b in octets)
                    hex.Append(b.ToString("x2", CultureInfo.InvariantCulture));
                return hex.ToString();
            }
        }

        /// <summary>Vrai si la table porte les colonnes de scellement (migration 38).</summary>
        private static bool ScellementActif(SQLiteConnection conn, SQLiteTransaction tx)
        {
            bool empreinte = false, precedente = false;
            using (var cmd = conn.CreateCommand())
            {
                if (tx != null) cmd.Transaction = tx;
                cmd.CommandText = "PRAGMA table_info(JournalAudit);";
                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        string nom = Convert.ToString(reader["name"], CultureInfo.InvariantCulture);
                        if (string.Equals(nom, "Empreinte", StringComparison.OrdinalIgnoreCase)) empreinte = true;
                        if (string.Equals(nom, "EmpreintePrecedente", StringComparison.OrdinalIgnoreCase)) precedente = true;
                    }
                }
            }
            return empreinte && precedente;
        }

        private static async Task<string> DerniereEmpreinteAsync(SQLiteConnection conn, SQLiteTransaction tx)
        {
            using (var cmd = conn.CreateCommand())
            {
                if (tx != null) cmd.Transaction = tx;
                cmd.CommandText = "SELECT Empreinte FROM JournalAudit ORDER BY Id DESC LIMIT 1;";
                string derniere = DbValue.TextFromDb(await cmd.ExecuteScalarAsync());
                return string.IsNullOrEmpty(derniere) ? EmpreinteOrigine : derniere;
            }
        }

        private static bool TableExiste(SQLiteConnection conn, string table)
        {
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = @n;";
                cmd.Parameters.AddWithValue("@n", table);
                return DbValue.IntFromDb(cmd.ExecuteScalar()) > 0;
            }
        }

        /// <summary>
        /// Dernier n° d'entrée antérieur au scellement, noté par la migration 38 dans
        /// JournalAuditScellement ; -1 s'il est inconnu.
        /// </summary>
        private static long BorneScellement(SQLiteConnection conn)
        {
            if (!TableExiste(conn, "JournalAuditScellement")) return -1;
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "SELECT DernierIdNonScelle FROM JournalAuditScellement WHERE Id = 1;";
                object v = cmd.ExecuteScalar();
                return (v == null || v == DBNull.Value) ? -1 : DbValue.LongFromDb(v);
            }
        }

        /// <summary>Empreinte de la dernière entrée du journal (vide si non scellé).</summary>
        public async Task<string> ObtenirEmpreinteDeTeteAsync()
        {
            using (var conn = _sqliteService.CreateConnection())
            {
                if (!ScellementActif(conn, null)) return "";
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT Empreinte FROM JournalAudit WHERE IFNULL(Empreinte, '') <> '' ORDER BY Id DESC LIMIT 1;";
                    return DbValue.TextFromDb(await cmd.ExecuteScalarAsync()) ?? "";
                }
            }
        }

        /// <summary>N° de la dernière entrée du journal (0 s'il est vide).</summary>
        public async Task<long> DernierIdAsync()
        {
            using (var conn = _sqliteService.CreateConnection())
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "SELECT IFNULL(MAX(Id), 0) FROM JournalAudit;";
                object v = await cmd.ExecuteScalarAsync();
                return (v == null || v == DBNull.Value) ? 0 : DbValue.LongFromDb(v);
            }
        }

        public async Task<bool> EstScelleAsync()
        {
            using (var conn = _sqliteService.CreateConnection())
            {
                await Task.Yield();
                return ScellementActif(conn, null);
            }
        }

        /// <summary>
        /// Recalcule toute la chaîne, de la plus ancienne entrée à la plus récente, et
        /// s'arrête à la première non conforme : contenu modifié (l'empreinte recalculée
        /// diffère), chaînage rompu (entrée supprimée ou insérée), empreinte effacée, ou
        /// fin du journal tronquée (le compteur AUTOINCREMENT a dépassé le dernier n°).
        ///
        /// Limite assumée : quelqu'un qui connaît l'algorithme et accède directement au
        /// fichier peut recalculer toute la chaîne. L'empreinte de tête, affichée et
        /// imprimée dans l'export, sert de point de repère externe : notée ailleurs (PV de
        /// clôture, courriel), elle permet de prouver qu'aucune entrée n'a été réécrite.
        /// </summary>
        public async Task<ResultatIntegriteAudit> VerifierIntegriteAsync()
        {
            var resultat = new ResultatIntegriteAudit { DateVerification = DateTime.Now };

            using (var conn = _sqliteService.CreateConnection())
            {
                resultat.ScellementActif = ScellementActif(conn, null);
                if (!resultat.ScellementActif) return resultat;

                long borne = BorneScellement(conn);
                string precedente = EmpreinteOrigine;
                bool chaineCommencee = false;
                long dernierId = 0;

                // Le compteur AUTOINCREMENT est lu AVANT les entrées : une entrée enregistrée
                // par un autre poste pendant la vérification ne doit pas être prise pour une
                // fin de journal supprimée (elle sera lue, ou simplement ignorée).
                long sequence = 0;
                bool sequenceConnue = TableExiste(conn, "sqlite_sequence");
                if (sequenceConnue)
                {
                    using (var cmdSeq = conn.CreateCommand())
                    {
                        cmdSeq.CommandText = "SELECT seq FROM sqlite_sequence WHERE name = 'JournalAudit';";
                        object v = cmdSeq.ExecuteScalar();
                        sequence = (v == null || v == DBNull.Value) ? 0 : DbValue.LongFromDb(v);
                    }
                }

                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = @"
                        SELECT Id, CAST(DateAction AS TEXT) AS DateAction, Utilisateur, Action, Entite, EntiteId,
                               ChampModifie, AncienneValeur, NouvelleValeur, Details, AdresseMachine,
                               Empreinte, EmpreintePrecedente
                        FROM JournalAudit
                        ORDER BY Id;";

                    using (var reader = (SQLiteDataReader)await cmd.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            long id = DbValue.LongFromDb(reader["Id"]);
                            dernierId = id;
                            resultat.EntreesControlees++;

                            string date = Texte(reader["DateAction"]);
                            string empreinte = Texte(reader["Empreinte"]);
                            string empreintePrecedente = Texte(reader["EmpreintePrecedente"]);

                            if (empreinte.Length == 0)
                            {
                                bool anterieure = borne >= 0 ? id <= borne : !chaineCommencee;
                                if (anterieure)
                                {
                                    resultat.EntreesNonScellees++;
                                    continue;
                                }

                                Signaler(resultat, id, date, string.Format(
                                    "l'entrée n° {0} ({1}) ne porte plus d'empreinte alors qu'elle est postérieure au scellement : elle a été modifiée en dehors de l'application.",
                                    id, DateLisible(date)));
                                break;
                            }

                            chaineCommencee = true;

                            if (!string.Equals(empreintePrecedente, precedente, StringComparison.Ordinal))
                            {
                                Signaler(resultat, id, date, string.Format(
                                    "la chaîne est rompue juste avant l'entrée n° {0} ({1}) : une ou plusieurs entrées ont été supprimées, insérées ou altérées à cet endroit.",
                                    id, DateLisible(date)));
                                break;
                            }

                            string recalculee = CalculerEmpreinte(
                                date,
                                Texte(reader["Utilisateur"]),
                                Texte(reader["Action"]),
                                Texte(reader["Entite"]),
                                Texte(reader["EntiteId"]),
                                Texte(reader["ChampModifie"]),
                                Texte(reader["AncienneValeur"]),
                                Texte(reader["NouvelleValeur"]),
                                Texte(reader["Details"]),
                                Texte(reader["AdresseMachine"]),
                                empreintePrecedente);

                            if (!string.Equals(recalculee, empreinte, StringComparison.OrdinalIgnoreCase))
                            {
                                Signaler(resultat, id, date, string.Format(
                                    "l'entrée n° {0} ({1}) a été modifiée après son enregistrement : son contenu ne correspond plus à son empreinte.",
                                    id, DateLisible(date)));
                                break;
                            }

                            resultat.EntreesScellees++;
                            precedente = empreinte;
                            resultat.EmpreinteDeTete = empreinte;
                        }
                    }
                }

                // Fin du journal tronquée : la chaîne seule ne voit pas qu'on a retiré les
                // dernières entrées. Le compteur AUTOINCREMENT, lui, garde le plus grand n°
                // jamais attribué.
                if (resultat.Anomalie == null && sequenceConnue && sequence > dernierId && sequence > borne)
                {
                    long premierManquant = Math.Max(dernierId, Math.Max(borne, 0)) + 1;
                    resultat.PremiereEntreeAlteree = premierManquant;
                    resultat.Anomalie = string.Format(
                        "les dernières entrées du journal (n° {0} à {1}) ont été supprimées.",
                        premierManquant, sequence);
                }
            }

            return resultat;
        }

        private static void Signaler(ResultatIntegriteAudit resultat, long id, string date, string anomalie)
        {
            resultat.PremiereEntreeAlteree = id;
            DateTime d;
            if (DateTime.TryParse(date, CultureInfo.InvariantCulture, DateTimeStyles.None, out d))
                resultat.DatePremiereEntreeAlteree = d;
            resultat.Anomalie = anomalie;
        }

        private static string Texte(object valeur)
        {
            return (valeur == null || valeur == DBNull.Value) ? "" : Convert.ToString(valeur, CultureInfo.InvariantCulture);
        }

        private static string DateLisible(string date)
        {
            DateTime d;
            return DateTime.TryParse(date, CultureInfo.InvariantCulture, DateTimeStyles.None, out d)
                ? d.ToString("dd/MM/yyyy HH:mm:ss", CultureInfo.InvariantCulture)
                : date;
        }

        // ── Méthodes spécialisées par domaine de paie ────────────────────────

        /// <summary>
        /// Audite en détail les modifications apportées à la fiche d'un salarié.
        /// Détecte champ par champ les changements de rémunération et d'état civil.
        /// </summary>
        public async Task AuditerModificationEmployeAsync(Employee ancien, Employee nouveau, string utilisateur = null)
        {
            if (nouveau == null) return;
            if (ancien == null)
            {
                await AuditerCreationEmployeAsync(nouveau, utilisateur);
                return;
            }

            string empId = nouveau.EmployeeId;
            string nomComplet = string.Format("{0} {1}", nouveau.LastName, nouveau.FirstName).Trim();
            int changements = 0;

            // 1. Salaire de base (exigence clé ADM-02)
            if (ancien.BaseSalary != nouveau.BaseSalary)
            {
                decimal diff = nouveau.BaseSalary - ancien.BaseSalary;
                string sens = diff > 0 ? string.Format("+{0:N0} FCFA", diff) : string.Format("{0:N0} FCFA", diff);
                await EnregistrerAsync(
                    action: "Modification salaire de base",
                    entite: "Salarié",
                    entiteId: empId,
                    champ: "Salaire de base",
                    ancienneValeur: string.Format("{0:N0} FCFA", ancien.BaseSalary),
                    nouvelleValeur: string.Format("{0:N0} FCFA", nouveau.BaseSalary),
                    details: string.Format("Évolution contractuelle pour {0} ({1}) : {2}", nomComplet, empId, sens),
                    utilisateur: utilisateur);
                changements++;
            }

            // 2. Indemnités contractuelles
            if (ancien.HousingAllowance != nouveau.HousingAllowance)
            {
                await EnregistrerAsync(
                    action: "Modification indemnité",
                    entite: "Salarié",
                    entiteId: empId,
                    champ: "Indemnité de logement",
                    ancienneValeur: string.Format("{0:N0} FCFA", ancien.HousingAllowance),
                    nouvelleValeur: string.Format("{0:N0} FCFA", nouveau.HousingAllowance),
                    details: string.Format("Logement pour {0} ({1})", nomComplet, empId),
                    utilisateur: utilisateur);
                changements++;
            }

            if (ancien.TransportAllowance != nouveau.TransportAllowance)
            {
                await EnregistrerAsync(
                    action: "Modification indemnité",
                    entite: "Salarié",
                    entiteId: empId,
                    champ: "Indemnité de transport",
                    ancienneValeur: string.Format("{0:N0} FCFA", ancien.TransportAllowance),
                    nouvelleValeur: string.Format("{0:N0} FCFA", nouveau.TransportAllowance),
                    details: string.Format("Transport pour {0} ({1})", nomComplet, empId),
                    utilisateur: utilisateur);
                changements++;
            }

            if (ancien.FunctionAllowance != nouveau.FunctionAllowance)
            {
                await EnregistrerAsync(
                    action: "Modification indemnité",
                    entite: "Salarié",
                    entiteId: empId,
                    champ: "Indemnité de fonction",
                    ancienneValeur: string.Format("{0:N0} FCFA", ancien.FunctionAllowance),
                    nouvelleValeur: string.Format("{0:N0} FCFA", nouveau.FunctionAllowance),
                    details: string.Format("Fonction pour {0} ({1})", nomComplet, empId),
                    utilisateur: utilisateur);
                changements++;
            }

            if (ancien.SujetionAllowance != nouveau.SujetionAllowance)
            {
                await EnregistrerAsync(
                    action: "Modification indemnité",
                    entite: "Salarié",
                    entiteId: empId,
                    champ: "Indemnité de sujétion",
                    ancienneValeur: string.Format("{0:N0} FCFA", ancien.SujetionAllowance),
                    nouvelleValeur: string.Format("{0:N0} FCFA", nouveau.SujetionAllowance),
                    details: string.Format("Sujétion pour {0} ({1})", nomComplet, empId),
                    utilisateur: utilisateur);
                changements++;
            }

            // 3. Charges de famille et statut fiscal
            if (ancien.FamilyCharges != nouveau.FamilyCharges)
            {
                await EnregistrerAsync(
                    action: "Modification situation",
                    entite: "Salarié",
                    entiteId: empId,
                    champ: "Charges de famille",
                    ancienneValeur: string.Format("{0}", ancien.FamilyCharges),
                    nouvelleValeur: string.Format("{0}", nouveau.FamilyCharges),
                    details: string.Format("Modification du nombre de personnes à charge pour {0} (impact réduction IUTS)", nomComplet),
                    utilisateur: utilisateur);
                changements++;
            }

            // 4. Coordonnées bancaires et mode de règlement (impact CPT-02)
            if (ancien.Banque != nouveau.Banque || ancien.NumeroCompte != nouveau.NumeroCompte)
            {
                await EnregistrerAsync(
                    action: "Modification bancaire",
                    entite: "Salarié",
                    entiteId: empId,
                    champ: "Compte bancaire",
                    ancienneValeur: string.Format("{0} - {1}", ancien.Banque ?? "(sans)", ancien.NumeroCompte ?? "(sans)"),
                    nouvelleValeur: string.Format("{0} - {1}", nouveau.Banque ?? "(sans)", nouveau.NumeroCompte ?? "(sans)"),
                    details: string.Format("Domiciliation pour {0} ({1})", nomComplet, empId),
                    utilisateur: utilisateur);
                changements++;
            }

            if (ancien.ModeReglement != nouveau.ModeReglement)
            {
                await EnregistrerAsync(
                    action: "Modification règlement",
                    entite: "Salarié",
                    entiteId: empId,
                    champ: "Mode de règlement",
                    ancienneValeur: ancien.ModeReglement ?? "(sans)",
                    nouvelleValeur: nouveau.ModeReglement ?? "(sans)",
                    details: string.Format("Mode de paiement pour {0}", nomComplet),
                    utilisateur: utilisateur);
                changements++;
            }

            // 5. Emploi / Catégorie
            if (ancien.Fonction != nouveau.Fonction || ancien.Category != nouveau.Category)
            {
                await EnregistrerAsync(
                    action: "Modification poste",
                    entite: "Salarié",
                    entiteId: empId,
                    champ: "Fonction / Catégorie",
                    ancienneValeur: string.Format("{0} ({1})", ancien.Fonction, ancien.Category),
                    nouvelleValeur: string.Format("{0} ({1})", nouveau.Fonction, nouveau.Category),
                    details: string.Format("Affectation professionnelle de {0}", nomComplet),
                    utilisateur: utilisateur);
                changements++;
            }

            if (changements == 0)
            {
                // Mise à jour mineure sans impact sur les champs surveillés ci-dessus
                await EnregistrerAsync(
                    action: "Mise à jour salarié",
                    entite: "Salarié",
                    entiteId: empId,
                    details: string.Format("Mise à jour des informations de {0} ({1})", nomComplet, empId),
                    utilisateur: utilisateur);
            }
        }

        public async Task AuditerCreationEmployeAsync(Employee employe, string utilisateur = null)
        {
            if (employe == null) return;

            string nomComplet = string.Format("{0} {1}", employe.LastName, employe.FirstName).Trim();
            await EnregistrerAsync(
                action: "Création salarié",
                entite: "Salarié",
                entiteId: employe.EmployeeId,
                champ: "Fiche complète",
                nouvelleValeur: string.Format("Salaire : {0:N0} FCFA", employe.BaseSalary),
                details: string.Format("Création du salarié {0} (Matricule {1}, Emploi : {2})",
                    nomComplet, employe.EmployeeId, employe.Fonction ?? "Non défini"),
                utilisateur: utilisateur);
        }

        public async Task AuditerSuppressionEmployeAsync(Employee employe, string utilisateur = null)
        {
            if (employe == null) return;

            string nomComplet = string.Format("{0} {1}", employe.LastName, employe.FirstName).Trim();
            await EnregistrerAsync(
                action: "Suppression salarié",
                entite: "Salarié",
                entiteId: employe.EmployeeId,
                ancienneValeur: string.Format("Dernier salaire : {0:N0} FCFA", employe.BaseSalary),
                details: string.Format("Suppression de la fiche de {0} (Matricule {1})", nomComplet, employe.EmployeeId),
                utilisateur: utilisateur);
        }

        public async Task AuditerPeriodeAsync(string action, int annee, int mois, string details, string utilisateur = null)
        {
            await EnregistrerAsync(
                action: action,
                entite: "Période",
                entiteId: string.Format("{0}-{1:D2}", annee, mois),
                details: details,
                utilisateur: utilisateur);
        }

        public async Task AuditerVariablesAsync(int annee, int mois, int nbLignes, decimal totalMontant, string details, string utilisateur = null)
        {
            await EnregistrerAsync(
                action: "Saisie variables",
                entite: "Variables",
                entiteId: string.Format("{0}-{1:D2}", annee, mois),
                champ: "Lignes de variables",
                nouvelleValeur: string.Format("{0} ligne(s), Total : {1:N0} FCFA", nbLignes, totalMontant),
                details: details ?? string.Format("Enregistrement de {0} variables pour la période {1}-{2:D2}", nbLignes, annee, mois),
                utilisateur: utilisateur);
        }

        /// <summary>
        /// Journalise l'enregistrement d'un bulletin ou d'un calcul (entité « Calcul »), dans
        /// la transaction qui l'enregistre — à appeler depuis DataService, seul point de
        /// passage de tous les écrans de paie (Bulletin de paie, Bulletins en masse, Calcul
        /// d'IUTS, Calcul en lot).
        /// </summary>
        /// <param name="periode">Mois de paie concerné.</param>
        /// <param name="bulletinArchive">Vrai si le document du bulletin est archivé avec le calcul.</param>
        /// <param name="lignesRemplacees">Nombre de calculs du même mois supprimés pour être remplacés.</param>
        public static Task AuditerEnregistrementCalculAsync(
            SQLiteConnection conn,
            SQLiteTransaction tx,
            TaxCalculation calcul,
            DateTime periode,
            bool bulletinArchive,
            int lignesRemplacees = 0)
        {
            if (calcul == null) return Task.FromResult(0);

            string matricule = calcul.Employee != null ? (calcul.Employee.EmployeeId ?? "") : "";
            string nom = calcul.Employee != null
                ? string.Format("{0} {1}", calcul.Employee.LastName, calcul.Employee.FirstName).Trim()
                : "";

            string nature;
            if (calcul.IsAnnual) nature = "Calcul annuel";
            else if (calcul.IsRegularization) nature = "Régularisation";
            else if (bulletinArchive) nature = "Bulletin mensuel";
            else nature = "Calcul mensuel";

            return EnregistrerDansTransactionAsync(
                conn, tx,
                action: bulletinArchive ? "Enregistrement bulletin" : "Enregistrement calcul",
                entite: EntiteCalcul,
                entiteId: matricule,
                champ: string.Format(CultureInfo.InvariantCulture, "Paie {0:MM/yyyy}", periode),
                ancienneValeur: lignesRemplacees > 0
                    ? string.Format("{0} calcul(s) du mois remplacé(s)", lignesRemplacees)
                    : "",
                nouvelleValeur: string.Format("Brut {0:N0} — IUTS {1:N0} — Net {2:N0} FCFA",
                    calcul.SalaireBrut, calcul.IUTSNet, calcul.SalaireNetaPayer),
                details: string.Format(
                    "{0} de {1} ({2}) pour {3:MM/yyyy}{4} : CNSS salariale {5:N0}, CNSS patronale {6:N0}, TPA {7:N0} FCFA.",
                    nature, string.IsNullOrEmpty(nom) ? "salarié" : nom, matricule, periode,
                    bulletinArchive ? ", document archivé" : "",
                    calcul.CnssEmployee, calcul.CnssPartPatronale, calcul.TaxePatronale));
        }

        /// <summary>
        /// Journalise la suppression d'un calcul depuis l'Historique (entité « Calcul »),
        /// dans la transaction qui le supprime.
        /// </summary>
        public static Task AuditerSuppressionCalculAsync(
            SQLiteConnection conn,
            SQLiteTransaction tx,
            string calculationId,
            DateTime dateCalcul,
            string employeeId = null)
        {
            return EnregistrerDansTransactionAsync(
                conn, tx,
                action: "Suppression calcul",
                entite: EntiteCalcul,
                entiteId: employeeId ?? "",
                champ: string.Format(CultureInfo.InvariantCulture, "Paie {0:MM/yyyy}", dateCalcul),
                ancienneValeur: calculationId ?? "",
                details: string.Format("Calcul du {0:dd/MM/yyyy} supprimé depuis l'Historique.", dateCalcul));
        }

        // ── Lecture et filtrage multicritère ─────────────────────────────────

        /// <summary>Clause WHERE commune à la recherche, au comptage et aux statistiques.</summary>
        private static string ClauseFiltre(FiltreAudit filtre, List<SQLiteParameter> parametres)
        {
            var sql = new StringBuilder(" WHERE 1=1 ");
            if (filtre == null) return sql.ToString();

            if (filtre.IdMax.HasValue)
            {
                sql.Append(" AND Id <= @idmax");
                parametres.Add(new SQLiteParameter("@idmax", filtre.IdMax.Value));
            }

            if (filtre.DateDebut.HasValue)
            {
                sql.Append(" AND DateAction >= @debut");
                parametres.Add(new SQLiteParameter("@debut", DbValue.DateToDb(filtre.DateDebut.Value.Date)));
            }

            if (filtre.DateFin.HasValue)
            {
                sql.Append(" AND DateAction <= @fin");
                parametres.Add(new SQLiteParameter("@fin", DbValue.DateToDb(filtre.DateFin.Value.Date.AddDays(1).AddTicks(-1))));
            }

            if (!string.IsNullOrWhiteSpace(filtre.Utilisateur) &&
                filtre.Utilisateur != "Tous les utilisateurs")
            {
                sql.Append(" AND Utilisateur = @user");
                parametres.Add(new SQLiteParameter("@user", filtre.Utilisateur));
            }

            if (!string.IsNullOrWhiteSpace(filtre.Entite) &&
                filtre.Entite != "Toutes les entités")
            {
                sql.Append(" AND Entite = @entite");
                parametres.Add(new SQLiteParameter("@entite", filtre.Entite));
            }

            if (!string.IsNullOrWhiteSpace(filtre.Action) &&
                filtre.Action != "Toutes les actions")
            {
                if (filtre.Action == "Modifications salariales")
                {
                    sql.Append(" AND (Action LIKE '%salaire%' OR ChampModifie LIKE '%salaire%' OR ChampModifie LIKE '%indemnité%')");
                }
                else if (filtre.Action == "Clôtures de période")
                {
                    sql.Append(" AND (Action LIKE '%clôture%' OR Action LIKE '%cloture%')");
                }
                else if (filtre.Action == "Créations / Suppressions")
                {
                    sql.Append(" AND (Action LIKE '%création%' OR Action LIKE '%suppression%')");
                }
                else
                {
                    sql.Append(" AND Action = @action");
                    parametres.Add(new SQLiteParameter("@action", filtre.Action));
                }
            }

            if (!string.IsNullOrWhiteSpace(filtre.RechercheTexte))
            {
                sql.Append(" AND (EntiteId LIKE @txt OR ChampModifie LIKE @txt OR Details LIKE @txt OR AncienneValeur LIKE @txt OR NouvelleValeur LIKE @txt)");
                parametres.Add(new SQLiteParameter("@txt", "%" + filtre.RechercheTexte.Trim() + "%"));
            }

            return sql.ToString();
        }

        /// <summary>
        /// Entrées répondant au filtre, des plus récentes aux plus anciennes. Une page de
        /// <see cref="FiltreAudit.Limite"/> entrées à partir de <see cref="FiltreAudit.Decalage"/> ;
        /// Limite à 0 : toutes les entrées, sans plafond (exports). Le plafond fixe de 500
        /// entrées, appliqué sans le dire, faisait disparaître l'historique au-delà.
        /// </summary>
        public async Task<List<EntreeAudit>> RechercherAsync(FiltreAudit filtre)
        {
            var resultats = new List<EntreeAudit>();
            var parametres = new List<SQLiteParameter>();

            using (var conn = _sqliteService.CreateConnection())
            {
                bool scelle = ScellementActif(conn, null);

                var sql = new StringBuilder(@"
                SELECT Id, DateAction, Utilisateur, Action, Entite, EntiteId,
                       ChampModifie, AncienneValeur, NouvelleValeur, Details, AdresseMachine"
                    + (scelle ? ", Empreinte, EmpreintePrecedente" : "") + @"
                FROM JournalAudit ");
                sql.Append(ClauseFiltre(filtre, parametres));
                sql.Append(" ORDER BY DateAction DESC, Id DESC");

                int limite = filtre == null ? 500 : filtre.Limite;
                if (limite > 0)
                {
                    int decalage = filtre != null && filtre.Decalage > 0 ? filtre.Decalage : 0;
                    sql.Append(string.Format(CultureInfo.InvariantCulture, " LIMIT {0} OFFSET {1}", limite, decalage));
                }

                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = sql.ToString();
                    foreach (var p in parametres)
                        cmd.Parameters.Add(p);

                    using (var reader = (SQLiteDataReader)await cmd.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            resultats.Add(new EntreeAudit
                            {
                                Id = DbValue.LongFromDb(reader["Id"]),
                                DateAction = DbValue.DateFromDb(reader["DateAction"], DateTime.Now),
                                Utilisateur = DbValue.TextFromDb(reader["Utilisateur"]),
                                Action = DbValue.TextFromDb(reader["Action"]),
                                Entite = DbValue.TextFromDb(reader["Entite"]),
                                EntiteId = DbValue.TextFromDb(reader["EntiteId"]),
                                ChampModifie = DbValue.TextFromDb(reader["ChampModifie"]),
                                AncienneValeur = DbValue.TextFromDb(reader["AncienneValeur"]),
                                NouvelleValeur = DbValue.TextFromDb(reader["NouvelleValeur"]),
                                Details = DbValue.TextFromDb(reader["Details"]),
                                AdresseMachine = DbValue.TextFromDb(reader["AdresseMachine"]),
                                Empreinte = scelle ? DbValue.TextFromDb(reader["Empreinte"]) : null,
                                EmpreintePrecedente = scelle ? DbValue.TextFromDb(reader["EmpreintePrecedente"]) : null
                            });
                        }
                    }
                }
            }

            return resultats;
        }

        /// <summary>Nombre total d'entrées répondant au filtre (pagination ignorée).</summary>
        public async Task<int> CompterAsync(FiltreAudit filtre)
        {
            var parametres = new List<SQLiteParameter>();
            using (var conn = _sqliteService.CreateConnection())
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "SELECT COUNT(*) FROM JournalAudit " + ClauseFiltre(filtre, parametres) + ";";
                foreach (var p in parametres)
                    cmd.Parameters.Add(p);
                return DbValue.IntFromDb(await cmd.ExecuteScalarAsync());
            }
        }

        /// <summary>
        /// Compteurs des cartes de l'écran, sur toutes les entrées du filtre (pagination
        /// ignorée), avec exactement les mêmes règles que <see cref="EntreeAudit"/>.
        /// </summary>
        public async Task<StatistiquesAudit> StatistiquesAsync(FiltreAudit filtre)
        {
            var stats = new StatistiquesAudit();
            var parametres = new List<SQLiteParameter>();

            using (var conn = _sqliteService.CreateConnection())
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "SELECT DateAction, Action, ChampModifie FROM JournalAudit " +
                                  ClauseFiltre(filtre, parametres) + " ORDER BY DateAction DESC, Id DESC;";
                foreach (var p in parametres)
                    cmd.Parameters.Add(p);

                using (var reader = (SQLiteDataReader)await cmd.ExecuteReaderAsync())
                {
                    var tampon = new EntreeAudit();
                    while (await reader.ReadAsync())
                    {
                        if (stats.Total == 0)
                            stats.DerniereActivite = DbValue.DateFromDb(reader["DateAction"], DateTime.Now);

                        stats.Total++;
                        tampon.Action = DbValue.TextFromDb(reader["Action"]);
                        tampon.ChampModifie = DbValue.TextFromDb(reader["ChampModifie"]);
                        if (tampon.EstModificationFinanciere) stats.ModificationsFinancieres++;
                        if (tampon.EstActionSensible) stats.ActionsSensibles++;
                    }
                }
            }

            return stats;
        }

        public async Task<List<string>> ObtenirUtilisateursDistinctsAsync()
        {
            var liste = new List<string>();
            using (var conn = _sqliteService.CreateConnection())
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "SELECT DISTINCT Utilisateur FROM JournalAudit WHERE Utilisateur IS NOT NULL AND Utilisateur <> '' ORDER BY Utilisateur;";
                using (var reader = (SQLiteDataReader)await cmd.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                    {
                        liste.Add(DbValue.TextFromDb(reader[0]));
                    }
                }
            }
            return liste;
        }

        public async Task<List<string>> ObtenirModulesDistinctsAsync()
        {
            var liste = new List<string>();
            using (var conn = _sqliteService.CreateConnection())
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "SELECT DISTINCT Entite FROM JournalAudit WHERE Entite IS NOT NULL AND Entite <> '' ORDER BY Entite;";
                using (var reader = (SQLiteDataReader)await cmd.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                    {
                        liste.Add(DbValue.TextFromDb(reader[0]));
                    }
                }
            }
            return liste;
        }
    }
}
