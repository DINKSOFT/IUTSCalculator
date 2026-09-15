using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using IUTSCalculator.Helpers;
using IUTSCalculator.Models;
using IUTSCalculator.Services;

namespace IUTSCalculator.ViewModels
{
    public class EmployeeManagementViewModel : BaseViewModel
    {
        private readonly DataService _dataService;

        /// <summary>
        /// Personnes à charge nominatives (module SAL-01). Optionnel : un appelant qui
        /// ne le fournit pas obtient l'écran tel qu'il était, sans l'onglet des charges.
        /// </summary>
        private readonly PersonneAChargeService _personneAChargeService;

        /// <summary>
        /// Journal d'audit (module ADM-02) : traçabilité des modifications contractuelles
        /// et des mouvements de personnel. Optionnel.
        /// </summary>
        private readonly AuditService _auditService;

        /// <summary>
        /// Conventions collectives et grilles de salaires minima (module SOC-05). Optionnel.
        /// </summary>
        private readonly ConventionCollectiveService _conventionService;

        public ObservableCollection<Employee> Employees { get; } = new ObservableCollection<Employee>();
        public ObservableCollection<ConventionCollective> ConventionsDisponibles { get; } = new ObservableCollection<ConventionCollective>();
        public ObservableCollection<string> EchelonsDisponibles { get; } = new ObservableCollection<string> { "A", "B", "C" };
        public Dictionary<int, string> StatutsSalarieDisponibles { get; } = new Dictionary<int, string>
        {
            { 0, "Actif" },
            { 1, "Sorti (Fin de contrat)" },
            { 2, "Suspendu" }
        };

        public ICollectionView EmployeesView { get; }

        private string _searchText;
        public string SearchText
        {
            get => _searchText;
            set
            {
                if (SetProperty(ref _searchText, value))
                {
                    EmployeesView?.Refresh();
                    OnPropertyChanged(nameof(HasSearchText));
                    OnPropertyChanged(nameof(HasActiveFilters));
                    OnPropertyChanged(nameof(NombreSalariesAffiches));
                    OnPropertyChanged(nameof(TotalSalaireBaseAffiche));
                }
            }
        }

        public bool HasSearchText => !string.IsNullOrWhiteSpace(SearchText);

        private int? _selectedStatutFilter;
        public int? SelectedStatutFilter
        {
            get => _selectedStatutFilter;
            set
            {
                if (SetProperty(ref _selectedStatutFilter, value))
                {
                    EmployeesView?.Refresh();
                    OnPropertyChanged(nameof(HasActiveFilters));
                    OnPropertyChanged(nameof(NombreSalariesAffiches));
                    OnPropertyChanged(nameof(TotalSalaireBaseAffiche));
                }
            }
        }

        private string _selectedCategoryFilter = "Toutes les catégories";
        public string SelectedCategoryFilter
        {
            get => _selectedCategoryFilter;
            set
            {
                if (SetProperty(ref _selectedCategoryFilter, value))
                {
                    EmployeesView?.Refresh();
                    OnPropertyChanged(nameof(HasActiveFilters));
                    OnPropertyChanged(nameof(NombreSalariesAffiches));
                    OnPropertyChanged(nameof(TotalSalaireBaseAffiche));
                }
            }
        }

        public bool HasActiveFilters => HasSearchText || SelectedStatutFilter.HasValue || (!string.IsNullOrWhiteSpace(SelectedCategoryFilter) && SelectedCategoryFilter != "Toutes les catégories");

        public List<KeyValuePair<int?, string>> StatutsFiltreOptions { get; } = new List<KeyValuePair<int?, string>>
        {
            new KeyValuePair<int?, string>(null, "Tous les statuts"),
            new KeyValuePair<int?, string>(0, "Actifs uniquement"),
            new KeyValuePair<int?, string>(1, "Sortis"),
            new KeyValuePair<int?, string>(2, "Suspendus")
        };

        public ObservableCollection<string> CategoriesFiltreOptions { get; } = new ObservableCollection<string> { "Toutes les catégories" };

        public int NombreSalariesAffiches => EmployeesView?.Cast<Employee>().Count() ?? Employees.Count;

