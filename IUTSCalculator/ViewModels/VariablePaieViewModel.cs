using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Data;
using System.Windows.Input;
using IUTSCalculator.Helpers;
using IUTSCalculator.Models;
using IUTSCalculator.Services;

namespace IUTSCalculator.ViewModels
{
    /// <summary>
    /// Écran « Variables du mois » (module PAI-02).
    ///
    /// On choisit une période, on saisit ce qui varie — heures supplémentaires, primes,
    /// absences, retenues — et l'écran totalise par salarié. Deux gestes rendent le mois
    /// de paie supportable : le REPORT de la période précédente, parce que l'essentiel se
    /// répète, et l'AJOUT EN MASSE d'une rubrique à plusieurs salariés.
    /// </summary>
    public class VariablePaieViewModel : BaseViewModel
    {
        private readonly VariablePaieService _variableService;
        private readonly RubriqueService _rubriqueService;
        private readonly DataService _dataService;

        private List<Rubrique> _planDePaie = new List<Rubrique>();

        public ObservableCollection<VariablePaie> Variables { get; } =
            new ObservableCollection<VariablePaie>();

        public ObservableCollection<TotalVariablesSalarie> Totaux { get; } =
            new ObservableCollection<TotalVariablesSalarie>();

        public ObservableCollection<string> Anomalies { get; } =
            new ObservableCollection<string>();

        public ObservableCollection<Employee> Salaries { get; } =
            new ObservableCollection<Employee>();

        public ObservableCollection<Rubrique> RubriquesSaisissables { get; } =
            new ObservableCollection<Rubrique>();

        public ICollectionView VariablesView { get; }
        public ICollectionView TotauxView { get; }

        private string _searchText;
        public string SearchText
        {
            get { return _searchText; }
            set
            {
                if (SetProperty(ref _searchText, value))
                {
                    VariablesView?.Refresh();
                    TotauxView?.Refresh();
                    OnPropertyChanged(nameof(HasSearchText));
                    OnPropertyChanged(nameof(HasActiveFilters));
                    OnPropertyChanged(nameof(NombreLignesAffichees));
                }
            }
        }

        public bool HasSearchText => !string.IsNullOrWhiteSpace(SearchText);

        private SensRubrique? _selectedSensFilter;
        public SensRubrique? SelectedSensFilter
        {
            get { return _selectedSensFilter; }
            set
            {
                if (SetProperty(ref _selectedSensFilter, value))
                {
                    VariablesView?.Refresh();
                    OnPropertyChanged(nameof(HasActiveFilters));
                    OnPropertyChanged(nameof(NombreLignesAffichees));
                }
            }
        }

        private Rubrique _selectedRubriqueFilter;
        public Rubrique SelectedRubriqueFilter
        {
            get { return _selectedRubriqueFilter; }
            set
            {
                if (SetProperty(ref _selectedRubriqueFilter, value))
                {
                    VariablesView?.Refresh();
                    OnPropertyChanged(nameof(HasActiveFilters));
                    OnPropertyChanged(nameof(NombreLignesAffichees));
                }
            }
        }

        public bool HasActiveFilters => HasSearchText || SelectedSensFilter.HasValue || SelectedRubriqueFilter != null;

        public List<KeyValuePair<SensRubrique?, string>> SensFiltreOptions { get; } = new List<KeyValuePair<SensRubrique?, string>>
        {
            new KeyValuePair<SensRubrique?, string>(null, "Tous les sens"),
            new KeyValuePair<SensRubrique?, string>(SensRubrique.Gain, "Gains uniquement"),
            new KeyValuePair<SensRubrique?, string>(SensRubrique.Retenue, "Retenues uniquement")
        };

        public int NombreLignesAffichees => VariablesView?.Cast<VariablePaie>().Count() ?? Variables.Count;

        public ICommand ClearSearchCommand { get; }
        public ICommand ResetFiltersCommand { get; }

        private readonly AuditService _auditService;

        public VariablePaieViewModel(
            VariablePaieService variableService,
            RubriqueService rubriqueService,
            DataService dataService)
            : this(variableService, rubriqueService, dataService, null)
        {
        }

