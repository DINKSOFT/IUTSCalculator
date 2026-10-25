using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using IUTSCalculator.Models;

namespace IUTSCalculator.Services
{
    /// <summary>Compte rendu d'un transfert vers la paie.</summary>
    public class ResultatTransfertTemps
    {
        public ResultatTransfertTemps()
        {
            Ecartes = new List<string>();
        }

        public int Traites { get; set; }
        public List<string> Ecartes { get; private set; }

        public string Resume(string objet)
        {
            string texte = string.Format("{0} salarié(s) : {1} transféré(s).", Traites, objet);
            if (Ecartes.Count > 0)
                texte += string.Format(" {0} écarté(s) : {1}", Ecartes.Count, string.Join(" ; ", Ecartes.Take(5)))
                       + (Ecartes.Count > 5 ? " …" : string.Empty);
            return texte;
        }
    }

    /// <summary>Compte rendu du chargement des jours fériés légaux d'une année.</summary>
    public class ResultatChargementFeries
    {
        public ResultatChargementFeries()
        {
            FetesAMettreAJour = new List<string>();
        }

        public int Annee { get; set; }
        public int Ajoutes { get; set; }
        public int DejaPresents { get; set; }

        /// <summary>Fêtes musulmanes dont la date n'est pas encore publiée : à saisir à la main.</summary>
        public List<string> FetesAMettreAJour { get; private set; }

        public string Resume
        {
            get
            {
                string texte = Ajoutes == 0
                    ? string.Format("Jours fériés légaux de {0} : déjà à jour ({1} présent(s)), rien n'a été ajouté.", Annee, DejaPresents)
                    : string.Format("Jours fériés légaux de {0} : {1} ajouté(s){2}.", Annee, Ajoutes,
                        DejaPresents > 0 ? string.Format(", {0} déjà présent(s) conservé(s)", DejaPresents) : string.Empty);
                if (FetesAMettreAJour.Count > 0)
                    texte += " " + JoursFeriesLegaux.Rappel(Annee, FetesAMettreAJour);
                return texte;
            }
        }
    }

    /// <summary>
    /// Gestion des temps et des rotations (module RH-08) : équipes et cycles, planning jour
    /// par jour, jours fériés, paramètres, décompte de la période et transferts vers la paie
    /// (heures supplémentaires PAI-03, absences PAI-04, paniers PAI-02).
    ///
    /// Le décompte lui-même est dans <see cref="CalculTemps"/>, sans accès à la base.
    /// </summary>
    public class GestionTempsService
    {
        private readonly SQLiteService _sqliteService;
        private readonly DataService _dataService;

        /// <summary>Préfixe du commentaire des lignes produites par ce module dans les autres écrans.</summary>
        public const string Marqueur = "[RH-08]";

        public const string CodeRubriquePanier = "PANIER";

        public GestionTempsService(SQLiteService sqliteService, DataService dataService)
        {
            _sqliteService = sqliteService ?? throw new ArgumentNullException(nameof(sqliteService));
            _dataService = dataService ?? throw new ArgumentNullException(nameof(dataService));
        }

        // ═══════════════════════════════════════════════════════════════════
        //  Paramètres
        // ═══════════════════════════════════════════════════════════════════

        public async Task<ParametresTemps> ChargerParametresAsync()
        {
            using (var conn = _sqliteService.CreateConnection())
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "SELECT JourArrete, AJCompteDansSeuil FROM ParametresTemps WHERE Id = 1;";
                using (var reader = (SQLiteDataReader)await cmd.ExecuteReaderAsync())
                {
                    if (await reader.ReadAsync())
                    {
                        return new ParametresTemps
                        {
                            JourArrete = DbValue.IntFromDb(reader["JourArrete"]),
                            AJCompteDansSeuil = DbValue.IntFromDb(reader["AJCompteDansSeuil"]) != 0
                        };
                    }
                }
            }

            return new ParametresTemps();
        }

