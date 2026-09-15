using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using IUTSCalculator.Helpers;
using IUTSCalculator.Models;
using IUTSCalculator.Services;
using Microsoft.Win32;

namespace IUTSCalculator.ViewModels
{
    /// <summary>
    /// ViewModel pour le module PAI-07 — Rappels & Régularisations.
    /// Gère les 3 volets :
    /// 1. Rappels & corrections ponctuelles (avec assistant de simulation rétroactive)
    /// 2. Régularisation annuelle d'IUTS de fin d'exercice
    /// 3. Registre, historique et exports ClosedXML
    /// </summary>
    public class RappelPaieViewModel : BaseViewModel
    {
        private readonly RappelPaieService _rappelService;
        private readonly RappelPaieExportService _exportService;
        private readonly DataService _dataService;

        private int _selectedTabIndex;
        private bool _isBusy;
        private string _statusMessage;

        // Collections
        private readonly ObservableCollection<RappelPaie> _rappels = new ObservableCollection<RappelPaie>();
        private readonly ObservableCollection<RappelPaie> _rappelsFiltres = new ObservableCollection<RappelPaie>();
        private readonly ObservableCollection<RegularisationIutsAnnuel> _regularisationsIuts = new ObservableCollection<RegularisationIutsAnnuel>();
        private readonly ObservableCollection<LigneRappelRetroactifDetail> _detailsSimulation = new ObservableCollection<LigneRappelRetroactifDetail>();
        private readonly ObservableCollection<Employee> _employes = new ObservableCollection<Employee>();

        // Filtres
        private string _rechercheTexte;
        private int _filtreStatutIndex = 0; // 0=Tous, 1=En attente, 2=Imputé, 3=Annulé
        private int _filtreTypeIndex = 0;

        // Sélections
        private Employee _selectedEmploye;
        private RappelPaie _selectedRappel;
        private RegularisationIutsAnnuel _selectedRegularisation;

        // Formulaire Saisie / Édition
        private bool _isEditionMode;
        private int _formId;
        private Employee _formEmploye;
        private TypeRappelPaie _formTypeRappel = TypeRappelPaie.RappelSalaire;
        private string _formLibelle = "Rappel de salaire rétroactif";
        private SensRappel _formSens = SensRappel.Gain;
        private DateTime _formMoisOrigineDebut = DateTime.Now.AddMonths(-1);
        private DateTime _formMoisOrigineFin = DateTime.Now.AddMonths(-1);
        private int _formAnneeImputation = DateTime.Now.Year;
        private int _formMoisImputation = DateTime.Now.Month;
        private decimal _formMontantBrut;
        private bool _formSoumisIUTS = true;
        private bool _formSoumisCNSS = true;
        private bool _formSoumisTPA = true;
        private string _formMotif;

        // Assistant Simulation Rétroactive
        private Employee _simuEmploye;
        private DateTime _simuMoisDebut = DateTime.Now.AddMonths(-3);
        private DateTime _simuMoisFin = DateTime.Now.AddMonths(-1);
        private decimal _simuAugmentationSalaireBase = 25000m;
        private decimal _simuPrimeRetroactive = 0m;
        private decimal _simuTotalBrut;
        private decimal _simuTotalCnssSalarial;
        private decimal _simuTotalCnssPatronal;
        private decimal _simuTotalTpa;
        private decimal _simuTotalNet;
        private bool _hasSimulationResults;

        // Régularisation Annuelle IUTS
        private int _regulAnnee = DateTime.Now.Year;
        private decimal _totalCumulBrutExercice;
        private decimal _totalIutsTheoriqueExercice;
        private decimal _totalIutsPreleveExercice;
        private decimal _totalDifferenceExercice;

        public RappelPaieViewModel(
            RappelPaieService rappelService,
            RappelPaieExportService exportService,
            DataService dataService)
        {
            _rappelService = rappelService ?? throw new ArgumentNullException(nameof(rappelService));
            _exportService = exportService ?? throw new ArgumentNullException(nameof(exportService));
            _dataService = dataService ?? throw new ArgumentNullException(nameof(dataService));

            Title = "Rappels & régularisations";

            InitialiserCommandes();
            _ = ChargerDonneesAsync();
        }

        #region Propriétés Exposées

