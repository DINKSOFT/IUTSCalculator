using System;
using System.Collections.Generic;
using System.Linq;
using IUTSCalculator.Models;

namespace IUTSCalculator.Services
{
    /// <summary>
    /// Ce que les variables d'un mois, ses absences (PAI-04), ses prêts (PAI-05) et le
    /// profil de paie du salarié (SOC-03) apportent ensemble au bulletin, réparti selon
    /// le traitement fiscal et social de chaque rubrique (branchement du moteur sur
    /// SOC-01/PAI-02).
    ///
    /// Le module PAI-02 recueillait les variables sans qu'elles atteignent le bulletin.
    /// Cette classe est le pont : elle traduit une liste de saisies en montants que le
    /// moteur sait où placer. La distinction n'est pas cosmétique — une prime soumise à
    /// l'IUTS mais pas à la CNSS n'entre pas dans la même assiette qu'une prime soumise
    /// aux deux, et les confondre fausse à la fois l'impôt et les cotisations.
    ///
    /// <b>Équivalence stricte.</b> <see cref="Aucun"/> ne porte que des zéros : un calcul
    /// fait sans variables, sans absence, sans prêt et sans profil donne exactement ce
    /// qu'il donnait avant ces branchements. C'est la même garantie par construction que
    /// pour DEC-01, et le même écran la vérifie.
    /// </summary>
    public sealed class ApportVariables
    {
        /// <summary>Gains entrant à la fois dans la base IUTS et dans l'assiette CNSS.</summary>
        public decimal GainsImposablesEtCotisables { get; private set; }

        /// <summary>Gains entrant dans la base IUTS seulement, hors assiette CNSS.</summary>
        public decimal GainsImposablesSeulement { get; private set; }

        /// <summary>Gains entrant dans l'assiette CNSS seulement, hors base IUTS.</summary>
        public decimal GainsCotisablesSeulement { get; private set; }

        /// <summary>
        /// Gains ni imposables ni cotisables : ils s'ajoutent au net à payer sans passer
        /// par le calcul (remboursement de frais, par exemple).
        /// </summary>
        public decimal GainsNonSoumis { get; private set; }

        /// <summary>
        /// Gains exonérés de la taxe patronale d'apprentissage (module DEC-02), qu'ils
        /// soient par ailleurs imposables, cotisables, ou ni l'un ni l'autre — c'est une
        /// exonération à part, indépendante de l'IUTS et de la CNSS.
        ///
        /// Une rubrique n'y contribue que si son plan de paie le dit explicitement
        /// (<see cref="Rubrique.SoumisTPA"/> à faux) : par défaut, toute rubrique est
        /// soumise à la TPA, si bien qu'un dossier qui n'a jamais touché ce réglage garde
        /// exactement l'assiette qu'il avait avant ce module.
        /// </summary>
        public decimal GainsExoneresTPA { get; private set; }

        /// <summary>Retenues diverses, déduites du net à payer après impôt et cotisations.</summary>
        public decimal Retenues { get; private set; }

        private readonly List<LigneApport> _lignes = new List<LigneApport>();

        /// <summary>
        /// Détail des variables retenues, dans l'ordre de saisie. Les totaux ci-dessus
        /// suffisent au calcul, mais pas au bulletin : un brut qui augmente sans qu'aucune
        /// ligne ne l'explique est un bulletin que le salarié ne peut pas vérifier.
        /// Les variables ignorées (rubrique inconnue, inactive ou calculée par le moteur)
        /// n'y figurent pas, puisqu'elles n'entrent pas non plus dans les totaux.
        /// </summary>
        public IReadOnlyList<LigneApport> Lignes
        {
            get { return _lignes; }
        }

        private readonly List<string> _avertissements = new List<string>();

        /// <summary>
        /// Avertissements indicatifs accumulés pendant la construction de cet apport (par
        /// exemple un doublon probable entre une retenue saisie à la main et une retenue
        /// automatique d'un module — dette connue n°9). Rien ne bloque le calcul : l'écran
        /// qui affiche le résultat décide comment les montrer.
        /// </summary>
        public IReadOnlyList<string> Avertissements
        {
            get { return _avertissements; }
        }

        /// <summary>
        /// Retenue pour absence non rémunérée (module PAI-04). Elle vient en déduction
        /// de la rémunération permanente AVANT tout calcul : une journée non travaillée
        /// réduit l'assiette de l'impôt et des cotisations, pas seulement le net. La
        /// traiter comme une retenue ordinaire ferait payer au salarié l'IUTS et la CNSS
        /// sur un salaire qu'il n'a pas perçu.
        /// </summary>
        public decimal RetenueSurBrut { get; private set; }

