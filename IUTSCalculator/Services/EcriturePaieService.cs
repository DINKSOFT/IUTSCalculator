using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using IUTSCalculator.Models;

namespace IUTSCalculator.Services
{
    /// <summary>
    /// Service de génération des écritures comptables de paie SYSCOHADA (module CPT-01).
    ///
    /// Construit la pièce de paie mensuelle équilibrée à partir des bulletins enregistrés,
    /// des comptes configurés dans le plan de paie et des paramètres comptables du dossier
    /// (table ParametresComptables, migration 38 : journal, pièce, longueur des comptes,
    /// comptes de charges patronales et de dettes — jusque-là écrits en dur ici).
    /// </summary>
    public class EcriturePaieService
    {
        /// <summary>Table des paramètres comptables du dossier (Cle, Valeur).</summary>
        public const string TableParametres = "ParametresComptables";

        private readonly SQLiteService _sqliteService;
        private readonly PeriodePaieService _periodeService;
        private readonly RubriqueService _rubriqueService;

        public EcriturePaieService(
            SQLiteService sqliteService,
            PeriodePaieService periodeService,
            RubriqueService rubriqueService)
        {
            if (sqliteService == null) throw new ArgumentNullException(nameof(sqliteService));
            if (periodeService == null) throw new ArgumentNullException(nameof(periodeService));
            if (rubriqueService == null) throw new ArgumentNullException(nameof(rubriqueService));

            _sqliteService = sqliteService;
            _periodeService = periodeService;
            _rubriqueService = rubriqueService;
        }

        // ── Paramètres comptables du dossier ─────────────────────────────────

        private static bool TableExiste(SQLiteConnection conn, SQLiteTransaction tx, string table)
        {
            using (var cmd = conn.CreateCommand())
            {
                if (tx != null) cmd.Transaction = tx;
                cmd.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = @n;";
                cmd.Parameters.AddWithValue("@n", table);
                return DbValue.IntFromDb(cmd.ExecuteScalar()) > 0;
            }
        }

        private static async Task<Dictionary<string, string>> LireValeursAsync(SQLiteConnection conn, SQLiteTransaction tx)
        {
            var valeurs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (!TableExiste(conn, tx, TableParametres)) return valeurs;

            using (var cmd = conn.CreateCommand())
            {
                if (tx != null) cmd.Transaction = tx;
                cmd.CommandText = "SELECT Cle, Valeur FROM " + TableParametres + ";";
                using (var reader = (SQLiteDataReader)await cmd.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                    {
                        string cle = DbValue.TextFromDb(reader["Cle"]);
                        if (!string.IsNullOrEmpty(cle))
                            valeurs[cle] = DbValue.TextFromDb(reader["Valeur"]) ?? "";
                    }
                }
            }
            return valeurs;
        }

        /// <summary>Vrai si la base du dossier peut enregistrer les paramètres (migration 38 passée).</summary>
        public async Task<bool> ParametresEnregistrablesAsync()
        {
            using (var conn = _sqliteService.CreateConnection())
            {
                await Task.Yield();
                return TableExiste(conn, null, TableParametres);
            }
        }

        /// <summary>
        /// Paramètres comptables du dossier. Un paramètre jamais enregistré — ou une base
        /// antérieure à la migration 38 — garde sa valeur initiale, qui reproduit l'écriture
        /// d'avant le paramétrage.
        /// </summary>
        public async Task<ParametresComptables> ChargerParametresAsync()
        {
            using (var conn = _sqliteService.CreateConnection())
            {
                return ParametresComptables.Depuis(await LireValeursAsync(conn, null));
            }
        }

