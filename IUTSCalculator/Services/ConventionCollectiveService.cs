using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Linq;
using System.Threading.Tasks;
using IUTSCalculator.Models;

namespace IUTSCalculator.Services
{
    /// <summary>
    /// Service de gestion des conventions collectives, des grilles de salaires minima
    /// et de valorisation de la prime d'ancienneté légale conventionnelle burkinabè (module SOC-05).
    /// </summary>
    public class ConventionCollectiveService
    {
        private readonly SQLiteService _sqliteService;
        private readonly AuditService _auditService;

        public ConventionCollectiveService(SQLiteService sqliteService, AuditService auditService = null)
        {
            _sqliteService = sqliteService ?? throw new ArgumentNullException(nameof(sqliteService));
            _auditService = auditService;
        }

        /// <summary>
        /// Charge l'ensemble des conventions collectives actives avec leurs effectifs rattachés.
        /// </summary>
        public async Task<List<ConventionCollective>> ObtenirToutesConventionsAsync()
        {
            var conventions = new List<ConventionCollective>();

            using (var conn = _sqliteService.CreateConnection())
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = @"
                    SELECT Id, Code, Libelle, Description, BaseCalculAnciennete, EstActive, DateCreation, DateModification
                    FROM ConventionsCollectives
                    ORDER BY Code;";

                using (var reader = (SQLiteDataReader)await cmd.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                    {
                        conventions.Add(LireConvention(reader));
                    }
                }
            }

            return conventions;
        }

