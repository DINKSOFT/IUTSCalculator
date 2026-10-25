using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Linq;
using System.Threading.Tasks;
using IUTSCalculator.Models;

namespace IUTSCalculator.Services
{
    /// <summary>Une ligne de retenue au titre des saisies/cessions telle qu'elle figurera sur le bulletin.</summary>
    public sealed class LigneSaisie
    {
        public LigneSaisie(int saisieOppositionId, string code, string libelle, decimal montant)
        {
            SaisieOppositionId = saisieOppositionId;
            Code = code;
            Libelle = libelle;
            Montant = montant;
        }

        public int SaisieOppositionId { get; private set; }
        public string Code { get; private set; }
        public string Libelle { get; private set; }
        public decimal Montant { get; private set; }
    }

    /// <summary>
    /// Ce que les saisies-arrêts, cessions et oppositions d'un salarié retiennent sur son bulletin du mois (module PAI-06).
    ///
    /// La retenue s'opère sur le salaire NET après déduction des cotisations sociales (CNSS) et de l'impôt (IUTS),
    /// dans la limite de la quotité saisissable légale fixée par le Code du travail burkinabè et l'Acte Uniforme OHADA,
    /// avec sanctuarisation du minimum vital insaisissable (SMIG).
    /// </summary>
    public sealed class ApportSaisies
    {
        private readonly List<LigneSaisie> _lignes = new List<LigneSaisie>();

        private ApportSaisies()
        {
        }

        public static ApportSaisies Aucun()
        {
            return new ApportSaisies();
        }

        /// <summary>Total retenu sur le net au titre des saisies, cessions et oppositions du mois.</summary>
        public decimal Retenue
        {
            get { return _lignes.Sum(l => l.Montant); }
        }

        public IReadOnlyList<LigneSaisie> Lignes
        {
            get { return _lignes; }
        }

        public bool EstVide
        {
            get { return _lignes.Count == 0; }
        }

        internal void Ajouter(LigneSaisie ligne)
        {
            if (ligne == null || ligne.Montant <= 0m) return;
            _lignes.Add(ligne);
        }
    }

    /// <summary>
    /// Service de gestion des saisies-arrêts, cessions sur salaire et oppositions (module PAI-06).
    /// Assure le calcul de la quotité saisissable progressive, la priorité des créances et le suivi des amortissements.
    /// </summary>
    public class SaisieOppositionService
    {
        private readonly SQLiteService _sqliteService;

        private const string ColonnesSaisie =
            "s.Id, s.EmployeeId, s.TypeSaisie, s.NumeroDossier, s.Creancier, s.Beneficiaire, " +
            "s.MontantTotalDu, s.RetenueMensuelleFixee, s.TotalAmorti, s.DateDebut, s.DateFin, " +
            "s.DateNotification, s.TribunalOuAutorite, s.ReferenceJugement, s.Statut, s.PrioriteOrdre, " +
            "s.DateMainlevee, s.ReferenceMainlevee, s.Commentaire, s.DateCreation, s.DateModification";

        public SaisieOppositionService(SQLiteService sqliteService)
        {
            if (sqliteService == null) throw new ArgumentNullException(nameof(sqliteService));
            _sqliteService = sqliteService;
        }

        #region Moteur Légal de Calcul de la Quotité Saisissable

        /// <summary>
        /// Calcule le barème progressif burkinabè de la quotité saisissable sur le salaire net de référence.
        /// Code du Travail Burkinabè (Loi N° 028-2008/AN, art. 122 et suivants) &amp; Acte Uniforme OHADA (AUPSRVE art. 177+).
        /// </summary>
        public static ResultatCalculQuotite CalculerQuotiteSaisissable(decimal salaireNetReference, decimal smig = 45000m)
        {
            var res = new ResultatCalculQuotite
            {
                SalaireNetReference = salaireNetReference,
                SMIG = smig
            };

            decimal net = Math.Max(0m, salaireNetReference);

            var definitions = new[]
            {
                new { Num = 1, Lib = "Jusqu'à 30 000 FCFA", Bas = 0m, Haut = 30000m, Frac = 1m / 20m, Ratio = "1/20e (5%)" },
                new { Num = 2, Lib = "De 30 001 à 60 000 FCFA", Bas = 30000m, Haut = 60000m, Frac = 1m / 10m, Ratio = "1/10e (10%)" },
                new { Num = 3, Lib = "De 60 001 à 100 000 FCFA", Bas = 60000m, Haut = 100000m, Frac = 1m / 5m, Ratio = "1/5e (20%)" },
                new { Num = 4, Lib = "De 100 001 à 150 000 FCFA", Bas = 100000m, Haut = 150000m, Frac = 1m / 4m, Ratio = "1/4e (25%)" },
                new { Num = 5, Lib = "De 150 001 à 250 000 FCFA", Bas = 150000m, Haut = 250000m, Frac = 1m / 3m, Ratio = "1/3e (33,33%)" },
                new { Num = 6, Lib = "De 250 001 à 400 000 FCFA", Bas = 250000m, Haut = 400000m, Frac = 1m / 2m, Ratio = "1/2 (50%)" },
                new { Num = 7, Lib = "Au-delà de 400 000 FCFA", Bas = 400000m, Haut = decimal.MaxValue, Frac = 1m, Ratio = "Totalité (100%)" }
            };

            decimal totalBareme = 0m;

            foreach (var d in definitions)
            {
                var tr = new DetailTrancheQuotite
                {
                    NumeroTranche = d.Num,
                    LibelleTranche = d.Lib,
                    PlafondBas = d.Bas,
                    PlafondHaut = d.Haut,
                    FractionQuotite = d.Frac,
                    RatioAffichage = d.Ratio
                };

                if (net > d.Bas)
                {
                    decimal assiette = Math.Min(net, d.Haut) - d.Bas;
                    tr.AssietteTranche = assiette;
                    decimal montant = decimal.Round(assiette * d.Frac, 0, MidpointRounding.AwayFromZero);
                    tr.MontantSaisissableTranche = montant;
                    totalBareme += montant;
                }

                res.Tranches.Add(tr);
            }

            // Garantie absolue du minimum vital insaisissable (SMIG) pour les créanciers ordinaires
            decimal maximumSaisissableApresSmig = Math.Max(0m, net - smig);
            res.QuotiteSaisissableLegale = Math.Min(totalBareme, maximumSaisissableApresSmig);
            res.FractionInsaisissable = Math.Max(0m, net - res.QuotiteSaisissableLegale);

            // Super-privilège pension alimentaire : assiette mobilisable = net - SMIG
            res.MontantDisponiblePensionAlimentaire = maximumSaisissableApresSmig;

            return res;
        }

