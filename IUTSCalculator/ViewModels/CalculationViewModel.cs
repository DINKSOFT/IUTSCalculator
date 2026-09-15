using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using IUTSCalculator.Models;
using IUTSCalculator.Helpers;
using IUTSCalculator.Services;
using System.Windows;
using System.IO;
using System.Web.Script.Serialization;


namespace IUTSCalculator.ViewModels
{
    /// <summary>
    /// ViewModel pour la vue de calcul d'impôt IUTS
    /// </summary>
    public class CalculationViewModel : BaseViewModel
    {
        public bool CanEditData => UserSession.Instance.CanEdit;
        public bool IsAdmin => UserSession.Instance.IsAdmin;
        public bool IsReadOnly => UserSession.Instance.IsReadOnly;
        private readonly TaxCalculatorService _taxCalculatorService;
        public ObservableCollection<string> EmployeeCategories => _dataService.EmployeeCategories;
        private readonly DataService _dataService;

        // Variables du mois (PAI-02), absences (PAI-04) et prêts (PAI-05). Facultatifs :
        // sans eux, le calcul mensuel est celui d'avant ces modules.
        private readonly VariablePaieService _variablePaieService;
        private readonly RubriqueService _rubriqueService;
        private readonly AbsenceService _absenceService;
        private readonly PretService _pretService;
        private readonly ProfilPaieService _profilPaieService;
        private readonly BaremeService _baremeService;
        private Employee _currentEmployee;
        private TaxCalculation _currentCalculation;
        private string _resultMessage;
        private bool _isNewEmployee;        
        private bool _isAnnual;
        private bool _isRegularization;
        private decimal _previousTaxPaid;

        /// <summary>
        /// Événement déclenché lorsqu'un calcul est terminé
        /// </summary>
        public event EventHandler CalculationCompleted;

        /// <summary>
        /// Période traitée. Un calcul de paie appartient à un mois : c'est de lui que
        /// dépendent les variables et les absences prises en compte, et c'est sous lui
        /// que le calcul est enregistré. Le moteur datait jusqu'ici du jour où il
        /// tournait, si bien qu'un calcul de septembre fait en octobre était déclaré
        /// en octobre.
        /// </summary>
        private int _annee = DateTime.Today.Year;
        public int Annee
        {
            get => _annee;
            set
            {
                if (SetProperty(ref _annee, value))
                    OnPropertyChanged(nameof(PeriodeLibelle));
            }
        }

        private MoisOption _moisSelectionne =
            MoisOption.Tous.FirstOrDefault(m => m.Numero == DateTime.Today.Month);
        public MoisOption MoisSelectionne
        {
            get => _moisSelectionne;
            set
            {
                if (SetProperty(ref _moisSelectionne, value))
                    OnPropertyChanged(nameof(PeriodeLibelle));
            }
        }

        public System.Collections.Generic.IEnumerable<MoisOption> MoisDisponibles => MoisOption.Tous;

        public System.Collections.Generic.IEnumerable<int> AnneesDisponibles =>
            Enumerable.Range(DateTime.Today.Year - 5, 7).ToList();

        public int Mois => MoisSelectionne != null ? MoisSelectionne.Numero : DateTime.Today.Month;

        /// <summary>Dernier jour du mois traité : la date sous laquelle le calcul est enregistré.</summary>
        public DateTime PeriodeDate => new DateTime(Annee, Mois, 1).AddMonths(1).AddDays(-1);

        public string PeriodeLibelle =>
            new DateTime(Annee, Mois, 1).ToString("MMMM yyyy", new System.Globalization.CultureInfo("fr-FR"));

        /// <summary>
        /// Employé actuellement sélectionné ou créé
        /// </summary>
        public Employee CurrentEmployee
        {
            get => _currentEmployee;
            set => SetProperty(ref _currentEmployee, value);
        }

        /// <summary>
        /// Calcul d'impôt actuel
        /// </summary>
        public TaxCalculation CurrentCalculation
        {
            get => _currentCalculation;
            set => SetProperty(ref _currentCalculation, value);
        }

        /// <summary>
        /// Message de résultat du calcul
        /// </summary>
        public string ResultMessage
        {
            get => _resultMessage;
            set => SetProperty(ref _resultMessage, value);
        }

