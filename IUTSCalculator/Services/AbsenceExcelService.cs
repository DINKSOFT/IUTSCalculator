using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ClosedXML.Excel;
using IUTSCalculator.Models;

namespace IUTSCalculator.Services
{
    /// <summary>
    /// Résultat du traitement d'un fichier Excel d'importation de congés.
    /// </summary>
    public class ResultatImportConges
    {
        public int TotalLignesLues { get; set; }
        public int CongesImportes { get; set; }
        public int LignesIgnorees { get; set; }
        public List<string> Erreurs { get; } = new List<string>();
        public List<Absence> AbsencesValides { get; } = new List<Absence>();
        public bool Succes => Erreurs.Count == 0 && CongesImportes > 0;
    }

    /// <summary>
    /// Service d'exportation et d'importation Excel pour les congés et absences.
    /// Permet la saisie et l'intégration en masse pour des effectifs de plusieurs milliers de salariés.
    /// </summary>
    public class AbsenceExcelService
    {
        private static readonly XLColor CouleurEnTete = XLColor.FromHtml("#14675A");
        private static readonly XLColor CouleurBordure = XLColor.FromHtml("#CBD1CC");
        private static readonly XLColor CouleurAlternance = XLColor.FromHtml("#FAFBFA");
        private static readonly XLColor CouleurSaisie = XLColor.FromHtml("#FEF9E7"); // Légère teinte jaune d'invite à la saisie
        private static readonly XLColor CouleurBordureSaisie = XLColor.FromHtml("#F5B041");

