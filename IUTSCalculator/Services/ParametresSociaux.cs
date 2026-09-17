using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using IUTSCalculator.Models;

namespace IUTSCalculator.Services
{
    /// <summary>
    /// Les taux et plafonds sociaux que le moteur de calcul applique (module DEC-01).
    ///
    /// Ces valeurs étaient écrites en dur dans <c>TaxCalculatorService</c> :
    /// <c>CalculerCotisationSociale(grossIncome, 800000, 0.055m)</c>. Deux conséquences,
    /// toutes deux corrigées ici :
    ///
    ///   • Un changement de taux imposait une livraison de code.
    ///   • La part patronale n'existait que globalement (16 %), sans la ventilation par
    ///     branche que la CNSS exige sur le bordereau.
    ///
    /// <b>Équivalence stricte.</b> Les valeurs par défaut de cette classe sont exactement
    /// celles qui étaient codées en dur. Tant qu'un barème ne les modifie pas, le moteur
    /// produit au franc près les mêmes bulletins qu'avant — et l'écran « Contrôle
    /// d'équivalence » le vérifie ligne à ligne plutôt que de le promettre.
    ///
    /// La ventilation ne peut pas introduire d'écart non plus : les trois branches sont
    /// calculées sur la même assiette et leur somme est, en arithmétique décimale exacte,
    /// égale à la part patronale globale. Aucun arrondi n'est appliqué par branche.
    /// </summary>
    public sealed class ParametresSociaux
    {
        /// <summary>Part salariale — pensions.</summary>
        public decimal TauxCnssSalarie { get; private set; }

        /// <summary>Part patronale — prestations familiales.</summary>
        public decimal TauxPrestationsFamiliales { get; private set; }

        /// <summary>Part patronale — risques professionnels.</summary>
        public decimal TauxRisquesProfessionnels { get; private set; }

        /// <summary>Part patronale — pensions.</summary>
        public decimal TauxPensionsPatronal { get; private set; }

        /// <summary>Plafond mensuel de l'assiette cotisable, en FCFA.</summary>
        public decimal PlafondMensuel { get; private set; }

        /// <summary>Taxe patronale d'apprentissage, assise sur le brut.</summary>
        public decimal TauxTaxePatronale { get; private set; }

        // Plafonds d'exonération de l'Art.106 §1 CGI (logement, fonction, transport) et
        // abattements professionnels forfaitaires. Comme les taux CNSS/TPA ci-dessus, ils
        // venaient jusqu'ici directement de TaxDataService (une seule valeur globale, pas
        // datée) au lieu de suivre le barème appliqué à la période (dette connue n°2).

        /// <summary>Taux plafond de l'indemnité de logement exonérée (Art.106 §1).</summary>
        public decimal TauxMaxIndemniteLogement { get; private set; }

        /// <summary>Montant plafond, en FCFA, de l'indemnité de logement exonérée.</summary>
        public decimal PlafondIndemniteLogement { get; private set; }

        /// <summary>Taux plafond de l'indemnité de fonction exonérée (Art.106 §1).</summary>
        public decimal TauxMaxIndemniteFonction { get; private set; }

        /// <summary>Montant plafond, en FCFA, de l'indemnité de fonction exonérée.</summary>
        public decimal PlafondIndemniteFonction { get; private set; }

        /// <summary>Taux plafond de l'indemnité de transport exonérée (Art.106 §1).</summary>
        public decimal TauxMaxIndemniteTransport { get; private set; }

        /// <summary>Montant plafond, en FCFA, de l'indemnité de transport exonérée.</summary>
        public decimal PlafondIndemniteTransport { get; private set; }

        /// <summary>Abattement forfaitaire professionnel — catégories supérieures.</summary>
        public decimal AbattementCategoriesSuperieures { get; private set; }

        /// <summary>Abattement forfaitaire professionnel — autres catégories.</summary>
        public decimal AbattementAutresCategories { get; private set; }

        /// <summary>Barème dont ces paramètres sont issus, s'il y en a un.</summary>
        public BaremeLegal Origine { get; private set; }

        // Barème IUTS (Art.112 CGI) et réductions pour charges de famille (Art.113 CGI).
        // Venaient jusqu'ici directement de TaxDataService (fichiers taxBrackets.json /
        // taxConstants.json, sans dimension temporelle) au lieu de suivre le barème daté
        // applicable à la période, comme les taux CNSS/TPA et les plafonds d'exonération
        // ci-dessus — c'était le point resté volontairement en suspens depuis SOC-02 (voir
        // la note d'architecture de BaremeLegal.cs), branché ici.

