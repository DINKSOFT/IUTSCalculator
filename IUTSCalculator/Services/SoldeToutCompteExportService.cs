using System;
using System.IO;
using ClosedXML.Excel;
using iTextSharp.text;
using iTextSharp.text.pdf;
using IUTSCalculator.Models;

namespace IUTSCalculator.Services
{
    /// <summary>
    /// Service d'édition des documents légaux de fin de contrat et d'export du Solde de Tout Compte :
    /// - Reçu pour solde de tout compte (PDF & Excel)
    /// - Certificat de travail (Art. 67 du Code du travail burkinabè) (PDF)
    /// - Attestation de travail / fin de service (PDF)
    /// </summary>
    public class SoldeToutCompteExportService
    {
        private static readonly BaseColor PrimaryColor = new BaseColor(30, 41, 59);    // Slate 800
        private static readonly BaseColor AccentColor = new BaseColor(20, 103, 90);    // Emerald/Teal #14675A
        private static readonly BaseColor LightBgColor = new BaseColor(248, 250, 252); // Slate 50
        private static readonly BaseColor BorderColor = new BaseColor(226, 232, 240);  // Slate 200
        private static readonly BaseColor TextMuted = new BaseColor(100, 116, 139);   // Slate 500

        private static readonly Font TitleFont = new Font(Font.FontFamily.HELVETICA, 16, Font.BOLD, PrimaryColor);
        private static readonly Font SubTitleFont = new Font(Font.FontFamily.HELVETICA, 11, Font.BOLD, AccentColor);
        private static readonly Font SectionFont = new Font(Font.FontFamily.HELVETICA, 11, Font.BOLD, PrimaryColor);
        private static readonly Font BoldFont = new Font(Font.FontFamily.HELVETICA, 9.5f, Font.BOLD, PrimaryColor);
        private static readonly Font NormalFont = new Font(Font.FontFamily.HELVETICA, 9.5f, Font.NORMAL, PrimaryColor);
        private static readonly Font SmallFont = new Font(Font.FontFamily.HELVETICA, 8.5f, Font.NORMAL, TextMuted);
        private static readonly Font LegalNoticeFont = new Font(Font.FontFamily.HELVETICA, 8.5f, Font.ITALIC, PrimaryColor);

        // ═══════════════════════════════════════════════════════════════════════
        // 1. REÇU POUR SOLDE DE TOUT COMPTE (PDF)
        // ═══════════════════════════════════════════════════════════════════════

