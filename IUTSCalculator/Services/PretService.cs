using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Linq;
using System.Threading.Tasks;
using IUTSCalculator.Models;

namespace IUTSCalculator.Services
{
    /// <summary>Une ligne de remboursement telle qu'elle figurera sur le bulletin.</summary>
    public sealed class LignePret
    {
        public LignePret(int pretId, string code, string libelle, decimal montant)
        {
            PretId = pretId;
            Code = code;
            Libelle = libelle;
            Montant = montant;
        }

        public int PretId { get; private set; }
        public string Code { get; private set; }
        public string Libelle { get; private set; }
        public decimal Montant { get; private set; }
    }

    /// <summary>
    /// Ce que les prêts d'un salarié retiennent sur son bulletin du mois (module PAI-05).
    ///
    /// La retenue porte sur le NET, après l'impôt et les cotisations : rembourser un prêt
    /// n'est pas perdre un salaire. Le salarié a bien perçu la somme — le mois où elle lui
    /// a été avancée — et elle a été imposée à ce titre. La retenir sur le brut lui ferait
    /// récupérer un impôt qu'il doit, et fausserait l'assiette CNSS déclarée à la Caisse.
    /// C'est exactement l'inverse du traitement d'une absence non rémunérée (PAI-04).
    ///
    /// <b>Équivalence stricte.</b> <see cref="Aucun"/> ne porte que des zéros : un dossier
    /// sans prêt produit exactement le bulletin qu'il produisait avant ce module.
    /// </summary>
    public sealed class ApportPrets
    {
        private readonly List<LignePret> _lignes = new List<LignePret>();

        private ApportPrets()
        {
        }

        public static ApportPrets Aucun()
        {
            return new ApportPrets();
        }

        /// <summary>Total retenu sur le net au titre des remboursements du mois.</summary>
        public decimal Retenue
        {
            get { return _lignes.Sum(l => l.Montant); }
        }

        /// <summary>
        /// Le détail, une ligne par prêt. Un salarié qui rembourse deux avances doit voir
        /// deux lignes : un total agrégé ne lui permettrait pas de vérifier laquelle des
        /// deux avance et de combien.
        /// </summary>
        public IReadOnlyList<LignePret> Lignes
        {
            get { return _lignes; }
        }

        public bool EstVide
        {
            get { return _lignes.Count == 0; }
        }

        internal void Ajouter(LignePret ligne)
        {
            if (ligne == null || ligne.Montant <= 0m) return;
            _lignes.Add(ligne);
        }
    }

    /// <summary>
    /// Prêts, avances et acomptes (module PAI-05).
    ///
    /// Avant ce module, un remboursement se saisissait à la main dans les variables du
    /// mois, tous les mois : rien ne tenait l'échéancier, rien ne disait le solde restant
    /// dû, et rien n'arrêtait la retenue après le dernier versement. L'oubli d'un mois
    /// comme la retenue de trop passaient inaperçus jusqu'à la réclamation du salarié.
    ///
    /// <b>Ce qui fait foi.</b> Une échéance n'a pas d'état « payée » : elle est réputée
    /// retenue quand la période à laquelle elle se rapporte est CLÔTURÉE. C'est la règle
    /// déjà retenue pour les totaux de période — ce qui est arrêté est acquis, ce qui est
    /// ouvert se recalcule. Le solde restant dû en découle, et une période rouverte le
    /// remet automatiquement dans l'état où il était.
    ///
    /// <b>Plafond.</b> La retenue d'un mois ne peut jamais dépasser ce qui reste dû. Un
    /// échéancier mal saisi, ou une période rejouée, ne peut donc pas récupérer plus que
    /// la somme avancée.
    /// </summary>
    public class PretService
    {
        private readonly SQLiteService _sqliteService;

        private const string ColonnesPret =
            "p.Id, p.EmployeeId, p.Type, p.Reference, p.DateOctroi, p.MontantAccorde, " +
            "p.MontantEcheance, p.NombreEcheances, p.AnneePremiereEcheance, " +
            "p.MoisPremiereEcheance, p.Statut, p.Motif, p.Commentaire, p.DateSaisie";

        public PretService(SQLiteService sqliteService)
        {
            if (sqliteService == null) throw new ArgumentNullException("sqliteService");
            _sqliteService = sqliteService;
        }

        // ── Lecture ──────────────────────────────────────────────────────────

        /// <summary>
        /// Tous les prêts du dossier, du plus récent au plus ancien. La jointure sur la
        /// fiche salarié est EXTERNE : un salarié retiré du dossier ne doit pas faire
        /// disparaître la créance que l'entreprise détient encore sur lui.
        /// </summary>
        public async Task<List<Pret>> ChargerAsync()
        {
            var liste = new List<Pret>();

            using (var conn = _sqliteService.CreateConnection())
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText =
                    "SELECT " + ColonnesPret + ", e.FirstName, e.LastName " +
                    "FROM Prets p " +
                    "LEFT JOIN Employees e ON e.EmployeeId = p.EmployeeId " +
                    "ORDER BY p.DateOctroi DESC, p.Id DESC";

                using (var reader = (SQLiteDataReader)await cmd.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                        liste.Add(LirePret(reader));
                }
            }

