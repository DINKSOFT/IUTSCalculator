using System;
using NUnit.Framework;
using IUTSCalculator.Models;
using IUTSCalculator.Services;

namespace IUTSCalculator.Tests
{
    /// <summary>
    /// Équivalence heures / jour retenue par le client (19 octobre 2026) : 8 heures = 1 jour.
    ///
    /// Avant : une absence saisie en heures seulement comptait une journée entière par date
    /// dans les bases « trentième » et « jours ouvrables » (4 h retenues comme 1 jour) ; au taux
    /// horaire, une absence saisie en jours seulement ne donnait lieu à aucune retenue, et les
    /// heures d'une absence à cheval sur deux mois étaient comptées en entier dans chacun.
    /// Les absences saisies en jours, dans les bases en jours, ne changent pas.
    /// </summary>
    [TestFixture]
    [Microsoft.VisualStudio.TestTools.UnitTesting.TestClass]
    public class AbsenceHeuresTests
    {
        private static readonly Employee Salarie = new Employee { EmployeeId = "KS001", BaseSalary = 90000m };

        private static Absence Absence(DateTime debut, DateTime fin, decimal jours, decimal heures, TypeAbsence type = TypeAbsence.AbsenceNonRemuneree)
        {
            return new Absence { EmployeeId = "KS001", Type = type, DateDebut = debut, DateFin = fin, NombreJours = jours, NombreHeures = heures };
        }

        private static ApportAbsences Apport(Absence a, int mois, BaseRetenueAbsence base_)
        {
            return AbsenceService.Apport(Salarie, new[] { a }, 2026, mois, base_);
        }

        [Test]
        [Microsoft.VisualStudio.TestTools.UnitTesting.TestMethod]
        public void AbsenceEnHeures_VautUnJourPourHuitHeures()
        {
            var mardi = new DateTime(2026, 10, 20);
            var a = Absence(mardi, mardi, 0m, 4m);
            Assert.That(a.JoursEquivalents, Is.EqualTo(0.5m));
            Assert.That(AbsenceService.JoursPourMoisDePaie(a, 2026, 10), Is.EqualTo(0.5m));

            Assert.That(Apport(a, 10, BaseRetenueAbsence.TrentiemeMensuel).RetenueSurBrut, Is.EqualTo(1500m));      // 90 000 / 30 × 0,5
            Assert.That(Apport(a, 10, BaseRetenueAbsence.JoursOuvrablesDuMois).RetenueSurBrut, Is.EqualTo(1667m));  // 90 000 / 27 × 0,5
            Assert.That(Apport(a, 10, BaseRetenueAbsence.TauxHoraire).RetenueSurBrut, Is.EqualTo(2077m));           // 90 000 / 173,33 × 4
        }

        [Test]
        [Microsoft.VisualStudio.TestTools.UnitTesting.TestMethod]
        public void TauxHoraire_AbsenceEnJours_VautHuitHeuresParJour()
        {
            var a = Absence(new DateTime(2026, 10, 20), new DateTime(2026, 10, 21), 2m, 0m);
            var apport = Apport(a, 10, BaseRetenueAbsence.TauxHoraire);
            Assert.That(apport.HeuresNonRemunerees, Is.EqualTo(16m));
            Assert.That(apport.RetenueSurBrut, Is.EqualTo(8308m));                                                  // 90 000 / 173,33 × 16
            Assert.That(apport.LibelleRetenue, Does.Contain("16 h"));
        }

        [Test]
        [Microsoft.VisualStudio.TestTools.UnitTesting.TestMethod]
        public void TauxHoraire_HeuresAChevalSurDeuxMois_Reparties()
        {
            // Lundi 28/09 → vendredi 02/10 : 5 jours ouvrables, 3 en septembre, 2 en octobre.
            var a = Absence(new DateTime(2026, 9, 28), new DateTime(2026, 10, 2), 0m, 40m);
            Assert.That(Apport(a, 9, BaseRetenueAbsence.TauxHoraire).HeuresNonRemunerees, Is.EqualTo(24m));
            Assert.That(Apport(a, 10, BaseRetenueAbsence.TauxHoraire).HeuresNonRemunerees, Is.EqualTo(16m));
            Assert.That(Apport(a, 9, BaseRetenueAbsence.TrentiemeMensuel).JoursNonRemuneres, Is.EqualTo(3m));
        }

        [Test]
        [Microsoft.VisualStudio.TestTools.UnitTesting.TestMethod]
        public void AbsenceEnJours_BasesEnJours_Inchangees()
        {
            var a = Absence(new DateTime(2026, 10, 20), new DateTime(2026, 10, 22), 3m, 0m);
            Assert.That(Apport(a, 10, BaseRetenueAbsence.TrentiemeMensuel).RetenueSurBrut, Is.EqualTo(9000m));
            Assert.That(Apport(a, 10, BaseRetenueAbsence.JoursOuvrablesDuMois).RetenueSurBrut, Is.EqualTo(10000m));

            // Jours ET heures saisis : les jours pour les bases en jours, les heures au taux horaire.
            var b = Absence(new DateTime(2026, 10, 20), new DateTime(2026, 10, 20), 1m, 6m);
            Assert.That(Apport(b, 10, BaseRetenueAbsence.TrentiemeMensuel).JoursNonRemuneres, Is.EqualTo(1m));
            Assert.That(Apport(b, 10, BaseRetenueAbsence.TauxHoraire).HeuresNonRemunerees, Is.EqualTo(6m));
        }

        [Test]
        [Microsoft.VisualStudio.TestTools.UnitTesting.TestMethod]
        public void CongesEnHeures_DecompteEnJours()
        {
            var cp = Absence(new DateTime(2026, 10, 20), new DateTime(2026, 10, 21), 0m, 16m, TypeAbsence.CongePaye);
            var salarie = new Employee { EmployeeId = "KS001", BaseSalary = 90000m, DateEntree = new DateTime(2025, 1, 1) };
            var solde = AbsenceService.Solde(salarie, new[] { cp }, new DateTime(2026, 10, 31), 2.5m, 2026, 10);
            Assert.That(solde.JoursPris, Is.EqualTo(2m));
            Assert.That(solde.JoursPrisPeriode, Is.EqualTo(2m));
            Assert.That(Apport(cp, 10, BaseRetenueAbsence.TrentiemeMensuel).JoursCongesPris, Is.EqualTo(2m));
        }

        [Test]
        [Microsoft.VisualStudio.TestTools.UnitTesting.TestMethod]
        public void Saisie_HeuresInvraisemblablesRefusees_EtAbsenceImputee()
        {
            var mardi = new DateTime(2026, 10, 20);
            Assert.That(AbsenceService.Anomalie(Absence(mardi, mardi, 0m, 30m)), Does.Contain("vérifiez la saisie"));
            Assert.That(AbsenceService.Anomalie(Absence(mardi, mardi, 0m, 12m)), Is.Null);

            var imputee = Absence(new DateTime(2026, 9, 25), new DateTime(2026, 9, 25), 0m, 12m);
            imputee.PeriodeImputation = "2026-10";
            Assert.That(AbsenceService.JoursPourMoisDePaie(imputee, 2026, 10), Is.EqualTo(1.5m));
            Assert.That(AbsenceService.HeuresPourMoisDePaie(imputee, 2026, 10), Is.EqualTo(12m));
            Assert.That(AbsenceService.HeuresPourMoisDePaie(imputee, 2026, 9), Is.EqualTo(0m));
        }
    }
}