        /// <summary>
        /// Enregistre les paramètres comptables, après contrôle, et journalise chaque valeur
        /// modifiée dans le journal d'audit (entité « Écritures de paie »), dans la même
        /// transaction.
        /// </summary>
        /// <returns>Nombre de paramètres modifiés.</returns>
        public async Task<int> EnregistrerParametresAsync(ParametresComptables parametres)
        {
            if (parametres == null) throw new ArgumentNullException(nameof(parametres));

            var anomalies = parametres.Anomalies(DateTime.Today);
            if (anomalies.Count > 0)
                throw new InvalidOperationException(string.Join(" ", anomalies));

            var nouvelles = parametres.Valeurs();
            int modifies = 0;

            using (var conn = _sqliteService.CreateConnection())
            using (var tx = conn.BeginTransaction())
            {
                try
                {
                    if (!TableExiste(conn, tx, TableParametres))
                    {
                        throw new InvalidOperationException(
                            "La base de ce dossier n'a pas encore la table des paramètres comptables " +
                            "(mise à niveau du schéma v38) : les paramètres ne peuvent pas être enregistrés. " +
                            "L'écriture continue d'utiliser les comptes d'origine.");
                    }

                    var anciennes = ParametresComptables.Depuis(await LireValeursAsync(conn, tx)).Valeurs();

                    foreach (var d in ParametresComptables.Definitions)
                    {
                        string valeur = nouvelles[d.Cle];
                        if (d.EstCompte) valeur = ParametresComptables.NettoyerCompte(valeur);
                        else valeur = (valeur ?? "").Trim();

                        using (var cmd = conn.CreateCommand())
                        {
                            cmd.Transaction = tx;
                            cmd.CommandText = "INSERT OR REPLACE INTO " + TableParametres + " (Cle, Valeur) VALUES (@cle, @valeur);";
                            cmd.Parameters.AddWithValue("@cle", d.Cle);
                            cmd.Parameters.AddWithValue("@valeur", valeur);
                            await cmd.ExecuteNonQueryAsync();
                        }

                        string ancienne = anciennes[d.Cle];
                        if (!string.Equals(ancienne, valeur, StringComparison.Ordinal))
                        {
                            modifies++;
                            await AuditService.EnregistrerDansTransactionAsync(
                                conn, tx,
                                action: "Modification paramètre comptable",
                                entite: AuditService.EntiteEcrituresPaie,
                                entiteId: d.Cle,
                                champ: d.Libelle,
                                ancienneValeur: ancienne,
                                nouvelleValeur: valeur,
                                details: "Paramètre de l'écriture de paie (écran « Écritures de paie », onglet « Paramètres comptables »). " +
                                         "S'applique aux écritures générées à partir de maintenant.");
                        }
                    }

                    tx.Commit();
                }
                catch
                {
                    try { tx.Rollback(); }
                    catch { /* connexion perdue */ }
                    throw;
                }
            }

            return modifies;
        }

        // ── Construction de la pièce ─────────────────────────────────────────

        /// <summary>
        /// Construit la pièce de paie pour un mois donné, avec les paramètres comptables
        /// enregistrés du dossier (ou ceux fournis).
        /// </summary>
        public async Task<PieceComptable> ConstruireAsync(Dossier dossier, int annee, int mois, ParametresComptables parametres = null)
        {
            if (dossier == null) throw new ArgumentNullException(nameof(dossier));
            if (mois < 1 || mois > 12) throw new ArgumentOutOfRangeException(nameof(mois));

            if (parametres == null)
                parametres = await ChargerParametresAsync();

            var debutPeriode = new DateTime(annee, mois, 1);
            var finPeriode = debutPeriode.AddMonths(1).AddDays(-1);

            var piece = new PieceComptable
            {
                NumeroPiece = parametres.NumeroPiece(finPeriode),
                Reference = parametres.Reference(finPeriode),
                CodeJournal = (parametres.CodeJournal ?? "").Trim(),
                LibelleJournal = (parametres.LibelleJournal ?? "").Trim(),
                EnTeteFichierSage = parametres.EnTeteFichierSage,
                DatePiece = finPeriode,
                DebutPeriode = debutPeriode,
                FinPeriode = finPeriode,
                RaisonSociale = dossier.RaisonSociale,
                Ifu = dossier.IFU
            };

            // Vérifier la clôture de la période
            var periode = await _periodeService.LirePeriodeAsync(annee, mois);
            piece.EstBrouillon = (periode == null || !periode.EstCloturee);

            // Charger les rubriques pour récupérer les comptes comptables paramétrés
            var rubriques = await _rubriqueService.ChargerAsync();
            var rubriqueParCode = new Dictionary<string, Rubrique>(StringComparer.OrdinalIgnoreCase);
            if (rubriques != null)
            {
                foreach (var r in rubriques)
                {
                    if (!rubriqueParCode.ContainsKey(r.Code))
                        rubriqueParCode[r.Code] = r;
                }
            }

            // Longueur uniforme des comptes généraux : paramétrée, ou à défaut celle du plus
            // long compte utilisé (rubriques du plan de paie + paramètres). Les rubriques
            // livrées portent des comptes à 4 chiffres (6611), les comptes de dettes en
            // avaient 6 (422000) : la pièce mélangeait les deux, ce que Sage refuse.
            var comptesUtilises = new List<string>(parametres.Comptes());
            if (rubriques != null)
            {
                foreach (var r in rubriques)
                {
                    comptesUtilises.Add(r.CompteComptable);
                    comptesUtilises.Add(r.CompteContrepartie);
                }
            }

            piece.LongueurComptesAutomatique = parametres.LongueurComptes <= 0;
            piece.LongueurComptes = piece.LongueurComptesAutomatique
                ? ParametresComptables.LongueurAutomatique(comptesUtilises)
                : parametres.LongueurComptes;

            // Agréger les données des bulletins enregistrés
            await RemplirEcrituresAsync(piece, rubriqueParCode, parametres);

            return piece;
        }