        /// <summary>
        /// Indique si l'employé courant est nouveau
        /// </summary>
        public bool IsNewEmployee
        {
            get => _isNewEmployee;
            set => SetProperty(ref _isNewEmployee, value);
        }

        /// <summary>
        /// Liste des catégories d'employés disponibles
        /// </summary>
        //public ObservableCollection<string> EmployeeCategories
        //{
        //    get => _employeeCategories;
        //    set => SetProperty(ref _employeeCategories, value);
        //}

        /// <summary>
        /// Indique si le calcul est annuel ou mensuel
        /// </summary>
        public bool IsAnnual
        {
            get => _isAnnual;
            set
            {
                if (SetProperty(ref _isAnnual, value))
                {
                    // Mettre à jour la période de calcul dans le calcul courant
                    if (CurrentCalculation != null)
                    {
                        CurrentCalculation.IsAnnual = value;
                    }
                }
            }
        }

        /// <summary>
        /// Indique si le calcul est une régularisation annuelle
        /// </summary>
        public bool IsRegularization
        {
            get => _isRegularization;
            set
            {
                if (SetProperty(ref _isRegularization, value))
                {
                    // Mettre à jour le mode de régularisation dans le calcul courant
                    if (CurrentCalculation != null)
                    {
                        CurrentCalculation.IsRegularization = value;
                    }
                }
            }
        }

        /// <summary>
        /// Montant d'impôt déjà payé (pour les régularisations)
        /// </summary>
        public decimal PreviousTaxPaid
        {
            get => _previousTaxPaid;
            set => SetProperty(ref _previousTaxPaid, value);
        }

        /// <summary>
        /// Indique si un calcul est actuellement chargé
        /// </summary>
        public bool HasCalculation => CurrentCalculation != null;

        // Commandes
        public ICommand CalculateTaxCommand { get; private set; }
        public ICommand SaveCalculationCommand { get; private set; }
        public ICommand CreateNewEmployeeCommand { get; private set; }
        public ICommand ClearFormCommand { get; private set; }
        public ICommand LoadEmployeeCommand { get; private set; }
        public ICommand ExportExcelCommand { get; private set; }

        /// <summary>
        /// Constructeur
        /// </summary>
        /// <param name="taxCalculatorService">Service de calcul d'impôt</param>
        /// <param name="dataService">Service de données</param>
        public CalculationViewModel(
            TaxCalculatorService taxCalculatorService,
            DataService dataService,
            VariablePaieService variablePaieService = null,
            RubriqueService rubriqueService = null,
            AbsenceService absenceService = null,
            PretService pretService = null,
            ProfilPaieService profilPaieService = null,
            BaremeService baremeService = null)
        {
            _taxCalculatorService = taxCalculatorService ?? throw new ArgumentNullException(nameof(taxCalculatorService));
            _dataService = dataService ?? throw new ArgumentNullException(nameof(dataService));
            _variablePaieService = variablePaieService;
            _rubriqueService = rubriqueService;
            _absenceService = absenceService;
            _pretService = pretService;
            _profilPaieService = profilPaieService;
            _baremeService = baremeService;

            Title = "Calcul d'IUTS";

            // Initialiser les collections
            //"P", "A", "B", "C", "D", "E", "F", "G", "1", "2", "3", "4", "5", "6" valeurs initiales
            //EmployeeCategories = new ObservableCollection<string>
            //{
            //    "Cadre Supérieur", "Cadre Inférieur"
            //};

            // Initialiser les commandes
            InitializeCommands();

            // Créer un nouveau calcul par défaut
            CreateNewCalculation();

            // Charger les paramètres asynchrone
            _ = LoadSettingsAsync();
        }