        public bool GenererRecuPdf(
            SoldeToutCompte stc,
            string cheminFichier,
            string raisonSociale = null,
            string ifu = "",
            string adresse = "")
        {
            if (stc == null) throw new ArgumentNullException(nameof(stc));
            if (string.IsNullOrWhiteSpace(cheminFichier))
                throw new ArgumentException("Chemin de fichier requis.", nameof(cheminFichier));

            var currentDossier = DossierSession.Instance.CurrentDossier;
            if (string.IsNullOrWhiteSpace(raisonSociale))
                raisonSociale = !string.IsNullOrWhiteSpace(currentDossier?.RaisonSociale) ? currentDossier.RaisonSociale : "L'ENTREPRISE";
            if (string.IsNullOrWhiteSpace(ifu))
                ifu = currentDossier?.IFU ?? "";
            if (string.IsNullOrWhiteSpace(adresse))
                adresse = currentDossier?.Adresse ?? "";

            var dir = Path.GetDirectoryName(cheminFichier);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            Document document = null;
            PdfWriter writer = null;
            FileStream fs = null;

            try
            {
                fs = new FileStream(cheminFichier, FileMode.Create);
                document = new Document(PageSize.A4, 36, 36, 36, 36);
                writer = PdfWriter.GetInstance(document, fs);

                document.Open();

                // En-tête entreprise
                var enteteTable = new PdfPTable(2) { WidthPercentage = 100 };
                enteteTable.SetWidths(new float[] { 3f, 2f });

                var cellEnt = new PdfPCell
                {
                    Border = Rectangle.NO_BORDER,
                    PaddingBottom = 8f
                };
                cellEnt.AddElement(new Paragraph(raisonSociale.ToUpper(), new Font(Font.FontFamily.HELVETICA, 14, Font.BOLD, AccentColor)));
                if (!string.IsNullOrWhiteSpace(ifu))
                    cellEnt.AddElement(new Paragraph($"IFU : {ifu}", SmallFont));
                if (!string.IsNullOrWhiteSpace(adresse))
                    cellEnt.AddElement(new Paragraph(adresse, SmallFont));
                enteteTable.AddCell(cellEnt);

                var cellDate = new PdfPCell
                {
                    Border = Rectangle.NO_BORDER,
                    HorizontalAlignment = Element.ALIGN_RIGHT,
                    PaddingBottom = 8f
                };
                var pDate = new Paragraph($"Édité le : {DateTime.Now:dd/MM/yyyy}", SmallFont) { Alignment = Element.ALIGN_RIGHT };
                cellDate.AddElement(pDate);
                enteteTable.AddCell(cellDate);

                document.Add(enteteTable);

                // Titre document
                var pTitre = new Paragraph("REÇU POUR SOLDE DE TOUT COMPTE", TitleFont)
                {
                    Alignment = Element.ALIGN_CENTER,
                    SpacingBefore = 6f,
                    SpacingAfter = 12f
                };
                document.Add(pTitre);

                // Bloc informations Salarié & Contrat
                var infoTable = new PdfPTable(4) { WidthPercentage = 100, SpacingAfter = 12f };
                infoTable.SetWidths(new float[] { 1.3f, 2f, 1.3f, 2f });

                AddInfoCell(infoTable, "Salarié :", stc.NomSalarie, true);
                AddInfoCell(infoTable, "Matricule :", stc.EmployeeId, false);
                AddInfoCell(infoTable, "Emploi / Fonction :", stc.Fonction, false);
                AddInfoCell(infoTable, "Classification :", $"{stc.Categorie} (Éch. {stc.Echelon})", false);
                AddInfoCell(infoTable, "Date d'embauche :", stc.DateEntree.ToString("dd/MM/yyyy"), false);
                AddInfoCell(infoTable, "Date de sortie :", stc.DateSortie.ToString("dd/MM/yyyy"), false);
                AddInfoCell(infoTable, "Ancienneté :", stc.AncienneteTexte, false);
                AddInfoCell(infoTable, "Motif de rupture :", stc.MotifLibelle, true);

                document.Add(infoTable);

                // Tableau financier
                var finTable = new PdfPTable(4) { WidthPercentage = 100, SpacingAfter = 12f };
                finTable.SetWidths(new float[] { 3.5f, 1.8f, 1.5f, 1.8f });

                // Entêtes
                AddHeaderCell(finTable, "Éléments et indemnités de départ");
                AddHeaderCell(finTable, "Base / Détail", Element.ALIGN_CENTER);
                AddHeaderCell(finTable, "Régime fiscal", Element.ALIGN_CENTER);
                AddHeaderCell(finTable, "Montant (FCFA)", Element.ALIGN_RIGHT);

                // Lignes de gains
                AddFinancialRow(finTable, "Dernier salaire (salaire de présence)", $"{stc.JoursPresence:0.##} jours", "Imposable", stc.SalairePresence);
                AddFinancialRow(finTable, "Indemnité compensatrice de congés payés", $"{stc.JoursCongesDus:0.##} jours dus", "Imposable", stc.IndemniteConges);

                string preavisDetail = stc.StatutPreavis == StatutPreavis.DispensePaye
                    ? $"{stc.DureePreavisMois} mois compensés"
                    : stc.StatutPreavisLibelle;
                AddFinancialRow(finTable, "Indemnité compensatrice de préavis", preavisDetail, "Imposable", stc.IndemnitePreavis);

                string licDetail = stc.IndemniteLicenciement > 0
                    ? $"Taux {stc.TauxLicenciementApplique:0.##}%"
                    : "Non applicable";
                AddFinancialRow(finTable, "Indemnité de licenciement / de fin de service", licDetail, "Exonéré (Art. 106)", stc.IndemniteLicenciement, true);

                if (stc.Indemnite13emeMois > 0)
                    AddFinancialRow(finTable, "Prorata de gratification / 13ème mois", $"{stc.Mois13emeMois:0.##} mois", "Imposable", stc.Indemnite13emeMois);

                if (stc.IndemnitesDiverses > 0)
                {
                    string libelle = string.IsNullOrWhiteSpace(stc.LibelleIndemnitesDiverses) ? "Indemnités diverses" : stc.LibelleIndemnitesDiverses;
                    AddFinancialRow(finTable, libelle, "-", "Imposable", stc.IndemnitesDiverses);
                }

                // Total Brut
                AddTotalRow(finTable, "TOTAL BRUT DES INDEMNITÉS", stc.TotalBrut);

                // Déductions et retenues
                if (stc.EstimationIUTS > 0)
                    AddFinancialRow(finTable, "Estimation Retenue IUTS (sur éléments imposables)", "-", "Retenue fiscale", -stc.EstimationIUTS);
                if (stc.EstimationCNSS > 0)
                    AddFinancialRow(finTable, "Estimation Retenue CNSS salariale (5,5%)", "-", "Cotisation sociale", -stc.EstimationCNSS);
                if (stc.DeductionsPrets > 0)
                    AddFinancialRow(finTable, "Remboursement solde de prêts et avances en cours", "Dette éteinte", "Retenue sur net", -stc.DeductionsPrets);
                if (stc.DeductionsAutres > 0)
                {
                    string libelle = string.IsNullOrWhiteSpace(stc.LibelleDeductionsAutres) ? "Autres retenues de sortie" : stc.LibelleDeductionsAutres;
                    AddFinancialRow(finTable, libelle, "-", "Retenue sur net", -stc.DeductionsAutres);
                }

                // Total Retenues
                AddTotalRow(finTable, "TOTAL DES RETENUES ET DÉDUCTIONS", -stc.TotalRetenues);

                // NET À PAYER
                AddGrandTotalRow(finTable, "NET À PERCEVOIR POUR SOLDE DE TOUT COMPTE", stc.TotalNet);

                document.Add(finTable);

                // Montant en lettres
                string montantEnLettres = NombreEnLettresHelper.Formater(stc.TotalNet);
                var pLettres = new Paragraph($"Arrêté le présent solde de tout compte à la somme nette de : {montantEnLettres.ToUpper()} FRANCS CFA.", BoldFont)
                {
                    SpacingAfter = 10f
                };
                document.Add(pLettres);

                // Décharge et mentions légales
                var pMention = new Paragraph(
                    "Je soussigné(e), " + stc.NomSalarie + ", reconnais avoir reçu de l'entreprise " + raisonSociale +
                    " la somme indiquée ci-dessus pour solde de tout compte, en règlement définitif de tous salaires, indemnités et avantages de toute nature qui m'étaient dus au titre de l'exécution et de la cessation de mon contrat de travail.\n\n" +
                    "Il est rappelé que conformément aux dispositions du Code du travail du Burkina Faso, le salarié dispose d'un délai légal de deux (2) mois à compter de la signature pour dénoncer le présent reçu par lettre recommandée ou notification écrite dûment motivée.",
                    LegalNoticeFont)
                {
                    SpacingAfter = 14f
                };
                document.Add(pMention);

                // Signatures
                var sigTable = new PdfPTable(2) { WidthPercentage = 100 };
                sigTable.SetWidths(new float[] { 1f, 1f });

                var cellSigEmp = new PdfPCell { Border = Rectangle.NO_BORDER, PaddingTop = 5f };
                cellSigEmp.AddElement(new Paragraph("Pour l'Employeur / La Direction", BoldFont));
                cellSigEmp.AddElement(new Paragraph("Signature et cachet", SmallFont));
                cellSigEmp.AddElement(new Paragraph("\n\n\n\n__________________________________", SmallFont));
                sigTable.AddCell(cellSigEmp);

                var cellSigSal = new PdfPCell { Border = Rectangle.NO_BORDER, PaddingTop = 5f, HorizontalAlignment = Element.ALIGN_RIGHT };
                var pSalTitle = new Paragraph("Le Salarié", BoldFont) { Alignment = Element.ALIGN_RIGHT };
                var pSalMention = new Paragraph("Mention manuscrite obligatoire :\n« Pour solde de tout compte »", SmallFont) { Alignment = Element.ALIGN_RIGHT };
                var pSalLine = new Paragraph("\n\n\n__________________________________", SmallFont) { Alignment = Element.ALIGN_RIGHT };
                cellSigSal.AddElement(pSalTitle);
                cellSigSal.AddElement(pSalMention);
                cellSigSal.AddElement(pSalLine);
                sigTable.AddCell(cellSigSal);

                document.Add(sigTable);

                document.Close();
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Erreur export PDF STC: {ex.Message}");
                return false;
            }
            finally
            {
                if (document != null && document.IsOpen()) document.Close();
                if (writer != null) writer.Close();
                if (fs != null) { fs.Close(); fs.Dispose(); }
            }
        }

