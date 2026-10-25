using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ClosedXML.Excel;
using IUTSCalculator.Models;

namespace IUTSCalculator.Services
{
    /// <summary>Ligne du planning telle qu'exportée ou importée en grille.</summary>
    public class LigneGrillePlanning
    {
        public string EmployeeId { get; set; }
        public string NomComplet { get; set; }
        public string EquipeCode { get; set; }

        /// <summary>Code de chaque jour de la période, dans l'ordre des dates.</summary>
        public Dictionary<DateTime, string> Codes { get; } = new Dictionary<DateTime, string>();
    }

    /// <summary>Résultat d'un import de pointages ou de planning.</summary>
    public class ResultatImportPointages
    {
        public int LignesLues { get; set; }
        public List<PlanningJour> Jours { get; } = new List<PlanningJour>();
        public List<string> Erreurs { get; } = new List<string>();
        public string Format { get; set; }
    }

    /// <summary>
    /// Échanges Excel/CSV du relevé des temps (module RH-08) :
    ///   • export du planning en GRILLE (une ligne par salarié, une colonne par jour, les
    ///     codes J/N/R… colorés comme dans l'état de rotation), avec le récapitulatif ;
    ///   • import de cette même grille corrigée dans Excel ;
    ///   • import d'un fichier de POINTAGES (badgeuse) : une ligne par salarié et par jour,
    ///     avec un code ou des heures d'entrée et de sortie.
    /// </summary>
    public class GestionTempsExcelService
    {
        private static readonly XLColor CouleurEnTete = XLColor.FromHtml("#14675A");
        private static readonly XLColor CouleurBordure = XLColor.FromHtml("#CBD1CC");
        private static readonly XLColor CouleurTotaux = XLColor.FromHtml("#E6F0ED");
        private static readonly XLColor CouleurWeekEnd = XLColor.FromHtml("#F3F4F2");

        /// <summary>Couleur de fond d'un code de planning (mêmes teintes que l'écran).</summary>
        public static string CouleurCode(string code)
        {
            switch (code)
            {
                case CodesPlanning.Jour: return "#FFF4C2";
                case CodesPlanning.Nuit: return "#C9D7F2";
                case CodesPlanning.Repos: return "#E3EFE6";
                case CodesPlanning.FerieJour: return "#FFD9A8";
                case CodesPlanning.FerieNuit: return "#B9B2E6";
                case CodesPlanning.AbsenceJustifiee: return "#E6E6E6";
                case CodesPlanning.AbsenceNonJustifiee: return "#F6C1BD";
                case CodesPlanning.Conge: return "#CFE8F7";
                case CodesPlanning.Maladie: return "#F2D5E8";
                default: return "#FFFFFF";
            }
        }

        // ═══════════════════════════════════════════════════════════════════
        //  Export
        // ═══════════════════════════════════════════════════════════════════

        public async Task ExporterPlanningAsync(
            IEnumerable<LigneGrillePlanning> lignes,
            IEnumerable<RecapTempsSalarie> recaps,
            PeriodePointage periode,
            Dossier dossier,
            string cheminFichier)
        {
            if (lignes == null) throw new ArgumentNullException(nameof(lignes));
            if (periode == null) throw new ArgumentNullException(nameof(periode));
            if (string.IsNullOrWhiteSpace(cheminFichier)) throw new ArgumentNullException(nameof(cheminFichier));

            var liste = lignes.ToList();
            var listeRecaps = (recaps ?? Enumerable.Empty<RecapTempsSalarie>()).ToList();

            await Task.Run(() =>
            {
                using (var wb = new XLWorkbook())
                {
                    EcrireGrille(wb, liste, periode, dossier);
                    if (listeRecaps.Count > 0)
                        EcrireRecapitulatif(wb, listeRecaps, periode, dossier);
                    EcrireLegende(wb);
                    wb.SaveAs(cheminFichier);
                }
            });
        }

