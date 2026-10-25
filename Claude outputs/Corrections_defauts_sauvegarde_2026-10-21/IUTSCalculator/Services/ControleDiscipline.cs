using System;
using System.Collections.Generic;
using System.Linq;
using IUTSCalculator.Models;

namespace IUTSCalculator.Services
{
    /// <summary>
    /// Contrôles de procédure disciplinaire (RH-06), sans base de données ni interface :
    /// tout ce qui décide d'une alerte est ici, testable isolément.
    ///
    /// Trois niveaux :
    /// - BLOQUANT : la saisie contredit une règle de procédure (décision avant la fin du
    ///   délai de réponse, licenciement d'un délégué sans autorisation de l'inspecteur,
    ///   mise à pied au-delà du maximum, dates dans le désordre…) — l'enregistrement est refusé ;
    /// - À VÉRIFIER : un délai interne dépassé, une pièce manquante, des faits déjà sanctionnés ;
    /// - INFO : l'étape suivante, les antécédents, les effets sur la paie.
    ///
    /// Les jours ouvrables vont du lundi au samedi, jours fériés exclus (table des fériés
    /// de la Gestion des temps, RH-08).
    /// </summary>
    public static class ControleDiscipline
    {
        private static readonly ISet<DateTime> AucunFerie = new HashSet<DateTime>();

        // ── Calendrier ───────────────────────────────────────────────────

        public static bool EstOuvrable(DateTime jour, ISet<DateTime> feries)
        {
            return jour.DayOfWeek != DayOfWeek.Sunday && !(feries ?? AucunFerie).Contains(jour.Date);
        }

        /// <summary>Date atteinte en ajoutant n jours ouvrables (le jour de départ ne compte pas).</summary>
        public static DateTime AjouterJoursOuvrables(DateTime depart, int n, ISet<DateTime> feries)
        {
            var jour = depart.Date;
            int ajoutes = 0;
            while (ajoutes < n)
            {
                jour = jour.AddDays(1);
                if (EstOuvrable(jour, feries)) ajoutes++;
            }
            return jour;
        }

        /// <summary>Jours ouvrables STRICTEMENT compris entre deux dates (ni l'une ni l'autre ne compte).</summary>
        public static int JoursOuvrablesEntre(DateTime debut, DateTime fin, ISet<DateTime> feries)
        {
            int n = 0;
            for (var j = debut.Date.AddDays(1); j < fin.Date; j = j.AddDays(1))
                if (EstOuvrable(j, feries)) n++;
            return n;
        }

        /// <summary>Jours de mise à pied proposés pour une période : dimanches exclus, comme les absences PAI-04.</summary>
        public static decimal JoursMiseAPiedProposes(DateTime debut, DateTime fin)
        {
            return Absence.JoursEntre(debut, fin, true);
        }

        // ── Échéances calculées ──────────────────────────────────────────

        /// <summary>Dernier jour laissé au salarié pour répondre à la demande d'explication.</summary>
        public static DateTime? DateLimiteReponse(ProcedureDisciplinaire p, ParametresDiscipline prm, ISet<DateTime> feries)
        {
            if (p == null || !p.DateDemandeExplication.HasValue) return null;
            return AjouterJoursOuvrables(p.DateDemandeExplication.Value, Math.Max(0, (prm ?? ParametresDiscipline.Defaut()).DelaiReponseJoursOuvrables), feries);
        }

        /// <summary>Premier jour possible pour l'entretien, d'après la date de convocation.</summary>
        public static DateTime? PremiereDateEntretien(DateTime? dateConvocation, ParametresDiscipline prm, ISet<DateTime> feries)
        {
            if (!dateConvocation.HasValue) return null;
            // n jours ouvrables pleins entre la convocation et l'entretien : l'entretien tombe au (n+1)e.
            return AjouterJoursOuvrables(dateConvocation.Value, Math.Max(0, (prm ?? ParametresDiscipline.Defaut()).DelaiConvocationJoursOuvrables) + 1, feries);
        }

