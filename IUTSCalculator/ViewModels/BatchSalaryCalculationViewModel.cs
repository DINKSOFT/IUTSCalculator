using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using System.Windows.Input;
using IUTSCalculator.Models;
using IUTSCalculator.Services;
using IUTSCalculator.Helpers;
using System.Linq;
using System.Windows;
using Microsoft.Win32;
using ClosedXML.Excel;

namespace IUTSCalculator.ViewModels
{
    /// <summary>
    /// ViewModel pour le calcul en lot des salaires d'une liste d'employés
    /// </summary>
    public class BatchSalaryCalculationViewModel : BaseViewModel
    {
        private readonly DataService _dataService;
        private readonly TaxCalculatorService _taxCalculatorService;

        public ObservableCollection<Employee> Employees { get; } = new ObservableCollection<Employee>();
        public ObservableCollection<TaxCalculation> Calculations { get; } = new ObservableCollection<TaxCalculation>();

        private string _statusMessage;
        public new string StatusMessage
        {
            get => _statusMessage;
            set => SetProperty(ref _statusMessage, value);
        }

        private bool _isBusy;
        public new bool IsBusy
        {
            get => _isBusy;
            set
            {
                if (SetProperty(ref _isBusy, value))
                {
                    (CalculateAllCommand as RelayCommand)?.RaiseCanExecuteChanged();
                    (ExportAllCommand as RelayCommand)?.RaiseCanExecuteChanged();
                    (GenerateExcelTableCommand as RelayCommand)?.RaiseCanExecuteChanged();
                }
            }
        }

        private string _periode;
        public string Periode
        {
            get => _periode ?? DateTime.Now.ToString("MMMM yyyy");
            set => SetProperty(ref _periode, value);
        }

        public ICommand LoadEmployeesCommand { get; }
        public ICommand CalculateAllCommand { get; }
        public ICommand ExportAllCommand { get; }
        public ICommand GenerateExcelTableCommand { get; }

        public BatchSalaryCalculationViewModel(DataService dataService, TaxCalculatorService taxCalculatorService)
        {
            _dataService = dataService ?? throw new ArgumentNullException(nameof(dataService));
            _taxCalculatorService = taxCalculatorService ?? throw new ArgumentNullException(nameof(taxCalculatorService));

            // Le titre alimente le message de la barre d'état et signale l'écran
            // actif dans le rail de navigation : sans lui, ni l'un ni l'autre.
            Title = "Calcul de salaires en lot";

            LoadEmployeesCommand = new RelayCommand(async () => await LoadEmployeesAsync(), () => !IsBusy);
            CalculateAllCommand = new RelayCommand(async () => await CalculateAllAsync(), () => !IsBusy && Employees.Any());
            ExportAllCommand = new RelayCommand(async () => await ExportAllAsync(), () => !IsBusy && Calculations.Any());
            GenerateExcelTableCommand = new RelayCommand(async () => await GenerateExcelSalaryTableAsync(), () => !IsBusy && Calculations.Any());
        }

        private async Task LoadEmployeesAsync()
        {
            try
            {
                IsBusy = true;
                StatusMessage = "Chargement des employés...";
                Employees.Clear();
                var list = await _dataService.LoadEmployeesAsync();
                foreach (var emp in list)
                    Employees.Add(emp);
                StatusMessage = $"{Employees.Count} employés chargés.";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Erreur lors du chargement : {ex.Message}";
            }
            finally
            {
                IsBusy = false;
            }
        }

        private async Task CalculateAllAsync()
        {
            try
            {
                IsBusy = true;
                StatusMessage = "Calcul en cours...";
                Calculations.Clear();

                // Correction : calcul du salaire pour chaque employé avec la méthode existante
                var results = await Task.Run(() =>
                    Employees.Select(emp => _taxCalculatorService.CalculateMonthlyTax(emp)).ToList()
                );
                foreach (var calc in results)
                    Calculations.Add(calc);

                StatusMessage = $"Calcul terminé pour {Calculations.Count} employés.";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Erreur lors du calcul : {ex.Message}";
            }
            finally
            {
                IsBusy = false;
            }
        }

        private async Task ExportAllAsync()
        {
            try
            {
                IsBusy = true;
                StatusMessage = "Exportation en cours...";

                // Export Excel ou PDF selon vos besoins
                // Ici, exemple d'export Excel pour tous les calculs (à adapter selon votre DataService)
                foreach (var calc in Calculations)
                {
                    await _dataService.ExportToExcelAsync(calc);
                }

                StatusMessage = "Exportation terminée.";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Erreur lors de l'export : {ex.Message}";
            }
            finally
            {
                IsBusy = false;
            }
        }

