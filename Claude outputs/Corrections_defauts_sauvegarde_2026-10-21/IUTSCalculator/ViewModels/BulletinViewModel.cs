using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
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

        // Variables du mois (PAI-02). Les deux services sont facultatifs : sans eux, le
        // bulletin est généré sans apport, exactement comme avant ce branchement.
        private readonly VariablePaieService _variablePaieService;
        private readonly RubriqueService _rubriqueService;

        // Congés et absences (PAI-04). Facultatif comme les précédents : sans lui, le
        // bulletin garde le nombre de jours de congé saisi à la main.
        private readonly AbsenceService _absenceService;

        // Prêts, avances et acomptes (PAI-05). Facultatif de la même façon : sans lui,
        // aucun remboursement n'est retenu et le bulletin est celui d'avant le module.
        private readonly PretService _pretService;

        // Profils de paie (SOC-03). Facultatif comme les précédents : sans lui, un salarié
        // ne reçoit que ce que sa fiche et ses variables du mois portent.
        private readonly ProfilPaieService _profilPaieService;

        // Barème daté applicable à la période (module DEC-02). Facultatif : sans lui, le
        // moteur garde ses valeurs historiques, comme avant ce module.
        private readonly BaremeService _baremeService;

        // Périodes de paie et clôture (PAI-01). Facultatif : sans lui, la génération sur
        // plusieurs mois traite chaque mois comme ouvert, exactement comme le fait déjà
        // « Générer le bulletin » pour un seul mois.
        private readonly PeriodePaieService _periodePaieService;

        // Heures supplémentaires et majorations légales (PAI-03).
        private readonly HeureSupplementaireService _heureSupplementaireService;

        // Grilles salariales et conventions collectives (SOC-05).
        private readonly ConventionCollectiveService _conventionCollectiveService;

        // Saisies-arrêts, cessions et oppositions sur salaire (PAI-06).
        private readonly SaisieOppositionService _saisieOppositionService;

        // Rappels et régularisations de paie (PAI-07).
        private readonly RappelPaieService _rappelPaieService;

        // Avantages en nature (PAI-08).
        private readonly AvantageNatureService _avantageNatureService;

        public ObservableCollection<Employee> Employees { get; } = new ObservableCollection<Employee>();
        public ObservableCollection<Employee> EmployeesFiltres { get; } = new ObservableCollection<Employee>();

        private string _filtreSalarie;
        public string FiltreSalarie
        {
            get => _filtreSalarie;
            set
            {
                if (SetProperty(ref _filtreSalarie, value))
                {
                    AppliquerFiltreSalaries();
                    OnPropertyChanged(nameof(HasFiltreSalarie));
                }
            }
        }

        public bool HasFiltreSalarie => !string.IsNullOrWhiteSpace(FiltreSalarie);

        public ICommand ClearFiltreSalarieCommand { get; }

        private void AppliquerFiltreSalaries()
        {
            EmployeesFiltres.Clear();
            var q = string.IsNullOrWhiteSpace(FiltreSalarie)
                ? (System.Collections.Generic.IEnumerable<Employee>)Employees
                : Employees.Where(e =>
                    (!string.IsNullOrEmpty(e.LastName) && e.LastName.IndexOf(FiltreSalarie, StringComparison.OrdinalIgnoreCase) >= 0) ||
                    (!string.IsNullOrEmpty(e.FirstName) && e.FirstName.IndexOf(FiltreSalarie, StringComparison.OrdinalIgnoreCase) >= 0) ||
                    (!string.IsNullOrEmpty(e.EmployeeId) && e.EmployeeId.IndexOf(FiltreSalarie, StringComparison.OrdinalIgnoreCase) >= 0) ||
                    (!string.IsNullOrEmpty(e.Fonction) && e.Fonction.IndexOf(FiltreSalarie, StringComparison.OrdinalIgnoreCase) >= 0));

            foreach (var e in q)
                EmployeesFiltres.Add(e);

            if (EmployeesFiltres.Count == 1)
            {
                SelectedEmployee = EmployeesFiltres[0];
            }
        }

        private Employee _selectedEmployee;
        public Employee SelectedEmployee
        {
            get => _selectedEmployee;
            set
            {
                if (SetProperty(ref _selectedEmployee, value))
                {
                    (GenererCommand as RelayCommand)?.RaiseCanExecuteChanged();
                    (RappelerCommand as RelayCommand)?.RaiseCanExecuteChanged();
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
        public ICommand RappelerCommand { get; }
        public ICommand ExportPdfCommand { get; }
        public ICommand ExportExcelCommand { get; }

        // ── Génération sur plusieurs mois ────────────────────────────────────
        // Écarté de « Générer le bulletin » : ce dernier affiche un document à l'écran,
        // alors qu'une plage de mois n'a de sens qu'exportée directement en PDF, un
        // fichier par mois, dans un dossier choisi — comme l'écran « Bulletins en masse »,
        // mais pour un seul salarié.

        private DateTime _periodeDebutPlage = DateTime.Now;
        public DateTime PeriodeDebutPlage
        {
            get => _periodeDebutPlage;
            set
            {
                if (SetProperty(ref _periodeDebutPlage, value))
                    ActualiserMoisPlage();
            }
        }

        private DateTime _periodeFinPlage = DateTime.Now;
        public DateTime PeriodeFinPlage
        {
            get => _periodeFinPlage;
            set
            {
                if (SetProperty(ref _periodeFinPlage, value))
                    ActualiserMoisPlage();
            }
        }

        public ObservableCollection<MoisSelectionnable> MoisPlage { get; } = new ObservableCollection<MoisSelectionnable>();

        /// <summary>
        /// Affiche la liste à cocher dès qu'un mois est dans la plage — y compris un seul —
        /// pour que le mécanisme de sélection individuelle (« les deux » : plage + cases)
        /// soit toujours visible, plutôt que de disparaître silencieusement tant que
        /// l'utilisateur n'a pas étendu « Au » au-delà de « Du ».
        /// </summary>
        public bool AfficherSelectionMois => MoisPlage.Count > 0;

        private string _outputFolderPlage;
        public string OutputFolderPlage
        {
            get => _outputFolderPlage;
            set
            {
                if (SetProperty(ref _outputFolderPlage, value))
                    (GenererPlageCommand as RelayCommand)?.RaiseCanExecuteChanged();
            }
        }

        public ObservableCollection<string> JournalPlage { get; } = new ObservableCollection<string>();

        private int _totalGeneresPlage;
        public int TotalGeneresPlage
        {
            get => _totalGeneresPlage;
            set => SetProperty(ref _totalGeneresPlage, value);
        }

        private int _totalEchecsPlage;
        public int TotalEchecsPlage
        {
            get => _totalEchecsPlage;
            set => SetProperty(ref _totalEchecsPlage, value);
        }

        private int _progressTotalPlage;
        public int ProgressTotalPlage
        {
            get => _progressTotalPlage;
            set => SetProperty(ref _progressTotalPlage, value);
        }

        private int _progressCurrentPlage;
        public int ProgressCurrentPlage
        {
            get => _progressCurrentPlage;
            set => SetProperty(ref _progressCurrentPlage, value);
        }

        public ICommand ChoisirDossierPlageCommand { get; }
        public ICommand GenererPlageCommand { get; }

        public BulletinViewModel(
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

            Title = "Bulletin de paie";

            GenererCommand = new RelayCommand(
                execute: async () => await GenererBulletinAsync(),
                canExecute: () => !IsBusy && SelectedEmployee != null);

            RappelerCommand = new RelayCommand(
                execute: async () => await RappelerBulletinAsync(),
                canExecute: () => !IsBusy && SelectedEmployee != null);

            ExportPdfCommand = new RelayCommand(
                execute: async () => await ExportPdfAsync(),
                canExecute: () => !IsBusy && HasBulletin);

            ExportExcelCommand = new RelayCommand(
                execute: async () => await ExportExcelAsync(),
                canExecute: () => !IsBusy && HasBulletin);

            ChoisirDossierPlageCommand = new RelayCommand(ChoisirDossierPlage);

            GenererPlageCommand = new RelayCommand(
                execute: async () => await GenererSurPlageAsync(),
                canExecute: () => !IsBusy && SelectedEmployee != null
                    && !string.IsNullOrEmpty(OutputFolderPlage)
                    && MoisPlage.Any(m => m.EstSelectionne));

            ClearFiltreSalarieCommand = new RelayCommand(() => FiltreSalarie = string.Empty);
            ActualiserMoisPlage();

            _ = LoadEmployeesAsync();
            _ = ChargerAffichageDeviseAsync();
        }

        /// <summary>
        /// Ce ViewModel n'est créé qu'une seule fois (voir MainViewModel) et ne recharge
        /// jamais SelectedEmployee/EmployeesFiltres après ce premier chargement : sans ce
        /// rafraîchissement, revenir sur cet écran après avoir modifié une fiche ailleurs
        /// (Gestion des employés, import CSV) continuait d'afficher les anciennes données
        /// tant que le dossier n'était pas fermé puis rouvert (signalé le 16 septembre 2026,
        /// voir ETAT-DEVELOPPEMENT.md item 21).
        /// </summary>
        protected override void OnIsActiveChanged()
        {
            if (IsActive)
                _ = LoadEmployeesAsync();
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

        private async Task LoadEmployeesAsync()
        {
            try
            {
                IsBusy = true;
                StatusMessage = "Chargement des employés...";

                // Cet écran se rafraîchit désormais aussi au retour sur l'écran (voir
                // OnIsActiveChanged ci-dessous), pas seulement à l'ouverture : conserver
                // l'employé sélectionné par matricule évite de le désélectionner à chaque
                // fois que l'utilisateur revient sur cet écran sans avoir rien à y refaire.
                string idSelectionne = SelectedEmployee?.EmployeeId;

                Employees.Clear();
                var list = await _dataService.LoadEmployeesAsync();
                foreach (var emp in list)
                    Employees.Add(emp);
                AppliquerFiltreSalaries();

                if (!string.IsNullOrEmpty(idSelectionne))
                {
                    var correspondance = EmployeesFiltres.FirstOrDefault(e => e.EmployeeId == idSelectionne);
                    if (correspondance != null)
                        SelectedEmployee = correspondance;
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

        /// <summary>
        /// Avertissement indicatif si le mois n'a jamais été ouvert (module PAI-01). Rien
        /// n'empêche le calcul — signalé par le client le 13 septembre 2026 comme une
        /// lacune, mais un blocage strict casserait tout dossier existant, dont aucun mois
        /// n'a jamais été ouvert explicitement. On prévient donc, sans bloquer.
        /// </summary>
        private async Task<string> AvertissementPeriodeAsync(int annee, int mois)
        {
            if (_periodePaieService == null) return string.Empty;

            try
            {
                var periodePaie = await _periodePaieService.LirePeriodeAsync(annee, mois);
                return periodePaie == null
                    ? " Attention : cette période n'a pas été ouverte (écran « Périodes de paie »)."
                    : string.Empty;
            }
            catch
            {
                return string.Empty;
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

                // Toujours relire la fiche depuis la base avant de calculer : SelectedEmployee
                // vient de la liste chargée une seule fois à l'ouverture de cet écran (aucun
                // bouton d'actualisation ici) et reste donc périmée si la fiche a été modifiée
                // depuis (écran Gestion des employés, import CSV). Sans cette relecture, le
                // bulletin affichait l'ancienne valeur ET, pire, son enregistrement réécrivait
                // ensuite la fiche employé avec cette copie périmée, annulant la modification
                // (signalé le 16 septembre 2026, voir ETAT-DEVELOPPEMENT.md item 19).
                var employee = await _dataService.GetEmployeeByIdAsync(SelectedEmployee.EmployeeId) ?? SelectedEmployee;
                var periode = Periode;
                var congesPris = CongesPrisJours;
                var modeReglement = ModeReglement;
                var numeroCompte = NumeroCompte;

                // Variables du mois : heures supplémentaires, primes, acomptes… Elles sont
                // lues ici et non dans le service de bulletin, qui doit rester purement
                // calculatoire et sans accès à la base.
                var apport = await ChargerApportAsync(employee.EmployeeId, periode);

                var bulletin = await Task.Run(() => _bulletinService.GenererBulletin(
                    employee, periode, congesPris, modeReglement, numeroCompte, apport));

                CurrentBulletin = bulletin;

                // Le bulletin est ENREGISTRÉ, pas seulement affiché : les déclarations
                // CNSS et IUTS, le livre de paie et les cumuls lisent l'historique des
                // calculs, jamais l'écran. Un bulletin qui n'y entre pas n'existe que
                // le temps de la fenêtre.
                //
                // L'enregistrement remplace celui du même salarié pour le même mois :
                // régénérer ne crée pas de doublon.
                string mention = await AvertissementPeriodeAsync(periode.Year, periode.Month);
                foreach (var avertissement in apport.Avertissements)
                    mention += " Attention : " + avertissement + ".";
                try
                {
                    var calcul = _bulletinService.DernierCalcul;
                    if (calcul != null)
                    {
                        await _dataService.EnregistrerBulletinDuMoisAsync(calcul, periode, bulletin);

                        if (_saisieOppositionService != null)
                        {
                            try
                            {
                                decimal netRef = _bulletinService.CalculerNetReference(employee, apport);
                                await _saisieOppositionService.AppliquerEtEnregistrerRetenuesMoisAsync(
                                    periode.Year, periode.Month, employee.EmployeeId, netRef, DateTime.Today);
                            }
                            catch
                            {
                                // Ne bloque pas l'enregistrement du bulletin si le suivi échoue
                            }
                        }

                        if (_rappelPaieService != null)
                        {
                            try
                            {
                                await _rappelPaieService.MarquerImputesMoisAsync(periode.Year, periode.Month, employee.EmployeeId);
                            }
                            catch
                            {
                                // Ne bloque pas l'enregistrement du bulletin si le marquage des rappels échoue
                            }
                        }

                        mention += " Enregistré dans la paie du mois.";
                    }
                }
                catch (InvalidOperationException ex)
                {
                    // Période clôturée : le bulletin reste affichable, il n'est pas enregistré.
                    mention += " " + ex.Message;
                }
                catch (Exception ex)
                {
                    mention += " Affiché mais NON enregistré : " + ex.Message;
                }

                StatusMessage = (bulletin.ADesVariables
                    ? $"Bulletin généré pour {bulletin.NomComplet} — {bulletin.PeriodeLibelle} " +
                      $"({bulletin.LignesVariables.Count} variable(s) du mois prise(s) en compte)."
                    : $"Bulletin généré pour {bulletin.NomComplet} — {bulletin.PeriodeLibelle}.") + mention;
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

        /// <summary>
        /// Réaffiche le bulletin DÉJÀ ENREGISTRÉ pour ce salarié et ce mois, tel qu'il a
        /// été édité — sans rien recalculer.
        ///
        /// C'est la différence avec « Générer » : régénérer repart de la fiche du salarié
        /// telle qu'elle est aujourd'hui, augmentation comprise, et ne redonne donc pas le
        /// bulletin de septembre si le salaire a changé depuis. Un bulletin remis au
        /// salarié doit pouvoir être réédité à l'identique.
        /// </summary>
        private async Task RappelerBulletinAsync()
        {
            if (SelectedEmployee == null)
            {
                StatusMessage = "Veuillez sélectionner un employé.";
                return;
            }

            try
            {
                IsBusy = true;
                StatusMessage = "Recherche du bulletin enregistré...";

                var archive = await _dataService.RappelerBulletinAsync(
                    SelectedEmployee.EmployeeId, Periode);

                if (archive != null)
                {
                    CurrentBulletin = archive;
                    StatusMessage = string.Format(
                        "Bulletin rappelé pour {0} — {1}. Document enregistré, affiché tel qu'il a été édité.",
                        archive.NomComplet, archive.PeriodeLibelle);
                    return;
                }

                // Rien à rappeler : soit le mois n'a pas de bulletin, soit il a été
                // enregistré avant l'archivage. Les deux cas se distinguent, et se
                // règlent différemment.
                bool existe = await _dataService.BulletinEnregistreAsync(
                    SelectedEmployee.EmployeeId, Periode);

                StatusMessage = existe
                    ? "Ce mois a bien un bulletin enregistré, mais il date d'avant l'archivage des " +
                      "documents : seuls ses totaux sont conservés. Régénérez-le pour qu'il soit " +
                      "archivé à son tour."
                    : string.Format("Aucun bulletin enregistré pour {0:MM/yyyy}. Cliquez sur « Générer » pour l'établir.", Periode);
            }
            catch (Exception ex)
            {
                StatusMessage = "Erreur lors du rappel du bulletin : " + ex.Message;
            }
            finally
            {
                IsBusy = false;
                (RappelerCommand as RelayCommand)?.RaiseCanExecuteChanged();
            }
        }

        /// <summary>
        /// Reconstruit la liste à cocher des mois entre <see cref="PeriodeDebutPlage"/> et
        /// <see cref="PeriodeFinPlage"/>, tous cochés par défaut. Appelée à chaque
        /// changement de borne : une plage redéfinie repart d'une sélection complète
        /// plutôt que de tenter de deviner quels mois garder de la précédente.
        /// </summary>
        private void ActualiserMoisPlage()
        {
            MoisPlage.Clear();
            foreach (var mois in MoisSelectionnable.PlageEntre(PeriodeDebutPlage, PeriodeFinPlage))
            {
                mois.PropertyChanged += (s, e) =>
                {
                    if (e.PropertyName == nameof(MoisSelectionnable.EstSelectionne))
                        (GenererPlageCommand as RelayCommand)?.RaiseCanExecuteChanged();
                };
                MoisPlage.Add(mois);
            }

            OnPropertyChanged(nameof(AfficherSelectionMois));
            (GenererPlageCommand as RelayCommand)?.RaiseCanExecuteChanged();
        }

        private void ChoisirDossierPlage()
        {
            using (var dialog = new System.Windows.Forms.FolderBrowserDialog
            {
                Description = "Choisissez le dossier où enregistrer les bulletins PDF",
                ShowNewFolderButton = true
            })
            {
                if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                {
                    OutputFolderPlage = dialog.SelectedPath;
                    StatusMessage = $"Dossier de destination : {OutputFolderPlage}";
                }
            }
        }

        /// <summary>
        /// Génère (ou rappelle, pour un mois clôturé) le bulletin du salarié sélectionné
        /// pour chaque mois coché de la plage, et exporte chacun en PDF dans un
        /// sous-dossier portant son nom — même convention que l'écran « Bulletins en
        /// masse » lorsqu'il traite plusieurs mois.
        ///
        /// Un mois clôturé n'est jamais recalculé : le bulletin déjà archivé pour ce
        /// salarié et ce mois est rappelé tel quel et exporté, sans passer par le moteur.
        /// S'il n'en existe aucun — le mois a été clôturé avant qu'un bulletin n'y soit
        /// établi — le mois est ignoré et signalé, plutôt que d'enfreindre la clôture en
        /// le calculant après coup.
        /// </summary>
        private async Task GenererSurPlageAsync()
        {
            var employee = SelectedEmployee;
            if (employee == null)
            {
                StatusMessage = "Veuillez sélectionner un employé.";
                return;
            }

            var moisChoisis = MoisPlage.Where(m => m.EstSelectionne).OrderBy(m => m.Mois).ToList();
            if (!moisChoisis.Any())
            {
                StatusMessage = "Veuillez sélectionner au moins un mois.";
                return;
            }

            if (string.IsNullOrEmpty(OutputFolderPlage))
            {
                StatusMessage = "Veuillez choisir un dossier de destination.";
                return;
            }

            try
            {
                IsBusy = true;
                JournalPlage.Clear();
                TotalGeneresPlage = 0;
                TotalEchecsPlage = 0;
                ProgressTotalPlage = moisChoisis.Count;
                ProgressCurrentPlage = 0;

                var congesPris = CongesPrisJours;
                var modeReglement = ModeReglement;
                var numeroCompte = NumeroCompte;
                var template = SelectedTemplateOption?.Value ?? BulletinTemplate.Detaille;

                string dossierEmploye = Path.Combine(
                    OutputFolderPlage, NettoyerNomFichier($"{employee.LastName}_{employee.FirstName}"));
                Directory.CreateDirectory(dossierEmploye);

                foreach (var mois in moisChoisis)
                {
                    try
                    {
                        await Task.Yield();

                        bool clos = _periodePaieService != null
                            && await _periodePaieService.EstClotureeAsync(mois.Mois);
                        Bulletin bulletin;

                        if (clos)
                        {
                            bulletin = await _dataService.RappelerBulletinAsync(employee.EmployeeId, mois.Mois);
                            if (bulletin == null)
                            {
                                TotalEchecsPlage++;
                                JournalPlage.Add($"✘ {mois.Libelle} — mois clôturé, aucun bulletin archivé : ignoré");
                                continue;
                            }
                        }
                        else
                        {
                            var apport = await ChargerApportAsync(employee.EmployeeId, mois.Mois);
                            bulletin = await Task.Run(() => _bulletinService.GenererBulletin(
                                employee, mois.Mois, congesPris, modeReglement, numeroCompte, apport));

                            var calcul = _bulletinService.DernierCalcul;
                            if (calcul != null)
                                await _dataService.EnregistrerBulletinDuMoisAsync(calcul, mois.Mois, bulletin);
                        }

                        string fileName = ConstruireNomFichierPlage(bulletin);
                        string filePath = Path.Combine(dossierEmploye, fileName);
                        bool success = await _bulletinExportService.ExportToPdfAsync(bulletin, filePath, template);

                        if (success)
                        {
                            TotalGeneresPlage++;
                            JournalPlage.Add($"✔ {mois.Libelle} — {fileName}");
                        }
                        else
                        {
                            TotalEchecsPlage++;
                            JournalPlage.Add($"✘ {mois.Libelle} — échec de l'export PDF");
                        }
                    }
                    catch (Exception ex)
                    {
                        TotalEchecsPlage++;
                        JournalPlage.Add($"✘ {mois.Libelle} — erreur : {ex.Message}");
                    }
                    finally
                    {
                        ProgressCurrentPlage++;
                    }
                }

                StatusMessage = TotalEchecsPlage == 0
                    ? $"{TotalGeneresPlage} bulletin(s) généré(s) avec succès dans {dossierEmploye}"
                    : $"{TotalGeneresPlage} bulletin(s) généré(s), {TotalEchecsPlage} ignoré(s) ou en échec — voir le journal.";
            }
            finally
            {
                IsBusy = false;
            }
        }

        private static string ConstruireNomFichierPlage(Bulletin bulletin)
        {
            string nom = $"Bulletin_{bulletin.NomEmploye}_{bulletin.PrenomEmploye}_{bulletin.PeriodeFin:yyyyMM}.pdf";
            return NettoyerNomFichier(nom);
        }

        private static string NettoyerNomFichier(string nom)
        {
            foreach (char c in Path.GetInvalidFileNameChars())
                nom = nom.Replace(c, '_');
            return Regex.Replace(nom, @"\s+", "_");
        }

        /// <summary>
        /// Variables saisies pour ce salarié sur ce mois, traduites en apport pour le
        /// moteur. Renvoie un apport vide — donc un bulletin inchangé — si le module des
        /// variables n'est pas branché ou si la lecture échoue : une variable manquante
        /// est un bulletin à corriger, une exception est un bulletin impossible à sortir.
        /// </summary>
        private async Task<ApportVariables> ChargerApportAsync(string employeeId, DateTime periode)
        {
            // Barème daté applicable à la période (module DEC-02 sur le socle SOC-02) :
            // les taux CNSS et TPA du moteur suivent désormais la période du bulletin.
            if (_baremeService != null)
            {
                try
                {
                    var bareme = await _baremeService.BaremeApplicableAsync(periode);
                    _bulletinService.AppliquerBareme(bareme);
                }
                catch (Exception ex)
                {
                    StatusMessage = "Barème illisible, bulletin généré avec les valeurs historiques : " + ex.Message;
                    _bulletinService.AppliquerBareme(null);
                }
            }

            var apport = ApportVariables.Aucun();

            if (_variablePaieService != null && _rubriqueService != null)
            {
                try
                {
                    var plan = await _rubriqueService.ChargerAsync();
                    apport = await _variablePaieService.ApportAsync(
                        periode.Year, periode.Month, employeeId, plan);
                }
                catch (Exception ex)
                {
                    StatusMessage = "Variables du mois illisibles, bulletin généré sans elles : " + ex.Message;
                }
            }

            // Congés et absences (PAI-04) : jours de congé pris et retenue pour absence
            // non rémunérée rejoignent le même apport, que le moteur reçoit d'un bloc.
            if (_absenceService != null && SelectedEmployee != null)
            {
                try
                {
                    var reglages = await _dataService.LoadSettingsAsync();
                    var baseRetenue = reglages != null
                        ? reglages.BaseRetenueAbsence
                        : BaseRetenueAbsence.TrentiemeMensuel;

                    var absences = await _absenceService.ApportAsync(
                        SelectedEmployee, periode.Year, periode.Month, baseRetenue);

                    apport = apport.AvecAbsences(absences);
                }
                catch (Exception ex)
                {
                    StatusMessage = "Absences illisibles, bulletin généré sans elles : " + ex.Message;
                }
            }

            // Prêts, avances et acomptes (PAI-05) : le remboursement du mois rejoint le
            // même apport, en retenue sur le net. Un échec de lecture ne bloque pas
            // l'édition du bulletin, mais il se dit — un bulletin sans sa retenue de prêt
            // paie au salarié une somme qu'il devra rendre le mois suivant.
            if (_pretService != null)
            {
                try
                {
                    var prets = await _pretService.ApportAsync(employeeId, periode.Year, periode.Month);
                    apport = apport.AvecPrets(prets);
                }
                catch (Exception ex)
                {
                    StatusMessage = "Prêts illisibles, bulletin généré sans retenue de remboursement : " + ex.Message;
                }
            }

            // Profil de paie (SOC-03) : les rubriques que le salarié tient de sa population.
            // Appliqué EN DERNIER, car une rubrique déjà saisie dans les variables du mois
            // écarte celle du profil — la saisie du mois est une décision explicite, le
            // profil n'est qu'un défaut.
            if (_profilPaieService != null && _rubriqueService != null && SelectedEmployee != null)
            {
                try
                {
                    var plan = await _rubriqueService.ChargerAsync();
                    var profil = await _profilPaieService.ApportAsync(
                        SelectedEmployee, periode.Year, periode.Month, plan);

                    apport = apport.AvecProfil(profil);
                }
                catch (Exception ex)
                {
                    StatusMessage = "Profil de paie illisible, bulletin généré sans lui : " + ex.Message;
                }
            }

            // Heures supplémentaires (PAI-03) : valorisation des heures majorées (115%, 130%, 150%, 200%).
            if (_heureSupplementaireService != null && SelectedEmployee != null)
            {
                try
                {
                    var heuresSup = await _heureSupplementaireService.ApportAsync(
                        SelectedEmployee, periode.Year, periode.Month);
                    apport = apport.AvecHeuresSupplementaires(heuresSup);
                }
                catch (Exception ex)
                {
                    StatusMessage = "Heures supplémentaires illisibles, bulletin généré sans elles : " + ex.Message;
                }
            }

            // Conventions collectives et prime d'ancienneté (SOC-05) : valorisation légale conventionnelle.
            if (_conventionCollectiveService != null && SelectedEmployee != null)
            {
                try
                {
                    var apportConv = await _conventionCollectiveService.ApportAsync(
                        SelectedEmployee, periode);
                    apport = apport.AvecConventionCollective(apportConv);
                }
                catch (Exception ex)
                {
                    StatusMessage = "Convention collective illisible, bulletin généré sans prime d'ancienneté : " + ex.Message;
                }
            }

            // Rappels & régularisations (PAI-07) : salaires rétroactifs, primes, trop-perçus, régul IUTS.
            if (_rappelPaieService != null)
            {
                try
                {
                    var rappels = await _rappelPaieService.ApportAsync(periode.Year, periode.Month, employeeId);
                    apport = apport.AvecRappels(rappels);
                }
                catch (Exception ex)
                {
                    StatusMessage = "Rappels et régularisations illisibles, bulletin généré sans eux : " + ex.Message;
                }
            }

            // Avantages en nature (PAI-08) : réintégration fiscale et reprise en bas de bulletin.
            if (_avantageNatureService != null)
            {
                try
                {
                    var avantages = await _avantageNatureService.ObtenirApportMoisAsync(employeeId, periode.Year, periode.Month);
                    apport = apport.AvecAvantagesNature(avantages);
                }
                catch (Exception ex)
                {
                    StatusMessage = "Avantages en nature illisibles, bulletin généré sans eux : " + ex.Message;
                }
            }

            // Saisies-arrêts, cessions et oppositions (PAI-06) : calcul légal de la quotité saisissable sur le net.
            if (_saisieOppositionService != null && SelectedEmployee != null)
            {
                try
                {
                    decimal netRef = _bulletinService.CalculerNetReference(SelectedEmployee, apport);
                    if (netRef > 0m)
                    {
                        var saisies = await _saisieOppositionService.ApportAsync(
                            periode.Year, periode.Month, employeeId, netRef);
                        apport = apport.AvecSaisies(saisies);
                    }
                }
                catch (Exception ex)
                {
                    StatusMessage = "Saisies illisibles, bulletin généré sans retenue judiciaire : " + ex.Message;
                }
            }

            return apport;
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
