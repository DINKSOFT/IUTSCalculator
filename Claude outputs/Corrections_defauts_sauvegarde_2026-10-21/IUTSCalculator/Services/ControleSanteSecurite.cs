using System;
using System.Collections.Generic;
using System.Linq;
using IUTSCalculator.Models;

namespace IUTSCalculator.Services
{
    /// <summary>Une absence réduite à ce qu'il faut pour repérer un arrêt long (visite de reprise).</summary>
    public class ArretSST
    {
        public string EmployeeId { get; set; }
        public TypeAbsence Type { get; set; }
        public DateTime Debut { get; set; }
        public DateTime Fin { get; set; }
        public int JoursCalendaires { get { return (Fin.Date - Debut.Date).Days + 1; } }
    }

    /// <summary>
    /// Contrôles et calculs de santé et sécurité au travail (RH-07), sans base de données :
    /// échéances des visites médicales, délai de déclaration d'un accident (48 heures
    /// ouvrables), indemnités journalières de la CNSS (2/3 à partir du lendemain de
    /// l'accident), renouvellement des EPI.
    /// </summary>
    public static class ControleSanteSecurite
    {
        // ── Rémunération journalière et indemnités ───────────────────────

        /// <summary>Rémunération journalière proposée : salaire de base + indemnités, divisé par 30.</summary>
        public static decimal SalaireJournalierPropose(Employee salarie, ParametresSST prm)
        {
            if (salarie == null) return 0m;
            prm = prm ?? ParametresSST.Defaut();
            decimal mensuel = salarie.BaseSalary + salarie.HousingAllowance + salarie.TransportAllowance
                              + salarie.FunctionAllowance + salarie.SujetionAllowance;
            int jours = prm.JoursMoisReference > 0 ? prm.JoursMoisReference : 30;
            return Math.Round(mensuel / jours, 0);
        }

        /// <summary>
        /// Jours indemnisés par la CNSS entre deux dates : chaque jour d'arrêt, ouvrable ou
        /// non, à partir du LENDEMAIN de l'accident (le jour de l'accident reste à
        /// l'employeur), jusqu'à la fin d'arrêt — ou la veille de la reprise, ou la date
        /// d'arrêté tant que la fin n'est pas connue.
        /// </summary>
        public static int JoursIndemnises(AccidentTravail a, DateTime du, DateTime au, DateTime aujourdHui)
        {
            if (a == null || !a.AvecArret || a.Mortel || !a.DebutArret.HasValue) return 0;

            var debut = a.DebutArret.Value.Date;
            var lendemain = a.DateAccident.Date.AddDays(1);
            if (debut < lendemain) debut = lendemain;

            DateTime fin;
            if (a.FinArret.HasValue) fin = a.FinArret.Value.Date;
            else if (a.DateReprise.HasValue) fin = a.DateReprise.Value.Date.AddDays(-1);
            else fin = aujourdHui.Date;

            if (debut < du.Date) debut = du.Date;
            if (fin > au.Date) fin = au.Date;
            return fin < debut ? 0 : (fin - debut).Days + 1;
        }

        /// <summary>Indemnités journalières dues par la CNSS pour un nombre de jours.</summary>
        public static decimal MontantCnss(int jours, decimal salaireJournalier, ParametresSST prm)
        {
            if (jours <= 0 || salaireJournalier <= 0m) return 0m;
            return Math.Round(jours * salaireJournalier * (prm ?? ParametresSST.Defaut()).TauxIndemniteJournaliere, 0);
        }

