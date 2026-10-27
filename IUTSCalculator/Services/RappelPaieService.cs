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
    /// Service gérant les rappels de salaire, régularisations sur périodes closes et régularisations annuelles d'IUTS (module PAI-07).
    /// Respecte strictement le principe d'inviolabilité des périodes clôturées en imputant les correctifs sur la paie ouverte courante.
    /// </summary>
    public class RappelPaieService
    {
        private readonly SQLiteService _sqliteService;
        private readonly TaxCalculatorService _taxCalculatorService;
        private readonly AuditService _auditService;

        public const decimal PlafondCnssMensuelDefaut = 800_000m;
        public const decimal TauxCnssSalarialDefaut = 0.055m;
        public const decimal TauxCnssPatronalDefaut = 0.16m;
        public const decimal TauxTpaDefaut = 0.03m;

        public RappelPaieService(
            SQLiteService sqliteService,
            TaxCalculatorService taxCalculatorService = null,
            AuditService auditService = null)
        {
            _sqliteService = sqliteService ?? throw new ArgumentNullException(nameof(sqliteService));
            _taxCalculatorService = taxCalculatorService;
            _auditService = auditService;
        }

        #region CRUD RappelsPaie

        public async Task<List<RappelPaie>> ObtenirRappelsAsync(
            int? anneeImputation = null,
            int? moisImputation = null,
            string employeeId = null,
            StatutRappelPaie? statut = null)
        {
            var rappels = new List<RappelPaie>();

            await Task.Run(() =>
            {
                using (var connection = _sqliteService.CreerConnexion())
                {
                    var sql = @"
                        SELECT r.Id, r.EmployeeId, r.TypeRappel, r.Libelle, r.Sens,
                               r.MoisOrigineDebut, r.MoisOrigineFin, r.AnneeImputation, r.MoisImputation,
                               r.MontantBrut, r.MontantCnssSalarial, r.MontantCnssPatronal,
                               r.MontantTPA, r.MontantIUTS, r.MontantNet,
                               r.SoumisIUTS, r.SoumisCNSS, r.SoumisTPA, r.Statut,
                               r.Motif, r.DetailsCalculJson, r.DateCreation, r.DateImputation, r.CreePar,
                               e.FirstName, e.LastName
                        FROM RappelsPaie r
                        LEFT JOIN Employees e ON e.EmployeeId = r.EmployeeId
                        WHERE 1 = 1";

                    if (anneeImputation.HasValue)
                        sql += " AND r.AnneeImputation = @ai";
                    if (moisImputation.HasValue)
                        sql += " AND r.MoisImputation = @mi";
                    if (!string.IsNullOrWhiteSpace(employeeId))
                        sql += " AND r.EmployeeId = @emp";
                    if (statut.HasValue)
                        sql += " AND r.Statut = @st";

                    sql += " ORDER BY r.AnneeImputation DESC, r.MoisImputation DESC, r.Id DESC;";

                    using (var cmd = connection.CreateCommand())
                    {
                        cmd.CommandText = sql;
                        if (anneeImputation.HasValue) cmd.Parameters.AjouterValeur("@ai", anneeImputation.Value);
                        if (moisImputation.HasValue) cmd.Parameters.AjouterValeur("@mi", moisImputation.Value);
                        if (!string.IsNullOrWhiteSpace(employeeId)) cmd.Parameters.AjouterValeur("@emp", employeeId);
                        if (statut.HasValue) cmd.Parameters.AjouterValeur("@st", (int)statut.Value);

                        using (var reader = cmd.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                rappels.Add(LireRappel(reader));
                            }
                        }
                    }
                }
            });

            return rappels;
        }

        public async Task<RappelPaie> ObtenirRappelParIdAsync(int id)
        {
            return await Task.Run(() =>
            {
                using (var connection = _sqliteService.CreerConnexion())
                {
                    using (var cmd = connection.CreateCommand())
                    {
                        cmd.CommandText = @"
                            SELECT r.Id, r.EmployeeId, r.TypeRappel, r.Libelle, r.Sens,
                                   r.MoisOrigineDebut, r.MoisOrigineFin, r.AnneeImputation, r.MoisImputation,
                                   r.MontantBrut, r.MontantCnssSalarial, r.MontantCnssPatronal,
                                   r.MontantTPA, r.MontantIUTS, r.MontantNet,
                                   r.SoumisIUTS, r.SoumisCNSS, r.SoumisTPA, r.Statut,
                                   r.Motif, r.DetailsCalculJson, r.DateCreation, r.DateImputation, r.CreePar,
                                   e.FirstName, e.LastName
                            FROM RappelsPaie r
                            LEFT JOIN Employees e ON e.EmployeeId = r.EmployeeId
                            WHERE r.Id = @id LIMIT 1;";
                        cmd.Parameters.AjouterValeur("@id", id);

                        using (var reader = cmd.ExecuteReader())
                        {
                            if (reader.Read())
                                return LireRappel(reader);
                        }
                    }
                }
                return null;
            });
        }

        public async Task<int> EnregistrerRappelAsync(RappelPaie rappel)
        {
            if (rappel == null) throw new ArgumentNullException(nameof(rappel));
            if (string.IsNullOrWhiteSpace(rappel.EmployeeId)) throw new ArgumentException("L'employé est obligatoire.");
            if (string.IsNullOrWhiteSpace(rappel.Libelle)) throw new ArgumentException("Le libellé est obligatoire.");
            if (rappel.MoisImputation < 1 || rappel.MoisImputation > 12 || rappel.AnneeImputation < 2000)
                throw new ArgumentException("Le mois et l'année d'imputation sont obligatoires (mois de 1 à 12).");

            // Correction : Contrôle anti-doublon.
            // On refuse d'enregistrer un rappel si un autre rappel identique (même employé, même type,
            // même montant et même période d'origine) existe déjà et est toujours en attente ou imputé.
            if (rappel.Id == 0)
            {
                var doublon = await ObtenirRappelDoublonAsync(rappel);
                if (doublon != null)
                {
                    throw new InvalidOperationException($"Un rappel identique existe déjà pour cet employé sur la même période d'origine : {doublon.Libelle}.");
                }
            }

            return await Task.Run(async () =>
            {
                int id = rappel.Id;
                using (var connection = _sqliteService.CreerConnexion())
                {
                    // Défaut n° 9 du guide : un rappel en attente pouvait viser un mois déjà
                    // clôturé — il n'y était jamais imputé (aucun bulletin ne s'y recalcule)
                    // et restait en attente sans que rien ne le signale. Le principe annoncé
                    // (imputer sur le mois ouvert) est désormais contrôlé.
                    if (rappel.Statut == StatutRappelPaie.EnAttente &&
                        await PeriodePaieService.EstClotureeAsync(connection, new DateTime(rappel.AnneeImputation, rappel.MoisImputation, 1)))
                    {
                        throw new InvalidOperationException(string.Format(
                            "Le mois d'imputation {0:00}/{1} est clôturé : un rappel s'impute sur la paie d'un mois ouvert. " +
                            "Choisissez le mois de paie en cours.", rappel.MoisImputation, rappel.AnneeImputation));
                    }
                    using (var cmd = connection.CreateCommand())
                    {
                        if (id == 0)
                        {
                            cmd.CommandText = @"
                                INSERT INTO RappelsPaie (
                                    EmployeeId, TypeRappel, Libelle, Sens,
                                    MoisOrigineDebut, MoisOrigineFin, AnneeImputation, MoisImputation,
                                    MontantBrut, MontantCnssSalarial, MontantCnssPatronal,
                                    MontantTPA, MontantIUTS, MontantNet,
                                    SoumisIUTS, SoumisCNSS, SoumisTPA, Statut,
                                    Motif, DetailsCalculJson, DateCreation, DateImputation, CreePar
                                ) VALUES (
                                    @emp, @type, @libelle, @sens,
                                    @mod, @mof, @ai, @mi,
                                    @brut, @cnsss, @cnssp,
                                    @tpa, @iuts, @net,
                                    @siuts, @scnss, @stpa, @statut,
                                    @motif, @details, @dc, @di, @creepar
                                );
                                SELECT last_insert_rowid();";
                        }
                        else
                        {
                            cmd.CommandText = @"
                                UPDATE RappelsPaie SET
                                    EmployeeId = @emp,
                                    TypeRappel = @type,
                                    Libelle = @libelle,
                                    Sens = @sens,
                                    MoisOrigineDebut = @mod,
                                    MoisOrigineFin = @mof,
                                    AnneeImputation = @ai,
                                    MoisImputation = @mi,
                                    MontantBrut = @brut,
                                    MontantCnssSalarial = @cnsss,
                                    MontantCnssPatronal = @cnssp,
                                    MontantTPA = @tpa,
                                    MontantIUTS = @iuts,
                                    MontantNet = @net,
                                    SoumisIUTS = @siuts,
                                    SoumisCNSS = @scnss,
                                    SoumisTPA = @stpa,
                                    Statut = @statut,
                                    Motif = @motif,
                                    DetailsCalculJson = @details,
                                    DateImputation = @di
                                WHERE Id = @id;";
                            cmd.Parameters.AjouterValeur("@id", id);
                        }
                        cmd.Parameters.AjouterValeur("@emp", rappel.EmployeeId);
                        cmd.Parameters.AjouterValeur("@type", (int)rappel.TypeRappel);
                        cmd.Parameters.AjouterValeur("@libelle", rappel.Libelle ?? string.Empty);
                        cmd.Parameters.AjouterValeur("@sens", (int)rappel.Sens);
                        cmd.Parameters.AjouterValeur("@mod", rappel.MoisOrigineDebut.HasValue ? DbValue.DateToDb(rappel.MoisOrigineDebut.Value) : (object)DBNull.Value);
                        cmd.Parameters.AjouterValeur("@mof", rappel.MoisOrigineFin.HasValue ? DbValue.DateToDb(rappel.MoisOrigineFin.Value) : (object)DBNull.Value);
                        cmd.Parameters.AjouterValeur("@ai", rappel.AnneeImputation);
                        cmd.Parameters.AjouterValeur("@mi", rappel.MoisImputation);
                        cmd.Parameters.AjouterValeur("@brut", DbValue.MoneyToDb(rappel.MontantBrut));
                        cmd.Parameters.AjouterValeur("@cnsss", DbValue.MoneyToDb(rappel.MontantCnssSalarial));
                        cmd.Parameters.AjouterValeur("@cnssp", DbValue.MoneyToDb(rappel.MontantCnssPatronal));
                        cmd.Parameters.AjouterValeur("@tpa", DbValue.MoneyToDb(rappel.MontantTPA));
                        cmd.Parameters.AjouterValeur("@iuts", DbValue.MoneyToDb(rappel.MontantIUTS));
                        cmd.Parameters.AjouterValeur("@net", DbValue.MoneyToDb(rappel.MontantNet));
                        cmd.Parameters.AjouterValeur("@siuts", rappel.SoumisIUTS ? 1 : 0);
                        cmd.Parameters.AjouterValeur("@scnss", rappel.SoumisCNSS ? 1 : 0);
                        cmd.Parameters.AjouterValeur("@stpa", rappel.SoumisTPA ? 1 : 0);
                        cmd.Parameters.AjouterValeur("@statut", (int)rappel.Statut);
                        cmd.Parameters.AjouterValeur("@motif", rappel.Motif ?? (object)DBNull.Value);
                        cmd.Parameters.AjouterValeur("@details", rappel.DetailsCalculJson ?? (object)DBNull.Value);
                        cmd.Parameters.AjouterValeur("@dc", DbValue.DateToDb(rappel.DateCreation));
                        cmd.Parameters.AjouterValeur("@di", rappel.DateImputation.HasValue ? DbValue.DateToDb(rappel.DateImputation.Value) : (object)DBNull.Value);
                        cmd.Parameters.AjouterValeur("@creepar", rappel.CreePar ?? (object)DBNull.Value);
                        if (id == 0)
                        {
                            id = Convert.ToInt32(cmd.ExecuteScalar());
                            rappel.Id = id;
                        }
                        else
                        {
                            cmd.ExecuteNonQuery();
                        }
                    }
                }
                if (_auditService != null)
                {
                    try
                    {
                        await _auditService.EnregistrerAsync(
                            id == 0 ? "CREATION_RAPPEL_PAIE" : "MODIFICATION_RAPPEL_PAIE",
                            "RappelsPaie",
                            id.ToString(),
                            $"{rappel.TypeLibelle} de {rappel.MontantBrut:N0} FCFA pour {rappel.NomSalarie} ({rappel.PeriodeImputationLibelle})");
                    }
                    catch { }
                }
                return id;
            });
        }

        private async Task<RappelPaie> ObtenirRappelDoublonAsync(RappelPaie rappel)
        {
            return await Task.Run(() =>
            {
                using (var connection = _sqliteService.CreerConnexion())
                {
                    using (var cmd = connection.CreateCommand())
                    {
                        cmd.CommandText = @"
                            SELECT r.Id, r.EmployeeId, r.TypeRappel, r.Libelle, r.Sens,
                                   r.MoisOrigineDebut, r.MoisOrigineFin, r.AnneeImputation, r.MoisImputation,
                                   r.MontantBrut, r.MontantCnssSalarial, r.MontantCnssPatronal,
                                   r.MontantTPA, r.MontantIUTS, r.MontantNet,
                                   r.SoumisIUTS, r.SoumisCNSS, r.SoumisTPA, r.Statut,
                                   r.Motif, r.DetailsCalculJson, r.DateCreation, r.DateImputation, r.CreePar,
                                   e.FirstName, e.LastName
                            FROM RappelsPaie r
                            LEFT JOIN Employees e ON e.EmployeeId = r.EmployeeId
                            WHERE r.EmployeeId = @emp
                              AND r.TypeRappel = @type
                              AND r.MontantBrut = @brut
                              AND r.MoisOrigineDebut = @mod
                              AND r.MoisOrigineFin = @mof
                              AND r.Statut != 2
                            LIMIT 1;";
                        cmd.Parameters.AjouterValeur("@emp", rappel.EmployeeId);
                        cmd.Parameters.AjouterValeur("@type", (int)rappel.TypeRappel);
                        cmd.Parameters.AjouterValeur("@brut", DbValue.MoneyToDb(rappel.MontantBrut));
                        cmd.Parameters.AjouterValeur("@mod", rappel.MoisOrigineDebut.HasValue ? DbValue.DateToDb(rappel.MoisOrigineDebut.Value) : (object)DBNull.Value);
                        cmd.Parameters.AjouterValeur("@mof", rappel.MoisOrigineFin.HasValue ? DbValue.DateToDb(rappel.MoisOrigineFin.Value) : (object)DBNull.Value);
                        using (var reader = cmd.ExecuteReader())
                        {
                            if (reader.Read()) return LireRappel(reader);
                        }
                    }
                }
                return null;
            });
        }

        public async Task<bool> SupprimerRappelAsync(int id)
        {
            return await Task.Run(async () =>
            {
                using (var connection = _sqliteService.CreerConnexion())
                {
                    using (var cmd = connection.CreateCommand())
                    {
                        cmd.CommandText = "DELETE FROM RappelsPaie WHERE Id = @id AND Statut != 1;";
                        cmd.Parameters.AjouterValeur("@id", id);
                        int affected = cmd.ExecuteNonQuery();
                        if (affected > 0 && _auditService != null)
                        {
                            try
                            {
                                await _auditService.EnregistrerAsync(
                                    "SUPPRESSION_RAPPEL_PAIE", "RappelsPaie", id.ToString(),
                                    $"Suppression du rappel n°{id}");
                            }
                            catch { }
                        }
                        return affected > 0;
                    }
                }
            });
        }

        public async Task<bool> MarquerImputeAsync(int id, DateTime dateImputation)
        {
            return await Task.Run(() =>
            {
                using (var connection = _sqliteService.CreerConnexion())
                {
                    using (var cmd = connection.CreateCommand())
                    {
                        cmd.CommandText = @"
                            UPDATE RappelsPaie 
                            SET Statut = 1, DateImputation = @di 
                            WHERE Id = @id;";
                        cmd.Parameters.AjouterValeur("@id", id);
                        cmd.Parameters.AjouterValeur("@di", DbValue.DateToDb(dateImputation));
                        return cmd.ExecuteNonQuery() > 0;
                    }
                }
            });
        }

        public async Task<int> MarquerImputesMoisAsync(int annee, int mois, string employeeId = null, DateTime? dateImputation = null)
        {
            return await Task.Run(() =>
            {
                using (var connection = _sqliteService.CreerConnexion())
                {
                    using (var cmd = connection.CreateCommand())
                    {
                        var sql = @"
                            UPDATE RappelsPaie 
                            SET Statut = 1, DateImputation = @di 
                            WHERE AnneeImputation = @ai AND MoisImputation = @mi AND Statut = 0";
                        if (!string.IsNullOrWhiteSpace(employeeId))
                        {
                            sql += " AND EmployeeId = @emp";
                            cmd.Parameters.AjouterValeur("@emp", employeeId);
                        }
                        cmd.CommandText = sql;
                        cmd.Parameters.AjouterValeur("@ai", annee);
                        cmd.Parameters.AjouterValeur("@mi", mois);
                        cmd.Parameters.AjouterValeur("@di", DbValue.DateToDb(dateImputation ?? DateTime.Today));
                        return cmd.ExecuteNonQuery();
                    }
                }
            });
        }

        public async Task<bool> MarquerAnnuleAsync(int id)
        {
            return await Task.Run(() =>
            {
                using (var connection = _sqliteService.CreerConnexion())
                {
                    using (var cmd = connection.CreateCommand())
                    {
                        cmd.CommandText = "UPDATE RappelsPaie SET Statut = 2 WHERE Id = @id;";
                        cmd.Parameters.AjouterValeur("@id", id);
                        return cmd.ExecuteNonQuery() > 0;
                    }
                }
            });
        }

        private static RappelPaie LireRappel(SqliteDataReader reader)
        {
            string prenom = Convert.ToString(reader["FirstName"] ?? string.Empty);
            string nom = Convert.ToString(reader["LastName"] ?? string.Empty);
            string nomComplet = $"{nom} {prenom}".Trim();

            return new RappelPaie
            {
                Id = DbValue.IntFromDb(reader["Id"]),
                EmployeeId = Convert.ToString(reader["EmployeeId"]),
                NomSalarie = nomComplet,
                Matricule = Convert.ToString(reader["EmployeeId"]),
                TypeRappel = (TypeRappelPaie)DbValue.IntFromDb(reader["TypeRappel"]),
                Libelle = Convert.ToString(reader["Libelle"]),
                Sens = (SensRappel)DbValue.IntFromDb(reader["Sens"]),
                MoisOrigineDebut = DbValue.DateNullableFromDb(reader["MoisOrigineDebut"]),
                MoisOrigineFin = DbValue.DateNullableFromDb(reader["MoisOrigineFin"]),
                AnneeImputation = DbValue.IntFromDb(reader["AnneeImputation"]),
                MoisImputation = DbValue.IntFromDb(reader["MoisImputation"]),
                MontantBrut = DbValue.MoneyFromDb(reader["MontantBrut"]),
                MontantCnssSalarial = DbValue.MoneyFromDb(reader["MontantCnssSalarial"]),
                MontantCnssPatronal = DbValue.MoneyFromDb(reader["MontantCnssPatronal"]),
                MontantTPA = DbValue.MoneyFromDb(reader["MontantTPA"]),
                MontantIUTS = DbValue.MoneyFromDb(reader["MontantIUTS"]),
                MontantNet = DbValue.MoneyFromDb(reader["MontantNet"]),
                SoumisIUTS = DbValue.IntFromDb(reader["SoumisIUTS"]) == 1,
                SoumisCNSS = DbValue.IntFromDb(reader["SoumisCNSS"]) == 1,
                SoumisTPA = DbValue.IntFromDb(reader["SoumisTPA"]) == 1,
                Statut = (StatutRappelPaie)DbValue.IntFromDb(reader["Statut"]),
                Motif = Convert.ToString(reader["Motif"]),
                DetailsCalculJson = Convert.ToString(reader["DetailsCalculJson"]),
                DateCreation = DbValue.DateFromDb(reader["DateCreation"], DateTime.Now),
                DateImputation = DbValue.DateNullableFromDb(reader["DateImputation"]),
                CreePar = Convert.ToString(reader["CreePar"])
            };
        }

        #endregion

        #region Moteur de calcul différentiel rétroactif

        /// <summary>
        /// Mois de paie sur lequel imputer un rappel par défaut : la période ouverte la plus
        /// récente ; à défaut, le mois qui suit la dernière période clôturée ; à défaut, le
        /// mois en cours. « Créer le rappel pour le mois courant » prenait le mois de
        /// l'horloge du poste, qui peut être un mois déjà clôturé ou pas encore ouvert.
        /// </summary>
        public async Task<DateTime> MoisImputationParDefautAsync()
        {
            var aujourdhui = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
            try
            {
                using (var connection = _sqliteService.CreerConnexion())
                {
                    using (var cmd = connection.CreateCommand())
                    {
                        cmd.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='PeriodesPaie';";
                        if (Convert.ToInt32(await cmd.ExecuteScalarAsync()) == 0) return aujourdhui;
                    }
                    using (var cmd = connection.CreateCommand())
                    {
                        cmd.CommandText = "SELECT MAX(Annee * 100 + Mois) FROM PeriodesPaie WHERE Statut = 0;";
                        object o = await cmd.ExecuteScalarAsync();
                        if (o != null && o != DBNull.Value && Convert.ToInt32(o) > 0)
                        {
                            int am = Convert.ToInt32(o);
                            return new DateTime(am / 100, am % 100, 1);
                        }
                    }
                    using (var cmd = connection.CreateCommand())
                    {
                        cmd.CommandText = "SELECT MAX(Annee * 100 + Mois) FROM PeriodesPaie WHERE Statut = 1;";
                        object o = await cmd.ExecuteScalarAsync();
                        if (o != null && o != DBNull.Value && Convert.ToInt32(o) > 0)
                        {
                            int am = Convert.ToInt32(o);
                            return new DateTime(am / 100, am % 100, 1).AddMonths(1);
                        }
                    }
                }
            }
            catch (SqliteException)
            {
                // Base illisible : repli sur le mois en cours.
            }
            return aujourdhui;
        }

        /// <summary>
        /// Écart d'IUTS qu'un gain imposable et cotisable de <paramref name="gain"/> FCFA
        /// produit sur le bulletin du salarié (même moteur que la paie, barème courant).
        /// Renvoie 0 si le moteur n'est pas branché.
        /// </summary>
        public decimal DeltaIutsPourGain(Employee employee, decimal gain)
        {
            if (_taxCalculatorService == null || employee == null || gain == 0m) return 0m;

            var sans = _taxCalculatorService.CalculateMonthlyTax(employee, ApportVariables.Aucun());
            var rappel = new ApportRappels();
            rappel.AjouterLigne("RAPPELSAL", "Simulation de rappel", gain > 0m ? SensRubrique.Gain : SensRubrique.Retenue,
                                Math.Abs(gain), true, true, true);
            var avec = _taxCalculatorService.CalculateMonthlyTax(employee, ApportVariables.Aucun().AvecRappels(rappel));
            return Math.Round(avec.IUTSNet - sans.IUTSNet, 0, MidpointRounding.AwayFromZero);
        }

        /// <summary>
        /// Calcule le différentiel rétroactif mois par mois sur une plage de mois passés fermés.
        /// Respecte le plafonnement de la CNSS (800 000 FCFA/mois) et les taux légaux.
        /// </summary>
        public (List<LigneRappelRetroactifDetail> Details,
                decimal TotalBrut,
                decimal TotalCnssSalarial,
                decimal TotalCnssPatronal,
                decimal TotalTpa,
                decimal TotalNet)
            CalculerDifferentielRetroactif(
                Employee employee,
                DateTime debut,
                DateTime fin,
                decimal augmentationSalaireBase,
                decimal primeRetroactive = 0m,
                decimal tauxTpa = TauxTpaDefaut,
                decimal plafondCnss = PlafondCnssMensuelDefaut)
        {
            if (employee == null) throw new ArgumentNullException(nameof(employee));
            if (debut > fin) throw new ArgumentException("La date de début doit être antérieure ou égale à la date de fin.");

            var details = new List<LigneRappelRetroactifDetail>();
            DateTime curseur = new DateTime(debut.Year, debut.Month, 1);
            DateTime borneFin = new DateTime(fin.Year, fin.Month, 1);

            decimal cumulBrut = 0m;
            decimal cumulCnssSalarial = 0m;
            decimal cumulCnssPatronal = 0m;
            decimal cumulTpa = 0m;
            decimal cumulNet = 0m;

            while (curseur <= borneFin)
            {
                decimal ancienBase = employee.BaseSalary;
                decimal nouveauBase = ancienBase + augmentationSalaireBase;
                decimal deltaBase = augmentationSalaireBase;
                decimal deltaBrut = deltaBase + primeRetroactive;

                // Capping CNSS : comparaison de l'assiette plafonnée avant et après
                decimal ancienBrutTotal = ancienBase;
                decimal nouveauBrutTotal = ancienBrutTotal + deltaBrut;

                decimal ancienneAssietteCnss = Math.Min(ancienBrutTotal, plafondCnss);
                decimal nouvelleAssietteCnss = Math.Min(nouveauBrutTotal, plafondCnss);
                decimal deltaAssietteCnss = Math.Max(0m, nouvelleAssietteCnss - ancienneAssietteCnss);

                decimal deltaCnssSalarial = Math.Round(deltaAssietteCnss * TauxCnssSalarialDefaut, MidpointRounding.AwayFromZero);
                decimal deltaCnssPatronal = Math.Round(deltaAssietteCnss * TauxCnssPatronalDefaut, MidpointRounding.AwayFromZero);
                decimal deltaTpa = Math.Round(deltaBrut * tauxTpa, MidpointRounding.AwayFromZero);

                // Défaut n° 9 : l'écart d'IUTS valait toujours 0. Il est simulé par le
                // moteur de paie, mois par mois, comme si la hausse avait été payée à temps.
                decimal deltaIuts = DeltaIutsPourGain(employee, deltaBrut);
                decimal deltaNet = deltaBrut - deltaCnssSalarial - deltaIuts;

                details.Add(new LigneRappelRetroactifDetail
                {
                    Annee = curseur.Year,
                    Mois = curseur.Month,
                    AncienSalaireDeBase = ancienBase,
                    NouveauSalaireDeBase = nouveauBase,
                    DeltaSalaireDeBase = deltaBase,
                    DeltaIndemnites = primeRetroactive,
                    DeltaBrut = deltaBrut,
                    DeltaAssietteCnss = deltaAssietteCnss,
                    DeltaCnssSalarial = deltaCnssSalarial,
                    DeltaCnssPatronal = deltaCnssPatronal,
                    DeltaTpa = deltaTpa,
                    DeltaIuts = deltaIuts,
                    DeltaNet = deltaNet,
                    Observation = deltaAssietteCnss < deltaBrut ? "Assiette CNSS plafonnée à 800 000 FCFA" : "Plein différentiel"
                });

                cumulBrut += deltaBrut;
                cumulCnssSalarial += deltaCnssSalarial;
                cumulCnssPatronal += deltaCnssPatronal;
                cumulTpa += deltaTpa;
                cumulNet += deltaNet;

                curseur = curseur.AddMonths(1);
            }

            return (details, cumulBrut, cumulCnssSalarial, cumulCnssPatronal, cumulTpa, cumulNet);
        }

        #endregion

        #region Moteur de régularisation annuelle d'IUTS (CGI Burkina Faso)

        public async Task<List<RegularisationIutsAnnuel>> ObtenirRegularisationsIutsAsync(int annee)
        {
            var list = new List<RegularisationIutsAnnuel>();
            await Task.Run(() =>
            {
                using (var connection = _sqliteService.CreerConnexion())
                {
                    using (var cmd = connection.CreateCommand())
                    {
                        cmd.CommandText = @"
                            SELECT r.Id, r.EmployeeId, r.Annee, r.NombreMoisTravailles,
                                   r.CumulBrutImposable, r.IutsTheoriqueAnnuel, r.CumulIutsPreleve,
                                   r.DifferenceRegularisation, r.MoisImputation, r.Statut,
                                   r.RappelPaieId, r.Observations, r.DateCreation, r.DateModification,
                                   e.FirstName, e.LastName
                            FROM RegularisationsIUTSAnnuel r
                            LEFT JOIN Employees e ON e.EmployeeId = r.EmployeeId
                            WHERE r.Annee = @annee
                            ORDER BY e.LastName, e.FirstName;";
                        cmd.Parameters.AjouterValeur("@annee", annee);

                        using (var reader = cmd.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                string prenom = Convert.ToString(reader["FirstName"] ?? string.Empty);
                                string nom = Convert.ToString(reader["LastName"] ?? string.Empty);
                                list.Add(new RegularisationIutsAnnuel
                                {
                                    Id = DbValue.IntFromDb(reader["Id"]),
                                    EmployeeId = Convert.ToString(reader["EmployeeId"]),
                                    NomSalarie = $"{nom} {prenom}".Trim(),
                                    Matricule = Convert.ToString(reader["EmployeeId"]),
                                    Annee = DbValue.IntFromDb(reader["Annee"]),
                                    NombreMoisTravailles = DbValue.IntFromDb(reader["NombreMoisTravailles"]),
                                    CumulBrutImposable = DbValue.MoneyFromDb(reader["CumulBrutImposable"]),
                                    IutsTheoriqueAnnuel = DbValue.MoneyFromDb(reader["IutsTheoriqueAnnuel"]),
                                    CumulIutsPreleve = DbValue.MoneyFromDb(reader["CumulIutsPreleve"]),
                                    DifferenceRegularisation = DbValue.MoneyFromDb(reader["DifferenceRegularisation"]),
                                    MoisImputation = DbValue.IntFromDb(reader["MoisImputation"]),
                                    Statut = (StatutRegulIuts)DbValue.IntFromDb(reader["Statut"]),
                                    RappelPaieId = reader["RappelPaieId"] == DBNull.Value ? (int?)null : DbValue.IntFromDb(reader["RappelPaieId"]),
                                    Observations = Convert.ToString(reader["Observations"]),
                                    DateCreation = DbValue.DateFromDb(reader["DateCreation"], DateTime.Now),
                                    DateModification = DbValue.DateFromDb(reader["DateModification"], DateTime.Now)
                                });
                            }
                        }
                    }
                }
            });
            return list;
        }

        public async Task<int> EnregistrerRegularisationIutsAsync(RegularisationIutsAnnuel regul)
        {
            if (regul == null) throw new ArgumentNullException(nameof(regul));
            return await Task.Run(() =>
            {
                using (var connection = _sqliteService.CreerConnexion())
                {
                    using (var cmd = connection.CreateCommand())
                    {
                        if (regul.Id == 0)
                        {
                            cmd.CommandText = @"
                                INSERT INTO RegularisationsIUTSAnnuel (
                                    EmployeeId, Annee, NombreMoisTravailles,
                                    CumulBrutImposable, IutsTheoriqueAnnuel, CumulIutsPreleve,
                                    DifferenceRegularisation, MoisImputation, Statut,
                                    RappelPaieId, Observations, DateCreation, DateModification
                                ) VALUES (
                                    @emp, @annee, @nmois,
                                    @brut, @theorique, @preleve,
                                    @diff, @mi, @statut,
                                    @rpid, @obs, @dc, @dm
                                );
                                SELECT last_insert_rowid();";
                        }
                        else
                        {
                            cmd.CommandText = @"
                                UPDATE RegularisationsIUTSAnnuel SET
                                    EmployeeId = @emp,
                                    Annee = @annee,
                                    NombreMoisTravailles = @nmois,
                                    CumulBrutImposable = @brut,
                                    IutsTheoriqueAnnuel = @theorique,
                                    CumulIutsPreleve = @preleve,
                                    DifferenceRegularisation = @diff,
                                    MoisImputation = @mi,
                                    Statut = @statut,
                                    RappelPaieId = @rpid,
                                    Observations = @obs,
                                    DateModification = @dm
                                WHERE Id = @id;";
                            cmd.Parameters.AjouterValeur("@id", regul.Id);
                        }

                        cmd.Parameters.AjouterValeur("@emp", regul.EmployeeId);
                        cmd.Parameters.AjouterValeur("@annee", regul.Annee);
                        cmd.Parameters.AjouterValeur("@nmois", regul.NombreMoisTravailles);
                        cmd.Parameters.AjouterValeur("@brut", DbValue.MoneyToDb(regul.CumulBrutImposable));
                        cmd.Parameters.AjouterValeur("@theorique", DbValue.MoneyToDb(regul.IutsTheoriqueAnnuel));
                        cmd.Parameters.AjouterValeur("@preleve", DbValue.MoneyToDb(regul.CumulIutsPreleve));
                        cmd.Parameters.AjouterValeur("@diff", DbValue.MoneyToDb(regul.DifferenceRegularisation));
                        cmd.Parameters.AjouterValeur("@mi", regul.MoisImputation);
                        cmd.Parameters.AjouterValeur("@statut", (int)regul.Statut);
                        cmd.Parameters.AjouterValeur("@rpid", regul.RappelPaieId.HasValue ? (object)regul.RappelPaieId.Value : DBNull.Value);
                        cmd.Parameters.AjouterValeur("@obs", regul.Observations ?? (object)DBNull.Value);
                        cmd.Parameters.AjouterValeur("@dc", DbValue.DateToDb(regul.DateCreation));
                        cmd.Parameters.AjouterValeur("@dm", DbValue.DateToDb(DateTime.Now));

                        if (regul.Id == 0)
                        {
                            regul.Id = Convert.ToInt32(cmd.ExecuteScalar());
                        }
                        else
                        {
                            cmd.ExecuteNonQuery();
                        }
                    }
                }
                return regul.Id;
            });
        }

        /// <summary>
        /// Exécute la simulation de régularisation annuelle d'IUTS pour tous les salariés de l'entreprise sur l'exercice.
        /// Compare l'IUTS théorique annuel au total des acomptes effectivement prélevés.
        /// </summary>
        public async Task<List<RegularisationIutsAnnuel>> CalculerRegularisationAnnuelleTousSalariesAsync(
            int annee,
            IEnumerable<Employee> employes)
        {
            var resultat = new List<RegularisationIutsAnnuel>();
            if (employes == null) return resultat;

            // Récupérer l'historique des calculs mensuels enregistrés sur l'année
            var calculsParEmploye = new Dictionary<string, List<(decimal BaseIuts, decimal IutsNet, DateTime DateCalc)>>();

            await Task.Run(() =>
            {
                using (var connection = _sqliteService.CreerConnexion())
                {
                    using (var cmd = connection.CreateCommand())
                    {
                        cmd.CommandText = @"
                            SELECT EmployeeId, BaseIUTSHorsDeduction, IUTSNet, CalculationDate
                            FROM TaxCalculations
                            WHERE CalculationDate LIKE @anneePrefix
                              AND IsAnnual = 0 AND IsRegularization = 0
                            ORDER BY CalculationDate ASC;";
                        cmd.Parameters.AjouterValeur("@anneePrefix", $"{annee:0000}-%");

                        using (var reader = cmd.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                string empId = Convert.ToString(reader["EmployeeId"]);
                                decimal baseIuts = DbValue.MoneyFromDb(reader["BaseIUTSHorsDeduction"]);
                                decimal iutsNet = DbValue.MoneyFromDb(reader["IUTSNet"]);
                                DateTime dateCalc = DbValue.DateFromDb(reader["CalculationDate"], new DateTime(annee, 1, 1));

                                if (!calculsParEmploye.TryGetValue(empId, out var liste))
                                {
                                    liste = new List<(decimal, decimal, DateTime)>();
                                    calculsParEmploye[empId] = liste;
                                }
                                liste.Add((baseIuts, iutsNet, dateCalc));
                            }
                        }
                    }
                }
            });

            foreach (var emp in employes)
            {
                if (emp == null) continue;

                calculsParEmploye.TryGetValue(emp.EmployeeId, out var historique);
                historique = historique ?? new List<(decimal, decimal, DateTime)>();

                int nbMois = historique.Count;
                if (nbMois == 0)
                {
                    // Aucun bulletin enregistré sur l'exercice pour cet employé
                    continue;
                }

                decimal cumulBaseImposable = historique.Sum(h => h.BaseIuts);
                decimal cumulIutsPreleve = historique.Sum(h => h.IutsNet);

                // Annualisation : revenu mensuel moyen équivalent
                decimal revenuMoyenMensuel = cumulBaseImposable / nbMois;

                // Calcul de l'impôt théorique sur barème mensuel progressif
                decimal impotBrutMensuel = _taxCalculatorService != null
                    ? _taxCalculatorService.CalculateGrossTax(revenuMoyenMensuel)
                    : CalculerImpotBrutParDefaut(revenuMoyenMensuel);

                decimal impotBrutAnnuel = impotBrutMensuel * nbMois;

                // Réduction pour charges familiales
                int charges = Math.Min(Math.Max(0, emp.FamilyCharges), 4);
                decimal tauxReduction = 0m;
                switch (charges)
                {
                    case 1: tauxReduction = 0.08m; break;
                    case 2: tauxReduction = 0.10m; break;
                    case 3: tauxReduction = 0.12m; break;
                    case 4: tauxReduction = 0.14m; break;
                }

                decimal reductionAnnuelle = Math.Round(impotBrutAnnuel * tauxReduction, MidpointRounding.AwayFromZero);
                decimal iutsTheoriqueAnnuel = Math.Max(0m, impotBrutAnnuel - reductionAnnuelle);

                // Écart de régularisation : Théorique - Prélevé
                decimal difference = iutsTheoriqueAnnuel - cumulIutsPreleve;

                resultat.Add(new RegularisationIutsAnnuel
                {
                    EmployeeId = emp.EmployeeId,
                    NomSalarie = $"{emp.LastName} {emp.FirstName}".Trim(),
                    Matricule = emp.EmployeeId,
                    Annee = annee,
                    NombreMoisTravailles = nbMois,
                    CumulBrutImposable = cumulBaseImposable,
                    IutsTheoriqueAnnuel = iutsTheoriqueAnnuel,
                    CumulIutsPreleve = cumulIutsPreleve,
                    DifferenceRegularisation = difference,
                    MoisImputation = 12,
                    Statut = StatutRegulIuts.Simule,
                    Observations = difference > 0m
                        ? $"Complément d'IUTS de {difference:N0} FCFA dû en fin d'exercice"
                        : (difference < 0m
                            ? $"Crédit d'IUTS de {Math.Abs(difference):N0} FCFA en faveur du salarié"
                            : "Situation fiscale conforme")
                });
            }

            return resultat;
        }

        /// <summary>
        /// Convertit une régularisation annuelle d'IUTS validée en ligne de rappel pour le bulletin de décembre.
        /// </summary>
        public async Task<RappelPaie> GenererRappelDepuisRegulIutsAsync(
            RegularisationIutsAnnuel regul,
            int anneeCible,
            int moisCible = 12)
        {
            if (regul == null) throw new ArgumentNullException(nameof(regul));
            if (regul.DifferenceRegularisation == 0m) return null;

            bool estDebit = regul.DifferenceRegularisation > 0m;
            decimal montantAbsolu = Math.Abs(regul.DifferenceRegularisation);

            var rappel = new RappelPaie
            {
                EmployeeId = regul.EmployeeId,
                NomSalarie = regul.NomSalarie,
                Matricule = regul.Matricule,
                TypeRappel = TypeRappelPaie.RegularisationIuts,
                Libelle = estDebit
                    ? $"Régularisation IUTS annuelle {regul.Annee} (Complément d'impôt)"
                    : $"Régularisation IUTS annuelle {regul.Annee} (Crédit d'impôt restitué)",
                Sens = estDebit ? SensRappel.Retenue : SensRappel.Gain,
                MoisOrigineDebut = new DateTime(regul.Annee, 1, 1),
                MoisOrigineFin = new DateTime(regul.Annee, 12, 31),
                AnneeImputation = anneeCible,
                MoisImputation = moisCible,
                MontantBrut = montantAbsolu,
                MontantNet = montantAbsolu,
                SoumisIUTS = false, // Ne pas ré-imposer le remboursement ou le complément d'impôt
                SoumisCNSS = false,
                SoumisTPA = false,
                Statut = StatutRappelPaie.EnAttente,
                Motif = $"Régularisation annuelle d'IUTS {regul.Annee} : impôt théorique {regul.IutsTheoriqueAnnuel:N0} FCFA vs acomptes prélevés {regul.CumulIutsPreleve:N0} FCFA",
                DateCreation = DateTime.Now
            };

            int id = await EnregistrerRappelAsync(rappel);
            rappel.Id = id;

            // Lier la régularisation au rappel créé
            regul.RappelPaieId = id;
            regul.Statut = StatutRegulIuts.Valide;
            await EnregistrerRegularisationIutsAsync(regul);

            return rappel;
        }

        private static decimal CalculerImpotBrutParDefaut(decimal baseImposable)
        {
            // Barème légal progressif mensuel du Burkina Faso (CGI Art. 109)
            // 0 à 30 000 : 0 %
            // 30 001 à 50 000 : 12,10 %
            // 50 001 à 80 000 : 13,90 %
            // 80 001 à 120 000 : 15,70 %
            // 120 001 à 170 000 : 18,40 %
            // 170 001 à 250 000 : 21,70 %
            // Plus de 250 000 : 25,00 %
            decimal impot = 0m;
            if (baseImposable <= 30_000m) return 0m;

            if (baseImposable > 30_000m)
                impot += Math.Min(baseImposable - 30_000m, 20_000m) * 0.121m;
            if (baseImposable > 50_000m)
                impot += Math.Min(baseImposable - 50_000m, 30_000m) * 0.139m;
            if (baseImposable > 80_000m)
                impot += Math.Min(baseImposable - 80_000m, 40_000m) * 0.157m;
            if (baseImposable > 120_000m)
                impot += Math.Min(baseImposable - 120_000m, 50_000m) * 0.184m;
            if (baseImposable > 170_000m)
                impot += Math.Min(baseImposable - 170_000m, 80_000m) * 0.217m;
            if (baseImposable > 250_000m)
                impot += (baseImposable - 250_000m) * 0.250m;

            return Math.Round(impot, MidpointRounding.AwayFromZero);
        }

        #endregion

        #region Branchement Bulletin & ApportVariables

        /// <summary>
        /// Rappels du mois d'imputation d'un salarié : en attente ET déjà imputés à CE mois.
        ///
        /// Seuls les rappels « En attente » étaient repris ; or l'enregistrement d'un bulletin
        /// les marque « Imputé ». Recalculer un mois déjà enregistré (Calcul en lot, Calcul
        /// d'IUTS, plage de mois, régénération du bulletin, bulletin de sortie) perdait donc
        /// ses rappels. Un rappel imputé reste attaché à son propre mois d'imputation, et à
        /// lui seul ; un rappel annulé n'est jamais repris.
        /// </summary>
        public async Task<ApportRappels> ApportAsync(int annee, int mois, string employeeId)
        {
            var rappels = await ObtenirRappelsDuMoisAsync(annee, mois, employeeId);
            return ApportDe(rappels, employeeId);
        }

        /// <summary>Rappels imputables au mois (en attente ou imputés à ce mois), hors annulés.</summary>
        public async Task<List<RappelPaie>> ObtenirRappelsDuMoisAsync(int annee, int mois, string employeeId = null)
        {
            var rappels = await ObtenirRappelsAsync(annee, mois, employeeId, null);
            return rappels.Where(r => r.Statut != StatutRappelPaie.Annule).ToList();
        }

        public static ApportRappels ApportDe(IEnumerable<RappelPaie> tousLesRappels, string employeeId)
        {
            var apport = new ApportRappels();
            if (tousLesRappels == null) return apport;

            // En attente ou déjà imputé : la liste fournie est celle du mois calculé
            // (ObtenirRappelsDuMoisAsync) ; un rappel annulé n'est jamais repris.
            var pourEmploye = tousLesRappels.Where(r =>
                string.Equals(r.EmployeeId, employeeId, StringComparison.OrdinalIgnoreCase) &&
                r.Statut != StatutRappelPaie.Annule);

            foreach (var r in pourEmploye)
            {
                string codeRubrique;
                switch (r.TypeRappel)
                {
                    case TypeRappelPaie.RappelSalaire: codeRubrique = "RAPPELSAL"; break;
                    case TypeRappelPaie.RappelPrime: codeRubrique = "RAPPELPRIME"; break;
                    case TypeRappelPaie.OmissionHeuresSup: codeRubrique = "HSUP"; break;
                    case TypeRappelPaie.TropPercuSalaire: codeRubrique = "TROPPERCU"; break;
                    case TypeRappelPaie.RegularisationIuts: codeRubrique = "REGULIUTS"; break;
                    default: codeRubrique = r.Sens == SensRappel.Gain ? "RAPPELSAL" : "TROPPERCU"; break;
                }

                SensRubrique sensRubrique = r.Sens == SensRappel.Gain ? SensRubrique.Gain : SensRubrique.Retenue;

                apport.AjouterLigne(
                    codeRubrique,
                    r.Libelle,
                    sensRubrique,
                    r.MontantBrut,
                    r.SoumisIUTS,
                    r.SoumisCNSS,
                    r.SoumisTPA,
                    r.Id);
            }

            return apport;
        }

        #endregion
    }
}
