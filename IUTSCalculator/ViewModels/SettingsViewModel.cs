using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using IUTSCalculator.Helpers;
using IUTSCalculator.Models;
using IUTSCalculator.Services;

namespace IUTSCalculator.ViewModels
{
    /// <summary>
    /// Écran « Paramètres » : préférences d'affichage (thème, langue, sauvegarde
    /// automatique, dossier d'export) et barème IUTS actuellement en vigueur.
    ///
    /// <b>Signalé le 16 septembre 2026, avec le branchement du barème daté sur le moteur
    /// (voir ETAT-DEVELOPPEMENT.md item 23) :</b> cet écran possédait son propre éditeur de
    /// tranches d'IUTS et de taux pour charges de famille, enregistré à part dans
    /// <see cref="AppSettings"/> (<c>TaxBrackets</c>/<c>FamilyCharges</c>) — un réglage que
    /// le moteur de calcul n'a jamais lu, ni avant ni après ce branchement, et dont les
    /// valeurs par défaut étaient elles-mêmes fausses (bornes 30 100/50 100/... au lieu de
    /// 30 000/50 000/... et taux mal proportionnés). Un utilisateur qui modifiait l'IUTS
    /// depuis cet écran n'avait donc jamais le moindre effet sur les bulletins.
    ///
    /// Reconstruit pour éditer le même barème que l'écran « Barèmes légaux » (module SOC-02,
    /// table <c>Baremes</c>/<c>BaremeTranches</c>/<c>BaremeChargesFamille</c>, via
    /// <see cref="BaremeService"/>) : concrètement, le barème actuellement APPLICABLE (celui
    /// dont la date d'effet la plus récente ne dépasse pas aujourd'hui). Pour un changement
    /// de loi avec une nouvelle date d'effet, l'écran « Barèmes légaux » reste le bon
    /// endroit (bouton « Dupliquer ») : modifier ici change rétroactivement tout bulletin
    /// déjà calculé avec ce barème, exactement comme le ferait la même modification depuis
    /// l'écran « Barèmes légaux ».
    /// </summary>
    public class SettingsViewModel : BaseViewModel
    {
        public bool CanEditData => UserSession.Instance.CanEdit;
        public bool IsAdmin => UserSession.Instance.IsAdmin;
        public bool IsReadOnly => UserSession.Instance.IsReadOnly;

        private readonly DataService _dataService;
        private readonly BaremeService _baremeService;
        private readonly RattrapageCnssService _rattrapageCnssService;

        private bool _isDarkTheme;
        private bool _isAutoSaveEnabled;
        private string _exportDirectory;
        private string _language;

        /// <summary>
        /// Événement déclenché lorsque les paramètres sont sauvegardés
        /// </summary>
        public event EventHandler SettingsSaved;

        public SettingsViewModel(DataService dataService, BaremeService baremeService, RattrapageCnssService rattrapageCnssService)
        {
            _dataService = dataService ?? throw new ArgumentNullException(nameof(dataService));
            _baremeService = baremeService ?? throw new ArgumentNullException(nameof(baremeService));
            _rattrapageCnssService = rattrapageCnssService ?? throw new ArgumentNullException(nameof(rattrapageCnssService));

            // Le titre alimente le message de la barre d'état et signale l'écran
            // actif dans le rail de navigation : sans lui, ni l'un ni l'autre.
            Title = "Paramètres";

            SaveSettingsCommand = new RelayCommand<object>(async _ => await SaveSettingsAsync(), _ => CanSaveSettings());
            ResetToDefaultsCommand = new RelayCommand<object>(_ => ReinitialiserBaremeVersLegal());
            ImportSettingsCommand = new RelayCommand<object>(async _ => await ImportSettingsAsync());
            ExportSettingsCommand = new RelayCommand<object>(async _ => await ExportSettingsAsync());
            AjouterTrancheCommand = new RelayCommand(AjouterTranche, () => FicheBareme != null);
            SupprimerTrancheCommand = new RelayCommand(SupprimerTranche, () => TrancheSelectionnee != null);
            AnalyserRattrapageCnssCommand = new RelayCommand<object>(async _ => await AnalyserRattrapageCnssAsync(), _ => !IsBusy);
            AppliquerRattrapageCnssCommand = new RelayCommand<object>(async _ => await AppliquerRattrapageCnssAsync(), _ => !IsBusy && PeutAppliquerRattrapageCnss);

            // Chargement initial des paramètres
            _ = LoadSettingsAsync();
        }

