using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using IUTSCalculator.Models;

namespace IUTSCalculator.Services
{
    /// <summary>
    /// Service de calcul, de gestion et de persistance du Solde de Tout Compte (SAL-05)
    /// selon les dispositions du Code du travail burkinabè et de la Convention Collective Interprofessionnelle (CCI-BF).
    /// </summary>
    public class SoldeToutCompteService
    {
        private readonly SQLiteService _sqliteService;
        private readonly AbsenceService _absenceService;
        private readonly PretService _pretService;
        private readonly AuditService _auditService;
        private readonly TaxCalculatorService _taxCalculatorService;

        private const string Colonnes =
            "Id, EmployeeId, NomSalarie, DateSortie, Motif, DateEntree, AncienneteAnnees, AncienneteMois, " +
            "Categorie, Echelon, Fonction, SalaireReference, SalairePresence, JoursPresence, JoursCongesDus, " +
            "IndemniteConges, DureePreavisMois, StatutPreavis, IndemnitePreavis, IndemniteLicenciement, " +
            "TauxLicenciementApplique, Indemnite13emeMois, Mois13emeMois, IndemnitesDiverses, " +
            "LibelleIndemnitesDiverses, DeductionsPrets, DeductionsAutres, LibelleDeductionsAutres, " +
            "EstimationIUTS, EstimationCNSS, TotalBrut, TotalExonere, TotalImposable, TotalRetenues, " +
            "TotalNet, Commentaire, DateCreation, DateModification";

        public SoldeToutCompteService(
            SQLiteService sqliteService,
            AuditService auditService = null,
            AbsenceService absenceService = null,
            PretService pretService = null,
            TaxCalculatorService taxCalculatorService = null)
        {
            _sqliteService = sqliteService ?? throw new ArgumentNullException(nameof(sqliteService));
            _auditService = auditService ?? new AuditService(sqliteService);
            _absenceService = absenceService ?? new AbsenceService(sqliteService);
            _pretService = pretService ?? new PretService(sqliteService);
            _taxCalculatorService = taxCalculatorService;
        }

        // ── Calcul métier du Solde de Tout Compte ──────────────────────────────

