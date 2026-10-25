using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using IUTSCalculator.Models;

namespace IUTSCalculator.Services
{
    /// <summary>Ce que l'enregistrement d'une procédure a produit, à afficher.</summary>
    public class ResultatDiscipline
    {
        public List<string> Messages { get; } = new List<string>();
        public List<AlerteDiscipline> Alertes { get; set; } = new List<AlerteDiscipline>();

        public string Resume
        {
            get { return Messages.Count == 0 ? "Procédure enregistrée." : "Procédure enregistrée. " + string.Join(" ", Messages); }
        }
    }

    /// <summary>
    /// Discipline & procédures (module RH-06) : procédures disciplinaires, catalogue des
    /// sanctions, délais du dossier et pièces.
    ///
    /// Passerelles :
    /// - PAI-04 : une mise à pied disciplinaire NOTIFIÉE devient une absence « Mise à pied »
    ///   (non rémunérée) marquée <see cref="MarqueurDiscipline"/>, que le bulletin retient ;
    ///   la mise à pied conservatoire ne l'est que si la procédure aboutit à un licenciement
    ///   pour faute lourde (sinon le salaire reste dû) ;
    /// - SAL-05 : le dernier licenciement notifié propose le motif et la date de sortie du
    ///   solde de tout compte ;
    /// - RH-08 : les jours fériés de la Gestion des temps ne comptent pas comme ouvrables.
    ///
    /// Toute opération sensible est tracée dans le journal d'audit.
    /// </summary>
    public class DisciplineService
    {
        /// <summary>Préfixe du commentaire des absences créées par une procédure disciplinaire.</summary>
        public const string MarqueurDiscipline = "[RH-06]";

        private const string EntiteAudit = "Discipline";

        private readonly SQLiteService _sqlite;
        private readonly AuditService _audit;

        public DisciplineService(SQLiteService sqliteService)
        {
            _sqlite = sqliteService ?? throw new ArgumentNullException(nameof(sqliteService));
            _audit = new AuditService(sqliteService);
        }

        // ═══════════════════════════════════════════════════════════════════
        //  Paramètres
        // ═══════════════════════════════════════════════════════════════════

        public async Task<ParametresDiscipline> ChargerParametresAsync()
        {
            var prm = ParametresDiscipline.Defaut();
            using (var conn = _sqlite.CreateConnection())
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "SELECT * FROM ParametresDiscipline WHERE Id = 1;";
                using (var r = (SQLiteDataReader)await cmd.ExecuteReaderAsync())
                {
                    if (await r.ReadAsync())
                    {
                        prm.DelaiEngagementJours = DbValue.IntFromDb(r["DelaiEngagementJours"]);
                        prm.DelaiReponseJoursOuvrables = DbValue.IntFromDb(r["DelaiReponseJoursOuvrables"]);
                        prm.DelaiConvocationJoursOuvrables = DbValue.IntFromDb(r["DelaiConvocationJoursOuvrables"]);
                        prm.DelaiDecisionJours = DbValue.IntFromDb(r["DelaiDecisionJours"]);
                        prm.ReferenceReglementInterieur = DbValue.TextFromDb(r["ReferenceReglementInterieur"]);
                        prm.Signataire = DbValue.TextFromDb(r["Signataire"]);
                        prm.FonctionSignataire = DbValue.TextFromDb(r["FonctionSignataire"]);
                    }
                }
            }
            return prm;
        }

        public static string AnomalieParametres(ParametresDiscipline prm)
        {
            if (prm == null) return "Paramètres vides.";
            if (prm.DelaiEngagementJours < 0 || prm.DelaiEngagementJours > 3650) return "Délai d'engagement : entre 0 et 3 650 jours (0 = non contrôlé).";
            if (prm.DelaiReponseJoursOuvrables < 1 || prm.DelaiReponseJoursOuvrables > 60) return "Délai de réponse : entre 1 et 60 jours ouvrables.";
            if (prm.DelaiConvocationJoursOuvrables < 0 || prm.DelaiConvocationJoursOuvrables > 60) return "Délai de convocation : entre 0 et 60 jours ouvrables.";
            if (prm.DelaiDecisionJours < 0 || prm.DelaiDecisionJours > 365) return "Délai de décision : entre 0 et 365 jours (0 = non contrôlé).";
            return null;
        }

        public async Task EnregistrerParametresAsync(ParametresDiscipline prm)
        {
            var anomalie = AnomalieParametres(prm);
            if (anomalie != null) throw new InvalidOperationException(anomalie);

            using (var conn = _sqlite.CreateConnection())
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText =
                    "INSERT INTO ParametresDiscipline (Id, DelaiEngagementJours, DelaiReponseJoursOuvrables, " +
                    "DelaiConvocationJoursOuvrables, DelaiDecisionJours, ReferenceReglementInterieur, Signataire, FonctionSignataire) " +
                    "VALUES (1, @eng, @rep, @conv, @dec, @ri, @sig, @fct) " +
                    "ON CONFLICT(Id) DO UPDATE SET DelaiEngagementJours = @eng, DelaiReponseJoursOuvrables = @rep, " +
                    "DelaiConvocationJoursOuvrables = @conv, DelaiDecisionJours = @dec, ReferenceReglementInterieur = @ri, " +
                    "Signataire = @sig, FonctionSignataire = @fct;";
                cmd.Parameters.AddWithValue("@eng", prm.DelaiEngagementJours);
                cmd.Parameters.AddWithValue("@rep", prm.DelaiReponseJoursOuvrables);
                cmd.Parameters.AddWithValue("@conv", prm.DelaiConvocationJoursOuvrables);
                cmd.Parameters.AddWithValue("@dec", prm.DelaiDecisionJours);
                cmd.Parameters.AddWithValue("@ri", (object)Nettoyer(prm.ReferenceReglementInterieur) ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@sig", (object)Nettoyer(prm.Signataire) ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@fct", (object)Nettoyer(prm.FonctionSignataire) ?? DBNull.Value);
                await cmd.ExecuteNonQueryAsync();
            }

