using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Data.SQLite;
using System.Linq;
using System.Threading.Tasks;
using IUTSCalculator.Models;

namespace IUTSCalculator.Services
{
    /// <summary>
    /// Service de gestion de l'organigramme d'entreprise (module RH-01).
    /// Gère les directions, services, postes et rattachements hiérarchiques.
    /// </summary>
    public class OrganigrammeService
    {
        private readonly SQLiteService _sqliteService;

        public OrganigrammeService(SQLiteService sqliteService)
        {
            _sqliteService = sqliteService ?? throw new ArgumentNullException(nameof(sqliteService));
        }

        #region Unités Organisationnelles (Directions & Services)

        /// <summary>
        /// Charge l'ensemble des unités organisationnelles avec les informations de leur parent,
        /// de leur responsable et le décompte de leurs postes et salariés.
        /// </summary>
        public async Task<List<UniteOrganisationnelle>> ChargerUnitesAsync()
        {
            var unites = new List<UniteOrganisationnelle>();

            using (var conn = _sqliteService.CreateConnection())
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = @"
                    SELECT 
                        u.Id, u.Code, u.Libelle, u.TypeUnite, u.ParentId, u.ResponsableMatricule,
                        u.Description, u.Couleur, u.Ordre, u.DateCreation,
                        p.Libelle AS ParentLibelle,
                        e.FirstName || ' ' || e.LastName AS ResponsableNom,
                        (SELECT COUNT(*) FROM Postes pos WHERE pos.UniteId = u.Id) AS NbPostes,
                        (SELECT COUNT(*) FROM Employees emp WHERE emp.Service = u.Libelle AND emp.StatutSalarie = 0) AS NbSalariesDirects
                    FROM UnitesOrganisationnelles u
                    LEFT JOIN UnitesOrganisationnelles p ON u.ParentId = p.Id
                    LEFT JOIN Employees e ON u.ResponsableMatricule = e.EmployeeId
                    ORDER BY u.Ordre ASC, u.Libelle ASC;";

                using (var reader = (SQLiteDataReader)await cmd.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                    {
                        var u = new UniteOrganisationnelle
                        {
                            Id = DbValue.IntFromDb(reader["Id"]),
                            Code = DbValue.TextFromDb(reader["Code"]),
                            Libelle = DbValue.TextFromDb(reader["Libelle"]),
                            TypeUnite = (TypeUniteOrganisationnelle)DbValue.IntFromDb(reader["TypeUnite"]),
                            ParentId = DbValue.IntNullableFromDb(reader["ParentId"]),
                            ParentLibelle = DbValue.TextFromDb(reader["ParentLibelle"]),
                            ResponsableMatricule = DbValue.TextFromDb(reader["ResponsableMatricule"]),
                            ResponsableNom = DbValue.TextFromDb(reader["ResponsableNom"]),
                            Description = DbValue.TextFromDb(reader["Description"]),
                            Couleur = DbValue.TextFromDb(reader["Couleur"]) ?? "#14675A",
                            Ordre = DbValue.IntFromDb(reader["Ordre"]),
                            DateCreation = DbValue.DateFromDb(reader["DateCreation"], DateTime.Now),
                            NombrePostes = DbValue.IntFromDb(reader["NbPostes"]),
                            NombreSalariesDirects = DbValue.IntFromDb(reader["NbSalariesDirects"])
                        };
                        unites.Add(u);
                    }
                }
            }

            // Calcul récursif de l'effectif total (salariés de l'unité + salariés de ses sous-unités)
            CalculerEffectifsTotaux(unites);

            return unites;
        }

        private static void CalculerEffectifsTotaux(List<UniteOrganisationnelle> unites)
        {
            var dic = unites.ToDictionary(u => u.Id);
            var enfantsParParent = new Dictionary<int, List<int>>();

            foreach (var u in unites)
            {
                if (u.ParentId.HasValue && dic.ContainsKey(u.ParentId.Value))
                {
                    if (!enfantsParParent.TryGetValue(u.ParentId.Value, out var list))
                    {
                        list = new List<int>();
                        enfantsParParent[u.ParentId.Value] = list;
                    }
                    list.Add(u.Id);
                }
            }

            foreach (var u in unites)
            {
                u.NombreSalariesTotal = CalculerTotalDescendants(u.Id, dic, enfantsParParent);
            }
        }

        private static int CalculerTotalDescendants(int uniteId, Dictionary<int, UniteOrganisationnelle> dic, Dictionary<int, List<int>> enfantsParParent)
        {
            if (!dic.TryGetValue(uniteId, out var unite)) return 0;

            int total = unite.NombreSalariesDirects;
            if (enfantsParParent.TryGetValue(uniteId, out var enfants))
            {
                foreach (var enfantId in enfants)
                {
                    total += CalculerTotalDescendants(enfantId, dic, enfantsParParent);
                }
            }
            return total;
        }

