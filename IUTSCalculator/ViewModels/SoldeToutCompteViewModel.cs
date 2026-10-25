using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using Microsoft.Win32;
using IUTSCalculator.Helpers;
using IUTSCalculator.Models;
using IUTSCalculator.Services;

namespace IUTSCalculator.ViewModels
{
    /// <summary>
    /// Élément d'option pour le choix du motif de rupture dans les listes déroulantes.
    /// </summary>
    public class OptionMotifDepart
    {
        public MotifDepart Valeur { get; set; }
        public string Libelle { get; set; }
        public override string ToString() => Libelle;
    }

    /// <summary>
    /// Élément d'option pour le choix du statut du préavis.
    /// </summary>
    public class OptionStatutPreavis
    {
        public StatutPreavis Valeur { get; set; }
        public string Libelle { get; set; }
        public override string ToString() => Libelle;
    }

    /// <summary>
    /// ViewModel principal pour le module SAL-05 — Solde de tout compte et gestion des départs.
    /// </summary>
    public class SoldeToutCompteViewModel : BaseViewModel
    {
        private readonly SoldeToutCompteService _stcService;
        private readonly DataService _dataService;
        private readonly SoldeToutCompteExportService _exportService;
        private readonly SQLiteService _sqliteService;
        private readonly Dossier _dossier;

        private Employee _selectedEmployee;
        private DateTime _dateSortie = DateTime.Today;
        private OptionMotifDepart _selectedMotifOption;
        private OptionStatutPreavis _selectedPreavisOption;

        private decimal _joursPresence = Math.Min(DateTime.Today.Day, 30);
        private decimal? _joursCongesManuel;
        private int? _dureePreavisManuelle;

        // Bulletin de sortie (défaut n° 1 du guide) : préparé au calcul, enregistré à la validation.
        private readonly ChaineBulletinService _chaine;
        private readonly RubriqueService _rubriqueService;
        private ResultatBulletinMois _bulletinSortie;

        // Chaque frappe relance le calcul : seul le DERNIER lancé est retenu (les calculs
        // plus anciens qui finissent après lui sont ignorés).
        private int _versionCalcul;

        // Pour annuler les retenues de saisies si le décompte échoue après le bulletin.
        private readonly SaisieOppositionService _saisiesService;
        private decimal? _salaireRefManuel;
        private decimal _gratification = 0m;
        private decimal _indemnitesDiverses = 0m;
        private string _libelleIndemnitesDiverses = "";
        private decimal _autresDeductions = 0m;
        private string _libelleAutresDeductions = "";
        private string _commentaire = "";

        private SoldeToutCompte _soldeCourant;
        private SoldeToutCompte _selectedSoldeHistorique;
        private string _filtreHistorique = "";
        private string _filtreSalarie = "";

        public ObservableCollection<Employee> Employees { get; } = new ObservableCollection<Employee>();
        public ObservableCollection<Employee> EmployeesFiltres { get; } = new ObservableCollection<Employee>();
        public ObservableCollection<SoldeToutCompte> SoldesHistorique { get; } = new ObservableCollection<SoldeToutCompte>();
        public ObservableCollection<SoldeToutCompte> SoldesHistoriqueFiltres { get; } = new ObservableCollection<SoldeToutCompte>();

        public List<OptionMotifDepart> Motifs { get; } = new List<OptionMotifDepart>
        {
            new OptionMotifDepart { Valeur = MotifDepart.Demission, Libelle = "Démission" },
            new OptionMotifDepart { Valeur = MotifDepart.FinContrat, Libelle = "Fin de contrat CDD (Terme)" },
            new OptionMotifDepart { Valeur = MotifDepart.LicenciementPersonnel, Libelle = "Licenciement pour motif personnel" },
            new OptionMotifDepart { Valeur = MotifDepart.LicenciementEconomique, Libelle = "Licenciement pour motif économique" },
            new OptionMotifDepart { Valeur = MotifDepart.LicenciementFauteLourde, Libelle = "Licenciement pour faute lourde" },
            new OptionMotifDepart { Valeur = MotifDepart.RetraiteDepart, Libelle = "Départ volontaire à la retraite" },
            new OptionMotifDepart { Valeur = MotifDepart.RetraiteMise, Libelle = "Mise à la retraite par l'employeur" },
            new OptionMotifDepart { Valeur = MotifDepart.RuptureConventionnelle, Libelle = "Rupture d'un commun accord" },
            new OptionMotifDepart { Valeur = MotifDepart.Deces, Libelle = "Décès du travailleur" },
            new OptionMotifDepart { Valeur = MotifDepart.Autre, Libelle = "Autre motif" }
        };

