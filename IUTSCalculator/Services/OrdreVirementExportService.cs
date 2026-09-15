using System;
using System.IO;
using System.Text;
using ClosedXML.Excel;
using IUTSCalculator.Models;

namespace IUTSCalculator.Services
{
    /// <summary>
    /// Service d'export des ordres de virement et états des paiements (module CPT-02).
    /// Produit des classeurs Excel officiels (ClosedXML) et des fichiers CSV bancaires délimités.
    /// </summary>
    public class OrdreVirementExportService
    {
        private static readonly XLColor CouleurAccent = XLColor.FromArgb(20, 103, 90);       // #14675A
        private static readonly XLColor CouleurAccentClair = XLColor.FromArgb(230, 240, 237); // #E6F0ED
        private static readonly XLColor CouleurEntete = XLColor.FromArgb(15, 81, 69);        // #0F5145
        private static readonly XLColor CouleurTrame = XLColor.FromArgb(248, 250, 249);
        private static readonly XLColor CouleurBordure = XLColor.FromArgb(207, 226, 220);
        private static readonly XLColor CouleurAlerte = XLColor.FromArgb(156, 47, 40);       // #9C2F28

        private const string FormatMontant = "#,##0";

        /// <summary>
        /// Exporte l'ordre de virement et l'état des paiements dans un classeur Excel soigné.
        /// </summary>
        public string ExporterExcel(OrdreVirement ordre, string cheminFichier)
        {
            if (ordre == null) throw new ArgumentNullException(nameof(ordre));
            if (string.IsNullOrWhiteSpace(cheminFichier))
                throw new ArgumentException("Chemin de fichier manquant.", nameof(cheminFichier));

            var dossierParent = Path.GetDirectoryName(cheminFichier);
            if (!string.IsNullOrWhiteSpace(dossierParent))
                Directory.CreateDirectory(dossierParent);

            using (var classeur = new XLWorkbook())
            {
                // ═══════════════════════════════════════════════════════════════
                // FEUILLE 1 : ORDRE DE VIREMENT BANCAIRE
                // ═══════════════════════════════════════════════════════════════

                var f1 = classeur.Worksheets.Add("Ordre de virement");
                f1.PageSetup.PageOrientation = XLPageOrientation.Landscape;
                f1.PageSetup.PaperSize = XLPaperSize.A4Paper;
                f1.ShowGridLines = true;

                int l = 1;

                // Titre et statut
                f1.Cell(l, 1).Value = "ORDRE DE VIREMENT BANCAIRE COLLECTIF — SALAIRES";
                f1.Cell(l, 1).Style.Font.Bold = true;
                f1.Cell(l, 1).Style.Font.FontSize = 14;
                f1.Cell(l, 1).Style.Font.FontColor = CouleurAccent;
                f1.Range(l, 1, l, 6).Merge();

                if (ordre.EstBrouillon)
                {
                    f1.Cell(l, 7).Value = "BROUILLON (PÉRIODE NON CLÔTURÉE)";
                    f1.Cell(l, 7).Style.Font.Bold = true;
                    f1.Cell(l, 7).Style.Font.FontSize = 11;
                    f1.Cell(l, 7).Style.Font.FontColor = CouleurAlerte;
                    f1.Cell(l, 7).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;
                    f1.Range(l, 7, l, 8).Merge();
                }

                l++;

                // En-tête employeur
                f1.Cell(l, 1).Value = string.Format("{0} {1}", ordre.RaisonSociale ?? "", string.IsNullOrWhiteSpace(ordre.Sigle) ? "" : "(" + ordre.Sigle + ")").Trim();
                f1.Cell(l, 1).Style.Font.Bold = true;
                f1.Cell(l, 1).Style.Font.FontSize = 11;
                f1.Range(l, 1, l, 5).Merge();

                f1.Cell(l, 6).Value = string.Format("Période : {0}", ordre.PeriodeLibelle);
                f1.Cell(l, 6).Style.Font.Bold = true;
                f1.Cell(l, 6).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;
                f1.Range(l, 6, l, 8).Merge();

                l++;

                string ifuRccm = string.Format("IFU : {0} | RCCM : {1}", ordre.Ifu ?? "—", ordre.Rccm ?? "—");
                f1.Cell(l, 1).Value = ifuRccm;
                f1.Cell(l, 1).Style.Font.FontColor = XLColor.FromArgb(100, 110, 120);
                f1.Cell(l, 1).Style.Font.FontSize = 9;
                f1.Range(l, 1, l, 5).Merge();

                f1.Cell(l, 6).Value = string.Format("Réf. Ordre : {0} | Émis le : {1:dd/MM/yyyy}", ordre.NumeroOrdre, ordre.DateEmission);
                f1.Cell(l, 6).Style.Font.FontColor = XLColor.FromArgb(100, 110, 120);
                f1.Cell(l, 6).Style.Font.FontSize = 9;
                f1.Cell(l, 6).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;
                f1.Range(l, 6, l, 8).Merge();

                l++;

                if (!string.IsNullOrWhiteSpace(ordre.BanqueCible) && ordre.BanqueCible != "Toutes les banques")
                {
                    f1.Cell(l, 1).Value = string.Format("Banque destinataire : {0}", ordre.BanqueCible);
                    f1.Cell(l, 1).Style.Font.Bold = true;
                    f1.Cell(l, 1).Style.Font.FontColor = CouleurEntete;
                    f1.Range(l, 1, l, 5).Merge();
                    l++;
                }

                l++;

                // ── En-tête du tableau ───────────────────────────────────────
                int colEntete = l;
                f1.Cell(l, 1).Value = "N°";
                f1.Cell(l, 2).Value = "Matricule";
                f1.Cell(l, 3).Value = "Nom et Prénom";
                f1.Cell(l, 4).Value = "Fonction / Poste";
                f1.Cell(l, 5).Value = "Mode règlement";
                f1.Cell(l, 6).Value = "Établissement bancaire";
                f1.Cell(l, 7).Value = "N° de Compte / RIB";
                f1.Cell(l, 8).Value = "Net à payer (FCFA)";

                var rangeEntete = f1.Range(l, 1, l, 8);
                rangeEntete.Style.Font.Bold = true;
                rangeEntete.Style.Font.FontSize = 9;
                rangeEntete.Style.Font.FontColor = XLColor.White;
                rangeEntete.Style.Fill.BackgroundColor = CouleurAccent;
                rangeEntete.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                rangeEntete.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
                f1.Row(l).Height = 24;
                l++;

                // ── Lignes de données ────────────────────────────────────────
                int num = 0;
                foreach (var ligne in ordre.Lignes)
                {
                    num++;
                    bool pair = (num % 2 == 0);

                    f1.Cell(l, 1).Value = ligne.Ordre;
                    f1.Cell(l, 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

                    f1.Cell(l, 2).Value = ligne.EmployeeId;
                    f1.Cell(l, 2).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

                    f1.Cell(l, 3).Value = ligne.NomComplet;
                    f1.Cell(l, 3).Style.Font.Bold = true;

                    f1.Cell(l, 4).Value = ligne.Fonction ?? "";

                    f1.Cell(l, 5).Value = ligne.ModeReglement ?? "";
                    f1.Cell(l, 5).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

                    f1.Cell(l, 6).Value = ligne.Banque ?? "";

                    f1.Cell(l, 7).Value = ligne.NumeroCompte ?? "";
                    f1.Cell(l, 7).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

                    if (ligne.MontantNet > 0m)
                    {
                        var cellNet = f1.Cell(l, 8);
                        cellNet.Value = (XLCellValue)ligne.MontantNet;
                        cellNet.Style.NumberFormat.Format = FormatMontant;
                        cellNet.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;
                    }

                    if (pair)
                    {
                        f1.Range(l, 1, l, 8).Style.Fill.BackgroundColor = CouleurTrame;
                    }

                    // Surligner en rouge clair si anomalie (virement sans compte)
                    if (ligne.AAnomalie)
                    {
                        f1.Range(l, 1, l, 8).Style.Fill.BackgroundColor = XLColor.FromArgb(253, 237, 236);
                    }

                    f1.Range(l, 1, l, 8).Style.Border.BottomBorder = XLBorderStyleValues.Thin;
                    f1.Range(l, 1, l, 8).Style.Border.BottomBorderColor = CouleurBordure;

                    f1.Row(l).Height = 18;
                    l++;
                }

                // ── Ligne de total ───────────────────────────────────────────
                f1.Cell(l, 1).Value = "TOTAL GÉNÉRAL";
                f1.Cell(l, 1).Style.Font.Bold = true;
                f1.Cell(l, 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;
                f1.Range(l, 1, l, 7).Merge();

                var cellTotal = f1.Cell(l, 8);
                cellTotal.Value = (XLCellValue)ordre.TotalNetGeneral;
                cellTotal.Style.Font.Bold = true;
                cellTotal.Style.Font.FontSize = 10;
                cellTotal.Style.NumberFormat.Format = FormatMontant;
                cellTotal.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;

                var rangeTotal = f1.Range(l, 1, l, 8);
                rangeTotal.Style.Fill.BackgroundColor = CouleurAccentClair;
                rangeTotal.Style.Border.TopBorder = XLBorderStyleValues.Thin;
                rangeTotal.Style.Border.BottomBorder = XLBorderStyleValues.Double;
                rangeTotal.Style.Border.BottomBorderColor = CouleurAccent;
                f1.Row(l).Height = 22;
                l += 2;

                // ── Blocs de signature ───────────────────────────────────────
                f1.Cell(l, 2).Value = "Pour l'Employeur / La Direction Générale";
                f1.Cell(l, 2).Style.Font.Bold = true;
                f1.Cell(l, 2).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                f1.Range(l, 2, l, 3).Merge();

                f1.Cell(l, 6).Value = "Pour la Banque (Accusé de réception)";
                f1.Cell(l, 6).Style.Font.Bold = true;
                f1.Cell(l, 6).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                f1.Range(l, 6, l, 7).Merge();

                l++;
                f1.Cell(l, 2).Value = "Date, Signature et Cachet autorisés :";
                f1.Cell(l, 2).Style.Font.Italic = true;
                f1.Cell(l, 2).Style.Font.FontSize = 9;
                f1.Cell(l, 2).Style.Font.FontColor = XLColor.FromArgb(120, 125, 135);
                f1.Cell(l, 2).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                f1.Range(l, 2, l, 3).Merge();

                f1.Cell(l, 6).Value = "Date de réception, Visa et Cachet :";
                f1.Cell(l, 6).Style.Font.Italic = true;
                f1.Cell(l, 6).Style.Font.FontSize = 9;
                f1.Cell(l, 6).Style.Font.FontColor = XLColor.FromArgb(120, 125, 135);
                f1.Cell(l, 6).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                f1.Range(l, 6, l, 7).Merge();

                l += 4; // Espace pour signatures manuscrites

                f1.Columns().AdjustToContents();
                f1.Column(1).Width = 6;
                f1.Column(2).Width = 12;
                f1.Column(3).Width = 30;
                f1.Column(4).Width = 24;
                f1.Column(5).Width = 18;
                f1.Column(6).Width = 22;
                f1.Column(7).Width = 28;
                f1.Column(8).Width = 18;

                // ═══════════════════════════════════════════════════════════════
                // FEUILLE 2 : RÉCAPITULATIF DES MODES DE PAIEMENT
                // ═══════════════════════════════════════════════════════════════

                var f2 = classeur.Worksheets.Add("Récapitulatif règlements");
                f2.PageSetup.PageOrientation = XLPageOrientation.Portrait;
                f2.PageSetup.PaperSize = XLPaperSize.A4Paper;
                f2.ShowGridLines = true;

                int r = 1;
                f2.Cell(r, 1).Value = "RÉCAPITULATIF DES MODES DE RÈGLEMENT DE LA PAIE";
                f2.Cell(r, 1).Style.Font.Bold = true;
                f2.Cell(r, 1).Style.Font.FontSize = 13;
                f2.Cell(r, 1).Style.Font.FontColor = CouleurAccent;
                f2.Range(r, 1, r, 4).Merge();
                r++;

                f2.Cell(r, 1).Value = string.Format("{0} — Période : {1}", ordre.RaisonSociale ?? "", ordre.PeriodeLibelle);
                f2.Cell(r, 1).Style.Font.FontColor = XLColor.FromArgb(100, 110, 120);
                f2.Range(r, 1, r, 4).Merge();
                r += 2;

                // En-tête du tableau récap
                f2.Cell(r, 1).Value = "Mode de règlement";
                f2.Cell(r, 2).Value = "Nombre de salariés";
                f2.Cell(r, 3).Value = "Montant net (FCFA)";
                f2.Cell(r, 4).Value = "Part (%)";

                var rangeEntete2 = f2.Range(r, 1, r, 4);
                rangeEntete2.Style.Font.Bold = true;
                rangeEntete2.Style.Font.FontColor = XLColor.White;
                rangeEntete2.Style.Fill.BackgroundColor = CouleurAccent;
                rangeEntete2.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                f2.Row(r).Height = 22;
                r++;

                foreach (var rec in ordre.Recapitulatifs)
                {
                    f2.Cell(r, 1).Value = rec.Mode;
                    f2.Cell(r, 1).Style.Font.Bold = true;

                    f2.Cell(r, 2).Value = rec.Nombre;
                    f2.Cell(r, 2).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

                    var cMontant = f2.Cell(r, 3);
                    cMontant.Value = (XLCellValue)rec.Montant;
                    cMontant.Style.NumberFormat.Format = FormatMontant;
                    cMontant.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;

                    f2.Cell(r, 4).Value = string.Format("{0:0.0} %", rec.Pourcentage);
                    f2.Cell(r, 4).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;

                    f2.Range(r, 1, r, 4).Style.Border.BottomBorder = XLBorderStyleValues.Thin;
                    f2.Range(r, 1, r, 4).Style.Border.BottomBorderColor = CouleurBordure;
                    r++;
                }

                // Total récap
                f2.Cell(r, 1).Value = "TOTAL MASSE NETTE";
                f2.Cell(r, 1).Style.Font.Bold = true;

                f2.Cell(r, 2).Value = ordre.NombreTotalSalaries;
                f2.Cell(r, 2).Style.Font.Bold = true;
                f2.Cell(r, 2).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

                var cTotalRecap = f2.Cell(r, 3);
                cTotalRecap.Value = (XLCellValue)ordre.TotalNetGeneral;
                cTotalRecap.Style.Font.Bold = true;
                cTotalRecap.Style.NumberFormat.Format = FormatMontant;
                cTotalRecap.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;

                f2.Cell(r, 4).Value = "100.0 %";
                f2.Cell(r, 4).Style.Font.Bold = true;
                f2.Cell(r, 4).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;

                var rTot = f2.Range(r, 1, r, 4);
                rTot.Style.Fill.BackgroundColor = CouleurAccentClair;
                rTot.Style.Border.TopBorder = XLBorderStyleValues.Thin;
                rTot.Style.Border.BottomBorder = XLBorderStyleValues.Double;
                rTot.Style.Border.BottomBorderColor = CouleurAccent;
                r += 2;

                // Note de cadrage
                f2.Cell(r, 1).Value = "Note de contrôle : ce montant correspond strictement au Salaire Net à Payer du Livre de Paie et au compte 422000 du Journal des Opérations Diverses.";
                f2.Cell(r, 1).Style.Font.Italic = true;
                f2.Cell(r, 1).Style.Font.FontSize = 9;
                f2.Cell(r, 1).Style.Font.FontColor = XLColor.FromArgb(110, 120, 130);
                f2.Range(r, 1, r, 4).Merge();

                f2.Columns().AdjustToContents();
                f2.Column(1).Width = 26;
                f2.Column(2).Width = 20;
                f2.Column(3).Width = 22;
                f2.Column(4).Width = 14;

                classeur.SaveAs(cheminFichier);
            }

            return cheminFichier;
        }

        /// <summary>
        /// Exporte l'ordre de virement au format CSV/texte standard pour transmission bancaire.
        /// </summary>
        public string ExporterCsv(OrdreVirement ordre, string cheminFichier)
        {
            if (ordre == null) throw new ArgumentNullException(nameof(ordre));
            if (string.IsNullOrWhiteSpace(cheminFichier))
                throw new ArgumentException("Chemin de fichier manquant.", nameof(cheminFichier));

            var sb = new StringBuilder();
            // Ligne d'en-tête standard
            sb.AppendLine("Date;NumeroOrdre;Matricule;NomBeneficiaire;Banque;NumeroCompte;Montant;Motif");

            string dateEmissionStr = ordre.DateEmission.ToString("dd/MM/yyyy");
            string motif = string.Format("Virement salaires {0}", ordre.PeriodeLibelle).Replace(";", ",");

            foreach (var l in ordre.Lignes)
            {
                // Uniquement les virements dans le fichier de télétransmission
                if (!l.EstVirement) continue;

                sb.AppendFormat("{0};{1};{2};{3};{4};{5};{6:0};{7}",
                    dateEmissionStr,
                    ordre.NumeroOrdre,
                    l.EmployeeId,
                    (l.NomComplet ?? "").Replace(";", ","),
                    (l.Banque ?? "").Replace(";", ","),
                    (l.NumeroCompte ?? "").Replace(";", ",").Trim(),
                    l.MontantNet,
                    motif);
                sb.AppendLine();
            }

            File.WriteAllText(cheminFichier, sb.ToString(), Encoding.UTF8);
            return cheminFichier;
        }
    }
}
