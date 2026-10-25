using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using System.Windows.Input;
using IUTSCalculator.Models;
using IUTSCalculator.Services;
using IUTSCalculator.Helpers;
using System.Linq;
using System.Windows;
using Microsoft.Win32;
using ClosedXML.Excel;

namespace IUTSCalculator.ViewModels
{
    /// <summary>
    /// ViewModel pour le calcul en lot des salaires d'une liste d'employés
    /// </summary>
    public class BatchSalaryCalculationViewModel : BaseViewModel
    {
        private readonly DataService _dataService;
        private readonly TaxCalculatorService _taxCalculatorService;
        private readonly ChaineBulletinService _chaine;

        // Variables du mois (PAI-02), absences (PAI-04) et prêts (PAI-05). Facultatifs :
        // sans eux, le calcul est celui d'avant ces modules.
        private readonly VariablePaieService _variablePaieService;
        private readonly RubriqueService _rubriqueService;
        private readonly AbsenceService _absenceService;
        private readonly PretService _pretService;
        private readonly ProfilPaieService _profilPaieService;
        private readonly BaremeService _baremeService;

        // Heures supplémentaires (PAI-03), convention collective (SOC-05), rappels et
        // régularisations (PAI-07), avantages en nature (PAI-08). Facultatifs, comme les
        // autres : mêmes entrées que sur le bulletin, sans quoi cet écran et le bulletin
        // donneraient deux résultats différents pour le même salarié et le même mois.
        private readonly HeureSupplementaireService _heureSupplementaireService;
        private readonly ConventionCollectiveService _conventionCollectiveService;
        private readonly RappelPaieService _rappelPaieService;
        private readonly AvantageNatureService _avantageNatureService;

        // Périodes de paie et clôture (PAI-01). Facultatif : sans lui, aucun avertissement
        // n'est affiché sur les mois jamais ouverts — comme avant ce contrôle.
        private readonly PeriodePaieService _periodePaieService;

        public ObservableCollection<Employee> Employees { get; } = new ObservableCollection<Employee>();
        public ObservableCollection<TaxCalculation> Calculations { get; } = new ObservableCollection<TaxCalculation>();

        private string _statusMessage;
        public new string StatusMessage
        {
            get => _statusMessage;
            set => SetProperty(ref _statusMessage, value);
        }

        private bool _isBusy;
        public new bool IsBusy
        {
            get => _isBusy;
            set
            {
                if (SetProperty(ref _isBusy, value))
                {
                    (CalculateAllCommand as RelayCommand)?.RaiseCanExecuteChanged();
                    (ExportAllCommand as RelayCommand)?.RaiseCanExecuteChanged();
                    (GenerateExcelTableCommand as RelayCommand)?.RaiseCanExecuteChanged();
                }
            }
        }

        /// <summary>
        /// Période traitée. C'était un simple libellé saisi à la main (« Janvier 2026 »),
        /// sans effet sur le calcul ni sur l'enregistrement : le mois et l'année sont
        /// maintenant des valeurs, parce que c'est d'elles que dépendent les variables du
        /// mois, les absences, et la période sous laquelle la paie est enregistrée.
        /// </summary>
        private int _annee = DateTime.Today.Year;
        public int Annee
        {
            get => _annee;
            set
            {
                if (SetProperty(ref _annee, value))
                    OnPropertyChanged(nameof(Periode));
            }
        }

        private MoisOption _moisSelectionne =
            MoisOption.Tous.FirstOrDefault(m => m.Numero == DateTime.Today.Month);
        public MoisOption MoisSelectionne
        {
            get => _moisSelectionne;
            set
            {
                if (SetProperty(ref _moisSelectionne, value))
                    OnPropertyChanged(nameof(Periode));
            }
        }

        public IEnumerable<MoisOption> MoisDisponibles => MoisOption.Tous;

        public IEnumerable<int> AnneesDisponibles =>
            Enumerable.Range(DateTime.Today.Year - 5, 7).ToList();

