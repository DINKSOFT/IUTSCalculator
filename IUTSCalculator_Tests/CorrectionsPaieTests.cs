using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using IUTSCalculator.Models;
using IUTSCalculator.Services;

namespace IUTSCalculator.Tests
{
    /// <summary>
    /// Corrections de la paie relevées en préparant le guide de formation (version 1.4.37.1) :
    /// défauts n° 2, 3, 5, 6, 7, 8, 11, 22, 23 et 25.
    /// </summary>
    [TestFixture]
    [Microsoft.VisualStudio.TestTools.UnitTesting.TestClass]
    public class CorrectionsPaieTests
    {
        private static Employee Salarie(DateTime entree, decimal base_ = 90000m, string categorie = "1ère Catégorie")
        {
            return new Employee { EmployeeId = "T001", FirstName = "A", LastName = "B", BaseSalary = base_, DateEntree = entree, Category = categorie };
        }

        // ── Défaut 2 : ancienneté au 1er du mois ─────────────────────────────

        [Test]
        [Microsoft.VisualStudio.TestTools.UnitTesting.TestMethod]
        public void Anciennete_DateRameneeAuPremierDuMois()
        {
            Assert.That(ConventionCollectiveService.DateCalculAnciennete(new DateTime(2026, 2, 28)), Is.EqualTo(new DateTime(2026, 2, 1)));

            var conv = new ConventionCollective
            {
                BaseCalculAnciennete = BaseCalculAnciennete.SalaireDeBaseReel,
                TranchesAnciennete = new List<TrancheAnciennete>
                {
                    new TrancheAnciennete { AnneesMin = 2, AnneesMax = 3, TauxPourcentage = 5m },
                    new TrancheAnciennete { AnneesMin = 3, AnneesMax = null, TauxPourcentage = 6m }
                }
            };
            var e = Salarie(new DateTime(2023, 2, 15));
            // Calcul pur, sans base : instance créée sans passer par le constructeur.
            var service = (ConventionCollectiveService)System.Runtime.Serialization.FormatterServices
                .GetUninitializedObject(typeof(ConventionCollectiveService));
            // Anniversaire du 15/02 : le 3e anniversaire n'est pris en compte qu'au 1er mars,
            // quel que soit le jour passé par l'écran (fin de mois, date choisie…).
            Assert.That(service.CalculerAnciennete(e, new DateTime(2026, 2, 28), conv).TauxPourcentage, Is.EqualTo(5m));
            Assert.That(service.CalculerAnciennete(e, new DateTime(2026, 2, 1), conv).TauxPourcentage, Is.EqualTo(5m));
            Assert.That(service.CalculerAnciennete(e, new DateTime(2026, 3, 31), conv).TauxPourcentage, Is.EqualTo(6m));
        }

        // ── Défaut 3 : vocabulaire des catégories ────────────────────────────

        [Test]
        [Microsoft.VisualStudio.TestTools.UnitTesting.TestMethod]
        public void CadreSuperieur_SingulierPlurielCasseAccents()
        {
            Assert.That(Employee.EstCategorieCadreSuperieur("Cadre Supérieur"), Is.True);
            Assert.That(Employee.EstCategorieCadreSuperieur("Cadres Supérieurs"), Is.True);
            Assert.That(Employee.EstCategorieCadreSuperieur("  cadres  superieurs "), Is.True);
            Assert.That(Employee.EstCategorieCadreSuperieur("Cadres Moyens"), Is.False);
            Assert.That(Employee.EstCategorieCadreSuperieur("Agents de Maîtrise"), Is.False);
            Assert.That(new Employee { Category = "Cadres Supérieurs" }.IsHigherCategory, Is.True);
        }

        // ── Défaut 5 : mode de règlement ─────────────────────────────────────

