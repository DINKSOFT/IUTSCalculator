using System;
using System.Collections.ObjectModel;
using System.Windows.Input;
using IUTSCalculator.Helpers;
using IUTSCalculator.Models;
using IUTSCalculator.Services;
using System.Threading.Tasks;
using Microsoft.Win32;
using System.IO;
using CsvHelper;
using System.Globalization;
using System.Linq;
using System.Collections.Generic;

namespace IUTSCalculator.ViewModels
{
    /// <summary>
    /// ViewModel pour l'import CSV, l'estimation en masse et l'export Excel des salaires de base
    /// </summary>
    public class BatchSalaryEstimationViewModel : BaseViewModel
    {
        private readonly TaxCalculatorService _taxCalculatorService;

        public BatchSalaryEstimationViewModel(TaxCalculatorService taxCalculatorService)
        {
            _taxCalculatorService = taxCalculatorService ?? throw new ArgumentNullException(nameof(taxCalculatorService));
            Title = "Estimation de salaires en lot";
            Employees = new ObservableCollection<Employee>();
            InitializeCommands();
        }

        public ObservableCollection<Employee> Employees { get; }

        public ICommand ImportCsvAndBatchEstimateCommand { get; private set; }
        public ICommand ExportBatchToExcelCommand { get; private set; }

        private void InitializeCommands()
        {
            ImportCsvAndBatchEstimateCommand = new RelayCommand(
                execute: async () => await ImportCsvAndBatchEstimateAsync(),
                canExecute: () => !IsBusy
            );

            ExportBatchToExcelCommand = new RelayCommand(
                execute: () => ExportBatchToExcel(),
                canExecute: () => !IsBusy && Employees.Any()
            );
        }

        /// <summary>
        /// Importe un fichier CSV, estime les salaires de base pour chaque ligne, et prépare l'export en masse.
        /// </summary>
        private async Task ImportCsvAndBatchEstimateAsync()
        {
            try
            {
                IsBusy = true;
                StatusMessage = "Import du CSV et estimation en masse...";

                var openFileDialog = new OpenFileDialog
                {
                    Filter = "Fichiers CSV (*.csv)|*.csv|Tous les fichiers (*.*)|*.*",
                    DefaultExt = "csv",
                    Title = "Sélectionnez le fichier CSV à importer"
                };

                if (openFileDialog.ShowDialog() != true)
                {
                    StatusMessage = "Import annulé.";
                    return;
                }

                var employees = new List<Employee>();

                await Task.Run(() =>
                {
                    using (var reader = new StreamReader(openFileDialog.FileName))
                    using (var csv = new CsvReader(reader, CultureInfo.InvariantCulture))
                    {
                        var records = csv.GetRecords<dynamic>().ToList();

                        foreach (var record in records)
                        {
                            try
                            {
                                decimal netSalary = Convert.ToDecimal(record.NetSalary);
                                decimal housing = Convert.ToDecimal(record.HousingAllowance);
                                decimal function = Convert.ToDecimal(record.FunctionAllowance);
                                decimal transport = Convert.ToDecimal(record.TransportAllowance);
                                int familyCharges = Convert.ToInt32(record.FamilyCharges);
                                string category = Convert.ToString(record.Category);
                                bool isSpouseWorking = Convert.ToBoolean(record.IsSpouseWorking);
                                string firstName = Convert.ToString(record.FirstName);
                                string lastName = Convert.ToString(record.LastName);

                                var employee = new Employee
                                {
                                    FirstName = firstName,
                                    LastName = lastName,
                                    FamilyCharges = familyCharges,
                                    Category = category,
                                    IsSpouseWorking = isSpouseWorking,
                                    HousingAllowance = housing,
                                    FunctionAllowance = function,
                                    TransportAllowance = transport
                                };

                                // Correction : récupération du tuple et affectation des valeurs
                                var estimation = _taxCalculatorService.EstimateBaseSalaryWithConstraints(netSalary, employee);
                                employee.BaseSalary = estimation.baseSalary;
                                employee.HousingAllowance = estimation.housing;
                                employee.FunctionAllowance = estimation.function;
                                employee.TransportAllowance = estimation.transport;
                                // estimation.iuts peut être utilisé si besoin

                                employees.Add(employee);
                            }
                            catch (Exception ex)
                            {
                                System.Diagnostics.Debug.WriteLine($"Erreur lors du traitement d'une ligne CSV : {ex.Message}");
                            }
                        }
                    }
                });

                Employees.Clear();
                foreach (var emp in employees)
                    Employees.Add(emp);

                StatusMessage = $"Import et estimation terminés : {employees.Count} employés traités.";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Erreur lors de l'import ou de l'estimation : {ex.Message}";
            }
            finally
            {
                IsBusy = false;
            }
        }

        /// <summary>
        /// Exporte tous les résultats estimés en masse via ExcelExportHelper
        /// </summary>
        private void ExportBatchToExcel()
        {
            try
            {
                IsBusy = true;
                StatusMessage = "Exportation Excel en masse en cours...";

                var helper = new ExcelExportHelper(_taxCalculatorService, new TaxDataService(AppDomain.CurrentDomain.BaseDirectory));
                helper.ExportPayrollToExcel(Employees.ToList()); 

                StatusMessage = "Exportation Excel en masse terminée.";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Erreur lors de l'export en masse : {ex.Message}";
            }
            finally
            {
                IsBusy = false;
            }
        }
    }
}
