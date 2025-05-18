using NUnit.Framework;
using IUTSCalculator.ViewModels;

namespace IUTSCalculator.Tests.ViewModels
{
    [TestFixture]
    public class MainViewModelTests
    {
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
