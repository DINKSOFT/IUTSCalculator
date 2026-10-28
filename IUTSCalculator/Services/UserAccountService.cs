using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using IUTSCalculator.Models;

namespace IUTSCalculator.Services
{
    public class UserAccountService
    {
        private readonly string _usersFilePath;
        private List<UserAccount> _cachedUsers;
        private readonly JsonSerializerOptions _jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        public UserAccountService(string appDataFolder)
        {
            _usersFilePath = Path.Combine(appDataFolder, "users.json");
            _cachedUsers = new List<UserAccount>();
            //LoadUsersAsync().Wait();
        }

        /// <summary>Chiffre le contenu de <c>users.json</c> pour le profil Windows courant (DPAPI).</summary>
        public static byte[] ProtegerPourCePoste(byte[] clair)
        {
            return ProtectedData.Protect(clair, null, DataProtectionScope.CurrentUser);
        }

        /// <summary>Déchiffre un <c>users.json</c> protégé pour le profil Windows courant ; lève CryptographicException sinon.</summary>
        public static byte[] DeprotegerPourCePoste(byte[] protege)
        {
            return ProtectedData.Unprotect(protege, null, DataProtectionScope.CurrentUser);
        }

        /// <summary>
        /// Vrai si <c>users.json</c> existe mais n'a pu être lu (ni chiffré pour ce profil Windows, ni
        /// JSON valide). Le programme ne doit alors PAS présenter l'écran comme un premier lancement
        /// sans prévenir : créer un compte réécrirait le fichier.
        /// </summary>
        public bool FichierIllisible { get; private set; }

        /// <summary>Copie conservée du fichier illisible (renseignée avec <see cref="FichierIllisible"/>).</summary>
        public string SauvegardeFichierIllisible { get; private set; }

        public async Task<List<UserAccount>> LoadUsersAsync()
        {
            FichierIllisible = false;
            SauvegardeFichierIllisible = null;

            if (!File.Exists(_usersFilePath)) return _cachedUsers;

            byte[] data = await Task.Run(() => File.ReadAllBytes(_usersFilePath)).ConfigureAwait(false);

            // 1. Format courant : JSON chiffré par DPAPI pour le profil Windows courant.
            List<UserAccount> comptes = null;
            try
            {
                byte[] clair = DeprotegerPourCePoste(data);
                comptes = JsonSerializer.Deserialize<List<UserAccount>>(Encoding.UTF8.GetString(clair), _jsonOptions);
            }
            catch (Exception) { /* pas (ou plus) lisible avec DPAPI : on essaie l'ancien format */ }

            if (comptes != null)
            {
                _cachedUsers = comptes;
                return _cachedUsers;
            }

            // 2. Ancien format : JSON en clair, écrit avant l'ajout du chiffrement. On le lit,
            //    puis on le re-chiffre aussitôt (migration unique) ; la copie en clair est
            //    d'abord conservée à côté.
            try
            {
                string texte = Encoding.UTF8.GetString(data).TrimStart('﻿');
                comptes = JsonSerializer.Deserialize<List<UserAccount>>(texte, _jsonOptions);
            }
            catch (Exception) { comptes = null; }

            if (comptes != null)
            {
                SauvegarderCopie(".avant-chiffrement");
                _cachedUsers = comptes;
                await SaveUsersAsync(_cachedUsers).ConfigureAwait(false);
                return _cachedUsers;
            }

            // 3. Illisible (autre profil Windows, autre machine, fichier abîmé) : rien n'est effacé,
            //    une copie est gardée et l'appelant est prévenu.
            SauvegardeFichierIllisible = SauvegarderCopie(".illisible");
            FichierIllisible = true;
            _cachedUsers = new List<UserAccount>();
            return _cachedUsers;
        }

        /// <summary>Copie <c>users.json</c> en <c>users.json&lt;suffixe&gt;</c> (puis suffixe2, suffixe3…) sans jamais écraser une copie.</summary>
        private string SauvegarderCopie(string suffixe)
        {
            try
            {
                string cible = _usersFilePath + suffixe;
                for (int i = 2; File.Exists(cible); i++) cible = _usersFilePath + suffixe + i;
                File.Copy(_usersFilePath, cible);
                return cible;
            }
            catch (Exception)
            {
                return null;
            }
        }

