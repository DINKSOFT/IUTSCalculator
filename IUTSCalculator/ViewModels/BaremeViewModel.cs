using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using IUTSCalculator.Helpers;
using IUTSCalculator.Models;
using IUTSCalculator.Services;

namespace IUTSCalculator.ViewModels
{
    /// <summary>
    /// Écran « Barèmes légaux » (module SOC-02) : la liste datée des jeux de paramètres à
    /// gauche, le détail du barème sélectionné à droite.
    ///
    /// L'écran pousse volontairement vers la duplication plutôt que vers la correction.
    /// Modifier un barème déjà utilisé change rétroactivement des bulletins émis ; créer
    /// le barème suivant avec sa date d'effet est le geste juste, et c'est celui que le
    /// bouton principal propose.
    /// </summary>
    public class BaremeViewModel : BaseViewModel
    {
        private readonly BaremeService _baremeService;

        public ObservableCollection<BaremeLegal> Baremes { get; } = new ObservableCollection<BaremeLegal>();

        public BaremeViewModel(BaremeService baremeService)
        {
            if (baremeService == null) throw new ArgumentNullException("baremeService");
            _baremeService = baremeService;

            Title = "Barèmes légaux";

            _periodeTest = DateTime.Today;

            EnregistrerCommand = new RelayCommand(async () => await EnregistrerAsync(), PeutEnregistrer);
            DupliquerCommand = new RelayCommand(Dupliquer, () => BaremeSelectionne != null);
            SupprimerCommand = new RelayCommand(async () => await SupprimerAsync(), () => FicheCourante != null && FicheCourante.Id > 0);
            AnnulerCommand = new RelayCommand(Annuler, () => FicheCourante != null);
            RechargerCommand = new RelayCommand(async () => await ChargerAsync());
            AjouterTrancheCommand = new RelayCommand(AjouterTranche, () => FicheCourante != null);
            SupprimerTrancheCommand = new RelayCommand(SupprimerTranche, () => TrancheSelectionnee != null);

            _ = ChargerAsync();
        }

        /// <summary>
        /// Cet écran ne se rechargeait jamais automatiquement après sa première ouverture :
        /// revenir dessus après qu'un barème a été ajouté ou modifié ailleurs (ou par un
        /// collègue sur le même dossier) continuait d'afficher une liste périmée tant qu'on
        /// ne cliquait pas sur « Recharger » — signalé le 16 septembre 2026 (voir
        /// ETAT-DEVELOPPEMENT.md item 22). Sans risque pour une fiche en cours de
        /// modification : voir le garde « mêmeBarème » dans BaremeSelectionne.
        /// </summary>
        protected override void OnIsActiveChanged()
        {
            if (IsActive)
                _ = ChargerAsync();
        }

        // ── Sélection et fiche ───────────────────────────────────────────────

        private BaremeLegal _baremeSelectionne;
        public BaremeLegal BaremeSelectionne
        {
            get { return _baremeSelectionne; }
            set
            {
                // ChargerAsync() réaffecte cette propriété après chaque rechargement avec
                // une NOUVELLE instance représentant le MÊME barème. Recloner FicheCourante
                // à chaque fois écraserait alors une modification en cours de saisie dès
                // qu'on revient sur cet écran.
                bool memeBareme = _baremeSelectionne != null && value != null
                    && _baremeSelectionne.Id == value.Id;

                if (SetProperty(ref _baremeSelectionne, value))
                {
                    // Copie de travail : tant qu'on n'a pas enregistré, la liste — et donc
                    // la base — restent intactes.
                    if (!memeBareme)
                        FicheCourante = value != null ? value.Clone() : null;
                }
            }
        }

        private BaremeLegal _ficheCourante;
        public BaremeLegal FicheCourante
        {
            get { return _ficheCourante; }
            set
            {
                var precedente = _ficheCourante;

                if (SetProperty(ref _ficheCourante, value))
                {
                    Desabonner(precedente);
                    Abonner(value);

                    TrancheSelectionnee = null;
                    OnPropertyChanged(nameof(ALaFiche));
                    OnPropertyChanged(nameof(EstBaremeSysteme));
                    RafraichirCommandes();
                }
            }
        }

