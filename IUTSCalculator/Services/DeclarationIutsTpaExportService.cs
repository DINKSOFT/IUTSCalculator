using System;
using System.Globalization;
using System.Linq;
using ClosedXML.Excel;
using IUTSCalculator.Models;

namespace IUTSCalculator.Services
{
    /// <summary>
    /// Export de la déclaration IUTS/TPA en classeur Excel (module DEC-03).
    ///
    /// Deux feuilles, sur le même principe que la déclaration CNSS (DEC-04) :
    ///
    ///   • « Bordereau » — l'en-tête employeur, l'IUTS retenu, la TPA et le total à
    ///     verser. C'est la page qu'on recopie sur l'imprimé de la DGI.
    ///
    ///   • « État nominatif » — une ligne par salarié avec l'IUTS qui lui a été retenu.
    ///     Identifiée par le matricule interne, pas par un identifiant fiscal personnel :
    ///     au Burkina Faso un salarié n'a pas de numéro IFU propre, contrairement à
    ///     l'entreprise elle-même (vérifié sur le bordereau). La TPA n'y figure pas en
    ///     colonne : c'est une charge de l'employeur, pas une retenue individuelle — le
    ///     client a choisi de ne la montrer qu'en un seul total sur le bordereau.
    /// </summary>
    public class DeclarationIutsTpaExportService
    {
        private static readonly XLColor Encre = XLColor.FromArgb(31, 41, 55);
        private static readonly XLColor Accent = XLColor.FromArgb(30, 64, 120);
        private static readonly XLColor Trame = XLColor.FromArgb(243, 245, 248);
        private static readonly XLColor Alerte = XLColor.FromArgb(180, 45, 45);

        private const string FormatMontant = "#,##0";
        private const string FormatTaux = "0.00%";

        /// <summary>
        /// Écrit le classeur à l'emplacement demandé et renvoie ce chemin.
        /// </summary>
        public string Exporter(DeclarationIutsTpa declaration, string cheminFichier)
        {
            if (declaration == null) throw new ArgumentNullException("declaration");
            if (string.IsNullOrWhiteSpace(cheminFichier))
                throw new ArgumentException("Chemin de fichier manquant.", "cheminFichier");

            using (var classeur = new XLWorkbook())
            {
                ConstruireBordereau(classeur, declaration);
                ConstruireEtatNominatif(classeur, declaration);
                classeur.SaveAs(cheminFichier);
            }

            return cheminFichier;
        }

        // ── Feuille 1 : le bordereau ─────────────────────────────────────────