        // ═══════════════════════════════════════════════════════════════════════
        // 2. CERTIFICAT DE TRAVAIL (ART. 67 CODE DU TRAVAIL) (PDF)
        // ═══════════════════════════════════════════════════════════════════════

        public bool GenererCertificatTravailPdf(
            SoldeToutCompte stc,
            string cheminFichier,
            string raisonSociale = null,
            string ifu = "",
            string adresse = "",
            string ville = null)
        {
            if (stc == null) throw new ArgumentNullException(nameof(stc));
            if (string.IsNullOrWhiteSpace(cheminFichier))
                throw new ArgumentException("Chemin de fichier requis.", nameof(cheminFichier));

            var currentDossier = DossierSession.Instance.CurrentDossier;
            if (string.IsNullOrWhiteSpace(raisonSociale))
                raisonSociale = !string.IsNullOrWhiteSpace(currentDossier?.RaisonSociale) ? currentDossier.RaisonSociale : "L'ENTREPRISE";
            if (string.IsNullOrWhiteSpace(ifu))
                ifu = currentDossier?.IFU ?? "";
            if (string.IsNullOrWhiteSpace(adresse))
                adresse = currentDossier?.Adresse ?? "";
            if (string.IsNullOrWhiteSpace(ville))
                ville = !string.IsNullOrWhiteSpace(currentDossier?.Ville) ? currentDossier.Ville : "Ouagadougou";

            var dir = Path.GetDirectoryName(cheminFichier);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            Document document = null;
            PdfWriter writer = null;
            FileStream fs = null;

            try
            {
                fs = new FileStream(cheminFichier, FileMode.Create);
                document = new Document(PageSize.A4, 45, 45, 45, 45);
                writer = PdfWriter.GetInstance(document, fs);

                document.Open();

                // En-tête
                var entete = new Paragraph(raisonSociale.ToUpper(), new Font(Font.FontFamily.HELVETICA, 15, Font.BOLD, AccentColor))
                {
                    Alignment = Element.ALIGN_CENTER
                };
                document.Add(entete);

                if (!string.IsNullOrWhiteSpace(ifu) || !string.IsNullOrWhiteSpace(adresse))
                {
                    string sous = $"{adresse} {(string.IsNullOrEmpty(ifu) ? "" : "— IFU : " + ifu)}".Trim();
                    var pSous = new Paragraph(sous, SmallFont) { Alignment = Element.ALIGN_CENTER, SpacingAfter = 25f };
                    document.Add(pSous);
                }
                else
                {
                    document.Add(new Paragraph(" ") { SpacingAfter = 20f });
                }

                // Titre
                var pTitre = new Paragraph("CERTIFICAT DE TRAVAIL", new Font(Font.FontFamily.HELVETICA, 18, Font.BOLD, PrimaryColor))
                {
                    Alignment = Element.ALIGN_CENTER
                };
                document.Add(pTitre);

                var pRefLegale = new Paragraph("(Délivré en application de l'Article 67 du Code du travail du Burkina Faso)", new Font(Font.FontFamily.HELVETICA, 10, Font.ITALIC, TextMuted))
                {
                    Alignment = Element.ALIGN_CENTER,
                    SpacingAfter = 35f
                };
                document.Add(pRefLegale);

                // Corps du certificat
                string corps =
                    $"Nous soussignés, {raisonSociale.ToUpper()}, certifions par la présente que :\n\n" +
                    $"Monsieur / Madame : {stc.NomSalarie.ToUpper()}\n" +
                    $"Matricule : {stc.EmployeeId}\n" +
                    $"Classification professionnelle : {stc.Categorie} (Échelon {stc.Echelon})\n\n" +
                    $"A été employé(e) dans notre entreprise en qualité de : {stc.Fonction.ToUpper()}\n\n" +
                    $"Du : {stc.DateEntree:dd MMMM yyyy}\n" +
                    $"Au : {stc.DateSortie:dd MMMM yyyy}\n\n" +
                    "L'intéressé(e) quitte notre entreprise libre de tout engagement professionnel envers elle.\n\n" +
                    "Conformément aux prescriptions de l'article 67 du Code du travail, le présent certificat est exempt de toute mention tendancieuse ou préjudiciable au travailleur et lui est délivré pour servir et valoir ce que de droit.";

                var pCorps = new Paragraph(corps, new Font(Font.FontFamily.HELVETICA, 11, Font.NORMAL, PrimaryColor))
                {
                    Leading = 20f,
                    SpacingAfter = 40f
                };
                document.Add(pCorps);

                // Date et lieu
                var pLieu = new Paragraph($"Fait à {ville}, le {DateTime.Today:dd MMMM yyyy}", BoldFont)
                {
                    Alignment = Element.ALIGN_RIGHT,
                    SpacingAfter = 20f
                };
                document.Add(pLieu);

                // Signature Direction
                var pSign = new Paragraph("La Direction Générale\nSignature & Cachet", BoldFont)
                {
                    Alignment = Element.ALIGN_RIGHT
                };
                document.Add(pSign);

                document.Close();
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Erreur export Certificat: {ex.Message}");
                return false;
            }
            finally
            {
                if (document != null && document.IsOpen()) document.Close();
                if (writer != null) writer.Close();
                if (fs != null) { fs.Close(); fs.Dispose(); }
            }
        }