        private static void EcrireGrille(XLWorkbook wb, List<LigneGrillePlanning> lignes, PeriodePointage periode, Dossier dossier)
        {
            var ws = wb.Worksheets.Add("Planning");
            ws.PageSetup.PageOrientation = XLPageOrientation.Landscape;
            ws.PageSetup.PaperSize = XLPaperSize.A4Paper;
            ws.PageSetup.FitToPages(1, 0);

            var dates = new List<DateTime>();
            for (var d = periode.Debut; d <= periode.Fin; d = d.AddDays(1)) dates.Add(d);
            int premiereColDate = 4;
            int derniereCol = premiereColDate + dates.Count - 1;

            ws.Cell(1, 1).Value = (XLCellValue)(dossier?.RaisonSociale ?? "ENTREPRISE");
            ws.Cell(1, 1).Style.Font.Bold = true;
            ws.Cell(1, 1).Style.Font.FontSize = 13;
            ws.Cell(2, 1).Value = (XLCellValue)string.Format(
                "PLANNING DE ROTATION — PAIE DE {0:D2}/{1} — PÉRIODE {2}", periode.Mois, periode.Annee, periode.Libelle.ToUpperInvariant());
            ws.Cell(2, 1).Style.Font.Bold = true;
            ws.Cell(3, 1).Value = (XLCellValue)("Codes : " + CodesPlanning.Legende() +
                ". Pour réimporter : ne modifiez ni la colonne Matricule ni la ligne des dates.");
            ws.Cell(3, 1).Style.Font.Italic = true;
            ws.Cell(3, 1).Style.Font.FontSize = 9;

            int ligneEntete = 5;
            ws.Cell(ligneEntete, 1).Value = (XLCellValue)"Matricule";
            ws.Cell(ligneEntete, 2).Value = (XLCellValue)"Nom & prénom";
            ws.Cell(ligneEntete, 3).Value = (XLCellValue)"Équipe";
            for (int i = 0; i < dates.Count; i++)
            {
                var c = ws.Cell(ligneEntete, premiereColDate + i);
                c.Value = (XLCellValue)dates[i];
                c.Style.DateFormat.Format = "dd/MM";
                c.Style.Alignment.TextRotation = 90;
                var j = ws.Cell(ligneEntete - 1, premiereColDate + i);
                j.Value = (XLCellValue)CultureInfo.GetCultureInfo("fr-FR").DateTimeFormat.GetAbbreviatedDayName(dates[i].DayOfWeek).Substring(0, 2);
                j.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                j.Style.Font.FontSize = 8;
            }

            var entete = ws.Range(ligneEntete, 1, ligneEntete, derniereCol);
            entete.Style.Font.Bold = true;
            entete.Style.Font.FontColor = XLColor.White;
            entete.Style.Fill.BackgroundColor = CouleurEnTete;
            entete.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
            entete.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            ws.Row(ligneEntete).Height = 38;

            int row = ligneEntete + 1;
            foreach (var l in lignes.OrderBy(x => x.EquipeCode).ThenBy(x => x.NomComplet))
            {
                ws.Cell(row, 1).Value = (XLCellValue)(l.EmployeeId ?? "");
                ws.Cell(row, 2).Value = (XLCellValue)(l.NomComplet ?? "");
                ws.Cell(row, 3).Value = (XLCellValue)(l.EquipeCode ?? "");

                for (int i = 0; i < dates.Count; i++)
                {
                    string code;
                    l.Codes.TryGetValue(dates[i], out code);
                    var c = ws.Cell(row, premiereColDate + i);
                    c.Value = (XLCellValue)(code ?? "");
                    c.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                    if (!string.IsNullOrEmpty(code))
                        c.Style.Fill.BackgroundColor = XLColor.FromHtml(CouleurCode(code));
                    else if (dates[i].DayOfWeek == DayOfWeek.Sunday)
                        c.Style.Fill.BackgroundColor = CouleurWeekEnd;
                }
                row++;
            }

            if (row > ligneEntete + 1)
            {
                var donnees = ws.Range(ligneEntete, 1, row - 1, derniereCol);
                donnees.Style.Border.InsideBorder = XLBorderStyleValues.Thin;
                donnees.Style.Border.InsideBorderColor = CouleurBordure;
                donnees.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
                donnees.Style.Border.OutsideBorderColor = CouleurBordure;

                var saisie = ws.Range(ligneEntete + 1, premiereColDate, row - 1, derniereCol);
                saisie.CreateDataValidation().List("\"" + string.Join(",", CodesPlanning.Tous) + "\"", true);
            }

            ws.Column(1).Width = 12;
            ws.Column(2).Width = 28;
            ws.Column(3).Width = 10;
            for (int c = premiereColDate; c <= derniereCol; c++) ws.Column(c).Width = 4.2;
            ws.SheetView.FreezeRows(ligneEntete);
            ws.SheetView.FreezeColumns(3);
        }

