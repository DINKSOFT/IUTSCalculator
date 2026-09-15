using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using IUTSCalculator.Helpers;
using IUTSCalculator.Models;
using IUTSCalculator.Services;

namespace IUTSCalculator.ViewModels
{
    /// <summary>
    /// Écran « Livre de paie » (module ETA-01).
    ///
    /// Permet de consulter et d'exporter l'état récapitulatif mensuel de la paie :
    /// tous les salariés en lignes, toutes les rubriques en colonnes, totaux de contrôle.
    /// Lit les bulletins enregistrés et signale les anomalies (mois non clôturé, etc.).
    /// </summary>
    public class LivrePaieViewModel : BaseViewModel
    {
        private readonly LivrePaieService _livreService;
        private readonly LivrePaieExportService _exportService;
        private readonly Dossier _dossier;

        public LivrePaieViewModel(
            LivrePaieService livreService,
            LivrePaieExportService exportService,
            Dossier dossier)
        {
            if (livreService == null) throw new ArgumentNullException("livreService");
            if (exportService == null) throw new ArgumentNullException("exportService");
            if (dossier == null) throw new ArgumentNullException("dossier");

            _livreService = livreService;
            _exportService = exportService;
            _dossier = dossier;

            Title = "Livre de paie";

            // Par défaut, le mois précédent
            var reference = DateTime.Today.AddMonths(-1);
            _annee = reference.Year;
            _moisSelectionne = MoisDisponibles.FirstOrDefault(m => m.Numero == reference.Month);

            GenererCommand = new RelayCommand(async () => await GenererAsync(), () => !IsBusy);
            ExporterCommand = new RelayCommand(Exporter, () => LivrePaie != null && !IsBusy);
        }

        // ── Paramètres ───────────────────────────────────────────────────────

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
            set
            {
                if (SetProperty(ref _annee, value))
                {
                    OnPropertyChanged(nameof(PeriodeChoisieLibelle));
                }
            }
        }

        private MoisOption _moisSelectionne;
        public MoisOption MoisSelectionne
        {
            get { return _moisSelectionne; }
            set
            {
                if (SetProperty(ref _moisSelectionne, value))
                {
                    OnPropertyChanged(nameof(PeriodeChoisieLibelle));
                }
            }
        }

        public string PeriodeChoisieLibelle
        {
            get
            {
                return MoisSelectionne == null
                    ? string.Format("{0}", Annee)
                    : string.Format("{0} {1}", MoisSelectionne.Nom, Annee);
            }
        }

        // ── Résultat ─────────────────────────────────────────────────────────

        private LivrePaie _livrePaie;
        public LivrePaie LivrePaie
        {
            get { return _livrePaie; }
            set
            {
                if (SetProperty(ref _livrePaie, value))
                {
                    Lignes.Clear();
                    Anomalies.Clear();

                    if (value != null)
                    {
                        foreach (var ligne in value.Lignes.OrderBy(l => l.Nom).ThenBy(l => l.Prenom))
                            Lignes.Add(ligne);

                        foreach (string anomalie in value.Anomalies())
                            Anomalies.Add(anomalie);
                    }

                    OnPropertyChanged(nameof(ALivrePaie));
                    OnPropertyChanged(nameof(ADesAnomalies));
                    RafraichirCommandes();
                }
            }
        }

        public ObservableCollection<LigneLivrePaie> Lignes { get; } =
            new ObservableCollection<LigneLivrePaie>();

        public ObservableCollection<string> Anomalies { get; } =
            new ObservableCollection<string>();

        public bool ALivrePaie
        {
            get { return LivrePaie != null; }
        }

        public bool ADesAnomalies
        {
            get { return Anomalies.Count > 0; }
        }

        // ── Commandes ────────────────────────────────────────────────────────

        public ICommand GenererCommand { get; private set; }
        public ICommand ExporterCommand { get; private set; }

        private void RafraichirCommandes()
        {
            foreach (var commande in new[] { GenererCommand, ExporterCommand })
            {
                var relay = commande as RelayCommand;
                if (relay != null) relay.RaiseCanExecuteChanged();
            }
        }

        private async Task GenererAsync()
        {
            try
            {
                IsBusy = true;
                StatusMessage = "Construction du livre de paie...";

                if (MoisSelectionne == null)
                {
                    StatusMessage = "Choisissez le mois à éditer.";
                    return;
                }

                var livre = await _livreService.ConstruireAsync(_dossier, Annee, MoisSelectionne.Numero);
                LivrePaie = livre;

                if (livre.Lignes.Count == 0)
                {
                    StatusMessage = string.Format("Aucun bulletin enregistré sur {0}.", livre.PeriodeLibelle);
                    return;
                }

                StatusMessage = string.Format(
                    "{0} — {1} salarié(s), Brut : {2:N0} FCFA, Net : {3:N0} FCFA, Masse salariale : {4:N0} FCFA. {5}",
                    livre.PeriodeLibelle,
                    livre.Effectif,
                    livre.TotalBrut,
                    livre.TotalNetAPayer,
                    livre.TotalMasseSalariale,
                    Anomalies.Count == 0
                        ? "Aucune anomalie."
                        : string.Format("{0} avertissement(s).", Anomalies.Count));
            }
            catch (Exception ex)
            {
                LivrePaie = null;
                StatusMessage = "Erreur lors de la construction : " + ex.Message;
            }
            finally
            {
                IsBusy = false;
            }
        }

        private void Exporter()
        {
            if (LivrePaie == null) return;

            try
            {
                IsBusy = true;

                string chemin = ExcelExportHelper.SaveFileDialog(LivrePaie.NomFichierPropose);
                if (string.IsNullOrWhiteSpace(chemin))
                {
                    StatusMessage = "Export annulé.";
                    return;
                }

                _exportService.Exporter(LivrePaie, chemin);

                StatusMessage = string.Format("Livre de paie enregistré : {0}", chemin);

                try
                {
                    Process.Start(new ProcessStartInfo(chemin) { UseShellExecute = true });
                }
                catch
                {
                    // L'export a réussi
                }
            }
            catch (Exception ex)
            {
                StatusMessage = "Erreur lors de l'export : " + ex.Message;
            }
            finally
            {
                IsBusy = false;
            }
        }
    }
}
