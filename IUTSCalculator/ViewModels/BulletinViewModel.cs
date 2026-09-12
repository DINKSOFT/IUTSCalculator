using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using System.Windows.Input;
using Microsoft.Win32;
using IUTSCalculator.Helpers;
using IUTSCalculator.Models;
using IUTSCalculator.Services;

namespace IUTSCalculator.ViewModels
{
    /// <summary>
    /// ViewModel de l'écran "Bulletin de paie" : permet de sélectionner un employé et une période,
    /// de générer son bulletin de paie (reproduisant le modèle de référence "Option 4") et de
    /// l'exporter en PDF ou en Excel.
    /// </summary>
    public class BulletinViewModel : BaseViewModel
    {
        private readonly DataService _dataService;
        private readonly BulletinService _bulletinService;
        private readonly BulletinExportService _bulletinExportService;

        public ObservableCollection<Employee> Employees { get; } = new ObservableCollection<Employee>();

        private Employee _selectedEmployee;
        public Employee SelectedEmployee
        {
            get => _selectedEmployee;
            set
            {
                if (SetProperty(ref _selectedEmployee, value))
                {
                    (GenererCommand as RelayCommand)?.RaiseCanExecuteChanged();
                }
            }
        }

        private DateTime _periode = DateTime.Now;
        /// <summary>Mois/année de la période de paie (seul le mois et l'année sont pris en compte)</summary>
        public DateTime Periode
        {
            get => _periode;
            set => SetProperty(ref _periode, value);
        }

        private decimal _congesPrisJours;
        public decimal CongesPrisJours
        {
            get => _congesPrisJours;
            set => SetProperty(ref _congesPrisJours, value);
        }

        private string _modeReglement = "Virement bancaire";
        public string ModeReglement
        {
            get => _modeReglement;
            set => SetProperty(ref _modeReglement, value);
        }

        public ObservableCollection<string> ModesReglement { get; } = new ObservableCollection<string>
        {
            "Virement bancaire",
            "Chèque",
            "Espèces"
        };

        private string _numeroCompte;
        public string NumeroCompte
        {
            get => _numeroCompte;
            set => SetProperty(ref _numeroCompte, value);
        }

        /// <summary>Modèles de bulletin proposés dans le sélecteur (Détaillé, Condensé, Cumuls annuels, Bilingue).</summary>
        public System.Collections.Generic.List<BulletinTemplateOption> Templates => BulletinTemplateOption.TousLesModeles;

        private BulletinTemplateOption _selectedTemplateOption = BulletinTemplateOption.TousLesModeles[0];
        public BulletinTemplateOption SelectedTemplateOption
        {
            get => _selectedTemplateOption;
            set => SetProperty(ref _selectedTemplateOption, value);
        }

        private Bulletin _currentBulletin;
        /// <summary>Bulletin de paie actuellement généré, affiché à l'écran</summary>
        public Bulletin CurrentBulletin
        {
            get => _currentBulletin;
            set
            {
                if (SetProperty(ref _currentBulletin, value))
                {
                    OnPropertyChanged(nameof(HasBulletin));
                    OnPropertyChanged(nameof(NoBulletin));
                    (ExportPdfCommand as RelayCommand)?.RaiseCanExecuteChanged();
                    (ExportExcelCommand as RelayCommand)?.RaiseCanExecuteChanged();
                }
            }
        }

        /// <summary>Indique si un bulletin a déjà été généré (pour activer les exports)</summary>
        public bool HasBulletin => CurrentBulletin != null;

        /// <summary>Inverse de <see cref="HasBulletin"/>, utilisé pour afficher le message d'invite initial</summary>
        public bool NoBulletin => CurrentBulletin == null;

        public ICommand GenererCommand { get; }
        public ICommand ExportPdfCommand { get; }
        public ICommand ExportExcelCommand { get; }

        public BulletinViewModel(DataService dataService, BulletinService bulletinService, BulletinExportService bulletinExportService)
        {
            _dataService = dataService ?? throw new ArgumentNullException(nameof(dataService));
            _bulletinService = bulletinService ?? throw new ArgumentNullException(nameof(bulletinService));
            _bulletinExportService = bulletinExportService ?? throw new ArgumentNullException(nameof(bulletinExportService));

            Title = "Bulletin de paie";

            GenererCommand = new RelayCommand(
                execute: async () => await GenererBulletinAsync(),
                canExecute: () => !IsBusy && SelectedEmployee != null);

            ExportPdfCommand = new RelayCommand(
                execute: async () => await ExportPdfAsync(),
                canExecute: () => !IsBusy && HasBulletin);

            ExportExcelCommand = new RelayCommand(
                execute: async () => await ExportExcelAsync(),
                canExecute: () => !IsBusy && HasBulletin);

            _ = LoadEmployeesAsync();
        }

