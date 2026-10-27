using System;
using System.Collections.Generic;
using Microsoft.Data.Sqlite;
using System.Linq;
using System.Threading.Tasks;
using IUTSCalculator.Models;

namespace IUTSCalculator.Services
{
    /// <summary>Total des variables d'un salarié sur une période.</summary>
    public class TotalVariablesSalarie
    {
        public string EmployeeId { get; set; }
        public string NomSalarie { get; set; }
        public decimal TotalGains { get; set; }
        public decimal TotalRetenues { get; set; }
        public int NombreLignes { get; set; }

        public decimal Net
        {
            get { return TotalGains - TotalRetenues; }
        }
    }

    /// <summary>
    /// Saisie des variables du mois (module PAI-02).
    ///
    /// Le service garde une règle de conduite : une variable appartient à une PÉRIODE, et
    /// une période clôturée n'en accepte plus. C'est le même verrou que pour les
    /// bulletins (PAI-01) — sans lui, on pourrait ajouter une prime à un mois déjà
    /// déclaré à la DGI, et les totaux figés de la clôture cesseraient d'être vrais.
    ///
    /// <b>Chemin vers le bulletin.</b> <see cref="ApportAsync"/> traduit les variables
    /// d'un salarié en <see cref="ApportVariables"/>, que le moteur sait répartir entre
    /// base imposable, assiette CNSS, net non soumis et retenues. C'est l'unique point
    /// de passage : rien d'autre dans l'application n'a besoin de connaître la structure
    /// des variables.
    /// </summary>
    public class VariablePaieService
    {
        private readonly SQLiteService _sqliteService;

        private const string Colonnes =
            "v.Id, v.Annee, v.Mois, v.EmployeeId, v.CodeRubrique, v.Quantite, v.Montant, " +
            "v.Commentaire, v.DateSaisie";

        public VariablePaieService(SQLiteService sqliteService)
        {
            if (sqliteService == null) throw new ArgumentNullException("sqliteService");
            _sqliteService = sqliteService;
        }

        // ── Lecture ──────────────────────────────────────────────────────────

        /// <summary>
        /// Les variables d'une période, enrichies du nom du salarié et du libellé de la
        /// rubrique. Les deux jointures sont EXTERNES : une rubrique retirée du plan ou un
        /// salarié supprimé ne doit pas faire disparaître une saisie du mois sans un mot.
        /// </summary>
        public async Task<List<VariablePaie>> ChargerAsync(int annee, int mois)
        {
            var liste = new List<VariablePaie>();

            using (var conn = _sqliteService.CreerConnexion())
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText =
                    "SELECT " + Colonnes + ", e.FirstName, e.LastName, r.Libelle, r.Sens " +
                    "FROM VariablesPaie v " +
                    "LEFT JOIN Employees e ON e.EmployeeId = v.EmployeeId " +
                    "LEFT JOIN Rubriques r ON r.Code = v.CodeRubrique " +
                    "WHERE v.Annee = @a AND v.Mois = @m " +
                    "ORDER BY e.LastName, e.FirstName, v.CodeRubrique";
                cmd.Parameters.AjouterValeur("@a", annee);
                cmd.Parameters.AjouterValeur("@m", mois);

                using (var reader = (SqliteDataReader)await cmd.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                        liste.Add(Lire(reader));
                }
            }

            return liste;
        }

        private static VariablePaie Lire(SqliteDataReader reader)
        {
            string nom = DbValue.TextFromDb(reader["LastName"]);
            string prenom = DbValue.TextFromDb(reader["FirstName"]);
            string complet = string.Format("{0} {1}", nom, prenom).Trim();

            return new VariablePaie
            {
                Id = DbValue.IntFromDb(reader["Id"]),
                Annee = DbValue.IntFromDb(reader["Annee"]),
                Mois = DbValue.IntFromDb(reader["Mois"]),
                EmployeeId = DbValue.TextFromDb(reader["EmployeeId"]),
                NomSalarie = string.IsNullOrWhiteSpace(complet) ? "Salarié supprimé" : complet,
                CodeRubrique = DbValue.TextFromDb(reader["CodeRubrique"]),
                LibelleRubrique = DbValue.TextFromDb(reader["Libelle"]),
                Sens = (SensRubrique)DbValue.IntFromDb(reader["Sens"]),
                Quantite = DbValue.DecimalFromDb(reader["Quantite"]),
                Montant = DbValue.MoneyFromDb(reader["Montant"]),
                Commentaire = DbValue.TextFromDb(reader["Commentaire"]),
                DateSaisie = DbValue.DateFromDb(reader["DateSaisie"], DateTime.Now)
            };
        }