        public async Task SaveUsersAsync(List<UserAccount> users)
        {
            _cachedUsers = users ?? new List<UserAccount>();
            string json = JsonSerializer.Serialize(_cachedUsers, _jsonOptions);
            byte[] plainData = Encoding.UTF8.GetBytes(json);
            byte[] encryptedData = ProtegerPourCePoste(plainData);

            // Créer le dossier de données s'il n'existe pas encore (premier démarrage).
            string dossier = Path.GetDirectoryName(_usersFilePath);
            if (!string.IsNullOrEmpty(dossier))
                Directory.CreateDirectory(dossier);
            await Task.Run(() => File.WriteAllBytes(_usersFilePath, encryptedData));
        }

        public async Task<UserAccount> AuthenticateAsync(string username, string password)
        {
            await LoadUsersAsync();
            var user = _cachedUsers.FirstOrDefault(u => u.Username.Equals(username, StringComparison.OrdinalIgnoreCase));
            if (user != null && VerifyPassword(password, user.PasswordHash))
            {
                // Remise à niveau silencieuse d'un ancien hachage vers le format PBKDF2 salé.
                if (EstAncienFormat(user.PasswordHash))
                {
                    user.PasswordHash = HashPassword(password);
                    await SaveUsersAsync(_cachedUsers);
                }
                return user;
            }
            return null;
        }

        public async Task<bool> AddUserAsync(UserAccount user, string password)
        {
            await LoadUsersAsync();
            if (_cachedUsers.Any(u => u.Username.Equals(user.Username, StringComparison.OrdinalIgnoreCase)))
                return false;
            user.UserId = Guid.NewGuid().ToString();
            user.PasswordHash = HashPassword(password);
            _cachedUsers.Add(user);
            await SaveUsersAsync(_cachedUsers);
            return true;
        }

        public async Task<bool> RemoveUserAsync(string userId)
        {
            await LoadUsersAsync();
            var user = _cachedUsers.FirstOrDefault(u => u.UserId == userId);
            if (user == null) return false;
            _cachedUsers.Remove(user);
            await SaveUsersAsync(_cachedUsers);
            return true;
        }

        public async Task<bool> ReinitialiserMotDePasseAsync(string userId, string nouveauMotDePasse)
        {
            await LoadUsersAsync();
            var user = _cachedUsers.FirstOrDefault(u => u.UserId == userId);
            if (user == null) return false;
            user.PasswordHash = HashPassword(nouveauMotDePasse);
            await SaveUsersAsync(_cachedUsers);
            return true;
        }

        public async Task<int> CompterAdminsAsync()
        {
            await LoadUsersAsync();
            return _cachedUsers.Count(u => u.Role == UserRole.Admin);
        }

        public async Task<bool> UpdateUserRoleAsync(string userId, UserRole newRole)
        {
            await LoadUsersAsync();
            var user = _cachedUsers.FirstOrDefault(u => u.UserId == userId);
            if (user == null) return false;
            user.Role = newRole;
            await SaveUsersAsync(_cachedUsers);
            return true;
        }

