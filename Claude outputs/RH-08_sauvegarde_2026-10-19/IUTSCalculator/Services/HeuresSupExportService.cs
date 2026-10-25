using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ClosedXML.Excel;
using IUTSCalculator.Models;

namespace IUTSCalculator.Services
{
    /// <summary>
    /// Service d'exportation Excel officiel pour le récapitulatif des heures supplémentaires (module PAI-03).
    /// </summary>
    public class HeuresSupExportService
    {
        private static readonly XLColor CouleurEnTete = XLColor.FromHtml("#14675A");
        private static readonly XLColor CouleurBordure = XLColor.FromHtml("#CBD1CC");
        private static readonly XLColor CouleurAlternance = XLColor.FromHtml("#FAFBFA");
        private static readonly XLColor CouleurTotaux = XLColor.FromHtml("#E6F0ED");

        public async Task ExporterExcelAsync(
            IEnumerable<HeureSupplementaire> lignes,
            Dossier dossier,
            int annee,
            int mois,
            string cheminFichier)
        {
            if (lignes == null) throw new ArgumentNullException(nameof(lignes));
            if (string.IsNullOrWhiteSpace(cheminFichier)) throw new ArgumentNullException(nameof(cheminFichier));

            await Task.Run(() =>
            {
                using (var workbook = new XLWorkbook())
                {
                    var ws = workbook.Worksheets.Add("Heures supplémentaires");
                    ws.PageSetup.PageOrientation = XLPageOrientation.Landscape;
                    ws.PageSetup.PaperSize = XLPaperSize.A4Paper;

                    int row = 1;

                    // 1. En-tête employeur
                    ws.Cell(row, 1).Value = (XLCellValue)(dossier?.RaisonSociale ?? "ENTREPRISE");
                    ws.Cell(row, 1).Style.Font.Bold = true;
                    ws.Cell(row, 1).Style.Font.FontSize = 14;
                    row++;

                    string ifuRccm = string.Format("IFU : {0}  |  RCCM : {1}", dossier?.IFU ?? "N/A", dossier?.RCCM ?? "N/A");
                    ws.Cell(row, 1).Value = (XLCellValue)ifuRccm;
                    ws.Cell(row, 1).Style.Font.FontSize = 9;
                    ws.Cell(row, 1).Style.Font.FontColor = XLColor.FromHtml("#48534F");
                    row += 2;

                    // 2. Titre officiel
                    ws.Range(row, 1, row, 15).Merge();
                    ws.Cell(row, 1).Value = (XLCellValue)string.Format("ÉTAT RÉCAPITULATIF DES HEURES SUPPLÉMENTAIRES — PÉRIODE DE {0:D2}/{1}", mois, annee);
                    ws.Cell(row, 1).Style.Font.Bold = true;
                    ws.Cell(row, 1).Style.Font.FontSize = 13;
                    ws.Cell(row, 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                    ws.Cell(row, 1).Style.Fill.BackgroundColor = CouleurTotaux;
                    row += 2;

                    // 3. Colonnes d'en-tête
                    string[] headers = new[]
                    {
                        "N°", "Matricule", "Nom & Prénom", "Salaire Base (FCFA)", "Taux Hor. (FCFA)",
                        "HS 15% (h)", "Mt 15% (FCFA)", "HS 30% (h)", "Mt 30% (FCFA)",
                        "HS 50% (h)", "Mt 50% (FCFA)", "HS 100% (h)", "Mt 100% (FCFA)",
                        "Total Heures", "Total Brut (FCFA)"
                    };

                    for (int i = 0; i < headers.Length; i++)
                    {
                        var cell = ws.Cell(row, i + 1);
                        cell.Value = (XLCellValue)headers[i];
                        cell.Style.Font.Bold = true;
                        cell.Style.Font.FontColor = XLColor.White;
                        cell.Style.Fill.BackgroundColor = CouleurEnTete;
                        cell.Style.Alignment.Horizontal = (i == 1 || i == 2) ? XLAlignmentHorizontalValues.Left : XLAlignmentHorizontalValues.Center;
                        cell.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
                        cell.Style.Alignment.WrapText = true;
                        cell.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
                        cell.Style.Border.OutsideBorderColor = CouleurBordure;
                    }
                    ws.Row(row).Height = 28;
                    row++;

                    // 4. Données
                    int num = 1;
                    int startDataRow = row;

                    foreach (var l in lignes.OrderBy(x => x.NomComplet))
                    {
                        ws.Cell(row, 1).Value = (XLCellValue)num;
                        ws.Cell(row, 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

                        ws.Cell(row, 2).Value = (XLCellValue)(l.Matricule ?? l.EmployeeId);
                        ws.Cell(row, 2).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

                        ws.Cell(row, 3).Value = (XLCellValue)(l.NomComplet ?? string.Empty);
                        ws.Cell(row, 3).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Left;

                        ws.Cell(row, 4).Value = (XLCellValue)Convert.ToDouble(l.SalaireDeBase);
                        ws.Cell(row, 4).Style.NumberFormat.Format = "#,##0";

                        ws.Cell(row, 5).Value = (XLCellValue)Convert.ToDouble(l.TauxHoraireBase);
                        ws.Cell(row, 5).Style.NumberFormat.Format = "#,##0.00";

                        ws.Cell(row, 6).Value = (XLCellValue)Convert.ToDouble(l.Heures15);
                        ws.Cell(row, 6).Style.NumberFormat.Format = "0.00";

                        ws.Cell(row, 7).Value = (XLCellValue)Convert.ToDouble(l.Montant15);
                        ws.Cell(row, 7).Style.NumberFormat.Format = "#,##0";

                        ws.Cell(row, 8).Value = (XLCellValue)Convert.ToDouble(l.Heures30);
                        ws.Cell(row, 8).Style.NumberFormat.Format = "0.00";

                        ws.Cell(row, 9).Value = (XLCellValue)Convert.ToDouble(l.Montant30);
                        ws.Cell(row, 9).Style.NumberFormat.Format = "#,##0";

                        ws.Cell(row, 10).Value = (XLCellValue)Convert.ToDouble(l.Heures50);
                        ws.Cell(row, 10).Style.NumberFormat.Format = "0.00";

                        ws.Cell(row, 11).Value = (XLCellValue)Convert.ToDouble(l.Montant50);
                        ws.Cell(row, 11).Style.NumberFormat.Format = "#,##0";

                        ws.Cell(row, 12).Value = (XLCellValue)Convert.ToDouble(l.Heures100);
                        ws.Cell(row, 12).Style.NumberFormat.Format = "0.00";

                        ws.Cell(row, 13).Value = (XLCellValue)Convert.ToDouble(l.Montant100);
                        ws.Cell(row, 13).Style.NumberFormat.Format = "#,##0";

                        ws.Cell(row, 14).Value = (XLCellValue)Convert.ToDouble(l.TotalHeures);
                        ws.Cell(row, 14).Style.NumberFormat.Format = "0.00";
                        ws.Cell(row, 14).Style.Font.Bold = true;

                        ws.Cell(row, 15).Value = (XLCellValue)Convert.ToDouble(l.MontantTotal);
                        ws.Cell(row, 15).Style.NumberFormat.Format = "#,##0";
                        ws.Cell(row, 15).Style.Font.Bold = true;

                        for (int c = 1; c <= 15; c++)
                        {
                            var cell = ws.Cell(row, c);
                            cell.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
                            cell.Style.Border.OutsideBorderColor = CouleurBordure;
                            if (num % 2 == 0)
                                cell.Style.Fill.BackgroundColor = CouleurAlternance;
                        }

                        row++;
                        num++;
                    }

                    int endDataRow = row - 1;

                    // 5. Ligne des totaux généraux
                    if (endDataRow >= startDataRow)
                    {
                        ws.Range(row, 1, row, 3).Merge();
                        ws.Cell(row, 1).Value = (XLCellValue)"TOTAUX GÉNÉRAUX";
                        ws.Cell(row, 1).Style.Font.Bold = true;
                        ws.Cell(row, 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

                        ws.Cell(row, 4).FormulaA1 = string.Format("SUM(D{0}:D{1})", startDataRow, endDataRow);
                        ws.Cell(row, 4).Style.NumberFormat.Format = "#,##0";

                        ws.Cell(row, 5).Value = (XLCellValue)string.Empty;

                        ws.Cell(row, 6).FormulaA1 = string.Format("SUM(F{0}:F{1})", startDataRow, endDataRow);
                        ws.Cell(row, 6).Style.NumberFormat.Format = "0.00";

                        ws.Cell(row, 7).FormulaA1 = string.Format("SUM(G{0}:G{1})", startDataRow, endDataRow);
                        ws.Cell(row, 7).Style.NumberFormat.Format = "#,##0";

                        ws.Cell(row, 8).FormulaA1 = string.Format("SUM(H{0}:H{1})", startDataRow, endDataRow);
                        ws.Cell(row, 8).Style.NumberFormat.Format = "0.00";

                        ws.Cell(row, 9).FormulaA1 = string.Format("SUM(I{0}:I{1})", startDataRow, endDataRow);
                        ws.Cell(row, 9).Style.NumberFormat.Format = "#,##0";

                        ws.Cell(row, 10).FormulaA1 = string.Format("SUM(J{0}:J{1})", startDataRow, endDataRow);
                        ws.Cell(row, 10).Style.NumberFormat.Format = "0.00";

                        ws.Cell(row, 11).FormulaA1 = string.Format("SUM(K{0}:K{1})", startDataRow, endDataRow);
                        ws.Cell(row, 11).Style.NumberFormat.Format = "#,##0";

                        ws.Cell(row, 12).FormulaA1 = string.Format("SUM(L{0}:L{1})", startDataRow, endDataRow);
                        ws.Cell(row, 12).Style.NumberFormat.Format = "0.00";

                        ws.Cell(row, 13).FormulaA1 = string.Format("SUM(M{0}:M{1})", startDataRow, endDataRow);
                        ws.Cell(row, 13).Style.NumberFormat.Format = "#,##0";

                        ws.Cell(row, 14).FormulaA1 = string.Format("SUM(N{0}:N{1})", startDataRow, endDataRow);
                        ws.Cell(row, 14).Style.NumberFormat.Format = "0.00";

                        ws.Cell(row, 15).FormulaA1 = string.Format("SUM(O{0}:O{1})", startDataRow, endDataRow);
                        ws.Cell(row, 15).Style.NumberFormat.Format = "#,##0";

                        for (int c = 1; c <= 15; c++)
                        {
                            var cell = ws.Cell(row, c);
                            cell.Style.Font.Bold = true;
                            cell.Style.Fill.BackgroundColor = CouleurTotaux;
                            cell.Style.Border.TopBorder = XLBorderStyleValues.Medium;
                            cell.Style.Border.BottomBorder = XLBorderStyleValues.Double;
                            cell.Style.Border.OutsideBorderColor = CouleurEnTete;
                        }

                        row += 3;

                        // 6. Signatures
                        ws.Cell(row, 2).Value = (XLCellValue)"Le Gestionnaire de Paie";
                        ws.Cell(row, 2).Style.Font.Bold = true;

                        ws.Cell(row, 14).Value = (XLCellValue)"La Direction Générale";
                        ws.Cell(row, 14).Style.Font.Bold = true;
                        ws.Cell(row, 14).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;
                    }

                    ws.Columns().AdjustToContents();
                    workbook.SaveAs(cheminFichier);
                }
            });
        }
    }
}
