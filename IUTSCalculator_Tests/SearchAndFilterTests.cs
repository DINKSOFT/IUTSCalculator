using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Data;
using NUnit.Framework;
using IUTSCalculator.Models;
using IUTSCalculator.Services;
using IUTSCalculator.ViewModels;

namespace IUTSCalculator.Tests
{
    [TestFixture]
    [Microsoft.VisualStudio.TestTools.UnitTesting.TestClass]
    [Apartment(ApartmentState.STA)]
    public class SearchAndFilterTests
    {
        private string _dbPath;
        private SQLiteService _sqliteService;
        private DataService _dataService;

        [SetUp]
        [Microsoft.VisualStudio.TestTools.UnitTesting.TestInitialize]
        public void SetUp()
        {
            string loc = typeof(SearchAndFilterTests).Assembly.Location;
            string dir = string.IsNullOrWhiteSpace(loc) ? Path.GetTempPath() : Path.GetDirectoryName(loc);
            _dbPath = Path.Combine(dir, "test_search_" + Guid.NewGuid().ToString("N") + ".db");
            _sqliteService = new SQLiteService(_dbPath);
            _dataService = new DataService(_dbPath);

            DossierSession.Instance.Open(new Dossier
            {
                DossierId = "test-dossier-search",
                RaisonSociale = "Entreprise Test Recherche",
                DbFileName = Path.GetFileName(_dbPath)
            });
        }

        [TearDown]
        [Microsoft.VisualStudio.TestTools.UnitTesting.TestCleanup]
        public void TearDown()
        {
            try
            {
                System.Data.SQLite.SQLiteConnection.ClearAllPools();
                if (File.Exists(_dbPath))
                {
                    File.Delete(_dbPath);
                }
            }
            catch
            {
                // Nettoyage silencieux
            }
        }

        #region 1. EmployeeManagementViewModel Tests

        [Test]
        [Microsoft.VisualStudio.TestTools.UnitTesting.TestMethod]
        public void EmployeeManagement_FiltreParTexteMultiChamps()
        {
            var vm = new EmployeeManagementViewModel(_dataService);
            vm.Employees.Clear();

            var emp1 = new Employee { EmployeeId = "EMP001", FirstName = "Moussa", LastName = "TRAORE", Fonction = "Ingénieur Logiciel", Category = "Cadre", Email = "moussa@corp.com" };
            var emp2 = new Employee { EmployeeId = "EMP002", FirstName = "Fatou", LastName = "OUEDRAOGO", Fonction = "Comptable", Category = "Employé", Telephone = "+226 70 00 00 01" };
            var emp3 = new Employee { EmployeeId = "EMP003", FirstName = "Ibrahim", LastName = "SAWADOGO", Fonction = "Directeur RH", Category = "Cadre Supérieur", NumeroIfu = "0001234567A" };

            vm.Employees.Add(emp1);
            vm.Employees.Add(emp2);
            vm.Employees.Add(emp3);

            // Recherche par nom de famille (insensible à la casse)
            vm.SearchText = "traore";
            var visibles = vm.EmployeesView.Cast<Employee>().ToList();
            Assert.That(visibles.Count, Is.EqualTo(1));
            Assert.That(visibles[0].EmployeeId, Is.EqualTo("EMP001"));
            Assert.That(vm.NombreSalariesAffiches, Is.EqualTo(1));

            // Recherche par prénom
            vm.SearchText = "fatou";
            visibles = vm.EmployeesView.Cast<Employee>().ToList();
            Assert.That(visibles.Count, Is.EqualTo(1));
            Assert.That(visibles[0].EmployeeId, Is.EqualTo("EMP002"));

            // Recherche par matricule
            vm.SearchText = "EMP003";
            visibles = vm.EmployeesView.Cast<Employee>().ToList();
            Assert.That(visibles.Count, Is.EqualTo(1));
            Assert.That(visibles[0].EmployeeId, Is.EqualTo("EMP003"));

            // Recherche par fonction
            vm.SearchText = "comptable";
            visibles = vm.EmployeesView.Cast<Employee>().ToList();
            Assert.That(visibles.Count, Is.EqualTo(1));
            Assert.That(visibles[0].EmployeeId, Is.EqualTo("EMP002"));

            // Recherche par IFU
            vm.SearchText = "1234567";
            visibles = vm.EmployeesView.Cast<Employee>().ToList();
            Assert.That(visibles.Count, Is.EqualTo(1));
            Assert.That(visibles[0].EmployeeId, Is.EqualTo("EMP003"));

            // Effacer la recherche
            vm.ClearSearchCommand.Execute(null);
            visibles = vm.EmployeesView.Cast<Employee>().ToList();
            Assert.That(visibles.Count, Is.EqualTo(3));
            Assert.That(vm.HasSearchText, Is.False);
        }

