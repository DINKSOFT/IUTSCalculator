using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Windows.Input;
using IUTSCalculator.Helpers;
using IUTSCalculator.Models;
using IUTSCalculator.Services;

namespace IUTSCalculator.ViewModels
{
    /// <summary>
    /// ViewModel de l'écran "Bulletins en masse" : permet de sélectionner plusieurs employés
    /// et de générer/exporter leur bulletin de paie (un fichier PDF par employé) pour une
    /// période donnée, en une seule opération.
    /// </summary>
    public class BulletinBatchViewModel : BaseViewModel
    {
        private readonly DataService _dataService;
        private readonly BulletinService _bulletinService;
        private readonly BulletinExportService _bulletinExportService;

        public ObservableCollection<SelectableEmployee> Employees { get; } = new ObservableCollection<SelectableEmployee>();

        /// <summary>Journal des résultats de la dernière génération (un message par employé)</summary>
        public ObservableCollection<string> Journal { get; } = new ObservableCollection<string>();

        private DateTime _periode = DateTime.Now;
        /// <summary>Mois/année de la période de paie, appliquée à tous les employés sélectionnés</summary>
        public DateTime Periode
        {
            get => _periode;
            set => SetProperty(ref _periode, value);
        }

        private decimal _congesPrisJours;
        /// <summary>Jours de congé pris, appliqués uniformément à tous les employés sélectionnés
        /// (à corriger individuellement ensuite via l'écran "Bulletin de paie" si besoin)</summary>
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

        private string _outputFolder;
        /// <summary>Dossier de destination des PDF générés (choisi via le bouton "Choisir le dossier...")</summary>
        public string OutputFolder
        {
            get => _outputFolder;
            set
            {
                if (SetProperty(ref _outputFolder, value))
                {
                    (GenererEtExporterCommand as RelayCommand)?.RaiseCanExecuteChanged();
                }
            }
        }

        private int _totalGeneres;
        public int TotalGeneres
        {
            get => _totalGeneres;
            set => SetProperty(ref _totalGeneres, value);
        }

        private int _totalEchecs;
        public int TotalEchecs
        {
            get => _totalEchecs;
            set => SetProperty(ref _totalEchecs, value);
        }

        public int SelectedCount => Employees.Count(e => e.IsSelected);

        /// <summary>Modèles de bulletin proposés dans le sélecteur (Détaillé, Condensé, Cumuls annuels, Bilingue).</summary>
        public System.Collections.Generic.List<BulletinTemplateOption> Templates => BulletinTemplateOption.TousLesModeles;

        private BulletinTemplateOption _selectedTemplateOption = BulletinTemplateOption.TousLesModeles[0];
        /// <summary>Modèle de bulletin appliqué à chaque PDF généré lors de l'export en masse</summary>
        public BulletinTemplateOption SelectedTemplateOption
        {
            get => _selectedTemplateOption;
            set => SetProperty(ref _selectedTemplateOption, value);
        }

        private int _progressTotal;
        /// <summary>Nombre total d'employés à traiter lors de la génération en cours</summary>
        public int ProgressTotal
        {
            get => _progressTotal;
            set => SetProperty(ref _progressTotal, value);
        }

        private int _progressCurrent;
        /// <summary>Nombre d'employés déjà traités (avec succès ou en échec) lors de la génération en cours</summary>
        public int ProgressCurrent
        {
            get => _progressCurrent;
            set => SetProperty(ref _progressCurrent, value);
        }

        public ICommand SelectionnerToutCommand { get; }
        public ICommand DeselectionnerToutCommand { get; }
        public ICommand ChoisirDossierCommand { get; }
        public ICommand GenererEtExporterCommand { get; }
        public ICommand OuvrirDossierCommand { get; }
        public ICommand ExporterRegistreCommand { get; }