        private async Task RemplirEcrituresAsync(
            PieceComptable piece, Dictionary<string, Rubrique> rubriqueParCode, ParametresComptables parametres)
        {
            int longueur = piece.LongueurComptes;
            var comptesTropLongs = new SortedSet<string>(StringComparer.Ordinal);

            // Tout compte passe par ici avant d'entrer dans la pièce : complété par des zéros
            // à droite jusqu'à la longueur retenue, ou conservé tel quel — et signalé — s'il
            // est plus long. C'est aussi la clé d'agrégation : 6612 et 661200 ne font qu'un.
            string Normaliser(string compte)
            {
                string c = ParametresComptables.CompleterCompte(compte, longueur);
                if (longueur > 0 && c.Length > longueur)
                    comptesTropLongs.Add(c);
                return c;
            }

            decimal totalSalaireBase = 0m;
            decimal totalIndLogement = 0m;
            decimal totalIndTransport = 0m;
            decimal totalIndFonction = 0m;
            decimal totalIndSujetion = 0m;
            decimal totalRetenueAbsences = 0m;

            decimal totalCnssEmployee = 0m;
            decimal totalCnssPatronale = 0m;
            decimal totalIuts = 0m;
            decimal totalTpa = 0m;
            decimal totalRetenueSpeciale = 0m;
            decimal totalRetenuesDiverses = 0m;
            decimal totalNetAPayer = 0m;

            // Dictionnaire pour agréger les rubriques variables par compte comptable
            var gainsVariablesParCompte = new Dictionary<string, (string Libelle, decimal Montant)>(
                StringComparer.OrdinalIgnoreCase);
            var retenuesVariablesParCompte = new Dictionary<string, (string Libelle, decimal Montant)>(
                StringComparer.OrdinalIgnoreCase);

            string compteGainsRepli = Normaliser(parametres.CompteGainsVariables);
            string compteRetenuesRepli = Normaliser(parametres.CompteRetenuesDiverses);

            using (var conn = _sqliteService.CreateConnection())
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = @"
                    SELECT c.EmployeeId,
                           c.SalaireBrut,
                           c.CnssEmployee,
                           c.CnssPartPatronale,
                           c.IUTSNet,
                           c.RetenueSpeciale,
                           c.RetenuesDiverses,
                           c.GainsVariables,
                           c.TaxePatronale,
                           c.NetSalaryAfterTax,
                           c.SalaireNetaPayer,
                           c.BulletinArchive,
                           e.FirstName,
                           e.LastName,
                           e.BaseSalary,
                           e.HousingAllowance,
                           e.TransportAllowance,
                           e.FunctionAllowance,
                           e.SujetionAllowance
                    FROM TaxCalculations c
                    LEFT JOIN Employees e ON e.EmployeeId = c.EmployeeId
                    WHERE c.CalculationDate >= @debut
                      AND c.CalculationDate <= @fin
                      AND c.IsAnnual = 0
                      AND c.IsRegularization = 0
                    ORDER BY e.LastName, e.FirstName, c.CalculationDate";

                cmd.Parameters.AddWithValue("@debut",
                    DbValue.DateToDb(piece.DebutPeriode.Date));
                cmd.Parameters.AddWithValue("@fin",
                    DbValue.DateToDb(piece.FinPeriode.Date.AddDays(1).AddTicks(-1)));

                using (var reader = (SQLiteDataReader)await cmd.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                    {
                        decimal cnssEmployee = DbValue.MoneyFromDb(reader["CnssEmployee"]);
                        decimal cnssPatron = DbValue.MoneyFromDb(reader["CnssPartPatronale"]);
                        decimal iuts = DbValue.MoneyFromDb(reader["IUTSNet"]);
                        decimal retenueSpeciale = DbValue.MoneyFromDb(reader["RetenueSpeciale"]);
                        decimal retenuesDiverses = DbValue.MoneyFromDb(reader["RetenuesDiverses"]);
                        decimal gainsVariables = DbValue.MoneyFromDb(reader["GainsVariables"]);
                        decimal tpa = DbValue.MoneyFromDb(reader["TaxePatronale"]);
                        decimal netAPayer = DbValue.MoneyFromDb(reader["SalaireNetaPayer"]);
                        decimal brut = DbValue.MoneyFromDb(reader["SalaireBrut"]);

                        // Indemnités de repli depuis la fiche salarié
                        decimal salBase = DbValue.MoneyFromDb(reader["BaseSalary"]);
                        decimal indLog = DbValue.MoneyFromDb(reader["HousingAllowance"]);
                        decimal indTrans = DbValue.MoneyFromDb(reader["TransportAllowance"]);
                        decimal indFonc = DbValue.MoneyFromDb(reader["FunctionAllowance"]);
                        decimal indSuj = DbValue.MoneyFromDb(reader["SujetionAllowance"]);

                        totalCnssEmployee += cnssEmployee;
                        totalCnssPatronale += cnssPatron;
                        totalIuts += iuts;
                        totalTpa += tpa;
                        totalRetenueSpeciale += retenueSpeciale;
                        totalNetAPayer += netAPayer;

                        // Tenter d'extraire les rubriques et retenues d'absence depuis BulletinArchive
                        string archiveJson = DbValue.TextFromDb(reader["BulletinArchive"]);
                        bool aArchive = false;
                        decimal retenueAbsenceSalarie = 0m;

                        if (!string.IsNullOrWhiteSpace(archiveJson))
                        {
                            try
                            {
                                var bulletin = JsonSerializer.Deserialize<Bulletin>(archiveJson);
                                if (bulletin != null)
                                {
                                    aArchive = true;
                                    totalSalaireBase += bulletin.SalaireDeBase;
                                    totalIndLogement += bulletin.IndemniteLogement;
                                    totalIndTransport += bulletin.IndemniteTransport;
                                    totalIndFonction += bulletin.IndemniteFonction;
                                    totalIndSujetion += bulletin.IndemniteSujetion;
                                    retenueAbsenceSalarie = bulletin.RetenueAbsences;

                                    decimal totalGainsVars = 0m;
                                    if (bulletin.LignesVariables != null && bulletin.LignesVariables.Count > 0)
                                    {
                                        foreach (var lv in bulletin.LignesVariables)
                                        {
                                            string code = lv.Code ?? "";
                                            Rubrique rub = null;
                                            rubriqueParCode.TryGetValue(code, out rub);

                                            // Un gain se crédite globalement plus bas, sur une seule ligne « Personnel,
                                            // rémunérations dues » : CompteComptable suffit comme compte de
                                            // charge (débit). Une retenue, elle, RÈGLE un tiers ou un organisme précis
                                            // (l'acompte déjà versé, l'échéance de prêt, l'opposition, le syndicat...) :
                                            // c'est CompteContrepartie qui porte ce compte-là (4271/4272/4273/4274...),
                                            // CompteComptable valant 4221 — « Personnel » — pour toutes indifféremment.
                                            // Créditer CompteComptable les aurait toutes fondues sur une même ligne.
                                            string compte;
                                            if (lv.EstRetenue)
                                            {
                                                compte = rub != null && !string.IsNullOrWhiteSpace(rub.CompteContrepartie)
                                                    ? Normaliser(rub.CompteContrepartie)
                                                    : (rub != null && !string.IsNullOrWhiteSpace(rub.CompteComptable)
                                                        ? Normaliser(rub.CompteComptable)
                                                        : compteRetenuesRepli);
                                            }
                                            else
                                            {
                                                compte = rub != null && !string.IsNullOrWhiteSpace(rub.CompteComptable)
                                                    ? Normaliser(rub.CompteComptable)
                                                    : compteGainsRepli;
                                            }

                                            string libelle = rub != null && !string.IsNullOrWhiteSpace(rub.Libelle)
                                                ? rub.Libelle
                                                : (lv.Libelle ?? code);

                                            if (lv.EstRetenue)
                                            {
                                                totalRetenuesDiverses += lv.Montant;
                                                if (retenuesVariablesParCompte.ContainsKey(compte))
                                                    retenuesVariablesParCompte[compte] = (retenuesVariablesParCompte[compte].Libelle, retenuesVariablesParCompte[compte].Montant + lv.Montant);
                                                else
                                                    retenuesVariablesParCompte[compte] = (libelle, lv.Montant);
                                            }
                                            else
                                            {
                                                totalGainsVars += lv.Montant;
                                                if (gainsVariablesParCompte.ContainsKey(compte))
                                                    gainsVariablesParCompte[compte] = (gainsVariablesParCompte[compte].Libelle, gainsVariablesParCompte[compte].Montant + lv.Montant);
                                                else
                                                    gainsVariablesParCompte[compte] = (libelle, lv.Montant);
                                            }
                                        }
                                    }

                                    // Sécurité d'équilibrage : si RetenueAbsences valait 0 dans l'archive
                                    // mais qu'un écart existe entre rémunération contractuelle et brut réel
                                    if (retenueAbsenceSalarie <= 0m)
                                    {
                                        decimal totalPermanent = bulletin.SalaireDeBase + bulletin.IndemniteLogement +
                                                                 bulletin.IndemniteTransport + bulletin.IndemniteFonction +
                                                                 bulletin.IndemniteSujetion;
                                        decimal diff = (totalPermanent + totalGainsVars) - brut;
                                        if (diff > 0m)
                                            retenueAbsenceSalarie = diff;
                                    }
                                }
                            }
                            catch
                            {
                                aArchive = false;
                            }
                        }

                        if (!aArchive)
                        {
                            totalSalaireBase += salBase;
                            totalIndLogement += indLog;
                            totalIndTransport += indTrans;
                            totalIndFonction += indFonc;
                            totalIndSujetion += indSuj;

                            if (gainsVariables > 0m)
                            {
                                string compte = compteGainsRepli;
                                if (gainsVariablesParCompte.ContainsKey(compte))
                                    gainsVariablesParCompte[compte] = (gainsVariablesParCompte[compte].Libelle, gainsVariablesParCompte[compte].Montant + gainsVariables);
                                else
                                    gainsVariablesParCompte[compte] = ("Gains variables", gainsVariables);
                            }

                            if (retenuesDiverses > 0m)
                            {
                                totalRetenuesDiverses += retenuesDiverses;
                                string compte = compteRetenuesRepli;
                                if (retenuesVariablesParCompte.ContainsKey(compte))
                                    retenuesVariablesParCompte[compte] = (retenuesVariablesParCompte[compte].Libelle, retenuesVariablesParCompte[compte].Montant + retenuesDiverses);
                                else
                                    retenuesVariablesParCompte[compte] = ("Retenues diverses / Prêts", retenuesDiverses);
                            }

                            // Déduction d'absence de repli reconstituée depuis l'écart permanent + variables - brut
                            decimal totalPermanent = salBase + indLog + indTrans + indFonc + indSuj;
                            decimal diff = (totalPermanent + gainsVariables) - brut;
                            if (diff > 0m)
                                retenueAbsenceSalarie = diff;
                        }

                        totalRetenueAbsences += retenueAbsenceSalarie;
                    }
                }
            }

