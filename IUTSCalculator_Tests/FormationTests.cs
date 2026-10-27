using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using IUTSCalculator.Models;
using IUTSCalculator.Services;

namespace IUTSCalculator.Tests
{
    [TestFixture]
    [Microsoft.VisualStudio.TestTools.UnitTesting.TestClass]
    public class FormationTests
    {
        private string _tempDb;
        private SQLiteService _sqlite;
        private DataService _dataService;
        private OrganigrammeService _organigrammeService;
        private EvaluationService _evaluationService;
        private FormationService _formationService;
        private FormationExportService _exportService;

        [SetUp]
        [Microsoft.VisualStudio.TestTools.UnitTesting.TestInitialize]
        public void Setup()
        {
            _tempDb = Path.Combine(Path.GetTempPath(), $"test_form_{Guid.NewGuid():N}.db");
            _sqlite = new SQLiteService(_tempDb);
            _dataService = new DataService(_tempDb);
            _organigrammeService = new OrganigrammeService(_sqlite);
            _evaluationService = new EvaluationService(_sqlite, _organigrammeService);
            _formationService = new FormationService(_sqlite);
            _exportService = new FormationExportService();

            // Insérer quelques employés de test (connexion retournée déjà ouverte par SQLiteService)
            using (var conn = _sqlite.CreerConnexion())
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = @"
                    INSERT OR IGNORE INTO Employees (EmployeeId, FirstName, LastName, Service, DateEntree, StatutSalarie)
                    VALUES ('EMP-001', 'Oumar', 'TRAORE', 'Informatique', '2022-01-15', 0);
                    INSERT OR IGNORE INTO Employees (EmployeeId, FirstName, LastName, Service, DateEntree, StatutSalarie)
                    VALUES ('EMP-002', 'Awa', 'SANOGO', 'Ressources Humaines', '2023-03-01', 0);
                    INSERT OR IGNORE INTO Employees (EmployeeId, FirstName, LastName, Service, DateEntree, StatutSalarie)
                    VALUES ('EMP-003', 'Paul', 'KABORE', 'Comptabilité', '2021-06-10', 0);";
                cmd.ExecuteNonQuery();
            }
        }

        [TearDown]
        [Microsoft.VisualStudio.TestTools.UnitTesting.TestCleanup]
        public void TearDown()
        {
            try
            {
                if (File.Exists(_tempDb))
                {
                    File.Delete(_tempDb);
                }
            }
            catch
            {
                // Ignoré si le fichier est encore temporairement verrouillé
            }
        }

