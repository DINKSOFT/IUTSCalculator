using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace IUTSCalculator.Converters
{
    /// <summary>
    /// Convertisseur pour transformer un booléen en texte conditionnel
    /// </summary>
    public class BooleanToTextConverter : IValueConverter
    {
        /// <summary>
        /// Convertit une valeur booléenne en texte selon les paramètres fournis
        /// </summary>
        /// <param name="value">La valeur booléenne à convertir</param>
        /// <param name="targetType">Le type cible</param>
        /// <param name="parameter">Paramètre au format "text:ValeurSiTrue;ValeurSiFalse"</param>
        /// <param name="culture">Culture à utiliser</param>
        /// <returns>Le texte correspondant à la valeur booléenne</returns>
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (!(value is bool boolValue))
                return string.Empty;

            string paramStr = parameter?.ToString() ?? string.Empty;

            // Inverser la logique si demandé
            if (paramStr.Contains("invert"))
            {
                boolValue = !boolValue;
            }

            // Si le paramètre contient "text:", alors on retourne du texte selon la valeur
            if (paramStr.StartsWith("text:"))
            {
                string[] parts = paramStr.Substring(5).Split(';');
                if (parts.Length == 2)
                {
                    return boolValue ? parts[0] : parts[1];
                }
            }

            return boolValue.ToString();
        }

        /// <summary>
        /// Convertit une chaîne en booléen (non implémenté)
        /// </summary>
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            // La conversion inverse n'est pas implémentée pour ce convertisseur
            return DependencyProperty.UnsetValue;
        }
    }
}