using System;
using System.IO;
using iTextSharp.text;
using iTextSharp.text.pdf;

namespace SigmaGuideGenerator
{
    // Événement d'en-tête et pied de page personnalisé
    public class HeaderFooterPageEvent : PdfPageEventHelper
    {
        private Font _headerFont = FontFactory.GetFont(FontFactory.HELVETICA_BOLD, 8, new BaseColor(20, 103, 90));
        private Font _footerFont = FontFactory.GetFont(FontFactory.HELVETICA, 8, new BaseColor(113, 128, 150));
        private Font _pageFont = FontFactory.GetFont(FontFactory.HELVETICA_BOLD, 8, new BaseColor(20, 103, 90));

        public override void OnEndPage(PdfWriter writer, Document document)
        {
            if (writer.PageNumber <= 1) return; // Pas d'en-tête ni pied sur la page de couverture

            PdfContentByte cb = writer.DirectContent;

            // En-tête
            float topY = document.Top + 15;
            ColumnText.ShowTextAligned(cb, Element.ALIGN_LEFT,
                new Phrase("SIGMA PAIE & RH — MANUEL OFFICIEL D'EXPLOITATION & RÉFÉRENTIEL TECHNIQUE", _headerFont),
                document.Left, topY, 0);

            ColumnText.ShowTextAligned(cb, Element.ALIGN_RIGHT,
                new Phrase("CONFORME SYSCOHADA & FISCALITÉ 2026", _headerFont),
                document.Right, topY, 0);

            // Ligne fine d'en-tête
            cb.SetColorStroke(new BaseColor(203, 213, 224));
            cb.SetLineWidth(0.75f);
            cb.MoveTo(document.Left, topY - 4);
            cb.LineTo(document.Right, topY - 4);
            cb.Stroke();

            // Pied de page
            float bottomY = document.Bottom - 18;
            cb.SetColorStroke(new BaseColor(203, 213, 224));
            cb.SetLineWidth(0.75f);
            cb.MoveTo(document.Left, bottomY + 12);
            cb.LineTo(document.Right, bottomY + 12);
            cb.Stroke();

            ColumnText.ShowTextAligned(cb, Element.ALIGN_LEFT,
                new Phrase("Document Confidentiel — Usage Professionnel RH & Comptabilité", _footerFont),
                document.Left, bottomY, 0);

            ColumnText.ShowTextAligned(cb, Element.ALIGN_RIGHT,
                new Phrase("Page " + writer.PageNumber, _pageFont),
                document.Right, bottomY, 0);
        }
    }

    class Program
    {
        // Palette de couleurs Corporate
        static BaseColor ColorTealPrimary = new BaseColor(20, 103, 90);      // #14675A
        static BaseColor ColorTealDark = new BaseColor(15, 76, 66);         // #0F4C42
        static BaseColor ColorTealLight = new BaseColor(240, 247, 245);     // #F0F7F5
        static BaseColor ColorGoldAccent = new BaseColor(217, 119, 6);      // #D97706
        static BaseColor ColorDarkText = new BaseColor(45, 55, 72);         // #2D3748
        static BaseColor ColorMutedText = new BaseColor(100, 116, 139);     // #64748B
        static BaseColor ColorBorder = new BaseColor(226, 232, 240);        // #E2E8F0
        static BaseColor ColorInfoBg = new BaseColor(235, 248, 255);        // #EBF8FF
        static BaseColor ColorInfoBorder = new BaseColor(49, 130, 206);     // #3182CE
        static BaseColor ColorWarningBg = new BaseColor(254, 252, 232);     // #FEFCE8
        static BaseColor ColorWarningBorder = new BaseColor(202, 138, 4);   // #CA8A04
        static BaseColor ColorSuccessBg = new BaseColor(240, 253, 244);     // #F0FDF4
        static BaseColor ColorSuccessBorder = new BaseColor(22, 163, 74);   // #16A34A
        static BaseColor ColorPlaceholderBg = new BaseColor(241, 245, 249); // #F1F5F9
        static BaseColor ColorPlaceholderBorder = new BaseColor(148, 163, 184); // #94A3B8

        // Polices
        static Font FontCoverTitle = FontFactory.GetFont(FontFactory.HELVETICA_BOLD, 24, BaseColor.WHITE);
        static Font FontCoverSubtitle = FontFactory.GetFont(FontFactory.HELVETICA, 12, new BaseColor(209, 234, 229));
        static Font FontCoverMeta = FontFactory.GetFont(FontFactory.HELVETICA, 9.5f, BaseColor.WHITE);
        static Font FontCoverMetaBold = FontFactory.GetFont(FontFactory.HELVETICA_BOLD, 9.5f, new BaseColor(251, 211, 141));

        static Font FontH1 = FontFactory.GetFont(FontFactory.HELVETICA_BOLD, 15, ColorTealDark);
        static Font FontH2 = FontFactory.GetFont(FontFactory.HELVETICA_BOLD, 12, ColorTealPrimary);
        static Font FontH3 = FontFactory.GetFont(FontFactory.HELVETICA_BOLD, 10.5f, ColorDarkText);

        static Font FontBody = FontFactory.GetFont(FontFactory.HELVETICA, 9.5f, ColorDarkText);
        static Font FontBodyBold = FontFactory.GetFont(FontFactory.HELVETICA_BOLD, 9.5f, ColorDarkText);
        static Font FontBodyItalic = FontFactory.GetFont(FontFactory.HELVETICA_OBLIQUE, 9, ColorMutedText);
        static Font FontCode = FontFactory.GetFont(FontFactory.COURIER, 8.5f, new BaseColor(15, 23, 42));

        static Font FontTableHeader = FontFactory.GetFont(FontFactory.HELVETICA_BOLD, 9, BaseColor.WHITE);
        static Font FontTableCell = FontFactory.GetFont(FontFactory.HELVETICA, 8.5f, ColorDarkText);
        static Font FontTableCellBold = FontFactory.GetFont(FontFactory.HELVETICA_BOLD, 8.5f, ColorDarkText);

