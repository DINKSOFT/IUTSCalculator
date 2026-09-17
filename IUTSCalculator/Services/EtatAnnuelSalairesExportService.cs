using System;
using System.Linq;
using ClosedXML.Excel;
using IUTSCalculator.Models;

namespace IUTSCalculator.Services
{
    /// <summary>
    /// Export de l'état annuel des salaires en classeur Excel (module DEC-05).
    ///
    /// Feuille unique « État annuel des salaires » : en-tête employeur, tableau
    /// croisé salariés × rubriques avec cumuls annuels, totaux de contrôle. Mêmes
    /// couleurs que les autres états (DEC-03, DEC-04, ETA-01), pour une identité
    /// visuelle cohérente.
    /// </summary>
    public class EtatAnnuelSalairesExportService
    {
        private static readonly XLColor Encre = XLColor.FromArgb(31, 41, 55);
        private static readonly XLColor Accent = XLColor.FromArgb(30, 64, 120);
        private static readonly XLColor Trame = XLColor.FromArgb(243, 245, 248);
        private static readonly XLColor Alerte = XLColor.FromArgb(180, 45, 45);

        private const string FormatMontant = "#,##0";

        /// <summary>
        /// Écrit le classeur à l'emplacement demandé et renvoie ce chemin.
        /// </summary>
        public string Exporter(EtatAnnuelSalaires etat, string cheminFichier)
        {
            if (etat == null) throw new ArgumentNullException("etat");
            if (string.IsNullOrWhiteSpace(cheminFichier))
                throw new ArgumentException("Chemin de fichier manquant.", "cheminFichier");

            using (var classeur = new XLWorkbook())
            {
                ConstruireFeuille(classeur, etat);
                classeur.SaveAs(cheminFichier);
            }

            return cheminFichier;
        }