        public int Mois => MoisSelectionne != null ? MoisSelectionne.Numero : DateTime.Today.Month;

        /// <summary>Dernier jour du mois traité : la date sous laquelle la paie est enregistrée.</summary>
        public DateTime PeriodeDate => new DateTime(Annee, Mois, 1).AddMonths(1).AddDays(-1);

        /// <summary>Libellé de la période, pour les en-têtes et les exports.</summary>
        public string Periode =>
            new DateTime(Annee, Mois, 1).ToString("MMMM yyyy", new System.Globalization.CultureInfo("fr-FR"));

        public ICommand LoadEmployeesCommand { get; }
        public ICommand CalculateAllCommand { get; }
        public ICommand ExportAllCommand { get; }
        public ICommand GenerateExcelTableCommand { get; }

        public BatchSalaryCalculationViewModel(
            DataService dataService,
            TaxCalculatorService taxCalculatorService,
            VariablePaieService variablePaieService = null,
            RubriqueService rubriqueService = null,
            AbsenceService absenceService = null,
            PretService pretService = null,
            ProfilPaieService profilPaieService = null,
            BaremeService baremeService = null,
            HeureSupplementaireService heureSupplementaireService = null,
            ConventionCollectiveService conventionCollectiveService = null,
            RappelPaieService rappelPaieService = null,
            AvantageNatureService avantageNatureService = null,
            PeriodePaieService periodePaieService = null,
            SaisieOppositionService saisieOppositionService = null,
            BulletinService bulletinService = null)
        {
            _dataService = dataService ?? throw new ArgumentNullException(nameof(dataService));
            _taxCalculatorService = taxCalculatorService ?? throw new ArgumentNullException(nameof(taxCalculatorService));
            _variablePaieService = variablePaieService;
            _rubriqueService = rubriqueService;
            _absenceService = absenceService;
            _pretService = pretService;
            _profilPaieService = profilPaieService;
            _baremeService = baremeService;
            _heureSupplementaireService = heureSupplementaireService;
            _conventionCollectiveService = conventionCollectiveService;
            _rappelPaieService = rappelPaieService;
            _avantageNatureService = avantageNatureService;
            _periodePaieService = periodePaieService;

            // Chaîne commune avec l'écran « Bulletin de paie » (défaut n° 4 du guide) : le
            // calcul en lot produit désormais de vrais bulletins archivés, saisies comprises.
            _chaine = new ChaineBulletinService(
                _dataService, bulletinService ?? new BulletinService(_taxCalculatorService),
                _baremeService, _rubriqueService, _variablePaieService, _absenceService, _pretService,
                _profilPaieService, _heureSupplementaireService, _conventionCollectiveService,
                _rappelPaieService, _avantageNatureService, saisieOppositionService);

            // Le titre alimente le message de la barre d'état et signale l'écran
            // actif dans le rail de navigation : sans lui, ni l'un ni l'autre.
            Title = "Calcul de salaires en lot";

            LoadEmployeesCommand = new RelayCommand(async () => await LoadEmployeesAsync(), () => !IsBusy);
            CalculateAllCommand = new RelayCommand(async () => await CalculateAllAsync(), () => !IsBusy && Employees.Any());
            ExportAllCommand = new RelayCommand(async () => await ExportAllAsync(), () => !IsBusy && Calculations.Any());
            GenerateExcelTableCommand = new RelayCommand(async () => await GenerateExcelSalaryTableAsync(), () => !IsBusy && Calculations.Any());
        }

        /// <summary>
        /// Ce ViewModel n'est créé qu'une seule fois (voir MainViewModel) : sans ce
        /// rafraîchissement automatique, la liste Employees restait celle du dernier clic sur
        /// « Charger les employés », potentiellement périmée si une fiche a été modifiée
        /// ailleurs depuis (signalé le 16 septembre 2026, voir ETAT-DEVELOPPEMENT.md item 21).
        /// « Charger les employés » reste disponible pour un rafraîchissement manuel.
        /// </summary>
        protected override void OnIsActiveChanged()
        {
            if (IsActive)
                _ = LoadEmployeesAsync();
        }

