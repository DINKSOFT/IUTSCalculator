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
        /// Date de la dernière mise à jour des paramètres
        /// </summary>
        public DateTime LastUpdated { get; set; } = DateTime.Now;
    }
}