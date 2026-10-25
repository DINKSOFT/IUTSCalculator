using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using IUTSCalculator.Models;
using IUTSCalculator.Services;

namespace IUTSCalculator.Tests
{
    /// <summary>
    /// Module RH-06 — Discipline & procédures : contrôles de procédure (moteur pur
    /// <see cref="ControleDiscipline"/>), sans base de données.
    ///
    /// Calendrier de référence : le lundi 19 octobre 2026. Jours ouvrables du lundi au
    /// samedi, jours fériés exclus.
    /// </summary>
    [TestFixture]
    [Microsoft.VisualStudio.TestTools.UnitTesting.TestClass]
    public class DisciplineTests
    {
        private static readonly DateTime Lundi19 = new DateTime(2026, 10, 19);
        private static readonly DateTime Plus_tard = new DateTime(2026, 12, 15);
        private static readonly ParametresDiscipline Prm = ParametresDiscipline.Defaut();
        private static readonly HashSet<DateTime> SansFerie = new HashSet<DateTime>();

        private static TypeSanction Type(string code)
        {
            var t = TypeSanction.CatalogueParDefaut().First(x => x.Code == code);
            t.Id = TypeSanction.CatalogueParDefaut().FindIndex(x => x.Code == code) + 1;
            return t;
        }

        private static ProcedureDisciplinaire Procedure(TypeSanction type)
        {
            return new ProcedureDisciplinaire
            {
                Id = 10,
                Reference = "DISC-2026-0010",
                EmployeeId = "M001",
                NomSalarie = "OUEDRAOGO Awa",
                DateFaits = new DateTime(2026, 10, 15),
                DateConnaissance = new DateTime(2026, 10, 16),
                CategorieFaits = "Absence injustifiée",
                DescriptionFaits = "Absence non justifiée les 14 et 15 octobre.",
                TypeSanctionId = type?.Id,
                TypeSanction = type
            };
        }

        private static List<AlerteDiscipline> Analyser(ProcedureDisciplinaire p, DateTime? aujourdHui = null,
            IEnumerable<ProcedureDisciplinaire> autres = null, IEnumerable<PieceDisciplinaire> pieces = null, ISet<DateTime> feries = null)
        {
            return ControleDiscipline.Analyser(p, p.TypeSanction, Prm, autres, pieces, feries ?? SansFerie, aujourdHui ?? Plus_tard);
        }

