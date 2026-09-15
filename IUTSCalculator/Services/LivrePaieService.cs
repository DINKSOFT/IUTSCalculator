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
    /// Construit le livre de paie mensuel (module ETA-01).
    ///
    /// Le service ne calcule pas la paie : il lit les bulletins déjà enregistrés,
    /// les rapproche des salariés et du plan de paie, et produit l'état. Même
    /// principe que <see cref="DeclarationCnssService"/> et
    /// <see cref="DeclarationIutsTpaService"/> : le livre dit exactement ce que
    /// la paie a retenu — pas une valeur recalculée.
    /// </summary>
    public class LivrePaieService
    {
        private readonly SQLiteService _sqliteService;
        private readonly PeriodePaieService _periodeService;
        private readonly RubriqueService _rubriqueService;

        public LivrePaieService(
            SQLiteService sqliteService,
            PeriodePaieService periodeService,
            RubriqueService rubriqueService)
        {
            if (sqliteService == null) throw new ArgumentNullException("sqliteService");
            if (periodeService == null) throw new ArgumentNullException("periodeService");
            if (rubriqueService == null) throw new ArgumentNullException("rubriqueService");

            _sqliteService = sqliteService;
            _periodeService = periodeService;
            _rubriqueService = rubriqueService;
        }

        /// <summary>
        /// Construit le livre de paie d'un mois.
        /// </summary>
        public async Task<LivrePaie> ConstruireAsync(Dossier dossier, int annee, int mois)
        {
            if (dossier == null) throw new ArgumentNullException("dossier");
            if (mois < 1 || mois > 12) throw new ArgumentOutOfRangeException("mois");

            var debutPeriode = new DateTime(annee, mois, 1);
            var finPeriode = debutPeriode.AddMonths(1).AddDays(-1);

            var livre = new LivrePaie
            {
                RaisonSociale = dossier.RaisonSociale,
                Ifu = dossier.IFU,
                Rccm = dossier.RCCM,
                Adresse = dossier.Adresse,
                Ville = dossier.Ville,
                Telephone = dossier.Telephone,
                DebutPeriode = debutPeriode,
                FinPeriode = finPeriode
            };

            // Vérifier si la période est clôturée
            var periode = await _periodeService.LirePeriodeAsync(annee, mois);
            livre.EstBrouillon = (periode == null || !periode.EstCloturee);

            // Charger les rubriques pour ordonner les colonnes dynamiques
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

            // Lire les bulletins du mois
            await RemplirLignesAsync(livre, rubriqueParCode);

            return livre;
        }

        /// <summary>
        /// Une ligne par salarié, depuis les TaxCalculations et les BulletinArchive.
        /// </summary>
        private async Task RemplirLignesAsync(
            LivrePaie livre, Dictionary<string, Rubrique> rubriqueParCode)
        {
            // Ensemble des codes de rubriques dynamiques rencontrés
            var codesRubriques = new Dictionary<string, ColonneRubriqueDynamique>(
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
                           e.Category,
                           e.Fonction,
                           e.DateEntree,
                           e.NumeroCnss,
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
                    DbValue.DateToDb(livre.DebutPeriode.Date));
                cmd.Parameters.AddWithValue("@fin",
                    DbValue.DateToDb(livre.FinPeriode.Date.AddDays(1).AddTicks(-1)));

                using (var reader = (SQLiteDataReader)await cmd.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                    {
                        decimal brut = DbValue.MoneyFromDb(reader["SalaireBrut"]);
                        decimal cnssEmployee = DbValue.MoneyFromDb(reader["CnssEmployee"]);
                        decimal cnssPatron = DbValue.MoneyFromDb(reader["CnssPartPatronale"]);
                        decimal iuts = DbValue.MoneyFromDb(reader["IUTSNet"]);
                        decimal retenueSpeciale = DbValue.MoneyFromDb(reader["RetenueSpeciale"]);
                        decimal retenuesDiverses = DbValue.MoneyFromDb(reader["RetenuesDiverses"]);
                        decimal gainsVariables = DbValue.MoneyFromDb(reader["GainsVariables"]);
                        decimal tpa = DbValue.MoneyFromDb(reader["TaxePatronale"]);
                        decimal netAPayer = DbValue.MoneyFromDb(reader["SalaireNetaPayer"]);

                        // Valeurs par défaut depuis la fiche salarié (si l'archive n'est pas disponible)
                        decimal salaireDeBase = DbValue.MoneyFromDb(reader["BaseSalary"]);
                        decimal indLogement = DbValue.MoneyFromDb(reader["HousingAllowance"]);
                        decimal indTransport = DbValue.MoneyFromDb(reader["TransportAllowance"]);
                        decimal indFonction = DbValue.MoneyFromDb(reader["FunctionAllowance"]);
                        decimal indSujetion = DbValue.MoneyFromDb(reader["SujetionAllowance"]);
                        decimal retenueAbsences = 0m;
                        var montantsParRubrique = new Dictionary<string, decimal>(
                            StringComparer.OrdinalIgnoreCase);

                        // Tenter de lire le BulletinArchive pour obtenir le détail exact édité
                        string archiveJson = DbValue.TextFromDb(reader["BulletinArchive"]);
                        if (!string.IsNullOrWhiteSpace(archiveJson))
                        {
                            try
                            {
                                var bulletin = JsonSerializer.Deserialize<Bulletin>(archiveJson);
                                if (bulletin != null)
                                {
                                    salaireDeBase = bulletin.SalaireDeBase;
                                    indLogement = bulletin.IndemniteLogement;
                                    indTransport = bulletin.IndemniteTransport;
                                    indFonction = bulletin.IndemniteFonction;
                                    indSujetion = bulletin.IndemniteSujetion;
                                    retenueAbsences = bulletin.RetenueAbsences;

                                    // Lignes variables : les rubriques dynamiques
                                    if (bulletin.LignesVariables != null)
                                    {
                                        foreach (var lv in bulletin.LignesVariables)
                                        {
                                            string code = lv.Code ?? "";
                                            if (string.IsNullOrWhiteSpace(code)) continue;

                                            decimal montant = lv.EstRetenue ? -lv.Montant : lv.Montant;

                                            if (montantsParRubrique.ContainsKey(code))
                                                montantsParRubrique[code] += montant;
                                            else
                                                montantsParRubrique[code] = montant;

                                            // Enregistrer la colonne dynamique
                                            if (!codesRubriques.ContainsKey(code))
                                            {
                                                int ordre = 999;
                                                bool estRetenue = lv.EstRetenue;
                                                string libelle = lv.Libelle ?? code;

                                                Rubrique rub;
                                                if (rubriqueParCode.TryGetValue(code, out rub))
                                                {
                                                    ordre = rub.Ordre;
                                                    libelle = rub.Libelle;
                                                    estRetenue = rub.Sens == SensRubrique.Retenue;
                                                }

                                                codesRubriques[code] = new ColonneRubriqueDynamique
                                                {
                                                    Code = code,
                                                    Libelle = libelle,
                                                    Ordre = ordre,
                                                    EstRetenue = estRetenue
                                                };
                                            }
                                        }
                                    }
                                }
                            }
                            catch
                            {
                                // Archive illisible : on se rabat sur les colonnes fixes.
                            }
                        }

                        livre.Lignes.Add(new LigneLivrePaie
                        {
                            Matricule = DbValue.TextFromDb(reader["EmployeeId"]),
                            Nom = DbValue.TextFromDb(reader["LastName"]),
                            Prenom = DbValue.TextFromDb(reader["FirstName"]),
                            Categorie = DbValue.TextFromDb(reader["Category"]),
                            Emploi = DbValue.TextFromDb(reader["Fonction"]),
                            DateEntree = DbValue.DateNullableFromDb(reader["DateEntree"]),
                            SalaireDeBase = salaireDeBase,
                            IndemniteLogement = indLogement,
                            IndemniteTransport = indTransport,
                            IndemniteFonction = indFonction,
                            IndemniteSujetion = indSujetion,
                            GainsVariables = gainsVariables,
                            RetenueAbsences = retenueAbsences,
                            SalaireBrut = brut,
                            CnssSalariale = cnssEmployee,
                            CnssPatronale = cnssPatron,
                            Iuts = iuts,
                            RetenueSpeciale = retenueSpeciale,
                            RetenuesDiverses = retenuesDiverses,
                            NetAPayer = netAPayer,
                            Tpa = tpa,
                            MontantsParRubrique = montantsParRubrique
                        });
                    }
                }
            }

            // Ordonner les colonnes dynamiques par ordre du plan de paie
            livre.ColonnesDynamiques = codesRubriques.Values
                .OrderBy(c => c.Ordre)
                .ThenBy(c => c.Code, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
    }
}