        public async Task EnregistrerParametresAsync(ParametresTemps parametres)
        {
            if (parametres == null) throw new ArgumentNullException(nameof(parametres));
            if (parametres.JourArrete < 0 || parametres.JourArrete > 27)
                throw new InvalidOperationException("Le jour d'arrêté doit être compris entre 1 et 27 (0 pour le mois civil).");

            using (var conn = _sqliteService.CreateConnection())
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText =
                    "INSERT INTO ParametresTemps (Id, JourArrete, AJCompteDansSeuil) VALUES (1, @j, @aj) " +
                    "ON CONFLICT(Id) DO UPDATE SET JourArrete = excluded.JourArrete, AJCompteDansSeuil = excluded.AJCompteDansSeuil;";
                cmd.Parameters.AddWithValue("@j", parametres.JourArrete);
                cmd.Parameters.AddWithValue("@aj", parametres.AJCompteDansSeuil ? 1 : 0);
                await cmd.ExecuteNonQueryAsync();
            }
        }

        public async Task<PeriodePointage> PeriodeAsync(int annee, int mois)
        {
            var parametres = await ChargerParametresAsync();
            return PeriodePointage.Pour(annee, mois, parametres.JourArrete);
        }

        // ═══════════════════════════════════════════════════════════════════
        //  Équipes et affectations
        // ═══════════════════════════════════════════════════════════════════

        public async Task<List<EquipeTravail>> ChargerEquipesAsync()
        {
            var liste = new List<EquipeTravail>();

            using (var conn = _sqliteService.CreateConnection())
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText =
                    "SELECT e.*, (SELECT COUNT(*) FROM EquipesMembres m WHERE m.EquipeId = e.Id) AS NbMembres " +
                    "FROM EquipesTravail e ORDER BY e.Active DESC, e.Code;";
                using (var reader = (SQLiteDataReader)await cmd.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                    {
                        liste.Add(new EquipeTravail
                        {
                            Id = DbValue.IntFromDb(reader["Id"]),
                            Code = DbValue.TextFromDb(reader["Code"]),
                            Libelle = DbValue.TextFromDb(reader["Libelle"]),
                            Site = DbValue.TextFromDb(reader["Site"]),
                            Motif = DbValue.TextFromDb(reader["Motif"]),
                            DateDebutCycle = DbValue.DateFromDb(reader["DateDebutCycle"], DateTime.Today),
                            DureePosteJour = DbValue.DecimalFromDb(reader["DureePosteJour"]),
                            DureePosteNuit = DbValue.DecimalFromDb(reader["DureePosteNuit"]),
                            HeuresNormalesParPoste = DbValue.DecimalFromDb(reader["HeuresNormalesParPoste"]),
                            ModeSemaine = (ModeSemaineDecompte)DbValue.IntFromDb(reader["ModeSemaine"]),
                            DimancheMajore = DbValue.IntFromDb(reader["DimancheMajore"]) != 0,
                            PanierMode = (ModePanier)DbValue.IntFromDb(reader["PanierMode"]),
                            PanierValeur = DbValue.DecimalFromDb(reader["PanierValeur"]),
                            Active = DbValue.IntFromDb(reader["Active"]) != 0,
                            Commentaire = DbValue.TextFromDb(reader["Commentaire"]),
                            NombreMembres = DbValue.IntFromDb(reader["NbMembres"])
                        });
                    }
                }
            }

            return liste;
        }

        public async Task EnregistrerEquipeAsync(EquipeTravail equipe)
        {
            if (equipe == null) throw new ArgumentNullException(nameof(equipe));

            equipe.Code = (equipe.Code ?? string.Empty).Trim().ToUpperInvariant();
            var anomalies = equipe.Anomalies();
            if (anomalies.Count > 0)
                throw new InvalidOperationException(string.Join(Environment.NewLine, anomalies));

            // Le cycle est enregistré sous sa forme normalisée (« 7J 7N 7R »).
            var cycle = equipe.Cycle();
            if (cycle != null) equipe.Motif = cycle.Normalise;

            using (var conn = _sqliteService.CreateConnection())
            {
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT COUNT(*) FROM EquipesTravail WHERE Code = @c AND Id <> @id;";
                    cmd.Parameters.AddWithValue("@c", equipe.Code);
                    cmd.Parameters.AddWithValue("@id", equipe.Id);
                    if (DbValue.IntFromDb(await cmd.ExecuteScalarAsync()) > 0)
                        throw new InvalidOperationException(string.Format("Une équipe porte déjà le code « {0} ».", equipe.Code));
                }

                using (var cmd = conn.CreateCommand())
                {
                    if (equipe.Id > 0)
                    {
                        cmd.CommandText =
                            "UPDATE EquipesTravail SET Code = @code, Libelle = @libelle, Site = @site, Motif = @motif, " +
                            "DateDebutCycle = @debut, DureePosteJour = @dj, DureePosteNuit = @dn, " +
                            "HeuresNormalesParPoste = @hn, ModeSemaine = @mode, DimancheMajore = @dim, " +
                            "PanierMode = @pm, PanierValeur = @pv, Active = @active, Commentaire = @comm WHERE Id = @id;";
                        cmd.Parameters.AddWithValue("@id", equipe.Id);
                    }
                    else
                    {
                        cmd.CommandText =
                            "INSERT INTO EquipesTravail (Code, Libelle, Site, Motif, DateDebutCycle, DureePosteJour, " +
                            "DureePosteNuit, HeuresNormalesParPoste, ModeSemaine, DimancheMajore, PanierMode, PanierValeur, " +
                            "Active, Commentaire, DateCreation) VALUES (@code, @libelle, @site, @motif, @debut, @dj, @dn, " +
                            "@hn, @mode, @dim, @pm, @pv, @active, @comm, @creation);";
                        cmd.Parameters.AddWithValue("@creation", DbValue.DateToDb(DateTime.Now));
                    }

                    cmd.Parameters.AddWithValue("@code", equipe.Code);
                    cmd.Parameters.AddWithValue("@libelle", equipe.Libelle ?? string.Empty);
                    cmd.Parameters.AddWithValue("@site", equipe.Site ?? string.Empty);
                    cmd.Parameters.AddWithValue("@motif", equipe.Motif ?? string.Empty);
                    cmd.Parameters.AddWithValue("@debut", DbValue.DateToDb(equipe.DateDebutCycle.Date));
                    cmd.Parameters.AddWithValue("@dj", DbValue.DecimalToDb(equipe.DureePosteJour));
                    cmd.Parameters.AddWithValue("@dn", DbValue.DecimalToDb(equipe.DureePosteNuit));
                    cmd.Parameters.AddWithValue("@hn", DbValue.DecimalToDb(equipe.HeuresNormalesParPoste));
                    cmd.Parameters.AddWithValue("@mode", (int)equipe.ModeSemaine);
                    cmd.Parameters.AddWithValue("@dim", equipe.DimancheMajore ? 1 : 0);
                    cmd.Parameters.AddWithValue("@pm", (int)equipe.PanierMode);
                    cmd.Parameters.AddWithValue("@pv", DbValue.DecimalToDb(equipe.PanierValeur));
                    cmd.Parameters.AddWithValue("@active", equipe.Active ? 1 : 0);
                    cmd.Parameters.AddWithValue("@comm", equipe.Commentaire ?? string.Empty);

                    await cmd.ExecuteNonQueryAsync();
                    if (equipe.Id <= 0)
                        equipe.Id = (int)conn.LastInsertRowId;
                }
            }
        }

        /// <summary>
        /// Supprime une équipe et ses affectations. Le planning déjà établi reste : ses jours
        /// gardent leurs codes et se décomptent avec l'équipe actuelle de chaque salarié.
        /// </summary>
        public async Task SupprimerEquipeAsync(EquipeTravail equipe)
        {
            if (equipe == null || equipe.Id <= 0) return;

            using (var conn = _sqliteService.CreateConnection())
            using (var tx = conn.BeginTransaction())
            {
                foreach (var sql in new[]
                {
                    "DELETE FROM EquipesMembres WHERE EquipeId = @id;",
                    "UPDATE PlanningJours SET EquipeId = NULL WHERE EquipeId = @id;",
                    "DELETE FROM EquipesTravail WHERE Id = @id;"
                })
                {
                    using (var cmd = conn.CreateCommand())
                    {
                        cmd.Transaction = tx;
                        cmd.CommandText = sql;
                        cmd.Parameters.AddWithValue("@id", equipe.Id);
                        await cmd.ExecuteNonQueryAsync();
                    }
                }
                tx.Commit();
            }
        }

        /// <summary>Équipe de chaque salarié affecté : matricule → identifiant d'équipe.</summary>
        public async Task<Dictionary<string, int>> ChargerAffectationsAsync()
        {
            var dico = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            using (var conn = _sqliteService.CreateConnection())
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "SELECT EmployeeId, EquipeId FROM EquipesMembres;";
                using (var reader = (SQLiteDataReader)await cmd.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                        dico[DbValue.TextFromDb(reader["EmployeeId"])] = DbValue.IntFromDb(reader["EquipeId"]);
                }
            }

            return dico;
        }

        /// <summary>Affecte des salariés à une équipe (un salarié n'appartient qu'à une équipe à la fois).</summary>
        public async Task AffecterAsync(IEnumerable<string> employes, int equipeId)
        {
            var liste = (employes ?? Enumerable.Empty<string>()).Where(e => !string.IsNullOrWhiteSpace(e)).Distinct().ToList();
            if (liste.Count == 0 || equipeId <= 0) return;

            using (var conn = _sqliteService.CreateConnection())
            using (var tx = conn.BeginTransaction())
            {
                foreach (var employe in liste)
                {
                    using (var cmd = conn.CreateCommand())
                    {
                        cmd.Transaction = tx;
                        cmd.CommandText =
                            "INSERT INTO EquipesMembres (EmployeeId, EquipeId, DateAffectation) VALUES (@e, @q, @d) " +
                            "ON CONFLICT(EmployeeId) DO UPDATE SET EquipeId = excluded.EquipeId, DateAffectation = excluded.DateAffectation;";
                        cmd.Parameters.AddWithValue("@e", employe);
                        cmd.Parameters.AddWithValue("@q", equipeId);
                        cmd.Parameters.AddWithValue("@d", DbValue.DateToDb(DateTime.Today));
                        await cmd.ExecuteNonQueryAsync();
                    }
                }
                tx.Commit();
            }
        }

        public async Task RetirerAsync(IEnumerable<string> employes)
        {
            var liste = (employes ?? Enumerable.Empty<string>()).Where(e => !string.IsNullOrWhiteSpace(e)).Distinct().ToList();
            if (liste.Count == 0) return;

            using (var conn = _sqliteService.CreateConnection())
            using (var tx = conn.BeginTransaction())
            {
                foreach (var employe in liste)
                {
                    using (var cmd = conn.CreateCommand())
                    {
                        cmd.Transaction = tx;
                        cmd.CommandText = "DELETE FROM EquipesMembres WHERE EmployeeId = @e;";
                        cmd.Parameters.AddWithValue("@e", employe);
                        await cmd.ExecuteNonQueryAsync();
                    }
                }
                tx.Commit();
            }
        }

        // ═══════════════════════════════════════════════════════════════════
        //  Jours fériés
        // ═══════════════════════════════════════════════════════════════════

        public async Task<List<JourFerie>> ChargerFeriesAsync()
        {
            var liste = new List<JourFerie>();

            using (var conn = _sqliteService.CreateConnection())
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "SELECT Id, DateJour, Libelle FROM JoursFeries ORDER BY DateJour DESC;";
                using (var reader = (SQLiteDataReader)await cmd.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                    {
                        liste.Add(new JourFerie
                        {
                            Id = DbValue.IntFromDb(reader["Id"]),
                            Date = DbValue.DateFromDb(reader["DateJour"], DateTime.Today).Date,
                            Libelle = DbValue.TextFromDb(reader["Libelle"])
                        });
                    }
                }
            }

            return liste;
        }

        public async Task AjouterFerieAsync(DateTime date, string libelle)
        {
            if (string.IsNullOrWhiteSpace(libelle))
                throw new InvalidOperationException("Indiquez le nom du jour férié.");

            using (var conn = _sqliteService.CreateConnection())
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText =
                    "INSERT INTO JoursFeries (DateJour, Libelle) VALUES (@d, @l) " +
                    "ON CONFLICT(DateJour) DO UPDATE SET Libelle = excluded.Libelle;";
                cmd.Parameters.AddWithValue("@d", DbValue.DateToDb(date.Date));
                cmd.Parameters.AddWithValue("@l", libelle.Trim());
                await cmd.ExecuteNonQueryAsync();
            }
        }

        /// <summary>
        /// Pré-charge les jours chômés et payés légaux d'une année (<see cref="JoursFeriesLegaux"/>,
        /// loi n° 001-2026/ALT) : seuls les jours absents de la liste sont ajoutés. Les jours
        /// saisis à la main ne sont ni remplacés ni renommés, rien n'est supprimé, et relancer
        /// le chargement n'ajoute aucun doublon.
        /// </summary>
        public async Task<ResultatChargementFeries> ChargerFeriesLegauxAsync(int annee)
        {
            if (annee < JoursFeriesLegaux.PremiereAnnee)
            {
                throw new InvalidOperationException(string.Format(
                    "Les jours fériés pré-chargés sont ceux de la {0}, en vigueur à partir de {1} : " +
                    "pour {2}, saisissez-les à la main.", JoursFeriesLegaux.Reference, JoursFeriesLegaux.PremiereAnnee, annee));
            }

            var existants = await ChargerFeriesAsync();
            int dejaPresents;
            var aAjouter = JoursFeriesLegaux.AAjouter(annee, existants, out dejaPresents);

            int ajoutes = 0;
            if (aAjouter.Count > 0)
            {
                using (var conn = _sqliteService.CreateConnection())
                using (var tx = conn.BeginTransaction())
                {
                    foreach (var j in aAjouter)
                    {
                        using (var cmd = conn.CreateCommand())
                        {
                            cmd.Transaction = tx;
                            // DO NOTHING : une date saisie entre-temps garde son nom.
                            cmd.CommandText =
                                "INSERT INTO JoursFeries (DateJour, Libelle) VALUES (@d, @l) ON CONFLICT(DateJour) DO NOTHING;";
                            cmd.Parameters.AddWithValue("@d", DbValue.DateToDb(j.Date.Date));
                            cmd.Parameters.AddWithValue("@l", j.Libelle);
                            ajoutes += await cmd.ExecuteNonQueryAsync();
                        }
                    }
                    tx.Commit();
                }
            }

            var resultat = new ResultatChargementFeries
            {
                Annee = annee,
                Ajoutes = ajoutes,
                DejaPresents = dejaPresents + (aAjouter.Count - ajoutes)
            };
            resultat.FetesAMettreAJour.AddRange(
                JoursFeriesLegaux.FetesAMettreAJour(annee, await ChargerFeriesAsync()).Select(f => f.Libelle));
            return resultat;
        }

        public async Task SupprimerFerieAsync(JourFerie ferie)
        {
            if (ferie == null || ferie.Id <= 0) return;

            using (var conn = _sqliteService.CreateConnection())
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "DELETE FROM JoursFeries WHERE Id = @id;";
                cmd.Parameters.AddWithValue("@id", ferie.Id);
                await cmd.ExecuteNonQueryAsync();
            }
        }

        // ═══════════════════════════════════════════════════════════════════
        //  Planning
        // ═══════════════════════════════════════════════════════════════════

        /// <summary>Jours de planning entre deux dates incluses, tous salariés.</summary>
        public async Task<List<PlanningJour>> ChargerPlanningAsync(DateTime debut, DateTime fin)
        {
            var liste = new List<PlanningJour>();

            using (var conn = _sqliteService.CreateConnection())
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText =
                    "SELECT Id, EmployeeId, DateJour, Code, Heures, EquipeId, Source, Commentaire FROM PlanningJours " +
                    "WHERE DateJour >= @debut AND DateJour <= @fin;";
                cmd.Parameters.AddWithValue("@debut", DbValue.DateToDb(debut.Date));
                cmd.Parameters.AddWithValue("@fin", DbValue.DateToDb(fin.Date));
                using (var reader = (SQLiteDataReader)await cmd.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                        liste.Add(LireJour(reader));
                }
            }

            return liste;
        }

        /// <summary>
        /// Enregistre des jours de planning (création ou remplacement). Un jour au code vide
        /// est supprimé. Les jours d'une période de paie clôturée sont refusés.
        /// </summary>
        public async Task<int> EnregistrerPlanningAsync(IEnumerable<PlanningJour> jours)
        {
            var liste = (jours ?? Enumerable.Empty<PlanningJour>())
                .Where(j => j != null && !string.IsNullOrWhiteSpace(j.EmployeeId))
                .ToList();
            if (liste.Count == 0) return 0;

            foreach (var j in liste)
            {
                string code = CodesPlanning.Normaliser(j.Code);
                if (code == null)
                    throw new InvalidOperationException(string.Format(
                        "{0} le {1:dd/MM/yyyy} : code « {2} » inconnu ({3}).",
                        j.EmployeeId, j.Date, j.Code, string.Join(", ", CodesPlanning.Tous)));
                j.Code = code;
            }

            int n = 0;
            using (var conn = _sqliteService.CreateConnection())
            {
                var parametres = await ChargerParametresAsync();
                await VerifierPeriodesOuvertesAsync(conn, liste.Select(j => j.Date), parametres);

                using (var tx = conn.BeginTransaction())
                {
                    try
                    {
                        string maintenant = DbValue.DateToDb(DateTime.Now);
                        foreach (var j in liste)
                        {
                            using (var cmd = conn.CreateCommand())
                            {
                                cmd.Transaction = tx;
                                if (string.IsNullOrEmpty(j.Code))
                                {
                                    cmd.CommandText = "DELETE FROM PlanningJours WHERE EmployeeId = @e AND DateJour = @d;";
                                }
                                else
                                {
                                    cmd.CommandText =
                                        "INSERT INTO PlanningJours (EmployeeId, DateJour, Code, Heures, EquipeId, Source, Commentaire, DateModification) " +
                                        "VALUES (@e, @d, @c, @h, @q, @s, @comm, @m) " +
                                        "ON CONFLICT(EmployeeId, DateJour) DO UPDATE SET Code = excluded.Code, Heures = excluded.Heures, " +
                                        "EquipeId = excluded.EquipeId, Source = excluded.Source, Commentaire = excluded.Commentaire, " +
                                        "DateModification = excluded.DateModification;";
                                    cmd.Parameters.AddWithValue("@c", j.Code);
                                    cmd.Parameters.AddWithValue("@h", j.Heures.HasValue ? (object)DbValue.DecimalToDb(j.Heures.Value) : DBNull.Value);
                                    cmd.Parameters.AddWithValue("@q", j.EquipeId.HasValue ? (object)j.EquipeId.Value : DBNull.Value);
                                    cmd.Parameters.AddWithValue("@s", (int)j.Source);
                                    cmd.Parameters.AddWithValue("@comm", j.Commentaire ?? string.Empty);
                                    cmd.Parameters.AddWithValue("@m", maintenant);
                                }
                                cmd.Parameters.AddWithValue("@e", j.EmployeeId);
                                cmd.Parameters.AddWithValue("@d", DbValue.DateToDb(j.Date.Date));
                                await cmd.ExecuteNonQueryAsync();
                                n++;
                            }
                        }
                        tx.Commit();
                    }
                    catch
                    {
                        try { tx.Rollback(); } catch { /* connexion perdue */ }
                        throw;
                    }
                }
            }

            return n;
        }

        /// <summary>
        /// Un jour de planning appartient à la période de paie qui le contient ; si cette
        /// période est clôturée, ses heures ont déjà été payées.
        /// </summary>
        private static async Task VerifierPeriodesOuvertesAsync(SQLiteConnection conn, IEnumerable<DateTime> dates, ParametresTemps parametres)
        {
            var mois = new HashSet<DateTime>();
            foreach (var d in dates)
                mois.Add(MoisDePaie(d.Date, parametres.JourArrete));

            foreach (var m in mois)
            {
                if (await PeriodePaieService.EstClotureeAsync(conn, m, null))
                {
                    throw new InvalidOperationException(string.Format(
                        "La période de paie de {0:MM/yyyy} est clôturée : son planning ne peut plus être modifié.", m));
                }
            }
        }

        /// <summary>Mois de paie (premier jour) auquel appartient une date, selon le jour d'arrêté.</summary>
        public static DateTime MoisDePaie(DateTime date, int jourArrete)
        {
            var premier = new DateTime(date.Year, date.Month, 1);
            if (jourArrete <= 0 || jourArrete >= 28) return premier;
            return date.Day > jourArrete ? premier.AddMonths(1) : premier;
        }

        /// <summary>
        /// Génère le planning d'une période depuis les cycles des équipes : chaque jour sans
        /// saisie reçoit le code du cycle. Un jour saisi à la main ou importé n'est jamais
        /// écrasé, sauf demande expresse. Un jour de travail couvert par une absence de
        /// « Congés et absences » reçoit le code de l'absence (CP, M, AJ, A).
        ///
        /// Les jours de la première semaine qui précèdent la période ne sont complétés que
        /// s'ils sont vides : ils appartiennent à la période précédente.
        /// </summary>
        public async Task<int> GenererDepuisCyclesAsync(PeriodePointage periode, int? equipeFiltre, bool ecraserSaisies)
        {
            if (periode == null) throw new ArgumentNullException(nameof(periode));

            var equipes = (await ChargerEquipesAsync()).Where(e => e.Active).ToDictionary(e => e.Id);
            var affectations = await ChargerAffectationsAsync();
            var debutCharge = periode.Debut.AddDays(-6);
            var existants = (await ChargerPlanningAsync(debutCharge, periode.Fin))
                .ToDictionary(j => Cle(j.EmployeeId, j.Date), StringComparer.OrdinalIgnoreCase);
            var absences = await ChargerAbsencesSaisiesAsync(debutCharge, periode.Fin);

            var aEcrire = new List<PlanningJour>();

            foreach (var affectation in affectations)
            {
                EquipeTravail equipe;
                if (!equipes.TryGetValue(affectation.Value, out equipe)) continue;
                if (equipeFiltre.HasValue && equipeFiltre.Value > 0 && equipe.Id != equipeFiltre.Value) continue;

                var cycle = equipe.Cycle();
                if (cycle == null) continue;

                var debut = CalculTemps.DebutChargement(periode, equipe.PremierJourSemaine);
                for (var d = debut; d <= periode.Fin; d = d.AddDays(1))
                {
                    if (d < equipe.DateDebutCycle) continue;

                    PlanningJour existant;
                    existants.TryGetValue(Cle(affectation.Key, d), out existant);

                    bool horsPeriode = d < periode.Debut;
                    if (existant != null)
                    {
                        if (horsPeriode) continue;
                        if (existant.Source != SourceJourPlanning.Cycle && existant.Source != SourceJourPlanning.Absence && !ecraserSaisies)
                            continue;
                    }

                    string code = cycle.CodePour(d, equipe.DateDebutCycle);
                    var source = SourceJourPlanning.Cycle;

                    Absence absence;
                    if (CodesPlanning.EstTravaille(code) && absences.TryGetValue(Cle(affectation.Key, d), out absence))
                    {
                        code = CodesPlanning.DepuisAbsence(absence.Type);
                        source = SourceJourPlanning.Absence;
                    }

                    if (existant != null && existant.Code == code && existant.Source == source && existant.EquipeId == equipe.Id)
                        continue;

                    aEcrire.Add(new PlanningJour
                    {
                        EmployeeId = affectation.Key,
                        Date = d,
                        Code = code,
                        EquipeId = equipe.Id,
                        Source = source
                    });
                }
            }

            if (aEcrire.Count == 0) return 0;

            // Les jours d'une période clôturée (première semaine) sont écartés, pas bloquants.
            var parametres = await ChargerParametresAsync();
            var clotures = new HashSet<DateTime>();
            using (var conn = _sqliteService.CreateConnection())
            {
                foreach (var m in aEcrire.Select(j => MoisDePaie(j.Date, parametres.JourArrete)).Distinct())
                {
                    if (await PeriodePaieService.EstClotureeAsync(conn, m, null))
                        clotures.Add(m);
                }
            }

            var ouverts = aEcrire.Where(j => !clotures.Contains(MoisDePaie(j.Date, parametres.JourArrete))).ToList();
            return await EnregistrerPlanningAsync(ouverts);
        }

        /// <summary>
        /// Absences saisies dans « Congés et absences » (hors relevés RH-08), jour par jour :
        /// clé matricule|date → absence.
        /// </summary>
        private async Task<Dictionary<string, Absence>> ChargerAbsencesSaisiesAsync(DateTime debut, DateTime fin)
        {
            var dico = new Dictionary<string, Absence>(StringComparer.OrdinalIgnoreCase);

            using (var conn = _sqliteService.CreateConnection())
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText =
                    "SELECT EmployeeId, Type, DateDebut, DateFin FROM Absences " +
                    "WHERE DateDebut <= @fin AND DateFin >= @debut " +
                    "AND (Commentaire IS NULL OR Commentaire NOT LIKE @marqueur);";
                cmd.Parameters.AddWithValue("@debut", DbValue.DateToDb(debut.Date));
                cmd.Parameters.AddWithValue("@fin", DbValue.DateToDb(fin.Date));
                cmd.Parameters.AddWithValue("@marqueur", AbsenceService.MarqueurReleveTemps + "%");

                using (var reader = (SQLiteDataReader)await cmd.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                    {
                        var a = new Absence
                        {
                            EmployeeId = DbValue.TextFromDb(reader["EmployeeId"]),
                            Type = (TypeAbsence)DbValue.IntFromDb(reader["Type"]),
                            DateDebut = DbValue.DateFromDb(reader["DateDebut"], DateTime.Today).Date,
                            DateFin = DbValue.DateFromDb(reader["DateFin"], DateTime.Today).Date
                        };

                        for (var d = a.DateDebut < debut.Date ? debut.Date : a.DateDebut;
                             d <= a.DateFin && d <= fin.Date; d = d.AddDays(1))
                        {
                            dico[Cle(a.EmployeeId, d)] = a;
                        }
                    }
                }
            }

            return dico;
        }

        // ═══════════════════════════════════════════════════════════════════
        //  Décompte de la période
        // ═══════════════════════════════════════════════════════════════════

        /// <summary>
        /// Décompte de la période de paie (<paramref name="annee"/>, <paramref name="mois"/>)
        /// pour les salariés affectés à une équipe (ou à l'équipe filtrée), et pour tout
        /// salarié qui a des jours de planning dans la période.
        /// </summary>
        public async Task<List<RecapTempsSalarie>> CalculerAsync(int annee, int mois, int? equipeFiltre)
        {
            var parametres = await ChargerParametresAsync();
            var periode = PeriodePointage.Pour(annee, mois, parametres.JourArrete);
            var bareme = await new BaremeHeuresSupService(_sqliteService).ApplicableAsync(annee, mois);

            var equipes = (await ChargerEquipesAsync()).ToDictionary(e => e.Id);
            var affectations = await ChargerAffectationsAsync();
            var feries = new HashSet<DateTime>((await ChargerFeriesAsync()).Select(f => f.Date.Date));

            // Une semaine de rotation peut commencer jusqu'à 6 jours avant la période.
            var planning = await ChargerPlanningAsync(periode.Debut.AddDays(-6), periode.Fin);
            var parSalarie = planning.GroupBy(j => j.EmployeeId, StringComparer.OrdinalIgnoreCase)
                                     .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

            var salaries = await _dataService.LoadEmployeesAsync();
            var recaps = new List<RecapTempsSalarie>();

            foreach (var salarie in salaries.Where(s => s != null && !string.IsNullOrWhiteSpace(s.EmployeeId)))
            {
                int equipeId;
                EquipeTravail equipe = null;
                if (affectations.TryGetValue(salarie.EmployeeId, out equipeId))
                    equipes.TryGetValue(equipeId, out equipe);

                List<PlanningJour> jours;
                parSalarie.TryGetValue(salarie.EmployeeId, out jours);
                bool aDesJours = jours != null && jours.Any(j => periode.Contient(j.Date));

                if (equipe == null && !aDesJours) continue;
                if (equipeFiltre.HasValue && equipeFiltre.Value > 0 && (equipe == null || equipe.Id != equipeFiltre.Value)) continue;

                // Un salarié sorti avant la période n'a plus rien à décompter.
                if (salarie.DateSortie.HasValue && salarie.DateSortie.Value.Date < periode.Debut && !aDesJours) continue;

                recaps.Add(CalculTemps.Decompter(salarie, periode, jours, equipe, equipes, feries, bareme, parametres));
            }

            return recaps.OrderBy(r => r.EquipeCode).ThenBy(r => r.NomComplet).ToList();
        }

        // ═══════════════════════════════════════════════════════════════════
        //  Transferts vers la paie
        // ═══════════════════════════════════════════════════════════════════

        /// <summary>
        /// Reporte les heures supplémentaires décomptées dans la saisie du mois (PAI-03),
        /// en remplaçant la saisie existante des salariés concernés. Les autres salariés
        /// ne sont pas touchés.
        /// </summary>
        public async Task<ResultatTransfertTemps> TransfererHeuresSupAsync(
            int annee, int mois, IEnumerable<RecapTempsSalarie> recaps, PeriodePointage periode)
        {
            var resultat = new ResultatTransfertTemps();
            var liste = (recaps ?? Enumerable.Empty<RecapTempsSalarie>()).Where(r => r != null).ToList();
            if (liste.Count == 0) return resultat;

            var periodeService = new PeriodePaieService(_sqliteService);
            var hsService = new HeureSupplementaireService(_sqliteService, new AuditService(_sqliteService), periodeService);

            var grille = await hsService.ChargerSaisieMoisAsync(annee, mois);
            var parSalarie = grille.ToDictionary(h => h.EmployeeId, StringComparer.OrdinalIgnoreCase);
            var modifiees = new List<HeureSupplementaire>();

            foreach (var recap in liste)
            {
                HeureSupplementaire ligne;
                if (!parSalarie.TryGetValue(recap.EmployeeId, out ligne))
                {
                    resultat.Ecartes.Add(recap.EmployeeId + " : salarié inactif ou sorti, absent de la saisie du mois");
                    continue;
                }

                ligne.Heures15 = recap.Tranche1;
                ligne.Heures30 = recap.Tranche2;
                ligne.Heures50 = recap.Nuit;
                ligne.Heures60 = recap.FerieJour;
                ligne.Heures100 = recap.FerieNuit;
                ligne.Commentaire = string.Format("{0} Relevé des temps, semaines du {1}",
                    Marqueur, recap.PeriodeDecompteLibelle);

                // Une ligne déjà en base mais ramenée à zéro doit être réécrite, pas ignorée.
                if (ligne.Id > 0 || recap.HeuresSup > 0m)
                    modifiees.Add(ligne);

                resultat.Traites++;
            }

            await hsService.EnregistrerGrilleAsync(annee, mois, modifiees, NomUtilisateur());
            return resultat;
        }

        /// <summary>
        /// Crée, dans « Congés et absences », une absence non rémunérée par série de jours
        /// « A » consécutifs, imputée au mois de paie. Les absences relevées lors d'un transfert
        /// précédent pour ce mois sont remplacées ; celles saisies à la main ne sont pas touchées.
        /// </summary>
        public async Task<ResultatTransfertTemps> TransfererAbsencesAsync(
            int annee, int mois, IEnumerable<RecapTempsSalarie> recaps, PeriodePointage periode)
        {
            var resultat = new ResultatTransfertTemps();
            var liste = (recaps ?? Enumerable.Empty<RecapTempsSalarie>()).Where(r => r != null).ToList();
            if (liste.Count == 0) return resultat;

            var nouvelles = liste.SelectMany(r => CalculTemps.AbsencesDepuisReleve(r, periode)).ToList();
            var absenceService = new AbsenceService(_sqliteService);
            var ecartes = await absenceService.RemplacerAbsencesReleveesAsync(
                annee, mois, liste.Select(r => r.EmployeeId), nouvelles);

            resultat.Ecartes.AddRange(ecartes);
            resultat.Traites = liste.Count(r => r.JoursAbsenceNonJustifiee > 0) -
                               ecartes.Count;
            if (resultat.Traites < 0) resultat.Traites = 0;
            return resultat;
        }

        /// <summary>
        /// Écrit la prime de panier de chaque salarié dans les variables du mois (rubrique
        /// PANIER), en remplaçant les lignes produites par un transfert précédent.
        /// </summary>
        public async Task<ResultatTransfertTemps> TransfererPaniersAsync(
            int annee, int mois, IEnumerable<RecapTempsSalarie> recaps, PeriodePointage periode)
        {
            var resultat = new ResultatTransfertTemps();
            var liste = (recaps ?? Enumerable.Empty<RecapTempsSalarie>()).Where(r => r != null).ToList();
            if (liste.Count == 0) return resultat;

            var rubriques = new RubriqueService(_sqliteService);
            if (!await rubriques.CodeExisteAsync(CodeRubriquePanier))
            {
                throw new InvalidOperationException(
                    "La rubrique « PANIER » n'existe pas dans le plan de paie. Créez-la (gain, montant fixe) " +
                    "depuis l'écran « Plan de paie », puis relancez le transfert.");
            }

            var variablesService = new VariablePaieService(_sqliteService);
            var variables = await variablesService.ChargerAsync(annee, mois);
            var cibles = new HashSet<string>(liste.Select(r => r.EmployeeId), StringComparer.OrdinalIgnoreCase);

            variables.RemoveAll(v => cibles.Contains(v.EmployeeId ?? "")
                                     && string.Equals(v.CodeRubrique, CodeRubriquePanier, StringComparison.OrdinalIgnoreCase)
                                     && (v.Commentaire ?? "").StartsWith(Marqueur, StringComparison.Ordinal));

            foreach (var recap in liste)
            {
                // Équipe sans prime de panier : rien à verser, ce n'est pas une anomalie.
                if (!recap.PanierActif || recap.Paniers <= 0) continue;

                if (recap.MontantPanier <= 0m)
                {
                    resultat.Ecartes.Add(recap.EmployeeId + " : panier à 0 FCFA (salaire de base ou valeur du panier à vérifier)");
                    continue;
                }

                variables.Add(new VariablePaie
                {
                    Annee = annee,
                    Mois = mois,
                    EmployeeId = recap.EmployeeId,
                    CodeRubrique = CodeRubriquePanier,
                    Quantite = recap.Paniers,
                    Montant = recap.MontantPanier,
                    Commentaire = string.Format("{0} {1} panier(s), période {2}", Marqueur, recap.Paniers,
                        periode != null ? periode.Libelle : string.Empty),
                    DateSaisie = DateTime.Now
                });
                resultat.Traites++;
            }

            await variablesService.EnregistrerToutAsync(annee, mois, variables);
            return resultat;
        }

        // ═══════════════════════════════════════════════════════════════════

        private static string NomUtilisateur()
        {
            return UserSession.Instance.CurrentUser?.Username ?? Environment.UserName;
        }

        private static string Cle(string employe, DateTime date)
        {
            return (employe ?? string.Empty) + "|" + date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }

        private static PlanningJour LireJour(SQLiteDataReader reader)
        {
            string heures = DbValue.TextFromDb(reader["Heures"]);
            return new PlanningJour
            {
                Id = DbValue.LongFromDb(reader["Id"]),
                EmployeeId = DbValue.TextFromDb(reader["EmployeeId"]),
                Date = DbValue.DateFromDb(reader["DateJour"], DateTime.Today).Date,
                Code = DbValue.TextFromDb(reader["Code"]),
                Heures = string.IsNullOrWhiteSpace(heures) ? (decimal?)null : DbValue.DecimalFromDb(heures),
                EquipeId = DbValue.IntNullableFromDb(reader["EquipeId"]),
                Source = (SourceJourPlanning)DbValue.IntFromDb(reader["Source"]),
                Commentaire = DbValue.TextFromDb(reader["Commentaire"])
            };
        }
    }
}