            int ordre = 1;
            string refPiece = piece.NumeroPiece;
            DateTime dateEcriture = piece.DatePiece;
            string moisTexte = piece.PeriodeLibelle;
            string moisCourt = piece.PeriodeCourte;

            void AjouterLigne(string compte, string intitule, string libelleOp, string libelleSage, decimal debit, decimal credit)
            {
                // Un montant négatif (gain variable globalement négatif, régularisation) passe
                // dans l'autre colonne : l'ignorer, comme avant, déséquilibrait la pièce.
                if (debit < 0m) { credit += -debit; debit = 0m; }
                if (credit < 0m) { debit += -credit; credit = 0m; }
                if (debit == 0m && credit == 0m) return;

                piece.Lignes.Add(new LigneEcritureComptable
                {
                    Ordre = ordre++,
                    DateEcriture = dateEcriture,
                    CodeJournal = piece.CodeJournal,
                    ReferencePiece = refPiece,
                    CompteGeneral = compte,
                    IntituleCompte = intitule,
                    LibelleOperation = libelleOp,
                    LibelleSage = LigneImportSage.IntituleAvecSuffixe(libelleSage, moisCourt),
                    CompteTiers = "",
                    Debit = debit,
                    Credit = credit
                });
            }

            string ObtenirCompte(string[] codes, string compteDefaut)
            {
                if (codes != null)
                {
                    foreach (var code in codes)
                    {
                        if (rubriqueParCode.TryGetValue(code, out var r) && !string.IsNullOrWhiteSpace(r.CompteComptable))
                            return Normaliser(r.CompteComptable);
                    }
                }
                return Normaliser(compteDefaut);
            }

