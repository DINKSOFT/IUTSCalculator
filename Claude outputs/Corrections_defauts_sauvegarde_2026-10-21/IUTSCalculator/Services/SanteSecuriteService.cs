using System;
using System.Collections.Generic;
using System.Data.SQLite;
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
            using (var conn = _sqlite.CreateConnection())
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "SELECT * FROM ParametresSST WHERE Id = 1;";
                using (var r = (SQLiteDataReader)await cmd.ExecuteReaderAsync())
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

            using (var conn = _sqlite.CreateConnection())
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText =
                    "INSERT INTO ParametresSST (Id, PeriodiciteVisiteMois, RepriseApresArretJours, DelaiDeclarationJoursOuvrables, " +
                    "TauxIndemniteJournaliere, JoursMoisReference, AlerteAvanceJours) VALUES (1, @p, @r, @d, @t, @j, @a) " +
                    "ON CONFLICT(Id) DO UPDATE SET PeriodiciteVisiteMois = @p, RepriseApresArretJours = @r, " +
                    "DelaiDeclarationJoursOuvrables = @d, TauxIndemniteJournaliere = @t, JoursMoisReference = @j, AlerteAvanceJours = @a;";
                cmd.Parameters.AddWithValue("@p", p.PeriodiciteVisiteMois);
                cmd.Parameters.AddWithValue("@r", p.RepriseApresArretJours);
                cmd.Parameters.AddWithValue("@d", p.DelaiDeclarationJoursOuvrables);
                cmd.Parameters.AddWithValue("@t", DbValue.DecimalToDb(p.TauxIndemniteJournaliere));
                cmd.Parameters.AddWithValue("@j", p.JoursMoisReference);
                cmd.Parameters.AddWithValue("@a", p.AlerteAvanceJours);
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
            using (var conn = _sqlite.CreateConnection())
            {
                if (!TableExiste(conn, "Postes")) return liste;
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText =
                        "SELECT p.Id, p.Code, p.Intitule, pv.Mois, " +
                        "(SELECT COUNT(*) FROM Employees e WHERE e.PosteId = p.Id AND e.StatutSalarie = 0) AS Titulaires " +
                        "FROM Postes p LEFT JOIN PeriodicitesVisitePoste pv ON pv.PosteId = p.Id " +
                        "WHERE p.Actif = 1 ORDER BY p.Code;";
                    using (var r = (SQLiteDataReader)await cmd.ExecuteReaderAsync())
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

            using (var conn = _sqlite.CreateConnection())
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
                        cmd.Parameters.AddWithValue("@p", p.PosteId);
                        cmd.Parameters.AddWithValue("@m", p.Mois.Value);
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
            using (var conn = _sqlite.CreateConnection())
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText =
                    "SELECT v.*, e.FirstName, e.LastName FROM VisitesMedicales v LEFT JOIN Employees e ON e.EmployeeId = v.EmployeeId " +
                    "ORDER BY COALESCE(NULLIF(v.DateVisite, ''), v.DatePrevue) DESC, v.Id DESC;";
                using (var r = (SQLiteDataReader)await cmd.ExecuteReaderAsync())
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

            using (var conn = _sqlite.CreateConnection())
            using (var cmd = conn.CreateCommand())
            {
                if (v.Id > 0)
                {
                    cmd.CommandText =
                        "UPDATE VisitesMedicales SET EmployeeId = @e, Type = @t, DatePrevue = @dp, DateVisite = @dv, Medecin = @m, " +
                        "Aptitude = @a, Restrictions = @r, ProchaineVisite = @pv, AccidentId = @acc, Commentaire = @c WHERE Id = @id;";
                    cmd.Parameters.AddWithValue("@id", v.Id);
                }
                else
                {
                    cmd.CommandText =
                        "INSERT INTO VisitesMedicales (EmployeeId, Type, DatePrevue, DateVisite, Medecin, Aptitude, Restrictions, " +
                        "ProchaineVisite, AccidentId, Commentaire, DateSaisie) VALUES (@e, @t, @dp, @dv, @m, @a, @r, @pv, @acc, @c, @s);";
                    cmd.Parameters.AddWithValue("@s", DbValue.DateToDb(DateTime.Now));
                }
                cmd.Parameters.AddWithValue("@e", v.EmployeeId.Trim());
                cmd.Parameters.AddWithValue("@t", (int)v.Type);
                cmd.Parameters.AddWithValue("@dp", DbValue.DateNullableToDb(v.DatePrevue));
                cmd.Parameters.AddWithValue("@dv", DbValue.DateNullableToDb(v.DateVisite));
                cmd.Parameters.AddWithValue("@m", (object)Nettoyer(v.Medecin) ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@a", (int)v.Aptitude);
                cmd.Parameters.AddWithValue("@r", (object)Nettoyer(v.Restrictions) ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@pv", DbValue.DateNullableToDb(v.ProchaineVisite));
                cmd.Parameters.AddWithValue("@acc", v.AccidentId.HasValue ? (object)v.AccidentId.Value : DBNull.Value);
                cmd.Parameters.AddWithValue("@c", (object)Nettoyer(v.Commentaire) ?? DBNull.Value);
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
            using (var conn = _sqlite.CreateConnection())
                await Executer(conn, "DELETE FROM VisitesMedicales WHERE Id = @id;", v.Id);
            await Auditer("Suppression visite médicale", v.EmployeeId, v.TypeLibelle + " du " + v.DateLibelle);
        }

        // ═══════════════════════════════════════════════════════════════════
        //  EPI
        // ═══════════════════════════════════════════════════════════════════

        public async Task<List<TypeEPI>> ChargerTypesEpiAsync()
        {
            var liste = new List<TypeEPI>();
            using (var conn = _sqlite.CreateConnection())
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "SELECT * FROM TypesEPI ORDER BY Libelle;";
                using (var r = (SQLiteDataReader)await cmd.ExecuteReaderAsync())
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

            using (var conn = _sqlite.CreateConnection())
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = t.Id > 0
                    ? "UPDATE TypesEPI SET Libelle = @l, DureeVieMois = @d, Norme = @n, Actif = @a WHERE Id = @id;"
                    : "INSERT INTO TypesEPI (Libelle, DureeVieMois, Norme, Actif) VALUES (@l, @d, @n, @a);";
                if (t.Id > 0) cmd.Parameters.AddWithValue("@id", t.Id);
                cmd.Parameters.AddWithValue("@l", t.Libelle.Trim());
                cmd.Parameters.AddWithValue("@d", t.DureeVieMois);
                cmd.Parameters.AddWithValue("@n", (object)Nettoyer(t.Norme) ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@a", t.Actif ? 1 : 0);
                await cmd.ExecuteNonQueryAsync();
                if (t.Id <= 0) t.Id = await DernierIdAsync(conn);
            }
            await Auditer("Catalogue des EPI", t.Libelle, t.DureeLibelle);
        }

        public async Task SupprimerTypeEpiAsync(TypeEPI t)
        {
            if (t == null || t.Id <= 0) return;
            using (var conn = _sqlite.CreateConnection())
            {
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT COUNT(*) FROM DotationsEPI WHERE TypeEpiId = @id;";
                    cmd.Parameters.AddWithValue("@id", t.Id);
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
            using (var conn = _sqlite.CreateConnection())
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText =
                    "SELECT d.*, t.Libelle AS TypeLibelle, e.FirstName, e.LastName FROM DotationsEPI d " +
                    "LEFT JOIN TypesEPI t ON t.Id = d.TypeEpiId LEFT JOIN Employees e ON e.EmployeeId = d.EmployeeId " +
                    "ORDER BY d.DateRemise DESC, d.Id DESC;";
                using (var r = (SQLiteDataReader)await cmd.ExecuteReaderAsync())
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

            using (var conn = _sqlite.CreateConnection())
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = d.Id > 0
                    ? "UPDATE DotationsEPI SET EmployeeId = @e, TypeEpiId = @t, DateRemise = @dr, Quantite = @q, Taille = @ta, " +
                      "DateRenouvellement = @dn, Etat = @et, DateRetour = @dret, Commentaire = @c WHERE Id = @id;"
                    : "INSERT INTO DotationsEPI (EmployeeId, TypeEpiId, DateRemise, Quantite, Taille, DateRenouvellement, Etat, DateRetour, Commentaire) " +
                      "VALUES (@e, @t, @dr, @q, @ta, @dn, @et, @dret, @c);";
                if (d.Id > 0) cmd.Parameters.AddWithValue("@id", d.Id);
                cmd.Parameters.AddWithValue("@e", d.EmployeeId.Trim());
                cmd.Parameters.AddWithValue("@t", d.TypeEpiId);
                cmd.Parameters.AddWithValue("@dr", DbValue.DateToDb(d.DateRemise.Date));
                cmd.Parameters.AddWithValue("@q", d.Quantite);
                cmd.Parameters.AddWithValue("@ta", (object)Nettoyer(d.Taille) ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@dn", DbValue.DateNullableToDb(d.DateRenouvellement));
                cmd.Parameters.AddWithValue("@et", (int)d.Etat);
                cmd.Parameters.AddWithValue("@dret", DbValue.DateNullableToDb(d.DateRetour));
                cmd.Parameters.AddWithValue("@c", (object)Nettoyer(d.Commentaire) ?? DBNull.Value);
                await cmd.ExecuteNonQueryAsync();
                if (d.Id <= 0) d.Id = await DernierIdAsync(conn);
            }
            await Auditer("Remise d'EPI", d.EmployeeId, string.Format("{0} × {1}, le {2}, {3}", d.Quantite, d.TypeEpiLibelle, LibellesSST.Date(d.DateRemise), d.EtatLibelle));
        }

        public async Task SupprimerDotationAsync(DotationEPI d)
        {
            if (d == null || d.Id <= 0) return;
            using (var conn = _sqlite.CreateConnection())
                await Executer(conn, "DELETE FROM DotationsEPI WHERE Id = @id;", d.Id);
            await Auditer("Suppression remise d'EPI", d.EmployeeId, d.TypeEpiLibelle + " du " + LibellesSST.Date(d.DateRemise));
        }

        // ═══════════════════════════════════════════════════════════════════
        //  Accidents du travail
        // ═══════════════════════════════════════════════════════════════════

        public async Task<List<AccidentTravail>> ChargerAccidentsAsync()
        {
            var liste = new List<AccidentTravail>();
            using (var conn = _sqlite.CreateConnection())
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText =
                    "SELECT a.*, e.FirstName, e.LastName FROM AccidentsTravail a LEFT JOIN Employees e ON e.EmployeeId = a.EmployeeId " +
                    "ORDER BY a.DateAccident DESC, a.Id DESC;";
                using (var r = (SQLiteDataReader)await cmd.ExecuteReaderAsync())
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
                            Commentaire = DbValue.TextFromDb(r["Commentaire"])
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

            using (var conn = _sqlite.CreateConnection())
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
                            cmd.Parameters.AddWithValue("@r", a.Reference);
                            cmd.Parameters.AddWithValue("@e", a.EmployeeId);
                            cmd.Parameters.AddWithValue("@d", DbValue.DateToDb(a.DateAccident));
                            cmd.Parameters.AddWithValue("@n", DbValue.DateToDb(DateTime.Now));
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
                    a.AbsenceId = await DisciplineService.AppliquerAbsenceAsync(conn, tx, a.AbsenceId, voulue,
                        new[] { a.AbsenceId }, "période d'arrêt de travail", messages);

                    using (var cmd = conn.CreateCommand())
                    {
                        cmd.Transaction = tx;
                        cmd.CommandText =
                            "UPDATE AccidentsTravail SET EmployeeId = @e, Nature = @na, DateAccident = @d, Heure = @h, Lieu = @l, " +
                            "Circonstances = @c, SiegeLesion = @sl, NatureLesion = @nl, Temoins = @t, PremiersSoins = @ps, AvecArret = @aa, " +
                            "DebutArret = @da, FinArret = @fa, DateReprise = @dr, Mortel = @m, DateDeclarationCnss = @dc, " +
                            "DateDeclarationInspection = @di, ReferenceCnss = @rc, SalaireJournalier = @sj, TauxIpp = @ipp, " +
                            "AbsenceId = @abs, Commentaire = @com, DateModification = @mod WHERE Id = @id;";
                        cmd.Parameters.AddWithValue("@id", a.Id);
                        cmd.Parameters.AddWithValue("@e", a.EmployeeId);
                        cmd.Parameters.AddWithValue("@na", (int)a.Nature);
                        cmd.Parameters.AddWithValue("@d", DbValue.DateToDb(a.DateAccident));
                        cmd.Parameters.AddWithValue("@h", (object)Nettoyer(a.Heure) ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@l", (object)Nettoyer(a.Lieu) ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@c", (object)Nettoyer(a.Circonstances) ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@sl", (object)Nettoyer(a.SiegeLesion) ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@nl", (object)Nettoyer(a.NatureLesion) ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@t", (object)Nettoyer(a.Temoins) ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@ps", (object)Nettoyer(a.PremiersSoins) ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@aa", a.AvecArret ? 1 : 0);
                        cmd.Parameters.AddWithValue("@da", DbValue.DateNullableToDb(a.DebutArret));
                        cmd.Parameters.AddWithValue("@fa", DbValue.DateNullableToDb(a.FinArret));
                        cmd.Parameters.AddWithValue("@dr", DbValue.DateNullableToDb(a.DateReprise));
                        cmd.Parameters.AddWithValue("@m", a.Mortel ? 1 : 0);
                        cmd.Parameters.AddWithValue("@dc", DbValue.DateNullableToDb(a.DateDeclarationCnss));
                        cmd.Parameters.AddWithValue("@di", DbValue.DateNullableToDb(a.DateDeclarationInspection));
                        cmd.Parameters.AddWithValue("@rc", (object)Nettoyer(a.ReferenceCnss) ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@sj", DbValue.MoneyToDb(a.SalaireJournalier));
                        cmd.Parameters.AddWithValue("@ipp", DbValue.DecimalToDb(a.TauxIpp));
                        cmd.Parameters.AddWithValue("@abs", a.AbsenceId.HasValue ? (object)a.AbsenceId.Value : DBNull.Value);
                        cmd.Parameters.AddWithValue("@com", (object)Nettoyer(a.Commentaire) ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@mod", DbValue.DateToDb(DateTime.Now));
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

            using (var conn = _sqlite.CreateConnection())
            using (var tx = conn.BeginTransaction())
            {
                var messages = new List<string>();
                await DisciplineService.AppliquerAbsenceAsync(conn, tx, a.AbsenceId, null, new[] { a.AbsenceId }, "période d'arrêt de travail", messages);
                using (var cmd = conn.CreateCommand())
                {
                    cmd.Transaction = tx;
                    cmd.CommandText = "DELETE FROM AccidentsTravail WHERE Id = @id; UPDATE VisitesMedicales SET AccidentId = NULL WHERE AccidentId = @id;";
                    cmd.Parameters.AddWithValue("@id", a.Id);
                    await cmd.ExecuteNonQueryAsync();
                }
                tx.Commit();
            }
            await Auditer("Suppression accident du travail", a.Reference, "Salarié " + a.EmployeeId + ", " + a.DateLibelle);
        }

        /// <summary>Arrêts maladie et accident du travail enregistrés (pour les visites de reprise).</summary>
        public async Task<List<ArretSST>> ChargerArretsAsync()
        {
            var liste = new List<ArretSST>();
            using (var conn = _sqlite.CreateConnection())
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "SELECT EmployeeId, Type, DateDebut, DateFin FROM Absences WHERE Type IN (@m, @at);";
                cmd.Parameters.AddWithValue("@m", (int)TypeAbsence.Maladie);
                cmd.Parameters.AddWithValue("@at", (int)TypeAbsence.AccidentTravail);
                using (var r = (SQLiteDataReader)await cmd.ExecuteReaderAsync())
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
            using (var conn = _sqlite.CreateConnection())
            {
                if (!TableExiste(conn, "JoursFeries")) return feries;
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
        //  Utilitaires
        // ═══════════════════════════════════════════════════════════════════

        private static async Task<string> ProchaineReferenceAsync(SQLiteConnection conn, SQLiteTransaction tx, int annee)
        {
            string prefixe = string.Format(CultureInfo.InvariantCulture, "AT-{0:D4}-", annee);
            int max = 0;
            using (var cmd = conn.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = "SELECT Reference FROM AccidentsTravail WHERE Reference LIKE @p;";
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

        private async Task Auditer(string action, string entiteId, string details)
        {
            try { await _audit.EnregistrerAsync(action, EntiteAudit, entiteId ?? "", details: details); }
            catch { /* le journal ne fait jamais échouer une saisie validée */ }
        }

        private static string Nom(SQLiteDataReader r)
        {
            string nom = string.Format("{0} {1}", DbValue.TextFromDb(r["LastName"]), DbValue.TextFromDb(r["FirstName"])).Trim();
            return string.IsNullOrEmpty(nom) ? DbValue.TextFromDb(r["EmployeeId"]) : nom;
        }

        private static string Nettoyer(string t) { return string.IsNullOrWhiteSpace(t) ? null : t.Trim(); }

        private static async Task<int> DernierIdAsync(SQLiteConnection conn, SQLiteTransaction tx = null)
        {
            using (var cmd = conn.CreateCommand())
            {
                if (tx != null) cmd.Transaction = tx;
                cmd.CommandText = "SELECT last_insert_rowid();";
                return DbValue.IntFromDb(await cmd.ExecuteScalarAsync());
            }
        }

        private static async Task Executer(SQLiteConnection conn, string sql, int id)
        {
            using (var cmd = conn.CreateCommand())
            {
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