            return liste;
        }

        private static Pret LirePret(SQLiteDataReader reader)
        {
            string nom = DbValue.TextFromDb(reader["LastName"]);
            string prenom = DbValue.TextFromDb(reader["FirstName"]);
            string complet = string.Format("{0} {1}", nom, prenom).Trim();

            return new Pret
            {
                Id = DbValue.IntFromDb(reader["Id"]),
                EmployeeId = DbValue.TextFromDb(reader["EmployeeId"]),
                NomSalarie = string.IsNullOrWhiteSpace(complet) ? "Salarié supprimé" : complet,
                Type = (TypePret)DbValue.IntFromDb(reader["Type"]),
                Reference = DbValue.TextFromDb(reader["Reference"]),
                DateOctroi = DbValue.DateFromDb(reader["DateOctroi"], DateTime.Today),
                MontantAccorde = DbValue.MoneyFromDb(reader["MontantAccorde"]),
                MontantEcheance = DbValue.MoneyFromDb(reader["MontantEcheance"]),
                NombreEcheances = DbValue.IntFromDb(reader["NombreEcheances"]),
                AnneePremiereEcheance = DbValue.IntFromDb(reader["AnneePremiereEcheance"]),
                MoisPremiereEcheance = DbValue.IntFromDb(reader["MoisPremiereEcheance"]),
                Statut = (StatutPret)DbValue.IntFromDb(reader["Statut"]),
                Motif = DbValue.TextFromDb(reader["Motif"]),
                Commentaire = DbValue.TextFromDb(reader["Commentaire"]),
                DateSaisie = DbValue.DateFromDb(reader["DateSaisie"], DateTime.Now)
            };
        }