        public List<OptionStatutPreavis> StatutsPreavis { get; } = new List<OptionStatutPreavis>
        {
            new OptionStatutPreavis { Valeur = StatutPreavis.Effectue, Libelle = "Préavis effectué (travaillé)" },
            new OptionStatutPreavis { Valeur = StatutPreavis.DispensePaye, Libelle = "Dispensé et rémunéré (indemnité due)" },
            new OptionStatutPreavis { Valeur = StatutPreavis.Inapplicable, Libelle = "Non applicable / Sans préavis" }
        };

        public SoldeToutCompteViewModel(
            SQLiteService sqliteService = null,
            DataService dataService = null,
            SoldeToutCompteService stcService = null,
            SoldeToutCompteExportService exportService = null,
            Dossier dossier = null,
            ChaineBulletinService chaine = null,
            RubriqueService rubriqueService = null)
        {
            Title = "Solde de Tout Compte & Départs";
            _dossier = dossier ?? DossierSession.Instance.CurrentDossier;
            _sqliteService = sqliteService ?? new SQLiteService();
            _dataService = dataService ?? new DataService("iuts_data.db");
            _stcService = stcService ?? new SoldeToutCompteService(_sqliteService);
            _exportService = exportService ?? new SoldeToutCompteExportService();
            _chaine = chaine;
            _rubriqueService = rubriqueService ?? new RubriqueService(_sqliteService);
            _saisiesService = new SaisieOppositionService(_sqliteService);

            _selectedMotifOption = Motifs.FirstOrDefault();
            _selectedPreavisOption = StatutsPreavis.FirstOrDefault();

            InitialiserCommandes();
            _ = ChargerDonneesAsync();
        }

        // ── Propriétés d'état et de saisie ───────────────────────────────────

        public Employee SelectedEmployee
        {
            get => _selectedEmployee;
            set
            {
                if (SetProperty(ref _selectedEmployee, value))
                {
                    OnSelectedEmployeeChanged();
                }
            }
        }

        public DateTime DateSortie
        {
            get => _dateSortie;
            set
            {
                if (SetProperty(ref _dateSortie, value))
                {
                    _ = CalculerSoldeAutomatiqueAsync();
                }
            }
        }

        public OptionMotifDepart SelectedMotifOption
        {
            get => _selectedMotifOption;
            set
            {
                if (SetProperty(ref _selectedMotifOption, value))
                {
                    AjusterPreavisSelonMotif();
                    _ = CalculerSoldeAutomatiqueAsync();
                }
            }
        }

        public OptionStatutPreavis SelectedPreavisOption
        {
            get => _selectedPreavisOption;
            set
            {
                if (SetProperty(ref _selectedPreavisOption, value))
                {
                    _ = CalculerSoldeAutomatiqueAsync();
                }
            }
        }

        /// <summary>
        /// Durée du préavis en mois saisie à la main (vide : durée de la catégorie — 3 mois
        /// pour les cadres, agents de maîtrise et techniciens, 1 mois sinon).
        /// </summary>
        public int? DureePreavisManuelle
        {
            get => _dureePreavisManuelle;
            set
            {
                if (SetProperty(ref _dureePreavisManuelle, value))
                {
                    _ = CalculerSoldeAutomatiqueAsync();
                }
            }
        }

        public decimal JoursPresence
        {
            get => _joursPresence;
            set
            {
                if (SetProperty(ref _joursPresence, value))
                {
                    _ = CalculerSoldeAutomatiqueAsync();
                }
            }
        }

        public decimal? JoursCongesManuel
        {
            get => _joursCongesManuel;
            set
            {
                if (SetProperty(ref _joursCongesManuel, value))
                {
                    _ = CalculerSoldeAutomatiqueAsync();
                }
            }
        }

        public decimal? SalaireRefManuel
        {
            get => _salaireRefManuel;
            set
            {
                if (SetProperty(ref _salaireRefManuel, value))
                {
                    _ = CalculerSoldeAutomatiqueAsync();
                }
            }
        }

        public decimal Gratification
        {
            get => _gratification;
            set
            {
                if (SetProperty(ref _gratification, value))
                {
                    _ = CalculerSoldeAutomatiqueAsync();
                }
            }
        }