        /// <summary>
        /// Établit une simulation ou un décompte complet de solde de tout compte pour un salarié à une date de départ donnée.
        /// </summary>
        public async Task<SoldeToutCompte> PreparerSoldeAsync(
            Employee salarie,
            DateTime dateSortie,
            MotifDepart motif = MotifDepart.Demission,
            StatutPreavis? statutPreavis = null,
            decimal? joursPresence = null,
            decimal? joursCongesManuels = null,
            decimal? salaireRefManuel = null,
            decimal gratification = 0m,
            decimal indemnitesDiverses = 0m,
            string libelleDivers = null,
            decimal autresDeductions = 0m,
            string libelleDeductions = null,
            int? dureePreavisManuelle = null,
            bool calculer13emeProrata = false)
        {
            if (salarie == null)
                throw new ArgumentNullException(nameof(salarie));

            var stc = new SoldeToutCompte
            {
                EmployeeId = salarie.EmployeeId,
                NomSalarie = salarie.FullName,
                DateSortie = dateSortie.Date,
                Motif = motif,
                DateEntree = salarie.DateEntree != default(DateTime) ? salarie.DateEntree : dateSortie.Date,
                Categorie = salarie.Category ?? "",
                Echelon = salarie.Echelon ?? "",
                Fonction = salarie.Fonction ?? "",
                LibelleIndemnitesDiverses = libelleDivers ?? "",
                LibelleDeductionsAutres = libelleDeductions ?? ""
            };

            // 1. Calcul de l'ancienneté exacte
            int annees = salarie.ObtenirAncienneteAnnees(dateSortie);
            int mois = salarie.ObtenirAncienneteMois(dateSortie);
            stc.AncienneteAnnees = annees;
            stc.AncienneteMois = mois;

            // 2. Assiette de référence (salaire mensuel brut contractuel ou manuel)
            decimal salaireBrutContractuel = salarie.BaseSalary + salarie.HousingAllowance +
                                             salarie.TransportAllowance + salarie.FunctionAllowance +
                                             salarie.SujetionAllowance;
            stc.SalaireReference = (salaireRefManuel.HasValue && salaireRefManuel.Value > 0m)
                ? salaireRefManuel.Value
                : salaireBrutContractuel;

            // 3. Salaire de présence du mois de départ (prorata temporis)
            decimal jPresence = joursPresence ?? Math.Min(dateSortie.Day, 30);
            stc.JoursPresence = jPresence;
            if (jPresence > 0 && stc.SalaireReference > 0)
            {
                stc.SalairePresence = Math.Round((stc.SalaireReference / 30m) * jPresence, 0);
            }

            // 4. Reliquat de congés payés
            if (joursCongesManuels.HasValue)
            {
                stc.JoursCongesDus = joursCongesManuels.Value;
            }
            else
            {
                try
                {
                    var absences = await _absenceService.ChargerSalarieAsync(salarie.EmployeeId);
                    var soldeConges = AbsenceService.Solde(
                        salarie, absences, dateSortie, AbsenceService.DroitMensuelLegal, dateSortie.Year, dateSortie.Month);
                    stc.JoursCongesDus = Math.Max(0m, soldeConges.Solde);
                }
                catch
                {
                    stc.JoursCongesDus = 0m;
                }
            }

            if (stc.JoursCongesDus > 0 && stc.SalaireReference > 0)
            {
                // Indemnité compensatrice de congés = (SalaireRef / 30) * JoursDus
                stc.IndemniteConges = Math.Round((stc.SalaireReference / 30m) * stc.JoursCongesDus, 0);
            }

            // 5. Durée et indemnité compensatrice de préavis (Art. 30 CCI-BF)
            // Durée de la catégorie (CCI-BF art. 35), sauf durée saisie dans le formulaire
            // (travailleur payé à l'heure ou à la journée, contrat plus favorable…).
            stc.DureePreavisMois = dureePreavisManuelle.HasValue && dureePreavisManuelle.Value >= 0
                ? dureePreavisManuelle.Value
                : DeterminerDureePreavisMois(salarie, annees);
            stc.StatutPreavis = statutPreavis ?? DeterminerStatutPreavisDefaut(motif);

            if (stc.StatutPreavis == StatutPreavis.DispensePaye && stc.DureePreavisMois > 0)
            {
                stc.IndemnitePreavis = Math.Round(stc.DureePreavisMois * stc.SalaireReference, 0);
            }
            else
            {
                stc.IndemnitePreavis = 0m;
            }

            // 6. Indemnité de licenciement / retraite / départ (Art. 35 CCI-BF)
            if (OuvreDroitIndemniteLicenciement(motif) && annees >= 1)
            {
                decimal tauxGlobal = CalculerTauxIndemniteLicenciement(annees, mois);

                // Départ VOLONTAIRE à la retraite : demi-indemnité (art. 38 CCI-BF), comme
                // l'annonce la documentation du module (CalculerIndemniteLicenciementLegal).
                // L'écran versait jusqu'ici l'indemnité entière (défaut n° 8 du guide).
                decimal indemnite = Math.Round(stc.SalaireReference * (tauxGlobal / 100m), 0);
                if (motif == MotifDepart.RetraiteDepart)
                {
                    tauxGlobal = tauxGlobal / 2m;
                    indemnite = Math.Round(indemnite / 2m, 0);
                }

                stc.TauxLicenciementApplique = tauxGlobal;
                stc.IndemniteLicenciement = indemnite;
            }
            else
            {
                stc.TauxLicenciementApplique = 0m;
                stc.IndemniteLicenciement = 0m;
            }

            // 7. Prorata de 13ème mois. Une gratification saisie l'emporte ; sinon le prorata
            // n'est calculé que si le dossier le demande (tous les employeurs ne versent pas
            // de 13e mois : le calcul automatique ne doit pas en créer un à leur place).
            if (gratification > 0m)
            {
                stc.Mois13emeMois = dateSortie.Month;
                stc.Indemnite13emeMois = gratification;
            }
            else if (calculer13emeProrata)
            {
                stc.Mois13emeMois = MoisPresence13emeMois(stc.DateEntree, dateSortie);
                stc.Indemnite13emeMois = Calculer13emeMoisProrata(stc.SalaireReference, stc.Mois13emeMois);
            }
            else
            {
                stc.Mois13emeMois = 0m;
                stc.Indemnite13emeMois = 0m;
            }

            // 8. Indemnités diverses
            stc.IndemnitesDiverses = indemnitesDiverses;

            // 9. Déductions : prêts en cours
            decimal dettesPrets = await ObtenirDettePretsRestanteAsync(salarie.EmployeeId);
            stc.DeductionsPrets = dettesPrets;
            stc.DeductionsAutres = autresDeductions;

            // 10. Estimations fiscales et sociales
            // L'indemnité légale de licenciement est exonérée (Art. 106 CGI).
            // Le reste (dernier salaire, congés, préavis, 13ème mois, primes diverses) est imposable et soumis à CNSS.
            decimal imposable = stc.TotalImposable;
            if (imposable > 0)
            {
                // CNSS salariale estimée à 5.5% (part salariale branche retraite)
                stc.EstimationCNSS = Math.Round(imposable * 0.055m, 0);

                // Estimation simplifiée de l'IUTS sur la rémunération imposable de sortie
                stc.EstimationIUTS = EstimerIUTS(imposable, salarie.FamilyCharges);
            }
            else
            {
                stc.EstimationCNSS = 0m;
                stc.EstimationIUTS = 0m;
            }

            return stc;
        }

        /// <summary>
        /// Mois de présence dans l'année civile de la sortie, en mois de 30 jours : du 1er janvier
        /// (ou de la date d'entrée si elle est la même année) jusqu'à la date de sortie incluse.
        /// Du 1er janvier au 15 mars : 2,5 mois. Plafonné à 12.
        /// </summary>
        public static decimal MoisPresence13emeMois(DateTime dateEntree, DateTime dateSortie)
        {
            DateTime debutAnnee = new DateTime(dateSortie.Year, 1, 1);
            DateTime debut = dateEntree.Date > debutAnnee ? dateEntree.Date : debutAnnee;
            if (debut > dateSortie.Date) return 0m;

            int mois = dateSortie.Month - debut.Month;
            int jours = Math.Min(dateSortie.Day, 30) - Math.Min(debut.Day, 30) + 1;
            decimal total = mois + jours / 30m;
            return Math.Min(12m, Math.Max(0m, Math.Round(total, 2)));
        }

        /// <summary>13e mois au prorata : salaire de référence / 12 × mois de présence.</summary>
        public static decimal Calculer13emeMoisProrata(decimal salaireReference, decimal moisPresence)
        {
            if (salaireReference <= 0m || moisPresence <= 0m) return 0m;
            return Math.Round(salaireReference / 12m * moisPresence, 0);
        }

        /// <summary>
        /// Durée légale ou conventionnelle du préavis en mois selon la qualification et l'ancienneté (Art. 30 CCI-BF).
        /// </summary>
        public static int DeterminerDureePreavisMois(Employee salarie, int anneesAnciennete)
        {
            if (salarie == null) return 1;
            return EstCategoriePreavisLong(salarie) ? 3 : 1;
        }

