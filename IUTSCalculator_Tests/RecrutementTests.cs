using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using IUTSCalculator.Models;
using IUTSCalculator.Services;
using ClosedXML.Excel;

namespace IUTSCalculator.Tests
{
    [TestFixture]
    [Microsoft.VisualStudio.TestTools.UnitTesting.TestClass]
    public class RecrutementTests
    {
        private string _tempDb;
        private SQLiteService _sqlite;
        private DataService _dataService;
        private OrganigrammeService _organigrammeService;
        private RecrutementService _recrutementService;
        private RecrutementExportService _exportService;

        [SetUp]
        [Microsoft.VisualStudio.TestTools.UnitTesting.TestInitialize]
        public void Setup()
        {
            _tempDb = Path.Combine(Path.GetTempPath(), $"test_recrut_{Guid.NewGuid():N}.db");
            _sqlite = new SQLiteService(_tempDb);
            _dataService = new DataService(_tempDb);
            _organigrammeService = new OrganigrammeService(_sqlite);
            _recrutementService = new RecrutementService(_sqlite, _dataService);
            _exportService = new RecrutementExportService();

            // Créer une direction et un poste dans l'organigramme pour les tests d'affectation
            using (var conn = _sqlite.CreateConnection())
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = @"
                    INSERT OR IGNORE INTO UnitesOrganisationnelles (Id, Code, Libelle, TypeUnite, DateCreation)
                    VALUES (1, 'DIR-RH', 'Direction des Ressources Humaines', 0, date('now'));

                    INSERT OR IGNORE INTO Postes (Id, Code, Intitule, UniteId, Classification, Missions, DateCreation)
                    VALUES (1, 'PST-PAIE', 'Responsable Paie & Administration', 1, 'Cadre', 'Gestion de la paie et déclarations sociales', date('now'));";
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
                // Ignoré lors du nettoyage
            }
        }

        [Test]
        [Microsoft.VisualStudio.TestTools.UnitTesting.TestMethod]
        public async Task Test01_SchemaMigration33_TablesEtIndexCrees_AmorcageInitialPresent()
        {
            // Vérifier que les tables RH-05 existent et que l'amorçage initial est présent
            var besoins = await _recrutementService.ObtenirBesoinsAsync();
            var vivier = await _recrutementService.ObtenirCandidatsAsync();

            Assert.That(besoins, Is.Not.Null);
            Assert.That(besoins.Count, Is.GreaterThanOrEqualTo(2), "L'amorçage des besoins de recrutement doit comporter au moins 2 besoins.");

            Assert.That(vivier, Is.Not.Null);
            Assert.That(vivier.Count, Is.GreaterThanOrEqualTo(2), "L'amorçage du vivier de candidats doit comporter au moins 2 profils.");

            var premierBesoin = besoins.FirstOrDefault(b => b.Reference == "REC-2026-001");
            Assert.That(premierBesoin, Is.Not.Null);
            Assert.That(premierBesoin.IntitulePoste, Is.EqualTo("Gestionnaire Paie & Administration RH"));
            Assert.That(premierBesoin.Statut, Is.EqualTo(StatutBesoinRecrutement.Ouvert));
        }

        [Test]
        [Microsoft.VisualStudio.TestTools.UnitTesting.TestMethod]
        public async Task Test02_BesoinRecrutement_CrudEtTransitionStatut()
        {
            string refGen = await _recrutementService.GenererReferenceBesoinAsync();
            Assert.That(refGen, Does.StartWith("REC-"));

            var nouveauBesoin = new BesoinRecrutement
            {
                Reference = refGen,
                IntitulePoste = "Ingénieur DevOps & Cloud",
                Departement = "Systèmes d'Information",
                Motif = MotifRecrutement.CreationPoste,
                NombrePostes = 2,
                TypeContratVise = "CDI",
                RemunerationProposeeMin = 500000m,
                RemunerationProposeeMax = 700000m,
                DateExpression = DateTime.Today,
                DateEcheanceSouhaitee = DateTime.Today.AddDays(45),
                Statut = StatutBesoinRecrutement.Brouillon,
                ResponsableRecrutement = "DRH",
                DescriptionProfil = "Expertise Docker, Kubernetes, CI/CD et sécurité cloud."
            };

            int id = await _recrutementService.EnregistrerBesoinAsync(nouveauBesoin);
            Assert.That(id, Is.GreaterThan(0));

            var relecture = await _recrutementService.ObtenirBesoinParIdAsync(id);
            Assert.That(relecture, Is.Not.Null);
            Assert.That(relecture.IntitulePoste, Is.EqualTo("Ingénieur DevOps & Cloud"));
            Assert.That(relecture.RemunerationProposeeMin, Is.EqualTo(500000m));
            Assert.That(relecture.PostesRestants, Is.EqualTo(2));

            // Transition de statut vers Ouvert
            await _recrutementService.ChangerStatutBesoinAsync(id, StatutBesoinRecrutement.Ouvert);
            var maj = await _recrutementService.ObtenirBesoinParIdAsync(id);
            Assert.That(maj.Statut, Is.EqualTo(StatutBesoinRecrutement.Ouvert));

            // Suppression sans embauche autorisée
            await _recrutementService.SupprimerBesoinAsync(id);
            var supprime = await _recrutementService.ObtenirBesoinParIdAsync(id);
            Assert.That(supprime, Is.Null);
        }

