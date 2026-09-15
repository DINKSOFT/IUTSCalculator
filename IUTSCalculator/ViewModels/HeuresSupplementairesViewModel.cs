using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Data;
using System.Windows.Input;
using IUTSCalculator.Helpers;
using IUTSCalculator.Models;
using IUTSCalculator.Services;
using Microsoft.Win32;

namespace IUTSCalculator.ViewModels
{
    /// <summary>
    /// ViewModel pour l'écran de gestion et de valorisation des heures supplémentaires (module PAI-03).
    /// </summary>
    public class HeuresSupplementairesViewModel : BaseViewModel
    {
        private readonly HeureSupplementaireService _service;
        private readonly HeuresSupExportService _exportService;
        private readonly PeriodePaieService _periodePaieService;

        private int _annee;
        private MoisOption _moisSelectionne;
        private bool _estPeriodeCloturee;
        private string _rechercheTexte;
        private HeureSupplementaire _ligneSelectionnee;

        private decimal _totalHeuresMois;
        private decimal _totalMontantMois;
        private int _nombreSalariesConcernes;
        private decimal _tauxHoraireMoyen;

        public ObservableCollection<HeureSupplementaire> Lignes { get; } = new ObservableCollection<HeureSupplementaire>();

        private ICollectionView _vueFiltree;
        public ICollectionView VueFiltree => _vueFiltree;

        public IEnumerable<MoisOption> MoisDisponibles => MoisOption.Tous;
        public IEnumerable<int> AnneesDisponibles => Enumerable.Range(DateTime.Today.Year - 4, 6).ToList();

        public int Annee
        {
            get => _annee;
            set
            {
                if (SetProperty(ref _annee, value))
                {
                    _ = ChargerAsync();
                }
            }
        }

        public MoisOption MoisSelectionne
        {
            get => _moisSelectionne;
            set
            {
                if (SetProperty(ref _moisSelectionne, value))
                {
                    _ = ChargerAsync();
                }
            }
        }

        public bool EstPeriodeCloturee
        {
            get => _estPeriodeCloturee;
            set
            {
                if (SetProperty(ref _estPeriodeCloturee, value))
                {
                    OnPropertyChanged(nameof(PeriodeOuverte));
                    (EnregistrerCommand as RelayCommand)?.RaiseCanExecuteChanged();
                }
            }
        }

        public bool PeriodeOuverte => !_estPeriodeCloturee;

        public string RechercheTexte
        {
            get => _rechercheTexte;
            set
            {
                if (SetProperty(ref _rechercheTexte, value))
                {
                    _vueFiltree?.Refresh();
                }
            }
        }

        public HeureSupplementaire LigneSelectionnee
        {
            get => _ligneSelectionnee;
            set => SetProperty(ref _ligneSelectionnee, value);
        }

        public decimal TotalHeuresMois
        {
            get => _totalHeuresMois;
            set => SetProperty(ref _totalHeuresMois, value);
        }

        public decimal TotalMontantMois
        {
            get => _totalMontantMois;
            set => SetProperty(ref _totalMontantMois, value);
        }

        public int NombreSalariesConcernes
        {
            get => _nombreSalariesConcernes;
            set => SetProperty(ref _nombreSalariesConcernes, value);
        }

        public decimal TauxHoraireMoyen
        {
            get => _tauxHoraireMoyen;
            set => SetProperty(ref _tauxHoraireMoyen, value);
        }

        public ICommand ChargerCommand { get; }
        public ICommand EnregistrerCommand { get; }
        public ICommand ExporterExcelCommand { get; }
        public ICommand ReinitialiserCommand { get; }

        public HeuresSupplementairesViewModel(
            HeureSupplementaireService service,
            HeuresSupExportService exportService = null,
            PeriodePaieService periodePaieService = null)
        {
            _service = service ?? throw new ArgumentNullException(nameof(service));
            _exportService = exportService ?? new HeuresSupExportService();
            _periodePaieService = periodePaieService;

            Title = "Heures supplémentaires";

            var now = DateTime.Today;
            _annee = now.Year;
            _moisSelectionne = MoisOption.Tous.FirstOrDefault(m => m.Numero == now.Month) ?? MoisOption.Tous.First();

            _vueFiltree = CollectionViewSource.GetDefaultView(Lignes);
            _vueFiltree.Filter = FiltrerLigne;

            ChargerCommand = new RelayCommand(async () => await ChargerAsync(), () => !IsBusy);
            EnregistrerCommand = new RelayCommand(async () => await EnregistrerAsync(), () => !IsBusy && PeriodeOuverte);
            ExporterExcelCommand = new RelayCommand(async () => await ExporterExcelAsync(), () => !IsBusy && Lignes.Count > 0);
            ReinitialiserCommand = new RelayCommand(ReinitialiserSaisie, () => !IsBusy && PeriodeOuverte);

            _ = ChargerAsync();
        }

        private bool FiltrerLigne(object item)
        {
            if (string.IsNullOrWhiteSpace(_rechercheTexte)) return true;
            if (item is HeureSupplementaire hs)
            {
                string r = _rechercheTexte.Trim();
                return (hs.NomComplet != null && hs.NomComplet.IndexOf(r, StringComparison.OrdinalIgnoreCase) >= 0)
                    || (hs.Matricule != null && hs.Matricule.IndexOf(r, StringComparison.OrdinalIgnoreCase) >= 0)
                    || (hs.Commentaire != null && hs.Commentaire.IndexOf(r, StringComparison.OrdinalIgnoreCase) >= 0);
            }
            return true;
        }

