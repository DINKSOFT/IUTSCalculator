using System;
using System.Collections.Generic;
using Microsoft.Data.Sqlite;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using IUTSCalculator.Helpers;
using IUTSCalculator.Models;

namespace IUTSCalculator.Services
{
    /// <summary>
    /// Service métier pour la gestion du recrutement, du vivier des candidatures,
    /// des étapes de sélection et de la passerelle d'intégration directe en fiche salarié (module RH-05).
    /// </summary>
    public class RecrutementService
    {
        private readonly SQLiteService _sqliteService;
        private readonly DataService _dataService;

        public RecrutementService(SQLiteService sqliteService, DataService dataService)
        {
            _sqliteService = sqliteService ?? throw new ArgumentNullException(nameof(sqliteService));
            _dataService = dataService ?? throw new ArgumentNullException(nameof(dataService));
        }

        #region Besoins de Recrutement

        /// <summary>
        /// Récupère la liste des besoins de recrutement avec filtres facultatifs.
        /// </summary>
        public async Task<List<BesoinRecrutement>> ObtenirBesoinsAsync(StatutBesoinRecrutement? statut = null, string recherche = null)
        {
            var besoins = new List<BesoinRecrutement>();

            await Task.Run(() =>
            {
                using (var conn = _sqliteService.CreerConnexion())
                {
                    using (var cmd = conn.CreateCommand())
                    {
                        string sql = @"
                            SELECT b.Id, b.Reference, b.IntitulePoste, b.PosteId, b.Departement, b.Motif,
                                   b.NombrePostes, b.NombreRecrutes, b.TypeContratVise,
                                   b.RemunerationProposeeMin, b.RemunerationProposeeMax,
                                   b.IndemniteLogement, b.IndemniteTransport, b.IndemniteFonction, b.IndemniteSujetion,
                                   b.DateExpression, b.DateEcheanceSouhaitee, b.DateCloture,
                                   b.Statut, b.ResponsableRecrutement, b.DescriptionProfil, b.DateCreation,
                                   p.Intitule AS PosteIntitule,
                                   COUNT(c.Id) AS TotalCandidatures,
                                   SUM(CASE WHEN c.Statut IN (0, 1) THEN 1 ELSE 0 END) AS TotalEnCours
                            FROM RecrutementBesoins b
                            LEFT JOIN Postes p ON b.PosteId = p.Id
                            LEFT JOIN RecrutementCandidatures c ON b.Id = c.BesoinId
                            WHERE 1=1 ";

                        if (statut.HasValue)
                        {
                            sql += " AND b.Statut = @statut ";
                            cmd.Parameters.AjouterValeur("@statut", (int)statut.Value);
                        }

                        if (!string.IsNullOrWhiteSpace(recherche))
                        {
                            sql += @" AND (b.Reference LIKE @recherche 
                                       OR b.IntitulePoste LIKE @recherche 
                                       OR b.Departement LIKE @recherche 
                                       OR b.ResponsableRecrutement LIKE @recherche) ";
                            cmd.Parameters.AjouterValeur("@recherche", "%" + recherche.Trim() + "%");
                        }

                        sql += @" GROUP BY b.Id, b.Reference, b.IntitulePoste, b.PosteId, b.Departement, b.Motif,
                                           b.NombrePostes, b.NombreRecrutes, b.TypeContratVise,
                                           b.RemunerationProposeeMin, b.RemunerationProposeeMax,
                                   b.IndemniteLogement, b.IndemniteTransport, b.IndemniteFonction, b.IndemniteSujetion,
                                           b.DateExpression, b.DateEcheanceSouhaitee, b.DateCloture,
                                           b.Statut, b.ResponsableRecrutement, b.DescriptionProfil, b.DateCreation,
                                           p.Intitule
                                  ORDER BY b.DateExpression DESC, b.Id DESC;";

                        cmd.CommandText = sql;

                        using (var reader = cmd.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                besoins.Add(LireBesoin(reader));
                            }
                        }
                    }
                }
            });

