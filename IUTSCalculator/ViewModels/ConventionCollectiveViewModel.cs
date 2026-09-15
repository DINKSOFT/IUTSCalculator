using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using IUTSCalculator.Helpers;
using IUTSCalculator.Models;
using IUTSCalculator.Services;

namespace IUTSCalculator.ViewModels
{
    public class OptionBaseCalcul
    {
        public BaseCalculAnciennete Valeur { get; set; }
        public string Libelle { get; set; }

        public override string ToString() => Libelle;
    }

    /// <summary>
    /// ViewModel de gestion des conventions collectives, des grilles de salaires minima
    /// et de simulation de la prime d'ancienneté (module SOC-05).
    /// </summary>
    public class ConventionCollectiveViewModel : BaseViewModel
    {
        private readonly ConventionCollectiveService _conventionService;
        private readonly DataService _dataService;

        private ConventionCollective _selectedConvention;
        private GrilleSalaire _selectedGrille;
        private TrancheAnciennete _selectedTranche;
        private Employee _selectedEmployeeForSimulation;
        private DateTime _dateSimulation = DateTime.Today;
        private CalculAncienneteResultat _resultatSimulation;

        // Édition convention
        private string _codeConvention;
        private string _libelleConvention;
        private string _descriptionConvention;
        private OptionBaseCalcul _selectedBaseCalculOption;
        private bool _estActiveConvention = true;

        // Édition grille
        private string _categorieGrille;
        private string _echelonGrille;
        private decimal _salaireMinimumGrille;
        private string _descriptionGrille;

        // Édition tranche ancienneté
        private int _anneesMinTranche;
        private int? _anneesMaxTranche;
        private decimal _tauxTranche;

        public ObservableCollection<ConventionCollective> Conventions { get; } = new ObservableCollection<ConventionCollective>();
        public ObservableCollection<GrilleSalaire> Grilles { get; } = new ObservableCollection<GrilleSalaire>();
        public ObservableCollection<TrancheAnciennete> Tranches { get; } = new ObservableCollection<TrancheAnciennete>();
        public ObservableCollection<Employee> Employees { get; } = new ObservableCollection<Employee>();

        public List<OptionBaseCalcul> OptionsBaseCalcul { get; } = new List<OptionBaseCalcul>
        {
            new OptionBaseCalcul
            {
                Valeur = BaseCalculAnciennete.MinimumConventionnel,
                Libelle = "Salaire minimum conventionnel (Art. 37 CCI-BF)"
            },
            new OptionBaseCalcul
            {
                Valeur = BaseCalculAnciennete.SalaireDeBaseReel,
                Libelle = "Salaire de base contractuel réel du salarié"
            }
        };

        public ConventionCollective SelectedConvention
        {
            get => _selectedConvention;
            set
            {
                if (SetProperty(ref _selectedConvention, value))
                {
                    ChargerDetailsConvention();
                    (SupprimerConventionCommand as RelayCommand)?.RaiseCanExecuteChanged();
                }
            }
        }

        public GrilleSalaire SelectedGrille
        {
            get => _selectedGrille;
            set
            {
                if (SetProperty(ref _selectedGrille, value))
                {
                    RemplirChampsGrille();
                    (SupprimerGrilleCommand as RelayCommand)?.RaiseCanExecuteChanged();
                }
            }
        }

        public TrancheAnciennete SelectedTranche
        {
            get => _selectedTranche;
            set
            {
                if (SetProperty(ref _selectedTranche, value))
                {
                    RemplirChampsTranche();
                    (SupprimerTrancheCommand as RelayCommand)?.RaiseCanExecuteChanged();
                }
            }
        }

        public Employee SelectedEmployeeForSimulation
        {
            get => _selectedEmployeeForSimulation;
            set
            {
                if (SetProperty(ref _selectedEmployeeForSimulation, value))
                {
                    ExecuterSimulation();
                }
            }
        }

        public DateTime DateSimulation
        {
            get => _dateSimulation;
            set
            {
                if (SetProperty(ref _dateSimulation, value))
                {
                    ExecuterSimulation();
                }
            }
        }

        public CalculAncienneteResultat ResultatSimulation
        {
            get => _resultatSimulation;
            set => SetProperty(ref _resultatSimulation, value);
        }

        #region Propriétés Formulaire Convention

        public string CodeConvention
        {
            get => _codeConvention;
            set => SetProperty(ref _codeConvention, value);
        }

        public string LibelleConvention
        {
            get => _libelleConvention;
            set => SetProperty(ref _libelleConvention, value);
        }

        public string DescriptionConvention
        {
            get => _descriptionConvention;
            set => SetProperty(ref _descriptionConvention, value);
        }

