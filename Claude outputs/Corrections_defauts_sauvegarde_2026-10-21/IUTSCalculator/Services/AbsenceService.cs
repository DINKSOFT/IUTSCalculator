using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Linq;
using System.Threading.Tasks;
using IUTSCalculator.Models;

namespace IUTSCalculator.Services
{
    /// <summary>
    /// Ce qu'une période d'absences apporte au bulletin d'un salarié : des jours de congé
    /// à décompter, et — pour les absences non rémunérées — une retenue sur le brut.
    ///
    /// La retenue porte sur le BRUT et non sur le net : une journée non travaillée ne
    /// réduit pas seulement ce que le salarié touche, elle réduit l'assiette de l'impôt
    /// et des cotisations. La traiter comme une retenue ordinaire ferait payer au salarié
    /// l'IUTS et la CNSS sur un salaire qu'il n'a pas perçu.
    /// </summary>
    public sealed class ApportAbsences
    {
        public static ApportAbsences Aucun()
        {
            return new ApportAbsences();
        }

        /// <summary>Jours de congé payé pris sur la période : alimente le bulletin.</summary>
        public decimal JoursCongesPris { get; set; }

        /// <summary>Jours d'absence non rémunérée retenus sur la période.</summary>
        public decimal JoursNonRemuneres { get; set; }

        /// <summary>Heures d'absence non rémunérée, pour les dossiers au taux horaire.</summary>
        public decimal HeuresNonRemunerees { get; set; }

        /// <summary>Montant retenu sur le brut au titre des absences non rémunérées.</summary>
        public decimal RetenueSurBrut { get; set; }

        /// <summary>Libellé de la retenue tel qu'il figure sur le bulletin.</summary>
        public string LibelleRetenue { get; set; }

        public bool EstVide
        {
            get { return JoursCongesPris == 0m && RetenueSurBrut == 0m; }
        }
    }

    /// <summary>
    /// Congés et absences (module PAI-04).
    ///
    /// Avant ce module, le bulletin portait un champ « congés pris » saisi à la main, que
    /// rien ne rattachait à des dates ni à un droit acquis : deux gestionnaires pouvaient
    /// écrire deux chiffres différents pour le même mois sans que rien ne le signale.
    /// Le compteur repose désormais sur des absences datées.
    ///
    /// <b>Droit à congé.</b> Le Code du travail burkinabè ouvre 2,5 jours ouvrables par
    /// mois de service effectif, soit 30 jours par an. Le droit mensuel est cependant
    /// paramétrable : une convention collective peut être plus favorable, et un dossier
    /// qui applique 3 jours doit pouvoir le dire plutôt que de corriger à la main.
    ///
    /// <b>Équivalence stricte.</b> <see cref="ApportAbsences.Aucun"/> ne porte que des
    /// zéros. Un dossier sans absence saisie produit exactement le bulletin qu'il
    /// produisait avant ce module.
    /// </summary>
    public class AbsenceService
    {
        private readonly SQLiteService _sqliteService;

        /// <summary>Droit mensuel légal : 2,5 jours ouvrables par mois de service effectif.</summary>
        public const decimal DroitMensuelLegal = 2.5m;

        /// <summary>Horaire mensuel de référence (39 h par semaine), diviseur du taux horaire.</summary>
        public const decimal HeuresMensuellesReference = 173.33m;

        private const string Colonnes =
            "a.Id, a.EmployeeId, a.Type, a.DateDebut, a.DateFin, a.NombreJours, " +
            "a.NombreHeures, a.Commentaire, a.DateSaisie, a.PeriodeImputation";

        /// <summary>
        /// Préfixe du commentaire des absences créées par la Gestion des temps (RH-08) :
        /// c'est lui qui permet de remplacer, et seulement elles, à chaque nouveau transfert.
        /// </summary>
        public const string MarqueurReleveTemps = "[RH-08]";

        public AbsenceService(SQLiteService sqliteService)
        {
            if (sqliteService == null) throw new ArgumentNullException("sqliteService");
            _sqliteService = sqliteService;
        }

        // ── Lecture ──────────────────────────────────────────────────────────

        /// <summary>
        /// Absences qui touchent une période de paie, c'est-à-dire qui la chevauchent ne
        /// serait-ce que d'un jour. Un congé du 28 mars au 5 avril concerne les deux mois :
        /// ne le rattacher qu'à sa date de début le ferait disparaître du bulletin d'avril.
        ///
        /// Une absence IMPUTÉE (relevé des temps RH-08, période de pointage décalée) ne
        /// suit pas cette règle : elle appartient au seul mois de paie qu'elle désigne.
        /// </summary>
        public async Task<List<Absence>> ChargerPeriodeAsync(int annee, int mois)
        {
            var debut = new DateTime(annee, mois, 1);
            var fin = debut.AddMonths(1).AddDays(-1);

            var liste = new List<Absence>();

            using (var conn = _sqliteService.CreateConnection())
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText =
                    "SELECT " + Colonnes + ", e.FirstName, e.LastName " +
                    "FROM Absences a " +
                    "LEFT JOIN Employees e ON e.EmployeeId = a.EmployeeId " +
                    "WHERE ((a.PeriodeImputation IS NULL OR a.PeriodeImputation = '') " +
                    "       AND a.DateDebut <= @fin AND a.DateFin >= @debut) " +
                    "   OR a.PeriodeImputation = @imputation " +
                    "ORDER BY e.LastName, e.FirstName, a.DateDebut";
                cmd.Parameters.AddWithValue("@debut", DbValue.DateToDb(debut));
                cmd.Parameters.AddWithValue("@fin", DbValue.DateToDb(fin));
                cmd.Parameters.AddWithValue("@imputation", Absence.CleImputation(annee, mois));

                using (var reader = (SQLiteDataReader)await cmd.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                        liste.Add(Lire(reader));
                }
            }