        public async Task ChargerAsync()
        {
            if (MoisSelectionne == null) return;

            try
            {
                IsBusy = true;
                StatusMessage = $"Chargement des heures supplémentaires pour {MoisSelectionne.Nom} {Annee}...";

                // Vérifier si la période est clôturée
                if (_periodePaieService != null)
                {
                    EstPeriodeCloturee = await _periodePaieService.EstClotureeAsync(new DateTime(Annee, MoisSelectionne.Numero, 1));
                }
                else
                {
                    EstPeriodeCloturee = false;
                }

                var donnees = await _service.ChargerSaisieMoisAsync(Annee, MoisSelectionne.Numero);

                // Détacher les anciens abonnements
                foreach (var l in Lignes)
                    l.PropertyChanged -= Ligne_PropertyChanged;

                Lignes.Clear();
                foreach (var l in donnees)
                {
                    l.PropertyChanged += Ligne_PropertyChanged;
                    Lignes.Add(l);
                }

                RecalculerTotaux();

                StatusMessage = EstPeriodeCloturee
                    ? $"Période {MoisSelectionne.Nom} {Annee} CLÔTURÉE : saisie en lecture seule."
                    : $"{Lignes.Count} salarié(s) chargé(s) pour {MoisSelectionne.Nom} {Annee}.";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Erreur lors du chargement : {ex.Message}";
            }
            finally
            {
                IsBusy = false;
                (EnregistrerCommand as RelayCommand)?.RaiseCanExecuteChanged();
                (ExporterExcelCommand as RelayCommand)?.RaiseCanExecuteChanged();
            }
        }

        private void Ligne_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(HeureSupplementaire.MontantTotal) ||
                e.PropertyName == nameof(HeureSupplementaire.TotalHeures) ||
                e.PropertyName == nameof(HeureSupplementaire.Heures15) ||
                e.PropertyName == nameof(HeureSupplementaire.Heures30) ||
                e.PropertyName == nameof(HeureSupplementaire.Heures50) ||
                e.PropertyName == nameof(HeureSupplementaire.Heures100))
            {
                RecalculerTotaux();
            }
        }

        private void RecalculerTotaux()
        {
            decimal totalHeures = 0m;
            decimal totalMontant = 0m;
            int conc = 0;
            decimal sommeTaux = 0m;

            foreach (var l in Lignes)
            {
                totalHeures += l.TotalHeures;
                totalMontant += l.MontantTotal;
                sommeTaux += l.TauxHoraireBase;
                if (l.TotalHeures > 0m)
                    conc++;
            }

            TotalHeuresMois = totalHeures;
            TotalMontantMois = totalMontant;
            NombreSalariesConcernes = conc;
            TauxHoraireMoyen = Lignes.Count > 0 ? Math.Round(sommeTaux / Lignes.Count, 2) : 0m;
        }

        public async Task EnregistrerAsync()
        {
            if (EstPeriodeCloturee)
            {
                StatusMessage = "Impossible d'enregistrer : cette période de paie est clôturée.";
                return;
            }

            try
            {
                IsBusy = true;
                StatusMessage = "Enregistrement des heures supplémentaires en cours...";

                string utilisateur = UserSession.Instance.CurrentUser?.Username ?? Environment.UserName;
                int nb = await _service.EnregistrerGrilleAsync(Annee, MoisSelectionne.Numero, Lignes, utilisateur);

                StatusMessage = $"Enregistrement réussi : {nb} ligne(s) d'heures supplémentaires sauvegardée(s) pour {MoisSelectionne.Nom} {Annee}.";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Erreur lors de l'enregistrement : {ex.Message}";
            }
            finally
            {
                IsBusy = false;
            }
        }

        private void ReinitialiserSaisie()
        {
            if (EstPeriodeCloturee) return;

            foreach (var l in Lignes)
            {
                l.Heures15 = 0m;
                l.Heures30 = 0m;
                l.Heures50 = 0m;
                l.Heures100 = 0m;
                l.Commentaire = string.Empty;
            }
            RecalculerTotaux();
            StatusMessage = "Toutes les heures supplémentaires de la grille ont été remises à zéro.";
        }

        public async Task ExporterExcelAsync()
        {
            if (Lignes.Count == 0) return;

            var dialog = new SaveFileDialog
            {
                Filter = "Classeur Excel (*.xlsx)|*.xlsx",
                FileName = $"Heures_Supplementaires_{Annee}_{MoisSelectionne.Numero:D2}.xlsx",
                Title = "Exporter le récapitulatif des heures supplémentaires"
            };

            if (dialog.ShowDialog() == true)
            {
                try
                {
                    IsBusy = true;
                    StatusMessage = "Génération du classeur Excel en cours...";

                    var dossier = DossierSession.Instance.CurrentDossier;
                    await _exportService.ExporterExcelAsync(Lignes, dossier, Annee, MoisSelectionne.Numero, dialog.FileName);

                    StatusMessage = $"Fichier Excel exporté avec succès : {Path.GetFileName(dialog.FileName)}";
                }
                catch (Exception ex)
                {
                    StatusMessage = $"Erreur lors de l'export Excel : {ex.Message}";
                }
                finally
                {
                    IsBusy = false;
                }
            }
        }
    }
}