        public OptionBaseCalcul SelectedBaseCalculOption
        {
            get => _selectedBaseCalculOption;
            set => SetProperty(ref _selectedBaseCalculOption, value);
        }

        public bool EstActiveConvention
        {
            get => _estActiveConvention;
            set => SetProperty(ref _estActiveConvention, value);
        }

        #endregion

        #region Propriétés Formulaire Grille

        public string CategorieGrille
        {
            get => _categorieGrille;
            set => SetProperty(ref _categorieGrille, value);
        }

        public string EchelonGrille
        {
            get => _echelonGrille;
            set => SetProperty(ref _echelonGrille, value);
        }

        public decimal SalaireMinimumGrille
        {
            get => _salaireMinimumGrille;
            set => SetProperty(ref _salaireMinimumGrille, value);
        }

        public string DescriptionGrille
        {
            get => _descriptionGrille;
            set => SetProperty(ref _descriptionGrille, value);
        }

        #endregion

        #region Propriétés Formulaire Tranche

        public int AnneesMinTranche
        {
            get => _anneesMinTranche;
            set => SetProperty(ref _anneesMinTranche, value);
        }

        public int? AnneesMaxTranche
        {
            get => _anneesMaxTranche;
            set => SetProperty(ref _anneesMaxTranche, value);
        }

        public decimal TauxTranche
        {
            get => _tauxTranche;
            set => SetProperty(ref _tauxTranche, value);
        }

        #endregion

        public ICommand ChargerCommand { get; }
        public ICommand NouvelleConventionCommand { get; }
        public ICommand EnregistrerConventionCommand { get; }
        public ICommand SupprimerConventionCommand { get; }

        public ICommand NouvelleGrilleCommand { get; }
        public ICommand EnregistrerGrilleCommand { get; }
        public ICommand SupprimerGrilleCommand { get; }

        public ICommand NouvelleTrancheCommand { get; }
        public ICommand EnregistrerTrancheCommand { get; }
        public ICommand SupprimerTrancheCommand { get; }

        public ICommand SimulerCommand { get; }

        public ConventionCollectiveViewModel(
            ConventionCollectiveService conventionService,
            DataService dataService)
        {
            _conventionService = conventionService ?? throw new ArgumentNullException(nameof(conventionService));
            _dataService = dataService ?? throw new ArgumentNullException(nameof(dataService));

            Title = "Conventions collectives et grilles salariales";

            SelectedBaseCalculOption = OptionsBaseCalcul[0];

            ChargerCommand = new RelayCommand(async () => await ChargerToutAsync());
            NouvelleConventionCommand = new RelayCommand(ReinitialiserChampsConvention);
            EnregistrerConventionCommand = new RelayCommand(async () => await EnregistrerConventionAsync());
            SupprimerConventionCommand = new RelayCommand(
                async () => await SupprimerConventionAsync(),
                () => SelectedConvention != null && SelectedConvention.Id > 0);

            NouvelleGrilleCommand = new RelayCommand(ReinitialiserChampsGrille);
            EnregistrerGrilleCommand = new RelayCommand(async () => await EnregistrerGrilleAsync());
            SupprimerGrilleCommand = new RelayCommand(
                async () => await SupprimerGrilleAsync(),
                () => SelectedGrille != null && SelectedGrille.Id > 0);

            NouvelleTrancheCommand = new RelayCommand(ReinitialiserChampsTranche);
            EnregistrerTrancheCommand = new RelayCommand(async () => await EnregistrerTrancheAsync());
            SupprimerTrancheCommand = new RelayCommand(
                async () => await SupprimerTrancheAsync(),
                () => SelectedTranche != null && SelectedTranche.Id > 0);

            SimulerCommand = new RelayCommand(ExecuterSimulation);
        }

        public async Task ChargerToutAsync()
        {
            try
            {
                IsBusy = true;
                StatusMessage = "Chargement des conventions collectives et salariés...";

                var listeConventions = await _conventionService.ObtenirToutesConventionsAsync();
                Conventions.Clear();
                foreach (var conv in listeConventions)
                {
                    Conventions.Add(conv);
                }

                if (SelectedConvention == null && Conventions.Count > 0)
                {
                    SelectedConvention = Conventions[0];
                }
                else if (SelectedConvention != null)
                {
                    var actuelle = Conventions.FirstOrDefault(c => c.Id == SelectedConvention.Id);
                    SelectedConvention = actuelle ?? (Conventions.Count > 0 ? Conventions[0] : null);
                }

                var listeSalaries = await _dataService.LoadEmployeesAsync();
                Employees.Clear();
                foreach (var emp in listeSalaries)
                {
                    Employees.Add(emp);
                }

                if (SelectedEmployeeForSimulation == null && Employees.Count > 0)
                {
                    SelectedEmployeeForSimulation = Employees[0];
                }

                StatusMessage = $"{Conventions.Count} convention(s) collective(s) chargée(s).";
            }
            catch (Exception ex)
            {
                StatusMessage = "Erreur lors du chargement : " + ex.Message;
            }
            finally
            {
                IsBusy = false;
            }
        }