        /// <summary>État des indemnités journalières d'un mois, accident par accident.</summary>
        public static List<LigneIndemniteJournaliere> EtatIndemnites(
            IEnumerable<AccidentTravail> accidents, int annee, int mois, ParametresSST prm, DateTime aujourdHui)
        {
            prm = prm ?? ParametresSST.Defaut();
            var du = new DateTime(annee, mois, 1);
            var au = du.AddMonths(1).AddDays(-1);
            var lignes = new List<LigneIndemniteJournaliere>();

            foreach (var a in accidents ?? Enumerable.Empty<AccidentTravail>())
            {
                int jours = JoursIndemnises(a, du, au, aujourdHui);
                if (jours <= 0) continue;

                var debut = a.DebutArret.Value.Date < a.DateAccident.Date.AddDays(1) ? a.DateAccident.Date.AddDays(1) : a.DebutArret.Value.Date;
                if (debut < du) debut = du;
                var montant = MontantCnss(jours, a.SalaireJournalier, prm);
                lignes.Add(new LigneIndemniteJournaliere
                {
                    AccidentId = a.Id,
                    Reference = a.Reference,
                    EmployeeId = a.EmployeeId,
                    NomSalarie = a.NomSalarie,
                    DateAccident = a.DateAccident,
                    Du = debut,
                    Au = debut.AddDays(jours - 1),
                    Jours = jours,
                    SalaireJournalier = a.SalaireJournalier,
                    MontantCnss = montant,
                    PartEmployeur = Math.Round(jours * a.SalaireJournalier, 0) - montant
                });
            }

            return lignes.OrderBy(l => l.NomSalarie).ThenBy(l => l.DateAccident).ToList();
        }

        // ── Déclaration de l'accident ────────────────────────────────────

        /// <summary>Dernier jour pour déclarer : 48 heures ouvrables = 2 jours ouvrables après l'accident.</summary>
        public static DateTime DateLimiteDeclaration(AccidentTravail a, ParametresSST prm, ISet<DateTime> feries)
        {
            int n = Math.Max(1, (prm ?? ParametresSST.Defaut()).DelaiDeclarationJoursOuvrables);
            return ControleDiscipline.AjouterJoursOuvrables(a.DateAccident, n, feries);
        }

        // ── Visites médicales ────────────────────────────────────────────

        /// <summary>Périodicité applicable à un salarié : celle de son poste si elle existe, sinon celle du dossier.</summary>
        public static int Periodicite(Employee salarie, IEnumerable<PeriodicitePoste> periodicites, ParametresSST prm)
        {
            prm = prm ?? ParametresSST.Defaut();
            if (salarie != null && salarie.PosteId.HasValue && periodicites != null)
            {
                var p = periodicites.FirstOrDefault(x => x.PosteId == salarie.PosteId.Value);
                if (p != null && p.Mois.HasValue && p.Mois.Value > 0) return p.Mois.Value;
            }
            return prm.PeriodiciteVisiteMois > 0 ? prm.PeriodiciteVisiteMois : 12;
        }

