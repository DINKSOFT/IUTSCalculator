using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace IUTSCalculator.Converters
{
    /// <summary>
    /// Convertisseur booléen polyvalent, piloté par le type cible et par
    /// <c>ConverterParameter</c>.
    ///
    /// Il ne savait à l'origine produire que le libellé « Annuel » / « Mensuel », et
    /// ignorait purement et simplement son paramètre. Les vues l'utilisaient pourtant
    /// déjà pour piloter des visibilités (<c>Visibility="{Binding IsRegularization,
    /// Converter=...}"</c>), des cases à cocher inversées (<c>ConverterParameter=invert</c>)
    /// et des messages de statut (<c>ConverterParameter=text:...;...</c>) : dans ces trois
    /// cas la conversion échouait en silence, si bien que des panneaux censés être
    /// conditionnels restaient toujours affichés et que la barre d'état annonçait
    /// « Mensuel » au lieu de « Prêt ».
    ///
    /// Les usages corrects existants — la colonne « Type » de l'historique, qui attend
    /// bien une chaîne — se comportent exactement comme avant.
    /// </summary>
    public class BoolToTypeConverter : IValueConverter
    {
        private const string ParamInvert = "invert";
        private const string PrefixeTexte = "text:";

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            bool drapeau = (value is bool) && (bool)value;
            string param = parameter as string;

            // « invert » : on inverse le booléen, puis on applique la règle habituelle.
            if (!string.IsNullOrEmpty(param) &&
                param.Equals(ParamInvert, StringComparison.OrdinalIgnoreCase))
            {
                drapeau = !drapeau;
                param = null;
            }

            // « text:SiVrai;SiFaux » : deux libellés au choix.
            if (!string.IsNullOrEmpty(param) &&
                param.StartsWith(PrefixeTexte, StringComparison.OrdinalIgnoreCase))
            {
                string[] parties = param.Substring(PrefixeTexte.Length).Split(';');
                string siVrai = parties.Length > 0 ? parties[0] : string.Empty;
                string siFaux = parties.Length > 1 ? parties[1] : string.Empty;
                return drapeau ? siVrai : siFaux;
            }

            if (targetType == typeof(Visibility) || targetType == typeof(Visibility?))
                return drapeau ? Visibility.Visible : Visibility.Collapsed;

            if (targetType == typeof(bool) || targetType == typeof(bool?))
                return drapeau;

            // Usage historique : libellé du type de calcul.
            return drapeau ? "Annuel" : "Mensuel";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            string param = parameter as string;
            bool inverser = !string.IsNullOrEmpty(param) &&
                            param.Equals(ParamInvert, StringComparison.OrdinalIgnoreCase);

            if (value is bool)
            {
                bool b = (bool)value;
                return inverser ? !b : b;
            }

            if (value is Visibility)
            {
                bool visible = ((Visibility)value) == Visibility.Visible;
                return inverser ? !visible : visible;
            }

            if (value is string)
            {
                bool annuel = ((string)value) == "Annuel";
                return inverser ? !annuel : annuel;
            }

            return DependencyProperty.UnsetValue;
        }
    }
}