        /// <summary>Libellé de cette retenue sur le bulletin (« Absence non rémunérée (3 j) »).</summary>
        public string LibelleRetenueSurBrut { get; private set; }

        /// <summary>Jours de congé payé pris sur la période, repris sur le bulletin.</summary>
        public decimal JoursCongesPris { get; private set; }

        /// <summary>
        /// Désignation du profil de paie effectivement appliqué (module SOC-03), pour que
        /// l'écran et le bulletin puissent en faire mention. Reste vide tant qu'aucune
        /// ligne du profil n'a été retenue — un profil entièrement écarté parce que les
        /// variables du mois portaient déjà toutes ses rubriques n'a, de ce point de vue,
        /// rien changé au bulletin.
        /// </summary>
        public string ProfilApplique { get; private set; }

        private ApportVariables()
        {
        }

        /// <summary>
        /// Ajoute à cet apport ce que les absences du mois y changent. Renvoie la même
        /// instance : les deux sources — variables saisies et absences — décrivent le même
        /// mois du même salarié, et le moteur n'a qu'un seul apport à recevoir.
        /// </summary>
        public ApportVariables AvecAbsences(ApportAbsences absences)
        {
            if (absences == null) return this;

            RetenueSurBrut = absences.RetenueSurBrut;
            LibelleRetenueSurBrut = absences.LibelleRetenue;
            JoursCongesPris = absences.JoursCongesPris;
            return this;
        }

        /// <summary>
        /// Ajoute la retenue du mois au titre des prêts, avances et acomptes (module
        /// PAI-05). Elle porte sur le NET : elle rejoint <see cref="Retenues"/>, exactement
        /// comme une opposition saisie à la main, sans toucher à l'assiette de l'impôt ni
        /// à celle des cotisations — le salarié a déjà perçu et payé l'impôt sur la somme
        /// qu'il rembourse.
        /// </summary>
        public ApportVariables AvecPrets(ApportPrets prets)
        {
            if (prets == null || prets.EstVide) return this;

            // Doublon probable (dette connue n°9) : un dossier qui gérait ses
            // remboursements par une retenue saisie à la main, avant de basculer sur ce
            // module, doit la supprimer — sinon la même somme est retenue deux fois. Rien
            // ne l'empêche ici, mais une retenue déjà saisie ce mois-ci dont le code ou le
            // libellé évoque un prêt/une avance/un acompte, en même temps qu'une retenue
            // automatique du module pour ce salarié, est un signal fort de doublon.
            string[] motsClesPret = { "PRET", "PRÊT", "AVANCE", "ACOMPTE", "REMB" };
            foreach (var ligneExistante in _lignes)
            {
                if (ligneExistante.Sens != SensRubrique.Retenue) continue;

                string texte = ((ligneExistante.Code ?? string.Empty) + " " + (ligneExistante.Libelle ?? string.Empty))
                    .ToUpperInvariant();

                if (motsClesPret.Any(m => texte.Contains(m)))
                {
                    _avertissements.Add(string.Format(
                        "doublon possible : la retenue « {0} » saisie dans les variables du mois " +
                        "ressemble à un remboursement de prêt déjà pris en compte par le module Prêts",
                        ligneExistante.Libelle));
                }
            }

            foreach (var ligne in prets.Lignes)
            {
                if (ligne == null || ligne.Montant == 0m) continue;

                Retenues += ligne.Montant;
                _lignes.Add(new LigneApport(
                    ligne.Code, ligne.Libelle, SensRubrique.Retenue,
                    0m, ligne.Montant, false, false, false));
            }

            return this;
        }

        /// <summary>
        /// Ajoute les retenues du mois au titre des saisies-arrêts, cessions et oppositions (module PAI-06).
        /// Elles portent sur le NET après impôts et cotisations, dans la limite de la quotité saisissable
        /// légale et avec sanctuarisation du minimum vital d'existence (SMIG).
        /// </summary>
        public ApportVariables AvecSaisies(ApportSaisies saisies)
        {
            if (saisies == null || saisies.EstVide) return this;

            foreach (var ligne in saisies.Lignes)
            {
                if (ligne == null || ligne.Montant <= 0m) continue;

                Retenues += ligne.Montant;
                _lignes.Add(new LigneApport(
                    ligne.Code, ligne.Libelle, SensRubrique.Retenue,
                    0m, ligne.Montant, false, false, false));
            }

            return this;
        }

