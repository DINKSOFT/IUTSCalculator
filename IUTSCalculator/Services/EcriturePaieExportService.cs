using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using ClosedXML.Excel;
using IUTSCalculator.Models;

namespace IUTSCalculator.Services
{
    /// <summary>
    /// Service d'export de la pièce de paie SYSCOHADA en classeur Excel, en fichier texte
    /// générique, et au format d'import Sage 100c Comptabilité (module CPT-01).
    /// </summary>
    public class EcriturePaieExportService
    {
        private static readonly XLColor CouleurEncre = XLColor.FromArgb(23, 33, 31);
        private static readonly XLColor CouleurAccent = XLColor.FromArgb(20, 103, 90);
        private static readonly XLColor CouleurTrame = XLColor.FromArgb(250, 251, 250);
        private static readonly XLColor CouleurAlerte = XLColor.FromArgb(156, 47, 40);
        private static readonly XLColor CouleurBordure = XLColor.FromArgb(224, 228, 224);

        private const string FormatMontant = "#,##0";

        /// <summary>
        /// Exporte la pièce comptable au format Excel (ClosedXML).
        /// </summary>
        public string ExporterExcel(PieceComptable piece, string cheminFichier)
        {
            if (piece == null) throw new ArgumentNullException(nameof(piece));
            if (string.IsNullOrWhiteSpace(cheminFichier))
                throw new ArgumentException("Chemin de fichier manquant.", nameof(cheminFichier));

            using (var classeur = new XLWorkbook())
            {
                var f = classeur.Worksheets.Add("Écritures de paie");
                f.Style.Font.FontName = "Segoe UI";
                f.Style.Font.FontSize = 10;

                int l = 1;

                // ── Titre ────────────────────────────────────────────────────
                // Titre tiré des paramètres comptables : « JOURNAL PAIE — JOURNAL DE PAIE ».
                f.Cell(l, 1).Value = piece.EnteteJournal;
                f.Cell(l, 1).Style.Font.Bold = true;
                f.Cell(l, 1).Style.Font.FontSize = 14;
                f.Cell(l, 1).Style.Font.FontColor = CouleurAccent;
                f.Range(l, 1, l, 6).Merge();

                if (piece.EstBrouillon)
                {
                    f.Cell(l, 7).Value = "BROUILLON";
                    f.Cell(l, 7).Style.Font.Bold = true;
                    f.Cell(l, 7).Style.Font.FontSize = 12;
                    f.Cell(l, 7).Style.Font.FontColor = CouleurAlerte;
                    f.Cell(l, 7).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;
                    f.Range(l, 7, l, 9).Merge();
                }

                l++;

                f.Cell(l, 1).Value = string.Format("{0} — {1}", piece.RaisonSociale ?? "", piece.PeriodeLibelle);
                f.Cell(l, 1).Style.Font.FontColor = XLColor.FromArgb(110, 118, 132);
                f.Range(l, 1, l, 6).Merge();
                l++;

                if (!string.IsNullOrWhiteSpace(piece.Ifu))
                {
                    f.Cell(l, 1).Value = string.Format("IFU : {0} | Pièce N° : {1}", piece.Ifu, piece.NumeroPiece);
                    f.Cell(l, 1).Style.Font.FontColor = XLColor.FromArgb(110, 118, 132);
                    f.Cell(l, 1).Style.Font.FontSize = 9;
                    f.Range(l, 1, l, 6).Merge();
                    l++;
                }

                f.Cell(l, 1).Value = string.Format("Date de pièce : {0:dd/MM/yyyy} | Édité le {1:dd/MM/yyyy à HH:mm}",
                    piece.DatePiece, piece.DateGeneration);
                f.Cell(l, 1).Style.Font.FontColor = XLColor.FromArgb(150, 155, 165);
                f.Cell(l, 1).Style.Font.FontSize = 9;
                f.Range(l, 1, l, 6).Merge();
                l += 2;

                // ── En-tête du tableau ───────────────────────────────────────
                int entete = l;
                f.Cell(l, 1).Value = "N°";
                f.Cell(l, 2).Value = "Date";
                f.Cell(l, 3).Value = "Journal";
                f.Cell(l, 4).Value = "Pièce";
                f.Cell(l, 5).Value = "Compte";
                f.Cell(l, 6).Value = "Intitulé du compte";
                f.Cell(l, 7).Value = "Libellé de l'écriture";
                f.Cell(l, 8).Value = "Débit";
                f.Cell(l, 9).Value = "Crédit";

                var rangeEntete = f.Range(l, 1, l, 9);
                rangeEntete.Style.Font.Bold = true;
                rangeEntete.Style.Font.FontSize = 9;
                rangeEntete.Style.Font.FontColor = XLColor.White;
                rangeEntete.Style.Fill.BackgroundColor = CouleurAccent;
                rangeEntete.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                rangeEntete.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
                f.Row(l).Height = 24;
                l++;

                // ── Lignes ───────────────────────────────────────────────────
                int num = 0;
                foreach (var ligne in piece.Lignes)
                {
                    num++;
                    bool pair = (num % 2 == 0);

                    f.Cell(l, 1).Value = ligne.Ordre;
                    f.Cell(l, 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

                    f.Cell(l, 2).Value = ligne.DateEcriture;
                    f.Cell(l, 2).Style.DateFormat.Format = "dd/MM/yyyy";
                    f.Cell(l, 2).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

                    f.Cell(l, 3).Value = ligne.CodeJournal;
                    f.Cell(l, 3).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

                    f.Cell(l, 4).Value = ligne.ReferencePiece;
                    f.Cell(l, 4).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

                    f.Cell(l, 5).Value = ligne.CompteGeneral;
                    f.Cell(l, 5).Style.Font.Bold = true;
                    f.Cell(l, 5).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

                    f.Cell(l, 6).Value = ligne.IntituleCompte;
                    f.Cell(l, 7).Value = ligne.LibelleOperation;

                    if (ligne.Debit > 0m)
                        f.Cell(l, 8).Value = ligne.Debit;
                    f.Cell(l, 8).Style.NumberFormat.Format = FormatMontant;
                    f.Cell(l, 8).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;

                    if (ligne.Credit > 0m)
                        f.Cell(l, 9).Value = ligne.Credit;
                    f.Cell(l, 9).Style.NumberFormat.Format = FormatMontant;
                    f.Cell(l, 9).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;

                    if (pair)
                    {
                        f.Range(l, 1, l, 9).Style.Fill.BackgroundColor = CouleurTrame;
                    }

                    f.Range(l, 1, l, 9).Style.Border.BottomBorder = XLBorderStyleValues.Thin;
                    f.Range(l, 1, l, 9).Style.Border.BottomBorderColor = CouleurBordure;

                    f.Row(l).Height = 20;
                    l++;
                }

                // ── Totaux de contrôle ───────────────────────────────────────
                f.Cell(l, 1).Value = "TOTAL PIÈCE";
                f.Cell(l, 1).Style.Font.Bold = true;
                f.Range(l, 1, l, 7).Merge();
                f.Range(l, 1, l, 7).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;

                f.Cell(l, 8).Value = piece.TotalDebit;
                f.Cell(l, 8).Style.NumberFormat.Format = FormatMontant;
                f.Cell(l, 8).Style.Font.Bold = true;
                f.Cell(l, 8).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;

                f.Cell(l, 9).Value = piece.TotalCredit;
                f.Cell(l, 9).Style.NumberFormat.Format = FormatMontant;
                f.Cell(l, 9).Style.Font.Bold = true;
                f.Cell(l, 9).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;

                var rangeTotaux = f.Range(l, 1, l, 9);
                rangeTotaux.Style.Border.TopBorder = XLBorderStyleValues.Medium;
                rangeTotaux.Style.Border.TopBorderColor = CouleurAccent;
                rangeTotaux.Style.Border.BottomBorder = XLBorderStyleValues.Double;
                rangeTotaux.Style.Border.BottomBorderColor = CouleurAccent;
                rangeTotaux.Style.Fill.BackgroundColor = XLColor.FromArgb(240, 246, 244);
                f.Row(l).Height = 22;
                l += 2;

                // Statut d'équilibre
                f.Cell(l, 1).Value = piece.EstEquilibree
                    ? "✓ Pièce équilibrée (Écart Débit / Crédit = 0 FCFA)"
                    : string.Format("⚠ Pièce déséquilibrée — Écart : {0:N0} FCFA", piece.Ecart);
                f.Cell(l, 1).Style.Font.Bold = true;
                f.Cell(l, 1).Style.Font.FontColor = piece.EstEquilibree
                    ? XLColor.FromArgb(31, 122, 77)
                    : CouleurAlerte;
                f.Range(l, 1, l, 9).Merge();

                f.Columns().AdjustToContents();
                f.Column(1).Width = 6;
                f.Column(2).Width = 12;
                f.Column(3).Width = 10;
                f.Column(4).Width = 18;
                f.Column(5).Width = 12;
                f.Column(6).Width = 32;
                f.Column(7).Width = 38;
                f.Column(8).Width = 16;
                f.Column(9).Width = 16;

                // Onglet « Import Sage » : seulement pour une pièce importable (équilibrée,
                // champs dans les limites de Sage). Sinon l'onglet induirait en erreur.
                var lignesSage = ConstruireImportSage(piece);
                if (ControlerImportSage(piece, lignesSage).Count == 0)
                    AjouterOngletImportSage(classeur, lignesSage);

                classeur.SaveAs(cheminFichier);
            }

            return cheminFichier;
        }

        // ═══════════════════════════════════════════════════════════════════════
        // IMPORT SAGE 100c COMPTABILITÉ
        // ═══════════════════════════════════════════════════════════════════════

        /// <summary>Encodage attendu par Sage 100c pour un fichier d'import : Windows-1252 (ANSI).</summary>
        public static Encoding EncodageSage
        {
            get { return Encoding.GetEncoding(1252); }
        }

        /// <summary>
        /// Lignes d'import Sage de la pièce : une ligne par montant non nul, avec son sens.
        /// Un montant négatif est porté, en positif, dans le sens opposé.
        /// </summary>
        public static List<LigneImportSage> ConstruireImportSage(PieceComptable piece)
        {
            var lignes = new List<LigneImportSage>();
            if (piece == null) return lignes;

            string date = piece.DatePiece.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);

            foreach (var l in piece.Lignes)
            {
                string intitule = LigneImportSage.TronquerIntitule(
                    !string.IsNullOrWhiteSpace(l.LibelleSage) ? l.LibelleSage : l.LibelleOperation);

                foreach (var montant in new[] { l.Debit, -l.Credit })
                {
                    if (montant == 0m) continue;

                    lignes.Add(new LigneImportSage
                    {
                        JO_Num = Nettoyer(piece.CodeJournal),
                        EC_Reference = Nettoyer(piece.Reference),
                        Date = date,
                        EC_Piece = Nettoyer(piece.NumeroPiece),
                        CG_Num = Nettoyer(l.CompteGeneral),
                        CT_Num = Nettoyer(l.CompteTiers),
                        EC_Intitule = intitule,
                        Sens = montant > 0m ? LigneImportSage.SensDebit : LigneImportSage.SensCredit,
                        Montant = Math.Abs(montant)
                    });
                }
            }

            return lignes;
        }