        [Test]
        [Microsoft.VisualStudio.TestTools.UnitTesting.TestMethod]
        public void ModeDeLaFiche_RepriseDuModeEtDuCompte()
        {
            var e = new Employee { ModeReglement = "Mobile money", NumeroCompte = "70 00 00 00" };
            Assert.That(ModesReglementPaie.Resoudre(ModesReglementPaie.ModeDeLaFiche, e), Is.EqualTo("Mobile money"));
            Assert.That(ModesReglementPaie.CompteDe(ModesReglementPaie.ModeDeLaFiche, null, e), Is.EqualTo("70 00 00 00"));
            Assert.That(ModesReglementPaie.Resoudre("Espèces", e), Is.EqualTo("Espèces"));
            Assert.That(ModesReglementPaie.Resoudre(null, new Employee { ModeReglement = "Virement" }), Is.EqualTo("Virement bancaire"));
            Assert.That(ModesReglementPaie.Liste(), Does.Contain("Mobile money"));
        }

        // ── Défaut 6 : date d'effet et libellé des saisies ───────────────────

        [Test]
        [Microsoft.VisualStudio.TestTools.UnitTesting.TestMethod]
        public void Saisies_DateDEffetRespectee()
        {
            var s = new SaisieOpposition { Id = 1, DateDebut = new DateTime(2026, 5, 10) };
            var fin = new SaisieOpposition { Id = 2, DateDebut = new DateTime(2026, 1, 1), DateFin = new DateTime(2026, 3, 31) };
            var liste = new[] { s, fin };
            Assert.That(SaisieOppositionService.EffectivesPourMois(liste, 2026, 4).Select(x => x.Id), Is.Empty);
            Assert.That(SaisieOppositionService.EffectivesPourMois(liste, 2026, 5).Select(x => x.Id), Is.EqualTo(new[] { 1 }));
            Assert.That(SaisieOppositionService.EffectivesPourMois(liste, 2026, 3).Select(x => x.Id), Is.EqualTo(new[] { 2 }));
        }

        [Test]
        [Microsoft.VisualStudio.TestTools.UnitTesting.TestMethod]
        public void Saisies_LibelleLisible()
        {
            var l = new LigneRepartitionSaisie { TypeSaisie = TypeSaisieOpposition.SaisieArretJudiciaire, Creancier = "CAISSE POPULAIRE", NumeroDossier = "SA-2026-014" };
            Assert.That(SaisieOppositionService.LibelleBulletin(l), Is.EqualTo("Saisie-arrêt judiciaire (CAISSE POPULAIRE) — dossier SA-2026-014"));
        }

        // ── Défaut 11 : prorata d'entrée et de sortie ────────────────────────

        [Test]
        [Microsoft.VisualStudio.TestTools.UnitTesting.TestMethod]
        public void Prorata_SortieEnCoursDeMois_Trentieme()
        {
            var e = Salarie(new DateTime(2020, 1, 1));
            e.DateSortie = new DateTime(2026, 9, 25);
            var apport = AbsenceService.Apport(e, new List<Absence>(), 2026, 9, BaseRetenueAbsence.TrentiemeMensuel);
            Assert.That(apport.JoursNonRemuneres, Is.EqualTo(5m));
            Assert.That(apport.RetenueSurBrut, Is.EqualTo(15000m));       // 90 000 / 30 × 5
            Assert.That(apport.LibelleRetenue, Does.Contain("Prorata de sortie"));
        }

        [Test]
        [Microsoft.VisualStudio.TestTools.UnitTesting.TestMethod]
        public void Prorata_MoisComplets_AucuneRetenue()
        {
            var e = Salarie(new DateTime(2026, 8, 1));
            e.DateSortie = new DateTime(2026, 8, 31);
            Assert.That(AbsenceService.ProrataHorsContrat(e, 2026, 8, BaseRetenueAbsence.TrentiemeMensuel).EstVide, Is.True);
            var f = Salarie(new DateTime(2020, 1, 1));
            f.DateSortie = new DateTime(2026, 2, 28);
            Assert.That(AbsenceService.ProrataHorsContrat(f, 2026, 2, BaseRetenueAbsence.TrentiemeMensuel).EstVide, Is.True);
        }

        [Test]
        [Microsoft.VisualStudio.TestTools.UnitTesting.TestMethod]
        public void Prorata_AbsenceApresSortie_PasCompteeDeuxFois()
        {
            var e = Salarie(new DateTime(2020, 1, 1));
            e.DateSortie = new DateTime(2026, 9, 25);
            var abs = new Absence { EmployeeId = "T001", Type = TypeAbsence.AbsenceNonRemuneree, DateDebut = new DateTime(2026, 9, 26), DateFin = new DateTime(2026, 9, 30), NombreJours = 5m };
            var apport = AbsenceService.Apport(e, new[] { abs }, 2026, 9, BaseRetenueAbsence.TrentiemeMensuel);
            Assert.That(apport.JoursNonRemuneres, Is.EqualTo(5m));
        }

