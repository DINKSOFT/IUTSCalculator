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
    /// <summary>
    /// Module RH-08 — Gestion des temps et des rotations, et barème daté des heures
    /// supplémentaires (PAI-03 à cinq tranches).
    ///
    /// Le cas de référence est l'onglet « 01 » de l'état de salaire de rotation (cycle
    /// 7 repos / 7 jours / 7 nuits, postes de 12 h, semaine de rotation) : 16 h à 15 %,
    /// 44 h à 35 % et 28 h de nuit à 50 % sur les quatre semaines de la période.
    /// </summary>
    [TestFixture]
    [Microsoft.VisualStudio.TestTools.UnitTesting.TestClass]
    public class GestionTempsTests
    {
        private static readonly BaremeHeuresSup Bareme = BaremeHeuresSup.CinqTranches2026();

        private static EquipeTravail EquipeKS()
        {
            return new EquipeTravail
            {
                Id = 1,
                Code = "TEAM A",
                Motif = "7R 7J 7N",
                DateDebutCycle = new DateTime(2026, 10, 21),
                ModeSemaine = ModeSemaineDecompte.Rotation,
                DureePosteJour = 12m,
                DureePosteNuit = 12m,
                HeuresNormalesParPoste = 8m,
                PanierMode = ModePanier.FractionTauxHoraire,
                PanierValeur = 1m / 3m
            };
        }

        private static List<PlanningJour> Planning(DateTime debut, string codes)
        {
            var liste = new List<PlanningJour>();
            var d = debut;
            foreach (var c in codes.Split(' '))
            {
                liste.Add(new PlanningJour { EmployeeId = "KS001", Date = d, Code = c });
                d = d.AddDays(1);
            }
            return liste;
        }

        private static List<(string Code, decimal Heures, decimal HeuresNormalesPoste)> Semaine(params string[] codes)
        {
            return codes.Select(c => (c, CodesPlanning.EstTravaille(c) ? 12m : 0m, 8m)).ToList();
        }

        // ── Moteur ──────────────────────────────────────────────────────────

        [Test]
        [Microsoft.VisualStudio.TestTools.UnitTesting.TestMethod]
        public void Decompter_CasOnglet01_ReproduitLeClasseur()
        {
            var equipe = EquipeKS();
            var cycle = equipe.Cycle();
            var periode = PeriodePointage.Pour(2026, 11, 20); // 21/10 → 20/11

            var jours = new List<PlanningJour>();
            for (var d = new DateTime(2026, 10, 15); d <= periode.Fin; d = d.AddDays(1))
                jours.Add(new PlanningJour { EmployeeId = "KS001", Date = d, Code = cycle.CodePour(d, equipe.DateDebutCycle) });

            var salarie = new Employee { EmployeeId = "KS001", FirstName = "SOUMAILA", LastName = "NIKIEMA", BaseSalary = 82000m };
            var recap = CalculTemps.Decompter(salarie, periode, jours, equipe,
                new Dictionary<int, EquipeTravail> { { 1, equipe } }, new HashSet<DateTime>(), Bareme, new ParametresTemps());

            Assert.That(recap.Semaines.Count, Is.EqualTo(4));
            Assert.That(recap.Tranche1, Is.EqualTo(16m));
            Assert.That(recap.Tranche2, Is.EqualTo(44m));
            Assert.That(recap.Nuit, Is.EqualTo(28m));
            Assert.That(recap.HeuresSup, Is.EqualTo(88m));
            Assert.That(recap.Paniers, Is.EqualTo(17));
            Assert.That(recap.Avertissements.Any(a => a.Contains("semaine à cheval")), Is.True);
        }

        [Test]
        [Microsoft.VisualStudio.TestTools.UnitTesting.TestMethod]
        public void DecompterSemaine_NuitsEtFeries_LaSommeDesTranchesEgaleLesHeuresSup()
        {
            // Le classeur comptait 12 h pour 8 h supplémentaires réelles dans ce cas.
            var s = CalculTemps.DecompterSemaine(new DateTime(2026, 10, 5), Semaine("N", "N", "N", "FN", "R", "R", "R"), Bareme);
            Assert.That(s.HeuresSup, Is.EqualTo(8m));
            Assert.That(s.Nuit + s.Tranche1 + s.Tranche2 + s.FerieJour + s.FerieNuit, Is.EqualTo(8m));

            s = CalculTemps.DecompterSemaine(new DateTime(2026, 10, 5), Semaine("N", "N", "N", "N", "N", "FN", "FN"), Bareme);
            Assert.That(new[] { s.Nuit, s.Tranche1, s.FerieNuit, s.Tranche2 }, Is.EqualTo(new[] { 20m, 8m, 8m, 8m }));

            s = CalculTemps.DecompterSemaine(new DateTime(2026, 10, 5), Semaine("J", "J", "J", "J", "J", "J", "FJ"), Bareme);
            Assert.That(new[] { s.Tranche1, s.FerieJour, s.Tranche2 }, Is.EqualTo(new[] { 8m, 4m, 32m }));
        }

        [Test]
        [Microsoft.VisualStudio.TestTools.UnitTesting.TestMethod]
        public void CodeEffectif_JourFerieEtDimancheMajore()
        {
            var equipe = EquipeKS();
            var feries = new HashSet<DateTime> { new DateTime(2026, 11, 1) };
            Assert.That(CalculTemps.CodeEffectif("J", new DateTime(2026, 11, 1), feries, equipe), Is.EqualTo("FJ"));
            Assert.That(CalculTemps.CodeEffectif("N", new DateTime(2026, 11, 1), feries, equipe), Is.EqualTo("FN"));
            Assert.That(CalculTemps.CodeEffectif("J", new DateTime(2026, 11, 8), feries, equipe), Is.EqualTo("J"));

            equipe.DimancheMajore = true;
            Assert.That(CalculTemps.CodeEffectif("J", new DateTime(2026, 11, 8), new HashSet<DateTime>(), equipe), Is.EqualTo("FJ"));
        }

        [Test]
        [Microsoft.VisualStudio.TestTools.UnitTesting.TestMethod]
        public void Decompter_AbsenceJustifiee_ComptePourLeSeuilSeulementSiLeDossierLeDecide()
        {
            var equipe = new EquipeTravail { Id = 2, Code = "C", Motif = "7R", DateDebutCycle = new DateTime(2026, 1, 1) };
            var periode = PeriodePointage.Pour(2026, 10, 0);
            var jours = Planning(new DateTime(2026, 10, 5), "J J J J J J AJ");
            var salarie = new Employee { EmployeeId = "KS001", BaseSalary = 82000m };

            var sans = CalculTemps.Decompter(salarie, periode, jours, equipe, null, null, Bareme, new ParametresTemps());
            var avec = CalculTemps.Decompter(salarie, periode, jours, equipe, null, null, Bareme, new ParametresTemps { AJCompteDansSeuil = true });

            Assert.That(sans.HeuresSup, Is.EqualTo(32m));
            Assert.That(avec.HeuresSup, Is.EqualTo(40m));
            Assert.That(sans.Paniers, Is.EqualTo(6));
        }

        [Test]
        [Microsoft.VisualStudio.TestTools.UnitTesting.TestMethod]
        public void PeriodePointage_ArreteEtSemainesACheval()
        {
            var octobre = PeriodePointage.Pour(2026, 10, 20);
            Assert.That(octobre.Debut, Is.EqualTo(new DateTime(2026, 9, 21)));
            Assert.That(octobre.Fin, Is.EqualTo(new DateTime(2026, 10, 20)));
            Assert.That(PeriodePointage.Pour(2026, 1, 20).Debut, Is.EqualTo(new DateTime(2025, 12, 21)));
            Assert.That(PeriodePointage.Pour(2026, 2, 0).Fin, Is.EqualTo(new DateTime(2026, 2, 28)));
            Assert.That(GestionTempsService.MoisDePaie(new DateTime(2026, 10, 25), 20), Is.EqualTo(new DateTime(2026, 11, 1)));

            // Deux périodes consécutives : chaque semaine est décomptée une fois, et une seule.
            var a = CalculTemps.SemainesDeLaPeriode(octobre, DayOfWeek.Wednesday);
            var b = CalculTemps.SemainesDeLaPeriode(PeriodePointage.Pour(2026, 11, 20), DayOfWeek.Wednesday);
            Assert.That(a.Intersect(b), Is.Empty);
            Assert.That(b.First(), Is.EqualTo(a.Last().AddDays(7)));
        }

        [Test]
        [Microsoft.VisualStudio.TestTools.UnitTesting.TestMethod]
        public void MotifCycle_LectureEtCodeDuJour()
        {
            MotifCycle motif;
            string erreur;
            Assert.That(MotifCycle.TryParse("7r 7j 7n", out motif, out erreur), Is.True);
            Assert.That(motif.Longueur, Is.EqualTo(21));
            Assert.That(motif.Normalise, Is.EqualTo("7R 7J 7N"));
            Assert.That(motif.CodePour(new DateTime(2026, 10, 28), new DateTime(2026, 10, 21)), Is.EqualTo("J"));
            Assert.That(motif.CodePour(new DateTime(2026, 10, 20), new DateTime(2026, 10, 21)), Is.EqualTo("N"));
            Assert.That(MotifCycle.TryParse("7X 7R", out motif, out erreur), Is.False);
        }

        [Test]
        [Microsoft.VisualStudio.TestTools.UnitTesting.TestMethod]
        public void AbsencesDepuisReleve_RegroupeLesJoursConsecutifs()
        {
            var equipe = new EquipeTravail { Id = 2, Code = "C", Motif = "7R", DateDebutCycle = new DateTime(2026, 1, 1) };
            var periode = PeriodePointage.Pour(2026, 10, 0);
            var recap = CalculTemps.Decompter(new Employee { EmployeeId = "KS001" }, periode,
                Planning(new DateTime(2026, 10, 1), "A A A J J J J J A"), equipe, null, null, Bareme, null);

            var absences = CalculTemps.AbsencesDepuisReleve(recap, periode);
            Assert.That(absences.Count, Is.EqualTo(2));
            Assert.That(absences[0].NombreJours, Is.EqualTo(3m));
            Assert.That(absences[0].NombreHeures, Is.EqualTo(36m));
            Assert.That(absences[0].Commentaire, Does.StartWith(AbsenceService.MarqueurReleveTemps));
        }

        // ── PAI-03 : équivalence stricte et cinq tranches ──────────────────

        [Test]
        [Microsoft.VisualStudio.TestTools.UnitTesting.TestMethod]
        public void HeureSupplementaire_BaremeHistorique_IdentiqueAuxAnciensLitteraux()
        {
            var hs = new HeureSupplementaire { SalaireDeBase = 82000m, Heures15 = 16m, Heures30 = 44m, Heures50 = 28m, Heures100 = 3m };
            decimal th = Math.Round(82000m / HeureSupplementaire.HeuresMensuellesReference, 2);
            decimal ancien = Math.Round(16m * th * 1.15m, 0) + Math.Round(44m * th * 1.30m, 0)
                           + Math.Round(28m * th * 1.50m, 0) + Math.Round(3m * th * 2.00m, 0);

            Assert.That(hs.MontantTotal, Is.EqualTo(ancien), "taux par défaut");
            hs.AppliquerBareme(BaremeHeuresSup.Historique());
            Assert.That(hs.MontantTotal, Is.EqualTo(ancien), "barème historique");
            Assert.That(HeureSupplementaireService.LignesBulletin(hs).Select(l => l.Code),
                Is.EqualTo(new[] { "HS15", "HS30", "HS50", "HS100" }));

            hs.AppliquerBareme(Bareme);
            hs.Heures60 = 4m;
            Assert.That(hs.Montant30, Is.EqualTo(Math.Round(44m * th * 1.35m, 0)));
            Assert.That(hs.Montant60, Is.EqualTo(Math.Round(4m * th * 1.60m, 0)));
            Assert.That(hs.Montant100, Is.EqualTo(Math.Round(3m * th * 2.20m, 0)));
        }

        [Test]
        [Microsoft.VisualStudio.TestTools.UnitTesting.TestMethod]
        public void BaremeHeuresSup_ApplicableSelonLaDateDEffet()
        {
            var liste = new List<BaremeHeuresSup> { BaremeHeuresSup.Historique(), BaremeHeuresSup.CinqTranches2026() };
            Assert.That(BaremeHeuresSup.Applicable(liste, new DateTime(2026, 9, 1)).TauxTranche2, Is.EqualTo(0.30m));
            Assert.That(BaremeHeuresSup.Applicable(liste, new DateTime(2026, 10, 1)).TauxTranche2, Is.EqualTo(0.35m));
        }

        // ── PAI-04 : absences imputées ─────────────────────────────────────

        [Test]
        [Microsoft.VisualStudio.TestTools.UnitTesting.TestMethod]
        public void Absence_Imputee_CompteEnEntierDansSonMoisDePaie()
        {
            var salarie = new Employee { EmployeeId = "KS002", BaseSalary = 120000m };
            var absence = new Absence
            {
                EmployeeId = "KS002", Type = TypeAbsence.AbsenceNonRemuneree,
                DateDebut = new DateTime(2026, 9, 26), DateFin = new DateTime(2026, 9, 27),
                NombreJours = 2m, PeriodeImputation = "2026-10"
            };

            Assert.That(AbsenceService.Anomalie(absence), Is.Null, "un dimanche travaillé n'est pas une anomalie");
            Assert.That(AbsenceService.Apport(salarie, new[] { absence }, 2026, 9, BaseRetenueAbsence.TrentiemeMensuel).RetenueSurBrut, Is.EqualTo(0m));
            Assert.That(AbsenceService.Apport(salarie, new[] { absence }, 2026, 10, BaseRetenueAbsence.TrentiemeMensuel).RetenueSurBrut,
                Is.EqualTo(Math.Round(120000m / 30m * 2m, 0)));
        }

        // ── Base de données : migration 34 et chaîne complète ──────────────

        private string _dbName;
        private SQLiteService _sqlite;

        [SetUp]
        [Microsoft.VisualStudio.TestTools.UnitTesting.TestInitialize]
        public void SetUp()
        {
            string loc = typeof(GestionTempsTests).Assembly.Location;
            string dir = string.IsNullOrWhiteSpace(loc) ? Path.GetTempPath() : Path.GetDirectoryName(loc);
            _dbName = Path.Combine(dir, "test_temps_" + Guid.NewGuid().ToString("N") + ".db");
            _sqlite = new SQLiteService(_dbName);

            using (var c = _sqlite.CreateConnection())
            using (var cmd = c.CreateCommand())
            {
                cmd.CommandText =
                    "INSERT INTO Employees (EmployeeId, FirstName, LastName, BaseSalary, StatutSalarie) VALUES " +
                    "('KS001','SOUMAILA','NIKIEMA',8200000,0), ('KS002','AWA','OUEDRAOGO',12000000,0);";
                cmd.ExecuteNonQuery();
            }
        }

        [TearDown]
        [Microsoft.VisualStudio.TestTools.UnitTesting.TestCleanup]
        public void TearDown()
        {
            try
            {
                System.Data.SQLite.SQLiteConnection.ClearAllPools();
                if (File.Exists(_dbName)) File.Delete(_dbName);
            }
            catch
            {
                // Verrou temporaire : sans conséquence.
            }
        }

        [Test]
        [Microsoft.VisualStudio.TestTools.UnitTesting.TestMethod]
        public async Task DossierNeuf_BaremesLivresEtRubriquePanier()
        {
            var baremes = await new BaremeHeuresSupService(_sqlite).ChargerToutAsync();
            Assert.That(baremes.Count, Is.EqualTo(2));
            Assert.That((await new BaremeHeuresSupService(_sqlite).ApplicableAsync(2026, 9)).TauxTranche2, Is.EqualTo(0.30m));
            Assert.That((await new BaremeHeuresSupService(_sqlite).ApplicableAsync(2026, 10)).TauxTranche2, Is.EqualTo(0.35m));
            Assert.That(await new RubriqueService(_sqlite).CodeExisteAsync("PANIER"), Is.True);
        }

        [Test]
        [Microsoft.VisualStudio.TestTools.UnitTesting.TestMethod]
        public async Task ChaineComplete_PlanningDecompteEtTransferts()
        {
            var gt = new GestionTempsService(_sqlite, new DataService(_dbName));
            await gt.EnregistrerParametresAsync(new ParametresTemps { JourArrete = 20 });

            var equipe = EquipeKS();
            equipe.Id = 0;
            await gt.EnregistrerEquipeAsync(equipe);
            await gt.AffecterAsync(new[] { "KS001", "KS002" }, equipe.Id);

            var periode = await gt.PeriodeAsync(2026, 11);
            Assert.That(await gt.GenererDepuisCyclesAsync(periode, null, false), Is.EqualTo(62));

            await gt.EnregistrerPlanningAsync(new[]
            {
                new PlanningJour { EmployeeId = "KS002", Date = new DateTime(2026, 11, 5), Code = "A", Source = SourceJourPlanning.Saisie, EquipeId = equipe.Id },
                new PlanningJour { EmployeeId = "KS002", Date = new DateTime(2026, 11, 6), Code = "A", Source = SourceJourPlanning.Saisie, EquipeId = equipe.Id }
            });
            await gt.GenererDepuisCyclesAsync(periode, null, false); // ne touche pas la saisie

            var recaps = await gt.CalculerAsync(2026, 11, null);
            var ks1 = recaps.Single(r => r.EmployeeId == "KS001");
            Assert.That(new[] { ks1.Tranche1, ks1.Tranche2, ks1.Nuit }, Is.EqualTo(new[] { 16m, 44m, 28m }));
            Assert.That(recaps.Single(r => r.EmployeeId == "KS002").JoursAbsenceNonJustifiee, Is.EqualTo(2));

            await gt.TransfererHeuresSupAsync(2026, 11, recaps, periode);
            var hs = await new HeureSupplementaireService(_sqlite).ObtenirPourEmployeAsync("KS001", 2026, 11);
            Assert.That(new[] { hs.Heures15, hs.Heures30, hs.Heures50 }, Is.EqualTo(new[] { 16m, 44m, 28m }));

            await gt.TransfererAbsencesAsync(2026, 11, recaps, periode);
            await gt.TransfererAbsencesAsync(2026, 11, recaps, periode); // remplace, ne double pas
            var absences = await new AbsenceService(_sqlite).ChargerPeriodeAsync(2026, 11);
            Assert.That(absences.Count, Is.EqualTo(1));
            Assert.That(absences[0].PeriodeImputation, Is.EqualTo("2026-11"));

            await gt.TransfererPaniersAsync(2026, 11, recaps, periode);
            var paniers = (await new VariablePaieService(_sqlite).ChargerAsync(2026, 11)).Where(v => v.CodeRubrique == "PANIER").ToList();
            Assert.That(paniers.Count, Is.EqualTo(2));
            Assert.That(paniers.Single(v => v.EmployeeId == "KS001").Quantite, Is.EqualTo(17m));
        }
    }
}