        /// <summary>Tranches de l'IUTS, de la plus basse à la plus haute, bornes contiguës.</summary>
        public List<TaxBracket> Tranches { get; private set; }

        /// <summary>Réduction d'impôt pour charges de famille, par nombre de charges (0 à 7).</summary>
        public Dictionary<int, decimal> ChargesFamille { get; private set; }

        private ParametresSociaux()
        {
        }

        /// <summary>
        /// Les valeurs historiques du moteur, reprises à l'identique :
        /// 5,5 % salarié ; 6 % + 1,5 % + 8,5 % = 16 % patronal ; plafond 800 000 ; TPA 3 %.
        ///
        /// C'est la référence d'équivalence : ne jamais y toucher sans raison légale.
        /// </summary>
        public static ParametresSociaux Historiques()
        {
            return new ParametresSociaux
            {
                TauxCnssSalarie = 0.055m,
                TauxPrestationsFamiliales = 0.06m,
                TauxRisquesProfessionnels = 0.015m,
                TauxPensionsPatronal = 0.085m,
                PlafondMensuel = 800000m,
                TauxTaxePatronale = 0.03m,
                TauxMaxIndemniteLogement = 0.20m,
                PlafondIndemniteLogement = 75000m,
                TauxMaxIndemniteFonction = 0.05m,
                PlafondIndemniteFonction = 50000m,
                TauxMaxIndemniteTransport = 0.05m,
                PlafondIndemniteTransport = 30000m,
                AbattementCategoriesSuperieures = 0.20m,
                AbattementAutresCategories = 0.25m,
                Tranches = TranchesHistoriques(),
                ChargesFamille = ChargesFamilleHistoriques(),
                Origine = null
            };
        }

        /// <summary>
        /// Tranches historiques de l'IUTS (Art.112 CGI), reprises à l'identique des valeurs
        /// jusqu'ici codées en dur dans TaxDataService.InitializeDefaultData : bornes
        /// CONTIGÜES (30 000/50 000/80 000/... et non les repères informels 30 100/50 100/...
        /// du texte publié), conformes au bulletin de référence "Option 4".
        /// </summary>
        private static List<TaxBracket> TranchesHistoriques()
        {
            return new List<TaxBracket>
            {
                new TaxBracket(0m, 30000m, 0m),
                new TaxBracket(30000m, 50000m, 12.10m),
                new TaxBracket(50000m, 80000m, 13.90m),
                new TaxBracket(80000m, 120000m, 15.70m),
                new TaxBracket(120000m, 170000m, 18.40m),
                new TaxBracket(170000m, 250000m, 21.70m),
                new TaxBracket(250000m, decimal.MaxValue, 25.00m)
            };
        }

        /// <summary>
        /// Réductions historiques pour charges de famille (Art.113 CGI), 0 à 7 charges,
        /// reprises à l'identique des valeurs jusqu'ici codées en dur dans
        /// TaxDataService.InitializeDefaultData.
        /// </summary>
        private static Dictionary<int, decimal> ChargesFamilleHistoriques()
        {
            return new Dictionary<int, decimal>
            {
                { 0, 0m }, { 1, 0.08m }, { 2, 0.10m }, { 3, 0.12m },
                { 4, 0.14m }, { 5, 0.16m }, { 6, 0.18m }, { 7, 0.20m }
            };
        }