        private static void ConstruireBordereau(XLWorkbook classeur, DeclarationIutsTpa d)
        {
            var f = classeur.Worksheets.Add("Bordereau");
            f.Style.Font.FontName = "Calibri";
            f.Style.Font.FontSize = 11;

            int l = 1;

            // Titre
            f.Cell(l, 1).Value = "DÉCLARATION IUTS / TAXE PATRONALE ET D'APPRENTISSAGE";
            f.Range(l, 1, l, 4).Merge();
            f.Cell(l, 1).Style.Font.Bold = true;
            f.Cell(l, 1).Style.Font.FontSize = 15;
            f.Cell(l, 1).Style.Font.FontColor = Accent;
            l += 1;

            f.Cell(l, 1).Value = string.Format("Déclaration mensuelle — {0}", d.PeriodeLibelle);
            f.Range(l, 1, l, 4).Merge();
            f.Cell(l, 1).Style.Font.FontColor = XLColor.FromArgb(110, 118, 132);
            l += 2;

            // Employeur
            l = SectionTitre(f, l, "EMPLOYEUR");
            l = Ligne(f, l, "Raison sociale", d.RaisonSociale);
            l = Ligne(f, l, "IFU",
                string.IsNullOrWhiteSpace(d.Ifu) ? "NON RENSEIGNÉ" : d.Ifu,
                string.IsNullOrWhiteSpace(d.Ifu));
            l = Ligne(f, l, "Régime fiscal", d.RegimeFiscal);
            l = Ligne(f, l, "Adresse", string.Format("{0} {1}", d.Adresse, d.Ville).Trim());
            l = Ligne(f, l, "Téléphone", d.Telephone);
            l += 1;

            // Période
            l = SectionTitre(f, l, "PÉRIODE DÉCLARÉE");
            l = Ligne(f, l, "Période", d.PeriodeLibelle);
            l = Ligne(f, l, "Du", d.DebutPeriode.ToString("dd/MM/yyyy"));
            l = Ligne(f, l, "Au", d.FinPeriode.ToString("dd/MM/yyyy"));
            l = Ligne(f, l, "Date limite de dépôt et de versement",
                d.DateEcheance.ToString("dd/MM/yyyy"), d.EcheanceDepassee);
            l = Ligne(f, l, "Effectif déclaré", d.Effectif.ToString(CultureInfo.CurrentCulture));
            if (d.Bareme != null)
                l = Ligne(f, l, "Barème appliqué", d.Bareme.Designation);
            l += 1;

            // IUTS
            l = SectionTitre(f, l, "IMPÔT UNIQUE SUR LES TRAITEMENTS ET SALAIRES (IUTS)");
            l = LigneMontant(f, l, "Salaires bruts de la période", d.TotalSalaireBrut);
            l = LigneMontant(f, l, "Base imposable", d.TotalBaseImposable);
            l = LigneMontant(f, l, "IUTS retenu — à reverser", d.TotalIUTSRetenu, true);
            l += 1;

            // TPA
            l = SectionTitre(f, l, "TAXE PATRONALE ET D'APPRENTISSAGE (TPA)");
            l = LigneMontant(f, l, "Assiette TPA", d.TotalAssietteTPA);
            if (d.Bareme != null)
            {
                f.Cell(l, 1).Value = "Taux appliqué";
                f.Cell(l, 1).Style.Font.FontColor = XLColor.FromArgb(90, 98, 112);
                f.Cell(l, 3).Value = d.Bareme.TauxTaxePatronaleApprentissage;
                f.Cell(l, 3).Style.NumberFormat.Format = FormatTaux;
                f.Cell(l, 3).Style.Font.Bold = true;
                l += 1;
            }
            l = LigneMontant(f, l, "TPA retenue sur les bulletins — à verser", d.TotalTaxePatronale, true);

            if (d.Bareme != null)
            {
                l = LigneMontant(f, l, "TPA attendue d'après le barème", d.TotalTaxePatronaleAttendue);

                f.Cell(l, 1).Value = "Écart";
                f.Cell(l, 3).Value = d.EcartTPA;
                f.Cell(l, 3).Style.NumberFormat.Format = FormatMontant;
                f.Range(l, 1, l, 3).Style.Font.Bold = true;
                if (d.EcartTPASignificatif)
                {
                    f.Range(l, 1, l, 3).Style.Font.FontColor = Alerte;
                    l += 1;
                    f.Cell(l, 1).Value =
                        "Un écart non nul signifie que les bulletins et le barème de la période ne " +
                        "concordent pas : vérifiez avant d'envoyer.";
                    f.Range(l, 1, l, 4).Merge();
                    f.Cell(l, 1).Style.Font.FontColor = Alerte;
                    f.Cell(l, 1).Style.Font.FontSize = 10;
                }
                l += 1;
            }
            l += 1;

            // Total à verser
            l = SectionTitre(f, l, "TOTAL À VERSER À LA DGI");
            l = LigneMontant(f, l, "IUTS retenu", d.TotalIUTSRetenu);
            l = LigneMontant(f, l, "TPA retenue", d.TotalTaxePatronale);

            f.Cell(l, 1).Value = "TOTAL À VERSER";
            f.Cell(l, 3).Value = d.TotalAVerser;
            f.Cell(l, 3).Style.NumberFormat.Format = FormatMontant;
            f.Range(l, 1, l, 3).Style.Font.Bold = true;
            f.Range(l, 1, l, 3).Style.Font.FontColor = Accent;
            f.Range(l, 1, l, 3).Style.Border.TopBorder = XLBorderStyleValues.Thin;
            l += 2;

            // Anomalies
            var anomalies = d.Anomalies();
            if (anomalies.Count > 0)
            {
                l = SectionTitre(f, l, "À VÉRIFIER AVANT DÉPÔT");
                foreach (string anomalie in anomalies)
                {
                    f.Cell(l, 1).Value = "• " + anomalie;
                    f.Range(l, 1, l, 4).Merge();
                    f.Cell(l, 1).Style.Alignment.WrapText = true;
                    f.Cell(l, 1).Style.Font.FontColor = Alerte;
                    f.Row(l).Height = 28;
                    l += 1;
                }
                l += 1;
            }

            f.Cell(l, 1).Value = string.Format(
                "Document produit le {0:dd/MM/yyyy à HH:mm} par Sigma Paie & RH.", d.DateGeneration);
            f.Range(l, 1, l, 4).Merge();
            f.Cell(l, 1).Style.Font.FontSize = 9;
            f.Cell(l, 1).Style.Font.FontColor = XLColor.FromArgb(140, 146, 158);

            f.Column(1).Width = 42;
            f.Column(2).Width = 14;
            f.Column(3).Width = 18;
            f.Column(4).Width = 22;
            f.SheetView.FreezeRows(2);
        }

        // ── Feuille 2 : l'état nominatif ─────────────────────────────────────

