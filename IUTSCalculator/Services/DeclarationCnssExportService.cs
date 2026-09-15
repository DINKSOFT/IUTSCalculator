using System;
using System.Globalization;
using System.Linq;
using ClosedXML.Excel;
using IUTSCalculator.Models;

namespace IUTSCalculator.Services
{
    /// <summary>
    /// Export du bordereau CNSS en classeur Excel (module DEC-04).
    ///
    /// Deux feuilles, parce qu'elles répondent à deux questions différentes :
    ///
    ///   • « Bordereau » — l'en-tête employeur, l'assiette, la ventilation par branche et
    ///     le total à verser. C'est la page qu'on recopie sur le formulaire de la Caisse.
    ///
    ///   • « État nominatif » — une ligne par salarié, avec son matricule et son assiette.
    ///     C'est la pièce que la Caisse demande en annexe, et celle qui sert à se défendre
    ///     en cas de contrôle.
    ///
    /// Les anomalies ne sont pas passées sous silence dans le fichier : une colonne les
    /// porte, et l'en-tête les récapitule. Un bordereau qui a l'air propre alors que
    /// quatre matricules manquent est un piège.
    /// </summary>
    public class DeclarationCnssExportService
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
        public string Exporter(DeclarationCnss declaration, string cheminFichier)
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