        /// <summary>
        /// Répartit la quotité saisissable entre créanciers selon le rang légal de priorité et le prorata (au marc le franc).
        /// </summary>
        public static ResultatCalculQuotite RepartirSaisies(
            decimal salaireNetReference,
            IEnumerable<SaisieOpposition> saisies,
            decimal smig = 45000m)
        {
            var res = CalculerQuotiteSaisissable(salaireNetReference, smig);
            if (saisies == null) return res;

            var actives = saisies.Where(s => s != null && s.EstActive).ToList();
            if (actives.Count == 0) return res;

            // 1. Isolation de la pension alimentaire (Super-privilège légal)
            var pensionsAlimentaires = actives
                .Where(s => s.TypeSaisie == TypeSaisieOpposition.PensionAlimentaire)
                .OrderBy(s => s.PrioriteEffective)
                .ToList();

            var ordinaires = actives
                .Where(s => s.TypeSaisie != TypeSaisieOpposition.PensionAlimentaire)
                .OrderBy(s => s.PrioriteEffective)
                .ThenBy(s => s.DateNotification ?? s.DateDebut)
                .ToList();

            // A) Traitement de la pension alimentaire
            decimal capaciteAlimentaire = res.MontantDisponiblePensionAlimentaire;
            foreach (var p in pensionsAlimentaires)
            {
                decimal soldeRestant = p.EstIllimitee ? decimal.MaxValue : p.SoldeRestantDu;
                decimal demande = Math.Min(p.RetenueMensuelleFixee, soldeRestant);
                decimal retenu = Math.Min(demande, capaciteAlimentaire);

                capaciteAlimentaire -= retenu;
                res.RetenuePensionAlimentaire += retenu;

                res.Repartitions.Add(new LigneRepartitionSaisie
                {
                    SaisieOppositionId = p.Id,
                    Creancier = p.Creancier,
                    NumeroDossier = p.NumeroDossier,
                    TypeSaisie = p.TypeSaisie,
                    RangPriorite = p.PrioriteEffective,
                    MontantReclameMensuel = demande,
                    MontantRetenu = retenu,
                    SoldeRestantAvant = p.EstIllimitee ? 0m : p.SoldeRestantDu,
                    SoldeRestantApres = p.EstIllimitee ? 0m : Math.Max(0m, p.SoldeRestantDu - retenu),
                    EstCompletementAmortie = !p.EstIllimitee && (p.SoldeRestantDu - retenu <= 0m),
                    MotifPlafonnementOuNote = retenu < demande
                        ? "Plafonné par la réserve minimale de subsistance (SMIG)"
                        : "Totalement honoré"
                });
            }

            // B) Quotité résiduelle pour créanciers ordinaires
            // La pension alimentaire absorbe en priorité la fraction saisissable ordinaire.
            decimal quotiteOrdinaireRestante = Math.Max(0m, res.QuotiteSaisissableLegale - res.RetenuePensionAlimentaire);
            res.QuotiteOrdinaireRestante = quotiteOrdinaireRestante;

            // C) Traitement des créanciers ordinaires par groupe de priorité
            var groupesRang = ordinaires.GroupBy(s => s.PrioriteEffective).OrderBy(g => g.Key);

            foreach (var groupe in groupesRang)
            {
                var listeGroupe = groupe.ToList();
                decimal totalDemandesGroupe = 0m;
                var demandes = new Dictionary<int, decimal>();

                foreach (var s in listeGroupe)
                {
                    decimal soldeRestant = s.EstIllimitee ? decimal.MaxValue : s.SoldeRestantDu;
                    decimal demande = Math.Min(s.RetenueMensuelleFixee, soldeRestant);
                    demandes[s.Id] = demande;
                    totalDemandesGroupe += demande;
                }

                if (quotiteOrdinaireRestante <= 0m)
                {
                    foreach (var s in listeGroupe)
                    {
                        decimal d = demandes[s.Id];
                        res.Repartitions.Add(new LigneRepartitionSaisie
                        {
                            SaisieOppositionId = s.Id,
                            Creancier = s.Creancier,
                            NumeroDossier = s.NumeroDossier,
                            TypeSaisie = s.TypeSaisie,
                            RangPriorite = s.PrioriteEffective,
                            MontantReclameMensuel = d,
                            MontantRetenu = 0m,
                            SoldeRestantAvant = s.EstIllimitee ? 0m : s.SoldeRestantDu,
                            SoldeRestantApres = s.EstIllimitee ? 0m : s.SoldeRestantDu,
                            EstCompletementAmortie = false,
                            MotifPlafonnementOuNote = "Quotité légale saisissable épuisée par créances prioritaires"
                        });
                    }
                    continue;
                }

                if (totalDemandesGroupe <= quotiteOrdinaireRestante)
                {
                    foreach (var s in listeGroupe)
                    {
                        decimal d = demandes[s.Id];
                        quotiteOrdinaireRestante -= d;
                        res.RetenuesOrdinairesTotal += d;

                        res.Repartitions.Add(new LigneRepartitionSaisie
                        {
                            SaisieOppositionId = s.Id,
                            Creancier = s.Creancier,
                            NumeroDossier = s.NumeroDossier,
                            TypeSaisie = s.TypeSaisie,
                            RangPriorite = s.PrioriteEffective,
                            MontantReclameMensuel = d,
                            MontantRetenu = d,
                            SoldeRestantAvant = s.EstIllimitee ? 0m : s.SoldeRestantDu,
                            SoldeRestantApres = s.EstIllimitee ? 0m : Math.Max(0m, s.SoldeRestantDu - d),
                            EstCompletementAmortie = !s.EstIllimitee && (s.SoldeRestantDu - d <= 0m),
                            MotifPlafonnementOuNote = "Totalement honoré"
                        });
                    }
                }
                else
                {
                    // Répartition "au marc le franc" (proportionnelle au montant des demandes)
                    decimal enveloppeADistribuer = quotiteOrdinaireRestante;
                    quotiteOrdinaireRestante = 0m;
                    decimal totalDistribue = 0m;

                    var parts = new List<Tuple<SaisieOpposition, decimal, decimal>>();
                    foreach (var s in listeGroupe)
                    {
                        decimal d = demandes[s.Id];
                        decimal part = totalDemandesGroupe > 0m
                            ? Math.Floor(d / totalDemandesGroupe * enveloppeADistribuer)
                            : 0m;
                        parts.Add(Tuple.Create(s, d, part));
                        totalDistribue += part;
                    }

                    // Répartition équitable des centimes résiduels
                    decimal reliquat = enveloppeADistribuer - totalDistribue;
                    for (int i = 0; i < parts.Count && reliquat > 0; i++)
                    {
                        var t = parts[i];
                        decimal ajout = Math.Min(reliquat, 1m);
                        parts[i] = Tuple.Create(t.Item1, t.Item2, t.Item3 + ajout);
                        reliquat -= ajout;
                        totalDistribue += ajout;
                    }

                    foreach (var p in parts)
                    {
                        var s = p.Item1;
                        decimal d = p.Item2;
                        decimal retenu = p.Item3;
                        res.RetenuesOrdinairesTotal += retenu;

                        res.Repartitions.Add(new LigneRepartitionSaisie
                        {
                            SaisieOppositionId = s.Id,
                            Creancier = s.Creancier,
                            NumeroDossier = s.NumeroDossier,
                            TypeSaisie = s.TypeSaisie,
                            RangPriorite = s.PrioriteEffective,
                            MontantReclameMensuel = d,
                            MontantRetenu = retenu,
                            SoldeRestantAvant = s.EstIllimitee ? 0m : s.SoldeRestantDu,
                            SoldeRestantApres = s.EstIllimitee ? 0m : Math.Max(0m, s.SoldeRestantDu - retenu),
                            EstCompletementAmortie = !s.EstIllimitee && (s.SoldeRestantDu - retenu <= 0m),
                            MotifPlafonnementOuNote = string.Format(
                                "Répartition au marc le franc ({0:N0} FCFA alloués sur {1:N0} FCFA demandés)",
                                retenu, d)
                        });
                    }
                }
            }

            return res;
        }