        public decimal IndemnitesDiverses
        {
            get => _indemnitesDiverses;
            set
            {
                if (SetProperty(ref _indemnitesDiverses, value))
                {
                    _ = CalculerSoldeAutomatiqueAsync();
                }
            }
        }

        public string LibelleIndemnitesDiverses
        {
            get => _libelleIndemnitesDiverses;
            set => SetProperty(ref _libelleIndemnitesDiverses, value);
        }

        public decimal AutresDeductions
        {
            get => _autresDeductions;
            set
            {
                if (SetProperty(ref _autresDeductions, value))
                {
                    _ = CalculerSoldeAutomatiqueAsync();
                }
            }
        }

        public string LibelleAutresDeductions
        {
            get => _libelleAutresDeductions;
            set => SetProperty(ref _libelleAutresDeductions, value);
        }

        public string Commentaire
        {
            get => _commentaire;
            set => SetProperty(ref _commentaire, value);
        }

        public SoldeToutCompte SoldeCourant
        {
            get => _soldeCourant;
            set
            {
                if (SetProperty(ref _soldeCourant, value))
                {
                    OnPropertyChanged(nameof(ASoldeCalcule));
                    ActualiserEtatCommandes();
                }
            }
        }

        public bool ASoldeCalcule => SoldeCourant != null;

        public SoldeToutCompte SelectedSoldeHistorique
        {
            get => _selectedSoldeHistorique;
            set
            {
                if (SetProperty(ref _selectedSoldeHistorique, value))
                {
                    ActualiserEtatCommandes();
                }
            }
        }

        public string FiltreHistorique
        {
            get => _filtreHistorique;
            set
            {
                if (SetProperty(ref _filtreHistorique, value))
                {
                    AppliquerFiltreHistorique();
                }
            }
        }

        public string FiltreSalarie
        {
            get => _filtreSalarie;
            set
            {
                if (SetProperty(ref _filtreSalarie, value))
                {
                    AppliquerFiltreSalaries();
                }
            }
        }

        // ── Commandes ────────────────────────────────────────────────────────

        public ICommand CalculerCommand { get; private set; }
        public ICommand ValiderEtEnregistrerCommand { get; private set; }
        public ICommand ImprimerRecuPdfCommand { get; private set; }
        public ICommand ImprimerCertificatPdfCommand { get; private set; }
        public ICommand ImprimerAttestationPdfCommand { get; private set; }
        public ICommand ExporterExcelCommand { get; private set; }
        public ICommand SupprimerSoldeCommand { get; private set; }
        public ICommand RafraichirCommand { get; private set; }
        public ICommand ChargerSoldeHistoriqueCommand { get; private set; }

        private void InitialiserCommandes()
        {
            CalculerCommand = new RelayCommand(async () => await CalculerSoldeAutomatiqueAsync(), () => SelectedEmployee != null);
            ValiderEtEnregistrerCommand = new RelayCommand(async () => await ValiderEtEnregistrerAsync(), () => SoldeCourant != null);

            ImprimerRecuPdfCommand = new RelayCommand(ImprimerRecuPdf, () => ObtenirSoldeActif() != null);
            ImprimerCertificatPdfCommand = new RelayCommand(ImprimerCertificatPdf, () => ObtenirSoldeActif() != null);
            ImprimerAttestationPdfCommand = new RelayCommand(ImprimerAttestationPdf, () => ObtenirSoldeActif() != null);
            ExporterExcelCommand = new RelayCommand(ExporterExcel, () => ObtenirSoldeActif() != null);

            SupprimerSoldeCommand = new RelayCommand(async () => await SupprimerSoldeSelectionneAsync(), () => SelectedSoldeHistorique != null);
            RafraichirCommand = new RelayCommand(async () => await ChargerDonneesAsync());
            ChargerSoldeHistoriqueCommand = new RelayCommand<SoldeToutCompte>(stc =>
            {
                if (stc != null)
                {
                    SoldeCourant = stc;
                    StatusMessage = $"Solde rappelé pour {stc.NomSalarie} (Sortie le {stc.DateSortie:dd/MM/yyyy})";
                }
            });
        }

