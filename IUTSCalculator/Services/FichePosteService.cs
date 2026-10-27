using System;
using System.Collections.Generic;
using Microsoft.Data.Sqlite;
using System.Threading.Tasks;
using IUTSCalculator.Models;

namespace IUTSCalculator.Services
{
    /// <summary>
    /// Service de gestion des fiches de poste, des compétences et du suivi budgétaire GPEC (module RH-02).
    /// </summary>
    public class FichePosteService
    {
        private readonly SQLiteService _sqliteService;

        public FichePosteService(SQLiteService sqliteService)
        {
            _sqliteService = sqliteService ?? throw new ArgumentNullException(nameof(sqliteService));
        }

        #region Catalogue des Compétences

        /// <summary>
        /// Charge toutes les compétences de la bibliothèque d'entreprise.
        /// </summary>
        public async Task<List<Competence>> ChargerToutesCompetencesAsync()
        {
            var liste = new List<Competence>();

            using (var conn = _sqliteService.CreerConnexion())
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = @"
                    SELECT Id, Code, Libelle, Domaine, Description, DateCreation
                    FROM Competences
                    ORDER BY Domaine ASC, Libelle ASC;";

                using (var reader = (SqliteDataReader)await cmd.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                    {
                        liste.Add(new Competence
                        {
                            Id = DbValue.IntFromDb(reader["Id"]),
                            Code = DbValue.TextFromDb(reader["Code"]),
                            Libelle = DbValue.TextFromDb(reader["Libelle"]),
                            Domaine = (DomaineCompetence)DbValue.IntFromDb(reader["Domaine"]),
                            Description = DbValue.TextFromDb(reader["Description"]),
                            DateCreation = DbValue.DateFromDb(reader["DateCreation"], DateTime.Now)
                        });
                    }
                }
            }

            return liste;
        }

        /// <summary>
        /// Enregistre une compétence (création ou modification).
        /// </summary>
        public async Task EnregistrerCompetenceAsync(Competence comp)
        {
            if (comp == null) throw new ArgumentNullException(nameof(comp));
            if (string.IsNullOrWhiteSpace(comp.Code)) throw new InvalidOperationException("Le code de compétence est obligatoire.");
            if (string.IsNullOrWhiteSpace(comp.Libelle)) throw new InvalidOperationException("Le libellé de compétence est obligatoire.");

            comp.Code = comp.Code.Trim().ToUpperInvariant();
            comp.Libelle = comp.Libelle.Trim();

            using (var conn = _sqliteService.CreerConnexion())
            {
                // Vérifier l'unicité du code
                using (var cmdCheck = conn.CreateCommand())
                {
                    cmdCheck.CommandText = "SELECT Id FROM Competences WHERE Code = @Code AND Id <> @Id;";
                    cmdCheck.Parameters.AjouterValeur("@Code", comp.Code);
                    cmdCheck.Parameters.AjouterValeur("@Id", comp.Id);
                    var existant = await cmdCheck.ExecuteScalarAsync();
                    if (existant != null && existant != DBNull.Value)
                    {
                        throw new InvalidOperationException($"Le code de compétence '{comp.Code}' est déjà utilisé.");
                    }
                }

                using (var cmd = conn.CreateCommand())
                {
                    if (comp.Id == 0)
                    {
                        cmd.CommandText = @"
                            INSERT INTO Competences (Code, Libelle, Domaine, Description, DateCreation)
                            VALUES (@Code, @Libelle, @Domaine, @Description, @DateCreation);
                            SELECT last_insert_rowid();";
                        cmd.Parameters.AjouterValeur("@DateCreation", DbValue.DateToDb(DateTime.Now));
                    }
                    else
                    {
                        cmd.CommandText = @"
                            UPDATE Competences
                            SET Code = @Code, Libelle = @Libelle, Domaine = @Domaine, Description = @Description
                            WHERE Id = @Id;";
                        cmd.Parameters.AjouterValeur("@Id", comp.Id);
                    }

                    cmd.Parameters.AjouterValeur("@Code", comp.Code);
                    cmd.Parameters.AjouterValeur("@Libelle", comp.Libelle);
                    cmd.Parameters.AjouterValeur("@Domaine", (int)comp.Domaine);
                    cmd.Parameters.AjouterValeur("@Description", (object)comp.Description ?? DBNull.Value);

                    if (comp.Id == 0)
                    {
                        var newId = await cmd.ExecuteScalarAsync();
                        comp.Id = Convert.ToInt32(newId);
                    }
                    else
                    {
                        await cmd.ExecuteNonQueryAsync();
                    }
                }
            }
        }

