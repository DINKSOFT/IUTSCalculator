using System;
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
    /// Écran « Tableau de bord masse salariale » (module PIL-01).
    ///
    /// Évolution mensuelle du brut, du net et du coût employeur sur une année, avec
    /// ventilation par catégorie et par service. Même schéma d'écran que « État annuel
    /// des salaires » (DEC-05) : sélection d'une année, génération, export — alimenté
    /// directement par les mois clôturés (module PAI-01).
    /// </summary>
    public class TableauBordMasseSalarialeViewModel : BaseViewModel
    {
        private readonly TableauBordMasseSalarialeService _tableauService;
        private readonly TableauBordMasseSalarialeExportService _exportService;
        private readonly Dossier _dossier;

        public TableauBordMasseSalarialeViewModel(
            TableauBordMasseSalarialeService tableauService,
            TableauBordMasseSalarialeExportService exportService,
            Dossier dossier)
        {
            if (tableauService == null) throw new ArgumentNullException("tableauService");
            if (exportService == null) throw new ArgumentNullException("exportService");
            if (dossier == null) throw new ArgumentNullException("dossier");

            _tableauService = tableauService;
            _exportService = exportService;
            _dossier = dossier;

            Title = "Tableau de bord masse salariale";

            // Par défaut, l'année précédente : c'est celle qui a le plus de chances
            // d'être entièrement clôturée au moment où on consulte le tableau de bord.
            _annee = DateTime.Today.AddYears(-1).Year;

            GenererCommand = new RelayCommand(async () => await GenererAsync(), () => !IsBusy);
            ExporterCommand = new RelayCommand(Exporter, () => Tableau != null && !IsBusy);
        }

        // ── Paramètres ───────────────────────────────────────────────────────

        public System.Collections.Generic.IEnumerable<int> AnneesDisponibles
        {
            get { return Enumerable.Range(DateTime.Today.Year - 6, 7).ToList(); }
        }

        private int _annee;
        public int Annee
        {
            get { return _annee; }
            set
            {
                if (SetProperty(ref _annee, value))
                    OnPropertyChanged(nameof(PeriodeChoisieLibelle));
            }
        }

        public string PeriodeChoisieLibelle => string.Format("Année {0}", Annee);

        // ── Résultat ─────────────────────────────────────────────────────────

        private TableauBordMasseSalariale _tableau;
        public TableauBordMasseSalariale Tableau
        {
            get { return _tableau; }
            set
            {
                if (SetProperty(ref _tableau, value))
                {
                    Evolution.Clear();
                    ParCategorie.Clear();
                    ParService.Clear();
                    Anomalies.Clear();

                    if (value != null)
                    {
                        foreach (var point in value.Evolution)
                            Evolution.Add(point);

                        foreach (var ligne in value.ParCategorie)
                            ParCategorie.Add(ligne);

                        foreach (var ligne in value.ParService)
                            ParService.Add(ligne);

                        foreach (string anomalie in value.Anomalies())
                            Anomalies.Add(anomalie);
                    }

                    OnPropertyChanged(nameof(ATableau));
                    OnPropertyChanged(nameof(ADesAnomalies));
                    OnPropertyChanged(nameof(EstPartiel));
                    RafraichirCommandes();
                }
            }
        }

        public ObservableCollection<PointEvolutionMensuelle> Evolution { get; } =
            new ObservableCollection<PointEvolutionMensuelle>();

        public ObservableCollection<VentilationMasseSalariale> ParCategorie { get; } =
            new ObservableCollection<VentilationMasseSalariale>();

        public ObservableCollection<VentilationMasseSalariale> ParService { get; } =
            new ObservableCollection<VentilationMasseSalariale>();

        public ObservableCollection<string> Anomalies { get; } =
            new ObservableCollection<string>();

        public bool ATableau => Tableau != null;

        public bool ADesAnomalies => Anomalies.Count > 0;

        /// <summary>
        /// Vrai seulement une fois un tableau généré ET incomplet — jamais avant toute
        /// génération (même précaution que sur l'état annuel des salaires : lier
        /// directement l'inverse de <c>EstComplet</c> afficherait le bandeau tant
        /// qu'aucun tableau n'existe encore, la liaison sur une propriété nulle
        /// s'évaluant à faux puis à vrai une fois inversée).
        /// </summary>
        public bool EstPartiel => Tableau != null && !Tableau.EstComplet;

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
                StatusMessage = "Construction du tableau de bord masse salariale...";

                var tableau = await _tableauService.ConstruireAsync(_dossier, Annee);
                Tableau = tableau;

                if (tableau.Evolution.Count == 0)
                {
                    StatusMessage = string.Format(
                        "Aucun mois clôturé pour {0} : rien à afficher.", Annee);
                    return;
                }

                StatusMessage = string.Format(
                    "{0} — {1} mois clôturé(s) sur 12. Coût employeur cumulé : {2:N0} FCFA " +
                    "(moyenne mensuelle : {3:N0} FCFA). {4}",
                    tableau.PeriodeLibelle,
                    tableau.MoisClotures.Count,
                    tableau.TotalCoutEmployeur,
                    tableau.MoyenneMensuelleCoutEmployeur,
                    tableau.EstComplet ? "Année complète." : "Année partielle — voir les avertissements.");
            }
            catch (Exception ex)
            {
                Tableau = null;
                StatusMessage = "Erreur lors de la construction : " + ex.Message;
            }
            finally
            {
                IsBusy = false;
            }
        }

        private void Exporter()
        {
            if (Tableau == null) return;

            try
            {
                IsBusy = true;

                string chemin = ExcelExportHelper.SaveFileDialog(Tableau.NomFichierPropose);
                if (string.IsNullOrWhiteSpace(chemin))
                {
                    StatusMessage = "Export annulé.";
                    return;
                }

                _exportService.Exporter(Tableau, chemin);

                StatusMessage = string.Format("Tableau de bord masse salariale enregistré : {0}", chemin);

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