        private static void ConstruireBordereau(XLWorkbook classeur, DeclarationCnss d)
        {
            var f = classeur.Worksheets.Add("Bordereau");
            f.Style.Font.FontName = "Calibri";
            f.Style.Font.FontSize = 11;

            int l = 1;

            // Titre
            f.Cell(l, 1).Value = "DÉCLARATION DE COTISATIONS SOCIALES — CNSS";
            f.Range(l, 1, l, 4).Merge();
            f.Cell(l, 1).Style.Font.Bold = true;
            f.Cell(l, 1).Style.Font.FontSize = 15;
            f.Cell(l, 1).Style.Font.FontColor = Accent;
            l += 1;

            f.Cell(l, 1).Value = string.Format("Déclaration {0} — {1}",
                d.PeriodiciteLibelle.ToLower(CultureInfo.GetCultureInfo("fr-FR")), d.PeriodeLibelle);
            f.Range(l, 1, l, 4).Merge();
            f.Cell(l, 1).Style.Font.FontColor = XLColor.FromArgb(110, 118, 132);
            l += 2;

            // Employeur
            l = SectionTitre(f, l, "EMPLOYEUR");
            l = Ligne(f, l, "Raison sociale", d.RaisonSociale);
            l = Ligne(f, l, "Numéro d'employeur CNSS",
                string.IsNullOrWhiteSpace(d.NumeroEmployeurCnss) ? "NON RENSEIGNÉ" : d.NumeroEmployeurCnss,
                string.IsNullOrWhiteSpace(d.NumeroEmployeurCnss));
            l = Ligne(f, l, "IFU", d.Ifu);
            l = Ligne(f, l, "Adresse", string.Format("{0} {1}", d.Adresse, d.Ville).Trim());
            l = Ligne(f, l, "Téléphone", d.Telephone);
            l += 1;

            // Période
            l = SectionTitre(f, l, "PÉRIODE DÉCLARÉE");
            l = Ligne(f, l, "Période", d.PeriodeLibelle);
            l = Ligne(f, l, "Du", d.DebutPeriode.ToString("dd/MM/yyyy"));
            l = Ligne(f, l, "Au", d.FinPeriode.ToString("dd/MM/yyyy"));
            l = Ligne(f, l, "Périodicité", d.PeriodiciteLibelle);
            l = Ligne(f, l, "Date limite de dépôt",
                d.DateEcheance.ToString("dd/MM/yyyy"), d.EcheanceDepassee);
            l = Ligne(f, l, "Effectif déclaré", d.Effectif.ToString(CultureInfo.CurrentCulture));
            if (d.Bareme != null)
            {
                l = Ligne(f, l, "Barème appliqué", d.Bareme.Designation);
                l = Ligne(f, l, "Plafond mensuel",
                    d.Bareme.PlafondCnssMensuel.ToString("#,##0", CultureInfo.CurrentCulture) + " FCFA");
            }
            l += 1;

            // Assiette
            l = SectionTitre(f, l, "ASSIETTE");
            l = LigneMontant(f, l, "Salaires bruts de la période", d.TotalSalaireBrut);
            l = LigneMontant(f, l, "Assiette cotisable (bruts plafonnés)", d.TotalAssiette, true);
            if (d.NombreLignesPlafonnees > 0)
            {
                l = Ligne(f, l, "dont plafonnées",
                    string.Format("{0} ligne(s) ramenée(s) au plafond", d.NombreLignesPlafonnees));
            }
            l += 1;

            // Ventilation
            l = SectionTitre(f, l, "COTISATIONS PAR BRANCHE");

            f.Cell(l, 1).Value = "Branche";
            f.Cell(l, 2).Value = "Taux";
            f.Cell(l, 3).Value = "Montant";
            f.Range(l, 1, l, 3).Style.Font.Bold = true;
            f.Range(l, 1, l, 3).Style.Fill.BackgroundColor = Trame;
            f.Range(l, 1, l, 3).Style.Border.BottomBorder = XLBorderStyleValues.Thin;
            l += 1;

            if (d.Bareme != null)
            {
                l = LigneBranche(f, l, "Prestations familiales",
                    d.Bareme.TauxCnssPrestationsFamiliales, d.PrestationsFamiliales);
                l = LigneBranche(f, l, "Risques professionnels",
                    d.Bareme.TauxCnssRisquesProfessionnels, d.RisquesProfessionnels);
                l = LigneBranche(f, l, "Pensions — part patronale",
                    d.Bareme.TauxCnssPensionsPatronal, d.PensionsPatronal);
                l = LigneBranche(f, l, "Pensions — part salariale",
                    d.Bareme.TauxCnssSalarie, d.PensionsSalarial);

                f.Cell(l, 1).Value = "TOTAL À VERSER";
                f.Cell(l, 2).Value = d.Bareme.TauxCnssGlobal;
                f.Cell(l, 2).Style.NumberFormat.Format = FormatTaux;
                f.Cell(l, 3).Value = d.TotalCotisationsAttendu;
                f.Cell(l, 3).Style.NumberFormat.Format = FormatMontant;
                f.Range(l, 1, l, 3).Style.Font.Bold = true;
                f.Range(l, 1, l, 3).Style.Font.FontColor = Accent;
                f.Range(l, 1, l, 3).Style.Border.TopBorder = XLBorderStyleValues.Thin;
                l += 2;
            }

            // Rapprochement avec la paie
            l = SectionTitre(f, l, "RAPPROCHEMENT AVEC LA PAIE");
            l = LigneMontant(f, l, "Retenu sur les bulletins — part salariale", d.TotalCotisationSalariale);
            l = LigneMontant(f, l, "Calculé sur les bulletins — part patronale", d.TotalCotisationPatronale);
            l = LigneMontant(f, l, "Total des bulletins", d.TotalCotisations);
            l = LigneMontant(f, l, "Total attendu d'après le barème", d.TotalCotisationsAttendu);

            f.Cell(l, 1).Value = "Écart";
            f.Cell(l, 3).Value = d.EcartGlobal;
            f.Cell(l, 3).Style.NumberFormat.Format = FormatMontant;
            f.Range(l, 1, l, 3).Style.Font.Bold = true;
            if (d.EcartGlobalSignificatif)
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

        private static void ConstruireEtatNominatif(XLWorkbook classeur, DeclarationCnss d)
        {
            var f = classeur.Worksheets.Add("État nominatif");
            f.Style.Font.FontName = "Calibri";
            f.Style.Font.FontSize = 10;

            f.Cell(1, 1).Value = string.Format("ÉTAT NOMINATIF DES COTISATIONS — {0}", d.PeriodeLibelle);
            f.Range(1, 1, 1, 9).Merge();
            f.Cell(1, 1).Style.Font.Bold = true;
            f.Cell(1, 1).Style.Font.FontSize = 13;
            f.Cell(1, 1).Style.Font.FontColor = Accent;

            f.Cell(2, 1).Value = string.Format("{0} — employeur CNSS {1}",
                d.RaisonSociale,
                string.IsNullOrWhiteSpace(d.NumeroEmployeurCnss) ? "non renseigné" : d.NumeroEmployeurCnss);
            f.Range(2, 1, 2, 9).Merge();
            f.Cell(2, 1).Style.Font.FontColor = XLColor.FromArgb(110, 118, 132);

            int entete = 4;
            string[] colonnes =
            {
                "N°", "Matricule CNSS", "Nom et prénoms", "Salaire brut", "Assiette cotisable",
                "Part salariale", "Part patronale", "Total", "Observation"
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
                f.Cell(l, 2).Value = ligne.MatriculeManquant ? "—" : ligne.NumeroCnss;
                f.Cell(l, 3).Value = ligne.NomComplet;
                f.Cell(l, 4).Value = ligne.SalaireBrut;
                f.Cell(l, 5).Value = ligne.AssiettePlafonnee;
                f.Cell(l, 6).Value = ligne.CotisationSalariale;
                f.Cell(l, 7).Value = ligne.CotisationPatronale;
                f.Cell(l, 8).Value = ligne.CotisationTotale;
                f.Cell(l, 9).Value = ligne.Anomalie;

                f.Range(l, 4, l, 8).Style.NumberFormat.Format = FormatMontant;
                f.Cell(l, 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

                if (ligne.AUneAnomalie)
                {
                    f.Cell(l, 9).Style.Font.FontColor = Alerte;
                    f.Cell(l, 2).Style.Font.FontColor = ligne.MatriculeManquant ? Alerte : Encre;
                }

                if (numero % 2 == 0)
                    f.Range(l, 1, l, 9).Style.Fill.BackgroundColor = Trame;

                l += 1;
                numero += 1;
            }

            // Totaux
            f.Cell(l, 3).Value = string.Format("TOTAL — {0} ligne(s)", d.Lignes.Count);
            f.Cell(l, 4).Value = d.TotalSalaireBrut;
            f.Cell(l, 5).Value = d.TotalAssiette;
            f.Cell(l, 6).Value = d.TotalCotisationSalariale;
            f.Cell(l, 7).Value = d.TotalCotisationPatronale;
            f.Cell(l, 8).Value = d.TotalCotisations;
            f.Range(l, 4, l, 8).Style.NumberFormat.Format = FormatMontant;
            f.Range(l, 1, l, 9).Style.Font.Bold = true;
            f.Range(l, 1, l, 9).Style.Border.TopBorder = XLBorderStyleValues.Medium;
            f.Range(l, 1, l, 9).Style.Font.FontColor = Accent;

            f.Column(1).Width = 6;
            f.Column(2).Width = 18;
            f.Column(3).Width = 32;
            for (int c = 4; c <= 8; c++) f.Column(c).Width = 15;
            f.Column(9).Width = 38;

            f.SheetView.FreezeRows(entete);
            f.Range(entete, 1, l, 9).Style.Border.InsideBorder = XLBorderStyleValues.Hair;
            f.Range(entete, 1, l, 9).Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
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

        private static int LigneBranche(IXLWorksheet f, int l, string branche, decimal taux, decimal montant)
        {
            f.Cell(l, 1).Value = branche;
            f.Cell(l, 2).Value = taux;
            f.Cell(l, 2).Style.NumberFormat.Format = FormatTaux;
            f.Cell(l, 3).Value = montant;
            f.Cell(l, 3).Style.NumberFormat.Format = FormatMontant;
            return l + 1;
        }
    }
}
