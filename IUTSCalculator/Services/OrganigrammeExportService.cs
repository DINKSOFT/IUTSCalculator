using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ClosedXML.Excel;
using iTextSharp.text;
using iTextSharp.text.pdf;
using IUTSCalculator.Models;

namespace IUTSCalculator.Services
{
    /// <summary>
    /// Service d'export de l'organigramme d'entreprise en formats Excel et PDF (module RH-01).
    /// </summary>
    public class OrganigrammeExportService
    {
        #region Palette & Polices Excel

        private static readonly XLColor CouleurAccent = XLColor.FromArgb(20, 103, 90);    // #14675A
        private static readonly XLColor CouleurEncre = XLColor.FromArgb(23, 33, 31);     // #17211F
        private static readonly XLColor CouleurDiscret = XLColor.FromArgb(121, 131, 126); // #79837E
        private static readonly XLColor CouleurTrame = XLColor.FromArgb(245, 246, 245);   // #F5F6F5
        private static readonly XLColor CouleurBordure = XLColor.FromArgb(203, 209, 204); // #CBD1CC
        private static readonly XLColor CouleurAlerte = XLColor.FromArgb(156, 47, 40);    // #9C2F28
        private static readonly XLColor CouleurBleu = XLColor.FromArgb(30, 64, 120);     // #1E40AF

        #endregion

        #region Palette & Polices PDF

        private static readonly BaseColor PdfPrimaryColor = new BaseColor(23, 33, 31);
        private static readonly BaseColor PdfAccentColor = new BaseColor(20, 103, 90);
        private static readonly BaseColor PdfLightBg = new BaseColor(245, 246, 245);
        private static readonly BaseColor PdfBorderColor = new BaseColor(203, 209, 204);
        private static readonly BaseColor PdfMutedColor = new BaseColor(100, 116, 139);

        private static readonly Font PdfTitleFont = new Font(Font.FontFamily.HELVETICA, 15, Font.BOLD, PdfAccentColor);
        private static readonly Font PdfSubTitleFont = new Font(Font.FontFamily.HELVETICA, 10, Font.BOLD, PdfPrimaryColor);
        private static readonly Font PdfSectionFont = new Font(Font.FontFamily.HELVETICA, 11, Font.BOLD, PdfAccentColor);
        private static readonly Font PdfHeaderCellFont = new Font(Font.FontFamily.HELVETICA, 9, Font.BOLD, BaseColor.WHITE);
        private static readonly Font PdfCellBoldFont = new Font(Font.FontFamily.HELVETICA, 8.5f, Font.BOLD, PdfPrimaryColor);
        private static readonly Font PdfCellNormalFont = new Font(Font.FontFamily.HELVETICA, 8.5f, Font.NORMAL, PdfPrimaryColor);
        private static readonly Font PdfSmallMutedFont = new Font(Font.FontFamily.HELVETICA, 7.5f, Font.NORMAL, PdfMutedColor);

        #endregion

        #region Export Excel (ClosedXML)

        /// <summary>
        /// Exporte l'organigramme complet dans un classeur Excel à 3 feuilles :
        /// 1. Organigramme & Structures
        /// 2. Nomenclature des Postes
        /// 3. Salariés & Lignes Hiérarchiques
        /// </summary>
        public string ExporterExcel(
            List<UniteOrganisationnelle> unites,
            List<Poste> postes,
            List<AffectationSalarieLigne> affectations,
            string raisonSociale,
            string ifu,
            string cheminFichier)
        {
            if (string.IsNullOrWhiteSpace(cheminFichier))
                throw new ArgumentException("Le chemin de destination est obligatoire.", nameof(cheminFichier));

            unites = unites ?? new List<UniteOrganisationnelle>();
            postes = postes ?? new List<Poste>();
            affectations = affectations ?? new List<AffectationSalarieLigne>();

            using (var wb = new XLWorkbook())
            {
                ConstruireFeuilleStructures(wb, unites, raisonSociale, ifu);
                ConstruireFeuillePostes(wb, postes, raisonSociale, ifu);
                ConstruireFeuilleAffectations(wb, affectations, raisonSociale, ifu);

                wb.SaveAs(cheminFichier);
            }

            return cheminFichier;
        }