        private async Task GenerateExcelSalaryTableAsync()
        {
            try
            {
                IsBusy = true;
                StatusMessage = "Génération du tableau Excel en cours...";

                await Task.Run(() =>
                {
                    var saveFileDialog = new SaveFileDialog
                    {
                        Filter = "Excel files (*.xlsx)|*.xlsx",
                        DefaultExt = "xlsx",
                        FileName = $"Tableau_Salaires_{Periode.Replace(" ", "_")}.xlsx"
                    };

                    if (saveFileDialog.ShowDialog() == true)
                    {
                        CreateSalaryTable(saveFileDialog.FileName);
                    }
                });

                StatusMessage = "Tableau Excel généré avec succès.";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Erreur lors de la génération du tableau : {ex.Message}";
            }
            finally
            {
                IsBusy = false;
            }
        }

        private void CreateSalaryTable(string filePath)
        {
            if (!Employees.Any() || !Calculations.Any())
                throw new ArgumentException("Aucune donnée à exporter.");

            using (var workbook = new XLWorkbook())
            {
                var worksheet = workbook.Worksheets.Add("Tableau des Salaires");

                // Configuration générale  
                var titleRange = worksheet.Range("A1:R1");
                titleRange.Merge().Value = "TABLEAU DES SALAIRES DES AGENTS DE SIXIEME SENS BURKINA FASO";
                titleRange.Style.Font.Bold = true;
                titleRange.Style.Font.FontSize = 12;
                titleRange.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                titleRange.Style.Fill.BackgroundColor = XLColor.LightGray;
                titleRange.Style.Border.OutsideBorder = XLBorderStyleValues.Thick;

                // Période  
                var periodeRange = worksheet.Range("S1:V1");
                periodeRange.Merge().Value = $"PERIODE : {Periode}";
                periodeRange.Style.Font.Bold = true;
                periodeRange.Style.Font.FontSize = 12;
                periodeRange.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                periodeRange.Style.Fill.BackgroundColor = XLColor.LightGray;
                periodeRange.Style.Border.OutsideBorder = XLBorderStyleValues.Thick;

                // En-têtes principales (ligne 2)  
                var headers = new[]
                {
                    "N°", "Nom et Prénom(s)", "Fonction", "Date d'entrée", "Situat° matrimonial", "Nbre Chges", "SalBase",
                    "Indem Logement", "Indem Transport", "Idem Fonction", "Idem Sujetion", "Total Indemn.",
                    "Remuner. Brute", "CNSS 5,5%", "CNSS 16%", "Salaire Imposable", "IUTS mensuel", "Soutient patriotique",
                    "Total retenue" , "TPA", "Salaire Net à payer", "Chèque Virement"
                };

                for (int i = 0; i < headers.Length; i++)
                {
                    var cell = worksheet.Cell(2, i + 1);
                    cell.Value = headers[i];
                    cell.Style.Font.Bold = true;
                    cell.Style.Font.FontSize = 10;
                    cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                    cell.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
                    cell.Style.Fill.BackgroundColor = XLColor.LightBlue;
                    cell.Style.Border.OutsideBorder = XLBorderStyleValues.Medium;
                    cell.Style.Alignment.WrapText = true;
                }

                // Données des agents  
                for (int i = 0; i < Employees.Count; i++)
                {
                    var employee = Employees[i];
                    var calculation = Calculations.FirstOrDefault(c => c.Employee?.EmployeeId == employee.EmployeeId) ?? new TaxCalculation();
                    var row = i + 3;

                    worksheet.Cell(row, 1).Value = i + 1;
                    worksheet.Cell(row, 2).Value = $"{employee.LastName} {employee.FirstName}";
                    worksheet.Cell(row, 3).Value = employee.Fonction;
                    worksheet.Cell(row, 4).Value = employee.DateEntree.ToString("dd/MM/yyyy");
                    worksheet.Cell(row, 5).Value = employee.SituationMatrimoniale;
                    worksheet.Cell(row, 6).Value = employee.FamilyCharges;
                    worksheet.Cell(row, 7).Value = employee.BaseSalary;
                    worksheet.Cell(row, 8).Value = employee.HousingAllowance;
                    worksheet.Cell(row, 9).Value = employee.TransportAllowance;
                    worksheet.Cell(row, 10).Value = employee.FunctionAllowance;
                    worksheet.Cell(row, 11).Value = employee.SujetionAllowance;
                    worksheet.Cell(row, 12).Value = employee.TotalIndemnites;
                    worksheet.Cell(row, 13).Value = employee.BaseSalary + employee.TotalIndemnites;
                    worksheet.Cell(row, 14).Value = calculation.CnssEmployee;
                    worksheet.Cell(row, 15).Value = calculation.CnssPartPatronale;
                    worksheet.Cell(row, 16).Value = calculation.BaseIUTSHorsDeduction;
                    worksheet.Cell(row, 17).Value = calculation.IUTSNet;
                    worksheet.Cell(row, 18).Value = calculation.RetenueSpeciale;
                    worksheet.Cell(row, 19).Value = calculation.TotalRetenue;
                    worksheet.Cell(row, 20).Value = calculation.TaxePatronale;
                    worksheet.Cell(row, 21).Value = calculation.SalaireNetaPayer;
                    worksheet.Cell(row, 22).Value = calculation.SalaireNetaPayer;

                    // Style des lignes de données  
                    var dataRange = worksheet.Range(row, 1, row, 22);
                    dataRange.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
                    dataRange.Style.Font.FontSize = 9;

                    // Alignement des colonnes numériques à droite  
                    for (int col = 6; col <= 22; col++)
                    {
                        if (col != 4) // Sauf la colonne date  
                        {
                            worksheet.Cell(row, col).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;
                            worksheet.Cell(row, col).Style.NumberFormat.Format = "#,##0";
                        }
                    }
                }

                // Ligne TOTAUX  
                var totalRow = Employees.Count + 3;
                var totalLabelRange = worksheet.Range(totalRow, 1, totalRow, 5);
                totalLabelRange.Merge().Value = "TOTAUX";
                totalLabelRange.Style.Font.Bold = true;
                totalLabelRange.Style.Fill.BackgroundColor = XLColor.Yellow;
                totalLabelRange.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                totalLabelRange.Style.Border.OutsideBorder = XLBorderStyleValues.Medium;

                // Calcul des totaux  
                var totalColumns = new int[] { 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18, 19, 20, 21, 22 };
                foreach (var col in totalColumns)
                {
                    var sum = CalculateColumnTotal(col);
                    worksheet.Cell(totalRow, col).Value = sum;
                    worksheet.Cell(totalRow, col).Style.Font.Bold = true;
                    worksheet.Cell(totalRow, col).Style.Fill.BackgroundColor = XLColor.Yellow;
                    worksheet.Cell(totalRow, col).Style.NumberFormat.Format = "#,##0";
                    worksheet.Cell(totalRow, col).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;
                    worksheet.Cell(totalRow, col).Style.Border.OutsideBorder = XLBorderStyleValues.Medium;
                }

                // Direction (en bas à droite)  
                var directionRange = worksheet.Range("S" + (totalRow + 2), "V" + (totalRow + 2));
                directionRange.Merge().Value = "Direction";
                directionRange.Style.Font.Bold = true;
                directionRange.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

                // Ajustement automatique des colonnes  
                worksheet.Columns().AdjustToContents();

                // Largeurs spécifiques pour certaines colonnes  
                worksheet.Column(2).Width = 25; // Nom et prénom  
                worksheet.Column(3).Width = 20; // Fonction  
                worksheet.Column(4).Width = 12; // Date  

                // Sauvegarde  
                workbook.SaveAs(filePath);
            }
        }