        private void ChargerDetailsConvention()
        {
            if (SelectedConvention == null)
            {
                Grilles.Clear();
                Tranches.Clear();
                ReinitialiserChampsConvention();
                return;
            }

            CodeConvention = SelectedConvention.Code;
            LibelleConvention = SelectedConvention.Libelle;
            DescriptionConvention = SelectedConvention.Description;
            SelectedBaseCalculOption = OptionsBaseCalcul.FirstOrDefault(o => o.Valeur == SelectedConvention.BaseCalculAnciennete)
                ?? OptionsBaseCalcul[0];
            EstActiveConvention = SelectedConvention.EstActive;

            _ = Task.Run(async () =>
            {
                var convComplete = await _conventionService.ObtenirParIdAsync(SelectedConvention.Id);
                if (convComplete != null)
                {
                    App.Current.Dispatcher.Invoke(() =>
                    {
                        Grilles.Clear();
                        foreach (var g in convComplete.Grilles.OrderBy(g => g.Ordre).ThenBy(g => g.Categorie).ThenBy(g => g.Echelon))
                        {
                            Grilles.Add(g);
                        }

                        Tranches.Clear();
                        foreach (var t in convComplete.TranchesAnciennete.OrderBy(t => t.Ordre).ThenBy(t => t.AnneesMin))
                        {
                            Tranches.Add(t);
                        }

                        ReinitialiserChampsGrille();
                        ReinitialiserChampsTranche();
                        ExecuterSimulation();
                    });
                }
            });
        }

        private void ReinitialiserChampsConvention()
        {
            CodeConvention = string.Empty;
            LibelleConvention = string.Empty;
            DescriptionConvention = string.Empty;
            SelectedBaseCalculOption = OptionsBaseCalcul[0];
            EstActiveConvention = true;
        }

        private void ReinitialiserChampsGrille()
        {
            SelectedGrille = null;
            CategorieGrille = string.Empty;
            EchelonGrille = "A";
            SalaireMinimumGrille = 0m;
            DescriptionGrille = string.Empty;
        }

        private void RemplirChampsGrille()
        {
            if (SelectedGrille == null) return;
            CategorieGrille = SelectedGrille.Categorie;
            EchelonGrille = SelectedGrille.Echelon;
            SalaireMinimumGrille = SelectedGrille.SalaireMinimum;
            DescriptionGrille = SelectedGrille.Description;
        }

        private void ReinitialiserChampsTranche()
        {
            SelectedTranche = null;
            AnneesMinTranche = 2;
            AnneesMaxTranche = null;
            TauxTranche = 5m;
        }

        private void RemplirChampsTranche()
        {
            if (SelectedTranche == null) return;
            AnneesMinTranche = SelectedTranche.AnneesMin;
            AnneesMaxTranche = SelectedTranche.AnneesMax;
            TauxTranche = SelectedTranche.TauxPourcentage;
        }

        private async Task EnregistrerConventionAsync()
        {
            if (string.IsNullOrWhiteSpace(CodeConvention) || string.IsNullOrWhiteSpace(LibelleConvention))
            {
                StatusMessage = "Le code et le libellé de la convention sont obligatoires.";
                return;
            }

            try
            {
                IsBusy = true;
                var conv = new ConventionCollective
                {
                    Id = SelectedConvention?.Id ?? 0,
                    Code = CodeConvention.Trim().ToUpperInvariant(),
                    Libelle = LibelleConvention.Trim(),
                    Description = DescriptionConvention?.Trim(),
                    BaseCalculAnciennete = SelectedBaseCalculOption?.Valeur ?? BaseCalculAnciennete.MinimumConventionnel,
                    EstActive = EstActiveConvention
                };

                await _conventionService.EnregistrerConventionAsync(conv, "Utilisateur");
                StatusMessage = $"Convention '{conv.Libelle}' enregistrée avec succès.";
                await ChargerToutAsync();
            }
            catch (Exception ex)
            {
                StatusMessage = "Erreur lors de l'enregistrement de la convention : " + ex.Message;
            }
            finally
            {
                IsBusy = false;
            }
        }

