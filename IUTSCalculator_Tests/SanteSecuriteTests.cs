using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using IUTSCalculator.Models;
using IUTSCalculator.Services;

namespace IUTSCalculator.Tests
{
    /// <summary>
    /// Module RH-07 — Santé & sécurité au travail : calculs et contrôles du moteur pur
    /// <see cref="ControleSanteSecurite"/>, sans base de données.
    ///
    /// Calendrier de référence : le lundi 19 octobre 2026. Jours ouvrables du lundi au
    /// samedi, jours fériés exclus. Règles CNSS : déclaration dans les 48 heures
    /// ouvrables ; indemnité journalière des 2/3 à partir du lendemain de l'accident.
    /// </summary>
    [TestFixture]
    [Microsoft.VisualStudio.TestTools.UnitTesting.TestClass]
    public class SanteSecuriteTests
    {
        private static readonly DateTime Lundi19 = new DateTime(2026, 10, 19);
        private static readonly ParametresSST Prm = ParametresSST.Defaut();
        private static readonly HashSet<DateTime> SansFerie = new HashSet<DateTime>();

        private static Employee Salarie(string id, DateTime entree, int? posteId = null)
        {
            return new Employee
            {
                EmployeeId = id, LastName = "SAWADOGO", FirstName = id, DateEntree = entree, PosteId = posteId,
                BaseSalary = 150000m, HousingAllowance = 20000m, TransportAllowance = 10000m
            };
        }

        private static AccidentTravail Accident(DateTime date, DateTime? debut = null, DateTime? fin = null, decimal salaireJournalier = 3000m)
        {
            return new AccidentTravail
            {
                Id = 1, Reference = "AT-2026-0001", EmployeeId = "M001", NomSalarie = "SAWADOGO M001",
                DateAccident = date, AvecArret = debut.HasValue, DebutArret = debut, FinArret = fin, SalaireJournalier = salaireJournalier
            };
        }

        private static VisiteMedicale Visite(string id, DateTime? passee, DateTime? prevue = null, TypeVisiteMedicale type = TypeVisiteMedicale.Periodique)
        {
            return new VisiteMedicale { Id = 1, EmployeeId = id, Type = type, DateVisite = passee, DatePrevue = prevue, Aptitude = passee.HasValue ? AptitudeMedicale.Apte : AptitudeMedicale.NonRenseignee };
        }

