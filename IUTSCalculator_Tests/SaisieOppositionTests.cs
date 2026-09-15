using System;
using System.Collections.Generic;
using NUnit.Framework;
using IUTSCalculator.Models;
using IUTSCalculator.Services;

namespace IUTSCalculator.Tests
{
    [TestFixture]
    [Microsoft.VisualStudio.TestTools.UnitTesting.TestClass]
    public class SaisieOppositionTests
    {
        [Test]
        [Microsoft.VisualStudio.TestTools.UnitTesting.TestMethod]
        public void CalculerQuotiteSaisissable_NetInferieurAuSMIG_QuotiteOrdinaireZero()
        {
            // Net = 40 000 FCFA, inférieur au SMIG (45 000 FCFA) : insaisissable pour créances ordinaires
            var res = SaisieOppositionService.CalculerQuotiteSaisissable(40_000m, smig: 45_000m);

            Assert.That(res.QuotiteSaisissableLegale, Is.EqualTo(0m));
            Assert.That(res.MontantDisponiblePensionAlimentaire, Is.EqualTo(0m));
            Assert.That(res.FractionInsaisissable, Is.EqualTo(40_000m));
        }

        [Test]
        [Microsoft.VisualStudio.TestTools.UnitTesting.TestMethod]
        public void CalculerQuotiteSaisissable_Tranche1Seule_5Pourcent()
        {
            // Net = 25 000 FCFA (dans tranche 1 : 0 à 30 000 à 1/20e = 5%)
            // Si SMIG abaissé pour le test à 20 000 FCFA
            var res = SaisieOppositionService.CalculerQuotiteSaisissable(25_000m, smig: 20_000m);

            // 25 000 * 5% = 1 250 FCFA
            Assert.That(res.QuotiteSaisissableLegale, Is.EqualTo(1_250m));
        }

        [Test]
        [Microsoft.VisualStudio.TestTools.UnitTesting.TestMethod]
        public void CalculerQuotiteSaisissable_100000FCFA_Tranches1_2_Et_3()
        {
            // Net = 100 000 FCFA
            // Tranche 1 (0 à 30 000) : 30 000 * 5% = 1 500
            // Tranche 2 (30 000 à 60 000) : 30 000 * 10% = 3 000
            // Tranche 3 (60 000 à 100 000) : 40 000 * 20% = 8 000
            // Total barème = 12 500 FCFA
            // Disponible après SMIG 45 000 = 55 000 >= 12 500
            var res = SaisieOppositionService.CalculerQuotiteSaisissable(100_000m, smig: 45_000m);

            Assert.That(res.QuotiteSaisissableLegale, Is.EqualTo(12_500m));
            Assert.That(res.FractionInsaisissable, Is.EqualTo(87_500m));
            Assert.That(res.MontantDisponiblePensionAlimentaire, Is.EqualTo(55_000m));
        }

        [Test]
        [Microsoft.VisualStudio.TestTools.UnitTesting.TestMethod]
        public void CalculerQuotiteSaisissable_HautSalaire_500000FCFA()
        {
            // Net = 500 000 FCFA
            // T1 (30k @ 5%) = 1 500
            // T2 (30k @ 10%) = 3 000
            // T3 (40k @ 20%) = 8 000
            // T4 (50k @ 25%) = 12 500
            // T5 (100k @ 1/3) = 33 333
            // T6 (150k @ 50%) = 75 000
            // T7 (100k surplus @ 100%) = 100 000
            // Total = 233 333 FCFA
            var res = SaisieOppositionService.CalculerQuotiteSaisissable(500_000m, smig: 45_000m);

            Assert.That(res.QuotiteSaisissableLegale, Is.EqualTo(233_333m));
            Assert.That(res.FractionInsaisissable, Is.EqualTo(500_000m - 233_333m));
        }

