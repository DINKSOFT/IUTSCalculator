using System.Windows.Controls;
using IUTSCalculator.ViewModels;
using IUTSCalculator.Models;
using System;

namespace IUTSCalculator.Views
{
    public partial class PayslipMonthlyView : UserControl
    {
        public PayslipMonthlyView(PayslipMonthly payslip)
        {
            InitializeComponent();
            DataContext = new PayslipMonthlyViewModel(payslip);
        }

        public PayslipMonthlyView() : this(new PayslipMonthly
        {
            Employee = new Employee { FirstName = "Jean", LastName = "Kaboré", Category = "A" },
            Period = new DateTime(2024, 6, 1),
            BaseSalary = 250000,
            HousingAllowance = 50000,
            FunctionAllowance = 20000,
            TransportAllowance = 15000,
            GrossSalary = 335000,
            CnssEmployee = 9000,
            CnssEmployer = 18000,
            ProfessionalTax = 2000,
            IUTS = 12000,
            NetToPay = 310000
        })
        { }
    }
}