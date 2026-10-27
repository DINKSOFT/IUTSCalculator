using System;
using System.Collections.Generic;
using Microsoft.Data.Sqlite;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;

namespace IUTSCalculator.Services
{
    /// <summary>Une ligne de <c>TaxCalculations</c> dont la ventilation CNSS est à rattraper.</summary>
    public class LigneRattrapageCnss
    {
        public string Id { get; set; }
        public string EmployeeId { get; set; }
        public string NomComplet { get; set; }
        public DateTime? CalculationDate { get; set; }

        /// <summary>Ce qui était déjà en base, et sert de référence au rattrapage.</summary>
        public decimal CnssPartPatronale { get; set; }
        public decimal CnssEmployee { get; set; }

        /// <summary>Ce que le rattrapage propose d'écrire.</summary>
        public decimal AssietteCnss { get; set; }
        public decimal CnssPrestationsFamiliales { get; set; }
        public decimal CnssRisquesProfessionnels { get; set; }
        public decimal CnssPensionsPatronal { get; set; }

        /// <summary>Vrai si les deux façons de déduire l'assiette (part patronale et part
        /// salariale) ne concordent pas : la ligne n'est alors PAS rattrapée automatiquement,
        /// elle est seulement signalée pour un examen manuel.</summary>
        public bool Suspecte { get; set; }
        public string RaisonSuspicion { get; set; }
    }

    /// <summary>Résultat d'une analyse ou d'une application du rattrapage.</summary>
    public class RapportRattrapageCnss
    {
        /// <summary>Lignes que le rattrapage a corrigées (ou corrigerait, en analyse seule).</summary>
        public List<LigneRattrapageCnss> ARattraper { get; } = new List<LigneRattrapageCnss>();

        /// <summary>Lignes trouvées non ventilées mais dont les deux parts ne concordent pas :
        /// laissées de côté, à traiter à la main.</summary>
        public List<LigneRattrapageCnss> Suspectes { get; } = new List<LigneRattrapageCnss>();

        /// <summary>Vrai si <see cref="RattrapageCnssService.AppliquerAsync"/> a réellement
        /// écrit en base ; faux pour un rapport d'analyse seule.</summary>
        public bool Applique { get; set; }

        /// <summary>Chemin de la copie de sauvegarde déposée avant écriture, si applicable.</summary>
        public string CopieSauvegarde { get; set; }
    }

    /// <summary>
    /// Rattrapage de la ventilation CNSS par branche sur les bulletins enregistrés AVANT la
    /// migration 7 (dette connue n°3).
    ///
    /// La migration 7 a ajouté quatre colonnes à <c>TaxCalculations</c>
    /// (<c>AssietteCnss</c>, <c>CnssPrestationsFamiliales</c>, <c>CnssRisquesProfessionnels</c>,
    /// <c>CnssPensionsPatronal</c>), toutes à zéro pour les lignes déjà enregistrées — la
    /// part patronale GLOBALE (<c>CnssPartPatronale</c>) existait déjà et reste juste, mais
    /// sa ventilation par branche, exigée sur le bordereau CNSS, manque sur ces bulletins-là.
    ///
    /// <b>Comment la ventilation est reconstituée, sans deviner.</b> Ces lignes datent
    /// toutes d'avant les barèmes datés (DEC-01/DEC-02) : elles ont donc nécessairement été
    /// calculées avec l'unique taux patronal global qui existait alors, 16 % pile
    /// (6 % + 1,5 % + 8,5 %, les valeurs historiques de <see cref="ParametresSociaux"/>).
    /// L'assiette qui a produit <c>CnssPartPatronale</c> se retrouve donc exactement par
    /// division : <c>assiette = CnssPartPatronale / 0,16</c>. Comme <c>CnssEmployee</c> (la
    /// part salariale, 5,5 %) existait déjà avant la migration 7 et n'a jamais été touchée,
    /// la même assiette doit AUSSI valoir <c>CnssEmployee / 0,055</c> — une seconde façon,
    /// indépendante, de retrouver le même nombre. Une ligne où les deux ne concordent pas
    /// (à la faveur d'une donnée modifiée à la main, d'un import, d'un cas qui ne suit pas
    /// le schéma attendu) n'est PAS rattrapée automatiquement : elle est signalée à part,
    /// pour un examen au cas par cas.
    ///
    /// <b>Portée volontairement stricte.</b> Ce service ne touche NI <c>SalaireBrut</c> NI
    /// aucun autre champ déjà correct — uniquement les quatre colonnes de ventilation, et
    /// seulement sur les lignes où elles sont encore à zéro alors qu'une part patronale
    /// existe (signe certain d'une ligne jamais ventilée : si la part patronale est nulle,
    /// l'assiette l'est aussi, et il n'y a rien à rattraper).
    ///
    /// <b>Aucun arrondi par branche</b>, comme dans <see cref="TaxCalculatorService"/> : la
    /// somme des trois branches reconstitue exactement <c>CnssPartPatronale</c>.
    ///
    /// <b>Ce service n'est appelé depuis AUCUN écran ni AUCUNE migration automatique.</b> Le
    /// rattrapage touche des données de paie déjà déclarées : <see cref="AnalyserAsync"/>
    /// (lecture seule, sans écrire) doit être exécuté et son rapport relu avant d'envisager
    /// <see cref="AppliquerAsync"/> — qui dépose lui-même une copie de sauvegarde du fichier
    /// avant d'écrire quoi que ce soit, sur le même principe que les migrations de schéma.
    /// Le brancher sur un bouton d'écran est un choix délibéré, à faire séparément, pas une
    /// omission.
    /// </summary>
    public class RattrapageCnssService
    {
        private readonly SQLiteService _sqliteService;