        /// <summary>
        /// Contrôles bloquants avant tout export Sage : pièce non vide, total D = total C,
        /// et champs dans les limites de Sage 100c. Liste vide : la pièce est importable.
        /// </summary>
        public static List<string> ControlerImportSage(PieceComptable piece, IList<LigneImportSage> lignes)
        {
            var erreurs = new List<string>();
            if (piece == null || lignes == null || lignes.Count == 0)
            {
                erreurs.Add("Aucune écriture à exporter : générez d'abord l'écriture d'un mois qui a des bulletins enregistrés.");
                return erreurs;
            }

            decimal totalD = lignes.Where(l => l.Sens == LigneImportSage.SensDebit).Sum(l => l.Montant);
            decimal totalC = lignes.Where(l => l.Sens == LigneImportSage.SensCredit).Sum(l => l.Montant);
            if (totalD != totalC)
            {
                erreurs.Add(string.Format(
                    "Pièce déséquilibrée : total D {0} ≠ total C {1} (écart {2} FCFA). Sage refuserait la pièce ; corrigez les bulletins avant d'exporter.",
                    FormaterMontant(totalD), FormaterMontant(totalC), FormaterMontant(totalD - totalC)));
            }

            string journal = lignes[0].JO_Num ?? "";
            if (journal.Length == 0 || journal.Length > ParametresComptables.LongueurMaxJournalSage)
                erreurs.Add(string.Format("Code journal « {0} » invalide : 1 à {1} caractères (paramètres comptables).", journal, ParametresComptables.LongueurMaxJournalSage));

            string numero = lignes[0].EC_Piece ?? "";
            if (numero.Length == 0 || numero.Length > ParametresComptables.LongueurMaxPieceSage)
                erreurs.Add(string.Format("N° de pièce « {0} » invalide : 1 à {1} caractères (paramètres comptables).", numero, ParametresComptables.LongueurMaxPieceSage));

            string reference = lignes[0].EC_Reference ?? "";
            if (reference.Length > ParametresComptables.LongueurMaxReferenceSage)
                erreurs.Add(string.Format("Référence « {0} » trop longue : {1} caractères au plus (paramètres comptables).", reference, ParametresComptables.LongueurMaxReferenceSage));

            foreach (var l in lignes)
            {
                if (string.IsNullOrEmpty(l.CG_Num))
                    erreurs.Add(string.Format("Ligne « {0} » sans compte général.", l.EC_Intitule));
                else if (l.CG_Num.Length > ParametresComptables.LongueurMaxCompteSage)
                    erreurs.Add(string.Format("Compte {0} : plus de {1} caractères (limite de Sage 100c).", l.CG_Num, ParametresComptables.LongueurMaxCompteSage));

                if ((l.EC_Intitule ?? "").Length > ParametresComptables.LongueurMaxIntituleSage)
                    erreurs.Add(string.Format("Intitulé « {0} » : 34 caractères ou plus.", l.EC_Intitule));
            }

            return erreurs.Distinct().ToList();
        }

