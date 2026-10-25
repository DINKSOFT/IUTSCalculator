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
    ///
    /// Défaut 21 du guide de formation 1.4.37.1 :
    ///   • plus de plafond silencieux à 500 lignes : le journal se lit par pages de
    ///     <see cref="TaillePage"/> entrées, le nombre total du filtre est affiché, les
    ///     cartes comptent tout le filtre et les exports sortent TOUT le filtre ;
    ///   • le badge « journal scellé » repose désormais sur un vrai chaînage d'empreintes,
    ///     contrôlé par « Vérifier l'intégrité ».
    /// </summary>
    public class AuditViewModel : BaseViewModel
    {
        /// <summary>Nombre d'entrées par page à l'écran.</summary>
        public const int TaillePage = 500;

        private static readonly string[] ModulesDeBase =
        {
            "Toutes les entités",
            AuditService.EntiteSalarie,
            AuditService.EntitePeriode,
            AuditService.EntiteVariables,
            AuditService.EntiteBaremes,
            AuditService.EntiteCalcul
        };

        private readonly AuditService _auditService;
        private readonly AuditExportService _exportService;
        private readonly Dossier _dossier;

        /// <summary>Filtre de la dernière recherche : la pagination et les exports s'y tiennent.</summary>
        private FiltreAudit _filtreApplique;

        /// <summary>N° de la première entrée altérée trouvée par la dernière vérification.</summary>
        private long? _premiereEntreeAlteree;

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

            ModulesDisponibles = new ObservableCollection<string>(ModulesDeBase);
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
            ExporterExcelCommand = new RelayCommand(async () => await ExporterExcelAsync(), () => AEntrees && !IsBusy);
            ExporterCsvCommand = new RelayCommand(async () => await ExporterCsvAsync(), () => AEntrees && !IsBusy);
            PagePrecedenteCommand = new RelayCommand(async () => await AllerALaPageAsync(PageCourante - 1), () => !IsBusy && PageCourante > 1);
            PageSuivanteCommand = new RelayCommand(async () => await AllerALaPageAsync(PageCourante + 1), () => !IsBusy && PageCourante < NombrePages);
            VerifierIntegriteCommand = new RelayCommand(async () => await VerifierIntegriteAsync(), () => !IsBusy);
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

        /// <summary>
        /// Entités proposées : les entités de base, puis toutes celles réellement présentes
        /// dans le journal (Discipline, ConventionCollective…), qui manquaient à la liste.
        /// </summary>
        public ObservableCollection<string> ModulesDisponibles { get; }

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

        /// <summary>Entrées de la page affichée.</summary>
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

        private StatistiquesAudit _statistiques = new StatistiquesAudit();

        /// <summary>Nombre total d'entrées du filtre (toutes pages confondues).</summary>
        public int TotalEntrees
        {
            get { return _statistiques.Total; }
        }

        public int TotalModificationsSalaires
        {
            get { return _statistiques.ModificationsFinancieres; }
        }

        public int TotalActionsSensibles
        {
            get { return _statistiques.ActionsSensibles; }
        }

        public string DerniereActivite
        {
            get
            {
                return _statistiques.DerniereActivite.HasValue
                    ? _statistiques.DerniereActivite.Value.ToString("dd/MM/yyyy HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture)
                    : "Aucun événement";
            }
        }

        // ── Pagination ───────────────────────────────────────────────────────

        private int _pageCourante = 1;
        public int PageCourante
        {
            get { return _pageCourante; }
            private set { SetProperty(ref _pageCourante, value); }
        }

        public int NombrePages
        {
            get { return Math.Max(1, (TotalEntrees + TaillePage - 1) / TaillePage); }
        }

        public bool APlusieursPages
        {
            get { return TotalEntrees > TaillePage; }
        }

        /// <summary>« Entrées 1 à 500 sur 1 234 — page 1 / 3 ».</summary>
        public string PaginationLibelle
        {
            get
            {
                if (TotalEntrees == 0) return "Aucune entrée";
                int premiere = (PageCourante - 1) * TaillePage + 1;
                int derniere = premiere + Entrees.Count - 1;
                return string.Format("Entrées {0} à {1} sur {2} — page {3} / {4}",
                    premiere, derniere, TotalEntrees, PageCourante, NombrePages);
            }
        }

        // ── Scellement ───────────────────────────────────────────────────────

        private bool _journalScelle = true;
        /// <summary>Faux tant que la base n'a pas les colonnes d'empreinte (schéma v38).</summary>
        public bool JournalScelle
        {
            get { return _journalScelle; }
            private set
            {
                if (SetProperty(ref _journalScelle, value))
                    NotifierBadges();
            }
        }

        private ResultatIntegriteAudit _resultatIntegrite;
        public ResultatIntegriteAudit ResultatIntegrite
        {
            get { return _resultatIntegrite; }
            private set
            {
                if (SetProperty(ref _resultatIntegrite, value))
                {
                    OnPropertyChanged(nameof(AResultatIntegrite));
                    OnPropertyChanged(nameof(MessageIntegrite));
                    NotifierBadges();
                }
            }
        }

        public bool AResultatIntegrite
        {
            get { return ResultatIntegrite != null; }
        }

        public string MessageIntegrite
        {
            get { return ResultatIntegrite != null ? ResultatIntegrite.Message : ""; }
        }

        /// <summary>Badge neutre : journal scellé, pas encore vérifié dans cette session.</summary>
        public bool BadgeScelleNonVerifie
        {
            get { return JournalScelle && ResultatIntegrite == null; }
        }

        public bool BadgeIntegriteVerifiee
        {
            get { return JournalScelle && ResultatIntegrite != null && ResultatIntegrite.EstIntegre; }
        }

        public bool BadgeJournalAltere
        {
            get { return JournalScelle && ResultatIntegrite != null && ResultatIntegrite.ScellementActif && !ResultatIntegrite.EstIntegre; }
        }

        public bool BadgeNonScelle
        {
            get { return !JournalScelle; }
        }

        private void NotifierBadges()
        {
            OnPropertyChanged(nameof(BadgeScelleNonVerifie));
            OnPropertyChanged(nameof(BadgeIntegriteVerifiee));
            OnPropertyChanged(nameof(BadgeJournalAltere));
            OnPropertyChanged(nameof(BadgeNonScelle));
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
        public ICommand PagePrecedenteCommand { get; }
        public ICommand PageSuivanteCommand { get; }
        public ICommand VerifierIntegriteCommand { get; }

        protected override void OnIsActiveChanged()
        {
            base.OnIsActiveChanged();
            // La condition Entrees.Count == 0 limitait le rechargement automatique à la
            // toute première visite de l'écran : revenir plus tard sur le journal d'audit
            // affichait une liste figée au moment de ce premier chargement, alors même que
            // c'est l'écran où la fraîcheur des données compte le plus — signalé le 16
            // septembre 2026 (voir ETAT-DEVELOPPEMENT.md item 22). ChargerAsync() ne fait
            // que rejouer les filtres actuels, sans état de saisie à perdre.
            if (IsActive)
            {
                _ = ChargerAsync();
            }
        }

        private FiltreAudit FiltreDepuisEcran()
        {
            return new FiltreAudit
            {
                DateDebut = DateDebut,
                DateFin = DateFin,
                Utilisateur = UtilisateurSelectionne,
                Entite = ModuleSelectionne,
                Action = ActionSelectionnee,
                RechercheTexte = RechercheTexte,
                Limite = TaillePage,
                Decalage = 0
            };
        }

        /// <summary>Applique les filtres de l'écran et affiche la première page.</summary>
        public async Task ChargerAsync()
        {
            IsBusy = true;
            MessageStatut = "Chargement du journal d'audit...";

            try
            {
                await RafraichirListesAsync();
                JournalScelle = await _auditService.EstScelleAsync();

                _filtreApplique = FiltreDepuisEcran();
                // Borne figée : les pages et le total restent cohérents même si d'autres
                // entrées sont enregistrées pendant la consultation (« Filtrer » les inclut).
                _filtreApplique.IdMax = await _auditService.DernierIdAsync();
                _statistiques = await _auditService.StatistiquesAsync(_filtreApplique) ?? new StatistiquesAudit();
                PageCourante = 1;
                await ChargerPageAsync();
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

        /// <summary>Met à jour les listes d'opérateurs et d'entités réellement présents dans le journal.</summary>
        private async Task RafraichirListesAsync()
        {
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

            var modules = await _auditService.ObtenirModulesDistinctsAsync();
            foreach (var m in modules)
            {
                if (!string.IsNullOrWhiteSpace(m) && !ModulesDisponibles.Contains(m))
                    ModulesDisponibles.Add(m);
            }
        }

        private async Task AllerALaPageAsync(int page)
        {
            if (_filtreApplique == null) return;
            if (page < 1 || page > NombrePages) return;

            IsBusy = true;
            try
            {
                PageCourante = page;
                await ChargerPageAsync();
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

        private async Task ChargerPageAsync()
        {
            var filtre = _filtreApplique.Copier();
            filtre.Limite = TaillePage;
            filtre.Decalage = (PageCourante - 1) * TaillePage;

            var liste = await _auditService.RechercherAsync(filtre);

            Entrees.Clear();
            foreach (var e in liste)
            {
                if (_premiereEntreeAlteree.HasValue && e.Id == _premiereEntreeAlteree.Value)
                    e.EstAlteree = true;
                Entrees.Add(e);
            }

            OnPropertyChanged(nameof(AEntrees));
            OnPropertyChanged(nameof(TotalEntrees));
            OnPropertyChanged(nameof(TotalModificationsSalaires));
            OnPropertyChanged(nameof(TotalActionsSensibles));
            OnPropertyChanged(nameof(DerniereActivite));
            OnPropertyChanged(nameof(PageCourante));
            OnPropertyChanged(nameof(NombrePages));
            OnPropertyChanged(nameof(APlusieursPages));
            OnPropertyChanged(nameof(PaginationLibelle));

            MessageStatut = string.Format(
                "{0} événement(s) trouvé(s) — Période : {1:dd/MM/yyyy} au {2:dd/MM/yyyy}{3}",
                TotalEntrees, _filtreApplique.DateDebut, _filtreApplique.DateFin,
                APlusieursPages
                    ? string.Format(" — {0} : les exports sortent les {1} entrées du filtre.", PaginationLibelle, TotalEntrees)
                    : "");
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

        /// <summary>
        /// Recalcule toute la chaîne d'empreintes du journal (pas seulement la page ni le
        /// filtre affichés) et signale la première entrée altérée.
        /// </summary>
        private async Task VerifierIntegriteAsync()
        {
            IsBusy = true;
            MessageStatut = "Vérification de l'intégrité du journal d'audit...";

            try
            {
                var resultat = await _auditService.VerifierIntegriteAsync();
                JournalScelle = resultat.ScellementActif;
                _premiereEntreeAlteree = resultat.EstIntegre ? (long?)null : resultat.PremiereEntreeAlteree;
                ResultatIntegrite = resultat;

                if (_filtreApplique != null)
                    await ChargerPageAsync();

                MessageStatut = resultat.Message;
            }
            catch (Exception ex)
            {
                MessageStatut = "Erreur lors de la vérification de l'intégrité : " + ex.Message;
            }
            finally
            {
                IsBusy = false;
                CommandManager.InvalidateRequerySuggested();
            }
        }

        /// <summary>Toutes les entrées du filtre appliqué, sans limite (exports).</summary>
        private async Task<List<EntreeAudit>> ToutesLesEntreesDuFiltreAsync()
        {
            var filtre = (_filtreApplique ?? FiltreDepuisEcran()).Copier();
            filtre.Limite = 0;
            filtre.Decalage = 0;

            var toutes = await _auditService.RechercherAsync(filtre);
            if (_premiereEntreeAlteree.HasValue)
            {
                foreach (var e in toutes)
                {
                    if (e.Id == _premiereEntreeAlteree.Value) e.EstAlteree = true;
                }
            }
            return toutes;
        }

        private async Task ExporterExcelAsync()
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

            try
            {
                var toutes = await ToutesLesEntreesDuFiltreAsync();
                string tete = await _auditService.ObtenirEmpreinteDeTeteAsync();

                await _exportService.ExporterExcelAsync(toutes, _dossier, _filtreApplique ?? FiltreDepuisEcran(), dlg.FileName, tete);

                MessageStatut = string.Format("Journal d'audit exporté avec succès ({0} entrée(s)) : {1}",
                    toutes.Count, Path.GetFileName(dlg.FileName));
                if (File.Exists(dlg.FileName))
                {
                    try { Process.Start(new ProcessStartInfo(dlg.FileName) { UseShellExecute = true }); }
                    catch { }
                }
            }
            catch (Exception ex)
            {
                MessageStatut = "Erreur lors de l'export Excel : " + ex.Message;
            }
            finally
            {
                IsBusy = false;
                CommandManager.InvalidateRequerySuggested();
            }
        }

        private async Task ExporterCsvAsync()
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

            try
            {
                var toutes = await ToutesLesEntreesDuFiltreAsync();
                await _exportService.ExporterCsvAsync(toutes, dlg.FileName);

                MessageStatut = string.Format("Fichier CSV exporté avec succès ({0} entrée(s)) : {1}",
                    toutes.Count, Path.GetFileName(dlg.FileName));
                if (File.Exists(dlg.FileName))
                {
                    try { Process.Start(new ProcessStartInfo(dlg.FileName) { UseShellExecute = true }); }
                    catch { }
                }
            }
            catch (Exception ex)
            {
                MessageStatut = "Erreur lors de l'export CSV : " + ex.Message;
            }
            finally
            {
                IsBusy = false;
                CommandManager.InvalidateRequerySuggested();
            }
        }
    }
}
