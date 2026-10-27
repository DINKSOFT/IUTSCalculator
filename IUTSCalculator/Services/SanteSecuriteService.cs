using System;
using System.Collections.Generic;
using Microsoft.Data.Sqlite;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using IUTSCalculator.Models;

namespace IUTSCalculator.Services
{
    /// <summary>
    /// Santé & sécurité au travail (module RH-07) : visites médicales, EPI, accidents du
    /// travail. Passerelle PAI-04 : l'arrêt consécutif à un accident devient une absence
    /// « Accident du travail » (rémunérée : le salaire est maintenu, choix du client) marquée
    /// <see cref="MarqueurSST"/> ; les indemnités journalières de la CNSS sont calculées à
    /// part, pour être réclamées. Toute opération est tracée dans le journal d'audit.
    /// </summary>
    public class SanteSecuriteService
    {
        public const string MarqueurSST = "[RH-07]";
        private const string EntiteAudit = "Santé & sécurité";
        private const string LibelleArret = "période d'arrêt de travail";

        private readonly SQLiteService _sqlite;
        private readonly AuditService _audit;

        public SanteSecuriteService(SQLiteService sqliteService)
        {
            _sqlite = sqliteService ?? throw new ArgumentNullException(nameof(sqliteService));
            _audit = new AuditService(sqliteService);
        }

        // ═══════════════════════════════════════════════════════════════════
        //  Paramètres
        // ═══════════════════════════════════════════════════════════════════

        public async Task<ParametresSST> ChargerParametresAsync()
        {
            var p = ParametresSST.Defaut();
            using (var conn = _sqlite.CreerConnexion())
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "SELECT * FROM ParametresSST WHERE Id = 1;";
                using (var r = (SqliteDataReader)await cmd.ExecuteReaderAsync())
                {
                    if (await r.ReadAsync())
                    {
                        p.PeriodiciteVisiteMois = DbValue.IntFromDb(r["PeriodiciteVisiteMois"]);
                        p.RepriseApresArretJours = DbValue.IntFromDb(r["RepriseApresArretJours"]);
                        p.DelaiDeclarationJoursOuvrables = DbValue.IntFromDb(r["DelaiDeclarationJoursOuvrables"]);
                        p.TauxIndemniteJournaliere = DbValue.DecimalFromDb(r["TauxIndemniteJournaliere"]);
                        p.JoursMoisReference = DbValue.IntFromDb(r["JoursMoisReference"]);
                        p.AlerteAvanceJours = DbValue.IntFromDb(r["AlerteAvanceJours"]);
                    }
                }
            }
            return p;
        }

        public static string AnomalieParametres(ParametresSST p)
        {
            if (p == null) return "Paramètres vides.";
            if (p.PeriodiciteVisiteMois < 1 || p.PeriodiciteVisiteMois > 60) return "Périodicité des visites : entre 1 et 60 mois.";
            if (p.RepriseApresArretJours < 0 || p.RepriseApresArretJours > 365) return "Visite de reprise : entre 0 et 365 jours d'arrêt.";
            if (p.DelaiDeclarationJoursOuvrables < 1 || p.DelaiDeclarationJoursOuvrables > 10) return "Délai de déclaration : entre 1 et 10 jours ouvrables (48 heures ouvrables = 2).";
            if (p.TauxIndemniteJournaliere <= 0m || p.TauxIndemniteJournaliere > 1m) return "Taux de l'indemnité journalière : entre 0 et 100 %.";
            if (p.JoursMoisReference < 1 || p.JoursMoisReference > 31) return "Jours du mois de référence : entre 1 et 31.";
            if (p.AlerteAvanceJours < 0 || p.AlerteAvanceJours > 365) return "Anticipation des alertes : entre 0 et 365 jours.";
            return null;
        }

        public async Task EnregistrerParametresAsync(ParametresSST p)
        {
            var anomalie = AnomalieParametres(p);
            if (anomalie != null) throw new InvalidOperationException(anomalie);

            using (var conn = _sqlite.CreerConnexion())
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText =
                    "INSERT INTO ParametresSST (Id, PeriodiciteVisiteMois, RepriseApresArretJours, DelaiDeclarationJoursOuvrables, " +
                    "TauxIndemniteJournaliere, JoursMoisReference, AlerteAvanceJours) VALUES (1, @p, @r, @d, @t, @j, @a) " +
                    "ON CONFLICT(Id) DO UPDATE SET PeriodiciteVisiteMois = @p, RepriseApresArretJours = @r, " +
                    "DelaiDeclarationJoursOuvrables = @d, TauxIndemniteJournaliere = @t, JoursMoisReference = @j, AlerteAvanceJours = @a;";
                cmd.Parameters.AjouterValeur("@p", p.PeriodiciteVisiteMois);
                cmd.Parameters.AjouterValeur("@r", p.RepriseApresArretJours);
                cmd.Parameters.AjouterValeur("@d", p.DelaiDeclarationJoursOuvrables);
                cmd.Parameters.AjouterValeur("@t", DbValue.DecimalToDb(p.TauxIndemniteJournaliere));
                cmd.Parameters.AjouterValeur("@j", p.JoursMoisReference);
                cmd.Parameters.AjouterValeur("@a", p.AlerteAvanceJours);
                await cmd.ExecuteNonQueryAsync();
            }