        // ═══════════════════════════════════════════════════════════════════════
        // 3. ATTESTATION DE FIN DE SERVICE / DE TRAVAIL (PDF)
        // ═══════════════════════════════════════════════════════════════════════

        public bool GenererAttestationTravailPdf(
            SoldeToutCompte stc,
            string cheminFichier,
            string raisonSociale = null,
            string ifu = "",
            string adresse = "",
            string ville = null)
        {
            if (stc == null) throw new ArgumentNullException(nameof(stc));
            if (string.IsNullOrWhiteSpace(cheminFichier))
                throw new ArgumentException("Chemin de fichier requis.", nameof(cheminFichier));

            var currentDossier = DossierSession.Instance.CurrentDossier;
            if (string.IsNullOrWhiteSpace(raisonSociale))
                raisonSociale = !string.IsNullOrWhiteSpace(currentDossier?.RaisonSociale) ? currentDossier.RaisonSociale : "L'ENTREPRISE";
            if (string.IsNullOrWhiteSpace(ifu))
                ifu = currentDossier?.IFU ?? "";
            if (string.IsNullOrWhiteSpace(adresse))
                adresse = currentDossier?.Adresse ?? "";
            if (string.IsNullOrWhiteSpace(ville))
                ville = !string.IsNullOrWhiteSpace(currentDossier?.Ville) ? currentDossier.Ville : "Ouagadougou";

            var dir = Path.GetDirectoryName(cheminFichier);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            Document document = null;
            PdfWriter writer = null;
            FileStream fs = null;

            try
            {
                fs = new FileStream(cheminFichier, FileMode.Create);
                document = new Document(PageSize.A4, 45, 45, 45, 45);
                writer = PdfWriter.GetInstance(document, fs);

                document.Open();

                // En-tête
                var entete = new Paragraph(raisonSociale.ToUpper(), new Font(Font.FontFamily.HELVETICA, 15, Font.BOLD, AccentColor))
                {
                    Alignment = Element.ALIGN_CENTER
                };
                document.Add(entete);

                if (!string.IsNullOrWhiteSpace(ifu) || !string.IsNullOrWhiteSpace(adresse))
                {
                    string sous = $"{adresse} {(string.IsNullOrEmpty(ifu) ? "" : "— IFU : " + ifu)}".Trim();
                    var pSous = new Paragraph(sous, SmallFont) { Alignment = Element.ALIGN_CENTER, SpacingAfter = 25f };
                    document.Add(pSous);
                }
                else
                {
                    document.Add(new Paragraph(" ") { SpacingAfter = 20f });
                }

                // Titre
                var pTitre = new Paragraph("ATTESTATION DE TRAVAIL", new Font(Font.FontFamily.HELVETICA, 18, Font.BOLD, PrimaryColor))
                {
                    Alignment = Element.ALIGN_CENTER,
                    SpacingAfter = 35f
                };
                document.Add(pTitre);

                // Corps
                string corps =
                    $"La Direction de {raisonSociale.ToUpper()} atteste par la présente que :\n\n" +
                    $"Monsieur / Madame : {stc.NomSalarie.ToUpper()}\n" +
                    $"Matricule interne : {stc.EmployeeId}\n" +
                    $"A exercé au sein de notre établissement les fonctions de :\n" +
                    $"« {stc.Fonction.ToUpper()} »\n\n" +
                    $"Du {stc.DateEntree:dd MMMM yyyy} au {stc.DateSortie:dd MMMM yyyy}.\n\n" +
                    "Durant toute sa période d'activité, l'intéressé(e) a accompli ses devoirs professionnels avec ponctualité, probité et compétence.\n\n" +
                    "En foi de quoi, la présente attestation lui est délivrée à sa demande pour servir et valoir ce que de droit.";

                var pCorps = new Paragraph(corps, new Font(Font.FontFamily.HELVETICA, 11, Font.NORMAL, PrimaryColor))
                {
                    Leading = 22f,
                    SpacingAfter = 40f
                };
                document.Add(pCorps);

                // Date et lieu
                var pLieu = new Paragraph($"Fait à {ville}, le {DateTime.Today:dd MMMM yyyy}", BoldFont)
                {
                    Alignment = Element.ALIGN_RIGHT,
                    SpacingAfter = 20f
                };
                document.Add(pLieu);

                // Signature Direction
                var pSign = new Paragraph("Pour l'Employeur\nLa Direction des Ressources Humaines", BoldFont)
                {
                    Alignment = Element.ALIGN_RIGHT
                };
                document.Add(pSign);

                document.Close();
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Erreur export Attestation: {ex.Message}");
                return false;
            }
            finally
            {
                if (document != null && document.IsOpen()) document.Close();
                if (writer != null) writer.Close();
                if (fs != null) { fs.Close(); fs.Dispose(); }
            }
        }

