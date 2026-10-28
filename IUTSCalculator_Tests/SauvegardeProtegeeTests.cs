using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using NUnit.Framework;
using IUTSCalculator.Models;
using IUTSCalculator.Services;

namespace IUTSCalculator.Tests
{
    /// <summary>
    /// Sauvegardes portables protégées par le mot de passe de sauvegarde : deux « postes » sont
    /// simulés par deux dossiers de données, chacun avec sa propre clé des bases.
    /// </summary>
    [TestFixture]
    [Microsoft.VisualStudio.TestTools.UnitTesting.TestClass]
    public class SauvegardeProtegeeTests
    {
        private const string MotDePasse = "Sauvegarde-2026";

        private class Poste
        {
            public string Dossier;
            public string Cle;
            public string Base { get { return Path.Combine(Dossier, "Dossiers", "dossier_a.db"); } }
            public SauvegardeService Service() { return new SauvegardeService(Dossier, () => Cle); }
        }

        private static string NouveauDossier()
        {
            string d = Path.Combine(Path.GetTempPath(), "SigmaSauvegardeProtegee_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(d);
            return d;
        }

        private static Poste NouveauPoste()
        {
            string dir = NouveauDossier();
            return new Poste { Dossier = dir, Cle = ChiffrementBases.ObtenirCleDuPoste(dir) };
        }

        private static bool EnteteEnClair(byte[] debut)
        {
            return Encoding.ASCII.GetString(debut, 0, 15) == "SQLite format 3";
        }

        /// <summary>Poste A : un dossier avec un salarié à charge, ses comptes, et le registre.</summary>
        private static Poste PosteAvecDonnees()
        {
            var poste = NouveauPoste();
            Directory.CreateDirectory(Path.GetDirectoryName(poste.Base));
            SQLiteService.FournisseurCle = () => poste.Cle;
            try
            {
                new PersonneAChargeService(new SQLiteService(poste.Base)).EnregistrerToutAsync("M100", new[]
                {
                    new PersonneACharge { Nom = "OUEDRAOGO", Prenom = "AWA", Lien = LienParente.Enfant, DateNaissance = new DateTime(2015, 3, 1) }
                }).GetAwaiter().GetResult();
            }
            finally
            {
                SQLiteService.FournisseurCle = null;
            }
            File.WriteAllText(Path.Combine(poste.Dossier, "dossiers.json"), "[{\"RaisonSociale\":\"ENTREPRISE ZOODO\",\"DbFileName\":\"Dossiers\\\\dossier_a.db\"}]");
            new UserAccountService(poste.Dossier).SaveUsersAsync(new List<UserAccount>
            {
                new UserAccount { UserId = "1", Username = "admin.zoodo", DisplayName = "Administrateur", Role = UserRole.Admin, PasswordHash = "x" }
            }).GetAwaiter().GetResult();
            MotDePasseSauvegarde.Definir(poste.Dossier, MotDePasse);
            return poste;
        }

        private static string PrenomLu(Poste poste)
        {
            SQLiteService.FournisseurCle = () => poste.Cle;
            try
            {
                return new PersonneAChargeService(new SQLiteService(poste.Base)).ChargerAsync("M100").GetAwaiter().GetResult().Single().Prenom;
            }
            finally
            {
                SQLiteService.FournisseurCle = null;
            }
        }

        [Test]
        [Microsoft.VisualStudio.TestTools.UnitTesting.TestMethod]
        public void AllerRetour_EntreDeuxPostes()
        {
            var a = PosteAvecDonnees();
            string zip = Path.Combine(NouveauDossier(), "sauvegarde.zip");
            var r = a.Service().Sauvegarder(zip, MotDePasse, "test");
            Assert.That(r.Bases, Is.EqualTo(1));
            Assert.That(r.Avertissements, Is.Empty, string.Join(" | ", r.Avertissements));

            // L'archive ne montre que le manifeste, le coffre et la base, chiffrée ; ni clé du poste,
            // ni mot de passe, ni comptes ou registre en clair, ni nom d'entreprise.
            using (var archive = ZipFile.OpenRead(zip))
            {
                Assert.That(archive.Entries.Select(e => e.FullName.Replace('\\', '/')),
                    Is.EquivalentTo(new[] { SauvegardeService.NomManifeste, SauvegardeService.NomCoffre, "Dossiers/dossier_a.db" }));
                var debut = new byte[16];
                using (var s = archive.Entries.Single(e => e.Name == "dossier_a.db").Open()) s.Read(debut, 0, 16);
                Assert.That(EnteteEnClair(debut), Is.False);
                using (var s = new StreamReader(archive.GetEntry(SauvegardeService.NomManifeste).Open()))
                {
                    string manifeste = s.ReadToEnd();
                    Assert.That(manifeste, Does.Contain("Format=2"));
                    Assert.That(manifeste, Does.Not.Contain("ZOODO"));
                    Assert.That(manifeste, Does.Not.Contain(MotDePasse));
                }
            }

            // Poste B : autre clé des bases. Sans mot de passe, la sauvegarde ne se lit pas.
            var b = NouveauPoste();
            Assert.That(b.Cle, Is.Not.EqualTo(a.Cle));
            byte[] cleB = File.ReadAllBytes(CleDonnees.Chemin(b.Dossier));
            var sansMotDePasse = b.Service().Analyser(zip);
            Assert.That(sansMotDePasse.MotDePasseRequis, Is.True);
            Assert.That(SauvegardeService.Obstacle(sansMotDePasse), Does.Contain("mot de passe"));

            var contenu = b.Service().Analyser(zip, MotDePasse);
            Assert.That(SauvegardeService.Obstacle(contenu), Is.Null);
            Assert.That(contenu.Dossiers.Single().RaisonSociale, Is.EqualTo("ENTREPRISE ZOODO"));
            Assert.That(contenu.Dossiers.Single().Niveau, Is.EqualTo(SQLiteService.SchemaCible));

            b.Service().Restaurer(zip, MotDePasse, Path.Combine(NouveauDossier(), "securite.zip"), null, "test");

            // La base est rechiffrée avec la clé de B : lisible par B, plus par la clé de A.
            Assert.That(SQLiteService.LireVersionFichier(b.Base, b.Cle), Is.EqualTo(SQLiteService.SchemaCible));
            Assert.That(SQLiteService.LireVersionFichier(b.Base, a.Cle), Is.EqualTo(-1));
            Assert.That(SQLiteService.LireVersionFichier(b.Base), Is.EqualTo(-1));
            Assert.That(PrenomLu(b), Is.EqualTo("AWA"));

            // Les comptes sont relisibles par le poste B (DPAPI local) ; le registre est revenu.
            var comptes = new UserAccountService(b.Dossier).LoadUsersAsync().GetAwaiter().GetResult();
            Assert.That(comptes.Single().Username, Is.EqualTo("admin.zoodo"));
            Assert.That(Encoding.UTF8.GetString(File.ReadAllBytes(Path.Combine(b.Dossier, "users.json"))), Does.Not.Contain("admin.zoodo"));
            Assert.That(File.ReadAllText(Path.Combine(b.Dossier, "dossiers.json")), Does.Contain("ENTREPRISE ZOODO"));

            // Ni la clé ni le mot de passe du poste A n'ont été apportés.
            Assert.That(File.ReadAllBytes(CleDonnees.Chemin(b.Dossier)), Is.EqualTo(cleB));
            Assert.That(MotDePasseSauvegarde.Existe(b.Dossier), Is.False);
        }

        [Test]
        [Microsoft.VisualStudio.TestTools.UnitTesting.TestMethod]
        public void MauvaisMotDePasse_Refuse_RienNEstModifie()
        {
            var a = PosteAvecDonnees();
            string zip = Path.Combine(NouveauDossier(), "sauvegarde.zip");
            a.Service().Sauvegarder(zip, MotDePasse, "test");

            var b = PosteAvecDonnees();
            byte[] baseAvant = File.ReadAllBytes(b.Base);
            byte[] comptesAvant = File.ReadAllBytes(Path.Combine(b.Dossier, "users.json"));
            string securite = Path.Combine(NouveauDossier(), "securite.zip");

            Assert.Throws<MotDePasseSauvegardeIncorrectException>(() => b.Service().Analyser(zip, "Pas-le-bon-1"));
            Assert.Throws<MotDePasseSauvegardeIncorrectException>(() => b.Service().Restaurer(zip, "Pas-le-bon-1", securite, MotDePasse, "test"));
            Assert.Throws<ArgumentException>(() => b.Service().Restaurer(zip, "", securite, MotDePasse, "test"));

            Assert.That(File.ReadAllBytes(b.Base), Is.EqualTo(baseAvant));
            Assert.That(File.ReadAllBytes(Path.Combine(b.Dossier, "users.json")), Is.EqualTo(comptesAvant));
            Assert.That(File.Exists(securite), Is.False);
        }

        [Test]
        [Microsoft.VisualStudio.TestTools.UnitTesting.TestMethod]
        public void CoffreAltere_Refuse()
        {
            var a = PosteAvecDonnees();
            string zip = Path.Combine(NouveauDossier(), "sauvegarde.zip");
            a.Service().Sauvegarder(zip, MotDePasse, "test");

            using (var archive = ZipFile.Open(zip, ZipArchiveMode.Update))
            {
                var entree = archive.GetEntry(SauvegardeService.NomCoffre);
                byte[] octets;
                using (var s = entree.Open())
                using (var m = new MemoryStream()) { s.CopyTo(m); octets = m.ToArray(); }
                octets[octets.Length / 2] ^= 0x01;
                entree.Delete();
                using (var s = archive.CreateEntry(SauvegardeService.NomCoffre).Open()) s.Write(octets, 0, octets.Length);
            }

            Assert.Throws<MotDePasseSauvegardeIncorrectException>(() => NouveauPoste().Service().Analyser(zip, MotDePasse));
        }

        [Test]
        [Microsoft.VisualStudio.TestTools.UnitTesting.TestMethod]
        public void SauvegardeDeSecurite_ProtegeeParLeMotDePasseDuPoste()
        {
            var a = PosteAvecDonnees();
            string zip = Path.Combine(NouveauDossier(), "sauvegarde.zip");
            a.Service().Sauvegarder(zip, MotDePasse, "test");

            var b = PosteAvecDonnees();
            string securite = Path.Combine(NouveauDossier(), "securite.zip");
            b.Service().Restaurer(zip, MotDePasse, securite, "Autre-mot-de-passe-B", "test");

            Assert.That(File.Exists(securite), Is.True);
            Assert.Throws<MotDePasseSauvegardeIncorrectException>(() => b.Service().Analyser(securite, MotDePasse));
            Assert.That(SauvegardeService.Obstacle(b.Service().Analyser(securite, "Autre-mot-de-passe-B")), Is.Null);
        }

        [Test]
        [Microsoft.VisualStudio.TestTools.UnitTesting.TestMethod]
        public void MotDePasseDuPoste_ConserveChiffre()
        {
            string dir = NouveauDossier();
            Assert.That(MotDePasseSauvegarde.Lire(dir), Is.Null);
            Assert.That(MotDePasseSauvegarde.Valider("court"), Is.Not.Null);
            Assert.Throws<ArgumentException>(() => MotDePasseSauvegarde.Definir(dir, "court"));

            MotDePasseSauvegarde.Definir(dir, MotDePasse);
            Assert.That(MotDePasseSauvegarde.Lire(dir), Is.EqualTo(MotDePasse));
            Assert.That(Encoding.UTF8.GetString(File.ReadAllBytes(MotDePasseSauvegarde.Chemin(dir))), Does.Not.Contain(MotDePasse));

            MotDePasseSauvegarde.Definir(dir, "Nouveau-mot-de-passe");
            Assert.That(MotDePasseSauvegarde.Lire(dir), Is.EqualTo("Nouveau-mot-de-passe"));

            // Fichier abîmé (ou d'un autre profil Windows) : considéré comme non défini, jamais exposé.
            File.WriteAllBytes(MotDePasseSauvegarde.Chemin(dir), new byte[] { 1, 2, 3 });
            Assert.That(MotDePasseSauvegarde.Lire(dir), Is.Null);
        }

        [Test]
        [Microsoft.VisualStudio.TestTools.UnitTesting.TestMethod]
        public void Sauvegarde_SansMotDePasseValable_Refusee()
        {
            var a = PosteAvecDonnees();
            string zip = Path.Combine(NouveauDossier(), "sauvegarde.zip");
            Assert.Throws<ArgumentException>(() => a.Service().Sauvegarder(zip, null, "test"));
            Assert.Throws<ArgumentException>(() => a.Service().Sauvegarder(zip, "court", "test"));
            Assert.That(File.Exists(zip), Is.False);
        }
    }
}
