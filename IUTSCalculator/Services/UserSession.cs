// Fichier : Services/UserSession.cs
using IUTSCalculator.Models;

namespace IUTSCalculator.Services
{
    public class UserSession
    {
        private static UserSession _instance;
        public static UserSession Instance => _instance ?? (_instance = new UserSession());

        public UserAccount CurrentUser { get; private set; }

        public void Login(UserAccount user) => CurrentUser = user;
        public void Logout() => CurrentUser = null;

        public bool IsAuthenticated => CurrentUser != null;
        public bool IsAdmin => CurrentUser?.Role == UserRole.Admin;
        public bool CanEdit => CurrentUser != null && (CurrentUser.Role == UserRole.Editor || CurrentUser.Role == UserRole.Admin);
        public bool IsReadOnly => CurrentUser?.Role == UserRole.ReadOnly;
    }
}