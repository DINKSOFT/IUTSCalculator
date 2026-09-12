using NUnit.Framework;
using IUTSCalculator.Models;
using IUTSCalculator.Services;
using IUTSCalculator.ViewModels;

namespace IUTSCalculator.Tests.ViewModels
{
    [TestFixture]
    public class MainViewModelTests
    {
        [SetUp]
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
    }
}
