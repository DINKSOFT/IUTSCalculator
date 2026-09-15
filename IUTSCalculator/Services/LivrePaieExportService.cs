using System;
using System.Globalization;
using System.Linq;
using ClosedXML.Excel;
using IUTSCalculator.Models;

namespace IUTSCalculator.Services
{
    /// <summary>
    /// Export du livre de paie en classeur Excel (module ETA-01).
    ///
    /// Feuille unique « Livre de paie » : en-tête employeur, tableau croisé
    /// salariés × rubriques, totaux de contrôle. Mêmes couleurs que les
    /// bordereaux DEC-03 et DEC-04, pour que l'ensemble des états garde
    /// une identité visuelle cohérente.
    /// </summary>
    public class LivrePaieExportService
    {
        private static readonly XLColor Encre = XLColor.FromArgb(31, 41, 55);
        private static readonly XLColor Accent = XLColor.FromArgb(30, 64, 120);
        private static readonly XLColor Trame = XLColor.FromArgb(243, 245, 248);
        private static readonly XLColor Alerte = XLColor.FromArgb(180, 45, 45);

        private const string FormatMontant = "#,##0";

        /// <summary>
        /// Écrit le classeur à l'emplacement demandé et renvoie ce chemin.
        /// </summary>
        public string Exporter(LivrePaie livre, string cheminFichier)
        {
            if (livre == null) throw new ArgumentNullException("livre");
            if (string.IsNullOrWhiteSpace(cheminFichier))
                throw new ArgumentException("Chemin de fichier manquant.", "cheminFichier");

            using (var classeur = new XLWorkbook())
            {
                ConstruireFeuille(classeur, livre);
                classeur.SaveAs(cheminFichier);
            }

            return cheminFichier;
        }

        private void ConstruireFeuille(XLWorkbook classeur, LivrePaie d)
        {
            var f = classeur.Worksheets.Add("Livre de paie");
            f.Style.Font.FontName = "Calibri";
            f.Style.Font.FontSize = 10;

            int l = 1;

            // ── Titre ────────────────────────────────────────────────────────
            f.Cell(l, 1).Value = "LIVRE DE PAIE";
            f.Cell(l, 1).Style.Font.Bold = true;
            f.Cell(l, 1).Style.Font.FontSize = 15;
            f.Cell(l, 1).Style.Font.FontColor = Accent;
            f.Range(l, 1, l, 6).Merge();

            if (d.EstBrouillon)
            {
                f.Cell(l, 7).Value = "BROUILLON";
                f.Cell(l, 7).Style.Font.Bold = true;
                f.Cell(l, 7).Style.Font.FontSize = 14;
                f.Cell(l, 7).Style.Font.FontColor = Alerte;
            }

            l++;

            f.Cell(l, 1).Value = string.Format("{0} — {1}",
                d.RaisonSociale ?? "", d.PeriodeLibelle);
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

            f.Cell(l, 1).Value = string.Format("Édité le {0:dd/MM/yyyy à HH:mm}", d.DateGeneration);
            f.Cell(l, 1).Style.Font.FontColor = XLColor.FromArgb(160, 165, 175);
            f.Cell(l, 1).Style.Font.FontSize = 9;
            f.Range(l, 1, l, 4).Merge();
            l += 2;

            // ── Calcul des colonnes ──────────────────────────────────────────
            // Colonnes fixes avant les rubriques dynamiques
            int col = 1;
            int colNo = col++;          // N°
            int colMatricule = col++;   // Matricule
            int colNomPrenom = col++;    // Nom et prénom
            int colCategorie = col++;    // Catégorie
            int colEmploi = col++;       // Emploi
            int colDateEntree = col++;   // Date d'entrée
            int colSalBase = col++;      // Salaire de base
            int colIndLog = col++;       // Ind. logement
            int colIndTrans = col++;     // Ind. transport
            int colIndFonc = col++;      // Ind. fonction
            int colIndSuj = col++;       // Ind. sujétion

            // Colonnes dynamiques (rubriques variables)
            int premiereDynamique = col;
            foreach (var cd in d.ColonnesDynamiques)
            {
                col++;
            }
            int derniereDynamique = col - 1;

            // Colonnes de synthèse
            int colRetAbs = col++;       // Ret. absences
            int colBrut = col++;         // Brut
            int colCnssSal = col++;      // CNSS salarié
            int colIuts = col++;         // IUTS
            int colRetSpec = col++;      // Ret. spéciale
            int colRetDiv = col++;       // Ret. diverses
            int colNet = col++;          // Net à payer
            int colCnssPat = col++;      // CNSS patron
            int colTpa = col++;          // TPA
            int colMasse = col++;        // Masse salariale
            int dernierCol = col - 1;

            // ── En-tête du tableau ───────────────────────────────────────────
            int entete = l;

            f.Cell(l, colNo).Value = "N°";
            f.Cell(l, colMatricule).Value = "Matricule";
            f.Cell(l, colNomPrenom).Value = "Nom et prénom";
            f.Cell(l, colCategorie).Value = "Catégorie";
            f.Cell(l, colEmploi).Value = "Emploi";
            f.Cell(l, colDateEntree).Value = "Entrée";
            f.Cell(l, colSalBase).Value = "Sal. de base";
            f.Cell(l, colIndLog).Value = "Ind. logement";
            f.Cell(l, colIndTrans).Value = "Ind. transport";
            f.Cell(l, colIndFonc).Value = "Ind. fonction";
            f.Cell(l, colIndSuj).Value = "Ind. sujétion";

            // En-têtes dynamiques
            int idxDyn = premiereDynamique;
            foreach (var cd in d.ColonnesDynamiques)
            {
                f.Cell(l, idxDyn).Value = cd.Libelle;
                idxDyn++;
            }

            f.Cell(l, colRetAbs).Value = "Ret. absences";
            f.Cell(l, colBrut).Value = "Brut";
            f.Cell(l, colCnssSal).Value = "CNSS salarié";
            f.Cell(l, colIuts).Value = "IUTS";
            f.Cell(l, colRetSpec).Value = "Ret. spéciale";
            f.Cell(l, colRetDiv).Value = "Ret. diverses";
            f.Cell(l, colNet).Value = "Net à payer";
            f.Cell(l, colCnssPat).Value = "CNSS patron";
            f.Cell(l, colTpa).Value = "TPA";
            f.Cell(l, colMasse).Value = "Masse salariale";

            // Style de l'en-tête
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

                if (ligne.DateEntree.HasValue)
                {
                    f.Cell(l, colDateEntree).Value = ligne.DateEntree.Value;
                    f.Cell(l, colDateEntree).Style.DateFormat.Format = "dd/MM/yyyy";
                }

                f.Cell(l, colSalBase).Value = ligne.SalaireDeBase;
                f.Cell(l, colIndLog).Value = ligne.IndemniteLogement;
                f.Cell(l, colIndTrans).Value = ligne.IndemniteTransport;
                f.Cell(l, colIndFonc).Value = ligne.IndemniteFonction;
                f.Cell(l, colIndSuj).Value = ligne.IndemniteSujetion;

                // Colonnes dynamiques
                idxDyn = premiereDynamique;
                foreach (var cd in d.ColonnesDynamiques)
                {
                    decimal montant;
                    if (ligne.MontantsParRubrique.TryGetValue(cd.Code, out montant))
                        f.Cell(l, idxDyn).Value = montant;
                    else
                        f.Cell(l, idxDyn).Value = 0;
                    idxDyn++;
                }

                f.Cell(l, colRetAbs).Value = ligne.RetenueAbsences;
                f.Cell(l, colBrut).Value = ligne.SalaireBrut;
                f.Cell(l, colCnssSal).Value = ligne.CnssSalariale;
                f.Cell(l, colIuts).Value = ligne.Iuts;
                f.Cell(l, colRetSpec).Value = ligne.RetenueSpeciale;
                f.Cell(l, colRetDiv).Value = ligne.RetenuesDiverses;
                f.Cell(l, colNet).Value = ligne.NetAPayer;
                f.Cell(l, colCnssPat).Value = ligne.CnssPatronale;
                f.Cell(l, colTpa).Value = ligne.Tpa;
                f.Cell(l, colMasse).Value = ligne.MasseSalariale;

                // Trame alternée
                if (pair)
                    f.Range(l, 1, l, dernierCol).Style.Fill.BackgroundColor = Trame;

                l++;
            }

