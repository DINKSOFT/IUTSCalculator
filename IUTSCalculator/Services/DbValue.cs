using System;
using System.Globalization;

namespace IUTSCalculator.Services
{
    /// <summary>
    /// Conversions entre les valeurs .NET et leur représentation en base.
    ///
    /// SQLite ne possède pas de type décimal : une colonne déclarée REAL stocke un
    /// flottant IEEE-754, et un montant comme 133 333,33 n'y est pas représentable
    /// exactement. Pour une application de paie auditée au franc près, c'est le mauvais
    /// support de stockage — l'erreur est invisible sur un montant isolé mais s'accumule
    /// sur les cumuls annuels.
    ///
    /// Deux conventions sont donc appliquées :
    ///
    ///   • Les MONTANTS sont stockés en colonnes INTEGER, exprimées en CENTIMES
    ///     (le montant en francs multiplié par 100). Le stockage est exact, les
    ///     additions et les tris en SQL le sont aussi.
    ///
    ///   • Les TAUX et autres décimaux non monétaires (un taux moyen d'imposition
    ///     comporte de nombreuses décimales et ne doit surtout pas être arrondi au
    ///     centime) sont stockés en TEXT au format invariant, ce qui préserve la
    ///     valeur décimale exacte.
    ///
    /// Toutes les lectures sont tolérantes : elles acceptent encore les anciennes
    /// valeurs REAL, afin qu'une base partiellement migrée ne fasse jamais planter
    /// l'application.
    /// </summary>
    public static class DbValue
    {
        /// <summary>Montant en francs → entier de centimes destiné à la base.</summary>
        public static long MoneyToDb(decimal montant)
        {
            return (long)decimal.Round(montant * 100m, 0, MidpointRounding.AwayFromZero);
        }

        /// <summary>Valeur lue en base → montant en francs.</summary>
        public static decimal MoneyFromDb(object valeur)
        {
            if (valeur == null || valeur == DBNull.Value)
                return 0m;

            if (valeur is long)
                return (long)valeur / 100m;

            if (valeur is int)
                return (int)valeur / 100m;

            if (valeur is string)
            {
                decimal texte;
                return decimal.TryParse((string)valeur, NumberStyles.Any, CultureInfo.InvariantCulture, out texte)
                    ? texte / 100m
                    : 0m;
            }

            try
            {
                // Ancienne valeur REAL (base pas encore migrée) ou type inattendu.
                return Convert.ToDecimal(valeur, CultureInfo.InvariantCulture) / 100m;
            }
            catch
            {
                return 0m;
            }
        }

        /// <summary>Décimal non monétaire (taux, pourcentage) → texte invariant exact.</summary>
        public static string DecimalToDb(decimal valeur)
        {
            return valeur.ToString(CultureInfo.InvariantCulture);
        }

        /// <summary>Valeur lue en base → décimal non monétaire.</summary>
        public static decimal DecimalFromDb(object valeur)
        {
            if (valeur == null || valeur == DBNull.Value)
                return 0m;

            if (valeur is string)
            {
                decimal texte;
                return decimal.TryParse((string)valeur, NumberStyles.Any, CultureInfo.InvariantCulture, out texte)
                    ? texte
                    : 0m;
            }

            try
            {
                return Convert.ToDecimal(valeur, CultureInfo.InvariantCulture);
            }
            catch
            {
                return 0m;
            }
        }

        /// <summary>Date → texte triable et indépendant de la culture du poste.</summary>
        public static string DateToDb(DateTime date)
        {
            return date.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Valeur lue en base → date. Retourne <paramref name="defaut"/> si la colonne est
        /// vide ou illisible, plutôt que de lever une exception au chargement d'un dossier.
        /// </summary>
        public static DateTime DateFromDb(object valeur, DateTime defaut)
        {
            if (valeur == null || valeur == DBNull.Value)
                return defaut;

            if (valeur is DateTime)
                return (DateTime)valeur;

            string texte = valeur.ToString();
            if (string.IsNullOrWhiteSpace(texte))
                return defaut;

            DateTime resultat;
            if (DateTime.TryParse(texte, CultureInfo.InvariantCulture, DateTimeStyles.None, out resultat))
                return resultat;

            // Anciennes bases écrites avec la culture du poste.
            if (DateTime.TryParse(texte, out resultat))
                return resultat;

            return defaut;
        }

        /// <summary>Valeur lue en base → chaîne (null si la colonne est NULL).</summary>
        public static string TextFromDb(object valeur)
        {
            return (valeur == null || valeur == DBNull.Value) ? null : valeur.ToString();
        }

        /// <summary>Valeur lue en base → entier, 0 si la colonne est NULL ou illisible.</summary>
        public static int IntFromDb(object valeur)
        {
            if (valeur == null || valeur == DBNull.Value)
                return 0;

            try
            {
                return Convert.ToInt32(valeur, CultureInfo.InvariantCulture);
            }
            catch
            {
                return 0;
            }
        }
    }
}
