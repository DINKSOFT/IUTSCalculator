using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using Microsoft.Win32;
using IUTSCalculator.Helpers;
using IUTSCalculator.Models;
using IUTSCalculator.Services;

namespace IUTSCalculator.ViewModels
{
    /// <summary>
    /// ViewModel transverse pour le suivi budgétaire des postes, l'analyse GPEC (budget vs pourvu)
    /// et la gestion du catalogue d'entreprise des compétences (module RH-02).
    /// </summary>
    public class SuiviPostesBudgetViewModel : BaseViewModel
    {
        private readonly FichePosteService _fichePosteService;
        private readonly FichePosteExportService _exportService;
        private readonly OrganigrammeService _organigrammeService;
        private readonly Dossier _dossier;

        public SuiviPostesBudgetViewModel(
            FichePosteService fichePosteService,
            FichePosteExportService exportService,
            OrganigrammeService organigrammeService,
            Dossier dossier)
        {
            _fichePosteService = fichePosteService ?? throw new ArgumentNullException(nameof(fichePosteService));
            _exportService = exportService ?? throw new ArgumentNullException(nameof(exportService));
            _organigrammeService = organigrammeService ?? throw new ArgumentNullException(nameof(organigrammeService));
            _dossier = dossier ?? throw new ArgumentNullException(nameof(dossier));

            Title = "Suivi Budgétaire des Postes";

            RechargerCommand = new RelayCommand(async () => await ChargerToutAsync(), () => !IsBusy);
            ExporterExcelCommand = new RelayCommand(ExporterExcel, () => !IsBusy);

            // Commandes Compétences
            NouvelleCompetenceCommand = new RelayCommand(NouvelleCompetence);
            EditerCompetenceCommand = new RelayCommand(EditerCompetence, () => CompetenceSelectionnee != null);
            EnregistrerCompetenceCommand = new RelayCommand(async () => await EnregistrerCompetenceAsync(), () => CompetenceEnEdition != null && !IsBusy);
            SupprimerCompetenceCommand = new RelayCommand(async () => await SupprimerCompetenceAsync(), () => CompetenceSelectionnee != null && !IsBusy);
            AnnulerEditionCompetenceCommand = new RelayCommand(() => EstEnEditionCompetence = false);
        }

        #region Propriétés d'état & Navigation

        private int _selectedTabIndex;
        public int SelectedTabIndex
        {
            get => _selectedTabIndex;
            set { if (SetProperty(ref _selectedTabIndex, value)) { OnPropertyChanged(nameof(EstOngletBudget)); OnPropertyChanged(nameof(EstOngletCompetences)); } }
        }

        public bool EstOngletBudget => _selectedTabIndex == 0;
        public bool EstOngletCompetences => _selectedTabIndex == 1;

        private string _rechercheTexte;
        public string RechercheTexte
        {
            get => _rechercheTexte;
            set
            {
                if (SetProperty(ref _rechercheTexte, value))
                {
                    FiltrerDonnees();
                }
            }
        }

        private string _filtreStatut = "Tous";
        public string FiltreStatut
        {
            get => _filtreStatut;
            set
            {
                if (SetProperty(ref _filtreStatut, value))
                {
                    FiltrerDonnees();
                }
            }
        }

        private string _filtreService = "Tous";
        public string FiltreService
        {
            get => _filtreService;
            set
            {
                if (SetProperty(ref _filtreService, value))
                {
                    FiltrerDonnees();
                }
            }
        }

        public ObservableCollection<string> ServicesDisponibles { get; } = new ObservableCollection<string>();

        #endregion

        #region KPIs & Synthèse GPEC

        private int _totalPostes;
        public int TotalPostes { get => _totalPostes; set => SetProperty(ref _totalPostes, value); }

        private int _totalBudgete;
        public int TotalBudgete { get => _totalBudgete; set => SetProperty(ref _totalBudgete, value); }

        private int _totalPourvu;
        public int TotalPourvu { get => _totalPourvu; set => SetProperty(ref _totalPourvu, value); }

        private int _totalVacants;
        public int TotalVacants { get => _totalVacants; set => SetProperty(ref _totalVacants, value); }

        private int _totalSureffectif;
        public int TotalSureffectif { get => _totalSureffectif; set => SetProperty(ref _totalSureffectif, value); }

        private double _tauxOccupationGlobal;
        public double TauxOccupationGlobal { get => _tauxOccupationGlobal; set => SetProperty(ref _tauxOccupationGlobal, value); }

        public string TauxOccupationGlobalTexte => $"{TauxOccupationGlobal:F1} %";

        #endregion

        #region Données Budget vs Pourvu

        private List<SynthesePosteBudget> _toutesSyntheses = new List<SynthesePosteBudget>();
        public ObservableCollection<SynthesePosteBudget> PostesBudget { get; } = new ObservableCollection<SynthesePosteBudget>();

