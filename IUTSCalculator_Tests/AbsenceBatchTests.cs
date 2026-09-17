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
    public class AbsenceBatchTests
    {
        private string _dbName;
        private SQLiteService _sqliteService;
        private AbsenceService _absenceService;
        private AbsenceExcelService _excelService;

        [SetUp]
        [Microsoft.VisualStudio.TestTools.UnitTesting.TestInitialize]
        public void SetUp()
        {
            string loc = typeof(AbsenceBatchTests).Assembly.Location;
            string dir = string.IsNullOrWhiteSpace(loc) ? Path.GetTempPath() : Path.GetDirectoryName(loc);
            _dbName = Path.Combine(dir, "test_absence_" + Guid.NewGuid().ToString("N") + ".db");
            _sqliteService = new SQLiteService(_dbName);
            _absenceService = new AbsenceService(_sqliteService);
            _excelService = new AbsenceExcelService();
        }

        [TearDown]
        [Microsoft.VisualStudio.TestTools.UnitTesting.TestCleanup]
        public void TearDown()
        {
            try
            {
                System.Data.SQLite.SQLiteConnection.ClearAllPools();
                if (File.Exists(_sqliteService.DatabasePath))
                {
                    File.Delete(_sqliteService.DatabasePath);
                }
            }
            catch
            {
                // Ignorer si verrou temporaire
            }
        }

        [Test]
        [Microsoft.VisualStudio.TestTools.UnitTesting.TestMethod]
        public async Task EnregistrerEnMasseAsync_EnregistreDesCentainesDAbsencesEnTransaction()
        {
            // Préparer 500 absences simulant un effectif important
            var absences = new List<Absence>();
            var debut = new DateTime(2026, 9, 1);
            var fin = new DateTime(2026, 9, 15);

            for (int i = 1; i <= 500; i++)
            {
                absences.Add(new Absence
                {
                    EmployeeId = "EMP_" + i.ToString("D4"),
                    NomSalarie = "Salarié " + i,
                    Type = TypeAbsence.CongePaye,
                    DateDebut = debut,
                    DateFin = fin,
                    NombreJours = 13m,
                    NombreHeures = 0m,
                    Commentaire = "Congé annuel 2026"
                });
            }

            int inseres = await _absenceService.EnregistrerEnMasseAsync(absences);
            Assert.That(inseres, Is.EqualTo(500));

            // Vérifier que la lecture de la période retrouve bien les 500 absences
            var chargees = await _absenceService.ChargerPeriodeAsync(2026, 9);
            Assert.That(chargees.Count, Is.EqualTo(500));
        }

        [Test]
        [Microsoft.VisualStudio.TestTools.UnitTesting.TestMethod]
        public async Task ExporterModeleExcel_CreeFichierValideEtNonVide()
        {
            var salaries = new List<Employee>
            {
                new Employee { EmployeeId = "EMP001", FirstName = "Moussa", LastName = "TRAORE", Category = "Cadre", Fonction = "Ingénieur", BaseSalary = 400_000m },
                new Employee { EmployeeId = "EMP002", FirstName = "Fatou", LastName = "OUEDRAOGO", Category = "Employé", Fonction = "Comptable", BaseSalary = 250_000m }
            };

            var soldes = new List<SoldeConges>
            {
                new SoldeConges { EmployeeId = "EMP001", JoursAcquis = 30m, JoursPris = 5m },
                new SoldeConges { EmployeeId = "EMP002", JoursAcquis = 20m, JoursPris = 0m }
            };

            var dossier = new Dossier
            {
                RaisonSociale = "ENTREPRISE TEST BURKINA",
                IFU = "00012345X",
                Ville = "Ouagadougou"
            };

            string dir = Path.GetDirectoryName(typeof(AbsenceBatchTests).Assembly.Location);
            string tempExcel = Path.Combine(dir, "modele_conges_" + Guid.NewGuid().ToString("N") + ".xlsx");

            try
            {
                // 1. Exporter le modèle
                await _excelService.ExporterModeleAsync(salaries, soldes, dossier, 2026, 9, tempExcel);
                Assert.That(File.Exists(tempExcel), Is.True);
                var fi = new FileInfo(tempExcel);
                Assert.That(fi.Length, Is.GreaterThan(1000)); // Le fichier Excel a bien été écrit

                // 2. Importer le modèle vierge (doit ignorer les lignes sans jours pris sans planter)
                var resultat = await _excelService.ImporterCongesAsync(tempExcel, 2026, 9, salaries);
                Assert.That(resultat.CongesImportes, Is.EqualTo(0));
                Assert.That(resultat.LignesIgnorees, Is.EqualTo(2));
                Assert.That(resultat.Erreurs.Count, Is.EqualTo(0));
            }
            finally
            {
                if (File.Exists(tempExcel))
                {
                    try { File.Delete(tempExcel); } catch { }
                }
            }
        }

        [Test]
        [Microsoft.VisualStudio.TestTools.UnitTesting.TestMethod]
        public async Task EnregistrerEnMasseAsync_RefuseEnregistrementSiPeriodeCloturee()
        {
            // Clôturer la période 09/2026 directement en base
            using (var conn = _sqliteService.CreateConnection())
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "INSERT INTO PeriodesPaie (Annee, Mois, Statut, DateCloture, ClotureePar) " +
                                  "VALUES (2026, 9, 1, '2026-09-30', 'TestUser');";
                await cmd.ExecuteNonQueryAsync();
            }

            var absences = new List<Absence>
            {
                new Absence
                {
                    EmployeeId = "EMP001",
                    NomSalarie = "Salarié Test",
                    Type = TypeAbsence.CongePaye,
                    DateDebut = new DateTime(2026, 9, 1),
                    DateFin = new DateTime(2026, 9, 10),
                    NombreJours = 8m
                }
            };

            // Doit lever une exception car la période est verrouillée
            Assert.ThrowsAsync<InvalidOperationException>(async () =>
            {
                await _absenceService.EnregistrerEnMasseAsync(absences);
            });
        }
    }
}
