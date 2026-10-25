using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using IUTSCalculator.Helpers;
using IUTSCalculator.Models;
using IUTSCalculator.Services;
using Microsoft.Win32;

namespace IUTSCalculator.ViewModels
{
    /// <summary>
    /// ViewModel principal pour le module RH-05 Recrutement & Gestion des Candidatures.
    /// Gère les besoins de recrutement, le vivier des profils, le pipeline de sélection,
    /// les comptes rendus d'évaluation, la passerelle d'embauche SAL-01 et les exports.
    /// </summary>
    public class RecrutementViewModel : BaseViewModel
    {
        private readonly RecrutementService _recrutementService;
        private readonly RecrutementExportService _exportService;
        private readonly OrganigrammeService _organigrammeService;
        private readonly DataService _dataService;

        #region Propriétés & Collections

        private int _ongletActifIndex = 0;
        private TableauBordRecrutement _tableauBord = new TableauBordRecrutement();

        // Sélections
        private BesoinRecrutement _besoinSelectionne;
        private Candidat _candidatSelectionne;
        private Candidature _candidatureSelectionnee;
        private RecrutementEntretien _entretienSelectionne;

        // Filtres
        private string _filtreStatutBesoin = "Tous";
        private string _rechercheBesoin = string.Empty;
        private string _filtreEtapeCandidature = "Toutes";
        private string _filtreStatutCandidature = "Toutes";
        private string _rechercheCandidature = string.Empty;
        private string _filtreVivierActif = "Actifs";
        private string _rechercheCandidat = string.Empty;

        // Champs de saisie pour l'intégration SAL-01
        private decimal _salaireEmbaucheSaisi;
        private DateTime _dateEmbaucheSaisie = DateTime.Today;
        private string _typeContratEmbaucheSaisi = "CDI";
        private string _categorieEmbaucheSaisie = "Cadre Moyen";
        private string _serviceEmbaucheSaisi = string.Empty;
        private string _matriculeEmbaucheSaisi = string.Empty;
        private string _echelonEmbaucheSaisi = string.Empty;
        private string _conventionEmbaucheLibelle = string.Empty;
        private decimal _indemniteLogementSaisie;
        private decimal _indemniteTransportSaisie;
        private decimal _indemniteFonctionSaisie;
        private decimal _indemniteSujetionSaisie;

        /// <summary>Motifs de recrutement proposés dans la fiche du besoin (valeur, libellé).</summary>
        public List<KeyValuePair<MotifRecrutement, string>> MotifsRecrutement { get; } =
            Enum.GetValues(typeof(MotifRecrutement))
                .Cast<MotifRecrutement>()
                .Select(m => new KeyValuePair<MotifRecrutement, string>(m, RecrutementHelper.GetDescription(m)))
                .ToList();

        // Collections observables
        public ObservableCollection<BesoinRecrutement> Besoins { get; } = new ObservableCollection<BesoinRecrutement>();
        public ObservableCollection<BesoinRecrutement> BesoinsFiltres { get; } = new ObservableCollection<BesoinRecrutement>();

        public ObservableCollection<Candidat> Candidats { get; } = new ObservableCollection<Candidat>();
        public ObservableCollection<Candidat> CandidatsFiltres { get; } = new ObservableCollection<Candidat>();

        public ObservableCollection<Candidature> Candidatures { get; } = new ObservableCollection<Candidature>();
        public ObservableCollection<Candidature> CandidaturesFiltrees { get; } = new ObservableCollection<Candidature>();

        public ObservableCollection<RecrutementEntretien> Entretiens { get; } = new ObservableCollection<RecrutementEntretien>();

        // Référentiels externes (postes, salariés)
        public ObservableCollection<Poste> PostesDisponibles { get; } = new ObservableCollection<Poste>();
        public ObservableCollection<Employee> EmployesDisponibles { get; } = new ObservableCollection<Employee>();

        public int OngletActifIndex
        {
            get => _ongletActifIndex;
            set => SetProperty(ref _ongletActifIndex, value);
        }

        public TableauBordRecrutement TableauBord
        {
            get => _tableauBord;
            set => SetProperty(ref _tableauBord, value);
        }

        public BesoinRecrutement BesoinSelectionne
        {
            get => _besoinSelectionne;
            set
            {
                if (SetProperty(ref _besoinSelectionne, value))
                {
                    CommandManager.InvalidateRequerySuggested();
                }
            }
        }

        public Candidat CandidatSelectionne
        {
            get => _candidatSelectionne;
            set
            {
                if (SetProperty(ref _candidatSelectionne, value))
                {
                    CommandManager.InvalidateRequerySuggested();
                }
            }
        }

        public Candidature CandidatureSelectionnee
        {
            get => _candidatureSelectionnee;
            set
            {
                if (SetProperty(ref _candidatureSelectionnee, value))
                {
                    if (value != null)
                    {
                        PreRemplirConditionsEmbauche(value);
                        _ = ChargerEntretiensCandidatureAsync();
                    }
                    else
                    {
                        Entretiens.Clear();
                    }
                    CommandManager.InvalidateRequerySuggested();
                }
            }
        }

        public RecrutementEntretien EntretienSelectionne
        {
            get => _entretienSelectionne;
            set => SetProperty(ref _entretienSelectionne, value);
        }

        public string FiltreStatutBesoin
        {
            get => _filtreStatutBesoin;
            set
            {
                if (SetProperty(ref _filtreStatutBesoin, value))
                    AppliquerFiltreBesoins();
            }
        }