        /// <summary>Dernière visite passée d'un salarié (tous types), ou null.</summary>
        public static VisiteMedicale DerniereVisite(string employeeId, IEnumerable<VisiteMedicale> visites)
        {
            return (visites ?? Enumerable.Empty<VisiteMedicale>())
                .Where(v => v != null && v.DateVisite.HasValue && string.Equals(v.EmployeeId, employeeId, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(v => v.DateVisite.Value)
                .FirstOrDefault();
        }

        /// <summary>
        /// Prochaine visite due d'un salarié : la date fixée par le médecin, sinon la dernière
        /// visite + la périodicité ; sans aucune visite, la visite d'embauche, due à l'entrée.
        /// </summary>
        public static DateTime ProchaineVisite(Employee salarie, IEnumerable<VisiteMedicale> visites,
            IEnumerable<PeriodicitePoste> periodicites, ParametresSST prm, out TypeVisiteMedicale type)
        {
            var derniere = DerniereVisite(salarie.EmployeeId, visites);
            if (derniere == null)
            {
                type = TypeVisiteMedicale.Embauche;
                return salarie.DateEntree > new DateTime(1900, 1, 1) ? salarie.DateEntree.Date : DateTime.Today;
            }
            type = TypeVisiteMedicale.Periodique;
            if (derniere.ProchaineVisite.HasValue && derniere.ProchaineVisite.Value > derniere.DateVisite.Value)
                return derniere.ProchaineVisite.Value.Date;
            return derniere.DateVisite.Value.Date.AddMonths(Periodicite(salarie, periodicites, prm));
        }

        /// <summary>
        /// Arrêts qui appellent une visite de reprise : arrêt pour accident du travail ou
        /// maladie de plus de N jours (paramètre), terminé, sans visite de reprise depuis.
        /// </summary>
        public static List<ArretSST> ReprisesAttendues(IEnumerable<ArretSST> arrets, IEnumerable<VisiteMedicale> visites, ParametresSST prm)
        {
            prm = prm ?? ParametresSST.Defaut();
            var v = (visites ?? Enumerable.Empty<VisiteMedicale>()).Where(x => x.DateVisite.HasValue).ToList();
            return (arrets ?? Enumerable.Empty<ArretSST>())
                .Where(a => a != null && (a.Type == TypeAbsence.AccidentTravail || a.Type == TypeAbsence.Maladie)
                            && a.JoursCalendaires > prm.RepriseApresArretJours)
                .Where(a => !v.Any(x => string.Equals(x.EmployeeId, a.EmployeeId, StringComparison.OrdinalIgnoreCase)
                                        && x.DateVisite.Value.Date > a.Fin.Date.AddDays(-1)))
                .ToList();
        }

        // ── Échéancier ───────────────────────────────────────────────────

        public static List<EcheanceSST> Echeances(
            IEnumerable<Employee> salariesActifs,
            IEnumerable<VisiteMedicale> visites,
            IEnumerable<PeriodicitePoste> periodicites,
            IEnumerable<ArretSST> arrets,
            IEnumerable<DotationEPI> dotations,
            IEnumerable<AccidentTravail> accidents,
            ParametresSST prm, ISet<DateTime> feries, DateTime aujourdHui)
        {
            prm = prm ?? ParametresSST.Defaut();
            var jour = aujourdHui.Date;
            var horizon = jour.AddDays(Math.Max(0, prm.AlerteAvanceJours));
            var liste = new List<EcheanceSST>();
            var toutesVisites = (visites ?? Enumerable.Empty<VisiteMedicale>()).ToList();

            Func<string, string, string, string, DateTime, int?, int?, EcheanceSST> ajouter = (cat, id, nom, lib, d, acc, dot) =>
            {
                var e = new EcheanceSST
                {
                    Categorie = cat, EmployeeId = id, NomSalarie = nom, Libelle = lib, Date = d.Date,
                    JoursRestants = (d.Date - jour).Days, AccidentId = acc, DotationId = dot
                };
                liste.Add(e);
                return e;
            };

            foreach (var s in salariesActifs ?? Enumerable.Empty<Employee>())
            {
                if (s == null || string.IsNullOrWhiteSpace(s.EmployeeId)) continue;

                // Une visite programmée (convocation) remplace l'échéance calculée.
                var programmee = toutesVisites
                    .Where(x => !x.DateVisite.HasValue && x.DatePrevue.HasValue && string.Equals(x.EmployeeId, s.EmployeeId, StringComparison.OrdinalIgnoreCase))
                    .OrderBy(x => x.DatePrevue.Value).FirstOrDefault();
                if (programmee != null)
                {
                    if (programmee.DatePrevue.Value <= horizon)
                    {
                        var e = ajouter("Visite médicale", s.EmployeeId, s.FullName, programmee.TypeLibelle + " programmée", programmee.DatePrevue.Value, programmee.AccidentId, null);
                        e.VisiteId = programmee.Id;
                        e.TypeVisite = programmee.Type;
                    }
                    continue;
                }

                TypeVisiteMedicale type;
                var due = ProchaineVisite(s, toutesVisites, periodicites, prm, out type);
                if (due <= horizon)
                    ajouter("Visite médicale", s.EmployeeId, s.FullName,
                        type == TypeVisiteMedicale.Embauche ? "Visite d'embauche à passer" : "Visite périodique à passer", due, null, null).TypeVisite = type;
            }

            // Seuls les arrêts terminés depuis moins de 90 jours sont rappelés : au-delà, la
            // reprise a eu lieu depuis longtemps et l'alerte ne servirait plus à rien.
            var arretsRecents = (arrets ?? Enumerable.Empty<ArretSST>()).Where(x => x != null && x.Fin.Date >= jour.AddDays(-90));
            foreach (var a in ReprisesAttendues(arretsRecents, toutesVisites, prm))
            {
                var s = (salariesActifs ?? Enumerable.Empty<Employee>()).FirstOrDefault(x => string.Equals(x.EmployeeId, a.EmployeeId, StringComparison.OrdinalIgnoreCase));
                if (s == null) continue;
                // Une visite de reprise déjà programmée figure dans l'échéancier à sa date.
                if (toutesVisites.Any(x => !x.DateVisite.HasValue && x.DatePrevue.HasValue && x.Type == TypeVisiteMedicale.Reprise
                                           && string.Equals(x.EmployeeId, a.EmployeeId, StringComparison.OrdinalIgnoreCase)
                                           && x.DatePrevue.Value.Date >= a.Fin.Date)) continue;
                ajouter("Visite médicale", a.EmployeeId, s.FullName,
                    string.Format(LibellesSST.Fr, "Visite de reprise (arrêt de {0} j terminé le {1:dd/MM/yyyy})", a.JoursCalendaires, a.Fin), a.Fin.AddDays(1), null, null)
                    .TypeVisite = TypeVisiteMedicale.Reprise;
            }

            foreach (var d in dotations ?? Enumerable.Empty<DotationEPI>())
            {
                if (d == null || d.Etat != EtatDotation.EnService || !d.DateRenouvellement.HasValue) continue;
                if (d.DateRenouvellement.Value <= horizon)
                    ajouter("EPI", d.EmployeeId, d.NomSalarie, "Renouveler : " + d.TypeEpiLibelle, d.DateRenouvellement.Value, null, d.Id);
            }

            foreach (var a in accidents ?? Enumerable.Empty<AccidentTravail>())
            {
                if (a == null) continue;
                if (!a.DateDeclarationCnss.HasValue || !a.DateDeclarationInspection.HasValue)
                {
                    var manque = !a.DateDeclarationCnss.HasValue && !a.DateDeclarationInspection.HasValue ? "CNSS et inspection du travail"
                               : !a.DateDeclarationCnss.HasValue ? "CNSS" : "inspection du travail";
                    ajouter("Accident", a.EmployeeId, a.NomSalarie, "Déclarer l'accident " + a.Reference + " (" + manque + ")",
                        DateLimiteDeclaration(a, prm, feries), a.Id, null);
                }
                if (a.AvecArret && !a.Mortel && a.FinArret.HasValue && !a.DateReprise.HasValue && a.FinArret.Value < jour)
                    ajouter("Accident", a.EmployeeId, a.NomSalarie, "Saisir la reprise (arrêt terminé) — " + a.Reference, a.FinArret.Value.AddDays(1), a.Id, null);
            }

            return liste.OrderBy(e => e.Date).ThenBy(e => e.NomSalarie).ToList();
        }

        /// <summary>
        /// Situation de chaque salarié actif : dernière visite, avis, prochaine échéance
        /// (visite programmée si elle existe, sinon échéance calculée).
        /// </summary>
        public static List<SituationMedicale> SituationsMedicales(
            IEnumerable<Employee> salariesActifs, IEnumerable<VisiteMedicale> visites,
            IEnumerable<PeriodicitePoste> periodicites, ParametresSST prm, DateTime aujourdHui)
        {
            prm = prm ?? ParametresSST.Defaut();
            var jour = aujourdHui.Date;
            var toutes = (visites ?? Enumerable.Empty<VisiteMedicale>()).ToList();
            var postes = (periodicites ?? Enumerable.Empty<PeriodicitePoste>()).ToList();
            var liste = new List<SituationMedicale>();

            foreach (var s in salariesActifs ?? Enumerable.Empty<Employee>())
            {
                if (s == null || string.IsNullOrWhiteSpace(s.EmployeeId)) continue;
                var derniere = DerniereVisite(s.EmployeeId, toutes);
                TypeVisiteMedicale type;
                var due = ProchaineVisite(s, toutes, postes, prm, out type);
                var programmee = toutes
                    .Where(x => !x.DateVisite.HasValue && x.DatePrevue.HasValue && string.Equals(x.EmployeeId, s.EmployeeId, StringComparison.OrdinalIgnoreCase))
                    .OrderBy(x => x.DatePrevue.Value).FirstOrDefault();
                if (programmee != null)
                {
                    due = programmee.DatePrevue.Value.Date;
                    type = programmee.Type;
                }
                var poste = s.PosteId.HasValue ? postes.FirstOrDefault(p => p.PosteId == s.PosteId.Value) : null;

                liste.Add(new SituationMedicale
                {
                    EmployeeId = s.EmployeeId,
                    NomSalarie = s.FullName,
                    Poste = poste != null ? poste.Designation : s.Fonction,
                    PeriodiciteMois = Periodicite(s, postes, prm),
                    DerniereVisite = derniere != null ? derniere.DateVisite : null,
                    DerniereVisiteType = derniere != null ? derniere.TypeLibelle.Replace("Visite ", "") : null,
                    Aptitude = derniere != null ? derniere.Aptitude : AptitudeMedicale.NonRenseignee,
                    Restrictions = derniere != null ? derniere.Restrictions : null,
                    ProchaineEcheance = due,
                    TypeProchaine = type,
                    EstProgrammee = programmee != null,
                    JoursRestants = (due - jour).Days
                });
            }

            return liste.OrderBy(x => x.ProchaineEcheance).ThenBy(x => x.NomSalarie).ToList();
        }

        // ── Contrôles de saisie ──────────────────────────────────────────

        public static string AnomalieVisite(VisiteMedicale v, DateTime aujourdHui)
        {
            if (v == null) return "Visite vide.";
            if (string.IsNullOrWhiteSpace(v.EmployeeId)) return "Choisissez le salarié.";
            if (!v.DatePrevue.HasValue && !v.DateVisite.HasValue) return "Indiquez la date prévue (convocation) ou la date de la visite.";
            if (v.DateVisite.HasValue && v.DateVisite.Value.Date > aujourdHui.Date) return "La visite n'a pas encore eu lieu : renseignez la date prévue, pas la date de visite.";
            if (!v.DateVisite.HasValue && v.Aptitude != AptitudeMedicale.NonRenseignee) return "L'avis d'aptitude suppose une visite passée : renseignez sa date.";
            if (v.DateVisite.HasValue && v.ProchaineVisite.HasValue && v.ProchaineVisite.Value <= v.DateVisite.Value) return "La prochaine visite doit suivre la visite.";
            return null;
        }

        public static string AnomalieDotation(DotationEPI d)
        {
            if (d == null) return "Remise vide.";
            if (string.IsNullOrWhiteSpace(d.EmployeeId)) return "Choisissez le salarié.";
            if (d.TypeEpiId <= 0) return "Choisissez l'équipement.";
            if (d.Quantite <= 0) return "La quantité doit être positive.";
            if (d.DateRenouvellement.HasValue && d.DateRenouvellement.Value < d.DateRemise) return "Le renouvellement précède la remise.";
            if (d.DateRetour.HasValue && d.DateRetour.Value < d.DateRemise) return "Le retour précède la remise.";
            if (d.Etat != EtatDotation.EnService && !d.DateRetour.HasValue) return "Indiquez la date de retour (ou de constat de perte).";
            return null;
        }

        /// <summary>Alertes d'un accident, bloquantes d'abord.</summary>
        public static List<AlerteSST> AnalyserAccident(AccidentTravail a, ParametresSST prm, ISet<DateTime> feries,
            IEnumerable<VisiteMedicale> visites, DateTime aujourdHui)
        {
            var l = new List<AlerteSST>();
            if (a == null) return l;
            prm = prm ?? ParametresSST.Defaut();
            var jour = aujourdHui.Date;
            var fr = LibellesSST.Fr;
            Action<string> bloquer = m => l.Add(new AlerteSST(NiveauAlerte.Bloquant, m));
            Action<string> signaler = m => l.Add(new AlerteSST(NiveauAlerte.Avertissement, m));
            Action<string> informer = m => l.Add(new AlerteSST(NiveauAlerte.Info, m));

            if (string.IsNullOrWhiteSpace(a.EmployeeId)) bloquer("Choisissez la victime.");
            if (a.DateAccident > jour) bloquer("La date de l'accident est dans le futur.");
            if (a.AvecArret && !a.Mortel && !a.DebutArret.HasValue) bloquer("Indiquez le premier jour d'arrêt (en principe le lendemain de l'accident).");
            if (a.DebutArret.HasValue && a.DebutArret.Value < a.DateAccident) bloquer("L'arrêt commence avant l'accident.");
            if (a.FinArret.HasValue && a.DebutArret.HasValue && a.FinArret.Value < a.DebutArret.Value) bloquer("La fin d'arrêt précède son début.");
            if (a.DateReprise.HasValue && a.DebutArret.HasValue && a.DateReprise.Value <= a.DebutArret.Value) bloquer("La reprise doit suivre le début de l'arrêt.");
            if (a.DateDeclarationCnss.HasValue && a.DateDeclarationCnss.Value < a.DateAccident) bloquer("La déclaration à la CNSS précède l'accident.");
            if (a.DateDeclarationInspection.HasValue && a.DateDeclarationInspection.Value < a.DateAccident) bloquer("La déclaration à l'inspection précède l'accident.");
            if (a.SalaireJournalier < 0m) bloquer("La rémunération journalière ne peut pas être négative.");
            if (a.TauxIpp < 0m || a.TauxIpp > 100m) bloquer("Le taux d'incapacité permanente est compris entre 0 et 100 %.");

            var limite = DateLimiteDeclaration(a, prm, feries);
            Action<DateTime?, string> verifierDeclaration = (date, organisme) =>
            {
                if (date.HasValue)
                {
                    if (date.Value > limite)
                        signaler(string.Format(fr, "Déclaration à {0} faite le {1:dd/MM/yyyy}, après le délai de 48 heures ouvrables (jusqu'au {2:dd/MM/yyyy}).", organisme, date.Value, limite));
                }
                else if (jour > limite)
                    signaler(string.Format(fr, "Déclaration à {0} en retard : le délai de 48 heures ouvrables a expiré le {1:dd/MM/yyyy}.", organisme, limite));
                else
                    informer(string.Format(fr, "À déclarer à {0} au plus tard le {1:dddd dd/MM/yyyy} (48 heures ouvrables), sur l'imprimé de la CNSS.", organisme, limite));
            };
            verifierDeclaration(a.DateDeclarationCnss, "la CNSS");
            verifierDeclaration(a.DateDeclarationInspection, "l'inspection du travail");

            if (a.AvecArret && !a.Mortel && a.DebutArret.HasValue)
            {
                if (a.DebutArret.Value == a.DateAccident)
                    informer("Le jour de l'accident reste entièrement à la charge de l'employeur : la CNSS indemnise à partir du lendemain.");
                if (!a.FinArret.HasValue)
                    signaler("Saisissez la fin d'arrêt (prévue sur le certificat médical) : elle est nécessaire pour reporter l'arrêt dans les congés et absences.");
                if (a.SalaireJournalier <= 0m)
                    signaler("Renseignez la rémunération journalière : sans elle, les indemnités journalières ne peuvent pas être calculées.");
                else
                {
                    int jours = JoursIndemnises(a, a.DateAccident, a.FinArret ?? jour, jour);
                    if (jours > 0)
                        informer(string.Format(fr, "Indemnités journalières à réclamer à la CNSS : {0} jour(s) × {1:N0} × {2:0.##} % = {3:N0} FCFA{4} (salaire maintenu en entier par l'entreprise).",
                            jours, a.SalaireJournalier, prm.TauxIndemniteJournaliere * 100m, MontantCnss(jours, a.SalaireJournalier, prm),
                            a.FinArret.HasValue ? "" : " à ce jour"));
                }

                bool repriseAttendue = a.FinArret.HasValue && (a.FinArret.Value - a.DebutArret.Value).Days + 1 > prm.RepriseApresArretJours;
                if (repriseAttendue)
                {
                    var finArret = a.FinArret.Value;
                    bool visiteFaite = (visites ?? Enumerable.Empty<VisiteMedicale>()).Any(v => v.DateVisite.HasValue
                        && string.Equals(v.EmployeeId, a.EmployeeId, StringComparison.OrdinalIgnoreCase) && v.DateVisite.Value >= finArret);
                    if (!visiteFaite)
                    {
                        if (finArret < jour) signaler(string.Format(fr, "Arrêt de plus de {0} jours terminé : la visite de reprise n'est pas enregistrée.", prm.RepriseApresArretJours));
                        else informer(string.Format(fr, "Arrêt de plus de {0} jours : prévoir une visite de reprise.", prm.RepriseApresArretJours));
                    }
                }
            }

            if (a.Mortel) informer("Accident mortel : déclaration immédiate ; les ayants droit relèvent des rentes de survivants de la CNSS.");

            return l.OrderByDescending(x => x.Niveau).ToList();
        }

        public static string PremierBlocage(IEnumerable<AlerteSST> alertes)
        {
            var b = (alertes ?? Enumerable.Empty<AlerteSST>()).FirstOrDefault(x => x.Niveau == NiveauAlerte.Bloquant);
            return b == null ? null : b.Message;
        }
    }
}
