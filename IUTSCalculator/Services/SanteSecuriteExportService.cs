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
    /// Documents du module RH-07 : fiche de déclaration d'accident du travail (les éléments
    /// à reporter sur l'imprimé de la CNSS), fiche de dotation en EPI à faire signer, et
    /// registre Excel (accidents, indemnités journalières, visites, EPI, échéancier).
    ///
    /// La déclaration officielle reste l'imprimé de la CNSS, en deux exemplaires, adressés
    /// à la CNSS et à l'inspection du travail : la fiche le prépare, elle ne le remplace pas.
    /// </summary>
    public class SanteSecuriteExportService
    {
        private static readonly CultureInfo Fr = LibellesSST.Fr;

        private static readonly BaseColor Encre = new BaseColor(23, 33, 31);
        private static readonly BaseColor Accent = new BaseColor(20, 103, 90);
        private static readonly BaseColor Trame = new BaseColor(245, 246, 245);
        private static readonly BaseColor Bordure = new BaseColor(203, 209, 204);
        private static readonly BaseColor Discret = new BaseColor(100, 116, 139);
        private static readonly BaseColor Alerte = new BaseColor(198, 40, 40);

        private static readonly Font FTitre = new Font(Font.FontFamily.HELVETICA, 13f, Font.BOLD, Accent);
        private static readonly Font FSousTitre = new Font(Font.FontFamily.HELVETICA, 8.5f, Font.NORMAL, Discret);
        private static readonly Font FObjet = new Font(Font.FontFamily.HELVETICA, 11f, Font.BOLD, Encre);
        private static readonly Font FSection = new Font(Font.FontFamily.HELVETICA, 9f, Font.BOLD, BaseColor.WHITE);
        private static readonly Font FLibelle = new Font(Font.FontFamily.HELVETICA, 7.5f, Font.NORMAL, Discret);
        private static readonly Font FValeur = new Font(Font.FontFamily.HELVETICA, 9.5f, Font.NORMAL, Encre);
        private static readonly Font FCorps = new Font(Font.FontFamily.HELVETICA, 9.5f, Font.NORMAL, Encre);
        private static readonly Font FCorpsGras = new Font(Font.FontFamily.HELVETICA, 9.5f, Font.BOLD, Encre);
        private static readonly Font FAlerte = new Font(Font.FontFamily.HELVETICA, 9.5f, Font.BOLD, Alerte);
        private static readonly Font FNote = new Font(Font.FontFamily.HELVETICA, 7.5f, Font.ITALIC, Discret);
        private static readonly Font FPetit = new Font(Font.FontFamily.HELVETICA, 8.5f, Font.NORMAL, Encre);
        private static readonly Font FTableTitre = new Font(Font.FontFamily.HELVETICA, 8f, Font.BOLD, BaseColor.WHITE);

        /// <summary>Ce qui manque pour produire la fiche de déclaration, ou null.</summary>
        public static string PrerequisFiche(AccidentTravail a)
        {
            if (a == null) return "Aucun accident sélectionné.";
            if (a.Id <= 0) return "Enregistrez d'abord l'accident.";
            if (string.IsNullOrWhiteSpace(a.EmployeeId)) return "Choisissez la victime.";
            return null;
        }

        // ═══════════════════════════════════════════════════════════════════
        //  Fiche de déclaration d'accident du travail
        // ═══════════════════════════════════════════════════════════════════

        public void GenererFicheAccident(AccidentTravail a, Employee salarie, ParametresSST prm, ISet<DateTime> feries, string chemin)
        {
            var manque = PrerequisFiche(a);
            if (manque != null) throw new InvalidOperationException(manque);
            prm = prm ?? ParametresSST.Defaut();
            var dossier = DossierSession.Instance.CurrentDossier;
            var limite = ControleSanteSecurite.DateLimiteDeclaration(a, prm, feries);

            using (var fs = new FileStream(chemin, FileMode.Create, FileAccess.Write, FileShare.None))
            using (var doc = new Document(PageSize.A4, 40f, 40f, 28f, 28f))
            {
                PdfWriter.GetInstance(doc, fs);
                doc.Open();

                EnTete(doc, dossier, "Réf. interne : " + a.Reference, "Fiche établie le " + DateTime.Today.ToString("dd/MM/yyyy", Fr));

                string titre;
                switch (a.Nature)
                {
                    case NatureAccident.Trajet: titre = "DÉCLARATION D'ACCIDENT DE TRAJET"; break;
                    case NatureAccident.MaladieProfessionnelle: titre = "DÉCLARATION DE MALADIE PROFESSIONNELLE"; break;
                    default: titre = "DÉCLARATION D'ACCIDENT DU TRAVAIL"; break;
                }
                var tObjet = new PdfPTable(1) { WidthPercentage = 100f, SpacingAfter = 5f };
                var cObjet = new PdfPCell { Border = Rectangle.LEFT_BORDER, BorderWidthLeft = 4f, BorderColorLeft = Accent, BackgroundColor = Trame, Padding = 5f };
                cObjet.AddElement(new Paragraph(titre, FObjet));
                cObjet.AddElement(new Paragraph("Fiche préparatoire : éléments à reporter sur l'imprimé de déclaration de la CNSS, établi en deux exemplaires.", FSousTitre));
                tObjet.AddCell(cObjet);
                doc.Add(tObjet);

                // Délai
                var delai = new Paragraph { SpacingAfter = 4f, Leading = 12f };
                bool enRetard = (!a.DateDeclarationCnss.HasValue || !a.DateDeclarationInspection.HasValue) && DateTime.Today > limite;
                delai.Add(new Chunk("Délai : ", FCorpsGras));
                delai.Add(new Chunk(string.Format(Fr, "déclaration à la CNSS et à l'inspection du travail au plus tard le {0:dddd dd MMMM yyyy} (48 heures ouvrables après l'accident).", limite),
                    enRetard ? FAlerte : FCorps));
                doc.Add(delai);

                // 1. Employeur
                Section(doc, "1. L'EMPLOYEUR");
                var t1 = Grille();
                Champ(t1, "Raison sociale", dossier?.RaisonSociale);
                Champ(t1, "N° employeur CNSS", dossier?.NumeroEmployeurCnss);
                Champ(t1, "IFU — RCCM", Joindre(" — ", dossier?.IFU, dossier?.RCCM));
                Champ(t1, "Adresse — téléphone", Joindre(" — ", Joindre(", ", dossier?.Adresse, dossier?.Ville), dossier?.Telephone));
                doc.Add(t1);

                // 2. Victime
                Section(doc, "2. LA VICTIME");
                var t2 = Grille();
                Champ(t2, "Nom et prénoms", salarie != null ? salarie.FullName : (a.NomSalarie ?? a.EmployeeId));
                Champ(t2, "Matricule", a.EmployeeId);
                Champ(t2, "N° d'immatriculation CNSS", salarie?.NumeroCnss);
                Champ(t2, "Date et lieu de naissance", salarie == null ? null
                    : Joindre(" à ", salarie.DateNaissance.HasValue ? salarie.DateNaissance.Value.ToString("dd/MM/yyyy", Fr) : null, salarie.LieuNaissance));
                Champ(t2, "Sexe — nationalité", salarie == null ? null : Joindre(" — ", salarie.Sexe, salarie.Nationalite));
                Champ(t2, "Emploi occupé", salarie?.Fonction);
                Champ(t2, "Service", salarie?.Service);
                Champ(t2, "Date d'embauche", salarie != null && salarie.DateEntree > new DateTime(1900, 1, 1) ? salarie.DateEntree.ToString("dd/MM/yyyy", Fr) : null);
                Champ(t2, "Adresse", salarie?.Adresse);
                Champ(t2, "Rémunération journalière retenue", a.SalaireJournalier > 0m ? a.SalaireJournalier.ToString("N0", Fr) + " FCFA" : null);
                doc.Add(t2);

                // 3. Accident
                Section(doc, "3. " + (a.Nature == NatureAccident.MaladieProfessionnelle ? "LA MALADIE PROFESSIONNELLE" : "L'ACCIDENT"));
                var t3 = Grille();
                Champ(t3, "Nature", a.NatureLibelle);
                Champ(t3, a.Nature == NatureAccident.MaladieProfessionnelle ? "Date de constatation" : "Date et heure",
                    Joindre(" à ", a.DateAccident.ToString("dddd dd MMMM yyyy", Fr), a.Heure));
                Champ(t3, "Lieu", a.Lieu);
                Champ(t3, "Témoins", a.Temoins);
                Champ(t3, "Circonstances détaillées", a.Circonstances, 2);
                Champ(t3, "Siège des lésions", a.SiegeLesion);
                Champ(t3, "Nature des lésions", a.NatureLesion);
                Champ(t3, "Premiers soins / structure de prise en charge", a.PremiersSoins, 2);
                doc.Add(t3);

                // 4. Conséquences
                Section(doc, "4. CONSÉQUENCES");
                var t4 = Grille();
                Champ(t4, "Arrêt de travail", a.Mortel ? "Accident mortel" : a.AvecArret ? "Oui" : "Non");
                Champ(t4, "Période d'arrêt", a.AvecArret && !a.Mortel ? a.ArretLibelle : null);
                Champ(t4, "Reprise du travail le", a.DateReprise.HasValue ? a.DateReprise.Value.ToString("dd/MM/yyyy", Fr) : null);
                Champ(t4, "Taux d'incapacité permanente", a.TauxIpp > 0m ? a.TauxIpp.ToString("0.##", Fr) + " %" : null);
                if (a.AvecArret && !a.Mortel && a.DebutArret.HasValue && a.SalaireJournalier > 0m)
                {
                    int jours = ControleSanteSecurite.JoursIndemnises(a, a.DateAccident, a.FinArret ?? DateTime.Today, DateTime.Today);
                    if (jours > 0)
                        Champ(t4, "Indemnités journalières estimées (CNSS)", string.Format(Fr,
                            "{0} jour(s) × {1:N0} × {2:0.##} % = {3:N0} FCFA{4} — salaire maintenu en entier par l'employeur, indemnités à réclamer à la CNSS",
                            jours, a.SalaireJournalier, prm.TauxIndemniteJournaliere * 100m,
                            ControleSanteSecurite.MontantCnss(jours, a.SalaireJournalier, prm), a.FinArret.HasValue ? "" : " à ce jour"), 2);
                }
                doc.Add(t4);

                // 5. Déclarations
                Section(doc, "5. DÉCLARATIONS");
                var t5 = Grille();
                Champ(t5, "Déclaré à la CNSS le", a.DateDeclarationCnss.HasValue ? a.DateDeclarationCnss.Value.ToString("dd/MM/yyyy", Fr) : "____ / ____ / ________");
                Champ(t5, "Référence CNSS", a.ReferenceCnss);
                Champ(t5, "Déclaré à l'inspection du travail le", a.DateDeclarationInspection.HasValue ? a.DateDeclarationInspection.Value.ToString("dd/MM/yyyy", Fr) : "____ / ____ / ________");
                Champ(t5, "Date limite", limite.ToString("dddd dd/MM/yyyy", Fr));
                doc.Add(t5);

                var rappel = new Paragraph(
                    "Rappels : la déclaration se fait sur l'imprimé de la CNSS, en deux exemplaires, adressés à la CNSS et à l'inspection du travail " +
                    "dans les 48 heures ouvrables ; joignez les pièces demandées par la CNSS (certificat médical notamment). " +
                    "Le jour de l'accident est entièrement à la charge de l'employeur ; à partir du lendemain, la CNSS verse une indemnité journalière " +
                    "égale aux 2/3 de la rémunération journalière, pour chaque jour d'incapacité, ouvrable ou non. Les premiers soins sont à la charge de l'employeur. " +
                    "La victime peut elle-même déclarer l'accident pendant deux ans.", FNote) { SpacingBefore = 6f, SpacingAfter = 10f, Leading = 9.5f };
                doc.Add(rappel);

                var sign = new PdfPTable(2) { WidthPercentage = 100f };
                sign.SetWidths(new[] { 50f, 50f });
                var cG = new PdfPCell { Border = Rectangle.NO_BORDER };
                cG.AddElement(new Paragraph(string.Format(Fr, "Fait à {0}, le ____ / ____ / ________",
                    string.IsNullOrWhiteSpace(dossier?.Ville) ? "________________" : dossier.Ville.Trim()), FPetit));
                sign.AddCell(cG);
                // Signature dans l'espace libre sous cette ligne : la fiche tient sur une page.
                var cD = new PdfPCell { Border = Rectangle.NO_BORDER };
                var pD = new Paragraph { Alignment = Element.ALIGN_RIGHT };
                pD.Add(new Chunk("L'employeur ", FCorpsGras));
                pD.Add(new Chunk("(nom, qualité, signature et cachet)", FNote));
                cD.AddElement(pD);
                sign.AddCell(cD);
                doc.Add(sign);

                doc.Close();
            }
        }

        // ═══════════════════════════════════════════════════════════════════
        //  Fiche de dotation en EPI
        // ═══════════════════════════════════════════════════════════════════

        public void GenererDechargeEpi(Employee salarie, string employeeId, string nomSalarie, IEnumerable<DotationEPI> dotations, string chemin)
        {
            var lignes = (dotations ?? Enumerable.Empty<DotationEPI>()).OrderBy(d => d.DateRemise).ThenBy(d => d.TypeEpiLibelle).ToList();
            if (lignes.Count == 0) throw new InvalidOperationException("Aucun équipement en service pour ce salarié.");
            var dossier = DossierSession.Instance.CurrentDossier;
            string nom = salarie != null ? salarie.FullName : (nomSalarie ?? employeeId);

            using (var fs = new FileStream(chemin, FileMode.Create, FileAccess.Write, FileShare.None))
            using (var doc = new Document(PageSize.A4, 45f, 45f, 36f, 36f))
            {
                PdfWriter.GetInstance(doc, fs);
                doc.Open();

                EnTete(doc, dossier, null, "Édition du " + DateTime.Today.ToString("dd/MM/yyyy", Fr));

                var tObjet = new PdfPTable(1) { WidthPercentage = 100f, SpacingAfter = 10f };
                var cObjet = new PdfPCell { Border = Rectangle.LEFT_BORDER, BorderWidthLeft = 4f, BorderColorLeft = Accent, BackgroundColor = Trame, Padding = 7f };
                cObjet.AddElement(new Paragraph("FICHE DE DOTATION EN ÉQUIPEMENTS DE PROTECTION INDIVIDUELLE", FObjet));
                cObjet.AddElement(new Paragraph("Remise contre décharge — équipements fournis gratuitement par l'employeur", FSousTitre));
                tObjet.AddCell(cObjet);
                doc.Add(tObjet);

                var t = Grille();
                Champ(t, "Salarié", nom);
                Champ(t, "Matricule", employeeId);
                Champ(t, "Emploi occupé", salarie?.Fonction);
                Champ(t, "Service", salarie?.Service);
                doc.Add(t);

                var tab = new PdfPTable(6) { WidthPercentage = 100f, SpacingBefore = 10f, SpacingAfter = 10f, HeaderRows = 1 };
                tab.SetWidths(new[] { 34f, 8f, 10f, 14f, 16f, 18f });
                foreach (var titre in new[] { "Équipement", "Qté", "Taille", "Remis le", "À renouveler le", "Émargement" })
                    tab.AddCell(new PdfPCell(new Phrase(titre, FTableTitre)) { BackgroundColor = Accent, BorderColor = Accent, Padding = 4f });
                foreach (var d in lignes)
                {
                    tab.AddCell(Cellule(d.TypeEpiLibelle));
                    tab.AddCell(Cellule(d.Quantite.ToString(Fr), Element.ALIGN_RIGHT));
                    tab.AddCell(Cellule(d.Taille));
                    tab.AddCell(Cellule(d.DateRemise.ToString("dd/MM/yyyy", Fr)));
                    tab.AddCell(Cellule(d.DateRenouvellement.HasValue ? d.DateRenouvellement.Value.ToString("dd/MM/yyyy", Fr) : "—"));
                    tab.AddCell(new PdfPCell(new Phrase(" ", FPetit)) { BorderColor = Bordure, MinimumHeight = 20f });
                }
                doc.Add(tab);

                doc.Add(new Paragraph(
                    "Je soussigné(e) " + nom + " reconnais avoir reçu les équipements de protection individuelle désignés ci-dessus, " +
                    "en bon état. Je m'engage à les porter pendant le travail conformément aux consignes de sécurité, à en prendre soin, " +
                    "à signaler sans délai toute perte ou détérioration et à les restituer lors de leur renouvellement ou de mon départ de l'entreprise.",
                    FCorps) { Alignment = Element.ALIGN_JUSTIFIED, Leading = 13f, SpacingAfter = 18f });

                var sign = new PdfPTable(2) { WidthPercentage = 100f };
                sign.SetWidths(new[] { 50f, 50f });
                var cSal = new PdfPCell { Border = Rectangle.BOX, BorderColor = Bordure, Padding = 8f };
                cSal.AddElement(new Paragraph("Le salarié — « Lu et approuvé », le ____ / ____ / ________", FPetit));
                cSal.AddElement(new Paragraph("\n\n\n", FPetit));
                cSal.AddElement(new Paragraph("Signature", FNote));
                sign.AddCell(cSal);
                var cEmp = new PdfPCell { Border = Rectangle.NO_BORDER, PaddingLeft = 20f };
                cEmp.AddElement(new Paragraph("Pour l'employeur", FCorpsGras) { Alignment = Element.ALIGN_RIGHT });
                cEmp.AddElement(new Paragraph("\n\n\n", FPetit));
                cEmp.AddElement(new Paragraph("Nom, signature et cachet", FNote) { Alignment = Element.ALIGN_RIGHT });
                sign.AddCell(cEmp);
                doc.Add(sign);

                doc.Close();
            }
        }

        // ═══════════════════════════════════════════════════════════════════
        //  Mise en page commune
        // ═══════════════════════════════════════════════════════════════════

        private static void EnTete(Document doc, Dossier dossier, string reference, string date)
        {
            string raison = string.IsNullOrWhiteSpace(dossier?.RaisonSociale) ? "L'EMPLOYEUR" : dossier.RaisonSociale.Trim();
            var entete = new PdfPTable(2) { WidthPercentage = 100f };
            entete.SetWidths(new[] { 62f, 38f });
            var gauche = new PdfPCell { Border = Rectangle.NO_BORDER };
            gauche.AddElement(new Paragraph(raison.ToUpper(Fr), FTitre));
            var coord = new List<string>();
            if (!string.IsNullOrWhiteSpace(dossier?.Adresse)) coord.Add(dossier.Adresse.Trim());
            if (!string.IsNullOrWhiteSpace(dossier?.Telephone)) coord.Add("Tél. " + dossier.Telephone.Trim());
            if (coord.Count > 0) gauche.AddElement(new Paragraph(string.Join(" | ", coord), FSousTitre));
            var ids = new List<string>();
            if (!string.IsNullOrWhiteSpace(dossier?.NumeroEmployeurCnss)) ids.Add("N° CNSS " + dossier.NumeroEmployeurCnss.Trim());
            if (!string.IsNullOrWhiteSpace(dossier?.IFU)) ids.Add("IFU " + dossier.IFU.Trim());
            if (ids.Count > 0) gauche.AddElement(new Paragraph(string.Join(" | ", ids), FSousTitre));
            entete.AddCell(gauche);

            var droite = new PdfPCell { Border = Rectangle.NO_BORDER };
            droite.AddElement(new Paragraph(date, FPetit) { Alignment = Element.ALIGN_RIGHT });
            if (!string.IsNullOrEmpty(reference)) droite.AddElement(new Paragraph(reference, FNote) { Alignment = Element.ALIGN_RIGHT });
            entete.AddCell(droite);
            doc.Add(entete);

            var trait = new PdfPTable(1) { WidthPercentage = 100f, SpacingBefore = 4f, SpacingAfter = 10f };
            trait.AddCell(new PdfPCell { Border = Rectangle.BOTTOM_BORDER, BorderWidthBottom = 2f, BorderColorBottom = Accent, FixedHeight = 4f });
            doc.Add(trait);
        }

        private static void Section(Document doc, string titre)
        {
            var t = new PdfPTable(1) { WidthPercentage = 100f, SpacingBefore = 5f };
            t.AddCell(new PdfPCell(new Phrase(titre, FSection)) { BackgroundColor = Accent, BorderColor = Accent, PaddingTop = 2f, PaddingBottom = 3.5f, PaddingLeft = 6f });
            doc.Add(t);
        }

        /// <summary>Grille de champs « libellé au-dessus, valeur dessous », deux par ligne.</summary>
        private static PdfPTable Grille()
        {
            var t = new PdfPTable(2) { WidthPercentage = 100f };
            t.SetWidths(new[] { 50f, 50f });
            return t;
        }

        private static void Champ(PdfPTable t, string libelle, string valeur, int colonnes = 1)
        {
            var c = new PdfPCell { BorderColor = Bordure, PaddingLeft = 4f, PaddingRight = 4f, PaddingTop = 1f, PaddingBottom = 4f, Colspan = colonnes };
            c.AddElement(new Paragraph(libelle, FLibelle) { Leading = 9f });
            c.AddElement(new Paragraph(string.IsNullOrWhiteSpace(valeur) ? " " : valeur.Trim(), FValeur) { Leading = 11.5f });
            t.AddCell(c);
        }

        private static PdfPCell Cellule(string texte, int alignement = Element.ALIGN_LEFT)
        {
            return new PdfPCell(new Phrase(string.IsNullOrWhiteSpace(texte) ? "" : texte, FPetit))
            {
                BorderColor = Bordure, Padding = 4f, HorizontalAlignment = alignement, MinimumHeight = 20f, VerticalAlignment = Element.ALIGN_MIDDLE
            };
        }

        private static string Joindre(string separateur, params string[] parties)
        {
            return string.Join(separateur, parties.Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => p.Trim()));
        }

        // ═══════════════════════════════════════════════════════════════════
        //  Registre Excel
        // ═══════════════════════════════════════════════════════════════════

        public void ExporterRegistre(
            string chemin,
            IEnumerable<AccidentTravail> accidents,
            IEnumerable<LigneIndemniteJournaliere> indemnites, int annee,
            IEnumerable<VisiteMedicale> visites,
            IEnumerable<DotationEPI> dotations,
            IEnumerable<EcheanceSST> echeances,
            IEnumerable<SuiviRemboursementIJ> remboursements = null)
        {
            var dossier = DossierSession.Instance.CurrentDossier;
            string raison = dossier?.RaisonSociale ?? "";

            using (var wb = new XLWorkbook())
            {
                // 1. Registre des accidents du travail
                var wa = wb.Worksheets.Add("Accidents du travail");
                Entete(wa, "Registre des accidents du travail et maladies professionnelles", raison, 19);
                var ta = new[] { "Référence", "Matricule", "Salarié", "Nature", "Date", "Heure", "Lieu", "Siège des lésions", "Nature des lésions",
                                 "Arrêt", "Début d'arrêt", "Fin d'arrêt", "Reprise", "Jours d'arrêt", "Mortel", "Déclaré CNSS le", "Déclaré inspection le",
                                 "Réf. CNSS", "Taux IPP (%)" };
                Titres(wa, 4, ta);
                int l = 5;
                foreach (var a in accidents ?? Enumerable.Empty<AccidentTravail>())
                {
                    wa.Cell(l, 1).Value = a.Reference;
                    wa.Cell(l, 2).Value = a.EmployeeId;
                    wa.Cell(l, 3).Value = a.NomSalarie;
                    wa.Cell(l, 4).Value = a.NatureLibelle;
                    DateOuVide(wa.Cell(l, 5), a.DateAccident);
                    wa.Cell(l, 6).Value = a.Heure;
                    wa.Cell(l, 7).Value = a.Lieu;
                    wa.Cell(l, 8).Value = a.SiegeLesion;
                    wa.Cell(l, 9).Value = a.NatureLesion;
                    wa.Cell(l, 10).Value = a.AvecArret ? "Oui" : "Non";
                    DateOuVide(wa.Cell(l, 11), a.DebutArret);
                    DateOuVide(wa.Cell(l, 12), a.FinArret);
                    DateOuVide(wa.Cell(l, 13), a.DateReprise);
                    if (a.AvecArret && a.DebutArret.HasValue && a.FinArret.HasValue && a.FinArret.Value >= a.DebutArret.Value)
                        wa.Cell(l, 14).Value = (a.FinArret.Value - a.DebutArret.Value).Days + 1;
                    wa.Cell(l, 15).Value = a.Mortel ? "Oui" : "";
                    DateOuVide(wa.Cell(l, 16), a.DateDeclarationCnss);
                    DateOuVide(wa.Cell(l, 17), a.DateDeclarationInspection);
                    wa.Cell(l, 18).Value = a.ReferenceCnss;
                    if (a.TauxIpp > 0m) wa.Cell(l, 19).Value = a.TauxIpp;
                    l++;
                }
                Finaliser(wa, 4, l - 1, ta.Length);

                // 2. Indemnités journalières à réclamer
                var wi = wb.Worksheets.Add("Indemnités journalières");
                Entete(wi, string.Format(Fr, "Indemnités journalières à réclamer à la CNSS — {0}", annee), raison, 9);
                var ti = new[] { "Mois", "Référence", "Matricule", "Salarié", "Période", "Jours", "Rémunération journalière", "IJ CNSS (2/3)", "Part employeur (1/3)" };
                Titres(wi, 4, ti);
                l = 5;
                decimal totalCnss = 0m, totalEmployeur = 0m;
                int totalJours = 0;
                foreach (var i in indemnites ?? Enumerable.Empty<LigneIndemniteJournaliere>())
                {
                    wi.Cell(l, 1).Value = i.MoisLibelle;
                    wi.Cell(l, 2).Value = i.Reference;
                    wi.Cell(l, 3).Value = i.EmployeeId;
                    wi.Cell(l, 4).Value = i.NomSalarie;
                    wi.Cell(l, 5).Value = i.PeriodeLibelle;
                    wi.Cell(l, 6).Value = i.Jours;
                    Montant(wi.Cell(l, 7), i.SalaireJournalier);
                    Montant(wi.Cell(l, 8), i.MontantCnss);
                    Montant(wi.Cell(l, 9), i.PartEmployeur);
                    totalJours += i.Jours;
                    totalCnss += i.MontantCnss;
                    totalEmployeur += i.PartEmployeur;
                    l++;
                }
                if (l > 5)
                {
                    wi.Cell(l, 1).Value = "TOTAL";
                    wi.Cell(l, 6).Value = totalJours;
                    Montant(wi.Cell(l, 8), totalCnss);
                    Montant(wi.Cell(l, 9), totalEmployeur);
                    wi.Range(l, 1, l, ti.Length).Style.Font.Bold = true;
                    wi.Range(l, 1, l, ti.Length).Style.Fill.BackgroundColor = XLColor.FromArgb(245, 246, 245);
                }
                Finaliser(wi, 4, l > 5 ? l - 1 : 4, ti.Length);

                // 2 bis. Suivi des remboursements de la CNSS, arrêt par arrêt (toutes années)
                var wr = wb.Worksheets.Add("Remboursements CNSS");
                Entete(wr, "Suivi du remboursement des indemnités journalières par la CNSS", raison, 13);
                var tr = new[] { "Référence", "Matricule", "Salarié", "Accident du", "Jours indemnisés", "Période indemnisée",
                                 "Montant réclamé", "Dossier déposé le", "Montant remboursé", "Remboursé le", "Restant dû", "Statut", "Arrêt en cours" };
                Titres(wr, 4, tr);
                l = 5;
                decimal totalReclame = 0m, totalRembourse = 0m, totalRestant = 0m;
                foreach (var r in remboursements ?? Enumerable.Empty<SuiviRemboursementIJ>())
                {
                    wr.Cell(l, 1).Value = r.Reference;
                    wr.Cell(l, 2).Value = r.EmployeeId;
                    wr.Cell(l, 3).Value = r.NomSalarie;
                    DateOuVide(wr.Cell(l, 4), r.DateAccident);
                    wr.Cell(l, 5).Value = r.Jours;
                    wr.Cell(l, 6).Value = string.Format(Fr, "{0:dd/MM/yyyy} au {1:dd/MM/yyyy}", r.Du, r.Au);
                    Montant(wr.Cell(l, 7), r.MontantReclame);
                    DateOuVide(wr.Cell(l, 8), r.DateDepot);
                    Montant(wr.Cell(l, 9), r.MontantRembourse);
                    DateOuVide(wr.Cell(l, 10), r.DateRemboursement);
                    Montant(wr.Cell(l, 11), r.RestantDu);
                    wr.Cell(l, 12).Value = r.StatutLibelle;
                    wr.Cell(l, 13).Value = r.EnCours ? "Oui (compté jusqu'à ce jour)" : "";
                    if (r.RestantDu > 0m) wr.Cell(l, 11).Style.Font.FontColor = XLColor.FromArgb(198, 40, 40);
                    totalReclame += r.MontantReclame;
                    totalRembourse += r.MontantRembourse;
                    totalRestant += r.RestantDu;
                    l++;
                }
                if (l > 5)
                {
                    wr.Cell(l, 1).Value = "TOTAL";
                    Montant(wr.Cell(l, 7), totalReclame);
                    Montant(wr.Cell(l, 9), totalRembourse);
                    Montant(wr.Cell(l, 11), totalRestant);
                    wr.Range(l, 1, l, tr.Length).Style.Font.Bold = true;
                    wr.Range(l, 1, l, tr.Length).Style.Fill.BackgroundColor = XLColor.FromArgb(245, 246, 245);
                }
                Finaliser(wr, 4, l > 5 ? l - 1 : 4, tr.Length);

                // 3. Visites médicales
                var wv = wb.Worksheets.Add("Visites médicales");
                Entete(wv, "Visites médicales", raison, 9);
                var tv = new[] { "Matricule", "Salarié", "Visite", "Prévue le", "Passée le", "Médecin / service", "Avis", "Réserves / aménagements", "Prochaine visite fixée" };
                Titres(wv, 4, tv);
                l = 5;
                foreach (var v in visites ?? Enumerable.Empty<VisiteMedicale>())
                {
                    wv.Cell(l, 1).Value = v.EmployeeId;
                    wv.Cell(l, 2).Value = v.NomSalarie;
                    wv.Cell(l, 3).Value = v.TypeLibelle;
                    DateOuVide(wv.Cell(l, 4), v.DatePrevue);
                    DateOuVide(wv.Cell(l, 5), v.DateVisite);
                    wv.Cell(l, 6).Value = v.Medecin;
                    wv.Cell(l, 7).Value = v.AptitudeLibelle;
                    wv.Cell(l, 8).Value = v.Restrictions;
                    DateOuVide(wv.Cell(l, 9), v.ProchaineVisite);
                    l++;
                }
                Finaliser(wv, 4, l - 1, tv.Length);

                // 4. EPI remis
                var we = wb.Worksheets.Add("EPI remis");
                Entete(we, "Équipements de protection individuelle remis", raison, 9);
                var tepi = new[] { "Matricule", "Salarié", "Équipement", "Qté", "Taille", "Remis le", "À renouveler le", "État", "Rendu / constaté le" };
                Titres(we, 4, tepi);
                l = 5;
                foreach (var d in dotations ?? Enumerable.Empty<DotationEPI>())
                {
                    we.Cell(l, 1).Value = d.EmployeeId;
                    we.Cell(l, 2).Value = d.NomSalarie;
                    we.Cell(l, 3).Value = d.TypeEpiLibelle;
                    we.Cell(l, 4).Value = d.Quantite;
                    we.Cell(l, 5).Value = d.Taille;
                    DateOuVide(we.Cell(l, 6), d.DateRemise);
                    DateOuVide(we.Cell(l, 7), d.DateRenouvellement);
                    we.Cell(l, 8).Value = d.EtatLibelle;
                    DateOuVide(we.Cell(l, 9), d.DateRetour);
                    if (d.Etat != EtatDotation.EnService) we.Range(l, 1, l, tepi.Length).Style.Font.FontColor = XLColor.FromArgb(121, 131, 126);
                    l++;
                }
                Finaliser(we, 4, l - 1, tepi.Length);

                // 5. Échéancier
                var wc = wb.Worksheets.Add("Échéancier");
                Entete(wc, "Échéancier santé & sécurité", raison, 6);
                var tc = new[] { "Date", "Délai", "Catégorie", "Échéance", "Matricule", "Salarié" };
                Titres(wc, 4, tc);
                l = 5;
                foreach (var e in echeances ?? Enumerable.Empty<EcheanceSST>())
                {
                    DateOuVide(wc.Cell(l, 1), e.Date);
                    wc.Cell(l, 2).Value = e.DelaiLibelle;
                    wc.Cell(l, 3).Value = e.Categorie;
                    wc.Cell(l, 4).Value = e.Libelle;
                    wc.Cell(l, 5).Value = e.EmployeeId;
                    wc.Cell(l, 6).Value = e.NomSalarie;
                    if (e.EstDepassee) wc.Range(l, 1, l, tc.Length).Style.Font.FontColor = XLColor.FromArgb(198, 40, 40);
                    l++;
                }
                Finaliser(wc, 4, l - 1, tc.Length);

                wb.SaveAs(chemin);
            }
        }

        private static void Montant(IXLCell cell, decimal valeur)
        {
            cell.Value = valeur;
            cell.Style.NumberFormat.Format = "# ##0";
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