        private static bool Bloque(List<AlerteDiscipline> alertes, string fragment)
        {
            return alertes.Any(a => a.Niveau == NiveauAlerte.Bloquant && a.Message.IndexOf(fragment, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private static bool Signale(List<AlerteDiscipline> alertes, string fragment)
        {
            return alertes.Any(a => a.Niveau == NiveauAlerte.Avertissement && a.Message.IndexOf(fragment, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        // ── Calendrier ───────────────────────────────────────────────────

        [Test]
        [Microsoft.VisualStudio.TestTools.UnitTesting.TestMethod]
        public void JoursOuvrables_DimancheEtFeriesExclus()
        {
            Assert.That(ControleDiscipline.AjouterJoursOuvrables(Lundi19, 3, SansFerie), Is.EqualTo(new DateTime(2026, 10, 22)));
            // Vendredi 23 + 3 : samedi 24, (dimanche 25 exclu), lundi 26, mardi 27.
            Assert.That(ControleDiscipline.AjouterJoursOuvrables(new DateTime(2026, 10, 23), 3, SansFerie), Is.EqualTo(new DateTime(2026, 10, 27)));
            var feries = new HashSet<DateTime> { new DateTime(2026, 10, 26) };
            Assert.That(ControleDiscipline.AjouterJoursOuvrables(new DateTime(2026, 10, 23), 3, feries), Is.EqualTo(new DateTime(2026, 10, 28)));

            Assert.That(ControleDiscipline.JoursOuvrablesEntre(Lundi19, new DateTime(2026, 10, 26), SansFerie), Is.EqualTo(5));
            Assert.That(ControleDiscipline.PremiereDateEntretien(Lundi19, Prm, SansFerie), Is.EqualTo(new DateTime(2026, 10, 26)));
        }

        // ── Droits de la défense ─────────────────────────────────────────

        [Test]
        [Microsoft.VisualStudio.TestTools.UnitTesting.TestMethod]
        public void Decision_AvantFinDuDelaiDeReponse_Bloquee()
        {
            var p = Procedure(Type("AVT"));
            p.DateDemandeExplication = Lundi19;              // réponse attendue jusqu'au jeudi 22
            p.DateDecision = new DateTime(2026, 10, 22);
            Assert.That(Bloque(Analyser(p), "délai de réponse"), Is.True);

            p.DateDecision = new DateTime(2026, 10, 23);
            Assert.That(ControleDiscipline.PremierBlocage(Analyser(p)), Is.Null);

            // Une réponse reçue avant l'échéance permet de décider aussitôt après.
            p.DateReponse = new DateTime(2026, 10, 20);
            p.DateDecision = new DateTime(2026, 10, 21);
            Assert.That(ControleDiscipline.PremierBlocage(Analyser(p)), Is.Null);
        }

        [Test]
        [Microsoft.VisualStudio.TestTools.UnitTesting.TestMethod]
        public void Decision_SansDemandeDExplication_Bloquee()
        {
            var p = Procedure(Type("AVT"));
            p.DateDecision = Lundi19;
            Assert.That(Bloque(Analyser(p), "demande d'explication"), Is.True);
        }

        [Test]
        [Microsoft.VisualStudio.TestTools.UnitTesting.TestMethod]
        public void Licenciement_SansEntretienNiMotifs_Bloque()
        {
            var p = Procedure(Type("LIC"));
            p.DateDemandeExplication = Lundi19;
            p.DateReponse = new DateTime(2026, 10, 20);
            p.DateDecision = new DateTime(2026, 11, 2);
            var a = Analyser(p);
            Assert.That(Bloque(a, "entretien préalable"), Is.True);
            Assert.That(Bloque(a, "motivé par écrit"), Is.True);

            p.DateConvocation = new DateTime(2026, 10, 21);
            p.DateEntretien = new DateTime(2026, 10, 28);
            p.MotifsDecision = "Absences injustifiées répétées malgré un avertissement.";
            Assert.That(ControleDiscipline.PremierBlocage(Analyser(p)), Is.Null);
        }

        [Test]
        [Microsoft.VisualStudio.TestTools.UnitTesting.TestMethod]
        public void Convocation_TropProcheDeLEntretien_Signalee()
        {
            var p = Procedure(Type("LIC"));
            p.DateConvocation = Lundi19;
            p.DateEntretien = new DateTime(2026, 10, 23);    // 3 jours ouvrables pleins seulement
            var a = Analyser(p, Lundi19);
            Assert.That(Signale(a, "jour(s) ouvrable(s) seulement"), Is.True);
            Assert.That(ControleDiscipline.PremierBlocage(a), Is.Null);

            p.DateEntretien = new DateTime(2026, 10, 26);
            Assert.That(Signale(Analyser(p, Lundi19), "ouvrable(s) seulement"), Is.False);
        }

        // ── Règles du Code du travail ────────────────────────────────────

        [Test]
        [Microsoft.VisualStudio.TestTools.UnitTesting.TestMethod]
        public void SalarieProtege_LicenciementSansAutorisation_Bloque()
        {
            var p = Procedure(Type("LIC"));
            p.SalarieProtege = true;
            p.MandatProtege = "Délégué du personnel";
            p.DateDemandeExplication = Lundi19;
            p.DateReponse = Lundi19;
            p.DateConvocation = Lundi19;
            p.DateEntretien = new DateTime(2026, 10, 26);
            p.MotifsDecision = "Motifs.";
            p.DateDecision = new DateTime(2026, 11, 20);
            Assert.That(Bloque(Analyser(p), "inspecteur du travail"), Is.True);

            p.DecisionInspection = DecisionInspection.Accordee;
            p.DateDecisionInspection = new DateTime(2026, 11, 16);
            Assert.That(ControleDiscipline.PremierBlocage(Analyser(p)), Is.Null);
            Assert.That(ControleDiscipline.PiecesExigees(p, p.TypeSanction, null).Any(x => x.Type == TypePiece.AutorisationInspection), Is.True);
        }

        [Test]
        [Microsoft.VisualStudio.TestTools.UnitTesting.TestMethod]
        public void FauteLourde_ExigeUneFauteQualifieeLourde()
        {
            var p = Procedure(Type("LFL"));
            p.DateDemandeExplication = Lundi19;
            p.DateReponse = Lundi19;
            p.DateConvocation = Lundi19;
            p.DateEntretien = new DateTime(2026, 10, 26);
            p.MotifsDecision = "Vol de matériel constaté.";
            p.DateDecision = new DateTime(2026, 10, 27);
            p.Gravite = GraviteFaute.Serieuse;
            Assert.That(Bloque(Analyser(p), "faute qualifiée de lourde"), Is.True);

            p.Gravite = GraviteFaute.Lourde;
            Assert.That(ControleDiscipline.PremierBlocage(Analyser(p)), Is.Null);
            Assert.That(p.TypeSanction.MotifDepartAssocie, Is.EqualTo(MotifDepart.LicenciementFauteLourde));
            Assert.That(Type("LIC").MotifDepartAssocie, Is.EqualTo(MotifDepart.LicenciementPersonnel));
            Assert.That(Type("MAP").MotifDepartAssocie, Is.Null);
        }

        [Test]
        [Microsoft.VisualStudio.TestTools.UnitTesting.TestMethod]
        public void MiseAPied_DureeMaximaleEtDateDeDebut()
        {
            var p = Procedure(Type("MAP"));
            p.DateDemandeExplication = Lundi19;
            p.DateReponse = Lundi19;
            p.DateDecision = new DateTime(2026, 10, 21);
            p.DateNotification = new DateTime(2026, 10, 22);
            p.MotifsDecision = "Absence injustifiée.";
            p.MiseAPiedDebut = new DateTime(2026, 10, 26);
            p.MiseAPiedFin = new DateTime(2026, 11, 5);      // 10 jours ouvrables
            p.MiseAPiedJours = ControleDiscipline.JoursMiseAPiedProposes(p.MiseAPiedDebut.Value, p.MiseAPiedFin.Value);
            Assert.That(p.MiseAPiedJours, Is.EqualTo(10m));
            Assert.That(Bloque(Analyser(p), "maximum à 8 jours"), Is.True);

            p.MiseAPiedFin = new DateTime(2026, 11, 3);
            p.MiseAPiedJours = 8m;
            Assert.That(ControleDiscipline.PremierBlocage(Analyser(p)), Is.Null);

            // Une mise à pied ne commence pas avant d'avoir été notifiée.
            p.MiseAPiedDebut = new DateTime(2026, 10, 21);
            p.MiseAPiedFin = new DateTime(2026, 10, 24);
            p.MiseAPiedJours = 4m;
            Assert.That(Bloque(Analyser(p), "avant d'avoir été notifiée"), Is.True);

            // Plus de jours retenus que la période n'en compte.
            p.MiseAPiedDebut = new DateTime(2026, 10, 26);
            p.MiseAPiedFin = new DateTime(2026, 10, 27);
            p.MiseAPiedJours = 3m;
            Assert.That(Bloque(Analyser(p), "compte que"), Is.True);
        }

        [Test]
        [Microsoft.VisualStudio.TestTools.UnitTesting.TestMethod]
        public void DatesDansLeDesordre_Bloquees()
        {
            var p = Procedure(Type("AVT"));
            p.DateConnaissance = new DateTime(2026, 10, 10);   // avant les faits du 15
            Assert.That(Bloque(Analyser(p), "précède la date des faits"), Is.True);

            p = Procedure(Type("AVT"));
            p.DateNotification = Lundi19;
            Assert.That(Bloque(Analyser(p), "sans décision"), Is.True);

            p = Procedure(null);
            p.ClasseeSansSuite = true;
            Assert.That(Bloque(Analyser(p), "motif du classement"), Is.True);
            p.MotifClassement = "Explications jugées satisfaisantes.";
            Assert.That(ControleDiscipline.PremierBlocage(Analyser(p)), Is.Null);
            Assert.That(p.Statut, Is.EqualTo(StatutProcedure.ClasseeSansSuite));
        }

        // ── Délais internes, antécédents, pièces ─────────────────────────

        [Test]
        [Microsoft.VisualStudio.TestTools.UnitTesting.TestMethod]
        public void Engagement_TardifSignale()
        {
            var p = Procedure(Type("AVT"));
            p.DateDemandeExplication = p.DateConnaissance.AddDays(70);
            Assert.That(Signale(Analyser(p), "risque de prescription"), Is.True);

            p.DateDemandeExplication = p.DateConnaissance.AddDays(20);
            Assert.That(Signale(Analyser(p), "risque de prescription"), Is.False);
        }

        [Test]
        [Microsoft.VisualStudio.TestTools.UnitTesting.TestMethod]
        public void Antecedents_EffacementEtNonBisInIdem()
        {
            var avt = Type("AVT");      // effacé après 12 mois
            var ancien = Procedure(avt);
            ancien.Id = 1; ancien.Reference = "DISC-2025-0001";
            ancien.DateFaits = new DateTime(2025, 1, 6); ancien.DateConnaissance = ancien.DateFaits;
            ancien.DateDemandeExplication = new DateTime(2025, 1, 7); ancien.DateReponse = ancien.DateDemandeExplication;
            ancien.DateDecision = new DateTime(2025, 1, 9); ancien.DateNotification = new DateTime(2025, 1, 10);

            var recent = Procedure(Type("BLA"));
            recent.Id = 2; recent.Reference = "DISC-2026-0003";
            recent.DateFaits = new DateTime(2026, 9, 1); recent.DateConnaissance = recent.DateFaits;
            recent.DateDemandeExplication = new DateTime(2026, 9, 2); recent.DateReponse = recent.DateDemandeExplication;
            recent.DateDecision = new DateTime(2026, 9, 4); recent.DateNotification = new DateTime(2026, 9, 5);

            var registre = ControleDiscipline.Registre(new[] { ancien, recent }, Lundi19);
            Assert.That(registre.Count, Is.EqualTo(2));
            Assert.That(registre[0].Reference, Is.EqualTo("DISC-2026-0003"));
            Assert.That(registre.Single(r => r.Reference == "DISC-2025-0001").EstEffacee, Is.True);
            Assert.That(registre.Single(r => r.Reference == "DISC-2026-0003").EstEffacee, Is.False);

            var p = Procedure(Type("MAP"));
            var a = Analyser(p, Lundi19, new[] { ancien, recent });
            var info = a.Single(x => x.Message.StartsWith("Antécédent", StringComparison.Ordinal));
            Assert.That(info.Message.Contains("Blâme"), Is.True);
            Assert.That(info.Message.Contains("Avertissement"), Is.False);   // effacé

            // Mêmes faits (même date, même nature) déjà sanctionnés.
            p.DateFaits = recent.DateFaits; p.DateConnaissance = recent.DateFaits;
            p.CategorieFaits = recent.CategorieFaits;
            Assert.That(Signale(Analyser(p, Lundi19, new[] { recent }), "deux fois"), Is.True);
        }

        [Test]
        [Microsoft.VisualStudio.TestTools.UnitTesting.TestMethod]
        public void PiecesExigees_SuiventLeStadeEtLaSanction()
        {
            var p = Procedure(Type("LIC"));
            var exigees = ControleDiscipline.PiecesExigees(p, p.TypeSanction, null).Select(x => x.Type).ToList();
            Assert.That(exigees, Is.EquivalentTo(new[] { TypePiece.ConstatFaits, TypePiece.DemandeExplication, TypePiece.Convocation }));

            p.DateDemandeExplication = Lundi19; p.DateReponse = Lundi19;
            p.DateConvocation = Lundi19; p.DateEntretien = new DateTime(2026, 10, 26);
            p.MotifsDecision = "Motifs."; p.DateDecision = new DateTime(2026, 10, 27);
            p.DateNotification = new DateTime(2026, 10, 28); p.DateEffetLicenciement = new DateTime(2026, 11, 28);
            var pieces = new[]
            {
                new PieceDisciplinaire { Type = TypePiece.ConstatFaits },
                new PieceDisciplinaire { Type = TypePiece.DemandeExplication },
                new PieceDisciplinaire { Type = TypePiece.Notification }
            };
            var etat = ControleDiscipline.PiecesExigees(p, p.TypeSanction, pieces);
            Assert.That(etat.Count, Is.EqualTo(7));
            Assert.That(etat.Count(x => x.Fournie), Is.EqualTo(3));
            Assert.That(Signale(Analyser(p, Lundi19, null, pieces), "Pièce(s) à verser"), Is.True);
            Assert.That(p.Statut, Is.EqualTo(StatutProcedure.Notifiee));
        }

        [Test]
        [Microsoft.VisualStudio.TestTools.UnitTesting.TestMethod]
        public void RegistreDesMandats_UneProtectionNePeutPasEtreOubliee()
        {
            var mandats = new[]
            {
                new MandatRepresentatif { Id = 1, EmployeeId = "M001", Mandat = "Délégué du personnel titulaire",
                                          DateDebut = new DateTime(2025, 3, 1), DateFin = new DateTime(2026, 9, 30) },
                new MandatRepresentatif { Id = 2, EmployeeId = "M999", Mandat = "Délégué syndical", DateDebut = new DateTime(2024, 1, 1) }
            };

            var p = Procedure(Type("LIC"));
            p.DateDemandeExplication = Lundi19; p.DateReponse = Lundi19;
            p.DateConvocation = Lundi19; p.DateEntretien = new DateTime(2026, 10, 26);
            p.MotifsDecision = "Motifs."; p.DateDecision = new DateTime(2026, 10, 28);

            // Mandat terminé le 30 septembre, faits du 15 octobre : plus de protection.
            Assert.That(ControleDiscipline.MandatApplicable(mandats, p, Lundi19), Is.Null);
            Assert.That(ControleDiscipline.PremierBlocage(ControleDiscipline.Analyser(p, p.TypeSanction, Prm, null, null, SansFerie, Plus_tard, mandats)), Is.Null);

            // Protection prolongée jusqu'à fin mars 2027 : le licenciement non coché est bloqué.
            mandats[0].FinProtection = new DateTime(2027, 3, 31);
            Assert.That(ControleDiscipline.MandatApplicable(mandats, p, Lundi19), Is.SameAs(mandats[0]));
            var a = ControleDiscipline.Analyser(p, p.TypeSanction, Prm, null, null, SansFerie, Plus_tard, mandats);
            Assert.That(Bloque(a, "registre des salariés protégés"), Is.True);

            // Même chose avant la décision : simple avertissement.
            p.DateDecision = null;
            a = ControleDiscipline.Analyser(p, p.TypeSanction, Prm, null, null, SansFerie, Lundi19, mandats);
            Assert.That(Signale(a, "registre des salariés protégés"), Is.True);
            Assert.That(ControleDiscipline.PremierBlocage(a), Is.Null);

            // Coché : l'alerte disparaît au profit de la règle de l'autorisation.
            p.SalarieProtege = true;
            p.DateDecision = new DateTime(2026, 10, 28);
            a = ControleDiscipline.Analyser(p, p.TypeSanction, Prm, null, null, SansFerie, Plus_tard, mandats);
            Assert.That(Bloque(a, "registre"), Is.False);
            Assert.That(Bloque(a, "inspecteur du travail"), Is.True);

            // Un mandat d'un autre salarié ne compte pas ; un mandat en cours, oui.
            p.EmployeeId = "M999"; p.SalarieProtege = false;
            Assert.That(ControleDiscipline.MandatApplicable(mandats, p, Lundi19), Is.SameAs(mandats[1]));
        }

        [Test]
        [Microsoft.VisualStudio.TestTools.UnitTesting.TestMethod]
        public void Echeancier_EtEtapeSuivante()
        {
            var p = Procedure(Type("AVT"));
            p.DateDemandeExplication = Lundi19;
            var e = ControleDiscipline.Echeances(new[] { p }, Prm, SansFerie, Lundi19);
            var reponse = e.Single(x => x.Libelle.StartsWith("Fin du délai de réponse", StringComparison.Ordinal));
            Assert.That(reponse.Date, Is.EqualTo(new DateTime(2026, 10, 22)));
            Assert.That(reponse.JoursRestants, Is.EqualTo(3));
            Assert.That(ControleDiscipline.EtapeSuivante(p, p.TypeSanction, Prm, SansFerie, Lundi19).Contains("22/10/2026"), Is.True);

            // Délai expiré sans réponse : la décision est possible, avec une date limite interne.
            var apres = new DateTime(2026, 10, 24);
            Assert.That(ControleDiscipline.DateLimiteDecision(p, Prm, SansFerie, apres), Is.EqualTo(new DateTime(2026, 11, 21)));
            Assert.That(ControleDiscipline.EtapeSuivante(p, p.TypeSanction, Prm, SansFerie, apres).StartsWith("La décision peut être prise", StringComparison.Ordinal), Is.True);
            Assert.That(p.Statut, Is.EqualTo(StatutProcedure.ExplicationDemandee));
        }

        // ── Sanction notifiée : éléments figés (défaut 18) ─────────────

        private static ProcedureDisciplinaire AvertissementNotifie()
        {
            var p = Procedure(Type("AVT"));
            p.DateDemandeExplication = Lundi19;
            p.DateReponse = new DateTime(2026, 10, 20);
            p.DateDecision = new DateTime(2026, 10, 23);
            p.MotifsDecision = "Absence injustifiée les 14 et 15 octobre.";
            p.DateNotification = new DateTime(2026, 10, 24);
            p.ModeNotification = LibellesDiscipline.ModesNotification[0];
            return p;
        }

        [Test]
        [Microsoft.VisualStudio.TestTools.UnitTesting.TestMethod]
        public void SanctionNotifiee_ElementsFigesMaisRecoursPossible()
        {
            var notifiee = AvertissementNotifie();
            Assert.That(notifiee.Statut, Is.EqualTo(StatutProcedure.Notifiee));
            Assert.That(ControleDiscipline.EstFigee(notifiee), Is.True);
            Assert.That(ControleDiscipline.EstFigee(Procedure(Type("AVT"))), Is.False);   // pas encore notifiée

            // Étapes postérieures : accusé de réception (première saisie), recours, issue, commentaire.
            var saisie = notifiee.Clone();
            saisie.DateAccuseReception = new DateTime(2026, 10, 26);
            saisie.DateRecours = new DateTime(2026, 11, 2);
            saisie.InstanceRecours = "Inspection du travail (conciliation)";
            saisie.IssueRecours = "En attente";
            saisie.Commentaire = "Recours reçu.";
            Assert.That(ControleDiscipline.ChampsFigesModifies(notifiee, saisie), Is.Empty);

            // Espaces de bord et texte vide/absent ne comptent pas comme une modification.
            saisie.MotifsDecision = "  " + notifiee.MotifsDecision + " ";
            saisie.LieuFaits = "   ";
            Assert.That(ControleDiscipline.ChampsFigesModifies(notifiee, saisie), Is.Empty);

            // Ce qui fonde la sanction ne se réécrit plus.
            var faits = notifiee.Clone();
            faits.DescriptionFaits = "Autre version des faits.";
            Assert.That(ControleDiscipline.ChampsFigesModifies(notifiee, faits), Is.EquivalentTo(new[] { "description des faits" }));

            var sanction = notifiee.Clone();
            sanction.TypeSanctionId = Type("BLA").Id;
            sanction.DateNotification = new DateTime(2026, 10, 25);
            var modifies = ControleDiscipline.ChampsFigesModifies(notifiee, sanction);
            Assert.That(modifies, Does.Contain("sanction"));
            Assert.That(modifies, Does.Contain("date et mode de notification"));
            Assert.That(ControleDiscipline.MessageProcedureFigee(notifiee, modifies), Does.Contain("24/10/2026").And.Contain("figés"));

            var classee = notifiee.Clone();
            classee.ClasseeSansSuite = true;
            classee.MotifClassement = "Revirement.";
            Assert.That(ControleDiscipline.ChampsFigesModifies(notifiee, classee), Does.Contain("classement sans suite"));

            // Un accusé de réception déjà saisi n'est plus réécrit.
            var avecAr = notifiee.Clone();
            avecAr.DateAccuseReception = new DateTime(2026, 10, 26);
            var arModifie = avecAr.Clone();
            arModifie.DateAccuseReception = new DateTime(2026, 10, 27);
            Assert.That(ControleDiscipline.ChampsFigesModifies(avecAr, arModifie), Is.EquivalentTo(new[] { "décharge / accusé de réception" }));
        }

        [Test]
        [Microsoft.VisualStudio.TestTools.UnitTesting.TestMethod]
        public void MiseAPiedNotifiee_DureeFigee_EtBlocageHeriteRetrograde()
        {
            var p = Procedure(Type("MAP"));
            p.DateDemandeExplication = Lundi19;
            p.DateReponse = Lundi19;
            p.DateDecision = new DateTime(2026, 10, 21);
            p.DateNotification = new DateTime(2026, 10, 22);
            p.MotifsDecision = "Absence injustifiée.";
            p.MiseAPiedDebut = new DateTime(2026, 10, 26);
            p.MiseAPiedFin = new DateTime(2026, 11, 3);
            p.MiseAPiedJours = 8m;
            Assert.That(ControleDiscipline.PremierBlocage(Analyser(p)), Is.Null);

            var plusCourte = p.Clone();
            plusCourte.MiseAPiedJours = 5m;
            Assert.That(ControleDiscipline.ChampsFigesModifies(p, plusCourte), Is.EquivalentTo(new[] { "durée et dates de la mise à pied" }));

            // Le règlement abaisse ensuite le maximum à 5 jours : la mise à pied notifiée
            // (8 jours, figée) devient « bloquante », mais cela ne doit pas empêcher de
            // saisir le recours du salarié.
            p.TypeSanction.DureeMaxJours = 5;
            var recours = p.Clone();
            recours.DateRecours = new DateTime(2026, 11, 10);
            var brutes = Analyser(recours);
            Assert.That(Bloque(brutes, "maximum à 5 jours"), Is.True);
            var ajustees = ControleDiscipline.AlertesApresNotification(Analyser(p), brutes);
            Assert.That(ControleDiscipline.PremierBlocage(ajustees), Is.Null);
            Assert.That(Signale(ajustees, "maximum à 5 jours"), Is.True);

            // Un blocage NÉ de la saisie en cours reste bloquant : AR antérieur à la notification.
            recours.DateAccuseReception = new DateTime(2026, 10, 20);
            ajustees = ControleDiscipline.AlertesApresNotification(Analyser(p), Analyser(recours));
            Assert.That(Bloque(ajustees, "accusé de réception précède la notification"), Is.True);
        }

        [Test]
        [Microsoft.VisualStudio.TestTools.UnitTesting.TestMethod]
        public void SalarieProtege_RecalculeAuChangementDeSalarie()
        {
            var mandats = new[]
            {
                new MandatRepresentatif { Id = 1, EmployeeId = "M001", Mandat = "Délégué du personnel titulaire", DateDebut = new DateTime(2025, 1, 1) }
            };
            var p = Procedure(Type("AVT"));

            // Salarié délégué : coché, avec son mandat.
            var m = ControleDiscipline.AppliquerProtectionDuRegistre(p, mandats, Lundi19);
            Assert.That(m, Is.SameAs(mandats[0]));
            Assert.That(p.SalarieProtege, Is.True);
            Assert.That(p.MandatProtege, Is.EqualTo("Délégué du personnel titulaire"));

            // Autre salarié, sans mandat : la case ne reste plus cochée.
            p.EmployeeId = "M002";
            m = ControleDiscipline.AppliquerProtectionDuRegistre(p, mandats, Lundi19);
            Assert.That(m, Is.Null);
            Assert.That(p.SalarieProtege, Is.False);
            Assert.That(p.MandatProtege, Is.Null);
        }

        [Test]
        [Microsoft.VisualStudio.TestTools.UnitTesting.TestMethod]
        public async Task EnregistrerAsync_SanctionNotifiee_RefuseLesElementsFigesAccepteLeRecours()
        {
            string tempDb = Path.Combine(Path.GetTempPath(), $"test_disc_fige_{Guid.NewGuid():N}.db");
            try
            {
                var sqlite = new SQLiteService(tempDb);
                var dataService = new DataService(tempDb);
                var service = new DisciplineService(sqlite);
                await dataService.AddEmployeeAsync(new Employee { EmployeeId = "M001", FirstName = "Awa", LastName = "OUEDRAOGO", StatutSalarie = 0, DateEntree = new DateTime(2020, 1, 6) });

                var avt = (await service.ChargerTypesAsync()).First(t => t.Code == "AVT");
                var j = DateTime.Today;
                var p = new ProcedureDisciplinaire
                {
                    EmployeeId = "M001",
                    DateFaits = j.AddDays(-30),
                    DateConnaissance = j.AddDays(-30),
                    CategorieFaits = "Retards répétés",
                    DescriptionFaits = "Retards répétés.",
                    TypeSanctionId = avt.Id,
                    DateDemandeExplication = j.AddDays(-28),
                    DateReponse = j.AddDays(-27),
                    DateDecision = j.AddDays(-25),
                    MotifsDecision = "Retards répétés malgré un rappel.",
                    DateNotification = j.AddDays(-24)
                };
                await service.EnregistrerAsync(p);
                Assert.That(p.Id, Is.GreaterThan(0));

                // Modifier les faits d'une sanction notifiée : refusé, avec un message clair.
                var modifiee = (await service.ChargerProceduresAsync()).Single(x => x.Id == p.Id).Clone();
                modifiee.DescriptionFaits = "Version réécrite.";
                var ex = Assert.ThrowsAsync<InvalidOperationException>(async () => await service.EnregistrerAsync(modifiee));
                Assert.That(ex.Message, Does.Contain("figés").And.Contain("description des faits"));

                // Le recours, lui, s'enregistre.
                var recours = (await service.ChargerProceduresAsync()).Single(x => x.Id == p.Id).Clone();
                recours.DateRecours = j.AddDays(-2);
                recours.InstanceRecours = "Tribunal du travail";
                Assert.DoesNotThrowAsync(async () => await service.EnregistrerAsync(recours));

                var relue = (await service.ChargerProceduresAsync()).Single(x => x.Id == p.Id);
                Assert.That(relue.DescriptionFaits, Is.EqualTo("Retards répétés."));
                Assert.That(relue.DateRecours, Is.EqualTo(j.AddDays(-2)));
            }
            finally
            {
                try { if (File.Exists(tempDb)) File.Delete(tempDb); } catch { }
            }
        }
    }
}