        /// <summary>
        /// Supprime une compétence du catalogue ainsi que ses liaisons dans les fiches de poste.
        /// </summary>
        public async Task SupprimerCompetenceAsync(int competenceId)
        {
            if (competenceId <= 0) return;

            using (var conn = _sqliteService.CreerConnexion())
            using (var tx = conn.BeginTransaction())
            {
                using (var cmdLiaisons = conn.CreateCommand())
                {
                    cmdLiaisons.Transaction = tx;
                    cmdLiaisons.CommandText = "DELETE FROM PosteCompetences WHERE CompetenceId = @Id;";
                    cmdLiaisons.Parameters.AjouterValeur("@Id", competenceId);
                    await cmdLiaisons.ExecuteNonQueryAsync();
                }

                using (var cmdComp = conn.CreateCommand())
                {
                    cmdComp.Transaction = tx;
                    cmdComp.CommandText = "DELETE FROM Competences WHERE Id = @Id;";
                    cmdComp.Parameters.AjouterValeur("@Id", competenceId);
                    await cmdComp.ExecuteNonQueryAsync();
                }

                tx.Commit();
            }
        }

        #endregion

        #region Suivi Budgétaire GPEC & Synthèse Effectifs

        /// <summary>
        /// Calcule la synthèse budgétaire complète : comparaison effectif budgété vs pourvu par poste.
        /// </summary>
        public async Task<List<SynthesePosteBudget>> CalculerSyntheseBudgetaireEffectifsAsync()
        {
            var syntheses = new List<SynthesePosteBudget>();

            using (var conn = _sqliteService.CreerConnexion())
            {
                // 1. Lire les postes et décompter les effectifs pourvus
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = @"
                        SELECT 
                            pos.Id AS PosteId, pos.Code, pos.Intitule,
                            pos.EffectifRequis AS EffectifBudgete,
                            pos.SalaireMinBudgete, pos.SalaireMaxBudgete, pos.SalaireMinimumConventionnel,
                            pos.CategorieConventionnelle, pos.EchelonConventionnel,
                            u.Libelle AS UniteLibelle,
                            sup.Intitule AS SuperieurIntitule,
                            COUNT(CASE WHEN e.StatutSalarie = 0 THEN 1 END) AS EffectifPourvu
                        FROM Postes pos
                        LEFT JOIN UnitesOrganisationnelles u ON pos.UniteId = u.Id
                        LEFT JOIN Postes sup ON pos.SuperieurPosteId = sup.Id
                        LEFT JOIN Employees e ON e.PosteId = pos.Id
                        WHERE pos.Actif = 1
                        GROUP BY pos.Id
                        ORDER BY u.Libelle ASC, pos.Intitule ASC;";

                    using (var reader = (SqliteDataReader)await cmd.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            syntheses.Add(new SynthesePosteBudget
                            {
                                PosteId = DbValue.IntFromDb(reader["PosteId"]),
                                Code = DbValue.TextFromDb(reader["Code"]),
                                Intitule = DbValue.TextFromDb(reader["Intitule"]),
                                UniteLibelle = DbValue.TextFromDb(reader["UniteLibelle"]) ?? "(Non rattaché)",
                                SuperieurIntitule = DbValue.TextFromDb(reader["SuperieurIntitule"]) ?? "(Direction / Aucun)",
                                EffectifBudgete = Math.Max(1, DbValue.IntFromDb(reader["EffectifBudgete"])),
                                EffectifPourvu = DbValue.IntFromDb(reader["EffectifPourvu"]),
                                SalaireMinBudgete = DbValue.MoneyFromDb(reader["SalaireMinBudgete"]),
                                SalaireMaxBudgete = DbValue.MoneyFromDb(reader["SalaireMaxBudgete"]),
                                SalaireMinConventionnel = DbValue.MoneyFromDb(reader["SalaireMinimumConventionnel"]),
                                CategorieConventionnelle = DbValue.TextFromDb(reader["CategorieConventionnelle"]),
                                EchelonConventionnel = DbValue.TextFromDb(reader["EchelonConventionnel"])
                            });
                        }
                    }
                }

