using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
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
    /// ViewModel pour l'écran « Ordres de virement bancaire » (module CPT-02).
    /// </summary>
    public class OrdreVirementViewModel : BaseViewModel
    {
        private readonly OrdreVirementService _virementService;
        private readonly OrdreVirementExportService _exportService;
        private readonly Dossier _dossier;

        public OrdreVirementViewModel(
            OrdreVirementService virementService,
            OrdreVirementExportService exportService,
            Dossier dossier)
        {
            if (virementService == null) throw new ArgumentNullException(nameof(virementService));
            if (exportService == null) throw new ArgumentNullException(nameof(exportService));
            if (dossier == null) throw new ArgumentNullException(nameof(dossier));

            _virementService = virementService;
            _exportService = exportService;
            _dossier = dossier;

            Title = "Ordres de virement";

            // Mois précédent par défaut
            var reference = DateTime.Today.AddMonths(-1);
            _annee = reference.Year;
            _moisSelectionne = MoisDisponibles.FirstOrDefault(m => m.Numero == reference.Month);

            BanquesDisponibles = new ObservableCollection<string> { "Toutes les banques" };
            _banqueSelectionnee = "Toutes les banques";

            ModesDisponibles = new List<string>
            {
                "Tous les modes",
                "Virements bancaires",
                "Chèques",
                "Espèces",
                "Mobile Money"
            };
            _modeSelectionne = "Virements bancaires"; // Par défaut, on s'intéresse aux virements

            GenererCommand = new RelayCommand(async () => await GenererAsync(), () => !IsBusy);
            ExporterExcelCommand = new RelayCommand(ExporterExcel, () => Ordre != null && !IsBusy);
            ExporterCsvCommand = new RelayCommand(ExporterCsv, () => Ordre != null && !IsBusy);
        }

        // ── Période et filtres ───────────────────────────────────────────────

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

        public ObservableCollection<string> BanquesDisponibles { get; }

        private string _banqueSelectionnee;
        public string BanqueSelectionnee
        {
            get { return _banqueSelectionnee; }
            set
            {
                if (SetProperty(ref _banqueSelectionnee, value))
                {
                    if (Ordre != null)
                    {
                        _ = GenererAsync();
                    }
                }
            }
        }

        public List<string> ModesDisponibles { get; }

        private string _modeSelectionne;
        public string ModeSelectionne
        {
            get { return _modeSelectionne; }
            set
            {
                if (SetProperty(ref _modeSelectionne, value))
                {
                    if (Ordre != null)
                    {
                        _ = GenererAsync();
                    }
                }
            }
        }

        // ── Données ──────────────────────────────────────────────────────────

        private OrdreVirement _ordre;
        public OrdreVirement Ordre
        {
            get { return _ordre; }
            private set
            {
                if (SetProperty(ref _ordre, value))
                {
                    OnPropertyChanged(nameof(AOrdre));
                    OnPropertyChanged(nameof(TotalNetGeneral));
                    OnPropertyChanged(nameof(TotalVirements));
                    OnPropertyChanged(nameof(NombreTotalSalaries));
                    OnPropertyChanged(nameof(NombreVirements));
                    OnPropertyChanged(nameof(NombreAnomalies));
                    OnPropertyChanged(nameof(AAnomalies));
                }
            }
        }

        public ObservableCollection<LigneOrdreVirement> Lignes { get; } =
            new ObservableCollection<LigneOrdreVirement>();

        public ObservableCollection<RecapModeReglement> Recapitulatifs { get; } =
            new ObservableCollection<RecapModeReglement>();

        public ObservableCollection<string> Anomalies { get; } =
            new ObservableCollection<string>();

        public bool AOrdre
        {
            get { return Ordre != null && Lignes.Count > 0; }
        }

        public decimal TotalNetGeneral
        {
            get { return Ordre != null ? Ordre.TotalNetGeneral : 0m; }
        }

        public decimal TotalVirements
        {
            get { return Ordre != null ? Ordre.TotalVirements : 0m; }
        }

        public int NombreTotalSalaries
        {
            get { return Ordre != null ? Ordre.NombreTotalSalaries : 0; }
        }

        public int NombreVirements
        {
            get { return Ordre != null ? Ordre.NombreVirements : 0; }
        }

        public int NombreAnomalies
        {
            get { return Ordre != null ? Ordre.NombreAnomalies : 0; }
        }

        public bool AAnomalies
        {
            get { return Ordre != null && Ordre.AAnomalies; }
        }

        private string _messageStatut;
        public string MessageStatut
        {
            get { return _messageStatut; }
            private set { SetProperty(ref _messageStatut, value); }
        }

        // ── Commandes ────────────────────────────────────────────────────────

        public ICommand GenererCommand { get; }
        public ICommand ExporterExcelCommand { get; }
        public ICommand ExporterCsvCommand { get; }

        public async Task GenererAsync()
        {
            if (MoisSelectionne == null) return;

            IsBusy = true;
            MessageStatut = "Génération de l'ordre de virement en cours...";

            try
            {
                // Mettre à jour la liste des banques réelles de la période
                var banquesReelles = await _virementService.ObtenirBanquesDistinctesAsync(Annee, MoisSelectionne.Numero);
                string selectionActuelle = BanqueSelectionnee;

                BanquesDisponibles.Clear();
                BanquesDisponibles.Add("Toutes les banques");
                foreach (var b in banquesReelles)
                {
                    BanquesDisponibles.Add(b);
                }

                if (BanquesDisponibles.Contains(selectionActuelle))
                    _banqueSelectionnee = selectionActuelle;
                else
                    _banqueSelectionnee = "Toutes les banques";
                OnPropertyChanged(nameof(BanqueSelectionnee));

                var res = await _virementService.ConstruireAsync(
                    _dossier, Annee, MoisSelectionne.Numero, BanqueSelectionnee, ModeSelectionne);

                Ordre = res;

                Lignes.Clear();
                foreach (var l in res.Lignes)
                {
                    Lignes.Add(l);
                }

                Recapitulatifs.Clear();
                foreach (var r in res.Recapitulatifs)
                {
                    Recapitulatifs.Add(r);
                }

                Anomalies.Clear();
                foreach (var a in res.Anomalies())
                {
                    Anomalies.Add(a);
                }

                OnPropertyChanged(nameof(AOrdre));

                if (Lignes.Count == 0)
                {
                    MessageStatut = string.Format(
                        "Aucun règlement trouvé pour {0} avec les filtres sélectionnés.",
                        PeriodeChoisieLibelle);
                }
                else
                {
                    MessageStatut = string.Format(
                        "{0} paiement(s) généré(s) pour {1} (Total : {2:N0} FCFA).",
                        Lignes.Count, PeriodeChoisieLibelle, TotalNetGeneral);
                }
            }
            catch (Exception ex)
            {
                MessageStatut = "Erreur lors de la génération : " + ex.Message;
            }
            finally
            {
                IsBusy = false;
            }
        }

        private void ExporterExcel()
        {
            if (Ordre == null) return;

            try
            {
                var dialog = new SaveFileDialog
                {
                    Title = "Exporter l'ordre de virement vers Excel",
                    Filter = "Classeur Excel (*.xlsx)|*.xlsx",
                    FileName = Ordre.NomFichierExcel
                };

                if (dialog.ShowDialog() == true)
                {
                    _exportService.ExporterExcel(Ordre, dialog.FileName);
                    MessageStatut = "Export Excel réussi : " + Path.GetFileName(dialog.FileName);

                    if (File.Exists(dialog.FileName))
                    {
                        Process.Start(new ProcessStartInfo(dialog.FileName) { UseShellExecute = true });
                    }
                }
            }
            catch (Exception ex)
            {
                MessageStatut = "Erreur lors de l'export Excel : " + ex.Message;
            }
        }

        private void ExporterCsv()
        {
            if (Ordre == null) return;

            try
            {
                var dialog = new SaveFileDialog
                {
                    Title = "Exporter le fichier de virement bancaire",
                    Filter = "Fichier CSV / Texte (*.csv)|*.csv|Fichier Texte (*.txt)|*.txt",
                    FileName = Ordre.NomFichierCsv
                };

                if (dialog.ShowDialog() == true)
                {
                    _exportService.ExporterCsv(Ordre, dialog.FileName);
                    MessageStatut = "Export fichier bancaire réussi : " + Path.GetFileName(dialog.FileName);

                    if (File.Exists(dialog.FileName))
                    {
                        Process.Start(new ProcessStartInfo(dialog.FileName) { UseShellExecute = true });
                    }
                }
            }
            catch (Exception ex)
            {
                MessageStatut = "Erreur lors de l'export fichier bancaire : " + ex.Message;
            }
        }
    }
}
