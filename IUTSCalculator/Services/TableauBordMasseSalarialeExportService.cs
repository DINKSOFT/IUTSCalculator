using System;
using ClosedXML.Excel;
using IUTSCalculator.Models;

namespace IUTSCalculator.Services
{
    /// <summary>
    /// Export du tableau de bord masse salariale en classeur Excel (module PIL-01).
    ///
    /// Trois feuilles — évolution mensuelle, ventilation par catégorie, ventilation par
    /// service — plutôt qu'une seule, car les trois tableaux n'ont ni les mêmes colonnes
    /// ni la même unité de ligne (un mois, une catégorie, un service). Mêmes couleurs que
    /// les autres états du projet (DEC-05, ETA-01), pour une identité visuelle cohérente.
    /// </summary>
    public class TableauBordMasseSalarialeExportService
    {
        private static readonly XLColor Encre = XLColor.FromArgb(31, 41, 55);
        private static readonly XLColor Accent = XLColor.FromArgb(30, 64, 120);
        private static readonly XLColor Trame = XLColor.FromArgb(243, 245, 248);
        private static readonly XLColor Alerte = XLColor.FromArgb(180, 45, 45);
        private static readonly XLColor Discret = XLColor.FromArgb(110, 118, 132);

        private const string FormatMontant = "#,##0";

        public string Exporter(TableauBordMasseSalariale tableau, string cheminFichier)
        {
            if (tableau == null) throw new ArgumentNullException("tableau");
            if (string.IsNullOrWhiteSpace(cheminFichier))
                throw new ArgumentException("Chemin de fichier manquant.", "cheminFichier");

            using (var classeur = new XLWorkbook())
            {
                ConstruireFeuilleEvolution(classeur, tableau);
                ConstruireFeuilleVentilation(classeur, tableau, "Par catégorie", "Catégorie", tableau.ParCategorie);
                ConstruireFeuilleVentilation(classeur, tableau, "Par service", "Service", tableau.ParService);
                classeur.SaveAs(cheminFichier);
            }

            return cheminFichier;
        }

        private void EcrireEnTete(IXLWorksheet f, TableauBordMasseSalariale d, string sousTitre)
        {
            f.Style.Font.FontName = "Calibri";
            f.Style.Font.FontSize = 10;

            f.Cell(1, 1).Value = "TABLEAU DE BORD MASSE SALARIALE";
            f.Cell(1, 1).Style.Font.Bold = true;
            f.Cell(1, 1).Style.Font.FontSize = 15;
            f.Cell(1, 1).Style.Font.FontColor = Accent;
            f.Range(1, 1, 1, 5).Merge();

            if (!d.EstComplet)
            {
                f.Cell(1, 6).Value = "ANNÉE PARTIELLE";
                f.Cell(1, 6).Style.Font.Bold = true;
                f.Cell(1, 6).Style.Font.FontSize = 12;
                f.Cell(1, 6).Style.Font.FontColor = Alerte;
            }

            f.Cell(2, 1).Value = string.Format("{0} — {1} — {2}", d.RaisonSociale ?? "", d.PeriodeLibelle, sousTitre);
            f.Cell(2, 1).Style.Font.FontColor = Discret;
            f.Range(2, 1, 2, 5).Merge();

            f.Cell(3, 1).Value = string.Format(
                "Mois clôturés inclus ({0}/12) : {1}", d.MoisClotures.Count, d.MoisClosLibelle);
            f.Cell(3, 1).Style.Font.FontColor = Discret;
            f.Cell(3, 1).Style.Font.FontSize = 9;
            f.Range(3, 1, 3, 5).Merge();

            int ligne = 4;
            if (!d.EstComplet)
            {
                f.Cell(ligne, 1).Value = string.Format("Mois non clôturés, absents : {0}", d.MoisManquantsLibelle);
                f.Cell(ligne, 1).Style.Font.FontColor = Alerte;
                f.Cell(ligne, 1).Style.Font.FontSize = 9;
                f.Range(ligne, 1, ligne, 5).Merge();
                ligne++;
            }

            f.Cell(ligne, 1).Value = string.Format("Édité le {0:dd/MM/yyyy à HH:mm}", d.DateGeneration);
            f.Cell(ligne, 1).Style.Font.FontColor = XLColor.FromArgb(160, 165, 175);
            f.Cell(ligne, 1).Style.Font.FontSize = 9;
        }