            // ── Ligne de totaux ──────────────────────────────────────────────
            int ligneTotaux = l;

            f.Cell(l, colNo).Value = "";
            f.Cell(l, colMatricule).Value = "";
            f.Cell(l, colNomPrenom).Value = string.Format("TOTAUX ({0} salarié(s))", d.Effectif);
            f.Cell(l, colSalBase).Value = d.TotalSalaireBase;
            f.Cell(l, colIndLog).Value = d.TotalIndemniteLogement;
            f.Cell(l, colIndTrans).Value = d.TotalIndemniteTransport;
            f.Cell(l, colIndFonc).Value = d.TotalIndemniteFonction;
            f.Cell(l, colIndSuj).Value = d.TotalIndemniteSujetion;

            // Totaux dynamiques
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

            f.Cell(l, colRetAbs).Value = d.TotalRetenueAbsences;
            f.Cell(l, colBrut).Value = d.TotalBrut;
            f.Cell(l, colCnssSal).Value = d.TotalCnssSalariale;
            f.Cell(l, colIuts).Value = d.TotalIuts;
            f.Cell(l, colRetSpec).Value = d.TotalRetenueSpeciale;
            f.Cell(l, colRetDiv).Value = d.TotalRetenuesDiverses;
            f.Cell(l, colNet).Value = d.TotalNetAPayer;
            f.Cell(l, colCnssPat).Value = d.TotalCnssPatronale;
            f.Cell(l, colTpa).Value = d.TotalTpa;
            f.Cell(l, colMasse).Value = d.TotalMasseSalariale;

            var rangeTotaux = f.Range(l, 1, l, dernierCol);
            rangeTotaux.Style.Font.Bold = true;
            rangeTotaux.Style.Font.FontColor = Accent;
            rangeTotaux.Style.Border.TopBorder = XLBorderStyleValues.Medium;

            // ── Format montants sur toutes les colonnes numériques ────────────
            int premierMontant = colSalBase;
            for (int c = premierMontant; c <= dernierCol; c++)
            {
                f.Column(c).Style.NumberFormat.Format = FormatMontant;
            }

            // ── Largeurs de colonnes ──────────────────────────────────────────
            f.Column(colNo).Width = 5;
            f.Column(colMatricule).Width = 14;
            f.Column(colNomPrenom).Width = 28;
            f.Column(colCategorie).Width = 16;
            f.Column(colEmploi).Width = 18;
            f.Column(colDateEntree).Width = 12;
            for (int c = colSalBase; c <= dernierCol; c++)
                f.Column(c).Width = 14;

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
