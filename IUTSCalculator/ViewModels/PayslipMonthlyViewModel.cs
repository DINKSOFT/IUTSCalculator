using System;
using System.ComponentModel;
using System.Windows.Input;
using IUTSCalculator.Models;
using IUTSCalculator.Helpers;

namespace IUTSCalculator.ViewModels
{
    /// <summary>  
    /// ViewModel pour l'affichage d'un bulletin de salaire mensuel.  
    /// </summary>      
    public class PayslipMonthlyViewModel : BaseViewModel
    {
        private PayslipMonthly _payslip;
        public PayslipMonthly Payslip
        {
            get => _payslip;
            set
            {
                if (_payslip != value)
                {
                    _payslip = value;
                    OnPropertyChanged(nameof(Payslip));
                }
            }
        }

        public ICommand PrintCommand { get; }
        public ICommand ExportPdfCommand { get; }

        public PayslipMonthlyViewModel(PayslipMonthly payslip)
        {
            Payslip = payslip ?? throw new ArgumentNullException(nameof(payslip));
            PrintCommand = new RelayCommand(OnPrint);
            ExportPdfCommand = new RelayCommand(OnExportPdf);
        }

        private void OnPrint()
        {
            // À implémenter : impression du bulletin  
        }

        private void OnExportPdf()
        {
            // À implémenter : export PDF du bulletin  
        }

        // Utilisation de la méthode OnPropertyChanged définie dans BaseViewModel  
        protected override void OnPropertyChanged(string propertyName) =>
            base.OnPropertyChanged(propertyName);
    }
}