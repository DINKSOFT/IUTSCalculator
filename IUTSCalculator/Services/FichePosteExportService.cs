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
    /// Service d'export des fiches de poste et du suivi budgétaire GPEC en PDF et Excel (module RH-02).
    /// </summary>
    public class FichePosteExportService
    {
        #region Couleurs & Polices Excel

        private static readonly XLColor CouleurAccent = XLColor.FromArgb(20, 103, 90);    // #14675A
        private static readonly XLColor CouleurEncre = XLColor.FromArgb(23, 33, 31);     // #17211F
        private static readonly XLColor CouleurDiscret = XLColor.FromArgb(121, 131, 126); // #79837E
        private static readonly XLColor CouleurTrame = XLColor.FromArgb(245, 246, 245);   // #F5F6F5
        private static readonly XLColor CouleurBordure = XLColor.FromArgb(203, 209, 204); // #CBD1CC
        private static readonly XLColor CouleurRouge = XLColor.FromArgb(198, 40, 40);    // #C62828 (Vacant)
        private static readonly XLColor CouleurAmbre = XLColor.FromArgb(230, 81, 0);     // #E65100 (Partiel)
        private static readonly XLColor CouleurVert = XLColor.FromArgb(27, 94, 32);      // #1B5E20 (Pourvu)
        private static readonly XLColor CouleurViolet = XLColor.FromArgb(106, 27, 154);  // #6A1B9A (Sureffectif)

        #endregion

        #region Couleurs & Polices PDF

        private static readonly BaseColor PdfPrimaryColor = new BaseColor(23, 33, 31);
        private static readonly BaseColor PdfAccentColor = new BaseColor(20, 103, 90);
        private static readonly BaseColor PdfLightBg = new BaseColor(245, 246, 245);
        private static readonly BaseColor PdfBorderColor = new BaseColor(203, 209, 204);
        private static readonly BaseColor PdfMutedColor = new BaseColor(100, 116, 139);

        private static readonly Font PdfTitleFont = new Font(Font.FontFamily.HELVETICA, 14, Font.BOLD, PdfAccentColor);
        private static readonly Font PdfSubTitleFont = new Font(Font.FontFamily.HELVETICA, 10, Font.BOLD, PdfPrimaryColor);
        private static readonly Font PdfSectionFont = new Font(Font.FontFamily.HELVETICA, 10.5f, Font.BOLD, PdfAccentColor);
        private static readonly Font PdfHeaderCellFont = new Font(Font.FontFamily.HELVETICA, 8.5f, Font.BOLD, BaseColor.WHITE);
        private static readonly Font PdfCellBoldFont = new Font(Font.FontFamily.HELVETICA, 8.5f, Font.BOLD, PdfPrimaryColor);
        private static readonly Font PdfCellNormalFont = new Font(Font.FontFamily.HELVETICA, 8.5f, Font.NORMAL, PdfPrimaryColor);
        private static readonly Font PdfSmallMutedFont = new Font(Font.FontFamily.HELVETICA, 7.5f, Font.NORMAL, PdfMutedColor);

        #endregion

        #region Export PDF de la Fiche de Poste Unitaire (iTextSharp A4)

        /// <summary>
        /// Surcharge pratique générant la fiche de poste à partir des collections attachées au modèle Poste.
        /// </summary>
        public bool GenererFichePostePdf(Poste poste, Dossier dossier, string cheminPdf)
        {
            var comp = poste?.CompetencesRequises != null ? new List<PosteCompetence>(poste.CompetencesRequises) : new List<PosteCompetence>();
            var titu = poste?.TitulairesActuels != null ? new List<TitulairePosteLigne>(poste.TitulairesActuels) : new List<TitulairePosteLigne>();
            return GenererFichePostePdf(poste, comp, titu, dossier, cheminPdf);
        }

        /// <summary>
        /// Génère le document PDF officiel A4 de la fiche de poste individuelle.
        /// Conforme aux standards d'administration du personnel et de la GPEC burkinabè.
        /// </summary>
        public bool GenererFichePostePdf(
            Poste poste,
            List<PosteCompetence> competences,
            List<TitulairePosteLigne> titulaires,
            Dossier dossier,
            string cheminPdf)
        {
            if (poste == null) throw new ArgumentNullException(nameof(poste));
            if (string.IsNullOrWhiteSpace(cheminPdf)) throw new ArgumentNullException(nameof(cheminPdf));

            competences = competences ?? new List<PosteCompetence>();
            titulaires = titulaires ?? new List<TitulairePosteLigne>();

            string dossierParent = Path.GetDirectoryName(cheminPdf);
            if (!string.IsNullOrEmpty(dossierParent) && !Directory.Exists(dossierParent))
            {
                Directory.CreateDirectory(dossierParent);
            }

            var doc = new Document(PageSize.A4, 30, 30, 30, 30);

            try
            {
                using (var fs = new FileStream(cheminPdf, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    var writer = PdfWriter.GetInstance(doc, fs);
                    doc.Open();

                    // 1. En-tête officiel de l'entreprise
                    var tableHeader = new PdfPTable(2) { WidthPercentage = 100 };
                    tableHeader.SetWidths(new float[] { 60f, 40f });

                    var cellSociete = new PdfPCell
                    {
                        Border = Rectangle.NO_BORDER,
                        PaddingBottom = 10f
                    };
                    string raison = dossier != null && !string.IsNullOrWhiteSpace(dossier.RaisonSociale)
                        ? dossier.RaisonSociale
                        : "ENTREPRISE (BURKINA FASO)";
                    string ifu = dossier != null && !string.IsNullOrWhiteSpace(dossier.IFU)
                        ? $"N° IFU : {dossier.IFU}"
                        : "";
                    cellSociete.AddElement(new Paragraph(raison, PdfSubTitleFont));
                    if (!string.IsNullOrEmpty(ifu))
                        cellSociete.AddElement(new Paragraph(ifu, PdfSmallMutedFont));
                    cellSociete.AddElement(new Paragraph("Direction des Ressources Humaines", PdfSmallMutedFont));
                    tableHeader.AddCell(cellSociete);

                    var cellTitreDoc = new PdfPCell
                    {
                        Border = Rectangle.NO_BORDER,
                        HorizontalAlignment = Element.ALIGN_RIGHT,
                        PaddingBottom = 10f
                    };
                    cellTitreDoc.AddElement(new Paragraph("FICHE DE POSTE & DE FONCTION", PdfTitleFont) { Alignment = Element.ALIGN_RIGHT });
                    cellTitreDoc.AddElement(new Paragraph($"Code : {poste.Code} — Édité le {DateTime.Now:dd/MM/yyyy}", PdfSmallMutedFont) { Alignment = Element.ALIGN_RIGHT });
                    tableHeader.AddCell(cellTitreDoc);

                    doc.Add(tableHeader);

                    // Ligne de séparation accentuée
                    var tableSep = new PdfPTable(1) { WidthPercentage = 100 };
                    var cellSep = new PdfPCell { BackgroundColor = PdfAccentColor, FixedHeight = 2.5f, Border = Rectangle.NO_BORDER };
                    tableSep.AddCell(cellSep);
                    doc.Add(tableSep);

                    doc.Add(new Paragraph(" ", new Font(Font.FontFamily.HELVETICA, 6)));

                    // 2. Section I : Identification du Poste & Rattachement
                    doc.Add(CreerTitreSection("I. IDENTIFICATION DU POSTE & RATTACHEMENT HIÉRARCHIQUE"));

                    var tableId = new PdfPTable(4) { WidthPercentage = 100, SpacingAfter = 10f };
                    tableId.SetWidths(new float[] { 22f, 28f, 22f, 28f });

                    AjouterLigneCleValeur(tableId, "Intitulé du poste :", poste.Intitule, "Code interne :", poste.Code);
                    AjouterLigneCleValeur(tableId, "Direction / Service :", poste.UniteLibelle ?? "(Non rattaché)", "Supérieur direct (N+1) :", poste.SuperieurPosteIntitule ?? "Direction Générale");
                    AjouterLigneCleValeur(tableId, "Effectif budgété :", $"{poste.EffectifRequis} poste(s)", "Effectif actuel :", $"{poste.EffectifPourvu} titulaire(s) ({poste.StatutOccupationGpec})");
                    AjouterLigneCleValeur(tableId, "Statut du poste :", poste.Actif ? "Actif au budget" : "Suspendu / Inactif", "Date de création :", poste.DateCreation.ToString("dd/MM/yyyy"));

                    doc.Add(tableId);

                    // 3. Section II : Classification Conventionnelle & Rémunération
                    doc.Add(CreerTitreSection("II. CLASSIFICATION CONVENTIONNELLE & RÉMUNÉRATION DE RÉFÉRENCE"));

                    var tableClassif = new PdfPTable(4) { WidthPercentage = 100, SpacingAfter = 10f };
                    tableClassif.SetWidths(new float[] { 22f, 28f, 22f, 28f });

                    string convNom = !string.IsNullOrWhiteSpace(poste.ConventionLibelle) ? poste.ConventionLibelle : "Convention Collective Interprofessionnelle (Burkina Faso)";
                    string catEch = $"{poste.CategorieConventionnelle ?? "-"} / {poste.EchelonConventionnel ?? "-"}";
                    string minConv = poste.SalaireMinimumConventionnel > 0 ? $"{poste.SalaireMinimumConventionnel:N0} FCFA" : "Non défini";
                    string fourchette = poste.SalaireMinBudgete > 0 || poste.SalaireMaxBudgete > 0
                        ? $"{poste.SalaireMinBudgete:N0} — {poste.SalaireMaxBudgete:N0} FCFA"
                        : "Grille d'entreprise";

                    AjouterLigneCleValeur(tableClassif, "Convention collective :", convNom, "Catégorie / Échelon :", catEch);
                    AjouterLigneCleValeur(tableClassif, "Salaire min conventionnel :", minConv, "Fourchette budgétée :", fourchette);

                    doc.Add(tableClassif);

                    // 4. Section III : Finalité & Missions Principales
                    doc.Add(CreerTitreSection("III. FINALITÉ & MISSIONS PRINCIPALES"));

                    var tableMissions = new PdfPTable(1) { WidthPercentage = 100, SpacingAfter = 10f };
                    string finalite = !string.IsNullOrWhiteSpace(poste.FinalitePrincipale)
                        ? poste.FinalitePrincipale
                        : "Assurer les missions assignées au poste dans le respect des procédures, des règles légales et des objectifs de l'entreprise.";

                    var cellFinalite = new PdfPCell(new Phrase($"Raison d'être (Finalité) :\n{finalite}\n", PdfCellNormalFont))
                    {
                        BackgroundColor = PdfLightBg,
                        Padding = 6f,
                        BorderColor = PdfBorderColor
                    };
                    tableMissions.AddCell(cellFinalite);

                    if (!string.IsNullOrWhiteSpace(poste.Missions))
                    {
                        var cellMissionsTexte = new PdfPCell(new Phrase($"Missions générales :\n{poste.Missions}", PdfCellNormalFont))
                        {
                            Padding = 6f,
                            BorderColor = PdfBorderColor
                        };
                        tableMissions.AddCell(cellMissionsTexte);
                    }

                    if (!string.IsNullOrWhiteSpace(poste.ActivitesCles))
                    {
                        var cellActivites = new PdfPCell(new Phrase($"Activités clés & Responsabilités quotidiennes :\n{poste.ActivitesCles}", PdfCellNormalFont))
                        {
                            Padding = 6f,
                            BorderColor = PdfBorderColor
                        };
                        tableMissions.AddCell(cellActivites);
                    }

                    doc.Add(tableMissions);

                    // 5. Section IV : Profil & Qualifications Requises
                    doc.Add(CreerTitreSection("IV. PROFIL & QUALIFICATIONS REQUISES"));

                    var tableProfil = new PdfPTable(4) { WidthPercentage = 100, SpacingAfter = 10f };
                    tableProfil.SetWidths(new float[] { 22f, 28f, 22f, 28f });

                    string niveauEtude = !string.IsNullOrWhiteSpace(poste.NiveauEtudeRequis) ? poste.NiveauEtudeRequis : "Non précisé";
                    string diplome = !string.IsNullOrWhiteSpace(poste.DiplomeRequis) ? poste.DiplomeRequis : "Diplôme équivalent";
                    string expMin = poste.ExperienceMinimaleAnnees > 0 ? $"{poste.ExperienceMinimaleAnnees} an(s) minimum" : "Débutant accepté";
                    string conditions = !string.IsNullOrWhiteSpace(poste.ConditionsTravail) ? poste.ConditionsTravail : "Horaires standards, bureau";

                    AjouterLigneCleValeur(tableProfil, "Niveau d'études :", niveauEtude, "Diplôme exigé :", diplome);
                    AjouterLigneCleValeur(tableProfil, "Expérience requise :", expMin, "Conditions de travail :", conditions);

                    doc.Add(tableProfil);

                    // 6. Section V : Compétences Exigées & Niveaux de Maîtrise
                    doc.Add(CreerTitreSection("V. MATRICE DES COMPÉTENCES EXIGÉES"));

                    if (competences.Count == 0)
                    {
                        var pVide = new Paragraph("Aucune compétence spécifique répertoriée pour ce poste.", PdfSmallMutedFont) { SpacingAfter = 10f };
                        doc.Add(pVide);
                    }
                    else
                    {
                        var tableComp = new PdfPTable(4) { WidthPercentage = 100, SpacingAfter = 10f };
                        tableComp.SetWidths(new float[] { 35f, 25f, 20f, 20f });

                        // En-têtes
                        tableComp.AddCell(CreerCelluleEntete("Compétence"));
                        tableComp.AddCell(CreerCelluleEntete("Domaine"));
                        tableComp.AddCell(CreerCelluleEntete("Criticité"));
                        tableComp.AddCell(CreerCelluleEntete("Niveau Exigé"));

                        foreach (var c in competences)
                        {
                            tableComp.AddCell(new PdfPCell(new Phrase(c.CompetenceLibelle, PdfCellBoldFont)) { Padding = 4f, BorderColor = PdfBorderColor });
                            tableComp.AddCell(new PdfPCell(new Phrase(c.Domaine.ToString(), PdfCellNormalFont)) { Padding = 4f, BorderColor = PdfBorderColor });
                            tableComp.AddCell(new PdfPCell(new Phrase(c.CriticiteLibelle, PdfCellNormalFont)) { Padding = 4f, BorderColor = PdfBorderColor });
                            tableComp.AddCell(new PdfPCell(new Phrase($"{c.NiveauLibelle} {c.NiveauEtoiles}", PdfCellBoldFont)) { Padding = 4f, BorderColor = PdfBorderColor, HorizontalAlignment = Element.ALIGN_CENTER });
                        }

                        doc.Add(tableComp);
                    }

                    // 7. Section VI : Salariés Actuellement en Poste (Titulaires)
                    if (titulaires.Count > 0)
                    {
                        doc.Add(CreerTitreSection("VI. TITULAIRE(S) ACTUELLEMENT AFFECTÉ(S)"));

                        var tableTit = new PdfPTable(5) { WidthPercentage = 100, SpacingAfter = 10f };
                        tableTit.SetWidths(new float[] { 15f, 35f, 20f, 15f, 15f });

                        tableTit.AddCell(CreerCelluleEntete("Matricule"));
                        tableTit.AddCell(CreerCelluleEntete("Nom & Prénom"));
                        tableTit.AddCell(CreerCelluleEntete("Département / Service"));
                        tableTit.AddCell(CreerCelluleEntete("Catégorie"));
                        tableTit.AddCell(CreerCelluleEntete("Ancienneté"));

                        foreach (var t in titulaires)
                        {
                            tableTit.AddCell(new PdfPCell(new Phrase(t.EmployeeId, PdfCellBoldFont)) { Padding = 4f, BorderColor = PdfBorderColor });
                            tableTit.AddCell(new PdfPCell(new Phrase(t.NomComplet, PdfCellNormalFont)) { Padding = 4f, BorderColor = PdfBorderColor });
                            tableTit.AddCell(new PdfPCell(new Phrase(t.Department ?? "-", PdfCellNormalFont)) { Padding = 4f, BorderColor = PdfBorderColor });
                            tableTit.AddCell(new PdfPCell(new Phrase(t.Categorie ?? "-", PdfCellNormalFont)) { Padding = 4f, BorderColor = PdfBorderColor, HorizontalAlignment = Element.ALIGN_CENTER });
                            tableTit.AddCell(new PdfPCell(new Phrase(t.AncienneteTexte, PdfCellNormalFont)) { Padding = 4f, BorderColor = PdfBorderColor, HorizontalAlignment = Element.ALIGN_CENTER });
                        }

                        doc.Add(tableTit);
                    }

                    // 8. Section VII : Cadre de Validation & Signatures
                    doc.Add(CreerTitreSection("VII. VALIDATION ET SIGNATURES"));

                    var tableSign = new PdfPTable(3) { WidthPercentage = 100, SpacingBefore = 5f };
                    tableSign.SetWidths(new float[] { 33f, 34f, 33f });

                    tableSign.AddCell(CreerCelluleSignature("Le Titulaire du Poste", "Mention « Lu et approuvé »\nDate & Signature :"));
                    tableSign.AddCell(CreerCelluleSignature("Le Supérieur Hiérarchique (N+1)", "Avis & Visa\nDate & Signature :"));
                    tableSign.AddCell(CreerCelluleSignature("La Direction des Ressources Humaines", "Approbation DRH\nDate, Cachet & Signature :"));

                    doc.Add(tableSign);

                    doc.Close();
                    return true;
                }
            }
            catch
            {
                if (doc.IsOpen()) doc.Close();
                throw;
            }
        }

        private static Paragraph CreerTitreSection(string titre)
        {
            return new Paragraph(titre, PdfSectionFont)
            {
                SpacingBefore = 4f,
                SpacingAfter = 4f
            };
        }

        private static PdfPCell CreerCelluleEntete(string texte)
        {
            return new PdfPCell(new Phrase(texte, PdfHeaderCellFont))
            {
                BackgroundColor = PdfAccentColor,
                HorizontalAlignment = Element.ALIGN_CENTER,
                VerticalAlignment = Element.ALIGN_MIDDLE,
                Padding = 5f,
                BorderColor = PdfBorderColor
            };
        }

        private static void AjouterLigneCleValeur(PdfPTable table, string cle1, string val1, string cle2, string val2)
        {
            table.AddCell(new PdfPCell(new Phrase(cle1, PdfCellBoldFont)) { BackgroundColor = PdfLightBg, BorderColor = PdfBorderColor, Padding = 4f });
            table.AddCell(new PdfPCell(new Phrase(val1 ?? "-", PdfCellNormalFont)) { BorderColor = PdfBorderColor, Padding = 4f });
            table.AddCell(new PdfPCell(new Phrase(cle2, PdfCellBoldFont)) { BackgroundColor = PdfLightBg, BorderColor = PdfBorderColor, Padding = 4f });
            table.AddCell(new PdfPCell(new Phrase(val2 ?? "-", PdfCellNormalFont)) { BorderColor = PdfBorderColor, Padding = 4f });
        }

        private static PdfPCell CreerCelluleSignature(string role, string instructions)
        {
            var cell = new PdfPCell
            {
                BorderColor = PdfBorderColor,
                MinimumHeight = 65f,
                Padding = 5f,
                BackgroundColor = PdfLightBg
            };
            cell.AddElement(new Paragraph(role, PdfCellBoldFont));
            cell.AddElement(new Paragraph(instructions, PdfSmallMutedFont) { SpacingBefore = 3f });
            return cell;
        }

        #endregion

        #region Export Excel de Suivi Budgétaire & Référentiel des Postes (ClosedXML)

        /// <summary>
        /// Exporte le suivi complet des postes dans un classeur ClosedXML à 3 feuilles :
        /// 1. Synthèse Budget vs Pourvu (GPEC)
        /// 2. Référentiel des Fiches de Postes
        /// 3. Matrice des Compétences
        /// </summary>
        public string ExporterSyntheseExcel(
            List<SynthesePosteBudget> syntheses,
            List<Competence> catalogueCompetences,
            List<PosteCompetence> toutesCompetencesPostes,
            string raisonSociale,
            string cheminExcel)
        {
            if (string.IsNullOrWhiteSpace(cheminExcel))
                throw new ArgumentNullException(nameof(cheminExcel));

            syntheses = syntheses ?? new List<SynthesePosteBudget>();
            catalogueCompetences = catalogueCompetences ?? new List<Competence>();
            toutesCompetencesPostes = toutesCompetencesPostes ?? new List<PosteCompetence>();

            string dossierParent = Path.GetDirectoryName(cheminExcel);
            if (!string.IsNullOrEmpty(dossierParent) && !Directory.Exists(dossierParent))
            {
                Directory.CreateDirectory(dossierParent);
            }

            using (var workbook = new XLWorkbook())
            {
                // ── FEUILLE 1 : Synthèse Budget vs Pourvu ──
                var wsBudget = workbook.Worksheets.Add("Budget vs Pourvu");
                wsBudget.ShowGridLines = true;

                // Titre
                wsBudget.Cell("A1").Value = $"SUIVI BUDGÉTAIRE DES EFFECTIFS & VACANCES DE POSTES — {raisonSociale?.ToUpperInvariant()}";
                wsBudget.Range("A1:I1").Merge().Style
                    .Font.SetBold().Font.SetFontSize(13).Font.SetFontColor(XLColor.White)
                    .Fill.SetBackgroundColor(CouleurAccent)
                    .Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center);

                wsBudget.Cell("A2").Value = $"Généré le {DateTime.Now:dd/MM/yyyy à HH:mm} — Source : Module RH-02 Sigma Paie & RH";
                wsBudget.Range("A2:I2").Merge().Style
                    .Font.SetItalic().Font.SetFontSize(9).Font.SetFontColor(CouleurDiscret)
                    .Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center);

                // KPI résumés
                int totalBudgete = syntheses.Sum(s => s.EffectifBudgete);
                int totalPourvu = syntheses.Sum(s => s.EffectifPourvu);
                int totalVacants = syntheses.Count(s => s.EffectifPourvu == 0);
                int totalSureffectif = syntheses.Count(s => s.EffectifPourvu > s.EffectifBudgete);
                double tauxGlobal = totalBudgete > 0 ? Math.Round((double)totalPourvu / totalBudgete * 100.0, 1) : 0;

                wsBudget.Cell("A4").Value = "Postes créés :";
                wsBudget.Cell("B4").Value = syntheses.Count;
                wsBudget.Cell("C4").Value = "Effectif budgété :";
                wsBudget.Cell("D4").Value = totalBudgete;
                wsBudget.Cell("E4").Value = "Effectif pourvu :";
                wsBudget.Cell("F4").Value = totalPourvu;
                wsBudget.Cell("G4").Value = "Taux d'occupation :";
                wsBudget.Cell("H4").Value = $"{tauxGlobal} %";

                wsBudget.Range("A4:H4").Style.Font.SetBold().Fill.SetBackgroundColor(CouleurTrame);

                // Tableau principal
                string[] headersBudget = { "Code Poste", "Intitulé du Poste", "Direction / Service", "Supérieur (N+1)", "Budget", "Pourvu", "Écart", "Taux", "Statut GPEC" };
                for (int c = 0; c < headersBudget.Length; c++)
                {
                    var cell = wsBudget.Cell(6, c + 1);
                    cell.Value = headersBudget[c];
                    cell.Style.Font.SetBold().Font.SetFontColor(XLColor.White)
                        .Fill.SetBackgroundColor(CouleurAccent)
                        .Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center);
                }

                int row = 7;
                foreach (var syn in syntheses)
                {
                    wsBudget.Cell(row, 1).Value = syn.Code;
                    wsBudget.Cell(row, 2).Value = syn.Intitule;
                    wsBudget.Cell(row, 3).Value = syn.UniteLibelle;
                    wsBudget.Cell(row, 4).Value = syn.SuperieurIntitule;
                    wsBudget.Cell(row, 5).Value = syn.EffectifBudgete;
                    wsBudget.Cell(row, 6).Value = syn.EffectifPourvu;
                    wsBudget.Cell(row, 7).Value = syn.Ecart;
                    wsBudget.Cell(row, 8).Value = $"{syn.TauxOccupation} %";
                    wsBudget.Cell(row, 9).Value = syn.StatutOccupation;

                    // Aligner les chiffres
                    wsBudget.Range(row, 5, row, 8).Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center);

                    // Colorer le statut
                    var cellStatut = wsBudget.Cell(row, 9);
                    cellStatut.Style.Font.SetBold().Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center);
                    if (syn.StatutOccupation == "Vacant")
                    {
                        cellStatut.Style.Font.SetFontColor(CouleurRouge).Fill.SetBackgroundColor(XLColor.FromHtml("#FFEBEE"));
                    }
                    else if (syn.StatutOccupation == "Partiel")
                    {
                        cellStatut.Style.Font.SetFontColor(CouleurAmbre).Fill.SetBackgroundColor(XLColor.FromHtml("#FFF3E0"));
                    }
                    else if (syn.StatutOccupation == "Pourvu")
                    {
                        cellStatut.Style.Font.SetFontColor(CouleurVert).Fill.SetBackgroundColor(XLColor.FromHtml("#E8F5E9"));
                    }
                    else
                    {
                        cellStatut.Style.Font.SetFontColor(CouleurViolet).Fill.SetBackgroundColor(XLColor.FromHtml("#F3E5F5"));
                    }

                    row++;
                }

                wsBudget.Columns().AdjustToContents();

                // ── FEUILLE 2 : Référentiel des Postes ──
                var wsPostes = workbook.Worksheets.Add("Nomenclature des Postes");
                wsPostes.ShowGridLines = true;

                wsPostes.Cell("A1").Value = $"RÉFÉRENTIEL DES POSTES & CLASSIFICATION — {raisonSociale?.ToUpperInvariant()}";
                wsPostes.Range("A1:J1").Merge().Style
                    .Font.SetBold().Font.SetFontSize(13).Font.SetFontColor(XLColor.White)
                    .Fill.SetBackgroundColor(CouleurAccent)
                    .Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center);

                string[] headersPostes = { "Code", "Intitulé", "Service", "Catégorie", "Échelon", "Salaire Min Conv.", "Salaire Min Budg.", "Salaire Max Budg.", "Diplôme Requis", "Expérience Min." };
                for (int c = 0; c < headersPostes.Length; c++)
                {
                    var cell = wsPostes.Cell(3, c + 1);
                    cell.Value = headersPostes[c];
                    cell.Style.Font.SetBold().Font.SetFontColor(XLColor.White)
                        .Fill.SetBackgroundColor(CouleurAccent)
                        .Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center);
                }

                int rPoste = 4;
                foreach (var syn in syntheses)
                {
                    wsPostes.Cell(rPoste, 1).Value = syn.Code;
                    wsPostes.Cell(rPoste, 2).Value = syn.Intitule;
                    wsPostes.Cell(rPoste, 3).Value = syn.UniteLibelle;
                    wsPostes.Cell(rPoste, 4).Value = syn.CategorieConventionnelle ?? "-";
                    wsPostes.Cell(rPoste, 5).Value = syn.EchelonConventionnel ?? "-";
                    wsPostes.Cell(rPoste, 6).Value = syn.SalaireMinConventionnel;
                    wsPostes.Cell(rPoste, 7).Value = syn.SalaireMinBudgete;
                    wsPostes.Cell(rPoste, 8).Value = syn.SalaireMaxBudgete;
                    wsPostes.Cell(rPoste, 9).Value = "Selon fiche de poste";
                    wsPostes.Cell(rPoste, 10).Value = "-";

                    wsPostes.Range(rPoste, 6, rPoste, 8).Style.NumberFormat.Format = "#,##0 \"FCFA\"";

                    rPoste++;
                }

                wsPostes.Columns().AdjustToContents();

                // ── FEUILLE 3 : Référentiel des Compétences ──
                var wsComp = workbook.Worksheets.Add("Catalogue Compétences");
                wsComp.ShowGridLines = true;

                wsComp.Cell("A1").Value = $"RÉFÉRENTIEL DES COMPÉTENCES D'ENTREPRISE — {raisonSociale?.ToUpperInvariant()}";
                wsComp.Range("A1:D1").Merge().Style
                    .Font.SetBold().Font.SetFontSize(13).Font.SetFontColor(XLColor.White)
                    .Fill.SetBackgroundColor(CouleurAccent)
                    .Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center);

                string[] headersComp = { "Code Compétence", "Libellé de la Compétence", "Famille / Domaine", "Description du Savoir-Faire" };
                for (int c = 0; c < headersComp.Length; c++)
                {
                    var cell = wsComp.Cell(3, c + 1);
                    cell.Value = headersComp[c];
                    cell.Style.Font.SetBold().Font.SetFontColor(XLColor.White)
                        .Fill.SetBackgroundColor(CouleurAccent)
                        .Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center);
                }

                int rComp = 4;
                foreach (var c in catalogueCompetences)
                {
                    wsComp.Cell(rComp, 1).Value = c.Code;
                    wsComp.Cell(rComp, 2).Value = c.Libelle;
                    wsComp.Cell(rComp, 3).Value = c.DomaineLibelle;
                    wsComp.Cell(rComp, 4).Value = c.Description ?? "-";
                    rComp++;
                }

                wsComp.Columns().AdjustToContents();

                workbook.SaveAs(cheminExcel);
            }

            return cheminExcel;
        }

        #endregion
    }
}
