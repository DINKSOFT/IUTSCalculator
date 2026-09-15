using System.Collections.Generic;
using System.Linq;
using IUTSCalculator.Services;

namespace IUTSCalculator.Models
{
    /// <summary>
    /// Ce que les rappels et régularisations du mois apportent au bulletin de paie ouvert (module PAI-07).
    /// </summary>
    public sealed class ApportRappels
    {
        private readonly List<LigneApport> _lignes = new List<LigneApport>();
        private readonly List<int> _rappelIds = new List<int>();

        public IReadOnlyList<LigneApport> Lignes => _lignes;
        public IReadOnlyList<int> RappelIds => _rappelIds;

        public decimal GainsImposablesEtCotisables { get; private set; }
        public decimal GainsImposablesSeulement { get; private set; }
        public decimal GainsCotisablesSeulement { get; private set; }
        public decimal GainsNonSoumis { get; private set; }
        public decimal GainsExoneresTPA { get; private set; }
        public decimal Retenues { get; private set; }

        public bool EstVide => _lignes.Count == 0 &&
                               GainsImposablesEtCotisables == 0m &&
                               GainsImposablesSeulement == 0m &&
                               GainsCotisablesSeulement == 0m &&
                               GainsNonSoumis == 0m &&
                               Retenues == 0m;

        public ApportRappels()
        {
        }

        public void AjouterLigne(
            string codeRubrique,
            string libelle,
            SensRubrique sens,
            decimal montant,
            bool soumisIUTS,
            bool soumisCNSS,
            bool soumisTPA,
            int? rappelId = null)
        {
            if (montant == 0m) return;

            if (rappelId.HasValue && !_rappelIds.Contains(rappelId.Value))
                _rappelIds.Add(rappelId.Value);

            if (sens == SensRubrique.Gain)
            {
                if (soumisIUTS && soumisCNSS)
                    GainsImposablesEtCotisables += montant;
                else if (soumisIUTS)
                    GainsImposablesSeulement += montant;
                else if (soumisCNSS)
                    GainsCotisablesSeulement += montant;
                else
                    GainsNonSoumis += montant;

                if (!soumisTPA)
                    GainsExoneresTPA += montant;
            }
            else
            {
                Retenues += montant;
            }

            _lignes.Add(new LigneApport(
                codeRubrique,
                libelle,
                sens,
                0m,
                montant,
                soumisIUTS,
                soumisCNSS,
                soumisTPA));
        }

        public static ApportRappels Aucun() => new ApportRappels();
    }
}