        [Test]
        [Microsoft.VisualStudio.TestTools.UnitTesting.TestMethod]
        public async Task Test03_VivierCandidats_CrudEtReactivation()
        {
            var candidat = new Candidat
            {
                Nom = "ZONGO",
                Prenoms = "Wendkouni David",
                Email = "david.zongo@test.bf",
                Telephone = "+226 78 00 11 22",
                DiplomeLePlusEleve = "Master Informatique Réseaux",
                AnneesExperience = 6,
                DerniereFonction = "Administrateur Systèmes",
                CompetencesCles = "Linux, Sécurité, Scripting Bash, Python",
                VivierActif = true,
                Commentaires = "Très bon profil technique."
            };

            int id = await _recrutementService.EnregistrerCandidatAsync(candidat);
            Assert.That(id, Is.GreaterThan(0));

            var lu = await _recrutementService.ObtenirCandidatParIdAsync(id);
            Assert.That(lu, Is.Not.Null);
            Assert.That(lu.NomComplet, Is.EqualTo("ZONGO Wendkouni David"));
            Assert.That(lu.VivierActif, Is.True);

            // Désactivation / archivage dans le vivier
            lu.VivierActif = false;
            await _recrutementService.EnregistrerCandidatAsync(lu);

            var archive = await _recrutementService.ObtenirCandidatParIdAsync(id);
            Assert.That(archive.VivierActif, Is.False);
            Assert.That(archive.VivierActifLibelle, Is.EqualTo("Archivé"));

            // Suppression autorisée car sans embauche
            await _recrutementService.SupprimerCandidatAsync(id);
            var apresSuppr = await _recrutementService.ObtenirCandidatParIdAsync(id);
            Assert.That(apresSuppr, Is.Null);
        }

        [Test]
        [Microsoft.VisualStudio.TestTools.UnitTesting.TestMethod]
        public async Task Test04_Candidature_PostulationEtAvancementEtapes()
        {
            var besoins = await _recrutementService.ObtenirBesoinsAsync();
            var vivier = await _recrutementService.ObtenirCandidatsAsync();
            int besoinId = besoins[0].Id;
            int candidatId = vivier[0].Id;

            int candidatureId = await _recrutementService.PostulerAsync(
                besoinId,
                candidatId,
                CanalSourceCandidature.ReseauxSociaux,
                pretentionSalariale: 420000m);

            Assert.That(candidatureId, Is.GreaterThan(0));

            var cand = await _recrutementService.ObtenirCandidatureParIdAsync(candidatureId);
            Assert.That(cand, Is.Not.Null);
            Assert.That(cand.PretentionSalariale, Is.EqualTo(420000m));
            Assert.That(cand.EtapeActuelle, Is.EqualTo(EtapeRecrutement.ReceptionCandidature));
            Assert.That(cand.Statut, Is.EqualTo(StatutCandidature.Nouvelle));

            // Avancer à l'étape Tri
            await _recrutementService.AvancerEtapeCandidatureAsync(candidatureId, EtapeRecrutement.TriEtPreselection);
            cand = await _recrutementService.ObtenirCandidatureParIdAsync(candidatureId);
            Assert.That(cand.EtapeActuelle, Is.EqualTo(EtapeRecrutement.TriEtPreselection));
            Assert.That(cand.Statut, Is.EqualTo(StatutCandidature.EnEvaluation));

            // Avancer à l'étape Entretien RH
            await _recrutementService.AvancerEtapeCandidatureAsync(candidatureId, EtapeRecrutement.EntretienRH);
            cand = await _recrutementService.ObtenirCandidatureParIdAsync(candidatureId);
            Assert.That(cand.EtapeActuelle, Is.EqualTo(EtapeRecrutement.EntretienRH));

            // Retenir la candidature
            await _recrutementService.ChangerStatutCandidatureAsync(candidatureId, StatutCandidature.Retenue, "Profil parfaitement aligné");
            cand = await _recrutementService.ObtenirCandidatureParIdAsync(candidatureId);
            Assert.That(cand.Statut, Is.EqualTo(StatutCandidature.Retenue));
            Assert.That(cand.CommentaireEvaluation, Does.Contain("Profil parfaitement aligné"));
        }