        public decimal TotalSalaireBaseAffiche => EmployeesView?.Cast<Employee>().Sum(e => e.BaseSalary) ?? TotalSalaireBase;

        public ICommand ClearSearchCommand { get; }
        public ICommand ResetFiltersCommand { get; }

        private Employee _selectedEmployee;
        public Employee SelectedEmployee
        {
            get => _selectedEmployee;
            set
            {
                if (SetProperty(ref _selectedEmployee, value))
                {
                    // Copie complète pour l'édition : voir Employee.Clone(), qui évite
                    // que les champs recopiés à la main ne soient oubliés.
                    EditEmployee = value != null ? value.Clone() : null;
                    OnPropertyChanged(nameof(EstEnEdition));
                    RafraichirConformite();
                    // Les personnes à charge suivent la fiche ouverte (SAL-01).
                    _ = ChargerPersonnesAsync(value != null ? value.EmployeeId : null);
                    (DeleteEmployeeCommand as RelayCommand)?.RaiseCanExecuteChanged();
                    (EditEmployeeCommand as RelayCommand)?.RaiseCanExecuteChanged();
                }
            }
        }

        private Employee _editEmployee;
        public Employee EditEmployee
        {
            get => _editEmployee;
            set
            {
                if (SetProperty(ref _editEmployee, value))
                {
                    OnPropertyChanged(nameof(FicheCourante));
                    RafraichirConformite();
                }
            }
        }

        private Employee _newEmployee = new Employee();
        public Employee NewEmployee
        {
            get => _newEmployee;
            set
            {
                if (SetProperty(ref _newEmployee, value))
                {
                    OnPropertyChanged(nameof(FicheCourante));
                    RafraichirConformite();
                }
            }
        }

        /// <summary>
        /// Le salarié affiché dans la fiche : celui qui est sélectionné dans la liste,
        /// ou la fiche vierge quand aucun ne l'est. Un seul formulaire sert donc à la
        /// création comme à la modification — seuls les boutons du bas changent.
        /// </summary>
        public Employee FicheCourante => EditEmployee ?? NewEmployee;

        /// <summary>Vrai lorsqu'un salarié existant est sélectionné (mode modification).</summary>
        public bool EstEnEdition => SelectedEmployee != null;

        /// <summary>
        /// Total des salaires de base de l'effectif, affiché en pied de liste : c'est
        /// le contrôle le plus rapide pour repérer une saisie aberrante.
        /// </summary>
        public decimal TotalSalaireBase
        {
            get { return Employees.Sum(e => e.BaseSalary); }
        }

        private void RafraichirTotaux()
        {
            OnPropertyChanged(nameof(TotalSalaireBase));
            EmployeesView?.Refresh();
            OnPropertyChanged(nameof(NombreSalariesAffiches));
            OnPropertyChanged(nameof(TotalSalaireBaseAffiche));
        }

        public ObservableCollection<string> EmployeeCategories => _dataService.EmployeeCategories;

        /// <summary>Valeurs proposées pour la situation matrimoniale du salarié.</summary>
        public ObservableCollection<string> SituationsMatrimoniales { get; } = new ObservableCollection<string>
        {
            "Célibataire",
            "Marié(e)",
            "Divorcé(e)",
            "Veuf(ve)"
        };

        // ══════════════════════════════════════════════════════════════════
        //  PERSONNES À CHARGE (module SAL-01)
        // ══════════════════════════════════════════════════════════════════

        public ObservableCollection<PersonneACharge> PersonnesACharge { get; } =
            new ObservableCollection<PersonneACharge>();

        private PersonneACharge _personneSelectionnee;
        public PersonneACharge PersonneSelectionnee
        {
            get => _personneSelectionnee;
            set
            {
                if (SetProperty(ref _personneSelectionnee, value))
                    (SupprimerPersonneCommand as RelayCommand)?.RaiseCanExecuteChanged();
            }
        }

        /// <summary>Vrai quand le module est branché : conditionne l'affichage du bloc.</summary>
        public bool GereLesPersonnesACharge => _personneAChargeService != null;