        private void ConstruireFeuilleEvolution(XLWorkbook classeur, TableauBordMasseSalariale d)
        {
            var f = classeur.Worksheets.Add("Évolution mensuelle");
            EcrireEnTete(f, d, "Évolution mensuelle");

            int l = 7;
            int entete = l;

            f.Cell(l, 1).Value = "Mois";
            f.Cell(l, 2).Value = "Nb bulletins";
            f.Cell(l, 3).Value = "Brut";
            f.Cell(l, 4).Value = "Net à payer";
            f.Cell(l, 5).Value = "Coût employeur";

            var rangeEntete = f.Range(l, 1, l, 5);
            rangeEntete.Style.Font.Bold = true;
            rangeEntete.Style.Font.FontColor = XLColor.White;
            rangeEntete.Style.Fill.BackgroundColor = Accent;
            rangeEntete.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            l++;

            int numero = 0;
            foreach (var p in d.Evolution)
            {
                numero++;
                f.Cell(l, 1).Value = p.Designation;
                f.Cell(l, 2).Value = p.NombreBulletins;
                f.Cell(l, 3).Value = p.Brut;
                f.Cell(l, 4).Value = p.Net;
                f.Cell(l, 5).Value = p.CoutEmployeur;

                if (numero % 2 == 0)
                    f.Range(l, 1, l, 5).Style.Fill.BackgroundColor = Trame;
                l++;
            }

            int ligneTotaux = l;
            f.Cell(l, 1).Value = "TOTAL";
            f.Cell(l, 2).Value = d.TotalBulletins;
            f.Cell(l, 3).Value = d.TotalBrut;
            f.Cell(l, 4).Value = d.Evolution.Count == 0 ? 0m : d.TotalNet;
            f.Cell(l, 5).Value = d.TotalCoutEmployeur;

            var rangeTotaux = f.Range(l, 1, l, 5);
            rangeTotaux.Style.Font.Bold = true;
            rangeTotaux.Style.Font.FontColor = Accent;
            rangeTotaux.Style.Border.TopBorder = XLBorderStyleValues.Medium;

            f.Column(3).Style.NumberFormat.Format = FormatMontant;
            f.Column(4).Style.NumberFormat.Format = FormatMontant;
            f.Column(5).Style.NumberFormat.Format = FormatMontant;

            f.Column(1).Width = 14;
            f.Column(2).Width = 12;
            f.Column(3).Width = 18;
            f.Column(4).Width = 18;
            f.Column(5).Width = 18;

            f.SheetView.FreezeRows(entete);

            if (d.Evolution.Count > 0)
            {
                var rangeTableau = f.Range(entete, 1, ligneTotaux, 5);
                rangeTableau.Style.Border.InsideBorder = XLBorderStyleValues.Hair;
                rangeTableau.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
            }
        }

        private void ConstruireFeuilleVentilation(
            XLWorkbook classeur, TableauBordMasseSalariale d,
            string nomFeuille, string libelleColonneCle,
            System.Collections.Generic.List<VentilationMasseSalariale> lignes)
        {
            var f = classeur.Worksheets.Add(nomFeuille);
            EcrireEnTete(f, d, nomFeuille);

            int l = 7;
            int entete = l;

            f.Cell(l, 1).Value = libelleColonneCle;
            f.Cell(l, 2).Value = "Nb bulletins";
            f.Cell(l, 3).Value = "Brut";
            f.Cell(l, 4).Value = "Net à payer";
            f.Cell(l, 5).Value = "Coût employeur";
            f.Cell(l, 6).Value = "Part (%)";

            var rangeEntete = f.Range(l, 1, l, 6);
            rangeEntete.Style.Font.Bold = true;
            rangeEntete.Style.Font.FontColor = XLColor.White;
            rangeEntete.Style.Fill.BackgroundColor = Accent;
            rangeEntete.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            l++;

            int numero = 0;
            foreach (var v in lignes)
            {
                numero++;
                f.Cell(l, 1).Value = v.Cle;
                f.Cell(l, 2).Value = v.NombreBulletins;
                f.Cell(l, 3).Value = v.Brut;
                f.Cell(l, 4).Value = v.Net;
                f.Cell(l, 5).Value = v.CoutEmployeur;
                f.Cell(l, 6).Value = Math.Round(v.PartPourcentage, 1);

                if (string.Equals(v.Cle, "Non renseigné", StringComparison.OrdinalIgnoreCase))
                    f.Cell(l, 1).Style.Font.FontColor = Alerte;

                if (numero % 2 == 0)
                    f.Range(l, 1, l, 6).Style.Fill.BackgroundColor = Trame;
                l++;
            }

            int ligneTotaux = l;
            f.Cell(l, 1).Value = "TOTAL";
            f.Cell(l, 2).Value = d.TotalBulletins;
            decimal totalBrut = 0m, totalNet = 0m, totalCoutEmployeur = 0m;
            foreach (var v in lignes)
            {
                totalBrut += v.Brut;
                totalNet += v.Net;
                totalCoutEmployeur += v.CoutEmployeur;
            }
            f.Cell(l, 3).Value = totalBrut;
            f.Cell(l, 4).Value = totalNet;
            f.Cell(l, 5).Value = totalCoutEmployeur;
            f.Cell(l, 6).Value = lignes.Count == 0 ? 0m : 100m;

            var rangeTotaux = f.Range(l, 1, l, 6);
            rangeTotaux.Style.Font.Bold = true;
            rangeTotaux.Style.Font.FontColor = Accent;
            rangeTotaux.Style.Border.TopBorder = XLBorderStyleValues.Medium;

            f.Column(3).Style.NumberFormat.Format = FormatMontant;
            f.Column(4).Style.NumberFormat.Format = FormatMontant;
            f.Column(5).Style.NumberFormat.Format = FormatMontant;
            f.Column(6).Style.NumberFormat.Format = "0.0";

            f.Column(1).Width = 22;
            f.Column(2).Width = 12;
            f.Column(3).Width = 18;
            f.Column(4).Width = 18;
            f.Column(5).Width = 18;
            f.Column(6).Width = 11;

            f.SheetView.FreezeRows(entete);

            if (lignes.Count > 0)
            {
                var rangeTableau = f.Range(entete, 1, ligneTotaux, 6);
                rangeTableau.Style.Border.InsideBorder = XLBorderStyleValues.Hair;
                rangeTableau.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
            }
        }
    }
}
