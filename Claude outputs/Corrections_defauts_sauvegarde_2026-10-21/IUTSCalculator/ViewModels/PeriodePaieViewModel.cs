using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using IUTSCalculator.Helpers;
using IUTSCalculator.Models;
using IUTSCalculator.Services;

namespace IUTSCalculator.ViewModels
{
    /// <summary>
    /// Écran « Périodes de paie » (module PAI-01) : l'historique des mois à gauche, le
    /// détail de la période sélectionnée et ses totaux arrêtés à droite.
    ///
    /// La clôture est le seul geste irréversible de cet écran — irréversible au sens où
    /// elle verrouille l'enregistrement des bulletins du mois. Elle reste annulable par
    /// une réouverture motivée, tracée dans le commentaire de la période.
    /// </summary>
    public class PeriodePaieViewModel : BaseViewModel
    {
        private readonly PeriodePaieService _periodeService;
        private readonly AuditService _auditService;

        public ObservableCollection<PeriodePaie> Periodes { get; } = new ObservableCollection<PeriodePaie>();

        public PeriodePaieViewModel(PeriodePaieService periodeService)
            : this(periodeService, null)
        {
        }

        public PeriodePaieViewModel(PeriodePaieService periodeService, AuditService auditService)
        {
            if (periodeService == null) throw new ArgumentNullException("periodeService");
            _periodeService = periodeService;
            _auditService = auditService;

            Title = "Périodes de paie";

            _anneeAOuvrir = DateTime.Today.Year;
            _moisAOuvrir = MoisDisponibles.FirstOrDefault(m => m.Numero == DateTime.Today.Month);

            OuvrirPeriodeCommand = new RelayCommand(async () => await OuvrirAsync(), () => !IsBusy);
            CloturerCommand = new RelayCommand(async () => await CloturerAsync(), PeutCloturer);
            RouvrirCommand = new RelayCommand(async () => await RouvrirAsync(), PeutRouvrir);
            RafraichirTotauxCommand = new RelayCommand(async () => await RafraichirTotauxAsync(), PeutRafraichir);
            EnregistrerCommentaireCommand = new RelayCommand(async () => await EnregistrerCommentaireAsync(), () => PeriodeSelectionnee != null);
            SupprimerCommand = new RelayCommand(async () => await SupprimerAsync(), PeutSupprimer);
            RechargerCommand = new RelayCommand(async () => await ChargerAsync());

            _ = ChargerAsync();
        }

        /// <summary>
        /// Cet écran ne se rechargeait jamais automatiquement après sa première ouverture :
        /// revenir dessus après une clôture faite ailleurs (ou par un collègue sur le même
        /// dossier) continuait d'afficher un état périmé tant qu'on ne cliquait pas sur
        /// « Recharger » — signalé le 16 septembre 2026 (voir ETAT-DEVELOPPEMENT.md item
        /// 22). Sans risque pour un motif de réouverture en cours de saisie : voir le garde
        /// « mêmePériode » dans PeriodeSelectionnee.
        /// </summary>
        protected override void OnIsActiveChanged()
        {
            if (IsActive)
                _ = ChargerAsync();
        }

        // ── Sélection ────────────────────────────────────────────────────────

        private PeriodePaie _periodeSelectionnee;
        public PeriodePaie PeriodeSelectionnee
        {
            get { return _periodeSelectionnee; }
            set
            {
                // ChargerAsync() réaffecte cette propriété après chaque rechargement avec
                // une NOUVELLE instance représentant la MÊME période, pour rafraîchir son
                // état. Remettre le motif de réouverture à zéro à chaque fois effacerait
                // alors une justification en cours de saisie dès qu'on revient sur cet
                // écran.
                bool memePeriode = _periodeSelectionnee != null && value != null
                    && _periodeSelectionnee.Id == value.Id;

                if (SetProperty(ref _periodeSelectionnee, value))
                {
                    if (!memePeriode)
                        MotifReouverture = "";
                    OnPropertyChanged(nameof(ALaFiche));
                    OnPropertyChanged(nameof(ConfirmationDemandee));
                    RafraichirCommandes();
                }
            }
        }