        [Test]
        [Microsoft.VisualStudio.TestTools.UnitTesting.TestMethod]
        public async Task Test05_Entretiens_AjoutNotationEtRecalculScoreCandidature()
        {
            var besoins = await _recrutementService.ObtenirBesoinsAsync();
            var vivier = await _recrutementService.ObtenirCandidatsAsync();
            int besoinId = besoins[0].Id;
            int candidatId = vivier[0].Id;

            int candId = await _recrutementService.PostulerAsync(besoinId, candidatId, CanalSourceCandidature.SiteWeb);

            // Premier entretien RH : Note 14/20
            var e1 = new RecrutementEntretien
            {
                CandidatureId = candId,
                Etape = EtapeRecrutement.EntretienRH,
                DateEntretien = DateTime.Today.AddDays(-2),
                EvaluateurNom = "Responsable RH",
                NoteGlobale = 14.0,
                Avis = AvisRecruteur.Favorable,
                PointsForts = "Sens de l'écoute, rigueur.",
                Synthese = "Premier filtre concluant."
            };
            await _recrutementService.EnregistrerEntretienAsync(e1);

            // Deuxième entretien Manager : Note 16/20
            var e2 = new RecrutementEntretien
            {
                CandidatureId = candId,
                Etape = EtapeRecrutement.EntretienManagerN1,
                DateEntretien = DateTime.Today,
                EvaluateurNom = "Directeur de Département",
                NoteGlobale = 16.0,
                Avis = AvisRecruteur.TresFavorable,
                PointsForts = "Expertise technique indéniable.",
                Synthese = "Candidat retenu pour la phase finale."
            };
            await _recrutementService.EnregistrerEntretienAsync(e2);

            // Vérifier la liste des entretiens et la moyenne recalculée (14 + 16) / 2 = 15.0
            var listeEntretiens = await _recrutementService.ObtenirEntretiensAsync(candId);
            Assert.That(listeEntretiens.Count, Is.EqualTo(2));

            var cand = await _recrutementService.ObtenirCandidatureParIdAsync(candId);
            Assert.That(cand.ScoreEvaluation, Is.EqualTo(15.0).Within(0.01));
            Assert.That(cand.AvisGlobal, Is.EqualTo(AvisRecruteur.TresFavorable));
        }