        #endregion

        #region Opérations CRUD en Base

        public async Task<List<SaisieOpposition>> ChargerToutAsync()
        {
            var liste = new List<SaisieOpposition>();

            using (var conn = _sqliteService.CreateConnection())
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText =
                    "SELECT " + ColonnesSaisie + ", e.FirstName, e.LastName " +
                    "FROM SaisiesOppositions s " +
                    "LEFT JOIN Employees e ON e.EmployeeId = s.EmployeeId " +
                    "ORDER BY s.Statut ASC, s.DateDebut DESC, s.Id DESC";

                using (var reader = (SQLiteDataReader)await cmd.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                        liste.Add(LireSaisie(reader));
                }
            }

            return liste;
        }

        public async Task<List<SaisieOpposition>> ChargerParEmployeAsync(string employeeId)
        {
            var liste = new List<SaisieOpposition>();
            if (string.IsNullOrWhiteSpace(employeeId)) return liste;

            using (var conn = _sqliteService.CreateConnection())
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText =
                    "SELECT " + ColonnesSaisie + ", e.FirstName, e.LastName " +
                    "FROM SaisiesOppositions s " +
                    "LEFT JOIN Employees e ON e.EmployeeId = s.EmployeeId " +
                    "WHERE s.EmployeeId = @emp " +
                    "ORDER BY s.Statut ASC, s.DateDebut DESC, s.Id DESC";
                cmd.Parameters.AddWithValue("@emp", employeeId);

                using (var reader = (SQLiteDataReader)await cmd.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                        liste.Add(LireSaisie(reader));
                }
            }