        /// <summary>
        /// Vrai pour les catégories au préavis de trois mois : cadres, agents de maîtrise et
        /// techniciens (convention collective interprofessionnelle du 9 juillet 1974,
        /// art. 35 ; nouveau Code du travail, loi n° 013-2026/ALP, art. 76). Les autres
        /// travailleurs payés au mois ont un mois ; ceux payés à l'heure ou à la journée,
        /// huit jours — durée à saisir dans le formulaire.
        ///
        /// L'ancienne règle donnait trois mois dès que la catégorie CONTENAIT « cadre » —
        /// « Non cadre » compris — et deux mois aux ouvriers et employés de cinq ans
        /// d'ancienneté, palier que le texte ne prévoit pas (défaut n° 8 du guide).
        /// </summary>
        public static bool EstCategoriePreavisLong(Employee salarie)
        {
            if (salarie == null) return false;
            if (salarie.IsHigherCategory) return true;

            string c = Employee.NormaliserCategorie(salarie.Category);
            if (c.Length == 0) return false;
            if (c.Contains("non cadre") || c.Contains("non-cadre")) return false;
            return c.Contains("cadre") || c.Contains("maitrise") || c.Contains("technicien");
        }

        /// <summary>
        /// Détermine le statut par défaut du préavis selon le motif de départ.
        /// </summary>
        public static StatutPreavis DeterminerStatutPreavisDefaut(MotifDepart motif)
        {
            switch (motif)
            {
                case MotifDepart.LicenciementFauteLourde:
                case MotifDepart.Deces:
                    return StatutPreavis.Inapplicable;

                case MotifDepart.LicenciementEconomique:
                case MotifDepart.LicenciementPersonnel:
                    return StatutPreavis.DispensePaye; // Fréquemment indemnisé par l'employeur

                default:
                    return StatutPreavis.Effectue;
            }
        }

        /// <summary>
        /// Indique si le motif ouvre droit à l'indemnité légale ou conventionnelle de licenciement / fin de contrat.
        /// </summary>
        public static bool OuvreDroitIndemniteLicenciement(MotifDepart motif)
        {
            switch (motif)
            {
                case MotifDepart.LicenciementPersonnel:
                case MotifDepart.LicenciementEconomique:
                case MotifDepart.RetraiteDepart:
                case MotifDepart.RetraiteMise:
                case MotifDepart.RuptureConventionnelle:
                case MotifDepart.Deces:
                    return true;

                case MotifDepart.Demission:
                case MotifDepart.LicenciementFauteLourde:
                case MotifDepart.FinContrat:
                default:
                    return false;
            }
        }

        /// <summary>
        /// Calcule le pourcentage global d'indemnité de licenciement par application des tranches de l'Art. 35 CCI-BF.
        /// 1ère à 5ème année : 25% par an.
        /// 6ème à 10ème année : 30% par an.
        /// Au-delà de 10 ans : 40% par an.
        /// Fractions d'années proratisées au mois révolu.
        /// </summary>
        public static decimal CalculerTauxIndemniteLicenciement(int annees, int mois)
        {
            if (annees < 1) return 0m;

            decimal totalPourcent = 0m;

            // Tranche 1 : 1 à 5 ans (max 5 ans) -> 25% par an
            int anneesTranche1 = Math.Min(annees, 5);
            totalPourcent += anneesTranche1 * 25m;

            // Tranche 2 : 6 à 10 ans (max 5 ans) -> 30% par an
            if (annees > 5)
            {
                int anneesTranche2 = Math.Min(annees - 5, 5);
                totalPourcent += anneesTranche2 * 30m;
            }

            // Tranche 3 : au-delà de 10 ans -> 40% par an
            if (annees > 10)
            {
                int anneesTranche3 = annees - 10;
                totalPourcent += anneesTranche3 * 40m;
            }

            // Prorata pour les mois de l'année incomplète
            if (mois > 0)
            {
                decimal fraction = (decimal)mois / 12m;
                decimal tauxMois = (annees < 5) ? 25m : (annees < 10 ? 30m : 40m);
                totalPourcent += Math.Round(fraction * tauxMois, 2);
            }

            return totalPourcent;
        }

        /// <summary>
        /// Calcule l'indemnité légale de licenciement selon l'Art. 35 CCI-BF (méthode utilitaire pour calcul direct et tests).
        /// </summary>
        public static decimal CalculerIndemniteLicenciementLegal(decimal salaireMoyen, decimal ancienneteAnnees, MotifDepart motif)
        {
            if (!OuvreDroitIndemniteLicenciement(motif) || ancienneteAnnees < 1m)
                return 0m;

            int annees = (int)Math.Floor(ancienneteAnnees);
            int mois = (int)Math.Round((ancienneteAnnees - annees) * 12m);
            decimal taux = CalculerTauxIndemniteLicenciement(annees, mois);
            decimal indemnite = Math.Round(salaireMoyen * (taux / 100m), 0);

            // Départ volontaire à la retraite : demi-indemnité selon l'art. 38 CCI-BF
            if (motif == MotifDepart.RetraiteDepart)
                indemnite = Math.Round(indemnite / 2m, 0);

            return indemnite;
        }

        /// <summary>
        /// Calcule la durée légale du préavis en mois selon la qualification et l'ancienneté (Art. 30 CCI-BF).
        /// </summary>
        public static int CalculerDureePreavisMois(bool isCadre, decimal ancienneteAnnees)
        {
            // Même règle que DeterminerDureePreavisMois : 3 mois (cadres, maîtrise,
            // techniciens), 1 mois sinon, sans palier d'ancienneté.
            return isCadre ? 3 : 1;
        }

