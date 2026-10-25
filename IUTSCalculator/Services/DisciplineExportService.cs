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
    /// <summary>Lettres que le module Discipline sait produire.</summary>
    public enum LettreDiscipline
    {
        DemandeExplication = 0,
        Convocation = 1,
        MiseAPiedConservatoire = 2,
        NotificationSanction = 3,
        Licenciement = 4
    }

    /// <summary>
    /// Lettres disciplinaires (PDF A4) et registre des sanctions (Excel) — module RH-06.
    ///
    /// Les lettres ne citent pas de numéro d'article : le Code du travail a changé en 2026
    /// (loi n° 013-2026/ALP) et les références du règlement intérieur varient d'un dossier
    /// à l'autre. Elles renvoient au Code du travail, au règlement intérieur (référence
    /// saisie dans les paramètres) et à la convention collective.
    /// </summary>
    public class DisciplineExportService
    {
        private static readonly CultureInfo Fr = LibellesDiscipline.Fr;

        private static readonly BaseColor Encre = new BaseColor(23, 33, 31);
        private static readonly BaseColor Accent = new BaseColor(20, 103, 90);
        private static readonly BaseColor Trame = new BaseColor(245, 246, 245);
        private static readonly BaseColor Bordure = new BaseColor(203, 209, 204);
        private static readonly BaseColor Discret = new BaseColor(100, 116, 139);

        private static readonly Font FTitre = new Font(Font.FontFamily.HELVETICA, 13f, Font.BOLD, Accent);
        private static readonly Font FSousTitre = new Font(Font.FontFamily.HELVETICA, 8.5f, Font.NORMAL, Discret);
        private static readonly Font FObjet = new Font(Font.FontFamily.HELVETICA, 10f, Font.BOLD, Encre);
        private static readonly Font FCorps = new Font(Font.FontFamily.HELVETICA, 9.5f, Font.NORMAL, Encre);
        private static readonly Font FCorpsGras = new Font(Font.FontFamily.HELVETICA, 9.5f, Font.BOLD, Encre);
        private static readonly Font FNote = new Font(Font.FontFamily.HELVETICA, 7.5f, Font.ITALIC, Discret);
        private static readonly Font FPetit = new Font(Font.FontFamily.HELVETICA, 8.5f, Font.NORMAL, Encre);

        /// <summary>Type de pièce sous lequel la lettre est versée au dossier.</summary>
        public static TypePiece TypePiecePour(LettreDiscipline lettre)
        {
            switch (lettre)
            {
                case LettreDiscipline.DemandeExplication: return TypePiece.DemandeExplication;
                case LettreDiscipline.Convocation: return TypePiece.Convocation;
                case LettreDiscipline.MiseAPiedConservatoire: return TypePiece.MiseAPiedConservatoire;
                default: return TypePiece.Notification;
            }
        }

        public static string Libelle(LettreDiscipline lettre)
        {
            switch (lettre)
            {
                case LettreDiscipline.DemandeExplication: return "Demande d'explication";
                case LettreDiscipline.Convocation: return "Convocation à entretien préalable";
                case LettreDiscipline.MiseAPiedConservatoire: return "Mise à pied conservatoire";
                case LettreDiscipline.NotificationSanction: return "Notification de sanction";
                default: return "Lettre de licenciement";
            }
        }

        /// <summary>
        /// Ce qui manque pour produire la lettre, ou null. Une lettre incomplète (sans date
        /// limite, sans motifs) serait pire que pas de lettre.
        /// </summary>
        public static string Prerequis(LettreDiscipline lettre, ProcedureDisciplinaire p)
        {
            if (p == null) return "Aucune procédure sélectionnée.";
            if (p.Id <= 0) return "Enregistrez d'abord la procédure.";
            var type = p.TypeSanction;
            switch (lettre)
            {
                case LettreDiscipline.DemandeExplication:
                    if (!p.DateDemandeExplication.HasValue) return "Renseignez la date de la demande d'explication, puis enregistrez.";
                    if (string.IsNullOrWhiteSpace(p.DescriptionFaits)) return "Décrivez les faits reprochés.";
                    return null;
                case LettreDiscipline.Convocation:
                    if (!p.DateConvocation.HasValue || !p.DateEntretien.HasValue) return "Renseignez les dates de convocation et d'entretien, puis enregistrez.";
                    return null;
                case LettreDiscipline.MiseAPiedConservatoire:
                    if (!p.ConservatoireDebut.HasValue) return "Renseignez la date de début de la mise à pied conservatoire, puis enregistrez.";
                    return null;
                case LettreDiscipline.NotificationSanction:
                    if (type == null || !p.DateDecision.HasValue) return "Enregistrez d'abord la décision (sanction et date).";
                    if (type.EstLicenciement) return "Pour un licenciement, utilisez la lettre de licenciement.";
                    return null;
                default:
                    if (type == null || !type.EstLicenciement || !p.DateDecision.HasValue) return "Enregistrez d'abord une décision de licenciement.";
                    if (string.IsNullOrWhiteSpace(p.MotifsDecision)) return "Rédigez les motifs du licenciement.";
                    if (!p.DateEffetLicenciement.HasValue) return "Indiquez la date d'effet du licenciement.";
                    return null;
            }
        }

        // ═══════════════════════════════════════════════════════════════════
        //  Lettres PDF
        // ═══════════════════════════════════════════════════════════════════

        public void GenererLettre(
            LettreDiscipline lettre, ProcedureDisciplinaire p, Employee salarie, ParametresDiscipline prm,
            ISet<DateTime> feries, string chemin)
        {
            var manque = Prerequis(lettre, p);
            if (manque != null) throw new InvalidOperationException(manque);
            prm = prm ?? ParametresDiscipline.Defaut();

            var paragraphes = new List<Paragraph>();
            string objet;
            DateTime dateLettre;
            string reglement = string.IsNullOrWhiteSpace(prm.ReferenceReglementInterieur)
                ? "le règlement intérieur de l'entreprise"
                : "le règlement intérieur de l'entreprise (" + prm.ReferenceReglementInterieur.Trim() + ")";
            string faits = DescriptionDesFaits(p);
            var type = p.TypeSanction;

            switch (lettre)
            {
                case LettreDiscipline.DemandeExplication:
                {
                    dateLettre = p.DateDemandeExplication.Value;
                    var limite = ControleDiscipline.DateLimiteReponse(p, prm, feries).Value;
                    objet = "Demande d'explication écrite";
                    paragraphes.Add(P("Il vous est reproché les faits suivants :"));
                    paragraphes.Add(Encadre(faits));
                    paragraphes.Add(P("Ces faits sont susceptibles de constituer un manquement à vos obligations professionnelles et à " +
                                      reglement + "."));
                    paragraphes.Add(PMixte(
                        "Afin de vous permettre de présenter votre défense, nous vous demandons de nous faire parvenir vos explications ÉCRITES ",
                        string.Format(Fr, "au plus tard le {0:dddd dd MMMM yyyy}", limite),
                        string.Format(Fr, " ({0} jour(s) ouvrable(s) à compter de la remise de la présente).", prm.DelaiReponseJoursOuvrables)));
                    paragraphes.Add(P("À défaut de réponse dans ce délai, la direction statuera au vu des seuls éléments en sa possession. " +
                                      "La présente demande ne préjuge pas de la suite qui sera donnée."));
                    break;
                }

                case LettreDiscipline.Convocation:
                {
                    dateLettre = p.DateConvocation.Value;
                    objet = "Convocation à un entretien préalable à une éventuelle sanction";
                    string envisagee = type == null
                        ? "une sanction disciplinaire"
                        : (type.EstLicenciement ? "une sanction disciplinaire pouvant aller jusqu'au licenciement" : "une sanction disciplinaire (" + type.Libelle.ToLower(Fr) + ")");
                    paragraphes.Add(P(string.Format("Nous envisageons de prendre à votre égard {0}, en raison des faits suivants :", envisagee)));
                    paragraphes.Add(Encadre(faits));
                    paragraphes.Add(PMixte("Vous êtes convoqué(e) à un entretien préalable ",
                        string.Format(Fr, "le {0:dddd dd MMMM yyyy}", p.DateEntretien.Value),
                        string.IsNullOrWhiteSpace(p.HeureLieuEntretien) ? "." : ", " + p.HeureLieuEntretien.Trim() + "."));
                    paragraphes.Add(P("Au cours de cet entretien, nous vous exposerons les motifs de la mesure envisagée et recueillerons vos explications. " +
                                      "Vous pouvez vous faire assister par une personne de votre choix appartenant au personnel de l'entreprise, " +
                                      "notamment un délégué du personnel."));
                    if (p.ConservatoireDebut.HasValue)
                        paragraphes.Add(P(string.Format(Fr, "Nous vous rappelons que vous faites l'objet d'une mise à pied conservatoire depuis le {0:dd/MM/yyyy}, " +
                                                            "dans l'attente de la décision.", p.ConservatoireDebut.Value)));
                    break;
                }

                case LettreDiscipline.MiseAPiedConservatoire:
                {
                    dateLettre = p.ConservatoireDebut.Value;
                    objet = "Notification d'une mise à pied à titre conservatoire";
                    paragraphes.Add(P("Compte tenu de la gravité des faits qui vous sont reprochés :"));
                    paragraphes.Add(Encadre(faits));
                    paragraphes.Add(PMixte("nous vous notifions une mise à pied à titre conservatoire ",
                        string.Format(Fr, "à compter du {0:dddd dd MMMM yyyy}", p.ConservatoireDebut.Value),
                        ", dans l'attente de la décision qui sera prise à l'issue de la procédure disciplinaire engagée à votre encontre."));
                    paragraphes.Add(P("Cette mesure n'est pas une sanction. Vous êtes dispensé(e) de vous présenter à votre poste pendant sa durée. " +
                                      "Le sort de votre rémunération pour cette période dépendra de la décision finale."));
                    break;
                }

                case LettreDiscipline.NotificationSanction:
                {
                    dateLettre = p.DateNotification ?? p.DateDecision.Value;
                    objet = "Notification d'une sanction disciplinaire : " + type.Libelle.ToLower(Fr);
                    paragraphes.Add(P(Rappel(p)));
                    paragraphes.Add(Encadre(faits));
                    if (!string.IsNullOrWhiteSpace(p.MotifsDecision))
                    {
                        paragraphes.Add(P("Les explications recueillies ne nous ont pas permis de modifier notre appréciation, pour les motifs suivants :"));
                        paragraphes.Add(Encadre(p.MotifsDecision.Trim()));
                    }
                    paragraphes.Add(PMixte("En conséquence, et conformément à " + reglement + ", nous vous notifions la sanction suivante : ",
                        type.Libelle.ToUpper(Fr), "."));
                    if (type.EstMiseAPied && p.MiseAPiedDebut.HasValue && p.MiseAPiedFin.HasValue)
                    {
                        paragraphes.Add(PMixte("Cette mise à pied s'étendra ",
                            string.Format(Fr, "du {0:dddd dd MMMM yyyy} au {1:dddd dd MMMM yyyy} inclus", p.MiseAPiedDebut.Value, p.MiseAPiedFin.Value),
                            string.Format(Fr, ", soit {0:0.##} jour(s). Pendant cette période, votre contrat de travail est suspendu et ne donne pas lieu à " +
                                              "rémunération. Vous reprendrez votre poste le {1:dddd dd MMMM yyyy}.",
                                p.MiseAPiedJours, p.MiseAPiedFin.Value.AddDays(1))));
                    }
                    if (p.ConservatoireDebut.HasValue)
                        paragraphes.Add(P("La période de mise à pied conservatoire qui a précédé la présente décision vous sera rémunérée."));
                    paragraphes.Add(P("Nous espérons que cette mesure vous conduira à un comportement conforme à vos obligations. " +
                                      "Vous disposez des voies de recours prévues par le Code du travail (inspection du travail, juridiction compétente)."));
                    break;
                }

                default:
                {
                    dateLettre = p.DateNotification ?? p.DateDecision.Value;
                    objet = type.FauteLourde ? "Notification de licenciement pour faute lourde" : "Notification de licenciement pour motif disciplinaire";
                    paragraphes.Add(P(Rappel(p)));
                    paragraphes.Add(Encadre(faits));
                    paragraphes.Add(P("Nous vous notifions par la présente votre licenciement, pour les motifs suivants :"));
                    paragraphes.Add(Encadre(p.MotifsDecision.Trim()));
                    if (p.SalarieProtege && p.DateDecisionInspection.HasValue)
                        paragraphes.Add(P(string.Format(Fr, "Ce licenciement intervient après l'autorisation de l'inspecteur du travail en date du {0:dd/MM/yyyy}{1}.",
                            p.DateDecisionInspection.Value,
                            string.IsNullOrWhiteSpace(p.ReferenceInspection) ? "" : " (réf. " + p.ReferenceInspection.Trim() + ")")));
                    if (type.FauteLourde)
                    {
                        paragraphes.Add(PMixte("Ces faits constituent une faute lourde, rendant impossible votre maintien dans l'entreprise. Votre licenciement prend effet ",
                            string.Format(Fr, "le {0:dddd dd MMMM yyyy}", p.DateEffetLicenciement.Value),
                            ", sans préavis ni indemnité de licenciement."));
                        if (p.ConservatoireDebut.HasValue)
                            paragraphes.Add(P(string.Format(Fr, "La période de mise à pied conservatoire, débutée le {0:dd/MM/yyyy}, ne sera pas rémunérée.", p.ConservatoireDebut.Value)));
                    }
                    else
                    {
                        paragraphes.Add(PMixte("Votre contrat de travail prendra fin ",
                            string.Format(Fr, "le {0:dddd dd MMMM yyyy}", p.DateEffetLicenciement.Value),
                            ", à l'issue du préavis prévu par la convention collective applicable. L'indemnité de licenciement vous sera versée avec votre solde de tout compte."));
                        if (p.ConservatoireDebut.HasValue)
                            paragraphes.Add(P("La période de mise à pied conservatoire qui a précédé la présente décision vous sera rémunérée."));
                    }
                    paragraphes.Add(P("Votre certificat de travail et votre solde de tout compte seront tenus à votre disposition à la date de fin de votre contrat. " +
                                      "Vous disposez des voies de recours prévues par le Code du travail."));
                    break;
                }
            }

            Ecrire(chemin, p, salarie, prm, dateLettre, objet, paragraphes, lettre);
        }

        private static string Rappel(ProcedureDisciplinaire p)
        {
            var etapes = new List<string>();
            if (p.DateDemandeExplication.HasValue)
                etapes.Add(string.Format(Fr, "notre demande d'explication du {0:dd/MM/yyyy}", p.DateDemandeExplication.Value) +
                           (p.DateReponse.HasValue ? string.Format(Fr, ", à laquelle vous avez répondu le {0:dd/MM/yyyy}", p.DateReponse.Value) : ", restée sans réponse dans le délai imparti"));
            if (p.DateEntretien.HasValue)
                etapes.Add(string.Format(Fr, "l'entretien préalable du {0:dd/MM/yyyy}", p.DateEntretien.Value) +
                           (string.IsNullOrWhiteSpace(p.AssistantEntretien) ? "" : ", au cours duquel vous étiez assisté(e) de " + p.AssistantEntretien.Trim()));
            return etapes.Count == 0
                ? "Nous revenons vers vous au sujet des faits suivants :"
                : "Faisant suite à " + string.Join(", puis à ", etapes) + ", nous revenons vers vous au sujet des faits suivants :";
        }

        private static string DescriptionDesFaits(ProcedureDisciplinaire p)
        {
            var debut = string.Format(Fr, "Le {0:dddd dd MMMM yyyy}", p.DateFaits);
            if (!string.IsNullOrWhiteSpace(p.LieuFaits)) debut += ", " + p.LieuFaits.Trim();
            var texte = debut + " : " + (string.IsNullOrWhiteSpace(p.DescriptionFaits) ? (p.CategorieFaits ?? "faits à préciser") : p.DescriptionFaits.Trim());
            if (!texte.EndsWith(".", StringComparison.Ordinal)) texte += ".";
            return texte;
        }

        private void Ecrire(string chemin, ProcedureDisciplinaire p, Employee salarie, ParametresDiscipline prm,
            DateTime dateLettre, string objet, List<Paragraph> paragraphes, LettreDiscipline lettre)
        {
            var dossier = DossierSession.Instance.CurrentDossier;
            string raison = string.IsNullOrWhiteSpace(dossier?.RaisonSociale) ? "L'EMPLOYEUR" : dossier.RaisonSociale.Trim();
            string ville = string.IsNullOrWhiteSpace(dossier?.Ville) ? "Ouagadougou" : dossier.Ville.Trim();

            using (var fs = new FileStream(chemin, FileMode.Create, FileAccess.Write, FileShare.None))
            using (var doc = new Document(PageSize.A4, 50f, 50f, 40f, 40f))
            {
                PdfWriter.GetInstance(doc, fs);
                doc.Open();

                // En-tête
                var entete = new PdfPTable(2) { WidthPercentage = 100f };
                entete.SetWidths(new[] { 60f, 40f });
                var gauche = new PdfPCell { Border = Rectangle.NO_BORDER };
                gauche.AddElement(new Paragraph(raison.ToUpper(Fr), FTitre));
                var coord = new List<string>();
                if (!string.IsNullOrWhiteSpace(dossier?.Adresse)) coord.Add(dossier.Adresse.Trim());
                if (!string.IsNullOrWhiteSpace(dossier?.Telephone)) coord.Add("Tél. " + dossier.Telephone.Trim());
                if (coord.Count > 0) gauche.AddElement(new Paragraph(string.Join(" | ", coord), FSousTitre));
                var ids = new List<string>();
                if (!string.IsNullOrWhiteSpace(dossier?.IFU)) ids.Add("IFU " + dossier.IFU.Trim());
                if (!string.IsNullOrWhiteSpace(dossier?.RCCM)) ids.Add("RCCM " + dossier.RCCM.Trim());
                if (ids.Count > 0) gauche.AddElement(new Paragraph(string.Join(" | ", ids), FSousTitre));
                entete.AddCell(gauche);

                var droite = new PdfPCell { Border = Rectangle.NO_BORDER };
                droite.AddElement(new Paragraph(string.Format(Fr, "{0}, le {1:dd MMMM yyyy}", ville, dateLettre), FPetit) { Alignment = Element.ALIGN_RIGHT });
                droite.AddElement(new Paragraph("Réf. : " + p.Reference, FNote) { Alignment = Element.ALIGN_RIGHT });
                entete.AddCell(droite);
                doc.Add(entete);

                var trait = new PdfPTable(1) { WidthPercentage = 100f, SpacingBefore = 6f, SpacingAfter = 12f };
                trait.AddCell(new PdfPCell { Border = Rectangle.BOTTOM_BORDER, BorderWidthBottom = 2f, BorderColorBottom = Accent, FixedHeight = 4f });
                doc.Add(trait);

                // Destinataire
                var dest = new PdfPTable(2) { WidthPercentage = 100f, SpacingAfter = 12f };
                dest.SetWidths(new[] { 50f, 50f });
                dest.AddCell(new PdfPCell { Border = Rectangle.NO_BORDER });
                var cDest = new PdfPCell { BorderColor = Bordure, BackgroundColor = Trame, Padding = 8f };
                cDest.AddElement(new Paragraph("À l'attention de", FSousTitre));
                string nom = salarie != null ? salarie.FullName : (p.NomSalarie ?? p.EmployeeId);
                cDest.AddElement(new Paragraph("M. / Mme " + (nom ?? "").ToUpper(Fr), FObjet));
                cDest.AddElement(new Paragraph("Matricule : " + p.EmployeeId, FPetit));
                string fonction = salarie != null && !string.IsNullOrWhiteSpace(salarie.Fonction) ? salarie.Fonction : p.Fonction;
                if (!string.IsNullOrWhiteSpace(fonction)) cDest.AddElement(new Paragraph("Fonction : " + fonction.Trim(), FPetit));
                if (salarie != null && !string.IsNullOrWhiteSpace(salarie.Adresse)) cDest.AddElement(new Paragraph(salarie.Adresse.Trim(), FPetit));
                cDest.AddElement(new Paragraph(lettre == LettreDiscipline.DemandeExplication || lettre == LettreDiscipline.Convocation
                    ? "Remise en main propre contre décharge"
                    : (string.IsNullOrWhiteSpace(p.ModeNotification) ? "Remise en main propre contre décharge" : p.ModeNotification.Trim()), FNote));
                dest.AddCell(cDest);
                doc.Add(dest);

                // Objet
                var tObjet = new PdfPTable(1) { WidthPercentage = 100f, SpacingAfter = 12f };
                var cObjet = new PdfPCell { Border = Rectangle.LEFT_BORDER, BorderWidthLeft = 4f, BorderColorLeft = Accent, BackgroundColor = Trame, Padding = 6f };
                cObjet.AddElement(new Paragraph("OBJET : " + objet, FObjet));
                tObjet.AddCell(cObjet);
                doc.Add(tObjet);

                doc.Add(new Paragraph("Madame, Monsieur,", FCorpsGras) { SpacingAfter = 8f });
                foreach (var para in paragraphes) doc.Add(para);
                doc.Add(new Paragraph("Veuillez agréer, Madame, Monsieur, l'expression de nos salutations distinguées.", FCorps) { SpacingBefore = 4f, SpacingAfter = 18f });

                // Signatures : l'employeur, puis la décharge du salarié.
                var sign = new PdfPTable(2) { WidthPercentage = 100f };
                sign.SetWidths(new[] { 50f, 50f });
                var cSal = new PdfPCell { Border = Rectangle.BOX, BorderColor = Bordure, Padding = 8f };
                cSal.AddElement(new Paragraph("Reçu en main propre le : ____ / ____ / ________", FPetit));
                cSal.AddElement(new Paragraph("Signature du salarié", FNote));
                cSal.AddElement(new Paragraph("\n\n", FPetit));
                cSal.AddElement(new Paragraph("En cas de refus de signer : noms et signatures de deux témoins", FNote));
                sign.AddCell(cSal);
                var cEmp = new PdfPCell { Border = Rectangle.NO_BORDER, PaddingLeft = 20f };
                cEmp.AddElement(new Paragraph(string.IsNullOrWhiteSpace(prm.FonctionSignataire) ? "Pour la Direction" : prm.FonctionSignataire.Trim(), FCorpsGras) { Alignment = Element.ALIGN_RIGHT });
                cEmp.AddElement(new Paragraph("\n\n\n", FPetit));
                cEmp.AddElement(new Paragraph(string.IsNullOrWhiteSpace(prm.Signataire) ? "Nom, signature et cachet" : prm.Signataire.Trim(), FPetit) { Alignment = Element.ALIGN_RIGHT });
                sign.AddCell(cEmp);
                doc.Add(sign);

                doc.Add(new Paragraph("Document établi dans le cadre de la procédure disciplinaire " + p.Reference +
                                      " — conformément au Code du travail, au règlement intérieur et à la convention collective applicable.", FNote)
                { SpacingBefore = 14f });

                doc.Close();
            }
        }

        private static Paragraph P(string texte)
        {
            return new Paragraph(texte, FCorps) { Alignment = Element.ALIGN_JUSTIFIED, SpacingAfter = 7f, Leading = 13f };
        }

        private static Paragraph PMixte(string avant, string gras, string apres)
        {
            var p = new Paragraph { Alignment = Element.ALIGN_JUSTIFIED, SpacingAfter = 7f, Leading = 13f };
            p.Add(new Chunk(avant, FCorps));
            p.Add(new Chunk(gras, FCorpsGras));
            p.Add(new Chunk(apres, FCorps));
            return p;
        }

        private static Paragraph Encadre(string texte)
        {
            return new Paragraph(texte, FCorps) { Alignment = Element.ALIGN_JUSTIFIED, IndentationLeft = 18f, IndentationRight = 10f, SpacingAfter = 8f, Leading = 13f };
        }

        // ═══════════════════════════════════════════════════════════════════
        //  Registre Excel
        // ═══════════════════════════════════════════════════════════════════

        public void ExporterRegistre(
            string chemin,
            IEnumerable<ProcedureDisciplinaire> procedures,
            IEnumerable<LigneRegistreSanction> registre,
            IEnumerable<EcheanceDiscipline> echeances)
        {
            var dossier = DossierSession.Instance.CurrentDossier;
            string raison = dossier?.RaisonSociale ?? "";

            using (var wb = new XLWorkbook())
            {
                // 1. Registre des sanctions
                var ws = wb.Worksheets.Add("Registre des sanctions");
                Entete(ws, "Registre des sanctions disciplinaires notifiées", raison, 11);
                var titres = new[] { "Référence", "Matricule", "Salarié", "Date des faits", "Nature des faits", "Sanction", "Notifiée le", "Jours de mise à pied", "Effacement", "Recours", "Statut" };
                Titres(ws, 4, titres);
                int l = 5;
                foreach (var r in registre ?? Enumerable.Empty<LigneRegistreSanction>())
                {
                    ws.Cell(l, 1).Value = r.Reference;
                    ws.Cell(l, 2).Value = r.EmployeeId;
                    ws.Cell(l, 3).Value = r.NomSalarie;
                    ws.Cell(l, 4).Value = r.DateFaits; ws.Cell(l, 4).Style.DateFormat.Format = "dd/mm/yyyy";
                    ws.Cell(l, 5).Value = r.CategorieFaits;
                    ws.Cell(l, 6).Value = r.Sanction;
                    ws.Cell(l, 7).Value = r.DateNotification; ws.Cell(l, 7).Style.DateFormat.Format = "dd/mm/yyyy";
                    if (r.JoursMiseAPied > 0m) ws.Cell(l, 8).Value = r.JoursMiseAPied;
                    ws.Cell(l, 9).Value = r.EffacementLibelle;
                    ws.Cell(l, 10).Value = r.Recours ? "Oui" : "";
                    ws.Cell(l, 11).Value = r.EstEffacee ? "Effacée" : "En vigueur";
                    if (r.EstEffacee) ws.Range(l, 1, l, 11).Style.Font.FontColor = XLColor.FromArgb(121, 131, 126);
                    l++;
                }
                Finaliser(ws, 4, l - 1, titres.Length);

                // 2. Procédures
                var wp = wb.Worksheets.Add("Procédures");
                Entete(wp, "Procédures disciplinaires", raison, 14);
                var tp = new[] { "Référence", "Matricule", "Salarié", "Date des faits", "Connaissance", "Nature des faits", "Gravité", "Étape",
                                 "Demande d'explication", "Réponse", "Entretien", "Sanction", "Décision", "Notification" };
                Titres(wp, 4, tp);
                l = 5;
                foreach (var p in procedures ?? Enumerable.Empty<ProcedureDisciplinaire>())
                {
                    wp.Cell(l, 1).Value = p.Reference;
                    wp.Cell(l, 2).Value = p.EmployeeId;
                    wp.Cell(l, 3).Value = p.NomSalarie;
                    wp.Cell(l, 4).Value = p.DateFaits; wp.Cell(l, 4).Style.DateFormat.Format = "dd/mm/yyyy";
                    wp.Cell(l, 5).Value = p.DateConnaissance; wp.Cell(l, 5).Style.DateFormat.Format = "dd/mm/yyyy";
                    wp.Cell(l, 6).Value = p.CategorieFaits;
                    wp.Cell(l, 7).Value = p.GraviteLibelle;
                    wp.Cell(l, 8).Value = p.StatutLibelle;
                    DateOuVide(wp.Cell(l, 9), p.DateDemandeExplication);
                    DateOuVide(wp.Cell(l, 10), p.DateReponse);
                    DateOuVide(wp.Cell(l, 11), p.DateEntretien);
                    wp.Cell(l, 12).Value = p.SanctionLibelle;
                    DateOuVide(wp.Cell(l, 13), p.DateDecision);
                    DateOuVide(wp.Cell(l, 14), p.DateNotification);
                    l++;
                }
                Finaliser(wp, 4, l - 1, tp.Length);

                // 3. Échéancier
                var we = wb.Worksheets.Add("Échéancier");
                Entete(we, "Échéancier des procédures en cours", raison, 6);
                var te = new[] { "Date", "Délai", "Échéance", "Référence", "Matricule", "Salarié" };
                Titres(we, 4, te);
                l = 5;
                foreach (var e in echeances ?? Enumerable.Empty<EcheanceDiscipline>())
                {
                    we.Cell(l, 1).Value = e.Date; we.Cell(l, 1).Style.DateFormat.Format = "dd/mm/yyyy";
                    we.Cell(l, 2).Value = e.DelaiLibelle;
                    we.Cell(l, 3).Value = e.Libelle;
                    we.Cell(l, 4).Value = e.Reference;
                    we.Cell(l, 5).Value = e.EmployeeId;
                    we.Cell(l, 6).Value = e.NomSalarie;
                    if (e.EstDepassee) we.Range(l, 1, l, 6).Style.Font.FontColor = XLColor.FromArgb(198, 40, 40);
                    l++;
                }
                Finaliser(we, 4, l - 1, te.Length);

                wb.SaveAs(chemin);
            }
        }

        private static void DateOuVide(IXLCell cell, DateTime? d)
        {
            if (!d.HasValue) return;
            cell.Value = d.Value;
            cell.Style.DateFormat.Format = "dd/mm/yyyy";
        }

        private static void Entete(IXLWorksheet ws, string titre, string raison, int colonnes)
        {
            ws.Cell(1, 1).Value = titre;
            ws.Cell(1, 1).Style.Font.Bold = true;
            ws.Cell(1, 1).Style.Font.FontSize = 14;
            ws.Cell(1, 1).Style.Font.FontColor = XLColor.FromArgb(20, 103, 90);
            ws.Cell(2, 1).Value = string.Format(Fr, "{0} — édité le {1:dd/MM/yyyy à HH:mm}", raison, DateTime.Now);
            ws.Cell(2, 1).Style.Font.FontColor = XLColor.FromArgb(121, 131, 126);
            ws.Range(1, 1, 1, colonnes).Merge();
            ws.Range(2, 1, 2, colonnes).Merge();
        }

        private static void Titres(IXLWorksheet ws, int ligne, string[] titres)
        {
            for (int c = 0; c < titres.Length; c++)
                ws.Cell(ligne, c + 1).Value = titres[c];
            var r = ws.Range(ligne, 1, ligne, titres.Length);
            r.Style.Font.Bold = true;
            r.Style.Font.FontColor = XLColor.White;
            r.Style.Fill.BackgroundColor = XLColor.FromArgb(20, 103, 90);
            r.Style.Alignment.WrapText = true;
        }

        private static void Finaliser(IXLWorksheet ws, int ligneTitres, int derniere, int colonnes)
        {
            if (derniere >= ligneTitres)
            {
                var r = ws.Range(ligneTitres, 1, Math.Max(derniere, ligneTitres), colonnes);
                r.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
                r.Style.Border.InsideBorder = XLBorderStyleValues.Hair;
                r.Style.Border.OutsideBorderColor = XLColor.FromArgb(203, 209, 204);
                r.Style.Border.InsideBorderColor = XLColor.FromArgb(203, 209, 204);
            }
            if (derniere > ligneTitres) ws.Range(ligneTitres, 1, derniere, colonnes).SetAutoFilter();
            ws.SheetView.FreezeRows(ligneTitres);
            ws.Columns(1, colonnes).AdjustToContents(ligneTitres, Math.Max(derniere, ligneTitres));
            foreach (var col in ws.Columns(1, colonnes))
                if (col.Width > 45) col.Width = 45;
        }
    }
}
