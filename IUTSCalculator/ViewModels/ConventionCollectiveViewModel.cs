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
        private MoisOption _moisSimulation = MoisOption.Tous.FirstOrDefault(m => m.Numero == DateTime.Today.Month);
        private int _anneeSimulation = DateTime.Today.Year;
        private CalculAncienneteResultat _resultatSimulation;

        // Convention sélectionnée, chargée AVEC sa grille et ses tranches : la liste des
        // conventions n'en contient que l'en-tête, et le simulateur calculait sur une
        // convention sans grille ni barème (minimum et prime toujours nuls).
        private ConventionCollective _conventionComplete;

        // Situation simulée : reprise de la fiche du salarié, modifiable.
        private DateTime? _dateEntreeSimulation;
        private string _categorieSimulation;
        private string _echelonSimulation;
        private decimal _salaireBaseSimulation;

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
                    ReprendreFicheSalarie();
                }
            }
        }

        #region Simulateur & conformité légale

        public IEnumerable<MoisOption> MoisDisponibles => MoisOption.Tous;
        public IEnumerable<int> AnneesDisponibles => Enumerable.Range(DateTime.Today.Year - 5, 7).ToList();

        /// <summary>Mois de paie simulé.</summary>
        public MoisOption MoisSimulation
        {
            get => _moisSimulation;
            set { if (SetProperty(ref _moisSimulation, value)) ExecuterSimulation(); }
        }

        public int AnneeSimulation
        {
            get => _anneeSimulation;
            set { if (SetProperty(ref _anneeSimulation, value)) ExecuterSimulation(); }
        }

        /// <summary>
        /// Premier jour du mois de paie simulé. L'ancienneté y est arrêtée selon la règle
        /// unique de l'application (<see cref="ConventionCollectiveService.DateCalculAnciennete"/>).
        /// </summary>
        public DateTime DateSimulation => new DateTime(_anneeSimulation, (_moisSimulation ?? MoisOption.Tous.First()).Numero, 1);

        public DateTime DateCalculAnciennete => ConventionCollectiveService.DateCalculAnciennete(DateSimulation);

        public string DateCalculLibelle => string.Format(CultureFr,
            "Ancienneté arrêtée au {0:dd/MM/yyyy} (1er du mois de paie, comme sur les bulletins).", DateCalculAnciennete);

        public DateTime? DateEntreeSimulation
        {
            get => _dateEntreeSimulation;
            set { if (SetProperty(ref _dateEntreeSimulation, value)) ExecuterSimulation(); }
        }

        public string CategorieSimulation
        {
            get => _categorieSimulation;
            set { if (SetProperty(ref _categorieSimulation, value)) ExecuterSimulation(); }
        }

        public string EchelonSimulation
        {
            get => _echelonSimulation;
            set { if (SetProperty(ref _echelonSimulation, value)) ExecuterSimulation(); }
        }

        /// <summary>Salaire de base contractuel simulé (repris de la fiche, modifiable).</summary>
        public decimal SalaireBaseSimulation
        {
            get => _salaireBaseSimulation;
            set { if (SetProperty(ref _salaireBaseSimulation, value)) ExecuterSimulation(); }
        }

        public CalculAncienneteResultat ResultatSimulation
        {
            get => _resultatSimulation;
            set
            {
                if (SetProperty(ref _resultatSimulation, value))
                    NotifierResultatSimulation();
            }
        }

        /// <summary>Vrai quand l'ancienneté peut être calculée (date d'entrée renseignée et antérieure au mois de paie).</summary>
        public bool SimulationPossible => _resultatSimulation != null && !ReferenceEquals(_resultatSimulation, CalculAncienneteResultat.Vide);

        public string AncienneteLibelle
        {
            get
            {
                if (_selectedEmployeeForSimulation == null || SelectedConvention == null) return "—";
                if (!_dateEntreeSimulation.HasValue) return "Date d'entrée non renseignée";
                if (_dateEntreeSimulation.Value.Date > DateCalculAnciennete) return "Entrée postérieure au mois de paie";
                return _resultatSimulation == null ? "—" : _resultatSimulation.DescriptionAnciennete;
            }
        }

        public string MinimumGrilleLibelle
        {
            get
            {
                if (!SimulationPossible) return "—";
                return _resultatSimulation.SalaireMinimumConventionnel > 0m
                    ? string.Format(CultureFr, "{0:N0} FCFA", _resultatSimulation.SalaireMinimumConventionnel)
                    : "Aucune ligne de la grille";
            }
        }

        public string BaseCalculLibelle
        {
            get
            {
                if (!SimulationPossible) return "—";
                return string.Format(CultureFr, "{0:N0} FCFA — {1}", _resultatSimulation.BaseCalcul, _resultatSimulation.BaseUtilisee);
            }
        }

        public string TauxLibelle
        {
            get
            {
                if (!SimulationPossible) return "—";
                return _resultatSimulation.TauxPourcentage > 0m
                    ? string.Format(CultureFr, "{0:0.##} % ({1})", _resultatSimulation.TauxPourcentage, _resultatSimulation.LibelleTranche)
                    : "0 % (aucune tranche du barème atteinte)";
            }
        }

        public string PrimeLibelle => SimulationPossible
            ? string.Format(CultureFr, "{0:N0} FCFA", _resultatSimulation.Montant)
            : "—";

        /// <summary>Salaire saisi au moins égal au minimum de la grille.</summary>
        public bool EstConforme => SimulationPossible && _resultatSimulation.SalaireMinimumConventionnel > 0m && !_resultatSimulation.EstSousLeMinimum;

        /// <summary>Salaire saisi inférieur au minimum de la grille.</summary>
        public bool EstNonConforme => SimulationPossible && _resultatSimulation.EstSousLeMinimum;

        /// <summary>Conformité non vérifiable (pas de date d'entrée, ou catégorie / échelon absents de la grille).</summary>
        public bool EstConformiteIndeterminee => _selectedEmployeeForSimulation != null && SelectedConvention != null && !EstConforme && !EstNonConforme;

        public string MessageConformite
        {
            get
            {
                if (_selectedEmployeeForSimulation == null || SelectedConvention == null) return "";
                if (!SimulationPossible)
                    return _dateEntreeSimulation.HasValue
                        ? "Le salarié n'est pas encore entré au 1er du mois de paie choisi : la conformité n'est pas évaluée pour ce mois."
                        : "Renseignez la date d'entrée pour simuler l'ancienneté et contrôler le salaire minimum.";
                var r = _resultatSimulation;
                if (EstNonConforme)
                    return string.Format(CultureFr,
                        "Salaire de base saisi : {0:N0} FCFA, inférieur au minimum garanti de la grille ({1:N0} FCFA) : écart de {2:N0} FCFA à combler.",
                        r.SalaireContractuel, r.SalaireMinimumConventionnel, r.EcartMinimum);
                if (EstConforme)
                    return string.Format(CultureFr,
                        "Salaire de base saisi : {0:N0} FCFA, au moins égal au minimum garanti de la grille ({1:N0} FCFA).",
                        r.SalaireContractuel, r.SalaireMinimumConventionnel);
                return string.Format(CultureFr,
                    "Conformité non vérifiable : aucune ligne de la grille de « {0} » ne correspond à la catégorie « {1} »{2}.",
                    SelectedConvention.Libelle,
                    string.IsNullOrWhiteSpace(_categorieSimulation) ? "(non renseignée)" : _categorieSimulation.Trim(),
                    string.IsNullOrWhiteSpace(_echelonSimulation) ? "" : ", échelon « " + _echelonSimulation.Trim() + " »");
            }
        }

        /// <summary>Ce que la paie appliquera réellement à ce salarié (convention de sa fiche).</summary>
        public string NoteApplicationPaie
        {
            get
            {
                var e = _selectedEmployeeForSimulation;
                var conv = SelectedConvention;
                if (e == null || conv == null) return "";
                if (!e.ConventionId.HasValue || e.ConventionId.Value <= 0)
                    return "Simulation seulement : la fiche de ce salarié n'est rattachée à aucune convention, ses bulletins ne portent pas de prime d'ancienneté conventionnelle.";
                if (e.ConventionId.Value != conv.Id)
                {
                    var autre = Conventions.FirstOrDefault(c => c.Id == e.ConventionId.Value);
                    return "Simulation sur « " + conv.Libelle + " » : la fiche de ce salarié est rattachée à « " +
                           (autre != null ? autre.Libelle : "une autre convention") + " », que ses bulletins appliquent.";
                }
                if (!conv.EstActive)
                    return "Convention inactive : ses bulletins ne portent pas de prime d'ancienneté conventionnelle tant qu'elle n'est pas réactivée.";
                return "Convention de la fiche du salarié : la prime calculée ici est celle de ses bulletins (rubrique PRIMEANC).";
            }
        }

        private static readonly System.Globalization.CultureInfo CultureFr = System.Globalization.CultureInfo.GetCultureInfo("fr-FR");

        private void NotifierResultatSimulation()
        {
            OnPropertyChanged(nameof(DateSimulation));
            OnPropertyChanged(nameof(DateCalculAnciennete));
            OnPropertyChanged(nameof(DateCalculLibelle));
            OnPropertyChanged(nameof(SimulationPossible));
            OnPropertyChanged(nameof(AncienneteLibelle));
            OnPropertyChanged(nameof(MinimumGrilleLibelle));
            OnPropertyChanged(nameof(BaseCalculLibelle));
            OnPropertyChanged(nameof(TauxLibelle));
            OnPropertyChanged(nameof(PrimeLibelle));
            OnPropertyChanged(nameof(EstConforme));
            OnPropertyChanged(nameof(EstNonConforme));
            OnPropertyChanged(nameof(EstConformiteIndeterminee));
            OnPropertyChanged(nameof(MessageConformite));
            OnPropertyChanged(nameof(NoteApplicationPaie));
        }

        #endregion

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
        public ICommand ReprendreFicheCommand { get; }

        public ConventionCollectiveViewModel(
            ConventionCollectiveService conventionService,
            DataService dataService)
        {
            _conventionService = conventionService ?? throw new ArgumentNullException(nameof(conventionService));
            _dataService = dataService ?? throw new ArgumentNullException(nameof(dataService));

            Title = "Conventions collectives et grilles salariales";

            SelectedBaseCalculOption = OptionsBaseCalcul[0];

            ChargerCommand = new RelayCommand(async () => await ChargerToutAsync());
            NouvelleConventionCommand = new RelayCommand(NouvelleConvention);
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
            ReprendreFicheCommand = new RelayCommand(ReprendreFicheSalarie, () => SelectedEmployeeForSimulation != null);
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

                var matriculeSimule = SelectedEmployeeForSimulation?.EmployeeId;
                var listeSalaries = await _dataService.LoadEmployeesAsync();
                Employees.Clear();
                foreach (var emp in listeSalaries)
                {
                    Employees.Add(emp);
                }

                // Le salarié simulé est conservé (fiche relue) après un rechargement.
                var memeSalarie = matriculeSimule == null ? null
                    : Employees.FirstOrDefault(e => string.Equals(e.EmployeeId, matriculeSimule, StringComparison.OrdinalIgnoreCase));
                if (memeSalarie != null)
                    SelectedEmployeeForSimulation = memeSalarie;
                else if (Employees.Count > 0)
                    SelectedEmployeeForSimulation = Employees[0];
                else
                    SelectedEmployeeForSimulation = null;

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
                _conventionComplete = null;
                ExecuterSimulation();
                return;
            }

            CodeConvention = SelectedConvention.Code;
            LibelleConvention = SelectedConvention.Libelle;
            DescriptionConvention = SelectedConvention.Description;
            SelectedBaseCalculOption = OptionsBaseCalcul.FirstOrDefault(o => o.Valeur == SelectedConvention.BaseCalculAnciennete)
                ?? OptionsBaseCalcul[0];
            EstActiveConvention = SelectedConvention.EstActive;

            _conventionComplete = null;
            ExecuterSimulation();

            int idConvention = SelectedConvention.Id;
            _ = Task.Run(async () =>
            {
                var convComplete = await _conventionService.ObtenirParIdAsync(idConvention);
                if (convComplete != null)
                {
                    App.Current.Dispatcher.Invoke(() =>
                    {
                        // Une autre convention a pu être sélectionnée pendant la lecture.
                        if (SelectedConvention == null || SelectedConvention.Id != idConvention) return;
                        _conventionComplete = convComplete;

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

        /// <summary>
        /// « Nouvelle convention » : désélectionne la convention affichée. Sans cela,
        /// « Enregistrer convention » écrasait la convention sélectionnée au lieu d'en créer une.
        /// </summary>
        private void NouvelleConvention()
        {
            SelectedConvention = null;
            ReinitialiserChampsConvention();
            StatusMessage = "Nouvelle convention : saisissez le code et la désignation, puis « Enregistrer convention ».";
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

                // L'enregistrement d'une convention réécrit sa grille et son barème avec ceux
                // qui lui sont transmis : ils sont relus en base, sinon modifier l'en-tête
                // effaçait toute la grille des salaires minima et les tranches d'ancienneté.
                if (conv.Id > 0)
                {
                    var enBase = await _conventionService.ObtenirParIdAsync(conv.Id);
                    if (enBase != null)
                    {
                        conv.Grilles = enBase.Grilles;
                        conv.TranchesAnciennete = enBase.TranchesAnciennete;
                    }
                }

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

        /// <summary>Recopie dans le simulateur la situation enregistrée sur la fiche du salarié choisi.</summary>
        private void ReprendreFicheSalarie()
        {
            var e = SelectedEmployeeForSimulation;
            _dateEntreeSimulation = e?.DateEntreeNullable;
            _categorieSimulation = e?.Category;
            _echelonSimulation = e?.Echelon;
            _salaireBaseSimulation = e?.BaseSalary ?? 0m;
            OnPropertyChanged(nameof(DateEntreeSimulation));
            OnPropertyChanged(nameof(CategorieSimulation));
            OnPropertyChanged(nameof(EchelonSimulation));
            OnPropertyChanged(nameof(SalaireBaseSimulation));
            ExecuterSimulation();
        }

        /// <summary>
        /// Calcule l'ancienneté, le minimum de la grille, la base, le taux et la prime avec
        /// la méthode de la paie (<see cref="ConventionCollectiveService.CalculerAnciennete"/>),
        /// sur la convention sélectionnée et la situation simulée, au 1er du mois de paie.
        /// </summary>
        private void ExecuterSimulation()
        {
            if (SelectedEmployeeForSimulation == null || SelectedConvention == null)
            {
                ResultatSimulation = null;
                NotifierResultatSimulation();
                return;
            }

            // Grille et barème de la convention sélectionnée (en-tête seul tant qu'ils ne sont pas lus).
            var convCible = _conventionComplete != null && _conventionComplete.Id == SelectedConvention.Id
                ? _conventionComplete
                : SelectedConvention;

            // La fiche n'est pas modifiée : le calcul porte sur une copie.
            var simule = SelectedEmployeeForSimulation.Clone();
            simule.DateEntree = _dateEntreeSimulation.HasValue ? _dateEntreeSimulation.Value.Date : default(DateTime);
            simule.Category = _categorieSimulation;
            simule.Echelon = _echelonSimulation;
            simule.BaseSalary = _salaireBaseSimulation;

            ResultatSimulation = _conventionService.CalculerAnciennete(simule, DateCalculAnciennete, convCible);
            NotifierResultatSimulation();
        }
    }
}
