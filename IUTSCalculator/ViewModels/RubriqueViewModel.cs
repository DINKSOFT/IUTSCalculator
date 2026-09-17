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
    /// Écran « Plan de paie » (module SOC-01) : liste des rubriques à gauche, fiche de la
    /// rubrique sélectionnée à droite, sur le même principe que l'écran Employés.
    /// </summary>
    public class RubriqueViewModel : BaseViewModel
    {
        private readonly RubriqueService _rubriqueService;

        public ObservableCollection<Rubrique> Rubriques { get; } = new ObservableCollection<Rubrique>();
        public ICollectionView RubriquesView { get; }

        private string _searchText;
        public string SearchText
        {
            get => _searchText;
            set
            {
                if (SetProperty(ref _searchText, value))
                {
                    RubriquesView?.Refresh();
                    OnPropertyChanged(nameof(HasSearchText));
                    OnPropertyChanged(nameof(HasActiveFilters));
                    OnPropertyChanged(nameof(NombreRubriquesAffichees));
                }
            }
        }

        public bool HasSearchText => !string.IsNullOrWhiteSpace(SearchText);

        private SensRubrique? _selectedSensFilter;
        public SensRubrique? SelectedSensFilter
        {
            get => _selectedSensFilter;
            set
            {
                if (SetProperty(ref _selectedSensFilter, value))
                {
                    RubriquesView?.Refresh();
                    OnPropertyChanged(nameof(HasActiveFilters));
                    OnPropertyChanged(nameof(NombreRubriquesAffichees));
                }
            }
        }

        public bool HasActiveFilters => HasSearchText || SelectedSensFilter.HasValue;

        public List<KeyValuePair<SensRubrique?, string>> SensFiltreOptions { get; } = new List<KeyValuePair<SensRubrique?, string>>
        {
            new KeyValuePair<SensRubrique?, string>(null, "Tous les sens"),
            new KeyValuePair<SensRubrique?, string>(SensRubrique.Gain, "Gains uniquement"),
            new KeyValuePair<SensRubrique?, string>(SensRubrique.Retenue, "Retenues uniquement"),
            new KeyValuePair<SensRubrique?, string>(SensRubrique.Information, "Informations")
        };

        public int NombreRubriquesAffichees => RubriquesView?.Cast<Rubrique>().Count() ?? Rubriques.Count;

        public ICommand ClearSearchCommand { get; }
        public ICommand ResetFiltersCommand { get; }

        public RubriqueViewModel(RubriqueService rubriqueService)
        {
            if (rubriqueService == null) throw new ArgumentNullException("rubriqueService");
            _rubriqueService = rubriqueService;

            Title = "Plan de paie";

            RubriquesView = CollectionViewSource.GetDefaultView(Rubriques);
            RubriquesView.Filter = FiltrerRubrique;

            ClearSearchCommand = new RelayCommand(() => SearchText = string.Empty);
            ResetFiltersCommand = new RelayCommand(() =>
            {
                SearchText = string.Empty;
                SelectedSensFilter = null;
            });

            EnregistrerCommand = new RelayCommand(async () => await EnregistrerAsync(), PeutEnregistrer);
            NouvelleRubriqueCommand = new RelayCommand(NouvelleRubrique);
            SupprimerCommand = new RelayCommand(async () => await SupprimerAsync(), () => FicheCourante != null);
            AnnulerCommand = new RelayCommand(Annuler, () => FicheCourante != null);
            RechargerCommand = new RelayCommand(async () => await ChargerAsync());

            _ = ChargerAsync();
        }

        /// <summary>
        /// Cet écran ne se rechargeait jamais automatiquement après sa première ouverture :
        /// revenir dessus après qu'une rubrique a été ajoutée ou modifiée ailleurs (ou par
        /// un collègue sur le même dossier) continuait d'afficher un plan de paie périmé
        /// tant qu'on ne cliquait pas sur « Recharger » — signalé le 16 septembre 2026 (voir
        /// ETAT-DEVELOPPEMENT.md item 22). Sans risque pour une fiche en cours de
        /// modification : voir le garde « mêmeRubrique » dans RubriqueSelectionnee.
        /// </summary>
        protected override void OnIsActiveChanged()
        {
            if (IsActive)
                _ = ChargerAsync();
        }

        private bool FiltrerRubrique(object obj)
        {
            if (!(obj is Rubrique rub)) return false;

            // Filtre par sens
            if (SelectedSensFilter.HasValue && rub.Sens != SelectedSensFilter.Value)
                return false;

            // Recherche par code ou libellé
            if (!string.IsNullOrWhiteSpace(SearchText))
            {
                string q = SearchText.Trim();
                bool match = (rub.Code != null && rub.Code.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0)
                          || (rub.Libelle != null && rub.Libelle.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0)
                          || (rub.CompteComptable != null && rub.CompteComptable.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0);
                if (!match) return false;
            }

            return true;
        }

        // ── Sélection et fiche ───────────────────────────────────────────────

        private Rubrique _rubriqueSelectionnee;
        public Rubrique RubriqueSelectionnee
        {
            get { return _rubriqueSelectionnee; }
            set
            {
                // ChargerAsync() réaffecte cette propriété après chaque rechargement avec
                // une NOUVELLE instance représentant la MÊME rubrique (même Code). Recloner
                // FicheCourante à chaque fois écraserait alors une modification en cours de
                // saisie dès qu'on revient sur cet écran.
                bool memeRubrique = _rubriqueSelectionnee != null && value != null
                    && string.Equals(_rubriqueSelectionnee.Code, value.Code, StringComparison.OrdinalIgnoreCase);

                if (SetProperty(ref _rubriqueSelectionnee, value))
                {
                    // Copie de travail : la liste n'est modifiée qu'à l'enregistrement.
                    if (!memeRubrique)
                    {
                        FicheCourante = value != null ? value.Clone() : null;
                        EstEnEdition = value != null;
                    }
                }
            }
        }

        private Rubrique _ficheCourante;
        public Rubrique FicheCourante
        {
            get { return _ficheCourante; }
            set
            {
                if (SetProperty(ref _ficheCourante, value))
                {
                    OnPropertyChanged(nameof(ALaFiche));
                    RafraichirCommandes();
                }
            }
        }

        private bool _estEnEdition;
        /// <summary>Vrai lorsqu'on modifie une rubrique existante, faux pour une création.</summary>
        public bool EstEnEdition
        {
            get { return _estEnEdition; }
            set { SetProperty(ref _estEnEdition, value); }
        }

        /// <summary>Une fiche est ouverte : le panneau de droite a quelque chose à montrer.</summary>
        public bool ALaFiche
        {
            get { return FicheCourante != null; }
        }

        // ── Listes de choix ──────────────────────────────────────────────────

        public IEnumerable<SensRubrique> SensDisponibles
        {
            get { return new[] { SensRubrique.Gain, SensRubrique.Retenue, SensRubrique.Information }; }
        }

        public IEnumerable<ModeCalculRubrique> ModesCalcul
        {
            get
            {
                return new[]
                {
                    ModeCalculRubrique.MontantFixe,
                    ModeCalculRubrique.BaseParTaux,
                    ModeCalculRubrique.PourcentageSalaireBase,
                    ModeCalculRubrique.Moteur
                };
            }
        }

        /// <summary>Codes utilisables comme base de calcul d'une autre rubrique.</summary>
        public IEnumerable<string> CodesBase
        {
            get { return Rubriques.Where(r => r.Sens == SensRubrique.Gain).Select(r => r.Code).ToList(); }
        }

        // ── Compteurs d'en-tête ──────────────────────────────────────────────

        public int NombreGains
        {
            get { return Rubriques.Count(r => r.Sens == SensRubrique.Gain && r.Active); }
        }

        public int NombreRetenues
        {
            get { return Rubriques.Count(r => r.Sens == SensRubrique.Retenue && r.Active); }
        }

        // ── Commandes ────────────────────────────────────────────────────────

        public ICommand EnregistrerCommand { get; private set; }
        public ICommand NouvelleRubriqueCommand { get; private set; }
        public ICommand SupprimerCommand { get; private set; }
        public ICommand AnnulerCommand { get; private set; }
        public ICommand RechargerCommand { get; private set; }

        private bool PeutEnregistrer()
        {
            return !IsBusy
                && FicheCourante != null
                && !string.IsNullOrWhiteSpace(FicheCourante.Code)
                && !string.IsNullOrWhiteSpace(FicheCourante.Libelle);
        }

        private void RafraichirCommandes()
        {
            var enregistrer = EnregistrerCommand as RelayCommand;
            if (enregistrer != null) enregistrer.RaiseCanExecuteChanged();
            var supprimer = SupprimerCommand as RelayCommand;
            if (supprimer != null) supprimer.RaiseCanExecuteChanged();
            var annuler = AnnulerCommand as RelayCommand;
            if (annuler != null) annuler.RaiseCanExecuteChanged();
        }

        private async Task ChargerAsync()
        {
            try
            {
                IsBusy = true;
                StatusMessage = "Chargement du plan de paie...";

                string codeRetenu = RubriqueSelectionnee != null ? RubriqueSelectionnee.Code : null;

                var liste = await _rubriqueService.ChargerAsync();

                Rubriques.Clear();
                foreach (var r in liste)
                    Rubriques.Add(r);

                RubriqueSelectionnee = !string.IsNullOrEmpty(codeRetenu)
                    ? Rubriques.FirstOrDefault(r => string.Equals(r.Code, codeRetenu, StringComparison.OrdinalIgnoreCase))
                    : null;

                RafraichirTotaux();
                StatusMessage = string.Format("{0} rubriques dans le plan de paie.", Rubriques.Count);
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

        private void NouvelleRubrique()
        {
            RubriqueSelectionnee = null;

            // Une rubrique créée à la main est un gain à montant fixe soumis aux deux
            // assiettes : c'est le cas de très loin le plus fréquent (prime, indemnité).
            FicheCourante = new Rubrique
            {
                Sens = SensRubrique.Gain,
                ModeCalcul = ModeCalculRubrique.MontantFixe,
                SoumisIUTS = true,
                SoumisCNSS = true,
                Active = true,
                EstSysteme = false,
                Ordre = ProchainOrdre()
            };
            EstEnEdition = false;
            StatusMessage = "Nouvelle rubrique.";
        }

        private int ProchainOrdre()
        {
            if (Rubriques.Count == 0) return 10;
            return ((Rubriques.Max(r => r.Ordre) / 10) + 1) * 10;
        }

        private async Task EnregistrerAsync()
        {
            if (FicheCourante == null) return;

            try
            {
                IsBusy = true;

                string code = (FicheCourante.Code ?? "").Trim().ToUpperInvariant();
                FicheCourante.Code = code;

                // Un code déjà pris écraserait silencieusement une autre rubrique :
                // INSERT OR REPLACE remplace la ligne de même clé primaire.
                if (!EstEnEdition && await _rubriqueService.CodeExisteAsync(code))
                {
                    StatusMessage = string.Format(
                        "Le code « {0} » est déjà utilisé par une autre rubrique.", code);
                    return;
                }

                await _rubriqueService.EnregistrerAsync(FicheCourante);

                var existante = Rubriques.FirstOrDefault(r =>
                    string.Equals(r.Code, code, StringComparison.OrdinalIgnoreCase));

                if (existante != null)
                    Rubriques[Rubriques.IndexOf(existante)] = FicheCourante.Clone();
                else
                    Rubriques.Add(FicheCourante.Clone());

                Trier();
                RafraichirTotaux();
                StatusMessage = string.Format("Rubrique « {0} » enregistrée.", code);
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

        private async Task SupprimerAsync()
        {
            if (FicheCourante == null) return;

            try
            {
                IsBusy = true;
                bool systeme = FicheCourante.EstSysteme;

                await _rubriqueService.SupprimerAsync(FicheCourante);
                await ChargerAsync();

                RubriqueSelectionnee = null;
                FicheCourante = null;

                StatusMessage = systeme
                    ? "Rubrique système désactivée : elle reste dans le plan car le calcul s'y réfère."
                    : "Rubrique supprimée.";
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

        private void Annuler()
        {
            RubriqueSelectionnee = null;
            FicheCourante = null;
            EstEnEdition = false;
            StatusMessage = "Modification abandonnée.";
        }

        private void Trier()
        {
            var triees = Rubriques.OrderBy(r => r.Ordre).ThenBy(r => r.Code).ToList();
            Rubriques.Clear();
            foreach (var r in triees)
                Rubriques.Add(r);
        }

        private void RafraichirTotaux()
        {
            OnPropertyChanged(nameof(NombreGains));
            OnPropertyChanged(nameof(NombreRetenues));
            OnPropertyChanged(nameof(CodesBase));
            RubriquesView?.Refresh();
            OnPropertyChanged(nameof(NombreRubriquesAffichees));
        }
    }
}
