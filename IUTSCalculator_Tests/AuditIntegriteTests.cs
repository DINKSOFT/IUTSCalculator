using System;
using Microsoft.Data.Sqlite;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using IUTSCalculator.Models;
using IUTSCalculator.Services;

namespace IUTSCalculator.Tests
{
    /// <summary>
    /// Journal d'audit (défaut 21) : chaînage d'intégrité SHA-256, détection de la première
    /// entrée altérée, pagination sans plafond silencieux. Les tests sur base créent une
    /// vraie base SQLite dans un dossier temporaire.
    /// </summary>
    [TestFixture]
    [Microsoft.VisualStudio.TestTools.UnitTesting.TestClass]
    public class AuditIntegriteTests
    {
        private static string Empreinte(string details, string precedente)
        {
            return AuditService.CalculerEmpreinte("2026-10-20 10:00:00", "admin", "Modification salaire de base",
                "Salarié", "M001", "Salaire de base", "100 000 FCFA", "120 000 FCFA", details, "POSTE-1", precedente);
        }

        private static void Sql(string db, string sql)
        {
            using (var c = new SqliteConnection("Data Source=" + db + ";Pooling=False"))
            {
                c.Open();
                using (var cmd = c.CreateCommand()) { cmd.CommandText = sql; cmd.ExecuteNonQuery(); }
            }
        }

        /// <summary>
        /// Base de test scellée. Si la migration 38 n'est pas encore intégrée au programme,
        /// les deux colonnes sont ajoutées ici : AuditService les détecte d'elle-même.
        /// </summary>
        private static string BaseScellee()
        {
            string dir = Path.Combine(Path.GetTempPath(), "SigmaAudit_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            string db = Path.Combine(dir, "dossier.db");
            new SQLiteService(db);
            using (var c = new SqliteConnection("Data Source=" + db + ";Pooling=False"))
            {
                c.Open();
                bool present;
                using (var cmd = c.CreateCommand())
                {
                    cmd.CommandText = "SELECT COUNT(*) FROM pragma_table_info('JournalAudit') WHERE name = 'Empreinte';";
                    present = Convert.ToInt32(cmd.ExecuteScalar()) > 0;
                }
                if (!present)
                {
                    using (var cmd = c.CreateCommand())
                    {
                        cmd.CommandText = "ALTER TABLE JournalAudit ADD COLUMN Empreinte TEXT; ALTER TABLE JournalAudit ADD COLUMN EmpreintePrecedente TEXT;";
                        cmd.ExecuteNonQuery();
                    }
                }
            }
            return db;
        }

        [Test]
        [Microsoft.VisualStudio.TestTools.UnitTesting.TestMethod]
        public void CalculerEmpreinte_Deterministe_SensibleAuContenuEtAuChainage()
        {
            string a = Empreinte("Évolution contractuelle", AuditService.EmpreinteOrigine);
            Assert.That(a, Has.Length.EqualTo(64));
            Assert.That(Empreinte("Évolution contractuelle", AuditService.EmpreinteOrigine), Is.EqualTo(a));
            Assert.That(Empreinte("Évolution contractuellE", AuditService.EmpreinteOrigine), Is.Not.EqualTo(a), "un caractère change tout");
            Assert.That(Empreinte("Évolution contractuelle", a), Is.Not.EqualTo(a), "l'empreinte précédente entre dans le calcul");
        }

        [Test]
        [Microsoft.VisualStudio.TestTools.UnitTesting.TestMethod]
        public void CalculerEmpreinte_DecoupageSansAmbiguite()
        {
            // « ab » + « c » et « a » + « bc » ne doivent pas donner la même empreinte.
            string x = AuditService.CalculerEmpreinte("d", "u", "ab", "c", "", "", "", "", "", "", "");
            string y = AuditService.CalculerEmpreinte("d", "u", "a", "bc", "", "", "", "", "", "", "");
            Assert.That(x, Is.Not.EqualTo(y));
        }

        [Test]
        [Microsoft.VisualStudio.TestTools.UnitTesting.TestMethod]
        public async Task VerifierIntegrite_SignaleLaPremiereEntreeAlteree()
        {
            string db = BaseScellee();
            var audit = new AuditService(new SQLiteService(db));
            for (int i = 1; i <= 5; i++)
                await audit.EnregistrerAsync("Saisie variables", "Variables", "2026-09", details: "lot " + i);

            var ok = await audit.VerifierIntegriteAsync();
            Assert.That(ok.ScellementActif, Is.True);
            Assert.That(ok.EstIntegre, Is.True, ok.Message);
            Assert.That(ok.EntreesScellees, Is.EqualTo(5));

            // Altération hors application (les déclencheurs de la migration 38 l'interdisent
            // d'abord : on les retire comme le ferait un fraudeur).
            Sql(db, "DROP TRIGGER IF EXISTS TR_JournalAudit_SansModification; DROP TRIGGER IF EXISTS TR_JournalAudit_SansSuppression;");
            Sql(db, "UPDATE JournalAudit SET Details = 'lot 30' WHERE Id = 3;");

            var ko = await audit.VerifierIntegriteAsync();
            Assert.That(ko.EstIntegre, Is.False);
            Assert.That(ko.PremiereEntreeAlteree, Is.EqualTo(3));
            Assert.That(ko.EntreesScellees, Is.EqualTo(2));

            Sql(db, "UPDATE JournalAudit SET Details = 'lot 3' WHERE Id = 3;");
            Sql(db, "DELETE FROM JournalAudit WHERE Id = 2;");
            var rupture = await audit.VerifierIntegriteAsync();
            Assert.That(rupture.EstIntegre, Is.False);
            Assert.That(rupture.PremiereEntreeAlteree, Is.EqualTo(3), "rupture de chaîne juste après l'entrée supprimée");
        }

        [Test]
        [Microsoft.VisualStudio.TestTools.UnitTesting.TestMethod]
        public async Task Recherche_PaginationEtExportSansPlafond()
        {
            string db = BaseScellee();
            var audit = new AuditService(new SQLiteService(db));
            for (int i = 0; i < 12; i++)
                await audit.EnregistrerAsync("Création salarié", "Salarié", "M" + i);

            var filtre = new FiltreAudit();
            var page1 = await audit.RechercherAsync(new FiltreAudit { Limite = 5, Decalage = 0 });
            var page3 = await audit.RechercherAsync(new FiltreAudit { Limite = 5, Decalage = 10 });
            var tout = await audit.RechercherAsync(new FiltreAudit { Limite = 0 });

            Assert.That(page1.Count, Is.EqualTo(5));
            Assert.That(page3.Count, Is.EqualTo(await audit.CompterAsync(filtre) - 10));
            Assert.That(tout.Count, Is.EqualTo(await audit.CompterAsync(filtre)));
            Assert.That((await audit.StatistiquesAsync(filtre)).Total, Is.EqualTo(tout.Count));
            Assert.That(tout.All(e => e.StatutScellement == "Scellée"), Is.True);
        }
    }
}