            // ═══════════════════════════════════════════════════════════════════
            // DÉBITS : CHARGES DE PERSONNEL & TAXES PATRONALES (CLASSE 6)
            // ═══════════════════════════════════════════════════════════════════

            if (totalSalaireBase > 0m)
            {
                string c = ObtenirCompte(new[] { "SALBASE", "BASE" }, parametres.CompteSalaireBase);
                AjouterLigne(c, "Appointements et salaires", "Salaires de base - " + moisTexte, "Salaires de base", totalSalaireBase, 0m);
            }

            if (totalIndLogement > 0m)
            {
                string c = ObtenirCompte(new[] { "INDLOG" }, parametres.CompteIndemniteLogement);
                AjouterLigne(c, "Indemnités de logement", "Indemnités de logement - " + moisTexte, "Ind. logement", totalIndLogement, 0m);
            }

            if (totalIndTransport > 0m)
            {
                string c = ObtenirCompte(new[] { "INDTRA", "INDTRANS" }, parametres.CompteIndemniteTransport);
                AjouterLigne(c, "Indemnités de transport", "Indemnités de transport - " + moisTexte, "Ind. transport", totalIndTransport, 0m);
            }

            if (totalIndFonction > 0m)
            {
                string c = ObtenirCompte(new[] { "INDFON", "INDFONC" }, parametres.CompteIndemniteFonction);
                AjouterLigne(c, "Indemnités de fonction", "Indemnités de fonction - " + moisTexte, "Ind. fonction", totalIndFonction, 0m);
            }