        [Test]
        [Microsoft.VisualStudio.TestTools.UnitTesting.TestMethod]
        public void Prorata_AbattementEtPrimeAncienneteSurLaBaseDue()
        {
            // Entrée le 21 : 10 jours dus sur 30 (fraction 1/3).
            var e = Salarie(new DateTime(2026, 9, 21), 300000m);
            var absences = AbsenceService.Apport(e, new List<Absence>(), 2026, 9, BaseRetenueAbsence.TrentiemeMensuel);
            Assert.That(absences.FractionContrat, Is.EqualTo(10m / 30m));

            var conv = new ApportConventionnel(new List<LigneApport>
            {
                new LigneApport("PRIMEANC", "Prime d'ancienneté", SensRubrique.Gain, 5m, 15000m, true, true, true)
            }, 15000m);
            var apport = ApportVariables.Aucun().AvecAbsences(absences).AvecConventionCollective(conv);
            Assert.That(apport.Lignes.Single(l => l.Code == "PRIMEANC").Montant, Is.EqualTo(5000m));

            var moteur = new TaxCalculatorService(new TaxDataService(System.IO.Path.GetTempPath()));
            var calcul = moteur.CalculateMonthlyTax(e, apport);
            Assert.That(calcul.ProfessionalDeduction, Is.EqualTo(300000m / 3m * 0.25m).Within(1m));   // 25 000, pas 75 000
        }

        [Test]
        [Microsoft.VisualStudio.TestTools.UnitTesting.TestMethod]
        public void AbsenceOrdinaire_AbattementInchange()
        {
            var e = Salarie(new DateTime(2020, 1, 1), 300000m);
            var abs = new Absence { EmployeeId = "T001", Type = TypeAbsence.AbsenceNonRemuneree, DateDebut = new DateTime(2026, 9, 7), DateFin = new DateTime(2026, 9, 11), NombreJours = 5m };
            var apport = ApportVariables.Aucun().AvecAbsences(AbsenceService.Apport(e, new[] { abs }, 2026, 9, BaseRetenueAbsence.TrentiemeMensuel));
            Assert.That(apport.FractionContrat, Is.EqualTo(1m));
            var calcul = new TaxCalculatorService(new TaxDataService(System.IO.Path.GetTempPath())).CalculateMonthlyTax(e, apport);
            Assert.That(calcul.ProfessionalDeduction, Is.EqualTo(75000m));
        }

        // ── Relecture : rappels et saisies d'un mois recalculé ───────────────

        [Test]
        [Microsoft.VisualStudio.TestTools.UnitTesting.TestMethod]
        public void Rappels_ImputesRepris_AnnulesExclus()
        {
            var rappels = new[]
            {
                new RappelPaie { Id = 1, EmployeeId = "T001", TypeRappel = TypeRappelPaie.RappelSalaire, Sens = SensRappel.Gain, MontantBrut = 10000m, Libelle = "R1", Statut = StatutRappelPaie.Impute, SoumisIUTS = true, SoumisCNSS = true, SoumisTPA = true },
                new RappelPaie { Id = 2, EmployeeId = "T001", TypeRappel = TypeRappelPaie.RappelSalaire, Sens = SensRappel.Gain, MontantBrut = 5000m, Libelle = "R2", Statut = StatutRappelPaie.Annule, SoumisIUTS = true, SoumisCNSS = true, SoumisTPA = true }
            };
            var apport = RappelPaieService.ApportDe(rappels, "T001");
            Assert.That(apport.Lignes.Sum(l => l.Montant), Is.EqualTo(10000m));
        }

