using System;
using System.Collections.Generic;
using Microsoft.Data.Sqlite;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using IUTSCalculator.Models;

namespace IUTSCalculator.Services
{
    /// <summary>
    /// Accès aux barèmes légaux datés du dossier (module SOC-02).
    ///
    /// Le principe tient en une phrase : un paramètre légal n'a de sens qu'accompagné de
    /// la date à partir de laquelle il s'applique. Recalculer le bulletin de mars avec le
    /// plafond CNSS d'octobre donne un résultat faux, et rien ne le signale.
    ///
    /// À la première ouverture, le dossier reçoit le barème en vigueur, transcrit depuis
    /// le Code général des impôts et l'arrêté CNSS. Quand un texte change, on ne modifie
    /// pas le barème existant : on le duplique avec une nouvelle date d'effet, et les
    /// périodes antérieures continuent de se recalculer à l'identique.
    /// </summary>
    public class BaremeService
    {
        private readonly SQLiteService _sqliteService;

        private const string ColonnesSansId =
            "DateEffet, Libelle, Reference, Commentaire, EstSysteme, " +
            "TauxCnssSalarie, TauxCnssPrestationsFamiliales, TauxCnssRisquesProfessionnels, " +
            "TauxCnssPensionsPatronal, PlafondCnssMensuel, TauxTaxePatronaleApprentissage, " +
            "TauxMaxIndemniteLogement, PlafondIndemniteLogement, " +
            "TauxMaxIndemniteTransport, PlafondIndemniteTransport, " +
            "TauxMaxIndemniteFonction, PlafondIndemniteFonction, " +
            "TauxMaxCotisationRetraiteDeductible, AbattementCategoriesSuperieures, " +
            "AbattementAutresCategories";

        private const string ColonnesBareme =
            "Id, DateEffet, Libelle, Reference, Commentaire, EstSysteme, " +
            "TauxCnssSalarie, TauxCnssPrestationsFamiliales, TauxCnssRisquesProfessionnels, " +
            "TauxCnssPensionsPatronal, PlafondCnssMensuel, TauxTaxePatronaleApprentissage, " +
            "TauxMaxIndemniteLogement, PlafondIndemniteLogement, " +
            "TauxMaxIndemniteTransport, PlafondIndemniteTransport, " +
            "TauxMaxIndemniteFonction, PlafondIndemniteFonction, " +
            "TauxMaxCotisationRetraiteDeductible, AbattementCategoriesSuperieures, " +
            "AbattementAutresCategories";

        /// <summary>
        /// Valeur stockée pour la borne supérieure de la dernière tranche, qui n'est pas
        /// plafonnée. <c>decimal.MaxValue</c> ne tient pas dans un INTEGER SQLite une fois
        /// converti en centimes ; ce repère, lui, passe partout et reste très au-dessus de
        /// tout revenu réel.
        /// </summary>
        public const decimal BorneSuperieureOuverte = 999999999999m;

        public BaremeService(SQLiteService sqliteService)
        {
            if (sqliteService == null) throw new ArgumentNullException("sqliteService");
            _sqliteService = sqliteService;
        }

        // ── Lecture ──────────────────────────────────────────────────────────

        /// <summary>
        /// Charge tous les barèmes du dossier, du plus récent au plus ancien, en
        /// alimentant d'abord la table si elle est vide.
        /// </summary>
        public async Task<List<BaremeLegal>> ChargerAsync()
        {
            var baremes = await LireAsync();

            if (baremes.Count == 0)
            {
                foreach (var bareme in BaremesInitiaux())
                    await EnregistrerAsync(bareme);

                baremes = await LireAsync();
            }

            return baremes;
        }

        private async Task<List<BaremeLegal>> LireAsync()
        {
            var liste = new List<BaremeLegal>();

            using (var conn = _sqliteService.CreerConnexion())
            {
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT " + ColonnesBareme + " FROM Baremes ORDER BY DateEffet DESC, Id DESC";
                    using (var reader = (SqliteDataReader)await cmd.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                            liste.Add(LireBareme(reader));
                    }
                }

                foreach (var bareme in liste)
                {
                    await ChargerTranchesAsync(conn, bareme);
                    await ChargerChargesFamilleAsync(conn, bareme);
                }
            }

            return liste;
        }