        /// <summary>Vrai si l'écriture peut être exportée vers Sage (et l'onglet « Import Sage » ajouté).</summary>
        public static bool ImportSagePossible(PieceComptable piece)
        {
            return ControlerImportSage(piece, ConstruireImportSage(piece)).Count == 0;
        }

        /// <summary>Contenu du fichier d'import : point-virgule, une ligne par montant, fin de ligne Windows.</summary>
        public static string ContenuFichierSage(PieceComptable piece, IList<LigneImportSage> lignes)
        {
            var sb = new StringBuilder();
            if (piece == null || piece.EnTeteFichierSage)
            {
                sb.Append(string.Join(";", LigneImportSage.Colonnes));
                sb.Append("\r\n");
            }

            foreach (var l in lignes)
            {
                sb.Append(string.Join(";", new[]
                {
                    l.JO_Num, l.EC_Reference, l.Date, l.EC_Piece, l.CG_Num, l.CT_Num,
                    Nettoyer(l.EC_Intitule), l.Sens, FormaterMontant(l.Montant)
                }));
                sb.Append("\r\n");
            }

            return sb.ToString();
        }

        /// <summary>
        /// Fichier texte d'import Sage 100c Comptabilité, encodé Windows-1252. Refuse
        /// (InvalidOperationException) une pièce vide, déséquilibrée ou hors des limites de
        /// Sage : rien n'est écrit dans ce cas.
        /// </summary>
        public string ExporterSage(PieceComptable piece, string cheminFichier)
        {
            if (piece == null) throw new ArgumentNullException(nameof(piece));
            if (string.IsNullOrWhiteSpace(cheminFichier))
                throw new ArgumentException("Chemin de fichier manquant.", nameof(cheminFichier));

            var lignes = ConstruireImportSage(piece);
            var erreurs = ControlerImportSage(piece, lignes);
            if (erreurs.Count > 0)
                throw new InvalidOperationException("Export Sage refusé : " + string.Join(" ", erreurs));

            File.WriteAllText(cheminFichier, ContenuFichierSage(piece, lignes), EncodageSage);
            return cheminFichier;
        }