        public bool ALaFiche
        {
            get { return PeriodeSelectionnee != null; }
        }

        /// <summary>Vrai quand la période ouverte demande un motif avant réouverture.</summary>
        public bool ConfirmationDemandee
        {
            get { return PeriodeSelectionnee != null && PeriodeSelectionnee.EstCloturee; }
        }

        private string _motifReouverture;
        /// <summary>
        /// Motif de la réouverture. Exigé : rouvrir un mois déclaré doit laisser une trace
        /// lisible par quelqu'un d'autre, des mois plus tard.
        /// </summary>
        public string MotifReouverture
        {
            get { return _motifReouverture; }
            set
            {
                if (SetProperty(ref _motifReouverture, value))
                    RafraichirCommandes();
            }
        }

        // ── Ouverture d'une nouvelle période ─────────────────────────────────

        /// <summary>Les douze mois, avec leur nom en français.</summary>
        public IEnumerable<MoisOption> MoisDisponibles
        {
            get { return MoisOption.Tous; }
        }

        public IEnumerable<int> AnneesDisponibles
        {
            get
            {
                int courante = DateTime.Today.Year;
                return Enumerable.Range(courante - 5, 8).ToList();
            }
        }

        private int _anneeAOuvrir;
        public int AnneeAOuvrir
        {
            get { return _anneeAOuvrir; }
            set { SetProperty(ref _anneeAOuvrir, value); }
        }

        private MoisOption _moisAOuvrir;
        public MoisOption MoisAOuvrir
        {
            get { return _moisAOuvrir; }
            set { SetProperty(ref _moisAOuvrir, value); }
        }

        // ── Compteurs d'en-tête ──────────────────────────────────────────────

        public int NombrePeriodesOuvertes
        {
            get { return Periodes.Count(p => p.EstOuverte); }
        }

        public int NombrePeriodesCloturees
        {
            get { return Periodes.Count(p => p.EstCloturee); }
        }

        // ── Commandes ────────────────────────────────────────────────────────

        public ICommand OuvrirPeriodeCommand { get; private set; }
        public ICommand CloturerCommand { get; private set; }
        public ICommand RouvrirCommand { get; private set; }
        public ICommand RafraichirTotauxCommand { get; private set; }
        public ICommand EnregistrerCommentaireCommand { get; private set; }
        public ICommand SupprimerCommand { get; private set; }
        public ICommand RechargerCommand { get; private set; }

        private bool PeutCloturer()
        {
            return !IsBusy && PeriodeSelectionnee != null && PeriodeSelectionnee.EstOuverte;
        }

        private bool PeutRouvrir()
        {
            return !IsBusy
                && PeriodeSelectionnee != null
                && PeriodeSelectionnee.EstCloturee
                && !string.IsNullOrWhiteSpace(MotifReouverture);
        }

        private bool PeutRafraichir()
        {
            return !IsBusy && PeriodeSelectionnee != null && PeriodeSelectionnee.EstOuverte;
        }

        private bool PeutSupprimer()
        {
            return !IsBusy && PeriodeSelectionnee != null && PeriodeSelectionnee.EstOuverte;
        }

        private void RafraichirCommandes()
        {
            foreach (var commande in new[]
            {
                OuvrirPeriodeCommand, CloturerCommand, RouvrirCommand,
                RafraichirTotauxCommand, EnregistrerCommentaireCommand, SupprimerCommand
            })
            {
                var relay = commande as RelayCommand;
                if (relay != null) relay.RaiseCanExecuteChanged();
            }
        }

