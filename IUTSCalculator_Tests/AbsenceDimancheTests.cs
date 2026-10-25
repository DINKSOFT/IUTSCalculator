using System;
using NUnit.Framework;
using IUTSCalculator.Models;
using IUTSCalculator.Services;

namespace IUTSCalculator.Tests
{
    /// <summary>
    /// Dette RH-08 n°3 (corrigée) : une absence non imputée posée sur un dimanche seul
    /// comptait pour 0 jour dans la paie, même avec un nombre de jours saisi — le
    /// décompte « dimanches exclus » valait zéro. Elle compte désormais pour ce qu'on a
    /// saisi, dans la limite du dimanche lui-même. Rien ne change pour les autres absences.
    /// </summary>
    [TestFixture]
    [Microsoft.VisualStudio.TestTools.UnitTesting.TestClass]
    public class AbsenceDimancheTests
    {
        private static readonly DateTime Dimanche = new DateTime(2026, 10, 18);

        private static Absence Absence(DateTime debut, DateTime fin, decimal jours)
        {
            return new Absence
            {
                EmployeeId = "KS001",
                Type = TypeAbsence.AbsenceNonRemuneree,
                DateDebut = debut,
                DateFin = fin,
                NombreJours = jours
            };
        }

        [Test]
        [Microsoft.VisualStudio.TestTools.UnitTesting.TestMethod]
        public void DimancheSeul_CompteDansLeMoisDePaie()
        {
            var a = Absence(Dimanche, Dimanche, 1m);
            Assert.That(AbsenceService.JoursPourMoisDePaie(a, 2026, 10), Is.EqualTo(1m));
            Assert.That(AbsenceService.JoursPourMoisDePaie(a, 2026, 11), Is.EqualTo(0m));
            Assert.That(AbsenceService.Anomalie(a), Is.Null);

            var salarie = new Employee { EmployeeId = "KS001", BaseSalary = 90000m };
            var apport = AbsenceService.Apport(salarie, new[] { a }, 2026, 10, BaseRetenueAbsence.TrentiemeMensuel);
            Assert.That(apport.JoursNonRemuneres, Is.EqualTo(1m));
            Assert.That(apport.RetenueSurBrut, Is.EqualTo(3000m));
        }

        [Test]
        [Microsoft.VisualStudio.TestTools.UnitTesting.TestMethod]
        public void DimancheSeul_PlafonneAUnJour()
        {
            Assert.That(AbsenceService.Anomalie(Absence(Dimanche, Dimanche, 2m)), Does.Contain("seul dimanche"));
        }

        [Test]
        [Microsoft.VisualStudio.TestTools.UnitTesting.TestMethod]
        public void AutresAbsences_Inchangees()
        {
            // Lundi 12 → dimanche 18 : 6 jours ouvrables, le dimanche reste exclu.
            var semaine = Absence(new DateTime(2026, 10, 12), Dimanche, 6m);
            Assert.That(AbsenceService.JoursPourMoisDePaie(semaine, 2026, 10), Is.EqualTo(6m));

            // À cheval sur deux mois : répartition au prorata des jours ouvrables, comme avant.
            var cheval = Absence(new DateTime(2026, 9, 28), new DateTime(2026, 10, 2), 4m);
            Assert.That(AbsenceService.JoursPourMoisDePaie(cheval, 2026, 9), Is.EqualTo(2.4m));
            Assert.That(AbsenceService.JoursPourMoisDePaie(cheval, 2026, 10), Is.EqualTo(1.6m));
            Assert.That(AbsenceService.Anomalie(Absence(new DateTime(2026, 10, 12), Dimanche, 7m)), Does.Contain("dimanches exclus"));
        }
    }
}
