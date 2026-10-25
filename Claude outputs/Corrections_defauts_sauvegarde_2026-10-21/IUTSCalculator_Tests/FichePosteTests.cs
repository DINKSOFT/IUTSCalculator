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
    public class FichePosteTests
    {
        [Test]
        [Microsoft.VisualStudio.TestTools.UnitTesting.TestMethod]
        public async Task CalculerSyntheseBudgetaireEffectifsAsync_CalculeQuatreStatutsGpec()
        {
            string tempDb = Path.Combine(Path.GetTempPath(), $"test_gpec_{Guid.NewGuid():N}.db");
            try
            {
                var sqlite = new SQLiteService(tempDb);
                var organigrammeService = new OrganigrammeService(sqlite);
                var fichePosteService = new FichePosteService(sqlite);
                var dataService = new DataService(tempDb);

                // 1. Création d'une direction
                var dg = new UniteOrganisationnelle { Code = "DG", Libelle = "Direction Générale", TypeUnite = TypeUniteOrganisationnelle.DirectionGenerale };
                await organigrammeService.EnregistrerUniteAsync(dg);

                // 2. Création de 4 postes avec des configurations d'effectifs différentes
                // Post 1 : Requis = 2, Pourvu = 0 -> Vacant
                var p1 = new Poste { Code = "P-VAC", Intitule = "Poste Vacant", UniteId = dg.Id, EffectifRequis = 2, Actif = true };
                await organigrammeService.EnregistrerPosteAsync(p1);

                // Post 2 : Requis = 3, Pourvu = 1 -> Partiel
                var p2 = new Poste { Code = "P-PAR", Intitule = "Poste Partiel", UniteId = dg.Id, EffectifRequis = 3, Actif = true };
                await organigrammeService.EnregistrerPosteAsync(p2);

                // Post 3 : Requis = 2, Pourvu = 2 -> Pourvu
                var p3 = new Poste { Code = "P-POU", Intitule = "Poste Pourvu", UniteId = dg.Id, EffectifRequis = 2, Actif = true };
                await organigrammeService.EnregistrerPosteAsync(p3);

                // Post 4 : Requis = 1, Pourvu = 2 -> Sureffectif
                var p4 = new Poste { Code = "P-SUR", Intitule = "Poste Sureffectif", UniteId = dg.Id, EffectifRequis = 1, Actif = true };
                await organigrammeService.EnregistrerPosteAsync(p4);

                // 3. Affectation des salariés actifs (StatutSalarie = 0)
                // Salarié pour P2 (1 salarié)
                await dataService.AddEmployeeAsync(new Employee { EmployeeId = "EMP01", FirstName = "Jean", LastName = "KABORE", PosteId = p2.Id, StatutSalarie = 0 });

                // Salariés pour P3 (2 salariés)
                await dataService.AddEmployeeAsync(new Employee { EmployeeId = "EMP02", FirstName = "Alice", LastName = "OUEDRAOGO", PosteId = p3.Id, StatutSalarie = 0 });
                await dataService.AddEmployeeAsync(new Employee { EmployeeId = "EMP03", FirstName = "Boureima", LastName = "SAWADOGO", PosteId = p3.Id, StatutSalarie = 0 });

                // Salariés pour P4 (2 salariés, un troisième inactif ne doit pas être compté)
                await dataService.AddEmployeeAsync(new Employee { EmployeeId = "EMP04", FirstName = "Moussa", LastName = "ZONGO", PosteId = p4.Id, StatutSalarie = 0 });
                await dataService.AddEmployeeAsync(new Employee { EmployeeId = "EMP05", FirstName = "Fatou", LastName = "TRAORE", PosteId = p4.Id, StatutSalarie = 0 });
                // Salarié inactif rattaché à P4 (démissionné, StatutSalarie = 1) -> NE DOIT PAS ÊTRE COMPTÉ
                await dataService.AddEmployeeAsync(new Employee { EmployeeId = "EMP06", FirstName = "Ancien", LastName = "PARTI", PosteId = p4.Id, StatutSalarie = 1 });

                // 4. Calcul de la synthèse GPEC
                var syntheses = await fichePosteService.CalculerSyntheseBudgetaireEffectifsAsync();

                Assert.That(syntheses.Count, Is.EqualTo(4));

                var synP1 = syntheses.First(s => s.Code == "P-VAC");
                Assert.That(synP1.EffectifBudgete, Is.EqualTo(2));
                Assert.That(synP1.EffectifPourvu, Is.EqualTo(0));
                Assert.That(synP1.EcartEffectif, Is.EqualTo(2));
                Assert.That(synP1.TauxOccupation, Is.EqualTo(0.0));
                Assert.That(synP1.StatutOccupationGpec, Is.EqualTo("Vacant"));

                var synP2 = syntheses.First(s => s.Code == "P-PAR");
                Assert.That(synP2.EffectifBudgete, Is.EqualTo(3));
                Assert.That(synP2.EffectifPourvu, Is.EqualTo(1));
                Assert.That(synP2.EcartEffectif, Is.EqualTo(2));
                Assert.That(synP2.TauxOccupation, Is.EqualTo(33.3));
                Assert.That(synP2.StatutOccupationGpec, Is.EqualTo("Partiel"));
                Assert.That(synP2.Titulaires.Count, Is.EqualTo(1));
                Assert.That(synP2.Titulaires[0].Matricule, Is.EqualTo("EMP01"));

                var synP3 = syntheses.First(s => s.Code == "P-POU");
                Assert.That(synP3.EffectifBudgete, Is.EqualTo(2));
                Assert.That(synP3.EffectifPourvu, Is.EqualTo(2));
                Assert.That(synP3.EcartEffectif, Is.EqualTo(0));
                Assert.That(synP3.TauxOccupation, Is.EqualTo(100.0));
                Assert.That(synP3.StatutOccupationGpec, Is.EqualTo("Pourvu"));
                Assert.That(synP3.Titulaires.Count, Is.EqualTo(2));

                var synP4 = syntheses.First(s => s.Code == "P-SUR");
                Assert.That(synP4.EffectifBudgete, Is.EqualTo(1));
                Assert.That(synP4.EffectifPourvu, Is.EqualTo(2)); // L'inactif EMP06 n'est pas comptabilisé
                Assert.That(synP4.EcartEffectif, Is.EqualTo(-1));
                Assert.That(synP4.TauxOccupation, Is.EqualTo(200.0));
                Assert.That(synP4.StatutOccupationGpec, Is.EqualTo("Sureffectif"));
                Assert.That(synP4.Titulaires.Count, Is.EqualTo(2));
            }
            finally
            {
                try { if (File.Exists(tempDb)) File.Delete(tempDb); } catch { }
            }
        }

        [Test]
        [Microsoft.VisualStudio.TestTools.UnitTesting.TestMethod]
        public async Task CompetencesCatalogueEtPoste_CrudFonctionne()
        {
            string tempDb = Path.Combine(Path.GetTempPath(), $"test_comp_{Guid.NewGuid():N}.db");
            try
            {
                var sqlite = new SQLiteService(tempDb);
                var fichePosteService = new FichePosteService(sqlite);
                var organigrammeService = new OrganigrammeService(sqlite);

                // 1. Migration 30 a pré-alimenté 10 compétences de référence
                var initiales = await fichePosteService.ChargerToutesCompetencesAsync();
                Assert.That(initiales.Count, Is.GreaterThanOrEqualTo(10));
                Assert.That(initiales.Any(c => c.Code == "COMPTA-OHADA"), Is.True);

                // 2. Création d'une nouvelle compétence
                var nouvelle = new Competence
                {
                    Code = "COMP-LEAD-INNOV",
                    Libelle = "Innovation & Conduite du changement",
                    Domaine = DomaineCompetence.Management,
                    Description = "Capacité à piloter la transformation numérique et organisationnelle."
                };
                await fichePosteService.EnregistrerCompetenceAsync(nouvelle);
                Assert.That(nouvelle.Id, Is.GreaterThan(0));

                // 3. Création d'un poste et association de la compétence
                var p = new Poste { Code = "P-CHG", Intitule = "Chef de projet Transformation", EffectifRequis = 1, Actif = true };
                await organigrammeService.EnregistrerPosteAsync(p);

                var pc = new PosteCompetence
                {
                    PosteId = p.Id,
                    CompetenceId = nouvelle.Id,
                    NiveauMaitriseRequis = NiveauMaitrise.Confirme, // 3
                    EstObligatoire = true
                };
                await organigrammeService.EnregistrerCompetencePosteAsync(pc);

                // 4. Relecture des compétences du poste
                var compsPoste = await organigrammeService.ChargerCompetencesPosteAsync(p.Id);
                Assert.That(compsPoste.Count, Is.EqualTo(1));
                Assert.That(compsPoste[0].CompetenceCode, Is.EqualTo("COMP-LEAD-INNOV"));
                Assert.That(compsPoste[0].NiveauMaitriseRequis, Is.EqualTo(NiveauMaitrise.Confirme));
                Assert.That(compsPoste[0].NiveauEtoiles, Is.EqualTo("★★★☆"));
                Assert.That(compsPoste[0].EstObligatoire, Is.True);

                // 5. Suppression de la compétence du poste
                await organigrammeService.SupprimerCompetencePosteAsync(p.Id, nouvelle.Id);
                var compsApresSuppr = await organigrammeService.ChargerCompetencesPosteAsync(p.Id);
                Assert.That(compsApresSuppr.Count, Is.EqualTo(0));
            }
            finally
            {
                try { if (File.Exists(tempDb)) File.Delete(tempDb); } catch { }
            }
        }

        [Test]
        [Microsoft.VisualStudio.TestTools.UnitTesting.TestMethod]
        public async Task ObtenirSalaireMinConventionnel_InterrogeGrilleSalariale()
        {
            string tempDb = Path.Combine(Path.GetTempPath(), $"test_salmin_{Guid.NewGuid():N}.db");
            try
            {
                var sqlite = new SQLiteService(tempDb);
                var fichePosteService = new FichePosteService(sqlite);

                // Insérer une ligne dans GrillesSalaires (convention 99, Cat 3B, Ech 2 -> 125 000 FCFA = 12 500 000 centimes)
                using (var conn = sqlite.CreateConnection())
                {
                    using (var cmd = conn.CreateCommand())
                    {
                        cmd.CommandText = @"
                            INSERT INTO GrillesSalaires (ConventionId, Categorie, Echelon, SalaireMinimum)
                            VALUES (99, '3B', '2', 12500000);";
                        cmd.ExecuteNonQuery();
                    }
                }

                var salMin = await fichePosteService.ObtenirSalaireMinConventionnelAsync(99, "3B", "2");
                Assert.That(salMin > 0m, Is.True);
                Assert.That(salMin, Is.EqualTo(125000m));

                // Échelon non existant
                var inexistant = await fichePosteService.ObtenirSalaireMinConventionnelAsync(99, "INEXISTANTE", "99");
                Assert.That(inexistant, Is.EqualTo(0m));
            }
            finally
            {
                try { if (File.Exists(tempDb)) File.Delete(tempDb); } catch { }
            }
        }

        [Test]
        [Microsoft.VisualStudio.TestTools.UnitTesting.TestMethod]
        public async Task ExportsFichePoste_PdfEtExcel_GenerentFichiersValides()
        {
            string tempDb = Path.Combine(Path.GetTempPath(), $"test_exp_{Guid.NewGuid():N}.db");
            string tempPdf = Path.Combine(Path.GetTempPath(), $"fiche_test_{Guid.NewGuid():N}.pdf");
            string tempXlsx = Path.Combine(Path.GetTempPath(), $"synthese_test_{Guid.NewGuid():N}.xlsx");

            try
            {
                var sqlite = new SQLiteService(tempDb);
                var organigrammeService = new OrganigrammeService(sqlite);
                var fichePosteService = new FichePosteService(sqlite);
                var exportService = new FichePosteExportService();

                var dg = new UniteOrganisationnelle { Code = "DG", Libelle = "Direction Générale", TypeUnite = TypeUniteOrganisationnelle.DirectionGenerale };
                await organigrammeService.EnregistrerUniteAsync(dg);

                var p = new Poste
                {
                    Code = "PST-FIN-01",
                    Intitule = "Responsable Administratif et Financier",
                    UniteId = dg.Id,
                    FinalitePrincipale = "Assurer la conformité comptable, fiscale et financière de l'entreprise.",
                    ActivitesCles = "1. Clôture des comptes mensuels\n2. Déclarations fiscales IUTS/TPA\n3. Suivi de trésorerie.",
                    NiveauEtudeRequis = "Bac+5",
                    DiplomeRequis = "Master Comptabilité Contrôle Audit",
                    ExperienceMinimaleAnnees = 5,
                    ConditionsTravail = "Déplacements ponctuels dans les agences régionales.",
                    ConventionLibelle = "Commerce et Industrie",
                    CategorieConventionnelle = "Cadre C1",
                    EchelonConventionnel = "1",
                    SalaireMinimumConventionnel = 250000,
                    SalaireMinBudgete = 300000,
                    SalaireMaxBudgete = 450000,
                    EffectifRequis = 1,
                    Actif = true
                };
                await organigrammeService.EnregistrerPosteAsync(p);

                // Compétence
                var comp = (await fichePosteService.ChargerToutesCompetencesAsync()).First();
                await organigrammeService.EnregistrerCompetencePosteAsync(new PosteCompetence
                {
                    PosteId = p.Id,
                    CompetenceId = comp.Id,
                    NiveauMaitriseRequis = NiveauMaitrise.Expert,
                    EstObligatoire = true
                });

                var compPoste = await organigrammeService.ChargerCompetencesPosteAsync(p.Id);
                p.CompetencesRequises = new System.Collections.ObjectModel.ObservableCollection<PosteCompetence>(compPoste);
                p.TitulairesActuels = new System.Collections.ObjectModel.ObservableCollection<TitulairePosteLigne>
                {
                    new TitulairePosteLigne { Matricule = "MAT001", NomComplet = "Moussa OUEDRAOGO", DateEmbaucheTexte = "01/02/2022", TypeContrat = "CDI" }
                };

                var dossier = new Dossier
                {
                    RaisonSociale = "ENTREPRISE BURKINABE SA",
                    Sigle = "EBSA",
                    IFU = "0001234567A",
                    RCCM = "BF-OUA-2024-B-1234",
                    Ville = "Ouagadougou"
                };

                // 1. Export PDF de la fiche de poste
                exportService.GenererFichePostePdf(p, dossier, tempPdf);
                Assert.That(File.Exists(tempPdf), Is.True);
                Assert.That(new FileInfo(tempPdf).Length, Is.GreaterThan(1000));

                // 2. Export Excel de la synthèse GPEC
                var syntheses = await fichePosteService.CalculerSyntheseBudgetaireEffectifsAsync();
                var toutesComp = await fichePosteService.ChargerToutesCompetencesAsync();

                exportService.ExporterSyntheseExcel(syntheses, toutesComp, new List<PosteCompetence>(), dossier.RaisonSociale, tempXlsx);
                Assert.That(File.Exists(tempXlsx), Is.True);
                Assert.That(new FileInfo(tempXlsx).Length, Is.GreaterThan(1000));
            }
            finally
            {
                try { if (File.Exists(tempDb)) File.Delete(tempDb); } catch { }
                try { if (File.Exists(tempPdf)) File.Delete(tempPdf); } catch { }
                try { if (File.Exists(tempXlsx)) File.Delete(tempXlsx); } catch { }
            }
        }
    }
}