        /// <summary>
        /// Date à partir de laquelle la procédure contradictoire est achevée : réponse reçue
        /// (ou délai de réponse expiré) et entretien tenu. Null tant qu'un acte est attendu.
        /// </summary>
        public static DateTime? FinInstruction(ProcedureDisciplinaire p, ParametresDiscipline prm, ISet<DateTime> feries, DateTime aujourdHui)
        {
            if (p == null) return null;

            DateTime? fin = null;
            if (p.DateDemandeExplication.HasValue)
            {
                var limite = DateLimiteReponse(p, prm, feries).Value;
                if (p.DateReponse.HasValue) fin = p.DateReponse;
                else if (aujourdHui.Date > limite) fin = limite;
                else return null; // la réponse est encore attendue
            }

            if (p.DateConvocation.HasValue)
            {
                if (!p.DateEntretien.HasValue || p.DateEntretien.Value.Date > aujourdHui.Date) return null;
                if (!fin.HasValue || p.DateEntretien.Value > fin.Value) fin = p.DateEntretien;
            }

            return fin;
        }

        /// <summary>Date limite (interne) pour décider, une fois l'instruction achevée.</summary>
        public static DateTime? DateLimiteDecision(ProcedureDisciplinaire p, ParametresDiscipline prm, ISet<DateTime> feries, DateTime aujourdHui)
        {
            var fin = FinInstruction(p, prm, feries, aujourdHui);
            if (!fin.HasValue) return null;
            return fin.Value.AddDays(Math.Max(0, (prm ?? ParametresDiscipline.Defaut()).DelaiDecisionJours));
        }

        /// <summary>Date à laquelle une sanction notifiée cesse d'être un antécédent ; null si jamais.</summary>
        public static DateTime? DateEffacement(ProcedureDisciplinaire p, TypeSanction type)
        {
            if (p == null || type == null || !p.DateNotification.HasValue || type.EffacementMois <= 0) return null;
            return p.DateNotification.Value.AddMonths(type.EffacementMois);
        }

        // ── Pièces exigées ───────────────────────────────────────────────

        /// <summary>
        /// Pièces que la procédure exige au stade atteint, et si elles sont au dossier. Une
        /// pièce compte dès qu'une pièce de même nature figure au dossier (lettre produite
        /// par le logiciel ou document joint).
        /// </summary>
        public static List<PieceExigee> PiecesExigees(ProcedureDisciplinaire p, TypeSanction type, IEnumerable<PieceDisciplinaire> pieces)
        {
            var liste = new List<PieceExigee>();
            if (p == null) return liste;

            var presentes = new HashSet<TypePiece>((pieces ?? Enumerable.Empty<PieceDisciplinaire>()).Select(x => x.Type));
            Action<TypePiece, string> exiger = (t, motif) =>
                liste.Add(new PieceExigee { Type = t, Motif = motif, Fournie = presentes.Contains(t) });

            exiger(TypePiece.ConstatFaits, "Établit la matérialité et la date des faits reprochés.");

            if (p.DateDemandeExplication.HasValue || (type != null && type.ExigeExplication && !p.ClasseeSansSuite))
                exiger(TypePiece.DemandeExplication, "Demande écrite : le salarié doit pouvoir s'expliquer avant toute sanction.");

            if (p.DateReponse.HasValue)
                exiger(TypePiece.ReponseSalarie, "Réponse écrite remise par le salarié.");

            if (p.ConservatoireDebut.HasValue)
                exiger(TypePiece.MiseAPiedConservatoire, "Notification écrite de la mise à pied conservatoire.");

            if (p.DateConvocation.HasValue || (type != null && type.ExigeEntretien && !p.ClasseeSansSuite))
            {
                exiger(TypePiece.Convocation, "Convocation écrite précisant l'objet, la date, l'heure et le droit à assistance.");
                if (p.DateEntretien.HasValue)
                    exiger(TypePiece.CompteRenduEntretien, "Trace des explications recueillies lors de l'entretien.");
            }

            if (p.SalarieProtege && type != null && type.EstLicenciement && !p.ClasseeSansSuite)
                exiger(TypePiece.AutorisationInspection, "Autorisation préalable de l'inspecteur du travail (salarié protégé).");

            if (p.DateDecision.HasValue && !p.ClasseeSansSuite)
            {
                exiger(TypePiece.Notification, "Décision écrite et motivée remise au salarié.");
                if (p.DateNotification.HasValue)
                    exiger(TypePiece.AccuseReception, "Preuve de la date de notification (décharge, AR, exploit).");
            }

            return liste;
        }

