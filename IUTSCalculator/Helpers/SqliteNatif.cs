using System;
using System.IO;
using System.Runtime.InteropServices;

namespace IUTSCalculator.Helpers
{
    /// <summary>
    /// Chargement du moteur SQLite chiffrant (SQLite3 Multiple Ciphers) utilisé par
    /// Microsoft.Data.Sqlite.
    ///
    /// Sous .NET Framework, le paquet SQLite3MC ne range pas sa DLL native à côté du
    /// programme : le projet la copie dans <c>x86\</c> et <c>x64\</c> (comme System.Data.SQLite),
    /// et elle est chargée ici selon le processus avant l'initialisation de SQLitePCLRaw, dont
    /// l'appel natif « sqlite3mc » retrouve alors le module déjà chargé.
    /// </summary>
    public static class SqliteNatif
    {
        private static readonly object Verrou = new object();
        private static bool _initialise;

        [DllImport("kernel32", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern IntPtr LoadLibrary(string chemin);

        /// <summary>À appeler avant toute connexion Microsoft.Data.Sqlite ; sans effet au-delà du premier appel.</summary>
        public static void Initialiser()
        {
            if (_initialise) return;
            lock (Verrou)
            {
                if (_initialise) return;

                string chemin = Path.Combine(AppDomain.CurrentDomain.BaseDirectory,
                    Environment.Is64BitProcess ? "x64" : "x86", "sqlite3mc.dll");
                if (LoadLibrary(chemin) == IntPtr.Zero)
                    throw new DllNotFoundException(string.Format(
                        "Le moteur de base de données chiffrée est introuvable ou inutilisable : {0} (erreur Windows {1}). Réinstallez le programme.",
                        chemin, Marshal.GetLastWin32Error()));

                SQLitePCL.Batteries_V2.Init();
                _initialise = true;
            }
        }
    }
}