        public string RechercheBesoin
        {
            get => _rechercheBesoin;
            set
            {
                if (SetProperty(ref _rechercheBesoin, value))
                    AppliquerFiltreBesoins();
            }
        }

        public string FiltreEtapeCandidature
        {
            get => _filtreEtapeCandidature;
            set
            {
                if (SetProperty(ref _filtreEtapeCandidature, value))
                    AppliquerFiltreCandidatures();
            }
        }

        public string FiltreStatutCandidature
        {
            get => _filtreStatutCandidature;
            set
            {
                if (SetProperty(ref _filtreStatutCandidature, value))
                    AppliquerFiltreCandidatures();
            }
        }

        public string RechercheCandidature
        {
            get => _rechercheCandidature;
            set
            {
                if (SetProperty(ref _rechercheCandidature, value))
                    AppliquerFiltreCandidatures();
            }
        }

        public string FiltreVivierActif
        {
            get => _filtreVivierActif;
            set
            {
                if (SetProperty(ref _filtreVivierActif, value))
                    AppliquerFiltreCandidats();
            }
        }

        public string RechercheCandidat
        {
            get => _rechercheCandidat;
            set
            {
                if (SetProperty(ref _rechercheCandidat, value))
                    AppliquerFiltreCandidats();
            }
        }

        public decimal SalaireEmbaucheSaisi
        {
            get => _salaireEmbaucheSaisi;
            set => SetProperty(ref _salaireEmbaucheSaisi, value);
        }

        public DateTime DateEmbaucheSaisie
        {
            get => _dateEmbaucheSaisie;
            set => SetProperty(ref _dateEmbaucheSaisie, value);
        }

        public string TypeContratEmbaucheSaisi
        {
            get => _typeContratEmbaucheSaisi;
            set => SetProperty(ref _typeContratEmbaucheSaisi, value);
        }

        public string CategorieEmbaucheSaisie
        {
            get => _categorieEmbaucheSaisie;
            set => SetProperty(ref _categorieEmbaucheSaisie, value);
        }

        public string ServiceEmbaucheSaisi
        {
            get => _serviceEmbaucheSaisi;
            set => SetProperty(ref _serviceEmbaucheSaisi, value);
        }

        /// <summary>Matricule à attribuer : prochain matricule libre du dossier, modifiable.</summary>
        public string MatriculeEmbaucheSaisi
        {
            get => _matriculeEmbaucheSaisi;
            set => SetProperty(ref _matriculeEmbaucheSaisi, value);
        }

        /// <summary>Échelon : repris du poste RH-02 rattaché au besoin, modifiable.</summary>
        public string EchelonEmbaucheSaisi
        {
            get => _echelonEmbaucheSaisi;
            set => SetProperty(ref _echelonEmbaucheSaisi, value);
        }

        /// <summary>Convention collective du poste RH-02 (lecture seule, reprise telle quelle).</summary>
        public string ConventionEmbaucheLibelle
        {
            get => _conventionEmbaucheLibelle;
            set => SetProperty(ref _conventionEmbaucheLibelle, value);
        }

        public decimal IndemniteLogementSaisie
        {
            get => _indemniteLogementSaisie;
            set => SetProperty(ref _indemniteLogementSaisie, value);
        }

        public decimal IndemniteTransportSaisie
        {
            get => _indemniteTransportSaisie;
            set => SetProperty(ref _indemniteTransportSaisie, value);
        }

        public decimal IndemniteFonctionSaisie
        {
            get => _indemniteFonctionSaisie;
            set => SetProperty(ref _indemniteFonctionSaisie, value);
        }

        public decimal IndemniteSujetionSaisie
        {
            get => _indemniteSujetionSaisie;
            set => SetProperty(ref _indemniteSujetionSaisie, value);
        }

        #endregion

        #region Commandes

        public ICommand ChargerToutCommand { get; }
        public ICommand RafraichirCommand { get; }

        // Besoins
        public ICommand NouveauBesoinCommand { get; }
        public ICommand EnregistrerBesoinCommand { get; }
        public ICommand SupprimerBesoinCommand { get; }
        public ICommand OuvrirBesoinCommand { get; }
        public ICommand CloturerBesoinCommand { get; }

        // Candidats (Vivier)
        public ICommand NouveauCandidatCommand { get; }
        public ICommand EnregistrerCandidatCommand { get; }
        public ICommand SupprimerCandidatCommand { get; }
        public ICommand BasculerStatutVivierCommand { get; }
        public ICommand ImporterCvCommand { get; }
        public ICommand OuvrirCvCommand { get; }

        // Candidatures (Pipeline)
        public ICommand PostulerCandidatSelectionneCommand { get; }
        public ICommand AvancerEtapeCommand { get; }
        public ICommand RetenirCandidatureCommand { get; }
        public ICommand RejeterCandidatureCommand { get; }
        public ICommand SupprimerCandidatureCommand { get; }

        // Entretiens
        public ICommand NouvelEntretienCommand { get; }
        public ICommand EnregistrerEntretienCommand { get; }
        public ICommand SupprimerEntretienCommand { get; }

        // Passerelle d'Embauche SAL-01
        public ICommand IntegrerEnSalarieCommand { get; }

        // Exports
        public ICommand ExporterLettreEmbauchePdfCommand { get; }
        public ICommand ExporterBilanExcelCommand { get; }

        #endregion