            return besoins;
        }

        /// <summary>
        /// Récupère un besoin de recrutement par son identifiant unique.
        /// </summary>
        public async Task<BesoinRecrutement> ObtenirBesoinParIdAsync(int id)
        {
            BesoinRecrutement besoin = null;

            await Task.Run(() =>
            {
                using (var conn = _sqliteService.CreerConnexion())
                {
                    using (var cmd = conn.CreateCommand())
                    {
                        cmd.CommandText = @"
                            SELECT b.Id, b.Reference, b.IntitulePoste, b.PosteId, b.Departement, b.Motif,
                                   b.NombrePostes, b.NombreRecrutes, b.TypeContratVise,
                                   b.RemunerationProposeeMin, b.RemunerationProposeeMax,
                                   b.IndemniteLogement, b.IndemniteTransport, b.IndemniteFonction, b.IndemniteSujetion,
                                   b.DateExpression, b.DateEcheanceSouhaitee, b.DateCloture,
                                   b.Statut, b.ResponsableRecrutement, b.DescriptionProfil, b.DateCreation,
                                   p.Intitule AS PosteIntitule,
                                   COUNT(c.Id) AS TotalCandidatures,
                                   SUM(CASE WHEN c.Statut IN (0, 1) THEN 1 ELSE 0 END) AS TotalEnCours
                            FROM RecrutementBesoins b
                            LEFT JOIN Postes p ON b.PosteId = p.Id
                            LEFT JOIN RecrutementCandidatures c ON b.Id = c.BesoinId
                            WHERE b.Id = @id
                            GROUP BY b.Id, b.Reference, b.IntitulePoste, b.PosteId, b.Departement, b.Motif,
                                     b.NombrePostes, b.NombreRecrutes, b.TypeContratVise,
                                     b.RemunerationProposeeMin, b.RemunerationProposeeMax,
                                   b.IndemniteLogement, b.IndemniteTransport, b.IndemniteFonction, b.IndemniteSujetion,
                                     b.DateExpression, b.DateEcheanceSouhaitee, b.DateCloture,
                                     b.Statut, b.ResponsableRecrutement, b.DescriptionProfil, b.DateCreation,
                                     p.Intitule;";

                        cmd.Parameters.AjouterValeur("@id", id);

                        using (var reader = cmd.ExecuteReader())
                        {
                            if (reader.Read())
                            {
                                besoin = LireBesoin(reader);
                            }
                        }
                    }
                }
            });

            return besoin;
        }

        /// <summary>
        /// Génère une nouvelle référence incrémentale de besoin au format REC-yyyy-XXXX.
        /// </summary>
        public async Task<string> GenererReferenceBesoinAsync()
        {
            return await Task.Run(() =>
            {
                int annee = DateTime.Today.Year;
                string prefixe = $"REC-{annee}-";

                using (var conn = _sqliteService.CreerConnexion())
                {
                    using (var cmd = conn.CreateCommand())
                    {
                        cmd.CommandText = @"
                            SELECT Reference FROM RecrutementBesoins 
                            WHERE Reference LIKE @pref 
                            ORDER BY Reference DESC LIMIT 1;";
                        cmd.Parameters.AjouterValeur("@pref", prefixe + "%");

                        var result = cmd.ExecuteScalar() as string;
                        int sequence = 1;
                        if (!string.IsNullOrEmpty(result) && result.Length >= prefixe.Length + 3)
                        {
                            string numStr = result.Substring(prefixe.Length);
                            if (int.TryParse(numStr, out int lastNum))
                            {
                                sequence = lastNum + 1;
                            }
                        }

                        return $"{prefixe}{sequence:D3}";
                    }
                }
            });
        }

        /// <summary>
        /// Crée ou met à jour un besoin de recrutement.
        /// </summary>
        public async Task<int> EnregistrerBesoinAsync(BesoinRecrutement besoin)
        {
            if (besoin == null) throw new ArgumentNullException(nameof(besoin));
            if (string.IsNullOrWhiteSpace(besoin.IntitulePoste))
                throw new ArgumentException("L'intitulé du poste est obligatoire.", nameof(besoin));

            return await Task.Run(() =>
            {
                using (var conn = _sqliteService.CreerConnexion())
                {
                    using (var cmd = conn.CreateCommand())
                    {
                        if (besoin.Id == 0)
                        {
                            if (string.IsNullOrWhiteSpace(besoin.Reference))
                            {
                                int annee = DateTime.Today.Year;
                                string prefixe = $"REC-{annee}-";
                                cmd.CommandText = "SELECT Reference FROM RecrutementBesoins WHERE Reference LIKE @pref ORDER BY Reference DESC LIMIT 1;";
                                cmd.Parameters.AjouterValeur("@pref", prefixe + "%");
                                var res = cmd.ExecuteScalar() as string;
                                int seq = 1;
                                if (!string.IsNullOrEmpty(res) && res.Length >= prefixe.Length + 3)
                                {
                                    if (int.TryParse(res.Substring(prefixe.Length), out int last))
                                        seq = last + 1;
                                }
                                besoin.Reference = $"{prefixe}{seq:D3}";
                                cmd.Parameters.Clear();
                            }

                            cmd.CommandText = @"
                                INSERT INTO RecrutementBesoins (
                                    Reference, IntitulePoste, PosteId, Departement, Motif,
                                    NombrePostes, NombreRecrutes, TypeContratVise,
                                    RemunerationProposeeMin, RemunerationProposeeMax,
                                    IndemniteLogement, IndemniteTransport, IndemniteFonction, IndemniteSujetion,
                                    DateExpression, DateEcheanceSouhaitee, DateCloture,
                                    Statut, ResponsableRecrutement, DescriptionProfil, DateCreation
                                ) VALUES (
                                    @Reference, @IntitulePoste, @PosteId, @Departement, @Motif,
                                    @NombrePostes, @NombreRecrutes, @TypeContratVise,
                                    @RemunMin, @RemunMax,
                                    @IndLogement, @IndTransport, @IndFonction, @IndSujetion,
                                    @DateExpression, @DateEcheance, @DateCloture,
                                    @Statut, @Responsable, @Description, @DateCreation
                                );
                                SELECT last_insert_rowid();";

                            RemplirParametresBesoin(cmd, besoin);
                            cmd.Parameters.AjouterValeur("@DateCreation", DbValue.DateToDb(DateTime.Now));

                            besoin.Id = Convert.ToInt32(cmd.ExecuteScalar());
                            return besoin.Id;
                        }
                        else
                        {
                            cmd.CommandText = @"
                                UPDATE RecrutementBesoins SET
                                    Reference = @Reference,
                                    IntitulePoste = @IntitulePoste,
                                    PosteId = @PosteId,
                                    Departement = @Departement,
                                    Motif = @Motif,
                                    NombrePostes = @NombrePostes,
                                    TypeContratVise = @TypeContratVise,
                                    RemunerationProposeeMin = @RemunMin,
                                    RemunerationProposeeMax = @RemunMax,
                                    IndemniteLogement = @IndLogement,
                                    IndemniteTransport = @IndTransport,
                                    IndemniteFonction = @IndFonction,
                                    IndemniteSujetion = @IndSujetion,
                                    DateExpression = @DateExpression,
                                    DateEcheanceSouhaitee = @DateEcheance,
                                    DateCloture = @DateCloture,
                                    Statut = @Statut,
                                    ResponsableRecrutement = @Responsable,
                                    DescriptionProfil = @Description
                                WHERE Id = @Id;";

                            cmd.Parameters.AjouterValeur("@Id", besoin.Id);
                            RemplirParametresBesoin(cmd, besoin);

                            cmd.ExecuteNonQuery();
                            return besoin.Id;
                        }
                    }
                }
            });
        }

        /// <summary>
        /// Change le statut d'un besoin de recrutement (ex: Ouvert, En cours, Pourvu, Annulé).
        /// </summary>
        public async Task ChangerStatutBesoinAsync(int besoinId, StatutBesoinRecrutement nouveauStatut)
        {
            await Task.Run(() =>
            {
                using (var conn = _sqliteService.CreerConnexion())
                {
                    using (var cmd = conn.CreateCommand())
                    {
                        cmd.CommandText = @"
                            UPDATE RecrutementBesoins 
                            SET Statut = @statut,
                                DateCloture = CASE 
                                    WHEN @statut IN (3, 4) AND DateCloture IS NULL THEN date('now')
                                    WHEN @statut NOT IN (3, 4) THEN NULL
                                    ELSE DateCloture END
                            WHERE Id = @id;";
                        cmd.Parameters.AjouterValeur("@statut", (int)nouveauStatut);
                        cmd.Parameters.AjouterValeur("@id", besoinId);
                        cmd.ExecuteNonQuery();
                    }
                }
            });
        }

        /// <summary>
        /// Supprime un besoin de recrutement.
        /// Verrou probatoire strict : si une candidature de ce besoin a abouti à une embauche
        /// avec un salarié créé en base, la suppression est strictement interdite.
        /// </summary>
        public async Task SupprimerBesoinAsync(int besoinId)
        {
            await Task.Run(() =>
            {
                using (var conn = _sqliteService.CreerConnexion())
                {
                    // 1. Contrôle du verrou probatoire
                    using (var cmdVerif = conn.CreateCommand())
                    {
                        cmdVerif.CommandText = @"
                            SELECT COUNT(*) FROM RecrutementCandidatures 
                            WHERE BesoinId = @besoinId AND (SalarieCreeMatricule IS NOT NULL OR Statut = 4);";
                        cmdVerif.Parameters.AjouterValeur("@besoinId", besoinId);
                        long nbEmbauches = Convert.ToInt64(cmdVerif.ExecuteScalar());
                        if (nbEmbauches > 0)
                        {
                            throw new InvalidOperationException(
                                "Suppression impossible : ce besoin de recrutement a donné lieu à une embauche effective " +
                                "avec création d'une fiche salarié dans la paie. Les données doivent être conservées à des fins légales et probatoires.");
                        }
                    }

                    // 2. Cascade applicative C# transactionnelle
                    using (var tx = conn.BeginTransaction())
                    {
                        try
                        {
                            // Supprimer les entretiens liés aux candidatures de ce besoin
                            using (var cmdCascade1 = conn.CreateCommand())
                            {
                                cmdCascade1.Transaction = tx;
                                cmdCascade1.CommandText = @"
                                    DELETE FROM RecrutementEntretiens 
                                    WHERE CandidatureId IN (SELECT Id FROM RecrutementCandidatures WHERE BesoinId = @besoinId);";
                                cmdCascade1.Parameters.AjouterValeur("@besoinId", besoinId);
                                cmdCascade1.ExecuteNonQuery();
                            }

                            // Supprimer les candidatures liées à ce besoin
                            using (var cmdCascade2 = conn.CreateCommand())
                            {
                                cmdCascade2.Transaction = tx;
                                cmdCascade2.CommandText = "DELETE FROM RecrutementCandidatures WHERE BesoinId = @besoinId;";
                                cmdCascade2.Parameters.AjouterValeur("@besoinId", besoinId);
                                cmdCascade2.ExecuteNonQuery();
                            }

                            // Supprimer le besoin
                            using (var cmdDelete = conn.CreateCommand())
                            {
                                cmdDelete.Transaction = tx;
                                cmdDelete.CommandText = "DELETE FROM RecrutementBesoins WHERE Id = @besoinId;";
                                cmdDelete.Parameters.AjouterValeur("@besoinId", besoinId);
                                cmdDelete.ExecuteNonQuery();
                            }

                            tx.Commit();
                        }
                        catch
                        {
                            tx.Rollback();
                            throw;
                        }
                    }
                }
            });
        }

        #endregion

        #region Vivier des Candidats

        /// <summary>
        /// Récupère la liste des profils du vivier avec filtres facultatifs.
        /// </summary>
        public async Task<List<Candidat>> ObtenirCandidatsAsync(bool? vivierActif = null, string recherche = null)
        {
            var candidats = new List<Candidat>();

            await Task.Run(() =>
            {
                using (var conn = _sqliteService.CreerConnexion())
                {
                    using (var cmd = conn.CreateCommand())
                    {
                        string sql = @"
                            SELECT c.Id, c.Nom, c.Prenoms, c.Email, c.Telephone, c.DiplomeLePlusEleve,
                                   c.AnneesExperience, c.DerniereFonction, c.CompetencesCles,
                                   c.CvChemin, c.VivierActif, c.Commentaires, c.DateCreation,
                                   COUNT(cand.Id) AS NombreCandidatures
                            FROM RecrutementCandidats c
                            LEFT JOIN RecrutementCandidatures cand ON c.Id = cand.CandidatId
                            WHERE 1=1 ";

                        if (vivierActif.HasValue)
                        {
                            sql += " AND c.VivierActif = @actif ";
                            cmd.Parameters.AjouterValeur("@actif", vivierActif.Value ? 1 : 0);
                        }

                        if (!string.IsNullOrWhiteSpace(recherche))
                        {
                            sql += @" AND (c.Nom LIKE @recherche 
                                       OR c.Prenoms LIKE @recherche 
                                       OR c.Email LIKE @recherche 
                                       OR c.Telephone LIKE @recherche 
                                       OR c.DiplomeLePlusEleve LIKE @recherche 
                                       OR c.CompetencesCles LIKE @recherche 
                                       OR c.DerniereFonction LIKE @recherche) ";
                            cmd.Parameters.AjouterValeur("@recherche", "%" + recherche.Trim() + "%");
                        }

                        sql += @" GROUP BY c.Id, c.Nom, c.Prenoms, c.Email, c.Telephone, c.DiplomeLePlusEleve,
                                           c.AnneesExperience, c.DerniereFonction, c.CompetencesCles,
                                           c.CvChemin, c.VivierActif, c.Commentaires, c.DateCreation
                                  ORDER BY c.Nom ASC, c.Prenoms ASC;";

                        cmd.CommandText = sql;

                        using (var reader = cmd.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                candidats.Add(LireCandidat(reader));
                            }
                        }
                    }
                }
            });

            return candidats;
        }

        /// <summary>
        /// Récupère un candidat par son identifiant unique.
        /// </summary>
        public async Task<Candidat> ObtenirCandidatParIdAsync(int id)
        {
            Candidat candidat = null;

            await Task.Run(() =>
            {
                using (var conn = _sqliteService.CreerConnexion())
                {
                    using (var cmd = conn.CreateCommand())
                    {
                        cmd.CommandText = @"
                            SELECT c.Id, c.Nom, c.Prenoms, c.Email, c.Telephone, c.DiplomeLePlusEleve,
                                   c.AnneesExperience, c.DerniereFonction, c.CompetencesCles,
                                   c.CvChemin, c.VivierActif, c.Commentaires, c.DateCreation,
                                   COUNT(cand.Id) AS NombreCandidatures
                            FROM RecrutementCandidats c
                            LEFT JOIN RecrutementCandidatures cand ON c.Id = cand.CandidatId
                            WHERE c.Id = @id
                            GROUP BY c.Id, c.Nom, c.Prenoms, c.Email, c.Telephone, c.DiplomeLePlusEleve,
                                     c.AnneesExperience, c.DerniereFonction, c.CompetencesCles,
                                     c.CvChemin, c.VivierActif, c.Commentaires, c.DateCreation;";

                        cmd.Parameters.AjouterValeur("@id", id);

                        using (var reader = cmd.ExecuteReader())
                        {
                            if (reader.Read())
                            {
                                candidat = LireCandidat(reader);
                            }
                        }
                    }
                }
            });

            return candidat;
        }

        /// <summary>
        /// Crée ou met à jour le profil d'un candidat dans le vivier.
        /// </summary>
        public async Task<int> EnregistrerCandidatAsync(Candidat candidat)
        {
            if (candidat == null) throw new ArgumentNullException(nameof(candidat));
            if (string.IsNullOrWhiteSpace(candidat.Nom) || string.IsNullOrWhiteSpace(candidat.Prenoms))
                throw new ArgumentException("Le nom et les prénoms du candidat sont obligatoires.", nameof(candidat));

            return await Task.Run(() =>
            {
                using (var conn = _sqliteService.CreerConnexion())
                {
                    VerifierDoublonCandidat(conn, candidat);

                    using (var cmd = conn.CreateCommand())
                    {
                        if (candidat.Id == 0)
                        {
                            cmd.CommandText = @"
                                INSERT INTO RecrutementCandidats (
                                    Nom, Prenoms, Email, Telephone, DiplomeLePlusEleve,
                                    AnneesExperience, DerniereFonction, CompetencesCles,
                                    CvChemin, VivierActif, Commentaires, DateCreation
                                ) VALUES (
                                    @Nom, @Prenoms, @Email, @Telephone, @Diplome,
                                    @AnneesExperience, @DerniereFonction, @CompetencesCles,
                                    @CvChemin, @VivierActif, @Commentaires, @DateCreation
                                );
                                SELECT last_insert_rowid();";

                            RemplirParametresCandidat(cmd, candidat);
                            cmd.Parameters.AjouterValeur("@DateCreation", DbValue.DateToDb(DateTime.Now));

                            candidat.Id = Convert.ToInt32(cmd.ExecuteScalar());
                            return candidat.Id;
                        }
                        else
                        {
                            cmd.CommandText = @"
                                UPDATE RecrutementCandidats SET
                                    Nom = @Nom,
                                    Prenoms = @Prenoms,
                                    Email = @Email,
                                    Telephone = @Telephone,
                                    DiplomeLePlusEleve = @Diplome,
                                    AnneesExperience = @AnneesExperience,
                                    DerniereFonction = @DerniereFonction,
                                    CompetencesCles = @CompetencesCles,
                                    CvChemin = @CvChemin,
                                    VivierActif = @VivierActif,
                                    Commentaires = @Commentaires
                                WHERE Id = @Id;";

                            cmd.Parameters.AjouterValeur("@Id", candidat.Id);
                            RemplirParametresCandidat(cmd, candidat);

                            cmd.ExecuteNonQuery();
                            return candidat.Id;
                        }
                    }
                }
            });
        }

        /// <summary>
        /// Supprime un profil du vivier.
        /// Verrou probatoire étendu : si le candidat a été embauché avec création d'une fiche
        /// salarié, la suppression est formellement bloquée.
        /// </summary>
        public async Task SupprimerCandidatAsync(int candidatId)
        {
            await Task.Run(() =>
            {
                using (var conn = _sqliteService.CreerConnexion())
                {
                    // 1. Contrôle du verrou probatoire
                    using (var cmdVerif = conn.CreateCommand())
                    {
                        cmdVerif.CommandText = @"
                            SELECT COUNT(*) FROM RecrutementCandidatures
                            WHERE CandidatId = @candidatId AND (SalarieCreeMatricule IS NOT NULL OR Statut = 4);";
                        cmdVerif.Parameters.AjouterValeur("@candidatId", candidatId);
                        long nbEmbauches = Convert.ToInt64(cmdVerif.ExecuteScalar());
                        if (nbEmbauches > 0)
                        {
                            throw new InvalidOperationException(
                                "Suppression impossible : ce candidat a été recruté et intégré dans le personnel de l'entreprise. " +
                                "Son dossier et ses candidatures doivent être conservés pour justification probatoire des embauches.");
                        }
                    }

                    // 1bis. Chemin du CV éventuel, à nettoyer du disque une fois la suppression
                    //       en base confirmée (jamais avant : la BD reste la source de vérité).
                    string cvChemin = null;
                    using (var cmdCv = conn.CreateCommand())
                    {
                        cmdCv.CommandText = "SELECT CvChemin FROM RecrutementCandidats WHERE Id = @candidatId;";
                        cmdCv.Parameters.AjouterValeur("@candidatId", candidatId);
                        var resultat = cmdCv.ExecuteScalar();
                        cvChemin = resultat as string;
                    }

                    // 2. Cascade C#
                    using (var tx = conn.BeginTransaction())
                    {
                        try
                        {
                            using (var cmdCascade1 = conn.CreateCommand())
                            {
                                cmdCascade1.Transaction = tx;
                                cmdCascade1.CommandText = @"
                                    DELETE FROM RecrutementEntretiens 
                                    WHERE CandidatureId IN (SELECT Id FROM RecrutementCandidatures WHERE CandidatId = @candidatId);";
                                cmdCascade1.Parameters.AjouterValeur("@candidatId", candidatId);
                                cmdCascade1.ExecuteNonQuery();
                            }

                            using (var cmdCascade2 = conn.CreateCommand())
                            {
                                cmdCascade2.Transaction = tx;
                                cmdCascade2.CommandText = "DELETE FROM RecrutementCandidatures WHERE CandidatId = @candidatId;";
                                cmdCascade2.Parameters.AjouterValeur("@candidatId", candidatId);
                                cmdCascade2.ExecuteNonQuery();
                            }

                            using (var cmdDelete = conn.CreateCommand())
                            {
                                cmdDelete.Transaction = tx;
                                cmdDelete.CommandText = "DELETE FROM RecrutementCandidats WHERE Id = @candidatId;";
                                cmdDelete.Parameters.AjouterValeur("@candidatId", candidatId);
                                cmdDelete.ExecuteNonQuery();
                            }

                            tx.Commit();
                        }
                        catch
                        {
                            tx.Rollback();
                            throw;
                        }
                    }

                    // 3. Nettoyage du fichier CV physique, une fois la suppression en base actée.
                    SupprimerFichierCvGere(cvChemin);
                }
            });
        }

        #endregion

        #region Candidatures & Pipeline

        /// <summary>
        /// Récupère la liste des candidatures (pipeline) avec filtres multiples et enrichissement des libellés.
        /// </summary>
        public async Task<List<Candidature>> ObtenirCandidaturesAsync(
            int? besoinId = null,
            int? candidatId = null,
            EtapeRecrutement? etape = null,
            StatutCandidature? statut = null,
            string recherche = null)
        {
            var candidatures = new List<Candidature>();

            await Task.Run(() =>
            {
                using (var conn = _sqliteService.CreerConnexion())
                {
                    using (var cmd = conn.CreateCommand())
                    {
                        string sql = @"
                            SELECT c.Id, c.BesoinId, c.CandidatId, c.DatePostulation, c.CanalSource,
                                   c.PretentionSalariale, c.EtapeActuelle, c.Statut, c.ScoreEvaluation,
                                   c.AvisGlobal, c.CommentaireEvaluation, c.DateEmbauche,
                                   c.SalaireNegocie, c.SalarieCreeMatricule, c.TypeContratEmbauche, c.DateCreation,
                                   cand.Nom AS CandidatNom, cand.Prenoms AS CandidatPrenoms,
                                   cand.Email AS CandidatEmail, cand.Telephone AS CandidatTelephone,
                                   cand.DiplomeLePlusEleve AS CandidatDiplome, cand.AnneesExperience AS CandidatExperience,
                                   b.Reference AS BesoinReference, b.IntitulePoste AS BesoinIntitule,
                                   b.Departement AS BesoinDepartement, b.PosteId AS BesoinPosteId
                            FROM RecrutementCandidatures c
                            INNER JOIN RecrutementCandidats cand ON c.CandidatId = cand.Id
                            INNER JOIN RecrutementBesoins b ON c.BesoinId = b.Id
                            WHERE 1=1 ";

                        if (besoinId.HasValue)
                        {
                            sql += " AND c.BesoinId = @besoinId ";
                            cmd.Parameters.AjouterValeur("@besoinId", besoinId.Value);
                        }

                        if (candidatId.HasValue)
                        {
                            sql += " AND c.CandidatId = @candidatId ";
                            cmd.Parameters.AjouterValeur("@candidatId", candidatId.Value);
                        }

                        if (etape.HasValue)
                        {
                            sql += " AND c.EtapeActuelle = @etape ";
                            cmd.Parameters.AjouterValeur("@etape", (int)etape.Value);
                        }

                        if (statut.HasValue)
                        {
                            sql += " AND c.Statut = @statut ";
                            cmd.Parameters.AjouterValeur("@statut", (int)statut.Value);
                        }

                        if (!string.IsNullOrWhiteSpace(recherche))
                        {
                            sql += @" AND (cand.Nom LIKE @recherche 
                                       OR cand.Prenoms LIKE @recherche 
                                       OR cand.Email LIKE @recherche 
                                       OR b.Reference LIKE @recherche 
                                       OR b.IntitulePoste LIKE @recherche) ";
                            cmd.Parameters.AjouterValeur("@recherche", "%" + recherche.Trim() + "%");
                        }

                        sql += " ORDER BY c.DatePostulation DESC, c.Id DESC;";

                        cmd.CommandText = sql;

                        using (var reader = cmd.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                candidatures.Add(LireCandidature(reader));
                            }
                        }
                    }
                }
            });

            return candidatures;
        }

        /// <summary>
        /// Récupère une candidature par son identifiant unique.
        /// </summary>
        public async Task<Candidature> ObtenirCandidatureParIdAsync(int id)
        {
            Candidature candidature = null;

            await Task.Run(() =>
            {
                using (var conn = _sqliteService.CreerConnexion())
                {
                    using (var cmd = conn.CreateCommand())
                    {
                        cmd.CommandText = @"
                            SELECT c.Id, c.BesoinId, c.CandidatId, c.DatePostulation, c.CanalSource,
                                   c.PretentionSalariale, c.EtapeActuelle, c.Statut, c.ScoreEvaluation,
                                   c.AvisGlobal, c.CommentaireEvaluation, c.DateEmbauche,
                                   c.SalaireNegocie, c.SalarieCreeMatricule, c.TypeContratEmbauche, c.DateCreation,
                                   cand.Nom AS CandidatNom, cand.Prenoms AS CandidatPrenoms,
                                   cand.Email AS CandidatEmail, cand.Telephone AS CandidatTelephone,
                                   cand.DiplomeLePlusEleve AS CandidatDiplome, cand.AnneesExperience AS CandidatExperience,
                                   b.Reference AS BesoinReference, b.IntitulePoste AS BesoinIntitule,
                                   b.Departement AS BesoinDepartement, b.PosteId AS BesoinPosteId
                            FROM RecrutementCandidatures c
                            INNER JOIN RecrutementCandidats cand ON c.CandidatId = cand.Id
                            INNER JOIN RecrutementBesoins b ON c.BesoinId = b.Id
                            WHERE c.Id = @id;";

                        cmd.Parameters.AjouterValeur("@id", id);

                        using (var reader = cmd.ExecuteReader())
                        {
                            if (reader.Read())
                            {
                                candidature = LireCandidature(reader);
                            }
                        }
                    }
                }
            });

            return candidature;
        }

        /// <summary>
        /// Crée une nouvelle candidature pour un candidat sur un besoin donné.
        /// En cas de ré-activation d'une candidature précédemment rejetée ou désistée,
        /// elle est remise à l'étape initiale et au statut EnEvaluation.
        /// </summary>
        public async Task<int> PostulerAsync(
            int besoinId,
            int candidatId,
            CanalSourceCandidature canal,
            decimal? pretentionSalariale = null)
        {
            return await Task.Run(() =>
            {
                using (var conn = _sqliteService.CreerConnexion())
                {
                    // 1. Vérifier si une candidature existe déjà pour ce couple
                    using (var cmdVerif = conn.CreateCommand())
                    {
                        cmdVerif.CommandText = @"
                            SELECT Id, Statut, SalarieCreeMatricule 
                            FROM RecrutementCandidatures 
                            WHERE BesoinId = @besoinId AND CandidatId = @candidatId;";
                        cmdVerif.Parameters.AjouterValeur("@besoinId", besoinId);
                        cmdVerif.Parameters.AjouterValeur("@candidatId", candidatId);

                        using (var reader = cmdVerif.ExecuteReader())
                        {
                            if (reader.Read())
                            {
                                int existingId = DbValue.IntFromDb(reader["Id"]);
                                var statutExistant = (StatutCandidature)DbValue.IntFromDb(reader["Statut"]);
                                string salarieMatricule = reader["SalarieCreeMatricule"] == DBNull.Value ? null : Convert.ToString(reader["SalarieCreeMatricule"]);

                                if (statutExistant == StatutCandidature.Embauchee || !string.IsNullOrEmpty(salarieMatricule))
                                {
                                    throw new InvalidOperationException("Ce candidat a déjà été retenu et embauché pour ce besoin de recrutement.");
                                }

                                // Si la candidature existe mais n'est pas embauchée (ex: archivée/rejetée), on la réactive
                                reader.Close();
                                using (var cmdReactiv = conn.CreateCommand())
                                {
                                    cmdReactiv.CommandText = @"
                                        UPDATE RecrutementCandidatures SET
                                            Statut = 1,
                                            EtapeActuelle = 0,
                                            CanalSource = @canal,
                                            PretentionSalariale = @pretention,
                                            DatePostulation = date('now')
                                        WHERE Id = @id;";
                                    cmdReactiv.Parameters.AjouterValeur("@canal", (int)canal);
                                    cmdReactiv.Parameters.AjouterValeur("@pretention", pretentionSalariale.HasValue ? (object)DbValue.MoneyToDb(pretentionSalariale.Value) : DBNull.Value);
                                    cmdReactiv.Parameters.AjouterValeur("@id", existingId);
                                    cmdReactiv.ExecuteNonQuery();
                                    return existingId;
                                }
                            }
                        }
                    }

                    // 2. Insertion standard
                    using (var cmd = conn.CreateCommand())
                    {
                        cmd.CommandText = @"
                            INSERT INTO RecrutementCandidatures (
                                BesoinId, CandidatId, DatePostulation, CanalSource,
                                PretentionSalariale, EtapeActuelle, Statut, DateCreation
                            ) VALUES (
                                @BesoinId, @CandidatId, @DatePostulation, @CanalSource,
                                @Pretention, 0, 0, @DateCreation
                            );
                            SELECT last_insert_rowid();";

                        cmd.Parameters.AjouterValeur("@BesoinId", besoinId);
                        cmd.Parameters.AjouterValeur("@CandidatId", candidatId);
                        cmd.Parameters.AjouterValeur("@DatePostulation", DbValue.DateToDb(DateTime.Today));
                        cmd.Parameters.AjouterValeur("@CanalSource", (int)canal);
                        cmd.Parameters.AjouterValeur("@Pretention", pretentionSalariale.HasValue ? (object)DbValue.MoneyToDb(pretentionSalariale.Value) : DBNull.Value);
                        cmd.Parameters.AjouterValeur("@DateCreation", DbValue.DateToDb(DateTime.Now));

                        return Convert.ToInt32(cmd.ExecuteScalar());
                    }
                }
            });
        }

        /// <summary>
        /// Fait progresser une candidature vers une nouvelle étape du pipeline.
        /// Verrou : impossible de modifier une candidature déjà embauchée.
        /// </summary>
        public async Task AvancerEtapeCandidatureAsync(int candidatureId, EtapeRecrutement nouvelleEtape)
        {
            await Task.Run(() =>
            {
                using (var conn = _sqliteService.CreerConnexion())
                {
                    // Contrôle verrou
                    VerifierCandidatureNonVerrouillee(conn, candidatureId);

                    using (var cmd = conn.CreateCommand())
                    {
                        cmd.CommandText = @"
                            UPDATE RecrutementCandidatures SET
                                EtapeActuelle = @etape,
                                Statut = CASE WHEN Statut = 0 THEN 1 ELSE Statut END
                            WHERE Id = @id;";
                        cmd.Parameters.AjouterValeur("@etape", (int)nouvelleEtape);
                        cmd.Parameters.AjouterValeur("@id", candidatureId);
                        cmd.ExecuteNonQuery();
                    }
                }
            });
        }

        /// <summary>
        /// Modifie le statut d'une candidature (Nouvelle, EnEvaluation, Retenue, Rejetee, Desistee).
        /// Verrou : impossible de modifier une candidature déjà embauchée.
        /// </summary>
        public async Task ChangerStatutCandidatureAsync(int candidatureId, StatutCandidature nouveauStatut, string motif = null)
        {
            if (nouveauStatut == StatutCandidature.Embauchee)
                throw new InvalidOperationException("Le statut 'Embauchée' doit être défini exclusivement via la procédure 'IntegrerCandidatEnSalarieAsync'.");

            await Task.Run(() =>
            {
                using (var conn = _sqliteService.CreerConnexion())
                {
                    VerifierCandidatureNonVerrouillee(conn, candidatureId);

                    using (var cmd = conn.CreateCommand())
                    {
                        cmd.CommandText = @"
                            UPDATE RecrutementCandidatures SET
                                Statut = @statut,
                                CommentaireEvaluation = CASE 
                                    WHEN @motif IS NOT NULL AND LENGTH(@motif) > 0 
                                    THEN COALESCE(CommentaireEvaluation || ' | ', '') || @motif
                                    ELSE CommentaireEvaluation END
                            WHERE Id = @id;";
                        cmd.Parameters.AjouterValeur("@statut", (int)nouveauStatut);
                        cmd.Parameters.AjouterValeur("@motif", (object)motif ?? DBNull.Value);
                        cmd.Parameters.AjouterValeur("@id", candidatureId);
                        cmd.ExecuteNonQuery();
                    }
                }
            });
        }

        /// <summary>
        /// Supprime une candidature et ses entretiens associés.
        /// Verrou probatoire strict : formellement interdit si déjà embauchée.
        /// </summary>
        public async Task SupprimerCandidatureAsync(int candidatureId)
        {
            await Task.Run(() =>
            {
                using (var conn = _sqliteService.CreerConnexion())
                {
                    // Contrôle verrou probatoire
                    VerifierCandidatureNonVerrouillee(conn, candidatureId);

                    using (var tx = conn.BeginTransaction())
                    {
                        try
                        {
                            using (var cmdCascade = conn.CreateCommand())
                            {
                                cmdCascade.Transaction = tx;
                                cmdCascade.CommandText = "DELETE FROM RecrutementEntretiens WHERE CandidatureId = @id;";
                                cmdCascade.Parameters.AjouterValeur("@id", candidatureId);
                                cmdCascade.ExecuteNonQuery();
                            }

                            using (var cmdDelete = conn.CreateCommand())
                            {
                                cmdDelete.Transaction = tx;
                                cmdDelete.CommandText = "DELETE FROM RecrutementCandidatures WHERE Id = @id;";
                                cmdDelete.Parameters.AjouterValeur("@id", candidatureId);
                                cmdDelete.ExecuteNonQuery();
                            }

                            tx.Commit();
                        }
                        catch
                        {
                            tx.Rollback();
                            throw;
                        }
                    }
                }
            });
        }

        #endregion

        #region Entretiens & Évaluations

        /// <summary>
        /// Récupère la liste des entretiens conduits pour une candidature.
        /// </summary>
        public async Task<List<RecrutementEntretien>> ObtenirEntretiensAsync(int? candidatureId = null)
        {
            var entretiens = new List<RecrutementEntretien>();

            await Task.Run(() =>
            {
                using (var conn = _sqliteService.CreerConnexion())
                {
                    using (var cmd = conn.CreateCommand())
                    {
                        string sql = @"
                            SELECT e.Id, e.CandidatureId, e.Etape, e.DateEntretien, e.EvaluateurNom,
                                   e.EvaluateurMatricule, e.NoteGlobale, e.Avis, e.PointsForts,
                                   e.AxesAmelioration, e.Synthese, e.DateCreation,
                                   (cand.Nom || ' ' || cand.Prenoms) AS CandidatNomComplet,
                                   b.IntitulePoste AS BesoinIntitule
                            FROM RecrutementEntretiens e
                            INNER JOIN RecrutementCandidatures c ON e.CandidatureId = c.Id
                            INNER JOIN RecrutementCandidats cand ON c.CandidatId = cand.Id
                            INNER JOIN RecrutementBesoins b ON c.BesoinId = b.Id
                            WHERE 1=1 ";

                        if (candidatureId.HasValue)
                        {
                            sql += " AND e.CandidatureId = @candId ";
                            cmd.Parameters.AjouterValeur("@candId", candidatureId.Value);
                        }

                        sql += " ORDER BY e.DateEntretien DESC, e.Id DESC;";

                        cmd.CommandText = sql;

                        using (var reader = cmd.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                entretiens.Add(LireEntretien(reader));
                            }
                        }
                    }
                }
            });

            return entretiens;
        }

        /// <summary>
        /// Enregistre un compte rendu d'entretien ou d'évaluation et recalcule la note globale de la candidature.
        /// </summary>
        public async Task<int> EnregistrerEntretienAsync(RecrutementEntretien entretien)
        {
            if (entretien == null) throw new ArgumentNullException(nameof(entretien));
            if (string.IsNullOrWhiteSpace(entretien.EvaluateurNom))
                throw new ArgumentException("Le nom de l'évaluateur est obligatoire.", nameof(entretien));

            return await Task.Run(() =>
            {
                using (var conn = _sqliteService.CreerConnexion())
                {
                    VerifierCandidatureNonVerrouillee(conn, entretien.CandidatureId);

                    using (var tx = conn.BeginTransaction())
                    {
                        try
                        {
                            int entretienId = entretien.Id;

                            using (var cmd = conn.CreateCommand())
                            {
                                cmd.Transaction = tx;
                                if (entretien.Id == 0)
                                {
                                    cmd.CommandText = @"
                                        INSERT INTO RecrutementEntretiens (
                                            CandidatureId, Etape, DateEntretien, EvaluateurNom,
                                            EvaluateurMatricule, NoteGlobale, Avis, PointsForts,
                                            AxesAmelioration, Synthese, DateCreation
                                        ) VALUES (
                                            @CandidatureId, @Etape, @DateEntretien, @EvaluateurNom,
                                            @EvaluateurMatricule, @NoteGlobale, @Avis, @PointsForts,
                                            @AxesAmelioration, @Synthese, @DateCreation
                                        );
                                        SELECT last_insert_rowid();";

                                    RemplirParametresEntretien(cmd, entretien);
                                    cmd.Parameters.AjouterValeur("@DateCreation", DbValue.DateToDb(DateTime.Now));

                                    entretienId = Convert.ToInt32(cmd.ExecuteScalar());
                                    entretien.Id = entretienId;
                                }
                                else
                                {
                                    cmd.CommandText = @"
                                        UPDATE RecrutementEntretiens SET
                                            Etape = @Etape,
                                            DateEntretien = @DateEntretien,
                                            EvaluateurNom = @EvaluateurNom,
                                            EvaluateurMatricule = @EvaluateurMatricule,
                                            NoteGlobale = @NoteGlobale,
                                            Avis = @Avis,
                                            PointsForts = @PointsForts,
                                            AxesAmelioration = @AxesAmelioration,
                                            Synthese = @Synthese
                                        WHERE Id = @Id;";

                                    cmd.Parameters.AjouterValeur("@Id", entretien.Id);
                                    RemplirParametresEntretien(cmd, entretien);

                                    cmd.ExecuteNonQuery();
                                }
                            }

                            // Recalculer le score moyen et l'avis le plus récent de la candidature
                            using (var cmdScore = conn.CreateCommand())
                            {
                                cmdScore.Transaction = tx;
                                cmdScore.CommandText = @"
                                    UPDATE RecrutementCandidatures SET
                                        ScoreEvaluation = (SELECT AVG(NoteGlobale) FROM RecrutementEntretiens WHERE CandidatureId = @candId AND NoteGlobale IS NOT NULL),
                                        AvisGlobal = @dernierAvis,
                                        Statut = CASE WHEN Statut = 0 THEN 1 ELSE Statut END
                                    WHERE Id = @candId;";
                                cmdScore.Parameters.AjouterValeur("@candId", entretien.CandidatureId);
                                cmdScore.Parameters.AjouterValeur("@dernierAvis", (object)(entretien.Avis.HasValue ? (int)entretien.Avis.Value : (object)DBNull.Value));
                                cmdScore.ExecuteNonQuery();
                            }

                            tx.Commit();
                            return entretienId;
                        }
                        catch
                        {
                            tx.Rollback();
                            throw;
                        }
                    }
                }
            });
        }

        /// <summary>
        /// Supprime un compte rendu d'entretien et met à jour le score de la candidature.
        /// </summary>
        public async Task SupprimerEntretienAsync(int entretienId)
        {
            await Task.Run(() =>
            {
                using (var conn = _sqliteService.CreerConnexion())
                {
                    int candId = 0;
                    using (var cmdGetCand = conn.CreateCommand())
                    {
                        cmdGetCand.CommandText = "SELECT CandidatureId FROM RecrutementEntretiens WHERE Id = @id;";
                        cmdGetCand.Parameters.AjouterValeur("@id", entretienId);
                        var obj = cmdGetCand.ExecuteScalar();
                        if (obj != null && obj != DBNull.Value)
                            candId = Convert.ToInt32(obj);
                    }

                    if (candId > 0)
                        VerifierCandidatureNonVerrouillee(conn, candId);

                    using (var tx = conn.BeginTransaction())
                    {
                        try
                        {
                            using (var cmd = conn.CreateCommand())
                            {
                                cmd.Transaction = tx;
                                cmd.CommandText = "DELETE FROM RecrutementEntretiens WHERE Id = @id;";
                                cmd.Parameters.AjouterValeur("@id", entretienId);
                                cmd.ExecuteNonQuery();
                            }

                            if (candId > 0)
                            {
                                using (var cmdScore = conn.CreateCommand())
                                {
                                    cmdScore.Transaction = tx;
                                    cmdScore.CommandText = @"
                                        UPDATE RecrutementCandidatures SET
                                            ScoreEvaluation = (SELECT AVG(NoteGlobale) FROM RecrutementEntretiens WHERE CandidatureId = @candId AND NoteGlobale IS NOT NULL)
                                        WHERE Id = @candId;";
                                    cmdScore.Parameters.AjouterValeur("@candId", candId);
                                    cmdScore.ExecuteNonQuery();
                                }
                            }

                            tx.Commit();
                        }
                        catch
                        {
                            tx.Rollback();
                            throw;
                        }
                    }
                }
            });
        }

        #endregion

        #region Passerelle d'Embauche & Intégration Salarié (SAL-01)

        /// <summary>
        /// Intègre directement un candidat retenu dans la base des salariés actifs (module SAL-01).
        /// Surcharge historique : les valeurs non fournies sont reprises de l'offre et du poste
        /// (voir <see cref="IntegrerCandidatEnSalarieAsync(int, decimal, DateTime, ConditionsEmbauche)"/>).
        /// </summary>
        public Task<Employee> IntegrerCandidatEnSalarieAsync(
            int candidatureId,
            decimal salaireNegocie,
            DateTime dateEntree,
            string typeContrat = null,
            string categorie = null,
            string service = null)
        {
            return IntegrerCandidatEnSalarieAsync(candidatureId, salaireNegocie, dateEntree, new ConditionsEmbauche
            {
                TypeContrat = typeContrat,
                Categorie = categorie,
                Service = service
            });
        }

        /// <summary>
        /// Intègre directement un candidat retenu dans la base des salariés actifs (module SAL-01).
        /// Exécuté dans une transaction SQLite unique et atomique :
        /// - Vérifie le verrou et le plafond de recrutement (NombreRecrutes &lt; NombrePostes)
        /// - Attribue le matricule saisi, ou à défaut le prochain matricule libre selon le schéma
        ///   du dossier (préfixe + numéro, ex. STI035 → STI036), après contrôle d'unicité
        /// - Crée le salarié dans `Employees` via `DataService.AddEmployeeAsync`, avec le type de
        ///   contrat, la convention, l'échelon, la catégorie et les indemnités repris de l'offre
        ///   ou du poste RH-02 quand ils existent (les valeurs saisies l'emportent)
        /// - Affecte le poste s'il est rattaché (`Postes.TitulaireMatricule`)
        /// - Passe la candidature au statut `Embauchee` (4) et étape `Finalisee` (7), en y figeant
        ///   le type de contrat retenu
        /// - Incrémente `NombreRecrutes` et clôture le besoin si tous les postes sont pourvus.
        /// </summary>
        public async Task<Employee> IntegrerCandidatEnSalarieAsync(
            int candidatureId,
            decimal salaireNegocie,
            DateTime dateEntree,
            ConditionsEmbauche conditions)
        {
            if (salaireNegocie <= 0)
                throw new ArgumentException("Le salaire négocié doit être strictement supérieur à zéro.", nameof(salaireNegocie));

            conditions = conditions ?? new ConditionsEmbauche();
            string matriculeSaisi = string.IsNullOrWhiteSpace(conditions.Matricule) ? null : conditions.Matricule.Trim();
            if (matriculeSaisi != null)
                VerifierFormatMatricule(matriculeSaisi);

            foreach (var indemnite in new[] { conditions.IndemniteLogement, conditions.IndemniteTransport, conditions.IndemniteFonction, conditions.IndemniteSujetion })
            {
                if (indemnite.HasValue && indemnite.Value < 0)
                    throw new ArgumentException("Les indemnités ne peuvent pas être négatives.");
            }

            return await Task.Run(async () =>
            {
                using (var conn = _sqliteService.CreerConnexion())
                {
                    // 1. Charger et valider la candidature, le besoin (offre) et le poste RH-02 rattaché
                    BesoinRecrutement besoin = null;
                    Candidat candidat = null;
                    int? posteConventionId = null;
                    string posteCategorie = null;
                    string posteEchelon = null;

                    using (var cmdRead = conn.CreateCommand())
                    {
                        cmdRead.CommandText = @"
                            SELECT c.Id, c.BesoinId, c.CandidatId, c.Statut, c.SalarieCreeMatricule,
                                   b.Id AS B_Id, b.NombrePostes, b.NombreRecrutes, b.Statut AS B_Statut,
                                   b.IntitulePoste, b.Departement, b.PosteId, b.TypeContratVise,
                                   b.IndemniteLogement, b.IndemniteTransport, b.IndemniteFonction, b.IndemniteSujetion,
                                   p.ConventionId AS P_ConventionId, p.CategorieConventionnelle AS P_Categorie,
                                   p.EchelonConventionnel AS P_Echelon,
                                   cand.Nom, cand.Prenoms, cand.Email, cand.Telephone, cand.DiplomeLePlusEleve
                            FROM RecrutementCandidatures c
                            INNER JOIN RecrutementBesoins b ON c.BesoinId = b.Id
                            INNER JOIN RecrutementCandidats cand ON c.CandidatId = cand.Id
                            LEFT JOIN Postes p ON b.PosteId = p.Id
                            WHERE c.Id = @id;";
                        cmdRead.Parameters.AjouterValeur("@id", candidatureId);

                        using (var reader = cmdRead.ExecuteReader())
                        {
                            if (!reader.Read())
                                throw new InvalidOperationException($"Candidature introuvable (ID: {candidatureId}).");

                            int statutCand = DbValue.IntFromDb(reader["Statut"]);
                            string matriculeExistant = reader["SalarieCreeMatricule"] == DBNull.Value ? null : Convert.ToString(reader["SalarieCreeMatricule"]);

                            if (statutCand == 4 || !string.IsNullOrEmpty(matriculeExistant))
                            {
                                throw new InvalidOperationException("Cette candidature a déjà été embauchée et intégrée dans les salariés.");
                            }

                            int bStatut = DbValue.IntFromDb(reader["B_Statut"]);
                            if (bStatut == (int)StatutBesoinRecrutement.Annule)
                            {
                                throw new InvalidOperationException("Impossible d'embaucher sur un besoin de recrutement qui a été annulé.");
                            }
                            int nbPostes = DbValue.IntFromDb(reader["NombrePostes"]);
                            int nbRecrutes = DbValue.IntFromDb(reader["NombreRecrutes"]);

                            // Garde-fou anti-sur-recrutement
                            if (bStatut == (int)StatutBesoinRecrutement.Pourvu || nbRecrutes >= nbPostes)
                            {
                                throw new InvalidOperationException(
                                    $"Le nombre de postes à pourvoir pour ce besoin a déjà été atteint ({nbRecrutes}/{nbPostes}). " +
                                    "Augmentez le nombre de postes sur le besoin avant de procéder à une nouvelle embauche.");
                            }

                            besoin = new BesoinRecrutement
                            {
                                Id = DbValue.IntFromDb(reader["B_Id"]),
                                NombrePostes = nbPostes,
                                NombreRecrutes = nbRecrutes,
                                IntitulePoste = Convert.ToString(reader["IntitulePoste"]),
                                Departement = Convert.ToString(reader["Departement"]),
                                PosteId = reader["PosteId"] == DBNull.Value ? (int?)null : Convert.ToInt32(reader["PosteId"]),
                                TypeContratVise = DbValue.TextFromDb(reader["TypeContratVise"]),
                                IndemniteLogement = DbValue.MoneyFromDb(reader["IndemniteLogement"]),
                                IndemniteTransport = DbValue.MoneyFromDb(reader["IndemniteTransport"]),
                                IndemniteFonction = DbValue.MoneyFromDb(reader["IndemniteFonction"]),
                                IndemniteSujetion = DbValue.MoneyFromDb(reader["IndemniteSujetion"])
                            };

                            posteConventionId = DbValue.IntNullableFromDb(reader["P_ConventionId"]);
                            posteCategorie = DbValue.TextFromDb(reader["P_Categorie"]);
                            posteEchelon = DbValue.TextFromDb(reader["P_Echelon"]);

                            candidat = new Candidat
                            {
                                Id = DbValue.IntFromDb(reader["CandidatId"]),
                                Nom = Convert.ToString(reader["Nom"]),
                                Prenoms = Convert.ToString(reader["Prenoms"]),
                                Email = Convert.ToString(reader["Email"]),
                                Telephone = Convert.ToString(reader["Telephone"]),
                                DiplomeLePlusEleve = Convert.ToString(reader["DiplomeLePlusEleve"])
                            };
                        }
                    }

                    // Conditions retenues : la saisie l'emporte, sinon l'offre, sinon le poste RH-02.
                    string typeContrat = PremiereValeur(conditions.TypeContrat, besoin.TypeContratVise, "CDI");
                    string categorie = PremiereValeur(conditions.Categorie, posteCategorie, "Cadre Moyen");
                    string echelon = PremiereValeur(conditions.Echelon, posteEchelon, null);
                    string service = PremiereValeur(conditions.Service, besoin.Departement, null);

                    // 2. Transaction atomique
                    using (var tx = conn.BeginTransaction())
                    {
                        try
                        {
                            // A. Matricule : saisi (contrôlé), sinon prochain matricule libre du dossier.
                            //    L'ancien Guid.NewGuid() produisait un matricule technique hors schéma.
                            //    L'unicité est contrôlée ici, dans la transaction : AddEmployeeAsync
                            //    fait un INSERT OR REPLACE qui écraserait sinon la fiche existante.
                            string matricule = matriculeSaisi ?? ProposerMatriculeSuivant(LireMatriculesExistants(conn, tx));
                            VerifierMatriculeDisponible(conn, tx, matricule);

                            // B. Créer le salarié
                            var salarie = new Employee
                            {
                                EmployeeId = matricule,
                                FirstName = candidat.Prenoms,
                                LastName = candidat.Nom,
                                BaseSalary = salaireNegocie,
                                HousingAllowance = conditions.IndemniteLogement ?? besoin.IndemniteLogement,
                                TransportAllowance = conditions.IndemniteTransport ?? besoin.IndemniteTransport,
                                FunctionAllowance = conditions.IndemniteFonction ?? besoin.IndemniteFonction,
                                SujetionAllowance = conditions.IndemniteSujetion ?? besoin.IndemniteSujetion,
                                Fonction = besoin.IntitulePoste,
                                Service = service,
                                DateEntree = dateEntree,
                                Email = candidat.Email,
                                Telephone = candidat.Telephone,
                                ConventionId = posteConventionId,
                                Echelon = echelon,
                                PosteId = besoin.PosteId,
                                StatutSalarie = 0, // Actif
                                // Catégorie en dernier : son affectation recalcule IsHigherCategory.
                                Category = categorie
                            };

                            await _dataService.AddEmployeeAsync(salarie, conn, tx);

                            // Type de contrat sur la fiche salarié (colonne Employees.TypeContrat, v38).
                            using (var cmdContrat = conn.CreateCommand())
                            {
                                cmdContrat.Transaction = tx;
                                cmdContrat.CommandText = "UPDATE Employees SET TypeContrat = @typeContrat WHERE EmployeeId = @matricule;";
                                cmdContrat.Parameters.AjouterValeur("@typeContrat", typeContrat);
                                cmdContrat.Parameters.AjouterValeur("@matricule", salarie.EmployeeId);
                                cmdContrat.ExecuteNonQuery();
                            }

                            // C. Affecter le poste si rattaché
                            if (besoin.PosteId.HasValue)
                            {
                                using (var cmdPoste = conn.CreateCommand())
                                {
                                    cmdPoste.Transaction = tx;
                                    cmdPoste.CommandText = "UPDATE Postes SET TitulaireMatricule = @matricule WHERE Id = @posteId;";
                                    cmdPoste.Parameters.AjouterValeur("@matricule", salarie.EmployeeId);
                                    cmdPoste.Parameters.AjouterValeur("@posteId", besoin.PosteId.Value);
                                    cmdPoste.ExecuteNonQuery();
                                }
                            }

                            // D. Mettre à jour la candidature (le type de contrat retenu y est figé)
                            using (var cmdCand = conn.CreateCommand())
                            {
                                cmdCand.Transaction = tx;
                                cmdCand.CommandText = @"
                                    UPDATE RecrutementCandidatures SET
                                        Statut = 4,
                                        EtapeActuelle = 7,
                                        DateEmbauche = @dateEmbauche,
                                        SalaireNegocie = @salaire,
                                        SalarieCreeMatricule = @matricule,
                                        TypeContratEmbauche = @typeContrat
                                    WHERE Id = @id;";
                                cmdCand.Parameters.AjouterValeur("@dateEmbauche", DbValue.DateToDb(dateEntree));
                                cmdCand.Parameters.AjouterValeur("@salaire", DbValue.MoneyToDb(salaireNegocie));
                                cmdCand.Parameters.AjouterValeur("@matricule", salarie.EmployeeId);
                                cmdCand.Parameters.AjouterValeur("@typeContrat", typeContrat);
                                cmdCand.Parameters.AjouterValeur("@id", candidatureId);
                                cmdCand.ExecuteNonQuery();
                            }

                            // E. Mettre à jour le besoin
                            int nouveauTotalRecrutes = besoin.NombreRecrutes + 1;
                            bool estPourvu = nouveauTotalRecrutes >= besoin.NombrePostes;
                            int nouveauStatutBesoin = estPourvu ? (int)StatutBesoinRecrutement.Pourvu : (int)StatutBesoinRecrutement.EnCours;

                            using (var cmdBesoin = conn.CreateCommand())
                            {
                                cmdBesoin.Transaction = tx;
                                cmdBesoin.CommandText = @"
                                    UPDATE RecrutementBesoins SET
                                        NombreRecrutes = @nbRecrutes,
                                        Statut = @statut,
                                        DateCloture = CASE WHEN @estPourvu = 1 THEN @dateCloture ELSE DateCloture END
                                    WHERE Id = @id;";
                                cmdBesoin.Parameters.AjouterValeur("@nbRecrutes", nouveauTotalRecrutes);
                                cmdBesoin.Parameters.AjouterValeur("@statut", nouveauStatutBesoin);
                                cmdBesoin.Parameters.AjouterValeur("@estPourvu", estPourvu ? 1 : 0);
                                cmdBesoin.Parameters.AjouterValeur("@dateCloture", DbValue.DateToDb(dateEntree));
                                cmdBesoin.Parameters.AjouterValeur("@id", besoin.Id);
                                cmdBesoin.ExecuteNonQuery();
                            }

                            tx.Commit();
                            return salarie;
                        }
                        catch
                        {
                            tx.Rollback();
                            throw;
                        }
                    }
                }
            });
        }

        #endregion

        #region Matricules des salariés embauchés

        /// <summary>Premier matricule proposé quand le dossier n'a encore aucun schéma de matricules.</summary>
        public const string MatriculeParDefaut = "EMP-0001";

        private const int LongueurMaxMatricule = 30;

        // Préfixe quelconque (éventuellement vide) suivi du numéro final : « STI035 » → « STI » + « 035 ».
        private static readonly Regex RegexSchemaMatricule = new Regex(@"^(.*?)(\d+)$", RegexOptions.CultureInvariant);

        // Lettres, chiffres et séparateurs « - », « _ », « . » ; le matricule sert aussi dans des noms de fichiers.
        private static readonly Regex RegexFormatMatricule = new Regex(@"^[\p{L}\p{N}][\p{L}\p{N}._-]*$", RegexOptions.CultureInvariant);

        private sealed class SchemaMatricule
        {
            public string Prefixe;
            public int Nombre;
            public long NumeroMax;
            public int Largeur;
        }

        /// <summary>
        /// Propose le prochain matricule libre du dossier, selon son schéma de matricules
        /// (voir <see cref="ProposerMatriculeSuivant"/>). Simple proposition, éditable à l'écran :
        /// l'unicité est de toute façon recontrôlée au moment de l'embauche.
        /// </summary>
        public async Task<string> ProposerProchainMatriculeAsync()
        {
            return await Task.Run(() =>
            {
                using (var conn = _sqliteService.CreerConnexion())
                {
                    return ProposerMatriculeSuivant(LireMatriculesExistants(conn, null));
                }
            });
        }

        /// <summary>
        /// Déduit le schéma de matricules du dossier et renvoie le prochain matricule libre :
        /// le schéma retenu est le préfixe le plus utilisé (à égalité, celui du plus grand
        /// numéro) ; le numéro suivant garde la largeur du numéro existant (STI035 → STI036,
        /// 0099 → 0100). Les matricules techniques (GUID des anciennes embauches RH-05) et
        /// ceux sans numéro final sont ignorés. Sans schéma reconnaissable : « EMP-0001 ».
        /// Le résultat n'est jamais un matricule déjà présent (comparaison sans casse).
        /// </summary>
        public static string ProposerMatriculeSuivant(IEnumerable<string> matriculesExistants)
        {
            var existants = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var schemas = new Dictionary<string, SchemaMatricule>(StringComparer.OrdinalIgnoreCase);

            if (matriculesExistants != null)
            {
                foreach (var brut in matriculesExistants)
                {
                    if (string.IsNullOrWhiteSpace(brut)) continue;
                    string matricule = brut.Trim();
                    existants.Add(matricule);

                    Guid guid;
                    if (Guid.TryParse(matricule, out guid)) continue;

                    var m = RegexSchemaMatricule.Match(matricule);
                    if (!m.Success) continue;

                    string chiffres = m.Groups[2].Value;
                    if (chiffres.Length > 9) continue; // horodatage ou identifiant technique, pas une numérotation

                    string prefixe = m.Groups[1].Value;
                    long numero = long.Parse(chiffres, CultureInfo.InvariantCulture);

                    SchemaMatricule schema;
                    if (!schemas.TryGetValue(prefixe, out schema))
                    {
                        schema = new SchemaMatricule { Prefixe = prefixe, NumeroMax = -1 };
                        schemas[prefixe] = schema;
                    }
                    schema.Nombre++;
                    if (numero > schema.NumeroMax)
                    {
                        schema.NumeroMax = numero;
                        schema.Prefixe = prefixe; // graphie du matricule le plus récent
                    }
                    if (chiffres.Length > schema.Largeur) schema.Largeur = chiffres.Length;
                }
            }

            SchemaMatricule retenu = schemas.Values
                .OrderByDescending(s => s.Nombre)
                .ThenByDescending(s => s.NumeroMax)
                .ThenBy(s => s.Prefixe, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault()
                ?? new SchemaMatricule { Prefixe = "EMP-", NumeroMax = 0, Largeur = 4 };

            long suivant = retenu.NumeroMax + 1;
            string proposition;
            do
            {
                proposition = retenu.Prefixe + suivant.ToString(CultureInfo.InvariantCulture).PadLeft(retenu.Largeur, '0');
                suivant++;
            }
            while (existants.Contains(proposition));

            return proposition;
        }

        /// <summary>
        /// Matricules déjà employés dans le dossier : salariés, et historique de paie (un salarié
        /// supprimé garde ses bulletins archivés : réutiliser son matricule les rattacherait au nouvel embauché).
        /// </summary>
        private static List<string> LireMatriculesExistants(SqliteConnection conn, SqliteTransaction tx)
        {
            var liste = new List<string>();
            using (var cmd = conn.CreateCommand())
            {
                if (tx != null) cmd.Transaction = tx;
                cmd.CommandText = "SELECT EmployeeId FROM Employees UNION SELECT EmployeeId FROM TaxCalculations;";
                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        string m = DbValue.TextFromDb(reader["EmployeeId"]);
                        if (!string.IsNullOrWhiteSpace(m)) liste.Add(m);
                    }
                }
            }
            return liste;
        }

        private static void VerifierFormatMatricule(string matricule)
        {
            if (string.IsNullOrWhiteSpace(matricule))
                throw new ArgumentException("Le matricule du salarié est obligatoire.");

            if (matricule.Length > LongueurMaxMatricule || !RegexFormatMatricule.IsMatch(matricule))
                throw new ArgumentException(
                    $"Le matricule « {matricule} » n'est pas valide : utilisez uniquement des lettres, des chiffres " +
                    $"et les séparateurs « - », « _ » ou « . » ({LongueurMaxMatricule} caractères au plus).");
        }

        private static void VerifierMatriculeDisponible(SqliteConnection conn, SqliteTransaction tx, string matricule)
        {
            using (var cmd = conn.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = "SELECT COUNT(*) FROM Employees WHERE TRIM(EmployeeId) = @matricule COLLATE NOCASE;";
                cmd.Parameters.AjouterValeur("@matricule", matricule);
                if (Convert.ToInt64(cmd.ExecuteScalar()) > 0)
                {
                    throw new InvalidOperationException(
                        $"Le matricule « {matricule} » est déjà attribué à un salarié du dossier. " +
                        "Saisissez un autre matricule (le prochain matricule libre est proposé automatiquement).");
                }
            }

            using (var cmd = conn.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = "SELECT COUNT(*) FROM TaxCalculations WHERE TRIM(EmployeeId) = @matricule COLLATE NOCASE;";
                cmd.Parameters.AjouterValeur("@matricule", matricule);
                if (Convert.ToInt64(cmd.ExecuteScalar()) > 0)
                {
                    throw new InvalidOperationException(
                        $"Le matricule « {matricule} » figure déjà dans l'historique de paie du dossier (ancien salarié). " +
                        "Saisissez un autre matricule (le prochain matricule libre est proposé automatiquement).");
                }
            }
        }

        /// <summary>Première valeur renseignée (non vide), rognée ; sinon la valeur par défaut.</summary>
        private static string PremiereValeur(string saisie, string repli, string defaut)
        {
            if (!string.IsNullOrWhiteSpace(saisie)) return saisie.Trim();
            if (!string.IsNullOrWhiteSpace(repli)) return repli.Trim();
            return defaut;
        }

        #endregion

        #region Statistiques & Tableau de Bord

        /// <summary>
        /// Calcule les métriques consolidées du tableau de bord RH-05.
        /// </summary>
        public async Task<TableauBordRecrutement> ObtenirTableauBordAsync()
        {
            var tb = new TableauBordRecrutement();

            await Task.Run(() =>
            {
                using (var conn = _sqliteService.CreerConnexion())
                {
                    // Métriques des besoins
                    using (var cmd = conn.CreateCommand())
                    {
                        cmd.CommandText = @"
                            SELECT 
                                COUNT(*) AS TotalBesoins,
                                SUM(CASE WHEN Statut = 1 THEN 1 ELSE 0 END) AS Ouverts,
                                SUM(CASE WHEN Statut = 2 THEN 1 ELSE 0 END) AS EnCours,
                                SUM(CASE WHEN Statut = 3 THEN 1 ELSE 0 END) AS Pourvus,
                                COALESCE(SUM(NombrePostes), 0) AS TotalPostes,
                                COALESCE(SUM(NombreRecrutes), 0) AS TotalRecrutes
                            FROM RecrutementBesoins;";

                        using (var r = cmd.ExecuteReader())
                        {
                            if (r.Read())
                            {
                                tb.TotalBesoins = DbValue.IntFromDb(r["TotalBesoins"]);
                                tb.BesoinsOuverts = DbValue.IntFromDb(r["Ouverts"]);
                                tb.BesoinsEnCours = DbValue.IntFromDb(r["EnCours"]);
                                tb.BesoinsPourvus = DbValue.IntFromDb(r["Pourvus"]);
                                tb.PostesAPourvoir = DbValue.IntFromDb(r["TotalPostes"]);
                                tb.PostesPourvus = DbValue.IntFromDb(r["TotalRecrutes"]);
                            }
                        }
                    }

                    // Métriques des candidatures
                    using (var cmd = conn.CreateCommand())
                    {
                        cmd.CommandText = @"
                            SELECT 
                                COUNT(*) AS TotalCandidatures,
                                SUM(CASE WHEN Statut = 0 THEN 1 ELSE 0 END) AS Nouvelles,
                                SUM(CASE WHEN Statut = 1 THEN 1 ELSE 0 END) AS EnEvaluation,
                                SUM(CASE WHEN Statut = 2 THEN 1 ELSE 0 END) AS Retenues,
                                SUM(CASE WHEN Statut = 4 THEN 1 ELSE 0 END) AS Embauchees,
                                SUM(CASE WHEN Statut = 3 THEN 1 ELSE 0 END) AS Rejetees
                            FROM RecrutementCandidatures;";

                        using (var r = cmd.ExecuteReader())
                        {
                            if (r.Read())
                            {
                                tb.TotalCandidatures = DbValue.IntFromDb(r["TotalCandidatures"]);
                                tb.CandidaturesNouvelles = DbValue.IntFromDb(r["Nouvelles"]);
                                tb.CandidaturesEnEvaluation = DbValue.IntFromDb(r["EnEvaluation"]);
                                tb.CandidaturesRetenues = DbValue.IntFromDb(r["Retenues"]);
                                tb.CandidaturesEmbauchees = DbValue.IntFromDb(r["Embauchees"]);
                                tb.CandidaturesRejetees = DbValue.IntFromDb(r["Rejetees"]);
                            }
                        }
                    }

                    // Vivier et entretiens
                    using (var cmd = conn.CreateCommand())
                    {
                        cmd.CommandText = "SELECT COUNT(*) FROM RecrutementCandidats WHERE VivierActif = 1;";
                        tb.TotalVivier = Convert.ToInt32(cmd.ExecuteScalar());
                    }

                    using (var cmd = conn.CreateCommand())
                    {
                        cmd.CommandText = "SELECT COUNT(*) FROM RecrutementEntretiens;";
                        tb.TotalEntretiens = Convert.ToInt32(cmd.ExecuteScalar());
                    }
                }
            });

            return tb;
        }

        #endregion

        #region Méthodes Utilitaires & Mappages

        private static void VerifierCandidatureNonVerrouillee(SqliteConnection conn, int candidatureId)
        {
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = @"
                    SELECT Statut, SalarieCreeMatricule 
                    FROM RecrutementCandidatures 
                    WHERE Id = @id;";
                cmd.Parameters.AjouterValeur("@id", candidatureId);

                using (var reader = cmd.ExecuteReader())
                {
                    if (reader.Read())
                    {
                        int statut = DbValue.IntFromDb(reader["Statut"]);
                        string matricule = reader["SalarieCreeMatricule"] == DBNull.Value ? null : Convert.ToString(reader["SalarieCreeMatricule"]);

                        if (statut == 4 || !string.IsNullOrEmpty(matricule))
                        {
                            throw new InvalidOperationException(
                                "Opération interdite : cette candidature est déjà finalisée et embauchée avec un salarié actif en base. " +
                                "Ses paramètres sont verrouillés pour préserver l'intégrité probatoire des données.");
                        }
                    }
                }
            }
        }

        private static void RemplirParametresBesoin(SqliteCommand cmd, BesoinRecrutement b)
        {
            cmd.Parameters.AjouterValeur("@Reference", b.Reference ?? "");
            cmd.Parameters.AjouterValeur("@IntitulePoste", b.IntitulePoste ?? "");
            cmd.Parameters.AjouterValeur("@PosteId", (object)b.PosteId ?? DBNull.Value);
            cmd.Parameters.AjouterValeur("@Departement", b.Departement ?? "");
            cmd.Parameters.AjouterValeur("@Motif", (int)b.Motif);
            cmd.Parameters.AjouterValeur("@NombrePostes", b.NombrePostes);
            cmd.Parameters.AjouterValeur("@NombreRecrutes", b.NombreRecrutes);
            cmd.Parameters.AjouterValeur("@TypeContratVise", b.TypeContratVise ?? "CDI");
            cmd.Parameters.AjouterValeur("@RemunMin", b.RemunerationProposeeMin.HasValue ? (object)DbValue.MoneyToDb(b.RemunerationProposeeMin.Value) : DBNull.Value);
            cmd.Parameters.AjouterValeur("@RemunMax", b.RemunerationProposeeMax.HasValue ? (object)DbValue.MoneyToDb(b.RemunerationProposeeMax.Value) : DBNull.Value);
            cmd.Parameters.AjouterValeur("@IndLogement", DbValue.MoneyToDb(Math.Max(0m, b.IndemniteLogement)));
            cmd.Parameters.AjouterValeur("@IndTransport", DbValue.MoneyToDb(Math.Max(0m, b.IndemniteTransport)));
            cmd.Parameters.AjouterValeur("@IndFonction", DbValue.MoneyToDb(Math.Max(0m, b.IndemniteFonction)));
            cmd.Parameters.AjouterValeur("@IndSujetion", DbValue.MoneyToDb(Math.Max(0m, b.IndemniteSujetion)));
            cmd.Parameters.AjouterValeur("@DateExpression", DbValue.DateToDb(b.DateExpression));
            cmd.Parameters.AjouterValeur("@DateEcheance", DbValue.DateNullableToDb(b.DateEcheanceSouhaitee));
            cmd.Parameters.AjouterValeur("@DateCloture", DbValue.DateNullableToDb(b.DateCloture));
            cmd.Parameters.AjouterValeur("@Statut", (int)b.Statut);
            cmd.Parameters.AjouterValeur("@Responsable", b.ResponsableRecrutement ?? "");
            cmd.Parameters.AjouterValeur("@Description", b.DescriptionProfil ?? "");
        }

        private static void RemplirParametresCandidat(SqliteCommand cmd, Candidat c)
        {
            cmd.Parameters.AjouterValeur("@Nom", c.Nom?.Trim().ToUpperInvariant() ?? "");
            cmd.Parameters.AjouterValeur("@Prenoms", c.Prenoms?.Trim() ?? "");
            cmd.Parameters.AjouterValeur("@Email", c.Email?.Trim() ?? "");
            cmd.Parameters.AjouterValeur("@Telephone", c.Telephone?.Trim() ?? "");
            cmd.Parameters.AjouterValeur("@Diplome", c.DiplomeLePlusEleve ?? "");
            cmd.Parameters.AjouterValeur("@AnneesExperience", c.AnneesExperience);
            cmd.Parameters.AjouterValeur("@DerniereFonction", c.DerniereFonction ?? "");
            cmd.Parameters.AjouterValeur("@CompetencesCles", c.CompetencesCles ?? "");
            cmd.Parameters.AjouterValeur("@CvChemin", c.CvChemin ?? "");
            cmd.Parameters.AjouterValeur("@VivierActif", c.VivierActif ? 1 : 0);
            cmd.Parameters.AjouterValeur("@Commentaires", c.Commentaires ?? "");
        }

        /// <summary>
        /// Empêche la création (ou la mise à jour) d'un doublon dans le vivier : refuse
        /// l'enregistrement si un AUTRE profil possède déjà le même email ou le même
        /// téléphone (comparaison insensible à la casse et aux espaces superflus).
        /// Les candidats sans email ni téléphone renseigné ne sont pas comparés.
        /// </summary>
        private static void VerifierDoublonCandidat(SqliteConnection conn, Candidat candidat)
        {
            bool aEmail = !string.IsNullOrWhiteSpace(candidat.Email);
            bool aTelephone = !string.IsNullOrWhiteSpace(candidat.Telephone);
            if (!aEmail && !aTelephone) return;

            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = @"
                    SELECT Nom, Prenoms FROM RecrutementCandidats
                    WHERE Id != @Id
                      AND (
                            (@AEmail = 1 AND LOWER(TRIM(Email)) = LOWER(TRIM(@Email)))
                         OR (@ATelephone = 1 AND TRIM(Telephone) = TRIM(@Telephone))
                      )
                    LIMIT 1;";
                cmd.Parameters.AjouterValeur("@Id", candidat.Id);
                cmd.Parameters.AjouterValeur("@AEmail", aEmail ? 1 : 0);
                cmd.Parameters.AjouterValeur("@Email", candidat.Email ?? "");
                cmd.Parameters.AjouterValeur("@ATelephone", aTelephone ? 1 : 0);
                cmd.Parameters.AjouterValeur("@Telephone", candidat.Telephone ?? "");

                using (var reader = cmd.ExecuteReader())
                {
                    if (reader.Read())
                    {
                        string nomExistant = $"{reader["Nom"]} {reader["Prenoms"]}".Trim();
                        throw new InvalidOperationException(
                            $"Un profil existe déjà dans le vivier avec le même email ou le même téléphone : « {nomExistant} ». " +
                            "Vérifiez qu'il ne s'agit pas d'un doublon avant d'enregistrer un nouveau profil.");
                    }
                }
            }
        }

        // Sous-dossier (sous le dossier de données de l'application) dans lequel les CV
        // importés sont physiquement stockés, séparé par dossier/entreprise.
        private static readonly string[] ExtensionsCvAutorisees = { ".pdf", ".doc", ".docx" };
        private const long TailleMaxCvOctets = 15 * 1024 * 1024; // 15 Mo

        private static string DossierCvRecrutement()
        {
            string dossierId = DossierSession.Instance.CurrentDossier?.DossierId ?? "defaut";
            string dossier = Path.Combine(Constants.AppDataFolder, "CV_Recrutement", dossierId);
            Directory.CreateDirectory(dossier);
            return dossier;
        }

        /// <summary>
        /// Importe physiquement un fichier de CV (PDF/DOC/DOCX, 15 Mo max) dans le dossier
        /// de stockage géré par l'application, et met à jour <see cref="Candidat.CvChemin"/>
        /// en mémoire. L'appelant doit ensuite persister le profil (EnregistrerCandidatAsync)
        /// pour que le nouveau chemin soit sauvegardé. Si le candidat avait déjà un CV géré
        /// par l'application, l'ancien fichier physique est supprimé après la copie réussie
        /// du nouveau, pour éviter d'accumuler des fichiers orphelins sur le disque.
        /// </summary>
        public string ImporterCvCandidat(Candidat candidat, string cheminFichierSource)
        {
            if (candidat == null) throw new ArgumentNullException(nameof(candidat));
            if (string.IsNullOrWhiteSpace(cheminFichierSource) || !File.Exists(cheminFichierSource))
                throw new FileNotFoundException("Le fichier sélectionné est introuvable.", cheminFichierSource);

            string extension = Path.GetExtension(cheminFichierSource)?.ToLowerInvariant();
            if (string.IsNullOrEmpty(extension) || Array.IndexOf(ExtensionsCvAutorisees, extension) < 0)
                throw new ArgumentException("Seuls les fichiers PDF, DOC ou DOCX sont acceptés pour un CV.");

            if (new FileInfo(cheminFichierSource).Length > TailleMaxCvOctets)
                throw new ArgumentException("Le fichier dépasse la taille maximale autorisée de 15 Mo pour un CV.");

            string dossierCv = DossierCvRecrutement();
            string nomFichierCible = $"Candidat_{(candidat.Id > 0 ? candidat.Id.ToString() : "nouveau")}_{Guid.NewGuid():N}{extension}";
            string cheminCible = Path.Combine(dossierCv, nomFichierCible);

            File.Copy(cheminFichierSource, cheminCible, overwrite: true);

            string ancienChemin = candidat.CvChemin;
            candidat.CvChemin = cheminCible;

            SupprimerFichierCvGere(ancienChemin);

            return cheminCible;
        }

        /// <summary>
        /// Supprime physiquement un fichier de CV du disque, uniquement s'il se trouve bien
        /// dans le dossier géré par l'application (jamais un fichier arbitraire ailleurs sur
        /// le poste). Échoue silencieusement (fichier déjà absent, verrouillé, etc.) : le
        /// nettoyage du disque ne doit jamais faire échouer une opération de base de données.
        /// </summary>
        private static void SupprimerFichierCvGere(string chemin)
        {
            if (string.IsNullOrWhiteSpace(chemin)) return;
            try
            {
                string dossierGere = Path.GetFullPath(Path.Combine(Constants.AppDataFolder, "CV_Recrutement"));
                string cheminComplet = Path.GetFullPath(chemin);
                if (!cheminComplet.StartsWith(dossierGere, StringComparison.OrdinalIgnoreCase)) return;
                if (File.Exists(cheminComplet)) File.Delete(cheminComplet);
            }
            catch
            {
                // Best-effort : ne jamais bloquer une suppression ou un import pour une
                // erreur de nettoyage de fichier (verrouillé, déjà supprimé, permissions...).
            }
        }

        private static void RemplirParametresEntretien(SqliteCommand cmd, RecrutementEntretien e)
        {
            cmd.Parameters.AjouterValeur("@CandidatureId", e.CandidatureId);
            cmd.Parameters.AjouterValeur("@Etape", (int)e.Etape);
            cmd.Parameters.AjouterValeur("@DateEntretien", DbValue.DateToDb(e.DateEntretien));
            cmd.Parameters.AjouterValeur("@EvaluateurNom", e.EvaluateurNom ?? "");
            cmd.Parameters.AjouterValeur("@EvaluateurMatricule", (object)e.EvaluateurMatricule ?? DBNull.Value);
            cmd.Parameters.AjouterValeur("@NoteGlobale", (object)e.NoteGlobale ?? DBNull.Value);
            cmd.Parameters.AjouterValeur("@Avis", (object)(e.Avis.HasValue ? (int)e.Avis.Value : (object)DBNull.Value));
            cmd.Parameters.AjouterValeur("@PointsForts", e.PointsForts ?? "");
            cmd.Parameters.AjouterValeur("@AxesAmelioration", e.AxesAmelioration ?? "");
            cmd.Parameters.AjouterValeur("@Synthese", e.Synthese ?? "");
        }

        private static BesoinRecrutement LireBesoin(SqliteDataReader r)
        {
            return new BesoinRecrutement
            {
                Id = DbValue.IntFromDb(r["Id"]),
                Reference = Convert.ToString(r["Reference"]),
                IntitulePoste = Convert.ToString(r["IntitulePoste"]),
                PosteId = r["PosteId"] == DBNull.Value ? (int?)null : Convert.ToInt32(r["PosteId"]),
                Departement = Convert.ToString(r["Departement"]),
                Motif = (MotifRecrutement)DbValue.IntFromDb(r["Motif"]),
                NombrePostes = DbValue.IntFromDb(r["NombrePostes"]),
                NombreRecrutes = DbValue.IntFromDb(r["NombreRecrutes"]),
                TypeContratVise = Convert.ToString(r["TypeContratVise"]),
                RemunerationProposeeMin = DbValue.MoneyFromDb(r["RemunerationProposeeMin"]),
                RemunerationProposeeMax = DbValue.MoneyFromDb(r["RemunerationProposeeMax"]),
                IndemniteLogement = DbValue.MoneyFromDb(r["IndemniteLogement"]),
                IndemniteTransport = DbValue.MoneyFromDb(r["IndemniteTransport"]),
                IndemniteFonction = DbValue.MoneyFromDb(r["IndemniteFonction"]),
                IndemniteSujetion = DbValue.MoneyFromDb(r["IndemniteSujetion"]),
                DateExpression = DbValue.DateFromDb(r["DateExpression"], DateTime.Today),
                DateEcheanceSouhaitee = DbValue.DateNullableFromDb(r["DateEcheanceSouhaitee"]),
                DateCloture = DbValue.DateNullableFromDb(r["DateCloture"]),
                Statut = (StatutBesoinRecrutement)DbValue.IntFromDb(r["Statut"]),
                ResponsableRecrutement = Convert.ToString(r["ResponsableRecrutement"]),
                DescriptionProfil = Convert.ToString(r["DescriptionProfil"]),
                DateCreation = DbValue.DateFromDb(r["DateCreation"], DateTime.Now),
                PosteLibelle = r["PosteIntitule"] == DBNull.Value ? "" : Convert.ToString(r["PosteIntitule"]),
                NombreCandidatures = DbValue.IntFromDb(r["TotalCandidatures"]),
                NombreEnCours = DbValue.IntFromDb(r["TotalEnCours"])
            };
        }

        private static Candidat LireCandidat(SqliteDataReader r)
        {
            return new Candidat
            {
                Id = DbValue.IntFromDb(r["Id"]),
                Nom = Convert.ToString(r["Nom"]),
                Prenoms = Convert.ToString(r["Prenoms"]),
                Email = Convert.ToString(r["Email"]),
                Telephone = Convert.ToString(r["Telephone"]),
                DiplomeLePlusEleve = Convert.ToString(r["DiplomeLePlusEleve"]),
                AnneesExperience = DbValue.IntFromDb(r["AnneesExperience"]),
                DerniereFonction = Convert.ToString(r["DerniereFonction"]),
                CompetencesCles = Convert.ToString(r["CompetencesCles"]),
                CvChemin = Convert.ToString(r["CvChemin"]),
                VivierActif = DbValue.IntFromDb(r["VivierActif"]) == 1,
                Commentaires = Convert.ToString(r["Commentaires"]),
                DateCreation = DbValue.DateFromDb(r["DateCreation"], DateTime.Now),
                NombreCandidatures = DbValue.IntFromDb(r["NombreCandidatures"])
            };
        }

        private static Candidature LireCandidature(SqliteDataReader r)
        {
            return new Candidature
            {
                Id = DbValue.IntFromDb(r["Id"]),
                BesoinId = DbValue.IntFromDb(r["BesoinId"]),
                CandidatId = DbValue.IntFromDb(r["CandidatId"]),
                DatePostulation = DbValue.DateFromDb(r["DatePostulation"], DateTime.Today),
                CanalSource = (CanalSourceCandidature)DbValue.IntFromDb(r["CanalSource"]),
                PretentionSalariale = DbValue.MoneyFromDb(r["PretentionSalariale"]),
                EtapeActuelle = (EtapeRecrutement)DbValue.IntFromDb(r["EtapeActuelle"]),
                Statut = (StatutCandidature)DbValue.IntFromDb(r["Statut"]),
                ScoreEvaluation = r["ScoreEvaluation"] == DBNull.Value ? (double?)null : Convert.ToDouble(r["ScoreEvaluation"], CultureInfo.InvariantCulture),
                AvisGlobal = r["AvisGlobal"] == DBNull.Value ? (AvisRecruteur?)null : (AvisRecruteur)Convert.ToInt32(r["AvisGlobal"]),
                CommentaireEvaluation = Convert.ToString(r["CommentaireEvaluation"]),
                DateEmbauche = DbValue.DateNullableFromDb(r["DateEmbauche"]),
                SalaireNegocie = DbValue.MoneyFromDb(r["SalaireNegocie"]),
                SalarieCreeMatricule = r["SalarieCreeMatricule"] == DBNull.Value ? null : Convert.ToString(r["SalarieCreeMatricule"]),
                TypeContratEmbauche = DbValue.TextFromDb(r["TypeContratEmbauche"]),
                DateCreation = DbValue.DateFromDb(r["DateCreation"], DateTime.Now),
                CandidatNomComplet = $"{Convert.ToString(r["CandidatNom"])} {Convert.ToString(r["CandidatPrenoms"])}".Trim(),
                CandidatEmail = Convert.ToString(r["CandidatEmail"]),
                CandidatTelephone = Convert.ToString(r["CandidatTelephone"]),
                CandidatDiplome = Convert.ToString(r["CandidatDiplome"]),
                CandidatExperience = DbValue.IntFromDb(r["CandidatExperience"]),
                BesoinReference = Convert.ToString(r["BesoinReference"]),
                BesoinIntitulePoste = Convert.ToString(r["BesoinIntitule"]),
                BesoinDepartement = Convert.ToString(r["BesoinDepartement"]),
                BesoinPosteId = r["BesoinPosteId"] == DBNull.Value ? (int?)null : Convert.ToInt32(r["BesoinPosteId"])
            };
        }

        private static RecrutementEntretien LireEntretien(SqliteDataReader r)
        {
            return new RecrutementEntretien
            {
                Id = DbValue.IntFromDb(r["Id"]),
                CandidatureId = DbValue.IntFromDb(r["CandidatureId"]),
                Etape = (EtapeRecrutement)DbValue.IntFromDb(r["Etape"]),
                DateEntretien = DbValue.DateFromDb(r["DateEntretien"], DateTime.Today),
                EvaluateurNom = Convert.ToString(r["EvaluateurNom"]),
                EvaluateurMatricule = r["EvaluateurMatricule"] == DBNull.Value ? null : Convert.ToString(r["EvaluateurMatricule"]),
                NoteGlobale = r["NoteGlobale"] == DBNull.Value ? (double?)null : Convert.ToDouble(r["NoteGlobale"], CultureInfo.InvariantCulture),
                Avis = r["Avis"] == DBNull.Value ? (AvisRecruteur?)null : (AvisRecruteur)Convert.ToInt32(r["Avis"]),
                PointsForts = Convert.ToString(r["PointsForts"]),
                AxesAmelioration = Convert.ToString(r["AxesAmelioration"]),
                Synthese = Convert.ToString(r["Synthese"]),
                DateCreation = DbValue.DateFromDb(r["DateCreation"], DateTime.Now),
                CandidatNomComplet = Convert.ToString(r["CandidatNomComplet"]),
                BesoinIntitulePoste = Convert.ToString(r["BesoinIntitule"])
            };
        }

        #endregion
    }
}