        /// <summary>
        /// Barème applicable à une période : le plus récent parmi ceux dont la date
        /// d'effet est antérieure ou égale à la période. Si la période est antérieure à
        /// tout barème connu — reprise d'historique ancien — le plus ancien est retenu,
        /// faute de mieux, plutôt que de ne rien renvoyer.
        /// </summary>
        public async Task<BaremeLegal> BaremeApplicableAsync(DateTime periode)
        {
            var baremes = await ChargerAsync();
            return BaremeApplicable(baremes, periode);
        }

        /// <summary>Même sélection, sur une liste déjà chargée.</summary>
        public static BaremeLegal BaremeApplicable(IEnumerable<BaremeLegal> baremes, DateTime periode)
        {
            if (baremes == null) return null;

            var liste = baremes.OrderBy(b => b.DateEffet).ToList();
            if (liste.Count == 0) return null;

            var applicable = liste.LastOrDefault(b => b.DateEffet.Date <= periode.Date);
            return applicable ?? liste[0];
        }

        private static BaremeLegal LireBareme(SqliteDataReader reader)
        {
            return new BaremeLegal
            {
                Id = DbValue.IntFromDb(reader["Id"]),
                DateEffet = DbValue.DateFromDb(reader["DateEffet"], DateTime.Today),
                Libelle = DbValue.TextFromDb(reader["Libelle"]),
                Reference = DbValue.TextFromDb(reader["Reference"]),
                Commentaire = DbValue.TextFromDb(reader["Commentaire"]),
                EstSysteme = DbValue.IntFromDb(reader["EstSysteme"]) != 0,

                TauxCnssSalarie = DbValue.DecimalFromDb(reader["TauxCnssSalarie"]),
                TauxCnssPrestationsFamiliales = DbValue.DecimalFromDb(reader["TauxCnssPrestationsFamiliales"]),
                TauxCnssRisquesProfessionnels = DbValue.DecimalFromDb(reader["TauxCnssRisquesProfessionnels"]),
                TauxCnssPensionsPatronal = DbValue.DecimalFromDb(reader["TauxCnssPensionsPatronal"]),
                PlafondCnssMensuel = DbValue.MoneyFromDb(reader["PlafondCnssMensuel"]),

                TauxTaxePatronaleApprentissage = DbValue.DecimalFromDb(reader["TauxTaxePatronaleApprentissage"]),

                TauxMaxIndemniteLogement = DbValue.DecimalFromDb(reader["TauxMaxIndemniteLogement"]),
                PlafondIndemniteLogement = DbValue.MoneyFromDb(reader["PlafondIndemniteLogement"]),
                TauxMaxIndemniteTransport = DbValue.DecimalFromDb(reader["TauxMaxIndemniteTransport"]),
                PlafondIndemniteTransport = DbValue.MoneyFromDb(reader["PlafondIndemniteTransport"]),
                TauxMaxIndemniteFonction = DbValue.DecimalFromDb(reader["TauxMaxIndemniteFonction"]),
                PlafondIndemniteFonction = DbValue.MoneyFromDb(reader["PlafondIndemniteFonction"]),

                TauxMaxCotisationRetraiteDeductible = DbValue.DecimalFromDb(reader["TauxMaxCotisationRetraiteDeductible"]),
                AbattementCategoriesSuperieures = DbValue.DecimalFromDb(reader["AbattementCategoriesSuperieures"]),
                AbattementAutresCategories = DbValue.DecimalFromDb(reader["AbattementAutresCategories"])
            };
        }

        private static async Task ChargerTranchesAsync(SqliteConnection conn, BaremeLegal bareme, SqliteTransaction tx = null)
        {
            bareme.Tranches.Clear();

            using (var cmd = conn.CreateCommand())
            {
                if (tx != null) cmd.Transaction = tx;
                cmd.CommandText =
                    "SELECT Ordre, BorneInferieure, BorneSuperieure, Taux " +
                    "FROM BaremeTranches WHERE BaremeId = @id ORDER BY Ordre";
                cmd.Parameters.AjouterValeur("@id", bareme.Id);

                using (var reader = (SqliteDataReader)await cmd.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                    {
                        decimal borneHaute = DbValue.MoneyFromDb(reader["BorneSuperieure"]);

                        bareme.Tranches.Add(new TrancheBareme
                        {
                            Ordre = DbValue.IntFromDb(reader["Ordre"]),
                            BorneInferieure = DbValue.MoneyFromDb(reader["BorneInferieure"]),
                            // La borne ouverte est restituée telle que le modèle l'attend.
                            BorneSuperieure = borneHaute >= BorneSuperieureOuverte
                                ? decimal.MaxValue
                                : borneHaute,
                            Taux = DbValue.DecimalFromDb(reader["Taux"])
                        });
                    }
                }
            }
        }