        public VariablePaieViewModel(
            VariablePaieService variableService,
            RubriqueService rubriqueService,
            DataService dataService,
            AuditService auditService)
        {
            if (variableService == null) throw new ArgumentNullException("variableService");
            if (rubriqueService == null) throw new ArgumentNullException("rubriqueService");
            if (dataService == null) throw new ArgumentNullException("dataService");

            _variableService = variableService;
            _rubriqueService = rubriqueService;
            _dataService = dataService;
            _auditService = auditService;

            Title = "Variables du mois";

            VariablesView = CollectionViewSource.GetDefaultView(Variables);
            VariablesView.Filter = FiltrerVariable;

            TotauxView = CollectionViewSource.GetDefaultView(Totaux);
            TotauxView.Filter = FiltrerTotal;

            ClearSearchCommand = new RelayCommand(() => SearchText = string.Empty);
            ResetFiltersCommand = new RelayCommand(() =>
            {
                SearchText = string.Empty;
                SelectedSensFilter = null;
                SelectedRubriqueFilter = null;
            });

            var reference = DateTime.Today;
            _annee = reference.Year;
            _moisSelectionne = MoisOption.Tous.FirstOrDefault(m => m.Numero == reference.Month);

            ChargerCommand = new RelayCommand(async () => await ChargerAsync(), () => !IsBusy);
            EnregistrerCommand = new RelayCommand(async () => await EnregistrerAsync(), () => !IsBusy && PeriodeOuverte);
            AjouterLigneCommand = new RelayCommand(AjouterLigne, () => PeriodeOuverte);
            SupprimerLigneCommand = new RelayCommand(SupprimerLigne, () => VariableSelectionnee != null && PeriodeOuverte);
            AjouterATousCommand = new RelayCommand(AjouterATous, () => PeriodeOuverte && RubriqueAAjouter != null);
            RetirerATousCommand = new RelayCommand(RetirerATous, () => PeriodeOuverte && RubriqueAAjouter != null);
            RetirerSalarieCommand = new RelayCommand(RetirerSalarie, () => PeriodeOuverte && SalarieVise != null);
            ToutEffacerCommand = new RelayCommand(ToutEffacer, () => PeriodeOuverte && Variables.Count > 0);
            ReporterCommand = new RelayCommand(async () => await ReporterAsync(), () => !IsBusy && PeriodeOuverte);

            _ = ChargerAsync();
        }

        private bool FiltrerVariable(object obj)
        {
            var item = obj as VariablePaie;
            if (item == null) return false;

            if (SelectedSensFilter.HasValue && item.Sens != SelectedSensFilter.Value)
                return false;

            if (SelectedRubriqueFilter != null && !string.Equals(item.CodeRubrique, SelectedRubriqueFilter.Code, StringComparison.OrdinalIgnoreCase))
                return false;

            if (!string.IsNullOrWhiteSpace(SearchText))
            {
                string term = SearchText.Trim();
                bool match = (item.NomSalarie != null && item.NomSalarie.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0)
                          || (item.EmployeeId != null && item.EmployeeId.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0)
                          || (item.CodeRubrique != null && item.CodeRubrique.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0)
                          || (item.LibelleRubrique != null && item.LibelleRubrique.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0)
                          || (item.Commentaire != null && item.Commentaire.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0);
                if (!match) return false;
            }

            return true;
        }

        private bool FiltrerTotal(object obj)
        {
            var item = obj as TotalVariablesSalarie;
            if (item == null) return false;

            if (!string.IsNullOrWhiteSpace(SearchText))
            {
                string term = SearchText.Trim();
                bool match = (item.NomSalarie != null && item.NomSalarie.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0)
                          || (item.EmployeeId != null && item.EmployeeId.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0);
                if (!match) return false;
            }

            return true;
        }

        // ── Période ──────────────────────────────────────────────────────────

        public IEnumerable<MoisOption> MoisDisponibles
        {
            get { return MoisOption.Tous; }
        }

        public IEnumerable<int> AnneesDisponibles
        {
            get { return Enumerable.Range(DateTime.Today.Year - 5, 7).ToList(); }
        }