        [Test]
        [Microsoft.VisualStudio.TestTools.UnitTesting.TestMethod]
        public void EmployeeManagement_FiltreParStatutEtCategorie()
        {
            var vm = new EmployeeManagementViewModel(_dataService);
            vm.Employees.Clear();

            var emp1 = new Employee { EmployeeId = "EMP001", FirstName = "A", LastName = "B", Category = "Cadre", StatutSalarie = 0 };
            var emp2 = new Employee { EmployeeId = "EMP002", FirstName = "C", LastName = "D", Category = "Employé", StatutSalarie = 1 };
            var emp3 = new Employee { EmployeeId = "EMP003", FirstName = "E", LastName = "F", Category = "Cadre", StatutSalarie = 2 };

            vm.Employees.Add(emp1);
            vm.Employees.Add(emp2);
            vm.Employees.Add(emp3);

            // Filtre statut Actif
            vm.SelectedStatutFilter = 0;
            var visibles = vm.EmployeesView.Cast<Employee>().ToList();
            Assert.That(visibles.Count, Is.EqualTo(1));
            Assert.That(visibles[0].EmployeeId, Is.EqualTo("EMP001"));

            // Filtre statut + catégorie
            vm.SelectedStatutFilter = null;
            vm.SelectedCategoryFilter = "Cadre";
            visibles = vm.EmployeesView.Cast<Employee>().ToList();
            Assert.That(visibles.Count, Is.EqualTo(2));

            // Réinitialisation complète
            vm.ResetFiltersCommand.Execute(null);
            visibles = vm.EmployeesView.Cast<Employee>().ToList();
            Assert.That(visibles.Count, Is.EqualTo(3));
            Assert.That(vm.SelectedStatutFilter, Is.Null);
            Assert.That(vm.SelectedCategoryFilter, Is.EqualTo("Toutes les catégories"));
        }

        #endregion

        #region 2. RubriqueViewModel Tests

        [Test]
        [Microsoft.VisualStudio.TestTools.UnitTesting.TestMethod]
        public void Rubrique_FiltreParTexteEtSens()
        {
            var rubService = new RubriqueService(_sqliteService);
            var vm = new RubriqueViewModel(rubService);
            vm.Rubriques.Clear();

            var r1 = new Rubrique { Code = "R100", Libelle = "Salaire de base", Sens = SensRubrique.Gain, CompteComptable = "641100" };
            var r2 = new Rubrique { Code = "R200", Libelle = "Indemnité de transport", Sens = SensRubrique.Gain, CompteComptable = "641300" };
            var r3 = new Rubrique { Code = "R500", Libelle = "Cotisation CNSS Ouvrière", Sens = SensRubrique.Retenue, CompteComptable = "431100" };
            var r4 = new Rubrique { Code = "R900", Libelle = "Heures d'absence décomptées", Sens = SensRubrique.Information };

            vm.Rubriques.Add(r1);
            vm.Rubriques.Add(r2);
            vm.Rubriques.Add(r3);
            vm.Rubriques.Add(r4);

            // Recherche textuelle sur libellé
            vm.SearchText = "transport";
            var visibles = vm.RubriquesView.Cast<Rubrique>().ToList();
            Assert.That(visibles.Count, Is.EqualTo(1));
            Assert.That(visibles[0].Code, Is.EqualTo("R200"));

            // Recherche sur compte comptable
            vm.SearchText = "431100";
            visibles = vm.RubriquesView.Cast<Rubrique>().ToList();
            Assert.That(visibles.Count, Is.EqualTo(1));
            Assert.That(visibles[0].Code, Is.EqualTo("R500"));

            // Filtre par Sens : Retenues
            vm.SearchText = string.Empty;
            vm.SelectedSensFilter = SensRubrique.Retenue;
            visibles = vm.RubriquesView.Cast<Rubrique>().ToList();
            Assert.That(visibles.Count, Is.EqualTo(1));
            Assert.That(visibles[0].Code, Is.EqualTo("R500"));

            // Filtre par Sens : Informations
            vm.SelectedSensFilter = SensRubrique.Information;
            visibles = vm.RubriquesView.Cast<Rubrique>().ToList();
            Assert.That(visibles.Count, Is.EqualTo(1));
            Assert.That(visibles[0].Code, Is.EqualTo("R900"));

            // Reset
            vm.ResetFiltersCommand.Execute(null);
            visibles = vm.RubriquesView.Cast<Rubrique>().ToList();
            Assert.That(visibles.Count, Is.EqualTo(4));
            Assert.That(vm.NombreRubriquesAffichees, Is.EqualTo(4));
        }

