using System.Windows.Controls;
using IUTSCalculator.ViewModels;
using IUTSCalculator.Services;

namespace IUTSCalculator.Views
{
    /// <summary>
    /// Logique d'interaction pour BatchSalaryCalculationView.xaml
    /// </summary>
    public partial class BatchSalaryCalculationView : UserControl
    {
        public BatchSalaryCalculationView()
        {
            InitializeComponent();
        }

        /// <summary>
        /// Constructeur avec injection de dépendances pour le ViewModel
        /// </summary>
        /// <param name="dataService">Service de données</param>
        /// <param name="taxCalculatorService">Service de calcul des impôts</param>
        public BatchSalaryCalculationView(DataService dataService, TaxCalculatorService taxCalculatorService)
            : this()
        {
            DataContext = new BatchSalaryCalculationViewModel(dataService, taxCalculatorService);
        }

        /// <summary>
        /// Constructeur avec ViewModel directement fourni
        /// </summary>
        /// <param name="viewModel">ViewModel à utiliser</param>
        public BatchSalaryCalculationView(BatchSalaryCalculationViewModel viewModel)
            : this()
        {
            DataContext = viewModel;
        }
    }
}