        [Test]
        [Microsoft.VisualStudio.TestTools.UnitTesting.TestMethod]
        public void RepartirSaisies_PensionAlimentaireSuperPrivilege_AbsorbeQuotiteOrdinaire()
        {
            // Salaire net = 100 000 FCFA -> Quotité légale ordinaire = 12 500 FCFA, Disponible pension = 55 000 FCFA
            var pension = new SaisieOpposition
            {
                Id = 1,
                TypeSaisie = TypeSaisieOpposition.PensionAlimentaire,
                Creancier = "Mme Ouédraogo pour enfant mineur",
                MontantTotalDu = 0m, // Illimitée
                RetenueMensuelleFixee = 25_000m,
                Statut = StatutSaisieOpposition.EnCours
            };

            var saisieOrdinaire = new SaisieOpposition
            {
                Id = 2,
                TypeSaisie = TypeSaisieOpposition.SaisieArretJudiciaire,
                Creancier = "Banque Commerciale",
                MontantTotalDu = 500_000m,
                RetenueMensuelleFixee = 20_000m,
                Statut = StatutSaisieOpposition.EnCours
            };

            var res = SaisieOppositionService.RepartirSaisies(100_000m, new[] { pension, saisieOrdinaire }, smig: 45_000m);

            // 1. La pension alimentaire prend la totalité de ses 25 000 FCFA
            Assert.That(res.RetenuePensionAlimentaire, Is.EqualTo(25_000m));

            // 2. La pension ayant dépassé la quotité légale ordinaire (25 000 >= 12 500),
            // il reste 0 FCFA pour le créancier ordinaire ce mois-ci.
            Assert.That(res.QuotiteOrdinaireRestante, Is.EqualTo(0m));
            Assert.That(res.RetenuesOrdinairesTotal, Is.EqualTo(0m));

            // 3. Salaire net final restant à payer
            Assert.That(res.NetRestantApresSaisies, Is.EqualTo(75_000m));
        }

        [Test]
        [Microsoft.VisualStudio.TestTools.UnitTesting.TestMethod]
        public void RepartirSaisies_PlusieursCreanciersMemeRang_RepartitionAuMarcLeFranc()
        {
            // Salaire net = 100 000 FCFA -> Quotité ordinaire = 12 500 FCFA
            var saisie1 = new SaisieOpposition
            {
                Id = 1,
                TypeSaisie = TypeSaisieOpposition.SaisieArretJudiciaire,
                Creancier = "Créancier A",
                MontantTotalDu = 100_000m,
                RetenueMensuelleFixee = 10_000m,
                Statut = StatutSaisieOpposition.EnCours
            };

            var saisie2 = new SaisieOpposition
            {
                Id = 2,
                TypeSaisie = TypeSaisieOpposition.SaisieArretJudiciaire,
                Creancier = "Créancier B",
                MontantTotalDu = 100_000m,
                RetenueMensuelleFixee = 10_000m,
                Statut = StatutSaisieOpposition.EnCours
            };

            var res = SaisieOppositionService.RepartirSaisies(100_000m, new[] { saisie1, saisie2 }, smig: 45_000m);

            // Total disponible = 12 500 FCFA. Total demandé = 20 000 FCFA.
            // Chaque créancier a la même demande (50%), donc chacun perçoit la moitié (6 250 FCFA).
            Assert.That(res.RetenuesOrdinairesTotal, Is.EqualTo(12_500m));
            Assert.That(res.Repartitions[0].MontantRetenu, Is.EqualTo(6_250m));
            Assert.That(res.Repartitions[1].MontantRetenu, Is.EqualTo(6_250m));
        }

        [Test]
        [Microsoft.VisualStudio.TestTools.UnitTesting.TestMethod]
        public void RepartirSaisies_PlafonnementAuSoldeRestantDu_ArretAutomatique()
        {
            // Dette restante = 15 000 FCFA, retenue demandée = 25 000 FCFA
            // Salaire net = 200 000 FCFA (quotité largement suffisante)
            var saisie = new SaisieOpposition
            {
                Id = 1,
                TypeSaisie = TypeSaisieOpposition.CessionVolontaire,
                Creancier = "Microfinance Express",
                MontantTotalDu = 100_000m,
                TotalAmorti = 85_000m, // Reste dû = 15 000 FCFA
                RetenueMensuelleFixee = 25_000m,
                Statut = StatutSaisieOpposition.EnCours
            };

            var res = SaisieOppositionService.RepartirSaisies(200_000m, new[] { saisie }, smig: 45_000m);

            // La retenue ne doit retenir QUE le solde restant dû (15 000 FCFA) et pas les 25 000 FCFA demandés
            Assert.That(res.RetenuesOrdinairesTotal, Is.EqualTo(15_000m));
            Assert.That(res.Repartitions[0].MontantRetenu, Is.EqualTo(15_000m));
            Assert.That(res.Repartitions[0].SoldeRestantApres, Is.EqualTo(0m));
            Assert.That(res.Repartitions[0].EstCompletementAmortie, Is.True);
        }
    }
}
