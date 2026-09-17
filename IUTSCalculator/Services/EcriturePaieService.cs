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
    /// Construit la pièce d'Opérations Diverses (OD de paie) mensuelle équilibrée
    /// à partir des bulletins enregistrés et des comptes configurés dans le plan de paie.
    /// </summary>
    public class EcriturePaieService
    {
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

        /// <summary>
        /// Construit la pièce d'OD de paie pour un mois donné.
        /// </summary>
        public async Task<PieceComptable> ConstruireAsync(Dossier dossier, int annee, int mois)
        {
            if (dossier == null) throw new ArgumentNullException(nameof(dossier));
            if (mois < 1 || mois > 12) throw new ArgumentOutOfRangeException(nameof(mois));

            var debutPeriode = new DateTime(annee, mois, 1);
            var finPeriode = debutPeriode.AddMonths(1).AddDays(-1);

            var piece = new PieceComptable
            {
                NumeroPiece = string.Format("OD-PAIE-{0:yyyy-MM}", finPeriode),
                CodeJournal = "OD",
                LibelleJournal = "Opérations Diverses",
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

            // Agréger les données des bulletins enregistrés
            await RemplirEcrituresAsync(piece, rubriqueParCode);

            return piece;
        }

        private async Task RemplirEcrituresAsync(
            PieceComptable piece, Dictionary<string, Rubrique> rubriqueParCode)
        {
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
                                            // rémunérations dues » (422000) : CompteComptable suffit comme compte de
                                            // charge (débit). Une retenue, elle, RÈGLE un tiers ou un organisme précis
                                            // (l'acompte déjà versé, l'échéance de prêt, l'opposition, le syndicat...) :
                                            // c'est CompteContrepartie qui porte ce compte-là (4271/4272/4273/4274...),
                                            // CompteComptable valant 4221 — « Personnel » — pour toutes indifféremment.
                                            // Créditer CompteComptable les aurait toutes fondues sur une même ligne.
                                            string compte;
                                            if (lv.EstRetenue)
                                            {
                                                compte = rub != null && !string.IsNullOrWhiteSpace(rub.CompteContrepartie)
                                                    ? rub.CompteContrepartie
                                                    : (rub != null && !string.IsNullOrWhiteSpace(rub.CompteComptable)
                                                        ? rub.CompteComptable
                                                        : "422100");
                                            }
                                            else
                                            {
                                                compte = rub != null && !string.IsNullOrWhiteSpace(rub.CompteComptable)
                                                    ? rub.CompteComptable
                                                    : "661800";
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
                                string compte = "661800";
                                if (gainsVariablesParCompte.ContainsKey(compte))
                                    gainsVariablesParCompte[compte] = (gainsVariablesParCompte[compte].Libelle, gainsVariablesParCompte[compte].Montant + gainsVariables);
                                else
                                    gainsVariablesParCompte[compte] = ("Gains variables", gainsVariables);
                            }

                            if (retenuesDiverses > 0m)
                            {
                                totalRetenuesDiverses += retenuesDiverses;
                                string compte = "422100";
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

            void AjouterLigne(string compte, string intitule, string libelleOp, decimal debit, decimal credit)
            {
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
                            return r.CompteComptable;
                    }
                }
                return compteDefaut;
            }

            // ═══════════════════════════════════════════════════════════════════
            // DÉBITS : CHARGES DE PERSONNEL & TAXES PATRONALES (CLASSE 6)
            // ═══════════════════════════════════════════════════════════════════

            if (totalSalaireBase > 0m)
            {
                string c = ObtenirCompte(new[] { "SALBASE", "BASE" }, "661100");
                AjouterLigne(c, "Appointements et salaires", "Salaires de base - " + moisTexte, totalSalaireBase, 0m);
            }

            if (totalIndLogement > 0m)
            {
                string c = ObtenirCompte(new[] { "INDLOG" }, "663100");
                AjouterLigne(c, "Indemnités de logement", "Indemnités de logement - " + moisTexte, totalIndLogement, 0m);
            }

            if (totalIndTransport > 0m)
            {
                string c = ObtenirCompte(new[] { "INDTRA", "INDTRANS" }, "663200");
                AjouterLigne(c, "Indemnités de transport", "Indemnités de transport - " + moisTexte, totalIndTransport, 0m);
            }

            if (totalIndFonction > 0m)
            {
                string c = ObtenirCompte(new[] { "INDFON", "INDFONC" }, "663300");
                AjouterLigne(c, "Indemnités de fonction", "Indemnités de fonction - " + moisTexte, totalIndFonction, 0m);
            }

            if (totalIndSujetion > 0m)
            {
                string c = ObtenirCompte(new[] { "INDSUJ" }, "663400");
                AjouterLigne(c, "Indemnités de sujétion", "Indemnités de sujétion - " + moisTexte, totalIndSujetion, 0m);
            }

            // Gains variables par compte
            foreach (var kvp in gainsVariablesParCompte.OrderBy(k => k.Key))
            {
                if (kvp.Value.Montant > 0m)
                {
                    AjouterLigne(kvp.Key, kvp.Value.Libelle, kvp.Value.Libelle + " - " + moisTexte, kvp.Value.Montant, 0m);
                }
            }

            // Charges patronales CNSS
            if (totalCnssPatronale > 0m)
            {
                AjouterLigne("664100", "Charges sociales patronales - CNSS", "Cotisations patronales CNSS - " + moisTexte, totalCnssPatronale, 0m);
            }

            // Taxes patronales TPA
            if (totalTpa > 0m)
            {
                AjouterLigne("641300", "Taxes sur salaires - TPA", "Taxe patronale et d'apprentissage - " + moisTexte, totalTpa, 0m);
            }

            // ═══════════════════════════════════════════════════════════════════
            // CRÉDITS : DETTES ENVERS LE PERSONNEL, L'ÉTAT ET ORGANISMES (CLASSE 4)
            //           ET ATTÉNUATION DE CHARGES (ABSENCES ET CONGÉS NON RÉMUNÉRÉS)
            // ═══════════════════════════════════════════════════════════════════

            // Retenues pour absences et congés non rémunérés (atténuation directe de charge SYSCOHADA)
            if (totalRetenueAbsences > 0m)
            {
                string c = ObtenirCompte(new[] { "ABS", "ABSENCE", "CONGE", "CONGES", "SALBASE", "BASE" }, "661100");
                string intitule = "Appointements et salaires (Absences)";
                if (rubriqueParCode.TryGetValue("ABS", out var rAbs) && !string.IsNullOrWhiteSpace(rAbs.Libelle))
                    intitule = rAbs.Libelle;
                else if (rubriqueParCode.TryGetValue("ABSENCE", out rAbs) && !string.IsNullOrWhiteSpace(rAbs.Libelle))
                    intitule = rAbs.Libelle;

                AjouterLigne(c, intitule, "Retenues pour absences et congés non rémunérés - " + moisTexte, 0m, totalRetenueAbsences);
            }

            // Sécurité sociale (CNSS globale due = salariale + patronale)
            decimal totalCnssGlobal = totalCnssEmployee + totalCnssPatronale;
            if (totalCnssGlobal > 0m)
            {
                AjouterLigne("431100", "Sécurité sociale - CNSS", "Cotisations CNSS dues (sal. + pat.) - " + moisTexte, 0m, totalCnssGlobal);
            }

            // État - IUTS
            if (totalIuts > 0m)
            {
                AjouterLigne("447200", "État, impôts retenus à la source - IUTS", "IUTS retenu sur salaires - " + moisTexte, 0m, totalIuts);
            }

            // État - TPA due
            if (totalTpa > 0m)
            {
                AjouterLigne("447300", "État, taxe patronale et apprentissage", "TPA due au Trésor - " + moisTexte, 0m, totalTpa);
            }

            // Retenue spéciale
            if (totalRetenueSpeciale > 0m)
            {
                AjouterLigne("447800", "État, autres retenues fiscales", "Retenue spéciale sur salaires - " + moisTexte, 0m, totalRetenueSpeciale);
            }

            // Retenues diverses / remboursements de prêts
            if (retenuesVariablesParCompte.Count > 0)
            {
                foreach (var kvp in retenuesVariablesParCompte.OrderBy(k => k.Key))
                {
                    if (kvp.Value.Montant > 0m)
                    {
                        AjouterLigne(kvp.Key, kvp.Value.Libelle, kvp.Value.Libelle + " - " + moisTexte, 0m, kvp.Value.Montant);
                    }
                }
            }
            else if (totalRetenuesDiverses > 0m)
            {
                AjouterLigne("422100", "Personnel, avances et acomptes / prêts", "Retenues sur salaires - " + moisTexte, 0m, totalRetenuesDiverses);
            }

            // Net à payer aux salariés
            if (totalNetAPayer > 0m)
            {
                AjouterLigne("422000", "Personnel, rémunérations dues", "Salaires nets à payer - " + moisTexte, 0m, totalNetAPayer);
            }
        }
    }
}