        private async Task LoadEmployeesAsync()
        {
            try
            {
                IsBusy = true;
                StatusMessage = "Chargement des employés...";
                Employees.Clear();
                var list = await _dataService.LoadEmployeesAsync();
                // Le calcul en lot est une paie de masse : un employé sorti (ou suspendu)
                // n'a plus vocation à y figurer — son solde de tout compte se traite via
                // l'écran dédié, pas via un bulletin mensuel ordinaire (signalé le
                // 16 septembre 2026, voir ETAT-DEVELOPPEMENT.md item 20).
                foreach (var emp in list.Where(e => e.EstActif))
                    Employees.Add(emp);
                StatusMessage = $"{Employees.Count} employés actifs chargés.";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Erreur lors du chargement : {ex.Message}";
            }
            finally
            {
                IsBusy = false;
            }
        }

        private async Task CalculateAllAsync()
        {
            try
            {
                IsBusy = true;
                StatusMessage = "Calcul en cours...";

                var periode = PeriodeDate;

                // Toujours relire les fiches depuis la base ici plutôt que d'utiliser Employees
                // tel qu'affiché à l'écran (ETAT-DEVELOPPEMENT.md item 19). Seuls les employés
                // actifs entrent dans un calcul en lot (item 20).
                var salaries = (await _dataService.LoadEmployeesAsync()).Where(e => e.EstActif).ToList();

                // Défaut n° 4 du guide : ce calcul ENREGISTRE la paie du mois. Il remplaçait
                // en silence les bulletins archivés par de simples calculs, sans document ni
                // saisies. Il passe désormais par la même chaîne que « Bulletin de paie »
                // (bulletin archivé, saisies, rappels) et prévient avant de remplacer.
                bool clos = _periodePaieService != null && await _periodePaieService.EstClotureeAsync(periode);
                if (!clos)
                {
                    int dejaEnregistres = 0;
                    foreach (var emp in salaries)
                    {
                        if (await _dataService.BulletinEnregistreAsync(emp.EmployeeId, periode))
                            dejaEnregistres++;
                    }

                    if (dejaEnregistres > 0)
                    {
                        var reponse = MessageBox.Show(
                            string.Format(
                                "{0} salarié(s) ont déjà un bulletin enregistré pour {1}.\n\n" +
                                "« Calculer pour tous » va les régénérer à partir des fiches et des éléments du mois " +
                                "actuels : leurs bulletins archivés seront remplacés par les nouveaux.\n\nContinuer ?",
                                dejaEnregistres, Periode),
                            "Calcul en lot", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
                        if (reponse != MessageBoxResult.Yes)
                        {
                            StatusMessage = "Calcul annulé : aucun bulletin n'a été remplacé.";
                            return;
                        }
                    }
                }

                Calculations.Clear();
                await _chaine.AppliquerBaremeAsync(periode, m => StatusMessage = m);

                int enregistres = 0;
                int doublonsPossibles = 0;
                string refus = null;
                var erreurs = new List<string>();

                foreach (var emp in salaries)
                {
                    try
                    {
                        var apport = await _chaine.ChargerApportAsync(emp, periode, m => erreurs.Add(emp.EmployeeId + " : " + m), avecBareme: false);
                        doublonsPossibles += apport.Avertissements.Count;

                        var resultat = await _chaine.PreparerAsync(
                            emp, periode, 0m, ModesReglementPaie.ModeDeLaFiche, null, apport);

                        if (resultat.Calcul == null) continue;
                        resultat.Calcul.CalculationDate = periode;

                        if (refus == null)
                        {
                            try
                            {
                                await _chaine.EnregistrerAsync(resultat, emp, periode);
                                enregistres++;
                            }
                            catch (BulletinDeSortieProtegeException ex)
                            {
                                // Bulletin de sortie d'un solde de tout compte : on passe au suivant.
                                erreurs.Add(emp.EmployeeId + " : " + ex.Message);
                            }
                            catch (InvalidOperationException ex)
                            {
                                // Période clôturée : inutile d'insister salarié par salarié.
                                refus = ex.Message;
                            }
                        }

                        Calculations.Add(resultat.Calcul);
                    }
                    catch (Exception ex)
                    {
                        erreurs.Add(emp.EmployeeId + " : " + ex.Message);
                    }
                }

                string mentionDoublons = doublonsPossibles > 0
                    ? $" {doublonsPossibles} doublon(s) possible(s) — vérifiez les variables du mois des salariés concernés."
                    : string.Empty;
                string mentionErreurs = erreurs.Count > 0
                    ? $" {erreurs.Count} incident(s) : {erreurs[0]}"
                    : string.Empty;

                StatusMessage = (refus != null
                    ? $"{Calculations.Count} calcul(s) affiché(s), aucun enregistré — {refus}"
                    : $"Calcul terminé pour {Calculations.Count} employé(s) sur {Periode} — " +
                      $"{enregistres} bulletin(s) enregistré(s) et archivé(s) dans la paie du mois.")
                    + await AvertissementPeriodeAsync(Annee, Mois)
                    + mentionDoublons + mentionErreurs;
            }
            catch (Exception ex)
            {
                StatusMessage = $"Erreur lors du calcul : {ex.Message}";
            }
            finally
            {
                IsBusy = false;
            }
        }

        /// <summary>Variables du mois de tout l'effectif, en une lecture.</summary>
        private async Task<Dictionary<string, ApportVariables>> ChargerApportsAsync()
        {
            if (_variablePaieService == null || _rubriqueService == null)
                return new Dictionary<string, ApportVariables>(StringComparer.OrdinalIgnoreCase);

            try
            {
                var plan = await _rubriqueService.ChargerAsync();
                return await _variablePaieService.ApportsAsync(Annee, Mois, plan);
            }
            catch (Exception ex)
            {
                StatusMessage = "Variables du mois illisibles, calcul sans elles : " + ex.Message;
                return new Dictionary<string, ApportVariables>(StringComparer.OrdinalIgnoreCase);
            }
        }

        /// <summary>
        /// Remboursements de prêts de la période, pour tout l'effectif et en une lecture.
        /// Un dictionnaire vide — module non branché ou lecture impossible — calcule sans
        /// retenue de remboursement plutôt que de faire échouer le lot.
        /// </summary>
        private async Task<Dictionary<string, ApportPrets>> ChargerPretsAsync()
        {
            if (_pretService == null)
                return new Dictionary<string, ApportPrets>(StringComparer.OrdinalIgnoreCase);

            try
            {
                return await _pretService.ApportsAsync(Annee, Mois);
            }
            catch (Exception ex)
            {
                StatusMessage = "Prêts illisibles, calcul sans retenue de remboursement : " + ex.Message;
                return new Dictionary<string, ApportPrets>(StringComparer.OrdinalIgnoreCase);
            }
        }

        /// <summary>
        /// Profils applicables à la période, pour tout l'effectif et en une lecture. Un
        /// dictionnaire vide — module non branché ou lecture impossible — calcule sans les
        /// profils plutôt que de faire échouer le lot.
        /// </summary>
        private async Task<Dictionary<string, ApportProfil>> ChargerProfilsAsync(IEnumerable<Employee> salaries)
        {
            if (_profilPaieService == null || _rubriqueService == null)
                return new Dictionary<string, ApportProfil>(StringComparer.OrdinalIgnoreCase);

            try
            {
                var plan = await _rubriqueService.ChargerAsync();
                return await _profilPaieService.ApportsAsync(Annee, Mois, salaries, plan);
            }
            catch (Exception ex)
            {
                StatusMessage = "Profils de paie illisibles, calcul sans eux : " + ex.Message;
                return new Dictionary<string, ApportProfil>(StringComparer.OrdinalIgnoreCase);
            }
        }

        /// <summary>
        /// Avertissement indicatif si le mois n'a jamais été ouvert (module PAI-01). Rien
        /// n'empêche le calcul — signalé par le client le 13 septembre 2026 comme une
        /// lacune, mais un blocage strict casserait tout dossier existant, dont aucun mois
        /// n'a jamais été ouvert explicitement. On prévient donc, sans bloquer.
        /// </summary>
        private async Task<string> AvertissementPeriodeAsync(int annee, int mois)
        {
            if (_periodePaieService == null) return string.Empty;

            try
            {
                var periodePaie = await _periodePaieService.LirePeriodeAsync(annee, mois);
                return periodePaie == null
                    ? " Attention : cette période n'a pas été ouverte (écran « Périodes de paie »)."
                    : string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        /// <summary>Heures supplémentaires de la période, pour tout l'effectif et en une lecture.</summary>
        private async Task<Dictionary<string, ApportHeuresSupplementaires>> ChargerHeuresSupAsync(
            DateTime periode, IEnumerable<Employee> salaries)
        {
            if (_heureSupplementaireService == null)
                return new Dictionary<string, ApportHeuresSupplementaires>(StringComparer.OrdinalIgnoreCase);

            try
            {
                return await _heureSupplementaireService.ChargerApportsMoisAsync(periode.Year, periode.Month, salaries);
            }
            catch (Exception ex)
            {
                StatusMessage = "Heures supplémentaires illisibles, calcul sans elles : " + ex.Message;
                return new Dictionary<string, ApportHeuresSupplementaires>(StringComparer.OrdinalIgnoreCase);
            }
        }

        private static ApportHeuresSupplementaires HeuresSupDe(
            Dictionary<string, ApportHeuresSupplementaires> heuresSup, string employeeId)
        {
            if (heuresSup == null || string.IsNullOrWhiteSpace(employeeId))
                return ApportHeuresSupplementaires.Vide;

            return heuresSup.TryGetValue(employeeId, out var apport) && apport != null
                ? apport
                : ApportHeuresSupplementaires.Vide;
        }

        /// <summary>Conventions collectives et prime d'ancienneté de la période, pour tout l'effectif et en une lecture.</summary>
        private async Task<Dictionary<string, ApportConventionnel>> ChargerConventionsAsync(
            DateTime periode, IEnumerable<Employee> salaries)
        {
            if (_conventionCollectiveService == null)
                return new Dictionary<string, ApportConventionnel>(StringComparer.OrdinalIgnoreCase);

            try
            {
                return await _conventionCollectiveService.ChargerApportsMoisAsync(periode, salaries);
            }
            catch (Exception ex)
            {
                StatusMessage = "Conventions collectives illisibles, calcul sans prime d'ancienneté : " + ex.Message;
                return new Dictionary<string, ApportConventionnel>(StringComparer.OrdinalIgnoreCase);
            }
        }

        private static ApportConventionnel ConventionDe(
            Dictionary<string, ApportConventionnel> conventions, string employeeId)
        {
            if (conventions == null || string.IsNullOrWhiteSpace(employeeId))
                return ApportConventionnel.Vide;

            return conventions.TryGetValue(employeeId, out var apport) && apport != null
                ? apport
                : ApportConventionnel.Vide;
        }

        /// <summary>Rappels et régularisations (PAI-07) en attente pour la période, en une lecture.</summary>
        private async Task<List<RappelPaie>> ChargerRappelsAsync(DateTime periode)
        {
            if (_rappelPaieService == null)
                return new List<RappelPaie>();

            try
            {
                return await _rappelPaieService.ObtenirRappelsDuMoisAsync(periode.Year, periode.Month);
            }
            catch (Exception ex)
            {
                StatusMessage = "Rappels et régularisations illisibles, calcul sans eux : " + ex.Message;
                return new List<RappelPaie>();
            }
        }

        /// <summary>Avantages en nature (PAI-08) actifs, en une lecture.</summary>
        private async Task<List<AvantageNature>> ChargerAvantagesNatureAsync(DateTime periode)
        {
            if (_avantageNatureService == null)
                return new List<AvantageNature>();

            try
            {
                return await _avantageNatureService.ObtenirAvantagesAsync(null, seulementActifs: true);
            }
            catch (Exception ex)
            {
                StatusMessage = "Avantages en nature illisibles, calcul sans eux : " + ex.Message;
                return new List<AvantageNature>();
            }
        }

        /// <summary>Absences de la période, en une lecture.</summary>
        private async Task<List<Absence>> ChargerAbsencesAsync()
        {
            if (_absenceService == null) return new List<Absence>();

            try
            {
                return await _absenceService.ChargerPeriodeAsync(Annee, Mois);
            }
            catch (Exception ex)
            {
                StatusMessage = "Absences illisibles, calcul sans elles : " + ex.Message;
                return new List<Absence>();
            }
        }

        private async Task<BaseRetenueAbsence> BaseRetenueAsync()
        {
            try
            {
                var reglages = await _dataService.LoadSettingsAsync();
                return reglages != null ? reglages.BaseRetenueAbsence : BaseRetenueAbsence.TrentiemeMensuel;
            }
            catch
            {
                return BaseRetenueAbsence.TrentiemeMensuel;
            }
        }

        private static ApportVariables ApportDe(
            Dictionary<string, ApportVariables> apports, string employeeId)
        {
            ApportVariables apport;
            if (apports != null && !string.IsNullOrWhiteSpace(employeeId)
                && apports.TryGetValue(employeeId, out apport))
            {
                return apport;
            }

            return ApportVariables.Aucun();
        }

        private async Task ExportAllAsync()
        {
            try
            {
                IsBusy = true;
                StatusMessage = "Exportation en cours...";

                // Export Excel ou PDF selon vos besoins
                // Ici, exemple d'export Excel pour tous les calculs (à adapter selon votre DataService)
                foreach (var calc in Calculations)
                {
                    await _dataService.ExportToExcelAsync(calc);
                }

                StatusMessage = "Exportation terminée.";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Erreur lors de l'export : {ex.Message}";
            }
            finally
            {
                IsBusy = false;
            }
        }

        private async Task GenerateExcelSalaryTableAsync()
        {
            try
            {
                IsBusy = true;
                StatusMessage = "Génération du tableau Excel en cours...";

                await Task.Run(() =>
                {
                    var saveFileDialog = new SaveFileDialog
                    {
                        Filter = "Excel files (*.xlsx)|*.xlsx",
                        DefaultExt = "xlsx",
                        FileName = $"Tableau_Salaires_{Periode.Replace(" ", "_")}.xlsx"
                    };

                    if (saveFileDialog.ShowDialog() == true)
                    {
                        CreateSalaryTable(saveFileDialog.FileName);
                    }
                });

                StatusMessage = "Tableau Excel généré avec succès.";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Erreur lors de la génération du tableau : {ex.Message}";
            }
            finally
            {
                IsBusy = false;
            }
        }

        private void CreateSalaryTable(string filePath)
        {
            if (!Employees.Any() || !Calculations.Any())
                throw new ArgumentException("Aucune donnée à exporter.");

            using (var workbook = new XLWorkbook())
            {
                var worksheet = workbook.Worksheets.Add("Tableau des Salaires");

                // Configuration générale  
                var titleRange = worksheet.Range("A1:R1");
                titleRange.Merge().Value = "TABLEAU DES SALAIRES DES AGENTS DE SIXIEME SENS BURKINA FASO";
                titleRange.Style.Font.Bold = true;
                titleRange.Style.Font.FontSize = 12;
                titleRange.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                titleRange.Style.Fill.BackgroundColor = XLColor.LightGray;
                titleRange.Style.Border.OutsideBorder = XLBorderStyleValues.Thick;

                // Période  
                var periodeRange = worksheet.Range("S1:V1");
                periodeRange.Merge().Value = $"PERIODE : {Periode}";
                periodeRange.Style.Font.Bold = true;
                periodeRange.Style.Font.FontSize = 12;
                periodeRange.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                periodeRange.Style.Fill.BackgroundColor = XLColor.LightGray;
                periodeRange.Style.Border.OutsideBorder = XLBorderStyleValues.Thick;

                // En-têtes principales (ligne 2)  
                var headers = new[]
                {
                    "N°", "Nom et Prénom(s)", "Fonction", "Date d'entrée", "Situat° matrimonial", "Nbre Chges", "SalBase",
                    "Indem Logement", "Indem Transport", "Idem Fonction", "Idem Sujetion", "Total Indemn.",
                    "Remuner. Brute", "CNSS 5,5%", "CNSS 16%", "Salaire Imposable", "IUTS mensuel", "Soutient patriotique",
                    "Total retenue" , "TPA", "Salaire Net à payer", "Chèque Virement"
                };

                for (int i = 0; i < headers.Length; i++)
                {
                    var cell = worksheet.Cell(2, i + 1);
                    cell.Value = headers[i];
                    cell.Style.Font.Bold = true;
                    cell.Style.Font.FontSize = 10;
                    cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                    cell.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
                    cell.Style.Fill.BackgroundColor = XLColor.LightBlue;
                    cell.Style.Border.OutsideBorder = XLBorderStyleValues.Medium;
                    cell.Style.Alignment.WrapText = true;
                }

                // Données des agents  
                for (int i = 0; i < Employees.Count; i++)
                {
                    var employee = Employees[i];
                    var calculation = Calculations.FirstOrDefault(c => c.Employee?.EmployeeId == employee.EmployeeId) ?? new TaxCalculation();
                    var row = i + 3;

                    worksheet.Cell(row, 1).Value = i + 1;
                    worksheet.Cell(row, 2).Value = $"{employee.LastName} {employee.FirstName}";
                    worksheet.Cell(row, 3).Value = employee.Fonction;
                    worksheet.Cell(row, 4).Value = employee.DateEntree.ToString("dd/MM/yyyy");
                    worksheet.Cell(row, 5).Value = employee.SituationMatrimoniale;
                    worksheet.Cell(row, 6).Value = employee.FamilyCharges;
                    worksheet.Cell(row, 7).Value = employee.BaseSalary;
                    worksheet.Cell(row, 8).Value = employee.HousingAllowance;
                    worksheet.Cell(row, 9).Value = employee.TransportAllowance;
                    worksheet.Cell(row, 10).Value = employee.FunctionAllowance;
                    worksheet.Cell(row, 11).Value = employee.SujetionAllowance;
                    worksheet.Cell(row, 12).Value = employee.TotalIndemnites;
                    worksheet.Cell(row, 13).Value = employee.BaseSalary + employee.TotalIndemnites;
                    worksheet.Cell(row, 14).Value = calculation.CnssEmployee;
                    worksheet.Cell(row, 15).Value = calculation.CnssPartPatronale;
                    worksheet.Cell(row, 16).Value = calculation.BaseIUTSHorsDeduction;
                    worksheet.Cell(row, 17).Value = calculation.IUTSNet;
                    worksheet.Cell(row, 18).Value = calculation.RetenueSpeciale;
                    worksheet.Cell(row, 19).Value = calculation.TotalRetenue;
                    worksheet.Cell(row, 20).Value = calculation.TaxePatronale;
                    worksheet.Cell(row, 21).Value = calculation.SalaireNetaPayer;
                    worksheet.Cell(row, 22).Value = calculation.SalaireNetaPayer;

                    // Style des lignes de données  
                    var dataRange = worksheet.Range(row, 1, row, 22);
                    dataRange.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
                    dataRange.Style.Font.FontSize = 9;

                    // Alignement des colonnes numériques à droite  
                    for (int col = 6; col <= 22; col++)
                    {
                        if (col != 4) // Sauf la colonne date  
                        {
                            worksheet.Cell(row, col).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;
                            worksheet.Cell(row, col).Style.NumberFormat.Format = "#,##0";
                        }
                    }
                }

                // Ligne TOTAUX  
                var totalRow = Employees.Count + 3;
                var totalLabelRange = worksheet.Range(totalRow, 1, totalRow, 5);
                totalLabelRange.Merge().Value = "TOTAUX";
                totalLabelRange.Style.Font.Bold = true;
                totalLabelRange.Style.Fill.BackgroundColor = XLColor.Yellow;
                totalLabelRange.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                totalLabelRange.Style.Border.OutsideBorder = XLBorderStyleValues.Medium;

                // Calcul des totaux  
                var totalColumns = new int[] { 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18, 19, 20, 21, 22 };
                foreach (var col in totalColumns)
                {
                    var sum = CalculateColumnTotal(col);
                    worksheet.Cell(totalRow, col).Value = sum;
                    worksheet.Cell(totalRow, col).Style.Font.Bold = true;
                    worksheet.Cell(totalRow, col).Style.Fill.BackgroundColor = XLColor.Yellow;
                    worksheet.Cell(totalRow, col).Style.NumberFormat.Format = "#,##0";
                    worksheet.Cell(totalRow, col).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;
                    worksheet.Cell(totalRow, col).Style.Border.OutsideBorder = XLBorderStyleValues.Medium;
                }

                // Direction (en bas à droite)  
                var directionRange = worksheet.Range("S" + (totalRow + 2), "V" + (totalRow + 2));
                directionRange.Merge().Value = "Direction";
                directionRange.Style.Font.Bold = true;
                directionRange.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

                // Ajustement automatique des colonnes  
                worksheet.Columns().AdjustToContents();

                // Largeurs spécifiques pour certaines colonnes  
                worksheet.Column(2).Width = 25; // Nom et prénom  
                worksheet.Column(3).Width = 20; // Fonction  
                worksheet.Column(4).Width = 12; // Date  

                // Sauvegarde  
                workbook.SaveAs(filePath);
            }
        }

        private decimal CalculateColumnTotal(int column)
        {
            decimal total = 0;
            for (int i = 0; i < Employees.Count; i++)
            {
                var employee = Employees[i];
                var calculation = Calculations.FirstOrDefault(c => c.Employee?.EmployeeId == employee.EmployeeId) ?? new TaxCalculation();
                total += GetColumnValue(employee, calculation, column);
            }
            return total;
        }

        private static decimal GetColumnValue(Employee employee, TaxCalculation calculation, int column)
        {
            switch (column)
            {
                case 6: return employee.FamilyCharges;
                case 7: return employee.BaseSalary;
                case 8: return employee.HousingAllowance;
                case 9: return employee.TransportAllowance;
                case 10: return employee.FunctionAllowance;
                case 11: return employee.SujetionAllowance;
                case 12: return employee.TotalIndemnites;
                case 13: return employee.BaseSalary + employee.TotalIndemnites;
                case 14: return calculation.CnssEmployee;
                case 15: return calculation.CnssPartPatronale;
                case 16: return calculation.BaseIUTSHorsDeduction;
                case 17: return calculation.IUTSNet;
                case 18: return calculation.RetenueSpeciale;
                case 19: return calculation.TotalRetenue;
                case 20: return calculation.TaxePatronale;
                case 21: return calculation.SalaireNetaPayer;
                case 22: return calculation.SalaireNetaPayer;
                default: return 0;
            }
        }
    }
}
