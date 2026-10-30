using System;
using System.Globalization;
using System.Windows.Data;

namespace IUTSCalculator.Converters
{
    /// <summary>
    /// Retranche d'une dimension (ActualHeight, ActualWidth...) les dimensions suivantes
    /// d'une MultiBinding, puis le paramètre. Sert à plafonner une zone défilante à la
    /// place réellement laissée par ses voisines.
    /// </summary>
    public class SubtractConverter : IValueConverter, IMultiValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return Convert(new[] { value }, targetType, parameter, culture);
        }

        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values == null || values.Length == 0 || !EstMesure(values[0], out double taille))
                return double.PositiveInfinity; // carte pas encore mesurée : ne rien plafonner

            for (int i = 1; i < values.Length; i++)
            {
                if (values[i] is double voisin && !double.IsNaN(voisin) && !double.IsInfinity(voisin))
                    taille -= voisin;
            }

            if (parameter != null
                && double.TryParse(parameter.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out double retrait))
                taille -= retrait;

            return Math.Max(0, taille);
        }

        private static bool EstMesure(object value, out double taille)
        {
            taille = value is double d ? d : 0;
            return !double.IsNaN(taille) && !double.IsInfinity(taille) && taille > 0;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}