        public IEnumerable<LienParente> LiensDisponibles => new[]
        {
            LienParente.Enfant, LienParente.Conjoint, LienParente.Ascendant, LienParente.Autre
        };

        /// <summary>Décompte que la règle d'âge propose pour la fiche ouverte, aujourd'hui.</summary>
        public int DecomptePropose
        {
            get
            {
                var fiche = FicheCourante;
                if (fiche == null) return 0;

                return PersonneAChargeService.Decompte(
                    PersonnesACharge, DateTime.Today, fiche.IsSpouseWorking);
            }
        }

        /// <summary>
        /// Le décompte proposé s'écarte du nombre saisi sur la fiche. On ne corrige pas
        /// d'office : c'est ce nombre-là qui commande la réduction d'IUTS.
        /// </summary>
        public bool DecompteDiffere
        {
            get
            {
                var fiche = FicheCourante;
                if (fiche == null || PersonnesACharge.Count == 0) return false;
                return DecomptePropose != fiche.FamilyCharges;
            }
        }

        public string DecompteLibelle
        {
            get
            {
                var fiche = FicheCourante;
                if (fiche == null) return "";

                if (PersonnesACharge.Count == 0)
                    return "Aucune personne recensée. Le nombre de charges reste celui saisi sur la fiche.";

                var sorties = PersonneAChargeService.SortiesProchaines(PersonnesACharge, DateTime.Today);

                string bilan = DecompteDiffere
                    ? string.Format(
                        "La règle d'âge donne {0} charge(s), la fiche en porte {1}.",
                        DecomptePropose, fiche.FamilyCharges)
                    : string.Format("{0} charge(s) — le décompte concorde avec la fiche.", DecomptePropose);

                if (sorties.Count > 0)
                {
                    bilan += string.Format(
                        " Dans les douze mois, {0} sortira du décompte.",
                        string.Join(", ", sorties.Select(p => p.NomComplet)));
                }

                return bilan;
            }
        }

        public ICommand AjouterPersonneCommand { get; private set; }
        public ICommand SupprimerPersonneCommand { get; private set; }
        public ICommand ReprendreDecompteCommand { get; private set; }

        private void AjouterPersonne()
        {
            var fiche = FicheCourante;
            if (fiche == null) return;

            var personne = new PersonneACharge
            {
                EmployeeId = fiche.EmployeeId,
                Nom = fiche.LastName
            };

            PersonnesACharge.Add(personne);
            PersonneSelectionnee = personne;
            RafraichirDecompte();
        }

        private void SupprimerPersonne()
        {
            if (PersonneSelectionnee == null) return;

            PersonnesACharge.Remove(PersonneSelectionnee);
            PersonneSelectionnee = null;
            RafraichirDecompte();
        }

        /// <summary>
        /// Recopie le décompte proposé dans le champ qui commande le calcul. C'est un
        /// geste explicite, demandé par l'utilisateur — jamais automatique.
        /// </summary>
        private void ReprendreDecompte()
        {
            var fiche = FicheCourante;
            if (fiche == null) return;

            int avant = fiche.FamilyCharges;
            fiche.FamilyCharges = DecomptePropose;
            RafraichirDecompte();

            StatusMessage = string.Format(
                "Charges de famille : {0} au lieu de {1}. Enregistrez la fiche pour que le " +
                "calcul en tienne compte.", fiche.FamilyCharges, avant);
        }

        private void RafraichirDecompte()
        {
            OnPropertyChanged(nameof(DecomptePropose));
            OnPropertyChanged(nameof(DecompteDiffere));
            OnPropertyChanged(nameof(DecompteLibelle));
            (ReprendreDecompteCommand as RelayCommand)?.RaiseCanExecuteChanged();
        }

        private async Task ChargerPersonnesAsync(string employeeId)
        {
            PersonnesACharge.Clear();
            PersonneSelectionnee = null;

            if (_personneAChargeService != null && !string.IsNullOrWhiteSpace(employeeId))
            {
                var liste = await _personneAChargeService.ChargerAsync(employeeId);
                foreach (var personne in liste)
                    PersonnesACharge.Add(personne);
            }

            RafraichirDecompte();
        }

