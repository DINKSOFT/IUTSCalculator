using ClosedXML.Excel;
using System.IO;
using System.Globalization;
using IUTSCalculator.Models;
using System;

public static class ExcelExportHelper
{
    /// <summary>
    /// Exporte les détails du calcul d'impôt au format Excel (XLSX).
    /// </summary>
    /// <param name="calculation">Le calcul d'impôt à exporter</param>
    /// <param name="filePath">Chemin du fichier Excel à générer</param>

    private static readonly object fileLock = new object();

    public static void ExportCalculationDetailsToExcel(TaxCalculation calculation, string filePath)
    {
        if (calculation == null || string.IsNullOrWhiteSpace(calculation.CalculationDetails))
            throw new ArgumentException("Aucun détail de calcul à exporter.");

        var lines = calculation.CalculationDetails
            .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

        lock (fileLock)
        {
            using (var workbook = new XLWorkbook())
            {
                var ws = workbook.Worksheets.Add("Détails du calcul");

                // En-têtes
                ws.Cell(1, 1).Value = "N°";
                ws.Cell(1, 2).Value = "Désignation";
                ws.Cell(1, 3).Value = "Montant";
                ws.Range(1, 1, 1, 3).Style.Font.Bold = true;

                int row = 2;
                int index = 1;
                foreach (var line in lines)
                {
                    var parts = line.Split(new[] { ':' }, 2);
                    string designation = parts[0].Trim();
                    string montantStr = parts.Length > 1 ? parts[1].Trim() : "";

                    decimal montant;
                    bool isMontant = decimal.TryParse(
                        montantStr.Replace(" ", "").Replace(",", "."),
                        NumberStyles.Any,
                        CultureInfo.InvariantCulture,
                        out montant);

                    ws.Cell(row, 1).Value = index;
                    ws.Cell(row, 2).Value = designation;
                    if (isMontant)
                    {
                        ws.Cell(row, 3).Value = montant;
                        ws.Cell(row, 3).Style.NumberFormat.Format = "#,##0.00";
                    }
                    else
                    {
                        ws.Cell(row, 3).Value = montantStr;
                    }

                    row++;
                    index++;
                }

                ws.Columns().AdjustToContents();
                workbook.SaveAs(filePath);
            }
        }
    }
}