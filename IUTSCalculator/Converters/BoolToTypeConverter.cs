using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace IUTSCalculator.Converters
{
    /// <summary>
    /// Convertisseur pour transformer un booléen IsAnnual en texte "Annuel" ou "Mensuel"
    /// </summary>
    public class BoolToTypeConverter : IValueConverter
    {
        /// <summary>
        /// Convertit une valeur booléenne en texte
        /// </summary>
        /// <param name="value">La valeur à convertir (booléen indiquant si annuel)</param>
        /// <param name="targetType">Le type cible</param>
        /// <param name="parameter">Paramètre non utilisé</param>
        /// <param name="culture">Culture à utiliser</param>
        /// <returns>"Annuel" si true, "Mensuel" si false, "Inconnu" sinon</returns>
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool isAnnual)
            {
                return isAnnual ? "Annuel" : "Mensuel";
            }
            return "Inconnu";
        }

        /// <summary>
        /// Convertit une valeur texte en booléen
        /// </summary>
        /// <param name="value">La valeur à convertir</param>
        /// <param name="targetType">Le type cible</param>
        /// <param name="parameter">Paramètre non utilisé</param>
        /// <param name="culture">Culture à utiliser</param>
        /// <returns>true si "Annuel", false sinon</returns>
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is string stringValue)
            {
                return stringValue == "Annuel";
            }
            return DependencyProperty.UnsetValue;
        }
    }
}