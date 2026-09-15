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
    /// Écran « Déclaration IUTS/TPA » (module DEC-03).
    ///
    /// Plus simple que l'écran CNSS : l'IUTS et la TPA se déclarent chaque mois, sans
    /// seuil d'effectif ni choix de périodicité. L'écran ne demande donc que le mois, puis
    /// construit la déclaration à partir de la paie déjà saisie.
    /// </summary>
    public class DeclarationIutsTpaViewModel : BaseViewModel
    {
        private readonly DeclarationIutsTpaService _declarationService;
        private readonly DeclarationIutsTpaExportService _exportService;
        private readonly Dossier _dossier;

        public DeclarationIutsTpaViewModel(
            DeclarationIutsTpaService declarationService,
            DeclarationIutsTpaExportService exportService,
            Dossier dossier)
        {
            if (declarationService == null) throw new ArgumentNullException("declarationService");
            if (exportService == null) throw new ArgumentNullException("exportService");
            if (dossier == null) throw new ArgumentNullException("dossier");

            _declarationService = declarationService;
            _exportService = exportService;
            _dossier = dossier;

            Title = "Déclaration IUTS/TPA";

            // Par défaut, le mois précédent : c'est celui qu'on déclare.
            var reference = DateTime.Today.AddMonths(-1);
            _annee = reference.Year;
            _moisSelectionne = MoisDisponibles.FirstOrDefault(m => m.Numero == reference.Month);

            GenererCommand = new RelayCommand(async () => await GenererAsync(), () => !IsBusy);
            ExporterCommand = new RelayCommand(Exporter, () => Declaration != null && !IsBusy);
        }

        // ── Paramètres de la déclaration ─────────────────────────────────────

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

        // ── Résultat ─────────────────────────────────────────────────────────

        private DeclarationIutsTpa _declaration;
        public DeclarationIutsTpa Declaration
        {
            get { return _declaration; }
            set
            {
                if (SetProperty(ref _declaration, value))
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

                    OnPropertyChanged(nameof(ADeclaration));
                    OnPropertyChanged(nameof(ADesAnomalies));
                    RafraichirCommandes();
                }
            }
        }

        public ObservableCollection<LigneDeclarationIutsTpa> Lignes { get; } =
            new ObservableCollection<LigneDeclarationIutsTpa>();

        public ObservableCollection<string> Anomalies { get; } =
            new ObservableCollection<string>();

        public bool ADeclaration
        {
            get { return Declaration != null; }
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
                StatusMessage = "Construction de la déclaration...";

                if (MoisSelectionne == null)
                {
                    StatusMessage = "Choisissez le mois à déclarer.";
                    return;
                }

                var declaration = await _declarationService.ConstruireAsync(_dossier, Annee, MoisSelectionne.Numero);
                Declaration = declaration;

                if (declaration.Lignes.Count == 0)
                {
                    StatusMessage = string.Format(
                        "Aucun bulletin sur {0} : il n'y a rien à déclarer.", declaration.PeriodeLibelle);
                    return;
                }

                StatusMessage = string.Format(
                    "{0} — {1} salarié(s), {2:N0} FCFA d'IUTS retenu, {3:N0} FCFA de TPA. {4}",
                    declaration.PeriodeLibelle,
                    declaration.Effectif,
                    declaration.TotalIUTSRetenu,
                    declaration.TotalTaxePatronale,
                    Anomalies.Count == 0
                        ? "Aucune anomalie."
                        : string.Format("{0} point(s) à vérifier avant dépôt.", Anomalies.Count));
            }
            catch (Exception ex)
            {
                Declaration = null;
                StatusMessage = "Erreur lors de la construction : " + ex.Message;
            }
            finally
            {
                IsBusy = false;
            }
        }

        private void Exporter()
        {
            if (Declaration == null) return;

            try
            {
                IsBusy = true;

                string chemin = ExcelExportHelper.SaveFileDialog(Declaration.NomFichierPropose);
                if (string.IsNullOrWhiteSpace(chemin))
                {
                    StatusMessage = "Export annulé.";
                    return;
                }

                _exportService.Exporter(Declaration, chemin);

                StatusMessage = string.Format("Bordereau et état nominatif enregistrés : {0}", chemin);

                // Ouvrir le classeur : on vient de le produire pour le relire, autant
                // épargner un aller-retour dans l'explorateur.
                try
                {
                    Process.Start(new ProcessStartInfo(chemin) { UseShellExecute = true });
                }
                catch
                {
                    // Aucune application associée au format : le fichier est écrit, c'est
                    // l'essentiel, et le chemin est affiché.
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