        private static bool Contient(IEnumerable<AlerteSST> alertes, NiveauAlerte niveau, string fragment)
        {
            return alertes.Any(a => a.Niveau == niveau && a.Message.IndexOf(fragment, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        // ── Déclaration : 48 heures ouvrables ────────────────────────────

        [Test]
        [Microsoft.VisualStudio.TestTools.UnitTesting.TestMethod]
        public void DateLimite_QuaranteHuitHeuresOuvrables()
        {
            Assert.That(ControleSanteSecurite.DateLimiteDeclaration(Accident(Lundi19), Prm, SansFerie), Is.EqualTo(new DateTime(2026, 10, 21)));
            // Vendredi 23 : samedi 24, (dimanche exclu), lundi 26.
            Assert.That(ControleSanteSecurite.DateLimiteDeclaration(Accident(new DateTime(2026, 10, 23)), Prm, SansFerie), Is.EqualTo(new DateTime(2026, 10, 26)));
            var feries = new HashSet<DateTime> { new DateTime(2026, 10, 26) };
            Assert.That(ControleSanteSecurite.DateLimiteDeclaration(Accident(new DateTime(2026, 10, 23)), Prm, feries), Is.EqualTo(new DateTime(2026, 10, 27)));
        }

        [Test]
        [Microsoft.VisualStudio.TestTools.UnitTesting.TestMethod]
        public void Declaration_TardiveOuEnRetard_Signalee()
        {
            var a = Accident(Lundi19);
            a.DateDeclarationCnss = new DateTime(2026, 10, 23);   // après le mercredi 21
            var alertes = ControleSanteSecurite.AnalyserAccident(a, Prm, SansFerie, null, new DateTime(2026, 10, 23));
            Assert.That(Contient(alertes, NiveauAlerte.Avertissement, "après le délai"), Is.True);
            Assert.That(Contient(alertes, NiveauAlerte.Avertissement, "inspection du travail en retard"), Is.True);

            // Dans le délai : simple rappel de l'échéance.
            var b = Accident(Lundi19);
            alertes = ControleSanteSecurite.AnalyserAccident(b, Prm, SansFerie, null, Lundi19);
            Assert.That(alertes.Any(x => x.Niveau == NiveauAlerte.Avertissement), Is.False);
            Assert.That(Contient(alertes, NiveauAlerte.Info, "mercredi 21/10/2026"), Is.True);
        }

        [Test]
        [Microsoft.VisualStudio.TestTools.UnitTesting.TestMethod]
        public void Accident_SaisiesIncoherentes_Bloquees()
        {
            var a = Accident(Lundi19);
            a.EmployeeId = null;
            Assert.That(ControleSanteSecurite.PremierBlocage(ControleSanteSecurite.AnalyserAccident(a, Prm, SansFerie, null, Lundi19)), Does.Contain("victime"));

            a = Accident(new DateTime(2026, 10, 20));
            Assert.That(ControleSanteSecurite.PremierBlocage(ControleSanteSecurite.AnalyserAccident(a, Prm, SansFerie, null, Lundi19)), Does.Contain("futur"));

            a = Accident(Lundi19);
            a.AvecArret = true;
            Assert.That(ControleSanteSecurite.PremierBlocage(ControleSanteSecurite.AnalyserAccident(a, Prm, SansFerie, null, Lundi19)), Does.Contain("premier jour d'arrêt"));

            a = Accident(Lundi19, new DateTime(2026, 10, 25), new DateTime(2026, 10, 22));
            Assert.That(ControleSanteSecurite.PremierBlocage(ControleSanteSecurite.AnalyserAccident(a, Prm, SansFerie, null, Lundi19)), Does.Contain("fin d'arrêt"));

            a = Accident(Lundi19);
            a.DateDeclarationCnss = new DateTime(2026, 10, 18);
            Assert.That(ControleSanteSecurite.PremierBlocage(ControleSanteSecurite.AnalyserAccident(a, Prm, SansFerie, null, Lundi19)), Does.Contain("CNSS précède"));

            a = Accident(Lundi19);
            a.TauxIpp = 120m;
            Assert.That(ControleSanteSecurite.PremierBlocage(ControleSanteSecurite.AnalyserAccident(a, Prm, SansFerie, null, Lundi19)), Does.Contain("incapacité"));

            // Un accident mortel n'exige pas de dates d'arrêt.
            a = Accident(Lundi19);
            a.AvecArret = true;
            a.Mortel = true;
            Assert.That(ControleSanteSecurite.PremierBlocage(ControleSanteSecurite.AnalyserAccident(a, Prm, SansFerie, null, Lundi19)), Is.Null);
        }

        // ── Indemnités journalières ──────────────────────────────────────

        [Test]
        [Microsoft.VisualStudio.TestTools.UnitTesting.TestMethod]
        public void JoursIndemnises_APartirDuLendemain_ChaqueJourCalendaire()
        {
            var debut = new DateTime(2026, 1, 1);
            var fin = new DateTime(2026, 12, 31);
            // Arrêt saisi dès le jour de l'accident : ce jour reste à l'employeur.
            Assert.That(ControleSanteSecurite.JoursIndemnises(Accident(Lundi19, Lundi19, new DateTime(2026, 10, 28)), debut, fin, Lundi19), Is.EqualTo(9));
            Assert.That(ControleSanteSecurite.JoursIndemnises(Accident(Lundi19, new DateTime(2026, 10, 20), new DateTime(2026, 10, 28)), debut, fin, Lundi19), Is.EqualTo(9));

            // Sans fin d'arrêt : jusqu'à la veille de la reprise, sinon jusqu'à aujourd'hui.
            var a = Accident(Lundi19, new DateTime(2026, 10, 20));
            a.DateReprise = new DateTime(2026, 11, 2);
            Assert.That(ControleSanteSecurite.JoursIndemnises(a, debut, fin, new DateTime(2026, 11, 20)), Is.EqualTo(13));
            a.DateReprise = null;
            Assert.That(ControleSanteSecurite.JoursIndemnises(a, debut, fin, new DateTime(2026, 10, 25)), Is.EqualTo(6));

            // Sans arrêt, ou accident mortel : pas d'indemnité journalière.
            Assert.That(ControleSanteSecurite.JoursIndemnises(Accident(Lundi19), debut, fin, Lundi19), Is.EqualTo(0));
            var m = Accident(Lundi19, new DateTime(2026, 10, 20), new DateTime(2026, 10, 28));
            m.Mortel = true;
            Assert.That(ControleSanteSecurite.JoursIndemnises(m, debut, fin, Lundi19), Is.EqualTo(0));
        }

        [Test]
        [Microsoft.VisualStudio.TestTools.UnitTesting.TestMethod]
        public void Indemnites_DeuxTiers_RepartiesParMois()
        {
            Assert.That(ControleSanteSecurite.MontantCnss(9, 3000m, Prm), Is.EqualTo(18000m));
            Assert.That(ControleSanteSecurite.MontantCnss(7, 2900m, Prm), Is.EqualTo(13533m));
            Assert.That(ControleSanteSecurite.MontantCnss(0, 2900m, Prm), Is.EqualTo(0m));

            // Accident le samedi 24/10, arrêt du 25/10 au 10/11.
            var a = Accident(new DateTime(2026, 10, 24), new DateTime(2026, 10, 25), new DateTime(2026, 11, 10), 2900m);
            var octobre = ControleSanteSecurite.EtatIndemnites(new[] { a }, 2026, 10, Prm, Lundi19).Single();
            Assert.That(octobre.Jours, Is.EqualTo(7));
            Assert.That(octobre.Du, Is.EqualTo(new DateTime(2026, 10, 25)));
            Assert.That(octobre.Au, Is.EqualTo(new DateTime(2026, 10, 31)));
            Assert.That(octobre.MontantCnss, Is.EqualTo(13533m));
            Assert.That(octobre.PartEmployeur, Is.EqualTo(7 * 2900m - 13533m));

            var novembre = ControleSanteSecurite.EtatIndemnites(new[] { a }, 2026, 11, Prm, Lundi19).Single();
            Assert.That(novembre.Jours, Is.EqualTo(10));
            Assert.That(novembre.Du, Is.EqualTo(new DateTime(2026, 11, 1)));
            Assert.That(novembre.MoisLibelle, Is.EqualTo("Novembre 2026"));
            Assert.That(ControleSanteSecurite.EtatIndemnites(new[] { a }, 2026, 12, Prm, Lundi19), Is.Empty);
        }

        // ── Remboursement des indemnités journalières (défaut n° 13 b) ───

        [Test]
        [Microsoft.VisualStudio.TestTools.UnitTesting.TestMethod]
        public void RemboursementCnss_MontantReclameStatutEtResteDu()
        {
            // Accident le samedi 24/10, arrêt du 25/10 au 10/11 : 17 jours indemnisés.
            var a = Accident(new DateTime(2026, 10, 24), new DateTime(2026, 10, 25), new DateTime(2026, 11, 10), 2900m);
            var aujourdhui = new DateTime(2026, 11, 20);
            var s = ControleSanteSecurite.Suivi(a, Prm, aujourdhui);
            Assert.That(s.Jours, Is.EqualTo(17));
            Assert.That(s.MontantReclame, Is.EqualTo(ControleSanteSecurite.MontantCnss(17, 2900m, Prm)));
            Assert.That(s.MontantReclame, Is.EqualTo(32867m));
            Assert.That(s.Du, Is.EqualTo(new DateTime(2026, 10, 25)));
            Assert.That(s.Au, Is.EqualTo(new DateTime(2026, 11, 10)));
            Assert.That(s.Statut, Is.EqualTo(StatutRemboursementIJ.ADeposer));
            Assert.That(s.StatutLibelle, Is.EqualTo("À déposer"));
            Assert.That(s.RestantDu, Is.EqualTo(32867m));

            a.DateDepotIJ = new DateTime(2026, 11, 12);
            Assert.That(ControleSanteSecurite.Suivi(a, Prm, aujourdhui).Statut, Is.EqualTo(StatutRemboursementIJ.Depose));

            a.MontantRembourseIJ = 20000m;
            a.DateRemboursementIJ = new DateTime(2026, 11, 19);
            s = ControleSanteSecurite.Suivi(a, Prm, aujourdhui);
            Assert.That(s.Statut, Is.EqualTo(StatutRemboursementIJ.RemboursePartiellement));
            Assert.That(s.RestantDu, Is.EqualTo(12867m));

            a.MontantRembourseIJ = 32867m;
            s = ControleSanteSecurite.Suivi(a, Prm, aujourdhui);
            Assert.That(s.Statut, Is.EqualTo(StatutRemboursementIJ.Rembourse));
            Assert.That(s.RestantDu, Is.EqualTo(0m));

            // Arrêt sans jour indemnisé : pas de ligne de suivi.
            Assert.That(ControleSanteSecurite.SuiviRemboursements(new[] { a, Accident(Lundi19) }, Prm, aujourdhui).Count, Is.EqualTo(1));
        }

        [Test]
        [Microsoft.VisualStudio.TestTools.UnitTesting.TestMethod]
        public void RemboursementCnss_SaisiesIncoherentes_Refusees()
        {
            var aujourdhui = new DateTime(2026, 11, 20);
            var s = ControleSanteSecurite.Suivi(Accident(new DateTime(2026, 10, 24), new DateTime(2026, 10, 25), new DateTime(2026, 11, 10), 2900m), Prm, aujourdhui);
            Assert.That(ControleSanteSecurite.AnomalieRemboursement(s, aujourdhui), Is.Null);

            s.MontantRembourse = 10000m;
            s.DateRemboursement = new DateTime(2026, 11, 15);
            Assert.That(ControleSanteSecurite.AnomalieRemboursement(s, aujourdhui), Does.Contain("dépôt"));
            s.DateDepot = new DateTime(2026, 11, 18);
            Assert.That(ControleSanteSecurite.AnomalieRemboursement(s, aujourdhui), Does.Contain("précéder"));
            s.DateDepot = new DateTime(2026, 11, 2);
            Assert.That(ControleSanteSecurite.AnomalieRemboursement(s, aujourdhui), Is.Null);
            s.DateRemboursement = null;
            Assert.That(ControleSanteSecurite.AnomalieRemboursement(s, aujourdhui), Does.Contain("date du remboursement"));
            s.MontantRembourse = -1m;
            Assert.That(ControleSanteSecurite.AnomalieRemboursement(s, aujourdhui), Does.Contain("négatif"));
        }

        // ── Prolongation d'un arrêt touchant un mois clôturé (défaut n° 13 a) ──

        private static Absence ArretAT(DateTime debut, DateTime fin)
        {
            return new Absence
            {
                EmployeeId = "M001", Type = TypeAbsence.AccidentTravail, DateDebut = debut, DateFin = fin,
                NombreJours = Absence.JoursEntre(debut, fin, true)
            };
        }

        [Test]
        [Microsoft.VisualStudio.TestTools.UnitTesting.TestMethod]
        public void ProlongationArret_MoisClotureInchange_Acceptee()
        {
            var septembre = new[] { new DateTime(2026, 9, 1) };

            // Arrêt du 10 au 30/09, septembre clôturé : prolongé jusqu'au 15/10.
            var avant = ArretAT(new DateTime(2026, 9, 10), new DateTime(2026, 9, 30));
            Assert.That(ControleSanteSecurite.RefusModificationArret(avant, ArretAT(new DateTime(2026, 9, 10), new DateTime(2026, 10, 15)), septembre), Is.Null);

            // Arrêt à cheval (10/09 → 05/10) : prolongé ou raccourci dans octobre, septembre intact.
            avant = ArretAT(new DateTime(2026, 9, 10), new DateTime(2026, 10, 5));
            Assert.That(ControleSanteSecurite.RefusModificationArret(avant, ArretAT(new DateTime(2026, 9, 10), new DateTime(2026, 10, 31)), septembre), Is.Null);
            Assert.That(ControleSanteSecurite.RefusModificationArret(avant, ArretAT(new DateTime(2026, 9, 10), new DateTime(2026, 10, 2)), septembre), Is.Null);
            Assert.That(ControleSanteSecurite.MoisCouverts(avant), Is.EqualTo(new[] { new DateTime(2026, 9, 1), new DateTime(2026, 10, 1) }));
        }

        [Test]
        [Microsoft.VisualStudio.TestTools.UnitTesting.TestMethod]
        public void ProlongationArret_JoursDuMoisClotureModifies_Refusee()
        {
            var septembre = new[] { new DateTime(2026, 9, 1) };

            // Fin au 20/09, septembre clôturé : prolonger ajouterait les 21-30/09.
            var avant = ArretAT(new DateTime(2026, 9, 10), new DateTime(2026, 9, 20));
            var refus = ControleSanteSecurite.RefusModificationArret(avant, ArretAT(new DateTime(2026, 9, 10), new DateTime(2026, 10, 10)), septembre);
            Assert.That(refus, Does.Contain("septembre 2026"));
            Assert.That(refus, Does.Contain("du 10/09 au 20/09"));
            Assert.That(refus, Does.Contain("Périodes de paie"));

            // Premier jour d'arrêt déplacé dans le mois clôturé, ou arrêt raccourci jusque dans septembre.
            avant = ArretAT(new DateTime(2026, 9, 10), new DateTime(2026, 10, 5));
            Assert.That(ControleSanteSecurite.RefusModificationArret(avant, ArretAT(new DateTime(2026, 9, 12), new DateTime(2026, 10, 5)), septembre), Is.Not.Null);
            Assert.That(ControleSanteSecurite.RefusModificationArret(avant, ArretAT(new DateTime(2026, 9, 10), new DateTime(2026, 9, 25)), septembre), Is.Not.Null);

            // Jours saisis à la main différents du décompte : la part de septembre changerait.
            var ajuste = ArretAT(new DateTime(2026, 9, 10), new DateTime(2026, 10, 5));
            ajuste.NombreJours = 20m;
            Assert.That(ControleSanteSecurite.EcartDansMois(ajuste, ArretAT(new DateTime(2026, 9, 10), new DateTime(2026, 10, 20)), septembre[0]), Does.Contain("jours décomptés"));

            // Sans mois clôturé, rien n'est refusé ici (la règle commune s'applique).
            Assert.That(ControleSanteSecurite.RefusModificationArret(avant, ArretAT(new DateTime(2026, 9, 12), new DateTime(2026, 10, 5)), new DateTime[0]), Is.Null);
        }

        [Test]
        [Microsoft.VisualStudio.TestTools.UnitTesting.TestMethod]
        public void SalaireJournalier_MensuelSurTrente_EtTauxEnPourcentage()
        {
            Assert.That(ControleSanteSecurite.SalaireJournalierPropose(Salarie("M001", new DateTime(2020, 1, 1)), Prm), Is.EqualTo(6000m));

            var p = ParametresSST.Defaut();
            Assert.That(p.TauxIndemniteJournalierePourcent, Is.EqualTo(66.67m));
            p.TauxIndemniteJournalierePourcent = 50m;
            Assert.That(p.TauxIndemniteJournaliere, Is.EqualTo(0.5m));
            p.TauxIndemniteJournalierePourcent = 66.67m;          // ressaisi : revient aux 2/3 exacts
            Assert.That(p.TauxIndemniteJournaliere, Is.EqualTo(2m / 3m));
            Assert.That(SanteSecuriteService.AnomalieParametres(p), Is.Null);
            p.DelaiDeclarationJoursOuvrables = 0;
            Assert.That(SanteSecuriteService.AnomalieParametres(p), Does.Contain("48 heures"));
        }

        // ── Visites médicales ────────────────────────────────────────────

        [Test]
        [Microsoft.VisualStudio.TestTools.UnitTesting.TestMethod]
        public void ProchaineVisite_EmbauchePeriodiciteEtPoste()
        {
            TypeVisiteMedicale type;
            var s = Salarie("M001", new DateTime(2026, 10, 1), posteId: 7);
            Assert.That(ControleSanteSecurite.ProchaineVisite(s, null, null, Prm, out type), Is.EqualTo(new DateTime(2026, 10, 1)));
            Assert.That(type, Is.EqualTo(TypeVisiteMedicale.Embauche));

            var visites = new[] { Visite("M001", new DateTime(2026, 3, 15)) };
            Assert.That(ControleSanteSecurite.ProchaineVisite(s, visites, null, Prm, out type), Is.EqualTo(new DateTime(2027, 3, 15)));
            Assert.That(type, Is.EqualTo(TypeVisiteMedicale.Periodique));

            var postes = new[] { new PeriodicitePoste { PosteId = 7, Code = "SOUD", Intitule = "Soudeur", Mois = 6 } };
            Assert.That(ControleSanteSecurite.ProchaineVisite(s, visites, postes, Prm, out type), Is.EqualTo(new DateTime(2026, 9, 15)));

            // Le médecin fixe la prochaine visite : sa date prime.
            visites[0].ProchaineVisite = new DateTime(2026, 12, 1);
            Assert.That(ControleSanteSecurite.ProchaineVisite(s, visites, postes, Prm, out type), Is.EqualTo(new DateTime(2026, 12, 1)));
        }

        [Test]
        [Microsoft.VisualStudio.TestTools.UnitTesting.TestMethod]
        public void Reprise_AttendueApresArretLong_SaufVisitePassee()
        {
            var arrets = new[]
            {
                new ArretSST { EmployeeId = "M001", Type = TypeAbsence.AccidentTravail, Debut = new DateTime(2026, 9, 10), Fin = new DateTime(2026, 10, 9) },
                new ArretSST { EmployeeId = "M002", Type = TypeAbsence.Maladie, Debut = new DateTime(2026, 10, 1), Fin = new DateTime(2026, 10, 14) },
                new ArretSST { EmployeeId = "M003", Type = TypeAbsence.CongePaye, Debut = new DateTime(2026, 8, 1), Fin = new DateTime(2026, 8, 31) }
            };
            var attendues = ControleSanteSecurite.ReprisesAttendues(arrets, null, Prm);
            Assert.That(attendues.Select(a => a.EmployeeId), Is.EquivalentTo(new[] { "M001" }));

            var visites = new[] { Visite("M001", new DateTime(2026, 10, 12), type: TypeVisiteMedicale.Reprise) };
            Assert.That(ControleSanteSecurite.ReprisesAttendues(arrets, visites, Prm), Is.Empty);
        }

        [Test]
        [Microsoft.VisualStudio.TestTools.UnitTesting.TestMethod]
        public void Echeancier_VisitesEpiAccidents()
        {
            var nouveau = Salarie("M001", new DateTime(2026, 10, 1));            // entré le 1er : visite d'embauche en retard
            var ancien = Salarie("M002", new DateTime(2019, 1, 1));              // visite au 01/11/2025 : due le 01/11/2026
            var ajour = Salarie("M003", new DateTime(2019, 1, 1));               // visite en juin : rien à signaler
            var convoque = Salarie("M004", new DateTime(2019, 1, 1));            // visite programmée : remplace l'échéance calculée
            var visites = new List<VisiteMedicale>
            {
                Visite("M002", new DateTime(2025, 11, 1)),
                Visite("M003", new DateTime(2026, 6, 1)),
                Visite("M004", new DateTime(2025, 9, 1)),
                Visite("M004", null, new DateTime(2026, 10, 27))
            };
            visites[3].Id = 44;
            var dotations = new[]
            {
                new DotationEPI { Id = 5, EmployeeId = "M003", NomSalarie = "SAWADOGO M003", TypeEpiLibelle = "Casque de sécurité", Etat = EtatDotation.EnService, DateRenouvellement = new DateTime(2026, 11, 5) },
                new DotationEPI { Id = 6, EmployeeId = "M003", TypeEpiLibelle = "Gants", Etat = EtatDotation.Rendu, DateRenouvellement = new DateTime(2026, 10, 1) },
                new DotationEPI { Id = 7, EmployeeId = "M003", TypeEpiLibelle = "Chaussures", Etat = EtatDotation.EnService, DateRenouvellement = new DateTime(2027, 6, 1) }
            };
            var accident = Accident(new DateTime(2026, 10, 15));
            accident.Id = 9;
            var arrets = new[] { new ArretSST { EmployeeId = "M003", Type = TypeAbsence.AccidentTravail, Debut = new DateTime(2026, 9, 1), Fin = new DateTime(2026, 10, 5) } };

            var e = ControleSanteSecurite.Echeances(new[] { nouveau, ancien, ajour, convoque }, visites, null, arrets, dotations, new[] { accident }, Prm, SansFerie, Lundi19);

            var embauche = e.Single(x => x.EmployeeId == "M001" && x.Categorie == "Visite médicale");
            Assert.That(embauche.Libelle, Does.Contain("embauche"));
            Assert.That(embauche.JoursRestants, Is.EqualTo(-18));
            Assert.That(embauche.TypeVisite, Is.EqualTo(TypeVisiteMedicale.Embauche));

            Assert.That(e.Single(x => x.EmployeeId == "M002").Date, Is.EqualTo(new DateTime(2026, 11, 1)));
            var programmee = e.Single(x => x.EmployeeId == "M004");
            Assert.That(programmee.VisiteId, Is.EqualTo(44));
            Assert.That(programmee.Date, Is.EqualTo(new DateTime(2026, 10, 27)));

            // M003 : visite de reprise (arrêt AT de 35 jours) et casque à renouveler ; ni les gants rendus ni les chaussures.
            var m003 = e.Where(x => x.EmployeeId == "M003").ToList();
            Assert.That(m003.Count, Is.EqualTo(2));
            Assert.That(m003.Any(x => x.TypeVisite == TypeVisiteMedicale.Reprise && x.Date == new DateTime(2026, 10, 6)), Is.True);
            Assert.That(m003.Single(x => x.Categorie == "EPI").DotationId, Is.EqualTo(5));

            // Accident du jeudi 15 non déclaré : échéance au samedi 17, dépassée.
            var at = e.Single(x => x.Categorie == "Accident");
            Assert.That(at.Date, Is.EqualTo(new DateTime(2026, 10, 17)));
            Assert.That(at.EstDepassee, Is.True);
            Assert.That(at.AccidentId, Is.EqualTo(9));

            // Une visite de reprise programmée fait disparaître l'alerte « reprise à prévoir ».
            visites.Add(new VisiteMedicale { Id = 50, EmployeeId = "M003", Type = TypeVisiteMedicale.Reprise, DatePrevue = new DateTime(2026, 10, 22) });
            e = ControleSanteSecurite.Echeances(new[] { ajour }, visites, null, arrets, dotations, null, Prm, SansFerie, Lundi19);
            Assert.That(e.Count(x => x.Categorie == "Visite médicale"), Is.EqualTo(1));
            Assert.That(e.Single(x => x.Categorie == "Visite médicale").VisiteId, Is.EqualTo(50));
        }

        [Test]
        [Microsoft.VisualStudio.TestTools.UnitTesting.TestMethod]
        public void SituationMedicale_DerniereVisiteEtProchaineEcheance()
        {
            var visites = new[]
            {
                new VisiteMedicale { Id = 1, EmployeeId = "M002", Type = TypeVisiteMedicale.Periodique, DateVisite = new DateTime(2025, 11, 1), Aptitude = AptitudeMedicale.AptAvecReserves, Restrictions = "Pas de port de charges" }
            };
            var s = ControleSanteSecurite.SituationsMedicales(
                new[] { Salarie("M001", new DateTime(2026, 10, 1)), Salarie("M002", new DateTime(2019, 1, 1)) }, visites, null, Prm, Lundi19);
            Assert.That(s.Count, Is.EqualTo(2));
            Assert.That(s[0].EmployeeId, Is.EqualTo("M001"));                     // la plus urgente d'abord
            Assert.That(s[0].DerniereVisiteLibelle, Is.EqualTo("Aucune"));
            Assert.That(s[0].EstDepassee, Is.True);
            Assert.That(s[1].AvecReserves, Is.True);
            Assert.That(s[1].ProchaineEcheance, Is.EqualTo(new DateTime(2026, 11, 1)));
            Assert.That(s[1].PeriodiciteMois, Is.EqualTo(12));
        }

        [Test]
        [Microsoft.VisualStudio.TestTools.UnitTesting.TestMethod]
        public void Saisies_VisiteEtRemise_Controlees()
        {
            Assert.That(ControleSanteSecurite.AnomalieVisite(new VisiteMedicale { EmployeeId = "M001" }, Lundi19), Does.Contain("date"));
            Assert.That(ControleSanteSecurite.AnomalieVisite(new VisiteMedicale { EmployeeId = "M001", DateVisite = new DateTime(2026, 10, 25) }, Lundi19), Does.Contain("pas encore eu lieu"));
            Assert.That(ControleSanteSecurite.AnomalieVisite(new VisiteMedicale { EmployeeId = "M001", DatePrevue = Lundi19, Aptitude = AptitudeMedicale.Apte }, Lundi19), Does.Contain("aptitude"));
            Assert.That(ControleSanteSecurite.AnomalieVisite(new VisiteMedicale { EmployeeId = "M001", DateVisite = Lundi19, Aptitude = AptitudeMedicale.Apte }, Lundi19), Is.Null);

            var d = new DotationEPI { EmployeeId = "M001", TypeEpiId = 1, DateRemise = Lundi19, Quantite = 1 };
            Assert.That(ControleSanteSecurite.AnomalieDotation(d), Is.Null);
            d.Etat = EtatDotation.PerduOuUse;
            Assert.That(ControleSanteSecurite.AnomalieDotation(d), Does.Contain("retour"));
            d.DateRetour = new DateTime(2026, 10, 1);
            Assert.That(ControleSanteSecurite.AnomalieDotation(d), Does.Contain("précède"));
            d.DateRetour = null;
            d.Etat = EtatDotation.EnService;
            d.Quantite = 0;
            Assert.That(ControleSanteSecurite.AnomalieDotation(d), Does.Contain("quantité"));
        }
    }
}
