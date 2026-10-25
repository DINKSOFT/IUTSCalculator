using System;
using NUnit.Framework;
using IUTSCalculator.Models;
using IUTSCalculator.Services;

namespace IUTSCalculator.Tests
{
    [TestFixture]
    [Microsoft.VisualStudio.TestTools.UnitTesting.TestClass]
    public class SoldeToutCompteTests
    {
        [Test]
        [Microsoft.VisualStudio.TestTools.UnitTesting.TestMethod]
        public void CalculerIndemniteLicenciementLegal_MoinsDe1An_RetourneZero()
        {
            // Moins d'un an d'ancienneté : pas d'indemnité de licenciement (Art. 35 CCI-BF)
            decimal res = SoldeToutCompteService.CalculerIndemniteLicenciementLegal(
                salaireMoyen: 250_000m,
                ancienneteAnnees: 0.8m,
                motif: MotifDepart.LicenciementPersonnel);

            Assert.That(res, Is.EqualTo(0m));
        }

        [Test]
        [Microsoft.VisualStudio.TestTools.UnitTesting.TestMethod]
        public void CalculerIndemniteLicenciementLegal_3Ans_Tranche1Seule()
        {
            // 3 ans : 3 × (25 % × 200 000) = 3 × 50 000 = 150 000 FCFA
            decimal res = SoldeToutCompteService.CalculerIndemniteLicenciementLegal(
                salaireMoyen: 200_000m,
                ancienneteAnnees: 3.0m,
                motif: MotifDepart.LicenciementPersonnel);

            Assert.That(res, Is.EqualTo(150_000m));
        }

        [Test]
        [Microsoft.VisualStudio.TestTools.UnitTesting.TestMethod]
        public void CalculerIndemniteLicenciementLegal_7Ans_Tranches1Et2()
        {
            // 7 ans :
            // Tranche 1 (1 à 5 ans = 5 ans) : 5 × (25 % × 300 000) = 375 000
            // Tranche 2 (6 à 7 ans = 2 ans) : 2 × (30 % × 300 000) = 180 000
            // Total = 555 000 FCFA
            decimal res = SoldeToutCompteService.CalculerIndemniteLicenciementLegal(
                salaireMoyen: 300_000m,
                ancienneteAnnees: 7.0m,
                motif: MotifDepart.LicenciementEconomique);

            Assert.That(res, Is.EqualTo(555_000m));
        }

        [Test]
        [Microsoft.VisualStudio.TestTools.UnitTesting.TestMethod]
        public void CalculerIndemniteLicenciementLegal_12AnsEtDemi_Tranches1_2_Et_3()
        {
            // 12.5 ans :
            // Tranche 1 (5 ans) : 5 × (25 % × 400 000) = 500 000
            // Tranche 2 (5 ans) : 5 × (30 % × 400 000) = 600 000
            // Tranche 3 (2.5 ans) : 2.5 × (40 % × 400 000) = 400 000
            // Total = 1 500 000 FCFA
            decimal res = SoldeToutCompteService.CalculerIndemniteLicenciementLegal(
                salaireMoyen: 400_000m,
                ancienneteAnnees: 12.5m,
                motif: MotifDepart.LicenciementPersonnel);

            Assert.That(res, Is.EqualTo(1_500_000m));
        }

        [Test]
        [Microsoft.VisualStudio.TestTools.UnitTesting.TestMethod]
        public void CalculerIndemniteLicenciementLegal_FauteLourde_RetourneZero()
        {
            // Faute lourde privative de toute indemnité de rupture (Art. 35 CCI-BF)
            decimal res = SoldeToutCompteService.CalculerIndemniteLicenciementLegal(
                salaireMoyen: 500_000m,
                ancienneteAnnees: 10m,
                motif: MotifDepart.LicenciementFauteLourde);

            Assert.That(res, Is.EqualTo(0m));
        }

        [Test]
        [Microsoft.VisualStudio.TestTools.UnitTesting.TestMethod]
        public void CalculerIndemniteLicenciementLegal_Demission_RetourneZero()
        {
            // Démission volontaire ordinaire : pas d'indemnité de licenciement
            decimal res = SoldeToutCompteService.CalculerIndemniteLicenciementLegal(
                salaireMoyen: 350_000m,
                ancienneteAnnees: 6m,
                motif: MotifDepart.Demission);

            Assert.That(res, Is.EqualTo(0m));
        }

