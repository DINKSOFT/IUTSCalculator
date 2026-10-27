// Fichier : Services/GardeLectureSeule.cs
using System;

namespace IUTSCalculator.Services
{
    /// <summary>
    /// Profil « Lecture seule » appliqué au niveau de la base (défaut n° 10 du guide de
    /// formation) : jusqu'ici, le rôle ne grisait que quelques boutons, et un utilisateur en
    /// lecture seule pouvait enregistrer depuis la plupart des écrans.
    ///
    /// Chaque connexion ouverte par <see cref="SQLiteService.CreerConnexion"/> (une fois le
    /// schéma du dossier prêt) reçoit, si l'utilisateur connecté est en lecture seule, un
    /// « autorisateur » SQLite qui refuse toute écriture : INSERT, UPDATE, DELETE, création,
    /// modification ou suppression de table, d'index, de vue ou de déclencheur. Un seul point
    /// de contrôle couvre ainsi tous les écrans et tous les services, y compris ceux à venir.
    ///
    /// Restent permis : la lecture, les transactions, les PRAGMA, les objets temporaires
    /// (base « temp ») et l'écriture dans le journal d'audit (la connexion d'un utilisateur
    /// et ses tentatives refusées doivent pouvoir y être tracées).
    ///
    /// La mise à niveau du schéma n'est pas concernée : elle s'exécute à l'ouverture du
    /// dossier, avant que la garde ne s'applique (voir SQLiteService).
    /// </summary>
    public static class GardeLectureSeule
    {
        /// <summary>Message affiché quand une écriture est refusée.</summary>
        public const string Message =
            "Votre profil « Lecture seule » permet de consulter le dossier, pas de le modifier : " +
            "rien n'a été enregistré. Demandez à un éditeur ou à un administrateur.";

        /// <summary>Branche la garde sur toutes les connexions de dossier. À appeler au démarrage.</summary>
        public static void Activer()
        {
            SQLiteService.GardeConnexionSqlite = AppliquerSqlite;
        }

        /// <summary>
        /// Vrai si l'exception (ou l'une de ses causes) vient d'une écriture refusée par la garde.
        /// SQLite renvoie alors le code SQLITE_AUTH (« not authorized »).
        /// </summary>
        public static bool EstRefus(Exception ex)
        {
            for (var e = ex; e != null; e = e.InnerException)
            {
                var sqliteMds = e as Microsoft.Data.Sqlite.SqliteException;
                if (sqliteMds != null && sqliteMds.SqliteErrorCode == SQLitePCL.raw.SQLITE_AUTH)
                    return true;
                if (e.Message != null && e.Message.IndexOf("not authorized", StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Remplace, dans un message d'état, le texte technique de SQLite par une phrase
        /// compréhensible. Sans effet sur les autres messages.
        /// </summary>
        public static string Traduire(string message)
        {
            if (string.IsNullOrEmpty(message)) return message;
            if (message.IndexOf("not authorized", StringComparison.OrdinalIgnoreCase) < 0 &&
                message.IndexOf("authorization denied", StringComparison.OrdinalIgnoreCase) < 0)
                return message;
            return Message;
        }

        // Microsoft.Data.Sqlite n'expose pas l'autorisateur : il est posé directement sur la
        // connexion native. Le délégué est gardé dans un champ statique pour que le ramasse-miettes
        // ne le libère pas tant que des connexions l'utilisent.
        private static readonly SQLitePCL.strdelegate_authorizer AutoriserNatif = AutoriserSqlite;

        private static void AppliquerSqlite(Microsoft.Data.Sqlite.SqliteConnection connexion)
        {
            if (connexion == null || !UserSession.Instance.IsReadOnly) return;
            int rc = SQLitePCL.raw.sqlite3_set_authorizer(connexion.Handle, AutoriserNatif, null);
            if (rc != SQLitePCL.raw.SQLITE_OK)
                throw new InvalidOperationException("La garde « Lecture seule » n'a pas pu être posée (code SQLite " + rc + ").");
        }

        private static int AutoriserSqlite(object userData, int action, string argument1, string argument2, string baseCible, string declencheur)
        {
            switch (action)
            {
                case SQLitePCL.raw.SQLITE_INSERT:
                case SQLitePCL.raw.SQLITE_UPDATE:
                case SQLitePCL.raw.SQLITE_DELETE:
                    return EstTemporaire(baseCible) || EstTableAutorisee(argument1) ? SQLitePCL.raw.SQLITE_OK : SQLitePCL.raw.SQLITE_DENY;

                case SQLitePCL.raw.SQLITE_CREATE_TABLE:
                case SQLitePCL.raw.SQLITE_CREATE_INDEX:
                case SQLitePCL.raw.SQLITE_CREATE_TRIGGER:
                case SQLitePCL.raw.SQLITE_CREATE_VIEW:
                case SQLitePCL.raw.SQLITE_CREATE_VTABLE:
                case SQLitePCL.raw.SQLITE_DROP_TABLE:
                case SQLitePCL.raw.SQLITE_DROP_INDEX:
                case SQLitePCL.raw.SQLITE_DROP_TRIGGER:
                case SQLitePCL.raw.SQLITE_DROP_VIEW:
                case SQLitePCL.raw.SQLITE_DROP_VTABLE:
                case SQLitePCL.raw.SQLITE_ALTER_TABLE:
                case SQLitePCL.raw.SQLITE_REINDEX:
                case SQLitePCL.raw.SQLITE_ATTACH:
                    return SQLitePCL.raw.SQLITE_DENY;

                default:
                    // Lecture, transactions, PRAGMA, fonctions, objets temporaires.
                    return SQLitePCL.raw.SQLITE_OK;
            }
        }

        private static bool EstTemporaire(string baseCible)
        {
            return string.Equals(baseCible, "temp", StringComparison.OrdinalIgnoreCase);
        }

        private static bool EstTableAutorisee(string table)
        {
            return string.Equals(table, "JournalAudit", StringComparison.OrdinalIgnoreCase)
                || string.Equals(table, "sqlite_sequence", StringComparison.OrdinalIgnoreCase);
        }
    }
}