        public RecrutementViewModel(
            RecrutementService recrutementService,
            RecrutementExportService exportService,
            OrganigrammeService organigrammeService,
            DataService dataService)
        {
            _recrutementService = recrutementService ?? throw new ArgumentNullException(nameof(recrutementService));
            _exportService = exportService ?? throw new ArgumentNullException(nameof(exportService));
            _organigrammeService = organigrammeService ?? throw new ArgumentNullException(nameof(organigrammeService));
            _dataService = dataService ?? throw new ArgumentNullException(nameof(dataService));

            Title = "Recrutement & Candidatures";

            // Initialisation des commandes
            ChargerToutCommand = new RelayCommand(async () => await ChargerToutesDonneesAsync());
            RafraichirCommand = new RelayCommand(async () => await ChargerToutesDonneesAsync());

            NouveauBesoinCommand = new RelayCommand(() => CreerNouveauBesoin());
            EnregistrerBesoinCommand = new RelayCommand(async () => await EnregistrerBesoinAsync(), () => BesoinSelectionne != null);
            SupprimerBesoinCommand = new RelayCommand(async () => await SupprimerBesoinAsync(), () => BesoinSelectionne != null);
            OuvrirBesoinCommand = new RelayCommand(async () => await ChangerStatutBesoinAsync(StatutBesoinRecrutement.Ouvert), () => BesoinSelectionne != null && BesoinSelectionne.Statut != StatutBesoinRecrutement.Ouvert);
            CloturerBesoinCommand = new RelayCommand(async () => await ChangerStatutBesoinAsync(StatutBesoinRecrutement.Pourvu), () => BesoinSelectionne != null && BesoinSelectionne.Statut != StatutBesoinRecrutement.Pourvu);

            NouveauCandidatCommand = new RelayCommand(() => CreerNouveauCandidat());
            EnregistrerCandidatCommand = new RelayCommand(async () => await EnregistrerCandidatAsync(), () => CandidatSelectionne != null);
            SupprimerCandidatCommand = new RelayCommand(async () => await SupprimerCandidatAsync(), () => CandidatSelectionne != null);
            BasculerStatutVivierCommand = new RelayCommand(async () => await BasculerStatutVivierAsync(), () => CandidatSelectionne != null);
            ImporterCvCommand = new RelayCommand(async () => await ImporterCvAsync(), () => CandidatSelectionne != null);
            OuvrirCvCommand = new RelayCommand(() => OuvrirCv(), () => !string.IsNullOrWhiteSpace(CandidatSelectionne?.CvChemin));

            PostulerCandidatSelectionneCommand = new RelayCommand(async () => await PostulerAsync(), () => CandidatSelectionne != null && BesoinSelectionne != null);
            AvancerEtapeCommand = new RelayCommand<object>(async param => await AvancerEtapeAsync(param), _ => CandidatureSelectionnee != null && !CandidatureSelectionnee.EstEmbauchee);
            RetenirCandidatureCommand = new RelayCommand(async () => await ChangerStatutCandidatureAsync(StatutCandidature.Retenue), () => CandidatureSelectionnee != null && !CandidatureSelectionnee.EstEmbauchee);
            RejeterCandidatureCommand = new RelayCommand(async () => await ChangerStatutCandidatureAsync(StatutCandidature.Rejetee), () => CandidatureSelectionnee != null && !CandidatureSelectionnee.EstEmbauchee);
            SupprimerCandidatureCommand = new RelayCommand(async () => await SupprimerCandidatureAsync(), () => CandidatureSelectionnee != null && !CandidatureSelectionnee.EstEmbauchee);

            NouvelEntretienCommand = new RelayCommand(() => CreerNouvelEntretien(), () => CandidatureSelectionnee != null && !CandidatureSelectionnee.EstEmbauchee);
            EnregistrerEntretienCommand = new RelayCommand(async () => await EnregistrerEntretienAsync(), () => EntretienSelectionne != null);
            SupprimerEntretienCommand = new RelayCommand(async () => await SupprimerEntretienAsync(), () => EntretienSelectionne != null);

            IntegrerEnSalarieCommand = new RelayCommand(async () => await IntegrerEnSalarieAsync(), () => CandidatureSelectionnee != null && !CandidatureSelectionnee.EstEmbauchee);

            ExporterLettreEmbauchePdfCommand = new RelayCommand(() => ExporterLettreEmbauchePdf(), () => CandidatureSelectionnee != null);
            ExporterBilanExcelCommand = new RelayCommand(() => ExporterBilanExcel());

            // Chargement initial
            _ = ChargerToutesDonneesAsync();
        }

        #region Méthodes de Chargement

