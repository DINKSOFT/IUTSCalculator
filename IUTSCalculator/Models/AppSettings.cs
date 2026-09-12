using System;
using System.Collections.Generic;

namespace IUTSCalculator.Models
{
    /// <summary>
    /// Paramètres de configuration de l'application IUTSCalculator
    /// </summary>
    public class AppSettings
    {
        /// <summary>
        /// Thème de l'interface utilisateur (Light, Dark, System)
        /// </summary>
        public string Theme { get; set; } = "Light";

        /// <summary>
        /// Code de langue et culture de l'application (ex: fr-FR, en-US)
        /// </summary>
        public string Language { get; set; } = "fr-FR";

        /// <summary>
        /// Indique si la sauvegarde automatique est activée
        /// </summary>
        public bool AutoSave { get; set; } = true;

        /// <summary>
        /// Liste des catégories d'employés disponibles
        /// </summary>
        public List<string> EmployeeCategories { get; set; } = new List<string>
        {
            "Cadre Supérieur", "Cadre Inférieur"
        };

        /// <summary>
        /// Répertoire d'exportation des fichiers
        /// </summary>
        public string ExportDirectory { get; set; } = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);

        /// <summary>
        /// Liste des tranches d'imposition
        /// </summary>
        public List<TaxBracket> TaxBrackets { get; set; } = new List<TaxBracket>
        {
            new TaxBracket { LowerBound = 0, UpperBound = 30000, Rate = 0.0m },
            new TaxBracket { LowerBound = 30100, UpperBound = 50000, Rate = 0.121m },
            new TaxBracket { LowerBound = 50100, UpperBound = 80000, Rate = 0.139m },
            new TaxBracket { LowerBound = 80100, UpperBound = 120000, Rate = 0.157m },
            new TaxBracket { LowerBound = 120100, UpperBound = 170000, Rate = 0.184m },
            new TaxBracket { LowerBound = 170100, UpperBound = 250000, Rate = 0.217m },
            new TaxBracket { LowerBound = 250100, UpperBound = decimal.MaxValue, Rate = 0.25m }
        };

        /// <summary>
        /// Liste des abattements pour charges familiales
        /// </summary>
        public List<FamilyCharge> FamilyCharges { get; set; } = new List<FamilyCharge>
        {
            new FamilyCharge { ChargeCount = 0, AbatementPercentage = 0.0m },
            new FamilyCharge { ChargeCount = 1, AbatementPercentage = 0.05m },
            new FamilyCharge { ChargeCount = 2, AbatementPercentage = 0.10m },
            new FamilyCharge { ChargeCount = 3, AbatementPercentage = 0.15m },
            new FamilyCharge { ChargeCount = 4, AbatementPercentage = 0.20m },
            new FamilyCharge { ChargeCount = 5, AbatementPercentage = 0.25m }
        };

        /// <summary>
        /// Date de la dernière mise à jour des paramètres
        /// </summary>
        public DateTime LastUpdated { get; set; } = DateTime.Now;
    }
}