        private async Task LoadEmployeesAsync()
        {
            try
            {
                IsBusy = true;
                StatusMessage = "Chargement des employés...";
                Employees.Clear();
                var list = await _dataService.LoadEmployeesAsync();
                foreach (var emp in list)
                    Employees.Add(emp);
                StatusMessage = $"{Employees.Count} employés chargés.";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Erreur lors du chargement des employés : {ex.Message}";
            }
            finally
            {
                IsBusy = false;
            }
        }

        private async Task GenererBulletinAsync()
        {
            if (SelectedEmployee == null)
            {
                StatusMessage = "Veuillez sélectionner un employé.";
                return;
            }

            try
            {
                IsBusy = true;
                StatusMessage = "Génération du bulletin en cours...";

                var employee = SelectedEmployee;
                var periode = Periode;
                var congesPris = CongesPrisJours;
                var modeReglement = ModeReglement;
                var numeroCompte = NumeroCompte;

                var bulletin = await Task.Run(() => _bulletinService.GenererBulletin(
                    employee, periode, congesPris, modeReglement, numeroCompte));

                CurrentBulletin = bulletin;
                StatusMessage = $"Bulletin généré pour {bulletin.NomComplet} — {bulletin.PeriodeLibelle}.";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Erreur lors de la génération du bulletin : {ex.Message}";
            }
            finally
            {
                IsBusy = false;
            }
        }

        private async Task ExportPdfAsync()
        {
            if (CurrentBulletin == null)
            {
                StatusMessage = "Veuillez d'abord générer un bulletin.";
                return;
            }

            try
            {
                string defaultFileName = $"Bulletin_{CurrentBulletin.NomEmploye}_{CurrentBulletin.PeriodeFin:yyyyMM}.pdf";
                var dialog = new SaveFileDialog
                {
                    Filter = "Fichiers PDF (*.pdf)|*.pdf|Tous les fichiers (*.*)|*.*",
                    DefaultExt = "pdf",
                    FileName = defaultFileName,
                    Title = "Exporter le bulletin en PDF"
                };

                if (dialog.ShowDialog() != true)
                {
                    StatusMessage = "Exportation PDF annulée.";
                    return;
                }

                IsBusy = true;
                StatusMessage = "Exportation PDF en cours...";

                var template = SelectedTemplateOption?.Value ?? BulletinTemplate.Detaille;
                bool success = await _bulletinExportService.ExportToPdfAsync(CurrentBulletin, dialog.FileName, template);

                StatusMessage = success
                    ? $"Bulletin exporté en PDF : {dialog.FileName}"
                    : "Échec de l'exportation PDF.";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Erreur lors de l'export PDF : {ex.Message}";
            }
            finally
            {
                IsBusy = false;
            }
        }

        private async Task ExportExcelAsync()
        {
            if (CurrentBulletin == null)
            {
                StatusMessage = "Veuillez d'abord générer un bulletin.";
                return;
            }

            try
            {
                string defaultFileName = $"Bulletin_{CurrentBulletin.NomEmploye}_{CurrentBulletin.PeriodeFin:yyyyMM}.xlsx";
                var dialog = new SaveFileDialog
                {
                    Filter = "Fichiers Excel (*.xlsx)|*.xlsx|Tous les fichiers (*.*)|*.*",
                    DefaultExt = "xlsx",
                    FileName = defaultFileName,
                    Title = "Exporter le bulletin en Excel"
                };

                if (dialog.ShowDialog() != true)
                {
                    StatusMessage = "Exportation Excel annulée.";
                    return;
                }

                IsBusy = true;
                StatusMessage = "Exportation Excel en cours...";

                var template = SelectedTemplateOption?.Value ?? BulletinTemplate.Detaille;
                bool success = await _bulletinExportService.ExportToExcelAsync(CurrentBulletin, dialog.FileName, template);

                StatusMessage = success
                    ? $"Bulletin exporté en Excel : {dialog.FileName}"
                    : "Échec de l'exportation Excel.";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Erreur lors de l'export Excel : {ex.Message}";
            }
            finally
            {
                IsBusy = false;
            }
        }
    }
}
