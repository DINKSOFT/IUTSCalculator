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

                    var liste = lignes.OrderBy(x => x.NomComplet).ToList();
                    var reference = liste.FirstOrDefault() ?? new HeureSupplementaire();

                    // Colonnes : identité, puis une paire (heures, montant) par tranche du barème
                    // appliqué au mois (cinq tranches depuis RH-08), puis les totaux.
                    var tranches = new List<(string Titre, Func<HeureSupplementaire, decimal> Heures, Func<HeureSupplementaire, decimal> Montant)>
                    {
                        ("41e-48e h " + BaremeHeuresSup.Pourcent(reference.TauxTranche1), l => l.Heures15, l => l.Montant15),
                        ("> 48e h " + BaremeHeuresSup.Pourcent(reference.TauxTranche2), l => l.Heures30, l => l.Montant30),
                        ("Nuit " + BaremeHeuresSup.Pourcent(reference.TauxNuit), l => l.Heures50, l => l.Montant50),
                        ("Dim./férié jour " + BaremeHeuresSup.Pourcent(reference.TauxFerieJour), l => l.Heures60, l => l.Montant60),
                        ("Dim./férié nuit " + BaremeHeuresSup.Pourcent(reference.TauxFerieNuit), l => l.Heures100, l => l.Montant100)
                    };

                    var headers = new List<string> { "N°", "Matricule", "Nom & Prénom", "Salaire Base (FCFA)", "Taux Hor. (FCFA)" };
                    foreach (var t in tranches)
                    {
                        headers.Add("HS " + t.Titre + " (h)");
                        headers.Add("Mt " + t.Titre + " (FCFA)");
                    }
                    headers.Add("Total Heures");
                    headers.Add("Total Brut (FCFA)");
                    int nbCol = headers.Count;

                    // 2. Titre officiel
                    ws.Range(row, 1, row, nbCol).Merge();
                    ws.Cell(row, 1).Value = (XLCellValue)string.Format("ÉTAT RÉCAPITULATIF DES HEURES SUPPLÉMENTAIRES — PÉRIODE DE {0:D2}/{1}", mois, annee);
                    ws.Cell(row, 1).Style.Font.Bold = true;
                    ws.Cell(row, 1).Style.Font.FontSize = 13;
                    ws.Cell(row, 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                    ws.Cell(row, 1).Style.Fill.BackgroundColor = CouleurTotaux;
                    row += 2;

                    // 3. Colonnes d'en-tête
                    for (int i = 0; i < headers.Count; i++)
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
                    ws.Row(row).Height = 42;
                    row++;

                    // 4. Données
                    int num = 1;
                    int startDataRow = row;

                    foreach (var l in liste)
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

                        int col = 6;
                        foreach (var t in tranches)
                        {
                            ws.Cell(row, col).Value = (XLCellValue)Convert.ToDouble(t.Heures(l));
                            ws.Cell(row, col).Style.NumberFormat.Format = "0.00";
                            ws.Cell(row, col + 1).Value = (XLCellValue)Convert.ToDouble(t.Montant(l));
                            ws.Cell(row, col + 1).Style.NumberFormat.Format = "#,##0";
                            col += 2;
                        }

                        ws.Cell(row, col).Value = (XLCellValue)Convert.ToDouble(l.TotalHeures);
                        ws.Cell(row, col).Style.NumberFormat.Format = "0.00";
                        ws.Cell(row, col).Style.Font.Bold = true;

                        ws.Cell(row, col + 1).Value = (XLCellValue)Convert.ToDouble(l.MontantTotal);
                        ws.Cell(row, col + 1).Style.NumberFormat.Format = "#,##0";
                        ws.Cell(row, col + 1).Style.Font.Bold = true;

                        for (int c = 1; c <= nbCol; c++)
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

                        for (int c = 4; c <= nbCol; c++)
                        {
                            if (c == 5)
                            {
                                ws.Cell(row, c).Value = (XLCellValue)string.Empty;
                                continue;
                            }

                            string lettre = ws.Column(c).ColumnLetter();
                            ws.Cell(row, c).FormulaA1 = string.Format("SUM({0}{1}:{0}{2})", lettre, startDataRow, endDataRow);
                            bool estHeures = c >= 6 && c < nbCol && ((c - 6) % 2 == 0);
                            ws.Cell(row, c).Style.NumberFormat.Format = estHeures ? "0.00" : "#,##0";
                        }

                        for (int c = 1; c <= nbCol; c++)
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

                        ws.Cell(row, nbCol - 1).Value = (XLCellValue)"La Direction Générale";
                        ws.Cell(row, nbCol - 1).Style.Font.Bold = true;
                        ws.Cell(row, nbCol - 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;
                    }

                    ws.Columns().AdjustToContents();
                    workbook.SaveAs(cheminFichier);
                }
            });
        }
    }
}