        /// <summary>
        /// Exporte un modèle Excel pré-rempli pour la saisie des congés du mois.
        /// </summary>
        public async Task ExporterModeleAsync(
            IEnumerable<Employee> salaries,
            IEnumerable<SoldeConges> soldes,
            Dossier dossier,
            int annee,
            int mois,
            string cheminFichier)
        {
            if (salaries == null) throw new ArgumentNullException(nameof(salaries));
            if (string.IsNullOrWhiteSpace(cheminFichier)) throw new ArgumentNullException(nameof(cheminFichier));

            var listeSalaries = salaries.OrderBy(s => s.LastName).ThenBy(s => s.FirstName).ToList();
            var dictSoldes = (soldes ?? Enumerable.Empty<SoldeConges>())
                .ToDictionary(s => s.EmployeeId, StringComparer.OrdinalIgnoreCase);

            await Task.Run(() =>
            {
                using (var workbook = new XLWorkbook())
                {
                    var ws = workbook.Worksheets.Add("Saisie Congés");
                    ws.PageSetup.PageOrientation = XLPageOrientation.Landscape;
                    ws.PageSetup.PaperSize = XLPaperSize.A4Paper;

                    int row = 1;

                    // En-tête entreprise
                    ws.Cell(row, 1).Value = (XLCellValue)(dossier?.RaisonSociale ?? "ENTREPRISE");
                    ws.Cell(row, 1).Style.Font.Bold = true;
                    ws.Cell(row, 1).Style.Font.FontSize = 13;
                    row++;

                    string sousTitre = string.Format("MODÈLE DE SAISIE EN MASSE DES CONGÉS — PÉRIODE DE {0:D2}/{1}", mois, annee);
                    ws.Cell(row, 1).Value = (XLCellValue)sousTitre;
                    ws.Cell(row, 1).Style.Font.Bold = true;
                    ws.Cell(row, 1).Style.Font.FontSize = 10;
                    ws.Cell(row, 1).Style.Font.FontColor = XLColor.FromHtml("#48534F");
                    row++;

                    // Note explicative pour le gestionnaire RH
                    ws.Range(row, 1, row, 10).Merge();
                    ws.Cell(row, 1).Value = (XLCellValue)
                        "Instructions : Renseignez la colonne « Jours pris » pour les salariés concernés. " +
                        "Les dates « Du » et « Au » sont optionnelles (si omises, la période s'impute sur le mois en cours). " +
                        "Les lignes dont les « Jours pris » sont vides ou à 0 seront automatiquement ignorées lors de l'import.";
                    ws.Cell(row, 1).Style.Font.Italic = true;
                    ws.Cell(row, 1).Style.Font.FontSize = 9;
                    ws.Cell(row, 1).Style.Font.FontColor = XLColor.FromHtml("#5D6D7E");
                    row += 2;

                    int tableStartRow = row;

                    // Titres des colonnes
                    string[] entetes =
                    {
                        "Matricule", "Nom", "Prénom", "Catégorie", "Emploi",
                        "Solde actuel (j)", "Jours pris (*)", "Du (JJ/MM/AAAA)", "Au (JJ/MM/AAAA)", "Motif / Commentaire"
                    };

                    for (int c = 0; c < entetes.Length; c++)
                    {
                        var cell = ws.Cell(row, c + 1);
                        cell.Value = (XLCellValue)entetes[c];
                        cell.Style.Font.Bold = true;
                        cell.Style.Font.FontSize = 10;
                        cell.Style.Font.FontColor = XLColor.White;
                        cell.Style.Fill.BackgroundColor = CouleurEnTete;
                        cell.Style.Alignment.Horizontal = (c == 5 || c == 6) ? XLAlignmentHorizontalValues.Right : XLAlignmentHorizontalValues.Left;
                        cell.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
                        cell.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
                        cell.Style.Border.OutsideBorderColor = CouleurBordure;
                    }
                    ws.Row(row).Height = 25;
                    row++;

                    // Données
                    var dateDebutMois = new DateTime(annee, mois, 1);
                    var dateFinMois = dateDebutMois.AddMonths(1).AddDays(-1);

                    foreach (var s in listeSalaries)
                    {
                        dictSoldes.TryGetValue(s.EmployeeId ?? "", out var solde);

                        ws.Cell(row, 1).Value = (XLCellValue)(s.EmployeeId ?? "");
                        ws.Cell(row, 2).Value = (XLCellValue)(s.LastName ?? "");
                        ws.Cell(row, 3).Value = (XLCellValue)(s.FirstName ?? "");
                        ws.Cell(row, 4).Value = (XLCellValue)(s.Category ?? "");
                        ws.Cell(row, 5).Value = (XLCellValue)(s.Fonction ?? "");
                        
                        // Solde actuel informatif
                        var cellSolde = ws.Cell(row, 6);
                        cellSolde.Value = (XLCellValue)(solde != null ? solde.Solde : 0m);
                        cellSolde.Style.NumberFormat.Format = "#,##0.00";
                        cellSolde.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;

                        // Colonne de saisie principale : Jours pris
                        var cellJours = ws.Cell(row, 7);
                        cellJours.Style.Fill.BackgroundColor = CouleurSaisie;
                        cellJours.Style.NumberFormat.Format = "#,##0.00";
                        cellJours.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;
                        cellJours.Style.Font.Bold = true;

                        // Colonnes dates optionnelles
                        var cellDebut = ws.Cell(row, 8);
                        cellDebut.Style.Fill.BackgroundColor = CouleurSaisie;
                        cellDebut.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

                        var cellFin = ws.Cell(row, 9);
                        cellFin.Style.Fill.BackgroundColor = CouleurSaisie;
                        cellFin.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

                        // Commentaire
                        var cellCommentaire = ws.Cell(row, 10);
                        cellCommentaire.Style.Fill.BackgroundColor = CouleurSaisie;

                        // Bordures et alternance
                        for (int c = 1; c <= 10; c++)
                        {
                            var cell = ws.Cell(row, c);
                            cell.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
                            cell.Style.Border.OutsideBorderColor = CouleurBordure;
                            if (c <= 6 && (row % 2 == 0))
                            {
                                cell.Style.Fill.BackgroundColor = CouleurAlternance;
                            }
                        }

                        row++;
                    }

                    // Largeurs automatiques avec marges
                    ws.Columns(1, 10).AdjustToContents();
                    ws.Column(1).Width = Math.Max(ws.Column(1).Width, 14);
                    ws.Column(2).Width = Math.Max(ws.Column(2).Width, 18);
                    ws.Column(3).Width = Math.Max(ws.Column(3).Width, 18);
                    ws.Column(6).Width = Math.Max(ws.Column(6).Width, 16);
                    ws.Column(7).Width = 16;
                    ws.Column(8).Width = 16;
                    ws.Column(9).Width = 16;
                    ws.Column(10).Width = 24;

                    workbook.SaveAs(cheminFichier);
                }
            });
        }

