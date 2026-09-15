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
    /// Service d'exportation Excel pour le registre des avantages en nature (module PAI-08).
    /// </summary>
    public class AvantageNatureExportService
    {
        private static readonly XLColor CouleurEnTete = XLColor.FromHtml("#14675A");
        private static readonly XLColor CouleurBordure = XLColor.FromHtml("#CBD1CC");
        private static readonly XLColor CouleurAlternance = XLColor.FromHtml("#FAFBFA");
        private static readonly XLColor CouleurTotaux = XLColor.FromHtml("#E6F0ED");

        /// <summary>
        /// Exporte le registre des avantages en nature accordés aux employés dans un classeur Excel ClosedXML.
        /// </summary>
        public async Task ExporterRegistreAvantagesExcelAsync(
            IEnumerable<AvantageNature> avantages,
            Dossier dossier,
            string cheminFichier)
        {
            if (avantages == null) throw new ArgumentNullException(nameof(avantages));
            if (string.IsNullOrWhiteSpace(cheminFichier)) throw new ArgumentNullException(nameof(cheminFichier));

            await Task.Run(() =>
            {
                var dir = Path.GetDirectoryName(cheminFichier);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                using (var workbook = new XLWorkbook())
                {
                    var ws = workbook.Worksheets.Add("Avantages en nature");
                    ws.PageSetup.PageOrientation = XLPageOrientation.Landscape;
                    ws.PageSetup.PaperSize = XLPaperSize.A4Paper;

                    int row = 1;

                    // 1. En-tête entreprise
                    ws.Cell(row, 1).Value = (XLCellValue)(dossier?.RaisonSociale ?? "ENTREPRISE");
                    ws.Cell(row, 1).Style.Font.Bold = true;
                    ws.Cell(row, 1).Style.Font.FontSize = 14;
                    row++;

                    string ifuRccm = string.Format("IFU : {0}  |  RCCM : {1}", dossier?.IFU ?? "N/A", dossier?.RCCM ?? "N/A");
                    ws.Cell(row, 1).Value = (XLCellValue)ifuRccm;
                    ws.Cell(row, 1).Style.Font.FontSize = 9;
                    ws.Cell(row, 1).Style.Font.FontColor = XLColor.FromHtml("#48534F");
                    row += 2;

                    // 2. Titre du document
                    ws.Range(row, 1, row, 11).Merge();
                    ws.Cell(row, 1).Value = (XLCellValue)"REGISTRE DES AVANTAGES EN NATURE ACCORDÉS AU PERSONNEL";
                    ws.Cell(row, 1).Style.Font.Bold = true;
                    ws.Cell(row, 1).Style.Font.FontSize = 12;
                    ws.Cell(row, 1).Style.Font.FontColor = CouleurEnTete;
                    ws.Cell(row, 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                    row++;

                    ws.Range(row, 1, row, 11).Merge();
                    ws.Cell(row, 1).Value = (XLCellValue)string.Format("Édité le {0:dd/MM/yyyy à HH:mm} — Conforme CGI Art. 106 & CNSS Burkina Faso", DateTime.Now);
                    ws.Cell(row, 1).Style.Font.FontSize = 9;
                    ws.Cell(row, 1).Style.Font.Italic = true;
                    ws.Cell(row, 1).Style.Font.FontColor = XLColor.FromHtml("#79837E");
                    ws.Cell(row, 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                    row += 2;

                    // 3. Tableau des avantages
                    int headerRow = row;
                    string[] headers =
                    {
                        "Matricule",
                        "Nom & Prénom",
                        "Type d'avantage",
                        "Libellé",
                        "Mode d'évaluation",
                        "Montant Brut (FCFA)",
                        "Part Salariale (FCFA)",
                        "Valeur Nette Imposable (FCFA)",
                        "Période",
                        "Statut",
                        "Rubrique"
                    };

                    for (int i = 0; i < headers.Length; i++)
                    {
                        var cell = ws.Cell(headerRow, i + 1);
                        cell.Value = (XLCellValue)headers[i];
                        cell.Style.Font.Bold = true;
                        cell.Style.Font.FontColor = XLColor.White;
                        cell.Style.Fill.BackgroundColor = CouleurEnTete;
                        cell.Style.Alignment.Horizontal = (i >= 5 && i <= 7)
                            ? XLAlignmentHorizontalValues.Right
                            : XLAlignmentHorizontalValues.Center;
                        cell.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
                        cell.Style.Border.OutsideBorderColor = CouleurBordure;
                    }
                    row++;

                    decimal totalBrut = 0m;
                    decimal totalPartSalariale = 0m;
                    decimal totalNetImposable = 0m;
                    int startDataRow = row;

                    foreach (var av in avantages.OrderBy(a => a.NomSalarie).ThenBy(a => a.DateDebut))
                    {
                        bool alt = (row % 2 == 0);
                        var bg = alt ? CouleurAlternance : XLColor.White;

                        ws.Cell(row, 1).Value = (XLCellValue)av.EmployeeId;
                        ws.Cell(row, 2).Value = (XLCellValue)(av.NomSalarie ?? av.EmployeeId);
                        ws.Cell(row, 3).Value = (XLCellValue)av.TypeLibelle;
                        ws.Cell(row, 4).Value = (XLCellValue)av.Libelle;
                        ws.Cell(row, 5).Value = (XLCellValue)av.ModeLibelle;

                        ws.Cell(row, 6).Value = (XLCellValue)av.MontantAvantage;
                        ws.Cell(row, 6).Style.NumberFormat.Format = "#,##0";

                        ws.Cell(row, 7).Value = (XLCellValue)av.PartSalariale;
                        ws.Cell(row, 7).Style.NumberFormat.Format = "#,##0";

                        ws.Cell(row, 8).Value = (XLCellValue)av.ValeurNetteImposable;
                        ws.Cell(row, 8).Style.NumberFormat.Format = "#,##0";
                        ws.Cell(row, 8).Style.Font.Bold = true;

                        string periode = av.DateFin.HasValue
                            ? string.Format("{0:dd/MM/yy} au {1:dd/MM/yy}", av.DateDebut, av.DateFin.Value)
                            : string.Format("Dès le {0:dd/MM/yy} (indéterminé)", av.DateDebut);
                        ws.Cell(row, 9).Value = (XLCellValue)periode;

                        ws.Cell(row, 10).Value = (XLCellValue)av.StatutLibelle;
                        ws.Cell(row, 11).Value = (XLCellValue)string.Format("{0} / {1}", av.CodeRubriqueGain, av.CodeRubriqueRetenue);

                        for (int col = 1; col <= headers.Length; col++)
                        {
                            var c = ws.Cell(row, col);
                            c.Style.Fill.BackgroundColor = bg;
                            c.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
                            c.Style.Border.OutsideBorderColor = CouleurBordure;
                            if (col >= 6 && col <= 8)
                                c.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;
                            else if (col == 1 || col == 9 || col == 10 || col == 11)
                                c.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                        }

                        totalBrut += av.MontantAvantage;
                        totalPartSalariale += av.PartSalariale;
                        totalNetImposable += av.ValeurNetteImposable;
                        row++;
                    }

                    // Ligne de totaux
                    if (row > startDataRow)
                    {
                        ws.Range(row, 1, row, 5).Merge();
                        ws.Cell(row, 1).Value = (XLCellValue)"TOTAUX GÉNÉRAUX";
                        ws.Cell(row, 1).Style.Font.Bold = true;
                        ws.Cell(row, 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;

                        ws.Cell(row, 6).Value = (XLCellValue)totalBrut;
                        ws.Cell(row, 6).Style.NumberFormat.Format = "#,##0";
                        ws.Cell(row, 6).Style.Font.Bold = true;

                        ws.Cell(row, 7).Value = (XLCellValue)totalPartSalariale;
                        ws.Cell(row, 7).Style.NumberFormat.Format = "#,##0";
                        ws.Cell(row, 7).Style.Font.Bold = true;

                        ws.Cell(row, 8).Value = (XLCellValue)totalNetImposable;
                        ws.Cell(row, 8).Style.NumberFormat.Format = "#,##0";
                        ws.Cell(row, 8).Style.Font.Bold = true;

                        for (int col = 1; col <= headers.Length; col++)
                        {
                            var c = ws.Cell(row, col);
                            c.Style.Fill.BackgroundColor = CouleurTotaux;
                            c.Style.Border.TopBorder = XLBorderStyleValues.Medium;
                            c.Style.Border.BottomBorder = XLBorderStyleValues.Double;
                            c.Style.Border.OutsideBorderColor = CouleurBordure;
                        }
                    }

                    ws.Columns().AdjustToContents();
                    workbook.SaveAs(cheminFichier);
                }
            }).ConfigureAwait(false);
        }
    }
}
