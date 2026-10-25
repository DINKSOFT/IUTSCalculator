using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Linq;
using System.Threading.Tasks;
using IUTSCalculator.Models;

namespace IUTSCalculator.Services
{
    /// <summary>
    /// Barèmes datés des majorations d'heures supplémentaires (table BaremesHeuresSup,
    /// migration 34). Lus par PAI-03 pour valoriser les heures et par RH-08 pour les
    /// seuils hebdomadaires (40 h, 8 h de tranche 1).
    /// </summary>
    public class BaremeHeuresSupService
    {
        private readonly SQLiteService _sqliteService;

        private const string Colonnes =
            "Id, DateEffet, Libelle, Reference, EstSysteme, TauxTranche1, TauxTranche2, TauxNuit, " +
            "TauxFerieJour, TauxFerieNuit, SeuilTranche1, DureeHebdoLegale";

        public BaremeHeuresSupService(SQLiteService sqliteService)
        {
            _sqliteService = sqliteService ?? throw new ArgumentNullException(nameof(sqliteService));
        }

        /// <summary>Tous les barèmes, du plus récent au plus ancien.</summary>
        public async Task<List<BaremeHeuresSup>> ChargerToutAsync()
        {
            var liste = new List<BaremeHeuresSup>();

            using (var conn = _sqliteService.CreateConnection())
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "SELECT " + Colonnes + " FROM BaremesHeuresSup ORDER BY DateEffet DESC, Id DESC;";
                using (var reader = (SQLiteDataReader)await cmd.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                        liste.Add(Lire(reader));
                }
            }

            return liste;
        }

        /// <summary>
        /// Barème applicable au mois de paie (premier jour du mois). Sans barème en base,
        /// les taux historiques de PAI-03 : le calcul ne change jamais par défaut.
        /// </summary>
        public async Task<BaremeHeuresSup> ApplicableAsync(int annee, int mois)
        {
            var tous = await ChargerToutAsync();
            return BaremeHeuresSup.Applicable(tous, new DateTime(annee, mois, 1));
        }

