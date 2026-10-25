using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using IUTSCalculator.Models;
using IUTSCalculator.Services;

namespace IUTSCalculator.Tests
{
    /// <summary>
    /// Écritures de paie (défaut 20) : comptes paramétrables, longueur uniforme des comptes
    /// généraux, export au format d'import Sage 100c Comptabilité du client.
    /// </summary>
    [TestFixture]
    [Microsoft.VisualStudio.TestTools.UnitTesting.TestClass]
    public class EcriturePaieTests
    {
        private static PieceComptable PieceExemple()
        {
            var fin = new DateTime(2026, 9, 30);
            var piece = new PieceComptable
            {
                CodeJournal = "PAIE",
                NumeroPiece = "PAIE-202609",
                Reference = "PAIE-202609",
                DatePiece = fin,
                DebutPeriode = new DateTime(2026, 9, 1),
                FinPeriode = fin
            };
            piece.Lignes.Add(new LigneEcritureComptable { CompteGeneral = "661100", LibelleSage = "Salaires de base 09/2026", Debit = 350000m });
            piece.Lignes.Add(new LigneEcritureComptable { CompteGeneral = "664100", LibelleSage = "CNSS patronale 09/2026", Debit = 66400m });
            piece.Lignes.Add(new LigneEcritureComptable { CompteGeneral = "431100", LibelleSage = "CNSS sal. + pat. 09/2026", Credit = 89225m });
            piece.Lignes.Add(new LigneEcritureComptable { CompteGeneral = "447200", LibelleSage = "IUTS retenu 09/2026", Credit = 17300m });
            piece.Lignes.Add(new LigneEcritureComptable { CompteGeneral = "422000", LibelleSage = "Salaires nets 09/2026", Credit = 309875m });
            return piece;
        }

        [Test]
        [Microsoft.VisualStudio.TestTools.UnitTesting.TestMethod]
        public void ParametresComptables_ValeursInitiales_SontLesComptesAutrefoisEcritsEnDur()
        {
            // Tant que l'utilisateur ne modifie rien, l'écriture garde exactement ses comptes.
            var p = new ParametresComptables();
            Assert.That(p.CompteChargesCnss, Is.EqualTo("664100"));
            Assert.That(p.CompteChargeTpa, Is.EqualTo("641300"));
            Assert.That(p.CompteCnss, Is.EqualTo("431100"));
            Assert.That(p.CompteIuts, Is.EqualTo("447200"));
            Assert.That(p.CompteTpa, Is.EqualTo("447300"));
            Assert.That(p.CompteRetenueSpeciale, Is.EqualTo("447800"));
            Assert.That(p.CompteNetAPayer, Is.EqualTo("422000"));
            Assert.That(p.CompteRetenuesDiverses, Is.EqualTo("422100"));
            Assert.That(p.CompteSalaireBase, Is.EqualTo("661100"));
            Assert.That(p.CompteGainsVariables, Is.EqualTo("661800"));
            Assert.That(p.CompteIndemniteLogement, Is.EqualTo("663100"));
            Assert.That(p.CompteIndemniteSujetion, Is.EqualTo("663400"));
            Assert.That(p.LongueurComptes, Is.EqualTo(0), "0 = longueur automatique");
            Assert.That(p.NumeroPiece(new DateTime(2026, 9, 30)), Is.EqualTo("PAIE-202609"));
            Assert.That(p.Anomalies(new DateTime(2026, 9, 30)), Is.Empty);
        }

        [Test]
        [Microsoft.VisualStudio.TestTools.UnitTesting.TestMethod]
        public void ParametresComptables_DepuisBase_CleAbsenteOuCompteVide_GardeLaValeurInitiale()
        {
            var p = ParametresComptables.Depuis(new Dictionary<string, string>
            {
                { ParametresComptables.CleCompteIuts, "4471" },
                { ParametresComptables.CleCompteCnss, "  " },
                { "CleInconnue", "x" }
            });
            Assert.That(p.CompteIuts, Is.EqualTo("4471"));
            Assert.That(p.CompteCnss, Is.EqualTo("431100"));
            Assert.That(p.CompteNetAPayer, Is.EqualTo("422000"));
        }

