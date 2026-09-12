using ClosedXML.Excel;
using System.IO;
using System.Globalization;
using IUTSCalculator.Models;
using System;
using Microsoft.Win32;
using IUTSCalculator.ViewModels;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.Linq;

namespace IUTSCalculator.Services
{
    public class ExcelExportHelper
    {
        private static readonly object fileLock = new object();
        private readonly TaxCalculatorService _taxCalculatorService;
        private readonly TaxDataService _taxDataService;

        public static ObservableCollection<Employee> Employees { get; } = new ObservableCollection<Employee>();        

        /// <summary>
        /// Constructeur par défaut
        /// </summary>
        public ExcelExportHelper()
        {
            // Initialisation des services
            _taxDataService = new TaxDataService(AppDomain.CurrentDomain.BaseDirectory);
            _taxCalculatorService = new TaxCalculatorService(_taxDataService);
        }

        /// <summary>
        /// Constructeur avec services injectés
        /// </summary>
        public ExcelExportHelper(TaxCalculatorService taxCalculatorService, TaxDataService taxDataService)
        {
            _taxCalculatorService = taxCalculatorService ?? throw new ArgumentNullException(nameof(taxCalculatorService));
            _taxDataService = taxDataService ?? throw new ArgumentNullException(nameof(taxDataService));
        }

        
        /// <summary>
        /// Génère un fichier Excel avec les éléments de salaire pour plusieurs employés
        /// </summary>
        /// <param name="employees">Liste des employés</param>
        /// <param name="filePath">Chemin de sortie du fichier Excel</param>
        public void ExportPayrollToExcel(List<Employee> employees)
        {
            if (employees == null || !employees.Any())
                throw new ArgumentException("Aucun employé à exporter.");

            lock (fileLock)
            {
                using (var workbook = new XLWorkbook())
                {
                    var worksheet = workbook.Worksheets.Add("ELEMENTS DE SALAIRES");

                    // Configuration des en-têtes
                    SetupPayrollHeaders(worksheet);

                    // Remplissage des données
                    FillEmployeePayrollData(worksheet, employees);

                    // Calcul des totaux
                    CalculatePayrollTotals(worksheet, employees.Count);

                    // Formatage du tableau
                    FormatPayrollWorksheet(worksheet, employees.Count);

                    // Numéro de contrôle unique du document (protection contre les faux
                    // documents : permet de vérifier l'authenticité de cet export auprès de
                    // l'employeur).
                    int controleRow = employees.Count + 5;
                    string numeroControle = QrCodeService.GenererNumeroControle(
                        $"PAYROLL|{string.Join(",", employees.Select(e => e.EmployeeId))}|{DateTime.Now:yyyyMMddHHmmss}");
                    worksheet.Cell(controleRow, 1).Value = $"N° de contrôle : {numeroControle}";
                    worksheet.Cell(controleRow, 1).Style.Font.FontSize = 8;
                    worksheet.Cell(controleRow, 1).Style.Font.Italic = true;

                    // Sauvegarde
                    workbook.SaveAs(SaveFileDialog());
                }
            }
        }

        /// <summary>
        /// Exporte les éléments de salaire des employés depuis les collections statiques
        /// </summary>
        /// <param name="filePath">Chemin de sortie du fichier Excel</param>
        public void ExportPayrollFromCollections()
        {
            var employeesList = Employees.ToList();
            if (!employeesList.Any())
                throw new ArgumentException("Aucun employé dans la collection à exporter.");

            ExportPayrollToExcel(employeesList);
        }

        /// <summary>
        /// Configure les en-têtes du tableau de paie
        /// </summary>
        private static void SetupPayrollHeaders(IXLWorksheet worksheet)
        {
            var headers = new[]
            {
                "N° d'ordre",
                "Nombre de Charges",
                "Nom et Prénom",
                "Salaire de base",
                "ind log",
                "ind trps",
                "ind fonct",
                "Sal Brut",
                "CNSS",
                "Base Imposable",
                "IUTS",
                "RETENUE SUR SALAIRE",
                "Salaire net",
                "TPA",
                "CNSS Part Patronale",
                "Indemnité de congé payé",
                "Indemnité de fin de contrat",
                "commission cabinet",
                "Masse salariale"
            };

            for (int i = 0; i < headers.Length; i++)
            {
                worksheet.Cell(1, i + 1).Value = headers[i];
            }
        }

        /// <summary>
        /// Remplit les données des employés dans le tableau de paie
        /// </summary>
        private void FillEmployeePayrollData(IXLWorksheet worksheet, List<Employee> employees)
        {
            int row = 2;

            foreach (var employee in employees)
            {
                var taxCalculation = _taxCalculatorService.CalculateMonthlyTax(employee);

                worksheet.Cell(row, 1).Value = row - 1;
                worksheet.Cell(row, 2).Value = employee.FamilyCharges;
                worksheet.Cell(row, 3).Value = $"{employee.LastName} {employee.FirstName}";
                worksheet.Cell(row, 4).Value = employee.BaseSalary;
                worksheet.Cell(row, 5).Value = employee.HousingAllowance;
                worksheet.Cell(row, 6).Value = employee.TransportAllowance;
                worksheet.Cell(row, 7).Value = employee.FunctionAllowance;
                worksheet.Cell(row, 8).Value = taxCalculation.SalaireBrut;
                worksheet.Cell(row, 9).Value = taxCalculation.CnssEmployee;
                worksheet.Cell(row, 10).Value = taxCalculation.BaseIUTSHorsDeduction;
                worksheet.Cell(row, 11).Value = taxCalculation.IUTSNet;
                worksheet.Cell(row, 12).Value = taxCalculation.RetenueSpeciale;
                worksheet.Cell(row, 13).Value = taxCalculation.SalaireNetaPayer;
                worksheet.Cell(row, 14).Value = taxCalculation.TaxePatronale;
                worksheet.Cell(row, 15).Value = taxCalculation.CnssPartPatronale;
                worksheet.Cell(row, 16).Value = CalculateIndemniteCongesPaye(employee);
                worksheet.Cell(row, 17).Value = CalculateIndemniteFinContrat(employee);
                worksheet.Cell(row, 18).Value = CalculateCommissionCabinet(employee);
                worksheet.Cell(row, 19).Value = taxCalculation.MasseSalariale;

                row++;
            }
        }