        public int SelectedTabIndex
        {
            get => _selectedTabIndex;
            set => SetProperty(ref _selectedTabIndex, value);
        }

        public new bool IsBusy
        {
            get => _isBusy;
            set => SetProperty(ref _isBusy, value);
        }

        public new string StatusMessage
        {
            get => _statusMessage;
            set => SetProperty(ref _statusMessage, value);
        }

        public ObservableCollection<RappelPaie> Rappels => _rappels;
        public ObservableCollection<RappelPaie> RappelsFiltres => _rappelsFiltres;
        public ObservableCollection<RegularisationIutsAnnuel> RegularisationsIuts => _regularisationsIuts;
        public ObservableCollection<LigneRappelRetroactifDetail> DetailsSimulation => _detailsSimulation;
        public ObservableCollection<Employee> Employes => _employes;

        public Employee SelectedEmploye
        {
            get => _selectedEmploye;
            set
            {
                if (SetProperty(ref _selectedEmploye, value))
                {
                    if (value != null)
                    {
                        FormEmploye = value;
                        SimuEmploye = value;
                    }
                    AppliquerFiltre();
                }
            }
        }

        public RappelPaie SelectedRappel
        {
            get => _selectedRappel;
            set
            {
                if (SetProperty(ref _selectedRappel, value) && value != null)
                {
                    ChargerRappelDansFormulaire(value);
                }
            }
        }

        public RegularisationIutsAnnuel SelectedRegularisation
        {
            get => _selectedRegularisation;
            set => SetProperty(ref _selectedRegularisation, value);
        }

        public string RechercheTexte
        {
            get => _rechercheTexte;
            set
            {
                if (SetProperty(ref _rechercheTexte, value))
                    AppliquerFiltre();
            }
        }

        public int FiltreStatutIndex
        {
            get => _filtreStatutIndex;
            set
            {
                if (SetProperty(ref _filtreStatutIndex, value))
                    AppliquerFiltre();
            }
        }

        public int FiltreTypeIndex
        {
            get => _filtreTypeIndex;
            set
            {
                if (SetProperty(ref _filtreTypeIndex, value))
                    AppliquerFiltre();
            }
        }

        #endregion

        #region Propriétés Formulaire

        public bool IsEditionMode
        {
            get => _isEditionMode;
            set => SetProperty(ref _isEditionMode, value);
        }

        public Employee FormEmploye
        {
            get => _formEmploye;
            set => SetProperty(ref _formEmploye, value);
        }

        public TypeRappelPaie FormTypeRappel
        {
            get => _formTypeRappel;
            set
            {
                if (SetProperty(ref _formTypeRappel, value))
                {
                    if (value == TypeRappelPaie.TropPercuSalaire)
                    {
                        FormSens = SensRappel.Retenue;
                        FormSoumisIUTS = false;
                        FormSoumisCNSS = false;
                        FormSoumisTPA = false;
                        FormLibelle = "Trop-perçu sur rémunération antérieure";
                    }
                    else if (value == TypeRappelPaie.RegularisationIuts)
                    {
                        FormSoumisIUTS = false;
                        FormSoumisCNSS = false;
                        FormSoumisTPA = false;
                        FormLibelle = "Régularisation IUTS de fin d'année";
                    }
                    else
                    {
                        FormSens = SensRappel.Gain;
                        FormSoumisIUTS = true;
                        FormSoumisCNSS = true;
                        FormSoumisTPA = true;
                        if (value == TypeRappelPaie.RappelPrime) FormLibelle = "Rappel de prime";
                        else if (value == TypeRappelPaie.OmissionHeuresSup) FormLibelle = "Rattrapage heures supplémentaires";
                        else FormLibelle = "Rappel de salaire rétroactif";
                    }
                }
            }
        }

        public string FormLibelle
        {
            get => _formLibelle;
            set => SetProperty(ref _formLibelle, value);
        }

        public SensRappel FormSens
        {
            get => _formSens;
            set
            {
                if (SetProperty(ref _formSens, value))
                {
                    OnPropertyChanged(nameof(FormIsGain));
                    OnPropertyChanged(nameof(FormIsRetenue));
                }
            }
        }

        public bool FormIsGain
        {
            get => _formSens == SensRappel.Gain;
            set
            {
                if (value) FormSens = SensRappel.Gain;
            }
        }

