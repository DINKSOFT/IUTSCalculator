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
    /// Service de génération des ordres de virement et des états de paiement (module CPT-02).
    /// 
    /// Restitue fidèlement les montants nets des bulletins arrêtés, identifie les modes de règlement,
    /// contrôle la validité des comptes bancaires et ventile les règlements.
    /// </summary>
    public class OrdreVirementService
    {
        private readonly SQLiteService _sqliteService;
        private readonly PeriodePaieService _periodeService;

        public OrdreVirementService(
            SQLiteService sqliteService,
            PeriodePaieService periodeService)
        {
            if (sqliteService == null) throw new ArgumentNullException(nameof(sqliteService));
            if (periodeService == null) throw new ArgumentNullException(nameof(periodeService));

            _sqliteService = sqliteService;
            _periodeService = periodeService;
        }

        /// <summary>
        /// Construit la pièce d'ordre de virement et l'état des paiements pour un mois donné.
        /// </summary>
        public async Task<OrdreVirement> ConstruireAsync(
            Dossier dossier,
            int annee,
            int mois,
            string filtreBanque = null,
            string filtreMode = null)
        {
            if (dossier == null) throw new ArgumentNullException(nameof(dossier));
            if (mois < 1 || mois > 12) throw new ArgumentOutOfRangeException(nameof(mois));

            var debutPeriode = new DateTime(annee, mois, 1);
            var finPeriode = debutPeriode.AddMonths(1).AddDays(-1);

            var ordre = new OrdreVirement
            {
                NumeroOrdre = string.Format("VIR-{0:yyyy-MM}", finPeriode),
                DateEmission = DateTime.Now,
                DebutPeriode = debutPeriode,
                FinPeriode = finPeriode,
                RaisonSociale = dossier.RaisonSociale,
                Sigle = dossier.Sigle,
                Ifu = dossier.IFU,
                Rccm = dossier.RCCM,
                Adresse = dossier.Adresse,
                Ville = dossier.Ville,
                Telephone = dossier.Telephone,
                BanqueCible = string.IsNullOrWhiteSpace(filtreBanque) ? "Toutes les banques" : filtreBanque
            };

            // Vérifier la clôture de la période
            var periode = await _periodeService.LirePeriodeAsync(annee, mois);
            ordre.EstBrouillon = (periode == null || !periode.EstCloturee);

            var toutesLesLignes = new List<LigneOrdreVirement>();

            using (var conn = _sqliteService.CreateConnection())
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = @"
                    SELECT c.EmployeeId,
                           c.SalaireNetaPayer,
                           c.NetSalaryAfterTax,
                           c.CnssEmployee,
                           c.RetenueSpeciale,
                           c.RetenuesDiverses,
                           c.BulletinArchive,
                           e.FirstName,
                           e.LastName,
                           e.Fonction,
                           e.Banque,
                           e.NumeroCompte,
                           e.ModeReglement
                    FROM TaxCalculations c
                    LEFT JOIN Employees e ON e.EmployeeId = c.EmployeeId
                    WHERE c.CalculationDate >= @debut
                      AND c.CalculationDate <= @fin
                      AND c.IsAnnual = 0
                      AND c.IsRegularization = 0
                    ORDER BY e.LastName, e.FirstName, c.CalculationDate";

                cmd.Parameters.AddWithValue("@debut", DbValue.DateToDb(debutPeriode.Date));
                cmd.Parameters.AddWithValue("@fin", DbValue.DateToDb(finPeriode.Date.AddDays(1).AddTicks(-1)));

                using (var reader = (SQLiteDataReader)await cmd.ExecuteReaderAsync())
                {
                    int index = 1;
                    while (await reader.ReadAsync())
                    {
                        string empId = DbValue.TextFromDb(reader["EmployeeId"]);
                        string nom = DbValue.TextFromDb(reader["LastName"]);
                        string prenom = DbValue.TextFromDb(reader["FirstName"]);
                        string fonction = DbValue.TextFromDb(reader["Fonction"]);

                        string banque = DbValue.TextFromDb(reader["Banque"]);
                        string compte = DbValue.TextFromDb(reader["NumeroCompte"]);
                        string mode = DbValue.TextFromDb(reader["ModeReglement"]);

                        decimal netAPayer = DbValue.MoneyFromDb(reader["SalaireNetaPayer"]);
                        if (netAPayer == 0m)
                        {
                            decimal netApresImpot = DbValue.MoneyFromDb(reader["NetSalaryAfterTax"]);
                            decimal cnss = DbValue.MoneyFromDb(reader["CnssEmployee"]);
                            decimal retSpec = DbValue.MoneyFromDb(reader["RetenueSpeciale"]);
                            decimal retDiv = DbValue.MoneyFromDb(reader["RetenuesDiverses"]);
                            netAPayer = netApresImpot - cnss - retSpec - retDiv;
                        }

                        // Priorité à l'archive du bulletin si disponible
                        string archiveJson = DbValue.TextFromDb(reader["BulletinArchive"]);
                        if (!string.IsNullOrWhiteSpace(archiveJson))
                        {
                            try
                            {
                                var bulletin = JsonSerializer.Deserialize<Bulletin>(archiveJson);
                                if (bulletin != null)
                                {
                                    if (!string.IsNullOrWhiteSpace(bulletin.ModeReglement))
                                        mode = bulletin.ModeReglement;
                                    if (!string.IsNullOrWhiteSpace(bulletin.NumeroCompte))
                                        compte = bulletin.NumeroCompte;
                                    if (bulletin.SalaireNetAPayer > 0m)
                                        netAPayer = bulletin.SalaireNetAPayer;
                                }
                            }
                            catch
                            {
                                // Repli sur les données de l'employé
                            }
                        }

                        if (string.IsNullOrWhiteSpace(mode))
                            mode = "Virement bancaire";

                        toutesLesLignes.Add(new LigneOrdreVirement
                        {
                            Ordre = index++,
                            EmployeeId = empId,
                            Nom = nom,
                            Prenom = prenom,
                            Fonction = fonction,
                            ModeReglement = mode,
                            Banque = banque,
                            NumeroCompte = compte,
                            MontantNet = netAPayer
                        });
                    }
                }
            }

            // Filtrage par banque si demandé
            IEnumerable<LigneOrdreVirement> filtre = toutesLesLignes;

            if (!string.IsNullOrWhiteSpace(filtreBanque) &&
                !string.Equals(filtreBanque, "Toutes les banques", StringComparison.OrdinalIgnoreCase))
            {
                filtre = filtre.Where(l => string.Equals(l.Banque, filtreBanque, StringComparison.OrdinalIgnoreCase));
            }

            // Filtrage par mode de règlement si demandé
            if (!string.IsNullOrWhiteSpace(filtreMode) &&
                !string.Equals(filtreMode, "Tous les modes", StringComparison.OrdinalIgnoreCase))
            {
                if (string.Equals(filtreMode, "Virements bancaires", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(filtreMode, "Virement", StringComparison.OrdinalIgnoreCase))
                {
                    filtre = filtre.Where(l => l.EstVirement);
                }
                else if (string.Equals(filtreMode, "Chèques", StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(filtreMode, "Chèque", StringComparison.OrdinalIgnoreCase))
                {
                    filtre = filtre.Where(l => l.EstCheque);
                }
                else if (string.Equals(filtreMode, "Espèces", StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(filtreMode, "Espèce", StringComparison.OrdinalIgnoreCase))
                {
                    filtre = filtre.Where(l => l.EstEspeces);
                }
                else if (string.Equals(filtreMode, "Mobile Money", StringComparison.OrdinalIgnoreCase))
                {
                    filtre = filtre.Where(l => l.EstMobileMoney);
                }
            }

            int n = 1;
            foreach (var l in filtre)
            {
                l.Ordre = n++;
                ordre.Lignes.Add(l);
            }

            return ordre;
        }

        /// <summary>
        /// Renvoie la liste distincte des banques présentes dans les fiches salariés pour un mois donné.
        /// </summary>
        public async Task<List<string>> ObtenirBanquesDistinctesAsync(int annee, int mois)
        {
            var debut = new DateTime(annee, mois, 1);
            var fin = debut.AddMonths(1).AddDays(-1);

            var banques = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            using (var conn = _sqliteService.CreateConnection())
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = @"
                    SELECT DISTINCT e.Banque
                    FROM TaxCalculations c
                    LEFT JOIN Employees e ON e.EmployeeId = c.EmployeeId
                    WHERE c.CalculationDate >= @debut
                      AND c.CalculationDate <= @fin
                      AND c.IsAnnual = 0
                      AND c.IsRegularization = 0
                      AND e.Banque IS NOT NULL
                      AND TRIM(e.Banque) != ''
                    ORDER BY e.Banque";

                cmd.Parameters.AddWithValue("@debut", DbValue.DateToDb(debut.Date));
                cmd.Parameters.AddWithValue("@fin", DbValue.DateToDb(fin.Date.AddDays(1).AddTicks(-1)));

                using (var reader = (SQLiteDataReader)await cmd.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                    {
                        string b = DbValue.TextFromDb(reader["Banque"]);
                        if (!string.IsNullOrWhiteSpace(b))
                            banques.Add(b.Trim());
                    }
                }
            }

            return banques.OrderBy(b => b).ToList();
        }
    }
}
