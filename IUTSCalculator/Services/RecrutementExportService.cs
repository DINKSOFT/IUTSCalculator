using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using ClosedXML.Excel;
using iTextSharp.text;
using iTextSharp.text.pdf;
using IUTSCalculator.Models;

namespace IUTSCalculator.Services
{
    /// <summary>
    /// Service d'exportation pour le module RH-05 Recrutement & Candidatures.
    /// Génère :
    /// 1. La lettre formelle d'embauche A4 (PDF iTextSharp) conforme aux Articles 38-43 de la Loi n°028-2008/AN.
    /// 2. Le bilan consolidé du recrutement à 4 feuilles (Excel ClosedXML).
    /// </summary>
    public class RecrutementExportService
    {
        #region Constantes & Couleurs

        // Couleurs Excel
        private static readonly XLColor CouleurAccent = XLColor.FromArgb(20, 103, 90);     // #14675A Vert émeraude
        private static readonly XLColor CouleurEncre = XLColor.FromArgb(23, 33, 31);      // #17211F Texte principal
        private static readonly XLColor CouleurDiscret = XLColor.FromArgb(121, 131, 126); // #79837E Gris moyen
        private static readonly XLColor CouleurTrame = XLColor.FromArgb(245, 246, 245);   // #F5F6F5 Fond léger
        private static readonly XLColor CouleurBordure = XLColor.FromArgb(203, 209, 204); // #CBD1CC
        private static readonly XLColor CouleurVert = XLColor.FromArgb(27, 94, 32);       // #1B5E20
        private static readonly XLColor CouleurAmbre = XLColor.FromArgb(230, 81, 0);      // #E65100
        private static readonly XLColor CouleurRouge = XLColor.FromArgb(198, 40, 40);     // #C62828

        // Couleurs & Polices PDF (iTextSharp)
        private static readonly BaseColor PdfPrimaryColor = new BaseColor(23, 33, 31);
        private static readonly BaseColor PdfAccentColor = new BaseColor(20, 103, 90);
        private static readonly BaseColor PdfLightBg = new BaseColor(245, 246, 245);
        private static readonly BaseColor PdfBorderColor = new BaseColor(203, 209, 204);
        private static readonly BaseColor PdfMutedColor = new BaseColor(100, 116, 139);

        private static readonly Font PdfTitleFont = new Font(Font.FontFamily.HELVETICA, 13f, Font.BOLD, PdfAccentColor);
        private static readonly Font PdfSubTitleFont = new Font(Font.FontFamily.HELVETICA, 8.5f, Font.NORMAL, PdfMutedColor);
        private static readonly Font PdfSubjectFont = new Font(Font.FontFamily.HELVETICA, 9.5f, Font.BOLD, PdfPrimaryColor);
        private static readonly Font PdfBodyFont = new Font(Font.FontFamily.HELVETICA, 8.5f, Font.NORMAL, PdfPrimaryColor);
        private static readonly Font PdfBodyBoldFont = new Font(Font.FontFamily.HELVETICA, 8.5f, Font.BOLD, PdfPrimaryColor);
        private static readonly Font PdfLegalFont = new Font(Font.FontFamily.HELVETICA, 7.5f, Font.ITALIC, PdfMutedColor);
        private static readonly Font PdfHeaderCellFont = new Font(Font.FontFamily.HELVETICA, 8f, Font.BOLD, BaseColor.WHITE);
        private static readonly Font PdfCellFont = new Font(Font.FontFamily.HELVETICA, 8f, Font.NORMAL, PdfPrimaryColor);

        #endregion

        #region Lettre d'Embauche Officielle (PDF A4 iTextSharp)