        private void ConstruireFeuilleStructures(XLWorkbook wb, List<UniteOrganisationnelle> unites, string raisonSociale, string ifu)
        {
            var ws = wb.Worksheets.Add("Structures & Unités");
            ws.ShowGridLines = true;
            ws.Style.Font.FontName = "Calibri";
            ws.Style.Font.FontSize = 10;

            // En-tête
            ws.Cell(1, 1).Value = "ORGANIGRAMME — STRUCTURES & UNITÉS";
            ws.Cell(1, 1).Style.Font.Bold = true;
            ws.Cell(1, 1).Style.Font.FontSize = 14;
            ws.Cell(1, 1).Style.Font.FontColor = CouleurAccent;
            ws.Range(1, 1, 1, 8).Merge();

            ws.Cell(2, 1).Value = string.Format("{0} — IFU : {1} — Édité le {2:dd/MM/yyyy HH:mm}",
                string.IsNullOrWhiteSpace(raisonSociale) ? "Entreprise" : raisonSociale,
                string.IsNullOrWhiteSpace(ifu) ? "Non renseigné" : ifu,
                DateTime.Now);
            ws.Cell(2, 1).Style.Font.FontColor = CouleurDiscret;
            ws.Range(2, 1, 2, 8).Merge();

            // Table Header
            string[] colonnes = { "Code", "Type d'unité", "Libellé de l'entité", "Entité parente", "Responsable désigné", "Nb Postes", "Effectif direct", "Effectif total" };
            int row = 4;
            for (int i = 0; i < colonnes.Length; i++)
            {
                var cell = ws.Cell(row, i + 1);
                cell.Value = colonnes[i];
                cell.Style.Font.Bold = true;
                cell.Style.Font.FontColor = XLColor.White;
                cell.Style.Fill.BackgroundColor = CouleurAccent;
                cell.Style.Alignment.Horizontal = (i >= 5) ? XLAlignmentHorizontalValues.Right : XLAlignmentHorizontalValues.Left;
                cell.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
            }
            ws.Row(row).Height = 22;

            row++;
            int totalSalariesDirects = 0;
            int totalPostes = 0;

            foreach (var u in unites)
            {
                ws.Cell(row, 1).Value = u.Code ?? "";
                ws.Cell(row, 2).Value = u.BadgeTypeLibelle;
                ws.Cell(row, 3).Value = u.Libelle ?? "";
                ws.Cell(row, 4).Value = u.ParentLibelle ?? "(Niveau racine)";
                ws.Cell(row, 5).Value = u.ResponsableNom ?? "Non pourvu";
                ws.Cell(row, 6).Value = u.NombrePostes;
                ws.Cell(row, 7).Value = u.NombreSalariesDirects;
                ws.Cell(row, 8).Value = u.NombreSalariesTotal;

                if (u.EstDirection)
                {
                    ws.Range(row, 1, row, 8).Style.Font.Bold = true;
                    ws.Range(row, 1, row, 8).Style.Fill.BackgroundColor = CouleurTrame;
                }

                ws.Cell(row, 6).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;
                ws.Cell(row, 7).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;
                ws.Cell(row, 8).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;

                totalPostes += u.NombrePostes;
                totalSalariesDirects += u.NombreSalariesDirects;
                row++;
            }

            // Totaux
            ws.Cell(row, 1).Value = "TOTAL ENTREPRISE";
            ws.Cell(row, 1).Style.Font.Bold = true;
            ws.Range(row, 1, row, 5).Merge();
            ws.Cell(row, 6).Value = totalPostes;
            ws.Cell(row, 6).Style.Font.Bold = true;
            ws.Cell(row, 7).Value = totalSalariesDirects;
            ws.Cell(row, 7).Style.Font.Bold = true;
            ws.Cell(row, 8).Value = totalSalariesDirects;
            ws.Cell(row, 8).Style.Font.Bold = true;

            ws.Range(row, 1, row, 8).Style.Border.TopBorder = XLBorderStyleValues.Thin;
            ws.Range(row, 1, row, 8).Style.Border.BottomBorder = XLBorderStyleValues.Double;

            ws.Columns().AdjustToContents(15, 45);
        }