        // ── Analyse complète ─────────────────────────────────────────────

        /// <summary>
        /// Mandat du registre qui protège le salarié pendant la procédure — des faits jusqu'à
        /// la décision (ou jusqu'à aujourd'hui tant qu'elle n'est pas prise) — ou null.
        /// </summary>
        public static MandatRepresentatif MandatApplicable(
            IEnumerable<MandatRepresentatif> mandats, ProcedureDisciplinaire p, DateTime aujourdHui)
        {
            if (p == null || string.IsNullOrWhiteSpace(p.EmployeeId) || mandats == null) return null;
            var fin = p.DateDecision ?? aujourdHui.Date;
            if (fin < p.DateFaits) fin = p.DateFaits;
            return mandats
                .Where(m => m != null && string.Equals(m.EmployeeId, p.EmployeeId, StringComparison.OrdinalIgnoreCase)
                            && m.CouvreLaPeriode(p.DateFaits, fin))
                .OrderByDescending(m => m.DateDebut)
                .FirstOrDefault();
        }

        /// <summary>Toutes les alertes de la procédure, bloquantes d'abord.</summary>
        public static List<AlerteDiscipline> Analyser(
            ProcedureDisciplinaire p,
            TypeSanction type,
            ParametresDiscipline prm,
            IEnumerable<ProcedureDisciplinaire> autresDuSalarie,
            IEnumerable<PieceDisciplinaire> pieces,
            ISet<DateTime> feries,
            DateTime aujourdHui,
            IEnumerable<MandatRepresentatif> mandats = null)
        {
            var alertes = new List<AlerteDiscipline>();
            if (p == null) return alertes;
            prm = prm ?? ParametresDiscipline.Defaut();
            feries = feries ?? AucunFerie;
            var jour = aujourdHui.Date;
            var fr = LibellesDiscipline.Fr;

            Action<string> bloquer = m => alertes.Add(new AlerteDiscipline(NiveauAlerte.Bloquant, m));
            Action<string> signaler = m => alertes.Add(new AlerteDiscipline(NiveauAlerte.Avertissement, m));
            Action<string> informer = m => alertes.Add(new AlerteDiscipline(NiveauAlerte.Info, m));

            // ── 1. Cohérence des dates ───────────────────────────────────
            if (string.IsNullOrWhiteSpace(p.EmployeeId))
                bloquer("Choisissez le salarié concerné.");
            if (p.DateConnaissance < p.DateFaits)
                bloquer("La date de connaissance des faits précède la date des faits.");
            if (p.DateFaits > jour)
                bloquer("La date des faits est dans le futur.");
            if (p.DateDemandeExplication.HasValue && p.DateDemandeExplication.Value < p.DateFaits)
                bloquer("La demande d'explication est datée d'avant les faits.");
            if (p.DateReponse.HasValue && !p.DateDemandeExplication.HasValue)
                bloquer("Une réponse est saisie sans demande d'explication.");
            if (p.DateReponse.HasValue && p.DateDemandeExplication.HasValue && p.DateReponse.Value < p.DateDemandeExplication.Value)
                bloquer("La réponse du salarié précède la demande d'explication.");
            if (p.DateEntretien.HasValue && !p.DateConvocation.HasValue)
                bloquer("Un entretien est daté sans convocation : renseignez la date de convocation.");
            if (p.DateEntretien.HasValue && p.DateConvocation.HasValue && p.DateEntretien.Value < p.DateConvocation.Value)
                bloquer("L'entretien précède la convocation.");
            if (p.ConservatoireFin.HasValue && !p.ConservatoireDebut.HasValue)
                bloquer("Fin de mise à pied conservatoire sans date de début.");
            if (p.ConservatoireDebut.HasValue && p.ConservatoireFin.HasValue && p.ConservatoireFin.Value < p.ConservatoireDebut.Value)
                bloquer("La mise à pied conservatoire se termine avant d'avoir commencé.");
            if (p.DateNotification.HasValue && !p.DateDecision.HasValue && !p.ClasseeSansSuite)
                bloquer("Une notification est datée sans décision : renseignez la date de décision.");
            if (p.DateNotification.HasValue && p.DateDecision.HasValue && p.DateNotification.Value < p.DateDecision.Value)
                bloquer("La notification précède la décision.");
            if (p.DateAccuseReception.HasValue && p.DateNotification.HasValue && p.DateAccuseReception.Value < p.DateNotification.Value)
                bloquer("L'accusé de réception précède la notification.");
            if (p.DateRecours.HasValue && !p.DateNotification.HasValue)
                bloquer("Un recours est daté alors que la décision n'a pas été notifiée.");
            if (p.ClasseeSansSuite && string.IsNullOrWhiteSpace(p.MotifClassement))
                bloquer("Indiquez le motif du classement sans suite.");

            // ── 2. Décision ──────────────────────────────────────────────
            bool decide = p.DateDecision.HasValue && !p.ClasseeSansSuite;
            var limiteReponse = DateLimiteReponse(p, prm, feries);

            if (decide)
            {
                if (type == null)
                {
                    bloquer("Choisissez la sanction décidée.");
                }
                else
                {
                    var engagement = p.DateEngagement;
                    if (engagement.HasValue && p.DateDecision.Value < engagement.Value)
                        bloquer("La décision précède le premier acte de procédure.");

                    if (type.ExigeExplication && !p.DateDemandeExplication.HasValue)
                        bloquer(string.Format("« {0} » exige une demande d'explication écrite préalable : le salarié doit pouvoir présenter sa défense.", type.Libelle));

                    if (p.DateDemandeExplication.HasValue)
                    {
                        if (p.DateReponse.HasValue && p.DateDecision.Value < p.DateReponse.Value)
                            bloquer("La décision précède la réponse du salarié.");
                        else if (!p.DateReponse.HasValue && p.DateDecision.Value <= limiteReponse.Value)
                            bloquer(string.Format(fr, "Décision prise avant l'expiration du délai de réponse (le salarié a jusqu'au {0:dd/MM/yyyy}).", limiteReponse.Value));
                    }

                    if (type.ExigeEntretien && !p.DateEntretien.HasValue)
                        bloquer(string.Format("« {0} » exige un entretien préalable : convoquez le salarié et tenez l'entretien avant de décider.", type.Libelle));
                    if (p.DateEntretien.HasValue && p.DateDecision.Value < p.DateEntretien.Value)
                        bloquer("La décision précède l'entretien préalable.");

                    if (type.EstMiseAPied)
                    {
                        if (!p.MiseAPiedDebut.HasValue || !p.MiseAPiedFin.HasValue)
                            bloquer("Indiquez les dates de début et de fin de la mise à pied.");
                        else
                        {
                            if (p.MiseAPiedFin.Value < p.MiseAPiedDebut.Value)
                                bloquer("La mise à pied se termine avant d'avoir commencé.");
                            if (p.MiseAPiedJours <= 0m)
                                bloquer("Indiquez le nombre de jours de mise à pied retenus.");
                            var possibles = JoursMiseAPiedProposes(p.MiseAPiedDebut.Value, p.MiseAPiedFin.Value);
                            if (possibles > 0m && p.MiseAPiedJours > possibles)
                                bloquer(string.Format(fr, "{0:0.##} jours retenus alors que la période n'en compte que {1:0.##} (dimanches exclus).", p.MiseAPiedJours, possibles));
                            if (type.DureeMaxJours > 0 && p.MiseAPiedJours > type.DureeMaxJours)
                                bloquer(string.Format(fr, "Mise à pied de {0:0.##} jours : le règlement en fixe le maximum à {1} jours.", p.MiseAPiedJours, type.DureeMaxJours));
                            if (p.DateNotification.HasValue && p.MiseAPiedDebut.Value < p.DateNotification.Value)
                                bloquer("La mise à pied ne peut pas commencer avant d'avoir été notifiée.");
                        }
                    }

                    if (type.EstLicenciement)
                    {
                        if (string.IsNullOrWhiteSpace(p.MotifsDecision))
                            bloquer("Le licenciement doit être motivé par écrit : rédigez les motifs précis (sans motif écrit, la rupture est irrégulière).");
                        if (type.FauteLourde && p.Gravite != GraviteFaute.Lourde)
                            bloquer("Un licenciement pour faute lourde suppose une faute qualifiée de lourde.");
                        if (p.SalarieProtege && p.DecisionInspection != DecisionInspection.Accordee)
                            bloquer("Salarié protégé : le licenciement exige l'autorisation préalable de l'inspecteur du travail.");
                        if (p.DateNotification.HasValue && !p.DateEffetLicenciement.HasValue)
                            bloquer("Indiquez la date d'effet du licenciement (fin du contrat).");
                        if (p.DateEffetLicenciement.HasValue && p.DateNotification.HasValue && p.DateEffetLicenciement.Value < p.DateNotification.Value)
                            bloquer("Le licenciement ne peut pas prendre effet avant sa notification.");
                        if (p.SalarieProtege && p.DecisionInspection == DecisionInspection.Accordee && p.DateDecisionInspection.HasValue
                            && p.DateDecision.Value < p.DateDecisionInspection.Value)
                            bloquer("La décision de licenciement précède l'autorisation de l'inspecteur du travail.");
                    }
                    else if (string.IsNullOrWhiteSpace(p.MotifsDecision))
                    {
                        signaler("Motivez la sanction : la lettre de notification doit énoncer les faits reprochés.");
                    }
                }
            }

            // ── 2 bis. Registre des mandats : une protection ne s'oublie pas ──
            var mandat = MandatApplicable(mandats, p, jour);
            if (mandat != null && !p.SalarieProtege && !p.ClasseeSansSuite)
            {
                string m = string.Format(fr, "Le registre des salariés protégés indique un mandat de {0} ({1}, protection {2}) : cochez « Salarié protégé ».",
                    (mandat.Mandat ?? "représentant").ToLower(fr), mandat.PeriodeLibelle, mandat.ProtectionLibelle.ToLower(fr));
                if (decide && type != null && type.EstLicenciement)
                    bloquer(m + " Le licenciement exige l'autorisation préalable de l'inspecteur du travail.");
                else
                    signaler(m);
            }

            // ── 3. Délais internes (règlement intérieur) ─────────────────
            var dateEngagement = p.DateEngagement;
            if (dateEngagement.HasValue)
            {
                int ecart = (dateEngagement.Value - p.DateConnaissance).Days;
                if (prm.DelaiEngagementJours > 0 && ecart > prm.DelaiEngagementJours)
                    signaler(string.Format(fr, "Procédure engagée {0} jours après la connaissance des faits (délai du dossier : {1} jours) : risque de prescription.", ecart, prm.DelaiEngagementJours));
            }
            else if (!p.ClasseeSansSuite)
            {
                int ecart = (jour - p.DateConnaissance).Days;
                if (prm.DelaiEngagementJours > 0 && ecart > prm.DelaiEngagementJours)
                    signaler(string.Format(fr, "Aucun acte de procédure {0} jours après la connaissance des faits (délai du dossier : {1} jours).", ecart, prm.DelaiEngagementJours));
            }

            if (p.DateConvocation.HasValue && p.DateEntretien.HasValue)
            {
                int ouvrables = JoursOuvrablesEntre(p.DateConvocation.Value, p.DateEntretien.Value, feries);
                if (ouvrables < prm.DelaiConvocationJoursOuvrables)
                    signaler(string.Format(fr, "{0} jour(s) ouvrable(s) seulement entre la convocation et l'entretien ({1} exigés) : le salarié doit pouvoir préparer sa défense.", ouvrables, prm.DelaiConvocationJoursOuvrables));
            }

            var limiteDecision = DateLimiteDecision(p, prm, feries, jour);
            if (limiteDecision.HasValue && !p.ClasseeSansSuite)
            {
                if (p.DateDecision.HasValue && p.DateDecision.Value > limiteDecision.Value)
                    signaler(string.Format(fr, "Décision prise après le délai du dossier ({0:dd/MM/yyyy}).", limiteDecision.Value));
                else if (!p.DateDecision.HasValue && jour > limiteDecision.Value)
                    signaler(string.Format(fr, "Délai de décision dépassé depuis le {0:dd/MM/yyyy} : décidez ou classez sans suite.", limiteDecision.Value));
            }

            if (p.DateDecision.HasValue && !p.DateNotification.HasValue && !p.ClasseeSansSuite)
                signaler("Décision prise mais non notifiée : elle ne produit d'effet qu'une fois notifiée par écrit.");

            if (p.ConservatoireDebut.HasValue && !p.ConservatoireFin.HasValue && !p.DateDecision.HasValue)
            {
                int duree = (jour - p.ConservatoireDebut.Value).Days + 1;
                if (prm.DelaiDecisionJours > 0 && duree > prm.DelaiDecisionJours)
                    signaler(string.Format(fr, "Mise à pied conservatoire en cours depuis {0} jours : une mesure d'attente doit déboucher rapidement sur une décision.", duree));
                else
                    informer(string.Format(fr, "Mise à pied conservatoire en cours depuis le {0:dd/MM/yyyy}.", p.ConservatoireDebut.Value));
            }

            // ── 4. Pièces ────────────────────────────────────────────────
            var exigees = PiecesExigees(p, type, pieces);
            var manquantes = exigees.Where(x => !x.Fournie).Select(x => x.Libelle).ToList();
            if (manquantes.Count > 0)
            {
                var message = "Pièce(s) à verser au dossier : " + string.Join(", ", manquantes) + ".";
                if (p.EstClose) signaler(message); else informer(message);
            }

            // ── 5. Historique du salarié ─────────────────────────────────
            var autres = (autresDuSalarie ?? Enumerable.Empty<ProcedureDisciplinaire>())
                .Where(a => a != null && a.Id != p.Id && string.Equals(a.EmployeeId, p.EmployeeId, StringComparison.OrdinalIgnoreCase))
                .ToList();

            var memesFaits = autres.FirstOrDefault(a => a.Statut == StatutProcedure.Notifiee
                && a.DateFaits == p.DateFaits
                && string.Equals((a.CategorieFaits ?? "").Trim(), (p.CategorieFaits ?? "").Trim(), StringComparison.OrdinalIgnoreCase));
            if (memesFaits != null)
                signaler(string.Format("Des faits de même nature et de même date ont déjà été sanctionnés ({0}) : un même fait ne peut être sanctionné deux fois.", memesFaits.Reference));

            var antecedents = autres
                .Where(a => a.Statut == StatutProcedure.Notifiee && a.TypeSanction != null
                    && a.DateNotification.HasValue && a.DateNotification.Value <= jour)
                .Where(a => { var e = DateEffacement(a, a.TypeSanction); return !e.HasValue || e.Value > jour; })
                .OrderByDescending(a => a.DateNotification)
                .ToList();
            if (antecedents.Count > 0)
                informer(string.Format(fr, "Antécédent(s) non effacé(s) : {0}.", string.Join(" ; ",
                    antecedents.Select(a => string.Format(fr, "{0} du {1:dd/MM/yyyy}", a.TypeSanction.Libelle, a.DateNotification.Value)))));

            // ── 6. Effets et étape suivante ──────────────────────────────
            if (p.SalarieProtege && !p.ClasseeSansSuite)
                informer("Salarié protégé" + (string.IsNullOrWhiteSpace(p.MandatProtege) ? "" : " (" + p.MandatProtege.Trim() + ")") +
                         " : les faits liés à l'exercice du mandat ne peuvent pas fonder une sanction.");

            if (p.ConservatoireDebut.HasValue && (p.ClasseeSansSuite || (decide && type != null && !(type.EstLicenciement && type.FauteLourde))))
                informer("La mise à pied conservatoire n'est pas une sanction : les jours correspondants restent payés (seul un licenciement pour faute lourde permet de les retenir).");

            if (decide && type != null && type.EstLicenciement)
                informer(type.FauteLourde
                    ? "Faute lourde : rupture sans préavis ni indemnité de licenciement. Établissez le solde de tout compte (SAL-05), le motif y est proposé."
                    : "Licenciement disciplinaire : préavis et indemnité de licenciement restent dus. Établissez le solde de tout compte (SAL-05).");

            if (p.Statut == StatutProcedure.Notifiee && type != null && type.EstMiseAPied && p.MiseAPiedDebut.HasValue)
                informer(string.Format(fr, "Retenue de {0:0.##} jour(s) transmise aux congés et absences (mise à pied du {1:dd/MM/yyyy} au {2:dd/MM/yyyy}).",
                    p.MiseAPiedJours, p.MiseAPiedDebut.Value, p.MiseAPiedFin ?? p.MiseAPiedDebut.Value));

            var etape = EtapeSuivante(p, type, prm, feries, jour);
            if (!string.IsNullOrEmpty(etape)) informer(etape);

            return alertes.OrderByDescending(a => a.Niveau).ToList();
        }

