namespace IUTSCalculator.Models
{
    public enum UserRole
    {
        ReadOnly,
        Editor,
        Admin
    }

    public class UserAccount
    {
        public string UserId { get; set; }
        public string Username { get; set; }
        public string PasswordHash { get; set; } // Stockez le hash, pas le mot de passe en clair
        public UserRole Role { get; set; }
        public string DisplayName { get; set; }
    }
}