        #endregion

        #region 3. BulletinViewModel Tests

        [Test]
        [Microsoft.VisualStudio.TestTools.UnitTesting.TestMethod]
        public void Bulletin_RechercheRapideSalarieFiltreListe()
        {
            var taxDataService = new TaxDataService(AppDomain.CurrentDomain.BaseDirectory);
            var taxCalculatorService = new TaxCalculatorService(taxDataService);
            var bulletinService = new BulletinService(taxCalculatorService);
            var exportService = new BulletinExportService();
            var vm = new BulletinViewModel(_dataService, bulletinService, exportService);

            vm.Employees.Clear();
            var e1 = new Employee { EmployeeId = "EMP001", FirstName = "Moussa", LastName = "TRAORE", Fonction = "Directeur" };
            var e2 = new Employee { EmployeeId = "EMP002", FirstName = "Fatou", LastName = "OUEDRAOGO", Fonction = "Comptable" };
            var e3 = new Employee { EmployeeId = "EMP003", FirstName = "Moussa", LastName = "KABORE", Fonction = "Chauffeur" };

            vm.Employees.Add(e1);
            vm.Employees.Add(e2);
            vm.Employees.Add(e3);

            // Filtre avec saisie "Moussa" -> Doit retourner 2 salariés
            vm.FiltreSalarie = "Moussa";
            Assert.That(vm.EmployeesFiltres.Count, Is.EqualTo(2));
            Assert.That(vm.EmployeesFiltres.Any(e => e.EmployeeId == "EMP001"), Is.True);
            Assert.That(vm.EmployeesFiltres.Any(e => e.EmployeeId == "EMP003"), Is.True);

            // Filtre avec matricule "EMP002"
            vm.FiltreSalarie = "EMP002";
            Assert.That(vm.EmployeesFiltres.Count, Is.EqualTo(1));
            Assert.That(vm.EmployeesFiltres[0].LastName, Is.EqualTo("OUEDRAOGO"));

            // Effacer avec commande
            vm.ClearFiltreSalarieCommand.Execute(null);
            Assert.That(vm.EmployeesFiltres.Count, Is.EqualTo(3));
            Assert.That(string.IsNullOrEmpty(vm.FiltreSalarie), Is.True);
        }

        #endregion

        #region 4. VariablePaieViewModel Tests

        [Test]
        [Microsoft.VisualStudio.TestTools.UnitTesting.TestMethod]
        public void VariablePaie_FiltreMultiCriteres()
        {
            var varService = new VariablePaieService(_sqliteService);
            var rubService = new RubriqueService(_sqliteService);
            var vm = new VariablePaieViewModel(varService, rubService, _dataService);

            vm.Variables.Clear();
            var v1 = new VariablePaie { EmployeeId = "EMP001", NomSalarie = "TRAORE Moussa", CodeRubrique = "R200", LibelleRubrique = "Prime rendement", Sens = SensRubrique.Gain, Montant = 50_000m };
            var v2 = new VariablePaie { EmployeeId = "EMP002", NomSalarie = "OUEDRAOGO Fatou", CodeRubrique = "R300", LibelleRubrique = "Heures supplémentaires", Sens = SensRubrique.Gain, Montant = 35_000m };
            var v3 = new VariablePaie { EmployeeId = "EMP001", NomSalarie = "TRAORE Moussa", CodeRubrique = "R550", LibelleRubrique = "Avance quinzaine", Sens = SensRubrique.Retenue, Montant = 20_000m };

            vm.Variables.Add(v1);
            vm.Variables.Add(v2);
            vm.Variables.Add(v3);

            // Recherche sur nom salarié
            vm.SearchText = "Fatou";
            var visibles = vm.VariablesView.Cast<VariablePaie>().ToList();
            Assert.That(visibles.Count, Is.EqualTo(1));
            Assert.That(visibles[0].CodeRubrique, Is.EqualTo("R300"));

            // Recherche sur libellé rubrique
            vm.SearchText = "quinzaine";
            visibles = vm.VariablesView.Cast<VariablePaie>().ToList();
            Assert.That(visibles.Count, Is.EqualTo(1));
            Assert.That(visibles[0].EmployeeId, Is.EqualTo("EMP001"));

            // Filtre par Sens : Retenues
            vm.SearchText = string.Empty;
            vm.SelectedSensFilter = SensRubrique.Retenue;
            visibles = vm.VariablesView.Cast<VariablePaie>().ToList();
            Assert.That(visibles.Count, Is.EqualTo(1));
            Assert.That(visibles[0].CodeRubrique, Is.EqualTo("R550"));

            // Reset
            vm.ResetFiltersCommand.Execute(null);
            visibles = vm.VariablesView.Cast<VariablePaie>().ToList();
            Assert.That(visibles.Count, Is.EqualTo(3));
        }

