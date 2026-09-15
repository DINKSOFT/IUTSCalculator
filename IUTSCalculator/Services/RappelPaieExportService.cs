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
    /// Service d'exportation Excel pour les rappels de paie et la régularisation annuelle d'IUTS (module PAI-07).
    /// Produit les registres officiels, les fiches de simulation rétroactive et l'état fiscal de fin d'année.
    /// </summary>
    public class RappelPaieExportService
    {
        private static readonly XLColor CouleurEnTete = XLColor.FromHtml("#14675A");
        private static readonly XLColor CouleurBordure = XLColor.FromHtml("#CBD1CC");
        private static readonly XLColor CouleurAlternance = XLColor.FromHtml("#FAFBFA");
        private static readonly XLColor CouleurTotaux = XLColor.FromHtml("#E6F0ED");

        /// <summary>
        /// Exporte le registre général des rappels et régularisations en classeur Excel ClosedXML.
        /// </summary>
        public async Task ExporterRegistreRappelsExcelAsync(
            IEnumerable<RappelPaie> rappels,
            Dossier dossier,
            string cheminFichier)
        {
            if (rappels == null) throw new ArgumentNullException(nameof(rappels));
            if (string.IsNullOrWhiteSpace(cheminFichier)) throw new ArgumentNullException(nameof(cheminFichier));

            await Task.Run(() =>
            {
                var dir = Path.GetDirectoryName(cheminFichier);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                using (var workbook = new XLWorkbook())
                {
                    var ws = workbook.Worksheets.Add("Registre des rappels");
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
                    ws.Range(row, 1, row, 12).Merge();
                    ws.Cell(row, 1).Value = (XLCellValue)"REGISTRE DES RAPPELS DE SALAIRE ET RÉGULARISATIONS SUR PÉRIODES CLOSES";
                    ws.Cell(row, 1).Style.Font.Bold = true;
                    ws.Cell(row, 1).Style.Font.FontSize = 13;
                    ws.Cell(row, 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                    ws.Cell(row, 1).Style.Fill.BackgroundColor = CouleurTotaux;
                    row += 2;

                    // 3. Colonnes d'en-tête
                    string[] headers = new[]
                    {
                        "N°", "Matricule", "Salarié", "Type régularisation",
                        "Période origine", "Période imputation", "Sens",
                        "Montant Brut (FCFA)", "CNSS Salariale", "TPA", "Net (FCFA)", "Statut"
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
                    var liste = rappels.ToList();

                    foreach (var r in liste)
                    {
                        ws.Cell(row, 1).Value = (XLCellValue)numLigne++;
                        ws.Cell(row, 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

                        ws.Cell(row, 2).Value = (XLCellValue)(r.EmployeeId ?? "");
                        ws.Cell(row, 2).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

                        ws.Cell(row, 3).Value = (XLCellValue)(r.NomSalarie ?? "");
                        ws.Cell(row, 4).Value = (XLCellValue)r.TypeLibelle;

                        ws.Cell(row, 5).Value = (XLCellValue)r.PeriodeOrigineLibelle;
                        ws.Cell(row, 5).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

                        ws.Cell(row, 6).Value = (XLCellValue)r.PeriodeImputationLibelle;
                        ws.Cell(row, 6).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

                        ws.Cell(row, 7).Value = (XLCellValue)r.SensLibelle;
                        ws.Cell(row, 7).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

                        ws.Cell(row, 8).Value = (XLCellValue)(double)r.MontantBrut;
                        ws.Cell(row, 8).Style.NumberFormat.Format = "#,##0";

                        ws.Cell(row, 9).Value = (XLCellValue)(double)r.MontantCnssSalarial;
                        ws.Cell(row, 9).Style.NumberFormat.Format = "#,##0";

                        ws.Cell(row, 10).Value = (XLCellValue)(double)r.MontantTPA;
                        ws.Cell(row, 10).Style.NumberFormat.Format = "#,##0";

                        ws.Cell(row, 11).Value = (XLCellValue)(double)r.MontantNet;
                        ws.Cell(row, 11).Style.NumberFormat.Format = "#,##0";

                        ws.Cell(row, 12).Value = (XLCellValue)r.StatutLibelle;
                        ws.Cell(row, 12).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

                        for (int c = 1; c <= headers.Length; c++)
                        {
                            var cell = ws.Cell(row, c);
                            cell.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
                            cell.Style.Border.OutsideBorderColor = CouleurBordure;
                            if (numLigne % 2 == 0)
                                cell.Style.Fill.BackgroundColor = CouleurAlternance;
                        }

                        row++;
                    }

                    // 5. Ligne de totaux
                    if (liste.Count > 0)
                    {
                        ws.Cell(row, 1).Value = (XLCellValue)"TOTAL GÉNÉRAL";
                        ws.Range(row, 1, row, 7).Merge();
                        ws.Cell(row, 1).Style.Font.Bold = true;
                        ws.Cell(row, 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;

                        ws.Cell(row, 8).FormulaA1 = $"SUM(H{dataStartRow}:H{row - 1})";
                        ws.Cell(row, 8).Style.NumberFormat.Format = "#,##0";
                        ws.Cell(row, 8).Style.Font.Bold = true;

                        ws.Cell(row, 9).FormulaA1 = $"SUM(I{dataStartRow}:I{row - 1})";
                        ws.Cell(row, 9).Style.NumberFormat.Format = "#,##0";
                        ws.Cell(row, 9).Style.Font.Bold = true;

                        ws.Cell(row, 10).FormulaA1 = $"SUM(J{dataStartRow}:J{row - 1})";
                        ws.Cell(row, 10).Style.NumberFormat.Format = "#,##0";
                        ws.Cell(row, 10).Style.Font.Bold = true;

                        ws.Cell(row, 11).FormulaA1 = $"SUM(K{dataStartRow}:K{row - 1})";
                        ws.Cell(row, 11).Style.NumberFormat.Format = "#,##0";
                        ws.Cell(row, 11).Style.Font.Bold = true;

                        ws.Range(row, 1, row, 12).Style.Fill.BackgroundColor = CouleurTotaux;
                        ws.Range(row, 1, row, 12).Style.Border.TopBorder = XLBorderStyleValues.Medium;
                        ws.Range(row, 1, row, 12).Style.Border.BottomBorder = XLBorderStyleValues.Double;
                        ws.Range(row, 1, row, 12).Style.Border.TopBorderColor = CouleurBordure;
                        ws.Range(row, 1, row, 12).Style.Border.BottomBorderColor = CouleurBordure;
                    }

                    ws.Columns().AdjustToContents();
                    workbook.SaveAs(cheminFichier);
                }
            });
        }

        /// <summary>
        /// Exporte la feuille de calcul détaillée d'une simulation rétroactive multi-mois.
        /// </summary>
        public async Task ExporterSimulationRetroactiveExcelAsync(
            RappelPaie rappel,
            IEnumerable<LigneRappelRetroactifDetail> details,
            Dossier dossier,
            string cheminFichier)
        {
            if (rappel == null) throw new ArgumentNullException(nameof(rappel));
            if (details == null) throw new ArgumentNullException(nameof(details));
            if (string.IsNullOrWhiteSpace(cheminFichier)) throw new ArgumentNullException(nameof(cheminFichier));

            await Task.Run(() =>
            {
                var dir = Path.GetDirectoryName(cheminFichier);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                using (var workbook = new XLWorkbook())
                {
                    var ws = workbook.Worksheets.Add("Simulation rétroactive");
                    ws.PageSetup.PageOrientation = XLPageOrientation.Landscape;
                    ws.PageSetup.PaperSize = XLPaperSize.A4Paper;

                    int row = 1;

                    // En-tête société
                    ws.Cell(row, 1).Value = (XLCellValue)(dossier?.RaisonSociale ?? "ENTREPRISE");
                    ws.Cell(row, 1).Style.Font.Bold = true;
                    ws.Cell(row, 1).Style.Font.FontSize = 14;
                    row += 2;

                    // Titre
                    ws.Range(row, 1, row, 11).Merge();
                    ws.Cell(row, 1).Value = (XLCellValue)$"FICHE DE SIMULATION DIFFÉRENTIELLE RÉTROACTIVE — {rappel.NomSalarie}";
                    ws.Cell(row, 1).Style.Font.Bold = true;
                    ws.Cell(row, 1).Style.Font.FontSize = 13;
                    ws.Cell(row, 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                    ws.Cell(row, 1).Style.Fill.BackgroundColor = CouleurTotaux;
                    row += 2;

                    // Synthèse
                    ws.Cell(row, 1).Value = (XLCellValue)$"Salarié : {rappel.NomSalarie} (Matricule : {rappel.EmployeeId})";
                    ws.Cell(row, 1).Style.Font.Bold = true;
                    row++;
                    ws.Cell(row, 1).Value = (XLCellValue)$"Période rétroactive : {rappel.PeriodeOrigineLibelle}  |  Mois d'imputation cible : {rappel.PeriodeImputationLibelle}";
                    row++;
                    ws.Cell(row, 1).Value = (XLCellValue)$"Motif : {rappel.Motif}";
                    row += 2;

                    // Tableau détaillé
                    string[] headers = new[]
                    {
                        "Mois", "Ancien Base", "Nouveau Base", "Δ Base",
                        "Δ Primes", "Δ Brut Total", "Δ Assiette CNSS",
                        "CNSS Sal. (5,5%)", "CNSS Pat. (16%)", "TPA (3%)", "Net Différentiel"
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
                        cell.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
                        cell.Style.Border.OutsideBorderColor = CouleurBordure;
                    }
                    row++;

                    int dataStartRow = row;
                    var liste = details.ToList();

                    foreach (var d in liste)
                    {
                        ws.Cell(row, 1).Value = (XLCellValue)d.PeriodeLibelle;
                        ws.Cell(row, 2).Value = (XLCellValue)(double)d.AncienSalaireDeBase;
                        ws.Cell(row, 2).Style.NumberFormat.Format = "#,##0";
                        ws.Cell(row, 3).Value = (XLCellValue)(double)d.NouveauSalaireDeBase;
                        ws.Cell(row, 3).Style.NumberFormat.Format = "#,##0";
                        ws.Cell(row, 4).Value = (XLCellValue)(double)d.DeltaSalaireDeBase;
                        ws.Cell(row, 4).Style.NumberFormat.Format = "#,##0";
                        ws.Cell(row, 5).Value = (XLCellValue)(double)d.DeltaIndemnites;
                        ws.Cell(row, 5).Style.NumberFormat.Format = "#,##0";
                        ws.Cell(row, 6).Value = (XLCellValue)(double)d.DeltaBrut;
                        ws.Cell(row, 6).Style.NumberFormat.Format = "#,##0";
                        ws.Cell(row, 7).Value = (XLCellValue)(double)d.DeltaAssietteCnss;
                        ws.Cell(row, 7).Style.NumberFormat.Format = "#,##0";
                        ws.Cell(row, 8).Value = (XLCellValue)(double)d.DeltaCnssSalarial;
                        ws.Cell(row, 8).Style.NumberFormat.Format = "#,##0";
                        ws.Cell(row, 9).Value = (XLCellValue)(double)d.DeltaCnssPatronal;
                        ws.Cell(row, 9).Style.NumberFormat.Format = "#,##0";
                        ws.Cell(row, 10).Value = (XLCellValue)(double)d.DeltaTpa;
                        ws.Cell(row, 10).Style.NumberFormat.Format = "#,##0";
                        ws.Cell(row, 11).Value = (XLCellValue)(double)d.DeltaNet;
                        ws.Cell(row, 11).Style.NumberFormat.Format = "#,##0";

                        for (int c = 1; c <= headers.Length; c++)
                        {
                            ws.Cell(row, c).Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
                            ws.Cell(row, c).Style.Border.OutsideBorderColor = CouleurBordure;
                        }
                        row++;
                    }

                    // Totaux
                    if (liste.Count > 0)
                    {
                        ws.Cell(row, 1).Value = (XLCellValue)"TOTAL RAPPEL";
                        ws.Range(row, 1, row, 3).Merge();
                        ws.Cell(row, 1).Style.Font.Bold = true;
                        ws.Cell(row, 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;

                        for (int col = 4; col <= 11; col++)
                        {
                            string colLetter = ws.Column(col).ColumnLetter();
                            ws.Cell(row, col).FormulaA1 = $"SUM({colLetter}{dataStartRow}:{colLetter}{row - 1})";
                            ws.Cell(row, col).Style.NumberFormat.Format = "#,##0";
                            ws.Cell(row, col).Style.Font.Bold = true;
                        }

                        ws.Range(row, 1, row, 11).Style.Fill.BackgroundColor = CouleurTotaux;
                        ws.Range(row, 1, row, 11).Style.Border.TopBorder = XLBorderStyleValues.Medium;
                        ws.Range(row, 1, row, 11).Style.Border.BottomBorder = XLBorderStyleValues.Double;
                        ws.Range(row, 1, row, 11).Style.Border.TopBorderColor = CouleurBordure;
                        ws.Range(row, 1, row, 11).Style.Border.BottomBorderColor = CouleurBordure;
                    }

                    ws.Columns().AdjustToContents();
                    workbook.SaveAs(cheminFichier);
                }
            });
        }

        /// <summary>
        /// Exporte l'état fiscal de fin d'année de la régularisation annuelle d'IUTS (CGI Burkina Faso).
        /// </summary>
        public async Task ExporterRegularisationAnnuelleIUTSExcelAsync(
            IEnumerable<RegularisationIutsAnnuel> regularisations,
            Dossier dossier,
            int annee,
            string cheminFichier)
        {
            if (regularisations == null) throw new ArgumentNullException(nameof(regularisations));
            if (string.IsNullOrWhiteSpace(cheminFichier)) throw new ArgumentNullException(nameof(cheminFichier));

            await Task.Run(() =>
            {
                var dir = Path.GetDirectoryName(cheminFichier);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                using (var workbook = new XLWorkbook())
                {
                    var ws = workbook.Worksheets.Add($"Régularisation IUTS {annee}");
                    ws.PageSetup.PageOrientation = XLPageOrientation.Landscape;
                    ws.PageSetup.PaperSize = XLPaperSize.A4Paper;

                    int row = 1;

                    // En-tête
                    ws.Cell(row, 1).Value = (XLCellValue)(dossier?.RaisonSociale ?? "ENTREPRISE");
                    ws.Cell(row, 1).Style.Font.Bold = true;
                    ws.Cell(row, 1).Style.Font.FontSize = 14;
                    row++;

                    ws.Cell(row, 1).Value = (XLCellValue)$"IFU : {dossier?.IFU ?? "N/A"}  |  Exercice fiscal : {annee}";
                    ws.Cell(row, 1).Style.Font.FontSize = 9;
                    ws.Cell(row, 1).Style.Font.FontColor = XLColor.FromHtml("#48534F");
                    row += 2;

                    // Titre
                    ws.Range(row, 1, row, 9).Merge();
                    ws.Cell(row, 1).Value = (XLCellValue)$"ÉTAT DE RÉGULARISATION ANNUELLE DE L'IUTS — EXERCICE FISCAL {annee}";
                    ws.Cell(row, 1).Style.Font.Bold = true;
                    ws.Cell(row, 1).Style.Font.FontSize = 13;
                    ws.Cell(row, 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                    ws.Cell(row, 1).Style.Fill.BackgroundColor = CouleurTotaux;
                    row += 2;

                    string[] headers = new[]
                    {
                        "N°", "Matricule", "Salarié", "Mois travaillés",
                        "Cumul Brut Imposable (FCFA)", "IUTS Théorique Annuel",
                        "Acomptes IUTS Retenus", "Différence (+/-)", "Décision fiscale / Action"
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
                        cell.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
                        cell.Style.Border.OutsideBorderColor = CouleurBordure;
                    }
                    row++;

                    int numLigne = 1;
                    int dataStartRow = row;
                    var liste = regularisations.ToList();

                    foreach (var r in liste)
                    {
                        ws.Cell(row, 1).Value = (XLCellValue)numLigne++;
                        ws.Cell(row, 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

                        ws.Cell(row, 2).Value = (XLCellValue)(r.EmployeeId ?? "");
                        ws.Cell(row, 2).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

                        ws.Cell(row, 3).Value = (XLCellValue)(r.NomSalarie ?? "");

                        ws.Cell(row, 4).Value = (XLCellValue)r.NombreMoisTravailles;
                        ws.Cell(row, 4).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

                        ws.Cell(row, 5).Value = (XLCellValue)(double)r.CumulBrutImposable;
                        ws.Cell(row, 5).Style.NumberFormat.Format = "#,##0";

                        ws.Cell(row, 6).Value = (XLCellValue)(double)r.IutsTheoriqueAnnuel;
                        ws.Cell(row, 6).Style.NumberFormat.Format = "#,##0";

                        ws.Cell(row, 7).Value = (XLCellValue)(double)r.CumulIutsPreleve;
                        ws.Cell(row, 7).Style.NumberFormat.Format = "#,##0";

                        ws.Cell(row, 8).Value = (XLCellValue)(double)r.DifferenceRegularisation;
                        ws.Cell(row, 8).Style.NumberFormat.Format = "+#,##0;-#,##0;0";

                        ws.Cell(row, 9).Value = (XLCellValue)r.ActionRecommandee;

                        for (int c = 1; c <= headers.Length; c++)
                        {
                            var cell = ws.Cell(row, c);
                            cell.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
                            cell.Style.Border.OutsideBorderColor = CouleurBordure;
                            if (numLigne % 2 == 0)
                                cell.Style.Fill.BackgroundColor = CouleurAlternance;
                        }
                        row++;
                    }

                    // Totaux
                    if (liste.Count > 0)
                    {
                        ws.Cell(row, 1).Value = (XLCellValue)"TOTAL GÉNÉRAL";
                        ws.Range(row, 1, row, 4).Merge();
                        ws.Cell(row, 1).Style.Font.Bold = true;
                        ws.Cell(row, 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;

                        ws.Cell(row, 5).FormulaA1 = $"SUM(E{dataStartRow}:E{row - 1})";
                        ws.Cell(row, 5).Style.NumberFormat.Format = "#,##0";
                        ws.Cell(row, 5).Style.Font.Bold = true;

                        ws.Cell(row, 6).FormulaA1 = $"SUM(F{dataStartRow}:F{row - 1})";
                        ws.Cell(row, 6).Style.NumberFormat.Format = "#,##0";
                        ws.Cell(row, 6).Style.Font.Bold = true;

                        ws.Cell(row, 7).FormulaA1 = $"SUM(G{dataStartRow}:G{row - 1})";
                        ws.Cell(row, 7).Style.NumberFormat.Format = "#,##0";
                        ws.Cell(row, 7).Style.Font.Bold = true;

                        ws.Cell(row, 8).FormulaA1 = $"SUM(H{dataStartRow}:H{row - 1})";
                        ws.Cell(row, 8).Style.NumberFormat.Format = "+#,##0;-#,##0;0";
                        ws.Cell(row, 8).Style.Font.Bold = true;

                        ws.Range(row, 1, row, 9).Style.Fill.BackgroundColor = CouleurTotaux;
                        ws.Range(row, 1, row, 9).Style.Border.TopBorder = XLBorderStyleValues.Medium;
                        ws.Range(row, 1, row, 9).Style.Border.BottomBorder = XLBorderStyleValues.Double;
                        ws.Range(row, 1, row, 9).Style.Border.TopBorderColor = CouleurBordure;
                        ws.Range(row, 1, row, 9).Style.Border.BottomBorderColor = CouleurBordure;
                    }

                    ws.Columns().AdjustToContents();
                    workbook.SaveAs(cheminFichier);
                }
            });
        }
    }
}
