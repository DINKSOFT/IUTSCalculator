using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace IUTSCalculator.Converters
{
    /// <summary>
    /// Convertisseur pour comparer une chaîne avec une valeur spécifiée
    /// </summary>
    public class StringEqualsConverter : IValueConverter
    {
        /// <summary>
        /// Convertit une valeur string en booléen en la comparant avec le paramètre
        /// </summary>
        /// <param name="value">La valeur à comparer</param>
        /// <param name="targetType">Le type cible</param>
        /// <param name="parameter">La valeur à comparer avec value</param>
        /// <param name="culture">Culture à utiliser</param>
        /// <returns>true si les chaînes sont égales, false sinon</returns>
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value == null || parameter == null)
                return false;

            string valueString = value.ToString();
            string parameterString = parameter.ToString();

            return valueString.Equals(parameterString, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Convertit une valeur booléenne en chaîne
        /// </summary>
        /// <param name="value">La valeur à convertir</param>
        /// <param name="targetType">Le type cible</param>
        /// <param name="parameter">La chaîne à retourner si la valeur est true</param>
        /// <param name="culture">Culture à utiliser</param>
        /// <returns>La chaîne spécifiée en paramètre si true, DependencyProperty.UnsetValue sinon</returns>
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool boolValue && boolValue && parameter != null)
            {
                return parameter.ToString();
            }

            return DependencyProperty.UnsetValue;
        }
    }
}