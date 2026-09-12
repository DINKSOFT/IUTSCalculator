using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ClosedXML.Excel;
using iTextSharp.text;
using iTextSharp.text.pdf;
using IUTSCalculator.Models;

namespace IUTSCalculator.Services
{
    /// <summary>
    /// Exporte un <see cref="Bulletin"/> de paie au format PDF (iTextSharp) ou Excel (ClosedXML),
    /// selon l'un des modèles de mise en page définis par <see cref="BulletinTemplate"/> : le
    /// modèle détaillé reproduit le modèle de référence (fichier "model_buletin.xlsx", onglet
    /// "Option 4"), et trois modèles complémentaires (condensé, avec cumuls annuels, bilingue)
    /// réutilisent les mêmes données. Chaque document porte un numéro de contrôle unique (et,
    /// en PDF, un QR code correspondant) permettant d'en vérifier l'authenticité.
    /// </summary>
    public class BulletinExportService
    {
        private readonly CultureInfo _culture = new CultureInfo("fr-FR");
        private const string CurrencyFormat = "#,##0";

        /// <summary>Regroupe les polices utilisées dans un export PDF, pour éviter de les recréer/re-passer une à une.</summary>
        private class PdfFonts
        {
            public Font Title;
            public Font SubTitle;
            public Font Section;
            public Font Label;
            public Font LabelBold;
            public Font Value;
            public Font ValueBold;
            public Font Total;
            public Font CompanyName;
            public Font CompanyInfo;
            public Font Small;
        }

        #region PDF

        public async Task<bool> ExportToPdfAsync(Bulletin bulletin, string filePath, BulletinTemplate template = BulletinTemplate.Detaille)
        {
            return await Task.Run(() => ExportToPdf(bulletin, filePath, template));
        }