        /// <summary>
        /// Enregistre une unité organisationnelle (création ou mise à jour).
        /// Contrôle l'unicité du code et l'absence de cycle de parenté.
        /// </summary>
        public async Task EnregistrerUniteAsync(UniteOrganisationnelle unite)
        {
            if (unite == null) throw new ArgumentNullException(nameof(unite));
            if (string.IsNullOrWhiteSpace(unite.Code)) throw new InvalidOperationException("Le code de l'unité est obligatoire.");
            if (string.IsNullOrWhiteSpace(unite.Libelle)) throw new InvalidOperationException("Le libellé de l'unité est obligatoire.");

            unite.Code = unite.Code.Trim().ToUpperInvariant();
            unite.Libelle = unite.Libelle.Trim();

            using (var conn = _sqliteService.CreateConnection())
            using (var tx = conn.BeginTransaction())
            {
                // Vérifier unicité du code
                using (var cmd = conn.CreateCommand())
                {
                    cmd.Transaction = tx;
                    cmd.CommandText = "SELECT Id FROM UnitesOrganisationnelles WHERE Code = @Code AND Id <> @Id;";
                    cmd.Parameters.AddWithValue("@Code", unite.Code);
                    cmd.Parameters.AddWithValue("@Id", unite.Id);
                    var existant = await cmd.ExecuteScalarAsync();
                    if (existant != null && existant != DBNull.Value)
                    {
                        throw new InvalidOperationException($"Le code '{unite.Code}' est déjà utilisé par une autre unité.");
                    }
                }

                // Vérifier l'absence de cycle hiérarchique
                if (unite.Id > 0 && unite.ParentId.HasValue)
                {
                    if (unite.ParentId.Value == unite.Id)
                        throw new InvalidOperationException("Une unité ne peut pas être sa propre parente.");

                    if (await EstDescendantUniteAsync(conn, tx, unite.Id, unite.ParentId.Value))
                    {
                        throw new InvalidOperationException("Impossible de créer ce rattachement : l'unité parente choisie est déjà une sous-unité de cette entité (création d'un cycle).");
                    }
                }

                if (unite.Id == 0)
                {
                    // Insertion
                    using (var cmd = conn.CreateCommand())
                    {
                        cmd.Transaction = tx;
                        cmd.CommandText = @"
                            INSERT INTO UnitesOrganisationnelles
                            (Code, Libelle, TypeUnite, ParentId, ResponsableMatricule, Description, Couleur, Ordre, DateCreation)
                            VALUES
                            (@Code, @Libelle, @TypeUnite, @ParentId, @ResponsableMatricule, @Description, @Couleur, @Ordre, @DateCreation);
                            SELECT last_insert_rowid();";

                        cmd.Parameters.AddWithValue("@Code", unite.Code);
                        cmd.Parameters.AddWithValue("@Libelle", unite.Libelle);
                        cmd.Parameters.AddWithValue("@TypeUnite", (int)unite.TypeUnite);
                        cmd.Parameters.AddWithValue("@ParentId", unite.ParentId.HasValue ? (object)unite.ParentId.Value : DBNull.Value);
                        cmd.Parameters.AddWithValue("@ResponsableMatricule", (object)unite.ResponsableMatricule ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@Description", (object)unite.Description ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@Couleur", string.IsNullOrWhiteSpace(unite.Couleur) ? "#14675A" : unite.Couleur);
                        cmd.Parameters.AddWithValue("@Ordre", unite.Ordre);
                        cmd.Parameters.AddWithValue("@DateCreation", DbValue.DateToDb(DateTime.Now));

                        var newId = await cmd.ExecuteScalarAsync();
                        unite.Id = Convert.ToInt32(newId);
                    }
                }
                else
                {
                    // Mise à jour
                    using (var cmd = conn.CreateCommand())
                    {
                        cmd.Transaction = tx;
                        cmd.CommandText = @"
                            UPDATE UnitesOrganisationnelles
                            SET Code = @Code,
                                Libelle = @Libelle,
                                TypeUnite = @TypeUnite,
                                ParentId = @ParentId,
                                ResponsableMatricule = @ResponsableMatricule,
                                Description = @Description,
                                Couleur = @Couleur,
                                Ordre = @Ordre
                            WHERE Id = @Id;";

                        cmd.Parameters.AddWithValue("@Code", unite.Code);
                        cmd.Parameters.AddWithValue("@Libelle", unite.Libelle);
                        cmd.Parameters.AddWithValue("@TypeUnite", (int)unite.TypeUnite);
                        cmd.Parameters.AddWithValue("@ParentId", unite.ParentId.HasValue ? (object)unite.ParentId.Value : DBNull.Value);
                        cmd.Parameters.AddWithValue("@ResponsableMatricule", (object)unite.ResponsableMatricule ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@Description", (object)unite.Description ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@Couleur", string.IsNullOrWhiteSpace(unite.Couleur) ? "#14675A" : unite.Couleur);
                        cmd.Parameters.AddWithValue("@Ordre", unite.Ordre);
                        cmd.Parameters.AddWithValue("@Id", unite.Id);

                        await cmd.ExecuteNonQueryAsync();
                    }
                }

                tx.Commit();
            }
        }

