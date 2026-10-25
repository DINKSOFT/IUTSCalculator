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

        public async Task<List<UserAccount>> LoadUsersAsync()
        {
            if (File.Exists(_usersFilePath))
            {
                // Lire le fichier de façon synchrone dans un thread de fond pour ne pas bloquer l'UI
                string json = await Task.Run(() => File.ReadAllText(_usersFilePath)).ConfigureAwait(false);
                _cachedUsers = JsonSerializer.Deserialize<List<UserAccount>>(json, _jsonOptions) ?? new List<UserAccount>();
            }
            return _cachedUsers;
        }

        public async Task SaveUsersAsync(List<UserAccount> users)
        {
            _cachedUsers = users ?? new List<UserAccount>();
            string json = JsonSerializer.Serialize(_cachedUsers, _jsonOptions);
            // Créer le dossier de données s'il n'existe pas encore (premier démarrage).
            string dossier = Path.GetDirectoryName(_usersFilePath);
            if (!string.IsNullOrEmpty(dossier))
                Directory.CreateDirectory(dossier);
            await Task.Run(() => File.WriteAllText(_usersFilePath, json));
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