        #endregion

        #region 5. AbsenceViewModel Tests

        [Test]
        [Microsoft.VisualStudio.TestTools.UnitTesting.TestMethod]
        public void Absence_FiltresEtSelectionSalarie()
        {
            var absenceService = new AbsenceService(_sqliteService);
            var vm = new AbsenceViewModel(absenceService, _dataService, new AbsenceExcelService());

            vm.Absences.Clear();
            var a1 = new Absence { EmployeeId = "EMP001", NomSalarie = "Moussa TRAORE", Type = TypeAbsence.CongePaye, DateDebut = DateTime.Today, DateFin = DateTime.Today.AddDays(5), NombreJours = 5m, Commentaire = "Vacances annuelles" };
            var a2 = new Absence { EmployeeId = "EMP002", NomSalarie = "Fatou OUEDRAOGO", Type = TypeAbsence.Maladie, DateDebut = DateTime.Today, DateFin = DateTime.Today.AddDays(2), NombreJours = 2m, Commentaire = "Certificat médical" };
            var a3 = new Absence { EmployeeId = "EMP001", NomSalarie = "Moussa TRAORE", Type = TypeAbsence.Maternite, DateDebut = DateTime.Today, DateFin = DateTime.Today.AddDays(90), NombreJours = 90m, Commentaire = "Maternité" };

            vm.Absences.Add(a1);
            vm.Absences.Add(a2);
            vm.Absences.Add(a3);

            // Recherche texte
            vm.SearchText = "médical";
            var visibles = vm.AbsencesView.Cast<Absence>().ToList();
            Assert.That(visibles.Count, Is.EqualTo(1));
            Assert.That(visibles[0].NomSalarie, Is.EqualTo("Fatou OUEDRAOGO"));

            // Filtre par Type d'absence
            vm.SearchText = string.Empty;
            vm.SelectedTypeFilter = TypeAbsence.CongePaye;
            visibles = vm.AbsencesView.Cast<Absence>().ToList();
            Assert.That(visibles.Count, Is.EqualTo(1));
            Assert.That(visibles[0].EmployeeId, Is.EqualTo("EMP001"));

            // Test filtre rapide salarié du formulaire de création
            vm.Salaries.Clear();
            vm.Salaries.Add(new Employee { EmployeeId = "EMP001", FirstName = "Moussa", LastName = "TRAORE" });
            vm.Salaries.Add(new Employee { EmployeeId = "EMP002", FirstName = "Fatou", LastName = "OUEDRAOGO" });

            vm.FiltreSalarie = "Fatou";
            Assert.That(vm.SalariesFiltres.Count, Is.EqualTo(1));
            Assert.That(vm.SalariesFiltres[0].EmployeeId, Is.EqualTo("EMP002"));

            vm.ClearFiltreSalarieCommand.Execute(null);
            Assert.That(vm.SalariesFiltres.Count, Is.EqualTo(2));
        }

        #endregion

        #region 6. PretViewModel Tests