        /// <summary>
        /// Échéancier d'un prêt, chaque échéance sachant si son mois est arrêté. Sans
        /// cette information, l'écran ne pourrait pas distinguer ce qui a été retenu de
        /// ce qui est seulement prévu.
        /// </summary>
        public async Task<List<EcheancePret>> ChargerEcheancesAsync(int pretId)
        {
            var liste = new List<EcheancePret>();
            if (pretId <= 0) return liste;

            using (var conn = _sqliteService.CreateConnection())
            {
                var cloturees = await ChargerPeriodesClotureesAsync(conn, null);

                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText =
                        "SELECT Id, PretId, Rang, Annee, Mois, MontantPrevu, Statut, Commentaire " +
                        "FROM EcheancesPret WHERE PretId = @pret ORDER BY Annee, Mois, Rang;";
                    cmd.Parameters.AddWithValue("@pret", pretId);

                    using (var reader = (SQLiteDataReader)await cmd.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            var echeance = LireEcheance(reader);
                            echeance.PeriodeCloturee = cloturees.Contains(Cle(echeance.Annee, echeance.Mois));
                            liste.Add(echeance);
                        }
                    }
                }
            }

            return liste;
        }

        private static EcheancePret LireEcheance(SQLiteDataReader reader)
        {
            return new EcheancePret
            {
                Id = DbValue.IntFromDb(reader["Id"]),
                PretId = DbValue.IntFromDb(reader["PretId"]),
                Rang = DbValue.IntFromDb(reader["Rang"]),
                Annee = DbValue.IntFromDb(reader["Annee"]),
                Mois = DbValue.IntFromDb(reader["Mois"]),
                MontantPrevu = DbValue.MoneyFromDb(reader["MontantPrevu"]),
                Statut = (StatutEcheance)DbValue.IntFromDb(reader["Statut"]),
                Commentaire = DbValue.TextFromDb(reader["Commentaire"])
            };
        }

        // ── Apport au bulletin ───────────────────────────────────────────────

        /// <summary>
        /// Remboursements de TOUS les salariés sur une période, en une seule lecture.
        /// À utiliser pour un traitement en lot : appeler <see cref="ApportAsync"/> dans
        /// une boucle relirait la table autant de fois qu'il y a de bulletins.
        /// </summary>
        public async Task<Dictionary<string, ApportPrets>> ApportsAsync(int annee, int mois)
        {
            var resultat = new Dictionary<string, ApportPrets>(StringComparer.OrdinalIgnoreCase);

            if (annee <= 0 || mois < 1 || mois > 12)
                return resultat;

            var lignes = new List<EcheanceAvecPret>();

            using (var conn = _sqliteService.CreateConnection())
            {
                var cloturees = await ChargerPeriodesClotureesAsync(conn, null);

                using (var cmd = conn.CreateCommand())
                {
                    // Toutes les échéances des prêts EN COURS, pas seulement celles du mois
                    // demandé : le plafond du mois dépend de ce qui a déjà été retenu, donc
                    // de l'échéancier antérieur.
                    cmd.CommandText =
                        "SELECT e.Annee, e.Mois, e.MontantPrevu, e.Statut AS StatutEcheance, " +
                        "       p.Id AS PretId, p.EmployeeId, p.Type, p.Reference, " +
                        "       p.MontantAccorde, p.Statut AS StatutPret " +
                        "FROM EcheancesPret e " +
                        "INNER JOIN Prets p ON p.Id = e.PretId " +
                        "WHERE p.Statut = @enCours " +
                        "ORDER BY p.Id, e.Annee, e.Mois, e.Rang;";
                    cmd.Parameters.AddWithValue("@enCours", (int)StatutPret.EnCours);

                    using (var reader = (SQLiteDataReader)await cmd.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            lignes.Add(new EcheanceAvecPret
                            {
                                PretId = DbValue.IntFromDb(reader["PretId"]),
                                EmployeeId = DbValue.TextFromDb(reader["EmployeeId"]),
                                Type = (TypePret)DbValue.IntFromDb(reader["Type"]),
                                Reference = DbValue.TextFromDb(reader["Reference"]),
                                MontantAccorde = DbValue.MoneyFromDb(reader["MontantAccorde"]),
                                Annee = DbValue.IntFromDb(reader["Annee"]),
                                Mois = DbValue.IntFromDb(reader["Mois"]),
                                MontantPrevu = DbValue.MoneyFromDb(reader["MontantPrevu"]),
                                Statut = (StatutEcheance)DbValue.IntFromDb(reader["StatutEcheance"])
                            });
                        }
                    }
                }

                foreach (var ligne in lignes)
                    ligne.PeriodeCloturee = cloturees.Contains(Cle(ligne.Annee, ligne.Mois));
            }

            int cleMois = Cle(annee, mois);

            foreach (var parPret in lignes.GroupBy(l => l.PretId))
            {
                var echeances = parPret.ToList();
                var premiere = echeances[0];

                if (string.IsNullOrWhiteSpace(premiere.EmployeeId))
                    continue;

                decimal duMois = echeances
                    .Where(e => e.Statut == StatutEcheance.Prevue && Cle(e.Annee, e.Mois) == cleMois)
                    .Sum(e => e.MontantPrevu);

                if (duMois <= 0m) continue;

                // Ce qui a déjà été retenu AVANT ce mois-ci. L'échéance du mois traité en
                // est exclue, même si sa période est clôturée : sinon un mois rejoué se
                // verrait opposer sa propre retenue comme plafond, et ne retiendrait rien.
                decimal dejaRetenu = echeances
                    .Where(e => e.Statut == StatutEcheance.Prevue
                             && e.PeriodeCloturee
                             && Cle(e.Annee, e.Mois) < cleMois)
                    .Sum(e => e.MontantPrevu);

                decimal restantDu = premiere.MontantAccorde - dejaRetenu;
                if (restantDu <= 0m) continue;

                decimal retenue = duMois > restantDu ? restantDu : duMois;
                if (retenue <= 0m) continue;

                ApportPrets apport;
                if (!resultat.TryGetValue(premiere.EmployeeId, out apport))
                {
                    apport = ApportPrets.Aucun();
                    resultat[premiere.EmployeeId] = apport;
                }

                apport.Ajouter(new LignePret(
                    premiere.PretId,
                    Pret.CodeRetenue(premiere.Type),
                    LibelleRetenue(premiere.Type, premiere.Reference),
                    retenue));
            }

            return resultat;
        }

        /// <summary>Remboursements d'un seul salarié sur une période.</summary>
        public async Task<ApportPrets> ApportAsync(string employeeId, int annee, int mois)
        {
            if (string.IsNullOrWhiteSpace(employeeId))
                return ApportPrets.Aucun();

            var tous = await ApportsAsync(annee, mois);

            ApportPrets apport;
            return tous.TryGetValue(employeeId, out apport) ? apport : ApportPrets.Aucun();
        }

        /// <summary>Apport d'un salarié dans un lot déjà chargé, ou un apport vide.</summary>
        public static ApportPrets ApportDe(Dictionary<string, ApportPrets> apports, string employeeId)
        {
            ApportPrets apport;
            if (apports != null && !string.IsNullOrWhiteSpace(employeeId)
                && apports.TryGetValue(employeeId, out apport))
            {
                return apport;
            }

            return ApportPrets.Aucun();
        }

        private static string LibelleRetenue(TypePret type, string reference)
        {
            string libelle = "Remboursement " + Pret.Libelle(type).ToLowerInvariant();

            return string.IsNullOrWhiteSpace(reference)
                ? libelle
                : string.Format("{0} ({1})", libelle, reference.Trim());
        }

        // ── Situation des prêts ──────────────────────────────────────────────

        /// <summary>
        /// Situation de tous les prêts à la période consultée : accordé, retenu, restant
        /// dû, avancement de l'échéancier et retenue du mois.
        /// </summary>
        public async Task<List<SituationPret>> SituationsAsync(int annee, int mois)
        {
            var prets = await ChargerAsync();
            if (prets.Count == 0) return new List<SituationPret>();

            var apports = await ApportsAsync(annee, mois);
            var situations = new List<SituationPret>();

            using (var conn = _sqliteService.CreateConnection())
            {
                var cloturees = await ChargerPeriodesClotureesAsync(conn, null);
                var parPret = await ChargerToutesEcheancesAsync(conn, cloturees);

                foreach (var pret in prets)
                {
                    List<EcheancePret> echeances;
                    if (!parPret.TryGetValue(pret.Id, out echeances))
                        echeances = new List<EcheancePret>();

                    var situation = new SituationPret
                    {
                        PretId = pret.Id,
                        EmployeeId = pret.EmployeeId,
                        NomSalarie = pret.NomSalarie,
                        Type = pret.Type,
                        Statut = pret.Statut,
                        Reference = pret.Reference,
                        MontantAccorde = pret.MontantAccorde,
                        MontantEcheance = pret.MontantEcheance,
                        EcheancesTotal = echeances.Count(e => e.Statut != StatutEcheance.Annulee),
                        EcheancesRetenues = echeances.Count(e => e.EstRetenue),
                        TotalRetenu = echeances.Where(e => e.EstRetenue).Sum(e => e.MontantPrevu),
                        TotalAVenir = echeances.Where(e => e.EstAVenir).Sum(e => e.MontantPrevu)
                    };

                    var prochaine = echeances
                        .Where(e => e.EstAVenir)
                        .OrderBy(e => e.Annee).ThenBy(e => e.Mois)
                        .FirstOrDefault();

                    situation.ProchaineEcheance = prochaine != null ? prochaine.PeriodeLibelle : "—";

                    var apport = ApportDe(apports, pret.EmployeeId);
                    situation.RetenueDuMois = apport.Lignes
                        .Where(l => l.PretId == pret.Id)
                        .Sum(l => l.Montant);

                    situations.Add(situation);
                }
            }

            return situations
                .OrderBy(s => s.EstSolde)
                .ThenBy(s => s.NomSalarie)
                .ToList();
        }

        private static async Task<Dictionary<int, List<EcheancePret>>> ChargerToutesEcheancesAsync(
            SQLiteConnection conn, HashSet<int> cloturees)
        {
            var parPret = new Dictionary<int, List<EcheancePret>>();

            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText =
                    "SELECT Id, PretId, Rang, Annee, Mois, MontantPrevu, Statut, Commentaire " +
                    "FROM EcheancesPret ORDER BY PretId, Annee, Mois, Rang;";

                using (var reader = (SQLiteDataReader)await cmd.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                    {
                        var echeance = LireEcheance(reader);
                        echeance.PeriodeCloturee = cloturees.Contains(Cle(echeance.Annee, echeance.Mois));

                        List<EcheancePret> liste;
                        if (!parPret.TryGetValue(echeance.PretId, out liste))
                        {
                            liste = new List<EcheancePret>();
                            parPret[echeance.PretId] = liste;
                        }

                        liste.Add(echeance);
                    }
                }
            }

            return parPret;
        }

        // ── Écriture ─────────────────────────────────────────────────────────

        /// <summary>
        /// Enregistre un prêt et (re)construit son échéancier.
        ///
        /// Les échéances déjà RETENUES — celles dont le mois est clôturé — ne sont jamais
        /// touchées : elles figurent sur des bulletins remis et des déclarations déposées.
        /// Seul le reliquat est replanifié, à partir du premier mois encore ouvert.
        /// </summary>
        public async Task EnregistrerAsync(Pret pret)
        {
            if (pret == null) throw new ArgumentNullException("pret");

            var anomalie = Anomalie(pret);
            if (anomalie != null) throw new InvalidOperationException(anomalie);

            using (var conn = _sqliteService.CreateConnection())
            using (var tx = conn.BeginTransaction())
            {
                try
                {
                    if (pret.Id > 0)
                    {
                        using (var cmd = conn.CreateCommand())
                        {
                            cmd.Transaction = tx;
                            cmd.CommandText =
                                "UPDATE Prets SET EmployeeId = @employe, Type = @type, " +
                                "Reference = @reference, DateOctroi = @octroi, " +
                                "MontantAccorde = @accorde, MontantEcheance = @echeance, " +
                                "NombreEcheances = @nombre, AnneePremiereEcheance = @annee, " +
                                "MoisPremiereEcheance = @mois, Statut = @statut, " +
                                "Motif = @motif, Commentaire = @commentaire " +
                                "WHERE Id = @id;";
                            cmd.Parameters.AddWithValue("@id", pret.Id);
                            AjouterParametres(cmd, pret);
                            await cmd.ExecuteNonQueryAsync();
                        }
                    }
                    else
                    {
                        using (var cmd = conn.CreateCommand())
                        {
                            cmd.Transaction = tx;
                            cmd.CommandText =
                                "INSERT INTO Prets " +
                                "(EmployeeId, Type, Reference, DateOctroi, MontantAccorde, " +
                                " MontantEcheance, NombreEcheances, AnneePremiereEcheance, " +
                                " MoisPremiereEcheance, Statut, Motif, Commentaire, DateSaisie) " +
                                "VALUES (@employe, @type, @reference, @octroi, @accorde, " +
                                "        @echeance, @nombre, @annee, @mois, @statut, @motif, " +
                                "        @commentaire, @saisie);";
                            AjouterParametres(cmd, pret);
                            cmd.Parameters.AddWithValue("@saisie", DbValue.DateToDb(DateTime.Now));
                            await cmd.ExecuteNonQueryAsync();
                        }

                        // Identifiant relu par une commande distincte plutôt qu'en second
                        // ordre de la même : last_insert_rowid() porte sur la connexion, et
                        // la lire séparément ne laisse aucune ambiguïté sur ce qui est
                        // renvoyé par un lot de deux instructions.
                        using (var cmd = conn.CreateCommand())
                        {
                            cmd.Transaction = tx;
                            cmd.CommandText = "SELECT last_insert_rowid();";
                            object id = await cmd.ExecuteScalarAsync();
                            pret.Id = DbValue.IntFromDb(id);
                        }
                    }

                    await ReconstruireEcheancierAsync(conn, tx, pret, null, null);

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

        private static void AjouterParametres(SQLiteCommand cmd, Pret pret)
        {
            cmd.Parameters.AddWithValue("@employe", pret.EmployeeId ?? "");
            cmd.Parameters.AddWithValue("@type", (int)pret.Type);
            cmd.Parameters.AddWithValue("@reference", pret.Reference ?? "");
            cmd.Parameters.AddWithValue("@octroi", DbValue.DateToDb(pret.DateOctroi));
            cmd.Parameters.AddWithValue("@accorde", DbValue.MoneyToDb(pret.MontantAccorde));
            cmd.Parameters.AddWithValue("@echeance", DbValue.MoneyToDb(pret.MontantEcheance));
            cmd.Parameters.AddWithValue("@nombre", pret.NombreEcheances);
            cmd.Parameters.AddWithValue("@annee", pret.AnneePremiereEcheance);
            cmd.Parameters.AddWithValue("@mois", pret.MoisPremiereEcheance);
            cmd.Parameters.AddWithValue("@statut", (int)pret.Statut);
            cmd.Parameters.AddWithValue("@motif", pret.Motif ?? "");
            cmd.Parameters.AddWithValue("@commentaire", pret.Commentaire ?? "");
        }

        /// <summary>
        /// Supprime un prêt et son échéancier. Refusé dès qu'une échéance a été retenue :
        /// la retenue figure sur un bulletin remis au salarié, et effacer la créance sans
        /// laisser de trace priverait le dossier de la pièce qui l'explique. Le geste
        /// prévu dans ce cas est l'annulation, qui conserve l'historique.
        /// </summary>
        public async Task SupprimerAsync(Pret pret)
        {
            if (pret == null || pret.Id <= 0) return;

            using (var conn = _sqliteService.CreateConnection())
            {
                var cloturees = await ChargerPeriodesClotureesAsync(conn, null);
                var echeances = await ChargerEcheancesDuPretAsync(conn, null, pret.Id, cloturees);

                if (echeances.Any(e => e.EstRetenue))
                {
                    throw new InvalidOperationException(
                        "Ce prêt a déjà donné lieu à des retenues sur des périodes clôturées : " +
                        "il ne peut plus être supprimé. Annulez-le pour arrêter les retenues à " +
                        "venir en conservant l'historique.");
                }

                using (var tx = conn.BeginTransaction())
                {
                    try
                    {
                        using (var cmd = conn.CreateCommand())
                        {
                            cmd.Transaction = tx;
                            cmd.CommandText = "DELETE FROM EcheancesPret WHERE PretId = @id;";
                            cmd.Parameters.AddWithValue("@id", pret.Id);
                            await cmd.ExecuteNonQueryAsync();
                        }

                        using (var cmd = conn.CreateCommand())
                        {
                            cmd.Transaction = tx;
                            cmd.CommandText = "DELETE FROM Prets WHERE Id = @id;";
                            cmd.Parameters.AddWithValue("@id", pret.Id);
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
        /// Interrompt le remboursement : le prêt passe en « suspendu » et toutes les
        /// échéances des mois encore ouverts sont neutralisées. Le solde restant dû ne
        /// bouge plus — c'est bien le but — et la reprise le replanifiera.
        /// </summary>
        public async Task SuspendreAsync(Pret pret, string motif)
        {
            if (pret == null || pret.Id <= 0) return;

            using (var conn = _sqliteService.CreateConnection())
            using (var tx = conn.BeginTransaction())
            {
                try
                {
                    var cloturees = await ChargerPeriodesClotureesAsync(conn, tx);
                    var echeances = await ChargerEcheancesDuPretAsync(conn, tx, pret.Id, cloturees);

                    foreach (var echeance in echeances.Where(e => e.EstAVenir))
                    {
                        using (var cmd = conn.CreateCommand())
                        {
                            cmd.Transaction = tx;
                            cmd.CommandText =
                                "UPDATE EcheancesPret SET Statut = @statut WHERE Id = @id;";
                            cmd.Parameters.AddWithValue("@statut", (int)StatutEcheance.Suspendue);
                            cmd.Parameters.AddWithValue("@id", echeance.Id);
                            await cmd.ExecuteNonQueryAsync();
                        }
                    }

                    string trace = string.IsNullOrWhiteSpace(motif)
                        ? "Suspendu, motif non précisé"
                        : "Suspendu — " + motif.Trim();

                    using (var cmd = conn.CreateCommand())
                    {
                        cmd.Transaction = tx;
                        cmd.CommandText =
                            "UPDATE Prets SET Statut = @statut, Motif = @motif WHERE Id = @id;";
                        cmd.Parameters.AddWithValue("@statut", (int)StatutPret.Suspendu);
                        cmd.Parameters.AddWithValue("@motif", trace);
                        cmd.Parameters.AddWithValue("@id", pret.Id);
                        await cmd.ExecuteNonQueryAsync();
                    }

                    tx.Commit();

                    pret.Statut = StatutPret.Suspendu;
                    pret.Motif = trace;
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
        /// Reprend un remboursement suspendu à partir du mois indiqué. Le reliquat est
        /// réétalé avec la même échéance mensuelle : une suspension de trois mois décale
        /// la fin du prêt de trois mois, elle n'augmente pas la retenue.
        /// </summary>
        public async Task ReprendreAsync(Pret pret, int annee, int mois)
        {
            if (pret == null || pret.Id <= 0) return;

            if (mois < 1 || mois > 12)
                throw new InvalidOperationException("Le mois de reprise doit être compris entre 1 et 12.");

            using (var conn = _sqliteService.CreateConnection())
            using (var tx = conn.BeginTransaction())
            {
                try
                {
                    using (var cmd = conn.CreateCommand())
                    {
                        cmd.Transaction = tx;
                        cmd.CommandText = "UPDATE Prets SET Statut = @statut WHERE Id = @id;";
                        cmd.Parameters.AddWithValue("@statut", (int)StatutPret.EnCours);
                        cmd.Parameters.AddWithValue("@id", pret.Id);
                        await cmd.ExecuteNonQueryAsync();
                    }

                    pret.Statut = StatutPret.EnCours;

                    await ReconstruireEcheancierAsync(conn, tx, pret, annee, mois);

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
        /// Arrête définitivement un prêt sans effacer son histoire : les retenues déjà
        /// opérées restent, les échéances à venir sont annulées. C'est le geste de
        /// l'abandon de créance et du départ du salarié.
        /// </summary>
        public async Task AnnulerAsync(Pret pret, string motif)
        {
            if (pret == null || pret.Id <= 0) return;

            using (var conn = _sqliteService.CreateConnection())
            using (var tx = conn.BeginTransaction())
            {
                try
                {
                    var cloturees = await ChargerPeriodesClotureesAsync(conn, tx);
                    var echeances = await ChargerEcheancesDuPretAsync(conn, tx, pret.Id, cloturees);

                    foreach (var echeance in echeances.Where(e => e.EstAVenir || e.Statut == StatutEcheance.Suspendue))
                    {
                        using (var cmd = conn.CreateCommand())
                        {
                            cmd.Transaction = tx;
                            cmd.CommandText = "UPDATE EcheancesPret SET Statut = @statut WHERE Id = @id;";
                            cmd.Parameters.AddWithValue("@statut", (int)StatutEcheance.Annulee);
                            cmd.Parameters.AddWithValue("@id", echeance.Id);
                            await cmd.ExecuteNonQueryAsync();
                        }
                    }

                    string trace = string.IsNullOrWhiteSpace(motif)
                        ? "Annulé, motif non précisé"
                        : "Annulé — " + motif.Trim();

                    using (var cmd = conn.CreateCommand())
                    {
                        cmd.Transaction = tx;
                        cmd.CommandText = "UPDATE Prets SET Statut = @statut, Motif = @motif WHERE Id = @id;";
                        cmd.Parameters.AddWithValue("@statut", (int)StatutPret.Annule);
                        cmd.Parameters.AddWithValue("@motif", trace);
                        cmd.Parameters.AddWithValue("@id", pret.Id);
                        await cmd.ExecuteNonQueryAsync();
                    }

                    tx.Commit();

                    pret.Statut = StatutPret.Annule;
                    pret.Motif = trace;
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
        /// Replanifie la part non encore retenue de l'échéancier.
        ///
        /// Le point de départ est le plus tardif de trois mois : celui que le prêt
        /// annonce, celui qui suit la dernière retenue opérée, et celui demandé par une
        /// reprise. Il est ensuite avancé tant qu'il tombe sur une période clôturée : une
        /// échéance posée sur un mois arrêté ne serait retenue sur aucun bulletin, et le
        /// solde ne se solderait jamais.
        /// </summary>
        private static async Task ReconstruireEcheancierAsync(
            SQLiteConnection conn, SQLiteTransaction tx, Pret pret, int? anneeReprise, int? moisReprise)
        {
            var cloturees = await ChargerPeriodesClotureesAsync(conn, tx);
            var existantes = await ChargerEcheancesDuPretAsync(conn, tx, pret.Id, cloturees);

            var retenues = existantes.Where(e => e.EstRetenue).ToList();
            decimal dejaRetenu = retenues.Sum(e => e.MontantPrevu);

            // Tout ce qui n'a pas encore été retenu est effacé puis reconstruit : c'est
            // plus sûr que de corriger ligne à ligne un échéancier dont le montant, le
            // nombre d'échéances ou le mois de départ viennent peut-être de changer.
            using (var cmd = conn.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText =
                    "DELETE FROM EcheancesPret WHERE PretId = @pret AND Id NOT IN (" +
                    (retenues.Count == 0
                        ? "SELECT 0"
                        : string.Join(",", retenues.Select(e => e.Id.ToString()))) + ");";
                cmd.Parameters.AddWithValue("@pret", pret.Id);
                await cmd.ExecuteNonQueryAsync();
            }

            if (pret.Statut == StatutPret.Annule)
                return;

            decimal reste = pret.MontantAccorde - dejaRetenu;
            if (reste <= 0m)
                return;

            var depart = new DateTime(
                pret.AnneePremiereEcheance < 1 ? DateTime.Today.Year : pret.AnneePremiereEcheance,
                pret.MoisPremiereEcheance < 1 || pret.MoisPremiereEcheance > 12
                    ? DateTime.Today.Month
                    : pret.MoisPremiereEcheance,
                1);

            if (retenues.Count > 0)
            {
                var derniere = retenues
                    .OrderBy(e => e.Annee).ThenBy(e => e.Mois)
                    .Last();

                var suivant = new DateTime(derniere.Annee, derniere.Mois, 1).AddMonths(1);
                if (suivant > depart) depart = suivant;
            }

            if (anneeReprise.HasValue && moisReprise.HasValue)
            {
                var demande = new DateTime(anneeReprise.Value, moisReprise.Value, 1);
                if (demande > depart) depart = demande;
            }

            int garde = 0;
            while (cloturees.Contains(Cle(depart.Year, depart.Month)) && garde < 240)
            {
                depart = depart.AddMonths(1);
                garde++;
            }

            int restantes = pret.NombreEcheances - retenues.Count;
            if (restantes <= 0)
            {
                // L'échéancier prévu est épuisé alors qu'il reste un solde : une échéance
                // de solde est ajoutée plutôt que d'abandonner la créance en silence.
                restantes = 1;
            }

            var echeancier = Pret.ConstruireEcheancier(
                reste, pret.MontantEcheance, restantes,
                depart.Year, depart.Month, retenues.Count + 1);

            foreach (var echeance in echeancier)
            {
                using (var cmd = conn.CreateCommand())
                {
                    cmd.Transaction = tx;
                    cmd.CommandText =
                        "INSERT INTO EcheancesPret " +
                        "(PretId, Rang, Annee, Mois, MontantPrevu, Statut, Commentaire) " +
                        "VALUES (@pret, @rang, @annee, @mois, @montant, @statut, @commentaire);";
                    cmd.Parameters.AddWithValue("@pret", pret.Id);
                    cmd.Parameters.AddWithValue("@rang", echeance.Rang);
                    cmd.Parameters.AddWithValue("@annee", echeance.Annee);
                    cmd.Parameters.AddWithValue("@mois", echeance.Mois);
                    cmd.Parameters.AddWithValue("@montant", DbValue.MoneyToDb(echeance.MontantPrevu));
                    cmd.Parameters.AddWithValue("@statut", (int)StatutEcheance.Prevue);
                    cmd.Parameters.AddWithValue("@commentaire", echeance.Commentaire ?? "");
                    await cmd.ExecuteNonQueryAsync();
                }
            }
        }

        private static async Task<List<EcheancePret>> ChargerEcheancesDuPretAsync(
            SQLiteConnection conn, SQLiteTransaction tx, int pretId, HashSet<int> cloturees)
        {
            var liste = new List<EcheancePret>();

            using (var cmd = conn.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText =
                    "SELECT Id, PretId, Rang, Annee, Mois, MontantPrevu, Statut, Commentaire " +
                    "FROM EcheancesPret WHERE PretId = @pret ORDER BY Annee, Mois, Rang;";
                cmd.Parameters.AddWithValue("@pret", pretId);

                using (var reader = (SQLiteDataReader)await cmd.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                    {
                        var echeance = LireEcheance(reader);
                        echeance.PeriodeCloturee = cloturees.Contains(Cle(echeance.Annee, echeance.Mois));
                        liste.Add(echeance);
                    }
                }
            }

            return liste;
        }

        /// <summary>
        /// Périodes arrêtées du dossier, sous la forme d'un jeu de clés « année × 100 +
        /// mois ». Lues une fois par opération : interroger la table période par période
        /// multiplierait les allers-retours pour une information qui tient en mémoire.
        /// </summary>
        private static async Task<HashSet<int>> ChargerPeriodesClotureesAsync(
            SQLiteConnection conn, SQLiteTransaction tx)
        {
            var cles = new HashSet<int>();

            try
            {
                using (var cmd = conn.CreateCommand())
                {
                    cmd.Transaction = tx;
                    cmd.CommandText =
                        "SELECT Annee, Mois FROM PeriodesPaie WHERE Statut = @cloturee;";
                    cmd.Parameters.AddWithValue("@cloturee", (int)StatutPeriode.Cloturee);

                    using (var reader = (SQLiteDataReader)await cmd.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            cles.Add(Cle(
                                DbValue.IntFromDb(reader["Annee"]),
                                DbValue.IntFromDb(reader["Mois"])));
                        }
                    }
                }
            }
            catch
            {
                // Dossier antérieur aux périodes de paie : aucune période n'est arrêtée,
                // donc rien n'est encore réputé retenu. Mieux vaut un solde prudent qu'une
                // exception au chargement de l'écran.
            }

            return cles;
        }

        private static int Cle(int annee, int mois)
        {
            return annee * 100 + mois;
        }

        // ── Contrôles ────────────────────────────────────────────────────────

        /// <summary>Ce qui empêche d'enregistrer, en une phrase. Null quand tout est correct.</summary>
        public static string Anomalie(Pret pret)
        {
            if (pret == null) return "Prêt vide.";

            if (string.IsNullOrWhiteSpace(pret.EmployeeId))
                return "Choisissez le salarié bénéficiaire.";

            if (pret.MontantAccorde <= 0m)
                return "Indiquez le montant accordé.";

            if (pret.NombreEcheances <= 0)
                return "Le remboursement compte au moins une échéance.";

            if (pret.MontantEcheance < 0m)
                return "Une échéance ne peut pas être négative.";

            if (pret.MoisPremiereEcheance < 1 || pret.MoisPremiereEcheance > 12)
                return "Le mois de la première échéance doit être compris entre 1 et 12.";

            if (pret.AnneePremiereEcheance < 2000)
                return "L'année de la première échéance est invalide.";

            if (pret.MontantEcheance > pret.MontantAccorde)
                return "L'échéance mensuelle dépasse le montant accordé.";

            return null;
        }

        /// <summary>
        /// Ce qui mérite d'être signalé sans empêcher d'enregistrer.
        ///
        /// Le seuil du tiers de la rémunération permanente est un repère de PRUDENCE, pas
        /// une règle de droit : la quotité cessible et saisissable du salaire relève des
        /// saisies-arrêts (module PAI-06), qui la calculera par tranches. Ici, il s'agit
        /// seulement d'attirer l'œil sur une retenue qui laissera peu de chose à vivre.
        /// </summary>
        public static List<string> Avertissements(
            IEnumerable<SituationPret> situations, IEnumerable<Employee> salaries)
        {
            var messages = new List<string>();

            var parMatricule = (salaries ?? Enumerable.Empty<Employee>())
                .Where(s => s != null && !string.IsNullOrWhiteSpace(s.EmployeeId))
                .GroupBy(s => s.EmployeeId, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

            var actives = (situations ?? Enumerable.Empty<SituationPret>())
                .Where(s => s != null && !s.EstSolde && s.Statut != StatutPret.Annule)
                .ToList();

            foreach (var parSalarie in actives
                         .Where(s => s.RetenueDuMois > 0m)
                         .GroupBy(s => s.EmployeeId ?? "", StringComparer.OrdinalIgnoreCase))
            {
                Employee salarie;
                if (!parMatricule.TryGetValue(parSalarie.Key, out salarie))
                    continue;

                decimal remuneration = salarie.BaseSalary
                                     + salarie.HousingAllowance
                                     + salarie.FunctionAllowance
                                     + salarie.TransportAllowance
                                     + salarie.SujetionAllowance;

                if (remuneration <= 0m) continue;

                decimal retenue = parSalarie.Sum(s => s.RetenueDuMois);

                if (retenue > remuneration / 3m)
                {
                    messages.Add(string.Format(
                        "{0} : {1:N0} retenus ce mois-ci, soit plus du tiers de sa rémunération " +
                        "permanente. Vérifiez que le net à payer reste vivable.",
                        parSalarie.First().NomSalarie, retenue));
                }
            }

            foreach (var situation in actives.Where(s => s.SansEcheancierRestant))
            {
                messages.Add(string.Format(
                    "{0} : {1:N0} restent dus sans aucune échéance programmée. Reprenez le " +
                    "remboursement ou annulez le prêt.",
                    situation.NomSalarie, situation.SoldeRestantDu));
            }

            return messages;
        }

        /// <summary>Échéance jointe à son prêt, le temps d'un calcul d'apport.</summary>
        private sealed class EcheanceAvecPret
        {
            public int PretId { get; set; }
            public string EmployeeId { get; set; }
            public TypePret Type { get; set; }
            public string Reference { get; set; }
            public decimal MontantAccorde { get; set; }
            public int Annee { get; set; }
            public int Mois { get; set; }
            public decimal MontantPrevu { get; set; }
            public StatutEcheance Statut { get; set; }
            public bool PeriodeCloturee { get; set; }
        }
    }
}