            return liste;
        }

        public async Task<List<SaisieOpposition>> ChargerActivesParEmployeAsync(string employeeId)
        {
            var liste = await ChargerParEmployeAsync(employeeId);
            return liste.Where(s => s.EstActive).ToList();
        }

        /// <summary>
        /// Toutes les saisies du salarié (le tri par mois se fait ensuite avec
        /// <see cref="ActivesPourMois"/>), lues DANS la
        /// transaction en cours plutôt que sur une connexion séparée — nécessaire pour voir
        /// un TotalAmorti déjà corrigé par cette même transaction avant qu'elle ne soit
        /// validée (voir <see cref="AppliquerEtEnregistrerRetenuesMoisAsync"/>).
        /// </summary>
        private static async Task<List<SaisieOpposition>> ChargerActivesParEmployeAsync(
            string employeeId, SQLiteConnection conn, SQLiteTransaction trans)
        {
            var liste = new List<SaisieOpposition>();
            if (string.IsNullOrWhiteSpace(employeeId)) return liste;

            using (var cmd = conn.CreateCommand())
            {
                cmd.Transaction = trans;
                cmd.CommandText =
                    "SELECT " + ColonnesSaisie + ", e.FirstName, e.LastName " +
                    "FROM SaisiesOppositions s " +
                    "LEFT JOIN Employees e ON e.EmployeeId = s.EmployeeId " +
                    "WHERE s.EmployeeId = @emp " +
                    "ORDER BY s.Statut ASC, s.DateDebut DESC, s.Id DESC";
                cmd.Parameters.AddWithValue("@emp", employeeId);

                using (var reader = (SQLiteDataReader)await cmd.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                        liste.Add(LireSaisie(reader));
                }
            }

            return liste;
        }

        public async Task<SaisieOpposition> ChargerParIdAsync(int id)
        {
            using (var conn = _sqliteService.CreateConnection())
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText =
                    "SELECT " + ColonnesSaisie + ", e.FirstName, e.LastName " +
                    "FROM SaisiesOppositions s " +
                    "LEFT JOIN Employees e ON e.EmployeeId = s.EmployeeId " +
                    "WHERE s.Id = @id";
                cmd.Parameters.AddWithValue("@id", id);

                using (var reader = (SQLiteDataReader)await cmd.ExecuteReaderAsync())
                {
                    if (await reader.ReadAsync())
                        return LireSaisie(reader);
                }
            }

            return null;
        }

        public async Task EnregistrerAsync(SaisieOpposition saisie)
        {
            if (saisie == null) throw new ArgumentNullException(nameof(saisie));
            if (string.IsNullOrWhiteSpace(saisie.EmployeeId))
                throw new InvalidOperationException("Un salarié doit obligatoirement être sélectionné.");
            if (string.IsNullOrWhiteSpace(saisie.Creancier))
                throw new InvalidOperationException("La désignation du créancier est obligatoire.");
            if (saisie.RetenueMensuelleFixee <= 0m)
                throw new InvalidOperationException("La retenue mensuelle fixée doit être strictement positive.");

            using (var conn = _sqliteService.CreateConnection())
            using (var cmd = conn.CreateCommand())
            {
                if (saisie.Id <= 0)
                {
                    saisie.DateCreation = DateTime.Now;
                    saisie.DateModification = DateTime.Now;

                    cmd.CommandText = @"
                        INSERT INTO SaisiesOppositions (
                            EmployeeId, TypeSaisie, NumeroDossier, Creancier, Beneficiaire,
                            MontantTotalDu, RetenueMensuelleFixee, TotalAmorti, DateDebut, DateFin,
                            DateNotification, TribunalOuAutorite, ReferenceJugement, Statut, PrioriteOrdre,
                            DateMainlevee, ReferenceMainlevee, Commentaire, DateCreation, DateModification
                        ) VALUES (
                            @emp, @type, @dossier, @creancier, @beneficiaire,
                            @montant, @retenue, @amorti, @debut, @fin,
                            @notif, @tribunal, @refjug, @statut, @priorite,
                            @mainlevee, @refmainlevee, @comm, @creation, @modif
                        );
                        SELECT last_insert_rowid();";

                    InjecterParametresSaisie(cmd, saisie);
                    object id = await cmd.ExecuteScalarAsync();
                    saisie.Id = (int)DbValue.IntFromDb(id);
                }
                else
                {
                    saisie.DateModification = DateTime.Now;

                    cmd.CommandText = @"
                        UPDATE SaisiesOppositions SET
                            EmployeeId = @emp,
                            TypeSaisie = @type,
                            NumeroDossier = @dossier,
                            Creancier = @creancier,
                            Beneficiaire = @beneficiaire,
                            MontantTotalDu = @montant,
                            RetenueMensuelleFixee = @retenue,
                            TotalAmorti = @amorti,
                            DateDebut = @debut,
                            DateFin = @fin,
                            DateNotification = @notif,
                            TribunalOuAutorite = @tribunal,
                            ReferenceJugement = @refjug,
                            Statut = @statut,
                            PrioriteOrdre = @priorite,
                            DateMainlevee = @mainlevee,
                            ReferenceMainlevee = @refmainlevee,
                            Commentaire = @comm,
                            DateModification = @modif
                        WHERE Id = @id;";

                    cmd.Parameters.AddWithValue("@id", saisie.Id);
                    InjecterParametresSaisie(cmd, saisie);
                    await cmd.ExecuteNonQueryAsync();
                }
            }
        }

        public async Task SupprimerAsync(int id)
        {
            using (var conn = _sqliteService.CreateConnection())
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "DELETE FROM SaisiesOppositions WHERE Id = @id";
                cmd.Parameters.AddWithValue("@id", id);
                await cmd.ExecuteNonQueryAsync();
            }
        }

        public async Task PrononcerMainleveeAsync(int id, DateTime dateMainlevee, string referenceMainlevee)
        {
            using (var conn = _sqliteService.CreateConnection())
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = @"
                    UPDATE SaisiesOppositions SET
                        Statut = @statut,
                        DateMainlevee = @date,
                        ReferenceMainlevee = @ref,
                        DateModification = @modif
                    WHERE Id = @id;";

                cmd.Parameters.AddWithValue("@id", id);
                cmd.Parameters.AddWithValue("@statut", (int)StatutSaisieOpposition.Mainlevee);
                cmd.Parameters.AddWithValue("@date", DbValue.DateToDb(dateMainlevee));
                cmd.Parameters.AddWithValue("@ref", (object)referenceMainlevee ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@modif", DbValue.DateToDb(DateTime.Now));

                await cmd.ExecuteNonQueryAsync();
            }
        }

        public async Task ChangerStatutAsync(int id, StatutSaisieOpposition nouveauStatut)
        {
            using (var conn = _sqliteService.CreateConnection())
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = @"
                    UPDATE SaisiesOppositions SET
                        Statut = @statut,
                        DateModification = @modif
                    WHERE Id = @id;";

                cmd.Parameters.AddWithValue("@id", id);
                cmd.Parameters.AddWithValue("@statut", (int)nouveauStatut);
                cmd.Parameters.AddWithValue("@modif", DbValue.DateToDb(DateTime.Now));

                await cmd.ExecuteNonQueryAsync();
            }
        }

        #endregion

        #region Suivi des Retenues Mensuelles et Intégration Bulletin

        /// <summary>
        /// Applique et enregistre les retenues de saisies du mois pour un salarié dans le cadre du calcul de paie.
        /// </summary>
        public async Task<ResultatCalculQuotite> AppliquerEtEnregistrerRetenuesMoisAsync(
            int annee,
            int mois,
            string employeeId,
            decimal salaireNetReference,
            DateTime datePrelevement)
        {
            ResultatCalculQuotite repartition;

            using (var conn = _sqliteService.CreateConnection())
            using (var trans = conn.BeginTransaction())
            {
                try
                {
                    // 1. Annuler les anciennes retenues enregistrées pour cette même période et ce salarié
                    // et réajuster les amortissements en conséquence.
                    //
                    // Ce nettoyage doit se faire AVANT de répartir la nouvelle retenue, pas
                    // après : un second calcul du même mois (bulletin recalculé après une
                    // correction, par exemple) lisait sinon un TotalAmorti qui incluait
                    // encore la retenue déjà enregistrée POUR CE MOIS, sous-évaluant le
                    // solde restant dû et donc la nouvelle retenue calculée — la créance
                    // finissait réglée plus vite qu'elle n'aurait dû, ou faussement déclarée
                    // soldée — signalé le 16 septembre 2026 (voir ETAT-DEVELOPPEMENT.md item
                    // 22). RepartirSaisies() est maintenant appelé plus bas, une fois ce
                    // nettoyage fait et les saisies rechargées avec un TotalAmorti à jour.
                    using (var cmdAncien = conn.CreateCommand())
                    {
                        cmdAncien.CommandText = @"
                            SELECT SaisieOppositionId, MontantRetenu
                            FROM RetenuesSaisiesMensuelles
                            WHERE Annee = @annee AND Mois = @mois AND EmployeeId = @emp";
                        cmdAncien.Parameters.AddWithValue("@annee", annee);
                        cmdAncien.Parameters.AddWithValue("@mois", mois);
                        cmdAncien.Parameters.AddWithValue("@emp", employeeId);

                        var deltas = new List<Tuple<int, decimal>>();
                        using (var reader = (SQLiteDataReader)await cmdAncien.ExecuteReaderAsync())
                        {
                            while (await reader.ReadAsync())
                            {
                                int sId = (int)DbValue.IntFromDb(reader["SaisieOppositionId"]);
                                decimal montant = DbValue.MoneyFromDb(reader["MontantRetenu"]);
                                deltas.Add(Tuple.Create(sId, montant));
                            }
                        }

                        foreach (var d in deltas)
                        {
                            using (var cmdRevert = conn.CreateCommand())
                            {
                                cmdRevert.CommandText = @"
                                    UPDATE SaisiesOppositions
                                    SET TotalAmorti = MAX(0, TotalAmorti - @mnt),
                                        Statut = CASE WHEN Statut = 2 THEN 0 ELSE Statut END,
                                        DateModification = @modif
                                    WHERE Id = @id";
                                cmdRevert.Parameters.AddWithValue("@mnt", DbValue.MoneyToDb(d.Item2));
                                cmdRevert.Parameters.AddWithValue("@modif", DbValue.DateToDb(DateTime.Now));
                                cmdRevert.Parameters.AddWithValue("@id", d.Item1);
                                await cmdRevert.ExecuteNonQueryAsync();
                            }
                        }
                    }

                    // Nettoyer la table mensuelle
                    using (var cmdDel = conn.CreateCommand())
                    {
                        cmdDel.CommandText = "DELETE FROM RetenuesSaisiesMensuelles WHERE Annee = @annee AND Mois = @mois AND EmployeeId = @emp";
                        cmdDel.Parameters.AddWithValue("@annee", annee);
                        cmdDel.Parameters.AddWithValue("@mois", mois);
                        cmdDel.Parameters.AddWithValue("@emp", employeeId);
                        await cmdDel.ExecuteNonQueryAsync();
                    }

                    // 2. Répartir la retenue du mois — maintenant que TotalAmorti a été
                    // remis à l'état d'avant ce mois (étape 1 ci-dessus), le solde restant
                    // dû que RepartirSaisies() calcule est le bon, que ce soit le premier
                    // passage du mois ou une correction.
                    var saisies = ActivesPourMois(await ChargerActivesParEmployeAsync(employeeId, conn, trans), null, annee, mois);
                    repartition = RepartirSaisies(salaireNetReference, saisies);

                    // 3. Insérer les nouvelles retenues
                    foreach (var ligne in repartition.Repartitions)
                    {
                        if (ligne.MontantRetenu <= 0m) continue;

                        using (var cmdIns = conn.CreateCommand())
                        {
                            cmdIns.CommandText = @"
                                INSERT INTO RetenuesSaisiesMensuelles (
                                    SaisieOppositionId, EmployeeId, Annee, Mois, MontantRetenu,
                                    BaseNetCalcul, QuotiteDisponible, RangPriorite, DatePrelevement,
                                    StatutPaiement, Commentaire
                                ) VALUES (
                                    @saisieId, @emp, @annee, @mois, @retenu,
                                    @baseNet, @quotite, @rang, @datePrelev,
                                    0, @comm
                                );";

                            cmdIns.Parameters.AddWithValue("@saisieId", ligne.SaisieOppositionId);
                            cmdIns.Parameters.AddWithValue("@emp", employeeId);
                            cmdIns.Parameters.AddWithValue("@annee", annee);
                            cmdIns.Parameters.AddWithValue("@mois", mois);
                            cmdIns.Parameters.AddWithValue("@retenu", DbValue.MoneyToDb(ligne.MontantRetenu));
                            cmdIns.Parameters.AddWithValue("@baseNet", DbValue.MoneyToDb(salaireNetReference));
                            cmdIns.Parameters.AddWithValue("@quotite", DbValue.MoneyToDb(repartition.QuotiteSaisissableLegale));
                            cmdIns.Parameters.AddWithValue("@rang", ligne.RangPriorite);
                            cmdIns.Parameters.AddWithValue("@datePrelev", DbValue.DateToDb(datePrelevement));
                            cmdIns.Parameters.AddWithValue("@comm", (object)ligne.MotifPlafonnementOuNote ?? DBNull.Value);

                            await cmdIns.ExecuteNonQueryAsync();
                        }

                        // Mettre à jour TotalAmorti et vérifier si la saisie est soldée
                        using (var cmdUpd = conn.CreateCommand())
                        {
                            cmdUpd.CommandText = @"
                                UPDATE SaisiesOppositions
                                SET TotalAmorti = TotalAmorti + @retenu,
                                    Statut = CASE
                                        WHEN MontantTotalDu > 0 AND (TotalAmorti + @retenu) >= MontantTotalDu THEN 2
                                        ELSE Statut
                                    END,
                                    DateModification = @modif
                                WHERE Id = @saisieId";

                            cmdUpd.Parameters.AddWithValue("@retenu", DbValue.MoneyToDb(ligne.MontantRetenu));
                            cmdUpd.Parameters.AddWithValue("@modif", DbValue.DateToDb(DateTime.Now));
                            cmdUpd.Parameters.AddWithValue("@saisieId", ligne.SaisieOppositionId);

                            await cmdUpd.ExecuteNonQueryAsync();
                        }
                    }

                    trans.Commit();
                }
                catch
                {
                    trans.Rollback();
                    throw;
                }
            }

            return repartition;
        }

        /// <summary>Dernier mois (1er du mois) pour lequel une retenue a été enregistrée, ou null.</summary>
        public async Task<DateTime?> DernierMoisAvecRetenuesAsync()
        {
            using (var conn = _sqliteService.CreateConnection())
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "SELECT MAX(Annee * 100 + Mois) FROM RetenuesSaisiesMensuelles;";
                object o = await cmd.ExecuteScalarAsync();
                if (o == null || o == DBNull.Value) return null;
                int am = Convert.ToInt32(o);
                if (am <= 0) return null;
                return new DateTime(am / 100, am % 100, 1);
            }
        }

        public async Task<List<RetenueSaisieMensuelle>> ChargerRetenuesMoisAsync(int annee, int mois)
        {
            var liste = new List<RetenueSaisieMensuelle>();

            using (var conn = _sqliteService.CreateConnection())
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = @"
                    SELECT r.Id, r.SaisieOppositionId, r.EmployeeId, r.Annee, r.Mois,
                           r.MontantRetenu, r.BaseNetCalcul, r.QuotiteDisponible, r.RangPriorite,
                           r.DatePrelevement, r.NumeroChequeOuVirement, r.StatutPaiement,
                           r.DateReversement, r.Commentaire,
                           e.FirstName, e.LastName,
                           s.Creancier, s.TypeSaisie
                    FROM RetenuesSaisiesMensuelles r
                    LEFT JOIN Employees e ON e.EmployeeId = r.EmployeeId
                    LEFT JOIN SaisiesOppositions s ON s.Id = r.SaisieOppositionId
                    WHERE r.Annee = @annee AND r.Mois = @mois
                    ORDER BY e.LastName, e.FirstName, r.RangPriorite";
                cmd.Parameters.AddWithValue("@annee", annee);
                cmd.Parameters.AddWithValue("@mois", mois);

                using (var reader = (SQLiteDataReader)await cmd.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                        liste.Add(LireRetenue(reader));
                }
            }

            return liste;
        }

        public async Task<List<RetenueSaisieMensuelle>> ChargerHistoriqueRetenuesParSaisieAsync(int saisieId)
        {
            var liste = new List<RetenueSaisieMensuelle>();

            using (var conn = _sqliteService.CreateConnection())
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = @"
                    SELECT r.Id, r.SaisieOppositionId, r.EmployeeId, r.Annee, r.Mois,
                           r.MontantRetenu, r.BaseNetCalcul, r.QuotiteDisponible, r.RangPriorite,
                           r.DatePrelevement, r.NumeroChequeOuVirement, r.StatutPaiement,
                           r.DateReversement, r.Commentaire,
                           e.FirstName, e.LastName,
                           s.Creancier, s.TypeSaisie
                    FROM RetenuesSaisiesMensuelles r
                    LEFT JOIN Employees e ON e.EmployeeId = r.EmployeeId
                    LEFT JOIN SaisiesOppositions s ON s.Id = r.SaisieOppositionId
                    WHERE r.SaisieOppositionId = @id
                    ORDER BY r.Annee DESC, r.Mois DESC";
                cmd.Parameters.AddWithValue("@id", saisieId);

                using (var reader = (SQLiteDataReader)await cmd.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                        liste.Add(LireRetenue(reader));
                }
            }

            return liste;
        }

        public async Task MettreAJourStatutPaiementRetenueAsync(
            int retenueId,
            StatutPaiementSaisie statut,
            DateTime? dateReversement,
            string numeroChequeOuVirement)
        {
            using (var conn = _sqliteService.CreateConnection())
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = @"
                    UPDATE RetenuesSaisiesMensuelles SET
                        StatutPaiement = @statut,
                        DateReversement = @dateRev,
                        NumeroChequeOuVirement = @ref
                    WHERE Id = @id";

                cmd.Parameters.AddWithValue("@id", retenueId);
                cmd.Parameters.AddWithValue("@statut", (int)statut);
                cmd.Parameters.AddWithValue("@dateRev", dateReversement.HasValue ? (object)DbValue.DateToDb(dateReversement.Value) : DBNull.Value);
                cmd.Parameters.AddWithValue("@ref", (object)numeroChequeOuVirement ?? DBNull.Value);

                await cmd.ExecuteNonQueryAsync();
            }
        }

        /// <summary>
        /// Calcule l'apport au bulletin d'un salarié pour une période donnée.
        /// </summary>
        public async Task<ApportSaisies> ApportAsync(int annee, int mois, string employeeId, decimal salaireNetReference)
        {
            var apport = ApportSaisies.Aucun();
            if (string.IsNullOrWhiteSpace(employeeId) || salaireNetReference <= 0m)
                return apport;

            // Aperçu du mois tel que l'enregistrement le calculera : les retenues déjà
            // enregistrées pour CE mois et CE salarié sont neutralisées (l'enregistrement
            // les annule avant de répartir), sinon un recalcul du mois lisait un TotalAmorti
            // qui les contenait déjà — bulletin à 0 alors que le suivi reprenait la retenue.
            var dejaRetenu = await RetenuesDuMoisParSaisieAsync(annee, mois, employeeId);
            var saisies = ActivesPourMois(await ChargerParEmployeAsync(employeeId), dejaRetenu, annee, mois);
            if (saisies.Count == 0) return apport;

            var repartition = RepartirSaisies(salaireNetReference, saisies);

            foreach (var rep in repartition.Repartitions)
            {
                if (rep.MontantRetenu <= 0m) continue;

                string codeRubrique = rep.TypeSaisie == TypeSaisieOpposition.PensionAlimentaire ? "PENALIM" : "SAISIE";
                apport.Ajouter(new LigneSaisie(rep.SaisieOppositionId, codeRubrique, LibelleBulletin(rep), rep.MontantRetenu));
            }

            return apport;
        }

        /// <summary>
        /// Calcule en lot les apports pour un ensemble d'employés.
        /// </summary>
        public async Task<Dictionary<string, ApportSaisies>> ApportsAsync(
            int annee,
            int mois,
            IDictionary<string, decimal> netsParEmploye)
        {
            var resultat = new Dictionary<string, ApportSaisies>(StringComparer.OrdinalIgnoreCase);
            if (netsParEmploye == null || netsParEmploye.Count == 0)
                return resultat;

            var toutesSaisies = await ChargerToutAsync();
            var dejaRetenu = await RetenuesDuMoisParSaisieAsync(annee, mois, null);
            var saisiesParEmp = ActivesPourMois(toutesSaisies, dejaRetenu, annee, mois)
                .GroupBy(s => s.EmployeeId, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

            foreach (var kvp in netsParEmploye)
            {
                string empId = kvp.Key;
                decimal net = kvp.Value;
                var apport = ApportSaisies.Aucun();

                List<SaisieOpposition> saisies;
                saisiesParEmp.TryGetValue(empId, out saisies);
                if (saisies != null && saisies.Count > 0 && net > 0m)
                {
                    var rep = RepartirSaisies(net, saisies);
                    foreach (var r in rep.Repartitions)
                    {
                        if (r.MontantRetenu <= 0m) continue;
                        string code = r.TypeSaisie == TypeSaisieOpposition.PensionAlimentaire ? "PENALIM" : "SAISIE";
                        apport.Ajouter(new LigneSaisie(r.SaisieOppositionId, code, LibelleBulletin(r), r.MontantRetenu));
                    }
                }

                resultat[empId] = apport;
            }

            return resultat;
        }

        /// <summary>
        /// Saisies qui jouent sur le mois de paie : date d'effet (DateDebut) au plus tard
        /// le dernier jour du mois, et pas de date de fin antérieure au 1er du mois.
        ///
        /// La date d'effet était ignorée : un dossier créé en mars pour une saisie qui ne
        /// joue qu'à partir de mai était retenu dès la paie de mars, et un dossier saisi
        /// avec un effet antérieur était bien retenu — mais seulement à partir du mois
        /// calculé, jamais rétroactivement (c'est voulu : une paie clôturée ne bouge pas).
        /// </summary>
        public static List<SaisieOpposition> EffectivesPourMois(IEnumerable<SaisieOpposition> saisies, int annee, int mois)
        {
            var resultat = new List<SaisieOpposition>();
            if (saisies == null || mois < 1 || mois > 12) return resultat;

            var debutMois = new DateTime(annee, mois, 1);
            var finMois = debutMois.AddMonths(1).AddDays(-1);
            foreach (var s in saisies)
            {
                if (s == null) continue;
                if (s.DateDebut != default(DateTime) && s.DateDebut.Date > finMois) continue;
                if (s.DateFin.HasValue && s.DateFin.Value.Date < debutMois) continue;
                resultat.Add(s);
            }
            return resultat;
        }

        /// <summary>
        /// Saisies recevables pour un mois de paie : statut en cours, créance non soldée
        /// (ou illimitée), date d'effet et date de fin compatibles avec le MOIS (et non avec
        /// la date du jour, comme EstActive — un recalcul de mars ignorait une saisie
        /// terminée fin mars).
        ///
        /// <paramref name="dejaRetenu"/> : retenues déjà enregistrées pour ce mois, par
        /// saisie. Elles sont retirées du TotalAmorti, et une saisie soldée par ce mois-là
        /// redevient recevable — exactement l'état que l'enregistrement reconstitue avant de
        /// répartir (AppliquerEtEnregistrerRetenuesMoisAsync).
        /// </summary>
        public static List<SaisieOpposition> ActivesPourMois(
            IEnumerable<SaisieOpposition> saisies, IDictionary<int, decimal> dejaRetenu, int annee, int mois)
        {
            var recevables = new List<SaisieOpposition>();
            foreach (var s in saisies ?? Enumerable.Empty<SaisieOpposition>())
            {
                if (s == null) continue;
                decimal retenu;
                if (dejaRetenu != null && dejaRetenu.TryGetValue(s.Id, out retenu) && retenu > 0m)
                {
                    s.TotalAmorti = Math.Max(0m, s.TotalAmorti - retenu);
                    if (s.Statut == StatutSaisieOpposition.Soldee)
                        s.Statut = StatutSaisieOpposition.EnCours;
                }
                if (s.Statut != StatutSaisieOpposition.EnCours) continue;
                if (s.EstSolde && !s.EstIllimitee) continue;
                recevables.Add(s);
            }
            return EffectivesPourMois(recevables, annee, mois);
        }

        /// <summary>Retenues déjà enregistrées pour un mois (et un salarié, si précisé), par saisie.</summary>
        private async Task<Dictionary<int, decimal>> RetenuesDuMoisParSaisieAsync(int annee, int mois, string employeeId)
        {
            var map = new Dictionary<int, decimal>();
            using (var conn = _sqliteService.CreateConnection())
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "SELECT SaisieOppositionId, SUM(MontantRetenu) AS M FROM RetenuesSaisiesMensuelles " +
                                  "WHERE Annee = @a AND Mois = @m" + (string.IsNullOrWhiteSpace(employeeId) ? "" : " AND EmployeeId = @e") +
                                  " GROUP BY SaisieOppositionId;";
                cmd.Parameters.AddWithValue("@a", annee);
                cmd.Parameters.AddWithValue("@m", mois);
                if (!string.IsNullOrWhiteSpace(employeeId)) cmd.Parameters.AddWithValue("@e", employeeId);
                using (var reader = (SQLiteDataReader)await cmd.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                        map[DbValue.IntFromDb(reader["SaisieOppositionId"])] = DbValue.MoneyFromDb(reader["M"]);
                }
            }
            return map;
        }

        /// <summary>
        /// Libellé de la ligne de retenue sur le bulletin : nature lisible, créancier et
        /// numéro de dossier — « Saisie-arrêt judiciaire (CAISSE POPULAIRE) — dossier
        /// SA-2026-014 » au lieu du nom technique « SaisieArretJudiciaire (…) ».
        /// </summary>
        public static string LibelleBulletin(LigneRepartitionSaisie ligne)
        {
            if (ligne == null) return "";
            string libelle = SaisieOpposition.LibelleCourtType(ligne.TypeSaisie);
            if (!string.IsNullOrWhiteSpace(ligne.Creancier))
                libelle += " (" + ligne.Creancier.Trim() + ")";
            if (!string.IsNullOrWhiteSpace(ligne.NumeroDossier))
                libelle += " — dossier " + ligne.NumeroDossier.Trim();
            return libelle;
        }

        /// <summary>
        /// Extrait l'apport de saisies pour un salarié depuis un dictionnaire préchargé.
        /// </summary>
        public static ApportSaisies ApportDe(Dictionary<string, ApportSaisies> apports, string employeeId)
        {
            if (apports == null || string.IsNullOrWhiteSpace(employeeId))
                return ApportSaisies.Aucun();

            ApportSaisies apport;
            return apports.TryGetValue(employeeId, out apport) ? apport : ApportSaisies.Aucun();
        }

        #endregion

        #region Helpers Mappage SQL

        private static SaisieOpposition LireSaisie(SQLiteDataReader reader)
        {
            string nom = DbValue.TextFromDb(reader["LastName"]);
            string prenom = DbValue.TextFromDb(reader["FirstName"]);

            return new SaisieOpposition
            {
                Id = (int)DbValue.IntFromDb(reader["Id"]),
                EmployeeId = DbValue.TextFromDb(reader["EmployeeId"]),
                NomSalarie = string.Format("{0} {1}", nom, prenom).Trim(),
                TypeSaisie = (TypeSaisieOpposition)DbValue.IntFromDb(reader["TypeSaisie"]),
                NumeroDossier = DbValue.TextFromDb(reader["NumeroDossier"]),
                Creancier = DbValue.TextFromDb(reader["Creancier"]),
                Beneficiaire = DbValue.TextFromDb(reader["Beneficiaire"]),
                MontantTotalDu = DbValue.MoneyFromDb(reader["MontantTotalDu"]),
                RetenueMensuelleFixee = DbValue.MoneyFromDb(reader["RetenueMensuelleFixee"]),
                TotalAmorti = DbValue.MoneyFromDb(reader["TotalAmorti"]),
                DateDebut = DbValue.DateFromDb(reader["DateDebut"], DateTime.Today),
                DateFin = reader["DateFin"] != DBNull.Value ? (DateTime?)DbValue.DateFromDb(reader["DateFin"], DateTime.Today) : null,
                DateNotification = reader["DateNotification"] != DBNull.Value ? (DateTime?)DbValue.DateFromDb(reader["DateNotification"], DateTime.Today) : null,
                TribunalOuAutorite = DbValue.TextFromDb(reader["TribunalOuAutorite"]),
                ReferenceJugement = DbValue.TextFromDb(reader["ReferenceJugement"]),
                Statut = (StatutSaisieOpposition)DbValue.IntFromDb(reader["Statut"]),
                PrioriteOrdre = (int)DbValue.IntFromDb(reader["PrioriteOrdre"]),
                DateMainlevee = reader["DateMainlevee"] != DBNull.Value ? (DateTime?)DbValue.DateFromDb(reader["DateMainlevee"], DateTime.Today) : null,
                ReferenceMainlevee = DbValue.TextFromDb(reader["ReferenceMainlevee"]),
                Commentaire = DbValue.TextFromDb(reader["Commentaire"]),
                DateCreation = DbValue.DateFromDb(reader["DateCreation"], DateTime.Now),
                DateModification = DbValue.DateFromDb(reader["DateModification"], DateTime.Now)
            };
        }

        private static RetenueSaisieMensuelle LireRetenue(SQLiteDataReader reader)
        {
            string nom = DbValue.TextFromDb(reader["LastName"]);
            string prenom = DbValue.TextFromDb(reader["FirstName"]);

            return new RetenueSaisieMensuelle
            {
                Id = (int)DbValue.IntFromDb(reader["Id"]),
                SaisieOppositionId = (int)DbValue.IntFromDb(reader["SaisieOppositionId"]),
                EmployeeId = DbValue.TextFromDb(reader["EmployeeId"]),
                NomSalarie = string.Format("{0} {1}", nom, prenom).Trim(),
                Creancier = DbValue.TextFromDb(reader["Creancier"]),
                TypeSaisie = (TypeSaisieOpposition)DbValue.IntFromDb(reader["TypeSaisie"]),
                Annee = (int)DbValue.IntFromDb(reader["Annee"]),
                Mois = (int)DbValue.IntFromDb(reader["Mois"]),
                MontantRetenu = DbValue.MoneyFromDb(reader["MontantRetenu"]),
                BaseNetCalcul = DbValue.MoneyFromDb(reader["BaseNetCalcul"]),
                QuotiteDisponible = DbValue.MoneyFromDb(reader["QuotiteDisponible"]),
                RangPriorite = (int)DbValue.IntFromDb(reader["RangPriorite"]),
                DatePrelevement = DbValue.DateFromDb(reader["DatePrelevement"], DateTime.Today),
                NumeroChequeOuVirement = DbValue.TextFromDb(reader["NumeroChequeOuVirement"]),
                StatutPaiement = (StatutPaiementSaisie)DbValue.IntFromDb(reader["StatutPaiement"]),
                DateReversement = reader["DateReversement"] != DBNull.Value ? (DateTime?)DbValue.DateFromDb(reader["DateReversement"], DateTime.Today) : null,
                Commentaire = DbValue.TextFromDb(reader["Commentaire"])
            };
        }

        private static void InjecterParametresSaisie(SQLiteCommand cmd, SaisieOpposition s)
        {
            cmd.Parameters.AddWithValue("@emp", s.EmployeeId ?? string.Empty);
            cmd.Parameters.AddWithValue("@type", (int)s.TypeSaisie);
            cmd.Parameters.AddWithValue("@dossier", (object)s.NumeroDossier ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@creancier", s.Creancier ?? string.Empty);
            cmd.Parameters.AddWithValue("@beneficiaire", (object)s.Beneficiaire ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@montant", DbValue.MoneyToDb(s.MontantTotalDu));
            cmd.Parameters.AddWithValue("@retenue", DbValue.MoneyToDb(s.RetenueMensuelleFixee));
            cmd.Parameters.AddWithValue("@amorti", DbValue.MoneyToDb(s.TotalAmorti));
            cmd.Parameters.AddWithValue("@debut", DbValue.DateToDb(s.DateDebut));
            cmd.Parameters.AddWithValue("@fin", s.DateFin.HasValue ? (object)DbValue.DateToDb(s.DateFin.Value) : DBNull.Value);
            cmd.Parameters.AddWithValue("@notif", s.DateNotification.HasValue ? (object)DbValue.DateToDb(s.DateNotification.Value) : DBNull.Value);
            cmd.Parameters.AddWithValue("@tribunal", (object)s.TribunalOuAutorite ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@refjug", (object)s.ReferenceJugement ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@statut", (int)s.Statut);
            cmd.Parameters.AddWithValue("@priorite", s.PrioriteOrdre);
            cmd.Parameters.AddWithValue("@mainlevee", s.DateMainlevee.HasValue ? (object)DbValue.DateToDb(s.DateMainlevee.Value) : DBNull.Value);
            cmd.Parameters.AddWithValue("@refmainlevee", (object)s.ReferenceMainlevee ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@comm", (object)s.Commentaire ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@creation", DbValue.DateToDb(s.DateCreation));
            cmd.Parameters.AddWithValue("@modif", DbValue.DateToDb(s.DateModification));
        }

        #endregion
    }
}
