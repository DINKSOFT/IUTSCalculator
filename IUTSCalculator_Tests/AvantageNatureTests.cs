using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using IUTSCalculator.Models;
using IUTSCalculator.Services;

namespace IUTSCalculator.Tests
{
    [TestFixture]
    [Microsoft.VisualStudio.TestTools.UnitTesting.TestClass]
    public class AvantageNatureTests
    {
        [Test]
        [Microsoft.VisualStudio.TestTools.UnitTesting.TestMethod]
        public void CalculForfaitLogement_Plafonnement75000FCFA_ConformeAuCGI()
        {
            // Cas 1 : Salaire modeste (200 000 FCFA) -> 20% = 40 000 FCFA < 75 000 FCFA
            decimal forfait1 = AvantageNatureService.CalculerForfaitLogementLegal(200_000m);
            Assert.That(forfait1, Is.EqualTo(40_000m));

            // Cas 2 : Salaire charnière (375 000 FCFA) -> 20% = 75 000 FCFA == Plafond
            decimal forfait2 = AvantageNatureService.CalculerForfaitLogementLegal(375_000m);
            Assert.That(forfait2, Is.EqualTo(75_000m));

            // Cas 3 : Cadre supérieur (600 000 FCFA) -> 20% = 120 000 FCFA -> Écrêté à 75 000 FCFA
            decimal forfait3 = AvantageNatureService.CalculerForfaitLogementLegal(600_000m);
            Assert.That(forfait3, Is.EqualTo(75_000m));

            // Cas 4 : Salaire nul ou négatif
            decimal forfait4 = AvantageNatureService.CalculerForfaitLogementLegal(0m);
            Assert.That(forfait4, Is.EqualTo(0m));
        }

        [Test]
        [Microsoft.VisualStudio.TestTools.UnitTesting.TestMethod]
        public void ParticipationSalariale_ReduitValeurNetteImposable()
        {
            var av = new AvantageNature
            {
                EmployeeId = "EMP001",
                TypeAvantage = TypeAvantageNature.Logement,
                Libelle = "Logement de fonction avec retenue partielle",
                MontantAvantage = 100_000m,
                PartSalariale = 35_000m
            };

            // La valeur nette imposable doit être 100 000 - 35 000 = 65 000 FCFA
            Assert.That(av.ValeurNetteImposable, Is.EqualTo(65_000m));

            // Si la part salariale couvre la totalité
            av.PartSalariale = 120_000m;
            Assert.That(av.ValeurNetteImposable, Is.EqualTo(0m));
        }

        [Test]
        [Microsoft.VisualStudio.TestTools.UnitTesting.TestMethod]
        public void ApportDe_AvantageNature_AlimenteGainsEtRetenuesSimultanement()
        {
            var avantages = new List<AvantageNature>
            {
                new AvantageNature
                {
                    Id = 1,
                    EmployeeId = "EMP001",
                    TypeAvantage = TypeAvantageNature.Logement,
                    Libelle = "Logement villa fonction",
                    MontantAvantage = 75_000m,
                    PartSalariale = 0m,
                    DateDebut = new DateTime(2026, 1, 1),
                    Actif = true,
                    SoumisIUTS = true,
                    SoumisCNSS = true,
                    SoumisTPA = true,
                    CodeRubriqueGain = "AVNAT_LOG",
                    CodeRubriqueRetenue = "RET_AVNAT"
                }
            };

            var apport = AvantageNatureService.ApportDe(avantages, "EMP001", 2026, 9);

            Assert.That(apport.EstVide, Is.False);
            Assert.That(apport.GainsImposablesEtCotisables, Is.EqualTo(75_000m));
            Assert.That(apport.Retenues, Is.EqualTo(75_000m));
            Assert.That(apport.Lignes.Count, Is.EqualTo(2));

            // 1. Ligne de Gain (réintégration dans l'assiette)
            var ligneGain = apport.Lignes[0];
            Assert.That(ligneGain.Code, Is.EqualTo("AVNAT_LOG"));
            Assert.That(ligneGain.Sens, Is.EqualTo(SensRubrique.Gain));
            Assert.That(ligneGain.Montant, Is.EqualTo(75_000m));
            Assert.That(ligneGain.SoumisIUTS, Is.True);
            Assert.That(ligneGain.SoumisCNSS, Is.True);
            Assert.That(ligneGain.SoumisTPA, Is.True);

            // 2. Ligne de Retenue (déduction du net)
            var ligneRetenue = apport.Lignes[1];
            Assert.That(ligneRetenue.Code, Is.EqualTo("RET_AVNAT"));
            Assert.That(ligneRetenue.Sens, Is.EqualTo(SensRubrique.Retenue));
            Assert.That(ligneRetenue.Montant, Is.EqualTo(75_000m));
        }