        /// <summary>
        /// Montant sans séparateur de milliers ; décimales (virgule) seulement s'il y en a —
        /// en FCFA, il n'y en a normalement pas.
        /// </summary>
        public static string FormaterMontant(decimal montant)
        {
            return montant == decimal.Truncate(montant)
                ? montant.ToString("0", CultureInfo.InvariantCulture)
                : montant.ToString("0.00", CultureInfo.GetCultureInfo("fr-FR"));
        }

        private static string Nettoyer(string texte)
        {
            return (texte ?? "").Replace(';', ',').Replace('\r', ' ').Replace('\n', ' ').Trim();
        }

        /// <summary>
        /// Onglet au format de la feuille « Import Sage » que le client utilise déjà :
        /// dates et comptes en texte, montant unique sans séparateur de milliers, lignes de
        /// contrôle par SOMME.SI sur la colonne Sens.
        /// </summary>
        private static void AjouterOngletImportSage(XLWorkbook classeur, IList<LigneImportSage> lignes)
        {
            var f = classeur.Worksheets.Add("Import Sage");
            f.Style.Font.FontName = "Arial";
            f.Style.Font.FontSize = 10;

            for (int c = 0; c < LigneImportSage.Colonnes.Length; c++)
            {
                var cellule = f.Cell(1, c + 1);
                cellule.Value = LigneImportSage.Colonnes[c];
                cellule.Style.Font.Bold = true;
            }

            bool avecDecimales = lignes.Any(l => l.Montant != decimal.Truncate(l.Montant));
            int ligne = 2;
            foreach (var l in lignes)
            {
                EcrireTexte(f.Cell(ligne, 1), l.JO_Num);
                EcrireTexte(f.Cell(ligne, 2), l.EC_Reference);
                EcrireTexte(f.Cell(ligne, 3), l.Date);
                EcrireTexte(f.Cell(ligne, 4), l.EC_Piece);
                EcrireTexte(f.Cell(ligne, 5), l.CG_Num);
                EcrireTexte(f.Cell(ligne, 6), l.CT_Num);
                EcrireTexte(f.Cell(ligne, 7), l.EC_Intitule);
                EcrireTexte(f.Cell(ligne, 8), l.Sens);
                f.Cell(ligne, 9).Value = l.Montant;
                f.Cell(ligne, 9).Style.NumberFormat.Format = avecDecimales ? "0.00" : "0";
                ligne++;
            }

            int derniere = ligne - 1;
            ligne++;
            string plageSens = string.Format(CultureInfo.InvariantCulture, "$H$2:$H${0}", derniere);
            string plageMontant = string.Format(CultureInfo.InvariantCulture, "$I$2:$I${0}", derniere);

            f.Cell(ligne, 8).Value = "TOTAL";
            f.Cell(ligne, 9).FormulaA1 = string.Format(CultureInfo.InvariantCulture,
                "SUMIF({0},\"D\",{1})-SUMIF({0},\"C\",{1})", plageSens, plageMontant);
            ligne++;
            f.Cell(ligne, 8).Value = "TOTAL D";
            f.Cell(ligne, 9).FormulaA1 = string.Format(CultureInfo.InvariantCulture,
                "SUMIF({0},\"D\",{1})", plageSens, plageMontant);
            ligne++;
            f.Cell(ligne, 8).Value = "TOTAL C";
            f.Cell(ligne, 9).FormulaA1 = string.Format(CultureInfo.InvariantCulture,
                "SUMIF({0},\"C\",{1})", plageSens, plageMontant);

            var controle = f.Range(ligne - 2, 8, ligne, 9);
            controle.Style.Font.Bold = true;
            f.Range(ligne - 2, 9, ligne, 9).Style.NumberFormat.Format = avecDecimales ? "0.00" : "0";

            f.Columns().AdjustToContents();
        }