        [Test]
        [Microsoft.VisualStudio.TestTools.UnitTesting.TestMethod]
        public async Task Test06_PasserelleEmbaucheSAL01_IntegrationAtomique_CreeSalarieEtAffectePoste()
        {
            // 1. Créer un besoin avec 1 poste rattaché au PosteId = 1
            var besoin = new BesoinRecrutement
            {
                Reference = "REC-EMB-01",
                IntitulePoste = "Responsable Paie & Administration",
                PosteId = 1,
                Departement = "Direction des Ressources Humaines",
                NombrePostes = 1,
                NombreRecrutes = 0,
                Statut = StatutBesoinRecrutement.Ouvert,
                DateExpression = DateTime.Today
            };
            int bId = await _recrutementService.EnregistrerBesoinAsync(besoin);

            // 2. Créer un candidat
            var cand = new Candidat
            {
                Nom = "OUEDRAOGO",
                Prenoms = "Fatoumata",
                Email = "fatou.ouedraogo@candidat.bf",
                Telephone = "+226 70 88 99 00",
                DiplomeLePlusEleve = "Master Audit & Contrôle de Gestion",
                AnneesExperience = 5
            };
            int cId = await _recrutementService.EnregistrerCandidatAsync(cand);

            // 3. Postuler
            int candidatureId = await _recrutementService.PostulerAsync(bId, cId, CanalSourceCandidature.CabinetRecrutement, 450000m);

            // 4. Intégrer directement en fiche salarié
            DateTime datePriseFonction = new DateTime(2026, 10, 1);
            decimal salaireNegocie = 480000m;

            var salarieCree = await _recrutementService.IntegrerCandidatEnSalarieAsync(
                candidatureId,
                salaireNegocie,
                datePriseFonction,
                typeContrat: "CDI",
                categorie: "Cadre Supérieur",
                service: "Direction des Ressources Humaines");

            // Vérifications SAL-01
            Assert.That(salarieCree, Is.Not.Null);
            Assert.That(salarieCree.EmployeeId, Is.Not.Null.And.Not.Empty);
            Guid guid;
            Assert.That(Guid.TryParse(salarieCree.EmployeeId, out guid), Is.False, "Le matricule n'est plus un GUID technique.");
            Assert.That(salarieCree.EmployeeId, Is.EqualTo(RecrutementService.MatriculeParDefaut), "Dossier sans salarié : premier matricule par défaut.");
            Assert.That(salarieCree.LastName, Is.EqualTo("OUEDRAOGO"));
            Assert.That(salarieCree.FirstName, Is.EqualTo("Fatoumata"));
            Assert.That(salarieCree.BaseSalary, Is.EqualTo(480000m));
            Assert.That(salarieCree.DateEntree, Is.EqualTo(datePriseFonction));
            Assert.That(salarieCree.Fonction, Is.EqualTo("Responsable Paie & Administration"));
            Assert.That(salarieCree.PosteId, Is.EqualTo(1));

            // Vérifier en base la persistance réelle du salarié
            var employeEnBase = await _dataService.GetEmployeeByIdAsync(salarieCree.EmployeeId);
            Assert.That(employeEnBase, Is.Not.Null);
            Assert.That(employeEnBase.BaseSalary, Is.EqualTo(480000m));

            // Vérifier que le poste dans Postes a son TitulaireMatricule mis à jour
            var postes = await _organigrammeService.ChargerPostesAsync();
            var posteMaj = postes.FirstOrDefault(p => p.Id == 1);
            Assert.That(posteMaj, Is.Not.Null);
            Assert.That(posteMaj.TitulaireMatricule, Is.EqualTo(salarieCree.EmployeeId));

            // Vérifier que la candidature est passée à Embauchee (4) et Finalisee (7)
            var candMaj = await _recrutementService.ObtenirCandidatureParIdAsync(candidatureId);
            Assert.That(candMaj.Statut, Is.EqualTo(StatutCandidature.Embauchee));
            Assert.That(candMaj.EtapeActuelle, Is.EqualTo(EtapeRecrutement.Finalisee));
            Assert.That(candMaj.SalarieCreeMatricule, Is.EqualTo(salarieCree.EmployeeId));
            Assert.That(candMaj.SalaireNegocie, Is.EqualTo(480000m));
            Assert.That(candMaj.DateEmbauche, Is.EqualTo(datePriseFonction));

            // Vérifier que le besoin est passé à Pourvu (1 poste / 1 recruté)
            var besoinMaj = await _recrutementService.ObtenirBesoinParIdAsync(bId);
            Assert.That(besoinMaj.NombreRecrutes, Is.EqualTo(1));
            Assert.That(besoinMaj.Statut, Is.EqualTo(StatutBesoinRecrutement.Pourvu));
            Assert.That(besoinMaj.PostesRestants, Is.EqualTo(0));
        }