        public bool FormIsRetenue
        {
            get => _formSens == SensRappel.Retenue;
            set
            {
                if (value) FormSens = SensRappel.Retenue;
            }
        }

        public DateTime FormMoisOrigineDebut
        {
            get => _formMoisOrigineDebut;
            set => SetProperty(ref _formMoisOrigineDebut, value);
        }

        public DateTime FormMoisOrigineFin
        {
            get => _formMoisOrigineFin;
            set => SetProperty(ref _formMoisOrigineFin, value);
        }

        public int FormAnneeImputation
        {
            get => _formAnneeImputation;
            set => SetProperty(ref _formAnneeImputation, value);
        }

        public int FormMoisImputation
        {
            get => _formMoisImputation;
            set => SetProperty(ref _formMoisImputation, value);
        }

        public decimal FormMontantBrut
        {
            get => _formMontantBrut;
            set => SetProperty(ref _formMontantBrut, value);
        }

        public bool FormSoumisIUTS
        {
            get => _formSoumisIUTS;
            set => SetProperty(ref _formSoumisIUTS, value);
        }

        public bool FormSoumisCNSS
        {
            get => _formSoumisCNSS;
            set => SetProperty(ref _formSoumisCNSS, value);
        }

        public bool FormSoumisTPA
        {
            get => _formSoumisTPA;
            set => SetProperty(ref _formSoumisTPA, value);
        }

        public string FormMotif
        {
            get => _formMotif;
            set => SetProperty(ref _formMotif, value);
        }

        #endregion

        #region Propriétés Assistant Simulation

        public Employee SimuEmploye
        {
            get => _simuEmploye;
            set => SetProperty(ref _simuEmploye, value);
        }

        public DateTime SimuMoisDebut
        {
            get => _simuMoisDebut;
            set => SetProperty(ref _simuMoisDebut, value);
        }

        public DateTime SimuMoisFin
        {
            get => _simuMoisFin;
            set => SetProperty(ref _simuMoisFin, value);
        }

        public decimal SimuAugmentationSalaireBase
        {
            get => _simuAugmentationSalaireBase;
            set => SetProperty(ref _simuAugmentationSalaireBase, value);
        }

        public decimal SimuPrimeRetroactive
        {
            get => _simuPrimeRetroactive;
            set => SetProperty(ref _simuPrimeRetroactive, value);
        }

        public decimal SimuTotalBrut
        {
            get => _simuTotalBrut;
            set => SetProperty(ref _simuTotalBrut, value);
        }

        public decimal SimuTotalCnssSalarial
        {
            get => _simuTotalCnssSalarial;
            set => SetProperty(ref _simuTotalCnssSalarial, value);
        }

        public decimal SimuTotalCnssPatronal
        {
            get => _simuTotalCnssPatronal;
            set => SetProperty(ref _simuTotalCnssPatronal, value);
        }

        public decimal SimuTotalTpa
        {
            get => _simuTotalTpa;
            set => SetProperty(ref _simuTotalTpa, value);
        }

        public decimal SimuTotalNet
        {
            get => _simuTotalNet;
            set => SetProperty(ref _simuTotalNet, value);
        }

        public bool HasSimulationResults
        {
            get => _hasSimulationResults;
            set => SetProperty(ref _hasSimulationResults, value);
        }

        #endregion

        #region Propriétés Régularisation Annuelle

        public int RegulAnnee
        {
            get => _regulAnnee;
            set => SetProperty(ref _regulAnnee, value);
        }

        public decimal TotalCumulBrutExercice
        {
            get => _totalCumulBrutExercice;
            set => SetProperty(ref _totalCumulBrutExercice, value);
        }

        public decimal TotalIutsTheoriqueExercice
        {
            get => _totalIutsTheoriqueExercice;
            set => SetProperty(ref _totalIutsTheoriqueExercice, value);
        }

        public decimal TotalIutsPreleveExercice
        {
            get => _totalIutsPreleveExercice;
            set => SetProperty(ref _totalIutsPreleveExercice, value);
        }

        public decimal TotalDifferenceExercice
        {
            get => _totalDifferenceExercice;
            set => SetProperty(ref _totalDifferenceExercice, value);
        }

        #endregion

        #region Commandes