        /// <summary>
        /// Totaux par salarié : ce que la période ajoute ou retire à chacun. Sert à
        /// l'affichage et aux contrôles ; le calcul du bulletin passe par
        /// <see cref="ApportAsync"/>, qui distingue les traitements fiscaux.
        /// </summary>
        public async Task<List<TotalVariablesSalarie>> TotauxParSalarieAsync(int annee, int mois)
        {
            var variables = await ChargerAsync(annee, mois);

            return variables
                .GroupBy(v => v.EmployeeId)
                .Select(g => new TotalVariablesSalarie
                {
                    EmployeeId = g.Key,
                    NomSalarie = g.First().NomSalarie,
                    NombreLignes = g.Count(),
                    TotalGains = g.Where(v => v.Sens == SensRubrique.Gain).Sum(v => v.Montant),
                    TotalRetenues = g.Where(v => v.Sens == SensRubrique.Retenue).Sum(v => v.Montant)
                })
                .OrderBy(t => t.NomSalarie)
                .ToList();
        }

        /// <summary>
        /// Apport des variables d'un salarié sur une période, prêt pour le moteur de
        /// calcul. C'est l'unique point d'entrée du branchement : le bulletin y passe,
        /// et rien d'autre n'a besoin de connaître la structure des variables.
        ///
        /// Renvoie <see cref="ApportVariables.Aucun"/> quand il n'y a rien : le bulletin
        /// est alors identique à ce qu'il était avant ce module.
        /// </summary>
        public async Task<ApportVariables> ApportAsync(
            int annee, int mois, string employeeId, IEnumerable<Rubrique> planDePaie)
        {
            if (string.IsNullOrWhiteSpace(employeeId))
                return ApportVariables.Aucun();

            var variables = await ChargerAsync(annee, mois);

            var duSalarie = variables
                .Where(v => string.Equals(v.EmployeeId, employeeId, StringComparison.OrdinalIgnoreCase))
                .ToList();

            return ApportVariables.Depuis(duSalarie, planDePaie);
        }

        /// <summary>
        /// Apports de TOUS les salariés d'une période, en une seule lecture. À utiliser
        /// pour un traitement en lot : appeler <see cref="ApportAsync"/> dans une boucle
        /// relirait la table autant de fois qu'il y a de salariés.
        /// </summary>
        public async Task<Dictionary<string, ApportVariables>> ApportsAsync(
            int annee, int mois, IEnumerable<Rubrique> planDePaie)
        {
            var variables = await ChargerAsync(annee, mois);

            return variables
                .Where(v => !string.IsNullOrWhiteSpace(v.EmployeeId))
                .GroupBy(v => v.EmployeeId, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    g => g.Key,
                    g => ApportVariables.Depuis(g.ToList(), planDePaie),
                    StringComparer.OrdinalIgnoreCase);
        }

        // ── Écriture ─────────────────────────────────────────────────────────

