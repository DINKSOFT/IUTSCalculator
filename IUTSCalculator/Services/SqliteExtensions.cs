using System;
using Microsoft.Data.Sqlite;

namespace IUTSCalculator.Services
{
    /// <summary>
    /// Équivalents Microsoft.Data.Sqlite de commodités de System.Data.SQLite dont dépendent
    /// les services.
    /// </summary>
    public static class SqliteExtensions
    {
        /// <summary>
        /// Comme <c>AddWithValue</c>, mais une valeur <c>null</c> est enregistrée comme NULL :
        /// c'était le comportement de System.Data.SQLite, alors que Microsoft.Data.Sqlite refuse
        /// d'exécuter une commande dont un paramètre vaut null (« Value must be set »).
        /// </summary>
        public static SqliteParameter AjouterValeur(this SqliteParameterCollection parametres, string nom, object valeur)
        {
            return parametres.AddWithValue(nom, valeur ?? DBNull.Value);
        }

        /// <summary>Identifiant de la dernière ligne insérée sur cette connexion (<c>LastInsertRowId</c> de System.Data.SQLite).</summary>
        public static long DernierIdInsere(this SqliteConnection connexion)
        {
            return SQLitePCL.raw.sqlite3_last_insert_rowid(connexion.Handle);
        }
    }
}
