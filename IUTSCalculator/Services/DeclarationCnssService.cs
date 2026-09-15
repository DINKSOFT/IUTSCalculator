using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Linq;
using System.Threading.Tasks;
using IUTSCalculator.Models;

namespace IUTSCalculator.Services
{
    /// <summary>
    /// Construit la déclaration de cotisations CNSS (module DEC-04).
    ///
    /// Le service ne calcule pas la paie : il lit les bulletins déjà enregistrés, les
    /// rapproche des salariés et du barème en vigueur à la période, et produit la
    /// déclaration. Deux conséquences voulues :
    ///
    ///   • La déclaration dit exactement ce que la paie a retenu — pas une valeur
    ///     recalculée qui pourrait diverger du bulletin remis au salarié.
    ///
    ///   • L'écart entre cette retenue et ce que le barème impose est CALCULÉ et EXPOSÉ,
    ///     ligne par ligne. C'est le contrôle que fait la Caisse ; autant le faire avant
    ///     elle.
    /// </summary>
    public class DeclarationCnssService
    {
        private readonly SQLiteService _sqliteService;
        private readonly BaremeService _baremeService;
        private readonly PeriodePaieService _periodeService;

        public DeclarationCnssService(
            SQLiteService sqliteService,
            BaremeService baremeService,
            PeriodePaieService periodeService)
        {
            if (sqliteService == null) throw new ArgumentNullException("sqliteService");
            if (baremeService == null) throw new ArgumentNullException("baremeService");
            if (periodeService == null) throw new ArgumentNullException("periodeService");

            _sqliteService = sqliteService;
            _baremeService = baremeService;
            _periodeService = periodeService;
        }

        /// <summary>
        /// Détermine la périodicité légale à partir de l'effectif : mensuelle à partir de
        /// 20 salariés, trimestrielle en dessous.
        /// </summary>
        public static PeriodiciteDeclaration PeriodiciteSelonEffectif(int effectif)
        {
            return effectif >= DeclarationCnss.SeuilEffectifMensuel
                ? PeriodiciteDeclaration.Mensuelle
                : PeriodiciteDeclaration.Trimestrielle;
        }

        /// <summary>
        /// Échéance légale de dépôt : 15 jours après la fin du mois déclaré, 30 jours après
        /// la fin du trimestre.
        /// </summary>
        public static DateTime Echeance(DateTime finPeriode, PeriodiciteDeclaration periodicite)
        {
            int jours = periodicite == PeriodiciteDeclaration.Mensuelle ? 15 : 30;
            return finPeriode.Date.AddDays(jours);
        }

        /// <summary>Le trimestre (1 à 4) auquel appartient un mois.</summary>
        public static int Trimestre(int mois)
        {
            return ((mois - 1) / 3) + 1;
        }

        /// <summary>Les trois mois d'un trimestre.</summary>
        public static IEnumerable<int> MoisDuTrimestre(int trimestre)
        {
            int premier = ((trimestre - 1) * 3) + 1;
            return new[] { premier, premier + 1, premier + 2 };
        }

        /// <summary>
        /// Construit la déclaration mensuelle d'un mois.
        /// </summary>
        public Task<DeclarationCnss> ConstruireMensuelleAsync(Dossier dossier, int annee, int mois)
        {
            return ConstruireAsync(dossier, annee, new[] { mois }, PeriodiciteDeclaration.Mensuelle);
        }

        /// <summary>
        /// Construit la déclaration trimestrielle d'un trimestre.
        /// </summary>
        public Task<DeclarationCnss> ConstruireTrimestrielleAsync(Dossier dossier, int annee, int trimestre)
        {
            return ConstruireAsync(dossier, annee, MoisDuTrimestre(trimestre), PeriodiciteDeclaration.Trimestrielle);
        }

        /// <summary>
        /// Cœur du module : une déclaration pour un ensemble de mois d'une même année.
        ///
        /// Les bulletins annuels et les régularisations sont écartés — ils ne correspondent
        /// à aucun mois de cotisation et gonfleraient l'assiette déclarée.
        /// </summary>
        public async Task<DeclarationCnss> ConstruireAsync(
            Dossier dossier, int annee, IEnumerable<int> mois, PeriodiciteDeclaration periodicite)
        {
            if (dossier == null) throw new ArgumentNullException("dossier");

            var listeMois = (mois ?? Enumerable.Empty<int>())
                .Where(m => m >= 1 && m <= 12)
                .Distinct()
                .OrderBy(m => m)
                .ToList();

            if (listeMois.Count == 0)
                throw new ArgumentException("Aucun mois à déclarer.", "mois");

            var declaration = new DeclarationCnss
            {
                RaisonSociale = dossier.RaisonSociale,
                NumeroEmployeurCnss = dossier.NumeroEmployeurCnss,
                Ifu = dossier.IFU,
                Adresse = dossier.Adresse,
                Ville = dossier.Ville,
                Telephone = dossier.Telephone,
                Periodicite = periodicite,
                DebutPeriode = new DateTime(annee, listeMois[0], 1),
                FinPeriode = new DateTime(annee, listeMois[listeMois.Count - 1], 1)
                                .AddMonths(1).AddDays(-1)
            };

            declaration.DateEcheance = Echeance(declaration.FinPeriode, periodicite);

            // Le barème applicable est celui du DÉBUT de la période déclarée : c'est lui
            // qui était en vigueur quand les bulletins ont été établis.
            var baremes = await _baremeService.ChargerAsync();
            declaration.Bareme = BaremeService.BaremeApplicable(baremes, declaration.DebutPeriode);

            foreach (int m in listeMois)
            {
                var periode = await _periodeService.LirePeriodeAsync(annee, m);
                if (periode != null)
                    declaration.PeriodesCouvertes.Add(periode);
            }

            await RemplirLignesAsync(declaration);

            return declaration;
        }