        /// <summary>
        /// Estimation rapide de l'IUTS net sur le montant imposable.
        /// </summary>
        private static decimal EstimerIUTS(decimal netImposable, int chargesFamille)
        {
            if (netImposable <= 30000m) return 0m;

            // Barème simplifié IUTS Burkina Faso (tranches 10%, 20%, 25%, 30%)
            decimal impot = 0m;
            if (netImposable > 30000m)
                impot += Math.Min(netImposable - 30000m, 20000m) * 0.10m; // 30 000 à 50 000
            if (netImposable > 50000m)
                impot += Math.Min(netImposable - 50000m, 30000m) * 0.15m; // 50 000 à 80 000
            if (netImposable > 80000m)
                impot += Math.Min(netImposable - 80000m, 70000m) * 0.20m; // 80 000 à 150 000
            if (netImposable > 150000m)
                impot += Math.Min(netImposable - 150000m, 100000m) * 0.25m; // 150 000 à 250 000
            if (netImposable > 250000m)
                impot += (netImposable - 250000m) * 0.30m; // > 250 000

            // Réduction pour charges de famille (8% par charge, max 4 charges = 32%)
            int charges = Math.Min(Math.Max(0, chargesFamille), 4);
            decimal tauxReduction = charges * 0.08m;
            decimal reduction = impot * tauxReduction;

            return Math.Max(0m, Math.Round(impot - reduction, 0));
        }

        /// <summary>
        /// Calcule le total du capital restant dû sur l'ensemble des prêts et avances d'un salarié.
        ///
        /// Les prêts SUSPENDUS sont inclus au même titre que ceux en cours : une suspension
        /// arrête temporairement les retenues, elle n'efface pas la dette. Les exclure ici
        /// laissait cette dette hors du solde de tout compte — ni retenue sur le dernier
        /// paiement, ni annulée avec le prêt (voir EnregistrerSoldeAsync) — signalé le 16
        /// septembre 2026 (voir ETAT-DEVELOPPEMENT.md item 22).
        /// </summary>
        public async Task<decimal> ObtenirDettePretsRestanteAsync(string employeeId)
        {
            if (string.IsNullOrWhiteSpace(employeeId)) return 0m;

            try
            {
                var prets = await _pretService.ChargerAsync();
                var pretsSalarie = prets.Where(p => string.Equals(p.EmployeeId, employeeId, StringComparison.OrdinalIgnoreCase) &&
                                                    (p.Statut == StatutPret.EnCours || p.Statut == StatutPret.Suspendu)).ToList();

                decimal totalRestant = 0m;
                foreach (var pret in pretsSalarie)
                {
                    var echeances = await _pretService.ChargerEcheancesAsync(pret.Id);
                    decimal paye = echeances.Where(e => e.PeriodeCloturee).Sum(e => e.MontantPrevu);
                    decimal reste = Math.Max(0m, pret.MontantAccorde - paye);
                    totalRestant += reste;
                }
                return totalRestant;
            }
            catch
            {
                return 0m;
            }
        }

        // ── Bulletin de sortie (défaut n° 1 du guide) ─────────────────────────

        /// <summary>Codes du plan de paie portés par le bulletin de sortie.</summary>
        public const string CodeIndemniteConges = "INDCONGES";
        public const string CodeIndemnitePreavis = "INDPREAVIS";
        public const string CodeIndemniteDepart = "INDDEPART";
        public const string CodeGratification = "GRATIF";
        public const string CodeIndemnitesDiverses = "INDDIVERS";
        public const string CodeSoldePrets = "PRET";
        public const string CodeRetenueSortie = "RETSORTIE";

        private static readonly string[] CodesPrets = { "PRET", "AVANCE", "ACOMPTE" };

