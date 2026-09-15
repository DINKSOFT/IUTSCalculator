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
    /// Service d'exportation Excel pour les saisies-arrêts, cessions sur salaire et oppositions (module PAI-06).
    /// Produit les bordereaux de reversement pour les tribunaux, greffes, huissiers et créanciers.
    /// </summary>
    public class SaisieOppositionExportService
    {
        private static readonly XLColor CouleurEnTete = XLColor.FromHtml("#14675A");
        private static readonly XLColor CouleurBordure = XLColor.FromHtml("#CBD1CC");
        private static readonly XLColor CouleurAlternance = XLColor.FromHtml("#FAFBFA");
        private static readonly XLColor CouleurTotaux = XLColor.FromHtml("#E6F0ED");

        /// <summary>
        /// Exporte le bordereau mensuel des retenues et reversements pour le greffe ou les créanciers.
        /// </summary>
        public async Task ExporterRecapitulatifMensuelExcelAsync(
            IEnumerable<RetenueSaisieMensuelle> retenues,
            Dossier dossier,
            int annee,
            int mois,
            string cheminFichier)
        {
            if (retenues == null) throw new ArgumentNullException(nameof(retenues));
            if (string.IsNullOrWhiteSpace(cheminFichier)) throw new ArgumentNullException(nameof(cheminFichier));

            await Task.Run(() =>
            {
                var dir = Path.GetDirectoryName(cheminFichier);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                using (var workbook = new XLWorkbook())
                {
                    var ws = workbook.Worksheets.Add("Retenues et reversements");
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
                    ws.Cell(row, 1).Value = (XLCellValue)string.Format(
                        "BORDEREAU MENSUEL DES SAISIES-ARRÊTS ET CESSIONS SUR SALAIRE — {0:D2}/{1}",
                        mois, annee);
                    ws.Cell(row, 1).Style.Font.Bold = true;
                    ws.Cell(row, 1).Style.Font.FontSize = 13;
                    ws.Cell(row, 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                    ws.Cell(row, 1).Style.Fill.BackgroundColor = CouleurTotaux;
                    row += 2;

                    // 3. Colonnes d'en-tête
                    string[] headers = new[]
                    {
                        "N°", "Matricule", "Salarié", "Créancier", "Type de créance",
                        "Net Réf. (FCFA)", "Quotité Disp. (FCFA)", "Retenue (FCFA)",
                        "Réf. Règlement", "Statut Paiement", "Commentaire / Prorata"
                    };

                    int startHeaderRow = row;
                    for (int c = 0; c < headers.Length; c++)
                    {
                        var cell = ws.Cell(row, c + 1);
                        cell.Value = (XLCellValue)headers[c];
                        cell.Style.Font.Bold = true;
                        cell.Style.Font.FontColor = XLColor.White;
                        cell.Style.Fill.BackgroundColor = CouleurEnTete;
                        cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                        cell.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
                        cell.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
                        cell.Style.Border.OutsideBorderColor = CouleurBordure;
                    }
                    ws.Row(row).Height = 24;
                    row++;

                    // 4. Données
                    int numLigne = 1;
                    int dataStartRow = row;
                    var liste = retenues.ToList();

                    foreach (var r in liste)
                    {
                        ws.Cell(row, 1).Value = (XLCellValue)numLigne++;
                        ws.Cell(row, 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

                        ws.Cell(row, 2).Value = (XLCellValue)r.EmployeeId;
                        ws.Cell(row, 2).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

                        ws.Cell(row, 3).Value = (XLCellValue)(r.NomSalarie ?? "");
                        ws.Cell(row, 4).Value = (XLCellValue)(r.Creancier ?? "");
                        ws.Cell(row, 5).Value = (XLCellValue)r.TypeSaisie.ToString();

                        ws.Cell(row, 6).Value = (XLCellValue)(double)r.BaseNetCalcul;
                        ws.Cell(row, 6).Style.NumberFormat.Format = "#,##0";

                        ws.Cell(row, 7).Value = (XLCellValue)(double)r.QuotiteDisponible;
                        ws.Cell(row, 7).Style.NumberFormat.Format = "#,##0";

                        ws.Cell(row, 8).Value = (XLCellValue)(double)r.MontantRetenu;
                        ws.Cell(row, 8).Style.NumberFormat.Format = "#,##0";
                        ws.Cell(row, 8).Style.Font.Bold = true;

                        ws.Cell(row, 9).Value = (XLCellValue)(r.NumeroChequeOuVirement ?? "");
                        ws.Cell(row, 9).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

                        ws.Cell(row, 10).Value = (XLCellValue)r.StatutPaiement.ToString();
                        ws.Cell(row, 10).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

                        ws.Cell(row, 11).Value = (XLCellValue)(r.Commentaire ?? "");

                        // Style de la ligne
                        var rowRange = ws.Range(row, 1, row, headers.Length);
                        rowRange.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
                        rowRange.Style.Border.OutsideBorderColor = CouleurBordure;
                        rowRange.Style.Border.InsideBorder = XLBorderStyleValues.Thin;
                        rowRange.Style.Border.InsideBorderColor = CouleurBordure;

                        if (numLigne % 2 == 1)
                            rowRange.Style.Fill.BackgroundColor = CouleurAlternance;

                        row++;
                    }

                    // 5. Ligne de totaux
                    int dataEndRow = row - 1;
                    if (liste.Count > 0)
                    {
                        ws.Cell(row, 1).Value = (XLCellValue)"TOTAUX";
                        ws.Range(row, 1, row, 5).Merge();
                        ws.Cell(row, 1).Style.Font.Bold = true;
                        ws.Cell(row, 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;

                        ws.Cell(row, 8).FormulaA1 = string.Format("SUM(H{0}:H{1})", dataStartRow, dataEndRow);
                        ws.Cell(row, 8).Style.Font.Bold = true;
                        ws.Cell(row, 8).Style.NumberFormat.Format = "#,##0";

                        var totalRange = ws.Range(row, 1, row, headers.Length);
                        totalRange.Style.Fill.BackgroundColor = CouleurTotaux;
                        totalRange.Style.Font.Bold = true;
                        totalRange.Style.Border.OutsideBorder = XLBorderStyleValues.Medium;
                        totalRange.Style.Border.OutsideBorderColor = CouleurEnTete;
                        row += 3;
                    }
                    else
                    {
                        ws.Range(row, 1, row, headers.Length).Merge();
                        ws.Cell(row, 1).Value = (XLCellValue)"Aucune retenue de saisie enregistrée sur cette période.";
                        ws.Cell(row, 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                        ws.Cell(row, 1).Style.Font.Italic = true;
                        row += 3;
                    }

                    // 6. Bloc de signatures
                    ws.Cell(row, 2).Value = (XLCellValue)"Pour l'Employeur / Le Responsable Paie";
                    ws.Cell(row, 2).Style.Font.Bold = true;
                    ws.Cell(row, 8).Value = (XLCellValue)"Visa du Greffe / Huissier / Créancier";
                    ws.Cell(row, 8).Style.Font.Bold = true;

                    ws.Columns().AdjustToContents();
                    workbook.SaveAs(cheminFichier);
                }
            });
        }

        /// <summary>
        /// Exporte le registre permanent des dossiers de saisies, cessions et oppositions avec état des soldes.
        /// </summary>
        public async Task ExporterRegistreCreancesExcelAsync(
            IEnumerable<SaisieOpposition> saisies,
            Dossier dossier,
            string cheminFichier)
        {
            if (saisies == null) throw new ArgumentNullException(nameof(saisies));
            if (string.IsNullOrWhiteSpace(cheminFichier)) throw new ArgumentNullException(nameof(cheminFichier));

            await Task.Run(() =>
            {
                var dir = Path.GetDirectoryName(cheminFichier);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                using (var workbook = new XLWorkbook())
                {
                    var ws = workbook.Worksheets.Add("Registre des saisies");
                    ws.PageSetup.PageOrientation = XLPageOrientation.Landscape;
                    ws.PageSetup.PaperSize = XLPaperSize.A4Paper;

                    int row = 1;

                    // En-tête
                    ws.Cell(row, 1).Value = (XLCellValue)(dossier?.RaisonSociale ?? "ENTREPRISE");
                    ws.Cell(row, 1).Style.Font.Bold = true;
                    ws.Cell(row, 1).Style.Font.FontSize = 14;
                    row++;

                    string ifuRccm = string.Format("IFU : {0}  |  RCCM : {1}", dossier?.IFU ?? "N/A", dossier?.RCCM ?? "N/A");
                    ws.Cell(row, 1).Value = (XLCellValue)ifuRccm;
                    ws.Cell(row, 1).Style.Font.FontSize = 9;
                    ws.Cell(row, 1).Style.Font.FontColor = XLColor.FromHtml("#48534F");
                    row += 2;

                    // Titre
                    ws.Range(row, 1, row, 12).Merge();
                    ws.Cell(row, 1).Value = (XLCellValue)"REGISTRE DES SAISIES-ARRÊTS, CESSIONS ET OPPOSITIONS SUR SALAIRE";
                    ws.Cell(row, 1).Style.Font.Bold = true;
                    ws.Cell(row, 1).Style.Font.FontSize = 13;
                    ws.Cell(row, 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                    ws.Cell(row, 1).Style.Fill.BackgroundColor = CouleurTotaux;
                    row += 2;

                    string[] headers = new[]
                    {
                        "N°", "Matricule", "Salarié", "Créancier", "Type", "N° Dossier / Jug.",
                        "Total Dû (FCFA)", "Retenue Mensuelle (FCFA)", "Cumul Amorti (FCFA)",
                        "Solde Restant (FCFA)", "Taux Amorti (%)", "Statut"
                    };

                    for (int c = 0; c < headers.Length; c++)
                    {
                        var cell = ws.Cell(row, c + 1);
                        cell.Value = (XLCellValue)headers[c];
                        cell.Style.Font.Bold = true;
                        cell.Style.Font.FontColor = XLColor.White;
                        cell.Style.Fill.BackgroundColor = CouleurEnTete;
                        cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                        cell.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
                        cell.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
                        cell.Style.Border.OutsideBorderColor = CouleurBordure;
                    }
                    ws.Row(row).Height = 24;
                    row++;

                    int numLigne = 1;
                    int dataStartRow = row;
                    var liste = saisies.ToList();

                    foreach (var s in liste)
                    {
                        ws.Cell(row, 1).Value = (XLCellValue)numLigne++;
                        ws.Cell(row, 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

                        ws.Cell(row, 2).Value = (XLCellValue)s.EmployeeId;
                        ws.Cell(row, 2).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

                        ws.Cell(row, 3).Value = (XLCellValue)(s.NomSalarie ?? "");
                        ws.Cell(row, 4).Value = (XLCellValue)(s.Creancier ?? "");
                        ws.Cell(row, 5).Value = (XLCellValue)s.TypeLibelle;
                        ws.Cell(row, 6).Value = (XLCellValue)(s.NumeroDossier ?? s.ReferenceJugement ?? "");

                        ws.Cell(row, 7).Value = (XLCellValue)(double)s.MontantTotalDu;
                        ws.Cell(row, 7).Style.NumberFormat.Format = "#,##0";

                        ws.Cell(row, 8).Value = (XLCellValue)(double)s.RetenueMensuelleFixee;
                        ws.Cell(row, 8).Style.NumberFormat.Format = "#,##0";

                        ws.Cell(row, 9).Value = (XLCellValue)(double)s.TotalAmorti;
                        ws.Cell(row, 9).Style.NumberFormat.Format = "#,##0";

                        ws.Cell(row, 10).Value = (XLCellValue)(double)s.SoldeRestantDu;
                        ws.Cell(row, 10).Style.NumberFormat.Format = "#,##0";
                        ws.Cell(row, 10).Style.Font.Bold = true;

                        ws.Cell(row, 11).Value = (XLCellValue)(s.TauxProgression / 100.0);
                        ws.Cell(row, 11).Style.NumberFormat.Format = "0.0%";
                        ws.Cell(row, 11).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;

                        ws.Cell(row, 12).Value = (XLCellValue)s.StatutLibelle;
                        ws.Cell(row, 12).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

                        var rowRange = ws.Range(row, 1, row, headers.Length);
                        rowRange.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
                        rowRange.Style.Border.OutsideBorderColor = CouleurBordure;
                        rowRange.Style.Border.InsideBorder = XLBorderStyleValues.Thin;
                        rowRange.Style.Border.InsideBorderColor = CouleurBordure;

                        if (numLigne % 2 == 1)
                            rowRange.Style.Fill.BackgroundColor = CouleurAlternance;

                        row++;
                    }

                    int dataEndRow = row - 1;
                    if (liste.Count > 0)
                    {
                        ws.Cell(row, 1).Value = (XLCellValue)"TOTAUX";
                        ws.Range(row, 1, row, 6).Merge();
                        ws.Cell(row, 1).Style.Font.Bold = true;
                        ws.Cell(row, 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;

                        ws.Cell(row, 7).FormulaA1 = string.Format("SUM(G{0}:G{1})", dataStartRow, dataEndRow);
                        ws.Cell(row, 7).Style.Font.Bold = true;
                        ws.Cell(row, 7).Style.NumberFormat.Format = "#,##0";

                        ws.Cell(row, 9).FormulaA1 = string.Format("SUM(I{0}:I{1})", dataStartRow, dataEndRow);
                        ws.Cell(row, 9).Style.Font.Bold = true;
                        ws.Cell(row, 9).Style.NumberFormat.Format = "#,##0";

                        ws.Cell(row, 10).FormulaA1 = string.Format("SUM(J{0}:J{1})", dataStartRow, dataEndRow);
                        ws.Cell(row, 10).Style.Font.Bold = true;
                        ws.Cell(row, 10).Style.NumberFormat.Format = "#,##0";

                        var totalRange = ws.Range(row, 1, row, headers.Length);
                        totalRange.Style.Fill.BackgroundColor = CouleurTotaux;
                        totalRange.Style.Font.Bold = true;
                        totalRange.Style.Border.OutsideBorder = XLBorderStyleValues.Medium;
                        totalRange.Style.Border.OutsideBorderColor = CouleurEnTete;
                    }

                    ws.Columns().AdjustToContents();
                    workbook.SaveAs(cheminFichier);
                }
            });
        }
    }
}