        /// <summary>Version synchrone, pour les appels déjà sur une connexion ouverte.</summary>
        public static BaremeHeuresSup Applicable(SQLiteConnection conn, DateTime date)
        {
            var liste = new List<BaremeHeuresSup>();
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "SELECT " + Colonnes + " FROM BaremesHeuresSup;";
                using (var reader = (SQLiteDataReader)cmd.ExecuteReader())
                {
                    while (reader.Read())
                        liste.Add(Lire(reader));
                }
            }
            return BaremeHeuresSup.Applicable(liste, date);
        }

        /// <summary>
        /// Crée ou met à jour un barème. Deux barèmes ne peuvent pas partager la même date
        /// d'effet : le barème applicable à un mois deviendrait indéterminé.
        /// </summary>
        public async Task EnregistrerAsync(BaremeHeuresSup bareme)
        {
            if (bareme == null) throw new ArgumentNullException(nameof(bareme));

            var anomalies = bareme.Anomalies();
            if (anomalies.Count > 0)
                throw new InvalidOperationException(string.Join(Environment.NewLine, anomalies));

            using (var conn = _sqliteService.CreateConnection())
            {
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT COUNT(*) FROM BaremesHeuresSup WHERE DateEffet = @d AND Id <> @id;";
                    cmd.Parameters.AddWithValue("@d", DbValue.DateToDb(bareme.DateEffet.Date));
                    cmd.Parameters.AddWithValue("@id", bareme.Id);
                    if (DbValue.IntFromDb(await cmd.ExecuteScalarAsync()) > 0)
                    {
                        throw new InvalidOperationException(string.Format(
                            "Un barème d'heures supplémentaires existe déjà au {0:dd/MM/yyyy}. " +
                            "Choisissez une autre date d'effet.", bareme.DateEffet));
                    }
                }

                using (var cmd = conn.CreateCommand())
                {
                    if (bareme.Id > 0)
                    {
                        cmd.CommandText =
                            "UPDATE BaremesHeuresSup SET DateEffet = @d, Libelle = @l, Reference = @r, " +
                            "TauxTranche1 = @t1, TauxTranche2 = @t2, TauxNuit = @tn, TauxFerieJour = @tfj, " +
                            "TauxFerieNuit = @tfn, SeuilTranche1 = @s1, DureeHebdoLegale = @dh WHERE Id = @id;";
                        cmd.Parameters.AddWithValue("@id", bareme.Id);
                    }
                    else
                    {
                        cmd.CommandText =
                            "INSERT INTO BaremesHeuresSup (DateEffet, Libelle, Reference, EstSysteme, " +
                            "TauxTranche1, TauxTranche2, TauxNuit, TauxFerieJour, TauxFerieNuit, SeuilTranche1, DureeHebdoLegale) " +
                            "VALUES (@d, @l, @r, 0, @t1, @t2, @tn, @tfj, @tfn, @s1, @dh);";
                    }

                    cmd.Parameters.AddWithValue("@d", DbValue.DateToDb(bareme.DateEffet.Date));
                    cmd.Parameters.AddWithValue("@l", bareme.Libelle ?? string.Empty);
                    cmd.Parameters.AddWithValue("@r", bareme.Reference ?? string.Empty);
                    cmd.Parameters.AddWithValue("@t1", DbValue.DecimalToDb(bareme.TauxTranche1));
                    cmd.Parameters.AddWithValue("@t2", DbValue.DecimalToDb(bareme.TauxTranche2));
                    cmd.Parameters.AddWithValue("@tn", DbValue.DecimalToDb(bareme.TauxNuit));
                    cmd.Parameters.AddWithValue("@tfj", DbValue.DecimalToDb(bareme.TauxFerieJour));
                    cmd.Parameters.AddWithValue("@tfn", DbValue.DecimalToDb(bareme.TauxFerieNuit));
                    cmd.Parameters.AddWithValue("@s1", DbValue.DecimalToDb(bareme.SeuilTranche1));
                    cmd.Parameters.AddWithValue("@dh", DbValue.DecimalToDb(bareme.DureeHebdoLegale));

                    await cmd.ExecuteNonQueryAsync();

                    if (bareme.Id <= 0)
                        bareme.Id = (int)conn.LastInsertRowId;
                }
            }
        }

        /// <summary>Supprime un barème créé par l'utilisateur. Les barèmes livrés restent.</summary>
        public async Task SupprimerAsync(BaremeHeuresSup bareme)
        {
            if (bareme == null || bareme.Id <= 0) return;
            if (bareme.EstSysteme)
                throw new InvalidOperationException("Un barème livré avec l'application ne se supprime pas : créez-en un nouveau avec une date d'effet plus récente.");

            using (var conn = _sqliteService.CreateConnection())
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "DELETE FROM BaremesHeuresSup WHERE Id = @id AND EstSysteme = 0;";
                cmd.Parameters.AddWithValue("@id", bareme.Id);
                await cmd.ExecuteNonQueryAsync();
            }
        }

        private static BaremeHeuresSup Lire(SQLiteDataReader reader)
        {
            return new BaremeHeuresSup
            {
                Id = DbValue.IntFromDb(reader["Id"]),
                DateEffet = DbValue.DateFromDb(reader["DateEffet"], new DateTime(2000, 1, 1)),
                Libelle = DbValue.TextFromDb(reader["Libelle"]),
                Reference = DbValue.TextFromDb(reader["Reference"]),
                EstSysteme = DbValue.IntFromDb(reader["EstSysteme"]) != 0,
                TauxTranche1 = DbValue.DecimalFromDb(reader["TauxTranche1"]),
                TauxTranche2 = DbValue.DecimalFromDb(reader["TauxTranche2"]),
                TauxNuit = DbValue.DecimalFromDb(reader["TauxNuit"]),
                TauxFerieJour = DbValue.DecimalFromDb(reader["TauxFerieJour"]),
                TauxFerieNuit = DbValue.DecimalFromDb(reader["TauxFerieNuit"]),
                SeuilTranche1 = DbValue.DecimalFromDb(reader["SeuilTranche1"]),
                DureeHebdoLegale = DbValue.DecimalFromDb(reader["DureeHebdoLegale"])
            };
        }
    }
}