        /// <summary>
        /// Cet écran ne se rechargeait jamais après sa première ouverture, comme la
        /// plupart des écrans avant l'item 21. Rechargé ici sans risque pour une
        /// modification de tranche en cours : voir le garde « même barème » dans
        /// <see cref="ChargerBaremeAsync"/>.
        /// </summary>
        protected override void OnIsActiveChanged()
        {
            if (IsActive)
                _ = LoadSettingsAsync();
        }

        #region Propriétés — préférences

        public bool IsDarkTheme
        {
            get => _isDarkTheme;
            set
            {
                if (SetProperty(ref _isDarkTheme, value))
                {
                    ApplyTheme();
                }
            }
        }

        public bool IsAutoSaveEnabled
        {
            get => _isAutoSaveEnabled;
            set => SetProperty(ref _isAutoSaveEnabled, value);
        }

        public string Language
        {
            get => _language;
            set => SetProperty(ref _language, value);
        }

        public string ExportDirectory
        {
            get => _exportDirectory;
            set => SetProperty(ref _exportDirectory, value);
        }

        #endregion

        #region Propriétés — barème IUTS en vigueur

        private BaremeLegal _ficheBareme;

        /// <summary>
        /// Copie de travail du barème actuellement en vigueur. Une copie, comme sur
        /// l'écran « Barèmes légaux » : tant qu'on n'a pas cliqué sur « Enregistrer », la
        /// base reste intacte, y compris pour un bulletin en cours de calcul ailleurs.
        /// </summary>
        public BaremeLegal FicheBareme
        {
            get => _ficheBareme;
            private set
            {
                var precedente = _ficheBareme;

                if (SetProperty(ref _ficheBareme, value))
                {
                    Desabonner(precedente);
                    Abonner(value);

                    TrancheSelectionnee = null;
                    OnPropertyChanged(nameof(EstBaremeSysteme));
                    OnPropertyChanged(nameof(BaremeInfoLibelle));
                    RafraichirCommandes();
                }
            }
        }

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
                foreach (TrancheBareme tranche in e.OldItems)
                    tranche.PropertyChanged -= TrancheModifiee;

            if (e.NewItems != null)
                foreach (TrancheBareme tranche in e.NewItems)
                    tranche.PropertyChanged += TrancheModifiee;
        }

        private void TrancheModifiee(object sender, PropertyChangedEventArgs e)
        {
            // Seule la borne haute est saisie ; la borne basse en découle.
            if (e.PropertyName == nameof(TrancheBareme.BorneSuperieure))
                EditionTranches.Renumeroter(FicheBareme.Tranches);
        }

        private TrancheBareme _trancheSelectionnee;
        public TrancheBareme TrancheSelectionnee
        {
            get => _trancheSelectionnee;
            set
            {
                if (SetProperty(ref _trancheSelectionnee, value))
                    RafraichirCommandes();
            }
        }

        /// <summary>Le barème en vigueur est celui livré avec l'application.</summary>
        public bool EstBaremeSysteme => FicheBareme != null && FicheBareme.EstSysteme;

