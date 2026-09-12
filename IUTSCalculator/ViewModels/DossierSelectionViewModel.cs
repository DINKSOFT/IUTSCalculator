using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using IUTSCalculator.Helpers;
using IUTSCalculator.Models;
using IUTSCalculator.Services;

namespace IUTSCalculator.ViewModels
{
    /// <summary>
    /// ViewModel de l'écran de sélection/gestion des dossiers (entreprises).
    /// Permet d'ouvrir un dossier existant ou d'en créer, modifier, supprimer un.
    /// </summary>
    public class DossierSelectionViewModel : BaseViewModel
    {
        private readonly DossierRegistryService _registryService;
        private string _editingDossierId;

        public ObservableCollection<Dossier> Dossiers { get; } = new ObservableCollection<Dossier>();

        private Dossier _selectedDossier;
        public Dossier SelectedDossier
        {
            get => _selectedDossier;
            set
            {
                if (SetProperty(ref _selectedDossier, value))
                {
                    RaiseCommandsCanExecuteChanged();
                }
            }
        }

        private bool _isEditing;
        public bool IsEditing
        {
            get => _isEditing;
            set
            {
                if (SetProperty(ref _isEditing, value))
                {
                    RaiseCommandsCanExecuteChanged();
                }
            }
        }

        private Dossier _editingDossier = new Dossier();
        public Dossier EditingDossier
        {
            get => _editingDossier;
            set => SetProperty(ref _editingDossier, value);
        }

        /// <summary>
        /// Déclenché lorsqu'un dossier a été ouvert avec succès (la fenêtre peut alors se fermer).
        /// </summary>
        public event EventHandler DossierOpened;

        public ICommand LoadDossiersCommand { get; }
        public ICommand OpenDossierCommand { get; }
        public ICommand NewDossierCommand { get; }
        public ICommand EditDossierCommand { get; }
        public ICommand SaveDossierCommand { get; }
        public ICommand CancelEditCommand { get; }
        public ICommand DeleteDossierCommand { get; }
        public ICommand ChoisirLogoCommand { get; }

        public DossierSelectionViewModel()
        {
            _registryService = new DossierRegistryService();
            Title = "Sélection du dossier";

            LoadDossiersCommand = new RelayCommand(async () => await LoadDossiersAsync());
            OpenDossierCommand = new RelayCommand(async () => await OpenDossierAsync(), () => SelectedDossier != null && !IsEditing);
            NewDossierCommand = new RelayCommand(StartNewDossier, () => !IsEditing);
            EditDossierCommand = new RelayCommand(StartEditDossier, () => SelectedDossier != null && !IsEditing);
            SaveDossierCommand = new RelayCommand(async () => await SaveDossierAsync(), CanSaveDossier);
            CancelEditCommand = new RelayCommand(CancelEdit, () => IsEditing);
            DeleteDossierCommand = new RelayCommand(async () => await DeleteDossierAsync(), () => SelectedDossier != null && !IsEditing);
            ChoisirLogoCommand = new RelayCommand(ChoisirLogo, () => IsEditing);

            _ = LoadDossiersAsync();
        }

        private async Task LoadDossiersAsync()
        {
            try
            {
                IsBusy = true;
                StatusMessage = "Chargement des dossiers...";

                var list = await _registryService.LoadDossiersAsync();
                Dossiers.Clear();
                foreach (var d in list)
                    Dossiers.Add(d);

                StatusMessage = Dossiers.Count > 0
                    ? $"{Dossiers.Count} dossier(s) disponible(s)."
                    : "Aucun dossier pour l'instant. Créez-en un pour commencer.";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Erreur lors du chargement des dossiers : {ex.Message}";
            }
            finally
            {
                IsBusy = false;
            }
        }