            await _audit.EnregistrerAsync("Paramètres discipline", EntiteAudit, "Paramètres",
                details: string.Format(CultureInfo.InvariantCulture,
                    "Engagement {0} j, réponse {1} j ouvr., convocation {2} j ouvr., décision {3} j",
                    prm.DelaiEngagementJours, prm.DelaiReponseJoursOuvrables, prm.DelaiConvocationJoursOuvrables, prm.DelaiDecisionJours));
        }

        // ═══════════════════════════════════════════════════════════════════
        //  Catalogue des sanctions
        // ═══════════════════════════════════════════════════════════════════

        public async Task<List<TypeSanction>> ChargerTypesAsync()
        {
            using (var conn = _sqlite.CreateConnection())
                return await ChargerTypesAsync(conn, null);
        }

        private static async Task<List<TypeSanction>> ChargerTypesAsync(SQLiteConnection conn, SQLiteTransaction tx)
        {
            var liste = new List<TypeSanction>();
            using (var cmd = conn.CreateCommand())
            {
                if (tx != null) cmd.Transaction = tx;
                cmd.CommandText = "SELECT * FROM TypesSanction ORDER BY Ordre, Code;";
                using (var r = (SQLiteDataReader)await cmd.ExecuteReaderAsync())
                {
                    while (await r.ReadAsync())
                    {
                        liste.Add(new TypeSanction
                        {
                            Id = DbValue.IntFromDb(r["Id"]),
                            Code = DbValue.TextFromDb(r["Code"]),
                            Libelle = DbValue.TextFromDb(r["Libelle"]),
                            Nature = (NatureSanction)DbValue.IntFromDb(r["Nature"]),
                            Ordre = DbValue.IntFromDb(r["Ordre"]),
                            ExigeExplication = DbValue.IntFromDb(r["ExigeExplication"]) != 0,
                            ExigeEntretien = DbValue.IntFromDb(r["ExigeEntretien"]) != 0,
                            DureeMaxJours = DbValue.IntFromDb(r["DureeMaxJours"]),
                            FauteLourde = DbValue.IntFromDb(r["FauteLourde"]) != 0,
                            EffacementMois = DbValue.IntFromDb(r["EffacementMois"]),
                            Actif = DbValue.IntFromDb(r["Actif"]) != 0,
                            EstSysteme = DbValue.IntFromDb(r["EstSysteme"]) != 0
                        });
                    }
                }
            }
            return liste;
        }

        public static string AnomalieType(TypeSanction t, IEnumerable<TypeSanction> existants)
        {
            if (t == null) return "Sanction vide.";
            if (string.IsNullOrWhiteSpace(t.Code)) return "Indiquez un code (3 à 10 caractères, ex. AVT).";
            if (t.Code.Trim().Length > 10) return "Le code ne peut pas dépasser 10 caractères.";
            if (string.IsNullOrWhiteSpace(t.Libelle)) return "Indiquez le libellé de la sanction.";
            if (t.EffacementMois > 240) return "Délai d'effacement : 240 mois au plus (0 = jamais effacée).";
            if (t.DureeMaxJours > 60) return "Durée maximale de mise à pied : 60 jours au plus.";
            if ((existants ?? Enumerable.Empty<TypeSanction>()).Any(x => x.Id != t.Id &&
                    string.Equals(x.Code, t.Code.Trim(), StringComparison.OrdinalIgnoreCase)))
                return string.Format("Le code « {0} » est déjà utilisé.", t.Code.Trim().ToUpperInvariant());
            return null;
        }

        public async Task EnregistrerTypeAsync(TypeSanction t)
        {
            var existants = await ChargerTypesAsync();
            var anomalie = AnomalieType(t, existants);
            if (anomalie != null) throw new InvalidOperationException(anomalie);

            // Les attributs sans objet pour la nature choisie sont neutralisés.
            if (!t.EstMiseAPied) t.DureeMaxJours = 0;
            if (!t.EstLicenciement) t.FauteLourde = false;

            using (var conn = _sqlite.CreateConnection())
            using (var cmd = conn.CreateCommand())
            {
                if (t.Id > 0)
                {
                    cmd.CommandText =
                        "UPDATE TypesSanction SET Code = @c, Libelle = @l, Nature = @n, Ordre = @o, ExigeExplication = @ex, " +
                        "ExigeEntretien = @en, DureeMaxJours = @d, FauteLourde = @fl, EffacementMois = @ef, Actif = @a WHERE Id = @id;";
                    cmd.Parameters.AddWithValue("@id", t.Id);
                }
                else
                {
                    cmd.CommandText =
                        "INSERT INTO TypesSanction (Code, Libelle, Nature, Ordre, ExigeExplication, ExigeEntretien, " +
                        "DureeMaxJours, FauteLourde, EffacementMois, Actif, EstSysteme) " +
                        "VALUES (@c, @l, @n, @o, @ex, @en, @d, @fl, @ef, @a, 0);";
                }
                cmd.Parameters.AddWithValue("@c", t.Code.Trim().ToUpperInvariant());
                cmd.Parameters.AddWithValue("@l", t.Libelle.Trim());
                cmd.Parameters.AddWithValue("@n", (int)t.Nature);
                cmd.Parameters.AddWithValue("@o", t.Ordre);
                cmd.Parameters.AddWithValue("@ex", t.ExigeExplication ? 1 : 0);
                cmd.Parameters.AddWithValue("@en", t.ExigeEntretien ? 1 : 0);
                cmd.Parameters.AddWithValue("@d", t.DureeMaxJours);
                cmd.Parameters.AddWithValue("@fl", t.FauteLourde ? 1 : 0);
                cmd.Parameters.AddWithValue("@ef", t.EffacementMois);
                cmd.Parameters.AddWithValue("@a", t.Actif ? 1 : 0);
                await cmd.ExecuteNonQueryAsync();

                if (t.Id <= 0) t.Id = await DernierIdAsync(conn, null);
            }

            await _audit.EnregistrerAsync("Catalogue des sanctions", EntiteAudit, t.Code,
                details: string.Format("{0} — {1}", t.Libelle, t.NatureLibelle));
        }

        public async Task SupprimerTypeAsync(TypeSanction t)
        {
            if (t == null || t.Id <= 0) return;
            if (t.EstSysteme)
                throw new InvalidOperationException("Une sanction livrée avec le module ne se supprime pas : désactivez-la si votre règlement ne la prévoit pas.");

            using (var conn = _sqlite.CreateConnection())
            {
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT COUNT(*) FROM ProceduresDisciplinaires WHERE TypeSanctionId = @id;";
                    cmd.Parameters.AddWithValue("@id", t.Id);
                    if (DbValue.IntFromDb(await cmd.ExecuteScalarAsync()) > 0)
                        throw new InvalidOperationException("Cette sanction figure dans des procédures : désactivez-la plutôt que de la supprimer.");
                }
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "DELETE FROM TypesSanction WHERE Id = @id;";
                    cmd.Parameters.AddWithValue("@id", t.Id);
                    await cmd.ExecuteNonQueryAsync();
                }
            }

            await _audit.EnregistrerAsync("Suppression sanction du catalogue", EntiteAudit, t.Code, details: t.Libelle);
        }

        // ═══════════════════════════════════════════════════════════════════
        //  Jours fériés (Gestion des temps, RH-08)
        // ═══════════════════════════════════════════════════════════════════

        public async Task<HashSet<DateTime>> ChargerFeriesAsync()
        {
            var feries = new HashSet<DateTime>();
            using (var conn = _sqlite.CreateConnection())
            {
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'JoursFeries';";
                    if (DbValue.IntFromDb(await cmd.ExecuteScalarAsync()) == 0) return feries;
                }
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT DateJour FROM JoursFeries;";
                    using (var r = (SQLiteDataReader)await cmd.ExecuteReaderAsync())
                    {
                        while (await r.ReadAsync())
                        {
                            var d = DbValue.DateNullableFromDb(r[0]);
                            if (d.HasValue) feries.Add(d.Value.Date);
                        }
                    }
                }
            }
            return feries;
        }

        // ═══════════════════════════════════════════════════════════════════
        //  Procédures
        // ═══════════════════════════════════════════════════════════════════

        public async Task<List<ProcedureDisciplinaire>> ChargerProceduresAsync()
        {
            using (var conn = _sqlite.CreateConnection())
            {
                var types = await ChargerTypesAsync(conn, null);
                return await ChargerProceduresAsync(conn, null, types, null);
            }
        }

        private static async Task<List<ProcedureDisciplinaire>> ChargerProceduresAsync(
            SQLiteConnection conn, SQLiteTransaction tx, List<TypeSanction> types, string employeeId)
        {
            var liste = new List<ProcedureDisciplinaire>();
            using (var cmd = conn.CreateCommand())
            {
                if (tx != null) cmd.Transaction = tx;
                cmd.CommandText =
                    "SELECT p.*, e.FirstName, e.LastName, e.Fonction AS FonctionSalarie " +
                    "FROM ProceduresDisciplinaires p " +
                    "LEFT JOIN Employees e ON e.EmployeeId = p.EmployeeId " +
                    (employeeId != null ? "WHERE p.EmployeeId = @employe " : "") +
                    "ORDER BY p.DateFaits DESC, p.Id DESC;";
                if (employeeId != null) cmd.Parameters.AddWithValue("@employe", employeeId);

                using (var r = (SQLiteDataReader)await cmd.ExecuteReaderAsync())
                {
                    while (await r.ReadAsync())
                    {
                        var p = Lire(r);
                        p.TypeSanction = p.TypeSanctionId.HasValue ? types.FirstOrDefault(t => t.Id == p.TypeSanctionId.Value) : null;
                        liste.Add(p);
                    }
                }
            }
            return liste;
        }

        private static ProcedureDisciplinaire Lire(SQLiteDataReader r)
        {
            string nom = string.Format("{0} {1}", DbValue.TextFromDb(r["LastName"]), DbValue.TextFromDb(r["FirstName"])).Trim();
            var p = new ProcedureDisciplinaire
            {
                Id = DbValue.IntFromDb(r["Id"]),
                Reference = DbValue.TextFromDb(r["Reference"]),
                EmployeeId = DbValue.TextFromDb(r["EmployeeId"]),
                NomSalarie = string.IsNullOrEmpty(nom) ? DbValue.TextFromDb(r["EmployeeId"]) : nom,
                Fonction = DbValue.TextFromDb(r["FonctionSalarie"]),
                DateFaits = DbValue.DateFromDb(r["DateFaits"], DateTime.Today),
                DateConnaissance = DbValue.DateFromDb(r["DateConnaissance"], DateTime.Today),
                LieuFaits = DbValue.TextFromDb(r["LieuFaits"]),
                CategorieFaits = DbValue.TextFromDb(r["CategorieFaits"]),
                DescriptionFaits = DbValue.TextFromDb(r["DescriptionFaits"]),
                Temoins = DbValue.TextFromDb(r["Temoins"]),
                Gravite = (GraviteFaute)DbValue.IntFromDb(r["Gravite"]),
                SalarieProtege = DbValue.IntFromDb(r["SalarieProtege"]) != 0,
                MandatProtege = DbValue.TextFromDb(r["MandatProtege"]),
                DateDemandeExplication = DbValue.DateNullableFromDb(r["DateDemandeExplication"]),
                DateReponse = DbValue.DateNullableFromDb(r["DateReponse"]),
                ResumeReponse = DbValue.TextFromDb(r["ResumeReponse"]),
                ConservatoireDebut = DbValue.DateNullableFromDb(r["ConservatoireDebut"]),
                ConservatoireFin = DbValue.DateNullableFromDb(r["ConservatoireFin"]),
                DateConvocation = DbValue.DateNullableFromDb(r["DateConvocation"]),
                DateEntretien = DbValue.DateNullableFromDb(r["DateEntretien"]),
                HeureLieuEntretien = DbValue.TextFromDb(r["HeureLieuEntretien"]),
                AssistantEntretien = DbValue.TextFromDb(r["AssistantEntretien"]),
                CompteRenduEntretien = DbValue.TextFromDb(r["CompteRenduEntretien"]),
                DecisionInspection = (DecisionInspection)DbValue.IntFromDb(r["DecisionInspection"]),
                DateDemandeInspection = DbValue.DateNullableFromDb(r["DateDemandeInspection"]),
                DateDecisionInspection = DbValue.DateNullableFromDb(r["DateDecisionInspection"]),
                ReferenceInspection = DbValue.TextFromDb(r["ReferenceInspection"]),
                TypeSanctionId = DbValue.IntNullableFromDb(r["TypeSanctionId"]),
                DateDecision = DbValue.DateNullableFromDb(r["DateDecision"]),
                MotifsDecision = DbValue.TextFromDb(r["MotifsDecision"]),
                MiseAPiedDebut = DbValue.DateNullableFromDb(r["MiseAPiedDebut"]),
                MiseAPiedFin = DbValue.DateNullableFromDb(r["MiseAPiedFin"]),
                MiseAPiedJours = DbValue.DecimalFromDb(r["MiseAPiedJours"]),
                DateEffetLicenciement = DbValue.DateNullableFromDb(r["DateEffetLicenciement"]),
                ClasseeSansSuite = DbValue.IntFromDb(r["ClasseeSansSuite"]) != 0,
                MotifClassement = DbValue.TextFromDb(r["MotifClassement"]),
                DateNotification = DbValue.DateNullableFromDb(r["DateNotification"]),
                ModeNotification = DbValue.TextFromDb(r["ModeNotification"]),
                DateAccuseReception = DbValue.DateNullableFromDb(r["DateAccuseReception"]),
                DateRecours = DbValue.DateNullableFromDb(r["DateRecours"]),
                InstanceRecours = DbValue.TextFromDb(r["InstanceRecours"]),
                IssueRecours = DbValue.TextFromDb(r["IssueRecours"]),
                AbsenceMiseAPiedId = DbValue.IntNullableFromDb(r["AbsenceMiseAPiedId"]),
                AbsenceConservatoireId = DbValue.IntNullableFromDb(r["AbsenceConservatoireId"]),
                Commentaire = DbValue.TextFromDb(r["Commentaire"]),
                DateCreation = DbValue.DateFromDb(r["DateCreation"], DateTime.Now),
                DateModification = DbValue.DateFromDb(r["DateModification"], DateTime.Now),
                CreePar = DbValue.TextFromDb(r["CreePar"])
            };
            return p;
        }

        /// <summary>Alertes d'une procédure, avec le contexte lu en base (historique, pièces, fériés).</summary>
        public async Task<List<AlerteDiscipline>> AnalyserAsync(ProcedureDisciplinaire p, DateTime aujourdHui)
        {
            if (p == null) return new List<AlerteDiscipline>();
            var prm = await ChargerParametresAsync();
            var feries = await ChargerFeriesAsync();
            using (var conn = _sqlite.CreateConnection())
            {
                var types = await ChargerTypesAsync(conn, null);
                var type = p.TypeSanctionId.HasValue ? types.FirstOrDefault(t => t.Id == p.TypeSanctionId.Value) : null;
                var autres = string.IsNullOrWhiteSpace(p.EmployeeId)
                    ? new List<ProcedureDisciplinaire>()
                    : await ChargerProceduresAsync(conn, null, types, p.EmployeeId);
                var pieces = p.Id > 0 ? await ChargerPiecesAsync(conn, p.Id) : new List<PieceDisciplinaire>();
                return ControleDiscipline.Analyser(p, type, prm, autres, pieces, feries, aujourdHui, await ChargerMandatsAsync(conn));
            }
        }

        /// <summary>
        /// Enregistre une procédure. Une alerte bloquante refuse l'enregistrement ; la
        /// mise à pied notifiée est transmise aux congés et absences (PAI-04) dans la même
        /// transaction — si la période de paie est clôturée, rien n'est enregistré.
        /// </summary>
        public async Task<ResultatDiscipline> EnregistrerAsync(ProcedureDisciplinaire p)
        {
            if (p == null) throw new ArgumentNullException(nameof(p));

            var resultat = new ResultatDiscipline();
            var prm = await ChargerParametresAsync();
            var feries = await ChargerFeriesAsync();
            var aujourdHui = DateTime.Today;

            ProcedureDisciplinaire avant = null;
            using (var conn = _sqlite.CreateConnection())
            {
                var types = await ChargerTypesAsync(conn, null);
                var type = p.TypeSanctionId.HasValue ? types.FirstOrDefault(t => t.Id == p.TypeSanctionId.Value) : null;
                if (p.TypeSanctionId.HasValue && type == null)
                    throw new InvalidOperationException("La sanction choisie n'existe plus dans le catalogue.");
                p.TypeSanction = type;

                var autres = await ChargerProceduresAsync(conn, null, types, p.EmployeeId ?? "");
                avant = p.Id > 0 ? autres.FirstOrDefault(a => a.Id == p.Id) : null;
                if (p.Id > 0 && avant == null)
                    throw new InvalidOperationException("Cette procédure a été supprimée entre-temps.");

                // Un salarié ne change pas en cours de procédure : la référence lui appartient.
                if (avant != null && !string.Equals(avant.EmployeeId, p.EmployeeId, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("Le salarié d'une procédure enregistrée ne peut pas être changé : classez-la sans suite et ouvrez-en une autre.");

                var pieces = p.Id > 0 ? await ChargerPiecesAsync(conn, p.Id) : new List<PieceDisciplinaire>();
                var mandats = await ChargerMandatsAsync(conn);
                var alertes = ControleDiscipline.Analyser(p, type, prm, autres, pieces, feries, aujourdHui, mandats);
                var blocage = ControleDiscipline.PremierBlocage(alertes);
                if (blocage != null) throw new InvalidOperationException(blocage);

                // Si la transaction échoue (période clôturée, chevauchement d'absences),
                // l'objet édité doit redevenir exactement ce qu'il était : sinon une
                // procédure jamais créée garderait un identifiant et une référence.
                var sauvegarde = new
                {
                    p.Id, p.Reference, p.DateCreation, p.DateModification, p.CreePar,
                    p.AbsenceMiseAPiedId, p.AbsenceConservatoireId
                };

                using (var tx = conn.BeginTransaction())
                {
                    try
                    {
                        var maintenant = DateTime.Now;
                        p.DateModification = maintenant;

                        if (p.Id <= 0)
                        {
                            p.Reference = await ProchaineReferenceAsync(conn, tx, p.DateConnaissance.Year);
                            p.DateCreation = maintenant;
                            p.CreePar = UtilisateurCourant();
                            await InsererAsync(conn, tx, p);
                            p.Id = await DernierIdAsync(conn, tx);
                        }

                        await SynchroniserPaieAsync(conn, tx, p, type, resultat.Messages);
                        await MettreAJourAsync(conn, tx, p);

                        tx.Commit();
                    }
                    catch
                    {
                        tx.Rollback();
                        p.Id = sauvegarde.Id;
                        p.Reference = sauvegarde.Reference;
                        p.DateCreation = sauvegarde.DateCreation;
                        p.DateModification = sauvegarde.DateModification;
                        p.CreePar = sauvegarde.CreePar;
                        p.AbsenceMiseAPiedId = sauvegarde.AbsenceMiseAPiedId;
                        p.AbsenceConservatoireId = sauvegarde.AbsenceConservatoireId;
                        throw;
                    }
                }

                autres = await ChargerProceduresAsync(conn, null, types, p.EmployeeId);
                pieces = await ChargerPiecesAsync(conn, p.Id);
                resultat.Alertes = ControleDiscipline.Analyser(p, type, prm, autres, pieces, feries, aujourdHui, mandats);
            }

            await AuditerAsync(avant, p);
            return resultat;
        }

        /// <summary>Supprime une procédure non notifiée (une sanction notifiée reste au registre).</summary>
        public async Task SupprimerAsync(ProcedureDisciplinaire p)
        {
            if (p == null || p.Id <= 0) return;

            using (var conn = _sqlite.CreateConnection())
            {
                var types = await ChargerTypesAsync(conn, null);
                var enBase = (await ChargerProceduresAsync(conn, null, types, p.EmployeeId ?? "")).FirstOrDefault(x => x.Id == p.Id);
                if (enBase == null) return;
                if (enBase.Statut == StatutProcedure.Notifiee)
                    throw new InvalidOperationException("Une sanction notifiée fait partie du registre : elle ne peut pas être supprimée. Saisissez plutôt l'issue d'un éventuel recours.");

                var pieces = await ChargerPiecesAsync(conn, p.Id);

                using (var tx = conn.BeginTransaction())
                {
                    // Aucune absence n'existe avant notification ; par prudence, on retire celles qui resteraient.
                    enBase.TypeSanction = null;
                    enBase.ClasseeSansSuite = true;
                    var messages = new List<string>();
                    await SynchroniserPaieAsync(conn, tx, enBase, null, messages);

                    using (var cmd = conn.CreateCommand())
                    {
                        cmd.Transaction = tx;
                        cmd.CommandText = "DELETE FROM PiecesDisciplinaires WHERE ProcedureId = @id; DELETE FROM ProceduresDisciplinaires WHERE Id = @id;";
                        cmd.Parameters.AddWithValue("@id", p.Id);
                        await cmd.ExecuteNonQueryAsync();
                    }
                    tx.Commit();
                }

                foreach (var piece in pieces)
                {
                    try { File.Delete(CheminPiece(enBase, piece)); } catch { /* fichier déjà absent */ }
                }
                try
                {
                    var rep = RepertoireProcedure(enBase);
                    if (Directory.Exists(rep) && !Directory.EnumerateFileSystemEntries(rep).Any()) Directory.Delete(rep);
                }
                catch { /* répertoire conservé */ }
            }

            await _audit.EnregistrerAsync("Suppression procédure disciplinaire", EntiteAudit, p.Reference,
                details: string.Format("Salarié {0}, faits du {1:dd/MM/yyyy}", p.EmployeeId, p.DateFaits));
        }

        /// <summary>Dernier licenciement disciplinaire notifié d'un salarié (passerelle SAL-05), ou null.</summary>
        public async Task<ProcedureDisciplinaire> DernierLicenciementNotifieAsync(string employeeId)
        {
            if (string.IsNullOrWhiteSpace(employeeId)) return null;
            using (var conn = _sqlite.CreateConnection())
            {
                if (!TableExiste(conn, "ProceduresDisciplinaires")) return null;
                var types = await ChargerTypesAsync(conn, null);
                return (await ChargerProceduresAsync(conn, null, types, employeeId))
                    .Where(p => p.Statut == StatutProcedure.Notifiee && p.TypeSanction != null && p.TypeSanction.EstLicenciement)
                    .OrderByDescending(p => p.DateNotification)
                    .FirstOrDefault();
            }
        }

        // ── Écriture ─────────────────────────────────────────────────────

        private const string ColonnesEcrites =
            "Reference, EmployeeId, DateFaits, DateConnaissance, LieuFaits, CategorieFaits, DescriptionFaits, Temoins, " +
            "Gravite, SalarieProtege, MandatProtege, DateDemandeExplication, DateReponse, ResumeReponse, ConservatoireDebut, " +
            "ConservatoireFin, DateConvocation, DateEntretien, HeureLieuEntretien, AssistantEntretien, CompteRenduEntretien, DecisionInspection, " +
            "DateDemandeInspection, DateDecisionInspection, ReferenceInspection, TypeSanctionId, DateDecision, MotifsDecision, " +
            "MiseAPiedDebut, MiseAPiedFin, MiseAPiedJours, DateEffetLicenciement, ClasseeSansSuite, MotifClassement, " +
            "DateNotification, ModeNotification, DateAccuseReception, DateRecours, InstanceRecours, IssueRecours, " +
            "AbsenceMiseAPiedId, AbsenceConservatoireId, Commentaire, DateCreation, DateModification, CreePar";

        private static async Task InsererAsync(SQLiteConnection conn, SQLiteTransaction tx, ProcedureDisciplinaire p)
        {
            var colonnes = ColonnesEcrites.Split(',').Select(c => c.Trim()).ToList();
            using (var cmd = conn.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = "INSERT INTO ProceduresDisciplinaires (" + ColonnesEcrites + ") VALUES (" +
                                  string.Join(", ", colonnes.Select(c => "@" + c)) + ");";
                Parametrer(cmd, p);
                await cmd.ExecuteNonQueryAsync();
            }
        }

        private static async Task MettreAJourAsync(SQLiteConnection conn, SQLiteTransaction tx, ProcedureDisciplinaire p)
        {
            var colonnes = ColonnesEcrites.Split(',').Select(c => c.Trim()).ToList();
            using (var cmd = conn.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = "UPDATE ProceduresDisciplinaires SET " +
                                  string.Join(", ", colonnes.Select(c => c + " = @" + c)) + " WHERE Id = @Id;";
                Parametrer(cmd, p);
                cmd.Parameters.AddWithValue("@Id", p.Id);
                await cmd.ExecuteNonQueryAsync();
            }
        }

        private static void Parametrer(SQLiteCommand cmd, ProcedureDisciplinaire p)
        {
            Action<string, object> v = (nom, valeur) => cmd.Parameters.AddWithValue("@" + nom, valeur ?? DBNull.Value);
            Func<DateTime?, object> d = x => DbValue.DateNullableToDb(x);
            Func<string, object> t = x => (object)Nettoyer(x);

            v("Reference", p.Reference);
            v("EmployeeId", p.EmployeeId ?? "");
            v("DateFaits", DbValue.DateToDb(p.DateFaits));
            v("DateConnaissance", DbValue.DateToDb(p.DateConnaissance));
            v("LieuFaits", t(p.LieuFaits));
            v("CategorieFaits", t(p.CategorieFaits));
            v("DescriptionFaits", t(p.DescriptionFaits));
            v("Temoins", t(p.Temoins));
            v("Gravite", (int)p.Gravite);
            v("SalarieProtege", p.SalarieProtege ? 1 : 0);
            v("MandatProtege", t(p.MandatProtege));
            v("DateDemandeExplication", d(p.DateDemandeExplication));
            v("DateReponse", d(p.DateReponse));
            v("ResumeReponse", t(p.ResumeReponse));
            v("ConservatoireDebut", d(p.ConservatoireDebut));
            v("ConservatoireFin", d(p.ConservatoireFin));
            v("DateConvocation", d(p.DateConvocation));
            v("DateEntretien", d(p.DateEntretien));
            v("HeureLieuEntretien", t(p.HeureLieuEntretien));
            v("AssistantEntretien", t(p.AssistantEntretien));
            v("CompteRenduEntretien", t(p.CompteRenduEntretien));
            v("DecisionInspection", (int)p.DecisionInspection);
            v("DateDemandeInspection", d(p.DateDemandeInspection));
            v("DateDecisionInspection", d(p.DateDecisionInspection));
            v("ReferenceInspection", t(p.ReferenceInspection));
            v("TypeSanctionId", p.TypeSanctionId.HasValue ? (object)p.TypeSanctionId.Value : null);
            v("DateDecision", d(p.DateDecision));
            v("MotifsDecision", t(p.MotifsDecision));
            v("MiseAPiedDebut", d(p.MiseAPiedDebut));
            v("MiseAPiedFin", d(p.MiseAPiedFin));
            v("MiseAPiedJours", DbValue.DecimalToDb(p.MiseAPiedJours));
            v("DateEffetLicenciement", d(p.DateEffetLicenciement));
            v("ClasseeSansSuite", p.ClasseeSansSuite ? 1 : 0);
            v("MotifClassement", t(p.MotifClassement));
            v("DateNotification", d(p.DateNotification));
            v("ModeNotification", t(p.ModeNotification));
            v("DateAccuseReception", d(p.DateAccuseReception));
            v("DateRecours", d(p.DateRecours));
            v("InstanceRecours", t(p.InstanceRecours));
            v("IssueRecours", t(p.IssueRecours));
            v("AbsenceMiseAPiedId", p.AbsenceMiseAPiedId.HasValue ? (object)p.AbsenceMiseAPiedId.Value : null);
            v("AbsenceConservatoireId", p.AbsenceConservatoireId.HasValue ? (object)p.AbsenceConservatoireId.Value : null);
            v("Commentaire", t(p.Commentaire));
            v("DateCreation", DbValue.DateToDb(p.DateCreation));
            v("DateModification", DbValue.DateToDb(p.DateModification));
            v("CreePar", t(p.CreePar));
        }

        /// <summary>Référence suivante de l'année : DISC-2026-0001, -0002…</summary>
        private static async Task<string> ProchaineReferenceAsync(SQLiteConnection conn, SQLiteTransaction tx, int annee)
        {
            string prefixe = string.Format(CultureInfo.InvariantCulture, "DISC-{0:D4}-", annee);
            int max = 0;
            using (var cmd = conn.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = "SELECT Reference FROM ProceduresDisciplinaires WHERE Reference LIKE @p;";
                cmd.Parameters.AddWithValue("@p", prefixe + "%");
                using (var r = (SQLiteDataReader)await cmd.ExecuteReaderAsync())
                {
                    while (await r.ReadAsync())
                    {
                        int n;
                        var texte = DbValue.TextFromDb(r[0]) ?? "";
                        if (texte.Length > prefixe.Length && int.TryParse(texte.Substring(prefixe.Length), NumberStyles.None, CultureInfo.InvariantCulture, out n))
                            max = Math.Max(max, n);
                    }
                }
            }
            return prefixe + (max + 1).ToString("D4", CultureInfo.InvariantCulture);
        }

        // ═══════════════════════════════════════════════════════════════════
        //  Passerelle paie (PAI-04)
        // ═══════════════════════════════════════════════════════════════════

        /// <summary>
        /// Met les absences liées en accord avec la procédure : la mise à pied notifiée
        /// devient (ou reste) une absence « Mise à pied » ; toute autre situation retire
        /// l'absence liée. Une absence inchangée n'est pas réécrite : un recours saisi
        /// après la clôture du mois ne bute donc pas sur la période fermée.
        /// </summary>
        private static async Task SynchroniserPaieAsync(
            SQLiteConnection conn, SQLiteTransaction tx, ProcedureDisciplinaire p, TypeSanction type, List<string> messages)
        {
            bool notifiee = p.Statut == StatutProcedure.Notifiee && type != null;
            var exclure = new[] { p.AbsenceMiseAPiedId, p.AbsenceConservatoireId };

            Absence miseAPied = null;
            if (notifiee && type.EstMiseAPied && p.MiseAPiedDebut.HasValue && p.MiseAPiedFin.HasValue && p.MiseAPiedJours > 0m)
            {
                miseAPied = new Absence
                {
                    EmployeeId = p.EmployeeId,
                    Type = TypeAbsence.MiseAPied,
                    DateDebut = p.MiseAPiedDebut.Value,
                    DateFin = p.MiseAPiedFin.Value,
                    NombreJours = p.MiseAPiedJours,
                    Commentaire = MarqueurDiscipline + " Mise à pied disciplinaire — " + p.Reference
                };
            }
            p.AbsenceMiseAPiedId = await AppliquerAbsenceAsync(conn, tx, p.AbsenceMiseAPiedId, miseAPied, exclure, "mise à pied disciplinaire", messages);

            Absence conservatoire = null;
            if (notifiee && type.EstLicenciement && type.FauteLourde && p.ConservatoireDebut.HasValue)
            {
                var debut = p.ConservatoireDebut.Value;
                var fin = p.ConservatoireFin
                          ?? (p.DateEffetLicenciement.HasValue ? p.DateEffetLicenciement.Value.AddDays(-1) : p.DateNotification.Value);
                if (fin < debut) fin = debut;
                var jours = Absence.JoursEntre(debut, fin, true);
                if (jours > 0m)
                {
                    conservatoire = new Absence
                    {
                        EmployeeId = p.EmployeeId,
                        Type = TypeAbsence.MiseAPied,
                        DateDebut = debut,
                        DateFin = fin,
                        NombreJours = jours,
                        Commentaire = MarqueurDiscipline + " Mise à pied conservatoire (faute lourde) — " + p.Reference
                    };
                }
            }
            p.AbsenceConservatoireId = await AppliquerAbsenceAsync(conn, tx, p.AbsenceConservatoireId, conservatoire, exclure, "mise à pied conservatoire non payée", messages);
        }

        private static async Task<int?> AppliquerAbsenceAsync(
            SQLiteConnection conn, SQLiteTransaction tx, int? idExistant, Absence voulue, int?[] exclure, string libelle, List<string> messages)
        {
            var fr = LibellesDiscipline.Fr;
            Absence existante = idExistant.HasValue ? await LireAbsenceAsync(conn, tx, idExistant.Value) : null;

            if (voulue == null)
            {
                if (existante == null) return null;
                await VerifierMoisOuvertsAsync(conn, tx, existante, "retirer la " + libelle);
                await ExecuterAsync(conn, tx, "DELETE FROM Absences WHERE Id = @id;", existante.Id);
                messages.Add(string.Format(fr, "Absence « {0} » du {1:dd/MM/yyyy} retirée des congés et absences.", libelle, existante.DateDebut));
                return null;
            }

            if (existante != null
                && existante.Type == voulue.Type
                && existante.DateDebut.Date == voulue.DateDebut.Date
                && existante.DateFin.Date == voulue.DateFin.Date
                && existante.NombreJours == voulue.NombreJours
                && string.Equals(existante.EmployeeId, voulue.EmployeeId, StringComparison.OrdinalIgnoreCase))
                return existante.Id;

            var anomalie = AbsenceService.Anomalie(voulue);
            if (anomalie != null) throw new InvalidOperationException("Transmission à la paie impossible (" + libelle + ") : " + anomalie);

            if (existante != null) await VerifierMoisOuvertsAsync(conn, tx, existante, "modifier la " + libelle);
            await VerifierMoisOuvertsAsync(conn, tx, voulue, "enregistrer la " + libelle);
            await VerifierChevauchementAsync(conn, tx, voulue, exclure);

            if (existante != null)
            {
                using (var cmd = conn.CreateCommand())
                {
                    cmd.Transaction = tx;
                    cmd.CommandText =
                        "UPDATE Absences SET Type = @type, DateDebut = @debut, DateFin = @fin, NombreJours = @jours, " +
                        "NombreHeures = @heures, Commentaire = @commentaire WHERE Id = @id;";
                    ParametrerAbsence(cmd, voulue);
                    cmd.Parameters.AddWithValue("@id", existante.Id);
                    await cmd.ExecuteNonQueryAsync();
                }
                messages.Add(string.Format(fr, "Absence « {0} » mise à jour ({1:0.##} j, du {2:dd/MM/yyyy} au {3:dd/MM/yyyy}).",
                    libelle, voulue.NombreJours, voulue.DateDebut, voulue.DateFin));
                return existante.Id;
            }

            using (var cmd = conn.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText =
                    "INSERT INTO Absences (EmployeeId, Type, DateDebut, DateFin, NombreJours, NombreHeures, Commentaire, DateSaisie, PeriodeImputation) " +
                    "VALUES (@employe, @type, @debut, @fin, @jours, @heures, @commentaire, @saisie, NULL);";
                ParametrerAbsence(cmd, voulue);
                cmd.Parameters.AddWithValue("@employe", voulue.EmployeeId);
                cmd.Parameters.AddWithValue("@saisie", DbValue.DateToDb(DateTime.Now));
                await cmd.ExecuteNonQueryAsync();
            }
            messages.Add(string.Format(fr, "Absence « {0} » transmise à la paie ({1:0.##} j, du {2:dd/MM/yyyy} au {3:dd/MM/yyyy}).",
                libelle, voulue.NombreJours, voulue.DateDebut, voulue.DateFin));
            return await DernierIdAsync(conn, tx);
        }

        private static void ParametrerAbsence(SQLiteCommand cmd, Absence a)
        {
            cmd.Parameters.AddWithValue("@type", (int)a.Type);
            cmd.Parameters.AddWithValue("@debut", DbValue.DateToDb(a.DateDebut));
            cmd.Parameters.AddWithValue("@fin", DbValue.DateToDb(a.DateFin));
            cmd.Parameters.AddWithValue("@jours", DbValue.DecimalToDb(a.NombreJours));
            cmd.Parameters.AddWithValue("@heures", DbValue.DecimalToDb(0m));
            cmd.Parameters.AddWithValue("@commentaire", a.Commentaire ?? "");
        }

        private static async Task<Absence> LireAbsenceAsync(SQLiteConnection conn, SQLiteTransaction tx, int id)
        {
            using (var cmd = conn.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = "SELECT Id, EmployeeId, Type, DateDebut, DateFin, NombreJours, PeriodeImputation FROM Absences WHERE Id = @id;";
                cmd.Parameters.AddWithValue("@id", id);
                using (var r = (SQLiteDataReader)await cmd.ExecuteReaderAsync())
                {
                    if (!await r.ReadAsync()) return null;
                    return new Absence
                    {
                        Id = DbValue.IntFromDb(r["Id"]),
                        EmployeeId = DbValue.TextFromDb(r["EmployeeId"]),
                        Type = (TypeAbsence)DbValue.IntFromDb(r["Type"]),
                        DateDebut = DbValue.DateFromDb(r["DateDebut"], DateTime.Today),
                        DateFin = DbValue.DateFromDb(r["DateFin"], DateTime.Today),
                        NombreJours = DbValue.DecimalFromDb(r["NombreJours"]),
                        PeriodeImputation = DbValue.TextFromDb(r["PeriodeImputation"])
                    };
                }
            }
        }

        private static async Task VerifierMoisOuvertsAsync(SQLiteConnection conn, SQLiteTransaction tx, Absence a, string action)
        {
            var mois = new DateTime(a.DateDebut.Year, a.DateDebut.Month, 1);
            var dernier = new DateTime(a.DateFin.Year, a.DateFin.Month, 1);
            if (a.EstImputee && a.MoisImputation.HasValue) mois = dernier = a.MoisImputation.Value;

            for (; mois <= dernier; mois = mois.AddMonths(1))
            {
                if (await PeriodePaieService.EstClotureeAsync(conn, mois, tx))
                    throw new InvalidOperationException(string.Format(LibellesDiscipline.Fr,
                        "Impossible de {0} : la période de paie de {1:MMMM yyyy} est clôturée. Rouvrez-la depuis « Périodes de paie » si la correction est justifiée, ou décalez les dates.",
                        action, mois));
            }
        }

        private static async Task VerifierChevauchementAsync(SQLiteConnection conn, SQLiteTransaction tx, Absence a, int?[] exclure)
        {
            using (var cmd = conn.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText =
                    "SELECT Id, Type, DateDebut, DateFin FROM Absences " +
                    "WHERE EmployeeId = @employe AND DateDebut <= @fin AND DateFin >= @debut;";
                cmd.Parameters.AddWithValue("@employe", a.EmployeeId);
                cmd.Parameters.AddWithValue("@debut", DbValue.DateToDb(a.DateDebut));
                cmd.Parameters.AddWithValue("@fin", DbValue.DateToDb(a.DateFin));
                using (var r = (SQLiteDataReader)await cmd.ExecuteReaderAsync())
                {
                    while (await r.ReadAsync())
                    {
                        int id = DbValue.IntFromDb(r["Id"]);
                        if (exclure.Any(x => x.HasValue && x.Value == id)) continue;
                        throw new InvalidOperationException(string.Format(LibellesDiscipline.Fr,
                            "Le salarié a déjà une absence « {0} » du {1:dd/MM/yyyy} au {2:dd/MM/yyyy} qui chevauche ces dates : " +
                            "corrigez-la dans « Congés et absences » ou décalez la mise à pied.",
                            Absence.Libelle((TypeAbsence)DbValue.IntFromDb(r["Type"])),
                            DbValue.DateFromDb(r["DateDebut"], DateTime.Today), DbValue.DateFromDb(r["DateFin"], DateTime.Today)));
                    }
                }
            }
        }

        // ═══════════════════════════════════════════════════════════════════
        //  Registre des salariés protégés (mandats représentatifs)
        // ═══════════════════════════════════════════════════════════════════

        public async Task<List<MandatRepresentatif>> ChargerMandatsAsync()
        {
            using (var conn = _sqlite.CreateConnection())
                return await ChargerMandatsAsync(conn);
        }

        private static async Task<List<MandatRepresentatif>> ChargerMandatsAsync(SQLiteConnection conn)
        {
            var liste = new List<MandatRepresentatif>();
            if (!TableExiste(conn, "MandatsRepresentatifs")) return liste;

            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText =
                    "SELECT m.*, e.FirstName, e.LastName FROM MandatsRepresentatifs m " +
                    "LEFT JOIN Employees e ON e.EmployeeId = m.EmployeeId " +
                    "ORDER BY e.LastName, e.FirstName, m.DateDebut DESC;";
                using (var r = (SQLiteDataReader)await cmd.ExecuteReaderAsync())
                {
                    while (await r.ReadAsync())
                    {
                        string nom = string.Format("{0} {1}", DbValue.TextFromDb(r["LastName"]), DbValue.TextFromDb(r["FirstName"])).Trim();
                        liste.Add(new MandatRepresentatif
                        {
                            Id = DbValue.IntFromDb(r["Id"]),
                            EmployeeId = DbValue.TextFromDb(r["EmployeeId"]),
                            NomSalarie = string.IsNullOrEmpty(nom) ? DbValue.TextFromDb(r["EmployeeId"]) : nom,
                            Mandat = DbValue.TextFromDb(r["Mandat"]),
                            DateDebut = DbValue.DateFromDb(r["DateDebut"], DateTime.Today),
                            DateFin = DbValue.DateNullableFromDb(r["DateFin"]),
                            FinProtection = DbValue.DateNullableFromDb(r["FinProtection"]),
                            Commentaire = DbValue.TextFromDb(r["Commentaire"])
                        });
                    }
                }
            }
            return liste;
        }

        public static string AnomalieMandat(MandatRepresentatif m)
        {
            if (m == null) return "Mandat vide.";
            if (string.IsNullOrWhiteSpace(m.EmployeeId)) return "Choisissez le salarié.";
            if (string.IsNullOrWhiteSpace(m.Mandat)) return "Indiquez le mandat exercé.";
            if (m.DateFin.HasValue && m.DateFin.Value.Date < m.DateDebut.Date) return "La fin du mandat précède son début.";
            if (m.FinProtection.HasValue && m.FinProtection.Value.Date < m.DateDebut.Date) return "La fin de la protection précède le début du mandat.";
            if (m.FinProtection.HasValue && m.DateFin.HasValue && m.FinProtection.Value.Date < m.DateFin.Value.Date)
                return "La protection ne peut pas s'arrêter avant la fin du mandat : laissez « Protection jusqu'au » vide.";
            if (m.FinProtection.HasValue && !m.DateFin.HasValue)
                return "Indiquez d'abord la fin du mandat ; « Protection jusqu'au » sert à prolonger la protection au-delà.";
            return null;
        }

        public async Task EnregistrerMandatAsync(MandatRepresentatif m)
        {
            var anomalie = AnomalieMandat(m);
            if (anomalie != null) throw new InvalidOperationException(anomalie);

            using (var conn = _sqlite.CreateConnection())
            using (var cmd = conn.CreateCommand())
            {
                if (m.Id > 0)
                {
                    cmd.CommandText =
                        "UPDATE MandatsRepresentatifs SET EmployeeId = @e, Mandat = @m, DateDebut = @d, DateFin = @f, " +
                        "FinProtection = @p, Commentaire = @c WHERE Id = @id;";
                    cmd.Parameters.AddWithValue("@id", m.Id);
                }
                else
                {
                    cmd.CommandText =
                        "INSERT INTO MandatsRepresentatifs (EmployeeId, Mandat, DateDebut, DateFin, FinProtection, Commentaire) " +
                        "VALUES (@e, @m, @d, @f, @p, @c);";
                }
                cmd.Parameters.AddWithValue("@e", m.EmployeeId.Trim());
                cmd.Parameters.AddWithValue("@m", m.Mandat.Trim());
                cmd.Parameters.AddWithValue("@d", DbValue.DateToDb(m.DateDebut.Date));
                cmd.Parameters.AddWithValue("@f", DbValue.DateNullableToDb(m.DateFin));
                cmd.Parameters.AddWithValue("@p", DbValue.DateNullableToDb(m.FinProtection));
                cmd.Parameters.AddWithValue("@c", (object)Nettoyer(m.Commentaire) ?? DBNull.Value);
                await cmd.ExecuteNonQueryAsync();

                if (m.Id <= 0) m.Id = await DernierIdAsync(conn, null);
            }

            await _audit.EnregistrerAsync("Registre des salariés protégés", EntiteAudit, m.EmployeeId,
                details: string.Format("{0}, {1}, protection {2}", m.Mandat, m.PeriodeLibelle, m.ProtectionLibelle.ToLowerInvariant()));
        }

        public async Task SupprimerMandatAsync(MandatRepresentatif m)
        {
            if (m == null || m.Id <= 0) return;
            using (var conn = _sqlite.CreateConnection())
                await ExecuterAsync(conn, null, "DELETE FROM MandatsRepresentatifs WHERE Id = @id;", m.Id);

            await _audit.EnregistrerAsync("Mandat retiré du registre des salariés protégés", EntiteAudit, m.EmployeeId,
                details: string.Format("{0}, {1}", m.Mandat, m.PeriodeLibelle));
        }

        // ═══════════════════════════════════════════════════════════════════
        //  Pièces
        // ═══════════════════════════════════════════════════════════════════

        /// <summary>Répertoire des pièces du dossier : à côté de sa base, sous Pieces\&lt;base&gt;\Discipline.</summary>
        public string RepertoirePieces
        {
            get
            {
                var db = _sqlite.DatabasePath;
                return Path.Combine(Path.GetDirectoryName(db) ?? ".", "Pieces", Path.GetFileNameWithoutExtension(db), "Discipline");
            }
        }

        public string RepertoireProcedure(ProcedureDisciplinaire p)
        {
            return Path.Combine(RepertoirePieces, NomDeFichierSur(p.Reference ?? ("procedure-" + p.Id)));
        }

        public string CheminPiece(ProcedureDisciplinaire p, PieceDisciplinaire piece)
        {
            return Path.Combine(RepertoireProcedure(p), piece.NomFichier ?? "");
        }

        public async Task<List<PieceDisciplinaire>> ChargerPiecesAsync(int procedureId)
        {
            using (var conn = _sqlite.CreateConnection())
                return await ChargerPiecesAsync(conn, procedureId);
        }

        private static async Task<List<PieceDisciplinaire>> ChargerPiecesAsync(SQLiteConnection conn, int procedureId)
        {
            var liste = new List<PieceDisciplinaire>();
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "SELECT * FROM PiecesDisciplinaires WHERE ProcedureId = @id ORDER BY DateAjout, Id;";
                cmd.Parameters.AddWithValue("@id", procedureId);
                using (var r = (SQLiteDataReader)await cmd.ExecuteReaderAsync())
                {
                    while (await r.ReadAsync())
                    {
                        liste.Add(new PieceDisciplinaire
                        {
                            Id = DbValue.IntFromDb(r["Id"]),
                            ProcedureId = DbValue.IntFromDb(r["ProcedureId"]),
                            Type = (TypePiece)DbValue.IntFromDb(r["Type"]),
                            Libelle = DbValue.TextFromDb(r["Libelle"]),
                            NomFichier = DbValue.TextFromDb(r["NomFichier"]),
                            NomOriginal = DbValue.TextFromDb(r["NomOriginal"]),
                            DateAjout = DbValue.DateFromDb(r["DateAjout"], DateTime.Now),
                            EstGeneree = DbValue.IntFromDb(r["EstGeneree"]) != 0
                        });
                    }
                }
            }
            return liste;
        }

        /// <summary>
        /// Verse un fichier au dossier : il est COPIÉ dans le répertoire de la procédure, de
        /// sorte que la pièce survive au déplacement ou à la suppression de l'original.
        /// </summary>
        public async Task<PieceDisciplinaire> AjouterPieceAsync(ProcedureDisciplinaire p, TypePiece type, string cheminSource, string libelle, bool estGeneree)
        {
            if (p == null || p.Id <= 0) throw new InvalidOperationException("Enregistrez la procédure avant d'y verser des pièces.");
            if (string.IsNullOrWhiteSpace(cheminSource) || !File.Exists(cheminSource))
                throw new InvalidOperationException("Fichier introuvable : " + cheminSource);

            var rep = RepertoireProcedure(p);
            Directory.CreateDirectory(rep);

            string nomOriginal = Path.GetFileName(cheminSource);
            string cible = Path.Combine(rep, nomOriginal);
            bool dejaEnPlace = string.Equals(Path.GetFullPath(cheminSource), Path.GetFullPath(cible), StringComparison.OrdinalIgnoreCase);
            if (!dejaEnPlace)
            {
                string nom = DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + "_" + NomDeFichierSur(nomOriginal);
                cible = Path.Combine(rep, nom);
                int i = 2;
                while (File.Exists(cible))
                    cible = Path.Combine(rep, Path.GetFileNameWithoutExtension(nom) + "_" + (i++) + Path.GetExtension(nom));
                File.Copy(cheminSource, cible);
            }

            var piece = new PieceDisciplinaire
            {
                ProcedureId = p.Id,
                Type = type,
                Libelle = string.IsNullOrWhiteSpace(libelle) ? LibellesDiscipline.Piece(type) : libelle.Trim(),
                NomFichier = Path.GetFileName(cible),
                NomOriginal = nomOriginal,
                DateAjout = DateTime.Now,
                EstGeneree = estGeneree
            };

            using (var conn = _sqlite.CreateConnection())
            {
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText =
                        "INSERT INTO PiecesDisciplinaires (ProcedureId, Type, Libelle, NomFichier, NomOriginal, DateAjout, EstGeneree) " +
                        "VALUES (@p, @t, @l, @f, @o, @d, @g);";
                    cmd.Parameters.AddWithValue("@p", piece.ProcedureId);
                    cmd.Parameters.AddWithValue("@t", (int)piece.Type);
                    cmd.Parameters.AddWithValue("@l", piece.Libelle);
                    cmd.Parameters.AddWithValue("@f", piece.NomFichier);
                    cmd.Parameters.AddWithValue("@o", piece.NomOriginal);
                    cmd.Parameters.AddWithValue("@d", DbValue.DateToDb(piece.DateAjout));
                    cmd.Parameters.AddWithValue("@g", piece.EstGeneree ? 1 : 0);
                    await cmd.ExecuteNonQueryAsync();
                }
                piece.Id = await DernierIdAsync(conn, null);
            }

            await _audit.EnregistrerAsync(estGeneree ? "Lettre disciplinaire générée" : "Pièce versée au dossier disciplinaire",
                EntiteAudit, p.Reference, details: piece.TypeLibelle + " — " + piece.NomOriginal);
            return piece;
        }

        /// <summary>Retire une pièce. Le dossier d'une sanction notifiée est figé : ses pièces restent.</summary>
        public async Task SupprimerPieceAsync(ProcedureDisciplinaire p, PieceDisciplinaire piece)
        {
            if (p == null || piece == null || piece.Id <= 0) return;
            if (p.Statut == StatutProcedure.Notifiee)
                throw new InvalidOperationException("Le dossier d'une sanction notifiée est figé : ses pièces ne peuvent plus être retirées (vous pouvez en ajouter).");

            using (var conn = _sqlite.CreateConnection())
                await ExecuterAsync(conn, null, "DELETE FROM PiecesDisciplinaires WHERE Id = @id;", piece.Id);

            try { File.Delete(CheminPiece(p, piece)); } catch { /* fichier déjà absent */ }

            await _audit.EnregistrerAsync("Pièce retirée du dossier disciplinaire", EntiteAudit, p.Reference,
                details: piece.TypeLibelle + " — " + piece.NomOriginal);
        }

        /// <summary>Chemin d'une nouvelle lettre à produire dans le répertoire de la procédure.</summary>
        public string CheminNouvelleLettre(ProcedureDisciplinaire p, string prefixe)
        {
            var rep = RepertoireProcedure(p);
            Directory.CreateDirectory(rep);
            string nom = string.Format(CultureInfo.InvariantCulture, "{0}_{1}_{2:yyyyMMdd-HHmmss}.pdf",
                NomDeFichierSur(prefixe), NomDeFichierSur(p.Reference ?? "procedure"), DateTime.Now);
            return Path.Combine(rep, nom);
        }

        // ═══════════════════════════════════════════════════════════════════
        //  Utilitaires
        // ═══════════════════════════════════════════════════════════════════

        private async Task AuditerAsync(ProcedureDisciplinaire avant, ProcedureDisciplinaire apres)
        {
            try
            {
                string sanction = apres.TypeSanction != null ? apres.TypeSanction.Libelle : "—";
                if (avant == null)
                {
                    await _audit.EnregistrerAsync("Ouverture procédure disciplinaire", EntiteAudit, apres.Reference,
                        details: string.Format("Salarié {0} — {1}, faits du {2:dd/MM/yyyy}", apres.EmployeeId, apres.CategorieFaits, apres.DateFaits));
                    return;
                }

                if (avant.Statut != apres.Statut)
                    await _audit.EnregistrerAsync("Étape procédure disciplinaire", EntiteAudit, apres.Reference, "Statut",
                        avant.StatutLibelle, apres.StatutLibelle, details: "Sanction : " + sanction);

                if (avant.DateNotification != apres.DateNotification || avant.TypeSanctionId != apres.TypeSanctionId
                    || avant.MiseAPiedJours != apres.MiseAPiedJours || avant.MiseAPiedDebut != apres.MiseAPiedDebut)
                    await _audit.EnregistrerAsync("Décision disciplinaire", EntiteAudit, apres.Reference, "Sanction",
                        avant.SanctionLibelle, apres.SanctionLibelle,
                        details: string.Format("Notifiée le {0}", LibellesDiscipline.Date(apres.DateNotification)));
                else if (avant.Statut == apres.Statut)
                    await _audit.EnregistrerAsync("Mise à jour procédure disciplinaire", EntiteAudit, apres.Reference,
                        details: "Étape : " + apres.StatutLibelle);
            }
            catch
            {
                // Le journal ne doit jamais faire échouer un enregistrement déjà validé.
            }
        }

        private static string UtilisateurCourant()
        {
            var u = UserSession.Instance.CurrentUser;
            if (u == null) return Environment.UserName;
            return string.IsNullOrWhiteSpace(u.DisplayName) ? u.Username : u.DisplayName;
        }

        private static string Nettoyer(string texte)
        {
            return string.IsNullOrWhiteSpace(texte) ? null : texte.Trim();
        }

        public static string NomDeFichierSur(string texte)
        {
            if (string.IsNullOrWhiteSpace(texte)) return "piece";
            var interdits = new HashSet<char>(Path.GetInvalidFileNameChars());
            var sb = new StringBuilder();
            foreach (var c in texte.Trim())
                sb.Append(interdits.Contains(c) || c == ' ' ? '_' : c);
            var nom = sb.ToString();
            return nom.Length > 80 ? nom.Substring(0, 80) : nom;
        }

        private static async Task<int> DernierIdAsync(SQLiteConnection conn, SQLiteTransaction tx)
        {
            using (var cmd = conn.CreateCommand())
            {
                if (tx != null) cmd.Transaction = tx;
                cmd.CommandText = "SELECT last_insert_rowid();";
                return DbValue.IntFromDb(await cmd.ExecuteScalarAsync());
            }
        }

        private static async Task ExecuterAsync(SQLiteConnection conn, SQLiteTransaction tx, string sql, int id)
        {
            using (var cmd = conn.CreateCommand())
            {
                if (tx != null) cmd.Transaction = tx;
                cmd.CommandText = sql;
                cmd.Parameters.AddWithValue("@id", id);
                await cmd.ExecuteNonQueryAsync();
            }
        }

        private static bool TableExiste(SQLiteConnection conn, string nom)
        {
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = @n;";
                cmd.Parameters.AddWithValue("@n", nom);
                return DbValue.IntFromDb(cmd.ExecuteScalar()) > 0;
            }
        }
    }
}
