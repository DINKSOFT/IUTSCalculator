using System;
using System.Collections.Generic;
using Microsoft.Data.Sqlite;
using System.Threading.Tasks;
using IUTSCalculator.Models;

namespace IUTSCalculator.Services
{
    /// <summary>
    /// Périodes de paie et clôture mensuelle (module PAI-01).
    ///
    /// Deux services rendus, tous deux absents jusqu'ici :
    ///
    /// 1. La clôture ARRÊTE les totaux du mois et les fige dans la ligne de la période.
    ///    Ce sont ces montants-là qui ont été déclarés à la CNSS et à la DGI ; ils doivent
    ///    rester lisibles tels quels, même si un salaire est corrigé plus tard.
    ///
    /// 2. Une période close REFUSE tout nouvel enregistrement de calcul. Sans ce verrou,
    ///    la clôture ne serait qu'une étiquette : rien n'empêcherait d'écraser en novembre
    ///    un bulletin de mars déjà déclaré.
    ///
    /// Aucune période n'est créée d'office : tant que l'utilisateur n'en ouvre pas, rien
    /// n'est verrouillé et l'application se comporte exactement comme avant.
    /// </summary>
    public class PeriodePaieService
    {
        private readonly SQLiteService _sqliteService;

        private const string Colonnes =
            "Id, Annee, Mois, Statut, DateOuverture, DateCloture, ClotureePar, Commentaire, " +
            "NombreBulletins, TotalBrut, TotalNetAPayer, TotalIuts, TotalCnssSalarie, " +
            "TotalCnssPatronal, TotalTaxePatronale, TotalMasseSalariale";

        public PeriodePaieService(SQLiteService sqliteService)
        {
            if (sqliteService == null) throw new ArgumentNullException("sqliteService");
            _sqliteService = sqliteService;
        }

        // ── Lecture ──────────────────────────────────────────────────────────

        /// <summary>
        /// Toutes les périodes, de la plus récente à la plus ancienne.
        ///
        /// Les totaux d'une période OUVERTE sont recalculés à chaque lecture depuis les
        /// bulletins enregistrés : ils n'ont de sens que s'ils reflètent l'état du moment.
        /// Ceux d'une période CLÔTURÉE sont relus tels qu'ils ont été figés — c'est
        /// précisément ce que clôturer veut dire, et les recalculer trahirait l'arrêté.
        ///
        /// Sans ce rafraîchissement, l'écran affichait les totaux du jour où la période
        /// avait été ouverte : un bulletin enregistré ensuite n'y apparaissait pas, et un
        /// bulletin corrigé y gardait ses anciens montants.
        /// </summary>
        public async Task<List<PeriodePaie>> ChargerAsync()
        {
            var liste = new List<PeriodePaie>();

            using (var conn = _sqliteService.CreerConnexion())
            {
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT " + Colonnes + " FROM PeriodesPaie ORDER BY Annee DESC, Mois DESC";
                    using (var reader = (SqliteDataReader)await cmd.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                            liste.Add(Lire(reader));
                    }
                }

                foreach (var periode in liste)
                {
                    if (periode.EstCloturee) continue;

                    await CalculerTotauxAsync(conn, null, periode);
                    await EnregistrerTotauxAsync(conn, null, periode);
                }
            }

            return liste;
        }