        [Test]
        [Microsoft.VisualStudio.TestTools.UnitTesting.TestMethod]
        public void CompleterCompte_ZerosADroite_CompteTropLongConserve()
        {
            Assert.That(ParametresComptables.CompleterCompte("6611", 6), Is.EqualTo("661100"));
            Assert.That(ParametresComptables.CompleterCompte(" 42 21 ", 6), Is.EqualTo("422100"));
            Assert.That(ParametresComptables.CompleterCompte("422000", 6), Is.EqualTo("422000"));
            Assert.That(ParametresComptables.CompleterCompte("6611000", 6), Is.EqualTo("6611000"), "jamais tronqué");
            Assert.That(ParametresComptables.CompleterCompte("6611", 0), Is.EqualTo("6611"));
        }

        [Test]
        [Microsoft.VisualStudio.TestTools.UnitTesting.TestMethod]
        public void LongueurAutomatique_EstCelleDuPlusLongCompte()
        {
            var comptes = new List<string> { "6611", "4221", null, "", "422000" };
            Assert.That(ParametresComptables.LongueurAutomatique(comptes), Is.EqualTo(6));

            // Plan de paie livré (4 chiffres) + paramètres (6 chiffres) : sortie sur 6.
            var p = new ParametresComptables();
            Assert.That(ParametresComptables.LongueurAutomatique(p.Comptes().Concat(new[] { "6611", "4271" })), Is.EqualTo(6));
        }

        [Test]
        [Microsoft.VisualStudio.TestTools.UnitTesting.TestMethod]
        public void ParametresComptables_Anomalies_LimitesDeSage()
        {
            var p = new ParametresComptables();
            p.CodeJournal = "JOURNAL";                 // 7 > 6
            p.FormatPiece = "OD-PAIE-{AAAA}-{MM}";     // 15 > 13
            p.LongueurComptes = 20;
            p.CompteIuts = "";

            var anomalies = p.Anomalies(new DateTime(2026, 9, 30));
            Assert.That(anomalies.Any(a => a.Contains("code journal")), Is.True);
            Assert.That(anomalies.Any(a => a.Contains("13 caractères")), Is.True);
            Assert.That(anomalies.Any(a => a.Contains("longueur des comptes")), Is.True);
            Assert.That(anomalies.Any(a => a.Contains("IUTS")), Is.True);
        }

        [Test]
        [Microsoft.VisualStudio.TestTools.UnitTesting.TestMethod]
        public void AppliquerFormat_RemplaceAnneeEtMois()
        {
            var fin = new DateTime(2026, 3, 31);
            Assert.That(ParametresComptables.AppliquerFormat("PAIE-{AAAA}{MM}", fin), Is.EqualTo("PAIE-202603"));
            Assert.That(ParametresComptables.AppliquerFormat("SAL{aa}{mm}", fin), Is.EqualTo("SAL2603"));
        }

        [Test]
        [Microsoft.VisualStudio.TestTools.UnitTesting.TestMethod]
        public void TronquerIntitule_StrictementMoinsDe34Caracteres()
        {
            string longue = "Indemnité de départ à la retraite négociée selon la convention";
            string t = LigneImportSage.TronquerIntitule(longue);
            Assert.That(t.Length, Is.LessThan(34));
            Assert.That(longue.StartsWith(t), Is.True);
            Assert.That(t.EndsWith(" "), Is.False);

            string s = LigneImportSage.IntituleAvecSuffixe("Prime de rendement exceptionnelle du trimestre", "09/2026");
            Assert.That(s.Length, Is.LessThanOrEqualTo(33));
            Assert.That(s.EndsWith(" 09/2026"), Is.True, "la période n'est jamais coupée");

            Assert.That(LigneImportSage.TronquerIntitule("Prêt; avance"), Is.EqualTo("Prêt, avance"), "pas de point-virgule");
        }