        private void ConstruireFeuillePostes(XLWorkbook wb, List<Poste> postes, string raisonSociale, string ifu)
        {
            var ws = wb.Worksheets.Add("Nomenclature des Postes");
            ws.ShowGridLines = true;
            ws.Style.Font.FontName = "Calibri";
            ws.Style.Font.FontSize = 10;

            // En-tête
            ws.Cell(1, 1).Value = "NOMENCLATURE DES POSTES & FONCTIONS";
            ws.Cell(1, 1).Style.Font.Bold = true;
            ws.Cell(1, 1).Style.Font.FontSize = 14;
            ws.Cell(1, 1).Style.Font.FontColor = CouleurAccent;
            ws.Range(1, 1, 1, 9).Merge();

            ws.Cell(2, 1).Value = string.Format("{0} — Effectif budgété vs pourvu — Édité le {1:dd/MM/yyyy HH:mm}",
                string.IsNullOrWhiteSpace(raisonSociale) ? "Entreprise" : raisonSociale, DateTime.Now);
            ws.Cell(2, 1).Style.Font.FontColor = CouleurDiscret;
            ws.Range(2, 1, 2, 9).Merge();

            // Table Header
            string[] colonnes = { "Code Poste", "Intitulé du Poste", "Direction / Service", "N+1 Hiérarchique", "Salarié Titulaire", "Matricule", "Classification", "Effectif Requis", "Statut" };
            int row = 4;
            for (int i = 0; i < colonnes.Length; i++)
            {
                var cell = ws.Cell(row, i + 1);
                cell.Value = colonnes[i];
                cell.Style.Font.Bold = true;
                cell.Style.Font.FontColor = XLColor.White;
                cell.Style.Fill.BackgroundColor = CouleurAccent;
                cell.Style.Alignment.Horizontal = (i == 7) ? XLAlignmentHorizontalValues.Right : XLAlignmentHorizontalValues.Left;
                cell.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
            }
            ws.Row(row).Height = 22;

            row++;
            int totalRequis = 0;
            int totalPourvus = 0;

            foreach (var p in postes)
            {
                ws.Cell(row, 1).Value = p.Code ?? "";
                ws.Cell(row, 2).Value = p.Intitule ?? "";
                ws.Cell(row, 3).Value = p.UniteLibelle ?? "Non rattaché";
                ws.Cell(row, 4).Value = p.SuperieurPosteIntitule ?? "(Direction Générale)";
                ws.Cell(row, 5).Value = p.TitulaireNom ?? "Vacant";
                ws.Cell(row, 6).Value = p.TitulaireMatricule ?? "";
                ws.Cell(row, 7).Value = p.Classification ?? "";
                ws.Cell(row, 8).Value = p.EffectifRequis;
                ws.Cell(row, 9).Value = p.StatutOccupation;

                if (!p.EstPourvu)
                {
                    ws.Cell(row, 5).Style.Font.FontColor = CouleurAlerte;
                    ws.Cell(row, 9).Style.Font.FontColor = CouleurAlerte;
                }
                else
                {
                    totalPourvus++;
                }

                ws.Cell(row, 8).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;
                totalRequis += p.EffectifRequis;
                row++;
            }

            // Totaux
            ws.Cell(row, 1).Value = string.Format("TOTAL : {0} postes recensés ({1} pourvus, {2} vacants)", postes.Count, totalPourvus, postes.Count - totalPourvus);
            ws.Cell(row, 1).Style.Font.Bold = true;
            ws.Range(row, 1, row, 7).Merge();
            ws.Cell(row, 8).Value = totalRequis;
            ws.Cell(row, 8).Style.Font.Bold = true;

            ws.Range(row, 1, row, 9).Style.Border.TopBorder = XLBorderStyleValues.Thin;
            ws.Range(row, 1, row, 9).Style.Border.BottomBorder = XLBorderStyleValues.Double;

            ws.Columns().AdjustToContents(15, 45);
        }