        private async Task<bool> EstDescendantUniteAsync(SQLiteConnection conn, SQLiteTransaction tx, int idParent, int idCandidat)
        {
            int? courantId = idCandidat;
            var visites = new HashSet<int>();

            while (courantId.HasValue)
            {
                if (courantId.Value == idParent) return true;
                if (!visites.Add(courantId.Value)) break;

                using (var cmd = conn.CreateCommand())
                {
                    cmd.Transaction = tx;
                    cmd.CommandText = "SELECT ParentId FROM UnitesOrganisationnelles WHERE Id = @Id;";
                    cmd.Parameters.AddWithValue("@Id", courantId.Value);
                    var parentObj = await cmd.ExecuteScalarAsync();
                    courantId = DbValue.IntNullableFromDb(parentObj);
                }
            }
            return false;
        }

        /// <summary>
        /// Supprime une unité organisationnelle.
        /// Raccroche ses sous-unités directes au grand-parent et détache ses postes.
        /// </summary>
        public async Task SupprimerUniteAsync(int uniteId)
        {
            if (uniteId <= 0) return;

            using (var conn = _sqliteService.CreateConnection())
            using (var tx = conn.BeginTransaction())
            {
                // Obtenir le parent de l'unité à supprimer pour y rattacher ses sous-unités
                int? grandParentId = null;
                using (var cmd = conn.CreateCommand())
                {
                    cmd.Transaction = tx;
                    cmd.CommandText = "SELECT ParentId FROM UnitesOrganisationnelles WHERE Id = @Id;";
                    cmd.Parameters.AddWithValue("@Id", uniteId);
                    grandParentId = DbValue.IntNullableFromDb(await cmd.ExecuteScalarAsync());
                }

                // Raccrocher les sous-unités au grand-parent
                using (var cmd = conn.CreateCommand())
                {
                    cmd.Transaction = tx;
                    cmd.CommandText = "UPDATE UnitesOrganisationnelles SET ParentId = @GrandParentId WHERE ParentId = @Id;";
                    cmd.Parameters.AddWithValue("@GrandParentId", grandParentId.HasValue ? (object)grandParentId.Value : DBNull.Value);
                    cmd.Parameters.AddWithValue("@Id", uniteId);
                    await cmd.ExecuteNonQueryAsync();
                }

                // Détacher les postes rattachés à cette unité
                using (var cmd = conn.CreateCommand())
                {
                    cmd.Transaction = tx;
                    cmd.CommandText = "UPDATE Postes SET UniteId = NULL WHERE UniteId = @Id;";
                    cmd.Parameters.AddWithValue("@Id", uniteId);
                    await cmd.ExecuteNonQueryAsync();
                }

                // Supprimer l'unité
                using (var cmd = conn.CreateCommand())
                {
                    cmd.Transaction = tx;
                    cmd.CommandText = "DELETE FROM UnitesOrganisationnelles WHERE Id = @Id;";
                    cmd.Parameters.AddWithValue("@Id", uniteId);
                    await cmd.ExecuteNonQueryAsync();
                }

                tx.Commit();
            }
        }

        #endregion

        #region Postes et Fonctions

        /// <summary>
        /// Charge la nomenclature complète des postes avec leurs rattachements et leurs titulaires.
        /// </summary>
        public async Task<List<Poste>> ChargerPostesAsync()
        {
            var postes = new List<Poste>();

            using (var conn = _sqliteService.CreateConnection())
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = @"
                    SELECT 
                        pos.Id, pos.Code, pos.Intitule, pos.UniteId, pos.SuperieurPosteId,
                        pos.TitulaireMatricule, pos.Classification, pos.Missions,
                        pos.EffectifRequis, pos.Actif, pos.Ordre, pos.DateCreation,
                        u.Libelle AS UniteLibelle,
                        sup.Intitule AS SuperieurPosteIntitule,
                        e.FirstName || ' ' || e.LastName AS TitulaireNom
                    FROM Postes pos
                    LEFT JOIN UnitesOrganisationnelles u ON pos.UniteId = u.Id
                    LEFT JOIN Postes sup ON pos.SuperieurPosteId = sup.Id
                    LEFT JOIN Employees e ON pos.TitulaireMatricule = e.EmployeeId
                    ORDER BY pos.Ordre ASC, pos.Intitule ASC;";

                using (var reader = (SQLiteDataReader)await cmd.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                    {
                        var p = new Poste
                        {
                            Id = DbValue.IntFromDb(reader["Id"]),
                            Code = DbValue.TextFromDb(reader["Code"]),
                            Intitule = DbValue.TextFromDb(reader["Intitule"]),
                            UniteId = DbValue.IntNullableFromDb(reader["UniteId"]),
                            UniteLibelle = DbValue.TextFromDb(reader["UniteLibelle"]),
                            SuperieurPosteId = DbValue.IntNullableFromDb(reader["SuperieurPosteId"]),
                            SuperieurPosteIntitule = DbValue.TextFromDb(reader["SuperieurPosteIntitule"]),
                            TitulaireMatricule = DbValue.TextFromDb(reader["TitulaireMatricule"]),
                            TitulaireNom = DbValue.TextFromDb(reader["TitulaireNom"]),
                            Classification = DbValue.TextFromDb(reader["Classification"]),
                            Missions = DbValue.TextFromDb(reader["Missions"]),
                            EffectifRequis = Math.Max(1, DbValue.IntFromDb(reader["EffectifRequis"])),
                            Actif = DbValue.IntFromDb(reader["Actif"]) == 1,
                            Ordre = DbValue.IntFromDb(reader["Ordre"]),
                            DateCreation = DbValue.DateFromDb(reader["DateCreation"], DateTime.Now)
                        };
                        postes.Add(p);
                    }
                }
            }

