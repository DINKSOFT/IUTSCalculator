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
            await Task.Run(() => File.WriteAllText(_usersFilePath, json));
        }

        public async Task<UserAccount> AuthenticateAsync(string username, string password)
        {
            await LoadUsersAsync();
            var user = _cachedUsers.FirstOrDefault(u => u.Username.Equals(username, StringComparison.OrdinalIgnoreCase));
            if (user != null && VerifyPassword(password, user.PasswordHash))
                return user;
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

        public async Task<bool> UpdateUserRoleAsync(string userId, UserRole newRole)
        {
            await LoadUsersAsync();
            var user = _cachedUsers.FirstOrDefault(u => u.UserId == userId);
            if (user == null) return false;
            user.Role = newRole;
            await SaveUsersAsync(_cachedUsers);
            return true;
        }

        // --- Sécurité basique (à améliorer pour production) ---
        private string HashPassword(string password)
        {
            using (var sha = SHA256.Create())
            {
                var bytes = Encoding.UTF8.GetBytes(password);
                var hash = sha.ComputeHash(bytes);
                return Convert.ToBase64String(hash);
            }
        }

        private bool VerifyPassword(string password, string hash)
        {
            return HashPassword(password) == hash;
        }
    }
}