        private decimal CalculateColumnTotal(int column)
        {
            decimal total = 0;
            for (int i = 0; i < Employees.Count; i++)
            {
                var employee = Employees[i];
                var calculation = Calculations.FirstOrDefault(c => c.Employee?.EmployeeId == employee.EmployeeId) ?? new TaxCalculation();
                total += GetColumnValue(employee, calculation, column);
            }
            return total;
        }

        private static decimal GetColumnValue(Employee employee, TaxCalculation calculation, int column)
        {
            switch (column)
            {
                case 6: return employee.FamilyCharges;
                case 7: return employee.BaseSalary;
                case 8: return employee.HousingAllowance;
                case 9: return employee.TransportAllowance;
                case 10: return employee.FunctionAllowance;
                case 11: return employee.SujetionAllowance;
                case 12: return employee.TotalIndemnites;
                case 13: return employee.BaseSalary + employee.TotalIndemnites;
                case 14: return calculation.CnssEmployee;
                case 15: return calculation.CnssPartPatronale;
                case 16: return calculation.BaseIUTSHorsDeduction;
                case 17: return calculation.IUTSNet;
                case 18: return calculation.RetenueSpeciale;
                case 19: return calculation.TotalRetenue;
                case 20: return calculation.TaxePatronale;
                case 21: return calculation.SalaireNetaPayer;
                case 22: return calculation.SalaireNetaPayer;
                default: return 0;
            }
        }
    }
}
