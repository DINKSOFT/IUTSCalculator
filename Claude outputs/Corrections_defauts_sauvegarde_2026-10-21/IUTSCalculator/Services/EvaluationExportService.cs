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
    /// Service d'exportation pour le module RH-03 Entretiens & Évaluations.
    /// Produit la fiche d'évaluation individuelle officielle A4 tripartite en PDF (iTextSharp)
    /// et la synthèse de campagne multi-onglets en Excel (ClosedXML).
    /// </summary>
    public class EvaluationExportService
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
        private static readonly Font PdfSubTitleFont = new Font(Font.FontFamily.HELVETICA, 9.5f, Font.BOLD, PdfPrimaryColor);
        private static readonly Font PdfSectionFont = new Font(Font.FontFamily.HELVETICA, 10f, Font.BOLD, PdfAccentColor);
        private static readonly Font PdfHeaderCellFont = new Font(Font.FontFamily.HELVETICA, 8f, Font.BOLD, BaseColor.WHITE);
        private static readonly Font PdfCellBoldFont = new Font(Font.FontFamily.HELVETICA, 8f, Font.BOLD, PdfPrimaryColor);
        private static readonly Font PdfCellNormalFont = new Font(Font.FontFamily.HELVETICA, 8f, Font.NORMAL, PdfPrimaryColor);
        private static readonly Font PdfSmallMutedFont = new Font(Font.FontFamily.HELVETICA, 7.5f, Font.NORMAL, PdfMutedColor);

        #endregion

        #region Export PDF de la Fiche d'Évaluation Individuelle (iTextSharp A4)

        /// <summary>
        /// Génère la fiche officielle A4 d'entretien et d'évaluation individuelle en PDF.
        /// Document tripartite officiel avec cartouche employeur, 6 sections de bilan et pavé de 3 signatures.
        /// </summary>
        public bool GenererFicheEntretienPdf(EntretienEvaluation entretien, Dossier dossier, string cheminPdf)
        {
            if (entretien == null) throw new ArgumentNullException(nameof(entretien));
            if (string.IsNullOrWhiteSpace(cheminPdf)) throw new ArgumentNullException(nameof(cheminPdf));

            string dossierParent = Path.GetDirectoryName(cheminPdf);
            if (!string.IsNullOrEmpty(dossierParent) && !Directory.Exists(dossierParent))
            {
                Directory.CreateDirectory(dossierParent);
            }

            using (var fs = new FileStream(cheminPdf, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                // Format A4 vertical avec marges standard 28pt (~1cm)
                var doc = new Document(PageSize.A4, 28f, 28f, 28f, 28f);
                var writer = PdfWriter.GetInstance(doc, fs);

                doc.Open();

                // 1. Cartouche Entreprise & En-tête
                doc.Add(CreerCartoucheEmployeur(dossier));
                doc.Add(new Paragraph(" ") { SpacingAfter = 2f });

                // 2. Titre Officiel du Document
                var tableTitre = new PdfPTable(1) { WidthPercentage = 100f };
                var cellTitre = new PdfPCell
                {
                    BackgroundColor = PdfLightBg,
                    BorderColor = PdfAccentColor,
                    BorderWidth = 1.5f,
                    Padding = 8f,
                    HorizontalAlignment = Element.ALIGN_CENTER
                };
                var pTitre = new Paragraph("FICHE OFFICIELLE D'ÉVALUATION ET D'ENTRETIEN INDIVIDUEL", PdfTitleFont)
                {
                    Alignment = Element.ALIGN_CENTER
                };
                var pCampagne = new Paragraph(
                    $"Campagne : {entretien.CampagneTitre} — Exercice : {entretien.CampagneAnnee} | Statut : {entretien.StatutLibelle.ToUpperInvariant()}",
                    PdfSubTitleFont)
                {
                    Alignment = Element.ALIGN_CENTER
                };
                cellTitre.AddElement(pTitre);
                cellTitre.AddElement(pCampagne);
                tableTitre.AddCell(cellTitre);
                doc.Add(tableTitre);

                doc.Add(new Paragraph(" ") { SpacingAfter = 4f });

                // 3. Bloc 1 : Identification du Collaborateur & du Supérieur N+1
                doc.Add(CreerSectionTitre("1. IDENTIFICATION DU COLLABORATEUR & DE L'ÉVALUATEUR"));
                doc.Add(CreerBlocIdentification(entretien));

                doc.Add(new Paragraph(" ") { SpacingAfter = 4f });

                // 4. Bloc 2 : Bilan des Objectifs de l'Exercice Écoulé (N-1)
                doc.Add(CreerSectionTitre("2. BILAN DES OBJECTIFS DE L'EXERCICE ÉCOULÉ (N-1)"));
                doc.Add(CreerTableauObjectifsPasses(entretien));

                doc.Add(new Paragraph(" ") { SpacingAfter = 4f });

                // 5. Bloc 3 : Évaluation des Compétences & Qualités Professionnelles
                doc.Add(CreerSectionTitre("3. ÉVALUATION DES COMPÉTENCES & QUALITÉS PROFESSIONNELLES"));
                doc.Add(CreerTableauCriteres(entretien));

                doc.Add(new Paragraph(" ") { SpacingAfter = 4f });

                // 6. Bloc 4 : Fixation des Nouveaux Objectifs (N+1)
                doc.Add(CreerSectionTitre("4. FIXATION DES NOUVEAUX OBJECTIFS POUR L'EXERCICE SUIVANT (N+1)"));
                doc.Add(CreerTableauNouveauxObjectifs(entretien));

                doc.Add(new Paragraph(" ") { SpacingAfter = 4f });

                // 7. Bloc 5 : Besoins en Formation & Perspectives d'Évolution (Passerelle RH-04)
                doc.Add(CreerSectionTitre("5. PLAN DE DÉVELOPPEMENT & BESOINS DE FORMATION (N+1)"));
                doc.Add(CreerTableauFormations(entretien));

                doc.Add(new Paragraph(" ") { SpacingAfter = 4f });

                // 8. Bloc 6 : Synthèse & Appréciation Globale
                doc.Add(CreerSectionTitre("6. SYNTHÈSE & APPRÉCIATION COLLÉGIALE"));
                doc.Add(CreerBlocSynthese(entretien));

                doc.Add(new Paragraph(" ") { SpacingAfter = 6f });

                // 9. Bloc 7 : Pavé des 3 Signatures Officielles
                doc.Add(CreerBlocSignatures(entretien));

                doc.Close();
            }

            return true;
        }

        private static PdfPTable CreerCartoucheEmployeur(Dossier dossier)
        {
            var table = new PdfPTable(2) { WidthPercentage = 100f };
            table.SetWidths(new float[] { 65f, 35f });

            string raisonSociale = dossier != null ? dossier.RaisonSociale : "SIGMA PAIE & RH";
            string ifu = dossier != null && !string.IsNullOrWhiteSpace(dossier.IFU) ? dossier.IFU : "N/A";
            string rccm = dossier != null && !string.IsNullOrWhiteSpace(dossier.RCCM) ? dossier.RCCM : "N/A";
            string regime = dossier != null && !string.IsNullOrWhiteSpace(dossier.RegimeFiscal) ? dossier.RegimeFiscal : "Régime Normal";

            var cellGauche = new PdfPCell { Border = Rectangle.NO_BORDER, Padding = 2f };
            cellGauche.AddElement(new Paragraph(raisonSociale.ToUpperInvariant(), PdfTitleFont));
            cellGauche.AddElement(new Paragraph($"IFU : {ifu} | RCCM : {rccm} | {regime}", PdfSmallMutedFont));
            cellGauche.AddElement(new Paragraph("BURKINA FASO — DIRECTION DES RESSOURCES HUMAINES", PdfSmallMutedFont));

            var cellDroite = new PdfPCell { Border = Rectangle.NO_BORDER, HorizontalAlignment = Element.ALIGN_RIGHT, Padding = 2f };
            var pDate = new Paragraph($"Édité le : {DateTime.Now:dd/MM/yyyy HH:mm}", PdfSmallMutedFont) { Alignment = Element.ALIGN_RIGHT };
            var pRef = new Paragraph("Réf : RH-03 / GPEC & ÉVALUATION", PdfSmallMutedFont) { Alignment = Element.ALIGN_RIGHT };
            cellDroite.AddElement(pDate);
            cellDroite.AddElement(pRef);

            table.AddCell(cellGauche);
            table.AddCell(cellDroite);
            return table;
        }

        private static PdfPTable CreerSectionTitre(string titre)
        {
            var table = new PdfPTable(1) { WidthPercentage = 100f, SpacingBefore = 3f, SpacingAfter = 2f };
            var cell = new PdfPCell
            {
                BackgroundColor = PdfLightBg,
                BorderColor = PdfAccentColor,
                BorderWidth = 1f,
                Padding = 4f
            };
            cell.AddElement(new Paragraph(titre, PdfSectionFont));
            table.AddCell(cell);
            return table;
        }

        private static PdfPTable CreerBlocIdentification(EntretienEvaluation ent)
        {
            var table = new PdfPTable(4) { WidthPercentage = 100f };
            table.SetWidths(new float[] { 22f, 28f, 22f, 28f });

            AjouterLigneCleValeur(table, "Matricule Salarié :", ent.EmployeeId ?? "—", "Supérieur Hiérarchique N+1 :", ent.EvaluateurNomComplet ?? "—");
            AjouterLigneCleValeur(table, "Nom & Prénom(s) :", ent.EmployeeNomComplet ?? "—", "Poste de l'Évaluateur :", ent.EvaluateurPoste ?? "—");
            AjouterLigneCleValeur(table, "Poste / Fonction :", ent.EmployeePoste ?? "—", "Date de l'Entretien :", ent.DateEntretienTexte);
            AjouterLigneCleValeur(table, "Direction / Service :", ent.EmployeeService ?? "—", "Date d'embauche :", ent.EmployeeDateEmbauche.HasValue ? ent.EmployeeDateEmbauche.Value.ToString("dd/MM/yyyy") : "—");
            AjouterLigneCleValeur(table, "Ancienneté révolue :", ent.EmployeeAnciennete ?? "—", "Statut de l'évaluation :", ent.StatutLibelle);

            return table;
        }

        private static PdfPTable CreerTableauObjectifsPasses(EntretienEvaluation ent)
        {
            var table = new PdfPTable(5) { WidthPercentage = 100f };
            table.SetWidths(new float[] { 35f, 20f, 20f, 12f, 13f });

            AjouterCelluleEntete(table, "Intitulé de l'objectif");
            AjouterCelluleEntete(table, "Cible attendue");
            AjouterCelluleEntete(table, "Résultat obtenu");
            AjouterCelluleEntete(table, "Atteinte %");
            AjouterCelluleEntete(table, "Pondération");

            if (ent.ObjectifsPasses != null && ent.ObjectifsPasses.Count > 0)
            {
                foreach (var obj in ent.ObjectifsPasses)
                {
                    AjouterCelluleDonnee(table, obj.Intitule ?? "—", false);
                    AjouterCelluleDonnee(table, obj.CibleAttendue ?? "—", false);
                    AjouterCelluleDonnee(table, obj.ResultatObtenu ?? "—", false);
                    AjouterCelluleDonnee(table, $"{obj.TauxAtteintePct} %", true, Element.ALIGN_CENTER);
                    AjouterCelluleDonnee(table, $"Poids {obj.Poids}", false, Element.ALIGN_CENTER);
                }

                // Ligne de synthèse
                var cellSynth = new PdfPCell(new Phrase("TAUX MOYEN D'ATTEINTE DES OBJECTIFS (N-1) :", PdfCellBoldFont))
                {
                    Colspan = 3,
                    BackgroundColor = PdfLightBg,
                    BorderColor = PdfBorderColor,
                    Padding = 4f,
                    HorizontalAlignment = Element.ALIGN_RIGHT
                };
                table.AddCell(cellSynth);

                var cellMoyenne = new PdfPCell(new Phrase(ent.TauxAtteinteMoyenTexte, PdfCellBoldFont))
                {
                    Colspan = 2,
                    BackgroundColor = PdfLightBg,
                    BorderColor = PdfBorderColor,
                    Padding = 4f,
                    HorizontalAlignment = Element.ALIGN_CENTER
                };
                table.AddCell(cellMoyenne);
            }
            else
            {
                var cellVide = new PdfPCell(new Phrase("Aucun objectif formalisé n'était enregistré pour cet exercice.", PdfSmallMutedFont))
                {
                    Colspan = 5,
                    Padding = 6f,
                    BorderColor = PdfBorderColor,
                    HorizontalAlignment = Element.ALIGN_CENTER
                };
                table.AddCell(cellVide);
            }

            return table;
        }

        private static PdfPTable CreerTableauCriteres(EntretienEvaluation ent)
        {
            var table = new PdfPTable(4) { WidthPercentage = 100f };
            table.SetWidths(new float[] { 25f, 35f, 15f, 25f });

            AjouterCelluleEntete(table, "Catégorie");
            AjouterCelluleEntete(table, "Compétence / Critère évalué");
            AjouterCelluleEntete(table, "Note attribuée");
            AjouterCelluleEntete(table, "Commentaires / Appréciation");

            if (ent.Criteres != null && ent.Criteres.Count > 0)
            {
                foreach (var c in ent.Criteres)
                {
                    AjouterCelluleDonnee(table, c.Categorie ?? "Générale", false);
                    AjouterCelluleDonnee(table, c.Libelle ?? "—", true);
                    AjouterCelluleDonnee(table, $"{c.Note}/5  {c.NoteEtoiles}", true, Element.ALIGN_CENTER);
                    AjouterCelluleDonnee(table, c.Commentaire ?? "—", false);
                }

                // Ligne de total / moyenne
                var cellSynth = new PdfPCell(new Phrase("NOTE GLOBALE MOYENNE DES CRITÈRES (SUR 5) :", PdfCellBoldFont))
                {
                    Colspan = 2,
                    BackgroundColor = PdfLightBg,
                    BorderColor = PdfBorderColor,
                    Padding = 5f,
                    HorizontalAlignment = Element.ALIGN_RIGHT
                };
                table.AddCell(cellSynth);

                var cellNote = new PdfPCell(new Phrase($"{ent.NoteGlobaleFormatee} — {ent.MentionGlobale}", PdfCellBoldFont))
                {
                    Colspan = 2,
                    BackgroundColor = PdfLightBg,
                    BorderColor = PdfBorderColor,
                    Padding = 5f,
                    HorizontalAlignment = Element.ALIGN_CENTER
                };
                table.AddCell(cellNote);
            }
            else
            {
                var cellVide = new PdfPCell(new Phrase("Aucun critère n'a été saisi pour cet entretien.", PdfSmallMutedFont))
                {
                    Colspan = 4,
                    Padding = 6f,
                    BorderColor = PdfBorderColor,
                    HorizontalAlignment = Element.ALIGN_CENTER
                };
                table.AddCell(cellVide);
            }

            return table;
        }

        private static PdfPTable CreerTableauNouveauxObjectifs(EntretienEvaluation ent)
        {
            var table = new PdfPTable(5) { WidthPercentage = 100f };
            table.SetWidths(new float[] { 35f, 22f, 20f, 13f, 10f });

            AjouterCelluleEntete(table, "Nouvel Objectif Stratégique / Opérationnel");
            AjouterCelluleEntete(table, "Indicateur de mesure");
            AjouterCelluleEntete(table, "Cible / Résultat attendu");
            AjouterCelluleEntete(table, "Échéance");
            AjouterCelluleEntete(table, "Priorité");

            if (ent.NouveauxObjectifs != null && ent.NouveauxObjectifs.Count > 0)
            {
                foreach (var obj in ent.NouveauxObjectifs)
                {
                    AjouterCelluleDonnee(table, obj.Intitule ?? "—", true);
                    AjouterCelluleDonnee(table, obj.IndicateurMesure ?? "—", false);
                    AjouterCelluleDonnee(table, obj.CibleAttendue ?? "—", false);
                    AjouterCelluleDonnee(table, obj.Echeance ?? "Annuelle", false, Element.ALIGN_CENTER);
                    AjouterCelluleDonnee(table, $"P{obj.Poids}", false, Element.ALIGN_CENTER);
                }
            }
            else
            {
                var cellVide = new PdfPCell(new Phrase("Aucun nouvel objectif consigné.", PdfSmallMutedFont))
                {
                    Colspan = 5,
                    Padding = 6f,
                    BorderColor = PdfBorderColor,
                    HorizontalAlignment = Element.ALIGN_CENTER
                };
                table.AddCell(cellVide);
            }

            return table;
        }

        private static PdfPTable CreerTableauFormations(EntretienEvaluation ent)
        {
            var table = new PdfPTable(4) { WidthPercentage = 100f };
            table.SetWidths(new float[] { 35f, 25f, 15f, 25f });

            AjouterCelluleEntete(table, "Thème de formation souhaité");
            AjouterCelluleEntete(table, "Compétence visée");
            AjouterCelluleEntete(table, "Priorité");
            AjouterCelluleEntete(table, "Avis de l'évaluateur N+1");

            if (ent.BesoinsFormation != null && ent.BesoinsFormation.Count > 0)
            {
                foreach (var f in ent.BesoinsFormation)
                {
                    AjouterCelluleDonnee(table, f.ThemeFormation ?? "—", true);
                    AjouterCelluleDonnee(table, f.CompetenceVisee ?? "—", false);
                    AjouterCelluleDonnee(table, f.PrioriteLibelle, false, Element.ALIGN_CENTER);
                    AjouterCelluleDonnee(table, f.AvisEvaluateur ?? "Favorable", false);
                }
            }
            else
            {
                var cellVide = new PdfPCell(new Phrase("Aucun besoin particulier de formation identifié à cette date.", PdfSmallMutedFont))
                {
                    Colspan = 4,
                    Padding = 6f,
                    BorderColor = PdfBorderColor,
                    HorizontalAlignment = Element.ALIGN_CENTER
                };
                table.AddCell(cellVide);
            }

            return table;
        }

        private static PdfPTable CreerBlocSynthese(EntretienEvaluation ent)
        {
            var table = new PdfPTable(2) { WidthPercentage = 100f };
            table.SetWidths(new float[] { 50f, 50f });

            // Note & Mention
            var cellNote = new PdfPCell
            {
                Colspan = 2,
                BackgroundColor = PdfLightBg,
                BorderColor = PdfBorderColor,
                Padding = 5f
            };
            cellNote.AddElement(new Paragraph($"NOTE D'ÉVALUATION GLOBALE : {ent.NoteGlobaleFormatee} — APPRÉCIATION : {ent.MentionGlobale.ToUpperInvariant()}", PdfTitleFont));
            table.AddCell(cellNote);

            // Points forts & Axes de progrès
            AjouterCadreTexte(table, "Points Forts & Réussites Notables :", ent.PointsForts);
            AjouterCadreTexte(table, "Axes de Progrès & Points d'Amélioration :", ent.AxesProgres);

            // Commentaires du Salarié & Avis N+1
            AjouterCadreTexte(table, "Commentaires & Remarques du Collaborateur :", ent.CommentairesSalarie);
            AjouterCadreTexte(table, "Appréciation Générale du Supérieur Hiérarchique N+1 :", ent.CommentairesEvaluateur);

            // Avis DRH / Décisions
            var cellRH = new PdfPCell
            {
                Colspan = 2,
                BorderColor = PdfBorderColor,
                Padding = 4f
            };
            cellRH.AddElement(new Paragraph("Avis & Décisions de la Direction des Ressources Humaines :", PdfCellBoldFont));
            cellRH.AddElement(new Paragraph(!string.IsNullOrWhiteSpace(ent.AvisDirectionRH) ? ent.AvisDirectionRH : "Conforme aux orientations RH et de politique salariale.", PdfCellNormalFont));
            table.AddCell(cellRH);

            return table;
        }

        private static PdfPTable CreerBlocSignatures(EntretienEvaluation ent)
        {
            var table = new PdfPTable(3) { WidthPercentage = 100f };
            table.SetWidths(new float[] { 33f, 34f, 33f });

            table.AddCell(CreerCelluleSignature("Le Collaborateur Évalué", "« Lu et pris connaissance »", ent.EmployeeNomComplet));
            table.AddCell(CreerCelluleSignature("Le Supérieur Hiérarchique N+1", "« Avis et signature de l'évaluateur »", ent.EvaluateurNomComplet));
            table.AddCell(CreerCelluleSignature("La Direction des RH / DG", "« Visa officiel de validation »", "Direction Générale / DRH"));

            return table;
        }

        private static PdfPCell CreerCelluleSignature(string titre, string sousTitre, string signataire)
        {
            var cell = new PdfPCell
            {
                BorderColor = PdfBorderColor,
                BorderWidth = 1f,
                Padding = 6f,
                MinimumHeight = 65f
            };
            cell.AddElement(new Paragraph(titre, PdfCellBoldFont));
            cell.AddElement(new Paragraph(sousTitre, PdfSmallMutedFont));
            cell.AddElement(new Paragraph(" ") { SpacingAfter = 18f });
            cell.AddElement(new Paragraph($"Signataire : {signataire}", PdfSmallMutedFont));
            return cell;
        }

        private static void AjouterLigneCleValeur(PdfPTable table, string cle1, string val1, string cle2, string val2)
        {
            var cellCle1 = new PdfPCell(new Phrase(cle1, PdfCellBoldFont)) { Border = Rectangle.NO_BORDER, Padding = 2f, BackgroundColor = PdfLightBg };
            var cellVal1 = new PdfPCell(new Phrase(val1, PdfCellNormalFont)) { Border = Rectangle.NO_BORDER, Padding = 2f };
            var cellCle2 = new PdfPCell(new Phrase(cle2, PdfCellBoldFont)) { Border = Rectangle.NO_BORDER, Padding = 2f, BackgroundColor = PdfLightBg };
            var cellVal2 = new PdfPCell(new Phrase(val2, PdfCellNormalFont)) { Border = Rectangle.NO_BORDER, Padding = 2f };

            table.AddCell(cellCle1);
            table.AddCell(cellVal1);
            table.AddCell(cellCle2);
            table.AddCell(cellVal2);
        }

        private static void AjouterCadreTexte(PdfPTable table, string titre, string contenu)
        {
            var cell = new PdfPCell { BorderColor = PdfBorderColor, Padding = 4f };
            cell.AddElement(new Paragraph(titre, PdfCellBoldFont));
            cell.AddElement(new Paragraph(!string.IsNullOrWhiteSpace(contenu) ? contenu : "—", PdfCellNormalFont));
            table.AddCell(cell);
        }

        private static void AjouterCelluleEntete(PdfPTable table, string texte)
        {
            var cell = new PdfPCell(new Phrase(texte, PdfHeaderCellFont))
            {
                BackgroundColor = PdfAccentColor,
                BorderColor = PdfBorderColor,
                Padding = 4f,
                HorizontalAlignment = Element.ALIGN_CENTER
            };
            table.AddCell(cell);
        }

        private static void AjouterCelluleDonnee(PdfPTable table, string texte, bool gras, int alignement = Element.ALIGN_LEFT)
        {
            var cell = new PdfPCell(new Phrase(texte, gras ? PdfCellBoldFont : PdfCellNormalFont))
            {
                BorderColor = PdfBorderColor,
                Padding = 3.5f,
                HorizontalAlignment = alignement
            };
            table.AddCell(cell);
        }

        #endregion

        #region Export Excel de la Campagne d'Évaluation (ClosedXML 4 feuilles)

        /// <summary>
        /// Génère le classeur Excel officiel de synthèse de la campagne d'évaluation.
        /// 4 feuilles de pilotage RH :
        ///   1. « Synthèse Campagne » (KPIs, baromètre, taux de complétion)
        ///   2. « Relevé des Évaluations » (Liste nominative complète et notes)
        ///   3. « Objectifs Fixés N+1 » (Portefeuille des objectifs consolidés)
        ///   4. « Besoins de Formation » (Recensement consolidé pour RH-04)
        /// </summary>
        public bool GenererSyntheseCampagneExcel(
            CampagneEvaluation campagne,
            List<EntretienEvaluation> entretiens,
            StatistiquesCampagne stats,
            Dossier dossier,
            string cheminExcel)
        {
            if (campagne == null) throw new ArgumentNullException(nameof(campagne));
            if (string.IsNullOrWhiteSpace(cheminExcel)) throw new ArgumentNullException(nameof(cheminExcel));

            entretiens = entretiens ?? new List<EntretienEvaluation>();
            stats = stats ?? new StatistiquesCampagne();

            string dossierParent = Path.GetDirectoryName(cheminExcel);
            if (!string.IsNullOrEmpty(dossierParent) && !Directory.Exists(dossierParent))
            {
                Directory.CreateDirectory(dossierParent);
            }

            using (var wb = new XLWorkbook())
            {
                // 1. Feuille 1 : Synthèse & Indicateurs de Campagne
                ConstruireFeuilleSynthese(wb, campagne, stats, dossier);

                // 2. Feuille 2 : Relevé Nominatif des Évaluations
                ConstruireFeuilleReleveNominatif(wb, entretiens);

                // 3. Feuille 3 : Objectifs N+1 Consolidés
                ConstruireFeuilleObjectifs(wb, entretiens);

                // 4. Feuille 4 : Besoins de Formation (Passerelle RH-04)
                ConstruireFeuilleFormations(wb, entretiens);

                wb.SaveAs(cheminExcel);
            }

            return true;
        }

        private static void ConstruireFeuilleSynthese(
            XLWorkbook wb,
            CampagneEvaluation camp,
            StatistiquesCampagne stats,
            Dossier dossier)
        {
            var ws = wb.Worksheets.Add("Synthèse Campagne");
            ws.ShowGridLines = true;

            // En-tête société
            string ste = dossier != null ? dossier.RaisonSociale : "SIGMA PAIE & RH";
            ws.Cell("A1").Value = ste.ToUpperInvariant();
            ws.Cell("A1").Style.Font.Bold = true;
            ws.Cell("A1").Style.Font.FontSize = 14;
            ws.Cell("A1").Style.Font.FontColor = CouleurAccent;

            ws.Cell("A2").Value = $"TABLEAU DE BORD DE CAMPAGNE D'ÉVALUATION — {camp.Titre.ToUpperInvariant()}";
            ws.Cell("A2").Style.Font.Bold = true;
            ws.Cell("A2").Style.Font.FontSize = 11;
            ws.Cell("A2").Style.Font.FontColor = CouleurEncre;

            ws.Cell("A3").Value = $"Code : {camp.Code} | Exercice : {camp.AnneeExercice} | Période : {camp.PeriodeLibelle} | Statut : {camp.StatutLibelle}";
            ws.Cell("A3").Style.Font.FontSize = 9.5;
            ws.Cell("A3").Style.Font.FontColor = CouleurDiscret;

            // Cartes KPIs
            int rowKpi = 5;
            CreerCarteKpiExcel(ws, rowKpi, 1, "SALARIÉS CIBLÉS", stats.TotalSalariesCibles.ToString(), CouleurAccent);
            CreerCarteKpiExcel(ws, rowKpi, 3, "PLANIFIÉS", stats.EntretiensPlanifies.ToString(), XLColor.FromArgb(2, 136, 209));
            CreerCarteKpiExcel(ws, rowKpi, 5, "RÉALISÉS", stats.EntretiensRealises.ToString(), CouleurAmbre);
            CreerCarteKpiExcel(ws, rowKpi, 7, "VALIDÉS & SIGNÉS", stats.EntretiensValides.ToString(), CouleurVert);
            CreerCarteKpiExcel(ws, rowKpi, 9, "TAUX D'AVANCEMENT", $"{stats.TauxAvancementPct:N1} %", CouleurAccent);
            CreerCarteKpiExcel(ws, rowKpi, 11, "NOTE MOYENNE", stats.NoteMoyenneGlobale > 0 ? $"{stats.NoteMoyenneGlobale:N2} / 5" : "—", CouleurAccent);

            // Tableau de distribution des notes
            int rowDist = 10;
            ws.Cell(rowDist, 1).Value = "RÉPARTITION DES MENTIONS D'ÉVALUATION";
            ws.Cell(rowDist, 1).Style.Font.Bold = true;
            ws.Cell(rowDist, 1).Style.Font.FontColor = CouleurAccent;

            rowDist++;
            ws.Cell(rowDist, 1).Value = "Mention d'évaluation";
            ws.Cell(rowDist, 2).Value = "Note correspondante";
            ws.Cell(rowDist, 3).Value = "Nombre de collaborateurs";
            ws.Cell(rowDist, 4).Value = "Proportion (%)";
            var rngH = ws.Range(rowDist, 1, rowDist, 4);
            rngH.Style.Fill.BackgroundColor = CouleurAccent;
            rngH.Style.Font.FontColor = XLColor.White;
            rngH.Style.Font.Bold = true;

            int totNotés = stats.CountInsuffisant + stats.CountADevelopper + stats.CountConforme + stats.CountDepasse + stats.CountRemarquable;

            var mentions = new[]
            {
                new { Lib = "Insuffisant", Plage = "< 2.0 / 5", Count = stats.CountInsuffisant },
                new { Lib = "À développer", Plage = "2.0 à 2.9 / 5", Count = stats.CountADevelopper },
                new { Lib = "Conforme aux attentes", Plage = "3.0 à 3.9 / 5", Count = stats.CountConforme },
                new { Lib = "Dépasse les attentes", Plage = "4.0 à 4.4 / 5", Count = stats.CountDepasse },
                new { Lib = "Remarquable / Exceptionnel", Plage = ">= 4.5 / 5", Count = stats.CountRemarquable }
            };

            foreach (var m in mentions)
            {
                rowDist++;
                ws.Cell(rowDist, 1).Value = m.Lib;
                ws.Cell(rowDist, 2).Value = m.Plage;
                ws.Cell(rowDist, 3).Value = m.Count;
                ws.Cell(rowDist, 4).Value = totNotés > 0 ? Math.Round((double)m.Count / totNotés * 100.0, 1) : 0.0;
                ws.Cell(rowDist, 4).Style.NumberFormat.Format = "0.0\" %\"";
            }

            ws.Columns(1, 12).AdjustToContents();
        }

        private static void CreerCarteKpiExcel(IXLWorksheet ws, int row, int col, string titre, string valeur, XLColor couleur)
        {
            var range = ws.Range(row, col, row + 2, col + 1);
            range.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
            range.Style.Border.OutsideBorderColor = CouleurBordure;
            range.Style.Fill.BackgroundColor = CouleurTrame;

            ws.Cell(row, col).Value = titre;
            ws.Cell(row, col).Style.Font.Bold = true;
            ws.Cell(row, col).Style.Font.FontSize = 8;
            ws.Cell(row, col).Style.Font.FontColor = CouleurDiscret;

            ws.Cell(row + 1, col).Value = valeur;
            ws.Cell(row + 1, col).Style.Font.Bold = true;
            ws.Cell(row + 1, col).Style.Font.FontSize = 14;
            ws.Cell(row + 1, col).Style.Font.FontColor = couleur;
        }

        private static void ConstruireFeuilleReleveNominatif(XLWorkbook wb, List<EntretienEvaluation> entretiens)
        {
            var ws = wb.Worksheets.Add("Relevé des Évaluations");
            ws.ShowGridLines = true;

            string[] headers = {
                "Matricule", "Nom & Prénom(s)", "Direction / Service", "Poste occupé",
                "Évaluateur N+1", "Date Entretien", "Statut", "Note Globale (/5)",
                "Mention", "Taux Obj N-1 (%)", "Points Forts", "Axes de Progrès"
            };

            for (int i = 0; i < headers.Length; i++)
            {
                ws.Cell(1, i + 1).Value = headers[i];
                ws.Cell(1, i + 1).Style.Fill.BackgroundColor = CouleurAccent;
                ws.Cell(1, i + 1).Style.Font.FontColor = XLColor.White;
                ws.Cell(1, i + 1).Style.Font.Bold = true;
            }

            int row = 2;
            foreach (var e in entretiens)
            {
                ws.Cell(row, 1).Value = e.EmployeeId;
                ws.Cell(row, 2).Value = e.EmployeeNomComplet;
                ws.Cell(row, 3).Value = e.EmployeeService ?? "—";
                ws.Cell(row, 4).Value = e.EmployeePoste ?? "—";
                ws.Cell(row, 5).Value = e.EvaluateurNomComplet ?? "—";
                ws.Cell(row, 6).Value = e.DateEntretienTexte;
                ws.Cell(row, 7).Value = e.StatutLibelle;
                if (e.NoteGlobale > 0)
                {
                    ws.Cell(row, 8).Value = e.NoteGlobale;
                    ws.Cell(row, 8).Style.NumberFormat.Format = "0.00";
                }
                else
                {
                    ws.Cell(row, 8).Value = "—";
                }

                ws.Cell(row, 9).Value = e.MentionGlobale;

                if (e.ObjectifsPasses.Count > 0)
                {
                    ws.Cell(row, 10).Value = e.TauxAtteinteMoyenObjectifsPasses;
                    ws.Cell(row, 10).Style.NumberFormat.Format = "0.0\" %\"";
                }
                else
                {
                    ws.Cell(row, 10).Value = "—";
                }

                ws.Cell(row, 11).Value = e.PointsForts ?? "—";
                ws.Cell(row, 12).Value = e.AxesProgres ?? "—";

                row++;
            }

            ws.Columns(1, headers.Length).AdjustToContents();
        }

        private static void ConstruireFeuilleObjectifs(XLWorkbook wb, List<EntretienEvaluation> entretiens)
        {
            var ws = wb.Worksheets.Add("Objectifs Fixés N+1");
            ws.ShowGridLines = true;

            string[] headers = {
                "Matricule", "Salarié", "Service", "Intitulé de l'Objectif (N+1)",
                "Indicateur de mesure", "Cible attendue", "Échéance", "Pondération"
            };

            for (int i = 0; i < headers.Length; i++)
            {
                ws.Cell(1, i + 1).Value = headers[i];
                ws.Cell(1, i + 1).Style.Fill.BackgroundColor = CouleurAccent;
                ws.Cell(1, i + 1).Style.Font.FontColor = XLColor.White;
                ws.Cell(1, i + 1).Style.Font.Bold = true;
            }

            int row = 2;
            foreach (var e in entretiens)
            {
                if (e.NouveauxObjectifs == null || e.NouveauxObjectifs.Count == 0) continue;

                foreach (var obj in e.NouveauxObjectifs)
                {
                    ws.Cell(row, 1).Value = e.EmployeeId;
                    ws.Cell(row, 2).Value = e.EmployeeNomComplet;
                    ws.Cell(row, 3).Value = e.EmployeeService ?? "—";
                    ws.Cell(row, 4).Value = obj.Intitule;
                    ws.Cell(row, 5).Value = obj.IndicateurMesure ?? "—";
                    ws.Cell(row, 6).Value = obj.CibleAttendue ?? "—";
                    ws.Cell(row, 7).Value = obj.Echeance ?? "Annuelle";
                    ws.Cell(row, 8).Value = obj.Poids;

                    row++;
                }
            }

            ws.Columns(1, headers.Length).AdjustToContents();
        }

        private static void ConstruireFeuilleFormations(XLWorkbook wb, List<EntretienEvaluation> entretiens)
        {
            var ws = wb.Worksheets.Add("Besoins de Formation");
            ws.ShowGridLines = true;

            string[] headers = {
                "Matricule", "Salarié", "Service", "Poste",
                "Thème de formation demandé", "Compétence visée", "Priorité",
                "Statut", "Avis Évaluateur N+1"
            };

            for (int i = 0; i < headers.Length; i++)
            {
                ws.Cell(1, i + 1).Value = headers[i];
                ws.Cell(1, i + 1).Style.Fill.BackgroundColor = CouleurAccent;
                ws.Cell(1, i + 1).Style.Font.FontColor = XLColor.White;
                ws.Cell(1, i + 1).Style.Font.Bold = true;
            }

            int row = 2;
            foreach (var e in entretiens)
            {
                if (e.BesoinsFormation == null || e.BesoinsFormation.Count == 0) continue;

                foreach (var f in e.BesoinsFormation)
                {
                    ws.Cell(row, 1).Value = e.EmployeeId;
                    ws.Cell(row, 2).Value = e.EmployeeNomComplet;
                    ws.Cell(row, 3).Value = e.EmployeeService ?? "—";
                    ws.Cell(row, 4).Value = e.EmployeePoste ?? "—";
                    ws.Cell(row, 5).Value = f.ThemeFormation;
                    ws.Cell(row, 6).Value = f.CompetenceVisee ?? "—";
                    ws.Cell(row, 7).Value = f.PrioriteLibelle;
                    ws.Cell(row, 8).Value = f.StatutLibelle;
                    ws.Cell(row, 9).Value = f.AvisEvaluateur ?? "—";

                    row++;
                }
            }

            ws.Columns(1, headers.Length).AdjustToContents();
        }

        #endregion
    }
}
