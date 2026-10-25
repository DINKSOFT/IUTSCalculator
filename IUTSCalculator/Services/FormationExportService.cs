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
    /// Service d'exportation pour le module RH-04 Formation.
    /// Produit les attestations officielles A4 individuelles en PDF (iTextSharp),
    /// le bilan annuel de formation et le classeur de pilotage 4 feuilles en Excel (ClosedXML).
    /// </summary>
    public class FormationExportService
    {
        #region Constantes & Couleurs

        // Palette graphique Excel
        private static readonly XLColor CouleurAccent = XLColor.FromArgb(20, 103, 90);     // #14675A Vert émeraude
        private static readonly XLColor CouleurEncre = XLColor.FromArgb(23, 33, 31);      // #17211F Texte foncé
        private static readonly XLColor CouleurDiscret = XLColor.FromArgb(121, 131, 126); // #79837E Gris moyen
        private static readonly XLColor CouleurTrame = XLColor.FromArgb(245, 246, 245);   // #F5F6F5 Fond léger
        private static readonly XLColor CouleurBordure = XLColor.FromArgb(203, 209, 204); // #CBD1CC
        private static readonly XLColor CouleurVert = XLColor.FromArgb(27, 94, 32);       // #1B5E20
        private static readonly XLColor CouleurAmbre = XLColor.FromArgb(230, 81, 0);      // #E65100
        private static readonly XLColor CouleurRouge = XLColor.FromArgb(198, 40, 40);     // #C62828

        // Palette graphique PDF (iTextSharp)
        private static readonly BaseColor PdfPrimaryColor = new BaseColor(23, 33, 31);
        private static readonly BaseColor PdfAccentColor = new BaseColor(20, 103, 90);
        private static readonly BaseColor PdfLightBg = new BaseColor(245, 246, 245);
        private static readonly BaseColor PdfBorderColor = new BaseColor(203, 209, 204);
        private static readonly BaseColor PdfMutedColor = new BaseColor(100, 116, 139);

        private static readonly Font PdfTitleFont = new Font(Font.FontFamily.HELVETICA, 13, Font.BOLD, PdfAccentColor);
        private static readonly Font PdfSubTitleFont = new Font(Font.FontFamily.HELVETICA, 9f, Font.NORMAL, PdfMutedColor);
        private static readonly Font PdfSectionFont = new Font(Font.FontFamily.HELVETICA, 10f, Font.BOLD, PdfAccentColor);
        private static readonly Font PdfHeaderCellFont = new Font(Font.FontFamily.HELVETICA, 8f, Font.BOLD, BaseColor.WHITE);
        private static readonly Font PdfCellBoldFont = new Font(Font.FontFamily.HELVETICA, 8f, Font.BOLD, PdfPrimaryColor);
        private static readonly Font PdfCellNormalFont = new Font(Font.FontFamily.HELVETICA, 8f, Font.NORMAL, PdfPrimaryColor);
        private static readonly Font PdfSmallMutedFont = new Font(Font.FontFamily.HELVETICA, 7.5f, Font.NORMAL, PdfMutedColor);

        #endregion

        #region Export PDF : Attestation Individuelle de Formation (iTextSharp A4)

        /// <summary>
        /// Génère une attestation individuelle officielle de formation professionnelle au format A4.
        /// Document tripartite officiel certifiant la participation, les acquis et le volume horaire.
        /// </summary>
        public bool GenererAttestationFormationPdf(SessionFormation session, SessionInscription participant, Dossier dossier, string cheminPdf)
        {
            if (session == null) throw new ArgumentNullException(nameof(session));
            if (participant == null) throw new ArgumentNullException(nameof(participant));
            if (string.IsNullOrWhiteSpace(cheminPdf)) throw new ArgumentNullException(nameof(cheminPdf));

            string dossierParent = Path.GetDirectoryName(cheminPdf);
            if (!string.IsNullOrEmpty(dossierParent) && !Directory.Exists(dossierParent))
            {
                Directory.CreateDirectory(dossierParent);
            }

            using (var fs = new FileStream(cheminPdf, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                var doc = new Document(PageSize.A4, 30f, 30f, 30f, 30f);
                var writer = PdfWriter.GetInstance(doc, fs);

                doc.Open();

                // 1. Cartouche Entreprise
                doc.Add(CreerCartoucheEmployeur(dossier));
                doc.Add(new Paragraph(" ") { SpacingAfter = 4f });

                // 2. Titre Officiel
                var tableTitre = new PdfPTable(1) { WidthPercentage = 100f };
                var cellTitre = new PdfPCell
                {
                    BackgroundColor = PdfLightBg,
                    BorderColor = PdfAccentColor,
                    BorderWidth = 1.5f,
                    Padding = 10f,
                    HorizontalAlignment = Element.ALIGN_CENTER
                };
                var pTitre = new Paragraph("ATTESTATION DE FORMATION PROFESSIONNELLE", PdfTitleFont)
                {
                    Alignment = Element.ALIGN_CENTER
                };
                var pSousTitre = new Paragraph("Justificatif officiel de développement des compétences & contrepartie TPA (Burkina Faso)", PdfSubTitleFont)
                {
                    Alignment = Element.ALIGN_CENTER,
                    SpacingBefore = 3f
                };
                cellTitre.AddElement(pTitre);
                cellTitre.AddElement(pSousTitre);
                tableTitre.AddCell(cellTitre);
                doc.Add(tableTitre);

                doc.Add(new Paragraph(" ") { SpacingAfter = 6f });

                // 3. Section Bénéficiaire
                doc.Add(CreerTitreSection("1. IDENTIFICATION DU BÉNÉFICIAIRE"));
                var tableBenef = new PdfPTable(4) { WidthPercentage = 100f };
                tableBenef.SetWidths(new float[] { 22f, 28f, 22f, 28f });

                AjouterCelluleChamp(tableBenef, "Salarié bénéficiaire :", participant.EmployeeNomComplet, 2);
                AjouterCelluleChamp(tableBenef, "N° Matricule :", participant.EmployeeMatricule, 1);
                AjouterCelluleChamp(tableBenef, "Direction / Service :", participant.Department ?? "Non assigné", 1);
                AjouterCelluleChamp(tableBenef, "Poste / Fonction :", participant.PosteTitre ?? "Non assigné", 2);

                doc.Add(tableBenef);
                doc.Add(new Paragraph(" ") { SpacingAfter = 6f });

                // 4. Section Action de Formation
                doc.Add(CreerTitreSection("2. CARACTÉRISTIQUES DE L'ACTION DE FORMATION"));
                var tableAction = new PdfPTable(4) { WidthPercentage = 100f };
                tableAction.SetWidths(new float[] { 22f, 28f, 22f, 28f });

                AjouterCelluleChamp(tableAction, "Intitulé du stage :", session.IntituleSession ?? session.FormationIntitule, 3);
                AjouterCelluleChamp(tableAction, "Code session :", session.CodeSession, 1);
                AjouterCelluleChamp(tableAction, "Formation catalogue :", $"[{session.FormationCode}] {session.FormationIntitule}", 3);
                AjouterCelluleChamp(tableAction, "Volume horaire :", $"{session.DureeHeures} heures", 1);
                AjouterCelluleChamp(tableAction, "Modalité :", "Présentiel / Pratique professionnelle", 1);
                AjouterCelluleChamp(tableAction, "Période réalisée :", session.PeriodeLibelle, 2);
                AjouterCelluleChamp(tableAction, "Lieu de formation :", session.Lieu ?? "Locaux de l'entreprise", 2);

                doc.Add(tableAction);
                doc.Add(new Paragraph(" ") { SpacingAfter = 6f });

                // 5. Section Organisme & Encadrement
                doc.Add(CreerTitreSection("3. ORGANISME FORMATEUR & ENCADREMENT"));
                var tableOrg = new PdfPTable(4) { WidthPercentage = 100f };
                tableOrg.SetWidths(new float[] { 22f, 28f, 22f, 28f });

                AjouterCelluleChamp(tableOrg, "Organisme formateur :", session.OrganismeNom ?? "Direction des Ressources Humaines (Interne)", 2);
                AjouterCelluleChamp(tableOrg, "N° Agrément FAFPA :", session.OrganismeNumeroAgrement ?? "Formation interne", 2);
                AjouterCelluleChamp(tableOrg, "Formateur / Animateur :", session.FormateurNom ?? "Intervenant qualifié", 2);
                AjouterCelluleChamp(tableOrg, "Rattachement Plan :", $"[{session.PlanCode}] {session.PlanTitre}", 2);

                doc.Add(tableOrg);
                doc.Add(new Paragraph(" ") { SpacingAfter = 6f });

                // 6. Section Validation & Résultats
                doc.Add(CreerTitreSection("4. ASSIDUITÉ ET ÉVALUATION DES ACQUIS"));
                var tableEval = new PdfPTable(4) { WidthPercentage = 100f };
                tableEval.SetWidths(new float[] { 22f, 28f, 22f, 28f });

                AjouterCelluleChamp(tableEval, "Statut de présence :", participant.StatutPresenceLibelle, 1);
                AjouterCelluleChamp(tableEval, "Évaluation / Note :", participant.NoteEvaluation ?? "Assiduité complète", 1);
                AjouterCelluleChamp(tableEval, "Attestation obtenue :", participant.AttestationObtenue ? "OUI — Délivrée" : "NON", 1);
                AjouterCelluleChamp(tableEval, "Date de délivrance :", DateTime.Today.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture), 1);

                doc.Add(tableEval);
                doc.Add(new Paragraph(" ") { SpacingAfter = 14f });

                // 7. Bloc officiel de signatures tripartites
                doc.Add(CreerBlocSignaturesTripartite());

                // 8. Bas de page officiel
                doc.Add(new Paragraph(" ") { SpacingAfter = 10f });
                var pFooter = new Paragraph(
                    $"Document officiel édité par Sigma Paie & RH le {DateTime.Now:dd/MM/yyyy à HH:mm}. " +
                    "Cette attestation est délivrée pour valoir et servir ce que de droit auprès de la Direction Générale des Impôts (DGI) et du FAFPA.",
                    PdfSmallMutedFont)
                {
                    Alignment = Element.ALIGN_CENTER
                };
                doc.Add(pFooter);

                doc.Close();
                return true;
            }
        }

        #endregion

        #region Export PDF : Bilan Annuel du Plan de Formation (iTextSharp A4)

        /// <summary>
        /// Génère le bilan annuel officiel du plan de formation et du suivi de la TPA.
        /// </summary>
        public bool GenererBilanAnnuelPdf(PlanFormation plan, TableauBordTpaFormation tpa, List<SessionFormation> sessions, Dossier dossier, string cheminPdf)
        {
            if (plan == null) throw new ArgumentNullException(nameof(plan));
            if (tpa == null) throw new ArgumentNullException(nameof(tpa));
            if (string.IsNullOrWhiteSpace(cheminPdf)) throw new ArgumentNullException(nameof(cheminPdf));

            string dossierParent = Path.GetDirectoryName(cheminPdf);
            if (!string.IsNullOrEmpty(dossierParent) && !Directory.Exists(dossierParent))
            {
                Directory.CreateDirectory(dossierParent);
            }

            using (var fs = new FileStream(cheminPdf, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                var doc = new Document(PageSize.A4, 28f, 28f, 28f, 28f);
                var writer = PdfWriter.GetInstance(doc, fs);

                doc.Open();

                // En-tête
                doc.Add(CreerCartoucheEmployeur(dossier));
                doc.Add(new Paragraph(" ") { SpacingAfter = 4f });

                // Titre
                var tableTitre = new PdfPTable(1) { WidthPercentage = 100f };
                var cellTitre = new PdfPCell
                {
                    BackgroundColor = PdfLightBg,
                    BorderColor = PdfAccentColor,
                    BorderWidth = 1.2f,
                    Padding = 8f,
                    HorizontalAlignment = Element.ALIGN_CENTER
                };
                cellTitre.AddElement(new Paragraph($"BILAN DU PLAN DE FORMATION & JUSTIFICATIF TPA — EXERCICE {plan.AnneeExercice}", PdfTitleFont) { Alignment = Element.ALIGN_CENTER });
                cellTitre.AddElement(new Paragraph($"Plan : [{plan.Code}] {plan.Titre} | Statut : {plan.StatutLibelle}", PdfSubTitleFont) { Alignment = Element.ALIGN_CENTER, SpacingBefore = 2f });
                tableTitre.AddCell(cellTitre);
                doc.Add(tableTitre);

                doc.Add(new Paragraph(" ") { SpacingAfter = 6f });

                // Tableau TPA & Finances
                doc.Add(CreerTitreSection("1. INDICATEURS FINANCIERS & CONTREPARTIE TPA"));
                var tableTpa = new PdfPTable(4) { WidthPercentage = 100f };
                tableTpa.SetWidths(new float[] { 28f, 22f, 28f, 22f });

                AjouterCelluleChamp(tableTpa, "Masse salariale TPA :", tpa.MasseSalarialeTexte, 1);
                AjouterCelluleChamp(tableTpa, "TPA due théorique :", tpa.TpaDueTexte, 1);
                AjouterCelluleChamp(tableTpa, "Budget formation voté :", plan.BudgetPrevisionnelTexte, 1);
                AjouterCelluleChamp(tableTpa, "Dépenses réelles engagées :", tpa.TotalDepensesTexte, 1);
                AjouterCelluleChamp(tableTpa, "Ratio d'effort formation :", tpa.RatioDepensesMasseTexte, 1);
                AjouterCelluleChamp(tableTpa, "Taux couverture TPA :", tpa.TauxCouvertureTpaTexte, 1);
                AjouterCelluleChamp(tableTpa, "Salariés formés :", $"{tpa.NombreSalariesFormes} agent(s)", 1);
                AjouterCelluleChamp(tableTpa, "Sessions réalisées :", $"{tpa.NombreSessionsRealisees} session(s)", 1);

                doc.Add(tableTpa);
                doc.Add(new Paragraph(" ") { SpacingAfter = 8f });

                // Liste des sessions
                doc.Add(CreerTitreSection("2. DÉTAIL DES SESSIONS DE FORMATION DE L'EXERCICE"));
                var tableSess = new PdfPTable(6) { WidthPercentage = 100f };
                tableSess.SetWidths(new float[] { 14f, 28f, 18f, 12f, 14f, 14f });

                AjouterCelluleEntete(tableSess, "Code");
                AjouterCelluleEntete(tableSess, "Intitulé Session");
                AjouterCelluleEntete(tableSess, "Période");
                AjouterCelluleEntete(tableSess, "Statut");
                AjouterCelluleEntete(tableSess, "Inscrits");
                AjouterCelluleEntete(tableSess, "Coût Réel");

                if (sessions != null && sessions.Count > 0)
                {
                    foreach (var s in sessions)
                    {
                        AjouterCelluleDonnee(tableSess, s.CodeSession, false);
                        AjouterCelluleDonnee(tableSess, s.IntituleSession ?? s.FormationIntitule, false);
                        AjouterCelluleDonnee(tableSess, s.PeriodeLibelle, false);
                        AjouterCelluleDonnee(tableSess, s.StatutLibelle, false);
                        AjouterCelluleDonnee(tableSess, $"{s.NombrePresents}/{s.NombreInscrits}", true);
                        AjouterCelluleDonnee(tableSess, s.CoutTotalTexte, true);
                    }
                }
                else
                {
                    var cellVide = new PdfPCell(new Phrase("Aucune session enregistrée pour ce plan.", PdfCellNormalFont))
                    {
                        Colspan = 6,
                        HorizontalAlignment = Element.ALIGN_CENTER,
                        Padding = 8f
                    };
                    tableSess.AddCell(cellVide);
                }

                doc.Add(tableSess);
                doc.Add(new Paragraph(" ") { SpacingAfter = 14f });

                // Signatures Direction
                var tableSign = new PdfPTable(2) { WidthPercentage = 100f };
                tableSign.SetWidths(new float[] { 50f, 50f });

                var cellS1 = new PdfPCell { Border = PdfPCell.NO_BORDER, Padding = 6f };
                cellS1.AddElement(new Paragraph("Le Responsable Formation & Développement RH", PdfCellBoldFont));
                cellS1.AddElement(new Paragraph("Date : __________________________\n\n\nSignature :", PdfCellNormalFont));

                var cellS2 = new PdfPCell { Border = PdfPCell.NO_BORDER, Padding = 6f };
                cellS2.AddElement(new Paragraph("La Direction Générale", PdfCellBoldFont));
                cellS2.AddElement(new Paragraph("Date : __________________________\n\n\nSignature & Cachet :", PdfCellNormalFont));

                tableSign.AddCell(cellS1);
                tableSign.AddCell(cellS2);
                doc.Add(tableSign);

                doc.Close();
                return true;
            }
        }

        #endregion

        #region Export Excel ClosedXML : Synthèse Complète 4 Feuilles

        /// <summary>
        /// Exporte la synthèse complète de la formation professionnelle en classeur Excel à 4 feuilles.
        /// </summary>
        public bool GenererSyntheseFormationExcel(
            PlanFormation plan,
            TableauBordTpaFormation tpa,
            List<SessionFormation> sessions,
            List<SessionInscription> participants,
            List<EntretienBesoinFormation> besoinsRh03,
            Dossier dossier,
            string cheminExcel)
        {
            if (plan == null) throw new ArgumentNullException(nameof(plan));
            if (tpa == null) throw new ArgumentNullException(nameof(tpa));
            if (string.IsNullOrWhiteSpace(cheminExcel)) throw new ArgumentNullException(nameof(cheminExcel));

            string dossierParent = Path.GetDirectoryName(cheminExcel);
            if (!string.IsNullOrEmpty(dossierParent) && !Directory.Exists(dossierParent))
            {
                Directory.CreateDirectory(dossierParent);
            }

            using (var workbook = new XLWorkbook())
            {
                // ───── FEUILLE 1 : SYNTHÈSE & TPA ─────
                var ws1 = workbook.Worksheets.Add("Synthèse & TPA");
                ConstruireFeuilleSyntheseTpa(ws1, plan, tpa, dossier);

                // ───── FEUILLE 2 : SESSIONS ─────
                var ws2 = workbook.Worksheets.Add("Sessions de formation");
                ConstruireFeuilleSessions(ws2, sessions, plan);

                // ───── FEUILLE 3 : PARTICIPANTS ─────
                var ws3 = workbook.Worksheets.Add("Participants & Présence");
                ConstruireFeuilleParticipants(ws3, participants);

                // ───── FEUILLE 4 : BESOINS RH-03 ─────
                var ws4 = workbook.Worksheets.Add("Recueil Besoins RH-03");
                ConstruireFeuilleBesoinsRh03(ws4, besoinsRh03);

                workbook.SaveAs(cheminExcel);
                return true;
            }
        }

        private void ConstruireFeuilleSyntheseTpa(IXLWorksheet ws, PlanFormation plan, TableauBordTpaFormation tpa, Dossier dossier)
        {
            ws.ShowGridLines = true;

            // Titre
            ws.Cell("B2").Value = $"PLAN DE FORMATION PROFESSIONNELLE & JUSTIFICATIF TPA — {plan.AnneeExercice}";
            ws.Cell("B2").Style.Font.Bold = true;
            ws.Cell("B2").Style.Font.FontSize = 14;
            ws.Cell("B2").Style.Font.FontColor = CouleurAccent;

            ws.Cell("B3").Value = $"Entreprise : {dossier?.RaisonSociale ?? "Sigma Entreprise"} | Code Plan : {plan.Code} | Statut : {plan.StatutLibelle}";
            ws.Cell("B3").Style.Font.Italic = true;
            ws.Cell("B3").Style.Font.FontColor = CouleurDiscret;

            // Section 1 : Caractéristiques du Plan
            int row = 5;
            ws.Cell(row, 2).Value = "1. INFORMATIONS GÉNÉRALES DU PLAN";
            ws.Cell(row, 2).Style.Font.Bold = true;
            ws.Cell(row, 2).Style.Font.FontColor = CouleurAccent;
            row++;

            AjouterLigneCleValeurExcel(ws, ref row, "Code du plan", plan.Code);
            AjouterLigneCleValeurExcel(ws, ref row, "Intitulé du plan", plan.Titre);
            AjouterLigneCleValeurExcel(ws, ref row, "Année d'exercice", plan.AnneeExercice.ToString());
            AjouterLigneCleValeurExcel(ws, ref row, "Statut", plan.StatutLibelle);
            AjouterLigneCleValeurExcel(ws, ref row, "Objectifs stratégiques", plan.ObjectifsStrategiques ?? "Développement des compétences clés");
            AjouterLigneCleValeurExcel(ws, ref row, "Budget prévisionnel voté", plan.BudgetPrevisionnelTexte);
            AjouterLigneCleValeurExcel(ws, ref row, "Dépenses réelles engagées", plan.DepensesReellesTexte);
            AjouterLigneCleValeurExcel(ws, ref row, "Écart budgétaire", plan.EcartBudgetTexte);
            AjouterLigneCleValeurExcel(ws, ref row, "Taux de consommation budgétaire", plan.TauxConsommationTexte);

            // Section 2 : Volet Fiscal & TPA
            row += 2;
            ws.Cell(row, 2).Value = "2. CONTREPARTIE FISCALE TPA & EFFORT DE FORMATION (BURKINA FASO)";
            ws.Cell(row, 2).Style.Font.Bold = true;
            ws.Cell(row, 2).Style.Font.FontColor = CouleurAccent;
            row++;

            AjouterLigneCleValeurExcel(ws, ref row, "Masse salariale soumise TPA (Assiette nette)", tpa.MasseSalarialeTexte);
            AjouterLigneCleValeurExcel(ws, ref row, "Taux légal TPA de référence", tpa.TauxTpaTexte);
            AjouterLigneCleValeurExcel(ws, ref row, "TPA théorique due / versée", tpa.TpaDueTexte);
            AjouterLigneCleValeurExcel(ws, ref row, "Total des dépenses de formation engagées", tpa.TotalDepensesTexte);
            AjouterLigneCleValeurExcel(ws, ref row, "Ratio d'effort de formation (Dépenses / Masse TPA)", tpa.RatioDepensesMasseTexte);
            AjouterLigneCleValeurExcel(ws, ref row, "Taux de couverture TPA (Dépenses / TPA)", tpa.TauxCouvertureTpaTexte);
            AjouterLigneCleValeurExcel(ws, ref row, "Nombre de salariés formés", tpa.NombreSalariesFormes.ToString());
            AjouterLigneCleValeurExcel(ws, ref row, "Volume total d'heures de formation", $"{tpa.NombreHeuresTotal} h");
            AjouterLigneCleValeurExcel(ws, ref row, "Sessions réalisées", tpa.NombreSessionsRealisees.ToString());

            ws.Columns(2, 4).AdjustToContents();
        }

        private void ConstruireFeuilleSessions(IXLWorksheet ws, List<SessionFormation> sessions, PlanFormation plan)
        {
            ws.ShowGridLines = true;

            ws.Cell("B2").Value = $"SESSIONS DE FORMATION — PLAN {plan.Code} ({plan.AnneeExercice})";
            ws.Cell("B2").Style.Font.Bold = true;
            ws.Cell("B2").Style.Font.FontSize = 13;
            ws.Cell("B2").Style.Font.FontColor = CouleurAccent;

            string[] entetes = new[]
            {
                "Code Session", "Intitulé de la Session", "Formation Catalogue", "Durée (h)",
                "Date Début", "Date Fin", "Lieu", "Formateur", "Organisme", "N° Agrément",
                "Statut", "Coût Pédagogique", "Coût Logistique", "Coût Autre", "Coût Total Réel",
                "Inscrits", "Présents", "Attestations"
            };

            int startRow = 4;
            for (int i = 0; i < entetes.Length; i++)
            {
                var cell = ws.Cell(startRow, i + 2);
                cell.Value = entetes[i];
                cell.Style.Font.Bold = true;
                cell.Style.Fill.BackgroundColor = CouleurAccent;
                cell.Style.Font.FontColor = XLColor.White;
                cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            }

            int curRow = startRow + 1;
            if (sessions != null && sessions.Count > 0)
            {
                foreach (var s in sessions)
                {
                    ws.Cell(curRow, 2).Value = s.CodeSession;
                    ws.Cell(curRow, 3).Value = s.IntituleSession ?? s.FormationIntitule;
                    ws.Cell(curRow, 4).Value = s.FormationIntitule;
                    ws.Cell(curRow, 5).Value = s.DureeHeures;
                    ws.Cell(curRow, 6).Value = s.DateDebut.ToString("dd/MM/yyyy");
                    ws.Cell(curRow, 7).Value = s.DateFin.ToString("dd/MM/yyyy");
                    ws.Cell(curRow, 8).Value = s.Lieu ?? "Interne";
                    ws.Cell(curRow, 9).Value = s.FormateurNom ?? "";
                    ws.Cell(curRow, 10).Value = s.OrganismeNom ?? "Interne";
                    ws.Cell(curRow, 11).Value = s.OrganismeNumeroAgrement ?? "";
                    ws.Cell(curRow, 12).Value = s.StatutLibelle;
                    ws.Cell(curRow, 13).Value = s.CoutPedagogique;
                    ws.Cell(curRow, 13).Style.NumberFormat.Format = "#,##0";
                    ws.Cell(curRow, 14).Value = s.CoutLogistique;
                    ws.Cell(curRow, 14).Style.NumberFormat.Format = "#,##0";
                    ws.Cell(curRow, 15).Value = s.CoutAutre;
                    ws.Cell(curRow, 15).Style.NumberFormat.Format = "#,##0";
                    ws.Cell(curRow, 16).Value = s.CoutTotalReel;
                    ws.Cell(curRow, 16).Style.NumberFormat.Format = "#,##0";
                    ws.Cell(curRow, 16).Style.Font.Bold = true;
                    ws.Cell(curRow, 17).Value = s.NombreInscrits;
                    ws.Cell(curRow, 18).Value = s.NombrePresents;
                    ws.Cell(curRow, 19).Value = s.NombreAttestations;

                    curRow++;
                }
            }

            ws.Columns(2, entetes.Length + 1).AdjustToContents();
        }

        private void ConstruireFeuilleParticipants(IXLWorksheet ws, List<SessionInscription> participants)
        {
            ws.ShowGridLines = true;

            ws.Cell("B2").Value = "REGISTRE NOMINATIF DES PARTICIPANTS AUX FORMATIONS";
            ws.Cell("B2").Style.Font.Bold = true;
            ws.Cell("B2").Style.Font.FontSize = 13;
            ws.Cell("B2").Style.Font.FontColor = CouleurAccent;

            string[] entetes = new[]
            {
                "Matricule", "Nom & Prénom", "Direction / Service", "Poste Occupé",
                "Code Session", "Intitulé Session", "Période", "Volume (h)",
                "Statut Présence", "Note / Évaluation", "Attestation Délivrée", "Frais Imputés"
            };

            int startRow = 4;
            for (int i = 0; i < entetes.Length; i++)
            {
                var cell = ws.Cell(startRow, i + 2);
                cell.Value = entetes[i];
                cell.Style.Font.Bold = true;
                cell.Style.Fill.BackgroundColor = CouleurAccent;
                cell.Style.Font.FontColor = XLColor.White;
                cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            }

            int curRow = startRow + 1;
            if (participants != null && participants.Count > 0)
            {
                foreach (var p in participants)
                {
                    ws.Cell(curRow, 2).Value = p.EmployeeMatricule;
                    ws.Cell(curRow, 3).Value = p.EmployeeNomComplet;
                    ws.Cell(curRow, 4).Value = p.Department ?? "";
                    ws.Cell(curRow, 5).Value = p.PosteTitre ?? "";
                    ws.Cell(curRow, 6).Value = p.SessionCode;
                    ws.Cell(curRow, 7).Value = p.SessionIntitule;
                    ws.Cell(curRow, 8).Value = $"{p.DateDebut:dd/MM/yyyy} - {p.DateFin:dd/MM/yyyy}";
                    ws.Cell(curRow, 9).Value = p.DureeHeures;
                    ws.Cell(curRow, 10).Value = p.StatutPresenceLibelle;
                    ws.Cell(curRow, 11).Value = p.NoteEvaluation ?? "";
                    ws.Cell(curRow, 12).Value = p.AttestationObtenue ? "OUI" : "NON";
                    ws.Cell(curRow, 13).Value = p.FraisImputes;
                    ws.Cell(curRow, 13).Style.NumberFormat.Format = "#,##0";

                    curRow++;
                }
            }

            ws.Columns(2, entetes.Length + 1).AdjustToContents();
        }

        private void ConstruireFeuilleBesoinsRh03(IXLWorksheet ws, List<EntretienBesoinFormation> besoins)
        {
            ws.ShowGridLines = true;

            ws.Cell("B2").Value = "RECUEIL DES BESOINS DE FORMATION ISSUS DES ENTRETIENS ANNUELS (RH-03)";
            ws.Cell("B2").Style.Font.Bold = true;
            ws.Cell("B2").Style.Font.FontSize = 13;
            ws.Cell("B2").Style.Font.FontColor = CouleurAccent;

            string[] entetes = new[]
            {
                "N°", "Thème de Formation Demandé", "Compétence Visée", "Priorité",
                "Avis de l'Évaluateur", "Statut Prise en Charge"
            };

            int startRow = 4;
            for (int i = 0; i < entetes.Length; i++)
            {
                var cell = ws.Cell(startRow, i + 2);
                cell.Value = entetes[i];
                cell.Style.Font.Bold = true;
                cell.Style.Fill.BackgroundColor = CouleurAccent;
                cell.Style.Font.FontColor = XLColor.White;
                cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            }

            int curRow = startRow + 1;
            if (besoins != null && besoins.Count > 0)
            {
                foreach (var b in besoins)
                {
                    ws.Cell(curRow, 2).Value = b.Id;
                    ws.Cell(curRow, 3).Value = b.ThemeFormation;
                    ws.Cell(curRow, 4).Value = b.CompetenceVisee ?? "";
                    ws.Cell(curRow, 5).Value = b.PrioriteLibelle;
                    ws.Cell(curRow, 6).Value = b.AvisEvaluateur ?? "";
                    ws.Cell(curRow, 7).Value = b.Statut.ToString();

                    curRow++;
                }
            }

            ws.Columns(2, entetes.Length + 1).AdjustToContents();
        }

        private static void AjouterLigneCleValeurExcel(IXLWorksheet ws, ref int row, string cle, string valeur)
        {
            ws.Cell(row, 2).Value = cle;
            ws.Cell(row, 2).Style.Font.Bold = true;
            ws.Cell(row, 3).Value = valeur;
            row++;
        }

        #endregion

        #region Helpers PDF Communs

        private static PdfPTable CreerCartoucheEmployeur(Dossier dossier)
        {
            var table = new PdfPTable(2) { WidthPercentage = 100f };
            table.SetWidths(new float[] { 65f, 35f });

            var cellGauche = new PdfPCell { Border = PdfPCell.NO_BORDER, Padding = 2f };
            string nomDossier = !string.IsNullOrWhiteSpace(dossier?.RaisonSociale) ? dossier.RaisonSociale : "SIGMA RH & PAIE";
            cellGauche.AddElement(new Paragraph(nomDossier, PdfTitleFont));

            string adresse = !string.IsNullOrWhiteSpace(dossier?.Adresse) ? dossier.Adresse : "Ouagadougou, Burkina Faso";
            cellGauche.AddElement(new Paragraph(adresse, PdfSmallMutedFont));

            var cellDroite = new PdfPCell { Border = PdfPCell.NO_BORDER, Padding = 2f, HorizontalAlignment = Element.ALIGN_RIGHT };
            string ifu = !string.IsNullOrWhiteSpace(dossier?.IFU) ? $"IFU : {dossier.IFU}" : "IFU : Non renseigné";
            string rccm = !string.IsNullOrWhiteSpace(dossier?.RCCM) ? $"RCCM : {dossier.RCCM}" : "RCCM : Non renseigné";
            cellDroite.AddElement(new Paragraph(ifu, PdfSmallMutedFont) { Alignment = Element.ALIGN_RIGHT });
            cellDroite.AddElement(new Paragraph(rccm, PdfSmallMutedFont) { Alignment = Element.ALIGN_RIGHT });
            cellDroite.AddElement(new Paragraph("RÉPUBLIQUE DU BURKINA FASO", PdfCellBoldFont) { Alignment = Element.ALIGN_RIGHT });

            table.AddCell(cellGauche);
            table.AddCell(cellDroite);
            return table;
        }

        private static Paragraph CreerTitreSection(string titre)
        {
            var p = new Paragraph(titre, PdfSectionFont)
            {
                SpacingBefore = 4f,
                SpacingAfter = 3f
            };
            return p;
        }

        private static void AjouterCelluleChamp(PdfPTable table, string libelle, string valeur, int colSpanValeur = 1)
        {
            var cellLib = new PdfPCell(new Phrase(libelle, PdfCellBoldFont))
            {
                BackgroundColor = PdfLightBg,
                BorderColor = PdfBorderColor,
                Padding = 4f,
                VerticalAlignment = Element.ALIGN_MIDDLE
            };
            var cellVal = new PdfPCell(new Phrase(valeur ?? "—", PdfCellNormalFont))
            {
                Colspan = colSpanValeur,
                BorderColor = PdfBorderColor,
                Padding = 4f,
                VerticalAlignment = Element.ALIGN_MIDDLE
            };

            table.AddCell(cellLib);
            table.AddCell(cellVal);
        }

        private static void AjouterCelluleEntete(PdfPTable table, string texte)
        {
            var cell = new PdfPCell(new Phrase(texte, PdfHeaderCellFont))
            {
                BackgroundColor = PdfAccentColor,
                BorderColor = PdfBorderColor,
                Padding = 5f,
                HorizontalAlignment = Element.ALIGN_CENTER,
                VerticalAlignment = Element.ALIGN_MIDDLE
            };
            table.AddCell(cell);
        }

        private static void AjouterCelluleDonnee(PdfPTable table, string texte, bool alignRight = false)
        {
            var cell = new PdfPCell(new Phrase(texte ?? "", PdfCellNormalFont))
            {
                BorderColor = PdfBorderColor,
                Padding = 4f,
                HorizontalAlignment = alignRight ? Element.ALIGN_RIGHT : Element.ALIGN_LEFT,
                VerticalAlignment = Element.ALIGN_MIDDLE
            };
            table.AddCell(cell);
        }

        private static PdfPTable CreerBlocSignaturesTripartite()
        {
            var table = new PdfPTable(3) { WidthPercentage = 100f };
            table.SetWidths(new float[] { 33f, 34f, 33f });

            table.AddCell(CreerBoiteSignature("Le Titulaire / Bénéficiaire", "Mention : « Lu et approuvé »\n\n\n\nSignature :"));
            table.AddCell(CreerBoiteSignature("Le Formateur / Organisme Agréé", "Certifié exact et conforme\n\n\n\nSignature & Cachet :"));
            table.AddCell(CreerBoiteSignature("La Direction Générale / DRH", "Pour valoir ce que de droit\n\n\n\nSignature & Sceau :"));

            return table;
        }

        private static PdfPCell CreerBoiteSignature(string titre, string corps)
        {
            var cell = new PdfPCell
            {
                BackgroundColor = PdfLightBg,
                BorderColor = PdfBorderColor,
                BorderWidth = 1f,
                Padding = 6f
            };
            cell.AddElement(new Paragraph(titre, PdfCellBoldFont) { Alignment = Element.ALIGN_CENTER });
            cell.AddElement(new Paragraph(corps, PdfSmallMutedFont) { Alignment = Element.ALIGN_LEFT, SpacingBefore = 4f });
            return cell;
        }

        #endregion
    }
}
