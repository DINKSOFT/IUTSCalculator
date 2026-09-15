using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using IUTSCalculator.ViewModels;

namespace IUTSCalculator.Views
{
    /// <summary>
    /// Logique d'interaction pour SaisieOppositionView.xaml (module PAI-06).
    /// </summary>
    public partial class SaisieOppositionView : UserControl
    {
        public SaisieOppositionView()
        {
            InitializeComponent();
            DataContextChanged += OnDataContextChanged;
            Loaded += (s, e) => FormScrollViewer?.ScrollToTop();
        }

        private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (e.OldValue is INotifyPropertyChanged oldVm)
                oldVm.PropertyChanged -= OnViewModelPropertyChanged;

            if (e.NewValue is INotifyPropertyChanged newVm)
                newVm.PropertyChanged += OnViewModelPropertyChanged;

            FormScrollViewer?.ScrollToTop();
        }

        private void OnViewModelPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(SaisieOppositionViewModel.SelectedSaisie) ||
                e.PropertyName == nameof(SaisieOppositionViewModel.IsEditing))
            {
                FormScrollViewer?.ScrollToTop();
            }
        }
    }
}
