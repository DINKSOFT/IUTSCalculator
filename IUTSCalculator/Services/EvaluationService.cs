using System;
using System.Collections.Generic;
using Microsoft.Data.Sqlite;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using IUTSCalculator.Models;

namespace IUTSCalculator.Services
{
    /// <summary>
    /// Résultat détaillé d'une initialisation de campagne d'évaluation.
    /// </summary>
    public class RapportInitialisationCampagne
    {
        public int TotalSalariesActifs { get; set; }
        public int EntretiensCrees { get; set; }
        public int EntretiensExistantDeja { get; set; }
        public int EvaluateursAssignes { get; set; }
        public int EvaluateursAAssigner { get; set; }
        public List<string> SalariesSansEvaluateur { get; set; } = new List<string>();
    }

    /// <summary>
    /// Service métier pour les campagnes d'évaluation, la conduite des entretiens,
    /// la notation, les objectifs et l'historique de performance (module RH-03).
    /// </summary>
    public class EvaluationService
    {
        private readonly SQLiteService _sqliteService;
        private readonly OrganigrammeService _organigrammeService;

        public EvaluationService(
            SQLiteService sqliteService,
            OrganigrammeService organigrammeService = null)
        {
            _sqliteService = sqliteService ?? throw new ArgumentNullException(nameof(sqliteService));
            _organigrammeService = organigrammeService;
        }

        #region Campagnes d'évaluation

        /// <summary>
        /// Récupère la liste de toutes les campagnes d'évaluation avec leurs statistiques d'avancement.
        /// </summary>
        public async Task<List<CampagneEvaluation>> ObtenirCampagnesAsync()
        {
            var liste = new List<CampagneEvaluation>();

            using (var conn = _sqliteService.CreerConnexion())
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = @"
                    SELECT 
                        c.Id, c.Code, c.Titre, c.AnneeExercice, c.DateDebut, c.DateFin, c.Statut, c.Description, c.DateCreation,
                        COUNT(e.Id) AS TotalEntretiens,
                        SUM(CASE WHEN e.Statut = 0 THEN 1 ELSE 0 END) AS Planifies,
                        SUM(CASE WHEN e.Statut = 1 THEN 1 ELSE 0 END) AS EnCours,
                        SUM(CASE WHEN e.Statut = 2 THEN 1 ELSE 0 END) AS Realises,
                        SUM(CASE WHEN e.Statut >= 3 THEN 1 ELSE 0 END) AS Valides,
                        SUM(CASE WHEN e.EvaluateurMatricule IS NULL OR TRIM(e.EvaluateurMatricule) = '' THEN 1 ELSE 0 END) AS SansEvaluateur,
                        AVG(CASE WHEN CAST(e.NoteGlobale AS REAL) > 0 THEN CAST(e.NoteGlobale AS REAL) ELSE NULL END) AS NoteMoyenne
                    FROM CampagnesEvaluation c
                    LEFT JOIN EntretiensEvaluation e ON e.CampagneId = c.Id
                    GROUP BY c.Id
                    ORDER BY c.AnneeExercice DESC, c.DateDebut DESC, c.Id DESC;";

                using (var reader = (SqliteDataReader)await cmd.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                    {
                        var c = new CampagneEvaluation
                        {
                            Id = DbValue.IntFromDb(reader["Id"]),
                            Code = DbValue.TextFromDb(reader["Code"]),
                            Titre = DbValue.TextFromDb(reader["Titre"]),
                            AnneeExercice = DbValue.IntFromDb(reader["AnneeExercice"]),
                            DateDebut = DbValue.DateFromDb(reader["DateDebut"], DateTime.Today),
                            DateFin = DbValue.DateFromDb(reader["DateFin"], DateTime.Today),
                            Statut = (StatutCampagne)DbValue.IntFromDb(reader["Statut"]),
                            Description = DbValue.TextFromDb(reader["Description"]),
                            DateCreation = DbValue.DateFromDb(reader["DateCreation"], DateTime.Now),
                            TotalEntretiens = DbValue.IntFromDb(reader["TotalEntretiens"]),
                            EntretiensPlanifies = DbValue.IntFromDb(reader["Planifies"]),
                            EntretiensEnCours = DbValue.IntFromDb(reader["EnCours"]),
                            EntretiensRealises = DbValue.IntFromDb(reader["Realises"]),
                            EntretiensValides = DbValue.IntFromDb(reader["Valides"]),
                            EvaluateursAAssigner = DbValue.IntFromDb(reader["SansEvaluateur"])
                        };

                        if (c.TotalEntretiens > 0)
                        {
                            c.TauxAvancementPct = Math.Round((double)(c.EntretiensRealises + c.EntretiensValides) / c.TotalEntretiens * 100.0, 1);
                        }

                        var noteObj = reader["NoteMoyenne"];
                        if (noteObj != null && noteObj != DBNull.Value)
                        {
                            c.NoteMoyenne = Math.Round(Convert.ToDouble(noteObj, CultureInfo.InvariantCulture), 2);
                        }

                        liste.Add(c);
                    }
                }
            }

            return liste;
        }

        /// <summary>
        /// Récupère une campagne par son identifiant.
        /// </summary>
        public async Task<CampagneEvaluation> ObtenirCampagneParIdAsync(int campagneId)
        {
            var liste = await ObtenirCampagnesAsync();
            return liste.FirstOrDefault(c => c.Id == campagneId);
        }

        /// <summary>
        /// Enregistre ou met à jour une campagne d'évaluation.
        /// </summary>
        public async Task EnregistrerCampagneAsync(CampagneEvaluation campagne)
        {
            if (campagne == null) throw new ArgumentNullException(nameof(campagne));
            if (string.IsNullOrWhiteSpace(campagne.Code)) throw new InvalidOperationException("Le code de la campagne est obligatoire.");
            if (string.IsNullOrWhiteSpace(campagne.Titre)) throw new InvalidOperationException("Le titre de la campagne est obligatoire.");

            campagne.Code = campagne.Code.Trim().ToUpperInvariant();
            campagne.Titre = campagne.Titre.Trim();

            using (var conn = _sqliteService.CreerConnexion())
            {
                // Vérifier l'unicité du code
                using (var cmdCheck = conn.CreateCommand())
                {
                    cmdCheck.CommandText = "SELECT Id FROM CampagnesEvaluation WHERE Code = @Code AND Id <> @Id;";
                    cmdCheck.Parameters.AjouterValeur("@Code", campagne.Code);
                    cmdCheck.Parameters.AjouterValeur("@Id", campagne.Id);
                    var existant = await cmdCheck.ExecuteScalarAsync();
                    if (existant != null && existant != DBNull.Value)
                    {
                        throw new InvalidOperationException($"Le code de campagne '{campagne.Code}' est déjà utilisé.");
                    }
                }

                using (var cmd = conn.CreateCommand())
                {
                    if (campagne.Id == 0)
                    {
                        cmd.CommandText = @"
                            INSERT INTO CampagnesEvaluation (Code, Titre, AnneeExercice, DateDebut, DateFin, Statut, Description, DateCreation)
                            VALUES (@Code, @Titre, @Annee, @DateDebut, @DateFin, @Statut, @Desc, @DateCreation);
                            SELECT last_insert_rowid();";
                        cmd.Parameters.AjouterValeur("@DateCreation", DbValue.DateToDb(DateTime.Now));
                    }
                    else
                    {
                        cmd.CommandText = @"
                            UPDATE CampagnesEvaluation
                            SET Code = @Code, Titre = @Titre, AnneeExercice = @Annee,
                                DateDebut = @DateDebut, DateFin = @DateFin, Statut = @Statut, Description = @Desc
                            WHERE Id = @Id;";
                        cmd.Parameters.AjouterValeur("@Id", campagne.Id);
                    }

                    cmd.Parameters.AjouterValeur("@Code", campagne.Code);
                    cmd.Parameters.AjouterValeur("@Titre", campagne.Titre);
                    cmd.Parameters.AjouterValeur("@Annee", campagne.AnneeExercice);
                    cmd.Parameters.AjouterValeur("@DateDebut", DbValue.DateToDb(campagne.DateDebut));
                    cmd.Parameters.AjouterValeur("@DateFin", DbValue.DateToDb(campagne.DateFin));
                    cmd.Parameters.AjouterValeur("@Statut", (int)campagne.Statut);
                    cmd.Parameters.AjouterValeur("@Desc", (object)campagne.Description ?? DBNull.Value);

                    if (campagne.Id == 0)
                    {
                        var newId = await cmd.ExecuteScalarAsync();
                        campagne.Id = Convert.ToInt32(newId);
                    }
                    else
                    {
                        await cmd.ExecuteNonQueryAsync();
                    }
                }
            }
        }