        private static void EcrireTexte(IXLCell cellule, string valeur)
        {
            // Format texte AVANT la valeur : 0601 ou 431100 resteraient sinon des nombres.
            cellule.Style.NumberFormat.Format = "@";
            cellule.SetValue(valeur ?? "");
        }

        /// <summary>
        /// Exporte la pièce comptable au format texte générique (Date;Journal;Piece;Compte;Libelle;Debit;Credit,
        /// UTF-8). Ce n'est PAS le format d'import de Sage : voir <see cref="ExporterSage"/>.
        /// </summary>
        public string ExporterTexte(PieceComptable piece, string cheminFichier)
        {
            if (piece == null) throw new ArgumentNullException(nameof(piece));
            if (string.IsNullOrWhiteSpace(cheminFichier))
                throw new ArgumentException("Chemin de fichier manquant.", nameof(cheminFichier));

            var sb = new StringBuilder();
            // Ligne d'en-tête standard
            sb.AppendLine("Date;Journal;Piece;Compte;Libelle;Debit;Credit");

            foreach (var l in piece.Lignes)
            {
                sb.AppendFormat("{0:dd/MM/yyyy};{1};{2};{3};{4};{5:0};{6:0}",
                    l.DateEcriture,
                    l.CodeJournal,
                    l.ReferencePiece,
                    l.CompteGeneral,
                    (l.LibelleOperation ?? "").Replace(";", ","),
                    l.Debit,
                    l.Credit);
                sb.AppendLine();
            }

            File.WriteAllText(cheminFichier, sb.ToString(), Encoding.UTF8);
            return cheminFichier;
        }
    }
}