        /// <summary>
        /// Modifie la fiche d'un compte en une seule opération : nom affiché, identifiant,
        /// rôle et, si un nouveau mot de passe est fourni, le mot de passe. Renvoie null en cas
        /// de succès, sinon le motif du refus (rien n'est alors enregistré).
        ///
        /// Garde-fous : identifiant obligatoire et unique (sans tenir compte des majuscules),
        /// mot de passe de 6 caractères au moins, et au moins un administrateur restant.
        /// </summary>
        public async Task<string> ModifierUtilisateurAsync(
            string userId, string nomAffiche, string identifiant, UserRole role, string nouveauMotDePasse)
        {
            await LoadUsersAsync();
            var user = _cachedUsers.FirstOrDefault(u => u.UserId == userId);
            if (user == null)
                return "Ce compte n'existe plus : il a peut-être été supprimé entre-temps.";

            identifiant = (identifiant ?? string.Empty).Trim();
            nomAffiche = (nomAffiche ?? string.Empty).Trim();

            if (identifiant.Length == 0)
                return "L'identifiant est obligatoire.";

            if (_cachedUsers.Any(u => u.UserId != userId &&
                                      string.Equals(u.Username, identifiant, StringComparison.OrdinalIgnoreCase)))
                return "Cet identifiant est déjà utilisé.";

            if (!string.IsNullOrEmpty(nouveauMotDePasse) && nouveauMotDePasse.Length < 6)
                return "Le nouveau mot de passe doit comporter au moins 6 caractères.";

            if (user.Role == UserRole.Admin && role != UserRole.Admin &&
                _cachedUsers.Count(u => u.Role == UserRole.Admin) <= 1)
                return "Impossible : il doit rester au moins un administrateur.";

            user.Username = identifiant;
            user.DisplayName = nomAffiche.Length == 0 ? identifiant : nomAffiche;
            user.Role = role;
            if (!string.IsNullOrEmpty(nouveauMotDePasse))
                user.PasswordHash = HashPassword(nouveauMotDePasse);

            await SaveUsersAsync(_cachedUsers);
            return null;
        }

        // --- Sécurité des mots de passe ---
        // Format courant : "PBKDF2$<iterations>$<selBase64>$<hashBase64>".
        // L'ancien format (SHA-256 sans sel, simple Base64) reste accepté en vérification
        // pour compatibilité ; un compte qui s'y connecte est remis à niveau automatiquement.
        private const int Pbkdf2Iterations = 100000;
        private const int SelTaille = 16;
        private const int HashTaille = 32;

        // Conserve cette signature exacte : PasswordResetWindow l'appelle par réflexion.
        private string HashPassword(string password)
        {
            byte[] sel = new byte[SelTaille];
            using (var rng = RandomNumberGenerator.Create())
                rng.GetBytes(sel);
            byte[] hash;
            using (var pbkdf2 = new Rfc2898DeriveBytes(password ?? string.Empty, sel, Pbkdf2Iterations, HashAlgorithmName.SHA256))
                hash = pbkdf2.GetBytes(HashTaille);
            return "PBKDF2$" + Pbkdf2Iterations.ToString(System.Globalization.CultureInfo.InvariantCulture)
                   + "$" + Convert.ToBase64String(sel) + "$" + Convert.ToBase64String(hash);
        }

        private bool VerifyPassword(string password, string hash)
        {
            if (string.IsNullOrEmpty(hash)) return false;

            if (hash.StartsWith("PBKDF2$", StringComparison.Ordinal))
            {
                var p = hash.Split('$');
                if (p.Length != 4) return false;
                if (!int.TryParse(p[1], out int iterations) || iterations < 1000) return false;
                byte[] sel, attendu;
                try { sel = Convert.FromBase64String(p[2]); attendu = Convert.FromBase64String(p[3]); }
                catch (FormatException) { return false; }
                byte[] calcule;
                using (var pbkdf2 = new Rfc2898DeriveBytes(password ?? string.Empty, sel, iterations, HashAlgorithmName.SHA256))
                    calcule = pbkdf2.GetBytes(attendu.Length);
                return ComparerConstant(calcule, attendu);
            }

            // Ancien format SHA-256 sans sel (compatibilité ascendante).
            using (var sha = SHA256.Create())
            {
                string legacy = Convert.ToBase64String(sha.ComputeHash(Encoding.UTF8.GetBytes(password ?? string.Empty)));
                return legacy == hash;
            }
        }

        private static bool ComparerConstant(byte[] a, byte[] b)
        {
            if (a == null || b == null || a.Length != b.Length) return false;
            int diff = 0;
            for (int i = 0; i < a.Length; i++) diff |= a[i] ^ b[i];
            return diff == 0;
        }

        private static bool EstAncienFormat(string hash)
        {
            return !string.IsNullOrEmpty(hash) && !hash.StartsWith("PBKDF2$", StringComparison.Ordinal);
        }
    }
}