        [Test]
        [Microsoft.VisualStudio.TestTools.UnitTesting.TestMethod]
        public async Task Test07_GardeFou_AntiSurRecrutement()
        {
            // 1. Besoin de 1 poste
            var besoin = new BesoinRecrutement
            {
                Reference = "REC-QUOTA-01",
                IntitulePoste = "Technicien Support",
                NombrePostes = 1,
                NombreRecrutes = 0,
                Statut = StatutBesoinRecrutement.Ouvert,
                DateExpression = DateTime.Today
            };
            int bId = await _recrutementService.EnregistrerBesoinAsync(besoin);

            // 2. Deux candidats distincts
            int c1Id = await _recrutementService.EnregistrerCandidatAsync(new Candidat { Nom = "A", Prenoms = "Candidat 1" });
            int c2Id = await _recrutementService.EnregistrerCandidatAsync(new Candidat { Nom = "B", Prenoms = "Candidat 2" });

            int cand1Id = await _recrutementService.PostulerAsync(bId, c1Id, CanalSourceCandidature.SiteWeb);
            int cand2Id = await _recrutementService.PostulerAsync(bId, c2Id, CanalSourceCandidature.SiteWeb);

            // Embaucher le premier candidat -> Succès
            await _recrutementService.IntegrerCandidatEnSalarieAsync(cand1Id, 250000m, DateTime.Today);

            // Tentative d'embaucher le second candidat -> Doit échouer par dépassement de quota
            var ex = Assert.ThrowsAsync<InvalidOperationException>(async () =>
            {
                await _recrutementService.IntegrerCandidatEnSalarieAsync(cand2Id, 250000m, DateTime.Today);
            });

            Assert.That(ex.Message, Does.Contain("Le nombre de postes à pourvoir pour ce besoin a déjà été atteint"));
        }

        [Test]
        [Microsoft.VisualStudio.TestTools.UnitTesting.TestMethod]
        public async Task Test08_VerrouProbatoire_InterdictionSuppressionBesoinSiEmbauche()
        {
            var besoin = new BesoinRecrutement
            {
                Reference = "REC-LOCK-01",
                IntitulePoste = "Contrôleur Interne",
                NombrePostes = 1,
                Statut = StatutBesoinRecrutement.Ouvert,
                DateExpression = DateTime.Today
            };
            int bId = await _recrutementService.EnregistrerBesoinAsync(besoin);
            int cId = await _recrutementService.EnregistrerCandidatAsync(new Candidat { Nom = "C", Prenoms = "Test" });
            int candId = await _recrutementService.PostulerAsync(bId, cId, CanalSourceCandidature.Cooptation);

            await _recrutementService.IntegrerCandidatEnSalarieAsync(candId, 300000m, DateTime.Today);

            // Tentative de supprimer le besoin ayant donné lieu à une embauche
            var ex = Assert.ThrowsAsync<InvalidOperationException>(async () =>
            {
                await _recrutementService.SupprimerBesoinAsync(bId);
            });

            Assert.That(ex.Message, Does.Contain("Suppression impossible : ce besoin de recrutement a donné lieu à une embauche effective"));
        }

        [Test]
        [Microsoft.VisualStudio.TestTools.UnitTesting.TestMethod]
        public async Task Test09_VerrouProbatoire_InterdictionSuppressionCandidatVivierSiEmbauche()
        {
            var besoin = new BesoinRecrutement
            {
                Reference = "REC-LOCK-02",
                IntitulePoste = "Juriste d'Entreprise",
                NombrePostes = 1,
                Statut = StatutBesoinRecrutement.Ouvert,
                DateExpression = DateTime.Today
            };
            int bId = await _recrutementService.EnregistrerBesoinAsync(besoin);
            int cId = await _recrutementService.EnregistrerCandidatAsync(new Candidat { Nom = "D", Prenoms = "Test" });
            int candId = await _recrutementService.PostulerAsync(bId, cId, CanalSourceCandidature.Cooptation);

            await _recrutementService.IntegrerCandidatEnSalarieAsync(candId, 350000m, DateTime.Today);

            // Tentative de supprimer le profil vivier embauché
            var ex = Assert.ThrowsAsync<InvalidOperationException>(async () =>
            {
                await _recrutementService.SupprimerCandidatAsync(cId);
            });

            Assert.That(ex.Message, Does.Contain("ce candidat a été recruté et intégré dans le personnel"));
        }

