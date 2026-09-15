using System;
using System.IO;
using System.Text;
using ClosedXML.Excel;
using IUTSCalculator.Models;

namespace IUTSCalculator.Services
{
    /// <summary>
    /// Service d'export de la pièce d'OD de paie SYSCOHADA en classeur Excel et fichier texte (module CPT-01).
    /// </summary>
    public class EcriturePaieExportService
    {
        private static readonly XLColor CouleurEncre = XLColor.FromArgb(23, 33, 31);
        private static readonly XLColor CouleurAccent = XLColor.FromArgb(20, 103, 90);
        private static readonly XLColor CouleurTrame = XLColor.FromArgb(250, 251, 250);
        private static readonly XLColor CouleurAlerte = XLColor.FromArgb(156, 47, 40);
        private static readonly XLColor CouleurBordure = XLColor.FromArgb(224, 228, 224);

        private const string FormatMontant = "#,##0";

        /// <summary>
        /// Exporte la pièce comptable au format Excel (ClosedXML).
        /// </summary>
        public string ExporterExcel(PieceComptable piece, string cheminFichier)
        {
            if (piece == null) throw new ArgumentNullException(nameof(piece));
            if (string.IsNullOrWhiteSpace(cheminFichier))
                throw new ArgumentException("Chemin de fichier manquant.", nameof(cheminFichier));

            using (var classeur = new XLWorkbook())
            {
                var f = classeur.Worksheets.Add("Écritures de paie");
                f.Style.Font.FontName = "Segoe UI";
                f.Style.Font.FontSize = 10;

                int l = 1;

                // ── Titre ────────────────────────────────────────────────────
                f.Cell(l, 1).Value = "JOURNAL DES OPÉRATIONS DIVERSES — PAIE";
                f.Cell(l, 1).Style.Font.Bold = true;
                f.Cell(l, 1).Style.Font.FontSize = 14;
                f.Cell(l, 1).Style.Font.FontColor = CouleurAccent;
                f.Range(l, 1, l, 6).Merge();

                if (piece.EstBrouillon)
                {
                    f.Cell(l, 7).Value = "BROUILLON";
                    f.Cell(l, 7).Style.Font.Bold = true;
                    f.Cell(l, 7).Style.Font.FontSize = 12;
                    f.Cell(l, 7).Style.Font.FontColor = CouleurAlerte;
                    f.Cell(l, 7).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;
                    f.Range(l, 7, l, 9).Merge();
                }

                l++;

                f.Cell(l, 1).Value = string.Format("{0} — {1}", piece.RaisonSociale ?? "", piece.PeriodeLibelle);
                f.Cell(l, 1).Style.Font.FontColor = XLColor.FromArgb(110, 118, 132);
                f.Range(l, 1, l, 6).Merge();
                l++;

                if (!string.IsNullOrWhiteSpace(piece.Ifu))
                {
                    f.Cell(l, 1).Value = string.Format("IFU : {0} | Pièce N° : {1}", piece.Ifu, piece.NumeroPiece);
                    f.Cell(l, 1).Style.Font.FontColor = XLColor.FromArgb(110, 118, 132);
                    f.Cell(l, 1).Style.Font.FontSize = 9;
                    f.Range(l, 1, l, 6).Merge();
                    l++;
                }

                f.Cell(l, 1).Value = string.Format("Date de pièce : {0:dd/MM/yyyy} | Édité le {1:dd/MM/yyyy à HH:mm}",
                    piece.DatePiece, piece.DateGeneration);
                f.Cell(l, 1).Style.Font.FontColor = XLColor.FromArgb(150, 155, 165);
                f.Cell(l, 1).Style.Font.FontSize = 9;
                f.Range(l, 1, l, 6).Merge();
                l += 2;

                // ── En-tête du tableau ───────────────────────────────────────
                int entete = l;
                f.Cell(l, 1).Value = "N°";
                f.Cell(l, 2).Value = "Date";
                f.Cell(l, 3).Value = "Journal";
                f.Cell(l, 4).Value = "Pièce";
                f.Cell(l, 5).Value = "Compte";
                f.Cell(l, 6).Value = "Intitulé du compte";
                f.Cell(l, 7).Value = "Libellé de l'écriture";
                f.Cell(l, 8).Value = "Débit";
                f.Cell(l, 9).Value = "Crédit";

                var rangeEntete = f.Range(l, 1, l, 9);
                rangeEntete.Style.Font.Bold = true;
                rangeEntete.Style.Font.FontSize = 9;
                rangeEntete.Style.Font.FontColor = XLColor.White;
                rangeEntete.Style.Fill.BackgroundColor = CouleurAccent;
                rangeEntete.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                rangeEntete.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
                f.Row(l).Height = 24;
                l++;

                // ── Lignes ───────────────────────────────────────────────────
                int num = 0;
                foreach (var ligne in piece.Lignes)
                {
                    num++;
                    bool pair = (num % 2 == 0);

                    f.Cell(l, 1).Value = ligne.Ordre;
                    f.Cell(l, 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

                    f.Cell(l, 2).Value = ligne.DateEcriture;
                    f.Cell(l, 2).Style.DateFormat.Format = "dd/MM/yyyy";
                    f.Cell(l, 2).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

                    f.Cell(l, 3).Value = ligne.CodeJournal;
                    f.Cell(l, 3).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

                    f.Cell(l, 4).Value = ligne.ReferencePiece;
                    f.Cell(l, 4).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

                    f.Cell(l, 5).Value = ligne.CompteGeneral;
                    f.Cell(l, 5).Style.Font.Bold = true;
                    f.Cell(l, 5).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

                    f.Cell(l, 6).Value = ligne.IntituleCompte;
                    f.Cell(l, 7).Value = ligne.LibelleOperation;

                    if (ligne.Debit > 0m)
                        f.Cell(l, 8).Value = ligne.Debit;
                    f.Cell(l, 8).Style.NumberFormat.Format = FormatMontant;
                    f.Cell(l, 8).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;

                    if (ligne.Credit > 0m)
                        f.Cell(l, 9).Value = ligne.Credit;
                    f.Cell(l, 9).Style.NumberFormat.Format = FormatMontant;
                    f.Cell(l, 9).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;

                    if (pair)
                    {
                        f.Range(l, 1, l, 9).Style.Fill.BackgroundColor = CouleurTrame;
                    }

                    f.Range(l, 1, l, 9).Style.Border.BottomBorder = XLBorderStyleValues.Thin;
                    f.Range(l, 1, l, 9).Style.Border.BottomBorderColor = CouleurBordure;

                    f.Row(l).Height = 20;
                    l++;
                }

                // ── Totaux de contrôle ───────────────────────────────────────
                f.Cell(l, 1).Value = "TOTAL PIÈCE";
                f.Cell(l, 1).Style.Font.Bold = true;
                f.Range(l, 1, l, 7).Merge();
                f.Range(l, 1, l, 7).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;

                f.Cell(l, 8).Value = piece.TotalDebit;
                f.Cell(l, 8).Style.NumberFormat.Format = FormatMontant;
                f.Cell(l, 8).Style.Font.Bold = true;
                f.Cell(l, 8).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;

                f.Cell(l, 9).Value = piece.TotalCredit;
                f.Cell(l, 9).Style.NumberFormat.Format = FormatMontant;
                f.Cell(l, 9).Style.Font.Bold = true;
                f.Cell(l, 9).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;

                var rangeTotaux = f.Range(l, 1, l, 9);
                rangeTotaux.Style.Border.TopBorder = XLBorderStyleValues.Medium;
                rangeTotaux.Style.Border.TopBorderColor = CouleurAccent;
                rangeTotaux.Style.Border.BottomBorder = XLBorderStyleValues.Double;
                rangeTotaux.Style.Border.BottomBorderColor = CouleurAccent;
                rangeTotaux.Style.Fill.BackgroundColor = XLColor.FromArgb(240, 246, 244);
                f.Row(l).Height = 22;
                l += 2;

                // Statut d'équilibre
                f.Cell(l, 1).Value = piece.EstEquilibree
                    ? "✓ Pièce équilibrée (Écart Débit / Crédit = 0 FCFA)"
                    : string.Format("⚠ Pièce déséquilibrée — Écart : {0:N0} FCFA", piece.Ecart);
                f.Cell(l, 1).Style.Font.Bold = true;
                f.Cell(l, 1).Style.Font.FontColor = piece.EstEquilibree
                    ? XLColor.FromArgb(31, 122, 77)
                    : CouleurAlerte;
                f.Range(l, 1, l, 9).Merge();

                f.Columns().AdjustToContents();
                f.Column(1).Width = 6;
                f.Column(2).Width = 12;
                f.Column(3).Width = 10;
                f.Column(4).Width = 18;
                f.Column(5).Width = 12;
                f.Column(6).Width = 32;
                f.Column(7).Width = 38;
                f.Column(8).Width = 16;
                f.Column(9).Width = 16;

                classeur.SaveAs(cheminFichier);
            }

            return cheminFichier;
        }

        /// <summary>
        /// Exporte la pièce comptable au format texte / CSV tabulé pour import dans un logiciel comptable.
        /// </summary>
        public string ExporterTexte(PieceComptable piece, string cheminFichier)
        {
            if (piece == null) throw new ArgumentNullException(nameof(piece));
            if (string.IsNullOrWhiteSpace(cheminFichier))
                throw new ArgumentException("Chemin de fichier manquant.", nameof(cheminFichier));

            var sb = new StringBuilder();
            // Ligne d'en-tête standard
            sb.AppendLine("Date;Journal;Piece;Compte;Libelle;Debit;Credit");

            foreach (var l in piece.Lignes)
            {
                sb.AppendFormat("{0:dd/MM/yyyy};{1};{2};{3};{4};{5:0};{6:0}",
                    l.DateEcriture,
                    l.CodeJournal,
                    l.ReferencePiece,
                    l.CompteGeneral,
                    (l.LibelleOperation ?? "").Replace(";", ","),
                    l.Debit,
                    l.Credit);
                sb.AppendLine();
            }

            File.WriteAllText(cheminFichier, sb.ToString(), Encoding.UTF8);
            return cheminFichier;
        }
    }
}