        // Valeurs historiques de ParametresSociaux.Historiques() — celles, et uniquement
        // celles, qui pouvaient avoir produit une ligne calculée avant les barèmes datés.
        private const decimal TauxPrestationsFamiliales = 0.06m;
        private const decimal TauxRisquesProfessionnels = 0.015m;
        private const decimal TauxPensionsPatronal = 0.085m;
        private const decimal TauxPatronalTotal =
            TauxPrestationsFamiliales + TauxRisquesProfessionnels + TauxPensionsPatronal; // 0,16
        private const decimal TauxSalarieHistorique = 0.055m;

        // Tolérance de rapprochement entre les deux façons de déduire l'assiette. Les deux
        // proviennent du même calcul sans arrondi intermédiaire ; un centime couvre le seul
        // arrondi réel, celui de MoneyToDb au moment du stockage initial.
        private const decimal ToleranceFcfa = 1m;

        public RattrapageCnssService(SQLiteService sqliteService)
        {
            if (sqliteService == null) throw new ArgumentNullException("sqliteService");
            _sqliteService = sqliteService;
        }

        /// <summary>
        /// Analyse SANS RIEN ÉCRIRE : identifie les lignes à rattraper et celles qui, faute
        /// de concordance entre les deux parts, doivent être examinées à la main.
        /// </summary>
        public async Task<RapportRattrapageCnss> AnalyserAsync()
        {
            var rapport = new RapportRattrapageCnss();

            using (var conn = _sqliteService.CreerConnexion())
            using (var cmd = conn.CreateCommand())
            {
                // CnssPartPatronale > 0 avec les quatre colonnes de ventilation encore à
                // zéro : la signature exacte d'une ligne jamais ventilée. Si la part
                // patronale est nulle, l'assiette l'est aussi — rien à rattraper.
                cmd.CommandText = @"
                    SELECT c.Id, c.EmployeeId, c.CalculationDate, c.CnssPartPatronale, c.CnssEmployee,
                           e.FirstName, e.LastName
                    FROM TaxCalculations c
                    LEFT JOIN Employees e ON e.EmployeeId = c.EmployeeId
                    WHERE c.CnssPartPatronale > 0
                      AND c.AssietteCnss = 0
                      AND c.CnssPrestationsFamiliales = 0
                      AND c.CnssRisquesProfessionnels = 0
                      AND c.CnssPensionsPatronal = 0
                    ORDER BY c.CalculationDate, e.LastName, e.FirstName";

                using (var reader = (SqliteDataReader)await cmd.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                    {
                        decimal cnssPatronale = DbValue.MoneyFromDb(reader["CnssPartPatronale"]);
                        decimal cnssSalarie = DbValue.MoneyFromDb(reader["CnssEmployee"]);

                        decimal assietteDepuisPatronale = cnssPatronale / TauxPatronalTotal;

                        var ligne = new LigneRattrapageCnss
                        {
                            Id = DbValue.TextFromDb(reader["Id"]),
                            EmployeeId = DbValue.TextFromDb(reader["EmployeeId"]),
                            NomComplet = string.Format(
                                "{0} {1}",
                                DbValue.TextFromDb(reader["LastName"]),
                                DbValue.TextFromDb(reader["FirstName"])).Trim(),
                            CalculationDate = DbValue.DateNullableFromDb(reader["CalculationDate"]),
                            CnssPartPatronale = cnssPatronale,
                            CnssEmployee = cnssSalarie
                        };

                        if (cnssSalarie > 0m)
                        {
                            decimal assietteDepuisSalarie = cnssSalarie / TauxSalarieHistorique;
                            if (Math.Abs(assietteDepuisPatronale - assietteDepuisSalarie) > ToleranceFcfa)
                            {
                                ligne.Suspecte = true;
                                ligne.RaisonSuspicion = string.Format(CultureInfo.InvariantCulture,
                                    "Assiette déduite de la part patronale ({0:N0} FCFA) et de la part salariale " +
                                    "({1:N0} FCFA) en désaccord — ligne laissée de côté, à vérifier à la main.",
                                    assietteDepuisPatronale, assietteDepuisSalarie);
                                rapport.Suspectes.Add(ligne);
                                continue;
                            }
                        }

                        ligne.AssietteCnss = assietteDepuisPatronale;
                        ligne.CnssPrestationsFamiliales = assietteDepuisPatronale * TauxPrestationsFamiliales;
                        ligne.CnssRisquesProfessionnels = assietteDepuisPatronale * TauxRisquesProfessionnels;
                        ligne.CnssPensionsPatronal = assietteDepuisPatronale * TauxPensionsPatronal;

                        rapport.ARattraper.Add(ligne);
                    }
                }
            }

            return rapport;
        }

