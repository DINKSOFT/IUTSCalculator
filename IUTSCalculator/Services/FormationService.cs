using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Globalization;
using System.Threading.Tasks;
using IUTSCalculator.Models;

namespace IUTSCalculator.Services
{
    /// <summary>
    /// Service de gestion de la formation professionnelle, des sessions, des inscriptions
    /// et du suivi de la contrepartie fiscale TPA (module RH-04).
    /// </summary>
    public class FormationService
    {
        private readonly SQLiteService _sqliteService;

        public FormationService(SQLiteService sqliteService)
        {
            _sqliteService = sqliteService ?? throw new ArgumentNullException(nameof(sqliteService));
        }

        #region Plans de Formation

        /// <summary>
        /// Récupère l'ensemble des plans de formation, optionnellement filtrés par exercice.
        /// </summary>
        public async Task<List<PlanFormation>> ObtenirPlansAsync(int? annee = null)
        {
            var plans = new List<PlanFormation>();

            await Task.Run(() =>
            {
                using (var connection = _sqliteService.CreateConnection())
                {
                    using (var cmd = connection.CreateCommand())
                    {
                        // Chaque agrégat est calculé dans sa propre sous-requête : une jointure
                        // unique plans → sessions → inscriptions présentes répétait le coût et la
                        // durée d'une session autant de fois qu'elle comptait de présents
                        // (dépenses, heures et solde budgétaire gonflés).
                        // Statut de session 4 = Annulée (exclue) ; StatutPresence 1 = Présent.
                        string sql = @"
                            SELECT p.Id, p.Code, p.Titre, p.AnneeExercice, p.BudgetPrevisionnel,
                                   p.Statut, p.ObjectifsStrategiques, p.DateValidation, p.DateCreation,
                                   (SELECT COUNT(*)
                                      FROM SessionsFormation s
                                     WHERE s.PlanFormationId = p.Id AND s.Statut <> 4) AS NombreSessions,
                                   (SELECT COALESCE(SUM(s.CoutTotalReel), 0)
                                      FROM SessionsFormation s
                                     WHERE s.PlanFormationId = p.Id AND s.Statut <> 4) AS TotalDepenses,
                                   (SELECT COALESCE(SUM(fc.DureeHeures), 0)
                                      FROM SessionsFormation s
                                      INNER JOIN FormationsCatalogue fc ON s.FormationId = fc.Id
                                     WHERE s.PlanFormationId = p.Id AND s.Statut <> 4) AS TotalHeures,
                                   (SELECT COUNT(DISTINCT i.EmployeeId)
                                      FROM SessionInscriptions i
                                      INNER JOIN SessionsFormation s ON i.SessionId = s.Id
                                     WHERE s.PlanFormationId = p.Id AND s.Statut <> 4
                                       AND i.StatutPresence = 1) AS NombreSalariesFormes
                            FROM PlansFormation p
                            WHERE 1=1 ";

                        if (annee.HasValue)
                        {
                            sql += " AND p.AnneeExercice = @annee ";
                            cmd.Parameters.AddWithValue("@annee", annee.Value);
                        }

                        sql += " ORDER BY p.AnneeExercice DESC, p.Code ASC;";

                        cmd.CommandText = sql;

                        using (var reader = cmd.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                var plan = new PlanFormation
                                {
                                    Id = DbValue.IntFromDb(reader["Id"]),
                                    Code = Convert.ToString(reader["Code"]),
                                    Titre = Convert.ToString(reader["Titre"]),
                                    AnneeExercice = DbValue.IntFromDb(reader["AnneeExercice"]),
                                    BudgetPrevisionnel = DbValue.MoneyFromDb(reader["BudgetPrevisionnel"]),
                                    Statut = (StatutPlanFormation)DbValue.IntFromDb(reader["Statut"]),
                                    ObjectifsStrategiques = reader["ObjectifsStrategiques"] == DBNull.Value ? null : Convert.ToString(reader["ObjectifsStrategiques"]),
                                    DateCreation = DateTime.Parse(Convert.ToString(reader["DateCreation"]), CultureInfo.InvariantCulture),
                                    NombreSessions = DbValue.IntFromDb(reader["NombreSessions"]),
                                    DepensesReelles = DbValue.MoneyFromDb(reader["TotalDepenses"]),
                                    TotalHeures = DbValue.IntFromDb(reader["TotalHeures"]),
                                    NombreSalariesFormes = DbValue.IntFromDb(reader["NombreSalariesFormes"])
                                };

                                if (reader["DateValidation"] != DBNull.Value && !string.IsNullOrWhiteSpace(Convert.ToString(reader["DateValidation"])))
                                {
                                    plan.DateValidation = DateTime.Parse(Convert.ToString(reader["DateValidation"]), CultureInfo.InvariantCulture);
                                }

                                plans.Add(plan);
                            }
                        }
                    }
                }
            });

            return plans;
        }

        /// <summary>
        /// Récupère un plan de formation par son identifiant unique.
        /// </summary>
        public async Task<PlanFormation> ObtenirPlanParIdAsync(int planId)
        {
            var plans = await ObtenirPlansAsync();
            return plans.Find(p => p.Id == planId);
        }