        private async Task ChargerAsync()
        {
            try
            {
                IsBusy = true;
                StatusMessage = "Chargement des périodes...";

                int idSelectionne = PeriodeSelectionnee != null ? PeriodeSelectionnee.Id : 0;

                var liste = await _periodeService.ChargerAsync();

                Periodes.Clear();
                foreach (var p in liste)
                    Periodes.Add(p);

                PeriodeSelectionnee = Periodes.FirstOrDefault(p => p.Id == idSelectionne);

                RafraichirTotaux();

                StatusMessage = Periodes.Count == 0
                    ? "Aucune période ouverte. Ouvrez le mois en cours pour commencer à suivre les clôtures."
                    : string.Format("{0} période(s) — {1} ouverte(s), {2} clôturée(s).",
                        Periodes.Count, NombrePeriodesOuvertes, NombrePeriodesCloturees);
            }
            catch (Exception ex)
            {
                StatusMessage = "Erreur lors du chargement : " + ex.Message;
            }
            finally
            {
                IsBusy = false;
            }
        }

        private async Task OuvrirAsync()
        {
            if (MoisAOuvrir == null)
            {
                StatusMessage = "Choisissez le mois à ouvrir.";
                return;
            }

            try
            {
                IsBusy = true;

                bool existait = await _periodeService.LirePeriodeAsync(AnneeAOuvrir, MoisAOuvrir.Numero) != null;

                var periode = await _periodeService.OuvrirAsync(AnneeAOuvrir, MoisAOuvrir.Numero);
                await ChargerAsync();

                PeriodeSelectionnee = Periodes.FirstOrDefault(p => p.Id == periode.Id);

                StatusMessage = existait
                    ? string.Format("La période {0} existait déjà ; elle est sélectionnée.", periode.Designation)
                    : string.Format("Période {0} ouverte.", periode.Designation);
            }
            catch (Exception ex)
            {
                StatusMessage = "Erreur lors de l'ouverture : " + ex.Message;
            }
            finally
            {
                IsBusy = false;
            }
        }

        private async Task RafraichirTotauxAsync()
        {
            if (PeriodeSelectionnee == null) return;

            try
            {
                IsBusy = true;

                await _periodeService.RafraichirTotauxAsync(PeriodeSelectionnee);

                StatusMessage = string.Format(
                    "{0} : {1} bulletin(s), {2:N0} FCFA de brut. Les totaux ne sont pas encore arrêtés.",
                    PeriodeSelectionnee.Designation,
                    PeriodeSelectionnee.NombreBulletins,
                    PeriodeSelectionnee.TotalBrut);
            }
            catch (Exception ex)
            {
                StatusMessage = "Erreur lors du calcul des totaux : " + ex.Message;
            }
            finally
            {
                IsBusy = false;
            }
        }

        private async Task CloturerAsync()
        {
            if (PeriodeSelectionnee == null) return;

            try
            {
                IsBusy = true;

                var periode = PeriodeSelectionnee;
                await _periodeService.CloturerAsync(periode, UtilisateurCourant());

                if (_auditService != null)
                {
                    await _auditService.AuditerPeriodeAsync(
                        action: "Clôture de période",
                        annee: periode.Annee,
                        mois: periode.Mois,
                        details: string.Format("Clôture définitive de la période {0} ({1} bulletins, Brut : {2:N0} FCFA, IUTS : {3:N0} FCFA)",
                            periode.Designation, periode.NombreBulletins, periode.TotalBrut, periode.TotalIuts),
                        utilisateur: UtilisateurCourant());
                }

                RafraichirTotaux();
                RafraichirCommandes();
                OnPropertyChanged(nameof(ConfirmationDemandee));

                StatusMessage = string.Format(
                    "{0} clôturée : {1} bulletin(s), {2:N0} FCFA de brut, {3:N0} FCFA d'IUTS. " +
                    "Ces totaux sont désormais figés et aucun bulletin de ce mois ne peut plus être enregistré.",
                    periode.Designation, periode.NombreBulletins, periode.TotalBrut, periode.TotalIuts);
            }
            catch (InvalidOperationException ex)
            {
                StatusMessage = ex.Message;
            }
            catch (Exception ex)
            {
                StatusMessage = "Erreur lors de la clôture : " + ex.Message;
            }
            finally
            {
                IsBusy = false;
            }
        }