        /// <summary>
        /// Produit une lettre formelle d'offre et d'engagement d'embauche au format A4 portrait,
        /// rigoureusement conforme au Code du travail du Burkina Faso (Loi n° 028-2008/AN, Articles 38-43).
        /// </summary>
        public bool GenererLettreEmbauchePdf(Candidature candidature, string cheminFichier, string typeContrat = null)
        {
            if (candidature == null) throw new ArgumentNullException(nameof(candidature));
            if (string.IsNullOrWhiteSpace(cheminFichier)) throw new ArgumentNullException(nameof(cheminFichier));

            var dossier = DossierSession.Instance.CurrentDossier;
            string raisonSociale = dossier?.RaisonSociale ?? "SIGMA INDUSTRIES & SERVICES SA";
            string ville = string.IsNullOrWhiteSpace(dossier?.Ville) ? "Ouagadougou" : dossier.Ville;
            string ifu = dossier?.IFU ?? "00012345Z";
            string rccm = dossier?.RCCM ?? "BF-OUA-2020-B-1234";
            string tel = dossier?.Telephone ?? "+226 25 30 00 00";
            string adresse = dossier?.Adresse ?? "Secteur 15, Ouagadougou, Burkina Faso";

            // Déterminer la date d'embauche et le salaire
            DateTime datePriseFonction = candidature.DateEmbauche ?? DateTime.Today;
            decimal salaireBrut = candidature.SalaireNegocie ?? (candidature.PretentionSalariale ?? 350000m);
            string dateJour = DateTime.Today.ToString("dd MMMM yyyy", CultureInfo.GetCultureInfo("fr-FR"));
            string dateEffetStr = datePriseFonction.ToString("dd MMMM yyyy", CultureInfo.GetCultureInfo("fr-FR"));

            using (var fs = new FileStream(cheminFichier, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                using (var document = new Document(PageSize.A4, 40f, 40f, 35f, 35f))
                {
                    PdfWriter writer = PdfWriter.GetInstance(document, fs);
                    document.Open();

                    // 1. En-tête Entreprise
                    var tableEntete = new PdfPTable(2) { WidthPercentage = 100f };
                    tableEntete.SetWidths(new float[] { 60f, 40f });

                    var cellGauche = new PdfPCell { Border = Rectangle.NO_BORDER };
                    cellGauche.AddElement(new Paragraph(raisonSociale.ToUpperInvariant(), PdfTitleFont));
                    cellGauche.AddElement(new Paragraph($"{adresse} | Tél: {tel}", PdfSubTitleFont));
                    cellGauche.AddElement(new Paragraph($"IFU: {ifu} | RCCM: {rccm}", PdfSubTitleFont));
                    tableEntete.AddCell(cellGauche);

                    var cellDroite = new PdfPCell { Border = Rectangle.NO_BORDER, HorizontalAlignment = Element.ALIGN_RIGHT };
                    var pVilleDate = new Paragraph($"{ville}, le {dateJour}", PdfBodyFont) { Alignment = Element.ALIGN_RIGHT };
                    var pRef = new Paragraph($"Réf : EMB/{DateTime.Today.Year}/{candidature.Id:D4}", PdfLegalFont) { Alignment = Element.ALIGN_RIGHT };
                    cellDroite.AddElement(pVilleDate);
                    cellDroite.AddElement(pRef);
                    tableEntete.AddCell(cellDroite);

                    document.Add(tableEntete);
                    document.Add(new Paragraph(" ") { SpacingAfter = 5f });

                    // Ligne de séparation émeraude
                    var tableSeparateur = new PdfPTable(1) { WidthPercentage = 100f };
                    var cellSep = new PdfPCell { Border = Rectangle.BOTTOM_BORDER, BorderWidthBottom = 2f, BorderColorBottom = PdfAccentColor, PaddingBottom = 4f };
                    tableSeparateur.AddCell(cellSep);
                    document.Add(tableSeparateur);
                    document.Add(new Paragraph(" ") { SpacingAfter = 10f });

                    // 2. Bloc Destinataire
                    var tableDest = new PdfPTable(2) { WidthPercentage = 100f };
                    tableDest.SetWidths(new float[] { 50f, 50f });

                    var cellVide = new PdfPCell { Border = Rectangle.NO_BORDER };
                    tableDest.AddCell(cellVide);

                    var cellDest = new PdfPCell
                    {
                        Border = Rectangle.BOX,
                        BorderColor = PdfBorderColor,
                        BorderWidth = 1f,
                        BackgroundColor = PdfLightBg,
                        Padding = 8f
                    };
                    cellDest.AddElement(new Paragraph("À l'attention de :", PdfSubTitleFont));
                    cellDest.AddElement(new Paragraph($"M./Mme {candidature.CandidatNomComplet.ToUpperInvariant()}", PdfSubjectFont));
                    if (!string.IsNullOrWhiteSpace(candidature.CandidatEmail))
                        cellDest.AddElement(new Paragraph($"Email : {candidature.CandidatEmail}", PdfCellFont));
                    if (!string.IsNullOrWhiteSpace(candidature.CandidatTelephone))
                        cellDest.AddElement(new Paragraph($"Tél : {candidature.CandidatTelephone}", PdfCellFont));
                    tableDest.AddCell(cellDest);

                    document.Add(tableDest);
                    document.Add(new Paragraph(" ") { SpacingAfter = 10f });

                    // 3. Objet de la lettre
                    var tableObjet = new PdfPTable(1) { WidthPercentage = 100f };
                    var cellObjet = new PdfPCell
                    {
                        Border = Rectangle.LEFT_BORDER,
                        BorderWidthLeft = 4f,
                        BorderColorLeft = PdfAccentColor,
                        PaddingLeft = 8f,
                        PaddingTop = 4f,
                        PaddingBottom = 4f,
                        BackgroundColor = PdfLightBg
                    };
                    cellObjet.AddElement(new Paragraph($"OBJET : Offre formelle d'embauche et promesse d'engagement - Poste de {candidature.BesoinIntitulePoste}", PdfSubjectFont));
                    tableObjet.AddCell(cellObjet);
                    document.Add(tableObjet);
                    document.Add(new Paragraph(" ") { SpacingAfter = 12f });

                    // 4. Formule d'appel et corps de la lettre
                    var pCivilite = new Paragraph($"Madame / Monsieur {candidature.CandidatNomComplet},", PdfBodyBoldFont) { SpacingAfter = 8f };
                    document.Add(pCivilite);

                    var pCorps1 = new Paragraph(
                        $"À la suite des entretiens d'évaluation et du processus de sélection relatif au besoin {candidature.BesoinReference}, " +
                        $"nous avons le plaisir de vous confirmer notre proposition d'embauche au sein de notre société {raisonSociale}.",
                        PdfBodyFont)
                    { SpacingAfter = 8f, Alignment = Element.ALIGN_JUSTIFIED };
                    document.Add(pCorps1);

                    var pCorps2 = new Paragraph(
                        "Les conditions substantielles de votre engagement sont convenues comme suit :",
                        PdfBodyFont)
                    { SpacingAfter = 6f };
                    document.Add(pCorps2);

                    // 5. Tableau récapitulatif des conditions
                    var tableCond = new PdfPTable(2) { WidthPercentage = 100f };
                    tableCond.SetWidths(new float[] { 35f, 65f });

                    AjouterLigneCondition(tableCond, "Intitulé du poste :", candidature.BesoinIntitulePoste);
                    AjouterLigneCondition(tableCond, "Direction / Département :", string.IsNullOrWhiteSpace(candidature.BesoinDepartement) ? "Direction Générale" : candidature.BesoinDepartement);
                    // Candidature embauchée : le contrat figé à l'embauche fait foi ; sinon, la saisie en cours.
                    string contratRetenu = !string.IsNullOrWhiteSpace(candidature.TypeContratEmbauche) ? candidature.TypeContratEmbauche : typeContrat;
                    AjouterLigneCondition(tableCond, "Nature du contrat :", LibelleNatureContrat(contratRetenu));
                    AjouterLigneCondition(tableCond, "Date de prise de fonction :", dateEffetStr);
                    AjouterLigneCondition(tableCond, "Lieu de travail :", $"{ville}, Burkina Faso");
                    AjouterLigneCondition(tableCond, "Rémunération mensuelle brute :", $"{salaireBrut:N0} FCFA (soumise aux retenues fiscales IUTS et sociales CNSS)");
                    AjouterLigneCondition(tableCond, "Régime de sécurité sociale :", "Affiliation obligatoire à la Caisse Nationale de Sécurité Sociale (CNSS)");

                    document.Add(tableCond);
                    document.Add(new Paragraph(" ") { SpacingAfter = 8f });

                    // 6. Cadre Juridique burkinabè (Articles 38-43 Loi 028-2008/AN)
                    var tableCadreLegal = new PdfPTable(1) { WidthPercentage = 100f };
                    var cellLegal = new PdfPCell
                    {
                        Border = Rectangle.BOX,
                        BorderColor = PdfBorderColor,
                        BorderWidth = 0.8f,
                        Padding = 8f,
                        BackgroundColor = PdfLightBg
                    };
                    var pTitreLegal = new Paragraph("Dispositions légales régissant la période d'essai :", PdfBodyBoldFont);
                    var pTxtLegal = new Paragraph(
                        "Conformément aux Articles 38 à 43 de la Loi n° 028-2008/AN du 13 mai 2008 portant Code du travail du Burkina Faso, " +
                        "le présent engagement est assorti d'une période d'essai stipulée par écrit. La durée de cet essai est fixée conformément " +
                        "à votre classification professionnelle (1 mois pour les employés, 2 mois pour les agents de maîtrise, 3 mois renouvelables " +
                        "pour les cadres). Durant cette période, chacune des parties pourra résilier le contrat sans préavis ni indemnité, dans le strict " +
                        "respect des dispositions réglementaires en vigueur.",
                        PdfLegalFont)
                    { Alignment = Element.ALIGN_JUSTIFIED };

                    cellLegal.AddElement(pTitreLegal);
                    cellLegal.AddElement(pTxtLegal);
                    tableCadreLegal.AddCell(cellLegal);
                    document.Add(tableCadreLegal);
                    document.Add(new Paragraph(" ") { SpacingAfter = 10f });

                    var pConclusion = new Paragraph(
                        "Si ces conditions emportent votre agrément, nous vous prions de bien vouloir nous retourner un exemplaire de la présente, " +
                        "revêtu de votre signature précédée de la mention manuscrite « Lu et approuvé, bon pour accord ».",
                        PdfBodyFont)
                    { SpacingAfter = 12f, Alignment = Element.ALIGN_JUSTIFIED };
                    document.Add(pConclusion);

                    // 7. Double bloc de signature
                    var tableSign = new PdfPTable(2) { WidthPercentage = 100f };
                    tableSign.SetWidths(new float[] { 50f, 50f });

                    var cellSignEntr = new PdfPCell { Border = Rectangle.NO_BORDER };
                    cellSignEntr.AddElement(new Paragraph("Pour la Direction Générale / DRH :", PdfBodyBoldFont));
                    cellSignEntr.AddElement(new Paragraph("Cachet et Signature autorisée", PdfSubTitleFont));
                    cellSignEntr.AddElement(new Paragraph("\n\n\n_________________________________"));
                    tableSign.AddCell(cellSignEntr);

                    var cellSignCand = new PdfPCell { Border = Rectangle.NO_BORDER, HorizontalAlignment = Element.ALIGN_RIGHT };
                    var pTitreCand = new Paragraph("Le Candidat retenu :", PdfBodyBoldFont) { Alignment = Element.ALIGN_RIGHT };
                    var pMention = new Paragraph("(Mention manuscrite « Lu et approuvé »)", PdfLegalFont) { Alignment = Element.ALIGN_RIGHT };
                    var pLigne = new Paragraph("\n\n\n_________________________________") { Alignment = Element.ALIGN_RIGHT };
                    cellSignCand.AddElement(pTitreCand);
                    cellSignCand.AddElement(pMention);
                    cellSignCand.AddElement(pLigne);
                    tableSign.AddCell(cellSignCand);

                    document.Add(tableSign);

                    document.Close();
                }
            }

            return true;
        }

        /// <summary>
        /// Traduit le type de contrat saisi lors de l'embauche (SAL-01) en libellé complet
        /// pour la lettre d'embauche. Se rabat sur le CDI uniquement si rien n'a été saisi,
        /// au lieu de l'imposer systématiquement quel que soit le type réellement négocié.
        /// </summary>
        private static string LibelleNatureContrat(string typeContrat)
        {
            if (string.IsNullOrWhiteSpace(typeContrat))
                return "Contrat de Travail à Durée Indéterminée (CDI)";

            switch (typeContrat.Trim().ToUpperInvariant())
            {
                case "CDI":
                    return "Contrat de Travail à Durée Indéterminée (CDI)";
                case "CDD":
                    return "Contrat de Travail à Durée Déterminée (CDD)";
                case "STAGE":
                    return "Convention de Stage";
                case "ESSAI":
                    return "Contrat à l'Essai";
                default:
                    return typeContrat.Trim();
            }
        }

        private static void AjouterLigneCondition(PdfPTable table, string libelle, string valeur)
        {
            var cellLib = new PdfPCell(new Phrase(libelle, PdfBodyBoldFont))
            {
                Border = Rectangle.BOTTOM_BORDER,
                BorderColor = PdfBorderColor,
                BorderWidth = 0.5f,
                PaddingTop = 4f,
                PaddingBottom = 4f,
                BackgroundColor = PdfLightBg
            };
            var cellVal = new PdfPCell(new Phrase(valeur, PdfCellFont))
            {
                Border = Rectangle.BOTTOM_BORDER,
                BorderColor = PdfBorderColor,
                BorderWidth = 0.5f,
                PaddingTop = 4f,
                PaddingBottom = 4f
            };
            table.AddCell(cellLib);
            table.AddCell(cellVal);
        }

        #endregion

        #region Bilan & Pilotage du Recrutement (Excel ClosedXML - 4 Feuilles)

        /// <summary>
        /// Produit un classeur Excel complet de pilotage et d'audit du recrutement comprenant 4 onglets :
        /// 1. Synthèse & Tableau de Bord
        /// 2. Besoins de Recrutement
        /// 3. Pipeline des Candidatures
        /// 4. Vivier des Candidats
        /// </summary>
        public bool GenererBilanRecrutementExcel(
            List<BesoinRecrutement> besoins,
            List<Candidature> candidatures,
            List<Candidat> vivier,
            string cheminFichier)
        {
            if (string.IsNullOrWhiteSpace(cheminFichier)) throw new ArgumentNullException(nameof(cheminFichier));

            besoins = besoins ?? new List<BesoinRecrutement>();
            candidatures = candidatures ?? new List<Candidature>();
            vivier = vivier ?? new List<Candidat>();

            var dossier = DossierSession.Instance.CurrentDossier;
            string nomDossier = dossier?.RaisonSociale ?? "Sigma Paie & RH";

            using (var workbook = new XLWorkbook())
            {
                // ==========================================
                // FEUILLE 1 : TABLEAU DE BORD & SYNTHÈSE
                // ==========================================
                var wsSynthese = workbook.Worksheets.Add("Synthèse & KPIs");
                wsSynthese.ShowGridLines = true;

                // Titre
                wsSynthese.Cell("B2").Value = $"BILAN DU RECRUTEMENT & PILOTAGE DES CANDIDATURES — {nomDossier.ToUpperInvariant()}";
                wsSynthese.Cell("B2").Style.Font.Bold = true;
                wsSynthese.Cell("B2").Style.Font.FontSize = 14;
                wsSynthese.Cell("B2").Style.Font.FontColor = CouleurAccent;

                wsSynthese.Cell("B3").Value = $"Édité le {DateTime.Now:dd/MM/yyyy à HH:mm} — Données consolidées RH-05";
                wsSynthese.Cell("B3").Style.Font.Italic = true;
                wsSynthese.Cell("B3").Style.Font.FontColor = CouleurDiscret;

                // Cartouches KPI
                int totalBesoins = besoins.Count;
                int besoinsOuverts = besoins.Count(b => b.Statut == StatutBesoinRecrutement.Ouvert || b.Statut == StatutBesoinRecrutement.EnCours);
                int besoinsPourvus = besoins.Count(b => b.Statut == StatutBesoinRecrutement.Pourvu);
                int totalPostes = besoins.Sum(b => b.NombrePostes);
                int totalRecrutes = besoins.Sum(b => b.NombreRecrutes);
                int totalCandidatures = candidatures.Count;
                int candidaturesEmbauchees = candidatures.Count(c => c.Statut == StatutCandidature.Embauchee);
                int vivierActif = vivier.Count(v => v.VivierActif);

                // Tableau récapitulatif
                int row = 5;
                wsSynthese.Cell(row, 2).Value = "Indicateur Clé de Performance (KPI)";
                wsSynthese.Cell(row, 3).Value = "Valeur Consolidée";
                var enteteRange = wsSynthese.Range(row, 2, row, 3);
                enteteRange.Style.Fill.BackgroundColor = CouleurAccent;
                enteteRange.Style.Font.Bold = true;
                enteteRange.Style.Font.FontColor = XLColor.White;

                row++;
                AjouterLigneKpi(wsSynthese, row++, "Total des besoins de recrutement exprimés", totalBesoins.ToString());
                AjouterLigneKpi(wsSynthese, row++, "Besoins en cours de traitement / ouverts", besoinsOuverts.ToString());
                AjouterLigneKpi(wsSynthese, row++, "Besoins intégralement pourvus", besoinsPourvus.ToString());
                AjouterLigneKpi(wsSynthese, row++, "Nombre total de postes ouverts", totalPostes.ToString());
                AjouterLigneKpi(wsSynthese, row++, "Nombre de salariés effectivement recrutés", totalRecrutes.ToString());
                AjouterLigneKpi(wsSynthese, row++, "Taux d'atteinte des recrutements", totalPostes > 0 ? $"{(double)totalRecrutes / totalPostes:P1}" : "0 %");
                AjouterLigneKpi(wsSynthese, row++, "Total des candidatures enregistrées", totalCandidatures.ToString());
                AjouterLigneKpi(wsSynthese, row++, "Candidatures ayant abouti à une embauche", candidaturesEmbauchees.ToString());
                AjouterLigneKpi(wsSynthese, row++, "Profils actifs dans le vivier de compétences", vivierActif.ToString());

                wsSynthese.Columns().AdjustToContents();

                // ==========================================
                // FEUILLE 2 : BESOINS DE RECRUTEMENT
                // ==========================================
                var wsBesoins = workbook.Worksheets.Add("Besoins & Postes");
                wsBesoins.ShowGridLines = true;

                string[] entetesBesoins = new[]
                {
                    "Réf. Besoin", "Intitulé du Poste", "Département", "Motif",
                    "Postes Ouverts", "Recrutés", "Restants", "Type Contrat",
                    "Rémun. Min (FCFA)", "Rémun. Max (FCFA)", "Date Expression",
                    "Échéance", "Statut", "Responsable", "Total Candidats"
                };

                for (int i = 0; i < entetesBesoins.Length; i++)
                {
                    var cell = wsBesoins.Cell(1, i + 1);
                    cell.Value = entetesBesoins[i];
                    cell.Style.Fill.BackgroundColor = CouleurAccent;
                    cell.Style.Font.Bold = true;
                    cell.Style.Font.FontColor = XLColor.White;
                }

                int rB = 2;
                foreach (var b in besoins)
                {
                    wsBesoins.Cell(rB, 1).Value = b.Reference;
                    wsBesoins.Cell(rB, 2).Value = b.IntitulePoste;
                    wsBesoins.Cell(rB, 3).Value = b.Departement;
                    wsBesoins.Cell(rB, 4).Value = b.MotifLibelle;
                    wsBesoins.Cell(rB, 5).Value = b.NombrePostes;
                    wsBesoins.Cell(rB, 6).Value = b.NombreRecrutes;
                    wsBesoins.Cell(rB, 7).Value = b.PostesRestants;
                    wsBesoins.Cell(rB, 8).Value = b.TypeContratVise;
                    wsBesoins.Cell(rB, 9).Value = b.RemunerationProposeeMin ?? 0;
                    wsBesoins.Cell(rB, 9).Style.NumberFormat.Format = "#,##0";
                    wsBesoins.Cell(rB, 10).Value = b.RemunerationProposeeMax ?? 0;
                    wsBesoins.Cell(rB, 10).Style.NumberFormat.Format = "#,##0";
                    wsBesoins.Cell(rB, 11).Value = b.DateExpression.ToString("dd/MM/yyyy");
                    wsBesoins.Cell(rB, 12).Value = b.DateEcheanceSouhaitee?.ToString("dd/MM/yyyy") ?? "-";
                    wsBesoins.Cell(rB, 13).Value = b.StatutLibelle;
                    wsBesoins.Cell(rB, 14).Value = b.ResponsableRecrutement;
                    wsBesoins.Cell(rB, 15).Value = b.NombreCandidatures;

                    if (rB % 2 == 1)
                    {
                        wsBesoins.Range(rB, 1, rB, entetesBesoins.Length).Style.Fill.BackgroundColor = CouleurTrame;
                    }

                    rB++;
                }
                wsBesoins.Columns().AdjustToContents();

                // ==========================================
                // FEUILLE 3 : PIPELINE DES CANDIDATURES
                // ==========================================
                var wsCand = workbook.Worksheets.Add("Pipeline Candidatures");
                wsCand.ShowGridLines = true;

                string[] entetesCand = new[]
                {
                    "Réf. Besoin", "Poste Visé", "Candidat", "Email", "Téléphone",
                    "Diplôme", "Expérience (ans)", "Date Postulation", "Canal Source",
                    "Prétention (FCFA)", "Étape Actuelle", "Statut", "Score (/20)",
                    "Avis Global", "Date Embauche", "Salaire Négocié (FCFA)", "Matricule Salarié Créé"
                };

                for (int i = 0; i < entetesCand.Length; i++)
                {
                    var cell = wsCand.Cell(1, i + 1);
                    cell.Value = entetesCand[i];
                    cell.Style.Fill.BackgroundColor = CouleurAccent;
                    cell.Style.Font.Bold = true;
                    cell.Style.Font.FontColor = XLColor.White;
                }

                int rC = 2;
                foreach (var c in candidatures)
                {
                    wsCand.Cell(rC, 1).Value = c.BesoinReference;
                    wsCand.Cell(rC, 2).Value = c.BesoinIntitulePoste;
                    wsCand.Cell(rC, 3).Value = c.CandidatNomComplet;
                    wsCand.Cell(rC, 4).Value = c.CandidatEmail;
                    wsCand.Cell(rC, 5).Value = c.CandidatTelephone;
                    wsCand.Cell(rC, 6).Value = c.CandidatDiplome;
                    wsCand.Cell(rC, 7).Value = c.CandidatExperience;
                    wsCand.Cell(rC, 8).Value = c.DatePostulation.ToString("dd/MM/yyyy");
                    wsCand.Cell(rC, 9).Value = c.CanalSourceLibelle;
                    wsCand.Cell(rC, 10).Value = c.PretentionSalariale ?? 0;
                    wsCand.Cell(rC, 10).Style.NumberFormat.Format = "#,##0";
                    wsCand.Cell(rC, 11).Value = c.EtapeActuelleLibelle;
                    wsCand.Cell(rC, 12).Value = c.StatutLibelle;
                    wsCand.Cell(rC, 13).Value = c.ScoreEvaluation.HasValue ? c.ScoreEvaluation.Value.ToString("F1", CultureInfo.InvariantCulture) : "-";
                    wsCand.Cell(rC, 14).Value = c.AvisGlobalLibelle;
                    wsCand.Cell(rC, 15).Value = c.DateEmbauche?.ToString("dd/MM/yyyy") ?? "-";
                    wsCand.Cell(rC, 16).Value = c.SalaireNegocie ?? 0;
                    wsCand.Cell(rC, 16).Style.NumberFormat.Format = "#,##0";
                    wsCand.Cell(rC, 17).Value = c.SalarieCreeMatricule ?? "-";

                    if (c.EstEmbauchee)
                    {
                        wsCand.Range(rC, 1, rC, entetesCand.Length).Style.Font.FontColor = CouleurVert;
                        wsCand.Cell(rC, 12).Style.Font.Bold = true;
                    }
                    else if (rC % 2 == 1)
                    {
                        wsCand.Range(rC, 1, rC, entetesCand.Length).Style.Fill.BackgroundColor = CouleurTrame;
                    }

                    rC++;
                }
                wsCand.Columns().AdjustToContents();

                // ==========================================
                // FEUILLE 4 : VIVIER DES CANDIDATS
                // ==========================================
                var wsVivier = workbook.Worksheets.Add("Vivier des Talents");
                wsVivier.ShowGridLines = true;

                string[] entetesVivier = new[]
                {
                    "Nom & Prénoms", "Email", "Téléphone", "Diplôme le plus élevé",
                    "Expérience (ans)", "Dernière Fonction", "Compétences Clés",
                    "Statut Vivier", "Nombre Candidatures", "Date Enregistrement"
                };

                for (int i = 0; i < entetesVivier.Length; i++)
                {
                    var cell = wsVivier.Cell(1, i + 1);
                    cell.Value = entetesVivier[i];
                    cell.Style.Fill.BackgroundColor = CouleurAccent;
                    cell.Style.Font.Bold = true;
                    cell.Style.Font.FontColor = XLColor.White;
                }

                int rV = 2;
                foreach (var v in vivier)
                {
                    wsVivier.Cell(rV, 1).Value = v.NomComplet;
                    wsVivier.Cell(rV, 2).Value = v.Email;
                    wsVivier.Cell(rV, 3).Value = v.Telephone;
                    wsVivier.Cell(rV, 4).Value = v.DiplomeLePlusEleve;
                    wsVivier.Cell(rV, 5).Value = v.AnneesExperience;
                    wsVivier.Cell(rV, 6).Value = v.DerniereFonction;
                    wsVivier.Cell(rV, 7).Value = v.CompetencesCles;
                    wsVivier.Cell(rV, 8).Value = v.VivierActif ? "Actif" : "Archivé";
                    wsVivier.Cell(rV, 9).Value = v.NombreCandidatures;
                    wsVivier.Cell(rV, 10).Value = v.DateCreation.ToString("dd/MM/yyyy");

                    if (rV % 2 == 1)
                    {
                        wsVivier.Range(rV, 1, rV, entetesVivier.Length).Style.Fill.BackgroundColor = CouleurTrame;
                    }

                    rV++;
                }
                wsVivier.Columns().AdjustToContents();

                workbook.SaveAs(cheminFichier);
            }

            return true;
        }

        private static void AjouterLigneKpi(IXLWorksheet ws, int row, string libelle, string valeur)
        {
            ws.Cell(row, 2).Value = libelle;
            ws.Cell(row, 3).Value = valeur;
            ws.Cell(row, 3).Style.Font.Bold = true;
            ws.Cell(row, 3).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;

            var r = ws.Range(row, 2, row, 3);
            r.Style.Border.BottomBorder = XLBorderStyleValues.Thin;
            r.Style.Border.BottomBorderColor = CouleurBordure;

            if (row % 2 == 1)
            {
                r.Style.Fill.BackgroundColor = CouleurTrame;
            }
        }

        #endregion
    }
}