        public BulletinBatchViewModel(DataService dataService, BulletinService bulletinService, BulletinExportService bulletinExportService)
        {
            _dataService = dataService ?? throw new ArgumentNullException(nameof(dataService));
            _bulletinService = bulletinService ?? throw new ArgumentNullException(nameof(bulletinService));
            _bulletinExportService = bulletinExportService ?? throw new ArgumentNullException(nameof(bulletinExportService));

            Title = "Bulletins de paie en masse";

            SelectionnerToutCommand = new RelayCommand(() => SetAllSelected(true));
            DeselectionnerToutCommand = new RelayCommand(() => SetAllSelected(false));
            ChoisirDossierCommand = new RelayCommand(ChoisirDossier);
            GenererEtExporterCommand = new RelayCommand(
                execute: async () => await GenererEtExporterAsync(),
                canExecute: () => !IsBusy && !string.IsNullOrEmpty(OutputFolder) && Employees.Any(e => e.IsSelected));
            OuvrirDossierCommand = new RelayCommand(
                execute: OuvrirDossier,
                canExecute: () => !string.IsNullOrEmpty(OutputFolder) && Directory.Exists(OutputFolder));
            ExporterRegistreCommand = new RelayCommand(
                execute: async () => await ExporterRegistreAsync(),
                canExecute: () => !IsBusy && Employees.Any(e => e.IsSelected));

            _ = LoadEmployeesAsync();
        }