        /// <summary>
        /// Enregistre ou met à jour un plan de formation.
        /// </summary>
        public async Task EnregistrerPlanAsync(PlanFormation plan)
        {
            if (plan == null) throw new ArgumentNullException(nameof(plan));
            if (string.IsNullOrWhiteSpace(plan.Code)) throw new ArgumentException("Le code du plan est obligatoire.");
            if (string.IsNullOrWhiteSpace(plan.Titre)) throw new ArgumentException("Le titre du plan est obligatoire.");

            await Task.Run(() =>
            {
                using (var connection = _sqliteService.CreateConnection())
                {

                    // Vérification unicité code
                    using (var checkCmd = connection.CreateCommand())
                    {
                        checkCmd.CommandText = "SELECT COUNT(*) FROM PlansFormation WHERE Code = @code AND Id <> @id;";
                        checkCmd.Parameters.AddWithValue("@code", plan.Code.Trim().ToUpperInvariant());
                        checkCmd.Parameters.AddWithValue("@id", plan.Id);
                        if (Convert.ToInt32(checkCmd.ExecuteScalar()) > 0)
                        {
                            throw new InvalidOperationException($"Un plan de formation avec le code '{plan.Code}' existe déjà.");
                        }
                    }

                    using (var cmd = connection.CreateCommand())
                    {
                        if (plan.Id == 0)
                        {
                            cmd.CommandText = @"
                                INSERT INTO PlansFormation (Code, Titre, AnneeExercice, BudgetPrevisionnel, Statut, ObjectifsStrategiques, DateValidation, DateCreation)
                                VALUES (@Code, @Titre, @AnneeExercice, @BudgetPrevisionnel, @Statut, @ObjectifsStrategiques, @DateValidation, @DateCreation);
                                SELECT last_insert_rowid();";
                        }
                        else
                        {
                            cmd.CommandText = @"
                                UPDATE PlansFormation
                                SET Code = @Code, Titre = @Titre, AnneeExercice = @AnneeExercice,
                                    BudgetPrevisionnel = @BudgetPrevisionnel, Statut = @Statut,
                                    ObjectifsStrategiques = @ObjectifsStrategiques, DateValidation = @DateValidation
                                WHERE Id = @Id;";
                            cmd.Parameters.AddWithValue("@Id", plan.Id);
                        }

                        cmd.Parameters.AddWithValue("@Code", plan.Code.Trim().ToUpperInvariant());
                        cmd.Parameters.AddWithValue("@Titre", plan.Titre.Trim());
                        cmd.Parameters.AddWithValue("@AnneeExercice", plan.AnneeExercice);
                        cmd.Parameters.AddWithValue("@BudgetPrevisionnel", DbValue.MoneyToDb(plan.BudgetPrevisionnel));
                        cmd.Parameters.AddWithValue("@Statut", (int)plan.Statut);
                        cmd.Parameters.AddWithValue("@ObjectifsStrategiques", (object)plan.ObjectifsStrategiques ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@DateValidation", plan.DateValidation.HasValue ? plan.DateValidation.Value.ToString("yyyy-MM-dd HH:mm:ss") : (object)DBNull.Value);
                        cmd.Parameters.AddWithValue("@DateCreation", plan.DateCreation.ToString("yyyy-MM-dd HH:mm:ss"));

                        if (plan.Id == 0)
                        {
                            plan.Id = Convert.ToInt32(cmd.ExecuteScalar());
                        }
                        else
                        {
                            cmd.ExecuteNonQuery();
                        }
                    }
                }
            });
        }

        /// <summary>
        /// Supprime un plan de formation, à condition qu'aucune de ses sessions ne contienne d'attestation officielle délivrée.
        /// Nettoie explicitement en C# les inscriptions, les besoins RH-03 rattachés et les sessions dans une transaction.
        /// </summary>
        public async Task SupprimerPlanFormationAsync(int planId)
        {
            await Task.Run(() =>
            {
                using (var connection = _sqliteService.CreateConnection())
                {

                    // Vérification du verrou probatoire : présence d'attestation délivrée
                    using (var checkCmd = connection.CreateCommand())
                    {
                        checkCmd.CommandText = @"
                            SELECT COUNT(*)
                            FROM SessionInscriptions i
                            INNER JOIN SessionsFormation s ON i.SessionId = s.Id
                            WHERE s.PlanFormationId = @PlanId AND i.AttestationObtenue = 1;";
                        checkCmd.Parameters.AddWithValue("@PlanId", planId);
                        int attestations = Convert.ToInt32(checkCmd.ExecuteScalar());
                        if (attestations > 0)
                        {
                            throw new InvalidOperationException(
                                "Impossible de supprimer ce plan de formation : des attestations officielles ont déjà été délivrées pour ses sessions. " +
                                "Veuillez plutôt clôturer ou archiver le plan.");
                        }
                    }

                    // Nettoyage en cascade explicite en C# dans une transaction
                    using (var transaction = connection.BeginTransaction())
                    {
                        try
                        {
                            // 1. Détacher les besoins de formation RH-03
                            using (var cmdDetach = connection.CreateCommand())
                            {
                                cmdDetach.Transaction = transaction;
                                cmdDetach.CommandText = @"
                                    UPDATE EntretienBesoinsFormation
                                    SET SessionFormationId = NULL
                                    WHERE SessionFormationId IN (SELECT Id FROM SessionsFormation WHERE PlanFormationId = @PlanId);";
                                cmdDetach.Parameters.AddWithValue("@PlanId", planId);
                                cmdDetach.ExecuteNonQuery();
                            }

                            // 2. Supprimer les inscriptions aux sessions de ce plan
                            using (var cmdDelInsc = connection.CreateCommand())
                            {
                                cmdDelInsc.Transaction = transaction;
                                cmdDelInsc.CommandText = @"
                                    DELETE FROM SessionInscriptions
                                    WHERE SessionId IN (SELECT Id FROM SessionsFormation WHERE PlanFormationId = @PlanId);";
                                cmdDelInsc.Parameters.AddWithValue("@PlanId", planId);
                                cmdDelInsc.ExecuteNonQuery();
                            }

                            // 3. Supprimer les sessions de formation
                            using (var cmdDelSess = connection.CreateCommand())
                            {
                                cmdDelSess.Transaction = transaction;
                                cmdDelSess.CommandText = "DELETE FROM SessionsFormation WHERE PlanFormationId = @PlanId;";
                                cmdDelSess.Parameters.AddWithValue("@PlanId", planId);
                                cmdDelSess.ExecuteNonQuery();
                            }

                            // 4. Supprimer le plan
                            using (var cmdDelPlan = connection.CreateCommand())
                            {
                                cmdDelPlan.Transaction = transaction;
                                cmdDelPlan.CommandText = "DELETE FROM PlansFormation WHERE Id = @PlanId;";
                                cmdDelPlan.Parameters.AddWithValue("@PlanId", planId);
                                cmdDelPlan.ExecuteNonQuery();
                            }

                            transaction.Commit();
                        }
                        catch
                        {
                            transaction.Rollback();
                            throw;
                        }
                    }
                }
            });
        }

        /// <summary>
        /// Modifie le statut d'un plan de formation.
        /// </summary>
        public async Task ChangerStatutPlanAsync(int planId, StatutPlanFormation nouveauStatut)
        {
            await Task.Run(() =>
            {
                using (var connection = _sqliteService.CreateConnection())
                {
                    using (var cmd = connection.CreateCommand())
                    {
                        cmd.CommandText = @"
                            UPDATE PlansFormation
                            SET Statut = @Statut,
                                DateValidation = CASE WHEN @Statut = 1 AND DateValidation IS NULL THEN @DateNow ELSE DateValidation END
                            WHERE Id = @Id;";
                        cmd.Parameters.AddWithValue("@Statut", (int)nouveauStatut);
                        cmd.Parameters.AddWithValue("@DateNow", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                        cmd.Parameters.AddWithValue("@Id", planId);
                        cmd.ExecuteNonQuery();
                    }
                }
            });
        }

        #endregion

        #region Organismes de Formation

        /// <summary>
        /// Récupère la liste des organismes de formation enregistrés.
        /// </summary>
        public async Task<List<OrganismeFormation>> ObtenirOrganismesAsync(bool seulementActifs = false)
        {
            var list = new List<OrganismeFormation>();

            await Task.Run(() =>
            {
                using (var connection = _sqliteService.CreateConnection())
                {
                    using (var cmd = connection.CreateCommand())
                    {
                        string sql = "SELECT Id, RaisonSociale, ContactNom, Telephone, Email, Adresse, NumeroAgrement, Specialites, Actif FROM OrganismesFormation WHERE 1=1 ";
                        if (seulementActifs)
                        {
                            sql += " AND Actif = 1 ";
                        }
                        sql += " ORDER BY RaisonSociale ASC;";
                        cmd.CommandText = sql;

                        using (var reader = cmd.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                list.Add(new OrganismeFormation
                                {
                                    Id = DbValue.IntFromDb(reader["Id"]),
                                    RaisonSociale = Convert.ToString(reader["RaisonSociale"]),
                                    ContactNom = reader["ContactNom"] == DBNull.Value ? null : Convert.ToString(reader["ContactNom"]),
                                    Telephone = reader["Telephone"] == DBNull.Value ? null : Convert.ToString(reader["Telephone"]),
                                    Email = reader["Email"] == DBNull.Value ? null : Convert.ToString(reader["Email"]),
                                    Adresse = reader["Adresse"] == DBNull.Value ? null : Convert.ToString(reader["Adresse"]),
                                    NumeroAgrement = reader["NumeroAgrement"] == DBNull.Value ? null : Convert.ToString(reader["NumeroAgrement"]),
                                    Specialites = reader["Specialites"] == DBNull.Value ? null : Convert.ToString(reader["Specialites"]),
                                    Actif = DbValue.IntFromDb(reader["Actif"]) == 1
                                });
                            }
                        }
                    }
                }
            });

            return list;
        }

        /// <summary>
        /// Enregistre ou met à jour un organisme de formation.
        /// </summary>
        public async Task EnregistrerOrganismeAsync(OrganismeFormation organisme)
        {
            if (organisme == null) throw new ArgumentNullException(nameof(organisme));
            if (string.IsNullOrWhiteSpace(organisme.RaisonSociale))
                throw new ArgumentException("La raison sociale de l'organisme est obligatoire.");

            await Task.Run(() =>
            {
                using (var connection = _sqliteService.CreateConnection())
                {
                    using (var cmd = connection.CreateCommand())
                    {
                        if (organisme.Id == 0)
                        {
                            cmd.CommandText = @"
                                INSERT INTO OrganismesFormation (RaisonSociale, ContactNom, Telephone, Email, Adresse, NumeroAgrement, Specialites, Actif)
                                VALUES (@RaisonSociale, @ContactNom, @Telephone, @Email, @Adresse, @NumeroAgrement, @Specialites, @Actif);
                                SELECT last_insert_rowid();";
                        }
                        else
                        {
                            cmd.CommandText = @"
                                UPDATE OrganismesFormation
                                SET RaisonSociale = @RaisonSociale, ContactNom = @ContactNom, Telephone = @Telephone,
                                    Email = @Email, Adresse = @Adresse, NumeroAgrement = @NumeroAgrement,
                                    Specialites = @Specialites, Actif = @Actif
                                WHERE Id = @Id;";
                            cmd.Parameters.AddWithValue("@Id", organisme.Id);
                        }

                        cmd.Parameters.AddWithValue("@RaisonSociale", organisme.RaisonSociale.Trim());
                        cmd.Parameters.AddWithValue("@ContactNom", (object)organisme.ContactNom ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@Telephone", (object)organisme.Telephone ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@Email", (object)organisme.Email ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@Adresse", (object)organisme.Adresse ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@NumeroAgrement", (object)organisme.NumeroAgrement ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@Specialites", (object)organisme.Specialites ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@Actif", organisme.Actif ? 1 : 0);

                        if (organisme.Id == 0)
                        {
                            organisme.Id = Convert.ToInt32(cmd.ExecuteScalar());
                        }
                        else
                        {
                            cmd.ExecuteNonQuery();
                        }
                    }
                }
            });
        }

        /// <summary>
        /// Supprime un organisme de formation ou refuse la suppression s'il est rattaché à des sessions existantes.
        /// </summary>
        public async Task SupprimerOrganismeAsync(int organismeId)
        {
            await Task.Run(() =>
            {
                using (var connection = _sqliteService.CreateConnection())
                {

                    using (var checkCmd = connection.CreateCommand())
                    {
                        checkCmd.CommandText = "SELECT COUNT(*) FROM SessionsFormation WHERE OrganismeId = @Id;";
                        checkCmd.Parameters.AddWithValue("@Id", organismeId);
                        if (Convert.ToInt32(checkCmd.ExecuteScalar()) > 0)
                        {
                            throw new InvalidOperationException(
                                "Cet organisme de formation est associé à des sessions réelles. " +
                                "Il ne peut pas être supprimé afin de préserver l'intégrité des attestations ; vous pouvez le désactiver.");
                        }
                    }

                    using (var cmd = connection.CreateCommand())
                    {
                        cmd.CommandText = "DELETE FROM OrganismesFormation WHERE Id = @Id;";
                        cmd.Parameters.AddWithValue("@Id", organismeId);
                        cmd.ExecuteNonQuery();
                    }
                }
            });
        }

        /// <summary>
        /// Active ou désactive un organisme de formation.
        /// </summary>
        public async Task BasculerStatutOrganismeAsync(int organismeId, bool actif)
        {
            await Task.Run(() =>
            {
                using (var connection = _sqliteService.CreateConnection())
                {
                    using (var cmd = connection.CreateCommand())
                    {
                        cmd.CommandText = "UPDATE OrganismesFormation SET Actif = @Actif WHERE Id = @Id;";
                        cmd.Parameters.AddWithValue("@Actif", actif ? 1 : 0);
                        cmd.Parameters.AddWithValue("@Id", organismeId);
                        cmd.ExecuteNonQuery();
                    }
                }
            });
        }

        #endregion

        #region Catalogue des Formations

        /// <summary>
        /// Récupère la liste des formations du catalogue, avec jointure sur les compétences ciblées (RH-02).
        /// </summary>
        public async Task<List<FormationCatalogue>> ObtenirCatalogueFormationsAsync(bool seulementActifs = false)
        {
            var list = new List<FormationCatalogue>();

            await Task.Run(() =>
            {
                using (var connection = _sqliteService.CreateConnection())
                {
                    using (var cmd = connection.CreateCommand())
                    {
                        string sql = @"
                            SELECT f.Id, f.Code, f.Intitule, f.Domaine, f.CompetenceViseeId,
                                   f.TypeFormation, f.DureeHeures, f.CoutEstimeParParticipant,
                                   f.ObjectifsPedagogiques, f.Prerequis, f.Actif,
                                   c.Libelle AS CompetenceLibelle, c.Code AS CompetenceCode
                            FROM FormationsCatalogue f
                            LEFT JOIN Competences c ON f.CompetenceViseeId = c.Id
                            WHERE 1=1 ";

                        if (seulementActifs)
                        {
                            sql += " AND f.Actif = 1 ";
                        }

                        sql += " ORDER BY f.Code ASC;";
                        cmd.CommandText = sql;

                        using (var reader = cmd.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                var item = new FormationCatalogue
                                {
                                    Id = DbValue.IntFromDb(reader["Id"]),
                                    Code = Convert.ToString(reader["Code"]),
                                    Intitule = Convert.ToString(reader["Intitule"]),
                                    Domaine = reader["Domaine"] == DBNull.Value ? null : Convert.ToString(reader["Domaine"]),
                                    CompetenceViseeId = reader["CompetenceViseeId"] == DBNull.Value ? (int?)null : DbValue.IntFromDb(reader["CompetenceViseeId"]),
                                    TypeFormation = (TypeActionFormation)DbValue.IntFromDb(reader["TypeFormation"]),
                                    DureeHeures = DbValue.IntFromDb(reader["DureeHeures"]),
                                    CoutEstimeParParticipant = DbValue.MoneyFromDb(reader["CoutEstimeParParticipant"]),
                                    ObjectifsPedagogiques = reader["ObjectifsPedagogiques"] == DBNull.Value ? null : Convert.ToString(reader["ObjectifsPedagogiques"]),
                                    Prerequis = reader["Prerequis"] == DBNull.Value ? null : Convert.ToString(reader["Prerequis"]),
                                    Actif = DbValue.IntFromDb(reader["Actif"]) == 1
                                };

                                if (reader["CompetenceLibelle"] != DBNull.Value)
                                {
                                    item.CompetenceViseeLibelle = $"[{reader["CompetenceCode"]}] {reader["CompetenceLibelle"]}";
                                }

                                list.Add(item);
                            }
                        }
                    }
                }
            });

            return list;
        }