        public bool ExportToPdf(Bulletin bulletin, string filePath, BulletinTemplate template = BulletinTemplate.Detaille)
        {
            if (bulletin == null || string.IsNullOrEmpty(filePath))
                return false;

            Document document = null;
            PdfWriter writer = null;
            FileStream fs = null;

            try
            {
                string directory = Path.GetDirectoryName(filePath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                    Directory.CreateDirectory(directory);

                fs = new FileStream(filePath, FileMode.Create);
                document = new Document(PageSize.A4, 40, 40, 40, 40);
                writer = PdfWriter.GetInstance(document, fs);
                document.Open();

                document.AddTitle("Bulletin de paye");
                document.AddSubject($"Bulletin de paie - {bulletin.NomComplet} - {bulletin.PeriodeLibelle}");
                document.AddCreator("IUTSCalculator");
                document.AddAuthor("IUTSCalculator");

                var f = CreerPolices();

                EcrireEnTetePdf(document, bulletin, f, template);

                switch (template)
                {
                    case BulletinTemplate.Condense:
                        EcrireCorpsCondensePdf(document, bulletin, f);
                        break;
                    case BulletinTemplate.CumulsAnnuels:
                        EcrireCorpsDetaillePdf(document, bulletin, f, avecCumuls: true);
                        break;
                    case BulletinTemplate.Bilingue:
                        EcrireCorpsBilinguePdf(document, bulletin, f);
                        break;
                    case BulletinTemplate.Detaille:
                    default:
                        EcrireCorpsDetaillePdf(document, bulletin, f, avecCumuls: false);
                        break;
                }

                AjouterControlePdf(document, bulletin, f);

                document.Close();
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Erreur lors de l'export PDF du bulletin : {ex.Message}");
                return false;
            }
            finally
            {
                if (document != null && document.IsOpen()) document.Close();
                writer?.Close();
                if (fs != null) { fs.Close(); fs.Dispose(); }
            }
        }

        private PdfFonts CreerPolices()
        {
            return new PdfFonts
            {
                Title = new Font(Font.FontFamily.HELVETICA, 16, Font.BOLD),
                SubTitle = new Font(Font.FontFamily.HELVETICA, 10, Font.ITALIC),
                Section = new Font(Font.FontFamily.HELVETICA, 11, Font.BOLD, BaseColor.WHITE),
                Label = new Font(Font.FontFamily.HELVETICA, 10),
                LabelBold = new Font(Font.FontFamily.HELVETICA, 10, Font.BOLD),
                Value = new Font(Font.FontFamily.HELVETICA, 10),
                ValueBold = new Font(Font.FontFamily.HELVETICA, 10, Font.BOLD),
                Total = new Font(Font.FontFamily.HELVETICA, 12, Font.BOLD),
                CompanyName = new Font(Font.FontFamily.HELVETICA, 12, Font.BOLD),
                CompanyInfo = new Font(Font.FontFamily.HELVETICA, 8, Font.NORMAL),
                Small = new Font(Font.FontFamily.HELVETICA, 7, Font.ITALIC)
            };
        }

        /// <summary>En-tête commun à tous les modèles : informations de l'entreprise + logo (gauche), titre + période (droite).</summary>
        private void EcrireEnTetePdf(Document document, Bulletin bulletin, PdfFonts f, BulletinTemplate template)
        {
            var headerTable = new PdfPTable(2) { WidthPercentage = 100, SpacingAfter = 6f };
            headerTable.SetWidths(new float[] { 1.4f, 1f });

            var enteteGaucheCell = new PdfPCell { Border = Rectangle.NO_BORDER, Padding = 0f, VerticalAlignment = Element.ALIGN_TOP };
            if (bulletin.HasLogo)
            {
                try
                {
                    var logo = iTextSharp.text.Image.GetInstance(bulletin.EntrepriseLogoPath);
                    logo.ScaleToFit(70f, 70f);
                    logo.SpacingAfter = 4f;
                    enteteGaucheCell.AddElement(logo);
                }
                catch
                {
                    // Logo illisible ou format non supporté : on l'ignore silencieusement.
                }
            }
            if (!string.IsNullOrWhiteSpace(bulletin.EntrepriseRaisonSociale))
                enteteGaucheCell.AddElement(new Paragraph(bulletin.EntrepriseRaisonSociale, f.CompanyName));
            if (!string.IsNullOrWhiteSpace(bulletin.EntrepriseAdresseLigne))
                enteteGaucheCell.AddElement(new Paragraph(bulletin.EntrepriseAdresseLigne, f.CompanyInfo));
            if (!string.IsNullOrWhiteSpace(bulletin.EntrepriseTelephone))
                enteteGaucheCell.AddElement(new Paragraph($"Tél : {bulletin.EntrepriseTelephone}", f.CompanyInfo));
            if (!string.IsNullOrWhiteSpace(bulletin.EntrepriseIdentifiants))
                enteteGaucheCell.AddElement(new Paragraph(bulletin.EntrepriseIdentifiants, f.CompanyInfo));
            if (!string.IsNullOrWhiteSpace(bulletin.EntrepriseRegimeFiscal))
                enteteGaucheCell.AddElement(new Paragraph($"Régime fiscal : {bulletin.EntrepriseRegimeFiscal}", f.CompanyInfo));
            headerTable.AddCell(enteteGaucheCell);

            string titre = template == BulletinTemplate.Bilingue ? "BULLETIN DE PAYE / PAY SLIP" : "BULLETIN DE PAYE";
            string periodeLabel = template == BulletinTemplate.Bilingue
                ? $"Période / Period : {bulletin.PeriodeLibelle}"
                : $"Période du : {bulletin.PeriodeLibelle}";

            var enteteDroiteCell = new PdfPCell { Border = Rectangle.NO_BORDER, Padding = 0f, VerticalAlignment = Element.ALIGN_TOP };
            enteteDroiteCell.AddElement(new Paragraph(titre, f.Title) { Alignment = Element.ALIGN_RIGHT });
            enteteDroiteCell.AddElement(new Paragraph(periodeLabel, f.SubTitle) { Alignment = Element.ALIGN_RIGHT, SpacingBefore = 4f });
            headerTable.AddCell(enteteDroiteCell);

            document.Add(headerTable);

            var headerSeparator = new PdfPTable(1) { WidthPercentage = 100, SpacingAfter = 10f };
            headerSeparator.AddCell(new PdfPCell
            {
                Border = Rectangle.BOTTOM_BORDER,
                BorderColor = BaseColor.GRAY,
                BorderWidth = 1f,
                FixedHeight = 2f
            });
            document.Add(headerSeparator);
        }

        /// <summary>Corps complet du bulletin (modèle "Option 4"), avec section cumuls annuels optionnelle.</summary>
        private void EcrireCorpsDetaillePdf(Document document, Bulletin bulletin, PdfFonts f, bool avecCumuls)
        {
            // ---- Bloc identité ----
            var infoTable = new PdfPTable(4) { WidthPercentage = 100, SpacingAfter = 10f };
            infoTable.SetWidths(new float[] { 1.3f, 2f, 1.3f, 2f });
            AddCell(infoTable, "Employé", f.LabelBold, BaseColor.LIGHT_GRAY);
            AddCell(infoTable, bulletin.NomComplet, f.Value);
            AddCell(infoTable, "Emploi", f.LabelBold, BaseColor.LIGHT_GRAY);
            AddCell(infoTable, bulletin.Emploi ?? "-", f.Value);

            AddCell(infoTable, "Date d'embauche", f.LabelBold, BaseColor.LIGHT_GRAY);
            AddCell(infoTable, bulletin.DateEmbauche?.ToString("dd/MM/yyyy", _culture) ?? "-", f.Value);
            AddCell(infoTable, "Ancienneté (mois)", f.LabelBold, BaseColor.LIGHT_GRAY);
            AddCell(infoTable, bulletin.AncienneteMois.ToString(_culture), f.Value);

            AddCell(infoTable, "Congé acquis (jours)", f.LabelBold, BaseColor.LIGHT_GRAY);
            AddCell(infoTable, bulletin.CongesAcquisJours.ToString("0.##", _culture), f.Value);
            AddCell(infoTable, "Congé pris (jours)", f.LabelBold, BaseColor.LIGHT_GRAY);
            AddCell(infoTable, bulletin.CongesPrisJours.ToString("0.##", _culture), f.Value);

            AddCell(infoTable, "Congé cumulatif (jours)", f.LabelBold, BaseColor.LIGHT_GRAY);
            AddCell(infoTable, bulletin.CongesCumulesJours.ToString("0.##", _culture), f.Value);
            AddCell(infoTable, "Charges familiales", f.LabelBold, BaseColor.LIGHT_GRAY);
            AddCell(infoTable, bulletin.ChargesFamiliales.ToString(_culture), f.Value);
            document.Add(infoTable);

            // ---- Gains ----
            document.Add(SectionHeader("GAINS", f.Section));
            var gainsTable = new PdfPTable(2) { WidthPercentage = 100, SpacingAfter = 10f };
            gainsTable.SetWidths(new float[] { 3f, 1.3f });
            AddLigne(gainsTable, "Salaire de base", bulletin.SalaireDeBase, f.Label, f.Value);
            AddLigne(gainsTable, "Indemnité de logement", bulletin.IndemniteLogement, f.Label, f.Value);
            AddLigne(gainsTable, "Indemnité de transport", bulletin.IndemniteTransport, f.Label, f.Value);
            AddLigne(gainsTable, "Indemnité de fonction", bulletin.IndemniteFonction, f.Label, f.Value);
            AddLigne(gainsTable, "Indemnité de sujétion", bulletin.IndemniteSujetion, f.Label, f.Value);
            AddLigne(gainsTable, "SALAIRE BRUT", bulletin.SalaireBrut, f.LabelBold, f.ValueBold, BaseColor.LIGHT_GRAY);
            document.Add(gainsTable);

            // ---- Retenues / impôt ----
            document.Add(SectionHeader("COTISATIONS ET IMPÔT", f.Section));
            var retenuesTable = new PdfPTable(2) { WidthPercentage = 100, SpacingAfter = 10f };
            retenuesTable.SetWidths(new float[] { 3f, 1.3f });
            AddLigne(retenuesTable, "CNSS part employé", bulletin.CnssEmploye, f.Label, f.Value);
            AddLigne(retenuesTable, "CNSS part employeur", bulletin.CnssEmployeur, f.Label, f.Value);
            AddLigne(retenuesTable, "Total exonérations (Art.106 CGI)", bulletin.TotalExonerations, f.Label, f.Value);
            AddLigne(retenuesTable, "Abattement forfaitaire (frais professionnels)", bulletin.AbattementForfaitaire, f.Label, f.Value);
            AddLigne(retenuesTable, "Base IUTS", bulletin.BaseIUTS, f.Label, f.Value);
            AddLigne(retenuesTable, "IUTS brut", bulletin.IUTSBrut, f.Label, f.Value);
            AddLigne(retenuesTable, $"Abattement charges familiales ({bulletin.ChargesFamiliales})", bulletin.AbattementChargesFamiliales, f.Label, f.Value);
            AddLigne(retenuesTable, "NET IUTS", bulletin.NetIUTS, f.LabelBold, f.ValueBold);
            AddLigne(retenuesTable, "Retenue obligatoire sur salaire", bulletin.RetenueObligatoire, f.Label, f.Value);
            document.Add(retenuesTable);

            // ---- Résultat ----
            var netTable = new PdfPTable(2) { WidthPercentage = 100, SpacingAfter = 14f };
            netTable.SetWidths(new float[] { 3f, 1.3f });
            var netLabelCell = new PdfPCell(new Phrase("SALAIRE NET À PAYER", f.Total))
            {
                BackgroundColor = new BaseColor(220, 235, 220),
                Padding = 8f,
                HorizontalAlignment = Element.ALIGN_LEFT,
                VerticalAlignment = Element.ALIGN_MIDDLE
            };
            var netValueCell = new PdfPCell(new Phrase(FormatAmount(bulletin.SalaireNetAPayer), f.Total))
            {
                BackgroundColor = new BaseColor(220, 235, 220),
                Padding = 8f,
                HorizontalAlignment = Element.ALIGN_RIGHT,
                VerticalAlignment = Element.ALIGN_MIDDLE
            };
            netTable.AddCell(netLabelCell);
            netTable.AddCell(netValueCell);
            document.Add(netTable);

            if (avecCumuls)
                EcrireCumulsAnnuelsPdf(document, bulletin, f);

            // ---- Règlement ----
            var reglementPara = new Paragraph(
                $"Mode de règlement : {bulletin.ModeReglement}" +
                (string.IsNullOrWhiteSpace(bulletin.NumeroCompte) ? "" : $" — Compte : {bulletin.NumeroCompte}"),
                f.Label)
            { SpacingAfter = 24f };
            document.Add(reglementPara);

            // ---- Signatures ----
            var signTable = new PdfPTable(2) { WidthPercentage = 100, SpacingBefore = 20f };
            var empCell = new PdfPCell(new Phrase("\n\n\nL'employeur", f.Label)) { Border = Rectangle.TOP_BORDER, PaddingTop = 4f };
            var salCell = new PdfPCell(new Phrase("\n\n\nSignature de l'employé(e)", f.Label)) { Border = Rectangle.TOP_BORDER, PaddingTop = 4f };
            signTable.AddCell(empCell);
            signTable.AddCell(salCell);
            document.Add(signTable);

            var mention = new Paragraph("Ce bulletin de salaire doit être conservé sans limitation de durée", f.Small)
            { SpacingBefore = 16f, Alignment = Element.ALIGN_CENTER };
            document.Add(mention);
        }

        /// <summary>Section "CUMULS DEPUIS JANVIER", insérée après le résultat dans le modèle CumulsAnnuels.</summary>
        private void EcrireCumulsAnnuelsPdf(Document document, Bulletin bulletin, PdfFonts f)
        {
            string titreSection = $"CUMULS DEPUIS JANVIER {bulletin.PeriodeFin.Year}" + (bulletin.CumulsEstimes ? " (estimés)" : "");
            document.Add(SectionHeader(titreSection, f.Section));
            var cumulsTable = new PdfPTable(2) { WidthPercentage = 100, SpacingAfter = 10f };
            cumulsTable.SetWidths(new float[] { 3f, 1.3f });
            AddLigne(cumulsTable, "Cumul salaire brut", bulletin.CumulBrutAnnuel, f.Label, f.Value);
            AddLigne(cumulsTable, "Cumul CNSS part employé", bulletin.CumulCnssEmployeAnnuel, f.Label, f.Value);
            AddLigne(cumulsTable, "Cumul Net IUTS", bulletin.CumulIUTSAnnuel, f.Label, f.Value);
            AddLigne(cumulsTable, "Cumul salaire net à payer", bulletin.CumulNetAnnuel, f.LabelBold, f.ValueBold, BaseColor.LIGHT_GRAY);
            document.Add(cumulsTable);
            if (bulletin.CumulsEstimes)
            {
                var note = new Paragraph("Cumuls estimés à partir de la rémunération du mois en cours, faute d'historique de paie détaillé.", f.Small)
                { SpacingAfter = 8f };
                document.Add(note);
            }
        }

        /// <summary>Corps condensé (demi-page) : identité minimale + totaux essentiels uniquement.</summary>
        private void EcrireCorpsCondensePdf(Document document, Bulletin bulletin, PdfFonts f)
        {
            var infoTable = new PdfPTable(4) { WidthPercentage = 100, SpacingAfter = 8f };
            infoTable.SetWidths(new float[] { 1.3f, 2f, 1.3f, 2f });
            AddCell(infoTable, "Employé", f.LabelBold, BaseColor.LIGHT_GRAY);
            AddCell(infoTable, bulletin.NomComplet, f.Value);
            AddCell(infoTable, "Période", f.LabelBold, BaseColor.LIGHT_GRAY);
            AddCell(infoTable, bulletin.PeriodeLibelle, f.Value);
            document.Add(infoTable);

            var totauxTable = new PdfPTable(2) { WidthPercentage = 100, SpacingAfter = 10f };
            totauxTable.SetWidths(new float[] { 3f, 1.3f });
            AddLigne(totauxTable, "Salaire brut", bulletin.SalaireBrut, f.Label, f.Value);
            decimal totalRetenues = bulletin.CnssEmploye + bulletin.NetIUTS + bulletin.RetenueObligatoire;
            AddLigne(totauxTable, "Total des retenues (CNSS + IUTS + retenue obligatoire)", totalRetenues, f.Label, f.Value);
            AddLigne(totauxTable, "SALAIRE NET À PAYER", bulletin.SalaireNetAPayer, f.LabelBold, f.ValueBold, new BaseColor(220, 235, 220));
            document.Add(totauxTable);

            var reglementPara = new Paragraph(
                $"Mode de règlement : {bulletin.ModeReglement}" +
                (string.IsNullOrWhiteSpace(bulletin.NumeroCompte) ? "" : $" — Compte : {bulletin.NumeroCompte}"),
                f.Label)
            { SpacingAfter = 10f };
            document.Add(reglementPara);
        }

        /// <summary>Corps bilingue français / anglais.</summary>
        private void EcrireCorpsBilinguePdf(Document document, Bulletin bulletin, PdfFonts f)
        {
            var infoTable = new PdfPTable(4) { WidthPercentage = 100, SpacingAfter = 10f };
            infoTable.SetWidths(new float[] { 1.6f, 2f, 1.6f, 2f });
            AddCell(infoTable, "Employé / Employee", f.LabelBold, BaseColor.LIGHT_GRAY);
            AddCell(infoTable, bulletin.NomComplet, f.Value);
            AddCell(infoTable, "Emploi / Position", f.LabelBold, BaseColor.LIGHT_GRAY);
            AddCell(infoTable, bulletin.Emploi ?? "-", f.Value);

            AddCell(infoTable, "Date d'embauche / Hire date", f.LabelBold, BaseColor.LIGHT_GRAY);
            AddCell(infoTable, bulletin.DateEmbauche?.ToString("dd/MM/yyyy", _culture) ?? "-", f.Value);
            AddCell(infoTable, "Ancienneté (mois) / Seniority (months)", f.LabelBold, BaseColor.LIGHT_GRAY);
            AddCell(infoTable, bulletin.AncienneteMois.ToString(_culture), f.Value);
            document.Add(infoTable);

            document.Add(SectionHeader("GAINS / EARNINGS", f.Section));
            var gainsTable = new PdfPTable(2) { WidthPercentage = 100, SpacingAfter = 10f };
            gainsTable.SetWidths(new float[] { 3f, 1.3f });
            AddLigne(gainsTable, "Salaire de base / Base salary", bulletin.SalaireDeBase, f.Label, f.Value);
            AddLigne(gainsTable, "Indemnité de logement / Housing allowance", bulletin.IndemniteLogement, f.Label, f.Value);
            AddLigne(gainsTable, "Indemnité de transport / Transport allowance", bulletin.IndemniteTransport, f.Label, f.Value);
            AddLigne(gainsTable, "Indemnité de fonction / Function allowance", bulletin.IndemniteFonction, f.Label, f.Value);
            AddLigne(gainsTable, "Indemnité de sujétion / Special duty allowance", bulletin.IndemniteSujetion, f.Label, f.Value);
            AddLigne(gainsTable, "SALAIRE BRUT / GROSS SALARY", bulletin.SalaireBrut, f.LabelBold, f.ValueBold, BaseColor.LIGHT_GRAY);
            document.Add(gainsTable);

            document.Add(SectionHeader("COTISATIONS ET IMPÔT / DEDUCTIONS & TAX", f.Section));
            var retenuesTable = new PdfPTable(2) { WidthPercentage = 100, SpacingAfter = 10f };
            retenuesTable.SetWidths(new float[] { 3f, 1.3f });
            AddLigne(retenuesTable, "CNSS part employé / Employee social security", bulletin.CnssEmploye, f.Label, f.Value);
            AddLigne(retenuesTable, "CNSS part employeur / Employer social security", bulletin.CnssEmployeur, f.Label, f.Value);
            AddLigne(retenuesTable, "Base IUTS / Taxable base", bulletin.BaseIUTS, f.Label, f.Value);
            AddLigne(retenuesTable, "NET IUTS / Net income tax", bulletin.NetIUTS, f.LabelBold, f.ValueBold);
            AddLigne(retenuesTable, "Retenue obligatoire / Mandatory withholding", bulletin.RetenueObligatoire, f.Label, f.Value);
            document.Add(retenuesTable);

            var netTable = new PdfPTable(2) { WidthPercentage = 100, SpacingAfter = 14f };
            netTable.SetWidths(new float[] { 3f, 1.3f });
            var netLabelCell = new PdfPCell(new Phrase("SALAIRE NET À PAYER / NET PAY", f.Total))
            {
                BackgroundColor = new BaseColor(220, 235, 220),
                Padding = 8f,
                HorizontalAlignment = Element.ALIGN_LEFT,
                VerticalAlignment = Element.ALIGN_MIDDLE
            };
            var netValueCell = new PdfPCell(new Phrase(FormatAmount(bulletin.SalaireNetAPayer), f.Total))
            {
                BackgroundColor = new BaseColor(220, 235, 220),
                Padding = 8f,
                HorizontalAlignment = Element.ALIGN_RIGHT,
                VerticalAlignment = Element.ALIGN_MIDDLE
            };
            netTable.AddCell(netLabelCell);
            netTable.AddCell(netValueCell);
            document.Add(netTable);

            var reglementPara = new Paragraph(
                $"Mode de règlement / Payment method : {bulletin.ModeReglement}" +
                (string.IsNullOrWhiteSpace(bulletin.NumeroCompte) ? "" : $" — Compte / Account : {bulletin.NumeroCompte}"),
                f.Label)
            { SpacingAfter = 24f };
            document.Add(reglementPara);

            var signTable = new PdfPTable(2) { WidthPercentage = 100, SpacingBefore = 20f };
            signTable.AddCell(new PdfPCell(new Phrase("\n\n\nL'employeur / Employer", f.Label)) { Border = Rectangle.TOP_BORDER, PaddingTop = 4f });
            signTable.AddCell(new PdfPCell(new Phrase("\n\n\nSignature de l'employé(e) / Employee signature", f.Label)) { Border = Rectangle.TOP_BORDER, PaddingTop = 4f });
            document.Add(signTable);

            var mention = new Paragraph("Ce bulletin de salaire doit être conservé sans limitation de durée / This pay slip must be kept indefinitely", f.Small)
            { SpacingBefore = 16f, Alignment = Element.ALIGN_CENTER };
            document.Add(mention);
        }

        /// <summary>Pied de contrôle commun à tous les modèles de bulletin : numéro de contrôle + QR code de vérification.</summary>
        private void AjouterControlePdf(Document document, Bulletin bulletin, PdfFonts f)
        {
            if (string.IsNullOrWhiteSpace(bulletin.NumeroControle))
                return;

            string contenuVerification = QrCodeService.ConstruireContenuVerification(
                "Bulletin de paie", bulletin.EmployeeId ?? bulletin.NomComplet, bulletin.PeriodeLibelle, bulletin.SalaireNetAPayer, bulletin.NumeroControle);

            AjouterControlePdf(document, bulletin.NumeroControle, contenuVerification,
                "Ce numéro (et le QR code ci-contre) permettent de vérifier l'authenticité de ce bulletin auprès de l'employeur.", f);
        }

        /// <summary>Pied de contrôle générique (utilisé aussi par le registre de paie) : numéro de contrôle + QR code de vérification.</summary>
        private void AjouterControlePdf(Document document, string numeroControle, string contenuVerification, string mentionAide, PdfFonts f)
        {
            if (string.IsNullOrWhiteSpace(numeroControle))
                return;

            var controleTable = new PdfPTable(2) { WidthPercentage = 100, SpacingBefore = 16f };
            controleTable.SetWidths(new float[] { 1f, 4f });

            var qrCell = new PdfPCell { Border = Rectangle.NO_BORDER, Padding = 0f, VerticalAlignment = Element.ALIGN_MIDDLE };
            var qrImage = QrCodeService.GenererImageQrCodePdf(contenuVerification);
            if (qrImage != null)
            {
                qrImage.ScaleAbsolute(50f, 50f);
                qrCell.AddElement(qrImage);
            }
            controleTable.AddCell(qrCell);

            var texteCell = new PdfPCell { Border = Rectangle.NO_BORDER, Padding = 0f, VerticalAlignment = Element.ALIGN_MIDDLE };
            texteCell.AddElement(new Paragraph($"N° de contrôle : {numeroControle}", f.LabelBold));
            texteCell.AddElement(new Paragraph(mentionAide, f.Small));
            controleTable.AddCell(texteCell);

            document.Add(controleTable);
        }

        private Paragraph SectionHeader(string text, Font font)
        {
            var p = new Paragraph(text, font)
            {
                SpacingBefore = 8f,
                SpacingAfter = 4f
            };
            return p;
        }

        private void AddCell(PdfPTable table, string text, Font font, BaseColor background = null)
        {
            var cell = new PdfPCell(new Phrase(text, font))
            {
                Padding = 5f,
                VerticalAlignment = Element.ALIGN_MIDDLE
            };
            if (background != null) cell.BackgroundColor = background;
            table.AddCell(cell);
        }

        private void AddLigne(PdfPTable table, string label, decimal montant, Font labelFont, Font valueFont, BaseColor background = null)
        {
            var labelCell = new PdfPCell(new Phrase(label, labelFont)) { Padding = 5f, HorizontalAlignment = Element.ALIGN_LEFT };
            var valueCell = new PdfPCell(new Phrase(FormatAmount(montant), valueFont)) { Padding = 5f, HorizontalAlignment = Element.ALIGN_RIGHT };
            if (background != null) { labelCell.BackgroundColor = background; valueCell.BackgroundColor = background; }
            table.AddCell(labelCell);
            table.AddCell(valueCell);
        }

        private string FormatAmount(decimal montant) => montant.ToString(CurrencyFormat, _culture) + " FCFA";

        #endregion

        #region Excel

        public async Task<bool> ExportToExcelAsync(Bulletin bulletin, string filePath, BulletinTemplate template = BulletinTemplate.Detaille)
        {
            return await Task.Run(() => ExportToExcel(bulletin, filePath, template));
        }

        public bool ExportToExcel(Bulletin bulletin, string filePath, BulletinTemplate template = BulletinTemplate.Detaille)
        {
            if (bulletin == null || string.IsNullOrEmpty(filePath))
                return false;

            try
            {
                string directory = Path.GetDirectoryName(filePath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                    Directory.CreateDirectory(directory);

                using (var workbook = new XLWorkbook())
                {
                    var ws = workbook.Worksheets.Add("Bulletin de paye");
                    int row = 1;

                    row = EcrireEnTeteExcel(ws, bulletin, row, template);

                    switch (template)
                    {
                        case BulletinTemplate.Condense:
                            row = EcrireCorpsCondenseExcel(ws, bulletin, row);
                            break;
                        case BulletinTemplate.CumulsAnnuels:
                            row = EcrireCorpsDetailleExcel(ws, bulletin, row, avecCumuls: true);
                            break;
                        case BulletinTemplate.Bilingue:
                            row = EcrireCorpsBilingueExcel(ws, bulletin, row);
                            break;
                        case BulletinTemplate.Detaille:
                        default:
                            row = EcrireCorpsDetailleExcel(ws, bulletin, row, avecCumuls: false);
                            break;
                    }

                    row = EcrireControleExcel(ws, bulletin, row);

                    ws.Column(1).Width = 40;
                    ws.Column(2).Width = 20;
                    ws.Column(3).Width = 26;
                    ws.Column(4).Width = 20;

                    workbook.SaveAs(filePath);
                }

                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Erreur lors de l'export Excel du bulletin : {ex.Message}");
                return false;
            }
        }

        /// <summary>En-tête commun à tous les modèles : logo + informations de l'entreprise (gauche), titre + période (droite).</summary>
        private int EcrireEnTeteExcel(IXLWorksheet ws, Bulletin bulletin, int row, BulletinTemplate template)
        {
            if (bulletin.HasLogo)
            {
                try
                {
                    ws.AddPicture(bulletin.EntrepriseLogoPath)
                      .MoveTo(ws.Cell(row, 1))
                      .WithSize(60, 60);
                }
                catch
                {
                    // Logo illisible ou format non supporté : on l'ignore silencieusement.
                }
            }

            ws.Cell(row, 2).Value = bulletin.EntrepriseRaisonSociale ?? "";
            ws.Cell(row, 2).Style.Font.Bold = true;
            ws.Cell(row, 2).Style.Font.FontSize = 12;

            ws.Cell(row, 3).Value = template == BulletinTemplate.Bilingue ? "BULLETIN DE PAYE / PAY SLIP" : "BULLETIN DE PAYE";
            ws.Range(row, 3, row, 4).Merge();
            ws.Cell(row, 3).Style.Font.Bold = true;
            ws.Cell(row, 3).Style.Font.FontSize = 16;
            ws.Cell(row, 3).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;
            row++;

            if (!string.IsNullOrWhiteSpace(bulletin.EntrepriseAdresseLigne))
                ws.Cell(row, 2).Value = bulletin.EntrepriseAdresseLigne;

            ws.Cell(row, 3).Value = template == BulletinTemplate.Bilingue
                ? $"Période / Period : {bulletin.PeriodeLibelle}"
                : $"Période du : {bulletin.PeriodeLibelle}";
            ws.Range(row, 3, row, 4).Merge();
            ws.Cell(row, 3).Style.Font.Italic = true;
            ws.Cell(row, 3).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;
            row++;

            if (!string.IsNullOrWhiteSpace(bulletin.EntrepriseTelephone))
            {
                ws.Cell(row, 2).Value = $"Tél : {bulletin.EntrepriseTelephone}";
                row++;
            }

            if (!string.IsNullOrWhiteSpace(bulletin.EntrepriseIdentifiants))
            {
                ws.Cell(row, 2).Value = bulletin.EntrepriseIdentifiants;
                row++;
            }

            if (!string.IsNullOrWhiteSpace(bulletin.EntrepriseRegimeFiscal))
            {
                ws.Cell(row, 2).Value = $"Régime fiscal : {bulletin.EntrepriseRegimeFiscal}";
                row++;
            }

            row++;
            return row;
        }

        /// <summary>Corps complet du bulletin (modèle "Option 4"), avec section cumuls annuels optionnelle.</summary>
        private int EcrireCorpsDetailleExcel(IXLWorksheet ws, Bulletin bulletin, int row, bool avecCumuls)
        {
            WriteLabelValue(ws, ref row, "Employé", bulletin.NomComplet, "Emploi", bulletin.Emploi);
            WriteLabelValue(ws, ref row, "Date d'embauche", bulletin.DateEmbauche?.ToString("dd/MM/yyyy") ?? "-", "Ancienneté (mois)", bulletin.AncienneteMois.ToString());
            WriteLabelValue(ws, ref row, "Congé acquis (jours)", bulletin.CongesAcquisJours.ToString("0.##"), "Congé pris (jours)", bulletin.CongesPrisJours.ToString("0.##"));
            WriteLabelValue(ws, ref row, "Congé cumulatif (jours)", bulletin.CongesCumulesJours.ToString("0.##"), "Charges familiales", bulletin.ChargesFamiliales.ToString());
            row++;

            row = WriteSectionTitle(ws, row, "GAINS");
            row = WriteMoneyRow(ws, row, "Salaire de base", bulletin.SalaireDeBase);
            row = WriteMoneyRow(ws, row, "Indemnité de logement", bulletin.IndemniteLogement);
            row = WriteMoneyRow(ws, row, "Indemnité de transport", bulletin.IndemniteTransport);
            row = WriteMoneyRow(ws, row, "Indemnité de fonction", bulletin.IndemniteFonction);
            row = WriteMoneyRow(ws, row, "Indemnité de sujétion", bulletin.IndemniteSujetion);
            row = WriteMoneyRow(ws, row, "SALAIRE BRUT", bulletin.SalaireBrut, bold: true, fill: true);
            row++;

            row = WriteSectionTitle(ws, row, "COTISATIONS ET IMPÔT");
            row = WriteMoneyRow(ws, row, "CNSS part employé", bulletin.CnssEmploye);
            row = WriteMoneyRow(ws, row, "CNSS part employeur", bulletin.CnssEmployeur);
            row = WriteMoneyRow(ws, row, "Total exonérations (Art.106 CGI)", bulletin.TotalExonerations);
            row = WriteMoneyRow(ws, row, "Abattement forfaitaire (frais professionnels)", bulletin.AbattementForfaitaire);
            row = WriteMoneyRow(ws, row, "Base IUTS", bulletin.BaseIUTS);
            row = WriteMoneyRow(ws, row, "IUTS brut", bulletin.IUTSBrut);
            row = WriteMoneyRow(ws, row, $"Abattement charges familiales ({bulletin.ChargesFamiliales})", bulletin.AbattementChargesFamiliales);
            row = WriteMoneyRow(ws, row, "NET IUTS", bulletin.NetIUTS, bold: true);
            row = WriteMoneyRow(ws, row, "Retenue obligatoire sur salaire", bulletin.RetenueObligatoire);
            row++;

            row = WriteMoneyRow(ws, row, "SALAIRE NET À PAYER", bulletin.SalaireNetAPayer, bold: true, fill: true, big: true);
            row++;

            if (avecCumuls)
            {
                string titreSection = $"CUMULS DEPUIS JANVIER {bulletin.PeriodeFin.Year}" + (bulletin.CumulsEstimes ? " (estimés)" : "");
                row = WriteSectionTitle(ws, row, titreSection);
                row = WriteMoneyRow(ws, row, "Cumul salaire brut", bulletin.CumulBrutAnnuel);
                row = WriteMoneyRow(ws, row, "Cumul CNSS part employé", bulletin.CumulCnssEmployeAnnuel);
                row = WriteMoneyRow(ws, row, "Cumul Net IUTS", bulletin.CumulIUTSAnnuel);
                row = WriteMoneyRow(ws, row, "Cumul salaire net à payer", bulletin.CumulNetAnnuel, bold: true, fill: true);
                if (bulletin.CumulsEstimes)
                {
                    ws.Cell(row, 1).Value = "Cumuls estimés à partir de la rémunération du mois en cours, faute d'historique de paie détaillé.";
                    ws.Range(row, 1, row, 4).Merge();
                    ws.Cell(row, 1).Style.Font.FontSize = 8;
                    ws.Cell(row, 1).Style.Font.Italic = true;
                    row++;
                }
                row++;
            }

            ws.Cell(row, 1).Value = "Mode de règlement";
            ws.Cell(row, 1).Style.Font.Bold = true;
            ws.Cell(row, 2).Value = bulletin.ModeReglement +
                (string.IsNullOrWhiteSpace(bulletin.NumeroCompte) ? "" : $" — Compte : {bulletin.NumeroCompte}");
            row += 3;

            ws.Cell(row, 1).Value = "L'employeur";
            ws.Cell(row, 3).Value = "Signature de l'employé(e)";
            row += 2;

            ws.Cell(row, 1).Value = "Ce bulletin de salaire doit être conservé sans limitation de durée";
            ws.Range(row, 1, row, 4).Merge();
            ws.Cell(row, 1).Style.Font.FontSize = 8;
            ws.Cell(row, 1).Style.Font.Italic = true;
            row += 2;

            return row;
        }

        /// <summary>Corps condensé (demi-page) : identité minimale + totaux essentiels uniquement.</summary>
        private int EcrireCorpsCondenseExcel(IXLWorksheet ws, Bulletin bulletin, int row)
        {
            WriteLabelValue(ws, ref row, "Employé", bulletin.NomComplet, "Période", bulletin.PeriodeLibelle);
            row++;

            row = WriteMoneyRow(ws, row, "Salaire brut", bulletin.SalaireBrut);
            decimal totalRetenues = bulletin.CnssEmploye + bulletin.NetIUTS + bulletin.RetenueObligatoire;
            row = WriteMoneyRow(ws, row, "Total des retenues", totalRetenues);
            row = WriteMoneyRow(ws, row, "SALAIRE NET À PAYER", bulletin.SalaireNetAPayer, bold: true, fill: true, big: true);
            row++;

            ws.Cell(row, 1).Value = "Mode de règlement";
            ws.Cell(row, 1).Style.Font.Bold = true;
            ws.Cell(row, 2).Value = bulletin.ModeReglement +
                (string.IsNullOrWhiteSpace(bulletin.NumeroCompte) ? "" : $" — Compte : {bulletin.NumeroCompte}");
            row += 2;

            return row;
        }

        /// <summary>Corps bilingue français / anglais.</summary>
        private int EcrireCorpsBilingueExcel(IXLWorksheet ws, Bulletin bulletin, int row)
        {
            WriteLabelValue(ws, ref row, "Employé / Employee", bulletin.NomComplet, "Emploi / Position", bulletin.Emploi);
            WriteLabelValue(ws, ref row, "Date d'embauche / Hire date", bulletin.DateEmbauche?.ToString("dd/MM/yyyy") ?? "-", "Ancienneté (mois) / Seniority", bulletin.AncienneteMois.ToString());
            row++;

            row = WriteSectionTitle(ws, row, "GAINS / EARNINGS");
            row = WriteMoneyRow(ws, row, "Salaire de base / Base salary", bulletin.SalaireDeBase);
            row = WriteMoneyRow(ws, row, "Indemnité de logement / Housing allowance", bulletin.IndemniteLogement);
            row = WriteMoneyRow(ws, row, "Indemnité de transport / Transport allowance", bulletin.IndemniteTransport);
            row = WriteMoneyRow(ws, row, "Indemnité de fonction / Function allowance", bulletin.IndemniteFonction);
            row = WriteMoneyRow(ws, row, "Indemnité de sujétion / Special duty allowance", bulletin.IndemniteSujetion);
            row = WriteMoneyRow(ws, row, "SALAIRE BRUT / GROSS SALARY", bulletin.SalaireBrut, bold: true, fill: true);
            row++;

            row = WriteSectionTitle(ws, row, "COTISATIONS ET IMPÔT / DEDUCTIONS & TAX");
            row = WriteMoneyRow(ws, row, "CNSS part employé / Employee social security", bulletin.CnssEmploye);
            row = WriteMoneyRow(ws, row, "CNSS part employeur / Employer social security", bulletin.CnssEmployeur);
            row = WriteMoneyRow(ws, row, "Base IUTS / Taxable base", bulletin.BaseIUTS);
            row = WriteMoneyRow(ws, row, "NET IUTS / Net income tax", bulletin.NetIUTS, bold: true);
            row = WriteMoneyRow(ws, row, "Retenue obligatoire / Mandatory withholding", bulletin.RetenueObligatoire);
            row++;

            row = WriteMoneyRow(ws, row, "SALAIRE NET À PAYER / NET PAY", bulletin.SalaireNetAPayer, bold: true, fill: true, big: true);
            row++;

            ws.Cell(row, 1).Value = "Mode de règlement / Payment method";
            ws.Cell(row, 1).Style.Font.Bold = true;
            ws.Cell(row, 2).Value = bulletin.ModeReglement +
                (string.IsNullOrWhiteSpace(bulletin.NumeroCompte) ? "" : $" — Compte / Account : {bulletin.NumeroCompte}");
            row += 2;

            return row;
        }

        /// <summary>Pied de contrôle commun à tous les modèles : numéro de contrôle unique.</summary>
        private int EcrireControleExcel(IXLWorksheet ws, Bulletin bulletin, int row)
        {
            if (string.IsNullOrWhiteSpace(bulletin.NumeroControle))
                return row;

            ws.Cell(row, 1).Value = $"N° de contrôle : {bulletin.NumeroControle}";
            ws.Cell(row, 1).Style.Font.FontSize = 8;
            ws.Cell(row, 1).Style.Font.Italic = true;
            ws.Range(row, 1, row, 4).Merge();
            row++;

            ws.Cell(row, 1).Value = "Ce numéro permet de vérifier l'authenticité de ce bulletin auprès de l'employeur.";
            ws.Cell(row, 1).Style.Font.FontSize = 7;
            ws.Cell(row, 1).Style.Font.Italic = true;
            ws.Range(row, 1, row, 4).Merge();
            row++;

            return row;
        }

        private void WriteLabelValue(IXLWorksheet ws, ref int row, string label1, string value1, string label2, string value2)
        {
            ws.Cell(row, 1).Value = label1;
            ws.Cell(row, 1).Style.Font.Bold = true;
            ws.Cell(row, 1).Style.Fill.BackgroundColor = XLColor.LightGray;
            ws.Cell(row, 2).Value = value1 ?? "-";

            ws.Cell(row, 3).Value = label2;
            ws.Cell(row, 3).Style.Font.Bold = true;
            ws.Cell(row, 3).Style.Fill.BackgroundColor = XLColor.LightGray;
            ws.Cell(row, 4).Value = value2 ?? "-";
            row++;
        }

        private int WriteSectionTitle(IXLWorksheet ws, int row, string titre)
        {
            ws.Cell(row, 1).Value = titre;
            ws.Range(row, 1, row, 4).Merge();
            ws.Cell(row, 1).Style.Font.Bold = true;
            ws.Cell(row, 1).Style.Font.FontColor = XLColor.White;
            ws.Cell(row, 1).Style.Fill.BackgroundColor = XLColor.FromHtml("#4F6228");
            return row + 1;
        }

        private int WriteMoneyRow(IXLWorksheet ws, int row, string label, decimal montant, bool bold = false, bool fill = false, bool big = false)
        {
            ws.Cell(row, 1).Value = label;
            ws.Cell(row, 2).Value = montant;
            ws.Cell(row, 2).Style.NumberFormat.Format = "#,##0 \"FCFA\"";

            if (bold)
            {
                ws.Cell(row, 1).Style.Font.Bold = true;
                ws.Cell(row, 2).Style.Font.Bold = true;
            }
            if (fill)
            {
                ws.Range(row, 1, row, 2).Style.Fill.BackgroundColor = XLColor.FromHtml("#DCEBDC");
            }
            if (big)
            {
                ws.Cell(row, 1).Style.Font.FontSize = 13;
                ws.Cell(row, 2).Style.Font.FontSize = 13;
            }
            return row + 1;
        }

        #endregion

        #region Registre de paie (tableau multi-employés)

        public async Task<bool> ExportRegistrePaiePdfAsync(List<Bulletin> bulletins, string filePath)
        {
            return await Task.Run(() => ExportRegistrePaiePdf(bulletins, filePath));
        }

        /// <summary>
        /// Exporte, en un seul document PDF, une ligne récapitulative par bulletin déjà généré
        /// (brut, retenues, net) : sert de registre/archive RH pour la période, en complément
        /// des bulletins individuels.
        /// </summary>
        public bool ExportRegistrePaiePdf(List<Bulletin> bulletins, string filePath)
        {
            if (bulletins == null || bulletins.Count == 0 || string.IsNullOrEmpty(filePath))
                return false;

            Document document = null;
            PdfWriter writer = null;
            FileStream fs = null;

            try
            {
                string directory = Path.GetDirectoryName(filePath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                    Directory.CreateDirectory(directory);

                fs = new FileStream(filePath, FileMode.Create);
                document = new Document(PageSize.A4.Rotate(), 30, 30, 30, 30);
                writer = PdfWriter.GetInstance(document, fs);
                document.Open();

                var premier = bulletins[0];
                document.AddTitle("Registre de paie");
                document.AddSubject($"Registre de paie - {premier.PeriodeLibelle}");
                document.AddCreator("IUTSCalculator");
                document.AddAuthor("IUTSCalculator");

                var f = CreerPolices();

                var titre = new Paragraph($"REGISTRE DE PAIE — {premier.PeriodeLibelle}", f.Title) { Alignment = Element.ALIGN_CENTER, SpacingAfter = 4f };
                document.Add(titre);
                if (!string.IsNullOrWhiteSpace(premier.EntrepriseRaisonSociale))
                {
                    var entreprise = new Paragraph(premier.EntrepriseRaisonSociale, f.CompanyInfo) { Alignment = Element.ALIGN_CENTER, SpacingAfter = 10f };
                    document.Add(entreprise);
                }

                var table = new PdfPTable(5) { WidthPercentage = 100, SpacingAfter = 12f };
                table.SetWidths(new float[] { 2.5f, 2f, 1.3f, 1.3f, 1.3f });
                AddCell(table, "Employé", f.LabelBold, BaseColor.LIGHT_GRAY);
                AddCell(table, "Emploi", f.LabelBold, BaseColor.LIGHT_GRAY);
                AddCell(table, "Salaire brut", f.LabelBold, BaseColor.LIGHT_GRAY);
                AddCell(table, "Total retenues", f.LabelBold, BaseColor.LIGHT_GRAY);
                AddCell(table, "Net à payer", f.LabelBold, BaseColor.LIGHT_GRAY);

                decimal totalBrut = 0, totalRetenues = 0, totalNet = 0;
                foreach (var b in bulletins)
                {
                    decimal retenues = b.CnssEmploye + b.NetIUTS + b.RetenueObligatoire;
                    AddCell(table, b.NomComplet, f.Value);
                    AddCell(table, b.Emploi ?? "-", f.Value);
                    AddCell(table, FormatAmount(b.SalaireBrut), f.Value);
                    AddCell(table, FormatAmount(retenues), f.Value);
                    AddCell(table, FormatAmount(b.SalaireNetAPayer), f.ValueBold);

                    totalBrut += b.SalaireBrut;
                    totalRetenues += retenues;
                    totalNet += b.SalaireNetAPayer;
                }

                AddCell(table, $"TOTAL ({bulletins.Count} employé(s))", f.LabelBold, BaseColor.LIGHT_GRAY);
                AddCell(table, "", f.LabelBold, BaseColor.LIGHT_GRAY);
                AddCell(table, FormatAmount(totalBrut), f.LabelBold, BaseColor.LIGHT_GRAY);
                AddCell(table, FormatAmount(totalRetenues), f.LabelBold, BaseColor.LIGHT_GRAY);
                AddCell(table, FormatAmount(totalNet), f.LabelBold, BaseColor.LIGHT_GRAY);
                document.Add(table);

                string numeroControle = QrCodeService.GenererNumeroControle(
                    $"REGISTRE|{premier.PeriodeFin:yyyyMM}|{bulletins.Count}|{totalNet}|{DateTime.Now:yyyyMMddHHmmss}");
                string contenuVerification = QrCodeService.ConstruireContenuVerification(
                    "Registre de paie", $"{bulletins.Count} employés", premier.PeriodeLibelle, totalNet, numeroControle);
                AjouterControlePdf(document, numeroControle, contenuVerification,
                    "Ce numéro (et le QR code ci-contre) permettent de vérifier l'authenticité de ce registre auprès de l'employeur.", f);

                document.Close();
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Erreur lors de l'export du registre de paie (PDF) : {ex.Message}");
                return false;
            }
            finally
            {
                if (document != null && document.IsOpen()) document.Close();
                writer?.Close();
                if (fs != null) { fs.Close(); fs.Dispose(); }
            }
        }

        public async Task<bool> ExportRegistrePaieExcelAsync(List<Bulletin> bulletins, string filePath)
        {
            return await Task.Run(() => ExportRegistrePaieExcel(bulletins, filePath));
        }

        /// <summary>
        /// Exporte, en un seul classeur Excel, une ligne récapitulative par bulletin déjà
        /// généré (brut, retenues, net) : sert de registre/archive RH pour la période.
        /// </summary>
        public bool ExportRegistrePaieExcel(List<Bulletin> bulletins, string filePath)
        {
            if (bulletins == null || bulletins.Count == 0 || string.IsNullOrEmpty(filePath))
                return false;

            try
            {
                string directory = Path.GetDirectoryName(filePath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                    Directory.CreateDirectory(directory);

                var premier = bulletins[0];

                using (var workbook = new XLWorkbook())
                {
                    var ws = workbook.Worksheets.Add("Registre de paie");
                    int row = 1;

                    ws.Cell(row, 1).Value = $"REGISTRE DE PAIE — {premier.PeriodeLibelle}";
                    ws.Range(row, 1, row, 5).Merge();
                    ws.Cell(row, 1).Style.Font.Bold = true;
                    ws.Cell(row, 1).Style.Font.FontSize = 14;
                    row++;

                    if (!string.IsNullOrWhiteSpace(premier.EntrepriseRaisonSociale))
                    {
                        ws.Cell(row, 1).Value = premier.EntrepriseRaisonSociale;
                        ws.Range(row, 1, row, 5).Merge();
                        row++;
                    }
                    row++;

                    string[] entetes = { "Employé", "Emploi", "Salaire brut", "Total retenues", "Net à payer" };
                    for (int c = 0; c < entetes.Length; c++)
                    {
                        ws.Cell(row, c + 1).Value = entetes[c];
                        ws.Cell(row, c + 1).Style.Font.Bold = true;
                        ws.Cell(row, c + 1).Style.Fill.BackgroundColor = XLColor.LightGray;
                    }
                    row++;

                    int firstDataRow = row;
                    decimal totalBrut = 0, totalRetenues = 0, totalNet = 0;
                    foreach (var b in bulletins)
                    {
                        decimal retenues = b.CnssEmploye + b.NetIUTS + b.RetenueObligatoire;
                        ws.Cell(row, 1).Value = b.NomComplet;
                        ws.Cell(row, 2).Value = b.Emploi ?? "-";
                        ws.Cell(row, 3).Value = b.SalaireBrut;
                        ws.Cell(row, 4).Value = retenues;
                        ws.Cell(row, 5).Value = b.SalaireNetAPayer;
                        ws.Cell(row, 5).Style.Font.Bold = true;

                        totalBrut += b.SalaireBrut;
                        totalRetenues += retenues;
                        totalNet += b.SalaireNetAPayer;
                        row++;
                    }

                    ws.Cell(row, 1).Value = $"TOTAL ({bulletins.Count} employé(s))";
                    ws.Cell(row, 3).Value = totalBrut;
                    ws.Cell(row, 4).Value = totalRetenues;
                    ws.Cell(row, 5).Value = totalNet;
                    ws.Range(row, 1, row, 5).Style.Font.Bold = true;
                    ws.Range(row, 1, row, 5).Style.Fill.BackgroundColor = XLColor.LightBlue;
                    int totalRow = row;
                    row += 2;

                    ws.Range(firstDataRow, 3, totalRow, 5).Style.NumberFormat.Format = "#,##0 \"FCFA\"";

                    string numeroControle = QrCodeService.GenererNumeroControle(
                        $"REGISTRE|{premier.PeriodeFin:yyyyMM}|{bulletins.Count}|{totalNet}|{DateTime.Now:yyyyMMddHHmmss}");
                    ws.Cell(row, 1).Value = $"N° de contrôle : {numeroControle}";
                    ws.Cell(row, 1).Style.Font.FontSize = 8;
                    ws.Cell(row, 1).Style.Font.Italic = true;
                    ws.Range(row, 1, row, 5).Merge();

                    ws.Columns().AdjustToContents();
                    ws.Column(1).Width = 30;

                    workbook.SaveAs(filePath);
                }

                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Erreur lors de l'export du registre de paie (Excel) : {ex.Message}");
                return false;
            }
        }

        #endregion
    }
}