        private static PeriodePaie Lire(SqliteDataReader reader)
        {
            object dateCloture = reader["DateCloture"];

            return new PeriodePaie
            {
                Id = DbValue.IntFromDb(reader["Id"]),
                Annee = DbValue.IntFromDb(reader["Annee"]),
                Mois = DbValue.IntFromDb(reader["Mois"]),
                Statut = DbValue.IntFromDb(reader["Statut"]) == 1
                    ? StatutPeriode.Cloturee
                    : StatutPeriode.Ouverte,
                DateOuverture = DbValue.DateFromDb(reader["DateOuverture"], DateTime.Now),
                DateCloture = (dateCloture == null || dateCloture == DBNull.Value ||
                               string.IsNullOrWhiteSpace(DbValue.TextFromDb(dateCloture)))
                    ? (DateTime?)null
                    : DbValue.DateFromDb(dateCloture, DateTime.Now),
                ClotureePar = DbValue.TextFromDb(reader["ClotureePar"]),
                Commentaire = DbValue.TextFromDb(reader["Commentaire"]),

                NombreBulletins = DbValue.IntFromDb(reader["NombreBulletins"]),
                TotalBrut = DbValue.MoneyFromDb(reader["TotalBrut"]),
                TotalNetAPayer = DbValue.MoneyFromDb(reader["TotalNetAPayer"]),
                TotalIuts = DbValue.MoneyFromDb(reader["TotalIuts"]),
                TotalCnssSalarie = DbValue.MoneyFromDb(reader["TotalCnssSalarie"]),
                TotalCnssPatronal = DbValue.MoneyFromDb(reader["TotalCnssPatronal"]),
                TotalTaxePatronale = DbValue.MoneyFromDb(reader["TotalTaxePatronale"]),
                TotalMasseSalariale = DbValue.MoneyFromDb(reader["TotalMasseSalariale"])
            };
        }

        /// <summary>La période correspondant à une date, ou null si elle n'a jamais été ouverte.</summary>
        public async Task<PeriodePaie> LirePeriodeAsync(int annee, int mois)
        {
            using (var conn = _sqliteService.CreerConnexion())
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "SELECT " + Colonnes + " FROM PeriodesPaie WHERE Annee = @a AND Mois = @m";
                cmd.Parameters.AjouterValeur("@a", annee);
                cmd.Parameters.AjouterValeur("@m", mois);

                using (var reader = (SqliteDataReader)await cmd.ExecuteReaderAsync())
                {
                    if (await reader.ReadAsync())
                        return Lire(reader);
                }
            }