        [Test]
        [Microsoft.VisualStudio.TestTools.UnitTesting.TestMethod]
        public async Task Test10_VerrouProbatoire_InterdictionModificationCandidatureEmbauchee()
        {
            var besoin = new BesoinRecrutement
            {
                Reference = "REC-LOCK-03",
                IntitulePoste = "Chargé de Communication",
                NombrePostes = 1,
                Statut = StatutBesoinRecrutement.Ouvert,
                DateExpression = DateTime.Today
            };
            int bId = await _recrutementService.EnregistrerBesoinAsync(besoin);
            int cId = await _recrutementService.EnregistrerCandidatAsync(new Candidat { Nom = "E", Prenoms = "Test" });
            int candId = await _recrutementService.PostulerAsync(bId, cId, CanalSourceCandidature.SiteWeb);

            await _recrutementService.IntegrerCandidatEnSalarieAsync(candId, 320000m, DateTime.Today);

            // Tentative d'avancer l'étape post-embauche
            Assert.ThrowsAsync<InvalidOperationException>(async () =>
            {
                await _recrutementService.AvancerEtapeCandidatureAsync(candId, EtapeRecrutement.DecisionEtOffre);
            });

            // Tentative de changer le statut post-embauche
            Assert.ThrowsAsync<InvalidOperationException>(async () =>
            {
                await _recrutementService.ChangerStatutCandidatureAsync(candId, StatutCandidature.Rejetee);
            });

            // Tentative de supprimer la candidature post-embauche
            Assert.ThrowsAsync<InvalidOperationException>(async () =>
            {
                await _recrutementService.SupprimerCandidatureAsync(candId);
            });
        }

        [Test]
        [Microsoft.VisualStudio.TestTools.UnitTesting.TestMethod]
        public void Test12_ProposerMatriculeSuivant_RespecteLeSchemaDuDossier()
        {
            // Préfixe + numéro, largeur conservée ; GUID techniques et matricules hors schéma ignorés
            Assert.That(RecrutementService.ProposerMatriculeSuivant(new[] { "STI033", "STI035", "STI034", Guid.NewGuid().ToString(), "DIRECTION" }),
                Is.EqualTo("STI036"));
            Assert.That(RecrutementService.ProposerMatriculeSuivant(new[] { "A0098", "A0099" }), Is.EqualTo("A0100"));
            Assert.That(RecrutementService.ProposerMatriculeSuivant(new[] { "sti035", "STI036" }), Is.EqualTo("STI037"), "Préfixe comparé sans casse.");

            // Schéma majoritaire : 3 « STI » contre 1 « EMP- »
            Assert.That(RecrutementService.ProposerMatriculeSuivant(new[] { "EMP-0900", "STI001", "STI002", "STI003" }), Is.EqualTo("STI004"));

            // Matricules purement numériques
            Assert.That(RecrutementService.ProposerMatriculeSuivant(new[] { "0041", "0042" }), Is.EqualTo("0043"));

            // Aucun schéma reconnaissable
            Assert.That(RecrutementService.ProposerMatriculeSuivant(new string[0]), Is.EqualTo("EMP-0001"));
            Assert.That(RecrutementService.ProposerMatriculeSuivant(new[] { Guid.NewGuid().ToString() }), Is.EqualTo("EMP-0001"));
        }

        [Test]
        [Microsoft.VisualStudio.TestTools.UnitTesting.TestMethod]
        public async Task Test13_Embauche_RepriseContratConventionEchelonCategorieIndemnitesEtMatriculeDuDossier()
        {
            // Salariés existants au schéma « STI » + un ancien matricule technique GUID
            await _dataService.AddEmployeeAsync(new Employee { EmployeeId = "STI034", FirstName = "Awa", LastName = "SANOGO", StatutSalarie = 0 });
            await _dataService.AddEmployeeAsync(new Employee { EmployeeId = "STI035", FirstName = "Paul", LastName = "KABORE", StatutSalarie = 0 });
            await _dataService.AddEmployeeAsync(new Employee { EmployeeId = Guid.NewGuid().ToString(), FirstName = "Ancien", LastName = "GUID", StatutSalarie = 0 });

            Assert.That(await _recrutementService.ProposerProchainMatriculeAsync(), Is.EqualTo("STI036"));

            // Poste RH-02 avec classification conventionnelle
            int conventionId;
            using (var conn = _sqlite.CreateConnection())
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = @"
                    INSERT OR IGNORE INTO ConventionsCollectives (Code, Libelle, DateCreation, DateModification)
                    VALUES ('CC-TEST-RH05', 'Convention test RH-05', datetime('now'), datetime('now'));
                    SELECT Id FROM ConventionsCollectives WHERE Code = 'CC-TEST-RH05';";
                conventionId = Convert.ToInt32(cmd.ExecuteScalar());

                cmd.CommandText = @"
                    UPDATE Postes SET ConventionId = @conv, CategorieConventionnelle = 'Agents de Maîtrise', EchelonConventionnel = 'B'
                    WHERE Id = 1;";
                cmd.Parameters.AddWithValue("@conv", conventionId);
                cmd.ExecuteNonQuery();
            }