        /// <summary>
        /// Vrai si les tranches d'un barème forment une table utilisable par le moteur :
        /// au moins une tranche, la première commençant à 0, chaque borne touchant
        /// exactement la suivante, la dernière ouverte, des taux entre 0 et 100 %. Mêmes
        /// contrôles que <see cref="BaremeLegal.Anomalies"/>, mais limités aux tranches :
        /// un barème dont seule la partie CNSS est incomplète (déjà couverte champ par champ
        /// ci-dessus) ne doit pas faire perdre des tranches par ailleurs valides.
        ///
        /// Un barème qui échoue à ce contrôle (tranches pas encore saisies, ou
        /// incohérentes) retombe sur la table historique plutôt que de produire un IUTS
        /// faux ou une exception en cours de bulletin — même logique de repli que les
        /// champs scalaires ci-dessus, mais tout ou rien : contrairement à un taux isolé,
        /// une tranche ne se complète pas champ par champ, elle se remplace en bloc.
        /// </summary>
        private static bool TranchesValides(ObservableCollection<TrancheBareme> tranches)
        {
            if (tranches == null || tranches.Count == 0) return false;

            var ordonnees = tranches.OrderBy(t => t.BorneInferieure).ToList();

            if (ordonnees[0].BorneInferieure != 0m) return false;

            for (int i = 0; i < ordonnees.Count; i++)
            {
                var tranche = ordonnees[i];

                if (tranche.BorneSuperieure <= tranche.BorneInferieure) return false;
                if (tranche.Taux < 0m || tranche.Taux > 100m) return false;
                if (i > 0 && ordonnees[i - 1].BorneSuperieure != tranche.BorneInferieure) return false;
            }

            return ordonnees[ordonnees.Count - 1].EstDerniereTranche;
        }

        private static List<TaxBracket> ConvertirTranches(ObservableCollection<TrancheBareme> tranches)
        {
            return tranches
                .OrderBy(t => t.BorneInferieure)
                .Select(t => new TaxBracket(t.BorneInferieure, t.BorneSuperieure, t.Taux))
                .ToList();
        }

        /// <summary>
        /// Réductions pour charges de famille tirées du barème, complétées par les valeurs
        /// historiques pour tout nombre de charges (0 à 7) que le barème n'a pas renseigné —
        /// même logique de repli champ par champ que les taux CNSS/TPA ci-dessus (une
        /// réduction se complète charge par charge, à la différence des tranches).
        /// </summary>
        private static Dictionary<int, decimal> FusionnerChargesFamille(
            ObservableCollection<ReductionChargeFamille> chargesBareme, Dictionary<int, decimal> reference)
        {
            var parBareme = (chargesBareme ?? Enumerable.Empty<ReductionChargeFamille>())
                .Where(c => c != null)
                .GroupBy(c => c.NombreCharges)
                .ToDictionary(g => g.Key, g => g.First().Taux);

            var resultat = new Dictionary<int, decimal>();
            foreach (var charges in reference.Keys)
            {
                decimal tauxBareme;
                resultat[charges] = (charges > 0 && parBareme.TryGetValue(charges, out tauxBareme) && tauxBareme > 0m)
                    ? tauxBareme
                    : reference[charges];
            }
            return resultat;
        }

        /// <summary>
        /// Paramètres tirés d'un barème daté (SOC-02). Un barème incomplet — champ laissé
        /// à zéro parce qu'il n'a pas été saisi — ne doit pas faire tomber une cotisation
        /// à zéro sans un mot : chaque valeur manquante retombe sur la valeur historique,
        /// et <see cref="Ecarts"/> permet de savoir laquelle a été reprise.
        /// </summary>
        public static ParametresSociaux DepuisBareme(BaremeLegal bareme)
        {
            var reference = Historiques();
            if (bareme == null)
                return reference;

            return new ParametresSociaux
            {
                TauxCnssSalarie = bareme.TauxCnssSalarie > 0m
                    ? bareme.TauxCnssSalarie : reference.TauxCnssSalarie,
                TauxPrestationsFamiliales = bareme.TauxCnssPrestationsFamiliales > 0m
                    ? bareme.TauxCnssPrestationsFamiliales : reference.TauxPrestationsFamiliales,
                TauxRisquesProfessionnels = bareme.TauxCnssRisquesProfessionnels > 0m
                    ? bareme.TauxCnssRisquesProfessionnels : reference.TauxRisquesProfessionnels,
                TauxPensionsPatronal = bareme.TauxCnssPensionsPatronal > 0m
                    ? bareme.TauxCnssPensionsPatronal : reference.TauxPensionsPatronal,
                PlafondMensuel = bareme.PlafondCnssMensuel > 0m
                    ? bareme.PlafondCnssMensuel : reference.PlafondMensuel,
                TauxTaxePatronale = bareme.TauxTaxePatronaleApprentissage > 0m
                    ? bareme.TauxTaxePatronaleApprentissage : reference.TauxTaxePatronale,
                TauxMaxIndemniteLogement = bareme.TauxMaxIndemniteLogement > 0m
                    ? bareme.TauxMaxIndemniteLogement : reference.TauxMaxIndemniteLogement,
                PlafondIndemniteLogement = bareme.PlafondIndemniteLogement > 0m
                    ? bareme.PlafondIndemniteLogement : reference.PlafondIndemniteLogement,
                TauxMaxIndemniteFonction = bareme.TauxMaxIndemniteFonction > 0m
                    ? bareme.TauxMaxIndemniteFonction : reference.TauxMaxIndemniteFonction,
                PlafondIndemniteFonction = bareme.PlafondIndemniteFonction > 0m
                    ? bareme.PlafondIndemniteFonction : reference.PlafondIndemniteFonction,
                TauxMaxIndemniteTransport = bareme.TauxMaxIndemniteTransport > 0m
                    ? bareme.TauxMaxIndemniteTransport : reference.TauxMaxIndemniteTransport,
                PlafondIndemniteTransport = bareme.PlafondIndemniteTransport > 0m
                    ? bareme.PlafondIndemniteTransport : reference.PlafondIndemniteTransport,
                AbattementCategoriesSuperieures = bareme.AbattementCategoriesSuperieures > 0m
                    ? bareme.AbattementCategoriesSuperieures : reference.AbattementCategoriesSuperieures,
                AbattementAutresCategories = bareme.AbattementAutresCategories > 0m
                    ? bareme.AbattementAutresCategories : reference.AbattementAutresCategories,
                Tranches = TranchesValides(bareme.Tranches)
                    ? ConvertirTranches(bareme.Tranches) : reference.Tranches,
                ChargesFamille = FusionnerChargesFamille(bareme.ChargesFamille, reference.ChargesFamille),
                Origine = bareme
            };
        }