        private void ConstruireFeuilleAffectations(XLWorkbook wb, List<AffectationSalarieLigne> affectations, string raisonSociale, string ifu)
        {
            var ws = wb.Worksheets.Add("Rattachements Salariés");
            ws.ShowGridLines = true;
            ws.Style.Font.FontName = "Calibri";
            ws.Style.Font.FontSize = 10;

            // En-tête
            ws.Cell(1, 1).Value = "SALARIÉS & LIGNES HIÉRARCHIQUES DIRECTES";
            ws.Cell(1, 1).Style.Font.Bold = true;
            ws.Cell(1, 1).Style.Font.FontSize = 14;
            ws.Cell(1, 1).Style.Font.FontColor = CouleurAccent;
            ws.Range(1, 1, 1, 7).Merge();

            ws.Cell(2, 1).Value = string.Format("{0} — Effectif nominatif ({1} collaborateurs) — Édité le {2:dd/MM/yyyy HH:mm}",
                string.IsNullOrWhiteSpace(raisonSociale) ? "Entreprise" : raisonSociale, affectations.Count, DateTime.Now);
            ws.Cell(2, 1).Style.Font.FontColor = CouleurDiscret;
            ws.Range(2, 1, 2, 7).Merge();

            // Table Header
            string[] colonnes = { "Matricule", "Nom & Prénoms", "Catégorie", "Service d'affectation", "Poste occupé", "Supérieur Direct N+1", "Statut" };
            int row = 4;
            for (int i = 0; i < colonnes.Length; i++)
            {
                var cell = ws.Cell(row, i + 1);
                cell.Value = colonnes[i];
                cell.Style.Font.Bold = true;
                cell.Style.Font.FontColor = XLColor.White;
                cell.Style.Fill.BackgroundColor = CouleurAccent;
                cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Left;
                cell.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
            }
            ws.Row(row).Height = 22;

            row++;
            foreach (var aff in affectations)
            {
                ws.Cell(row, 1).Value = aff.Matricule ?? "";
                ws.Cell(row, 2).Value = aff.NomComplet ?? "";
                ws.Cell(row, 3).Value = aff.Category ?? "";
                ws.Cell(row, 4).Value = aff.ServiceActuel ?? "Non renseigné";
                ws.Cell(row, 5).Value = aff.PosteActuelLibelle ?? "Non assigné";
                ws.Cell(row, 6).Value = aff.ManagerActuelNom ?? "Non renseigné";
                ws.Cell(row, 7).Value = aff.StatutLibelle;

                if (!aff.EstActif)
                {
                    ws.Range(row, 1, row, 7).Style.Font.FontColor = CouleurDiscret;
                }

                row++;
            }

            ws.Columns().AdjustToContents(15, 45);
        }

        #endregion

        #region Export PDF (iTextSharp)