        /// <summary>
        /// Calcule les totaux en bas du tableau de paie
        /// </summary>
        private static void CalculatePayrollTotals(IXLWorksheet worksheet, int employeeCount)
        {
            int totalRow = employeeCount + 3;

            worksheet.Cell(totalRow, 3).Value = "Total";
            worksheet.Cell(totalRow, 3).Style.Font.Bold = true;

            for (int col = 4; col <= 19; col++)
            {
                var range = worksheet.Range(2, col, employeeCount + 1, col);
                worksheet.Cell(totalRow, col).FormulaA1 = $"SUM({range.RangeAddress})";
            }
        }

        /// <summary>
        /// Applique le formatage au tableau de paie
        /// </summary>
        private static void FormatPayrollWorksheet(IXLWorksheet worksheet, int employeeCount)
        {
            // Formatage des en-têtes
            var headerRange = worksheet.Range(1, 1, 1, 19);
            headerRange.Style.Font.Bold = true;
            headerRange.Style.Fill.BackgroundColor = XLColor.LightGray;
            headerRange.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            headerRange.Style.Border.OutsideBorder = XLBorderStyleValues.Thick;
            headerRange.Style.Border.InsideBorder = XLBorderStyleValues.Thin;

            // Formatage des données
            var dataRange = worksheet.Range(2, 1, employeeCount + 1, 19);
            dataRange.Style.Border.OutsideBorder = XLBorderStyleValues.Medium;
            dataRange.Style.Border.InsideBorder = XLBorderStyleValues.Thin;

            // Formatage des montants
            var moneyRange = worksheet.Range(2, 4, employeeCount + 3, 19);
            moneyRange.Style.NumberFormat.Format = "#,##0";

            // Formatage de la ligne des totaux
            var totalRange = worksheet.Range(employeeCount + 3, 1, employeeCount + 3, 19);
            totalRange.Style.Font.Bold = true;
            totalRange.Style.Fill.BackgroundColor = XLColor.LightBlue;
            totalRange.Style.Border.OutsideBorder = XLBorderStyleValues.Thick;

            // Ajustement automatique de la largeur des colonnes
            worksheet.Columns().AdjustToContents();
            worksheet.Column(3).Width = 20; // Nom et Prénom
        }

        /// <summary>
        /// Calcule l'indemnité de congés payés (1/12ieme du salaire net)
        /// </summary>
        private decimal CalculateIndemniteCongesPaye(Employee employee)
        {
            var taxCalculation = _taxCalculatorService.CalculateMonthlyTax(employee);
            return taxCalculation.SalaireNetaPayer/12;
        
        }        
        /// <summary>
        /// Calcule l'indemnité de fin de contrat (basé sur l'ancienneté)
        /// </summary>
        private decimal CalculateIndemniteFinContrat(Employee employee)
        {
            var taxCalculation = _taxCalculatorService.CalculateMonthlyTax(employee);
            return taxCalculation.SalaireBrut * 0.25m/12; // 25% du salaire brut 
        }

        /// <summary>
        /// Calcule la commission cabinet selon la catégorie
        /// </summary>
        private decimal CalculateCommissionCabinet(Employee employee)
        {
            var taxCalculation = _taxCalculatorService.CalculateMonthlyTax(employee);
            return taxCalculation.SalaireBrut * 0.25m; // 25% du salaire brut 
        }


        /// <summary>
        /// Exporte un seul employé vers Excel (feuille de calcul individuelle)
        /// </summary>
        /// <param name="employee">L'employé à exporter</param>
        /// <param name="filePath">Chemin de sortie du fichier Excel</param>
        public void ExportSingleEmployeeToExcel(Employee employee)
        {
            if (employee == null)
                throw new ArgumentNullException(nameof(employee));

            var employees = new List<Employee> { employee };
            ExportPayrollToExcel(employees);
        }

        /// <summary>
        /// Méthode utilitaire pour sauvegarder un fichier Excel avec gestion des erreurs
        /// </summary>
        /// <param name="filePath">Chemin du fichier</param>
        /// <returns>Chemin du fichier sauvegardé ou null en cas d'erreur</returns>
        public static string SaveFileDialog(string defaultFileName = "Export_Salaires.xlsx")
        {
            try
            {
                var saveFileDialog = new SaveFileDialog
                {
                    Filter = "Excel files (*.xlsx)|*.xlsx|All files (*.*)|*.*",
                    DefaultExt = "xlsx",
                    FileName = defaultFileName,
                    InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Desktop)
                };

                if (saveFileDialog.ShowDialog() == true)
                {
                    return saveFileDialog.FileName;
                }
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Erreur lors de la sélection du fichier : {ex.Message}", ex);
            }

            return null;
        }
    }
}