        /// <summary>
        /// Charge une convention complète avec sa grille salariale et son barème d'ancienneté.
        /// </summary>
        public async Task<ConventionCollective> ObtenirParIdAsync(int id)
        {
            ConventionCollective conv = null;

            using (var conn = _sqliteService.CreateConnection())
            {
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = @"
                        SELECT Id, Code, Libelle, Description, BaseCalculAnciennete, EstActive, DateCreation, DateModification
                        FROM ConventionsCollectives
                        WHERE Id = @id;";
                    cmd.Parameters.AddWithValue("@id", id);

                    using (var reader = (SQLiteDataReader)await cmd.ExecuteReaderAsync())
                    {
                        if (await reader.ReadAsync())
                        {
                            conv = LireConvention(reader);
                        }
                    }
                }

                if (conv == null) return null;

                // Charger la grille des salaires
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = @"
                        SELECT Id, ConventionId, Categorie, Echelon, SalaireMinimum, Ordre, Description
                        FROM GrillesSalaires
                        WHERE ConventionId = @id
                        ORDER BY Ordre, Categorie, Echelon;";
                    cmd.Parameters.AddWithValue("@id", id);

                    using (var reader = (SQLiteDataReader)await cmd.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            conv.Grilles.Add(LireGrille(reader));
                        }
                    }
                }

                // Charger les tranches d'ancienneté
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = @"
                        SELECT Id, ConventionId, AnneesMin, AnneesMax, TauxPourcentage, Ordre
                        FROM TranchesAnciennete
                        WHERE ConventionId = @id
                        ORDER BY AnneesMin, Ordre;";
                    cmd.Parameters.AddWithValue("@id", id);

                    using (var reader = (SQLiteDataReader)await cmd.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            conv.TranchesAnciennete.Add(LireTranche(reader));
                        }
                    }
                }
            }

            return conv;
        }

        /// <summary>
        /// Enregistre ou met à jour une convention collective, sa grille et ses tranches en transaction atomique.
        /// </summary>
        public async Task<int> EnregistrerConventionAsync(ConventionCollective conv, string utilisateur = "admin")
        {
            if (conv == null) throw new ArgumentNullException(nameof(conv));
            if (string.IsNullOrWhiteSpace(conv.Code)) throw new ArgumentException("Le code de la convention est requis.");
            if (string.IsNullOrWhiteSpace(conv.Libelle)) throw new ArgumentException("Le libellé de la convention est requis.");

            string now = DbValue.DateToDb(DateTime.Now);

            using (var conn = _sqliteService.CreateConnection())
            using (var tx = conn.BeginTransaction())
            {
                try
                {
                    using (var cmd = conn.CreateCommand())
                    {
                        cmd.Transaction = tx;
                        if (conv.Id == 0)
                        {
                            cmd.CommandText = @"
                                INSERT INTO ConventionsCollectives (
                                    Code, Libelle, Description, BaseCalculAnciennete, EstActive, DateCreation, DateModification
                                ) VALUES (
                                    @code, @lib, @desc, @base, @active, @dc, @dm
                                );";
                            cmd.Parameters.AddWithValue("@code", conv.Code.Trim().ToUpperInvariant());
                            cmd.Parameters.AddWithValue("@lib", conv.Libelle.Trim());
                            cmd.Parameters.AddWithValue("@desc", conv.Description ?? string.Empty);
                            cmd.Parameters.AddWithValue("@base", (int)conv.BaseCalculAnciennete);
                            cmd.Parameters.AddWithValue("@active", conv.EstActive ? 1 : 0);
                            cmd.Parameters.AddWithValue("@dc", now);
                            cmd.Parameters.AddWithValue("@dm", now);

                            await cmd.ExecuteNonQueryAsync();
                            conv.Id = (int)conn.LastInsertRowId;
                        }
                        else
                        {
                            cmd.CommandText = @"
                                UPDATE ConventionsCollectives SET
                                    Code = @code, Libelle = @lib, Description = @desc,
                                    BaseCalculAnciennete = @base, EstActive = @active, DateModification = @dm
                                WHERE Id = @id;";
                            cmd.Parameters.AddWithValue("@id", conv.Id);
                            cmd.Parameters.AddWithValue("@code", conv.Code.Trim().ToUpperInvariant());
                            cmd.Parameters.AddWithValue("@lib", conv.Libelle.Trim());
                            cmd.Parameters.AddWithValue("@desc", conv.Description ?? string.Empty);
                            cmd.Parameters.AddWithValue("@base", (int)conv.BaseCalculAnciennete);
                            cmd.Parameters.AddWithValue("@active", conv.EstActive ? 1 : 0);
                            cmd.Parameters.AddWithValue("@dm", now);

                            await cmd.ExecuteNonQueryAsync();
                        }
                    }

                    // Enregistrer la grille salariale
                    using (var cmdDel = conn.CreateCommand())
                    {
                        cmdDel.Transaction = tx;
                        cmdDel.CommandText = "DELETE FROM GrillesSalaires WHERE ConventionId = @id;";
                        cmdDel.Parameters.AddWithValue("@id", conv.Id);
                        await cmdDel.ExecuteNonQueryAsync();
                    }

                    if (conv.Grilles != null && conv.Grilles.Count > 0)
                    {
                        foreach (var g in conv.Grilles)
                        {
                            using (var cmdIns = conn.CreateCommand())
                            {
                                cmdIns.Transaction = tx;
                                cmdIns.CommandText = @"
                                    INSERT INTO GrillesSalaires (
                                        ConventionId, Categorie, Echelon, SalaireMinimum, Ordre, Description
                                    ) VALUES (
                                        @conv, @cat, @ech, @sal, @ord, @desc
                                    );";
                                cmdIns.Parameters.AddWithValue("@conv", conv.Id);
                                cmdIns.Parameters.AddWithValue("@cat", g.Categorie?.Trim() ?? string.Empty);
                                cmdIns.Parameters.AddWithValue("@ech", g.Echelon?.Trim() ?? string.Empty);
                                cmdIns.Parameters.AddWithValue("@sal", DbValue.MoneyToDb(g.SalaireMinimum));
                                cmdIns.Parameters.AddWithValue("@ord", g.Ordre);
                                cmdIns.Parameters.AddWithValue("@desc", g.Description ?? string.Empty);
                                await cmdIns.ExecuteNonQueryAsync();
                            }
                        }
                    }

                    // Enregistrer les tranches d'ancienneté
                    using (var cmdDelTr = conn.CreateCommand())
                    {
                        cmdDelTr.Transaction = tx;
                        cmdDelTr.CommandText = "DELETE FROM TranchesAnciennete WHERE ConventionId = @id;";
                        cmdDelTr.Parameters.AddWithValue("@id", conv.Id);
                        await cmdDelTr.ExecuteNonQueryAsync();
                    }

                    if (conv.TranchesAnciennete != null && conv.TranchesAnciennete.Count > 0)
                    {
                        foreach (var tr in conv.TranchesAnciennete)
                        {
                            using (var cmdIns = conn.CreateCommand())
                            {
                                cmdIns.Transaction = tx;
                                cmdIns.CommandText = @"
                                    INSERT INTO TranchesAnciennete (
                                        ConventionId, AnneesMin, AnneesMax, TauxPourcentage, Ordre
                                    ) VALUES (
                                        @conv, @min, @max, @taux, @ord
                                    );";
                                cmdIns.Parameters.AddWithValue("@conv", conv.Id);
                                cmdIns.Parameters.AddWithValue("@min", tr.AnneesMin);
                                cmdIns.Parameters.AddWithValue("@max", tr.AnneesMax.HasValue ? (object)tr.AnneesMax.Value : DBNull.Value);
                                cmdIns.Parameters.AddWithValue("@taux", DbValue.DecimalToDb(tr.TauxPourcentage));
                                cmdIns.Parameters.AddWithValue("@ord", tr.Ordre);
                                await cmdIns.ExecuteNonQueryAsync();
                            }
                        }
                    }

                    tx.Commit();

                    // Audit
                    if (_auditService != null)
                    {
                        try
                        {
                            await _auditService.EnregistrerAsync(
                                action: "Enregistrement convention collective",
                                entite: "ConventionCollective",
                                entiteId: conv.Code,
                                champ: "Grilles & Tranches",
                                ancienneValeur: string.Empty,
                                nouvelleValeur: conv.Libelle,
                                details: $"Paramétrage convention '{conv.Code}' avec {conv.Grilles?.Count ?? 0} ligne(s) de grille et {conv.TranchesAnciennete?.Count ?? 0} tranche(s).",
                                utilisateur: utilisateur);
                        }
                        catch { }
                    }

                    return conv.Id;
                }
                catch
                {
                    tx.Rollback();
                    throw;
                }
            }
        }

        /// <summary>
        /// Supprime une convention collective et ses données associées.
        /// </summary>
        public async Task SupprimerConventionAsync(int id, string utilisateur = null)
        {
            if (id <= 0) return;

            using (var conn = _sqliteService.CreateConnection())
            using (var tx = conn.BeginTransaction())
            {
                try
                {
                    using (var cmdEmp = conn.CreateCommand())
                    {
                        cmdEmp.Transaction = tx;
                        cmdEmp.CommandText = "UPDATE Employees SET ConventionId = NULL WHERE ConventionId = @id;";
                        cmdEmp.Parameters.AddWithValue("@id", id);
                        await cmdEmp.ExecuteNonQueryAsync();
                    }

                    using (var cmdTr = conn.CreateCommand())
                    {
                        cmdTr.Transaction = tx;
                        cmdTr.CommandText = "DELETE FROM TranchesAnciennete WHERE ConventionId = @id;";
                        cmdTr.Parameters.AddWithValue("@id", id);
                        await cmdTr.ExecuteNonQueryAsync();
                    }

                    using (var cmdGr = conn.CreateCommand())
                    {
                        cmdGr.Transaction = tx;
                        cmdGr.CommandText = "DELETE FROM GrillesSalaires WHERE ConventionId = @id;";
                        cmdGr.Parameters.AddWithValue("@id", id);
                        await cmdGr.ExecuteNonQueryAsync();
                    }

                    using (var cmdConv = conn.CreateCommand())
                    {
                        cmdConv.Transaction = tx;
                        cmdConv.CommandText = "DELETE FROM ConventionsCollectives WHERE Id = @id;";
                        cmdConv.Parameters.AddWithValue("@id", id);
                        await cmdConv.ExecuteNonQueryAsync();
                    }

                    tx.Commit();

                    if (_auditService != null)
                    {
                        try
                        {
                            await _auditService.EnregistrerAsync(
                                action: "Suppression convention collective",
                                entite: "ConventionCollective",
                                entiteId: id.ToString(),
                                details: $"Suppression convention collective id {id}.",
                                utilisateur: utilisateur);
                        }
                        catch { }
                    }
                }
                catch
                {
                    tx.Rollback();
                    throw;
                }
            }
        }

        /// <summary>
        /// Enregistre ou met à jour une ligne de grille salariale.
        /// </summary>
        public async Task EnregistrerGrilleAsync(GrilleSalaire grille, string utilisateur = null)
        {
            if (grille == null || grille.ConventionId <= 0) return;

            using (var conn = _sqliteService.CreateConnection())
            using (var cmd = conn.CreateCommand())
            {
                if (grille.Id <= 0)
                {
                    cmd.CommandText = @"
                        INSERT OR REPLACE INTO GrillesSalaires (
                            ConventionId, Categorie, Echelon, SalaireMinimum, Ordre, Description
                        ) VALUES (
                            @conv, @cat, @ech, @sal, @ord, @desc
                        );";
                }
                else
                {
                    cmd.CommandText = @"
                        UPDATE GrillesSalaires SET
                            Categorie = @cat, Echelon = @ech, SalaireMinimum = @sal, Ordre = @ord, Description = @desc
                        WHERE Id = @id;";
                    cmd.Parameters.AddWithValue("@id", grille.Id);
                }

                cmd.Parameters.AddWithValue("@conv", grille.ConventionId);
                cmd.Parameters.AddWithValue("@cat", grille.Categorie?.Trim() ?? string.Empty);
                cmd.Parameters.AddWithValue("@ech", string.IsNullOrWhiteSpace(grille.Echelon) ? "A" : grille.Echelon.Trim());
                cmd.Parameters.AddWithValue("@sal", DbValue.MoneyToDb(grille.SalaireMinimum));
                cmd.Parameters.AddWithValue("@ord", grille.Ordre);
                cmd.Parameters.AddWithValue("@desc", grille.Description ?? string.Empty);

                await cmd.ExecuteNonQueryAsync();
            }
        }

        /// <summary>
        /// Supprime une ligne de grille salariale.
        /// </summary>
        public async Task SupprimerGrilleAsync(int id, string utilisateur = null)
        {
            if (id <= 0) return;

            using (var conn = _sqliteService.CreateConnection())
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "DELETE FROM GrillesSalaires WHERE Id = @id;";
                cmd.Parameters.AddWithValue("@id", id);
                await cmd.ExecuteNonQueryAsync();
            }
        }

        /// <summary>
        /// Enregistre ou met à jour une tranche d'ancienneté.
        /// </summary>
        public async Task EnregistrerTrancheAsync(TrancheAnciennete tranche, string utilisateur = null)
        {
            if (tranche == null || tranche.ConventionId <= 0) return;

            using (var conn = _sqliteService.CreateConnection())
            using (var cmd = conn.CreateCommand())
            {
                if (tranche.Id <= 0)
                {
                    cmd.CommandText = @"
                        INSERT INTO TranchesAnciennete (
                            ConventionId, AnneesMin, AnneesMax, TauxPourcentage, Ordre
                        ) VALUES (
                            @conv, @min, @max, @taux, @ord
                        );";
                }
                else
                {
                    cmd.CommandText = @"
                        UPDATE TranchesAnciennete SET
                            AnneesMin = @min, AnneesMax = @max, TauxPourcentage = @taux, Ordre = @ord
                        WHERE Id = @id;";
                    cmd.Parameters.AddWithValue("@id", tranche.Id);
                }

                cmd.Parameters.AddWithValue("@conv", tranche.ConventionId);
                cmd.Parameters.AddWithValue("@min", tranche.AnneesMin);
                cmd.Parameters.AddWithValue("@max", tranche.AnneesMax.HasValue ? (object)tranche.AnneesMax.Value : DBNull.Value);
                cmd.Parameters.AddWithValue("@taux", DbValue.DecimalToDb(tranche.TauxPourcentage));
                cmd.Parameters.AddWithValue("@ord", tranche.Ordre);

                await cmd.ExecuteNonQueryAsync();
            }
        }

        /// <summary>
        /// Supprime une tranche d'ancienneté.
        /// </summary>
        public async Task SupprimerTrancheAsync(int id, string utilisateur = null)
        {
            if (id <= 0) return;

            using (var conn = _sqliteService.CreateConnection())
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "DELETE FROM TranchesAnciennete WHERE Id = @id;";
                cmd.Parameters.AddWithValue("@id", id);
                await cmd.ExecuteNonQueryAsync();
            }
        }

        /// <summary>
        /// Calcule l'ancienneté et valorise la prime d'ancienneté d'un employé pour une période donnée.
        /// </summary>
        public CalculAncienneteResultat CalculerAnciennete(Employee employe, DateTime datePaie, ConventionCollective convention)
        {
            if (employe == null || convention == null)
                return CalculAncienneteResultat.Vide;

            if (employe.DateEntree == default(DateTime) || employe.DateEntree > datePaie)
                return CalculAncienneteResultat.Vide;

            int annees = employe.ObtenirAncienneteAnnees(datePaie);
            int mois = employe.ObtenirAncienneteMois(datePaie);

            var resultat = new CalculAncienneteResultat
            {
                Annees = annees,
                Mois = mois,
                SalaireContractuel = employe.BaseSalary
            };

            // Recherche du salaire minimum de la catégorie/échelon du salarié dans la grille
            GrilleSalaire grille = null;
            if (convention.Grilles != null && convention.Grilles.Count > 0 && !string.IsNullOrWhiteSpace(employe.Category))
            {
                grille = convention.Grilles.FirstOrDefault(g =>
                    string.Equals(g.Categorie?.Trim(), employe.Category?.Trim(), StringComparison.OrdinalIgnoreCase) &&
                    (string.IsNullOrWhiteSpace(employe.Echelon) || string.Equals(g.Echelon?.Trim(), employe.Echelon?.Trim(), StringComparison.OrdinalIgnoreCase)));

                if (grille == null && !string.IsNullOrWhiteSpace(employe.Category))
                {
                    // Fallback sur la catégorie seule si l'échelon n'est pas trouvé
                    grille = convention.Grilles.FirstOrDefault(g =>
                        string.Equals(g.Categorie?.Trim(), employe.Category?.Trim(), StringComparison.OrdinalIgnoreCase));
                }
            }

            resultat.SalaireMinimumConventionnel = grille != null ? grille.SalaireMinimum : 0m;

            // Détermination de la base de calcul
            if (convention.BaseCalculAnciennete == BaseCalculAnciennete.MinimumConventionnel && resultat.SalaireMinimumConventionnel > 0m)
            {
                resultat.BaseCalcul = resultat.SalaireMinimumConventionnel;
                resultat.BaseUtilisee = $"Minimum conventionnel ({grille.ClassificationComplete})";
            }
            else
            {
                resultat.BaseCalcul = employe.BaseSalary;
                resultat.BaseUtilisee = "Salaire de base contractuel";
            }

            // Recherche de la tranche d'ancienneté applicable.
            //
            // Le seuil d'éligibilité n'est PAS codé en dur ici : il vient entièrement de
            // AnneesMin sur la tranche la plus basse de la convention (ci-dessous). Un
            // ancien "annees >= 2" imposait un minimum de deux ans quelle que soit la
            // convention, empêchant toute prime d'ancienneté pour une convention dont la
            // première tranche démarre plus tôt (une tranche à partir d'un an, fréquente
            // dans les conventions collectives burkinabè) — signalé le 16 septembre 2026
            // (voir ETAT-DEVELOPPEMENT.md item 22). Sans tranche correspondante, la
            // recherche ci-dessous ne trouve simplement rien et TauxPourcentage reste à 0.
            if (convention.TranchesAnciennete != null && convention.TranchesAnciennete.Count > 0)
            {
                // Trouver la tranche qui encadre l'ancienneté révolue
                var tranche = convention.TranchesAnciennete
                    .Where(t => t.AnneesMin <= annees && (!t.AnneesMax.HasValue || annees < t.AnneesMax.Value))
                    .OrderByDescending(t => t.AnneesMin)
                    .FirstOrDefault();

                // Si aucune tranche exacte mais que l'ancienneté dépasse le max connu, prendre la tranche la plus élevée
                if (tranche == null)
                {
                    tranche = convention.TranchesAnciennete
                        .Where(t => t.AnneesMin <= annees)
                        .OrderByDescending(t => t.AnneesMin)
                        .FirstOrDefault();
                }

                if (tranche != null && tranche.TauxPourcentage > 0m)
                {
                    resultat.TauxPourcentage = tranche.TauxPourcentage;
                    resultat.LibelleTranche = tranche.LibelleTranche;
                    resultat.Montant = Math.Round(resultat.BaseCalcul * (resultat.TauxPourcentage / 100m), 2);
                }
            }

            return resultat;
        }

        /// <summary>
        /// Génère l'apport de paie de la prime d'ancienneté conventionnelle pour un salarié donné (rubrique PRIMEANC).
        /// </summary>
        public async Task<ApportConventionnel> ApportAsync(Employee employe, DateTime datePaie)
        {
            if (employe == null || !employe.ConventionId.HasValue || employe.ConventionId.Value <= 0)
                return ApportConventionnel.Vide;

            var conv = await ObtenirParIdAsync(employe.ConventionId.Value);
            if (conv == null || !conv.EstActive)
                return ApportConventionnel.Vide;

            var calcul = CalculerAnciennete(employe, datePaie, conv);
            if (!calcul.EstEligible)
                return ApportConventionnel.Vide;

            var lignes = new List<LigneApport>
            {
                new LigneApport(
                    code: "PRIMEANC",
                    libelle: $"Prime d'ancienneté ({calcul.TauxPourcentage:0.##}% — {calcul.Annees} ans)",
                    sens: SensRubrique.Gain,
                    quantite: calcul.TauxPourcentage,
                    montant: calcul.Montant,
                    soumisIUTS: true,
                    soumisCNSS: true,
                    soumisTPA: true)
            };

            return new ApportConventionnel(lignes, calcul.Montant);
        }

        /// <summary>
        /// Charge l'ensemble des apports conventionnels du mois pour un lot de salariés.
        /// </summary>
        public async Task<Dictionary<string, ApportConventionnel>> ChargerApportsMoisAsync(
            DateTime datePaie, IEnumerable<Employee> salaries)
        {
            var resultat = new Dictionary<string, ApportConventionnel>(StringComparer.OrdinalIgnoreCase);
            if (salaries == null) return resultat;

            var listeSalaries = salaries.Where(s => s != null && !string.IsNullOrWhiteSpace(s.EmployeeId)).ToList();
            if (listeSalaries.Count == 0) return resultat;

            // Charger toutes les conventions nécessaires en une seule passe
            var convIds = listeSalaries
                .Where(s => s.ConventionId.HasValue && s.ConventionId.Value > 0)
                .Select(s => s.ConventionId.Value)
                .Distinct()
                .ToList();

            if (convIds.Count == 0) return resultat;

            var mapConventions = new Dictionary<int, ConventionCollective>();
            foreach (var cid in convIds)
            {
                var c = await ObtenirParIdAsync(cid);
                if (c != null) mapConventions[cid] = c;
            }

            foreach (var emp in listeSalaries)
            {
                if (emp.ConventionId.HasValue && mapConventions.TryGetValue(emp.ConventionId.Value, out var conv) && conv.EstActive)
                {
                    var calc = CalculerAnciennete(emp, datePaie, conv);
                    if (calc.EstEligible)
                    {
                        var lignes = new List<LigneApport>
                        {
                            new LigneApport(
                                code: "PRIMEANC",
                                libelle: $"Prime d'ancienneté ({calc.TauxPourcentage:0.##}% — {calc.Annees} ans)",
                                sens: SensRubrique.Gain,
                                quantite: calc.TauxPourcentage,
                                montant: calc.Montant,
                                soumisIUTS: true,
                                soumisCNSS: true,
                                soumisTPA: true)
                        };
                        resultat[emp.EmployeeId] = new ApportConventionnel(lignes, calc.Montant);
                    }
                }
            }

            return resultat;
        }

        private static ConventionCollective LireConvention(SQLiteDataReader reader)
        {
            return new ConventionCollective
            {
                Id = DbValue.IntFromDb(reader["Id"]),
                Code = DbValue.TextFromDb(reader["Code"]),
                Libelle = DbValue.TextFromDb(reader["Libelle"]),
                Description = DbValue.TextFromDb(reader["Description"]),
                BaseCalculAnciennete = (BaseCalculAnciennete)DbValue.IntFromDb(reader["BaseCalculAnciennete"]),
                EstActive = DbValue.IntFromDb(reader["EstActive"]) == 1,
                DateCreation = DbValue.DateFromDb(reader["DateCreation"], DateTime.Now),
                DateModification = DbValue.DateFromDb(reader["DateModification"], DateTime.Now)
            };
        }

        private static GrilleSalaire LireGrille(SQLiteDataReader reader)
        {
            return new GrilleSalaire
            {
                Id = DbValue.IntFromDb(reader["Id"]),
                ConventionId = DbValue.IntFromDb(reader["ConventionId"]),
                Categorie = DbValue.TextFromDb(reader["Categorie"]),
                Echelon = DbValue.TextFromDb(reader["Echelon"]),
                SalaireMinimum = DbValue.MoneyFromDb(reader["SalaireMinimum"]),
                Ordre = DbValue.IntFromDb(reader["Ordre"]),
                Description = DbValue.TextFromDb(reader["Description"])
            };
        }

        private static TrancheAnciennete LireTranche(SQLiteDataReader reader)
        {
            return new TrancheAnciennete
            {
                Id = DbValue.IntFromDb(reader["Id"]),
                ConventionId = DbValue.IntFromDb(reader["ConventionId"]),
                AnneesMin = DbValue.IntFromDb(reader["AnneesMin"]),
                AnneesMax = reader["AnneesMax"] != DBNull.Value && reader["AnneesMax"] != null ? Convert.ToInt32(reader["AnneesMax"]) : (int?)null,
                TauxPourcentage = DbValue.DecimalFromDb(reader["TauxPourcentage"]),
                Ordre = DbValue.IntFromDb(reader["Ordre"])
            };
        }
    }

    /// <summary>
    /// Contient les rubriques conventionnelles apportées au bulletin de paie (module SOC-05).
    /// </summary>
    public class ApportConventionnel
    {
        public static readonly ApportConventionnel Vide = new ApportConventionnel(new List<LigneApport>(), 0m);

        public List<LigneApport> Lignes { get; }
        public decimal MontantTotal { get; }
        public bool EstVide => Lignes == null || Lignes.Count == 0 || MontantTotal <= 0m;

        public ApportConventionnel(List<LigneApport> lignes, decimal montantTotal)
        {
            Lignes = lignes ?? new List<LigneApport>();
            MontantTotal = montantTotal;
        }
    }
}
