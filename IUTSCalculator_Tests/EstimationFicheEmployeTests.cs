using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using IUTSCalculator.Models;
using IUTSCalculator.Services;
using IUTSCalculator.ViewModels;

namespace IUTSCalculator.Tests
{
    /// <summary>
    /// Estimation du salaire de base → création d'une fiche employé pré-remplie.
    /// </summary>
    [TestFixture]
    [Microsoft.VisualStudio.TestTools.UnitTesting.TestClass]
    [Apartment(ApartmentState.STA)]
    public class EstimationFicheEmployeTests
    {
        private string _dbPath;
        private DataService _dataService;
        private TaxCalculatorService _taxCalculatorService;

        [SetUp]
        [Microsoft.VisualStudio.TestTools.UnitTesting.TestInitialize]
        public void SetUp()
        {
            string loc = typeof(EstimationFicheEmployeTests).Assembly.Location;
            string dir = string.IsNullOrWhiteSpace(loc) ? Path.GetTempPath() : Path.GetDirectoryName(loc);
            _dbPath = Path.Combine(dir, "test_estim_fiche_" + Guid.NewGuid().ToString("N") + ".db");
            _dataService = new DataService(_dbPath);
            _taxCalculatorService = new TaxCalculatorService(new TaxDataService(AppDomain.CurrentDomain.BaseDirectory));

            DossierSession.Instance.Open(new Dossier
            {
                DossierId = "test-dossier-estim-fiche",
                RaisonSociale = "Entreprise Test Estimation",
                DbFileName = Path.GetFileName(_dbPath)
            });

            UserSession.Instance.Login(new UserAccount { Username = "saisie", Role = UserRole.Editor });
        }

        [TearDown]
        [Microsoft.VisualStudio.TestTools.UnitTesting.TestCleanup]
        public void TearDown()
        {
            UserSession.Instance.Logout();
            try
            {
                Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
                if (File.Exists(_dbPath))
                    File.Delete(_dbPath);
            }
            catch
            {
                // Nettoyage silencieux
            }
        }

        private BaseSalaryEstimatorViewModel NouvelEstimateur()
        {
            return new BaseSalaryEstimatorViewModel(_dataService, _taxCalculatorService)
            {
                NetSalary = 350000m,
                Category = "Cadre",
                FamilyCharges = 2,
                IsSpouseWorking = true
            };
        }

        [Test]
        [Microsoft.VisualStudio.TestTools.UnitTesting.TestMethod]
        public void CreerFiche_InactifAvantEstimation()
        {
            var vm = NouvelEstimateur();

            Assert.That(vm.EstimationDisponible, Is.False);
            Assert.That(vm.CreerFicheEmployeCommand.CanExecute(null), Is.False);
            Assert.That(vm.ConstruireFicheEmploye(), Is.Null);
        }

        [Test]
        [Microsoft.VisualStudio.TestTools.UnitTesting.TestMethod]
        public async Task CreerFiche_ApresEstimation_RepriseDesValeursCalculees()
        {
            var vm = NouvelEstimateur();
            await vm.EstimateBaseSalaryAsync();

            Assert.That(vm.EstimationDisponible, Is.True, vm.StatusMessage);
            Assert.That(vm.CreerFicheEmployeCommand.CanExecute(null), Is.True);

            Employee recue = null;
            vm.CreationFicheDemandee += (s, fiche) => recue = fiche;
            vm.CreerFicheEmployeCommand.Execute(null);

            Assert.That(recue, Is.Not.Null);
            Assert.That(recue.BaseSalary, Is.EqualTo(vm.EstimatedBaseSalary));
            Assert.That(recue.BaseSalary, Is.GreaterThan(0m));
            Assert.That(recue.HousingAllowance, Is.EqualTo(vm.EstimatedHousing));
            Assert.That(recue.FunctionAllowance, Is.EqualTo(vm.EstimatedFunction));
            Assert.That(recue.TransportAllowance, Is.EqualTo(vm.EstimatedTransport));
            Assert.That(recue.Category, Is.EqualTo("Cadre"));
            Assert.That(recue.FamilyCharges, Is.EqualTo(2));
            Assert.That(recue.IsSpouseWorking, Is.True);
            Assert.That(recue.SituationMatrimoniale, Is.EqualTo("Marié(e)"));
            // L'identité reste à saisir : la fiche n'est pas encore un salarié.
            Assert.That(string.IsNullOrEmpty(recue.EmployeeId), Is.True);
            Assert.That(string.IsNullOrEmpty(recue.LastName), Is.True);
        }