        private async Task SupprimerConventionAsync()
        {
            if (SelectedConvention == null || SelectedConvention.Id <= 0) return;

            try
            {
                IsBusy = true;
                await _conventionService.SupprimerConventionAsync(SelectedConvention.Id, "Utilisateur");
                StatusMessage = "Convention collective supprimée avec succès.";
                SelectedConvention = null;
                await ChargerToutAsync();
            }
            catch (Exception ex)
            {
                StatusMessage = "Erreur lors de la suppression : " + ex.Message;
            }
            finally
            {
                IsBusy = false;
            }
        }

        private async Task EnregistrerGrilleAsync()
        {
            if (SelectedConvention == null || SelectedConvention.Id <= 0)
            {
                StatusMessage = "Veuillez d'abord sélectionner une convention collective.";
                return;
            }

            if (string.IsNullOrWhiteSpace(CategorieGrille))
            {
                StatusMessage = "La catégorie salariale est obligatoire.";
                return;
            }

            try
            {
                IsBusy = true;
                var grille = new GrilleSalaire
                {
                    Id = SelectedGrille?.Id ?? 0,
                    ConventionId = SelectedConvention.Id,
                    Categorie = CategorieGrille.Trim(),
                    Echelon = string.IsNullOrWhiteSpace(EchelonGrille) ? "A" : EchelonGrille.Trim(),
                    SalaireMinimum = SalaireMinimumGrille,
                    Description = DescriptionGrille?.Trim()
                };

                await _conventionService.EnregistrerGrilleAsync(grille, "Utilisateur");
                StatusMessage = $"Catégorie '{grille.Categorie}' échelon '{grille.Echelon}' enregistrée ({grille.SalaireMinimum:N0} FCFA).";
                ChargerDetailsConvention();
            }
            catch (Exception ex)
            {
                StatusMessage = "Erreur lors de l'enregistrement de la grille : " + ex.Message;
            }
            finally
            {
                IsBusy = false;
            }
        }

        private async Task SupprimerGrilleAsync()
        {
            if (SelectedGrille == null || SelectedGrille.Id <= 0) return;

            try
            {
                IsBusy = true;
                await _conventionService.SupprimerGrilleAsync(SelectedGrille.Id, "Utilisateur");
                StatusMessage = "Ligne de grille supprimée avec succès.";
                SelectedGrille = null;
                ChargerDetailsConvention();
            }
            catch (Exception ex)
            {
                StatusMessage = "Erreur lors de la suppression de la grille : " + ex.Message;
            }
            finally
            {
                IsBusy = false;
            }
        }

        private async Task EnregistrerTrancheAsync()
        {
            if (SelectedConvention == null || SelectedConvention.Id <= 0)
            {
                StatusMessage = "Veuillez d'abord sélectionner une convention collective.";
                return;
            }

            try
            {
                IsBusy = true;
                var tranche = new TrancheAnciennete
                {
                    Id = SelectedTranche?.Id ?? 0,
                    ConventionId = SelectedConvention.Id,
                    AnneesMin = AnneesMinTranche,
                    AnneesMax = AnneesMaxTranche,
                    TauxPourcentage = TauxTranche
                };

                await _conventionService.EnregistrerTrancheAsync(tranche, "Utilisateur");
                StatusMessage = $"Tranche ancienneté enregistrée ({tranche.LibelleTranche} -> {tranche.TauxPourcentage:0.##}%).";
                ChargerDetailsConvention();
            }
            catch (Exception ex)
            {
                StatusMessage = "Erreur lors de l'enregistrement du barème : " + ex.Message;
            }
            finally
            {
                IsBusy = false;
            }
        }

        private async Task SupprimerTrancheAsync()
        {
            if (SelectedTranche == null || SelectedTranche.Id <= 0) return;

            try
            {
                IsBusy = true;
                await _conventionService.SupprimerTrancheAsync(SelectedTranche.Id, "Utilisateur");
                StatusMessage = "Tranche d'ancienneté supprimée avec succès.";
                SelectedTranche = null;
                ChargerDetailsConvention();
            }
            catch (Exception ex)
            {
                StatusMessage = "Erreur lors de la suppression de la tranche : " + ex.Message;
            }
            finally
            {
                IsBusy = false;
            }
        }

        private void ExecuterSimulation()
        {
            if (SelectedEmployeeForSimulation == null || SelectedConvention == null)
            {
                ResultatSimulation = null;
                return;
            }

            // Calculer sur la convention sélectionnée (ou la convention de l'employé si rattaché)
            var convCible = SelectedConvention;
            ResultatSimulation = _conventionService.CalculerAnciennete(SelectedEmployeeForSimulation, DateSimulation, convCible);
        }
    }
}
