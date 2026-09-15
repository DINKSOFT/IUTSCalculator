using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Linq;
using System.Threading.Tasks;
using IUTSCalculator.Models;

namespace IUTSCalculator.Services
{
    /// <summary>
    /// Ce que le profil d'un salarié apporte à son bulletin du mois (module SOC-03).
    ///
    /// Les lignes sont des <see cref="LigneApport"/>, comme celles des variables du mois :
    /// le moteur ne fait aucune différence entre une prime qui vient d'un profil et la
    /// même prime saisie à la main, et il n'a aucune raison d'en faire une. Ce qui change
    /// est seulement l'endroit où elle a été écrite une fois pour toutes.
    ///
    /// <b>Équivalence stricte.</b> <see cref="Aucun"/> ne porte aucune ligne : un salarié
    /// sans profil produit exactement le bulletin qu'il produisait avant ce module.
    /// </summary>
    public sealed class ApportProfil
    {
        private readonly List<LigneApport> _lignes = new List<LigneApport>();

        private ApportProfil()
        {
        }

        public static ApportProfil Aucun()
        {
            return new ApportProfil();
        }

        /// <summary>Désignation du profil appliqué, reprise sur l'écran de contrôle.</summary>
        public string ProfilDesignation { get; private set; }

        public IReadOnlyList<LigneApport> Lignes
        {
            get { return _lignes; }
        }

        public bool EstVide
        {
            get { return _lignes.Count == 0; }
        }

        internal void Nommer(string designation)
        {
            ProfilDesignation = designation;
        }

        internal void Ajouter(LigneApport ligne)
        {
            if (ligne == null || ligne.Montant == 0m) return;
            _lignes.Add(ligne);
        }
    }

    /// <summary>
    /// Profils de paie (module SOC-03).
    ///
    /// Le plan de paie (SOC-01) dit quelles rubriques existent ; le profil dit lesquelles
    /// s'appliquent à quelle population, et avec quel montant par défaut. Un salarié porte
    /// son profil, et les rubriques du profil alimentent son bulletin à chaque calcul.
    ///
    /// <b>Trois règles gouvernent tout le module.</b>
    ///
    ///   • <b>La variable du mois l'emporte.</b> Si la même rubrique est saisie dans les
    ///     variables du mois, c'est elle qui compte, et la ligne du profil est écartée.
    ///     Sans cette priorité, une prime exceptionnelle s'ajouterait à la prime habituelle
    ///     au lieu de la remplacer, et le brut serait faux sans que rien ne le signale.
    ///
    ///   • <b>Un profil ne porte aucune rubrique du moteur.</b> Salaire de base,
    ///     indemnités plafonnées de l'article 106, CNSS, IUTS : le moteur audité les lit
    ///     sur la fiche du salarié. Les porter aussi en profil les compterait deux fois.
    ///     Les rubriques <c>EstSysteme</c> et celles en mode <c>Moteur</c> sont donc
    ///     refusées à la saisie comme ignorées au calcul.
    ///
    ///   • <b>L'affectation est datée.</b> Un salarié promu cadre en avril garde son profil
    ///     d'employé sur ses bulletins de janvier à mars. C'est la même exigence que pour
    ///     les barèmes de SOC-02 : recalculer un mois doit utiliser ce qui était vrai ce
    ///     mois-là.
    /// </summary>
    public class ProfilPaieService
    {
        private readonly SQLiteService _sqliteService;

        private const string ColonnesProfil =
            "p.Id, p.Code, p.Libelle, p.Population, p.Description, p.Actif, p.Ordre, p.DateSaisie";

        public ProfilPaieService(SQLiteService sqliteService)
        {
            if (sqliteService == null) throw new ArgumentNullException("sqliteService");
            _sqliteService = sqliteService;
        }

        // ── Lecture des profils ──────────────────────────────────────────────