        public async Task ChargerToutesDonneesAsync()
        {
            try
            {
                IsBusy = true;

                // 0. Référentiels externes (postes et salariés) d'abord : la sélection d'une
                //    candidature pré-remplit les conditions d'embauche à partir du poste RH-02.
                var postes = await _organigrammeService.ChargerPostesAsync();
                PostesDisponibles.Clear();
                foreach (var p in postes) PostesDisponibles.Add(p);

                var employes = await _dataService.LoadEmployeesAsync();
                EmployesDisponibles.Clear();
                foreach (var e in employes) EmployesDisponibles.Add(e);

                // 1. Besoins
                var listeBesoins = await _recrutementService.ObtenirBesoinsAsync();
                Besoins.Clear();
                foreach (var b in listeBesoins) Besoins.Add(b);
                AppliquerFiltreBesoins();

                // 2. Candidats
                var listeCandidats = await _recrutementService.ObtenirCandidatsAsync();
                Candidats.Clear();
                foreach (var c in listeCandidats) Candidats.Add(c);
                AppliquerFiltreCandidats();

                // 3. Candidatures
                var listeCandidatures = await _recrutementService.ObtenirCandidaturesAsync();
                Candidatures.Clear();
                foreach (var cand in listeCandidatures) Candidatures.Add(cand);
                AppliquerFiltreCandidatures();

                // 4. Tableau de bord
                TableauBord = await _recrutementService.ObtenirTableauBordAsync();

                // Restaurer ou initialiser les sélections
                if (BesoinSelectionne == null && BesoinsFiltres.Count > 0)
                    BesoinSelectionne = BesoinsFiltres[0];

                if (CandidatSelectionne == null && CandidatsFiltres.Count > 0)
                    CandidatSelectionne = CandidatsFiltres[0];

                if (CandidatureSelectionnee == null && CandidaturesFiltrees.Count > 0)
                    CandidatureSelectionnee = CandidaturesFiltrees[0];
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erreur lors du chargement des données de recrutement : {ex.Message}",
                    "Erreur de chargement", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                IsBusy = false;
            }
        }