        private void ConstruireFeuille(XLWorkbook classeur, EtatAnnuelSalaires d)
        {
            var f = classeur.Worksheets.Add("État annuel des salaires");
            f.Style.Font.FontName = "Calibri";
            f.Style.Font.FontSize = 10;

            int l = 1;

            // ── Titre ────────────────────────────────────────────────────────
            f.Cell(l, 1).Value = "ÉTAT ANNUEL DES SALAIRES (DADS)";
            f.Cell(l, 1).Style.Font.Bold = true;
            f.Cell(l, 1).Style.Font.FontSize = 15;
            f.Cell(l, 1).Style.Font.FontColor = Accent;
            f.Range(l, 1, l, 6).Merge();

            if (!d.EstComplet)
            {
                f.Cell(l, 7).Value = "ÉTAT PARTIEL";
                f.Cell(l, 7).Style.Font.Bold = true;
                f.Cell(l, 7).Style.Font.FontSize = 14;
                f.Cell(l, 7).Style.Font.FontColor = Alerte;
            }

            l++;

            f.Cell(l, 1).Value = string.Format("{0} — {1}", d.RaisonSociale ?? "", d.PeriodeLibelle);
            f.Cell(l, 1).Style.Font.FontColor = XLColor.FromArgb(110, 118, 132);
            f.Range(l, 1, l, 6).Merge();
            l++;

            // Informations employeur
            if (!string.IsNullOrWhiteSpace(d.Ifu))
            {
                f.Cell(l, 1).Value = string.Format("IFU : {0}", d.Ifu);
                f.Cell(l, 1).Style.Font.FontColor = XLColor.FromArgb(110, 118, 132);
                f.Cell(l, 1).Style.Font.FontSize = 9;
            }
            if (!string.IsNullOrWhiteSpace(d.Rccm))
            {
                f.Cell(l, 3).Value = string.Format("RCCM : {0}", d.Rccm);
                f.Cell(l, 3).Style.Font.FontColor = XLColor.FromArgb(110, 118, 132);
                f.Cell(l, 3).Style.Font.FontSize = 9;
            }
            l++;

            f.Cell(l, 1).Value = string.Format(
                "Mois clôturés inclus dans ce cumul ({0}/12) : {1}", d.MoisClotures.Count, d.MoisClosLibelle);
            f.Cell(l, 1).Style.Font.FontColor = XLColor.FromArgb(110, 118, 132);
            f.Cell(l, 1).Style.Font.FontSize = 9;
            f.Range(l, 1, l, 6).Merge();
            l++;

            if (!d.EstComplet)
            {
                f.Cell(l, 1).Value = string.Format("Mois non clôturés, absents de ce cumul : {0}", d.MoisManquantsLibelle);
                f.Cell(l, 1).Style.Font.FontColor = Alerte;
                f.Cell(l, 1).Style.Font.FontSize = 9;
                f.Range(l, 1, l, 6).Merge();
                l++;
            }

            f.Cell(l, 1).Value = string.Format("Édité le {0:dd/MM/yyyy à HH:mm}", d.DateGeneration);
            f.Cell(l, 1).Style.Font.FontColor = XLColor.FromArgb(160, 165, 175);
            f.Cell(l, 1).Style.Font.FontSize = 9;
            f.Range(l, 1, l, 4).Merge();
            l += 2;

            // ── Calcul des colonnes ──────────────────────────────────────────
            int col = 1;
            int colNo = col++;
            int colMatricule = col++;
            int colNomPrenom = col++;
            int colCategorie = col++;
            int colEmploi = col++;
            int colCnss = col++;
            int colNbBulletins = col++;

            int premiereDynamique = col;
            foreach (var cd in d.ColonnesDynamiques)
                col++;
            int derniereDynamique = col - 1;

            int colBrut = col++;
            int colBaseImposable = col++;
            int colCnssSal = col++;
            int colIuts = col++;
            int colNet = col++;
            int colCnssPat = col++;
            int colTpa = col++;
            int colMasse = col++;
            int dernierCol = col - 1;

            // ── En-tête du tableau ───────────────────────────────────────────
            int entete = l;

            f.Cell(l, colNo).Value = "N°";
            f.Cell(l, colMatricule).Value = "Matricule";
            f.Cell(l, colNomPrenom).Value = "Nom et prénom";
            f.Cell(l, colCategorie).Value = "Catégorie";
            f.Cell(l, colEmploi).Value = "Emploi";
            f.Cell(l, colCnss).Value = "N° CNSS";
            f.Cell(l, colNbBulletins).Value = "Nb bulletins";

            int idxDyn = premiereDynamique;
            foreach (var cd in d.ColonnesDynamiques)
            {
                f.Cell(l, idxDyn).Value = cd.Libelle;
                idxDyn++;
            }

            f.Cell(l, colBrut).Value = "Cumul brut";
            f.Cell(l, colBaseImposable).Value = "Cumul base imposable";
            f.Cell(l, colCnssSal).Value = "Cumul CNSS salarié";
            f.Cell(l, colIuts).Value = "Cumul IUTS retenu";
            f.Cell(l, colNet).Value = "Cumul net à payer";
            f.Cell(l, colCnssPat).Value = "Cumul CNSS patron";
            f.Cell(l, colTpa).Value = "Cumul TPA";
            f.Cell(l, colMasse).Value = "Cumul masse salariale";

            var rangeEntete = f.Range(l, 1, l, dernierCol);
            rangeEntete.Style.Font.Bold = true;
            rangeEntete.Style.Font.FontSize = 9;
            rangeEntete.Style.Font.FontColor = XLColor.White;
            rangeEntete.Style.Fill.BackgroundColor = Accent;
            rangeEntete.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            rangeEntete.Style.Alignment.WrapText = true;
            l++;

            // ── Lignes de données ────────────────────────────────────────────
            int numero = 0;
            foreach (var ligne in d.Lignes)
            {
                numero++;
                bool pair = (numero % 2 == 0);

                f.Cell(l, colNo).Value = numero;
                f.Cell(l, colMatricule).Value = ligne.Matricule ?? "";
                f.Cell(l, colNomPrenom).Value = ligne.NomComplet;
                f.Cell(l, colCategorie).Value = ligne.Categorie ?? "";
                f.Cell(l, colEmploi).Value = ligne.Emploi ?? "";
                f.Cell(l, colCnss).Value = ligne.NumeroCnss ?? "";
                f.Cell(l, colNbBulletins).Value = ligne.NombreBulletins;

                idxDyn = premiereDynamique;
                foreach (var cd in d.ColonnesDynamiques)
                {
                    decimal montant;
                    f.Cell(l, idxDyn).Value = ligne.MontantsParRubrique.TryGetValue(cd.Code, out montant) ? montant : 0m;
                    idxDyn++;
                }

                f.Cell(l, colBrut).Value = ligne.CumulSalaireBrut;
                f.Cell(l, colBaseImposable).Value = ligne.CumulBaseImposable;
                f.Cell(l, colCnssSal).Value = ligne.CumulCnssSalariale;
                f.Cell(l, colIuts).Value = ligne.CumulIuts;
                f.Cell(l, colNet).Value = ligne.CumulNetAPayer;
                f.Cell(l, colCnssPat).Value = ligne.CumulCnssPatronale;
                f.Cell(l, colTpa).Value = ligne.CumulTpa;
                f.Cell(l, colMasse).Value = ligne.CumulMasseSalariale;

                if (pair)
                    f.Range(l, 1, l, dernierCol).Style.Fill.BackgroundColor = Trame;

                l++;
            }

            // ── Ligne de totaux ──────────────────────────────────────────────
            int ligneTotaux = l;

            f.Cell(l, colNomPrenom).Value = string.Format("TOTAUX ({0} salarié(s))", d.Effectif);

            idxDyn = premiereDynamique;
            foreach (var cd in d.ColonnesDynamiques)
            {
                decimal totalCol = d.Lignes.Sum(lg =>
                {
                    decimal m;
                    return lg.MontantsParRubrique.TryGetValue(cd.Code, out m) ? m : 0m;
                });
                f.Cell(l, idxDyn).Value = totalCol;
                idxDyn++;
            }

            f.Cell(l, colBrut).Value = d.TotalSalaireBrut;
            f.Cell(l, colBaseImposable).Value = d.TotalBaseImposable;
            f.Cell(l, colCnssSal).Value = d.TotalCnssSalariale;
            f.Cell(l, colIuts).Value = d.TotalIuts;
            f.Cell(l, colNet).Value = d.TotalNetAPayer;
            f.Cell(l, colCnssPat).Value = d.TotalCnssPatronale;
            f.Cell(l, colTpa).Value = d.TotalTpa;
            f.Cell(l, colMasse).Value = d.TotalMasseSalariale;

            var rangeTotaux = f.Range(l, 1, l, dernierCol);
            rangeTotaux.Style.Font.Bold = true;
            rangeTotaux.Style.Font.FontColor = Accent;
            rangeTotaux.Style.Border.TopBorder = XLBorderStyleValues.Medium;

            // ── Format montants ────────────────────────────────────────────────
            for (int c = premiereDynamique; c <= dernierCol; c++)
            {
                if (c == colNbBulletins) continue;
                f.Column(c).Style.NumberFormat.Format = FormatMontant;
            }

            // ── Largeurs de colonnes ──────────────────────────────────────────
            f.Column(colNo).Width = 5;
            f.Column(colMatricule).Width = 14;
            f.Column(colNomPrenom).Width = 28;
            f.Column(colCategorie).Width = 16;
            f.Column(colEmploi).Width = 18;
            f.Column(colCnss).Width = 14;
            f.Column(colNbBulletins).Width = 11;
            for (int c = premiereDynamique; c <= dernierCol; c++)
                f.Column(c).Width = 16;

            // ── Gel des lignes d'en-tête et des premières colonnes ────────────
            f.SheetView.FreezeRows(entete);
            f.SheetView.FreezeColumns(colNomPrenom);

            // ── Bordures ─────────────────────────────────────────────────────
            if (d.Lignes.Count > 0)
            {
                var rangeTableau = f.Range(entete, 1, ligneTotaux, dernierCol);
                rangeTableau.Style.Border.InsideBorder = XLBorderStyleValues.Hair;
                rangeTableau.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
            }

            // ── Orientation paysage pour l'impression ────────────────────────
            f.PageSetup.PageOrientation = XLPageOrientation.Landscape;
            f.PageSetup.FitToPages(1, 0);
        }
    }
}