        private SynthesePosteBudget _posteBudgetSelectionne;
        public SynthesePosteBudget PosteBudgetSelectionne
        {
            get => _posteBudgetSelectionne;
            set => SetProperty(ref _posteBudgetSelectionne, value);
        }

        #endregion

        #region Catalogue des Compétences

        private List<Competence> _toutesCompetences = new List<Competence>();
        public ObservableCollection<Competence> Competences { get; } = new ObservableCollection<Competence>();

        private Competence _competenceSelectionnee;
        public Competence CompetenceSelectionnee
        {
            get => _competenceSelectionnee;
            set
            {
                if (SetProperty(ref _competenceSelectionnee, value))
                {
                    EditerCompetence();
                }
            }
        }

        private Competence _competenceEnEdition;
        public Competence CompetenceEnEdition
        {
            get => _competenceEnEdition;
            set => SetProperty(ref _competenceEnEdition, value);
        }

        private bool _estEnEditionCompetence;
        public bool EstEnEditionCompetence
        {
            get => _estEnEditionCompetence;
            set => SetProperty(ref _estEnEditionCompetence, value);
        }

        public IEnumerable<DomaineCompetence> DomainesDisponibles =>
            Enum.GetValues(typeof(DomaineCompetence)).Cast<DomaineCompetence>();

        #endregion

        #region Commandes

        public ICommand RechargerCommand { get; }
        public ICommand ExporterExcelCommand { get; }

        public ICommand NouvelleCompetenceCommand { get; }
        public ICommand EditerCompetenceCommand { get; }
        public ICommand EnregistrerCompetenceCommand { get; }
        public ICommand SupprimerCompetenceCommand { get; }
        public ICommand AnnulerEditionCompetenceCommand { get; }

        #endregion

        #region Cycle de Vie & Chargement

        protected override void OnIsActiveChanged()
        {
            base.OnIsActiveChanged();
            if (IsActive)
            {
                _ = ChargerToutAsync();
            }
        }

