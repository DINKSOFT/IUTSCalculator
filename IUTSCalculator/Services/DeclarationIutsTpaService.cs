using System;
using System.Collections.Generic;
using Microsoft.Data.Sqlite;
using System.Linq;
using System.Threading.Tasks;
using IUTSCalculator.Models;

namespace IUTSCalculator.Services
{
    /// <summary>
    /// Construit la déclaration mensuelle IUTS/TPA (module DEC-03).
    ///
    /// Même logique que la déclaration CNSS (DEC-04) : le service ne recalcule pas la
    /// paie, il lit les bulletins déjà enregistrés et les rapproche du barème en vigueur
    /// à la période. L'IUTS retenu est repris tel quel — un salarié n'a qu'un impôt, celui
    /// de son bulletin. Seule la TPA, assise sur un taux unique, se prête à une
    /// comparaison avec le barème.
    /// </summary>
    public class DeclarationIutsTpaService
    {
        private readonly SQLiteService _sqliteService;
        private readonly BaremeService _baremeService;
        private readonly PeriodePaieService _periodeService;

        public DeclarationIutsTpaService(
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
        /// Échéance légale de dépôt et de versement : le 10 du mois suivant celui déclaré.
        /// </summary>
        public static DateTime Echeance(DateTime finPeriode)
        {
            return new DateTime(finPeriode.Year, finPeriode.Month, 1).AddMonths(1).AddDays(9);
        }

        /// <summary>
        /// Construit la déclaration d'un mois.
        /// </summary>
        public async Task<DeclarationIutsTpa> ConstruireAsync(Dossier dossier, int annee, int mois)
        {
            if (dossier == null) throw new ArgumentNullException("dossier");
            if (mois < 1 || mois > 12) throw new ArgumentOutOfRangeException("mois");

            var declaration = new DeclarationIutsTpa
            {
                RaisonSociale = dossier.RaisonSociale,
                Ifu = dossier.IFU,
                RegimeFiscal = dossier.RegimeFiscal,
                Adresse = dossier.Adresse,
                Ville = dossier.Ville,
                Telephone = dossier.Telephone,
                DebutPeriode = new DateTime(annee, mois, 1),
                FinPeriode = new DateTime(annee, mois, 1).AddMonths(1).AddDays(-1)
            };

            declaration.DateEcheance = Echeance(declaration.FinPeriode);

            // Le barème applicable est celui du DÉBUT de la période déclarée : c'est lui
            // qui était en vigueur quand les bulletins ont été établis.
            var baremes = await _baremeService.ChargerAsync();
            declaration.Bareme = BaremeService.BaremeApplicable(baremes, declaration.DebutPeriode);

            declaration.PeriodeCouverte = await _periodeService.LirePeriodeAsync(annee, mois);

            await RemplirLignesAsync(declaration);

            return declaration;
        }

        /// <summary>
        /// Une ligne par salarié ayant un bulletin sur le mois, avec l'IUTS retenu et
        /// l'assiette TPA sous-jacente.
        /// </summary>
        private async Task RemplirLignesAsync(DeclarationIutsTpa declaration)
        {
            using (var conn = _sqliteService.CreerConnexion())
            using (var cmd = conn.CreateCommand())
            {
                // Jointure externe : un bulletin dont la fiche salarié a été supprimée doit
                // rester visible sur la déclaration, sans nom, plutôt que disparaître sans
                // bruit d'un impôt déjà retenu.
                cmd.CommandText = @"
                    SELECT c.EmployeeId,
                           c.SalaireBrut,
                           c.BaseIUTSHorsDeduction,
                           c.IUTSNet,
                           c.AssietteTPA,
                           c.TaxePatronale,
                           e.FirstName,
                           e.LastName
                    FROM TaxCalculations c
                    LEFT JOIN Employees e ON e.EmployeeId = c.EmployeeId
                    WHERE c.CalculationDate >= @debut
                      AND c.CalculationDate <= @fin
                      AND c.IsAnnual = 0
                      AND c.IsRegularization = 0
                    ORDER BY e.LastName, e.FirstName, c.CalculationDate";

                cmd.Parameters.AjouterValeur("@debut", DbValue.DateToDb(declaration.DebutPeriode.Date));
                cmd.Parameters.AjouterValeur("@fin",
                    DbValue.DateToDb(declaration.FinPeriode.Date.AddDays(1).AddTicks(-1)));

                using (var reader = (SqliteDataReader)await cmd.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                    {
                        decimal brut = DbValue.MoneyFromDb(reader["SalaireBrut"]);
                        decimal taxePatronale = DbValue.MoneyFromDb(reader["TaxePatronale"]);

                        // Migration 19 : les bulletins antérieurs n'ont pas d'assiette TPA
                        // enregistrée. Pour ceux-là seulement, on l'approxime par le brut —
                        // comme la déclaration CNSS le fait déjà pour ses bulletins d'avant
                        // la migration 7 — plutôt que de laisser une assiette nulle produire
                        // un écart de comparaison qui n'existe pas réellement.
                        decimal assietteEnregistree = DbValue.MoneyFromDb(reader["AssietteTPA"]);
                        decimal assietteTPA = assietteEnregistree > 0m ? assietteEnregistree : brut;

                        declaration.Lignes.Add(new LigneDeclarationIutsTpa
                        {
                            Matricule = DbValue.TextFromDb(reader["EmployeeId"]),
                            Nom = DbValue.TextFromDb(reader["LastName"]),
                            Prenom = DbValue.TextFromDb(reader["FirstName"]),
                            SalaireBrut = brut,
                            BaseImposable = DbValue.MoneyFromDb(reader["BaseIUTSHorsDeduction"]),
                            IUTSRetenu = DbValue.MoneyFromDb(reader["IUTSNet"]),
                            AssietteTPA = assietteTPA,
                            TaxePatronale = taxePatronale
                        });
                    }
                }
            }
        }
    }
}