        /// <summary>Première alerte bloquante, ou null : ce que le service oppose à l'enregistrement.</summary>
        public static string PremierBlocage(IEnumerable<AlerteDiscipline> alertes)
        {
            var b = (alertes ?? Enumerable.Empty<AlerteDiscipline>()).FirstOrDefault(a => a.Niveau == NiveauAlerte.Bloquant);
            return b == null ? null : b.Message;
        }

        /// <summary>Ce qui reste à faire, en une phrase.</summary>
        public static string EtapeSuivante(ProcedureDisciplinaire p, TypeSanction type, ParametresDiscipline prm, ISet<DateTime> feries, DateTime aujourdHui)
        {
            if (p == null) return null;
            var fr = LibellesDiscipline.Fr;
            prm = prm ?? ParametresDiscipline.Defaut();

            switch (p.Statut)
            {
                case StatutProcedure.ClasseeSansSuite:
                    return "Procédure classée sans suite.";

                case StatutProcedure.Notifiee:
                    return p.DateRecours.HasValue
                        ? "Décision notifiée et contestée : suivez l'issue du recours."
                        : "Procédure close : décision notifiée.";

                case StatutProcedure.DecisionPrise:
                    return "Étape suivante : notifier la décision par écrit (remise contre décharge ou lettre recommandée), puis saisir la date de notification.";
            }

            if (!p.DateDemandeExplication.HasValue)
                return "Étape suivante : adresser au salarié une demande d'explication écrite (lettre disponible).";

            var limite = DateLimiteReponse(p, prm, feries).Value;
            if (!p.DateReponse.HasValue && aujourdHui.Date <= limite)
                return string.Format(fr, "En attente de la réponse du salarié jusqu'au {0:dddd dd/MM/yyyy}.", limite);

            bool entretienRequis = type != null && type.ExigeEntretien;
            if (entretienRequis && !p.DateConvocation.HasValue)
            {
                var premiere = PremiereDateEntretien(aujourdHui, prm, feries).Value;
                return string.Format(fr, "Étape suivante : convoquer le salarié à un entretien préalable (au plus tôt le {0:dd/MM/yyyy} pour une convocation remise aujourd'hui).", premiere);
            }

            if (p.DateConvocation.HasValue && (!p.DateEntretien.HasValue || p.DateEntretien.Value.Date > aujourdHui.Date))
                return p.DateEntretien.HasValue
                    ? string.Format(fr, "Entretien préalable prévu le {0:dddd dd/MM/yyyy}.", p.DateEntretien.Value)
                    : "Fixez la date de l'entretien préalable.";

            if (type != null && type.EstLicenciement && p.SalarieProtege && p.DecisionInspection != DecisionInspection.Accordee)
                return p.DecisionInspection == DecisionInspection.Refusee
                    ? "L'inspecteur du travail a refusé le licenciement : retenez une autre sanction ou classez sans suite."
                    : "Étape suivante : demander l'autorisation de licencier à l'inspecteur du travail (salarié protégé).";

            var limiteDecision = DateLimiteDecision(p, prm, feries, aujourdHui);
            return limiteDecision.HasValue
                ? string.Format(fr, "La décision peut être prise (délai du dossier : jusqu'au {0:dd/MM/yyyy}), ou la procédure classée sans suite.", limiteDecision.Value)
                : "La décision peut être prise, ou la procédure classée sans suite.";
        }

