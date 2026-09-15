using System;
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

        /// <summary>Barème dont ces paramètres sont issus, s'il y en a un.</summary>
        public BaremeLegal Origine { get; private set; }

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
                Origine = null
            };
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
                    || TauxTaxePatronale != h.TauxTaxePatronale;
            }
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