        [Test]
        [Microsoft.VisualStudio.TestTools.UnitTesting.TestMethod]
        public void ApportDe_FiltreParPeriodeEtEmploye()
        {
            var avantages = new List<AvantageNature>
            {
                new AvantageNature
                {
                    Id = 1,
                    EmployeeId = "EMP001",
                    TypeAvantage = TypeAvantageNature.Vehicule,
                    Libelle = "Véhicule de service",
                    MontantAvantage = 50_000m,
                    DateDebut = new DateTime(2026, 1, 1),
                    DateFin = new DateTime(2026, 6, 30), // Expiré en juillet
                    Actif = true
                },
                new AvantageNature
                {
                    Id = 2,
                    EmployeeId = "EMP002", // Autre salarié
                    TypeAvantage = TypeAvantageNature.Electricite,
                    Libelle = "Prise en charge électricité",
                    MontantAvantage = 30_000m,
                    DateDebut = new DateTime(2026, 1, 1),
                    Actif = true
                },
                new AvantageNature
                {
                    Id = 3,
                    EmployeeId = "EMP001",
                    TypeAvantage = TypeAvantageNature.Eau,
                    Libelle = "Prise en charge eau ONEA",
                    MontantAvantage = 15_000m,
                    DateDebut = new DateTime(2026, 1, 1),
                    Actif = false // Inactif
                },
                new AvantageNature
                {
                    Id = 4,
                    EmployeeId = "EMP001",
                    TypeAvantage = TypeAvantageNature.Logement,
                    Libelle = "Logement de fonction",
                    MontantAvantage = 70_000m,
                    DateDebut = new DateTime(2026, 1, 1),
                    Actif = true // Valide
                }
            };

            // Test pour EMP001 en septembre 2026 : seul le logement (Id 4) doit être actif
            var apportSeptembre = AvantageNatureService.ApportDe(avantages, "EMP001", 2026, 9);
            Assert.That(apportSeptembre.GainsImposablesEtCotisables, Is.EqualTo(70_000m));
            Assert.That(apportSeptembre.Retenues, Is.EqualTo(70_000m));
            Assert.That(apportSeptembre.AvantageIds.Count, Is.EqualTo(1));
            Assert.That(apportSeptembre.AvantageIds.Contains(4), Is.True);

            // Test pour EMP001 en mars 2026 : véhicule (Id 1) + logement (Id 4) = 50 000 + 70 000 = 120 000 FCFA
            var apportMars = AvantageNatureService.ApportDe(avantages, "EMP001", 2026, 3);
            Assert.That(apportMars.GainsImposablesEtCotisables, Is.EqualTo(120_000m));
            Assert.That(apportMars.Retenues, Is.EqualTo(120_000m));
            Assert.That(apportMars.AvantageIds.Count, Is.EqualTo(2));
        }

        [Test]
        [Microsoft.VisualStudio.TestTools.UnitTesting.TestMethod]
        public void EquivalenceStricte_SansAvantageNature_AucuneModification()
        {
            var apport = ApportVariables.Aucun();
            apport = apport.AvecAvantagesNature(null);

            Assert.That(apport.GainsImposablesEtCotisables, Is.EqualTo(0m));
            Assert.That(apport.Retenues, Is.EqualTo(0m));
            Assert.That(apport.Lignes.Count, Is.EqualTo(0));

            var apportVide = ApportAvantagesNature.Aucun();
            apport = apport.AvecAvantagesNature(apportVide);

            Assert.That(apport.GainsImposablesEtCotisables, Is.EqualTo(0m));
            Assert.That(apport.Retenues, Is.EqualTo(0m));
            Assert.That(apport.Lignes.Count, Is.EqualTo(0));
        }
    }
}