        // ── Échéancier et registre ───────────────────────────────────────

        /// <summary>Échéances des procédures en cours (et des mises à pied à venir), par date.</summary>
        public static List<EcheanceDiscipline> Echeances(
            IEnumerable<ProcedureDisciplinaire> procedures, ParametresDiscipline prm, ISet<DateTime> feries, DateTime aujourdHui)
        {
            var liste = new List<EcheanceDiscipline>();
            var jour = aujourdHui.Date;
            prm = prm ?? ParametresDiscipline.Defaut();

            foreach (var p in procedures ?? Enumerable.Empty<ProcedureDisciplinaire>())
            {
                if (p == null || p.ClasseeSansSuite) continue;

                Action<DateTime, string> ajouter = (d, lib) => liste.Add(new EcheanceDiscipline
                {
                    ProcedureId = p.Id,
                    Reference = p.Reference,
                    EmployeeId = p.EmployeeId,
                    NomSalarie = p.NomSalarie,
                    Date = d.Date,
                    Libelle = lib,
                    JoursRestants = (d.Date - jour).Days
                });

                if (p.Statut == StatutProcedure.Notifiee)
                {
                    // Seules les mises à pied encore à venir restent à suivre.
                    if (p.TypeSanction != null && p.TypeSanction.EstMiseAPied && p.MiseAPiedDebut.HasValue && p.MiseAPiedFin.HasValue
                        && p.MiseAPiedFin.Value >= jour)
                    {
                        if (p.MiseAPiedDebut.Value >= jour) ajouter(p.MiseAPiedDebut.Value, "Début de la mise à pied");
                        ajouter(p.MiseAPiedFin.Value, "Fin de la mise à pied (reprise le lendemain)");
                    }
                    continue;
                }

                if (p.DateDecision.HasValue)
                {
                    ajouter(p.DateDecision.Value, "Notifier la décision");
                    continue;
                }

                if (!p.DateEngagement.HasValue && prm.DelaiEngagementJours > 0)
                    ajouter(p.DateConnaissance.AddDays(prm.DelaiEngagementJours), "Engager la procédure (délai du dossier)");

                if (p.DateDemandeExplication.HasValue && !p.DateReponse.HasValue)
                {
                    var limite = DateLimiteReponse(p, prm, feries).Value;
                    if (limite >= jour) ajouter(limite, "Fin du délai de réponse à la demande d'explication");
                }

                if (p.DateEntretien.HasValue && p.DateEntretien.Value >= jour)
                    ajouter(p.DateEntretien.Value, "Entretien préalable");

                var limiteDecision = DateLimiteDecision(p, prm, feries, jour);
                if (limiteDecision.HasValue)
                    ajouter(limiteDecision.Value, "Décider (délai du dossier)");
            }

            return liste.OrderBy(e => e.Date).ThenBy(e => e.Reference).ToList();
        }