        /// <summary>
        /// Ajoute les rappels et régularisations de paie du mois (module PAI-07).
        /// Les rappels de gains (salaires, primes) rejoignent les gains imposables et cotisables,
        /// tandis que les trop-perçus et débits de régularisation d'impôt rejoignent les retenues.
        /// </summary>
        public ApportVariables AvecRappels(ApportRappels rappels)
        {
            if (rappels == null || rappels.EstVide) return this;

            GainsImposablesEtCotisables += rappels.GainsImposablesEtCotisables;
            GainsImposablesSeulement += rappels.GainsImposablesSeulement;
            GainsCotisablesSeulement += rappels.GainsCotisablesSeulement;
            GainsNonSoumis += rappels.GainsNonSoumis;
            GainsExoneresTPA += rappels.GainsExoneresTPA;
            Retenues += rappels.Retenues;

            foreach (var ligne in rappels.Lignes)
            {
                if (ligne != null && ligne.Montant != 0m)
                {
                    _lignes.Add(ligne);
                }
            }

            return this;
        }

        /// <summary>
        /// Ajoute les avantages en nature du salarié sur le mois (module PAI-08).
        /// Réintègre la valeur nette imposable dans l'assiette brute (IUTS, CNSS, TPA)
        /// et ajoute automatiquement la déduction équivalente sur le net à payer (reprise).
        /// </summary>
        public ApportVariables AvecAvantagesNature(ApportAvantagesNature avantages)
        {
            if (avantages == null || avantages.EstVide) return this;

            GainsImposablesEtCotisables += avantages.GainsImposablesEtCotisables;
            GainsImposablesSeulement += avantages.GainsImposablesSeulement;
            GainsCotisablesSeulement += avantages.GainsCotisablesSeulement;
            GainsNonSoumis += avantages.GainsNonSoumis;
            GainsExoneresTPA += avantages.GainsExoneresTPA;
            Retenues += avantages.Retenues;

            foreach (var ligne in avantages.Lignes)
            {
                if (ligne != null && ligne.Montant != 0m)
                {
                    _lignes.Add(ligne);
                }
            }

            return this;
        }

        /// <summary>
        /// Ajoute les heures supplémentaires valorisées du mois (module PAI-03).
        /// Rémunérations soumises à l'impôt (IUTS), aux cotisations (CNSS) et à la TPA.
        /// Elles rejoignent <see cref="GainsImposablesEtCotisables"/> et ajoutent les
        /// lignes détaillées (HS15, HS30, HS50, HS100) pour l'impression du bulletin.
        /// </summary>
        public ApportVariables AvecHeuresSupplementaires(ApportHeuresSupplementaires heuresSup)
        {
            if (heuresSup == null || heuresSup.EstVide) return this;

            foreach (var ligne in heuresSup.Lignes)
            {
                if (ligne == null || ligne.Montant <= 0m) continue;

                GainsImposablesEtCotisables += ligne.Montant;
                _lignes.Add(ligne);
            }

            return this;
        }

        /// <summary>
        /// Ajoute la prime d'ancienneté conventionnelle (module SOC-05).
        /// Soumise à l'impôt (IUTS), aux cotisations (CNSS) et à la TPA.
        /// Elle rejoint <see cref="GainsImposablesEtCotisables"/> et ajoute la
        /// ligne détaillée (PRIMEANC) pour l'impression du bulletin.
        /// Ne s'ajoute que si la rubrique PRIMEANC n'a pas déjà été saisie manuellement
        /// dans les variables du mois (règle de préséance).
        /// </summary>
        public ApportVariables AvecConventionCollective(ApportConventionnel apport)
        {
            if (apport == null || apport.EstVide) return this;

            var codesExistants = new HashSet<string>(
                _lignes.Select(l => (l.Code ?? string.Empty).ToUpperInvariant()));

            foreach (var ligne in apport.Lignes)
            {
                if (ligne == null || ligne.Montant <= 0m) continue;

                string cle = (ligne.Code ?? string.Empty).ToUpperInvariant();
                if (codesExistants.Contains(cle))
                    continue;

                GainsImposablesEtCotisables += ligne.Montant;
                _lignes.Add(ligne);
                codesExistants.Add(cle);
            }

            return this;
        }

