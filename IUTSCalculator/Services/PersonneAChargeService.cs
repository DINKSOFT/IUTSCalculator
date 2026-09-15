using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Linq;
using System.Threading.Tasks;
using IUTSCalculator.Models;

namespace IUTSCalculator.Services
{
    /// <summary>
    /// Personnes à charge nominatives d'un salarié (module SAL-01).
    ///
    /// Le service rend deux choses distinctes, et il importe de ne pas les confondre :
    ///
    ///   • Le RECENSEMENT — qui sont les personnes à charge, avec leur nom et leur date de
    ///     naissance. C'est la pièce justificative qui manquait devant un contrôle.
    ///
    ///   • Le DÉCOMPTE applicable à une date, obtenu en appliquant la règle d'âge. Ce
    ///     décompte est PROPOSÉ à l'utilisateur, jamais écrit d'autorité dans le champ
    ///     <c>FamilyCharges</c> du salarié : c'est ce champ-là qui commande la réduction
    ///     d'IUTS, et une situation familiale réelle ne se laisse pas toujours réduire à
    ///     une règle d'âge.
    /// </summary>
    public class PersonneAChargeService
    {
        private readonly SQLiteService _sqliteService;

        /// <summary>Plafond légal de la réduction pour charges de famille (art. 113 CGI).</summary>
        public const int PlafondCharges = 7;

        private const string Colonnes =
            "Id, EmployeeId, Nom, Prenom, Lien, DateNaissance, PoursuitDesEtudes, " +
            "SituationDeHandicap, CompteCommeCharge, Commentaire";

        public PersonneAChargeService(SQLiteService sqliteService)
        {
            if (sqliteService == null) throw new ArgumentNullException("sqliteService");
            _sqliteService = sqliteService;
        }

        /// <summary>Les personnes à charge d'un salarié, conjoint d'abord puis par âge.</summary>
        public async Task<List<PersonneACharge>> ChargerAsync(string employeeId)
        {
            var liste = new List<PersonneACharge>();
            if (string.IsNullOrWhiteSpace(employeeId)) return liste;

            using (var conn = _sqliteService.CreateConnection())
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText =
                    "SELECT " + Colonnes + " FROM PersonnesACharge WHERE EmployeeId = @id " +
                    "ORDER BY Lien, DateNaissance, Nom";
                cmd.Parameters.AddWithValue("@id", employeeId);

                using (var reader = (SQLiteDataReader)await cmd.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                        liste.Add(Lire(reader));
                }
            }

            return liste;
        }

        private static PersonneACharge Lire(SQLiteDataReader reader)
        {
            return new PersonneACharge
            {
                Id = DbValue.IntFromDb(reader["Id"]),
                EmployeeId = DbValue.TextFromDb(reader["EmployeeId"]),
                Nom = DbValue.TextFromDb(reader["Nom"]),
                Prenom = DbValue.TextFromDb(reader["Prenom"]),
                Lien = (LienParente)DbValue.IntFromDb(reader["Lien"]),
                DateNaissance = DbValue.DateNullableFromDb(reader["DateNaissance"]),
                PoursuitDesEtudes = DbValue.IntFromDb(reader["PoursuitDesEtudes"]) != 0,
                SituationDeHandicap = DbValue.IntFromDb(reader["SituationDeHandicap"]) != 0,
                CompteCommeCharge = DbValue.IntFromDb(reader["CompteCommeCharge"]) != 0,
                Commentaire = DbValue.TextFromDb(reader["Commentaire"])
            };
        }

        /// <summary>
        /// Remplace en bloc les personnes à charge d'un salarié. Le tout dans une seule
        /// transaction : une liste à moitié écrite donnerait un décompte faux.
        /// </summary>
        public async Task EnregistrerToutAsync(string employeeId, IEnumerable<PersonneACharge> personnes)
        {
            if (string.IsNullOrWhiteSpace(employeeId))
                throw new ArgumentException("Le salarié doit être enregistré avant ses personnes à charge.", "employeeId");

            var liste = (personnes ?? Enumerable.Empty<PersonneACharge>())
                .Where(p => p != null && !string.IsNullOrWhiteSpace(p.NomComplet))
                .ToList();

            using (var conn = _sqliteService.CreateConnection())
            using (var tx = conn.BeginTransaction())
            {
                try
                {
                    using (var cmd = conn.CreateCommand())
                    {
                        cmd.Transaction = tx;
                        cmd.CommandText = "DELETE FROM PersonnesACharge WHERE EmployeeId = @id;";
                        cmd.Parameters.AddWithValue("@id", employeeId);
                        await cmd.ExecuteNonQueryAsync();
                    }

                    foreach (var personne in liste)
                    {
                        using (var cmd = conn.CreateCommand())
                        {
                            cmd.Transaction = tx;
                            cmd.CommandText =
                                "INSERT INTO PersonnesACharge " +
                                "(EmployeeId, Nom, Prenom, Lien, DateNaissance, PoursuitDesEtudes, " +
                                " SituationDeHandicap, CompteCommeCharge, Commentaire) " +
                                "VALUES (@employe, @nom, @prenom, @lien, @naissance, @etudes, " +
                                "        @handicap, @compte, @commentaire);";

                            cmd.Parameters.AddWithValue("@employe", employeeId);
                            cmd.Parameters.AddWithValue("@nom", personne.Nom ?? "");
                            cmd.Parameters.AddWithValue("@prenom", personne.Prenom ?? "");
                            cmd.Parameters.AddWithValue("@lien", (int)personne.Lien);
                            cmd.Parameters.AddWithValue("@naissance", DbValue.DateNullableToDb(personne.DateNaissance));
                            cmd.Parameters.AddWithValue("@etudes", personne.PoursuitDesEtudes ? 1 : 0);
                            cmd.Parameters.AddWithValue("@handicap", personne.SituationDeHandicap ? 1 : 0);
                            cmd.Parameters.AddWithValue("@compte", personne.CompteCommeCharge ? 1 : 0);
                            cmd.Parameters.AddWithValue("@commentaire", personne.Commentaire ?? "");

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

        /// <summary>
        /// Nombre de charges ouvrant droit à réduction à une date donnée, plafonné à 7.
        ///
        /// Le conjoint n'est compté que s'il ne travaille pas — c'est la condition posée
        /// par l'article 113, et c'est aussi ce que traduit déjà le champ
        /// <c>IsSpouseWorking</c> de la fiche.
        /// </summary>
        public static int Decompte(
            IEnumerable<PersonneACharge> personnes, DateTime date, bool conjointTravaille)
        {
            if (personnes == null) return 0;

            int total = personnes.Count(p =>
                p.OuvreDroitA(date) &&
                !(p.Lien == LienParente.Conjoint && conjointTravaille));

            return Math.Min(total, PlafondCharges);
        }

        /// <summary>
        /// Les charges qui sortiront du décompte dans les douze mois — un enfant qui
        /// atteint sa limite d'âge. C'est l'alerte qui manquait : personne ne surveille
        /// une date d'anniversaire, et la réduction continue d'être appliquée à tort.
        /// </summary>
        public static List<PersonneACharge> SortiesProchaines(
            IEnumerable<PersonneACharge> personnes, DateTime date)
        {
            if (personnes == null) return new List<PersonneACharge>();

            var dansUnAn = date.AddYears(1);

            return personnes
                .Where(p => p.OuvreDroitA(date) && !p.OuvreDroitA(dansUnAn))
                .OrderBy(p => p.DateNaissance)
                .ToList();
        }
    }
}
