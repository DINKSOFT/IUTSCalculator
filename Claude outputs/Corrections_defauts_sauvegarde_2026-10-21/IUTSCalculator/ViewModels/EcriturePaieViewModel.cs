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
using Microsoft.Win32;

namespace IUTSCalculator.ViewModels
{
    /// <summary>
    /// ViewModel pour l'écran « Écritures de paie SYSCOHADA » (module CPT-01).
    /// </summary>
    public class EcriturePaieViewModel : BaseViewModel
    {
        private readonly EcriturePaieService _ecritureService;
        private readonly EcriturePaieExportService _exportService;
        private readonly Dossier _dossier;

        public EcriturePaieViewModel(
            EcriturePaieService ecritureService,
            EcriturePaieExportService exportService,
            Dossier dossier)
        {
            if (ecritureService == null) throw new ArgumentNullException(nameof(ecritureService));
            if (exportService == null) throw new ArgumentNullException(nameof(exportService));
            if (dossier == null) throw new ArgumentNullException(nameof(dossier));

            _ecritureService = ecritureService;
            _exportService = exportService;
            _dossier = dossier;

            Title = "Écritures de paie";

            // Mois précédent par défaut
            var reference = DateTime.Today.AddMonths(-1);
            _annee = reference.Year;
            _moisSelectionne = MoisDisponibles.FirstOrDefault(m => m.Numero == reference.Month);

            GenererCommand = new RelayCommand(async () => await GenererAsync(), () => !IsBusy);
            ExporterExcelCommand = new RelayCommand(ExporterExcel, () => PieceComptable != null && !IsBusy);
            ExporterTxtCommand = new RelayCommand(ExporterTxt, () => PieceComptable != null && !IsBusy);
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

        // ── Données ──────────────────────────────────────────────────────────

        private PieceComptable _pieceComptable;
        public PieceComptable PieceComptable
        {
            get { return _pieceComptable; }
            set
            {
                if (SetProperty(ref _pieceComptable, value))
                {
                    Lignes.Clear();
                    Anomalies.Clear();

                    if (value != null)
                    {
                        foreach (var l in value.Lignes)
                            Lignes.Add(l);

                        foreach (var a in value.Anomalies())
                            Anomalies.Add(a);
                    }

                    OnPropertyChanged(nameof(APieceComptable));
                    OnPropertyChanged(nameof(ADesAnomalies));
                    OnPropertyChanged(nameof(EstEquilibree));
                    OnPropertyChanged(nameof(TotalDebit));
                    OnPropertyChanged(nameof(TotalCredit));
                    OnPropertyChanged(nameof(Ecart));
                    RafraichirCommandes();
                }
            }
        }

        public ObservableCollection<LigneEcritureComptable> Lignes { get; } =
            new ObservableCollection<LigneEcritureComptable>();

        public ObservableCollection<string> Anomalies { get; } =
            new ObservableCollection<string>();

        public bool APieceComptable
        {
            get { return PieceComptable != null; }
        }

        public bool ADesAnomalies
        {
            get { return Anomalies.Count > 0; }
        }

        public bool EstEquilibree
        {
            get { return PieceComptable != null && PieceComptable.EstEquilibree; }
        }

        public decimal TotalDebit
        {
            get { return PieceComptable != null ? PieceComptable.TotalDebit : 0m; }
        }

        public decimal TotalCredit
        {
            get { return PieceComptable != null ? PieceComptable.TotalCredit : 0m; }
        }

        public decimal Ecart
        {
            get { return PieceComptable != null ? PieceComptable.Ecart : 0m; }
        }

        // ── Commandes ────────────────────────────────────────────────────────

        public ICommand GenererCommand { get; }
        public ICommand ExporterExcelCommand { get; }
        public ICommand ExporterTxtCommand { get; }

        private void RafraichirCommandes()
        {
            foreach (var cmd in new[] { GenererCommand, ExporterExcelCommand, ExporterTxtCommand })
            {
                var relay = cmd as RelayCommand;
                if (relay != null) relay.RaiseCanExecuteChanged();
            }
        }

        private async Task GenererAsync()
        {
            try
            {
                IsBusy = true;
                StatusMessage = "Génération de la pièce comptable...";

                if (MoisSelectionne == null)
                {
                    StatusMessage = "Choisissez le mois à traiter.";
                    return;
                }

                var piece = await _ecritureService.ConstruireAsync(_dossier, Annee, MoisSelectionne.Numero);
                PieceComptable = piece;

                if (piece.Lignes.Count == 0)
                {
                    StatusMessage = string.Format("Aucun bulletin enregistré pour {0}.", piece.PeriodeLibelle);
                    return;
                }

                StatusMessage = string.Format(
                    "Écriture {0} générée : {1} ligne(s), Total Débit : {2:N0} FCFA, Total Crédit : {3:N0} FCFA. {4}",
                    piece.NumeroPiece,
                    piece.NombreLignes,
                    piece.TotalDebit,
                    piece.TotalCredit,
                    piece.EstEquilibree ? "Pièce équilibrée." : string.Format("Déséquilibre : {0:N0} FCFA !", piece.Ecart));
            }
            catch (Exception ex)
            {
                PieceComptable = null;
                StatusMessage = "Erreur lors de la génération de l'écriture : " + ex.Message;
            }
            finally
            {
                IsBusy = false;
            }
        }

        private void ExporterExcel()
        {
            if (PieceComptable == null) return;

            try
            {
                IsBusy = true;
                string chemin = ExcelExportHelper.SaveFileDialog(PieceComptable.NomFichierExcel);
                if (string.IsNullOrWhiteSpace(chemin))
                {
                    StatusMessage = "Export Excel annulé.";
                    return;
                }

                _exportService.ExporterExcel(PieceComptable, chemin);
                StatusMessage = string.Format("Pièce comptable exportée vers Excel : {0}", chemin);

                try
                {
                    Process.Start(new ProcessStartInfo(chemin) { UseShellExecute = true });
                }
                catch { }
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

        private void ExporterTxt()
        {
            if (PieceComptable == null) return;

            try
            {
                IsBusy = true;
                var dlg = new SaveFileDialog
                {
                    FileName = PieceComptable.NomFichierTxt,
                    DefaultExt = ".txt",
                    Filter = "Fichier texte délimité (*.txt;*.csv)|*.txt;*.csv|Tous les fichiers (*.*)|*.*"
                };

                if (dlg.ShowDialog() != true)
                {
                    StatusMessage = "Export texte annulé.";
                    return;
                }

                _exportService.ExporterTexte(PieceComptable, dlg.FileName);
                StatusMessage = string.Format("Fichier d'import comptable généré : {0}", dlg.FileName);

                try
                {
                    Process.Start(new ProcessStartInfo(dlg.FileName) { UseShellExecute = true });
                }
                catch { }
            }
            catch (Exception ex)
            {
                StatusMessage = "Erreur lors de l'export texte : " + ex.Message;
            }
            finally
            {
                IsBusy = false;
            }
        }
    }
}
