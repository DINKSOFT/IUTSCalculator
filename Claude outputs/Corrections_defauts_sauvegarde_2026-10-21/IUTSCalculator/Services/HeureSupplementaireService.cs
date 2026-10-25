using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Linq;
using System.Threading.Tasks;
using IUTSCalculator.Models;

namespace IUTSCalculator.Services
{
    /// <summary>
    /// Service de gestion et de valorisation des heures supplémentaires (module PAI-03).
    /// </summary>
    public class HeureSupplementaireService
    {
        private readonly SQLiteService _sqliteService;
        private readonly AuditService _auditService;
        private readonly PeriodePaieService _periodePaieService;
        private readonly BaremeHeuresSupService _baremeService;

        private const string Colonnes =
            "h.Id, h.EmployeeId, h.Annee, h.Mois, h.Heures15, h.Heures30, h.Heures50, h.Heures60, h.Heures100, " +
            "h.TauxHoraireBase, h.Montant15, h.Montant30, h.Montant50, h.Montant60, h.Montant100, h.MontantTotal, " +
            "h.Commentaire, h.DateCreation, h.DateModification";

        public HeureSupplementaireService(
            SQLiteService sqliteService,
            AuditService auditService = null,
            PeriodePaieService periodePaieService = null)
        {
            _sqliteService = sqliteService ?? throw new ArgumentNullException(nameof(sqliteService));
            _auditService = auditService;
            _periodePaieService = periodePaieService;
            _baremeService = new BaremeHeuresSupService(sqliteService);
        }

        /// <summary>Barème de majorations applicable au mois (RH-08 : taux datés).</summary>
        public Task<BaremeHeuresSup> BaremeApplicableAsync(int annee, int mois)
        {
            return _baremeService.ApplicableAsync(annee, mois);
        }

        /// <summary>
        /// Charge la saisie des heures supplémentaires pour tous les employés actifs sur une période donnée.
        /// Si un employé n'a pas encore de saisie enregistrée, une ligne avec des heures à zéro est initialisée.
        /// </summary>
        public async Task<List<HeureSupplementaire>> ChargerSaisieMoisAsync(int annee, int mois)
        {
            var resultat = new List<HeureSupplementaire>();
            var bareme = await _baremeService.ApplicableAsync(annee, mois);

            using (var conn = _sqliteService.CreateConnection())
            {
                // 1. Charger tous les employés actifs.
                //
                // La requête ne filtrait pas sur StatutSalarie/DateSortie malgré ce que dit
                // le commentaire ci-dessus : un employé sorti restait proposé dans la grille
                // de saisie et pouvait s'y voir attribuer des heures supplémentaires — signalé
                // le 16 septembre 2026 (voir ETAT-DEVELOPPEMENT.md item 22). Le filtre
                // reproduit Employee.EstActif (StatutSalarie = 0 et pas de DateSortie passée).
                var employes = new List<(string Id, string Matricule, string Nom, decimal SalaireBase)>();
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText =
                        "SELECT EmployeeId, LastName, FirstName, BaseSalary FROM Employees " +
                        "WHERE StatutSalarie = 0 AND (DateSortie IS NULL OR DateSortie = '' OR DateSortie >= @today) " +
                        "ORDER BY LastName, FirstName;";
                    // DateSortie est stockée au format "yyyy-MM-dd HH:mm:ss" (voir DbValue.DateToDb) et une
                    // date absente y est écrite comme chaîne vide, jamais comme NULL SQL (voir
                    // DbValue.DateNullableToDb) : les deux cas sont donc couverts ci-dessus, et la
                    // comparaison de chaînes ne reste correcte que parce que @today suit le même format.
                    cmd.Parameters.AddWithValue("@today", DateTime.Today.ToString("yyyy-MM-dd HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture));
                    using (var reader = (SQLiteDataReader)await cmd.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            string id = DbValue.TextFromDb(reader["EmployeeId"]);
                            string nom = $"{DbValue.TextFromDb(reader["LastName"])} {DbValue.TextFromDb(reader["FirstName"])}".Trim();
                            decimal salaire = DbValue.MoneyFromDb(reader["BaseSalary"]);
                            employes.Add((id, id, nom, salaire));
                        }
                    }
                }

                // 2. Charger les heures sup existantes pour la période
                var mapHeures = new Dictionary<string, HeureSupplementaire>();
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText =
                        "SELECT " + Colonnes + " FROM HeuresSupplementaires h WHERE h.Annee = @a AND h.Mois = @m;";
                    cmd.Parameters.AddWithValue("@a", annee);
                    cmd.Parameters.AddWithValue("@m", mois);

                    using (var reader = (SQLiteDataReader)await cmd.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            var hs = Lire(reader);
                            mapHeures[hs.EmployeeId] = hs;
                        }
                    }
                }