        /// <summary>
        /// Tous les profils du dossier, avec le nombre de rubriques qu'ils portent et le
        /// nombre de salariés qui les portent aujourd'hui. Ce second chiffre est ce qui
        /// dit au gestionnaire combien de bulletins une retouche va déplacer.
        /// </summary>
        public async Task<List<ProfilPaie>> ChargerAsync()
        {
            var liste = new List<ProfilPaie>();

            using (var conn = _sqliteService.CreateConnection())
            {
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText =
                        "SELECT " + ColonnesProfil + " FROM ProfilsPaie p " +
                        "ORDER BY p.Ordre, p.Code;";

                    using (var reader = (SQLiteDataReader)await cmd.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                            liste.Add(LireProfil(reader));
                    }
                }

                var lignes = await CompterAsync(conn,
                    "SELECT ProfilId, COUNT(*) AS N FROM LignesProfil WHERE Active = 1 GROUP BY ProfilId;");

                var affectations = await ProfilsApplicablesAsync(conn, DateTime.Today.Date.AddDays(1).AddSeconds(-1));

                var salariesParProfil = affectations
                    .Where(a => a.Value > 0)
                    .GroupBy(a => a.Value)
                    .ToDictionary(g => g.Key, g => g.Count());

                foreach (var profil in liste)
                {
                    int n;
                    profil.NombreLignes = lignes.TryGetValue(profil.Id, out n) ? n : 0;
                    profil.NombreSalaries = salariesParProfil.TryGetValue(profil.Id, out n) ? n : 0;
                }
            }

            return liste;
        }

        private static async Task<Dictionary<int, int>> CompterAsync(SQLiteConnection conn, string sql)
        {
            var comptes = new Dictionary<int, int>();

            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = sql;

                using (var reader = (SQLiteDataReader)await cmd.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                        comptes[DbValue.IntFromDb(reader["ProfilId"])] = DbValue.IntFromDb(reader["N"]);
                }
            }