        /// <summary>
        /// Supprime une campagne et l'ensemble de ses entretiens non verrouillés en cascade.
        /// RÈGLE STRICTE D'IMMUTABILITÉ : rejet si la campagne contient au moins un entretien
        /// Validé ou Clôturé (document signé), pour ne jamais perdre un historique déjà figé.
        /// </summary>
        public async Task SupprimerCampagneAsync(int campagneId)
        {
            using (var conn = _sqliteService.CreerConnexion())
            {
                using (var cmdCheck = conn.CreateCommand())
                {
                    cmdCheck.CommandText = "SELECT COUNT(*) FROM EntretiensEvaluation WHERE CampagneId = @Id AND Statut >= @StatutValide;";
                    cmdCheck.Parameters.AjouterValeur("@Id", campagneId);
                    cmdCheck.Parameters.AjouterValeur("@StatutValide", (int)StatutEntretien.Valide);
                    int nbVerrouilles = DbValue.IntFromDb(await cmdCheck.ExecuteScalarAsync());

                    if (nbVerrouilles > 0)
                    {
                        throw new InvalidOperationException(
                            $"Cette campagne contient {nbVerrouilles} entretien(s) validé(s) ou clôturé(s) — des documents signés qui ne peuvent pas être supprimés. " +
                            "Clôturez ou archivez la campagne plutôt que de la supprimer.");
                    }
                }

                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "DELETE FROM CampagnesEvaluation WHERE Id = @Id;";
                    cmd.Parameters.AjouterValeur("@Id", campagneId);
                    await cmd.ExecuteNonQueryAsync();
                }
            }
        }

        /// <summary>
        /// Clôture une campagne d'évaluation.
        /// </summary>
        public async Task CloturerCampagneAsync(int campagneId)
        {
            using (var conn = _sqliteService.CreerConnexion())
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "UPDATE CampagnesEvaluation SET Statut = @Statut WHERE Id = @Id;";
                cmd.Parameters.AjouterValeur("@Statut", (int)StatutCampagne.Cloturee);
                cmd.Parameters.AjouterValeur("@Id", campagneId);
                await cmd.ExecuteNonQueryAsync();
            }
        }

        #endregion

        #region Initialisation automatique des Entretiens de Campagne

        /// <summary>
        /// Salarié interne pour l'initialisation de campagne.
        /// </summary>
        private class SalarieCandidatInit
        {
            public string EmployeeId { get; set; }
            public string NomComplet { get; set; }
            public int? PosteId { get; set; }
            public string ManagerMatricule { get; set; }
            public int? SuperieurPosteId { get; set; }
        }

        /// <summary>
        /// Initialise les fiches d'entretien individuel pour l'effectif actif de l'entreprise.
        /// Applique l'algorithme d'assignation N+1 à 3 étapes et injecte les critères types.
        /// Opération idempotente garantie par l'index unique (CampagneId, EmployeeId).
        /// </summary>
        public async Task<RapportInitialisationCampagne> InitialiserEntretiensCampagneAsync(int campagneId)
        {
            var rapport = new RapportInitialisationCampagne();

            // 1. Récupérer les salariés actifs
            // Respect de la règle EstActif : StatutSalarie == 0 ET (DateSortie est nulle OU >= aujourd'hui)
            var candidats = new List<SalarieCandidatInit>();
            string todayStr = DateTime.Today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

            using (var conn = _sqliteService.CreerConnexion())
            {
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = @"
                        SELECT 
                            e.EmployeeId, e.FirstName, e.LastName, e.PosteId, e.ManagerMatricule,
                            pos.SuperieurPosteId
                        FROM Employees e
                        LEFT JOIN Postes pos ON e.PosteId = pos.Id
                        WHERE e.StatutSalarie = 0 
                          AND (e.DateSortie IS NULL OR TRIM(e.DateSortie) = '' OR SUBSTR(e.DateSortie, 1, 10) >= @Today)
                        ORDER BY e.LastName ASC, e.FirstName ASC;";
                    cmd.Parameters.AjouterValeur("@Today", todayStr);

                    using (var reader = (SqliteDataReader)await cmd.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            var empId = DbValue.TextFromDb(reader["EmployeeId"]);
                            var prenom = DbValue.TextFromDb(reader["FirstName"]);
                            var nom = DbValue.TextFromDb(reader["LastName"]);

                            candidats.Add(new SalarieCandidatInit
                            {
                                EmployeeId = empId,
                                NomComplet = $"{prenom} {nom}".Trim(),
                                PosteId = DbValue.IntNullableFromDb(reader["PosteId"]),
                                ManagerMatricule = DbValue.TextFromDb(reader["ManagerMatricule"]),
                                SuperieurPosteId = DbValue.IntNullableFromDb(reader["SuperieurPosteId"])
                            });
                        }
                    }
                }

                rapport.TotalSalariesActifs = candidats.Count;

                // 2. Traiter chaque salarié actif
                foreach (var cand in candidats)
                {
                    // Vérifier si un entretien existe déjà pour ce salarié dans cette campagne
                    bool existeDeja;
                    using (var cmdCheck = conn.CreateCommand())
                    {
                        cmdCheck.CommandText = "SELECT COUNT(*) FROM EntretiensEvaluation WHERE CampagneId = @CampagneId AND EmployeeId = @EmpId;";
                        cmdCheck.Parameters.AjouterValeur("@CampagneId", campagneId);
                        cmdCheck.Parameters.AjouterValeur("@EmpId", cand.EmployeeId);
                        existeDeja = DbValue.IntFromDb(await cmdCheck.ExecuteScalarAsync()) > 0;
                    }

                    if (existeDeja)
                    {
                        rapport.EntretiensExistantDeja++;
                        continue;
                    }

                    // Résolution N+1 en 3 étapes :
                    // Étape 1 : ManagerMatricule sur la fiche salarié
                    string evaluateurMatricule = null;
                    if (!string.IsNullOrWhiteSpace(cand.ManagerMatricule))
                    {
                        evaluateurMatricule = cand.ManagerMatricule.Trim();
                    }
                    // Étape 2 : Dérivation via l'organigramme (SuperieurPosteId)
                    else if (cand.SuperieurPosteId.HasValue && _organigrammeService != null)
                    {
                        var titulairesSup = await _organigrammeService.ChargerTitulairesPosteAsync(cand.SuperieurPosteId.Value);
                        var titulairesActifs = titulairesSup.Where(t => t.StatutSalarie == 0).ToList();

                        if (titulairesActifs.Count == 1)
                        {
                            evaluateurMatricule = titulairesActifs[0].EmployeeId;
                        }
                    }

                    // Étape 3 : Si non trouvé ou ambigu, laisser vide et comptabiliser
                    if (string.IsNullOrWhiteSpace(evaluateurMatricule))
                    {
                        rapport.EvaluateursAAssigner++;
                        rapport.SalariesSansEvaluateur.Add(cand.NomComplet);
                    }
                    else
                    {
                        rapport.EvaluateursAssignes++;
                    }

                    // Création transactionnelle de l'entretien et de ses critères / objectifs par défaut
                    using (var tx = conn.BeginTransaction())
                    {
                        int entretienId;
                        using (var cmdInsert = conn.CreateCommand())
                        {
                            cmdInsert.Transaction = tx;
                            cmdInsert.CommandText = @"
                                INSERT INTO EntretiensEvaluation (
                                    CampagneId, EmployeeId, EvaluateurMatricule, DateEntretien, Statut,
                                    NoteGlobale, PointsForts, AxesProgres, DateCreation
                                ) VALUES (
                                    @CampagneId, @EmployeeId, @EvaluateurMatricule, @DateEntretien, 0,
                                    '0.00', '', '', @DateCreation
                                );
                                SELECT last_insert_rowid();";
                            cmdInsert.Parameters.AjouterValeur("@CampagneId", campagneId);
                            cmdInsert.Parameters.AjouterValeur("@EmployeeId", cand.EmployeeId);
                            cmdInsert.Parameters.AjouterValeur("@EvaluateurMatricule", (object)evaluateurMatricule ?? DBNull.Value);
                            cmdInsert.Parameters.AjouterValeur("@DateEntretien", DbValue.DateToDb(DateTime.Today));
                            cmdInsert.Parameters.AjouterValeur("@DateCreation", DbValue.DateToDb(DateTime.Now));

                            var idObj = await cmdInsert.ExecuteScalarAsync();
                            entretienId = Convert.ToInt32(idObj);
                        }

                        // Critères types :
                        // Charger les compétences requises du poste si disponible
                        List<PosteCompetence> compPoste = null;
                        if (cand.PosteId.HasValue && _organigrammeService != null)
                        {
                            compPoste = await _organigrammeService.ChargerCompetencesPosteAsync(cand.PosteId.Value);
                        }

                        if (compPoste != null && compPoste.Count > 0)
                        {
                            foreach (var cp in compPoste)
                            {
                                using (var cmdCrit = conn.CreateCommand())
                                {
                                    cmdCrit.Transaction = tx;
                                    cmdCrit.CommandText = @"
                                        INSERT INTO EntretienCriteres (EntretienId, CompetenceId, Categorie, Libelle, Note, Poids, Commentaire)
                                        VALUES (@EntretienId, @CompId, @Cat, @Lib, 3, @Poids, '');";
                                    cmdCrit.Parameters.AjouterValeur("@EntretienId", entretienId);
                                    cmdCrit.Parameters.AjouterValeur("@CompId", cp.CompetenceId);
                                    cmdCrit.Parameters.AjouterValeur("@Cat", cp.DomaineLibelle);
                                    cmdCrit.Parameters.AjouterValeur("@Lib", cp.CompetenceLibelle);
                                    cmdCrit.Parameters.AjouterValeur("@Poids", cp.EstObligatoire ? 2 : 1);
                                    await cmdCrit.ExecuteNonQueryAsync();
                                }
                            }
                        }
                        else
                        {
                            // Critères de base standards
                            var criteresDefaut = new[]
                            {
                                new { Cat = "Compétences Métier", Lib = "Tenue du poste & maîtrise technique", Poids = 2 },
                                new { Cat = "Organisation & Rigueur", Lib = "Qualité du travail, respect des délais & des procédures", Poids = 1 },
                                new { Cat = "Comportement & Savoir-Être", Lib = "Autonomie, assiduité & discrétion professionnelle", Poids = 1 },
                                new { Cat = "Communication & Relations", Lib = "Esprit d'équipe, communication & relation de travail", Poids = 1 }
                            };

                            foreach (var cd in criteresDefaut)
                            {
                                using (var cmdCrit = conn.CreateCommand())
                                {
                                    cmdCrit.Transaction = tx;
                                    cmdCrit.CommandText = @"
                                        INSERT INTO EntretienCriteres (EntretienId, CompetenceId, Categorie, Libelle, Note, Poids, Commentaire)
                                        VALUES (@EntretienId, NULL, @Cat, @Lib, 3, @Poids, '');";
                                    cmdCrit.Parameters.AjouterValeur("@EntretienId", entretienId);
                                    cmdCrit.Parameters.AjouterValeur("@Cat", cd.Cat);
                                    cmdCrit.Parameters.AjouterValeur("@Lib", cd.Lib);
                                    cmdCrit.Parameters.AjouterValeur("@Poids", cd.Poids);
                                    await cmdCrit.ExecuteNonQueryAsync();
                                }
                            }
                        }

                        // Objectifs types pour la période à venir N+1
                        var objDefaut = new[]
                        {
                            new { Intitule = "Atteindre les objectifs opérationnels et les indicateurs clés du poste", Poids = 1 },
                            new { Intitule = "Développer ses compétences et veiller au respect des standards qualité", Poids = 1 }
                        };

                        foreach (var od in objDefaut)
                        {
                            using (var cmdObj = conn.CreateCommand())
                            {
                                cmdObj.Transaction = tx;
                                cmdObj.CommandText = @"
                                    INSERT INTO EntretienObjectifs (EntretienId, TypeObjectif, Intitule, TauxAtteintePct, Poids)
                                    VALUES (@EntretienId, 1, @Intitule, 0, @Poids);";
                                cmdObj.Parameters.AjouterValeur("@EntretienId", entretienId);
                                cmdObj.Parameters.AjouterValeur("@Intitule", od.Intitule);
                                cmdObj.Parameters.AjouterValeur("@Poids", od.Poids);
                                await cmdObj.ExecuteNonQueryAsync();
                            }
                        }

                        tx.Commit();
                        rapport.EntretiensCrees++;
                    }
                }
            }

            return rapport;
        }

        #endregion

        #region Entretiens d'évaluation (CRUD & Conduite)

        /// <summary>
        /// Charge la liste des entretiens pour une campagne donnée.
        /// </summary>
        public async Task<List<EntretienEvaluation>> ObtenirEntretiensCampagneAsync(int campagneId)
        {
            var liste = new List<EntretienEvaluation>();

            using (var conn = _sqliteService.CreerConnexion())
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = @"
                    SELECT 
                        e.Id, e.CampagneId, e.EmployeeId, e.EvaluateurMatricule, e.DateEntretien, e.Statut,
                        e.NoteGlobale, e.PointsForts, e.AxesProgres, e.CommentairesSalarie, e.CommentairesEvaluateur,
                        e.AvisDirectionRH, e.SouhaitsEvolution, e.DateValidation, e.ValidePar, e.DateCreation,
                        emp.FirstName AS EmpPrenom, emp.LastName AS EmpNom, emp.Service AS EmpService, emp.DateEntree AS EmpDateEntree,
                        pos.Intitule AS EmpPoste,
                        mgr.FirstName AS MgrPrenom, mgr.LastName AS MgrNom,
                        pos_mgr.Intitule AS MgrPoste,
                        camp.Titre AS CampagneTitre, camp.AnneeExercice AS CampagneAnnee
                    FROM EntretiensEvaluation e
                    INNER JOIN CampagnesEvaluation camp ON e.CampagneId = camp.Id
                    INNER JOIN Employees emp ON e.EmployeeId = emp.EmployeeId
                    LEFT JOIN Postes pos ON emp.PosteId = pos.Id
                    LEFT JOIN Employees mgr ON e.EvaluateurMatricule = mgr.EmployeeId
                    LEFT JOIN Postes pos_mgr ON mgr.PosteId = pos_mgr.Id
                    WHERE e.CampagneId = @CampagneId
                    ORDER BY emp.LastName ASC, emp.FirstName ASC;";
                cmd.Parameters.AjouterValeur("@CampagneId", campagneId);

                using (var reader = (SqliteDataReader)await cmd.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                    {
                        var ent = MapperEntretienEntete(reader);
                        liste.Add(ent);
                    }
                }
            }

            return liste;
        }

        /// <summary>
        /// Charge un entretien d'évaluation complet avec ses critères, ses objectifs passés/futurs
        /// et ses besoins de formation.
        /// </summary>
        public async Task<EntretienEvaluation> ObtenirEntretienCompletAsync(int entretienId)
        {
            EntretienEvaluation ent = null;

            using (var conn = _sqliteService.CreerConnexion())
            {
                // 1. En-tête de l'entretien
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = @"
                        SELECT 
                            e.Id, e.CampagneId, e.EmployeeId, e.EvaluateurMatricule, e.DateEntretien, e.Statut,
                            e.NoteGlobale, e.PointsForts, e.AxesProgres, e.CommentairesSalarie, e.CommentairesEvaluateur,
                            e.AvisDirectionRH, e.SouhaitsEvolution, e.DateValidation, e.ValidePar, e.DateCreation,
                            emp.FirstName AS EmpPrenom, emp.LastName AS EmpNom, emp.Service AS EmpService, emp.DateEntree AS EmpDateEntree,
                            pos.Intitule AS EmpPoste,
                            mgr.FirstName AS MgrPrenom, mgr.LastName AS MgrNom,
                            pos_mgr.Intitule AS MgrPoste,
                            camp.Titre AS CampagneTitre, camp.AnneeExercice AS CampagneAnnee
                        FROM EntretiensEvaluation e
                        INNER JOIN CampagnesEvaluation camp ON e.CampagneId = camp.Id
                        INNER JOIN Employees emp ON e.EmployeeId = emp.EmployeeId
                        LEFT JOIN Postes pos ON emp.PosteId = pos.Id
                        LEFT JOIN Employees mgr ON e.EvaluateurMatricule = mgr.EmployeeId
                        LEFT JOIN Postes pos_mgr ON mgr.PosteId = pos_mgr.Id
                        WHERE e.Id = @Id;";
                    cmd.Parameters.AjouterValeur("@Id", entretienId);

                    using (var reader = (SqliteDataReader)await cmd.ExecuteReaderAsync())
                    {
                        if (await reader.ReadAsync())
                        {
                            ent = MapperEntretienEntete(reader);
                        }
                    }
                }

                if (ent == null) return null;

                // 2. Critères et compétences
                using (var cmdCrit = conn.CreateCommand())
                {
                    cmdCrit.CommandText = @"
                        SELECT Id, EntretienId, CompetenceId, Categorie, Libelle, Note, Poids, Commentaire
                        FROM EntretienCriteres
                        WHERE EntretienId = @EntretienId
                        ORDER BY Id ASC;";
                    cmdCrit.Parameters.AjouterValeur("@EntretienId", entretienId);

                    using (var reader = (SqliteDataReader)await cmdCrit.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            ent.Criteres.Add(new EntretienCritere
                            {
                                Id = DbValue.IntFromDb(reader["Id"]),
                                EntretienId = DbValue.IntFromDb(reader["EntretienId"]),
                                CompetenceId = DbValue.IntNullableFromDb(reader["CompetenceId"]),
                                Categorie = DbValue.TextFromDb(reader["Categorie"]),
                                Libelle = DbValue.TextFromDb(reader["Libelle"]),
                                Note = Math.Max(1, Math.Min(5, DbValue.IntFromDb(reader["Note"]))),
                                Poids = Math.Max(1, DbValue.IntFromDb(reader["Poids"])),
                                Commentaire = DbValue.TextFromDb(reader["Commentaire"])
                            });
                        }
                    }
                }

                // 3. Objectifs (Passés N-1 et Nouveaux N+1)
                using (var cmdObj = conn.CreateCommand())
                {
                    cmdObj.CommandText = @"
                        SELECT Id, EntretienId, TypeObjectif, Intitule, IndicateurMesure, CibleAttendue, ResultatObtenu, TauxAtteintePct, Appreciation, Echeance, Poids
                        FROM EntretienObjectifs
                        WHERE EntretienId = @EntretienId
                        ORDER BY TypeObjectif ASC, Id ASC;";
                    cmdObj.Parameters.AjouterValeur("@EntretienId", entretienId);

                    using (var reader = (SqliteDataReader)await cmdObj.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            var typeObj = (TypeObjectifEntretien)DbValue.IntFromDb(reader["TypeObjectif"]);
                            var obj = new EntretienObjectif
                            {
                                Id = DbValue.IntFromDb(reader["Id"]),
                                EntretienId = DbValue.IntFromDb(reader["EntretienId"]),
                                TypeObjectif = typeObj,
                                Intitule = DbValue.TextFromDb(reader["Intitule"]),
                                IndicateurMesure = DbValue.TextFromDb(reader["IndicateurMesure"]),
                                CibleAttendue = DbValue.TextFromDb(reader["CibleAttendue"]),
                                ResultatObtenu = DbValue.TextFromDb(reader["ResultatObtenu"]),
                                TauxAtteintePct = DbValue.IntFromDb(reader["TauxAtteintePct"]),
                                Appreciation = DbValue.TextFromDb(reader["Appreciation"]),
                                Echeance = DbValue.TextFromDb(reader["Echeance"]),
                                Poids = Math.Max(1, DbValue.IntFromDb(reader["Poids"]))
                            };

                            if (typeObj == TypeObjectifEntretien.ObjectifPasse)
                            {
                                ent.ObjectifsPasses.Add(obj);
                            }
                            else
                            {
                                ent.NouveauxObjectifs.Add(obj);
                            }
                        }
                    }
                }

                // 4. Besoins de formation
                using (var cmdForm = conn.CreateCommand())
                {
                    cmdForm.CommandText = @"
                        SELECT Id, EntretienId, CompetenceId, ThemeFormation, CompetenceVisee, Priorite, AvisEvaluateur, Statut
                        FROM EntretienBesoinsFormation
                        WHERE EntretienId = @EntretienId
                        ORDER BY Priorite DESC, Id ASC;";
                    cmdForm.Parameters.AjouterValeur("@EntretienId", entretienId);

                    using (var reader = (SqliteDataReader)await cmdForm.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            ent.BesoinsFormation.Add(new EntretienBesoinFormation
                            {
                                Id = DbValue.IntFromDb(reader["Id"]),
                                EntretienId = DbValue.IntFromDb(reader["EntretienId"]),
                                CompetenceId = DbValue.IntNullableFromDb(reader["CompetenceId"]),
                                ThemeFormation = DbValue.TextFromDb(reader["ThemeFormation"]),
                                CompetenceVisee = DbValue.TextFromDb(reader["CompetenceVisee"]),
                                Priorite = (PrioriteFormation)Math.Max(1, DbValue.IntFromDb(reader["Priorite"])),
                                AvisEvaluateur = DbValue.TextFromDb(reader["AvisEvaluateur"]),
                                Statut = (StatutBesoinFormation)DbValue.IntFromDb(reader["Statut"])
                            });
                        }
                    }
                }
            }

            ent.RecalculerNoteGlobale();
            return ent;
        }

        /// <summary>
        /// Enregistre l'ensemble d'un entretien (en-tête, critères, objectifs et besoins de formation).
        /// RÈGLE STRICTE D'IMMUTABILITÉ : rejet si l'entretien est déjà Validé ou Clôturé.
        /// </summary>
        public async Task EnregistrerEntretienAsync(EntretienEvaluation entretien)
        {
            if (entretien == null) throw new ArgumentNullException(nameof(entretien));
            if (entretien.Id <= 0) throw new InvalidOperationException("L'entretien doit d'abord exister.");

            using (var conn = _sqliteService.CreerConnexion())
            {
                // Contrôle d'immutabilité côté service
                using (var cmdStatut = conn.CreateCommand())
                {
                    cmdStatut.CommandText = "SELECT Statut FROM EntretiensEvaluation WHERE Id = @Id;";
                    cmdStatut.Parameters.AjouterValeur("@Id", entretien.Id);
                    var currentStatut = DbValue.IntFromDb(await cmdStatut.ExecuteScalarAsync());

                    if (currentStatut >= (int)StatutEntretien.Valide)
                    {
                        throw new InvalidOperationException(
                            "Cet entretien d'évaluation a été validé et signé. Il est strictement verrouillé et ne peut plus être modifié.");
                    }
                }

                // La validation (et la clôture) est une transition unique, contrôlée par
                // ChangerStatutEntretienAsync, qui scelle la date et l'auteur de la validation.
                // Écrire le statut Validé par un simple enregistrement laissait l'entretien
                // validé sans date de validation, puis faisait échouer la transition contrôlée
                // (statut lu déjà « Validé »).
                if (entretien.Statut >= StatutEntretien.Valide)
                {
                    throw new InvalidOperationException(
                        "Un entretien ne peut pas être validé par un simple enregistrement : utilisez « Valider & Sceller », " +
                        "qui scelle la date de validation.");
                }

                // Recalcul de la note globale
                entretien.RecalculerNoteGlobale();
                string noteGlobaleTexte = entretien.NoteGlobale.ToString("F2", CultureInfo.InvariantCulture);

                using (var tx = conn.BeginTransaction())
                {
                    // 1. Mise à jour de l'en-tête
                    using (var cmd = conn.CreateCommand())
                    {
                        cmd.Transaction = tx;
                        cmd.CommandText = @"
                            UPDATE EntretiensEvaluation
                            SET EvaluateurMatricule = @EvalMatricule,
                                DateEntretien = @DateEntretien,
                                Statut = @Statut,
                                NoteGlobale = @NoteGlobale,
                                PointsForts = @PointsForts,
                                AxesProgres = @AxesProgres,
                                CommentairesSalarie = @ComSalarie,
                                CommentairesEvaluateur = @ComEvaluateur,
                                AvisDirectionRH = @AvisRH,
                                SouhaitsEvolution = @Souhaits
                            WHERE Id = @Id;";
                        cmd.Parameters.AjouterValeur("@Id", entretien.Id);
                        cmd.Parameters.AjouterValeur("@EvalMatricule", (object)entretien.EvaluateurMatricule ?? DBNull.Value);
                        cmd.Parameters.AjouterValeur("@DateEntretien", DbValue.DateNullableToDb(entretien.DateEntretien));
                        cmd.Parameters.AjouterValeur("@Statut", (int)entretien.Statut);
                        cmd.Parameters.AjouterValeur("@NoteGlobale", noteGlobaleTexte);
                        cmd.Parameters.AjouterValeur("@PointsForts", (object)entretien.PointsForts ?? DBNull.Value);
                        cmd.Parameters.AjouterValeur("@AxesProgres", (object)entretien.AxesProgres ?? DBNull.Value);
                        cmd.Parameters.AjouterValeur("@ComSalarie", (object)entretien.CommentairesSalarie ?? DBNull.Value);
                        cmd.Parameters.AjouterValeur("@ComEvaluateur", (object)entretien.CommentairesEvaluateur ?? DBNull.Value);
                        cmd.Parameters.AjouterValeur("@AvisRH", (object)entretien.AvisDirectionRH ?? DBNull.Value);
                        cmd.Parameters.AjouterValeur("@Souhaits", (object)entretien.SouhaitsEvolution ?? DBNull.Value);
                        await cmd.ExecuteNonQueryAsync();
                    }

                    // 2. Remplacement des critères
                    using (var cmdDelCrit = conn.CreateCommand())
                    {
                        cmdDelCrit.Transaction = tx;
                        cmdDelCrit.CommandText = "DELETE FROM EntretienCriteres WHERE EntretienId = @Id;";
                        cmdDelCrit.Parameters.AjouterValeur("@Id", entretien.Id);
                        await cmdDelCrit.ExecuteNonQueryAsync();
                    }

                    foreach (var crit in entretien.Criteres)
                    {
                        using (var cmdCrit = conn.CreateCommand())
                        {
                            cmdCrit.Transaction = tx;
                            cmdCrit.CommandText = @"
                                INSERT INTO EntretienCriteres (EntretienId, CompetenceId, Categorie, Libelle, Note, Poids, Commentaire)
                                VALUES (@EntretienId, @CompId, @Cat, @Lib, @Note, @Poids, @Com);";
                            cmdCrit.Parameters.AjouterValeur("@EntretienId", entretien.Id);
                            cmdCrit.Parameters.AjouterValeur("@CompId", (object)crit.CompetenceId ?? DBNull.Value);
                            cmdCrit.Parameters.AjouterValeur("@Cat", crit.Categorie ?? "Générale");
                            cmdCrit.Parameters.AjouterValeur("@Lib", crit.Libelle ?? "");
                            cmdCrit.Parameters.AjouterValeur("@Note", crit.Note);
                            cmdCrit.Parameters.AjouterValeur("@Poids", crit.Poids);
                            cmdCrit.Parameters.AjouterValeur("@Com", (object)crit.Commentaire ?? DBNull.Value);
                            await cmdCrit.ExecuteNonQueryAsync();
                        }
                    }

                    // 3. Remplacement des objectifs (Passés N-1 et Nouveaux N+1)
                    using (var cmdDelObj = conn.CreateCommand())
                    {
                        cmdDelObj.Transaction = tx;
                        cmdDelObj.CommandText = "DELETE FROM EntretienObjectifs WHERE EntretienId = @Id;";
                        cmdDelObj.Parameters.AjouterValeur("@Id", entretien.Id);
                        await cmdDelObj.ExecuteNonQueryAsync();
                    }

                    var tousObjectifs = new List<EntretienObjectif>();
                    if (entretien.ObjectifsPasses != null)
                    {
                        foreach (var o in entretien.ObjectifsPasses) { o.TypeObjectif = TypeObjectifEntretien.ObjectifPasse; tousObjectifs.Add(o); }
                    }
                    if (entretien.NouveauxObjectifs != null)
                    {
                        foreach (var o in entretien.NouveauxObjectifs) { o.TypeObjectif = TypeObjectifEntretien.NouvelObjectif; tousObjectifs.Add(o); }
                    }

                    foreach (var obj in tousObjectifs)
                    {
                        using (var cmdObj = conn.CreateCommand())
                        {
                            cmdObj.Transaction = tx;
                            cmdObj.CommandText = @"
                                INSERT INTO EntretienObjectifs (EntretienId, TypeObjectif, Intitule, IndicateurMesure, CibleAttendue, ResultatObtenu, TauxAtteintePct, Appreciation, Echeance, Poids)
                                VALUES (@EntretienId, @Type, @Intitule, @Indicateur, @Cible, @Resultat, @Taux, @Apprec, @Echeance, @Poids);";
                            cmdObj.Parameters.AjouterValeur("@EntretienId", entretien.Id);
                            cmdObj.Parameters.AjouterValeur("@Type", (int)obj.TypeObjectif);
                            cmdObj.Parameters.AjouterValeur("@Intitule", obj.Intitule ?? "");
                            cmdObj.Parameters.AjouterValeur("@Indicateur", (object)obj.IndicateurMesure ?? DBNull.Value);
                            cmdObj.Parameters.AjouterValeur("@Cible", (object)obj.CibleAttendue ?? DBNull.Value);
                            cmdObj.Parameters.AjouterValeur("@Resultat", (object)obj.ResultatObtenu ?? DBNull.Value);
                            cmdObj.Parameters.AjouterValeur("@Taux", obj.TauxAtteintePct);
                            cmdObj.Parameters.AjouterValeur("@Apprec", (object)obj.Appreciation ?? DBNull.Value);
                            cmdObj.Parameters.AjouterValeur("@Echeance", (object)obj.Echeance ?? DBNull.Value);
                            cmdObj.Parameters.AjouterValeur("@Poids", obj.Poids);
                            await cmdObj.ExecuteNonQueryAsync();
                        }
                    }

                    // 4. Remplacement des besoins de formation
                    using (var cmdDelForm = conn.CreateCommand())
                    {
                        cmdDelForm.Transaction = tx;
                        cmdDelForm.CommandText = "DELETE FROM EntretienBesoinsFormation WHERE EntretienId = @Id;";
                        cmdDelForm.Parameters.AjouterValeur("@Id", entretien.Id);
                        await cmdDelForm.ExecuteNonQueryAsync();
                    }

                    foreach (var form in entretien.BesoinsFormation)
                    {
                        using (var cmdForm = conn.CreateCommand())
                        {
                            cmdForm.Transaction = tx;
                            cmdForm.CommandText = @"
                                INSERT INTO EntretienBesoinsFormation (EntretienId, CompetenceId, ThemeFormation, CompetenceVisee, Priorite, AvisEvaluateur, Statut)
                                VALUES (@EntretienId, @CompId, @Theme, @CompVisee, @Priorite, @Avis, @Statut);";
                            cmdForm.Parameters.AjouterValeur("@EntretienId", entretien.Id);
                            cmdForm.Parameters.AjouterValeur("@CompId", (object)form.CompetenceId ?? DBNull.Value);
                            cmdForm.Parameters.AjouterValeur("@Theme", form.ThemeFormation ?? "");
                            cmdForm.Parameters.AjouterValeur("@CompVisee", (object)form.CompetenceVisee ?? DBNull.Value);
                            cmdForm.Parameters.AjouterValeur("@Priorite", (int)form.Priorite);
                            cmdForm.Parameters.AjouterValeur("@Avis", (object)form.AvisEvaluateur ?? DBNull.Value);
                            cmdForm.Parameters.AjouterValeur("@Statut", (int)form.Statut);
                            await cmdForm.ExecuteNonQueryAsync();
                        }
                    }

                    tx.Commit();
                }
            }
        }

        /// <summary>
        /// Fait progresser le statut d'un entretien (ex: Planifié -> Réalisé -> Validé).
        /// Si le statut devient Validé, la date de validation et l'utilisateur qui valide
        /// sont scellés : c'est le seul chemin de validation (EnregistrerEntretienAsync refuse
        /// d'écrire un statut Validé ou Clôturé).
        /// RÈGLE STRICTE D'IMMUTABILITÉ : rejet si l'entretien est déjà Validé ou Clôturé,
        /// même garde que EnregistrerEntretienAsync/SupprimerEntretienAsync — un document
        /// signé ne doit jamais pouvoir être rouvert ou reclassé par un appel direct à ce service.
        /// </summary>
        /// <param name="utilisateur">Auteur de la validation ; à défaut, l'utilisateur connecté (UserSession).</param>
        public async Task ChangerStatutEntretienAsync(int entretienId, StatutEntretien nouveauStatut, string utilisateur = null)
        {
            using (var conn = _sqliteService.CreerConnexion())
            {
                using (var cmdCheck = conn.CreateCommand())
                {
                    cmdCheck.CommandText = "SELECT Statut FROM EntretiensEvaluation WHERE Id = @Id;";
                    cmdCheck.Parameters.AjouterValeur("@Id", entretienId);
                    var currentStatut = DbValue.IntFromDb(await cmdCheck.ExecuteScalarAsync());

                    if (currentStatut >= (int)StatutEntretien.Valide)
                    {
                        throw new InvalidOperationException(
                            "Cet entretien d'évaluation a été validé et signé. Il est strictement verrouillé et son statut ne peut plus être modifié.");
                    }
                }

            using (var cmd = conn.CreateCommand())
            {
                if (nouveauStatut == StatutEntretien.Valide)
                {
                    cmd.CommandText = @"
                        UPDATE EntretiensEvaluation
                        SET Statut = @Statut, DateValidation = @DateValidation, ValidePar = @ValidePar
                        WHERE Id = @Id;";
                    cmd.Parameters.AjouterValeur("@DateValidation", DbValue.DateToDb(DateTime.Now));
                    string auteur = !string.IsNullOrWhiteSpace(utilisateur) ? utilisateur.Trim() : UtilisateurCourant();
                    cmd.Parameters.AjouterValeur("@ValidePar", (object)auteur ?? DBNull.Value);
                }
                else
                {
                    cmd.CommandText = "UPDATE EntretiensEvaluation SET Statut = @Statut WHERE Id = @Id;";
                }

                cmd.Parameters.AjouterValeur("@Statut", (int)nouveauStatut);
                cmd.Parameters.AjouterValeur("@Id", entretienId);
                await cmd.ExecuteNonQueryAsync();
            }
            }
        }

        /// <summary>
        /// Assigne ou modifie manuellement l'évaluateur N+1 d'un entretien.
        /// RÈGLE STRICTE D'IMMUTABILITÉ : même garde que ChangerStatutEntretienAsync —
        /// un évaluateur ne peut plus être réassigné sur un entretien déjà verrouillé.
        /// </summary>
        public async Task AssignerEvaluateurAsync(int entretienId, string evaluateurMatricule)
        {
            using (var conn = _sqliteService.CreerConnexion())
            {
                using (var cmdCheck = conn.CreateCommand())
                {
                    cmdCheck.CommandText = "SELECT Statut FROM EntretiensEvaluation WHERE Id = @Id;";
                    cmdCheck.Parameters.AjouterValeur("@Id", entretienId);
                    var currentStatut = DbValue.IntFromDb(await cmdCheck.ExecuteScalarAsync());

                    if (currentStatut >= (int)StatutEntretien.Valide)
                    {
                        throw new InvalidOperationException(
                            "Cet entretien d'évaluation a été validé et signé. L'évaluateur ne peut plus être réassigné.");
                    }
                }

                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "UPDATE EntretiensEvaluation SET EvaluateurMatricule = @Matricule WHERE Id = @Id;";
                    cmd.Parameters.AjouterValeur("@Matricule", (object)evaluateurMatricule?.Trim() ?? DBNull.Value);
                    cmd.Parameters.AjouterValeur("@Id", entretienId);
                    await cmd.ExecuteNonQueryAsync();
                }
            }
        }

        /// <summary>
        /// Supprime un entretien individuel s'il n'est pas encore validé.
        /// </summary>
        public async Task SupprimerEntretienAsync(int entretienId)
        {
            using (var conn = _sqliteService.CreerConnexion())
            {
                using (var cmdCheck = conn.CreateCommand())
                {
                    cmdCheck.CommandText = "SELECT Statut FROM EntretiensEvaluation WHERE Id = @Id;";
                    cmdCheck.Parameters.AjouterValeur("@Id", entretienId);
                    var statut = DbValue.IntFromDb(await cmdCheck.ExecuteScalarAsync());
                    if (statut >= (int)StatutEntretien.Valide)
                    {
                        throw new InvalidOperationException("Un entretien validé ne peut pas être supprimé.");
                    }
                }

                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "DELETE FROM EntretiensEvaluation WHERE Id = @Id;";
                    cmd.Parameters.AjouterValeur("@Id", entretienId);
                    await cmd.ExecuteNonQueryAsync();
                }
            }
        }

        #endregion

        #region Historique de carrière & Statistiques

        /// <summary>
        /// Récupère l'historique complet des entretiens passés pour un collaborateur donné.
        /// </summary>
        public async Task<List<HistoriqueEntretienSalarie>> ObtenirHistoriqueEntretiensSalarieAsync(string employeeId)
        {
            var historique = new List<HistoriqueEntretienSalarie>();
            if (string.IsNullOrWhiteSpace(employeeId)) return historique;

            using (var conn = _sqliteService.CreerConnexion())
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = @"
                    SELECT 
                        e.Id AS EntretienId, e.CampagneId, e.DateEntretien, e.Statut, e.NoteGlobale,
                        camp.Code AS CampagneCode, camp.Titre AS CampagneTitre, camp.AnneeExercice,
                        mgr.FirstName AS MgrPrenom, mgr.LastName AS MgrNom,
                        pos_mgr.Intitule AS MgrPoste,
                        (SELECT COUNT(*) FROM EntretienObjectifs WHERE EntretienId = e.Id AND TypeObjectif = 0) AS ObjPassesCount,
                        (SELECT AVG(TauxAtteintePct) FROM EntretienObjectifs WHERE EntretienId = e.Id AND TypeObjectif = 0) AS TauxMoyenObj,
                        (SELECT COUNT(*) FROM EntretienObjectifs WHERE EntretienId = e.Id AND TypeObjectif = 1) AS NouvObjCount,
                        (SELECT COUNT(*) FROM EntretienBesoinsFormation WHERE EntretienId = e.Id) AS FormCount
                    FROM EntretiensEvaluation e
                    INNER JOIN CampagnesEvaluation camp ON e.CampagneId = camp.Id
                    LEFT JOIN Employees mgr ON e.EvaluateurMatricule = mgr.EmployeeId
                    LEFT JOIN Postes pos_mgr ON mgr.PosteId = pos_mgr.Id
                    WHERE e.EmployeeId = @EmpId
                    ORDER BY camp.AnneeExercice DESC, e.DateEntretien DESC, e.Id DESC;";
                cmd.Parameters.AjouterValeur("@EmpId", employeeId.Trim());

                using (var reader = (SqliteDataReader)await cmd.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                    {
                        var noteTexte = DbValue.TextFromDb(reader["NoteGlobale"]);
                        double noteVal = 0.0;
                        if (!string.IsNullOrWhiteSpace(noteTexte))
                        {
                            double.TryParse(noteTexte, NumberStyles.Any, CultureInfo.InvariantCulture, out noteVal);
                        }

                        var statut = (StatutEntretien)DbValue.IntFromDb(reader["Statut"]);
                        var mgrPrenom = DbValue.TextFromDb(reader["MgrPrenom"]);
                        var mgrNom = DbValue.TextFromDb(reader["MgrNom"]);
                        var mgrPoste = DbValue.TextFromDb(reader["MgrPoste"]);

                        var h = new HistoriqueEntretienSalarie
                        {
                            EntretienId = DbValue.IntFromDb(reader["EntretienId"]),
                            CampagneId = DbValue.IntFromDb(reader["CampagneId"]),
                            CampagneCode = DbValue.TextFromDb(reader["CampagneCode"]),
                            CampagneTitre = DbValue.TextFromDb(reader["CampagneTitre"]),
                            AnneeExercice = DbValue.IntFromDb(reader["AnneeExercice"]),
                            DateEntretien = DbValue.DateNullableFromDb(reader["DateEntretien"]),
                            EvaluateurNom = !string.IsNullOrWhiteSpace(mgrNom) ? $"{mgrPrenom} {mgrNom}".Trim() : "—",
                            EvaluateurPoste = mgrPoste ?? "—",
                            NoteGlobale = noteVal,
                            Statut = statut,
                            StatutLibelle = ObtenirStatutLibelle(statut),
                            CouleurStatut = ObtenirCouleurStatut(statut),
                            ObjectifsPassesCount = DbValue.IntFromDb(reader["ObjPassesCount"]),
                            NouveauxObjectifsCount = DbValue.IntFromDb(reader["NouvObjCount"]),
                            FormationsCount = DbValue.IntFromDb(reader["FormCount"])
                        };

                        var tauxObj = reader["TauxMoyenObj"];
                        if (tauxObj != null && tauxObj != DBNull.Value)
                        {
                            h.TauxAtteinteMoyen = Math.Round(Convert.ToDouble(tauxObj, CultureInfo.InvariantCulture), 1);
                        }

                        if (noteVal <= 0) h.MentionGlobale = "Non noté";
                        else if (noteVal < 2.0) h.MentionGlobale = "Insuffisant";
                        else if (noteVal < 3.0) h.MentionGlobale = "À développer";
                        else if (noteVal < 4.0) h.MentionGlobale = "Conforme aux attentes";
                        else if (noteVal < 4.5) h.MentionGlobale = "Dépasse les attentes";
                        else h.MentionGlobale = "Remarquable / Exceptionnel";

                        historique.Add(h);
                    }
                }
            }

            return historique;
        }

        /// <summary>
        /// Calcule les statistiques d'avancement et la distribution des notes pour une campagne.
        /// </summary>
        public async Task<StatistiquesCampagne> CalculerStatistiquesCampagneAsync(int campagneId)
        {
            var stats = new StatistiquesCampagne();

            using (var conn = _sqliteService.CreerConnexion())
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = @"
                    SELECT 
                        Statut,
                        NoteGlobale,
                        EvaluateurMatricule
                    FROM EntretiensEvaluation
                    WHERE CampagneId = @CampagneId;";
                cmd.Parameters.AjouterValeur("@CampagneId", campagneId);

                double sommeNotes = 0.0;
                int countNotes = 0;

                using (var reader = (SqliteDataReader)await cmd.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                    {
                        stats.TotalSalariesCibles++;
                        var statut = (StatutEntretien)DbValue.IntFromDb(reader["Statut"]);

                        switch (statut)
                        {
                            case StatutEntretien.Planifie: stats.EntretiensPlanifies++; break;
                            case StatutEntretien.EnCours: stats.EntretiensEnCours++; break;
                            case StatutEntretien.Realise: stats.EntretiensRealises++; break;
                            case StatutEntretien.Valide: stats.EntretiensValides++; break;
                            case StatutEntretien.Cloture: stats.EntretiensClotures++; break;
                        }

                        var evalMat = DbValue.TextFromDb(reader["EvaluateurMatricule"]);
                        if (string.IsNullOrWhiteSpace(evalMat))
                        {
                            stats.EvaluateursAAssignerCount++;
                        }

                        var noteTexte = DbValue.TextFromDb(reader["NoteGlobale"]);
                        if (!string.IsNullOrWhiteSpace(noteTexte))
                        {
                            if (double.TryParse(noteTexte, NumberStyles.Any, CultureInfo.InvariantCulture, out double noteVal) && noteVal > 0)
                            {
                                sommeNotes += noteVal;
                                countNotes++;

                                if (noteVal < 2.0) stats.CountInsuffisant++;
                                else if (noteVal < 3.0) stats.CountADevelopper++;
                                else if (noteVal < 4.0) stats.CountConforme++;
                                else if (noteVal < 4.5) stats.CountDepasse++;
                                else stats.CountRemarquable++;
                            }
                        }
                    }
                }

                if (countNotes > 0)
                {
                    stats.NoteMoyenneGlobale = Math.Round(sommeNotes / countNotes, 2);
                }
            }

            return stats;
        }

        #endregion

        #region Helpers de Mapping

        private static EntretienEvaluation MapperEntretienEntete(SqliteDataReader reader)
        {
            var empPrenom = DbValue.TextFromDb(reader["EmpPrenom"]);
            var empNom = DbValue.TextFromDb(reader["EmpNom"]);
            var empDateEntree = DbValue.DateNullableFromDb(reader["EmpDateEntree"]);
            string anciennete = "-";
            if (empDateEntree.HasValue)
            {
                var annees = DateTime.Today.Year - empDateEntree.Value.Year;
                if (DateTime.Today < empDateEntree.Value.AddYears(annees)) annees--;
                anciennete = annees > 0 ? $"{annees} an(s)" : "< 1 an";
            }

            var mgrPrenom = DbValue.TextFromDb(reader["MgrPrenom"]);
            var mgrNom = DbValue.TextFromDb(reader["MgrNom"]);

            var ent = new EntretienEvaluation
            {
                Id = DbValue.IntFromDb(reader["Id"]),
                CampagneId = DbValue.IntFromDb(reader["CampagneId"]),
                CampagneTitre = DbValue.TextFromDb(reader["CampagneTitre"]),
                CampagneAnnee = DbValue.IntFromDb(reader["CampagneAnnee"]),
                EmployeeId = DbValue.TextFromDb(reader["EmployeeId"]),
                EmployeeNomComplet = $"{empPrenom} {empNom}".Trim(),
                EmployeeService = DbValue.TextFromDb(reader["EmpService"]),
                EmployeePoste = DbValue.TextFromDb(reader["EmpPoste"]),
                EmployeeDateEmbauche = empDateEntree,
                EmployeeAnciennete = anciennete,
                EvaluateurMatricule = DbValue.TextFromDb(reader["EvaluateurMatricule"]),
                EvaluateurNomComplet = !string.IsNullOrWhiteSpace(mgrNom) ? $"{mgrPrenom} {mgrNom}".Trim() : "—",
                EvaluateurPoste = DbValue.TextFromDb(reader["MgrPoste"]) ?? "—",
                DateEntretien = DbValue.DateNullableFromDb(reader["DateEntretien"]),
                Statut = (StatutEntretien)DbValue.IntFromDb(reader["Statut"]),
                PointsForts = DbValue.TextFromDb(reader["PointsForts"]),
                AxesProgres = DbValue.TextFromDb(reader["AxesProgres"]),
                CommentairesSalarie = DbValue.TextFromDb(reader["CommentairesSalarie"]),
                CommentairesEvaluateur = DbValue.TextFromDb(reader["CommentairesEvaluateur"]),
                AvisDirectionRH = DbValue.TextFromDb(reader["AvisDirectionRH"]),
                SouhaitsEvolution = DbValue.TextFromDb(reader["SouhaitsEvolution"]),
                DateValidation = DbValue.DateNullableFromDb(reader["DateValidation"]),
                ValidePar = DbValue.TextFromDb(reader["ValidePar"]),
                DateCreation = DbValue.DateFromDb(reader["DateCreation"], DateTime.Now)
            };

            var noteTexte = DbValue.TextFromDb(reader["NoteGlobale"]);
            if (!string.IsNullOrWhiteSpace(noteTexte) && double.TryParse(noteTexte, NumberStyles.Any, CultureInfo.InvariantCulture, out double nVal))
            {
                ent.NoteGlobale = nVal;
            }

            return ent;
        }

        /// <summary>
        /// Utilisateur de l'application connecté (nom affiché, sinon identifiant de connexion),
        /// à défaut le compte Windows : même convention que les autres modules RH.
        /// </summary>
        private static string UtilisateurCourant()
        {
            var u = UserSession.Instance.CurrentUser;
            if (u == null) return Environment.UserName;
            return string.IsNullOrWhiteSpace(u.DisplayName) ? u.Username : u.DisplayName;
        }

        private static string ObtenirStatutLibelle(StatutEntretien s)
        {
            switch (s)
            {
                case StatutEntretien.Planifie: return "Planifié";
                case StatutEntretien.EnCours: return "En cours";
                case StatutEntretien.Realise: return "Réalisé";
                case StatutEntretien.Valide: return "Validé & Signé";
                case StatutEntretien.Cloture: return "Clôturé";
                default: return s.ToString();
            }
        }

        private static string ObtenirCouleurStatut(StatutEntretien s)
        {
            switch (s)
            {
                case StatutEntretien.Planifie: return "#0288D1";
                case StatutEntretien.EnCours: return "#E65100";
                case StatutEntretien.Realise: return "#14675A";
                case StatutEntretien.Valide: return "#1B5E20";
                case StatutEntretien.Cloture: return "#616161";
                default: return "#757575";
            }
        }

        #endregion
    }
}