        // ═══════════════════════════════════════════════════════════════════════
        // 4. EXPORT EXCEL (CLOSEDXML) DU DÉCOMPTE DU STC
        // ═══════════════════════════════════════════════════════════════════════

        public string ExporterExcel(SoldeToutCompte stc, string cheminFichier, string raisonSociale = null)
        {
            if (stc == null) throw new ArgumentNullException(nameof(stc));
            if (string.IsNullOrWhiteSpace(cheminFichier))
                throw new ArgumentException("Chemin de fichier requis.", nameof(cheminFichier));

            var currentDossier = DossierSession.Instance.CurrentDossier;
            if (string.IsNullOrWhiteSpace(raisonSociale))
                raisonSociale = !string.IsNullOrWhiteSpace(currentDossier?.RaisonSociale) ? currentDossier.RaisonSociale : "L'ENTREPRISE";

            var dir = Path.GetDirectoryName(cheminFichier);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            using (var wb = new XLWorkbook())
            {
                var ws = wb.Worksheets.Add("Solde de tout compte");
                ws.ShowGridLines = true;

                int row = 1;

                // Titre
                ws.Cell(row, 1).Value = raisonSociale.ToUpper();
                ws.Cell(row, 1).Style.Font.Bold = true;
                ws.Cell(row, 1).Style.Font.FontSize = 14;
                ws.Cell(row, 1).Style.Font.FontColor = XLColor.FromArgb(20, 103, 90);
                row++;

                ws.Cell(row, 1).Value = "DÉCOMPTE DU SOLDE DE TOUT COMPTE";
                ws.Cell(row, 1).Style.Font.Bold = true;
                ws.Cell(row, 1).Style.Font.FontSize = 13;
                row += 2;

                // Informations Salarié
                ws.Cell(row, 1).Value = "Salarié :";
                ws.Cell(row, 1).Style.Font.Bold = true;
                ws.Cell(row, 2).Value = stc.NomSalarie;
                ws.Cell(row, 3).Value = "Matricule :";
                ws.Cell(row, 3).Style.Font.Bold = true;
                ws.Cell(row, 4).Value = stc.EmployeeId;
                row++;

                ws.Cell(row, 1).Value = "Fonction :";
                ws.Cell(row, 1).Style.Font.Bold = true;
                ws.Cell(row, 2).Value = stc.Fonction;
                ws.Cell(row, 3).Value = "Classification :";
                ws.Cell(row, 3).Style.Font.Bold = true;
                ws.Cell(row, 4).Value = $"{stc.Categorie} - Éch. {stc.Echelon}";
                row++;

                ws.Cell(row, 1).Value = "Date d'entrée :";
                ws.Cell(row, 1).Style.Font.Bold = true;
                ws.Cell(row, 2).Value = stc.DateEntree.ToString("dd/MM/yyyy");
                ws.Cell(row, 3).Value = "Date de sortie :";
                ws.Cell(row, 3).Style.Font.Bold = true;
                ws.Cell(row, 4).Value = stc.DateSortie.ToString("dd/MM/yyyy");
                row++;

                ws.Cell(row, 1).Value = "Ancienneté :";
                ws.Cell(row, 1).Style.Font.Bold = true;
                ws.Cell(row, 2).Value = stc.AncienneteTexte;
                ws.Cell(row, 3).Value = "Motif :";
                ws.Cell(row, 3).Style.Font.Bold = true;
                ws.Cell(row, 4).Value = stc.MotifLibelle;
                row += 2;

                // En-têtes du tableau
                ws.Cell(row, 1).Value = "RUBRIQUE";
                ws.Cell(row, 2).Value = "DÉTAIL / BASE";
                ws.Cell(row, 3).Value = "RÉGIME FISCAL";
                ws.Cell(row, 4).Value = "MONTANT (FCFA)";
                ws.Range(row, 1, row, 4).Style.Font.Bold = true;
                ws.Range(row, 1, row, 4).Style.Fill.BackgroundColor = XLColor.FromArgb(20, 103, 90);
                ws.Range(row, 1, row, 4).Style.Font.FontColor = XLColor.White;
                row++;

                // Gains
                AddExcelRow(ws, ref row, "Dernier salaire (salaire de présence)", $"{stc.JoursPresence:0.##} jours", "Imposable", stc.SalairePresence);
                AddExcelRow(ws, ref row, "Indemnité compensatrice de congés payés", $"{stc.JoursCongesDus:0.##} jours", "Imposable", stc.IndemniteConges);
                AddExcelRow(ws, ref row, "Indemnité compensatrice de préavis", stc.StatutPreavisLibelle, "Imposable", stc.IndemnitePreavis);
                AddExcelRow(ws, ref row, "Indemnité de licenciement / de fin de service", $"Taux {stc.TauxLicenciementApplique}%", "Exonéré (Art. 106)", stc.IndemniteLicenciement);
                if (stc.Indemnite13emeMois > 0)
                    AddExcelRow(ws, ref row, "Gratification / 13ème mois", $"{stc.Mois13emeMois} mois", "Imposable", stc.Indemnite13emeMois);
                if (stc.IndemnitesDiverses > 0)
                    AddExcelRow(ws, ref row, stc.LibelleIndemnitesDiverses ?? "Indemnités diverses", "-", "Imposable", stc.IndemnitesDiverses);

                // Total Brut
                ws.Cell(row, 1).Value = "TOTAL BRUT DES INDEMNITÉS";
                ws.Cell(row, 1).Style.Font.Bold = true;
                ws.Cell(row, 4).Value = (double)stc.TotalBrut;
                ws.Cell(row, 4).Style.Font.Bold = true;
                ws.Cell(row, 4).Style.NumberFormat.Format = "#,##0";
                ws.Range(row, 1, row, 4).Style.Fill.BackgroundColor = XLColor.FromArgb(240, 243, 246);
                row++;

                // Retenues
                if (stc.EstimationIUTS > 0)
                    AddExcelRow(ws, ref row, "Retenue IUTS estimée", "-", "Retenue fiscale", -stc.EstimationIUTS);
                if (stc.EstimationCNSS > 0)
                    AddExcelRow(ws, ref row, "Retenue CNSS salariale (5.5%)", "-", "Cotisation sociale", -stc.EstimationCNSS);
                if (stc.DeductionsPrets > 0)
                    AddExcelRow(ws, ref row, "Remboursement de prêts en cours", "-", "Retenue sur net", -stc.DeductionsPrets);
                if (stc.DeductionsAutres > 0)
                    AddExcelRow(ws, ref row, stc.LibelleDeductionsAutres ?? "Autres déductions", "-", "Retenue sur net", -stc.DeductionsAutres);

                // Total Retenues
                ws.Cell(row, 1).Value = "TOTAL DES RETENUES ET DÉDUCTIONS";
                ws.Cell(row, 1).Style.Font.Bold = true;
                ws.Cell(row, 4).Value = (double)(-stc.TotalRetenues);
                ws.Cell(row, 4).Style.Font.Bold = true;
                ws.Cell(row, 4).Style.NumberFormat.Format = "#,##0";
                ws.Range(row, 1, row, 4).Style.Fill.BackgroundColor = XLColor.FromArgb(240, 243, 246);
                row++;

                // Net
                ws.Cell(row, 1).Value = "NET À PAYER POUR SOLDE DE TOUT COMPTE";
                ws.Cell(row, 1).Style.Font.Bold = true;
                ws.Cell(row, 1).Style.Font.FontSize = 11;
                ws.Cell(row, 4).Value = (double)stc.TotalNet;
                ws.Cell(row, 4).Style.Font.Bold = true;
                ws.Cell(row, 4).Style.Font.FontSize = 11;
                ws.Cell(row, 4).Style.NumberFormat.Format = "#,##0";
                ws.Range(row, 1, row, 4).Style.Fill.BackgroundColor = XLColor.FromArgb(220, 237, 230);
                row += 2;

                ws.Columns(1, 4).AdjustToContents();
                wb.SaveAs(cheminFichier);
            }

            return cheminFichier;
        }