        /// <summary>
        /// Écrit en base les lignes que <see cref="AnalyserAsync"/> a identifiées, après
        /// avoir déposé une copie de sauvegarde du fichier — même principe qu'avant une
        /// migration de schéma. Les lignes suspectes ne sont jamais écrites automatiquement.
        /// </summary>
        public async Task<RapportRattrapageCnss> AppliquerAsync()
        {
            var rapport = await AnalyserAsync();

            if (rapport.ARattraper.Count == 0)
            {
                rapport.Applique = true;
                return rapport;
            }

            rapport.CopieSauvegarde = DeposerCopieSauvegarde();

            using (var conn = _sqliteService.CreerConnexion())
            using (var tx = conn.BeginTransaction())
            {
                foreach (var ligne in rapport.ARattraper)
                {
                    using (var cmd = conn.CreateCommand())
                    {
                        cmd.Transaction = tx;
                        cmd.CommandText = @"
                            UPDATE TaxCalculations
                            SET AssietteCnss = @assiette,
                                CnssPrestationsFamiliales = @pf,
                                CnssRisquesProfessionnels = @rp,
                                CnssPensionsPatronal = @pp
                            WHERE Id = @id";

                        cmd.Parameters.AjouterValeur("@assiette", DbValue.MoneyToDb(ligne.AssietteCnss));
                        cmd.Parameters.AjouterValeur("@pf", DbValue.MoneyToDb(ligne.CnssPrestationsFamiliales));
                        cmd.Parameters.AjouterValeur("@rp", DbValue.MoneyToDb(ligne.CnssRisquesProfessionnels));
                        cmd.Parameters.AjouterValeur("@pp", DbValue.MoneyToDb(ligne.CnssPensionsPatronal));
                        cmd.Parameters.AjouterValeur("@id", ligne.Id);

                        await cmd.ExecuteNonQueryAsync();
                    }
                }

                tx.Commit();
            }

            rapport.Applique = true;
            return rapport;
        }

        private string DeposerCopieSauvegarde()
        {
            string source = _sqliteService.DatabasePath;
            if (!File.Exists(source))
                return null;

            string copie = string.Format(
                CultureInfo.InvariantCulture,
                "{0}.avant-rattrapage-cnss-{1:yyyyMMdd-HHmmss}.bak",
                source, DateTime.Now);

            File.Copy(source, copie, overwrite: false);
            return copie;
        }
    }
}
