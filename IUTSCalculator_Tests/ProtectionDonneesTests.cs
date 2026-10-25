using System;
using System.Data.SQLite;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using NUnit.Framework;
using IUTSCalculator.Services;

namespace IUTSCalculator.Tests
{
    /// <summary>
    /// Protection des données lors des mises à jour : mise à niveau des bases « tout ou
    /// rien », refus d'une base plus récente que le programme, sauvegarde et restauration.
    /// Ces tests créent de vraies bases SQLite dans un dossier temporaire.
    /// </summary>
    [TestFixture]
    [Microsoft.VisualStudio.TestTools.UnitTesting.TestClass]
    public class ProtectionDonneesTests
    {
        private static string NouveauDossier()
        {
            string d = Path.Combine(Path.GetTempPath(), "SigmaTests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(d);
            return d;
        }

        private static void Sql(string db, string sql)
        {
            using (var c = new SQLiteConnection("Data Source=" + db + ";Version=3;"))
            {
                c.Open();
                using (var cmd = c.CreateCommand()) { cmd.CommandText = sql; cmd.ExecuteNonQuery(); }
            }
        }

        private static string Empreinte(string fichier)
        {
            using (var md5 = MD5.Create())
            using (var f = File.OpenRead(fichier))
                return BitConverter.ToString(md5.ComputeHash(f));
        }

        [Test]
        [Microsoft.VisualStudio.TestTools.UnitTesting.TestMethod]
        public void MiseANiveauEnEchec_LaisseLeFichierIntact()
        {
            string dir = NouveauDossier();
            string db = Path.Combine(dir, "dossier.db");
            new SQLiteService(db);   // base neuve, au niveau du programme
            // Ramène la base au niveau précédent en plaçant un obstacle à la dernière migration.
            Sql(db, "DROP TABLE AccidentsTravail; CREATE TABLE AccidentsTravail (Id INTEGER PRIMARY KEY); " +
                    "DELETE FROM SchemaVersion WHERE Version >= " + SQLiteService.SchemaCible + "; " +
                    "INSERT INTO SchemaVersion (Version, AppliedOn) VALUES (" + (SQLiteService.SchemaCible - 1) + ", '2026-01-01 00:00:00');");
            string avant = Empreinte(db);

            var ex = Assert.Throws<InvalidOperationException>(() => new SQLiteService(db));
            Assert.That(ex.Message, Does.Contain("n'a pas été modifié"));
            Assert.That(Empreinte(db), Is.EqualTo(avant));
            Assert.That(SQLiteService.LireVersionFichier(db), Is.EqualTo(SQLiteService.SchemaCible - 1));
            Assert.That(File.Exists(db + SQLiteService.SuffixeCopieMigration), Is.False);

            Sql(db, "DROP TABLE AccidentsTravail;");
            new SQLiteService(db);
            Assert.That(SQLiteService.LireVersionFichier(db), Is.EqualTo(SQLiteService.SchemaCible));
        }

        [Test]
        [Microsoft.VisualStudio.TestTools.UnitTesting.TestMethod]
        public void BasePlusRecente_RefuseeSansModification()
        {
            string dir = NouveauDossier();
            string db = Path.Combine(dir, "dossier.db");
            new SQLiteService(db);
            Sql(db, "INSERT INTO SchemaVersion (Version, AppliedOn) VALUES (" + (SQLiteService.SchemaCible + 1) + ", '2030-01-01 00:00:00');");
            string avant = Empreinte(db);

            var ex = Assert.Throws<BaseTropRecenteException>(() => new SQLiteService(db));
            Assert.That(ex.VersionBase, Is.EqualTo(SQLiteService.SchemaCible + 1));
            Assert.That(Empreinte(db), Is.EqualTo(avant));
        }

        [Test]
        [Microsoft.VisualStudio.TestTools.UnitTesting.TestMethod]
        public void SauvegardeEtRestauration_AllerRetour()
        {
            string dir = NouveauDossier();
            string racine = Path.Combine(dir, "donnees");
            Directory.CreateDirectory(Path.Combine(racine, "Dossiers"));
            string db = Path.Combine(racine, "Dossiers", "dossier_a.db");
            new SQLiteService(db);
            Sql(db, "INSERT INTO Employees (EmployeeId, FirstName, LastName) VALUES ('M001', 'AWA', 'OUEDRAOGO');");
            File.WriteAllText(Path.Combine(racine, "dossiers.json"), "[{\"RaisonSociale\":\"ENTREPRISE A\",\"DbFileName\":\"Dossiers\\\\dossier_a.db\"}]");
            File.WriteAllText(Path.Combine(racine, "users.json"), "avant");

            var service = new SauvegardeService(racine);
            string zip = Path.Combine(dir, "sauvegarde.zip");
            var r = service.Sauvegarder(zip, "test");
            Assert.That(r.Bases, Is.EqualTo(1));
            var contenu = service.Analyser(zip);
            Assert.That(contenu.EstSauvegardeSigma, Is.True);
            Assert.That(contenu.Dossiers.Single().RaisonSociale, Is.EqualTo("ENTREPRISE A"));
            Assert.That(SauvegardeService.Obstacle(contenu), Is.Null);

            Sql(db, "DELETE FROM Employees;");
            File.WriteAllText(Path.Combine(racine, "users.json"), "après");
            string securite = Path.Combine(dir, "securite.zip");
            service.Restaurer(zip, securite, "test");

            Assert.That(File.Exists(securite), Is.True);
            Assert.That(File.ReadAllText(Path.Combine(racine, "users.json")), Is.EqualTo("avant"));
            using (var c = new SQLiteConnection("Data Source=" + db + ";Version=3;"))
            {
                c.Open();
                using (var cmd = c.CreateCommand())
                {
                    cmd.CommandText = "SELECT COUNT(*) FROM Employees;";
                    Assert.That(Convert.ToInt32(cmd.ExecuteScalar()), Is.EqualTo(1));
                }
            }
        }

        [Test]
        [Microsoft.VisualStudio.TestTools.UnitTesting.TestMethod]
        public void ArchiveEtrangere_Refusee()
        {
            string dir = NouveauDossier();
            string zip = Path.Combine(dir, "autre.zip");
            using (var z = ZipFile.Open(zip, ZipArchiveMode.Create))
            using (var w = new StreamWriter(z.CreateEntry("note.txt").Open()))
                w.Write("autre chose");
            var service = new SauvegardeService(Path.Combine(dir, "donnees"));
            Assert.That(SauvegardeService.Obstacle(service.Analyser(zip)), Does.Contain("n'est pas une sauvegarde"));
            Assert.Throws<InvalidOperationException>(() => service.Restaurer(zip, Path.Combine(dir, "s.zip"), "test"));
        }
    }
}
