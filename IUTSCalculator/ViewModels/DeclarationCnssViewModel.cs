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
    /// Écran « Déclaration CNSS » (module DEC-04).
    ///
    /// L'écran ne demande que deux choses — la période et le rythme — puis construit la
    /// déclaration à partir de la paie déjà saisie. Le reste est du contrôle : matricules
    /// manquants, cotisations qui ne collent pas au barème de la période, mois non
    /// clôturés. Ce sont exactement les points sur lesquels la Caisse revient.
    /// </summary>
    public class DeclarationCnssViewModel : BaseViewModel
    {
        private readonly DeclarationCnssService _declarationService;
        private readonly DeclarationCnssExportService _exportService;
        private readonly Dossier _dossier;

        public DeclarationCnssViewModel(
            DeclarationCnssService declarationService,
            DeclarationCnssExportService exportService,
            Dossier dossier)
        {
            if (declarationService == null) throw new ArgumentNullException("declarationService");
            if (exportService == null) throw new ArgumentNullException("exportService");
            if (dossier == null) throw new ArgumentNullException("dossier");

            _declarationService = declarationService;
            _exportService = exportService;
            _dossier = dossier;

            Title = "Déclaration CNSS";

            // Par défaut, le mois précédent : c'est celui qu'on déclare.
            var reference = DateTime.Today.AddMonths(-1);
            _annee = reference.Year;
            _moisSelectionne = MoisDisponibles.FirstOrDefault(m => m.Numero == reference.Month);
            _trimestreSelectionne = DeclarationCnssService.Trimestre(reference.Month);
            _estMensuelle = true;

            GenererCommand = new RelayCommand(async () => await GenererAsync(), () => !IsBusy);
            ExporterCommand = new RelayCommand(Exporter, () => Declaration != null && !IsBusy);
            DeterminerPeriodiciteCommand = new RelayCommand(async () => await DeterminerPeriodiciteAsync(), () => !IsBusy);
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

        public IEnumerable<int> TrimestresDisponibles
        {
            get { return new[] { 1, 2, 3, 4 }; }
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

        private int _trimestreSelectionne;
        public int TrimestreSelectionne
        {
            get { return _trimestreSelectionne; }
            set { SetProperty(ref _trimestreSelectionne, value); }
        }

        private bool _estMensuelle;
        /// <summary>
        /// Rythme de la déclaration. Le choix est laissé à l'utilisateur — le seuil légal
        /// de 20 salariés s'apprécie sur l'effectif habituel, pas sur celui d'un mois où
        /// deux bulletins manquent — mais le bouton « Déterminer » propose le rythme que
        /// l'effectif constaté impose.
        /// </summary>
        public bool EstMensuelle
        {
            get { return _estMensuelle; }
            set
            {
                if (SetProperty(ref _estMensuelle, value))
                {
                    OnPropertyChanged(nameof(EstTrimestrielle));
                    OnPropertyChanged(nameof(PeriodeChoisieLibelle));
                }
            }
        }

        public bool EstTrimestrielle
        {
            get { return !EstMensuelle; }
            set { EstMensuelle = !value; }
        }

        public string PeriodeChoisieLibelle
        {
            get
            {
                if (EstMensuelle)
                {
                    return MoisSelectionne == null
                        ? string.Format("{0}", Annee)
                        : string.Format("{0} {1}", MoisSelectionne.Nom, Annee);
                }

                return string.Format("{0}ᵉ trimestre {1}", TrimestreSelectionne, Annee);
            }
        }

        // ── Résultat ─────────────────────────────────────────────────────────

        private DeclarationCnss _declaration;
        public DeclarationCnss Declaration
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

        public ObservableCollection<LigneDeclarationCnss> Lignes { get; } =
            new ObservableCollection<LigneDeclarationCnss>();

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
        public ICommand DeterminerPeriodiciteCommand { get; private set; }

        private void RafraichirCommandes()
        {
            foreach (var commande in new[] { GenererCommand, ExporterCommand, DeterminerPeriodiciteCommand })
            {
                var relay = commande as RelayCommand;
                if (relay != null) relay.RaiseCanExecuteChanged();
            }
        }

        /// <summary>
        /// Compte l'effectif du mois choisi et propose le rythme que la loi impose à cet
        /// effectif, sans l'appliquer d'autorité.
        /// </summary>
        private async Task DeterminerPeriodiciteAsync()
        {
            try
            {
                IsBusy = true;

                int mois = EstMensuelle && MoisSelectionne != null
                    ? MoisSelectionne.Numero
                    : DeclarationCnssService.MoisDuTrimestre(TrimestreSelectionne).First();

                int effectif = await _declarationService.EffectifDuMoisAsync(Annee, mois);
                var proposee = DeclarationCnssService.PeriodiciteSelonEffectif(effectif);

                if (effectif == 0)
                {
                    StatusMessage = "Aucun bulletin sur ce mois : l'effectif ne peut pas être constaté.";
                    return;
                }

                EstMensuelle = proposee == PeriodiciteDeclaration.Mensuelle;

                StatusMessage = string.Format(
                    "{0} salarié(s) constaté(s) : la déclaration est {1} (seuil légal : {2} salariés).",
                    effectif,
                    proposee == PeriodiciteDeclaration.Mensuelle ? "mensuelle" : "trimestrielle",
                    DeclarationCnss.SeuilEffectifMensuel);
            }
            catch (Exception ex)
            {
                StatusMessage = "Erreur lors du comptage de l'effectif : " + ex.Message;
            }
            finally
            {
                IsBusy = false;
            }
        }

        private async Task GenererAsync()
        {
            try
            {
                IsBusy = true;
                StatusMessage = "Construction de la déclaration...";

                DeclarationCnss declaration;

                if (EstMensuelle)
                {
                    if (MoisSelectionne == null)
                    {
                        StatusMessage = "Choisissez le mois à déclarer.";
                        return;
                    }

                    declaration = await _declarationService.ConstruireMensuelleAsync(
                        _dossier, Annee, MoisSelectionne.Numero);
                }
                else
                {
                    declaration = await _declarationService.ConstruireTrimestrielleAsync(
                        _dossier, Annee, TrimestreSelectionne);
                }

                Declaration = declaration;

                if (declaration.Lignes.Count == 0)
                {
                    StatusMessage = string.Format(
                        "Aucun bulletin sur {0} : il n'y a rien à déclarer.", declaration.PeriodeLibelle);
                    return;
                }

                StatusMessage = string.Format(
                    "{0} — {1} salarié(s), {2:N0} FCFA d'assiette, {3:N0} FCFA de cotisations. {4}",
                    declaration.PeriodeLibelle,
                    declaration.Effectif,
                    declaration.TotalAssiette,
                    declaration.TotalCotisations,
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
