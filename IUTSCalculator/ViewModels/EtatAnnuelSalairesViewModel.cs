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
    /// Écran « État annuel des salaires (DADS) » (module DEC-05).
    ///
    /// Permet de consulter et d'exporter le récapitulatif annuel par salarié et par
    /// rubrique : cumuls de brut, de base imposable, d'IUTS retenu et de cotisations,
    /// alimentés directement par les mois clôturés (module PAI-01). Même schéma
    /// d'écran que « Livre de paie » (ETA-01), sur une année plutôt qu'un mois.
    /// </summary>
    public class EtatAnnuelSalairesViewModel : BaseViewModel
    {
        private readonly EtatAnnuelSalairesService _etatService;
        private readonly EtatAnnuelSalairesExportService _exportService;
        private readonly Dossier _dossier;

        public EtatAnnuelSalairesViewModel(
            EtatAnnuelSalairesService etatService,
            EtatAnnuelSalairesExportService exportService,
            Dossier dossier)
        {
            if (etatService == null) throw new ArgumentNullException("etatService");
            if (exportService == null) throw new ArgumentNullException("exportService");
            if (dossier == null) throw new ArgumentNullException("dossier");

            _etatService = etatService;
            _exportService = exportService;
            _dossier = dossier;

            Title = "État annuel des salaires";

            // Par défaut, l'année précédente : c'est celle qui a le plus de chances
            // d'être entièrement clôturée au moment où on édite l'état.
            _annee = DateTime.Today.AddYears(-1).Year;

            GenererCommand = new RelayCommand(async () => await GenererAsync(), () => !IsBusy);
            ExporterCommand = new RelayCommand(Exporter, () => EtatAnnuel != null && !IsBusy);
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

        private EtatAnnuelSalaires _etatAnnuel;
        public EtatAnnuelSalaires EtatAnnuel
        {
            get { return _etatAnnuel; }
            set
            {
                if (SetProperty(ref _etatAnnuel, value))
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

                    OnPropertyChanged(nameof(AEtatAnnuel));
                    OnPropertyChanged(nameof(ADesAnomalies));
                    OnPropertyChanged(nameof(EstPartiel));
                    RafraichirCommandes();
                }
            }
        }

        public ObservableCollection<LigneEtatAnnuelSalaires> Lignes { get; } =
            new ObservableCollection<LigneEtatAnnuelSalaires>();

        public ObservableCollection<string> Anomalies { get; } =
            new ObservableCollection<string>();

        public bool AEtatAnnuel => EtatAnnuel != null;

        public bool ADesAnomalies => Anomalies.Count > 0;

        /// <summary>
        /// Vrai seulement une fois un état généré ET incomplet — jamais avant toute
        /// génération. Une simple négation de <c>EstComplet</c> se serait évaluée à
        /// « vrai » tant qu'aucun état n'existe encore (liaison sur une propriété
        /// nulle), affichant le bandeau d'alerte alors qu'il n'y a encore rien à l'écran.
        /// </summary>
        public bool EstPartiel => EtatAnnuel != null && !EtatAnnuel.EstComplet;

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
                StatusMessage = "Construction de l'état annuel des salaires...";

                var etat = await _etatService.ConstruireAsync(_dossier, Annee);
                EtatAnnuel = etat;

                if (etat.Lignes.Count == 0)
                {
                    StatusMessage = string.Format(
                        "Aucun cumul disponible pour {0} ({1} mois clôturé(s) sur 12).",
                        Annee, etat.MoisClotures.Count);
                    return;
                }

                StatusMessage = string.Format(
                    "{0} — {1} salarié(s), {2} mois clôturé(s) sur 12. Brut cumulé : {3:N0} FCFA, " +
                    "IUTS cumulé : {4:N0} FCFA. {5}",
                    etat.PeriodeLibelle,
                    etat.Effectif,
                    etat.MoisClotures.Count,
                    etat.TotalSalaireBrut,
                    etat.TotalIuts,
                    etat.EstComplet ? "Année complète." : "Cumul partiel — voir les avertissements.");
            }
            catch (Exception ex)
            {
                EtatAnnuel = null;
                StatusMessage = "Erreur lors de la construction : " + ex.Message;
            }
            finally
            {
                IsBusy = false;
            }
        }

        private void Exporter()
        {
            if (EtatAnnuel == null) return;

            try
            {
                IsBusy = true;

                string chemin = ExcelExportHelper.SaveFileDialog(EtatAnnuel.NomFichierPropose);
                if (string.IsNullOrWhiteSpace(chemin))
                {
                    StatusMessage = "Export annulé.";
                    return;
                }

                _exportService.Exporter(EtatAnnuel, chemin);

                StatusMessage = string.Format("État annuel des salaires enregistré : {0}", chemin);

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
