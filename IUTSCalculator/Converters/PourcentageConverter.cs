using System;
using System.Globalization;
using System.Windows.Data;

namespace IUTSCalculator.Converters
{
    /// <summary>
    /// Affiche une proportion comme un pourcentage, et sait la relire.
    ///
    /// <c>StringFormat=P2</c> ne convient pas à un champ de SAISIE : le format n'est
    /// appliqué qu'à l'affichage, et la relecture repasse par le convertisseur par défaut
    /// du type, qui ne sait pas quoi faire de « 5,50 % ». Le champ prend alors une bordure
    /// rouge et la valeur saisie n'atteint jamais le modèle — l'utilisateur croit avoir
    /// modifié un taux qui, en réalité, n'a pas bougé.
    ///
    /// Ici la conversion fonctionne dans les deux sens : 0,055 s'affiche « 5,50 % », et
    /// « 5,5 », « 5,5 % » ou « 5,5% » redonnent 0,055.
    ///
    /// <c>ConverterParameter</c> fixe le nombre de décimales affichées (2 par défaut).
    /// </summary>
    public class PourcentageConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value == null) return string.Empty;

            decimal proportion;
            try
            {
                proportion = System.Convert.ToDecimal(value, CultureInfo.InvariantCulture);
            }
            catch
            {
                return string.Empty;
            }

            return (proportion * 100m).ToString("N" + Decimales(parameter), Culture(culture)) + " %";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            string texte = value as string;
            if (string.IsNullOrWhiteSpace(texte))
                return 0m;

            // On retire le signe, les espaces ordinaires et les espaces insécables que le
            // formatage a pu insérer comme séparateur de milliers.
            texte = texte.Replace("%", "")
                         .Replace(" ", "")
                         .Replace(" ", "")
                         .Replace(" ", "")
                         .Trim();

            decimal saisie;
            var styles = NumberStyles.Number;
            var culturePreferee = Culture(culture);

            if (!decimal.TryParse(texte, styles, culturePreferee, out saisie) &&
                !decimal.TryParse(texte, styles, CultureInfo.InvariantCulture, out saisie))
            {
                // Saisie illisible : on laisse la liaison échouer proprement plutôt que
                // d'écraser le taux par zéro.
                return Binding.DoNothing;
            }

            return saisie / 100m;
        }

        private static int Decimales(object parameter)
        {
            int decimales;
            if (parameter != null &&
                int.TryParse(parameter.ToString(), NumberStyles.Integer,
                             CultureInfo.InvariantCulture, out decimales) &&
                decimales >= 0 && decimales <= 6)
            {
                return decimales;
            }

            return 2;
        }

        private static CultureInfo Culture(CultureInfo culture)
        {
            return culture == null || culture.Equals(CultureInfo.InvariantCulture)
                ? CultureInfo.CurrentCulture
                : culture;
        }
    }

    /// <summary>
    /// Affiche un montant en FCFA avec séparateur de milliers, et sait le relire.
    ///
    /// Même raison d'être que <see cref="PourcentageConverter"/> : <c>StringFormat=N0</c>
    /// sur un champ de saisie affiche « 800 000 » mais ne sait pas le relire, parce que
    /// l'espace insérée comme séparateur n'est pas celle que l'utilisateur tape.
    /// </summary>
    public class MontantConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value == null) return string.Empty;

            try
            {
                decimal montant = System.Convert.ToDecimal(value, CultureInfo.InvariantCulture);
                return montant.ToString("N0", Culture(culture));
            }
            catch
            {
                return string.Empty;
            }
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            string texte = value as string;
            if (string.IsNullOrWhiteSpace(texte))
                return 0m;

            texte = texte.Replace(" ", "")
                         .Replace(" ", "")
                         .Replace(" ", "")
                         .Trim();

            decimal saisie;
            var styles = NumberStyles.Number;

            if (!decimal.TryParse(texte, styles, Culture(culture), out saisie) &&
                !decimal.TryParse(texte, styles, CultureInfo.InvariantCulture, out saisie))
            {
                return Binding.DoNothing;
            }

            return saisie;
        }

        private static CultureInfo Culture(CultureInfo culture)
        {
            return culture == null || culture.Equals(CultureInfo.InvariantCulture)
                ? CultureInfo.CurrentCulture
                : culture;
        }
    }
}
