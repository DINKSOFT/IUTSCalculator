using System.Windows.Controls;
using IUTSCalculator.ViewModels;
using IUTSCalculator.Services;


namespace IUTSCalculator.Views
{
    public partial class BatchSalaryEstimationView : UserControl
    {
        public BatchSalaryEstimationView()
        {
            InitializeComponent();

            // Fix: Provide required arguments 'dataDirectory' and 'cacheDurationMinutes' to TaxDataService constructor  
            var dataDirectory = "path/to/data/directory"; // Replace with actual path  
            var cacheDurationMinutes = 60; // Example value, adjust as needed  
            var taxDataService = new TaxDataService(dataDirectory, cacheDurationMinutes);
            var taxCalculatorService = new TaxCalculatorService(taxDataService);
            this.DataContext = new BatchSalaryEstimationViewModel(taxCalculatorService);
        }
    }
}
