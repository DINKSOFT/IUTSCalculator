using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace IUTSCalculator.Converters
{
    /// <summary>
    /// Convertisseur pour formater un décimal en chaîne selon différents formats
    /// </summary>
    public class DecimalToStringConverter : IValueConverter
    {
        /// <summary>
        /// Convertit une valeur décimale en chaîne formatée
        /// </summary>
        /// <param name="value">La valeur à convertir</param>
        /// <param name="targetType">Le type cible</param>
        /// <param name="parameter">Format à utiliser (par défaut "N0")</param>
        /// <param name="culture">Culture à utiliser pour le formatage</param>
        /// <returns>La chaîne formatée ou une chaîne vide si la conversion échoue</returns>
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value == null) return string.Empty;

            decimal decimalValue;
            if (value is decimal val)
            {
                decimalValue = val;
            }
            else if (decimal.TryParse(value.ToString(), out decimal parsedValue))
            {
                decimalValue = parsedValue;
            }
            else
            {
                return string.Empty;
            }

            string format = parameter as string ?? "N0";
            return decimalValue.ToString(format, culture);
        }

        /// <summary>
        /// Convertit une chaîne formatée en valeur décimale
        /// </summary>
        /// <param name="value">La chaîne à convertir</param>
        /// <param name="targetType">Le type cible</param>
        /// <param name="parameter">Paramètre non utilisé</param>
        /// <param name="culture">Culture à utiliser pour l'analyse</param>
        /// <returns>La valeur décimale ou DependencyProperty.UnsetValue si la conversion échoue</returns>
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (!(value is string stringValue) || string.IsNullOrWhiteSpace(stringValue))
            {
                return DependencyProperty.UnsetValue;
            }

            // Supprimer les espaces et autres séparateurs non numériques
            stringValue = stringValue.Replace(" ", "").Replace(",", ".");

            if (decimal.TryParse(stringValue, NumberStyles.Any, culture, out decimal result))
            {
                return result;
            }

            return DependencyProperty.UnsetValue;
        }
    }
}