            if (totalIndSujetion > 0m)
            {
                string c = ObtenirCompte(new[] { "INDSUJ" }, parametres.CompteIndemniteSujetion);
                AjouterLigne(c, "Indemnités de sujétion", "Indemnités de sujétion - " + moisTexte, "Ind. sujétion", totalIndSujetion, 0m);
            }

            // Gains variables par compte
            foreach (var kvp in gainsVariablesParCompte.OrderBy(k => k.Key))
            {
                if (kvp.Value.Montant != 0m)
                {
                    AjouterLigne(kvp.Key, kvp.Value.Libelle, kvp.Value.Libelle + " - " + moisTexte, kvp.Value.Libelle, kvp.Value.Montant, 0m);
                }
            }

            // Charges patronales CNSS
            if (totalCnssPatronale > 0m)
            {
                AjouterLigne(Normaliser(parametres.CompteChargesCnss), "Charges sociales patronales - CNSS",
                    "Cotisations patronales CNSS - " + moisTexte, "CNSS patronale", totalCnssPatronale, 0m);
            }

            // Taxes patronales TPA
            if (totalTpa > 0m)
            {
                AjouterLigne(Normaliser(parametres.CompteChargeTpa), "Taxes sur salaires - TPA",
                    "Taxe patronale et d'apprentissage - " + moisTexte, "TPA", totalTpa, 0m);
            }