        private void ActualiserEtatCommandes()
        {
            (CalculerCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (ValiderEtEnregistrerCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (ImprimerRecuPdfCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (ImprimerCertificatPdfCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (ImprimerAttestationPdfCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (ExporterExcelCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (SupprimerSoldeCommand as RelayCommand)?.RaiseCanExecuteChanged();
        }

        private SoldeToutCompte ObtenirSoldeActif()
        {
            return SoldeCourant ?? SelectedSoldeHistorique;
        }

        // ── Logique métier ───────────────────────────────────────────────────

        private string _infoDiscipline;

        /// <summary>
        /// Rappel du licenciement disciplinaire notifié (module RH-06) qui a servi à
        /// proposer le motif et la date de sortie ; vide s'il n'y en a pas.
        /// </summary>
        public string InfoDiscipline
        {
            get => _infoDiscipline;
            private set
            {
                if (SetProperty(ref _infoDiscipline, value))
                    OnPropertyChanged(nameof(AInfoDiscipline));
            }
        }

        public bool AInfoDiscipline => !string.IsNullOrEmpty(_infoDiscipline);

        /// <summary>
        /// Passerelle RH-06 : si le salarié a reçu notification d'un licenciement
        /// disciplinaire, son motif (faute lourde ou motif personnel) et sa date d'effet
        /// sont proposés. Le gestionnaire garde la main : tout reste modifiable.
        /// </summary>
        private async Task ProposerDepuisDisciplineAsync(Employee salarie)
        {
            InfoDiscipline = null;
            if (salarie == null || _sqliteService == null) return;

            try
            {
                var procedure = await new DisciplineService(_sqliteService).DernierLicenciementNotifieAsync(salarie.EmployeeId);
                if (procedure == null || procedure.TypeSanction == null || !ReferenceEquals(SelectedEmployee, salarie)) return;

                var motif = procedure.TypeSanction.MotifDepartAssocie;
                if (motif.HasValue)
                {
                    var option = Motifs.FirstOrDefault(m => m.Valeur == motif.Value);
                    if (option != null) SelectedMotifOption = option;
                }
                if (procedure.DateEffetLicenciement.HasValue && !salarie.DateSortie.HasValue)
                    DateSortie = procedure.DateEffetLicenciement.Value;

                InfoDiscipline = string.Format(LibellesDiscipline.Fr,
                    "Procédure disciplinaire {0} : {1} notifié le {2:dd/MM/yyyy}{3}. Motif et date de sortie proposés en conséquence.",
                    procedure.Reference, procedure.TypeSanction.Libelle.ToLower(LibellesDiscipline.Fr), procedure.DateNotification,
                    procedure.DateEffetLicenciement.HasValue
                        ? string.Format(LibellesDiscipline.Fr, ", effet le {0:dd/MM/yyyy}", procedure.DateEffetLicenciement.Value)
                        : "");
            }
            catch
            {
                // Dossier sans module Discipline (schéma antérieur) : rien à proposer.
            }
        }

        private void OnSelectedEmployeeChanged()
        {
            if (SelectedEmployee == null)
            {
                SoldeCourant = null;
                InfoDiscipline = null;
                return;
            }

            if (SelectedEmployee.DateSortie.HasValue)
            {
                DateSortie = SelectedEmployee.DateSortie.Value;
            }
            else
            {
                DateSortie = DateTime.Today;
            }

            JoursPresence = Math.Min(DateSortie.Day, 30);
            _joursCongesManuel = null;
            OnPropertyChanged(nameof(JoursCongesManuel));
            _salaireRefManuel = null;
            OnPropertyChanged(nameof(SalaireRefManuel));

            AjusterPreavisSelonMotif();
            _ = CalculerSoldeAutomatiqueAsync();
            _ = ProposerDepuisDisciplineAsync(SelectedEmployee);
        }

        private void AjusterPreavisSelonMotif()
        {
            if (SelectedMotifOption == null) return;

            var defaut = SoldeToutCompteService.DeterminerStatutPreavisDefaut(SelectedMotifOption.Valeur);
            var opt = StatutsPreavis.FirstOrDefault(p => p.Valeur == defaut);
            if (opt != null && (SelectedPreavisOption == null || SelectedPreavisOption.Valeur != defaut))
            {
                _selectedPreavisOption = opt;
                OnPropertyChanged(nameof(SelectedPreavisOption));
            }
        }

        public async Task CalculerSoldeAutomatiqueAsync()
        {
            if (SelectedEmployee == null) return;
            int version = ++_versionCalcul;

            try
            {
                IsBusy = true;
                StatusMessage = "Calcul du solde de tout compte...";

                var motif = SelectedMotifOption?.Valeur ?? MotifDepart.Demission;
                var preavis = SelectedPreavisOption?.Valeur ?? StatutPreavis.Effectue;

                var stc = await _stcService.PreparerSoldeAsync(
                    SelectedEmployee,
                    DateSortie,
                    motif,
                    preavis,
                    JoursPresence,
                    JoursCongesManuel,
                    SalaireRefManuel,
                    Gratification,
                    IndemnitesDiverses,
                    LibelleIndemnitesDiverses,
                    AutresDeductions,
                    LibelleAutresDeductions,
                    DureePreavisManuelle);

                // Bulletin de sortie calculé par le moteur de paie : le décompte s'aligne sur
                // lui (IUTS au barème, CNSS plafonnée, net = net du bulletin).
                string mention = string.Empty;
                ResultatBulletinMois bulletin = null;
                if (_chaine != null)
                {
                    var salarie = await _dataService.GetEmployeeByIdAsync(stc.EmployeeId) ?? SelectedEmployee;
                    var plan = await _rubriqueService.ChargerAsync();
                    bulletin = await _stcService.PreparerBulletinSortieAsync(stc, salarie, _chaine, plan);
                    mention = " Calculé sur le bulletin de sortie de " + stc.DateSortie.ToString("MM/yyyy") + ".";
                    foreach (var avertissement in bulletin.Avertissements)
                        mention += " Attention : " + avertissement + ".";
                }

                // Un calcul plus récent a été lancé pendant celui-ci : on l'ignore.
                if (version != _versionCalcul) return;

                _bulletinSortie = bulletin;
                stc.Commentaire = Commentaire;
                SoldeCourant = stc;

                StatusMessage = $"Décompte prêt pour {stc.NomSalarie} : Net à percevoir {stc.TotalNet:N0} FCFA (Brut: {stc.TotalBrut:N0} FCFA)." + mention;
            }
            catch (Exception ex)
            {
                if (version == _versionCalcul)
                    StatusMessage = "Erreur lors du calcul du solde : " + ex.Message;
            }
            finally
            {
                if (version == _versionCalcul)
                    IsBusy = false;
            }
        }

        private async Task ValiderEtEnregistrerAsync()
        {
            if (SoldeCourant == null) return;

            var res = MessageBox.Show(
                $"Confirmez-vous la validation définitive de la sortie de {SoldeCourant.NomSalarie} au {SoldeCourant.DateSortie:dd/MM/yyyy} ?\n\n" +
                $"Montant net à verser : {SoldeCourant.TotalNet:N0} FCFA.\n\n" +
                "Le salarié sera marqué comme 'Sorti / Inactif' dans la base du personnel et le décompte sera archivé.",
                "Validation du Solde de Tout Compte",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (res != MessageBoxResult.Yes)
                return;

            try
            {
                IsBusy = true;
                StatusMessage = "Enregistrement du solde et mise à jour du salarié...";

                // Le bulletin de sortie entre dans la paie du mois de sortie : ce mois doit
                // être ouvert, sinon rien n'est enregistré (ni décompte ni sortie).
                var periodeSortie = new DateTime(SoldeCourant.DateSortie.Year, SoldeCourant.DateSortie.Month, 1);
                if (_chaine != null && await new PeriodePaieService(_sqliteService).EstClotureeAsync(periodeSortie))
                {
                    StatusMessage = $"Validation refusée : la période {periodeSortie:MM/yyyy} est clôturée. Rouvrez-la (écran « Périodes de paie ») " +
                                    "pour y enregistrer le bulletin de sortie, ou choisissez une date de sortie dans une période ouverte.";
                    MessageBox.Show(StatusMessage, "Solde de tout compte", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                // Décompte et bulletin recalculés au moment de valider, à partir des saisies
                // de l'écran et de la fiche relue (un décompte déjà aligné ne se réaligne pas).
                ResultatBulletinMois bulletinSortie = null;
                if (_chaine != null)
                {
                    await CalculerSoldeAutomatiqueAsync();
                    IsBusy = true;
                    bulletinSortie = _bulletinSortie;
                    if (bulletinSortie == null || SoldeCourant == null)
                    {
                        StatusMessage = "Validation interrompue : le bulletin de sortie n'a pas pu être calculé. " + StatusMessage;
                        return;
                    }
                }

                // Un bulletin ordinaire déjà enregistré pour le mois de sortie sera remplacé
                // par le bulletin de sortie : on le dit avant de le faire.
                if (bulletinSortie != null && await _dataService.BulletinEnregistreAsync(SoldeCourant.EmployeeId, periodeSortie))
                {
                    var remplacer = MessageBox.Show(
                        $"Un bulletin est déjà enregistré pour {SoldeCourant.NomSalarie} en {periodeSortie:MM/yyyy}.\n\n" +
                        "La validation le remplace par le bulletin de sortie (paie du mois au prorata et indemnités de rupture).\n\nContinuer ?",
                        "Solde de tout compte", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
                    if (remplacer != MessageBoxResult.Yes)
                    {
                        StatusMessage = "Validation annulée : le bulletin du mois de sortie est conservé, rien n'a été enregistré.";
                        return;
                    }
                }

                SoldeCourant.Commentaire = Commentaire;

                // Ordre d'enregistrement : le bulletin de sortie d'abord, le décompte ensuite.
                // Un échec du bulletin n'enregistre rien (pas de salarié « Sorti » sans bulletin) ;
                // un échec du décompte supprime le bulletin qu'on vient d'écrire.
                string mentionBulletin = string.Empty;
                if (bulletinSortie != null)
                {
                    var salarie = await _dataService.GetEmployeeByIdAsync(SoldeCourant.EmployeeId) ?? SelectedEmployee;
                    try
                    {
                        await _chaine.EnregistrerAsync(bulletinSortie, salarie, periodeSortie);
                    }
                    catch (Exception exBulletin)
                    {
                        StatusMessage = "Validation interrompue : le bulletin de sortie n'a pas pu être enregistré (" + exBulletin.Message +
                                        "). Rien n'a été enregistré : le salarié reste actif. Corrigez la cause puis validez à nouveau.";
                        MessageBox.Show(StatusMessage, "Solde de tout compte", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }
                    mentionBulletin = $"\nLe bulletin de sortie de {periodeSortie:MM/yyyy} est enregistré dans la paie du mois " +
                                      $"(net à payer {bulletinSortie.Bulletin.SalaireNetAPayer:N0} FCFA) : il entre dans les déclarations, " +
                                      "le livre de paie, les virements et les écritures.";
                }

                int id;
                try
                {
                    id = await _stcService.EnregistrerSoldeAsync(SoldeCourant, marquerEmployeSorti: true);
                }
                catch (Exception exSolde)
                {
                    string annulation = string.Empty;
                    if (bulletinSortie != null && bulletinSortie.Calcul != null)
                    {
                        try
                        {
                            await _dataService.DeleteCalculationFromHistoryAsync(bulletinSortie.Calcul.Id);
                            if (_saisiesService != null)
                                await _saisiesService.AppliquerEtEnregistrerRetenuesMoisAsync(
                                    periodeSortie.Year, periodeSortie.Month, SoldeCourant.EmployeeId, 0m, DateTime.Today);
                            annulation = " Le bulletin de sortie qui venait d'être enregistré a été supprimé : régénérez le bulletin du mois si besoin.";
                        }
                        catch (Exception exAnnulation)
                        {
                            annulation = " ATTENTION : le bulletin de sortie déjà enregistré n'a pas pu être supprimé (" + exAnnulation.Message + ").";
                        }
                    }
                    StatusMessage = "Erreur lors de l'enregistrement du décompte : " + exSolde.Message + "." + annulation;
                    MessageBox.Show(StatusMessage, "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                StatusMessage = $"Solde de tout compte #{id} validé et archivé avec succès. Salarié marqué comme sorti." +
                                mentionBulletin.Replace("\n", " ");
                MessageBox.Show(
                    $"Le solde de tout compte a été enregistré avec succès (N° {id}).\n" +
                    "La fiche du salarié a été mise à jour avec le statut 'Sorti'." + mentionBulletin,
                    "Enregistrement réussi",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);

                await ChargerDonneesAsync();
            }
            catch (Exception ex)
            {
                StatusMessage = "Erreur lors de l'enregistrement : " + ex.Message;
                MessageBox.Show("Erreur lors de l'enregistrement : " + ex.Message, "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                IsBusy = false;
            }
        }

        private async Task SupprimerSoldeSelectionneAsync()
        {
            if (SelectedSoldeHistorique == null) return;

            var res = MessageBox.Show(
                $"Supprimer définitivement le solde de tout compte #{SelectedSoldeHistorique.Id} de {SelectedSoldeHistorique.NomSalarie} ?",
                "Confirmation de suppression",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (res != MessageBoxResult.Yes) return;

            try
            {
                IsBusy = true;
                bool ok = await _stcService.SupprimerSoldeAsync(SelectedSoldeHistorique.Id);
                if (ok)
                {
                    StatusMessage = "Solde supprimé de l'historique.";
                    await ChargerSoldesHistoriqueAsync();
                }
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

        // ── Exports & Impressions ────────────────────────────────────────────

        private Dossier ObtenirDossier() => _dossier ?? DossierSession.Instance.CurrentDossier;

        private void ImprimerRecuPdf()
        {
            var stc = ObtenirSoldeActif();
            if (stc == null) return;

            string defaut = $"Recu_STC_{stc.EmployeeId}_{stc.DateSortie:yyyyMMdd}.pdf";
            string path = SelectionnerFichier(defaut, "Fichier PDF (*.pdf)|*.pdf", ".pdf");
            if (string.IsNullOrWhiteSpace(path)) return;

            try
            {
                var dossier = ObtenirDossier();
                string raisonSociale = dossier?.RaisonSociale;
                string ifu = dossier?.IFU ?? "";
                string adresse = dossier?.Adresse ?? "";

                bool ok = _exportService.GenererRecuPdf(stc, path, raisonSociale, ifu, adresse);
                if (ok)
                {
                    StatusMessage = $"Reçu pour solde de tout compte exporté : {path}";
                    OuvrirFichier(path);
                }
                else
                {
                    StatusMessage = "Échec de génération du reçu PDF.";
                }
            }
            catch (Exception ex)
            {
                StatusMessage = "Erreur d'export PDF : " + ex.Message;
            }
        }

        private void ImprimerCertificatPdf()
        {
            var stc = ObtenirSoldeActif();
            if (stc == null) return;

            string defaut = $"Certificat_Travail_{stc.EmployeeId}_{stc.DateSortie:yyyyMMdd}.pdf";
            string path = SelectionnerFichier(defaut, "Fichier PDF (*.pdf)|*.pdf", ".pdf");
            if (string.IsNullOrWhiteSpace(path)) return;

            try
            {
                var dossier = ObtenirDossier();
                string raisonSociale = dossier?.RaisonSociale;
                string ifu = dossier?.IFU ?? "";
                string adresse = dossier?.Adresse ?? "";
                string ville = dossier?.Ville;

                bool ok = _exportService.GenererCertificatTravailPdf(stc, path, raisonSociale, ifu, adresse, ville);
                if (ok)
                {
                    StatusMessage = $"Certificat de travail exporté : {path}";
                    OuvrirFichier(path);
                }
                else
                {
                    StatusMessage = "Échec de génération du certificat PDF.";
                }
            }
            catch (Exception ex)
            {
                StatusMessage = "Erreur certificat : " + ex.Message;
            }
        }

        private void ImprimerAttestationPdf()
        {
            var stc = ObtenirSoldeActif();
            if (stc == null) return;

            string defaut = $"Attestation_Travail_{stc.EmployeeId}_{stc.DateSortie:yyyyMMdd}.pdf";
            string path = SelectionnerFichier(defaut, "Fichier PDF (*.pdf)|*.pdf", ".pdf");
            if (string.IsNullOrWhiteSpace(path)) return;

            try
            {
                var dossier = ObtenirDossier();
                string raisonSociale = dossier?.RaisonSociale;
                string ifu = dossier?.IFU ?? "";
                string adresse = dossier?.Adresse ?? "";
                string ville = dossier?.Ville;

                bool ok = _exportService.GenererAttestationTravailPdf(stc, path, raisonSociale, ifu, adresse, ville);
                if (ok)
                {
                    StatusMessage = $"Attestation de travail exportée : {path}";
                    OuvrirFichier(path);
                }
                else
                {
                    StatusMessage = "Échec de génération de l'attestation PDF.";
                }
            }
            catch (Exception ex)
            {
                StatusMessage = "Erreur attestation : " + ex.Message;
            }
        }

        private void ExporterExcel()
        {
            var stc = ObtenirSoldeActif();
            if (stc == null) return;

            string defaut = $"STC_{stc.EmployeeId}_{stc.DateSortie:yyyyMMdd}.xlsx";
            string path = SelectionnerFichier(defaut, "Classeur Excel (*.xlsx)|*.xlsx", ".xlsx");
            if (string.IsNullOrWhiteSpace(path)) return;

            try
            {
                var dossier = ObtenirDossier();
                string raisonSociale = dossier?.RaisonSociale;

                _exportService.ExporterExcel(stc, path, raisonSociale);
                StatusMessage = $"Décompte exporté vers Excel : {path}";
                OuvrirFichier(path);
            }
            catch (Exception ex)
            {
                StatusMessage = "Erreur export Excel : " + ex.Message;
            }
        }

        private static string SelectionnerFichier(string nomDefaut, string filtre, string extDefaut)
        {
            var sfd = new SaveFileDialog
            {
                FileName = nomDefaut,
                Filter = filtre,
                DefaultExt = extDefaut,
                InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
            };

            return sfd.ShowDialog() == true ? sfd.FileName : null;
        }

        private static void OuvrirFichier(string chemin)
        {
            try
            {
                if (File.Exists(chemin))
                {
                    Process.Start(new ProcessStartInfo(chemin) { UseShellExecute = true });
                }
            }
            catch
            {
                // Ignorer l'erreur d'ouverture automatique
            }
        }

        // ── Chargement des données ───────────────────────────────────────────

        public async Task ChargerDonneesAsync()
        {
            try
            {
                IsBusy = true;
                StatusMessage = "Chargement des salariés et de l'historique...";

                var employes = await _dataService.LoadEmployeesAsync();
                Employees.Clear();
                foreach (var e in employes.OrderBy(e => e.LastName).ThenBy(e => e.FirstName))
                {
                    Employees.Add(e);
                }

                AppliquerFiltreSalaries();

                if (SelectedEmployee == null && EmployeesFiltres.Count > 0)
                {
                    SelectedEmployee = EmployeesFiltres.FirstOrDefault(e => e.EstActif) ?? EmployeesFiltres.First();
                }

                await ChargerSoldesHistoriqueAsync();

                StatusMessage = $"{Employees.Count} salarié(s) chargé(s), {SoldesHistorique.Count} solde(s) archivé(s).";
            }
            catch (Exception ex)
            {
                StatusMessage = "Erreur chargement données : " + ex.Message;
            }
            finally
            {
                IsBusy = false;
            }
        }

        private async Task ChargerSoldesHistoriqueAsync()
        {
            var soldes = await _stcService.ChargerSoldesAsync();
            SoldesHistorique.Clear();
            foreach (var s in soldes)
            {
                SoldesHistorique.Add(s);
            }
            AppliquerFiltreHistorique();
        }

        private void AppliquerFiltreSalaries()
        {
            EmployeesFiltres.Clear();
            var q = string.IsNullOrWhiteSpace(FiltreSalarie) ? Employees :
                Employees.Where(e =>
                    (!string.IsNullOrEmpty(e.LastName) && e.LastName.IndexOf(FiltreSalarie, StringComparison.OrdinalIgnoreCase) >= 0) ||
                    (!string.IsNullOrEmpty(e.FirstName) && e.FirstName.IndexOf(FiltreSalarie, StringComparison.OrdinalIgnoreCase) >= 0) ||
                    (!string.IsNullOrEmpty(e.EmployeeId) && e.EmployeeId.IndexOf(FiltreSalarie, StringComparison.OrdinalIgnoreCase) >= 0));

            foreach (var e in q)
                EmployeesFiltres.Add(e);
        }

        private void AppliquerFiltreHistorique()
        {
            SoldesHistoriqueFiltres.Clear();
            var q = string.IsNullOrWhiteSpace(FiltreHistorique) ? SoldesHistorique :
                SoldesHistorique.Where(s =>
                    (!string.IsNullOrEmpty(s.NomSalarie) && s.NomSalarie.IndexOf(FiltreHistorique, StringComparison.OrdinalIgnoreCase) >= 0) ||
                    (!string.IsNullOrEmpty(s.EmployeeId) && s.EmployeeId.IndexOf(FiltreHistorique, StringComparison.OrdinalIgnoreCase) >= 0) ||
                    (!string.IsNullOrEmpty(s.MotifLibelle) && s.MotifLibelle.IndexOf(FiltreHistorique, StringComparison.OrdinalIgnoreCase) >= 0));

            foreach (var s in q)
                SoldesHistoriqueFiltres.Add(s);
        }
    }
}