        /// <summary>
        /// Initialise les commandes du ViewModel
        /// </summary>
        private void InitializeCommands()
        {
            CalculateTaxCommand = new RelayCommand(
                execute: async () => await ExecuteCalculateTaxAsync(),
                canExecute: () => !IsBusy && CurrentEmployee != null);

            SaveCalculationCommand = new RelayCommand(
                execute: async () => await ExecuteSaveCalculationAsync(),
                canExecute: () => !IsBusy && CurrentCalculation != null && CurrentCalculation.SalaireBrut > 0);

            CreateNewEmployeeCommand = new RelayCommand(
                execute: ExecuteCreateNewEmployee,
                canExecute: () => !IsBusy);

            ClearFormCommand = new RelayCommand(
                execute: ExecuteClearForm,
                canExecute: () => !IsBusy);

            LoadEmployeeCommand = new RelayCommand(
                execute: async () => await ExecuteLoadEmployeeAsync(),
                canExecute: () => !IsBusy);

            ExportExcelCommand = new RelayCommand(
                execute: async () => await _dataService.ExportToExcelAsync(CurrentCalculation),
                canExecute: () => !IsBusy && CurrentCalculation != null && CurrentCalculation.SalaireBrut > 0);
        }

        /// <summary>
        /// Crée un nouveau calcul d'impôt
        /// </summary>
        public void CreateNewCalculation()
        {
            // Créer un nouvel employé
            CurrentEmployee = new Employee
            {
                EmployeeId = GenerateEmployeeId(),
                Category = "Cadre Inférieur" // Catégorie par défaut
            };

            // Créer un nouveau calcul
            CurrentCalculation = new TaxCalculation
            {
                Id = Guid.NewGuid().ToString(),
                Employee = CurrentEmployee,
                CalculationDate = PeriodeDate,
                IsAnnual = IsAnnual,
                IsRegularization = IsRegularization
            };

            IsNewEmployee = true;
            ResultMessage = "Nouveau calcul créé. Veuillez remplir les informations de l'employé.";
        }

        /// <summary>
        /// Réinitialise le calcul actuel - cette méthode est ajoutée pour
        /// assurer la compatibilité avec MainWindow.xaml.cs
        /// </summary>
        public void ResetCalculation()
        {
            // Réutilise simplement la méthode CreateNewCalculation()
            CreateNewCalculation();
        }

        /// <summary>
        /// Charge un calcul existant
        /// </summary>
        /// <param name="calculation">Calcul à charger</param>
        public void LoadExistingCalculation(TaxCalculation calculation)
        {
            if (calculation == null)
                throw new ArgumentNullException(nameof(calculation));

            CurrentCalculation = calculation;
            CurrentEmployee = calculation.Employee;
            IsAnnual = calculation.IsAnnual;
            IsRegularization = calculation.IsRegularization;
            IsNewEmployee = false;
            ResultMessage = $"Calcul chargé pour {CurrentEmployee.FirstName} {CurrentEmployee.LastName}";
        }

        /// <summary>
        /// Charge un calcul depuis l'historique - méthode ajoutée pour
        /// assurer la compatibilité avec MainWindow.xaml.cs
        /// </summary>
        /// <param name="calculation">Calcul à charger</param>
        public void LoadFromHistory(TaxCalculation calculation)
        {
            // Réutilise la méthode LoadExistingCalculation
            LoadExistingCalculation(calculation);
        }

        /// <summary>
        /// Vérifie si un calcul peut être exporté
        /// </summary>
        /// <returns>true si un calcul valide existe</returns>
        public bool CanExport()
        {
            return CurrentCalculation != null && CurrentCalculation.SalaireBrut > 0;
        }