        /// <summary>
        /// Remplace en bloc les variables d'une période. Le tout dans une transaction : une
        /// saisie à moitié écrite donnerait des bulletins faux sur une partie de l'effectif.
        ///
        /// La période doit être ouverte, sans quoi rien n'est écrit.
        /// </summary>
        public async Task EnregistrerToutAsync(int annee, int mois, IEnumerable<VariablePaie> variables)
        {
            var liste = (variables ?? Enumerable.Empty<VariablePaie>())
                .Where(v => v != null && v.EstValide)
                .ToList();

            using (var conn = _sqliteService.CreerConnexion())
            {
                await VerifierPeriodeOuverteAsync(conn, annee, mois, null);
                await VerifierHeuresSupSansDoublonAsync(conn, annee, mois, liste);

                using (var tx = conn.BeginTransaction())
                {
                    try
                    {
                        using (var cmd = conn.CreateCommand())
                        {
                            cmd.Transaction = tx;
                            cmd.CommandText = "DELETE FROM VariablesPaie WHERE Annee = @a AND Mois = @m;";
                            cmd.Parameters.AjouterValeur("@a", annee);
                            cmd.Parameters.AjouterValeur("@m", mois);
                            await cmd.ExecuteNonQueryAsync();
                        }

                        foreach (var variable in liste)
                        {
                            using (var cmd = conn.CreateCommand())
                            {
                                cmd.Transaction = tx;
                                cmd.CommandText =
                                    "INSERT INTO VariablesPaie " +
                                    "(Annee, Mois, EmployeeId, CodeRubrique, Quantite, Montant, Commentaire, DateSaisie) " +
                                    "VALUES (@a, @m, @employe, @code, @quantite, @montant, @commentaire, @saisie);";

                                cmd.Parameters.AjouterValeur("@a", annee);
                                cmd.Parameters.AjouterValeur("@m", mois);
                                cmd.Parameters.AjouterValeur("@employe", variable.EmployeeId ?? "");
                                cmd.Parameters.AjouterValeur("@code", (variable.CodeRubrique ?? "").Trim().ToUpperInvariant());
                                cmd.Parameters.AjouterValeur("@quantite", DbValue.DecimalToDb(variable.Quantite));
                                cmd.Parameters.AjouterValeur("@montant", DbValue.MoneyToDb(variable.Montant));
                                cmd.Parameters.AjouterValeur("@commentaire", variable.Commentaire ?? "");
                                cmd.Parameters.AjouterValeur("@saisie", DbValue.DateToDb(
                                    variable.DateSaisie == default(DateTime) ? DateTime.Now : variable.DateSaisie));

                                await cmd.ExecuteNonQueryAsync();
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
            }
        }

        /// <summary>
        /// Recopie les variables d'une période vers une autre. Le geste le plus fréquent
        /// d'un mois de paie : la plupart des primes et retenues se répètent à l'identique,
        /// et les ressaisir à la main est la première source d'erreur.
        ///
        /// Les lignes existantes de la période cible sont REMPLACÉES ; l'écran prévient.
        /// </summary>
        public async Task<int> ReporterAsync(int anneeSource, int moisSource, int anneeCible, int moisCible)
        {
            if (anneeSource == anneeCible && moisSource == moisCible)
                throw new InvalidOperationException("La période source et la période cible sont identiques.");

            var source = await ChargerAsync(anneeSource, moisSource);
            if (source.Count == 0)
                return 0;

            foreach (var variable in source)
            {
                variable.Id = 0;
                variable.Annee = anneeCible;
                variable.Mois = moisCible;
                variable.DateSaisie = DateTime.Now;
            }

            await EnregistrerToutAsync(anneeCible, moisCible, source);
            return source.Count;
        }

        /// <summary>
        /// Refuse l'écriture si la période est clôturée. Même raisonnement que le verrou
        /// des bulletins : une clôture qui n'empêche rien n'est qu'une étiquette.
        /// </summary>
        /// <summary>Code de la rubrique de saisie libre des heures supplémentaires.</summary>
        public const string CodeHeuresSupLibre = "HSUP";

        /// <summary>
        /// Refuse une variable HSUP pour un salarié dont les heures supplémentaires du
        /// même mois sont déjà saisies dans le module « Heures supplémentaires » : les deux
        /// circuits s'additionnaient sur le bulletin et les heures étaient payées deux fois
        /// (défaut n° 23 du guide). Un seul circuit par salarié et par mois.
        /// </summary>
        private static async Task VerifierHeuresSupSansDoublonAsync(
            SqliteConnection conn, int annee, int mois, IEnumerable<VariablePaie> variables)
        {
            var salaries = variables
                .Where(v => string.Equals((v.CodeRubrique ?? "").Trim(), CodeHeuresSupLibre, StringComparison.OrdinalIgnoreCase))
                .Select(v => v.EmployeeId)
                .Where(e => !string.IsNullOrWhiteSpace(e))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (salaries.Count == 0) return;

            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='HeuresSupplementaires';";
                if (Convert.ToInt32(await cmd.ExecuteScalarAsync()) == 0) return;
            }

            var enDouble = new List<string>();
            foreach (var employe in salaries)
            {
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT COUNT(*) FROM HeuresSupplementaires " +
                                      "WHERE Annee = @a AND Mois = @m AND EmployeeId = @e AND MontantTotal > 0;";
                    cmd.Parameters.AjouterValeur("@a", annee);
                    cmd.Parameters.AjouterValeur("@m", mois);
                    cmd.Parameters.AjouterValeur("@e", employe);
                    if (Convert.ToInt32(await cmd.ExecuteScalarAsync()) > 0)
                        enDouble.Add(employe);
                }
            }

            if (enDouble.Count > 0)
            {
                throw new InvalidOperationException(string.Format(
                    "Variable HSUP refusée pour {0} : les heures supplémentaires de {1:00}/{2} sont déjà saisies dans " +
                    "l'écran « Heures supplémentaires ». Utilisez un seul circuit par salarié et par mois, sinon " +
                    "les heures seraient payées deux fois. Rien n'a été enregistré.",
                    string.Join(", ", enDouble), mois, annee));
            }
        }

        private static async Task VerifierPeriodeOuverteAsync(
            SqliteConnection conn, int annee, int mois, SqliteTransaction tx)
        {
            var premierJour = new DateTime(annee, mois, 1);

            if (await PeriodePaieService.EstClotureeAsync(conn, premierJour, tx))
            {
                throw new InvalidOperationException(string.Format(
                    "La période de {0:MM/yyyy} est clôturée : ses variables ne peuvent plus être " +
                    "modifiées. Rouvrez-la depuis l'écran « Périodes de paie » si la correction " +
                    "est justifiée.", premierJour));
            }
        }

        /// <summary>Vrai si la période accepte encore des saisies.</summary>
        public async Task<bool> PeriodeOuverteAsync(int annee, int mois)
        {
            using (var conn = _sqliteService.CreerConnexion())
            {
                return !await PeriodePaieService.EstClotureeAsync(conn, new DateTime(annee, mois, 1));
            }
        }

        /// <summary>
        /// Contrôles de cohérence d'une saisie, dans l'ordre où ils comptent.
        ///
        /// Le plus utile est le dernier : une rubrique absente du plan de paie ne sera
        /// jamais calculée. La ligne serait saisie, enregistrée, totalisée à l'écran — et
        /// n'apparaîtrait sur aucun bulletin.
        /// </summary>
        public static List<string> Anomalies(
            IEnumerable<VariablePaie> variables, IEnumerable<Rubrique> planDePaie)
        {
            var anomalies = new List<string>();
            var liste = (variables ?? Enumerable.Empty<VariablePaie>()).ToList();

            if (liste.Count == 0)
                return anomalies;

            var codesConnus = new HashSet<string>(
                (planDePaie ?? Enumerable.Empty<Rubrique>())
                    .Where(r => r.Active)
                    .Select(r => (r.Code ?? "").ToUpperInvariant()));

            var inconnues = liste
                .Select(v => (v.CodeRubrique ?? "").ToUpperInvariant())
                .Where(c => !string.IsNullOrWhiteSpace(c) && !codesConnus.Contains(c))
                .Distinct()
                .ToList();

            if (inconnues.Count > 0)
            {
                anomalies.Add(string.Format(
                    "Rubrique(s) absente(s) ou inactive(s) du plan de paie : {0}. Ces lignes ne " +
                    "seront calculées sur aucun bulletin.", string.Join(", ", inconnues)));
            }

            var doublons = liste
                .Where(v => !string.IsNullOrWhiteSpace(v.EmployeeId))
                .GroupBy(v => new { v.EmployeeId, Code = (v.CodeRubrique ?? "").ToUpperInvariant() })
                .Where(g => g.Count() > 1)
                .ToList();

            if (doublons.Count > 0)
            {
                anomalies.Add(string.Format(
                    "{0} salarié(s) portent deux fois la même rubrique. Les montants s'ajouteront : " +
                    "vérifiez qu'il ne s'agit pas d'une double saisie.", doublons.Count));
            }

            var sansMontant = liste.Count(v => v.Montant == 0m && v.Quantite != 0m);
            if (sansMontant > 0)
            {
                anomalies.Add(string.Format(
                    "{0} ligne(s) portent une quantité sans montant. Tant que le moteur ne " +
                    "valorise pas les quantités, ces lignes resteront sans effet.", sansMontant));
            }

            return anomalies;
        }
    }
}