        /// <summary>
        /// Enregistre ou met à jour une formation au catalogue.
        /// </summary>
        public async Task EnregistrerFormationCatalogueAsync(FormationCatalogue formation)
        {
            if (formation == null) throw new ArgumentNullException(nameof(formation));
            if (string.IsNullOrWhiteSpace(formation.Code)) throw new ArgumentException("Le code de la formation est obligatoire.");
            if (string.IsNullOrWhiteSpace(formation.Intitule)) throw new ArgumentException("L'intitulé de la formation est obligatoire.");

            await Task.Run(() =>
            {
                using (var connection = _sqliteService.CreateConnection())
                {

                    using (var checkCmd = connection.CreateCommand())
                    {
                        checkCmd.CommandText = "SELECT COUNT(*) FROM FormationsCatalogue WHERE Code = @code AND Id <> @id;";
                        checkCmd.Parameters.AddWithValue("@code", formation.Code.Trim().ToUpperInvariant());
                        checkCmd.Parameters.AddWithValue("@id", formation.Id);
                        if (Convert.ToInt32(checkCmd.ExecuteScalar()) > 0)
                        {
                            throw new InvalidOperationException($"Une formation avec le code '{formation.Code}' existe déjà.");
                        }
                    }

                    using (var cmd = connection.CreateCommand())
                    {
                        if (formation.Id == 0)
                        {
                            cmd.CommandText = @"
                                INSERT INTO FormationsCatalogue (Code, Intitule, Domaine, CompetenceViseeId, TypeFormation, DureeHeures, CoutEstimeParParticipant, ObjectifsPedagogiques, Prerequis, Actif)
                                VALUES (@Code, @Intitule, @Domaine, @CompetenceViseeId, @TypeFormation, @DureeHeures, @CoutEstimeParParticipant, @ObjectifsPedagogiques, @Prerequis, @Actif);
                                SELECT last_insert_rowid();";
                        }
                        else
                        {
                            cmd.CommandText = @"
                                UPDATE FormationsCatalogue
                                SET Code = @Code, Intitule = @Intitule, Domaine = @Domaine,
                                    CompetenceViseeId = @CompetenceViseeId, TypeFormation = @TypeFormation,
                                    DureeHeures = @DureeHeures, CoutEstimeParParticipant = @CoutEstimeParParticipant,
                                    ObjectifsPedagogiques = @ObjectifsPedagogiques, Prerequis = @Prerequis, Actif = @Actif
                                WHERE Id = @Id;";
                            cmd.Parameters.AddWithValue("@Id", formation.Id);
                        }

                        cmd.Parameters.AddWithValue("@Code", formation.Code.Trim().ToUpperInvariant());
                        cmd.Parameters.AddWithValue("@Intitule", formation.Intitule.Trim());
                        cmd.Parameters.AddWithValue("@Domaine", (object)formation.Domaine ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@CompetenceViseeId", (object)formation.CompetenceViseeId ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@TypeFormation", (int)formation.TypeFormation);
                        cmd.Parameters.AddWithValue("@DureeHeures", formation.DureeHeures);
                        cmd.Parameters.AddWithValue("@CoutEstimeParParticipant", DbValue.MoneyToDb(formation.CoutEstimeParParticipant));
                        cmd.Parameters.AddWithValue("@ObjectifsPedagogiques", (object)formation.ObjectifsPedagogiques ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@Prerequis", (object)formation.Prerequis ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@Actif", formation.Actif ? 1 : 0);

                        if (formation.Id == 0)
                        {
                            formation.Id = Convert.ToInt32(cmd.ExecuteScalar());
                        }
                        else
                        {
                            cmd.ExecuteNonQuery();
                        }
                    }
                }
            });
        }

