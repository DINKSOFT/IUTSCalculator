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
    public class EvaluationTests
    {
        private string _tempDb;
        private SQLiteService _sqlite;
        private DataService _dataService;
        private OrganigrammeService _organigrammeService;
        private FichePosteService _fichePosteService;
        private EvaluationService _evaluationService;
        private EvaluationExportService _exportService;

        [SetUp]
        [Microsoft.VisualStudio.TestTools.UnitTesting.TestInitialize]
        public void Setup()
        {
            _tempDb = Path.Combine(Path.GetTempPath(), $"test_eval_{Guid.NewGuid():N}.db");
            _sqlite = new SQLiteService(_tempDb);
            _dataService = new DataService(_tempDb);
            _organigrammeService = new OrganigrammeService(_sqlite);
            _fichePosteService = new FichePosteService(_sqlite);
            _evaluationService = new EvaluationService(_sqlite, _organigrammeService);
            _exportService = new EvaluationExportService();
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
        public async Task Schema_Migration31_TablesEtIndexCrees()
        {
            using (var conn = _sqlite.CreerConnexion())
            {
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = @"
                        SELECT count(*) FROM sqlite_master WHERE type='table' 
                        AND name IN ('CampagnesEvaluation', 'EntretiensEvaluation', 'EntretienCriteres', 'EntretienObjectifs', 'EntretienBesoinsFormation');";
                    var tableCount = Convert.ToInt32(await cmd.ExecuteScalarAsync());
                    Assert.That(tableCount, Is.EqualTo(5), "Les 5 tables RH-03 doivent être créées.");

                    cmd.CommandText = @"
                        SELECT count(*) FROM sqlite_master WHERE type='index' 
                        AND name = 'UX_EntretiensEvaluation_Campagne_Employe';";
                    var indexCount = Convert.ToInt32(await cmd.ExecuteScalarAsync());
                    Assert.That(indexCount, Is.EqualTo(1), "L'index unique (CampagneId, EmployeeId) doit exister.");
                }
            }
        }

        [Test]
        [Microsoft.VisualStudio.TestTools.UnitTesting.TestMethod]
        public async Task CampagnesCRUD_FonctionneCorrectement()
        {
            var campagne = new CampagneEvaluation
            {
                Code = "CAMP-2026",
                Titre = "Campagne Annuelle 2026",
                AnneeExercice = 2026,
                DateDebut = new DateTime(2026, 1, 1),
                DateFin = new DateTime(2026, 3, 31),
                Description = "Campagne test",
                Statut = StatutCampagne.Planifiee
            };

            await _evaluationService.EnregistrerCampagneAsync(campagne);
            Assert.That(campagne.Id, Is.GreaterThan(0));

            var liste = await _evaluationService.ObtenirCampagnesAsync();
            Assert.That(liste.Count, Is.EqualTo(1));
            Assert.That(liste[0].Titre, Is.EqualTo("Campagne Annuelle 2026"));
            Assert.That(liste[0].AnneeExercice, Is.EqualTo(2026));

            campagne.Titre = "Campagne Modifiée";
            campagne.Statut = StatutCampagne.EnCours;
            await _evaluationService.EnregistrerCampagneAsync(campagne);

            var chargee = await _evaluationService.ObtenirCampagneParIdAsync(campagne.Id);
            Assert.That(chargee.Titre, Is.EqualTo("Campagne Modifiée"));
            Assert.That(chargee.Statut, Is.EqualTo(StatutCampagne.EnCours));

            await _evaluationService.SupprimerCampagneAsync(campagne.Id);
            var apresSuppr = await _evaluationService.ObtenirCampagnesAsync();
            Assert.That(apresSuppr.Count, Is.EqualTo(0));
        }

        [Test]
        [Microsoft.VisualStudio.TestTools.UnitTesting.TestMethod]
        public async Task InitialiserCampagneAsync_FiltreEstActifEtEstIdempotent()
        {
            // Salarié 1 : Actif standard (StatutSalarie = 0, DateSortie = null)
            await _dataService.AddEmployeeAsync(new Employee
            {
                EmployeeId = "EMP01",
                FirstName = "Amadou",
                LastName = "TRAORE",
                StatutSalarie = 0,
                DateSortie = null
            });

            // Salarié 2 : Sortie passée (StatutSalarie = 0 mais DateSortie dans le passé -> EstActif = false !)
            await _dataService.AddEmployeeAsync(new Employee
            {
                EmployeeId = "EMP02",
                FirstName = "Jean",
                LastName = "OUEDRAOGO",
                StatutSalarie = 0,
                DateSortie = DateTime.Today.AddDays(-15)
            });

            // Salarié 3 : Inactif (StatutSalarie = 1)
            await _dataService.AddEmployeeAsync(new Employee
            {
                EmployeeId = "EMP03",
                FirstName = "Fatou",
                LastName = "KABORE",
                StatutSalarie = 1,
                DateSortie = null
            });

            // Salarié 4 : Actif avec sortie future
            await _dataService.AddEmployeeAsync(new Employee
            {
                EmployeeId = "EMP04",
                FirstName = "Moussa",
                LastName = "SAWADOGO",
                StatutSalarie = 0,
                DateSortie = DateTime.Today.AddDays(30)
            });

            var campagne = new CampagneEvaluation
            {
                Code = "CAMP-ACTIFS",
                Titre = "Campagne Test Actifs",
                AnneeExercice = 2026,
                DateDebut = DateTime.Today,
                DateFin = DateTime.Today.AddMonths(2),
                Statut = StatutCampagne.EnCours
            };
            await _evaluationService.EnregistrerCampagneAsync(campagne);

            // Premier lancement de l'initialisation
            var rapport1 = await _evaluationService.InitialiserEntretiensCampagneAsync(campagne.Id);
            Assert.That(rapport1.TotalSalariesActifs, Is.EqualTo(2), "Seuls EMP01 et EMP04 sont actifs selon EstActif.");
            Assert.That(rapport1.EntretiensCrees, Is.EqualTo(2));
            Assert.That(rapport1.EntretiensExistantDeja, Is.EqualTo(0));

            var entretiens = await _evaluationService.ObtenirEntretiensCampagneAsync(campagne.Id);
            Assert.That(entretiens.Count, Is.EqualTo(2));
            Assert.That(entretiens.Any(e => e.EmployeeId == "EMP01"), Is.True);
            Assert.That(entretiens.Any(e => e.EmployeeId == "EMP04"), Is.True);
            Assert.That(entretiens.Any(e => e.EmployeeId == "EMP02"), Is.False, "Le salarié parti ne doit pas être inclus.");
            Assert.That(entretiens.Any(e => e.EmployeeId == "EMP03"), Is.False, "Le salarié inactif ne doit pas être inclus.");

            // Deuxième lancement (test d'idempotence)
            var rapport2 = await _evaluationService.InitialiserEntretiensCampagneAsync(campagne.Id);
            Assert.That(rapport2.TotalSalariesActifs, Is.EqualTo(2));
            Assert.That(rapport2.EntretiensCrees, Is.EqualTo(0), "Aucun nouvel entretien ne doit être créé.");
            Assert.That(rapport2.EntretiensExistantDeja, Is.EqualTo(2), "Les 2 entretiens déjà existants doivent être détectés sans lever d'exception.");
        }

        [Test]
        [Microsoft.VisualStudio.TestTools.UnitTesting.TestMethod]
        public async Task ResolutionEvaluateur_RespecteHierarchie3Etapes()
        {
            // 1. Direction et Postes
            var dir = new UniteOrganisationnelle { Code = "DSI", Libelle = "Direction SI", TypeUnite = TypeUniteOrganisationnelle.Direction };
            await _organigrammeService.EnregistrerUniteAsync(dir);

            var pDirecteur = new Poste { Code = "DIR-SI", Intitule = "Directeur SI", UniteId = dir.Id, EffectifRequis = 1, Actif = true };
            await _organigrammeService.EnregistrerPosteAsync(pDirecteur);

            var pChefProjet = new Poste { Code = "CP-SI", Intitule = "Chef de Projet", UniteId = dir.Id, EffectifRequis = 1, Actif = true, SuperieurPosteId = pDirecteur.Id };
            await _organigrammeService.EnregistrerPosteAsync(pChefProjet);

            var pDev = new Poste { Code = "DEV", Intitule = "Développeur", UniteId = dir.Id, EffectifRequis = 2, Actif = true, SuperieurPosteId = pChefProjet.Id };
            await _organigrammeService.EnregistrerPosteAsync(pDev);

            var pOrphelin = new Poste { Code = "STAG", Intitule = "Stagiaire", UniteId = dir.Id, EffectifRequis = 1, Actif = true, SuperieurPosteId = null };
            await _organigrammeService.EnregistrerPosteAsync(pOrphelin);

            // 2. Salariés
            // Directeur
            await _dataService.AddEmployeeAsync(new Employee { EmployeeId = "DIR01", FirstName = "Directeur", LastName = "UN", PosteId = pDirecteur.Id, StatutSalarie = 0 });
            // Chef de projet
            await _dataService.AddEmployeeAsync(new Employee { EmployeeId = "CP01", FirstName = "Chef", LastName = "PROJET", PosteId = pChefProjet.Id, StatutSalarie = 0 });

            // Cas 1 : ManagerMatricule explicite (Priorité 1)
            // Dev 1 a son poste rattaché à CP-SI, mais son ManagerMatricule est forcé sur DIR01 -> Priorité 1 doit l'emporter !
            await _dataService.AddEmployeeAsync(new Employee { EmployeeId = "DEV01", FirstName = "Dev", LastName = "EXPLICITE", PosteId = pDev.Id, ManagerMatricule = "DIR01", StatutSalarie = 0 });

            // Cas 2 : Dérivation par organigramme (Priorité 2)
            // Dev 2 n'a pas de ManagerMatricule, son poste pDev a pour supérieur pChefProjet dont le titulaire est CP01 -> Doit être CP01
            await _dataService.AddEmployeeAsync(new Employee { EmployeeId = "DEV02", FirstName = "Dev", LastName = "ORGANIGRAMME", PosteId = pDev.Id, ManagerMatricule = null, StatutSalarie = 0 });

            // Cas 3 : Aucun supérieur identifiable (Priorité 3 : non assigné)
            await _dataService.AddEmployeeAsync(new Employee { EmployeeId = "STAG01", FirstName = "Stagiaire", LastName = "SANS_CHEF", PosteId = pOrphelin.Id, ManagerMatricule = null, StatutSalarie = 0 });

            var camp = new CampagneEvaluation { Code = "CAMP-N1", Titre = "Campagne N+1", AnneeExercice = 2026, Statut = StatutCampagne.EnCours };
            await _evaluationService.EnregistrerCampagneAsync(camp);

            await _evaluationService.InitialiserEntretiensCampagneAsync(camp.Id);

            var entretiens = await _evaluationService.ObtenirEntretiensCampagneAsync(camp.Id);

            var entDev01 = entretiens.First(e => e.EmployeeId == "DEV01");
            Assert.That(entDev01.EvaluateurMatricule, Is.EqualTo("DIR01"), "Priorité 1 : ManagerMatricule explicite prioritaire sur l'organigramme.");

            var entDev02 = entretiens.First(e => e.EmployeeId == "DEV02");
            Assert.That(entDev02.EvaluateurMatricule, Is.EqualTo("CP01"), "Priorité 2 : Titulaire unique du poste supérieur dans l'organigramme.");

            var entStag = entretiens.First(e => e.EmployeeId == "STAG01");
            Assert.That(string.IsNullOrEmpty(entStag.EvaluateurMatricule), Is.True, "Priorité 3 : Aucun évaluateur deviné si pas de supérieur.");
            Assert.That(entStag.EvaluateurAAssigner, Is.True, "Le salarié sans évaluateur doit avoir EvaluateurAAssigner = true.");
        }

        [Test]
        [Microsoft.VisualStudio.TestTools.UnitTesting.TestMethod]
        public async Task NoteGlobale_RecalculeeEtFligeeALaValidation()
        {
            await _dataService.AddEmployeeAsync(new Employee { EmployeeId = "EMP10", FirstName = "Paul", LastName = "SANOU", StatutSalarie = 0 });

            var camp = new CampagneEvaluation { Code = "CAMP-NOTES", Titre = "Campagne Notes", AnneeExercice = 2026, Statut = StatutCampagne.EnCours };
            await _evaluationService.EnregistrerCampagneAsync(camp);

            await _evaluationService.InitialiserEntretiensCampagneAsync(camp.Id);
            var entretiens = await _evaluationService.ObtenirEntretiensCampagneAsync(camp.Id);
            var entretien = entretiens.First(e => e.EmployeeId == "EMP10");
            entretien = await _evaluationService.ObtenirEntretienCompletAsync(entretien.Id);

            // Ajout de critères de notation
            // Critère 1 : Note 4 / 5, Poids 2
            // Critère 2 : Note 2 / 5, Poids 1
            // Moyenne pondérée attendue : (4*2 + 2*1) / (2 + 1) = 10 / 3 = 3.33
            entretien.Criteres.Clear();
            entretien.Criteres.Add(new EntretienCritere { EntretienId = entretien.Id, Categorie = "Technique", Libelle = "C# / WPF", Note = 4, Poids = 2 });
            entretien.Criteres.Add(new EntretienCritere { EntretienId = entretien.Id, Categorie = "Comportementale", Libelle = "Travail d'équipe", Note = 2, Poids = 1 });

            await _evaluationService.EnregistrerEntretienAsync(entretien);

            var relu = await _evaluationService.ObtenirEntretienCompletAsync(entretien.Id);
            Assert.That(relu.NoteGlobale, Is.EqualTo(3.33));

            // Modification de la note de critère 2 : Note passe à 4
            // Nouvelle moyenne : (4*2 + 4*1) / 3 = 12 / 3 = 4.00
            relu.Criteres[1].Note = 4;
            await _evaluationService.EnregistrerEntretienAsync(relu);

            relu = await _evaluationService.ObtenirEntretienCompletAsync(entretien.Id);
            Assert.That(relu.NoteGlobale, Is.EqualTo(4.00));

            // Validation de l'entretien -> Fige le document. La validation passe exclusivement par
            // la transition contrôlée du service (ChangerStatutEntretienAsync), qui scelle la date.
            relu.DateEntretien = DateTime.Today;
            await _evaluationService.EnregistrerEntretienAsync(relu);
            await _evaluationService.ChangerStatutEntretienAsync(relu.Id, StatutEntretien.Valide, "Testeur RH");
            relu = await _evaluationService.ObtenirEntretienCompletAsync(entretien.Id);
            Assert.That(relu.Statut, Is.EqualTo(StatutEntretien.Valide));
            Assert.That(relu.NoteGlobale, Is.EqualTo(4.00), "La note est figée à la validation.");

            // Tentative de modifier l'entretien validé -> Rejeté par la règle d'immutabilité côté service
            relu.PointsForts = "Tentative de modification après validation";
            Assert.ThrowsAsync<InvalidOperationException>(async () =>
            {
                await _evaluationService.EnregistrerEntretienAsync(relu);
            }, "Toute modification sur un entretien validé doit être bloquée côté service.");
        }

        [Test]
        [Microsoft.VisualStudio.TestTools.UnitTesting.TestMethod]
        public async Task Validation_TransitionUniqueControleeParLeService_ScelleDateEtAuteur()
        {
            await _dataService.AddEmployeeAsync(new Employee { EmployeeId = "EMP15", FirstName = "Ines", LastName = "ZERBO", StatutSalarie = 0 });

            var camp = new CampagneEvaluation { Code = "CAMP-VALID", Titre = "Campagne Validation", AnneeExercice = 2026, Statut = StatutCampagne.EnCours };
            await _evaluationService.EnregistrerCampagneAsync(camp);
            await _evaluationService.InitialiserEntretiensCampagneAsync(camp.Id);

            var entretiens = await _evaluationService.ObtenirEntretiensCampagneAsync(camp.Id);
            var ent = await _evaluationService.ObtenirEntretienCompletAsync(entretiens.First(e => e.EmployeeId == "EMP15").Id);
            Assert.That(ent.DateValidation, Is.Null);

            // 1. Écrire le statut Validé par un simple enregistrement est refusé (aucune écriture)
            ent.PointsForts = "Saisie avant validation";
            ent.Statut = StatutEntretien.Valide;
            var exEnregistrement = Assert.ThrowsAsync<InvalidOperationException>(async () =>
            {
                await _evaluationService.EnregistrerEntretienAsync(ent);
            });
            Assert.That(exEnregistrement.Message, Does.Contain("Valider & Sceller"));

            var relu = await _evaluationService.ObtenirEntretienCompletAsync(ent.Id);
            Assert.That(relu.Statut, Is.EqualTo(StatutEntretien.Planifie), "Le refus ne doit rien écrire.");
            Assert.That(relu.PointsForts, Is.Not.EqualTo("Saisie avant validation"));

            // 2. Séquence de l'écran « Valider & Sceller » : enregistrement (statut inchangé) puis transition unique
            relu.PointsForts = "Saisie avant validation";
            DateTime avant = DateTime.Now.AddSeconds(-2);
            await _evaluationService.EnregistrerEntretienAsync(relu);
            Assert.DoesNotThrowAsync(async () =>
            {
                await _evaluationService.ChangerStatutEntretienAsync(relu.Id, StatutEntretien.Valide, "Awa SANOGO");
            }, "La validation ne doit plus échouer après l'enregistrement.");

            var valide = await _evaluationService.ObtenirEntretienCompletAsync(relu.Id);
            Assert.That(valide.Statut, Is.EqualTo(StatutEntretien.Valide));
            Assert.That(valide.EstVerrouille, Is.True);
            Assert.That(valide.PointsForts, Is.EqualTo("Saisie avant validation"), "Les dernières saisies sont conservées.");
            Assert.That(valide.DateValidation, Is.Not.Null, "La date de validation doit être enregistrée.");
            Assert.That(valide.DateValidation.Value, Is.GreaterThanOrEqualTo(avant));
            Assert.That(valide.ValidePar, Is.EqualTo("Awa SANOGO"));
            Assert.That(valide.ValidationLibelle, Does.StartWith("Validé et scellé le ").And.EndWith(" par Awa SANOGO"));

            // 3. Une seconde validation est refusée (document scellé)
            Assert.ThrowsAsync<InvalidOperationException>(async () =>
            {
                await _evaluationService.ChangerStatutEntretienAsync(relu.Id, StatutEntretien.Valide);
            });
        }

        [Test]
        [Microsoft.VisualStudio.TestTools.UnitTesting.TestMethod]
        public async Task BesoinsFormationEtObjectifs_LiaisonCompetenceEtCycleN1()
        {
            await _dataService.AddEmployeeAsync(new Employee { EmployeeId = "EMP20", FirstName = "Awa", LastName = "COMPAORE", StatutSalarie = 0 });

            // Création d'une compétence dans le catalogue RH-02
            var comp = new Competence { Code = "SEC-01", Libelle = "Cybersécurité des SI", Domaine = DomaineCompetence.Reglementaire };
            await _fichePosteService.EnregistrerCompetenceAsync(comp);

            var camp = new CampagneEvaluation { Code = "CAMP-2026-B", Titre = "Campagne 2026", AnneeExercice = 2026, Statut = StatutCampagne.EnCours };
            await _evaluationService.EnregistrerCampagneAsync(camp);

            await _evaluationService.InitialiserEntretiensCampagneAsync(camp.Id);
            var entretiens = await _evaluationService.ObtenirEntretiensCampagneAsync(camp.Id);
            var entretien = await _evaluationService.ObtenirEntretienCompletAsync(entretiens.First(e => e.EmployeeId == "EMP20").Id);

            // 1. Ajout de critère rattaché à la compétence -> Domaine dérive vers la catégorie
            entretien.Criteres.Clear();
            entretien.Criteres.Add(new EntretienCritere
            {
                EntretienId = entretien.Id,
                CompetenceId = comp.Id,
                Libelle = "Maîtrise normes sécurité",
                Categorie = "Réglementaire",
                Note = 4,
                Poids = 1
            });

            // 2. Objectifs N-1 et N+1
            entretien.ObjectifsPasses.Add(new EntretienObjectif
            {
                EntretienId = entretien.Id,
                Intitule = "Migrer base vers SQLite crypté",
                TypeObjectif = TypeObjectifEntretien.ObjectifPasse,
                TauxAtteintePct = 90,
                Poids = 2
            });

            entretien.NouveauxObjectifs.Clear();
            entretien.NouveauxObjectifs.Add(new EntretienObjectif
            {
                EntretienId = entretien.Id,
                Intitule = "Déployer module RH-04",
                TypeObjectif = TypeObjectifEntretien.NouvelObjectif,
                Poids = 1,
                Echeance = "30/06/2026"
            });

            // 3. Besoin de formation avec CompetenceId (Passerelle RH-04)
            entretien.BesoinsFormation.Clear();
            entretien.BesoinsFormation.Add(new EntretienBesoinFormation
            {
                EntretienId = entretien.Id,
                CompetenceId = comp.Id,
                ThemeFormation = "Sécurisation architectures cloud",
                Priorite = PrioriteFormation.Haute,
                Statut = StatutBesoinFormation.Retenu
            });

            await _evaluationService.EnregistrerEntretienAsync(entretien);

            var relu = await _evaluationService.ObtenirEntretienCompletAsync(entretien.Id);

            Assert.That(relu.Criteres.Count, Is.EqualTo(1));
            Assert.That(relu.Criteres[0].CompetenceId, Is.EqualTo(comp.Id));
            Assert.That(relu.Criteres[0].Categorie, Is.EqualTo("Réglementaire"));

            Assert.That(relu.ObjectifsPasses.Count, Is.EqualTo(1));
            Assert.That(relu.ObjectifsPasses[0].TauxAtteintePct, Is.EqualTo(90));

            Assert.That(relu.NouveauxObjectifs.Count, Is.EqualTo(1));
            Assert.That(relu.NouveauxObjectifs[0].Intitule, Is.EqualTo("Déployer module RH-04"));

            Assert.That(relu.BesoinsFormation.Count, Is.EqualTo(1));
            Assert.That(relu.BesoinsFormation[0].CompetenceId, Is.EqualTo(comp.Id));
            Assert.That(relu.BesoinsFormation[0].Priorite, Is.EqualTo(PrioriteFormation.Haute));
        }

        [Test]
        [Microsoft.VisualStudio.TestTools.UnitTesting.TestMethod]
        public async Task Exports_PdfEtExcelGeneresAvecSucces()
        {
            await _dataService.AddEmployeeAsync(new Employee
            {
                EmployeeId = "EMP30",
                FirstName = "Bintou",
                LastName = "KONE",
                StatutSalarie = 0,
                Email = "bintou.kone@example.com"
            });

            var camp = new CampagneEvaluation
            {
                Code = "CAMP-EXP",
                Titre = "Campagne Annuelle Test Exports",
                AnneeExercice = 2026,
                DateDebut = new DateTime(2026, 1, 1),
                DateFin = new DateTime(2026, 3, 31),
                Statut = StatutCampagne.EnCours
            };
            await _evaluationService.EnregistrerCampagneAsync(camp);
            await _evaluationService.InitialiserEntretiensCampagneAsync(camp.Id);

            var entretiens = await _evaluationService.ObtenirEntretiensCampagneAsync(camp.Id);
            var ent = await _evaluationService.ObtenirEntretienCompletAsync(entretiens.First(e => e.EmployeeId == "EMP30").Id);

            ent.EvaluateurMatricule = "EVAL99";
            ent.EvaluateurNomComplet = "Directeur RH";
            ent.DateEntretien = DateTime.Today;
            ent.Statut = StatutEntretien.Realise;
            ent.PointsForts = "Rigueur, investissement.";
            ent.AxesProgres = "Gestion du stress en pic d'activité.";
            ent.CommentairesEvaluateur = "Très bonne progression.";

            ent.Criteres.Add(new EntretienCritere
            {
                EntretienId = ent.Id,
                Categorie = "Technique",
                Libelle = "Qualité de code",
                Note = 4,
                Poids = 2
            });

            ent.NouveauxObjectifs.Add(new EntretienObjectif
            {
                EntretienId = ent.Id,
                Intitule = "Certification Scrum Master",
                TypeObjectif = TypeObjectifEntretien.NouvelObjectif,
                Poids = 1
            });

            ent.BesoinsFormation.Add(new EntretienBesoinFormation
            {
                EntretienId = ent.Id,
                ThemeFormation = "Agilité à l'échelle",
                Priorite = PrioriteFormation.Normale
            });

            await _evaluationService.EnregistrerEntretienAsync(ent);
            ent = await _evaluationService.ObtenirEntretienCompletAsync(ent.Id);

            var dossier = new Dossier
            {
                RaisonSociale = "ENTREPRISE BURKINA RH",
                IFU = "00012345678A",
                RCCM = "BF-OUA-2026-B-1234",
                RegimeFiscal = "Régime du Réel Normal"
            };

            // 1. Test Export PDF de l'entretien (iTextSharp)
            string pdfPath = Path.Combine(Path.GetTempPath(), $"fiche_eval_{Guid.NewGuid():N}.pdf");
            try
            {
                bool pdfOk = _exportService.GenererFicheEntretienPdf(ent, dossier, pdfPath);
                Assert.That(pdfOk, Is.True, "La génération du PDF doit retourner true.");
                Assert.That(File.Exists(pdfPath), Is.True, "Le fichier PDF de l'entretien doit être présent sur le disque.");
                var fileInfo = new FileInfo(pdfPath);
                Assert.That(fileInfo.Length, Is.GreaterThan(1000), "Le fichier PDF ne doit pas être vide.");
            }
            finally
            {
                if (File.Exists(pdfPath)) File.Delete(pdfPath);
            }

            // 2. Test Export Excel de synthèse de campagne (ClosedXML 4 feuilles)
            string xlsxPath = Path.Combine(Path.GetTempPath(), $"campagne_{Guid.NewGuid():N}.xlsx");
            try
            {
                var stats = await _evaluationService.CalculerStatistiquesCampagneAsync(camp.Id);
                var listeEnt = await _evaluationService.ObtenirEntretiensCampagneAsync(camp.Id);

                bool xlsxOk = _exportService.GenererSyntheseCampagneExcel(camp, listeEnt, stats, dossier, xlsxPath);
                Assert.That(xlsxOk, Is.True, "La génération Excel doit retourner true.");
                Assert.That(File.Exists(xlsxPath), Is.True, "Le fichier Excel de la campagne doit être présent sur le disque.");
                var fileInfo = new FileInfo(xlsxPath);
                Assert.That(fileInfo.Length, Is.GreaterThan(1000), "Le fichier Excel ne doit pas être vide.");
            }
            finally
            {
                if (File.Exists(xlsxPath)) File.Delete(xlsxPath);
            }
        }
    }
}