        private static void ConstruireEtatNominatif(XLWorkbook classeur, DeclarationIutsTpa d)
        {
            var f = classeur.Worksheets.Add("État nominatif");
            f.Style.Font.FontName = "Calibri";
            f.Style.Font.FontSize = 10;

            f.Cell(1, 1).Value = string.Format("ÉTAT NOMINATIF DE L'IUTS RETENU — {0}", d.PeriodeLibelle);
            f.Range(1, 1, 1, 5).Merge();
            f.Cell(1, 1).Style.Font.Bold = true;
            f.Cell(1, 1).Style.Font.FontSize = 13;
            f.Cell(1, 1).Style.Font.FontColor = Accent;

            f.Cell(2, 1).Value = string.Format("{0} — IFU {1}",
                d.RaisonSociale,
                string.IsNullOrWhiteSpace(d.Ifu) ? "non renseigné" : d.Ifu);
            f.Range(2, 1, 2, 5).Merge();
            f.Cell(2, 1).Style.Font.FontColor = XLColor.FromArgb(110, 118, 132);

            int entete = 4;
            // Identification par matricule interne, pas par un identifiant fiscal
            // personnel : au Burkina Faso un salarié n'a pas de numéro IFU propre.
            string[] colonnes =
            {
                "N°", "Nom et prénoms", "Salaire brut", "Base imposable", "IUTS retenu"
            };

            for (int c = 0; c < colonnes.Length; c++)
            {
                var cellule = f.Cell(entete, c + 1);
                cellule.Value = colonnes[c];
                cellule.Style.Font.Bold = true;
                cellule.Style.Font.FontColor = XLColor.White;
                cellule.Style.Fill.BackgroundColor = Accent;
                cellule.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
                cellule.Style.Alignment.WrapText = true;
            }
            f.Row(entete).Height = 30;

            int l = entete + 1;
            int numero = 1;

            foreach (var ligne in d.Lignes.OrderBy(x => x.Nom).ThenBy(x => x.Prenom))
            {
                f.Cell(l, 1).Value = numero;
                f.Cell(l, 2).Value = ligne.NomComplet;
                f.Cell(l, 3).Value = ligne.SalaireBrut;
                f.Cell(l, 4).Value = ligne.BaseImposable;
                f.Cell(l, 5).Value = ligne.IUTSRetenu;

                f.Range(l, 3, l, 5).Style.NumberFormat.Format = FormatMontant;
                f.Cell(l, 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

                if (numero % 2 == 0)
                    f.Range(l, 1, l, 5).Style.Fill.BackgroundColor = Trame;

                l += 1;
                numero += 1;
            }

            // Totaux
            f.Cell(l, 2).Value = string.Format("TOTAL — {0} ligne(s)", d.Lignes.Count);
            f.Cell(l, 3).Value = d.TotalSalaireBrut;
            f.Cell(l, 4).Value = d.TotalBaseImposable;
            f.Cell(l, 5).Value = d.TotalIUTSRetenu;
            f.Range(l, 3, l, 5).Style.NumberFormat.Format = FormatMontant;
            f.Range(l, 1, l, 5).Style.Font.Bold = true;
            f.Range(l, 1, l, 5).Style.Border.TopBorder = XLBorderStyleValues.Medium;
            f.Range(l, 1, l, 5).Style.Font.FontColor = Accent;

            f.Column(1).Width = 6;
            f.Column(2).Width = 32;
            for (int c = 3; c <= 5; c++) f.Column(c).Width = 15;

            f.SheetView.FreezeRows(entete);
            f.Range(entete, 1, l, 5).Style.Border.InsideBorder = XLBorderStyleValues.Hair;
            f.Range(entete, 1, l, 5).Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
        }

        // ── Petites aides de mise en page ────────────────────────────────────

        private static int SectionTitre(IXLWorksheet f, int l, string titre)
        {
            f.Cell(l, 1).Value = titre;
            f.Range(l, 1, l, 4).Merge();
            f.Cell(l, 1).Style.Font.Bold = true;
            f.Cell(l, 1).Style.Font.FontSize = 11;
            f.Cell(l, 1).Style.Font.FontColor = Accent;
            f.Cell(l, 1).Style.Border.BottomBorder = XLBorderStyleValues.Thin;
            f.Cell(l, 1).Style.Border.BottomBorderColor = Accent;
            return l + 1;
        }

        private static int Ligne(IXLWorksheet f, int l, string libelle, string valeur, bool alerte = false)
        {
            f.Cell(l, 1).Value = libelle;
            f.Cell(l, 1).Style.Font.FontColor = XLColor.FromArgb(90, 98, 112);
            f.Cell(l, 3).Value = valeur ?? "";
            f.Range(l, 3, l, 4).Merge();
            f.Cell(l, 3).Style.Font.Bold = true;
            if (alerte) f.Cell(l, 3).Style.Font.FontColor = Alerte;
            return l + 1;
        }

        private static int LigneMontant(IXLWorksheet f, int l, string libelle, decimal montant, bool souligne = false)
        {
            f.Cell(l, 1).Value = libelle;
            f.Cell(l, 1).Style.Font.FontColor = XLColor.FromArgb(90, 98, 112);
            f.Cell(l, 3).Value = montant;
            f.Cell(l, 3).Style.NumberFormat.Format = FormatMontant;
            f.Cell(l, 3).Style.Font.Bold = true;
            if (souligne)
            {
                f.Cell(l, 1).Style.Font.FontColor = Encre;
                f.Cell(l, 3).Style.Font.FontColor = Accent;
            }
            return l + 1;
        }
    }
}