            return liste;
        }

        /// <summary>Toutes les absences d'un salarié, depuis son entrée. Base du compteur de congés.</summary>
        public async Task<List<Absence>> ChargerSalarieAsync(string employeeId)
        {
            var liste = new List<Absence>();
            if (string.IsNullOrWhiteSpace(employeeId)) return liste;

            using (var conn = _sqliteService.CreateConnection())
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText =
                    "SELECT " + Colonnes + ", e.FirstName, e.LastName " +
                    "FROM Absences a " +
                    "LEFT JOIN Employees e ON e.EmployeeId = a.EmployeeId " +
                    "WHERE a.EmployeeId = @employe " +
                    "ORDER BY a.DateDebut DESC";
                cmd.Parameters.AddWithValue("@employe", employeeId);

                using (var reader = (SQLiteDataReader)await cmd.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                        liste.Add(Lire(reader));
                }
            }

            return liste;
        }

        /// <summary>Toutes les absences du dossier, pour les compteurs d'ensemble.</summary>
        public async Task<List<Absence>> ChargerToutAsync()
        {
            var liste = new List<Absence>();

            using (var conn = _sqliteService.CreateConnection())
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText =
                    "SELECT " + Colonnes + ", e.FirstName, e.LastName " +
                    "FROM Absences a " +
                    "LEFT JOIN Employees e ON e.EmployeeId = a.EmployeeId " +
                    "ORDER BY a.DateDebut DESC";

                using (var reader = (SQLiteDataReader)await cmd.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                        liste.Add(Lire(reader));
                }
            }

            return liste;
        }

        private static Absence Lire(SQLiteDataReader reader)
        {
            string nom = DbValue.TextFromDb(reader["LastName"]);
            string prenom = DbValue.TextFromDb(reader["FirstName"]);
            string complet = string.Format("{0} {1}", nom, prenom).Trim();

            return new Absence
            {
                Id = DbValue.IntFromDb(reader["Id"]),
                EmployeeId = DbValue.TextFromDb(reader["EmployeeId"]),
                NomSalarie = string.IsNullOrWhiteSpace(complet) ? "Salarié supprimé" : complet,
                Type = (TypeAbsence)DbValue.IntFromDb(reader["Type"]),
                DateDebut = DbValue.DateFromDb(reader["DateDebut"], DateTime.Today),
                DateFin = DbValue.DateFromDb(reader["DateFin"], DateTime.Today),
                NombreJours = DbValue.DecimalFromDb(reader["NombreJours"]),
                NombreHeures = DbValue.DecimalFromDb(reader["NombreHeures"]),
                Commentaire = DbValue.TextFromDb(reader["Commentaire"]),
                DateSaisie = DbValue.DateFromDb(reader["DateSaisie"], DateTime.Now),
                PeriodeImputation = DbValue.TextFromDb(reader["PeriodeImputation"])
            };
        }

        // ── Compteur de congés ───────────────────────────────────────────────

        /// <summary>
        /// Situation d'un salarié au regard de ses congés à une date d'arrêté : jours
        /// acquis depuis l'entrée, jours pris, reliquat.
        ///
        /// Les jours acquis se comptent en mois de service RÉVOLUS : un salarié entré le
        /// 20 du mois n'acquiert son premier droit qu'un mois plus tard. Compter le mois
        /// d'entrée en entier offrirait un droit qui n'est pas dû.
        /// </summary>
        public static SoldeConges Solde(
            Employee salarie, IEnumerable<Absence> absences, DateTime arrete,
            decimal droitMensuel, int anneePeriode, int moisPeriode)
        {
            if (salarie == null) return new SoldeConges();

            if (droitMensuel <= 0m) droitMensuel = DroitMensuelLegal;

            var siennes = (absences ?? Enumerable.Empty<Absence>())
                .Where(a => a != null && string.Equals(a.EmployeeId, salarie.EmployeeId,
                                                       StringComparison.OrdinalIgnoreCase))
                .ToList();

            int moisService = MoisDeService(salarie.DateEntree, arrete);

            var solde = new SoldeConges
            {
                EmployeeId = salarie.EmployeeId,
                NomSalarie = string.Format("{0} {1}", salarie.LastName, salarie.FirstName).Trim(),
                MoisDeService = moisService,
                JoursAcquis = moisService * droitMensuel,
                JoursPris = siennes.Where(a => a.ImputeSurConges).Sum(a => a.JoursEquivalents)
            };

            if (anneePeriode > 0 && moisPeriode > 0)
            {
                var debut = new DateTime(anneePeriode, moisPeriode, 1);
                var fin = debut.AddMonths(1).AddDays(-1);

                solde.JoursPrisPeriode = siennes
                    .Where(a => a.ImputeSurConges)
                    .Sum(a => JoursDansPeriode(a, debut, fin));

                solde.JoursNonRemuneresPeriode = siennes
                    .Where(a => a.DonneLieuARetenue)
                    .Sum(a => JoursDansPeriode(a, debut, fin));
            }

            return solde;
        }

        /// <summary>
        /// Mois de service révolus entre l'entrée et la date d'arrêté. Zéro si l'entrée
        /// n'est pas renseignée : mieux vaut un compteur vide qu'un droit inventé à partir
        /// d'une date par défaut.
        /// </summary>
        public static int MoisDeService(DateTime dateEntree, DateTime arrete)
        {
            if (dateEntree == default(DateTime) || dateEntree > arrete)
                return 0;

            int mois = ((arrete.Year - dateEntree.Year) * 12) + arrete.Month - dateEntree.Month;

            // Le mois n'est révolu que passé le quantième d'entrée.
            if (arrete.Day < dateEntree.Day) mois--;

            return Math.Max(0, mois);
        }

        /// <summary>
        /// Fraction (de 0 à 1) d'une absence qui tombe dans une période, au prorata de ses
        /// jours ouvrables. Une absence posée sur un dimanche seul — le seul cas où le
        /// décompte « dimanches exclus » vaut zéro — se mesure en jours calendaires : elle
        /// comptait jusqu'ici pour 0 jour, même avec un nombre de jours saisi.
        /// </summary>
        private static decimal FractionDansPeriode(Absence absence, DateTime debut, DateTime fin, out decimal joursDesDates)
        {
            joursDesDates = 0m;
            var debutEffectif = absence.DateDebut.Date > debut.Date ? absence.DateDebut.Date : debut.Date;
            var finEffective = absence.DateFin.Date < fin.Date ? absence.DateFin.Date : fin.Date;

            if (finEffective < debutEffectif) return 0m;

            decimal joursTotaux = Absence.JoursEntre(absence.DateDebut, absence.DateFin, true);
            decimal joursPeriode;

            if (joursTotaux > 0m)
            {
                joursPeriode = Absence.JoursEntre(debutEffectif, finEffective, true);
            }
            else
            {
                joursTotaux = Absence.JoursEntre(absence.DateDebut, absence.DateFin, false);
                joursPeriode = Absence.JoursEntre(debutEffectif, finEffective, false);
                if (joursTotaux <= 0m) return 0m;
            }

            joursDesDates = joursTotaux;
            if (joursPeriode >= joursTotaux) return 1m;
            return joursPeriode / joursTotaux;
        }

        /// <summary>
        /// Part d'une absence qui tombe dans une période donnée, en jours. Une absence à
        /// cheval sur deux mois est répartie au prorata de ses jours, et non attribuée en
        /// entier au mois de son premier jour.
        ///
        /// Le nombre de jours saisi prime sur le comptage des dates : c'est lui que le
        /// gestionnaire a arrêté. Une absence saisie en HEURES seulement vaut
        /// <see cref="Absence.HeuresParJour"/> heures par jour (4 h = 0,5 j) — elle comptait
        /// jusqu'ici pour une journée entière par date.
        /// </summary>
        public static decimal JoursDansPeriode(Absence absence, DateTime debut, DateTime fin)
        {
            if (absence == null) return 0m;

            decimal joursDesDates;
            decimal fraction = FractionDansPeriode(absence, debut, fin, out joursDesDates);
            if (fraction <= 0m) return 0m;

            decimal saisis = absence.NombreJours > 0m ? absence.NombreJours
                           : absence.NombreHeures > 0m ? absence.NombreHeures / Absence.HeuresParJour
                           : joursDesDates;

            if (fraction >= 1m) return saisis;

            return Math.Round(saisis * fraction, 2);
        }

        /// <summary>
        /// Part d'une absence qui tombe dans une période donnée, en heures, pour les dossiers
        /// qui retiennent au taux horaire. Les heures saisies sont réparties au même prorata
        /// que les jours (une absence à cheval sur deux mois n'est plus comptée en entier dans
        /// chacun) ; une absence saisie en JOURS seulement vaut <see cref="Absence.HeuresParJour"/>
        /// heures par jour — elle ne donnait jusqu'ici lieu à aucune retenue au taux horaire.
        /// </summary>
        public static decimal HeuresDansPeriode(Absence absence, DateTime debut, DateTime fin)
        {
            if (absence == null) return 0m;

            decimal heures = absence.HeuresEquivalentes;
            if (heures <= 0m) return 0m;

            decimal joursDesDates;
            decimal fraction = FractionDansPeriode(absence, debut, fin, out joursDesDates);
            if (fraction <= 0m) return 0m;

            return fraction >= 1m ? heures : Math.Round(heures * fraction, 2);
        }

        /// <summary>
        /// Jours d'une absence qui reviennent à un mois de paie. Une absence imputée (RH-08)
        /// compte en entier dans son mois d'imputation et nulle part ailleurs ; les autres
        /// suivent <see cref="JoursDansPeriode"/>, sans aucun changement.
        /// </summary>
        public static decimal JoursPourMoisDePaie(Absence absence, int annee, int mois)
        {
            if (absence == null) return 0m;

            if (absence.EstImputee)
            {
                if (!string.Equals(absence.PeriodeImputation, Absence.CleImputation(annee, mois), StringComparison.Ordinal))
                    return 0m;
                if (absence.NombreJours > 0m) return absence.NombreJours;
                if (absence.NombreHeures > 0m) return absence.NombreHeures / Absence.HeuresParJour;
                return (decimal)((absence.DateFin.Date - absence.DateDebut.Date).Days + 1);
            }

            var debut = new DateTime(annee, mois, 1);
            return JoursDansPeriode(absence, debut, debut.AddMonths(1).AddDays(-1));
        }

        /// <summary>Heures d'une absence qui reviennent à un mois de paie (même règle que les jours).</summary>
        public static decimal HeuresPourMoisDePaie(Absence absence, int annee, int mois)
        {
            if (absence == null) return 0m;

            if (absence.EstImputee)
            {
                if (!string.Equals(absence.PeriodeImputation, Absence.CleImputation(annee, mois), StringComparison.Ordinal))
                    return 0m;
                return absence.HeuresEquivalentes;
            }

            var debut = new DateTime(annee, mois, 1);
            return HeuresDansPeriode(absence, debut, debut.AddMonths(1).AddDays(-1));
        }

        /// <summary>Situation de tout l'effectif, pour l'écran de suivi.</summary>
        public static List<SoldeConges> Soldes(
            IEnumerable<Employee> salaries, IEnumerable<Absence> absences,
            DateTime arrete, decimal droitMensuel, int anneePeriode, int moisPeriode)
        {
            var liste = (absences ?? Enumerable.Empty<Absence>()).ToList();

            return (salaries ?? Enumerable.Empty<Employee>())
                .Where(s => s != null)
                .Select(s => Solde(s, liste, arrete, droitMensuel, anneePeriode, moisPeriode))
                .OrderBy(s => s.NomSalarie)
                .ToList();
        }

        // ── Apport au bulletin ───────────────────────────────────────────────

        /// <summary>
        /// Ce que les absences d'un salarié apportent à son bulletin du mois : les jours
        /// de congé à afficher, et la retenue à opérer sur le brut.
        ///
        /// Le salaire de référence de la retenue est la rémunération permanente — salaire
        /// de base et indemnités — et non le seul salaire de base : une journée non
        /// travaillée ne donne pas droit à l'indemnité de transport de ce jour-là.
        /// </summary>
        public async Task<ApportAbsences> ApportAsync(
            Employee salarie, int annee, int mois, BaseRetenueAbsence base_)
        {
            if (salarie == null || string.IsNullOrWhiteSpace(salarie.EmployeeId))
                return ApportAbsences.Aucun();

            var absences = await ChargerPeriodeAsync(annee, mois);
            return Apport(salarie, absences, annee, mois, base_);
        }

        /// <summary>Version synchrone, pour un lot dont les absences sont déjà chargées.</summary>
        public static ApportAbsences Apport(
            Employee salarie, IEnumerable<Absence> absencesDeLaPeriode,
            int annee, int mois, BaseRetenueAbsence base_)
        {
            var apport = ApportAbsences.Aucun();

            if (salarie == null || string.IsNullOrWhiteSpace(salarie.EmployeeId))
                return apport;

            var debut = new DateTime(annee, mois, 1);
            string cle = Absence.CleImputation(annee, mois);

            // Une absence imputée à un autre mois de paie (RH-08) n'a rien à faire ici.
            var siennes = (absencesDeLaPeriode ?? Enumerable.Empty<Absence>())
                .Where(a => a != null && string.Equals(a.EmployeeId, salarie.EmployeeId,
                                                       StringComparison.OrdinalIgnoreCase))
                .Where(a => !a.EstImputee || string.Equals(a.PeriodeImputation, cle, StringComparison.Ordinal))
                .ToList();

            if (siennes.Count == 0) return apport;

            apport.JoursCongesPris = siennes
                .Where(a => a.ImputeSurConges)
                .Sum(a => JoursPourMoisDePaie(a, annee, mois));

            var retenues = siennes.Where(a => a.DonneLieuARetenue).ToList();
            if (retenues.Count == 0) return apport;

            apport.JoursNonRemuneres = retenues.Sum(a => JoursPourMoisDePaie(a, annee, mois));
            apport.HeuresNonRemunerees = retenues.Sum(a => HeuresPourMoisDePaie(a, annee, mois));

            decimal remunerationPermanente = salarie.BaseSalary
                                           + salarie.HousingAllowance
                                           + salarie.FunctionAllowance
                                           + salarie.TransportAllowance
                                           + salarie.SujetionAllowance;

            apport.RetenueSurBrut = Retenue(
                remunerationPermanente, apport.JoursNonRemuneres, apport.HeuresNonRemunerees,
                base_, debut);

            // La retenue ne peut pas dépasser ce qui est dû : un mois entier d'absence
            // ramène le brut à zéro, il ne le rend pas négatif.
            if (apport.RetenueSurBrut > remunerationPermanente)
                apport.RetenueSurBrut = remunerationPermanente;

            apport.LibelleRetenue = LibelleRetenue(apport, base_);
            return apport;
        }

        /// <summary>
        /// Montant retenu pour absence non rémunérée, selon la base choisie par le dossier.
        /// La loi burkinabè ne prescrit pas de diviseur ; les trois usages coexistent et
        /// c'est l'entreprise qui arrête le sien.
        /// </summary>
        public static decimal Retenue(
            decimal salaireMensuel, decimal jours, decimal heures,
            BaseRetenueAbsence base_, DateTime mois)
        {
            if (salaireMensuel <= 0m) return 0m;

            switch (base_)
            {
                case BaseRetenueAbsence.TauxHoraire:
                    if (heures <= 0m) return 0m;
                    return Math.Round(salaireMensuel / HeuresMensuellesReference * heures, 0);

                case BaseRetenueAbsence.JoursOuvrablesDuMois:
                {
                    if (jours <= 0m) return 0m;
                    decimal ouvrables = JoursOuvrablesDuMois(mois);
                    if (ouvrables <= 0m) return 0m;
                    return Math.Round(salaireMensuel / ouvrables * jours, 0);
                }

                case BaseRetenueAbsence.TrentiemeMensuel:
                default:
                    if (jours <= 0m) return 0m;
                    return Math.Round(salaireMensuel / 30m * jours, 0);
            }
        }

        /// <summary>Jours ouvrables d'un mois : du lundi au samedi, dimanche exclu.</summary>
        public static decimal JoursOuvrablesDuMois(DateTime mois)
        {
            var debut = new DateTime(mois.Year, mois.Month, 1);
            var fin = debut.AddMonths(1).AddDays(-1);
            return Absence.JoursEntre(debut, fin, true);
        }

        private static string LibelleRetenue(ApportAbsences apport, BaseRetenueAbsence base_)
        {
            if (base_ == BaseRetenueAbsence.TauxHoraire && apport.HeuresNonRemunerees > 0m)
                return string.Format("Absence non rémunérée ({0:0.##} h)", apport.HeuresNonRemunerees);

            return string.Format("Absence non rémunérée ({0:0.##} j)", apport.JoursNonRemuneres);
        }

        // ── Écriture ─────────────────────────────────────────────────────────

        /// <summary>
        /// Enregistre une absence. Une absence qui touche une période clôturée est
        /// refusée : elle changerait un bulletin déjà arrêté et des cotisations déjà
        /// déclarées.
        /// </summary>
        public async Task EnregistrerAsync(Absence absence)
        {
            if (absence == null) throw new ArgumentNullException("absence");

            var anomalie = Anomalie(absence);
            if (anomalie != null) throw new InvalidOperationException(anomalie);

            using (var conn = _sqliteService.CreateConnection())
            {
                await VerifierPeriodesOuvertesAsync(conn, absence);
                await VerifierChevauchementAsync(conn, absence);

                using (var cmd = conn.CreateCommand())
                {
                    if (absence.Id > 0)
                    {
                        cmd.CommandText =
                            "UPDATE Absences SET EmployeeId = @employe, Type = @type, " +
                            "DateDebut = @debut, DateFin = @fin, NombreJours = @jours, " +
                            "NombreHeures = @heures, Commentaire = @commentaire " +
                            "WHERE Id = @id;";
                        cmd.Parameters.AddWithValue("@id", absence.Id);
                    }
                    else
                    {
                        // La période d'imputation (RH-08) ne s'écrit qu'à la création : une
                        // modification depuis l'écran des congés la conserve telle quelle.
                        cmd.CommandText =
                            "INSERT INTO Absences " +
                            "(EmployeeId, Type, DateDebut, DateFin, NombreJours, NombreHeures, " +
                            " Commentaire, DateSaisie, PeriodeImputation) " +
                            "VALUES (@employe, @type, @debut, @fin, @jours, @heures, " +
                            "        @commentaire, @saisie, @imputation);";
                        cmd.Parameters.AddWithValue("@saisie", DbValue.DateToDb(DateTime.Now));
                        cmd.Parameters.AddWithValue("@imputation", (object)absence.PeriodeImputation ?? DBNull.Value);
                    }

                    cmd.Parameters.AddWithValue("@employe", absence.EmployeeId ?? "");
                    cmd.Parameters.AddWithValue("@type", (int)absence.Type);
                    cmd.Parameters.AddWithValue("@debut", DbValue.DateToDb(absence.DateDebut));
                    cmd.Parameters.AddWithValue("@fin", DbValue.DateToDb(absence.DateFin));
                    cmd.Parameters.AddWithValue("@jours", DbValue.DecimalToDb(absence.NombreJours));
                    cmd.Parameters.AddWithValue("@heures", DbValue.DecimalToDb(absence.NombreHeures));
                    cmd.Parameters.AddWithValue("@commentaire", absence.Commentaire ?? "");

                    await cmd.ExecuteNonQueryAsync();
                }
            }
        }

        /// <summary>
        /// Enregistre une collection d'absences en masse dans une seule transaction SQLite.
        /// Idéal pour l'attribution groupée ou l'import Excel d'effectifs volumineux (2 000 à 3 000 salariés).
        /// </summary>
        public async Task<int> EnregistrerEnMasseAsync(IEnumerable<Absence> absences)
        {
            if (absences == null) return 0;
            var liste = absences.Where(a => a != null).ToList();
            if (liste.Count == 0) return 0;

            // Valider chaque absence
            foreach (var a in liste)
            {
                var anomalie = Anomalie(a);
                if (anomalie != null)
                {
                    throw new InvalidOperationException(string.Format("Salarié {0} : {1}", a.EmployeeId, anomalie));
                }
            }

            using (var conn = _sqliteService.CreateConnection())
            {
                // Vérifier les périodes concernées une seule fois par mois distinct
                var moisDistincts = new HashSet<DateTime>();
                foreach (var a in liste)
                {
                    if (a.EstImputee && a.MoisImputation.HasValue)
                    {
                        moisDistincts.Add(a.MoisImputation.Value);
                        continue;
                    }

                    var m = new DateTime(a.DateDebut.Year, a.DateDebut.Month, 1);
                    var der = new DateTime(a.DateFin.Year, a.DateFin.Month, 1);
                    while (m <= der)
                    {
                        moisDistincts.Add(m);
                        m = m.AddMonths(1);
                    }
                }

                foreach (var mois in moisDistincts)
                {
                    if (await PeriodePaieService.EstClotureeAsync(conn, mois, null))
                    {
                        throw new InvalidOperationException(string.Format(
                            "La période de {0:MM/yyyy} est clôturée : les absences ne peuvent pas y être enregistrées.", mois));
                    }
                }

                // Chevauchement entre absences du même lot, pour un même salarié.
                foreach (var groupe in liste.GroupBy(a => a.EmployeeId, StringComparer.OrdinalIgnoreCase))
                {
                    var pourEmploye = groupe.ToList();
                    for (int i = 0; i < pourEmploye.Count; i++)
                    {
                        for (int j = i + 1; j < pourEmploye.Count; j++)
                        {
                            if (SeChevauchent(pourEmploye[i], pourEmploye[j]))
                            {
                                throw new InvalidOperationException(string.Format(
                                    "Salarié {0} : deux absences du lot se chevauchent ({1:dd/MM/yyyy}-{2:dd/MM/yyyy} " +
                                    "et {3:dd/MM/yyyy}-{4:dd/MM/yyyy}).",
                                    groupe.Key, pourEmploye[i].DateDebut, pourEmploye[i].DateFin,
                                    pourEmploye[j].DateDebut, pourEmploye[j].DateFin));
                            }
                        }
                    }
                }

                // Chevauchement avec des absences déjà enregistrées en base.
                foreach (var a in liste)
                {
                    await VerifierChevauchementAsync(conn, a);
                }

                int count = 0;
                using (var tx = conn.BeginTransaction())
                {
                    using (var cmd = conn.CreateCommand())
                    {
                        cmd.Transaction = tx;
                        cmd.CommandText =
                            "INSERT INTO Absences " +
                            "(EmployeeId, Type, DateDebut, DateFin, NombreJours, NombreHeures, " +
                            " Commentaire, DateSaisie, PeriodeImputation) " +
                            "VALUES (@employe, @type, @debut, @fin, @jours, @heures, " +
                            "        @commentaire, @saisie, @imputation);";

                        var pImputation = cmd.Parameters.Add("@imputation", System.Data.DbType.String);
                        var pEmploye = cmd.Parameters.Add("@employe", System.Data.DbType.String);
                        var pType = cmd.Parameters.Add("@type", System.Data.DbType.Int32);
                        var pDebut = cmd.Parameters.Add("@debut", System.Data.DbType.String);
                        var pFin = cmd.Parameters.Add("@fin", System.Data.DbType.String);
                        var pJours = cmd.Parameters.Add("@jours", System.Data.DbType.Decimal);
                        var pHeures = cmd.Parameters.Add("@heures", System.Data.DbType.Decimal);
                        var pCommentaire = cmd.Parameters.Add("@commentaire", System.Data.DbType.String);
                        var pSaisie = cmd.Parameters.Add("@saisie", System.Data.DbType.String);

                        string maintenantDb = DbValue.DateToDb(DateTime.Now);

                        foreach (var a in liste)
                        {
                            pEmploye.Value = a.EmployeeId ?? "";
                            pType.Value = (int)a.Type;
                            pDebut.Value = DbValue.DateToDb(a.DateDebut);
                            pFin.Value = DbValue.DateToDb(a.DateFin);
                            pJours.Value = a.NombreJours;
                            pHeures.Value = a.NombreHeures;
                            pCommentaire.Value = a.Commentaire ?? "";
                            pSaisie.Value = maintenantDb;
                            pImputation.Value = (object)a.PeriodeImputation ?? DBNull.Value;

                            await cmd.ExecuteNonQueryAsync();
                            count++;
                        }
                    }
                    tx.Commit();
                }

                return count;
            }
        }

        public async Task SupprimerAsync(Absence absence)
        {
            if (absence == null || absence.Id <= 0) return;

            using (var conn = _sqliteService.CreateConnection())
            {
                await VerifierPeriodesOuvertesAsync(conn, absence);

                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "DELETE FROM Absences WHERE Id = @id;";
                    cmd.Parameters.AddWithValue("@id", absence.Id);
                    await cmd.ExecuteNonQueryAsync();
                }
            }
        }

        /// <summary>
        /// Une absence peut couvrir plusieurs mois : toutes les périodes qu'elle touche
        /// doivent être ouvertes, pas seulement celle de sa date de début.
        /// </summary>
        private static async Task VerifierPeriodesOuvertesAsync(SQLiteConnection conn, Absence absence)
        {
            // Une absence imputée (RH-08) ne touche que son mois de paie.
            if (absence.EstImputee && absence.MoisImputation.HasValue)
            {
                if (await PeriodePaieService.EstClotureeAsync(conn, absence.MoisImputation.Value, null))
                {
                    throw new InvalidOperationException(string.Format(
                        "La période de {0:MM/yyyy} est clôturée : cette absence, imputée à ce mois de paie, " +
                        "ne peut plus être modifiée.", absence.MoisImputation.Value));
                }
                return;
            }

            var mois = new DateTime(absence.DateDebut.Year, absence.DateDebut.Month, 1);
            var dernier = new DateTime(absence.DateFin.Year, absence.DateFin.Month, 1);

            while (mois <= dernier)
            {
                if (await PeriodePaieService.EstClotureeAsync(conn, mois, null))
                {
                    throw new InvalidOperationException(string.Format(
                        "La période de {0:MM/yyyy} est clôturée : cette absence ne peut plus y être " +
                        "enregistrée. Rouvrez-la depuis l'écran « Périodes de paie » si la " +
                        "correction est justifiée.", mois));
                }

                mois = mois.AddMonths(1);
            }
        }

        /// <summary>
        /// Refuse une absence qui chevauche, ne serait-ce que d'un jour, une autre absence
        /// déjà enregistrée pour le même salarié. Sans ce garde-fou, un même jour pouvait être
        /// compté deux fois — deux congés qui se recouvrent, ou un congé payé et une absence
        /// non rémunérée sur la même date — faussant le compteur de congés et la retenue sur
        /// le brut du bulletin concerné.
        /// </summary>
        private static async Task VerifierChevauchementAsync(SQLiteConnection conn, Absence absence)
        {
            if (string.IsNullOrWhiteSpace(absence.EmployeeId)) return;

            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText =
                    "SELECT DateDebut, DateFin FROM Absences " +
                    "WHERE EmployeeId = @employe AND Id <> @id " +
                    "AND DateDebut <= @fin AND DateFin >= @debut;";
                cmd.Parameters.AddWithValue("@employe", absence.EmployeeId);
                cmd.Parameters.AddWithValue("@id", absence.Id);
                cmd.Parameters.AddWithValue("@debut", DbValue.DateToDb(absence.DateDebut));
                cmd.Parameters.AddWithValue("@fin", DbValue.DateToDb(absence.DateFin));

                using (var reader = (SQLiteDataReader)await cmd.ExecuteReaderAsync())
                {
                    if (await reader.ReadAsync())
                    {
                        var autreDebut = DbValue.DateFromDb(reader["DateDebut"], DateTime.Today);
                        var autreFin = DbValue.DateFromDb(reader["DateFin"], DateTime.Today);
                        throw new InvalidOperationException(string.Format(
                            "Ce salarié a déjà une absence enregistrée du {0:dd/MM/yyyy} au {1:dd/MM/yyyy}, " +
                            "qui chevauche la période saisie.", autreDebut, autreFin));
                    }
                }
            }
        }

        /// <summary>Vrai si deux absences se recouvrent sur au moins un jour.</summary>
        private static bool SeChevauchent(Absence a, Absence b)
        {
            return a.DateDebut.Date <= b.DateFin.Date && a.DateFin.Date >= b.DateDebut.Date;
        }

        /// <summary>
        /// Remplace les absences relevées par la Gestion des temps (RH-08) pour un mois de
        /// paie et une liste de salariés : les absences marquées <see cref="MarqueurReleveTemps"/>
        /// et imputées à ce mois sont supprimées, puis les nouvelles insérées, dans une seule
        /// transaction. Les absences saisies dans l'écran des congés ne sont jamais touchées.
        ///
        /// Un salarié dont une absence relevée chevauche une absence déjà saisie est écarté
        /// en entier (rien n'est remplacé pour lui) ; la raison est renvoyée.
        /// </summary>
        public async Task<List<string>> RemplacerAbsencesReleveesAsync(
            int annee, int mois, IEnumerable<string> salaries, IEnumerable<Absence> nouvelles)
        {
            var ecartes = new List<string>();
            var cibles = new HashSet<string>(
                (salaries ?? Enumerable.Empty<string>()).Where(x => !string.IsNullOrWhiteSpace(x)),
                StringComparer.OrdinalIgnoreCase);
            if (cibles.Count == 0) return ecartes;

            string cle = Absence.CleImputation(annee, mois);
            var aInserer = (nouvelles ?? Enumerable.Empty<Absence>())
                .Where(a => a != null && cibles.Contains(a.EmployeeId ?? ""))
                .ToList();

            foreach (var a in aInserer)
            {
                a.PeriodeImputation = cle;
                if (string.IsNullOrEmpty(a.Commentaire) || !a.Commentaire.StartsWith(MarqueurReleveTemps, StringComparison.Ordinal))
                    a.Commentaire = MarqueurReleveTemps + " " + (a.Commentaire ?? "");
            }

            using (var conn = _sqliteService.CreateConnection())
            {
                if (await PeriodePaieService.EstClotureeAsync(conn, new DateTime(annee, mois, 1), null))
                {
                    throw new InvalidOperationException(string.Format(
                        "La période de {0:D2}/{1} est clôturée : les absences relevées ne peuvent plus y être imputées.",
                        mois, annee));
                }

                using (var tx = conn.BeginTransaction())
                {
                    try
                    {
                        var parSalarie = aInserer.GroupBy(a => a.EmployeeId, StringComparer.OrdinalIgnoreCase)
                                                 .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

                        foreach (var employe in cibles)
                        {
                            List<Absence> siennes;
                            parSalarie.TryGetValue(employe, out siennes);
                            siennes = siennes ?? new List<Absence>();

                            // Contrôle AVANT tout remplacement : un salarié écarté garde son relevé
                            // précédent. Les absences relevées de ce mois, qui vont être remplacées,
                            // ne comptent pas comme chevauchement.
                            string raison = null;
                            foreach (var a in siennes)
                            {
                                raison = Anomalie(a);
                                if (raison != null) break;

                                using (var cmd = conn.CreateCommand())
                                {
                                    cmd.Transaction = tx;
                                    cmd.CommandText =
                                        "SELECT DateDebut, DateFin FROM Absences WHERE EmployeeId = @employe " +
                                        "AND DateDebut <= @fin AND DateFin >= @debut " +
                                        "AND NOT (PeriodeImputation = @cle AND Commentaire LIKE @marqueur) LIMIT 1;";
                                    cmd.Parameters.AddWithValue("@employe", employe);
                                    cmd.Parameters.AddWithValue("@debut", DbValue.DateToDb(a.DateDebut));
                                    cmd.Parameters.AddWithValue("@fin", DbValue.DateToDb(a.DateFin));
                                    cmd.Parameters.AddWithValue("@cle", cle);
                                    cmd.Parameters.AddWithValue("@marqueur", MarqueurReleveTemps + "%");
                                    using (var reader = (SQLiteDataReader)await cmd.ExecuteReaderAsync())
                                    {
                                        if (await reader.ReadAsync())
                                        {
                                            raison = string.Format(
                                                "absence déjà saisie du {0:dd/MM/yyyy} au {1:dd/MM/yyyy}, qui chevauche le relevé",
                                                DbValue.DateFromDb(reader["DateDebut"], DateTime.Today),
                                                DbValue.DateFromDb(reader["DateFin"], DateTime.Today));
                                        }
                                    }
                                }

                                if (raison != null) break;
                            }

                            if (raison != null)
                            {
                                ecartes.Add(employe + " : " + raison);
                                continue;
                            }

                            using (var cmd = conn.CreateCommand())
                            {
                                cmd.Transaction = tx;
                                cmd.CommandText =
                                    "DELETE FROM Absences WHERE EmployeeId = @employe AND PeriodeImputation = @cle " +
                                    "AND Commentaire LIKE @marqueur;";
                                cmd.Parameters.AddWithValue("@employe", employe);
                                cmd.Parameters.AddWithValue("@cle", cle);
                                cmd.Parameters.AddWithValue("@marqueur", MarqueurReleveTemps + "%");
                                await cmd.ExecuteNonQueryAsync();
                            }

                            foreach (var a in siennes)
                            {
                                using (var cmd = conn.CreateCommand())
                                {
                                    cmd.Transaction = tx;
                                    cmd.CommandText =
                                        "INSERT INTO Absences " +
                                        "(EmployeeId, Type, DateDebut, DateFin, NombreJours, NombreHeures, " +
                                        " Commentaire, DateSaisie, PeriodeImputation) " +
                                        "VALUES (@employe, @type, @debut, @fin, @jours, @heures, " +
                                        "        @commentaire, @saisie, @imputation);";
                                    cmd.Parameters.AddWithValue("@employe", a.EmployeeId);
                                    cmd.Parameters.AddWithValue("@type", (int)a.Type);
                                    cmd.Parameters.AddWithValue("@debut", DbValue.DateToDb(a.DateDebut));
                                    cmd.Parameters.AddWithValue("@fin", DbValue.DateToDb(a.DateFin));
                                    cmd.Parameters.AddWithValue("@jours", DbValue.DecimalToDb(a.NombreJours));
                                    cmd.Parameters.AddWithValue("@heures", DbValue.DecimalToDb(a.NombreHeures));
                                    cmd.Parameters.AddWithValue("@commentaire", a.Commentaire ?? "");
                                    cmd.Parameters.AddWithValue("@saisie", DbValue.DateToDb(DateTime.Now));
                                    cmd.Parameters.AddWithValue("@imputation", cle);
                                    await cmd.ExecuteNonQueryAsync();
                                }
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

            return ecartes;
        }

        /// <summary>Vrai si la période accepte encore des saisies.</summary>
        public async Task<bool> PeriodeOuverteAsync(int annee, int mois)
        {
            using (var conn = _sqliteService.CreateConnection())
            {
                return !await PeriodePaieService.EstClotureeAsync(conn, new DateTime(annee, mois, 1), null);
            }
        }

        /// <summary>Ce qui empêche d'enregistrer, en une phrase. Null quand tout est correct.</summary>
        public static string Anomalie(Absence absence)
        {
            if (absence == null) return "Absence vide.";

            if (string.IsNullOrWhiteSpace(absence.EmployeeId))
                return "Choisissez le salarié concerné.";

            if (absence.DateFin.Date < absence.DateDebut.Date)
                return "La date de fin précède la date de début.";

            if (absence.NombreJours <= 0m && absence.NombreHeures <= 0m)
                return "Indiquez un nombre de jours ou d'heures.";

            if (absence.NombreJours < 0m || absence.NombreHeures < 0m)
                return "Un nombre de jours ou d'heures ne peut pas être négatif.";

            // Une absence relevée au planning d'une rotation (RH-08) peut tomber un dimanche
            // travaillé : le décompte « dimanches exclus » ne la concerne pas.
            if (absence.EstImputee) return null;

            decimal joursDesDates = Absence.JoursEntre(absence.DateDebut, absence.DateFin, true);
            if (absence.NombreJours > joursDesDates && joursDesDates > 0m)
            {
                return string.Format(
                    "{0:0.##} jours décomptés alors que la période n'en compte que {1:0.##} " +
                    "(dimanches exclus).", absence.NombreJours, joursDesDates);
            }

            // Absence posée sur un dimanche seul : elle vaut au plus ce dimanche.
            decimal joursCalendaires = Absence.JoursEntre(absence.DateDebut, absence.DateFin, false);

            // Une absence en heures seulement ne peut pas dépasser 24 h par jour de la période.
            if (absence.NombreJours == 0m && absence.NombreHeures > 24m * joursCalendaires)
            {
                return string.Format(
                    "{0:0.##} heures pour une période de {1:0.##} jour(s) calendaire(s) : vérifiez la saisie.",
                    absence.NombreHeures, joursCalendaires);
            }
            if (joursDesDates == 0m && absence.NombreJours > joursCalendaires)
            {
                return string.Format(
                    "{0:0.##} jours décomptés pour une absence d'un seul dimanche.", absence.NombreJours);
            }

            return null;
        }

        /// <summary>
        /// Ce qui mérite d'être signalé sans empêcher d'enregistrer : un congé pris
        /// au-delà du droit acquis, par exemple, qui peut être une avance consentie
        /// comme une erreur de saisie.
        /// </summary>
        public static List<string> Avertissements(IEnumerable<SoldeConges> soldes)
        {
            var messages = new List<string>();

            foreach (var solde in (soldes ?? Enumerable.Empty<SoldeConges>()).Where(s => s != null))
            {
                if (solde.SoldeNegatif)
                {
                    messages.Add(string.Format(
                        "{0} a pris {1:0.##} jours de congé pour {2:0.##} acquis : {3:0.##} jours d'avance.",
                        solde.NomSalarie, solde.JoursPris, solde.JoursAcquis, Math.Abs(solde.Solde)));
                }
            }

            return messages;
        }
    }
}
