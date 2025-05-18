using System.Windows;
using IUTSCalculator.ViewModels;
using IUTSCalculator.Services;

namespace IUTSCalculator.Views
{
    public partial class LoginWindow : Window
    {
        public LoginWindow(UserAccountService userService)
        {
            InitializeComponent();
            var vm = new LoginViewModel(userService);
            DataContext = vm;
            vm.LoginSucceeded += (s, e) => { DialogResult = true; Close(); };
            vm.PasswordResetRequested += (s, e) => ShowPasswordResetDialog(userService, vm.Username);
        }

        private void PasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
        {
            if (DataContext is LoginViewModel vm)
                vm.Password = PasswordBox.Password;
        }

        private void ShowPasswordResetDialog(UserAccountService userService, string username)
        {
            var dialog = new PasswordResetWindow(userService, username);
            dialog.Owner = this;
            dialog.ShowDialog();
        }
    }
}