        /// <summary>
        /// Importe et analyse un fichier Excel de saisie de congés.
        /// Identifie chaque salarié par matricule (ou nom), valide les données et prépare les objets Absence.
        /// </summary>
        public async Task<ResultatImportConges> ImporterCongesAsync(
            string cheminFichier,
            int annee,
            int mois,
            IEnumerable<Employee> salaries)
        {
            if (string.IsNullOrWhiteSpace(cheminFichier) || !File.Exists(cheminFichier))
                throw new FileNotFoundException("Fichier Excel introuvable.", cheminFichier);

            var listeSalaries = (salaries ?? Enumerable.Empty<Employee>()).ToList();
            var dictParMatricule = listeSalaries
                .Where(s => !string.IsNullOrWhiteSpace(s.EmployeeId))
                .ToDictionary(s => s.EmployeeId.Trim(), StringComparer.OrdinalIgnoreCase);

            var dictParNomComplet = listeSalaries
                .ToDictionary(s => string.Format("{0} {1}", s.LastName, s.FirstName).Trim().ToLowerInvariant(),
                              s => s);

            var resultat = new ResultatImportConges();

            await Task.Run(() =>
            {
                using (var workbook = new XLWorkbook(cheminFichier))
                {
                    var ws = workbook.Worksheets.FirstOrDefault();
                    if (ws == null)
                    {
                        resultat.Erreurs.Add("Le fichier Excel ne contient aucune feuille de calcul.");
                        return;
                    }

                    // 1. Détection de la ligne d'en-tête
                    int headerRow = 0;
                    int colMatricule = 0;
                    int colNom = 0;
                    int colPrenom = 0;
                    int colJours = 0;
                    int colDebut = 0;
                    int colFin = 0;
                    int colCommentaire = 0;

                    for (int r = 1; r <= 15; r++)
                    {
                        var row = ws.Row(r);
                        for (int c = 1; c <= 15; c++)
                        {
                            string val = row.Cell(c).GetString().Trim().ToLowerInvariant();
                            if (val.Contains("matricule") || val == "id" || val == "code") colMatricule = c;
                            else if (val == "nom") colNom = c;
                            else if (val == "prénom" || val == "prenom") colPrenom = c;
                            else if (val.Contains("jour") && (val.Contains("pris") || val.Contains("cong"))) colJours = c;
                            else if (val.StartsWith("du") || val.Contains("date début") || val.Contains("date debut")) colDebut = c;
                            else if (val.StartsWith("au") || val.Contains("date fin")) colFin = c;
                            else if (val.Contains("motif") || val.Contains("commentaire") || val.Contains("observ")) colCommentaire = c;
                        }

                        // Si au moins Matricule (ou Nom) et Jours sont trouvés, on a repéré l'en-tête
                        if ((colMatricule > 0 || colNom > 0) && colJours > 0)
                        {
                            headerRow = r;
                            break;
                        }
                    }

                    if (headerRow == 0 || colJours == 0)
                    {
                        resultat.Erreurs.Add("Impossible de repérer les colonnes obligatoires (Matricule / Nom et Jours pris) dans le fichier.");
                        return;
                    }

                    // 2. Parcours des lignes de données
                    int lastRow = ws.LastRowUsed()?.RowNumber() ?? headerRow;
                    var dateDebutMois = new DateTime(annee, mois, 1);
                    var dateFinMois = dateDebutMois.AddMonths(1).AddDays(-1);

                    for (int r = headerRow + 1; r <= lastRow; r++)
                    {
                        var row = ws.Row(r);
                        resultat.TotalLignesLues++;

                        // Lecture des jours
                        var cellJours = row.Cell(colJours);
                        decimal joursPris = 0m;

                        if (cellJours.TryGetValue<decimal>(out decimal decVal))
                        {
                            joursPris = decVal;
                        }
                        else
                        {
                            string strJours = cellJours.GetString().Trim().Replace(',', '.');
                            decimal.TryParse(strJours, NumberStyles.Any, CultureInfo.InvariantCulture, out joursPris);
                        }

                        // Si jours <= 0 ou non renseigné, on ignore la ligne
                        if (joursPris <= 0m)
                        {
                            resultat.LignesIgnorees++;
                            continue;
                        }

                        // Identification du salarié
                        string matricule = colMatricule > 0 ? row.Cell(colMatricule).GetString().Trim() : "";
                        string nom = colNom > 0 ? row.Cell(colNom).GetString().Trim() : "";
                        string prenom = colPrenom > 0 ? row.Cell(colPrenom).GetString().Trim() : "";

                        Employee salarie = null;
                        if (!string.IsNullOrWhiteSpace(matricule) && dictParMatricule.TryGetValue(matricule, out var empMat))
                        {
                            salarie = empMat;
                        }
                        else if (!string.IsNullOrWhiteSpace(nom))
                        {
                            string cleNom = string.Format("{0} {1}", nom, prenom).Trim().ToLowerInvariant();
                            dictParNomComplet.TryGetValue(cleNom, out salarie);
                        }

                        if (salarie == null)
                        {
                            resultat.Erreurs.Add(string.Format("Ligne {0} : Salarié non trouvé (Matricule: '{1}', Nom: '{2}').", r, matricule, nom));
                            continue;
                        }

                        // Dates de congé
                        DateTime dateDebut = dateDebutMois;
                        DateTime dateFin = dateFinMois;

                        if (colDebut > 0 && row.Cell(colDebut).TryGetValue<DateTime>(out DateTime dDeb))
                        {
                            dateDebut = dDeb;
                        }
                        else if (colDebut > 0)
                        {
                            string sDeb = row.Cell(colDebut).GetString().Trim();
                            if (DateTime.TryParseExact(sDeb, new[] { "dd/MM/yyyy", "d/M/yyyy", "yyyy-MM-dd" },
                                                       CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime pDeb))
                            {
                                dateDebut = pDeb;
                            }
                        }

                        if (colFin > 0 && row.Cell(colFin).TryGetValue<DateTime>(out DateTime dFin))
                        {
                            dateFin = dFin;
                        }
                        else if (colFin > 0)
                        {
                            string sFin = row.Cell(colFin).GetString().Trim();
                            if (DateTime.TryParseExact(sFin, new[] { "dd/MM/yyyy", "d/M/yyyy", "yyyy-MM-dd" },
                                                       CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime pFin))
                            {
                                dateFin = pFin;
                            }
                            else
                            {
                                // Si pas de date fin explicite, on calcule d'après les jours
                                dateFin = dateDebut.AddDays((double)Math.Max(1m, joursPris) - 1);
                            }
                        }
                        else
                        {
                            dateFin = dateDebut.AddDays((double)Math.Max(1m, joursPris) - 1);
                        }

                        if (dateFin < dateDebut)
                        {
                            dateFin = dateDebut;
                        }

                        // Commentaire
                        string commentaire = colCommentaire > 0 ? row.Cell(colCommentaire).GetString().Trim() : "";
                        if (string.IsNullOrWhiteSpace(commentaire))
                        {
                            commentaire = string.Format("Congé payé importé ({0:D2}/{1})", mois, annee);
                        }

                        var absence = new Absence
                        {
                            EmployeeId = salarie.EmployeeId,
                            NomSalarie = string.Format("{0} {1}", salarie.LastName, salarie.FirstName).Trim(),
                            Type = TypeAbsence.CongePaye,
                            DateDebut = dateDebut,
                            DateFin = dateFin,
                            NombreJours = joursPris,
                            NombreHeures = 0m,
                            Commentaire = commentaire,
                            DateSaisie = DateTime.Now
                        };

                        resultat.AbsencesValides.Add(absence);
                        resultat.CongesImportes++;
                    }
                }
            });

            return resultat;
        }
    }
}