        /// <summary>
        /// Génère un état officiel de l'organigramme en document PDF format A4.
        /// </summary>
        public string ExporterPdf(
            List<UniteOrganisationnelle> unites,
            List<Poste> postes,
            string raisonSociale,
            string ifu,
            string rccm,
            string cheminFichier)
        {
            if (string.IsNullOrWhiteSpace(cheminFichier))
                throw new ArgumentException("Le chemin du fichier est obligatoire.", nameof(cheminFichier));

            unites = unites ?? new List<UniteOrganisationnelle>();
            postes = postes ?? new List<Poste>();

            using (var fs = new FileStream(cheminFichier, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                // Document A4 Portrait avec marges 36pt (1,27 cm)
                using (var doc = new Document(PageSize.A4, 36, 36, 36, 36))
                {
                    var writer = PdfWriter.GetInstance(doc, fs);
                    doc.Open();

                    // 1. En-tête officiel de l'entreprise
                    var tableHeader = new PdfPTable(2) { WidthPercentage = 100 };
                    tableHeader.SetWidths(new float[] { 65f, 35f });

                    var cellInfo = new PdfPCell { Border = Rectangle.NO_BORDER };
                    cellInfo.AddElement(new Paragraph(string.IsNullOrWhiteSpace(raisonSociale) ? "ENTREPRISE" : raisonSociale.ToUpperInvariant(), PdfTitleFont));
                    cellInfo.AddElement(new Paragraph(string.Format("IFU : {0}  |  RCCM : {1}", string.IsNullOrWhiteSpace(ifu) ? "-" : ifu, string.IsNullOrWhiteSpace(rccm) ? "-" : rccm), PdfSmallMutedFont));
                    tableHeader.AddCell(cellInfo);

                    var cellDoc = new PdfPCell { Border = Rectangle.NO_BORDER, HorizontalAlignment = Element.ALIGN_RIGHT };
                    var pDoc = new Paragraph("ORGANIGRAMME GÉNÉRAL", PdfSubTitleFont) { Alignment = Element.ALIGN_RIGHT };
                    var pDate = new Paragraph("Édité le " + DateTime.Now.ToString("dd/MM/yyyy"), PdfSmallMutedFont) { Alignment = Element.ALIGN_RIGHT };
                    cellDoc.AddElement(pDoc);
                    cellDoc.AddElement(pDate);
                    tableHeader.AddCell(cellDoc);

                    doc.Add(tableHeader);
                    doc.Add(new Paragraph(new Chunk(new iTextSharp.text.pdf.draw.LineSeparator(0.8f, 100, PdfAccentColor, Element.ALIGN_CENTER, -4))));
                    doc.Add(new Paragraph(" "));

                    // 2. Synthèse des Directions & Services
                    doc.Add(new Paragraph("1. STRUCTURES ORGANISATIONNELLES (DIRECTIONS & SERVICES)", PdfSectionFont));
                    doc.Add(new Paragraph(" "));

                    var tableUnites = new PdfPTable(5) { WidthPercentage = 100 };
                    tableUnites.SetWidths(new float[] { 15f, 35f, 25f, 12f, 13f });

                    AjouterEnteteTableauPdf(tableUnites, new[] { "Code", "Direction / Service", "Responsable désigné", "Postes", "Effectif" });

                    foreach (var u in unites)
                    {
                        var c1 = CreerCellulePdf(u.Code ?? "", PdfCellBoldFont);
                        var c2 = CreerCellulePdf(u.Libelle ?? "", u.EstDirection ? PdfCellBoldFont : PdfCellNormalFont);
                        var c3 = CreerCellulePdf(u.ResponsableNom ?? "—", PdfCellNormalFont);
                        var c4 = CreerCellulePdf(u.NombrePostes.ToString(), PdfCellNormalFont, Element.ALIGN_RIGHT);
                        var c5 = CreerCellulePdf(u.NombreSalariesDirects.ToString(), PdfCellBoldFont, Element.ALIGN_RIGHT);

                        if (u.EstDirection)
                        {
                            c1.BackgroundColor = PdfLightBg;
                            c2.BackgroundColor = PdfLightBg;
                            c3.BackgroundColor = PdfLightBg;
                            c4.BackgroundColor = PdfLightBg;
                            c5.BackgroundColor = PdfLightBg;
                        }

                        tableUnites.AddCell(c1);
                        tableUnites.AddCell(c2);
                        tableUnites.AddCell(c3);
                        tableUnites.AddCell(c4);
                        tableUnites.AddCell(c5);
                    }

                    doc.Add(tableUnites);
                    doc.Add(new Paragraph(" "));

                    // 3. Nomenclature des Postes Clés
                    doc.Add(new Paragraph("2. NOMENCLATURE DES POSTES & RATTACHEMENTS N+1", PdfSectionFont));
                    doc.Add(new Paragraph(" "));

                    var tablePostes = new PdfPTable(5) { WidthPercentage = 100 };
                    tablePostes.SetWidths(new float[] { 18f, 32f, 25f, 15f, 10f });

                    AjouterEnteteTableauPdf(tablePostes, new[] { "Code", "Intitulé du Poste", "Direction / Service", "N+1 Direct", "Statut" });

                    foreach (var p in postes.Take(30)) // Limité à 30 pour un affichage propre A4
                    {
                        tablePostes.AddCell(CreerCellulePdf(p.Code ?? "", PdfCellBoldFont));
                        tablePostes.AddCell(CreerCellulePdf(p.Intitule ?? "", PdfCellNormalFont));
                        tablePostes.AddCell(CreerCellulePdf(p.UniteLibelle ?? "—", PdfCellNormalFont));
                        tablePostes.AddCell(CreerCellulePdf(p.SuperieurPosteIntitule ?? "Direction Générale", PdfSmallMutedFont));
                        tablePostes.AddCell(CreerCellulePdf(p.StatutOccupation, p.EstPourvu ? PdfCellNormalFont : PdfCellBoldFont, Element.ALIGN_CENTER));
                    }

                    doc.Add(tablePostes);
                    doc.Add(new Paragraph(" "));

                    // 4. Cartouche de validation
                    doc.Add(new Paragraph(" "));
                    var tableSign = new PdfPTable(2) { WidthPercentage = 100 };
                    tableSign.SetWidths(new float[] { 50f, 50f });

                    var cSign1 = new PdfPCell { Border = Rectangle.NO_BORDER };
                    cSign1.AddElement(new Paragraph("La Direction des Ressources Humaines", PdfCellBoldFont));
                    cSign1.AddElement(new Paragraph("\n\n\n___________________________", PdfSmallMutedFont));
                    tableSign.AddCell(cSign1);

                    var cSign2 = new PdfPCell { Border = Rectangle.NO_BORDER, HorizontalAlignment = Element.ALIGN_RIGHT };
                    var pS2 = new Paragraph("La Direction Générale", PdfCellBoldFont) { Alignment = Element.ALIGN_RIGHT };
                    var pS2b = new Paragraph("\n\n\n___________________________", PdfSmallMutedFont) { Alignment = Element.ALIGN_RIGHT };
                    cSign2.AddElement(pS2);
                    cSign2.AddElement(pS2b);
                    tableSign.AddCell(cSign2);

                    doc.Add(tableSign);
                    doc.Close();
                }
            }

            return cheminFichier;
        }

        private static void AjouterEnteteTableauPdf(PdfPTable table, string[] titres)
        {
            foreach (var t in titres)
            {
                var cell = new PdfPCell(new Phrase(t, PdfHeaderCellFont))
                {
                    BackgroundColor = PdfAccentColor,
                    Padding = 5f,
                    HorizontalAlignment = Element.ALIGN_LEFT,
                    VerticalAlignment = Element.ALIGN_MIDDLE,
                    BorderColor = PdfBorderColor
                };
                table.AddCell(cell);
            }
        }

        private static PdfPCell CreerCellulePdf(string texte, Font police, int alignement = Element.ALIGN_LEFT)
        {
            return new PdfPCell(new Phrase(texte ?? "", police))
            {
                Padding = 4.5f,
                HorizontalAlignment = alignement,
                VerticalAlignment = Element.ALIGN_MIDDLE,
                BorderColor = PdfBorderColor
            };
        }

        #endregion
    }
}