        private async Task EnregistrerPersonnesAsync(string employeeId)
        {
            if (_personneAChargeService == null || string.IsNullOrWhiteSpace(employeeId))
                return;

            foreach (var personne in PersonnesACharge)
                personne.EmployeeId = employeeId;

            await _personneAChargeService.EnregistrerToutAsync(employeeId, PersonnesACharge);
        }

        public ICommand LoadEmployeesCommand { get; }
        public ICommand AddEmployeeCommand { get; }
        public ICommand EditEmployeeCommand { get; }
        public ICommand DeleteEmployeeCommand { get; }
        public ICommand ClearNewEmployeeCommand { get; }
        public ICommand ImportEmployeesCommand { get; }

        /// <summary>Repasse la fiche en création : désélectionne la liste et la vide.</summary>
        public ICommand NouveauSalarieCommand { get; }

        public EmployeeManagementViewModel(DataService dataService)
            : this(dataService, null, null, null)
        {
        }

        public EmployeeManagementViewModel(
            DataService dataService, PersonneAChargeService personneAChargeService)
            : this(dataService, personneAChargeService, null, null)
        {
        }

        public EmployeeManagementViewModel(
            DataService dataService, PersonneAChargeService personneAChargeService, AuditService auditService)
            : this(dataService, personneAChargeService, auditService, null)
        {
        }

        public EmployeeManagementViewModel(
            DataService dataService,
            PersonneAChargeService personneAChargeService,
            AuditService auditService,
            ConventionCollectiveService conventionService)
        {
            _dataService = dataService ?? throw new ArgumentNullException(nameof(dataService));
            _personneAChargeService = personneAChargeService;
            _auditService = auditService;
            _conventionService = conventionService;
            Title = "Gestion des employés";

            AjouterPersonneCommand = new RelayCommand(AjouterPersonne, () => _personneAChargeService != null);
            SupprimerPersonneCommand = new RelayCommand(SupprimerPersonne, () => PersonneSelectionnee != null);
            ReprendreDecompteCommand = new RelayCommand(ReprendreDecompte, () => DecompteDiffere);

            LoadEmployeesCommand = new RelayCommand(async () => await LoadEmployeesAsync());
            AddEmployeeCommand = new RelayCommand(async () => await AddEmployeeAsync(), CanAddEmployee);
            EditEmployeeCommand = new RelayCommand(async () => await EditEmployeeAsync(), () => SelectedEmployee != null && EditEmployee != null);
            DeleteEmployeeCommand = new RelayCommand(async () => await DeleteEmployeeAsync(), () => SelectedEmployee != null);
            ClearNewEmployeeCommand = new RelayCommand(ClearNewEmployee);
            ImportEmployeesCommand = new RelayCommand(async () => await ImportEmployeesAsync());
            NouveauSalarieCommand = new RelayCommand(NouveauSalarie);

            EmployeesView = CollectionViewSource.GetDefaultView(Employees);
            EmployeesView.Filter = FiltrerSalarie;

            ClearSearchCommand = new RelayCommand(() => SearchText = string.Empty);
            ResetFiltersCommand = new RelayCommand(() =>
            {
                SearchText = string.Empty;
                SelectedStatutFilter = null;
                SelectedCategoryFilter = "Toutes les catégories";
            });

            _ = LoadEmployeesAsync();
        }