                // 3. Fusionner pour produire la grille complète
                foreach (var emp in employes)
                {
                    if (mapHeures.TryGetValue(emp.Id, out var existant))
                    {
                        existant.Matricule = emp.Matricule;
                        existant.NomComplet = emp.Nom;
                        existant.SalaireDeBase = emp.SalaireBase;
                        existant.AppliquerBareme(bareme);
                        existant.EstModifie = false;
                        resultat.Add(existant);
                    }
                    else
                    {
                        var nouveau = new HeureSupplementaire
                        {
                            Id = 0,
                            EmployeeId = emp.Id,
                            Annee = annee,
                            Mois = mois,
                            Matricule = emp.Matricule,
                            NomComplet = emp.Nom,
                            SalaireDeBase = emp.SalaireBase,
                            Heures15 = 0m,
                            Heures30 = 0m,
                            Heures50 = 0m,
                            Heures60 = 0m,
                            Heures100 = 0m,
                            DateCreation = DateTime.Now,
                            DateModification = DateTime.Now,
                            EstModifie = false
                        };
                        nouveau.AppliquerBareme(bareme);
                        nouveau.EstModifie = false;
                        resultat.Add(nouveau);
                    }
                }
            }

            return resultat;
        }

        /// <summary>
        /// Obtient les heures supplémentaires d'un employé pour une période donnée.
        /// </summary>
        public async Task<HeureSupplementaire> ObtenirPourEmployeAsync(string employeeId, int annee, int mois)
        {
            if (string.IsNullOrWhiteSpace(employeeId)) return null;

            using (var conn = _sqliteService.CreateConnection())
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText =
                    "SELECT " + Colonnes + " FROM HeuresSupplementaires h WHERE h.EmployeeId = @emp AND h.Annee = @a AND h.Mois = @m;";
                cmd.Parameters.AddWithValue("@emp", employeeId);
                cmd.Parameters.AddWithValue("@a", annee);
                cmd.Parameters.AddWithValue("@m", mois);

                using (var reader = (SQLiteDataReader)await cmd.ExecuteReaderAsync())
                {
                    if (await reader.ReadAsync())
                    {
                        var hs = Lire(reader);
                        // Taux du mois, montants tels qu'enregistrés (ApportAsync revalorise ensuite).
                        hs.AppliquerBareme(BaremeHeuresSupService.Applicable(conn, new DateTime(annee, mois, 1)), revaloriser: false);
                        hs.EstModifie = false;
                        return hs;
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// Enregistre l'ensemble de la grille des heures supplémentaires du mois dans une transaction atomique.
        /// </summary>
        public async Task<int> EnregistrerGrilleAsync(
            int annee, int mois, IEnumerable<HeureSupplementaire> lignes, string utilisateur)
        {
            if (lignes == null) return 0;

            if (_periodePaieService != null && await _periodePaieService.EstClotureeAsync(new DateTime(annee, mois, 1)))
            {
                throw new InvalidOperationException(
                    $"La période de paie {mois:D2}/{annee} est clôturée. Impossible de modifier les heures supplémentaires.");
            }

            int enregistres = 0;
            decimal montantTotalSaisi = 0m;
            decimal totalHeuresSaisies = 0m;
            DateTime now = DateTime.Now;
            var bareme = await _baremeService.ApplicableAsync(annee, mois);

            using (var conn = _sqliteService.CreateConnection())
            using (var tx = conn.BeginTransaction())
            {
                foreach (var hs in lignes)
                {
                    hs.AppliquerBareme(bareme);

                    // Si toutes les heures sont nulles et qu'il n'existe pas en base, on ne crée pas de ligne vide inutile
                    if (hs.Id == 0 && hs.TotalHeures <= 0m && string.IsNullOrWhiteSpace(hs.Commentaire))
                        continue;

                    using (var cmd = conn.CreateCommand())
                    {
                        cmd.Transaction = tx;
                        if (hs.Id == 0)
                        {
                            cmd.CommandText = @"
                                INSERT INTO HeuresSupplementaires (
                                    EmployeeId, Annee, Mois,
                                    Heures15, Heures30, Heures50, Heures60, Heures100,
                                    TauxHoraireBase, Montant15, Montant30, Montant50, Montant60, Montant100, MontantTotal,
                                    Commentaire, DateCreation, DateModification
                                ) VALUES (
                                    @emp, @a, @m,
                                    @h15, @h30, @h50, @h60, @h100,
                                    @th, @m15, @m30, @m50, @m60, @m100, @mtot,
                                    @comm, @dc, @dm
                                );";
                            cmd.Parameters.AddWithValue("@emp", hs.EmployeeId);
                            cmd.Parameters.AddWithValue("@a", annee);
                            cmd.Parameters.AddWithValue("@m", mois);
                            cmd.Parameters.AddWithValue("@h15", DbValue.DecimalToDb(hs.Heures15));
                            cmd.Parameters.AddWithValue("@h30", DbValue.DecimalToDb(hs.Heures30));
                            cmd.Parameters.AddWithValue("@h50", DbValue.DecimalToDb(hs.Heures50));
                            cmd.Parameters.AddWithValue("@h60", DbValue.DecimalToDb(hs.Heures60));
                            cmd.Parameters.AddWithValue("@h100", DbValue.DecimalToDb(hs.Heures100));
                            cmd.Parameters.AddWithValue("@th", DbValue.MoneyToDb(hs.TauxHoraireBase));
                            cmd.Parameters.AddWithValue("@m15", DbValue.MoneyToDb(hs.Montant15));
                            cmd.Parameters.AddWithValue("@m30", DbValue.MoneyToDb(hs.Montant30));
                            cmd.Parameters.AddWithValue("@m50", DbValue.MoneyToDb(hs.Montant50));
                            cmd.Parameters.AddWithValue("@m60", DbValue.MoneyToDb(hs.Montant60));
                            cmd.Parameters.AddWithValue("@m100", DbValue.MoneyToDb(hs.Montant100));
                            cmd.Parameters.AddWithValue("@mtot", DbValue.MoneyToDb(hs.MontantTotal));
                            cmd.Parameters.AddWithValue("@comm", hs.Commentaire ?? string.Empty);
                            cmd.Parameters.AddWithValue("@dc", DbValue.DateToDb(now));
                            cmd.Parameters.AddWithValue("@dm", DbValue.DateToDb(now));

                            await cmd.ExecuteNonQueryAsync();
                            hs.Id = conn.LastInsertRowId;
                        }
                        else
                        {
                            cmd.CommandText = @"
                                UPDATE HeuresSupplementaires SET
                                    Heures15 = @h15, Heures30 = @h30, Heures50 = @h50, Heures60 = @h60, Heures100 = @h100,
                                    TauxHoraireBase = @th, Montant15 = @m15, Montant30 = @m30, Montant50 = @m50, Montant60 = @m60, Montant100 = @m100, MontantTotal = @mtot,
                                    Commentaire = @comm, DateModification = @dm
                                WHERE Id = @id;";
                            cmd.Parameters.AddWithValue("@id", hs.Id);
                            cmd.Parameters.AddWithValue("@h15", DbValue.DecimalToDb(hs.Heures15));
                            cmd.Parameters.AddWithValue("@h30", DbValue.DecimalToDb(hs.Heures30));
                            cmd.Parameters.AddWithValue("@h50", DbValue.DecimalToDb(hs.Heures50));
                            cmd.Parameters.AddWithValue("@h60", DbValue.DecimalToDb(hs.Heures60));
                            cmd.Parameters.AddWithValue("@h100", DbValue.DecimalToDb(hs.Heures100));
                            cmd.Parameters.AddWithValue("@th", DbValue.MoneyToDb(hs.TauxHoraireBase));
                            cmd.Parameters.AddWithValue("@m15", DbValue.MoneyToDb(hs.Montant15));
                            cmd.Parameters.AddWithValue("@m30", DbValue.MoneyToDb(hs.Montant30));
                            cmd.Parameters.AddWithValue("@m50", DbValue.MoneyToDb(hs.Montant50));
                            cmd.Parameters.AddWithValue("@m60", DbValue.MoneyToDb(hs.Montant60));
                            cmd.Parameters.AddWithValue("@m100", DbValue.MoneyToDb(hs.Montant100));
                            cmd.Parameters.AddWithValue("@mtot", DbValue.MoneyToDb(hs.MontantTotal));
                            cmd.Parameters.AddWithValue("@comm", hs.Commentaire ?? string.Empty);
                            cmd.Parameters.AddWithValue("@dm", DbValue.DateToDb(now));

                            await cmd.ExecuteNonQueryAsync();
                        }

                        hs.EstModifie = false;
                        enregistres++;
                        montantTotalSaisi += hs.MontantTotal;
                        totalHeuresSaisies += hs.TotalHeures;
                    }
                }

                tx.Commit();
            }

            // Traçabilité d'audit
            if (_auditService != null && enregistres > 0)
            {
                try
                {
                    await _auditService.EnregistrerAsync(
                        action: "Saisie heures supplémentaires",
                        entite: "HeuresSup",
                        entiteId: $"{annee}-{mois:D2}",
                        champ: "MontantTotal",
                        ancienneValeur: string.Empty,
                        nouvelleValeur: $"{montantTotalSaisi:N0} FCFA",
                        details: $"Saisie d'heures supplémentaires pour {enregistres} salarié(s) ({totalHeuresSaisies:N1} h au total).");
                }
                catch
                {
                    // L'audit ne doit jamais bloquer la transaction métier principale
                }
            }

            return enregistres;
        }

        /// <summary>
        /// Construit l'apport prêt pour le moteur de paie pour un salarié et un mois donnés.
        /// </summary>
        public async Task<ApportHeuresSupplementaires> ApportAsync(Employee employe, int annee, int mois)
        {
            if (employe == null || string.IsNullOrWhiteSpace(employe.EmployeeId))
                return ApportHeuresSupplementaires.Vide;

            var hs = await ObtenirPourEmployeAsync(employe.EmployeeId, annee, mois);
            if (hs == null || hs.TotalHeures <= 0m)
                return ApportHeuresSupplementaires.Vide;

            // Recalculer sur la base du salaire de base actuel du salarié, aux taux du
            // barème applicable au mois (ObtenirPourEmployeAsync l'a déjà appliqué).
            hs.SalaireDeBase = employe.BaseSalary;
            hs.RecalculerMontants();

            if (hs.MontantTotal <= 0m)
                return ApportHeuresSupplementaires.Vide;

            return new ApportHeuresSupplementaires(LignesBulletin(hs), hs.MontantTotal, hs.TotalHeures);
        }

        /// <summary>
        /// Lignes détaillées du bulletin, une par tranche servie. Le code et le libellé
        /// portent le taux réellement appliqué (HS35 « Heures supplémentaires 135% ») :
        /// avec le barème historique, on retrouve exactement HS15/HS30/HS50/HS100.
        /// </summary>
        public static List<LigneApport> LignesBulletin(HeureSupplementaire hs)
        {
            var lignes = new List<LigneApport>();
            if (hs == null) return lignes;

            Ajouter(lignes, hs.Heures15, hs.Montant15, hs.TauxTranche1, null);
            Ajouter(lignes, hs.Heures30, hs.Montant30, hs.TauxTranche2, null);
            Ajouter(lignes, hs.Heures50, hs.Montant50, hs.TauxNuit, null);
            Ajouter(lignes, hs.Heures60, hs.Montant60, hs.TauxFerieJour, "dimanche/férié");
            Ajouter(lignes, hs.Heures100, hs.Montant100, hs.TauxFerieNuit, null);

            return lignes;
        }

        private static void Ajouter(List<LigneApport> lignes, decimal heures, decimal montant, decimal taux, string precision)
        {
            if (heures <= 0m || montant <= 0m) return;

            string code = "HS" + Math.Round(taux * 100m, 0).ToString(System.Globalization.CultureInfo.InvariantCulture);
            string libelle = string.IsNullOrEmpty(precision)
                ? $"Heures supplémentaires {BaremeHeuresSup.TauxBulletin(taux)} ({heures:0.##} h)"
                : $"Heures supplémentaires {BaremeHeuresSup.TauxBulletin(taux)} {precision} ({heures:0.##} h)";

            lignes.Add(new LigneApport(
                code: code,
                libelle: libelle,
                sens: SensRubrique.Gain,
                quantite: heures,
                montant: montant,
                soumisIUTS: true,
                soumisCNSS: true,
                soumisTPA: true));
        }

        /// <summary>
        /// Charge l'ensemble des apports d'heures supplémentaires du mois pour une liste de salariés (pour le traitement en lot).
        /// </summary>
        public async Task<Dictionary<string, ApportHeuresSupplementaires>> ChargerApportsMoisAsync(
            int annee, int mois, IEnumerable<Employee> salaries)
        {
            var dico = new Dictionary<string, ApportHeuresSupplementaires>(StringComparer.OrdinalIgnoreCase);
            if (salaries == null) return dico;

            var mapSalaries = salaries
                .Where(s => s != null && !string.IsNullOrWhiteSpace(s.EmployeeId))
                .ToDictionary(s => s.EmployeeId, StringComparer.OrdinalIgnoreCase);

            if (mapSalaries.Count == 0) return dico;

            var bareme = await _baremeService.ApplicableAsync(annee, mois);

            using (var conn = _sqliteService.CreateConnection())
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "SELECT " + Colonnes + " FROM HeuresSupplementaires h WHERE h.Annee = @a AND h.Mois = @m;";
                cmd.Parameters.AddWithValue("@a", annee);
                cmd.Parameters.AddWithValue("@m", mois);

                using (var reader = (SQLiteDataReader)await cmd.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                    {
                        var hs = Lire(reader);
                        if (mapSalaries.TryGetValue(hs.EmployeeId, out var emp))
                        {
                            hs.SalaireDeBase = emp.BaseSalary;
                            hs.AppliquerBareme(bareme);

                            if (hs.MontantTotal > 0m)
                            {
                                dico[hs.EmployeeId] = new ApportHeuresSupplementaires(LignesBulletin(hs), hs.MontantTotal, hs.TotalHeures);
                            }
                        }
                    }
                }
            }

            return dico;
        }

        private static HeureSupplementaire Lire(SQLiteDataReader reader)
        {
            return new HeureSupplementaire
            {
                Id = DbValue.LongFromDb(reader["Id"]),
                EmployeeId = DbValue.TextFromDb(reader["EmployeeId"]),
                Annee = DbValue.IntFromDb(reader["Annee"]),
                Mois = DbValue.IntFromDb(reader["Mois"]),
                Heures15 = DbValue.DecimalFromDb(reader["Heures15"]),
                Heures30 = DbValue.DecimalFromDb(reader["Heures30"]),
                Heures50 = DbValue.DecimalFromDb(reader["Heures50"]),
                Heures60 = DbValue.DecimalFromDb(reader["Heures60"]),
                Heures100 = DbValue.DecimalFromDb(reader["Heures100"]),
                TauxHoraireBase = DbValue.MoneyFromDb(reader["TauxHoraireBase"]),
                Montant15 = DbValue.MoneyFromDb(reader["Montant15"]),
                Montant30 = DbValue.MoneyFromDb(reader["Montant30"]),
                Montant50 = DbValue.MoneyFromDb(reader["Montant50"]),
                Montant60 = DbValue.MoneyFromDb(reader["Montant60"]),
                Montant100 = DbValue.MoneyFromDb(reader["Montant100"]),
                MontantTotal = DbValue.MoneyFromDb(reader["MontantTotal"]),
                Commentaire = DbValue.TextFromDb(reader["Commentaire"]),
                DateCreation = DbValue.DateFromDb(reader["DateCreation"], DateTime.Now),
                DateModification = DbValue.DateFromDb(reader["DateModification"], DateTime.Now),
                EstModifie = false
            };
        }
    }
}
