using System;
using IUTSCalculator.Models;

namespace IUTSCalculator.Services
{
    /// <summary>
    /// Service de génération du bulletin de paie mensuel d'un employé, conforme au
    /// modèle de référence (fichier "model_buletin.xlsx", onglet "Option 4") et au
    /// moteur de calcul IUTS (<see cref="TaxCalculatorService.CalculateMonthlyTax"/>).
    /// </summary>
    public class BulletinService
    {
        private readonly TaxCalculatorService _taxCalculatorService;

        public BulletinService(TaxCalculatorService taxCalculatorService)
        {
            _taxCalculatorService = taxCalculatorService ?? throw new ArgumentNullException(nameof(taxCalculatorService));
        }

        /// <summary>
        /// Génère le bulletin de paie d'un employé pour une période (mois) donnée.
        /// </summary>
        /// <param name="employee">Employé concerné</param>
        /// <param name="periode">Une date quelconque du mois concerné (seul le mois/année compte)</param>
        /// <param name="congesPrisJours">Nombre de jours de congé pris pendant la période</param>
        /// <param name="modeReglement">Mode de règlement (ex. "Virement bancaire", "Espèces", "Chèque")</param>
        /// <param name="numeroCompte">Numéro de compte de règlement (optionnel)</param>
        public Bulletin GenererBulletin(
            Employee employee,
            DateTime periode,
            decimal congesPrisJours = 0m,
            string modeReglement = "Virement bancaire",
            string numeroCompte = null)
        {
            if (employee == null) throw new ArgumentNullException(nameof(employee));

            var calcul = _taxCalculatorService.CalculateMonthlyTax(employee);

            DateTime periodeDebut = new DateTime(periode.Year, periode.Month, 1);
            DateTime periodeFin = periodeDebut.AddMonths(1).AddDays(-1);

            DateTime? dateEmbauche = employee.DateEntree != default(DateTime) ? employee.DateEntree : (DateTime?)null;
            int ancienneteMois = CalculerAncienneteMois(dateEmbauche, periodeFin);
            decimal congesAcquis = ancienneteMois * 2.5m;
            decimal congesCumules = congesAcquis - congesPrisJours;

            // IUTS brut (avant réduction pour charges familiales) : IUTSNet = IUTSBrut - AbattementChargesFamiliales
            decimal iutsBrut = calcul.IUTSNet + calcul.FamilyChargeReduction;

            // Informations de l'entreprise émettrice (dossier actuellement ouvert), affichées
            // dans l'en-tête du bulletin (à gauche), avec l'emplacement pour son logo.
            var dossier = DossierSession.Instance.CurrentDossier;

            // Cumuls depuis le 1er janvier : à défaut d'un historique de paie mensuel détaillé,
            // on estime le cumul en répétant la rémunération du mois en cours sur les mois
            // écoulés de l'année (bornés au mois d'embauche si l'employé n'a pas travaillé
            // toute l'année). Le bulletin indique clairement qu'il s'agit d'une estimation.
            int moisEcoulesAnnee = periodeFin.Month;
            if (dateEmbauche.HasValue && dateEmbauche.Value.Year == periodeFin.Year)
                moisEcoulesAnnee = periodeFin.Month - dateEmbauche.Value.Month + 1;
            moisEcoulesAnnee = Math.Max(1, moisEcoulesAnnee);

            decimal cumulBrutAnnuel = calcul.SalaireBrut * moisEcoulesAnnee;
            decimal cumulIUTSAnnuel = calcul.IUTSNet * moisEcoulesAnnee;
            decimal cumulCnssEmployeAnnuel = calcul.CnssEmployee * moisEcoulesAnnee;
            decimal cumulNetAnnuel = calcul.SalaireNetaPayer * moisEcoulesAnnee;

            // Numéro de contrôle unique du bulletin (protection contre les faux bulletins) :
            // calculé à partir des données visibles sur le document, il peut être recalculé
            // à tout moment pour vérifier qu'un bulletin n'a pas été falsifié.
            DateTime dateGeneration = DateTime.Now;
            string numeroControle = QrCodeService.GenererNumeroControle(
                $"{employee.EmployeeId}|{periodeFin:yyyyMM}|{calcul.SalaireNetaPayer}|{dateGeneration:yyyyMMddHHmmss}");

            return new Bulletin
            {
                EntrepriseRaisonSociale = dossier?.RaisonSociale,
                EntrepriseAdresse = dossier?.Adresse,
                EntrepriseVille = dossier?.Ville,
                EntrepriseTelephone = dossier?.Telephone,
                EntrepriseIFU = dossier?.IFU,
                EntrepriseRCCM = dossier?.RCCM,
                EntrepriseRegimeFiscal = dossier?.RegimeFiscal,
                EntrepriseLogoPath = dossier?.LogoPath,

                EmployeeId = employee.EmployeeId,
                NomEmploye = employee.LastName,
                PrenomEmploye = employee.FirstName,
                Emploi = employee.Fonction,
                Categorie = employee.Category,
                DateEmbauche = dateEmbauche,
                PeriodeDebut = periodeDebut,
                PeriodeFin = periodeFin,
                AncienneteMois = ancienneteMois,
                CongesAcquisJours = congesAcquis,
                CongesPrisJours = congesPrisJours,
                CongesCumulesJours = congesCumules,
                ChargesFamiliales = employee.FamilyCharges,
                ConjointTravaille = employee.IsSpouseWorking,

                SalaireDeBase = employee.BaseSalary,
                IndemniteLogement = employee.HousingAllowance,
                IndemniteTransport = employee.TransportAllowance,
                IndemniteFonction = employee.FunctionAllowance,
                IndemniteSujetion = employee.SujetionAllowance,
                SalaireBrut = calcul.SalaireBrut,

                CnssEmploye = calcul.CnssEmployee,
                CnssEmployeur = calcul.CnssPartPatronale,
                TotalExonerations = calcul.ExemptAmount,
                AbattementForfaitaire = calcul.ProfessionalDeduction,
                BaseIUTS = calcul.BaseIUTSHorsDeduction,
                IUTSBrut = iutsBrut,
                AbattementChargesFamiliales = calcul.FamilyChargeReduction,
                NetIUTS = calcul.IUTSNet,
                RetenueObligatoire = calcul.RetenueSpeciale,

                SalaireNetAPayer = calcul.SalaireNetaPayer,

                ModeReglement = modeReglement,
                NumeroCompte = numeroCompte,

                CumulBrutAnnuel = cumulBrutAnnuel,
                CumulIUTSAnnuel = cumulIUTSAnnuel,
                CumulCnssEmployeAnnuel = cumulCnssEmployeAnnuel,
                CumulNetAnnuel = cumulNetAnnuel,
                CumulsEstimes = true,

                NumeroControle = numeroControle,
                DateGeneration = dateGeneration
            };
        }

        /// <summary>
        /// Calcule l'ancienneté en mois complets entre la date d'embauche et la date de référence.
        /// Retourne 0 si la date d'embauche est inconnue ou postérieure à la date de référence.
        /// </summary>
        private int CalculerAncienneteMois(DateTime? dateEmbauche, DateTime dateReference)
        {
            if (dateEmbauche == null || dateEmbauche.Value > dateReference)
                return 0;

            var d = dateEmbauche.Value;
            int mois = (dateReference.Year - d.Year) * 12 + (dateReference.Month - d.Month);
            if (dateReference.Day < d.Day)
                mois--;

            return Math.Max(0, mois);
        }
    }
}