        /// <summary>
        /// Supprime une formation du catalogue ou refuse la suppression si elle est rattachée à des sessions.
        /// </summary>
        public async Task SupprimerFormationCatalogueAsync(int formationId)
        {
            await Task.Run(() =>
            {
                using (var connection = _sqliteService.CreateConnection())
                {

                    using (var checkCmd = connection.CreateCommand())
                    {
                        checkCmd.CommandText = "SELECT COUNT(*) FROM SessionsFormation WHERE FormationId = @Id;";
                        checkCmd.Parameters.AddWithValue("@Id", formationId);
                        if (Convert.ToInt32(checkCmd.ExecuteScalar()) > 0)
                        {
                            throw new InvalidOperationException(
                                "Ce module de formation est associé à des sessions réelles. " +
                                "Il ne peut pas être supprimé afin de préserver l'historique ; vous pouvez le désactiver.");
                        }
                    }

                    using (var cmd = connection.CreateCommand())
                    {
                        cmd.CommandText = "DELETE FROM FormationsCatalogue WHERE Id = @Id;";
                        cmd.Parameters.AddWithValue("@Id", formationId);
                        cmd.ExecuteNonQuery();
                    }
                }
            });
        }

        /// <summary>
        /// Active ou désactive une formation du catalogue.
        /// </summary>
        public async Task BasculerStatutFormationCatalogueAsync(int formationId, bool actif)
        {
            await Task.Run(() =>
            {
                using (var connection = _sqliteService.CreateConnection())
                {
                    using (var cmd = connection.CreateCommand())
                    {
                        cmd.CommandText = "UPDATE FormationsCatalogue SET Actif = @Actif WHERE Id = @Id;";
                        cmd.Parameters.AddWithValue("@Actif", actif ? 1 : 0);
                        cmd.Parameters.AddWithValue("@Id", formationId);
                        cmd.ExecuteNonQuery();
                    }
                }
            });
        }

        #endregion

        #region Sessions de Formation

        /// <summary>
        /// Récupère la liste des sessions de formation, optionnellement filtrées par plan.
        /// </summary>
        public async Task<List<SessionFormation>> ObtenirSessionsAsync(int? planId = null)
        {
            var list = new List<SessionFormation>();

            await Task.Run(() =>
            {
                using (var connection = _sqliteService.CreateConnection())
                {
                    using (var cmd = connection.CreateCommand())
                    {
                        string sql = @"
                            SELECT s.Id, s.FormationId, s.PlanFormationId, s.OrganismeId, s.CodeSession,
                                   s.IntituleSession, s.DateDebut, s.DateFin, s.Lieu, s.FormateurNom,
                                   s.Statut, s.CoutPedagogique, s.CoutLogistique, s.CoutAutre, s.CoutTotalReel,
                                   s.Commentaire,
                                   fc.Code AS FormationCode, fc.Intitule AS FormationIntitule, fc.DureeHeures,
                                   org.RaisonSociale AS OrganismeNom, org.NumeroAgrement AS OrganismeAgrement,
                                   p.Code AS PlanCode, p.Titre AS PlanTitre,
                                   COUNT(DISTINCT i.Id) AS NombreInscrits,
                                   COUNT(DISTINCT CASE WHEN i.StatutPresence = 1 THEN i.Id END) AS NombrePresents,
                                   COUNT(DISTINCT CASE WHEN i.AttestationObtenue = 1 THEN i.Id END) AS NombreAttestations
                            FROM SessionsFormation s
                            INNER JOIN FormationsCatalogue fc ON s.FormationId = fc.Id
                            INNER JOIN PlansFormation p ON s.PlanFormationId = p.Id
                            LEFT JOIN OrganismesFormation org ON s.OrganismeId = org.Id
                            LEFT JOIN SessionInscriptions i ON i.SessionId = s.Id
                            WHERE 1=1 ";

                        if (planId.HasValue)
                        {
                            sql += " AND s.PlanFormationId = @PlanId ";
                            cmd.Parameters.AddWithValue("@PlanId", planId.Value);
                        }

                        sql += @" GROUP BY s.Id, s.FormationId, s.PlanFormationId, s.OrganismeId, s.CodeSession,
                                          s.IntituleSession, s.DateDebut, s.DateFin, s.Lieu, s.FormateurNom,
                                          s.Statut, s.CoutPedagogique, s.CoutLogistique, s.CoutAutre, s.CoutTotalReel,
                                          s.Commentaire, fc.Code, fc.Intitule, fc.DureeHeures,
                                          org.RaisonSociale, org.NumeroAgrement, p.Code, p.Titre
                                  ORDER BY s.DateDebut DESC, s.CodeSession ASC;";

                        cmd.CommandText = sql;

                        using (var reader = cmd.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                var sess = new SessionFormation
                                {
                                    Id = DbValue.IntFromDb(reader["Id"]),
                                    FormationId = DbValue.IntFromDb(reader["FormationId"]),
                                    PlanFormationId = DbValue.IntFromDb(reader["PlanFormationId"]),
                                    OrganismeId = reader["OrganismeId"] == DBNull.Value ? (int?)null : DbValue.IntFromDb(reader["OrganismeId"]),
                                    CodeSession = Convert.ToString(reader["CodeSession"]),
                                    IntituleSession = reader["IntituleSession"] == DBNull.Value ? null : Convert.ToString(reader["IntituleSession"]),
                                    DateDebut = DateTime.Parse(Convert.ToString(reader["DateDebut"]), CultureInfo.InvariantCulture),
                                    DateFin = DateTime.Parse(Convert.ToString(reader["DateFin"]), CultureInfo.InvariantCulture),
                                    Lieu = reader["Lieu"] == DBNull.Value ? null : Convert.ToString(reader["Lieu"]),
                                    FormateurNom = reader["FormateurNom"] == DBNull.Value ? null : Convert.ToString(reader["FormateurNom"]),
                                    Statut = (StatutSessionFormation)DbValue.IntFromDb(reader["Statut"]),
                                    CoutPedagogique = DbValue.MoneyFromDb(reader["CoutPedagogique"]),
                                    CoutLogistique = DbValue.MoneyFromDb(reader["CoutLogistique"]),
                                    CoutAutre = DbValue.MoneyFromDb(reader["CoutAutre"]),
                                    CoutTotalReel = DbValue.MoneyFromDb(reader["CoutTotalReel"]),
                                    Commentaire = reader["Commentaire"] == DBNull.Value ? null : Convert.ToString(reader["Commentaire"]),
                                    FormationCode = Convert.ToString(reader["FormationCode"]),
                                    FormationIntitule = Convert.ToString(reader["FormationIntitule"]),
                                    DureeHeures = DbValue.IntFromDb(reader["DureeHeures"]),
                                    OrganismeNom = reader["OrganismeNom"] == DBNull.Value ? null : Convert.ToString(reader["OrganismeNom"]),
                                    OrganismeNumeroAgrement = reader["OrganismeAgrement"] == DBNull.Value ? null : Convert.ToString(reader["OrganismeAgrement"]),
                                    PlanCode = Convert.ToString(reader["PlanCode"]),
                                    PlanTitre = Convert.ToString(reader["PlanTitre"]),
                                    NombreInscrits = DbValue.IntFromDb(reader["NombreInscrits"]),
                                    NombrePresents = DbValue.IntFromDb(reader["NombrePresents"]),
                                    NombreAttestations = DbValue.IntFromDb(reader["NombreAttestations"])
                                };

                                list.Add(sess);
                            }
                        }
                    }
                }
            });