        public ICommand ChargerDonneesCommand { get; private set; }
        public ICommand NouveauRappelCommand { get; private set; }
        public ICommand EnregistrerRappelCommand { get; private set; }
        public ICommand SupprimerRappelCommand { get; private set; }
        public ICommand AnnulerRappelCommand { get; private set; }

        public ICommand CalculerSimulationCommand { get; private set; }
        public ICommand ValiderEtCreerRappelSimulationCommand { get; private set; }

        public ICommand CalculerRegularisationAnnuelleCommand { get; private set; }
        public ICommand GenererRappelsRegulCommand { get; private set; }

        public ICommand ExporterRegistreExcelCommand { get; private set; }
        public ICommand ExporterSimulationExcelCommand { get; private set; }
        public ICommand ExporterRegulIutsExcelCommand { get; private set; }

        private void InitialiserCommandes()
        {
            ChargerDonneesCommand = new RelayCommand(async () => await ChargerDonneesAsync());
            NouveauRappelCommand = new RelayCommand(ReinitialiserFormulaire);
            EnregistrerRappelCommand = new RelayCommand(async () => await EnregistrerRappelAsync(), () => FormEmploye != null && FormMontantBrut > 0m);
            SupprimerRappelCommand = new RelayCommand(async () => await SupprimerRappelAsync(), () => SelectedRappel != null && SelectedRappel.Statut != StatutRappelPaie.Impute);
            AnnulerRappelCommand = new RelayCommand(async () => await AnnulerRappelAsync(), () => SelectedRappel != null && SelectedRappel.Statut == StatutRappelPaie.EnAttente);

            CalculerSimulationCommand = new RelayCommand(CalculerSimulationRetroactive, () => SimuEmploye != null && (SimuAugmentationSalaireBase > 0m || SimuPrimeRetroactive > 0m));
            ValiderEtCreerRappelSimulationCommand = new RelayCommand(async () => await ValiderEtCreerRappelSimulationAsync(), () => HasSimulationResults && SimuTotalBrut > 0m);

            CalculerRegularisationAnnuelleCommand = new RelayCommand(async () => await CalculerRegularisationAnnuelleAsync());
            GenererRappelsRegulCommand = new RelayCommand(async () => await GenererRappelsRegulAsync(), () => RegularisationsIuts.Any(r => r.DifferenceRegularisation != 0m));

            ExporterRegistreExcelCommand = new RelayCommand(async () => await ExporterRegistreExcelAsync(), () => RappelsFiltres.Count > 0);
            ExporterSimulationExcelCommand = new RelayCommand(async () => await ExporterSimulationExcelAsync(), () => DetailsSimulation.Count > 0);
            ExporterRegulIutsExcelCommand = new RelayCommand(async () => await ExporterRegulIutsExcelAsync(), () => RegularisationsIuts.Count > 0);
        }

        #endregion

        #region Logique Métier & Chargement