        private static async Task ChargerChargesFamilleAsync(SqliteConnection conn, BaremeLegal bareme, SqliteTransaction tx = null)
        {
            bareme.ChargesFamille.Clear();

            using (var cmd = conn.CreateCommand())
            {
                if (tx != null) cmd.Transaction = tx;
                cmd.CommandText =
                    "SELECT NombreCharges, Taux FROM BaremeChargesFamille " +
                    "WHERE BaremeId = @id ORDER BY NombreCharges";
                cmd.Parameters.AjouterValeur("@id", bareme.Id);

                using (var reader = (SqliteDataReader)await cmd.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                    {
                        bareme.ChargesFamille.Add(new ReductionChargeFamille
                        {
                            NombreCharges = DbValue.IntFromDb(reader["NombreCharges"]),
                            Taux = DbValue.DecimalFromDb(reader["Taux"])
                        });
                    }
                }
            }
        }

        /// <summary>Vrai si un autre barème porte déjà cette date d'effet.</summary>
        public async Task<bool> DateEffetExisteAsync(DateTime dateEffet, int idAExclure)
        {
            using (var conn = _sqliteService.CreerConnexion())
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "SELECT COUNT(*) FROM Baremes WHERE DateEffet = @d AND Id <> @id";
                cmd.Parameters.AjouterValeur("@d", DbValue.DateToDb(dateEffet.Date));
                cmd.Parameters.AjouterValeur("@id", idAExclure);
                return DbValue.IntFromDb(await cmd.ExecuteScalarAsync()) > 0;
            }
        }

        // ── Écriture ─────────────────────────────────────────────────────────

        /// <summary>
        /// Enregistre un barème et ses collections. L'ensemble passe dans une seule
        /// transaction : un barème dont les tranches seraient écrites à moitié produirait
        /// des impôts faux sans jamais lever d'erreur.
        ///
        /// La création ou chaque valeur modifiée est journalisée (entité « Barèmes ») dans
        /// la même transaction : le filtre « Barèmes » du journal d'audit restait toujours
        /// vide (défaut 21). Les deux écrans qui enregistrent un barème — « Barèmes légaux »
        /// et « Paramètres » — passent par ici.
        /// </summary>
        public async Task EnregistrerAsync(BaremeLegal bareme)
        {
            if (bareme == null) throw new ArgumentNullException("bareme");

            using (var conn = _sqliteService.CreerConnexion())
            using (var transaction = conn.BeginTransaction())
            {
                try
                {
                    BaremeLegal ancien = bareme.Id > 0
                        ? await LireParIdAsync(conn, transaction, bareme.Id)
                        : null;

                    if (bareme.Id <= 0)
                    {
                        using (var cmd = conn.CreateCommand())
                        {
                            cmd.Transaction = transaction;
                            cmd.CommandText =
                                "INSERT INTO Baremes (" + ColonnesSansId + ") VALUES (" +
                                "@dateEffet, @libelle, @reference, @commentaire, @estSysteme, " +
                                "@tauxCnssSalarie, @tauxPf, @tauxRp, @tauxPensions, @plafondCnss, @tauxTpa, " +
                                "@tauxLogement, @plafondLogement, @tauxTransport, @plafondTransport, " +
                                "@tauxFonction, @plafondFonction, @tauxRetraite, @abattementSup, @abattementAutres);";
                            AjouterParametres(cmd, bareme);
                            await cmd.ExecuteNonQueryAsync();
                        }

                        using (var cmd = conn.CreateCommand())
                        {
                            cmd.Transaction = transaction;
                            cmd.CommandText = "SELECT last_insert_rowid();";
                            bareme.Id = DbValue.IntFromDb(await cmd.ExecuteScalarAsync());
                        }
                    }
                    else
                    {
                        using (var cmd = conn.CreateCommand())
                        {
                            cmd.Transaction = transaction;
                            cmd.CommandText =
                                "UPDATE Baremes SET " +
                                "DateEffet = @dateEffet, Libelle = @libelle, Reference = @reference, " +
                                "Commentaire = @commentaire, EstSysteme = @estSysteme, " +
                                "TauxCnssSalarie = @tauxCnssSalarie, " +
                                "TauxCnssPrestationsFamiliales = @tauxPf, " +
                                "TauxCnssRisquesProfessionnels = @tauxRp, " +
                                "TauxCnssPensionsPatronal = @tauxPensions, " +
                                "PlafondCnssMensuel = @plafondCnss, " +
                                "TauxTaxePatronaleApprentissage = @tauxTpa, " +
                                "TauxMaxIndemniteLogement = @tauxLogement, " +
                                "PlafondIndemniteLogement = @plafondLogement, " +
                                "TauxMaxIndemniteTransport = @tauxTransport, " +
                                "PlafondIndemniteTransport = @plafondTransport, " +
                                "TauxMaxIndemniteFonction = @tauxFonction, " +
                                "PlafondIndemniteFonction = @plafondFonction, " +
                                "TauxMaxCotisationRetraiteDeductible = @tauxRetraite, " +
                                "AbattementCategoriesSuperieures = @abattementSup, " +
                                "AbattementAutresCategories = @abattementAutres " +
                                "WHERE Id = @id;";
                            AjouterParametres(cmd, bareme);
                            cmd.Parameters.AjouterValeur("@id", bareme.Id);
                            await cmd.ExecuteNonQueryAsync();
                        }
                    }

                    await RemplacerTranchesAsync(conn, transaction, bareme);
                    await RemplacerChargesFamilleAsync(conn, transaction, bareme);

                    await JournaliserEnregistrementAsync(conn, transaction, ancien, bareme);

                    transaction.Commit();
                }
                catch
                {
                    try { transaction.Rollback(); }
                    catch { /* la connexion est perdue : rien à annuler */ }
                    throw;
                }
            }
        }

        private static void AjouterParametres(SqliteCommand cmd, BaremeLegal b)
        {
            cmd.Parameters.AjouterValeur("@dateEffet", DbValue.DateToDb(b.DateEffet.Date));
            cmd.Parameters.AjouterValeur("@libelle", b.Libelle ?? "");
            cmd.Parameters.AjouterValeur("@reference", b.Reference ?? "");
            cmd.Parameters.AjouterValeur("@commentaire", b.Commentaire ?? "");
            cmd.Parameters.AjouterValeur("@estSysteme", b.EstSysteme ? 1 : 0);

            cmd.Parameters.AjouterValeur("@tauxCnssSalarie", DbValue.DecimalToDb(b.TauxCnssSalarie));
            cmd.Parameters.AjouterValeur("@tauxPf", DbValue.DecimalToDb(b.TauxCnssPrestationsFamiliales));
            cmd.Parameters.AjouterValeur("@tauxRp", DbValue.DecimalToDb(b.TauxCnssRisquesProfessionnels));
            cmd.Parameters.AjouterValeur("@tauxPensions", DbValue.DecimalToDb(b.TauxCnssPensionsPatronal));
            cmd.Parameters.AjouterValeur("@plafondCnss", DbValue.MoneyToDb(b.PlafondCnssMensuel));
            cmd.Parameters.AjouterValeur("@tauxTpa", DbValue.DecimalToDb(b.TauxTaxePatronaleApprentissage));

            cmd.Parameters.AjouterValeur("@tauxLogement", DbValue.DecimalToDb(b.TauxMaxIndemniteLogement));
            cmd.Parameters.AjouterValeur("@plafondLogement", DbValue.MoneyToDb(b.PlafondIndemniteLogement));
            cmd.Parameters.AjouterValeur("@tauxTransport", DbValue.DecimalToDb(b.TauxMaxIndemniteTransport));
            cmd.Parameters.AjouterValeur("@plafondTransport", DbValue.MoneyToDb(b.PlafondIndemniteTransport));
            cmd.Parameters.AjouterValeur("@tauxFonction", DbValue.DecimalToDb(b.TauxMaxIndemniteFonction));
            cmd.Parameters.AjouterValeur("@plafondFonction", DbValue.MoneyToDb(b.PlafondIndemniteFonction));

            cmd.Parameters.AjouterValeur("@tauxRetraite", DbValue.DecimalToDb(b.TauxMaxCotisationRetraiteDeductible));
            cmd.Parameters.AjouterValeur("@abattementSup", DbValue.DecimalToDb(b.AbattementCategoriesSuperieures));
            cmd.Parameters.AjouterValeur("@abattementAutres", DbValue.DecimalToDb(b.AbattementAutresCategories));
        }

        private static async Task RemplacerTranchesAsync(
            SqliteConnection conn, SqliteTransaction transaction, BaremeLegal bareme)
        {
            using (var cmd = conn.CreateCommand())
            {
                cmd.Transaction = transaction;
                cmd.CommandText = "DELETE FROM BaremeTranches WHERE BaremeId = @id;";
                cmd.Parameters.AjouterValeur("@id", bareme.Id);
                await cmd.ExecuteNonQueryAsync();
            }

            int ordre = 1;
            foreach (var tranche in bareme.Tranches.OrderBy(t => t.Ordre).ThenBy(t => t.BorneInferieure))
            {
                using (var cmd = conn.CreateCommand())
                {
                    cmd.Transaction = transaction;
                    cmd.CommandText =
                        "INSERT INTO BaremeTranches (BaremeId, Ordre, BorneInferieure, BorneSuperieure, Taux) " +
                        "VALUES (@bareme, @ordre, @bas, @haut, @taux);";
                    cmd.Parameters.AjouterValeur("@bareme", bareme.Id);
                    cmd.Parameters.AjouterValeur("@ordre", ordre);
                    cmd.Parameters.AjouterValeur("@bas", DbValue.MoneyToDb(tranche.BorneInferieure));
                    cmd.Parameters.AjouterValeur("@haut", DbValue.MoneyToDb(
                        tranche.EstDerniereTranche ? BorneSuperieureOuverte : tranche.BorneSuperieure));
                    cmd.Parameters.AjouterValeur("@taux", DbValue.DecimalToDb(tranche.Taux));
                    await cmd.ExecuteNonQueryAsync();
                }

                tranche.Ordre = ordre;
                ordre++;
            }
        }

        private static async Task RemplacerChargesFamilleAsync(
            SqliteConnection conn, SqliteTransaction transaction, BaremeLegal bareme)
        {
            using (var cmd = conn.CreateCommand())
            {
                cmd.Transaction = transaction;
                cmd.CommandText = "DELETE FROM BaremeChargesFamille WHERE BaremeId = @id;";
                cmd.Parameters.AjouterValeur("@id", bareme.Id);
                await cmd.ExecuteNonQueryAsync();
            }

            foreach (var charge in bareme.ChargesFamille.OrderBy(c => c.NombreCharges))
            {
                using (var cmd = conn.CreateCommand())
                {
                    cmd.Transaction = transaction;
                    cmd.CommandText =
                        "INSERT INTO BaremeChargesFamille (BaremeId, NombreCharges, Taux) " +
                        "VALUES (@bareme, @nombre, @taux);";
                    cmd.Parameters.AjouterValeur("@bareme", bareme.Id);
                    cmd.Parameters.AjouterValeur("@nombre", charge.NombreCharges);
                    cmd.Parameters.AjouterValeur("@taux", DbValue.DecimalToDb(charge.Taux));
                    await cmd.ExecuteNonQueryAsync();
                }
            }
        }

        /// <summary>
        /// Supprime un barème et ses collections. Le dernier barème restant n'est jamais
        /// supprimé : un dossier sans barème ne saurait plus calculer un seul bulletin.
        /// </summary>
        public async Task SupprimerAsync(BaremeLegal bareme)
        {
            if (bareme == null || bareme.Id <= 0) return;

            using (var conn = _sqliteService.CreerConnexion())
            {
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT COUNT(*) FROM Baremes;";
                    if (DbValue.IntFromDb(await cmd.ExecuteScalarAsync()) <= 1)
                    {
                        throw new InvalidOperationException(
                            "Ce barème est le dernier du dossier : le supprimer rendrait tout calcul " +
                            "impossible. Créez d'abord le barème qui lui succède.");
                    }
                }

                using (var transaction = conn.BeginTransaction())
                {
                    try
                    {
                        BaremeLegal ancien = await LireParIdAsync(conn, transaction, bareme.Id) ?? bareme;

                        foreach (string table in new[] { "BaremeTranches", "BaremeChargesFamille", "Baremes" })
                        {
                            using (var cmd = conn.CreateCommand())
                            {
                                cmd.Transaction = transaction;
                                cmd.CommandText = table == "Baremes"
                                    ? "DELETE FROM Baremes WHERE Id = @id;"
                                    : "DELETE FROM " + table + " WHERE BaremeId = @id;";
                                cmd.Parameters.AjouterValeur("@id", bareme.Id);
                                await cmd.ExecuteNonQueryAsync();
                            }
                        }

                        await AuditService.EnregistrerDansTransactionAsync(
                            conn, transaction,
                            action: "Suppression barème",
                            entite: AuditService.EntiteBaremes,
                            entiteId: CleAudit(ancien),
                            champ: "Barème complet",
                            ancienneValeur: ancien.Designation,
                            details: "Barème supprimé. Contenu : " + Resume(ancien));

                        transaction.Commit();
                    }
                    catch
                    {
                        try { transaction.Rollback(); }
                        catch { /* connexion perdue */ }
                        throw;
                    }
                }
            }
        }

        // ── Journal d'audit ──────────────────────────────────────────────────

        private static async Task<BaremeLegal> LireParIdAsync(SqliteConnection conn, SqliteTransaction tx, int id)
        {
            BaremeLegal bareme = null;

            using (var cmd = conn.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = "SELECT " + ColonnesBareme + " FROM Baremes WHERE Id = @id";
                cmd.Parameters.AjouterValeur("@id", id);
                using (var reader = (SqliteDataReader)await cmd.ExecuteReaderAsync())
                {
                    if (await reader.ReadAsync())
                        bareme = LireBareme(reader);
                }
            }

            if (bareme != null)
            {
                await ChargerTranchesAsync(conn, bareme, tx);
                await ChargerChargesFamilleAsync(conn, bareme, tx);
            }

            return bareme;
        }

        private static string CleAudit(BaremeLegal b)
        {
            return b.DateEffet.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }

        private static string Pourcentage(decimal fraction)
        {
            return (fraction * 100m).ToString("0.###", CultureInfo.GetCultureInfo("fr-FR")) + " %";
        }

        private static string Montant(decimal montant)
        {
            return montant.ToString("N0", CultureInfo.GetCultureInfo("fr-FR")) + " FCFA";
        }

        private static string ResumeTranches(BaremeLegal b)
        {
            return string.Join(" ; ", b.Tranches.OrderBy(t => t.Ordre).ThenBy(t => t.BorneInferieure).Select(t => t.Designation));
        }

        private static string ResumeChargesFamille(BaremeLegal b)
        {
            return string.Join(" ; ", b.ChargesFamille.OrderBy(c => c.NombreCharges).Select(c => c.Designation));
        }

        private static string Resume(BaremeLegal b)
        {
            return string.Format(
                "CNSS salarié {0}, patronal {1} (PF {2}, RP {3}, pensions {4}), plafond {5} ; TPA {6} ; {7} tranche(s) IUTS : {8}.",
                Pourcentage(b.TauxCnssSalarie), Pourcentage(b.TauxCnssEmployeurTotal),
                Pourcentage(b.TauxCnssPrestationsFamiliales), Pourcentage(b.TauxCnssRisquesProfessionnels),
                Pourcentage(b.TauxCnssPensionsPatronal), Montant(b.PlafondCnssMensuel),
                Pourcentage(b.TauxTaxePatronaleApprentissage), b.Tranches.Count, ResumeTranches(b));
        }

        /// <summary>Champs comparés d'un barème : libellé et valeur lisible.</summary>
        private static List<KeyValuePair<string, string>> Champs(BaremeLegal b)
        {
            return new List<KeyValuePair<string, string>>
            {
                new KeyValuePair<string, string>("Date d'effet", b.DateEffet.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture)),
                new KeyValuePair<string, string>("Libellé", b.Libelle ?? ""),
                new KeyValuePair<string, string>("Référence légale", b.Reference ?? ""),
                new KeyValuePair<string, string>("Commentaire", b.Commentaire ?? ""),
                new KeyValuePair<string, string>("Taux CNSS salarié (pensions)", Pourcentage(b.TauxCnssSalarie)),
                new KeyValuePair<string, string>("Taux CNSS prestations familiales", Pourcentage(b.TauxCnssPrestationsFamiliales)),
                new KeyValuePair<string, string>("Taux CNSS risques professionnels", Pourcentage(b.TauxCnssRisquesProfessionnels)),
                new KeyValuePair<string, string>("Taux CNSS pensions (patronal)", Pourcentage(b.TauxCnssPensionsPatronal)),
                new KeyValuePair<string, string>("Plafond mensuel CNSS", Montant(b.PlafondCnssMensuel)),
                new KeyValuePair<string, string>("Taux de la taxe patronale d'apprentissage", Pourcentage(b.TauxTaxePatronaleApprentissage)),
                new KeyValuePair<string, string>("Exonération logement — taux", Pourcentage(b.TauxMaxIndemniteLogement)),
                new KeyValuePair<string, string>("Exonération logement — plafond", Montant(b.PlafondIndemniteLogement)),
                new KeyValuePair<string, string>("Exonération transport — taux", Pourcentage(b.TauxMaxIndemniteTransport)),
                new KeyValuePair<string, string>("Exonération transport — plafond", Montant(b.PlafondIndemniteTransport)),
                new KeyValuePair<string, string>("Exonération fonction — taux", Pourcentage(b.TauxMaxIndemniteFonction)),
                new KeyValuePair<string, string>("Exonération fonction — plafond", Montant(b.PlafondIndemniteFonction)),
                new KeyValuePair<string, string>("Cotisation retraite déductible", Pourcentage(b.TauxMaxCotisationRetraiteDeductible)),
                new KeyValuePair<string, string>("Abattement — catégories supérieures", Pourcentage(b.AbattementCategoriesSuperieures)),
                new KeyValuePair<string, string>("Abattement — autres catégories", Pourcentage(b.AbattementAutresCategories)),
                new KeyValuePair<string, string>("Tranches de l'IUTS", ResumeTranches(b)),
                new KeyValuePair<string, string>("Réductions pour charges de famille", ResumeChargesFamille(b))
            };
        }

        /// <summary>
        /// Journalise la création d'un barème, ou chaque valeur modifiée d'un barème
        /// existant (ancienne → nouvelle). Un enregistrement sans changement n'écrit rien.
        /// </summary>
        private static async Task JournaliserEnregistrementAsync(
            SqliteConnection conn, SqliteTransaction tx, BaremeLegal ancien, BaremeLegal nouveau)
        {
            if (ancien == null)
            {
                await AuditService.EnregistrerDansTransactionAsync(
                    conn, tx,
                    action: "Création barème",
                    entite: AuditService.EntiteBaremes,
                    entiteId: CleAudit(nouveau),
                    champ: "Barème complet",
                    nouvelleValeur: nouveau.Designation,
                    details: (nouveau.EstSysteme
                        ? "Barème livré avec l'application, installé dans le dossier. "
                        : "Nouveau barème daté (non rétroactif : il ne s'applique qu'à partir de sa date d'effet). ")
                        + "Contenu : " + Resume(nouveau));
                return;
            }

            var avant = Champs(ancien);
            var apres = Champs(nouveau);
            for (int i = 0; i < avant.Count; i++)
            {
                if (string.Equals(avant[i].Value, apres[i].Value, StringComparison.Ordinal)) continue;

                await AuditService.EnregistrerDansTransactionAsync(
                    conn, tx,
                    action: "Modification barème",
                    entite: AuditService.EntiteBaremes,
                    entiteId: CleAudit(nouveau),
                    champ: avant[i].Key,
                    ancienneValeur: avant[i].Value,
                    nouvelleValeur: apres[i].Value,
                    details: string.Format(
                        "Barème « {0} » modifié : la correction est rétroactive pour toutes les périodes qu'il couvre (bulletins recalculés).",
                        nouveau.Designation));
            }
        }

        /// <summary>
        /// Copie conforme d'un barème, prête à recevoir une nouvelle date d'effet. C'est
        /// le geste normal quand un texte change : on n'écrase jamais le barème précédent,
        /// dont dépendent tous les bulletins déjà émis.
        /// </summary>
        public static BaremeLegal Dupliquer(BaremeLegal source, DateTime nouvelleDateEffet)
        {
            if (source == null) throw new ArgumentNullException("source");

            var copie = source.Clone();
            copie.Id = 0;
            copie.EstSysteme = false;
            copie.DateEffet = nouvelleDateEffet.Date;
            copie.Libelle = string.Format("Barème {0:yyyy}", nouvelleDateEffet);
            copie.Reference = "";
            copie.Commentaire = string.Format(
                "Dupliqué depuis « {0} » le {1:dd/MM/yyyy}.",
                source.Designation, DateTime.Today);

            return copie;
        }

        // ── Barème livré ─────────────────────────────────────────────────────

        /// <summary>
        /// Le barème en vigueur au Burkina Faso, transcrit depuis les textes.
        ///
        /// Sources : art. 106, 112 et 113 du Code général des impôts pour l'IUTS et les
        /// exonérations d'indemnités ; arrêté fixant le plafond de cotisation CNSS à
        /// 800 000 FCFA par mois et la répartition 5,5 % salarié / 16 % employeur
        /// (6 % prestations familiales, 1,5 % risques professionnels, 8,5 % pensions).
        ///
        /// Les valeurs reprennent EXACTEMENT celles que le moteur de calcul applique
        /// aujourd'hui, y compris la construction contiguë des tranches : ce module
        /// déplace le référentiel, il ne change aucun résultat.
        /// </summary>
        public static List<BaremeLegal> BaremesInitiaux()
        {
            var bareme = new BaremeLegal
            {
                DateEffet = new DateTime(2023, 1, 1),
                Libelle = "Barème en vigueur",
                Reference = "CGI art. 106, 112 et 113 — arrêté CNSS plafond 800 000 FCFA",
                EstSysteme = true,
                Commentaire =
                    "Barème livré avec l'application, repris des paramètres appliqués par le moteur " +
                    "de calcul. À la première modification légale, dupliquez-le avec la nouvelle date " +
                    "d'effet plutôt que de le corriger : les bulletins déjà émis doivent rester " +
                    "recalculables à l'identique.",

                TauxCnssSalarie = 0.055m,
                TauxCnssPrestationsFamiliales = 0.06m,
                TauxCnssRisquesProfessionnels = 0.015m,
                TauxCnssPensionsPatronal = 0.085m,
                PlafondCnssMensuel = 800000m,

                TauxTaxePatronaleApprentissage = 0.03m,

                TauxMaxIndemniteLogement = 0.20m,
                PlafondIndemniteLogement = 75000m,
                TauxMaxIndemniteTransport = 0.05m,
                PlafondIndemniteTransport = 30000m,
                TauxMaxIndemniteFonction = 0.05m,
                PlafondIndemniteFonction = 50000m,

                TauxMaxCotisationRetraiteDeductible = 0.08m,
                AbattementCategoriesSuperieures = 0.20m,
                AbattementAutresCategories = 0.25m
            };

            // Tranches contiguës : la borne inférieure d'une tranche est la borne
            // supérieure de la précédente. Le texte publie « 30.100 à 50.000 », mais ce
            // 30.100 est un repère informel ; s'en servir ferait perdre 100 FCFA de
            // largeur à chaque palier.
            AjouterTranche(bareme, 1, 0m, 30000m, 0m);
            AjouterTranche(bareme, 2, 30000m, 50000m, 12.10m);
            AjouterTranche(bareme, 3, 50000m, 80000m, 13.90m);
            AjouterTranche(bareme, 4, 80000m, 120000m, 15.70m);
            AjouterTranche(bareme, 5, 120000m, 170000m, 18.40m);
            AjouterTranche(bareme, 6, 170000m, 250000m, 21.70m);
            AjouterTranche(bareme, 7, 250000m, decimal.MaxValue, 25.00m);

            // Art. 113 : le texte ne détaille explicitement que 1 à 4 charges
            // (8 / 10 / 12 / 14 %). Au-delà, la progression de +2 % par charge est
            // poursuivie jusqu'à 7 charges (20 %), conformément au bulletin de référence.
            for (int charges = 0; charges <= 7; charges++)
            {
                bareme.ChargesFamille.Add(new ReductionChargeFamille
                {
                    NombreCharges = charges,
                    Taux = charges == 0 ? 0m : 0.06m + (0.02m * charges)
                });
            }

            return new List<BaremeLegal> { bareme };
        }

        private static void AjouterTranche(
            BaremeLegal bareme, int ordre, decimal bas, decimal haut, decimal taux)
        {
            bareme.Tranches.Add(new TrancheBareme
            {
                Ordre = ordre,
                BorneInferieure = bas,
                BorneSuperieure = haut,
                Taux = taux
            });
        }
    }
}