        /// <summary>Somme des trois branches patronales : la part patronale globale.</summary>
        public decimal TauxPatronalTotal
        {
            get { return TauxPrestationsFamiliales + TauxRisquesProfessionnels + TauxPensionsPatronal; }
        }

        /// <summary>Assiette cotisable : le brut, ramené au plafond s'il le dépasse.</summary>
        public decimal Assiette(decimal salaireBrut)
        {
            if (PlafondMensuel <= 0m) return salaireBrut;
            return salaireBrut > PlafondMensuel ? PlafondMensuel : salaireBrut;
        }

        /// <summary>Vrai si ces paramètres diffèrent des valeurs historiques du moteur.</summary>
        public bool DiffereDesHistoriques
        {
            get
            {
                var h = Historiques();
                return TauxCnssSalarie != h.TauxCnssSalarie
                    || TauxPrestationsFamiliales != h.TauxPrestationsFamiliales
                    || TauxRisquesProfessionnels != h.TauxRisquesProfessionnels
                    || TauxPensionsPatronal != h.TauxPensionsPatronal
                    || PlafondMensuel != h.PlafondMensuel
                    || TauxTaxePatronale != h.TauxTaxePatronale
                    || TauxMaxIndemniteLogement != h.TauxMaxIndemniteLogement
                    || PlafondIndemniteLogement != h.PlafondIndemniteLogement
                    || TauxMaxIndemniteFonction != h.TauxMaxIndemniteFonction
                    || PlafondIndemniteFonction != h.PlafondIndemniteFonction
                    || TauxMaxIndemniteTransport != h.TauxMaxIndemniteTransport
                    || PlafondIndemniteTransport != h.PlafondIndemniteTransport
                    || AbattementCategoriesSuperieures != h.AbattementCategoriesSuperieures
                    || AbattementAutresCategories != h.AbattementAutresCategories
                    || !TranchesEgales(Tranches, h.Tranches)
                    || !ChargesFamilleEgales(ChargesFamille, h.ChargesFamille);
            }
        }

        private static bool TranchesEgales(List<TaxBracket> a, List<TaxBracket> b)
        {
            if (a == null || b == null) return a == b;
            if (a.Count != b.Count) return false;

            for (int i = 0; i < a.Count; i++)
            {
                if (a[i].LowerBound != b[i].LowerBound
                    || a[i].UpperBound != b[i].UpperBound
                    || a[i].Rate != b[i].Rate)
                    return false;
            }
            return true;
        }