        private async Task OpenDossierAsync()
        {
            if (SelectedDossier == null)
                return;

            try
            {
                IsBusy = true;
                StatusMessage = $"Ouverture du dossier « {SelectedDossier.RaisonSociale} »...";

                DossierSession.Instance.Open(SelectedDossier);
                await _registryService.TouchDossierAsync(SelectedDossier.DossierId);

                DossierOpened?.Invoke(this, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                StatusMessage = $"Erreur lors de l'ouverture du dossier : {ex.Message}";
            }
            finally
            {
                IsBusy = false;
            }
        }

        private void StartNewDossier()
        {
            _editingDossierId = null;
            EditingDossier = new Dossier();
            IsEditing = true;
            StatusMessage = "Renseignez les informations du nouveau dossier.";
        }

        private void StartEditDossier()
        {
            if (SelectedDossier == null)
                return;

            _editingDossierId = SelectedDossier.DossierId;
            EditingDossier = new Dossier
            {
                DossierId = SelectedDossier.DossierId,
                DbFileName = SelectedDossier.DbFileName,
                RaisonSociale = SelectedDossier.RaisonSociale,
                Sigle = SelectedDossier.Sigle,
                IFU = SelectedDossier.IFU,
                RCCM = SelectedDossier.RCCM,
                RegimeFiscal = SelectedDossier.RegimeFiscal,
                Adresse = SelectedDossier.Adresse,
                Ville = SelectedDossier.Ville,
                Telephone = SelectedDossier.Telephone,
                LogoPath = SelectedDossier.LogoPath,
                DateCreation = SelectedDossier.DateCreation,
                DerniereOuverture = SelectedDossier.DerniereOuverture
            };
            IsEditing = true;
            StatusMessage = $"Modification du dossier « {SelectedDossier.RaisonSociale} ».";
        }

        private bool CanSaveDossier()
        {
            return IsEditing && !string.IsNullOrWhiteSpace(EditingDossier?.RaisonSociale);
        }

        private async Task SaveDossierAsync()
        {
            try
            {
                IsBusy = true;

                if (string.IsNullOrEmpty(_editingDossierId))
                {
                    var created = await _registryService.CreateDossierAsync(EditingDossier);
                    Dossiers.Add(created);
                    SelectedDossier = created;
                    StatusMessage = $"Dossier « {created.RaisonSociale} » créé.";
                }
                else
                {
                    await _registryService.UpdateDossierAsync(EditingDossier);
                    var index = Dossiers.ToList().FindIndex(d => d.DossierId == EditingDossier.DossierId);
                    if (index >= 0)
                        Dossiers[index] = EditingDossier;
                    SelectedDossier = EditingDossier;
                    StatusMessage = $"Dossier « {EditingDossier.RaisonSociale} » mis à jour.";
                }

                IsEditing = false;
            }
            catch (Exception ex)
            {
                StatusMessage = $"Erreur lors de l'enregistrement du dossier : {ex.Message}";
            }
            finally
            {
                IsBusy = false;
            }
        }

        private void ChoisirLogo()
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "Images (*.png;*.jpg;*.jpeg;*.bmp)|*.png;*.jpg;*.jpeg;*.bmp|Tous les fichiers (*.*)|*.*",
                Title = "Choisir le logo de l'entreprise"
            };

            if (dialog.ShowDialog() == true)
            {
                EditingDossier.LogoPath = dialog.FileName;
            }
        }

        private void CancelEdit()
        {
            IsEditing = false;
            EditingDossier = new Dossier();
            _editingDossierId = null;
            StatusMessage = "Prêt.";
        }

        private async Task DeleteDossierAsync()
        {
            if (SelectedDossier == null)
                return;

            var confirm = MessageBox.Show(
                $"Supprimer le dossier « {SelectedDossier.RaisonSociale} » ainsi que toutes ses données (employés, calculs) ?\nCette action est irréversible.",
                "Confirmer la suppression",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (confirm != MessageBoxResult.Yes)
                return;

            try
            {
                IsBusy = true;
                await _registryService.DeleteDossierAsync(SelectedDossier.DossierId, deleteDatabaseFile: true);
                Dossiers.Remove(SelectedDossier);
                SelectedDossier = null;
                StatusMessage = "Dossier supprimé.";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Erreur lors de la suppression : {ex.Message}";
            }
            finally
            {
                IsBusy = false;
            }
        }

        private void RaiseCommandsCanExecuteChanged()
        {
            (OpenDossierCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (NewDossierCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (EditDossierCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (SaveDossierCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (CancelEditCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (DeleteDossierCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (ChoisirLogoCommand as RelayCommand)?.RaiseCanExecuteChanged();
        }
    }
}