        /// <summary>
        /// Prépare (sans l'enregistrer) le bulletin du mois de sortie qui porte le solde de
        /// tout compte : la paie normale du mois — proratisée à la date de sortie, avec ses
        /// variables, absences, heures, primes, avantages, rappels et saisies — plus les
        /// indemnités de rupture du décompte, chacune sous sa rubrique du plan de paie, et
        /// le solde des prêts. Le tout est calculé par le moteur de paie : IUTS au barème
        /// normal, CNSS plafonnée, TPA, exonérations suivant les cases du plan (indemnité de
        /// licenciement exonérée d'IUTS en vertu de l'article 106-5 du CGI ; préavis et
        /// congés payés imposables).
        ///
        /// Jusqu'ici le décompte ne créait aucun bulletin : ses indemnités n'entraient ni
        /// dans les déclarations, ni dans le livre de paie, ni dans les virements, et son
        /// IUTS (barème simplifié) et sa CNSS (sans plafond) étaient des estimations — le
        /// reçu ne correspondait pas à la somme versée.
        ///
        /// Le décompte est ensuite aligné sur ce bulletin (<see cref="AlignerSurBulletin"/>) :
        /// le net à percevoir du reçu EST le net à payer du bulletin.
        /// </summary>
        public async Task<ResultatBulletinMois> PreparerBulletinSortieAsync(
            SoldeToutCompte stc, Employee salarie, ChaineBulletinService chaine, IEnumerable<Rubrique> plan)
        {
            if (stc == null) throw new ArgumentNullException(nameof(stc));
            if (salarie == null) throw new ArgumentNullException(nameof(salarie));
            if (chaine == null) throw new ArgumentNullException(nameof(chaine));

            // La fiche porte la date de sortie dès l'aperçu : le prorata de sortie
            // s'applique avant même la validation.
            var sortant = salarie.Clone();
            sortant.DateSortie = stc.DateSortie.Date;

            var periode = new DateTime(stc.DateSortie.Year, stc.DateSortie.Month, 1);
            var avertissements = new List<string>();
            // Saisies calculées APRÈS les lignes de sortie (voir plus bas), sur le net qui les
            // inclut : c'est ce net qui sert ensuite à enregistrer les retenues.
            var apport = await chaine.ChargerApportAsync(sortant, periode, m => avertissements.Add(m), avecSaisies: false);

            var planParCode = (plan ?? Enumerable.Empty<Rubrique>())
                .Where(r => r != null && !string.IsNullOrWhiteSpace(r.Code))
                .GroupBy(r => r.Code.Trim().ToUpperInvariant())
                .ToDictionary(g => g.Key, g => g.First());

            var lignes = new ApportRappels();

            AjouterLigneSortie(lignes, apport, planParCode, avertissements, CodeIndemniteConges,
                string.Format(CultureInfo.GetCultureInfo("fr-FR"), "Indemnité compensatrice de congés payés ({0:0.##} j)", stc.JoursCongesDus),
                SensRubrique.Gain, stc.IndemniteConges, true, true, true);
            AjouterLigneSortie(lignes, apport, planParCode, avertissements, CodeIndemnitePreavis,
                string.Format("Indemnité compensatrice de préavis ({0} mois)", stc.DureePreavisMois),
                SensRubrique.Gain, stc.IndemnitePreavis, true, false, true);
            AjouterLigneSortie(lignes, apport, planParCode, avertissements, CodeIndemniteDepart,
                stc.Motif == MotifDepart.RetraiteDepart || stc.Motif == MotifDepart.RetraiteMise
                    ? "Indemnité de départ à la retraite"
                    : "Indemnité de licenciement / de fin de service",
                SensRubrique.Gain, stc.IndemniteLicenciement, false, false, false);
            AjouterLigneSortie(lignes, apport, planParCode, avertissements, CodeGratification,
                "Gratification / 13e mois (prorata de sortie)",
                SensRubrique.Gain, stc.Indemnite13emeMois, true, true, true);
            AjouterLigneSortie(lignes, apport, planParCode, avertissements, CodeIndemnitesDiverses,
                string.IsNullOrWhiteSpace(stc.LibelleIndemnitesDiverses) ? "Indemnités diverses de sortie" : stc.LibelleIndemnitesDiverses,
                SensRubrique.Gain, stc.IndemnitesDiverses, true, true, true);

            // Prêts : le décompte retient tout le capital restant dû ; la retenue normale du
            // mois est déjà dans l'apport — seul le complément est ajouté.
            decimal pretsDuMois = apport.Lignes
                .Where(l => l.Sens == SensRubrique.Retenue && CodesPrets.Contains((l.Code ?? "").ToUpperInvariant()))
                .Sum(l => l.Montant);
            decimal complementPrets = Math.Max(0m, stc.DeductionsPrets - pretsDuMois);
            if (complementPrets > 0m)
                lignes.AjouterLigne(CodeSoldePrets, "Solde des prêts et avances au départ", SensRubrique.Retenue,
                                    complementPrets, false, false, false);

            if (stc.DeductionsAutres > 0m)
                lignes.AjouterLigne(CodeRetenueSortie,
                    string.IsNullOrWhiteSpace(stc.LibelleDeductionsAutres) ? "Retenue de sortie" : stc.LibelleDeductionsAutres,
                    SensRubrique.Retenue, stc.DeductionsAutres, false, false, false);

            apport = apport.AvecRappels(lignes);
            apport = await chaine.AjouterSaisiesAsync(apport, sortant, periode, m => avertissements.Add(m));

            var resultat = await chaine.PreparerAsync(
                sortant, periode, apport.JoursCongesPris, ModesReglementPaie.ModeDeLaFiche, null, apport);
            resultat.Avertissements.AddRange(avertissements);
            resultat.EstBulletinDeSortie = true;

            AlignerSurBulletin(stc, resultat);
            return resultat;
        }

        /// <summary>
        /// Ajoute une ligne du décompte au bulletin, avec le traitement de sa rubrique dans le
        /// plan de paie (repli sur le traitement par défaut si la rubrique n'existe pas).
        /// Une rubrique déjà saisie dans les variables du mois n'est pas ajoutée une seconde
        /// fois : c'est la saisie du mois qui fait foi, et l'écran le signale.
        /// </summary>
        private static void AjouterLigneSortie(
            ApportRappels lignes, ApportVariables apport, Dictionary<string, Rubrique> plan, List<string> avertissements,
            string code, string libelle, SensRubrique sens, decimal montant, bool iuts, bool cnss, bool tpa)
        {
            if (montant <= 0m) return;

            if (apport.Lignes.Any(l => string.Equals(l.Code, code, StringComparison.OrdinalIgnoreCase)))
            {
                avertissements.Add(string.Format(
                    "la rubrique {0} est déjà saisie dans les variables du mois : le montant du décompte ({1:N0} FCFA) n'est pas ajouté une seconde fois",
                    code, montant));
                return;
            }

            Rubrique r;
            if (plan != null && plan.TryGetValue(code, out r))
            {
                iuts = r.SoumisIUTS;
                cnss = r.SoumisCNSS;
                tpa = r.SoumisTPA;
            }
            lignes.AjouterLigne(code, libelle, sens, montant, iuts, cnss, tpa);
        }

        /// <summary>
        /// Recopie dans le décompte les montants du bulletin de sortie : IUTS et CNSS du
        /// moteur, prêts retenus, autres retenues du bulletin, et salaire de présence (paie
        /// normale du mois, prorata compris). Après alignement, le total brut du décompte
        /// est le brut du bulletin et son net à percevoir, le net à payer du bulletin.
        /// </summary>
        public static void AlignerSurBulletin(SoldeToutCompte stc, ResultatBulletinMois resultat)
        {
            if (stc == null || resultat == null || resultat.Bulletin == null || resultat.Calcul == null) return;

            var b = resultat.Bulletin;
            var lignes = resultat.Apport != null ? resultat.Apport.Lignes : (IReadOnlyList<LigneApport>)new List<LigneApport>();
            Func<string, decimal> gain = code => lignes
                .Where(l => l.Sens == SensRubrique.Gain && string.Equals(l.Code, code, StringComparison.OrdinalIgnoreCase))
                .Sum(l => l.Montant);

            stc.IndemniteConges = gain(CodeIndemniteConges);
            stc.IndemnitePreavis = gain(CodeIndemnitePreavis);
            stc.IndemniteLicenciement = gain(CodeIndemniteDepart);
            stc.Indemnite13emeMois = gain(CodeGratification);
            stc.IndemnitesDiverses = gain(CodeIndemnitesDiverses);

            decimal indemnites = stc.IndemniteConges + stc.IndemnitePreavis + stc.IndemniteLicenciement
                               + stc.Indemnite13emeMois + stc.IndemnitesDiverses;
            stc.SalairePresence = Math.Max(0m, b.SalaireBrut - indemnites);

            stc.EstimationIUTS = resultat.Calcul.IUTSNet;
            stc.EstimationCNSS = resultat.Calcul.CnssEmployee;
            stc.DeductionsPrets = lignes
                .Where(l => l.Sens == SensRubrique.Retenue && CodesPrets.Contains((l.Code ?? "").ToUpperInvariant()))
                .Sum(l => l.Montant);

            // Tout le reste de ce qui sépare le brut du net : retenue obligatoire, saisies,
            // cotisation syndicale, reprise d'avantages en nature, retenue de sortie…
            stc.DeductionsAutres = Math.Max(0m,
                b.SalaireBrut - b.SalaireNetAPayer - stc.EstimationIUTS - stc.EstimationCNSS - stc.DeductionsPrets);
            string saisie = stc.LibelleDeductionsAutres;
            stc.LibelleDeductionsAutres = "Autres retenues du bulletin (retenue obligatoire, saisies, cotisations, reprises)" +
                (string.IsNullOrWhiteSpace(saisie) ? "" : ", dont « " + saisie.Trim() + " »");
        }

