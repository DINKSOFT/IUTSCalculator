using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using IUTSCalculator.Models;
using IUTSCalculator.Services;
using IUTSCalculator.Helpers;

namespace IUTSCalculator.ViewModels
{
    public class LoginViewModel : BaseViewModel
    {
        private readonly UserAccountService _userService;
        private string _username;
        private string _password;
        private string _statusMessage;
        private bool _isBusy;

        public event EventHandler LoginSucceeded;
        public event EventHandler PasswordResetRequested;

        public LoginViewModel(UserAccountService userService)
        {
            _userService = userService ?? throw new ArgumentNullException(nameof(userService));
            LoginCommand = new RelayCommand(async () => await LoginAsync(), () => !IsBusy);
            ResetPasswordCommand = new RelayCommand(() => OnPasswordResetRequested(), () => !IsBusy);
        }

        public string Username
        {
            get => _username;
            set => SetProperty(ref _username, value);
        }

        public string Password
        {
            get => _password;
            set => SetProperty(ref _password, value);
        }

        public new string StatusMessage
        {
            get => _statusMessage;
            set => SetProperty(ref _statusMessage, value);
        }

        public new bool IsBusy
        {
            get => _isBusy;
            set
            {
                if (SetProperty(ref _isBusy, value))
                {
                    (LoginCommand as RelayCommand)?.RaiseCanExecuteChanged();
                    (ResetPasswordCommand as RelayCommand)?.RaiseCanExecuteChanged();
                }
            }
        }

        public ICommand LoginCommand { get; }
        public ICommand ResetPasswordCommand { get; }

        private async Task LoginAsync()
        {
            IsBusy = true;
            StatusMessage = string.Empty;
            try
            {
                var user = await _userService.AuthenticateAsync(Username, Password);
                if (user != null)
                {
                    UserSession.Instance.Login(user);
                    StatusMessage = "Connexion réussie.";
                    LoginSucceeded?.Invoke(this, EventArgs.Empty);
                }
                else
                {
                    StatusMessage = "Identifiant ou mot de passe incorrect.";
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"Erreur : {ex.Message}";
            }
            finally
            {
                IsBusy = false;
            }
        }

        private void OnPasswordResetRequested()
        {
            PasswordResetRequested?.Invoke(this, EventArgs.Empty);
        }
    }
}