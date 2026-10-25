using System;
using System.Collections.Generic;
using System.Windows;

namespace IUTSCalculator.Services
{
    public enum NotificationType
    {
        Info,
        Success,
        Warning,
        Error
    }

    public class NotificationArgs : EventArgs
    {
        public string Message { get; }
        public NotificationType Type { get; }

        public NotificationArgs(string message, NotificationType type)
        {
            Message = message;
            Type = type;
        }
    }

    /// <summary>
    /// Service global pour gérer les notifications utilisateur sans utiliser de MessageBox bloquantes.
    /// </summary>
    public class NotificationService
    {
        private static readonly Lazy<NotificationService> _instance = new Lazy<NotificationService>(() => new NotificationService());
        public static NotificationService Instance => _instance.Value;

        public event EventHandler<NotificationArgs> NotificationRequested;

        private NotificationService() { }

        public void Notify(string message, NotificationType type = NotificationType.Info)
        {
            NotificationRequested?.Invoke(this, new NotificationArgs(message, type));
        }

        public void Info(string message) => Notify(message, NotificationType.Info);
        public void Success(string message) => Notify(message, NotificationType.Success);
        public void Warning(string message) => Notify(message, NotificationType.Warning);
        public void Error(string message) => Notify(message, NotificationType.Error);
    }
}