        private async Task RouvrirAsync()
        {
            if (PeriodeSelectionnee == null) return;

            try
            {
                IsBusy = true;

                var periode = PeriodeSelectionnee;
                string motif = MotifReouverture;
                await _periodeService.RouvrirAsync(periode, MotifReouverture);

                if (_auditService != null)
                {
                    await _auditService.AuditerPeriodeAsync(
                        action: "Réouverture de période",
                        annee: periode.Annee,
                        mois: periode.Mois,
                        details: string.Format("Réouverture exceptionnelle de la période {0}. Motif : {1}",
                            periode.Designation, string.IsNullOrWhiteSpace(motif) ? "(non spécifié)" : motif),
                        utilisateur: UtilisateurCourant());
                }

                MotifReouverture = "";
                RafraichirTotaux();
                RafraichirCommandes();
                OnPropertyChanged(nameof(ConfirmationDemandee));

                StatusMessage = string.Format(
                    "{0} rouverte. Les totaux affichés datent de la clôture précédente : " +
                    "recalculez-les avant de clôturer à nouveau.", periode.Designation);
            }
            catch (InvalidOperationException ex)
            {
                StatusMessage = ex.Message;
            }
            catch (Exception ex)
            {
                StatusMessage = "Erreur lors de la réouverture : " + ex.Message;
            }
            finally
            {
                IsBusy = false;
            }
        }

        private async Task EnregistrerCommentaireAsync()
        {
            if (PeriodeSelectionnee == null) return;

            try
            {
                IsBusy = true;
                await _periodeService.EnregistrerCommentaireAsync(PeriodeSelectionnee);
                StatusMessage = "Commentaire enregistré.";
            }
            catch (Exception ex)
            {
                StatusMessage = "Erreur lors de l'enregistrement : " + ex.Message;
            }
            finally
            {
                IsBusy = false;
            }
        }

        private async Task SupprimerAsync()
        {
            if (PeriodeSelectionnee == null) return;

            try
            {
                IsBusy = true;

                string designation = PeriodeSelectionnee.Designation;
                await _periodeService.SupprimerAsync(PeriodeSelectionnee);
                await ChargerAsync();

                PeriodeSelectionnee = null;
                StatusMessage = string.Format("Période {0} supprimée.", designation);
            }
            catch (InvalidOperationException ex)
            {
                StatusMessage = ex.Message;
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

        private static string UtilisateurCourant()
        {
            try
            {
                string nom = Environment.UserName;
                return string.IsNullOrWhiteSpace(nom) ? "—" : nom;
            }
            catch
            {
                return "—";
            }
        }

        private void RafraichirTotaux()
        {
            OnPropertyChanged(nameof(NombrePeriodesOuvertes));
            OnPropertyChanged(nameof(NombrePeriodesCloturees));
        }
    }

    /// <summary>Un mois, avec son numéro et son nom français, pour les listes déroulantes.</summary>
    public class MoisOption
    {
        public int Numero { get; private set; }
        public string Nom { get; private set; }

        private MoisOption(int numero, string nom)
        {
            Numero = numero;
            Nom = nom;
        }

        private static readonly List<MoisOption> _tous = Construire();

        public static IEnumerable<MoisOption> Tous
        {
            get { return _tous; }
        }

        private static List<MoisOption> Construire()
        {
            var culture = System.Globalization.CultureInfo.GetCultureInfo("fr-FR");
            var liste = new List<MoisOption>();

            for (int mois = 1; mois <= 12; mois++)
            {
                string nom = culture.DateTimeFormat.GetMonthName(mois);
                if (nom.Length > 0)
                    nom = char.ToUpper(nom[0], culture) + nom.Substring(1);

                liste.Add(new MoisOption(mois, nom));
            }

            return liste;
        }

        public override string ToString()
        {
            return Nom;
        }
    }
}