            return comptes;
        }

        private static ProfilPaie LireProfil(SQLiteDataReader reader)
        {
            return new ProfilPaie
            {
                Id = DbValue.IntFromDb(reader["Id"]),
                Code = DbValue.TextFromDb(reader["Code"]),
                Libelle = DbValue.TextFromDb(reader["Libelle"]),
                Population = (PopulationProfil)DbValue.IntFromDb(reader["Population"]),
                Description = DbValue.TextFromDb(reader["Description"]),
                Actif = DbValue.IntFromDb(reader["Actif"]) != 0,
                Ordre = DbValue.IntFromDb(reader["Ordre"]),
                DateSaisie = DbValue.DateFromDb(reader["DateSaisie"], DateTime.Now)
            };
        }

        /// <summary>
        /// Rubriques d'un profil, enrichies du libellé, du sens et du mode de calcul lus
        /// dans le plan de paie fourni. La jointure se fait en mémoire : le plan est déjà
        /// chargé par l'écran, et le relire en SQL pour chaque ligne ne dirait rien de plus.
        /// </summary>
        public async Task<List<LigneProfil>> ChargerLignesAsync(int profilId, IEnumerable<Rubrique> planDePaie)
        {
            var liste = new List<LigneProfil>();
            if (profilId <= 0) return liste;

            using (var conn = _sqliteService.CreateConnection())
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText =
                    "SELECT Id, ProfilId, CodeRubrique, Montant, Quantite, Ordre, Active, Commentaire " +
                    "FROM LignesProfil WHERE ProfilId = @profil ORDER BY Ordre, Id;";
                cmd.Parameters.AddWithValue("@profil", profilId);

                using (var reader = (SQLiteDataReader)await cmd.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                    {
                        liste.Add(new LigneProfil
                        {
                            Id = DbValue.IntFromDb(reader["Id"]),
                            ProfilId = DbValue.IntFromDb(reader["ProfilId"]),
                            CodeRubrique = DbValue.TextFromDb(reader["CodeRubrique"]),
                            Montant = DbValue.MoneyFromDb(reader["Montant"]),
                            Quantite = DbValue.DecimalFromDb(reader["Quantite"]),
                            Ordre = DbValue.IntFromDb(reader["Ordre"]),
                            Active = DbValue.IntFromDb(reader["Active"]) != 0,
                            Commentaire = DbValue.TextFromDb(reader["Commentaire"])
                        });
                    }
                }
            }

            var plan = Indexer(planDePaie);

            foreach (var ligne in liste)
            {
                Rubrique rubrique;
                if (plan.TryGetValue(Cle(ligne.CodeRubrique), out rubrique))
                {
                    ligne.LibelleRubrique = rubrique.Libelle;
                    ligne.Sens = rubrique.Sens;
                    ligne.ModeCalcul = rubrique.ModeCalcul;
                }
                else
                {
                    ligne.LibelleRubrique = "Rubrique absente du plan de paie";
                }
            }

            return liste;
        }

        private static Dictionary<string, Rubrique> Indexer(IEnumerable<Rubrique> planDePaie)
        {
            var plan = new Dictionary<string, Rubrique>();

            foreach (var rubrique in (planDePaie ?? Enumerable.Empty<Rubrique>()).Where(r => r != null))
                plan[Cle(rubrique.Code)] = rubrique;

            return plan;
        }

        private static string Cle(string code)
        {
            return (code ?? "").Trim().ToUpperInvariant();
        }

        // ── Écriture des profils ─────────────────────────────────────────────

        public async Task EnregistrerAsync(ProfilPaie profil)
        {
            if (profil == null) throw new ArgumentNullException("profil");

            var anomalie = Anomalie(profil);
            if (anomalie != null) throw new InvalidOperationException(anomalie);

            string code = Cle(profil.Code);

            using (var conn = _sqliteService.CreateConnection())
            {
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText =
                        "SELECT COUNT(*) FROM ProfilsPaie WHERE UPPER(Code) = @code AND Id <> @id;";
                    cmd.Parameters.AddWithValue("@code", code);
                    cmd.Parameters.AddWithValue("@id", profil.Id);

                    if (DbValue.IntFromDb(await cmd.ExecuteScalarAsync()) > 0)
                    {
                        throw new InvalidOperationException(string.Format(
                            "Le code « {0} » est déjà porté par un autre profil. Les affectations " +
                            "s'appuient sur le code : deux profils homonymes rendraient illisible " +
                            "la fiche d'un salarié.", code));
                    }
                }

                using (var cmd = conn.CreateCommand())
                {
                    if (profil.Id > 0)
                    {
                        cmd.CommandText =
                            "UPDATE ProfilsPaie SET Code = @code, Libelle = @libelle, " +
                            "Population = @population, Description = @description, " +
                            "Actif = @actif, Ordre = @ordre WHERE Id = @id;";
                        cmd.Parameters.AddWithValue("@id", profil.Id);
                    }
                    else
                    {
                        cmd.CommandText =
                            "INSERT INTO ProfilsPaie " +
                            "(Code, Libelle, Population, Description, Actif, Ordre, DateSaisie) " +
                            "VALUES (@code, @libelle, @population, @description, @actif, @ordre, @saisie);";
                        cmd.Parameters.AddWithValue("@saisie", DbValue.DateToDb(DateTime.Now));
                    }

                    cmd.Parameters.AddWithValue("@code", code);
                    cmd.Parameters.AddWithValue("@libelle", profil.Libelle ?? "");
                    cmd.Parameters.AddWithValue("@population", (int)profil.Population);
                    cmd.Parameters.AddWithValue("@description", profil.Description ?? "");
                    cmd.Parameters.AddWithValue("@actif", profil.Actif ? 1 : 0);
                    cmd.Parameters.AddWithValue("@ordre", profil.Ordre);

                    await cmd.ExecuteNonQueryAsync();
                }

                profil.Code = code;

                if (profil.Id <= 0)
                {
                    using (var cmd = conn.CreateCommand())
                    {
                        cmd.CommandText = "SELECT last_insert_rowid();";
                        profil.Id = DbValue.IntFromDb(await cmd.ExecuteScalarAsync());
                    }
                }
            }
        }

        /// <summary>
        /// Supprime un profil et ses rubriques. Refusé tant qu'un salarié le porte ou l'a
        /// porté : l'affectation datée est la pièce qui explique un bulletin passé, et un
        /// profil effacé la rendrait muette. Un profil devenu inutile se désactive.
        /// </summary>
        public async Task SupprimerAsync(ProfilPaie profil)
        {
            if (profil == null || profil.Id <= 0) return;

            using (var conn = _sqliteService.CreateConnection())
            {
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT COUNT(*) FROM AffectationsProfil WHERE ProfilId = @id;";
                    cmd.Parameters.AddWithValue("@id", profil.Id);

                    int affectations = DbValue.IntFromDb(await cmd.ExecuteScalarAsync());
                    if (affectations > 0)
                    {
                        throw new InvalidOperationException(string.Format(
                            "Ce profil a été affecté à {0} salarié(s) : il ne peut plus être " +
                            "supprimé sans priver leurs bulletins passés de la pièce qui les " +
                            "explique. Décochez « Actif » pour qu'il ne soit plus proposé.",
                            affectations));
                    }
                }

                using (var tx = conn.BeginTransaction())
                {
                    try
                    {
                        using (var cmd = conn.CreateCommand())
                        {
                            cmd.Transaction = tx;
                            cmd.CommandText = "DELETE FROM LignesProfil WHERE ProfilId = @id;";
                            cmd.Parameters.AddWithValue("@id", profil.Id);
                            await cmd.ExecuteNonQueryAsync();
                        }

                        using (var cmd = conn.CreateCommand())
                        {
                            cmd.Transaction = tx;
                            cmd.CommandText = "DELETE FROM ProfilsPaie WHERE Id = @id;";
                            cmd.Parameters.AddWithValue("@id", profil.Id);
                            await cmd.ExecuteNonQueryAsync();
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
        /// Remplace en bloc les rubriques d'un profil, dans une transaction. Une liste à
        /// moitié écrite donnerait des bulletins faux sur toute une population.
        /// </summary>
        public async Task EnregistrerLignesAsync(int profilId, IEnumerable<LigneProfil> lignes)
        {
            if (profilId <= 0) throw new InvalidOperationException("Enregistrez d'abord le profil.");

            var liste = (lignes ?? Enumerable.Empty<LigneProfil>())
                .Where(l => l != null && !string.IsNullOrWhiteSpace(l.CodeRubrique))
                .ToList();

            using (var conn = _sqliteService.CreateConnection())
            using (var tx = conn.BeginTransaction())
            {
                try
                {
                    using (var cmd = conn.CreateCommand())
                    {
                        cmd.Transaction = tx;
                        cmd.CommandText = "DELETE FROM LignesProfil WHERE ProfilId = @profil;";
                        cmd.Parameters.AddWithValue("@profil", profilId);
                        await cmd.ExecuteNonQueryAsync();
                    }

                    int ordre = 0;

                    foreach (var ligne in liste)
                    {
                        ordre += 10;

                        using (var cmd = conn.CreateCommand())
                        {
                            cmd.Transaction = tx;
                            cmd.CommandText =
                                "INSERT INTO LignesProfil " +
                                "(ProfilId, CodeRubrique, Montant, Quantite, Ordre, Active, Commentaire) " +
                                "VALUES (@profil, @code, @montant, @quantite, @ordre, @active, @commentaire);";
                            cmd.Parameters.AddWithValue("@profil", profilId);
                            cmd.Parameters.AddWithValue("@code", Cle(ligne.CodeRubrique));
                            cmd.Parameters.AddWithValue("@montant", DbValue.MoneyToDb(ligne.Montant));
                            cmd.Parameters.AddWithValue("@quantite", DbValue.DecimalToDb(ligne.Quantite));
                            cmd.Parameters.AddWithValue("@ordre", ligne.Ordre > 0 ? ligne.Ordre : ordre);
                            cmd.Parameters.AddWithValue("@active", ligne.Active ? 1 : 0);
                            cmd.Parameters.AddWithValue("@commentaire", ligne.Commentaire ?? "");
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

        // ── Affectations ─────────────────────────────────────────────────────

        /// <summary>
        /// Historique complet des rattachements, du plus récent au plus ancien. C'est lui
        /// qui justifie qu'un bulletin de mars ait été calculé avec le profil de mars.
        /// </summary>
        public async Task<List<AffectationProfil>> ChargerAffectationsAsync()
        {
            var liste = new List<AffectationProfil>();

            using (var conn = _sqliteService.CreateConnection())
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText =
                    "SELECT a.Id, a.EmployeeId, a.ProfilId, a.DateEffet, a.Commentaire, " +
                    "       e.FirstName, e.LastName, p.Code, p.Libelle " +
                    "FROM AffectationsProfil a " +
                    "LEFT JOIN Employees e ON e.EmployeeId = a.EmployeeId " +
                    "LEFT JOIN ProfilsPaie p ON p.Id = a.ProfilId " +
                    "ORDER BY a.DateEffet DESC, a.Id DESC;";

                using (var reader = (SQLiteDataReader)await cmd.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                    {
                        string nom = DbValue.TextFromDb(reader["LastName"]);
                        string prenom = DbValue.TextFromDb(reader["FirstName"]);
                        string complet = string.Format("{0} {1}", nom, prenom).Trim();

                        string code = DbValue.TextFromDb(reader["Code"]);
                        string libelle = DbValue.TextFromDb(reader["Libelle"]);

                        liste.Add(new AffectationProfil
                        {
                            Id = DbValue.IntFromDb(reader["Id"]),
                            EmployeeId = DbValue.TextFromDb(reader["EmployeeId"]),
                            NomSalarie = string.IsNullOrWhiteSpace(complet) ? "Salarié supprimé" : complet,
                            ProfilId = DbValue.IntFromDb(reader["ProfilId"]),
                            ProfilDesignation = string.IsNullOrWhiteSpace(code)
                                ? null
                                : string.Format("{0} — {1}", code, libelle),
                            DateEffet = DbValue.DateFromDb(reader["DateEffet"], DateTime.Today),
                            Commentaire = DbValue.TextFromDb(reader["Commentaire"])
                        });
                    }
                }
            }

            return liste;
        }

        /// <summary>
        /// Rattache un salarié à un profil à partir d'une date. Un <paramref name="profilId"/>
        /// à zéro retire le profil — ce retrait est lui aussi daté, et conservé.
        ///
        /// Deux affectations du même salarié au même jour n'auraient aucun sens : la
        /// seconde remplace la première.
        /// </summary>
        public async Task AffecterAsync(string employeeId, int profilId, DateTime dateEffet, string commentaire)
        {
            if (string.IsNullOrWhiteSpace(employeeId))
                throw new InvalidOperationException("Choisissez le salarié à rattacher.");

            var jour = dateEffet.Date;

            using (var conn = _sqliteService.CreateConnection())
            {
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText =
                        "DELETE FROM AffectationsProfil " +
                        "WHERE EmployeeId = @employe AND DateEffet = @date;";
                    cmd.Parameters.AddWithValue("@employe", employeeId);
                    cmd.Parameters.AddWithValue("@date", DbValue.DateToDb(jour));
                    await cmd.ExecuteNonQueryAsync();
                }

                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText =
                        "INSERT INTO AffectationsProfil (EmployeeId, ProfilId, DateEffet, Commentaire) " +
                        "VALUES (@employe, @profil, @date, @commentaire);";
                    cmd.Parameters.AddWithValue("@employe", employeeId);
                    cmd.Parameters.AddWithValue("@profil", profilId < 0 ? 0 : profilId);
                    cmd.Parameters.AddWithValue("@date", DbValue.DateToDb(jour));
                    cmd.Parameters.AddWithValue("@commentaire", commentaire ?? "");
                    await cmd.ExecuteNonQueryAsync();
                }
            }
        }

        /// <summary>Retire une ligne de l'historique des rattachements.</summary>
        public async Task SupprimerAffectationAsync(AffectationProfil affectation)
        {
            if (affectation == null || affectation.Id <= 0) return;

            using (var conn = _sqliteService.CreateConnection())
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "DELETE FROM AffectationsProfil WHERE Id = @id;";
                cmd.Parameters.AddWithValue("@id", affectation.Id);
                await cmd.ExecuteNonQueryAsync();
            }
        }

        /// <summary>
        /// Profil applicable à chaque salarié à une date : la dernière affectation dont la
        /// date d'effet ne dépasse pas cette date. Zéro pour un salarié sans profil.
        /// </summary>
        public async Task<Dictionary<string, int>> ProfilsApplicablesAsync(DateTime arrete)
        {
            using (var conn = _sqliteService.CreateConnection())
            {
                return await ProfilsApplicablesAsync(conn, arrete);
            }
        }

        private static async Task<Dictionary<string, int>> ProfilsApplicablesAsync(
            SQLiteConnection conn, DateTime arrete)
        {
            var applicables = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            try
            {
                using (var cmd = conn.CreateCommand())
                {
                    // Les dates sont écrites au format triable « yyyy-MM-dd HH:mm:ss » : la
                    // comparaison de chaînes suffit, et l'ordre SQL donne directement la
                    // dernière affectation de chaque salarié.
                    cmd.CommandText =
                        "SELECT EmployeeId, ProfilId FROM AffectationsProfil " +
                        "WHERE DateEffet <= @arrete " +
                        "ORDER BY EmployeeId, DateEffet, Id;";
                    cmd.Parameters.AddWithValue("@arrete", DbValue.DateToDb(arrete));

                    using (var reader = (SQLiteDataReader)await cmd.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            string employe = DbValue.TextFromDb(reader["EmployeeId"]);
                            if (string.IsNullOrWhiteSpace(employe)) continue;

                            applicables[employe] = DbValue.IntFromDb(reader["ProfilId"]);
                        }
                    }
                }
            }
            catch
            {
                // Dossier antérieur au module : aucun salarié n'a de profil, ce qui est la
                // situation d'avant SOC-03 et n'empêche aucun calcul.
            }

            return applicables;
        }

        // ── Apport au bulletin ───────────────────────────────────────────────

        /// <summary>
        /// Apports de TOUS les salariés d'une période, en une seule lecture. À utiliser
        /// pour un traitement en lot : appeler <see cref="ApportAsync"/> dans une boucle
        /// relirait les profils autant de fois qu'il y a de bulletins.
        /// </summary>
        public async Task<Dictionary<string, ApportProfil>> ApportsAsync(
            int annee, int mois, IEnumerable<Employee> salaries, IEnumerable<Rubrique> planDePaie)
        {
            var resultat = new Dictionary<string, ApportProfil>(StringComparer.OrdinalIgnoreCase);

            var effectif = (salaries ?? Enumerable.Empty<Employee>())
                .Where(s => s != null && !string.IsNullOrWhiteSpace(s.EmployeeId))
                .ToList();

            if (effectif.Count == 0 || annee <= 0 || mois < 1 || mois > 12)
                return resultat;

            var arrete = new DateTime(annee, mois, 1).AddMonths(1).AddSeconds(-1);
            var plan = Indexer(planDePaie);

            Dictionary<string, int> applicables;
            Dictionary<int, ProfilPaie> profils;
            Dictionary<int, List<LigneProfil>> lignesParProfil;

            using (var conn = _sqliteService.CreateConnection())
            {
                applicables = await ProfilsApplicablesAsync(conn, arrete);
                if (applicables.Count == 0) return resultat;

                profils = await ChargerProfilsIndexesAsync(conn);
                lignesParProfil = await ChargerToutesLignesAsync(conn);
            }

            foreach (var salarie in effectif)
            {
                int profilId;
                if (!applicables.TryGetValue(salarie.EmployeeId, out profilId) || profilId <= 0)
                    continue;

                ProfilPaie profil;
                if (!profils.TryGetValue(profilId, out profil) || !profil.Actif)
                    continue;

                List<LigneProfil> lignes;
                if (!lignesParProfil.TryGetValue(profilId, out lignes) || lignes.Count == 0)
                    continue;

                var apport = ApportProfil.Aucun();
                apport.Nommer(profil.Designation);

                foreach (var ligne in lignes.Where(l => l.Active))
                {
                    Rubrique rubrique;
                    if (!plan.TryGetValue(Cle(ligne.CodeRubrique), out rubrique))
                        continue;

                    if (!EstApplicable(rubrique))
                        continue;

                    decimal montant = Valoriser(ligne, rubrique, salarie);
                    if (montant == 0m) continue;

                    apport.Ajouter(new LigneApport(
                        rubrique.Code,
                        rubrique.Libelle,
                        rubrique.Sens,
                        ligne.Quantite,
                        montant,
                        rubrique.SoumisIUTS,
                        rubrique.SoumisCNSS,
                        rubrique.SoumisTPA));
                }

                if (!apport.EstVide)
                    resultat[salarie.EmployeeId] = apport;
            }

            return resultat;
        }

        /// <summary>Apport du profil d'un seul salarié sur une période.</summary>
        public async Task<ApportProfil> ApportAsync(
            Employee salarie, int annee, int mois, IEnumerable<Rubrique> planDePaie)
        {
            if (salarie == null || string.IsNullOrWhiteSpace(salarie.EmployeeId))
                return ApportProfil.Aucun();

            var tous = await ApportsAsync(annee, mois, new[] { salarie }, planDePaie);
            return ApportDe(tous, salarie.EmployeeId);
        }

        /// <summary>Apport d'un salarié dans un lot déjà chargé, ou un apport vide.</summary>
        public static ApportProfil ApportDe(Dictionary<string, ApportProfil> apports, string employeeId)
        {
            ApportProfil apport;
            if (apports != null && !string.IsNullOrWhiteSpace(employeeId)
                && apports.TryGetValue(employeeId, out apport))
            {
                return apport;
            }

            return ApportProfil.Aucun();
        }

        private static async Task<Dictionary<int, ProfilPaie>> ChargerProfilsIndexesAsync(SQLiteConnection conn)
        {
            var profils = new Dictionary<int, ProfilPaie>();

            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "SELECT " + ColonnesProfil + " FROM ProfilsPaie p;";

                using (var reader = (SQLiteDataReader)await cmd.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                    {
                        var profil = LireProfil(reader);
                        profils[profil.Id] = profil;
                    }
                }
            }

            return profils;
        }

        private static async Task<Dictionary<int, List<LigneProfil>>> ChargerToutesLignesAsync(SQLiteConnection conn)
        {
            var parProfil = new Dictionary<int, List<LigneProfil>>();

            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText =
                    "SELECT Id, ProfilId, CodeRubrique, Montant, Quantite, Ordre, Active, Commentaire " +
                    "FROM LignesProfil ORDER BY ProfilId, Ordre, Id;";

                using (var reader = (SQLiteDataReader)await cmd.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                    {
                        var ligne = new LigneProfil
                        {
                            Id = DbValue.IntFromDb(reader["Id"]),
                            ProfilId = DbValue.IntFromDb(reader["ProfilId"]),
                            CodeRubrique = DbValue.TextFromDb(reader["CodeRubrique"]),
                            Montant = DbValue.MoneyFromDb(reader["Montant"]),
                            Quantite = DbValue.DecimalFromDb(reader["Quantite"]),
                            Ordre = DbValue.IntFromDb(reader["Ordre"]),
                            Active = DbValue.IntFromDb(reader["Active"]) != 0,
                            Commentaire = DbValue.TextFromDb(reader["Commentaire"])
                        };

                        List<LigneProfil> liste;
                        if (!parProfil.TryGetValue(ligne.ProfilId, out liste))
                        {
                            liste = new List<LigneProfil>();
                            parProfil[ligne.ProfilId] = liste;
                        }

                        liste.Add(ligne);
                    }
                }
            }

            return parProfil;
        }

        /// <summary>
        /// Une rubrique peut-elle figurer dans un profil ? Non pour celles que le moteur
        /// calcule ou lit lui-même sur la fiche du salarié — les porter aussi en profil
        /// les compterait deux fois — et non pour les lignes d'information, qui n'entrent
        /// dans aucun total.
        /// </summary>
        public static bool EstApplicable(Rubrique rubrique)
        {
            if (rubrique == null || !rubrique.Active) return false;
            if (rubrique.EstSysteme) return false;
            if (rubrique.ModeCalcul == ModeCalculRubrique.Moteur) return false;
            if (rubrique.Sens == SensRubrique.Information) return false;

            return true;
        }

        /// <summary>
        /// Montant d'une ligne de profil pour un salarié donné.
        ///
        /// Le montant saisi sur la ligne prime : c'est la valeur arrêtée pour cette
        /// population. À défaut, la rubrique se valorise elle-même — pourcentage du
        /// salaire de base, ou montant fixe du plan de paie. Si aucune des deux sources
        /// ne dit rien, la ligne ne produit rien, et l'écran le signale.
        ///
        /// Le mode « base × taux » n'est pas valorisé ici : il suppose de résoudre une
        /// autre rubrique, ce que seul le moteur sait faire.
        /// </summary>
        public static decimal Valoriser(LigneProfil ligne, Rubrique rubrique, Employee salarie)
        {
            if (ligne == null || rubrique == null) return 0m;

            if (ligne.Montant != 0m) return ligne.Montant;

            if (rubrique.ModeCalcul == ModeCalculRubrique.PourcentageSalaireBase)
            {
                if (salarie == null || rubrique.TauxSalarial == 0m) return 0m;
                return Math.Round(salarie.BaseSalary * rubrique.TauxSalarial, 0, MidpointRounding.AwayFromZero);
            }

            if (rubrique.ModeCalcul == ModeCalculRubrique.MontantFixe)
                return rubrique.MontantFixe;

            return 0m;
        }

        // ── Contrôles ────────────────────────────────────────────────────────

        /// <summary>Ce qui empêche d'enregistrer un profil, en une phrase. Null si tout va bien.</summary>
        public static string Anomalie(ProfilPaie profil)
        {
            if (profil == null) return "Profil vide.";

            if (string.IsNullOrWhiteSpace(profil.Code))
                return "Donnez un code au profil (ex. CADRE).";

            if (profil.Code.Trim().Length > 20)
                return "Le code d'un profil tient en 20 caractères au plus.";

            if (string.IsNullOrWhiteSpace(profil.Libelle))
                return "Donnez un libellé au profil.";

            return null;
        }

        /// <summary>
        /// Ce qui mérite d'être signalé sur les rubriques d'un profil sans empêcher
        /// d'enregistrer. Le plus utile est le dernier : une ligne que rien ne valorise
        /// sera saisie, enregistrée, affichée — et n'apparaîtra sur aucun bulletin.
        /// </summary>
        public static List<string> Anomalies(IEnumerable<LigneProfil> lignes, IEnumerable<Rubrique> planDePaie)
        {
            var messages = new List<string>();
            var liste = (lignes ?? Enumerable.Empty<LigneProfil>()).Where(l => l != null).ToList();

            if (liste.Count == 0) return messages;

            var plan = Indexer(planDePaie);

            var inconnues = liste
                .Select(l => Cle(l.CodeRubrique))
                .Where(c => !string.IsNullOrWhiteSpace(c) && !plan.ContainsKey(c))
                .Distinct()
                .ToList();

            if (inconnues.Count > 0)
            {
                messages.Add(string.Format(
                    "Rubrique(s) absente(s) du plan de paie : {0}. Ces lignes ne seront calculées " +
                    "sur aucun bulletin.", string.Join(", ", inconnues)));
            }

            var doublons = liste
                .GroupBy(l => Cle(l.CodeRubrique))
                .Where(g => g.Count() > 1)
                .Select(g => g.Key)
                .ToList();

            if (doublons.Count > 0)
            {
                messages.Add(string.Format(
                    "Rubrique(s) présente(s) deux fois dans ce profil : {0}. Les montants " +
                    "s'ajouteront sur le bulletin.", string.Join(", ", doublons)));
            }

            var refusees = new List<string>();
            var sansValeur = new List<string>();

            foreach (var ligne in liste.Where(l => l.Active))
            {
                Rubrique rubrique;
                if (!plan.TryGetValue(Cle(ligne.CodeRubrique), out rubrique)) continue;

                if (!EstApplicable(rubrique))
                {
                    refusees.Add(rubrique.Code);
                    continue;
                }

                if (ligne.Montant == 0m
                    && rubrique.ModeCalcul != ModeCalculRubrique.PourcentageSalaireBase
                    && !(rubrique.ModeCalcul == ModeCalculRubrique.MontantFixe && rubrique.MontantFixe != 0m))
                {
                    sansValeur.Add(rubrique.Code);
                }
            }

            if (refusees.Count > 0)
            {
                messages.Add(string.Format(
                    "Rubrique(s) que le moteur lit déjà sur la fiche du salarié : {0}. Elles sont " +
                    "ignorées au calcul — les porter ici les compterait deux fois.",
                    string.Join(", ", refusees.Distinct())));
            }

            if (sansValeur.Count > 0)
            {
                messages.Add(string.Format(
                    "Rubrique(s) sans montant : {0}. Ni la ligne ni le plan de paie ne les " +
                    "valorisent : elles n'apparaîtront sur aucun bulletin.",
                    string.Join(", ", sansValeur.Distinct())));
            }

            return messages;
        }
    }
}
