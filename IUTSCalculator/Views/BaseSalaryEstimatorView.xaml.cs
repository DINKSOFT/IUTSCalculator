using System.Windows;
using IUTSCalculator.Services;
using IUTSCalculator.ViewModels;

namespace IUTSCalculator.Views
{
    public partial class BaseSalaryEstimatorView : Window
    {
        public BaseSalaryEstimatorView(TaxCalculatorService taxCalculatorService)
        {
            InitializeComponent();
            DataContext = new BaseSalaryEstimatorViewModel(taxCalculatorService);
        }
    }
}