        private int _annee;
        public int Annee
        {
            get { return _annee; }
            set { SetProperty(ref _annee, value); }
        }

        private MoisOption _moisSelectionne;
        public MoisOption MoisSelectionne
        {
            get { return _moisSelectionne; }
            set { SetProperty(ref _moisSelectionne, value); }
        }

        private bool _periodeOuverte = true;
        /// <summary>
        /// Faux quand la période est clôturée : tout l'écran passe en lecture seule. On
        /// laisse voir, on empêche d'écrire.
        /// </summary>
        public bool PeriodeOuverte
        {
            get { return _periodeOuverte; }
            set
            {
                if (SetProperty(ref _periodeOuverte, value))
                {
                    OnPropertyChanged(nameof(PeriodeCloturee));
                    RafraichirCommandes();
                }
            }
        }

        public bool PeriodeCloturee
        {
            get { return !PeriodeOuverte; }
        }

        public string PeriodeLibelle
        {
            get
            {
                return MoisSelectionne == null
                    ? Annee.ToString()
                    : string.Format("{0} {1}", MoisSelectionne.Nom, Annee);
            }
        }

        private int MoisCourant
        {
            get { return MoisSelectionne != null ? MoisSelectionne.Numero : DateTime.Today.Month; }
        }

        // ── Sélection et ajout ───────────────────────────────────────────────

        private VariablePaie _variableSelectionnee;
        public VariablePaie VariableSelectionnee
        {
            get { return _variableSelectionnee; }
            set
            {
                if (SetProperty(ref _variableSelectionnee, value))
                {
                    OnPropertyChanged(nameof(SalarieVise));
                    OnPropertyChanged(nameof(SalarieViseLibelle));
                    RafraichirCommandes();
                }
            }
        }

        private TotalVariablesSalarie _totalSelectionne;
        /// <summary>
        /// Salarié désigné dans le panneau des totaux. C'est là qu'on regarde l'effectif
        /// après un ajout en masse, donc c'est là qu'on doit pouvoir en retirer un.
        /// </summary>
        public TotalVariablesSalarie TotalSelectionne
        {
            get { return _totalSelectionne; }
            set
            {
                if (SetProperty(ref _totalSelectionne, value))
                {
                    OnPropertyChanged(nameof(SalarieVise));
                    OnPropertyChanged(nameof(SalarieViseLibelle));
                    RafraichirCommandes();
                }
            }
        }

        /// <summary>
        /// Le salarié sur lequel porte un retrait en bloc : celui du panneau des totaux
        /// s'il est désigné, sinon celui de la ligne sélectionnée. Les deux panneaux
        /// commandent la même action — on ne devrait pas avoir à deviner lequel compte.
        /// </summary>
        public string SalarieVise
        {
            get
            {
                if (TotalSelectionne != null && !string.IsNullOrWhiteSpace(TotalSelectionne.EmployeeId))
                    return TotalSelectionne.EmployeeId;

                if (VariableSelectionnee != null && !string.IsNullOrWhiteSpace(VariableSelectionnee.EmployeeId))
                    return VariableSelectionnee.EmployeeId;

                return null;
            }
        }

        public string SalarieViseLibelle
        {
            get
            {
                if (TotalSelectionne != null) return TotalSelectionne.NomSalarie;
                if (VariableSelectionnee != null) return VariableSelectionnee.NomSalarie;
                return "aucun salarié désigné";
            }
        }

        private Rubrique _rubriqueAAjouter;
        /// <summary>Rubrique à ajouter d'un coup à tout l'effectif.</summary>
        public Rubrique RubriqueAAjouter
        {
            get { return _rubriqueAAjouter; }
            set
            {
                if (SetProperty(ref _rubriqueAAjouter, value))
                    RafraichirCommandes();
            }
        }

        // ── Compteurs ────────────────────────────────────────────────────────

        public int NombreLignes
        {
            get { return Variables.Count; }
        }

        public decimal TotalGains
        {
            get { return Variables.Where(v => v.Sens == SensRubrique.Gain).Sum(v => v.Montant); }
        }

