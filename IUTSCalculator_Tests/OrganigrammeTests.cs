using System;
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
    public class OrganigrammeTests
    {
        [Test]
        [Microsoft.VisualStudio.TestTools.UnitTesting.TestMethod]
        public async Task EnregistrerUniteAsync_EmpecheCycleHierarchique()
        {
            // Arborescence de test :
            // DG (racine)
            //   └── DAF
            //         └── Service Comptabilité
            // DRH (racine indépendante, utilisée pour le cas de rattachement valide)

            string tempDb = Path.Combine(Path.GetTempPath(), $"test_org_{Guid.NewGuid():N}.db");
            try
            {
                var sqlite = new SQLiteService(tempDb);
                var service = new OrganigrammeService(sqlite);

                var dg = new UniteOrganisationnelle { Code = "DG", Libelle = "Direction Générale", TypeUnite = TypeUniteOrganisationnelle.DirectionGenerale };
                await service.EnregistrerUniteAsync(dg);

                var daf = new UniteOrganisationnelle { Code = "DAF", Libelle = "Direction Admin & Financière", TypeUnite = TypeUniteOrganisationnelle.Direction, ParentId = dg.Id };
                await service.EnregistrerUniteAsync(daf);

                var srvCompta = new UniteOrganisationnelle { Code = "COMPTA", Libelle = "Service Comptabilité", TypeUnite = TypeUniteOrganisationnelle.Service, ParentId = daf.Id };
                await service.EnregistrerUniteAsync(srvCompta);

                var drh = new UniteOrganisationnelle { Code = "DRH", Libelle = "Direction RH", TypeUnite = TypeUniteOrganisationnelle.Direction };
                await service.EnregistrerUniteAsync(drh);

                // 1. Rattacher DRH sous COMPTA -> rattachement valide, aucun cycle
                drh.ParentId = srvCompta.Id;
                Assert.DoesNotThrowAsync(async () => await service.EnregistrerUniteAsync(drh));

                // 2. Tenter de faire de COMPTA le parent de DG -> COMPTA est une sous-unité de DG
                //    (via DAF) : cycle détecté, l'enregistrement doit être refusé.
                dg.ParentId = srvCompta.Id;
                Assert.ThrowsAsync<InvalidOperationException>(async () => await service.EnregistrerUniteAsync(dg));

                // 3. Tenter de faire de DAF le parent de DG -> cycle détecté également.
                dg.ParentId = daf.Id;
                Assert.ThrowsAsync<InvalidOperationException>(async () => await service.EnregistrerUniteAsync(dg));

                // 4. Tenter de faire de DG son propre parent -> cas explicitement rejeté.
                dg.ParentId = dg.Id;
                Assert.ThrowsAsync<InvalidOperationException>(async () => await service.EnregistrerUniteAsync(dg));
            }
            finally
            {
                try { if (File.Exists(tempDb)) File.Delete(tempDb); } catch { }
            }
        }

        [Test]
        [Microsoft.VisualStudio.TestTools.UnitTesting.TestMethod]
        public async Task EnregistrerPosteAsync_EmpecheCycleSuperviseurNPlus1()
        {
            // Arbre hiérarchique de postes :
            // Poste DG (Supérieur : aucun)
            //   └── Poste Directeur Financier (Supérieur : DG)
            //         └── Poste Comptable Principal (Supérieur : Directeur Financier)
            // Poste Indépendant (Supérieur : aucun, utilisé pour le cas de rattachement valide)

            string tempDb = Path.Combine(Path.GetTempPath(), $"test_org_poste_{Guid.NewGuid():N}.db");
            try
            {
                var sqlite = new SQLiteService(tempDb);
                var service = new OrganigrammeService(sqlite);

                var pDg = new Poste { Code = "P-DG", Intitule = "Directeur Général" };
                await service.EnregistrerPosteAsync(pDg);

                var pDaf = new Poste { Code = "P-DAF", Intitule = "Directeur Financier", SuperieurPosteId = pDg.Id };
                await service.EnregistrerPosteAsync(pDaf);

                var pComptable = new Poste { Code = "P-CPT", Intitule = "Comptable Principal", SuperieurPosteId = pDaf.Id };
                await service.EnregistrerPosteAsync(pComptable);

                var pIndependant = new Poste { Code = "P-IND", Intitule = "Poste Indépendant" };
                await service.EnregistrerPosteAsync(pIndependant);

                // 1. Rattacher le poste indépendant sous le Comptable Principal -> rattachement valide.
                pIndependant.SuperieurPosteId = pComptable.Id;
                Assert.DoesNotThrowAsync(async () => await service.EnregistrerPosteAsync(pIndependant));

                // 2. Affecter le Directeur Général sous le Comptable Principal -> cycle !
                pDg.SuperieurPosteId = pComptable.Id;
                Assert.ThrowsAsync<InvalidOperationException>(async () => await service.EnregistrerPosteAsync(pDg));

                // 3. Affecter le Directeur Général sous le Directeur Financier -> cycle également.
                pDg.SuperieurPosteId = pDaf.Id;
                Assert.ThrowsAsync<InvalidOperationException>(async () => await service.EnregistrerPosteAsync(pDg));

                // 4. Affecter un poste sous lui-même -> cas explicitement rejeté.
                pDg.SuperieurPosteId = pDg.Id;
                Assert.ThrowsAsync<InvalidOperationException>(async () => await service.EnregistrerPosteAsync(pDg));
            }
            finally
            {
                try { if (File.Exists(tempDb)) File.Delete(tempDb); } catch { }
            }
        }

        [Test]
        [Microsoft.VisualStudio.TestTools.UnitTesting.TestMethod]
        public async Task ChargerArbreOrganigrammeAsync_CalculeEffectifsEtHierarchieCorrectement()
        {
            // L'effectif d'une unité est calculé par IUTSCalculator.Services.OrganigrammeService
            // à partir des salariés dont le champ Service correspond exactement au libellé de
            // l'unité (et non via un lien direct par Id) — voir ChargerUnitesAsync. L'ordre de
            // tri des unités (Ordre ASC) doit placer chaque parent avant ses enfants pour que le
            // calcul du niveau hiérarchique (Niveau) soit correct ; d'où les valeurs d'Ordre
            // explicites ci-dessous.
            //
            // Structure :
            // DG (Ordre 1)
            //   ├── DAF (Ordre 2)
            //   │     └── Service Comptabilité (Ordre 3)
            //   └── DRH (Ordre 4)

            string tempDb = Path.Combine(Path.GetTempPath(), $"test_org_arbre_{Guid.NewGuid():N}.db");
            try
            {
                var sqlite = new SQLiteService(tempDb);
                var service = new OrganigrammeService(sqlite);
                var dataService = new DataService(tempDb);

                var uDg = new UniteOrganisationnelle { Code = "DG", Libelle = "Direction Générale", TypeUnite = TypeUniteOrganisationnelle.DirectionGenerale, Ordre = 1 };
                await service.EnregistrerUniteAsync(uDg);

                var uDaf = new UniteOrganisationnelle { Code = "DAF", Libelle = "Direction Financière", TypeUnite = TypeUniteOrganisationnelle.Direction, ParentId = uDg.Id, Ordre = 2 };
                await service.EnregistrerUniteAsync(uDaf);

                var uCompta = new UniteOrganisationnelle { Code = "COMPTA", Libelle = "Comptabilité", TypeUnite = TypeUniteOrganisationnelle.Service, ParentId = uDaf.Id, Ordre = 3 };
                await service.EnregistrerUniteAsync(uCompta);

                var uDrh = new UniteOrganisationnelle { Code = "DRH", Libelle = "Direction RH", TypeUnite = TypeUniteOrganisationnelle.Direction, ParentId = uDg.Id, Ordre = 4 };
                await service.EnregistrerUniteAsync(uDrh);

                // Salariés fictifs, rattachés par le champ Service (les libellés doivent
                // correspondre exactement à ceux des unités ci-dessus).
                await dataService.AddEmployeeAsync(new Employee { EmployeeId = "EMP01", FirstName = "Patrice", LastName = "KABORE", Service = "Direction Générale", StatutSalarie = 0 });
                await dataService.AddEmployeeAsync(new Employee { EmployeeId = "EMP02", FirstName = "Aminata", LastName = "SAWADOGO", Service = "Direction Financière", StatutSalarie = 0 });
                await dataService.AddEmployeeAsync(new Employee { EmployeeId = "EMP03", FirstName = "Salif", LastName = "OUEDRAOGO", Service = "Comptabilité", StatutSalarie = 0 });
                await dataService.AddEmployeeAsync(new Employee { EmployeeId = "EMP04", FirstName = "Mariam", LastName = "ZONGO", Service = "Comptabilité", StatutSalarie = 0 });
                await dataService.AddEmployeeAsync(new Employee { EmployeeId = "EMP05", FirstName = "Ibrahim", LastName = "TRAORE", Service = "Direction RH", StatutSalarie = 0 });

                var arbre = await service.ChargerArbreOrganigrammeAsync();

                // Il doit y avoir 1 seule racine au niveau 0 : DG
                Assert.That(arbre.Count, Is.EqualTo(1));
                var racineDg = arbre[0];
                Assert.That(racineDg.Code, Is.EqualTo("DG"));
                Assert.That(racineDg.Niveau, Is.EqualTo(0));
                Assert.That(racineDg.Enfants.Count, Is.EqualTo(2)); // DAF et DRH

                // Effectif direct DG = 1 (DG)
                Assert.That(racineDg.EffectifDirect, Is.EqualTo(1));

                // Effectif total arbre sous DG = 5
                Assert.That(racineDg.EffectifTotal, Is.EqualTo(5));

                // Vérification branche DAF
                var noeudDaf = racineDg.Enfants.First(e => e.Code == "DAF");
                Assert.That(noeudDaf.Niveau, Is.EqualTo(1));
                Assert.That(noeudDaf.EffectifDirect, Is.EqualTo(1)); // DAF elle-même
                Assert.That(noeudDaf.EffectifTotal, Is.EqualTo(3));  // DAF + 2 Comptables

                // Vérification service Compta
                var noeudCompta = noeudDaf.Enfants.First(e => e.Code == "COMPTA");
                Assert.That(noeudCompta.Niveau, Is.EqualTo(2));
                Assert.That(noeudCompta.EffectifDirect, Is.EqualTo(2));
                Assert.That(noeudCompta.EffectifTotal, Is.EqualTo(2));
            }
            finally
            {
                try { if (File.Exists(tempDb)) File.Delete(tempDb); } catch { }
            }
        }

        [Test]
        [Microsoft.VisualStudio.TestTools.UnitTesting.TestMethod]
        public async Task InitialiserStructureDefautAsync_CreeStructureStandardBurkinabe()
        {
            string tempDb = Path.Combine(Path.GetTempPath(), $"test_org_init_{Guid.NewGuid():N}.db");
            try
            {
                var sqlite = new SQLiteService(tempDb);
                var service = new OrganigrammeService(sqlite);

                // Sur un dossier sans aucun salarié, InitialiserStructureDefautAsync génère
                // l'ossature standard d'entreprise burkinabè (DG, DAF, DRH, DTECH, DCOM + services).
                await service.InitialiserStructureDefautAsync();

                var unites = await service.ChargerUnitesAsync();
                var postes = await service.ChargerPostesAsync();

                Assert.That(unites.Count, Is.GreaterThanOrEqualTo(6));
                Assert.That(postes.Count, Is.GreaterThanOrEqualTo(4));

                // DG doit être racine
                var dg = unites.FirstOrDefault(u => u.Code == "DG");
                Assert.That(dg, Is.Not.Null);
                Assert.That(dg.ParentId, Is.Null);
                Assert.That(dg.TypeUnite, Is.EqualTo(TypeUniteOrganisationnelle.DirectionGenerale));

                // Le Directeur Général doit avoir le poste POSTE-DG sans supérieur hiérarchique
                var pDg = postes.FirstOrDefault(p => p.Code == "POSTE-DG");
                Assert.That(pDg, Is.Not.Null);
                Assert.That(pDg.SuperieurPosteId, Is.Null);

                // Le Responsable Paie & RH doit dépendre du Directeur des Ressources Humaines
                var pPaie = postes.FirstOrDefault(p => p.Code == "POSTE-PAIE");
                var pDrh = postes.FirstOrDefault(p => p.Code == "POSTE-DRH");
                Assert.That(pPaie, Is.Not.Null);
                Assert.That(pDrh, Is.Not.Null);
                Assert.That(pPaie.SuperieurPosteId, Is.EqualTo(pDrh.Id));
            }
            finally
            {
                try { if (File.Exists(tempDb)) File.Delete(tempDb); } catch { }
            }
        }

        [Test]
        [Microsoft.VisualStudio.TestTools.UnitTesting.TestMethod]
        public async Task ExporterExcelEtPdf_GenerentFichiersValides()
        {
            var dossier = new Dossier
            {
                DossierId = "TEST_DOSSIER_RH",
                RaisonSociale = "SOCIETE COMMERCIALE DU FASO SA",
                IFU = "00098765B",
                RCCM = "BF-OUA-2020-B-1234",
                Ville = "Ouagadougou"
            };
            DossierSession.Instance.Open(dossier);

            string tempDb = Path.Combine(Path.GetTempPath(), $"test_org_exp_{Guid.NewGuid():N}.db");
            string tempExcel = Path.Combine(Path.GetTempPath(), $"test_org_exp_{Guid.NewGuid():N}.xlsx");
            string tempPdf = Path.Combine(Path.GetTempPath(), $"test_org_exp_{Guid.NewGuid():N}.pdf");

            try
            {
                var sqlite = new SQLiteService(tempDb);
                var service = new OrganigrammeService(sqlite);
                await service.InitialiserStructureDefautAsync();

                var unites = await service.ChargerUnitesAsync();
                var postes = await service.ChargerPostesAsync();
                var affectations = await service.ChargerAffectationsSalariesAsync();

                var exportService = new OrganigrammeExportService();

                // Test Excel
                string excelPath = exportService.ExporterExcel(unites, postes, affectations, dossier.RaisonSociale, dossier.IFU, tempExcel);
                Assert.That(File.Exists(excelPath), Is.True);
                var fiExcel = new FileInfo(excelPath);
                Assert.That(fiExcel.Length, Is.GreaterThan(1000));

                // Test PDF
                string pdfPath = exportService.ExporterPdf(unites, postes, dossier.RaisonSociale, dossier.IFU, dossier.RCCM, tempPdf);
                Assert.That(File.Exists(pdfPath), Is.True);
                var fiPdf = new FileInfo(pdfPath);
                Assert.That(fiPdf.Length, Is.GreaterThan(1000));
            }
            finally
            {
                DossierSession.Instance.Close();
                try { if (File.Exists(tempDb)) File.Delete(tempDb); } catch { }
                try { if (File.Exists(tempExcel)) File.Delete(tempExcel); } catch { }
                try { if (File.Exists(tempPdf)) File.Delete(tempPdf); } catch { }
            }
        }
    }
}
