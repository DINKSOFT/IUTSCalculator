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
    /// ViewModel pour l'écran « Journal d'audit et traçabilité » (module ADM-02).
    /// </summary>
    public class AuditViewModel : BaseViewModel
    {
        private readonly AuditService _auditService;
        private readonly AuditExportService _exportService;
        private readonly Dossier _dossier;

        public AuditViewModel(
            AuditService auditService,
            AuditExportService exportService,
            Dossier dossier)
        {
            if (auditService == null) throw new ArgumentNullException(nameof(auditService));
            if (exportService == null) throw new ArgumentNullException(nameof(exportService));
            if (dossier == null) throw new ArgumentNullException(nameof(dossier));

            _auditService = auditService;
            _exportService = exportService;
            _dossier = dossier;

            Title = "Journal d'audit";

            // Filtres par défaut : 30 derniers jours
            _dateDebut = DateTime.Today.AddDays(-30);
            _dateFin = DateTime.Today;

            UtilisateursDisponibles = new ObservableCollection<string> { "Tous les utilisateurs" };
            _utilisateurSelectionne = "Tous les utilisateurs";

            ModulesDisponibles = new List<string>
            {
                "Toutes les entités",
                "Salarié",
                "Période",
                "Variables",
                "Barèmes",
                "Calcul"
            };
            _moduleSelectionne = "Toutes les entités";

            ActionsDisponibles = new List<string>
            {
                "Toutes les actions",
                "Modifications salariales",
                "Clôtures de période",
                "Créations / Suppressions"
            };
            _actionSelectionnee = "Toutes les actions";

            FiltrerCommand = new RelayCommand(async () => await ChargerAsync(), () => !IsBusy);
            ReinitialiserCommand = new RelayCommand(async () => await ReinitialiserFiltresAsync(), () => !IsBusy);
            ExporterExcelCommand = new RelayCommand(ExporterExcel, () => AEntrees && !IsBusy);
            ExporterCsvCommand = new RelayCommand(ExporterCsv, () => AEntrees && !IsBusy);
        }

        // ── Propriétés de filtrage ───────────────────────────────────────────

        private DateTime? _dateDebut;
        public DateTime? DateDebut
        {
            get { return _dateDebut; }
            set { SetProperty(ref _dateDebut, value); }
        }

        private DateTime? _dateFin;
        public DateTime? DateFin
        {
            get { return _dateFin; }
            set { SetProperty(ref _dateFin, value); }
        }

        public ObservableCollection<string> UtilisateursDisponibles { get; }

        private string _utilisateurSelectionne;
        public string UtilisateurSelectionne
        {
            get { return _utilisateurSelectionne; }
            set { SetProperty(ref _utilisateurSelectionne, value); }
        }

        public List<string> ModulesDisponibles { get; }

        private string _moduleSelectionne;
        public string ModuleSelectionne
        {
            get { return _moduleSelectionne; }
            set { SetProperty(ref _moduleSelectionne, value); }
        }

        public List<string> ActionsDisponibles { get; }

        private string _actionSelectionnee;
        public string ActionSelectionnee
        {
            get { return _actionSelectionnee; }
            set { SetProperty(ref _actionSelectionnee, value); }
        }

        private string _rechercheTexte;
        public string RechercheTexte
        {
            get { return _rechercheTexte; }
            set { SetProperty(ref _rechercheTexte, value); }
        }

        // ── Données et résultats ─────────────────────────────────────────────

        public ObservableCollection<EntreeAudit> Entrees { get; } =
            new ObservableCollection<EntreeAudit>();

        private EntreeAudit _entreeSelectionnee;
        public EntreeAudit EntreeSelectionnee
        {
            get { return _entreeSelectionnee; }
            set
            {
                if (SetProperty(ref _entreeSelectionnee, value))
                {
                    OnPropertyChanged(nameof(AEntreeSelectionnee));
                }
            }
        }

        public bool AEntreeSelectionnee
        {
            get { return EntreeSelectionnee != null; }
        }

        public bool AEntrees
        {
            get { return Entrees.Count > 0; }
        }

        public int TotalEntrees
        {
            get { return Entrees.Count; }
        }

        public int TotalModificationsSalaires
        {
            get { return Entrees.Count(e => e.EstModificationFinanciere); }
        }

        public int TotalActionsSensibles
        {
            get { return Entrees.Count(e => e.EstActionSensible); }
        }

        public string DerniereActivite
        {
            get { return Entrees.FirstOrDefault()?.DateActionFormattee ?? "Aucun événement"; }
        }

        private string _messageStatut;
        public string MessageStatut
        {
            get { return _messageStatut; }
            private set { SetProperty(ref _messageStatut, value); }
        }

        // ── Commandes ────────────────────────────────────────────────────────

        public ICommand FiltrerCommand { get; }
        public ICommand ReinitialiserCommand { get; }
        public ICommand ExporterExcelCommand { get; }
        public ICommand ExporterCsvCommand { get; }

        protected override void OnIsActiveChanged()
        {
            base.OnIsActiveChanged();
            if (IsActive && Entrees.Count == 0)
            {
                _ = ChargerAsync();
            }
        }

        public async Task ChargerAsync()
        {
            IsBusy = true;
            MessageStatut = "Chargement du journal d'audit...";

            try
            {
                // Mettre à jour la liste des utilisateurs réels présents dans l'audit
                var users = await _auditService.ObtenirUtilisateursDistinctsAsync();
                string selectionActuelle = UtilisateurSelectionne;

                UtilisateursDisponibles.Clear();
                UtilisateursDisponibles.Add("Tous les utilisateurs");
                foreach (var u in users)
                {
                    UtilisateursDisponibles.Add(u);
                }

                if (UtilisateursDisponibles.Contains(selectionActuelle))
                    _utilisateurSelectionne = selectionActuelle;
                else
                    _utilisateurSelectionne = "Tous les utilisateurs";
                OnPropertyChanged(nameof(UtilisateurSelectionne));

                var filtre = new FiltreAudit
                {
                    DateDebut = DateDebut,
                    DateFin = DateFin,
                    Utilisateur = UtilisateurSelectionne,
                    Entite = ModuleSelectionne,
                    Action = ActionSelectionnee,
                    RechercheTexte = RechercheTexte,
                    Limite = 500
                };

                var liste = await _auditService.RechercherAsync(filtre);

                Entrees.Clear();
                foreach (var e in liste)
                {
                    Entrees.Add(e);
                }

                OnPropertyChanged(nameof(AEntrees));
                OnPropertyChanged(nameof(TotalEntrees));
                OnPropertyChanged(nameof(TotalModificationsSalaires));
                OnPropertyChanged(nameof(TotalActionsSensibles));
                OnPropertyChanged(nameof(DerniereActivite));

                MessageStatut = string.Format(
                    "{0} événement(s) trouvé(s) — Période : {1:dd/MM/yyyy} au {2:dd/MM/yyyy}",
                    Entrees.Count, DateDebut, DateFin);
            }
            catch (Exception ex)
            {
                MessageStatut = "Erreur lors du chargement : " + ex.Message;
            }
            finally
            {
                IsBusy = false;
                CommandManager.InvalidateRequerySuggested();
            }
        }

        public async Task ReinitialiserFiltresAsync()
        {
            _dateDebut = DateTime.Today.AddDays(-30);
            _dateFin = DateTime.Today;
            _utilisateurSelectionne = "Tous les utilisateurs";
            _moduleSelectionne = "Toutes les entités";
            _actionSelectionnee = "Toutes les actions";
            _rechercheTexte = string.Empty;

            OnPropertyChanged(nameof(DateDebut));
            OnPropertyChanged(nameof(DateFin));
            OnPropertyChanged(nameof(UtilisateurSelectionne));
            OnPropertyChanged(nameof(ModuleSelectionne));
            OnPropertyChanged(nameof(ActionSelectionnee));
            OnPropertyChanged(nameof(RechercheTexte));

            await ChargerAsync();
        }

        private void ExporterExcel()
        {
            if (!AEntrees) return;

            var dlg = new SaveFileDialog
            {
                Filter = "Classeur Excel (*.xlsx)|*.xlsx",
                FileName = string.Format("Journal_Audit_{0}_{1:yyyyMMdd}.xlsx",
                    (_dossier?.RaisonSociale ?? "Entreprise").Replace(" ", "_"), DateTime.Today)
            };

            if (dlg.ShowDialog() != true) return;

            IsBusy = true;
            MessageStatut = "Export Excel en cours...";

            Task.Run(async () =>
            {
                try
                {
                    var filtre = new FiltreAudit
                    {
                        DateDebut = DateDebut,
                        DateFin = DateFin,
                        Utilisateur = UtilisateurSelectionne,
                        Entite = ModuleSelectionne,
                        Action = ActionSelectionnee,
                        RechercheTexte = RechercheTexte
                    };

                    await _exportService.ExporterExcelAsync(Entrees, _dossier, filtre, dlg.FileName);

                    MessageStatut = "Journal d'audit exporté avec succès : " + Path.GetFileName(dlg.FileName);
                    if (File.Exists(dlg.FileName))
                    {
                        Process.Start(new ProcessStartInfo(dlg.FileName) { UseShellExecute = true });
                    }
                }
                catch (Exception ex)
                {
                    MessageStatut = "Erreur lors de l'export Excel : " + ex.Message;
                }
                finally
                {
                    IsBusy = false;
                }
            });
        }

        private void ExporterCsv()
        {
            if (!AEntrees) return;

            var dlg = new SaveFileDialog
            {
                Filter = "Fichier CSV (*.csv)|*.csv",
                FileName = string.Format("Journal_Audit_{0}_{1:yyyyMMdd}.csv",
                    (_dossier?.RaisonSociale ?? "Entreprise").Replace(" ", "_"), DateTime.Today)
            };

            if (dlg.ShowDialog() != true) return;

            IsBusy = true;
            MessageStatut = "Export CSV en cours...";

            Task.Run(async () =>
            {
                try
                {
                    await _exportService.ExporterCsvAsync(Entrees, dlg.FileName);
                    MessageStatut = "Fichier CSV exporté avec succès : " + Path.GetFileName(dlg.FileName);
                    if (File.Exists(dlg.FileName))
                    {
                        Process.Start(new ProcessStartInfo(dlg.FileName) { UseShellExecute = true });
                    }
                }
                catch (Exception ex)
                {
                    MessageStatut = "Erreur lors de l'export CSV : " + ex.Message;
                }
                finally
                {
                    IsBusy = false;
                }
            });
        }
    }
}