        public decimal TotalRetenues
        {
            get { return Variables.Where(v => v.Sens == SensRubrique.Retenue).Sum(v => v.Montant); }
        }

        public bool ADesAnomalies
        {
            get { return Anomalies.Count > 0; }
        }

        // ── Commandes ────────────────────────────────────────────────────────

        public ICommand ChargerCommand { get; private set; }
        public ICommand EnregistrerCommand { get; private set; }
        public ICommand AjouterLigneCommand { get; private set; }
        public ICommand SupprimerLigneCommand { get; private set; }
        public ICommand AjouterATousCommand { get; private set; }
        public ICommand RetirerATousCommand { get; private set; }
        public ICommand RetirerSalarieCommand { get; private set; }
        public ICommand ToutEffacerCommand { get; private set; }
        public ICommand ReporterCommand { get; private set; }

        private void RafraichirCommandes()
        {
            foreach (var commande in new[]
            {
                ChargerCommand, EnregistrerCommand, AjouterLigneCommand,
                SupprimerLigneCommand, AjouterATousCommand, RetirerATousCommand,
                RetirerSalarieCommand, ToutEffacerCommand, ReporterCommand
            })
            {
                var relay = commande as RelayCommand;
                if (relay != null) relay.RaiseCanExecuteChanged();
            }
        }

