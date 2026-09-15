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
    /// Service gérant les avantages en nature (logement, véhicule, énergie, domesticité, nourriture)
    /// selon les règles fiscales et sociales burkinabè (CGI Art. 106 &amp; CNSS) — module PAI-08.
    /// </summary>
    public class AvantageNatureService
    {
        private readonly SQLiteService _sqliteService;
        private readonly AuditService _auditService;

        /// <summary>Plafond fiscal mensuel légal du logement de fonction au Burkina Faso (Art. 106 CGI : 75 000 FCFA).</summary>
        public const decimal PlafondLogementMensuelDefaut = 75_000m;

        /// <summary>Taux forfaitaire légal du logement : 20% du salaire de base indiciaire/conventionnel brut.</summary>
        public const decimal TauxForfaitLogementDefaut = 0.20m;

        public AvantageNatureService(
            SQLiteService sqliteService,
            AuditService auditService = null)
        {
            _sqliteService = sqliteService ?? throw new ArgumentNullException(nameof(sqliteService));
            _auditService = auditService;
        }

        #region CRUD Avantages

        public async Task<List<AvantageNature>> ObtenirAvantagesAsync(
            string employeeId = null,
            bool? seulementActifs = null)
        {
            var liste = new List<AvantageNature>();

            await Task.Run(() =>
            {
                using (var connection = _sqliteService.CreateConnection())
                {
                    var sql = @"
                        SELECT a.Id, a.EmployeeId, a.TypeAvantage, a.Libelle, a.ModeEvaluation,
                               a.MontantAvantage, a.PartSalariale, a.ValeurNetteImposable,
                               a.DateDebut, a.DateFin, a.Actif,
                               a.SoumisIUTS, a.SoumisCNSS, a.SoumisTPA,
                               a.CodeRubriqueGain, a.CodeRubriqueRetenue, a.Commentaire,
                               a.DateCreation, a.DateModification,
                               e.FirstName, e.LastName
                        FROM AvantagesEnNature a
                        LEFT JOIN Employees e ON e.EmployeeId = a.EmployeeId
                        WHERE 1 = 1";

                    if (!string.IsNullOrWhiteSpace(employeeId))
                        sql += " AND a.EmployeeId = @emp";
                    if (seulementActifs.HasValue && seulementActifs.Value)
                        sql += " AND a.Actif = 1";

                    sql += " ORDER BY a.Actif DESC, a.DateDebut DESC, a.Id DESC";

                    using (var cmd = new SQLiteCommand(sql, connection))
                    {
                        if (!string.IsNullOrWhiteSpace(employeeId))
                            cmd.Parameters.AddWithValue("@emp", employeeId);

                        using (var reader = cmd.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                liste.Add(MapperAvantage(reader));
                            }
                        }
                    }
                }
            }).ConfigureAwait(false);

            return liste;
        }

        public async Task<AvantageNature> ObtenirParIdAsync(int id)
        {
            return await Task.Run(() =>
            {
                using (var connection = _sqliteService.CreateConnection())
                {
                    var sql = @"
                        SELECT a.Id, a.EmployeeId, a.TypeAvantage, a.Libelle, a.ModeEvaluation,
                               a.MontantAvantage, a.PartSalariale, a.ValeurNetteImposable,
                               a.DateDebut, a.DateFin, a.Actif,
                               a.SoumisIUTS, a.SoumisCNSS, a.SoumisTPA,
                               a.CodeRubriqueGain, a.CodeRubriqueRetenue, a.Commentaire,
                               a.DateCreation, a.DateModification,
                               e.FirstName, e.LastName
                        FROM AvantagesEnNature a
                        LEFT JOIN Employees e ON e.EmployeeId = a.EmployeeId
                        WHERE a.Id = @id";

                    using (var cmd = new SQLiteCommand(sql, connection))
                    {
                        cmd.Parameters.AddWithValue("@id", id);
                        using (var reader = cmd.ExecuteReader())
                        {
                            if (reader.Read())
                                return MapperAvantage(reader);
                        }
                    }
                }
                return null;
            }).ConfigureAwait(false);
        }

        public async Task<int> EnregistrerAsync(AvantageNature avantage, string utilisateur = "Utilisateur")
        {
            if (avantage == null) throw new ArgumentNullException(nameof(avantage));
            if (string.IsNullOrWhiteSpace(avantage.EmployeeId))
                throw new InvalidOperationException("L'identifiant du salarié est obligatoire.");
            if (string.IsNullOrWhiteSpace(avantage.Libelle))
                throw new InvalidOperationException("Le libellé de l'avantage en nature est obligatoire.");
            if (avantage.MontantAvantage <= 0m)
                throw new InvalidOperationException("Le montant évalué de l'avantage doit être strictement supérieur à zéro.");

            int idGenere = avantage.Id;

            await Task.Run(async () =>
            {
                using (var connection = _sqliteService.CreateConnection())
                {
                    var maintenant = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);

                    if (avantage.Id == 0)
                    {
                        var sql = @"
                            INSERT INTO AvantagesEnNature (
                                EmployeeId, TypeAvantage, Libelle, ModeEvaluation,
                                MontantAvantage, PartSalariale, ValeurNetteImposable,
                                DateDebut, DateFin, Actif,
                                SoumisIUTS, SoumisCNSS, SoumisTPA,
                                CodeRubriqueGain, CodeRubriqueRetenue, Commentaire,
                                DateCreation, DateModification
                            ) VALUES (
                                @emp, @type, @lib, @mode,
                                @mont, @part, @net,
                                @debut, @fin, @actif,
                                @iuts, @cnss, @tpa,
                                @codeGain, @codeRet, @com,
                                @dateCre, @dateMod
                            );
                            SELECT last_insert_rowid();";

                        using (var cmd = new SQLiteCommand(sql, connection))
                        {
                            cmd.Parameters.AddWithValue("@emp", avantage.EmployeeId);
                            cmd.Parameters.AddWithValue("@type", (int)avantage.TypeAvantage);
                            cmd.Parameters.AddWithValue("@lib", avantage.Libelle);
                            cmd.Parameters.AddWithValue("@mode", (int)avantage.ModeEvaluation);
                            cmd.Parameters.AddWithValue("@mont", DbValue.MoneyToDb(avantage.MontantAvantage));
                            cmd.Parameters.AddWithValue("@part", DbValue.MoneyToDb(avantage.PartSalariale));
                            cmd.Parameters.AddWithValue("@net", DbValue.MoneyToDb(avantage.ValeurNetteImposable));
                            cmd.Parameters.AddWithValue("@debut", avantage.DateDebut.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
                            cmd.Parameters.AddWithValue("@fin", avantage.DateFin.HasValue ? (object)avantage.DateFin.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : DBNull.Value);
                            cmd.Parameters.AddWithValue("@actif", avantage.Actif ? 1 : 0);
                            cmd.Parameters.AddWithValue("@iuts", avantage.SoumisIUTS ? 1 : 0);
                            cmd.Parameters.AddWithValue("@cnss", avantage.SoumisCNSS ? 1 : 0);
                            cmd.Parameters.AddWithValue("@tpa", avantage.SoumisTPA ? 1 : 0);
                            cmd.Parameters.AddWithValue("@codeGain", avantage.CodeRubriqueGain ?? "AVNAT_LOG");
                            cmd.Parameters.AddWithValue("@codeRet", avantage.CodeRubriqueRetenue ?? "RET_AVNAT");
                            cmd.Parameters.AddWithValue("@com", (object)avantage.Commentaire ?? DBNull.Value);
                            cmd.Parameters.AddWithValue("@dateCre", maintenant);
                            cmd.Parameters.AddWithValue("@dateMod", maintenant);

                            idGenere = Convert.ToInt32(cmd.ExecuteScalar());
                            avantage.Id = idGenere;
                        }

                        if (_auditService != null)
                        {
                            try
                            {
                                await _auditService.EnregistrerAsync(
                                    "CREATION",
                                    "AvantagesEnNature",
                                    idGenere.ToString(CultureInfo.InvariantCulture),
                                    details: string.Format(CultureInfo.InvariantCulture,
                                        "Attribution avantage {0} ({1}) au salarié {2} : {3:N0} FCFA",
                                        avantage.TypeLibelle, avantage.Libelle, avantage.EmployeeId, avantage.ValeurNetteImposable),
                                    utilisateur: utilisateur).ConfigureAwait(false);
                            }
                            catch { }
                        }
                    }
                    else
                    {
                        var sql = @"
                            UPDATE AvantagesEnNature SET
                                EmployeeId = @emp,
                                TypeAvantage = @type,
                                Libelle = @lib,
                                ModeEvaluation = @mode,
                                MontantAvantage = @mont,
                                PartSalariale = @part,
                                ValeurNetteImposable = @net,
                                DateDebut = @debut,
                                DateFin = @fin,
                                Actif = @actif,
                                SoumisIUTS = @iuts,
                                SoumisCNSS = @cnss,
                                SoumisTPA = @tpa,
                                CodeRubriqueGain = @codeGain,
                                CodeRubriqueRetenue = @codeRet,
                                Commentaire = @com,
                                DateModification = @dateMod
                            WHERE Id = @id";

                        using (var cmd = new SQLiteCommand(sql, connection))
                        {
                            cmd.Parameters.AddWithValue("@id", avantage.Id);
                            cmd.Parameters.AddWithValue("@emp", avantage.EmployeeId);
                            cmd.Parameters.AddWithValue("@type", (int)avantage.TypeAvantage);
                            cmd.Parameters.AddWithValue("@lib", avantage.Libelle);
                            cmd.Parameters.AddWithValue("@mode", (int)avantage.ModeEvaluation);
                            cmd.Parameters.AddWithValue("@mont", DbValue.MoneyToDb(avantage.MontantAvantage));
                            cmd.Parameters.AddWithValue("@part", DbValue.MoneyToDb(avantage.PartSalariale));
                            cmd.Parameters.AddWithValue("@net", DbValue.MoneyToDb(avantage.ValeurNetteImposable));
                            cmd.Parameters.AddWithValue("@debut", avantage.DateDebut.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
                            cmd.Parameters.AddWithValue("@fin", avantage.DateFin.HasValue ? (object)avantage.DateFin.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : DBNull.Value);
                            cmd.Parameters.AddWithValue("@actif", avantage.Actif ? 1 : 0);
                            cmd.Parameters.AddWithValue("@iuts", avantage.SoumisIUTS ? 1 : 0);
                            cmd.Parameters.AddWithValue("@cnss", avantage.SoumisCNSS ? 1 : 0);
                            cmd.Parameters.AddWithValue("@tpa", avantage.SoumisTPA ? 1 : 0);
                            cmd.Parameters.AddWithValue("@codeGain", avantage.CodeRubriqueGain ?? "AVNAT_LOG");
                            cmd.Parameters.AddWithValue("@codeRet", avantage.CodeRubriqueRetenue ?? "RET_AVNAT");
                            cmd.Parameters.AddWithValue("@com", (object)avantage.Commentaire ?? DBNull.Value);
                            cmd.Parameters.AddWithValue("@dateMod", maintenant);

                            cmd.ExecuteNonQuery();
                        }

                        if (_auditService != null)
                        {
                            try
                            {
                                await _auditService.EnregistrerAsync(
                                    "MODIFICATION",
                                    "AvantagesEnNature",
                                    avantage.Id.ToString(CultureInfo.InvariantCulture),
                                    details: string.Format(CultureInfo.InvariantCulture,
                                        "Mise à jour avantage #{0} ({1}) : {2:N0} FCFA",
                                        avantage.Id, avantage.Libelle, avantage.ValeurNetteImposable),
                                    utilisateur: utilisateur).ConfigureAwait(false);
                            }
                            catch { }
                        }
                    }
                }
            }).ConfigureAwait(false);

            return idGenere;
        }

        public async Task SupprimerAsync(int id, string utilisateur = "Utilisateur")
        {
            await Task.Run(async () =>
            {
                using (var connection = _sqliteService.CreateConnection())
                {
                    var sql = "DELETE FROM AvantagesEnNature WHERE Id = @id";
                    using (var cmd = new SQLiteCommand(sql, connection))
                    {
                        cmd.Parameters.AddWithValue("@id", id);
                        cmd.ExecuteNonQuery();
                    }
                }

                if (_auditService != null)
                {
                    try
                    {
                        await _auditService.EnregistrerAsync(
                            "SUPPRESSION",
                            "AvantagesEnNature",
                            id.ToString(CultureInfo.InvariantCulture),
                            details: "Suppression définitive de l'avantage en nature #" + id,
                            utilisateur: utilisateur).ConfigureAwait(false);
                    }
                    catch { }
                }
            }).ConfigureAwait(false);
        }

        public async Task BasculerStatutActifAsync(int id, bool actif, string utilisateur = "Utilisateur")
        {
            await Task.Run(async () =>
            {
                using (var connection = _sqliteService.CreateConnection())
                {
                    var maintenant = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
                    var sql = @"
                        UPDATE AvantagesEnNature SET
                            Actif = @actif,
                            DateModification = @dateMod
                        WHERE Id = @id";

                    using (var cmd = new SQLiteCommand(sql, connection))
                    {
                        cmd.Parameters.AddWithValue("@id", id);
                        cmd.Parameters.AddWithValue("@actif", actif ? 1 : 0);
                        cmd.Parameters.AddWithValue("@dateMod", maintenant);
                        cmd.ExecuteNonQuery();
                    }
                }

                if (_auditService != null)
                {
                    try
                    {
                        await _auditService.EnregistrerAsync(
                            actif ? "ACTIVATION" : "DESACTIVATION",
                            "AvantagesEnNature",
                            id.ToString(CultureInfo.InvariantCulture),
                            champ: "Actif",
                            nouvelleValeur: actif.ToString(),
                            details: string.Format(CultureInfo.InvariantCulture, "Avantage #{0} {1}", id, actif ? "activé" : "désactivé"),
                            utilisateur: utilisateur).ConfigureAwait(false);
                    }
                    catch { }
                }
            }).ConfigureAwait(false);
        }

        #endregion

        #region Moteur de calcul réglementaire CGI

        /// <summary>
        /// Calcule la valeur forfaitaire mensuelle du logement de fonction selon l'Article 106 du CGI burkinabè :
        /// 20% du salaire de base indiciaire/conventionnel brut, avec écrêtement au plafond fiscal de 75 000 FCFA/mois.
        /// </summary>
        public static decimal CalculerForfaitLogementLegal(decimal salaireDeBase)
        {
            if (salaireDeBase <= 0m) return 0m;

            decimal forfaitTheorique = Math.Round(salaireDeBase * TauxForfaitLogementDefaut, 0, MidpointRounding.AwayFromZero);
            return Math.Min(forfaitTheorique, PlafondLogementMensuelDefaut);
        }

        #endregion

        #region Apport pour le moteur de paie

        /// <summary>
        /// Calcule l'apport des avantages en nature d'un salarié pour un mois de paie ouvert.
        /// </summary>
        public async Task<ApportAvantagesNature> ObtenirApportMoisAsync(string employeeId, int annee, int mois)
        {
            if (string.IsNullOrWhiteSpace(employeeId)) return ApportAvantagesNature.Aucun();

            var tousLesAvantages = await ObtenirAvantagesAsync(employeeId, seulementActifs: true).ConfigureAwait(false);
            return ApportDe(tousLesAvantages, employeeId, annee, mois);
        }

        /// <summary>
        /// Calcule en masse l'apport des avantages en nature pour l'ensemble des salariés pour un mois donné.
        /// </summary>
        public async Task<Dictionary<string, ApportAvantagesNature>> ObtenirApportsMoisAsync(int annee, int mois)
        {
            var resultat = new Dictionary<string, ApportAvantagesNature>(StringComparer.OrdinalIgnoreCase);
            var tousLesAvantages = await ObtenirAvantagesAsync(seulementActifs: true).ConfigureAwait(false);

            var groupes = tousLesAvantages
                .Where(a => a.EstEnCours(annee, mois))
                .GroupBy(a => a.EmployeeId, StringComparer.OrdinalIgnoreCase);

            foreach (var groupe in groupes)
            {
                resultat[groupe.Key] = ApportDe(groupe, groupe.Key, annee, mois);
            }

            return resultat;
        }

        /// <summary>
        /// Évaluation en mémoire pure de l'apport pour un salarié sur une période donnée.
        /// Utilisé par les tests unitaires et la génération en masse.
        /// </summary>
        public static ApportAvantagesNature ApportDe(
            IEnumerable<AvantageNature> avantages,
            string employeeId,
            int annee,
            int mois)
        {
            if (avantages == null || string.IsNullOrWhiteSpace(employeeId))
                return ApportAvantagesNature.Aucun();

            var apport = new ApportAvantagesNature();

            foreach (var av in avantages)
            {
                if (!string.Equals(av.EmployeeId, employeeId, StringComparison.OrdinalIgnoreCase))
                    continue;

                if (!av.EstEnCours(annee, mois))
                    continue;

                decimal netImposable = av.ValeurNetteImposable;
                if (netImposable <= 0m)
                    continue;

                apport.AjouterAvantage(
                    av.CodeRubriqueGain,
                    av.Libelle,
                    av.CodeRubriqueRetenue,
                    netImposable,
                    av.SoumisIUTS,
                    av.SoumisCNSS,
                    av.SoumisTPA,
                    av.Id);
            }

            return apport;
        }

        #endregion

        #region Mappeur

        private static AvantageNature MapperAvantage(SQLiteDataReader reader)
        {
            string prenom = reader.IsDBNull(reader.GetOrdinal("FirstName")) ? "" : reader.GetString(reader.GetOrdinal("FirstName"));
            string nom = reader.IsDBNull(reader.GetOrdinal("LastName")) ? "" : reader.GetString(reader.GetOrdinal("LastName"));
            string empId = reader.GetString(reader.GetOrdinal("EmployeeId"));
            string nomComplet = string.Format("{0} {1} ({2})", nom, prenom, empId).Trim();

            DateTime dateDebut;
            if (!DateTime.TryParse(reader.GetString(reader.GetOrdinal("DateDebut")), out dateDebut))
                dateDebut = DateTime.Today;

            DateTime? dateFin = null;
            if (!reader.IsDBNull(reader.GetOrdinal("DateFin")))
            {
                DateTime dt;
                if (DateTime.TryParse(reader.GetString(reader.GetOrdinal("DateFin")), out dt))
                    dateFin = dt;
            }

            DateTime dateCre;
            if (!DateTime.TryParse(reader.GetString(reader.GetOrdinal("DateCreation")), out dateCre))
                dateCre = DateTime.Now;

            DateTime dateMod;
            if (!DateTime.TryParse(reader.GetString(reader.GetOrdinal("DateModification")), out dateMod))
                dateMod = DateTime.Now;

            return new AvantageNature
            {
                Id = reader.GetInt32(reader.GetOrdinal("Id")),
                EmployeeId = empId,
                NomSalarie = nomComplet,
                TypeAvantage = (TypeAvantageNature)reader.GetInt32(reader.GetOrdinal("TypeAvantage")),
                Libelle = reader.GetString(reader.GetOrdinal("Libelle")),
                ModeEvaluation = (ModeEvaluationAvantage)reader.GetInt32(reader.GetOrdinal("ModeEvaluation")),
                MontantAvantage = DbValue.MoneyFromDb(reader.GetInt64(reader.GetOrdinal("MontantAvantage"))),
                PartSalariale = DbValue.MoneyFromDb(reader.GetInt64(reader.GetOrdinal("PartSalariale"))),
                DateDebut = dateDebut,
                DateFin = dateFin,
                Actif = reader.GetInt32(reader.GetOrdinal("Actif")) == 1,
                SoumisIUTS = reader.GetInt32(reader.GetOrdinal("SoumisIUTS")) == 1,
                SoumisCNSS = reader.GetInt32(reader.GetOrdinal("SoumisCNSS")) == 1,
                SoumisTPA = reader.GetInt32(reader.GetOrdinal("SoumisTPA")) == 1,
                CodeRubriqueGain = reader.GetString(reader.GetOrdinal("CodeRubriqueGain")),
                CodeRubriqueRetenue = reader.GetString(reader.GetOrdinal("CodeRubriqueRetenue")),
                Commentaire = reader.IsDBNull(reader.GetOrdinal("Commentaire")) ? null : reader.GetString(reader.GetOrdinal("Commentaire")),
                DateCreation = dateCre,
                DateModification = dateMod
            };
        }

        #endregion
    }
}
