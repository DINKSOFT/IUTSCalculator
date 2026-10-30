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
        private const string MotDePasse = "Sauvegarde-2026";

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

        /// <summary>Base en clair, telle que l'ont laissée les versions précédentes, avec un salarié à charge.</summary>
        private static string BaseEnClair(string dir, string relatif, string prenom)
        {
            string db = Path.Combine(dir, relatif);
            Directory.CreateDirectory(Path.GetDirectoryName(db));
            new PersonneAChargeService(new SQLiteService(db)).EnregistrerToutAsync("M100", new[] { Enfant(prenom) }).GetAwaiter().GetResult();
            Assert.That(EnteteEnClair(db), Is.True);
            return db;
        }

        private static string PrenomLu(string db, string cle)
        {
            SQLiteService.FournisseurCle = () => cle;
            try
            {
                return new PersonneAChargeService(new SQLiteService(db)).ChargerAsync("M100").GetAwaiter().GetResult().Single().Prenom;
            }
            finally
            {
                SQLiteService.FournisseurCle = null;
            }
        }

        [Test]
        [Microsoft.VisualStudio.TestTools.UnitTesting.TestMethod]
        public void BasesEnClair_ChiffreesAuDemarrage()
        {
            string dir = NouveauDossier();
            string dossier = BaseEnClair(dir, Path.Combine("Dossiers", "dossier_1.db"), "AWA");
            string heritee = BaseEnClair(dir, "iuts_data.db", "ISSA");
            string copieMigration = dossier + ".avant-v37.bak";
            File.Copy(dossier, copieMigration);
            File.WriteAllText(Path.Combine(dir, "dossiers.json"), "[]");

            string cle = ChiffrementBases.ObtenirCleDuPoste(dir);
            var resultat = ChiffrementBases.ChiffrerDossierDonnees(dir, cle);

            Assert.That(resultat.Echecs, Is.Empty);
            Assert.That(resultat.Chiffrees, Is.EquivalentTo(new[] { dossier, heritee, copieMigration }));
            foreach (var db in new[] { dossier, heritee, copieMigration })
            {
                Assert.That(EnteteEnClair(db), Is.False, db);
                Assert.That(SQLiteService.LireVersionFichier(db, cle), Is.EqualTo(SQLiteService.SchemaCible), db);
                Assert.That(Encoding.UTF8.GetString(File.ReadAllBytes(db)), Does.Not.Contain("OUEDRAOGO"), db);
            }
            // Sans la clé, plus rien n'est lisible ; avec, les données sont intactes.
            Assert.That(SQLiteService.LireVersionFichier(dossier), Is.EqualTo(-1));
            Assert.That(PrenomLu(dossier, cle), Is.EqualTo("AWA"));
            Assert.That(PrenomLu(heritee, cle), Is.EqualTo("ISSA"));
            // Ni copie de travail ni copie de sécurité en clair laissées derrière.
            Assert.That(Directory.GetFiles(dir, "*" + ChiffrementBases.SuffixeCopie, SearchOption.AllDirectories), Is.Empty);
            Assert.That(Directory.GetFiles(dir, "*" + ChiffrementBases.SuffixeSecurite, SearchOption.AllDirectories), Is.Empty);
            Assert.That(File.ReadAllText(Path.Combine(dir, "dossiers.json")), Is.EqualTo("[]"));

            // Deuxième lancement : rien à refaire.
            Assert.That(ChiffrementBases.ChiffrerDossierDonnees(dir, ChiffrementBases.ObtenirCleDuPoste(dir)).Chiffrees, Is.Empty);
        }

        /// <summary>Chiffre une base en clair comme les premières versions : clé passée en hexkey (dérivée par PBKDF2).</summary>
        private static void ChiffrerAncienFormat(string db, string cle)
        {
            IUTSCalculator.Helpers.SqliteNatif.Initialiser();
            using (var c = new Microsoft.Data.Sqlite.SqliteConnection("Data Source=" + db + ";Pooling=False"))
            {
                c.Open();
                using (var cmd = c.CreateCommand())
                {
                    cmd.CommandText = "PRAGMA cipher = 'sqlcipher'; PRAGMA legacy = 4; PRAGMA hexrekey = '" + cle + "';";
                    cmd.ExecuteNonQuery();
                }
            }
            Assert.That(EnteteEnClair(db), Is.False);
        }

        [Test]
        [Microsoft.VisualStudio.TestTools.UnitTesting.TestMethod]
        public void AncienFormat_ConvertiALaCleBrute()
        {
            string dir = NouveauDossier();
            string cle = CleDonnees.ObtenirOuCreer(dir);
            string ancienne = BaseEnClair(dir, "ancienne.db", "AWA");
            ChiffrerAncienFormat(ancienne, cle);
            // Base d'un autre poste (autre clé) : ni convertie, ni signalée en échec.
            string etrangere = BaseEnClair(dir, "etrangere.db", "ISSA");
            ChiffrerAncienFormat(etrangere, CleDonnees.ObtenirOuCreer(NouveauDossier()));
            byte[] etrangereAvant = File.ReadAllBytes(etrangere);

            // Avant conversion, l'ancien format reste lisible (restauration d'anciennes sauvegardes).
            Assert.That(SQLiteService.LireVersionFichier(ancienne, cle), Is.EqualTo(SQLiteService.SchemaCible));

            var avancement = new AvancementsReleves();
            var resultat = ChiffrementBases.ChiffrerDossierDonnees(dir, cle, avancement);

            Assert.That(resultat.Echecs, Is.Empty);
            Assert.That(resultat.Chiffrees, Is.Empty);
            Assert.That(resultat.Converties, Is.EqualTo(new[] { ancienne }));
            Assert.That(avancement.Releves.Select(a => a.Total), Is.All.EqualTo(2));
            Assert.That(PrenomLu(ancienne, cle), Is.EqualTo("AWA"));
            Assert.That(Encoding.UTF8.GetString(File.ReadAllBytes(ancienne)), Does.Not.Contain("OUEDRAOGO"));
            Assert.That(SQLiteService.LireVersionFichier(ancienne), Is.EqualTo(-1), "illisible sans clé");
            Assert.That(File.ReadAllBytes(etrangere), Is.EqualTo(etrangereAvant));
            Assert.That(Directory.GetFiles(dir, "*" + ChiffrementBases.SuffixeCopie, SearchOption.AllDirectories), Is.Empty);
            Assert.That(Directory.GetFiles(dir, "*" + ChiffrementBases.SuffixeSecurite, SearchOption.AllDirectories), Is.Empty);

            // Deuxième lancement : plus rien à convertir.
            Assert.That(ChiffrementBases.ChiffrerDossierDonnees(dir, cle).Converties, Is.Empty);
        }

        [Test]
        [Microsoft.VisualStudio.TestTools.UnitTesting.TestMethod]
        public void AncienFormat_ConvertiALOuvertureDuDossier()
        {
            string dir = NouveauDossier();
            string cle = CleDonnees.ObtenirOuCreer(dir);
            string db = BaseEnClair(dir, "dossier.db", "AWA");
            ChiffrerAncienFormat(db, cle);

            // Ouvrir le dossier (SQLiteService) le convertit au passage, comme une base restée en clair.
            Assert.That(PrenomLu(db, cle), Is.EqualTo("AWA"));
            Assert.That(ChiffrementBases.ConvertirSiAncienFormat(db, cle), Is.False, "déjà converti");
        }

        /// <summary>Relève les avancements sur le fil appelant (Progress&lt;T&gt; les différerait).</summary>
        private class AvancementsReleves : IProgress<AvancementChiffrement>
        {
            public System.Collections.Generic.List<AvancementChiffrement> Releves { get; } = new System.Collections.Generic.List<AvancementChiffrement>();
            public void Report(AvancementChiffrement value) => Releves.Add(value);
        }

        [Test]
        [Microsoft.VisualStudio.TestTools.UnitTesting.TestMethod]
        public void Avancement_NeCompteQueLesBasesEnClair()
        {
            string dir = NouveauDossier();
            string cle = CleDonnees.ObtenirOuCreer(dir);
            string premiere = BaseEnClair(dir, "a.db", "AWA");
            string seconde = BaseEnClair(dir, "b.db", "ISSA");
            ChiffrementBases.ChiffrerSiEnClair(premiere, cle);

            var avancement = new AvancementsReleves();
            ChiffrementBases.ChiffrerDossierDonnees(dir, cle, avancement);

            // Une seule base reste à chiffrer : un relevé avant elle, un à la fin.
            Assert.That(avancement.Releves.Select(a => a.Total), Is.All.EqualTo(1));
            Assert.That(avancement.Releves.Select(a => a.Traitees), Is.EqualTo(new[] { 0, 1 }));
            Assert.That(avancement.Releves.Select(a => a.Base), Is.EqualTo(new[] { seconde, null }));

            // Rien à faire : un seul relevé, à zéro sur zéro.
            var rien = new AvancementsReleves();
            ChiffrementBases.ChiffrerDossierDonnees(dir, cle, rien);
            Assert.That(rien.Releves.Select(a => a.Total), Is.EqualTo(new[] { 0 }));
        }

        [Test]
        [Microsoft.VisualStudio.TestTools.UnitTesting.TestMethod]
        public void BaseDejaChiffree_LaisseeTelleQuelle()
        {
            string dir = NouveauDossier();
            string cle = CleDonnees.ObtenirOuCreer(dir);
            string db = Path.Combine(dir, "Dossiers", "dossier_2.db");
            Directory.CreateDirectory(Path.GetDirectoryName(db));
            SQLiteService.FournisseurCle = () => cle;
            try { new SQLiteService(db); }
            finally { SQLiteService.FournisseurCle = null; }
            byte[] avant = File.ReadAllBytes(db);
            DateTime modifie = File.GetLastWriteTimeUtc(db);

            var resultat = ChiffrementBases.ChiffrerDossierDonnees(dir, ChiffrementBases.ObtenirCleDuPoste(dir));

            Assert.That(resultat.Chiffrees, Is.Empty);
            Assert.That(resultat.Echecs, Is.Empty);
            Assert.That(File.ReadAllBytes(db), Is.EqualTo(avant));
            Assert.That(File.GetLastWriteTimeUtc(db), Is.EqualTo(modifie));
        }

        [Test]
        [Microsoft.VisualStudio.TestTools.UnitTesting.TestMethod]
        public void CleIllisible_AucuneBaseTouchee()
        {
            string dir = NouveauDossier();
            string db = BaseEnClair(dir, Path.Combine("Dossiers", "dossier_3.db"), "FATI");
            byte[] avant = File.ReadAllBytes(db);
            File.WriteAllBytes(CleDonnees.Chemin(dir), new byte[] { 9, 9, 9 });

            Assert.Throws<CleDonneesIllisibleException>(() => ChiffrementBases.ObtenirCleDuPoste(dir));
            Assert.That(File.ReadAllBytes(CleDonnees.Chemin(dir)), Is.EqualTo(new byte[] { 9, 9, 9 }));
            Assert.That(File.ReadAllBytes(db), Is.EqualTo(avant));
        }

        [Test]
        [Microsoft.VisualStudio.TestTools.UnitTesting.TestMethod]
        public void CleDisparue_AvecBasesChiffrees_PasDeNouvelleCle()
        {
            string dir = NouveauDossier();
            BaseEnClair(dir, "iuts_data.db", "ALI");
            ChiffrementBases.ChiffrerDossierDonnees(dir, ChiffrementBases.ObtenirCleDuPoste(dir));
            File.Delete(CleDonnees.Chemin(dir));

            Assert.Throws<CleDonneesAbsenteException>(() => ChiffrementBases.ObtenirCleDuPoste(dir));
            Assert.That(CleDonnees.Existe(dir), Is.False);
        }

        [Test]
        [Microsoft.VisualStudio.TestTools.UnitTesting.TestMethod]
        public void BaseEndommagee_ResteeIntacte_EtSignalee()
        {
            string dir = NouveauDossier();
            string saine = BaseEnClair(dir, Path.Combine("Dossiers", "dossier_4.db"), "AWA");
            string abimee = Path.Combine(dir, "Dossiers", "dossier_5.db");
            var contenu = Encoding.ASCII.GetBytes("SQLite format 3\0").Concat(Enumerable.Repeat((byte)0xAB, 4096)).ToArray();
            File.WriteAllBytes(abimee, contenu);

            var resultat = ChiffrementBases.ChiffrerDossierDonnees(dir, ChiffrementBases.ObtenirCleDuPoste(dir));

            Assert.That(resultat.Chiffrees, Is.EquivalentTo(new[] { saine }));
            Assert.That(resultat.Echecs, Has.Count.EqualTo(1));
            Assert.That(resultat.Echecs[0], Does.Contain("dossier_5.db"));
            Assert.That(File.ReadAllBytes(abimee), Is.EqualTo(contenu));
            Assert.That(Directory.GetFiles(dir, "*.tmp", SearchOption.AllDirectories), Is.Empty);
        }

        [Test]
        [Microsoft.VisualStudio.TestTools.UnitTesting.TestMethod]
        public void BaseEnClair_OuverteAvecLaCle_ChiffreeAuPassage()
        {
            // Base restaurée d'une ancienne sauvegarde après le démarrage, par exemple.
            string dir = NouveauDossier();
            string db = BaseEnClair(dir, "dossier.db", "ISSA");
            string cle = CleDonnees.ObtenirOuCreer(dir);

            Assert.That(PrenomLu(db, cle), Is.EqualTo("ISSA"));
            Assert.That(EnteteEnClair(db), Is.False);
        }

        [Test]
        [Microsoft.VisualStudio.TestTools.UnitTesting.TestMethod]
        public void Sauvegarde_AvecCleActive_BasesCopieesChiffrees()
        {
            string dir = NouveauDossier();
            BaseEnClair(dir, Path.Combine("Dossiers", "dossier_6.db"), "AWA");
            File.WriteAllText(Path.Combine(dir, "dossiers.json"), "[{\"RaisonSociale\":\"SOCIETE TEST\",\"DbFileName\":\"Dossiers\\\\dossier_6.db\"}]");
            string cle = ChiffrementBases.ObtenirCleDuPoste(dir);
            ChiffrementBases.ChiffrerDossierDonnees(dir, cle);

            string zip = Path.Combine(NouveauDossier(), "sauvegarde.zip");
            SQLiteService.FournisseurCle = () => cle;
            try
            {
                var service = new SauvegardeService(dir);
                var resultat = service.Sauvegarder(zip, MotDePasse, "test");
                Assert.That(resultat.Avertissements, Is.Empty, string.Join(" | ", resultat.Avertissements));

                var contenu = service.Analyser(zip, MotDePasse);
                Assert.That(contenu.Dossiers.Single().Niveau, Is.EqualTo(SQLiteService.SchemaCible));
                Assert.That(SauvegardeService.Obstacle(contenu), Is.Null);
            }
            finally
            {
                SQLiteService.FournisseurCle = null;
            }

            // Dans l'archive, la base est chiffrée, et la clé du poste n'y figure pas.
            using (var archive = System.IO.Compression.ZipFile.OpenRead(zip))
            {
                Assert.That(archive.Entries.Any(e => e.Name == CleDonnees.NomFichier), Is.False);
                var entree = archive.Entries.Single(e => e.Name == "dossier_6.db");
                var b = new byte[16];
                using (var s = entree.Open()) s.Read(b, 0, 16);
                Assert.That(Encoding.ASCII.GetString(b).StartsWith("SQLite format 3"), Is.False);
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