                // 2. Charger les salariés titulaires pour chaque poste
                foreach (var syn in syntheses)
                {
                    using (var cmdTitulaires = conn.CreateCommand())
                    {
                        cmdTitulaires.CommandText = @"
                            SELECT EmployeeId, FirstName, LastName, Service, Category, Echelon, DateEntree, StatutSalarie
                            FROM Employees
                            WHERE PosteId = @PosteId AND StatutSalarie = 0
                            ORDER BY LastName ASC, FirstName ASC;";
                        cmdTitulaires.Parameters.AjouterValeur("@PosteId", syn.PosteId);

                        using (var reader = (SqliteDataReader)await cmdTitulaires.ExecuteReaderAsync())
                        {
                            while (await reader.ReadAsync())
                            {
                                var dateEntree = DbValue.DateNullableFromDb(reader["DateEntree"]);
                                string anciennete = "-";
                                if (dateEntree.HasValue)
                                {
                                    var annees = DateTime.Today.Year - dateEntree.Value.Year;
                                    if (DateTime.Today < dateEntree.Value.AddYears(annees)) annees--;
                                    anciennete = annees > 0 ? $"{annees} an(s)" : "< 1 an";
                                }

                                syn.Titulaires.Add(new TitulairePosteLigne
                                {
                                    EmployeeId = DbValue.TextFromDb(reader["EmployeeId"]),
                                    NomComplet = $"{DbValue.TextFromDb(reader["FirstName"])} {DbValue.TextFromDb(reader["LastName"])}".Trim(),
                                    Department = DbValue.TextFromDb(reader["Service"]),
                                    Categorie = DbValue.TextFromDb(reader["Category"]),
                                    Echelon = DbValue.TextFromDb(reader["Echelon"]),
                                    DateEmbauche = dateEntree,
                                    AncienneteTexte = anciennete,
                                    StatutSalarie = DbValue.IntFromDb(reader["StatutSalarie"])
                                });
                            }
                        }
                    }
                }
            }

            return syntheses;
        }

        #endregion

        #region Rapprochement Conventionnel (SOC-05)

        /// <summary>
        /// Recherche le salaire minimum conventionnel garanti dans la grille salariale SOC-05
        /// pour une convention, une catégorie et un échelon donnés.
        /// </summary>
        public async Task<decimal> ObtenirSalaireMinConventionnelAsync(int? conventionId, string categorie, string echelon)
        {
            if (!conventionId.HasValue || string.IsNullOrWhiteSpace(categorie))
                return 0m;

            using (var conn = _sqliteService.CreerConnexion())
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = @"
                    SELECT SalaireMinimum
                    FROM GrillesSalaires
                    WHERE ConventionId = @ConvId 
                      AND UPPER(TRIM(Categorie)) = UPPER(TRIM(@Cat))
                      AND (UPPER(TRIM(Echelon)) = UPPER(TRIM(@Ech)) OR @Ech IS NULL OR TRIM(@Ech) = '')
                    ORDER BY SalaireMinimum DESC
                    LIMIT 1;";
                cmd.Parameters.AjouterValeur("@ConvId", conventionId.Value);
                cmd.Parameters.AjouterValeur("@Cat", categorie.Trim());
                cmd.Parameters.AjouterValeur("@Ech", (object)echelon?.Trim() ?? DBNull.Value);

                var obj = await cmd.ExecuteScalarAsync();
                if (obj != null && obj != DBNull.Value)
                {
                    return DbValue.MoneyFromDb(obj);
                }
            }

            return 0m;
        }

        #endregion
    }
}