        private bool FiltrerSalarie(object obj)
        {
            if (!(obj is Employee emp)) return false;

            // Filtre Statut
            if (SelectedStatutFilter.HasValue && emp.StatutSalarie != SelectedStatutFilter.Value)
                return false;

            // Filtre Catégorie
            if (!string.IsNullOrWhiteSpace(SelectedCategoryFilter) && SelectedCategoryFilter != "Toutes les catégories")
            {
                if (!string.Equals(emp.Category, SelectedCategoryFilter, StringComparison.OrdinalIgnoreCase))
                    return false;
            }

            // Recherche textuelle
            if (!string.IsNullOrWhiteSpace(SearchText))
            {
                string q = SearchText.Trim();
                bool match = (emp.LastName != null && emp.LastName.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0)
                          || (emp.FirstName != null && emp.FirstName.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0)
                          || (emp.EmployeeId != null && emp.EmployeeId.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0)
                          || (emp.Fonction != null && emp.Fonction.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0)
                          || (emp.NumeroCnss != null && emp.NumeroCnss.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0)
                          || (emp.NumeroIfu != null && emp.NumeroIfu.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0)
                          || (emp.Telephone != null && emp.Telephone.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0)
                          || (emp.Email != null && emp.Email.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0);
                if (!match) return false;
            }

            return true;
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
                RafraichirTotaux();

                CategoriesFiltreOptions.Clear();
                CategoriesFiltreOptions.Add("Toutes les catégories");
                foreach (var cat in EmployeeCategories)
                {
                    if (!string.IsNullOrWhiteSpace(cat) && !CategoriesFiltreOptions.Contains(cat))
                        CategoriesFiltreOptions.Add(cat);
                }
                _selectedCategoryFilter = "Toutes les catégories";
                OnPropertyChanged(nameof(SelectedCategoryFilter));

                if (_conventionService != null)
                {
                    var convs = await _conventionService.ObtenirToutesConventionsAsync();
                    ConventionsDisponibles.Clear();
                    ConventionsDisponibles.Add(new ConventionCollective { Id = 0, Libelle = "(Aucune convention rattachée)" });
                    foreach (var c in convs)
                    {
                        var comp = await _conventionService.ObtenirParIdAsync(c.Id);
                        ConventionsDisponibles.Add(comp ?? c);
                    }
                }

                RafraichirConformite();
                StatusMessage = $"{Employees.Count} employés chargés.";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Erreur lors du chargement : {ex.Message}";
            }
            finally
            {
                IsBusy = false;
            }
        }

        private bool CanAddEmployee()
        {
            return !IsBusy
                && !string.IsNullOrWhiteSpace(NewEmployee.FirstName)
                && !string.IsNullOrWhiteSpace(NewEmployee.LastName)
                && !string.IsNullOrWhiteSpace(NewEmployee.Category);
        }

        private async Task AddEmployeeAsync()
        {
            try
            {
                IsBusy = true;
                StatusMessage = "Ajout de l'employé...";
                if (string.IsNullOrEmpty(NewEmployee.EmployeeId))
                    NewEmployee.EmployeeId = Guid.NewGuid().ToString();

                await _dataService.AddEmployeeAsync(NewEmployee);
                await EnregistrerPersonnesAsync(NewEmployee.EmployeeId);

                if (_auditService != null)
                {
                    await _auditService.AuditerCreationEmployeAsync(NewEmployee);
                }

                Employees.Add(NewEmployee.Clone());
                RafraichirTotaux();

                StatusMessage = "Employé ajouté avec succès.";
                ClearNewEmployee();
                RafraichirConformite();
            }
            catch (Exception ex)
            {
                StatusMessage = $"Erreur lors de l'ajout : {ex.Message}";
            }
            finally
            {
                IsBusy = false;
            }
        }

        private async Task EditEmployeeAsync()
        {
            if (SelectedEmployee == null || EditEmployee == null)
                return;

            try
            {
                IsBusy = true;
                StatusMessage = "Modification de l'employé...";

                var ancien = SelectedEmployee;
                await _dataService.UpdateEmployeeAsync(EditEmployee);
                await EnregistrerPersonnesAsync(EditEmployee.EmployeeId);

                if (_auditService != null && ancien != null)
                {
                    await _auditService.AuditerModificationEmployeAsync(ancien, EditEmployee);
                }

                // Mettre à jour la liste locale
                var index = Employees.IndexOf(SelectedEmployee);
                if (index >= 0)
                    Employees[index] = EditEmployee.Clone();
                RafraichirTotaux();

                StatusMessage = "Employé modifié avec succès.";
                SelectedEmployee = null;
            }
            catch (Exception ex)
            {
                StatusMessage = $"Erreur lors de la modification : {ex.Message}";
            }
            finally
            {
                IsBusy = false;
            }
        }

