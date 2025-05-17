using System;
using System.IO;
using System.Threading.Tasks;
using System.Collections.Generic;
using IUTSCalculator.Models;
using iTextSharp.text;
using iTextSharp.text.pdf;
using System.Globalization;
using System.Windows.Documents;
using System.Xml.Linq;

namespace IUTSCalculator.Services
{
    /// <summary>
    /// Service pour l'exportation des calculs d'impôts au format PDF
    /// </summary>
    public class PDFExportService
    {
        private readonly string _dateFormat = "dd/MM/yyyy";
        private readonly string _currencyFormat = "N0";
        private readonly CultureInfo _culture = new CultureInfo("fr-FR");

        /// <summary>
        /// Vérifie que toutes les dépendances sont disponibles pour l'export PDF
        /// </summary>
        private bool CheckDependencies()
        {
            try
            {
                // Vérifier que les types essentiels d'iTextSharp sont disponibles
                Type documentType = typeof(Document);
                Type pdfWriterType = typeof(PdfWriter);
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>
        /// Crée un document PDF contenant les détails d'un calcul d'impôt
        /// </summary>
        /// <param name="calculation">Le calcul d'impôt à exporter</param>
        /// <param name="filePath">Chemin de sauvegarde du fichier PDF</param>
        /// <returns>True si l'exportation a réussi, False sinon</returns>
        public async Task<bool> ExportTaxCalculationToPdfAsync(TaxCalculation calculation, string filePath)
        {
            try
            {
                return await Task.Run(() => ExportTaxCalculationToPdf(calculation, filePath));
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Erreur lors de l'exportation PDF: {ex.Message}");
                if (ex.InnerException != null)
                    Console.WriteLine($"Exception interne: {ex.InnerException.Message}");
                return false;
            }
        }

        /// <summary>
        /// Version synchrone de l'exportation d'un calcul d'impôt vers PDF
        /// </summary>
        /// <param name="calculation">Le calcul d'impôt à exporter</param>
        /// <param name="filePath">Chemin de sauvegarde du fichier PDF</param>
        /// <returns>True si l'exportation a réussi, False sinon</returns>
        public bool ExportTaxCalculationToPdf(TaxCalculation calculation, string filePath)
        {
            if (calculation == null || string.IsNullOrEmpty(filePath))
                return false;

            if (!CheckDependencies())
                return false;

            Document document = null;
            PdfWriter writer = null;
            FileStream fs = null;

            try
            {
                // Vérifier que le répertoire existe
                string directory = Path.GetDirectoryName(filePath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                // Créer le flux de fichier
                fs = new FileStream(filePath, FileMode.Create);

                // Définir le document et ses marges
                document = new Document(PageSize.A4, 50, 50, 50, 50);
                writer = PdfWriter.GetInstance(document, fs);

                document.Open();

                // Ajouter les détails du document
                AddDocumentMetadata(document, calculation);

                // Ajouter l'en-tête
                AddHeader(document);

                // Ajouter les informations de l'employé
                AddEmployeeInformation(document, calculation.Employee);

                // Ajouter le résumé du calcul d'impôt
                AddTaxSummary(document, calculation);

                // Ajouter les détails du calcul
                AddCalculationDetails(document, calculation);

                // Ajouter le pied de page
                AddFooter(document);

                document.Close();
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Erreur dans ExportTaxCalculationToPdf: {ex.Message}");
                if (ex.InnerException != null)
                    Console.WriteLine($"Exception interne: {ex.InnerException.Message}");
                return false;
            }
            finally
            {
                // S'assurer que toutes les ressources sont libérées
                if (document != null && document.IsOpen())
                {
                    document.Close();
                }

                if (writer != null)
                {
                    writer.Close();
                }

                if (fs != null)
                {
                    fs.Close();
                    fs.Dispose();
                }
            }
        }

        /// <summary>
        /// Exporte plusieurs calculs d'impôt dans un même document PDF
        /// </summary>
        /// <param name="calculations">Liste des calculs d'impôt à exporter</param>
        /// <param name="filePath">Chemin de sauvegarde du fichier PDF</param>
        /// <returns>True si l'exportation a réussi, False sinon</returns>
        public async Task<bool> ExportMultipleTaxCalculationsToPdfAsync(IEnumerable<TaxCalculation> calculations, string filePath)
        {
            try
            {
                return await Task.Run(() => ExportMultipleTaxCalculationsToPdf(calculations, filePath));
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Erreur lors de l'exportation multiple PDF: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Version synchrone de l'exportation de plusieurs calculs d'impôt vers PDF
        /// </summary>
        /// <param name="calculations">Liste des calculs d'impôt à exporter</param>
        /// <param name="filePath">Chemin de sauvegarde du fichier PDF</param>
        /// <returns>True si l'exportation a réussi, False sinon</returns>
        public bool ExportMultipleTaxCalculationsToPdf(IEnumerable<TaxCalculation> calculations, string filePath)
        {
            if (calculations == null || string.IsNullOrEmpty(filePath))
                return false;

            if (!CheckDependencies())
                return false;

            Document document = null;
            PdfWriter writer = null;
            FileStream fs = null;

            try
            {
                // Vérifier que le répertoire existe
                string directory = Path.GetDirectoryName(filePath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                // Créer le flux de fichier
                fs = new FileStream(filePath, FileMode.Create);

                // Définir le document et ses marges
                document = new Document(PageSize.A4, 50, 50, 50, 50);
                writer = PdfWriter.GetInstance(document, fs);

                document.Open();

                // Ajouter les détails du document
                AddDocumentMetadata(document, null);

                // Ajouter l'en-tête
                AddHeader(document);

                // Ajouter un tableau récapitulatif de tous les calculs
                AddCalculationsSummaryTable(document, calculations);

                // Pour chaque calcul, ajouter une section détaillée
                bool isFirstCalculation = true;
                foreach (var calculation in calculations)
                {
                    if (!isFirstCalculation)
                    {
                        // Ajouter un saut de page entre les calculs
                        document.NewPage();
                    }

                    // Ajouter les informations de l'employé
                    AddEmployeeInformation(document, calculation.Employee);

                    // Ajouter le résumé du calcul d'impôt
                    AddTaxSummary(document, calculation);

                    // Ajouter les détails du calcul
                    AddCalculationDetails(document, calculation);

                    isFirstCalculation = false;
                }

                // Ajouter le pied de page
                AddFooter(document);

                document.Close();
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Erreur dans ExportMultipleTaxCalculationsToPdf: {ex.Message}");
                return false;
            }
            finally
            {
                // S'assurer que toutes les ressources sont libérées
                if (document != null && document.IsOpen())
                {
                    document.Close();
                }

                if (writer != null)
                {
                    writer.Close();
                }

                if (fs != null)
                {
                    fs.Close();
                    fs.Dispose();
                }
            }
        }

        /// <summary>
        /// Ajoute les métadonnées au document PDF
        /// </summary>
        private void AddDocumentMetadata(Document document, TaxCalculation calculation)
        {
            if (document == null)
                return;

            string title = "Calcul d'impôt sur les traitements et salaires (IUTS)";
            string subject = calculation != null
                ? $"Calcul IUTS pour {calculation.Employee.FirstName} {calculation.Employee.LastName}"
                : "Résumé des calculs IUTS";

            document.AddTitle(title);
            document.AddSubject(subject);
            document.AddKeywords("IUTS, Impôt, Salaire, Calcul");
            document.AddCreator("IUTSCalculator");
            document.AddAuthor("IUTSCalculator");
            document.AddHeader("Généré le", DateTime.Now.ToString(_dateFormat, _culture));
        }

        /// <summary>
        /// Ajoute l'en-tête du document PDF
        /// </summary>
        private void AddHeader(Document document)
        {
            if (document == null)
                return;

            // Titre principal
            iTextSharp.text.Paragraph header = new iTextSharp.text.Paragraph("Calcul d'Impôt sur les Traitements et Salaires (IUTS)",
                new Font(Font.FontFamily.HELVETICA, 18, Font.BOLD));
            header.Alignment = Element.ALIGN_CENTER;
            document.Add(header);

            // Sous-titre avec la date de génération
            iTextSharp.text.Paragraph dateGeneration = new iTextSharp.text.Paragraph($"Généré le : {DateTime.Now.ToString(_dateFormat, _culture)}",
                new Font(Font.FontFamily.HELVETICA, 10, Font.ITALIC));
            dateGeneration.Alignment = Element.ALIGN_CENTER;
            document.Add(dateGeneration);

            document.Add(new iTextSharp.text.Paragraph(" ")); // Espace
        }

        /// <summary>
        /// Ajoute les informations de l'employé au document PDF
        /// </summary>
        private void AddEmployeeInformation(Document document, Employee employee)
        {
            if (document == null || employee == null)
                return;

            iTextSharp.text.Paragraph empTitle = new iTextSharp.text.Paragraph("Informations de l'employé",
                new Font(Font.FontFamily.HELVETICA, 14, Font.BOLD));
            empTitle.SpacingBefore = 15f;
            document.Add(empTitle);

            // Tableau pour les informations de l'employé
            PdfPTable table = new PdfPTable(2);
            table.WidthPercentage = 100;
            table.SpacingBefore = 10f;
            table.SpacingAfter = 10f;

            // Définir les largeurs des colonnes
            float[] widths = { 1f, 2f };
            table.SetWidths(widths);

            // Style pour les en-têtes
            Font headerFont = new Font(Font.FontFamily.HELVETICA, 10, Font.BOLD);
            Font contentFont = new Font(Font.FontFamily.HELVETICA, 10);

            // Ajouter les cellules avec les informations
            AddTableCell(table, "Nom et Prénom", $"{employee.LastName} {employee.FirstName}", headerFont, contentFont);
            AddTableCell(table, "Identifiant", employee.EmployeeId, headerFont, contentFont);
            AddTableCell(table, "Catégorie", employee.Category, headerFont, contentFont);
            AddTableCell(table, "Salaire de base", FormatAmount(employee.BaseSalary), headerFont, contentFont);
            AddTableCell(table, "Charges familiales", employee.FamilyCharges.ToString(), headerFont, contentFont);
            AddTableCell(table, "Conjoint travaille", employee.IsSpouseWorking ? "Oui" : "Non", headerFont, contentFont);

            document.Add(table);
        }

        /// <summary>
        /// Ajoute une cellule à un tableau PDF
        /// </summary>
        private void AddTableCell(PdfPTable table, string label, string value, Font labelFont, Font valueFont)
        {
            if (table == null)
                return;

            PdfPCell labelCell = new PdfPCell(new Phrase(label, labelFont));
            labelCell.BackgroundColor = BaseColor.LIGHT_GRAY;
            labelCell.HorizontalAlignment = Element.ALIGN_LEFT;
            labelCell.VerticalAlignment = Element.ALIGN_MIDDLE;
            labelCell.Padding = 5f;
            table.AddCell(labelCell);

            PdfPCell valueCell = new PdfPCell(new Phrase(value, valueFont));
            valueCell.HorizontalAlignment = Element.ALIGN_LEFT;
            valueCell.VerticalAlignment = Element.ALIGN_MIDDLE;
            valueCell.Padding = 5f;
            table.AddCell(valueCell);
        }

        /// <summary>
        /// Ajoute le résumé du calcul d'impôt au document PDF
        /// </summary>
        private void AddTaxSummary(Document document, TaxCalculation calculation)
        {
            if (document == null || calculation == null)
                return;

            iTextSharp.text.Paragraph taxTitle = new iTextSharp.text.Paragraph("Résumé du calcul d'impôt",
                new Font(Font.FontFamily.HELVETICA, 14, Font.BOLD));
            taxTitle.SpacingBefore = 15f;
            document.Add(taxTitle);

            // Tableau pour le résumé du calcul
            PdfPTable table = new PdfPTable(2);
            table.WidthPercentage = 100;
            table.SpacingBefore = 10f;
            table.SpacingAfter = 10f;

            // Définir les largeurs des colonnes
            float[] widths = { 1f, 2f };
            table.SetWidths(widths);

            // Style pour les en-têtes
            Font headerFont = new Font(Font.FontFamily.HELVETICA, 10, Font.BOLD);
            Font contentFont = new Font(Font.FontFamily.HELVETICA, 10);
            Font highlightFont = new Font(Font.FontFamily.HELVETICA, 10, Font.BOLD, BaseColor.RED);

            // Type de calcul
            string calculationType = calculation.IsAnnual ? "Annuel" : "Mensuel";
            if (calculation.IsRegularization)
                calculationType += " (Régularisation)";
            AddTableCell(table, "Type de calcul", calculationType, headerFont, contentFont);

            // Date du calcul
            AddTableCell(table, "Date du calcul", calculation.CalculationDate.ToString(_dateFormat, _culture), headerFont, contentFont);

            // Salaire brut
            AddTableCell(table, "Salaire brut", FormatAmount(calculation.SalaireBrut), headerFont, contentFont);

            // Montant exonéré
            AddTableCell(table, "Montant exonéré", FormatAmount(calculation.ExemptAmount), headerFont, contentFont);

            // Revenu net imposable
            AddTableCell(table, "Revenu net imposable", FormatAmount(calculation.BaseIUTSHorsDeduction), headerFont, contentFont);

            // Réduction pour charges
            AddTableCell(table, "Réduction charges familiales", FormatAmount(calculation.FamilyChargeReduction), headerFont, contentFont);

            // Montant de l'impôt
            Font taxFont = calculation.IUTSNet > 0 ? highlightFont : contentFont;
            AddTableCell(table, "Montant de l'impôt", FormatAmount(calculation.IUTSNet), headerFont, taxFont);

            // Taux d'imposition
            AddTableCell(table, "Taux d'imposition moyen", $"{calculation.TaxRate.ToString("N2", _culture)}%", headerFont, contentFont);

            // Salaire net après impôt
            AddTableCell(table, "Salaire net après impôt", FormatAmount(calculation.NetSalaryAfterTax), headerFont, contentFont);

            // Différence de régularisation si applicable
            if (calculation.IsRegularization)
            {
                string differenceLabel = calculation.TaxDifference > 0 ? "Impôt supplémentaire à payer" : "Remboursement d'impôt";
                Font diffFont = calculation.TaxDifference > 0 ? highlightFont : new Font(Font.FontFamily.HELVETICA, 10, Font.BOLD, BaseColor.GREEN);
                AddTableCell(table, differenceLabel, FormatAmount(Math.Abs(calculation.TaxDifference)), headerFont, diffFont);
            }

            document.Add(table);
        }

        /// <summary>
        /// Ajoute les détails du calcul d'impôt au document PDF
        /// </summary>
        private void AddCalculationDetails(Document document, TaxCalculation calculation)
        {
            if (document == null || calculation == null)
                return;

            iTextSharp.text.Paragraph detailsTitle = new iTextSharp.text.Paragraph("Détails du calcul",
                new Font(Font.FontFamily.HELVETICA, 14, Font.BOLD));
            detailsTitle.SpacingBefore = 15f;
            document.Add(detailsTitle);

            // Afficher les détails du calcul s'ils sont disponibles
            if (!string.IsNullOrEmpty(calculation.CalculationDetails))
            {
                iTextSharp.text.Paragraph details = new iTextSharp.text.Paragraph(calculation.CalculationDetails,
                    new Font(Font.FontFamily.HELVETICA, 10));
                details.SpacingBefore = 10f;
                document.Add(details);
            }
            else
            {
                iTextSharp.text.Paragraph noDetails = new iTextSharp.text.Paragraph("Aucun détail disponible pour ce calcul.",
                    new Font(Font.FontFamily.HELVETICA, 10, Font.ITALIC));
                noDetails.SpacingBefore = 10f;
                document.Add(noDetails);
            }
        }

        /// <summary>
        /// Ajoute un tableau récapitulatif de tous les calculs
        /// </summary>
        private void AddCalculationsSummaryTable(Document document, IEnumerable<TaxCalculation> calculations)
        {
            if (document == null || calculations == null)
                return;

            iTextSharp.text.Paragraph summaryTitle = new iTextSharp.text.Paragraph("Résumé des calculs d'impôt",
                new Font(Font.FontFamily.HELVETICA, 14, Font.BOLD));
            summaryTitle.SpacingBefore = 15f;
            document.Add(summaryTitle);

            // Créer le tableau de résumé
            PdfPTable table = new PdfPTable(6);
            table.WidthPercentage = 100;
            table.SpacingBefore = 10f;
            table.SpacingAfter = 10f;

            // Définir les largeurs des colonnes
            float[] widths = { 3f, 2f, 2f, 2f, 1.5f, 2.5f };
            table.SetWidths(widths);

            // Style pour les en-têtes
            Font headerFont = new Font(Font.FontFamily.HELVETICA, 9, Font.BOLD);

            // Ajouter les en-têtes du tableau
            string[] headers = { "Employé", "Date", "Type", "Salaire brut", "Taux", "Impôt" };
            foreach (var header in headers)
            {
                PdfPCell cell = new PdfPCell(new Phrase(header, headerFont));
                cell.BackgroundColor = BaseColor.LIGHT_GRAY;
                cell.HorizontalAlignment = Element.ALIGN_CENTER;
                cell.VerticalAlignment = Element.ALIGN_MIDDLE;
                cell.Padding = 5f;
                table.AddCell(cell);
            }

            // Style pour le contenu
            Font contentFont = new Font(Font.FontFamily.HELVETICA, 9);

            // Ajouter les données des calculs
            foreach (var calc in calculations)
            {
                // Nom de l'employé
                PdfPCell nameCell = new PdfPCell(new Phrase($"{calc.Employee.LastName} {calc.Employee.FirstName}", contentFont));
                nameCell.HorizontalAlignment = Element.ALIGN_LEFT;
                nameCell.Padding = 4f;
                table.AddCell(nameCell);

                // Date du calcul
                PdfPCell dateCell = new PdfPCell(new Phrase(calc.CalculationDate.ToString(_dateFormat, _culture), contentFont));
                dateCell.HorizontalAlignment = Element.ALIGN_CENTER;
                dateCell.Padding = 4f;
                table.AddCell(dateCell);

                // Type de calcul
                string calculationType = calc.IsAnnual ? "Annuel" : "Mensuel";
                if (calc.IsRegularization)
                    calculationType += " (R)";
                PdfPCell typeCell = new PdfPCell(new Phrase(calculationType, contentFont));
                typeCell.HorizontalAlignment = Element.ALIGN_CENTER;
                typeCell.Padding = 4f;
                table.AddCell(typeCell);

                // Salaire brut
                PdfPCell salaryCell = new PdfPCell(new Phrase(FormatAmount(calc.SalaireBrut), contentFont));
                salaryCell.HorizontalAlignment = Element.ALIGN_RIGHT;
                salaryCell.Padding = 4f;
                table.AddCell(salaryCell);

                // Taux d'imposition
                PdfPCell rateCell = new PdfPCell(new Phrase($"{calc.TaxRate.ToString("N2", _culture)}%", contentFont));
                rateCell.HorizontalAlignment = Element.ALIGN_CENTER;
                rateCell.Padding = 4f;
                table.AddCell(rateCell);

                // Montant de l'impôt
                PdfPCell taxCell = new PdfPCell(new Phrase(FormatAmount(calc.IUTSNet), contentFont));
                taxCell.HorizontalAlignment = Element.ALIGN_RIGHT;
                taxCell.Padding = 4f;
                table.AddCell(taxCell);
            }

            document.Add(table);
        }

        /// <summary>
        /// Ajoute le pied de page au document PDF
        /// </summary>
        private void AddFooter(Document document)
        {
            if (document == null)
                return;

            iTextSharp.text.Paragraph footer = new iTextSharp.text.Paragraph("Document généré par IUTSCalculator - Ce document est informatif et ne constitue pas un document officiel d'imposition.",
                new Font(Font.FontFamily.HELVETICA, 8, Font.ITALIC));
            footer.Alignment = Element.ALIGN_CENTER;
            footer.SpacingBefore = 20f;
            document.Add(footer);
        }

        /// <summary>
        /// Formate un montant pour l'affichage
        /// </summary>
        private string FormatAmount(decimal amount)
        {
            return $"{amount.ToString(_currencyFormat, _culture)} FCFA";
        }
    }
}