            return postes;
        }

        /// <summary>
        /// Enregistre un poste (création ou mise à jour).
        /// Contrôle l'unicité du code et l'absence de cycle hiérarchique de poste à poste.
        /// Met à jour la fiche salarié si un titulaire est assigné.
        /// </summary>
        public async Task EnregistrerPosteAsync(Poste poste)
        {
            if (poste == null) throw new ArgumentNullException(nameof(poste));
            if (string.IsNullOrWhiteSpace(poste.Code)) throw new InvalidOperationException("Le code du poste est obligatoire.");
            if (string.IsNullOrWhiteSpace(poste.Intitule)) throw new InvalidOperationException("L'intitulé du poste est obligatoire.");

            poste.Code = poste.Code.Trim().ToUpperInvariant();
            poste.Intitule = poste.Intitule.Trim();

            using (var conn = _sqliteService.CreateConnection())
            using (var tx = conn.BeginTransaction())
            {
                // Unicité du code poste
                using (var cmd = conn.CreateCommand())
                {
                    cmd.Transaction = tx;
                    cmd.CommandText = "SELECT Id FROM Postes WHERE Code = @Code AND Id <> @Id;";
                    cmd.Parameters.AddWithValue("@Code", poste.Code);
                    cmd.Parameters.AddWithValue("@Id", poste.Id);
                    var existant = await cmd.ExecuteScalarAsync();
                    if (existant != null && existant != DBNull.Value)
                    {
                        throw new InvalidOperationException($"Le code de poste '{poste.Code}' est déjà utilisé.");
                    }
                }

                // Contrôle de cycle hiérarchique N+1
                if (poste.Id > 0 && poste.SuperieurPosteId.HasValue)
                {
                    if (poste.SuperieurPosteId.Value == poste.Id)
                        throw new InvalidOperationException("Un poste ne peut pas être son propre supérieur hiérarchique.");

                    if (await EstDescendantPosteAsync(conn, tx, poste.Id, poste.SuperieurPosteId.Value))
                    {
                        throw new InvalidOperationException("Impossible de définir ce supérieur : le poste sélectionné dépend déjà de ce poste dans la ligne hiérarchique (cycle).");
                    }
                }

                if (poste.Id == 0)
                {
                    // Insertion
                    using (var cmd = conn.CreateCommand())
                    {
                        cmd.Transaction = tx;
                        cmd.CommandText = @"
                            INSERT INTO Postes
                            (Code, Intitule, UniteId, SuperieurPosteId, TitulaireMatricule, Classification, Missions, EffectifRequis, Actif, Ordre, DateCreation)
                            VALUES
                            (@Code, @Intitule, @UniteId, @SuperieurPosteId, @TitulaireMatricule, @Classification, @Missions, @EffectifRequis, @Actif, @Ordre, @DateCreation);
                            SELECT last_insert_rowid();";

                        cmd.Parameters.AddWithValue("@Code", poste.Code);
                        cmd.Parameters.AddWithValue("@Intitule", poste.Intitule);
                        cmd.Parameters.AddWithValue("@UniteId", poste.UniteId.HasValue ? (object)poste.UniteId.Value : DBNull.Value);
                        cmd.Parameters.AddWithValue("@SuperieurPosteId", poste.SuperieurPosteId.HasValue ? (object)poste.SuperieurPosteId.Value : DBNull.Value);
                        cmd.Parameters.AddWithValue("@TitulaireMatricule", (object)poste.TitulaireMatricule ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@Classification", (object)poste.Classification ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@Missions", (object)poste.Missions ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@EffectifRequis", Math.Max(1, poste.EffectifRequis));
                        cmd.Parameters.AddWithValue("@Actif", poste.Actif ? 1 : 0);
                        cmd.Parameters.AddWithValue("@Ordre", poste.Ordre);
                        cmd.Parameters.AddWithValue("@DateCreation", DbValue.DateToDb(DateTime.Now));

                        var newId = await cmd.ExecuteScalarAsync();
                        poste.Id = Convert.ToInt32(newId);
                    }
                }
                else
                {
                    // Mise à jour
                    using (var cmd = conn.CreateCommand())
                    {
                        cmd.Transaction = tx;
                        cmd.CommandText = @"
                            UPDATE Postes
                            SET Code = @Code,
                                Intitule = @Intitule,
                                UniteId = @UniteId,
                                SuperieurPosteId = @SuperieurPosteId,
                                TitulaireMatricule = @TitulaireMatricule,
                                Classification = @Classification,
                                Missions = @Missions,
                                EffectifRequis = @EffectifRequis,
                                Actif = @Actif,
                                Ordre = @Ordre
                            WHERE Id = @Id;";

                        cmd.Parameters.AddWithValue("@Code", poste.Code);
                        cmd.Parameters.AddWithValue("@Intitule", poste.Intitule);
                        cmd.Parameters.AddWithValue("@UniteId", poste.UniteId.HasValue ? (object)poste.UniteId.Value : DBNull.Value);
                        cmd.Parameters.AddWithValue("@SuperieurPosteId", poste.SuperieurPosteId.HasValue ? (object)poste.SuperieurPosteId.Value : DBNull.Value);
                        cmd.Parameters.AddWithValue("@TitulaireMatricule", (object)poste.TitulaireMatricule ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@Classification", (object)poste.Classification ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@Missions", (object)poste.Missions ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@EffectifRequis", Math.Max(1, poste.EffectifRequis));
                        cmd.Parameters.AddWithValue("@Actif", poste.Actif ? 1 : 0);
                        cmd.Parameters.AddWithValue("@Ordre", poste.Ordre);
                        cmd.Parameters.AddWithValue("@Id", poste.Id);

                        await cmd.ExecuteNonQueryAsync();
                    }
                }

                // Si un titulaire est désigné, synchroniser sur sa fiche employé (PosteId et Fonction)
                if (!string.IsNullOrWhiteSpace(poste.TitulaireMatricule))
                {
                    using (var cmd = conn.CreateCommand())
                    {
                        cmd.Transaction = tx;
                        cmd.CommandText = "UPDATE Employees SET PosteId = @PosteId, Fonction = @Fonction WHERE EmployeeId = @EmployeeId;";
                        cmd.Parameters.AddWithValue("@PosteId", poste.Id);
                        cmd.Parameters.AddWithValue("@Fonction", poste.Intitule);
                        cmd.Parameters.AddWithValue("@EmployeeId", poste.TitulaireMatricule);
                        await cmd.ExecuteNonQueryAsync();
                    }
                }

                tx.Commit();
            }
        }

        private async Task<bool> EstDescendantPosteAsync(SQLiteConnection conn, SQLiteTransaction tx, int idPosteParent, int idPosteCandidat)
        {
            int? courantId = idPosteCandidat;
            var visites = new HashSet<int>();

            while (courantId.HasValue)
            {
                if (courantId.Value == idPosteParent) return true;
                if (!visites.Add(courantId.Value)) break;

                using (var cmd = conn.CreateCommand())
                {
                    cmd.Transaction = tx;
                    cmd.CommandText = "SELECT SuperieurPosteId FROM Postes WHERE Id = @Id;";
                    cmd.Parameters.AddWithValue("@Id", courantId.Value);
                    var supObj = await cmd.ExecuteScalarAsync();
                    courantId = DbValue.IntNullableFromDb(supObj);
                }
            }
            return false;
        }

        /// <summary>
        /// Supprime un poste de travail.
        /// Détache le poste des salariés et réaligne les subordonnés sur le niveau supérieur.
        /// </summary>
        public async Task SupprimerPosteAsync(int posteId)
        {
            if (posteId <= 0) return;

            using (var conn = _sqliteService.CreateConnection())
            using (var tx = conn.BeginTransaction())
            {
                // Obtenir le supérieur du poste supprimé
                int? supId = null;
                using (var cmd = conn.CreateCommand())
                {
                    cmd.Transaction = tx;
                    cmd.CommandText = "SELECT SuperieurPosteId FROM Postes WHERE Id = @Id;";
                    cmd.Parameters.AddWithValue("@Id", posteId);
                    supId = DbValue.IntNullableFromDb(await cmd.ExecuteScalarAsync());
                }

                // Raccrocher les subordonnés au supérieur
                using (var cmd = conn.CreateCommand())
                {
                    cmd.Transaction = tx;
                    cmd.CommandText = "UPDATE Postes SET SuperieurPosteId = @SupId WHERE SuperieurPosteId = @Id;";
                    cmd.Parameters.AddWithValue("@SupId", supId.HasValue ? (object)supId.Value : DBNull.Value);
                    cmd.Parameters.AddWithValue("@Id", posteId);
                    await cmd.ExecuteNonQueryAsync();
                }

                // Détacher les employés rattachés à ce poste
                using (var cmd = conn.CreateCommand())
                {
                    cmd.Transaction = tx;
                    cmd.CommandText = "UPDATE Employees SET PosteId = NULL WHERE PosteId = @Id;";
                    cmd.Parameters.AddWithValue("@Id", posteId);
                    await cmd.ExecuteNonQueryAsync();
                }

                // Supprimer le poste
                using (var cmd = conn.CreateCommand())
                {
                    cmd.Transaction = tx;
                    cmd.CommandText = "DELETE FROM Postes WHERE Id = @Id;";
                    cmd.Parameters.AddWithValue("@Id", posteId);
                    await cmd.ExecuteNonQueryAsync();
                }

                tx.Commit();
            }
        }

        #endregion

        #region Affectations et Salariés

        /// <summary>
        /// Charge l'ensemble des salariés avec leurs affectations organisationnelles actuelles.
        /// </summary>
        public async Task<List<AffectationSalarieLigne>> ChargerAffectationsSalariesAsync()
        {
            var lignes = new List<AffectationSalarieLigne>();

            using (var conn = _sqliteService.CreateConnection())
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = @"
                    SELECT 
                        e.EmployeeId, e.FirstName, e.LastName, e.Category, e.Service,
                        e.PosteId, e.ManagerMatricule, e.StatutSalarie,
                        pos.Intitule AS PosteLibelle,
                        m.FirstName || ' ' || m.LastName AS ManagerNom
                    FROM Employees e
                    LEFT JOIN Postes pos ON e.PosteId = pos.Id
                    LEFT JOIN Employees m ON e.ManagerMatricule = m.EmployeeId
                    ORDER BY e.StatutSalarie ASC, e.LastName ASC, e.FirstName ASC;";

                using (var reader = (SQLiteDataReader)await cmd.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                    {
                        var matricule = DbValue.TextFromDb(reader["EmployeeId"]);
                        var prenom = DbValue.TextFromDb(reader["FirstName"]);
                        var nom = DbValue.TextFromDb(reader["LastName"]);

                        var ligne = new AffectationSalarieLigne
                        {
                            Matricule = matricule,
                            NomComplet = string.Format("{0} {1}", prenom, nom).Trim(),
                            Category = DbValue.TextFromDb(reader["Category"]),
                            ServiceActuel = DbValue.TextFromDb(reader["Service"]),
                            PosteActuelId = DbValue.IntNullableFromDb(reader["PosteId"]),
                            PosteActuelLibelle = DbValue.TextFromDb(reader["PosteLibelle"]),
                            ManagerActuelMatricule = DbValue.TextFromDb(reader["ManagerMatricule"]),
                            ManagerActuelNom = DbValue.TextFromDb(reader["ManagerNom"]),
                            StatutSalarie = DbValue.IntFromDb(reader["StatutSalarie"])
                        };
                        lignes.Add(ligne);
                    }
                }
            }

            return lignes;
        }

        /// <summary>
        /// Affecte un salarié à un service, un poste et/ou un responsable direct N+1.
        /// </summary>
        public async Task AffecterSalarieAsync(string matricule, string serviceNom, int? posteId, string managerMatricule)
        {
            if (string.IsNullOrWhiteSpace(matricule))
                throw new ArgumentException("Matricule salarié manquant.", nameof(matricule));

            using (var conn = _sqliteService.CreateConnection())
            using (var tx = conn.BeginTransaction())
            {
                // Obtenir le titre du poste si posteId est fourni
                string posteIntitule = null;
                if (posteId.HasValue)
                {
                    using (var cmd = conn.CreateCommand())
                    {
                        cmd.Transaction = tx;
                        cmd.CommandText = "SELECT Intitule FROM Postes WHERE Id = @Id;";
                        cmd.Parameters.AddWithValue("@Id", posteId.Value);
                        posteIntitule = DbValue.TextFromDb(await cmd.ExecuteScalarAsync());
                    }
                }

                // Mettre à jour la fiche de l'employé
                using (var cmd = conn.CreateCommand())
                {
                    cmd.Transaction = tx;
                    cmd.CommandText = @"
                        UPDATE Employees
                        SET Service = @Service,
                            PosteId = @PosteId,
                            Fonction = COALESCE(@Fonction, Fonction),
                            ManagerMatricule = @ManagerMatricule
                        WHERE EmployeeId = @EmployeeId;";

                    cmd.Parameters.AddWithValue("@Service", (object)serviceNom ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@PosteId", posteId.HasValue ? (object)posteId.Value : DBNull.Value);
                    cmd.Parameters.AddWithValue("@Fonction", (object)posteIntitule ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@ManagerMatricule", (object)managerMatricule ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@EmployeeId", matricule);

                    await cmd.ExecuteNonQueryAsync();
                }

                // Mettre à jour le poste pour indiquer ce titulaire
                if (posteId.HasValue)
                {
                    using (var cmd = conn.CreateCommand())
                    {
                        cmd.Transaction = tx;
                        cmd.CommandText = "UPDATE Postes SET TitulaireMatricule = @Matricule WHERE Id = @Id;";
                        cmd.Parameters.AddWithValue("@Matricule", matricule);
                        cmd.Parameters.AddWithValue("@Id", posteId.Value);
                        await cmd.ExecuteNonQueryAsync();
                    }
                }

                tx.Commit();
            }
        }

        #endregion

        #region Construction de l'Arborescence Graphique

        /// <summary>
        /// Construit l'arborescence graphique de l'organigramme (racines -> nœuds enfants).
        /// Inclut les unités et optionnellement les postes rattachés.
        /// </summary>
        public async Task<ObservableCollection<NoeudOrganigramme>> ChargerArbreOrganigrammeAsync(bool inclurePostes = true)
        {
            var unites = await ChargerUnitesAsync();
            var postes = inclurePostes ? await ChargerPostesAsync() : new List<Poste>();
            var racines = new ObservableCollection<NoeudOrganigramme>();

            if (unites.Count == 0 && postes.Count == 0)
                return racines;

            // Dictionnaire des unités
            var dicNoeudsUnites = new Dictionary<int, NoeudOrganigramme>();
            foreach (var u in unites)
            {
                string initiale = "?";
                if (!string.IsNullOrWhiteSpace(u.ResponsableNom))
                {
                    var parts = u.ResponsableNom.Trim().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    initiale = parts.Length == 1
                        ? (parts[0].Length >= 2 ? parts[0].Substring(0, 2).ToUpperInvariant() : parts[0].ToUpperInvariant())
                        : (parts[0][0].ToString() + parts[parts.Length - 1][0].ToString()).ToUpperInvariant();
                }

                var noeud = new NoeudOrganigramme
                {
                    Id = u.Id,
                    Code = u.Code,
                    Titre = u.Libelle,
                    SousTitre = u.BadgeTypeLibelle,
                    TypeNoeud = "Unite",
                    TypeUnite = u.TypeUnite,
                    BadgeType = u.BadgeTypeLibelle,
                    ResponsableOuTitulaire = u.ResponsableNom,
                    Matricule = u.ResponsableMatricule,
                    InitialeAvatar = initiale,
                    EffectifDirect = u.NombreSalariesDirects,
                    EffectifTotal = u.NombreSalariesTotal,
                    NombrePostes = u.NombrePostes,
                    CouleurEntete = string.IsNullOrWhiteSpace(u.Couleur) ? "#14675A" : u.Couleur,
                    IsExpanded = true,
                    UniteAssociee = u
                };
                dicNoeudsUnites[u.Id] = noeud;
            }

            // Attacher les postes aux unités correspondantes (ou à part)
            var postesParUnite = postes.Where(p => p.UniteId.HasValue).GroupBy(p => p.UniteId.Value).ToDictionary(g => g.Key, g => g.ToList());

            foreach (var kvp in postesParUnite)
            {
                if (dicNoeudsUnites.TryGetValue(kvp.Key, out var noeudUnite))
                {
                    foreach (var p in kvp.Value)
                    {
                        var noeudPoste = new NoeudOrganigramme
                        {
                            Id = p.Id,
                            Code = p.Code,
                            Titre = p.Intitule,
                            SousTitre = string.IsNullOrWhiteSpace(p.Classification) ? "Poste" : p.Classification,
                            TypeNoeud = "Poste",
                            BadgeType = p.StatutOccupation,
                            ResponsableOuTitulaire = p.TitulaireNom,
                            Matricule = p.TitulaireMatricule,
                            InitialeAvatar = p.InitialesTitulaire,
                            EffectifDirect = p.EstPourvu ? 1 : 0,
                            EffectifTotal = p.EffectifRequis,
                            NombrePostes = 1,
                            CouleurEntete = p.EstPourvu ? "#2A5B84" : "#9C2F28",
                            IsExpanded = false,
                            PosteAssocie = p
                        };
                        noeudUnite.Enfants.Add(noeudPoste);
                    }
                }
            }

            // Établir la hiérarchie des unités
            foreach (var u in unites)
            {
                var noeud = dicNoeudsUnites[u.Id];
                if (u.ParentId.HasValue && dicNoeudsUnites.TryGetValue(u.ParentId.Value, out var noeudParent))
                {
                    noeud.Niveau = noeudParent.Niveau + 1;
                    noeudParent.Enfants.Insert(0, noeud); // Mettre les sous-unités avant les postes
                }
                else
                {
                    noeud.Niveau = 0;
                    racines.Add(noeud);
                }
            }

            return racines;
        }

        #endregion

        #region Initialisation et Découverte Automatique

        /// <summary>
        /// Initialise une structure d'organigramme par défaut.
        /// Si des fiches salariés portent déjà des services distincts (PIL-01), crée les unités correspondantes.
        /// Sinon, génère une ossature standard d'entreprise burkinabè (DG, DAF, DRH, DTECH, DCOM).
        /// </summary>
        public async Task InitialiserStructureDefautAsync()
        {
            var unitesExistantes = await ChargerUnitesAsync();
            if (unitesExistantes.Count > 0) return;

            // Vérifier si des services distincts existent dans la table Employees
            var servicesPresents = new List<string>();
            using (var conn = _sqliteService.CreateConnection())
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "SELECT DISTINCT Service FROM Employees WHERE Service IS NOT NULL AND TRIM(Service) <> '' ORDER BY Service ASC;";
                using (var reader = (SQLiteDataReader)await cmd.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                    {
                        var s = DbValue.TextFromDb(reader["Service"]);
                        if (!string.IsNullOrWhiteSpace(s)) servicesPresents.Add(s);
                    }
                }
            }

            if (servicesPresents.Count > 0)
            {
                // Créer une Direction Générale racine
                var dg = new UniteOrganisationnelle
                {
                    Code = "DG",
                    Libelle = "Direction Générale",
                    TypeUnite = TypeUniteOrganisationnelle.DirectionGenerale,
                    Couleur = "#0A3E35",
                    Ordre = 1
                };
                await EnregistrerUniteAsync(dg);

                // Créer chaque service rattaché à la DG
                int ordre = 10;
                foreach (var srv in servicesPresents)
                {
                    string code = GenererCodeCourt(srv);
                    var u = new UniteOrganisationnelle
                    {
                        Code = code,
                        Libelle = srv,
                        TypeUnite = TypeUniteOrganisationnelle.Service,
                        ParentId = dg.Id,
                        Couleur = "#14675A",
                        Ordre = ordre++
                    };
                    await EnregistrerUniteAsync(u);
                }
            }
            else
            {
                // Structure standard type burkinabè
                var dg = new UniteOrganisationnelle { Code = "DG", Libelle = "Direction Générale", TypeUnite = TypeUniteOrganisationnelle.DirectionGenerale, Couleur = "#0A3E35", Ordre = 1 };
                await EnregistrerUniteAsync(dg);

                var daf = new UniteOrganisationnelle { Code = "DAF", Libelle = "Direction Administrative et Financière", TypeUnite = TypeUniteOrganisationnelle.Direction, ParentId = dg.Id, Couleur = "#1E40AF", Ordre = 2 };
                await EnregistrerUniteAsync(daf);

                var drh = new UniteOrganisationnelle { Code = "DRH", Libelle = "Direction des Ressources Humaines", TypeUnite = TypeUniteOrganisationnelle.Direction, ParentId = dg.Id, Couleur = "#14675A", Ordre = 3 };
                await EnregistrerUniteAsync(drh);

                var dtech = new UniteOrganisationnelle { Code = "DTECH", Libelle = "Direction Technique & Exploitation", TypeUnite = TypeUniteOrganisationnelle.Direction, ParentId = dg.Id, Couleur = "#B45309", Ordre = 4 };
                await EnregistrerUniteAsync(dtech);

                var dcom = new UniteOrganisationnelle { Code = "DCOM", Libelle = "Direction Commerciale & Marketing", TypeUnite = TypeUniteOrganisationnelle.Direction, ParentId = dg.Id, Couleur = "#7C3AED", Ordre = 5 };
                await EnregistrerUniteAsync(dcom);

                // Services sous DAF et DRH
                await EnregistrerUniteAsync(new UniteOrganisationnelle { Code = "SRV-COMPTA", Libelle = "Service Comptabilité & Trésorerie", TypeUnite = TypeUniteOrganisationnelle.Service, ParentId = daf.Id, Couleur = "#2563EB", Ordre = 10 });
                await EnregistrerUniteAsync(new UniteOrganisationnelle { Code = "SRV-PAIE", Libelle = "Service Paie & Administration", TypeUnite = TypeUniteOrganisationnelle.Service, ParentId = drh.Id, Couleur = "#059669", Ordre = 20 });
                await EnregistrerUniteAsync(new UniteOrganisationnelle { Code = "SRV-REC", Libelle = "Service Recrutement & Formation", TypeUnite = TypeUniteOrganisationnelle.Service, ParentId = drh.Id, Couleur = "#10B981", Ordre = 21 });
                await EnregistrerUniteAsync(new UniteOrganisationnelle { Code = "SRV-INFO", Libelle = "Service Informatique & Systèmes", TypeUnite = TypeUniteOrganisationnelle.Service, ParentId = dtech.Id, Couleur = "#D97706", Ordre = 30 });

                // Postes clés types
                var pDG = new Poste { Code = "POSTE-DG", Intitule = "Directeur Général", UniteId = dg.Id, Classification = "Cadre Dirigeant", Missions = "Direction stratégique et générale de l'entreprise", EffectifRequis = 1, Ordre = 1 };
                await EnregistrerPosteAsync(pDG);

                var pDAF = new Poste { Code = "POSTE-DAF", Intitule = "Directeur Administratif et Financier", UniteId = daf.Id, SuperieurPosteId = pDG.Id, Classification = "Cadre Supérieur", Missions = "Gestion financière, comptable et trésorerie", EffectifRequis = 1, Ordre = 2 };
                await EnregistrerPosteAsync(pDAF);

                var pDRH = new Poste { Code = "POSTE-DRH", Intitule = "Directeur des Ressources Humaines", UniteId = drh.Id, SuperieurPosteId = pDG.Id, Classification = "Cadre Supérieur", Missions = "Politique RH, paie, dialogue social", EffectifRequis = 1, Ordre = 3 };
                await EnregistrerPosteAsync(pDRH);

                var pPaie = new Poste { Code = "POSTE-PAIE", Intitule = "Responsable Paie & RH", UniteId = drh.Id, SuperieurPosteId = pDRH.Id, Classification = "Cadre Moyen", Missions = "Gestion de la paie, déclarations CNSS et IUTS", EffectifRequis = 1, Ordre = 4 };
                await EnregistrerPosteAsync(pPaie);
            }
        }

        private static string GenererCodeCourt(string libelle)
        {
            if (string.IsNullOrWhiteSpace(libelle)) return "SRV";

            var clean = libelle.Trim().ToUpperInvariant();
            var mots = clean.Split(new[] { ' ', '-', '_', '/' }, StringSplitOptions.RemoveEmptyEntries);
            if (mots.Length == 1)
            {
                return mots[0].Length <= 8 ? mots[0] : mots[0].Substring(0, 6);
            }

            var code = string.Join("", mots.Where(m => m.Length > 2).Select(m => m[0]));
            if (code.Length < 3) code = mots[0].Substring(0, Math.Min(3, mots[0].Length));
            return ("SRV-" + code).Substring(0, Math.Min(12, ("SRV-" + code).Length));
        }

        #endregion
    }
}