        /// <summary>
        /// Rappelle quel barème est en cours d'édition et met en garde contre une
        /// modification rétroactive — la même mise en garde que l'écran « Barèmes
        /// légaux », puisque c'est littéralement le même geste sur le même
        /// enregistrement.
        /// </summary>
        public string BaremeInfoLibelle
        {
            get
            {
                if (FicheBareme == null) return "Aucun barème enregistré.";

                return string.Format(
                    "Barème actuellement en vigueur : {0}. Le modifier ici change rétroactivement " +
                    "tout bulletin déjà calculé avec ce barème. Pour un changement de loi à une " +
                    "date d'effet future, utilisez plutôt l'écran « Barèmes légaux » (bouton " +
                    "« Dupliquer »), qui préserve les bulletins déjà émis.",
                    FicheBareme.Designation);
            }
        }

        #endregion

        #region Commandes

        public ICommand SaveSettingsCommand { get; }
        public ICommand ResetToDefaultsCommand { get; }
        public ICommand ImportSettingsCommand { get; }
        public ICommand ExportSettingsCommand { get; }
        public ICommand AjouterTrancheCommand { get; }
        public ICommand SupprimerTrancheCommand { get; }

        private void RafraichirCommandes()
        {
            foreach (var commande in new ICommand[] { AjouterTrancheCommand, SupprimerTrancheCommand })
            {
                var relay = commande as RelayCommand;
                if (relay != null) relay.RaiseCanExecuteChanged();
            }
        }

        #endregion

        #region Méthodes privées

