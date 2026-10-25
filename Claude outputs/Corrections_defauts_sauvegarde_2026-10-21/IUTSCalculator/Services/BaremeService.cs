using System;
using System.Collections.Generic;
using System.Data.SQLite;
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

            using (var conn = _sqliteService.CreateConnection())
            {
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT " + ColonnesBareme + " FROM Baremes ORDER BY DateEffet DESC, Id DESC";
                    using (var reader = (SQLiteDataReader)await cmd.ExecuteReaderAsync())
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

        private static BaremeLegal LireBareme(SQLiteDataReader reader)
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

        private static async Task ChargerTranchesAsync(SQLiteConnection conn, BaremeLegal bareme)
        {
            bareme.Tranches.Clear();

            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText =
                    "SELECT Ordre, BorneInferieure, BorneSuperieure, Taux " +
                    "FROM BaremeTranches WHERE BaremeId = @id ORDER BY Ordre";
                cmd.Parameters.AddWithValue("@id", bareme.Id);

                using (var reader = (SQLiteDataReader)await cmd.ExecuteReaderAsync())
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

        private static async Task ChargerChargesFamilleAsync(SQLiteConnection conn, BaremeLegal bareme)
        {
            bareme.ChargesFamille.Clear();

            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText =
                    "SELECT NombreCharges, Taux FROM BaremeChargesFamille " +
                    "WHERE BaremeId = @id ORDER BY NombreCharges";
                cmd.Parameters.AddWithValue("@id", bareme.Id);

                using (var reader = (SQLiteDataReader)await cmd.ExecuteReaderAsync())
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
            using (var conn = _sqliteService.CreateConnection())
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "SELECT COUNT(*) FROM Baremes WHERE DateEffet = @d AND Id <> @id";
                cmd.Parameters.AddWithValue("@d", DbValue.DateToDb(dateEffet.Date));
                cmd.Parameters.AddWithValue("@id", idAExclure);
                return DbValue.IntFromDb(await cmd.ExecuteScalarAsync()) > 0;
            }
        }

        // ── Écriture ─────────────────────────────────────────────────────────

        /// <summary>
        /// Enregistre un barème et ses collections. L'ensemble passe dans une seule
        /// transaction : un barème dont les tranches seraient écrites à moitié produirait
        /// des impôts faux sans jamais lever d'erreur.
        /// </summary>
        public async Task EnregistrerAsync(BaremeLegal bareme)
        {
            if (bareme == null) throw new ArgumentNullException("bareme");

            using (var conn = _sqliteService.CreateConnection())
            using (var transaction = conn.BeginTransaction())
            {
                try
                {
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
                            cmd.Parameters.AddWithValue("@id", bareme.Id);
                            await cmd.ExecuteNonQueryAsync();
                        }
                    }

                    await RemplacerTranchesAsync(conn, transaction, bareme);
                    await RemplacerChargesFamilleAsync(conn, transaction, bareme);

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

        private static void AjouterParametres(SQLiteCommand cmd, BaremeLegal b)
        {
            cmd.Parameters.AddWithValue("@dateEffet", DbValue.DateToDb(b.DateEffet.Date));
            cmd.Parameters.AddWithValue("@libelle", b.Libelle ?? "");
            cmd.Parameters.AddWithValue("@reference", b.Reference ?? "");
            cmd.Parameters.AddWithValue("@commentaire", b.Commentaire ?? "");
            cmd.Parameters.AddWithValue("@estSysteme", b.EstSysteme ? 1 : 0);

            cmd.Parameters.AddWithValue("@tauxCnssSalarie", DbValue.DecimalToDb(b.TauxCnssSalarie));
            cmd.Parameters.AddWithValue("@tauxPf", DbValue.DecimalToDb(b.TauxCnssPrestationsFamiliales));
            cmd.Parameters.AddWithValue("@tauxRp", DbValue.DecimalToDb(b.TauxCnssRisquesProfessionnels));
            cmd.Parameters.AddWithValue("@tauxPensions", DbValue.DecimalToDb(b.TauxCnssPensionsPatronal));
            cmd.Parameters.AddWithValue("@plafondCnss", DbValue.MoneyToDb(b.PlafondCnssMensuel));
            cmd.Parameters.AddWithValue("@tauxTpa", DbValue.DecimalToDb(b.TauxTaxePatronaleApprentissage));

            cmd.Parameters.AddWithValue("@tauxLogement", DbValue.DecimalToDb(b.TauxMaxIndemniteLogement));
            cmd.Parameters.AddWithValue("@plafondLogement", DbValue.MoneyToDb(b.PlafondIndemniteLogement));
            cmd.Parameters.AddWithValue("@tauxTransport", DbValue.DecimalToDb(b.TauxMaxIndemniteTransport));
            cmd.Parameters.AddWithValue("@plafondTransport", DbValue.MoneyToDb(b.PlafondIndemniteTransport));
            cmd.Parameters.AddWithValue("@tauxFonction", DbValue.DecimalToDb(b.TauxMaxIndemniteFonction));
            cmd.Parameters.AddWithValue("@plafondFonction", DbValue.MoneyToDb(b.PlafondIndemniteFonction));

            cmd.Parameters.AddWithValue("@tauxRetraite", DbValue.DecimalToDb(b.TauxMaxCotisationRetraiteDeductible));
            cmd.Parameters.AddWithValue("@abattementSup", DbValue.DecimalToDb(b.AbattementCategoriesSuperieures));
            cmd.Parameters.AddWithValue("@abattementAutres", DbValue.DecimalToDb(b.AbattementAutresCategories));
        }

        private static async Task RemplacerTranchesAsync(
            SQLiteConnection conn, SQLiteTransaction transaction, BaremeLegal bareme)
        {
            using (var cmd = conn.CreateCommand())
            {
                cmd.Transaction = transaction;
                cmd.CommandText = "DELETE FROM BaremeTranches WHERE BaremeId = @id;";
                cmd.Parameters.AddWithValue("@id", bareme.Id);
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
                    cmd.Parameters.AddWithValue("@bareme", bareme.Id);
                    cmd.Parameters.AddWithValue("@ordre", ordre);
                    cmd.Parameters.AddWithValue("@bas", DbValue.MoneyToDb(tranche.BorneInferieure));
                    cmd.Parameters.AddWithValue("@haut", DbValue.MoneyToDb(
                        tranche.EstDerniereTranche ? BorneSuperieureOuverte : tranche.BorneSuperieure));
                    cmd.Parameters.AddWithValue("@taux", DbValue.DecimalToDb(tranche.Taux));
                    await cmd.ExecuteNonQueryAsync();
                }

                tranche.Ordre = ordre;
                ordre++;
            }
        }

        private static async Task RemplacerChargesFamilleAsync(
            SQLiteConnection conn, SQLiteTransaction transaction, BaremeLegal bareme)
        {
            using (var cmd = conn.CreateCommand())
            {
                cmd.Transaction = transaction;
                cmd.CommandText = "DELETE FROM BaremeChargesFamille WHERE BaremeId = @id;";
                cmd.Parameters.AddWithValue("@id", bareme.Id);
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
                    cmd.Parameters.AddWithValue("@bareme", bareme.Id);
                    cmd.Parameters.AddWithValue("@nombre", charge.NombreCharges);
                    cmd.Parameters.AddWithValue("@taux", DbValue.DecimalToDb(charge.Taux));
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

            using (var conn = _sqliteService.CreateConnection())
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
                        foreach (string table in new[] { "BaremeTranches", "BaremeChargesFamille", "Baremes" })
                        {
                            using (var cmd = conn.CreateCommand())
                            {
                                cmd.Transaction = transaction;
                                cmd.CommandText = table == "Baremes"
                                    ? "DELETE FROM Baremes WHERE Id = @id;"
                                    : "DELETE FROM " + table + " WHERE BaremeId = @id;";
                                cmd.Parameters.AddWithValue("@id", bareme.Id);
                                await cmd.ExecuteNonQueryAsync();
                            }
                        }

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