        private static void AddExcelRow(IXLWorksheet ws, ref int row, string libelle, string detail, string regime, decimal montant)
        {
            ws.Cell(row, 1).Value = libelle;
            ws.Cell(row, 2).Value = detail;
            ws.Cell(row, 3).Value = regime;
            ws.Cell(row, 4).Value = (double)montant;
            ws.Cell(row, 4).Style.NumberFormat.Format = "#,##0";
            row++;
        }

        // ── Utilitaires PDF ───────────────────────────────────────────────────

        private static void AddInfoCell(PdfPTable table, string label, string value, bool bold)
        {
            var cellLbl = new PdfPCell(new Phrase(label, bold ? BoldFont : SmallFont))
            {
                BackgroundColor = LightBgColor,
                BorderColor = BorderColor,
                Padding = 4f
            };
            table.AddCell(cellLbl);

            var cellVal = new PdfPCell(new Phrase(value ?? "-", bold ? BoldFont : NormalFont))
            {
                BorderColor = BorderColor,
                Padding = 4f
            };
            table.AddCell(cellVal);
        }

        private static void AddHeaderCell(PdfPTable table, string text, int alignment = Element.ALIGN_LEFT)
        {
            var cell = new PdfPCell(new Phrase(text, new Font(Font.FontFamily.HELVETICA, 9.5f, Font.BOLD, BaseColor.WHITE)))
            {
                BackgroundColor = AccentColor,
                BorderColor = AccentColor,
                HorizontalAlignment = alignment,
                VerticalAlignment = Element.ALIGN_MIDDLE,
                Padding = 5f
            };
            table.AddCell(cell);
        }

