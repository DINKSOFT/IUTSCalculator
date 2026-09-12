using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using IUTSCalculator.Helpers;
using IUTSCalculator.Models;

namespace IUTSCalculator.Services
{
    /// <summary>
    /// Gère le registre des dossiers (entreprises) connus par l'application.
    /// Chaque dossier possède sa propre base de données SQLite (voir <see cref="DataService"/>
    /// et <see cref="SQLiteService"/>) ; ce registre ne fait que répertorier les dossiers
    /// existants et leurs informations, dans un simple fichier JSON.
    /// </summary>
    public class DossierRegistryService
    {
        private static readonly string RegistryFilePath = Path.Combine(Constants.AppDataFolder, "dossiers.json");
        private static readonly string DossiersFolder = Path.Combine(Constants.AppDataFolder, "Dossiers");
        private const string LegacyDbFileName = "iuts_data.db";
        private const string LegacyDossierName = "Dossier par défaut";

        /// <summary>
        /// Charge la liste des dossiers connus. Si aucun registre n'existe encore mais que
        /// l'ancienne base de données unique (utilisée avant l'introduction des dossiers)
        /// est présente, elle est automatiquement enregistrée comme premier dossier afin
        /// de ne perdre aucune donnée existante.
        /// </summary>
        public async Task<List<Dossier>> LoadDossiersAsync()
        {
            Directory.CreateDirectory(Constants.AppDataFolder);
            Directory.CreateDirectory(DossiersFolder);

            var dossiers = await ReadRegistryAsync();

            if (dossiers.Count == 0)
            {
                string legacyDbPath = Path.Combine(Constants.AppDataFolder, LegacyDbFileName);
                if (File.Exists(legacyDbPath))
                {
                    var legacyDossier = new Dossier
                    {
                        DossierId = Guid.NewGuid().ToString(),
                        RaisonSociale = LegacyDossierName,
                        DbFileName = LegacyDbFileName,
                        DateCreation = File.GetCreationTime(legacyDbPath)
                    };
                    dossiers.Add(legacyDossier);
                    await SaveDossiersAsync(dossiers);
                }
            }

            return dossiers.OrderBy(d => d.RaisonSociale).ToList();
        }

        private async Task<List<Dossier>> ReadRegistryAsync()
        {
            if (!File.Exists(RegistryFilePath))
                return new List<Dossier>();

            try
            {
                var json = await Task.Run(() => File.ReadAllText(RegistryFilePath));
                return JsonSerializer.Deserialize<List<Dossier>>(json) ?? new List<Dossier>();
            }
            catch
            {
                return new List<Dossier>();
            }
        }

        public async Task SaveDossiersAsync(List<Dossier> dossiers)
        {
            Directory.CreateDirectory(Constants.AppDataFolder);
            var json = JsonSerializer.Serialize(dossiers, new JsonSerializerOptions { WriteIndented = true });
            await Task.Run(() => File.WriteAllText(RegistryFilePath, json));
        }

        /// <summary>
        /// Crée un nouveau dossier : lui attribue un identifiant et un fichier de base de
        /// données dédié, l'ajoute au registre, puis initialise physiquement sa base SQLite.
        /// </summary>
        public async Task<Dossier> CreateDossierAsync(Dossier dossier)
        {
            if (dossier == null)
                throw new ArgumentNullException(nameof(dossier));
            if (string.IsNullOrWhiteSpace(dossier.RaisonSociale))
                throw new ArgumentException("La raison sociale du dossier est obligatoire.");

            var dossiers = await LoadDossiersAsync();

            if (string.IsNullOrEmpty(dossier.DossierId))
                dossier.DossierId = Guid.NewGuid().ToString();

            if (string.IsNullOrEmpty(dossier.DbFileName))
                dossier.DbFileName = Path.Combine("Dossiers", $"dossier_{dossier.DossierId}.db");

            dossier.DateCreation = DateTime.Now;

            dossiers.Add(dossier);
            await SaveDossiersAsync(dossiers);

            // Force la création physique du fichier de base de données du nouveau dossier
            using (new SQLiteService(dossier.DbFileName)) { }

            return dossier;
        }

        public async Task UpdateDossierAsync(Dossier dossier)
        {
            if (dossier == null)
                throw new ArgumentNullException(nameof(dossier));

            var dossiers = await LoadDossiersAsync();
            var index = dossiers.FindIndex(d => d.DossierId == dossier.DossierId);
            if (index >= 0)
            {
                dossiers[index] = dossier;
                await SaveDossiersAsync(dossiers);
            }
        }

        /// <summary>
        /// Met à jour la date de dernière ouverture d'un dossier.
        /// </summary>
        public async Task TouchDossierAsync(string dossierId)
        {
            var dossiers = await LoadDossiersAsync();
            var existing = dossiers.FirstOrDefault(d => d.DossierId == dossierId);
            if (existing != null)
            {
                existing.DerniereOuverture = DateTime.Now;
                await SaveDossiersAsync(dossiers);
            }
        }

        /// <summary>
        /// Supprime un dossier du registre et, si demandé, son fichier de base de données.
        /// </summary>
        public async Task DeleteDossierAsync(string dossierId, bool deleteDatabaseFile)
        {
            var dossiers = await LoadDossiersAsync();
            var existing = dossiers.FirstOrDefault(d => d.DossierId == dossierId);
            if (existing == null)
                return;

            dossiers.Remove(existing);
            await SaveDossiersAsync(dossiers);

            if (deleteDatabaseFile)
            {
                try
                {
                    var dbPath = Path.Combine(Constants.AppDataFolder, existing.DbFileName);
                    if (File.Exists(dbPath))
                        File.Delete(dbPath);
                }
                catch
                {
                    // La suppression physique du fichier n'est pas critique :
                    // le dossier a déjà été retiré du registre.
                }
            }
        }
    }
}