            // ═══════════════════════════════════════════════════════════════════
            // CRÉDITS : DETTES ENVERS LE PERSONNEL, L'ÉTAT ET ORGANISMES (CLASSE 4)
            //           ET ATTÉNUATION DE CHARGES (ABSENCES ET CONGÉS NON RÉMUNÉRÉS)
            // ═══════════════════════════════════════════════════════════════════

            // Retenues pour absences et congés non rémunérés (atténuation directe de charge SYSCOHADA)
            if (totalRetenueAbsences > 0m)
            {
                string c = ObtenirCompte(new[] { "ABS", "ABSENCE", "CONGE", "CONGES", "SALBASE", "BASE" }, parametres.CompteSalaireBase);
                string intitule = "Appointements et salaires (Absences)";
                if (rubriqueParCode.TryGetValue("ABS", out var rAbs) && !string.IsNullOrWhiteSpace(rAbs.Libelle))
                    intitule = rAbs.Libelle;
                else if (rubriqueParCode.TryGetValue("ABSENCE", out rAbs) && !string.IsNullOrWhiteSpace(rAbs.Libelle))
                    intitule = rAbs.Libelle;

                AjouterLigne(c, intitule, "Retenues pour absences et congés non rémunérés - " + moisTexte,
                    "Absences non rémunérées", 0m, totalRetenueAbsences);
            }

            // Sécurité sociale (CNSS globale due = salariale + patronale)
            decimal totalCnssGlobal = totalCnssEmployee + totalCnssPatronale;
            if (totalCnssGlobal > 0m)
            {
                AjouterLigne(Normaliser(parametres.CompteCnss), "Sécurité sociale - CNSS",
                    "Cotisations CNSS dues (sal. + pat.) - " + moisTexte, "CNSS sal. + pat.", 0m, totalCnssGlobal);
            }

            // État - IUTS
            if (totalIuts > 0m)
            {
                AjouterLigne(Normaliser(parametres.CompteIuts), "État, impôts retenus à la source - IUTS",
                    "IUTS retenu sur salaires - " + moisTexte, "IUTS retenu", 0m, totalIuts);
            }

            // État - TPA due
            if (totalTpa > 0m)
            {
                AjouterLigne(Normaliser(parametres.CompteTpa), "État, taxe patronale et apprentissage",
                    "TPA due au Trésor - " + moisTexte, "TPA due", 0m, totalTpa);
            }

            // Retenue spéciale
            if (totalRetenueSpeciale > 0m)
            {
                AjouterLigne(Normaliser(parametres.CompteRetenueSpeciale), "État, autres retenues fiscales",
                    "Retenue spéciale sur salaires - " + moisTexte, "Retenue spéciale", 0m, totalRetenueSpeciale);
            }

            // Retenues diverses / remboursements de prêts
            if (retenuesVariablesParCompte.Count > 0)
            {
                foreach (var kvp in retenuesVariablesParCompte.OrderBy(k => k.Key))
                {
                    if (kvp.Value.Montant != 0m)
                    {
                        AjouterLigne(kvp.Key, kvp.Value.Libelle, kvp.Value.Libelle + " - " + moisTexte, kvp.Value.Libelle, 0m, kvp.Value.Montant);
                    }
                }
            }
            else if (totalRetenuesDiverses > 0m)
            {
                AjouterLigne(compteRetenuesRepli, "Personnel, avances et acomptes / prêts",
                    "Retenues sur salaires - " + moisTexte, "Retenues sur salaires", 0m, totalRetenuesDiverses);
            }

            // Net à payer aux salariés
            if (totalNetAPayer > 0m)
            {
                AjouterLigne(Normaliser(parametres.CompteNetAPayer), "Personnel, rémunérations dues",
                    "Salaires nets à payer - " + moisTexte, "Salaires nets", 0m, totalNetAPayer);
            }

            foreach (var c in comptesTropLongs)
            {
                piece.AvertissementsComptes.Add(string.Format(
                    "Le compte {0} compte {1} caractères, plus que la longueur des comptes généraux ({2}) : " +
                    "il est repris tel quel. Vérifiez qu'il existe dans le plan comptable de Sage, ou ajustez la longueur.",
                    c, c.Length, longueur));
            }
        }
    }
}