            return list;
        }

        /// <summary>
        /// Enregistre ou met à jour une session de formation avec sommation automatique des coûts réels.
        /// </summary>
        public async Task EnregistrerSessionAsync(SessionFormation session)
        {
            if (session == null) throw new ArgumentNullException(nameof(session));
            if (string.IsNullOrWhiteSpace(session.CodeSession)) throw new ArgumentException("Le code de la session est obligatoire.");
            if (session.FormationId <= 0) throw new ArgumentException("Le choix de la formation est obligatoire.");
            if (session.PlanFormationId <= 0) throw new ArgumentException("Le rattachement à un plan de formation est obligatoire.");

            session.RecalculerCoutTotal();

            await Task.Run(() =>
            {
                using (var connection = _sqliteService.CreateConnection())
                {

                    using (var checkCmd = connection.CreateCommand())
                    {
                        checkCmd.CommandText = "SELECT COUNT(*) FROM SessionsFormation WHERE CodeSession = @code AND Id <> @id;";
                        checkCmd.Parameters.AddWithValue("@code", session.CodeSession.Trim().ToUpperInvariant());
                        checkCmd.Parameters.AddWithValue("@id", session.Id);
                        if (Convert.ToInt32(checkCmd.ExecuteScalar()) > 0)
                        {
                            throw new InvalidOperationException($"Une session avec le code '{session.CodeSession}' existe déjà.");
                        }
                    }

                    using (var cmd = connection.CreateCommand())
                    {
                        if (session.Id == 0)
                        {
                            cmd.CommandText = @"
                                INSERT INTO SessionsFormation (
                                    FormationId, PlanFormationId, OrganismeId, CodeSession, IntituleSession,
                                    DateDebut, DateFin, Lieu, FormateurNom, Statut,
                                    CoutPedagogique, CoutLogistique, CoutAutre, CoutTotalReel, Commentaire
                                ) VALUES (
                                    @FormationId, @PlanFormationId, @OrganismeId, @CodeSession, @IntituleSession,
                                    @DateDebut, @DateFin, @Lieu, @FormateurNom, @Statut,
                                    @CoutPedagogique, @CoutLogistique, @CoutAutre, @CoutTotalReel, @Commentaire
                                );
                                SELECT last_insert_rowid();";
                        }
                        else
                        {
                            cmd.CommandText = @"
                                UPDATE SessionsFormation
                                SET FormationId = @FormationId, PlanFormationId = @PlanFormationId,
                                    OrganismeId = @OrganismeId, CodeSession = @CodeSession,
                                    IntituleSession = @IntituleSession, DateDebut = @DateDebut,
                                    DateFin = @DateFin, Lieu = @Lieu, FormateurNom = @FormateurNom,
                                    Statut = @Statut, CoutPedagogique = @CoutPedagogique,
                                    CoutLogistique = @CoutLogistique, CoutAutre = @CoutAutre,
                                    CoutTotalReel = @CoutTotalReel, Commentaire = @Commentaire
                                WHERE Id = @Id;";
                            cmd.Parameters.AddWithValue("@Id", session.Id);
                        }

                        cmd.Parameters.AddWithValue("@FormationId", session.FormationId);
                        cmd.Parameters.AddWithValue("@PlanFormationId", session.PlanFormationId);
                        cmd.Parameters.AddWithValue("@OrganismeId", (object)session.OrganismeId ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@CodeSession", session.CodeSession.Trim().ToUpperInvariant());
                        cmd.Parameters.AddWithValue("@IntituleSession", (object)session.IntituleSession ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@DateDebut", session.DateDebut.ToString("yyyy-MM-dd"));
                        cmd.Parameters.AddWithValue("@DateFin", session.DateFin.ToString("yyyy-MM-dd"));
                        cmd.Parameters.AddWithValue("@Lieu", (object)session.Lieu ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@FormateurNom", (object)session.FormateurNom ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@Statut", (int)session.Statut);
                        cmd.Parameters.AddWithValue("@CoutPedagogique", DbValue.MoneyToDb(session.CoutPedagogique));
                        cmd.Parameters.AddWithValue("@CoutLogistique", DbValue.MoneyToDb(session.CoutLogistique));
                        cmd.Parameters.AddWithValue("@CoutAutre", DbValue.MoneyToDb(session.CoutAutre));
                        cmd.Parameters.AddWithValue("@CoutTotalReel", DbValue.MoneyToDb(session.CoutTotalReel));
                        cmd.Parameters.AddWithValue("@Commentaire", (object)session.Commentaire ?? DBNull.Value);

                        if (session.Id == 0)
                        {
                            session.Id = Convert.ToInt32(cmd.ExecuteScalar());
                        }
                        else
                        {
                            cmd.ExecuteNonQuery();
                        }
                    }
                }
            });
        }

        /// <summary>
        /// Supprime une session de formation après avoir vérifié qu'aucune attestation n'a été délivrée.
        /// Nettoie explicitement en C# les inscriptions et les liaisons avec RH-03.
        /// </summary>
        public async Task SupprimerSessionFormationAsync(int sessionId)
        {
            await Task.Run(() =>
            {
                using (var connection = _sqliteService.CreateConnection())
                {

                    using (var checkCmd = connection.CreateCommand())
                    {
                        checkCmd.CommandText = "SELECT COUNT(*) FROM SessionInscriptions WHERE SessionId = @Id AND AttestationObtenue = 1;";
                        checkCmd.Parameters.AddWithValue("@Id", sessionId);
                        if (Convert.ToInt32(checkCmd.ExecuteScalar()) > 0)
                        {
                            throw new InvalidOperationException(
                                "Impossible de supprimer cette session de formation : des attestations officielles ont déjà été délivrées. " +
                                "Veuillez plutôt passer la session au statut Annulée.");
                        }
                    }

                    using (var transaction = connection.BeginTransaction())
                    {
                        try
                        {
                            // 1. Détacher les besoins RH-03
                            using (var cmdDetach = connection.CreateCommand())
                            {
                                cmdDetach.Transaction = transaction;
                                cmdDetach.CommandText = "UPDATE EntretienBesoinsFormation SET SessionFormationId = NULL WHERE SessionFormationId = @Id;";
                                cmdDetach.Parameters.AddWithValue("@Id", sessionId);
                                cmdDetach.ExecuteNonQuery();
                            }

                            // 2. Supprimer les inscriptions
                            using (var cmdDelInsc = connection.CreateCommand())
                            {
                                cmdDelInsc.Transaction = transaction;
                                cmdDelInsc.CommandText = "DELETE FROM SessionInscriptions WHERE SessionId = @Id;";
                                cmdDelInsc.Parameters.AddWithValue("@Id", sessionId);
                                cmdDelInsc.ExecuteNonQuery();
                            }

                            // 3. Supprimer la session
                            using (var cmdDelSess = connection.CreateCommand())
                            {
                                cmdDelSess.Transaction = transaction;
                                cmdDelSess.CommandText = "DELETE FROM SessionsFormation WHERE Id = @Id;";
                                cmdDelSess.Parameters.AddWithValue("@Id", sessionId);
                                cmdDelSess.ExecuteNonQuery();
                            }

                            transaction.Commit();
                        }
                        catch
                        {
                            transaction.Rollback();
                            throw;
                        }
                    }
                }
            });
        }

        /// <summary>
        /// Change le statut d'une session de formation.
        /// </summary>
        public async Task ChangerStatutSessionAsync(int sessionId, StatutSessionFormation nouveauStatut)
        {
            await Task.Run(() =>
            {
                using (var connection = _sqliteService.CreateConnection())
                {
                    using (var cmd = connection.CreateCommand())
                    {
                        cmd.CommandText = "UPDATE SessionsFormation SET Statut = @Statut WHERE Id = @Id;";
                        cmd.Parameters.AddWithValue("@Statut", (int)nouveauStatut);
                        cmd.Parameters.AddWithValue("@Id", sessionId);
                        cmd.ExecuteNonQuery();
                    }
                }
            });
        }

        #endregion

        #region Inscriptions et Participants

        /// <summary>
        /// Récupère la liste des participants inscrits à une session avec détails du salarié.
        /// </summary>
        public async Task<List<SessionInscription>> ObtenirInscriptionsSessionAsync(int sessionId)
        {
            var list = new List<SessionInscription>();

            await Task.Run(() =>
            {
                using (var connection = _sqliteService.CreateConnection())
                {
                    using (var cmd = connection.CreateCommand())
                    {
                        cmd.CommandText = @"
                            SELECT i.Id, i.SessionId, i.EmployeeId, i.DateInscription, i.StatutPresence,
                                   i.NoteEvaluation, i.AttestationObtenue, i.FraisImputes, i.Commentaire,
                                   e.EmployeeId AS Matricule,
                                   COALESCE(e.LastName, '') || ' ' || COALESCE(e.FirstName, '') AS NomComplet,
                                   e.Service AS Department,
                                   p.Intitule AS PosteTitre,
                                   s.CodeSession, s.IntituleSession, s.DateDebut, s.DateFin,
                                   fc.Intitule AS FormationIntitule, fc.DureeHeures
                            FROM SessionInscriptions i
                            INNER JOIN Employees e ON i.EmployeeId = e.EmployeeId
                            INNER JOIN SessionsFormation s ON i.SessionId = s.Id
                            INNER JOIN FormationsCatalogue fc ON s.FormationId = fc.Id
                            LEFT JOIN Postes p ON e.PosteId = p.Id
                            WHERE i.SessionId = @SessionId
                            ORDER BY e.LastName ASC, e.FirstName ASC;";
                        cmd.Parameters.AddWithValue("@SessionId", sessionId);

                        using (var reader = cmd.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                list.Add(new SessionInscription
                                {
                                    Id = DbValue.IntFromDb(reader["Id"]),
                                    SessionId = DbValue.IntFromDb(reader["SessionId"]),
                                    EmployeeId = Convert.ToString(reader["EmployeeId"]),
                                    DateInscription = DateTime.Parse(Convert.ToString(reader["DateInscription"]), CultureInfo.InvariantCulture),
                                    StatutPresence = (StatutPresenceSession)DbValue.IntFromDb(reader["StatutPresence"]),
                                    NoteEvaluation = reader["NoteEvaluation"] == DBNull.Value ? null : Convert.ToString(reader["NoteEvaluation"]),
                                    AttestationObtenue = DbValue.IntFromDb(reader["AttestationObtenue"]) == 1,
                                    FraisImputes = DbValue.MoneyFromDb(reader["FraisImputes"]),
                                    Commentaire = reader["Commentaire"] == DBNull.Value ? null : Convert.ToString(reader["Commentaire"]),
                                    EmployeeMatricule = Convert.ToString(reader["Matricule"]),
                                    EmployeeNomComplet = Convert.ToString(reader["NomComplet"]).Trim(),
                                    Department = reader["Department"] == DBNull.Value ? null : Convert.ToString(reader["Department"]),
                                    PosteTitre = reader["PosteTitre"] == DBNull.Value ? null : Convert.ToString(reader["PosteTitre"]),
                                    SessionCode = Convert.ToString(reader["CodeSession"]),
                                    SessionIntitule = reader["IntituleSession"] == DBNull.Value ? Convert.ToString(reader["FormationIntitule"]) : Convert.ToString(reader["IntituleSession"]),
                                    FormationIntitule = Convert.ToString(reader["FormationIntitule"]),
                                    DureeHeures = DbValue.IntFromDb(reader["DureeHeures"]),
                                    DateDebut = DateTime.Parse(Convert.ToString(reader["DateDebut"]), CultureInfo.InvariantCulture),
                                    DateFin = DateTime.Parse(Convert.ToString(reader["DateFin"]), CultureInfo.InvariantCulture)
                                });
                            }
                        }
                    }
                }
            });

            return list;
        }

        /// <summary>
        /// Récupère toutes les inscriptions de l'ensemble des sessions d'un plan annuel.
        /// </summary>
        public async Task<List<SessionInscription>> ObtenirToutesInscriptionsPlanAsync(int planId)
        {
            var list = new List<SessionInscription>();

            await Task.Run(() =>
            {
                using (var connection = _sqliteService.CreateConnection())
                {
                    using (var cmd = connection.CreateCommand())
                    {
                        cmd.CommandText = @"
                            SELECT i.Id, i.SessionId, i.EmployeeId, i.DateInscription, i.StatutPresence,
                                   i.NoteEvaluation, i.AttestationObtenue, i.FraisImputes, i.Commentaire,
                                   e.EmployeeId AS Matricule,
                                   COALESCE(e.LastName, '') || ' ' || COALESCE(e.FirstName, '') AS NomComplet,
                                   e.Service AS Department,
                                   p.Intitule AS PosteTitre,
                                   s.CodeSession, s.IntituleSession, s.DateDebut, s.DateFin,
                                   fc.Intitule AS FormationIntitule, fc.DureeHeures
                            FROM SessionInscriptions i
                            INNER JOIN Employees e ON i.EmployeeId = e.EmployeeId
                            INNER JOIN SessionsFormation s ON i.SessionId = s.Id
                            INNER JOIN FormationsCatalogue fc ON s.FormationId = fc.Id
                            LEFT JOIN Postes p ON e.PosteId = p.Id
                            WHERE s.PlanFormationId = @PlanId
                            ORDER BY s.DateDebut ASC, e.LastName ASC;";
                        cmd.Parameters.AddWithValue("@PlanId", planId);

                        using (var reader = cmd.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                list.Add(new SessionInscription
                                {
                                    Id = DbValue.IntFromDb(reader["Id"]),
                                    SessionId = DbValue.IntFromDb(reader["SessionId"]),
                                    EmployeeId = Convert.ToString(reader["EmployeeId"]),
                                    DateInscription = DateTime.Parse(Convert.ToString(reader["DateInscription"]), CultureInfo.InvariantCulture),
                                    StatutPresence = (StatutPresenceSession)DbValue.IntFromDb(reader["StatutPresence"]),
                                    NoteEvaluation = reader["NoteEvaluation"] == DBNull.Value ? null : Convert.ToString(reader["NoteEvaluation"]),
                                    AttestationObtenue = DbValue.IntFromDb(reader["AttestationObtenue"]) == 1,
                                    FraisImputes = DbValue.MoneyFromDb(reader["FraisImputes"]),
                                    Commentaire = reader["Commentaire"] == DBNull.Value ? null : Convert.ToString(reader["Commentaire"]),
                                    EmployeeMatricule = Convert.ToString(reader["Matricule"]),
                                    EmployeeNomComplet = Convert.ToString(reader["NomComplet"]).Trim(),
                                    Department = reader["Department"] == DBNull.Value ? null : Convert.ToString(reader["Department"]),
                                    PosteTitre = reader["PosteTitre"] == DBNull.Value ? null : Convert.ToString(reader["PosteTitre"]),
                                    SessionCode = Convert.ToString(reader["CodeSession"]),
                                    SessionIntitule = reader["IntituleSession"] == DBNull.Value ? Convert.ToString(reader["FormationIntitule"]) : Convert.ToString(reader["IntituleSession"]),
                                    FormationIntitule = Convert.ToString(reader["FormationIntitule"]),
                                    DureeHeures = DbValue.IntFromDb(reader["DureeHeures"]),
                                    DateDebut = DateTime.Parse(Convert.ToString(reader["DateDebut"]), CultureInfo.InvariantCulture),
                                    DateFin = DateTime.Parse(Convert.ToString(reader["DateFin"]), CultureInfo.InvariantCulture)
                                });
                            }
                        }
                    }
                }
            });

            return list;
        }

        /// <summary>
        /// Inscrit un salarié à une session de formation en empêchant les doublons.
        /// </summary>
        public async Task<int> InscrireParticipantAsync(int sessionId, string employeeId, decimal fraisImputes = 0m, string commentaire = null)
        {
            if (sessionId <= 0) throw new ArgumentException("Session invalide.", nameof(sessionId));
            if (string.IsNullOrWhiteSpace(employeeId)) throw new ArgumentException("Salarié obligatoire.", nameof(employeeId));

            return await Task.Run(() =>
            {
                using (var connection = _sqliteService.CreateConnection())
                {

                    using (var checkCmd = connection.CreateCommand())
                    {
                        checkCmd.CommandText = "SELECT COUNT(*) FROM SessionInscriptions WHERE SessionId = @SessionId AND EmployeeId = @EmployeeId;";
                        checkCmd.Parameters.AddWithValue("@SessionId", sessionId);
                        checkCmd.Parameters.AddWithValue("@EmployeeId", employeeId);
                        if (Convert.ToInt32(checkCmd.ExecuteScalar()) > 0)
                        {
                            throw new InvalidOperationException("Ce salarié est déjà inscrit à cette session de formation.");
                        }
                    }

                    using (var cmd = connection.CreateCommand())
                    {
                        cmd.CommandText = @"
                            INSERT INTO SessionInscriptions (SessionId, EmployeeId, DateInscription, StatutPresence, NoteEvaluation, AttestationObtenue, FraisImputes, Commentaire)
                            VALUES (@SessionId, @EmployeeId, @DateInscription, 0, NULL, 0, @FraisImputes, @Commentaire);
                            SELECT last_insert_rowid();";
                        cmd.Parameters.AddWithValue("@SessionId", sessionId);
                        cmd.Parameters.AddWithValue("@EmployeeId", employeeId);
                        cmd.Parameters.AddWithValue("@DateInscription", DateTime.Today.ToString("yyyy-MM-dd"));
                        cmd.Parameters.AddWithValue("@FraisImputes", DbValue.MoneyToDb(fraisImputes));
                        cmd.Parameters.AddWithValue("@Commentaire", (object)commentaire ?? DBNull.Value);

                        return Convert.ToInt32(cmd.ExecuteScalar());
                    }
                }
            });
        }

        /// <summary>
        /// Met à jour les détails d'une inscription (présence, note, attestation, frais).
        /// </summary>
        public async Task MettreAJourInscriptionAsync(SessionInscription inscription)
        {
            if (inscription == null) throw new ArgumentNullException(nameof(inscription));

            await Task.Run(() =>
            {
                using (var connection = _sqliteService.CreateConnection())
                {
                    using (var cmd = connection.CreateCommand())
                    {
                        cmd.CommandText = @"
                            UPDATE SessionInscriptions
                            SET StatutPresence = @StatutPresence,
                                NoteEvaluation = @NoteEvaluation,
                                AttestationObtenue = @AttestationObtenue,
                                FraisImputes = @FraisImputes,
                                Commentaire = @Commentaire
                            WHERE Id = @Id;";
                        cmd.Parameters.AddWithValue("@StatutPresence", (int)inscription.StatutPresence);
                        cmd.Parameters.AddWithValue("@NoteEvaluation", (object)inscription.NoteEvaluation ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@AttestationObtenue", inscription.AttestationObtenue ? 1 : 0);
                        cmd.Parameters.AddWithValue("@FraisImputes", DbValue.MoneyToDb(inscription.FraisImputes));
                        cmd.Parameters.AddWithValue("@Commentaire", (object)inscription.Commentaire ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@Id", inscription.Id);
                        cmd.ExecuteNonQuery();
                    }
                }
            });
        }

        /// <summary>
        /// Supprime une inscription après vérification qu'aucune attestation probatoire n'a été délivrée.
        /// </summary>
        public async Task SupprimerInscriptionAsync(int inscriptionId)
        {
            await Task.Run(() =>
            {
                using (var connection = _sqliteService.CreateConnection())
                {

                    using (var checkCmd = connection.CreateCommand())
                    {
                        checkCmd.CommandText = "SELECT AttestationObtenue FROM SessionInscriptions WHERE Id = @Id;";
                        checkCmd.Parameters.AddWithValue("@Id", inscriptionId);
                        object res = checkCmd.ExecuteScalar();
                        if (res != null && DbValue.IntFromDb(res) == 1)
                        {
                            throw new InvalidOperationException(
                                "Impossible de retirer cette inscription : une attestation officielle a déjà été délivrée à ce salarié.");
                        }
                    }

                    using (var cmd = connection.CreateCommand())
                    {
                        cmd.CommandText = "DELETE FROM SessionInscriptions WHERE Id = @Id;";
                        cmd.Parameters.AddWithValue("@Id", inscriptionId);
                        cmd.ExecuteNonQuery();
                    }
                }
            });
        }

        /// <summary>
        /// Enregistre le pointage de présence et la délivrance d'attestation pour un participant.
        /// </summary>
        public async Task PointerPresenceAsync(int inscriptionId, StatutPresenceSession presence, bool attestation = false, string note = null)
        {
            await Task.Run(() =>
            {
                using (var connection = _sqliteService.CreateConnection())
                {
                    using (var cmd = connection.CreateCommand())
                    {
                        cmd.CommandText = @"
                            UPDATE SessionInscriptions
                            SET StatutPresence = @StatutPresence,
                                AttestationObtenue = @AttestationObtenue,
                                NoteEvaluation = COALESCE(@NoteEvaluation, NoteEvaluation)
                            WHERE Id = @Id;";
                        cmd.Parameters.AddWithValue("@StatutPresence", (int)presence);
                        cmd.Parameters.AddWithValue("@AttestationObtenue", attestation ? 1 : 0);
                        cmd.Parameters.AddWithValue("@NoteEvaluation", (object)note ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@Id", inscriptionId);
                        cmd.ExecuteNonQuery();
                    }
                }
            });
        }

        #endregion

        #region Passerelle RH-03 (Besoins d'Évaluation)

        /// <summary>
        /// Récupère la liste des besoins de formation recensés lors des entretiens d'évaluation annuels (RH-03).
        /// </summary>
        public async Task<List<EntretienBesoinFormation>> ObtenirBesoinsFormationRh03Async(int? anneeExercice = null, StatutBesoinFormation? statut = null)
        {
            var list = new List<EntretienBesoinFormation>();

            await Task.Run(() =>
            {
                using (var connection = _sqliteService.CreateConnection())
                {
                    using (var cmd = connection.CreateCommand())
                    {
                        string sql = @"
                            SELECT b.Id, b.EntretienId, b.CompetenceId, b.ThemeFormation, b.CompetenceVisee,
                                   b.Priorite, b.AvisEvaluateur, b.Statut, b.SessionFormationId,
                                   camp.AnneeExercice,
                                   e.EmployeeId AS Matricule,
                                   COALESCE(e.LastName, '') || ' ' || COALESCE(e.FirstName, '') AS EmployeNom,
                                   e.Service AS Department,
                                   p.Intitule AS PosteTitre,
                                   c.Libelle AS CompetenceLibelle,
                                   s.CodeSession, s.IntituleSession
                            FROM EntretienBesoinsFormation b
                            INNER JOIN EntretiensEvaluation ent ON b.EntretienId = ent.Id
                            INNER JOIN CampagnesEvaluation camp ON ent.CampagneId = camp.Id
                            INNER JOIN Employees e ON ent.EmployeeId = e.EmployeeId
                            LEFT JOIN Postes p ON e.PosteId = p.Id
                            LEFT JOIN Competences c ON b.CompetenceId = c.Id
                            LEFT JOIN SessionsFormation s ON b.SessionFormationId = s.Id
                            WHERE 1=1 ";

                        if (anneeExercice.HasValue)
                        {
                            sql += " AND camp.AnneeExercice = @annee ";
                            cmd.Parameters.AddWithValue("@annee", anneeExercice.Value);
                        }

                        if (statut.HasValue)
                        {
                            sql += " AND b.Statut = @statut ";
                            cmd.Parameters.AddWithValue("@statut", (int)statut.Value);
                        }

                        sql += " ORDER BY b.Priorite DESC, camp.AnneeExercice DESC, e.LastName ASC;";
                        cmd.CommandText = sql;

                        using (var reader = cmd.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                var besoin = new EntretienBesoinFormation
                                {
                                    Id = DbValue.IntFromDb(reader["Id"]),
                                    EntretienId = DbValue.IntFromDb(reader["EntretienId"]),
                                    CompetenceId = reader["CompetenceId"] == DBNull.Value ? (int?)null : DbValue.IntFromDb(reader["CompetenceId"]),
                                    ThemeFormation = Convert.ToString(reader["ThemeFormation"]),
                                    CompetenceVisee = reader["CompetenceVisee"] == DBNull.Value ? null : Convert.ToString(reader["CompetenceVisee"]),
                                    Priorite = (PrioriteFormation)DbValue.IntFromDb(reader["Priorite"]),
                                    AvisEvaluateur = reader["AvisEvaluateur"] == DBNull.Value ? null : Convert.ToString(reader["AvisEvaluateur"]),
                                    Statut = (StatutBesoinFormation)DbValue.IntFromDb(reader["Statut"])
                                };

                                list.Add(besoin);
                            }
                        }
                    }
                }
            });

            return list;
        }

        /// <summary>
        /// Rapproche un besoin d'évaluation RH-03 d'une session de formation réelle,
        /// en mettant à jour son statut vers Retenu (valeur 1).
        /// </summary>
        public async Task AssignerBesoinASessionAsync(int besoinId, int sessionId)
        {
            await Task.Run(() =>
            {
                using (var connection = _sqliteService.CreateConnection())
                {
                    using (var cmd = connection.CreateCommand())
                    {
                        cmd.CommandText = @"
                            UPDATE EntretienBesoinsFormation
                            SET SessionFormationId = @SessionId,
                                Statut = @StatutRetenu
                            WHERE Id = @BesoinId;";
                        cmd.Parameters.AddWithValue("@SessionId", sessionId);
                        cmd.Parameters.AddWithValue("@StatutRetenu", (int)StatutBesoinFormation.Retenu);
                        cmd.Parameters.AddWithValue("@BesoinId", besoinId);
                        cmd.ExecuteNonQuery();
                    }
                }
            });
        }

        /// <summary>
        /// Détache un besoin d'évaluation de sa session de formation et repasse son statut à Demandé.
        /// </summary>
        public async Task DetacherBesoinSessionAsync(int besoinId)
        {
            await Task.Run(() =>
            {
                using (var connection = _sqliteService.CreateConnection())
                {
                    using (var cmd = connection.CreateCommand())
                    {
                        cmd.CommandText = @"
                            UPDATE EntretienBesoinsFormation
                            SET SessionFormationId = NULL,
                                Statut = @StatutDemande
                            WHERE Id = @BesoinId;";
                        cmd.Parameters.AddWithValue("@StatutDemande", (int)StatutBesoinFormation.Demande);
                        cmd.Parameters.AddWithValue("@BesoinId", besoinId);
                        cmd.ExecuteNonQuery();
                    }
                }
            });
        }

        #endregion

        #region Tableau de Bord TPA & Finances

        /// <summary>
        /// Calcule les agrégats financiers et fiscaux TPA pour un exercice donné.
        /// Filtre strictement sur IsAnnual = 0 et IsRegularization = 0 pour éviter tout double compte.
        /// </summary>
        public async Task<TableauBordTpaFormation> ObtenirTableauBordTpaAsync(int anneeExercice, int? planId = null)
        {
            var tpa = new TableauBordTpaFormation
            {
                AnneeExercice = anneeExercice,
                TauxTpaLegal = 0.03m // Taux légal standard de 3% au Burkina Faso (Art. 120 CGI)
            };

            await Task.Run(() =>
            {
                using (var connection = _sqliteService.CreateConnection())
                {

                    // 1. Masse salariale soumise TPA et TPA brute due (source TaxCalculations)
                    string dateDebut = $"{anneeExercice}-01-01";
                    string dateFin = $"{anneeExercice}-12-31 23:59:59";

                    using (var cmdTax = connection.CreateCommand())
                    {
                        cmdTax.CommandText = @"
                            SELECT COALESCE(SUM(AssietteTPA), 0) AS TotalAssietteTPA,
                                   COALESCE(SUM(TaxePatronale), 0) AS TotalTaxePatronale
                            FROM TaxCalculations
                            WHERE CalculationDate >= @DateDebut AND CalculationDate <= @DateFin
                              AND IsAnnual = 0 AND IsRegularization = 0;";
                        cmdTax.Parameters.AddWithValue("@DateDebut", dateDebut);
                        cmdTax.Parameters.AddWithValue("@DateFin", dateFin);

                        using (var reader = cmdTax.ExecuteReader())
                        {
                            if (reader.Read())
                            {
                                tpa.MasseSalarialeSoumiseTpa = DbValue.MoneyFromDb(reader["TotalAssietteTPA"]);
                                tpa.TpaTheoriqueDue = DbValue.MoneyFromDb(reader["TotalTaxePatronale"]);
                            }
                        }
                    }

                    // 2. Budget et dépenses réelles de formation (source PlansFormation & SessionsFormation)
                    //    Un agrégat par sous-requête : avec une jointure unique plans → sessions →
                    //    inscriptions présentes, le coût et la durée d'une session étaient comptés
                    //    une fois par présent, et le budget du plan une fois par ligne jointe.
                    //    Statut de session 4 = Annulée (exclue), 3 = Réalisée ; StatutPresence 1 = Présent.
                    using (var cmdPlan = connection.CreateCommand())
                    {
                        string filtrePlans = planId.HasValue
                            ? "p.AnneeExercice = @Annee AND p.Id = @PlanId"
                            : "p.AnneeExercice = @Annee";

                        string sql = @"
                            SELECT
                                (SELECT COALESCE(SUM(p.BudgetPrevisionnel), 0)
                                   FROM PlansFormation p
                                  WHERE " + filtrePlans + @") AS TotalBudget,
                                (SELECT COALESCE(SUM(s.CoutTotalReel), 0)
                                   FROM SessionsFormation s
                                   INNER JOIN PlansFormation p ON s.PlanFormationId = p.Id
                                  WHERE " + filtrePlans + @" AND s.Statut <> 4) AS TotalDepenses,
                                (SELECT COUNT(DISTINCT i.EmployeeId)
                                   FROM SessionInscriptions i
                                   INNER JOIN SessionsFormation s ON i.SessionId = s.Id
                                   INNER JOIN PlansFormation p ON s.PlanFormationId = p.Id
                                  WHERE " + filtrePlans + @" AND s.Statut <> 4 AND i.StatutPresence = 1) AS NombreSalariesFormes,
                                (SELECT COALESCE(SUM(fc.DureeHeures), 0)
                                   FROM SessionsFormation s
                                   INNER JOIN FormationsCatalogue fc ON s.FormationId = fc.Id
                                   INNER JOIN PlansFormation p ON s.PlanFormationId = p.Id
                                  WHERE " + filtrePlans + @" AND s.Statut <> 4) AS TotalHeures,
                                (SELECT COUNT(*)
                                   FROM SessionsFormation s
                                   INNER JOIN PlansFormation p ON s.PlanFormationId = p.Id
                                  WHERE " + filtrePlans + @" AND s.Statut = 3) AS NombreSessionsRealisees;";

                        if (planId.HasValue)
                        {
                            cmdPlan.Parameters.AddWithValue("@PlanId", planId.Value);
                        }

                        cmdPlan.CommandText = sql;
                        cmdPlan.Parameters.AddWithValue("@Annee", anneeExercice);

                        using (var reader = cmdPlan.ExecuteReader())
                        {
                            if (reader.Read())
                            {
                                tpa.BudgetPlanFormation = DbValue.MoneyFromDb(reader["TotalBudget"]);
                                tpa.TotalDepensesFormation = DbValue.MoneyFromDb(reader["TotalDepenses"]);
                                tpa.NombreSalariesFormes = DbValue.IntFromDb(reader["NombreSalariesFormes"]);
                                tpa.NombreHeuresTotal = DbValue.IntFromDb(reader["TotalHeures"]);
                                tpa.NombreSessionsRealisees = DbValue.IntFromDb(reader["NombreSessionsRealisees"]);
                            }
                        }
                    }
                }
            });

            return tpa;
        }

        #endregion
    }
}