        private static void AddFinancialRow(PdfPTable table, string label, string detail, string regime, decimal amount, bool highlight = false)
        {
            var bg = highlight ? new BaseColor(241, 245, 249) : BaseColor.WHITE;

            table.AddCell(new PdfPCell(new Phrase(label, NormalFont))
            {
                BackgroundColor = bg,
                BorderColor = BorderColor,
                Padding = 4.5f
            });

            table.AddCell(new PdfPCell(new Phrase(detail, SmallFont))
            {
                BackgroundColor = bg,
                BorderColor = BorderColor,
                HorizontalAlignment = Element.ALIGN_CENTER,
                Padding = 4.5f
            });

            table.AddCell(new PdfPCell(new Phrase(regime, SmallFont))
            {
                BackgroundColor = bg,
                BorderColor = BorderColor,
                HorizontalAlignment = Element.ALIGN_CENTER,
                Padding = 4.5f
            });

            table.AddCell(new PdfPCell(new Phrase($"{amount:N0}", BoldFont))
            {
                BackgroundColor = bg,
                BorderColor = BorderColor,
                HorizontalAlignment = Element.ALIGN_RIGHT,
                Padding = 4.5f
            });
        }

        private static void AddTotalRow(PdfPTable table, string label, decimal amount)
        {
            var bg = new BaseColor(237, 242, 247);

            var cellLbl = new PdfPCell(new Phrase(label, BoldFont))
            {
                Colspan = 3,
                BackgroundColor = bg,
                BorderColor = BorderColor,
                Padding = 5f
            };
            table.AddCell(cellLbl);

            var cellVal = new PdfPCell(new Phrase($"{amount:N0}", BoldFont))
            {
                BackgroundColor = bg,
                BorderColor = BorderColor,
                HorizontalAlignment = Element.ALIGN_RIGHT,
                Padding = 5f
            };
            table.AddCell(cellVal);
        }