        /// <summary>
        /// Ajoute ce que le profil de paie du salarié apporte au mois (module SOC-03).
        /// Appliqué EN DERNIER : une rubrique déjà présente dans cet apport — saisie dans
        /// les variables du mois, ou déjà retenue au titre d'un prêt — écarte la ligne du
        /// profil qui porte le même code. La saisie du mois est une décision explicite
        /// prise pour ce mois-là ; le profil n'est qu'un défaut, et les additionner ferait
        /// payer deux fois la même prime.
        /// </summary>
        public ApportVariables AvecProfil(ApportProfil profil)
        {
            if (profil == null || profil.EstVide) return this;

            var codesExistants = new HashSet<string>(
                _lignes.Select(l => (l.Code ?? string.Empty).ToUpperInvariant()));

            bool appliquee = false;

            foreach (var ligne in profil.Lignes)
            {
                if (ligne == null || ligne.Montant == 0m) continue;

                string cle = (ligne.Code ?? string.Empty).ToUpperInvariant();
                if (codesExistants.Contains(cle))
                    continue;

                switch (ligne.Sens)
                {
                    case SensRubrique.Gain:
                        if (ligne.SoumisIUTS && ligne.SoumisCNSS)
                            GainsImposablesEtCotisables += ligne.Montant;
                        else if (ligne.SoumisIUTS)
                            GainsImposablesSeulement += ligne.Montant;
                        else if (ligne.SoumisCNSS)
                            GainsCotisablesSeulement += ligne.Montant;
                        else
                            GainsNonSoumis += ligne.Montant;

                        if (!ligne.SoumisTPA)
                            GainsExoneresTPA += ligne.Montant;
                        break;

                    case SensRubrique.Retenue:
                        Retenues += ligne.Montant;
                        break;

                    // Information : n'entre dans aucun total, par définition.
                    default:
                        continue;
                }

                _lignes.Add(ligne);
                appliquee = true;
            }

            if (appliquee)
                ProfilApplique = profil.ProfilDesignation;

            return this;
        }

        /// <summary>Aucune variable : tous les apports sont nuls.</summary>
        public static ApportVariables Aucun()
        {
            return new ApportVariables();
        }

        /// <summary>
        /// Répartit les variables d'un salarié selon les drapeaux de leur rubrique.
        ///
        /// Une variable dont la rubrique est introuvable ou inactive est IGNORÉE, pas
        /// traitée par défaut : mieux vaut qu'elle n'apparaisse pas sur le bulletin — où
        /// son absence se remarque — que d'être versée avec un traitement fiscal devinné.
        /// L'écran de saisie la signale déjà comme anomalie.
        /// </summary>
        public static ApportVariables Depuis(
            IEnumerable<VariablePaie> variables, IEnumerable<Rubrique> planDePaie)
        {
            var apport = new ApportVariables();

            if (variables == null)
                return apport;

            var plan = (planDePaie ?? Enumerable.Empty<Rubrique>())
                .Where(r => r.Active)
                .ToDictionary(r => (r.Code ?? "").ToUpperInvariant(), r => r);

            foreach (var variable in variables)
            {
                if (variable == null || variable.Montant == 0m)
                    continue;

                Rubrique rubrique;
                if (!plan.TryGetValue((variable.CodeRubrique ?? "").ToUpperInvariant(), out rubrique))
                    continue;

                // Une rubrique calculée par le moteur (IUTS, CNSS) ne se saisit pas en
                // variable : l'accepter reviendrait à ajouter une cotisation par-dessus
                // celle que le moteur vient de calculer.
                if (rubrique.ModeCalcul == ModeCalculRubrique.Moteur)
                    continue;

                switch (rubrique.Sens)
                {
                    case SensRubrique.Gain:
                        if (rubrique.SoumisIUTS && rubrique.SoumisCNSS)
                            apport.GainsImposablesEtCotisables += variable.Montant;
                        else if (rubrique.SoumisIUTS)
                            apport.GainsImposablesSeulement += variable.Montant;
                        else if (rubrique.SoumisCNSS)
                            apport.GainsCotisablesSeulement += variable.Montant;
                        else
                            apport.GainsNonSoumis += variable.Montant;

                        if (!rubrique.SoumisTPA)
                            apport.GainsExoneresTPA += variable.Montant;
                        break;

                    case SensRubrique.Retenue:
                        apport.Retenues += variable.Montant;
                        break;

                    // Information : n'entre dans aucun total, par définition.
                    default:
                        continue;
                }

                apport._lignes.Add(new LigneApport(
                    rubrique.Code,
                    string.IsNullOrWhiteSpace(variable.LibelleRubrique)
                        ? rubrique.Libelle
                        : variable.LibelleRubrique,
                    rubrique.Sens,
                    variable.Quantite,
                    variable.Montant,
                    rubrique.SoumisIUTS,
                    rubrique.SoumisCNSS,
                    rubrique.SoumisTPA));
            }

            return apport;
        }