            // Offre : CDD, motif Remplacement, indemnités proposées
            var besoin = new BesoinRecrutement
            {
                Reference = "REC-COND-01",
                IntitulePoste = "Responsable Paie & Administration",
                PosteId = 1,
                Departement = "Direction des Ressources Humaines",
                Motif = MotifRecrutement.Remplacement,
                NombrePostes = 1,
                TypeContratVise = "CDD",
                IndemniteLogement = 60000m,
                IndemniteTransport = 25000m,
                IndemniteFonction = 0m,
                IndemniteSujetion = 15000m,
                Statut = StatutBesoinRecrutement.Ouvert,
                DateExpression = DateTime.Today
            };
            int bId = await _recrutementService.EnregistrerBesoinAsync(besoin);

            var besoinRelu = await _recrutementService.ObtenirBesoinParIdAsync(bId);
            Assert.That(besoinRelu.Motif, Is.EqualTo(MotifRecrutement.Remplacement), "Le motif choisi est conservé.");
            Assert.That(besoinRelu.IndemniteLogement, Is.EqualTo(60000m));
            Assert.That(besoinRelu.IndemniteSujetion, Is.EqualTo(15000m));

            int cId = await _recrutementService.EnregistrerCandidatAsync(new Candidat { Nom = "ZONGO", Prenoms = "Salif" });
            int candId = await _recrutementService.PostulerAsync(bId, cId, CanalSourceCandidature.Cooptation);

            // Embauche sans rien préciser : tout est repris de l'offre et du poste
            var salarie = await _recrutementService.IntegrerCandidatEnSalarieAsync(candId, 400000m, new DateTime(2026, 11, 2), new ConditionsEmbauche());

            Assert.That(salarie.EmployeeId, Is.EqualTo("STI036"), "Prochain matricule libre selon le schéma du dossier.");

            var enBase = await _dataService.GetEmployeeByIdAsync("STI036");
            Assert.That(enBase, Is.Not.Null);
            Assert.That(enBase.ConventionId, Is.EqualTo(conventionId));
            Assert.That(enBase.Category, Is.EqualTo("Agents de Maîtrise"));
            Assert.That(enBase.Echelon, Is.EqualTo("B"));
            Assert.That(enBase.HousingAllowance, Is.EqualTo(60000m));
            Assert.That(enBase.TransportAllowance, Is.EqualTo(25000m));
            Assert.That(enBase.FunctionAllowance, Is.EqualTo(0m));
            Assert.That(enBase.SujetionAllowance, Is.EqualTo(15000m));