        [Test]
        [Microsoft.VisualStudio.TestTools.UnitTesting.TestMethod]
        public void CalculerIndemniteLicenciementLegal_RetraiteDepart_DemiIndemnite()
        {
            // Départ volontaire à la retraite : 50 % de l'indemnité légale de licenciement
            // Base pour 4 ans à 200 000 : 4 × 50 000 = 200 000 -> Retraite = 100 000 FCFA
            decimal res = SoldeToutCompteService.CalculerIndemniteLicenciementLegal(
                salaireMoyen: 200_000m,
                ancienneteAnnees: 4.0m,
                motif: MotifDepart.RetraiteDepart);

            Assert.That(res, Is.EqualTo(100_000m));
        }

        [Test]
        [Microsoft.VisualStudio.TestTools.UnitTesting.TestMethod]
        public void CalculerDureePreavisMois_Cadre_Toujours3Mois()
        {
            // Cadres et assimilés : préavis de 3 mois quel que soit l'ancienneté (Art. 30 CCI-BF)
            decimal preavis1 = SoldeToutCompteService.CalculerDureePreavisMois(isCadre: true, ancienneteAnnees: 0.5m);
            decimal preavis10 = SoldeToutCompteService.CalculerDureePreavisMois(isCadre: true, ancienneteAnnees: 10m);

            Assert.That(preavis1, Is.EqualTo(3.0m));
            Assert.That(preavis10, Is.EqualTo(3.0m));
        }

        [Test]
        [Microsoft.VisualStudio.TestTools.UnitTesting.TestMethod]
        public void CalculerDureePreavisMois_EmployeNonCadre_1MoisSansPalier()
        {
            // Non-cadres payés au mois : 1 mois, quelle que soit l'ancienneté (CCI-BF art. 35 ;
            // loi n° 013-2026/ALP, art. 76). L'ancien palier de 2 mois à 5 ans n'existe pas.
            decimal preavisMoins5 = SoldeToutCompteService.CalculerDureePreavisMois(isCadre: false, ancienneteAnnees: 3m);
            decimal preavisPlus5 = SoldeToutCompteService.CalculerDureePreavisMois(isCadre: false, ancienneteAnnees: 5m);
            decimal preavisPlus8 = SoldeToutCompteService.CalculerDureePreavisMois(isCadre: false, ancienneteAnnees: 8m);

            Assert.That(preavisMoins5, Is.EqualTo(1.0m));
            Assert.That(preavisPlus5, Is.EqualTo(1.0m));
            Assert.That(preavisPlus8, Is.EqualTo(1.0m));
        }

        [Test]
        [Microsoft.VisualStudio.TestTools.UnitTesting.TestMethod]
        public void DeterminerStatutPreavisDefaut_RetourneReglesLegales()
        {
            Assert.That(SoldeToutCompteService.DeterminerStatutPreavisDefaut(MotifDepart.Demission), Is.EqualTo(StatutPreavis.Effectue));
            Assert.That(SoldeToutCompteService.DeterminerStatutPreavisDefaut(MotifDepart.LicenciementFauteLourde), Is.EqualTo(StatutPreavis.Inapplicable));
            Assert.That(SoldeToutCompteService.DeterminerStatutPreavisDefaut(MotifDepart.LicenciementEconomique), Is.EqualTo(StatutPreavis.DispensePaye));
            Assert.That(SoldeToutCompteService.DeterminerStatutPreavisDefaut(MotifDepart.Deces), Is.EqualTo(StatutPreavis.Inapplicable));
        }