        [Test]
        [Microsoft.VisualStudio.TestTools.UnitTesting.TestMethod]
        public void Saisies_RecalculDuMois_NeutraliseLaRetenueDejaEnregistree()
        {
            // Créance de 20 000, soldée par la retenue de mars déjà enregistrée.
            var s = new SaisieOpposition { Id = 7, EmployeeId = "T001", DateDebut = new DateTime(2026, 3, 1), MontantTotalDu = 20000m, TotalAmorti = 20000m, Statut = StatutSaisieOpposition.Soldee, RetenueMensuelleFixee = 20000m };
            var recevables = SaisieOppositionService.ActivesPourMois(new[] { s }, new Dictionary<int, decimal> { { 7, 20000m } }, 2026, 3);
            Assert.That(recevables.Count, Is.EqualTo(1));
            Assert.That(recevables[0].TotalAmorti, Is.EqualTo(0m));
            Assert.That(SaisieOppositionService.ActivesPourMois(new[] { new SaisieOpposition { Id = 8, DateDebut = new DateTime(2026, 1, 1), MontantTotalDu = 1000m, TotalAmorti = 1000m, Statut = StatutSaisieOpposition.Soldee } }, null, 2026, 4), Is.Empty);
        }

        // ── Défaut 7 : reprise du solde de congés ────────────────────────────

        [Test]
        [Microsoft.VisualStudio.TestTools.UnitTesting.TestMethod]
        public void Conges_RepriseDuSolde()
        {
            var e = Salarie(new DateTime(2006, 3, 1));
            var sansReprise = AbsenceService.Solde(e, new List<Absence>(), new DateTime(2026, 6, 30), 2.5m, 0, 0);
            Assert.That(sansReprise.Solde, Is.GreaterThan(500m));

            e.SoldeCongesReprise = 12m;
            e.DateRepriseConges = new DateTime(2026, 1, 1);
            var pris = new Absence { EmployeeId = "T001", Type = TypeAbsence.CongePaye, DateDebut = new DateTime(2026, 4, 6), DateFin = new DateTime(2026, 4, 10), NombreJours = 5m };
            var avant = new Absence { EmployeeId = "T001", Type = TypeAbsence.CongePaye, DateDebut = new DateTime(2025, 8, 4), DateFin = new DateTime(2025, 8, 29), NombreJours = 22m };
            var solde = AbsenceService.Solde(e, new[] { pris, avant }, new DateTime(2026, 6, 30), 2.5m, 0, 0);
            Assert.That(solde.JoursRepris, Is.EqualTo(12m));
            Assert.That(solde.JoursAcquis, Is.EqualTo(12.5m));   // 5 mois révolus × 2,5
            Assert.That(solde.JoursPris, Is.EqualTo(5m));        // le congé d'août 2025 est avant la reprise
            Assert.That(solde.Solde, Is.EqualTo(19.5m));
        }

        // ── Défaut 8 : préavis et départ à la retraite ───────────────────────

        [Test]
        [Microsoft.VisualStudio.TestTools.UnitTesting.TestMethod]
        public void Preavis_ParCategorie()
        {
            Assert.That(SoldeToutCompteService.DeterminerDureePreavisMois(Salarie(new DateTime(2010, 1, 1), categorie: "Agents de Maîtrise"), 16), Is.EqualTo(3));
            Assert.That(SoldeToutCompteService.DeterminerDureePreavisMois(Salarie(new DateTime(2010, 1, 1), categorie: "Cadres Moyens"), 16), Is.EqualTo(3));
            Assert.That(SoldeToutCompteService.DeterminerDureePreavisMois(Salarie(new DateTime(2010, 1, 1), categorie: "Non cadre"), 16), Is.EqualTo(1));
            Assert.That(SoldeToutCompteService.DeterminerDureePreavisMois(Salarie(new DateTime(2010, 1, 1), categorie: "4ème Catégorie"), 16), Is.EqualTo(1));
        }

        // ── Défaut 23 : codes HS distincts avec le barème de 2000 ────────────

        [Test]
        [Microsoft.VisualStudio.TestTools.UnitTesting.TestMethod]
        public void HeuresSup_Bareme2000_CodesDistincts()
        {
            var hs = new HeureSupplementaire { SalaireDeBase = 82000m, Heures50 = 8m, Heures60 = 4m };
            hs.AppliquerBareme(BaremeHeuresSup.Historique());
            var codes = HeureSupplementaireService.LignesBulletin(hs).Select(l => l.Code).ToList();
            Assert.That(codes, Is.EquivalentTo(new[] { "HS50", "HS50J" }));
        }
    }
}