            using (var conn = _sqlite.CreateConnection())
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "SELECT TypeContrat FROM Employees WHERE EmployeeId = 'STI036';";
                Assert.That(Convert.ToString(cmd.ExecuteScalar()), Is.EqualTo("CDD"), "Le type de contrat de l'offre est conservé sur la fiche.");
            }

            var candMaj = await _recrutementService.ObtenirCandidatureParIdAsync(candId);
            Assert.That(candMaj.TypeContratEmbauche, Is.EqualTo("CDD"), "Le type de contrat est figé sur la candidature embauchée.");
            Assert.That(candMaj.SalarieCreeMatricule, Is.EqualTo("STI036"));
        }

        [Test]
        [Microsoft.VisualStudio.TestTools.UnitTesting.TestMethod]
        public async Task Test14_Embauche_MatriculeSaisiDejaPrisOuInvalide_RefusSansRienEcrire()
        {
            await _dataService.AddEmployeeAsync(new Employee { EmployeeId = "STI035", FirstName = "Paul", LastName = "KABORE", StatutSalarie = 0 });

            var besoin = new BesoinRecrutement { Reference = "REC-MAT-01", IntitulePoste = "Assistant RH", NombrePostes = 1, Statut = StatutBesoinRecrutement.Ouvert, DateExpression = DateTime.Today };
            int bId = await _recrutementService.EnregistrerBesoinAsync(besoin);
            int cId = await _recrutementService.EnregistrerCandidatAsync(new Candidat { Nom = "OUATTARA", Prenoms = "Mariam" });
            int candId = await _recrutementService.PostulerAsync(bId, cId, CanalSourceCandidature.SiteWeb);

            // Matricule déjà attribué (casse différente) : refus, la fiche existante n'est pas écrasée
            var exDoublon = Assert.ThrowsAsync<InvalidOperationException>(async () =>
            {
                await _recrutementService.IntegrerCandidatEnSalarieAsync(candId, 300000m, DateTime.Today, new ConditionsEmbauche { Matricule = "sti035" });
            });
            Assert.That(exDoublon.Message, Does.Contain("déjà attribué"));

            var existant = await _dataService.GetEmployeeByIdAsync("STI035");
            Assert.That(existant.LastName, Is.EqualTo("KABORE"));

            // Matricule au format invalide
            Assert.ThrowsAsync<ArgumentException>(async () =>
            {
                await _recrutementService.IntegrerCandidatEnSalarieAsync(candId, 300000m, DateTime.Today, new ConditionsEmbauche { Matricule = "STI 036/A" });
            });

            var cand = await _recrutementService.ObtenirCandidatureParIdAsync(candId);
            Assert.That(cand.Statut, Is.Not.EqualTo(StatutCandidature.Embauchee), "Aucune embauche ne doit avoir été enregistrée.");
            var besoinRelu = await _recrutementService.ObtenirBesoinParIdAsync(bId);
            Assert.That(besoinRelu.NombreRecrutes, Is.EqualTo(0));

            // Matricule saisi libre : accepté tel quel (modifiable à l'écran)
            var salarie = await _recrutementService.IntegrerCandidatEnSalarieAsync(candId, 300000m, DateTime.Today, new ConditionsEmbauche { Matricule = " STI100 " });
            Assert.That(salarie.EmployeeId, Is.EqualTo("STI100"));
        }

        [Test]
        [Microsoft.VisualStudio.TestTools.UnitTesting.TestMethod]
        public async Task Test11_Exports_LettreEmbauchePdfEtBilanExcel()
        {
            var besoins = await _recrutementService.ObtenirBesoinsAsync();
            var vivier = await _recrutementService.ObtenirCandidatsAsync();
            int bId = besoins[0].Id;
            int cId = vivier[0].Id;

            int candId = await _recrutementService.PostulerAsync(bId, cId, CanalSourceCandidature.SiteWeb);
            await _recrutementService.IntegrerCandidatEnSalarieAsync(candId, 380000m, DateTime.Today);

            var candidature = await _recrutementService.ObtenirCandidatureParIdAsync(candId);

            // 1. Test de la Lettre d'Embauche PDF
            string pdfPath = Path.Combine(Path.GetTempPath(), $"lettre_embauche_test_{Guid.NewGuid():N}.pdf");
            try
            {
                bool pdfOk = _exportService.GenererLettreEmbauchePdf(candidature, pdfPath);
                Assert.That(pdfOk, Is.True);
                Assert.That(File.Exists(pdfPath), Is.True);
                Assert.That(new FileInfo(pdfPath).Length, Is.GreaterThan(1000), "Le fichier PDF de lettre d'embauche doit contenir des données valides.");
            }
            finally
            {
                if (File.Exists(pdfPath)) File.Delete(pdfPath);
            }

            // 2. Test du Bilan Excel à 4 feuilles
            string xlsxPath = Path.Combine(Path.GetTempPath(), $"bilan_recrutement_test_{Guid.NewGuid():N}.xlsx");
            try
            {
                var candList = await _recrutementService.ObtenirCandidaturesAsync();
                bool xlsxOk = _exportService.GenererBilanRecrutementExcel(besoins, candList, vivier, xlsxPath);
                Assert.That(xlsxOk, Is.True);
                Assert.That(File.Exists(xlsxPath), Is.True);

                // Vérifier la présence des 4 feuilles
                using (var wb = new XLWorkbook(xlsxPath))
                {
                    Assert.That(wb.Worksheets.Count, Is.EqualTo(4));
                    Assert.That(wb.Worksheet("Synthèse & KPIs"), Is.Not.Null);
                    Assert.That(wb.Worksheet("Besoins & Postes"), Is.Not.Null);
                    Assert.That(wb.Worksheet("Pipeline Candidatures"), Is.Not.Null);
                    Assert.That(wb.Worksheet("Vivier des Talents"), Is.Not.Null);
                }
            }
            finally
            {
                if (File.Exists(xlsxPath)) File.Delete(xlsxPath);
            }
        }
    }
}