        static void Main(string[] args)
        {
            string pdfPath = @"c:\Users\HP\source\repos\IUTSCalculator\Guide_Utilisation_Sigma_Paie_et_RH.pdf";
            string artifactPath = @"C:\Users\HP\.gemini\antigravity-ide\brain\daba426d-1262-4968-ab9b-1be282026702\Guide_Utilisation_Sigma_Paie_et_RH.pdf";

            Console.WriteLine("Démarrage de la génération du Guide PDF Haute Définition...");

            using (FileStream fs = new FileStream(pdfPath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                Document doc = new Document(PageSize.A4, 38, 38, 44, 44);
                PdfWriter writer = PdfWriter.GetInstance(doc, fs);
                writer.PageEvent = new HeaderFooterPageEvent();

                doc.Open();

                // =========================================================================
                // 1. PAGE DE COUVERTURE EXECUTIVE
                // =========================================================================
                BuildCoverPage(doc);
                doc.NewPage();

                // =========================================================================
                // 2. SOMMAIRE ANALYTIQUE
                // =========================================================================
                BuildTableOfContents(doc);
                doc.NewPage();

                // =========================================================================
                // 3. SECTION 1 : INTRODUCTION & ARCHITECTURE SYSTÈME
                // =========================================================================
                BuildSection1_Introduction(doc);
                doc.NewPage();

                // =========================================================================
                // 4. SECTION 2 : GESTION DU PERSONNEL (SAL-01)
                // =========================================================================
                BuildSection2_Salaries(doc);
                doc.NewPage();

                // =========================================================================
                // 5. SECTION 3 : VARIABLES DU MOIS & PRIMES (PAI-02)
                // =========================================================================
                BuildSection3_Variables(doc);
                doc.NewPage();

                // =========================================================================
                // 6. SECTION 4 : MOTEUR DE CALCUL & BULLETINS (PAI-01 & BTH-01)
                // =========================================================================
                BuildSection4_CalculsBulletins(doc);
                doc.NewPage();

                // =========================================================================
                // 7. SECTION 5 : CONGÉS, ABSENCES & RÉGULARISATION COMPTABLE (CON-01)
                // =========================================================================
                BuildSection5_CongesAbsences(doc);
                doc.NewPage();

                // =========================================================================
                // 8. SECTION 6 : LIVRE DE PAIE & CONTRÔLES D'ÉQUILIBRE (ETA-01 & ETA-02)
                // =========================================================================
                BuildSection6_LivrePaie(doc);
                doc.NewPage();

                // =========================================================================
                // 9. SECTION 7 : INTÉGRATION COMPTABLE SYSCOHADA (CPT-01)
                // =========================================================================
                BuildSection7_ComptabiliteSYSCOHADA(doc);
                doc.NewPage();

                // =========================================================================
                // 10. SECTION 8 : RÈGLEMENTS BANCAIRES & VIREMENTS (CPT-02)
                // =========================================================================
                BuildSection8_VirementsBancaires(doc);
                doc.NewPage();

                // =========================================================================
                // 11. SECTION 9 : DÉCLARATIONS SOCIALES ET FISCALES (IMP-01 & SOC-01)
                // =========================================================================
                BuildSection9_Declarations(doc);
                doc.NewPage();

                // =========================================================================
                // 12. SECTION 10 : SÉCURITÉ, AUDIT SHA-256 & CLÔTURE (ADM-01 & ADM-02)
                // =========================================================================
                BuildSection10_AuditSecurite(doc);
                doc.NewPage();

                // =========================================================================
                // 13. SECTION 11 : GUIDE DES CAPTURES D'ÉCRAN POUR L'UTILISATEUR
                // =========================================================================
                BuildSection11_RepertoireCaptures(doc);

                doc.Close();
            }

            // Copie dans le répertoire des artefacts
            File.Copy(pdfPath, artifactPath, true);

            FileInfo fi = new FileInfo(pdfPath);
            Console.WriteLine("SUCCES: Guide PDF genere avec succes !");
            Console.WriteLine("Taille: " + fi.Length + " octets");
            Console.WriteLine("Chemin: " + pdfPath);
            Console.WriteLine("Copie Artefact: " + artifactPath);
        }

        #region BLOCS DE MISE EN PAGE RÉUTILISABLES

        static void AddHeading1(Document doc, string title, string badge = null)
        {
            PdfPTable table = new PdfPTable(1);
            table.WidthPercentage = 100f;
            table.SpacingBefore = 14f;
            table.SpacingAfter = 10f;

            PdfPCell cell = new PdfPCell();
            cell.BackgroundColor = ColorTealLight;
            cell.Border = Rectangle.LEFT_BORDER;
            cell.BorderColorLeft = ColorTealPrimary;
            cell.BorderWidthLeft = 4f;
            cell.PaddingTop = 8f;
            cell.PaddingBottom = 8f;
            cell.PaddingLeft = 12f;
            cell.PaddingRight = 10f;

            Paragraph p = new Paragraph();
            if (!string.IsNullOrEmpty(badge))
            {
                Chunk cBadge = new Chunk("[" + badge + "]  ", FontFactory.GetFont(FontFactory.HELVETICA_BOLD, 12, ColorGoldAccent));
                p.Add(cBadge);
            }
            p.Add(new Chunk(title, FontH1));
            cell.AddElement(p);

            table.AddCell(cell);
            doc.Add(table);
        }

        static void AddHeading2(Document doc, string title)
        {
            Paragraph p = new Paragraph(title, FontH2);
            p.SpacingBefore = 12f;
            p.SpacingAfter = 6f;
            doc.Add(p);
        }

        static void AddHeading3(Document doc, string title)
        {
            Paragraph p = new Paragraph(title, FontH3);
            p.SpacingBefore = 8f;
            p.SpacingAfter = 4f;
            doc.Add(p);
        }

        static void AddParagraph(Document doc, string text)
        {
            Paragraph p = new Paragraph(text, FontBody);
            p.SpacingAfter = 6f;
            p.SetLeading(0f, 1.35f);
            doc.Add(p);
        }

        static void AddCallout(Document doc, string title, string content, string type = "info")
        {
            PdfPTable table = new PdfPTable(1);
            table.WidthPercentage = 100f;
            table.SpacingBefore = 8f;
            table.SpacingAfter = 8f;

            PdfPCell cell = new PdfPCell();
            cell.Padding = 10f;

            BaseColor borderCol = ColorInfoBorder;
            BaseColor bgCol = ColorInfoBg;

            if (type == "warning")
            {
                borderCol = ColorWarningBorder;
                bgCol = ColorWarningBg;
            }
            else if (type == "success")
            {
                borderCol = ColorSuccessBorder;
                bgCol = ColorSuccessBg;
            }

            cell.BackgroundColor = bgCol;
            cell.Border = Rectangle.BOX;
            cell.BorderColor = borderCol;
            cell.BorderWidth = 1f;
            cell.BorderWidthLeft = 4f;

            Paragraph pTitle = new Paragraph(title, FontFactory.GetFont(FontFactory.HELVETICA_BOLD, 10, borderCol));
            pTitle.SpacingAfter = 4f;
            cell.AddElement(pTitle);

            Paragraph pContent = new Paragraph(content, FontBody);
            pContent.SetLeading(0f, 1.3f);
            cell.AddElement(pContent);

            table.AddCell(cell);
            doc.Add(table);
        }

        static void AddScreenshotPlaceholder(Document doc, int id, string title, string description, string[] elementsToCheck)
        {
            PdfPTable table = new PdfPTable(1);
            table.WidthPercentage = 100f;
            table.SpacingBefore = 10f;
            table.SpacingAfter = 10f;

            PdfPCell cell = new PdfPCell();
            cell.BackgroundColor = ColorPlaceholderBg;
            cell.Border = Rectangle.BOX;
            cell.BorderColor = ColorPlaceholderBorder;
            cell.BorderWidth = 1f;
            cell.Padding = 12f;

            // En-tête du placeholder
            Paragraph pHeader = new Paragraph();
            Chunk badgeChunk = new Chunk("ILLUSTRATION N " + id + " : ", FontFactory.GetFont(FontFactory.HELVETICA_BOLD, 9.5f, ColorTealPrimary));
            Chunk titleChunk = new Chunk(title, FontFactory.GetFont(FontFactory.HELVETICA_BOLD, 9.5f, ColorDarkText));
            pHeader.Add(badgeChunk);
            pHeader.Add(titleChunk);
            pHeader.SpacingAfter = 5f;
            cell.AddElement(pHeader);

            Paragraph pDesc = new Paragraph(description, FontBodyItalic);
            pDesc.SpacingAfter = 6f;
            cell.AddElement(pDesc);

            Paragraph pGuide = new Paragraph("Elements attendus sur la capture fournie par l'utilisateur :", FontTableCellBold);
            pGuide.SpacingAfter = 3f;
            cell.AddElement(pGuide);

            foreach (var elem in elementsToCheck)
            {
                Paragraph pItem = new Paragraph("  * " + elem, FontTableCell);
                pItem.SetLeading(0f, 1.25f);
                cell.AddElement(pItem);
            }

            table.AddCell(cell);
            doc.Add(table);
        }

        #endregion

        #region GÉNÉRATION DES PAGES

        static void BuildCoverPage(Document doc)
        {
            // Bloc de couverture stylisé dans une table pleine largeur
            PdfPTable coverTable = new PdfPTable(1);
            coverTable.WidthPercentage = 100f;
            coverTable.SpacingBefore = 20f;

            PdfPCell heroCell = new PdfPCell();
            heroCell.BackgroundColor = ColorTealDark;
            heroCell.PaddingTop = 40f;
            heroCell.PaddingBottom = 40f;
            heroCell.PaddingLeft = 30f;
            heroCell.PaddingRight = 30f;
            heroCell.Border = Rectangle.NO_BORDER;

            Paragraph pBadge = new Paragraph("SUITE LOGICIELLE OFFICIELLE RH & PAIE — SYSCOHADA", FontCoverMetaBold);
            pBadge.SpacingAfter = 15f;
            heroCell.AddElement(pBadge);

            Paragraph pTitle = new Paragraph("SIGMA PAIE & RH", FontCoverTitle);
            pTitle.SpacingAfter = 10f;
            heroCell.AddElement(pTitle);

            Paragraph pSub = new Paragraph("Guide d'Utilisation Exhaustif & Manuel de Référence Technique", FontCoverSubtitle);
            pSub.SpacingAfter = 20f;
            heroCell.AddElement(pSub);

            Paragraph pDesc = new Paragraph(
                "Document exhaustif couvrant l'intégralité des 16 modules opérationnels (Phases 1, 2 et 3) : " +
                "de la gestion du personnel à la génération des pièces comptables SYSCOHADA, du calcul certifié " +
                "de l'IUTS 2026 à la clôture sécurisée des périodes de paie.",
                FontFactory.GetFont(FontFactory.HELVETICA, 10, new BaseColor(226, 232, 240)));
            pDesc.SetLeading(0f, 1.4f);
            heroCell.AddElement(pDesc);

            coverTable.AddCell(heroCell);
            doc.Add(coverTable);

            // Tableau des Métadonnées & Conformité
            PdfPTable metaTable = new PdfPTable(2);
            metaTable.WidthPercentage = 100f;
            metaTable.SpacingBefore = 25f;
            metaTable.SetWidths(new float[] { 45f, 55f });

            AddMetaRow(metaTable, "Version du Système", "Sigma Paie & RH v3.0 (Architecture WPF & SQLite)");
            AddMetaRow(metaTable, "Référentiel Comptable", "SYSCOHADA Révisé (Comptes 6611, 4220, 4311, 4471)");
            AddMetaRow(metaTable, "Réglementation Sociale", "Code du Travail & Caisse Nationale de Sécurité Sociale");
            AddMetaRow(metaTable, "Barème Fiscal Intégré", "IUTS 2026 progressif par tranches & abattements");
            AddMetaRow(metaTable, "Sécurité & Audit", "Empreintes numériques SHA-256 & Isolation Multi-Dossiers");
            AddMetaRow(metaTable, "Diffusion", "Direction Générale, DRH, DAF, Gestionnaires de Paie");
            AddMetaRow(metaTable, "Date d'Édition", DateTime.Now.ToString("dd MMMM yyyy"));

            doc.Add(metaTable);

            // Note de bas de page de couverture
            Paragraph pNotice = new Paragraph(
                "Ce document constitue le manuel officiel de référence de l'application Sigma Paie & RH. " +
                "Toute reproduction ou diffusion sans autorisation préalable est strictement interdite.",
                FontBodyItalic);
            pNotice.SpacingBefore = 35f;
            pNotice.Alignment = Element.ALIGN_CENTER;
            doc.Add(pNotice);
        }

        static void AddMetaRow(PdfPTable table, string label, string value)
        {
            PdfPCell c1 = new PdfPCell(new Phrase(label, FontTableCellBold));
            c1.BackgroundColor = ColorTealLight;
            c1.Padding = 7f;
            c1.BorderColor = ColorBorder;

            PdfPCell c2 = new PdfPCell(new Phrase(value, FontTableCell));
            c2.Padding = 7f;
            c2.BorderColor = ColorBorder;

            table.AddCell(c1);
            table.AddCell(c2);
        }

        static void BuildTableOfContents(Document doc)
        {
            AddHeading1(doc, "SOMMAIRE ANALYTIQUE DE RÉFÉRENCE");
            AddParagraph(doc, "Le présent manuel est articulé autour des 11 sections thématiques couvrant l'ensemble du cycle de traitement de la paie, des ressources humaines et des écritures comptables.");

            PdfPTable tocTable = new PdfPTable(3);
            tocTable.WidthPercentage = 100f;
            tocTable.SpacingBefore = 10f;
            tocTable.SetWidths(new float[] { 15f, 65f, 20f });

            // En-têtes
            AddTableHeader(tocTable, "Section");
            AddTableHeader(tocTable, "Intitulé & Modules Couverts");
            AddTableHeader(tocTable, "Statut / Phase");

            AddTocRow(tocTable, "Section 1", "Introduction & Architecture Système (Multi-Dossiers, SQLite, Navigation)", "SOCLE");
            AddTocRow(tocTable, "Section 2", "Gestion du Personnel & Fiches Collaborateurs [SAL-01]", "PHASE 1");
            AddTocRow(tocTable, "Section 3", "Saisie des Variables du Mois & Primes/Indemnités [PAI-02]", "PHASE 1");
            AddTocRow(tocTable, "Section 4", "Moteur de Calcul, IUTS & Bulletins Certifiés [PAI-01, BTH-01]", "PHASE 1");
            AddTocRow(tocTable, "Section 5", "Congés Payés, Absences & Régularisation Comptable [CON-01]", "PHASE 2");
            AddTocRow(tocTable, "Section 6", "Grand Livre de Paie & Registre Mensuel [ETA-01, ETA-02]", "PHASE 2");
            AddTocRow(tocTable, "Section 7", "Intégration Comptable SYSCOHADA & Génération d'OD [CPT-01]", "PHASE 3");
            AddTocRow(tocTable, "Section 8", "Règlements Bancaires, Contrôle RIB & Ordres de Virement [CPT-02]", "PHASE 3");
            AddTocRow(tocTable, "Section 9", "Déclarations Fiscales (IUTS) et Sociales (CNSS) [IMP-01, SOC-01]", "PHASE 2");
            AddTocRow(tocTable, "Section 10", "Administration, Journal d'Audit SHA-256 & Clôture [ADM-01, ADM-02]", "PHASE 3");
            AddTocRow(tocTable, "Section 11", "Répertoire des 8 Captures d'Écran Recommandées", "ILLUSTRATIONS");

            doc.Add(tocTable);

            AddCallout(doc, "Méthodologie du Manuel",
                "Chaque section détaille précisément les règles légales applicables, les formules de calcul intégrées, " +
                "les contrôles de cohérence automatiques, les flux comptables SYSCOHADA associés ainsi que " +
                "l'emplacement réservé aux captures d'écran illustratives de l'application.", "info");
        }

        static void AddTableHeader(PdfPTable table, string title)
        {
            PdfPCell c = new PdfPCell(new Phrase(title, FontTableHeader));
            c.BackgroundColor = ColorTealPrimary;
            c.HorizontalAlignment = Element.ALIGN_CENTER;
            c.VerticalAlignment = Element.ALIGN_MIDDLE;
            c.Padding = 6f;
            c.BorderColor = ColorTealDark;
            table.AddCell(c);
        }

        static void AddTocRow(PdfPTable table, string sec, string title, string status)
        {
            PdfPCell c1 = new PdfPCell(new Phrase(sec, FontTableCellBold));
            c1.Padding = 6f;
            c1.BackgroundColor = ColorTealLight;
            c1.BorderColor = ColorBorder;

            PdfPCell c2 = new PdfPCell(new Phrase(title, FontTableCell));
            c2.Padding = 6f;
            c2.BorderColor = ColorBorder;

            PdfPCell c3 = new PdfPCell(new Phrase(status, FontTableCellBold));
            c3.Padding = 6f;
            c3.HorizontalAlignment = Element.ALIGN_CENTER;
            c3.BorderColor = ColorBorder;

            table.AddCell(c1);
            table.AddCell(c2);
            table.AddCell(c3);
        }

        static void BuildSection1_Introduction(Document doc)
        {
            AddHeading1(doc, "Section 1 : Architecture Système & Navigation", "SOCLE ARCHITECTURAL");

            AddParagraph(doc, "Sigma Paie & RH est une solution logicielle professionnelle sous architecture Windows Presentation Foundation (WPF) " +
                "et base de données relationnelle SQLite dédiée aux Directions des Ressources Humaines, Cabinets Comptables et Directions Financières.");

            AddHeading2(doc, "1.1 Principe d'Isolation Multi-Dossiers");
            AddParagraph(doc, "Chaque entreprise, filiale ou établissement constitue un dossier indépendant. " +
                "Toutes les données salariales, variables de paie, pièces comptables et historiques d'audit sont stockés dans un fichier SQLite dédié " +
                "(ex: dossier_entrepriseA.db). Cette étanchéité garantit le strict respect de la confidentialité des rémunérations et permet " +
                "des sauvegardes individualisées sans risque de contamination croisée.");

            AddHeading2(doc, "1.2 Rail de Navigation Latéral & Ergonomie");
            AddParagraph(doc, "L'interface utilisateur repose sur un rail de navigation moderne situé sur la gauche de l'écran. " +
                "Il offre un accès direct et contextuel aux différents espaces de travail :");

            PdfPTable navTable = new PdfPTable(2);
            navTable.WidthPercentage = 100f;
            navTable.SpacingBefore = 6f;
            navTable.SpacingAfter = 10f;
            navTable.SetWidths(new float[] { 30f, 70f });

            AddMetaRow(navTable, "Sélection du Dossier", "Choix de l'entreprise active, création de dossier, bascule instantanée");
            AddMetaRow(navTable, "Gestion Collaborateurs", "Fiches salariés, état civil, contrats, charges de famille, coordonnées RIB");
            AddMetaRow(navTable, "Variables Mensuelles", "Saisie matricielle des primes, heures, indemnités et retenues");
            AddMetaRow(navTable, "Calcul & Bulletins", "Moteur de paie unitaire et par lot (batch), aperçu temps réel");
            AddMetaRow(navTable, "Congés & Absences", "Compteurs de droits, décomptes d'indemnités et régularisation d'absences");
            AddMetaRow(navTable, "Grand Livre de Paie", "Registre mensuel, récapitulatif annuel, contrôle de concordance");
            AddMetaRow(navTable, "Écritures Comptables", "Génération des OD SYSCOHADA, ventilation Débit/Crédit équilibrée");
            AddMetaRow(navTable, "Règlements & Virements", "Bordereaux bancaires, analyse des RIB et fichiers de télévirement");
            AddMetaRow(navTable, "Audit & Clôture", "Journal d'audit infalsifiable avec hash SHA-256 et verrouillage mensuel");

            doc.Add(navTable);

            // Screenshot Placeholder 1
            AddScreenshotPlaceholder(doc, 1, "Écran d'Accueil & Rail de Navigation Latéral",
                "Vue principale de l'application affichant le tableau de bord, le sélecteur de dossier d'entreprise et le menu latéral complet.",
                new string[] {
                    "Rail de navigation gauche avec icônes et intitulés des modules",
                    "Bannière supérieure indiquant la Raison Sociale et la Période Active",
                    "Indicateurs de bord (KPIs) : Effectif actif, Masse salariale brute, Statut de la période"
                });
        }

        static void BuildSection2_Salaries(Document doc)
        {
            AddHeading1(doc, "Section 2 : Fiches Collaborateurs & Charges de Famille", "SAL-01");

            AddParagraph(doc, "La gestion du personnel constitue le socle du système de paie. " +
                "Chaque collaborateur dispose d'une fiche complète contenant l'intégralité des paramètres administratifs, légaux et contractuels.");

            AddHeading2(doc, "2.1 Données Administratives & Paramètres Salariaux");
            AddParagraph(doc, "La fiche salarié regroupe les rubriques fondamentales suivantes :");
            AddParagraph(doc, "• Identification : Matricule unique, Nom, Prénom, Date et lieu de naissance, Genre, Nationalité.\n" +
                "• Contrat de Travail : Nature (CDI, CDD, Stage, Intérim), Date d'embauche, Qualification, Catégorie socio-professionnelle, Service/Département.\n" +
                "• Rémunération de Base : Salaire de base catégoriel négocié, base horaire contractuelle (ex: 173.33 heures mensuelles).");

            AddHeading2(doc, "2.2 Détermination Rigoureuse des Charges de Famille");
            AddParagraph(doc, "En matière fiscale et sociale en Afrique de l'Ouest (UEMOA/SYSCOHADA), la situation de famille " +
                "conditionne directement les réductions d'impôt sur les traitements et salaires (IUTS) :");

            PdfPTable famTable = new PdfPTable(2);
            famTable.WidthPercentage = 100f;
            famTable.SpacingBefore = 6f;
            famTable.SpacingAfter = 10f;
            famTable.SetWidths(new float[] { 35f, 65f });

            AddMetaRow(famTable, "Situation Matrimoniale", "Célibataire, Marié(e), Divorcé(e), Veuf/Veuve (justificatifs requis)");
            AddMetaRow(famTable, "Enfants à Charge Légaux", "Enfants mineurs (< 21 ans ou < 25 ans si étudiants poursuivant des études)");
            AddMetaRow(famTable, "Plafonnement des Réductions", "Application stricte du barème officiel (réduction de 8% à 40% selon les personnes à charge)");
            AddMetaRow(famTable, "Impact sur le Net", "La réduction pour charges de famille s'applique directement sur l'IUTS brut calculé.");

            doc.Add(famTable);

            AddHeading2(doc, "2.3 Coordonnées Bancaires & Validité du RIB");
            AddParagraph(doc, "Pour chaque salarié rémunéré par virement bancaire, le système stocke et valide l'intégrité du RIB complet :\n" +
                "• Code Banque (5 chiffres) | Code Guichet (5 chiffres) | Numéro de Compte (11 ou 12 caractères) | Clé RIB (2 chiffres).\n" +
                "Le système calcule automatiquement la clé modulo 97 pour prévenir toute erreur de saisie avant l'émission des virements.");

            // Screenshot Placeholder 2
            AddScreenshotPlaceholder(doc, 2, "Fiche Salarié & Configuration des Charges de Famille",
                "Formulaire complet d'édition d'un collaborateur mettant en évidence l'onglet État Civil, Situation Matrimoniale et Coordonnées Bancaires.",
                new string[] {
                    "Champs Matricule, Nom, Prénom, Date d'embauche et Salaire de base",
                    "Sélecteur Situation matrimoniale (Marié, Célibataire, etc.) et Nombre d'enfants à charge",
                    "Section Coordonnées Bancaires avec Banque, Guichet, Compte et Clé RIB",
                    "Bouton 'Enregistrer' et indicateur de validation de la fiche"
                });
        }

        static void BuildSection3_Variables(Document doc)
        {
            AddHeading1(doc, "Section 3 : Saisie des Variables Mensuelles & Typologie des Primes", "PAI-02");

            AddParagraph(doc, "Chaque mois, le gestionnaire de paie ouvre la période active et procède à la saisie " +
                "des éléments variables propres au mois de paie considéré.");

            AddHeading2(doc, "3.1 Typologie Légale des Rubriques de Rémunération");
            AddParagraph(doc, "Le moteur de paie applique une distinction fiscale et sociale rigoureuse entre éléments soumis et éléments exonérés :");

            PdfPTable rubTable = new PdfPTable(3);
            rubTable.WidthPercentage = 100f;
            rubTable.SpacingBefore = 8f;
            tocTable_Header(rubTable, "Nature de la Rubrique", "Régime Fiscal (IUTS)", "Régime Social (CNSS)");

            AddRubRow(rubTable, "Salaire de base", "100% Imposable", "100% Soumis");
            AddRubRow(rubTable, "Heures supplémentaires", "100% Imposable (avec majorations)", "100% Soumis");
            AddRubRow(rubTable, "Prime d'ancienneté", "100% Imposable", "100% Soumis");
            AddRubRow(rubTable, "Prime de rendement / bilan", "100% Imposable", "100% Soumis");
            AddRubRow(rubTable, "Prime de responsabilité", "100% Imposable", "100% Soumis");
            AddRubRow(rubTable, "Indemnité de transport", "Exonérée dans la limite légale", "Exonérée dans la limite légale");
            AddRubRow(rubTable, "Panier repas / Cantine", "Exonéré si justification de déplacement", "Exonéré sous conditions");
            AddRubRow(rubTable, "Indemnité de logement", "Évaluée selon avantage en nature", "Soumise selon barème légal");

            doc.Add(rubTable);

            AddHeading2(doc, "3.2 Grille Matricielle de Saisie & Contrôles Temps Réel");
            AddParagraph(doc, "L'interface offre une grille de saisie en masse permettant au gestionnaire de renseigner d'un coup " +
                "les primes et indemnités de tous les salariés actifs sans devoir ouvrir chaque fiche individuellement. " +
                "Des alertes visuelles signalent immédiatement toute saisie anormale (valeur négative, dépassement de seuil, caractère invalide).");

            // Screenshot Placeholder 3
            AddScreenshotPlaceholder(doc, 3, "Grille Matricielle des Variables Mensuelles",
                "Tableau de saisie groupée des variables de paie du mois affichant l'ensemble des collaborateurs du dossier.",
                new string[] {
                    "Lignes représentant les salariés avec matricule et nom complet",
                    "Colonnes éditables pour : Primes rendement, Primes responsabilité, Indemnités transport, Avances",
                    "Totaux récapitulatifs automatiques en bas de colonne",
                    "Bouton 'Valider les variables du mois' avec indicateur de sauvegarde"
                });
        }

        static void tocTable_Header(PdfPTable table, string c1, string c2, string c3)
        {
            table.SetWidths(new float[] { 40f, 30f, 30f });
            AddTableHeader(table, c1);
            AddTableHeader(table, c2);
            AddTableHeader(table, c3);
        }

        static void AddRubRow(PdfPTable table, string col1, string col2, string col3)
        {
            PdfPCell cell1 = new PdfPCell(new Phrase(col1, FontTableCellBold));
            cell1.Padding = 5f;
            cell1.BorderColor = ColorBorder;

            PdfPCell cell2 = new PdfPCell(new Phrase(col2, FontTableCell));
            cell2.Padding = 5f;
            cell2.BorderColor = ColorBorder;

            PdfPCell cell3 = new PdfPCell(new Phrase(col3, FontTableCell));
            cell3.Padding = 5f;
            cell3.BorderColor = ColorBorder;

            table.AddCell(cell1);
            table.AddCell(cell2);
            table.AddCell(cell3);
        }

        static void BuildSection4_CalculsBulletins(Document doc)
        {
            AddHeading1(doc, "Section 4 : Moteur de Calcul & Bulletins Certifiés", "PAI-01 & BTH-01");

            AddParagraph(doc, "Le moteur de paie exécute l'algorithme de calcul conforme au barème fiscal IUTS 2026 " +
                "et aux cotisations sociales en vigueur.");

            AddHeading2(doc, "4.1 Algorithme Pas à Pas du Calcul de Paie");
            AddParagraph(doc, "Pour chaque salarié, le calcul procède selon la séquence ordonnée suivante :\n" +
                "1. Brut Total = Salaire de Base + Heures Supplémentaires + Primes + Indemnités.\n" +
                "2. Cotisation CNSS Ouvrière = Assiette Sociale × 5.5% (Retraite/Vieillesse).\n" +
                "3. Brut Imposable Fiscal = Brut Total - Éléments Exonérés - Cotisation CNSS Salariale.\n" +
                "4. IUTS Brut = Application du barème progressif par tranches sur le revenu net imposable après déduction forfaitaire légale.\n" +
                "5. IUTS Net = IUTS Brut - Réduction pour Charges de Famille (selon le nombre d'enfants et le conjoint).\n" +
                "6. Total Retenues Salariales = CNSS Ouvrière + IUTS Net + Avances sur salaire + Saisies-arrêts.\n" +
                "7. Net à Payer Collaborateur = Brut Total - Total Retenues Salariales.");

            AddHeading2(doc, "4.2 Charges Patronales & Coût Global Entreprise");
            AddParagraph(doc, "En parallèle, le moteur calcule les cotisations à la charge exclusive de l'employeur :\n" +
                "• CNSS Prestations Familiales & Risques Professionnels : 7.0% à 9.0% selon la convention.\n" +
                "• CNSS Retraite Patronale : 9.0% à 10.5% (Total CNSS Patronale moyen : 16.0% à 17.5%).\n" +
                "• Coût Global Entreprise = Salaire Brut Total + Total Charges Sociales Patronales.");

            AddHeading2(doc, "4.3 Sécurisation par QR Code SHA-256 & Calcul par Lot");
            AddParagraph(doc, "Le module BTH-01 permet le traitement instantané de centaines de salariés en un clic (traitement batch multithreadé). " +
                "Chaque bulletin généré au format PDF officiel intègre un QR Code de sécurité infalsifiable encodant les clés fondamentales " +
                "(Matricule, Période, Brut, Net à payer, Empreinte SHA-256) permettant un contrôle d'authenticité immédiat par smartphone.");

            // Screenshot Placeholder 4
            AddScreenshotPlaceholder(doc, 4, "Bulletin de Paie Officiel avec QR Code SHA-256",
                "Spécimen d'un bulletin de paie généré au format officiel A4 avec ventilation complète des rubriques et QR Code.",
                new string[] {
                    "En-tête de l'entreprise avec Raison Sociale, NIF, N° CNSS",
                    "Bloc collaborateur : Nom, Prénom, Matricule, Emploi, Catégorie, Charges de famille",
                    "Tableau détaillé des cotisations : Salaires de base, Primes, CNSS 5.5%, IUTS 2026, Net à payer",
                    "QR Code de certification SHA-256 en bas de page avec la mention de contrôle"
                });
        }

        static void BuildSection5_CongesAbsences(Document doc)
        {
            AddHeading1(doc, "Section 5 : Congés, Absences & Traitement Comptable", "CON-01");

            AddParagraph(doc, "Le module CON-01 assure la gestion rigoureuse des droits à congés payés, " +
                "le décompte des absences et leur traitement comptable strict afin de garantir l'équilibre parfait de la balance.");

            AddHeading2(doc, "5.1 Droits à Congés & Indemnité Compensatrice");
            AddParagraph(doc, "Conformément à l'Article 115 du Code du Travail :\n" +
                "• Acquisition des droits : 2.5 jours ouvrables de congés par mois de travail effectif (soit 30 jours ouvrables par an).\n" +
                "• Décompte des congés pris : Selon la méthode des jours ouvrables ou ouvrés contractuelle.\n" +
                "• Indemnité de congé : Application de la règle du maintien de salaire comparée à la règle du 1/12ème des rémunérations brutes perçues au cours de la période de référence.");

            AddHeading2(doc, "5.2 Décompte & Retenue sur Salaire pour Absences");
            AddParagraph(doc, "Pour les absences non rémunérées (absences injustifiées, convenance personnelle, congés sans solde) :\n" +
                "• Taux journalier moyen = Salaire Brut / 30 (ou heures réelles du mois).\n" +
                "• La retenue pour absence s'impute en diminution du salaire brut du mois.");

            AddHeading2(doc, "5.3 Traitement Comptable Strict des Congés & Absences");
            AddParagraph(doc, "Lors du traitement de la paie, l'impact des congés et des provisions doit être parfaitement équilibré dans les écritures :\n" +
                "• Lors de la prise de congé : Débit 6611 (Maintien de salaire) / Crédit 4220.\n" +
                "• Clôture mensuelle des provisions : Débit 6631 (Provisions pour congés payés) / Crédit 4281 (Dettes provisionnées pour congés).\n" +
                "• Reprise sur provision : Débit 4281 / Crédit 791 (ou régularisation directe sur le compte 6631). " +
                "Cette neutralisation rigoureuse élimine tout risque de déséquilibre lors de l'exportation des écritures d'OD.");

            AddCallout(doc, "Principe de Non-Déséquilibre Comptable",
                "Toute absence déduite du brut réduit proportionnellement la base imposable et la dette envers le personnel (Compte 4220). " +
                "Le système effectue une double vérification de parité Débit = Crédit avant de permettre la validation définitive.", "warning");
        }

        static void BuildSection6_LivrePaie(Document doc)
        {
            AddHeading1(doc, "Section 6 : Grand Livre de Paie & Registre Mensuel", "ETA-01 & ETA-02");

            AddParagraph(doc, "Le Grand Livre de Paie récapitule de manière exhaustive l'ensemble des flux financiers " +
                "liés aux rémunérations du mois pour tous les collaborateurs de l'entreprise.");

            AddHeading2(doc, "6.1 Structure des Colonnes du Grand Livre");
            AddParagraph(doc, "Le registre de paie mensuel présente les colonnes fondamentales suivantes pour chaque salarié :\n" +
                "• 1. Salaires de base contractuels\n" +
                "• 2. Heures supplémentaires et majorations\n" +
                "• 3. Primes et gratifications imposables\n" +
                "• 4. Indemnités et primes non imposables (transport, panier)\n" +
                "• 5. Salaire Brut Global\n" +
                "• 6. Retenue CNSS Salariale (5.5%)\n" +
                "• 7. Retenue Fiscale IUTS Net\n" +
                "• 8. Avances, acomptes et prêts déduits\n" +
                "• 9. Total des Retenues Salariales\n" +
                "• 10. Salaire Net à Payer Collaborateur\n" +
                "• 11. Charges Sociales Patronales CNSS (16.0% à 17.5%)\n" +
                "• 12. Coût Global Entreprise");

            AddHeading2(doc, "6.2 Contrôle Fondamental d'Équilibre Mathématique");
            AddParagraph(doc, "Le système intègre un moteur de contrôle qui vérifie en temps réel les deux égalités comptables universelles :\n" +
                "Équation 1 : TOTAL SALAIRES BRUTS - TOTAL RETENUES SALARIALES = TOTAL NETS À PAYER (au centime près)\n" +
                "Équation 2 : TOTAL SALAIRES BRUTS + TOTAL CHARGES PATRONALES = TOTAL COÛT GLOBAL ENTREPRISE");

            // Screenshot Placeholder 5
            AddScreenshotPlaceholder(doc, 5, "Grand Livre de Paie Mensuel & Totaux de Contrôle",
                "Tableau récapitulatif du Grand Livre de Paie affichant la liste des salariés avec les totaux généraux et la jauge d'équilibrage.",
                new string[] {
                    "Lignes des salariés avec colonnes Brut, CNSS, IUTS, Net à payer, Charges patronales",
                    "Ligne de 'Totaux Généraux' surlignée en bas de tableau",
                    "Bandeau de conformité : 'Équilibre comptable vérifié : Écart = 0 FCFA'",
                    "Boutons d'exportation vers Excel (.xlsx) et PDF officiel (.pdf)"
                });
        }

        static void BuildSection7_ComptabiliteSYSCOHADA(Document doc)
        {
            AddHeading1(doc, "Section 7 : Intégration Comptable SYSCOHADA (OD de Paie)", "CPT-01");

            AddParagraph(doc, "Le module CPT-01 génère automatiquement la pièce d'Opérations Diverses (OD) de paie " +
                "selon le plan comptable SYSCOHADA Révisé. Cette pièce est directement exportable vers les progiciels comptables " +
                "(Sage 100, Cegid, SAP, Tompro, Excel).");

            AddHeading2(doc, "7.1 Schéma d'Écritures Normalisé SYSCOHADA");
            AddParagraph(doc, "Pour chaque période de paie mensuelle, les écritures comptables suivantes sont constatées :");

            PdfPTable odTable = new PdfPTable(4);
            odTable.WidthPercentage = 100f;
            odTable.SpacingBefore = 8f;
            odTable.SpacingAfter = 10f;
            odTable.SetWidths(new float[] { 15f, 50f, 17f, 18f });

            AddTableHeader(odTable, "Compte");
            AddTableHeader(odTable, "Intitulé du Compte SYSCOHADA");
            AddTableHeader(odTable, "Débit (FCFA)");
            AddTableHeader(odTable, "Crédit (FCFA)");

            AddOdRow(odTable, "6611", "Rémunérations directes versées au personnel national", "X XXX XXX", "-");
            AddOdRow(odTable, "6612", "Primes et gratifications diverses", "XXX XXX", "-");
            AddOdRow(odTable, "6613", "Indemnités et avantages divers en espèces", "XXX XXX", "-");
            AddOdRow(odTable, "4220", "Personnel, rémunérations dues (Salaires bruts)", "-", "X XXX XXX");
            AddOdRow(odTable, "4220", "Personnel, rémunérations dues (Retenues salariales)", "X XXX XXX", "-");
            AddOdRow(odTable, "4311", "Sécurité Sociale - Cotisations ouvrières (5.5%)", "-", "XXX XXX");
            AddOdRow(odTable, "4471", "État, IUTS retenu à la source sur traitements", "-", "XXX XXX");
            AddOdRow(odTable, "4210", "Personnel, avances et acomptes récupérés", "-", "XX XXX");
            AddOdRow(odTable, "4220", "Solde Net dû au personnel (règlement ultérieur)", "-", "X XXX XXX");
            AddOdRow(odTable, "6641", "Charges sociales patronales - CNSS", "XXX XXX", "-");
            AddOdRow(odTable, "4313", "Sécurité Sociale - Cotisations patronales dues", "-", "XXX XXX");

            doc.Add(odTable);

            AddHeading2(doc, "7.2 Contrôle de Parité Débit / Crédit");
            AddParagraph(doc, "Le système interdit formellement l'exportation ou la validation d'une pièce comptable si la somme des Débits " +
                "est différente de la somme des Crédits. L'écart est calculé avec une précision au centime près.");

            // Screenshot Placeholder 6
            AddScreenshotPlaceholder(doc, 6, "Pièce d'OD Comptable SYSCOHADA Générée",
                "Grille de visualisation de la pièce d'OD comptable mensuelle prête pour l'exportation vers le progiciel financier.",
                new string[] {
                    "Numéro de pièce comptable, Date de valeur et Libellé d'écriture (ex: OD PAIE 09/2026)",
                    "Colonnes : N° Compte, Libellé compte, Débit, Crédit, Référence salarié/service",
                    "Totaux Débit et Crédit en bas avec indicateur : 'Balance équilibrée'",
                    "Boutons d'export : 'Exporter format Sage 100 (.txt)', 'Exporter Excel (.xlsx)'"
                });
        }

        static void AddOdRow(PdfPTable table, string compte, string libelle, string debit, string credit)
        {
            PdfPCell c1 = new PdfPCell(new Phrase(compte, FontCode));
            c1.Padding = 5f;
            c1.BorderColor = ColorBorder;

            PdfPCell c2 = new PdfPCell(new Phrase(libelle, FontTableCell));
            c2.Padding = 5f;
            c2.BorderColor = ColorBorder;

            PdfPCell c3 = new PdfPCell(new Phrase(debit, FontTableCellBold));
            c3.HorizontalAlignment = Element.ALIGN_RIGHT;
            c3.Padding = 5f;
            c3.BorderColor = ColorBorder;

            PdfPCell c4 = new PdfPCell(new Phrase(credit, FontTableCellBold));
            c4.HorizontalAlignment = Element.ALIGN_RIGHT;
            c4.Padding = 5f;
            c4.BorderColor = ColorBorder;

            table.AddCell(c1);
            table.AddCell(c2);
            table.AddCell(c3);
            table.AddCell(c4);
        }

        static void BuildSection8_VirementsBancaires(Document doc)
        {
            AddHeading1(doc, "Section 8 : Règlements Bancaires & Ordres de Virement", "CPT-02");

            AddParagraph(doc, "Le module CPT-02 organise le règlement effectif des salaires nets calculés " +
                "via l'émission d'ordres de virement groupés et de bordereaux bancaires individualisés par banque de domiciliation.");

            AddHeading2(doc, "8.1 Regroupement par Banque de Domiciliation");
            AddParagraph(doc, "Le système trie automatiquement les salariés selon leur banque d'accueil (ex: Coris Bank, Bank of Africa, " +
                "Ecobank, UBA, Société Générale, etc.) et génère un bordereau récapitulatif distinct pour chaque établissement financier.");

            AddHeading2(doc, "8.2 Contrôle Préalable de Cohérence des RIB");
            AddParagraph(doc, "Avant toute génération de virement, l'application vérifie l'exhaustivité des coordonnées bancaires. " +
                "Si un salarié présente un RIB manquant ou invalide, il est immédiatement placé dans la liste 'À Régler par Chèque / Espèces' " +
                "pour éviter le rejet du fichier par la chambre de compensation interbancaire.");

            AddHeading2(doc, "8.3 Fichiers de Télévirement Normalisés");
            AddParagraph(doc, "L'application produit des fichiers de télévirement au format universel CSV / texte conforme aux exigences " +
                "des plateformes de banque en ligne (e-banking professionnel), incluant la référence du donneur d'ordre et les libellés personnalisés.");

            // Screenshot Placeholder 7
            AddScreenshotPlaceholder(doc, 7, "Interface des Ordres de Virement Bancaire",
                "Module de préparation des virements affichant les collaborateurs bancarisés groupés par banque de domiciliation.",
                new string[] {
                    "Sélecteur de la banque émettrice de l'entreprise (Compte débiteur)",
                    "Liste des virements avec Nom collaborateur, RIB complet, Montant Net à virer",
                    "Total global des virements et nombre de salariés concernés",
                    "Bouton 'Générer la lettre d'ordre de virement' et 'Exporter le fichier bancaire'"
                });
        }

        static void BuildSection9_Declarations(Document doc)
        {
            AddHeading1(doc, "Section 9 : Déclarations Fiscales (IUTS) & Sociales (CNSS)", "IMP-01 & SOC-01");

            AddParagraph(doc, "Le système automatise l'édition des états préparatoires et des bordereaux officiels " +
                "pour la Direction Générale des Impôts (DGI) et la Caisse Nationale de Sécurité Sociale (CNSS).");

            AddHeading2(doc, "9.1 Déclaration Mensuelle de l'IUTS (Module IMP-01)");
            AddParagraph(doc, "Chaque mois, l'entreprise doit déposer son état récapitulatif de l'IUTS retenu à la source. " +
                "Le module IMP-01 édite le bordereau officiel avec :\n" +
                "• Effectif imposable déclaré\n" +
                "• Masse brute globale imposable\n" +
                "• Ventilation des bases imposables par tranche de barème\n" +
                "• Total des retenues IUTS nettes à reverser au Trésor Public");

            AddHeading2(doc, "9.2 Déclaration Mensuelle / Trimestrielle CNSS (Module SOC-01)");
            AddParagraph(doc, "Le module SOC-01 compile l'Appel de Cotisations Sociales CNSS conformément aux taux statutaires :\n" +
                "• Cotisations Salariales (5.5%) : Précomptées sur le bulletin de chaque travailleur.\n" +
                "• Cotisations Patronales : Prestations familiales, risques professionnels et retraite employeur.\n" +
                "• Total général à verser à la caisse de sécurité sociale avant la date limite légale (ex: 15 du mois suivant).");

            AddCallout(doc, "Archivage Réglementaire",
                "Tous les états déclaratifs sont archivés sous format PDF certifié avec empreinte d'authentification " +
                "pour faciliter les contrôles fiscaux et sociaux ultérieurs.", "info");
        }

        static void BuildSection10_AuditSecurite(Document doc)
        {
            AddHeading1(doc, "Section 10 : Administration, Audit SHA-256 & Clôture", "ADM-01 & ADM-02");

            AddParagraph(doc, "La sécurité des données de rémunération repose sur une traçabilité intégrale de toutes les opérations " +
                "et une procédure formelle de clôture de période.");

            AddHeading2(doc, "10.1 Journal d'Audit & Traçabilité Infalsifiable (ADM-02)");
            AddParagraph(doc, "Chaque action critique effectuée dans Sigma Paie & RH génère une entrée d'audit horodatée à la milliseconde près :\n" +
                "• Date et heure précise (UTC et heure locale)\n" +
                "• Utilisateur connecté et poste de travail\n" +
                "• Type d'opération (Création salarié, Modification de prime, Calcul paie, Export comptable, etc.)\n" +
                "• Détail complet avant/après modification\n" +
                "• Hash de contrôle SHA-256 garantissant la non-altération du journal par un tiers");

            AddHeading2(doc, "10.2 Procédure de Clôture Mensuelle Irréversible");
            AddParagraph(doc, "Lorsque les bulletins sont distribués, les virements exécutés et l'OD comptable exportée, " +
                "le gestionnaire exécute la Clôture de la Période. Cette action verrouille définitivement le mois de paie :\n" +
                "• Interdiction formelle de toute modification de salaire ou de variable rétroactive.\n" +
                "• Intangibilité des écritures comptables déjà transférées au grand livre.\n" +
                "• Initialisation automatique de la période suivante avec reconduction des paramètres permanents.");

            // Screenshot Placeholder 8
            AddScreenshotPlaceholder(doc, 8, "Journal d'Audit & Traçabilité des Salaires",
                "Interface du journal d'audit affichant l'historique complet des opérations horodatées avec filtrage et empreintes SHA-256.",
                new string[] {
                    "Tableau des événements avec Date/Heure, Utilisateur, Catégorie, Description",
                    "Filtres par plage de dates, collaborateur concerné ou type d'action",
                    "Empreinte numérique SHA-256 affichée en regard de chaque opération",
                    "Bouton d'exportation du journal d'audit au format PDF sécurisé"
                });
        }

        static void BuildSection11_RepertoireCaptures(Document doc)
        {
            AddHeading1(doc, "Section 11 : Répertoire Synoptique des 8 Illustrations Recommandées", "ILLUSTRATIONS");

            AddParagraph(doc, "Pour parfaire l'aspect visuel de ce manuel d'exploitation, l'utilisateur ou l'administrateur peut " +
                "capturer les 8 écrans opérationnels listés ci-dessous et les intégrer aux emplacements prévus :");

            PdfPTable repTable = new PdfPTable(4);
            repTable.WidthPercentage = 100f;
            repTable.SpacingBefore = 8f;
            repTable.SpacingAfter = 12f;
            repTable.SetWidths(new float[] { 10f, 15f, 40f, 35f });

            AddTableHeader(repTable, "N°");
            AddTableHeader(repTable, "Module");
            AddTableHeader(repTable, "Vue / Écran de l'Application");
            AddTableHeader(repTable, "Éléments Visuels Essentiels");

            AddRepRow(repTable, "1", "SOCLE", "Écran d'Accueil & Rail Latéral", "Menu navigation, sélection dossier, KPIs");
            AddRepRow(repTable, "2", "SAL-01", "Fiche Collaborateur", "État civil, charges de famille, coordonnées RIB");
            AddRepRow(repTable, "3", "PAI-02", "Grille des Variables Mensuelles", "Saisie matricielle primes/heures, totaux");
            AddRepRow(repTable, "4", "PAI-01", "Bulletin de Paie Officiel", "Rubriques détaillées, net à payer, QR Code SHA-256");
            AddRepRow(repTable, "5", "ETA-01", "Grand Livre de Paie", "Tableau récapitulatif, contrôle d'équilibre = 0");
            AddRepRow(repTable, "6", "CPT-01", "Pièce d'OD Comptable SYSCOHADA", "Comptes 6611, 4220, 4311, 4471, balance Débit/Crédit");
            AddRepRow(repTable, "7", "CPT-02", "Ordres de Virement Bancaire", "Ventilation par banque, analyse RIB, montants");
            AddRepRow(repTable, "8", "ADM-02", "Journal d'Audit Infalsifiable", "Horodatage précis, actions utilisateurs, hash SHA-256");

            doc.Add(repTable);

            AddCallout(doc, "Fourniture de vos Captures d'Écran",
                "Vous pouvez à tout moment transmettre ces captures d'écran sous format PNG ou JPEG (ex: en les déposant ou en indiquant votre accord). " +
                "Elles seront automatiquement incorporées aux cadres dédiés pour former le document final illustré.", "success");

            Paragraph pEnd = new Paragraph("— Fin du Guide d'Utilisation Officiel — Sigma Paie & RH —", FontFactory.GetFont(FontFactory.HELVETICA_BOLD, 9, ColorMutedText));
            pEnd.Alignment = Element.ALIGN_CENTER;
            pEnd.SpacingBefore = 20f;
            doc.Add(pEnd);
        }

        static void AddRepRow(PdfPTable table, string num, string mod, string vue, string elements)
        {
            PdfPCell c1 = new PdfPCell(new Phrase(num, FontTableCellBold));
            c1.HorizontalAlignment = Element.ALIGN_CENTER;
            c1.Padding = 5f;
            c1.BorderColor = ColorBorder;

            PdfPCell c2 = new PdfPCell(new Phrase(mod, FontTableCellBold));
            c2.HorizontalAlignment = Element.ALIGN_CENTER;
            c2.Padding = 5f;
            c2.BorderColor = ColorBorder;

            PdfPCell c3 = new PdfPCell(new Phrase(vue, FontTableCell));
            c3.Padding = 5f;
            c3.BorderColor = ColorBorder;

            PdfPCell c4 = new PdfPCell(new Phrase(elements, FontTableCell));
            c4.Padding = 5f;
            c4.BorderColor = ColorBorder;

            table.AddCell(c1);
            table.AddCell(c2);
            table.AddCell(c3);
            table.AddCell(c4);
        }

        #endregion
    }
}