        /// <summary>
        /// Suit les tranches de la fiche ouverte pour recoller les bornes dès qu'une borne
        /// supérieure change : sans cela, la grille afficherait des bornes incohérentes
        /// jusqu'à l'enregistrement, et l'utilisateur ne verrait pas le trou qu'il vient
        /// de créer entre deux paliers.
        /// </summary>
        private void Abonner(BaremeLegal bareme)
        {
            if (bareme == null) return;

            bareme.Tranches.CollectionChanged += TranchesModifiees;
            foreach (var tranche in bareme.Tranches)
                tranche.PropertyChanged += TrancheModifiee;
        }

        private void Desabonner(BaremeLegal bareme)
        {
            if (bareme == null) return;

            bareme.Tranches.CollectionChanged -= TranchesModifiees;
            foreach (var tranche in bareme.Tranches)
                tranche.PropertyChanged -= TrancheModifiee;
        }

        private void TranchesModifiees(object sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.OldItems != null)
            {
                foreach (TrancheBareme tranche in e.OldItems)
                    tranche.PropertyChanged -= TrancheModifiee;
            }

            if (e.NewItems != null)
            {
                foreach (TrancheBareme tranche in e.NewItems)
                    tranche.PropertyChanged += TrancheModifiee;
            }
        }

        private void TrancheModifiee(object sender, PropertyChangedEventArgs e)
        {
            // Seule la borne haute est saisie ; la borne basse en découle. Renuméroter
            // ne modifie que des bornes basses, il n'y a donc pas de boucle.
            if (e.PropertyName == nameof(TrancheBareme.BorneSuperieure))
                Renumeroter();
        }

        private TrancheBareme _trancheSelectionnee;
        public TrancheBareme TrancheSelectionnee
        {
            get { return _trancheSelectionnee; }
            set
            {
                if (SetProperty(ref _trancheSelectionnee, value))
                    RafraichirCommandes();
            }
        }

        public bool ALaFiche
        {
            get { return FicheCourante != null; }
        }

        /// <summary>Le barème ouvert est celui livré avec l'application.</summary>
        public bool EstBaremeSysteme
        {
            get { return FicheCourante != null && FicheCourante.EstSysteme; }
        }

        // ── Vérification : quel barème s'applique à telle période ? ──────────

        private DateTime _periodeTest;
        /// <summary>
        /// Période servant à vérifier la sélection. Sans elle, on ne peut pas répondre à
        /// la seule question qui compte devant un contrôle : quel barème a servi à ce mois
        /// de paie ?
        /// </summary>
        public DateTime PeriodeTest
        {
            get { return _periodeTest; }
            set
            {
                if (SetProperty(ref _periodeTest, value))
                    OnPropertyChanged(nameof(BaremeApplicableLibelle));
            }
        }

        public string BaremeApplicableLibelle
        {
            get
            {
                if (Baremes.Count == 0)
                    return "Aucun barème enregistré.";

                var applicable = BaremeService.BaremeApplicable(Baremes, PeriodeTest);
                if (applicable == null)
                    return "Aucun barème applicable à cette période.";

                if (applicable.DateEffet.Date > PeriodeTest.Date)
                {
                    return string.Format(
                        "Aucun barème n'était en vigueur à cette date ; « {0} » serait retenu par défaut.",
                        applicable.Designation);
                }

                return string.Format("Barème appliqué : {0}", applicable.Designation);
            }
        }

        public int NombreBaremes
        {
            get { return Baremes.Count; }
        }

        // ── Commandes ────────────────────────────────────────────────────────

        public ICommand EnregistrerCommand { get; private set; }
        public ICommand DupliquerCommand { get; private set; }
        public ICommand SupprimerCommand { get; private set; }
        public ICommand AnnulerCommand { get; private set; }
        public ICommand RechargerCommand { get; private set; }
        public ICommand AjouterTrancheCommand { get; private set; }
        public ICommand SupprimerTrancheCommand { get; private set; }

        private bool PeutEnregistrer()
        {
            return !IsBusy && FicheCourante != null;
        }