        private async Task ChargerEntretiensCandidatureAsync()
        {
            if (CandidatureSelectionnee == null)
            {
                Entretiens.Clear();
                return;
            }

            try
            {
                var liste = await _recrutementService.ObtenirEntretiensAsync(CandidatureSelectionnee.Id);
                Entretiens.Clear();
                foreach (var e in liste) Entretiens.Add(e);
                EntretienSelectionne = Entretiens.FirstOrDefault();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erreur lors du chargement des entretiens : {ex.Message}", "Erreur", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        #endregion

        #region Filtrages

        private void AppliquerFiltreBesoins()
        {
            BesoinsFiltres.Clear();
            var query = Besoins.AsEnumerable();

            if (FiltreStatutBesoin != "Tous")
            {
                if (Enum.TryParse<StatutBesoinRecrutement>(FiltreStatutBesoin, out var statut))
                {
                    query = query.Where(b => b.Statut == statut);
                }
            }

            if (!string.IsNullOrWhiteSpace(RechercheBesoin))
            {
                string r = RechercheBesoin.Trim().ToLowerInvariant();
                query = query.Where(b => (b.Reference != null && b.Reference.ToLowerInvariant().Contains(r))
                                      || (b.IntitulePoste != null && b.IntitulePoste.ToLowerInvariant().Contains(r))
                                      || (b.Departement != null && b.Departement.ToLowerInvariant().Contains(r)));
            }

            foreach (var item in query)
                BesoinsFiltres.Add(item);

            if (BesoinSelectionne == null || !BesoinsFiltres.Contains(BesoinSelectionne))
                BesoinSelectionne = BesoinsFiltres.FirstOrDefault();
        }

        private void AppliquerFiltreCandidats()
        {
            CandidatsFiltres.Clear();
            var query = Candidats.AsEnumerable();

            if (FiltreVivierActif == "Actifs")
                query = query.Where(c => c.VivierActif);
            else if (FiltreVivierActif == "Archivés")
                query = query.Where(c => !c.VivierActif);

            if (!string.IsNullOrWhiteSpace(RechercheCandidat))
            {
                string r = RechercheCandidat.Trim().ToLowerInvariant();
                query = query.Where(c => (c.Nom != null && c.Nom.ToLowerInvariant().Contains(r))
                                      || (c.Prenoms != null && c.Prenoms.ToLowerInvariant().Contains(r))
                                      || (c.Email != null && c.Email.ToLowerInvariant().Contains(r))
                                      || (c.CompetencesCles != null && c.CompetencesCles.ToLowerInvariant().Contains(r)));
            }

            foreach (var item in query)
                CandidatsFiltres.Add(item);

            if (CandidatSelectionne == null || !CandidatsFiltres.Contains(CandidatSelectionne))
                CandidatSelectionne = CandidatsFiltres.FirstOrDefault();
        }

        private void AppliquerFiltreCandidatures()
        {
            CandidaturesFiltrees.Clear();
            var query = Candidatures.AsEnumerable();

            if (FiltreEtapeCandidature != "Toutes")
            {
                if (Enum.TryParse<EtapeRecrutement>(FiltreEtapeCandidature, out var etape))
                    query = query.Where(c => c.EtapeActuelle == etape);
            }

            if (FiltreStatutCandidature != "Toutes")
            {
                if (Enum.TryParse<StatutCandidature>(FiltreStatutCandidature, out var statut))
                    query = query.Where(c => c.Statut == statut);
            }

            if (!string.IsNullOrWhiteSpace(RechercheCandidature))
            {
                string r = RechercheCandidature.Trim().ToLowerInvariant();
                query = query.Where(c => (c.CandidatNomComplet != null && c.CandidatNomComplet.ToLowerInvariant().Contains(r))
                                      || (c.BesoinIntitulePoste != null && c.BesoinIntitulePoste.ToLowerInvariant().Contains(r))
                                      || (c.BesoinReference != null && c.BesoinReference.ToLowerInvariant().Contains(r)));
            }

            foreach (var item in query)
                CandidaturesFiltrees.Add(item);

            if (CandidatureSelectionnee == null || !CandidaturesFiltrees.Contains(CandidatureSelectionnee))
                CandidatureSelectionnee = CandidaturesFiltrees.FirstOrDefault();
        }

        #endregion

        #region Actions Besoins

        private void CreerNouveauBesoin()
        {
            var nouveau = new BesoinRecrutement
            {
                Reference = $"REC-{DateTime.Today.Year}-NOUV",
                IntitulePoste = "Nouveau Poste",
                Departement = "Direction Générale",
                DateExpression = DateTime.Today,
                DateEcheanceSouhaitee = DateTime.Today.AddDays(30),
                NombrePostes = 1,
                TypeContratVise = "CDI",
                RemunerationProposeeMin = 300000m,
                RemunerationProposeeMax = 450000m,
                Statut = StatutBesoinRecrutement.Brouillon
            };

            // Le DataGrid de la grille des besoins est lié à BesoinSelectionne en TwoWay via
            // SelectedItem : sélectionner un objet qui n'est pas (encore) dans BesoinsFiltres
            // est rejeté par le DataGrid, qui repousse aussitôt l'ancienne sélection (ou null)
            // à travers la liaison — c'est ce qui faisait que le bouton semblait ne rien faire.
            // On réinitialise donc les filtres pour être sûr que le nouveau brouillon soit
            // visible, on l'ajoute d'abord à la liste, puis seulement ensuite on le sélectionne.
            FiltreStatutBesoin = "Tous";
            RechercheBesoin = string.Empty;

            Besoins.Insert(0, nouveau);
            AppliquerFiltreBesoins();
            BesoinSelectionne = nouveau;
        }

        private async Task EnregistrerBesoinAsync()
        {
            if (BesoinSelectionne == null) return;
            try
            {
                await _recrutementService.EnregistrerBesoinAsync(BesoinSelectionne);
                MessageBox.Show("Besoin de recrutement enregistré avec succès.", "Succès", MessageBoxButton.OK, MessageBoxImage.Information);
                await ChargerToutesDonneesAsync();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erreur lors de l'enregistrement du besoin : {ex.Message}", "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async Task SupprimerBesoinAsync()
        {
            if (BesoinSelectionne == null) return;

            var rep = MessageBox.Show(
                $"Êtes-vous certain de vouloir supprimer le besoin '{BesoinSelectionne.Reference} - {BesoinSelectionne.IntitulePoste}' ?\n" +
                "Cette action supprimera également toutes les candidatures et entretiens associés non embauchés.",
                "Confirmation de suppression",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (rep != MessageBoxResult.Yes) return;

            try
            {
                await _recrutementService.SupprimerBesoinAsync(BesoinSelectionne.Id);
                MessageBox.Show("Besoin de recrutement supprimé.", "Succès", MessageBoxButton.OK, MessageBoxImage.Information);
                await ChargerToutesDonneesAsync();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erreur : {ex.Message}", "Suppression impossible", MessageBoxButton.OK, MessageBoxImage.Stop);
            }
        }

        private async Task ChangerStatutBesoinAsync(StatutBesoinRecrutement nouveauStatut)
        {
            if (BesoinSelectionne == null) return;
            try
            {
                await _recrutementService.ChangerStatutBesoinAsync(BesoinSelectionne.Id, nouveauStatut);
                BesoinSelectionne.Statut = nouveauStatut;
                await ChargerToutesDonneesAsync();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erreur : {ex.Message}", "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        #endregion

        #region Actions Vivier des Candidats

        private void CreerNouveauCandidat()
        {
            var nouveau = new Candidat
            {
                Nom = "NOM",
                Prenoms = "Prénoms",
                VivierActif = true,
                DateCreation = DateTime.Now
            };

            // Même cause que pour CreerNouveauBesoin : sans ajout préalable à CandidatsFiltres,
            // le DataGrid du vivier (SelectedItem TwoWay) rejette la sélection d'un objet
            // absent de sa liste et la binding repousse l'ancienne valeur vers le ViewModel.
            FiltreVivierActif = "Tous";
            RechercheCandidat = string.Empty;

            Candidats.Insert(0, nouveau);
            AppliquerFiltreCandidats();
            CandidatSelectionne = nouveau;
        }

        private async Task EnregistrerCandidatAsync()
        {
            if (CandidatSelectionne == null) return;
            try
            {
                await _recrutementService.EnregistrerCandidatAsync(CandidatSelectionne);
                MessageBox.Show("Profil candidat enregistré avec succès dans le vivier.", "Succès", MessageBoxButton.OK, MessageBoxImage.Information);
                await ChargerToutesDonneesAsync();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erreur lors de l'enregistrement du candidat : {ex.Message}", "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async Task SupprimerCandidatAsync()
        {
            if (CandidatSelectionne == null) return;

            var rep = MessageBox.Show(
                $"Supprimer définitivement le profil de '{CandidatSelectionne.NomComplet}' du vivier ?",
                "Confirmation", MessageBoxButton.YesNo, MessageBoxImage.Warning);

            if (rep != MessageBoxResult.Yes) return;

            try
            {
                await _recrutementService.SupprimerCandidatAsync(CandidatSelectionne.Id);
                MessageBox.Show("Profil candidat supprimé.", "Succès", MessageBoxButton.OK, MessageBoxImage.Information);
                await ChargerToutesDonneesAsync();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erreur : {ex.Message}", "Suppression refusée", MessageBoxButton.OK, MessageBoxImage.Stop);
            }
        }

        private async Task BasculerStatutVivierAsync()
        {
            if (CandidatSelectionne == null) return;
            try
            {
                CandidatSelectionne.VivierActif = !CandidatSelectionne.VivierActif;
                await _recrutementService.EnregistrerCandidatAsync(CandidatSelectionne);
                AppliquerFiltreCandidats();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erreur : {ex.Message}", "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async Task ImporterCvAsync()
        {
            if (CandidatSelectionne == null) return;

            var dialog = new OpenFileDialog
            {
                Filter = "Documents CV (*.pdf;*.doc;*.docx)|*.pdf;*.doc;*.docx",
                Title = "Importer le CV du candidat"
            };
            if (dialog.ShowDialog() != true) return;

            try
            {
                _recrutementService.ImporterCvCandidat(CandidatSelectionne, dialog.FileName);
                await _recrutementService.EnregistrerCandidatAsync(CandidatSelectionne);
                MessageBox.Show("CV importé et associé au profil avec succès.", "Succès", MessageBoxButton.OK, MessageBoxImage.Information);
                CommandManager.InvalidateRequerySuggested();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erreur lors de l'import du CV : {ex.Message}", "Import impossible", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void OuvrirCv()
        {
            if (string.IsNullOrWhiteSpace(CandidatSelectionne?.CvChemin)) return;

            if (!File.Exists(CandidatSelectionne.CvChemin))
            {
                MessageBox.Show("Le fichier de CV associé est introuvable sur le disque (il a peut-être été déplacé ou supprimé).",
                    "Fichier introuvable", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(CandidatSelectionne.CvChemin) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Impossible d'ouvrir le CV : {ex.Message}", "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        #endregion

        #region Actions Pipeline & Candidatures

        private async Task PostulerAsync()
        {
            if (CandidatSelectionne == null || BesoinSelectionne == null)
            {
                MessageBox.Show("Veuillez sélectionner à la fois un candidat du vivier et un besoin de recrutement.", "Sélection requise", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            try
            {
                int candId = await _recrutementService.PostulerAsync(
                    BesoinSelectionne.Id,
                    CandidatSelectionne.Id,
                    CanalSourceCandidature.CandidatureSpontanee);

                MessageBox.Show($"Candidature enregistrée avec succès dans le pipeline pour le poste {BesoinSelectionne.IntitulePoste} !", "Succès", MessageBoxButton.OK, MessageBoxImage.Information);
                await ChargerToutesDonneesAsync();
                OngletActifIndex = 1; // Basculer vers l'onglet Pipeline
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erreur : {ex.Message}", "Postulation impossible", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private async Task AvancerEtapeAsync(object param)
        {
            if (CandidatureSelectionnee == null) return;

            EtapeRecrutement nouvelleEtape;
            if (param is EtapeRecrutement etapeEnum)
            {
                nouvelleEtape = etapeEnum;
            }
            else if (int.TryParse(param?.ToString(), out int etapeInt))
            {
                nouvelleEtape = (EtapeRecrutement)etapeInt;
            }
            else
            {
                // Avancer d'un cran par défaut
                int etapeActuelle = (int)CandidatureSelectionnee.EtapeActuelle;
                nouvelleEtape = (EtapeRecrutement)Math.Min(7, etapeActuelle + 1);
            }

            try
            {
                await _recrutementService.AvancerEtapeCandidatureAsync(CandidatureSelectionnee.Id, nouvelleEtape);
                CandidatureSelectionnee.EtapeActuelle = nouvelleEtape;
                await ChargerToutesDonneesAsync();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erreur : {ex.Message}", "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async Task ChangerStatutCandidatureAsync(StatutCandidature statut)
        {
            if (CandidatureSelectionnee == null) return;
            try
            {
                await _recrutementService.ChangerStatutCandidatureAsync(CandidatureSelectionnee.Id, statut);
                CandidatureSelectionnee.Statut = statut;
                await ChargerToutesDonneesAsync();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erreur : {ex.Message}", "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async Task SupprimerCandidatureAsync()
        {
            if (CandidatureSelectionnee == null) return;

            var rep = MessageBox.Show(
                $"Supprimer la candidature de '{CandidatureSelectionnee.CandidatNomComplet}' pour le poste '{CandidatureSelectionnee.BesoinIntitulePoste}' ?",
                "Confirmation", MessageBoxButton.YesNo, MessageBoxImage.Warning);

            if (rep != MessageBoxResult.Yes) return;

            try
            {
                await _recrutementService.SupprimerCandidatureAsync(CandidatureSelectionnee.Id);
                MessageBox.Show("Candidature supprimée.", "Succès", MessageBoxButton.OK, MessageBoxImage.Information);
                await ChargerToutesDonneesAsync();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erreur : {ex.Message}", "Suppression impossible", MessageBoxButton.OK, MessageBoxImage.Stop);
            }
        }

        #endregion

        #region Actions Entretiens

        private void CreerNouvelEntretien()
        {
            if (CandidatureSelectionnee == null) return;

            var employeConnecte = EmployesDisponibles.FirstOrDefault();

            EntretienSelectionne = new RecrutementEntretien
            {
                CandidatureId = CandidatureSelectionnee.Id,
                Etape = CandidatureSelectionnee.EtapeActuelle,
                DateEntretien = DateTime.Today,
                EvaluateurNom = employeConnecte != null ? $"{employeConnecte.LastName} {employeConnecte.FirstName}" : "Responsable Recrutement",
                EvaluateurMatricule = employeConnecte?.EmployeeId,
                NoteGlobale = 15.0,
                Avis = AvisRecruteur.Favorable,
                PointsForts = "Bonne aisance relationnelle, compétences techniques avérées.",
                AxesAmelioration = "Approfondir la connaissance des procédures internes.",
                Synthese = "Candidat très prometteur, recommandé pour l'étape suivante."
            };
        }

        private async Task EnregistrerEntretienAsync()
        {
            if (EntretienSelectionne == null) return;
            try
            {
                await _recrutementService.EnregistrerEntretienAsync(EntretienSelectionne);
                MessageBox.Show("Compte rendu d'entretien enregistré avec succès.", "Succès", MessageBoxButton.OK, MessageBoxImage.Information);
                await ChargerEntretiensCandidatureAsync();
                await ChargerToutesDonneesAsync();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erreur lors de l'enregistrement de l'entretien : {ex.Message}", "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async Task SupprimerEntretienAsync()
        {
            if (EntretienSelectionne == null) return;

            var rep = MessageBox.Show(
                $"Supprimer ce compte rendu d'entretien du {EntretienSelectionne.DateEntretien:dd/MM/yyyy} ?",
                "Confirmation", MessageBoxButton.YesNo, MessageBoxImage.Question);

            if (rep != MessageBoxResult.Yes) return;

            try
            {
                await _recrutementService.SupprimerEntretienAsync(EntretienSelectionne.Id);
                await ChargerEntretiensCandidatureAsync();
                await ChargerToutesDonneesAsync();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erreur : {ex.Message}", "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        #endregion

        #region Passerelle d'Embauche SAL-01

        /// <summary>
        /// Pré-remplit les conditions d'embauche à partir de la candidature, de l'offre (besoin)
        /// et du poste RH-02 rattaché : type de contrat de l'offre, catégorie, échelon et convention
        /// du poste, indemnités proposées par l'offre, et prochain matricule libre du dossier.
        /// Le type de contrat était figé sur « CDI », quel que soit celui de l'offre.
        /// </summary>
        private void PreRemplirConditionsEmbauche(Candidature candidature)
        {
            var besoin = Besoins.FirstOrDefault(b => b.Id == candidature.BesoinId);
            int? posteId = besoin != null ? besoin.PosteId : candidature.BesoinPosteId;
            var poste = posteId.HasValue ? PostesDisponibles.FirstOrDefault(p => p.Id == posteId.Value) : null;

            SalaireEmbaucheSaisi = candidature.SalaireNegocie ?? (candidature.PretentionSalariale ?? 350000m);
            DateEmbaucheSaisie = candidature.DateEmbauche ?? DateTime.Today;
            ServiceEmbaucheSaisi = candidature.BesoinDepartement;

            TypeContratEmbaucheSaisi = PremiereValeur(candidature.TypeContratEmbauche, besoin?.TypeContratVise, "CDI");
            CategorieEmbaucheSaisie = PremiereValeur(poste?.CategorieConventionnelle, null, "Cadre Moyen");
            EchelonEmbaucheSaisi = poste?.EchelonConventionnel ?? string.Empty;
            ConventionEmbaucheLibelle = poste != null && poste.ConventionId.HasValue
                ? PremiereValeur(poste.ConventionLibelle, null, "Convention n° " + poste.ConventionId.Value)
                : "Aucune (poste sans convention)";

            IndemniteLogementSaisie = besoin?.IndemniteLogement ?? 0m;
            IndemniteTransportSaisie = besoin?.IndemniteTransport ?? 0m;
            IndemniteFonctionSaisie = besoin?.IndemniteFonction ?? 0m;
            IndemniteSujetionSaisie = besoin?.IndemniteSujetion ?? 0m;

            if (candidature.EstEmbauchee)
            {
                MatriculeEmbaucheSaisi = candidature.SalarieCreeMatricule ?? string.Empty;

                // Embauche déjà faite : afficher les conditions réellement portées par la fiche salarié.
                var salarie = EmployesDisponibles.FirstOrDefault(e => string.Equals(e.EmployeeId, candidature.SalarieCreeMatricule, StringComparison.OrdinalIgnoreCase));
                if (salarie != null)
                {
                    ServiceEmbaucheSaisi = salarie.Service;
                    CategorieEmbaucheSaisie = salarie.Category;
                    EchelonEmbaucheSaisi = salarie.Echelon ?? string.Empty;
                    IndemniteLogementSaisie = salarie.HousingAllowance;
                    IndemniteTransportSaisie = salarie.TransportAllowance;
                    IndemniteFonctionSaisie = salarie.FunctionAllowance;
                    IndemniteSujetionSaisie = salarie.SujetionAllowance;
                }
            }
            else
            {
                MatriculeEmbaucheSaisi = string.Empty;
                _ = ProposerMatriculeAsync(candidature);
            }
        }

        private async Task ProposerMatriculeAsync(Candidature candidature)
        {
            try
            {
                string proposition = await _recrutementService.ProposerProchainMatriculeAsync();
                // La sélection a pu changer pendant la lecture, ou l'utilisateur a déjà saisi un matricule.
                if (ReferenceEquals(candidature, CandidatureSelectionnee) && string.IsNullOrWhiteSpace(MatriculeEmbaucheSaisi))
                    MatriculeEmbaucheSaisi = proposition;
            }
            catch (Exception ex)
            {
                StatusMessage = $"Proposition de matricule impossible : {ex.Message}";
            }
        }

        private static string PremiereValeur(string valeur, string repli, string defaut)
        {
            if (!string.IsNullOrWhiteSpace(valeur)) return valeur.Trim();
            if (!string.IsNullOrWhiteSpace(repli)) return repli.Trim();
            return defaut;
        }

        private async Task IntegrerEnSalarieAsync()
        {
            if (CandidatureSelectionnee == null) return;

            if (SalaireEmbaucheSaisi <= 0)
            {
                MessageBox.Show("Veuillez saisir un salaire mensuel négocié valide supérieur à 0 FCFA.", "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (string.IsNullOrWhiteSpace(MatriculeEmbaucheSaisi))
            {
                MessageBox.Show("Veuillez saisir le matricule à attribuer au salarié (le prochain matricule libre du dossier est proposé automatiquement).",
                    "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var rep = MessageBox.Show(
                $"Confirmez-vous l'embauche et l'intégration directe de M./Mme '{CandidatureSelectionnee.CandidatNomComplet}' ?\n\n" +
                $"• Matricule : {MatriculeEmbaucheSaisi.Trim()}\n" +
                $"• Poste : {CandidatureSelectionnee.BesoinIntitulePoste}\n" +
                $"• Salaire Brut : {SalaireEmbaucheSaisi:N0} FCFA\n" +
                $"• Date de Prise de Fonction : {DateEmbaucheSaisie:dd/MM/yyyy}\n" +
                $"• Contrat : {TypeContratEmbaucheSaisi}\n\n" +
                "Cette opération va créer la fiche salarié dans la paie, affecter le poste et verrouiller la candidature.",
                "Confirmation d'Embauche Officielle",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (rep != MessageBoxResult.Yes) return;

            try
            {
                var salarie = await _recrutementService.IntegrerCandidatEnSalarieAsync(
                    CandidatureSelectionnee.Id,
                    SalaireEmbaucheSaisi,
                    DateEmbaucheSaisie,
                    new ConditionsEmbauche
                    {
                        Matricule = MatriculeEmbaucheSaisi,
                        TypeContrat = TypeContratEmbaucheSaisi,
                        Categorie = CategorieEmbaucheSaisie,
                        Echelon = EchelonEmbaucheSaisi,
                        Service = ServiceEmbaucheSaisi,
                        IndemniteLogement = IndemniteLogementSaisie,
                        IndemniteTransport = IndemniteTransportSaisie,
                        IndemniteFonction = IndemniteFonctionSaisie,
                        IndemniteSujetion = IndemniteSujetionSaisie
                    });

                MessageBox.Show(
                    $"Félicitations ! L'embauche a été finalisée avec succès.\n\n" +
                    $"• Salarié créé dans la Paie : {salarie.LastName} {salarie.FirstName}\n" +
                    $"• Matricule attribué : {salarie.EmployeeId}\n" +
                    $"• Date d'entrée : {salarie.DateEntree:dd/MM/yyyy}\n\n" +
                    "Vous pouvez maintenant éditer sa lettre d'embauche officielle en PDF.",
                    "Embauche Validée (SAL-01)",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);

                await ChargerToutesDonneesAsync();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"L'embauche n'a pas pu aboutir : {ex.Message}", "Erreur d'embauche", MessageBoxButton.OK, MessageBoxImage.Stop);
            }
        }

        #endregion

        #region Exports PDF & Excel

        private void ExporterLettreEmbauchePdf()
        {
            if (CandidatureSelectionnee == null) return;

            var sfd = new SaveFileDialog
            {
                Filter = "Document PDF (*.pdf)|*.pdf",
                FileName = $"Lettre_Embauche_{CandidatureSelectionnee.CandidatNomComplet.Replace(" ", "_")}_{DateTime.Today:yyyyMMdd}.pdf",
                Title = "Enregistrer la Lettre d'Embauche Officielle"
            };

            if (sfd.ShowDialog() == true)
            {
                try
                {
                    _exportService.GenererLettreEmbauchePdf(CandidatureSelectionnee, sfd.FileName, TypeContratEmbaucheSaisi);
                    MessageBox.Show("Lettre d'embauche PDF générée avec succès !", "Export réussi", MessageBoxButton.OK, MessageBoxImage.Information);

                    if (File.Exists(sfd.FileName))
                    {
                        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(sfd.FileName) { UseShellExecute = true });
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Erreur lors de la génération de la lettre d'embauche : {ex.Message}", "Erreur PDF", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void ExporterBilanExcel()
        {
            var sfd = new SaveFileDialog
            {
                Filter = "Classeur Excel (*.xlsx)|*.xlsx",
                FileName = $"Bilan_Recrutement_Sigma_{DateTime.Today:yyyyMMdd}.xlsx",
                Title = "Exporter le Bilan Consolidé du Recrutement"
            };

            if (sfd.ShowDialog() == true)
            {
                try
                {
                    _exportService.GenererBilanRecrutementExcel(
                        Besoins.ToList(),
                        Candidatures.ToList(),
                        Candidats.ToList(),
                        sfd.FileName);

                    MessageBox.Show("Bilan de recrutement Excel à 4 feuilles exporté avec succès !", "Export réussi", MessageBoxButton.OK, MessageBoxImage.Information);

                    if (File.Exists(sfd.FileName))
                    {
                        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(sfd.FileName) { UseShellExecute = true });
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Erreur lors de l'exportation Excel : {ex.Message}", "Erreur Excel", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        #endregion
    }
}
