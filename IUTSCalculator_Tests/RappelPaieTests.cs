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
    public class RappelPaieTests
    {
        [Test]
        [Microsoft.VisualStudio.TestTools.UnitTesting.TestMethod]
        public void Employee_FullNameWithId_FormatteCorrectement()
        {
            var emp1 = new Employee
            {
                EmployeeId = "EMP001",
                LastName = "KABORE",
                FirstName = "Adama"
            };
            Assert.That(emp1.FullNameWithId, Is.EqualTo("KABORE Adama (EMP001)"));

            var emp2 = new Employee
            {
                EmployeeId = "",
                LastName = "OUEDRAOGO",
                FirstName = "Fatou"
            };
            Assert.That(emp2.FullNameWithId, Is.EqualTo("OUEDRAOGO Fatou"));

            var emp3 = new Employee
            {
                EmployeeId = "EMP003",
                LastName = "",
                FirstName = ""
            };
            Assert.That(emp3.FullNameWithId, Is.EqualTo("EMP003"));
        }

        [Test]
        [Microsoft.VisualStudio.TestTools.UnitTesting.TestMethod]
        public void ApportDe_RappelSalaire_AlimenteGainsImposablesEtCotisables()
        {
            var rappels = new List<RappelPaie>
            {
                new RappelPaie
                {
                    Id = 1,
                    EmployeeId = "EMP001",
                    TypeRappel = TypeRappelPaie.RappelSalaire,
                    Libelle = "Rappel de salaire rétroactif (3 mois)",
                    Sens = SensRappel.Gain,
                    MontantBrut = 75_000m,
                    SoumisIUTS = true,
                    SoumisCNSS = true,
                    SoumisTPA = true,
                    Statut = StatutRappelPaie.EnAttente
                }
            };

            var apport = RappelPaieService.ApportDe(rappels, "EMP001");

            Assert.That(apport.EstVide, Is.False);
            Assert.That(apport.GainsImposablesEtCotisables, Is.EqualTo(75_000m));
            Assert.That(apport.Retenues, Is.EqualTo(0m));
            Assert.That(apport.Lignes.Count, Is.EqualTo(1));
            Assert.That(apport.Lignes[0].CodeRubrique, Is.EqualTo("RAPPELSAL"));
            Assert.That(apport.Lignes[0].Montant, Is.EqualTo(75_000m));
            Assert.That(apport.Lignes[0].Sens, Is.EqualTo(SensRubrique.Gain));
            Assert.That(apport.RappelIds.Contains(1), Is.True);
        }

        [Test]
        [Microsoft.VisualStudio.TestTools.UnitTesting.TestMethod]
        public void ApportDe_TropPercu_AlimenteRetenues()
        {
            var rappels = new List<RappelPaie>
            {
                new RappelPaie
                {
                    Id = 2,
                    EmployeeId = "EMP001",
                    TypeRappel = TypeRappelPaie.TropPercuSalaire,
                    Libelle = "Trop-perçu sur salaire antérieur",
                    Sens = SensRappel.Retenue,
                    MontantBrut = 30_000m,
                    SoumisIUTS = false,
                    SoumisCNSS = false,
                    SoumisTPA = false,
                    Statut = StatutRappelPaie.EnAttente
                }
            };

            var apport = RappelPaieService.ApportDe(rappels, "EMP001");

            Assert.That(apport.EstVide, Is.False);
            Assert.That(apport.GainsImposablesEtCotisables, Is.EqualTo(0m));
            Assert.That(apport.Retenues, Is.EqualTo(30_000m));
            Assert.That(apport.Lignes.Count, Is.EqualTo(1));
            Assert.That(apport.Lignes[0].CodeRubrique, Is.EqualTo("TROPPERCU"));
            Assert.That(apport.Lignes[0].Sens, Is.EqualTo(SensRubrique.Retenue));
        }

        [Test]
        [Microsoft.VisualStudio.TestTools.UnitTesting.TestMethod]
        public void ApportDe_FiltreParStatutEtEmploye()
        {
            var rappels = new List<RappelPaie>
            {
                new RappelPaie
                {
                    Id = 1,
                    EmployeeId = "EMP001",
                    TypeRappel = TypeRappelPaie.RappelSalaire,
                    MontantBrut = 50_000m,
                    Sens = SensRappel.Gain,
                    SoumisIUTS = true,
                    SoumisCNSS = true,
                    SoumisTPA = true,
                    // Annulé : jamais repris. (Un rappel « Imputé » est en revanche repris
                    // quand on recalcule SON mois d'imputation — voir le test suivant.)
                    Statut = StatutRappelPaie.Annule
                },
                new RappelPaie
                {
                    Id = 2,
                    EmployeeId = "EMP002", // Autre salarié
                    TypeRappel = TypeRappelPaie.RappelSalaire,
                    MontantBrut = 40_000m,
                    Sens = SensRappel.Gain,
                    SoumisIUTS = true,
                    SoumisCNSS = true,
                    SoumisTPA = true,
                    Statut = StatutRappelPaie.EnAttente
                },
                new RappelPaie
                {
                    Id = 3,
                    EmployeeId = "EMP001",
                    TypeRappel = TypeRappelPaie.RappelPrime,
                    Libelle = "Rappel prime de bilan",
                    MontantBrut = 60_000m,
                    Sens = SensRappel.Gain,
                    SoumisIUTS = true,
                    SoumisCNSS = true,
                    SoumisTPA = true,
                    Statut = StatutRappelPaie.EnAttente
                }
            };

            var apport = RappelPaieService.ApportDe(rappels, "EMP001");

            Assert.That(apport.Lignes.Count, Is.EqualTo(1));
            Assert.That(apport.Lignes[0].CodeRubrique, Is.EqualTo("RAPPELPRIME"));
            Assert.That(apport.GainsImposablesEtCotisables, Is.EqualTo(60_000m));
            Assert.That(apport.RappelIds.Contains(3), Is.True);
            Assert.That(apport.RappelIds.Contains(1), Is.False);
            Assert.That(apport.RappelIds.Contains(2), Is.False);
        }

        [Test]
        [Microsoft.VisualStudio.TestTools.UnitTesting.TestMethod]
        public void ApportDe_RappelImputeRepris_AuRecalculDeSonMois()
        {
            // L'enregistrement d'un bulletin marque ses rappels « Imputé » : recalculer ce
            // mois ne doit pas les perdre (la liste fournie est celle du mois d'imputation).
            var rappels = new List<RappelPaie>
            {
                new RappelPaie
                {
                    Id = 4, EmployeeId = "EMP001", TypeRappel = TypeRappelPaie.RappelSalaire, Libelle = "Rappel",
                    MontantBrut = 50_000m, Sens = SensRappel.Gain, SoumisIUTS = true, SoumisCNSS = true, SoumisTPA = true,
                    Statut = StatutRappelPaie.Impute
                }
            };

            var apport = RappelPaieService.ApportDe(rappels, "EMP001");

            Assert.That(apport.GainsImposablesEtCotisables, Is.EqualTo(50_000m));
            Assert.That(apport.RappelIds.Contains(4), Is.True);
        }

        [Test]
        [Microsoft.VisualStudio.TestTools.UnitTesting.TestMethod]
        public void RegularisationIuts_RegulAnnuelle_NonSoumiseAuxCotisations()
        {
            var rappelRegul = new RappelPaie
            {
                Id = 4,
                EmployeeId = "EMP001",
                TypeRappel = TypeRappelPaie.RegularisationIuts,
                Libelle = "Régularisation IUTS annuelle 2026 (Complément d'impôt)",
                Sens = SensRappel.Retenue,
                MontantBrut = 15_400m,
                SoumisIUTS = false,
                SoumisCNSS = false,
                SoumisTPA = false,
                Statut = StatutRappelPaie.EnAttente
            };

            var apport = RappelPaieService.ApportDe(new[] { rappelRegul }, "EMP001");

            Assert.That(apport.Retenues, Is.EqualTo(15_400m));
            Assert.That(apport.GainsImposablesEtCotisables, Is.EqualTo(0m));
            Assert.That(apport.Lignes[0].CodeRubrique, Is.EqualTo("REGULIUTS"));
            Assert.That(apport.Lignes[0].SoumisIUTS, Is.False);
            Assert.That(apport.Lignes[0].SoumisCNSS, Is.False);
            Assert.That(apport.Lignes[0].SoumisTPA, Is.False);
        }
    }
}