        private static void AddGrandTotalRow(PdfPTable table, string label, decimal amount)
        {
            var bg = new BaseColor(209, 231, 221); // Pale green

            var cellLbl = new PdfPCell(new Phrase(label, new Font(Font.FontFamily.HELVETICA, 10.5f, Font.BOLD, PrimaryColor)))
            {
                Colspan = 3,
                BackgroundColor = bg,
                BorderColor = AccentColor,
                Padding = 6f
            };
            table.AddCell(cellLbl);

            var cellVal = new PdfPCell(new Phrase($"{amount:N0} FCFA", new Font(Font.FontFamily.HELVETICA, 11f, Font.BOLD, AccentColor)))
            {
                BackgroundColor = bg,
                BorderColor = AccentColor,
                HorizontalAlignment = Element.ALIGN_RIGHT,
                Padding = 6f
            };
            table.AddCell(cellVal);
        }
    }

    /// <summary>
    /// Helper de conversion de montants numériques en texte français (pour les mentions légales de chèques/reçus).
    /// </summary>
    public static class NombreEnLettresHelper
    {
        private static readonly string[] Unites = { "zéro", "un", "deux", "trois", "quatre", "cinq", "six", "sept", "huit", "neuf", "dix", "onze", "douze", "treize", "quatorze", "quinze", "seize", "dix-sept", "dix-huit", "dix-neuf" };
        private static readonly string[] Dizaines = { "", "dix", "vingt", "trente", "quarante", "cinquante", "soixante", "soixante-dix", "quatre-vingts", "quatre-vingt-dix" };

        public static string Formater(decimal montant)
        {
            long nombre = (long)Math.Round(montant, 0);
            if (nombre == 0) return "zéro";
            if (nombre < 0) return "moins " + Formater(-nombre);

            return Convertir(nombre).Trim();
        }

        private static string Convertir(long n)
        {
            if (n < 20) return Unites[n];
            if (n < 100)
            {
                int diz = (int)(n / 10);
                int reste = (int)(n % 10);
                if (diz == 7) return "soixante-" + (reste == 1 ? "et-onze" : Convertir(10 + reste));
                if (diz == 9) return "quatre-vingt-" + Convertir(10 + reste);
                if (reste == 0) return Dizaines[diz];
                if (reste == 1 && diz != 8) return Dizaines[diz] + " et un";
                return Dizaines[diz] + "-" + Unites[reste];
            }
            if (n < 1000)
            {
                int cent = (int)(n / 100);
                int reste = (int)(n % 100);
                string prefix = cent == 1 ? "cent" : Unites[cent] + " cent" + (reste == 0 ? "s" : "");
                return reste == 0 ? prefix : prefix + " " + Convertir(reste);
            }
            if (n < 1000000)
            {
                long mille = n / 1000;
                long reste = n % 1000;
                string prefix = mille == 1 ? "mille" : Convertir(mille) + " mille";
                return reste == 0 ? prefix : prefix + " " + Convertir(reste);
            }
            if (n < 1000000000)
            {
                long million = n / 1000000;
                long reste = n % 1000000;
                string prefix = million == 1 ? "un million" : Convertir(million) + " millions";
                return reste == 0 ? prefix : prefix + " " + Convertir(reste);
            }

            long milliard = n / 1000000000;
            long resteM = n % 1000000000;
            string pM = milliard == 1 ? "un milliard" : Convertir(milliard) + " milliards";
            return resteM == 0 ? pM : pM + " " + Convertir(resteM);
        }
    }
}