        private async Task LoadSettingsAsync()
        {
            IsBusy = true;
            StatusMessage = "Chargement des paramètres...";

            try
            {
                var appSettings = await _dataService.LoadSettingsAsync();

                IsDarkTheme = appSettings.Theme == "Dark";
                IsAutoSaveEnabled = appSettings.AutoSave;
                Language = appSettings.Language;

                ExportDirectory = !string.IsNullOrWhiteSpace(appSettings.ExportDirectory)
                    ? appSettings.ExportDirectory
                    : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);

                await ChargerBaremeAsync();

                StatusMessage = "Paramètres chargés avec succès";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Erreur lors du chargement des paramètres: {ex.Message}";
            }
            finally
            {
                IsBusy = false;
            }
        }

        /// <summary>
        /// Recharge le barème applicable à aujourd'hui. Garde « même barème » : si le
        /// barème résolu est toujours le même enregistrement (même Id) que la fiche déjà
        /// ouverte, on ne la reclone pas — sans quoi revenir sur cet écran effacerait une
        /// tranche en cours de modification, comme cela arrivait sur « Barèmes légaux »
        /// avant sa propre correction (item 22).
        /// </summary>
        private async Task ChargerBaremeAsync()
        {
            var applicable = await _baremeService.BaremeApplicableAsync(DateTime.Today);

            bool memeBareme = FicheBareme != null && applicable != null && FicheBareme.Id == applicable.Id;
            if (!memeBareme)
                FicheBareme = applicable != null ? applicable.Clone() : null;
        }

        private async Task SaveSettingsAsync()
        {
            IsBusy = true;
            StatusMessage = "Enregistrement des paramètres...";

            try
            {
                if (FicheBareme != null)
                {
                    EditionTranches.Renumeroter(FicheBareme.Tranches);

                    var anomalies = FicheBareme.Anomalies();
                    if (anomalies.Count > 0)
                    {
                        StatusMessage = "Barème non enregistré — " + string.Join(" ", anomalies);
                        return;
                    }

                    if (await _baremeService.DateEffetExisteAsync(FicheBareme.DateEffet, FicheBareme.Id))
                    {
                        StatusMessage = string.Format(
                            "Un autre barème porte déjà la date d'effet du {0:dd/MM/yyyy}.",
                            FicheBareme.DateEffet);
                        return;
                    }

                    await _baremeService.EnregistrerAsync(FicheBareme);
                }

                // Préférences d'affichage : relues avant d'être réécrites pour ne pas
                // réinitialiser au passage des réglages que cet écran n'édite pas
                // (devise du bulletin, droit à congé, base de retenue d'absence) — un
                // enregistrement bâtissait jusqu'ici un AppSettings tout neuf, qui les
                // ramenait silencieusement à leurs valeurs par défaut à chaque clic sur
                // « Enregistrer », effaçant par exemple le choix « Afficher la devise »
                // fait depuis l'écran Bulletin.
                var appSettings = await _dataService.LoadSettingsAsync();
                appSettings.Theme = IsDarkTheme ? "Dark" : "Light";
                appSettings.Language = Language;
                appSettings.AutoSave = IsAutoSaveEnabled;
                appSettings.LastUpdated = DateTime.Now;
                appSettings.ExportDirectory = ExportDirectory;

                await _dataService.SaveSettingsAsync(appSettings);

                await ChargerBaremeAsync();

                StatusMessage = "Paramètres enregistrés avec succès";
                SettingsSaved?.Invoke(this, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                StatusMessage = $"Erreur lors de l'enregistrement des paramètres: {ex.Message}";
            }
            finally
            {
                IsBusy = false;
            }
        }

        private bool CanSaveSettings()
        {
            return !IsBusy;
        }

        /// <summary>
        /// Réinitialise la copie de travail du barème aux valeurs légales (celles du
        /// barème livré avec l'application, <see cref="BaremeService.BaremesInitiaux"/>) —
        /// sans enregistrer : contrairement à l'ancien bouton, qui écrasait aussitôt un
        /// réglage sans effet réel, celui-ci prépare maintenant une modification du
        /// vrai barème utilisé pour la paie. L'utilisateur garde la main pour vérifier
        /// avant de cliquer sur « Enregistrer ».
        /// </summary>
        private void ReinitialiserBaremeVersLegal()
        {
            if (FicheBareme == null) return;

            var legal = BaremeService.BaremesInitiaux()[0];

            FicheBareme.Tranches.Clear();
            foreach (var tranche in legal.Tranches)
                FicheBareme.Tranches.Add(tranche.Clone());

            FicheBareme.ChargesFamille.Clear();
            foreach (var charge in legal.ChargesFamille)
                FicheBareme.ChargesFamille.Add(charge.Clone());

            EditionTranches.Renumeroter(FicheBareme.Tranches);

            IsDarkTheme = false;
            IsAutoSaveEnabled = true;
            Language = "fr-FR";
            ExportDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);

            StatusMessage = "Tranches et charges de famille ramenées aux valeurs légales. " +
                             "Cliquez sur « Enregistrer » pour appliquer.";
        }

        private void AjouterTranche()
        {
            if (FicheBareme == null) return;

            var nouvelle = EditionTranches.Ajouter(FicheBareme.Tranches);

            TrancheSelectionnee = nouvelle;
            StatusMessage = "Tranche ajoutée : renseignez sa borne supérieure et son taux.";
        }

        private void SupprimerTranche()
        {
            if (FicheBareme == null || TrancheSelectionnee == null) return;

            EditionTranches.Supprimer(FicheBareme.Tranches, TrancheSelectionnee);

            TrancheSelectionnee = null;
            StatusMessage = "Tranche supprimée ; les bornes voisines ont été recollées.";
        }

        private void ApplyTheme()
        {
            try
            {
                var appResources = System.Windows.Application.Current.Resources;
                var themeUri = IsDarkTheme
                    ? new Uri("/Resources/DarkTheme.xaml", UriKind.Relative)
                    : new Uri("/Resources/LightTheme.xaml", UriKind.Relative);

                appResources.MergedDictionaries.Clear();
                appResources.MergedDictionaries.Add(new System.Windows.ResourceDictionary { Source = themeUri });
                appResources.MergedDictionaries.Add(new System.Windows.ResourceDictionary { Source = new Uri("/Resources/Styles.xaml", UriKind.Relative) });
            }
            catch (Exception ex)
            {
                StatusMessage = $"Erreur lors de l'application du thème: {ex.Message}";
            }
        }

        private async Task ImportSettingsAsync()
        {
            try
            {
                var dialog = new Microsoft.Win32.OpenFileDialog
                {
                    DefaultExt = ".json",
                    Filter = "Fichiers JSON (*.json)|*.json"
                };

                if (dialog.ShowDialog() == true)
                {
                    StatusMessage = "Importation des paramètres...";
                    IsBusy = true;

                    var jsonData = File.ReadAllText(dialog.FileName);
                    await _dataService.ImportAllDataAsync(jsonData);

                    await LoadSettingsAsync();

                    StatusMessage = "Paramètres importés avec succès";
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"Erreur lors de l'importation des paramètres: {ex.Message}";
            }
            finally
            {
                IsBusy = false;
            }
        }

        private async Task ExportSettingsAsync()
        {
            try
            {
                var dialog = new Microsoft.Win32.SaveFileDialog
                {
                    DefaultExt = ".json",
                    Filter = "Fichiers JSON (*.json)|*.json",
                    FileName = "IUTSCalculator_Settings.json"
                };

                if (dialog.ShowDialog() == true)
                {
                    StatusMessage = "Exportation des paramètres...";
                    IsBusy = true;

                    var jsonData = await _dataService.ExportAllDataAsync();
                    File.WriteAllText(dialog.FileName, jsonData);

                    StatusMessage = "Paramètres exportés avec succès";
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"Erreur lors de l'exportation des paramètres: {ex.Message}";
            }
            finally
            {
                IsBusy = false;
            }
        }

        #endregion

        #region Rattrapage CNSS — dette technique n°3 (voir ETAT-DEVELOPPEMENT.md §7)

        // Panneau temporaire, volontairement minimal : voir la documentation de
        // RattrapageCnssService pour le détail de ce qui est reconstitué et pourquoi.
        // « Analyser » ne fait que lire ; « Appliquer » n'est activable qu'après une
        // analyse ayant trouvé des lignes à rattraper, et redemande confirmation avant
        // d'écrire (le service dépose lui-même une copie de sauvegarde du fichier).

        private RapportRattrapageCnss _dernierRapportCnss;

        /// <summary>Vrai dès qu'une analyse a été lancée au moins une fois dans cette session.</summary>
        public bool HasRapportRattrapageCnss => _dernierRapportCnss != null;

        public int NombreLignesARattraperCnss => _dernierRapportCnss?.ARattraper.Count ?? 0;

        public int NombreLignesSuspectesCnss => _dernierRapportCnss?.Suspectes.Count ?? 0;

        public bool RattrapageCnssDejaApplique => _dernierRapportCnss?.Applique ?? false;

        /// <summary>
        /// N'autorise « Appliquer » qu'après une analyse ayant trouvé des lignes à
        /// rattraper, et une seule fois par analyse : relancer « Analyser » après une
        /// application (le rapport revient alors à zéro ligne à rattraper) redésactive
        /// le bouton, comme il se doit.
        /// </summary>
        public bool PeutAppliquerRattrapageCnss =>
            _dernierRapportCnss != null && _dernierRapportCnss.ARattraper.Count > 0 && !_dernierRapportCnss.Applique;

        private string _rapportRattrapageCnssTexte;

        /// <summary>Compte-rendu affiché à l'écran après « Analyser » ou « Appliquer ».</summary>
        public string RapportRattrapageCnssTexte
        {
            get => _rapportRattrapageCnssTexte;
            private set => SetProperty(ref _rapportRattrapageCnssTexte, value);
        }

        public ICommand AnalyserRattrapageCnssCommand { get; private set; }
        public ICommand AppliquerRattrapageCnssCommand { get; private set; }

        private async Task AnalyserRattrapageCnssAsync()
        {
            IsBusy = true;
            StatusMessage = "Analyse du rattrapage CNSS en cours...";

            try
            {
                _dernierRapportCnss = await _rattrapageCnssService.AnalyserAsync();
                RapportRattrapageCnssTexte = ConstruireResumeRattrapageCnss(_dernierRapportCnss);
                StatusMessage = "Analyse du rattrapage CNSS terminée.";
            }
            catch (Exception ex)
            {
                _dernierRapportCnss = null;
                RapportRattrapageCnssTexte = null;
                StatusMessage = $"Erreur lors de l'analyse du rattrapage CNSS : {ex.Message}";
            }
            finally
            {
                IsBusy = false;
                NotifierRattrapageCnss();
            }
        }

        private async Task AppliquerRattrapageCnssAsync()
        {
            if (!PeutAppliquerRattrapageCnss) return;

            var confirmation = MessageBox.Show(
                string.Format(
                    "Ceci va écrire en base la ventilation CNSS reconstituée pour {0} bulletin(s) déjà " +
                    "enregistré(s), après dépôt d'une copie de sauvegarde du fichier. Cette opération " +
                    "touche des données de paie déjà déclarées et ne peut pas être annulée depuis " +
                    "l'application (seule la copie de sauvegarde le permet).\n\nConfirmer l'application " +
                    "du rattrapage CNSS ?",
                    NombreLignesARattraperCnss),
                "Confirmer le rattrapage CNSS",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (confirmation != MessageBoxResult.Yes) return;

            IsBusy = true;
            StatusMessage = "Application du rattrapage CNSS en cours...";

            try
            {
                _dernierRapportCnss = await _rattrapageCnssService.AppliquerAsync();
                RapportRattrapageCnssTexte = ConstruireResumeRattrapageCnss(_dernierRapportCnss);
                StatusMessage = "Rattrapage CNSS appliqué avec succès.";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Erreur lors de l'application du rattrapage CNSS : {ex.Message}";
            }
            finally
            {
                IsBusy = false;
                NotifierRattrapageCnss();
            }
        }

        private void NotifierRattrapageCnss()
        {
            OnPropertyChanged(nameof(HasRapportRattrapageCnss));
            OnPropertyChanged(nameof(NombreLignesARattraperCnss));
            OnPropertyChanged(nameof(NombreLignesSuspectesCnss));
            OnPropertyChanged(nameof(RattrapageCnssDejaApplique));
            OnPropertyChanged(nameof(PeutAppliquerRattrapageCnss));
            RafraichirCommandes();
        }

        private static string ConstruireResumeRattrapageCnss(RapportRattrapageCnss rapport)
        {
            if (rapport == null) return null;

            var lignes = new List<string>();

            if (rapport.Applique)
            {
                lignes.Add(rapport.ARattraper.Count > 0
                    ? string.Format("{0} ligne(s) rattrapée(s) et écrite(s) en base.", rapport.ARattraper.Count)
                    : "Aucune ligne à rattraper : rien n'a été écrit.");

                if (!string.IsNullOrEmpty(rapport.CopieSauvegarde))
                    lignes.Add("Copie de sauvegarde déposée : " + rapport.CopieSauvegarde);
            }
            else
            {
                lignes.Add(rapport.ARattraper.Count > 0
                    ? string.Format("{0} ligne(s) à rattraper (analyse seule, rien n'a encore été écrit).", rapport.ARattraper.Count)
                    : "Aucune ligne à rattraper.");
            }

            if (rapport.Suspectes.Count > 0)
            {
                lignes.Add(string.Format(
                    "{0} ligne(s) suspecte(s), laissée(s) de côté pour examen manuel :",
                    rapport.Suspectes.Count));

                foreach (var ligne in rapport.Suspectes.Take(20))
                {
                    lignes.Add(string.Format(
                        "  • {0} ({1}), {2:dd/MM/yyyy} — {3}",
                        ligne.NomComplet, ligne.EmployeeId, ligne.CalculationDate, ligne.RaisonSuspicion));
                }

                if (rapport.Suspectes.Count > 20)
                    lignes.Add(string.Format("  … et {0} autre(s).", rapport.Suspectes.Count - 20));
            }

            return string.Join(Environment.NewLine, lignes);
        }

        #endregion
    }
}
