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
            Assert.IsNotNull(mainViewModel.CurrentViewModel);
            Assert.IsInstanceOf<HistoryViewModel>(mainViewModel.CurrentViewModel);
            Assert.IsTrue(mainViewModel.CurrentViewModel.IsActive);
        }
    }
}