        [Test]
        [Microsoft.VisualStudio.TestTools.UnitTesting.TestMethod]
        public void ImportSage_UneLigneParMontant_SensDC_TotauxEgaux()
        {
            var piece = PieceExemple();
            var lignes = EcriturePaieExportService.ConstruireImportSage(piece);

            Assert.That(lignes.Count, Is.EqualTo(5));
            Assert.That(lignes.All(l => l.Sens == "D" || l.Sens == "C"), Is.True);
            Assert.That(lignes.All(l => l.Date == "30/09/2026"), Is.True);
            Assert.That(lignes.All(l => l.JO_Num == "PAIE" && l.EC_Piece == "PAIE-202609"), Is.True);
            Assert.That(lignes.Where(l => l.Sens == "D").Sum(l => l.Montant),
                Is.EqualTo(lignes.Where(l => l.Sens == "C").Sum(l => l.Montant)));
            Assert.That(EcriturePaieExportService.ControlerImportSage(piece, lignes), Is.Empty);
        }

        [Test]
        [Microsoft.VisualStudio.TestTools.UnitTesting.TestMethod]
        public void ImportSage_MontantNegatif_PasseDansLAutreSens()
        {
            var piece = PieceExemple();
            piece.Lignes.Add(new LigneEcritureComptable { CompteGeneral = "661800", LibelleSage = "Régularisation 09/2026", Debit = -5000m });
            piece.Lignes.Add(new LigneEcritureComptable { CompteGeneral = "422000", LibelleSage = "Net 09/2026", Debit = 5000m });

            var lignes = EcriturePaieExportService.ConstruireImportSage(piece);
            var regul = lignes.Single(l => l.CG_Num == "661800");
            Assert.That(regul.Sens, Is.EqualTo("C"));
            Assert.That(regul.Montant, Is.EqualTo(5000m));
            Assert.That(EcriturePaieExportService.ControlerImportSage(piece, lignes), Is.Empty);
        }

        [Test]
        [Microsoft.VisualStudio.TestTools.UnitTesting.TestMethod]
        public void ImportSage_PieceDesequilibree_Refusee()
        {
            var piece = PieceExemple();
            piece.Lignes[0].Debit += 1m;

            var erreurs = EcriturePaieExportService.ControlerImportSage(piece, EcriturePaieExportService.ConstruireImportSage(piece));
            Assert.That(erreurs.Any(e => e.Contains("déséquilibrée")), Is.True);
            Assert.That(EcriturePaieExportService.ImportSagePossible(piece), Is.False);
            Assert.Throws<InvalidOperationException>(() =>
                new EcriturePaieExportService().ExporterSage(piece, System.IO.Path.Combine(System.IO.Path.GetTempPath(), "jamais_" + Guid.NewGuid().ToString("N") + ".txt")));
        }

        [Test]
        [Microsoft.VisualStudio.TestTools.UnitTesting.TestMethod]
        public void ContenuFichierSage_FormatDuClient()
        {
            var piece = PieceExemple();
            string contenu = EcriturePaieExportService.ContenuFichierSage(piece, EcriturePaieExportService.ConstruireImportSage(piece));
            var lignes = contenu.Split(new[] { "\r\n" }, StringSplitOptions.RemoveEmptyEntries);

            Assert.That(lignes[0], Is.EqualTo("JO_Num;EC_Reference;Date;EC_Piece;CG_Num;CT_Num;EC_Intitule;Sens;Montant"));
            Assert.That(lignes[1], Is.EqualTo("PAIE;PAIE-202609;30/09/2026;PAIE-202609;661100;;Salaires de base 09/2026;D;350000"),
                "montant sans séparateur de milliers, compte en texte");
            Assert.That(lignes.Length, Is.EqualTo(6));

            piece.EnTeteFichierSage = false;
            contenu = EcriturePaieExportService.ContenuFichierSage(piece, EcriturePaieExportService.ConstruireImportSage(piece));
            Assert.That(contenu.StartsWith("PAIE;"), Is.True);
            Assert.That(EcriturePaieExportService.EncodageSage.CodePage, Is.EqualTo(1252));
        }

        [Test]
        [Microsoft.VisualStudio.TestTools.UnitTesting.TestMethod]
        public void PieceComptable_CompteTropLong_Signale()
        {
            var piece = PieceExemple();
            piece.AvertissementsComptes.Add("Le compte 6611000 compte 7 caractères, plus que la longueur des comptes généraux (6)");
            Assert.That(piece.Anomalies().Any(a => a.Contains("6611000")), Is.True);
        }
    }
}