        // ── Persistance & Historique ──────────────────────────────────────────

        /// <summary>
        /// Enregistre un solde de tout compte validé et met à jour le statut du salarié si demandé.
        /// </summary>
        public async Task<int> EnregistrerSoldeAsync(
            SoldeToutCompte stc,
            bool marquerEmployeSorti = true,
            string utilisateur = "Système")
        {
            if (stc == null) throw new ArgumentNullException(nameof(stc));
            if (string.IsNullOrWhiteSpace(stc.EmployeeId)) throw new ArgumentException("L'identifiant du salarié est requis.");

            string now = DbValue.DateToDb(DateTime.Now);

            using (var conn = _sqliteService.CreateConnection())
            using (var tx = conn.BeginTransaction())
            {
                try
                {
                    int id;
                    using (var cmd = conn.CreateCommand())
                    {
                        cmd.Transaction = tx;
                        cmd.CommandText = @"
                            INSERT INTO SoldesToutCompte (
                                EmployeeId, NomSalarie, DateSortie, Motif, DateEntree, AncienneteAnnees, AncienneteMois,
                                Categorie, Echelon, Fonction, SalaireReference, SalairePresence, JoursPresence, JoursCongesDus,
                                IndemniteConges, DureePreavisMois, StatutPreavis, IndemnitePreavis, IndemniteLicenciement,
                                TauxLicenciementApplique, Indemnite13emeMois, Mois13emeMois, IndemnitesDiverses,
                                LibelleIndemnitesDiverses, DeductionsPrets, DeductionsAutres, LibelleDeductionsAutres,
                                EstimationIUTS, EstimationCNSS, TotalBrut, TotalExonere, TotalImposable, TotalRetenues,
                                TotalNet, Commentaire, DateCreation, DateModification
                            ) VALUES (
                                @EmployeeId, @NomSalarie, @DateSortie, @Motif, @DateEntree, @AncienneteAnnees, @AncienneteMois,
                                @Categorie, @Echelon, @Fonction, @SalaireReference, @SalairePresence, @JoursPresence, @JoursCongesDus,
                                @IndemniteConges, @DureePreavisMois, @StatutPreavis, @IndemnitePreavis, @IndemniteLicenciement,
                                @TauxLicenciementApplique, @Indemnite13emeMois, @Mois13emeMois, @IndemnitesDiverses,
                                @LibelleIndemnitesDiverses, @DeductionsPrets, @DeductionsAutres, @LibelleDeductionsAutres,
                                @EstimationIUTS, @EstimationCNSS, @TotalBrut, @TotalExonere, @TotalImposable, @TotalRetenues,
                                @TotalNet, @Commentaire, @DateCreation, @DateModification
                            );";

                        cmd.Parameters.AddWithValue("@EmployeeId", stc.EmployeeId);
                        cmd.Parameters.AddWithValue("@NomSalarie", stc.NomSalarie ?? "");
                        cmd.Parameters.AddWithValue("@DateSortie", DbValue.DateToDb(stc.DateSortie));
                        cmd.Parameters.AddWithValue("@Motif", (int)stc.Motif);
                        cmd.Parameters.AddWithValue("@DateEntree", DbValue.DateToDb(stc.DateEntree));
                        cmd.Parameters.AddWithValue("@AncienneteAnnees", stc.AncienneteAnnees);
                        cmd.Parameters.AddWithValue("@AncienneteMois", stc.AncienneteMois);
                        cmd.Parameters.AddWithValue("@Categorie", stc.Categorie ?? "");
                        cmd.Parameters.AddWithValue("@Echelon", stc.Echelon ?? "");
                        cmd.Parameters.AddWithValue("@Fonction", stc.Fonction ?? "");
                        cmd.Parameters.AddWithValue("@SalaireReference", DbValue.MoneyToDb(stc.SalaireReference));
                        cmd.Parameters.AddWithValue("@SalairePresence", DbValue.MoneyToDb(stc.SalairePresence));
                        cmd.Parameters.AddWithValue("@JoursPresence", DbValue.DecimalToDb(stc.JoursPresence));
                        cmd.Parameters.AddWithValue("@JoursCongesDus", DbValue.DecimalToDb(stc.JoursCongesDus));
                        cmd.Parameters.AddWithValue("@IndemniteConges", DbValue.MoneyToDb(stc.IndemniteConges));
                        cmd.Parameters.AddWithValue("@DureePreavisMois", stc.DureePreavisMois);
                        cmd.Parameters.AddWithValue("@StatutPreavis", (int)stc.StatutPreavis);
                        cmd.Parameters.AddWithValue("@IndemnitePreavis", DbValue.MoneyToDb(stc.IndemnitePreavis));
                        cmd.Parameters.AddWithValue("@IndemniteLicenciement", DbValue.MoneyToDb(stc.IndemniteLicenciement));
                        cmd.Parameters.AddWithValue("@TauxLicenciementApplique", DbValue.DecimalToDb(stc.TauxLicenciementApplique));
                        cmd.Parameters.AddWithValue("@Indemnite13emeMois", DbValue.MoneyToDb(stc.Indemnite13emeMois));
                        cmd.Parameters.AddWithValue("@Mois13emeMois", DbValue.DecimalToDb(stc.Mois13emeMois));
                        cmd.Parameters.AddWithValue("@IndemnitesDiverses", DbValue.MoneyToDb(stc.IndemnitesDiverses));
                        cmd.Parameters.AddWithValue("@LibelleIndemnitesDiverses", stc.LibelleIndemnitesDiverses ?? "");
                        cmd.Parameters.AddWithValue("@DeductionsPrets", DbValue.MoneyToDb(stc.DeductionsPrets));
                        cmd.Parameters.AddWithValue("@DeductionsAutres", DbValue.MoneyToDb(stc.DeductionsAutres));
                        cmd.Parameters.AddWithValue("@LibelleDeductionsAutres", stc.LibelleDeductionsAutres ?? "");
                        cmd.Parameters.AddWithValue("@EstimationIUTS", DbValue.MoneyToDb(stc.EstimationIUTS));
                        cmd.Parameters.AddWithValue("@EstimationCNSS", DbValue.MoneyToDb(stc.EstimationCNSS));
                        cmd.Parameters.AddWithValue("@TotalBrut", DbValue.MoneyToDb(stc.TotalBrut));
                        cmd.Parameters.AddWithValue("@TotalExonere", DbValue.MoneyToDb(stc.TotalExonere));
                        cmd.Parameters.AddWithValue("@TotalImposable", DbValue.MoneyToDb(stc.TotalImposable));
                        cmd.Parameters.AddWithValue("@TotalRetenues", DbValue.MoneyToDb(stc.TotalRetenues));
                        cmd.Parameters.AddWithValue("@TotalNet", DbValue.MoneyToDb(stc.TotalNet));
                        cmd.Parameters.AddWithValue("@Commentaire", stc.Commentaire ?? "");
                        cmd.Parameters.AddWithValue("@DateCreation", now);
                        cmd.Parameters.AddWithValue("@DateModification", now);

                        await cmd.ExecuteNonQueryAsync();
                        id = (int)conn.LastInsertRowId;
                        stc.Id = id;
                    }

                    // Mise à jour de la fiche du salarié si demandée
                    if (marquerEmployeSorti)
                    {
                        using (var cmd = conn.CreateCommand())
                        {
                            cmd.Transaction = tx;
                            cmd.CommandText = @"
                                UPDATE Employees
                                   SET StatutSalarie = 1,
                                       DateSortie = @DateSortie,
                                       MotifDepart = @MotifDepart
                                 WHERE EmployeeId = @EmployeeId;";
                            cmd.Parameters.AddWithValue("@DateSortie", DbValue.DateToDb(stc.DateSortie));
                            cmd.Parameters.AddWithValue("@MotifDepart", stc.MotifLibelle);
                            cmd.Parameters.AddWithValue("@EmployeeId", stc.EmployeeId);
                            await cmd.ExecuteNonQueryAsync();
                        }
                    }

                    tx.Commit();

                    // Audit
                    await _auditService.EnregistrerAsync(
                        "Validation",
                        "SoldeToutCompte",
                        id.ToString(),
                        "StatutSalarie",
                        "Actif",
                        $"Sorti ({stc.MotifLibelle})",
                        $"Enregistrement Solde de Tout Compte #{id} pour {stc.NomSalarie} - Net payé : {stc.TotalNet:N0} FCFA",
                        utilisateur);

                    // Solde des prêts en cours : leur capital restant dû vient d'être
                    // retenu en une fois sur ce solde de tout compte (DeductionsPrets,
                    // voir PreparerSoldeAsync/ObtenirDettePretsRestanteAsync ci-dessus).
                    // Sans ce qui suit, le prêt restait « En cours » indéfiniment en base,
                    // avec ses échéances futures toujours actives : un rapport « Situation
                    // des prêts » continuait de le montrer comme dû, et si le dossier était
                    // rouvert plus tard sur ce salarié (réembauche, ou correction), rien
                    // n'empêchait ses échéances restantes d'être retenues une seconde fois
                    // — signalé le 16 septembre 2026 (voir ETAT-DEVELOPPEMENT.md item 22).
                    // Fait APRÈS la validation du commit ci-dessus, et non dans la même
                    // transaction : PretService.AnnulerAsync ouvre sa propre connexion et
                    // sa propre transaction d'écriture, que SQLite ne peut pas imbriquer
                    // dans celle, encore ouverte, de ce solde. Une éventuelle erreur ici
                    // n'annule donc pas le solde déjà enregistré (qui, lui, a bien réservé
                    // la somme due) : elle est seulement journalisée, pour ne pas laisser
                    // croire à l'utilisateur que l'enregistrement du solde a échoué et
                    // risquer une double saisie.
                    if (marquerEmployeSorti)
                    {
                        try
                        {
                            var prets = await _pretService.ChargerAsync();
                            var pretsAregler = prets.Where(p =>
                                string.Equals(p.EmployeeId, stc.EmployeeId, StringComparison.OrdinalIgnoreCase) &&
                                (p.Statut == StatutPret.EnCours || p.Statut == StatutPret.Suspendu)).ToList();

                            string motifSolde = $"Soldé au départ — Solde de tout compte #{id}";
                            foreach (var pret in pretsAregler)
                            {
                                await _pretService.AnnulerAsync(pret, motifSolde);
                            }
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine(
                                $"Solde de tout compte #{id} : échec du solde automatique des prêts de " +
                                $"{stc.NomSalarie} — à clôturer manuellement depuis l'écran « Prêts et avances ». " +
                                $"Détail : {ex.Message}");
                        }
                    }

                    return id;
                }
                catch
                {
                    tx.Rollback();
                    throw;
                }
            }
        }

