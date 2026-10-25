using System;
using System.Reflection;
using IUTSCalculator.Services;

namespace IUTSCalculator.Helpers
{
    /// <summary>
    /// Version du programme, lue dans <c>Properties/AssemblyInfo.cs</c> (seul endroit à
    /// changer à chaque livraison) : numéro court, libellé affiché, niveau de base géré.
    /// </summary>
    public static class VersionApplication
    {
        private static readonly Assembly Programme = typeof(VersionApplication).Assembly;

        /// <summary>Numéro complet (1.4.38.1).</summary>
        public static Version Numero => Programme.GetName().Version ?? new Version(1, 0, 0, 0);

        /// <summary>Numéro affiché : la révision n'apparaît que si elle n'est pas nulle.</summary>
        public static string Court
        {
            get
            {
                var v = Numero;
                return v.Revision > 0
                    ? string.Format("{0}.{1}.{2}.{3}", v.Major, v.Minor, v.Build, v.Revision)
                    : string.Format("{0}.{1}.{2}", v.Major, v.Minor, v.Build);
            }
        }

        /// <summary>Libellé de livraison (« 1.4.38.1 du 22/10/2026 »).</summary>
        public static string Libelle
        {
            get
            {
                var info = Programme.GetCustomAttribute<AssemblyInformationalVersionAttribute>();
                return info != null && !string.IsNullOrWhiteSpace(info.InformationalVersion) ? info.InformationalVersion : Court;
            }
        }

        /// <summary>Niveau de base de données que ce programme sait ouvrir (au plus).</summary>
        public static int NiveauBase => SQLiteService.SchemaCible;

        /// <summary>« Version 1.4.38.1 du 22/10/2026 — base de données v38 ».</summary>
        public static string Complet => "Version " + Libelle + " — base de données v" + NiveauBase;
    }
}
