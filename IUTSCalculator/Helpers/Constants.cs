using System;
using System.Collections.Generic;
using System.IO;

namespace IUTSCalculator.Helpers
{
    /// <summary>
    /// Classe contenant toutes les constantes utilisées dans l'application
    /// </summary>
    public static class Constants
    {
        #region Chemins de fichiers et dossiers

        /// <summary>
        /// Dossier de données de l'application
        /// </summary>
        public static readonly string AppDataFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "IUTSCalculator");

        /// <summary>
        /// Chemin du fichier de configuration de l'application
        /// </summary>
        public static readonly string SettingsFilePath = Path.Combine(AppDataFolder, "settings.json");

        /// <summary>
        /// Chemin du fichier contenant l'historique des calculs
        /// </summary>
        public static readonly string HistoryFilePath = Path.Combine(AppDataFolder, "history.json");

        /// <summary>
        /// Chemin du fichier contenant les tranches d'imposition
        /// </summary>
        public static readonly string TaxBracketsFilePath = Path.Combine(AppDataFolder, "taxbrackets.json");

        /// <summary>
        /// Chemin du fichier contenant les charges familiales
        /// </summary>
        public static readonly string FamilyChargesFilePath = Path.Combine(AppDataFolder, "familycharges.json");

        /// <summary>
        /// Dossier par défaut pour l'exportation des fichiers PDF
        /// </summary>
        public static readonly string DefaultExportFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "IUTSCalculator");

        #endregion

        #region Valeurs par défaut des paramètres

        /// <summary>
        /// Nombre maximal d'éléments dans l'historique
        /// </summary>
        public const int MaxHistoryItems = 100;

        /// <summary>
        /// Indique si le thème sombre est activé par défaut
        /// </summary>
        public const bool DefaultDarkThemeEnabled = false;

        /// <summary>
        /// Indique si l'enregistrement automatique est activé par défaut
        /// </summary>
        public const bool DefaultAutoSaveEnabled = true;

        /// <summary>
        /// Nombre de décimales à afficher pour les montants monétaires
        /// </summary>
        public const int DecimalPrecision = 2;

        /// <summary>
        /// Format des montants monétaires
        /// </summary>
        public const string MoneyFormat = "#,##0.00 FCFA";

        /// <summary>
        /// Format des pourcentages
        /// </summary>
        public const string PercentageFormat = "0.##%";

        #endregion

        #region Tranches d'imposition par défaut

        /// <summary>
        /// Tranches d'imposition par défaut
        /// </summary>
        public static readonly List<(decimal LowerBound, decimal UpperBound, decimal Rate)> DefaultTaxBrackets = new List<(decimal, decimal, decimal)>
        {
            (0m, 25000m, 0.00m),
            (25001m, 50000m, 0.05m),
            (50001m, 150000m, 0.10m),
            (150001m, 300000m, 0.15m),
            (300001m, decimal.MaxValue, 0.20m)
        };

        /// <summary>
        /// Charges familiales par défaut (nombre de personnes à charge, taux de déduction)
        /// </summary>
        public static readonly List<(int DependentCount, decimal DeductionRate)> DefaultFamilyCharges = new List<(int, decimal)>
        {
            (0, 0.00m),
            (1, 0.05m),
            (2, 0.10m),
            (3, 0.15m),
            (4, 0.20m),
            (5, 0.25m)
        };

        #endregion

        #region Clés de ressources

        /// <summary>
        /// Clé de ressource pour le thème clair
        /// </summary>
        public const string LightThemeResourceKey = "/Resources/LightTheme.xaml";

        /// <summary>
        /// Clé de ressource pour le thème sombre
        /// </summary>
        public const string DarkThemeResourceKey = "/Resources/DarkTheme.xaml";

        /// <summary>
        /// Clé de ressource pour les styles communs
        /// </summary>
        public const string CommonStylesResourceKey = "/Resources/Styles.xaml";

        #endregion

        #region Messages d'erreur et validations

        /// <summary>
        /// Message d'erreur pour un salaire brut invalide
        /// </summary>
        public const string InvalidSalaryMessage = "Le salaire brut doit être un montant positif valide.";

        /// <summary>
        /// Message d'erreur pour un nombre de personnes à charge invalide
        /// </summary>
        public const string InvalidDependentsMessage = "Le nombre de personnes à charge doit être un nombre entier positif.";

        /// <summary>
        /// Message d'erreur pour un taux d'imposition invalide
        /// </summary>
        public const string InvalidTaxRateMessage = "Le taux d'imposition doit être compris entre 0 et 1.";

        /// <summary>
        /// Message d'erreur pour un seuil d'imposition invalide
        /// </summary>
        public const string InvalidTaxBracketMessage = "Les seuils d'imposition doivent être des montants positifs et en ordre croissant.";

        /// <summary>
        /// Message d'erreur pour une charge familiale invalide
        /// </summary>
        public const string InvalidFamilyChargeMessage = "Le taux de déduction pour charges familiales doit être compris entre 0 et 1.";

        /// <summary>
        /// Message d'erreur pour un accès au fichier impossible
        /// </summary>
        public const string FileAccessErrorMessage = "Impossible d'accéder au fichier {0}. Vérifiez vos permissions.";

        /// <summary>
        /// Message d'erreur pour une erreur de chargement des données
        /// </summary>
        public const string DataLoadErrorMessage = "Erreur lors du chargement des données : {0}";

        /// <summary>
        /// Message d'erreur pour une erreur de sauvegarde des données
        /// </summary>
        public const string DataSaveErrorMessage = "Erreur lors de la sauvegarde des données : {0}";

        /// <summary>
        /// Message d'erreur pour une erreur d'exportation PDF
        /// </summary>
        public const string PdfExportErrorMessage = "Erreur lors de l'exportation en PDF : {0}";

        #endregion

        #region Titres et noms d'affichage

        /// <summary>
        /// Titre de l'application
        /// </summary>
        public const string AppTitle = "Calculateur IUTS";

        /// <summary>
        /// Nom d'affichage pour l'onglet de calcul
        /// </summary>
        public const string CalculationTabName = "Calcul";

        /// <summary>
        /// Nom d'affichage pour l'onglet d'historique
        /// </summary>
        public const string HistoryTabName = "Historique";

        /// <summary>
        /// Nom d'affichage pour l'onglet de paramètres
        /// </summary>
        public const string SettingsTabName = "Paramètres";

        /// <summary>
        /// Texte pour le bouton de calcul
        /// </summary>
        public const string CalculateButtonText = "Calculer";

        /// <summary>
        /// Texte pour le bouton d'exportation
        /// </summary>
        public const string ExportButtonText = "Exporter en PDF";

        /// <summary>
        /// Texte pour le bouton de sauvegarde
        /// </summary>
        public const string SaveButtonText = "Enregistrer";

        /// <summary>
        /// Texte pour le bouton d'effacement de l'historique
        /// </summary>
        public const string ClearHistoryButtonText = "Effacer l'historique";

        #endregion
    }
}