        private static bool ChargesFamilleEgales(Dictionary<int, decimal> a, Dictionary<int, decimal> b)
        {
            if (a == null || b == null) return a == b;
            if (a.Count != b.Count) return false;

            foreach (var charges in a.Keys)
            {
                decimal valeur;
                if (!b.TryGetValue(charges, out valeur) || valeur != a[charges]) return false;
            }
            return true;
        }

        /// <summary>Description lisible de ce qui s'écarte des valeurs historiques.</summary>
        public string Ecarts()
        {
            var h = Historiques();
            var differences = new System.Collections.Generic.List<string>();

            if (TauxCnssSalarie != h.TauxCnssSalarie)
                differences.Add(string.Format("CNSS salarié {0:P2} au lieu de {1:P2}", TauxCnssSalarie, h.TauxCnssSalarie));
            if (TauxPrestationsFamiliales != h.TauxPrestationsFamiliales)
                differences.Add(string.Format("prestations familiales {0:P2} au lieu de {1:P2}", TauxPrestationsFamiliales, h.TauxPrestationsFamiliales));
            if (TauxRisquesProfessionnels != h.TauxRisquesProfessionnels)
                differences.Add(string.Format("risques professionnels {0:P2} au lieu de {1:P2}", TauxRisquesProfessionnels, h.TauxRisquesProfessionnels));
            if (TauxPensionsPatronal != h.TauxPensionsPatronal)
                differences.Add(string.Format("pensions patronales {0:P2} au lieu de {1:P2}", TauxPensionsPatronal, h.TauxPensionsPatronal));
            if (PlafondMensuel != h.PlafondMensuel)
                differences.Add(string.Format("plafond {0:N0} au lieu de {1:N0}", PlafondMensuel, h.PlafondMensuel));
            if (TauxTaxePatronale != h.TauxTaxePatronale)
                differences.Add(string.Format("TPA {0:P2} au lieu de {1:P2}", TauxTaxePatronale, h.TauxTaxePatronale));
            if (TauxMaxIndemniteLogement != h.TauxMaxIndemniteLogement || PlafondIndemniteLogement != h.PlafondIndemniteLogement)
                differences.Add(string.Format("logement exonéré {0:P0}/{1:N0} au lieu de {2:P0}/{3:N0}",
                    TauxMaxIndemniteLogement, PlafondIndemniteLogement, h.TauxMaxIndemniteLogement, h.PlafondIndemniteLogement));
            if (TauxMaxIndemniteFonction != h.TauxMaxIndemniteFonction || PlafondIndemniteFonction != h.PlafondIndemniteFonction)
                differences.Add(string.Format("fonction exonérée {0:P0}/{1:N0} au lieu de {2:P0}/{3:N0}",
                    TauxMaxIndemniteFonction, PlafondIndemniteFonction, h.TauxMaxIndemniteFonction, h.PlafondIndemniteFonction));
            if (TauxMaxIndemniteTransport != h.TauxMaxIndemniteTransport || PlafondIndemniteTransport != h.PlafondIndemniteTransport)
                differences.Add(string.Format("transport exonéré {0:P0}/{1:N0} au lieu de {2:P0}/{3:N0}",
                    TauxMaxIndemniteTransport, PlafondIndemniteTransport, h.TauxMaxIndemniteTransport, h.PlafondIndemniteTransport));
            if (AbattementCategoriesSuperieures != h.AbattementCategoriesSuperieures)
                differences.Add(string.Format("abattement catégories supérieures {0:P0} au lieu de {1:P0}", AbattementCategoriesSuperieures, h.AbattementCategoriesSuperieures));
            if (AbattementAutresCategories != h.AbattementAutresCategories)
                differences.Add(string.Format("abattement autres catégories {0:P0} au lieu de {1:P0}", AbattementAutresCategories, h.AbattementAutresCategories));
            if (!TranchesEgales(Tranches, h.Tranches))
                differences.Add(string.Format("barème IUTS à {0} tranche(s) au lieu de {1}", Tranches.Count, h.Tranches.Count));
            if (!ChargesFamilleEgales(ChargesFamille, h.ChargesFamille))
                differences.Add("réductions pour charges de famille différentes des valeurs historiques");

            return differences.Count == 0
                ? "Identiques aux valeurs historiques du moteur."
                : string.Join(" ; ", differences);
        }

        public string Designation
        {
            get
            {
                return Origine == null
                    ? "Valeurs historiques du moteur"
                    : Origine.Designation;
            }
        }
    }
}
