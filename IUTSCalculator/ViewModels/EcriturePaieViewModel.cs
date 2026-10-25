using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
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
            ExporterSageCommand = new RelayCommand(ExporterSage, () => PieceComptable != null && !IsBusy);
            EnregistrerParametresCommand = new RelayCommand(async () => await EnregistrerParametresAsync(),
                () => Parametres != null && ParametresEnregistrables && !IsBusy);
            RetablirParametresCommand = new RelayCommand(RetablirParametres, () => Parametres != null && !IsBusy);
            AnnulerParametresCommand = new RelayCommand(async () => await ChargerParametresAsync(true), () => !IsBusy);

            _ = ChargerParametresAsync(false);
        }

        /// <summary>
        /// Revenir sur l'écran relit les paramètres comptables (ils peuvent avoir changé
        /// depuis un autre poste), sauf saisie en cours, qui serait perdue.
        /// </summary>
        protected override void OnIsActiveChanged()
        {
            base.OnIsActiveChanged();
            if (IsActive && !ParametresModifies)
                _ = ChargerParametresAsync(false);
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
                    OnPropertyChanged(nameof(ApercuParametres));
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
                    OnPropertyChanged(nameof(ApercuParametres));
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
                    OnPropertyChanged(nameof(EnteteJournal));
                    OnPropertyChanged(nameof(LongueurComptesLibelle));
                    RafraichirCommandes();
                }
            }
        }

        /// <summary>En-tête du tableau : « JOURNAL PAIE — JOURNAL DE PAIE ».</summary>
        public string EnteteJournal
        {
            get
            {
                if (PieceComptable != null) return PieceComptable.EnteteJournal;
                var p = Parametres ?? new ParametresComptables();
                return new PieceComptable { CodeJournal = p.CodeJournal, LibelleJournal = p.LibelleJournal }.EnteteJournal;
            }
        }

        public string LongueurComptesLibelle
        {
            get { return PieceComptable != null ? PieceComptable.LongueurComptesLibelle : ""; }
        }

        // ── Paramètres comptables du dossier (défaut 20) ─────────────────────

        private ParametresComptables _parametres;

        /// <summary>
        /// Copie de travail des paramètres comptables, liée à l'onglet « Paramètres
        /// comptables ». Rien n'est écrit en base avant « Enregistrer les paramètres ».
        /// </summary>
        public ParametresComptables Parametres
        {
            get { return _parametres; }
            private set
            {
                var precedent = _parametres;
                if (SetProperty(ref _parametres, value))
                {
                    if (precedent != null) precedent.PropertyChanged -= ParametreModifie;
                    if (value != null) value.PropertyChanged += ParametreModifie;
                    ParametresModifies = false;
                    OnPropertyChanged(nameof(ApercuParametres));
                    OnPropertyChanged(nameof(EnteteJournal));
                    RafraichirCommandes();
                }
            }
        }

        private void ParametreModifie(object sender, PropertyChangedEventArgs e)
        {
            ParametresModifies = true;
            OnPropertyChanged(nameof(ApercuParametres));
        }

        private bool _parametresModifies;
        public bool ParametresModifies
        {
            get { return _parametresModifies; }
            private set { SetProperty(ref _parametresModifies, value); }
        }

        private bool _parametresEnregistrables = true;
        /// <summary>Faux tant que la base du dossier n'a pas la table ParametresComptables (schéma v38).</summary>
        public bool ParametresEnregistrables
        {
            get { return _parametresEnregistrables; }
            private set
            {
                if (SetProperty(ref _parametresEnregistrables, value))
                {
                    OnPropertyChanged(nameof(ParametresNonEnregistrables));
                    RafraichirCommandes();
                }
            }
        }

        public bool ParametresNonEnregistrables
        {
            get { return !ParametresEnregistrables; }
        }

        private string _messageParametres;
        public string MessageParametres
        {
            get { return _messageParametres; }
            private set { SetProperty(ref _messageParametres, value); }
        }

        /// <summary>Aperçu, pour la période choisie, du journal, de la pièce et de la référence obtenus.</summary>
        public string ApercuParametres
        {
            get
            {
                if (Parametres == null) return "";
                var fin = FinPeriodeChoisie();
                string longueur = Parametres.LongueurComptes > 0
                    ? string.Format("comptes sur {0} caractères", Parametres.LongueurComptes)
                    : "longueur des comptes automatique (celle du plus long compte utilisé)";
                return string.Format(
                    "Pour {0} : journal « {1} », pièce « {2} », référence « {3} » ; {4}.",
                    PeriodeChoisieLibelle,
                    (Parametres.CodeJournal ?? "").Trim(),
                    Parametres.NumeroPiece(fin),
                    Parametres.Reference(fin),
                    longueur);
            }
        }

        private DateTime FinPeriodeChoisie()
        {
            int mois = MoisSelectionne != null ? MoisSelectionne.Numero : DateTime.Today.Month;
            int annee = Annee >= 1 && Annee <= 9999 ? Annee : DateTime.Today.Year;
            return new DateTime(annee, mois, 1).AddMonths(1).AddDays(-1);
        }

        private async Task ChargerParametresAsync(bool annulation)
        {
            try
            {
                ParametresEnregistrables = await _ecritureService.ParametresEnregistrablesAsync();
                Parametres = await _ecritureService.ChargerParametresAsync();
                MessageParametres = ParametresEnregistrables
                    ? (annulation ? "Modifications abandonnées : paramètres enregistrés rechargés." : "")
                    : "La base de ce dossier n'a pas encore reçu la mise à niveau des paramètres comptables (schéma v38) : " +
                      "les valeurs d'origine sont utilisées et ne peuvent pas être modifiées.";
            }
            catch (Exception ex)
            {
                if (Parametres == null) Parametres = new ParametresComptables();
                MessageParametres = "Paramètres comptables illisibles, valeurs d'origine utilisées : " + ex.Message;
            }
        }

        private async Task EnregistrerParametresAsync()
        {
            if (Parametres == null) return;

            try
            {
                if (UserSession.Instance.IsAuthenticated && !UserSession.Instance.CanEdit)
                {
                    MessageParametres = "Votre profil ne permet pas de modifier les paramètres comptables.";
                    return;
                }

                IsBusy = true;

                var anomalies = Parametres.Anomalies(FinPeriodeChoisie());
                if (anomalies.Count > 0)
                {
                    MessageParametres = "Paramètres non enregistrés — " + string.Join(" ", anomalies);
                    return;
                }

                int modifies = await _ecritureService.EnregistrerParametresAsync(Parametres);
                Parametres = await _ecritureService.ChargerParametresAsync();

                MessageParametres = modifies == 0
                    ? "Aucun paramètre modifié."
                    : string.Format("{0} paramètre(s) comptable(s) enregistré(s) et journalisé(s) dans le journal d'audit.", modifies);

                // La pièce affichée a été construite avec les anciens comptes : la régénérer.
                if (PieceComptable != null && modifies > 0)
                {
                    IsBusy = false;
                    await GenererAsync();
                }
            }
            catch (Exception ex)
            {
                MessageParametres = "Erreur lors de l'enregistrement des paramètres : " + ex.Message;
            }
            finally
            {
                IsBusy = false;
            }
        }

        /// <summary>
        /// Remet dans le formulaire les valeurs d'origine (celles que le programme utilisait
        /// avant le paramétrage) ; il reste à les enregistrer.
        /// </summary>
        private void RetablirParametres()
        {
            if (Parametres == null) return;
            Parametres.RetablirValeursInitiales();
            MessageParametres = "Valeurs d'origine rétablies dans le formulaire : cliquez sur « Enregistrer les paramètres » pour les appliquer.";
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
        public ICommand ExporterSageCommand { get; }
        public ICommand EnregistrerParametresCommand { get; }
        public ICommand RetablirParametresCommand { get; }
        public ICommand AnnulerParametresCommand { get; }

        private void RafraichirCommandes()
        {
            foreach (var cmd in new[]
            {
                GenererCommand, ExporterExcelCommand, ExporterTxtCommand, ExporterSageCommand,
                EnregistrerParametresCommand, RetablirParametresCommand, AnnulerParametresCommand
            })
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
                StatusMessage = EcriturePaieExportService.ImportSagePossible(PieceComptable)
                    ? string.Format("Pièce comptable exportée vers Excel (avec l'onglet « Import Sage ») : {0}", chemin)
                    : string.Format("Pièce comptable exportée vers Excel : {0} — onglet « Import Sage » non ajouté : la pièce n'est pas importable en l'état (voir « Export Sage 100c »).", chemin);

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

        /// <summary>
        /// Fichier d'import Sage 100c Comptabilité (point-virgule, Windows-1252), au format
        /// JO_Num | EC_Reference | Date | EC_Piece | CG_Num | CT_Num | EC_Intitule | Sens | Montant.
        /// Refusé tant que la pièce n'est pas équilibrée.
        /// </summary>
        private void ExporterSage()
        {
            if (PieceComptable == null) return;

            try
            {
                IsBusy = true;

                var erreurs = EcriturePaieExportService.ControlerImportSage(
                    PieceComptable, EcriturePaieExportService.ConstruireImportSage(PieceComptable));
                if (erreurs.Count > 0)
                {
                    StatusMessage = "Export Sage refusé : " + string.Join(" ", erreurs);
                    return;
                }

                var dlg = new SaveFileDialog
                {
                    FileName = PieceComptable.NomFichierSage,
                    DefaultExt = ".txt",
                    Filter = "Fichier d'import Sage (*.txt;*.csv)|*.txt;*.csv|Tous les fichiers (*.*)|*.*"
                };

                if (dlg.ShowDialog() != true)
                {
                    StatusMessage = "Export Sage annulé.";
                    return;
                }

                _exportService.ExporterSage(PieceComptable, dlg.FileName);
                StatusMessage = string.Format(
                    "Fichier d'import Sage 100c généré : {0} — journal {1}, pièce {2}, {3} ligne(s), total D = total C = {4} FCFA.",
                    dlg.FileName, PieceComptable.CodeJournal, PieceComptable.NumeroPiece,
                    EcriturePaieExportService.ConstruireImportSage(PieceComptable).Count,
                    EcriturePaieExportService.FormaterMontant(PieceComptable.TotalDebit));
            }
            catch (InvalidOperationException ex)
            {
                StatusMessage = ex.Message;
            }
            catch (Exception ex)
            {
                StatusMessage = "Erreur lors de l'export Sage : " + ex.Message;
            }
            finally
            {
                IsBusy = false;
            }
        }
    }
}
