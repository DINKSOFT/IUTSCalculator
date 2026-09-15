using NUnit.Framework;
using IUTSCalculator.Models;
using IUTSCalculator.Services;
using IUTSCalculator.ViewModels;

namespace IUTSCalculator.Tests.ViewModels
{
    [TestFixture]
    [Microsoft.VisualStudio.TestTools.UnitTesting.TestClass]
    public class MainViewModelTests
    {
        [SetUp]
        [Microsoft.VisualStudio.TestTools.UnitTesting.TestInitialize]
        public void SetUp()
        {
            // MainViewModel nécessite désormais qu'un dossier (entreprise) soit ouvert
            // (voir Services/DossierSession.cs) avant de pouvoir être instancié.
            DossierSession.Instance.Open(new Dossier
            {
                DossierId = "test-dossier",
                RaisonSociale = "Dossier de test",
                DbFileName = "iuts_data_tests.db"
            });
        }

        [Test]
        [Microsoft.VisualStudio.TestTools.UnitTesting.TestMethod]
        public void NavigateToHistoryCommand_WhenExecuted_SetsCurrentViewModelToHistoryViewModel()
        {
            // Arrange  
            var mainViewModel = new MainViewModel();

            // Act  
            mainViewModel.NavigateToHistoryCommand.Execute(null);

            // Assert  
            Assert.That(mainViewModel.CurrentViewModel, Is.Not.Null);
            Assert.That(mainViewModel.CurrentViewModel, Is.InstanceOf<HistoryViewModel>());
            Assert.That(mainViewModel.CurrentViewModel.IsActive, Is.True);
        }

        [Test]
        [Microsoft.VisualStudio.TestTools.UnitTesting.TestMethod]
        public void NavigateToOrdreVirementCommand_WhenExecuted_SetsCurrentViewModelToOrdreVirementViewModel()
        {
            // Arrange  
            var mainViewModel = new MainViewModel();

            // Act  
            mainViewModel.NavigateToOrdreVirementCommand.Execute(null);

            // Assert  
            Assert.That(mainViewModel.CurrentViewModel, Is.Not.Null);
            Assert.That(mainViewModel.CurrentViewModel, Is.InstanceOf<OrdreVirementViewModel>());
            Assert.That(mainViewModel.CurrentViewModel.IsActive, Is.True);
        }

        [Test]
        [Microsoft.VisualStudio.TestTools.UnitTesting.TestMethod]
        public void NavigateToAuditCommand_WhenExecuted_SetsCurrentViewModelToAuditViewModel()
        {
            // Arrange  
            var mainViewModel = new MainViewModel();

            // Act  
            mainViewModel.NavigateToAuditCommand.Execute(null);

            // Assert  
            Assert.That(mainViewModel.CurrentViewModel, Is.Not.Null);
            Assert.That(mainViewModel.CurrentViewModel, Is.InstanceOf<AuditViewModel>());
            Assert.That(mainViewModel.CurrentViewModel.IsActive, Is.True);
        }

        [Test]
        [Microsoft.VisualStudio.TestTools.UnitTesting.TestMethod]
        public void NavigateToConventionsCommand_WhenExecuted_SetsCurrentViewModelToConventionCollectiveViewModel()
        {
            // Arrange  
            var mainViewModel = new MainViewModel();

            // Act  
            mainViewModel.NavigateToConventionsCommand.Execute(null);

            // Assert  
            Assert.That(mainViewModel.CurrentViewModel, Is.Not.Null);
            Assert.That(mainViewModel.CurrentViewModel, Is.InstanceOf<ConventionCollectiveViewModel>());
            Assert.That(mainViewModel.CurrentViewModel.IsActive, Is.True);
        }

        [Test]
        [Microsoft.VisualStudio.TestTools.UnitTesting.TestMethod]
        public void NavigateToSoldeToutCompteCommand_WhenExecuted_SetsCurrentViewModelToSoldeToutCompteViewModel()
        {
            // Arrange  
            var mainViewModel = new MainViewModel();

            // Act  
            mainViewModel.NavigateToSoldeToutCompteCommand.Execute(null);

            // Assert  
            Assert.That(mainViewModel.CurrentViewModel, Is.Not.Null);
            Assert.That(mainViewModel.CurrentViewModel, Is.InstanceOf<SoldeToutCompteViewModel>());
            Assert.That(mainViewModel.CurrentViewModel.IsActive, Is.True);
        }

        [Test]
        [Microsoft.VisualStudio.TestTools.UnitTesting.TestMethod]
        public void NavigateToSaisiesCommand_WhenExecuted_SetsCurrentViewModelToSaisieOppositionViewModel()
        {
            // Arrange  
            var mainViewModel = new MainViewModel();

            // Act  
            mainViewModel.NavigateToSaisiesCommand.Execute(null);

            // Assert  
            Assert.That(mainViewModel.CurrentViewModel, Is.Not.Null);
            Assert.That(mainViewModel.CurrentViewModel, Is.InstanceOf<SaisieOppositionViewModel>());
            Assert.That(mainViewModel.CurrentViewModel.IsActive, Is.True);
        }

        [Test]
        [Microsoft.VisualStudio.TestTools.UnitTesting.TestMethod]
        public void NavigateToRappelsCommand_WhenExecuted_SetsCurrentViewModelToRappelPaieViewModel()
        {
            // Arrange  
            var mainViewModel = new MainViewModel();

            // Act  
            mainViewModel.NavigateToRappelsCommand.Execute(null);

            // Assert  
            Assert.That(mainViewModel.CurrentViewModel, Is.Not.Null);
            Assert.That(mainViewModel.CurrentViewModel, Is.InstanceOf<RappelPaieViewModel>());
            Assert.That(mainViewModel.CurrentViewModel.IsActive, Is.True);
        }

        [Test]
        [Microsoft.VisualStudio.TestTools.UnitTesting.TestMethod]
        public void NavigateToAvantagesNatureCommand_WhenExecuted_SetsCurrentViewModelToAvantageNatureViewModel()
        {
            // Arrange  
            var mainViewModel = new MainViewModel();

            // Act  
            mainViewModel.NavigateToAvantagesNatureCommand.Execute(null);

            // Assert  
            Assert.That(mainViewModel.CurrentViewModel, Is.Not.Null);
            Assert.That(mainViewModel.CurrentViewModel, Is.InstanceOf<AvantageNatureViewModel>());
            Assert.That(mainViewModel.CurrentViewModel.IsActive, Is.True);
        }

        [Test]
        [Microsoft.VisualStudio.TestTools.UnitTesting.TestMethod]
        public void IsNavRailVisible_DefaultValue_IsTrue()
        {
            // Arrange
            var mainViewModel = new MainViewModel();

            // Assert
            Assert.That(mainViewModel.IsNavRailVisible, Is.True);
        }

        [Test]
        [Microsoft.VisualStudio.TestTools.UnitTesting.TestMethod]
        public void ToggleNavRailCommand_WhenExecuted_TogglesVisibility()
        {
            // Arrange
            var mainViewModel = new MainViewModel();
            Assert.That(mainViewModel.IsNavRailVisible, Is.True);

            // Act: 1ère exécution -> masque le menu
            mainViewModel.ToggleNavRailCommand.Execute(null);

            // Assert
            Assert.That(mainViewModel.IsNavRailVisible, Is.False);

            // Act: 2nde exécution -> réaffiche le menu
            mainViewModel.ToggleNavRailCommand.Execute(null);

            // Assert
            Assert.That(mainViewModel.IsNavRailVisible, Is.True);
        }
    }
}
