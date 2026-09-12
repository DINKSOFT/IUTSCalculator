namespace IUTSCalculator.Models
{
    /// <summary>
    /// Modèle de mise en page utilisé pour générer et exporter un bulletin de paie.
    /// Les données du bulletin (<see cref="Bulletin"/>) sont toujours les mêmes ; seule la
    /// présentation change d'un modèle à l'autre.
    /// </summary>
    public enum BulletinTemplate
    {
        /// <summary>Bulletin détaillé complet (modèle de référence "Option 4"). Modèle par défaut.</summary>
        Detaille,

        /// <summary>Bulletin condensé sur une demi-page, ne conservant que les totaux essentiels.</summary>
        Condense,

        /// <summary>Bulletin détaillé complété par les cumuls depuis le 1er janvier pour chaque rubrique.</summary>
        CumulsAnnuels,

        /// <summary>Bulletin bilingue français / anglais.</summary>
        Bilingue
    }
}