        [Test]
        [Microsoft.VisualStudio.TestTools.UnitTesting.TestMethod]
        public async Task CreerFiche_HypothesesModifieesApres_FicheResteFideleAuResultat()
        {
            var vm = NouvelEstimateur();
            await vm.EstimateBaseSalaryAsync();
            decimal base1 = vm.EstimatedBaseSalary;

            // Hypothèses changées sans relancer l'estimation.
            vm.Category = "Non-cadre";
            vm.FamilyCharges = 0;

            var fiche = vm.ConstruireFicheEmploye();
            Assert.That(fiche.Category, Is.EqualTo("Cadre"));
            Assert.That(fiche.FamilyCharges, Is.EqualTo(2));
            Assert.That(fiche.BaseSalary, Is.EqualTo(base1));
        }

        [Test]
        [Microsoft.VisualStudio.TestTools.UnitTesting.TestMethod]
        public async Task CreerFiche_LectureSeule_Inactif()
        {
            var vm = NouvelEstimateur();
            await vm.EstimateBaseSalaryAsync();

            UserSession.Instance.Login(new UserAccount { Username = "lecteur", Role = UserRole.ReadOnly });

            Assert.That(vm.CreerFicheEmployeCommand.CanExecute(null), Is.False);
        }

        [Test]
        [Microsoft.VisualStudio.TestTools.UnitTesting.TestMethod]
        public async Task CreerFiche_EstimationEchouee_Inactif()
        {
            var vm = NouvelEstimateur();
            await vm.EstimateBaseSalaryAsync();
            Assert.That(vm.EstimationDisponible, Is.True);

            // Un net nul est refusé par l'estimateur : l'ancien résultat ne doit plus servir.
            vm.NetSalary = 0m;
            await vm.EstimateBaseSalaryAsync();

            Assert.That(vm.EstimationDisponible, Is.False);
            Assert.That(vm.CreerFicheEmployeCommand.CanExecute(null), Is.False);
        }

        [Test]
        [Microsoft.VisualStudio.TestTools.UnitTesting.TestMethod]
        public async Task FichePreRemplie_EnregistreeCommeUneCreationNormale()
        {
            var estimateur = NouvelEstimateur();
            await estimateur.EstimateBaseSalaryAsync();
            var fiche = estimateur.ConstruireFicheEmploye();

            var gestion = new EmployeeManagementViewModel(_dataService);
            // Le constructeur lance le chargement de la liste, qui occupe l'écran (IsBusy).
            var attente = Stopwatch.StartNew();
            while (gestion.IsBusy && attente.Elapsed < TimeSpan.FromSeconds(10))
                await Task.Delay(50);

            gestion.PreparerCreation(fiche);

            Assert.That(gestion.EstEnEdition, Is.False);
            Assert.That(gestion.FicheCourante, Is.SameAs(fiche));
            // Sans identité, « Ajouter » reste grisé comme pour une fiche vierge.
            Assert.That(gestion.AddEmployeeCommand.CanExecute(null), Is.False);

            fiche.LastName = "SAWADOGO";
            fiche.FirstName = "Awa";
            Assert.That(gestion.AddEmployeeCommand.CanExecute(null), Is.True);
            gestion.AddEmployeeCommand.Execute(null);

            Employee enregistre = null;
            var chrono = Stopwatch.StartNew();
            while (enregistre == null && chrono.Elapsed < TimeSpan.FromSeconds(10))
            {
                await Task.Delay(100);
                var liste = await _dataService.LoadEmployeesAsync();
                enregistre = liste.FirstOrDefault(e => e.LastName == "SAWADOGO");
            }

            Assert.That(enregistre, Is.Not.Null, gestion.StatusMessage);
            Assert.That(string.IsNullOrEmpty(enregistre.EmployeeId), Is.False);
            Assert.That(enregistre.BaseSalary, Is.EqualTo(estimateur.EstimatedBaseSalary));
            Assert.That(enregistre.HousingAllowance, Is.EqualTo(estimateur.EstimatedHousing));
            Assert.That(enregistre.FunctionAllowance, Is.EqualTo(estimateur.EstimatedFunction));
            Assert.That(enregistre.TransportAllowance, Is.EqualTo(estimateur.EstimatedTransport));
            Assert.That(enregistre.Category, Is.EqualTo("Cadre"));
            Assert.That(enregistre.FamilyCharges, Is.EqualTo(2));
        }
    }
}