        private static void EcrireRecapitulatif(XLWorkbook wb, List<RecapTempsSalarie> recaps, PeriodePointage periode, Dossier dossier)
        {
            var ws = wb.Worksheets.Add("Récapitulatif");
            ws.PageSetup.PageOrientation = XLPageOrientation.Landscape;

            ws.Cell(1, 1).Value = (XLCellValue)(dossier?.RaisonSociale ?? "ENTREPRISE");
            ws.Cell(1, 1).Style.Font.Bold = true;
            ws.Cell(2, 1).Value = (XLCellValue)string.Format(
                "RÉCAPITULATIF DES TEMPS — PAIE DE {0:D2}/{1} — PÉRIODE {2}", periode.Mois, periode.Annee, periode.Libelle.ToUpperInvariant());
            ws.Cell(2, 1).Style.Font.Bold = true;

            string[] entetes =
            {
                "Matricule", "Nom & prénom", "Équipe", "Jours travaillés", "Heures de la période",
                "Semaines décomptées", "Heures décomptées", "Heures normales", "HS totales",
                "HS 41e-48e h", "HS au-delà 48e h", "HS nuit", "HS dim./férié jour", "HS dim./férié nuit",
                "Paniers", "Montant paniers", "Repos", "AJ", "A", "CP", "M", "Observations"
            };

            int ligneEntete = 4;
            for (int i = 0; i < entetes.Length; i++)
            {
                var c = ws.Cell(ligneEntete, i + 1);
                c.Value = (XLCellValue)entetes[i];
                c.Style.Font.Bold = true;
                c.Style.Font.FontColor = XLColor.White;
                c.Style.Fill.BackgroundColor = CouleurEnTete;
                c.Style.Alignment.WrapText = true;
                c.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            }
            ws.Row(ligneEntete).Height = 32;

            int row = ligneEntete + 1;
            foreach (var r in recaps)
            {
                var valeurs = new object[]
                {
                    r.EmployeeId, r.NomComplet, r.EquipeCode, r.JoursTravailles, r.HeuresPeriode,
                    r.PeriodeDecompteLibelle, r.HeuresDecomptees, r.HeuresNormales, r.HeuresSup,
                    r.Tranche1, r.Tranche2, r.Nuit, r.FerieJour, r.FerieNuit,
                    r.Paniers, r.MontantPanier, r.JoursRepos, r.JoursAbsenceJustifiee, r.JoursAbsenceNonJustifiee,
                    r.JoursConge, r.JoursMaladie, r.Observations
                };

                for (int i = 0; i < valeurs.Length; i++)
                {
                    var c = ws.Cell(row, i + 1);
                    var v = valeurs[i];
                    if (v is decimal) { c.Value = (XLCellValue)Convert.ToDouble((decimal)v); c.Style.NumberFormat.Format = i == 15 ? "#,##0" : "0.##"; }
                    else if (v is int) c.Value = (XLCellValue)(int)v;
                    else c.Value = (XLCellValue)(v as string ?? "");
                    c.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
                    c.Style.Border.OutsideBorderColor = CouleurBordure;
                }
                row++;
            }

            if (row > ligneEntete + 1)
            {
                ws.Cell(row, 1).Value = (XLCellValue)"TOTAUX";
                ws.Range(row, 1, row, 3).Merge();
                foreach (int col in new[] { 4, 5, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18, 19, 20, 21 })
                {
                    string lettre = ws.Column(col).ColumnLetter();
                    ws.Cell(row, col).FormulaA1 = string.Format("SUM({0}{1}:{0}{2})", lettre, ligneEntete + 1, row - 1);
                    ws.Cell(row, col).Style.NumberFormat.Format = col == 16 ? "#,##0" : "0.##";
                }
                var totaux = ws.Range(row, 1, row, entetes.Length);
                totaux.Style.Font.Bold = true;
                totaux.Style.Fill.BackgroundColor = CouleurTotaux;
            }

            ws.Columns().AdjustToContents();
            ws.Column(entetes.Length).Width = 60;
            ws.SheetView.FreezeRows(ligneEntete);
        }