        /// <summary>Total des gains variables affichables sur le bulletin.</summary>
        public decimal TotalGainsLignes
        {
            get { return _lignes.Where(l => l.Sens == SensRubrique.Gain).Sum(l => l.Montant); }
        }

        /// <summary>Total des retenues diverses affichables sur le bulletin.</summary>
        public decimal TotalRetenuesLignes
        {
            get { return _lignes.Where(l => l.Sens == SensRubrique.Retenue).Sum(l => l.Montant); }
        }

        /// <summary>Total des gains entrant dans la base imposable à l'IUTS.</summary>
        public decimal GainsImposables
        {
            get { return GainsImposablesEtCotisables + GainsImposablesSeulement; }
        }

        /// <summary>Total des gains entrant dans l'assiette des cotisations CNSS.</summary>
        public decimal GainsCotisables
        {
            get { return GainsImposablesEtCotisables + GainsCotisablesSeulement; }
        }

        /// <summary>Total de tous les gains, quel que soit leur traitement.</summary>
        public decimal TousLesGains
        {
            get
            {
                return GainsImposablesEtCotisables
                     + GainsImposablesSeulement
                     + GainsCotisablesSeulement
                     + GainsNonSoumis;
            }
        }

        /// <summary>Vrai si cet apport ne change rien au calcul.</summary>
        public bool EstVide
        {
            get { return TousLesGains == 0m && Retenues == 0m && RetenueSurBrut == 0m; }
        }

        public override string ToString()
        {
            if (EstVide) return "Aucune variable";

            return string.Format(
                "gains {0:N0} (dont {1:N0} imposables, {2:N0} cotisables) — retenues {3:N0}",
                TousLesGains, GainsImposables, GainsCotisables, Retenues);
        }
    }

    /// <summary>
    /// Une variable du mois telle qu'elle doit apparaître sur le bulletin : le code et le
    /// libellé de la rubrique, la quantité saisie et le montant. Le traitement fiscal et
    /// social est conservé pour pouvoir l'expliquer au salarié (« prime non soumise »).
    /// </summary>
    public sealed class LigneApport
    {
        public LigneApport(
            string code, string libelle, SensRubrique sens,
            decimal quantite, decimal montant, bool soumisIUTS, bool soumisCNSS, bool soumisTPA)
        {
            Code = code;
            Libelle = libelle;
            Sens = sens;
            Quantite = quantite;
            Montant = montant;
            SoumisIUTS = soumisIUTS;
            SoumisCNSS = soumisCNSS;
            SoumisTPA = soumisTPA;
        }

        public string Code { get; private set; }
        public string CodeRubrique => Code;
        public string Libelle { get; private set; }
        public SensRubrique Sens { get; private set; }

        /// <summary>Quantité saisie (heures, jours…). Zéro quand la saisie est un montant seul.</summary>
        public decimal Quantite { get; private set; }

        public decimal Montant { get; private set; }
        public bool SoumisIUTS { get; private set; }
        public bool SoumisCNSS { get; private set; }

        /// <summary>La ligne entre dans l'assiette de la taxe patronale d'apprentissage (module DEC-02).</summary>
        public bool SoumisTPA { get; private set; }

        /// <summary>Libellé complet pour le bulletin : « Prime de rendement » ou « Prime de rendement (12 h) ».</summary>
        public string LibelleBulletin
        {
            get
            {
                if (Quantite == 0m) return Libelle;
                return string.Format("{0} ({1:0.##})", Libelle, Quantite);
            }
        }

        /// <summary>Mention du traitement, affichée en regard de la ligne sur le bulletin détaillé.</summary>
        public string TraitementLibelle
        {
            get
            {
                if (Sens == SensRubrique.Retenue) return "Retenue sur net";

                string traitement;
                if (SoumisIUTS && SoumisCNSS) traitement = "IUTS + CNSS";
                else if (SoumisIUTS) traitement = "IUTS";
                else if (SoumisCNSS) traitement = "CNSS";
                else traitement = "Non soumis";

                return SoumisTPA ? traitement : traitement + ", exonéré TPA";
            }
        }
    }
}