        public async Task ChargerToutAsync()
        {
            try
            {
                IsBusy = true;
                StatusMessage = "Chargement des synthèses budgétaires et des compétences...";

                _toutesSyntheses = await _fichePosteService.CalculerSyntheseBudgetaireEffectifsAsync();
                _toutesCompetences = await _fichePosteService.ChargerToutesCompetencesAsync();

                // Services uniques pour le filtre
                ServicesDisponibles.Clear();
                ServicesDisponibles.Add("Tous");
                var services = _toutesSyntheses
                    .Select(s => s.UniteLibelle)
                    .Where(s => !string.IsNullOrWhiteSpace(s))
                    .Distinct()
                    .OrderBy(s => s);
                foreach (var s in services) ServicesDisponibles.Add(s);

                CalculerKpi();
                FiltrerDonnees();

                StatusMessage = $"Données à jour : {_toutesSyntheses.Count} postes, {_toutesCompetences.Count} compétences.";
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

        private void CalculerKpi()
        {
            TotalPostes = _toutesSyntheses.Count;
            TotalBudgete = _toutesSyntheses.Sum(s => s.EffectifBudgete);
            TotalPourvu = _toutesSyntheses.Sum(s => s.EffectifPourvu);
            TotalVacants = _toutesSyntheses.Count(s => s.EffectifPourvu == 0);
            TotalSureffectif = _toutesSyntheses.Count(s => s.EffectifPourvu > s.EffectifBudgete);
            TauxOccupationGlobal = TotalBudgete > 0 ? Math.Round((double)TotalPourvu / TotalBudgete * 100.0, 1) : 0;
            OnPropertyChanged(nameof(TauxOccupationGlobalTexte));
        }

        private void FiltrerDonnees()
        {
            // 1. Filtrer les postes budget
            PostesBudget.Clear();
            var qBudget = _toutesSyntheses.AsEnumerable();

            if (_filtreStatut != "Tous")
            {
                qBudget = qBudget.Where(s => s.StatutOccupation.Equals(_filtreStatut, StringComparison.OrdinalIgnoreCase));
            }

            if (_filtreService != "Tous" && !string.IsNullOrWhiteSpace(_filtreService))
            {
                qBudget = qBudget.Where(s => s.UniteLibelle.Equals(_filtreService, StringComparison.OrdinalIgnoreCase));
            }

            if (!string.IsNullOrWhiteSpace(_rechercheTexte))
            {
                qBudget = qBudget.Where(s =>
                    (!string.IsNullOrEmpty(s.Code) && s.Code.IndexOf(_rechercheTexte, StringComparison.OrdinalIgnoreCase) >= 0) ||
                    (!string.IsNullOrEmpty(s.Intitule) && s.Intitule.IndexOf(_rechercheTexte, StringComparison.OrdinalIgnoreCase) >= 0) ||
                    (!string.IsNullOrEmpty(s.UniteLibelle) && s.UniteLibelle.IndexOf(_rechercheTexte, StringComparison.OrdinalIgnoreCase) >= 0) ||
                    (s.Titulaires != null && s.Titulaires.Any(t => t.NomComplet.IndexOf(_rechercheTexte, StringComparison.OrdinalIgnoreCase) >= 0)));
            }

            foreach (var b in qBudget) PostesBudget.Add(b);

            // 2. Filtrer les compétences
            Competences.Clear();
            var qComp = _toutesCompetences.AsEnumerable();
            if (!string.IsNullOrWhiteSpace(_rechercheTexte))
            {
                qComp = qComp.Where(c =>
                    (!string.IsNullOrEmpty(c.Code) && c.Code.IndexOf(_rechercheTexte, StringComparison.OrdinalIgnoreCase) >= 0) ||
                    (!string.IsNullOrEmpty(c.Libelle) && c.Libelle.IndexOf(_rechercheTexte, StringComparison.OrdinalIgnoreCase) >= 0) ||
                    (!string.IsNullOrEmpty(c.Description) && c.Description.IndexOf(_rechercheTexte, StringComparison.OrdinalIgnoreCase) >= 0));
            }
            foreach (var c in qComp) Competences.Add(c);
        }

        #endregion

        #region Gestion des Compétences

        private void NouvelleCompetence()
        {
            CompetenceEnEdition = new Competence
            {
                Id = 0,
                Code = "COMP-" + (_toutesCompetences.Count + 1).ToString("D2"),
                Libelle = "Nouvelle compétence",
                Domaine = DomaineCompetence.Technique
            };
            EstEnEditionCompetence = true;
        }

        private void EditerCompetence()
        {
            if (CompetenceSelectionnee == null) return;

            CompetenceEnEdition = new Competence
            {
                Id = CompetenceSelectionnee.Id,
                Code = CompetenceSelectionnee.Code,
                Libelle = CompetenceSelectionnee.Libelle,
                Domaine = CompetenceSelectionnee.Domaine,
                Description = CompetenceSelectionnee.Description,
                DateCreation = CompetenceSelectionnee.DateCreation
            };
            EstEnEditionCompetence = true;
        }

        private async Task EnregistrerCompetenceAsync()
        {
            if (CompetenceEnEdition == null) return;

            try
            {
                IsBusy = true;
                await _fichePosteService.EnregistrerCompetenceAsync(CompetenceEnEdition);
                EstEnEditionCompetence = false;
                await ChargerToutAsync();
                StatusMessage = "Compétence enregistrée dans le catalogue.";
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Validation compétence", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            finally
            {
                IsBusy = false;
            }
        }

        private async Task SupprimerCompetenceAsync()
        {
            if (CompetenceSelectionnee == null) return;

            var res = MessageBox.Show(
                $"Êtes-vous sûr de vouloir supprimer la compétence '{CompetenceSelectionnee.Libelle}' ?\nElle sera également retirée des fiches de poste associées.",
                "Confirmation de suppression",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (res != MessageBoxResult.Yes) return;

            try
            {
                IsBusy = true;
                await _fichePosteService.SupprimerCompetenceAsync(CompetenceSelectionnee.Id);
                EstEnEditionCompetence = false;
                await ChargerToutAsync();
                StatusMessage = "Compétence supprimée.";
            }
            catch (Exception ex)
            {
                MessageBox.Show("Erreur lors de la suppression : " + ex.Message, "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                IsBusy = false;
            }
        }

        #endregion

        #region Export Excel

        private void ExporterExcel()
        {
            try
            {
                var sfd = new SaveFileDialog
                {
                    Title = "Exporter le suivi budgétaire des postes",
                    Filter = "Fichier Excel (*.xlsx)|*.xlsx",
                    FileName = $"Suivi_Budget_Postes_{DateTime.Now:yyyyMMdd_HHmm}.xlsx"
                };

                if (sfd.ShowDialog() == true)
                {
                    _exportService.ExporterSyntheseExcel(
                        _toutesSyntheses,
                        _toutesCompetences,
                        new List<PosteCompetence>(),
                        _dossier?.RaisonSociale ?? "Entreprise",
                        sfd.FileName);

                    StatusMessage = $"Export Excel généré : {System.IO.Path.GetFileName(sfd.FileName)}";
                    MessageBox.Show("Le classeur Excel de suivi budgétaire et référentiel a été généré avec succès.", "Export terminé", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Erreur lors de l'export Excel : " + ex.Message, "Erreur export", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        #endregion
    }
}