        private static void EcrireLegende(XLWorkbook wb)
        {
            var ws = wb.Worksheets.Add("Codes");
            ws.Cell(1, 1).Value = (XLCellValue)"Code";
            ws.Cell(1, 2).Value = (XLCellValue)"Signification";
            ws.Range(1, 1, 1, 2).Style.Font.Bold = true;
            int row = 2;
            foreach (var code in CodesPlanning.Tous)
            {
                ws.Cell(row, 1).Value = (XLCellValue)code;
                ws.Cell(row, 1).Style.Fill.BackgroundColor = XLColor.FromHtml(CouleurCode(code));
                ws.Cell(row, 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                ws.Cell(row, 2).Value = (XLCellValue)CodesPlanning.Libelle(code);
                row++;
            }
            ws.Columns().AdjustToContents();
        }

        /// <summary>
        /// Modèle de fichier de pointages (badgeuse) : une ligne par salarié et par jour.
        /// La feuille « Pointages » est vide ; la feuille « Mode d'emploi » explique les colonnes.
        /// </summary>
        public async Task ExporterModelePointagesAsync(string cheminFichier)
        {
            if (string.IsNullOrWhiteSpace(cheminFichier)) throw new ArgumentNullException(nameof(cheminFichier));

            await Task.Run(() =>
            {
                using (var wb = new XLWorkbook())
                {
                    var ws = wb.Worksheets.Add("Pointages");
                    string[] entetes = { "Matricule", "Date", "Code", "Entrée", "Sortie", "Heures", "Commentaire" };
                    for (int i = 0; i < entetes.Length; i++)
                    {
                        var c = ws.Cell(1, i + 1);
                        c.Value = (XLCellValue)entetes[i];
                        c.Style.Font.Bold = true;
                        c.Style.Font.FontColor = XLColor.White;
                        c.Style.Fill.BackgroundColor = CouleurEnTete;
                    }
                    ws.Column(2).Style.DateFormat.Format = "dd/MM/yyyy";
                    ws.Column(4).Style.DateFormat.Format = "HH:mm";
                    ws.Column(5).Style.DateFormat.Format = "HH:mm";
                    ws.Range(2, 3, 2000, 3).CreateDataValidation().List("\"" + string.Join(",", CodesPlanning.Tous) + "\"", true);
                    ws.Columns().AdjustToContents();
                    ws.Column(7).Width = 40;

                    var aide = wb.Worksheets.Add("Mode d'emploi");
                    var lignes = new[]
                    {
                        "Une ligne par salarié et par jour. Colonnes obligatoires : Matricule et Date (JJ/MM/AAAA).",
                        "Code : J, N, R, FJ, FN, AJ, A, CP ou M. S'il est renseigné, il prime.",
                        "Sans code, Entrée et Sortie (HH:MM) suffisent : les heures sont la différence (une sortie le lendemain est reconnue),",
                        "et le poste est de NUIT si l'entrée est à 18:00 ou plus tard, ou avant 05:00 ; de JOUR sinon.",
                        "Heures : nombre d'heures réellement faites, s'il diffère de la durée du poste de l'équipe.",
                        "Un fichier CSV (séparateur ; ou ,) avec les mêmes en-têtes est aussi accepté.",
                        "À l'import, un jour déjà présent dans le planning est remplacé par le pointage."
                    };
                    for (int i = 0; i < lignes.Length; i++) aide.Cell(i + 1, 1).Value = (XLCellValue)lignes[i];
                    aide.Column(1).Width = 120;

                    wb.SaveAs(cheminFichier);
                }
            });
        }

        // ═══════════════════════════════════════════════════════════════════
        //  Import
        // ═══════════════════════════════════════════════════════════════════

        /// <summary>
        /// Importe un fichier Excel (grille exportée par l'écran, ou liste de pointages) ou
        /// CSV (liste de pointages). Seuls les matricules connus sont retenus.
        /// </summary>
        public async Task<ResultatImportPointages> ImporterAsync(string cheminFichier, ISet<string> matriculesConnus)
        {
            if (string.IsNullOrWhiteSpace(cheminFichier) || !File.Exists(cheminFichier))
                throw new FileNotFoundException("Fichier introuvable.", cheminFichier);

            return await Task.Run(() =>
            {
                string ext = Path.GetExtension(cheminFichier).ToLowerInvariant();
                List<string[]> table;
                var resultat = new ResultatImportPointages();

                if (ext == ".csv" || ext == ".txt")
                {
                    table = LireCsv(cheminFichier);
                    ImporterListe(table, matriculesConnus, resultat);
                    resultat.Format = "liste de pointages (CSV)";
                    return resultat;
                }

                using (var wb = new XLWorkbook(cheminFichier))
                {
                    var ws = wb.Worksheets.FirstOrDefault(w => string.Equals(w.Name, "Planning", StringComparison.OrdinalIgnoreCase))
                             ?? wb.Worksheets.FirstOrDefault(w => string.Equals(w.Name, "Pointages", StringComparison.OrdinalIgnoreCase))
                             ?? wb.Worksheets.First();

                    // Grille : une ligne d'en-tête « Matricule » suivie de cellules de dates.
                    int ligneGrille = TrouverEnteteGrille(ws);
                    if (ligneGrille > 0)
                    {
                        ImporterGrille(ws, ligneGrille, matriculesConnus, resultat);
                        resultat.Format = "grille de planning";
                        return resultat;
                    }

                    table = LireFeuille(ws);
                    ImporterListe(table, matriculesConnus, resultat);
                    resultat.Format = "liste de pointages";
                    return resultat;
                }
            });
        }

        private static int TrouverEnteteGrille(IXLWorksheet ws)
        {
            var derniere = ws.LastRowUsed();
            int max = Math.Min(derniere != null ? derniere.RowNumber() : 0, 15);
            for (int r = 1; r <= max; r++)
            {
                if (!ws.Cell(r, 1).GetString().Trim().StartsWith("Matricule", StringComparison.OrdinalIgnoreCase)) continue;

                int dates = 0;
                var derniereCol = ws.Row(r).LastCellUsed();
                int nbCol = derniereCol != null ? derniereCol.Address.ColumnNumber : 0;
                for (int c = 2; c <= nbCol; c++)
                {
                    DateTime d;
                    if (ws.Cell(r, c).TryGetValue<DateTime>(out d)) dates++;
                }
                if (dates >= 7) return r;
            }
            return 0;
        }

        private static void ImporterGrille(IXLWorksheet ws, int ligneEntete, ISet<string> matricules, ResultatImportPointages resultat)
        {
            var colonnesDates = new Dictionary<int, DateTime>();
            var derniereCol = ws.Row(ligneEntete).LastCellUsed();
            int nbCol = derniereCol != null ? derniereCol.Address.ColumnNumber : 0;
            for (int c = 2; c <= nbCol; c++)
            {
                DateTime d;
                if (ws.Cell(ligneEntete, c).TryGetValue<DateTime>(out d)) colonnesDates[c] = d.Date;
            }

            var derniere = ws.LastRowUsed();
            int nbLignes = derniere != null ? derniere.RowNumber() : ligneEntete;
            for (int r = ligneEntete + 1; r <= nbLignes; r++)
            {
                string matricule = ws.Cell(r, 1).GetString().Trim();
                if (string.IsNullOrEmpty(matricule) || matricule.StartsWith("TOTAL", StringComparison.OrdinalIgnoreCase)) continue;
                resultat.LignesLues++;

                if (matricules != null && !matricules.Contains(matricule))
                {
                    resultat.Erreurs.Add(string.Format("Ligne {0} : matricule « {1} » inconnu dans ce dossier.", r, matricule));
                    continue;
                }

                foreach (var col in colonnesDates)
                {
                    string brut = ws.Cell(r, col.Key).GetString().Trim();
                    if (string.IsNullOrEmpty(brut)) continue;
                    string code = CodesPlanning.Normaliser(brut);
                    if (string.IsNullOrEmpty(code))
                    {
                        resultat.Erreurs.Add(string.Format("Ligne {0}, {1:dd/MM} : code « {2} » inconnu.", r, col.Value, brut));
                        continue;
                    }
                    resultat.Jours.Add(new PlanningJour
                    {
                        EmployeeId = matricule,
                        Date = col.Value,
                        Code = code,
                        Source = SourceJourPlanning.Saisie,
                        Commentaire = "Import de grille"
                    });
                }
            }
        }

        /// <summary>Liste de pointages : Matricule, Date, [Code], [Entrée], [Sortie], [Heures], [Commentaire].</summary>
        private static void ImporterListe(List<string[]> table, ISet<string> matricules, ResultatImportPointages resultat)
        {
            if (table.Count == 0)
            {
                resultat.Erreurs.Add("Le fichier est vide.");
                return;
            }

            int ligneEntete = -1;
            for (int i = 0; i < Math.Min(table.Count, 10); i++)
            {
                if (table[i].Any(c => Norm(c) == "matricule")) { ligneEntete = i; break; }
            }
            if (ligneEntete < 0)
            {
                resultat.Erreurs.Add("Colonne « Matricule » introuvable dans les 10 premières lignes.");
                return;
            }

            var entetes = table[ligneEntete].Select(Norm).ToList();
            int iMat = entetes.IndexOf("matricule");
            int iDate = entetes.IndexOf("date");
            int iCode = entetes.IndexOf("code");
            int iEntree = entetes.FindIndex(e => e == "entree" || e == "heure entree" || e == "arrivee");
            int iSortie = entetes.FindIndex(e => e == "sortie" || e == "heure sortie" || e == "depart");
            int iHeures = entetes.FindIndex(e => e == "heures" || e == "nb heures" || e == "duree");
            int iComm = entetes.FindIndex(e => e == "commentaire" || e == "observation");

            if (iDate < 0)
            {
                resultat.Erreurs.Add("Colonne « Date » introuvable.");
                return;
            }

            for (int i = ligneEntete + 1; i < table.Count; i++)
            {
                var l = table[i];
                Func<int, string> val = k => k >= 0 && k < l.Length ? (l[k] ?? "").Trim() : "";
                string matricule = val(iMat);
                if (string.IsNullOrEmpty(matricule)) continue;
                resultat.LignesLues++;
                int numero = i + 1;

                if (matricules != null && !matricules.Contains(matricule))
                {
                    resultat.Erreurs.Add(string.Format("Ligne {0} : matricule « {1} » inconnu dans ce dossier.", numero, matricule));
                    continue;
                }

                DateTime date;
                if (!LireDate(val(iDate), out date))
                {
                    resultat.Erreurs.Add(string.Format("Ligne {0} : date « {1} » illisible (JJ/MM/AAAA attendu).", numero, val(iDate)));
                    continue;
                }

                string code = CodesPlanning.Normaliser(val(iCode));
                if (code == null)
                {
                    resultat.Erreurs.Add(string.Format("Ligne {0} : code « {1} » inconnu.", numero, val(iCode)));
                    continue;
                }

                decimal? heures = null;
                decimal h;
                if (!string.IsNullOrEmpty(val(iHeures)))
                {
                    if (!decimal.TryParse(val(iHeures).Replace(',', '.'), NumberStyles.Number, CultureInfo.InvariantCulture, out h) || h < 0m || h > 24m)
                    {
                        resultat.Erreurs.Add(string.Format("Ligne {0} : heures « {1} » illisibles (0 à 24).", numero, val(iHeures)));
                        continue;
                    }
                    heures = h;
                }

                TimeSpan entree, sortie;
                bool aEntree = LireHeure(val(iEntree), out entree);
                bool aSortie = LireHeure(val(iSortie), out sortie);
                if (aEntree && aSortie)
                {
                    var duree = sortie - entree;
                    if (duree <= TimeSpan.Zero) duree = duree.Add(TimeSpan.FromHours(24));
                    if (!heures.HasValue) heures = Math.Round((decimal)duree.TotalHours, 2);
                    if (string.IsNullOrEmpty(code))
                        code = (entree.TotalHours >= 18 || entree.TotalHours < 5) ? CodesPlanning.Nuit : CodesPlanning.Jour;
                }

                if (string.IsNullOrEmpty(code))
                {
                    resultat.Erreurs.Add(string.Format("Ligne {0} : ni code, ni heures d'entrée et de sortie.", numero));
                    continue;
                }

                resultat.Jours.Add(new PlanningJour
                {
                    EmployeeId = matricule,
                    Date = date.Date,
                    Code = code,
                    Heures = CodesPlanning.EstTravaille(code) ? heures : null,
                    Source = SourceJourPlanning.Pointage,
                    Commentaire = string.IsNullOrEmpty(val(iComm)) ? "Pointage importé" : val(iComm)
                });
            }

            // Un même jour présent deux fois : la dernière ligne l'emporte.
            var dedoublonne = resultat.Jours
                .GroupBy(j => j.EmployeeId.ToUpperInvariant() + "|" + j.Date.ToString("yyyyMMdd"))
                .Select(g => g.Last())
                .ToList();
            resultat.Jours.Clear();
            resultat.Jours.AddRange(dedoublonne);
        }

        private static string Norm(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return string.Empty;
            string d = s.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);
            var sb = new StringBuilder();
            foreach (char c in d)
                if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark) sb.Append(c);
            return sb.ToString().Replace("(", "").Replace(")", "").Replace("*", "").Trim();
        }

