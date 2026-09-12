using IUTSCalculator.Models;

namespace IUTSCalculator.ViewModels
{
    /// <summary>
    /// Associe une valeur de <see cref="BulletinTemplate"/> à son libellé lisible, pour
    /// l'affichage dans le sélecteur de modèle de bulletin (ComboBox).
    /// </summary>
    public class BulletinTemplateOption
    {
        public BulletinTemplate Value { get; set; }
        public string Label { get; set; }

        public static System.Collections.Generic.List<BulletinTemplateOption> TousLesModeles => new System.Collections.Generic.List<BulletinTemplateOption>
        {
            new BulletinTemplateOption { Value = BulletinTemplate.Detaille, Label = "Détaillé (par défaut)" },
            new BulletinTemplateOption { Value = BulletinTemplate.Condense, Label = "Condensé (demi-page)" },
            new BulletinTemplateOption { Value = BulletinTemplate.CumulsAnnuels, Label = "Avec cumuls annuels" },
            new BulletinTemplateOption { Value = BulletinTemplate.Bilingue, Label = "Bilingue Français / Anglais" }
        };
    }
}