        /// <summary>
        /// Une ligne par salarié et par bulletin du mois, avec l'assiette plafonnée et la
        /// cotisation attendue d'après le barème.
        /// </summary>
        private async Task RemplirLignesAsync(DeclarationCnss declaration)
        {
            decimal plafond = declaration.Bareme != null ? declaration.Bareme.PlafondCnssMensuel : 0m;
            decimal tauxSalarie = declaration.Bareme != null ? declaration.Bareme.TauxCnssSalarie : 0m;
            decimal tauxPatronal = declaration.Bareme != null ? declaration.Bareme.TauxCnssEmployeurTotal : 0m;

            using (var conn = _sqliteService.CreateConnection())
            using (var cmd = conn.CreateCommand())
            {
                // Jointure externe : un bulletin dont la fiche salarié a été supprimée doit
                // rester visible sur la déclaration, sans matricule, plutôt que disparaître
                // sans bruit d'une assiette déjà déclarée.
                cmd.CommandText = @"
                    SELECT c.EmployeeId,
                           c.SalaireBrut,
                           c.CnssEmployee,
                           c.CnssPartPatronale,
                           c.AssietteCnss,
                           e.FirstName,
                           e.LastName,
                           e.NumeroCnss
                    FROM TaxCalculations c
                    LEFT JOIN Employees e ON e.EmployeeId = c.EmployeeId
                    WHERE c.CalculationDate >= @debut
                      AND c.CalculationDate <= @fin
                      AND c.IsAnnual = 0
                      AND c.IsRegularization = 0
                    ORDER BY e.LastName, e.FirstName, c.CalculationDate";

                cmd.Parameters.AddWithValue("@debut", DbValue.DateToDb(declaration.DebutPeriode.Date));
                cmd.Parameters.AddWithValue("@fin",
                    DbValue.DateToDb(declaration.FinPeriode.Date.AddDays(1).AddTicks(-1)));

                using (var reader = (SQLiteDataReader)await cmd.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                    {
                        decimal brut = DbValue.MoneyFromDb(reader["SalaireBrut"]);

                        // Depuis DEC-01, le moteur enregistre l'assiette qu'il a réellement
                        // cotisée. On la reprend telle quelle : elle fait foi. Les bulletins
                        // antérieurs à ce module n'en ont pas — pour eux seulement, on la
                        // reconstitue depuis le brut et le plafond de la période.
                        decimal assietteEnregistree = DbValue.MoneyFromDb(reader["AssietteCnss"]);
                        decimal assiette = assietteEnregistree > 0m
                            ? assietteEnregistree
                            : (plafond > 0m ? Math.Min(brut, plafond) : brut);

                        declaration.Lignes.Add(new LigneDeclarationCnss
                        {
                            Matricule = DbValue.TextFromDb(reader["EmployeeId"]),
                            NumeroCnss = DbValue.TextFromDb(reader["NumeroCnss"]),
                            Nom = DbValue.TextFromDb(reader["LastName"]),
                            Prenom = DbValue.TextFromDb(reader["FirstName"]),
                            SalaireBrut = brut,
                            AssiettePlafonnee = assiette,
                            CotisationSalariale = DbValue.MoneyFromDb(reader["CnssEmployee"]),
                            CotisationPatronale = DbValue.MoneyFromDb(reader["CnssPartPatronale"]),
                            CotisationSalarialeAttendue = Arrondir(assiette * tauxSalarie),
                            CotisationPatronaleAttendue = Arrondir(assiette * tauxPatronal)
                        });
                    }
                }
            }
        }

        /// <summary>
        /// Effectif déclarable d'un mois : le nombre de salariés distincts ayant un bulletin.
        /// Sert à trancher entre déclaration mensuelle et trimestrielle.
        /// </summary>
        public async Task<int> EffectifDuMoisAsync(int annee, int mois)
        {
            var debut = new DateTime(annee, mois, 1);
            var fin = debut.AddMonths(1).AddTicks(-1);

            using (var conn = _sqliteService.CreateConnection())
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText =
                    "SELECT COUNT(DISTINCT EmployeeId) FROM TaxCalculations " +
                    "WHERE CalculationDate >= @debut AND CalculationDate <= @fin " +
                    "  AND IsAnnual = 0 AND IsRegularization = 0";
                cmd.Parameters.AddWithValue("@debut", DbValue.DateToDb(debut));
                cmd.Parameters.AddWithValue("@fin", DbValue.DateToDb(fin));

                return DbValue.IntFromDb(await cmd.ExecuteScalarAsync());
            }
        }

        private static decimal Arrondir(decimal montant)
        {
            return decimal.Round(montant, 0, MidpointRounding.AwayFromZero);
        }
    }
}