        private static bool LireDate(string texte, out DateTime date)
        {
            var formats = new[] { "dd/MM/yyyy", "d/M/yyyy", "dd/MM/yy", "yyyy-MM-dd", "yyyy-MM-dd HH:mm:ss", "dd-MM-yyyy", "dd.MM.yyyy" };
            if (DateTime.TryParseExact((texte ?? "").Trim(), formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out date))
                return true;
            double oa;
            if (double.TryParse(texte, NumberStyles.Float, CultureInfo.InvariantCulture, out oa) && oa > 20000 && oa < 80000)
            {
                date = DateTime.FromOADate(oa);
                return true;
            }
            return DateTime.TryParse(texte, CultureInfo.GetCultureInfo("fr-FR"), DateTimeStyles.None, out date);
        }

        private static bool LireHeure(string texte, out TimeSpan heure)
        {
            heure = TimeSpan.Zero;
            if (string.IsNullOrWhiteSpace(texte)) return false;
            string t = texte.Trim().Replace('h', ':').Replace('H', ':');
            if (t.EndsWith(":")) t += "00";
            if (TimeSpan.TryParseExact(t, new[] { @"h\:mm", @"hh\:mm", @"h\:mm\:ss", @"hh\:mm\:ss" }, CultureInfo.InvariantCulture, out heure))
                return true;
            DateTime dt;
            if (DateTime.TryParse(texte, CultureInfo.InvariantCulture, DateTimeStyles.None, out dt))
            {
                heure = dt.TimeOfDay;
                return true;
            }
            double fraction;
            if (double.TryParse(texte, NumberStyles.Float, CultureInfo.InvariantCulture, out fraction) && fraction >= 0 && fraction < 1)
            {
                heure = TimeSpan.FromDays(fraction);
                return true;
            }
            return false;
        }