        /// <summary>Registre des sanctions notifiées, la plus récente d'abord.</summary>
        public static List<LigneRegistreSanction> Registre(IEnumerable<ProcedureDisciplinaire> procedures, DateTime aujourdHui)
        {
            return (procedures ?? Enumerable.Empty<ProcedureDisciplinaire>())
                .Where(p => p != null && p.Statut == StatutProcedure.Notifiee && p.TypeSanction != null)
                .Select(p =>
                {
                    var eff = DateEffacement(p, p.TypeSanction);
                    return new LigneRegistreSanction
                    {
                        ProcedureId = p.Id,
                        Reference = p.Reference,
                        EmployeeId = p.EmployeeId,
                        NomSalarie = p.NomSalarie,
                        DateFaits = p.DateFaits,
                        CategorieFaits = p.CategorieFaits,
                        Sanction = p.TypeSanction.Libelle,
                        Nature = p.TypeSanction.Nature,
                        DateNotification = p.DateNotification.Value,
                        JoursMiseAPied = p.TypeSanction.EstMiseAPied ? p.MiseAPiedJours : 0m,
                        DateEffacement = eff,
                        EstEffacee = eff.HasValue && eff.Value <= aujourdHui.Date,
                        Recours = p.DateRecours.HasValue
                    };
                })
                .OrderByDescending(l => l.DateNotification)
                .ThenByDescending(l => l.Reference)
                .ToList();
        }
    }
}
