using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace IUTSCalculator.Converters
{
    /// <summary>
    /// Convertisseur pour transformer un booléen en Visibility
    /// </summary>
    public class BooleanToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            bool boolValue = (bool)value;
            
            // Si le paramètre contient "invert", inverser la logique
            if (parameter != null && parameter.ToString().Contains("invert"))
            {
                boolValue = !boolValue;
            }
            
            // Si le paramètre contient "text:", alors on retourne du texte selon la valeur
            if (parameter != null && parameter.ToString().StartsWith("text:"))
            {
                string[] parts = parameter.ToString().Substring(5).Split(';');
                if (parts.Length == 2)
                {
                    return boolValue ? parts[0] : parts[1];
                }
            }
            
            return boolValue ? Visibility.Visible : Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is Visibility visibility)
            {
                bool result = visibility == Visibility.Visible;
                
                // Si le paramètre contient "invert", inverser la logique
                if (parameter != null && parameter.ToString().Contains("invert"))
                {
                    result = !result;
                }
                
                return result;
            }
            
            return false;
        }
    }

    /// <summary>
    /// Convertisseur pour formater un décimal en chaîne selon différents formats
    /// </summary>
    public class DecimalToStringConverter : IValueConverter
    {
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

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            string stringValue = value as string;
            if (string.IsNullOrWhiteSpace(stringValue))
            {
                return 0m;
            }
            
            // Supprimer les espaces et autres séparateurs non numériques
            stringValue = stringValue.Replace(" ", "").Replace(",", ".");
            
            if (decimal.TryParse(stringValue, NumberStyles.Any, culture, out decimal result))
            {
                return result;
            }
            
            return 0m;
        }
    }
}