        /// <summary>
        /// Exporte le détail du calcul actuel au format JSON
        /// </summary>
        /// <returns>true si l'exportation a réussi</returns>
        public bool ExportCalculation()
        {
            try
            {
                if (CurrentCalculation == null || string.IsNullOrWhiteSpace(CurrentCalculation.CalculationDetails))
                    return false;

                // Préparer l'objet à exporter
                var exportObject = new
                {
                    CurrentCalculation.CalculationDetails
                };

                // Sérialiser en JSON
                var serializer = new JavaScriptSerializer();
                string json = serializer.Serialize(exportObject);

                // Demander à l'utilisateur où enregistrer le fichier
                var dialog = new Microsoft.Win32.SaveFileDialog
                {
                    Title = "Exporter les détails du calcul",
                    Filter = "Fichier JSON (*.json)|*.json",
                    FileName = "CalculationDetails.json"
                };

                if (dialog.ShowDialog() == true)
                {
                    File.WriteAllText(dialog.FileName, json);
                    MessageBox.Show("Exportation réussie.", "Export", MessageBoxButton.OK, MessageBoxImage.Information);
                    return true;
                }

                return false;
            }
            catch (Exception ex)
            {
                MessageBox.Show("Erreur lors de l'exportation : " + ex.Message, "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }
        }

        /// <summary>
        /// Rafraîchit les paramètres de calcul
        /// </summary>
        public async Task RefreshSettingsAsync()
        {
            try
            {
                // Recharger les catégories d'employés et autres paramètres depuis le service de données
                var settings = await _dataService.LoadSettingsAsync();

                // Mettre à jour les catégories si nécessaire
                if (settings != null && settings.EmployeeCategories != null && settings.EmployeeCategories.Count > 0)
                {
                    EmployeeCategories.Clear();
                    foreach (var category in settings.EmployeeCategories)
                    {
                        EmployeeCategories.Add(category);
                    }
                }
            }
            catch (Exception ex)
            {
                ResultMessage = $"Erreur lors du chargement des paramètres: {ex.Message}";
            }
        }

        /// <summary>
        /// Charge les paramètres d'application
        /// </summary>
        private async Task LoadSettingsAsync()
        {
            try
            {
                await RefreshSettingsAsync();
            }
            catch (Exception ex)
            {
                ResultMessage = $"Erreur lors du chargement des paramètres: {ex.Message}";
            }
        }

        /// <summary>
        /// Exécute le calcul d'impôt de façon asynchrone
        /// </summary>
        private async Task ExecuteCalculateTaxAsync()
        {
            try
            {
                IsBusy = true;
                ResultMessage = "Calcul en cours...";

                TaxCalculation result;
                string mentionApport = string.Empty;

                if (IsAnnual || IsRegularization)
                {
                    // Calcul annuel ou régularisation : ce n'est pas la paie d'un mois,
                    // les variables et absences mensuelles n'y ont pas leur place.
                    result = _taxCalculatorService.CalculateTax(
                        CurrentEmployee, IsAnnual, IsRegularization, PreviousTaxPaid);
                }
                else
                {
                    // Paie du mois : mêmes entrées que le bulletin — variables saisies et
                    // absences de la période. Sans cet apport, cet écran et le bulletin
                    // donnaient deux résultats différents pour le même salarié.
                    var apport = await ChargerApportDuMoisAsync(CurrentEmployee);
                    result = _taxCalculatorService.CalculateMonthlyTax(CurrentEmployee, apport);

                    if (!apport.EstVide)
                    {
                        mentionApport = apport.RetenueSurBrut > 0m
                            ? " (variables et absences du mois prises en compte)"
                            : " (variables du mois prises en compte)";
                    }
                }

                // Le résultat complet REMPLACE le calcul courant. Recopier sept champs sur
                // vingt-cinq laissait les cotisations CNSS, l'assiette et la taxe patronale
                // à zéro : le calcul partait ainsi vide dans l'historique, et la déclaration
                // CNSS n'avait rien à déclarer.
                result.Id = CurrentCalculation != null && !string.IsNullOrEmpty(CurrentCalculation.Id)
                    ? CurrentCalculation.Id
                    : Guid.NewGuid().ToString();
                result.Employee = CurrentEmployee;
                result.IsAnnual = IsAnnual;
                result.IsRegularization = IsRegularization;
                result.CalculationDate = PeriodeDate;

                if (IsRegularization)
                {
                    result.TaxDifference = result.IUTSNet - PreviousTaxPaid;
                }

                CurrentCalculation = result;

                ResultMessage = $"Impôt calculé pour {PeriodeLibelle} : {result.IUTSNet:N0} FCFA{mentionApport}";

                // Déclencher l'événement de calcul terminé
                CalculationCompleted?.Invoke(this, EventArgs.Empty);

                // Pour simuler un traitement asynchrone (si l'opération est réellement synchrone)
                await Task.CompletedTask;
            }
            catch (Exception ex)
            {
                ResultMessage = $"Erreur lors du calcul: {ex.Message}";
            }
            finally
            {
                IsBusy = false;
            }
        }

        /// <summary>
        /// Sauvegarde le calcul dans l'historique de façon asynchrone
        /// </summary>
        private async Task ExecuteSaveCalculationAsync()
        {
            try
            {
                IsBusy = true;
                ResultMessage = "Sauvegarde du calcul...";

                if (IsAnnual || IsRegularization)
                {
                    // Un calcul annuel ou une régularisation n'est pas la paie d'un mois :
                    // il s'ajoute à l'historique sans en remplacer aucun.
                    CurrentCalculation.CalculationDate = PeriodeDate;
                    await _dataService.AddCalculationToHistoryAsync(CurrentCalculation);
                    ResultMessage = $"Calcul enregistré sous {PeriodeLibelle}.";
                }
                else
                {
                    // Paie du mois : elle remplace celle du même salarié pour le même mois.
                    // Sans ce remplacement, enregistrer deux fois déclarerait deux salaires.
                    await _dataService.EnregistrerBulletinDuMoisAsync(CurrentCalculation, PeriodeDate);
                    ResultMessage = $"Paie de {PeriodeLibelle} enregistrée pour ce salarié.";
                }
            }
            catch (Exception ex)
            {
                ResultMessage = $"Erreur lors de la sauvegarde: {ex.Message}";
            }
            finally
            {
                IsBusy = false;
            }
        }

        /// <summary>
        /// Variables et absences du mois pour ce salarié, réunies en un seul apport.
        /// Silencieux en cas d'échec : un calcul sans ses variables vaut mieux qu'un
        /// écran qui refuse de calculer, et le message le signale.
        /// </summary>
        private async Task<ApportVariables> ChargerApportDuMoisAsync(Employee salarie)
        {
            // Barème daté applicable à la période (module DEC-02 sur le socle SOC-02) :
            // les taux CNSS et TPA du moteur suivent désormais la période calculée, plutôt
            // que de rester figés aux valeurs historiques. Un dossier qui n'a jamais créé
            // de barème personnalisé n'y voit aucun changement.
            if (_baremeService != null)
            {
                try
                {
                    var bareme = await _baremeService.BaremeApplicableAsync(new DateTime(Annee, Mois, 1));
                    _taxCalculatorService.AppliquerBareme(bareme);
                }
                catch (Exception ex)
                {
                    ResultMessage = "Barème illisible, calcul avec les valeurs historiques : " + ex.Message;
                    _taxCalculatorService.AppliquerBareme(null);
                }
            }

            var apport = ApportVariables.Aucun();

            if (salarie == null || string.IsNullOrWhiteSpace(salarie.EmployeeId))
                return apport;

            if (_variablePaieService != null && _rubriqueService != null)
            {
                try
                {
                    var plan = await _rubriqueService.ChargerAsync();
                    apport = await _variablePaieService.ApportAsync(Annee, Mois, salarie.EmployeeId, plan);
                }
                catch (Exception ex)
                {
                    ResultMessage = "Variables du mois illisibles, calcul sans elles : " + ex.Message;
                }
            }

            if (_absenceService != null)
            {
                try
                {
                    var reglages = await _dataService.LoadSettingsAsync();
                    var baseRetenue = reglages != null
                        ? reglages.BaseRetenueAbsence
                        : BaseRetenueAbsence.TrentiemeMensuel;

                    var absences = await _absenceService.ApportAsync(salarie, Annee, Mois, baseRetenue);
                    apport = apport.AvecAbsences(absences);
                }
                catch (Exception ex)
                {
                    ResultMessage = "Absences illisibles, calcul sans elles : " + ex.Message;
                }
            }

            // Prêts, avances et acomptes (PAI-05) : retenue sur le net, comme sur le
            // bulletin. Les quatre écrans qui produisent la paie prennent les mêmes
            // entrées — sans quoi deux d'entre eux donneraient deux nets pour le même
            // salarié et le même mois.
            if (_pretService != null)
            {
                try
                {
                    var prets = await _pretService.ApportAsync(salarie.EmployeeId, Annee, Mois);
                    apport = apport.AvecPrets(prets);
                }
                catch (Exception ex)
                {
                    ResultMessage = "Prêts illisibles, calcul sans retenue de remboursement : " + ex.Message;
                }
            }

            // Profil de paie (SOC-03), appliqué en dernier : une rubrique déjà saisie dans
            // les variables du mois écarte celle du profil.
            if (_profilPaieService != null && _rubriqueService != null)
            {
                try
                {
                    var plan = await _rubriqueService.ChargerAsync();
                    var profil = await _profilPaieService.ApportAsync(salarie, Annee, Mois, plan);
                    apport = apport.AvecProfil(profil);
                }
                catch (Exception ex)
                {
                    ResultMessage = "Profil de paie illisible, calcul sans lui : " + ex.Message;
                }
            }

            return apport;
        }

        /// <summary>
        /// Crée un nouvel employé
        /// </summary>
        private void ExecuteCreateNewEmployee()
        {
            CreateNewCalculation();
        }

        /// <summary>
        /// Réinitialise le formulaire
        /// </summary>
        private void ExecuteClearForm()
        {
            if (CurrentEmployee != null)
            {
                // Réinitialiser les propriétés mais garder l'employé
                CurrentEmployee.BaseSalary = 0;
                CurrentEmployee.HousingAllowance = 0;
                CurrentEmployee.TransportAllowance = 0;
                CurrentEmployee.FunctionAllowance = 0;
                CurrentEmployee.FamilyCharges = 0;
                CurrentEmployee.IsSpouseWorking = false;
                CurrentEmployee.PensionContribution = 0;

                // Réinitialiser le calcul
                CurrentCalculation = new TaxCalculation
                {
                    Id = Guid.NewGuid().ToString(),
                    Employee = CurrentEmployee,
                    CalculationDate = PeriodeDate,
                    IsAnnual = IsAnnual,
                    IsRegularization = IsRegularization
                };

                ResultMessage = "Formulaire réinitialisé";
            }
        }
        /// <summary>
        /// Charge un employé existant de façon asynchrone
        /// </summary>
        private async Task ExecuteLoadEmployeeAsync()
        {
            try
            {
                IsBusy = true;

                // Ouvrir une boîte de dialogue pour sélectionner un employé
                // Ici, on simule l'opération en chargeant un employé existant

                var employees = await _dataService.LoadEmployeesAsync();
                if (employees.Count > 0)
                {
                    // Prendre le premier employé comme exemple
                    var employee = employees[0];
                    CurrentEmployee = employee;

                    // Créer un nouveau calcul pour cet employé
                    CurrentCalculation = new TaxCalculation
                    {
                        Id = Guid.NewGuid().ToString(),
                        Employee = CurrentEmployee,
                        CalculationDate = PeriodeDate,
                        IsAnnual = IsAnnual,
                        IsRegularization = IsRegularization
                    };

                    IsNewEmployee = false;
                    ResultMessage = $"Employé chargé: {employee.FirstName} {employee.LastName}";
                }
                else
                {
                    ResultMessage = "Aucun employé trouvé";
                }
            }
            catch (Exception ex)
            {
                ResultMessage = $"Erreur lors du chargement: {ex.Message}";
            }
            finally
            {
                IsBusy = false;
            }
        }

        /// <summary>
        /// Génère un nouvel identifiant d'employé
        /// </summary>
        /// <returns>Identifiant unique</returns>
        private string GenerateEmployeeId()
        {
            // Format: E-YYYYMMDD-XXXX (où XXXX est un nombre aléatoire)
            return $"E-{DateTime.Now:yyyyMMdd}-{new Random().Next(1000, 9999)}";
        }

        /// <summary>
        /// Méthode appelée quand le ViewModel devient actif ou inactif
        /// </summary>
        protected override void OnIsActiveChanged()
        {
            if (IsActive)
            {
                // Actions à effectuer quand le ViewModel devient actif
                _ = RefreshSettingsAsync();
            }
        }

        /// <summary>
        /// Méthode synchrone pour la compatibilité avec les implémentations qui n'utilisent pas async/await
        /// </summary>
        public void RefreshSettings()
        {
            _ = RefreshSettingsAsync();
        }

        /// <summary>
        /// Libère les ressources utilisées par le ViewModel
        /// </summary>
        public void Dispose()
        {
            // Libérer les ressources si nécessaire
        }
    }
}