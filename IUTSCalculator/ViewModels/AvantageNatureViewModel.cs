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
    /// ViewModel pour le module PAI-08 — Avantages en nature.
    /// Gère la liste des avantages accordés, la saisie assistée avec barème légal CGI (Art. 106),
    /// le calcul automatique de la valeur nette imposable et les exports ClosedXML.
    /// </summary>
    public class AvantageNatureViewModel : BaseViewModel
    {
        private readonly AvantageNatureService _avantageService;
        private readonly AvantageNatureExportService _exportService;
        private readonly DataService _dataService;


        // Collections
        private readonly ObservableCollection<AvantageNature> _avantages = new ObservableCollection<AvantageNature>();
        private readonly ObservableCollection<AvantageNature> _avantagesFiltres = new ObservableCollection<AvantageNature>();
        private readonly ObservableCollection<Employee> _employes = new ObservableCollection<Employee>();

        // Filtres
        private string _rechercheTexte;
        private int _filtreStatutIndex = 0; // 0=Tous, 1=Actifs en cours, 2=Inactifs/Expirés
        private int _filtreTypeIndex = 0;   // 0=Tous, 1..7=Types spécifiques

        // Sélection
        private AvantageNature _selectedAvantage;

        // Formulaire
        private bool _isEditionMode;
        private int _formId;
        private Employee _formEmploye;
        private TypeAvantageNature _formTypeAvantage = TypeAvantageNature.Logement;
        private string _formLibelle = "Avantage en nature logement";
        private ModeEvaluationAvantage _formModeEvaluation = ModeEvaluationAvantage.ForfaitaireLegal;
        private decimal _formMontantAvantage = 75000m;
        private decimal _formPartSalariale = 0m;
        private DateTime _formDateDebut = new DateTime(DateTime.Now.Year, DateTime.Now.Month, 1);
        private DateTime? _formDateFin = null;
        private bool _formHasDateFin = false;
        private bool _formActif = true;
        private bool _formSoumisIUTS = true;
        private bool _formSoumisCNSS = true;
        private bool _formSoumisTPA = true;
        private string _formCodeGain = "AVNAT_LOG";
        private string _formCodeRetenue = "RET_AVNAT";
        private string _formCommentaire;

        public AvantageNatureViewModel(
            AvantageNatureService avantageService,
            AvantageNatureExportService exportService,
            DataService dataService)
        {
            _avantageService = avantageService ?? throw new ArgumentNullException(nameof(avantageService));
            _exportService = exportService ?? throw new ArgumentNullException(nameof(exportService));
            _dataService = dataService ?? throw new ArgumentNullException(nameof(dataService));

            Title = "Avantages en nature";

            // Commandes
            ChargerDonneesCommand = new RelayCommand(async () => await ChargerDonneesAsync());
            NouvelAvantageCommand = new RelayCommand(PreparerNouveauFormulaire);
            EnregistrerCommand = new RelayCommand(async () => await EnregistrerAsync(), () => PeutEnregistrer);
            SupprimerCommand = new RelayCommand(async () => await SupprimerAsync(), () => SelectedAvantage != null);
            BasculerStatutCommand = new RelayCommand(async () => await BasculerStatutAsync(), () => SelectedAvantage != null);
            CalculerForfaitCgiCommand = new RelayCommand(CalculerForfaitCgi, () => FormEmploye != null);
            ExporterExcelCommand = new RelayCommand(async () => await ExporterExcelAsync(), () => AvantagesFiltres.Count > 0);
            ReinitialiserFiltresCommand = new RelayCommand(ReinitialiserFiltres);
        }

        #region Propriétés exposées


        public ObservableCollection<AvantageNature> Avantages => _avantages;
        public ObservableCollection<AvantageNature> AvantagesFiltres => _avantagesFiltres;
        public ObservableCollection<Employee> Employes => _employes;

        public AvantageNature SelectedAvantage
        {
            get => _selectedAvantage;
            set
            {
                if (SetProperty(ref _selectedAvantage, value) && value != null)
                {
                    ChargerDansFormulaire(value);
                }
            }
        }

        // Filtres
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

        // Totaux
        public decimal TotalBrutFiltre => _avantagesFiltres.Sum(a => a.MontantAvantage);
        public decimal TotalPartSalarialeFiltre => _avantagesFiltres.Sum(a => a.PartSalariale);
        public decimal TotalNetImposableFiltre => _avantagesFiltres.Sum(a => a.ValeurNetteImposable);

        // Formulaire
        public bool IsEditionMode
        {
            get => _isEditionMode;
            set => SetProperty(ref _isEditionMode, value);
        }

        public Employee FormEmploye
        {
            get => _formEmploye;
            set
            {
                if (SetProperty(ref _formEmploye, value))
                {
                    OnPropertyChanged(nameof(PeutEnregistrer));
                    // Si mode forfaitaire et logement sélectionné, recalcule automatiquement selon le salaire
                    if (FormTypeAvantage == TypeAvantageNature.Logement && FormModeEvaluation == ModeEvaluationAvantage.ForfaitaireLegal)
                    {
                        CalculerForfaitCgi();
                    }
                }
            }
        }

        public TypeAvantageNature FormTypeAvantage
        {
            get => _formTypeAvantage;
            set
            {
                if (SetProperty(ref _formTypeAvantage, value))
                {
                    MettreAJourDefautsParType(value);
                }
            }
        }

        public string FormLibelle
        {
            get => _formLibelle;
            set
            {
                if (SetProperty(ref _formLibelle, value))
                    OnPropertyChanged(nameof(PeutEnregistrer));
            }
        }

        public ModeEvaluationAvantage FormModeEvaluation
        {
            get => _formModeEvaluation;
            set
            {
                if (SetProperty(ref _formModeEvaluation, value))
                {
                    if (value == ModeEvaluationAvantage.ForfaitaireLegal && FormTypeAvantage == TypeAvantageNature.Logement)
                        CalculerForfaitCgi();
                }
            }
        }

        public decimal FormMontantAvantage
        {
            get => _formMontantAvantage;
            set
            {
                if (SetProperty(ref _formMontantAvantage, value))
                {
                    OnPropertyChanged(nameof(FormValeurNetteImposable));
                    OnPropertyChanged(nameof(PeutEnregistrer));
                }
            }
        }

        public decimal FormPartSalariale
        {
            get => _formPartSalariale;
            set
            {
                if (SetProperty(ref _formPartSalariale, value))
                    OnPropertyChanged(nameof(FormValeurNetteImposable));
            }
        }

        public decimal FormValeurNetteImposable => Math.Max(0m, FormMontantAvantage - FormPartSalariale);

        public DateTime FormDateDebut
        {
            get => _formDateDebut;
            set => SetProperty(ref _formDateDebut, value);
        }

        public DateTime? FormDateFin
        {
            get => _formDateFin;
            set => SetProperty(ref _formDateFin, value);
        }

        public bool FormHasDateFin
        {
            get => _formHasDateFin;
            set
            {
                if (SetProperty(ref _formHasDateFin, value))
                {
                    if (!value) FormDateFin = null;
                    else if (!FormDateFin.HasValue) FormDateFin = FormDateDebut.AddYears(1);
                }
            }
        }

        public bool FormActif
        {
            get => _formActif;
            set => SetProperty(ref _formActif, value);
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

        public string FormCodeGain
        {
            get => _formCodeGain;
            set => SetProperty(ref _formCodeGain, value);
        }

        public string FormCodeRetenue
        {
            get => _formCodeRetenue;
            set => SetProperty(ref _formCodeRetenue, value);
        }

        public string FormCommentaire
        {
            get => _formCommentaire;
            set => SetProperty(ref _formCommentaire, value);
        }

        public bool PeutEnregistrer => FormEmploye != null &&
                                       !string.IsNullOrWhiteSpace(FormLibelle) &&
                                       FormMontantAvantage > 0m;

        #endregion

        #region Commandes

        public ICommand ChargerDonneesCommand { get; }
        public ICommand NouvelAvantageCommand { get; }
        public ICommand EnregistrerCommand { get; }
        public ICommand SupprimerCommand { get; }
        public ICommand BasculerStatutCommand { get; }
        public ICommand CalculerForfaitCgiCommand { get; }
        public ICommand ExporterExcelCommand { get; }
        public ICommand ReinitialiserFiltresCommand { get; }

        #endregion

        #region Actions métier

        public async Task ChargerDonneesAsync()
        {
            IsBusy = true;
            StatusMessage = "Chargement des avantages en nature...";

            try
            {
                var employes = await _dataService.LoadEmployeesAsync().ConfigureAwait(true);
                _employes.Clear();
                foreach (var emp in employes.OrderBy(e => e.LastName).ThenBy(e => e.FirstName))
                {
                    _employes.Add(emp);
                }

                var liste = await _avantageService.ObtenirAvantagesAsync().ConfigureAwait(true);
                _avantages.Clear();
                foreach (var av in liste)
                {
                    _avantages.Add(av);
                }

                AppliquerFiltre();

                if (FormEmploye == null && _employes.Count > 0)
                {
                    FormEmploye = _employes[0];
                }

                StatusMessage = $"{_avantages.Count} avantage(s) en nature chargé(s).";
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

        public void PreparerNouveauFormulaire()
        {
            IsEditionMode = false;
            _formId = 0;
            SelectedAvantage = null;

            FormTypeAvantage = TypeAvantageNature.Logement;
            FormModeEvaluation = ModeEvaluationAvantage.ForfaitaireLegal;
            FormLibelle = "Avantage en nature logement";
            FormPartSalariale = 0m;
            FormDateDebut = new DateTime(DateTime.Now.Year, DateTime.Now.Month, 1);
            FormDateFin = null;
            FormHasDateFin = false;
            FormActif = true;
            FormSoumisIUTS = true;
            FormSoumisCNSS = true;
            FormSoumisTPA = true;
            FormCommentaire = string.Empty;

            if (FormEmploye != null)
                CalculerForfaitCgi();
            else
                FormMontantAvantage = 75000m;
        }

        private void ChargerDansFormulaire(AvantageNature av)
        {
            if (av == null) return;

            IsEditionMode = true;
            _formId = av.Id;
            FormEmploye = _employes.FirstOrDefault(e => string.Equals(e.EmployeeId, av.EmployeeId, StringComparison.OrdinalIgnoreCase));
            FormTypeAvantage = av.TypeAvantage;
            FormModeEvaluation = av.ModeEvaluation;
            FormLibelle = av.Libelle;
            FormMontantAvantage = av.MontantAvantage;
            FormPartSalariale = av.PartSalariale;
            FormDateDebut = av.DateDebut;
            FormDateFin = av.DateFin;
            FormHasDateFin = av.DateFin.HasValue;
            FormActif = av.Actif;
            FormSoumisIUTS = av.SoumisIUTS;
            FormSoumisCNSS = av.SoumisCNSS;
            FormSoumisTPA = av.SoumisTPA;
            FormCodeGain = av.CodeRubriqueGain;
            FormCodeRetenue = av.CodeRubriqueRetenue;
            FormCommentaire = av.Commentaire;
        }

        private void MettreAJourDefautsParType(TypeAvantageNature type)
        {
            switch (type)
            {
                case TypeAvantageNature.Logement:
                    FormCodeGain = "AVNAT_LOG";
                    FormLibelle = "Avantage en nature logement";
                    FormModeEvaluation = ModeEvaluationAvantage.ForfaitaireLegal;
                    CalculerForfaitCgi();
                    break;
                case TypeAvantageNature.Vehicule:
                    FormCodeGain = "AVNAT_VEH";
                    FormLibelle = "Avantage en nature véhicule";
                    FormModeEvaluation = ModeEvaluationAvantage.ForfaitaireLegal;
                    FormMontantAvantage = 50000m;
                    break;
                case TypeAvantageNature.Electricite:
                    FormCodeGain = "AVNAT_ENG";
                    FormLibelle = "Prise en charge électricité (SONABEL)";
                    FormModeEvaluation = ModeEvaluationAvantage.ValeurReelle;
                    FormMontantAvantage = 25000m;
                    break;
                case TypeAvantageNature.Eau:
                    FormCodeGain = "AVNAT_ENG";
                    FormLibelle = "Prise en charge eau (ONEA)";
                    FormModeEvaluation = ModeEvaluationAvantage.ValeurReelle;
                    FormMontantAvantage = 10000m;
                    break;
                case TypeAvantageNature.Domesticite:
                    FormCodeGain = "AVNAT_DOM";
                    FormLibelle = "Gens de maison (gardien / cuisinier)";
                    FormModeEvaluation = ModeEvaluationAvantage.ValeurReelle;
                    FormMontantAvantage = 45000m;
                    break;
                case TypeAvantageNature.Nourriture:
                    FormCodeGain = "AVNAT_DIV";
                    FormLibelle = "Nourriture / Cantine d'entreprise";
                    FormModeEvaluation = ModeEvaluationAvantage.ForfaitaireLegal;
                    FormMontantAvantage = 30000m;
                    break;
                default:
                    FormCodeGain = "AVNAT_DIV";
                    FormLibelle = "Autre prestation en nature";
                    FormModeEvaluation = ModeEvaluationAvantage.ValeurReelle;
                    break;
            }

            FormCodeRetenue = "RET_AVNAT";
        }

        public void CalculerForfaitCgi()
        {
            if (FormEmploye == null) return;

            if (FormTypeAvantage == TypeAvantageNature.Logement)
            {
                FormMontantAvantage = AvantageNatureService.CalculerForfaitLogementLegal(FormEmploye.BaseSalary);
                StatusMessage = string.Format(
                    "Forfait logement calculé selon Art. 106 CGI : 20% du salaire ({0:N0}) = {1:N0} FCFA (plafonné à {2:N0} FCFA).",
                    FormEmploye.BaseSalary, FormMontantAvantage, AvantageNatureService.PlafondLogementMensuelDefaut);
            }
        }

        public async Task EnregistrerAsync()
        {
            if (!PeutEnregistrer) return;

            IsBusy = true;
            StatusMessage = "Enregistrement de l'avantage en nature...";

            try
            {
                var av = new AvantageNature
                {
                    Id = _formId,
                    EmployeeId = FormEmploye.EmployeeId,
                    TypeAvantage = FormTypeAvantage,
                    Libelle = FormLibelle.Trim(),
                    ModeEvaluation = FormModeEvaluation,
                    MontantAvantage = FormMontantAvantage,
                    PartSalariale = FormPartSalariale,
                    DateDebut = FormDateDebut,
                    DateFin = FormHasDateFin ? FormDateFin : null,
                    Actif = FormActif,
                    SoumisIUTS = FormSoumisIUTS,
                    SoumisCNSS = FormSoumisCNSS,
                    SoumisTPA = FormSoumisTPA,
                    CodeRubriqueGain = FormCodeGain,
                    CodeRubriqueRetenue = FormCodeRetenue,
                    Commentaire = FormCommentaire?.Trim()
                };

                int id = await _avantageService.EnregistrerAsync(av).ConfigureAwait(true);
                av.Id = id;

                await ChargerDonneesAsync();
                SelectedAvantage = _avantages.FirstOrDefault(a => a.Id == id);
                StatusMessage = "Avantage en nature enregistré avec succès.";
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

        public async Task SupprimerAsync()
        {
            if (SelectedAvantage == null) return;

            var r = System.Windows.MessageBox.Show(
                $"Supprimer définitivement l'avantage en nature « {SelectedAvantage.Libelle} » pour {SelectedAvantage.NomSalarie} ?",
                "Confirmation de suppression",
                System.Windows.MessageBoxButton.YesNo,
                System.Windows.MessageBoxImage.Warning);

            if (r != System.Windows.MessageBoxResult.Yes) return;

            IsBusy = true;
            try
            {
                await _avantageService.SupprimerAsync(SelectedAvantage.Id).ConfigureAwait(true);
                await ChargerDonneesAsync();
                PreparerNouveauFormulaire();
                StatusMessage = "Avantage en nature supprimé.";
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

        public async Task BasculerStatutAsync()
        {
            if (SelectedAvantage == null) return;

            IsBusy = true;
            try
            {
                bool nouvelEtat = !SelectedAvantage.Actif;
                await _avantageService.BasculerStatutActifAsync(SelectedAvantage.Id, nouvelEtat).ConfigureAwait(true);
                await ChargerDonneesAsync();
                StatusMessage = nouvelEtat ? "Avantage réactivé." : "Avantage désactivé.";
            }
            catch (Exception ex)
            {
                StatusMessage = "Erreur lors du changement d'état : " + ex.Message;
            }
            finally
            {
                IsBusy = false;
            }
        }

        public async Task ExporterExcelAsync()
        {
            if (AvantagesFiltres.Count == 0) return;

            var sfd = new SaveFileDialog
            {
                Filter = "Classeur Excel (*.xlsx)|*.xlsx",
                FileName = $"Registre_Avantages_En_Nature_{DateTime.Now:yyyyMMdd_HHmm}.xlsx",
                Title = "Exporter le registre des avantages en nature"
            };

            if (sfd.ShowDialog() != true) return;

            IsBusy = true;
            StatusMessage = "Génération du registre Excel...";

            try
            {
                var dossier = DossierSession.Instance.CurrentDossier;
                await _exportService.ExporterRegistreAvantagesExcelAsync(AvantagesFiltres, dossier, sfd.FileName).ConfigureAwait(true);
                StatusMessage = "Registre des avantages exporté avec succès : " + Path.GetFileName(sfd.FileName);
            }
            catch (Exception ex)
            {
                StatusMessage = "Erreur lors de l'export Excel : " + ex.Message;
            }
            finally
            {
                IsBusy = false;
            }
        }

        public void ReinitialiserFiltres()
        {
            RechercheTexte = string.Empty;
            FiltreStatutIndex = 0;
            FiltreTypeIndex = 0;
        }

        private void AppliquerFiltre()
        {
            var source = _avantages.AsEnumerable();

            // 1. Filtre Statut
            if (FiltreStatutIndex == 1) // Actifs en cours
                source = source.Where(a => a.Actif && (!a.DateFin.HasValue || a.DateFin.Value.Date >= DateTime.Today));
            else if (FiltreStatutIndex == 2) // Inactifs / Expirés
                source = source.Where(a => !a.Actif || (a.DateFin.HasValue && a.DateFin.Value.Date < DateTime.Today));

            // 2. Filtre Type
            if (FiltreTypeIndex > 0)
            {
                var typeAttendu = (TypeAvantageNature)(FiltreTypeIndex - 1);
                source = source.Where(a => a.TypeAvantage == typeAttendu);
            }

            // 3. Filtre texte
            if (!string.IsNullOrWhiteSpace(RechercheTexte))
            {
                string query = RechercheTexte.Trim();
                source = source.Where(a =>
                    (a.NomSalarie != null && a.NomSalarie.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0) ||
                    (a.EmployeeId != null && a.EmployeeId.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0) ||
                    (a.Libelle != null && a.Libelle.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0) ||
                    (a.Commentaire != null && a.Commentaire.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0));
            }

            _avantagesFiltres.Clear();
            foreach (var av in source)
            {
                _avantagesFiltres.Add(av);
            }

            OnPropertyChanged(nameof(TotalBrutFiltre));
            OnPropertyChanged(nameof(TotalPartSalarialeFiltre));
            OnPropertyChanged(nameof(TotalNetImposableFiltre));
        }

        #endregion
    }
}