            return null;
        }

        /// <summary>
        /// Vrai si la date tombe dans une période close. Une période jamais ouverte n'est
        /// pas close : le verrou ne s'applique qu'à ce que l'utilisateur a explicitement
        /// arrêté.
        /// </summary>
        public async Task<bool> EstClotureeAsync(DateTime date)
        {
            using (var conn = _sqliteService.CreerConnexion())
            {
                return await EstClotureeAsync(conn, date);
            }
        }

        /// <summary>
        /// Même contrôle, sur une connexion déjà ouverte. La transaction est passée
        /// explicitement : le contrôle a lieu au milieu de l'enregistrement d'un calcul,
        /// donc à l'intérieur de la transaction de celui-ci.
        /// </summary>
        public static async Task<bool> EstClotureeAsync(
            SqliteConnection conn, DateTime date, SqliteTransaction tx = null)
        {
            // Un dossier dont le schéma n'a pas encore atteint la version 5 n'a pas la
            // table : rien n'y est clôturé, et le verrou ne doit pas faire échouer
            // l'enregistrement.
            if (!TableExiste(conn, "PeriodesPaie", tx))
                return false;

            using (var cmd = conn.CreateCommand())
            {
                if (tx != null) cmd.Transaction = tx;
                cmd.CommandText =
                    "SELECT COUNT(*) FROM PeriodesPaie WHERE Annee = @a AND Mois = @m AND Statut = 1";
                cmd.Parameters.AjouterValeur("@a", date.Year);
                cmd.Parameters.AjouterValeur("@m", date.Month);
                return DbValue.IntFromDb(await cmd.ExecuteScalarAsync()) > 0;
            }
        }

        private static bool TableExiste(SqliteConnection conn, string nom, SqliteTransaction tx = null)
        {
            using (var cmd = conn.CreateCommand())
            {
                if (tx != null) cmd.Transaction = tx;
                cmd.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name=@n";
                cmd.Parameters.AjouterValeur("@n", nom);
                return DbValue.IntFromDb(cmd.ExecuteScalar()) > 0;
            }
        }

        // ── Ouverture, clôture, réouverture ──────────────────────────────────

        /// <summary>
        /// Ouvre une période. Si elle existe déjà, elle est renvoyée telle quelle plutôt
        /// que recréée : ouvrir deux fois le même mois ne doit rien casser.
        /// </summary>
        public async Task<PeriodePaie> OuvrirAsync(int annee, int mois)
        {
            if (mois < 1 || mois > 12)
                throw new ArgumentOutOfRangeException("mois", "Le mois doit être compris entre 1 et 12.");

            var existante = await LirePeriodeAsync(annee, mois);
            if (existante != null)
                return existante;

            var periode = new PeriodePaie
            {
                Annee = annee,
                Mois = mois,
                Statut = StatutPeriode.Ouverte,
                DateOuverture = DateTime.Now
            };

            using (var conn = _sqliteService.CreerConnexion())
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText =
                    "INSERT INTO PeriodesPaie (Annee, Mois, Statut, DateOuverture, DateCloture, " +
                    "ClotureePar, Commentaire, NombreBulletins, TotalBrut, TotalNetAPayer, TotalIuts, " +
                    "TotalCnssSalarie, TotalCnssPatronal, TotalTaxePatronale, TotalMasseSalariale) " +
                    "VALUES (@a, @m, 0, @ouverture, NULL, '', '', 0, 0, 0, 0, 0, 0, 0, 0);";
                cmd.Parameters.AjouterValeur("@a", annee);
                cmd.Parameters.AjouterValeur("@m", mois);
                cmd.Parameters.AjouterValeur("@ouverture", DbValue.DateToDb(periode.DateOuverture));
                await cmd.ExecuteNonQueryAsync();
            }

            return await LirePeriodeAsync(annee, mois);
        }

        /// <summary>
        /// Recalcule les totaux d'une période depuis les bulletins enregistrés, SANS la
        /// clôturer. Sert à voir où en est le mois avant de l'arrêter.
        /// </summary>
        public async Task<PeriodePaie> RafraichirTotauxAsync(PeriodePaie periode)
        {
            if (periode == null) throw new ArgumentNullException("periode");

            using (var conn = _sqliteService.CreerConnexion())
            {
                await CalculerTotauxAsync(conn, null, periode);
                await EnregistrerTotauxAsync(conn, null, periode);
            }

            return periode;
        }

        /// <summary>
        /// Arrête la période : les totaux sont recalculés une dernière fois, écrits dans
        /// la ligne, et le statut passe à « clôturée ». À partir de là, plus aucun calcul
        /// de ce mois ne peut être enregistré.
        /// </summary>
        public async Task<PeriodePaie> CloturerAsync(PeriodePaie periode, string utilisateur)
        {
            if (periode == null) throw new ArgumentNullException("periode");

            if (periode.EstCloturee)
                throw new InvalidOperationException("Cette période est déjà clôturée.");

            using (var conn = _sqliteService.CreerConnexion())
            using (var tx = conn.BeginTransaction())
            {
                try
                {
                    await CalculerTotauxAsync(conn, tx, periode);

                    if (periode.NombreBulletins == 0)
                    {
                        throw new InvalidOperationException(
                            "Aucun bulletin n'a été calculé sur cette période : il n'y a rien à arrêter. " +
                            "Calculez les bulletins du mois avant de le clôturer.");
                    }

                    periode.Statut = StatutPeriode.Cloturee;
                    periode.DateCloture = DateTime.Now;
                    periode.ClotureePar = string.IsNullOrWhiteSpace(utilisateur) ? "—" : utilisateur;

                    await EnregistrerTotauxAsync(conn, tx, periode);

                    tx.Commit();
                }
                catch
                {
                    try { tx.Rollback(); }
                    catch { /* connexion perdue */ }

                    // L'objet en mémoire ne doit pas rester « clôturé » si la base ne l'est pas.
                    periode.Statut = StatutPeriode.Ouverte;
                    periode.DateCloture = null;
                    throw;
                }
            }

            return periode;
        }

        /// <summary>
        /// Rouvre une période clôturée. Le geste est légitime — une erreur découverte
        /// après coup doit pouvoir être corrigée — mais il efface les totaux arrêtés, et
        /// l'écran le dit avant de le faire. Le motif est conservé dans le commentaire.
        /// </summary>
        public async Task<PeriodePaie> RouvrirAsync(PeriodePaie periode, string motif)
        {
            if (periode == null) throw new ArgumentNullException("periode");

            if (periode.EstOuverte)
                throw new InvalidOperationException("Cette période est déjà ouverte.");

            string trace = string.Format(
                "{0}Rouverte le {1:dd/MM/yyyy HH:mm} — {2}",
                string.IsNullOrWhiteSpace(periode.Commentaire) ? "" : periode.Commentaire + Environment.NewLine,
                DateTime.Now,
                string.IsNullOrWhiteSpace(motif) ? "motif non précisé" : motif.Trim());

            using (var conn = _sqliteService.CreerConnexion())
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText =
                    "UPDATE PeriodesPaie SET Statut = 0, DateCloture = NULL, ClotureePar = '', " +
                    "Commentaire = @commentaire WHERE Id = @id;";
                cmd.Parameters.AjouterValeur("@commentaire", trace);
                cmd.Parameters.AjouterValeur("@id", periode.Id);
                await cmd.ExecuteNonQueryAsync();
            }

            periode.Statut = StatutPeriode.Ouverte;
            periode.DateCloture = null;
            periode.ClotureePar = "";
            periode.Commentaire = trace;

            return periode;
        }

        /// <summary>Met à jour le seul commentaire, sans toucher au statut ni aux totaux.</summary>
        public async Task EnregistrerCommentaireAsync(PeriodePaie periode)
        {
            if (periode == null || periode.Id <= 0) return;

            using (var conn = _sqliteService.CreerConnexion())
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "UPDATE PeriodesPaie SET Commentaire = @c WHERE Id = @id;";
                cmd.Parameters.AjouterValeur("@c", periode.Commentaire ?? "");
                cmd.Parameters.AjouterValeur("@id", periode.Id);
                await cmd.ExecuteNonQueryAsync();
            }
        }

        /// <summary>
        /// Supprime une période OUVERTE. Une période clôturée n'est jamais supprimée :
        /// elle porte la trace de ce qui a été déclaré.
        /// </summary>
        public async Task SupprimerAsync(PeriodePaie periode)
        {
            if (periode == null || periode.Id <= 0) return;

            if (periode.EstCloturee)
            {
                throw new InvalidOperationException(
                    "Une période clôturée ne se supprime pas : elle conserve les totaux qui ont été " +
                    "déclarés. Rouvrez-la d'abord si vous devez vraiment la retirer.");
            }

            using (var conn = _sqliteService.CreerConnexion())
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "DELETE FROM PeriodesPaie WHERE Id = @id;";
                cmd.Parameters.AjouterValeur("@id", periode.Id);
                await cmd.ExecuteNonQueryAsync();
            }
        }

        // ── Totaux ───────────────────────────────────────────────────────────

        /// <summary>
        /// Additionne les bulletins du mois. Les régularisations et les calculs annuels
        /// sont exclus : ils ne sont pas des bulletins mensuels et fausseraient la masse
        /// salariale déclarée.
        /// </summary>
        private static async Task CalculerTotauxAsync(
            SqliteConnection conn, SqliteTransaction tx, PeriodePaie periode)
        {
            using (var cmd = conn.CreateCommand())
            {
                if (tx != null) cmd.Transaction = tx;

                cmd.CommandText =
                    "SELECT COUNT(*)                     AS Nb, " +
                    "       COALESCE(SUM(SalaireBrut),0)       AS Brut, " +
                    "       COALESCE(SUM(SalaireNetaPayer),0)  AS Net, " +
                    "       COALESCE(SUM(IUTSNet),0)           AS Iuts, " +
                    "       COALESCE(SUM(CnssEmployee),0)      AS CnssSal, " +
                    "       COALESCE(SUM(CnssPartPatronale),0) AS CnssPat, " +
                    "       COALESCE(SUM(TaxePatronale),0)     AS Tpa, " +
                    "       COALESCE(SUM(MasseSalariale),0)    AS Masse " +
                    "FROM TaxCalculations " +
                    "WHERE CalculationDate >= @debut AND CalculationDate <= @fin " +
                    "  AND IsAnnual = 0 AND IsRegularization = 0";

                cmd.Parameters.AjouterValeur("@debut", DbValue.DateToDb(periode.PremierJour));
                cmd.Parameters.AjouterValeur("@fin", DbValue.DateToDb(periode.DernierJour));

                using (var reader = (SqliteDataReader)await cmd.ExecuteReaderAsync())
                {
                    if (await reader.ReadAsync())
                    {
                        periode.NombreBulletins = DbValue.IntFromDb(reader["Nb"]);
                        periode.TotalBrut = DbValue.MoneyFromDb(reader["Brut"]);
                        periode.TotalNetAPayer = DbValue.MoneyFromDb(reader["Net"]);
                        periode.TotalIuts = DbValue.MoneyFromDb(reader["Iuts"]);
                        periode.TotalCnssSalarie = DbValue.MoneyFromDb(reader["CnssSal"]);
                        periode.TotalCnssPatronal = DbValue.MoneyFromDb(reader["CnssPat"]);
                        periode.TotalTaxePatronale = DbValue.MoneyFromDb(reader["Tpa"]);
                        periode.TotalMasseSalariale = DbValue.MoneyFromDb(reader["Masse"]);
                    }
                }
            }
        }

        private static async Task EnregistrerTotauxAsync(
            SqliteConnection conn, SqliteTransaction tx, PeriodePaie periode)
        {
            using (var cmd = conn.CreateCommand())
            {
                if (tx != null) cmd.Transaction = tx;

                cmd.CommandText =
                    "UPDATE PeriodesPaie SET " +
                    "Statut = @statut, DateCloture = @cloture, ClotureePar = @par, " +
                    "NombreBulletins = @nb, TotalBrut = @brut, TotalNetAPayer = @net, " +
                    "TotalIuts = @iuts, TotalCnssSalarie = @cnssSal, TotalCnssPatronal = @cnssPat, " +
                    "TotalTaxePatronale = @tpa, TotalMasseSalariale = @masse " +
                    "WHERE Id = @id;";

                cmd.Parameters.AjouterValeur("@statut", periode.EstCloturee ? 1 : 0);
                cmd.Parameters.AjouterValeur("@cloture",
                    periode.DateCloture.HasValue
                        ? (object)DbValue.DateToDb(periode.DateCloture.Value)
                        : DBNull.Value);
                cmd.Parameters.AjouterValeur("@par", periode.ClotureePar ?? "");
                cmd.Parameters.AjouterValeur("@nb", periode.NombreBulletins);
                cmd.Parameters.AjouterValeur("@brut", DbValue.MoneyToDb(periode.TotalBrut));
                cmd.Parameters.AjouterValeur("@net", DbValue.MoneyToDb(periode.TotalNetAPayer));
                cmd.Parameters.AjouterValeur("@iuts", DbValue.MoneyToDb(periode.TotalIuts));
                cmd.Parameters.AjouterValeur("@cnssSal", DbValue.MoneyToDb(periode.TotalCnssSalarie));
                cmd.Parameters.AjouterValeur("@cnssPat", DbValue.MoneyToDb(periode.TotalCnssPatronal));
                cmd.Parameters.AjouterValeur("@tpa", DbValue.MoneyToDb(periode.TotalTaxePatronale));
                cmd.Parameters.AjouterValeur("@masse", DbValue.MoneyToDb(periode.TotalMasseSalariale));
                cmd.Parameters.AjouterValeur("@id", periode.Id);

                await cmd.ExecuteNonQueryAsync();
            }
        }
    }
}
