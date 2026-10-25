using System;
using System.Collections.Generic;
using System.Data.SQLite;
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
                using (var connection = _sqliteService.CreateConnection())
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
                        if (anneeImputation.HasValue) cmd.Parameters.AddWithValue("@ai", anneeImputation.Value);
                        if (moisImputation.HasValue) cmd.Parameters.AddWithValue("@mi", moisImputation.Value);
                        if (!string.IsNullOrWhiteSpace(employeeId)) cmd.Parameters.AddWithValue("@emp", employeeId);
                        if (statut.HasValue) cmd.Parameters.AddWithValue("@st", (int)statut.Value);

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
                using (var connection = _sqliteService.CreateConnection())
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
                        cmd.Parameters.AddWithValue("@id", id);

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

            return await Task.Run(async () =>
            {
                int id = rappel.Id;
                using (var connection = _sqliteService.CreateConnection())
                {
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
                            cmd.Parameters.AddWithValue("@id", id);
                        }

                        cmd.Parameters.AddWithValue("@emp", rappel.EmployeeId);
                        cmd.Parameters.AddWithValue("@type", (int)rappel.TypeRappel);
                        cmd.Parameters.AddWithValue("@libelle", rappel.Libelle ?? string.Empty);
                        cmd.Parameters.AddWithValue("@sens", (int)rappel.Sens);
                        cmd.Parameters.AddWithValue("@mod", rappel.MoisOrigineDebut.HasValue ? DbValue.DateToDb(rappel.MoisOrigineDebut.Value) : (object)DBNull.Value);
                        cmd.Parameters.AddWithValue("@mof", rappel.MoisOrigineFin.HasValue ? DbValue.DateToDb(rappel.MoisOrigineFin.Value) : (object)DBNull.Value);
                        cmd.Parameters.AddWithValue("@ai", rappel.AnneeImputation);
                        cmd.Parameters.AddWithValue("@mi", rappel.MoisImputation);
                        cmd.Parameters.AddWithValue("@brut", DbValue.MoneyToDb(rappel.MontantBrut));
                        cmd.Parameters.AddWithValue("@cnsss", DbValue.MoneyToDb(rappel.MontantCnssSalarial));
                        cmd.Parameters.AddWithValue("@cnssp", DbValue.MoneyToDb(rappel.MontantCnssPatronal));
                        cmd.Parameters.AddWithValue("@tpa", DbValue.MoneyToDb(rappel.MontantTPA));
                        cmd.Parameters.AddWithValue("@iuts", DbValue.MoneyToDb(rappel.MontantIUTS));
                        cmd.Parameters.AddWithValue("@net", DbValue.MoneyToDb(rappel.MontantNet));
                        cmd.Parameters.AddWithValue("@siuts", rappel.SoumisIUTS ? 1 : 0);
                        cmd.Parameters.AddWithValue("@scnss", rappel.SoumisCNSS ? 1 : 0);
                        cmd.Parameters.AddWithValue("@stpa", rappel.SoumisTPA ? 1 : 0);
                        cmd.Parameters.AddWithValue("@statut", (int)rappel.Statut);
                        cmd.Parameters.AddWithValue("@motif", rappel.Motif ?? (object)DBNull.Value);
                        cmd.Parameters.AddWithValue("@details", rappel.DetailsCalculJson ?? (object)DBNull.Value);
                        cmd.Parameters.AddWithValue("@dc", DbValue.DateToDb(rappel.DateCreation));
                        cmd.Parameters.AddWithValue("@di", rappel.DateImputation.HasValue ? DbValue.DateToDb(rappel.DateImputation.Value) : (object)DBNull.Value);
                        cmd.Parameters.AddWithValue("@creepar", rappel.CreePar ?? (object)DBNull.Value);

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

        public async Task<bool> SupprimerRappelAsync(int id)
        {
            return await Task.Run(async () =>
            {
                using (var connection = _sqliteService.CreateConnection())
                {
                    using (var cmd = connection.CreateCommand())
                    {
                        cmd.CommandText = "DELETE FROM RappelsPaie WHERE Id = @id AND Statut != 1;";
                        cmd.Parameters.AddWithValue("@id", id);
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
                using (var connection = _sqliteService.CreateConnection())
                {
                    using (var cmd = connection.CreateCommand())
                    {
                        cmd.CommandText = @"
                            UPDATE RappelsPaie 
                            SET Statut = 1, DateImputation = @di 
                            WHERE Id = @id;";
                        cmd.Parameters.AddWithValue("@id", id);
                        cmd.Parameters.AddWithValue("@di", DbValue.DateToDb(dateImputation));
                        return cmd.ExecuteNonQuery() > 0;
                    }
                }
            });
        }

        public async Task<int> MarquerImputesMoisAsync(int annee, int mois, string employeeId = null, DateTime? dateImputation = null)
        {
            return await Task.Run(() =>
            {
                using (var connection = _sqliteService.CreateConnection())
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
                            cmd.Parameters.AddWithValue("@emp", employeeId);
                        }
                        cmd.CommandText = sql;
                        cmd.Parameters.AddWithValue("@ai", annee);
                        cmd.Parameters.AddWithValue("@mi", mois);
                        cmd.Parameters.AddWithValue("@di", DbValue.DateToDb(dateImputation ?? DateTime.Today));
                        return cmd.ExecuteNonQuery();
                    }
                }
            });
        }

        public async Task<bool> MarquerAnnuleAsync(int id)
        {
            return await Task.Run(() =>
            {
                using (var connection = _sqliteService.CreateConnection())
                {
                    using (var cmd = connection.CreateCommand())
                    {
                        cmd.CommandText = "UPDATE RappelsPaie SET Statut = 2 WHERE Id = @id;";
                        cmd.Parameters.AddWithValue("@id", id);
                        return cmd.ExecuteNonQuery() > 0;
                    }
                }
            });
        }

        private static RappelPaie LireRappel(SQLiteDataReader reader)
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
                decimal deltaNet = deltaBrut - deltaCnssSalarial;

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
                    DeltaIuts = 0m,
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
                using (var connection = _sqliteService.CreateConnection())
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
                        cmd.Parameters.AddWithValue("@annee", annee);

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
                using (var connection = _sqliteService.CreateConnection())
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
                            cmd.Parameters.AddWithValue("@id", regul.Id);
                        }

                        cmd.Parameters.AddWithValue("@emp", regul.EmployeeId);
                        cmd.Parameters.AddWithValue("@annee", regul.Annee);
                        cmd.Parameters.AddWithValue("@nmois", regul.NombreMoisTravailles);
                        cmd.Parameters.AddWithValue("@brut", DbValue.MoneyToDb(regul.CumulBrutImposable));
                        cmd.Parameters.AddWithValue("@theorique", DbValue.MoneyToDb(regul.IutsTheoriqueAnnuel));
                        cmd.Parameters.AddWithValue("@preleve", DbValue.MoneyToDb(regul.CumulIutsPreleve));
                        cmd.Parameters.AddWithValue("@diff", DbValue.MoneyToDb(regul.DifferenceRegularisation));
                        cmd.Parameters.AddWithValue("@mi", regul.MoisImputation);
                        cmd.Parameters.AddWithValue("@statut", (int)regul.Statut);
                        cmd.Parameters.AddWithValue("@rpid", regul.RappelPaieId.HasValue ? (object)regul.RappelPaieId.Value : DBNull.Value);
                        cmd.Parameters.AddWithValue("@obs", regul.Observations ?? (object)DBNull.Value);
                        cmd.Parameters.AddWithValue("@dc", DbValue.DateToDb(regul.DateCreation));
                        cmd.Parameters.AddWithValue("@dm", DbValue.DateToDb(DateTime.Now));

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
                using (var connection = _sqliteService.CreateConnection())
                {
                    using (var cmd = connection.CreateCommand())
                    {
                        cmd.CommandText = @"
                            SELECT EmployeeId, BaseIUTSHorsDeduction, IUTSNet, CalculationDate
                            FROM TaxCalculations
                            WHERE CalculationDate LIKE @anneePrefix
                              AND IsAnnual = 0 AND IsRegularization = 0
                            ORDER BY CalculationDate ASC;";
                        cmd.Parameters.AddWithValue("@anneePrefix", $"{annee:0000}-%");

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

        public async Task<ApportRappels> ApportAsync(int annee, int mois, string employeeId)
        {
            var rappels = await ObtenirRappelsAsync(annee, mois, employeeId, StatutRappelPaie.EnAttente);
            return ApportDe(rappels, employeeId);
        }

        public static ApportRappels ApportDe(IEnumerable<RappelPaie> tousLesRappels, string employeeId)
        {
            var apport = new ApportRappels();
            if (tousLesRappels == null) return apport;

            var pourEmploye = tousLesRappels.Where(r =>
                string.Equals(r.EmployeeId, employeeId, StringComparison.OrdinalIgnoreCase) &&
                r.Statut == StatutRappelPaie.EnAttente);

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