        private void SetAllSelected(bool selected)
        {
            foreach (var e in Employees)
                e.IsSelected = selected;
            OnPropertyChanged(nameof(SelectedCount));
            (GenererEtExporterCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (ExporterRegistreCommand as RelayCommand)?.RaiseCanExecuteChanged();
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
                {
                    var selectable = new SelectableEmployee(emp);
                    selectable.PropertyChanged += (s, e) =>
                    {
                        if (e.PropertyName == nameof(SelectableEmployee.IsSelected))
                        {
                            OnPropertyChanged(nameof(SelectedCount));
                            (GenererEtExporterCommand as RelayCommand)?.RaiseCanExecuteChanged();
                            (ExporterRegistreCommand as RelayCommand)?.RaiseCanExecuteChanged();
                        }
                    };
                    Employees.Add(selectable);
                }
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

        private void ChoisirDossier()
        {
            using (var dialog = new FolderBrowserDialog
            {
                Description = "Choisissez le dossier où enregistrer les bulletins PDF",
                ShowNewFolderButton = true
            })
            {
                if (dialog.ShowDialog() == DialogResult.OK)
                {
                    OutputFolder = dialog.SelectedPath;
                    StatusMessage = $"Dossier de destination : {OutputFolder}";
                }
            }
        }

        private void OuvrirDossier()
        {
            try
            {
                if (!string.IsNullOrEmpty(OutputFolder) && Directory.Exists(OutputFolder))
                    Process.Start("explorer.exe", OutputFolder);
            }
            catch (Exception ex)
            {
                StatusMessage = $"Impossible d'ouvrir le dossier : {ex.Message}";
            }
        }

        private async Task GenererEtExporterAsync()
        {
            var selected = Employees.Where(e => e.IsSelected).Select(e => e.Employee).ToList();
            if (!selected.Any())
            {
                StatusMessage = "Veuillez sélectionner au moins un employé.";
                return;
            }

            if (string.IsNullOrEmpty(OutputFolder))
            {
                StatusMessage = "Veuillez choisir un dossier de destination.";
                return;
            }

            try
            {
                IsBusy = true;
                Journal.Clear();
                TotalGeneres = 0;
                TotalEchecs = 0;

                var periode = Periode;
                var congesPris = CongesPrisJours;
                var modeReglement = ModeReglement;
                var template = SelectedTemplateOption?.Value ?? BulletinTemplate.Detaille;
                string folder = OutputFolder;

                int total = selected.Count;
                int index = 0;
                ProgressTotal = total;
                ProgressCurrent = 0;

                foreach (var employee in selected)
                {
                    index++;
                    StatusMessage = $"Génération {index}/{total} : {employee.LastName} {employee.FirstName}...";

                    // Force un passage du Dispatcher pour que la barre de progression, le journal
                    // et le message de statut se rafraîchissent bien à l'écran à chaque employé,
                    // même quand la génération est très rapide.
                    await Task.Yield();

                    try
                    {
                        var bulletin = await Task.Run(() => _bulletinService.GenererBulletin(
                            employee, periode, congesPris, modeReglement, numeroCompte: null));

                        string fileName = ConstruireNomFichier(bulletin);
                        string filePath = Path.Combine(folder, fileName);

                        bool success = await _bulletinExportService.ExportToPdfAsync(bulletin, filePath, template);

                        if (success)
                        {
                            TotalGeneres++;
                            Journal.Add($"✔ {bulletin.NomComplet} — {fileName}");
                        }
                        else
                        {
                            TotalEchecs++;
                            Journal.Add($"✘ {employee.LastName} {employee.FirstName} — échec de l'export PDF");
                        }
                    }
                    catch (Exception ex)
                    {
                        TotalEchecs++;
                        Journal.Add($"✘ {employee.LastName} {employee.FirstName} — erreur : {ex.Message}");
                    }
                    finally
                    {
                        ProgressCurrent = index;
                    }
                }

                StatusMessage = TotalEchecs == 0
                    ? $"{TotalGeneres} bulletin(s) généré(s) avec succès dans {folder}"
                    : $"{TotalGeneres} bulletin(s) généré(s), {TotalEchecs} échec(s) — voir le journal.";
            }
            finally
            {
                IsBusy = false;
                (OuvrirDossierCommand as RelayCommand)?.RaiseCanExecuteChanged();
            }
        }

        /// <summary>
        /// Génère un document récapitulatif unique (PDF ou Excel selon l'extension choisie)
        /// listant tous les employés sélectionnés — une ligne par employé — en complément
        /// des bulletins individuels. Sert d'archive/registre RH pour la période.
        /// </summary>
        private async Task ExporterRegistreAsync()
        {
            var selected = Employees.Where(e => e.IsSelected).Select(e => e.Employee).ToList();
            if (!selected.Any())
            {
                StatusMessage = "Veuillez sélectionner au moins un employé.";
                return;
            }

            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "Fichier PDF (*.pdf)|*.pdf|Fichier Excel (*.xlsx)|*.xlsx",
                DefaultExt = "pdf",
                FileName = $"Registre_de_paie_{Periode:yyyyMM}.pdf",
                Title = "Exporter le registre de paie"
            };

            if (dialog.ShowDialog() != true)
            {
                StatusMessage = "Export du registre annulé.";
                return;
            }

            try
            {
                IsBusy = true;
                StatusMessage = "Génération du registre de paie...";

                var periode = Periode;
                var congesPris = CongesPrisJours;
                var modeReglement = ModeReglement;

                var bulletins = await Task.Run(() => selected
                    .Select(e => _bulletinService.GenererBulletin(e, periode, congesPris, modeReglement, numeroCompte: null))
                    .ToList());

                bool estExcel = Path.GetExtension(dialog.FileName).Equals(".xlsx", StringComparison.OrdinalIgnoreCase);
                bool success = estExcel
                    ? await _bulletinExportService.ExportRegistrePaieExcelAsync(bulletins, dialog.FileName)
                    : await _bulletinExportService.ExportRegistrePaiePdfAsync(bulletins, dialog.FileName);

                StatusMessage = success
                    ? $"Registre de paie exporté : {dialog.FileName}"
                    : "Échec de l'export du registre de paie.";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Erreur lors de l'export du registre de paie : {ex.Message}";
            }
            finally
            {
                IsBusy = false;
            }
        }

        private static string ConstruireNomFichier(Bulletin bulletin)
        {
            string nom = $"Bulletin_{bulletin.NomEmploye}_{bulletin.PrenomEmploye}_{bulletin.PeriodeFin:yyyyMM}.pdf";
            // Retire les caractères invalides pour un nom de fichier Windows
            foreach (char c in Path.GetInvalidFileNameChars())
                nom = nom.Replace(c, '_');
            return Regex.Replace(nom, @"\s+", "_");
        }
    }
}