        private async Task ChargerAsync()
        {
            try
            {
                IsBusy = true;
                StatusMessage = "Chargement des variables...";

                _planDePaie = await _rubriqueService.ChargerAsync();

                RubriquesSaisissables.Clear();
                foreach (var rubrique in _planDePaie
                    .Where(r => r.Active && r.ModeCalcul != ModeCalculRubrique.Moteur)
                    .OrderBy(r => r.Ordre))
                {
                    RubriquesSaisissables.Add(rubrique);
                }

                Salaries.Clear();
                foreach (var salarie in (await _dataService.LoadEmployeesAsync())
                    .OrderBy(e => e.LastName).ThenBy(e => e.FirstName))
                {
                    Salaries.Add(salarie);
                }

                PeriodeOuverte = await _variableService.PeriodeOuverteAsync(Annee, MoisCourant);

                Variables.Clear();
                foreach (var variable in await _variableService.ChargerAsync(Annee, MoisCourant))
                    Variables.Add(variable);

                RafraichirTotaux();

                StatusMessage = PeriodeCloturee
                    ? string.Format("{0} — période clôturée : les variables sont en lecture seule.", PeriodeLibelle)
                    : string.Format("{0} — {1} ligne(s) saisie(s).", PeriodeLibelle, Variables.Count);
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

        private void AjouterLigne()
        {
            var salarie = Salaries.FirstOrDefault();
            var rubrique = RubriquesSaisissables.FirstOrDefault(r => r.Sens == SensRubrique.Gain)
                        ?? RubriquesSaisissables.FirstOrDefault();

            var variable = new VariablePaie
            {
                Annee = Annee,
                Mois = MoisCourant,
                EmployeeId = salarie != null ? salarie.EmployeeId : "",
                NomSalarie = salarie != null
                    ? string.Format("{0} {1}", salarie.LastName, salarie.FirstName).Trim()
                    : "",
                CodeRubrique = rubrique != null ? rubrique.Code : "",
                LibelleRubrique = rubrique != null ? rubrique.Libelle : "",
                Sens = rubrique != null ? rubrique.Sens : SensRubrique.Gain
            };

            Variables.Add(variable);
            VariableSelectionnee = variable;
            RafraichirTotaux();
        }

        private void SupprimerLigne()
        {
            if (VariableSelectionnee == null) return;

            Variables.Remove(VariableSelectionnee);
            VariableSelectionnee = null;
            RafraichirTotaux();
        }

        /// <summary>
        /// Ajoute la rubrique choisie à tous les salariés qui ne l'ont pas déjà. Le geste
        /// qui fait gagner une matinée sur un effectif de cinquante : une prime de fin
        /// d'année, une retenue de cotisation syndicale.
        /// </summary>
        private void AjouterATous()
        {
            if (RubriqueAAjouter == null) return;

            string code = RubriqueAAjouter.Code;
            int ajoutes = 0;

            foreach (var salarie in Salaries)
            {
                bool existe = Variables.Any(v =>
                    string.Equals(v.EmployeeId, salarie.EmployeeId, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(v.CodeRubrique, code, StringComparison.OrdinalIgnoreCase));

                if (existe) continue;

                Variables.Add(new VariablePaie
                {
                    Annee = Annee,
                    Mois = MoisCourant,
                    EmployeeId = salarie.EmployeeId,
                    NomSalarie = string.Format("{0} {1}", salarie.LastName, salarie.FirstName).Trim(),
                    CodeRubrique = code,
                    LibelleRubrique = RubriqueAAjouter.Libelle,
                    Sens = RubriqueAAjouter.Sens,
                    Montant = RubriqueAAjouter.MontantFixe
                });

                ajoutes++;
            }

            RafraichirTotaux();

            StatusMessage = ajoutes == 0
                ? string.Format("Tous les salariés portent déjà la rubrique « {0} ».", code)
                : string.Format(
                    "Rubrique « {0} » ajoutée à {1} salarié(s). Ajustez les montants, puis enregistrez.",
                    code, ajoutes);
        }

        /// <summary>
        /// Retire toutes les lignes d'un salarié — le geste qui manquait après un ajout en
        /// masse. Retirer une ligne à la fois sur un effectif de cinquante n'est pas une
        /// correction, c'est une punition.
        /// </summary>
        private void RetirerSalarie()
        {
            string employeeId = SalarieVise;
            if (string.IsNullOrWhiteSpace(employeeId)) return;

            string nom = SalarieViseLibelle;

            var aRetirer = Variables
                .Where(v => string.Equals(v.EmployeeId, employeeId, StringComparison.OrdinalIgnoreCase))
                .ToList();

            foreach (var variable in aRetirer)
                Variables.Remove(variable);

            VariableSelectionnee = null;
            TotalSelectionne = null;
            RafraichirTotaux();

            StatusMessage = aRetirer.Count == 0
                ? string.Format("{0} n'a aucune variable sur cette période.", nom)
                : string.Format(
                    "{0} : {1} ligne(s) retirée(s). Enregistrez pour que le retrait soit acquis.",
                    nom, aRetirer.Count);
        }

        /// <summary>
        /// Retire la rubrique choisie de tous les salariés : le symétrique exact de
        /// « Ajouter à tous ». Une prime ajoutée par erreur à l'effectif entier se défait
        /// du même geste qu'elle s'est faite.
        /// </summary>
        private void RetirerATous()
        {
            if (RubriqueAAjouter == null) return;

            string code = RubriqueAAjouter.Code;

            var aRetirer = Variables
                .Where(v => string.Equals(v.CodeRubrique, code, StringComparison.OrdinalIgnoreCase))
                .ToList();

            foreach (var variable in aRetirer)
                Variables.Remove(variable);

            VariableSelectionnee = null;
            TotalSelectionne = null;
            RafraichirTotaux();

            StatusMessage = aRetirer.Count == 0
                ? string.Format("Aucune ligne ne porte la rubrique « {0} ».", code)
                : string.Format(
                    "Rubrique « {0} » retirée de {1} ligne(s). Enregistrez pour que le retrait soit acquis.",
                    code, aRetirer.Count);
        }

        /// <summary>
        /// Vide la saisie du mois. Rien n'est perdu en base tant que l'écran n'est pas
        /// enregistré — « Recharger » ramène la dernière version enregistrée.
        /// </summary>
        private void ToutEffacer()
        {
            int compte = Variables.Count;

            Variables.Clear();
            VariableSelectionnee = null;
            TotalSelectionne = null;
            RafraichirTotaux();

            StatusMessage = string.Format(
                "{0} ligne(s) retirée(s) de la saisie. Rien n'est perdu en base : « Recharger » " +
                "ramène la dernière version enregistrée, « Enregistrer » valide le vidage.",
                compte);
        }

        /// <summary>
        /// Recopie les variables du mois précédent. Remplace ce qui est saisi : l'écran
        /// prévient avant, parce que c'est la seule action de cet écran qui détruit une
        /// saisie en cours.
        /// </summary>
        private async Task ReporterAsync()
        {
            try
            {
                IsBusy = true;

                var precedent = new DateTime(Annee, MoisCourant, 1).AddMonths(-1);

                int reportees = await _variableService.ReporterAsync(
                    precedent.Year, precedent.Month, Annee, MoisCourant);

                if (reportees == 0)
                {
                    StatusMessage = string.Format(
                        "Aucune variable sur {0:MM/yyyy} : il n'y a rien à reporter.", precedent);
                    return;
                }

                await ChargerAsync();

                StatusMessage = string.Format(
                    "{0} ligne(s) reportée(s) depuis {1:MM/yyyy}. Vérifiez les montants avant de clôturer.",
                    reportees, precedent);
            }
            catch (InvalidOperationException ex)
            {
                StatusMessage = ex.Message;
            }
            catch (Exception ex)
            {
                StatusMessage = "Erreur lors du report : " + ex.Message;
            }
            finally
            {
                IsBusy = false;
            }
        }

        private async Task EnregistrerAsync()
        {
            try
            {
                IsBusy = true;

                // Le sens et le libellé sont resynchronisés depuis le plan de paie : si
                // l'utilisateur a changé le code d'une ligne à la main, la ligne doit
                // suivre sa nouvelle rubrique, pas garder l'ancien sens.
                foreach (var variable in Variables)
                {
                    var rubrique = _planDePaie.FirstOrDefault(r =>
                        string.Equals(r.Code, variable.CodeRubrique, StringComparison.OrdinalIgnoreCase));

                    if (rubrique != null)
                    {
                        variable.LibelleRubrique = rubrique.Libelle;
                        variable.Sens = rubrique.Sens;
                    }

                    variable.Annee = Annee;
                    variable.Mois = MoisCourant;
                }

                await _variableService.EnregistrerToutAsync(Annee, MoisCourant, Variables);

                if (_auditService != null)
                {
                    decimal totalMontant = Variables.Sum(v => v.Montant);
                    await _auditService.AuditerVariablesAsync(
                        Annee,
                        MoisCourant,
                        Variables.Count,
                        totalMontant,
                        string.Format("Enregistrement de {0} variable(s) pour la période {1} (Total des montants : {2:N0} FCFA)",
                            Variables.Count, PeriodeLibelle, totalMontant));
                }

                await ChargerAsync();

                StatusMessage = string.Format(
                    "{0} — {1} ligne(s) enregistrée(s).", PeriodeLibelle, Variables.Count);
            }
            catch (InvalidOperationException ex)
            {
                StatusMessage = ex.Message;
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

        private void RafraichirTotaux()
        {
            Totaux.Clear();

            foreach (var total in Variables
                .Where(v => !string.IsNullOrWhiteSpace(v.EmployeeId))
                .GroupBy(v => v.EmployeeId)
                .Select(g => new TotalVariablesSalarie
                {
                    EmployeeId = g.Key,
                    NomSalarie = g.First().NomSalarie,
                    NombreLignes = g.Count(),
                    TotalGains = g.Where(v => v.Sens == SensRubrique.Gain).Sum(v => v.Montant),
                    TotalRetenues = g.Where(v => v.Sens == SensRubrique.Retenue).Sum(v => v.Montant)
                })
                .OrderBy(t => t.NomSalarie))
            {
                Totaux.Add(total);
            }

            Anomalies.Clear();
            foreach (string anomalie in VariablePaieService.Anomalies(Variables, _planDePaie))
                Anomalies.Add(anomalie);

            OnPropertyChanged(nameof(NombreLignes));
            OnPropertyChanged(nameof(TotalGains));
            OnPropertyChanged(nameof(TotalRetenues));
            OnPropertyChanged(nameof(ADesAnomalies));
            OnPropertyChanged(nameof(PeriodeLibelle));
            OnPropertyChanged(nameof(SalarieVise));
            OnPropertyChanged(nameof(SalarieViseLibelle));
            VariablesView?.Refresh();
            TotauxView?.Refresh();
            OnPropertyChanged(nameof(NombreLignesAffichees));
            RafraichirCommandes();
        }
    }
}