            await Auditer("Paramètres santé & sécurité", "Paramètres", string.Format(CultureInfo.InvariantCulture,
                "Visites tous les {0} mois, reprise après {1} j, déclaration {2} j ouvrables, IJ {3:0.##} %",
                p.PeriodiciteVisiteMois, p.RepriseApresArretJours, p.DelaiDeclarationJoursOuvrables, p.TauxIndemniteJournaliere * 100m));
        }

        /// <summary>Postes actifs (RH-02) avec leur périodicité propre éventuelle.</summary>
        public async Task<List<PeriodicitePoste>> ChargerPeriodicitesAsync()
        {
            var liste = new List<PeriodicitePoste>();
            using (var conn = _sqlite.CreerConnexion())
            {
                if (!TableExiste(conn, "Postes")) return liste;
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText =
                        "SELECT p.Id, p.Code, p.Intitule, pv.Mois, " +
                        "(SELECT COUNT(*) FROM Employees e WHERE e.PosteId = p.Id AND e.StatutSalarie = 0) AS Titulaires " +
                        "FROM Postes p LEFT JOIN PeriodicitesVisitePoste pv ON pv.PosteId = p.Id " +
                        "WHERE p.Actif = 1 ORDER BY p.Code;";
                    using (var r = (SqliteDataReader)await cmd.ExecuteReaderAsync())
                    {
                        while (await r.ReadAsync())
                        {
                            liste.Add(new PeriodicitePoste
                            {
                                PosteId = DbValue.IntFromDb(r["Id"]),
                                Code = DbValue.TextFromDb(r["Code"]),
                                Intitule = DbValue.TextFromDb(r["Intitule"]),
                                Mois = DbValue.IntNullableFromDb(r["Mois"]),
                                Titulaires = DbValue.IntFromDb(r["Titulaires"])
                            });
                        }
                    }
                }
            }
            return liste;
        }

        public async Task EnregistrerPeriodicitesAsync(IEnumerable<PeriodicitePoste> periodicites)
        {
            var liste = (periodicites ?? Enumerable.Empty<PeriodicitePoste>()).ToList();
            if (liste.Any(p => p.Mois.HasValue && (p.Mois.Value < 1 || p.Mois.Value > 60)))
                throw new InvalidOperationException("La périodicité d'un poste est comprise entre 1 et 60 mois (vide : celle du dossier).");

            using (var conn = _sqlite.CreerConnexion())
            using (var tx = conn.BeginTransaction())
            {
                using (var cmd = conn.CreateCommand())
                {
                    cmd.Transaction = tx;
                    cmd.CommandText = "DELETE FROM PeriodicitesVisitePoste;";
                    await cmd.ExecuteNonQueryAsync();
                }
                foreach (var p in liste.Where(x => x.Mois.HasValue))
                {
                    using (var cmd = conn.CreateCommand())
                    {
                        cmd.Transaction = tx;
                        cmd.CommandText = "INSERT INTO PeriodicitesVisitePoste (PosteId, Mois) VALUES (@p, @m);";
                        cmd.Parameters.AjouterValeur("@p", p.PosteId);
                        cmd.Parameters.AjouterValeur("@m", p.Mois.Value);
                        await cmd.ExecuteNonQueryAsync();
                    }
                }
                tx.Commit();
            }

            await Auditer("Périodicité des visites par poste", "Postes",
                string.Join(", ", liste.Where(x => x.Mois.HasValue).Select(x => x.Code + " : " + x.Mois.Value + " mois")));
        }

        // ═══════════════════════════════════════════════════════════════════
        //  Visites médicales
        // ═══════════════════════════════════════════════════════════════════

        public async Task<List<VisiteMedicale>> ChargerVisitesAsync()
        {
            var liste = new List<VisiteMedicale>();
            using (var conn = _sqlite.CreerConnexion())
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText =
                    "SELECT v.*, e.FirstName, e.LastName FROM VisitesMedicales v LEFT JOIN Employees e ON e.EmployeeId = v.EmployeeId " +
                    "ORDER BY COALESCE(NULLIF(v.DateVisite, ''), v.DatePrevue) DESC, v.Id DESC;";
                using (var r = (SqliteDataReader)await cmd.ExecuteReaderAsync())
                {
                    while (await r.ReadAsync())
                    {
                        liste.Add(new VisiteMedicale
                        {
                            Id = DbValue.IntFromDb(r["Id"]),
                            EmployeeId = DbValue.TextFromDb(r["EmployeeId"]),
                            NomSalarie = Nom(r),
                            Type = (TypeVisiteMedicale)DbValue.IntFromDb(r["Type"]),
                            DatePrevue = DbValue.DateNullableFromDb(r["DatePrevue"]),
                            DateVisite = DbValue.DateNullableFromDb(r["DateVisite"]),
                            Medecin = DbValue.TextFromDb(r["Medecin"]),
                            Aptitude = (AptitudeMedicale)DbValue.IntFromDb(r["Aptitude"]),
                            Restrictions = DbValue.TextFromDb(r["Restrictions"]),
                            ProchaineVisite = DbValue.DateNullableFromDb(r["ProchaineVisite"]),
                            AccidentId = DbValue.IntNullableFromDb(r["AccidentId"]),
                            Commentaire = DbValue.TextFromDb(r["Commentaire"])
                        });
                    }
                }
            }
            return liste;
        }

        public async Task EnregistrerVisiteAsync(VisiteMedicale v)
        {
            var anomalie = ControleSanteSecurite.AnomalieVisite(v, DateTime.Today);
            if (anomalie != null) throw new InvalidOperationException(anomalie);

            using (var conn = _sqlite.CreerConnexion())
            using (var cmd = conn.CreateCommand())
            {
                if (v.Id > 0)
                {
                    cmd.CommandText =
                        "UPDATE VisitesMedicales SET EmployeeId = @e, Type = @t, DatePrevue = @dp, DateVisite = @dv, Medecin = @m, " +
                        "Aptitude = @a, Restrictions = @r, ProchaineVisite = @pv, AccidentId = @acc, Commentaire = @c WHERE Id = @id;";
                    cmd.Parameters.AjouterValeur("@id", v.Id);
                }
                else
                {
                    cmd.CommandText =
                        "INSERT INTO VisitesMedicales (EmployeeId, Type, DatePrevue, DateVisite, Medecin, Aptitude, Restrictions, " +
                        "ProchaineVisite, AccidentId, Commentaire, DateSaisie) VALUES (@e, @t, @dp, @dv, @m, @a, @r, @pv, @acc, @c, @s);";
                    cmd.Parameters.AjouterValeur("@s", DbValue.DateToDb(DateTime.Now));
                }
                cmd.Parameters.AjouterValeur("@e", v.EmployeeId.Trim());
                cmd.Parameters.AjouterValeur("@t", (int)v.Type);
                cmd.Parameters.AjouterValeur("@dp", DbValue.DateNullableToDb(v.DatePrevue));
                cmd.Parameters.AjouterValeur("@dv", DbValue.DateNullableToDb(v.DateVisite));
                cmd.Parameters.AjouterValeur("@m", (object)Nettoyer(v.Medecin) ?? DBNull.Value);
                cmd.Parameters.AjouterValeur("@a", (int)v.Aptitude);
                cmd.Parameters.AjouterValeur("@r", (object)Nettoyer(v.Restrictions) ?? DBNull.Value);
                cmd.Parameters.AjouterValeur("@pv", DbValue.DateNullableToDb(v.ProchaineVisite));
                cmd.Parameters.AjouterValeur("@acc", v.AccidentId.HasValue ? (object)v.AccidentId.Value : DBNull.Value);
                cmd.Parameters.AjouterValeur("@c", (object)Nettoyer(v.Commentaire) ?? DBNull.Value);
                await cmd.ExecuteNonQueryAsync();
                if (v.Id <= 0) v.Id = await DernierIdAsync(conn);
            }

            // L'avis du médecin est une donnée de santé : seul le fait est tracé, pas les réserves.
            await Auditer("Visite médicale", v.EmployeeId, string.Format("{0} — {1} {2}", v.TypeLibelle,
                v.DateVisite.HasValue ? "passée le" : "prévue le", LibellesSST.Date(v.DateVisite ?? v.DatePrevue)));
        }

        public async Task SupprimerVisiteAsync(VisiteMedicale v)
        {
            if (v == null || v.Id <= 0) return;
            using (var conn = _sqlite.CreerConnexion())
                await Executer(conn, "DELETE FROM VisitesMedicales WHERE Id = @id;", v.Id);
            await Auditer("Suppression visite médicale", v.EmployeeId, v.TypeLibelle + " du " + v.DateLibelle);
        }

        // ═══════════════════════════════════════════════════════════════════
        //  EPI
        // ═══════════════════════════════════════════════════════════════════

        public async Task<List<TypeEPI>> ChargerTypesEpiAsync()
        {
            var liste = new List<TypeEPI>();
            using (var conn = _sqlite.CreerConnexion())
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "SELECT * FROM TypesEPI ORDER BY Libelle;";
                using (var r = (SqliteDataReader)await cmd.ExecuteReaderAsync())
                {
                    while (await r.ReadAsync())
                    {
                        liste.Add(new TypeEPI
                        {
                            Id = DbValue.IntFromDb(r["Id"]),
                            Libelle = DbValue.TextFromDb(r["Libelle"]),
                            DureeVieMois = DbValue.IntFromDb(r["DureeVieMois"]),
                            Norme = DbValue.TextFromDb(r["Norme"]),
                            Actif = DbValue.IntFromDb(r["Actif"]) != 0
                        });
                    }
                }
            }
            return liste;
        }

        public async Task EnregistrerTypeEpiAsync(TypeEPI t)
        {
            if (t == null || string.IsNullOrWhiteSpace(t.Libelle)) throw new InvalidOperationException("Indiquez le nom de l'équipement.");
            if (t.DureeVieMois < 0 || t.DureeVieMois > 240) throw new InvalidOperationException("Durée de vie : entre 0 (sans échéance) et 240 mois.");
            var existants = await ChargerTypesEpiAsync();
            if (existants.Any(x => x.Id != t.Id && string.Equals(x.Libelle.Trim(), t.Libelle.Trim(), StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException("Cet équipement existe déjà dans le catalogue.");

            using (var conn = _sqlite.CreerConnexion())
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = t.Id > 0
                    ? "UPDATE TypesEPI SET Libelle = @l, DureeVieMois = @d, Norme = @n, Actif = @a WHERE Id = @id;"
                    : "INSERT INTO TypesEPI (Libelle, DureeVieMois, Norme, Actif) VALUES (@l, @d, @n, @a);";
                if (t.Id > 0) cmd.Parameters.AjouterValeur("@id", t.Id);
                cmd.Parameters.AjouterValeur("@l", t.Libelle.Trim());
                cmd.Parameters.AjouterValeur("@d", t.DureeVieMois);
                cmd.Parameters.AjouterValeur("@n", (object)Nettoyer(t.Norme) ?? DBNull.Value);
                cmd.Parameters.AjouterValeur("@a", t.Actif ? 1 : 0);
                await cmd.ExecuteNonQueryAsync();
                if (t.Id <= 0) t.Id = await DernierIdAsync(conn);
            }
            await Auditer("Catalogue des EPI", t.Libelle, t.DureeLibelle);
        }

        public async Task SupprimerTypeEpiAsync(TypeEPI t)
        {
            if (t == null || t.Id <= 0) return;
            using (var conn = _sqlite.CreerConnexion())
            {
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT COUNT(*) FROM DotationsEPI WHERE TypeEpiId = @id;";
                    cmd.Parameters.AjouterValeur("@id", t.Id);
                    if (DbValue.IntFromDb(await cmd.ExecuteScalarAsync()) > 0)
                        throw new InvalidOperationException("Cet équipement a déjà été remis : désactivez-le plutôt que de le supprimer.");
                }
                await Executer(conn, "DELETE FROM TypesEPI WHERE Id = @id;", t.Id);
            }
            await Auditer("Suppression EPI du catalogue", t.Libelle, null);
        }

        public async Task<List<DotationEPI>> ChargerDotationsAsync()
        {
            var liste = new List<DotationEPI>();
            using (var conn = _sqlite.CreerConnexion())
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText =
                    "SELECT d.*, t.Libelle AS TypeLibelle, e.FirstName, e.LastName FROM DotationsEPI d " +
                    "LEFT JOIN TypesEPI t ON t.Id = d.TypeEpiId LEFT JOIN Employees e ON e.EmployeeId = d.EmployeeId " +
                    "ORDER BY d.DateRemise DESC, d.Id DESC;";
                using (var r = (SqliteDataReader)await cmd.ExecuteReaderAsync())
                {
                    while (await r.ReadAsync())
                    {
                        liste.Add(new DotationEPI
                        {
                            Id = DbValue.IntFromDb(r["Id"]),
                            EmployeeId = DbValue.TextFromDb(r["EmployeeId"]),
                            NomSalarie = Nom(r),
                            TypeEpiId = DbValue.IntFromDb(r["TypeEpiId"]),
                            TypeEpiLibelle = DbValue.TextFromDb(r["TypeLibelle"]),
                            DateRemise = DbValue.DateFromDb(r["DateRemise"], DateTime.Today),
                            Quantite = DbValue.IntFromDb(r["Quantite"]),
                            Taille = DbValue.TextFromDb(r["Taille"]),
                            DateRenouvellement = DbValue.DateNullableFromDb(r["DateRenouvellement"]),
                            Etat = (EtatDotation)DbValue.IntFromDb(r["Etat"]),
                            DateRetour = DbValue.DateNullableFromDb(r["DateRetour"]),
                            Commentaire = DbValue.TextFromDb(r["Commentaire"])
                        });
                    }
                }
            }
            return liste;
        }

        /// <summary>Enregistre une remise ; sans date de renouvellement, elle est proposée d'après la durée de vie de l'EPI.</summary>
        public async Task EnregistrerDotationAsync(DotationEPI d)
        {
            var anomalie = ControleSanteSecurite.AnomalieDotation(d);
            if (anomalie != null) throw new InvalidOperationException(anomalie);

            var type = (await ChargerTypesEpiAsync()).FirstOrDefault(t => t.Id == d.TypeEpiId);
            if (type == null) throw new InvalidOperationException("Cet équipement n'existe plus dans le catalogue.");
            d.TypeEpiLibelle = type.Libelle;
            if (!d.DateRenouvellement.HasValue && type.DureeVieMois > 0)
                d.DateRenouvellement = d.DateRemise.AddMonths(type.DureeVieMois);

            using (var conn = _sqlite.CreerConnexion())
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = d.Id > 0
                    ? "UPDATE DotationsEPI SET EmployeeId = @e, TypeEpiId = @t, DateRemise = @dr, Quantite = @q, Taille = @ta, " +
                      "DateRenouvellement = @dn, Etat = @et, DateRetour = @dret, Commentaire = @c WHERE Id = @id;"
                    : "INSERT INTO DotationsEPI (EmployeeId, TypeEpiId, DateRemise, Quantite, Taille, DateRenouvellement, Etat, DateRetour, Commentaire) " +
                      "VALUES (@e, @t, @dr, @q, @ta, @dn, @et, @dret, @c);";
                if (d.Id > 0) cmd.Parameters.AjouterValeur("@id", d.Id);
                cmd.Parameters.AjouterValeur("@e", d.EmployeeId.Trim());
                cmd.Parameters.AjouterValeur("@t", d.TypeEpiId);
                cmd.Parameters.AjouterValeur("@dr", DbValue.DateToDb(d.DateRemise.Date));
                cmd.Parameters.AjouterValeur("@q", d.Quantite);
                cmd.Parameters.AjouterValeur("@ta", (object)Nettoyer(d.Taille) ?? DBNull.Value);
                cmd.Parameters.AjouterValeur("@dn", DbValue.DateNullableToDb(d.DateRenouvellement));
                cmd.Parameters.AjouterValeur("@et", (int)d.Etat);
                cmd.Parameters.AjouterValeur("@dret", DbValue.DateNullableToDb(d.DateRetour));
                cmd.Parameters.AjouterValeur("@c", (object)Nettoyer(d.Commentaire) ?? DBNull.Value);
                await cmd.ExecuteNonQueryAsync();
                if (d.Id <= 0) d.Id = await DernierIdAsync(conn);
            }
            await Auditer("Remise d'EPI", d.EmployeeId, string.Format("{0} × {1}, le {2}, {3}", d.Quantite, d.TypeEpiLibelle, LibellesSST.Date(d.DateRemise), d.EtatLibelle));
        }

        public async Task SupprimerDotationAsync(DotationEPI d)
        {
            if (d == null || d.Id <= 0) return;
            using (var conn = _sqlite.CreerConnexion())
                await Executer(conn, "DELETE FROM DotationsEPI WHERE Id = @id;", d.Id);
            await Auditer("Suppression remise d'EPI", d.EmployeeId, d.TypeEpiLibelle + " du " + LibellesSST.Date(d.DateRemise));
        }

        // ═══════════════════════════════════════════════════════════════════
        //  Accidents du travail
        // ═══════════════════════════════════════════════════════════════════

        public async Task<List<AccidentTravail>> ChargerAccidentsAsync()
        {
            var liste = new List<AccidentTravail>();
            using (var conn = _sqlite.CreerConnexion())
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText =
                    "SELECT a.*, e.FirstName, e.LastName FROM AccidentsTravail a LEFT JOIN Employees e ON e.EmployeeId = a.EmployeeId " +
                    "ORDER BY a.DateAccident DESC, a.Id DESC;";
                using (var r = (SqliteDataReader)await cmd.ExecuteReaderAsync())
                {
                    while (await r.ReadAsync())
                    {
                        liste.Add(new AccidentTravail
                        {
                            Id = DbValue.IntFromDb(r["Id"]),
                            Reference = DbValue.TextFromDb(r["Reference"]),
                            EmployeeId = DbValue.TextFromDb(r["EmployeeId"]),
                            NomSalarie = Nom(r),
                            Nature = (NatureAccident)DbValue.IntFromDb(r["Nature"]),
                            DateAccident = DbValue.DateFromDb(r["DateAccident"], DateTime.Today),
                            Heure = DbValue.TextFromDb(r["Heure"]),
                            Lieu = DbValue.TextFromDb(r["Lieu"]),
                            Circonstances = DbValue.TextFromDb(r["Circonstances"]),
                            SiegeLesion = DbValue.TextFromDb(r["SiegeLesion"]),
                            NatureLesion = DbValue.TextFromDb(r["NatureLesion"]),
                            Temoins = DbValue.TextFromDb(r["Temoins"]),
                            PremiersSoins = DbValue.TextFromDb(r["PremiersSoins"]),
                            AvecArret = DbValue.IntFromDb(r["AvecArret"]) != 0,
                            DebutArret = DbValue.DateNullableFromDb(r["DebutArret"]),
                            FinArret = DbValue.DateNullableFromDb(r["FinArret"]),
                            DateReprise = DbValue.DateNullableFromDb(r["DateReprise"]),
                            Mortel = DbValue.IntFromDb(r["Mortel"]) != 0,
                            DateDeclarationCnss = DbValue.DateNullableFromDb(r["DateDeclarationCnss"]),
                            DateDeclarationInspection = DbValue.DateNullableFromDb(r["DateDeclarationInspection"]),
                            ReferenceCnss = DbValue.TextFromDb(r["ReferenceCnss"]),
                            SalaireJournalier = DbValue.MoneyFromDb(r["SalaireJournalier"]),
                            TauxIpp = DbValue.DecimalFromDb(r["TauxIpp"]),
                            AbsenceId = DbValue.IntNullableFromDb(r["AbsenceId"]),
                            Commentaire = DbValue.TextFromDb(r["Commentaire"]),
                            // Colonnes de la migration 38 : absentes d'une base pas encore mise à niveau.
                            DateDepotIJ = DbValue.DateNullableFromDb(Colonne(r, "DateDepotIJ")),
                            MontantRembourseIJ = DbValue.MoneyFromDb(Colonne(r, "MontantRembourseIJ")),
                            DateRemboursementIJ = DbValue.DateNullableFromDb(Colonne(r, "DateRemboursementIJ"))
                        });
                    }
                }
            }
            return liste;
        }

        /// <summary>
        /// Enregistre un accident. Une alerte bloquante refuse l'enregistrement. L'arrêt
        /// (dates connues) est reporté dans les congés et absences dans la même transaction :
        /// si la période de paie est clôturée ou que l'arrêt chevauche une autre absence, rien
        /// n'est écrit.
        /// </summary>
        public async Task<List<string>> EnregistrerAccidentAsync(AccidentTravail a, IEnumerable<VisiteMedicale> visites = null)
        {
            if (a == null) throw new ArgumentNullException(nameof(a));
            var messages = new List<string>();
            var prm = await ChargerParametresAsync();
            var feries = await ChargerFeriesAsync();
            var blocage = ControleSanteSecurite.PremierBlocage(ControleSanteSecurite.AnalyserAccident(a, prm, feries, visites, DateTime.Today));
            if (blocage != null) throw new InvalidOperationException(blocage);

            bool nouveau = a.Id <= 0;
            var sauvegarde = new { a.Id, a.Reference, a.AbsenceId };

            using (var conn = _sqlite.CreerConnexion())
            using (var tx = conn.BeginTransaction())
            {
                try
                {
                    if (nouveau)
                    {
                        a.Reference = await ProchaineReferenceAsync(conn, tx, a.DateAccident.Year);
                        using (var cmd = conn.CreateCommand())
                        {
                            cmd.Transaction = tx;
                            cmd.CommandText = "INSERT INTO AccidentsTravail (Reference, EmployeeId, DateAccident, DateCreation, DateModification) VALUES (@r, @e, @d, @n, @n);";
                            cmd.Parameters.AjouterValeur("@r", a.Reference);
                            cmd.Parameters.AjouterValeur("@e", a.EmployeeId);
                            cmd.Parameters.AjouterValeur("@d", DbValue.DateToDb(a.DateAccident));
                            cmd.Parameters.AjouterValeur("@n", DbValue.DateToDb(DateTime.Now));
                            await cmd.ExecuteNonQueryAsync();
                        }
                        a.Id = await DernierIdAsync(conn, tx);
                    }

                    // Arrêt → absence « Accident du travail » (rémunérée) dans PAI-04.
                    Absence voulue = null;
                    if (a.AvecArret && !a.Mortel && a.DebutArret.HasValue && a.FinArret.HasValue)
                    {
                        decimal jours = Absence.JoursEntre(a.DebutArret.Value, a.FinArret.Value, true);
                        if (jours <= 0m) jours = Absence.JoursEntre(a.DebutArret.Value, a.FinArret.Value, false);
                        voulue = new Absence
                        {
                            EmployeeId = a.EmployeeId,
                            Type = TypeAbsence.AccidentTravail,
                            DateDebut = a.DebutArret.Value,
                            DateFin = a.FinArret.Value,
                            NombreJours = jours,
                            Commentaire = MarqueurSST + " Arrêt suite à " + a.NatureLibelle.ToLower(LibellesSST.Fr) + " " + a.Reference
                        };
                    }
                    a.AbsenceId = await AppliquerArretAsync(conn, tx, a.AbsenceId, voulue, messages);

                    using (var cmd = conn.CreateCommand())
                    {
                        cmd.Transaction = tx;
                        cmd.CommandText =
                            "UPDATE AccidentsTravail SET EmployeeId = @e, Nature = @na, DateAccident = @d, Heure = @h, Lieu = @l, " +
                            "Circonstances = @c, SiegeLesion = @sl, NatureLesion = @nl, Temoins = @t, PremiersSoins = @ps, AvecArret = @aa, " +
                            "DebutArret = @da, FinArret = @fa, DateReprise = @dr, Mortel = @m, DateDeclarationCnss = @dc, " +
                            "DateDeclarationInspection = @di, ReferenceCnss = @rc, SalaireJournalier = @sj, TauxIpp = @ipp, " +
                            "AbsenceId = @abs, Commentaire = @com, DateModification = @mod WHERE Id = @id;";
                        cmd.Parameters.AjouterValeur("@id", a.Id);
                        cmd.Parameters.AjouterValeur("@e", a.EmployeeId);
                        cmd.Parameters.AjouterValeur("@na", (int)a.Nature);
                        cmd.Parameters.AjouterValeur("@d", DbValue.DateToDb(a.DateAccident));
                        cmd.Parameters.AjouterValeur("@h", (object)Nettoyer(a.Heure) ?? DBNull.Value);
                        cmd.Parameters.AjouterValeur("@l", (object)Nettoyer(a.Lieu) ?? DBNull.Value);
                        cmd.Parameters.AjouterValeur("@c", (object)Nettoyer(a.Circonstances) ?? DBNull.Value);
                        cmd.Parameters.AjouterValeur("@sl", (object)Nettoyer(a.SiegeLesion) ?? DBNull.Value);
                        cmd.Parameters.AjouterValeur("@nl", (object)Nettoyer(a.NatureLesion) ?? DBNull.Value);
                        cmd.Parameters.AjouterValeur("@t", (object)Nettoyer(a.Temoins) ?? DBNull.Value);
                        cmd.Parameters.AjouterValeur("@ps", (object)Nettoyer(a.PremiersSoins) ?? DBNull.Value);
                        cmd.Parameters.AjouterValeur("@aa", a.AvecArret ? 1 : 0);
                        cmd.Parameters.AjouterValeur("@da", DbValue.DateNullableToDb(a.DebutArret));
                        cmd.Parameters.AjouterValeur("@fa", DbValue.DateNullableToDb(a.FinArret));
                        cmd.Parameters.AjouterValeur("@dr", DbValue.DateNullableToDb(a.DateReprise));
                        cmd.Parameters.AjouterValeur("@m", a.Mortel ? 1 : 0);
                        cmd.Parameters.AjouterValeur("@dc", DbValue.DateNullableToDb(a.DateDeclarationCnss));
                        cmd.Parameters.AjouterValeur("@di", DbValue.DateNullableToDb(a.DateDeclarationInspection));
                        cmd.Parameters.AjouterValeur("@rc", (object)Nettoyer(a.ReferenceCnss) ?? DBNull.Value);
                        cmd.Parameters.AjouterValeur("@sj", DbValue.MoneyToDb(a.SalaireJournalier));
                        cmd.Parameters.AjouterValeur("@ipp", DbValue.DecimalToDb(a.TauxIpp));
                        cmd.Parameters.AjouterValeur("@abs", a.AbsenceId.HasValue ? (object)a.AbsenceId.Value : DBNull.Value);
                        cmd.Parameters.AjouterValeur("@com", (object)Nettoyer(a.Commentaire) ?? DBNull.Value);
                        cmd.Parameters.AjouterValeur("@mod", DbValue.DateToDb(DateTime.Now));
                        await cmd.ExecuteNonQueryAsync();
                    }

                    tx.Commit();
                }
                catch
                {
                    tx.Rollback();
                    a.Id = sauvegarde.Id;
                    a.Reference = sauvegarde.Reference;
                    a.AbsenceId = sauvegarde.AbsenceId;
                    throw;
                }
            }

            await Auditer(nouveau ? "Accident du travail enregistré" : "Accident du travail modifié", a.Reference,
                string.Format("Salarié {0}, {1} du {2}, {3}, {4}", a.EmployeeId, a.NatureLibelle, a.DateLibelle, a.ArretLibelle, a.DeclarationLibelle));
            return messages;
        }

        /// <summary>Supprime un accident et retire l'absence d'arrêt qu'il avait créée (période ouverte).</summary>
        public async Task SupprimerAccidentAsync(AccidentTravail a)
        {
            if (a == null || a.Id <= 0) return;
            if (a.DateDeclarationCnss.HasValue)
                throw new InvalidOperationException("Un accident déclaré à la CNSS fait partie du registre : il ne peut pas être supprimé.");

            using (var conn = _sqlite.CreerConnexion())
            using (var tx = conn.BeginTransaction())
            {
                var messages = new List<string>();
                await DisciplineService.AppliquerAbsenceAsync(conn, tx, a.AbsenceId, null, new[] { a.AbsenceId }, LibelleArret, messages);
                using (var cmd = conn.CreateCommand())
                {
                    cmd.Transaction = tx;
                    cmd.CommandText = "DELETE FROM AccidentsTravail WHERE Id = @id; UPDATE VisitesMedicales SET AccidentId = NULL WHERE AccidentId = @id;";
                    cmd.Parameters.AjouterValeur("@id", a.Id);
                    await cmd.ExecuteNonQueryAsync();
                }
                tx.Commit();
            }
            await Auditer("Suppression accident du travail", a.Reference, "Salarié " + a.EmployeeId + ", " + a.DateLibelle);
        }

        /// <summary>
        /// Report de l'arrêt dans les congés et absences (PAI-04).
        ///
        /// Défaut n° 13 du guide : une prolongation était refusée dès que l'arrêt déjà reporté
        /// touchait un mois de paie clôturé, même si seuls des jours de mois ouverts
        /// changeaient. Désormais, quand l'arrêt (avant ou après modification) touche un mois
        /// clôturé, la modification est acceptée si les jours de chaque mois clôturé restent
        /// identiques (<see cref="ControleSanteSecurite.RefusModificationArret"/>) ; elle est
        /// refusée, avec la raison, dans le cas contraire. Création, retrait et arrêt situé
        /// uniquement dans des mois ouverts suivent la règle commune
        /// (<see cref="DisciplineService.AppliquerAbsenceAsync"/>), qui refuse d'ajouter ou de
        /// retirer des jours dans un mois clôturé.
        /// </summary>
        private static async Task<int?> AppliquerArretAsync(
            SqliteConnection conn, SqliteTransaction tx, int? absenceId, Absence voulue, List<string> messages)
        {
            var exclure = new[] { absenceId };
            Absence existante = absenceId.HasValue ? await LireAbsenceAsync(conn, tx, absenceId.Value) : null;
            if (existante == null || voulue == null || existante.EstImputee)
                return await DisciplineService.AppliquerAbsenceAsync(conn, tx, absenceId, voulue, exclure, LibelleArret, messages);

            // Absence inchangée : rien à écrire (même règle que la discipline).
            if (existante.Type == voulue.Type
                && existante.DateDebut.Date == voulue.DateDebut.Date
                && existante.DateFin.Date == voulue.DateFin.Date
                && existante.NombreJours == voulue.NombreJours
                && string.Equals(existante.EmployeeId, voulue.EmployeeId, StringComparison.OrdinalIgnoreCase))
                return existante.Id;

            var clotures = new List<DateTime>();
            foreach (var mois in ControleSanteSecurite.MoisCouverts(existante).Union(ControleSanteSecurite.MoisCouverts(voulue)).OrderBy(m => m))
            {
                if (await PeriodePaieService.EstClotureeAsync(conn, mois, tx))
                    clotures.Add(mois);
            }
            if (clotures.Count == 0)
                return await DisciplineService.AppliquerAbsenceAsync(conn, tx, absenceId, voulue, exclure, LibelleArret, messages);

            var refus = ControleSanteSecurite.RefusModificationArret(existante, voulue, clotures);
            if (refus != null) throw new InvalidOperationException(refus);

            var anomalie = AbsenceService.Anomalie(voulue);
            if (anomalie != null) throw new InvalidOperationException("Transmission à la paie impossible (" + LibelleArret + ") : " + anomalie);
            await VerifierChevauchementAsync(conn, tx, voulue, existante.Id);

            using (var cmd = conn.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText =
                    "UPDATE Absences SET Type = @type, DateDebut = @debut, DateFin = @fin, NombreJours = @jours, " +
                    "NombreHeures = @heures, Commentaire = @commentaire WHERE Id = @id;";
                cmd.Parameters.AjouterValeur("@type", (int)voulue.Type);
                cmd.Parameters.AjouterValeur("@debut", DbValue.DateToDb(voulue.DateDebut));
                cmd.Parameters.AjouterValeur("@fin", DbValue.DateToDb(voulue.DateFin));
                cmd.Parameters.AjouterValeur("@jours", DbValue.DecimalToDb(voulue.NombreJours));
                cmd.Parameters.AjouterValeur("@heures", DbValue.DecimalToDb(0m));
                cmd.Parameters.AjouterValeur("@commentaire", voulue.Commentaire ?? "");
                cmd.Parameters.AjouterValeur("@id", existante.Id);
                await cmd.ExecuteNonQueryAsync();
            }

            messages.Add(string.Format(LibellesSST.Fr,
                "Absence « {0} » mise à jour ({1:0.##} j, du {2:dd/MM/yyyy} au {3:dd/MM/yyyy}) ; les jours de {4}, période clôturée, sont inchangés.",
                LibelleArret, voulue.NombreJours, voulue.DateDebut, voulue.DateFin,
                string.Join(", ", clotures.Select(m => m.ToString("MMMM yyyy", LibellesSST.Fr)))));
            return existante.Id;
        }

        private static async Task<Absence> LireAbsenceAsync(SqliteConnection conn, SqliteTransaction tx, int id)
        {
            using (var cmd = conn.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText =
                    "SELECT Id, EmployeeId, Type, DateDebut, DateFin, NombreJours, NombreHeures, PeriodeImputation FROM Absences WHERE Id = @id;";
                cmd.Parameters.AjouterValeur("@id", id);
                using (var r = (SqliteDataReader)await cmd.ExecuteReaderAsync())
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
                        NombreHeures = DbValue.DecimalFromDb(r["NombreHeures"]),
                        PeriodeImputation = DbValue.TextFromDb(r["PeriodeImputation"])
                    };
                }
            }
        }

        /// <summary>Même contrôle et même message que pour la discipline : une autre absence du salarié chevauche-t-elle l'arrêt ?</summary>
        private static async Task VerifierChevauchementAsync(SqliteConnection conn, SqliteTransaction tx, Absence a, int idExclu)
        {
            using (var cmd = conn.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText =
                    "SELECT Type, DateDebut, DateFin FROM Absences " +
                    "WHERE EmployeeId = @employe AND DateDebut <= @fin AND DateFin >= @debut AND Id <> @id LIMIT 1;";
                cmd.Parameters.AjouterValeur("@employe", a.EmployeeId);
                cmd.Parameters.AjouterValeur("@debut", DbValue.DateToDb(a.DateDebut));
                cmd.Parameters.AjouterValeur("@fin", DbValue.DateToDb(a.DateFin));
                cmd.Parameters.AjouterValeur("@id", idExclu);
                using (var r = (SqliteDataReader)await cmd.ExecuteReaderAsync())
                {
                    if (!await r.ReadAsync()) return;
                    throw new InvalidOperationException(string.Format(LibellesSST.Fr,
                        "Le salarié a déjà une absence « {0} » du {1:dd/MM/yyyy} au {2:dd/MM/yyyy} qui chevauche ces dates : " +
                        "corrigez-la dans « Congés et absences » ou modifiez les dates saisies ici.",
                        Absence.Libelle((TypeAbsence)DbValue.IntFromDb(r["Type"])),
                        DbValue.DateFromDb(r["DateDebut"], DateTime.Today), DbValue.DateFromDb(r["DateFin"], DateTime.Today)));
                }
            }
        }

        // ═══════════════════════════════════════════════════════════════════
        //  Remboursement des indemnités journalières par la CNSS
        // ═══════════════════════════════════════════════════════════════════

        /// <summary>
        /// Enregistre le suivi du remboursement d'un arrêt (dépôt du dossier, montant et date
        /// du remboursement). Seules ces trois colonnes sont écrites : la fiche de l'accident
        /// n'est pas touchée, et réciproquement l'enregistrement de la fiche ne les écrit pas.
        /// </summary>
        public async Task EnregistrerRemboursementIJAsync(SuiviRemboursementIJ s)
        {
            var anomalie = ControleSanteSecurite.AnomalieRemboursement(s, DateTime.Today);
            if (anomalie != null) throw new InvalidOperationException(anomalie);

            int lignes;
            try
            {
                using (var conn = _sqlite.CreerConnexion())
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText =
                        "UPDATE AccidentsTravail SET DateDepotIJ = @dd, MontantRembourseIJ = @mr, DateRemboursementIJ = @dr, " +
                        "DateModification = @mod WHERE Id = @id;";
                    cmd.Parameters.AjouterValeur("@dd", DbValue.DateNullableToDb(s.DateDepot));
                    cmd.Parameters.AjouterValeur("@mr", DbValue.MoneyToDb(s.MontantRembourse));
                    cmd.Parameters.AjouterValeur("@dr", DbValue.DateNullableToDb(s.DateRemboursement));
                    cmd.Parameters.AjouterValeur("@mod", DbValue.DateToDb(DateTime.Now));
                    cmd.Parameters.AjouterValeur("@id", s.AccidentId);
                    lignes = await cmd.ExecuteNonQueryAsync();
                }
            }
            catch (SqliteException ex) when (ex.Message.IndexOf("no such column", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                throw new InvalidOperationException(
                    "La base du dossier n'est pas encore mise à niveau (migration 38) : le suivi des remboursements CNSS ne peut pas être enregistré.");
            }
            if (lignes == 0) throw new InvalidOperationException("Cet accident n'existe plus dans le registre.");

            await Auditer("Remboursement des indemnités journalières (CNSS)", s.Reference, string.Format(LibellesSST.Fr,
                "{0} — réclamé {1:N0}, dossier déposé le {2}, remboursé {3:N0} le {4}, reste dû {5:N0}",
                s.StatutLibelle, s.MontantReclame, LibellesSST.Date(s.DateDepot), s.MontantRembourse, LibellesSST.Date(s.DateRemboursement), s.RestantDu));
        }

        /// <summary>Arrêts maladie et accident du travail enregistrés (pour les visites de reprise).</summary>
        public async Task<List<ArretSST>> ChargerArretsAsync()
        {
            var liste = new List<ArretSST>();
            using (var conn = _sqlite.CreerConnexion())
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "SELECT EmployeeId, Type, DateDebut, DateFin FROM Absences WHERE Type IN (@m, @at);";
                cmd.Parameters.AjouterValeur("@m", (int)TypeAbsence.Maladie);
                cmd.Parameters.AjouterValeur("@at", (int)TypeAbsence.AccidentTravail);
                using (var r = (SqliteDataReader)await cmd.ExecuteReaderAsync())
                {
                    while (await r.ReadAsync())
                    {
                        liste.Add(new ArretSST
                        {
                            EmployeeId = DbValue.TextFromDb(r["EmployeeId"]),
                            Type = (TypeAbsence)DbValue.IntFromDb(r["Type"]),
                            Debut = DbValue.DateFromDb(r["DateDebut"], DateTime.Today),
                            Fin = DbValue.DateFromDb(r["DateFin"], DateTime.Today)
                        });
                    }
                }
            }
            return liste;
        }

        public async Task<HashSet<DateTime>> ChargerFeriesAsync()
        {
            var feries = new HashSet<DateTime>();
            using (var conn = _sqlite.CreerConnexion())
            {
                if (!TableExiste(conn, "JoursFeries")) return feries;
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT DateJour FROM JoursFeries;";
                    using (var r = (SqliteDataReader)await cmd.ExecuteReaderAsync())
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
        //  Utilitaires
        // ═══════════════════════════════════════════════════════════════════

        private static async Task<string> ProchaineReferenceAsync(SqliteConnection conn, SqliteTransaction tx, int annee)
        {
            string prefixe = string.Format(CultureInfo.InvariantCulture, "AT-{0:D4}-", annee);
            int max = 0;
            using (var cmd = conn.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = "SELECT Reference FROM AccidentsTravail WHERE Reference LIKE @p;";
                cmd.Parameters.AjouterValeur("@p", prefixe + "%");
                using (var r = (SqliteDataReader)await cmd.ExecuteReaderAsync())
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

        private async Task Auditer(string action, string entiteId, string details)
        {
            try { await _audit.EnregistrerAsync(action, EntiteAudit, entiteId ?? "", details: details); }
            catch { /* le journal ne fait jamais échouer une saisie validée */ }
        }

        private static string Nom(SqliteDataReader r)
        {
            string nom = string.Format("{0} {1}", DbValue.TextFromDb(r["LastName"]), DbValue.TextFromDb(r["FirstName"])).Trim();
            return string.IsNullOrEmpty(nom) ? DbValue.TextFromDb(r["EmployeeId"]) : nom;
        }

        private static string Nettoyer(string t) { return string.IsNullOrWhiteSpace(t) ? null : t.Trim(); }

        /// <summary>Valeur d'une colonne qui peut manquer (base pas encore mise à niveau) : DBNull dans ce cas.</summary>
        private static object Colonne(SqliteDataReader r, string nom)
        {
            for (int i = 0; i < r.FieldCount; i++)
            {
                if (string.Equals(r.GetName(i), nom, StringComparison.OrdinalIgnoreCase))
                    return r.GetValue(i);
            }
            return DBNull.Value;
        }

        private static async Task<int> DernierIdAsync(SqliteConnection conn, SqliteTransaction tx = null)
        {
            using (var cmd = conn.CreateCommand())
            {
                if (tx != null) cmd.Transaction = tx;
                cmd.CommandText = "SELECT last_insert_rowid();";
                return DbValue.IntFromDb(await cmd.ExecuteScalarAsync());
            }
        }

        private static async Task Executer(SqliteConnection conn, string sql, int id)
        {
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = sql;
                cmd.Parameters.AjouterValeur("@id", id);
                await cmd.ExecuteNonQueryAsync();
            }
        }

        private static bool TableExiste(SqliteConnection conn, string nom)
        {
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = @n;";
                cmd.Parameters.AjouterValeur("@n", nom);
                return DbValue.IntFromDb(cmd.ExecuteScalar()) > 0;
            }
        }
    }
}
