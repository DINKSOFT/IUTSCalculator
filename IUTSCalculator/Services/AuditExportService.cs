using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using ClosedXML.Excel;
using IUTSCalculator.Models;

namespace IUTSCalculator.Services
{
    /// <summary>
    /// Service d'exportation du journal d'audit (module ADM-02).
    /// Produit les rapports officiels en Excel (.xlsx) et en fichier d'audit délimité (.csv).
    /// </summary>
    public class AuditExportService
    {
        private static readonly XLColor CouleurEnTete = XLColor.FromHtml("#14675A");
        private static readonly XLColor CouleurBordure = XLColor.FromHtml("#CBD1CC");
        private static readonly XLColor CouleurAlternance = XLColor.FromHtml("#FAFBFA");

        public async Task ExporterExcelAsync(
            IEnumerable<EntreeAudit> entrees,
            Dossier dossier,
            FiltreAudit filtre,
            string cheminFichier)
        {
            if (entrees == null) throw new ArgumentNullException(nameof(entrees));
            if (string.IsNullOrWhiteSpace(cheminFichier)) throw new ArgumentNullException(nameof(cheminFichier));

            await Task.Run(() =>
            {
                using (var workbook = new XLWorkbook())
                {
                    var ws = workbook.Worksheets.Add("Journal d'audit");
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

                    // 2. Titre du rapport
                    ws.Range(row, 1, row, 11).Merge();
                    ws.Cell(row, 1).Value = (XLCellValue)"JOURNAL D'AUDIT ET DE TRAÇABILITÉ DES OPÉRATIONS DE PAIE";
                    ws.Cell(row, 1).Style.Font.Bold = true;
                    ws.Cell(row, 1).Style.Font.FontSize = 13;
                    ws.Cell(row, 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                    ws.Cell(row, 1).Style.Fill.BackgroundColor = XLColor.FromHtml("#E6F0ED");
                    row += 2;

                    // 3. Métadonnées d'édition et filtres
                    ws.Cell(row, 1).Value = (XLCellValue)string.Format("Date d'édition : {0:dd/MM/yyyy HH:mm}", DateTime.Now);
                    ws.Cell(row, 1).Style.Font.FontSize = 9;
                    ws.Cell(row, 1).Style.Font.FontColor = XLColor.FromHtml("#48534F");

                    if (filtre != null && filtre.EstActif)
                    {
                        var criteres = new List<string>();
                        if (filtre.DateDebut.HasValue) criteres.Add(string.Format("Du {0:dd/MM/yyyy}", filtre.DateDebut.Value));
                        if (filtre.DateFin.HasValue) criteres.Add(string.Format("Au {0:dd/MM/yyyy}", filtre.DateFin.Value));
                        if (!string.IsNullOrWhiteSpace(filtre.Entite) && filtre.Entite != "Toutes les entités")
                            criteres.Add("Entité : " + filtre.Entite);
                        if (!string.IsNullOrWhiteSpace(filtre.Action) && filtre.Action != "Toutes les actions")
                            criteres.Add("Action : " + filtre.Action);
                        if (!string.IsNullOrWhiteSpace(filtre.Utilisateur) && filtre.Utilisateur != "Tous les utilisateurs")
                            criteres.Add("Utilisateur : " + filtre.Utilisateur);

                        ws.Cell(row, 6).Value = (XLCellValue)("Filtres : " + string.Join(" | ", criteres));
                        ws.Cell(row, 6).Style.Font.FontSize = 9;
                        ws.Cell(row, 6).Style.Font.Italic = true;
                    }
                    row += 2;

                    // 4. En-têtes de colonnes
                    string[] headers =
                    {
                        "N°", "Date", "Heure", "Utilisateur", "Poste / Machine",
                        "Module / Entité", "Réf. / Matricule", "Action effectuée",
                        "Champ modifié", "Ancienne valeur", "Nouvelle valeur", "Détails et observations"
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

                    int startDataRow = row;
                    int index = 1;

                    foreach (var e in entrees)
                    {
                        ws.Cell(row, 1).Value = (XLCellValue)e.Id;
                        ws.Cell(row, 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

                        ws.Cell(row, 2).Value = (XLCellValue)e.DateCourte;
                        ws.Cell(row, 2).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

                        ws.Cell(row, 3).Value = (XLCellValue)e.Heure;
                        ws.Cell(row, 3).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

                        ws.Cell(row, 4).Value = (XLCellValue)(e.Utilisateur ?? "");
                        ws.Cell(row, 5).Value = (XLCellValue)(e.AdresseMachine ?? "");
                        ws.Cell(row, 6).Value = (XLCellValue)(e.Entite ?? "");
                        ws.Cell(row, 7).Value = (XLCellValue)(e.EntiteId ?? "");

                        ws.Cell(row, 8).Value = (XLCellValue)(e.Action ?? "");
                        if (e.EstModificationFinanciere)
                        {
                            ws.Cell(row, 8).Style.Font.Bold = true;
                            ws.Cell(row, 8).Style.Font.FontColor = XLColor.FromHtml("#0F5145");
                        }
                        else if (e.Action != null && e.Action.ToLowerInvariant().Contains("suppression"))
                        {
                            ws.Cell(row, 8).Style.Font.Bold = true;
                            ws.Cell(row, 8).Style.Font.FontColor = XLColor.FromHtml("#9C2F28");
                        }

                        ws.Cell(row, 9).Value = (XLCellValue)(e.ChampModifie ?? "");
                        ws.Cell(row, 10).Value = (XLCellValue)(e.AncienneValeur ?? "");
                        ws.Cell(row, 11).Value = (XLCellValue)(e.NouvelleValeur ?? "");
                        ws.Cell(row, 12).Value = (XLCellValue)(e.Details ?? "");

                        for (int c = 1; c <= 12; c++)
                        {
                            ws.Cell(row, c).Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
                            ws.Cell(row, c).Style.Border.OutsideBorderColor = CouleurBordure;
                            if (index % 2 == 0)
                                ws.Cell(row, c).Style.Fill.BackgroundColor = CouleurAlternance;
                        }

                        row++;
                        index++;
                    }

                    // 5. Ligne de synthèse
                    if (row > startDataRow)
                    {
                        ws.Cell(row, 1).Value = (XLCellValue)string.Format("Total : {0} événement(s) tracé(s)", index - 1);
                        ws.Cell(row, 1).Style.Font.Bold = true;
                        ws.Range(row, 1, row, 12).Merge();
                        ws.Row(row).Height = 20;
                    }

                    ws.Columns().AdjustToContents(8.0, 45.0);
                    workbook.SaveAs(cheminFichier);
                }
            });
        }

        public async Task ExporterCsvAsync(IEnumerable<EntreeAudit> entrees, string cheminFichier)
        {
            if (entrees == null) throw new ArgumentNullException(nameof(entrees));
            if (string.IsNullOrWhiteSpace(cheminFichier)) throw new ArgumentNullException(nameof(cheminFichier));

            var sb = new StringBuilder();
            // BOM UTF-8 pour ouverture directe sous Excel sans problème d'accents
            sb.AppendLine("Id;Date;Heure;Utilisateur;Machine;Module;Reference;Action;Champ;AncienneValeur;NouvelleValeur;Details");

            foreach (var e in entrees)
            {
                sb.AppendLine(string.Format(
                    "{0};{1};{2};{3};{4};{5};{6};{7};{8};{9};{10};{11}",
                    e.Id,
                    EchapperCsv(e.DateCourte),
                    EchapperCsv(e.Heure),
                    EchapperCsv(e.Utilisateur),
                    EchapperCsv(e.AdresseMachine),
                    EchapperCsv(e.Entite),
                    EchapperCsv(e.EntiteId),
                    EchapperCsv(e.Action),
                    EchapperCsv(e.ChampModifie),
                    EchapperCsv(e.AncienneValeur),
                    EchapperCsv(e.NouvelleValeur),
                    EchapperCsv(e.Details)));
            }

            await Task.Run(() => File.WriteAllText(cheminFichier, sb.ToString(), new UTF8Encoding(true)));
        }

        private static string EchapperCsv(string texte)
        {
            if (string.IsNullOrEmpty(texte)) return "";
            if (texte.Contains(";") || texte.Contains("\"") || texte.Contains("\n") || texte.Contains("\r"))
            {
                return "\"" + texte.Replace("\"", "\"\"") + "\"";
            }
            return texte;
        }
    }
}
