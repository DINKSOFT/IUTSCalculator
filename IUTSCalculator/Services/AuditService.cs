using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Globalization;
using System.Text;
using System.Threading.Tasks;
using IUTSCalculator.Models;

namespace IUTSCalculator.Services
{
    /// <summary>
    /// Service de journalisation et d'audit des opérations (module ADM-02).
    ///
    /// Trace qui a changé quel salaire, quand, et quelle était la valeur précédente.
    /// </summary>
    public class AuditService
    {
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

            string user = UtilisateurCourant(utilisateur);
            string machine = MachineCourante();
            string dateStr = DbValue.DateToDb(DateTime.Now);

            using (var conn = _sqliteService.CreateConnection())
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = @"
                    INSERT INTO JournalAudit
                    (DateAction, Utilisateur, Action, Entite, EntiteId, ChampModifie, AncienneValeur, NouvelleValeur, Details, AdresseMachine)
                    VALUES
                    (@date, @user, @action, @entite, @entiteId, @champ, @ancienne, @nouvelle, @details, @machine);";

                cmd.Parameters.AddWithValue("@date", dateStr);
                cmd.Parameters.AddWithValue("@user", user);
                cmd.Parameters.AddWithValue("@action", action);
                cmd.Parameters.AddWithValue("@entite", entite);
                cmd.Parameters.AddWithValue("@entiteId", entiteId ?? "");
                cmd.Parameters.AddWithValue("@champ", champ ?? "");
                cmd.Parameters.AddWithValue("@ancienne", ancienneValeur ?? "");
                cmd.Parameters.AddWithValue("@nouvelle", nouvelleValeur ?? "");
                cmd.Parameters.AddWithValue("@details", details ?? "");
                cmd.Parameters.AddWithValue("@machine", machine);

                await cmd.ExecuteNonQueryAsync();
            }
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

        // ── Lecture et filtrage multicritère ─────────────────────────────────

        public async Task<List<EntreeAudit>> RechercherAsync(FiltreAudit filtre)
        {
            var resultats = new List<EntreeAudit>();
            var sql = new StringBuilder(@"
                SELECT Id, DateAction, Utilisateur, Action, Entite, EntiteId,
                       ChampModifie, AncienneValeur, NouvelleValeur, Details, AdresseMachine
                FROM JournalAudit
                WHERE 1=1 ");

            var parametres = new List<SQLiteParameter>();

            if (filtre != null)
            {
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
            }

            sql.Append(" ORDER BY DateAction DESC, Id DESC");

            int limite = (filtre != null && filtre.Limite > 0) ? filtre.Limite : 500;
            sql.Append(" LIMIT " + limite);

            using (var conn = _sqliteService.CreateConnection())
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
                            AdresseMachine = DbValue.TextFromDb(reader["AdresseMachine"])
                        });
                    }
                }
            }

            return resultats;
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
