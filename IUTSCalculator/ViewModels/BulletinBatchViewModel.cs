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

        // Variables du mois (PAI-02). Facultatifs : sans eux, les bulletins sont produits
        // sans apport, exactement comme avant ce branchement.
        private readonly VariablePaieService _variablePaieService;
        private readonly RubriqueService _rubriqueService;

        // Congés et absences (PAI-04), facultatif comme les précédents.
        private readonly AbsenceService _absenceService;

        // Prêts, avances et acomptes (PAI-05), facultatif comme les précédents.
        private readonly PretService _pretService;

        // Profils de paie (SOC-03), facultatif comme les précédents.
        private readonly ProfilPaieService _profilPaieService;

        // Barème daté applicable à la période (module DEC-02), facultatif comme les
        // précédents : sans lui, le moteur garde ses valeurs historiques.
        private readonly BaremeService _baremeService;

        // Périodes de paie et clôture (PAI-01). Facultatif : sans lui, chaque mois de la
        // plage est traité comme ouvert, exactement comme avant ce branchement.
        private readonly PeriodePaieService _periodePaieService;

        // Heures supplémentaires et majorations légales (PAI-03).
        private readonly HeureSupplementaireService _heureSupplementaireService;

        // Grilles salariales et conventions collectives (SOC-05).
        private readonly ConventionCollectiveService _conventionCollectiveService;

        // Saisies-arrêts, cessions et oppositions (PAI-06).
        private readonly SaisieOppositionService _saisieOppositionService;

        // Rappels et régularisations de paie (PAI-07).
        private readonly RappelPaieService _rappelPaieService;

        // Avantages en nature (PAI-08).
        private readonly AvantageNatureService _avantageNatureService;

        public ObservableCollection<SelectableEmployee> Employees { get; } = new ObservableCollection<SelectableEmployee>();

        /// <summary>Journal des résultats de la dernière génération (un message par employé et par mois)</summary>
        public ObservableCollection<string> Journal { get; } = new ObservableCollection<string>();

        private DateTime _periode = DateTime.Now;
        /// <summary>Premier (ou seul) mois de la plage de paie, appliquée à tous les employés sélectionnés</summary>
        public DateTime Periode
        {
            get => _periode;
            set
            {
                if (SetProperty(ref _periode, value))
                    ActualiserMoisPlage();
            }
        }

        private DateTime _periodeFin = DateTime.Now;
        /// <summary>
        /// Dernier mois de la plage. Égal à <see cref="Periode"/> par défaut — une plage
        /// d'un seul mois — et sans effet s'il lui est antérieur : la plage reste alors
        /// réduite à <see cref="Periode"/> plutôt que de s'inverser.
        /// </summary>
        public DateTime PeriodeFin
        {
            get => _periodeFin;
            set
            {
                if (SetProperty(ref _periodeFin, value))
                    ActualiserMoisPlage();
            }
        }

        public ObservableCollection<MoisSelectionnable> MoisPlage { get; } = new ObservableCollection<MoisSelectionnable>();

        /// <summary>
        /// Affiche la liste à cocher dès qu'un mois est dans la plage — y compris un seul —
        /// pour que la sélection individuelle reste toujours visible.
        /// </summary>
        public bool AfficherSelectionMois => MoisPlage.Count > 0;

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

        public BulletinBatchViewModel(
            DataService dataService,
            BulletinService bulletinService,
            BulletinExportService bulletinExportService,
            VariablePaieService variablePaieService = null,
            RubriqueService rubriqueService = null,
            AbsenceService absenceService = null,
            PretService pretService = null,
            ProfilPaieService profilPaieService = null,
            BaremeService baremeService = null,
            PeriodePaieService periodePaieService = null,
            HeureSupplementaireService heureSupplementaireService = null,
            ConventionCollectiveService conventionCollectiveService = null,
            SaisieOppositionService saisieOppositionService = null,
            RappelPaieService rappelPaieService = null,
            AvantageNatureService avantageNatureService = null)
        {
            _dataService = dataService ?? throw new ArgumentNullException(nameof(dataService));
            _bulletinService = bulletinService ?? throw new ArgumentNullException(nameof(bulletinService));
            _bulletinExportService = bulletinExportService ?? throw new ArgumentNullException(nameof(bulletinExportService));
            _variablePaieService = variablePaieService;
            _rubriqueService = rubriqueService;
            _absenceService = absenceService;
            _pretService = pretService;
            _profilPaieService = profilPaieService;
            _baremeService = baremeService;
            _periodePaieService = periodePaieService;
            _heureSupplementaireService = heureSupplementaireService;
            _conventionCollectiveService = conventionCollectiveService;
            _saisieOppositionService = saisieOppositionService;
            _rappelPaieService = rappelPaieService;
            _avantageNatureService = avantageNatureService;

            Title = "Bulletins de paie en masse";

            SelectionnerToutCommand = new RelayCommand(() => SetAllSelected(true));
            DeselectionnerToutCommand = new RelayCommand(() => SetAllSelected(false));
            ChoisirDossierCommand = new RelayCommand(ChoisirDossier);
            GenererEtExporterCommand = new RelayCommand(
                execute: async () => await GenererEtExporterAsync(),
                canExecute: () => !IsBusy && !string.IsNullOrEmpty(OutputFolder)
                    && Employees.Any(e => e.IsSelected) && MoisPlage.Any(m => m.EstSelectionne));
            OuvrirDossierCommand = new RelayCommand(
                execute: OuvrirDossier,
                canExecute: () => !string.IsNullOrEmpty(OutputFolder) && Directory.Exists(OutputFolder));
            ExporterRegistreCommand = new RelayCommand(
                execute: async () => await ExporterRegistreAsync(),
                canExecute: () => !IsBusy && Employees.Any(e => e.IsSelected));

            ActualiserMoisPlage();

            _ = LoadEmployeesAsync();
            _ = ChargerAffichageDeviseAsync();
        }

        /// <summary>
        /// Reconstruit la liste à cocher des mois entre <see cref="Periode"/> et
        /// <see cref="PeriodeFin"/>, tous cochés par défaut — une plage redéfinie repart
        /// d'une sélection complète plutôt que de deviner quels mois garder de l'ancienne.
        /// </summary>
        private void ActualiserMoisPlage()
        {
            MoisPlage.Clear();
            foreach (var mois in MoisSelectionnable.PlageEntre(Periode, PeriodeFin))
            {
                mois.PropertyChanged += (s, e) =>
                {
                    if (e.PropertyName == nameof(MoisSelectionnable.EstSelectionne))
                        (GenererEtExporterCommand as RelayCommand)?.RaiseCanExecuteChanged();
                };
                MoisPlage.Add(mois);
            }

            OnPropertyChanged(nameof(AfficherSelectionMois));
            (GenererEtExporterCommand as RelayCommand)?.RaiseCanExecuteChanged();
        }


        private bool _afficherDevise = true;
        /// <summary>
        /// Écrire « FCFA » derrière les montants des documents exportés. Réglage du
        /// dossier : le choix est retenu d'une session à l'autre, et partagé avec l'autre
        /// écran de bulletins — un même dossier n'a pas à imprimer deux présentations.
        /// </summary>
        public bool AfficherDevise
        {
            get { return _afficherDevise; }
            set
            {
                if (SetProperty(ref _afficherDevise, value))
                {
                    _bulletinExportService.AfficherDevise = value;
                    var _ = EnregistrerAffichageDeviseAsync(value);
                }
            }
        }

        /// <summary>
        /// Lit le réglage de devise du dossier. Silencieux en cas d'échec : l'affichage
        /// des montants ne doit pas empêcher d'éditer un bulletin.
        /// </summary>
        private async Task ChargerAffichageDeviseAsync()
        {
            try
            {
                var reglages = await _dataService.LoadSettingsAsync();
                if (reglages == null) return;

                // Affectation directe du champ : passer par la propriété publique
                // réécrirait aussitôt en base le réglage qu'on vient d'en lire.
                SetProperty(ref _afficherDevise, reglages.AfficherDeviseSurBulletin, nameof(AfficherDevise));
                _bulletinExportService.AfficherDevise = _afficherDevise;
            }
            catch
            {
                _bulletinExportService.AfficherDevise = _afficherDevise;
            }
        }

        private async Task EnregistrerAffichageDeviseAsync(bool valeur)
        {
            try
            {
                await _dataService.UpdateSettingAsync("AfficherDeviseSurBulletin", valeur);
            }
            catch
            {
                // Le réglage reste appliqué à cette session même s'il n'a pas pu être écrit.
            }
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

        /// <summary>
        /// Génère (ou rappelle, pour un mois clôturé) le bulletin de chaque employé
        /// sélectionné, pour chaque mois coché de la plage, et exporte chacun en PDF.
        ///
        /// Une plage d'un seul mois — le cas le plus courant — dépose les PDF à plat dans
        /// le dossier choisi, exactement comme avant cette extension. Dès que plusieurs
        /// mois sont sélectionnés, chaque employé reçoit son propre sous-dossier : sans
        /// ça, ses bulletins de janvier et février porteraient des noms trop proches pour
        /// s'y retrouver dans un dossier qui en contient déjà plusieurs par employé.
        ///
        /// Un mois clôturé n'est jamais recalculé : le bulletin déjà archivé pour ce
        /// salarié et ce mois est rappelé tel quel. S'il n'en existe aucun, le couple
        /// employé/mois est ignoré et signalé au journal plutôt que de recalculer un mois
        /// arrêté.
        /// </summary>
        private async Task GenererEtExporterAsync()
        {
            var selected = Employees.Where(e => e.IsSelected).Select(e => e.Employee).ToList();
            if (!selected.Any())
            {
                StatusMessage = "Veuillez sélectionner au moins un employé.";
                return;
            }

            var moisChoisis = MoisPlage.Where(m => m.EstSelectionne).OrderBy(m => m.Mois).ToList();
            if (!moisChoisis.Any())
            {
                StatusMessage = "Veuillez sélectionner au moins un mois.";
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

                var congesPris = CongesPrisJours;
                var modeReglement = ModeReglement;
                var template = SelectedTemplateOption?.Value ?? BulletinTemplate.Detaille;
                string folder = OutputFolder;
                bool plusieursMois = moisChoisis.Count > 1;

                int total = selected.Count * moisChoisis.Count;
                int index = 0;
                ProgressTotal = total;
                ProgressCurrent = 0;
                int avecVariables = 0;

                var baseRetenue = await BaseRetenueAsync();

                foreach (var mois in moisChoisis)
                {
                    await AppliquerBaremeAsync(mois.Mois);

                    bool clos = _periodePaieService != null
                        && await _periodePaieService.EstClotureeAsync(mois.Mois);

                    // Une seule lecture des variables par mois pour tout l'effectif : les
                    // relire employé par employé recommencerait la lecture autant de fois
                    // qu'il y a de bulletins pour ce mois.
                    var apports = clos
                        ? null
                        : await ChargerApportsAsync(mois.Mois);
                    var absences = clos ? null : await ChargerAbsencesAsync(mois.Mois);
                    var prets = clos ? null : await ChargerPretsAsync(mois.Mois);
                    var profils = clos ? null : await ChargerProfilsAsync(mois.Mois, selected);
                    var heuresSup = clos ? null : await ChargerHeuresSupAsync(mois.Mois, selected);
                    var conventions = clos ? null : await ChargerConventionsAsync(mois.Mois, selected);
                    var rappels = clos ? null : await ChargerRappelsAsync(mois.Mois);
                    var avantages = clos ? null : await ChargerAvantagesNatureAsync(mois.Mois);
                    var saisies = clos ? null : await ChargerSaisiesAsync(mois.Mois, selected, apports, absences, baseRetenue, prets, profils, heuresSup, conventions, rappels, avantages);

                    string prefixeMois = plusieursMois ? $"[{mois.Libelle}] " : string.Empty;

                    foreach (var employee in selected)
                    {
                        index++;
                        StatusMessage = plusieursMois
                            ? $"Génération {index}/{total} : {employee.LastName} {employee.FirstName} — {mois.Libelle}..."
                            : $"Génération {index}/{total} : {employee.LastName} {employee.FirstName}...";

                        // Force un passage du Dispatcher pour que la barre de progression, le
                        // journal et le message de statut se rafraîchissent bien à l'écran à
                        // chaque employé, même quand la génération est très rapide.
                        await Task.Yield();

                        try
                        {
                            Bulletin bulletin;

                            if (clos)
                            {
                                bulletin = await _dataService.RappelerBulletinAsync(employee.EmployeeId, mois.Mois);
                                if (bulletin == null)
                                {
                                    TotalEchecs++;
                                    Journal.Add($"✘ {prefixeMois}{employee.LastName} {employee.FirstName} — " +
                                                "mois clôturé, aucun bulletin archivé : ignoré");
                                    continue;
                                }
                            }
                            else
                            {
                                var apport = ApportDe(apports, employee.EmployeeId)
                                    .AvecAbsences(AbsenceService.Apport(
                                        employee, absences, mois.Mois.Year, mois.Mois.Month, baseRetenue))
                                    .AvecPrets(PretService.ApportDe(prets, employee.EmployeeId))
                                    .AvecHeuresSupplementaires(HeuresSupDe(heuresSup, employee.EmployeeId))
                                    .AvecConventionCollective(ConventionDe(conventions, employee.EmployeeId))
                                    .AvecProfil(ProfilPaieService.ApportDe(profils, employee.EmployeeId))
                                    .AvecRappels(RappelPaieService.ApportDe(rappels, employee.EmployeeId))
                                    .AvecAvantagesNature(AvantageNatureService.ApportDe(avantages, employee.EmployeeId, mois.Mois.Year, mois.Mois.Month))
                                    .AvecSaisies(SaisieOppositionService.ApportDe(saisies, employee.EmployeeId));

                                bulletin = await Task.Run(() => _bulletinService.GenererBulletin(
                                    employee, mois.Mois, congesPris, modeReglement, numeroCompte: null, apport: apport));

                                if (bulletin.ADesVariables) avecVariables++;

                                // Même règle que sur l'écran individuel : le bulletin entre
                                // dans la paie du mois, en remplaçant celui qui s'y trouverait
                                // déjà. C'est cet historique que lisent les déclarations.
                                var calcul = _bulletinService.DernierCalcul;
                                if (calcul != null)
                                {
                                    await _dataService.EnregistrerBulletinDuMoisAsync(calcul, mois.Mois, bulletin);
                                    if (_saisieOppositionService != null)
                                    {
                                        try
                                        {
                                            decimal netRef = _bulletinService.CalculerNetReference(employee, apport);
                                            await _saisieOppositionService.AppliquerEtEnregistrerRetenuesMoisAsync(
                                                mois.Mois.Year, mois.Mois.Month, employee.EmployeeId, netRef, DateTime.Today);
                                        }
                                        catch
                                        {
                                            // Ne bloque pas la génération en masse
                                        }
                                    }

                                    if (_rappelPaieService != null)
                                    {
                                        try
                                        {
                                            await _rappelPaieService.MarquerImputesMoisAsync(
                                                mois.Mois.Year, mois.Mois.Month, employee.EmployeeId);
                                        }
                                        catch
                                        {
                                            // Ne bloque pas la génération en masse
                                        }
                                    }
                                }
                            }

                            string dossierCible = plusieursMois
                                ? CheminDossierEmploye(folder, employee)
                                : folder;
                            if (plusieursMois)
                                Directory.CreateDirectory(dossierCible);

                            string fileName = ConstruireNomFichier(bulletin);
                            string filePath = Path.Combine(dossierCible, fileName);

                            bool success = await _bulletinExportService.ExportToPdfAsync(bulletin, filePath, template);

                            if (success)
                            {
                                TotalGeneres++;
                                Journal.Add($"✔ {prefixeMois}{bulletin.NomComplet} — {fileName}");
                            }
                            else
                            {
                                TotalEchecs++;
                                Journal.Add($"✘ {prefixeMois}{employee.LastName} {employee.FirstName} — échec de l'export PDF");
                            }
                        }
                        catch (Exception ex)
                        {
                            TotalEchecs++;
                            Journal.Add($"✘ {prefixeMois}{employee.LastName} {employee.FirstName} — erreur : {ex.Message}");
                        }
                        finally
                        {
                            ProgressCurrent = index;
                        }
                    }
                }

                string mentionVariables = avecVariables > 0
                    ? $" — variables du mois reprises sur {avecVariables} bulletin(s)"
                    : string.Empty;

                StatusMessage = TotalEchecs == 0
                    ? $"{TotalGeneres} bulletin(s) généré(s) avec succès dans {folder}{mentionVariables}"
                    : $"{TotalGeneres} bulletin(s) généré(s), {TotalEchecs} ignoré(s) ou en échec — voir le journal.";
            }
            finally
            {
                IsBusy = false;
                (OuvrirDossierCommand as RelayCommand)?.RaiseCanExecuteChanged();
            }
        }

        /// <summary>Sous-dossier propre à un employé, utilisé quand la plage couvre plusieurs mois.</summary>
        private static string CheminDossierEmploye(string dossierRacine, Employee employee)
        {
            string nom = $"{employee.LastName}_{employee.FirstName}";
            foreach (char c in Path.GetInvalidFileNameChars())
                nom = nom.Replace(c, '_');
            nom = Regex.Replace(nom, @"\s+", "_");
            return Path.Combine(dossierRacine, nom);
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
                await AppliquerBaremeAsync(periode);
                var congesPris = CongesPrisJours;
                var modeReglement = ModeReglement;

                // Le registre doit porter exactement les mêmes montants que les bulletins
                // individuels : il reprend donc les mêmes variables du mois.
                var apportsRegistre = await ChargerApportsAsync(periode);
                var absencesRegistre = await ChargerAbsencesAsync(periode);
                var pretsRegistre = await ChargerPretsAsync(periode);
                var profilsRegistre = await ChargerProfilsAsync(periode, selected);
                var baseRetenueRegistre = await BaseRetenueAsync();
                var heuresSupRegistre = await ChargerHeuresSupAsync(periode, selected);
                var conventionsRegistre = await ChargerConventionsAsync(periode, selected);
                var saisiesRegistre = await ChargerSaisiesAsync(periode, selected, apportsRegistre, absencesRegistre, baseRetenueRegistre, pretsRegistre, profilsRegistre, heuresSupRegistre, conventionsRegistre);

                var bulletins = await Task.Run(() => selected
                    .Select(e => _bulletinService.GenererBulletin(
                        e, periode, congesPris, modeReglement, numeroCompte: null,
                        apport: ApportDe(apportsRegistre, e.EmployeeId)
                            .AvecAbsences(AbsenceService.Apport(
                                e, absencesRegistre, periode.Year, periode.Month, baseRetenueRegistre))
                            .AvecPrets(PretService.ApportDe(pretsRegistre, e.EmployeeId))
                            .AvecHeuresSupplementaires(HeuresSupDe(heuresSupRegistre, e.EmployeeId))
                            .AvecConventionCollective(ConventionDe(conventionsRegistre, e.EmployeeId))
                            .AvecProfil(ProfilPaieService.ApportDe(profilsRegistre, e.EmployeeId))
                            .AvecSaisies(SaisieOppositionService.ApportDe(saisiesRegistre, e.EmployeeId))))
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

        /// <summary>
        /// Répercute sur le moteur le barème daté applicable à la période (module DEC-02),
        /// une seule fois pour tout le lot — tous les employés sélectionnés partagent la
        /// même période. Silencieux en cas d'échec : des bulletins avec les valeurs
        /// historiques valent mieux qu'un lot qui refuse de sortir.
        /// </summary>
        private async Task AppliquerBaremeAsync(DateTime periode)
        {
            if (_baremeService == null) return;

            try
            {
                var bareme = await _baremeService.BaremeApplicableAsync(periode);
                _bulletinService.AppliquerBareme(bareme);
            }
            catch (Exception ex)
            {
                Journal.Add("⚠ Barème illisible, lot généré avec les valeurs historiques : " + ex.Message);
                _bulletinService.AppliquerBareme(null);
            }
        }

        /// <summary>
        /// Variables du mois de tout l'effectif, indexées par matricule. Un dictionnaire
        /// vide — module non branché ou lecture impossible — produit les bulletins sans
        /// variables plutôt que de faire échouer le lot entier.
        /// </summary>
        private async Task<System.Collections.Generic.Dictionary<string, ApportVariables>> ChargerApportsAsync(
            DateTime periode)
        {
            if (_variablePaieService == null || _rubriqueService == null)
                return new System.Collections.Generic.Dictionary<string, ApportVariables>(
                    StringComparer.OrdinalIgnoreCase);

            try
            {
                var plan = await _rubriqueService.ChargerAsync();
                return await _variablePaieService.ApportsAsync(periode.Year, periode.Month, plan);
            }
            catch (Exception ex)
            {
                Journal.Add("⚠ Variables du mois illisibles, bulletins produits sans elles : " + ex.Message);
                return new System.Collections.Generic.Dictionary<string, ApportVariables>(
                    StringComparer.OrdinalIgnoreCase);
            }
        }

        /// <summary>
        /// Profils applicables à la période, lus une seule fois pour tout l'effectif. Un
        /// dictionnaire vide — module non branché ou lecture impossible — produit les
        /// bulletins sans les profils plutôt que de faire échouer le lot.
        /// </summary>
        private async Task<System.Collections.Generic.Dictionary<string, ApportProfil>> ChargerProfilsAsync(
            DateTime periode, System.Collections.Generic.IEnumerable<Employee> salaries)
        {
            if (_profilPaieService == null || _rubriqueService == null)
                return new System.Collections.Generic.Dictionary<string, ApportProfil>(StringComparer.OrdinalIgnoreCase);

            try
            {
                var plan = await _rubriqueService.ChargerAsync();
                return await _profilPaieService.ApportsAsync(periode.Year, periode.Month, salaries, plan);
            }
            catch (Exception ex)
            {
                Journal.Add("⚠ Profils de paie illisibles, bulletins produits sans eux : " + ex.Message);
                return new System.Collections.Generic.Dictionary<string, ApportProfil>(StringComparer.OrdinalIgnoreCase);
            }
        }

        /// <summary>
        /// Remboursements de prêts de la période, lus une seule fois pour tout l'effectif.
        /// Un dictionnaire vide — module non branché ou lecture impossible — produit les
        /// bulletins sans retenue de remboursement plutôt que de faire échouer le lot.
        /// </summary>
        private async Task<System.Collections.Generic.Dictionary<string, ApportPrets>> ChargerPretsAsync(DateTime periode)
        {
            if (_pretService == null)
                return new System.Collections.Generic.Dictionary<string, ApportPrets>(StringComparer.OrdinalIgnoreCase);

            try
            {
                return await _pretService.ApportsAsync(periode.Year, periode.Month);
            }
            catch (Exception ex)
            {
                Journal.Add("⚠ Prêts illisibles, bulletins produits sans retenue de remboursement : " + ex.Message);
                return new System.Collections.Generic.Dictionary<string, ApportPrets>(StringComparer.OrdinalIgnoreCase);
            }
        }

        /// <summary>
        /// Absences de la période, lues une seule fois pour tout l'effectif. Une liste
        /// vide — module non branché ou lecture impossible — produit les bulletins sans
        /// absences plutôt que de faire échouer le lot.
        /// </summary>
        private async Task<System.Collections.Generic.List<Absence>> ChargerAbsencesAsync(DateTime periode)
        {
            if (_absenceService == null)
                return new System.Collections.Generic.List<Absence>();

            try
            {
                return await _absenceService.ChargerPeriodeAsync(periode.Year, periode.Month);
            }
            catch (Exception ex)
            {
                Journal.Add("⚠ Absences illisibles, bulletins produits sans elles : " + ex.Message);
                return new System.Collections.Generic.List<Absence>();
            }
        }

        /// <summary>Base de retenue réglée pour ce dossier, 1/30e à défaut.</summary>
        private async Task<BaseRetenueAbsence> BaseRetenueAsync()
        {
            try
            {
                var reglages = await _dataService.LoadSettingsAsync();
                return reglages != null ? reglages.BaseRetenueAbsence : BaseRetenueAbsence.TrentiemeMensuel;
            }
            catch
            {
                return BaseRetenueAbsence.TrentiemeMensuel;
            }
        }

        private static ApportVariables ApportDe(
            System.Collections.Generic.Dictionary<string, ApportVariables> apports, string employeeId)
        {
            ApportVariables apport;
            if (apports != null && !string.IsNullOrWhiteSpace(employeeId)
                && apports.TryGetValue(employeeId, out apport))
            {
                return apport;
            }

            return ApportVariables.Aucun();
        }

        private async Task<System.Collections.Generic.Dictionary<string, ApportHeuresSupplementaires>> ChargerHeuresSupAsync(
            DateTime periode, System.Collections.Generic.IEnumerable<Employee> salaries)
        {
            if (_heureSupplementaireService == null)
                return new System.Collections.Generic.Dictionary<string, ApportHeuresSupplementaires>(StringComparer.OrdinalIgnoreCase);

            try
            {
                return await _heureSupplementaireService.ChargerApportsMoisAsync(periode.Year, periode.Month, salaries);
            }
            catch (Exception ex)
            {
                Journal.Add("⚠ Heures supplémentaires illisibles, bulletins produits sans elles : " + ex.Message);
                return new System.Collections.Generic.Dictionary<string, ApportHeuresSupplementaires>(StringComparer.OrdinalIgnoreCase);
            }
        }

        private static ApportHeuresSupplementaires HeuresSupDe(
            System.Collections.Generic.Dictionary<string, ApportHeuresSupplementaires> heuresSup,
            string employeeId)
        {
            if (heuresSup == null || string.IsNullOrWhiteSpace(employeeId))
                return ApportHeuresSupplementaires.Vide;

            return heuresSup.TryGetValue(employeeId, out var apport) && apport != null
                ? apport
                : ApportHeuresSupplementaires.Vide;
        }

        private async Task<System.Collections.Generic.Dictionary<string, ApportConventionnel>> ChargerConventionsAsync(
            DateTime periode, System.Collections.Generic.IEnumerable<Employee> salaries)
        {
            if (_conventionCollectiveService == null)
                return new System.Collections.Generic.Dictionary<string, ApportConventionnel>(StringComparer.OrdinalIgnoreCase);

            try
            {
                return await _conventionCollectiveService.ChargerApportsMoisAsync(periode, salaries);
            }
            catch (Exception ex)
            {
                Journal.Add("⚠ Conventions collectives illisibles, bulletins produits sans prime d'ancienneté : " + ex.Message);
                return new System.Collections.Generic.Dictionary<string, ApportConventionnel>(StringComparer.OrdinalIgnoreCase);
            }
        }

        private static ApportConventionnel ConventionDe(
            System.Collections.Generic.Dictionary<string, ApportConventionnel> conventions,
            string employeeId)
        {
            if (conventions == null || string.IsNullOrWhiteSpace(employeeId))
                return ApportConventionnel.Vide;

            return conventions.TryGetValue(employeeId, out var apport) && apport != null
                ? apport
                : ApportConventionnel.Vide;
        }

        private async Task<System.Collections.Generic.Dictionary<string, ApportSaisies>> ChargerSaisiesAsync(
            DateTime periode,
            System.Collections.Generic.List<Employee> employees,
            System.Collections.Generic.Dictionary<string, ApportVariables> apports,
            System.Collections.Generic.List<Absence> absences,
            BaseRetenueAbsence baseRetenue,
            System.Collections.Generic.Dictionary<string, ApportPrets> prets,
            System.Collections.Generic.Dictionary<string, ApportProfil> profils,
            System.Collections.Generic.Dictionary<string, ApportHeuresSupplementaires> heuresSup,
            System.Collections.Generic.Dictionary<string, ApportConventionnel> conventions,
            System.Collections.Generic.List<RappelPaie> rappels = null,
            System.Collections.Generic.List<AvantageNature> avantages = null)
        {
            if (_saisieOppositionService == null || employees == null || employees.Count == 0)
                return new System.Collections.Generic.Dictionary<string, ApportSaisies>(StringComparer.OrdinalIgnoreCase);

            try
            {
                var netsParEmploye = new System.Collections.Generic.Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
                foreach (var emp in employees)
                {
                    var apportInter = ApportDe(apports, emp.EmployeeId)
                        .AvecAbsences(AbsenceService.Apport(emp, absences, periode.Year, periode.Month, baseRetenue))
                        .AvecPrets(PretService.ApportDe(prets, emp.EmployeeId))
                        .AvecHeuresSupplementaires(HeuresSupDe(heuresSup, emp.EmployeeId))
                        .AvecConventionCollective(ConventionDe(conventions, emp.EmployeeId))
                        .AvecProfil(ProfilPaieService.ApportDe(profils, emp.EmployeeId))
                        .AvecRappels(RappelPaieService.ApportDe(rappels, emp.EmployeeId))
                        .AvecAvantagesNature(AvantageNatureService.ApportDe(avantages, emp.EmployeeId, periode.Year, periode.Month));

                    decimal netRef = _bulletinService.CalculerNetReference(emp, apportInter);
                    netsParEmploye[emp.EmployeeId] = netRef;
                }

                return await _saisieOppositionService.ApportsAsync(periode.Year, periode.Month, netsParEmploye);
            }
            catch (Exception ex)
            {
                Journal.Add($"⚠ Saisies illisibles pour {periode:MM/yyyy} : {ex.Message}");
                return new System.Collections.Generic.Dictionary<string, ApportSaisies>(StringComparer.OrdinalIgnoreCase);
            }
        }

        private async Task<System.Collections.Generic.List<RappelPaie>> ChargerRappelsAsync(DateTime periode)
        {
            if (_rappelPaieService == null)
                return new System.Collections.Generic.List<RappelPaie>();

            try
            {
                return await _rappelPaieService.ObtenirRappelsAsync(periode.Year, periode.Month, null, StatutRappelPaie.EnAttente);
            }
            catch (Exception ex)
            {
                Journal.Add("⚠ Rappels et régularisations illisibles, bulletins produits sans eux : " + ex.Message);
                return new System.Collections.Generic.List<RappelPaie>();
            }
        }

        private async Task<System.Collections.Generic.List<AvantageNature>> ChargerAvantagesNatureAsync(DateTime periode)
        {
            if (_avantageNatureService == null)
                return new System.Collections.Generic.List<AvantageNature>();

            try
            {
                return await _avantageNatureService.ObtenirAvantagesAsync(null, seulementActifs: true);
            }
            catch (Exception ex)
            {
                Journal.Add("⚠ Avantages en nature illisibles, bulletins produits sans eux : " + ex.Message);
                return new System.Collections.Generic.List<AvantageNature>();
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