        [Test]
        [Microsoft.VisualStudio.TestTools.UnitTesting.TestMethod]
        public void SoldeToutCompte_TotauxEtExonérations_SontConformesArt106CGI()
        {
            var stc = new SoldeToutCompte
            {
                SalairePresence = 150_000m,
                IndemniteConges = 50_000m,
                IndemnitePreavis = 200_000m,
                IndemniteLicenciement = 300_000m, // Exonérée IUTS & CNSS (Art. 106 CGI)
                Indemnite13emeMois = 25_000m,
                IndemnitesDiverses = 0m,
                DeductionsPrets = 40_000m,
                DeductionsAutres = 10_000m,
                EstimationCNSS = 22_000m,
                EstimationIUTS = 28_000m
            };

            // Brut total = 150k + 50k + 200k + 300k + 25k = 725k
            Assert.That(stc.TotalBrut, Is.EqualTo(725_000m));

            // Exonéré (indemnité licenciement) = 300k
            Assert.That(stc.TotalExonere, Is.EqualTo(300_000m));

            // Imposable = TotalBrut - TotalExonere = 425k
            Assert.That(stc.TotalImposable, Is.EqualTo(425_000m));

            // Total déductions = 22k (CNSS) + 28k (IUTS) + 40k (Prêts) + 10k (Autres) = 100k
            Assert.That(stc.TotalRetenues, Is.EqualTo(100_000m));

            // Total Net = TotalBrut - TotalRetenues = 725k - 100k = 625k
            Assert.That(stc.TotalNet, Is.EqualTo(625_000m));
        }

        [Test]
        [Microsoft.VisualStudio.TestTools.UnitTesting.TestMethod]
        public void ExportDocumentsLegaux_UtiliseRaisonSocialeDossierCourant()
        {
            var dossierTest = new Dossier
            {
                DossierId = "TEST_DOSSIER_01",
                RaisonSociale = "ENTREPRISE COMMERCIALE DU BURKINA SA",
                IFU = "00123456A",
                Adresse = "01 BP 1234 Ouagadougou 01",
                Ville = "Bobo-Dioulasso"
            };
            DossierSession.Instance.Open(dossierTest);

            var stc = new SoldeToutCompte
            {
                EmployeeId = "EMP001",
                NomSalarie = "OUEDRAOGO Jean",
                Fonction = "Comptable",
                Categorie = "Cadre Moyen",
                Echelon = "A",
                DateEntree = new DateTime(2020, 1, 1),
                DateSortie = new DateTime(2024, 6, 30),
                Motif = MotifDepart.Demission,
                SalairePresence = 150_000m,
                IndemniteConges = 50_000m
            };

            var exportService = new SoldeToutCompteExportService();
            string tempPdf = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"test_recu_{Guid.NewGuid():N}.pdf");
            string tempCert = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"test_cert_{Guid.NewGuid():N}.pdf");
            string tempAttest = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"test_attest_{Guid.NewGuid():N}.pdf");
            string tempExcel = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"test_stc_{Guid.NewGuid():N}.xlsx");

            try
            {
                // Test génération reçu PDF sans passer raisonSociale -> utilise le dossier courant
                bool recuOk = exportService.GenererRecuPdf(stc, tempPdf);
                Assert.That(recuOk, Is.True);
                Assert.That(System.IO.File.Exists(tempPdf), Is.True);

                // Test génération certificat travail PDF sans passer raisonSociale -> utilise le dossier courant
                bool certOk = exportService.GenererCertificatTravailPdf(stc, tempCert);
                Assert.That(certOk, Is.True);
                Assert.That(System.IO.File.Exists(tempCert), Is.True);

                // Test génération attestation travail PDF sans passer raisonSociale -> utilise le dossier courant
                bool attestOk = exportService.GenererAttestationTravailPdf(stc, tempAttest);
                Assert.That(attestOk, Is.True);
                Assert.That(System.IO.File.Exists(tempAttest), Is.True);

                // Test génération Excel sans passer raisonSociale -> utilise le dossier courant
                string excelPath = exportService.ExporterExcel(stc, tempExcel);
                Assert.That(System.IO.File.Exists(excelPath), Is.True);
            }
            finally
            {
                DossierSession.Instance.Close();
                try { if (System.IO.File.Exists(tempPdf)) System.IO.File.Delete(tempPdf); } catch { }
                try { if (System.IO.File.Exists(tempCert)) System.IO.File.Delete(tempCert); } catch { }
                try { if (System.IO.File.Exists(tempAttest)) System.IO.File.Delete(tempAttest); } catch { }
                try { if (System.IO.File.Exists(tempExcel)) System.IO.File.Delete(tempExcel); } catch { }
            }
        }
    }
}