        [Test]
        [Microsoft.VisualStudio.TestTools.UnitTesting.TestMethod]
        public void Pret_FiltresEtSelectionSalarie()
        {
            var pretService = new PretService(_sqliteService);
            var vm = new PretViewModel(pretService, _dataService);

            vm.Situations.Clear();
            var s1 = new SituationPret { EmployeeId = "EMP001", NomSalarie = "TRAORE Moussa", Type = TypePret.Pret, Statut = StatutPret.EnCours, MontantAccorde = 500_000m, TotalRetenu = 250_000m };
            var s2 = new SituationPret { EmployeeId = "EMP002", NomSalarie = "OUEDRAOGO Fatou", Type = TypePret.AvanceSurSalaire, Statut = StatutPret.EnCours, MontantAccorde = 100_000m, TotalRetenu = 100_000m };
            var s3 = new SituationPret { EmployeeId = "EMP003", NomSalarie = "KABORE Paul", Type = TypePret.Acompte, Statut = StatutPret.EnCours, MontantAccorde = 50_000m, TotalRetenu = 0m };

            vm.Situations.Add(s1);
            vm.Situations.Add(s2);
            vm.Situations.Add(s3);

            // Recherche texte
            vm.SearchText = "Paul";
            var visibles = vm.SituationsView.Cast<SituationPret>().ToList();
            Assert.That(visibles.Count, Is.EqualTo(1));
            Assert.That(visibles[0].EmployeeId, Is.EqualTo("EMP003"));

            // Filtre par Nature de prêt
            vm.SearchText = string.Empty;
            vm.SelectedTypeFilter = TypePret.Pret;
            visibles = vm.SituationsView.Cast<SituationPret>().ToList();
            Assert.That(visibles.Count, Is.EqualTo(1));
            Assert.That(visibles[0].EmployeeId, Is.EqualTo("EMP001"));

            // Filtre par Statut Soldé
            vm.SelectedTypeFilter = null;
            vm.SelectedStatutFilter = "Soldés uniquement";
            visibles = vm.SituationsView.Cast<SituationPret>().ToList();
            Assert.That(visibles.Count, Is.EqualTo(1));
            Assert.That(visibles[0].EmployeeId, Is.EqualTo("EMP002"));

            // Filtre rapide salarié formulaire
            vm.Salaries.Clear();
            vm.Salaries.Add(new Employee { EmployeeId = "EMP001", FirstName = "Moussa", LastName = "TRAORE" });
            vm.Salaries.Add(new Employee { EmployeeId = "EMP002", FirstName = "Fatou", LastName = "OUEDRAOGO" });

            vm.FiltreSalarie = "Moussa";
            Assert.That(vm.SalariesFiltres.Count, Is.EqualTo(1));
            Assert.That(vm.SalariesFiltres[0].EmployeeId, Is.EqualTo("EMP001"));

            vm.ClearFiltreSalarieCommand.Execute(null);
            Assert.That(vm.SalariesFiltres.Count, Is.EqualTo(2));
        }

        #endregion

        #region 7. ProfilPaieViewModel Tests

        [Test]
        [Microsoft.VisualStudio.TestTools.UnitTesting.TestMethod]
        public void ProfilPaie_FiltresEtSelectionSalarie()
        {
            var profilService = new ProfilPaieService(_sqliteService);
            var rubriqueService = new RubriqueService(_sqliteService);
            var vm = new ProfilPaieViewModel(profilService, rubriqueService, _dataService);

            vm.Profils.Clear();
            var p1 = new ProfilPaie { Code = "CADRE", Libelle = "Cadre Dirigeant & Supérieur", Population = PopulationProfil.Cadre, Actif = true };
            var p2 = new ProfilPaie { Code = "NON_CADRE", Libelle = "Employé & Ouvrier", Population = PopulationProfil.Employe, Actif = true };
            var p3 = new ProfilPaie { Code = "STAGIAIRE", Libelle = "Stagiaire Professionnel", Population = PopulationProfil.Stagiaire, Actif = true };

            vm.Profils.Add(p1);
            vm.Profils.Add(p2);
            vm.Profils.Add(p3);

            // Recherche texte
            vm.SearchText = "Ouvrier";
            var visibles = vm.ProfilsView.Cast<ProfilPaie>().ToList();
            Assert.That(visibles.Count, Is.EqualTo(1));
            Assert.That(visibles[0].Code, Is.EqualTo("NON_CADRE"));

            // Filtre par Population
            vm.SearchText = string.Empty;
            vm.SelectedPopulationFilter = PopulationProfil.Cadre;
            visibles = vm.ProfilsView.Cast<ProfilPaie>().ToList();
            Assert.That(visibles.Count, Is.EqualTo(1));
            Assert.That(visibles[0].Code, Is.EqualTo("CADRE"));

            // Filtre rapide salarié pour rattachement
            vm.Salaries.Clear();
            vm.Salaries.Add(new Employee { EmployeeId = "EMP001", FirstName = "Moussa", LastName = "TRAORE" });
            vm.Salaries.Add(new Employee { EmployeeId = "EMP002", FirstName = "Fatou", LastName = "OUEDRAOGO" });

            vm.FiltreSalarie = "OUEDRAOGO";
            Assert.That(vm.SalariesFiltres.Count, Is.EqualTo(1));
            Assert.That(vm.SalariesFiltres[0].EmployeeId, Is.EqualTo("EMP002"));

            vm.ClearFiltreSalarieCommand.Execute(null);
            Assert.That(vm.SalariesFiltres.Count, Is.EqualTo(2));
        }

        #endregion
    }
}