        [Test]
        [Microsoft.VisualStudio.TestTools.UnitTesting.TestMethod]
        public async Task Schema_Migration32_TablesEtColonnesCrees()
        {
            using (var conn = _sqlite.CreerConnexion())
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = @"
                    SELECT count(*) FROM sqlite_master WHERE type='table' 
                    AND name IN ('PlansFormation', 'OrganismesFormation', 'FormationsCatalogue', 'SessionsFormation', 'SessionInscriptions');";
                var tableCount = Convert.ToInt32(await cmd.ExecuteScalarAsync());
                Assert.That(tableCount, Is.EqualTo(5), "Les 5 tables RH-04 doivent être créées.");

                cmd.CommandText = @"
                    SELECT count(*) FROM sqlite_master WHERE type='index' 
                    AND name = 'UX_SessionInscriptions_Session_Employe';";
                var indexCount = Convert.ToInt32(await cmd.ExecuteScalarAsync());
                Assert.That(indexCount, Is.EqualTo(1), "L'index unique (SessionId, EmployeeId) doit exister.");

                cmd.CommandText = @"PRAGMA table_info(EntretienBesoinsFormation);";
                bool colonneExiste = false;
                using (var reader = await cmd.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                    {
                        var nomCol = reader["name"].ToString();
                        if (string.Equals(nomCol, "SessionFormationId", StringComparison.OrdinalIgnoreCase))
                        {
                            colonneExiste = true;
                            break;
                        }
                    }
                }
                Assert.That(colonneExiste, Is.True, "La colonne SessionFormationId doit exister dans EntretienBesoinsFormation.");
            }
        }

        [Test]
        [Microsoft.VisualStudio.TestTools.UnitTesting.TestMethod]
        public async Task PlansFormation_CRUD_FonctionneCorrectement()
        {
            var plan = new PlanFormation
            {
                Code = "PAF-2026",
                Titre = "Plan Annuel de Formation 2026",
                AnneeExercice = 2026,
                BudgetPrevisionnel = 15000000m, // 15 000 000 FCFA
                Statut = StatutPlanFormation.Elaboration,
                DateValidation = null,
                ObjectifsStrategiques = "Renforcer l'excellence opérationnelle et la cybersécurité"
            };

            await _formationService.EnregistrerPlanAsync(plan);
            Assert.That(plan.Id, Is.GreaterThan(0));

            var plans = await _formationService.ObtenirPlansAsync();
            Assert.That(plans.Count, Is.EqualTo(1));
            Assert.That(plans[0].AnneeExercice, Is.EqualTo(2026));
            Assert.That(plans[0].BudgetPrevisionnel, Is.EqualTo(15000000m));

            // Modification
            plan.Statut = StatutPlanFormation.Valide;
            plan.DateValidation = new DateTime(2026, 1, 15);
            await _formationService.EnregistrerPlanAsync(plan);

            var misAJour = await _formationService.ObtenirPlanParIdAsync(plan.Id);
            Assert.That(misAJour.Statut, Is.EqualTo(StatutPlanFormation.Valide));
            Assert.That(misAJour.DateValidation, Is.EqualTo(new DateTime(2026, 1, 15)));
        }

        [Test]
        [Microsoft.VisualStudio.TestTools.UnitTesting.TestMethod]
        public async Task OrganismesEtFormationsCatalogue_CRUD_AvecCompetenceLiee()
        {
            // 1. Organisme
            var organisme = new OrganismeFormation
            {
                RaisonSociale = "Cabinet Alpha Conseil & Formation",
                NumeroAgrement = "AGR-2024-089-METFP",
                ContactNom = "M. Soro",
                Telephone = "+226 25 30 00 00",
                Email = "contact@alphaconseil.bf",
                Adresse = "Ouagadougou, Secteur 15",
                Specialites = "Management, Comptabilité, RH",
                Actif = true
            };
            await _formationService.EnregistrerOrganismeAsync(organisme);
            Assert.That(organisme.Id, Is.GreaterThan(0));

            var organismes = await _formationService.ObtenirOrganismesAsync();
            Assert.That(organismes.Count, Is.GreaterThanOrEqualTo(1));
            Assert.That(organismes.Any(o => o.RaisonSociale == "Cabinet Alpha Conseil & Formation"), Is.True);

            // 2. Compétence pour test (RH-02)
            int competenceId = 0;
            using (var conn = _sqlite.CreerConnexion())
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "SELECT Id FROM Competences LIMIT 1;";
                var idObj = await cmd.ExecuteScalarAsync();
                if (idObj != null && idObj != DBNull.Value)
                {
                    competenceId = Convert.ToInt32(idObj);
                }
            }

            // 3. Formation Catalogue
            var formation = new FormationCatalogue
            {
                Code = "FORM-RH-01",
                Intitule = "Pratique du Droit du Travail et Paie",
                Domaine = "Ressources Humaines",
                CompetenceViseeId = competenceId > 0 ? (int?)competenceId : null,
                DureeHeures = 24,
                CoutEstimeParParticipant = 250000m, // 250 000 FCFA
                ObjectifsPedagogiques = "Maîtriser la gestion des contrats et le calcul des cotisations",
                Prerequis = "Notions de base en gestion RH",
                Actif = true
            };
            await _formationService.EnregistrerFormationCatalogueAsync(formation);
            Assert.That(formation.Id, Is.GreaterThan(0));

            var formations = await _formationService.ObtenirCatalogueFormationsAsync();
            Assert.That(formations.Count, Is.GreaterThanOrEqualTo(1));
            var formTrouvee = formations.FirstOrDefault(f => f.Code == "FORM-RH-01");
            Assert.That(formTrouvee, Is.Not.Null);
            Assert.That(formTrouvee.DureeHeures, Is.EqualTo(24));
        }

        [Test]
        [Microsoft.VisualStudio.TestTools.UnitTesting.TestMethod]
        public async Task SessionsFormation_CalculCoutTotalReel()
        {
            var plan = new PlanFormation
            {
                Code = "PAF-2026",
                Titre = "Plan 2026",
                AnneeExercice = 2026,
                BudgetPrevisionnel = 10000000m,
                Statut = StatutPlanFormation.Valide
            };
            await _formationService.EnregistrerPlanAsync(plan);

            var formation = new FormationCatalogue
            {
                Code = "FORM-SEC-01",
                Intitule = "Sécurité Incendie & Premiers Secours",
                Domaine = "Santé et Sécurité",
                DureeHeures = 14,
                CoutEstimeParParticipant = 150000m,
                Actif = true
            };
            await _formationService.EnregistrerFormationCatalogueAsync(formation);

            var session = new SessionFormation
            {
                PlanFormationId = plan.Id,
                FormationId = formation.Id,
                CodeSession = "SESS-2026-01",
                IntituleSession = "Session Sécurité Février",
                DateDebut = new DateTime(2026, 4, 10),
                DateFin = new DateTime(2026, 4, 12),
                Lieu = "Salle de formation Siège",
                CoutPedagogique = 300000m,  // 300 000 FCFA
                CoutLogistique = 150000m,   // 150 000 FCFA
                CoutAutre = 20000m,         // 20 000 FCFA
                Statut = StatutSessionFormation.Planifiee
            };

            await _formationService.EnregistrerSessionAsync(session);
            Assert.That(session.Id, Is.GreaterThan(0));
            Assert.That(session.CoutTotalReel, Is.EqualTo(470000m)); // 300k + 150k + 20k = 470k FCFA

            var sessions = await _formationService.ObtenirSessionsAsync(plan.Id);
            Assert.That(sessions.Count, Is.EqualTo(1));
            Assert.That(sessions[0].CoutTotalReel, Is.EqualTo(470000m));
            Assert.That(sessions[0].FormationIntitule, Is.EqualTo("Sécurité Incendie & Premiers Secours"));
        }

        [Test]
        [Microsoft.VisualStudio.TestTools.UnitTesting.TestMethod]
        public async Task Inscriptions_AntiDoublon_EtAttestation()
        {
            var plan = new PlanFormation { Code = "PAF-EXC", Titre = "Plan 2026", AnneeExercice = 2026, BudgetPrevisionnel = 5000000m, Statut = StatutPlanFormation.Valide };
            await _formationService.EnregistrerPlanAsync(plan);

            var formation = new FormationCatalogue { Code = "FORM-EXC", Intitule = "Excel Avancé", Domaine = "Bureautique", DureeHeures = 16, Actif = true };
            await _formationService.EnregistrerFormationCatalogueAsync(formation);

            var session = new SessionFormation
            {
                PlanFormationId = plan.Id,
                FormationId = formation.Id,
                CodeSession = "SESS-EXC-01",
                DateDebut = new DateTime(2026, 5, 2),
                DateFin = new DateTime(2026, 5, 4),
                Statut = StatutSessionFormation.Realisee
            };
            await _formationService.EnregistrerSessionAsync(session);

            // Inscription 1
            int inscId = await _formationService.InscrireParticipantAsync(session.Id, "EMP-001", 50000m, "Inscription initiale");
            Assert.That(inscId, Is.GreaterThan(0));

            // Tentative de double inscription pour le même employé
            var ex = Assert.ThrowsAsync<InvalidOperationException>(async () =>
            {
                await _formationService.InscrireParticipantAsync(session.Id, "EMP-001");
            });
            Assert.That(ex.Message, Does.Contain("déjà inscrit"));

            // Pointage présence et délivrance attestation
            await _formationService.PointerPresenceAsync(inscId, StatutPresenceSession.Present, true, "16.5/20");

            var inscriptions = await _formationService.ObtenirInscriptionsSessionAsync(session.Id);
            Assert.That(inscriptions.Count, Is.EqualTo(1));
            Assert.That(inscriptions[0].StatutPresence, Is.EqualTo(StatutPresenceSession.Present));
            Assert.That(inscriptions[0].AttestationObtenue, Is.True);
            Assert.That(inscriptions[0].NoteEvaluation, Is.EqualTo("16.5/20"));
        }

        [Test]
        [Microsoft.VisualStudio.TestTools.UnitTesting.TestMethod]
        public async Task VerrouProbatoire_InterditSuppressionSiAttestationDelivree()
        {
            var plan = new PlanFormation { Code = "PAF-PROB", Titre = "Plan 2026", AnneeExercice = 2026, BudgetPrevisionnel = 5000000m, Statut = StatutPlanFormation.Valide };
            await _formationService.EnregistrerPlanAsync(plan);

            var formation = new FormationCatalogue { Code = "FORM-PROB", Intitule = "Audit Interne", Domaine = "Audit", DureeHeures = 20, Actif = true };
            await _formationService.EnregistrerFormationCatalogueAsync(formation);

            var session = new SessionFormation
            {
                PlanFormationId = plan.Id,
                FormationId = formation.Id,
                CodeSession = "SESS-AUDIT-01",
                DateDebut = new DateTime(2026, 6, 1),
                DateFin = new DateTime(2026, 6, 3),
                Statut = StatutSessionFormation.Realisee
            };
            await _formationService.EnregistrerSessionAsync(session);

            int inscId = await _formationService.InscrireParticipantAsync(session.Id, "EMP-002");
            await _formationService.PointerPresenceAsync(inscId, StatutPresenceSession.Present, true, "18/20");

            // 1. Tentative suppression session avec attestation -> refusé
            var exSession = Assert.ThrowsAsync<InvalidOperationException>(async () =>
            {
                await _formationService.SupprimerSessionFormationAsync(session.Id);
            });
            Assert.That(exSession.Message, Does.Contain("attestation"));

            // 2. Tentative suppression plan parent -> refusé
            var exPlan = Assert.ThrowsAsync<InvalidOperationException>(async () =>
            {
                await _formationService.SupprimerPlanFormationAsync(plan.Id);
            });
            Assert.That(exPlan.Message, Does.Contain("attestation"));
        }

        [Test]
        [Microsoft.VisualStudio.TestTools.UnitTesting.TestMethod]
        public async Task CascadeCS_SuppressionPlanSansAttestationNettoieSessionsEtInscriptions()
        {
            var plan = new PlanFormation { Code = "PAF-CANC", Titre = "Plan Annulé", AnneeExercice = 2026, BudgetPrevisionnel = 2000000m, Statut = StatutPlanFormation.Elaboration };
            await _formationService.EnregistrerPlanAsync(plan);

            var formation = new FormationCatalogue { Code = "FORM-CANC", Intitule = "Formation Test Cascade", Domaine = "Test", DureeHeures = 8, Actif = true };
            await _formationService.EnregistrerFormationCatalogueAsync(formation);

            var session = new SessionFormation
            {
                PlanFormationId = plan.Id,
                FormationId = formation.Id,
                CodeSession = "SESS-CANC-01",
                DateDebut = new DateTime(2026, 7, 1),
                DateFin = new DateTime(2026, 7, 1),
                Statut = StatutSessionFormation.Annulee
            };
            await _formationService.EnregistrerSessionAsync(session);

            int inscId = await _formationService.InscrireParticipantAsync(session.Id, "EMP-003");

            // Suppression du plan
            await _formationService.SupprimerPlanFormationAsync(plan.Id);

            // Vérification en base que tout a disparu
            using (var conn = _sqlite.CreerConnexion())
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "SELECT count(*) FROM PlansFormation WHERE Id = @id;";
                cmd.Parameters.AjouterValeur("@id", plan.Id);
                var nbPlans = Convert.ToInt32(await cmd.ExecuteScalarAsync());
                Assert.That(nbPlans, Is.EqualTo(0));

                cmd.CommandText = "SELECT count(*) FROM SessionsFormation WHERE Id = @sid;";
                cmd.Parameters.Clear();
                cmd.Parameters.AjouterValeur("@sid", session.Id);
                var nbSessions = Convert.ToInt32(await cmd.ExecuteScalarAsync());
                Assert.That(nbSessions, Is.EqualTo(0), "La session doit être supprimée en cascade C#.");

                cmd.CommandText = "SELECT count(*) FROM SessionInscriptions WHERE Id = @iid;";
                cmd.Parameters.Clear();
                cmd.Parameters.AjouterValeur("@iid", inscId);
                var nbIns = Convert.ToInt32(await cmd.ExecuteScalarAsync());
                Assert.That(nbIns, Is.EqualTo(0), "L'inscription doit être supprimée en cascade C#.");
            }
        }

        [Test]
        [Microsoft.VisualStudio.TestTools.UnitTesting.TestMethod]
        public async Task PasserelleRH03_AssignationBesoinSessionEtDetachement()
        {
            // 1. Créer une campagne RH-03
            var campagne = new CampagneEvaluation
            {
                Code = "CAMP-RH03",
                Titre = "Campagne RH-03",
                AnneeExercice = 2026,
                DateDebut = new DateTime(2026, 1, 1),
                DateFin = new DateTime(2026, 3, 31),
                Statut = StatutCampagne.EnCours
            };
            await _evaluationService.EnregistrerCampagneAsync(campagne);

            // Initialiser les entretiens de la campagne pour les salariés actifs
            await _evaluationService.InitialiserEntretiensCampagneAsync(campagne.Id);
            var entretiens = await _evaluationService.ObtenirEntretiensCampagneAsync(campagne.Id);
            Assert.That(entretiens.Count, Is.GreaterThan(0));
            var entretien = entretiens.First();

            // Insérer un besoin de formation RH-03 (Brouillon/Demande)
            int besoinId = 0;
            using (var conn = _sqlite.CreerConnexion())
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = @"
                    INSERT INTO EntretienBesoinsFormation 
                    (EntretienId, CompetenceId, ThemeFormation, Priorite, Statut)
                    VALUES (@eid, NULL, 'Besoin de formation en IFRS', 1, 0);
                    SELECT last_insert_rowid();";
                cmd.Parameters.AjouterValeur("@eid", entretien.Id);
                besoinId = Convert.ToInt32(await cmd.ExecuteScalarAsync());
            }
            Assert.That(besoinId, Is.GreaterThan(0));

            // 2. Créer session RH-04
            var plan = new PlanFormation { Code = "PAF-IFRS", Titre = "Plan IFRS", AnneeExercice = 2026, BudgetPrevisionnel = 3000000m, Statut = StatutPlanFormation.Valide };
            await _formationService.EnregistrerPlanAsync(plan);

            var formation = new FormationCatalogue { Code = "FORM-IFRS", Intitule = "Normes IFRS Pratiques", Domaine = "Finance", DureeHeures = 30, Actif = true };
            await _formationService.EnregistrerFormationCatalogueAsync(formation);

            var session = new SessionFormation
            {
                PlanFormationId = plan.Id,
                FormationId = formation.Id,
                CodeSession = "SESS-IFRS-01",
                DateDebut = new DateTime(2026, 8, 1),
                DateFin = new DateTime(2026, 8, 5),
                Statut = StatutSessionFormation.Planifiee
            };
            await _formationService.EnregistrerSessionAsync(session);

            // 3. Assigner le besoin à la session -> Doit passer en Statut = 1 (Retenu) et lier SessionFormationId
            await _formationService.AssignerBesoinASessionAsync(besoinId, session.Id);

            using (var conn = _sqlite.CreerConnexion())
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "SELECT Statut, SessionFormationId FROM EntretienBesoinsFormation WHERE Id = @id;";
                cmd.Parameters.AjouterValeur("@id", besoinId);
                using (var reader = await cmd.ExecuteReaderAsync())
                {
                    Assert.That(await reader.ReadAsync(), Is.True);
                    Assert.That(Convert.ToInt32(reader["Statut"]), Is.EqualTo((int)StatutBesoinFormation.Retenu));
                    Assert.That(Convert.ToInt32(reader["SessionFormationId"]), Is.EqualTo(session.Id));
                }
            }

            // 4. Supprimer la session -> La cascade C# doit détacher le besoin (SessionFormationId = NULL) sans supprimer la ligne du besoin
            await _formationService.SupprimerSessionFormationAsync(session.Id);

            using (var conn = _sqlite.CreerConnexion())
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "SELECT SessionFormationId FROM EntretienBesoinsFormation WHERE Id = @id;";
                cmd.Parameters.AjouterValeur("@id", besoinId);
                var sidObj = await cmd.ExecuteScalarAsync();
                Assert.That(sidObj == null || sidObj == DBNull.Value, Is.True, "Le besoin de formation doit être détaché et non supprimé.");
            }
        }

        [Test]
        [Microsoft.VisualStudio.TestTools.UnitTesting.TestMethod]
        public async Task TableauBordTPA_CalculCorrectAvecFiltresEtPlafond()
        {
            // Insérer des calculs de taxes fictifs pour 2026
            using (var conn = _sqlite.CreerConnexion())
            using (var cmd = conn.CreateCommand())
            {
                // Ligne 1 : bulletin normal janvier (AssietteTPA = 1 000 000 FCFA, TaxePatronale = 35 000 FCFA en centimes)
                cmd.CommandText = @"
                    INSERT INTO TaxCalculations 
                    (Id, EmployeeId, CalculationDate, SalaireBrut, AssietteTPA, TaxePatronale, IsAnnual, IsRegularization)
                    VALUES ('TAX-01', 'EMP-001', '2026-01-31', 100000000, 100000000, 3500000, 0, 0);";
                await cmd.ExecuteNonQueryAsync();

                // Ligne 2 : bulletin normal février
                cmd.CommandText = @"
                    INSERT INTO TaxCalculations 
                    (Id, EmployeeId, CalculationDate, SalaireBrut, AssietteTPA, TaxePatronale, IsAnnual, IsRegularization)
                    VALUES ('TAX-02', 'EMP-001', '2026-02-28', 100000000, 100000000, 3500000, 0, 0);";
                await cmd.ExecuteNonQueryAsync();

                // Ligne 3 : régularisation (IsRegularization = 1 -> doit être IGNORÉE)
                cmd.CommandText = @"
                    INSERT INTO TaxCalculations 
                    (Id, EmployeeId, CalculationDate, SalaireBrut, AssietteTPA, TaxePatronale, IsAnnual, IsRegularization)
                    VALUES ('TAX-03', 'EMP-001', '2026-02-28', 50000000, 50000000, 1750000, 0, 1);";
                await cmd.ExecuteNonQueryAsync();

                // Ligne 4 : récapitulatif annuel (IsAnnual = 1 -> doit être IGNORÉE)
                cmd.CommandText = @"
                    INSERT INTO TaxCalculations 
                    (Id, EmployeeId, CalculationDate, SalaireBrut, AssietteTPA, TaxePatronale, IsAnnual, IsRegularization)
                    VALUES ('TAX-04', 'EMP-001', '2026-12-31', 1200000000, 1200000000, 42000000, 1, 0);";
                await cmd.ExecuteNonQueryAsync();
            }

            // Créer un plan avec une session réalisée et coût réel
            var plan = new PlanFormation { Code = "PAF-TPA", Titre = "Plan TPA", AnneeExercice = 2026, BudgetPrevisionnel = 1000000m, Statut = StatutPlanFormation.Valide };
            await _formationService.EnregistrerPlanAsync(plan);

            var formation = new FormationCatalogue { Code = "FORM-TPA", Intitule = "Formation TPA", Domaine = "Fiscalité", DureeHeures = 8, Actif = true };
            await _formationService.EnregistrerFormationCatalogueAsync(formation);

            var session = new SessionFormation
            {
                PlanFormationId = plan.Id,
                FormationId = formation.Id,
                CodeSession = "SESS-TPA-01",
                DateDebut = new DateTime(2026, 3, 1),
                DateFin = new DateTime(2026, 3, 1),
                CoutPedagogique = 20000m, // 20 000 FCFA
                Statut = StatutSessionFormation.Realisee
            };
            await _formationService.EnregistrerSessionAsync(session);

            var tb = await _formationService.ObtenirTableauBordTpaAsync(2026);
            Assert.That(tb, Is.Not.Null);
            Assert.That(tb.AnneeExercice, Is.EqualTo(2026));
            // Assiette cumulée normale = 1M + 1M = 2 000 000 FCFA
            Assert.That(tb.MasseSalarialeSoumiseTpa, Is.EqualTo(2000000m));
            // TPA théorique = 35 000 + 35 000 = 70 000 FCFA
            Assert.That(tb.TpaTheoriqueDue, Is.EqualTo(70000m));
            // Dépenses de formation = 20 000 FCFA
            Assert.That(tb.TotalDepensesFormation, Is.EqualTo(20000m));
            // Taux de couverture TPA = (20 000 / 70 000) * 100 = 28.6%
            Assert.That(tb.TauxCouvertureTpaPct, Is.EqualTo(28.6));
        }

        [Test]
        [Microsoft.VisualStudio.TestTools.UnitTesting.TestMethod]
        public async Task IndicateursPlanEtTPA_SessionAvecPlusieursPresents_SommesNonMultiplieesParLesPresents()
        {
            // Plan A : budget 1 000 000 FCFA
            var planA = new PlanFormation { Code = "PAF-MULTI", Titre = "Plan multi-présents", AnneeExercice = 2026, BudgetPrevisionnel = 1000000m, Statut = StatutPlanFormation.Valide };
            await _formationService.EnregistrerPlanAsync(planA);

            // Plan B (même exercice) : budget 500 000 FCFA
            var planB = new PlanFormation { Code = "PAF-MULTI-B", Titre = "Plan B", AnneeExercice = 2026, BudgetPrevisionnel = 500000m, Statut = StatutPlanFormation.Valide };
            await _formationService.EnregistrerPlanAsync(planB);

            var form14 = new FormationCatalogue { Code = "FORM-M14", Intitule = "Sécurité 14 h", Domaine = "HSE", DureeHeures = 14, Actif = true };
            await _formationService.EnregistrerFormationCatalogueAsync(form14);
            var form8 = new FormationCatalogue { Code = "FORM-M08", Intitule = "Excel 8 h", Domaine = "Bureautique", DureeHeures = 8, Actif = true };
            await _formationService.EnregistrerFormationCatalogueAsync(form8);

            // Session 1 (plan A) : 470 000 FCFA, 14 h, 3 présents
            var s1 = new SessionFormation { PlanFormationId = planA.Id, FormationId = form14.Id, CodeSession = "SESS-M-01", DateDebut = new DateTime(2026, 4, 1), DateFin = new DateTime(2026, 4, 2), CoutPedagogique = 300000m, CoutLogistique = 150000m, CoutAutre = 20000m, Statut = StatutSessionFormation.Realisee };
            await _formationService.EnregistrerSessionAsync(s1);
            foreach (var matricule in new[] { "EMP-001", "EMP-002", "EMP-003" })
            {
                int insc = await _formationService.InscrireParticipantAsync(s1.Id, matricule);
                await _formationService.PointerPresenceAsync(insc, StatutPresenceSession.Present, true);
            }

            // Session 2 (plan A) : 100 000 FCFA, 8 h, aucun présent (un inscrit absent)
            var s2 = new SessionFormation { PlanFormationId = planA.Id, FormationId = form8.Id, CodeSession = "SESS-M-02", DateDebut = new DateTime(2026, 5, 1), DateFin = new DateTime(2026, 5, 1), CoutPedagogique = 100000m, Statut = StatutSessionFormation.Planifiee };
            await _formationService.EnregistrerSessionAsync(s2);
            int inscAbsent = await _formationService.InscrireParticipantAsync(s2.Id, "EMP-001");
            await _formationService.PointerPresenceAsync(inscAbsent, StatutPresenceSession.AbsentJustifie, false);

            // Session 3 (plan A) : annulée -> exclue, même avec un présent
            var s3 = new SessionFormation { PlanFormationId = planA.Id, FormationId = form8.Id, CodeSession = "SESS-M-03", DateDebut = new DateTime(2026, 6, 1), DateFin = new DateTime(2026, 6, 1), CoutPedagogique = 50000m, Statut = StatutSessionFormation.Annulee };
            await _formationService.EnregistrerSessionAsync(s3);
            int inscAnnulee = await _formationService.InscrireParticipantAsync(s3.Id, "EMP-002");
            await _formationService.PointerPresenceAsync(inscAnnulee, StatutPresenceSession.Present, false);

            // Session 4 (plan B) : 200 000 FCFA, 8 h, 2 présents
            var s4 = new SessionFormation { PlanFormationId = planB.Id, FormationId = form8.Id, CodeSession = "SESS-M-04", DateDebut = new DateTime(2026, 7, 1), DateFin = new DateTime(2026, 7, 1), CoutPedagogique = 200000m, Statut = StatutSessionFormation.Realisee };
            await _formationService.EnregistrerSessionAsync(s4);
            foreach (var matricule in new[] { "EMP-001", "EMP-003" })
            {
                int insc = await _formationService.InscrireParticipantAsync(s4.Id, matricule);
                await _formationService.PointerPresenceAsync(insc, StatutPresenceSession.Present, true);
            }

            // 1. Indicateurs du plan A (synthèse d'exécution, solde budgétaire, export Excel)
            var a = await _formationService.ObtenirPlanParIdAsync(planA.Id);
            Assert.That(a.NombreSessions, Is.EqualTo(2), "La session annulée n'est pas rattachée au plan.");
            Assert.That(a.DepensesReelles, Is.EqualTo(570000m), "470 000 + 100 000 : chaque session comptée une seule fois.");
            Assert.That(a.TotalHeures, Is.EqualTo(22), "14 h + 8 h : la durée n'est pas multipliée par le nombre de présents.");
            Assert.That(a.NombreSalariesFormes, Is.EqualTo(3));
            Assert.That(a.EcartBudget, Is.EqualTo(430000m), "Solde = 1 000 000 - 570 000.");
            Assert.That(a.TauxConsommationPct, Is.EqualTo(57.0));

            // 2. Tableau de bord TPA filtré sur le plan A
            var tbA = await _formationService.ObtenirTableauBordTpaAsync(2026, planA.Id);
            Assert.That(tbA.BudgetPlanFormation, Is.EqualTo(1000000m), "Le budget n'est pas multiplié par le nombre de lignes jointes.");
            Assert.That(tbA.TotalDepensesFormation, Is.EqualTo(570000m));
            Assert.That(tbA.NombreHeuresTotal, Is.EqualTo(22));
            Assert.That(tbA.NombreSalariesFormes, Is.EqualTo(3));
            Assert.That(tbA.NombreSessionsRealisees, Is.EqualTo(1));

            // 3. Tableau de bord TPA de l'exercice (tous plans)
            var tbAnnee = await _formationService.ObtenirTableauBordTpaAsync(2026);
            Assert.That(tbAnnee.BudgetPlanFormation, Is.EqualTo(1500000m));
            Assert.That(tbAnnee.TotalDepensesFormation, Is.EqualTo(770000m));
            Assert.That(tbAnnee.NombreHeuresTotal, Is.EqualTo(30));
            Assert.That(tbAnnee.NombreSalariesFormes, Is.EqualTo(3), "Salariés distincts présents sur l'exercice.");
            Assert.That(tbAnnee.NombreSessionsRealisees, Is.EqualTo(2));
        }

        [Test]
        [Microsoft.VisualStudio.TestTools.UnitTesting.TestMethod]
        public void ExportAttestation_GenereFichierPdfValide()
        {
            var ins = new SessionInscription
            {
                EmployeeNomComplet = "OUEDRAOGO Jean-Baptiste",
                EmployeeMatricule = "EMP-0042",
                AttestationObtenue = true,
                StatutPresence = StatutPresenceSession.Present,
                NoteEvaluation = "17.5/20"
            };

            var session = new SessionFormation
            {
                CodeSession = "SESS-SEC-01",
                FormationIntitule = "Audit des Systèmes d'Information et Cybersécurité",
                DateDebut = new DateTime(2026, 6, 20),
                DateFin = new DateTime(2026, 6, 30),
                Lieu = "Ouagadougou, Siège Social",
                DureeHeures = 40,
                FormateurNom = "Dr. Zoungrana"
            };

            var dossier = new Dossier
            {
                RaisonSociale = "Sigma Technologies SARL",
                IFU = "320260012345A",
                RCCM = "BF-OUA-2024-B-1234",
                Ville = "Ouagadougou"
            };

            string tempPdf = Path.Combine(Path.GetTempPath(), $"attestation_test_{Guid.NewGuid():N}.pdf");
            try
            {
                bool succes = _exportService.GenererAttestationFormationPdf(session, ins, dossier, tempPdf);
                Assert.That(succes, Is.True);
                Assert.That(File.Exists(tempPdf), Is.True);

                var bytes = File.ReadAllBytes(tempPdf);
                Assert.That(bytes.Length, Is.GreaterThan(1000));

                // Vérification entête %PDF
                var header = System.Text.Encoding.ASCII.GetString(bytes.Take(4).ToArray());
                Assert.That(header, Is.EqualTo("%PDF"));
            }
            finally
            {
                if (File.Exists(tempPdf))
                {
                    try { File.Delete(tempPdf); } catch { }
                }
            }
        }
    }
}