        private async Task DeleteEmployeeAsync()
        {
            if (SelectedEmployee == null)
                return;

            try
            {
                IsBusy = true;
                StatusMessage = "Suppression de l'employé...";

                var toDelete = SelectedEmployee;
                await _dataService.DeleteEmployeeAsync(SelectedEmployee.EmployeeId);

                if (_auditService != null && toDelete != null)
                {
                    await _auditService.AuditerSuppressionEmployeAsync(toDelete);
                }

                Employees.Remove(SelectedEmployee);
                RafraichirTotaux();
                StatusMessage = "Employé supprimé avec succès.";
                SelectedEmployee = null;
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

        private async Task ImportEmployeesAsync()
        {
            try
            {
                var dialog = new Microsoft.Win32.OpenFileDialog
                {
                    Filter = "Fichiers JSON (*.json)|*.json|Tous les fichiers (*.*)|*.*",
                    Title = "Importer des employés"
                };
                if (dialog.ShowDialog() == true)
                {
                    IsBusy = true;
                    StatusMessage = "Importation en cours...";
                    await _dataService.ImportEmployeesFromFileAsync(dialog.FileName);
                    await LoadEmployeesAsync();
                    StatusMessage = "Importation terminée avec succès.";
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"Erreur lors de l'import : {ex.Message}";
            }
            finally
            {
                IsBusy = false;
            }
        }

        private void ClearNewEmployee()
        {
            NewEmployee = new Employee();
            PersonnesACharge.Clear();
            PersonneSelectionnee = null;
            RafraichirDecompte();
        }

        private void NouveauSalarie()
        {
            SelectedEmployee = null;
            NewEmployee = new Employee();
            PersonnesACharge.Clear();
            PersonneSelectionnee = null;
            RafraichirDecompte();
            StatusMessage = "Nouvelle fiche.";
            RafraichirConformite();
        }

        public bool EstNonConformeSalaireMinimum
        {
            get
            {
                var fiche = FicheCourante;
                if (fiche == null || !fiche.ConventionId.HasValue || fiche.ConventionId.Value <= 0 || string.IsNullOrWhiteSpace(fiche.Category))
                    return false;

                var conv = ConventionsDisponibles.FirstOrDefault(c => c.Id == fiche.ConventionId.Value);
                if (conv == null || conv.Grilles == null) return false;

                var ech = string.IsNullOrWhiteSpace(fiche.Echelon) ? "A" : fiche.Echelon;
                var grille = conv.Grilles.FirstOrDefault(g =>
                    string.Equals(g.Categorie, fiche.Category, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(g.Echelon, ech, StringComparison.OrdinalIgnoreCase));

                return grille != null && grille.SalaireMinimum > 0 && fiche.BaseSalary < grille.SalaireMinimum;
            }
        }

        public string MessageConformiteSalaireMinimum
        {
            get
            {
                var fiche = FicheCourante;
                if (fiche == null || !fiche.ConventionId.HasValue || fiche.ConventionId.Value <= 0 || string.IsNullOrWhiteSpace(fiche.Category))
                    return string.Empty;

                var conv = ConventionsDisponibles.FirstOrDefault(c => c.Id == fiche.ConventionId.Value);
                if (conv == null || conv.Grilles == null) return string.Empty;

                var ech = string.IsNullOrWhiteSpace(fiche.Echelon) ? "A" : fiche.Echelon;
                var grille = conv.Grilles.FirstOrDefault(g =>
                    string.Equals(g.Categorie, fiche.Category, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(g.Echelon, ech, StringComparison.OrdinalIgnoreCase));

                if (grille != null && grille.SalaireMinimum > 0 && fiche.BaseSalary < grille.SalaireMinimum)
                {
                    decimal ecart = grille.SalaireMinimum - fiche.BaseSalary;
                    return $"Attention : Le salaire de base ({fiche.BaseSalary:N0} FCFA) est inférieur au minimum conventionnel garanti ({grille.SalaireMinimum:N0} FCFA) pour la catégorie {fiche.Category} éch. {ech}. Écart à combler : {ecart:N0} FCFA.";
                }

                return string.Empty;
            }
        }

        public void RafraichirConformite()
        {
            OnPropertyChanged(nameof(EstNonConformeSalaireMinimum));
            OnPropertyChanged(nameof(MessageConformiteSalaireMinimum));
        }
    }
}