        public async Task ChargerDonneesAsync()
        {
            try
            {
                IsBusy = true;
                StatusMessage = "Chargement des rappels et employés...";

                var employesList = await _dataService.LoadEmployeesAsync();
                _employes.Clear();
                foreach (var emp in employesList.OrderBy(e => e.LastName).ThenBy(e => e.FirstName))
                {
                    _employes.Add(emp);
                }

                if (_selectedEmploye == null && _employes.Count > 0)
                {
                    SelectedEmploye = _employes[0];
                }

                var rappelsList = await _rappelService.ObtenirRappelsAsync();
                _rappels.Clear();
                foreach (var r in rappelsList)
                {
                    _rappels.Add(r);
                }

                AppliquerFiltre();

                var regulsList = await _rappelService.ObtenirRegularisationsIutsAsync(RegulAnnee);
                _regularisationsIuts.Clear();
                foreach (var reg in regulsList)
                {
                    _regularisationsIuts.Add(reg);
                }
                MettreAJourTotauxRegularisation();

                StatusMessage = $"{_rappels.Count} rappel(s) chargé(s)";
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

        private void AppliquerFiltre()
        {
            _rappelsFiltres.Clear();

            var query = _rappels.AsEnumerable();

            if (!string.IsNullOrWhiteSpace(RechercheTexte))
            {
                string search = RechercheTexte.Trim().ToLowerInvariant();
                query = query.Where(r =>
                    (r.NomSalarie != null && r.NomSalarie.ToLowerInvariant().Contains(search)) ||
                    (r.EmployeeId != null && r.EmployeeId.ToLowerInvariant().Contains(search)) ||
                    (r.Libelle != null && r.Libelle.ToLowerInvariant().Contains(search)) ||
                    (r.Motif != null && r.Motif.ToLowerInvariant().Contains(search)));
            }

            if (FiltreStatutIndex > 0)
            {
                StatutRappelPaie statutCible = (StatutRappelPaie)(FiltreStatutIndex - 1);
                query = query.Where(r => r.Statut == statutCible);
            }

            if (FiltreTypeIndex > 0)
            {
                TypeRappelPaie typeCible = (TypeRappelPaie)(FiltreTypeIndex - 1);
                query = query.Where(r => r.TypeRappel == typeCible);
            }

            foreach (var r in query)
            {
                _rappelsFiltres.Add(r);
            }
        }

        private void ChargerRappelDansFormulaire(RappelPaie r)
        {
            if (r == null) return;

            _formId = r.Id;
            FormEmploye = _employes.FirstOrDefault(e => e.EmployeeId == r.EmployeeId);
            FormTypeRappel = r.TypeRappel;
            FormLibelle = r.Libelle;
            FormSens = r.Sens;
            FormMoisOrigineDebut = r.MoisOrigineDebut ?? DateTime.Now.AddMonths(-1);
            FormMoisOrigineFin = r.MoisOrigineFin ?? DateTime.Now.AddMonths(-1);
            FormAnneeImputation = r.AnneeImputation;
            FormMoisImputation = r.MoisImputation;
            FormMontantBrut = r.MontantBrut;
            FormSoumisIUTS = r.SoumisIUTS;
            FormSoumisCNSS = r.SoumisCNSS;
            FormSoumisTPA = r.SoumisTPA;
            FormMotif = r.Motif;
            IsEditionMode = true;
        }

        private void ReinitialiserFormulaire()
        {
            _formId = 0;
            FormEmploye = SelectedEmploye ?? (_employes.Count > 0 ? _employes[0] : null);
            FormTypeRappel = TypeRappelPaie.RappelSalaire;
            FormLibelle = "Rappel de salaire rétroactif";
            FormSens = SensRappel.Gain;
            FormMoisOrigineDebut = DateTime.Now.AddMonths(-1);
            FormMoisOrigineFin = DateTime.Now.AddMonths(-1);
            FormAnneeImputation = DateTime.Now.Year;
            FormMoisImputation = DateTime.Now.Month;
            FormMontantBrut = 0m;
            FormSoumisIUTS = true;
            FormSoumisCNSS = true;
            FormSoumisTPA = true;
            FormMotif = string.Empty;
            IsEditionMode = false;
            SelectedRappel = null;
        }

        private async Task EnregistrerRappelAsync()
        {
            if (FormEmploye == null)
            {
                StatusMessage = "Veuillez sélectionner un employé.";
                return;
            }
            if (FormMontantBrut <= 0m)
            {
                StatusMessage = "Le montant doit être supérieur à zéro.";
                return;
            }

            try
            {
                IsBusy = true;

                decimal cnssSalarial = 0m;
                decimal cnssPatronal = 0m;
                decimal tpa = 0m;
                decimal net = FormMontantBrut;

                if (FormSens == SensRappel.Gain && FormSoumisCNSS)
                {
                    cnssSalarial = Math.Round(FormMontantBrut * RappelPaieService.TauxCnssSalarialDefaut, MidpointRounding.AwayFromZero);
                    cnssPatronal = Math.Round(FormMontantBrut * RappelPaieService.TauxCnssPatronalDefaut, MidpointRounding.AwayFromZero);
                    net = FormMontantBrut - cnssSalarial;
                }
                if (FormSens == SensRappel.Gain && FormSoumisTPA)
                {
                    tpa = Math.Round(FormMontantBrut * RappelPaieService.TauxTpaDefaut, MidpointRounding.AwayFromZero);
                }

                var rappel = new RappelPaie
                {
                    Id = _formId,
                    EmployeeId = FormEmploye.EmployeeId,
                    NomSalarie = $"{FormEmploye.LastName} {FormEmploye.FirstName}".Trim(),
                    Matricule = FormEmploye.EmployeeId,
                    TypeRappel = FormTypeRappel,
                    Libelle = FormLibelle,
                    Sens = FormSens,
                    MoisOrigineDebut = FormMoisOrigineDebut,
                    MoisOrigineFin = FormMoisOrigineFin,
                    AnneeImputation = FormAnneeImputation,
                    MoisImputation = FormMoisImputation,
                    MontantBrut = FormMontantBrut,
                    MontantCnssSalarial = cnssSalarial,
                    MontantCnssPatronal = cnssPatronal,
                    MontantTPA = tpa,
                    MontantNet = net,
                    SoumisIUTS = FormSoumisIUTS,
                    SoumisCNSS = FormSoumisCNSS,
                    SoumisTPA = FormSoumisTPA,
                    Statut = StatutRappelPaie.EnAttente,
                    Motif = FormMotif,
                    DateCreation = DateTime.Now
                };

                int id = await _rappelService.EnregistrerRappelAsync(rappel);
                rappel.Id = id;

                var existant = _rappels.FirstOrDefault(r => r.Id == id);
                if (existant != null)
                {
                    int idx = _rappels.IndexOf(existant);
                    _rappels[idx] = rappel;
                }
                else
                {
                    _rappels.Insert(0, rappel);
                }

                AppliquerFiltre();
                ReinitialiserFormulaire();
                StatusMessage = $"Rappel n°{id} enregistré avec succès.";
            }
            catch (Exception ex)
            {
                StatusMessage = "Erreur lors de l'enregistrement : " + ex.Message;
            }
            finally
            {
                IsBusy = false;
            }
        }

        private async Task SupprimerRappelAsync()
        {
            if (SelectedRappel == null) return;
            if (SelectedRappel.Statut == StatutRappelPaie.Impute)
            {
                StatusMessage = "Impossible de supprimer un rappel déjà imputé sur un bulletin de paie.";
                return;
            }

            try
            {
                IsBusy = true;
                bool ok = await _rappelService.SupprimerRappelAsync(SelectedRappel.Id);
                if (ok)
                {
                    _rappels.Remove(SelectedRappel);
                    AppliquerFiltre();
                    ReinitialiserFormulaire();
                    StatusMessage = "Rappel supprimé.";
                }
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

        private async Task AnnulerRappelAsync()
        {
            if (SelectedRappel == null) return;
            try
            {
                IsBusy = true;
                bool ok = await _rappelService.MarquerAnnuleAsync(SelectedRappel.Id);
                if (ok)
                {
                    SelectedRappel.Statut = StatutRappelPaie.Annule;
                    AppliquerFiltre();
                    StatusMessage = "Rappel annulé.";
                }
            }
            catch (Exception ex)
            {
                StatusMessage = "Erreur lors de l'annulation : " + ex.Message;
            }
            finally
            {
                IsBusy = false;
            }
        }

        #endregion

        #region Assistant de Simulation Rétroactive

        private void CalculerSimulationRetroactive()
        {
            if (SimuEmploye == null)
            {
                StatusMessage = "Veuillez sélectionner un employé pour la simulation.";
                return;
            }

            try
            {
                var res = _rappelService.CalculerDifferentielRetroactif(
                    SimuEmploye,
                    SimuMoisDebut,
                    SimuMoisFin,
                    SimuAugmentationSalaireBase,
                    SimuPrimeRetroactive);

                _detailsSimulation.Clear();
                foreach (var d in res.Details)
                {
                    _detailsSimulation.Add(d);
                }

                SimuTotalBrut = res.TotalBrut;
                SimuTotalCnssSalarial = res.TotalCnssSalarial;
                SimuTotalCnssPatronal = res.TotalCnssPatronal;
                SimuTotalTpa = res.TotalTpa;
                SimuTotalNet = res.TotalNet;
                HasSimulationResults = true;

                StatusMessage = $"Simulation calculée sur {_detailsSimulation.Count} mois : Total brut = {SimuTotalBrut:N0} FCFA";
            }
            catch (Exception ex)
            {
                StatusMessage = "Erreur simulation : " + ex.Message;
            }
        }

        private async Task ValiderEtCreerRappelSimulationAsync()
        {
            if (SimuEmploye == null || !HasSimulationResults || SimuTotalBrut <= 0m) return;

            try
            {
                IsBusy = true;
                var rappel = new RappelPaie
                {
                    EmployeeId = SimuEmploye.EmployeeId,
                    NomSalarie = $"{SimuEmploye.LastName} {SimuEmploye.FirstName}".Trim(),
                    Matricule = SimuEmploye.EmployeeId,
                    TypeRappel = TypeRappelPaie.RappelSalaire,
                    Libelle = $"Rappel revalorisation de salaire ({SimuMoisDebut:MMM yyyy} - {SimuMoisFin:MMM yyyy})",
                    Sens = SensRappel.Gain,
                    MoisOrigineDebut = SimuMoisDebut,
                    MoisOrigineFin = SimuMoisFin,
                    AnneeImputation = DateTime.Now.Year,
                    MoisImputation = DateTime.Now.Month,
                    MontantBrut = SimuTotalBrut,
                    MontantCnssSalarial = SimuTotalCnssSalarial,
                    MontantCnssPatronal = SimuTotalCnssPatronal,
                    MontantTPA = SimuTotalTpa,
                    MontantNet = SimuTotalNet,
                    SoumisIUTS = true,
                    SoumisCNSS = true,
                    SoumisTPA = true,
                    Statut = StatutRappelPaie.EnAttente,
                    Motif = $"Revalorisation rétroactive de +{SimuAugmentationSalaireBase:N0} FCFA/mois sur {_detailsSimulation.Count} mois.",
                    DateCreation = DateTime.Now
                };

                int id = await _rappelService.EnregistrerRappelAsync(rappel);
                rappel.Id = id;
                _rappels.Insert(0, rappel);
                AppliquerFiltre();

                StatusMessage = $"Rappel rétroactif n°{id} créé et placé en attente d'imputation.";
                SelectedTabIndex = 0; // Revenir au premier onglet
            }
            catch (Exception ex)
            {
                StatusMessage = "Erreur création rappel : " + ex.Message;
            }
            finally
            {
                IsBusy = false;
            }
        }

        #endregion

        #region Régularisation Annuelle d'IUTS

        private async Task CalculerRegularisationAnnuelleAsync()
        {
            try
            {
                IsBusy = true;
                StatusMessage = $"Calcul de la régularisation annuelle d'IUTS pour l'exercice {RegulAnnee}...";

                var employesList = await _dataService.LoadEmployeesAsync();
                var reguls = await _rappelService.CalculerRegularisationAnnuelleTousSalariesAsync(RegulAnnee, employesList);

                _regularisationsIuts.Clear();
                foreach (var r in reguls.OrderBy(r => r.NomSalarie))
                {
                    _regularisationsIuts.Add(r);
                    await _rappelService.EnregistrerRegularisationIutsAsync(r);
                }

                MettreAJourTotauxRegularisation();
                StatusMessage = $"{_regularisationsIuts.Count} régularisation(s) annuelle(s) calculée(s) pour {RegulAnnee}.";
            }
            catch (Exception ex)
            {
                StatusMessage = "Erreur calcul régularisation annuelle : " + ex.Message;
            }
            finally
            {
                IsBusy = false;
            }
        }

        private async Task GenererRappelsRegulAsync()
        {
            var aGenerer = _regularisationsIuts.Where(r => r.DifferenceRegularisation != 0m && r.Statut == StatutRegulIuts.Simule).ToList();
            if (aGenerer.Count == 0)
            {
                StatusMessage = "Toutes les régularisations avec écart ont déjà été validées ou converties.";
                return;
            }

            try
            {
                IsBusy = true;
                int count = 0;
                foreach (var reg in aGenerer)
                {
                    var rappel = await _rappelService.GenererRappelDepuisRegulIutsAsync(reg, RegulAnnee, moisCible: 12);
                    if (rappel != null)
                    {
                        _rappels.Insert(0, rappel);
                        count++;
                    }
                }

                AppliquerFiltre();
                StatusMessage = $"{count} ligne(s) de rappel/régularisation IUTS générée(s) pour la paie de décembre {RegulAnnee}.";
            }
            catch (Exception ex)
            {
                StatusMessage = "Erreur génération des régularisations : " + ex.Message;
            }
            finally
            {
                IsBusy = false;
            }
        }

        private void MettreAJourTotauxRegularisation()
        {
            TotalCumulBrutExercice = _regularisationsIuts.Sum(r => r.CumulBrutImposable);
            TotalIutsTheoriqueExercice = _regularisationsIuts.Sum(r => r.IutsTheoriqueAnnuel);
            TotalIutsPreleveExercice = _regularisationsIuts.Sum(r => r.CumulIutsPreleve);
            TotalDifferenceExercice = _regularisationsIuts.Sum(r => r.DifferenceRegularisation);
        }

        #endregion

        #region Exports Excel

        private async Task ExporterRegistreExcelAsync()
        {
            try
            {
                var sfd = new SaveFileDialog
                {
                    Title = "Exporter le registre des rappels et régularisations",
                    Filter = "Classeur Excel (*.xlsx)|*.xlsx",
                    FileName = $"Registre_Rappels_{DateTime.Now:yyyyMMdd}.xlsx"
                };

                if (sfd.ShowDialog() == true)
                {
                    IsBusy = true;
                    var dossier = DossierSession.Instance.CurrentDossier;
                    await _exportService.ExporterRegistreRappelsExcelAsync(RappelsFiltres, dossier, sfd.FileName);
                    StatusMessage = $"Registre exporté avec succès : {Path.GetFileName(sfd.FileName)}";
                }
            }
            catch (Exception ex)
            {
                StatusMessage = "Erreur export : " + ex.Message;
            }
            finally
            {
                IsBusy = false;
            }
        }

        private async Task ExporterSimulationExcelAsync()
        {
            try
            {
                var sfd = new SaveFileDialog
                {
                    Title = "Exporter la simulation différentielle rétroactive",
                    Filter = "Classeur Excel (*.xlsx)|*.xlsx",
                    FileName = $"Simulation_Retroactive_{SimuEmploye?.LastName ?? "Employe"}_{DateTime.Now:yyyyMMdd}.xlsx"
                };

                if (sfd.ShowDialog() == true)
                {
                    IsBusy = true;
                    var dossier = DossierSession.Instance.CurrentDossier;
                    var rappelFactice = new RappelPaie
                    {
                        NomSalarie = $"{SimuEmploye?.LastName} {SimuEmploye?.FirstName}".Trim(),
                        EmployeeId = SimuEmploye?.EmployeeId ?? "",
                        MoisOrigineDebut = SimuMoisDebut,
                        MoisOrigineFin = SimuMoisFin,
                        AnneeImputation = DateTime.Now.Year,
                        MoisImputation = DateTime.Now.Month,
                        Motif = $"Simulation rétroactive de {SimuMoisDebut:MMM yyyy} à {SimuMoisFin:MMM yyyy}"
                    };

                    await _exportService.ExporterSimulationRetroactiveExcelAsync(rappelFactice, DetailsSimulation, dossier, sfd.FileName);
                    StatusMessage = $"Simulation exportée avec succès : {Path.GetFileName(sfd.FileName)}";
                }
            }
            catch (Exception ex)
            {
                StatusMessage = "Erreur export : " + ex.Message;
            }
            finally
            {
                IsBusy = false;
            }
        }

        private async Task ExporterRegulIutsExcelAsync()
        {
            try
            {
                var sfd = new SaveFileDialog
                {
                    Title = "Exporter l'état de régularisation annuelle d'IUTS",
                    Filter = "Classeur Excel (*.xlsx)|*.xlsx",
                    FileName = $"Regularisation_Annuelle_IUTS_{RegulAnnee}_{DateTime.Now:yyyyMMdd}.xlsx"
                };

                if (sfd.ShowDialog() == true)
                {
                    IsBusy = true;
                    var dossier = DossierSession.Instance.CurrentDossier;
                    await _exportService.ExporterRegularisationAnnuelleIUTSExcelAsync(RegularisationsIuts, dossier, RegulAnnee, sfd.FileName);
                    StatusMessage = $"État de régularisation exporté : {Path.GetFileName(sfd.FileName)}";
                }
            }
            catch (Exception ex)
            {
                StatusMessage = "Erreur export : " + ex.Message;
            }
            finally
            {
                IsBusy = false;
            }
        }

        #endregion
    }
}