        /// <summary>
        /// Charge tous les soldes de tout compte enregistrés dans le dossier.
        /// </summary>
        public async Task<List<SoldeToutCompte>> ChargerSoldesAsync()
        {
            var liste = new List<SoldeToutCompte>();

            using (var conn = _sqliteService.CreateConnection())
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = $"SELECT {Colonnes} FROM SoldesToutCompte ORDER BY DateSortie DESC, Id DESC;";
                using (var reader = (SQLiteDataReader)await cmd.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                    {
                        liste.Add(LireSolde(reader));
                    }
                }
            }

            return liste;
        }

        /// <summary>
        /// Charge le solde de tout compte le plus récent pour un salarié spécifique.
        /// </summary>
        public async Task<SoldeToutCompte> ChargerSoldeParEmployeAsync(string employeeId)
        {
            if (string.IsNullOrWhiteSpace(employeeId)) return null;

            using (var conn = _sqliteService.CreateConnection())
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = $"SELECT {Colonnes} FROM SoldesToutCompte WHERE EmployeeId = @emp ORDER BY DateSortie DESC LIMIT 1;";
                cmd.Parameters.AddWithValue("@emp", employeeId);
                using (var reader = (SQLiteDataReader)await cmd.ExecuteReaderAsync())
                {
                    if (await reader.ReadAsync())
                        return LireSolde(reader);
                }
            }
            return null;
        }

        /// <summary>
        /// Supprime un solde de tout compte enregistré.
        /// </summary>
        public async Task<bool> SupprimerSoldeAsync(int id, string utilisateur = "Système")
        {
            if (id <= 0) return false;

            using (var conn = _sqliteService.CreateConnection())
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "DELETE FROM SoldesToutCompte WHERE Id = @id;";
                cmd.Parameters.AddWithValue("@id", id);
                int rows = await cmd.ExecuteNonQueryAsync();

                if (rows > 0)
                {
                    await _auditService.EnregistrerAsync(
                        "Suppression",
                        "SoldeToutCompte",
                        id.ToString(),
                        null,
                        null,
                        null,
                        $"Suppression du solde de tout compte #{id}",
                        utilisateur);
                }
                return rows > 0;
            }
        }

        private static SoldeToutCompte LireSolde(SQLiteDataReader reader)
        {
            return new SoldeToutCompte
            {
                Id = DbValue.IntFromDb(reader["Id"]),
                EmployeeId = DbValue.TextFromDb(reader["EmployeeId"]),
                NomSalarie = DbValue.TextFromDb(reader["NomSalarie"]),
                DateSortie = DbValue.DateFromDb(reader["DateSortie"], DateTime.Today),
                Motif = (MotifDepart)DbValue.IntFromDb(reader["Motif"]),
                DateEntree = DbValue.DateFromDb(reader["DateEntree"], DateTime.Today),
                AncienneteAnnees = DbValue.IntFromDb(reader["AncienneteAnnees"]),
                AncienneteMois = DbValue.IntFromDb(reader["AncienneteMois"]),
                Categorie = DbValue.TextFromDb(reader["Categorie"]),
                Echelon = DbValue.TextFromDb(reader["Echelon"]),
                Fonction = DbValue.TextFromDb(reader["Fonction"]),
                SalaireReference = DbValue.MoneyFromDb(reader["SalaireReference"]),
                SalairePresence = DbValue.MoneyFromDb(reader["SalairePresence"]),
                JoursPresence = DbValue.DecimalFromDb(reader["JoursPresence"]),
                JoursCongesDus = DbValue.DecimalFromDb(reader["JoursCongesDus"]),
                IndemniteConges = DbValue.MoneyFromDb(reader["IndemniteConges"]),
                DureePreavisMois = DbValue.IntFromDb(reader["DureePreavisMois"]),
                StatutPreavis = (StatutPreavis)DbValue.IntFromDb(reader["StatutPreavis"]),
                IndemnitePreavis = DbValue.MoneyFromDb(reader["IndemnitePreavis"]),
                IndemniteLicenciement = DbValue.MoneyFromDb(reader["IndemniteLicenciement"]),
                TauxLicenciementApplique = DbValue.DecimalFromDb(reader["TauxLicenciementApplique"]),
                Indemnite13emeMois = DbValue.MoneyFromDb(reader["Indemnite13emeMois"]),
                Mois13emeMois = DbValue.DecimalFromDb(reader["Mois13emeMois"]),
                IndemnitesDiverses = DbValue.MoneyFromDb(reader["IndemnitesDiverses"]),
                LibelleIndemnitesDiverses = DbValue.TextFromDb(reader["LibelleIndemnitesDiverses"]),
                DeductionsPrets = DbValue.MoneyFromDb(reader["DeductionsPrets"]),
                DeductionsAutres = DbValue.MoneyFromDb(reader["DeductionsAutres"]),
                LibelleDeductionsAutres = DbValue.TextFromDb(reader["LibelleDeductionsAutres"]),
                EstimationIUTS = DbValue.MoneyFromDb(reader["EstimationIUTS"]),
                EstimationCNSS = DbValue.MoneyFromDb(reader["EstimationCNSS"]),
                Commentaire = DbValue.TextFromDb(reader["Commentaire"]),
                DateCreation = DbValue.DateFromDb(reader["DateCreation"], DateTime.Now),
                DateModification = DbValue.DateFromDb(reader["DateModification"], DateTime.Now)
            };
        }
    }
}
