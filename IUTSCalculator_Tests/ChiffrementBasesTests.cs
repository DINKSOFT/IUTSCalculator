using System;
using System.IO;
using System.Linq;
using System.Text;
using NUnit.Framework;
using IUTSCalculator.Models;
using IUTSCalculator.Services;

namespace IUTSCalculator.Tests
{
    /// <summary>
    /// Chiffrement des bases (SQLite3MC, format SQLCipher) : clé du poste protégée par DPAPI,
    /// mise en place du schéma dans une base chiffrée, accès Microsoft.Data.Sqlite et garde
    /// « Lecture seule ».
    /// </summary>
    [TestFixture]
    [Microsoft.VisualStudio.TestTools.UnitTesting.TestClass]
    public class ChiffrementBasesTests
    {
        private static string NouveauDossier()
        {
            string d = Path.Combine(Path.GetTempPath(), "SigmaChiffrement_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(d);
            return d;
        }

        private static bool EnteteEnClair(string fichier)
        {
            var b = new byte[16];
            using (var f = File.OpenRead(fichier)) f.Read(b, 0, 16);
            return Encoding.ASCII.GetString(b).StartsWith("SQLite format 3");
        }

        private static PersonneACharge Enfant(string prenom)
        {
            return new PersonneACharge { Nom = "OUEDRAOGO", Prenom = prenom, Lien = LienParente.Enfant, DateNaissance = new DateTime(2015, 3, 1) };
        }

        [Test]
        [Microsoft.VisualStudio.TestTools.UnitTesting.TestMethod]
        public void CleDuPoste_CreeeUneFois_PuisRelue()
        {
            string dir = NouveauDossier();
            string cle = CleDonnees.ObtenirOuCreer(dir);

            Assert.That(cle, Has.Length.EqualTo(64));
            Assert.That(cle.All(Uri.IsHexDigit), Is.True);
            Assert.That(CleDonnees.ObtenirOuCreer(dir), Is.EqualTo(cle));
            Assert.That(CleDonnees.Lire(dir), Is.EqualTo(cle));
            // Le fichier ne contient pas la clé en clair.
            Assert.That(Encoding.ASCII.GetString(File.ReadAllBytes(CleDonnees.Chemin(dir))), Does.Not.Contain(cle));
        }

        [Test]
        [Microsoft.VisualStudio.TestTools.UnitTesting.TestMethod]
        public void CleIllisible_JamaisRemplacee()
        {
            string dir = NouveauDossier();
            File.WriteAllBytes(CleDonnees.Chemin(dir), new byte[] { 1, 2, 3, 4 });

            Assert.Throws<CleDonneesIllisibleException>(() => CleDonnees.ObtenirOuCreer(dir));
            Assert.That(File.ReadAllBytes(CleDonnees.Chemin(dir)), Is.EqualTo(new byte[] { 1, 2, 3, 4 }));
        }

        [Test]
        [Microsoft.VisualStudio.TestTools.UnitTesting.TestMethod]
        public void BaseChiffree_SchemaEtServicePilote()
        {
            string dir = NouveauDossier();
            string db = Path.Combine(dir, "dossier.db");
            string cle = CleDonnees.ObtenirOuCreer(dir);
            SQLiteService.FournisseurCle = () => cle;
            try
            {
                var sqlite = new SQLiteService(db);
                var service = new PersonneAChargeService(sqlite);
                service.EnregistrerToutAsync("M001", new[] { Enfant("AWA"), Enfant("ISSA") }).GetAwaiter().GetResult();

                var relues = service.ChargerAsync("M001").GetAwaiter().GetResult();
                Assert.That(relues.Select(p => p.Prenom), Is.EquivalentTo(new[] { "AWA", "ISSA" }));
                Assert.That(relues.First().DateNaissance, Is.EqualTo(new DateTime(2015, 3, 1)));

                // Le fichier est chiffré : ni en-tête SQLite, ni nom lisible.
                Assert.That(EnteteEnClair(db), Is.False);
                Assert.That(Encoding.UTF8.GetString(File.ReadAllBytes(db)), Does.Not.Contain("OUEDRAOGO"));

                // Sans la clé, la base est illisible ; avec, elle est au niveau attendu.
                Assert.That(SQLiteService.LireVersionFichier(db), Is.EqualTo(-1));
                Assert.That(SQLiteService.LireVersionFichier(db, cle), Is.EqualTo(SQLiteService.SchemaCible));

                // Réouverture (nouvelle instance) : le schéma est reconnu, pas de migration.
                Assert.That(new PersonneAChargeService(new SQLiteService(db)).ChargerAsync("M001").GetAwaiter().GetResult(), Has.Count.EqualTo(2));
            }
            finally
            {
                SQLiteService.FournisseurCle = null;
            }
        }

        [Test]
        [Microsoft.VisualStudio.TestTools.UnitTesting.TestMethod]
        public void SansCle_BaseEnClairCommeAvant()
        {
            string dir = NouveauDossier();
            string db = Path.Combine(dir, "dossier.db");
            var sqlite = new SQLiteService(db);
            Assert.That(EnteteEnClair(db), Is.True);

            new PersonneAChargeService(sqlite).EnregistrerToutAsync("M002", new[] { Enfant("FATI") }).GetAwaiter().GetResult();

            using (var c = sqlite.CreerConnexion())
            using (var cmd = c.CreateCommand())
            {
                cmd.CommandText = "SELECT Prenom FROM PersonnesACharge WHERE EmployeeId = 'M002';";
                Assert.That(cmd.ExecuteScalar(), Is.EqualTo("FATI"));
            }
        }

        [Test]
        [Microsoft.VisualStudio.TestTools.UnitTesting.TestMethod]
        public void ParametreNull_EnregistreCommeNull()
        {
            // System.Data.SQLite liait une valeur null à NULL ; Microsoft.Data.Sqlite la refuse.
            // AjouterValeur rétablit l'ancien comportement pour tous les services.
            var sqlite = new SQLiteService(Path.Combine(NouveauDossier(), "dossier.db"));
            using (var c = sqlite.CreerConnexion())
            using (var cmd = c.CreateCommand())
            {
                cmd.CommandText = "SELECT @v IS NULL;";
                cmd.Parameters.AjouterValeur("@v", null);
                Assert.That(cmd.ExecuteScalar(), Is.EqualTo(1L));
            }
        }

        [Test]
        [Microsoft.VisualStudio.TestTools.UnitTesting.TestMethod]
        public void GardeLectureSeule_BloqueLesEcrituresDuPilote()
        {
            string dir = NouveauDossier();
            var sqlite = new SQLiteService(Path.Combine(dir, "dossier.db"));
            var service = new PersonneAChargeService(sqlite);
            service.EnregistrerToutAsync("M003", new[] { Enfant("ALI") }).GetAwaiter().GetResult();

            GardeLectureSeule.Activer();
            UserSession.Instance.Login(new UserAccount { Username = "lecteur", Role = UserRole.ReadOnly });
            try
            {
                var ex = Assert.Catch(() => service.EnregistrerToutAsync("M003", new[] { Enfant("NOUVEAU") }).GetAwaiter().GetResult());
                Assert.That(GardeLectureSeule.EstRefus(ex), Is.True, ex.ToString());

                // La lecture reste permise, et rien n'a été modifié.
                Assert.That(service.ChargerAsync("M003").GetAwaiter().GetResult().Single().Prenom, Is.EqualTo("ALI"));
            }
            finally
            {
                UserSession.Instance.Logout();
                SQLiteService.GardeConnexionSqlite = null;
            }
        }
    }
}
