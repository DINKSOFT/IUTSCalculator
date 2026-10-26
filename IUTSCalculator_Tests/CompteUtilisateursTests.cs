using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using NUnit.Framework;
using IUTSCalculator.Models;
using IUTSCalculator.Services;

namespace IUTSCalculator.Tests
{
    /// <summary>
    /// Lecture de <c>users.json</c> : format chiffré (DPAPI), ancien format en clair (migré), et
    /// fichier illisible (jamais écrasé en silence, jamais pris pour un premier lancement).
    /// </summary>
    [TestFixture]
    [Microsoft.VisualStudio.TestTools.UnitTesting.TestClass]
    public class CompteUtilisateursTests
    {
        private const string JsonEnClair =
            "[\r\n  {\r\n    \"userId\": \"u-1\",\r\n    \"username\": \"admin\",\r\n" +
            "    \"passwordHash\": \"PBKDF2$100000$c2Vs$aGFzaA==\",\r\n    \"role\": 2,\r\n    \"displayName\": \"Administrateur\"\r\n  }\r\n]";

        private string _dossier;

        [SetUp]
        [Microsoft.VisualStudio.TestTools.UnitTesting.TestInitialize]
        public void Preparer()
        {
            _dossier = Path.Combine(Path.GetTempPath(), "iuts_users_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dossier);
        }

        [TearDown]
        [Microsoft.VisualStudio.TestTools.UnitTesting.TestCleanup]
        public void Nettoyer()
        {
            try { Directory.Delete(_dossier, true); } catch { }
        }

        private string Fichier { get { return Path.Combine(_dossier, "users.json"); } }

        [Test]
        [Microsoft.VisualStudio.TestTools.UnitTesting.TestMethod]
        public async Task FichierChiffre_EnregistreEtRelu()
        {
            var service = new UserAccountService(_dossier);
            Assert.That(await service.AddUserAsync(new UserAccount { Username = "admin", DisplayName = "Admin", Role = UserRole.Admin }, "secret1"), Is.True);

            var relu = await new UserAccountService(_dossier).LoadUsersAsync();

            Assert.That(relu.Count, Is.EqualTo(1));
            Assert.That(relu[0].Username, Is.EqualTo("admin"));
            Assert.That(File.ReadAllBytes(Fichier)[0], Is.Not.EqualTo((byte)'['), "le fichier doit être chiffré, pas du JSON lisible");
        }

        [Test]
        [Microsoft.VisualStudio.TestTools.UnitTesting.TestMethod]
        public async Task AncienFichierEnClair_EstLuPuisChiffre()
        {
            File.WriteAllText(Fichier, JsonEnClair, new UTF8Encoding(false));
            var service = new UserAccountService(_dossier);

            var comptes = await service.LoadUsersAsync();

            Assert.That(comptes.Count, Is.EqualTo(1), "le compte existant ne doit pas disparaître");
            Assert.That(comptes[0].Username, Is.EqualTo("admin"));
            Assert.That(comptes[0].Role, Is.EqualTo(UserRole.Admin));
            Assert.That(service.FichierIllisible, Is.False, "ce n'est pas un fichier illisible");
            Assert.That(File.ReadAllBytes(Fichier)[0], Is.Not.EqualTo((byte)'['), "le fichier doit être re-chiffré");
            Assert.That(File.ReadAllText(Fichier + ".avant-chiffrement"), Is.EqualTo(JsonEnClair), "la copie en clair est conservée");

            var secondeLecture = await new UserAccountService(_dossier).LoadUsersAsync();
            Assert.That(secondeLecture.Single().Username, Is.EqualTo("admin"), "la migration ne se refait pas, le fichier chiffré se relit");
        }

        [Test]
        [Microsoft.VisualStudio.TestTools.UnitTesting.TestMethod]
        public async Task FichierIllisible_EstSignaleSansEtreEfface()
        {
            byte[] abime = Enumerable.Range(0, 64).Select(i => (byte)(i * 7 + 3)).ToArray();
            File.WriteAllBytes(Fichier, abime);
            var service = new UserAccountService(_dossier);

            var comptes = await service.LoadUsersAsync();

            Assert.That(comptes, Is.Empty);
            Assert.That(service.FichierIllisible, Is.True, "l'appelant doit savoir que ce n'est pas un premier lancement");
            Assert.That(service.SauvegardeFichierIllisible, Is.Not.Null);
            Assert.That(File.ReadAllBytes(service.SauvegardeFichierIllisible), Is.EqualTo(abime), "la copie conservée est identique");
            Assert.That(File.ReadAllBytes(Fichier), Is.EqualTo(abime), "la lecture ne modifie pas le fichier");
        }

        [Test]
        [Microsoft.VisualStudio.TestTools.UnitTesting.TestMethod]
        public async Task AucunFichier_EstUnPremierLancement()
        {
            var service = new UserAccountService(_dossier);

            var comptes = await service.LoadUsersAsync();

            Assert.That(comptes, Is.Empty);
            Assert.That(service.FichierIllisible, Is.False);
        }
    }
}