        private static List<string[]> LireFeuille(IXLWorksheet ws)
        {
            var table = new List<string[]>();
            var derniere = ws.LastRowUsed();
            var derniereCol = ws.LastColumnUsed();
            if (derniere == null || derniereCol == null) return table;
            int nbCol = derniereCol.ColumnNumber();

            for (int r = 1; r <= derniere.RowNumber(); r++)
            {
                var ligne = new string[nbCol];
                for (int c = 1; c <= nbCol; c++)
                {
                    var cell = ws.Cell(r, c);
                    DateTime d;
                    if (cell.DataType == XLDataType.DateTime && cell.TryGetValue<DateTime>(out d))
                    {
                        ligne[c - 1] = d.Year < 1901
                            ? d.ToString("HH:mm", CultureInfo.InvariantCulture)
                            : d.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);
                    }
                    else if (cell.DataType == XLDataType.TimeSpan)
                    {
                        TimeSpan ts;
                        ligne[c - 1] = cell.TryGetValue<TimeSpan>(out ts) ? ts.ToString(@"hh\:mm", CultureInfo.InvariantCulture) : cell.GetString();
                    }
                    else if (cell.DataType == XLDataType.Number)
                    {
                        double n;
                        ligne[c - 1] = cell.TryGetValue<double>(out n) ? n.ToString(CultureInfo.InvariantCulture) : cell.GetString();
                    }
                    else
                    {
                        ligne[c - 1] = cell.GetString();
                    }
                }
                table.Add(ligne);
            }
            return table;
        }

        private static List<string[]> LireCsv(string chemin)
        {
            byte[] octets = File.ReadAllBytes(chemin);
            string texte;
            if (octets.Length >= 3 && octets[0] == 0xEF && octets[1] == 0xBB && octets[2] == 0xBF)
            {
                texte = Encoding.UTF8.GetString(octets, 3, octets.Length - 3);
            }
            else
            {
                try { texte = new UTF8Encoding(false, true).GetString(octets); }
                catch (DecoderFallbackException) { texte = Encoding.GetEncoding(1252).GetString(octets); }
            }

            var lignes = texte.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            string premiere = lignes.FirstOrDefault(l => !string.IsNullOrWhiteSpace(l)) ?? "";
            char sep = premiere.Count(c => c == ';') >= premiere.Count(c => c == ',') ? ';' : ',';
            if (premiere.Contains('\t') && !premiere.Contains(';')) sep = '\t';

            var table = new List<string[]>();
            foreach (var l in lignes)
            {
                if (string.IsNullOrWhiteSpace(l)) continue;
                table.Add(DecouperCsv(l, sep));
            }
            return table;
        }

        private static string[] DecouperCsv(string ligne, char sep)
        {
            var champs = new List<string>();
            var sb = new StringBuilder();
            bool guillemets = false;
            for (int i = 0; i < ligne.Length; i++)
            {
                char c = ligne[i];
                if (c == '"')
                {
                    if (guillemets && i + 1 < ligne.Length && ligne[i + 1] == '"') { sb.Append('"'); i++; }
                    else guillemets = !guillemets;
                }
                else if (c == sep && !guillemets)
                {
                    champs.Add(sb.ToString());
                    sb.Clear();
                }
                else sb.Append(c);
            }
            champs.Add(sb.ToString());
            return champs.ToArray();
        }
    }
}
