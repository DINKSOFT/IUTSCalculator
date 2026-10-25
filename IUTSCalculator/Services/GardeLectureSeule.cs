// Fichier : Services/GardeLectureSeule.cs
using System;
using System.Data.SQLite;

namespace IUTSCalculator.Services
{
    /// <summary>
    /// Profil « Lecture seule » appliqué au niveau de la base (défaut n° 10 du guide de
    /// formation) : jusqu'ici, le rôle ne grisait que quelques boutons, et un utilisateur en
    /// lecture seule pouvait enregistrer depuis la plupart des écrans.
    ///
    /// Chaque connexion ouverte par <see cref="SQLiteService.CreateConnection"/> (une fois le
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
            SQLiteService.GardeConnexion = Appliquer;
        }

        /// <summary>
        /// Vrai si l'exception (ou l'une de ses causes) vient d'une écriture refusée par la garde.
        /// SQLite renvoie alors le code SQLITE_AUTH (« not authorized »).
        /// </summary>
        public static bool EstRefus(Exception ex)
        {
            for (var e = ex; e != null; e = e.InnerException)
            {
                var sqlite = e as SQLiteException;
                if (sqlite != null && sqlite.ResultCode == SQLiteErrorCode.Auth)
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

        private static void Appliquer(SQLiteConnection connexion)
        {
            // Le profil est lu à l'ouverture de chaque connexion : une déconnexion suivie
            // d'une connexion sous un autre profil est prise en compte immédiatement.
            if (connexion == null || !UserSession.Instance.IsReadOnly) return;
            connexion.Authorize += Autoriser;
        }

        private static void Autoriser(object sender, AuthorizerEventArgs e)
        {
            switch (e.ActionCode)
            {
                case SQLiteAuthorizerActionCode.Insert:
                case SQLiteAuthorizerActionCode.Update:
                case SQLiteAuthorizerActionCode.Delete:
                    if (EstTemporaire(e.Database) || EstTableAutorisee(e.Argument1)) return;
                    e.ReturnCode = SQLiteAuthorizerReturnCode.Deny;
                    return;

                case SQLiteAuthorizerActionCode.CreateTable:
                case SQLiteAuthorizerActionCode.CreateIndex:
                case SQLiteAuthorizerActionCode.CreateTrigger:
                case SQLiteAuthorizerActionCode.CreateView:
                case SQLiteAuthorizerActionCode.CreateVtable:
                case SQLiteAuthorizerActionCode.DropTable:
                case SQLiteAuthorizerActionCode.DropIndex:
                case SQLiteAuthorizerActionCode.DropTrigger:
                case SQLiteAuthorizerActionCode.DropView:
                case SQLiteAuthorizerActionCode.DropVtable:
                case SQLiteAuthorizerActionCode.AlterTable:
                case SQLiteAuthorizerActionCode.Reindex:
                case SQLiteAuthorizerActionCode.Attach:
                    e.ReturnCode = SQLiteAuthorizerReturnCode.Deny;
                    return;

                default:
                    // Lecture, transactions, PRAGMA, fonctions, objets temporaires.
                    return;
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
