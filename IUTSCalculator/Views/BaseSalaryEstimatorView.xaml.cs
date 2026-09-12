using System.Windows.Controls;
using IUTSCalculator.ViewModels;
using IUTSCalculator.Services;

namespace IUTSCalculator.Views
{
    /// <summary>
    /// Logique d'interaction pour BaseSalaryEstimatorView.xaml (UserControl)
    /// </summary>
    public partial class BaseSalaryEstimatorView : UserControl
    {
        public BaseSalaryEstimatorView()
        {
            InitializeComponent();
        }

        // Si vous souhaitez injecter le ViewModel manuellement :
        public BaseSalaryEstimatorView(DataService dataService, TaxCalculatorService taxCalculatorService)
            : this()
        {
            DataContext = new BaseSalaryEstimatorViewModel(dataService, taxCalculatorService);
        }

        private void TextBox_TextChanged(object sender, TextChangedEventArgs e)
        {

        }
    }
}