        private void RafraichirCommandes()
        {
            foreach (var commande in new[]
            {
                EnregistrerCommand, DupliquerCommand, SupprimerCommand,
                AnnulerCommand, AjouterTrancheCommand, SupprimerTrancheCommand
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
                StatusMessage = "Chargement des barèmes...";

                int idRetenu = BaremeSelectionne != null ? BaremeSelectionne.Id : 0;

                var liste = await _baremeService.ChargerAsync();

                Baremes.Clear();
                foreach (var b in liste)
                    Baremes.Add(b);

                BaremeSelectionne = idRetenu > 0
                    ? Baremes.FirstOrDefault(b => b.Id == idRetenu)
                    : null;

                OnPropertyChanged(nameof(NombreBaremes));
                OnPropertyChanged(nameof(BaremeApplicableLibelle));

                StatusMessage = string.Format(
                    "{0} barème{1} enregistré{1}.", Baremes.Count, Baremes.Count > 1 ? "s" : "");
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

        /// <summary>
        /// Prépare le barème suivant à partir du plus récent : mêmes valeurs, nouvelle
        /// date d'effet, à corriger là où le texte a changé.
        /// </summary>
        private void Dupliquer()
        {
            if (BaremeSelectionne == null) return;

            var source = BaremeSelectionne;
            var proposition = new DateTime(Math.Max(source.DateEffet.Year + 1, DateTime.Today.Year), 1, 1);

            _baremeSelectionne = null;
            OnPropertyChanged(nameof(BaremeSelectionne));

            FicheCourante = BaremeService.Dupliquer(source, proposition);

            StatusMessage = string.Format(
                "Nouveau barème préparé à partir de « {0} ». Ajustez la date d'effet et les valeurs " +
                "modifiées par le texte, puis enregistrez.", source.Designation);
        }

        /// <summary>
        /// Ajout, suppression et recollage des bornes délégués à
        /// <see cref="EditionTranches"/>, partagée avec l'écran « Paramètres »
        /// (SettingsViewModel) — voir sa documentation pour le détail du geste.
        /// </summary>
        private void AjouterTranche()
        {
            if (FicheCourante == null) return;

            var nouvelle = EditionTranches.Ajouter(FicheCourante.Tranches);

            TrancheSelectionnee = nouvelle;
            StatusMessage = "Tranche ajoutée : renseignez sa borne supérieure et son taux.";
        }

        private void SupprimerTranche()
        {
            if (FicheCourante == null || TrancheSelectionnee == null) return;

            EditionTranches.Supprimer(FicheCourante.Tranches, TrancheSelectionnee);

            TrancheSelectionnee = null;
            StatusMessage = "Tranche supprimée ; les bornes voisines ont été recollées.";
        }

        private void Renumeroter()
        {
            if (FicheCourante == null) return;
            EditionTranches.Renumeroter(FicheCourante.Tranches);
        }

        private async Task EnregistrerAsync()
        {
            if (FicheCourante == null) return;

            try
            {
                IsBusy = true;

                Renumeroter();

                var anomalies = FicheCourante.Anomalies();
                if (anomalies.Count > 0)
                {
                    StatusMessage = "Barème non enregistré — " + string.Join(" ", anomalies);
                    return;
                }

                if (await _baremeService.DateEffetExisteAsync(FicheCourante.DateEffet, FicheCourante.Id))
                {
                    StatusMessage = string.Format(
                        "Un autre barème porte déjà la date d'effet du {0:dd/MM/yyyy}. " +
                        "Deux barèmes à la même date rendraient le choix arbitraire.",
                        FicheCourante.DateEffet);
                    return;
                }

                await _baremeService.EnregistrerAsync(FicheCourante);

                int id = FicheCourante.Id;
                await ChargerAsync();

                BaremeSelectionne = Baremes.FirstOrDefault(b => b.Id == id);
                StatusMessage = string.Format(
                    "Barème « {0} » enregistré.", BaremeSelectionne != null ? BaremeSelectionne.Designation : "");
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
            if (FicheCourante == null || FicheCourante.Id <= 0) return;

            try
            {
                IsBusy = true;

                await _baremeService.SupprimerAsync(FicheCourante);
                await ChargerAsync();

                BaremeSelectionne = null;
                FicheCourante = null;

                StatusMessage = "Barème supprimé.";
            }
            catch (InvalidOperationException ex)
            {
                StatusMessage = ex.Message;
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
            BaremeSelectionne = null;
            FicheCourante = null;
            StatusMessage = "Modification abandonnée.";
        }
    }
}
